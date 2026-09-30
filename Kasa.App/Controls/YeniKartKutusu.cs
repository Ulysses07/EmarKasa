using System.Windows.Input;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Kasa.App.Controls;

/// <summary>Kart ızgarasının sonundaki kesik çizgili "Yeni kart ekle" kutusu (yalnız editörde görünür; KartIzgarasi.YeniGorunur).
/// Tıklama yüzeyi KartKutusu'ndaki gibi şeffaf düğmedir (UI Otomasyonu'nda "Yeni kart ekle"; form açıkken "Form açık"
/// ipucu); yazılar girdiyi geçirir ve erişilebilirlik ağacında ayrı öğe değildir.</summary>
public class YeniKartKutusu : Border
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(YeniKartKutusu),
        propertyChanged: (b, _, y) => ((YeniKartKutusu)b)._dugme.Command = (ICommand?)y);

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    /// <summary>Yeni kart formu açıkken düğmeye yazılan UI Otomasyonu ipucu.</summary>
    public const string SeciliIpucu = "Form açık";

    private readonly Button _dugme;
    private bool _secili;

    public YeniKartKutusu()
    {
        StrokeShape = new RoundRectangle { CornerRadius = 14 };
        StrokeDashArray = new DoubleCollection { 4, 3 };
        MinimumHeightRequest = 120;
        BackgroundColor = Colors.Transparent;
        _dugme = new Button { Style = (Style)Application.Current!.Resources["SeffafDugme"] };
        SemanticProperties.SetDescription(_dugme, "Yeni kart ekle");
        var yesil = (Color)Application.Current!.Resources["Green"];
        var yazi = new VerticalStackLayout
        {
            Spacing = 2,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Yazi(new Label { Text = "+", FontSize = 26, HorizontalTextAlignment = TextAlignment.Center, TextColor = yesil }),
                Yazi(new Label { Text = "Yeni kart ekle", FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, TextColor = yesil }),
            },
        };
        var kok = new Grid();
        kok.Add(_dugme);
        kok.Add(yazi);
        Content = kok;
        Cerceve();
    }

    /// <summary>Yeni kart formu açık: kenar yeşil ve kalın.</summary>
    public bool Secili
    {
        get => _secili;
        set
        {
            _secili = value;
            Cerceve();
        }
    }

    private void Cerceve()
    {
        Stroke = new SolidColorBrush(_secili ? (Color)Application.Current!.Resources["Green"] : (Color)Application.Current!.Resources["FieldStroke"]);
        StrokeThickness = _secili ? 2.5 : 1.5;
        if (_secili)
            SemanticProperties.SetHint(_dugme, SeciliIpucu);
        else
            _dugme.ClearValue(SemanticProperties.HintProperty);
    }

    private static Label Yazi(Label etiket)
    {
        etiket.InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(etiket, false);
        return etiket;
    }
}
