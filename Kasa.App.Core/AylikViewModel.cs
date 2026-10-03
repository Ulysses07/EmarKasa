using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AylikViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public AylikViewModel(IKasaApi api, BaglantiDurumu? baglanti = null) : base(baglanti)
    {
        _api = api;
        var bugun = DateTime.Today;
        _yil = bugun.Year;
        _ay = bugun.Month;
    }

    [ObservableProperty] private int _yil;
    [ObservableProperty] private int _ay;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DagilimBekliyor))]
    [NotifyPropertyChangedFor(nameof(GenelGiderVar))]
    [NotifyPropertyChangedFor(nameof(GenelGelirVar))]
    [NotifyPropertyChangedFor(nameof(GenelAySonucu))]
    [NotifyPropertyChangedFor(nameof(KrediGirisiVar))]
    [NotifyPropertyChangedFor(nameof(KrediGirisiToplam))]
    [NotifyPropertyChangedFor(nameof(AySonucuBasligi))]
    [NotifyPropertyChangedFor(nameof(Dondurulmus))]
    [NotifyPropertyChangedFor(nameof(DondurulmusMetni))]
    [NotifyPropertyChangedFor(nameof(VeriSagligiUyarisi))]
    [NotifyPropertyChangedFor(nameof(VeriSagligiUyarisiVar))]
    private AylikRaporDto? _rapor;
    public bool DagilimBekliyor => Rapor?.DagilimBekleyenTutar > 0;
    public bool GenelGiderVar => Rapor?.GenelGider != 0 && Rapor is not null;
    public bool GenelGelirVar => Rapor?.GenelGelir != 0 && Rapor is not null;
    /// <summary>Kanal ay sonuçları ve genel kalemler. Kural 2'de kredi girişini içermez (K2); kural 1 ile dondurulmuş ayda takipli
    /// kredi çekimi kanal ay sonucunun içindedir.</summary>
    public decimal GenelAySonucu => Rapor is { } r ? r.Kanallar.Sum(k => k.AySonucu) - r.DagilimBekleyenTutar - r.GenelGider + r.GenelGelir : 0;

    /// <summary>K2: ayın kredi girişi (takipli ve eski kredi çekimi); kasaya girer, ay sonucuna dahil değildir. Kural 1 ile dondurulmuş
    /// ayda sunucu bu toplamı vermez (kredi Gelen'in içindedir).</summary>
    public decimal KrediGirisiToplam => Rapor?.KrediGirisi ?? 0;
    public bool KrediGirisiVar => Rapor?.KrediGirisi is { } k && k != 0;
    /// <summary>Kanal satırı başlığı: kural 2'de ay sonucu kredi hariçtir. LblSection stili TextTransform uygulamaz (Türkçe
    /// büyük harf kuralını bozar; T-04); metin burada zaten büyük harfle yazılır.</summary>
    public string AySonucuBasligi => Rapor?.KuralSurumu is >= 2 ? "AY SONUCU (KREDİ HARİÇ)" : "AY SONUCU";
    /// <summary>K4: kapatılmış ayın raporu kapatıldığı andaki haliyle gelir; sonraki kural değişiklikleri onu etkilemez.</summary>
    public bool Dondurulmus => Rapor?.Dondurulmus == true;
    public string? DondurulmusMetni => Rapor is { Dondurulmus: true } r
        ? "Bu ay kapatıldı: rapor kapatıldığı andaki haliyle gösteriliyor, sonraki kural değişiklikleri bu ayı etkilemez."
          + (r.KuralSurumu is null or < 2 ? " Bu raporda takipli kredi çekimi Gelen ve Ay sonucu içindedir." : "")
        : null;
    /// <summary>K1: tutarları değiştirmeyen veri sağlığı uyarısı (ör. takip başlangıcından önce tarihli giderler).</summary>
    public string? VeriSagligiUyarisi => Rapor?.VeriSagligiUyarisi;
    public bool VeriSagligiUyarisiVar => !string.IsNullOrWhiteSpace(Rapor?.VeriSagligiUyarisi);

    public override Task YukleAsync()
    {
        var yil = Yil;
        var ay = Ay;
        return RaporYukleAsync(() => _api.AylikAsync(yil, ay), rapor => Rapor = rapor);
    }

    partial void OnYilChanged(int value) => AyDegisti();
    partial void OnAyChanged(int value) => AyDegisti();

    /// <summary>Başka aya geçilince önceki ayın raporu bu ayınki gibi gösterilmez (son veri yalnız aynı ayın yenilemesinde korunur).</summary>
    private void AyDegisti()
    {
        if (Rapor is not { } r || (r.Yil == Yil && r.Ay == Ay))
            return;
        Rapor = null;
        VeriVar = false;
        VeriEski = false;
    }

    [RelayCommand]
    private Task OncekiAy()
    {
        if (Ay == 1)
        { Ay = 12; Yil--; }
        else
            Ay--;
        return YukleAsync();
    }

    [RelayCommand]
    private Task SonrakiAy()
    {
        if (Ay == 12)
        { Ay = 1; Yil++; }
        else
            Ay++;
        return YukleAsync();
    }
}
