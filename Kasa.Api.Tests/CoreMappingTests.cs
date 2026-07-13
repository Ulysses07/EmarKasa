using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api.Tests;

public class CoreMappingTests
{
    [Fact]
    public void IslemEntity_core_islem_e_donusur()
    {
        var e = new IslemEntity
        {
            Id = 5,
            Tarih = new DateOnly(2026, 6, 30),
            Cari = "PORT KARGO",
            TutarTl = 3874.03m,
            Kanal = "MEZAT",
            Tip = GiderTipi.Cari,
            Not = null,
        };

        Islem core = e.ToCore();

        Assert.Equal(new DateOnly(2026, 6, 30), core.Tarih);
        Assert.Equal("PORT KARGO", core.Cari);
        Assert.Equal(3874.03m, core.TutarTl);
        Assert.Equal("MEZAT", core.Kanal);
        Assert.Equal(GiderTipi.Cari, core.Tip);
    }

    [Fact]
    public void KanalEntity_ve_GelenEntity_core_e_donusur()
    {
        var k = new KanalEntity { Id = 1, Ad = "TOPTAN", Aktif = false, Sira = 2, AcilisDevri = 100m };
        Kanal ck = k.ToCore();
        Assert.Equal("TOPTAN", ck.Ad);
        Assert.False(ck.Aktif);
        Assert.Equal(2, ck.Sira);
        Assert.Equal(100m, ck.AcilisDevri);

        var g = new GelenEntity { Id = 3, DonemStart = new DateOnly(2026, 6, 29), Kanal = "MEZAT", TutarTl = 289_425m };
        Gelen cg = g.ToCore();
        Assert.Equal(new DateOnly(2026, 6, 29), cg.DonemStart);
        Assert.Equal("MEZAT", cg.Kanal);
        Assert.Equal(289_425m, cg.TutarTl);
    }
}
