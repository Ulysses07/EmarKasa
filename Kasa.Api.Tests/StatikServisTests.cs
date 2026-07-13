using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kasa.Api.Tests;

public class StatikServisTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public StatikServisTests(KasaWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Kok_istegi_index_html_doner()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.Contains("KASA_SPA_PLACEHOLDER", govde);
    }

    [Fact]
    public async Task Istemci_rotasi_index_html_e_fallback_yapar()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/haftalik");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.Contains("KASA_SPA_PLACEHOLDER", govde);
    }

    [Fact]
    public async Task Api_yolu_fallback_yerine_401_doner()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/api/kanallar");
        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    [Fact]
    public async Task Development_te_login_cerezi_secure_degil()
    {
        // Editör env KasaWebFactory tarafından set edilir; başarılı login çerezi döner.
        var client = _factory.CreateClient();
        var giris = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "kasa123" });
        giris.EnsureSuccessStatusCode();
        var setCookie = Assert.Single(giris.Headers.GetValues("Set-Cookie"));
        Assert.Contains("kasa_auth=", setCookie);
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }
}
