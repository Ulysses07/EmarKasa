using System.Text.Json.Serialization;

namespace Kasa.Core;

public record KanalHaftalik(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir, decimal KrediGirisi = 0m);

/// <param name="VeriSagligiUyarisi">İsteğe bağlı veri sağlığı uyarısı (ör. rapor ufkunun ötesinde tarihli kayıt);
/// yalnız raporun son döneminde ve yalnız sorun varsa dolu. Boşken JSON'a yazılmaz: eski yanıt biçimi aynen korunur.</param>
public record HaftalikOzet(
    Donem Donem,
    IReadOnlyList<KanalHaftalik> Kanallar,
    decimal ToplamGelen,
    decimal ToplamGiden,
    decimal KasaSonucu,
    decimal KasaDevir,
    decimal DagilimBekleyenTutar = 0m,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VeriSagligiUyarisi = null);

public static class HesapMotoru
{
    /// <summary>
    /// Dönem dönem haftalık özet üretir. Kanal devri yalnız o kanalın Cari tipli
    /// gidenini sayar. Kasa devri Cari + SabitGider + Ortak gidenleri kendi döneminde
    /// sayar; KrediKarti ise ertelenir — bir sonraki ayın son döneminde kasadan çıkar.
    /// Devirler tarih sırasına göre zincirlenir; ilk dönemin girişi açılış bakiyeleridir.
    /// </summary>
    public static IReadOnlyList<HaftalikOzet> HaftalikHesapla(
        decimal kasaAcilisDevri,
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler)
    {
        var sirali = donemler.OrderBy(d => d.Start).ToList();
        var kanalDevir = kanallar.ToDictionary(k => k.Ad, k => k.AcilisDevri);
        var kasaDevir = kasaAcilisDevri;
        var sonuc = new List<HaftalikOzet>();

        // Kredi kartı ertelemesi (kasa): bir ayın K.K'sı o ay kasadan çıkmaz;
        // ödemesi bir SONRAKİ ayın SON döneminde toplu olarak kasadan çıkar.
        var aylikKkToplam = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti && !i.NakitKartOdemesi)
            .GroupBy(EtkiAyi)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TutarTl));
        // Ay sonunda kasadan çıkan K.K'nın dağılım bekleyen kısmı da etki ayına göre bir kez toplanır.
        var aylikKkBekleyen = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti && !i.NakitKartOdemesi && i.DagilimBekliyor)
            .GroupBy(EtkiAyi)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TutarTl));
        // İşlemler ve gelirler dönemlerine bir kez dağıtılır: maliyet dönem × işlem değil dönem + işlem. Dönem içi
        // sıra girdi sırasıdır; sonuç her dönemde bütün listeyi taramakla birebir aynıdır.
        var donemIslemleri = DonemlereDagit(sirali, islemler);
        var donemGelenleri = gelenler.ToLookup(g => g.DonemStart);

        for (var d = 0; d < sirali.Count; d++)
        {
            var donem = sirali[d];
            var donemIslem = donemIslemleri?[d] ?? islemler.Where(i => donem.Icerir(i.Tarih)).ToList();
            var donemGelen = donemGelenleri[donem.Start].ToList();

            var kanalSatirlari = new List<KanalHaftalik>();
            foreach (var kanal in kanallar)
            {
                decimal gelen = donemGelen.Where(g => !g.GenelGelir && g.Kanal == kanal.Ad).Sum(g => g.TutarTl);
                decimal gidenCari = donemIslem
                    .Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == kanal.Ad && (i.Tip == GiderTipi.Cari || i.NakitKartOdemesi || i.AylikGider))
                    .Sum(i => i.TutarTl);
                decimal kanalSonuc = gelen - gidenCari;
                kanalDevir[kanal.Ad] += kanalSonuc;
                kanalSatirlari.Add(new KanalHaftalik(kanal.Ad, gelen, gidenCari, kanalSonuc, kanalDevir[kanal.Ad], donemGelen.Where(g => g.Kanal == kanal.Ad && g.KrediGirisi).Sum(g => g.TutarTl)));
            }

            decimal toplamGelen = donemGelen.Sum(g => g.TutarTl);
            // KK kendi döneminde kasadan çıkmaz (ertelenir).
            decimal toplamGiden = donemIslem.Where(i => i.Tip != GiderTipi.KrediKarti || i.NakitKartOdemesi).Sum(i => i.TutarTl);
            decimal bekleyen = donemIslem.Where(i => (i.Tip != GiderTipi.KrediKarti || i.NakitKartOdemesi) && i.DagilimBekliyor).Sum(i => i.TutarTl);
            // Kısmi raporun son dönemi ay sonu olmayabilir. KK ancak gerçek ay sonu
            // kapsanıyorsa kasadan çıkar; rapor ufku ilerledikçe eski haftalar değişmez.
            var aySonu = new DateOnly(donem.Yil, donem.Ay, DateTime.DaysInMonth(donem.Yil, donem.Ay));
            if (donem.Icerir(aySonu))
            {
                if (aylikKkToplam.TryGetValue((donem.Yil, donem.Ay), out var ertelenenKk))
                    toplamGiden += ertelenenKk;
                bekleyen += aylikKkBekleyen.GetValueOrDefault((donem.Yil, donem.Ay));
            }
            decimal kasaSonucu = toplamGelen - toplamGiden;
            kasaDevir += kasaSonucu;

            sonuc.Add(new HaftalikOzet(donem, kanalSatirlari, toplamGelen, toplamGiden, kasaSonucu, kasaDevir, bekleyen));
        }
        return sonuc;
    }

    /// <summary>
    /// Kasa hareket dökümü (gap-denetim-izi-gozlemlenebilirlik-3): <see cref="HaftalikHesapla"/>'nın genel kasaya ve kanal
    /// devrine yazdığı her tutar, kaynağıyla bir satır. Hesap yolu değişmez; bu yöntem aynı kuralları (dönem dağıtımı, dönem
    /// geliri, ertelemeli K.K'nın etki ayının son döneminde düşmesi, kanal devrine yalnız Cari/nakit kart ödemesi/aylık gider
    /// girmesi, dağılım bekleyen ve yalnız genel kasa satırının kanala yazılmaması) satır satır uygular. Değişmezler (testle
    /// sabit): açılış + Σ <see cref="KasaHareketi.GenelKasaEtkisi"/> = son dönemin KasaDevir'i; her dönem sonunda
    /// (çakışmayan dönemlerde) açılış + Σ(etki tarihi ≤ dönem sonu) = o dönemin KasaDevir'i; kanalın açılış devri + Σ
    /// <see cref="KasaHareketi.KanalEtkisi"/> = kanal devri. Satırlar etki tarihine göre sıralıdır (aynı günde hesap sırası).
    /// </summary>
    public static IReadOnlyList<KasaHareketi> KasaHareketleri(
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler)
    {
        var sirali = donemler.OrderBy(d => d.Start).ToList();
        var kanalAdlari = kanallar.Select(k => k.Ad).ToHashSet();
        // HaftalikHesapla ile aynı: ay sonunda kasadan çıkan K.K etki ayına göre toplanır; kendi döneminde kasaya yazılmaz.
        var ertelenenKk = islemler.Where(i => i.Tip == GiderTipi.KrediKarti && !i.NakitKartOdemesi).ToLookup(EtkiAyi);
        var donemIslemleri = DonemlereDagit(sirali, islemler);
        var donemGelenleri = gelenler.ToLookup(g => g.DonemStart);
        var sonuc = new List<KasaHareketi>();
        for (var d = 0; d < sirali.Count; d++)
        {
            var donem = sirali[d];
            foreach (var g in donemGelenleri[donem.Start])
                sonuc.Add(new(g.Tarih ?? donem.Start, g.TutarTl, !g.GenelGelir && kanalAdlari.Contains(g.Kanal) ? g.TutarTl : 0m, Gelen: g));
            foreach (var i in donemIslemleri?[d] ?? islemler.Where(i => donem.Icerir(i.Tarih)).ToList())
            {
                var ertelenen = i.Tip == GiderTipi.KrediKarti && !i.NakitKartOdemesi;
                var kanal = !i.DagilimBekliyor && !i.YalnizGenelKasa && kanalAdlari.Contains(i.Kanal)
                    && (i.Tip == GiderTipi.Cari || i.NakitKartOdemesi || i.AylikGider) ? -i.TutarTl : 0m;
                // Ertelemeli K.K kendi döneminde kasaya yazılmaz; kanal devrine de yazılmıyorsa satırı yalnız ay sonundadır.
                if (ertelenen && kanal == 0m)
                    continue;
                sonuc.Add(new(i.Tarih, ertelenen ? 0m : -i.TutarTl, kanal, Islem: i));
            }
            var aySonu = new DateOnly(donem.Yil, donem.Ay, DateTime.DaysInMonth(donem.Yil, donem.Ay));
            if (donem.Icerir(aySonu))
                foreach (var i in ertelenenKk[(donem.Yil, donem.Ay)])
                    sonuc.Add(new(aySonu, -i.TutarTl, 0m, Islem: i, KartAySonu: true));
        }
        // OrderBy kararlıdır: aynı günün satırları hesap sırasını korur.
        return sonuc.OrderBy(h => h.EtkiTarihi).ToList();
    }

    /// <summary>İşlemleri başlangıca göre sıralı dönemlerin kovalarına bir kez dağıtır (ikili arama). Dönemler çakışıyor
    /// ya da ters sınırlıysa (DonemUretici bunu üretmez) null döner; çağıran eski taramaya düşer.</summary>
    private static List<Islem>[]? DonemlereDagit(IReadOnlyList<Donem> sirali, IReadOnlyList<Islem> islemler)
    {
        for (var i = 0; i < sirali.Count; i++)
            if (sirali[i].End < sirali[i].Start || (i > 0 && sirali[i].Start <= sirali[i - 1].End))
                return null;
        var kovalar = new List<Islem>[sirali.Count];
        for (var i = 0; i < kovalar.Length; i++)
            kovalar[i] = [];
        foreach (var islem in islemler)
        {
            // Başlangıcı işlem tarihinden sonra olmayan son dönem; tarih onun bitişini aşıyorsa işlem hiçbir dönemde değildir.
            int alt = 0, ust = sirali.Count - 1, bulunan = -1;
            while (alt <= ust)
            {
                var orta = alt + (ust - alt) / 2;
                if (sirali[orta].Start <= islem.Tarih)
                { bulunan = orta; alt = orta + 1; }
                else
                    ust = orta - 1;
            }
            if (bulunan >= 0 && islem.Tarih <= sirali[bulunan].End)
                kovalar[bulunan].Add(islem);
        }
        return kovalar;
    }

    /// <summary>
    /// Bir takvim ayı için kanal başına AY SONUCU üretir.
    /// Aylık gelen = o aya düşen dönemlerin geleni. Ortak giderler (Kanallar.Ortak) ayın Ortak kümesine (verilmezse aktif
    /// kanallara) kuruş bazında (artık kuruşlar kümenin ilk kanallarına) dağıtılıp düşülür. Satırlar <paramref name="kanallar"/>
    /// sırasıyla, kanal başına birer tanedir.
    /// <paramref name="kuralSurumu"/> (<see cref="AylikKural"/>): <see cref="AylikKural.V1"/> 2.1–2.3 davranışıdır ve birebir
    /// korunur (kilitli ayların geçişte dondurulması için). <see cref="AylikKural.V2"/> (varsayılan) kredi girişini
    /// (takipli kredinin kanal payları ve eski modelin '__KREDI__' çekimi) Gelen ve Ay sonucu dışında tutar,
    /// <see cref="AylikRapor.KrediGirisi"/>'nde ayrı döndürür; kanal satırlarının kredi girişi sütunu iki sürümde aynıdır.
    /// Haftalık kasa ve kanal devri kuraldan etkilenmez (kredi nakit olarak kasaya girer).
    /// <paramref name="ortakKanallari"/> (core-1): ayın Ortak kümesi; kanal adlarıyla, Ortak gideri bölme sırasıyla. Verilirse Ortak
    /// gider yalnız bu kanallara VERİLEN SIRAYLA bölünür; kanalların bugünkü Aktif bayrağı ve liste sırası Ortak payını etkilemez.
    /// Böylece tamamlanmış ayın payı o ayın kümesiyle sabit kalır: sonradan pasife alınan kanal o ayın payını almaya devam eder,
    /// sonradan açılan kanal almaz. Boş liste o ay Ortak gideri bölecek kanal olmadığını söyler (aktif kanal yokken olduğu gibi pay
    /// üretilmez). Her ad <paramref name="kanallar"/>'da bulunmalı ve bir kez geçmelidir. Verilmezse (null) bugünkü davranış
    /// birebir korunur: <paramref name="kanallar"/>'ın aktif olanları, liste sırasıyla.
    /// </summary>
    public static AylikRapor AylikHesapla(
        int yil,
        int ay,
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler,
        int kuralSurumu = AylikKural.Guncel,
        IReadOnlyList<string>? ortakKanallari = null)
    {
        if (kuralSurumu is not (AylikKural.V1 or AylikKural.V2))
            throw new ArgumentOutOfRangeException(nameof(kuralSurumu), kuralSurumu, "Bilinmeyen aylık rapor kural sürümü.");
        if (ortakKanallari is not null)
        {
            var adlar = kanallar.Select(k => k.Ad).ToHashSet();
            if (ortakKanallari.FirstOrDefault(ad => !adlar.Contains(ad)) is { } bilinmeyen)
                throw new ArgumentException($"Ortak pay kümesindeki kanal bulunamadı: {bilinmeyen}", nameof(ortakKanallari));
            if (ortakKanallari.GroupBy(ad => ad).FirstOrDefault(g => g.Count() > 1) is { } yinelenen)
                throw new ArgumentException($"Ortak pay kümesinde kanal birden çok kez geçiyor: {yinelenen.Key}", nameof(ortakKanallari));
        }
        var krediAyri = kuralSurumu >= AylikKural.V2;
        var ayinDonemleri = donemler.Where(d => d.Yil == yil && d.Ay == ay).ToList();
        var ayinDonemStartlari = ayinDonemleri.Select(d => d.Start).ToHashSet();
        // Etki ayını ortak pay dağıtımından önce belirle; Ortak KK da diğer
        // kart harcamaları gibi bir sonraki ayın sonucuna girer.
        var ayinIslemleri = islemler.Where(i => EtkiAyi(i) == (yil, ay)).ToList();

        decimal ortakToplam = ayinIslemleri.Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == Kanallar.Ortak).Sum(i => i.TutarTl);
        var ortakKumesi = ortakKanallari ?? kanallar.Where(k => k.Aktif).Select(k => k.Ad).ToList();
        int aktifKanalSayisi = ortakKumesi.Count;

        // Ortak gideri kümenin kanallarına kuruş bazında dağıt. Tam bölünmediğinde
        // artık kuruş(ları) işaretini koruyarak kümenin ilk kanallarına ver.
        var ortakPaylari = new Dictionary<string, decimal>();
        if (aktifKanalSayisi > 0)
        {
            long toplamKurus = (long)decimal.Round(ortakToplam * 100m, 0, MidpointRounding.AwayFromZero);
            long tabanKurus = toplamKurus / aktifKanalSayisi;
            long artanKurus = toplamKurus - tabanKurus * aktifKanalSayisi;
            int aktifIndex = 0;
            foreach (var kanalAdi in ortakKumesi)
            {
                long payKurus = tabanKurus + (aktifIndex < Math.Abs(artanKurus) ? Math.Sign(artanKurus) : 0);
                ortakPaylari[kanalAdi] = payKurus / 100m;
                aktifIndex++;
            }
        }

        var satirlar = new List<KanalAylik>();
        foreach (var kanal in kanallar)
        {
            decimal gelen = gelenler
                .Where(g => !g.GenelGelir && g.Kanal == kanal.Ad && ayinDonemStartlari.Contains(g.DonemStart) && !(krediAyri && g.KrediGirisi))
                .Sum(g => g.TutarTl);
            decimal cari = ayinIslemleri.Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == kanal.Ad && i.Tip == GiderTipi.Cari).Sum(i => i.TutarTl);
            decimal sabit = ayinIslemleri.Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == kanal.Ad && i.Tip == GiderTipi.SabitGider).Sum(i => i.TutarTl);
            decimal kk = ayinIslemleri.Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == kanal.Ad && i.Tip == GiderTipi.KrediKarti).Sum(i => i.TutarTl);
            decimal ortakPay = ortakPaylari.GetValueOrDefault(kanal.Ad, 0m);
            decimal aySonucu = gelen - cari - sabit - kk - ortakPay;
            satirlar.Add(new KanalAylik(kanal.Ad, gelen, cari, sabit, kk, ortakPay, aySonucu, gelenler.Where(g => g.Kanal == kanal.Ad && g.KrediGirisi && ayinDonemStartlari.Contains(g.DonemStart)).Sum(g => g.TutarTl)));
        }
        var rapor = new AylikRapor(yil, ay, satirlar,
            ayinIslemleri.Where(i => i.DagilimBekliyor).Sum(i => i.TutarTl),
            ayinIslemleri.Where(i => i.YalnizGenelKasa).Sum(i => i.TutarTl),
            gelenler.Where(g => g.GenelGelir && ayinDonemStartlari.Contains(g.DonemStart)).Sum(g => g.TutarTl));
        if (!krediAyri)
            return rapor;
        // K2: ayın bütün kredi girişi tek alanda. Eski modelin çekimi hiçbir kanala ait değildir ('__KREDI__'); takipli
        // kredinin payları kanal satırlarında da görünür. İki model aynı ay sonucunu ve aynı toplamı verir.
        return rapor with
        {
            KrediGirisi = gelenler.Where(g => !g.GenelGelir && ayinDonemStartlari.Contains(g.DonemStart) && (g.KrediGirisi || g.Kanal == KrediTuretici.KrediKanal))
                .Sum(g => g.TutarTl),
            KuralSurumu = kuralSurumu,
        };
    }

    /// <summary>
    /// K1 (kullanıcı kararı): takip başlangıcından (ilk dönemden) önce tarihli mevcut giderler olduğu gibi kalır, tutarlar
    /// değişmez; ancak raporlarda farklı işlenir: haftalık kasaya ve kanal devrine hiç girmez, aylık raporda gider ayında
    /// sayılır. Ertelemeli eski K.K için bakılan tarih etki ayının son günüdür: etki ayı başlangıç ayı ya da sonrasıysa
    /// haftalık kasada da o ay sonunda düşer (tutarlı, sayılmaz). <paramref name="ay"/> verilirse yalnız o ayın aylık
    /// sonucuna giren satırlar sayılır. Hiç dönem yoksa (ör. takip başlangıcından önceki ayın raporu) bütün satırlar
    /// başlangıç öncesidir. Adet kaynak kayıttır (<see cref="Islem.Kaynak"/>): birden çok kanala bölünen kayıt ve eski kredinin
    /// türetilmiş taksitleri bir kez sayılır, anahtarsız satır tek başına bir kayıttır; toplam bütün satırlarındır.
    /// </summary>
    public static (int Adet, decimal Toplam) BaslangicOncesi(IReadOnlyList<Islem> islemler, IReadOnlyList<Donem> donemler, (int Yil, int Ay)? ay = null)
    {
        DateOnly? ilk = donemler.Count == 0 ? null : donemler.Min(d => d.Start);
        var satirlar = islemler.Where(i => (ay is not { } a || EtkiAyi(i) == a) && (ilk is not { } bas || KasaEtkiTarihi(i) < bas)).ToList();
        var adet = satirlar.Count(i => i.Kaynak is null) + satirlar.Where(i => i.Kaynak is not null).Select(i => i.Kaynak).Distinct(StringComparer.Ordinal).Count();
        return (adet, satirlar.Sum(i => i.TutarTl));
    }

    /// <summary>K1 uyarı metni (veri sağlığı alanı için); başlangıç öncesi satır yoksa null. Tutar Türkçe biçimdedir.</summary>
    public static string? BaslangicOncesiUyarisi((int Adet, decimal Toplam) oncesi, bool aylik) => oncesi.Adet == 0 ? null
        : $"Takip başlangıcından önce tarihli {oncesi.Adet} kayıt, toplam {TlMetni(oncesi.Toplam)} ₺ — raporlarda farklı işlenir: "
          + (aylik ? "bu ayın sonucunda sayılır, haftalık kasaya ve kanal devrine girmez."
                   : "haftalık kasaya ve kanal devrine girmez, aylık raporda gider ayının sonucunda sayılır.")
          + " Kayıtlar ve tutarlar olduğu gibi korunur.";

    // Kültürden bağımsız Türkçe tutar: binlik nokta, kuruş virgül (1.234,50).
    private static readonly System.Globalization.NumberFormatInfo TlBicimi = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NegativeSign = "-" };
    private static string TlMetni(decimal tutar) => tutar.ToString("#,0.00", TlBicimi);

    // İşlemin haftalık kasadan düştüğü gün: ertelemeli K.K etki ayının son günü, diğerleri kendi tarihi.
    private static DateOnly KasaEtkiTarihi(Islem islem)
    {
        if (islem.Tip != GiderTipi.KrediKarti || islem.NakitKartOdemesi)
            return islem.Tarih;
        var (yil, ay) = EtkiAyi(islem);
        return new DateOnly(yil, ay, DateTime.DaysInMonth(yil, ay));
    }

    // İşlemin haftalık kasa ve aylık sonuç üzerindeki ayı tek kuraldan türetilir.
    private static (int Yil, int Ay) EtkiAyi(Islem islem)
    {
        int yil = islem.Tarih.Year, ay = islem.Tarih.Month;
        if (islem.Tip != GiderTipi.KrediKarti || islem.NakitKartOdemesi)
            return (yil, ay);
        return ay == 12 ? (yil + 1, 1) : (yil, ay + 1);
    }
}

/// <summary>
/// Kasa hareket dökümünün satırı (<see cref="HesapMotoru.KasaHareketleri"/>): genel kasaya ya da bir kanalın devrine yazılan tek
/// tutar ve onu üreten gider (<paramref name="Islem"/>) ya da gelir (<paramref name="Gelen"/>).
/// </summary>
/// <param name="EtkiTarihi">Tutarın kasayı değiştirdiği gün: giderin ve günü bilinen gelirin kendi tarihi, dönem gelirinde dönem
/// başı, ertelemeli K.K'da (<paramref name="KartAySonu"/>) etki ayının son günü.</param>
/// <param name="GenelKasaEtkisi">Genel kasaya etkisi, işaretli (gelir +, gider −).</param>
/// <param name="KanalEtkisi">Satırın kanalının devrine etkisi, işaretli; sabit gider, Ortak, dağılım bekleyen, yalnız genel kasa
/// satırında ve gerçek kanal olmayan etikette 0.</param>
/// <param name="KartAySonu">Ertelemeli K.K'nın (eski kart kuralı) etki ayının sonundaki kasa düşümü: yazma olmadan, tarih
/// ilerleyince işler.</param>
public record KasaHareketi(DateOnly EtkiTarihi, decimal GenelKasaEtkisi, decimal KanalEtkisi, Islem? Islem = null, Gelen? Gelen = null, bool KartAySonu = false)
{
    /// <summary>Satırın kanal etiketi (gerçek kanal, <see cref="Kanallar.Ortak"/>, <see cref="Kanallar.DagilimBekliyor"/>, "Genel kasa" ya da
    /// <see cref="KrediTuretici.KrediKanal"/>).</summary>
    public string Kanal => Islem?.Kanal ?? Gelen?.Kanal ?? "";
    /// <summary>Kaydın kendi tarihi: giderin tarihi, gelirin günü ya da (tarihsiz dönem gelirinde) dönem başı.</summary>
    public DateOnly KayitTarihi => Islem?.Tarih ?? Gelen?.Tarih ?? Gelen?.DonemStart ?? EtkiTarihi;
    /// <summary>Kaydın döküm anahtarı (<see cref="Islem.KaynakAnahtari"/>, <see cref="Gelen.KaynakAnahtari"/>).</summary>
    public string? KaynakAnahtari => Islem?.KaynakAnahtari ?? Gelen?.KaynakAnahtari;
}

public record KanalAylik(
    string Kanal,
    decimal Gelen,
    decimal CariGiden,
    decimal SabitGider,
    decimal KrediKarti,
    decimal OrtakPay,
    decimal AySonucu,
    decimal KrediGirisi = 0m);

/// <param name="KrediGirisi">Ayın kredi girişi toplamı (yalnız <see cref="AylikKural.V2"/>): Gelen ve Ay sonucu dışında,
/// genel kasaya giren kredi çekimi. Kural 1 raporunda null ve JSON'a yazılmaz (eski yanıt biçimi aynen korunur).</param>
/// <param name="KuralSurumu">Raporu üreten kural; kural 1'de null (eski biçim).</param>
/// <param name="VeriSagligiUyarisi">Rakamları değiştirmeyen veri sağlığı uyarısı; yoksa null ve JSON'a yazılmaz.</param>
public record AylikRapor(int Yil, int Ay, IReadOnlyList<KanalAylik> Kanallar, decimal DagilimBekleyenTutar = 0m, decimal GenelGider = 0m, decimal GenelGelir = 0m,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? KrediGirisi = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? KuralSurumu = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VeriSagligiUyarisi = null);

/// <summary>
/// Aylık rapor kural sürümleri. Kilitli (kapatılmış) ayın raporu kilitlendiği andaki kuralla dondurulur (Kasa.Api anlık
/// görüntüsü): kural değişiklikleri kapatılmış ayı değiştirmez. Açık aylar her zaman <see cref="Guncel"/> ile hesaplanır.
/// </summary>
public static class AylikKural
{
    /// <summary>2.1–2.3 kuralı: takipli kredi çekimi kanal Gelen'ine ve Ay sonucuna girer (eski '__KREDI__' çekimi hiçbir alana).</summary>
    public const int V1 = 1;
    /// <summary>Kullanıcı kararı K2 (2026-09-27): kredi girişi Gelen ve Ay sonucu dışında, ayrı 'Kredi girişi' alanında.</summary>
    public const int V2 = 2;
    public const int Guncel = V2;
}
