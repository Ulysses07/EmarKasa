using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

// Sec ve Yeni artık [RelayCommand] değil (arayüz KutuSecCommand/YeniKartAcCommand kullanır, bkz. KartTakipViewModel.Gorunum.cs);
// testler bu iki metodu doğrudan çağırır (TakipKomutlariTests ve diğerleri), KutuSec'in "açıksa kapat" devrik mantığından
// etkilenmeden.
[assembly: InternalsVisibleTo("Kasa.App.Core.Tests")]

namespace Kasa.App.Core;

/// <param name="zaman">Kart kutularındaki "Son ödeme geçti" kuralının saati (yerel gün); verilmezse sistem saati. DI'da kayıtlı
/// değildir (isteğe bağlı parametre varsayılana düşer); testler sabit saat verir.</param>
public partial class KartTakipViewModel(IFinansTakipApi api, IKasaApi finans, AuthViewModel auth, IBenzerKayitApi? benzerlikApi = null,
    IKasaKontrolApi? kontrolApi = null, TimeProvider? zaman = null) : OturumluViewModel(auth)
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
    public BenzerKayitKontrolu HarcamaBenzerlik { get; } = new(benzerlikApi ?? finans as IBenzerKayitApi);
    public BenzerKayitKontrolu OdemeBenzerlik { get; } = new(benzerlikApi ?? finans as IBenzerKayitApi);
    private readonly TekrarAnahtari _kayit = new();
    // Kart başına: başka karta geçiş, bir kartın yanıtı belirsiz kalan isteğinin anahtarını ezmez (KartFormlariniTemizle).
    private readonly KayitBasinaTekrarAnahtari _harcama = new(), _odeme = new(), _ekstre = new(), _iptal = new(), _durum = new(), _gecis = new(), _devirDuzelt = new();
    private readonly OnizlemeOnay<KartTakipOdemeYaz, KartOdemeOnizlemeDto> _odemeOnizlemesi = new();
    private readonly OnizlemeOnay<KartGecisYaz, TakipGecisDto> _gecisOnizlemesi = new();
    private bool _gecisPaylariIzleniyor;
    // Web cardTransition gibi: kullanıcı K'ya dokunmadıkça alan kalan borç ile kart borcunun küçüğünü izler; elle
    // girilen tutar ezilmez. Sunucunun önizlemede önerdiği farklı tutar alana kendiliğinden yazılmaz, açık eylemle
    // (OnerilenleGecisOnizle, web'de "Önerilen tutarla yeniden önizle") uygulanır.
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
    public ObservableCollection<TakipPayEditor> DevirPaylari { get; } = new();
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
    /// <summary>Geçişli kartın eski borç devri (finance-2); açık eylemle (DevirYukle) okunur, kart değişince temizlenir.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DevirOzeti), nameof(DevirDuzeltilebilir))]
    private KartDevirDto? _devir;
    [ObservableProperty] private decimal _devirKalanBorc;
    [ObservableProperty] private decimal _devirOncedenSayilan;
    [ObservableProperty] private string _devirAciklama = "";
    /// <summary>Son önizlemede sunucunun önerdiği, girilenden farklı kasada önceden sayılan tutar; yoksa null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GecisOneriMetni), nameof(GecisOneriVar))]
    [NotifyCanExecuteChangedFor(nameof(OnerilenleGecisOnizleCommand))]
    private decimal? _gecisOnerilenTutar;
    public bool KartSecili => Secili is not null;
    public bool YeniKart => Secili is null;
    public bool YeniTakip => Secili?.YeniTakip == true;
    public bool EskiTakip => Secili is { YeniTakip: false };
    // Geçersiz metin (ParaAyristirici.Gecersiz) eksi olsa da iade girişi sayılmaz.
    public bool IadeGirisi => HarcamaTutari < 0 && ParaAyristirici.GecerliMi(HarcamaTutari);
    public bool HarcamaGirisi => !IadeGirisi;
    partial void OnHarcamaTutariChanged(decimal value) { OnPropertyChanged(nameof(IadeGirisi)); OnPropertyChanged(nameof(HarcamaGirisi)); if (value >= 0) IadeKaynagi = null; }
    public string KartOzeti => Secili is { } k ? new KartTakipSatiri(k, _zaman).Ozet : "Yeni kart bilgilerini girin.";
    public string KanalBorcOzeti => Secili?.KanalKartBorclari is { } paylar ? paylar.Count == 0 ? "Kayıtlı kanal kart borcu yok." : string.Join("\n", paylar.Select(p => TakipMetni.Paylar(new[] { p }))) : "Kanal kart borcu bilgisi alınamadı.";
    /// <summary>Eski karttan geçişin denetim izi (kural, açıklama, onay anındaki özet); geçiş yoksa null.</summary>
    public string? GecisKaydi => Secili?.Gecis is { } g ? TakipMetni.GecisKaydi(g) : null;
    /// <summary>İlk sürüm geçiş kalıntısı uyarısı; tahmini kasa farkı varsa tehlike, yalnız düşüş tarihi farklıysa bilgi.</summary>
    public string? GecisUyarisi => TakipMetni.GecisUyarisi(Secili?.Gecis);
    public bool GecisUyarisiTehlikeli => Secili?.Gecis is { TahminiKasaFarki: not 0 };
    /// <summary>Güncel önizleme sunucuca kabul edilebilir; değilse onay düğmesi kapalı ve GecisEngeli nedenini söyler.</summary>
    public bool GecisOnaylanabilir => _gecisOnizlemesi.Onizleme?.KabulEdilebilir == true;
    public bool GecisOneriVar => GecisOnerilenTutar is not null;
    /// <summary>Eski borç devri ve düzeltme sınırları metni; devir okunmadıysa null.</summary>
    public string? DevirOzeti => Devir is { } d ? TakipMetni.Devir(d) : null;
    public bool DevirDuzeltilebilir => Devir?.Duzeltilebilir == true;
    /// <summary>Eski borç devri satırı: iptal edilmez, "Devri düzelt" ile değiştirilir (devir okunduysa bilinir).</summary>
    public bool DevirSatiri(HarcamaSatiri satir) => Devir?.HarcamaId is { } id && satir.Veri.Id == id;
    public string? GecisOneriMetni => GecisOnerilenTutar is { } oneri ? $"Önerilen tutarla ({Bicim.Tl(oneri)} ₺) yeniden önizle" : null;
    partial void OnSeciliChanged(KartTakipDto? value) { foreach (var p in new[] { nameof(KartSecili), nameof(YeniKart), nameof(YeniTakip), nameof(EskiTakip), nameof(KartOzeti), nameof(KanalBorcOzeti), nameof(GecisKaydi), nameof(GecisUyarisi), nameof(GecisUyarisiTehlikeli) }) OnPropertyChanged(p); }
    partial void OnOncedenSayilanChanged(decimal value) { if (!_oncedenSayilanOneriliyor) _oncedenSayilanElle = true; GecisGecersizKil(); }
    partial void OnGecisKalanBorcChanged(decimal value)
    {
        GecisGecersizKil();
        if (!_oncedenSayilanElle && Secili is { } kart && ParaAyristirici.GecerliMi(value))
            OncedenSayilanOner(Math.Max(0, Math.Min(value, kart.Borc)));
    }
    partial void OnGecisTarihiChanged(DateTime value) => GecisGecersizKil();
    partial void OnGecisAciklamaChanged(string value) => GecisGecersizKil();
    /// <summary>Önizlemeden sonra geçiş girdisi (tarih, kalan borç, K, paylar, açıklama) değişince gösterilen önizleme,
    /// engel metni, öneri ve onay eski girdiye aittir: hepsi kalkar, onay düğmesi yeni önizlemeye kadar kapanır.</summary>
    private void GecisGecersizKil()
    {
        if (_gecisOnizlemesi.Var || GecisOnizleme is not null || GecisEngeli is not null || GecisOnerilenTutar is not null || GecisOnay)
            GecisDurumu(null, null);
    }
    private void OncedenSayilanOner(decimal tutar)
    {
        _oncedenSayilanOneriliyor = true;
        try
        { OncedenSayilan = tutar; }
        finally { _oncedenSayilanOneriliyor = false; }
    }
    /// <summary>Gösterilen geçiş önizlemesi (metin, engel, öneri); metin yoksa önizleme de kalkar. Onay her durumda kalkar.</summary>
    private void GecisDurumu(string? onizleme, string? engel, decimal? oneri = null)
    {
        if (onizleme is null)
            _gecisOnizlemesi.Temizle();
        GecisOnizleme = onizleme;
        GecisEngeli = engel;
        GecisOnerilenTutar = oneri;
        GecisOnay = false;
        OnPropertyChanged(nameof(GecisOnaylanabilir));
        GecisiOnaylaCommand.NotifyCanExecuteChanged();
    }

    public bool IdIleSec(int id)
    {
        if (Auth.AktifRol == Rol.Alici)
            return false;
        var satir = Kartlar.FirstOrDefault(k => k.Veri.Id == id);
        if (satir is null)
        { SayfaHatasiYaz("Kart bulunamadı. Listeyi yenileyip tekrar deneyin."); return false; }
        Sec(satir);
        return true;
    }

    public Task YukleAsync() => YurutAsync(KartFormu.Yok, async n =>
    {
        var kanallar = await finans.KanallarAsync();
        var kartlar = await api.TakipKartlarAsync();
        if (!Gecerli(n))
            return;
        TakipMetni.Doldur(Kanallar, kanallar);
        TakipMetni.Doldur(Kartlar, kartlar.Select(k => new KartTakipSatiri(k, _zaman)));
        if (Secili is { } eski)
        { var mevcut = kartlar.FirstOrDefault(k => k.Id == eski.Id); if (mevcut is not null) Sec(new(mevcut, _zaman)); else Yeni(); }
        Tamamlandi();
    });
    // [RelayCommand] kasıtlı olarak yok: arayüz KutuSecCommand'ı kullanır (KartTakipViewModel.Gorunum.cs), bu metodu çağırır.
    // internal: testler doğrudan çağırır (KutuSec'in "açıksa kapat" devrik mantığı test niyetini bozar).
    internal void Sec(KartTakipSatiri satir)
    {
        MasrafTemizle();
        HarcamaBenzerlik.Temizle();
        OdemeBenzerlik.Temizle();
        // Başka karta geçişte önceki kartın (ya da yeni kart formunun) yazılmış alanları taşınmaz; aynı kartın yeniden
        // seçilmesi (liste yenilemesi) yazılmakta olan formu korur.
        if (Secili?.Id != satir.Veri.Id)
            KartFormlariniTemizle();
        Secili = satir.Veri;
        Ad = Secili.Ad;
        Limit = Secili.Limit;
        KesimGunu = Secili.KesimGunu;
        SonOdemeGunu = Secili.SonOdemeGunu;
        AcilisBorc = 0; // açılış borcu yalnız yeni kartta girilir
        _oncedenSayilanElle = false;
        GecisKalanBorc = Secili.Borc;
        OncedenSayilanOner(Math.Max(0, Secili.Borc));
        GecisDurumu(null, null);
        _odemeOnizlemesi.Temizle();
        OdemeOnizleme = null;
        OdemeEkstresi = null;
        DuzenlenenEkstre = null;
        HarcamaPaylari.Clear();
        GecisPaylari.Clear();
        IadeKaynagi = null;
        DetaylariYansit();
    }
    // [RelayCommand] kasıtlı olarak yok: arayüz YeniKartAcCommand'ı kullanır (KartTakipViewModel.Gorunum.cs), bu metodu
    // çağırır. internal: testler doğrudan çağırır (YeniKartAc formu da açar; testler yalnız alan sıfırlamasıyla ilgilenir).
    internal void Yeni()
    {
        MasrafTemizle();
        HarcamaBenzerlik.Temizle();
        OdemeBenzerlik.Temizle();
        Secili = null;
        Ad = "";
        Limit = AcilisBorc = 0;
        AcilisTarihi = DateTime.Today;
        KesimGunu = 1;
        SonOdemeGunu = 10;
        AcilisPaylari.Clear();
        HarcamaPaylari.Clear();
        GecisPaylari.Clear();
        Ekstreler.Clear();
        MasrafEkstreleri.Clear();
        Harcamalar.Clear();
        IadeKaynaklari.Clear();
        IadeKaynagi = null;
        Odemeler.Clear();
        OdemeOnizleme = null;
        _odemeOnizlemesi.Temizle();
        DuzenlenenEkstre = null;
        GecisDurumu(null, null);
        DevirTemizle();
    }
    /// <summary>Karta özel form alanları: açılış (yalnız yeni kartta görünür), harcama, ödeme, ekstre, iptal/durum
    /// gerekçesi ve geçiş girdileri. Tekrar anahtarları sıfırlanmaz: kart başına tutulurlar (<see cref="KayitBasinaTekrarAnahtari"/>)
    /// ve gövde kartın kimliğini taşır; başka kartın isteği kendi anahtarını alır, yanıtı belirsiz kalan istek karta dönülüp
    /// aynı bilgilerle yeniden gönderilince aynı anahtarla gider (sunucu ikinci kez işlemez).</summary>
    private void KartFormlariniTemizle()
    {
        AcilisPaylari.Clear();
        AcilisBorc = 0;
        AcilisTarihi = DateTime.Today;
        HarcamaTutari = 0;
        HarcamaAciklama = "";
        TaksitSayisi = 1;
        IlkKesimVar = false;
        IlkKesimTarihi = DateTime.Today;
        OdemeTutari = 0;
        OdemeNotu = "";
        OdemeTarihi = DateTime.Today;
        AsgariVar = false;
        AsgariTutar = 0;
        Gerekce = "";
        GecisAciklama = "";
        OncedenSayilan = 0;
        DevirTemizle();
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
        if (!Gecerli(n))
            return false;
        var eski = Kartlar.FirstOrDefault(k => k.Veri.Id == sonuc.Id);
        if (eski is not null)
            Kartlar[Kartlar.IndexOf(eski)] = new(sonuc, _zaman);
        else
            Kartlar.Add(new(sonuc, _zaman));
        Secili = sonuc;
        DetaylariYansit();
        Tamamlandi();
        return true;
    }
    public void PayEkle(ObservableCollection<TakipPayEditor> liste)
    {
        var pay = new TakipPayEditor(Kanallar.ToList());
        if (ReferenceEquals(liste, GecisPaylari))
        {
            // Geçiş payı değişince (satır ekleme/kaldırma dahil) önizleme geçersizdir. Satırlar yalnız burada eklendiği
            // için liste izlemesi ilk eklemede kurulur.
            if (!_gecisPaylariIzleniyor)
            { _gecisPaylariIzleniyor = true; GecisPaylari.CollectionChanged += (_, _) => GecisGecersizKil(); }
            pay.PropertyChanged += (_, _) => GecisGecersizKil();
        }
        liste.Add(pay);
    }
    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(KartFormu.KartBilgisi, async n =>
    {
        if (!EditorMu)
            return;
        // Açılış borcu, tarihi ve dağılımı yalnız yeni kartta girilir ve okunur (bölüm yalnız YeniKart iken görünür); sunucu
        // güncellemede bu alanları yok sayar. Mevcut kartta görünmeyen bir açılış satırı kaydı reddettirmez.
        var yeni = Secili is null;
        if (!ParaAyristirici.HepsiGecerli(Limit, yeni ? AcilisBorc : 0))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (string.IsNullOrWhiteSpace(Ad) || Limit < 0 || KesimGunu is < 1 or > 31 || SonOdemeGunu is < 1 or > 31)
        { Hata = "Kart adını, limiti ve 1–31 arası günleri kontrol edin."; return; }
        var g = yeni
            ? new KartTakipYaz(Guid.Empty, 0, Ad.Trim(), Limit, KesimGunu, SonOdemeGunu, DateOnly.FromDateTime(AcilisTarihi), AcilisBorc, TakipMetni.Paylar(AcilisPaylari))
            : new KartTakipYaz(Guid.Empty, Secili!.Surum, Ad.Trim(), Limit, KesimGunu, SonOdemeGunu, Secili.TakipBaslangic ?? DateOnly.FromDateTime(AcilisTarihi), 0, Array.Empty<KanalPayYaz>());
        g = g with { IstekId = _kayit.Al(new { Id = Secili?.Id, g }) };
        if (Uygula(await api.TakipKartKaydetAsync(Secili?.Id, g), n))
        { _kayit.Temizle(); FormuKapat(KartFormu.KartBilgisi); Mesaj = "Kart kaydedildi."; }
    });
    [RelayCommand]
    private Task HarcamaKaydetAsync() => YurutAsync(KartFormu.Harcama, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart)
            return;
        if (!ParaAyristirici.GecerliMi(HarcamaTutari))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (string.IsNullOrWhiteSpace(HarcamaAciklama) || HarcamaTutari == 0 || TaksitSayisi is < 1 or > 60 || (HarcamaTutari < 0 && TaksitSayisi != 1))
        { Hata = "Açıklama ve tutar girin; iade eksi tutarlı tek taksit olmalıdır."; return; }
        if (IadeGirisi && (IadeKaynagi is null || !IadeKaynaklari.Any(h => h.Veri.Id == IadeKaynagi.Veri.Id)))
        { Hata = "İade edilen pozitif harcamayı seçin; kanal payları bu harcamadan alınır."; return; }
        var g = new KartHarcamaYaz(Guid.Empty, kart.Surum, DateOnly.FromDateTime(HarcamaTarihi), HarcamaAciklama.Trim(), HarcamaTutari, TaksitSayisi, IlkKesimVar ? DateOnly.FromDateTime(IlkKesimTarihi) : null, IadeGirisi ? Array.Empty<KanalPayYaz>() : TakipMetni.Paylar(HarcamaPaylari), IadeGirisi ? IadeKaynagi!.Veri.Id : null);
        g = g with { IstekId = _harcama.Al(kart.Id, new { kart.Id, g }) };
        if (!await HarcamaBenzerlik.DevamEdilebilirAsync(new(BenzerAramaTurleri.KartHarcama, g.Tarih, g.Tutar, kart.Id), new { kart.Id, g }, () => Gecerli(n) && Secili?.Id == kart.Id))
            return;
        if (Uygula(await api.TakipHarcamaKaydetAsync(kart.Id, g), n))
        { _harcama.Temizle(kart.Id); HarcamaBenzerlik.Temizle(); HarcamaTutari = 0; HarcamaAciklama = ""; HarcamaPaylari.Clear(); FormuKapat(KartFormu.Harcama); Mesaj = "Kart hareketi kaydedildi. Henüz kasa çıkışı oluşmadı."; }
    });
    [RelayCommand] private async Task HarcamayiAyriKaydetAsync() { if (HarcamaBenzerlik.Onayla()) await HarcamaKaydetAsync(); }
    private KartTakipOdemeYaz OdemeGovde()
    {
        ParaAyristirici.Dogrula(OdemeTutari);
        var g = new KartTakipOdemeYaz(Guid.Empty, Secili!.Surum, DateOnly.FromDateTime(OdemeTarihi), OdemeTutari, OdemeEkstresi?.Veri.Id, OdemeNotu);
        return g with { IstekId = _odeme.Al(Secili.Id, new { Secili.Id, g }) };
    }
    [RelayCommand]
    private Task OdemeOnizleAsync() => YurutAsync(KartFormu.Odeme, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart)
            return;
        _odemeOnizlemesi.Temizle();
        OdemeOnizleme = null;
        if (!ParaAyristirici.GecerliMi(OdemeTutari))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (OdemeTutari <= 0)
        { Hata = "Pozitif ödeme tutarı girin."; return; }
        if (!await _odemeOnizlemesi.IsteAsync(OdemeGovde, g => api.TakipOdemeOnizlemeAsync(kart.Id, g), () => Gecerli(n) && Secili?.Id == kart.Id))
            return;
        var sonuc = _odemeOnizlemesi.Onizleme!;
        OdemeOnizleme = $"Kasa çıkışı: {Bicim.Tl(sonuc.KasaEtkisi)} ₺\n{TakipMetni.Paylar(sonuc.Dagilimlar)}\n" + string.Join(" · ", sonuc.Ekstreler.Select(e => $"Ekstre #{e.EkstreId}: {Bicim.Tl(e.Tutar)} ₺"));
    });
    [RelayCommand]
    private Task OdemeKaydetAsync() => YurutAsync(KartFormu.Odeme, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart)
            return;
        var g = OdemeGovde();
        if (!_odemeOnizlemesi.Gecerli(g))
        { Hata = "Ödeme bilgileri için önce güncel önizlemeyi alın."; return; }
        if (!await OdemeBenzerlik.DevamEdilebilirAsync(new(BenzerAramaTurleri.KartOdeme, g.Tarih, g.Tutar, kart.Id), new { kart.Id, g }, () => Gecerli(n) && Secili?.Id == kart.Id))
            return;
        if (Uygula(await api.TakipOdemeKaydetAsync(kart.Id, g), n))
        { _odeme.Temizle(kart.Id); OdemeBenzerlik.Temizle(); _odemeOnizlemesi.Temizle(); OdemeOnizleme = null; OdemeTutari = 0; OdemeNotu = ""; FormuKapat(KartFormu.Odeme); Mesaj = "Kart ödemesi kaydedildi; kasa etkisi bir kez işlendi."; }
    });
    [RelayCommand] private async Task OdemeyiAyriKaydetAsync() { if (OdemeBenzerlik.Onayla()) await OdemeKaydetAsync(); }
    [RelayCommand]
    private void EkstreSec(EkstreSatiri satir)
    {
        // Ekstre bilgisi yalnız editörde ve yeni takipteki kartta düzenlenir; izleyicide ya da eski takipte seçim de olmaz.
        if (!EditorMu || !YeniTakip)
            return;
        DuzenlenenEkstre = satir;
        EkstreSonOdeme = satir.Veri.SonOdemeTarihi.ToDateTime(TimeOnly.MinValue);
        AsgariVar = satir.Veri.AsgariOdeme is not null;
        AsgariTutar = satir.Veri.AsgariOdeme ?? 0;
        FormAc(KartFormu.Ekstre);
    }
    [RelayCommand]
    private Task EkstreKaydetAsync() => YurutAsync(KartFormu.Ekstre, async n =>
    {
        if (!EditorMu || Secili is null || DuzenlenenEkstre is not { } ekstre)
            return;
        if (!GerekceVar())
            return;
        if (AsgariVar && !ParaAyristirici.GecerliMi(AsgariTutar))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        var g = new KartEkstreYaz(Guid.Empty, Secili.Surum, DateOnly.FromDateTime(EkstreSonOdeme), AsgariVar ? AsgariTutar : null, Gerekce.Trim());
        g = g with { IstekId = _ekstre.Al(Secili.Id, new { Secili.Id, EkstreId = DuzenlenenEkstre.Veri.Id, g }) };
        if (Uygula(await api.TakipEkstreKaydetAsync(Secili.Id, DuzenlenenEkstre.Veri.Id, g), n))
        {
            _ekstre.Temizle(Secili.Id);
            // Kayıt sürerken başka ekstre seçildiyse onun formu açık kalır.
            if (ReferenceEquals(DuzenlenenEkstre, ekstre))
            {
                DuzenlenenEkstre = null;
                FormuKapat(KartFormu.Ekstre);
            }
            Mesaj = "Ekstre bilgisi kaydedildi.";
        }
    });
    private bool GerekceVar() { if (!string.IsNullOrWhiteSpace(Gerekce)) return true; Hata = "İşlem gerekçesini yazın."; return false; }
    public Task OdemeIptalAsync(KartOdemeSatiri satir)
    {
        if (satir.Veri.EkstreKayitId is not null)
        { SayfaHatasiYaz("Bu ödeme Ekstre İçe Aktar bölümünden gerekçeyle iptal edilir."); return Task.CompletedTask; }
        if (satir.AvansDagitimi)
        { SayfaHatasiYaz("Kilitli avans dağıtımı ayrıca iptal edilemez; gerekirse avansı yatıran ödemeyi (dönemi açıksa) iptal edin."); return Task.CompletedTask; }
        return IptalAsync(satir.Veri.Id, false);
    }
    public Task HarcamaIptalAsync(HarcamaSatiri satir)
    {
        if (satir.Veri.EkstreKayitId is not null)
        { SayfaHatasiYaz("Bu hareket Ekstre İçe Aktar bölümünden gerekçeyle iptal edilir."); return Task.CompletedTask; }
        if (DevirSatiri(satir))
        { SayfaHatasiYaz("Eski borç devri iptal edilmez; \"Eski borç devri\" bölümünden gerekçeyle düzeltin."); return Task.CompletedTask; }
        return IptalAsync(satir.Veri.Id, true);
    }
    private Task IptalAsync(int id, bool harcama) => YurutAsync(KartFormu.Yok, async n =>
    {
        if (!EditorMu || Secili is null || !GerekceVar())
            return;
        var g = new TakipIptalYaz(Guid.Empty, Secili.Surum, Gerekce.Trim());
        g = g with { IstekId = _iptal.Al(Secili.Id, new { Secili.Id, KayitId = id, harcama, g }) };
        var sonuc = harcama ? await api.TakipHarcamaIptalAsync(Secili.Id, id, g) : await api.TakipOdemeIptalAsync(Secili.Id, id, g);
        if (Uygula(sonuc, n))
        { _iptal.Temizle(sonuc.Id); Mesaj = "İptal kaydedildi; geçmiş korundu."; }
    });
    public Task DurumDegistirAsync() => YurutAsync(KartFormu.KartBilgisi, async n =>
    {
        if (!EditorMu || Secili is null || !GerekceVar())
            return;
        var g = new TakipDurumYaz(Guid.Empty, Secili.Surum, !Secili.Aktif, Gerekce.Trim());
        g = g with { IstekId = _durum.Al(Secili.Id, new { Secili.Id, g }) };
        if (Uygula(await api.TakipKartDurumAsync(Secili.Id, g), n))
        { _durum.Temizle(Secili.Id); FormuKapat(KartFormu.KartBilgisi); Mesaj = "Kartın kullanım durumu değiştirildi; geçmiş korundu."; }
    });
    private KartGecisYaz GecisGovde()
    {
        ParaAyristirici.Dogrula(GecisKalanBorc, OncedenSayilan);
        var g = new KartGecisYaz(Guid.Empty, Secili!.Surum, DateOnly.FromDateTime(GecisTarihi), GecisKalanBorc, OncedenSayilan, TakipMetni.Paylar(GecisPaylari), GecisAciklama.Trim(), false);
        return g with { IstekId = _gecis.Al(Secili.Id, new { Secili.Id, g }) };
    }
    [RelayCommand]
    private Task GecisOnizleAsync() => YurutAsync(KartFormu.Gecis, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: false } kart)
            return;
        GecisDurumu(null, null);
        if (!await _gecisOnizlemesi.IsteAsync(GecisGovde, g => api.TakipKartGecisOnizlemeAsync(kart.Id, g), () => Gecerli(n) && Secili?.Id == kart.Id))
            return;
        var sonuc = _gecisOnizlemesi.Onizleme!;
        // Gösterilen ve onaylanacak önizleme alandaki tutarındır; sunucu farklı tutar önerirse yalnız teklif edilir.
        var oneri = sonuc.OnerilenKasadaSayilanTutar is { } o && o != _gecisOnizlemesi.Govde!.KasadaOncedenSayilanTutar ? o : (decimal?)null;
        GecisDurumu(TakipMetni.Gecis(sonuc), sonuc.KabulEdilebilir ? null : TakipMetni.GecisEngeli(sonuc), oneri);
    });
    /// <summary>Önerilen tutarı alana yazar ve yeniden önizler; alan artık elle girilmiş sayılır (web countedEdited).</summary>
    [RelayCommand(CanExecute = nameof(GecisOneriVar))]
    private Task OnerilenleGecisOnizleAsync()
    {
        if (GecisOnerilenTutar is not { } oneri)
            return Task.CompletedTask;
        OncedenSayilan = oneri;
        return GecisOnizleAsync();
    }
    [RelayCommand(CanExecute = nameof(GecisOnaylanabilir))]
    private Task GecisiOnaylaAsync() => YurutAsync(KartFormu.Gecis, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: false } kart)
            return;
        var g = GecisGovde();
        if (_gecisOnizlemesi.Onizleme is { KabulEdilebilir: false })
        { Hata = GecisEngeli; return; }
        if (!GecisOnay || !_gecisOnizlemesi.Gecerli(g))
        { Hata = "Güncel geçiş önizlemesini inceleyip onay kutusunu işaretleyin."; return; }
        g = g with { Onay = true };
        if (Uygula(await api.TakipKartGecisAsync(kart.Id, g), n))
        { _gecis.Temizle(kart.Id); GecisDurumu(null, null); FormuKapat(KartFormu.Gecis); Mesaj = "Yeni takip açıldı; geçmiş kayıtlar korundu."; }
    });
    // Devir başka karta geçişte (KartFormlariniTemizle) ve düzeltmeden sonra temizlenir; aynı kartın yenilenmesinde korunur.
    private int? _devirKartId;
    private void DevirTemizle() { Devir = null; _devirKartId = null; DevirKalanBorc = DevirOncedenSayilan = 0; DevirAciklama = ""; DevirPaylari.Clear(); }
    /// <summary>Geçişli kartın eski borç devrini ve düzeltme sınırlarını okur; form güncel devirle doldurulur.</summary>
    [RelayCommand]
    private Task DevirYukleAsync() => YurutAsync(KartFormu.KartBilgisi, async n =>
    {
        if (Secili is not { YeniTakip: true, Gecis: not null } kart)
            return;
        var devir = await api.TakipKartDevirAsync(kart.Id);
        if (!Gecerli(n) || Secili?.Id != kart.Id)
            return;
        DevirTemizle();
        _devirKartId = kart.Id;
        Devir = devir;
        DevirKalanBorc = devir.KalanBorc;
        DevirOncedenSayilan = devir.KasadaOncedenSayilanTutar;
        foreach (var pay in devir.Dagilimlar.Where(p => p.KanalId is not null))
        {
            var satir = new TakipPayEditor(Kanallar.ToList()) { Kanal = Kanallar.FirstOrDefault(k => k.Id == pay.KanalId), Tutar = pay.Tutar };
            DevirPaylari.Add(satir);
        }
        DetaylariYansit();
        Tamamlandi();
    });
    /// <summary>Eski borç devrinin gerekçeli düzeltmesi (finance-2): etkin devir iptal edilir, aynı tarihle yeni tutar yazılır.</summary>
    [RelayCommand]
    private Task DevirDuzeltAsync() => YurutAsync(KartFormu.KartBilgisi, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart || Devir is not { } devir || _devirKartId != kart.Id)
            return;
        if (!devir.Duzeltilebilir)
        { Hata = devir.Engel ?? "Eski borç devri düzeltilemez."; return; }
        if (!ParaAyristirici.HepsiGecerli(DevirKalanBorc, DevirOncedenSayilan))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (string.IsNullOrWhiteSpace(DevirAciklama))
        { Hata = "Devir düzeltmesinin gerekçesini yazın."; return; }
        var g = new KartDevirDuzeltYaz(Guid.Empty, kart.Surum, devir.HarcamaId, DevirKalanBorc, DevirOncedenSayilan, TakipMetni.Paylar(DevirPaylari), DevirAciklama.Trim());
        g = g with { IstekId = _devirDuzelt.Al(kart.Id, new { kart.Id, g }) };
        if (Uygula(await api.TakipKartDevirDuzeltAsync(kart.Id, g), n))
        { _devirDuzelt.Temizle(kart.Id); DevirTemizle(); FormuKapat(KartFormu.KartBilgisi); Mesaj = "Eski borç devri düzeltildi; geçmiş kasa sonuçları korundu."; }
    });
    protected override void OturumTemizle()
    {
        Kartlar.Clear();
        Kanallar.Clear();
        Yeni();
        AcikForm = KartFormu.Yok;
        SeciliSekme = KartSekmesi.Ekstreler;
        HarcamaAciklama = OdemeNotu = Gerekce = GecisAciklama = "";
        HarcamaTutari = OdemeTutari = OncedenSayilan = GecisKalanBorc = 0;
        _oncedenSayilanElle = false;
        _kayit.Temizle();
        foreach (var key in new[] { _harcama, _odeme, _ekstre, _iptal, _durum, _gecis, _devirDuzelt })
            key.Temizle();
    }
}
