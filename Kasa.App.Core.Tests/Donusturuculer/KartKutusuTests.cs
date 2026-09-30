using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.App.Controls;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Kart kutusu ve kart ızgarası bileşenleri gerçek uygulama kaynaklarıyla (GorunumOrtami): kutu satırın bilgilerini
/// banka renginde gösterir, tıklama yüzeyi adlı şeffaf düğmedir; ızgara kutuları kaynaktan kurar, açık kutuyu işaretler,
/// ayrıntıyı yalnız bir kutu açıkken gösterir. Yerleşimin kendisi KartIzgarasiHesabiTests'te sınanır; burada yalnız ızgaranın
/// çocukları hangi genişlikle ölçtüğü (ölçüm sözleşmesi) sınanır.</summary>
public class KartKutusuTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);

    private static KartTakipSatiri Satir(int id, string ad, decimal borc = 12500, decimal limit = 40000, bool yeniTakip = true,
        KartEkstreDto[]? ekstreler = null)
        => new(new KartTakipDto(id, 1, ad, yeniTakip, true, null, 10, 20, limit, borc, 0, ekstreler ?? [], [], []),
            new IslemEditorTests.SabitZaman(Bugun));

    private static Color Renk(string anahtar) => (Color)Application.Current!.Resources[anahtar];

    [Fact]
    public void Kutu_satirin_bilgilerini_banka_renginde_gosterir()
    {
        GorunumOrtami.Kur();
        var satir = Satir(3, "Garanti Bonus", yeniTakip: false, ekstreler: [new KartEkstreDto(7, new(2026, 9, 10), new(2026, 9, 20), 12500, 0, 12500, null)]);
        var kutu = new KartKutusu { BindingContext = satir };
        var metinler = kutu.GetVisualTreeDescendants().OfType<Label>().Where(l => l.IsVisible).Select(l => l.Text).Order().ToList();
        var beklenen = new[] { "Garanti Bonus", "Kart borcu", "12.500,00 ₺", "Limit 40.000,00 ₺", "Son ödeme 20.09.2026", "Son ödeme geçti", "Eski takip" };
        Assert.Equal(beklenen.Order(), metinler);
        Assert.Same(satir, kutu.Satir);
        Assert.Equal(Renk("KartYesilZemin"), kutu.BackgroundColor);
        Assert.All(kutu.GetVisualTreeDescendants().OfType<Label>().Where(l => l.Text is "Garanti Bonus" or "12.500,00 ₺"),
            l => Assert.Equal(Renk("KartYesilYazi"), l.TextColor));
        var dugme = kutu.GetVisualTreeDescendants().OfType<Button>().Single();
        Assert.Equal("Garanti Bonus kartı", SemanticProperties.GetDescription(dugme));
        // Yazılar girdiyi geçirir ve erişilebilirlik ağacında ayrı öğe değildir: kutunun tek UIA öğesi adlı düğmedir.
        Assert.All(kutu.GetVisualTreeDescendants().OfType<Label>(), l =>
        {
            Assert.True(l.InputTransparent);
            Assert.False(AutomationProperties.GetIsInAccessibleTree(l));
        });
    }

    [Fact]
    public void Doluluk_cubugu_orani_gosterir_limit_sifirsa_gizlenir()
    {
        GorunumOrtami.Kur();
        static Grid Cubuk(KartKutusu k) => k.GetVisualTreeDescendants().OfType<Grid>().Single(g => g.Children.OfType<BoxView>().Count() == 2);
        var kutu = new KartKutusu { BindingContext = Satir(3, "Garanti Bonus") };
        Assert.True(Cubuk(kutu).IsVisible);
        Assert.Equal(0.3125, Cubuk(kutu).ColumnDefinitions[0].Width.Value);
        Assert.Equal(0.6875, Cubuk(kutu).ColumnDefinitions[1].Width.Value);
        Assert.False(Cubuk(new KartKutusu { BindingContext = Satir(4, "Akbank", limit: 0) }).IsVisible);
    }

    [Fact]
    public void Secili_kutunun_kenari_kalinlasir_ve_yazi_rengini_alir()
    {
        GorunumOrtami.Kur();
        var kutu = new KartKutusu { BindingContext = Satir(3, "Garanti Bonus") };
        var dugme = kutu.GetVisualTreeDescendants().OfType<Button>().Single();
        const string ozet = "Kart borcu 12.500,00 ₺, Limit 40.000,00 ₺";
        Assert.Equal(1, kutu.StrokeThickness);
        Assert.Equal(ozet, SemanticProperties.GetHint(dugme));
        kutu.Secili = true;
        Assert.Equal(2.5, kutu.StrokeThickness);
        Assert.Equal(Renk("KartYesilYazi"), Assert.IsType<SolidColorBrush>(kutu.Stroke).Color);
        Assert.Equal(ozet + "; Ayrıntısı açık", SemanticProperties.GetHint(dugme));
        kutu.Secili = false;
        Assert.Equal(ozet, SemanticProperties.GetHint(dugme));
        Assert.Equal("Garanti Bonus kartı", SemanticProperties.GetDescription(dugme));
    }

    /// <summary>Seçimle kalınlaşan kenar kutuyu büyütmez (satır zıplamaz): kalınlık farkı kutunun iç boşluğundan düşülür.</summary>
    [Fact]
    public void Secim_kutunun_olcusunu_degistirmez()
    {
        GorunumOrtami.Kur();
        var kutu = new KartKutusu { BindingContext = Satir(3, "Garanti Bonus") };
        var yeni = new YeniKartKutusu();
        Size Olcu(IContentView v) => v.CrossPlatformMeasure(272, double.PositiveInfinity);
        var (kutuOlcusu, yeniOlcusu) = (Olcu(kutu), Olcu(yeni));
        kutu.Secili = true;
        yeni.Secili = true;
        Assert.Equal(kutuOlcusu, Olcu(kutu));
        Assert.Equal(yeniOlcusu, Olcu(yeni));
        kutu.Secili = false;
        yeni.Secili = false;
        Assert.Equal(kutuOlcusu, Olcu(kutu));
        Assert.Equal(yeniOlcusu, Olcu(yeni));
    }

    /// <summary>Kutu zeminleri BackgroundColor düz boyasıdır; Background fırçası yazılmaz (dotnet/maui#38813 dersi).</summary>
    [Fact]
    public void Kutular_Background_firca_ozelligini_yazmaz()
    {
        GorunumOrtami.Kur();
        var kutu = new KartKutusu { BindingContext = Satir(3, "Garanti Bonus"), Secili = true };
        var yeni = new YeniKartKutusu { Secili = true };
        Assert.False(kutu.IsSet(VisualElement.BackgroundProperty));
        Assert.False(yeni.IsSet(VisualElement.BackgroundProperty));
        Assert.All(kutu.GetVisualTreeDescendants().Concat(yeni.GetVisualTreeDescendants()).OfType<VisualElement>(),
            v => Assert.False(v.IsSet(VisualElement.BackgroundProperty)));
    }

    [Fact]
    public void Baglam_satir_degilse_kutu_bosalir()
    {
        GorunumOrtami.Kur();
        var kutu = new KartKutusu { BindingContext = Satir(3, "Garanti Bonus", yeniTakip: false), Secili = true };
        kutu.BindingContext = null;
        Assert.Null(kutu.Satir);
        Assert.DoesNotContain(kutu.GetVisualTreeDescendants().OfType<Label>(), l => l.IsVisible && !string.IsNullOrEmpty(l.Text) && l.Text != "Kart borcu");
        Assert.False(kutu.IsSet(VisualElement.BackgroundColorProperty));
        var dugme = kutu.GetVisualTreeDescendants().OfType<Button>().Single();
        Assert.Null(SemanticProperties.GetDescription(dugme));
        Assert.Null(SemanticProperties.GetHint(dugme));
        kutu.BindingContext = Satir(4, "Akbank");
        Assert.Equal("Akbank kartı", SemanticProperties.GetDescription(dugme));
        Assert.Equal(Renk("KartKirmiziZemin"), kutu.BackgroundColor);
    }

    [Fact]
    public void Izgara_kutulari_kaynaktan_kurar_yeni_kart_kutusu_ve_ayrinti_sondadir()
    {
        GorunumOrtami.Kur();
        var kartlar = new ObservableCollection<KartTakipSatiri> { Satir(5, "Garanti"), Satir(7, "Akbank") };
        var ayrinti = new Label();
        var izgara = new KartIzgarasi { ItemsSource = kartlar, Ayrinti = ayrinti, YeniGorunur = true };
        Assert.Equal(2, izgara.Kutular.Count);
        Assert.Equal(new object[] { izgara.Kutular[0], izgara.Kutular[1], izgara.YeniKutusu, ayrinti }, izgara.Cast<object>().ToArray());
        kartlar.Add(Satir(9, "Ziraat"));
        Assert.Equal(3, izgara.Kutular.Count);
        Assert.Same(izgara.YeniKutusu, izgara[3]);
        Assert.Same(ayrinti, izgara[4]);
        Assert.False(ayrinti.IsVisible);   // açık kart yok
    }

    /// <summary>Kaynak değişiklikleri artımlı işlenir: TakipMetni.Doldur (Clear + N×Add) N kutu kurar, mevcut kutular korunur.</summary>
    [Fact]
    public void Kaynaga_eklenen_kart_yalniz_kendi_kutusunu_kurar()
    {
        GorunumOrtami.Kur();
        var kartlar = new ObservableCollection<KartTakipSatiri> { Satir(1, "Eski") };
        var ayrinti = new Label();
        var izgara = new KartIzgarasi { ItemsSource = kartlar, Ayrinti = ayrinti, YeniGorunur = true };
        var kurulan = new List<KartKutusu>();
        TakipMetni.Doldur(kartlar, Enumerable.Range(1, 10).Select(i => Satir(i, "Kart " + i)));
        Assert.Equal(10, izgara.Kutular.Count);
        // Her ekleme yalnız yeni kutuyu kurar: önceki eklemelerde kurulan kutu örnekleri aynen yerindedir.
        kartlar.Clear();
        for (var i = 1; i <= 10; i++)
        {
            kartlar.Add(Satir(i, "Kart " + i));
            kurulan.Add(izgara.Kutular[^1]);
            Assert.Equal(kurulan, izgara.Kutular);
        }
        Assert.Equal(Enumerable.Range(1, 10), izgara.Kutular.Select(k => k.Satir!.Veri.Id));
        kartlar.Insert(0, Satir(42, "Başa"));
        Assert.Equal(kurulan, izgara.Kutular.Skip(1));
        Assert.Equal(42, izgara.Kutular[0].Satir!.Veri.Id);
        Assert.Equal(izgara.Kutular.Cast<object>().Append(izgara.YeniKutusu).Append(ayrinti), izgara.Cast<object>());
    }

    [Fact]
    public void Kaynaktan_cikan_tasinan_ve_degisen_kart_kutusunu_gunceller()
    {
        GorunumOrtami.Kur();
        var kartlar = new ObservableCollection<KartTakipSatiri> { Satir(5, "Garanti"), Satir(7, "Akbank"), Satir(9, "Ziraat") };
        var ayrinti = new Label();
        var izgara = new KartIzgarasi { ItemsSource = kartlar, Ayrinti = ayrinti, YeniGorunur = true, AcikKartId = 11 };
        var (k5, k7, k9) = (izgara.Kutular[0], izgara.Kutular[1], izgara.Kutular[2]);
        var isleyici = new SarimliIsleyici();
        k7.Handler = isleyici;

        kartlar.RemoveAt(1);   // çıkan kutu ızgaradan ayrılır, bağlaması ve işleyicisi kopar
        Assert.Equal([k5, k9], izgara.Kutular);
        Assert.DoesNotContain(k7, izgara.Cast<object>());
        Assert.True(isleyici.Koptu);
        izgara.SecCommand = new RelayCommand<KartTakipSatiri>(_ => { });
        Assert.Same(izgara.SecCommand, k5.Command);
        Assert.NotSame(izgara.SecCommand, k7.Command);

        kartlar[1] = Satir(11, "İş Bankası");   // değişen kart: kutu yerinde kalır, yeni satırı gösterir ve açık kart olur
        Assert.Equal([k5, k9], izgara.Kutular);
        Assert.Equal(11, k9.Satir!.Veri.Id);
        Assert.Same(kartlar[1], k9.CommandParameter);
        Assert.True(k9.Secili);
        Assert.True(ayrinti.IsVisible);

        kartlar.Move(0, 1);
        Assert.Equal([k9, k5], izgara.Kutular);
        Assert.Equal(0, izgara.AcikIndeks);
        Assert.Equal(new object[] { k9, k5, izgara.YeniKutusu, ayrinti }, izgara.Cast<object>().ToArray());

        kartlar.Clear();   // Reset
        Assert.Empty(izgara.Kutular);
        Assert.Equal(new object[] { izgara.YeniKutusu, ayrinti }, izgara.Cast<object>().ToArray());
        Assert.False(ayrinti.IsVisible);
    }

    [Fact]
    public void Kaynak_degisince_eski_kaynagin_aboneligi_kalkar()
    {
        GorunumOrtami.Kur();
        var eski = new ObservableCollection<KartTakipSatiri> { Satir(5, "Garanti") };
        var izgara = new KartIzgarasi { ItemsSource = eski };
        izgara.ItemsSource = new ObservableCollection<KartTakipSatiri> { Satir(7, "Akbank"), Satir(9, "Ziraat") };
        var kutular = izgara.Kutular.ToList();
        eski.Add(Satir(11, "QNB"));
        eski.Clear();
        Assert.Equal(kutular, izgara.Kutular);
    }

    [Fact]
    public void Acik_kartin_kutusu_isaretlenir_ve_ayrinti_gorunur()
    {
        GorunumOrtami.Kur();
        var ayrinti = new Label();
        var izgara = new KartIzgarasi { ItemsSource = new[] { Satir(5, "Garanti"), Satir(7, "Akbank") }, Ayrinti = ayrinti, YeniGorunur = true };
        izgara.AcikKartId = 7;
        Assert.Equal(1, izgara.AcikIndeks);
        Assert.Equal(new[] { false, true }, izgara.Kutular.Select(k => k.Secili));
        Assert.True(ayrinti.IsVisible);
        izgara.AcikKartId = null;
        Assert.Equal(-1, izgara.AcikIndeks);
        Assert.DoesNotContain(izgara.Kutular, k => k.Secili);
        Assert.False(ayrinti.IsVisible);
    }

    [Fact]
    public void Yeni_kart_formu_acikken_yeni_kart_kutusu_acik_izleyicide_yeni_kart_kutusu_yok()
    {
        GorunumOrtami.Kur();
        var izgara = new KartIzgarasi { ItemsSource = new[] { Satir(5, "Garanti"), Satir(7, "Akbank") }, Ayrinti = new Label(), YeniGorunur = true, YeniAcik = true };
        Assert.Equal(2, izgara.AcikIndeks);
        Assert.True(izgara.YeniKutusu.Secili);
        Assert.Equal("Form açık", SemanticProperties.GetHint(izgara.YeniKutusu.GetVisualTreeDescendants().OfType<Button>().Single()));
        izgara.YeniGorunur = false;
        Assert.False(izgara.YeniKutusu.IsVisible);
        Assert.Equal(-1, izgara.AcikIndeks);
    }

    [Fact]
    public void Kutuya_ve_yeni_kart_kutusuna_dokunmak_komutlari_calistirir()
    {
        GorunumOrtami.Kur();
        var kartlar = new[] { Satir(5, "Garanti"), Satir(7, "Akbank") };
        KartTakipSatiri? secilen = null;
        var yeni = 0;
        var izgara = new KartIzgarasi
        {
            ItemsSource = kartlar,
            SecCommand = new RelayCommand<KartTakipSatiri>(s => secilen = s),
            YeniCommand = new RelayCommand(() => yeni++),
            YeniGorunur = true,
        };
        var dugme = izgara.Kutular[1].GetVisualTreeDescendants().OfType<Button>().Single();
        dugme.Command!.Execute(dugme.CommandParameter);
        Assert.Same(kartlar[1], secilen);
        var yeniDugme = izgara.YeniKutusu.GetVisualTreeDescendants().OfType<Button>().Single();
        Assert.Equal("Yeni kart ekle", SemanticProperties.GetDescription(yeniDugme));
        yeniDugme.Command!.Execute(null);
        Assert.Equal(1, yeni);
    }

    /// <summary>Ölçüm sözleşmesi (KartIzgarasiHesabi belgesi): Hesapla'ya giden kutu yükseklikleri yerleşimin kutu genişliğiyle
    /// ölçülmüş olmalıdır; kutunun yüksekliği genişliğe bağlıdır (metin sarımı). Sonlu genişlikte kutular hesabın kutu
    /// genişliğiyle, ayrıntı tam genişlikle ölçülür. Sonsuz genişlikle ölçülüp başka genişlikle yerleştirilen ızgara çocuklarını
    /// yerleşimde o genişlikle yeniden ölçer, ölçüsünü geçersiz kılar ve sonraki sonsuz ölçümde son yerleşim genişliğini kullanır.</summary>
    [Fact]
    public void Kutular_yerlesimin_kutu_genisligiyle_olculur()
    {
        GorunumOrtami.Kur();
        var ayrinti = new Label();
        var izgara = new KartIzgarasi { ItemsSource = new[] { Satir(5, "Garanti"), Satir(7, "Akbank") }, Ayrinti = ayrinti, AcikKartId = 5 };
        var isleyiciler = izgara.Kutular.Cast<View>().Append(ayrinti).Select(v =>
        {
            var i = new SarimliIsleyici();
            v.Handler = i;
            return i;
        }).ToList();
        var yerlesim = (ICrossPlatformLayout)izgara;

        // 1136 px: dört sütun, kutu 272 px (yükseklik 148); ayrıntı 1136 px (yükseklik 36).
        var boyut = yerlesim.CrossPlatformMeasure(1136, double.PositiveInfinity);
        Assert.Equal([272d, 272d, 1136d], isleyiciler.Select(i => i.Genislikler.Last()));
        Assert.Equal(new Size(1136, 148 + 16 + 36), boyut);

        // Sonsuz genişlik (henüz yerleşim yok): tek sütun, 220 px; iki satır ve arada ayrıntı.
        boyut = yerlesim.CrossPlatformMeasure(double.PositiveInfinity, double.PositiveInfinity);
        Assert.Equal([220d, 220d, 220d], isleyiciler.Select(i => i.Genislikler.Last()));
        Assert.Equal(new Size(220, 182 + 16 + 182 + 16 + 182), boyut);

        // 1136 px'e yerleştirilir: çocuklar yeniden ölçülür, dikdörtgenler doğru yükseklikte, ölçü geçersiz kılınır.
        var gecersiz = 0;
        izgara.MeasureInvalidated += (_, _) => gecersiz++;
        var yerlesen = yerlesim.CrossPlatformArrange(new Rect(0, 0, 1136, 578));
        Assert.Equal([272d, 272d, 1136d], isleyiciler.Select(i => i.Genislikler.Last()));
        Assert.Equal(new Rect(288, 0, 272, 148), izgara.Kutular[1].Frame);
        Assert.Equal(new Rect(0, 164, 1136, 36), ayrinti.Frame);
        Assert.Equal(200, yerlesen.Height);
        Assert.Equal(1, gecersiz);

        // Sonraki sonsuz ölçüm son yerleşim genişliğini kullanır; aynı genişlikte yeniden yerleşim yeniden ölçmez.
        boyut = yerlesim.CrossPlatformMeasure(double.PositiveInfinity, double.PositiveInfinity);
        Assert.Equal(new Size(1136, 200), boyut);
        var olcumSayisi = isleyiciler.Sum(i => i.Genislikler.Count);
        yerlesim.CrossPlatformArrange(new Rect(0, 0, 1136, 200));
        Assert.Equal(olcumSayisi, isleyiciler.Sum(i => i.Genislikler.Count));
        Assert.Equal(1, gecersiz);
    }

    /// <summary>İç boşluk (Padding) ölçüye eklenir, hesap iç genişlikle yapılır ve dikdörtgenler boşluk kadar kayar.</summary>
    [Fact]
    public void Ic_bosluk_olcuye_eklenir_ve_yerlesimi_kaydirir()
    {
        GorunumOrtami.Kur();
        var ayrinti = new Label();
        var izgara = new KartIzgarasi
        {
            ItemsSource = new[] { Satir(5, "Garanti"), Satir(7, "Akbank") },
            Ayrinti = ayrinti,
            AcikKartId = 7,
            Padding = new Thickness(10, 20, 30, 40),
        };
        var isleyiciler = izgara.Kutular.Cast<View>().Append(ayrinti).Select(v =>
        {
            var i = new SarimliIsleyici();
            v.Handler = i;
            return i;
        }).ToList();
        var yerlesim = (ICrossPlatformLayout)izgara;
        var boyut = yerlesim.CrossPlatformMeasure(1176, double.PositiveInfinity);   // iç genişlik 1136: dört sütun
        Assert.Equal([272d, 272d, 1136d], isleyiciler.Select(i => i.Genislikler.Last()));
        Assert.Equal(new Size(1176, 200 + 60), boyut);
        var gecersiz = 0;
        izgara.MeasureInvalidated += (_, _) => gecersiz++;
        var yerlesen = yerlesim.CrossPlatformArrange(new Rect(0, 0, 1176, 260));
        Assert.Equal(new Rect(10, 20, 272, 148), izgara.Kutular[0].Frame);
        Assert.Equal(new Rect(298, 20, 272, 148), izgara.Kutular[1].Frame);
        Assert.Equal(new Rect(10, 184, 1136, 36), ayrinti.Frame);
        Assert.Equal(new Size(1176, 260), yerlesen);
        Assert.Equal(0, gecersiz);   // ölçüm ve yerleşim genişliği aynı: yeniden ölçüm ve geçersiz kılma yok
    }

    /// <summary>Platform işleyicisi yerine geçen sahte işleyici: yükseklik genişlikle ters orantılıdır (metin sarımı benzetimi);
    /// ölçüldüğü genişlikleri kaydeder.</summary>
    private sealed class SarimliIsleyici : IViewHandler
    {
        public List<double> Genislikler { get; } = [];
        public Size GetDesiredSize(double widthConstraint, double heightConstraint)
        {
            Genislikler.Add(widthConstraint);
            return new Size(widthConstraint, double.IsFinite(widthConstraint) ? Math.Ceiling(40000 / widthConstraint) : 0);
        }
        public void PlatformArrange(Rect frame) { }
        public bool HasContainer { get; set; }
        public object? ContainerView => null;
        public IView? VirtualView { get; private set; }
        IElement? IElementHandler.VirtualView => VirtualView;
        public object? PlatformView => null;
        public IMauiContext? MauiContext => null;
        public void SetMauiContext(IMauiContext mauiContext) { }
        public void SetVirtualView(IElement view) => VirtualView = (IView)view;
        public void UpdateValue(string property) { }
        public void Invoke(string command, object? args = null) { }
        public bool Koptu { get; private set; }
        public void DisconnectHandler() => Koptu = true;
    }
}
