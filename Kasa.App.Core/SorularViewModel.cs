using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Sorular sayfası (her iki rol). İzleyici bir işleme, haftaya, çeke ya da genel soru yazar; editör
/// cevaplar ve kapatır. Cevaplar herkese görünür. Sorular hiçbir kayda ve rakama dokunmaz.
/// </summary>
public partial class SorularViewModel : TemelViewModel
{
    public const int MetinEnCok = 2000;
    public const int CevapEnCok = 4000;

    public const string FiltreAcik = "Açık";
    public const string FiltreKapali = "Kapalı";
    public const string FiltreTumu = "Tümü";

    private readonly IKasaApi _api;
    private readonly SoruYonlendirme? _yonlendirme;
    private int _yuklemeNo;

    public SorularViewModel(IKasaApi api, SoruYonlendirme? yonlendirme = null, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        _yonlendirme = yonlendirme;
        Filtreler = [new SecimCipi(FiltreAcik) { Secili = true }, new SecimCipi(FiltreKapali), new SecimCipi(FiltreTumu)];
        Sorular.CollectionChanged += (_, _) => OnPropertyChanged(nameof(BosMu));
    }

    /// <summary>Cevap, kapatma, yeniden açma ve silme yalnız editörde.</summary>
    [ObservableProperty] private bool _editorMu;

    public IReadOnlyList<SecimCipi> Filtreler { get; }
    public string SeciliFiltre => Filtreler.First(f => f.Secili).Ad;

    public ObservableCollection<SoruSatiri> Sorular { get; } = new();
    public bool BosMu => Sorular.Count == 0;

    /// <summary>Açık soru sayısı (filtreden bağımsız; başlıkta "Açık sorular (N)").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Baslik))]
    private int _acikSayisi;

    public string Baslik => AcikSayisi > 0 ? $"Açık sorular ({AcikSayisi})" : "Sorular";

    /// <summary>Son başarılı işlemin kısa bildirimi ("Sorunuz iletildi." gibi).</summary>
    [ObservableProperty] private string? _bilgi;

    // ── Yeni soru formu ──
    [ObservableProperty] private bool _formAcik;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HedefMetni), nameof(HedefKaldirilabilir))]
    private SoruHedefi _hedef = SoruHedefi.Genel;
    [ObservableProperty] private string? _yeniMetin;

    /// <summary>Formdaki hedef: "İşlem · 24.09.2026 · MEZAT · 1.250,50 ₺ · Nakit" ya da "Genel soru".</summary>
    public string HedefMetni => Hedef.Tur == SoruHedefTuru.Genel ? Hedef.Ozet : $"{SoruHedefi.TurAdi(Hedef.Tur)} · {Hedef.Ozet}";
    public bool HedefKaldirilabilir => Hedef.Tur != SoruHedefTuru.Genel;

    /// <summary>Sayfa her açıldığında: satırdan "Soru sor" ile gelindiyse form o kayıtla açılır, sonra liste yüklenir.</summary>
    public Task YukleAsync()
    {
        if (_yonlendirme?.Al() is { } hedef) FormuAc(hedef);
        return CalistirAsync(DoldurAsync);
    }

    /// <summary>Formu verilen hedefle açar (metin sıfırlanır).</summary>
    public void FormuAc(SoruHedefi hedef)
    {
        Hedef = hedef;
        YeniMetin = "";
        Bilgi = null;
        FormAcik = true;
    }

    private async Task DoldurAsync()
    {
        var no = ++_yuklemeNo;
        SoruDurumu? durum = SeciliFiltre switch
        {
            FiltreAcik => SoruDurumu.Acik,
            FiltreKapali => SoruDurumu.Kapali,
            _ => null,
        };
        var listeGorevi = _api.SorularAsync(durum);
        var ozetGorevi = _api.SoruOzetAsync();
        var liste = await listeGorevi;
        var ozet = await ozetGorevi;
        if (no != _yuklemeNo) return;   // bu arada filtre değişti: eski yanıt yazılmaz
        AcikSayisi = ozet.AcikSayisi;
        Sorular.Clear();
        foreach (var s in liste) Sorular.Add(new SoruSatiri(s, Zaman.LocalTimeZone, EditorMu));
    }

    [RelayCommand]
    private Task FiltreSec(SecimCipi? cip)
    {
        if (cip is null || cip.Secili) return Task.CompletedTask;
        foreach (var f in Filtreler) f.Secili = ReferenceEquals(f, cip);
        OnPropertyChanged(nameof(SeciliFiltre));
        return CalistirAsync(DoldurAsync);
    }

    [RelayCommand]
    private Task Yenile() => CalistirAsync(DoldurAsync);

    [RelayCommand]
    private void GenelSoru() => FormuAc(SoruHedefi.Genel);

    [RelayCommand]
    private void HedefiKaldir() => Hedef = SoruHedefi.Genel;

    [RelayCommand]
    private void FormuKapat()
    {
        FormAcik = false;
        YeniMetin = "";
    }

    [RelayCommand]
    private Task Sor() => CalistirAsync(async () =>
    {
        Bilgi = null;
        var metin = YeniMetin?.Trim() ?? "";
        Dogrula(metin.Length > 0, "Sorunuzu yazın.");
        Dogrula(metin.Length <= MetinEnCok, $"Soru en fazla {MetinEnCok} karakter olabilir.");
        await _api.SoruSorAsync(new SoruYaz(Hedef.Tur, Hedef.Id, Hedef.Hafta, metin));
        FormAcik = false;
        YeniMetin = "";
        Hedef = SoruHedefi.Genel;
        Bilgi = "Sorunuz iletildi. Cevap gelince burada görünür.";
        await DoldurAsync();
    });

    // ── Editör ──

    /// <summary>Cevabı kaydeder ve soruyu kapatır.</summary>
    [RelayCommand]
    private Task Cevapla(SoruSatiri? s) => CevapYaz(s, kapat: true);

    /// <summary>Cevabı kaydeder, soru açık kalır (ek bilgi beklenirken).</summary>
    [RelayCommand]
    private Task CevaplaAcikKalsin(SoruSatiri? s) => CevapYaz(s, kapat: false);

    private Task CevapYaz(SoruSatiri? s, bool kapat) => s is null ? Task.CompletedTask : CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        var cevap = s.CevapTaslak?.Trim() ?? "";
        Dogrula(cevap.Length > 0, "Cevabı yazın.");
        Dogrula(cevap.Length <= CevapEnCok, $"Cevap en fazla {CevapEnCok} karakter olabilir.");
        await _api.SoruCevaplaAsync(s.Id, cevap, kapat);
        Bilgi = kapat ? "Cevaplandı ve kapatıldı." : "Cevaplandı.";
        await DoldurAsync();
    });

    [RelayCommand]
    private Task Kapat(SoruSatiri? s) => s is null ? Task.CompletedTask : CalistirAsync(async () =>
    {
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        await _api.SoruKapatAsync(s.Id);
        await DoldurAsync();
    });

    [RelayCommand]
    private Task Ac(SoruSatiri? s) => s is null ? Task.CompletedTask : CalistirAsync(async () =>
    {
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        await _api.SoruAcAsync(s.Id);
        await DoldurAsync();
    });

    /// <summary>İlk basış onay ister ("Emin misiniz?"), ikinci basış siler.</summary>
    [RelayCommand]
    private Task Sil(SoruSatiri? s)
    {
        if (s is null) return Task.CompletedTask;
        if (!s.SilOnayBekliyor)
        {
            foreach (var d in Sorular) d.SilOnayBekliyor = ReferenceEquals(d, s);
            return Task.CompletedTask;
        }
        return CalistirAsync(async () =>
        {
            Dogrula(EditorMu, HataMesaji.Yetkisiz);
            await _api.SoruSilAsync(s.Id);
            await DoldurAsync();
        });
    }
}

/// <summary>Sorular listesindeki bir satır.</summary>
public partial class SoruSatiri : ObservableObject
{
    public SoruSatiri(SoruDto d, TimeZoneInfo tz, bool editorMu)
    {
        Dto = d;
        EditorMu = editorMu;
        SorulmaZamani = YerelZaman.Metin(d.SorulmaUtc, tz);
        CevapMetni = d.Cevap is null ? null
            : string.Join(" · ", new[] { "Cevap", d.CevaplayanAd, YerelZaman.Metin(d.CevaplanmaUtc, tz) }.Where(x => !string.IsNullOrWhiteSpace(x)));
        _cevapTaslak = d.Cevap;
    }

    public SoruDto Dto { get; }
    public int Id => Dto.Id;
    public bool EditorMu { get; }

    /// <summary>"İşlem · 24.09.2026 · MEZAT · 1.250,50 ₺ · Nakit"; genel soruda "Genel soru".</summary>
    public string HedefBaslik => Dto.HedefTur == SoruHedefTuru.Genel
        ? "Genel soru"
        : $"{SoruHedefi.TurAdi(Dto.HedefTur)} · {(string.IsNullOrWhiteSpace(Dto.HedefOzet) ? "kayıt" : Dto.HedefOzet)}";

    public string Metin => Dto.Metin;
    public string SorulmaZamani { get; }
    /// <summary>"ORTAK · İzleyici · 24.09.2026 12:00".</summary>
    public string SoranMetni => $"{Dto.SoranAd} · {GecmisSatiri.RolMetni(Dto.SoranRol)} · {SorulmaZamani}";

    public bool Acik => Dto.Durum == SoruDurumu.Acik;
    public bool CevapVar => !string.IsNullOrWhiteSpace(Dto.Cevap);
    public string? Cevap => Dto.Cevap;
    /// <summary>"Cevap · EMAR · 24.09.2026 13:00" (cevap yoksa null).</summary>
    public string? CevapMetni { get; }

    /// <summary>Durum rozeti: "Açık", "Cevaplandı" (açık kaldı) ya da "Kapalı".</summary>
    public string DurumAdi => !Acik ? "Kapalı" : CevapVar ? "Cevaplandı" : "Açık";

    /// <summary>Editörün cevap alanı (varsa mevcut cevapla başlar).</summary>
    [ObservableProperty] private string? _cevapTaslak;

    public bool CevapFormuGorunur => EditorMu;
    public bool KapatGorunur => EditorMu && Acik;
    public bool AcGorunur => EditorMu && !Acik;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SilMetni))]
    private bool _silOnayBekliyor;

    public string SilMetni => SilOnayBekliyor ? "Emin misiniz? Sil" : "Sil";
}
