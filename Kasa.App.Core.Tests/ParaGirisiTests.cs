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
    public void Kontrolun_kendi_yazimi_geri_gonderimle_donguye_girmez()
    {
        var (g, vm) = Kur();
        TusTusYaz(g, "12,5");
        Assert.Equal(4, vm.Degisim);   // 1 → 12 → Gecersiz ('12,') → 12,5
        Assert.Equal("12,5", g.Text);
    }
}
