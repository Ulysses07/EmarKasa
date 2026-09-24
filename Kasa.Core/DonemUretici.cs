namespace Kasa.Core;

public static class DonemUretici
{
    /// <summary>
    /// [baslangic, bitis] aralığı için dönemleri üretir. Haftalar Pazartesi
    /// başlar (Mon–Sun); her dönem hafta-sonu veya ay-sonundan hangisi önce
    /// gelirse orada kapanır. Böylece her dönem tek bir takvim ayına aittir.
    /// </summary>
    public static IReadOnlyList<Donem> Uret(DateOnly baslangic, DateOnly bitis)
    {
        var sonuc = new List<Donem>();
        var imlec = baslangic;
        while (imlec <= bitis)
        {
            var donemSonu = DogalBitis(imlec);
            if (donemSonu > bitis) donemSonu = bitis;
            sonuc.Add(new Donem(imlec, donemSonu));
            imlec = donemSonu.AddDays(1);
        }
        return sonuc;
    }

    /// <summary>
    /// <paramref name="start"/> ile başlayan dönemin, bitiş tarihiyle kırpılmamış
    /// doğal sonu: hafta sonu (Pazar) veya ay sonundan hangisi önce gelirse.
    /// </summary>
    public static DateOnly DogalBitis(DateOnly start)
    {
        var haftaSonu = HaftaninPazari(start);
        var aySonu = AyinSonGunu(start);
        return haftaSonu <= aySonu ? haftaSonu : aySonu;
    }

    /// <summary>Dönem, ayın son gününü içeren (ayın gerçek son) dönemi mi?</summary>
    public static bool AyinSonDonemiMi(Donem d) => DogalBitis(d.Start) == AyinSonGunu(d.Start);

    // Verilen tarihin içinde bulunduğu Mon–Sun haftasının Pazar günü.
    private static DateOnly HaftaninPazari(DateOnly d)
    {
        // DayOfWeek: Sunday=0, Monday=1 ... Saturday=6
        int gun = (int)d.DayOfWeek;
        int pazaraKalan = gun == 0 ? 0 : 7 - gun;
        return d.AddDays(pazaraKalan);
    }

    private static DateOnly AyinSonGunu(DateOnly d)
        => new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
}
