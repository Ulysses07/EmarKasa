using Kasa.Api.Data;

namespace Kasa.Api;

/// <summary>Bekleyen hesabı için tekrarlayan giderin gerekli alanları.</summary>
/// <remarks>Paket D: Siklik (varsayılan Aylık), TutarDegisken ve KrediKartiId bekleyen satırına taşınır.</remarks>
public record TekrarlayanSablon(int Id, string Kalem, string Kanal, decimal Tutar, int AyinGunu, bool Aktif, DateOnly BaslangicAyi,
    TekrarSikligi Siklik = TekrarSikligi.Aylik, bool TutarDegisken = false, int? KrediKartiId = null);

/// <summary>
/// Tekrarlayan gider takvimi (saf). Hiçbir şey kendiliğinden girilmez: vadesi gelmiş ve
/// karar verilmemiş aylar "bekleyen" olarak listelenir.
/// </summary>
public static class TekrarlayanTakvim
{
    /// <summary>Bekleyen listesi bu ayla birlikte en fazla kaç ay geriye bakar (bu ay + önceki 2 ay).</summary>
    public const int GeriyeAy = 2;

    public static DateOnly AyBasi(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>Sıklığın ay cinsinden periyodu (Aylık 1, 3 ayda bir 3, 6 ayda bir 6, Yıllık 12).</summary>
    public static int Periyot(TekrarSikligi siklik) => siklik switch
    {
        TekrarSikligi.UcAylik => 3,
        TekrarSikligi.AltiAylik => 6,
        TekrarSikligi.Yillik => 12,
        _ => 1,
    };

    /// <summary>
    /// <paramref name="ay"/> bu şablonun tekrarlandığı aylardan mı: başlangıç ayından itibaren her
    /// periyotta bir (Aylık şablonda başlangıçtan sonraki her ay). Başlangıçtan önceki aylar hayır.
    /// </summary>
    public static bool AyDahil(TekrarSikligi siklik, DateOnly baslangicAyi, DateOnly ay)
    {
        var fark = (ay.Year - baslangicAyi.Year) * 12 + ay.Month - baslangicAyi.Month;
        return fark >= 0 && fark % Periyot(siklik) == 0;
    }

    /// <summary>Ayın vadesi: ayın <paramref name="ayinGunu"/>'ü; ay daha kısaysa ayın son günü (31 → 28/29/30).</summary>
    public static DateOnly Vade(DateOnly ay, int ayinGunu)
        => new(ay.Year, ay.Month, Math.Clamp(ayinGunu, 1, DateTime.DaysInMonth(ay.Year, ay.Month)));

    /// <summary>
    /// Her aktif şablon için max(başlangıç ayı, bu ay − 2) ile bu ay arasındaki, sıklığına uyan, vadesi
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
                if (!AyDahil(s.Siklik, bas, ay)) continue;
                var vade = Vade(ay, s.AyinGunu);
                if (vade > bugun || kararlar.Contains((s.Id, ay))) continue;
                liste.Add(new TekrarlayanBekleyenDto(s.Id, s.Kalem, s.Kanal, s.Tutar, ay, vade, s.TutarDegisken, s.KrediKartiId, s.Siklik));
            }
        }
        return liste
            .OrderBy(b => b.Vade)
            .ThenBy(b => b.Kalem, Metin.Sirala)
            .ThenBy(b => b.TekrarlayanGiderId)
            .ToList();
    }
}
