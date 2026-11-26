namespace Kasa.App.Controls;

/// <summary>Aşamalı gösterim (docs/specs/2026-11-26-slop-temizligi.md §2): uzun kural açıklaması varsayılan olarak gizlidir, küçük
/// "Ayrıntılar" bağlantısıyla açılır ve kapanır. Ekranda yalnız gerektiğinde okunan bilgi paragraf olarak durmaz. XAML'da
/// <c>&lt;ctl:Ayrinti Metin="…" /&gt;</c>, kodda <c>TakipUi.Yardim(…)</c>.</summary>
public class Ayrinti : VerticalStackLayout
{
    public static readonly BindableProperty MetinProperty = BindableProperty.Create(nameof(Metin), typeof(string), typeof(Ayrinti), "",
        propertyChanged: (b, _, yeni) => ((Ayrinti)b)._aciklama.Text = yeni as string);

    public string Metin { get => (string)GetValue(MetinProperty); set => SetValue(MetinProperty, value); }

    private readonly Label _aciklama;

    public Ayrinti()
    {
        Spacing = 4;
        _aciklama = new Label { Style = (Style)Application.Current!.Resources["LblTakipMetin"], IsVisible = false };
        var dugme = new Button { Text = "Ayrıntılar", Style = (Style)Application.Current!.Resources["BtnLink"] };
        SemanticProperties.SetHint(dugme, "Açıklamayı gösterir ya da gizler");
        dugme.Clicked += (_, _) =>
        {
            _aciklama.IsVisible = !_aciklama.IsVisible;
            dugme.Text = _aciklama.IsVisible ? "Ayrıntıları gizle" : "Ayrıntılar";
        };
        Children.Add(dugme);
        Children.Add(_aciklama);
    }
}
