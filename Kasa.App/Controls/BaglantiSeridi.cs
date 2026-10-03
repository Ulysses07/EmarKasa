using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.Controls;

namespace Kasa.App.Controls;

/// <summary>
/// Kabuğun bağlantı şeridi (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3): bağlam BaglantiDurumu'dur; yalnız
/// kopukken görünür ve "Sunucuya ulaşılamıyor · Son bağlantı 14:05 · Yeniden dene" yazar. Kabuk onu Shell.TitleView olarak
/// bütün sayfaların gezinme çubuğuna koyar (tek şerit). "Yeniden dene" <see cref="YenidenDeneIstendi"/>'yi bildirir; kabuk açık
/// sayfayı yeniler. Şerit çıktığında (K-3) durum metni ekran okuyucuya duyurulur; ayrı bir HeadingLevel gerekmez (duyuru
/// zaten yeterli).
/// </summary>
public class BaglantiSeridi : ContentView
{
    private readonly Label _metin;
    private readonly Button _dugme;

    public event EventHandler? YenidenDeneIstendi;

    /// <summary>Ekran okuyucu duyurusu (varsayılan SemanticScreenReader.Announce); testler değiştirebilir (K-3).</summary>
    internal Action<string> Duyur { get; set; } = s => SemanticScreenReader.Default.Announce(s);

    public BaglantiSeridi()
    {
        _metin = new Label { Style = (Style)Application.Current!.Resources["LblError"], VerticalOptions = LayoutOptions.Center };
        _metin.SetBinding(Label.TextProperty, "SeritMetni");
        _dugme = new Button { Text = "Yeniden dene", Style = (Style)Application.Current!.Resources["BtnSecondary"], VerticalOptions = LayoutOptions.Center };
        _dugme.Clicked += (_, _) => YenidenDeneIstendi?.Invoke(this, EventArgs.Empty);
        Content = new Border
        {
            Style = (Style)Application.Current!.Resources["ErrorBox"],
            Padding = new Thickness(12, 4),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout { Spacing = 12, Children = { _metin, _dugme } },
        };
        this.SetBinding(IsVisibleProperty, "Kopuk");
        this.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible) && IsVisible)
                Duyur(_metin.Text ?? "");
        };
    }

    /// <summary>Yenileme sürerken düğme kapalıdır (çift tıklama ikinci yenilemeyi başlatmaz).</summary>
    public bool Yenileniyor { set => _dugme.IsEnabled = !value; }
}

/// <summary>Kabuğun "Yeniden dene" ve bağlantı geri gelince yenileyebildiği sayfa.</summary>
public interface IYenilenebilir
{
    Task YenileAsync();
}
