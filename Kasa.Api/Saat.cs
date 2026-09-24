namespace Kasa.Api;

/// <summary>İşletmenin yerel günü (Türkiye). Sunucu saati UTC olsa da "bugün" doğru gelir.</summary>
public static class Saat
{
    private static readonly TimeZoneInfo? Istanbul = Bul();

    private static TimeZoneInfo? Bul()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    public static DateTime Simdi(DateTime utc) => Istanbul is not null
        ? TimeZoneInfo.ConvertTimeFromUtc(utc, Istanbul)
        : utc.AddHours(3); // Türkiye 2016'dan beri yaz saati uygulamıyor: sabit UTC+3

    public static DateOnly Bugun() => DateOnly.FromDateTime(Simdi(DateTime.UtcNow));
}
