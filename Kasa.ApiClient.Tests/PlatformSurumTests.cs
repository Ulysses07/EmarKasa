using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class PlatformSurumTests
{
    [Theory]
    [InlineData(null, "windows")]
    [InlineData("ios", "ios")]
    [InlineData(" IOS ", "ios")]
    [InlineData("android", "windows")]
    public async Task Platform_basligi_tum_isteklerde_surum_basligiyla_birlikte_gider(string? platform, string beklenen)
    {
        var handler = new SahteHandler().Kuyrukla(HttpStatusCode.OK, "[]");
        var client = new KasaApiClient(new HttpClient(handler) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore(), istemciPlatformu: platform);
        await client.KanallarAsync();
        Assert.Equal(beklenen, Assert.Single(handler.SonIstek!.Headers.GetValues(IstemciPlatformlari.Baslik)));
        Assert.Equal(KasaApiClient.IstemciSurumu, Assert.Single(handler.SonIstek.Headers.GetValues(KasaApiClient.IstemciSurumuBasligi)));
    }

    [Fact]
    public void Eski_sunum_sozlesmesi_okunur_iOS_Windows_dosyasina_yonlenmez_sunucu_surumu_yayin_sayilmaz()
    {
        var dto = JsonSerializer.Deserialize<SurumDto>("""{"surum":"9.0.0","minimumIstemci":"2.4.1","indirmeAdresi":"https://ornek.test/windows.zip","notlar":"Windows gerekli"}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Null(dto.Windows);
        Assert.Null(dto.Ios);
        Assert.Equal("https://ornek.test/windows.zip", dto.IstemciYayini(null).IndirmeAdresi);
        Assert.Null(dto.IstemciYayini(null).SonSurum);
        var ios = dto.IstemciYayini("ios");
        Assert.Equal(IstemciPlatformlari.TestFlightAdresi, ios.IndirmeAdresi);
        Assert.Null(ios.SonSurum);
        Assert.DoesNotContain("Windows", ios.Notlar);
    }

    [Fact]
    public void Yeni_sunum_sozlesmesi_platformlarin_yayin_ve_minimumunu_ayri_tasir()
    {
        var dto = JsonSerializer.Deserialize<SurumDto>("""{"surum":"9.0.0","minimumIstemci":"2.4.1","indirmeAdresi":null,"notlar":null,"windows":{"sonSurum":"2.5.0","minimumIstemci":"2.4.1","indirmeAdresi":"https://github.com/Ulysses07/EmarKasa/releases","notlar":null},"ios":{"sonSurum":null,"minimumIstemci":"2.4.2","indirmeAdresi":"https://testflight.apple.com/","notlar":null}}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("2.5.0", dto.IstemciYayini("windows").SonSurum);
        Assert.Equal("2.4.1", dto.IstemciYayini("windows").MinimumIstemci);
        Assert.Null(dto.IstemciYayini("ios").SonSurum);
        Assert.Equal("2.4.2", dto.IstemciYayini("ios").MinimumIstemci);
    }
}
