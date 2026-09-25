using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class BildirimTests
{
    private static AuthViewModel Auth() => new(new SahteApi()) { AktifRol = Rol.Editor };
    [Fact] public async Task Bildirim_ayari_saat_ve_guncel_surumu_korur()
    {
        var api = new Fake(); var vm = new BildirimViewModel(api, Auth()); await vm.YukleAsync(); vm.Saat = 25;
        await vm.KaydetCommand.ExecuteAsync(null); Assert.Null(api.Yazilan);
        vm.Saat = 10; vm.Dakika = 30; await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(new BildirimAyarYaz(true, 10, 30, 3), api.Yazilan); Assert.True(vm.CihazBildirimiEtkin);
    }
    [Fact] public async Task Oturum_degistiginde_geciken_bildirim_ve_cihazlar_gosterilmez()
    {
        var bekleyen = new TaskCompletionSource<IReadOnlyList<BildirimDto>>(); var api = new Fake { Bekleyen = bekleyen.Task }; var auth = Auth(); var vm = new BildirimViewModel(api, auth);
        var yukle = vm.YukleAsync(); auth.OturumSurumu++; bekleyen.SetResult(new[] { Fake.Bildirim() }); await yukle;
        Assert.Empty(vm.Bildirimler); Assert.Empty(vm.Cihazlar); Assert.False(vm.VeriHazir);
    }
    [Fact] public async Task Okundu_isareti_ve_cihaz_kapatma_basarili_cevaptan_sonra_yansir()
    {
        var api = new Fake(); var vm = new BildirimViewModel(api, Auth()); await vm.YukleAsync();
        await vm.OkunduAsync(vm.Bildirimler[0]); Assert.True(vm.Bildirimler[0].Veri.Okundu); Assert.Equal(3, api.Okundu);
        await vm.CihaziKaldirAsync(vm.Cihazlar[0]); Assert.False(vm.Cihazlar[0].Veri.Etkin); Assert.Equal(4, api.Kaldirilan);
    }
    [Theory]
    [InlineData(null, "https://kasa.emarglobal.com/#notifications")]
    [InlineData("http://localhost:5137/", "http://localhost:5137/#notifications")]
    public void Tarayici_kurulumu_yalniz_yapilandirilmis_api_kokenini_acar(string? adres, string beklenen)
        => Assert.Equal(beklenen, BildirimViewModel.KurulumAdresi(adres).AbsoluteUri);
    private sealed class Fake : IBildirimApi
    {
        public Task<IReadOnlyList<BildirimDto>>? Bekleyen; public BildirimAyarYaz? Yazilan; public int Okundu, Kaldirilan;
        public static BildirimDto Bildirim() => new(3, "Kart", "Son ödeme", new(2026, 9, 23), false, "/#cards/1", "SonOdeme", 1);
        public Task<IReadOnlyList<BildirimDto>> BildirimlerAsync() => Bekleyen ?? Task.FromResult<IReadOnlyList<BildirimDto>>(new[] { Bildirim() });
        public Task<BildirimAyarDto> BildirimAyarlariAsync() => Task.FromResult(new BildirimAyarDto(true, 9, 0, "Europe/Istanbul", 3));
        public Task<BildirimAyarDto> BildirimAyarKaydetAsync(BildirimAyarYaz g) { Yazilan = g; return Task.FromResult(new BildirimAyarDto(g.Etkin, g.Saat, g.Dakika, "Europe/Istanbul", 4)); }
        public Task BildirimOkunduAsync(int id) { Okundu = id; return Task.CompletedTask; }
        public Task<PushAnahtarDto> BildirimAnahtariAsync() => Task.FromResult(new PushAnahtarDto(true, "public"));
        public Task<IReadOnlyList<BildirimCihaziDto>> BildirimCihazlariAsync() => Task.FromResult<IReadOnlyList<BildirimCihaziDto>>(new[] { new BildirimCihaziDto(4, "Windows", DateTimeOffset.UtcNow, null, true) });
        public Task BildirimCihaziKaldirAsync(int id) { Kaldirilan = id; return Task.CompletedTask; }
    }
}
