using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kur tablosunun satırı (boş alan "kur yok").</summary>
public sealed record KurSatiri(KurDto Dto)
{
    public string AyEtiketi => KasaDokumuGorunum.AyEtiketi(Dto.Ay.Year, Dto.Ay.Month);
    public string TufeMetni => Metin(Dto.TufeEndeksi);
    public string UsdMetni => Metin(Dto.UsdTry);
    public string EurMetni => Metin(Dto.EurTry);
    public string AltinMetni => Metin(Dto.AltinGramTry);
    private static string Metin(decimal? d) => d is { } v ? v.ToString("#,##0.####", Kultur.Turkce) : GrafikVerisi.KurYokMetni;
}

/// <summary>
/// Paket B · Ayarlar'da aylık kur/endeks tablosu (grafiklerin reel TL, USD, EUR ve altın birimleri için).
/// Editör ayı seçip TÜFE endeksi, USD/TRY, EUR/TRY ve gram altını girer. "TCMB'den doldur" yalnız
/// USD ve EUR'yu ayın iş günü ortalamasıyla (TCMB döviz satış) doldurur; TÜFE ve altın elle girilir.
/// </summary>
public partial class AyarlarViewModel
{
    public ObservableCollection<KurSatiri> Kurlar { get; } = new();

    /// <summary>Formdaki ay (ayın ilk günü). Varsayılan: geçen ay (son biten ay).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KurAyEtiketi))]
    private DateOnly _kurAy;
    public string KurAyEtiketi => KurAy == default ? "" : KasaDokumuGorunum.AyEtiketi(KurAy.Year, KurAy.Month);

    [ObservableProperty] private string _kurTufe = "";
    [ObservableProperty] private string _kurUsd = "";
    [ObservableProperty] private string _kurEur = "";
    [ObservableProperty] private string _kurAltin = "";
    [ObservableProperty] private string? _kurBilgi;

    private DateOnly VarsayilanKurAyi()
    {
        var b = BugunTarih.AddMonths(-1);
        return new DateOnly(b.Year, b.Month, 1);
    }

    /// <summary>Kur tablosunu yükler (sayfa açılışında ana yüklemeden sonra çağrılır).</summary>
    public Task KurlariYukleAsync()
    {
        KurBilgi = null;
        if (KurAy == default) KurAy = VarsayilanKurAyi();
        return CalistirAsync(KurlariDoldurAsync);
    }

    private async Task KurlariDoldurAsync()
    {
        var liste = await _api.KurlarAsync();
        Kurlar.Clear();
        foreach (var k in liste.OrderByDescending(k => k.Ay)) Kurlar.Add(new KurSatiri(k));
        FormuDoldur();
    }

    /// <summary>Formu seçili ayın kayıtlı satırıyla doldurur (satır yoksa boş).</summary>
    private void FormuDoldur()
    {
        var k = Kurlar.FirstOrDefault(s => s.Dto.Ay == KurAy)?.Dto;
        KurTufe = KurGiris.Bicimle(k?.TufeEndeksi);
        KurUsd = KurGiris.Bicimle(k?.UsdTry);
        KurEur = KurGiris.Bicimle(k?.EurTry);
        KurAltin = KurGiris.Bicimle(k?.AltinGramTry);
    }

    [RelayCommand]
    private void KurOncekiAy()
    {
        KurAy = (KurAy == default ? VarsayilanKurAyi() : KurAy).AddMonths(-1);
        KurBilgi = null;
        FormuDoldur();
    }

    [RelayCommand]
    private void KurSonrakiAy()
    {
        KurAy = (KurAy == default ? VarsayilanKurAyi() : KurAy).AddMonths(1);
        KurBilgi = null;
        FormuDoldur();
    }

    [RelayCommand]
    private void KurDuzenle(KurSatiri s)
    {
        KurAy = s.Dto.Ay;
        KurBilgi = null;
        FormuDoldur();
    }

    private static decimal? Coz(string metin, string alan)
    {
        var r = KurGiris.Ayristir(metin);
        if (!r.Gecerli) throw new DogrulamaHatasi($"{alan}: {r.Hata}");
        return r.Deger;
    }

    /// <summary>Ayın satırını kaydeder; tüm alanlar boşsa satır silinir.</summary>
    [RelayCommand]
    private Task KurKaydetAsync() => CalistirAsync(async () =>
    {
        KurBilgi = null;
        var ay = KurAy == default ? VarsayilanKurAyi() : KurAy;
        var g = new KurDto(ay, Coz(KurTufe, "TÜFE endeksi"), Coz(KurUsd, "USD/TRY"), Coz(KurEur, "EUR/TRY"), Coz(KurAltin, "Gram altın"));
        await _api.KurKaydetAsync(g);
        bool bos = g.TufeEndeksi is null && g.UsdTry is null && g.EurTry is null && g.AltinGramTry is null;
        await KurlariDoldurAsync();
        KurBilgi = bos
            ? $"{KasaDokumuGorunum.AyEtiketi(ay.Year, ay.Month)} kur satırı silindi."
            : $"{KasaDokumuGorunum.AyEtiketi(ay.Year, ay.Month)} kurları kaydedildi.";
    });

    /// <summary>
    /// USD/TRY ve EUR/TRY'yi TCMB'den ayın iş günü ortalamasıyla doldurur ve kaydeder. Formda
    /// kaydedilmemiş TÜFE/altın metni korunur (kaydetmek için "Kurları kaydet").
    /// </summary>
    [RelayCommand]
    private Task KurTcmbDoldurAsync() => CalistirAsync(async () =>
    {
        KurBilgi = null;
        var ay = KurAy == default ? VarsayilanKurAyi() : KurAy;
        string tufe = KurTufe, altin = KurAltin;
        var r = await _api.KurTcmbDoldurAsync(ay);
        await KurlariDoldurAsync();
        KurTufe = tufe;
        KurAltin = altin;
        KurUsd = KurGiris.Bicimle(r.Kur.UsdTry);
        KurEur = KurGiris.Bicimle(r.Kur.EurTry);
        KurBilgi = $"TCMB: {r.IlkGun.ToString("d MMM", Kultur.Turkce)} – {r.SonGun.ToString("d MMM yyyy", Kultur.Turkce)} arası "
                   + $"{r.GunSayisi} iş günü döviz satış ortalaması kaydedildi. TÜFE ve gram altın elle girilir.";
    });
}
