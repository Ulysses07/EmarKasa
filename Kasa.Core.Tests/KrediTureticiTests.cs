using Kasa.Core;

namespace Kasa.Core.Tests;

public class KrediTureticiTests
{
    private static Kredi OrnekKredi(
        string ad = "Ziraat İhtiyaç",
        decimal cekilen = 120000m,
        int yil = 2026, int ay = 8, int gun = 3,
        int taksitSayisi = 12,
        decimal aylikOdeme = 11000m,
        int odemeGunu = 15,
        string kanal = "MEZAT")
        => new(ad, cekilen, new DateOnly(yil, ay, gun), taksitSayisi, aylikOdeme, odemeGunu, kanal);

    private static IReadOnlyList<Donem> Donemler(params (int ay, int sg, int eg)[] parcalar)
        => parcalar.Select(p => new Donem(new DateOnly(2026, p.ay, p.sg), new DateOnly(2026, p.ay, p.eg))).ToList();

    [Fact]
    public void Cekim_dogru_doneme_yazilir()
    {
        var k = OrnekKredi(yil: 2026, ay: 8, gun: 3, cekilen: 120000m);
        var donemler = Donemler((8, 1, 7), (8, 8, 14));

        var gelen = KrediTuretici.CekimGeleni(k, donemler);

        Assert.NotNull(gelen);
        Assert.Equal(new DateOnly(2026, 8, 1), gelen!.DonemStart);
        Assert.Equal(120000m, gelen.TutarTl);
        Assert.Equal("__KREDI__", gelen.Kanal);
        Assert.Equal(KrediTuretici.KrediKanal, gelen.Kanal);
    }

    [Fact]
    public void Cekim_donem_disindaysa_null()
    {
        var k = OrnekKredi(yil: 2026, ay: 12, gun: 25);
        var donemler = Donemler((8, 1, 7), (8, 8, 14));

        Assert.Null(KrediTuretici.CekimGeleni(k, donemler));
    }

    [Fact]
    public void Taksit_sayisi_kadar_gider_uretir()
    {
        var k = OrnekKredi(taksitSayisi: 12);
        Assert.Equal(12, KrediTuretici.TaksitGiderleri(k).Count);
    }

    [Theory]
    [InlineData(2026, 8, 3, 15, 2026, 8, 15)]   // çekim 03.08 / gün 15 → 15.08
    [InlineData(2026, 8, 20, 15, 2026, 9, 15)]  // çekim 20.08 / gün 15 → 15.09
    [InlineData(2026, 8, 15, 15, 2026, 9, 15)]  // çekim 15.08 / gün 15 → 15.09 (kesin sonra)
    public void Ilk_taksit_cekimden_sonraki_odeme_gunu(
        int cy, int cm, int cd, int gun, int ey, int em, int ed)
    {
        var k = OrnekKredi(yil: cy, ay: cm, gun: cd, odemeGunu: gun, taksitSayisi: 3);
        var taksitler = KrediTuretici.TaksitGiderleri(k);
        Assert.Equal(new DateOnly(ey, em, ed), taksitler[0].Tarih);
    }

    [Fact]
    public void Kisa_ay_gun_ay_sonuna_sabitlenir()
    {
        // Çekim 20.01.2026, gün 31 → ilk taksit 31.01; ikinci taksit Şubat → 28 (2026 artık değil)
        var k = OrnekKredi(yil: 2026, ay: 1, gun: 20, odemeGunu: 31, taksitSayisi: 3);
        var taksitler = KrediTuretici.TaksitGiderleri(k);

        Assert.Equal(new DateOnly(2026, 1, 31), taksitler[0].Tarih);
        Assert.Equal(new DateOnly(2026, 2, 28), taksitler[1].Tarih);
        Assert.Equal(new DateOnly(2026, 3, 31), taksitler[2].Tarih);
    }

    [Fact]
    public void Kisa_ay_artik_yilda_29()
    {
        // 2028 artık yıl → Şubat 29
        var k = OrnekKredi(yil: 2028, ay: 1, gun: 5, odemeGunu: 31, taksitSayisi: 2);
        var taksitler = KrediTuretici.TaksitGiderleri(k);

        Assert.Equal(new DateOnly(2028, 1, 31), taksitler[0].Tarih);
        Assert.Equal(new DateOnly(2028, 2, 29), taksitler[1].Tarih);
    }

    [Fact]
    public void Yil_sarmasi()
    {
        // Çekim 20.11.2026, gün 15, 4 taksit → 15.12.2026, 15.01.2027, 15.02.2027, 15.03.2027
        var k = OrnekKredi(yil: 2026, ay: 11, gun: 20, odemeGunu: 15, taksitSayisi: 4);
        var taksitler = KrediTuretici.TaksitGiderleri(k);

        Assert.Equal(new DateOnly(2026, 12, 15), taksitler[0].Tarih);
        Assert.Equal(new DateOnly(2027, 1, 15), taksitler[1].Tarih);
        Assert.Equal(new DateOnly(2027, 2, 15), taksitler[2].Tarih);
        Assert.Equal(new DateOnly(2027, 3, 15), taksitler[3].Tarih);
    }

    [Fact]
    public void Taksit_kanal_ve_tutar_dogru()
    {
        var k = OrnekKredi(ad: "Garanti Taşıt", aylikOdeme: 8500m, kanal: "TOPTAN", taksitSayisi: 6);
        var taksitler = KrediTuretici.TaksitGiderleri(k);

        Assert.All(taksitler, i =>
        {
            Assert.Equal("TOPTAN", i.Kanal);
            Assert.Equal(8500m, i.TutarTl);
            Assert.Equal(GiderTipi.Cari, i.Tip);
            Assert.Equal("Garanti Taşıt", i.Cari);
        });
    }

    [Fact]
    public void Ortak_kanal_korunur()
    {
        var k = OrnekKredi(kanal: Kanallar.Ortak, taksitSayisi: 3);
        var taksitler = KrediTuretici.TaksitGiderleri(k);

        Assert.All(taksitler, i => Assert.Equal(Kanallar.Ortak, i.Kanal));
    }

    [Fact]
    public void Taksitler_aylik_araliklarla_siralidir()
    {
        var k = OrnekKredi(yil: 2026, ay: 8, gun: 3, odemeGunu: 15, taksitSayisi: 5);
        var taksitler = KrediTuretici.TaksitGiderleri(k);

        Assert.Equal(new DateOnly(2026, 8, 15), taksitler[0].Tarih);
        Assert.Equal(new DateOnly(2026, 9, 15), taksitler[1].Tarih);
        Assert.Equal(new DateOnly(2026, 10, 15), taksitler[2].Tarih);
        Assert.Equal(new DateOnly(2026, 11, 15), taksitler[3].Tarih);
        Assert.Equal(new DateOnly(2026, 12, 15), taksitler[4].Tarih);
    }
}
