using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class HataYuzeyiTests
{
    [Fact]
    public async Task Panel_ag_hatasinda_hata_yazar_ve_spinner_iner()
    {
        var api = new SahteApi { YuklemeHatasi = new KasaApiException(HttpStatusCode.ServiceUnavailable, "kopuk") };
        var vm = new PanelViewModel(api);

        await vm.YukleAsync();

        Assert.False(vm.Mesgul);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Cariler_basarili_yuklemede_hata_null()
    {
        var api = new SahteApi { CarilerListe = new List<CariDto> { new(1, "Ahmet", true) } };
        var vm = new CarilerViewModel(api);

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Single(vm.Cariler);
    }
}
