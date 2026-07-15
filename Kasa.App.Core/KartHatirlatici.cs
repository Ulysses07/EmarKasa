using Kasa.ApiClient;

namespace Kasa.App.Core;

public enum HatirlatmaTuru { Kesim, SonOdeme3Gun, SonOdemeGunu }

public record Hatirlatma(int KartId, string KartAd, HatirlatmaTuru Tur, decimal EkstreBorc, DateOnly Tarih);

/// <summary>Kart kesim/son ödeme hatırlatmalarını üretir (yinelenme + borç/ödeme kapısı).</summary>
public static class KartHatirlatici
{
    public static IReadOnlyList<Hatirlatma> VadesiGelenler(
        IEnumerable<KrediKartiGorunum> kartlar, DateOnly bugun)
    {
        var sonuc = new List<Hatirlatma>();
        foreach (var k in kartlar)
        {
            if (k.EkstreBorc <= 0) continue;                      // borç yok → bildirim yok
            var sonKesim = KartTarih.OncekiGun(k.KesimTarihi.Day, bugun);

            if (bugun == sonKesim)                                // kesim günü (bilgi)
                sonuc.Add(new(k.Id, k.Ad, HatirlatmaTuru.Kesim, k.EkstreBorc, bugun));

            bool oDonemOdenmis = k.Odemeler.Any(o => o.Tarih >= sonKesim);
            if (oDonemOdenmis) continue;                          // ele alındı → ödeme hatırlatması yok

            var sonOdeme = KartTarih.SonrakiGun(k.SonOdemeTarihi.Day, sonKesim);
            if (bugun == sonOdeme.AddDays(-3))
                sonuc.Add(new(k.Id, k.Ad, HatirlatmaTuru.SonOdeme3Gun, k.EkstreBorc, bugun));
            else if (bugun == sonOdeme)
                sonuc.Add(new(k.Id, k.Ad, HatirlatmaTuru.SonOdemeGunu, k.EkstreBorc, bugun));
        }
        return sonuc;
    }

    /// <summary>Uygulama-içi şerit: son ödemeye 3 gün kala → bu dönem ödenene/yeni kesime kadar.</summary>
    public static bool OdemeBekliyor(KrediKartiGorunum k, DateOnly bugun)
    {
        if (k.EkstreBorc <= 0) return false;
        var sonKesim = KartTarih.OncekiGun(k.KesimTarihi.Day, bugun);
        if (k.Odemeler.Any(o => o.Tarih >= sonKesim)) return false;
        var sonOdeme = KartTarih.SonrakiGun(k.SonOdemeTarihi.Day, sonKesim);
        return bugun >= sonOdeme.AddDays(-3);
    }
}
