namespace Kasa.Api;

/// <summary>Bekleyen hesabı için tekrarlayan giderin gerekli alanları.</summary>
public record TekrarlayanSablon(int Id, string Kalem, string Kanal, decimal Tutar, int AyinGunu, bool Aktif, DateOnly BaslangicAyi);

/// <summary>
/// Tekrarlayan gider takvimi (saf). Hiçbir şey kendiliğinden girilmez: vadesi gelmiş ve
/// karar verilmemiş aylar "bekleyen" olarak listelenir.
/// </summary>
public static class TekrarlayanTakvim
{
    /// <summary>Bekleyen listesi bu ayla birlikte en fazla kaç ay geriye bakar (bu ay + önceki 2 ay).</summary>
    public const int GeriyeAy = 2;

    public static DateOnly AyBasi(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>Ayın vadesi: ayın <paramref name="ayinGunu"/>'ü; ay daha kısaysa ayın son günü (31 → 28/29/30).</summary>
    public static DateOnly Vade(DateOnly ay, int ayinGunu)
        => new(ay.Year, ay.Month, Math.Clamp(ayinGunu, 1, DateTime.DaysInMonth(ay.Year, ay.Month)));

    /// <summary>
    /// Her aktif şablon için max(başlangıç ayı, bu ay − 2) ile bu ay arasındaki, vadesi
    /// <paramref name="bugun"/>'ü geçmemiş ve kararı (girildi/atlandı) olmayan aylar; vadeye göre sıralı.
    /// </summary>
    /// <param name="kararlar">Karar verilmiş (şablon Id, ay başı) çiftleri.</param>
    public static IReadOnlyList<TekrarlayanBekleyenDto> Bekleyenler(
        IEnumerable<TekrarlayanSablon> sablonlar, IReadOnlySet<(int Id, DateOnly Ay)> kararlar, DateOnly bugun)
    {
        var buAy = AyBasi(bugun);
        var enErken = buAy.AddMonths(-GeriyeAy);
        var liste = new List<TekrarlayanBekleyenDto>();
        foreach (var s in sablonlar.Where(s => s.Aktif))
        {
            var bas = AyBasi(s.BaslangicAyi);
            for (var ay = bas > enErken ? bas : enErken; ay <= buAy; ay = ay.AddMonths(1))
            {
                var vade = Vade(ay, s.AyinGunu);
                if (vade > bugun || kararlar.Contains((s.Id, ay))) continue;
                liste.Add(new TekrarlayanBekleyenDto(s.Id, s.Kalem, s.Kanal, s.Tutar, ay, vade));
            }
        }
        return liste
            .OrderBy(b => b.Vade)
            .ThenBy(b => b.Kalem, Metin.Sirala)
            .ThenBy(b => b.TekrarlayanGiderId)
            .ToList();
    }
}
