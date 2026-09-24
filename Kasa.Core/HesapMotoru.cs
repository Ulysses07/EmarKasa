namespace Kasa.Core;

/// <summary>
/// Kanalın dönem satırı. <see cref="Gelen"/>/<see cref="Giden"/> yalnız gelen ve Cari işlemlerdir;
/// çek tahsilatı/ödemesi ayrıca <see cref="CekGelen"/>/<see cref="CekGiden"/>'de görünür.
/// Sonuc = Gelen + CekGelen − Giden − CekGiden; Devir bu sonuçla zincirlenir.
/// </summary>
public record KanalHaftalik(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir,
    decimal CekGelen = 0m, decimal CekGiden = 0m);

/// <summary>
/// Dönemin kasa özeti. <see cref="ToplamGelen"/>/<see cref="ToplamGiden"/> çek hariçtir; çek
/// tahsilatı/ödemesi <see cref="ToplamCekGelen"/>/<see cref="ToplamCekGiden"/>'dedir.
/// KasaSonucu = ToplamGelen + ToplamCekGelen − ToplamGiden − ToplamCekGiden.
/// </summary>
public record HaftalikOzet(
    Donem Donem,
    IReadOnlyList<KanalHaftalik> Kanallar,
    decimal ToplamGelen,
    decimal ToplamGiden,
    decimal KasaSonucu,
    decimal KasaDevir,
    decimal ToplamCekGelen = 0m,
    decimal ToplamCekGiden = 0m)
{
    /// <summary>
    /// Kasa sonucunu oluşturan kalemler (<see cref="KasaKalemi"/>, işaretli): Σ Tutar = KasaSonucu.
    /// Mevcut hiçbir rakamı değiştirmez; yalnız "kasa neden değişti?" dökümü için yanında hesaplanır.
    /// Rapor JSON'una girmez (haftalık raporun biçimi aynı kalır).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<KasaKalemi> Kalemler { get; init; } = Array.Empty<KasaKalemi>();
}

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
    /// Çekler (<paramref name="cekler"/>, <see cref="CekKurali"/>) yalnız işlem tarihinin döneminde
    /// etkilidir: tahsil edilen alınan çek o kanalın ek geleni, ödenen verilen çek o kanalın (Ortak
    /// ise yalnız kasanın) Cari gideri gibi sayılır; tutarları ayrı alanlarda (CekGelen/CekGiden) görünür.
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
        IReadOnlyList<KartOdeme>? kartOdemeleri = null,
        IReadOnlyList<Cek>? cekler = null)
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
        var cekSirali = CekKurali.Hareketler(cekler).OrderBy(h => h.Tarih).ToArray();
        var cekTarih = Array.ConvertAll(cekSirali, h => h.Tarih);

        // Kredi kartı ertelemesi (kasa): bir ayın K.K'sı o ay kasadan çıkmaz;
        // ödemesi bir SONRAKİ ayın SON döneminde toplu olarak kasadan çıkar.
        // Karta bağlı harcamalar burada yok: onlar kart ödemesiyle kasadan çıkar.
        var aylikKkToplam = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti && i.KrediKartiId is null)
            .GroupBy(i => (i.Tarih.Year, i.Tarih.Month))
            .ToDictionary(g => g.Key, g => g.Sum(i => Para.Yuvarla(i.TutarTl)));
        // Aynı ertelemenin kanal kırılımı (yalnız kasa dökümü için; toplamı aylikKkToplam'dır).
        var aylikKkKanal = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti && i.KrediKartiId is null)
            .GroupBy(i => (i.Tarih.Year, i.Tarih.Month))
            .ToDictionary(g => g.Key, g => g.GroupBy(i => i.Kanal)
                .Select(k => (Kanal: k.Key, Tutar: k.Sum(i => Para.Yuvarla(i.TutarTl)))).ToList());

        // Yalnız ayın son gününü içeren dönem sayılır; içinde bulunulan ayın bugüne kadar
        // üretilmiş son (kısmi) dönemi değil. Böylece ertelenen K.K her hafta kaymaz.
        var ayinSonDonemi = sirali
            .Where(DonemUretici.AyinSonDonemiMi)
            .GroupBy(d => (d.Yil, d.Ay))
            .ToDictionary(g => g.Key, g => g.Last());

        var kanalGelen = new Dictionary<string, decimal>();
        var kanalCari = new Dictionary<string, decimal>();
        var kanalCekGelen = new Dictionary<string, decimal>();
        var kanalCekGiden = new Dictionary<string, decimal>();
        // Kasa dökümü (yalnız bilgi): (tür, kanal) başına işaretli toplam.
        var kalemler = new Dictionary<(KasaKalemTuru Tur, string? Kanal), decimal>();
        void KalemEkle(KasaKalemTuru tur, string? kanal, decimal tutar)
            => kalemler[(tur, kanal)] = kalemler.GetValueOrDefault((tur, kanal)) + tutar;
        foreach (var donem in sirali)
        {
            kanalGelen.Clear();
            kanalCari.Clear();
            kanalCekGelen.Clear();
            kanalCekGiden.Clear();
            kalemler.Clear();

            decimal toplamGelen = 0m;
            var (gBas, gSon) = Aralik(gelenTarih, donem);
            for (int j = gBas; j < gSon; j++)
            {
                var g = gelenSirali[j];
                toplamGelen += g.Tutar;
                kanalGelen[g.Kanal] = kanalGelen.GetValueOrDefault(g.Kanal) + g.Tutar;
                KalemEkle(KasaKalemTuru.Gelen, g.Kanal, g.Tutar);
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
                KalemEkle(i.Kanal == Kanallar.Ortak ? KasaKalemTuru.OrtakGider
                    : i.Tip == GiderTipi.Cari ? KasaKalemTuru.CariGider : KasaKalemTuru.SabitGider, i.Kanal, -i.Tutar);
            }

            // Çek: tahsilat kanalın ek geleni, ödeme kanalın Cari gideri gibi (Ortak ödeme yalnız kasadan
            // düşer — Ortak Cari gider gibi hiçbir kanal satırına yazılmaz).
            decimal toplamCekGelen = 0m, toplamCekGiden = 0m;
            var (cBas, cSon) = Aralik(cekTarih, donem);
            for (int j = cBas; j < cSon; j++)
            {
                var h = cekSirali[j];
                if (h.Yon == CekYonu.Alinan)
                {
                    toplamCekGelen += h.Tutar;
                    kanalCekGelen[h.Kanal] = kanalCekGelen.GetValueOrDefault(h.Kanal) + h.Tutar;
                    KalemEkle(KasaKalemTuru.CekTahsilat, h.Kanal, h.Tutar);
                }
                else
                {
                    toplamCekGiden += h.Tutar;
                    kanalCekGiden[h.Kanal] = kanalCekGiden.GetValueOrDefault(h.Kanal) + h.Tutar;
                    KalemEkle(KasaKalemTuru.CekOdemesi, h.Kanal, -h.Tutar);
                }
            }

            var kanalSatirlari = new List<KanalHaftalik>(kanallar.Count);
            foreach (var kanal in kanallar)
            {
                decimal gelen = kanalGelen.GetValueOrDefault(kanal.Ad);
                decimal gidenCari = kanalCari.GetValueOrDefault(kanal.Ad);
                decimal cekGelen = kanalCekGelen.GetValueOrDefault(kanal.Ad);
                decimal cekGiden = kanalCekGiden.GetValueOrDefault(kanal.Ad);
                decimal kanalSonuc = gelen - gidenCari + cekGelen - cekGiden;
                kanalDevir[kanal.Ad] += kanalSonuc;
                kanalSatirlari.Add(new KanalHaftalik(kanal.Ad, gelen, gidenCari, kanalSonuc, kanalDevir[kanal.Ad], cekGelen, cekGiden));
            }

            // Bu dönem ayının SON dönemiyse: bir önceki ayın KK'sı şimdi kasadan çıkar.
            if (ayinSonDonemi.TryGetValue((donem.Yil, donem.Ay), out var sonDonem) && sonDonem == donem)
            {
                int oncekiYil = donem.Ay == 1 ? donem.Yil - 1 : donem.Yil;
                int oncekiAy = donem.Ay == 1 ? 12 : donem.Ay - 1;
                if (aylikKkToplam.TryGetValue((oncekiYil, oncekiAy), out var ertelenenKk))
                {
                    toplamGiden += ertelenenKk;
                    foreach (var (kkKanal, kkTutar) in aylikKkKanal[(oncekiYil, oncekiAy)])
                        KalemEkle(KasaKalemTuru.ErtelenenKk, kkKanal, -kkTutar);
                }
            }
            // Kart borç ödemeleri, ödendikleri dönemde kasadan çıkar.
            var (oBas, oSon) = Aralik(odemeTarih, donem);
            for (int j = oBas; j < oSon; j++)
            {
                toplamGiden += odemeSirali[j].Tutar;
                KalemEkle(KasaKalemTuru.KartOdemesi, null, -odemeSirali[j].Tutar);
            }

            decimal kasaSonucu = toplamGelen - toplamGiden + toplamCekGelen - toplamCekGiden;
            kasaDevir += kasaSonucu;

            sonuc.Add(new HaftalikOzet(donem, kanalSatirlari, toplamGelen, toplamGiden, kasaSonucu, kasaDevir,
                toplamCekGelen, toplamCekGiden)
            {
                Kalemler = KasaDokumuHesap.Sirala(
                    kalemler.Where(x => x.Value != 0m).Select(x => new KasaKalemi(x.Key.Tur, x.Key.Kanal, x.Value)), kanallar),
            });
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
    /// <item>Çekler (<paramref name="cekler"/>, <see cref="CekKurali"/>): tahsil edilen alınan çek,
    /// işlem tarihi ayın bir dönemine düşüyorsa o kanalın geleni gibi; ödenen verilen çek, işlem
    /// tarihi bu aydaysa (takipten önce değilse) o kanalın Cari gideri gibi sayılır ve kanalı
    /// "hareketli" yapar. Ortak çek ödemesi Ortak Cari gider gibi Ortak giderlerle AYNI havuzda
    /// bölünür (ay sonucu birebir aynı çıkar); kanala düşen çek kısmı OrtakPay'e değil CekGiden'e
    /// yazılır. Böylece Gelen/CariGiden/OrtakPay çeksiz anlamını korur.</item>
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
        DateOnly? takipBaslangic = null,
        IReadOnlyList<Cek>? cekler = null)
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

        // Çek hareketleri: tahsilat gelen gibi (ayın dönemlerine düşen), ödeme Cari gider gibi (ay içinde,
        // takipten önce değil).
        var cekHareketleri = CekKurali.Hareketler(cekler);
        var ayinTahsilatlari = cekHareketleri
            .Where(h => h.Yon == CekYonu.Alinan && ayinDonemleri.Any(d => d.Icerir(h.Tarih)))
            .ToList();
        var ayinCekOdemeleri = cekHareketleri
            .Where(h => h.Yon == CekYonu.Verilen && h.Tarih.Year == yil && h.Tarih.Month == ay
                        && (baslangic is null || h.Tarih >= baslangic))
            .ToList();
        decimal ortakCek = ayinCekOdemeleri.Where(h => h.Kanal == Kanallar.Ortak).Sum(h => h.Tutar);

        var satirlar = new List<(string Kanal, decimal Gelen, decimal Cari, decimal Sabit, decimal Kk, decimal CekGelen, decimal CekGiden, bool Hareketli)>();
        foreach (var kanal in kanallar)
        {
            var kanalGelenleri = gelenler
                .Where(g => g.Kanal == kanal.Ad && ayinDonemleri.Any(d => d.Icerir(g.DonemStart)))
                .ToList();
            decimal gelen = kanalGelenleri.Sum(g => Para.Yuvarla(g.TutarTl));
            decimal cari = ayinIslemleri.Where(i => i.Kanal == kanal.Ad && EtkinTip(i) == GiderTipi.Cari).Sum(i => Para.Yuvarla(i.TutarTl));
            decimal sabit = ayinIslemleri.Where(i => i.Kanal == kanal.Ad && EtkinTip(i) == GiderTipi.SabitGider).Sum(i => Para.Yuvarla(i.TutarTl));
            decimal kk = oncekiAyKk.Where(i => i.Kanal == kanal.Ad).Sum(i => Para.Yuvarla(i.TutarTl));
            decimal cekGelen = ayinTahsilatlari.Where(h => h.Kanal == kanal.Ad).Sum(h => h.Tutar);
            decimal cekGiden = ayinCekOdemeleri.Where(h => h.Kanal == kanal.Ad).Sum(h => h.Tutar);
            bool hareketli = kanalGelenleri.Any(g => g.TutarTl != 0m)
                             || ayinIslemleri.Any(i => i.Kanal == kanal.Ad && i.TutarTl != 0m)
                             || kk != 0m
                             || ayinTahsilatlari.Any(h => h.Kanal == kanal.Ad && h.Tutar != 0m)
                             || ayinCekOdemeleri.Any(h => h.Kanal == kanal.Ad && h.Tutar != 0m);
            satirlar.Add((kanal.Ad, gelen, cari, sabit, kk, cekGelen, cekGiden, hareketli));
        }

        // Ortak payı alacak kanallar: o ay hareketli → aktif → tümü.
        var payAlanlar = kanallar.Where((k, idx) => satirlar[idx].Hareketli).Select(k => k.Ad).ToList();
        if (payAlanlar.Count == 0) payAlanlar = kanallar.Where(k => k.Aktif).Select(k => k.Ad).ToList();
        if (payAlanlar.Count == 0) payAlanlar = kanallar.Select(k => k.Ad).ToList();

        var ortakPaylari = new Dictionary<string, decimal>();
        var ortakCekPaylari = new Dictionary<string, decimal>();
        if (payAlanlar.Count > 0)
        {
            var paylar = Para.KurusBol(ortakToplam, payAlanlar.Count);
            // Ortak çek ödemesi Ortak Cari gider gibi aynı havuzda bölünür (kuruş dağılımı birebir aynı);
            // kanala düşen fark çek payıdır.
            var cekliPaylar = ortakCek == 0m ? paylar : Para.KurusBol(ortakToplam + ortakCek, payAlanlar.Count);
            for (int i = 0; i < payAlanlar.Count; i++)
            {
                ortakPaylari[payAlanlar[i]] = ortakPaylari.GetValueOrDefault(payAlanlar[i]) + paylar[i];
                ortakCekPaylari[payAlanlar[i]] = ortakCekPaylari.GetValueOrDefault(payAlanlar[i]) + (cekliPaylar[i] - paylar[i]);
            }
        }

        var sonuc = new List<KanalAylik>(satirlar.Count);
        foreach (var s in satirlar)
        {
            decimal ortakPay = ortakPaylari.GetValueOrDefault(s.Kanal, 0m);
            decimal cekGiden = s.CekGiden + ortakCekPaylari.GetValueOrDefault(s.Kanal, 0m);
            decimal aySonucu = s.Gelen - s.Cari - s.Sabit - s.Kk - ortakPay + s.CekGelen - cekGiden;
            sonuc.Add(new KanalAylik(s.Kanal, s.Gelen, s.Cari, s.Sabit, s.Kk, ortakPay, aySonucu, s.CekGelen, cekGiden));
        }
        return new AylikRapor(yil, ay, sonuc);
    }
}

/// <summary>
/// Kanalın ay sonucu. Gelen/CariGiden/OrtakPay çek içermez; çek tahsilatı <see cref="CekGelen"/>,
/// çek ödemesi (kanalın kendi çekleri + Ortak çek ödemelerinden kanala düşen pay) <see cref="CekGiden"/>'dedir.
/// AySonucu = Gelen + CekGelen − CariGiden − SabitGider − KrediKarti − OrtakPay − CekGiden.
/// </summary>
public record KanalAylik(
    string Kanal,
    decimal Gelen,
    decimal CariGiden,
    decimal SabitGider,
    decimal KrediKarti,
    decimal OrtakPay,
    decimal AySonucu,
    decimal CekGelen = 0m,
    decimal CekGiden = 0m);

public record AylikRapor(int Yil, int Ay, IReadOnlyList<KanalAylik> Kanallar);
