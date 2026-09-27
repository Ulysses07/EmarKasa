using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kasa.Api.Tests;

/// <summary>
/// Ters vekil (nginx → docker köprüsü) arkasında gerçek istemci IP'si ve hız sınırı bölümleri.
/// TestServer bağlantı adresi vermediği için bağlantının geldiği adres 'X-Test-Baglanti' başlığıyla
/// (varsayılan 127.0.0.1, yani güvenilen vekil) simüle edilir; farklı istemciler X-Forwarded-For ile gelir.
/// </summary>
public class VekilVeHizSiniriTests
{
    private const string Vekil = "X-Test-Baglanti";

    /// <summary>Sınırları küçültülmüş, bağlantı adresini başlıktan alan fabrika.</summary>
    internal sealed class VekilFabrikasi(Dictionary<string, string?>? ek = null, UyariToplayici? loglar = null) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (loglar is not null) builder.ConfigureLogging(logging => logging.AddProvider(loglar));
            var ayarlar = new Dictionary<string, string?>
            {
                ["Kasa:HizSiniri:GirisKullaniciIzni"] = "3",
                ["Kasa:HizSiniri:GirisIpIzni"] = "6",
                ["Kasa:HizSiniri:GuvenlikIzni"] = "4",
            };
            foreach (var (k, v) in ek ?? new()) ayarlar[k] = v;
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(ayarlar));
            builder.ConfigureServices(s => s.AddSingleton<IStartupFilter, BaglantiAdresi>());
        }

        private sealed class BaglantiAdresi : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use((http, sonraki) =>
                {
                    var adres = http.Request.Headers[Vekil].ToString();
                    http.Connection.RemoteIpAddress = IPAddress.Parse(adres.Length > 0 ? adres : "127.0.0.1");
                    return sonraki(http);
                });
                next(app);
            };
        }
    }

    private static HttpClient Istemci(KasaWebFactory f, string? xff, string? baglanti = null)
    {
        var c = f.CreateClient();
        if (xff is not null) c.DefaultRequestHeaders.Add("X-Forwarded-For", xff);
        if (baglanti is not null) c.DefaultRequestHeaders.Add(Vekil, baglanti);
        return c;
    }

    private static Task<HttpResponseMessage> Giris(HttpClient c, string? kullanici, string sifre)
        => c.PostAsJsonAsync("/api/auth/login", new { kullanici, sifre });

    private static async Task Reddedildi(HttpResponseMessage yanit)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, yanit.StatusCode);
        Assert.True(yanit.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After başlığı saniye olarak gelmeli.");
        var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("Çok fazla deneme yapıldı.", govde.GetProperty("hata").GetString());
    }

    [Fact]
    public async Task Kimliksiz_saldirgan_baska_ipdeki_editorun_girisini_kilitleyemez()
    {
        await using var f = new VekilFabrikasi();
        using var saldirgan = Istemci(f, "198.51.100.1");
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(saldirgan, "editor", "yanlis")).StatusCode);
        // Doğru şifre de denenmeden reddedilir: kaba kuvvet bekleme süresinde ilerleyemez.
        await Reddedildi(await Giris(saldirgan, "editor", "kasa123"));

        using var editor = Istemci(f, "198.51.100.2");
        Assert.Equal(HttpStatusCode.OK, (await Giris(editor, "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Ayni_ipde_kilitlenen_kullanici_adi_diger_kullanicilari_etkilemez_ama_ip_penceresi_ustte_kalir()
    {
        await using var f = new VekilFabrikasi();
        using (var editor = await f.EditorClientAsync())
            (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı", "alici-sifre-1"))).EnsureSuccessStatusCode();
        using var ofis = Istemci(f, "198.51.100.3");
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(ofis, " EDITOR ", "yanlis")).StatusCode);
        // Normalize kullanıcı adı ("editor") kilitli; aynı ofisten alıcı yine girer.
        await Reddedildi(await Giris(ofis, "editor", "yanlis"));
        Assert.Equal(HttpStatusCode.OK, (await Giris(ofis, "alici-1", "alici-sifre-1")).StatusCode);
        // Kullanıcı adı değiştirerek deneme IP başına genel pencereyle sınırlanır (6 istek / pencere).
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(ofis, "rastgele-1", "x")).StatusCode);
        await Reddedildi(await Giris(ofis, "rastgele-2", "x"));
    }

    [Fact]
    public async Task Istemcinin_kendi_gonderdigi_x_forwarded_for_sinirdan_kacirmaz()
    {
        await using var f = new VekilFabrikasi();
        // nginx $proxy_add_x_forwarded_for istemcinin zincirinin sonuna gerçek adresi ekler; yalnız o kullanılır.
        for (var i = 0; i < 3; i++)
        {
            using var c = Istemci(f, $"10.0.0.{i}, 198.51.100.7");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "editor", "yanlis")).StatusCode);
        }
        using var son = Istemci(f, "10.9.9.9, 198.51.100.7");
        await Reddedildi(await Giris(son, "editor", "yanlis"));
    }

    [Fact]
    public async Task Guvenilmeyen_kaynaktan_gelen_x_forwarded_for_yok_sayilir()
    {
        await using var f = new VekilFabrikasi();
        for (var i = 0; i < 3; i++)
        {
            using var c = Istemci(f, $"198.51.100.{10 + i}", baglanti: "203.0.113.5");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "editor", "yanlis")).StatusCode);
        }
        using var son = Istemci(f, "198.51.100.99", baglanti: "203.0.113.5");
        await Reddedildi(await Giris(son, "editor", "yanlis"));
    }

    [Fact]
    public async Task Guvenilmeyen_vekilden_gelen_x_forwarded_for_bir_kez_uyari_olarak_loglanir()
    {
        var loglar = new UyariToplayici();
        await using var f = new VekilFabrikasi(loglar: loglar);
        for (var i = 0; i < 2; i++)
        {
            using var c = Istemci(f, "198.51.100.60", baglanti: "192.168.16.1");
            await Giris(c, "editor", "yanlis");
        }
        using var guvenilir = Istemci(f, "198.51.100.61");
        await Giris(guvenilir, "editor", "yanlis");
        using var basliksiz = Istemci(f, null, baglanti: "203.0.113.9");
        await Giris(basliksiz, "editor", "yanlis");

        var uyari = Assert.Single(loglar.Uyarilar, u => u.Contains("Kasa:GuvenilirVekiller"));
        Assert.Contains("192.168.16.1", uyari);
    }

    [Fact]
    public async Task Docker_kopru_ag_gecidi_varsayilan_olarak_guvenilir_vekildir()
    {
        await using var f = new VekilFabrikasi();
        for (var i = 0; i < 3; i++)
        {
            using var c = Istemci(f, "198.51.100.30", baglanti: "172.18.0.1");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "editor", "yanlis")).StatusCode);
        }
        using var baska = Istemci(f, "198.51.100.31", baglanti: "172.18.0.1");
        Assert.Equal(HttpStatusCode.OK, (await Giris(baska, "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Ipv6_istemciler_64_onekiyle_ayni_bolume_duser()
    {
        await using var f = new VekilFabrikasi();
        for (var i = 1; i <= 3; i++)
        {
            using var c = Istemci(f, $"2001:db8:1:2::{i}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "editor", "yanlis")).StatusCode);
        }
        using var ayniAg = Istemci(f, "2001:db8:1:2:ffff::9");
        await Reddedildi(await Giris(ayniAg, "editor", "yanlis"));
        using var baskaAg = Istemci(f, "2001:db8:1:3::1");
        Assert.Equal(HttpStatusCode.OK, (await Giris(baskaAg, "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Kurtarma_ile_giris_de_ip_ve_kullanici_adi_basina_sinirlanir()
    {
        await using var f = new VekilFabrikasi();
        using var saldirgan = Istemci(f, "198.51.100.40");
        var govde = new { kullanici = "editor", kod = "YANLIS", yeniSifre = "yepyeni-sifre-123" };
        for (var i = 0; i < 3; i++)
        {
            var yanit = await saldirgan.PostAsJsonAsync("/api/auth/kurtar", govde);
            Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
            Assert.Contains("kurtarma kodu", (await yanit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString());
        }
        await Reddedildi(await saldirgan.PostAsJsonAsync("/api/auth/kurtar", govde));
        using var editor = Istemci(f, "198.51.100.41");
        Assert.Equal(HttpStatusCode.OK, (await Giris(editor, "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Guvenlik_politikasi_gercek_istemci_ipsine_gore_bolunur_ve_kimliksiz_istek_kota_tuketmez()
    {
        await using var f = new VekilFabrikasi();
        using var editorA = Istemci(f, "198.51.100.50");
        (await Giris(editorA, "editor", "kasa123")).EnsureSuccessStatusCode();
        using var anonim = Istemci(f, "198.51.100.50");
        for (var i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "x" })).StatusCode);
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await editorA.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "yanlis" })).StatusCode);
        await Reddedildi(await editorA.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "yanlis" }));

        using var editorB = Istemci(f, "198.51.100.51");
        (await Giris(editorB, "editor", "kasa123")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await editorB.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "yanlis" })).StatusCode);
    }
    [Fact]
    public void Guvenilir_vekiller_varsayilani_loopback_ve_docker_kopru_araligidir()
    {
        var aglar = HizSinirlari.GuvenilirAglar(null);
        foreach (var ip in new[] { "127.0.0.1", "::1", "172.17.0.1", "172.18.0.1", "172.31.255.254" })
            Assert.Contains(aglar, a => a.Contains(IPAddress.Parse(ip)));
        foreach (var ip in new[] { "203.0.113.5", "192.168.1.1", "10.0.0.1", "172.32.0.1" })
            Assert.DoesNotContain(aglar, a => a.Contains(IPAddress.Parse(ip)));
        var ozel = HizSinirlari.GuvenilirAglar(" 10.1.2.0/24 ; 192.0.2.7 ");
        Assert.Equal(2, ozel.Count);
        Assert.True(ozel[0].Contains(IPAddress.Parse("10.1.2.200")));
        Assert.True(ozel[1].Contains(IPAddress.Parse("192.0.2.7")));
        Assert.False(ozel[1].Contains(IPAddress.Parse("192.0.2.8")));
    }

    [Theory]
    [InlineData("bozuk")]
    [InlineData("10.0.0.0/33")]
    [InlineData("127.0.0.0/8;abc/12")]
    public void Gecersiz_guvenilir_vekil_degeri_reddedilir(string deger)
    {
        var hata = Assert.Throws<InvalidOperationException>(() => HizSinirlari.GuvenilirAglar(deger));
        Assert.Contains("Kasa:GuvenilirVekiller", hata.Message);
    }

    [Fact]
    public async Task Gecersiz_guvenilir_vekil_ayari_uygulamayi_baslatmaz()
    {
        await using var f = new VekilFabrikasi(new() { ["Kasa:GuvenilirVekiller"] = "172.16.0.0/40" });
        var hata = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Kasa:GuvenilirVekiller", hata.ToString());
    }

    [Fact]
    public async Task Vekil_basliklari_yalniz_for_ve_proto_ile_tek_adimdir()
    {
        await using var f = new VekilFabrikasi();
        var o = f.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, o.ForwardedHeaders);
        Assert.Equal(1, o.ForwardLimit);
        Assert.Empty(o.KnownProxies);
        Assert.Equal(3, o.KnownIPNetworks.Count);
    }

    [Fact]
    public async Task Giris_ve_kurtarma_giris_politikasinda_hassas_uclar_hiz_sinirinda()
    {
        await using var f = new KasaWebFactory();
        _ = f.CreateClient();
        var uclar = f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        string? Politika(string desen, string metot) => uclar.Single(u => u.RoutePattern.RawText == desen
            && u.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Contains(metot))
            .Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        Assert.Equal(HizSinirlari.Giris, Politika("/api/auth/login", "POST"));
        Assert.Equal(HizSinirlari.Giris, Politika("/api/auth/kurtar", "POST"));
        Assert.Equal(HizSinirlari.Guvenlik, Politika("/api/auth/sifre", "POST"));
        Assert.Equal(HizSinirlari.Guvenlik, Politika("/api/auth/kurtarma-kodu", "POST"));
        foreach (var (desen, metot) in new[] { ("/api/yedek", "POST"), ("/api/ekstre-aktar/yukle", "POST"),
            ("/api/bildirimler/push/abonelik", "POST"), ("/api/bildirimler/test", "POST") })
            Assert.NotNull(Politika(desen, metot));
    }

    [Fact]
    public async Task Hiz_siniri_ayarlari_sifirdan_buyuk_olmalidir()
    {
        await using var f = new VekilFabrikasi(new() { ["Kasa:HizSiniri:GirisIpIzni"] = "0" });
        var hata = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Kasa:HizSiniri", hata.ToString());
    }
}
