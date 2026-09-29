using System.Text.Json;
using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>
/// Aylık rapor kural sürümü. Kural 1, K2 kararından önceki AylikHesapla gövdesidir ve kilitli ayların geçişte
/// dondurulmasında kullanılır: burada eski gövde referans olarak durur ve sabit tohumlu rastgele senaryolarda (takipli
/// kredi girişi, eski '__KREDI__' çekimi, genel gelir, K.K ertelemesi, nakit kart ödemesi, dağılım bekleyen, yalnız genel
/// kasa, dönem dışı tarihler) kural 1 çıktısı JSON olarak birebir aynıdır. Kural 2 (K2, varsayılan): kredi girişi Gelen ve
/// Ay sonucu dışında, ayrı alanda; eski ve takipli kredi aynı sonucu verir; haftalık kasa değişmez.
/// </summary>
public class AylikKuralSurumuTests
{
    private static readonly Kanal[] UcKanal = [new("MEZAT"), new("PERAKENDE"), new("TOPTAN")];
    private static readonly JsonSerializerOptions Json = new();

    [Fact]
    public void Rastgele_senaryolarda_kural_1_eski_aylik_hesapla_ile_birebir_ayni()
    {
        var rnd = new Random(20260927);
        for (var senaryo = 0; senaryo < 500; senaryo++)
        {
            var (kanallar, islemler, gelenler, donemler, aylar) = Senaryo(rnd);
            foreach (var (yil, ay) in aylar)
            {
                var beklenen = JsonSerializer.Serialize(EskiAylikHesapla(yil, ay, kanallar, islemler, gelenler, donemler), Json);
                var gercek = JsonSerializer.Serialize(HesapMotoru.AylikHesapla(yil, ay, kanallar, islemler, gelenler, donemler, AylikKural.V1), Json);
                Assert.True(beklenen == gercek, $"Senaryo {senaryo}, {yil}-{ay} farklı:\n{beklenen}\n{gercek}");
            }
        }
    }

    [Fact]
    public void Kural_1_yeni_alanlari_yazmaz_takipli_kredi_girisi_gelen_ve_ay_sonucunda_kalir()
    {
        var (donemler, gelenler, islemler) = TakipliKrediAyi();
        var rapor = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, gelenler, donemler, AylikKural.V1);
        var mezat = rapor.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(200_000m, mezat.Gelen);
        Assert.Equal(120_000m, mezat.KrediGirisi);
        Assert.Equal(100_000m, mezat.AySonucu);
        Assert.Null(rapor.KrediGirisi);
        Assert.Null(rapor.KuralSurumu);
        var json = JsonSerializer.Serialize(rapor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("kuralSurumu", json);
        Assert.DoesNotContain("veriSagligiUyarisi", json);
        Assert.EndsWith("\"genelGelir\":0}", json);
    }

    [Fact]
    public void Kural_2_takipli_kredi_girisi_gelen_ve_ay_sonucu_disinda_ayri_doner()
    {
        // MEZAT: satış 80.000, takipli kredi çekimi 120.000, cari gider 100.000. Faaliyet sonucu −20.000'dir.
        var (donemler, gelenler, islemler) = TakipliKrediAyi();
        var rapor = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, gelenler, donemler);
        var mezat = rapor.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(80_000m, mezat.Gelen);
        Assert.Equal(120_000m, mezat.KrediGirisi);
        Assert.Equal(-20_000m, mezat.AySonucu);
        Assert.Equal(120_000m, rapor.KrediGirisi);
        Assert.Equal(AylikKural.V2, rapor.KuralSurumu);
        Assert.Equal(AylikKural.V2, AylikKural.Guncel);
    }

    [Fact]
    public void Kural_2_eski_ve_takipli_kredi_ayni_ay_sonucunu_ve_ayni_kredi_girisi_toplamini_verir()
    {
        var (donemler, takipliGelenler, islemler) = TakipliKrediAyi();
        // Aynı ekonomik olay eski modelde: çekim hiçbir kanala ait olmayan '__KREDI__' geliri.
        var eskiGelenler = takipliGelenler.Where(g => !g.KrediGirisi)
            .Append(new Gelen(donemler[0].Start, KrediTuretici.KrediKanal, 120_000m)).ToList();

        var takipli = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, takipliGelenler, donemler);
        var eski = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, eskiGelenler, donemler);

        Assert.Equal(takipli.Kanallar.Select(k => (k.Kanal, k.Gelen, k.AySonucu)), eski.Kanallar.Select(k => (k.Kanal, k.Gelen, k.AySonucu)));
        Assert.Equal(takipli.KrediGirisi, eski.KrediGirisi);
        Assert.Equal(120_000m, eski.KrediGirisi);
        Assert.All(eski.Kanallar, k => Assert.Equal(0m, k.KrediGirisi)); // eski çekim kanal satırına dağıtılmaz
    }

    [Fact]
    public void Kural_2_haftalik_kasa_degismez_ve_haftalik_ile_aylik_kredi_girisiyle_mutabiktir()
    {
        // İki kanal payı (takipli kredi), eski kredi çekimi, genel gelir, ortak gider, dağılım bekleyen ve yalnız genel kasa gideri.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var d0 = donemler[0].Start;
        var d2 = donemler[2].Start;
        var gelenler = new List<Gelen>
        {
            new(d0, "MEZAT", 50_000m), new(d0, "TOPTAN", 20_000m),
            new(d2, "MEZAT", 300_000m, KrediGirisi: true), new(d2, "TOPTAN", 300_000m, KrediGirisi: true),
            new(d2, KrediTuretici.KrediKanal, 40_000m), new(d2, "Genel kasa", 1_000m, GenelGelir: true),
        };
        var islemler = new List<Islem>
        {
            new(new DateOnly(2026, 9, 3), "Tedarik", 10_000m, "MEZAT", GiderTipi.Cari),
            new(new DateOnly(2026, 9, 10), "Kira", 900.01m, Kanallar.Ortak, GiderTipi.SabitGider),
            new(new DateOnly(2026, 9, 12), "Bekleyen", 700m, Kanallar.DagilimBekliyor, GiderTipi.Cari, DagilimBekliyor: true),
            new(new DateOnly(2026, 9, 20), "Banka", 350m, "Genel kasa", GiderTipi.Cari, YalnizGenelKasa: true),
        };
        var haftalik = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, gelenler, donemler);
        var kural2 = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, gelenler, donemler);
        var kural1 = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, gelenler, donemler, AylikKural.V1);

        // Haftalık: kredi nakit olarak kasaya ve (takipliyse) kanal devrine girer; kuraldan bağımsızdır.
        Assert.Equal(300_000m, haftalik[2].Kanallar.Single(k => k.Kanal == "MEZAT").KrediGirisi);
        Assert.Equal(340_000m, haftalik[^1].Kanallar.Single(k => k.Kanal == "MEZAT").Devir); // 50.000 + 300.000 − 10.000
        Assert.Equal(711_000m, haftalik.Sum(h => h.ToplamGelen));

        decimal Mutabakat(AylikRapor r) => r.Kanallar.Sum(k => k.AySonucu) - r.DagilimBekleyenTutar - r.GenelGider + r.GenelGelir;
        Assert.Equal(640_000m, kural2.KrediGirisi);
        Assert.Equal(haftalik.Sum(h => h.KasaSonucu), Mutabakat(kural2) + kural2.KrediGirisi);
        // Kural 1'de takipli kredi ay sonucundaydı, eski '__KREDI__' çekimi hiçbir alanda değildi: mutabakat 40.000 eksik kalırdı.
        Assert.Equal(haftalik.Sum(h => h.KasaSonucu) - 40_000m, Mutabakat(kural1));
    }

    [Fact]
    public void Bilinmeyen_kural_surumu_reddedilir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => HesapMotoru.AylikHesapla(2026, 9, UcKanal, [], [], donemler, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => HesapMotoru.AylikHesapla(2026, 9, UcKanal, [], [], donemler, 0));
    }

    private static (IReadOnlyList<Donem>, List<Gelen>, List<Islem>) TakipliKrediAyi()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var gelenler = new List<Gelen>
        {
            new(donemler[0].Start, "MEZAT", 80_000m),
            new(donemler[1].Start, "MEZAT", 120_000m, KrediGirisi: true),
        };
        var islemler = new List<Islem> { new(new DateOnly(2026, 9, 15), "Tedarik", 100_000m, "MEZAT", GiderTipi.Cari) };
        return (donemler, gelenler, islemler);
    }

    private static (IReadOnlyList<Kanal>, IReadOnlyList<Islem>, IReadOnlyList<Gelen>, IReadOnlyList<Donem>, (int, int)[]) Senaryo(Random rnd)
    {
        var baslangic = new DateOnly(2025, 1, 1).AddDays(rnd.Next(0, 800));
        var bitis = baslangic.AddDays(rnd.Next(0, 400));
        var donemler = DonemUretici.Uret(baslangic, bitis);
        var adlar = new[] { "MEZAT", "PERAKENDE", "TOPTAN", "ESKI" }.Take(rnd.Next(1, 5)).ToArray();
        var kanallar = adlar.Select((a, i) => new Kanal(a, Tutar(rnd), rnd.Next(0, 4) != 0, i)).ToList();
        var etiketler = adlar.Concat([Kanallar.Ortak, Kanallar.DagilimBekliyor, "Genel kasa", KrediTuretici.KrediKanal, "YOK"]).ToArray();
        var islemler = Enumerable.Range(0, rnd.Next(0, 120)).Select(_ => new Islem(
            baslangic.AddDays(rnd.Next(-60, 440)), "Cari", Tutar(rnd), etiketler[rnd.Next(etiketler.Length)], (GiderTipi)rnd.Next(0, 3), null,
            DagilimBekliyor: rnd.Next(0, 6) == 0, NakitKartOdemesi: rnd.Next(0, 5) == 0, AylikGider: rnd.Next(0, 7) == 0, YalnizGenelKasa: rnd.Next(0, 8) == 0)).ToList();
        var baslar = donemler.Select(d => d.Start).ToArray();
        var gelenler = Enumerable.Range(0, rnd.Next(0, 40)).Select(_ => new Gelen(
            baslar.Length > 0 && rnd.Next(0, 5) != 0 ? baslar[rnd.Next(baslar.Length)] : baslangic.AddDays(rnd.Next(-10, 400)),
            etiketler[rnd.Next(etiketler.Length)], Tutar(rnd), KrediGirisi: rnd.Next(0, 4) == 0, GenelGelir: rnd.Next(0, 6) == 0)).ToList();
        // Dönem ufkunun öncesi ve sonrası dahil ay listesi.
        var aylar = Enumerable.Range(-2, 16).Select(i => baslangic.AddMonths(i)).Select(t => (t.Year, t.Month)).Distinct().ToArray();
        return (kanallar, islemler, gelenler, donemler, aylar);
    }

    // Ondalık ölçeği de değişir (100,5 ile 100,50): toplamların ölçeği JSON'da görünür ve korunmalıdır.
    private static decimal Tutar(Random rnd)
    {
        var tutar = rnd.Next(-500_00, 5_000_00) / 100m;
        return rnd.Next(0, 3) == 0 ? tutar * 1.0m : tutar;
    }

    /// <summary>K2 kararından önceki AylikHesapla gövdesi (kural 1'in tanımı).</summary>
    private static AylikRapor EskiAylikHesapla(int yil, int ay, IReadOnlyList<Kanal> kanallar, IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler, IReadOnlyList<Donem> donemler)
    {
        var ayinDonemleri = donemler.Where(d => d.Yil == yil && d.Ay == ay).ToList();
        var ayinDonemStartlari = ayinDonemleri.Select(d => d.Start).ToHashSet();
        var ayinIslemleri = islemler.Where(i => EtkiAyi(i) == (yil, ay)).ToList();
        decimal ortakToplam = ayinIslemleri.Where(i => !i.DagilimBekliyor && !i.YalnizGenelKasa && i.Kanal == Kanallar.Ortak).Sum(i => i.TutarTl);
        int aktifKanalSayisi = kanallar.Count(k => k.Aktif);
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

    private static (int Yil, int Ay) EtkiAyi(Islem islem)
    {
        int yil = islem.Tarih.Year, ay = islem.Tarih.Month;
        if (islem.Tip != GiderTipi.KrediKarti || islem.NakitKartOdemesi)
            return (yil, ay);
        return ay == 12 ? (yil + 1, 1) : (yil, ay + 1);
    }
}
