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

    [Fact]
    public void KanalRengi_bilinen_kanali_dondurur()
        => Assert.Equal("#C98A12", Bicim.KanalRengi("MEZAT"));

    [Fact]
    public void KanalRengi_bilinmeyene_ortak_rengi()
        => Assert.Equal("#7A828E", Bicim.KanalRengi("BILINMEYEN"));

    [Fact]
    public void Donem_etiketi_filtrede_yilsiz_gelir_formunda_yillidir()
    {
        // Yıl sınırını aşan dönem: gelir formu yanlış yılın dönem toplamını ezmesin diye yılı gösterir.
        var donem = new Kasa.ApiClient.DonemDto(new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 4), 2025, 12);
        Assert.Equal("29 Ara – 04 Oca", Bicim.Donem(donem));
        Assert.Equal("29 Ara 2025 – 04 Oca 2026", Bicim.Donem(donem, yilli: true));
    }
}
