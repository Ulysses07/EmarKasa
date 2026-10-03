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
        var serit = new BaglantiSeridi { BindingContext = durum, Duyur = _ => { } };
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

    /// <summary>K-3: şerit çıktığında (bağlantı koptuğunda) ekran okuyucuya duyurulur; durum metninde artık HeadingLevel yok
    /// (duyuru zaten yeterli).</summary>
    [Fact]
    public void Serit_ciktiginda_ekran_okuyucuya_duyurulur_headinglevel_yok()
    {
        GorunumOrtami.Kur();
        var durum = new BaglantiDurumu(zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 10, 2)));
        var serit = new BaglantiSeridi { BindingContext = durum };
        var metin = serit.GetVisualTreeDescendants().OfType<Label>().Single();
        var duyurular = new List<string>();
        serit.Duyur = duyurular.Add;

        Assert.Equal(SemanticHeadingLevel.None, SemanticProperties.GetHeadingLevel(metin));

        durum.Ulasildi();
        durum.Ulasilamadi();
        Assert.Equal([metin.Text], duyurular);

        // Yeniden gelip gidince (her çıkışta) tekrar duyurulur; görünürken duyurulmaz (IsVisible zaten true'dan true'ya geçmez).
        durum.Ulasildi();
        durum.Ulasilamadi();
        Assert.Equal(2, duyurular.Count);
    }
}
