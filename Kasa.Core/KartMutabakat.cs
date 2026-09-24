namespace Kasa.Core;

/// <summary>Kapanmış bir ekstre dönemi: önceki kesimin ertesi günü ile kesim günü (ikisi dahil).</summary>
public record KartEkstreDonemi(DateOnly Baslangic, DateOnly Kesim);

/// <summary>
/// Bir ekstre döneminin uygulamadaki dökümü. <see cref="DonemSonuBorc"/> = devreden + dönem harcaması
/// − dönem ödemesi; kesim günündeki <see cref="KartHesap.Durum"/>.GuncelBorc ile aynıdır.
/// </summary>
public record KartDonemOzeti(
    decimal DevredenBorc,   // önceki kesim günü sonundaki borç (açılış dahil)
    decimal DonemHarcama,   // dönem içindeki harcamalar
    decimal DonemOdeme,     // dönem içindeki ödemeler
    decimal DonemSonuBorc); // kesim günü sonundaki borç

/// <summary>
/// Kart ekstresi mutabakatı için saf hesaplar. Kart borcunu HESAPLAMAZ ya da DEĞİŞTİRMEZ:
/// dönem sonu borcu doğrudan <see cref="KartHesap.Durum"/>'dan (bugün = kesim) okunur; yalnız
/// ekrandaki döküm için dönem harcaması/ödemesi ayrıca toplanır.
/// </summary>
public static class KartMutabakat
{
    /// <summary>
    /// <paramref name="bugun"/> itibarıyla kapanmış son <paramref name="adet"/> ekstre dönemi, en yeni önce.
    /// Kesim günü kısa ayda ay sonuna kırpılır (<see cref="KartDonem.SonKesim"/>).
    /// </summary>
    public static IReadOnlyList<KartEkstreDonemi> KapanmisDonemler(int kesimGunu, DateOnly bugun, int adet)
    {
        var l = new List<KartEkstreDonemi>();
        if (adet <= 0) return l;
        var kesim = KartDonem.SonKesim(kesimGunu, bugun);
        for (int i = 0; i < adet; i++)
        {
            var onceki = KartDonem.SonKesim(kesimGunu, kesim.AddDays(-1));
            l.Add(new KartEkstreDonemi(onceki.AddDays(1), kesim));
            kesim = onceki;
        }
        return l;
    }

    /// <summary>Kesim tarihinin dönemi (kesim, kesim gününe denk gelmiyorsa null).</summary>
    public static KartEkstreDonemi? Donem(int kesimGunu, DateOnly kesim)
    {
        if (KartDonem.SonKesim(kesimGunu, kesim) != kesim) return null;
        var onceki = KartDonem.SonKesim(kesimGunu, kesim.AddDays(-1));
        return new KartEkstreDonemi(onceki.AddDays(1), kesim);
    }

    /// <summary>Dönemin dökümü; tutarlar kuruşa yuvarlanır. Dönem sonu borcu KartHesap.Durum ile birebir aynıdır.</summary>
    public static KartDonemOzeti Ozet(
        decimal acilisBorc, IReadOnlyCollection<KartHarcama> harcamalar, IReadOnlyCollection<KartOdeme> odemeler,
        int kesimGunu, KartEkstreDonemi donem)
    {
        var devreden = KartHesap.Durum(acilisBorc, harcamalar, odemeler, kesimGunu, donem.Baslangic.AddDays(-1)).GuncelBorc;
        var sonu = KartHesap.Durum(acilisBorc, harcamalar, odemeler, kesimGunu, donem.Kesim).GuncelBorc;
        decimal harcama = 0m, odeme = 0m;
        foreach (var h in harcamalar)
            if (h.Tarih >= donem.Baslangic && h.Tarih <= donem.Kesim) harcama += Para.Yuvarla(h.Tutar);
        foreach (var o in odemeler)
            if (o.Tarih >= donem.Baslangic && o.Tarih <= donem.Kesim) odeme += Para.Yuvarla(o.Tutar);
        return new KartDonemOzeti(devreden, harcama, odeme, sonu);
    }
}
