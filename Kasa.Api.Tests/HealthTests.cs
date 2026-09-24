using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kasa.Api.Tests;

public class HealthTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public HealthTests(KasaWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_ok_doner_ve_db_yoklar()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var j = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", j.GetProperty("durum").GetString());
        Assert.True(j.TryGetProperty("surum", out _));
        Assert.True(j.TryGetProperty("sonYedekYasSaat", out _));
    }

    [Fact]
    public async Task Guvenlik_basliklari_eklenir_server_basligi_yok()
    {
        var client = _factory.CreateClient();
        foreach (var yol in new[] { "/health", "/api/kanallar" })
        {
            var r = await client.GetAsync(yol);
            Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
            Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
            Assert.Equal("default-src 'none'; frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
            Assert.False(r.Headers.Contains("Server"));
        }
    }
}
