using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class KrediKartiEditorTests
{
    [Fact]
    public async Task Yeni_kart_olustur_cagirir()
    {
        var api = new SahteApi();
        var vm = new KrediKartlariViewModel(api)
        {
            DuzenAd = "Bonus",
            DuzenKesim = new DateTime(2026, 7, 5),
            DuzenSonOdeme = new DateTime(2026, 7, 25),
            DuzenLimit = 100000m,
            DuzenBorc = 30000m,
        };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonKartOlustur);
        Assert.Equal("Bonus", api.SonKartOlustur!.Ad);
        Assert.Equal(new DateOnly(2026, 7, 5), api.SonKartOlustur!.KesimTarihi);
        Assert.Equal(100000m, api.SonKartOlustur!.Limit);
    }

    [Fact]
    public async Task Mevcut_kart_guncelle_cagirir()
    {
        var api = new SahteApi();
        var vm = new KrediKartlariViewModel(api);
        vm.Duzenle(new KrediKartiGorunum(new KrediKartiDto(3, "World", new DateOnly(2026,7,1), new DateOnly(2026,7,20), 50000m, 10000m)));
        vm.DuzenBorc = 12000m;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonKartGuncelle);
        Assert.Equal(3, api.SonKartGuncelle!.Value.Id);
        Assert.Equal(12000m, api.SonKartGuncelle!.Value.G.Borc);
    }

    [Fact]
    public async Task Sil_kart_silme_cagirir()
    {
        var api = new SahteApi();
        var vm = new KrediKartlariViewModel(api);
        var kart = new KrediKartiGorunum(new KrediKartiDto(9, "Maximum", new DateOnly(2026,7,1), new DateOnly(2026,7,20), 20000m, 0m));

        await vm.SilCommand.ExecuteAsync(kart);

        Assert.Equal(9, api.SonKartSil);
    }
}
