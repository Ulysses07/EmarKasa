using System.Globalization;

namespace Kasa.Core;

/// <summary>Nakit tahmini kaleminin kaynağı.</summary>
public enum TahminKalemTuru
{
    AlinanCek,          // portföydeki alınan çek (vadesinde +) ya da ileri tarihli girilmiş tahsilat
    VerilenCek,         // ödenecek verilen çek (vadesinde −) ya da ileri tarihli girilmiş ödeme
    KartOdemesi,        // ekstre borcu (son ödeme gününde −) ya da ileri tarihli girilmiş kart ödemesi
    TekrarlayanGider,   // onay bekleyen ya da vadesi gelecek tekrarlayan gider (vade gününde −)
    IleriTarihliIslem,  // ileri tarihli girilmiş gider (Cari / sabit gider; tarihinde −)
    KartsizKrediKarti,  // karta bağlı olmayan eski K.K: sonraki ayın son döneminin ilk günü −
}

/// <summary>
/// Tahmindeki tek nakit hareketi. <see cref="Tarih"/> kalemin asıl günüdür (vade, son ödeme, işlem
/// tarihi); bugün ya da daha önceyse kalem hâlâ bekliyor demektir ve tahminde yarına yazılır.
/// <see cref="Tutar"/> işaretlidir: + kasaya giriş, − kasadan çıkış.
/// </summary>
/// <param name="CekId">Çek kalemlerinde çekin Id'si (hesaptan çıkarılabilmesi için).</param>
/// <param name="Gecikmis">Asıl günü bugünden önce (vadesi/son ödemesi geçmiş, hâlâ bekliyor).</param>
public record TahminKalemi(DateOnly Tarih, TahminKalemTuru Tur, string Aciklama, decimal Tutar,
    int? CekId = null, bool Gecikmis = false);

/// <summary>Tahminin bir günü: o güne yazılan kalemler ve gün sonundaki tahmini kasa.</summary>
public record TahminGunu(DateOnly Tarih, decimal Giris, decimal Cikis, decimal Kasa, IReadOnlyList<TahminKalemi> Kalemler);

/// <summary>
/// Gün gün nakit tahmini. <see cref="Gunler"/>[0] bugündür ve kasası <see cref="BaslangicKasa"/>'dır
/// (panelin güncel kasası); sonraki her gün önceki günün kasası + o günün girişleri − çıkışlarıdır.
/// <see cref="HaricKalemler"/> kullanıcının hesaptan çıkardığı çeklerin, ufuk içindeki kalemleridir.
/// </summary>
public record NakitTahminSonucu(
    DateOnly Bugun,
    int Gun,
    decimal BaslangicKasa,
    IReadOnlyList<TahminGunu> Gunler,
    DateOnly EnDusukTarih,
    decimal EnDusukKasa,
    decimal SonKasa,
    decimal ToplamGiris,
    decimal ToplamCikis,
    IReadOnlyList<TahminKalemi> HaricKalemler);

/// <summary>
/// Önümüzdeki günlerin nakit tahmini (saf, yan etkisiz). Hesap motorunun ürettiği bugünkü kasadan
/// başlar ve yalnız bilinen/planlanmış hareketleri gün gün ekler; <see cref="HesapMotoru"/>'nun hiçbir
/// çıktısını değiştirmez ya da yeniden hesaplamaz. Gelecek gelenler (satış) bilinmediğinden tahmine girmez.
/// </summary>
public static class NakitTahmini
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>İzin verilen en uzun ufuk (gün).</summary>
    public const int EnFazlaGun = 366;

    /// <summary>
    /// Kalemleri günlere yazar ve kasayı zincirler.
    /// <list type="bullet">
    /// <item>0. gün bugündür: kasası <paramref name="baslangicKasa"/>, kalemi yoktur.</item>
    /// <item>Asıl günü bugün ya da daha önce olan (hâlâ bekleyen: vadesi gelmiş çek, ödenmemiş ekstre,
    /// onay bekleyen gider) kalemler 1. güne (yarın) yazılır; bugünden öncekiler <see cref="TahminKalemi.Gecikmis"/>.</item>
    /// <item>Ufuktan (bugün + <paramref name="gun"/>) sonraki ve tutarı sıfır olan kalemler atlanır.</item>
    /// <item>En düşük gün: kasası en düşük gün; eşitlikte en erken gün (0. gün dahil).</item>
    /// </list>
    /// Tutarlar kuruşa yuvarlanır (<see cref="Para.Yuvarla"/>).
    /// </summary>
    public static NakitTahminSonucu Hesapla(
        DateOnly bugun,
        decimal baslangicKasa,
        int gun,
        IEnumerable<TahminKalemi> kalemler,
        IEnumerable<TahminKalemi>? haricKalemler = null)
    {
        if (gun is < 1 or > EnFazlaGun)
            throw new ArgumentOutOfRangeException(nameof(gun), $"Gün sayısı 1 ile {EnFazlaGun} arasında olmalı.");
        var bitis = bugun.AddDays(gun);
        var kasa = Para.Yuvarla(baslangicKasa);

        var gunluk = Yerlestir(bugun, bitis, kalemler)
            .GroupBy(x => x.Gun)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TahminKalemi>)g.Select(x => x.Kalem).ToList());

        var gunler = new List<TahminGunu>(gun + 1) { new(bugun, 0m, 0m, kasa, Array.Empty<TahminKalemi>()) };
        var enDusuk = gunler[0];
        decimal toplamGiris = 0m, toplamCikis = 0m;
        for (var t = bugun.AddDays(1); t <= bitis; t = t.AddDays(1))
        {
            var liste = gunluk.GetValueOrDefault(t) ?? Array.Empty<TahminKalemi>();
            var giris = liste.Where(k => k.Tutar > 0).Sum(k => k.Tutar);
            var cikis = -liste.Where(k => k.Tutar < 0).Sum(k => k.Tutar);
            kasa += giris - cikis;
            toplamGiris += giris;
            toplamCikis += cikis;
            var g = new TahminGunu(t, giris, cikis, kasa, liste);
            gunler.Add(g);
            if (g.Kasa < enDusuk.Kasa) enDusuk = g;
        }

        var haric = Yerlestir(bugun, bitis, haricKalemler ?? Array.Empty<TahminKalemi>())
            .Select(x => x.Kalem).ToList();
        return new NakitTahminSonucu(bugun, gun, gunler[0].Kasa, gunler, enDusuk.Tarih, enDusuk.Kasa, kasa,
            toplamGiris, toplamCikis, haric);
    }

    // Kalemin yazılacağı gün (bekleyenler yarına) + yuvarlanmış kalem; ufuk dışı ve sıfır tutarlılar atlanır.
    private static IEnumerable<(DateOnly Gun, TahminKalemi Kalem)> Yerlestir(DateOnly bugun, DateOnly bitis, IEnumerable<TahminKalemi> kalemler)
    {
        var yarin = bugun.AddDays(1);
        return kalemler
            .Select(k => k with { Tutar = Para.Yuvarla(k.Tutar), Gecikmis = k.Tarih < bugun })
            .Where(k => k.Tutar != 0m)
            .Select(k => (Gun: k.Tarih <= bugun ? yarin : k.Tarih, Kalem: k))
            .Where(x => x.Gun <= bitis)
            .OrderBy(x => x.Gun).ThenBy(x => x.Kalem.Tarih).ThenBy(x => x.Kalem.Tur)
            .ThenBy(x => x.Kalem.Aciklama, StringComparer.Ordinal).ThenBy(x => x.Kalem.CekId ?? 0);
    }

    /// <summary>
    /// Bir kartın ufuk içindeki ödemeleri (kasadan çıkış). Kart hesabı <see cref="KartHesap.Durum"/>'daki
    /// gibidir; tahmin yalnız bilinen kayıtları kullanır:
    /// <list type="bullet">
    /// <item>Açık ekstre (<see cref="KartDonem.AcikEkstre"/>): kalan <paramref name="ekstreBorc"/>, son ödeme
    /// gününde. Son ödeme geçmiş ve borç kalmışsa kalem gecikmiştir (yarına yazılır).</item>
    /// <item>Sonraki ekstreler: bugünden sonraki ilk kesimde kesilecek borç = güncel borcun ekstre dışı kısmı
    /// (<paramref name="guncelBorc"/> − <paramref name="ekstreBorc"/>; negatifse karttaki alacak) + o kesime
    /// kadarki ileri tarihli harcamalar; daha sonraki kesimlerde yalnız aradaki ileri tarihli harcamalar.
    /// Alacak sonraki ekstreye devreder. Son ödemesi ufuktan sonra olan ekstrede durulur.</item>
    /// <item>İleri tarihli girilmiş kart ödemeleri kendi tarihinde çıkış olarak yazılır ve ekstreleri eskiden
    /// yeniye doğru kapatır (aynı borç iki kez düşülmez).</item>
    /// </list>
    /// </summary>
    /// <param name="ileriHarcamalar">Karta bağlı, tarihi bugünden sonra olan harcamalar (diğerleri yok sayılır).</param>
    /// <param name="ileriOdemeler">Tarihi bugünden sonra olan kart ödemeleri (diğerleri yok sayılır).</param>
    public static IReadOnlyList<TahminKalemi> KartOdemeleri(
        string kartAdi,
        int kesimGunu,
        int sonOdemeGunu,
        decimal ekstreBorc,
        decimal guncelBorc,
        IEnumerable<KartHarcama> ileriHarcamalar,
        IEnumerable<KartOdeme> ileriOdemeler,
        DateOnly bugun,
        DateOnly bitis)
    {
        var sonuc = new List<TahminKalemi>();
        ekstreBorc = Math.Max(0m, Para.Yuvarla(ekstreBorc));
        guncelBorc = Para.Yuvarla(guncelBorc);

        // Ekstreler: (son ödeme, kalan borç), eskiden yeniye.
        var ekstreler = new List<(DateOnly SonOdeme, decimal Kalan)>();
        var acik = KartDonem.AcikEkstre(kesimGunu, sonOdemeGunu, bugun);
        ekstreler.Add((acik.SonOdeme, ekstreBorc));

        var harcamalar = ileriHarcamalar.Where(h => h.Tarih > bugun)
            .Select(h => (h.Tarih, Tutar: Para.Yuvarla(h.Tutar))).OrderBy(h => h.Tarih).ToList();
        var devreden = guncelBorc - ekstreBorc;
        var oncekiKesim = bugun;
        for (var kesim = SonrakiGun(kesimGunu, bugun); ; kesim = SonrakiGun(kesimGunu, kesim))
        {
            var sonOdeme = KartDonem.SonOdeme(kesim, sonOdemeGunu);
            if (sonOdeme > bitis) break;
            var borc = devreden + harcamalar.Where(h => h.Tarih > oncekiKesim && h.Tarih <= kesim).Sum(h => h.Tutar);
            if (borc > 0m) { ekstreler.Add((sonOdeme, borc)); devreden = 0m; }
            else devreden = borc;   // alacak (fazla ödeme) sonraki ekstreye devreder
            oncekiKesim = kesim;
        }

        // Planlanmış (ileri tarihli) ödemeler: tarihinde çıkış; ekstreleri eskiden yeniye kapatır.
        foreach (var o in ileriOdemeler.Where(o => o.Tarih > bugun).OrderBy(o => o.Tarih))
        {
            var tutar = Para.Yuvarla(o.Tutar);
            if (o.Tarih <= bitis)
                sonuc.Add(new TahminKalemi(o.Tarih, TahminKalemTuru.KartOdemesi, $"{kartAdi} · planlanmış kart ödemesi", -tutar));
            var kalan = tutar;
            for (int i = 0; i < ekstreler.Count && kalan > 0m; i++)
            {
                var dus = Math.Min(kalan, ekstreler[i].Kalan);
                ekstreler[i] = (ekstreler[i].SonOdeme, ekstreler[i].Kalan - dus);
                kalan -= dus;
            }
        }

        foreach (var (sonOdeme, kalan) in ekstreler)
            if (kalan > 0m && sonOdeme <= bitis)
                sonuc.Add(new TahminKalemi(sonOdeme, TahminKalemTuru.KartOdemesi, $"{kartAdi} · ekstre ödemesi", -kalan));
        return sonuc;
    }

    /// <summary>
    /// Karta bağlı olmayan eski K.K harcamaları: <see cref="HesapMotoru"/>'ndaki gibi bir ayın toplamı
    /// sonraki ayın SON döneminde kasadan çıkar. Tahminde o dönemin ilk günü
    /// (<see cref="AyinSonDonemBaslangici"/>) bugünden sonra ve ufuk içindeyse çıkış yazılır; bugün ya da
    /// önceyse düşüm bugünkü kasaya zaten girmiştir.
    /// </summary>
    public static IReadOnlyList<TahminKalemi> KartsizKrediKartiDusumleri(IEnumerable<Islem> islemler, DateOnly bugun, DateOnly bitis)
    {
        var sonuc = new List<TahminKalemi>();
        var aylik = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti && i.KrediKartiId is null)
            .GroupBy(i => (i.Tarih.Year, i.Tarih.Month))
            .Select(g => (Ay: new DateOnly(g.Key.Year, g.Key.Month, 1), Toplam: g.Sum(i => Para.Yuvarla(i.TutarTl))))
            .OrderBy(x => x.Ay);
        foreach (var (ay, toplam) in aylik)
        {
            var sonraki = ay.AddMonths(1);
            var gun = AyinSonDonemBaslangici(sonraki.Year, sonraki.Month);
            if (toplam == 0m || gun <= bugun || gun > bitis) continue;
            sonuc.Add(new TahminKalemi(gun, TahminKalemTuru.KartsizKrediKarti,
                $"Karta bağlı olmayan kredi kartı harcamaları ({ay.ToString("MMMM yyyy", Tr)})", -toplam));
        }
        return sonuc;
    }

    /// <summary>
    /// Ayın son döneminin (ayın son gününü içeren dönem, bkz. <see cref="DonemUretici"/>) ilk günü:
    /// son günün haftasının Pazartesi'si; ay o haftanın içinde başlıyorsa ayın 1'i.
    /// </summary>
    public static DateOnly AyinSonDonemBaslangici(int yil, int ay)
    {
        var aySonu = new DateOnly(yil, ay, DateTime.DaysInMonth(yil, ay));
        var pazartesi = aySonu.AddDays(-(((int)aySonu.DayOfWeek + 6) % 7));
        var ayBasi = new DateOnly(yil, ay, 1);
        return pazartesi > ayBasi ? pazartesi : ayBasi;
    }

    /// <summary>
    /// <paramref name="gun"/> günlü (kısa ayda ay sonuna kırpılır), <paramref name="referans"/>'tan
    /// KESİNLİKLE sonraki ilk tarih (kesim günü 31 → 30 Nisan, 28 Şubat…).
    /// </summary>
    public static DateOnly SonrakiGun(int gun, DateOnly referans) => KartDonem.SonOdeme(referans, gun);
}
