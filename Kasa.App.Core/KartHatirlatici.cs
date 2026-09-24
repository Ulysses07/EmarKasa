using Kasa.ApiClient;

namespace Kasa.App.Core;

public enum HatirlatmaTuru { Kesim, SonOdeme3Gun, SonOdemeGunu }

public record Hatirlatma(int KartId, string KartAd, HatirlatmaTuru Tur, decimal EkstreBorc, DateOnly Tarih);

/// <summary>Kart kesim/son ödeme hatırlatmalarını üretir (yinelenme + kalan ekstre borcu kapısı).</summary>
/// <remarks>
/// Kapı kalan ekstre borcudur: API'nin <c>EkstreBorc</c>'u kesimden sonraki ödemeleri zaten düşer.
/// Ekstre tam ödenince 0 olur ve hatırlatma susar; kısmi ödemede kalan tutarla sürer.
/// Son ödeme tarihi her zaman kesimden SONRAKİ ilk "son ödeme günü"dür (gün numarası kesime eşit ya
/// da Şubat kırpmasıyla aynı güne düşse bile bir sonraki aya geçer).
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
    /// (bilgisayar kapalıyken kaçırılanlar dahil). Her kart için en güncel son ödeme hatırlatması
    /// (3 gün kala / son gün) ve ondan sonra gelen en güncel kesim hatırlatması döner; yani daha
    /// sonraki bir kesim, kaçırılan son ödeme gününü gizlemez.
    /// </summary>
    /// <remarks>
    /// <paramref name="sonKontrol"/> bugünden ileriyse (saat ileri alınmıştı) geçersiz sayılır ve
    /// dün kabul edilir; aksi halde hatırlatmalar o güne kadar susardı.
    /// </remarks>
    public static IReadOnlyList<Hatirlatma> VadesiGelenler(
        IEnumerable<KrediKartiGorunum> kartlar, DateOnly bugun, DateOnly? sonKontrol)
    {
        var son = SonKontrolDuzelt(sonKontrol, bugun);
        if (son is { } s && s >= bugun) return [];               // bugün zaten kontrol edildi
        var ilk = son is { } k0 ? k0.AddDays(1) : bugun;
        if (ilk < bugun.AddDays(-EnFazlaGeriGun)) ilk = bugun.AddDays(-EnFazlaGeriGun);

        var sonuc = new List<Hatirlatma>();
        foreach (var k in kartlar)
        {
            if (k.EkstreBorc <= 0) continue;                      // kalan borç yok → bildirim yok
            Hatirlatma? odeme = null, kesim = null;
            for (var gun = ilk; gun <= bugun; gun = gun.AddDays(1))
                foreach (var h in GununHatirlatmalari(k, gun))
                    if (h.Tur == HatirlatmaTuru.Kesim) kesim = h;
                    else odeme = h;
            if (odeme is not null) sonuc.Add(odeme);               // Tarih = hatırlatmanın asıl günü
            if (kesim is not null && (odeme is null || kesim.Tarih >= odeme.Tarih)) sonuc.Add(kesim);
        }
        return sonuc;
    }

    /// <summary>Bugünden ileri tarihli son kontrol geçersizdir → dün.</summary>
    public static DateOnly? SonKontrolDuzelt(DateOnly? sonKontrol, DateOnly bugun)
        => sonKontrol is { } s && s > bugun ? bugun.AddDays(-1) : sonKontrol;

    /// <summary>Kesimden KESİNLİKLE sonraki ilk son ödeme günü.</summary>
    public static DateOnly SonOdemeTarihi(KrediKartiGorunum k, DateOnly kesim)
        => KartTarih.SonrakiGun(k.SonOdemeTarihi.Day, kesim);

    /// <summary>
    /// <paramref name="gun"/> itibarıyla açık ekstre (kesim, son ödeme): önceki ekstrenin son ödeme
    /// günü henüz geçmediyse o (örn. kesim 15 / son ödeme 15 kartında ayın 15'i hem yeni kesim hem eski
    /// son gündür), aksi halde en son kesilen ekstre.
    /// </summary>
    public static (DateOnly Kesim, DateOnly SonOdeme) AcikEkstre(KrediKartiGorunum k, DateOnly gun)
    {
        var sonKesim = KartTarih.OncekiGun(k.KesimTarihi.Day, gun);
        var oncekiKesim = KartTarih.OncekiGun(k.KesimTarihi.Day, sonKesim.AddDays(-1));
        var oncekiVade = SonOdemeTarihi(k, oncekiKesim);
        return oncekiVade >= gun ? (oncekiKesim, oncekiVade) : (sonKesim, SonOdemeTarihi(k, sonKesim));
    }

    private static IEnumerable<Hatirlatma> GununHatirlatmalari(KrediKartiGorunum k, DateOnly gun)
    {
        var (kesim, vade) = AcikEkstre(k, gun);
        if (gun == vade)
            yield return new(k.Id, k.Ad, HatirlatmaTuru.SonOdemeGunu, k.EkstreBorc, gun);
        // 3 gün kala noktası kesimden önceye/kesim gününe düşüyorsa (kesim 1 / son ödeme 2) atlanır:
        // o gün henüz ekstre yoktur; kesim ve son gün hatırlatmaları yine gelir.
        else if (gun == vade.AddDays(-3) && gun > kesim)
            yield return new(k.Id, k.Ad, HatirlatmaTuru.SonOdeme3Gun, k.EkstreBorc, gun);

        if (gun == KartTarih.OncekiGun(k.KesimTarihi.Day, gun))
            yield return new(k.Id, k.Ad, HatirlatmaTuru.Kesim, k.EkstreBorc, gun);
    }

    /// <summary>
    /// Uygulama-içi şerit: açık ekstrenin son ödemesine 3 gün kala (kesimden önce değil) başlar;
    /// ekstre ödenene (EkstreBorc 0) ya da yeni ekstre açılana kadar sürer.
    /// </summary>
    public static bool OdemeBekliyor(KrediKartiGorunum k, DateOnly bugun)
    {
        if (k.EkstreBorc <= 0) return false;
        var (kesim, vade) = AcikEkstre(k, bugun);
        var baslangic = vade.AddDays(-3);
        if (baslangic < kesim) baslangic = kesim;
        return bugun >= baslangic;
    }
}
