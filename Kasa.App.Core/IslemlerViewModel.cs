using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class IslemlerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;

    public IslemlerViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        _varsayilanGun = Bugun;
        _duzenTarih = _varsayilanGun;
        _gelenTarih = _varsayilanGun;
        (_filtreBaslangic, _filtreBitis) = ZamanAralik(_filtreZamanKod!);
    }

    /// <summary>Belirli bir kanala ait olmayan ortak gider etiketi (motorla birebir eşleşmeli).</summary>
    public const string OrtakKanal = "Ortak";

    /// <summary>Kanal filtresi "tüm kanallar" çip/etiket metni (gerçek kanal olamaz).</summary>
    private const string TumKanal = "Tümü";
    private const string BuAy = "Bu ay";
    private const string GecenAy = "Geçen ay";

    public ObservableCollection<IslemDto> Islemler { get; } = new();

    /// <summary>Gider formu kanal çipleri: aktif kanallar + "Ortak".</summary>
    public ObservableCollection<SecimCipi> GiderKanallari { get; } = new();

    /// <summary>Gider tipi çipleri: Cari · Sabit gider · Kredi kartı.</summary>
    public ObservableCollection<SecimCipi> TipCipleri { get; } = new();

    /// <summary>Kart harcaması için kart çipleri (yalnız "Kredi kartı" tipi seçiliyken görünür).</summary>
    public ObservableCollection<KartCipi> KartCipleri { get; } = new();

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
    private string? _filtreZamanKod = BuAy;                   // null = dönem picker aktif; varsayılan: bu ay

    /// <summary>Son dönem seçimi (Picker kaynak listesi yenilenirken seçimi geri yüklemek için).</summary>
    private DonemDto? _sonSeciliDonem;
    /// <summary>Dönem listesi yeniden kuruluyor: Picker'ın ittiği null/yeniden seçim yok sayılır.</summary>
    private bool _donemlerKuruluyor;
    /// <summary>İşlem listesi istek sürümü: eski (geç gelen) yanıt yeni filtrenin sonucunu ezmesin.</summary>
    private int _listeSurumu;

    /// <summary>Formların "bugün" varsayılanının kurulduğu gün (gece yarısından sonra tazelenir).</summary>
    private DateTime _varsayilanGun;

    private async Task DoldurAsync()
    {
        VarsayilanTarihleriTazele();
        if (_filtreZamanKod is { } kod) (FiltreBaslangic, FiltreBitis) = ZamanAralik(kod);   // "Bu ay" ay değişince kayar

        var kanalGorevi = _api.KanallarAsync();
        var donemGorevi = _api.DonemlerAsync();
        var kartGorevi = _api.KrediKartlariAsync();
        var cariGorevi = _api.CarilerAsync();
        var kalemGorevi = _api.GiderKalemleriAsync();
        var listeGorevi = IslemleriYukleAsync();
        await Task.WhenAll(kanalGorevi, donemGorevi, kartGorevi, cariGorevi, kalemGorevi, listeGorevi);
        var kanallar = kanalGorevi.Result;
        var donemler = donemGorevi.Result;
        var kartlar = kartGorevi.Result;
        CarileriKur(cariGorevi.Result);
        KalemleriKur(kalemGorevi.Result);

        var adlar = kanallar.Where(k => k.Aktif).OrderBy(k => k.Sira).Select(k => k.Ad).ToList();
        GelenKanallari.Clear();
        GiderKanallari.Clear();
        foreach (var ad in adlar) { GelenKanallari.Add(new SecimCipi(ad)); GiderKanallari.Add(new SecimCipi(ad)); }
        GiderKanallari.Add(new SecimCipi(OrtakKanal));
        SenkronSecim();

        if (TipCipleri.Count == 0)
            foreach (var t in new[] { GiderTipi.Cari, GiderTipi.SabitGider, GiderTipi.KrediKarti })
                TipCipleri.Add(new SecimCipi(TipAdi(t)));

        KartCipleri.Clear();
        foreach (var k in kartlar) KartCipleri.Add(new KartCipi(k.Id, k.Ad));
        OnPropertyChanged(nameof(KartYok));
        TipVurgu();
        KartVurgu();

        FiltreKanallari.Clear();
        FiltreKanallari.Add(new SecimCipi(TumKanal));
        foreach (var ad in adlar) FiltreKanallari.Add(new SecimCipi(ad));
        FiltreKanallari.Add(new SecimCipi(OrtakKanal));

        if (FiltreZamanlar.Count == 0)
        {
            FiltreZamanlar.Add(new SecimCipi(TumKanal));
            FiltreZamanlar.Add(new SecimCipi(BuAy));
            FiltreZamanlar.Add(new SecimCipi(GecenAy));
        }

        DonemleriKur(donemler);
        FiltreVurgu();
    }

    /// <summary>
    /// Dönem listesini (değiştiyse) yeniden kurar ve seçili dönemi korur. Picker, kaynak listesi
    /// temizlenince SelectedItem=null iter; bu sırada filtre durumu bozulmaz.
    /// </summary>
    private void DonemleriKur(IReadOnlyList<DonemDto> donemler)
    {
        _donemler.Clear();
        _donemler.AddRange(donemler);

        var sirali = donemler.OrderByDescending(d => d.Start).ToList();
        if (!sirali.SequenceEqual(FiltreDonemler))
        {
            _donemlerKuruluyor = true;
            try
            {
                FiltreDonemler.Clear();
                foreach (var d in sirali) FiltreDonemler.Add(d);
            }
            finally { _donemlerKuruluyor = false; }
        }

        // Dönem filtresi etkinse seçimi geri yükle (Picker null itmiş olabilir).
        if (_filtreZamanKod is null && _sonSeciliDonem is { } s)
        {
            var eslesen = FiltreDonemler.FirstOrDefault(d => d == s);
            if (eslesen is not null)
            {
                _donemlerKuruluyor = true;
                try { SeciliDonem = eslesen; }
                finally { _donemlerKuruluyor = false; }
            }
        }
    }

    /// <summary>Tarih bilinen dönemlerin dışındaysa dönem listesini yeniler (gün/hafta değişmiş olabilir).</summary>
    private async Task DonemleriGerekirseYenileAsync(DateOnly tarih)
    {
        if (DonemBul(tarih) is not null) return;
        DonemleriKur(await _api.DonemlerAsync());
    }

    private DonemDto? DonemBul(DateOnly tarih) => _donemler.FirstOrDefault(d => tarih >= d.Start && tarih <= d.End);

    /// <summary>Tek istekte çekilen en fazla işlem sayısı ("Daha eski işlemler" her basışta bir sayfa daha).</summary>
    public const int SayfaBoyutu = 500;

    /// <summary>Yüklü en eski kaydın sunucu sırasındaki (tarih, id artan) konumu; 0 = hepsi yüklü.</summary>
    private int _yukluOfset;
    /// <summary>Son yüklemenin filtresi (daha eski sayfa aynı filtreyle istenir).</summary>
    private (DateOnly? Bas, DateOnly? Bit, string? Kanal) _yukluFiltre;
    private bool _eskiYukleniyor;

    /// <summary>Filtreye uyan toplam işlem sayısı (sunucudan; yüklü olandan fazla olabilir).</summary>
    [ObservableProperty] private int _filtreToplamKayit;

    /// <summary>Filtreye uyan ama henüz yüklenmemiş daha eski işlemler var.</summary>
    [ObservableProperty] private bool _dahaEskiVar;

    /// <summary>
    /// Seçili filtreyle işlem listesini + özet toplamı yeniler (en yeni önce). Liste sayfalıdır:
    /// en yeni <see cref="SayfaBoyutu"/> işlem yüklenir. Eski (geç gelen) yanıtlar atılır.
    /// </summary>
    private async Task IslemleriYukleAsync()
    {
        var surum = ++_listeSurumu;
        var (bas, bit, kanal) = (FiltreBaslangic, FiltreBitis, FiltreKanal);
        IslemSayfasi sayfa;
        int ofset = 0;
        try
        {
            // Sunucu eskiden yeniye sıralar: ilk sayfa toplamı verir; fazlaysa en yeni sayfa istenir.
            sayfa = await _api.IslemSayfasiAsync(bas, bit, kanal, null, SayfaBoyutu, 0);
            if (sayfa.Toplam > sayfa.Kayitlar.Count && surum == _listeSurumu)
            {
                ofset = Math.Max(0, sayfa.Toplam - SayfaBoyutu);
                sayfa = await _api.IslemSayfasiAsync(bas, bit, kanal, null, SayfaBoyutu, ofset);
            }
        }
        catch when (surum != _listeSurumu)
        {
            return;   // bu arada yeni filtre istendi; eski isteğin hatası önemsiz
        }
        if (surum != _listeSurumu) return;

        Islemler.Clear();
        foreach (var i in sayfa.Kayitlar.OrderByDescending(i => i.Tarih).ThenByDescending(i => i.Id)) Islemler.Add(i);
        _yukluOfset = ofset;
        _yukluFiltre = (bas, bit, kanal);
        FiltreToplamKayit = Math.Max(sayfa.Toplam, Islemler.Count);
        OzetiGuncelle();
    }

    /// <summary>Bir sayfa daha eski işlemi listenin sonuna ekler.</summary>
    [RelayCommand]
    private Task DahaEskiYukleAsync() => CalistirAsync(async () =>
    {
        if (_yukluOfset <= 0 || _eskiYukleniyor) return;
        _eskiYukleniyor = true;
        try
        {
            var surum = _listeSurumu;
            var (bas, bit, kanal) = _yukluFiltre;
            var yeniOfset = Math.Max(0, _yukluOfset - SayfaBoyutu);
            var sayfa = await _api.IslemSayfasiAsync(bas, bit, kanal, null, _yukluOfset - yeniOfset, yeniOfset);
            if (surum != _listeSurumu) return;   // bu arada liste yeniden yüklendi

            var mevcut = Islemler.Select(i => i.Id).ToHashSet();
            foreach (var i in sayfa.Kayitlar.OrderByDescending(i => i.Tarih).ThenByDescending(i => i.Id))
                if (mevcut.Add(i.Id)) Islemler.Add(i);
            _yukluOfset = yeniOfset;
            FiltreToplamKayit = Math.Max(sayfa.Toplam, Islemler.Count);
            OzetiGuncelle();
        }
        finally { _eskiYukleniyor = false; }
    });

    private void OzetiGuncelle()
    {
        DahaEskiVar = _yukluOfset > 0;
        FiltreSayi = Islemler.Count;
        FiltreToplam = Islemler.Sum(i => i.TutarTl);
        var (bas, bit, _) = _yukluFiltre;
        FiltreOzet = DahaEskiVar
            ? $"{AralikMetni(bas, bit)} · {FiltreToplamKayit} işlem (en yeni {FiltreSayi} gösteriliyor) · gösterilen toplam {Bicim.Tl(FiltreToplam)} ₺"
            : $"{AralikMetni(bas, bit)} · {FiltreSayi} işlem · toplam {Bicim.Tl(FiltreToplam)} ₺";
    }

    /// <summary>Filtre aralığının okunur metni (filtre her zaman görünür olsun).</summary>
    private static string AralikMetni(DateOnly? bas, DateOnly? bit) => (bas, bit) switch
    {
        (null, null) => "Tüm tarihler",
        ({ } b, { } s) => $"{b.ToString("d MMM yyyy", Kultur.Turkce)} – {s.ToString("d MMM yyyy", Kultur.Turkce)}",
        ({ } b, null) => $"{b.ToString("d MMM yyyy", Kultur.Turkce)} sonrası",
        (null, { } s) => $"{s.ToString("d MMM yyyy", Kultur.Turkce)} öncesi",
    };

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

    private Task YenidenListele() => CalistirAsync(IslemleriYukleAsync);

    // ---- Filtre komutları ----
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecFiltreKanalAsync(SecimCipi s)
    {
        FiltreKanal = s.Ad == TumKanal ? null : s.Ad;   // OnFiltreKanalChanged vurguyu günceller
        return YenidenListele();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecFiltreZamanAsync(SecimCipi s)
    {
        _filtreZamanKod = s.Ad;
        _sonSeciliDonem = null;
        SeciliDonem = null;                              // dönem picker'ı temizle (OnChanged erken döner)
        (FiltreBaslangic, FiltreBitis) = ZamanAralik(s.Ad);
        FiltreVurgu();
        return YenidenListele();
    }

    partial void OnFiltreKanalChanged(string? value) => FiltreVurgu();

    partial void OnSeciliDonemChanged(DonemDto? value)
    {
        if (_donemlerKuruluyor) return;                  // liste yenilenirken Picker'ın ittiği değer
        if (value is null)
        {
            // Zaman çipi seçimi temizlediyse (_filtreZamanKod dolu) sorun yok. Dönem filtresi hâlâ
            // etkinken gelen null, Picker'ın kaynak değişiminde ittiği değerdir: seçimi geri koy.
            if (_filtreZamanKod is null && _sonSeciliDonem is { } s && FiltreDonemler.Contains(s))
                SeciliDonem = s;
            return;
        }
        _filtreZamanKod = null;
        _sonSeciliDonem = value;
        FiltreBaslangic = value.Start;
        FiltreBitis = value.End;
        FiltreVurgu();
        _ = YenidenListele();
    }

    /// <summary>Hızlı zaman çipi kodunu [baslangic, bitis] aralığına çevirir.</summary>
    private (DateOnly?, DateOnly?) ZamanAralik(string kod)
    {
        var bugun = BugunTarih;
        if (kod == BuAy)
            return (new DateOnly(bugun.Year, bugun.Month, 1),
                    new DateOnly(bugun.Year, bugun.Month, DateTime.DaysInMonth(bugun.Year, bugun.Month)));
        if (kod == GecenAy)
        {
            var g = bugun.AddMonths(-1);
            return (new DateOnly(g.Year, g.Month, 1),
                    new DateOnly(g.Year, g.Month, DateTime.DaysInMonth(g.Year, g.Month)));
        }
        return (null, null); // Tümü
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    /// <summary>
    /// Açık kalan sayfada gün değiştiyse, kullanıcının dokunmadığı "bugün" varsayılanlarını
    /// yeni güne taşır.
    /// </summary>
    private void VarsayilanTarihleriTazele()
    {
        var bugun = Bugun;
        if (bugun == _varsayilanGun) return;
        if (DuzenId == 0 && DuzenTarih == _varsayilanGun) DuzenTarih = bugun;
        if (GelenTarih == _varsayilanGun) GelenTarih = bugun;
        _varsayilanGun = bugun;
    }

    [ObservableProperty] private bool _editorMu;

    // İşlem düzenleme
    [ObservableProperty] private int _duzenId;          // 0 = yeni
    [ObservableProperty] private DateTime _duzenTarih;
    [ObservableProperty] private string _duzenCari = "";
    [ObservableProperty] private decimal _duzenTutar;
    [ObservableProperty] private string _duzenKanal = "";
    [ObservableProperty] private GiderTipi _duzenTip = GiderTipi.Cari;
    [ObservableProperty] private string? _duzenNot;
    [ObservableProperty] private int? _duzenKrediKartiId;   // dolu = kart harcaması

    // ---- Cari seçimi (işlemin carisi Cariler listesinde kayıtlı olmalı) ----

    /// <summary>Kayıtlı cari adları (doğrulama; pasifler dahil).</summary>
    private readonly List<string> _cariAdlari = new();
    /// <summary>Öneri kaynağı: aktif cariler.</summary>
    private readonly List<string> _aktifCariler = new();

    /// <summary>Cari kutusuna yazılana uyan kayıtlı cariler (dokununca seçilir).</summary>
    public ObservableCollection<string> CariOnerileri { get; } = new();

    [ObservableProperty] private bool _cariOnerileriGorunur;

    public const int EnFazlaOneri = 8;
    public const string CariBosMesaji = "Bir cari seçin.";
    public static string CariYokMesaji(string ad) => $"'{ad}' adında bir cari yok. Önce Cariler sayfasından ekleyin.";

    // ---- Sabit gider kalemi (tip "Sabit gider" iken ad bu listeden seçilir) ----

    private readonly List<string> _kalemAdlari = new();
    private readonly List<string> _aktifKalemler = new();

    public const string KalemBosMesaji = "Bir sabit gider kalemi seçin.";
    public static string KalemYokMesaji(string ad) => $"'{ad}' adında bir sabit gider kalemi yok. \"Kalem olarak ekle\" ile ekleyin.";

    /// <summary>Tip "Sabit gider" mi (ad cari yerine gider kalemidir).</summary>
    public bool SabitGiderMi => DuzenTip == GiderTipi.SabitGider;

    /// <summary>Ad alanının etiketi: tipe göre "Cari" ya da "Gider kalemi".</summary>
    public string AdEtiketi => SabitGiderMi ? "Gider kalemi" : "Cari";

    /// <summary>Yazılan ad henüz kalem değil: formda "Kalem olarak ekle" düğmesi görünür.</summary>
    public bool KalemEklenebilir => SabitGiderMi && (DuzenCari?.Trim() ?? "").Length > 0 && KayitliKalem(DuzenCari!.Trim()) is null;

    private void KalemleriKur(IReadOnlyList<GiderKalemiDto> kalemler)
    {
        _kalemAdlari.Clear();
        _kalemAdlari.AddRange(kalemler.Select(k => k.Ad));
        _aktifKalemler.Clear();
        _aktifKalemler.AddRange(kalemler.Where(k => k.Aktif).Select(k => k.Ad)
            .OrderBy(a => a, StringComparer.Create(Kultur.Turkce, true)));
        OnerileriGuncelle();
    }

    private string? KayitliKalem(string ad)
        => _kalemAdlari.FirstOrDefault(a => string.Compare(a, ad, Kultur.Turkce, System.Globalization.CompareOptions.IgnoreCase) == 0);

    /// <summary>Formdaki adı yeni sabit gider kalemi olarak ekler ve seçer.</summary>
    [RelayCommand]
    private Task KalemEkleAsync() => CalistirAsync(async () =>
    {
        var ad = DuzenCari?.Trim() ?? "";
        Dogrula(ad.Length > 0, KalemBosMesaji);
        if (KayitliKalem(ad) is null)
        {
            await _api.GiderKalemiOlusturAsync(new GiderKalemiYaz(ad, true));
            KalemleriKur(await _api.GiderKalemleriAsync());
        }
        DuzenCari = KayitliKalem(ad) ?? ad;
    });

    private void CarileriKur(IReadOnlyList<CariDto> cariler)
    {
        _cariAdlari.Clear();
        _cariAdlari.AddRange(cariler.Select(c => c.Ad));
        _aktifCariler.Clear();
        _aktifCariler.AddRange(cariler.Where(c => c.Aktif).Select(c => c.Ad)
            .OrderBy(a => a, StringComparer.Create(Kultur.Turkce, true)));
        OnerileriGuncelle();
    }

    /// <summary>Kayıtlı cari adını (Türkçe büyük/küçük harf duyarsız) bulur; yoksa null.</summary>
    private string? KayitliCari(string ad)
        => _cariAdlari.FirstOrDefault(a => string.Compare(a, ad, Kultur.Turkce, System.Globalization.CompareOptions.IgnoreCase) == 0);

    private void OnerileriGuncelle()
    {
        CariOnerileri.Clear();
        OnPropertyChanged(nameof(KalemEklenebilir));
        var metin = DuzenCari?.Trim() ?? "";
        var adlar = SabitGiderMi ? _kalemAdlari : _cariAdlari;
        var aktifler = SabitGiderMi ? _aktifKalemler : _aktifCariler;
        // Tam eşleşen kayıtlı ad seçilmiş demektir: öneri listesi kapanır.
        if (metin.Length > 0 && adlar.Contains(metin))
        {
            CariOnerileriGorunur = false;
            return;
        }
        var ci = Kultur.Turkce.CompareInfo;
        foreach (var a in aktifler
                     .Where(a => metin.Length == 0 || ci.IndexOf(a, metin, System.Globalization.CompareOptions.IgnoreCase) >= 0)
                     .Take(EnFazlaOneri))
            CariOnerileri.Add(a);
        CariOnerileriGorunur = CariOnerileri.Count > 0;
    }

    partial void OnDuzenCariChanged(string value) => OnerileriGuncelle();

    [RelayCommand]
    private void SecCari(string ad) => DuzenCari = ad;

    /// <summary>
    /// Cari kayıtlı değilse kaydetmeyi durdurur (sunucu da 400 döner); yazım farkını kayıtlı yazıma
    /// çevirir. Liste eskimiş olabilir (başka sayfada cari eklendi): bulunamazsa bir kez tazelenir.
    /// </summary>
    private async Task CariyiDogrulaAsync()
    {
        var ad = DuzenCari?.Trim() ?? "";
        if (SabitGiderMi)
        {
            Dogrula(ad.Length > 0, KalemBosMesaji);
            if (KayitliKalem(ad) is null)
            {
                KalemleriKur(await _api.GiderKalemleriAsync());
                Dogrula(KayitliKalem(ad) is not null, KalemYokMesaji(ad));
            }
            DuzenCari = KayitliKalem(ad)!;
            return;
        }
        Dogrula(ad.Length > 0, CariBosMesaji);
        if (KayitliCari(ad) is null)
        {
            CarileriKur(await _api.CarilerAsync());
            Dogrula(KayitliCari(ad) is not null, CariYokMesaji(ad));
        }
        DuzenCari = KayitliCari(ad)!;
    }

    /// <summary>Düzenlenen kayıt, kart bağlantısı olmayan eski (Excel'den gelen) bir K.K satırı mı?</summary>
    private bool _duzenEskiKartsizKK;

    /// <summary>İleri tarihli işlem kaydı için onay bekleniyor (dolu = uyarı metni).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IleriTarihOnayBekliyor))]
    private string? _ileriTarihUyarisi;
    public bool IleriTarihOnayBekliyor => IleriTarihUyarisi is not null;

    /// <summary>Kart seçici yalnız "Kredi kartı" tipi seçiliyken görünür.</summary>
    public bool KartSeciciGorunur => DuzenTip == GiderTipi.KrediKarti;

    /// <summary>Hiç kart tanımlı değil (kart seçicide yönlendirme metni gösterilir).</summary>
    public bool KartYok => KartCipleri.Count == 0;

    public const string KartSecinMesaji = "Kredi kartı harcaması için bir kart seçin.";
    public const string KartYokMesaji = "Kredi kartı harcaması için önce Kredi Kartları sayfasından kart ekleyin.";

    /// <summary>Gider tipi → çip etiketi.</summary>
    private static string TipAdi(GiderTipi t) => t switch
    {
        GiderTipi.SabitGider => "Sabit gider",
        GiderTipi.KrediKarti => "Kredi kartı",
        _ => "Cari",
    };

    /// <summary>Çip etiketi → gider tipi.</summary>
    private static GiderTipi TipDegeri(string ad) => ad switch
    {
        "Sabit gider" => GiderTipi.SabitGider,
        "Kredi kartı" => GiderTipi.KrediKarti,
        _ => GiderTipi.Cari,
    };

    // Gelen girişi
    [ObservableProperty] private DateTime _gelenTarih;
    [ObservableProperty] private string _gelenKanal = "";
    [ObservableProperty] private decimal _gelenTutar;

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
        OnPropertyChanged(nameof(SabitGiderMi));
        OnPropertyChanged(nameof(AdEtiketi));
        OnerileriGuncelle();
        if (value != GiderTipi.KrediKarti) DuzenKrediKartiId = null;
    }

    partial void OnDuzenKrediKartiIdChanged(int? value) => KartVurgu();

    partial void OnDuzenTarihChanged(DateTime value) => IleriTarihUyarisi = null;   // tarih değişti → onay yeniden sorulur

    private void TipVurgu()
    {
        var ad = TipAdi(DuzenTip);
        foreach (var t in TipCipleri) t.Secili = t.Ad == ad;
    }

    private void KartVurgu()
    {
        foreach (var k in KartCipleri) k.Secili = k.Id == DuzenKrediKartiId;
    }

    partial void OnGelenKanalChanged(string value)
    {
        foreach (var k in GelenKanallari) k.Secili = k.Ad == value;
    }

    [RelayCommand]
    private void Yeni()
    {
        DuzenId = 0; DuzenTarih = Bugun; DuzenCari = "";
        DuzenTutar = 0; DuzenKanal = ""; DuzenTip = GiderTipi.Cari; DuzenNot = null;
        DuzenKrediKartiId = null;
        _duzenEskiKartsizKK = false;
        _varsayilanGun = Bugun;
        IleriTarihUyarisi = null;
    }

    [RelayCommand]
    public void Duzenle(IslemDto i)
    {
        DuzenId = i.Id; DuzenTarih = i.Tarih.ToDateTime(TimeOnly.MinValue);
        DuzenCari = i.Cari; DuzenTutar = i.TutarTl; DuzenKanal = i.Kanal;
        DuzenTip = i.Tip; DuzenNot = i.Not; DuzenKrediKartiId = i.KrediKartiId;
        _duzenEskiKartsizKK = i.Tip == GiderTipi.KrediKarti && i.KrediKartiId is null;
        IleriTarihUyarisi = null;
    }

    [RelayCommand]
    private Task KaydetAsync() => KaydetIcAsync(ileriTarihOnayli: false);

    /// <summary>İleri tarih uyarısındaki "Yine de kaydet".</summary>
    [RelayCommand]
    private Task IleriTarihOnaylaAsync() => KaydetIcAsync(ileriTarihOnayli: true);

    [RelayCommand]
    private void IleriTarihVazgec() => IleriTarihUyarisi = null;

    private Task KaydetIcAsync(bool ileriTarihOnayli) => CalistirAsync(async () =>
    {
        // Kart seçilmeden "Kredi kartı" kaydı, kartsız K.K olarak farklı muhasebeleşir (ay sonuna
        // ertelenir). Yalnız zaten kartsız olan eski kayıtların düzenlenmesine izin verilir.
        if (DuzenTip == GiderTipi.KrediKarti && DuzenKrediKartiId is null && !(DuzenId != 0 && _duzenEskiKartsizKK))
            Dogrula(false, KartYok ? KartYokMesaji : KartSecinMesaji);
        await CariyiDogrulaAsync();

        var tarih = DateOnly.FromDateTime(DuzenTarih);
        if (tarih > BugunTarih && !ileriTarihOnayli)
        {
            IleriTarihUyarisi =
                $"İşlem tarihi ileri bir gün ({tarih.ToString("d MMMM yyyy", Kultur.Turkce)}). " +
                "Bu işlem kasaya ve raporlara ancak o gün geldiğinde yansır. Yine de kaydedilsin mi?";
            return;
        }
        IleriTarihUyarisi = null;

        var g = new IslemYaz(tarih, DuzenCari, DuzenTutar, DuzenKanal, DuzenTip, DuzenNot, DuzenKrediKartiId);
        if (DuzenId == 0) await _api.IslemOlusturAsync(g);
        else await _api.IslemGuncelleAsync(DuzenId, g);
        Yeni();
        // Kanal/kart listeleri değişmedi: yalnız liste (ve gerekirse dönemler) yenilenir.
        await DonemleriGerekirseYenileAsync(tarih);
        await IslemleriYukleAsync();
    });

    [RelayCommand]
    private Task SilAsync(IslemDto i) => CalistirAsync(async () =>
    {
        await _api.IslemSilAsync(i.Id);
        if (DuzenId == i.Id) Yeni();                     // silinen kayıt formda kalmasın (sonraki kaydet 404)
        await IslemleriYukleAsync();
    });

    [RelayCommand]
    private Task GelenKaydetAsync() => CalistirAsync(async () =>
    {
        // Gelen, dönem başına tutulur: seçilen tarihi içeren dönemin başlangıcına hizalanır.
        // Dönem bulunamazsa ham tarih ASLA gönderilmez (motor onu yanlış döneme sayar ya da kaybeder).
        var tarih = DateOnly.FromDateTime(GelenTarih);
        await DonemleriGerekirseYenileAsync(tarih);
        var donem = DonemBul(tarih);
        if (donem is null)
        {
            var ilk = _donemler.Count > 0 ? _donemler.Min(d => d.Start) : (DateOnly?)null;
            Dogrula(false,
                ilk is null ? "Henüz dönem yok. Önce Ayarlar'da takip başlangıcını kontrol edin."
                : tarih < ilk ? $"Seçilen tarih takip başlangıcından ({ilk.Value.ToString("d MMMM yyyy", Kultur.Turkce)}) önce; gelen bu tarihe girilemez."
                : "Seçilen tarih için henüz dönem yok; ileri tarihli gelen girilemez.");
        }
        await _api.GelenKaydetAsync(new GelenYaz(donem!.Start, GelenKanal, GelenTutar));
        GelenKanal = ""; GelenTutar = 0;
    });
}

/// <summary>Seçilebilir çip: ad + seçili durumu (çip görünümü buna göre değişir).</summary>
public partial class SecimCipi : ObservableObject
{
    public string Ad { get; }
    public SecimCipi(string ad) => Ad = ad;
    [ObservableProperty] private bool _secili;
}
