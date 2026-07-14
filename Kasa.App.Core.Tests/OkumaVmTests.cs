using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class OkumaVmTests
{
    [Fact]
    public async Task Haftalik_donemleri_yukler()
    {
        var api = new SahteApi
        {
            HaftalikListe = new List<HaftalikOzetDto>
            {
                new(new DonemDto(new DateOnly(2026,3,2), new DateOnly(2026,3,8), 2026, 3),
                    new List<KanalHaftalikDto>(), 0, 0, 100m, 100m),
            },
        };
        var vm = new HaftalikViewModel(api);
        await vm.YukleAsync();
        Assert.Single(vm.Donemler);
        Assert.Equal(100m, vm.Donemler[0].KasaSonucu);
    }

    [Fact]
    public async Task Aylik_secilen_yil_ayi_ister()
    {
        var api = new SahteApi { AylikRapor = new AylikRaporDto(2026, 4, new List<KanalAylikDto>()) };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 4 };
        await vm.YukleAsync();
        Assert.Equal(2026, api.SonAylikYil);
        Assert.Equal(4, api.SonAylikAy);
        Assert.NotNull(vm.Rapor);
    }

    [Fact]
    public async Task Cariler_listeyi_yukler()
    {
        var api = new SahteApi { CarilerListe = new List<CariDto> { new(1, "Ahmet", true) } };
        var vm = new CarilerViewModel(api);
        await vm.YukleAsync();
        Assert.Single(vm.Cariler);
        Assert.Equal("Ahmet", vm.Cariler[0].Ad);
    }

    [Fact]
    public async Task Islemler_listeyi_yukler()
    {
        var api = new SahteApi
        {
            IslemlerListe = new List<IslemDto>
            {
                new(1, new DateOnly(2026,3,5), "K.K", 10000m, "MEZAT", GiderTipi.KrediKarti, null),
            },
        };
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();
        Assert.Single(vm.Islemler);
        Assert.Equal(GiderTipi.KrediKarti, vm.Islemler[0].Tip);
    }
}
