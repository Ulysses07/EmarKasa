using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kasa.Api.Tests;

public class StatikServisTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public StatikServisTests(KasaWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Kok_istegi_mobil_giris_sayfasini_sunar()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Equal("text/html", yanit.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Istemci_rotasi_404_doner_fallback_yok()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/haftalik");
        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
    }

    [Fact]
    public async Task Api_yolu_kimliksiz_401_doner()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/api/kanallar");
        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    [Fact]
    public async Task Health_200_doner()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }
}
