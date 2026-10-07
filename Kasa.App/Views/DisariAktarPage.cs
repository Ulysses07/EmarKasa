using Kasa.App.Core;

namespace Kasa.App.Views;

public sealed class DisariAktarPage : ContentPage, Controls.IYenilenebilir
{
    private readonly DisariAktarViewModel _vm;

    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    public DisariAktarPage(DisariAktarViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Gider raporu dışa aktar";
        BackgroundColor = (Color)Application.Current!.Resources["AppBg"];
        var root = new VerticalStackLayout { Padding = new Thickness(28, 22), Spacing = 16, MaximumWidthRequest = 1220 };
        root.SizeChanged += (_, _) => root.Padding = root.Width < 600 ? new Thickness(16, 14) : new Thickness(28, 22);
        root.Add(new Label { Text = Title, Style = (Style)Application.Current.Resources["LblTakipSayfaBaslik"] });
        root.Add(new Label { Text = "Tarih ve kanal süzgeciyle Excel, CSV veya yazdırılabilir gider raporu. Kanal filtresi eşleşen ödemenin tamamını listeler; kanal payı toplamı değildir." });
        var yenile = new Button { Text = "Yenile / tekrar dene", HorizontalOptions = LayoutOptions.Start };
        yenile.Clicked += async (_, _) => await vm.YukleAsync();
        TakipUi.DurumSatirlari(root, yenile);

        var form = new VerticalStackLayout { Spacing = 12 };
        form.SetBinding(IsVisibleProperty, nameof(vm.VeriHazir));
        form.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        form.Add(Alan("Başlangıç tarihi", Tarih(nameof(vm.Baslangic))));
        form.Add(Alan("Bitiş tarihi", Tarih(nameof(vm.Bitis))));
        var kanal = new Picker { Title = "Tüm kanallar", ItemDisplayBinding = new Binding("Ad") };
        kanal.SetBinding(Picker.ItemsSourceProperty, nameof(vm.Kanallar));
        kanal.SetBinding(Picker.SelectedItemProperty, nameof(vm.Kanal));
        form.Add(Alan("Kanal (boş: tümü)", kanal));
        var temizle = new Button { Text = "Tüm kanallar", HorizontalOptions = LayoutOptions.Start };
        temizle.Clicked += (_, _) => vm.Kanal = null;
        form.Add(temizle);
        form.Add(Indir("Excel (.xlsx) kaydet", "xlsx"));
        form.Add(Indir("CSV kaydet", "csv"));
        form.Add(Indir("Yazdır / PDF kaydet", "html"));
        form.Add(TakipUi.Metin(DosyaIslemleri.YazdirmaAciklamasi));
        root.Add(new Border { Style = (Style)Application.Current.Resources["CardForm"], Content = form });
        Content = new ScrollView { Content = root };
    }

    private static View Alan(string label, View kontrol) => new VerticalStackLayout
    {
        Spacing = 4,
        Children = { new Label { Text = label, Style = (Style)Application.Current!.Resources["LblTakipKucuk"] }, kontrol },
    };
    private static DatePicker Tarih(string yol)
    {
        var v = new DatePicker();
        v.SetBinding(DatePicker.DateProperty, yol);
        return v;
    }
    private Button Indir(string text, string bicim)
    {
        var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Start };
        b.Clicked += async (_, _) =>
        {
            if (_vm.Mesgul)
                return;
            await DosyaIslemleri.IndirVeKaydetAsync(this, hedef => _vm.IndirAsync(bicim, hedef), bicim == "html");
        };
        return b;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }
}
