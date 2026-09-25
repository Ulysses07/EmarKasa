using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class AuthViewModelTests
{
    [Fact]
    public async Task Normal_giris_cikis_ve_oturum_sonu_kurtarma_sirlarini_temizler()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt") };
        var vm = new AuthViewModel(api) { KurtarmaAcik = true, KurtarmaKodu = "kod", KurtarmaYeniSifre = "gizli-yeni-sifre" };
        await vm.GirisCommand.ExecuteAsync(null);
        Assert.Empty(vm.KurtarmaKodu); Assert.Empty(vm.KurtarmaYeniSifre); Assert.False(vm.KurtarmaAcik);
        vm.KurtarmaKodu = "kod"; vm.KurtarmaYeniSifre = "gizli"; vm.KurtarmaAcik = true;
        await vm.CikisAsync();
        Assert.Empty(vm.KurtarmaKodu); Assert.Empty(vm.KurtarmaYeniSifre); Assert.False(vm.KurtarmaAcik);
        vm.KurtarmaKodu = "kod"; vm.KurtarmaYeniSifre = "gizli"; vm.KurtarmaAcik = true;
        api.OturumuSonlandir();
        Assert.Empty(vm.KurtarmaKodu); Assert.Empty(vm.KurtarmaYeniSifre); Assert.False(vm.KurtarmaAcik);
    }
    [Fact]
    public async Task Basarili_login_rolu_ayarlar_ve_hata_temizler()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt") };
        var vm = new AuthViewModel(api);
        vm.Sifre = "sifre";

        await vm.GirisCommand.ExecuteAsync(null);

        Assert.Equal(Rol.Editor, vm.AktifRol);
        Assert.True(vm.GirisYapildi);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Basarisiz_login_hata_gosterir_giris_yapilmaz()
    {
        var api = new SahteApi { LoginHatasi = new KasaApiException(HttpStatusCode.Unauthorized) };
        var vm = new AuthViewModel(api);
        vm.Sifre = "yanlis";

        await vm.GirisCommand.ExecuteAsync(null);

        Assert.False(vm.GirisYapildi);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Acilis_gecerli_token_rolu_dondurur()
    {
        var api = new SahteApi { MeRol = "viewer" };
        var vm = new AuthViewModel(api);

        var girildi = await vm.AcilistaDogrulaAsync();

        Assert.True(girildi);
        Assert.Equal(Rol.Izleyici, vm.AktifRol);
    }

    [Fact]
    public async Task Acilis_401_giris_yapilmamis_dondurur()
    {
        var api = new SahteApi { MeHatasi = new KasaApiException(HttpStatusCode.Unauthorized) };
        var vm = new AuthViewModel(api);

        var girildi = await vm.AcilistaDogrulaAsync();

        Assert.False(girildi);
    }

    [Fact]
    public async Task Cikis_api_cikisini_cagirir_ve_giris_durumu_sifirlanir()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt") };
        var vm = new AuthViewModel(api);
        vm.Sifre = "s";
        await vm.GirisCommand.ExecuteAsync(null);

        await vm.CikisAsync();

        Assert.True(api.CikisCagrildi);
        Assert.False(vm.GirisYapildi);
    }
}
