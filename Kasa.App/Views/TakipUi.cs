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

    /// <summary>Aşamalı gösterim: uzun kural açıklaması "Ayrıntılar" bağlantısının arkasında (Controls.Ayrinti).</summary>
    public static View Yardim(string metin) => new Controls.Ayrinti { Metin = metin };

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

    public static View Alan(string ad, View v)
    {
        // Görsel etiket ile giriş denetimi ayrı öğeler olsa da erişilebilir ad girişte duyulur.
        SemanticProperties.SetDescription(v, ad);
        return new VerticalStackLayout
        {
            Spacing = 5,
            Children = { new Label { Text = ad, Style = (Style)Application.Current!.Resources["LblTakipKucuk"] }, GirdiMi(v) ? Cerceve(v) : v },
        };
    }

    /// <summary>Form alanlarının tek görünümü (docs/specs/2026-11-26-slop-temizligi.md §7): metin, tutar, seçim ve tarih girdisi XAML
    /// sayfalarındaki gibi FieldBorder çerçevesindedir. WinUI'nin kendi kenarı ve zemini Windows'ta kaldırılır (WinUI.YerelGorunum);
    /// görünür tek sınır bu çerçevedir.</summary>
    public static Border Cerceve(View girdi) => new() { Style = (Style)Application.Current!.Resources["FieldBorder"], Content = girdi };

    /// <summary>Çerçeveye giren denetimler: metin ve tutar girişi, seçici, tarih ve saat.</summary>
    public static bool GirdiMi(View v) => v is InputView or Picker or DatePicker or TimePicker;

    /// <summary>Hatası gösterilen alan (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §1; Controls.FormAlani): başlık,
    /// girdi ve hata varken altında ileti; çerçeve yalnız hata varken (kırmızı) görünür. <paramref name="hatalar"/> modelin
    /// AlanHatalari özelliğinin yolu ("Hatalar"), <paramref name="alan"/> alanın adı ("Tutar"): hata "Hatalar[Tutar]" yolundan gelir.</summary>
    public static Controls.FormAlani Alan(string ad, View v, string hatalar, string alan)
    {
        var f = new Controls.FormAlani { Baslik = ad, Icerik = v, Alan = alan, Cerceveli = GirdiMi(v), TakipStili = true };
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
        // Etiket ayrı bir metin öğesidir; kutu ekran okuyucuda etiketin metniyle adlanır (G-1; UIA'da adsız "CheckBox ''" kalıyordu).
        SemanticProperties.SetDescription(c, text);
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

    /// <summary>Ana düğme (BtnPrimary): açık formun kaydetmesi ya da sayfanın tek asıl işi. Düğme varsayılan olarak ikincildir.</summary>
    public static Button Birincil(Button b)
    {
        b.Style = (Style)Application.Current!.Resources["BtnPrimary"];
        return b;
    }

    /// <summary>Yıkıcı eylem (BtnDanger): kırmızı yazılı ikincil düğme.</summary>
    public static Button Tehlikeli(Button b)
    {
        b.Style = (Style)Application.Current!.Resources["BtnDanger"];
        return b;
    }

    /// <summary>İlgili düğmeler yan yana (sığmazsa alt satıra kayar); düğmeler tam genişlikte alt alta dizilmez (sadeleştirme §8).</summary>
    public static FlexLayout DugmeSatiri(params View[] dugmeler)
    {
        var satir = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        foreach (var d in dugmeler)
        {
            d.Margin = new Thickness(0, 0, 8, 8);
            satir.Add(d);
        }
        return satir;
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
        dugme.Style = (Style)Application.Current!.Resources[birincil ? "BtnPrimary" : "BtnSecondary"];
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
        // Kart başlığı XAML sayfalarının bölüm başlığıyla aynı ölçektedir (tek sistem §7).
        l.Add(new Label { Text = title, Style = (Style)Application.Current!.Resources["LblSection"] });
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
    /// bu sorunu yaşar: içi gizliyken kendisi görünür kalır (EK-01). <paramref name="nesne"/>: yol bir nesneyse dolu olması koşuldur.</summary>
    public static View Editor(View v, string gorunurYol, bool nesne = false)
    {
        var cv = new ContentView { Content = v };
        cv.SetBinding(VisualElement.IsVisibleProperty, new MultiBinding
        {
            Bindings = { new Binding("EditorMu"), new Binding(gorunurYol, converter: nesne ? new NesneVarConverter() : null) },
            Converter = new HepsiDoguConverter(),
        });
        return cv;
    }

    public static View Benzerlik(string yol, string command)
        => Goster(Kart("Benzer kayıt kontrolü", Bagli(yol + ".Uyari"), Dugme("Ayrı işlem olarak kaydet", command)), yol + ".UyariVar");

    /// <summary>Gömülü bölümün durumu (Ayarlar'daki alt limitler, Aylık'taki ay kilidi; tasarım
    /// docs/specs/2026-11-26-slop-temizligi.md §3): yalnız meşgulken gösterge, yalnız doluyken hata ve ileti. Yenile düğmesi yoktur:
    /// bölüm sayfa başlığındaki Yenile ile (sayfanın yenilemesi) yenilenir. Bağlam modelinde Mesgul ve Hata (ya da
    /// <paramref name="hataYolu"/>) beklenir.</summary>
    public static void BolumDurumu(Layout hedef, Label? mesaj = null, string hataYolu = "Hata")
    {
        var gosterge = new ActivityIndicator { WidthRequest = 20, HeightRequest = 20, HorizontalOptions = LayoutOptions.Start };
        gosterge.SetBinding(ActivityIndicator.IsRunningProperty, "Mesgul");
        gosterge.SetBinding(VisualElement.IsVisibleProperty, "Mesgul");
        hedef.Add(gosterge);
        hedef.Add(DoluysaGoster(BagliHata(hataYolu)));
        if (mesaj is not null)
            hedef.Add(DoluysaGoster(mesaj));
    }

    /// <summary>Sayfa başlığı (Controls.SayfaBasligi) bağlam modeline bağlı: Mesgul, Hata (ya da <paramref name="hataYolu"/>), Mesaj,
    /// SonGuncellemeMetni ve VeriEski (OturumluViewModel).</summary>
    public static Controls.SayfaBasligi SayfaBasligi(string baslik, string hataYolu = "Hata")
    {
        var b = new Controls.SayfaBasligi { Baslik = baslik };
        b.SetBinding(Controls.SayfaBasligi.MesgulProperty, "Mesgul");
        b.SetBinding(Controls.SayfaBasligi.HataProperty, hataYolu);
        b.SetBinding(Controls.SayfaBasligi.MesajProperty, "Mesaj");
        b.SetBinding(Controls.SayfaBasligi.SonGuncellemeMetniProperty, nameof(OturumluViewModel.SonGuncellemeMetni));
        b.SetBinding(Controls.SayfaBasligi.VeriEskiProperty, nameof(OturumluViewModel.VeriEski));
        return b;
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

    /// <summary>Eski veri soluk (opaklık <see cref="EskiVeriOpakligi"/>): bağlamın VeriEski'si doğruyken (tasarım 2026-10-02 §3).</summary>
    public static DataTrigger EskiVeriSolugu(Type hedef)
    {
        var tetik = new DataTrigger(hedef) { Binding = new Binding("VeriEski"), Value = true };
        tetik.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = EskiVeriOpakligi });
        return tetik;
    }

    /// <summary>Son veri gövdesi (tasarım 2026-10-02 §3): bağlamın GovdeGorunur'u doğruyken görünür (yükleme hata verse de son
    /// başarılı veri kalır), VeriEski'de <see cref="EskiVeriSolugu"/> ile soluk. Takip sayfalarının gövdesi ve Kasalar alt bölümleri.</summary>
    public static T SonVeriGovdesi<T>(T govde) where T : View
    {
        govde.SetBinding(VisualElement.IsVisibleProperty, nameof(OturumluViewModel.GovdeGorunur));
        govde.Triggers.Add(EskiVeriSolugu(govde.GetType()));
        return govde;
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
    /// <param name="vurgu">Verilirse satırın <c>Veri.Id</c>'si bu kaynağın yolundaki değere eşitken satır vurgulanır (düzenlenen kayıt,
    /// tasarım 2026-10-02 §2; İşlemler ve Alışlar'daki EsitIse deseni).</param>
    /// <param name="bosMetinGizleYolu">Verilirse bu yoldaki değer doğruyken boş liste metni gizlenir (Çekler: gösterilen sorgu
    /// yüklenmedi, liste "boş" değil bilinmiyor).</param>
    public static View Liste<T>(string yol, Func<T, Task>? ac = null, string action = "Aç", Func<T, bool>? gorunur = null,
        Func<T, string>? actionText = null, bool bosMetin = true, Func<T, string>? aciklama = null, (object Kaynak, string Yol)? vurgu = null,
        string? bosMetinGizleYolu = null)
    {
        var l = new VerticalStackLayout { Spacing = 0 };
        l.SetBinding(BindableLayout.ItemsSourceProperty, yol);
        if (bosMetin)
        {
            var bos = Metin("Gösterilecek kayıt yok.");
            if (bosMetinGizleYolu is not null)
                bos.SetBinding(VisualElement.IsVisibleProperty, bosMetinGizleYolu, converter: new Converters.TersIseConverter());
            BindableLayout.SetEmptyView(l, bos);
        }
        // Tablo satırı (docs/specs/2026-11-26-slop-temizligi.md §6): solda başlık ve gri özet, sağda işaretli tutar sütunu (ITutarliSatir) ve
        // en çok bir hafif eylem bağlantısı; satırda ağır düğme yok. Dar ekranda (telefon) eylem başlığın altına iner, başlık ve özet
        // tutarla aynı satırda genişçe kalır.
        BindableLayout.SetItemTemplate(l, new DataTemplate(() =>
        {
            var satir = new Controls.UyumluIzgara
            {
                ColumnSpacing = 16,
                RowSpacing = 2,
                Padding = new Thickness(0, 8),
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) },
                DarSutunlar = [new(GridLength.Star), new(GridLength.Auto)],
                DarEsik = SatirDarEsigi,
            };
            if (vurgu is { } v)
                satir.Triggers.Add(new DataTrigger(typeof(Controls.UyumluIzgara))
                {
                    Binding = new MultiBinding
                    {
                        Converter = new Converters.EsitIseConverter(),
                        Bindings = { new Binding("Veri.Id"), new Binding(v.Yol, source: v.Kaynak) },
                    },
                    Value = true,
                    Setters = { new Setter { Property = VisualElement.BackgroundColorProperty, Value = Application.Current!.Resources["GreenSoft"] } },
                });
            var baslik = Bagli("Baslik");
            baslik.FontAttributes = FontAttributes.Bold;
            var metin = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { baslik, BagliMetin("Ozet") } };
            satir.Add(metin);
            Controls.UyumluIzgara.SetDarSatir(metin, 0);
            var tutar = SatirTutari();
            satir.Add(tutar, 1);
            Controls.UyumluIzgara.SetDarSatir(tutar, 0);
            Controls.UyumluIzgara.SetDarSutun(tutar, 1);
            if (ac is not null)
            {
                var eylem = ListeDugmesi(ac, action, gorunur, actionText, aciklama);
                eylem.HorizontalOptions = LayoutOptions.Start;
                satir.Add(eylem, 2);
                Controls.UyumluIzgara.SetDarSatir(eylem, 1);
                Controls.UyumluIzgara.SetDarSutunAraligi(eylem, 2);
            }
            return new VerticalStackLayout { Spacing = 0, Children = { satir, new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] } } };
        }));
        return l;
    }

    /// <summary>Liste satırının dar düzene geçtiği genişlik: telefonda kart içi yaklaşık 330 px, masaüstü penceresi bunun çok üstünde.</summary>
    public const double SatirDarEsigi = 480;

    /// <summary>Satırın tutar sütunu: ITutarliSatir satırında işaretli ve renkli (gider eksi kırmızı, gelir artı yeşil, sıfır gri).</summary>
    private static Label SatirTutari()
    {
        var tutar = new Label { Style = (Style)Application.Current!.Resources["LblMoney"], VerticalOptions = LayoutOptions.Center };
        tutar.BindingContextChanged += (_, _) =>
        {
            if (tutar.BindingContext is ITutarliSatir s)
            {
                tutar.Text = (string)new Converters.ParaBicimConverter().Convert(s.SatirTutari, typeof(string), Converters.ParaBicimConverter.Isaretli,
                    System.Globalization.CultureInfo.CurrentCulture);
                tutar.TextColor = (Color)new Converters.ParaRenkConverter().Convert(s.SatirTutari, typeof(Color), null, System.Globalization.CultureInfo.CurrentCulture);
            }
            tutar.IsVisible = tutar.BindingContext is ITutarliSatir;
        };
        return tutar;
    }

    private static Button ListeDugmesi<T>(Func<T, Task> ac, string action, Func<T, bool>? gorunur, Func<T, string>? actionText,
        Func<T, string>? aciklama)
    {
        var b = new Button { Text = action, VerticalOptions = LayoutOptions.Center, Style = (Style)Application.Current!.Resources["BtnLink"] };
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
            var row = new Controls.UyumluIzgara { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(new GridLength(145)), new(GridLength.Auto) } };
            row.Add(Cerceve(Secim("Kanallar", "Kanal")));
            row.Add(Cerceve(Girdi("Tutar", true)), 1);
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
            c.SetBinding(SemanticProperties.DescriptionProperty, "Ad");
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
    protected readonly VerticalStackLayout Govde = new() { Spacing = 20 };
    protected readonly ScrollView Kaydirici;
    /// <summary>Sayfanın kaydırma yardımcısı (açılan ayrıntı, form ve ilk hatalı alan görünür yere kaydırılır).</summary>
    protected readonly Controls.GorunurYapici Gorunur;
    /// <summary>Sayfa başlığındaki hata satırı (SayfaBasligi).</summary>
    protected readonly Label HataSatiri;
    /// <summary>Sayfa başındaki ileti satırı (Mesaj; başarılı kayıt).</summary>
    protected readonly Label MesajSatiri;
    private readonly Func<Task> _yukle;
    /// <summary>Sayfa: başlık satırı (başlık ve Yenile; açıklama paragrafı yok, docs/specs/2026-11-26-slop-temizligi.md §2-3) ve gövde.</summary>
    protected TakipSayfasi(T vm, string title, Func<Task> yukle, string hataYolu = nameof(TemelViewModel.Hata))
    {
        Vm = vm;
        BindingContext = vm;
        Title = title;
        _yukle = yukle;
        BackgroundColor = (Color)Application.Current!.Resources["AppBg"];
        var root = new VerticalStackLayout { Padding = new Thickness(28, 22), Spacing = 16, MaximumWidthRequest = 1160 };
        root.SizeChanged += (_, _) => root.Padding = root.Width < 600 ? new Thickness(16, 14) : new Thickness(28, 22);
        // Yenileme izlenen formu koruduğu için (son veri koruma, 2026-10-03) onay sormaz; başlığın Yenile'si YenileAsync'tir.
        var baslik = TakipUi.SayfaBasligi(title, hataYolu);
        root.Add(baslik);
        HataSatiri = baslik.HataEtiketi;
        MesajSatiri = baslik.MesajEtiketi;
        // Yükleme hata verse de son başarılı veri görünür kalır ve soluk gösterilir (tasarım 2026-10-02 §3).
        TakipUi.SonVeriGovdesi(Govde);
        Govde.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        root.Add(Govde);
        Kaydirici = new ScrollView { Content = root };
        Gorunur = new Controls.GorunurYapici(Kaydirici);
        Content = Kaydirici;
        // Başka kayda geçiş ve Yeni'de kaydedilmemiş değişiklik onayı (tasarım 2026-10-02 §2). Vazgeç sormaz: bilerek bırakmaktır.
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
