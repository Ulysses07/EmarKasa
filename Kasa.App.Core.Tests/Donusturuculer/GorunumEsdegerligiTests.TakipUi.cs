using System.Collections.ObjectModel;
using Kasa.ApiClient;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>TakipUi (kodla yazılmış sayfaların yapı taşları) sabit yazı boyutu ve renkleri yerine Styles.xaml anahtarlarını
/// uygular; kodla yazılmış sayfaların üç durum başlığı (TakipSayfasi, Kasa kontrolü, Dışa aktar) tek yardımcıya
/// (<see cref="TakipUi.DurumSatirlari"/>) indi. Her yapı taşı, değişiklikten önceki kodun birebir kopyasıyla
/// (<see cref="EskiTakipUi"/>, 1efdcb4; görünümü etkilemeyen tıklama işleyicileri hariç) aynı bağlamda kurulur ve çözümlenmiş görsel ağaçları karşılaştırılır.</summary>
public partial class GorunumEsdegerligiTests
{
    public sealed record ListeSatiri(string Baslik, string Ozet, bool Acik);

    public sealed class TakipBaglami
    {
        public bool Mesgul { get; set; }
        public string? Hata { get; set; }
        public string? Mesaj { get; set; }
        public DateTimeOffset? SonGuncelleme { get; set; } = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(3));
        public string Ozet { get; set; } = "Kart borcu 12.500,00 ₺";
        public string Metin { get; set; } = "Açıklama metni";
        public decimal Tutar { get; set; } = 250m;
        public bool Secili { get; set; } = true;
        public bool EditorMu { get; set; } = true;
        public ObservableCollection<ListeSatiri> Satirlar { get; } = [new("Bonus", "Kesim 12, son ödeme 22", true), new("World", "Pasif", false)];
        public ObservableCollection<TakipKanalSecimi> Kanallar { get; } =
            [new(new KanalDto(1, "MEZAT", true, 1, 0)) { Secili = true }, new(new KanalDto(2, "TOPTAN", true, 2, 0))];
        public BenzerlikDurumu Benzerlik { get; } = new();
    }

    public sealed class BenzerlikDurumu
    {
        public bool UyariVar => true;
        public string Uyari => "Aynı gün aynı tutarda kayıt var.";
    }

    public static TheoryData<bool, bool, bool> TakipDurumlari()
    {
        var veri = new TheoryData<bool, bool, bool>();
        foreach (var mesgul in new[] { false, true })
            foreach (var hata in new[] { false, true })
                foreach (var mesaj in new[] { false, true })
                    veri.Add(mesgul, hata, mesaj);
        return veri;
    }

    private static TakipBaglami TakipBaglam(bool mesgul = false, bool hata = true, bool mesaj = true) => new()
    {
        Mesgul = mesgul,
        Hata = hata ? HataMetni : null,
        Mesaj = mesaj ? MesajMetni : null,
    };

    private static void AyniGorunum(Func<View> eski, Func<View> yeni, object baglam)
    {
        GorunumOrtami.Kur();
        View Kur(Func<View> f)
        {
            var v = f();
            v.BindingContext = baglam;
            return v;
        }
        var once = GorunumOrtami.Dok(Kur(eski));
        Assert.True(once.Length > 20, "Eski kod boş döküldü:\n" + once);
        Assert.Equal(once, GorunumOrtami.Dok(Kur(yeni)));
    }

    [Fact]
    public void Takip_metinleri_eski_sabit_boyutlarla_ayni()
    {
        var b = TakipBaglam();
        AyniGorunum(() => EskiTakipUi.Metin("Açıklama"), () => TakipUi.Metin("Açıklama"), b);
        AyniGorunum(() => EskiTakipUi.Bagli("Ozet"), () => TakipUi.Bagli("Ozet"), b);
        AyniGorunum(() => EskiTakipUi.Bagli("Ozet", 13), () => TakipUi.BagliMetin("Ozet"), b);
        AyniGorunum(() => EskiTakipUi.Bagli("Ozet", 18), () => TakipUi.BagliBuyuk("Ozet"), b);
        AyniGorunum(() => { var l = EskiTakipUi.Bagli("Hata"); l.TextColor = Colors.DarkRed; return l; }, () => TakipUi.BagliHata("Hata"), b);
        AyniGorunum(() => EskiTakipUi.Alan("Tutar", EskiTakipUi.Girdi("Tutar", true)), () => TakipUi.Alan("Tutar", TakipUi.Girdi("Tutar", true)), b);
        AyniGorunum(() => EskiTakipUi.Alan("Tarih", EskiTakipUi.Tarih("SonGuncelleme")), () => TakipUi.Alan("Tarih", TakipUi.Tarih("SonGuncelleme")), b);
        AyniGorunum(() => EskiTakipUi.Onay("Onaylıyorum", "Secili"), () => TakipUi.Onay("Onaylıyorum", "Secili"), b);
    }

    [Fact]
    public void Takip_karti_listesi_ve_secimleri_eskisiyle_ayni()
    {
        var b = TakipBaglam();
        AyniGorunum(() => EskiTakipUi.Kart("Kart ayrıntısı", EskiTakipUi.Bagli("Ozet", 18), EskiTakipUi.Metin("Not"), EskiTakipUi.Dugme("Kaydet", "X")),
            () => TakipUi.Kart("Kart ayrıntısı", TakipUi.BagliBuyuk("Ozet"), TakipUi.Metin("Not"), TakipUi.Dugme("Kaydet", "X")), b);
        AyniGorunum(() => EskiTakipUi.Liste<ListeSatiri>("Satirlar", _ => Task.CompletedTask, "Aç", s => s.Acik),
            () => TakipUi.Liste<ListeSatiri>("Satirlar", _ => Task.CompletedTask, "Aç", s => s.Acik), b);
        AyniGorunum(() => EskiTakipUi.Liste<ListeSatiri>("Satirlar"), () => TakipUi.Liste<ListeSatiri>("Satirlar"), b);
        AyniGorunum(() => EskiTakipUi.Liste<ListeSatiri>("Yok"), () => TakipUi.Liste<ListeSatiri>("Yok"), b);   // boş liste görünümü
        AyniGorunum(() => EskiTakipUi.KanalSecimleri("Kanallar"), () => TakipUi.KanalSecimleri("Kanallar"), b);
        AyniGorunum(() => EskiTakipUi.Benzerlik("Benzerlik", "X"), () => TakipUi.Benzerlik("Benzerlik", "X"), b);
        IReadOnlyList<KanalDto> kanallar = [new(1, "MEZAT", true, 1, 0)];
        AyniGorunum(() => EskiTakipUi.Paylar([new TakipPayEditor(kanallar)], () => { }), () => TakipUi.Paylar([new TakipPayEditor(kanallar)], () => { }), b);
    }

    /// <summary>TakipSayfasi başlığı: başlık, açıklama, Yenile, gösterge, hata (koyu kırmızı), ileti (koyu yeşil), son güncelleme.</summary>
    [Theory]
    [MemberData(nameof(TakipDurumlari))]
    public void Takip_sayfasi_basligi_eskisiyle_ayni(bool mesgul, bool hata, bool mesaj)
    {
        GorunumOrtami.Kur();
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api, new AuthViewModel(api)) { Mesgul = mesgul, Hata = hata ? HataMetni : null, Mesaj = mesaj ? MesajMetni : null };
        var yeni = new DenemeSayfasi(vm);
        var eski = EskiTakipUi.Sayfa(vm, "Bildirimler", "Kart kesimi, son ödeme ve kredi taksiti hatırlatmaları");
        Assert.Equal(GorunumOrtami.Dok(eski.Content), GorunumOrtami.Dok(yeni.Content));
        Assert.Equal(eski.BackgroundColor, yeni.BackgroundColor);
        Assert.Equal(eski.Title, yeni.Title);
    }

    private sealed class DenemeSayfasi(AyarlarViewModel vm)
        : TakipSayfasi<AyarlarViewModel>(vm, "Bildirimler", "Kart kesimi, son ödeme ve kredi taksiti hatırlatmaları", () => Task.CompletedTask);

    /// <summary>Kasa kontrolü (KasaKontrolAlanlari.Durum) başlığı: gösterge satır boyunca, ileti örtük stilde.</summary>
    [Theory]
    [MemberData(nameof(TakipDurumlari))]
    public void Kasa_kontrolu_durum_satirlari_eskisiyle_ayni(bool mesgul, bool hata, bool mesaj)
        => AyniGorunum(() => EskiTakipUi.KasaKontrolDurumu(),
            () =>
            {
                var panel = new VerticalStackLayout { Spacing = 12 };
                TakipUi.DurumSatirlari(panel, TakipUi.Tikla("Yenile / tekrar dene", () => Task.CompletedTask), TakipUi.Bagli("Mesaj"), gostergeSolda: false);
                return panel;
            }, TakipBaglam(mesgul, hata, mesaj));

    /// <summary>Dışa aktar (DisariAktarPage) başlığı: ileti satırı yok, Yenile düğmesi sayfanın kendi düğmesi.</summary>
    [Theory]
    [MemberData(nameof(TakipDurumlari))]
    public void Disari_aktar_durum_satirlari_eskisiyle_ayni(bool mesgul, bool hata, bool mesaj)
        => AyniGorunum(() => EskiTakipUi.DisariAktarDurumu(),
            () =>
            {
                var root = new VerticalStackLayout { Padding = new Thickness(28, 22), Spacing = 16, MaximumWidthRequest = 1220 };
                root.Add(new Label { Text = "Gider raporu dışa aktar", Style = (Style)Application.Current!.Resources["LblTakipSayfaBaslik"] });
                TakipUi.DurumSatirlari(root, new Button { Text = "Yenile / tekrar dene", HorizontalOptions = LayoutOptions.Start });
                root.Add(TakipUi.Metin("PDF için rapor tarayıcıda açılır. Ctrl+P menüsünden PDF yazıcısını seçin."));
                root.Add(new VerticalStackLayout
                {
                    Spacing = 4,
                    Children = { new Label { Text = "Başlangıç tarihi", Style = (Style)Application.Current!.Resources["LblTakipKucuk"] }, new DatePicker() },
                });
                return root;
            }, TakipBaglam(mesgul, hata, mesaj));

    /// <summary>Değişiklikten önceki TakipUi ve durum başlıklarının birebir kopyası (yalnız karşılaştırma için).</summary>
    private static class EskiTakipUi
    {
        public static Label Metin(string text) => new() { Text = text, FontSize = 13 };
        public static Label Bagli(string yol, double size = 14) { var l = new Label { FontSize = size }; l.SetBinding(Label.TextProperty, yol); return l; }
        public static View Alan(string ad, View v) => new VerticalStackLayout { Spacing = 5, Children = { new Label { Text = ad, FontSize = 12 }, v } };
        public static Entry Girdi(string yol, bool para = false, bool sayi = false)
        {
            if (para)
            { var p = new Kasa.App.Controls.ParaGirisi(); p.SetBinding(Kasa.App.Controls.ParaGirisi.TutarProperty, yol); return p; }
            var e = new Entry { Keyboard = sayi ? Keyboard.Numeric : Keyboard.Default };
            e.SetBinding(Entry.TextProperty, yol);
            return e;
        }
        public static DatePicker Tarih(string yol) { var d = new DatePicker(); d.SetBinding(DatePicker.DateProperty, yol); return d; }
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
        public static Button Dugme(string text, string command) { var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Start }; b.SetBinding(Button.CommandProperty, command); return b; }
        public static Button Tikla(string text, Func<Task> action)
        {
            var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Start };
            b.Clicked += async (_, _) => { b.IsEnabled = false; try { await action(); } finally { b.IsEnabled = true; } };
            return b;
        }
        public static Border Kart(string title, params View[] views)
        {
            var l = new VerticalStackLayout { Spacing = 12 };
            l.Add(new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold });
            foreach (var v in views)
                l.Add(v);
            return new Border { Style = (Style)Application.Current!.Resources["CardForm"], Content = l };
        }
        public static T Goster<T>(T view, string yol) where T : View { view.SetBinding(VisualElement.IsVisibleProperty, yol); return view; }
        public static View Benzerlik(string yol, string command) => Goster(Kart("Benzer kayıt kontrolü", Bagli(yol + ".Uyari"), Dugme("Ayrı bir işlem, devam et", command)), yol + ".UyariVar");
        public static View Liste<T>(string yol, Func<T, Task>? ac = null, string action = "Aç", Func<T, bool>? gorunur = null, Func<T, string>? actionText = null)
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
                row.Add(Bagli("Ozet", 13));
                if (ac is not null)
                {
                    var b = new Button { Text = action, HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
                    b.BindingContextChanged += (_, _) => { b.IsVisible = b.BindingContext is T item && (gorunur?.Invoke(item) ?? true); if (b.BindingContext is T t) b.Text = actionText?.Invoke(t) ?? action; };
                    row.Add(b);
                }
                row.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("E3E0D6") });
                return row;
            }));
            return l;
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
                row.Add(sil, 2);
                return row;
            }));
            l.Add(rows);
            l.Add(Tikla("Kanal payı ekle", () => { ekle(); return Task.CompletedTask; }));
            return l;
        }
        public static View KanalSecimleri(string yol)
        {
            var rows = new VerticalStackLayout { Spacing = 3 };
            rows.SetBinding(BindableLayout.ItemsSourceProperty, yol);
            BindableLayout.SetItemTemplate(rows, new DataTemplate(() => { var c = new CheckBox(); c.SetBinding(CheckBox.IsCheckedProperty, "Secili"); var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } }; g.Add(c); var l = Bagli("Ad"); l.VerticalOptions = LayoutOptions.Center; g.Add(l, 1); return g; }));
            return rows;
        }

        /// <summary>Eski TakipSayfasi kurucusu (sayfa gövdesi boş).</summary>
        public static ContentPage Sayfa(OturumluViewModel vm, string title, string aciklama)
        {
            var sayfa = new ContentPage { BindingContext = vm, Title = title, BackgroundColor = (Color)Application.Current!.Resources["AppBg"] };
            var govde = new VerticalStackLayout { Spacing = 18 };
            var root = new VerticalStackLayout { Padding = new Thickness(28, 22), Spacing = 16, MaximumWidthRequest = 1160 };
            root.Add(new Label { Text = title, FontSize = 28, FontAttributes = FontAttributes.Bold });
            root.Add(Metin(aciklama));
            root.Add(Tikla("Yenile / tekrar dene", () => Task.CompletedTask));
            var busy = new ActivityIndicator { HorizontalOptions = LayoutOptions.Start };
            busy.SetBinding(ActivityIndicator.IsRunningProperty, nameof(vm.Mesgul));
            root.Add(busy);
            var hata = Bagli(nameof(vm.Hata));
            hata.TextColor = Colors.DarkRed;
            root.Add(hata);
            var mesaj = Bagli(nameof(vm.Mesaj));
            mesaj.TextColor = Colors.DarkGreen;
            root.Add(mesaj);
            var zaman = new Label { FontSize = 12 };
            zaman.SetBinding(Label.TextProperty, new Binding(nameof(vm.SonGuncelleme), stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}"));
            root.Add(zaman);
            govde.SetBinding(VisualElement.IsVisibleProperty, nameof(vm.VeriHazir));
            govde.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.Mesgul), converter: new Kasa.App.Converters.TersIseConverter());
            root.Add(govde);
            sayfa.Content = new ScrollView { Content = root };
            return sayfa;
        }

        /// <summary>Eski KasaKontrolAlanlari.Durum başlığı (gövdesiz).</summary>
        public static View KasaKontrolDurumu()
        {
            var panel = new VerticalStackLayout { Spacing = 12 };
            var busy = new ActivityIndicator();
            busy.SetBinding(ActivityIndicator.IsRunningProperty, "Mesgul");
            var hata = Bagli("Hata");
            hata.TextColor = Colors.DarkRed;
            panel.Add(Tikla("Yenile / tekrar dene", () => Task.CompletedTask));
            panel.Add(busy);
            panel.Add(hata);
            panel.Add(Bagli("Mesaj"));
            var tarih = new Label { FontSize = 12 };
            tarih.SetBinding(Label.TextProperty, new Binding("SonGuncelleme", stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}"));
            panel.Add(tarih);
            return panel;
        }

        /// <summary>Eski DisariAktarPage başlığı ve form öğesi örnekleri.</summary>
        public static View DisariAktarDurumu()
        {
            var root = new VerticalStackLayout { Padding = new Thickness(28, 22), Spacing = 16, MaximumWidthRequest = 1220 };
            root.Add(new Label { Text = "Gider raporu dışa aktar", FontSize = 28, FontAttributes = FontAttributes.Bold });
            var yenile = new Button { Text = "Yenile / tekrar dene", HorizontalOptions = LayoutOptions.Start };
            root.Add(yenile);
            var busy = new ActivityIndicator { HorizontalOptions = LayoutOptions.Start };
            busy.SetBinding(ActivityIndicator.IsRunningProperty, "Mesgul");
            root.Add(busy);
            var hata = new Label { TextColor = Colors.DarkRed };
            hata.SetBinding(Label.TextProperty, "Hata");
            root.Add(hata);
            var zaman = new Label { FontSize = 12 };
            zaman.SetBinding(Label.TextProperty, new Binding("SonGuncelleme", stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}"));
            root.Add(zaman);
            root.Add(new Label { Text = "PDF için rapor tarayıcıda açılır. Ctrl+P menüsünden PDF yazıcısını seçin.", FontSize = 13 });
            root.Add(new VerticalStackLayout { Spacing = 4, Children = { new Label { Text = "Başlangıç tarihi", FontSize = 12 }, new DatePicker() } });
            return root;
        }
    }
}
