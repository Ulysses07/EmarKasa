using System.Text.Json;

namespace Kasa.App.Core;

/// <summary>"Bu bilgisayarda Windows bildirimleri" anahtarı (yalnız bu bilgisayar; tasarım 2026-09-30 masaüstü bildirimleri §2).
/// Uygulama ve pencere açmadan çalışan görev aynı değeri okur.</summary>
public interface IBildirimAyari
{
    bool Acik { get; set; }
}

/// <summary>
/// %LOCALAPPDATA%\EmarKasa\bildirim-ayari.json (<c>{"Acik":true}</c>). Dosya yoksa ya da bozuksa açık sayılır (editör için varsayılan
/// açık). Yazma önce geçici dosyaya yapılır, sonra yerine taşınır: okuyan süreç yarım dosya görmez. Başka süreç dosyayı o an tutuyorsa
/// (okuma ya da taşıma sırasında) kısa aralıklarla yeniden denenir (varsayılan 3 deneme, aralarda 50 ms). Denemelere rağmen okunamayan
/// mevcut dosyada bu süreçte son bilinen değer, o da yoksa kapalı sayılır: dosya ancak kullanıcı anahtarı değiştirince oluşur,
/// bilinmeyen değer "açık" varsayılıp kapatılmış bildirim gösterilmez. Yazılamazsa değer bu süreçte bellekte kalır, hata dışarı
/// çıkmaz. MAUI Preferences kullanılmaz: paketsiz uygulamada Preferences dosyayı süreç başında bir kez okuyup bellekte tutar ve her
/// yazışta kilitsiz baştan yazar (dotnet/maui Preferences.windows.cs, UnpackagedPreferencesImplementation); uygulama ile görev aynı
/// ayarı iki süreçte kullanır.
/// </summary>
public sealed class DosyaBildirimAyari : IBildirimAyari
{
    public const string DosyaAdi = "bildirim-ayari.json";

    private readonly string _klasor;
    private readonly string _yol;
    private readonly int _deneme;
    private readonly TimeSpan _bekleme;
    /// <summary>Dosyaya yazılamayan değer (bu süreçte geçerli).</summary>
    private bool? _bellekte;
    /// <summary>Bu süreçte dosyadan son okunan ya da dosyaya son yazılan değer.</summary>
    private bool? _sonBilinen;

    /// <param name="deneme">Dosya başka süreçte açıkken en çok deneme sayısı (varsayılan 3).</param>
    /// <param name="bekleme">Denemeler arası bekleme (varsayılan 50 ms).</param>
    public DosyaBildirimAyari(string klasor, int deneme = 3, TimeSpan? bekleme = null)
    {
        _klasor = klasor;
        _yol = Path.Combine(klasor, DosyaAdi);
        _deneme = deneme;
        _bekleme = bekleme ?? TimeSpan.FromMilliseconds(50);
    }

    public static DosyaBildirimAyari Varsayilan() => new(YerelKlasor.Yol);

    public bool Acik
    {
        get => _bellekte ?? Oku();
        set => Yaz(value);
    }

    private bool Oku()
    {
        if (!File.Exists(_yol))
            return true;
        try
        {
            var metin = "";
            Dene(() => metin = File.ReadAllText(_yol));
            var acik = JsonSerializer.Deserialize<Kayit>(metin)?.Acik ?? true;
            _sonBilinen = acik;
            return acik;
        }
        catch (JsonException)
        {
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Denetimle okuma arasında silindi: dosya yok sayılır.
            return true;
        }
        catch (Exception)
        {
            return _sonBilinen ?? false;
        }
    }

    private void Yaz(bool acik)
    {
        _bellekte = acik;
        try
        {
            Directory.CreateDirectory(_klasor);
            var gecici = _yol + ".yeni";
            File.WriteAllText(gecici, JsonSerializer.Serialize(new Kayit(acik)));
            Dene(() => File.Move(gecici, _yol, overwrite: true));
            _bellekte = null;
            _sonBilinen = acik;
        }
        catch (Exception) { }
    }

    /// <summary>Dosya başka süreçte açıkken (<see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>) işlemi kısa
    /// aralıklarla yeniden dener; son denemenin hatası dışarı çıkar (GosterilenBildirimDeposu.Ac deseni).</summary>
    private void Dene(Action islem)
    {
        for (var sira = 1; ; sira++)
        {
            try
            {
                islem();
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && sira < _deneme)
            {
                Thread.Sleep(_bekleme);
            }
        }
    }

    private sealed record Kayit(bool Acik);
}
