using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <param name="zaman">"Tamamlanmış ay" denetiminin saati (yerel gün); verilmezse sistem saati. DI'da kayıtlı değildir (isteğe
/// bağlı parametre varsayılana düşer); testler sabit saat verir.</param>
public partial class AyKilidiViewModel(IAylikGiderApi api, AuthViewModel auth, TimeProvider? zaman = null) : OturumluViewModel(auth)
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
    private readonly TekrarAnahtari _anahtar = new();
    private AyKilidiDto? _durum;
    public int? DurumSurumu => _durum?.Surum;
    public ObservableCollection<AyKilidiSatiri> Gecmis { get; } = new();
    [ObservableProperty] private string _durumMetni = "Ay kilidi bilgisi alınmadı.";
    public Task YukleAsync() => YurutAsync(async n => { VeriHazir = false; var s = await api.AyKilidiAsync(); if (Gecerli(n)) Uygula(s); });
    private void Uygula(AyKilidiDto s)
    {
        _durum = s;
        DurumMetni = s.KilitliSonTarih is { } tarih ? $"{tarih:dd.MM.yyyy} dahil geçmiş mali hareketler kilitli." : "Kilitli ay yok.";
        TakipMetni.Doldur(Gecmis, s.Gecmis.Select(x => new AyKilidiSatiri(x)));
        Tamamlandi();
    }
    /// <summary>Kilit değişikliği onayının metni (maui-8: görünüm yalnız diyaloğu gösterir). <paramref name="rapor"/> ekrandaki
    /// aylık rapordur; kural 1 ile dondurulmuş ay açılırken güncel kuralla yeniden hesaplanacağı eklenir (web
    /// frozenRuleUnlockWarning ile aynı uyarı).</summary>
    public string OnayMetni(bool kapat, int yil, int ay, AylikRaporDto? rapor = null)
    {
        if (kapat)
            return $"{ay:00}.{yil} ayının sonuna kadar bütün geçmiş mali hareketler kilitlenecek. Okumalar ve gelecek planlar devam eder.";
        var metin = $"{ay:00}.{yil} ayı ve sonraki aylar yeniden açılacak. Bu dönemlerin mali hareketleri değiştirilebilir olacak.";
        return rapor is { Dondurulmus: true, KuralSurumu: null or < 2 } r && r.Yil == yil && r.Ay == ay ? metin + "\n" + EskiKuralUyarisi : metin;
    }
    /// <summary>K4: kural 1 ile dondurulmuş ayın görüntüsü kilit açılınca silinir; rapor güncel kuralla (kural 2) hesaplanır
    /// ve yeniden kapatınca öyle dondurulur.</summary>
    public const string EskiKuralUyarisi = "Bu ay eski kuralla (kural 1) kapatılmış: raporunda takipli kredi çekimi Gelen ve Ay sonucu içindedir. Kilit açılınca rapor güncel kuralla yeniden hesaplanır, yeniden kapatınca da güncel kuralla dondurulur: takipli kredi çekimi Gelen ve Ay sonucundan çıkar, eski kuraldaki rakamlara dönülemez. Eski kuralla kapatılmış sonraki aylar için de aynısı geçerlidir.";
    /// <summary>Onay diyaloğu açıkken oturum ya da ekrandaki rapor ayı değiştiyse istek artık gösterilen aya ait değildir.</summary>
    public bool IstekHalaGecerli(int oturum, int yil, int ay, int raporYil, int raporAy) => Gecerli(oturum) && yil == raporYil && ay == raporAy;
    public Task DegistirAsync(bool kapat, int yil, int ay, string aciklama, int onayOturumu, int onaySurumu) => YurutAsync(async n =>
    {
        if (!EditorMu || !Gecerli(onayOturumu) || !VeriHazir || _durum is null)
            return;
        if (_durum.Surum != onaySurumu)
        { Hata = "Ay kilidi değişti. Güncel durumu inceleyip yeniden onaylayın."; return; }
        if (string.IsNullOrWhiteSpace(aciklama))
        { Hata = "Ay kilidi değişikliği için açıklama yazın."; return; }
        var ilk = new DateOnly(yil, ay, 1);
        var bugun = DateOnly.FromDateTime(_zaman.GetLocalNow().DateTime);
        if (kapat && ilk.AddMonths(1) > bugun)
        { Hata = "Yalnız tamamlanmış aylar kapatılabilir."; return; }
        var g = new AyKilidiYaz(Guid.Empty, _durum.Surum, yil, ay, aciklama.Trim());
        g = g with { IstekId = _anahtar.Al(new { kapat, g }) };
        var sonuc = await api.AyKilidiDegistirAsync(kapat, g);
        if (!Gecerli(n))
            return;
        Uygula(sonuc);
        _anahtar.Temizle();
        Mesaj = kapat ? "Seçilen ayın sonuna kadar geçmiş kilitlendi." : "Seçilen ay ve sonraki aylar açıldı.";
    });
    protected override void OturumTemizle() { _durum = null; Gecmis.Clear(); DurumMetni = "Ay kilidi bilgisi alınmadı."; _anahtar.Temizle(); }
}
public record AyKilidiSatiri(AyKilidiOlayDto Veri)
{
    public string Baslik => $"{Veri.Zaman.LocalDateTime:dd.MM.yyyy HH:mm} · " + (Veri.YeniSonTarih is { } t ? $"{t:dd.MM.yyyy} tarihine kadar kilitli" : "Kilit kaldırıldı");
    public string Ozet => Veri.Aciklama;
}
