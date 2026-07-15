namespace Kasa.App.Core;

/// <summary>Kart tarihlerini ayın günü olarak yorumlar (aylık yinelenme).</summary>
public static class KartTarih
{
    /// <summary><paramref name="gun"/> günlü, <paramref name="referans"/>'a küçük/eşit en son tarih.</summary>
    public static DateOnly OncekiGun(int gun, DateOnly referans)
    {
        int y = referans.Year, m = referans.Month;
        var t = Clamp(y, m, gun);
        if (t <= referans) return t;
        m--; if (m < 1) { m = 12; y--; }
        return Clamp(y, m, gun);
    }

    /// <summary><paramref name="gun"/> günlü, <paramref name="referans"/>'a büyük/eşit ilk tarih.</summary>
    public static DateOnly SonrakiGun(int gun, DateOnly referans)
    {
        int y = referans.Year, m = referans.Month;
        var t = Clamp(y, m, gun);
        if (t >= referans) return t;
        m++; if (m > 12) { m = 1; y++; }
        return Clamp(y, m, gun);
    }

    private static DateOnly Clamp(int yil, int ay, int gun)
        => new(yil, ay, Math.Min(gun, DateTime.DaysInMonth(yil, ay)));
}
