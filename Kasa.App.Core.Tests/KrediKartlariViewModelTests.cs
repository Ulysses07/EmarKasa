using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class KrediKartlariViewModelTests
{
    [Fact]
    public async Task Yukle_karti_kalan_limitle_doldurur()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new List<KrediKartiDto>
            {
                new(1, "Bonus", new DateOnly(2026,7,5), new DateOnly(2026,7,25), 100000m, 30000m),
            },
        };
        var vm = new KrediKartlariViewModel(api);

        await vm.YukleAsync();

        Assert.Single(vm.Kartlar);
        Assert.Equal(70000m, vm.Kartlar[0].KalanLimit);   // limit - borç
        Assert.Equal("Bonus", vm.Kartlar[0].Ad);
    }

    [Fact]
    public async Task Yukle_bos_liste_bos_koleksiyon()
    {
        var vm = new KrediKartlariViewModel(new SahteApi());
        await vm.YukleAsync();
        Assert.Empty(vm.Kartlar);
    }
}
