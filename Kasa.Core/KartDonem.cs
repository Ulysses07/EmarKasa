namespace Kasa.Core;

/// <summary>Bir ekstrenin kesim tarihi ve o ekstrenin son ödeme tarihi.</summary>
public record KartEkstreTarihi(DateOnly Kesim, DateOnly SonOdeme);

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

    /// <summary>
    /// <paramref name="kesim"/> tarihinde kesilen ekstrenin son ödeme tarihi: kesimden KESİNLİKLE
    /// SONRA gelen ilk <paramref name="sonOdemeGunu"/> (kısa ayda ay sonuna kırpılır).
    /// Son ödeme günü kesim gününe eşit/küçükse bir sonraki aya düşer (kesim 15 / ödeme 15 → ertesi ayın 15'i).
    /// Şubat kırpmasıyla aynı güne çöken durumda da (kesim 30 → 28 Şubat, ödeme 29 → 28 Şubat)
    /// son ödeme Mart'a (29 Mart) gider; son ödeme hiçbir zaman kesim günüyle aynı olamaz.
    /// </summary>
    public static DateOnly SonOdeme(DateOnly kesim, int sonOdemeGunu)
    {
        var t = GunClamp(kesim.Year, kesim.Month, sonOdemeGunu);
        if (t > kesim) return t;
        int y = kesim.Year, m = kesim.Month + 1;
        if (m > 12) { m = 1; y++; }
        return GunClamp(y, m, sonOdemeGunu);
    }

    /// <summary>
    /// <paramref name="bugun"/> itibarıyla ödemesi beklenen ekstre. Normalde son kesimin
    /// (<see cref="SonKesim"/>) ekstresidir; ancak bir önceki ekstrenin son ödemesi henüz geçmediyse
    /// (ör. kesim 15 / ödeme 15 iken ayın 15'i: yeni kesim günü aynı zamanda eski ekstrenin son
    /// ödeme günüdür) o ekstre döner. <see cref="KartEkstreTarihi.Kesim"/> daima ≤ bugün;
    /// <see cref="KartEkstreTarihi.SonOdeme"/> son ekstrenin ödemesi geçtiyse bugünden önce olabilir
    /// (yeni kesime kadar gecikmiş/ödenmiş ekstre).
    /// </summary>
    public static KartEkstreTarihi AcikEkstre(int kesimGunu, int sonOdemeGunu, DateOnly bugun)
    {
        var kesim = SonKesim(kesimGunu, bugun);
        var oncekiKesim = SonKesim(kesimGunu, kesim.AddDays(-1));
        var oncekiOdeme = SonOdeme(oncekiKesim, sonOdemeGunu);
        if (oncekiOdeme >= bugun) return new KartEkstreTarihi(oncekiKesim, oncekiOdeme);
        return new KartEkstreTarihi(kesim, SonOdeme(kesim, sonOdemeGunu));
    }

    private static DateOnly GunClamp(int yil, int ay, int gun)
        => new(yil, ay, Math.Min(gun, DateTime.DaysInMonth(yil, ay)));
}
