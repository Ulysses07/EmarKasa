namespace Kasa.Core;

/// <summary>Karta bağlı tek bir harcama (tarih + tutar).</summary>
public record KartHarcama(DateOnly Tarih, decimal Tutar);

/// <summary>
/// Bir kartın <c>bugun</c> itibarıyla türetilmiş durumu.
/// <see cref="GuncelBorc"/> negatif olabilir: fazla ödeme karttaki ALACAKTIR (sonraki harcamalardan
/// düşer). <see cref="EkstreBorc"/> hiçbir zaman negatif olmaz (0'da kırpılır): fazla ödeme ekstreyi
/// kapatır, artanı <see cref="GuncelBorc"/>'ta alacak olarak görünür ve bir sonraki ekstreyi azaltır.
/// </summary>
public record KartDurumu(
    decimal HarcamaToplam,         // bugüne kadar (bugün dahil) harcamalar
    decimal OdemeToplam,           // bugüne kadar (bugün dahil) ödemeler
    decimal GuncelBorc,            // açılış + harcama − ödeme (negatifse alacak)
    decimal EkstreBorc,            // son kesime kadarki borçtan kalan ödenmemiş kısım (≥ 0)
    decimal GelecekHarcamaToplam,  // ileri tarihli (bugünden sonra) harcamalar — borca henüz girmez
    decimal GelecekOdemeToplam);   // ileri tarihli ödemeler — borçtan henüz düşmez

/// <summary>Kredi kartı güncel borcunu türetir (saf, yan-etkisiz).
/// GüncelBorç = açılış + harcamalar − ödemeler. HesapMotoru'na dokunmaz.</summary>
public static class KartHesap
{
    /// <summary>GüncelBorç = açılış + harcamalar − ödemeler (tarih filtresi yok; toplamlar hazır verilir).</summary>
    /// <param name="acilisBorc">
    /// Kartın açılış borcu: takip başlamadan önce karta birikmiş, kayıtlarda kalem kalem olmayan borç.
    /// DİKKAT: Aynı borç ayrıca karta bağlanmamış (KrediKartiId'siz) K.K işlemleri olarak da girildiyse
    /// (ör. Excel'den aktarılan eski K.K satırları) ve bu açılış borcu kart ödemesiyle kapatılırsa kasa
    /// aynı parayı İKİ KEZ düşer: karta bağlı olmayan K.K ertesi ayın son döneminde ertelemeyle, kart
    /// ödemesi de ödendiği dönemde. Hesap motoru niyeti bilemez; arayüz, karta bağlı olmayan K.K varken
    /// açılış borcu girilince/ödenince kullanıcıyı uyarmalıdır.
    /// </param>
    /// <param name="harcamaToplam">Karta bağlı harcamaların toplamı.</param>
    /// <param name="odemeToplam">Karta yapılan ödemelerin toplamı.</param>
    public static decimal GuncelBorc(decimal acilisBorc, decimal harcamaToplam, decimal odemeToplam)
        => acilisBorc + harcamaToplam - odemeToplam;

    /// <summary>
    /// Kartın <paramref name="bugun"/> itibarıyla durumunu hesaplar.
    /// Kurallar:
    /// <list type="bullet">
    /// <item>İleri tarihli (Tarih &gt; bugün) harcama ve ödemeler GüncelBorç/Harcama/Ödeme toplamlarına
    /// girmez; ayrıca <see cref="KartDurumu.GelecekHarcamaToplam"/>/<see cref="KartDurumu.GelecekOdemeToplam"/>
    /// olarak raporlanır.</item>
    /// <item>GüncelBorç = açılış + harcama(≤ bugün) − ödeme(≤ bugün); negatifse karttaki alacaktır.</item>
    /// <item>EkstreBorç = max(0, açılış + harcama(≤ son kesim) − ödeme(≤ bugün)). Ödemeler önce en eski
    /// borcu (ekstreyi) kapatır; fazlası kesim sonrası harcamaları (bir sonraki ekstreyi) azaltır.</item>
    /// </list>
    /// Tutarlar kuruşa yuvarlanır (<see cref="Para.Yuvarla"/>).
    /// </summary>
    /// <param name="acilisBorc">Açılış borcu — çift düşme riski için <see cref="GuncelBorc(decimal, decimal, decimal)"/>'a bakın.</param>
    public static KartDurumu Durum(
        decimal acilisBorc,
        IEnumerable<KartHarcama> harcamalar,
        IEnumerable<KartOdeme> odemeler,
        int kesimGunu,
        DateOnly bugun)
    {
        var sonKesim = KartDonem.SonKesim(kesimGunu, bugun);
        decimal harcama = 0m, gelecekHarcama = 0m, kesimeKadar = 0m;
        foreach (var h in harcamalar)
        {
            var t = Para.Yuvarla(h.Tutar);
            if (h.Tarih > bugun) { gelecekHarcama += t; continue; }
            harcama += t;
            if (h.Tarih <= sonKesim) kesimeKadar += t;
        }
        decimal odeme = 0m, gelecekOdeme = 0m;
        foreach (var o in odemeler)
        {
            var t = Para.Yuvarla(o.Tutar);
            if (o.Tarih > bugun) gelecekOdeme += t;
            else odeme += t;
        }
        var acilis = Para.Yuvarla(acilisBorc);
        var guncel = acilis + harcama - odeme;
        var ekstre = Math.Max(0m, acilis + kesimeKadar - odeme);
        return new KartDurumu(harcama, odeme, guncel, ekstre, gelecekHarcama, gelecekOdeme);
    }
}
