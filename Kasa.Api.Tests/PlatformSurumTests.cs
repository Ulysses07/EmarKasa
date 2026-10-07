using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kasa.Api.Tests;

public class PlatformSurumTests
{
    [Fact]
    public async Task Varsayilan_sozlesme_eski_alanlari_korur_yayinlanmis_surumu_uydurmaz()
    {
        await using var factory = new KasaWebFactory();
        using var client = factory.CreateClient();
        var dto = await client.GetFromJsonAsync<JsonElement>("/api/surum", TestContext.Current.CancellationToken);
        Assert.Equal(["surum", "minimumIstemci", "indirmeAdresi", "notlar"], dto.EnumerateObject().Take(4).Select(p => p.Name));
        Assert.Equal("2.4.1", dto.GetProperty("minimumIstemci").GetString());
        foreach (var platform in new[] { "windows", "ios" })
        {
            var yayin = dto.GetProperty(platform);
            Assert.Equal(JsonValueKind.Null, yayin.GetProperty("sonSurum").ValueKind);
            Assert.Equal("2.4.1", yayin.GetProperty("minimumIstemci").GetString());
        }
        Assert.Null(dto.GetProperty("indirmeAdresi").GetString());
        Assert.Equal(IstemciYayinAyarlari.WindowsIndirmeAdresi, dto.GetProperty("windows").GetProperty("indirmeAdresi").GetString());
        Assert.Equal(IstemciYayinAyarlari.IosIndirmeAdresi, dto.GetProperty("ios").GetProperty("indirmeAdresi").GetString());
    }

    [Fact]
    public async Task Son_yayin_yalniz_acik_platform_yapilandirmasindan_gelir()
    {
        await using var factory = new KasaWebFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kasa:IstemciYayinlari:Windows:SonSurum"] = "2.5.0",
            ["Kasa:IstemciYayinlari:Ios:SonSurum"] = "gecersiz",
            ["Kasa:IstemciYayinlari:Ios:MinimumIstemci"] = "2.3.0",
        })));
        using var client = configured.CreateClient();
        var dto = await client.GetFromJsonAsync<JsonElement>("/api/surum", TestContext.Current.CancellationToken);
        Assert.Equal("2.5.0", dto.GetProperty("windows").GetProperty("sonSurum").GetString());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("ios").GetProperty("sonSurum").ValueKind);
        Assert.Equal("2.4.1", dto.GetProperty("ios").GetProperty("minimumIstemci").GetString());
        Assert.Equal(SunucuSurumu.Deger, dto.GetProperty("surum").GetString());
    }

    [Theory]
    [InlineData("Ios", "ios", HttpStatusCode.Conflict)]
    [InlineData("Ios", "windows", HttpStatusCode.OK)]
    [InlineData("Ios", null, HttpStatusCode.OK)]
    [InlineData("Ios", "bilinmeyen", HttpStatusCode.OK)]
    [InlineData("Windows", "ios", HttpStatusCode.OK)]
    [InlineData("Windows", "windows", HttpStatusCode.Conflict)]
    [InlineData("Windows", null, HttpStatusCode.Conflict)]
    [InlineData("Windows", "bilinmeyen", HttpStatusCode.Conflict)]
    public async Task Bearer_yazma_kapisi_platform_minimumunu_secer_eski_istemci_Windows_kalir(string minimumPlatformu, string? platform, HttpStatusCode beklenen)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = KasaWebFactory.Sabit(new DateOnly(2026, 9, 25));
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kasa:IstemciYayinlari:" + minimumPlatformu + ":MinimumIstemci"] = "2.5.0",
        })));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "kasa123" }, ct);
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Kasa-Istemci-Surumu", "2.4.1");
        if (platform is not null)
            client.DefaultRequestHeaders.Add(IstemciYayinAyarlari.PlatformBasligi, platform);
        using var response = await client.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2026, 9, 1), kasaAcilisDevri = 123m }, ct);
        Assert.Equal(beklenen, response.StatusCode);
    }
}
