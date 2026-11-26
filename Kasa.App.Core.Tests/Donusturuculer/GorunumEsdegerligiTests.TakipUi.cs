using System.Collections.ObjectModel;
using Kasa.ApiClient;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>TakipUi (kodla yazılmış sayfaların yapı taşları) sabit yazı boyutu ve renkleri yerine Styles.xaml anahtarlarını
/// uygular; kodla yazılmış sayfaların üç durum başlığı (TakipSayfasi, Kasa kontrolü, Dışa aktar) tek yardımcıya
/// (<see cref="TakipUi.DurumSatirlari"/>) indi. Her yapı taşı, değişiklikten önceki kodun birebir kopyasıyla
/// (<see cref="EskiTakipUi"/>, 1efdcb4; görünümü etkilemeyen tıklama işleyicileri hariç) aynı bağlamda kurulur ve çözümlenmiş görsel ağaçları karşılaştırılır.
/// <para>Bilinçli değişiklikler (menü ve kartlar düzeltmesi, gerçek pencere görüntüleri): eski kopyaya da <see cref="EskiTakipUi.Bilincli"/>
/// ile uygulanır, kalan her şey birebir karşılaştırılır. (1) Durum satırlarında gösterge yalnız meşgulken, hata ve ileti yalnız
/// doluyken görünür (boşken Yenile ile son güncelleme arasında ~130 px boşluk kalıyordu). (2) Onay kutusunun en küçük genişliği 0
/// (WinUI CheckBox'ın MinWidth=120'si etiketi ~110 px uzağa itiyordu). (3) Gösterge ayrı satırda değil, Yenile düğmesinin sağında
/// "İşleniyor…" metniyle aynı yatay satırdadır (gösterge görününce içerik 48 px aşağı kayıyordu); Kasa kontrolünün göstergesi de
/// artık bu satırdadır. (4) Son güncelleme satırı modelin SonGuncellemeMetni'ne bağlıdır: hiç yükleme yokken "Henüz yüklenmedi.",
/// eski veride " · güncel olmayabilir" eki (tasarım 2026-10-02 §3). (5) TakipSayfasi gövdesi GovdeGorunur'a bağlıdır (son veri
/// varken hata da olsa görünür) ve eski veride soluktur.</para></summary>
public partial class GorunumEsdegerligiTests
{
    public sealed record ListeSatiri(string Baslik, string Ozet, bool Acik);

    public sealed class TakipBaglami
    {
        public bool Mesgul { get; set; }
        public string? Hata { get; set; }
        public string? Mesaj { get; set; }
        public DateTimeOffset? SonGuncelleme { get; set; } = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(3));
        public string SonGuncellemeMetni => SonGuncelleme is { } z ? $"Son güncelleme: {z:dd.MM.yyyy HH:mm}" : "Henüz yüklenmedi.";
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
        AyniGorunum(() => { var l = EskiTakipUi.Bagli("Hata"); l.TextColor = (Color)Application.Current!.Resources["Neg"]; return l; }, () => TakipUi.BagliHata("Hata"), b);
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

    public sealed class FormBaglami : System.ComponentModel.INotifyPropertyChanged
    {
        private KartFormu _acikForm;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public KartFormu AcikForm
        {
            get => _acikForm;
            set
            {
                _acikForm = value;
                PropertyChanged?.Invoke(this, new(nameof(AcikForm)));
            }
        }
    }

    /// <summary>Açık formun düğmesi (Kartlar ekranı): kenar kalınlığı her durumda 2'dir, açılınca yalnız kenar rengi (beyaz karta
    /// karşı belirgin, kontrastı MauiKayitTutarliligiTests'te) ve zemin değişir; düğme genişlemez, yanındakileri itmez. Kapalıyken
    /// kenar düğmenin kendi rengindedir (birincil: zemini Green, ikincil: BtnSecondary kenarı Border).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Form_dugmesi_acilinca_kenar_kalinligi_ve_ic_boslugu_degismez(bool birincil)
    {
        GorunumOrtami.Kur();
        var baglam = new FormBaglami();
        var dugme = TakipUi.FormDugmesi("Ödeme kaydet", "X", KartFormu.Odeme, nameof(FormBaglami.AcikForm), birincil);
        dugme.BindingContext = baglam;
        var kapali = (dugme.BorderWidth, dugme.Padding, dugme.WidthRequest, dugme.BorderColor, dugme.BackgroundColor);
        Assert.Equal(2, dugme.BorderWidth);
        Assert.Equal(Renk(birincil ? "Green" : "Border"), dugme.BorderColor);
        Assert.Null(SemanticProperties.GetHint(dugme));

        baglam.AcikForm = KartFormu.Odeme;
        Assert.Equal(kapali.BorderWidth, dugme.BorderWidth);
        Assert.Equal(kapali.Padding, dugme.Padding);
        Assert.Equal(kapali.WidthRequest, dugme.WidthRequest);
        Assert.Equal(Renk(TakipUi.AcikFormKenari), dugme.BorderColor);
        Assert.Equal(Renk(birincil ? "GreenDark" : "GreenSoft"), dugme.BackgroundColor);
        Assert.Equal("Açık", SemanticProperties.GetHint(dugme));

        baglam.AcikForm = KartFormu.Harcama;   // başka form açıldı: bu düğme kapalı görünüme döner
        Assert.Equal((kapali.BorderWidth, kapali.Padding, kapali.WidthRequest, kapali.BorderColor, kapali.BackgroundColor),
            (dugme.BorderWidth, dugme.Padding, dugme.WidthRequest, dugme.BorderColor, dugme.BackgroundColor));
        Assert.Null(SemanticProperties.GetHint(dugme));
    }

    private static Color Renk(string anahtar) => (Color)Application.Current!.Resources[anahtar];

    /// <summary>EK-01: "editörde ama içi gizli" durumunda sarmalayıcının kendisi de görünmez olmalı, yoksa dikey
    /// yığında (Spacing) boş bir satır kalır.</summary>
    [Fact]
    public void Editor_ikinci_parametreyle_hem_EditorMu_hem_kosula_bagliyken_gorunur()
    {
        GorunumOrtami.Kur();
        var v = TakipUi.Editor(new Label { Text = "İçerik" }, nameof(TakipBaglami.Secili));

        v.BindingContext = new TakipBaglami { EditorMu = true, Secili = true };
        Assert.True(v.IsVisible);

        v.BindingContext = new TakipBaglami { EditorMu = true, Secili = false };
        Assert.False(v.IsVisible);

        v.BindingContext = new TakipBaglami { EditorMu = false, Secili = true };
        Assert.False(v.IsVisible);
    }

    /// <summary>KS-03: Yenile satırının dışında duran, bağımsız bir yükleme göstergesi ve altındaki hata metni ikisi de
    /// boşken yer kaplamamalı (Kasalar'daki "Kart ve kredi takibi" kutusundaki ~87 px boşluk).</summary>
    [Fact]
    public void MesgulVeHata_bosken_gosterge_ve_hata_gorunmez_doluyken_gorunur()
    {
        GorunumOrtami.Kur();
        var (gosterge, hata) = TakipUi.MesgulVeHata("Mesgul", "Hata");
        var panel = new VerticalStackLayout { Children = { gosterge, hata } };

        panel.BindingContext = TakipBaglam(mesgul: false, hata: false, mesaj: false);
        Assert.False(gosterge.IsVisible);
        Assert.False(gosterge.IsRunning);
        Assert.False(hata.IsVisible);

        panel.BindingContext = TakipBaglam(mesgul: true, hata: false, mesaj: false);
        Assert.True(gosterge.IsVisible);
        Assert.True(gosterge.IsRunning);
        Assert.False(hata.IsVisible);

        panel.BindingContext = TakipBaglam(mesgul: false, hata: true, mesaj: false);
        Assert.False(gosterge.IsVisible);
        Assert.True(hata.IsVisible);
        Assert.Equal(HataMetni, hata.Text);
    }

    /// <summary>Değişiklikten önceki TakipUi ve durum başlıklarının birebir kopyası (yalnız karşılaştırma için).</summary>
    private static class EskiTakipUi
    {
        /// <summary>Eski koda sonradan bilerek uygulanan görünüm değişiklikleri (sınıf belgesindeki liste).</summary>
        public static class Bilincli
        {
            public static Label Doluysa(Label etiket)
            {
                etiket.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(Label.Text), source: etiket,
                    converter: new Kasa.App.Converters.DoluIseConverter()));
                return etiket;
            }

            public static CheckBox OnayKutusu() => new() { MinimumWidthRequest = 0 };

            /// <summary>(5): gövde GovdeGorunur'a bağlı, eski veride soluk.</summary>
            public static void Govde(VerticalStackLayout govde)
            {
                govde.SetBinding(VisualElement.IsVisibleProperty, "GovdeGorunur");
                var tetik = new DataTrigger(typeof(VerticalStackLayout)) { Binding = new Binding("VeriEski"), Value = true };
                tetik.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = 0.55 });
                govde.Triggers.Add(tetik);
            }
        }

        public static Label Metin(string text) => new() { Text = text, FontSize = 13, TextColor = (Color)Application.Current!.Resources["Sub"] };
        public static Label Bagli(string yol, double size = 14) { var l = new Label { FontSize = size }; if (size == 13) l.TextColor = (Color)Application.Current!.Resources["Sub"]; l.SetBinding(Label.TextProperty, yol); return l; }
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
            var c = Bilincli.OnayKutusu();
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
        public static View Benzerlik(string yol, string command) => Goster(Kart("Benzer kayıt kontrolü", Bagli(yol + ".Uyari"), Dugme("Ayrı işlem olarak kaydet", command)), yol + ".UyariVar");
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
            BindableLayout.SetItemTemplate(rows, new DataTemplate(() => { var c = Bilincli.OnayKutusu(); c.SetBinding(CheckBox.IsCheckedProperty, "Secili"); var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) } }; g.Add(c); var l = Bagli("Ad"); l.VerticalOptions = LayoutOptions.Center; g.Add(l, 1); return g; }));
            return rows;
        }

    }
}
