using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>
/// "Kasa neden değişti?" dökümü: açılış + Σ adımlar = kapanış; dönem başına Σ Kalemler = KasaSonucu.
/// Mevcut rakamların değişmediği <see cref="MotorAltinCiktiTests"/>'te sabitlenmiştir.
/// </summary>
public class KasaDokumuTests
{
    private static readonly Kanal[] UcKanal = { new("MEZAT"), new("PERAKENDE"), new("TOPTAN") };
    private static DateOnly G(int ay, int gun) => new(2026, ay, gun);

    private static void DonemDegismezleri(IReadOnlyList<HaftalikOzet> ozetler)
    {
        foreach (var o in ozetler)
        {
            Assert.Equal(o.KasaSonucu, o.Kalemler.Sum(k => k.Tutar));
            Assert.Equal(o.ToplamGelen, o.Kalemler.Where(k => k.Tur == KasaKalemTuru.Gelen).Sum(k => k.Tutar));
            Assert.Equal(o.ToplamCekGelen, o.Kalemler.Where(k => k.Tur == KasaKalemTuru.CekTahsilat).Sum(k => k.Tutar));
            Assert.Equal(-o.ToplamCekGiden, o.Kalemler.Where(k => k.Tur == KasaKalemTuru.CekOdemesi).Sum(k => k.Tutar));
            Assert.Equal(-o.ToplamGiden, o.Kalemler
                .Where(k => k.Tur is KasaKalemTuru.CariGider or KasaKalemTuru.SabitGider or KasaKalemTuru.OrtakGider
                    or KasaKalemTuru.KartOdemesi or KasaKalemTuru.ErtelenenKk)
                .Sum(k => k.Tutar));
            Assert.DoesNotContain(o.Kalemler, k => k.Tutar == 0m);
            // Aynı (tür, kanal) tek satırdır.
            Assert.Equal(o.Kalemler.Count, o.Kalemler.Select(k => (k.Tur, k.Kanal)).Distinct().Count());
            foreach (var k in o.Kalemler)
            {
                if (k.Tur is KasaKalemTuru.Gelen or KasaKalemTuru.CekTahsilat) Assert.True(k.Tutar > 0);
                else Assert.True(k.Tutar < 0, $"{k.Tur} {k.Kanal} {k.Tutar}");
                Assert.Equal(k.Tur == KasaKalemTuru.KartOdemesi, k.Kanal is null);
            }
        }
    }

    private static void DokumDegismezleri(IReadOnlyList<HaftalikOzet> parca, IReadOnlyList<Kanal> kanallar)
    {
        var d = KasaDokumuHesap.Olustur(parca, kanallar);
        Assert.NotNull(d);
        Assert.Equal(d.Kapanis, d.Acilis + d.Adimlar.Sum(a => a.Tutar));
        Assert.Equal(d.Kapanis, d.Adimlar.Count > 0 ? d.Adimlar[^1].Bakiye : d.Acilis);
        Assert.Equal(parca[^1].KasaDevir, d.Kapanis);
        Assert.Equal(parca.Sum(o => o.KasaSonucu), d.Kapanis - d.Acilis);
        Assert.Equal(d.Kapanis - d.Acilis, d.ToplamGiren - d.ToplamCikan);
        decimal bakiye = d.Acilis;
        foreach (var a in d.Adimlar) { bakiye += a.Tutar; Assert.Equal(bakiye, a.Bakiye); }
        Assert.Equal(parca.Min(o => o.Donem.Start), d.Baslangic);
        Assert.Equal(parca.Max(o => o.Donem.End), d.Bitis);
    }

    [Fact]
    public void Rastgele_girdilerde_acilis_arti_adimlar_kapanisa_esit()
    {
        for (int t = 1; t <= 250; t++)
        {
            var g = MotorVeriUretici.Uret(t);
            var ozetler = g.Haftalik();
            DonemDegismezleri(ozetler);
            // Her dönem, her ay, tüm aralık ve rastgele ardışık dilimler.
            foreach (var o in ozetler) DokumDegismezleri(new[] { o }, g.Kanallar);
            foreach (var ay in ozetler.GroupBy(o => (o.Donem.Yil, o.Donem.Ay))) DokumDegismezleri(ay.ToList(), g.Kanallar);
            if (ozetler.Count > 0) DokumDegismezleri(ozetler, g.Kanallar);
            var r = new Random(t);
            for (int k = 0; k < 5 && ozetler.Count > 1; k++)
            {
                int bas = r.Next(ozetler.Count), son = r.Next(bas, ozetler.Count);
                DokumDegismezleri(ozetler.Skip(bas).Take(son - bas + 1).ToList(), g.Kanallar);
            }
        }
    }

    [Fact]
    public void Haziran_senaryosu_adim_adim()
    {
        // HaziranSenaryoTests'teki 29-30 Haziran dönemi.
        var kanallar = new Kanal[] { new("MEZAT", 4_991_052m), new("PERAKENDE", 2_013_516m), new("TOPTAN", 619_647m) };
        var start = G(6, 29);
        var ozetler = HesapMotoru.HaftalikHesapla(2_907_053.21m, kanallar,
            new[]
            {
                new Islem(start, "MEZAT-cari", 1_308_800m, "MEZAT", GiderTipi.Cari),
                new Islem(start, "PERAKENDE-cari", 1_221_374m, "PERAKENDE", GiderTipi.Cari),
                new Islem(start, "TOPTAN-cari", 360_000m, "TOPTAN", GiderTipi.Cari),
                new Islem(G(6, 30), "SGK/Vergi/vb.", 455_321m, Kanallar.Ortak, GiderTipi.SabitGider),
            },
            new[] { new Gelen(start, "MEZAT", 289_425m), new Gelen(start, "PERAKENDE", 271_006m), new Gelen(start, "TOPTAN", 207_000m) },
            DonemUretici.Uret(start, G(6, 30)));

        var d = KasaDokumuHesap.Olustur(ozetler, kanallar)!;
        Assert.Equal(2_907_053.21m, d.Acilis);
        Assert.Equal(328_989.21m, d.Kapanis);
        Assert.Equal(new[]
        {
            new KasaDokumAdimi(KasaKalemTuru.Gelen, "MEZAT", 289_425m, 3_196_478.21m),
            new KasaDokumAdimi(KasaKalemTuru.Gelen, "PERAKENDE", 271_006m, 3_467_484.21m),
            new KasaDokumAdimi(KasaKalemTuru.Gelen, "TOPTAN", 207_000m, 3_674_484.21m),
            new KasaDokumAdimi(KasaKalemTuru.CariGider, "MEZAT", -1_308_800m, 2_365_684.21m),
            new KasaDokumAdimi(KasaKalemTuru.CariGider, "PERAKENDE", -1_221_374m, 1_144_310.21m),
            new KasaDokumAdimi(KasaKalemTuru.CariGider, "TOPTAN", -360_000m, 784_310.21m),
            new KasaDokumAdimi(KasaKalemTuru.OrtakGider, Kanallar.Ortak, -455_321m, 328_989.21m),
        }, d.Adimlar);
    }

    [Fact]
    public void Kart_ertelenen_kk_cek_ve_sabit_gider_ayri_adimlardir()
    {
        var donemler = DonemUretici.Uret(G(5, 1), G(6, 30));
        var islemler = new[]
        {
            new Islem(G(5, 10), "Market", 300m, "MEZAT", GiderTipi.KrediKarti),             // kartsız K.K → Haziran son döneminde
            new Islem(G(5, 12), "Akaryakıt", 200m, Kanallar.Ortak, GiderTipi.KrediKarti),   // kartsız, Ortak
            new Islem(G(5, 13), "Kartlı", 999m, "MEZAT", GiderTipi.Cari, null, 7),          // karta bağlı: kasaya dokunmaz
            new Islem(G(6, 3), "Maaş", 1_000m, "TOPTAN", GiderTipi.SabitGider),
            new Islem(G(6, 4), "Kira", 500m, Kanallar.Ortak, GiderTipi.Cari),
        };
        var gelenler = new[] { new Gelen(G(6, 1), "MEZAT", 10_000m), new Gelen(G(6, 1), "Eski kanal", 50m) };
        var odemeler = new[] { new KartOdeme(G(6, 15), 999m) };
        var cekler = new[]
        {
            new Cek(CekYonu.Alinan, 2_000m, "PERAKENDE", CekDurumu.TahsilEdildi, G(6, 20)),
            new Cek(CekYonu.Verilen, 700m, Kanallar.Ortak, CekDurumu.Odendi, G(6, 21)),
            new Cek(CekYonu.Alinan, 5_000m, "MEZAT", CekDurumu.Portfoyde, null),              // kasaya dokunmaz
            new Cek(CekYonu.Alinan, 4_000m, "MEZAT", CekDurumu.CiroEdildi, G(6, 22)),          // kasaya dokunmaz
        };
        var ozetler = HesapMotoru.HaftalikHesapla(1_000m, UcKanal, islemler, gelenler, donemler, odemeler, cekler);
        DonemDegismezleri(ozetler);

        var haziran = ozetler.Where(o => o.Donem.Ay == 6).ToList();
        var d = KasaDokumuHesap.Olustur(haziran, UcKanal)!;
        Assert.Equal(ozetler.Where(o => o.Donem.Ay == 5).Last().KasaDevir, d.Acilis);
        Assert.Equal(new (KasaKalemTuru, string?, decimal)[]
        {
            (KasaKalemTuru.Gelen, "MEZAT", 10_000m),
            (KasaKalemTuru.Gelen, "Eski kanal", 50m),
            (KasaKalemTuru.CekTahsilat, "PERAKENDE", 2_000m),
            (KasaKalemTuru.SabitGider, "TOPTAN", -1_000m),
            (KasaKalemTuru.OrtakGider, Kanallar.Ortak, -500m),
            (KasaKalemTuru.CekOdemesi, Kanallar.Ortak, -700m),
            (KasaKalemTuru.KartOdemesi, null, -999m),
            (KasaKalemTuru.ErtelenenKk, "MEZAT", -300m),
            (KasaKalemTuru.ErtelenenKk, Kanallar.Ortak, -200m),
        }, d.Adimlar.Select(a => (a.Tur, a.Kanal, a.Tutar)).ToArray());
        Assert.Equal(d.Acilis + 10_050m + 2_000m - 1_000m - 500m - 700m - 999m - 500m, d.Kapanis);
        // Ertelenen K.K yalnız ayın SON döneminde.
        Assert.All(haziran.SkipLast(1), o => Assert.DoesNotContain(o.Kalemler, k => k.Tur == KasaKalemTuru.ErtelenenKk));
    }

    [Fact]
    public void Kurus_yuvarlama_motorla_ayni()
    {
        var donemler = DonemUretici.Uret(G(6, 1), G(6, 7));
        var ozetler = HesapMotoru.HaftalikHesapla(0m, UcKanal,
            new[] { new Islem(G(6, 2), "X", 100.005m, "MEZAT", GiderTipi.Cari) },
            new[] { new Gelen(G(6, 1), "MEZAT", 0.004m), new Gelen(G(6, 1), "TOPTAN", 1.115m) },
            donemler);
        var o = ozetler.Single();
        Assert.Equal(new[] { new KasaKalemi(KasaKalemTuru.Gelen, "TOPTAN", 1.12m), new KasaKalemi(KasaKalemTuru.CariGider, "MEZAT", -100.01m) },
            o.Kalemler);
        Assert.Equal(o.KasaSonucu, o.Kalemler.Sum(k => k.Tutar));
    }

    [Fact]
    public void Bos_ozet_listesinde_dokum_yok_hareketsiz_donemde_adim_yok()
    {
        Assert.Null(KasaDokumuHesap.Olustur(Array.Empty<HaftalikOzet>(), UcKanal));
        var ozetler = HesapMotoru.HaftalikHesapla(750m, UcKanal, Array.Empty<Islem>(), Array.Empty<Gelen>(),
            DonemUretici.Uret(G(6, 1), G(6, 14)));
        var d = KasaDokumuHesap.Olustur(ozetler, UcKanal)!;
        Assert.Empty(d.Adimlar);
        Assert.Equal(750m, d.Acilis);
        Assert.Equal(750m, d.Kapanis);
    }

    [Fact]
    public void Kalemler_rapor_jsonuna_girmez()
    {
        var o = MotorVeriUretici.Uret(3).Haftalik().First(x => x.Kalemler.Count > 0);
        Assert.DoesNotContain("Kalemler", System.Text.Json.JsonSerializer.Serialize(o));
    }

    [Fact]
    public void Kanal_sirasi_verilen_liste_sonra_ortak_sonra_diger()
    {
        var s = KasaDokumuHesap.Sirala(new[]
        {
            new KasaKalemi(KasaKalemTuru.CariGider, "Zeta", -1m),
            new KasaKalemi(KasaKalemTuru.CariGider, Kanallar.Ortak, -1m),
            new KasaKalemi(KasaKalemTuru.CariGider, "Alfa", -1m),
            new KasaKalemi(KasaKalemTuru.CariGider, "TOPTAN", -1m),
            new KasaKalemi(KasaKalemTuru.Gelen, "TOPTAN", 1m),
            new KasaKalemi(KasaKalemTuru.CariGider, "MEZAT", -1m),
        }, UcKanal);
        Assert.Equal(new[] { "TOPTAN", "MEZAT", "TOPTAN", Kanallar.Ortak, "Alfa", "Zeta" }, s.Select(k => k.Kanal));
    }
}
