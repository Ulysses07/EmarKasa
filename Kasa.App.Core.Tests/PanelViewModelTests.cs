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
}
