using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AyKilidiViewModel(IAylikGiderApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    private readonly TekrarAnahtari _anahtar = new();
    private AyKilidiDto? _durum;
    public int? DurumSurumu => _durum?.Surum;
    public ObservableCollection<AyKilidiSatiri> Gecmis { get; } = new();
    [ObservableProperty] private string _durumMetni = "Ay kilidi bilgisi alınmadı.";
    public Task YukleAsync() => YurutAsync(async n => { VeriHazir = false; var s = await api.AyKilidiAsync(); if (Gecerli(n)) Uygula(s); });
    private void Uygula(AyKilidiDto s)
    {
        _durum = s; DurumMetni = s.KilitliSonTarih is { } tarih ? $"{tarih:dd.MM.yyyy} dahil geçmiş mali hareketler kilitli." : "Kilitli ay yok.";
        TakipMetni.Doldur(Gecmis, s.Gecmis.Select(x => new AyKilidiSatiri(x))); Tamamlandi();
    }
    public Task DegistirAsync(bool kapat, int yil, int ay, string aciklama, int onayOturumu, int onaySurumu) => YurutAsync(async n =>
    {
        if (!EditorMu || !Gecerli(onayOturumu) || !VeriHazir || _durum is null) return;
        if (_durum.Surum != onaySurumu) { Hata = "Ay kilidi değişti. Güncel durumu inceleyip yeniden onaylayın."; return; }
        if (string.IsNullOrWhiteSpace(aciklama)) { Hata = "Ay kilidi değişikliği için açıklama yazın."; return; }
        var ilk = new DateOnly(yil, ay, 1); var bugun = DateOnly.FromDateTime(DateTime.Today);
        if (kapat && ilk.AddMonths(1) > bugun) { Hata = "Yalnız tamamlanmış aylar kapatılabilir."; return; }
        var g = new AyKilidiYaz(Guid.Empty, _durum.Surum, yil, ay, aciklama.Trim()); g = g with { IstekId = _anahtar.Al(new { kapat, g }) };
        var sonuc = await api.AyKilidiDegistirAsync(kapat, g); if (!Gecerli(n)) return;
        Uygula(sonuc); _anahtar.Temizle(); Mesaj = kapat ? "Seçilen ayın sonuna kadar geçmiş kilitlendi." : "Seçilen ay ve sonraki aylar açıldı.";
    });
    protected override void OturumTemizle() { _durum = null; Gecmis.Clear(); DurumMetni = "Ay kilidi bilgisi alınmadı."; _anahtar.Temizle(); }
}
public record AyKilidiSatiri(AyKilidiOlayDto Veri)
{
    public string Baslik => $"{Veri.Zaman.LocalDateTime:dd.MM.yyyy HH:mm} · " + (Veri.YeniSonTarih is { } t ? $"{t:dd.MM.yyyy} tarihine kadar kilitli" : "Kilit kaldırıldı");
    public string Ozet => Veri.Aciklama;
}
