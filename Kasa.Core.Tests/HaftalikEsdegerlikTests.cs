using System.Diagnostics;
using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>
/// Hızlandırılmış HaftalikHesapla'nın (sıralama + ikili arama) eski O(dönem × kalem)
/// algoritmayla aynı sonucu verdiğini rastgele verilerle doğrular.
/// </summary>
public class HaftalikEsdegerlikTests
{
    /// <summary>311a335'teki HaftalikHesapla'nın birebir kopyası (referans).</summary>
    private static IReadOnlyList<HaftalikOzet> EskiHaftalikHesapla(
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

        var aylikKkToplam = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti && i.KrediKartiId is null)
            .GroupBy(i => (i.Tarih.Year, i.Tarih.Month))
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TutarTl));

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
            decimal toplamGiden = donemIslem.Where(i => i.Tip != GiderTipi.KrediKarti).Sum(i => i.TutarTl);
            if (ayinSonDonemi.TryGetValue((donem.Yil, donem.Ay), out var sonDonem) && sonDonem == donem)
            {
                int oncekiYil = donem.Ay == 1 ? donem.Yil - 1 : donem.Yil;
                int oncekiAy = donem.Ay == 1 ? 12 : donem.Ay - 1;
                if (aylikKkToplam.TryGetValue((oncekiYil, oncekiAy), out var ertelenenKk))
                    toplamGiden += ertelenenKk;
            }
            toplamGiden += odemeler.Where(o => donem.Icerir(o.Tarih)).Sum(o => o.Tutar);
            decimal kasaSonucu = toplamGelen - toplamGiden;
            kasaDevir += kasaSonucu;

            sonuc.Add(new HaftalikOzet(donem, kanalSatirlari, toplamGelen, toplamGiden, kasaSonucu, kasaDevir));
        }
        return sonuc;
    }

    private static readonly string[] KanalAdlari = { "MEZAT", "PERAKENDE", "TOPTAN", Kanallar.Ortak, "BILINMEYEN" };

    // Kuruşlu (2 ondalık) rastgele tutar — yeni motor tutarları kuruşa yuvarladığı için
    // eşdeğerlik 2 ondalıklı verilerle sınanır.
    private static decimal Tutar(Random r) => r.Next(0, 5_000_000) / 100m;

    private static DateOnly Tarih(Random r, DateOnly bas, int gun) => bas.AddDays(r.Next(-20, gun + 20));

    private static void Karsilastir(IReadOnlyList<HaftalikOzet> beklenen, IReadOnlyList<HaftalikOzet> gercek)
    {
        Assert.Equal(beklenen.Count, gercek.Count);
        for (int i = 0; i < beklenen.Count; i++)
        {
            var b = beklenen[i]; var g = gercek[i];
            Assert.Equal(b.Donem, g.Donem);
            Assert.Equal(b.ToplamGelen, g.ToplamGelen);
            Assert.Equal(b.ToplamGiden, g.ToplamGiden);
            Assert.Equal(b.KasaSonucu, g.KasaSonucu);
            Assert.Equal(b.KasaDevir, g.KasaDevir);
            Assert.Equal(b.Kanallar, g.Kanallar);   // KanalHaftalik record — değer eşitliği
        }
    }

    [Fact]
    public void Rastgele_verilerde_eski_algoritmayla_birebir_ayni()
    {
        var r = new Random(311);
        for (int tur = 0; tur < 300; tur++)
        {
            var bas = new DateOnly(2025, 1, 1).AddDays(r.Next(0, 700));
            int gun = r.Next(0, 200);
            var bitis = bas.AddDays(gun);

            var kanallar = new[]
            {
                new Kanal("MEZAT", Tutar(r)), new Kanal("PERAKENDE"), new Kanal("TOPTAN", 0m, Aktif: r.Next(2) == 0),
            };

            var islemler = Enumerable.Range(0, r.Next(0, 150)).Select(_ =>
            {
                var tip = (GiderTipi)r.Next(3);
                // Karta bağlı işlem yalnız K.K tipinde üretilir (Cari+kart artık bilinçli olarak farklı; ayrı testte).
                int? kart = tip == GiderTipi.KrediKarti && r.Next(2) == 0 ? r.Next(1, 3) : null;
                return new Islem(Tarih(r, bas, gun), "C", Tutar(r), KanalAdlari[r.Next(KanalAdlari.Length)], tip, KrediKartiId: kart);
            }).ToList();
            var gelenler = Enumerable.Range(0, r.Next(0, 60))
                .Select(_ => new Gelen(Tarih(r, bas, gun), KanalAdlari[r.Next(KanalAdlari.Length)], Tutar(r))).ToList();
            var odemeler = Enumerable.Range(0, r.Next(0, 20))
                .Select(_ => new KartOdeme(Tarih(r, bas, gun), Tutar(r))).ToList();

            // Çoğunlukla üretici takvimi; bazen karışık sıralı / çakışan / boş dönem listesi.
            List<Donem> donemler = DonemUretici.Uret(bas, bitis).ToList();
            if (tur % 5 == 0)
            {
                donemler.Add(new Donem(bas.AddDays(3), bas.AddDays(12)));       // çakışan
                donemler.Add(new Donem(bas.AddDays(9), bas.AddDays(5)));        // ters (boş)
                donemler = donemler.OrderBy(_ => r.Next()).ToList();
            }

            var acilis = Tutar(r);
            var kartOdemeleri = tur % 7 == 0 ? null : odemeler;
            Karsilastir(
                EskiHaftalikHesapla(acilis, kanallar, islemler, gelenler, donemler, kartOdemeleri),
                HesapMotoru.HaftalikHesapla(acilis, kanallar, islemler, gelenler, donemler, kartOdemeleri));
        }
    }

    [Fact]
    public void Buyuk_veride_hizli_calisir()
    {
        // ~10 yıl (≈ 600 dönem) × 60 bin işlem: eski algoritma dönem başına tüm listeyi tarardı.
        var r = new Random(7);
        var bas = new DateOnly(2016, 1, 1);
        int gun = 3650;
        var kanallar = new[] { new Kanal("MEZAT"), new Kanal("PERAKENDE"), new Kanal("TOPTAN") };
        var islemler = Enumerable.Range(0, 60_000)
            .Select(_ => new Islem(bas.AddDays(r.Next(gun)), "C", Tutar(r), KanalAdlari[r.Next(3)], (GiderTipi)r.Next(3))).ToList();
        var gelenler = Enumerable.Range(0, 5_000)
            .Select(_ => new Gelen(bas.AddDays(r.Next(gun)), KanalAdlari[r.Next(3)], Tutar(r))).ToList();
        var donemler = DonemUretici.Uret(bas, bas.AddDays(gun));

        var sw = Stopwatch.StartNew();
        var sonuc = HesapMotoru.HaftalikHesapla(0m, kanallar, islemler, gelenler, donemler);
        sw.Stop();

        Assert.Equal(donemler.Count, sonuc.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"HaftalikHesapla {sw.ElapsedMilliseconds} ms sürdü");
    }
}
