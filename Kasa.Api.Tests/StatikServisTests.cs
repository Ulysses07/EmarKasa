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
    public async Task Telefon_arayuzu_m_altinda_ayni_guvenlik_basliklariyla_sunulur()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/m/");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Equal("text/html", yanit.Content.Headers.ContentType?.MediaType);
        Assert.Contains("script-src 'self'", yanit.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("/m/app.js", await yanit.Content.ReadAsStringAsync());
    }

    // Web arayüzü adreslerinde sürüm sorgusu (?v=) yoktur (Kasa.Api.Ui.Tests yasaklar.test.mjs): tazelik bu başlıklara dayanır.
    // no-cache saklanan kopyanın her kullanımdan önce sunucuya doğrulatılmasını ister; ETag ile koşullu istek değişmeyen dosyada
    // 304 alır, değişen dosyanın yeni içeriği hemen gelir.
    [Theory]
    [InlineData("/")]
    [InlineData("/app.js")]
    [InlineData("/ui-shell.js")]
    [InlineData("/denetim-ui.js")]
    [InlineData("/styles.css")]
    [InlineData("/m/")]
    [InlineData("/m/app.js")]
    [InlineData("/m/app.css")]
    [InlineData("/m/icons.svg")]
    public async Task Web_arayuzu_dosyalari_her_kullanimda_dogrulanir_no_cache_ve_ETag(string yol)
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.True(yanit.Headers.CacheControl?.NoCache, $"{yol}: Cache-Control no-cache değil ({yanit.Headers.CacheControl}).");
        var etiket = yanit.Headers.ETag;
        Assert.NotNull(etiket);

        using var kosullu = new HttpRequestMessage(HttpMethod.Get, yol);
        kosullu.Headers.IfNoneMatch.Add(etiket);
        var ikinci = await client.SendAsync(kosullu);
        Assert.Equal(HttpStatusCode.NotModified, ikinci.StatusCode);
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
