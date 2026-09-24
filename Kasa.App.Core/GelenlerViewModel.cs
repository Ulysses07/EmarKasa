using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kayıtlı geleni olan satıra yeni tutarın nasıl yazılacağı.</summary>
public enum GelenYazmaModu { Secilmedi, UzerineYaz, UstuneEkle }

/// <summary>
/// Gelen tablosu: seçili dönemin (haftanın) bütün aktif kanalları tek ekranda. Yalnız değişen
/// satırlar gönderilir; her satır, kullanıcının gördüğü tutarla korumalı yazılır (başka biri bu arada
/// değiştirdiyse sunucu 409 döner, satır "Üzerine yaz / Üstüne ekle" sorar). Altta geçmiş dönemlerde
/// girilmemiş gelenler listelenir ("MEZAT geleni girilmedi").
/// </summary>
public partial class GelenlerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public GelenlerViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman) => _api = api;

    public static string ModSecMesaji(string kanal) => $"{kanal}: kayıtlı gelen var; 'Üzerine yaz' ya da 'Üstüne ekle' seçin.";
    public const string DegisiklikYokMesaji = "Kaydedilecek değişiklik yok.";

    public ObservableCollection<GelenSatiri> Satirlar { get; } = new();
    public ObservableCollection<EksikGelenSatiri> Eksikler { get; } = new();

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private DateOnly? _donemStart;
    [ObservableProperty] private string _donemMetni = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OncekiVar))]
    private DateOnly? _onceki;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonrakiVar))]
    private DateOnly? _sonraki;
    [ObservableProperty] private decimal _toplam;
    [ObservableProperty] private string? _sonuc;
    [ObservableProperty] private string _eksikOzeti = "";

    public bool OncekiVar => Onceki is not null;
    public bool SonrakiVar => Sonraki is not null;

    private int _surum;

    /// <summary>Sayfa açılışı: aynı dönem yeniden yüklenir, kaydedilmemiş girişler korunur (bkz. Gecis partial).</summary>
    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var eksik = EksikleriYukleAsync();
        await TabloYukleAsync(DonemStart, girisleriKoru: true);
        await eksik;
    });

    private async Task TabloYukleAsync(DateOnly? donem, bool girisleriKoru = false)
    {
        var surum = ++_surum;
        GelenTablosuDto t;
        try { t = await _api.GelenTablosuAsync(donem); }
        catch when (surum != _surum) { return; }
        if (surum != _surum) return;
        var eskiGirisler = girisleriKoru && DonemStart == t.DonemStart ? KaydedilmemisGirisler() : null;
        DonemStart = t.DonemStart;
        Onceki = t.Onceki;
        Sonraki = t.Sonraki;
        DonemMetni = $"{t.DonemStart.ToString("d MMM", Kultur.Turkce)} – {t.DonemEnd.ToString("d MMM yyyy", Kultur.Turkce)}";
        Satirlar.Clear();
        foreach (var h in t.Satirlar)
        {
            var satir = new GelenSatiri(h.Kanal, h.Aktif, h.TutarTl);
            if (eskiGirisler is not null && eskiGirisler.TryGetValue(h.Kanal, out var eski)) satir.GirisiDevral(eski);
            Satirlar.Add(satir);
        }
        ToplamiGuncelle();
    }

    private async Task EksikleriYukleAsync()
    {
        var s = await _api.EksikGelenListesiAsync();
        Eksikler.Clear();
        foreach (var e in s.Kayitlar) Eksikler.Add(new EksikGelenSatiri(e));
        EksikOzeti = s.Toplam switch
        {
            0 => "Geçmiş haftalarda eksik gelen yok.",
            var n when n > s.Kayitlar.Count => $"{n} eksik gelen (en yeni {s.Kayitlar.Count} gösteriliyor)",
            var n => $"{n} eksik gelen",
        };
    }

    private void ToplamiGuncelle() => Toplam = Satirlar.Sum(s => s.Kayitli ?? 0m);

    // Hafta değiştiren komutlar kaydedilmemiş giriş varsa önce sorar (GelenlerViewModel.Gecis.cs).
    [RelayCommand]
    private Task OncekiAsync() => Onceki is { } d ? GecisIsteAsync(d, kaydir: false) : Task.CompletedTask;

    [RelayCommand]
    private Task SonrakiAsync() => Sonraki is { } d ? GecisIsteAsync(d, kaydir: false) : Task.CompletedTask;

    [RelayCommand]
    private Task BuHaftaAsync() => GecisIsteAsync(null, kaydir: false);

    /// <summary>Eksik listesinden o döneme gider; tablo sayfanın başında olduğu için sayfa yukarı kaydırılır.</summary>
    [RelayCommand]
    private Task EksigeGitAsync(EksikGelenSatiri e) => GecisIsteAsync(e.DonemStart, kaydir: true);

    [RelayCommand] private void SecUzerineYaz(GelenSatiri s) => s.Mod = GelenYazmaModu.UzerineYaz;
    [RelayCommand] private void SecUstuneEkle(GelenSatiri s) => s.Mod = GelenYazmaModu.UstuneEkle;

    /// <summary>Yalnız değişen satırları korumalı kaydeder; çakışan satır güncel tutarla yeniden sorar.</summary>
    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        GecisVazgec();                                   // kaydetmeyi seçti: hafta değişimi sorusu kapanır
        Sonuc = null;
        Dogrula(DonemStart is not null, "Dönem yüklenmedi.");
        var donem = DonemStart!.Value;
        var degisen = Satirlar.Where(s => s.GirisVar).ToList();
        foreach (var s in degisen)
        {
            Dogrula(s.GirisHatasi is null, $"{s.Kanal}: {s.GirisHatasi}");
            Dogrula(s.Kayitli is null || s.Mod != GelenYazmaModu.Secilmedi, ModSecMesaji(s.Kanal));
        }
        var gonderilecek = degisen.Where(s => s.YeniTutar != s.Kayitli).ToList();
        foreach (var s in degisen.Except(gonderilecek)) s.GirisiTemizle();
        Dogrula(gonderilecek.Count > 0, DegisiklikYokMesaji);

        int kaydedilen = 0, cakisan = 0;
        foreach (var s in gonderilecek)
        {
            var r = await _api.GelenKorumaliKaydetAsync(new GelenYaz(donem, s.Kanal, s.YeniTutar!.Value), s.Kayitli ?? 0m);
            if (r.Kaydedildi)
            {
                s.Kayitli = r.Gelen?.TutarTl ?? s.YeniTutar;
                s.GirisiTemizle();
                s.Durum = "Kaydedildi";
                kaydedilen++;
            }
            else
            {
                s.Kayitli = r.MevcutTutar;
                s.Mod = GelenYazmaModu.Secilmedi;
                s.Durum = $"Siz açtıktan sonra değişmiş: kayıtlı {Bicim.Tl(r.MevcutTutar)} ₺. 'Üzerine yaz' ya da 'Üstüne ekle' seçip tekrar kaydedin.";
                s.Cakisma = true;
                cakisan++;
            }
        }
        ToplamiGuncelle();
        Sonuc = (kaydedilen > 0 ? $"{kaydedilen} kanal kaydedildi." : "")
                + (cakisan > 0 ? $" {cakisan} kanalda çakışma var: seçim yapıp tekrar kaydedin." : "");
        Sonuc = Sonuc.Trim();
        if (kaydedilen > 0) await EksikleriYukleAsync();
    });
}

/// <summary>Gelen tablosunun kanal satırı.</summary>
public partial class GelenSatiri : ObservableObject
{
    public GelenSatiri(string kanal, bool aktif, decimal? kayitli)
    {
        Kanal = kanal;
        Aktif = aktif;
        _kayitli = kayitli;
    }

    public string Kanal { get; }
    public bool Aktif { get; }

    /// <summary>Kayıtlı tutar (kullanıcının gördüğü); kayıt yoksa null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KayitVar), nameof(KayitliMetni), nameof(YeniTutar), nameof(Onizleme), nameof(ModGerekli))]
    private decimal? _kayitli;

    /// <summary>Girilen tutar metni (Türkçe biçim); boş = değişiklik yok, "0" = sıfır yaz.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GirisVar), nameof(GirisHatasi), nameof(YeniTutar), nameof(Onizleme), nameof(ModGerekli))]
    private string _girisMetni = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YeniTutar), nameof(Onizleme), nameof(UzerineYazSecili), nameof(UstuneEkleSecili), nameof(ModGerekli))]
    private GelenYazmaModu _mod;

    /// <summary>Son kayıt denemesinin sonucu (kaydedildi / çakışma).</summary>
    [ObservableProperty] private string? _durum;
    [ObservableProperty] private bool _cakisma;

    public bool KayitVar => Kayitli is not null;
    public string KayitliMetni => Kayitli is { } k ? Bicim.Tl(k) + " ₺" : "—";
    public bool GirisVar => GirisMetni.Trim().Length > 0;
    public string? GirisHatasi => GirisVar && ParaGiris.Ayristir(GirisMetni) is { Gecerli: false } p ? p.Hata : null;
    public bool UzerineYazSecili => Mod == GelenYazmaModu.UzerineYaz;
    public bool UstuneEkleSecili => Mod == GelenYazmaModu.UstuneEkle;

    /// <summary>Kayıtlı gelen varken girişin nasıl yazılacağı seçilmeli.</summary>
    public bool ModGerekli => GirisVar && KayitVar;

    /// <summary>Kaydedilecek yeni tutar; giriş yoksa/geçersizse ya da mod seçilmemişse null.</summary>
    public decimal? YeniTutar
    {
        get
        {
            if (!GirisVar || ParaGiris.Ayristir(GirisMetni) is not { Gecerli: true } p) return null;
            if (Kayitli is not { } k) return p.Tutar;
            return Mod switch
            {
                GelenYazmaModu.UzerineYaz => p.Tutar,
                GelenYazmaModu.UstuneEkle => k + p.Tutar,
                _ => null,
            };
        }
    }

    /// <summary>Kaydedilince oluşacak tutarın önizlemesi ("→ 15.000,00 ₺").</summary>
    public string? Onizleme => YeniTutar is { } y && y != Kayitli ? $"→ {Bicim.Tl(y)} ₺" : null;

    public void GirisiTemizle()
    {
        GirisMetni = "";
        Mod = GelenYazmaModu.Secilmedi;
        Cakisma = false;
    }

    partial void OnGirisMetniChanged(string value) => Durum = null;

    public static string KayitliDegistiMesaji(decimal? eski, decimal? yeni)
        => $"Siz yazarken kayıtlı tutar değişti ({Metin(eski)} → {Metin(yeni)}). Girişinizi kontrol edip yeniden seçin.";

    private static string Metin(decimal? t) => t is { } d ? Bicim.Tl(d) + " ₺" : "kayıt yok";

    /// <summary>
    /// Aynı dönem yeniden yüklenince kaydedilmemiş girişi yeni satıra taşır. Kayıtlı tutar bu arada
    /// değiştiyse yazma seçimi sıfırlanır ve satır uyarır (yanlış tutarın üstüne eklenmesin).
    /// </summary>
    internal void GirisiDevral(GelenSatiri eski)
    {
        GirisMetni = eski.GirisMetni;
        if (eski.Kayitli == Kayitli)
        {
            Mod = eski.Mod;
            Cakisma = eski.Cakisma;
            Durum = eski.Durum;
        }
        else
        {
            Mod = GelenYazmaModu.Secilmedi;
            Cakisma = true;
            Durum = KayitliDegistiMesaji(eski.Kayitli, Kayitli);
        }
    }
}

/// <summary>Eksik gelen satırı ("MEZAT geleni girilmedi · 14–20 Eyl").</summary>
public sealed class EksikGelenSatiri(EksikGelenSatiriDto d)
{
    public DateOnly DonemStart { get; } = d.DonemStart;
    public string Kanal { get; } = d.Kanal;
    public string Metin { get; } =
        $"{d.Kanal} geleni girilmedi · {d.DonemStart.ToString("d MMM", Kultur.Turkce)} – {d.DonemEnd.ToString("d MMM yyyy", Kultur.Turkce)}";
}
