using Kasa.ApiClient;

namespace Kasa.App.Core;

public enum HatirlatmaTuru { Kesim, SonOdeme3Gun, SonOdemeGunu }

public record Hatirlatma(int KartId, string KartAd, HatirlatmaTuru Tur, decimal EkstreBorc, DateOnly Tarih);

/// <summary>Kart kesim/son ödeme hatırlatmalarını üretir (yinelenme + kalan ekstre borcu kapısı).</summary>
/// <remarks>
/// Kapı kalan ekstre borcudur: API'nin <c>EkstreBorc</c>'u kesimden sonraki ödemeleri zaten düşer.
/// Ekstre tam ödenince 0 olur ve hatırlatma susar; kısmi ödemede kalan tutarla sürer.
/// </remarks>
public static class KartHatirlatici
{
    /// <summary>Kaçırılan günleri geriye en fazla bu kadar gün tarar.</summary>
    public const int EnFazlaGeriGun = 7;

    public static IReadOnlyList<Hatirlatma> VadesiGelenler(
        IEnumerable<KrediKartiGorunum> kartlar, DateOnly bugun)
        => VadesiGelenler(kartlar, bugun, sonKontrol: null);

    /// <summary>
    /// <paramref name="sonKontrol"/>'den sonraki günlerden bugüne kadar düşen hatırlatmalar
    /// (bilgisayar kapalıyken kaçırılanlar dahil). Her kart için yalnız en güncel olanı döner.
    /// </summary>
    public static IReadOnlyList<Hatirlatma> VadesiGelenler(
        IEnumerable<KrediKartiGorunum> kartlar, DateOnly bugun, DateOnly? sonKontrol)
    {
        if (sonKontrol is { } s && s >= bugun) return [];        // bugün zaten kontrol edildi
        var ilk = sonKontrol is { } k0 ? k0.AddDays(1) : bugun;
        if (ilk < bugun.AddDays(-EnFazlaGeriGun)) ilk = bugun.AddDays(-EnFazlaGeriGun);

        var sonuc = new List<Hatirlatma>();
        foreach (var k in kartlar)
        {
            if (k.EkstreBorc <= 0) continue;                      // kalan borç yok → bildirim yok
            Hatirlatma? enSon = null;
            for (var gun = ilk; gun <= bugun; gun = gun.AddDays(1))
                enSon = GununHatirlatmasi(k, gun) ?? enSon;
            if (enSon is not null) sonuc.Add(enSon);                // Tarih = hatırlatmanın asıl günü
        }
        return sonuc;
    }

    private static Hatirlatma? GununHatirlatmasi(KrediKartiGorunum k, DateOnly gun)
    {
        var sonKesim = KartTarih.OncekiGun(k.KesimTarihi.Day, gun);
        var sonOdeme = KartTarih.SonrakiGun(k.SonOdemeTarihi.Day, sonKesim);
        if (gun == sonOdeme) return new(k.Id, k.Ad, HatirlatmaTuru.SonOdemeGunu, k.EkstreBorc, gun);
        if (gun == sonOdeme.AddDays(-3)) return new(k.Id, k.Ad, HatirlatmaTuru.SonOdeme3Gun, k.EkstreBorc, gun);
        if (gun == sonKesim) return new(k.Id, k.Ad, HatirlatmaTuru.Kesim, k.EkstreBorc, gun);
        return null;
    }

    /// <summary>Uygulama-içi şerit: son ödemeye 3 gün kala → ekstre kapanana/yeni kesime kadar.</summary>
    public static bool OdemeBekliyor(KrediKartiGorunum k, DateOnly bugun)
    {
        if (k.EkstreBorc <= 0) return false;
        var sonKesim = KartTarih.OncekiGun(k.KesimTarihi.Day, bugun);
        var sonOdeme = KartTarih.SonrakiGun(k.SonOdemeTarihi.Day, sonKesim);
        return bugun >= sonOdeme.AddDays(-3);
    }
}
