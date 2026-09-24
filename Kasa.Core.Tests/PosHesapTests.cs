using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>POS komisyon / net / valör hesabı (kasa ve kârlılıktan bağımsız bilgi hesabı).</summary>
public class PosHesapTests
{
    private static DateOnly G(int gun) => new(2026, 9, gun);

    [Theory]
    [InlineData("1000", "1.79", "17.90", "982.10")]
    [InlineData("100", "0", "0.00", "100.00")]
    [InlineData("0.50", "1", "0.01", "0.49")]        // 0,005 → 0,01 (sıfırdan uzağa)
    [InlineData("0.49", "1", "0.00", "0.49")]        // 0,0049 → 0,00
    [InlineData("333.33", "2.5", "8.33", "325.00")]  // 8,33325 → 8,33
    [InlineData("1234.567", "1", "12.35", "1222.22")] // brüt önce kuruşa: 1234,57
    public void Komisyon_kurusa_yuvarlanir_net_ile_toplami_bruttur(string brut, string oran, string komisyon, string net)
    {
        var b = decimal.Parse(brut, System.Globalization.CultureInfo.InvariantCulture);
        var o = decimal.Parse(oran, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(decimal.Parse(komisyon, System.Globalization.CultureInfo.InvariantCulture), PosHesap.Komisyon(b, o));
        Assert.Equal(decimal.Parse(net, System.Globalization.CultureInfo.InvariantCulture), PosHesap.Net(b, o));
        Assert.Equal(Para.Yuvarla(b), PosHesap.Komisyon(b, o) + PosHesap.Net(b, o));
    }

    [Fact]
    public void Valor_takvim_gunuyle_ilerler_ay_ve_yil_atlar()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), PosHesap.Valor(G(30), 1));
        Assert.Equal(new DateOnly(2027, 1, 30), PosHesap.Valor(new DateOnly(2026, 12, 31), 30));
        Assert.Equal(G(5), PosHesap.Valor(G(5), 0));
    }

    [Theory]
    [InlineData(10, 3, 10, true)]    // satış günü bloke
    [InlineData(10, 3, 12, true)]
    [InlineData(10, 3, 13, false)]   // valör günü: hesaba geçti
    [InlineData(10, 0, 10, false)]   // blokajsız: hiç bloke olmaz
    [InlineData(15, 3, 12, false)]   // ileri tarihli satış henüz yok
    public void Bloke_satis_gunu_ile_valor_arasi(int satis, int blokaj, int bugun, bool beklenen)
        => Assert.Equal(beklenen, PosHesap.BlokeMi(G(satis), blokaj, G(bugun)));

    [Fact]
    public void Bloke_toplami_ve_valor_dokumu()
    {
        var kalemler = new[]
        {
            new PosKalem(G(20), "MEZAT", 1000m, 2m, 7),     // valör 27 → bloke, net 980
            new PosKalem(G(22), "TOPTAN", 500m, 1m, 5),     // valör 27 → bloke, net 495
            new PosKalem(G(23), "MEZAT", 200m, 0m, 1),      // valör 24 → bugün geçti
            new PosKalem(G(24), "MEZAT", 300m, 1.5m, 2),    // valör 26 → bloke, net 295,50
            new PosKalem(G(25), "MEZAT", 999m, 1m, 2),      // ileri tarihli
        };
        var (net, adet) = PosHesap.Bloke(kalemler, G(24));
        Assert.Equal(980m + 495m + 295.50m, net);
        Assert.Equal(3, adet);

        var dokum = PosHesap.BekleyenValorler(kalemler, G(24));
        Assert.Equal([G(26), G(27)], dokum.Select(d => d.Valor));
        Assert.Equal(295.50m, dokum[0].Net);
        Assert.Equal(980m + 495m, dokum[1].Net);
        Assert.Equal(2, dokum[1].Adet);
    }

    [Fact]
    public void Aylik_ozet_kanal_basina_toplar_diger_aylari_almaz()
    {
        var kalemler = new[]
        {
            new PosKalem(G(1), "MEZAT", 100.10m, 1.79m, 1),
            new PosKalem(G(2), "PERAKENDE", 50m, 2m, 0),
            new PosKalem(G(30), "MEZAT", 200m, 1.79m, 1),
            new PosKalem(new DateOnly(2026, 8, 31), "MEZAT", 1_000m, 1m, 1),
            new PosKalem(new DateOnly(2026, 10, 1), "MEZAT", 1_000m, 1m, 1),
        };
        var ozet = PosHesap.AylikOzet(kalemler, 2026, 9);
        Assert.Equal(["MEZAT", "PERAKENDE"], ozet.Select(o => o.Kanal));
        var mezat = ozet[0];
        Assert.Equal(300.10m, mezat.Brut);
        Assert.Equal(PosHesap.Komisyon(100.10m, 1.79m) + PosHesap.Komisyon(200m, 1.79m), mezat.Komisyon);
        Assert.Equal(mezat.Brut - mezat.Komisyon, mezat.Net);
        Assert.Equal(2, mezat.Adet);
        Assert.Equal(new PosKanalOzeti("PERAKENDE", 50m, 1m, 49m, 1), ozet[1]);
        Assert.Empty(PosHesap.AylikOzet(kalemler, 2026, 7));
    }

    /// <summary>Bulgu: bloke para yalnız tek toplam olarak vardı; kanal kanal görünmeli (ay sınırını aşan satış dahil).</summary>
    [Fact]
    public void Bloke_kanal_kanal_dokumu_ay_sinirini_asan_satisi_da_alir_toplami_bloke_ile_ayni()
    {
        var kalemler = new[]
        {
            new PosKalem(new DateOnly(2026, 8, 28), "TOPTAN", 1000m, 2m, 30),   // geçen ayın satışı, valör 27 Eylül → bloke
            new PosKalem(G(20), "MEZAT", 1000m, 2m, 7),                           // valör 27 → bloke, net 980
            new PosKalem(G(22), "TOPTAN", 500m, 1m, 5),                           // valör 27 → bloke, net 495
            new PosKalem(G(23), "MEZAT", 200m, 0m, 1),                            // valör 24 → bugün geçti
            new PosKalem(G(24), "Kanalsız", 300m, 1.5m, 2),                       // bloke, net 295,50
            new PosKalem(G(24), "PERAKENDE", 50m, 1m, 0),                         // blokajsız
            new PosKalem(G(25), "MEZAT", 999m, 1m, 2),                            // ileri tarihli
        };
        var dokum = PosHesap.BlokeKanallar(kalemler, G(24));

        Assert.Equal([new PosKanalBloke("TOPTAN", 980m + 495m, 2), new PosKanalBloke("MEZAT", 980m, 1), new PosKanalBloke("Kanalsız", 295.50m, 1)], dokum);
        var (net, adet) = PosHesap.Bloke(kalemler, G(24));
        Assert.Equal((net, adet), (dokum.Sum(d => d.Net), dokum.Sum(d => d.Adet)));
        Assert.Empty(PosHesap.BlokeKanallar(kalemler, G(30)));   // hepsinin valörü geçti
        Assert.Empty(PosHesap.BlokeKanallar([], G(24)));
    }
}
