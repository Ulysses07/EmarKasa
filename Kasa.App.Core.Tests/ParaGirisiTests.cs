using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.App.Controls;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace Kasa.App.Core.Tests;

/// <summary>ParaGirisi gerçek MAUI TwoWay bağlamasıyla: tuş tuş yazılan metin asla yeniden biçimlenmez,
/// '25.000' 25'e dönüşemez; VM'nin kendi değerleri biçimlenerek yazılır.</summary>
public partial class ParaGirisiTests
{
    // Uygulama dışında bağlama bir dispatcher ister; çağrıyı hemen çalıştıran sahte yeterli.
    private sealed class AnlikDispatcher : IDispatcher, IDispatcherProvider
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
        public IDispatcher? GetForCurrentThread() => this;
    }
    static ParaGirisiTests() => DispatcherProvider.SetCurrent(new AnlikDispatcher());

    public partial class Vm : ObservableObject
    {
        [ObservableProperty] private decimal _tutar;
        public int Degisim;
        partial void OnTutarChanged(decimal value) => Degisim++;
    }

    private static (ParaGirisi Giris, Vm Vm) Kur(decimal ilk = 0m)
    {
        var vm = new Vm { Tutar = ilk };
        var g = new ParaGirisi { BindingContext = vm };
        g.SetBinding(ParaGirisi.TutarProperty, nameof(Vm.Tutar));
        vm.Degisim = 0;
        return (g, vm);
    }

    private static void TusTusYaz(ParaGirisi g, string metin)
    {
        for (var i = 1; i <= metin.Length; i++)
        {
            g.Text = metin[..i];
            Assert.Equal(metin[..i], g.Text);   // kontrol ara metni biçimlemez
        }
    }

    [Theory]
    [InlineData("25.000")] [InlineData("1.500")] [InlineData("12.500")] [InlineData("1.250.000")] [InlineData("1,234.56")] [InlineData("1.234,56")]
    public void Gruplanmis_yazim_gecersiz_kalir_metin_bozulmaz(string metin)
    {
        var (g, vm) = Kur();
        TusTusYaz(g, metin);
        Assert.Equal(ParaAyristirici.Gecersiz, vm.Tutar);
        Assert.Equal(metin, g.Text);
        Assert.Equal(ParaAyristirici.BicimHatasi, ToolTipProperties.GetText(g));
    }

    [Theory]
    [InlineData("25000", "25000")] [InlineData("25000,5", "25000.5")] [InlineData("1500.50", "1500.50")] [InlineData("-12,30", "-12.30")]
    public void Gecerli_yazim_vm_ye_gider_metin_aynen_kalir(string metin, string beklenen)
    {
        var (g, vm) = Kur();
        TusTusYaz(g, metin);
        Assert.Equal(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture), vm.Tutar);
        Assert.Equal(metin, g.Text);
        Assert.Null(ToolTipProperties.GetText(g));
    }

    [Fact]
    public void Vm_degeri_bicimlenir_esit_deger_metne_dokunmaz()
    {
        var (g, vm) = Kur(5000m);
        Assert.Equal("5000", g.Text);
        TusTusYaz(g, "1500.50");
        vm.Tutar = 1500.5m;
        Assert.Equal("1500.50", g.Text);
        vm.Tutar = 1234.5m;
        Assert.Equal("1234,5", g.Text);
        vm.Tutar = 0m;
        Assert.Equal("", g.Text);
    }

    [Fact]
    public void Gecersiz_metin_vm_sifirlaninca_temizlenir_gorunum_duzelir()
    {
        var (g, vm) = Kur();
        var normal = g.TextColor;
        TusTusYaz(g, "25.000");
        Assert.NotEqual(normal, g.TextColor);
        vm.Tutar = 0m;
        Assert.Equal("", g.Text);
        Assert.Equal(normal, g.TextColor);
        Assert.Null(ToolTipProperties.GetText(g));
    }

    [Fact]
    public void Bagli_tutar_gecersizse_yeniden_uretilen_alan_hata_durumunda_acilir_vm_ezilmez()
    {
        // BindableLayout satırı yeniden üretildiğinde VM'de ParaAyristirici.Gecersiz durur; yazılan metin kaybolmuştur.
        var vm = new Vm { Tutar = ParaAyristirici.Gecersiz };
        var g = new ParaGirisi { Placeholder = "0,00 ₺" };
        var (yazi, yerTutucu) = (g.TextColor, g.PlaceholderColor);
        g.BindingContext = vm; g.SetBinding(ParaGirisi.TutarProperty, nameof(Vm.Tutar)); vm.Degisim = 0;

        Assert.NotEqual(yazi, g.TextColor); Assert.NotEqual(yerTutucu, g.PlaceholderColor);
        Assert.Equal(ParaAyristirici.GecersizGosterim, g.Placeholder);
        Assert.Equal(ParaGirisi.YenidenYazin, ToolTipProperties.GetText(g));
        Assert.Equal(ParaGirisi.YenidenYazin, SemanticProperties.GetHint(g));
        g.OdakKaybedildi();                                   // boş alandan çıkmak geçersiz tutarı 0'a çevirmez
        Assert.Equal(ParaAyristirici.Gecersiz, vm.Tutar); Assert.Equal(0, vm.Degisim);
        Assert.Equal(ParaAyristirici.GecersizGosterim, g.Placeholder);

        TusTusYaz(g, "250");
        Assert.Equal(250m, vm.Tutar);
        Assert.Equal(yazi, g.TextColor); Assert.Equal(yerTutucu, g.PlaceholderColor);
        Assert.Equal("0,00 ₺", g.Placeholder); Assert.Null(ToolTipProperties.GetText(g));
    }

    [Fact]
    public void Vm_gecersiz_tutari_temizleyince_alan_normale_doner()
    {
        var (g, vm) = Kur(ParaAyristirici.Gecersiz);
        Assert.Equal(ParaAyristirici.GecersizGosterim, g.Placeholder);
        vm.Tutar = 0m;
        Assert.Null(g.Placeholder); Assert.Null(ToolTipProperties.GetText(g)); Assert.Equal(new ParaGirisi().TextColor, g.TextColor);
    }

    [Fact]
    public void Kontrolun_kendi_yazimi_geri_gonderimle_donguye_girmez()
    {
        var (g, vm) = Kur();
        TusTusYaz(g, "12,5");
        Assert.Equal(4, vm.Degisim);   // 1 → 12 → Gecersiz ('12,') → 12,5
        Assert.Equal("12,5", g.Text);
    }
}
