using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class RaporDurumuTests
{
    private static AylikRaporDto Ay(int ay) => new(2026, ay, new List<KanalAylikDto>());

    [Fact]
    public async Task Ay_degistirirken_hata_olursa_onceki_ayin_verisi_gosterilmez_ve_tekrar_denenir()
    {
        var api = new SahteApi { AylikRapor = Ay(8) };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();
        var son = vm.SonGuncelleme;
        api.YuklemeHatasi = new HttpRequestException();

        await vm.SonrakiAyCommand.ExecuteAsync(null);

        Assert.Equal(9, vm.Ay);
        Assert.Null(vm.Rapor);
        Assert.False(vm.VeriVar);
        Assert.False(vm.Mesgul);
        Assert.NotNull(vm.Hata);
        Assert.Equal(son, vm.SonGuncelleme);

        api.YuklemeHatasi = null;
        api.AylikRapor = Ay(9);
        await vm.YenileCommand.ExecuteAsync(null);
        Assert.True(vm.VeriVar);
        Assert.Equal(vm.Ay, vm.Rapor!.Ay);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Gec_donen_eski_ay_yeni_ayin_raporunu_ezmez()
    {
        var eski = new TaskCompletionSource<AylikRaporDto>();
        var yeni = new TaskCompletionSource<AylikRaporDto>();
        var api = new SahteApi { AylikGetir = (_, ay) => ay == 8 ? eski.Task : yeni.Task };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 8 };
        var ilk = vm.YukleAsync();
        var sonraki = vm.SonrakiAyCommand.ExecuteAsync(null);
        Assert.True(vm.Mesgul);
        Assert.False(vm.VeriVar);

        yeni.SetResult(Ay(9));
        await sonraki;
        eski.SetResult(Ay(8));
        await ilk;

        Assert.Equal(9, vm.Ay);
        Assert.Equal(9, vm.Rapor!.Ay);
        Assert.True(vm.VeriVar);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Eski_istegin_hatasi_yeni_basarili_raporu_gizlemez()
    {
        var eski = new TaskCompletionSource<AylikRaporDto>();
        var api = new SahteApi { AylikGetir = (_, ay) => ay == 8 ? eski.Task : Task.FromResult(Ay(9)) };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 8 };
        var ilk = vm.YukleAsync();
        await vm.SonrakiAyCommand.ExecuteAsync(null);
        eski.SetException(new HttpRequestException());
        await ilk;
        Assert.Null(vm.Hata);
        Assert.True(vm.VeriVar);
        Assert.Equal(9, vm.Rapor!.Ay);
    }

    [Fact]
    public async Task Panel_ilk_yukleme_hatasinda_sifir_bakiye_gosterilmez()
    {
        var vm = new PanelViewModel(new SahteApi { YuklemeHatasi = new HttpRequestException() });
        await vm.YukleAsync();
        Assert.False(vm.VeriVar);
        Assert.Null(vm.SonGuncelleme);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Panel_yenilenirken_ve_hatada_onceki_bakiye_gizlenir()
    {
        var api = new SahteApi { Panel = new PanelDto(123m, new List<KanalBakiyeDto>(), 0, 0) };
        var vm = new PanelViewModel(api);
        await vm.YukleAsync();
        Assert.True(vm.VeriVar);
        var bekleyen = new TaskCompletionSource<PanelDto>();
        api.PanelGetir = () => bekleyen.Task;
        var yenile = vm.YukleAsync();
        Assert.False(vm.VeriVar);
        Assert.True(vm.Mesgul);
        bekleyen.SetException(new HttpRequestException());
        await yenile;
        Assert.False(vm.VeriVar);
        Assert.False(vm.Mesgul);
        Assert.NotNull(vm.SonGuncelleme);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Sunucunun_dogrulama_ve_cakisma_mesaji_korunur(HttpStatusCode kod)
    {
        var vm = new HaftalikViewModel(new SahteApi { YuklemeHatasi = new KasaApiException(kod, "Kanal kullanımda.") });
        await vm.YukleAsync();
        Assert.Equal("Kanal kullanımda.", vm.Hata);
        Assert.False(vm.VeriVar);
    }

    [Fact]
    public async Task Merkezi_401_bildirimi_auth_durumunu_kapatir()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "token") };
        var vm = new AuthViewModel(api);
        await vm.GirisCommand.ExecuteAsync(null);
        var bildirildi = false;
        vm.OturumSonlandi += (_, _) => bildirildi = true;
        api.OturumuSonlandir();
        Assert.False(vm.GirisYapildi);
        Assert.True(bildirildi);
        Assert.Contains("Oturumunuz sona erdi", vm.Hata);
    }
}
