using System.Collections.ObjectModel;
using Kasa.App.Core;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;

namespace Kasa.App.Views;

/// <summary>Kodla yazılmış takip sayfalarının (Kartlar, Krediler, Aylık Giderler, Ekstre, Bildirimler, Kasa kontrolü)
/// yapı taşları. Yazı boyutu ve renkleri burada yazılmaz: her öğe Styles.xaml'daki bir stil anahtarını uygular
/// (LblTakip*, TakipAyirici; anahtar yoksa örtük stil). Görünüm, anahtarlara geçişten önceki sabit değerlerle aynıdır
/// (Kasa.App.Core.Tests GorunumEsdegerligiTests).</summary>
internal static class TakipUi
{
    public static Label Metin(string text) => new() { Text = text, Style = (Style)Application.Current!.Resources["LblTakipMetin"] };

    /// <summary>Bağlı gövde metni (örtük Label stili).</summary>
    public static Label Bagli(string yol) => Bagla(new Label(), yol);

    /// <summary>Bağlı küçük metin (liste satırı özeti; Metin ile aynı stil).</summary>
    public static Label BagliMetin(string yol) => Bagla(new Label { Style = (Style)Application.Current!.Resources["LblTakipMetin"] }, yol);

    /// <summary>Bağlı büyük metin (kart, kredi ve ay özeti).</summary>
    public static Label BagliBuyuk(string yol) => Bagla(new Label { Style = (Style)Application.Current!.Resources["LblTakipBuyuk"] }, yol);

    /// <summary>Bağlı hata metni (koyu kırmızı).</summary>
    public static Label BagliHata(string yol) => Bagla(new Label { Style = (Style)Application.Current!.Resources["LblTakipHata"] }, yol);

    private static Label Bagla(Label etiket, string yol)
    {
        etiket.SetBinding(Label.TextProperty, yol);
        return etiket;
    }

    public static View Alan(string ad, View v) => new VerticalStackLayout
    {
        Spacing = 5,
        Children = { new Label { Text = ad, Style = (Style)Application.Current!.Resources["LblTakipKucuk"] }, v },
    };

    public static Entry Girdi(string yol, bool para = false, bool sayi = false)
    {
        if (para)
        {
            var p = new Controls.ParaGirisi();
            p.SetBinding(Controls.ParaGirisi.TutarProperty, yol);
            return p;
        }
        var e = new Entry { Keyboard = sayi ? Keyboard.Numeric : Keyboard.Default };
        e.SetBinding(Entry.TextProperty, yol);
        return e;
    }

    public static DatePicker Tarih(string yol)
    {
        var d = new DatePicker();
        d.SetBinding(DatePicker.DateProperty, yol);
        return d;
    }

    public static Picker Secim(string kaynak, string secili, string alan = "Ad")
    {
        var p = new Picker { Title = "Seçin", ItemDisplayBinding = new Binding(alan) };
        p.SetBinding(Picker.ItemsSourceProperty, kaynak);
        p.SetBinding(Picker.SelectedItemProperty, secili);
        return p;
    }

    public static View Onay(string text, string yol)
    {
        var c = new CheckBox();
        c.SetBinding(CheckBox.IsCheckedProperty, yol);
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8 };
        grid.Add(c);
        grid.Add(new Label { Text = text, VerticalOptions = LayoutOptions.Center }, 1);
        return grid;
    }

    public static Button Dugme(string text, string command)
    {
        var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Start };
        b.SetBinding(Button.CommandProperty, command);
        return b;
    }

    public static Button Tikla(string text, Func<Task> action)
    {
        var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Start };
        b.Clicked += async (_, _) =>
        {
            b.IsEnabled = false;
            try
            {
                await action();
            }
            finally
            {
                b.IsEnabled = true;
            }
        };
        return b;
    }

    public static Border Kart(string title, params View[] views)
    {
        var l = new VerticalStackLayout { Spacing = 12 };
        l.Add(new Label { Text = title, Style = (Style)Application.Current!.Resources["LblTakipKartBaslik"] });
        foreach (var v in views)
            l.Add(v);
        return new Border { Style = (Style)Application.Current!.Resources["CardForm"], Content = l };
    }

    public static T Goster<T>(T view, string yol, bool nesne = false) where T : View
    {
        view.SetBinding(VisualElement.IsVisibleProperty, yol, converter: nesne ? new NesneVarConverter() : null);
        return view;
    }

    public static View Editor(View v) => Goster(new ContentView { Content = v }, "EditorMu");

    public static View Benzerlik(string yol, string command)
        => Goster(Kart("Benzer kayıt kontrolü", Bagli(yol + ".Uyari"), Dugme("Ayrı bir işlem, devam et", command)), yol + ".UyariVar");

    /// <summary>Kodla yazılmış sayfaların durum satırları (Yenile, yükleniyor göstergesi, hata, isteğe bağlı ileti, son
    /// güncelleme): TakipSayfasi, Kasa kontrolü ve Dışa aktar aynı sırayı ve stilleri kullanır. Bağlam modelinde Mesgul,
    /// Hata (ya da <paramref name="hataYolu"/>) ve SonGuncelleme beklenir. <paramref name="gostergeSolda"/> false iken gösterge
    /// satır boyunca yerleşir (Kasa kontrolünün önceki görünümü). Kartlar ekranı hatayı form açıkken formun içinde gösterdiği
    /// için buraya SayfaHatasi'nı bağlar.</summary>
    public static void DurumSatirlari(Layout hedef, View yenile, Label? mesaj = null, bool gostergeSolda = true, string hataYolu = "Hata")
    {
        hedef.Add(yenile);
        var busy = new ActivityIndicator();
        if (gostergeSolda)
            busy.HorizontalOptions = LayoutOptions.Start;
        busy.SetBinding(ActivityIndicator.IsRunningProperty, "Mesgul");
        hedef.Add(busy);
        hedef.Add(BagliHata(hataYolu));
        if (mesaj is not null)
            hedef.Add(mesaj);
        var zaman = new Label { Style = (Style)Application.Current!.Resources["LblTakipKucuk"] };
        zaman.SetBinding(Label.TextProperty, new Binding("SonGuncelleme", stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}"));
        hedef.Add(zaman);
    }

    public static View Liste<T>(string yol, Func<T, Task>? ac = null, string action = "Aç", Func<T, bool>? gorunur = null,
        Func<T, string>? actionText = null)
    {
        var l = new VerticalStackLayout { Spacing = 10 };
        l.SetBinding(BindableLayout.ItemsSourceProperty, yol);
        BindableLayout.SetEmptyView(l, Metin("Gösterilecek kayıt yok."));
        BindableLayout.SetItemTemplate(l, new DataTemplate(() =>
        {
            var row = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(0, 10) };
            var baslik = Bagli("Baslik");
            baslik.FontAttributes = FontAttributes.Bold;
            row.Add(baslik);
            row.Add(BagliMetin("Ozet"));
            if (ac is not null)
                row.Add(ListeDugmesi(ac, action, gorunur, actionText));
            row.Add(new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] });
            return row;
        }));
        return l;
    }

    private static Button ListeDugmesi<T>(Func<T, Task> ac, string action, Func<T, bool>? gorunur, Func<T, string>? actionText)
    {
        var b = new Button { Text = action, HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
        b.BindingContextChanged += (_, _) =>
        {
            b.IsVisible = b.BindingContext is T item && (gorunur?.Invoke(item) ?? true);
            if (b.BindingContext is T t)
                b.Text = actionText?.Invoke(t) ?? action;
        };
        b.Clicked += async (_, _) =>
        {
            if (b.BindingContext is not T item)
                return;
            b.IsEnabled = false;
            try
            {
                await ac(item);
            }
            finally
            {
                b.IsEnabled = true;
            }
        };
        return b;
    }

    public static View Paylar(ObservableCollection<TakipPayEditor> paylar, Action ekle)
    {
        var l = new VerticalStackLayout { Spacing = 8 };
        var rows = new VerticalStackLayout { Spacing = 8 };
        BindableLayout.SetItemsSource(rows, paylar);
        BindableLayout.SetItemTemplate(rows, new DataTemplate(() =>
        {
            var row = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(new GridLength(145)), new(GridLength.Auto) } };
            row.Add(Secim("Kanallar", "Kanal"));
            row.Add(Girdi("Tutar", true), 1);
            var sil = new Button { Text = "Kaldır", Style = (Style)Application.Current!.Resources["BtnSecondary"] };
            sil.Clicked += (_, _) =>
            {
                if (sil.BindingContext is TakipPayEditor p)
                    paylar.Remove(p);
            };
            row.Add(sil, 2);
            return row;
        }));
        l.Add(rows);
        l.Add(Tikla("Kanal payı ekle", () =>
        {
            ekle();
            return Task.CompletedTask;
        }));
        return l;
    }

    public static View KanalSecimleri(string yol)
    {
        var rows = new VerticalStackLayout { Spacing = 3 };
        rows.SetBinding(BindableLayout.ItemsSourceProperty, yol);
        BindableLayout.SetItemTemplate(rows, new DataTemplate(() =>
        {
            var c = new CheckBox();
            c.SetBinding(CheckBox.IsCheckedProperty, "Secili");
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
            g.Add(c);
            var l = Bagli("Ad");
            l.VerticalOptions = LayoutOptions.Center;
            g.Add(l, 1);
            return g;
        }));
        return rows;
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
    protected TakipSayfasi(T vm, string title, string aciklama, Func<Task> yukle, string hataYolu = nameof(TemelViewModel.Hata))
    {
        Vm = vm;
        BindingContext = vm;
        Title = title;
        _yukle = yukle;
        BackgroundColor = (Color)Application.Current!.Resources["AppBg"];
        var root = new VerticalStackLayout { Padding = new Thickness(28, 22), Spacing = 16, MaximumWidthRequest = 1160 };
        root.Add(new Label { Text = title, Style = (Style)Application.Current!.Resources["LblTakipSayfaBaslik"] });
        root.Add(TakipUi.Metin(aciklama));
        var mesaj = new Label { Style = (Style)Application.Current!.Resources["LblTakipMesaj"] };
        mesaj.SetBinding(Label.TextProperty, nameof(vm.Mesaj));
        TakipUi.DurumSatirlari(root, TakipUi.Tikla("Yenile / tekrar dene", yukle), mesaj, hataYolu: hataYolu);
        Govde.SetBinding(IsVisibleProperty, nameof(vm.VeriHazir));
        Govde.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        root.Add(Govde);
        Kaydirici = new ScrollView { Content = root };
        Content = Kaydirici;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!Vm.VeriHazir)
            await _yukle();
    }

    /// <summary>Gerekçe penceresi (tek yol, <see cref="OturumluViewModel.GerekceyleAsync"/>): oturum pencere açılmadan önce yakalanır;
    /// pencere açıkken oturum değişirse işlem yapılmaz, gerekçe yeni oturumun formuna yazılmaz.</summary>
    protected Task GerekceyleAsync(string title, Func<string, int, Task> islem)
        => Vm.GerekceyleAsync(() => DisplayPromptAsync(title, "İşlemin nedenini yazın. Geçmiş kayıtlar korunur.", "Devam", "Vazgeç", maxLength: 1000), islem);
}
