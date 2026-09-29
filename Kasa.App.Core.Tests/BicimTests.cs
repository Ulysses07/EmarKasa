namespace Kasa.App.Core.Tests;

public class BicimTests
{
    [Fact]
    public void Tl_binlik_nokta_kurus_virgul()
        => Assert.Equal("1.308.800,00", Bicim.Tl(1308800m));

    [Fact]
    public void Tl_negatifi_biciminde_gosterir()
        => Assert.Equal("-48.200,00", Bicim.Tl(-48200m));

    [Fact]
    public void ImzaliTl_pozitife_arti_koyar()
        => Assert.Equal("+145.000,00", Bicim.ImzaliTl(145000m));

    [Fact]
    public void ImzaliTl_negatife_eksi_koyar()
        => Assert.Equal("-48.200,00", Bicim.ImzaliTl(-48200m));

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(12_595L, "12,3 KB")]
    [InlineData(3L * 1024 * 1024, "3 MB")]
    [InlineData(157_286_400L, "150 MB")]
    public void Boyut_dosya_buyuklugunu_okunur_yazar(long bayt, string beklenen)
        => Assert.Equal(beklenen, Bicim.Boyut(bayt));

    [Fact]
    public void Donem_etiketi_filtrede_yilsiz_gelir_formunda_yillidir()
    {
        // Yıl sınırını aşan dönem: gelir formu yanlış yılın dönem toplamını ezmesin diye yılı gösterir.
        var donem = new Kasa.ApiClient.DonemDto(new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 4), 2025, 12);
        Assert.Equal("29 Ara – 04 Oca", Bicim.Donem(donem));
        Assert.Equal("29 Ara 2025 – 04 Oca 2026", Bicim.Donem(donem, yilli: true));
    }
}
