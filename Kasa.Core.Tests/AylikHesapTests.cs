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
            new Islem(new DateOnly(2026, 5, 5), "K.K",     50m,  "MEZAT", GiderTipi.KrediKarti), // önceki ay → Haziran'a ertelenir
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

    [Fact]
    public void Tam_bolunmeyen_ortak_kurus_artigi_ilk_aktif_kanallara_dagilir()
    {
        // 455.321 / 3 = 151.773,666... → kuruş bazında 45.532.100 / 3 = 15.177.366 taban, 2 artık.
        // İlk 2 aktif kanal 151.773,67; üçüncü 151.773,66; toplam tam 455.321,00.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 6), "SGK/Vergi", 455_321m, Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var rapor = HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, Array.Empty<Gelen>(), donemler);

        Assert.Equal(151_773.67m, rapor.Kanallar.Single(k => k.Kanal == "MEZAT").OrtakPay);
        Assert.Equal(151_773.67m, rapor.Kanallar.Single(k => k.Kanal == "PERAKENDE").OrtakPay);
        Assert.Equal(151_773.66m, rapor.Kanallar.Single(k => k.Kanal == "TOPTAN").OrtakPay);
        // Dağıtılan pay toplamı ortak toplamı tam mutabık.
        Assert.Equal(455_321m, rapor.Kanallar.Sum(k => k.OrtakPay));
    }

    [Fact]
    public void Haftalik_kasa_sonucu_toplami_aylik_ay_sonucu_toplamina_esittir()
    {
        // Tam bölünmeyen ortak gider olsa bile Σ haftalık KasaSonucu == Σ aylık AySonucu.
        // Bu ancak ortak payı kuruş bazında tam dağıtıldığında (kuruş sızıntısı olmadan) sağlanır.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var ilkDonemStart = donemler[0].Start;
        var ikinciDonemStart = donemler[1].Start;

        var gelenler = new[]
        {
            new Gelen(ilkDonemStart, "MEZAT", 289_425m),
            new Gelen(ilkDonemStart, "PERAKENDE", 271_006m),
            new Gelen(ilkDonemStart, "TOPTAN", 207_000m),
            new Gelen(ikinciDonemStart, "MEZAT", 130_000m),
            new Gelen(ikinciDonemStart, "PERAKENDE", 95_500m),
        };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 3), "MEZAT-cari", 1_308_800m, "MEZAT", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 4), "PER-cari", 1_221_374m, "PERAKENDE", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 5), "TOP-cari", 360_000m, "TOPTAN", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 9), "MEZAT-maaş", 200_000m, "MEZAT", GiderTipi.SabitGider),
            new Islem(new DateOnly(2026, 6, 10), "PER-kk", 45_500m, "PERAKENDE", GiderTipi.KrediKarti),
            new Islem(new DateOnly(2026, 6, 6), "Ortak SGK", 455_321m, Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var haftalik = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, gelenler, donemler);
        decimal haftalikToplam = haftalik.Sum(o => o.KasaSonucu);

        var aylik = HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, gelenler, donemler);
        decimal aylikToplam = aylik.Kanallar.Sum(k => k.AySonucu);

        Assert.Equal(haftalikToplam, aylikToplam);
    }
}
