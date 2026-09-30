using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Dispatching;

namespace Kasa.App.Core.Tests;

/// <summary>Görünüm eşdeğerliği testlerinin ortamı: Kasa.App'in Colors.xaml, Styles.xaml ve App.xaml dönüştürücüleri çalışma
/// anında yüklenmiş bir MAUI Application (gerçek stil, örtük stil, DataTrigger ve kaynak çözümü; platform işleyicisi yok) ve
/// öğe ağacının çözümlenmiş görsel özelliklerini (renk, yazı, boşluk, kenarlık, köşe, görünürlük, görsel durumlar) metne
/// döken <see cref="Dok"/>.</summary>
internal static partial class GorunumOrtami
{
    private static readonly Lock Kilit = new();
    private static bool _kuruldu;

    private sealed class AnlikDispatcher : IDispatcher, IDispatcherProvider
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
        public IDispatcher? GetForCurrentThread() => this;
    }

    public static string DepoKoku()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(System.IO.Path.Combine(d.FullName, "Kasa.slnx")))
                return d.FullName;
        throw new InvalidOperationException("Depo kökü (Kasa.slnx) bulunamadı.");
    }

    public static string Oku(string yol) => File.ReadAllText(System.IO.Path.Combine(DepoKoku(), "Kasa.App", yol));

    /// <summary>Kasa.App'in ad alanları test derlemesindedir (aynı kaynak dosyaları): XAML'deki eşlemeler buraya yönlenir.</summary>
    public static string AdAlanlariniYonlendir(string xaml) => xaml
        .Replace("clr-namespace:Kasa.App.Controls\"", "clr-namespace:Kasa.App.Controls;assembly=Kasa.App.Core.Tests\"")
        .Replace("clr-namespace:Kasa.App.Converters\"", "clr-namespace:Kasa.App.Converters;assembly=Kasa.App.Core.Tests\"");

    /// <summary>Uygulama kaynaklarını (App.xaml'deki sırayla: Colors, Styles, dönüştürücüler) bir kez yükler.</summary>
    public static void Kur()
    {
        lock (Kilit)
        {
            if (_kuruldu)
                return;
            DispatcherProvider.SetCurrent(new AnlikDispatcher());
            static string Ic(string xaml)
            {
                var bas = xaml.IndexOf('>', xaml.IndexOf("<ResourceDictionary", StringComparison.Ordinal)) + 1;
                return xaml[bas..xaml.LastIndexOf("</ResourceDictionary>", StringComparison.Ordinal)];
            }
            var donusturuculer = string.Join("\n", DonusturucuKaydi().Matches(Oku("App.xaml")).Select(m => m.Value));
            var uygulama = $"""
                <Application xmlns="http://schemas.microsoft.com/dotnet/2021/maui" xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                             xmlns:ctl="clr-namespace:Kasa.App.Controls" xmlns:conv="clr-namespace:Kasa.App.Converters">
                  <Application.Resources>
                    <ResourceDictionary>
                      <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary>{Ic(Oku("Resources/Styles/Colors.xaml"))}</ResourceDictionary>
                        <ResourceDictionary>{Ic(Oku("Resources/Styles/Styles.xaml"))}</ResourceDictionary>
                      </ResourceDictionary.MergedDictionaries>
                      {donusturuculer}
                    </ResourceDictionary>
                  </Application.Resources>
                </Application>
                """;
            var app = new Application();
            app.LoadFromXaml(AdAlanlariniYonlendir(uygulama));
            Application.Current = app;
            _kuruldu = true;
        }
    }

    /// <summary>XAML parçasını (kökü ContentView) yükler; bağlamı verilirse atar.</summary>
    public static ContentView Yukle(string icerik, object? baglam = null)
    {
        Kur();
        var xaml = $"""
            <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui" xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
                         xmlns:ctl="clr-namespace:Kasa.App.Controls" x:Name="Sayfa">
            {XDataType().Replace(icerik, "")}
            </ContentView>
            """;
        var kok = new ContentView().LoadFromXaml(AdAlanlariniYonlendir(xaml));
        kok.BindingContext = baglam;
        return kok;
    }

    /// <summary>Görünür öğe ağacını çözümlenmiş görsel özellikleriyle döker. Görünmeyen (IsVisible=false) öğe dökülmez: MAUI
    /// yerleşimi onu ölçmez, yerleştirmez ve yığın aralığına saymaz (Visibility.Collapsed). Yerleşimi değiştirmeyen sarmallar
    /// açılır: dolgusuz, zeminsiz ContentView (içeriği yerine geçer) ve aynı aralıklı dikey yığının içindeki kenar boşluksuz
    /// DurumSeridi (çocukları üst yığına katılır; eski düz yerleşimle aynı konumlar).</summary>
    public static string Dok(Element kok)
    {
        var sb = new StringBuilder();
        Yaz(kok, 0, sb);
        return sb.ToString();
    }

    private static void Yaz(Element e, int derinlik, StringBuilder sb)
    {
        if (e is VisualElement { IsVisible: false })
            return;
        if (Sarmal(e) is { } icerik)
        {
            foreach (var c in icerik)
                Yaz(c, derinlik, sb);
            return;
        }
        sb.Append(' ', derinlik * 2).Append(TurAdi(e.GetType()));
        foreach (var (ad, deger) in Ozellikler(e))
            sb.Append(' ').Append(ad).Append('=').Append(deger);
        sb.Append('\n');
        foreach (var c in Cocuklar(e))
            Yaz(c, derinlik + 1, sb);
    }

    /// <summary>Açılabilen sarmalın çocukları; açılamıyorsa null. Görünür ama içi boş sarmal açılmaz: üst yığında yine yer
    /// (aralık) tutar ve dökümde ayrı öğe olarak kalır.</summary>
    private static IEnumerable<Element>? Sarmal(Element e)
    {
        if (e.GetType() == typeof(ContentView) && e is ContentView cv && cv.Padding == default && Brush.IsNullOrEmpty(cv.Background)
            && cv.BackgroundColor is null && cv.Margin == default && cv.Parent is not null && cv.Content is { IsVisible: true } c)
            return [c];
        if (e is Kasa.App.Controls.DurumSeridi s && s.Parent is VerticalStackLayout ust && ust.Spacing == s.Spacing
            && s.Margin == default && s.Padding == default && Brush.IsNullOrEmpty(s.Background) && s.BackgroundColor is null
            && s.HorizontalOptions == LayoutOptions.Fill && s.VerticalOptions == LayoutOptions.Fill
            && s.Children.OfType<VisualElement>().Any(x => x.IsVisible))
            return s.Children.OfType<Element>();
        return null;
    }

    private static IEnumerable<Element> Cocuklar(Element e) => e switch
    {
        ScrollView sv => sv.Content is { } c ? [c] : [],
        Border b => b.Content is { } c ? [c] : [],
        ContentView cv => cv.Content is { } c ? [c] : [],
        Layout l => l.Children.OfType<Element>(),
        _ => [],
    };

    /// <summary>Kasa kontrolleri (ParaGirisi dışında) MAUI taban türünün adıyla dökülür: yerleşim davranışı tabanındır.</summary>
    private static string TurAdi(Type t)
    {
        while (t.Namespace == "Kasa.App.Controls" && t.Name != "ParaGirisi")
            t = t.BaseType!;
        return t.Name;
    }

    private static IEnumerable<(string, string)> Ozellikler(Element e)
    {
        var ozellikler = new List<(string, BindableProperty)>();
        void Ekle(params BindableProperty[] p) => ozellikler.AddRange(p.Select(x => (x.PropertyName, x)));
        if (e is VisualElement)
        {
            Ekle(VisualElement.WidthRequestProperty, VisualElement.HeightRequestProperty, VisualElement.MinimumWidthRequestProperty,
                VisualElement.MinimumHeightRequestProperty, VisualElement.MaximumWidthRequestProperty, VisualElement.MaximumHeightRequestProperty,
                VisualElement.BackgroundColorProperty, VisualElement.BackgroundProperty, VisualElement.OpacityProperty,
                VisualElement.IsEnabledProperty, VisualElement.InputTransparentProperty, VisualElement.ShadowProperty);
        }
        if (e is View)
            Ekle(View.MarginProperty, View.HorizontalOptionsProperty, View.VerticalOptionsProperty);
        if (e.Parent is Grid)
            Ekle(Grid.RowProperty, Grid.ColumnProperty, Grid.RowSpanProperty, Grid.ColumnSpanProperty);
        if (e is Layout)
            Ekle(Layout.PaddingProperty);
        if (e is StackBase)
            Ekle(StackBase.SpacingProperty);
        if (e is Grid)
            Ekle(Grid.RowSpacingProperty, Grid.ColumnSpacingProperty, Grid.RowDefinitionsProperty, Grid.ColumnDefinitionsProperty);
        if (e is FlexLayout)
            Ekle(FlexLayout.WrapProperty, FlexLayout.DirectionProperty, FlexLayout.AlignItemsProperty, FlexLayout.AlignContentProperty,
                FlexLayout.JustifyContentProperty);
        if (e is ContentView)
            Ekle(ContentView.PaddingProperty);
        if (e is Label)
            Ekle(Label.TextProperty, Label.FormattedTextProperty, Label.FontFamilyProperty, Label.FontSizeProperty, Label.FontAttributesProperty,
                Label.TextColorProperty, Label.CharacterSpacingProperty, Label.TextTransformProperty, Label.HorizontalTextAlignmentProperty,
                Label.VerticalTextAlignmentProperty, Label.LineBreakModeProperty, Label.MaxLinesProperty, Label.LineHeightProperty,
                Label.TextDecorationsProperty, Label.PaddingProperty);
        if (e is Button)
            Ekle(Button.TextProperty, Button.FontFamilyProperty, Button.FontSizeProperty, Button.FontAttributesProperty, Button.TextColorProperty,
                Button.BorderColorProperty, Button.BorderWidthProperty, Button.CornerRadiusProperty, Button.PaddingProperty,
                Button.CharacterSpacingProperty, Button.TextTransformProperty, Button.LineBreakModeProperty);
        if (e is Border)
            Ekle(Border.StrokeProperty, Border.StrokeThicknessProperty, Border.StrokeShapeProperty, Border.PaddingProperty);
        if (e is ActivityIndicator)
            Ekle(ActivityIndicator.ColorProperty, ActivityIndicator.IsRunningProperty);
        if (e is BoxView)
            Ekle(BoxView.ColorProperty, BoxView.CornerRadiusProperty);
        if (e is InputView)
            Ekle(InputView.TextProperty, InputView.FontFamilyProperty, InputView.FontSizeProperty, InputView.FontAttributesProperty,
                InputView.TextColorProperty, InputView.PlaceholderProperty, InputView.PlaceholderColorProperty, InputView.KeyboardProperty,
                InputView.CharacterSpacingProperty);
        if (e is Entry)
            Ekle(Entry.HorizontalTextAlignmentProperty, Entry.IsPasswordProperty);
        if (e is DatePicker)
            Ekle(DatePicker.FontFamilyProperty, DatePicker.FontSizeProperty, DatePicker.TextColorProperty, DatePicker.FormatProperty);
        if (e is Picker)
            Ekle(Picker.TitleProperty, Picker.TitleColorProperty, Picker.FontFamilyProperty, Picker.FontSizeProperty, Picker.TextColorProperty);
        if (e is CheckBox)
            Ekle(CheckBox.ColorProperty, CheckBox.IsCheckedProperty);
        foreach (var (ad, p) in ozellikler)
            yield return (ad, Bicim(e.GetValue(p)));
        if (e is VisualElement ve && VisualStateManager.GetVisualStateGroups(ve) is { Count: > 0 } gruplar)
            yield return ("Durumlar", string.Join(";", gruplar.Select(g => g.Name + ":" + string.Join(",", g.States.Select(s =>
                s.Name + "{" + string.Join(",", s.Setters.Select(x => x.Property.PropertyName + "=" + Bicim(x.Value))) + "}")))));
    }

    private static string Bicim(object? d) => d switch
    {
        null => "∅",
        Color c => c.ToArgbHex(true),
        SolidColorBrush b => "Brush(" + Bicim(b.Color) + ")",
        Brush b => b.GetType().Name,
        Thickness t => FormattableString.Invariant($"({t.Left},{t.Top},{t.Right},{t.Bottom})"),
        LayoutOptions o => o.Alignment + (o.Expands ? "+" : ""),
        RoundRectangle r => FormattableString.Invariant($"RoundRectangle({r.CornerRadius.TopLeft},{r.CornerRadius.TopRight},{r.CornerRadius.BottomLeft},{r.CornerRadius.BottomRight})"),
        Shape s => s.GetType().Name,
        Shadow s => "Shadow(" + Bicim(s.Brush) + "," + Bicim(s.Opacity) + "," + Bicim(s.Radius) + "," + s.Offset + ")",
        FormattedString f => "[" + string.Join("|", f.Spans.Select(s => $"{s.Text}/{Bicim(s.FontAttributes)}/{Bicim(s.TextColor)}/{Bicim(s.FontSize)}")) + "]",
        RowDefinitionCollection r => "[" + string.Join(",", r.Select(x => Bicim(x.Height))) + "]",
        ColumnDefinitionCollection c => "[" + string.Join(",", c.Select(x => Bicim(x.Width))) + "]",
        GridLength g => g.IsAuto ? "Auto" : g.IsStar ? Bicim(g.Value) + "*" : Bicim(g.Value),
        double x => x.ToString("R", CultureInfo.InvariantCulture),
        float x => x.ToString("R", CultureInfo.InvariantCulture),
        string s => "\"" + s + "\"",
        _ => d.ToString() ?? "",
    };

    [GeneratedRegex(@"<conv:\w+ x:Key=""\w+"" />")]
    private static partial Regex DonusturucuKaydi();
    [GeneratedRegex(@"\s+x:DataType=""[^""]*""")]
    private static partial Regex XDataType();
}
