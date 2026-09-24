using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Çekler sayfası: üstte özet (portföy / ödenecek / vadesi yaklaşan / vadesi geçen), yön ve durum
/// filtreli liste, editörde form. Çek kasayı yalnız tahsil edildiği / ödendiği gün etkiler.
/// </summary>
public partial class CeklerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;

    public CeklerViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        _duzenDuzenlemeTarihi = Bugun;
        _duzenVadeTarihi = Bugun;
        _duzenIslemTarihi = Bugun;
        YonCipleriniKur();
        DurumCipleriniKur();
        FiltreCipleriniKur();
    }

    /// <summary>Ortak gider kanalı (yalnız verilen çekte seçilebilir).</summary>
    public const string OrtakKanal = "Ortak";
    private const string Tumu = "Tümü";

    public const string TutarMesaji = "Tutar sıfırdan büyük olmalı.";
    public const string KanalMesaji = "Bir kanal seçin.";
    public static string KisiMesaji(CekYonu yon) => yon == CekYonu.Alinan
        ? "Çeki veren kişi/firmayı yazın." : "Çekin verildiği kişi/firmayı yazın.";

    // ---------------------------------------------------------------- liste + filtre

    public ObservableCollection<CekGorunum> Cekler { get; } = new();

    /// <summary>Liste filtresi yön çipleri: Tümü · Alınan · Verilen.</summary>
    public ObservableCollection<YonCipi> FiltreYonleri { get; } = new();

    /// <summary>Liste filtresi durum çipleri: Tümü + seçili yöne uyan durumlar.</summary>
    public ObservableCollection<DurumCipi> FiltreDurumlari { get; } = new();

    [ObservableProperty] private CekYonu? _filtreYon;        // null = iki yön
    [ObservableProperty] private CekDurumu? _filtreDurum;    // null = tüm durumlar
    [ObservableProperty] private string _listeOzeti = "";

    /// <summary>Liste istek sürümü: hızlı filtre değişiminde geç gelen eski yanıt yenisini ezmesin.</summary>
    private int _listeSurumu;

    // ---------------------------------------------------------------- özet

    [ObservableProperty] private decimal _portfoydekiAlinanToplam;
    [ObservableProperty] private int _portfoydekiAlinanAdet;
    [ObservableProperty] private decimal _odenecekVerilenToplam;
    [ObservableProperty] private int _odenecekVerilenAdet;
    [ObservableProperty] private int _yaklasanAdet;
    [ObservableProperty] private string _yaklasanBaslik = "Vadesi 30 gün içinde";
    [ObservableProperty] private string _yaklasanAyrinti = "";
    [ObservableProperty] private int _vadesiGecenAdet;
    [ObservableProperty] private string _vadesiGecenAyrinti = "";

    /// <summary>Vadesi geçtiği hâlde portföyde bekleyen çekler (dikkat listesi).</summary>
    public ObservableCollection<CekGorunum> VadesiGecenler { get; } = new();
    public bool VadesiGecenVar => VadesiGecenler.Count > 0;

    // ---------------------------------------------------------------- yükleme

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    private async Task DoldurAsync()
    {
        var kanalGorevi = _api.KanallarAsync();
        var ozetGorevi = _api.CekOzetAsync();
        var listeGorevi = ListeyiYukleAsync();
        await Task.WhenAll(kanalGorevi, ozetGorevi, listeGorevi);
        _aktifKanallar.Clear();
        _aktifKanallar.AddRange(kanalGorevi.Result.Where(k => k.Aktif).OrderBy(k => k.Sira).Select(k => k.Ad));
        KanalCipleriniKur();
        OzetiKur(ozetGorevi.Result);
    }

    private async Task ListeyiYukleAsync()
    {
        var surum = ++_listeSurumu;
        IReadOnlyList<CekDto> liste;
        try { liste = await _api.CeklerAsync(FiltreYon, FiltreDurum); }
        catch when (surum != _listeSurumu) { return; }   // bu arada yeni filtre istendi
        if (surum != _listeSurumu) return;

        var bugun = BugunTarih;
        Cekler.Clear();
        foreach (var c in liste) Cekler.Add(new CekGorunum(c, bugun));
        var alinan = liste.Where(c => c.Yon == CekYonu.Alinan).Sum(c => c.Tutar);
        var verilen = liste.Where(c => c.Yon == CekYonu.Verilen).Sum(c => c.Tutar);
        ListeOzeti = liste.Count == 0
            ? "Filtreye uyan çek yok"
            : $"{liste.Count} çek · alınan {Bicim.Tl(alinan)} ₺ · verilen {Bicim.Tl(verilen)} ₺";
    }

    private void OzetiKur(CekOzetDto o)
    {
        PortfoydekiAlinanToplam = o.PortfoydekiAlinanToplam;
        PortfoydekiAlinanAdet = o.PortfoydekiAlinanAdet;
        OdenecekVerilenToplam = o.OdenecekVerilenToplam;
        OdenecekVerilenAdet = o.OdenecekVerilenAdet;
        YaklasanAdet = o.Yaklasanlar.Count;
        YaklasanBaslik = $"Vadesi {o.YaklasanGun} gün içinde";
        YaklasanAyrinti = YonKirilimi(o.Yaklasanlar);
        VadesiGecenAdet = o.VadesiGecenler.Count;
        VadesiGecenAyrinti = YonKirilimi(o.VadesiGecenler);
        var bugun = BugunTarih;
        VadesiGecenler.Clear();
        foreach (var c in o.VadesiGecenler) VadesiGecenler.Add(new CekGorunum(c, bugun));
        OnPropertyChanged(nameof(VadesiGecenVar));
    }

    private static string YonKirilimi(IReadOnlyList<CekDto> l)
        => $"Alınan {Bicim.Tl(l.Where(c => c.Yon == CekYonu.Alinan).Sum(c => c.Tutar))} ₺ · " +
           $"Verilen {Bicim.Tl(l.Where(c => c.Yon == CekYonu.Verilen).Sum(c => c.Tutar))} ₺";

    // ---------------------------------------------------------------- filtre komutları

    private void FiltreCipleriniKur()
    {
        FiltreYonleri.Clear();
        FiltreYonleri.Add(new YonCipi(Tumu, null));
        FiltreYonleri.Add(new YonCipi(CekMetin.YonAdi(CekYonu.Alinan), CekYonu.Alinan));
        FiltreYonleri.Add(new YonCipi(CekMetin.YonAdi(CekYonu.Verilen), CekYonu.Verilen));
        FiltreDurumlariniKur();
    }

    /// <summary>Durum çiplerini seçili yöne göre kurar; artık geçersiz durum filtresi "Tümü"ne döner.</summary>
    private void FiltreDurumlariniKur()
    {
        var durumlar = FiltreYon is { } y ? CekMetin.GecerliDurumlar(y) : CekMetin.TumDurumlar;
        if (FiltreDurum is { } d && !durumlar.Contains(d)) FiltreDurum = null;
        FiltreDurumlari.Clear();
        FiltreDurumlari.Add(new DurumCipi(Tumu, null));
        foreach (var durum in durumlar) FiltreDurumlari.Add(new DurumCipi(CekMetin.DurumAdi(FiltreYon, durum), durum));
        FiltreVurgu();
    }

    private void FiltreVurgu()
    {
        foreach (var c in FiltreYonleri) c.Secili = c.Yon == FiltreYon;
        foreach (var c in FiltreDurumlari) c.Secili = c.Durum == FiltreDurum;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecFiltreYonAsync(YonCipi c)
    {
        FiltreYon = c.Yon;
        FiltreDurumlariniKur();
        return CalistirAsync(ListeyiYukleAsync);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecFiltreDurumAsync(DurumCipi c)
    {
        FiltreDurum = c.Durum;
        FiltreVurgu();
        return CalistirAsync(ListeyiYukleAsync);
    }

    // ---------------------------------------------------------------- editör formu

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private int _duzenId;                    // 0 = yeni
    [ObservableProperty] private CekYonu _duzenYon = CekYonu.Alinan;
    [ObservableProperty] private string? _duzenCekNo;
    [ObservableProperty] private string? _duzenBanka;
    [ObservableProperty] private string _duzenKisi = "";
    [ObservableProperty] private decimal _duzenTutar;
    [ObservableProperty] private DateTime _duzenDuzenlemeTarihi;
    [ObservableProperty] private DateTime _duzenVadeTarihi;
    [ObservableProperty] private string _duzenKanal = "";
    [ObservableProperty] private CekDurumu _duzenDurum = CekDurumu.Portfoyde;
    [ObservableProperty] private DateTime _duzenIslemTarihi;
    [ObservableProperty] private string? _duzenNot;

    /// <summary>Form yön çipleri: Alınan · Verilen.</summary>
    public ObservableCollection<YonCipi> YonCipleri { get; } = new();
    /// <summary>Form kanal çipleri: aktif kanallar (+ "Ortak" yalnız verilen çekte).</summary>
    public ObservableCollection<SecimCipi> KanalCipleri { get; } = new();
    /// <summary>Form durum çipleri: seçili yöne uyan durumlar.</summary>
    public ObservableCollection<DurumCipi> DurumCipleri { get; } = new();

    private readonly List<string> _aktifKanallar = new();

    /// <summary>Tahsil / ödeme / ciro tarihi alanı yalnız gereken durumlarda görünür.</summary>
    public bool IslemTarihiGorunur => CekMetin.IslemTarihiGerekli(DuzenDurum);
    public string IslemTarihiEtiketi => CekMetin.IslemTarihiEtiketi(DuzenDurum);
    public string KisiEtiketi => DuzenYon == CekYonu.Alinan ? "Çeki veren (kişi/firma)" : "Verildiği kişi/firma";
    public string FormBasligi => DuzenId == 0 ? "Yeni çek" : "Çeki düzenle";

    /// <summary>Formdaki çekin kasaya etkisini anlatan satır (her durumda dolu).</summary>
    public string KasaEtkisiMetni
    {
        get
        {
            var tarih = DateOnly.FromDateTime(DuzenIslemTarihi);
            var gun = tarih.ToString("d MMMM yyyy", Kultur.Turkce);
            var ileri = tarih > BugunTarih ? " (ileri tarih: kasaya ancak o gün yansır)" : "";
            return (DuzenYon, DuzenDurum) switch
            {
                (CekYonu.Alinan, CekDurumu.TahsilEdildi) => $"{gun} günü kasaya ve {KanalMetni} kanalına girer{ileri}.",
                (CekYonu.Verilen, CekDurumu.Odendi) => $"{gun} günü {(DuzenKanal == OrtakKanal ? "ortak gider olarak " : "")}kasadan çıkar{ileri}.",
                (_, CekDurumu.Portfoyde) => DuzenYon == CekYonu.Alinan
                    ? "Portföyde: tahsil edilene kadar kasaya girmez."
                    : "Ödenecek: ödenene kadar kasadan çıkmaz.",
                _ => "Bu durumda çek kasayı etkilemez.",
            };
        }
    }

    private string KanalMetni => string.IsNullOrEmpty(DuzenKanal) ? "seçilen" : DuzenKanal;

    private void YonCipleriniKur()
    {
        YonCipleri.Clear();
        YonCipleri.Add(new YonCipi(CekMetin.YonAdi(CekYonu.Alinan), CekYonu.Alinan));
        YonCipleri.Add(new YonCipi(CekMetin.YonAdi(CekYonu.Verilen), CekYonu.Verilen));
        foreach (var c in YonCipleri) c.Secili = c.Yon == DuzenYon;
    }

    private void DurumCipleriniKur()
    {
        DurumCipleri.Clear();
        foreach (var d in CekMetin.GecerliDurumlar(DuzenYon))
            DurumCipleri.Add(new DurumCipi(CekMetin.DurumAdi(DuzenYon, d), d) { Secili = d == DuzenDurum });
    }

    private void KanalCipleriniKur()
    {
        KanalCipleri.Clear();
        foreach (var ad in _aktifKanallar) KanalCipleri.Add(new SecimCipi(ad) { Secili = ad == DuzenKanal });
        if (DuzenYon == CekYonu.Verilen) KanalCipleri.Add(new SecimCipi(OrtakKanal) { Secili = DuzenKanal == OrtakKanal });
    }

    [RelayCommand] private void SecYon(YonCipi c) { if (c.Yon is { } y) DuzenYon = y; }
    [RelayCommand] private void SecKanal(SecimCipi c) => DuzenKanal = c.Ad;
    [RelayCommand] private void SecDurum(DurumCipi c) { if (c.Durum is { } d) DuzenDurum = d; }

    partial void OnDuzenYonChanged(CekYonu value)
    {
        foreach (var c in YonCipleri) c.Secili = c.Yon == value;
        // Alınan çekte "Ortak" seçilemez; yöne uymayan durum portföye döner.
        if (value == CekYonu.Alinan && DuzenKanal == OrtakKanal) DuzenKanal = "";
        if (!CekMetin.DurumGecerliMi(value, DuzenDurum)) DuzenDurum = CekDurumu.Portfoyde;
        DurumCipleriniKur();
        KanalCipleriniKur();
        OnPropertyChanged(nameof(KisiEtiketi));
        OnPropertyChanged(nameof(KasaEtkisiMetni));
    }

    partial void OnDuzenDurumChanged(CekDurumu oldValue, CekDurumu newValue)
    {
        foreach (var c in DurumCipleri) c.Secili = c.Durum == newValue;
        // Tahsil/ödeme/ciroya geçen çekin işlem tarihi varsayılan olarak bugündür.
        if (CekMetin.IslemTarihiGerekli(newValue) && !CekMetin.IslemTarihiGerekli(oldValue))
            DuzenIslemTarihi = Bugun;
        OnPropertyChanged(nameof(IslemTarihiGorunur));
        OnPropertyChanged(nameof(IslemTarihiEtiketi));
        OnPropertyChanged(nameof(KasaEtkisiMetni));
    }

    partial void OnDuzenKanalChanged(string value)
    {
        foreach (var c in KanalCipleri) c.Secili = c.Ad == value;
        OnPropertyChanged(nameof(KasaEtkisiMetni));
    }

    partial void OnDuzenIslemTarihiChanged(DateTime value) => OnPropertyChanged(nameof(KasaEtkisiMetni));
    partial void OnDuzenIdChanged(int value) => OnPropertyChanged(nameof(FormBasligi));

    [RelayCommand]
    private void Yeni()
    {
        DuzenId = 0;
        DuzenYon = CekYonu.Alinan;
        DuzenDurum = CekDurumu.Portfoyde;
        DuzenCekNo = null; DuzenBanka = null; DuzenKisi = ""; DuzenTutar = 0; DuzenNot = null;
        DuzenKanal = "";
        DuzenDuzenlemeTarihi = Bugun; DuzenVadeTarihi = Bugun; DuzenIslemTarihi = Bugun;
    }

    [RelayCommand]
    public void Duzenle(CekGorunum g)
    {
        var c = g.Dto;
        DuzenId = c.Id;
        DuzenYon = c.Yon;
        DuzenDurum = c.Durum;                   // tarih aşağıda kaydınkiyle ezilir
        DuzenCekNo = c.CekNo; DuzenBanka = c.Banka; DuzenKisi = c.Kisi; DuzenTutar = c.Tutar; DuzenNot = c.Not;
        DuzenKanal = c.Kanal;
        DuzenDuzenlemeTarihi = c.DuzenlemeTarihi.ToDateTime(TimeOnly.MinValue);
        DuzenVadeTarihi = c.VadeTarihi.ToDateTime(TimeOnly.MinValue);
        DuzenIslemTarihi = (c.IslemTarihi ?? BugunTarih).ToDateTime(TimeOnly.MinValue);
    }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        Dogrula(DuzenTutar > 0, TutarMesaji);
        var kisi = DuzenKisi?.Trim() ?? "";
        Dogrula(kisi.Length > 0, KisiMesaji(DuzenYon));
        Dogrula(!string.IsNullOrWhiteSpace(DuzenKanal), KanalMesaji);

        var g = new CekYaz(
            DuzenYon,
            string.IsNullOrWhiteSpace(DuzenCekNo) ? null : DuzenCekNo.Trim(),
            string.IsNullOrWhiteSpace(DuzenBanka) ? null : DuzenBanka.Trim(),
            kisi, DuzenTutar,
            DateOnly.FromDateTime(DuzenDuzenlemeTarihi), DateOnly.FromDateTime(DuzenVadeTarihi),
            DuzenKanal, DuzenDurum,
            IslemTarihiGorunur ? DateOnly.FromDateTime(DuzenIslemTarihi) : null,
            string.IsNullOrWhiteSpace(DuzenNot) ? null : DuzenNot.Trim());
        if (DuzenId == 0) await _api.CekOlusturAsync(g);
        else await _api.CekGuncelleAsync(DuzenId, g);
        Yeni();
        await YenileAsync();
    });

    [RelayCommand]
    private Task SilAsync(CekGorunum g) => CalistirAsync(async () =>
    {
        await _api.CekSilAsync(g.Id);
        if (DuzenId == g.Id) Yeni();             // silinen kayıt formda kalmasın (sonraki kaydet 404)
        await YenileAsync();
    });

    /// <summary>Kayıttan sonra liste ve özet birlikte tazelenir.</summary>
    private async Task YenileAsync()
    {
        var ozetGorevi = _api.CekOzetAsync();
        await ListeyiYukleAsync();
        OzetiKur(await ozetGorevi);
    }
}
