using Kasa.App.Core;

namespace Kasa.App.Core.Tests;

public class KartTarihTests
{
    [Fact]
    public void OncekiGun_ay_icinde() =>
        Assert.Equal(new DateOnly(2026,7,15), KartTarih.OncekiGun(15, new DateOnly(2026,7,20)));

    [Fact]
    public void OncekiGun_gecmemisse_onceki_ay() =>
        Assert.Equal(new DateOnly(2026,6,25), KartTarih.OncekiGun(25, new DateOnly(2026,7,10)));

    [Fact]
    public void SonrakiGun_ay_icinde() =>
        Assert.Equal(new DateOnly(2026,7,22), KartTarih.SonrakiGun(22, new DateOnly(2026,7,15)));

    [Fact]
    public void SonrakiGun_gecmisse_sonraki_ay() =>
        Assert.Equal(new DateOnly(2026,8,5), KartTarih.SonrakiGun(5, new DateOnly(2026,7,15)));

    [Fact]
    public void SonrakiGun_kisa_ayda_kirpar() =>
        Assert.Equal(new DateOnly(2026,2,28), KartTarih.SonrakiGun(31, new DateOnly(2026,2,1)));
}
