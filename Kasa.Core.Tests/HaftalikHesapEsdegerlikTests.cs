using System.Diagnostics;
using System.Text.Json;
using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>
/// HaftalikHesapla'nın dönem × işlem taramasından dönem + işlem dağıtımına geçişi (gap-okuma-yolu-maliyet-kilit-cekismesi-10,
/// gap-veri-degismezleri-patlama-yaricapi-4): eski gövde burada referans olarak durur; sabit tohumlu rastgele senaryolarda
/// (kapsam dışı tarihler, nakit kart ödemesi, dağılım bekleyen, yalnız genel kasa, aylık gider, ay sonu K.K, eşleşmeyen
/// gelir dönemi, boşluklu/karışık sıralı ve çakışan dönem listeleri) yeni çıktı JSON olarak birebir aynıdır.
/// </summary>
public class HaftalikHesapEsdegerlikTests
{
    private static readonly JsonSerializerOptions Json = new();

    [Fact]
    public void Rastgele_senaryolarda_eski_taramayla_birebir_ayni()
    {
        var rnd = new Random(20260925);
        for (var senaryo = 0; senaryo < 500; senaryo++)
        {
            var (acilis, kanallar, islemler, gelenler, donemler) = Senaryo(rnd, senaryo);
            var beklenen = JsonSerializer.Serialize(Referans(acilis, kanallar, islemler, gelenler, donemler), Json);
            var gercek = JsonSerializer.Serialize(HesapMotoru.HaftalikHesapla(acilis, kanallar, islemler, gelenler, donemler), Json);
            Assert.True(beklenen == gercek, $"Senaryo {senaryo} farklı.");
        }
    }

    [Fact]
    public void Onbin_donem_ve_yirmibin_islem_saniyeler_icinde_hesaplanir()
    {
        // 180 yıllık ufuk (≈11 bin dönem) ve 20 bin işlem: eski tarama ≈220 milyon karşılaştırmaydı.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 1, 1), new DateOnly(2206, 6, 22));
        Assert.True(donemler.Count > 11_000);
        var rnd = new Random(7);
        var islemler = Enumerable.Range(0, 20_000).Select(i => new Islem(new DateOnly(2026, 1, 1).AddDays(rnd.Next(0, 1500)), "C", 10m + i % 5,
            i % 3 == 0 ? "A" : "B", (GiderTipi)(i % 3), DagilimBekliyor: i % 11 == 0)).ToList();
        var sure = Stopwatch.StartNew();
        var sonuc = HesapMotoru.HaftalikHesapla(0m, [new("A"), new("B")], islemler, [new(new DateOnly(2026, 1, 1), "A", 100m)], donemler);
        sure.Stop();
        Assert.Equal(donemler.Count, sonuc.Count);
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(5), $"{sure.ElapsedMilliseconds} ms sürdü.");
    }

    internal static (decimal, IReadOnlyList<Kanal>, IReadOnlyList<Islem>, IReadOnlyList<Gelen>, IReadOnlyList<Donem>) Senaryo(Random rnd, int no)
    {
        var baslangic = new DateOnly(2025, 1, 1).AddDays(rnd.Next(0, 800));
        var bitis = baslangic.AddDays(rnd.Next(0, 400));
        var donemler = DonemUretici.Uret(baslangic, bitis).ToList();
        switch (no % 5)
        {
            case 1:
                donemler = donemler.Where((_, i) => i % 3 != 1).ToList();
                break;             // boşluklu
            case 2:
                donemler = donemler.OrderBy(_ => rnd.Next()).ToList();
                break;                // karışık sıra
            case 3 when donemler.Count > 2:
                donemler.Add(donemler[1] with { End = donemler[2].End });
                break; // çakışan (eski yola düşer)
        }
        var adlar = new[] { "MEZAT", "PERAKENDE", "TOPTAN", "ESKI" }.Take(rnd.Next(1, 5)).ToArray();
        var kanallar = adlar.Select((a, i) => new Kanal(a, Tutar(rnd), rnd.Next(0, 4) != 0, i)).ToList();
        var etiketler = adlar.Concat([Kanallar.Ortak, Kanallar.DagilimBekliyor, "Genel kasa", "YOK"]).ToArray();
        var islemler = Enumerable.Range(0, rnd.Next(0, 120)).Select(_ => new Islem(
            baslangic.AddDays(rnd.Next(-40, 440)), "Cari", Tutar(rnd), etiketler[rnd.Next(etiketler.Length)], (GiderTipi)rnd.Next(0, 3), null,
            DagilimBekliyor: rnd.Next(0, 6) == 0, NakitKartOdemesi: rnd.Next(0, 5) == 0, AylikGider: rnd.Next(0, 7) == 0, YalnizGenelKasa: rnd.Next(0, 8) == 0)).ToList();
        var baslar = donemler.Select(d => d.Start).ToArray();
        var gelenler = Enumerable.Range(0, rnd.Next(0, 40)).Select(_ => new Gelen(
            baslar.Length > 0 && rnd.Next(0, 5) != 0 ? baslar[rnd.Next(baslar.Length)] : baslangic.AddDays(rnd.Next(-10, 400)),
            etiketler[rnd.Next(etiketler.Length)], Tutar(rnd), KrediGirisi: rnd.Next(0, 6) == 0, GenelGelir: rnd.Next(0, 6) == 0)).ToList();
        return (Tutar(rnd), kanallar, islemler, gelenler, donemler);
    }

    // Ondalık ölçeği de değişir (100,5 ile 100,50): toplamların ölçeği JSON'da görünür ve korunmalıdır.
    private static decimal Tutar(Random rnd)
    {
        var tutar = rnd.Next(-500_00, 5_000_00) / 100m;
        return rnd.Next(0, 3) == 0 ? tutar * 1.0m : tutar;
    }

    /// <summary>Değişiklikten önceki HaftalikHesapla gövdesi (her dönemde bütün işlem ve gelirleri tarar).</summary>
    private static IReadOnlyList<HaftalikOzet> Referans(decimal kasaAcilisDevri, IReadOnlyList<Kanal> kanallar, IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler, IReadOnlyList<Donem> donemler)
    {
        var sirali = donemler.OrderBy(d => d.Start).ToList();
        var kanalDevir = kanallar.ToDictionary(k => k.Ad, k => k.AcilisDevri);
        var kasaDevir = kasaAcilisDevri;
        var sonuc = new List<HaftalikOzet>();
        var aylikKkToplam = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti && !i.NakitKartOdemesi)
            .GroupBy(EtkiAyi)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TutarTl));
        foreach (var donem in sirali)
        {
            var donemIslem = islemler.Where(i => donem.Icerir(i.Tarih)).ToList();
            var donemGelen = gelenler.Where(g => g.DonemStart == donem.Start).ToList();
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
            decimal toplamGiden = donemIslem.Where(i => i.Tip != GiderTipi.KrediKarti || i.NakitKartOdemesi).Sum(i => i.TutarTl);
            decimal bekleyen = donemIslem.Where(i => (i.Tip != GiderTipi.KrediKarti || i.NakitKartOdemesi) && i.DagilimBekliyor).Sum(i => i.TutarTl);
            var aySonu = new DateOnly(donem.Yil, donem.Ay, DateTime.DaysInMonth(donem.Yil, donem.Ay));
            if (donem.Icerir(aySonu))
            {
                if (aylikKkToplam.TryGetValue((donem.Yil, donem.Ay), out var ertelenenKk))
                    toplamGiden += ertelenenKk;
                bekleyen += islemler.Where(i => i.Tip == GiderTipi.KrediKarti && !i.NakitKartOdemesi && i.DagilimBekliyor
                    && EtkiAyi(i) == (donem.Yil, donem.Ay)).Sum(i => i.TutarTl);
            }
            decimal kasaSonucu = toplamGelen - toplamGiden;
            kasaDevir += kasaSonucu;
            sonuc.Add(new HaftalikOzet(donem, kanalSatirlari, toplamGelen, toplamGiden, kasaSonucu, kasaDevir, bekleyen));
        }
        return sonuc;
    }

    private static (int Yil, int Ay) EtkiAyi(Islem islem)
    {
        int yil = islem.Tarih.Year, ay = islem.Tarih.Month;
        if (islem.Tip != GiderTipi.KrediKarti || islem.NakitKartOdemesi)
            return (yil, ay);
        return ay == 12 ? (yil + 1, 1) : (yil, ay + 1);
    }
}
