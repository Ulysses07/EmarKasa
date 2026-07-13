using Kasa.Core;

namespace Kasa.Core.Tests;

public class DomainTests
{
    [Fact]
    public void Islem_alanlari_dogru_kurulur()
    {
        var islem = new Islem(
            Tarih: new DateOnly(2026, 6, 30),
            Cari: "PORT KARGO",
            TutarTl: 3874.03m,
            Kanal: "MEZAT",
            Tip: GiderTipi.Cari,
            Not: null);

        Assert.Equal("MEZAT", islem.Kanal);
        Assert.Equal(GiderTipi.Cari, islem.Tip);
        Assert.Equal(3874.03m, islem.TutarTl);
    }

    [Fact]
    public void Ortak_kanal_sabiti_dogru()
    {
        Assert.Equal("Ortak", Kanallar.Ortak);
    }
}
