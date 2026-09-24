namespace Kasa.Core;

public record KanalHaftalik(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir);

public record HaftalikOzet(
    Donem Donem,
    IReadOnlyList<KanalHaftalik> Kanallar,
    decimal ToplamGelen,
    decimal ToplamGiden,
    decimal KasaSonucu,
    decimal KasaDevir);

public static class HesapMotoru
{
    /// <summary>
    /// Dönem dönem haftalık özet üretir. Kanal devri yalnız o kanalın Cari tipli
    /// gidenini sayar. Kasa devri Cari + SabitGider + Ortak gidenleri kendi döneminde
    /// sayar. Kredi kartı: bir karta bağlı harcama kasadan harcamayla değil, o kartın
    /// gerçek ödemesiyle (<paramref name="kartOdemeleri"/>, ödeme tarihinin döneminde) çıkar.
    /// Karta bağlı olmayan K.K harcaması ertelenir — bir sonraki ayın son döneminde çıkar.
    /// "Son dönem" ayın son gününü içeren dönemdir; içinde bulunulan ay henüz o haftaya
    /// gelmediyse ertelenen K.K bekler (haftadan haftaya kaymaz).
    /// Gelen, DonemStart'ı hangi dönemin içine düşüyorsa o döneme sayılır.
    /// Devirler tarih sırasına göre zincirlenir; ilk dönemin girişi açılış bakiyeleridir.
    /// </summary>
    public static IReadOnlyList<HaftalikOzet> HaftalikHesapla(
        decimal kasaAcilisDevri,
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler,
        IReadOnlyList<KartOdeme>? kartOdemeleri = null)
    {
        var sirali = donemler.OrderBy(d => d.Start).ToList();
        var odemeler = kartOdemeleri ?? Array.Empty<KartOdeme>();
        var kanalDevir = kanallar.ToDictionary(k => k.Ad, k => k.AcilisDevri);
        var kasaDevir = kasaAcilisDevri;
        var sonuc = new List<HaftalikOzet>();

        // Kredi kartı ertelemesi (kasa): bir ayın K.K'sı o ay kasadan çıkmaz;
        // ödemesi bir SONRAKİ ayın SON döneminde toplu olarak kasadan çıkar.
        // Karta bağlı harcamalar burada yok: onlar kart ödemesiyle kasadan çıkar.
        var aylikKkToplam = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti && i.KrediKartiId is null)
            .GroupBy(i => (i.Tarih.Year, i.Tarih.Month))
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TutarTl));

        // Yalnız ayın son gününü içeren dönem sayılır; içinde bulunulan ayın bugüne kadar
        // üretilmiş son (kısmi) dönemi değil. Böylece ertelenen K.K her hafta kaymaz.
        var ayinSonDonemi = sirali
            .Where(DonemUretici.AyinSonDonemiMi)
            .GroupBy(d => (d.Yil, d.Ay))
            .ToDictionary(g => g.Key, g => g.Last());

        foreach (var donem in sirali)
        {
            var donemIslem = islemler.Where(i => donem.Icerir(i.Tarih)).ToList();
            var donemGelen = gelenler.Where(g => donem.Icerir(g.DonemStart)).ToList();

            var kanalSatirlari = new List<KanalHaftalik>();
            foreach (var kanal in kanallar)
            {
                decimal gelen = donemGelen.Where(g => g.Kanal == kanal.Ad).Sum(g => g.TutarTl);
                decimal gidenCari = donemIslem
                    .Where(i => i.Kanal == kanal.Ad && i.Tip == GiderTipi.Cari)
                    .Sum(i => i.TutarTl);
                decimal kanalSonuc = gelen - gidenCari;
                kanalDevir[kanal.Ad] += kanalSonuc;
                kanalSatirlari.Add(new KanalHaftalik(kanal.Ad, gelen, gidenCari, kanalSonuc, kanalDevir[kanal.Ad]));
            }

            decimal toplamGelen = donemGelen.Sum(g => g.TutarTl);
            // KK kendi döneminde kasadan çıkmaz (ertelenir).
            decimal toplamGiden = donemIslem.Where(i => i.Tip != GiderTipi.KrediKarti).Sum(i => i.TutarTl);
            // Bu dönem ayının SON dönemiyse: bir önceki ayın KK'sı şimdi kasadan çıkar.
            if (ayinSonDonemi.TryGetValue((donem.Yil, donem.Ay), out var sonDonem) && sonDonem == donem)
            {
                int oncekiYil = donem.Ay == 1 ? donem.Yil - 1 : donem.Yil;
                int oncekiAy = donem.Ay == 1 ? 12 : donem.Ay - 1;
                if (aylikKkToplam.TryGetValue((oncekiYil, oncekiAy), out var ertelenenKk))
                    toplamGiden += ertelenenKk;
            }
            // Kart borç ödemeleri, ödendikleri dönemde kasadan çıkar.
            toplamGiden += odemeler.Where(o => donem.Icerir(o.Tarih)).Sum(o => o.Tutar);
            decimal kasaSonucu = toplamGelen - toplamGiden;
            kasaDevir += kasaSonucu;

            sonuc.Add(new HaftalikOzet(donem, kanalSatirlari, toplamGelen, toplamGiden, kasaSonucu, kasaDevir));
        }
        return sonuc;
    }

    /// <summary>
    /// Bir takvim ayı için kanal başına AY SONUCU üretir.
    /// Aylık gelen = o aya düşen dönemlerin geleni. Ortak giderler (Kanallar.Ortak)
    /// aktif kanallara kuruş bazında (artık kuruşlar ilk aktif kanallara) dağıtılıp düşülür.
    /// </summary>
    public static AylikRapor AylikHesapla(
        int yil,
        int ay,
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler)
    {
        var ayinDonemleri = donemler.Where(d => d.Yil == yil && d.Ay == ay).ToList();
        var ayinIslemleri = islemler.Where(i => i.Tarih.Year == yil && i.Tarih.Month == ay).ToList();
        int aktifKanalSayisi = kanallar.Count(k => k.Aktif);

        // Kredi kartı ertelemesi: bu ayın K.K'sı bu ay DÜŞÜLMEZ; ödemesi gelecek ay
        // yapıldığı için bir ÖNCEKİ ayın K.K'sı bu ayın sonucundan düşülür.
        int oncekiYil = ay == 1 ? yil - 1 : yil;
        int oncekiAy = ay == 1 ? 12 : ay - 1;
        var oncekiAyKk = islemler
            .Where(i => i.Tarih.Year == oncekiYil && i.Tarih.Month == oncekiAy
                        && i.Tip == GiderTipi.KrediKarti)
            .ToList();

        // Ortak gider: bu ayın K.K dışı Ortak giderleri + geçen ayın Ortak K.K'sı
        // (haftalık kasadaki ertelemeyle aynı kural).
        decimal ortakToplam =
            ayinIslemleri.Where(i => i.Kanal == Kanallar.Ortak && i.Tip != GiderTipi.KrediKarti).Sum(i => i.TutarTl)
            + oncekiAyKk.Where(i => i.Kanal == Kanallar.Ortak).Sum(i => i.TutarTl);

        // Ortak gideri aktif kanallara kuruş bazında dağıt. Tam bölünmediğinde
        // artık kuruş(ları) ilk aktif kanallara (+0,01) verip toplam tam mutabık kalsın.
        var ortakPaylari = new Dictionary<string, decimal>();
        if (aktifKanalSayisi > 0)
        {
            long toplamKurus = (long)decimal.Round(ortakToplam * 100m, 0, MidpointRounding.AwayFromZero);
            long tabanKurus = toplamKurus / aktifKanalSayisi;
            long artanKurus = toplamKurus - tabanKurus * aktifKanalSayisi;
            int aktifIndex = 0;
            foreach (var kanal in kanallar.Where(k => k.Aktif))
            {
                long payKurus = tabanKurus + (aktifIndex < artanKurus ? 1 : 0);
                ortakPaylari[kanal.Ad] = payKurus / 100m;
                aktifIndex++;
            }
        }

        var satirlar = new List<KanalAylik>();
        foreach (var kanal in kanallar)
        {
            decimal gelen = gelenler
                .Where(g => g.Kanal == kanal.Ad && ayinDonemleri.Any(d => d.Icerir(g.DonemStart)))
                .Sum(g => g.TutarTl);
            decimal cari = ayinIslemleri.Where(i => i.Kanal == kanal.Ad && i.Tip == GiderTipi.Cari).Sum(i => i.TutarTl);
            decimal sabit = ayinIslemleri.Where(i => i.Kanal == kanal.Ad && i.Tip == GiderTipi.SabitGider).Sum(i => i.TutarTl);
            decimal kk = oncekiAyKk.Where(i => i.Kanal == kanal.Ad).Sum(i => i.TutarTl);
            decimal ortakPay = ortakPaylari.GetValueOrDefault(kanal.Ad, 0m);
            decimal aySonucu = gelen - cari - sabit - kk - ortakPay;
            satirlar.Add(new KanalAylik(kanal.Ad, gelen, cari, sabit, kk, ortakPay, aySonucu));
        }
        return new AylikRapor(yil, ay, satirlar);
    }
}

public record KanalAylik(
    string Kanal,
    decimal Gelen,
    decimal CariGiden,
    decimal SabitGider,
    decimal KrediKarti,
    decimal OrtakPay,
    decimal AySonucu);

public record AylikRapor(int Yil, int Ay, IReadOnlyList<KanalAylik> Kanallar);
