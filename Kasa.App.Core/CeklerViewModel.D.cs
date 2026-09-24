using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Paket D (özellik 30 + 43): tek dokunuşla tahsil / ödeme / ciro / karşılıksız, senet türü ve
/// portföy konumu, tür/konum filtresi, çoklu seçimde toplam + ortalama vade, risk dağılımı.
/// Tek dokunuş sunucuda tam formla aynı kaydı yazar ("vadede kasaya" kuralı aynen işler).
/// </summary>
public partial class CeklerViewModel
{
    public const string CiroCariMesaji = "Ciro edilen cariyi yazın.";

    /// <summary>Tek dokunuş ve ciro sonrası bilgi satırı (hata değil).</summary>
    [ObservableProperty] private string? _evrakBilgi;

    // ---------------------------------------------------------------- tür / konum filtresi

    /// <summary>Liste filtresi tür çipleri: Tümü · Çek · Senet.</summary>
    public ObservableCollection<TurCipi> FiltreTurleri { get; } = TurCipleriOlustur(tumu: true, secili: null);
    /// <summary>Liste filtresi konum çipleri (yalnız alınan evrak): Tümü · Elde · Bankada tahsilde · Teminatta · İcrada.</summary>
    public ObservableCollection<KonumCipi> FiltreKonumlari { get; } = KonumCipleriOlustur(tumu: true, secili: null);

    [ObservableProperty] private CekTuru? _filtreTur;        // null = iki tür
    [ObservableProperty] private CekKonumu? _filtreKonum;    // null = tüm konumlar

    private static ObservableCollection<TurCipi> TurCipleriOlustur(bool tumu, CekTuru? secili)
    {
        var l = new ObservableCollection<TurCipi>();
        if (tumu) l.Add(new TurCipi(Tumu, null) { Secili = secili is null });
        foreach (var t in CekEvrakMetin.TumTurler) l.Add(new TurCipi(CekEvrakMetin.TurAdi(t), t) { Secili = secili == t });
        return l;
    }

    private static ObservableCollection<KonumCipi> KonumCipleriOlustur(bool tumu, CekKonumu? secili)
    {
        var l = new ObservableCollection<KonumCipi>();
        if (tumu) l.Add(new KonumCipi(Tumu, null) { Secili = secili is null });
        foreach (var k in CekEvrakMetin.TumKonumlar) l.Add(new KonumCipi(CekEvrakMetin.KonumAdi(k), k) { Secili = secili == k });
        return l;
    }

    /// <summary>
    /// Tür/konum filtresi istemcide uygulanır (yön/durum filtresi sunucuda). Konum yalnız alınan
    /// evrakta anlamlıdır: konum seçiliyken verilen evrak listelenmez.
    /// </summary>
    private IReadOnlyList<CekDto> EvrakSuz(IReadOnlyList<CekDto> liste)
    {
        if (FiltreTur is null && FiltreKonum is null) return liste;
        return liste.Where(c => (FiltreTur is null || c.Tur == FiltreTur)
                             && (FiltreKonum is null || (c.Yon == CekYonu.Alinan && c.Konum == FiltreKonum)))
                    .ToList();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecFiltreTurAsync(TurCipi c)
    {
        FiltreTur = c.Tur;
        foreach (var x in FiltreTurleri) x.Secili = x.Tur == FiltreTur;
        return CalistirAsync(async () => await Task.WhenAll(ListeyiYukleAsync(), RiskYukleAsync()));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecFiltreKonumAsync(KonumCipi c)
    {
        FiltreKonum = c.Konum;
        foreach (var x in FiltreKonumlari) x.Secili = x.Konum == FiltreKonum;
        return CalistirAsync(ListeyiYukleAsync);
    }

    // ---------------------------------------------------------------- form: tür, konum, ciro edilen cari

    [ObservableProperty] private CekTuru _duzenTur = CekTuru.Cek;
    [ObservableProperty] private CekKonumu _duzenKonum = CekKonumu.Elde;
    [ObservableProperty] private string? _duzenCiroEdilenCari;

    /// <summary>Form tür çipleri: Çek · Senet.</summary>
    public ObservableCollection<TurCipi> TurCipleri { get; } = TurCipleriOlustur(tumu: false, secili: CekTuru.Cek);
    /// <summary>Form konum çipleri (yalnız alınan evrakta görünür).</summary>
    public ObservableCollection<KonumCipi> KonumCipleri { get; } = KonumCipleriOlustur(tumu: false, secili: CekKonumu.Elde);

    /// <summary>Konum yalnız alınan evrakta seçilir; verilen evrak her zaman "Elde" kaydedilir.</summary>
    public bool KonumGorunur => DuzenYon == CekYonu.Alinan;
    /// <summary>Ciro edilen cari alanı yalnız "Ciro edildi" durumunda.</summary>
    public bool CiroCariGorunur => DuzenDurum == CekDurumu.CiroEdildi;

    /// <summary>Kaydedilecek konum: verilen evrakta Elde.</summary>
    private CekKonumu FormKonum => DuzenYon == CekYonu.Alinan ? DuzenKonum : CekKonumu.Elde;
    /// <summary>Kaydedilecek ciro carisi: yalnız ciroda ve doluysa.</summary>
    private string? FormCiroCari => DuzenDurum == CekDurumu.CiroEdildi && !string.IsNullOrWhiteSpace(DuzenCiroEdilenCari)
        ? DuzenCiroEdilenCari.Trim() : null;

    [RelayCommand] private void SecTur(TurCipi c) { if (c.Tur is { } t) DuzenTur = t; }
    [RelayCommand] private void SecKonum(KonumCipi c) { if (c.Konum is { } k) DuzenKonum = k; }

    partial void OnDuzenTurChanged(CekTuru value)
    {
        foreach (var c in TurCipleri) c.Secili = c.Tur == value;
    }

    partial void OnDuzenKonumChanged(CekKonumu value)
    {
        foreach (var c in KonumCipleri) c.Secili = c.Konum == value;
    }

    partial void OnDuzenYonChanged(CekYonu oldValue, CekYonu newValue) => OnPropertyChanged(nameof(KonumGorunur));
    partial void OnDuzenDurumChanged(CekDurumu value) => OnPropertyChanged(nameof(CiroCariGorunur));

    /// <summary>Düzenlenen evrakın tür/konum/ciro carisini forma alır (<see cref="Duzenle"/> çağırır).</summary>
    private void EvrakDuzenle(CekDto c)
    {
        DuzenTur = c.Tur;
        DuzenKonum = c.Konum;
        DuzenCiroEdilenCari = c.CiroEdilenCari;
    }

    /// <summary>Yeni form: Çek, Elde, cirosuz (<see cref="Yeni"/> çağırır).</summary>
    private void EvrakYeni()
    {
        DuzenTur = CekTuru.Cek;
        DuzenKonum = CekKonumu.Elde;
        DuzenCiroEdilenCari = null;
    }

    // ---------------------------------------------------------------- tek dokunuş (özellik 30)

    [RelayCommand] private Task TahsilEtAsync(CekGorunum g) => TekDokunusAsync(g, CekDurumu.TahsilEdildi, null);
    [RelayCommand] private Task OdeAsync(CekGorunum g) => TekDokunusAsync(g, CekDurumu.Odendi, null);
    [RelayCommand] private Task KarsiliksizAsync(CekGorunum g) => TekDokunusAsync(g, CekDurumu.Karsiliksiz, null);

    /// <summary>"Ciro et": satır içinde cari sorulur (diğer açık ciro formu kapanır).</summary>
    [RelayCommand]
    private Task CiroAcAsync(CekGorunum g) => CalistirAsync(async () =>
    {
        var acilacak = !g.CiroAcik;
        foreach (var c in Cekler) c.CiroAcik = false;
        CiroCari = "";
        g.CiroAcik = acilacak;
        if (acilacak && _cariler is null)
        {
            var liste = await _api.CarilerAsync();
            _cariler = liste.Where(c => c.Aktif).Select(c => c.Ad).ToList();
            CiroOnerileriniKur();
        }
    });

    [RelayCommand]
    private void CiroVazgec(CekGorunum g)
    {
        g.CiroAcik = false;
        CiroCari = "";
    }

    [RelayCommand]
    private Task CiroOnaylaAsync(CekGorunum g) => TekDokunusAsync(g, CekDurumu.CiroEdildi, CiroCari?.Trim() ?? "");

    /// <summary>Ciro edilen cari (satır içi form).</summary>
    [ObservableProperty] private string _ciroCari = "";
    /// <summary>Yazılana uyan kayıtlı cariler (en çok 6).</summary>
    public ObservableCollection<SecimCipi> CiroOnerileri { get; } = new();
    private List<string>? _cariler;

    [RelayCommand] private void SecCiroCari(SecimCipi c) => CiroCari = c.Ad;

    partial void OnCiroCariChanged(string value) => CiroOnerileriniKur();

    private void CiroOnerileriniKur()
    {
        CiroOnerileri.Clear();
        if (_cariler is null) return;
        var aranan = CiroCari?.Trim() ?? "";
        foreach (var ad in _cariler.Where(a => aranan.Length == 0 || CekEvrakMetin.Icerir(a, aranan)).Take(6))
            CiroOnerileri.Add(new SecimCipi(ad) { Secili = ad == aranan });
    }

    /// <summary>
    /// Portföydeki evrakın durumunu bugünle kaydeder (tarihi sunucu Türkiye saatine göre koyar).
    /// Formda aynı evrak açıksa form temizlenir (eski durumla üzerine yazılmasın).
    /// </summary>
    private Task TekDokunusAsync(CekGorunum g, CekDurumu durum, string? cari) => CalistirAsync(async () =>
    {
        EvrakBilgi = null;
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        if (durum == CekDurumu.CiroEdildi) Dogrula(!string.IsNullOrWhiteSpace(cari), CiroCariMesaji);
        try { await _api.CekDurumAsync(g.Id, new CekDurumYaz(durum, null, cari)); }
        catch (KasaApiException ex) when (ex.DurumKodu is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
        {
            try { await YenileAsync(); } catch (Exception) { /* asıl hata gösterilir */ }
            throw;
        }
        if (DuzenId == g.Id) Yeni();
        CiroCari = "";
        EvrakBilgi = TekDokunusMesaji(g, durum, cari);
        await YenileAsync();
    });

    public static string TekDokunusMesaji(CekGorunum g, CekDurumu durum, string? cari) => durum switch
    {
        CekDurumu.TahsilEdildi => $"{g.Kisi} {g.TurAdi.ToLower(Kultur.Turkce)}i bugün tahsil edildi; kasaya ve {g.Kanal} kanalına girdi.",
        CekDurumu.Odendi => $"{g.Kisi} {g.TurAdi.ToLower(Kultur.Turkce)}i bugün ödendi; kasadan çıktı.",
        CekDurumu.CiroEdildi => $"{g.Kisi} {g.TurAdi.ToLower(Kultur.Turkce)}i {cari} carisine ciro edildi; kasayı etkilemez.",
        CekDurumu.Karsiliksiz => $"{g.Kisi} {g.TurAdi.ToLower(Kultur.Turkce)}i karşılıksız olarak işaretlendi; kasayı etkilemez.",
        _ => "Kaydedildi.",
    };

    // ---------------------------------------------------------------- çoklu seçim (özellik 43)

    private readonly HashSet<int> _seciliIdler = new();

    [ObservableProperty] private int _secimAdet;
    [ObservableProperty] private string _secimOzeti = "";
    [ObservableProperty] private string _secimVadeMetni = "";
    public bool SecimVar => SecimAdet > 0;
    partial void OnSecimAdetChanged(int value) => OnPropertyChanged(nameof(SecimVar));

    /// <summary>Liste yenilenince seçimi (id ile) yeni satırlara taşır ve seçim değişimini izler.</summary>
    private void SecimiBagla()
    {
        var gorunen = Cekler.Select(c => c.Id).ToHashSet();
        _seciliIdler.IntersectWith(gorunen);
        foreach (var c in Cekler)
        {
            c.Secili = _seciliIdler.Contains(c.Id);
            c.PropertyChanged += SatirDegisti;
        }
        SecimiHesapla();
    }

    private void SatirDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CekGorunum.Secili) || sender is not CekGorunum g || !Cekler.Contains(g)) return;
        if (g.Secili) _seciliIdler.Add(g.Id); else _seciliIdler.Remove(g.Id);
        SecimiHesapla();
    }

    private void SecimiHesapla()
    {
        var secilen = Cekler.Where(c => c.Secili).Select(c => c.Dto).ToList();
        SecimAdet = secilen.Count;
        if (secilen.Count == 0) { SecimOzeti = ""; SecimVadeMetni = ""; return; }
        var alinan = secilen.Where(c => c.Yon == CekYonu.Alinan).Sum(c => c.Tutar);
        var verilen = secilen.Where(c => c.Yon == CekYonu.Verilen).Sum(c => c.Tutar);
        var tutar = alinan > 0 && verilen > 0
            ? $"alınan {Bicim.Tl(alinan)} ₺ · verilen {Bicim.Tl(verilen)} ₺"
            : $"toplam {Bicim.Tl(alinan + verilen)} ₺";
        SecimOzeti = $"{secilen.Count} evrak seçili · {tutar}";
        var gun = CekEvrakMetin.OrtalamaVadeGunu(secilen, BugunTarih);
        SecimVadeMetni = gun is { } g ? $"Tutar ağırlıklı ortalama vade: {CekEvrakMetin.VadeMetni(g, BugunTarih)}" : "";
    }

    [RelayCommand]
    private void TumunuSec()
    {
        foreach (var c in Cekler) c.Secili = true;
    }

    [RelayCommand]
    private void SecimiTemizle()
    {
        _seciliIdler.Clear();
        foreach (var c in Cekler) c.Secili = false;
        SecimiHesapla();
    }

    // ---------------------------------------------------------------- risk dağılımı (özellik 43)

    /// <summary>Portföydeki alınan evrak: keşideciye göre (tutara göre azalan).</summary>
    public ObservableCollection<CekRiskSatiri> RiskKesideciler { get; } = new();
    /// <summary>Portföydeki alınan evrak: bankaya göre.</summary>
    public ObservableCollection<CekRiskSatiri> RiskBankalar { get; } = new();
    [ObservableProperty] private string _riskOzeti = "";
    [ObservableProperty] private bool _riskVar;

    /// <summary>Risk dağılımını seçili tür filtresiyle çeker; sunucu ucu yoksa (404) kart gizlenir.</summary>
    private async Task RiskYukleAsync()
    {
        CekRiskDto r;
        try { r = await _api.CekRiskAsync(FiltreTur); }
        catch (KasaApiException ex) when (ex.DurumKodu == HttpStatusCode.NotFound)
        {
            r = new CekRiskDto(0m, 0, Array.Empty<CekRiskKalemiDto>(), Array.Empty<CekRiskKalemiDto>());
        }
        RiskKesideciler.Clear();
        foreach (var k in r.Kesideciler) RiskKesideciler.Add(new CekRiskSatiri(k));
        RiskBankalar.Clear();
        foreach (var k in r.Bankalar) RiskBankalar.Add(new CekRiskSatiri(k));
        RiskVar = r.Adet > 0;
        var kapsam = FiltreTur is { } t ? CekEvrakMetin.TurAdi(t).ToLower(Kultur.Turkce) : "evrak";
        RiskOzeti = r.Adet == 0 ? "" : $"Portföydeki alınan {kapsam}: {r.Adet} adet · {Bicim.Tl(r.Toplam)} ₺";
    }
}
