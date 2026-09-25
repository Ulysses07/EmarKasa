using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AlislarViewModel
{
    private readonly TekrarAnahtari _duzeltmeAnahtari = new();
    private readonly TekrarAnahtari _iptalAnahtari = new();
    public ObservableCollection<BelgeDto> Belgeler { get; } = new();
    public ObservableCollection<AlisSatiri> DuzeltmeHedefleri { get; } = new();
    public ObservableCollection<OdemeKartiSecenegi> DuzeltmeKartlari { get; } = new();
    [ObservableProperty] private AlisOdemeSatiri? _duzeltilecekOdeme;
    [ObservableProperty] private DateTime _duzeltmeTarihi = DateTime.Today;
    [ObservableProperty] private decimal _duzeltmeTutari;
    [ObservableProperty] private OdemeKartiSecenegi? _duzeltmeKarti;
    [ObservableProperty] private AlisSatiri? _hedefAlis;
    [ObservableProperty] private string _duzeltmeAciklamasi = "";
    [ObservableProperty] private bool _eskiKartHarcamasi;
    public bool DuzeltmeAcik => DuzeltilecekOdeme is not null && EditorMu;
    partial void OnDuzeltilecekOdemeChanged(AlisOdemeSatiri? value) => OnPropertyChanged(nameof(DuzeltmeAcik));
    public void IdIleSec(int id) { var satir = Alislar.FirstOrDefault(a => a.Veri.Id == id); if (satir is not null) Sec(satir); }
    [RelayCommand] private void OdemeDuzelt(AlisOdemeSatiri odeme)
    {
        if (!EditorMu || Mesgul) return;
        if (KaydedilmemisDegisiklikVar) { KaydetmeUyarisi(); return; }
        DuzeltilecekOdeme = odeme; DuzeltmeTarihi = odeme.Veri.Tarih.ToDateTime(TimeOnly.MinValue); DuzeltmeTutari = odeme.Veri.Tutar;
        EskiKartHarcamasi = odeme.Veri.KrediKartiId is null && _giderler.Any(g => g.Id == odeme.Veri.IslemId && g.Tip == GiderTipi.KrediKarti);
        DuzeltmeKartlari.Clear(); DuzeltmeKartlari.Add(new(null, EskiKartHarcamasi ? "Eski kart harcamasını koru" : "Nakit / banka"));
        foreach (var kart in OdemeKartlari.Where(k => k.Id is not null)) DuzeltmeKartlari.Add(kart);
        DuzeltmeKarti = DuzeltmeKartlari.FirstOrDefault(k => k.Id == odeme.Veri.KrediKartiId);
        HedefAlis = null; DuzeltmeAciklamasi = "";
        _duzeltmeAnahtari.Temizle(); _iptalAnahtari.Temizle();
    }
    [RelayCommand] private void DuzeltmedenVazgec() { DuzeltilecekOdeme = null; HedefAlis = null; }
    [RelayCommand] private void HedefiTemizle() => HedefAlis = null;
    [RelayCommand] private Task OdemeDuzeltKaydetAsync() => YurutAsync(async n =>
    {
        if (_odemelerApi is null || !EditorMu || _secili is null || DuzeltilecekOdeme is null) return;
        if (KaydedilmemisDegisiklikVar) { KaydetmeUyarisi(); return; }
        if (DuzeltmeTutari <= 0 || string.IsNullOrWhiteSpace(DuzeltmeAciklamasi)) { Hata = "Pozitif ödeme tutarı ve düzeltme açıklaması girin."; return; }
        var g = new AlisOdemeDuzeltYaz(_secili.Surum, Guid.Empty, DateOnly.FromDateTime(DuzeltmeTarihi), DuzeltmeTutari, DuzeltmeKarti?.Id,
            null, DuzeltmeAciklamasi.Trim(), HedefAlis?.Veri.Id, HedefAlis?.Veri.Surum);
        g = g with { IstekId = _duzeltmeAnahtari.Al(new { AlisId = _secili.Id, OdemeId = DuzeltilecekOdeme.Veri.Id, g }) };
        var sonuc = await _odemelerApi.AlisOdemeDuzeltAsync(_secili.Id, DuzeltilecekOdeme.Veri.Id, g);
        if (!SonucuUygula(sonuc, n)) return;
        _duzeltmeAnahtari.Temizle();
        var tumu = await _api.AlislarAsync();
        if (!Gecerli(n)) return;
        Degistir(Alislar, tumu.Select(a => new AlisSatiri(a))); SeciliyiGoster(sonuc); GiderSecenekleriniYenile();
        OnPropertyChanged(nameof(DagilimBekleyenTutar)); OnPropertyChanged(nameof(DagilimBekliyor));
        Mesaj = g.HedefAlisId is null ? "Ödeme düzeltildi; gerekçe işlem geçmişine kaydedildi." : "Ödeme seçilen alışa taşındı; ikinci gider oluşturulmadı.";
    });
    public Task OdemeIptalAsync() => YurutAsync(async n =>
    {
        if (_odemelerApi is null || !EditorMu || _secili is null || DuzeltilecekOdeme is null) return;
        if (KaydedilmemisDegisiklikVar) { KaydetmeUyarisi(); return; }
        if (string.IsNullOrWhiteSpace(DuzeltmeAciklamasi)) { Hata = "İptal nedenini açıklama alanına yazın."; return; }
        var g = new AlisOdemeIptalYaz(_secili.Surum, Guid.Empty, DuzeltmeAciklamasi.Trim());
        var id = DuzeltilecekOdeme.Veri.Id;
        g = g with { IstekId = _iptalAnahtari.Al(new { _secili.Id, OdemeId = id, g }) };
        if (!SonucuUygula(await _odemelerApi.AlisOdemeIptalAsync(_secili.Id, id, g), n)) return;
        _iptalAnahtari.Temizle();
        var giderler = await _finans.IslemlerAsync();
        if (!Gecerli(n)) return;
        _giderler = giderler; GiderSecenekleriniYenile();
        Mesaj = "Ödeme ve bağlı gider iptal edildi. İptal gerekçesi geçmişte korundu.";
    });
    [RelayCommand] public Task BelgeleriYukleAsync() => YurutAsync(async n =>
    {
        if (_yonetim is null || _secili is null) return;
        var id = _secili.Id;
        var belgeler = await _yonetim.BelgelerAsync(id);
        if (Gecerli(n) && _secili?.Id == id) Degistir(Belgeler, belgeler);
    });
    public Task BelgeYukleAsync(string ad, string tur, byte[] icerik, int? odemeId) => YurutAsync(async n =>
    {
        if (_yonetim is null || _secili is null) return;
        if (icerik.Length > 10 * 1024 * 1024 || icerik.Length == 0) { Hata = "Belge boş olamaz ve 10 MB sınırını aşamaz."; return; }
        var id = _secili.Id;
        var belge = await _yonetim.BelgeYukleAsync(id, ad, tur, icerik, EditorMu ? odemeId : null);
        if (Gecerli(n) && _secili?.Id == id) { Belgeler.Add(belge); Mesaj = "Belge eklendi."; }
    });
    public async Task<IndirilenDosya?> BelgeIndirAsync(BelgeDto belge)
    {
        IndirilenDosya? dosya = null;
        await YurutAsync(async n => { if (_yonetim is null) return; var d = await _yonetim.BelgeIndirAsync(belge.Id); if (Gecerli(n)) dosya = d; });
        return dosya;
    }
    public Task BelgeSilAsync(BelgeDto belge) => YurutAsync(async n =>
    {
        if (_yonetim is null) return;
        await _yonetim.BelgeSilAsync(belge.Id); if (Gecerli(n)) Belgeler.Remove(belge);
    });
}
