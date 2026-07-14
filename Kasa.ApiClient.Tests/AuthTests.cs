using System.Net;

namespace Kasa.ApiClient.Tests;

public class AuthTests
{
    private static (KasaApiClient client, SahteHandler handler, BellekTokenStore store) Kur()
    {
        var handler = new SahteHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") };
        var store = new BellekTokenStore();
        return (new KasaApiClient(http, store), handler, store);
    }

    [Fact]
    public async Task Login_token_ve_rol_dondurur_ve_tokeni_saklar()
    {
        var (client, handler, store) = Kur();
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-123"}""");

        var yanit = await client.LoginAsync("admin", "sifre");

        Assert.Equal("editor", yanit.Rol);
        Assert.Equal("jwt-123", yanit.Token);
        Assert.Equal("jwt-123", await store.OkuAsync());
        Assert.Equal(HttpMethod.Post, handler.SonIstek!.Method);
        Assert.EndsWith("/api/auth/login", handler.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Sonraki_istek_bearer_basligi_ekler()
    {
        var (client, handler, store) = Kur();
        await store.YazAsync("jwt-xyz");
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"viewer"}""");

        await client.BenKimAsync();

        Assert.Equal("Bearer", handler.SonIstek!.Headers.Authorization!.Scheme);
        Assert.Equal("jwt-xyz", handler.SonIstek.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Yetkisiz_401_KasaApiException_firlatir()
    {
        var (client, handler, _) = Kur();
        handler.Kuyrukla(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<KasaApiException>(() => client.PanelAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.DurumKodu);
    }

    [Fact]
    public async Task Login_401_KasaApiException_firlatir()
    {
        var (client, handler, _) = Kur();
        handler.Kuyrukla(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<KasaApiException>(() => client.LoginAsync("x", "y"));
    }
}
