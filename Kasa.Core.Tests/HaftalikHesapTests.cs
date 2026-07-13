using Kasa.Core;

namespace Kasa.Core.Tests;

public class HaftalikHesapTests
{
    private static readonly Kanal[] IkiKanal =
    {
        new("MEZAT", AcilisDevri: 1000m),
        new("TOPTAN", AcilisDevri: 0m),
    };

    [Fact]
    public void Kanal_sonucu_gelen_eksi_cari_giden()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 21));
        var gelenler = new[] { new Gelen(new DateOnly(2026, 6, 15), "MEZAT", 500m) };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 16), "A", 200m, "MEZAT", GiderTipi.Cari),
            // SabitGider kanal devrine GİRMEMELİ:
            new Islem(new DateOnly(2026, 6, 16), "SGK", 999m, "MEZAT", GiderTipi.SabitGider),
        };

        var ozetler = HesapMotoru.HaftalikHesapla(
            kasaAcilisDevri: 0m, IkiKanal, islemler, gelenler, donemler);

        var mezat = ozetler[0].Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(500m, mezat.Gelen);
        Assert.Equal(200m, mezat.Giden);          // yalnız Cari
        Assert.Equal(300m, mezat.Sonuc);          // 500 - 200
        Assert.Equal(1300m, mezat.Devir);         // açılış 1000 + 300
    }

    [Fact]
    public void Kasa_devri_tum_gidenleri_sayar_ve_zincirlenir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 28));
        var gelenler = new[]
        {
            new Gelen(new DateOnly(2026, 6, 15), "MEZAT", 500m),
            new Gelen(new DateOnly(2026, 6, 22), "MEZAT", 100m),
        };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 16), "A", 200m, "MEZAT", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 16), "SGK", 50m, "MEZAT", GiderTipi.SabitGider),
            new Islem(new DateOnly(2026, 6, 23), "Kira", 30m, Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var ozetler = HesapMotoru.HaftalikHesapla(
            kasaAcilisDevri: 2000m, IkiKanal, islemler, gelenler, donemler);

        // 1. dönem: gelen 500, giden 250 (200+50) → sonuç 250 → devir 2250
        Assert.Equal(500m, ozetler[0].ToplamGelen);
        Assert.Equal(250m, ozetler[0].ToplamGiden);
        Assert.Equal(2250m, ozetler[0].KasaDevir);
        // 2. dönem: gelen 100, giden 30 (ortak) → sonuç 70 → devir 2320
        Assert.Equal(100m, ozetler[1].ToplamGelen);
        Assert.Equal(30m, ozetler[1].ToplamGiden);
        Assert.Equal(2320m, ozetler[1].KasaDevir);
    }
}
