using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Kasa.App.Controls;

/// <summary>
/// Form alanı (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §1): başlık, çerçeve içinde girdi (<see cref="Icerik"/>)
/// ve hata varken alanın altında ileti. Hata varken çerçeve kırmızıdır (Neg) ve ekran okuyucu girdinin adına iletiyi ekler
/// ("Tutar. Tutar sıfırdan büyük olmalı."). <see cref="Alan"/> görünüm modelindeki alanın adıdır: kaydırma yardımcısı ilk hatalı
/// alanı bununla bulur. <see cref="Cerceveli"/> girdiyi FieldBorder içine alır (XAML sayfalarının metin, tutar ve tarih alanları);
/// çerçevesiz alanda (çip grupları, kodla yazılmış takip sayfaları) çerçeve yalnız hata varken (kırmızı) görünür. Başlık XAML
/// ailesinde LblField, <see cref="TakipStili"/> ile takip ailesinde LblTakipKucuk'tur (TakipUi.Alan).
/// </summary>
[ContentProperty(nameof(Icerik))]
public class FormAlani : ContentView
{
    public static readonly BindableProperty BaslikProperty = Ozellik(nameof(Baslik), typeof(string), null);
    public static readonly BindableProperty HataProperty = Ozellik(nameof(Hata), typeof(string), null);
    public static readonly BindableProperty AlanProperty = BindableProperty.Create(nameof(Alan), typeof(string), typeof(FormAlani));
    public static readonly BindableProperty IcerikProperty = Ozellik(nameof(Icerik), typeof(View), null);
    public static readonly BindableProperty CerceveliProperty = Ozellik(nameof(Cerceveli), typeof(bool), true);
    public static readonly BindableProperty TakipStiliProperty = Ozellik(nameof(TakipStili), typeof(bool), false);

    public string? Baslik { get => (string?)GetValue(BaslikProperty); set => SetValue(BaslikProperty, value); }
    public string? Hata { get => (string?)GetValue(HataProperty); set => SetValue(HataProperty, value); }
    /// <summary>Görünüm modelindeki alanın adı (AlanHatalari anahtarı, ör. "DuzenCari").</summary>
    public string? Alan { get => (string?)GetValue(AlanProperty); set => SetValue(AlanProperty, value); }
    public View? Icerik { get => (View?)GetValue(IcerikProperty); set => SetValue(IcerikProperty, value); }
    public bool Cerceveli { get => (bool)GetValue(CerceveliProperty); set => SetValue(CerceveliProperty, value); }
    /// <summary>Kodla yazılmış takip sayfalarının başlık stili (LblTakipKucuk) ve aralığı (5).</summary>
    public bool TakipStili { get => (bool)GetValue(TakipStiliProperty); set => SetValue(TakipStiliProperty, value); }

    private readonly VerticalStackLayout _yigin;
    private readonly Label _baslik;
    private readonly Border _cerceve;
    private readonly Label _hata;

    public FormAlani()
    {
        _baslik = new Label();
        _cerceve = new Border();
        _hata = new Label { Style = (Style)Application.Current!.Resources["LblError"], Margin = new Thickness(0, 4, 0, 0) };
        _yigin = new VerticalStackLayout { Children = { _baslik, _cerceve, _hata } };
        Content = _yigin;
        Guncelle();
    }

    private static BindableProperty Ozellik(string ad, Type tur, object? varsayilan)
        => BindableProperty.Create(ad, tur, typeof(FormAlani), varsayilan, propertyChanged: (s, _, _) => ((FormAlani)s).Guncelle());

    /// <summary>Hata varsa girdiye odaklanır (kaydırma yardımcısı ilk hatalı alana gidince).</summary>
    public bool Odaklan() => Icerik?.Focus() == true;

    private void Guncelle()
    {
        var hataVar = !string.IsNullOrWhiteSpace(Hata);
        _yigin.Spacing = TakipStili ? 5 : 0;
        _baslik.Style = (Style)(TakipStili ? Application.Current!.Resources["LblTakipKucuk"] : Application.Current!.Resources["LblField"]);
        _baslik.Text = Baslik;
        _baslik.IsVisible = !string.IsNullOrEmpty(Baslik);
        if (Cerceveli)
        {
            foreach (var ozellik in new[] { Border.PaddingProperty, Border.StrokeThicknessProperty, Border.StrokeShapeProperty })
                _cerceve.ClearValue(ozellik);
            _cerceve.Style = (Style)Application.Current!.Resources["FieldBorder"];
            if (hataVar)
                _cerceve.Stroke = (Brush)Application.Current!.Resources["BrushNeg"];
            else
                _cerceve.ClearValue(Border.StrokeProperty);
        }
        else
        {
            _cerceve.ClearValue(StyleProperty);
            _cerceve.Padding = 0;
            _cerceve.StrokeThickness = 1;
            _cerceve.StrokeShape = new RoundRectangle { CornerRadius = 6 };
            _cerceve.Stroke = hataVar ? (Brush)Application.Current!.Resources["BrushNeg"] : Brush.Transparent;
        }
        _cerceve.Content = Icerik;
        _hata.Text = Hata;
        _hata.IsVisible = hataVar;
        if (Icerik is null)
            return;
        // K-2: girdinin ekran okuyucu açıklaması her zaman başlıktır; hata varken "Başlık. İleti" olur. Hata kalkınca
        // ClearValue ile silinmez, başlığa döner (hata yokken de başlık okunsun).
        SemanticProperties.SetDescription(Icerik, hataVar ? (string.IsNullOrEmpty(Baslik) ? Hata : $"{Baslik}. {Hata}") : Baslik);
    }
}
