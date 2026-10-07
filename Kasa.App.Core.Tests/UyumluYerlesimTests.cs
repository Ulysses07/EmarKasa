using Kasa.App.Controls;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

public class UyumluYerlesimTests
{
    // Yerel metin/girdi işleyicisi bulunmayan testte sıfır ölçünün gizlediği taşmaları önler.
    private static T Olculen<T>(T view) where T : View
    {
        view.Handler = new OlcuIsleyici();
        return view;
    }
    private static void Yerlestir(Layout izgara, double genislik, double yukseklik)
    {
        var yerlesim = (Microsoft.Maui.ILayout)izgara;
        yerlesim.CrossPlatformMeasure(genislik, yukseklik);
        yerlesim.CrossPlatformArrange(new Rect(0, 0, genislik, yukseklik));
    }
    [Fact]
    public void Dar_form_alanlari_ustuste_sigarken_genis_duzen_ve_taslak_korunur()
    {
        GorunumOrtami.Kur();
        var izgara = new UyumluIzgara
        {
            ColumnDefinitions = { new(GridLength.Star), new(new GridLength(145)), new(GridLength.Auto) },
            ColumnSpacing = 10,
            RowSpacing = 8,
        };
        var kanal = Olculen(new Picker { MinimumWidthRequest = 180, HeightRequest = 44 });
        var tutar = Olculen(new Entry { Text = "125,50", HeightRequest = 44 });
        var sil = Olculen(new Button { Text = "Kaldır", WidthRequest = 88, HeightRequest = 44 });
        izgara.Add(kanal);
        izgara.Add(tutar, 1);
        izgara.Add(sil, 2);
        Yerlestir(izgara, 760, 200);
        Assert.True(kanal.Frame.Width >= 180);
        Assert.True(tutar.Frame.Height >= 44);
        Assert.Equal(kanal.Frame.Top, tutar.Frame.Top);
        Assert.True(tutar.Frame.Left >= kanal.Frame.Right);
        Yerlestir(izgara, 320, 200);
        Assert.True(tutar.Frame.Top >= kanal.Frame.Bottom);
        Assert.True(sil.Frame.Top >= tutar.Frame.Bottom);
        Assert.All(izgara.Children, alan => Assert.True(alan.Frame.Right <= 320));
        Yerlestir(izgara, 760, 200);
        Assert.Equal(kanal.Frame.Top, tutar.Frame.Top);
        Assert.True(sil.Frame.Left >= tutar.Frame.Right);
        Assert.Equal("125,50", tutar.Text);
    }

    [Fact]
    public void Dar_ozet_satirinda_tutar_metnin_altina_gecer_avatar_metnin_yaninda_kalir()
    {
        GorunumOrtami.Kur();
        var izgara = new UyumluIzgara
        {
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            DarSutunlar = [new(GridLength.Auto), new(GridLength.Star)],
            ColumnSpacing = 12,
            RowSpacing = 6,
        };
        var simge = Olculen(new Border { WidthRequest = 34, HeightRequest = 34 });
        var ad = Olculen(new Label { Text = "Uzun bir kanal açıklaması", HeightRequest = 34 });
        var tutar = Olculen(new Label { Text = "₺999.999.999,99", WidthRequest = 200, HeightRequest = 34 });
        izgara.Add(simge);
        izgara.Add(ad, 1);
        izgara.Add(tutar, 2);
        UyumluIzgara.SetDarSatir(ad, 0);
        UyumluIzgara.SetDarSutun(ad, 1);
        UyumluIzgara.SetDarSatir(tutar, 1);
        UyumluIzgara.SetDarSutun(tutar, 1);
        Yerlestir(izgara, 320, 140);
        Assert.True(ad.Frame.Height >= 34);
        Assert.Equal(simge.Frame.Top, ad.Frame.Top);
        Assert.True(ad.Frame.Left >= simge.Frame.Right);
        Assert.True(tutar.Frame.Top >= ad.Frame.Bottom);
        Assert.True(tutar.Frame.Right <= 320);
        Yerlestir(izgara, 900, 140);
        Assert.Equal(ad.Frame.Top, tutar.Frame.Top);
        Assert.True(tutar.Frame.Left >= ad.Frame.Right);
    }

    [Fact]
    public void Dar_iki_bolme_gecisinde_form_kalir_genis_ekranda_iki_bolme_yanyana_gorunur()
    {
        GorunumOrtami.Kur();
        var form = new ScrollView { Content = new Entry { Text = "Kaydedilmemiş taslak" } };
        var liste = new CollectionView();
        var bolmeler = new UyumluBolmeler { Ilk = form, Ikinci = liste, GenisSutunlar = [new(new GridLength(360)), new(GridLength.Star)] };
        Yerlestir(bolmeler, 390, 600);
        Assert.True(form.IsVisible);
        Assert.False(liste.IsVisible);
        bolmeler.IkinciyiAc();
        Assert.False(form.IsVisible);
        Assert.True(liste.IsVisible);
        bolmeler.IlkiAc();
        Assert.Equal("Kaydedilmemiş taslak", ((Entry)form.Content).Text);
        Yerlestir(bolmeler, 1000, 600);
        Assert.True(form.Frame.Width >= 360);
        Assert.True(form.IsVisible);
        Assert.True(liste.IsVisible);
        Assert.True(liste.Frame.Left >= form.Frame.Right);
        Yerlestir(bolmeler, 390, 600);
        Assert.True(form.IsVisible);
        Assert.False(liste.IsVisible);
    }

    [Fact]
    public void Rolun_gizledigi_form_bos_sutun_birakmaz_sekmeyle_acilmaz()
    {
        GorunumOrtami.Kur();
        var form = new ScrollView { Content = new Entry { Text = "Taslak" } };
        var liste = new CollectionView();
        var bolmeler = new UyumluBolmeler
        {
            Ilk = form,
            Ikinci = liste,
            IlkKullanilabilir = false,
            GenisSutunlar = [new(new GridLength(360)), new(GridLength.Star)],
        };
        Yerlestir(bolmeler, 1000, 600);
        Assert.False(form.IsVisible);
        Assert.Equal(0, liste.Frame.Left);
        Assert.Equal(1000, liste.Frame.Width);
        Yerlestir(bolmeler, 390, 600);
        bolmeler.IlkiAc();
        Assert.False(form.IsVisible);
        Assert.True(liste.IsVisible);
        Assert.DoesNotContain(bolmeler.GetVisualTreeDescendants().OfType<Grid>(), g => g.IsVisible && g.Children.OfType<Button>().Count() == 2);
    }

    [Fact]
    public void Uzun_dugme_satiri_dar_alanda_sarilir_genis_alanda_yanyana_kalir()
    {
        GorunumOrtami.Kur();
        var ilk = Olculen(new Button { Text = "Önümüzdeki 7 gün", WidthRequest = 180, HeightRequest = 44 });
        var ikinci = Olculen(new Button { Text = "Önümüzdeki 30 gün", WidthRequest = 180, HeightRequest = 44 });
        var satir = new UyumluSatir { Spacing = 10, Children = { ilk, ikinci } };
        Yerlestir(satir, 760, 120);
        Assert.Equal(180, ilk.Frame.Width);
        Assert.Equal(ilk.Frame.Top, ikinci.Frame.Top);
        Assert.True(ikinci.Frame.Left >= ilk.Frame.Right);
        Yerlestir(satir, 320, 120);
        Assert.True(ikinci.Frame.Top >= ilk.Frame.Bottom);
        Assert.All(satir.Children, alan => Assert.True(alan.Frame.Right <= 320));
        Assert.True(ikinci.Frame.Height >= 44);
        Yerlestir(satir, 760, 120);
        Assert.Equal(ilk.Frame.Top, ikinci.Frame.Top);
    }

    private sealed class OlcuIsleyici : IViewHandler
    {
        public Size GetDesiredSize(double widthConstraint, double heightConstraint)
        {
            var view = (View)VirtualView!;
            return new Size(Math.Min(view.WidthRequest >= 0 ? view.WidthRequest : 80, widthConstraint),
                Math.Min(view.HeightRequest >= 0 ? view.HeightRequest : 44, heightConstraint));
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
