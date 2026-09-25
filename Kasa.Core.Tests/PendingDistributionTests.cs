using Kasa.Core;

namespace Kasa.Core.Tests;

public class PendingDistributionTests
{
    // Eski veride aynı isimde gerçek kanal bulunabilse de bekleme durumu adla değil,
    // işlem üzerindeki işaretle belirlenmelidir.
    private static readonly Kanal[] KanallarTest =
    [new("MEZAT"), new("TOPTAN"), new(Kanallar.DagilimBekliyor)];

    [Theory]
    [InlineData("MEZAT", GiderTipi.Cari)]
    [InlineData("Ortak", GiderTipi.Cari)]
    [InlineData("Dağılım bekliyor", GiderTipi.Cari)]
    [InlineData("MEZAT", GiderTipi.SabitGider)]
    [InlineData("Ortak", GiderTipi.SabitGider)]
    [InlineData("Dağılım bekliyor", GiderTipi.SabitGider)]
    public void Bekleyen_nakit_kasada_bir_kez_sayilir_kanala_ve_ortak_paya_yazilmaz(string kanal, GiderTipi tip)
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        Islem[] islemler =
        [
            new(new(2026, 6, 5), "Bekleyen mal", 100.01m, kanal, tip, DagilimBekliyor: true),
            new(new(2026, 6, 5), "Gerçek ortak gider", 60m, Kanallar.Ortak, GiderTipi.SabitGider),
            new(new(2026, 6, 5), "Eski kanalın gideri", 30m, Kanallar.DagilimBekliyor, GiderTipi.Cari),
        ];

        var haftalik = HesapMotoru.HaftalikHesapla(1_000m, KanallarTest, islemler, [], donemler);
        var aylik = HesapMotoru.AylikHesapla(2026, 6, KanallarTest, islemler, [], donemler);

        Assert.Equal(190.01m, haftalik.Sum(h => h.ToplamGiden));
        Assert.Equal(809.99m, haftalik[^1].KasaDevir);
        Assert.Equal(100.01m, haftalik.Sum(h => h.DagilimBekleyenTutar));
        Assert.Equal(30m, haftalik.SelectMany(h => h.Kanallar).Sum(k => k.Giden));
        Assert.Equal(-30m, haftalik[^1].Kanallar.Single(k => k.Kanal == Kanallar.DagilimBekliyor).Devir);
        Assert.All(haftalik[^1].Kanallar.Where(k => k.Kanal != Kanallar.DagilimBekliyor), k => Assert.Equal(0m, k.Devir));
        Assert.All(aylik.Kanallar, k => Assert.Equal(20m, k.OrtakPay));
        Assert.Equal(100.01m, aylik.DagilimBekleyenTutar);
        Assert.Equal(-90m, aylik.Kanallar.Sum(k => k.AySonucu));
        Assert.Equal(haftalik.Sum(h => h.KasaSonucu), aylik.Kanallar.Sum(k => k.AySonucu) - aylik.DagilimBekleyenTutar);
    }

    [Fact]
    public void Bekleyen_kart_gideri_yil_gecisinde_gercek_ay_sonunda_bir_kez_duser()
    {
        Islem[] islemler =
        [new(new(2025, 12, 5), "Kartlı alış", 99.99m, Kanallar.DagilimBekliyor, GiderTipi.KrediKarti, DagilimBekliyor: true)];
        var kismi = DonemUretici.Uret(new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 30));
        var tam = DonemUretici.Uret(new DateOnly(2025, 12, 1), new DateOnly(2026, 2, 28));

        var once = HesapMotoru.HaftalikHesapla(1_000m, KanallarTest, islemler, [], kismi);
        var sonra = HesapMotoru.HaftalikHesapla(1_000m, KanallarTest, islemler, [], tam);

        Assert.All(once, h => { Assert.Equal(0m, h.ToplamGiden); Assert.Equal(0m, h.DagilimBekleyenTutar); });
        var etkilenen = Assert.Single(sonra, h => h.ToplamGiden != 0m);
        Assert.True(etkilenen.Donem.Icerir(new DateOnly(2026, 1, 31)));
        Assert.Equal(99.99m, etkilenen.ToplamGiden);
        Assert.Equal(99.99m, etkilenen.DagilimBekleyenTutar);
        Assert.Equal(900.01m, sonra[^1].KasaDevir);
        Assert.All(sonra.SelectMany(h => h.Kanallar), k => Assert.Equal(0m, k.Giden));

        var aralik = HesapMotoru.AylikHesapla(2025, 12, KanallarTest, islemler, [], tam);
        var ocak = HesapMotoru.AylikHesapla(2026, 1, KanallarTest, islemler, [], tam);
        var subat = HesapMotoru.AylikHesapla(2026, 2, KanallarTest, islemler, [], tam);
        Assert.Equal(0m, aralik.DagilimBekleyenTutar);
        Assert.Equal(99.99m, ocak.DagilimBekleyenTutar);
        Assert.Equal(0m, subat.DagilimBekleyenTutar);
        Assert.All(ocak.Kanallar, k => { Assert.Equal(0m, k.KrediKarti); Assert.Equal(0m, k.OrtakPay); });
        Assert.Equal(sonra.Sum(h => h.KasaSonucu), ocak.Kanallar.Sum(k => k.AySonucu) - ocak.DagilimBekleyenTutar);
    }

    [Theory]
    [InlineData(GiderTipi.Cari)]
    [InlineData(GiderTipi.KrediKarti)]
    public void Bekleyen_giderin_onayli_paylarla_degismesi_kasayi_ikinci_kez_etkilemez(GiderTipi tip)
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 30));
        var bekleyen = new Islem(new(2026, 5, 12), "Alış", 275.03m, Kanallar.DagilimBekliyor, tip, DagilimBekliyor: true);
        Islem[] onayli =
        [
            bekleyen with { Kanal = "MEZAT", TutarTl = 120.01m, DagilimBekliyor = false },
            bekleyen with { Kanal = "TOPTAN", TutarTl = 155.02m, DagilimBekliyor = false },
        ];

        var once = HesapMotoru.HaftalikHesapla(1_000m, KanallarTest, [bekleyen], [], donemler);
        var sonra = HesapMotoru.HaftalikHesapla(1_000m, KanallarTest, onayli, [], donemler);
        int etkiAyi = tip == GiderTipi.KrediKarti ? 6 : 5;
        var onceAylik = HesapMotoru.AylikHesapla(2026, etkiAyi, KanallarTest, [bekleyen], [], donemler);
        var sonraAylik = HesapMotoru.AylikHesapla(2026, etkiAyi, KanallarTest, onayli, [], donemler);

        Assert.Equal(once.Select(h => h.KasaDevir), sonra.Select(h => h.KasaDevir));
        Assert.Equal(724.97m, sonra[^1].KasaDevir);
        Assert.Equal(275.03m, once.Sum(h => h.DagilimBekleyenTutar));
        Assert.Equal(0m, sonra.Sum(h => h.DagilimBekleyenTutar));
        Assert.Equal(275.03m, onceAylik.DagilimBekleyenTutar);
        Assert.Equal(0m, sonraAylik.DagilimBekleyenTutar);
        Assert.Equal(-120.01m, sonraAylik.Kanallar.Single(k => k.Kanal == "MEZAT").AySonucu);
        Assert.Equal(-155.02m, sonraAylik.Kanallar.Single(k => k.Kanal == "TOPTAN").AySonucu);
        Assert.Equal(onceAylik.Kanallar.Sum(k => k.AySonucu) - onceAylik.DagilimBekleyenTutar,
            sonraAylik.Kanallar.Sum(k => k.AySonucu));
    }
}
