using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Kasa.App.Controls;

/// <summary>Boş liste görünümü (Kasalar, Haftalık, Aylık ve İşlemler listelerinin EmptyView'ının tek kopyası): ortada ₺
/// simgesi, kalın başlık ve açıklama.</summary>
public class BosDurum : VerticalStackLayout
{
    public static readonly BindableProperty BaslikProperty = BindableProperty.Create(nameof(Baslik), typeof(string), typeof(BosDurum),
        propertyChanged: (s, _, y) => ((BosDurum)s)._baslik.Text = (string?)y);
    public static readonly BindableProperty AciklamaProperty = BindableProperty.Create(nameof(Aciklama), typeof(string), typeof(BosDurum),
        propertyChanged: (s, _, y) => ((BosDurum)s)._aciklama.Text = (string?)y);

    public string? Baslik { get => (string?)GetValue(BaslikProperty); set => SetValue(BaslikProperty, value); }
    public string? Aciklama { get => (string?)GetValue(AciklamaProperty); set => SetValue(AciklamaProperty, value); }

    private readonly Label _baslik;
    private readonly Label _aciklama;

    public BosDurum()
    {
        Padding = new Thickness(20, 36, 20, 40);
        Spacing = 6;
        Children.Add(new Border
        {
            Style = (Style)Application.Current!.Resources["AvatarChip"],
            WidthRequest = 40,
            HeightRequest = 40,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            HorizontalOptions = LayoutOptions.Center,
            Content = new Label { Text = "₺", Style = (Style)Application.Current!.Resources["LblAvatar"], FontSize = 17 },
        });
        _baslik = new Label { Style = (Style)Application.Current!.Resources["LblEmptyTitle"], Margin = new Thickness(0, 4, 0, 0) };
        _aciklama = new Label { Style = (Style)Application.Current!.Resources["LblEmptySub"] };
        Children.Add(_baslik);
        Children.Add(_aciklama);
    }
}
