using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KartTakipViewModel(IFinansTakipApi api, IKasaApi finans, AuthViewModel auth, IBenzerKayitApi? benzerlikApi = null, IKasaKontrolApi? kontrolApi = null) : OturumluViewModel(auth)
{
    public BenzerKayitKontrolu HarcamaBenzerlik { get; } = new(benzerlikApi ?? finans as IBenzerKayitApi);
    public BenzerKayitKontrolu OdemeBenzerlik { get; } = new(benzerlikApi ?? finans as IBenzerKayitApi);
    private readonly TekrarAnahtari _kayit = new(), _harcama = new(), _odeme = new(), _ekstre = new(), _iptal = new(), _durum = new(), _gecis = new();
    private KartTakipOdemeYaz? _onizlenenOdeme;
    private KartGecisYaz? _onizlenenGecis;
    private bool _gecisUygun;
    // Web cardTransition gibi: kullanıcı K'ya dokunmadıkça alan öneriyi izler (kalan borç ile kart borcunun küçüğü,
    // önizlemede sunucunun önerdiği tutar); elle girilen tutar ezilmez.
    private bool _oncedenSayilanElle, _oncedenSayilanOneriliyor;
    public ObservableCollection<KartTakipSatiri> Kartlar { get; } = new();
    public ObservableCollection<KanalDto> Kanallar { get; } = new();
    public ObservableCollection<EkstreSatiri> Ekstreler { get; } = new();
    public ObservableCollection<HarcamaSatiri> Harcamalar { get; } = new();
    public ObservableCollection<HarcamaSatiri> IadeKaynaklari { get; } = new();
    public ObservableCollection<KartOdemeSatiri> Odemeler { get; } = new();
    public ObservableCollection<TakipPayEditor> AcilisPaylari { get; } = new();
    public ObservableCollection<TakipPayEditor> HarcamaPaylari { get; } = new();
    public ObservableCollection<TakipPayEditor> GecisPaylari { get; } = new();
    [ObservableProperty] private KartTakipDto? _secili;
    [ObservableProperty] private string _ad = "";
    [ObservableProperty] private decimal _limit;
    [ObservableProperty] private int _kesimGunu = 1;
    [ObservableProperty] private int _sonOdemeGunu = 10;
    [ObservableProperty] private DateTime _acilisTarihi = DateTime.Today;
    [ObservableProperty] private decimal _acilisBorc;
    [ObservableProperty] private DateTime _harcamaTarihi = DateTime.Today;
    [ObservableProperty] private string _harcamaAciklama = "";
    [ObservableProperty] private decimal _harcamaTutari;
    [ObservableProperty] private HarcamaSatiri? _iadeKaynagi;
    [ObservableProperty] private int _taksitSayisi = 1;
    [ObservableProperty] private bool _ilkKesimVar;
    [ObservableProperty] private DateTime _ilkKesimTarihi = DateTime.Today;
    [ObservableProperty] private DateTime _odemeTarihi = DateTime.Today;
    [ObservableProperty] private decimal _odemeTutari;
    [ObservableProperty] private EkstreSatiri? _odemeEkstresi;
    [ObservableProperty] private string _odemeNotu = "";
    [ObservableProperty] private string? _odemeOnizleme;
    [ObservableProperty] private EkstreSatiri? _duzenlenenEkstre;
    [ObservableProperty] private DateTime _ekstreSonOdeme = DateTime.Today;
    [ObservableProperty] private bool _asgariVar;
    [ObservableProperty] private decimal _asgariTutar;
    [ObservableProperty] private string _gerekce = "";
    [ObservableProperty] private DateTime _gecisTarihi = DateTime.Today;
    [ObservableProperty] private decimal _gecisKalanBorc;
    [ObservableProperty] private decimal _oncedenSayilan;
    [ObservableProperty] private string _gecisAciklama = "";
    [ObservableProperty] private string? _gecisOnizleme;
    [ObservableProperty] private string? _gecisEngeli;
    [ObservableProperty] private bool _gecisOnay;
    public bool KartSecili => Secili is not null;
    public bool YeniKart => Secili is null;
    public bool YeniTakip => Secili?.YeniTakip == true;
    public bool EskiTakip => Secili is { YeniTakip: false };
    // Geçersiz metin (ParaAyristirici.Gecersiz) eksi olsa da iade girişi sayılmaz.
    public bool IadeGirisi => HarcamaTutari < 0 && ParaAyristirici.GecerliMi(HarcamaTutari);
    public bool HarcamaGirisi => !IadeGirisi;
    partial void OnHarcamaTutariChanged(decimal value) { OnPropertyChanged(nameof(IadeGirisi)); OnPropertyChanged(nameof(HarcamaGirisi)); if (value >= 0) IadeKaynagi = null; }
    public string KartOzeti => Secili is { } k ? new KartTakipSatiri(k).Ozet : "Yeni kart bilgilerini girin.";
    public string KanalBorcOzeti => Secili?.KanalKartBorclari is { } paylar ? paylar.Count == 0 ? "Kayıtlı kanal kart borcu yok." : string.Join("\n", paylar.Select(p => TakipMetni.Paylar(new[] { p }))) : "Kanal kart borcu bilgisi alınamadı.";
    /// <summary>Eski karttan geçişin denetim izi (kural, açıklama, onay anındaki özet); geçiş yoksa null.</summary>
    public string? GecisKaydi => Secili?.Gecis is { } g ? TakipMetni.GecisKaydi(g) : null;
    /// <summary>İlk sürüm geçiş kalıntısı uyarısı; tahmini kasa farkı varsa tehlike, yalnız düşüş tarihi farklıysa bilgi.</summary>
    public string? GecisUyarisi => TakipMetni.GecisUyarisi(Secili?.Gecis);
    public bool GecisUyarisiTehlikeli => Secili?.Gecis is { TahminiKasaFarki: not 0 };
    /// <summary>Güncel önizleme sunucuca kabul edilebilir; değilse onay düğmesi kapalı ve GecisEngeli nedenini söyler.</summary>
    public bool GecisOnaylanabilir => _gecisUygun && _onizlenenGecis is not null;
    partial void OnSeciliChanged(KartTakipDto? value) { foreach (var p in new[] { nameof(KartSecili), nameof(YeniKart), nameof(YeniTakip), nameof(EskiTakip), nameof(KartOzeti), nameof(KanalBorcOzeti), nameof(GecisKaydi), nameof(GecisUyarisi), nameof(GecisUyarisiTehlikeli) }) OnPropertyChanged(p); }
    partial void OnOncedenSayilanChanged(decimal value) { if (!_oncedenSayilanOneriliyor) _oncedenSayilanElle = true; }
    partial void OnGecisKalanBorcChanged(decimal value)
    {
        if (!_oncedenSayilanElle && Secili is { } kart && ParaAyristirici.GecerliMi(value)) OncedenSayilanOner(Math.Max(0, Math.Min(value, kart.Borc)));
    }
    private void OncedenSayilanOner(decimal tutar)
    {
        _oncedenSayilanOneriliyor = true;
        try { OncedenSayilan = tutar; } finally { _oncedenSayilanOneriliyor = false; }
    }
    private void GecisDurumu(KartGecisYaz? onizlenen, bool uygun, string? onizleme, string? engel)
    {
        _onizlenenGecis = onizlenen; _gecisUygun = uygun; GecisOnizleme = onizleme; GecisEngeli = engel; GecisOnay = false;
        OnPropertyChanged(nameof(GecisOnaylanabilir)); GecisiOnaylaCommand.NotifyCanExecuteChanged();
    }

    public bool IdIleSec(int id)
    {
        if (Auth.AktifRol == Rol.Alici) return false;
        var satir = Kartlar.FirstOrDefault(k => k.Veri.Id == id);
        if (satir is null) { Hata = "Kart bulunamadı. Listeyi yenileyip tekrar deneyin."; return false; }
        Sec(satir); return true;
    }

    public Task YukleAsync() => YurutAsync(async n =>
    {
        var kanallar = await finans.KanallarAsync(); var kartlar = await api.TakipKartlarAsync();
        if (!Gecerli(n)) return;
        TakipMetni.Doldur(Kanallar, kanallar); TakipMetni.Doldur(Kartlar, kartlar.Select(k => new KartTakipSatiri(k)));
        if (Secili is { } eski) { var mevcut = kartlar.FirstOrDefault(k => k.Id == eski.Id); if (mevcut is not null) Sec(new(mevcut)); else Yeni(); }
        Tamamlandi();
    });
    [RelayCommand] private void Sec(KartTakipSatiri satir)
    {
        MasrafTemizle();
        HarcamaBenzerlik.Temizle(); OdemeBenzerlik.Temizle();
        Secili = satir.Veri; Ad = Secili.Ad; Limit = Secili.Limit; KesimGunu = Secili.KesimGunu; SonOdemeGunu = Secili.SonOdemeGunu; AcilisBorc = 0; // açılış borcu yalnız yeni kartta girilir
        _oncedenSayilanElle = false; GecisKalanBorc = Secili.Borc; OncedenSayilanOner(Math.Max(0, Secili.Borc)); GecisDurumu(null, false, null, null);
        _onizlenenOdeme = null; OdemeOnizleme = null; OdemeEkstresi = null; DuzenlenenEkstre = null;
        HarcamaPaylari.Clear(); GecisPaylari.Clear(); IadeKaynagi = null; DetaylariYansit();
    }
    [RelayCommand] private void Yeni()
    {
        MasrafTemizle();
        HarcamaBenzerlik.Temizle(); OdemeBenzerlik.Temizle();
        Secili = null; Ad = ""; Limit = AcilisBorc = 0; AcilisTarihi = DateTime.Today; KesimGunu = 1; SonOdemeGunu = 10;
        AcilisPaylari.Clear(); HarcamaPaylari.Clear(); GecisPaylari.Clear(); Ekstreler.Clear(); MasrafEkstreleri.Clear(); Harcamalar.Clear(); IadeKaynaklari.Clear(); IadeKaynagi = null; Odemeler.Clear();
        OdemeOnizleme = null; _onizlenenOdeme = null; GecisDurumu(null, false, null, null);
    }
    private void DetaylariYansit()
    {
        TakipMetni.Doldur(Ekstreler, (Secili?.Ekstreler ?? Array.Empty<KartEkstreDto>()).OrderByDescending(e => e.KesimTarihi).Select(e => new EkstreSatiri(e)));
        TakipMetni.Doldur(MasrafEkstreleri, Ekstreler.Where(e => e.Veri.Kalan > 0 && e.Veri.KesimTarihi <= DateOnly.FromDateTime(DateTime.Today)));
        TakipMetni.Doldur(Harcamalar, (Secili?.Harcamalar ?? Array.Empty<KartHarcamaDto>()).OrderByDescending(e => e.Tarih).Select(e => new HarcamaSatiri(e)));
        TakipMetni.Doldur(IadeKaynaklari, Harcamalar.Where(h => !h.Veri.Iptal && h.Veri.Tutar > 0));
        TakipMetni.Doldur(Odemeler, (Secili?.Odemeler ?? Array.Empty<KartTakipOdemeDto>()).OrderByDescending(e => e.Tarih).Select(e => new KartOdemeSatiri(e)));
    }
    private bool Uygula(KartTakipDto sonuc, int n)
    {
        if (!Gecerli(n)) return false;
        var eski = Kartlar.FirstOrDefault(k => k.Veri.Id == sonuc.Id); if (eski is not null) Kartlar[Kartlar.IndexOf(eski)] = new(sonuc); else Kartlar.Add(new(sonuc));
        Secili = sonuc; DetaylariYansit(); Tamamlandi(); return true;
    }
    public void PayEkle(ObservableCollection<TakipPayEditor> liste) => liste.Add(new(Kanallar.ToList()));
    [RelayCommand] private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu) return;
        if (!ParaAyristirici.HepsiGecerli(Limit, AcilisBorc)) { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (string.IsNullOrWhiteSpace(Ad) || Limit < 0 || KesimGunu is < 1 or > 31 || SonOdemeGunu is < 1 or > 31) { Hata = "Kart adını, limiti ve 1–31 arası günleri kontrol edin."; return; }
        var g = new KartTakipYaz(Guid.Empty, Secili?.Surum ?? 0, Ad.Trim(), Limit, KesimGunu, SonOdemeGunu, DateOnly.FromDateTime(AcilisTarihi), AcilisBorc, TakipMetni.Paylar(AcilisPaylari));
        g = g with { IstekId = _kayit.Al(new { Id = Secili?.Id, g }) };
        if (Uygula(await api.TakipKartKaydetAsync(Secili?.Id, g), n)) { _kayit.Temizle(); Mesaj = "Kart kaydedildi."; }
    });
    [RelayCommand] private Task HarcamaKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart) return;
        if (!ParaAyristirici.GecerliMi(HarcamaTutari)) { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (string.IsNullOrWhiteSpace(HarcamaAciklama) || HarcamaTutari == 0 || TaksitSayisi is < 1 or > 60 || (HarcamaTutari < 0 && TaksitSayisi != 1)) { Hata = "Açıklama ve tutar girin; iade eksi tutarlı tek taksit olmalıdır."; return; }
        if (IadeGirisi && (IadeKaynagi is null || !IadeKaynaklari.Any(h => h.Veri.Id == IadeKaynagi.Veri.Id))) { Hata = "İade edilen pozitif harcamayı seçin; kanal payları bu harcamadan alınır."; return; }
        var g = new KartHarcamaYaz(Guid.Empty, kart.Surum, DateOnly.FromDateTime(HarcamaTarihi), HarcamaAciklama.Trim(), HarcamaTutari, TaksitSayisi, IlkKesimVar ? DateOnly.FromDateTime(IlkKesimTarihi) : null, IadeGirisi ? Array.Empty<KanalPayYaz>() : TakipMetni.Paylar(HarcamaPaylari), IadeGirisi ? IadeKaynagi!.Veri.Id : null);
        g = g with { IstekId = _harcama.Al(new { kart.Id, g }) };
        if (!await HarcamaBenzerlik.DevamEdilebilirAsync(new("KartHarcama", g.Tarih, g.Tutar, kart.Id), new { kart.Id, g }, () => Gecerli(n) && Secili?.Id == kart.Id)) return;
        if (Uygula(await api.TakipHarcamaKaydetAsync(kart.Id, g), n)) { _harcama.Temizle(); HarcamaBenzerlik.Temizle(); HarcamaTutari = 0; HarcamaAciklama = ""; HarcamaPaylari.Clear(); Mesaj = "Kart hareketi kaydedildi. Henüz kasa çıkışı oluşmadı."; }
    });
    [RelayCommand] private async Task HarcamayiAyriKaydetAsync() { if (HarcamaBenzerlik.Onayla()) await HarcamaKaydetAsync(); }
    private KartTakipOdemeYaz OdemeGovde()
    {
        ParaAyristirici.Dogrula(OdemeTutari);
        var g = new KartTakipOdemeYaz(Guid.Empty, Secili!.Surum, DateOnly.FromDateTime(OdemeTarihi), OdemeTutari, OdemeEkstresi?.Veri.Id, OdemeNotu);
        return g with { IstekId = _odeme.Al(new { Secili.Id, g }) };
    }
    [RelayCommand] private Task OdemeOnizleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart) return;
        _onizlenenOdeme = null; OdemeOnizleme = null;
        if (!ParaAyristirici.GecerliMi(OdemeTutari)) { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (OdemeTutari <= 0) { Hata = "Pozitif ödeme tutarı girin."; return; }
        var g = OdemeGovde(); var sonuc = await api.TakipOdemeOnizlemeAsync(kart.Id, g);
        if (!Gecerli(n) || Secili?.Id != kart.Id || !TakipMetni.Ayni(g, OdemeGovde())) return;
        _onizlenenOdeme = g;
        OdemeOnizleme = $"Kasa çıkışı: {Bicim.Tl(sonuc.KasaEtkisi)} ₺\n{TakipMetni.Paylar(sonuc.Dagilimlar)}\n" + string.Join(" · ", sonuc.Ekstreler.Select(e => $"Ekstre #{e.EkstreId}: {Bicim.Tl(e.Tutar)} ₺"));
    });
    [RelayCommand] private Task OdemeKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart) return;
        var g = OdemeGovde();
        if (_onizlenenOdeme is null || !TakipMetni.Ayni(g, _onizlenenOdeme)) { Hata = "Ödeme bilgileri için önce güncel önizlemeyi alın."; return; }
        if (!await OdemeBenzerlik.DevamEdilebilirAsync(new("KartOdeme", g.Tarih, g.Tutar, kart.Id), new { kart.Id, g }, () => Gecerli(n) && Secili?.Id == kart.Id)) return;
        if (Uygula(await api.TakipOdemeKaydetAsync(kart.Id, g), n)) { _odeme.Temizle(); OdemeBenzerlik.Temizle(); _onizlenenOdeme = null; OdemeOnizleme = null; OdemeTutari = 0; OdemeNotu = ""; Mesaj = "Kart ödemesi kaydedildi; kasa etkisi bir kez işlendi."; }
    });
    [RelayCommand] private async Task OdemeyiAyriKaydetAsync() { if (OdemeBenzerlik.Onayla()) await OdemeKaydetAsync(); }
    [RelayCommand] private void EkstreSec(EkstreSatiri satir) { DuzenlenenEkstre = satir; EkstreSonOdeme = satir.Veri.SonOdemeTarihi.ToDateTime(TimeOnly.MinValue); AsgariVar = satir.Veri.AsgariOdeme is not null; AsgariTutar = satir.Veri.AsgariOdeme ?? 0; }
    [RelayCommand] private Task EkstreKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is null || DuzenlenenEkstre is null) return;
        if (!GerekceVar()) return;
        if (AsgariVar && !ParaAyristirici.GecerliMi(AsgariTutar)) { Hata = ParaAyristirici.GecersizMesaji; return; }
        var g = new KartEkstreYaz(Guid.Empty, Secili.Surum, DateOnly.FromDateTime(EkstreSonOdeme), AsgariVar ? AsgariTutar : null, Gerekce.Trim());
        g = g with { IstekId = _ekstre.Al(new { Secili.Id, EkstreId = DuzenlenenEkstre.Veri.Id, g }) };
        if (Uygula(await api.TakipEkstreKaydetAsync(Secili.Id, DuzenlenenEkstre.Veri.Id, g), n)) { _ekstre.Temizle(); DuzenlenenEkstre = null; Mesaj = "Ekstre bilgisi kaydedildi."; }
    });
    private bool GerekceVar() { if (!string.IsNullOrWhiteSpace(Gerekce)) return true; Hata = "İşlem gerekçesini yazın."; return false; }
    public Task OdemeIptalAsync(KartOdemeSatiri satir)
    {
        if (satir.Veri.EkstreKayitId is not null) { Hata = "Bu ödeme Ekstre İçe Aktar bölümünden gerekçeyle iptal edilir."; return Task.CompletedTask; }
        return IptalAsync(satir.Veri.Id, false);
    }
    public Task HarcamaIptalAsync(HarcamaSatiri satir)
    {
        if (satir.Veri.EkstreKayitId is not null) { Hata = "Bu hareket Ekstre İçe Aktar bölümünden gerekçeyle iptal edilir."; return Task.CompletedTask; }
        return IptalAsync(satir.Veri.Id, true);
    }
    private Task IptalAsync(int id, bool harcama) => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is null || !GerekceVar()) return;
        var g = new TakipIptalYaz(Guid.Empty, Secili.Surum, Gerekce.Trim()); g = g with { IstekId = _iptal.Al(new { Secili.Id, KayitId = id, harcama, g }) };
        var sonuc = harcama ? await api.TakipHarcamaIptalAsync(Secili.Id, id, g) : await api.TakipOdemeIptalAsync(Secili.Id, id, g);
        if (Uygula(sonuc, n)) { _iptal.Temizle(); Mesaj = "İptal kaydedildi; geçmiş korundu."; }
    });
    public Task DurumDegistirAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is null || !GerekceVar()) return;
        var g = new TakipDurumYaz(Guid.Empty, Secili.Surum, !Secili.Aktif, Gerekce.Trim()); g = g with { IstekId = _durum.Al(new { Secili.Id, g }) };
        if (Uygula(await api.TakipKartDurumAsync(Secili.Id, g), n)) { _durum.Temizle(); Mesaj = "Kartın kullanım durumu değiştirildi; geçmiş korundu."; }
    });
    private KartGecisYaz GecisGovde()
    {
        ParaAyristirici.Dogrula(GecisKalanBorc, OncedenSayilan);
        var g = new KartGecisYaz(Guid.Empty, Secili!.Surum, DateOnly.FromDateTime(GecisTarihi), GecisKalanBorc, OncedenSayilan, TakipMetni.Paylar(GecisPaylari), GecisAciklama.Trim(), false);
        return g with { IstekId = _gecis.Al(new { Secili.Id, g }) };
    }
    [RelayCommand] private Task GecisOnizleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: false } kart) return;
        GecisDurumu(null, false, null, null);
        var g = GecisGovde(); var sonuc = await api.TakipKartGecisOnizlemeAsync(kart.Id, g);
        if (!Gecerli(n) || Secili?.Id != kart.Id || !TakipMetni.Ayni(g, GecisGovde())) return;
        string? doldurma = null;
        // Elle değiştirilmemiş tutar sunucunun önerisiyle dolar ve yeniden önizlenir: gösterilen ve onaylanacak
        // önizleme, alandaki tutarındır (web'de "önerilen tutarla yeniden önizle").
        if (!_oncedenSayilanElle && sonuc.OnerilenKasadaSayilanTutar is { } oneri && oneri != g.KasadaOncedenSayilanTutar)
        {
            OncedenSayilanOner(oneri); doldurma = $"Kasada önceden sayılan tutar önerilen tutarla ({Bicim.Tl(oneri)} ₺) dolduruldu.\n";
            g = GecisGovde(); sonuc = await api.TakipKartGecisOnizlemeAsync(kart.Id, g);
            if (!Gecerli(n) || Secili?.Id != kart.Id || !TakipMetni.Ayni(g, GecisGovde())) return;
        }
        GecisDurumu(g, sonuc.KabulEdilebilir, doldurma + TakipMetni.Gecis(sonuc), sonuc.KabulEdilebilir ? null : TakipMetni.GecisEngeli(sonuc));
    });
    [RelayCommand(CanExecute = nameof(GecisOnaylanabilir))] private Task GecisiOnaylaAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: false } kart) return;
        var g = GecisGovde();
        if (_onizlenenGecis is not null && !_gecisUygun) { Hata = GecisEngeli; return; }
        if (!GecisOnay || _onizlenenGecis is null || !TakipMetni.Ayni(g, _onizlenenGecis)) { Hata = "Güncel geçiş önizlemesini inceleyip onay kutusunu işaretleyin."; return; }
        g = g with { Onay = true };
        if (Uygula(await api.TakipKartGecisAsync(kart.Id, g), n)) { _gecis.Temizle(); GecisDurumu(null, false, null, null); Mesaj = "Yeni takip açıldı; geçmiş kayıtlar korundu."; }
    });
    protected override void OturumTemizle()
    {
        Kartlar.Clear(); Kanallar.Clear(); Yeni(); HarcamaAciklama = OdemeNotu = Gerekce = GecisAciklama = ""; HarcamaTutari = OdemeTutari = OncedenSayilan = GecisKalanBorc = 0; _oncedenSayilanElle = false;
        foreach (var key in new[] { _kayit, _harcama, _odeme, _ekstre, _iptal, _durum, _gecis }) key.Temizle();
    }
}
