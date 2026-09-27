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

    /// <summary>İşlemleri başlangıca göre sıralı dönemlerin kovalarına bir kez dağıtır (ikili arama). Dönemler çakışıyor
    /// ya da ters sınırlıysa (DonemUretici bunu üretmez) null döner; çağıran eski taramaya düşer.</summary>
    private static List<Islem>[]? DonemlereDagit(IReadOnlyList<Donem> sirali, IReadOnlyList<Islem> islemler)
    {
        for (var i = 0; i < sirali.Count; i++)
            if (sirali[i].End < sirali[i].Start || (i > 0 && sirali[i].Start <= sirali[i - 1].End)) return null;
        var kovalar = new List<Islem>[sirali.Count];
        for (var i = 0; i < kovalar.Length; i++) kovalar[i] = [];
        foreach (var islem in islemler)
        {
            // Başlangıcı işlem tarihinden sonra olmayan son dönem; tarih onun bitişini aşıyorsa işlem hiçbir dönemde değildir.
            int alt = 0, ust = sirali.Count - 1, bulunan = -1;
            while (alt <= ust)
            {
                var orta = alt + (ust - alt) / 2;
                if (sirali[orta].Start <= islem.Tarih) { bulunan = orta; alt = orta + 1; }
                else ust = orta - 1;
            }
            if (bulunan >= 0 && islem.Tarih <= sirali[bulunan].End) kovalar[bulunan].Add(islem);
        }
        return kovalar;
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
        var ayinDonemStartlari = ayinDonemleri.Select(d => d.Start).ToHashSet();
        // Etki ayını ortak pay dağıtımından önce belirle; Ortak KK da diğer
        // kart harcamaları gibi bir sonraki ayın sonucuna girer.
        var ayinIslemleri = islemler.Where(i => EtkiAyi(i) == (yil, ay)).ToList();

        decimal ortakToplam = ayinIslemleri.Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == Kanallar.Ortak).Sum(i => i.TutarTl);
        int aktifKanalSayisi = kanallar.Count(k => k.Aktif);

        // Ortak gideri aktif kanallara kuruş bazında dağıt. Tam bölünmediğinde
        // artık kuruş(ları) işaretini koruyarak ilk aktif kanallara ver.
        var ortakPaylari = new Dictionary<string, decimal>();
        if (aktifKanalSayisi > 0)
        {
            long toplamKurus = (long)decimal.Round(ortakToplam * 100m, 0, MidpointRounding.AwayFromZero);
            long tabanKurus = toplamKurus / aktifKanalSayisi;
            long artanKurus = toplamKurus - tabanKurus * aktifKanalSayisi;
            int aktifIndex = 0;
            foreach (var kanal in kanallar.Where(k => k.Aktif))
            {
                long payKurus = tabanKurus + (aktifIndex < Math.Abs(artanKurus) ? Math.Sign(artanKurus) : 0);
                ortakPaylari[kanal.Ad] = payKurus / 100m;
                aktifIndex++;
            }
        }

        var satirlar = new List<KanalAylik>();
        foreach (var kanal in kanallar)
        {
            decimal gelen = gelenler
                .Where(g => !g.GenelGelir && g.Kanal == kanal.Ad && ayinDonemStartlari.Contains(g.DonemStart))
                .Sum(g => g.TutarTl);
            decimal cari = ayinIslemleri.Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == kanal.Ad && i.Tip == GiderTipi.Cari).Sum(i => i.TutarTl);
            decimal sabit = ayinIslemleri.Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == kanal.Ad && i.Tip == GiderTipi.SabitGider).Sum(i => i.TutarTl);
            decimal kk = ayinIslemleri.Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == kanal.Ad && i.Tip == GiderTipi.KrediKarti).Sum(i => i.TutarTl);
            decimal ortakPay = ortakPaylari.GetValueOrDefault(kanal.Ad, 0m);
            decimal aySonucu = gelen - cari - sabit - kk - ortakPay;
            satirlar.Add(new KanalAylik(kanal.Ad, gelen, cari, sabit, kk, ortakPay, aySonucu, gelenler.Where(g => g.Kanal == kanal.Ad && g.KrediGirisi && ayinDonemStartlari.Contains(g.DonemStart)).Sum(g => g.TutarTl)));
        }
        return new AylikRapor(yil, ay, satirlar,
            ayinIslemleri.Where(i => i.DagilimBekliyor).Sum(i => i.TutarTl),
            ayinIslemleri.Where(i => i.YalnizGenelKasa).Sum(i => i.TutarTl),
            gelenler.Where(g => g.GenelGelir && ayinDonemStartlari.Contains(g.DonemStart)).Sum(g => g.TutarTl));
    }

    // İşlemin haftalık kasa ve aylık sonuç üzerindeki ayı tek kuraldan türetilir.
    private static (int Yil, int Ay) EtkiAyi(Islem islem)
    {
        int yil = islem.Tarih.Year, ay = islem.Tarih.Month;
        if (islem.Tip != GiderTipi.KrediKarti || islem.NakitKartOdemesi) return (yil, ay);
        return ay == 12 ? (yil + 1, 1) : (yil, ay + 1);
    }
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

public record AylikRapor(int Yil, int Ay, IReadOnlyList<KanalAylik> Kanallar, decimal DagilimBekleyenTutar = 0m, decimal GenelGider = 0m, decimal GenelGelir = 0m);
