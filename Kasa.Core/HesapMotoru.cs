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
    /// İşlemin hesapta kullanılan tipi: karta bağlı (KrediKartiId dolu) işlem, kayıtlı tipi ne olursa
    /// olsun K.K sayılır — kasadan harcamayla değil, kart ödemesiyle çıkar (Cari+kart çift düşülmez).
    /// </summary>
    public static GiderTipi EtkinTip(Islem i) => i.KrediKartiId is not null ? GiderTipi.KrediKarti : i.Tip;

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
    /// Tutarlar kuruşa yuvarlanarak (<see cref="Para.Yuvarla"/>) işlenir.
    /// Karmaşıklık: işlem/gelen/ödeme tarihe göre bir kez sıralanır, her dönemin kalemleri ikili
    /// aramayla bulunur — O(n log n + dönem × kanal).
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
        var kanalDevir = kanallar.ToDictionary(k => k.Ad, k => k.AcilisDevri);
        var kasaDevir = kasaAcilisDevri;
        var sonuc = new List<HaftalikOzet>(sirali.Count);

        // Kalemleri bir kez tarihe göre sırala (kararlı sıralama); her dönem kendi aralığını
        // ikili aramayla bulur. Dönemler çakışsa bile her dönem [Start, End] içindeki tüm
        // kalemleri görür (eski "her dönem tüm listeyi tara" davranışıyla aynı sonuç).
        var islemSirali = islemler
            .Select(i => (i.Tarih, i.Kanal, Tip: EtkinTip(i), Tutar: Para.Yuvarla(i.TutarTl)))
            .OrderBy(x => x.Tarih).ToArray();
        var islemTarih = Array.ConvertAll(islemSirali, x => x.Tarih);
        var gelenSirali = gelenler
            .Select(g => (Tarih: g.DonemStart, g.Kanal, Tutar: Para.Yuvarla(g.TutarTl)))
            .OrderBy(x => x.Tarih).ToArray();
        var gelenTarih = Array.ConvertAll(gelenSirali, x => x.Tarih);
        var odemeSirali = (kartOdemeleri ?? Array.Empty<KartOdeme>())
            .Select(o => (o.Tarih, Tutar: Para.Yuvarla(o.Tutar)))
            .OrderBy(x => x.Tarih).ToArray();
        var odemeTarih = Array.ConvertAll(odemeSirali, x => x.Tarih);

        // Kredi kartı ertelemesi (kasa): bir ayın K.K'sı o ay kasadan çıkmaz;
        // ödemesi bir SONRAKİ ayın SON döneminde toplu olarak kasadan çıkar.
        // Karta bağlı harcamalar burada yok: onlar kart ödemesiyle kasadan çıkar.
        var aylikKkToplam = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti && i.KrediKartiId is null)
            .GroupBy(i => (i.Tarih.Year, i.Tarih.Month))
            .ToDictionary(g => g.Key, g => g.Sum(i => Para.Yuvarla(i.TutarTl)));

        // Yalnız ayın son gününü içeren dönem sayılır; içinde bulunulan ayın bugüne kadar
        // üretilmiş son (kısmi) dönemi değil. Böylece ertelenen K.K her hafta kaymaz.
        var ayinSonDonemi = sirali
            .Where(DonemUretici.AyinSonDonemiMi)
            .GroupBy(d => (d.Yil, d.Ay))
            .ToDictionary(g => g.Key, g => g.Last());

        var kanalGelen = new Dictionary<string, decimal>();
        var kanalCari = new Dictionary<string, decimal>();
        foreach (var donem in sirali)
        {
            kanalGelen.Clear();
            kanalCari.Clear();

            decimal toplamGelen = 0m;
            var (gBas, gSon) = Aralik(gelenTarih, donem);
            for (int j = gBas; j < gSon; j++)
            {
                var g = gelenSirali[j];
                toplamGelen += g.Tutar;
                kanalGelen[g.Kanal] = kanalGelen.GetValueOrDefault(g.Kanal) + g.Tutar;
            }

            // KK kendi döneminde kasadan çıkmaz (ertelenir / kart ödemesiyle çıkar).
            decimal toplamGiden = 0m;
            var (iBas, iSon) = Aralik(islemTarih, donem);
            for (int j = iBas; j < iSon; j++)
            {
                var i = islemSirali[j];
                if (i.Tip == GiderTipi.KrediKarti) continue;
                toplamGiden += i.Tutar;
                if (i.Tip == GiderTipi.Cari)
                    kanalCari[i.Kanal] = kanalCari.GetValueOrDefault(i.Kanal) + i.Tutar;
            }

            var kanalSatirlari = new List<KanalHaftalik>(kanallar.Count);
            foreach (var kanal in kanallar)
            {
                decimal gelen = kanalGelen.GetValueOrDefault(kanal.Ad);
                decimal gidenCari = kanalCari.GetValueOrDefault(kanal.Ad);
                decimal kanalSonuc = gelen - gidenCari;
                kanalDevir[kanal.Ad] += kanalSonuc;
                kanalSatirlari.Add(new KanalHaftalik(kanal.Ad, gelen, gidenCari, kanalSonuc, kanalDevir[kanal.Ad]));
            }

            // Bu dönem ayının SON dönemiyse: bir önceki ayın KK'sı şimdi kasadan çıkar.
            if (ayinSonDonemi.TryGetValue((donem.Yil, donem.Ay), out var sonDonem) && sonDonem == donem)
            {
                int oncekiYil = donem.Ay == 1 ? donem.Yil - 1 : donem.Yil;
                int oncekiAy = donem.Ay == 1 ? 12 : donem.Ay - 1;
                if (aylikKkToplam.TryGetValue((oncekiYil, oncekiAy), out var ertelenenKk))
                    toplamGiden += ertelenenKk;
            }
            // Kart borç ödemeleri, ödendikleri dönemde kasadan çıkar.
            var (oBas, oSon) = Aralik(odemeTarih, donem);
            for (int j = oBas; j < oSon; j++) toplamGiden += odemeSirali[j].Tutar;

            decimal kasaSonucu = toplamGelen - toplamGiden;
            kasaDevir += kasaSonucu;

            sonuc.Add(new HaftalikOzet(donem, kanalSatirlari, toplamGelen, toplamGiden, kasaSonucu, kasaDevir));
        }
        return sonuc;
    }

    // Sıralı tarih dizisinde [donem.Start, donem.End] aralığının [bas, son) indeksleri.
    private static (int Bas, int Son) Aralik(DateOnly[] tarihler, Donem donem)
    {
        if (donem.End < donem.Start) return (0, 0);
        return (AltSinir(tarihler, donem.Start), AltSinir(tarihler, donem.End.AddDays(1)));
    }

    // İlk tarihler[i] >= hedef olan indeks (yoksa Length).
    private static int AltSinir(DateOnly[] tarihler, DateOnly hedef)
    {
        int lo = 0, hi = tarihler.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (tarihler[mid] < hedef) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// Bir takvim ayı için kanal başına AY SONUCU üretir.
    /// <list type="bullet">
    /// <item>Aylık gelen = o aya düşen dönemlerin geleni.</item>
    /// <item>Takip başlangıcından (<paramref name="takipBaslangic"/>; verilmezse en erken dönemin
    /// başlangıcı) önceki işlemler, gelenler gibi, sayılmaz — haftalık raporla tutarlı. Takipten önce
    /// biten bir ay için önceki ayın K.K'sı da düşülmez (haftalıkta o ayın son dönemi yok).</item>
    /// <item>Ortak giderler (Kanallar.Ortak) kuruş bazında (<see cref="Para.KurusBol"/>) şu kanallara
    /// dağıtılır: o ay hareketi (gelen, gider ya da o aya düşen K.K) olan kanallar; hiç yoksa
    /// aktif kanallar; o da yoksa tüm kanallar. Böylece bir kanalın bugünkü Aktif bayrağı geçmiş
    /// ayları değiştirmez ve aktif kanal kalmasa da Ortak gider kaybolmaz. Artık kuruşlar kanal
    /// sırasına göre ilk kanallara verilir.</item>
    /// </list>
    /// Tutarlar kuruşa yuvarlanarak (<see cref="Para.Yuvarla"/>) işlenir.
    /// </summary>
    public static AylikRapor AylikHesapla(
        int yil,
        int ay,
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler,
        DateOnly? takipBaslangic = null)
    {
        var ayinDonemleri = donemler.Where(d => d.Yil == yil && d.Ay == ay).ToList();
        var baslangic = takipBaslangic ?? (donemler.Count > 0 ? donemler.Min(d => d.Start) : (DateOnly?)null);
        var aySonu = new DateOnly(yil, ay, DateTime.DaysInMonth(yil, ay));

        var ayinIslemleri = islemler
            .Where(i => i.Tarih.Year == yil && i.Tarih.Month == ay && (baslangic is null || i.Tarih >= baslangic))
            .ToList();

        // Kredi kartı ertelemesi: bu ayın K.K'sı bu ay DÜŞÜLMEZ; ödemesi gelecek ay
        // yapıldığı için bir ÖNCEKİ ayın K.K'sı bu ayın sonucundan düşülür.
        // (Haftalıktaki gibi: önceki ayın K.K'sı, takip öncesine düşse de, takibin ilk ayında düşer.)
        int oncekiYil = ay == 1 ? yil - 1 : yil;
        int oncekiAy = ay == 1 ? 12 : ay - 1;
        var oncekiAyKk = baslangic is not null && aySonu < baslangic
            ? new List<Islem>()
            : islemler
                .Where(i => i.Tarih.Year == oncekiYil && i.Tarih.Month == oncekiAy
                            && EtkinTip(i) == GiderTipi.KrediKarti)
                .ToList();

        // Ortak gider: bu ayın K.K dışı Ortak giderleri + geçen ayın Ortak K.K'sı
        // (haftalık kasadaki ertelemeyle aynı kural).
        decimal ortakToplam =
            ayinIslemleri.Where(i => i.Kanal == Kanallar.Ortak && EtkinTip(i) != GiderTipi.KrediKarti).Sum(i => Para.Yuvarla(i.TutarTl))
            + oncekiAyKk.Where(i => i.Kanal == Kanallar.Ortak).Sum(i => Para.Yuvarla(i.TutarTl));

        var satirlar = new List<(string Kanal, decimal Gelen, decimal Cari, decimal Sabit, decimal Kk, bool Hareketli)>();
        foreach (var kanal in kanallar)
        {
            var kanalGelenleri = gelenler
                .Where(g => g.Kanal == kanal.Ad && ayinDonemleri.Any(d => d.Icerir(g.DonemStart)))
                .ToList();
            decimal gelen = kanalGelenleri.Sum(g => Para.Yuvarla(g.TutarTl));
            decimal cari = ayinIslemleri.Where(i => i.Kanal == kanal.Ad && EtkinTip(i) == GiderTipi.Cari).Sum(i => Para.Yuvarla(i.TutarTl));
            decimal sabit = ayinIslemleri.Where(i => i.Kanal == kanal.Ad && EtkinTip(i) == GiderTipi.SabitGider).Sum(i => Para.Yuvarla(i.TutarTl));
            decimal kk = oncekiAyKk.Where(i => i.Kanal == kanal.Ad).Sum(i => Para.Yuvarla(i.TutarTl));
            bool hareketli = kanalGelenleri.Any(g => g.TutarTl != 0m)
                             || ayinIslemleri.Any(i => i.Kanal == kanal.Ad && i.TutarTl != 0m)
                             || kk != 0m;
            satirlar.Add((kanal.Ad, gelen, cari, sabit, kk, hareketli));
        }

        // Ortak payı alacak kanallar: o ay hareketli → aktif → tümü.
        var payAlanlar = kanallar.Where((k, idx) => satirlar[idx].Hareketli).Select(k => k.Ad).ToList();
        if (payAlanlar.Count == 0) payAlanlar = kanallar.Where(k => k.Aktif).Select(k => k.Ad).ToList();
        if (payAlanlar.Count == 0) payAlanlar = kanallar.Select(k => k.Ad).ToList();

        var ortakPaylari = new Dictionary<string, decimal>();
        if (payAlanlar.Count > 0)
        {
            var paylar = Para.KurusBol(ortakToplam, payAlanlar.Count);
            for (int i = 0; i < payAlanlar.Count; i++)
                ortakPaylari[payAlanlar[i]] = ortakPaylari.GetValueOrDefault(payAlanlar[i]) + paylar[i];
        }

        var sonuc = new List<KanalAylik>(satirlar.Count);
        foreach (var s in satirlar)
        {
            decimal ortakPay = ortakPaylari.GetValueOrDefault(s.Kanal, 0m);
            decimal aySonucu = s.Gelen - s.Cari - s.Sabit - s.Kk - ortakPay;
            sonuc.Add(new KanalAylik(s.Kanal, s.Gelen, s.Cari, s.Sabit, s.Kk, ortakPay, aySonucu));
        }
        return new AylikRapor(yil, ay, sonuc);
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
