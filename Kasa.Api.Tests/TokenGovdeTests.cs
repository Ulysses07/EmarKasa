using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class TokenGovdeTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public TokenGovdeTests(KasaWebFactory factory) => _factory = factory;

    private record GirisYanit(string Rol, string Token);

    [Fact]
    public async Task Login_govdede_token_doner()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "kasa123" });
        resp.EnsureSuccessStatusCode();

        var yanit = await resp.Content.ReadFromJsonAsync<GirisYanit>();
        Assert.NotNull(yanit);
        Assert.Equal("editor", yanit!.Rol);
        Assert.False(string.IsNullOrWhiteSpace(yanit.Token));
    }

    [Fact]
    public async Task Govdeden_alinan_token_bearer_olarak_calisir()
    {
        // 1) login ol, token'ı gövdeden al
        var loginClient = _factory.CreateClient();
        var resp = await loginClient.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "kasa123" });
        var yanit = await resp.Content.ReadFromJsonAsync<GirisYanit>();

        // 2) HİÇ login olmamış (cookie'siz) ayrı istemci: yalnız Bearer header ile
        var bearerClient = _factory.CreateClient();
        bearerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", yanit!.Token);

        var me = await bearerClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
}
