using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Ayarlar › Hesabım (editör): şifre değiştirme ve iki adımlı giriş (TOTP + kurtarma kodları).
/// Şifre değişince bu cihazın oturumu sürer (yeni token), diğer cihazlar çıkar. .env şifresi ilk
/// değişikliğe kadar geçerlidir; iki adımlı giriş açılana kadar kapalıdır. İki adımı açmak ve
/// kurtarma kodlarını yenilemek şifre ister (yalnız oturumu ele geçiren açamasın). Kurulumda
/// anahtar telefonla okutulacak QR kodu olarak da gösterilir; anahtar, adres ve kodlar kopyalanabilir.
/// </summary>
public partial class HesapGuvenligiViewModel : TemelViewModel
{
    public const int SifreEnAz = 8;
    public const int SifreEnCok = 200;
    public const string PanoYokMesaji = "Bu cihazda panoya kopyalanamıyor; ekrandaki metni elle yazın.";

    private readonly IKasaApi _api;
    private readonly IPano? _pano;

    public HesapGuvenligiViewModel(IKasaApi api, TimeProvider? zaman = null, IPano? pano = null) : base(zaman)
    {
        _api = api;
        _pano = pano;
        KurtarmaKodlari.CollectionChanged += (_, _) => OnPropertyChanged(nameof(KodlarGorunur));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HesapMetni), nameof(SifreBilgisi), nameof(IkiAdimDurumu), nameof(IkiAdimAcik),
        nameof(IkiAdimKapali), nameof(KurulumBaslatGorunur))]
    private HesapDto? _hesap;

    /// <summary>"EMAR · emar".</summary>
    public string HesapMetni => Hesap is { } h ? $"{h.AdSoyad} · {h.KullaniciAdi}" : "";

    public string SifreBilgisi => Hesap is { EnvSifresi: true }
        ? "Şu an sunucu ayarındaki (.env) şifre geçerli. Buradan değiştirirseniz yeni şifre geçerli olur."
        : "Şifre değişince diğer cihazlardaki oturumlarınız kapanır; bu cihazda oturum sürer.";

    public bool IkiAdimAcik => Hesap?.IkiAdimAcik == true;
    public bool IkiAdimKapali => Hesap is { IkiAdimAcik: false };

    public string IkiAdimDurumu => Hesap is not { } h ? ""
        : h.IkiAdimAcik ? $"Açık · {h.KurtarmaKoduKalan} kurtarma kodu kaldı" : "Kapalı";

    /// <summary>Son başarılı işlemin kısa bildirimi.</summary>
    [ObservableProperty] private string? _bilgi;

    // ── Şifre ──
    [ObservableProperty] private string _mevcutSifre = "";
    [ObservableProperty] private string _yeniSifre = "";
    [ObservableProperty] private string _yeniSifreTekrar = "";

    // ── İki adımlı giriş kurulumu ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KurulumSurerken), nameof(KurulumBaslatGorunur))]
    private IkiAdimKurulumDto? _kurulum;

    public bool KurulumSurerken => Kurulum is not null;
    public bool KurulumBaslatGorunur => IkiAdimKapali && Kurulum is null;

    /// <summary>Sır, 4'lü gruplar hâlinde (elle yazması kolay olsun).</summary>
    public string KurulumSirMetni => Kurulum is { } k ? Grupla(k.Sir) : "";

    /// <summary>otpauth:// adresinin QR kodu (telefondaki uygulamayla okutulur); kurulum yoksa null.</summary>
    public QrMatris? KurulumQr { get; private set; }
    public bool KurulumQrVar => KurulumQr is not null;

    partial void OnKurulumChanged(IkiAdimKurulumDto? value)
    {
        KurulumQr = QrUret(value?.Adres);
        OnPropertyChanged(nameof(KurulumSirMetni));
        OnPropertyChanged(nameof(KurulumQr));
        OnPropertyChanged(nameof(KurulumQrVar));
    }

    private static QrMatris? QrUret(string? adres)
    {
        if (string.IsNullOrEmpty(adres)) return null;
        try { return QrKodu.Olustur(adres, QrSeviye.M); }
        catch (ArgumentException) { return null; }   // sığmayan (aşırı uzun) adres: anahtar elle yazılır
    }

    [ObservableProperty] private string? _kurulumKodu;
    /// <summary>Hesap şifresi: iki adımı açmak şifre ister.</summary>
    [ObservableProperty] private string _kurulumSifre = "";

    /// <summary>Yeni kurtarma kodları: yalnız bir kez gösterilir.</summary>
    public ObservableCollection<string> KurtarmaKodlari { get; } = new();
    public bool KodlarGorunur => KurtarmaKodlari.Count > 0;
    public string KurtarmaKodlariMetni => string.Join(Environment.NewLine, KurtarmaKodlari);

    // ── Kapatma / kod yenileme ──
    [ObservableProperty] private string _kapatSifre = "";
    [ObservableProperty] private string? _kapatKod;
    [ObservableProperty] private string _yenileSifre = "";
    [ObservableProperty] private string? _yenileKod;

    public Task YukleAsync() => CalistirAsync(async () => Hesap = await _api.HesabimAsync());

    [RelayCommand]
    private Task SifreDegistir() => CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(MevcutSifre.Length > 0, "Mevcut şifreyi yazın.");
        Dogrula(YeniSifre.Length >= SifreEnAz, $"Yeni şifre en az {SifreEnAz} karakter olmalı.");
        Dogrula(YeniSifre.Length <= SifreEnCok, $"Yeni şifre en fazla {SifreEnCok} karakter olabilir.");
        Dogrula(YeniSifre == YeniSifreTekrar, "Yeni şifreler aynı değil.");
        Dogrula(YeniSifre != MevcutSifre, "Yeni şifre eskisiyle aynı olamaz.");
        await _api.SifremiDegistirAsync(MevcutSifre, YeniSifre);
        MevcutSifre = YeniSifre = YeniSifreTekrar = "";
        Bilgi = "Şifre değişti. Diğer cihazlardaki oturumlarınız kapandı.";
        Hesap = await _api.HesabimAsync();
    });

    [RelayCommand]
    private Task IkiAdimBaslat() => CalistirAsync(async () =>
    {
        Bilgi = null;
        KurtarmaKodlari.Clear();
        KurulumKodu = null;
        Kurulum = await _api.IkiAdimBaslatAsync();
    });

    [RelayCommand]
    private void KurulumIptal()
    {
        Kurulum = null;
        KurulumKodu = null;
        KurulumSifre = "";
    }

    [RelayCommand]
    private Task IkiAdimOnayla() => CalistirAsync(async () =>
    {
        Bilgi = null;
        var kod = KodTemizle(KurulumKodu);
        Dogrula(kod.Length == 6 && kod.All(char.IsAsciiDigit), "Uygulamadaki 6 haneli kodu yazın.");
        Dogrula(KurulumSifre.Length > 0, "Şifrenizi yazın.");
        var kodlar = await _api.IkiAdimOnaylaAsync(KurulumSifre, kod);
        Kurulum = null;
        KurulumKodu = null;
        KurulumSifre = "";
        KodlariGoster(kodlar);
        Bilgi = "İki adımlı giriş açıldı. Kurtarma kodlarını güvenli bir yere yazın; bir daha gösterilmez.";
        Hesap = await _api.HesabimAsync();
    });

    [RelayCommand]
    private Task IkiAdimKapat() => CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(KapatSifre.Length > 0, "Şifrenizi yazın.");
        Dogrula(!string.IsNullOrWhiteSpace(KapatKod), "Uygulamadaki kodu ya da bir kurtarma kodunu yazın.");
        await _api.IkiAdimKapatAsync(KapatSifre, KapatKod!.Trim());
        KapatSifre = "";
        KapatKod = null;
        KurtarmaKodlari.Clear();
        Bilgi = "İki adımlı giriş kapatıldı.";
        Hesap = await _api.HesabimAsync();
    });

    [RelayCommand]
    private Task KurtarmaKodlariYenile() => CalistirAsync(async () =>
    {
        Bilgi = null;
        var kod = KodTemizle(YenileKod);
        Dogrula(kod.Length == 6 && kod.All(char.IsAsciiDigit), "Uygulamadaki 6 haneli kodu yazın.");
        Dogrula(YenileSifre.Length > 0, "Şifrenizi yazın.");
        var kodlar = await _api.KurtarmaKodlariYenileAsync(YenileSifre, kod);
        YenileKod = null;
        YenileSifre = "";
        KodlariGoster(kodlar);
        Bilgi = "Yeni kurtarma kodları üretildi; eskileri artık geçmez.";
        Hesap = await _api.HesabimAsync();
    });

    // ── Panoya kopyalama (Windows'ta etiket metni seçilemez) ──
    [RelayCommand]
    private Task SirKopyala() => Kurulum is not { } k ? Task.CompletedTask
        : KopyalaAsync(k.Sir, "Anahtar panoya kopyalandı; telefondaki uygulamaya yapıştırabilirsiniz.");

    [RelayCommand]
    private Task AdresKopyala() => Kurulum is not { } k ? Task.CompletedTask
        : KopyalaAsync(k.Adres, "Kurulum adresi panoya kopyalandı.");

    [RelayCommand]
    private Task KodlariKopyala() => KurtarmaKodlari.Count == 0 ? Task.CompletedTask
        : KopyalaAsync(KurtarmaKodlariMetni, "Kurtarma kodları panoya kopyalandı; güvenli bir yere yapıştırın.");

    private Task KopyalaAsync(string metin, string bilgi) => CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(_pano is not null, PanoYokMesaji);
        await _pano!.YazAsync(metin);
        Bilgi = bilgi;
    });

    /// <summary>"Kodları kaydettim": kodlar ekrandan kalkar.</summary>
    [RelayCommand]
    private void KodlariGizle()
    {
        KurtarmaKodlari.Clear();
        OnPropertyChanged(nameof(KurtarmaKodlariMetni));
    }

    private void KodlariGoster(IReadOnlyList<string> kodlar)
    {
        KurtarmaKodlari.Clear();
        foreach (var k in kodlar) KurtarmaKodlari.Add(k);
        OnPropertyChanged(nameof(KurtarmaKodlariMetni));
    }

    private static string KodTemizle(string? kod) => new((kod ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string Grupla(string sir)
    {
        var parcalar = Enumerable.Range(0, (sir.Length + 3) / 4).Select(i => sir.Substring(i * 4, Math.Min(4, sir.Length - i * 4)));
        return string.Join(" ", parcalar);
    }
}
