using System.Globalization;

namespace Kasa.App.Core;

/// <summary>
/// Hatırlatıcının son çalıştığı günü ve son "oturum doldu" uyarısını yerel bir dosyada tutar.
/// Böylece bilgisayar kapalıyken kaçan günler sonraki açılışta telafi edilir.
/// </summary>
public sealed class HatirlatmaDurumu
{
    private readonly string _klasor;
    public HatirlatmaDurumu(string klasor) => _klasor = klasor;

    /// <summary>Varsayılan konum: %LOCALAPPDATA%\EmarKasa.</summary>
    public static HatirlatmaDurumu Varsayilan() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmarKasa"));

    public DateOnly? SonKontrol { get => Oku("son-kontrol.txt"); set => Yaz("son-kontrol.txt", value); }
    public DateOnly? SonOturumUyarisi { get => Oku("son-oturum-uyarisi.txt"); set => Yaz("son-oturum-uyarisi.txt", value); }

    private DateOnly? Oku(string ad)
    {
        try
        {
            var yol = Path.Combine(_klasor, ad);
            return File.Exists(yol) && DateOnly.TryParseExact(File.ReadAllText(yol).Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void Yaz(string ad, DateOnly? deger)
    {
        try
        {
            Directory.CreateDirectory(_klasor);
            var yol = Path.Combine(_klasor, ad);
            if (deger is { } d) File.WriteAllText(yol, d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            else if (File.Exists(yol)) File.Delete(yol);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
