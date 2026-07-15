using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class IslemlerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public IslemlerViewModel(IKasaApi api) => _api = api;

    /// <summary>Belirli bir kanala ait olmayan ortak gider etiketi (motorla birebir eşleşmeli).</summary>
    public const string OrtakKanal = "Ortak";

    /// <summary>Kanal filtresi "tüm kanallar" çip/etiket metni (gerçek kanal olamaz).</summary>
    private const string TumKanal = "Tümü";

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
    private string? _filtreZamanKod = TumKanal;               // null = dönem picker aktif

    private async Task DoldurAsync()
    {
        var kanallar = await _api.KanallarAsync();
        var donemler = await _api.DonemlerAsync();
        var kartlar = await _api.KrediKartlariAsync();

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
        TipVurgu();
        KartVurgu();

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

        FiltreDonemler.Clear();
        foreach (var d in donemler.OrderByDescending(d => d.Start)) FiltreDonemler.Add(d);

        _donemler.Clear();
        _donemler.AddRange(donemler);

        FiltreVurgu();
        await IslemleriYukleAsync();
    }

    /// <summary>Seçili filtreyle işlem listesini + özet toplamı yeniler.</summary>
    private async Task IslemleriYukleAsync()
    {
        var liste = await _api.IslemlerAsync(FiltreBaslangic, FiltreBitis, FiltreKanal, null);
        Islemler.Clear();
        foreach (var i in liste) Islemler.Add(i);
        FiltreSayi = liste.Count;
        FiltreToplam = liste.Sum(i => i.TutarTl);
        FiltreOzet = $"{FiltreSayi} işlem · toplam {Bicim.Tl(FiltreToplam)} ₺";
    }

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
        SeciliDonem = null;                              // dönem picker'ı temizle (OnChanged erken döner)
        (FiltreBaslangic, FiltreBitis) = ZamanAralik(s.Ad);
        FiltreVurgu();
        return YenidenListele();
    }

    partial void OnFiltreKanalChanged(string? value) => FiltreVurgu();

    partial void OnSeciliDonemChanged(DonemDto? value)
    {
        if (value is null) return;                       // temizleme; zaman çipi zaten güncellendi
        _filtreZamanKod = null;
        FiltreBaslangic = value.Start;
        FiltreBitis = value.End;
        FiltreVurgu();
        _ = YenidenListele();
    }

    /// <summary>Hızlı zaman çipi kodunu [baslangic, bitis] aralığına çevirir.</summary>
    private static (DateOnly?, DateOnly?) ZamanAralik(string kod)
    {
        var bugun = DateOnly.FromDateTime(DateTime.Today);
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

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

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

    /// <summary>Kart seçici yalnız "Kredi kartı" tipi seçiliyken görünür.</summary>
    public bool KartSeciciGorunur => DuzenTip == GiderTipi.KrediKarti;

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
    [ObservableProperty] private DateTime _gelenTarih = DateTime.Today;
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
        if (value != GiderTipi.KrediKarti) DuzenKrediKartiId = null;
    }

    partial void OnDuzenKrediKartiIdChanged(int? value) => KartVurgu();

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
        DuzenId = 0; DuzenTarih = DateTime.Today; DuzenCari = "";
        DuzenTutar = 0; DuzenKanal = ""; DuzenTip = GiderTipi.Cari; DuzenNot = null;
        DuzenKrediKartiId = null;
    }

    [RelayCommand]
    public void Duzenle(IslemDto i)
    {
        DuzenId = i.Id; DuzenTarih = i.Tarih.ToDateTime(TimeOnly.MinValue);
        DuzenCari = i.Cari; DuzenTutar = i.TutarTl; DuzenKanal = i.Kanal;
        DuzenTip = i.Tip; DuzenNot = i.Not; DuzenKrediKartiId = i.KrediKartiId;
    }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var g = new IslemYaz(DateOnly.FromDateTime(DuzenTarih), DuzenCari, DuzenTutar, DuzenKanal, DuzenTip, DuzenNot, DuzenKrediKartiId);
        if (DuzenId == 0) await _api.IslemOlusturAsync(g);
        else await _api.IslemGuncelleAsync(DuzenId, g);
        Yeni();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task SilAsync(IslemDto i) => CalistirAsync(async () =>
    {
        await _api.IslemSilAsync(i.Id);
        await DoldurAsync();
    });

    [RelayCommand]
    private Task GelenKaydetAsync() => CalistirAsync(async () =>
    {
        // Gelen, dönem başına tutulur; seçilen tarihi içeren dönemin başlangıcına hizala,
        // yoksa raporlar bu geliri hiçbir döneme denk getiremez.
        var tarih = DateOnly.FromDateTime(GelenTarih);
        var donemStart = _donemler.FirstOrDefault(d => tarih >= d.Start && tarih <= d.End)?.Start ?? tarih;
        await _api.GelenKaydetAsync(new GelenYaz(donemStart, GelenKanal, GelenTutar));
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
