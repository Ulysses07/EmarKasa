namespace Kasa.Core;

/// <summary>Kredi kartı tarihlerini ayın günü olarak yorumlar (aylık yinelenme).</summary>
public static class KartDonem
{
    /// <summary><paramref name="gun"/> günlü, <paramref name="bugun"/>'e küçük/eşit en son tarih.
    /// Ay kısa ise ay sonuna kırpar.</summary>
    public static DateOnly SonKesim(int gun, DateOnly bugun)
    {
        int y = bugun.Year, m = bugun.Month;
        var t = GunClamp(y, m, gun);
        if (t <= bugun) return t;
        m--; if (m < 1) { m = 12; y--; }
        return GunClamp(y, m, gun);
    }

    private static DateOnly GunClamp(int yil, int ay, int gun)
        => new(yil, ay, Math.Min(gun, DateTime.DaysInMonth(yil, ay)));
}
