using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Kodla yazılmış takip sayfalarında son veri (tasarım 2026-10-02 §3): hiç yükleme yokken "Henüz yüklenmedi.", yükleme
/// hata verse de son veri görünür ve soluk, son güncelleme satırı "güncel olmayabilir" der.</summary>
public class TakipSayfasiSonVeriTests
{
    private sealed class DenemeSayfasi(AyarlarViewModel vm) : TakipSayfasi<AyarlarViewModel>(vm, "Deneme", "Açıklama", () => Task.CompletedTask)
    {
        public VerticalStackLayout GovdeAlani => Govde;
    }

    [Fact]
    public void Govde_son_veri_varken_gorunur_eski_veride_soluk_zaman_satiri_durumu_soyler()
    {
        GorunumOrtami.Kur();
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api, new AuthViewModel(api));
        var sayfa = new DenemeSayfasi(vm);
        var zaman = sayfa.GetVisualTreeDescendants().OfType<Label>().Single(l => l.Text?.Contains("yüklenmedi") == true || l.Text?.StartsWith("Son güncelleme") == true);

        Assert.Equal("Henüz yüklenmedi.", zaman.Text);
        Assert.False(sayfa.GovdeAlani.IsVisible);

        vm.SonGuncelleme = new DateTimeOffset(2026, 10, 2, 14, 5, 0, TimeSpan.FromHours(3));
        vm.VeriEski = true;
        Assert.True(sayfa.GovdeAlani.IsVisible);
        Assert.Equal(TakipUi.EskiVeriOpakligi, sayfa.GovdeAlani.Opacity);
        Assert.Equal("Son güncelleme: 02.10.2026 14:05 · güncel olmayabilir", zaman.Text);

        vm.VeriEski = false;
        Assert.Equal(1, sayfa.GovdeAlani.Opacity);
        Assert.Equal("Son güncelleme: 02.10.2026 14:05", zaman.Text);
    }
}
