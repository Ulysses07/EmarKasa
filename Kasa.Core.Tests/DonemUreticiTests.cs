using Kasa.Core;

namespace Kasa.Core.Tests;

public class DonemUreticiTests
{
    [Fact]
    public void Pazartesi_baslayan_tam_haftalar_ve_ay_sonu_bolunmesi()
    {
        // 15 Haziran 2026 = Pazartesi
        var donemler = DonemUretici.Uret(
            baslangic: new DateOnly(2026, 6, 15),
            bitis: new DateOnly(2026, 7, 12));

        var beklenen = new (int, int, int, int)[]
        {
            (6, 15, 6, 21), // Pzt-Paz
            (6, 22, 6, 28),
            (6, 29, 6, 30), // ay sonunda kesildi
            (7, 1,  7, 5),  // aynı haftanın kalanı
            (7, 6,  7, 12),
        };

        Assert.Equal(beklenen.Length, donemler.Count);
        for (int i = 0; i < beklenen.Length; i++)
        {
            var (sa, sg, ea, eg) = beklenen[i];
            Assert.Equal(new DateOnly(2026, sa, sg), donemler[i].Start);
            Assert.Equal(new DateOnly(2026, ea, eg), donemler[i].End);
        }
    }

    [Fact]
    public void Hafta_ortasi_baslangic_kismi_ilk_donem_uretir()
    {
        // 17 Haziran 2026 = Çarşamba → ilk dönem 17-21 (kısmi)
        var donemler = DonemUretici.Uret(
            baslangic: new DateOnly(2026, 6, 17),
            bitis: new DateOnly(2026, 6, 28));

        Assert.Equal(new DateOnly(2026, 6, 17), donemler[0].Start);
        Assert.Equal(new DateOnly(2026, 6, 21), donemler[0].End);
        Assert.Equal(new DateOnly(2026, 6, 22), donemler[1].Start);
        Assert.Equal(new DateOnly(2026, 6, 28), donemler[1].End);
    }
}
