using System.Text.Json;

namespace Kasa.App.Core;

/// <summary>"Bu bilgisayarda Windows bildirimleri" anahtarı (yalnız bu bilgisayar; tasarım 2026-09-30 masaüstü bildirimleri §2).
/// Uygulama ve pencere açmadan çalışan görev aynı değeri okur.</summary>
public interface IBildirimAyari
{
    bool Acik { get; set; }
}

/// <summary>
/// %LOCALAPPDATA%\EmarKasa\bildirim-ayari.json (<c>{"Acik":true}</c>). Dosya yoksa, okunamıyorsa ya da bozuksa açık sayılır (editör
/// için varsayılan açık). Yazma önce geçici dosyaya yapılır, sonra yerine taşınır: okuyan süreç yarım dosya görmez. Yazılamazsa değer
/// bu süreçte bellekte kalır, hata dışarı çıkmaz. MAUI Preferences kullanılmaz: paketsiz uygulamada Preferences dosyayı süreç başında
/// bir kez okuyup bellekte tutar ve her yazışta kilitsiz baştan yazar (dotnet/maui Preferences.windows.cs,
/// UnpackagedPreferencesImplementation); uygulama ile görev aynı ayarı iki süreçte kullanır.
/// </summary>
public sealed class DosyaBildirimAyari : IBildirimAyari
{
    public const string DosyaAdi = "bildirim-ayari.json";

    private readonly string _klasor;
    private readonly string _yol;
    private bool? _bellekte;

    public DosyaBildirimAyari(string klasor)
    {
        _klasor = klasor;
        _yol = Path.Combine(klasor, DosyaAdi);
    }

    public static DosyaBildirimAyari Varsayilan() => new(YerelKlasor.Yol);

    public bool Acik
    {
        get => _bellekte ?? Oku();
        set => Yaz(value);
    }

    private bool Oku()
    {
        try
        {
            if (!File.Exists(_yol))
                return true;
            return JsonSerializer.Deserialize<Kayit>(File.ReadAllText(_yol))?.Acik ?? true;
        }
        catch (Exception)
        {
            return true;
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
            File.Move(gecici, _yol, overwrite: true);
            _bellekte = null;
        }
        catch (Exception) { }
    }

    private sealed record Kayit(bool Acik);
}
