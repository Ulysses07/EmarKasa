using Kasa.Core;

namespace Kasa.Core.Tests;

public class KartHesapTests
{
    [Fact]
    public void Acilis_arti_harcama_eksi_odeme()
    {
        var borc = KartHesap.GuncelBorc(acilisBorc: 1000m, harcamaToplam: 500m, odemeToplam: 200m);
        Assert.Equal(1300m, borc);
    }

    [Fact]
    public void Harcama_ve_odeme_yoksa_acilis_kalir()
    {
        Assert.Equal(1000m, KartHesap.GuncelBorc(1000m, 0m, 0m));
    }

    [Fact]
    public void Odeme_harcamayi_asarsa_negatif_olabilir()
    {
        Assert.Equal(-50m, KartHesap.GuncelBorc(100m, 0m, 150m));
    }
}
