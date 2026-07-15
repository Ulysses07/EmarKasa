using Kasa.Core;

namespace Kasa.Core.Tests;

public class KartDonemTests
{
    [Fact]
    public void SonKesim_ay_icinde_gecmis_gunu_dondurur()
        => Assert.Equal(new DateOnly(2026, 7, 15),
            KartDonem.SonKesim(15, new DateOnly(2026, 7, 20)));

    [Fact]
    public void SonKesim_gun_gelmediyse_onceki_aya_gider()
        => Assert.Equal(new DateOnly(2026, 6, 25),
            KartDonem.SonKesim(25, new DateOnly(2026, 7, 10)));

    [Fact]
    public void SonKesim_kisa_ayda_ay_sonuna_kirpar()
        => Assert.Equal(new DateOnly(2026, 2, 28),
            KartDonem.SonKesim(31, new DateOnly(2026, 3, 1)));

    [Fact]
    public void SonKesim_ocakta_onceki_yila_sarar()
        => Assert.Equal(new DateOnly(2025, 12, 20),
            KartDonem.SonKesim(20, new DateOnly(2026, 1, 5)));
}
