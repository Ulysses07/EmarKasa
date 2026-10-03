using Kasa.App.Controls;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Kabuğun bağlantı şeridi: yalnız kopukken görünür, son bağlantı saatini yazar, "Yeniden dene" kabuğa bildirilir.</summary>
public class BaglantiSeridiTests
{
    [Fact]
    public void Serit_yalniz_kopukken_gorunur_metni_ve_yeniden_dene_bildirimi()
    {
        GorunumOrtami.Kur();
        var durum = new BaglantiDurumu(zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 10, 2)));
        var serit = new BaglantiSeridi { BindingContext = durum };
        var istek = 0;
        serit.YenidenDeneIstendi += (_, _) => istek++;
        var metin = serit.GetVisualTreeDescendants().OfType<Label>().Single();
        var dugme = serit.GetVisualTreeDescendants().OfType<Button>().Single();

        Assert.False(serit.IsVisible);

        durum.Ulasildi();
        durum.Ulasilamadi();
        Assert.True(serit.IsVisible);
        Assert.Equal("Sunucuya ulaşılamıyor · Son bağlantı 12:00", metin.Text);
        Assert.Equal("Yeniden dene", dugme.Text);
        ((IButtonController)dugme).SendClicked();
        Assert.Equal(1, istek);

        serit.Yenileniyor = true;
        Assert.False(dugme.IsEnabled);
        serit.Yenileniyor = false;
        durum.Ulasildi();
        Assert.False(serit.IsVisible);
    }
}
