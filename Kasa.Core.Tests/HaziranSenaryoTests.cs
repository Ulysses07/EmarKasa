using Kasa.Core;

namespace Kasa.Core.Tests;

public class HaziranSenaryoTests
{
    // Ekran görüntüsündeki 29-30 Haziran dönemi (image 1).
    // Kanal açılış devirleri = "GEÇEN HAFTA DEVİR", kasa açılışı = 2.907.053,21.
    private static readonly Kanal[] Kanallar3 =
    {
        new("MEZAT",     AcilisDevri: 4_991_052m),
        new("PERAKENDE", AcilisDevri: 2_013_516m),
        new("TOPTAN",    AcilisDevri: 619_647m),
    };

    [Fact]
    public void Haziran_29_30_donemi_excel_rakamlarini_uretir()
    {
        var start = new DateOnly(2026, 6, 29); // Pazartesi
        var donemler = DonemUretici.Uret(start, new DateOnly(2026, 6, 30)); // tek dönem 29-30

        var gelenler = new[]
        {
            new Gelen(start, "MEZAT",     289_425m),
            new Gelen(start, "PERAKENDE", 271_006m),
            new Gelen(start, "TOPTAN",    207_000m),
        };

        // Kanal-bazlı Cari giden toplamları (screenshot):
        // MEZAT 1.308.800, PERAKENDE 1.221.374, TOPTAN 360.000
        // Ayrıca kanalsız/Ortak giderler: TOPLAM GİDEN 3.345.495 - 2.890.174 = 455.321
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 29), "MEZAT-cari",     1_308_800m, "MEZAT",     GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 29), "PERAKENDE-cari", 1_221_374m, "PERAKENDE", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 29), "TOPTAN-cari",    360_000m,   "TOPTAN",    GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 30), "SGK/Vergi/vb.",  455_321m,   Kanallar.Ortak, GiderTipi.SabitGider),
        };

        var ozetler = HesapMotoru.HaftalikHesapla(2_907_053.21m, Kanallar3, islemler, gelenler, donemler);
        var d = ozetler.Single();

        var mezat = d.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(-1_019_375m, mezat.Sonuc);   // 289.425 - 1.308.800
        Assert.Equal(3_971_677m, mezat.Devir);     // 4.991.052 - 1.019.375

        Assert.Equal(767_431m, d.ToplamGelen);
        Assert.Equal(3_345_495m, d.ToplamGiden);
        Assert.Equal(328_989.21m, d.KasaDevir);    // 2.907.053,21 + 767.431 - 3.345.495
    }
}
