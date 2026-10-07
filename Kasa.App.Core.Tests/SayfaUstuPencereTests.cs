using Kasa.App.Controls;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

public class SayfaUstuPencereTests
{
    private sealed class Sayfa : ContentPage
    {
        public int Acilis, Kapanis;
        protected override void OnAppearing() { Acilis++; base.OnAppearing(); }
        protected override void OnDisappearing() { Kapanis++; base.OnDisappearing(); }
    }
    [Fact]
    public void Katman_gezinmez_formun_baglamini_ve_metin_taslagini_korur()
    {
        GorunumOrtami.Kur();
        var form = new object();
        var girdi = new Entry { Text = "kaydedilmemiş taslak" };
        var sayfa = new Sayfa { BindingContext = form, Content = girdi };
        _ = new Window(sayfa) { Parent = Application.Current };
        ((IPageController)sayfa).SendAppearing();
        var panel = new ContentView { BindingContext = new object(), Content = new Label { Text = "Güncellemeler" } };
        using var pencere = new SayfaUstuPencere(sayfa, panel);
        Assert.NotSame(girdi, sayfa.Content);
        Assert.Contains(panel, sayfa.GetVisualTreeDescendants());
        Assert.Equal("kaydedilmemiş taslak", girdi.Text);
        Assert.Same(form, girdi.BindingContext);
        Assert.Equal(1, sayfa.Acilis);
        Assert.Equal(0, sayfa.Kapanis);
    }
    [Fact]
    public void Kapatma_ozgun_icerigi_korur_ikinci_kapatma_yeniden_cagirmez()
    {
        GorunumOrtami.Kur();
        var girdi = new Entry { Text = "taslak" };
        var sayfa = new ContentPage { Content = girdi };
        var pencere = new SayfaUstuPencere(sayfa, new ContentView());
        var kapandi = 0;
        pencere.Kapandi += (_, _) => kapandi++;
        pencere.Dispose();
        pencere.Dispose();
        Assert.Same(girdi, sayfa.Content);
        Assert.Equal("taslak", girdi.Text);
        Assert.Equal(1, kapandi);
    }
    [Fact]
    public void Gercek_sayfa_gecinisinde_katman_kapanir_ve_islem_iptali_bildirilir()
    {
        GorunumOrtami.Kur();
        var icerik = new Entry { Text = "taslak" };
        var sayfa = new Sayfa { Content = icerik };
        _ = new Window(sayfa) { Parent = Application.Current };
        ((IPageController)sayfa).SendAppearing();
        using var pencere = new SayfaUstuPencere(sayfa, new ContentView());
        var kapandi = 0;
        pencere.Kapandi += (_, _) => kapandi++;
        ((IPageController)sayfa).SendDisappearing();
        Assert.Same(icerik, sayfa.Content);
        Assert.Equal(1, kapandi);
        Assert.Equal(1, sayfa.Kapanis);
    }
    private sealed class ParaModel : BindableObject
    {
        public static readonly BindableProperty TutarProperty = BindableProperty.Create(nameof(Tutar), typeof(decimal), typeof(ParaModel), 0m);
        public decimal Tutar { get => (decimal)GetValue(TutarProperty); set => SetValue(TutarProperty, value); }
    }
    [Fact]
    public void Gecersiz_para_metni_acma_ve_kapatmada_aynen_kalir_modeli_ezilmez()
    {
        GorunumOrtami.Kur();
        var model = new ParaModel { Tutar = 12.5m };
        var girdi = new ParaGirisi();
        girdi.SetBinding(ParaGirisi.TutarProperty, nameof(model.Tutar));
        var sayfa = new ContentPage { BindingContext = model, Content = girdi };
        girdi.Text = "12,345";
        var tutar = model.Tutar;
        Assert.False(ParaAyristirici.GecerliMi(tutar));
        using var pencere = new SayfaUstuPencere(sayfa, new ContentView());
        Assert.Equal("12,345", girdi.Text);
        Assert.Equal(tutar, model.Tutar);
        pencere.Dispose();
        Assert.Equal("12,345", girdi.Text);
        Assert.Equal(tutar, model.Tutar);
    }
}
