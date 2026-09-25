using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kasa.Api.Auth;
using Microsoft.Extensions.Configuration;

namespace Kasa.Api.Tests;

public class GuvenliOturumTests
{
    private record Giris(string Rol, string Token);

    [Fact]
    public async Task Izleyici_sifresi_degisince_eski_cookie_ve_bearer_gecersiz_olur()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "onceki-sifre" })).EnsureSuccessStatusCode();
        using var viewer = f.CreateClient();
        var giris = await viewer.PostAsJsonAsync("/api/auth/login", new { sifre = "onceki-sifre" });
        giris.EnsureSuccessStatusCode();
        var oturum = await giris.Content.ReadFromJsonAsync<Giris>();
        using var bearer = f.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oturum!.Token);
        Assert.Equal(HttpStatusCode.OK, (await bearer.GetAsync("/api/auth/me")).StatusCode);

        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "yeni-sifre" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.PostAsJsonAsync("/api/auth/login", new { sifre = "yeni-sifre" })).StatusCode);
    }

    [Fact]
    public async Task Acik_bearer_basligi_eski_cookie_ile_golgelenmez()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici" })).EnsureSuccessStatusCode();
        using var viewer = f.CreateClient();
        (await viewer.PostAsJsonAsync("/api/auth/login", new { sifre = "izleyici" })).EnsureSuccessStatusCode();
        var login = await editor.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "kasa123" });
        var oturum = await login.Content.ReadFromJsonAsync<Giris>();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oturum!.Token);
        var me = await viewer.GetFromJsonAsync<RolYanit>("/api/auth/me");
        Assert.Equal("editor", me!.Rol);
    }

    [Fact]
    public async Task Dogru_imzali_ama_eski_damgali_token_kabul_edilmez()
    {
        await using var f = new KasaWebFactory();
        using var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            JwtYardimci.Uret("editor", "test-jwt-anahtari-en-az-32-bayt-olmali!!", "eski-damga"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Null_sifre_sunucu_hatasi_uretmez()
    {
        await using var f = new KasaWebFactory();
        using var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await c.PostAsJsonAsync("/api/auth/login", new { sifre = (string?)null })).StatusCode);
    }

    [Theory]
    [InlineData(null, "ozel-sifre")]
    [InlineData("kisa", "ozel-sifre")]
    [InlineData("gelistirme-icin-varsayilan-anahtar-en-az-32-bayt!!", "ozel-sifre")]
    [InlineData("test-icin-yeterince-uzun-bir-imzalama-anahtari", "degistir-beni")]
    [InlineData("test-icin-yeterince-uzun-bir-imzalama-anahtari", null)]
    public void Uretim_eksik_veya_varsayilan_kimlikle_baslamaz(string? key, string? sifre)
    {
        var cfg = Config(key, sifre);
        Assert.Throws<InvalidOperationException>(() => KasaKimlikAyarlari.AnahtariDogrula(cfg, gelistirme: false));
    }

    [Fact]
    public void Gecerli_uretim_ayarlari_ve_acik_gelistirme_ayarlari_kabul_edilir()
    {
        const string ozel = "test-icin-yeterince-uzun-bir-imzalama-anahtari";
        Assert.Equal(ozel, KasaKimlikAyarlari.AnahtariDogrula(Config(ozel, "ozel-sifre"), false));
        const string dev = "gelistirme-icin-varsayilan-anahtar-en-az-32-bayt!!";
        Assert.Equal(dev, KasaKimlikAyarlari.AnahtariDogrula(Config(dev, "degistir-beni"), true));
    }

    private static IConfiguration Config(string? key, string? sifre) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kasa:JwtKey"] = key, ["Kasa:EditorKullanici"] = "editor", ["Kasa:EditorSifre"] = sifre
        }).Build();

    private record RolYanit(string Rol);
}
