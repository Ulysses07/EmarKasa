namespace Kasa.Api;

/// <summary>
/// Dönem takviminin API tarafındaki kuralları. Dönemler takip başlangıcından
/// <see cref="Bitis"/>'e kadar üretilir; ileri tarihli işlem takvimi uzatmaz.
/// </summary>
public static class Takvim
{
    /// <summary>Takvimin son günü: bugün (takip başlangıcı ileride ise o gün).</summary>
    public static DateOnly Bitis(DateOnly takipBaslangic, DateOnly bugun)
        => takipBaslangic > bugun ? takipBaslangic : bugun;

    /// <summary>Tarihin içinde bulunduğu Mon–Sun haftanın Pazartesi'si.</summary>
    public static DateOnly Pazartesi(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>
    /// <paramref name="d"/>'yi içeren dönemin başlangıcı. Dönem sınırları takip başlangıcı,
    /// her Pazartesi ve her ayın 1'idir; bu yüzden başlangıç bu üçünün d'den küçük-eşit en
    /// büyüğüdür. <paramref name="d"/> takip başlangıcından önceyse null.
    /// </summary>
    public static DateOnly? DonemBaslangici(DateOnly d, DateOnly takipBaslangic)
    {
        if (d < takipBaslangic) return null;
        var ayBasi = new DateOnly(d.Year, d.Month, 1);
        var pzt = Pazartesi(d);
        var s = pzt > ayBasi ? pzt : ayBasi;
        return s > takipBaslangic ? s : takipBaslangic;
    }
}
