using System.Collections.ObjectModel;
using Kasa.App.Core;

namespace Kasa.App.Views;

internal static class TakipUi
{
    public static Label Metin(string text) => new() { Text = text, FontSize = 13 };
    public static Label Bagli(string yol, double size = 14) { var l = new Label { FontSize = size }; l.SetBinding(Label.TextProperty, yol); return l; }
    public static View Alan(string ad, View v) => new VerticalStackLayout { Spacing = 5, Children = { new Label { Text = ad, FontSize = 12 }, v } };
    public static Entry Girdi(string yol, bool para = false, bool sayi = false)
    {
        var e = new Entry { Keyboard = para || sayi ? Keyboard.Numeric : Keyboard.Default };
        e.SetBinding(Entry.TextProperty, yol, converter: para ? new Converters.ParaGirisConverter() : null); return e;
    }
    public static DatePicker Tarih(string yol) { var d = new DatePicker(); d.SetBinding(DatePicker.DateProperty, yol); return d; }
    public static Picker Secim(string kaynak, string secili, string alan = "Ad")
    {
        var p = new Picker { Title = "Seçin", ItemDisplayBinding = new Binding(alan) }; p.SetBinding(Picker.ItemsSourceProperty, kaynak); p.SetBinding(Picker.SelectedItemProperty, secili); return p;
    }
    public static View Onay(string text, string yol)
    {
        var c = new CheckBox(); c.SetBinding(CheckBox.IsCheckedProperty, yol);
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8 };
        grid.Add(c); grid.Add(new Label { Text = text, VerticalOptions = LayoutOptions.Center }, 1); return grid;
    }
    public static Button Dugme(string text, string command) { var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Start }; b.SetBinding(Button.CommandProperty, command); return b; }
    public static Button Tikla(string text, Func<Task> action)
    {
        var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Start };
        b.Clicked += async (_, _) => { b.IsEnabled = false; try { await action(); } finally { b.IsEnabled = true; } }; return b;
    }
    public static Border Kart(string title, params View[] views)
    {
        var l = new VerticalStackLayout { Spacing = 12 }; l.Add(new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold }); foreach (var v in views) l.Add(v);
        return new Border { Style = (Style)Application.Current!.Resources["CardForm"], Content = l };
    }
    public static T Goster<T>(T view, string yol, bool nesne = false) where T : View { view.SetBinding(VisualElement.IsVisibleProperty, yol, converter: nesne ? new NesneVarConverter() : null); return view; }
    public static View Editor(View v) => Goster(new ContentView { Content = v }, "EditorMu");
    public static View Benzerlik(string yol, string command) => Goster(Kart("Benzer kayıt kontrolü", Bagli(yol + ".Uyari"), Dugme("Ayrı bir işlem, devam et", command)), yol + ".UyariVar");
    public static View Liste<T>(string yol, Func<T, Task>? ac = null, string action = "Aç", Func<T, bool>? gorunur = null, Func<T, string>? actionText = null)
    {
        var l = new VerticalStackLayout { Spacing = 10 }; l.SetBinding(BindableLayout.ItemsSourceProperty, yol);
        BindableLayout.SetEmptyView(l, Metin("Gösterilecek kayıt yok."));
        BindableLayout.SetItemTemplate(l, new DataTemplate(() =>
        {
            var row = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(0, 10) };
            var baslik = Bagli("Baslik"); baslik.FontAttributes = FontAttributes.Bold; row.Add(baslik); row.Add(Bagli("Ozet", 13));
            if (ac is not null)
            {
                var b = new Button { Text = action, HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
                b.BindingContextChanged += (_, _) => { b.IsVisible = b.BindingContext is T item && (gorunur?.Invoke(item) ?? true); if (b.BindingContext is T t) b.Text = actionText?.Invoke(t) ?? action; };
                b.Clicked += async (_, _) => { if (b.BindingContext is T item) { b.IsEnabled = false; try { await ac(item); } finally { b.IsEnabled = true; } } }; row.Add(b);
            }
            row.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("E3E0D6") }); return row;
        }));
        return l;
    }
    public static View Paylar(ObservableCollection<TakipPayEditor> paylar, Action ekle)
    {
        var l = new VerticalStackLayout { Spacing = 8 }; var rows = new VerticalStackLayout { Spacing = 8 };
        BindableLayout.SetItemsSource(rows, paylar);
        BindableLayout.SetItemTemplate(rows, new DataTemplate(() =>
        {
            var row = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(new GridLength(145)), new(GridLength.Auto) } };
            row.Add(Secim("Kanallar", "Kanal")); row.Add(Girdi("Tutar", true), 1);
            var sil = new Button { Text = "Kaldır", Style = (Style)Application.Current!.Resources["BtnSecondary"] }; sil.Clicked += (_, _) => { if (sil.BindingContext is TakipPayEditor p) paylar.Remove(p); }; row.Add(sil, 2); return row;
        }));
        l.Add(rows); l.Add(Tikla("Kanal payı ekle", () => { ekle(); return Task.CompletedTask; })); return l;
    }
    public static View KanalSecimleri(string yol)
    {
        var rows = new VerticalStackLayout { Spacing = 3 }; rows.SetBinding(BindableLayout.ItemsSourceProperty, yol);
        BindableLayout.SetItemTemplate(rows, new DataTemplate(() => { var c = new CheckBox(); c.SetBinding(CheckBox.IsCheckedProperty, "Secili"); var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } }; g.Add(c); var l = Bagli("Ad"); l.VerticalOptions = LayoutOptions.Center; g.Add(l, 1); return g; })); return rows;
    }
    private sealed class NesneVarConverter : IValueConverter
    {
        public object Convert(object? value, Type type, object? parameter, System.Globalization.CultureInfo culture) => value is not null;
        public object ConvertBack(object? value, Type type, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}

public abstract class TakipSayfasi<T> : ContentPage where T : OturumluViewModel
{
    protected readonly T Vm;
    protected readonly VerticalStackLayout Govde = new() { Spacing = 18 };
    protected readonly ScrollView Kaydirici;
    private readonly Func<Task> _yukle;
    protected TakipSayfasi(T vm, string title, string aciklama, Func<Task> yukle)
    {
        Vm = vm; BindingContext = vm; Title = title; _yukle = yukle; BackgroundColor = (Color)Application.Current!.Resources["AppBg"];
        var root = new VerticalStackLayout { Padding = new Thickness(28, 22), Spacing = 16, MaximumWidthRequest = 1160 };
        root.Add(new Label { Text = title, FontSize = 28, FontAttributes = FontAttributes.Bold }); root.Add(TakipUi.Metin(aciklama));
        root.Add(TakipUi.Tikla("Yenile / tekrar dene", yukle));
        var busy = new ActivityIndicator { HorizontalOptions = LayoutOptions.Start }; busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(vm.Mesgul)); root.Add(busy);
        var hata = TakipUi.Bagli(nameof(vm.Hata)); hata.TextColor = Colors.DarkRed; root.Add(hata);
        var mesaj = TakipUi.Bagli(nameof(vm.Mesaj)); mesaj.TextColor = Colors.DarkGreen; root.Add(mesaj);
        var zaman = new Label { FontSize = 12 }; zaman.SetBinding(Label.TextProperty, new Binding(nameof(vm.SonGuncelleme), stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}")); root.Add(zaman);
        Govde.SetBinding(IsVisibleProperty, nameof(vm.VeriHazir)); Govde.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter()); root.Add(Govde);
        Kaydirici = new ScrollView { Content = root }; Content = Kaydirici;
    }
    protected override async void OnAppearing() { base.OnAppearing(); if (!Vm.VeriHazir) await _yukle(); }
    protected Task<string?> GerekceAsync(string title) => DisplayPromptAsync(title, "İşlemin nedenini yazın. Geçmiş kayıtlar korunur.", "Devam", "Vazgeç", maxLength: 1000);
}
