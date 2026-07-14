using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class CariEditorTests
{
    [Fact]
    public async Task Yeni_cari_kaydi_olustur_cagirir()
    {
        var api = new SahteApi();
        var vm = new CarilerViewModel(api) { DuzenAd = "Mehmet", DuzenAktif = true };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonCariOlustur);
        Assert.Equal("Mehmet", api.SonCariOlustur!.Ad);
        Assert.Null(api.SonCariGuncelle);
        Assert.Equal("", vm.DuzenAd);
    }

    [Fact]
    public async Task Mevcut_cari_guncelle_cagirir()
    {
        var api = new SahteApi();
        var vm = new CarilerViewModel(api);
        vm.Duzenle(new CariDto(7, "Ali", true));
        vm.DuzenAd = "Ali Veli";

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonCariGuncelle);
        Assert.Equal(7, api.SonCariGuncelle!.Value.Id);
        Assert.Equal("Ali Veli", api.SonCariGuncelle!.Value.G.Ad);
        Assert.Null(api.SonCariOlustur);
    }
}
