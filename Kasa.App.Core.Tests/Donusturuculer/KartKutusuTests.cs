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
        Assert.Equal(1, kutu.StrokeThickness);
        Assert.Null(SemanticProperties.GetHint(dugme));
        kutu.Secili = true;
        Assert.Equal(2.5, kutu.StrokeThickness);
        Assert.Equal(Renk("KartYesilYazi"), Assert.IsType<SolidColorBrush>(kutu.Stroke).Color);
        Assert.Equal("Ayrıntısı açık", SemanticProperties.GetHint(dugme));
        kutu.Secili = false;
        Assert.Null(SemanticProperties.GetHint(dugme));
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
        public void DisconnectHandler() { }
    }
}
