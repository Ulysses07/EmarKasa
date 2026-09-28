using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class IslemlerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    private readonly AuthViewModel? _auth;
    /// <summary>Yeni gider için tekrar anahtarı (appcore-5): istek zaman aşımına uğrayıp sunucuda yine de kaydedildiyse aynı
    /// formun yeniden gönderimi aynı kimliği taşır, sunucu ikinci gider açmaz. Başarıda, yeni formda ve düzenlemeye geçişte sıfırlanır.</summary>
    private readonly TekrarAnahtari _giderAnahtari = new();
    public BenzerKayitKontrolu GiderBenzerlik { get; }
    public IslemlerViewModel(IKasaApi api, IBenzerKayitApi? benzerlikApi = null, AuthViewModel? auth = null, TimeProvider? zaman = null)
    {
        _api = api; _auth = auth; _zaman = zaman ?? TimeProvider.System; GiderBenzerlik = new(benzerlikApi ?? api as IBenzerKayitApi);
        _listeHatti = new(Yurutucu); _kaynakHatti = new(Yurutucu); _gelenHatti = new(Yurutucu);
        if (auth is not null) OturumDegisiminiDinle(auth, OturumDegisti);
    }

    /// <summary>Oturum değişince bekleyen kayıt, liste ve gelir yanıtları eskir (sonuçları, hataları ve bitişleri yansımaz; nesli
    /// <see cref="TemelViewModel.OturumDegisiminiDinle"/> hemen artırır); eskiyen kayıt göstergeyi indirmeyeceği için burada
    /// indirilir, önceki oturumun formu, listesi ve iletileri UI bağlamında kalkar.</summary>
    private void OturumDegisti()
    {
        Mesgul = false; Hata = null;
        GiderBenzerlik.Temizle(); Yeni(); GelenTemizle(); ListeTemizle();
    }

    protected override void IletiyiTemizle() => Mesaj = null;

    /// <summary>Belirli bir kanala ait olmayan ortak gider etiketi (motorla birebir eşleşmeli).</summary>
    public const string OrtakKanal = "Ortak";

    /// <summary>Kanal filtresi "tüm kanallar" çip/etiket metni (gerçek kanal olamaz).</summary>
    private const string TumKanal = "Tümü";

    public ObservableCollection<IslemDto> Islemler { get; } = new();

    /// <summary>Gider formu kanal çipleri: aktif kanallar + "Ortak".</summary>
    public ObservableCollection<SecimCipi> GiderKanallari { get; } = new();

    /// <summary>Gider tipi çipleri: Diğer gider · Sabit gider · Kredi kartı.</summary>
    public ObservableCollection<SecimCipi> TipCipleri { get; } = new();

    /// <summary>Kart harcaması için kart çipleri (yalnız "Kredi kartı" tipi seçiliyken görünür). K3: yalnız yeni takipteki ve
    /// yeni kullanıma açık kartlar; düzenlenen eski kaydın eski kartı ayrıca "(eski kart)" olarak eklenir.</summary>
    public ObservableCollection<KartCipi> KartCipleri { get; } = new();

    /// <summary>K3 iletisi: sunucunun reddiyle aynı.</summary>
    public const string TakipliKartIletisi = "Kredi kartı gideri için yeni takipteki bir kart seçin. Kart eski takipteyse önce kart ekranından yeni takibe geçirin.";

    private IReadOnlyList<KrediKartiDto> _kartlar = [];
    /// <summary>Düzenlenen mevcut kayıt (yeni kayıtta null): eski kartsız/eski kartlı K.K kaydı kendi kartıyla kalabilir.</summary>
    private IslemDto? _duzenlenen;
    /// <summary>Düzenlenen giderin okunduğu andaki sürümü (contract-6; yeni kayıtta 0): kayıtla gönderilir, gider arada başka oturumda
    /// değiştiyse sunucu 409 verir.</summary>
    private int _duzenSurum;

    /// <summary>Gelen (kanal geliri) formu kanal çipleri: aktif kanallar.</summary>
    public ObservableCollection<SecimCipi> GelenKanallari { get; } = new();

    /// <summary>Liste filtresi kanal çipleri: "Tümü" + aktif kanallar + "Ortak".</summary>
    public ObservableCollection<SecimCipi> FiltreKanallari { get; } = new();

    /// <summary>Liste filtresi hızlı zaman çipleri: Tümü · Bu ay · Geçen ay.</summary>
    public ObservableCollection<SecimCipi> FiltreZamanlar { get; } = new();

    /// <summary>Dönem (hafta) seçici kaynağı — en yeni dönem üstte.</summary>
    public ObservableCollection<DonemDto> FiltreDonemler { get; } = new();

    private readonly List<DonemDto> _donemler = new();

    // Filtre kaynakları (IslemlerAsync'e geçer)
    [ObservableProperty] private string? _filtreKanal;        // null = tüm kanallar
    [ObservableProperty] private DateOnly? _filtreBaslangic;
    [ObservableProperty] private DateOnly? _filtreBitis;
    [ObservableProperty] private DonemDto? _seciliDonem;      // dönem picker seçimi
    [ObservableProperty] private decimal _filtreToplam;
    [ObservableProperty] private int _filtreSayi;
    [ObservableProperty] private string _filtreOzet = "";
    private string? _filtreZamanKod = TumKanal;               // null = dönem picker aktif
    /// <summary>Seçili haftanın başlangıcı: seçici kaynağı yenilenince aynı hafta geri seçilir.</summary>
    private DateOnly? _seciliDonemBaslangic;
    /// <summary>Seçici kaynağı yenilenirken Picker'ın yazdığı boş seçim ve geri seçim listeleme tetiklemez.</summary>
    private bool _donemlerYenileniyor;

    // Liste durumu: yalnız en son başlatılan liste isteğinin sonucu, hatası ve bitişi ekrana yansır.
    private readonly SonIstekHatti _listeHatti;
    // Kaynak (kanal, dönem, kart) durumu: yalnız en son başlatılan tam yüklemenin kaynakları uygulanır. Aradaki süzgeç
    // değişimi yalnız listeyi yeniler, kaynakları eskitmez; oturum değişimi eskitir.
    private readonly SonIstekHatti _kaynakHatti;
    [ObservableProperty] private bool _listeYukleniyor;
    /// <summary>Liste yükleme hatası (tüm rollere, listenin üstünde); form hataları <see cref="TemelViewModel.Hata"/>'da kalır.</summary>
    [ObservableProperty] private string? _yuklemeHatasi;
    /// <summary>Gösterilen liste güncel süzgecin başarılı yanıtıdır; yüklenirken ve hatada false (boş liste başlığı gizlenir).</summary>
    [ObservableProperty] private bool _veriVar;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private DateTimeOffset? _sonGuncelleme;
    /// <summary>Kayıt ve silme başarısı; liste yenilenemese de kaydın alındığını söyler.</summary>
    [ObservableProperty] private string? _mesaj;
    [ObservableProperty] private string _bosListeBasligi = "Henüz işlem yok";
    [ObservableProperty] private string _bosListeAciklamasi = "İlk kayıtla liste burada oluşur.";
    public string SonGuncellemeMetni => SonGuncelleme is { } zaman ? $"Son başarılı güncelleme: {zaman:dd.MM.yyyy HH:mm}" : "Liste henüz yüklenmedi.";

    /// <summary>Son başlatılan liste yüklemesi (dönem seçimi gibi beklenmeden başlayan yüklemeler için).</summary>
    public Task ListeYuklemesi { get; private set; } = Task.CompletedTask;

    /// <summary>Kanal, dönem ve kart kaynaklarını getirir; uygulanmaları güncellik denetiminden sonradır.</summary>
    private async Task<(IReadOnlyList<KanalDto> Kanallar, IReadOnlyList<DonemDto> Donemler, IReadOnlyList<KrediKartiDto> Kartlar)> KaynaklariGetirAsync()
        => (await _api.KanallarAsync(), await _api.DonemlerAsync(), await _api.KrediKartlariAsync());

    /// <summary>Kaynakları uygular: çipleri ve hafta seçicisini eşitler (liste ayrı istenir).</summary>
    private void KaynaklariUygula(IReadOnlyList<KanalDto> kanallar, IReadOnlyList<DonemDto> donemler, IReadOnlyList<KrediKartiDto> kartlar)
    {
        _kanallar = kanallar;

        var adlar = kanallar.Where(k => k.Aktif).OrderBy(k => k.Sira).Select(k => k.Ad).ToList();
        GelenKanallari.Clear();
        GiderKanallari.Clear();
        foreach (var ad in adlar) { GelenKanallari.Add(new SecimCipi(ad)); GiderKanallari.Add(new SecimCipi(ad)); }
        GiderKanallari.Add(new SecimCipi(OrtakKanal));
        SenkronSecim();

        if (TipCipleri.Count == 0)
            foreach (var t in new[] { GiderTipi.Cari, GiderTipi.SabitGider, GiderTipi.KrediKarti })
                TipCipleri.Add(new SecimCipi(TipAdi(t)));

        _kartlar = kartlar;
        KartCipleriniKur();
        TipVurgu();

        FiltreKanallari.Clear();
        FiltreKanallari.Add(new SecimCipi(TumKanal));
        foreach (var ad in adlar) FiltreKanallari.Add(new SecimCipi(ad));
        FiltreKanallari.Add(new SecimCipi(OrtakKanal));

        if (FiltreZamanlar.Count == 0)
        {
            FiltreZamanlar.Add(new SecimCipi(TumKanal));
            FiltreZamanlar.Add(new SecimCipi("Bu ay"));
            FiltreZamanlar.Add(new SecimCipi("Geçen ay"));
        }

        DonemleriEsitle(donemler);

        _donemler.Clear();
        _donemler.AddRange(donemler);

        FiltreVurgu();
    }

    /// <summary>Hafta seçici kaynağını yalnız değiştiyse yeniler ve seçili haftayı başlangıcıyla geri seçer. MAUI Picker
    /// kaynağı sıfırlanınca seçimi düşürüp VM'ye null yazar; bu yazım yok sayılır. Seçili hafta artık listede yoksa
    /// süzgeç görünmez bir aralıkta kalmasın diye "Tümü"ne döner.</summary>
    private void DonemleriEsitle(IReadOnlyList<DonemDto> donemler)
    {
        var yeni = donemler.OrderByDescending(d => d.Start).ToList();
        if (FiltreDonemler.SequenceEqual(yeni)) return;
        _donemlerYenileniyor = true;
        try
        {
            TakipMetni.Doldur(FiltreDonemler, yeni);
            SeciliDonem = _seciliDonemBaslangic is { } bas ? FiltreDonemler.FirstOrDefault(d => d.Start == bas) : null;
        }
        finally { _donemlerYenileniyor = false; }
        if (_seciliDonemBaslangic is not null && SeciliDonem is null)
        {
            _seciliDonemBaslangic = null;
            _filtreZamanKod = TumKanal;
            FiltreBaslangic = FiltreBitis = null;
        }
    }

    /// <summary>Liste isteğini başlatır; <paramref name="tam"/> ise önce kaynaklar (kanal, dönem, kart) yüklenir.
    /// Her istek numara alır ve yalnız en son başlatılanın sonucu, hatası ve bitişi ekrana yansır: eski süzgecin geç
    /// yanıtı yeni listeyi ve toplamı ezmez, önce biten eski istek yükleme göstergesini indirmez.</summary>
    /// <returns>Kaynaklar yüklendi mi (gelir formu ancak o zaman hazırlanır).</returns>
    private Task<bool> ListeyiYenile(bool tam = false)
    {
        var yukleme = ListeYukleAsync(_listeHatti.Baslat(), tam ? _kaynakHatti.Baslat() : null);
        ListeYuklemesi = yukleme;
        return yukleme;
    }

    /// <param name="kaynakIstek">Tam yüklemenin kaynak isteği bileti; yalnız liste isteniyorsa null.</param>
    private async Task<bool> ListeYukleAsync(IstekBileti istek, IstekBileti? kaynakIstek)
    {
        bool Guncel() => _listeHatti.Guncel(istek);
        ListeYukleniyor = true; YuklemeHatasi = null; VeriVar = false;
        var kaynaklar = kaynakIstek is null;
        try
        {
            if (kaynakIstek is { } k)
            {
                var (kanallar, donemler, kartlar) = await KaynaklariGetirAsync();
                // Sonra başlayan tam yükleme ya da oturum değişimi varken eski kaynaklar uygulanmaz: yeni kanal, kart ve
                // dönem listesini (ya da yeni oturumun ekranını) geç yanıt ezmez.
                if (!_kaynakHatti.Guncel(k)) return false;
                KaynaklariUygula(kanallar, donemler, kartlar); kaynaklar = true;
                if (!Guncel()) return kaynaklar;
            }
            // Süzgeç istek anında yakalanır; yanıt geldiğinde yalnız bu istek hâlâ en sonuncuysa uygulanır.
            var (bas, bit, kanal, suzgec) = (FiltreBaslangic, FiltreBitis, FiltreKanal, SuzgecMetni());
            var liste = await _api.IslemlerAsync(bas, bit, kanal, null);
            if (!Guncel()) return kaynaklar;
            ListeyiUygula(liste, suzgec, bas is not null || bit is not null || kanal is not null);
            VeriVar = true;
            SonGuncelleme = _zaman.GetLocalNow();
        }
        catch (Exception hata)
        {
            // Hatada eski süzgecin listesi ve toplamı gösterilmez; "Henüz işlem yok" da görünmez (VeriVar false). Liste ve
            // kaynaklar salt okumadır: zaman aşımında "sunucuda tamamlanmış olabilir" denmez.
            if (Guncel()) { YuklemeHatasi = OkumaHataMesaji(hata); ListeyiBosalt(); }
        }
        finally { if (Guncel()) ListeYukleniyor = false; }
        return kaynaklar;
    }

    private void ListeyiUygula(IReadOnlyList<IslemDto> liste, string suzgec, bool suzgecli)
    {
        TakipMetni.Doldur(Islemler, liste);
        FiltreSayi = liste.Count;
        FiltreToplam = liste.Sum(i => i.TutarTl);
        FiltreOzet = $"{suzgec} · {FiltreSayi} işlem · toplam {Bicim.Tl(FiltreToplam)} ₺";
        (BosListeBasligi, BosListeAciklamasi) = suzgecli
            ? ("Bu süzgeçte işlem yok", "Süzgeci değiştirin ya da kanal ve tarihte \"Tümü\"nü seçin.")
            : ("Henüz işlem yok", "İlk kayıtla liste burada oluşur.");
    }

    private void ListeyiBosalt() { Islemler.Clear(); FiltreSayi = 0; FiltreToplam = 0; FiltreOzet = ""; }

    /// <summary>Oturum değişince bekleyen liste yanıtları uygulanmaz, önceki oturumun listesi ve iletileri kalkar.</summary>
    private void ListeTemizle()
    {
        _listeHatti.Birak(); _kaynakHatti.Birak();
        ListeyiBosalt();
        ListeYukleniyor = false; VeriVar = false; YuklemeHatasi = null; SonGuncelleme = null; Mesaj = null;
    }

    /// <summary>Etkin süzgecin okunur hali; seçici bir an boş görünse de listenin hangi aralığa süzüldüğü okunur.</summary>
    private string SuzgecMetni()
    {
        var aralik = _filtreZamanKod switch
        {
            null => FiltreBaslangic is { } bas && FiltreBitis is { } bit ? Bicim.Aralik(bas, bit) : "Tüm tarihler",
            TumKanal => "Tüm tarihler",
            var kod => kod,
        };
        return FiltreKanal is { } kanal ? $"{kanal} · {aralik}" : aralik;
    }

    /// <summary>Kaydedilen gider etkin süzgeçte (tarih aralığı ve kanal) görünür mü.</summary>
    private bool SuzgecteGorunur(DateOnly tarih, string kanal)
        => (FiltreBaslangic is not { } bas || tarih >= bas) && (FiltreBitis is not { } bit || tarih <= bit) && (FiltreKanal is null || FiltreKanal == kanal);

    /// <summary>Editör form çiplerindeki "seçili" işaretini geçerli kanal değerleriyle eşitler.</summary>
    private void SenkronSecim()
    {
        foreach (var k in GiderKanallari) k.Secili = k.Ad == DuzenKanal;
        foreach (var k in GelenKanallari) k.Secili = k.Ad == GelenKanal;
    }

    /// <summary>Filtre çiplerinin "seçili" işaretini geçerli filtre durumuyla eşitler.</summary>
    private void FiltreVurgu()
    {
        foreach (var k in FiltreKanallari)
            k.Secili = (k.Ad == TumKanal && FiltreKanal is null) || k.Ad == FiltreKanal;
        foreach (var z in FiltreZamanlar)
            z.Secili = z.Ad == _filtreZamanKod;
    }

    private Task YenidenListele() => ListeyiYenile();

    // ---- Filtre komutları ----
    [RelayCommand]
    private Task SecFiltreKanalAsync(SecimCipi s)
    {
        FiltreKanal = s.Ad == TumKanal ? null : s.Ad;   // OnFiltreKanalChanged vurguyu günceller
        return YenidenListele();
    }

    [RelayCommand]
    private Task SecFiltreZamanAsync(SecimCipi s)
    {
        _filtreZamanKod = s.Ad;
        _seciliDonemBaslangic = null;
        SeciliDonem = null;                              // dönem picker'ı temizle (OnChanged erken döner)
        (FiltreBaslangic, FiltreBitis) = ZamanAralik(s.Ad);
        FiltreVurgu();
        return YenidenListele();
    }

    partial void OnFiltreKanalChanged(string? value) => FiltreVurgu();

    partial void OnSeciliDonemChanged(DonemDto? value)
    {
        if (_donemlerYenileniyor)
        {
            // Seçici kaynağı yenilenirken: Picker'ın yazdığı boş seçim yok sayılır, geri seçilen haftanın güncel aralığı
            // alınır. Liste, yenilemeyi yapan tam yüklemenin sonunda zaten istenir.
            if (value is not null) (FiltreBaslangic, FiltreBitis) = (value.Start, value.End);
            return;
        }
        if (value is null) return;                       // temizleme; zaman çipi zaten güncellendi
        _seciliDonemBaslangic = value.Start;
        _filtreZamanKod = null;
        FiltreBaslangic = value.Start;
        FiltreBitis = value.End;
        FiltreVurgu();
        _ = YenidenListele();
    }

    /// <summary>Hızlı zaman çipi kodunu [baslangic, bitis] aralığına çevirir.</summary>
    private (DateOnly?, DateOnly?) ZamanAralik(string kod)
    {
        var bugun = Bugun;
        if (kod == "Bu ay")
            return (new DateOnly(bugun.Year, bugun.Month, 1),
                    new DateOnly(bugun.Year, bugun.Month, DateTime.DaysInMonth(bugun.Year, bugun.Month)));
        if (kod == "Geçen ay")
        {
            var g = bugun.AddMonths(-1);
            return (new DateOnly(g.Year, g.Month, 1),
                    new DateOnly(g.Year, g.Month, DateTime.DaysInMonth(g.Year, g.Month)));
        }
        return (null, null); // Tümü
    }

    /// <summary>Ekrana gelişte ve "Yenile / tekrar dene"de tam yükleme. Hata listenin üstündeki durum şeridinde
    /// (<see cref="YuklemeHatasi"/>) görünür; form hatası alanı temizlenir.</summary>
    public async Task YukleAsync()
    {
        Hata = null; Mesaj = null;
        if (!await ListeyiYenile(tam: true)) return;
        if (_auth is null || _auth.AktifRol == Rol.Editor) await GelenFormunuHazirlaAsync();
    }

    [RelayCommand] private Task YenileAsync() => YukleAsync();

    [ObservableProperty] private bool _editorMu;

    // İşlem düzenleme
    [ObservableProperty] private int _duzenId;          // 0 = yeni
    [ObservableProperty] private DateTime _duzenTarih = DateTime.Today;
    [ObservableProperty] private string _duzenCari = "";
    [ObservableProperty] private decimal _duzenTutar;
    [ObservableProperty] private string _duzenKanal = "";
    [ObservableProperty] private GiderTipi _duzenTip = GiderTipi.Cari;
    [ObservableProperty] private string? _duzenNot;
    [ObservableProperty] private int? _duzenKrediKartiId;   // dolu = kart harcaması
    /// <summary>Yeni kart giderinin taksit sayısı (1–60) ve isteğe bağlı ilk kesimi (gap-coklu-giris-cift-sayim-mutabakat-6).</summary>
    [ObservableProperty] private int _duzenTaksitSayisi = 1;
    [ObservableProperty] private bool _duzenIlkKesimVar;
    [ObservableProperty] private DateTime _duzenIlkKesimTarihi = DateTime.Today;

    /// <summary>Kart seçici yalnız "Kredi kartı" tipi seçiliyken görünür.</summary>
    public bool KartSeciciGorunur => DuzenTip == GiderTipi.KrediKarti;
    /// <summary>Taksit yalnız yeni takipli kart giderinde girilir; düzenlemede plan değişmez (ödenmemişse gider silinip yeniden girilir).</summary>
    public bool TaksitGirilebilir => DuzenId == 0 && DuzenTip == GiderTipi.KrediKarti && DuzenKrediKartiId is not null;
    partial void OnDuzenIdChanged(int value) => OnPropertyChanged(nameof(TaksitGirilebilir));

    /// <summary>Formun gönderilecek gövdesi; taksit alanları yalnız taksit girilebilirken ve tek taksitten farklıysa doludur.</summary>
    private IslemYaz FormGovdesi()
    {
        var taksitli = TaksitGirilebilir && (DuzenTaksitSayisi != 1 || DuzenIlkKesimVar);
        return new IslemYaz(DateOnly.FromDateTime(DuzenTarih), DuzenCari, DuzenTutar, DuzenKanal, DuzenTip, DuzenNot, DuzenKrediKartiId,
            TaksitSayisi: taksitli && DuzenTaksitSayisi > 1 ? DuzenTaksitSayisi : null,
            IlkKesimTarihi: taksitli && DuzenIlkKesimVar ? DateOnly.FromDateTime(DuzenIlkKesimTarihi) : null, Surum: _duzenSurum);
    }

    /// <summary>K3: kredi kartı seçiliyken seçilebilecek takipli kart yoksa yol gösterir.</summary>
    public string? KartUyarisi => KartSeciciGorunur && KartCipleri.Count == 0
        ? "Takipte kart yok. Kredi Kartları bölümünden kart ekleyin ya da eski kartı yeni takibe geçirin." : null;

    /// <summary>Çipler: yeni takipteki açık kartlar; düzenlenen eski K.K kaydının eski kartı (listede yoksa) sonda.</summary>
    private void KartCipleriniKur()
    {
        KartCipleri.Clear();
        foreach (var k in _kartlar.Where(k => k.YeniTakip && k.Aktif)) KartCipleri.Add(new KartCipi(k.Id, k.Ad));
        if (_duzenlenen is { Tip: GiderTipi.KrediKarti, KrediKartiId: { } eski } && KartCipleri.All(k => k.Id != eski))
            KartCipleri.Add(new KartCipi(eski, (_kartlar.FirstOrDefault(k => k.Id == eski)?.Ad ?? "Kart") + " (eski kart)"));
        KartVurgu();
        OnPropertyChanged(nameof(KartUyarisi));
    }

    /// <summary>K3: yeni kredi kartı gideri kartsız kaydedilmez (seçenekler yalnız takipteki kartlardır; sunucu da doğrular).
    /// Mevcut kartsız eski K.K kaydının tutar/not/tarih düzeltmesi serbesttir.</summary>
    private bool KartsizEskiKayit => DuzenId != 0 && _duzenlenen is { Tip: GiderTipi.KrediKarti, KrediKartiId: null } d && d.Id == DuzenId;

    /// <summary>Gider tipi → çip etiketi.</summary>
    private static string TipAdi(GiderTipi t) => t switch
    {
        GiderTipi.SabitGider => "Sabit gider",
        GiderTipi.KrediKarti => "Kredi kartı",
        _ => "Diğer gider",
    };

    /// <summary>Çip etiketi → gider tipi.</summary>
    private static GiderTipi TipDegeri(string ad) => ad switch
    {
        "Sabit gider" => GiderTipi.SabitGider,
        "Kredi kartı" => GiderTipi.KrediKarti,
        _ => GiderTipi.Cari,
    };

    // Çip seçimleri kanal değerini ayarlar; işaretleme OnXChanged içinde eşitlenir.
    [RelayCommand] private void SecGiderKanal(SecimCipi s) => DuzenKanal = s.Ad;
    [RelayCommand] private void SecGelenKanal(SecimCipi s) => GelenKanal = s.Ad;
    [RelayCommand] private void SecTip(SecimCipi s) => DuzenTip = TipDegeri(s.Ad);
    [RelayCommand] private void SecKart(KartCipi s) => DuzenKrediKartiId = s.Id;

    partial void OnDuzenKanalChanged(string value)
    {
        foreach (var k in GiderKanallari) k.Secili = k.Ad == value;
    }

    partial void OnDuzenTipChanged(GiderTipi value)
    {
        TipVurgu();
        OnPropertyChanged(nameof(KartSeciciGorunur));
        OnPropertyChanged(nameof(KartUyarisi));
        OnPropertyChanged(nameof(TaksitGirilebilir));
        if (value != GiderTipi.KrediKarti) DuzenKrediKartiId = null;
    }

    partial void OnDuzenKrediKartiIdChanged(int? value) { KartVurgu(); OnPropertyChanged(nameof(TaksitGirilebilir)); }

    private void TipVurgu()
    {
        var ad = TipAdi(DuzenTip);
        foreach (var t in TipCipleri) t.Secili = t.Ad == ad;
    }

    private void KartVurgu()
    {
        foreach (var k in KartCipleri) k.Secili = k.Id == DuzenKrediKartiId;
    }

    [RelayCommand]
    private void Yeni()
    {
        GiderBenzerlik.Temizle(); _giderAnahtari.Temizle();
        DuzenId = 0; DuzenTarih = DateTime.Today; DuzenCari = "";
        DuzenTutar = 0; DuzenKanal = ""; DuzenTip = GiderTipi.Cari; DuzenNot = null;
        DuzenKrediKartiId = null;
        DuzenTaksitSayisi = 1; DuzenIlkKesimVar = false; DuzenIlkKesimTarihi = DateTime.Today;
        _duzenlenen = null; _duzenSurum = 0; KartCipleriniKur();
    }

    [RelayCommand]
    public void Duzenle(IslemDto i)
    {
        if (i.EkstreKayitId is not null) { Hata = "Bu kayıt PDF ekstresinden aktarıldı. Ekstre İçe Aktar bölümünden iptal edip doğru bilgilerle yeniden kaydedin."; return; }
        if (i.AylikGiderOdemeId is not null) { Hata = "Bu ödeme Aylık Giderler bölümüne bağlı. Düzeltmek için o bölümde iptal edip yeniden ödeme kaydedin."; return; }
        if (i.AlisId is not null) { Hata = "Bu gider bir alışa bağlı. Dağılımı Alışlar ekranında iade / düzenle / onayla adımlarıyla değiştirin."; return; }
        GiderBenzerlik.Temizle(); _giderAnahtari.Temizle();
        _duzenlenen = i; _duzenSurum = i.Surum;
        DuzenId = i.Id; DuzenTarih = i.Tarih.ToDateTime(TimeOnly.MinValue);
        DuzenCari = i.Cari; DuzenTutar = i.TutarTl; DuzenKanal = i.Kanal;
        DuzenTip = i.Tip; DuzenNot = i.Not; DuzenKrediKartiId = i.KrediKartiId;
        KartCipleriniKur();
    }

    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (_auth is not null && _auth.AktifRol != Rol.Editor) return;
        if (!ParaAyristirici.GecerliMi(DuzenTutar)) { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (DuzenTip == GiderTipi.KrediKarti && DuzenKrediKartiId is null && !KartsizEskiKayit) { Hata = TakipliKartIletisi; return; }
        if (TaksitGirilebilir && DuzenTaksitSayisi is < 1 or > 60) { Hata = "Taksit sayısı 1 ile 60 arasında olmalı."; return; }
        if (TaksitGirilebilir && DuzenIlkKesimVar && DuzenIlkKesimTarihi.Date < DuzenTarih.Date) { Hata = "İlk kesim tarihi harcamadan önce olamaz."; return; }
        var g = FormGovdesi();
        var id = DuzenId;
        if (id == 0 && !await GiderBenzerlik.DevamEdilebilirAsync(new("Gider", g.Tarih, g.TutarTl, g.KrediKartiId, g.Kanal), g,
            () => Gecerli(n) && DuzenId == id && TakipMetni.Ayni(g, FormGovdesi()))) return;
        try
        {
            if (DuzenId == 0) await _api.IslemOlusturAsync(g with { IstekId = _giderAnahtari.Al(g) });
            else await _api.IslemGuncelleAsync(DuzenId, g);
        }
        // contract-6: düzenlenen gider arada başka oturumda değiştiyse (409) ileti gösterilir ve liste güncel kayıtlarla yenilenir;
        // form korunur, gider listeden yeniden açılınca güncel sürümüyle kaydedilir.
        catch (KasaApiException e) when (e.DurumKodu == HttpStatusCode.Conflict && id != 0)
        {
            if (Gecerli(n)) await ListeyiYenile(tam: true);
            throw;
        }
        if (!Gecerli(n)) return;
        // Liste yenilenemese de kayıt alınmıştır: başarı ayrı söylenir (liste hatası durum şeridinde), form temizlenir.
        Mesaj = (id == 0 ? "Gider kaydedildi" : "Gider güncellendi")
            + (SuzgecteGorunur(g.Tarih, g.Kanal) ? "." : $"; seçili süzgeç ({SuzgecMetni()}) dışında kaldığı için listede görünmüyor.");
        Yeni();
        await ListeyiYenile(tam: true);
    });

    [RelayCommand] private async Task GideriAyriKaydetAsync() { if (GiderBenzerlik.Onayla()) await KaydetAsync(); }

    /// <summary>Onay diyaloğundan sonra gelir: kayıt ya da gelir kaydı sürerken silme yapılmaz ve bu söylenir (sessizce yok sayılmaz).</summary>
    [RelayCommand]
    private Task SilAsync(IslemDto i) => YurutAsync(async n =>
    {
        if (i.EkstreKayitId is not null) { Hata = "Ekstre kaydı buradan silinemez. Ekstre İçe Aktar bölümünden gerekçeyle iptal edin."; return; }
        if (i.AylikGiderOdemeId is not null) { Hata = "Aylık gider ödemesi buradan silinemez. Aylık Giderler bölümünden gerekçeyle iptal edin."; return; }
        if (i.AlisId is not null) { Hata = "Bu gider bir alış ödemesine bağlı; bu ekrandan silinemez. Alışlar ekranından kaydı inceleyin."; return; }
        await _api.IslemSilAsync(i.Id);
        if (!Gecerli(n)) return;
        Mesaj = "Kayıt silindi.";
        await ListeyiYenile(tam: true);
    }, mesgulkenBildir: true);
}

/// <summary>Seçilebilir çip: ad + seçili durumu (çip görünümü buna göre değişir).</summary>
public partial class SecimCipi : ObservableObject
{
    public string Ad { get; }
    public SecimCipi(string ad) => Ad = ad;
    [ObservableProperty] private bool _secili;
}
