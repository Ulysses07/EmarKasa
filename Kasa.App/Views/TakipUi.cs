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

    /// <summary>Hatası gösterilen alan (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §1; Controls.FormAlani): başlık,
    /// girdi ve hata varken altında ileti; çerçeve yalnız hata varken (kırmızı) görünür. <paramref name="hatalar"/> modelin
    /// AlanHatalari özelliğinin yolu ("Hatalar"), <paramref name="alan"/> alanın adı ("Tutar"): hata "Hatalar[Tutar]" yolundan gelir.</summary>
    public static Controls.FormAlani Alan(string ad, View v, string hatalar, string alan)
    {
        var f = new Controls.FormAlani { Baslik = ad, Icerik = v, Alan = alan, Cerceveli = false, TakipStili = true };
        f.SetBinding(Controls.FormAlani.HataProperty, $"{hatalar}[{alan}]");
        return f;
    }

    /// <summary>Formun genel hatası (tasarım §1): formun en üstünde, formun içinde kırmızı kutu (ErrorBox); yalnız doluyken görünür.
    /// Sayfanın başındaki hata satırı yalnız yükleme hataları için kalır.</summary>
    public static Border FormHatasi(string yol)
    {
        var metin = new Label { Style = (Style)Application.Current!.Resources["LblError"] };
        metin.SetBinding(Label.TextProperty, yol);
        var kutu = new Border { Style = (Style)Application.Current!.Resources["ErrorBox"], Content = metin };
        kutu.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(Label.Text), source: metin, converter: new Converters.DoluIseConverter()));
        return kutu;
    }

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
        var c = OnayKutusu();
        c.SetBinding(CheckBox.IsCheckedProperty, yol);
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8 };
        grid.Add(c);
        grid.Add(new Label { Text = text, VerticalOptions = LayoutOptions.Center }, 1);
        return grid;
    }

    /// <summary>Etiketli onay kutusunun kutusu: WinUI CheckBox'ın varsayılan stili MinWidth=120 yazar; etiket kutudan ~110 px
    /// uzakta kalıyordu. Açıkça verilen en küçük genişlik (0) onu ezer, kutu kendi genişliğinde ölçülür.</summary>
    private static CheckBox OnayKutusu() => new() { MinimumWidthRequest = 0 };

    public static Button Dugme(string text, string command)
    {
        var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Start };
        b.SetBinding(Button.CommandProperty, command);
        return b;
    }

    /// <summary>Açık formun düğmesinin kenar rengi (Colors.xaml anahtarı): beyaz kart zeminine karşı en az 3:1 (kontrast testi).</summary>
    public const string AcikFormKenari = "Ink";

    /// <summary>Form açan düğme (Kartlar ekranı): <paramref name="acikYol"/> <paramref name="form"/> iken açık görünür ve ekran
    /// okuyucuya "Açık" ipucu verir. Kenar kalınlığı her durumda 2'dir; açılınca yalnız kenar rengi (<see cref="AcikFormKenari"/>)
    /// ve zemin (birincil GreenDark, ikincil GreenSoft ve yeşil yazı) değişir, düğme genişleyip yanındakileri itmez. Kapalıyken
    /// kenar düğmenin kendi rengindedir (birincil: zemini Green, ikincil: BtnSecondary kenarı Border), görünmez. Kenar rengi
    /// bağlamayla, zemin tetikle gelir: kapalı düğmede stilin görsel durumları (üzerinde, basılı, kapalı) çalışmaya devam eder.</summary>
    public static Button FormDugmesi(string metin, string komut, object form, string acikYol, bool birincil = false)
    {
        var dugme = Dugme(metin, komut);
        dugme.CommandParameter = form;
        if (!birincil)
            dugme.Style = (Style)Application.Current!.Resources["BtnSecondary"];
        dugme.BorderWidth = 2;
        var kapaliKenar = birincil ? (Color)Application.Current!.Resources["Green"] : (Color)Application.Current!.Resources["Border"];
        dugme.SetBinding(Button.BorderColorProperty, acikYol,
            converter: new AcikIseConverter(form, (Color)Application.Current!.Resources[AcikFormKenari], kapaliKenar));
        var acik = new DataTrigger(typeof(Button)) { Binding = new Binding(acikYol), Value = form };
        acik.Setters.Add(new Setter { Property = SemanticProperties.HintProperty, Value = "Açık" });
        if (birincil)
        {
            acik.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = (Color)Application.Current!.Resources["GreenDark"] });
        }
        else
        {
            acik.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = (Color)Application.Current!.Resources["GreenSoft"] });
            acik.Setters.Add(new Setter { Property = Button.TextColorProperty, Value = (Color)Application.Current!.Resources["Green"] });
        }
        dugme.Triggers.Add(acik);
        return dugme;
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

    /// <summary>Yalnız editörde VE <paramref name="gorunurYol"/> doğruyken görünen alan. Sarmalayıcının kendi görünürlüğü
    /// de koşula bağlanır: "editörde ama içi gizli" durumunda (ör. henüz belge yok) sarmalayıcı erken kapanır, dikey
    /// yığında (Spacing) boşluk bırakmaz. Tek koşula (yalnız "EditorMu") bağlı <see cref="Goster{T}"/> ile sarılmış içerik
    /// bu sorunu yaşar: içi gizliyken kendisi görünür kalır (EK-01).</summary>
    public static View Editor(View v, string gorunurYol)
    {
        var cv = new ContentView { Content = v };
        cv.SetBinding(VisualElement.IsVisibleProperty, new MultiBinding
        {
            Bindings = { new Binding("EditorMu"), new Binding(gorunurYol) },
            Converter = new HepsiDoguConverter(),
        });
        return cv;
    }

    public static View Benzerlik(string yol, string command)
        => Goster(Kart("Benzer kayıt kontrolü", Bagli(yol + ".Uyari"), Dugme("Ayrı bir işlem, devam et", command)), yol + ".UyariVar");

    /// <summary>Kodla yazılmış sayfaların durum satırları (Yenile ve sağında yükleniyor göstergesi, hata, isteğe bağlı ileti, son
    /// güncelleme): TakipSayfasi, Kasa kontrolü ve Dışa aktar aynı sırayı ve stilleri kullanır. Bağlam modelinde Mesgul,
    /// Hata (ya da <paramref name="hataYolu"/>) ve SonGuncellemeMetni beklenir (hiç yükleme yokken "Henüz yüklenmedi.", veri
    /// eskiyse " · güncel olmayabilir" ekiyle; OturumluViewModel). Kartlar ekranı hatayı form açıkken formun içinde
    /// gösterdiği için buraya SayfaHatasi'nı bağlar. Hata ve ileti yalnız doluyken yer kaplar (boşken Yenile ile son güncelleme
    /// arasında ~130 px boşluk kalıyordu). Dönen değer hata satırıdır (sayfa onu görünür yere kaydırabilir).</summary>
    public static Label DurumSatirlari(Layout hedef, View yenile, Label? mesaj = null, string hataYolu = "Hata")
    {
        hedef.Add(YenileSatiri(yenile));
        var hata = DoluysaGoster(BagliHata(hataYolu));
        hedef.Add(hata);
        if (mesaj is not null)
            hedef.Add(DoluysaGoster(mesaj));
        var zaman = new Label { Style = (Style)Application.Current!.Resources["LblTakipKucuk"] };
        zaman.SetBinding(Label.TextProperty, nameof(OturumluViewModel.SonGuncellemeMetni));
        hedef.Add(zaman);
        return hata;
    }

    /// <summary>Kendi başına duran yükleme göstergesi (Yenile satırının dışında, ör. Kasalar'daki "Kart ve kredi takibi"
    /// kutusu) ve altındaki hata metni: gösterge yalnız <paramref name="mesgulYolu"/> doğruyken çalışır VE görünür, hata
    /// yalnız doluyken görünür. İkisi de boşken yer ve dikey yığında aralık (Spacing) kaplamaz (KS-03: aksi halde ~87 px
    /// boşluk kalır).</summary>
    public static (ActivityIndicator Gosterge, Label Hata) MesgulVeHata(string mesgulYolu, string hataYolu)
    {
        var gosterge = new ActivityIndicator();
        gosterge.SetBinding(ActivityIndicator.IsRunningProperty, mesgulYolu);
        gosterge.SetBinding(VisualElement.IsVisibleProperty, mesgulYolu);
        return (gosterge, DoluysaGoster(BagliHata(hataYolu)));
    }

    /// <summary>Yenile düğmesi ve sağında, yalnız meşgulken, gösterge ile "İşleniyor…" (yükleme de kayıt/dışa aktarma da): gösterge ayrı satırdayken
    /// görününce bütün içerik 48 px aşağı kayıyordu. Gösterge 24 px'tir, satırın yüksekliğini Yenile düğmesi belirler.</summary>
    private static HorizontalStackLayout YenileSatiri(View yenile)
    {
        var gosterge = new ActivityIndicator { WidthRequest = 24, HeightRequest = 24, VerticalOptions = LayoutOptions.Center };
        gosterge.SetBinding(ActivityIndicator.IsRunningProperty, "Mesgul");
        gosterge.SetBinding(VisualElement.IsVisibleProperty, "Mesgul");
        var metin = Metin("İşleniyor…");
        metin.VerticalOptions = LayoutOptions.Center;
        metin.SetBinding(VisualElement.IsVisibleProperty, "Mesgul");
        return new HorizontalStackLayout { Spacing = 12, Children = { yenile, gosterge, metin } };
    }

    /// <summary>Eski veri soluk (opaklık <see cref="EskiVeriOpakligi"/>): bağlamın VeriEski'si doğruyken (tasarım 2026-10-02 §3).</summary>
    public static DataTrigger EskiVeriSolugu(Type hedef)
    {
        var tetik = new DataTrigger(hedef) { Binding = new Binding("VeriEski"), Value = true };
        tetik.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = EskiVeriOpakligi });
        return tetik;
    }

    /// <summary>Eski verinin opaklığı (XAML sayfaları da aynı değeri yazar).</summary>
    public const double EskiVeriOpakligi = 0.55;

    /// <summary>Etiket yalnız metni doluyken görünür (boş hata/ileti satırı yığında yer ve aralık kaplamaz).</summary>
    private static Label DoluysaGoster(Label etiket)
    {
        etiket.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(Label.Text), source: etiket, converter: new Converters.DoluIseConverter()));
        return etiket;
    }

    /// <param name="bosMetin">Liste boşken "Gösterilecek kayıt yok." yazılır; bir listenin devamı olan ikinci parça (Çekler ekranında
    /// açık çekin altındaki satırlar) false verir, ileti iki kez görünmez.</param>
    /// <param name="aciklama">Satır düğmesinin ekran okuyucu adı (SemanticProperties.Description): her satırda aynı olan düğme
    /// metnini satırdan ayırt eder.</param>
    public static View Liste<T>(string yol, Func<T, Task>? ac = null, string action = "Aç", Func<T, bool>? gorunur = null,
        Func<T, string>? actionText = null, bool bosMetin = true, Func<T, string>? aciklama = null)
    {
        var l = new VerticalStackLayout { Spacing = 10 };
        l.SetBinding(BindableLayout.ItemsSourceProperty, yol);
        if (bosMetin)
            BindableLayout.SetEmptyView(l, Metin("Gösterilecek kayıt yok."));
        BindableLayout.SetItemTemplate(l, new DataTemplate(() =>
        {
            var row = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(0, 10) };
            var baslik = Bagli("Baslik");
            baslik.FontAttributes = FontAttributes.Bold;
            row.Add(baslik);
            row.Add(BagliMetin("Ozet"));
            if (ac is not null)
                row.Add(ListeDugmesi(ac, action, gorunur, actionText, aciklama));
            row.Add(new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] });
            return row;
        }));
        return l;
    }

    private static Button ListeDugmesi<T>(Func<T, Task> ac, string action, Func<T, bool>? gorunur, Func<T, string>? actionText,
        Func<T, string>? aciklama)
    {
        var b = new Button { Text = action, HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
        b.BindingContextChanged += (_, _) =>
        {
            b.IsVisible = b.BindingContext is T item && (gorunur?.Invoke(item) ?? true);
            if (b.BindingContext is T t)
                b.Text = actionText?.Invoke(t) ?? action;
            if (aciklama is not null && b.BindingContext is T s)
                SemanticProperties.SetDescription(b, aciklama(s));
            else
                b.ClearValue(SemanticProperties.DescriptionProperty);
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
            var c = OnayKutusu();
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

    /// <summary>Bağlı değer <paramref name="deger"/> ise <paramref name="acik"/>, değilse <paramref name="kapali"/>.</summary>
    public sealed class AcikIseConverter(object deger, object acik, object kapali) : IValueConverter
    {
        public object Convert(object? value, Type type, object? parameter, System.Globalization.CultureInfo culture) => Equals(value, deger) ? acik : kapali;
        public object ConvertBack(object? value, Type type, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class NesneVarConverter : IValueConverter
    {
        public object Convert(object? value, Type type, object? parameter, System.Globalization.CultureInfo culture) => value is not null;
        public object ConvertBack(object? value, Type type, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Bütün bağlı değerler doğruysa doğru (EK-01'deki birleşik görünürlük için).</summary>
    private sealed class HepsiDoguConverter : IMultiValueConverter
    {
        public object Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => values.All(v => v is true);
        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}

public abstract class TakipSayfasi<T> : ContentPage, Controls.IYenilenebilir where T : OturumluViewModel
{
    protected readonly T Vm;
    protected readonly VerticalStackLayout Govde = new() { Spacing = 18 };
    protected readonly ScrollView Kaydirici;
    /// <summary>Sayfanın kaydırma yardımcısı (açılan ayrıntı, form ve ilk hatalı alan görünür yere kaydırılır).</summary>
    protected readonly Controls.GorunurYapici Gorunur;
    /// <summary>Sayfa başındaki hata satırı (DurumSatirlari).</summary>
    protected readonly Label HataSatiri;
    /// <summary>Sayfa başındaki ileti satırı (Mesaj; başarılı kayıt).</summary>
    protected readonly Label MesajSatiri;
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
        MesajSatiri = mesaj;
        HataSatiri = TakipUi.DurumSatirlari(root, TakipUi.Tikla("Yenile / tekrar dene", yukle), mesaj, hataYolu: hataYolu);
        // Yükleme hata verse de son başarılı veri görünür kalır ve soluk gösterilir (tasarım 2026-10-02 §3).
        Govde.SetBinding(IsVisibleProperty, nameof(vm.GovdeGorunur));
        Govde.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        Govde.Triggers.Add(TakipUi.EskiVeriSolugu(typeof(VerticalStackLayout)));
        root.Add(Govde);
        Kaydirici = new ScrollView { Content = root };
        Gorunur = new Controls.GorunurYapici(Kaydirici);
        Content = Kaydirici;
        // Başka kayda geçiş, Yeni ve Vazgeç'te kaydedilmemiş değişiklik onayı (tasarım 2026-10-02 §2).
        vm.BirakmaOnayi = ileti => DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon);
    }

    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi: sayfanın yüklemesi.</summary>
    public Task YenileAsync() => _yukle();

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
