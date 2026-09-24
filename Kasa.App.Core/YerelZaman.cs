namespace Kasa.App.Core;

/// <summary>Sunucunun UTC zamanlarını yerel saatle "24.09.2026 12:00" biçiminde yazar (paket E ekranları).</summary>
public static class YerelZaman
{
    public static DateTime Cevir(DateTime zaman, TimeZoneInfo tz)
    {
        var utc = zaman.Kind == DateTimeKind.Local ? zaman.ToUniversalTime() : DateTime.SpecifyKind(zaman, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    public static string Metin(DateTime zaman, TimeZoneInfo tz)
        => Cevir(zaman, tz).ToString("dd.MM.yyyy HH:mm", Kultur.Turkce);

    public static string? Metin(DateTime? zaman, TimeZoneInfo tz) => zaman is { } z ? Metin(z, tz) : null;

    /// <summary>Yalnız gün: "24.09.2026".</summary>
    public static string Gun(DateTime zaman, TimeZoneInfo tz)
        => Cevir(zaman, tz).ToString("dd.MM.yyyy", Kultur.Turkce);
}
