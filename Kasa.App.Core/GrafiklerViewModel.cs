using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// 04 · Grafikler: seçilen aya kadar son 12 ay, kanal (ya da toplam) geliri veya ay sonucu; her ay
/// geçen yılın aynı ayıyla yan yana. Birim: TL, reel TL (TÜFE), USD, EUR, gram altın. Kur/endeks
/// Ayarlar'daki aylık tablodandır; olmayan ay "kur yok" gösterir (hiçbir değer tahmin edilmez).
/// Çizim sayfada (GraphicsView); buradaki veri birim testlidir.
/// </summary>
public partial class GrafiklerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;

    public GrafiklerViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        var bugun = BugunTarih;
        _yil = bugun.Year;
        _ay = bugun.Month;
        foreach (var o in new[] { GrafikOlcusu.Gelir, GrafikOlcusu.AySonucu }) OlcuCipleri.Add(new SecimCipi(GrafikVerisi.OlcuAdi(o)));
        foreach (var b in Enum.GetValues<GrafikBirimi>()) BirimCipleri.Add(new SecimCipi(GrafikVerisi.BirimAdi(b)));
        KanalCipleri.Add(new SecimCipi(GrafikVerisi.Toplam));
        Vurgula();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AyEtiketi))]
    private int _yil;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AyEtiketi))]
    private int _ay;
    public string AyEtiketi => KasaDokumuGorunum.AyEtiketi(Yil, Ay);

    public ObservableCollection<SecimCipi> KanalCipleri { get; } = new();
    public ObservableCollection<SecimCipi> OlcuCipleri { get; } = new();
    public ObservableCollection<SecimCipi> BirimCipleri { get; } = new();

    [ObservableProperty] private string _kanal = GrafikVerisi.Toplam;
    [ObservableProperty] private GrafikOlcusu _olcu = GrafikOlcusu.Gelir;
    [ObservableProperty] private GrafikBirimi _birim = GrafikBirimi.NominalTl;

    /// <summary>Çizilecek çubuklar (eskiden yeniye) ve dikey eksen aralığı.</summary>
    public IReadOnlyList<GrafikCubugu> Cubuklar { get; private set; } = [];
    public decimal EnKucuk { get; private set; }
    public decimal EnBuyuk { get; private set; } = 1m;

    /// <summary>Tablo (en yeni önce): değer, geçen yıl, değişim; kur yoksa "kur yok".</summary>
    public ObservableCollection<GrafikSatiri> Satirlar { get; } = new();

    [ObservableProperty] private string _baslik = "";
    /// <summary>Birimin açıklaması (reel TL tabanı, kur kaynağı) ve eksik kur uyarısı.</summary>
    [ObservableProperty] private string _birimNotu = "";
    [ObservableProperty] private int _kurYokSayisi;

    /// <summary>Veri ya da seçim değişince artar; sayfa GraphicsView'u yeniden çizer.</summary>
    [ObservableProperty] private int _cizimSurumu;

    private GrafikDto? _veri;
    private int _surum;

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var surum = ++_surum;
        int yil = Yil, ay = Ay;
        GrafikDto g;
        try { g = await _api.GrafikAsync(yil, ay); }
        catch when (surum != _surum) { return; }
        if (surum != _surum) return;
        _veri = g;
        KanallariKur(g);
        Yenile();
    });

    private void KanallariKur(GrafikDto g)
    {
        var adlar = new[] { GrafikVerisi.Toplam }.Concat(g.Kanallar).ToList();
        if (!adlar.SequenceEqual(KanalCipleri.Select(c => c.Ad)))
        {
            KanalCipleri.Clear();
            foreach (var a in adlar) KanalCipleri.Add(new SecimCipi(a));
        }
        if (!adlar.Contains(Kanal)) Kanal = GrafikVerisi.Toplam;
        Vurgula();
    }

    /// <summary>Seçimlerden çubukları, tabloyu ve notları yeniden kurar (sunucuya gitmez).</summary>
    private void Yenile()
    {
        var g = _veri;
        Cubuklar = g is null ? [] : GrafikVerisi.Cubuklar(g, Kanal, Olcu, Birim);
        (EnKucuk, EnBuyuk) = GrafikVerisi.Olcek(Cubuklar);
        Satirlar.Clear();
        foreach (var s in GrafikVerisi.Satirlar(Cubuklar, Birim)) Satirlar.Add(s);
        KurYokSayisi = Cubuklar.Count(c => c.KurYok) + Cubuklar.Count(c => c.GecenYilKurYok);
        Baslik = $"{Kanal} · {GrafikVerisi.OlcuAdi(Olcu)} · {GrafikVerisi.BirimAdi(Birim)}";
        BirimNotu = Not(g);
        OnPropertyChanged(nameof(Cubuklar));
        CizimSurumu++;
    }

    private string Not(GrafikDto? g)
    {
        var not = Birim switch
        {
            GrafikBirimi.ReelTl => g is not null && GrafikVerisi.TufeReferansi(g) is { } r
                ? $"Reel TL: {KasaDokumuGorunum.AyEtiketi(r.Yil, r.Ay)} fiyatlarıyla (TÜFE endeksi oranı)."
                : "Reel TL için Ayarlar'daki kur tablosuna TÜFE endeksi girin.",
            GrafikBirimi.Usd => "USD: ayın ortalama USD/TRY kuruyla (Ayarlar'daki kur tablosu).",
            GrafikBirimi.Eur => "EUR: ayın ortalama EUR/TRY kuruyla (Ayarlar'daki kur tablosu).",
            GrafikBirimi.AltinGram => "Gram altın: ayın gram altın fiyatıyla (Ayarlar'daki kur tablosu).",
            _ => "Nominal TL (enflasyondan arındırılmamış).",
        };
        if (KurYokSayisi > 0) not += $" {KurYokSayisi} ay için kur yok; o aylar çizilmez.";
        return not;
    }

    private void Vurgula()
    {
        foreach (var c in KanalCipleri) c.Secili = c.Ad == Kanal;
        foreach (var c in OlcuCipleri) c.Secili = c.Ad == GrafikVerisi.OlcuAdi(Olcu);
        foreach (var c in BirimCipleri) c.Secili = c.Ad == GrafikVerisi.BirimAdi(Birim);
    }

    [RelayCommand]
    private void SecKanal(SecimCipi s)
    {
        Kanal = s.Ad;
        Vurgula();
        Yenile();
    }

    [RelayCommand]
    private void SecOlcu(SecimCipi s)
    {
        Olcu = s.Ad == GrafikVerisi.OlcuAdi(GrafikOlcusu.AySonucu) ? GrafikOlcusu.AySonucu : GrafikOlcusu.Gelir;
        Vurgula();
        Yenile();
    }

    [RelayCommand]
    private void SecBirim(SecimCipi s)
    {
        Birim = Enum.GetValues<GrafikBirimi>().FirstOrDefault(b => GrafikVerisi.BirimAdi(b) == s.Ad);
        Vurgula();
        Yenile();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OncekiAy()
    {
        if (Ay == 1) { Ay = 12; Yil--; } else Ay--;
        return YukleAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SonrakiAy()
    {
        if (Ay == 12) { Ay = 1; Yil++; } else Ay++;
        return YukleAsync();
    }
}
