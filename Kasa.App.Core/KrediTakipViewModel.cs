using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KrediTakipViewModel(IFinansTakipApi api, IKasaApi finans, AuthViewModel auth) : OturumluViewModel(auth)
{
    private readonly TekrarAnahtari _kayit = new(), _taksit = new(), _kapat = new(), _durum = new(), _gecis = new();
    private KrediGecisYaz? _onizlenenGecis;
    private bool _gecisUygun;
    public ObservableCollection<KrediTakipSatiri> Krediler { get; } = new();
    public ObservableCollection<TaksitSatiri> Taksitler { get; } = new();
    public ObservableCollection<TakipKanalSecimi> Kanallar { get; } = new();
    [ObservableProperty] private KrediTakipDto? _secili;
    [ObservableProperty] private string _ad = "";
    [ObservableProperty] private decimal _cekilenTutar;
    [ObservableProperty] private DateTime _cekimTarihi = DateTime.Today;
    [ObservableProperty] private DateTime _ilkTaksitTarihi = DateTime.Today.AddMonths(1);
    [ObservableProperty] private int _taksitSayisi = 12;
    [ObservableProperty] private decimal _aylikOdeme;
    [ObservableProperty] private bool _mevcutKredi;
    [ObservableProperty] private TaksitSatiri? _duzenlenenTaksit;
    [ObservableProperty] private DateTime _taksitTarihi = DateTime.Today;
    [ObservableProperty] private decimal _taksitTutari;
    [ObservableProperty] private string _taksitNotu = "";
    [ObservableProperty] private bool _taksitIptal;
    [ObservableProperty] private string _gerekce = "";
    [ObservableProperty] private DateTime _kapatmaTarihi = DateTime.Today;
    [ObservableProperty] private decimal _kapatmaTutari;
    [ObservableProperty] private bool _kapatmaOnay;
    [ObservableProperty] private DateTime _gecisTarihi = DateTime.Today;
    [ObservableProperty] private string _gecisAciklama = "";
    [ObservableProperty] private string? _gecisOnizleme;
    [ObservableProperty] private bool _gecisOnay;
    public bool YeniKredi => Secili is null;
    public bool KrediSecili => Secili is not null;
    public bool YeniTakip => Secili?.YeniTakip == true;
    public bool EskiTakip => Secili is { YeniTakip: false };
    public bool TaksitDuzenlenebilir => DuzenlenenTaksit?.Veri.Durum == "Bekliyor";
    public string KrediOzeti => Secili is { } k ? new KrediTakipSatiri(k).Ozet + "\n" + TakipMetni.Paylar(k.KanalPaylari) : "Yeni kredi bilgilerini girin.";
    public string KapatmaOzeti => $"{KapatmaTarihi:dd.MM.yyyy} tarihinde {Bicim.Tl(KapatmaTutari)} ₺ kasa çıkışı kaydedilir; yerine geçen ileri taksitler iptal edilir. Bankanın bildirdiği kapama tutarını kullanın.";
    partial void OnSeciliChanged(KrediTakipDto? value) { foreach (var p in new[] { nameof(YeniKredi), nameof(KrediSecili), nameof(YeniTakip), nameof(EskiTakip), nameof(KrediOzeti) }) OnPropertyChanged(p); }
    partial void OnDuzenlenenTaksitChanged(TaksitSatiri? value) => OnPropertyChanged(nameof(TaksitDuzenlenebilir));
    partial void OnKapatmaTutariChanged(decimal value) { KapatmaOnay = false; OnPropertyChanged(nameof(KapatmaOzeti)); }
    partial void OnKapatmaTarihiChanged(DateTime value) { KapatmaOnay = false; OnPropertyChanged(nameof(KapatmaOzeti)); }
    public Task YukleAsync() => YurutAsync(async n =>
    {
        var kanallar = await finans.KanallarAsync(); var krediler = await api.TakipKredilerAsync();
        if (!Gecerli(n)) return;
        TakipMetni.Doldur(Kanallar, kanallar.Select(k => new TakipKanalSecimi(k))); TakipMetni.Doldur(Krediler, krediler.Select(k => new KrediTakipSatiri(k)));
        if (Secili is { } eski) { var mevcut = krediler.FirstOrDefault(k => k.Id == eski.Id); if (mevcut is not null) Sec(new(mevcut)); else Yeni(); }
        Tamamlandi();
    });
    [RelayCommand] private void Sec(KrediTakipSatiri satir)
    {
        Secili = satir.Veri; TakipMetni.Doldur(Taksitler, Secili.Taksitler.OrderBy(t => t.Tarih).ThenBy(t => t.No).Select(t => new TaksitSatiri(t)));
        foreach (var k in Kanallar) k.Secili = Secili.KanalPaylari.Any(p => p.KanalId == k.Veri.Id);
        DuzenlenenTaksit = null; GecisOnizleme = null; GecisOnay = KapatmaOnay = false; _onizlenenGecis = null; _gecisUygun = false; Gerekce = "";
    }
    [RelayCommand] private void Yeni()
    {
        Secili = null; Ad = ""; CekilenTutar = AylikOdeme = 0; TaksitSayisi = 12; MevcutKredi = false;
        CekimTarihi = DateTime.Today; IlkTaksitTarihi = DateTime.Today.AddMonths(1); Taksitler.Clear(); DuzenlenenTaksit = null;
        foreach (var k in Kanallar) k.Secili = false;
        GecisOnizleme = null; GecisOnay = KapatmaOnay = false; _onizlenenGecis = null;
    }
    [RelayCommand] private void TumKanallariSec() { foreach (var k in Kanallar) k.Secili = k.Veri.Aktif; }
    private IReadOnlyList<int> SecilenKanallar() => Kanallar.Where(k => k.Secili).Select(k => k.Veri.Id).ToList();
    private bool Uygula(KrediTakipDto sonuc, int n)
    {
        if (!Gecerli(n)) return false;
        var eski = Krediler.FirstOrDefault(k => k.Veri.Id == sonuc.Id); if (eski is not null) Krediler[Krediler.IndexOf(eski)] = new(sonuc); else Krediler.Add(new(sonuc));
        Secili = sonuc; TakipMetni.Doldur(Taksitler, sonuc.Taksitler.OrderBy(t => t.Tarih).ThenBy(t => t.No).Select(t => new TaksitSatiri(t))); Tamamlandi(); return true;
    }
    [RelayCommand] private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not null) return;
        var kanallar = SecilenKanallar();
        if (string.IsNullOrWhiteSpace(Ad) || CekilenTutar <= 0 || AylikOdeme <= 0 || TaksitSayisi is < 1 or > 600 || kanallar.Count == 0) { Hata = "Kredi adı, pozitif tutarlar, taksit sayısı ve en az bir kanal seçin."; return; }
        var g = new KrediTakipYaz(Guid.Empty, Ad.Trim(), CekilenTutar, DateOnly.FromDateTime(CekimTarihi), DateOnly.FromDateTime(IlkTaksitTarihi), TaksitSayisi, AylikOdeme, kanallar, MevcutKredi);
        g = g with { IstekId = _kayit.Al(g) };
        if (Uygula(await api.TakipKrediKaydetAsync(g), n)) { _kayit.Temizle(); Mesaj = g.MevcutKredi ? "Mevcut kredi takibe alındı; ikinci kredi girişi oluşturulmadı." : "Kredi kaydedildi; genel kasa ve seçilen kanal payları aynı hareketi gösterir."; }
    });
    [RelayCommand] private void TaksitSec(TaksitSatiri satir)
    {
        DuzenlenenTaksit = satir; TaksitTarihi = satir.Veri.Tarih.ToDateTime(TimeOnly.MinValue); TaksitTutari = satir.Veri.Tutar; TaksitNotu = satir.Veri.Not ?? ""; TaksitIptal = satir.Veri.Durum == "Iptal"; Gerekce = "";
    }
    private bool GerekceVar() { if (!string.IsNullOrWhiteSpace(Gerekce)) return true; Hata = "İşlem gerekçesini yazın."; return false; }
    [RelayCommand] private Task TaksitKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kredi || DuzenlenenTaksit is not { } taksit || !GerekceVar()) return;
        if (taksit.Veri.Durum != "Bekliyor" && (TaksitTarihi != taksit.Veri.Tarih.ToDateTime(TimeOnly.MinValue) || TaksitTutari != taksit.Veri.Tutar || TaksitIptal != (taksit.Veri.Durum == "Iptal"))) { Hata = "İşlenmiş taksidin tarih ve tutarı değişmez; yalnız not ekleyebilirsiniz."; return; }
        var g = new KrediTaksitYaz(Guid.Empty, kredi.Surum, DateOnly.FromDateTime(TaksitTarihi), TaksitTutari, TaksitNotu, TaksitIptal, Gerekce.Trim());
        g = g with { IstekId = _taksit.Al(new { kredi.Id, TaksitId = taksit.Veri.Id, g }) };
        if (Uygula(await api.TakipTaksitKaydetAsync(kredi.Id, taksit.Veri.Id, g), n)) { _taksit.Temizle(); DuzenlenenTaksit = null; Mesaj = "Taksit bilgisi kaydedildi. Not eklemek ikinci kasa çıkışı oluşturmaz."; }
    });
    [RelayCommand] private Task KapatAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kredi || !GerekceVar()) return;
        if (!KapatmaOnay || KapatmaTutari <= 0) { Hata = "Bankanın kapama tutarını girin ve kasa etkisini onaylayın."; return; }
        var g = new KrediKapatYaz(Guid.Empty, kredi.Surum, DateOnly.FromDateTime(KapatmaTarihi), KapatmaTutari, Gerekce.Trim()); g = g with { IstekId = _kapat.Al(new { kredi.Id, g }) };
        if (Uygula(await api.TakipKrediKapatAsync(kredi.Id, g), n)) { _kapat.Temizle(); KapatmaOnay = false; Mesaj = "Erken kapama kaydedildi; ileri taksitler geçmişiyle korundu."; }
    });
    public Task DurumDegistirAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is null || !GerekceVar()) return;
        var g = new TakipDurumYaz(Guid.Empty, Secili.Surum, !Secili.Aktif, Gerekce.Trim()); g = g with { IstekId = _durum.Al(new { Secili.Id, g }) };
        if (Uygula(await api.TakipKrediDurumAsync(Secili.Id, g), n)) { _durum.Temizle(); Mesaj = "Kredinin arşiv durumu değişti; mali geçmiş korundu."; }
    });
    private KrediGecisYaz GecisGovde()
    {
        var g = new KrediGecisYaz(Guid.Empty, Secili!.Surum, DateOnly.FromDateTime(GecisTarihi), SecilenKanallar(), GecisAciklama.Trim(), false);
        return g with { IstekId = _gecis.Al(new { Secili.Id, g }) };
    }
    [RelayCommand] private Task GecisOnizleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: false } kredi) return;
        _onizlenenGecis = null; _gecisUygun = false; GecisOnay = false; GecisOnizleme = null;
        var g = GecisGovde(); var sonuc = await api.TakipKrediGecisOnizlemeAsync(kredi.Id, g);
        if (!Gecerli(n) || Secili?.Id != kredi.Id || !TakipMetni.Ayni(g, GecisGovde())) return;
        _onizlenenGecis = g; _gecisUygun = sonuc.KabulEdilebilir; GecisOnizleme = TakipMetni.Gecis(sonuc);
    });
    [RelayCommand] private Task GecisiOnaylaAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: false } kredi) return;
        var g = GecisGovde();
        if (!GecisOnay || !_gecisUygun || _onizlenenGecis is null || !TakipMetni.Ayni(g, _onizlenenGecis)) { Hata = "Güncel geçiş önizlemesini inceleyip onay kutusunu işaretleyin."; return; }
        g = g with { Onay = true };
        if (Uygula(await api.TakipKrediGecisAsync(kredi.Id, g), n)) { _gecis.Temizle(); GecisOnay = false; GecisOnizleme = null; Mesaj = "Yeni kredi takibi açıldı; eski çekim yeniden gelir yazılmadı."; }
    });
    protected override void OturumTemizle()
    {
        Krediler.Clear(); Kanallar.Clear(); Yeni(); Gerekce = GecisAciklama = TaksitNotu = ""; KapatmaTutari = TaksitTutari = 0;
        foreach (var key in new[] { _kayit, _taksit, _kapat, _durum, _gecis }) key.Temizle();
    }
}

public partial class TakipOzetViewModel(IFinansTakipApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    public ObservableCollection<TakipOlaySatiri> Olaylar { get; } = new();
    [ObservableProperty] private int _gun = 30;
    [ObservableProperty] private string _ozet = "";
    [ObservableProperty] private IReadOnlyList<TakipKanalPayi>? _kanalKartBorclari;
    [ObservableProperty] private string _belirsizBorcOzeti = "";
    [RelayCommand] public Task YukleAsync() => YurutAsync(async n =>
    {
        VeriHazir = false; KanalKartBorclari = null; var v = await api.TakipOzetAsync(Gun);
        if (!Gecerli(n)) return;
        Ozet = $"Toplam kart borcu {Bicim.Tl(v.KartBorcu)} ₺ · kalan planlı kredi ödemesi {Bicim.Tl(v.KalanKrediPlani)} ₺" + (v.KartAlacakBakiyesi > 0 ? $"\nKart alacak bakiyesi: {Bicim.Tl(v.KartAlacakBakiyesi)} ₺. Diğer kartların borcundan düşülmez." : "");
        KanalKartBorclari = v.KanalKartBorclari;
        BelirsizBorcOzeti = v.KanalKartBorclari is null ? "Kanallara göre kart borcu bilgisi alınamadı." : $"Dağılım bekleyen kart borcu: {Bicim.Tl(v.KanalKartBorclari.Where(p => p.KanalId is null).Sum(p => Math.Max(0, p.Tutar)))} ₺";
        TakipMetni.Doldur(Olaylar, v.Olaylar.Select(o => new TakipOlaySatiri(o, v.Tarih))); Tamamlandi();
    });
    protected override void OturumTemizle() { Olaylar.Clear(); Ozet = ""; KanalKartBorclari = null; BelirsizBorcOzeti = ""; }
}
