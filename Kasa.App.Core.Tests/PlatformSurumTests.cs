using System.Net;
using System.Net.Http.Json;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class PlatformSurumTests
{
    [Theory]
    [InlineData("windows", false, "2.5.0")]
    [InlineData("ios", true, null)]
    public async Task Guvenlik_ekrani_platform_minimumunu_ve_yayinini_secer(string platform, bool gerekli, string? yayin)
    {
        var dto = new SurumDto("9.0.0", "2.4.1", IstemciPlatformlari.WindowsYayinAdresi, "Windows eski not",
            new("2.5.0", "2.4.1", IstemciPlatformlari.WindowsYayinAdresi, "Windows not"),
            new(null, "2.5.0", IstemciPlatformlari.TestFlightAdresi, "TestFlight not"));
        var vm = Kur(dto, platform);
        await vm.YukleAsync();
        Assert.Null(vm.Hata);
        Assert.Equal(gerekli, vm.GuncellemeGerekli);
        Assert.Equal(yayin, vm.SonYayinSurumu);
        Assert.Equal(platform == "ios" ? IstemciPlatformlari.TestFlightAdresi : IstemciPlatformlari.WindowsYayinAdresi, vm.IndirmeAdresi);
        Assert.Equal(platform == "ios" ? "TestFlight'ı aç" : "Windows sürümlerini aç", vm.IndirmeMetni);
        Assert.Contains("sunucu 9.0.0", vm.SurumBilgisi);
        Assert.DoesNotContain("Yayımlanmış " + (platform == "ios" ? "iOS" : "Windows") + " sürümü: 9.0.0", vm.SurumBilgisi);
    }

    [Fact]
    public async Task Eski_sunucuda_iOS_Windows_indirme_adresini_kullanmaz_ve_uretilen_surumu_son_yayin_saymaz()
    {
        var vm = Kur(new("9.0.0", "2.4.1", "https://ornek.test/setup.exe", "Windows güncellemesi"), "ios");
        await vm.YukleAsync();
        Assert.Null(vm.Hata);
        Assert.False(vm.GuncellemeGerekli);
        Assert.Null(vm.SonYayinSurumu);
        Assert.Equal(IstemciPlatformlari.TestFlightAdresi, vm.IndirmeAdresi);
        Assert.Contains("yayımlanmış son sürüm bilgisi henüz sağlanmadı", vm.SurumBilgisi);
        Assert.DoesNotContain("Windows", vm.SurumBilgisi);
    }

    [Fact]
    public async Task Gecersiz_veya_kimlik_bilgili_indirme_adresi_acilmaz()
    {
        var vm = Kur(new("9.0.0", "2.4.1", null, null, new(null, "2.4.1", "https://kullanici:sifre@ornek.test/setup.exe")), "windows");
        await vm.YukleAsync();
        Assert.Null(vm.Hata);
        Assert.Null(vm.IndirmeAdresi);
    }

    private static GuvenlikViewModel Kur(SurumDto dto, string platform)
    {
        var client = new KasaApiClient(new HttpClient(new Yanit(dto)) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore(), istemciPlatformu: platform);
        return new(client, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor, GirisYapildi = true }, platform);
    }

    private sealed class Yanit(SurumDto dto) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object body = request.RequestUri!.AbsolutePath == "/api/surum" ? dto : new YedekDurumuDto(false, null, null, null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
