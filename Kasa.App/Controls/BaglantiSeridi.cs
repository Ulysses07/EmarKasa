using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;

namespace Kasa.App.Controls;

/// <summary>
/// Kabuğun bağlantı şeridi (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3): bağlam BaglantiDurumu'dur; yalnız
/// kopukken görünür ve "Sunucuya ulaşılamıyor · Son bağlantı 14:05 · Yeniden dene" yazar. Kabuk onu Shell.TitleView olarak
/// bütün sayfaların gezinme çubuğuna koyar (tek şerit). "Yeniden dene" <see cref="YenidenDeneIstendi"/>'yi bildirir; kabuk açık
/// sayfayı yeniler.
/// </summary>
public class BaglantiSeridi : ContentView
{
    private readonly Button _dugme;

    public event EventHandler? YenidenDeneIstendi;

    public BaglantiSeridi()
    {
        var metin = new Label { Style = (Style)Application.Current!.Resources["LblError"], VerticalOptions = LayoutOptions.Center };
        metin.SetBinding(Label.TextProperty, "SeritMetni");
        _dugme = new Button { Text = "Yeniden dene", Style = (Style)Application.Current!.Resources["BtnSecondary"], VerticalOptions = LayoutOptions.Center };
        _dugme.Clicked += (_, _) => YenidenDeneIstendi?.Invoke(this, EventArgs.Empty);
        Content = new Border
        {
            Style = (Style)Application.Current!.Resources["ErrorBox"],
            Padding = new Thickness(12, 4),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout { Spacing = 12, Children = { metin, _dugme } },
        };
        SemanticProperties.SetHeadingLevel(metin, SemanticHeadingLevel.Level2);
        this.SetBinding(IsVisibleProperty, "Kopuk");
    }

    /// <summary>Yenileme sürerken düğme kapalıdır (çift tıklama ikinci yenilemeyi başlatmaz).</summary>
    public bool Yenileniyor { set => _dugme.IsEnabled = !value; }
}

/// <summary>Kabuğun "Yeniden dene" ve bağlantı geri gelince yenileyebildiği sayfa.</summary>
public interface IYenilenebilir
{
    Task YenileAsync();
}
