using Kasa.Core;

namespace Kasa.Core.Tests;

public class AylikHesapTests
{
    private static readonly Kanal[] UcKanal =
    {
        new("MEZAT"), new("PERAKENDE"), new("TOPTAN"),
    };

    [Fact]
    public void Ay_sonucu_tum_bilesenleri_ve_ortak_payini_dusiyor()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var ilkDonemStart = donemler[0].Start; // 1 Haziran (Pazartesi)

        var gelenler = new[] { new Gelen(ilkDonemStart, "MEZAT", 1000m) };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 3), "Tedarik", 100m, "MEZAT", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 4), "Maaş",    200m, "MEZAT", GiderTipi.SabitGider),
            new Islem(new DateOnly(2026, 6, 5), "K.K",     50m,  "MEZAT", GiderTipi.KrediKarti),
            new Islem(new DateOnly(2026, 6, 6), "Kira",    300m, Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var rapor = HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, gelenler, donemler);
        var mezat = rapor.Kanallar.Single(k => k.Kanal == "MEZAT");

        Assert.Equal(1000m, mezat.Gelen);
        Assert.Equal(100m, mezat.CariGiden);
        Assert.Equal(200m, mezat.SabitGider);
        Assert.Equal(50m, mezat.KrediKarti);
        Assert.Equal(100m, mezat.OrtakPay);            // 300 / 3 aktif kanal
        // 1000 - 100 - 200 - 50 - 100 = 550
        Assert.Equal(550m, mezat.AySonucu);
    }

    [Fact]
    public void Ortak_pay_yalniz_aktif_kanal_sayisina_bolunur()
    {
        var kanallar = new[]
        {
            new Kanal("MEZAT"), new Kanal("PERAKENDE"),
            new Kanal("ESKI", Aktif: false),   // pasif → bölmeye dahil değil
        };
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 6), "Kira", 300m, Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var rapor = HesapMotoru.AylikHesapla(2026, 6, kanallar, islemler, Array.Empty<Gelen>(), donemler);
        Assert.Equal(150m, rapor.Kanallar.Single(k => k.Kanal == "MEZAT").OrtakPay); // 300/2
    }
}
