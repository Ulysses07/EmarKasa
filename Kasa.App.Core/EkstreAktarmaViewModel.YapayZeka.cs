using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

// Yapay zekâ önerileri (docs/specs/2026-11-25-yapay-zeka-onerileri.md): yalnız düğmeyle sorulur; öneri satırı doldurur, seçmez ve kaydetmez.
public partial class EkstreAktarmaViewModel
{
    private readonly TekrarAnahtari _yapayZekaKey = new(), _olcumKey = new();
    /// <summary>Sunucudaki durum; sunucu desteklemiyorsa ya da alınamadıysa null (bölüm görünmez).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YapayZekaGorunur), nameof(YapayZekaAcik), nameof(YapayZekaMetni))]
    private YapayZekaDurumDto? _yapayZeka;
    /// <summary>Sor düğmesinin altındaki sonuç ya da hata.</summary>
    [ObservableProperty] private string? _yapayZekaMesaji;
    [ObservableProperty] private string? _yapayZekaHatasi;
    [ObservableProperty] private string? _olcumHatasi;
    public ObservableCollection<string> GonderilecekSatirlar { get; } = new();
    [ObservableProperty][NotifyPropertyChangedFor(nameof(GonderilecekDugmesi))] private bool _gonderilecekAcik;
    public string GonderilecekDugmesi => GonderilecekAcik ? "Gönderilecek metni gizle" : "Gönderilecek metni göster";

    public bool YapayZekaGorunur => YapayZeka is not null;
    public bool YapayZekaAcik => YapayZeka?.Acik == true;
    /// <summary>Kural ve hafızanın tanımadığı, önerisi olmayan bekleyen satırlar.</summary>
    private int TaninmayanSayisi => Satirlar.Count(s => s.Secilebilir && s.Oneri?.Durum == "Yok");
    /// <summary>Sorulmuş (ya da yalnız ad ve sayıdan oluşan) satırları kalan belge: tanınmayan satır sayısı artmadıkça yeniden sorulmaz.</summary>
    private (int Belge, int Satir)? _cevapsiz;
    public bool YapayZekaSorulabilir => YapayZekaAcik && Belge is { Kaynak: not EkstreKaynaklari.Erp } b && TaninmayanSayisi is > 0 and var sayi
        && !(_cevapsiz is { } c && c.Belge == b.Id && c.Satir >= sayi);
    public string YapayZekaSorDugmesi => $"Tanınmayan {TaninmayanSayisi} satırı yapay zekâya sor";
    /// <summary>Gönder onayının metni: gönderilecek ve gönderilmeyecek satırlar, en yüksek maliyet.</summary>
    public string YapayZekaOnayi(YapayZekaOnizlemeDto o)
    {
        var gonderilmeyen = TaninmayanSayisi - o.SatirSayisi - o.KalanSatir;
        return $"{o.SatirSayisi} satırın maskeli açıklaması{(o.OrnekSayisi > 0 ? $" ve {o.OrnekSayisi} geçmiş kararın maskeli örneği" : "")} Anthropic'e gönderilecek."
            + $" En çok {YapayZekaMetinleri.Dolar(o.EnYuksekMaliyetUsd)} tutar."
            + (gonderilmeyen > 0 ? $" Yalnız ad ve sayıdan oluşan {gonderilmeyen} satır gönderilmez." : "")
            + (o.KalanSatir > 0 ? $" {o.KalanSatir} satır sonraki soruya kalır." : "");
    }
    public string YapayZekaMetni => YapayZeka is not { } y ? ""
        : !y.Acik ? y.KapaliNedeni ?? "Kapalı."
        : string.Join("\n", new[]
        {
            $"Bu ay {YapayZekaMetinleri.Dolar(y.AyHarcamasiUsd)} / {YapayZekaMetinleri.Dolar(y.AylikSinirUsd)} · {y.AyCagri} çağrı · {y.Model}",
            y.Sonuclanan == 0 ? "Henüz sonuçlanan öneri yok." : $"Önerisi sonuçlanan {y.Sonuclanan} satırın {y.AyniTur} tanesi önerilen türde kapandı ({YapayZekaMetinleri.Yuzde(y.AyniTur, y.Sonuclanan)}).",
        }.Concat(y.Olcumler.Select(YapayZekaMetinleri.Olcum)));

    private void YapayZekayiBildir()
    {
        foreach (var ad in new[] { nameof(YapayZekaSorulabilir), nameof(YapayZekaSorDugmesi) })
            OnPropertyChanged(ad);
    }
    partial void OnYapayZekaChanged(YapayZekaDurumDto? value) => YapayZekayiBildir();

    /// <summary>Durum alınamazsa (eski sunucu, bağlantı) bölüm gizlenir; ekstre işlemeye devam edilir.</summary>
    private async Task YapayZekaDurumunuAlAsync(int n)
    {
        if (!EditorMu || !Gecerli(n))
            return;
        YapayZekaDurumDto? durum = null;
        try
        { durum = await api.YapayZekaDurumuAsync(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException || e is KasaApiException { DurumKodu: not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) }) { }
        if (Gecerli(n))
            YapayZeka = durum;
    }

    /// <summary>Gönderilecek maskeli metin (hiçbir şey gönderilmez).</summary>
    public async Task<YapayZekaOnizlemeDto?> YapayZekaOnizleAsync()
    {
        YapayZekaOnizlemeDto? sonuc = null;
        await YurutAsync(async n =>
        {
            YapayZekaHatasi = null;
            if (!EditorMu || !VeriHazir || !YapayZekaSorulabilir || Belge is not { } belge)
                return;
            var onizleme = await api.YapayZekaOnizlemeAsync(belge.Id);
            if (!Gecerli(n) || Belge?.Id != belge.Id)
                return;
            TakipMetni.Doldur(GonderilecekSatirlar, onizleme.Satirlar);
            if (onizleme.SatirSayisi == 0)
            {
                YapayZekaMesaji = "Tanınmayan satırlar yalnız ad ve sayıdan oluşuyor; gönderilecek bilgi yok.";
                _cevapsiz = (belge.Id, TaninmayanSayisi);
                YapayZekayiBildir();
                return;
            }
            sonuc = onizleme;
        }, hataIsle: e => YapayZekaHatasi = HataMesaji(e));
        return sonuc;
    }

    [RelayCommand]
    private async Task GonderilecekAcKapatAsync()
    {
        if (GonderilecekAcik)
        { GonderilecekAcik = false; return; }
        GonderilecekAcik = await YapayZekaOnizleAsync() is not null;
    }

    /// <summary>Tanınmayan satırları sorar (sayfa önce onay ister). Aynı istek yeniden gönderilirse ikinci kez ücret doğmaz.</summary>
    public Task YapayZekaSorAsync() => YurutAsync(async n =>
    {
        YapayZekaMesaji = null;
        YapayZekaHatasi = null;
        if (!EditorMu || !VeriHazir || !YapayZekaSorulabilir || Belge is not { } belge)
            return;
        YapayZekaSonucDto sonuc;
        try
        { sonuc = await api.YapayZekaSorAsync(belge.Id, new(_yapayZekaKey.Al(new { belge.Id, belge.Surum }))); }
        catch (KasaApiException) { _yapayZekaKey.Temizle(); throw; }
        _yapayZekaKey.Temizle();
        if (!Gecerli(n))
            return;
        GonderilecekAcik = false;
        GonderilecekSatirlar.Clear();
        if (Belge?.Id == belge.Id)
            await KurallariAlAsync(n);
        if (!Gecerli(n))
            return;
        _cevapsiz = sonuc is { Not: null, KalanSatir: 0 } ? (belge.Id, TaninmayanSayisi) : null;
        YapayZekayiBildir();
        YapayZekaMesaji = sonuc.Not is { } not ? $"{not} ({YapayZekaMetinleri.Dolar(sonuc.MaliyetUsd)})"
            : $"{sonuc.Sorulan} satır soruldu, {sonuc.Onerilen} satıra öneri geldi ({YapayZekaMetinleri.Dolar(sonuc.MaliyetUsd)}). Öneriler satırları doldurdu; kontrol edip kendiniz işaretleyin."
                + (sonuc.KalanSatir > 0 ? $" {sonuc.KalanSatir} satır kaldı; yeniden sorabilirsiniz." : "");
        await YapayZekaDurumunuAlAsync(n);
    }, mesgulkenBildir: true, hataIsle: e => YapayZekaHatasi = HataMesaji(e));

    /// <summary>Geçmiş kararların yarısıyla doğruluk ölçümü (sayfa önce onay ister).</summary>
    public Task YapayZekaOlcAsync(string kaynak) => YurutAsync(async n =>
    {
        OlcumHatasi = null;
        if (!EditorMu || !VeriHazir || !YapayZekaAcik)
            return;
        try
        { await api.YapayZekaOlcAsync(new(_olcumKey.Al(kaynak), kaynak)); }
        catch (KasaApiException) { _olcumKey.Temizle(); throw; }
        _olcumKey.Temizle();
        await YapayZekaDurumunuAlAsync(n);
    }, mesgulkenBildir: true, hataIsle: e => OlcumHatasi = HataMesaji(e));

    private void YapayZekayiTemizle()
    {
        YapayZeka = null;
        YapayZekaMesaji = null;
        YapayZekaHatasi = null;
        OlcumHatasi = null;
        GonderilecekAcik = false;
        GonderilecekSatirlar.Clear();
        _yapayZekaKey.Temizle();
        _olcumKey.Temizle();
        _cevapsiz = null;
    }
}

public static class YapayZekaMetinleri
{
    private static readonly System.Globalization.CultureInfo Tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
    /// <summary>Küçük tutarlar da görünür: "0,0026 $", "2,00 $".</summary>
    public static string Dolar(decimal usd) => usd.ToString(usd is > 0 and < 0.1m ? "0.0000" : "0.00", Tr) + " $";
    public static string Yuzde(int pay, int payda) => payda == 0 ? "—" : "%" + decimal.Round(100m * pay / payda, 0, MidpointRounding.AwayFromZero).ToString("0", Tr);
    public static string Olcum(YapayZekaOlcumDto o) =>
        $"{(o.Kaynak == EkstreKaynaklari.Kart ? "Kart" : "Banka")} ölçümü ({o.Zaman.LocalDateTime:dd.MM.yyyy}): {o.Satir} karar · tür {Yuzde(o.TurDogru, o.Satir)} · dağılım {Yuzde(o.DagilimDogru, o.DagilimSatiri)}"
        + $" · yüksek güvenlide tür {Yuzde(o.YuksekDogru, o.YuksekGuven)} · {o.Onerisiz} karara öneri yok · {Dolar(o.MaliyetUsd)}";
}
