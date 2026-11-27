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
        Title = "Rapor dışa aktar";
        BackgroundColor = (Color)Application.Current!.Resources["AppBg"];
        var root = new VerticalStackLayout { Padding = new Thickness(28, 22), Spacing = 16, MaximumWidthRequest = 1220 };
        root.SizeChanged += (_, _) => root.Padding = root.Width < 600 ? new Thickness(16, 14) : new Thickness(28, 22);
        root.Add(TakipUi.SayfaBasligi(Title));

        var form = new VerticalStackLayout { Spacing = 12 };
        form.SetBinding(IsVisibleProperty, nameof(vm.VeriHazir));
        form.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        // Tarih aralığı yan yana; kanal seçicisinin ilk seçeneği "Tüm kanallar"; biçim seçimi ve tek "Dışa aktar" (sadeleştirme §8).
        var tarihler = new Controls.UyumluIzgara { ColumnSpacing = 12, RowSpacing = 12, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
        tarihler.Add(Alan("Başlangıç tarihi", Tarih(nameof(vm.Baslangic))));
        tarihler.Add(Alan("Bitiş tarihi", Tarih(nameof(vm.Bitis))), 1);
        form.Add(tarihler);
        // Seçenekler yeniden dizilirken (yenileme listeyi boşaltıp doldurur) seçim modele yazılmaz; seçili kanal modelde korunur.
        var kanal = new Picker { ItemDisplayBinding = new Binding(nameof(KanalSecenegi.Ad)) };
        var doluyor = false;
        void KanallariDoldur()
        {
            doluyor = true;
            try
            {
                List<KanalSecenegi> secenekler = [new("Tüm kanallar", null), .. vm.Kanallar.Select(k => new KanalSecenegi(k.Ad, k))];
                kanal.ItemsSource = secenekler;
                kanal.SelectedItem = secenekler.FirstOrDefault(s => s.Kanal is { } k && k.Id == vm.Kanal?.Id) ?? secenekler[0];
            }
            finally { doluyor = false; }
        }
        KanallariDoldur();
        vm.Kanallar.CollectionChanged += (_, _) => KanallariDoldur();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Kanal) && (kanal.SelectedItem as KanalSecenegi)?.Kanal?.Id != vm.Kanal?.Id)
                KanallariDoldur();
        };
        kanal.SelectedIndexChanged += (_, _) =>
        {
            if (!doluyor)
                vm.Kanal = (kanal.SelectedItem as KanalSecenegi)?.Kanal;
        };
        form.Add(Alan("Kanal", kanal));
        var bicim = new Picker { ItemsSource = Bicimler.Select(b => b.Ad).ToList(), SelectedIndex = 0 };
        form.Add(Alan("Biçim", bicim));
        var aktar = TakipUi.Birincil(new Button { Text = "Dışa aktar", HorizontalOptions = LayoutOptions.Start });
        aktar.Clicked += async (_, _) =>
        {
            if (_vm.Mesgul || bicim.SelectedIndex < 0)
                return;
            var kod = Bicimler[bicim.SelectedIndex].Kod;
            await DosyaIslemleri.IndirVeKaydetAsync(this, hedef => _vm.IndirAsync(kod, hedef), kod == "html");
        };
        form.Add(aktar);
        form.Add(TakipUi.Metin(DosyaIslemleri.YazdirmaAciklamasi));
        root.Add(new Border { Style = (Style)Application.Current.Resources["CardForm"], Content = form });
        Content = new ScrollView { Content = root };
    }

    private static View Alan(string label, View kontrol) => new VerticalStackLayout
    {
        Spacing = 4,
        Children = { new Label { Text = label, Style = (Style)Application.Current!.Resources["LblTakipKucuk"] }, TakipUi.Cerceve(kontrol) },
    };
    private static DatePicker Tarih(string yol)
    {
        var v = new DatePicker();
        v.SetBinding(DatePicker.DateProperty, yol);
        return v;
    }
    private sealed record KanalSecenegi(string Ad, Kasa.ApiClient.KanalDto? Kanal);

    /// <summary>Dışa aktarma biçimleri (sunucu kodu xlsx, csv; yazdırılabilir rapor html).</summary>
    private static readonly (string Ad, string Kod)[] Bicimler = [("Excel", "xlsx"), ("CSV", "csv"), ("PDF (yazdırılabilir)", "html")];

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }
}
