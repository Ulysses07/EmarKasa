using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class PanelViewModelTests
{
    [Fact]
    public async Task Yukle_panel_alanlarini_doldurur()
    {
        var api = new SahteApi
        {
            Panel = new PanelDto(90000m, new List<KanalBakiyeDto> { new("MEZAT", 150.5m) }, 10m, -5m),
        };
        var vm = new PanelViewModel(api);

        await vm.YukleAsync();

        Assert.Equal(90000m, vm.GuncelKasa);
        Assert.Single(vm.Kanallar);
        Assert.Equal("MEZAT", vm.Kanallar[0].Kanal);
    }
    [Fact] public async Task Kart_borcu_kimlikle_eslenir_mevcut_kasa_ve_belirsiz_pay_degismez()
    {
        var api = new SahteApi { Panel = new(900, new[] { new KanalBakiyeDto("ESKİ AD", 300, 1), new KanalBakiyeDto("MEZAT", 600, 2) }, 0, 0) };
        var vm = new PanelViewModel(api); await vm.YukleAsync();
        vm.KartBorclariniYansit(new[] { new TakipKanalPayi(1, "YENİ AD", 100), new TakipKanalPayi(2, "MEZAT", 200), new TakipKanalPayi(null, "Dağılım bekliyor", 50) });
        Assert.Equal(100, vm.Kanallar[0].KartBorcu); Assert.Equal(200, vm.Kanallar[1].KartBorcu);
        Assert.Equal(900, vm.GuncelKasa); Assert.Equal(300, vm.Kanallar[0].Bakiye); Assert.Equal(600, vm.Kanallar[1].Bakiye);
        vm.KartBorclariniYansit(null); Assert.All(vm.Kanallar, k => Assert.Null(k.KartBorcu));
    }
}
