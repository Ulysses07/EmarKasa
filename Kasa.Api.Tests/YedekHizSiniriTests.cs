using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.VekilVeHizSiniriTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Elle yedek (POST /api/yedek) ayrı ve sıkı 'yedek' politikasında: kimliği doğrulanmış kullanıcı ve istemci IP'si
/// başına pencere (üretimde saatte 5). 'guvenlik' kovasını tüketmez; kimliksiz istek 401 alır, kota tüketmez.
/// </summary>
public class YedekHizSiniriTests
{
    private static async Task YedekDizininde(string yedekIzni, Func<VekilFabrikasi, Task> senaryo)
    {
        var dizin = Path.Combine(Path.GetTempPath(), "kasa-yedek-hiz-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var f = new VekilFabrikasi(new()
            {
                ["Kasa:HizSiniri:YedekIzni"] = yedekIzni, ["Kasa:HizSiniri:GuvenlikIzni"] = "1",
                ["Yedek:Dizin"] = dizin, ["Yedek:Etkin"] = "false", ["Bildirim:PushEtkin"] = "false",
                ["Bildirim:WorkerEtkin"] = "false", ["Bildirim:AnahtarDosyasi"] = Path.Combine(dizin, ".kasa-push-keys.json"),
            });
            await senaryo(f);
        }
        finally
        {
            if (Directory.Exists(dizin)) Directory.Delete(dizin, true);
        }
    }

    private static async Task<HttpClient> Editor(VekilFabrikasi f, string ip)
    {
        var c = Istemci(f, ip);
        (await Giris(c, "editor", "kasa123")).EnsureSuccessStatusCode();
        return c;
    }

    private static async Task SinirDoldu(HttpResponseMessage yanit, string izin)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, yanit.StatusCode);
        Assert.True(yanit.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After başlığı saniye olarak gelmeli.");
        var hata = (await yanit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString();
        Assert.StartsWith($"Elle yedek sınırına ulaşıldı: 60 dakikada en çok {izin} elle yedek alınabilir.", hata);
        Assert.Contains("dakika sonra yeniden deneyin", hata);
        Assert.Contains("Otomatik yedekleme bundan etkilenmez", hata);
    }

    [Fact]
    public Task Elle_yedek_kullanici_ve_ip_basina_siki_sinirda_turkce_429_doner() => YedekDizininde("2", async f =>
    {
        using var ofis = await Editor(f, "198.51.100.10");
        for (var i = 0; i < 2; i++)
            using (var yanit = await ofis.PostAsync("/api/yedek", null))
                Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        await SinirDoldu(await ofis.PostAsync("/api/yedek", null), "2");

        // Aynı kullanıcı başka ağda ayrı kovadadır: ele geçirilmiş bir oturum editörün kendi kotasını tüketemez, tersi de.
        using var ev = await Editor(f, "198.51.100.11");
        using (var yanit = await ev.PostAsync("/api/yedek", null)) Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    });

    [Fact]
    public Task Elle_yedek_guvenlik_kovasini_tuketmez() => YedekDizininde("5", async f =>
    {
        using var ofis = await Editor(f, "198.51.100.20");
        for (var i = 0; i < 3; i++)
            using (var yanit = await ofis.PostAsync("/api/yedek", null))
                Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        // 'guvenlik' izni bu testte 1: yedekler tüketseydi şifre/kurtarma kodu işlemi 429 alırdı.
        var kod = await ofis.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "yanlis" });
        Assert.Equal(HttpStatusCode.BadRequest, kod.StatusCode);
    });

    [Fact]
    public Task Kimliksiz_ve_yetkisiz_istek_yedek_kotasini_tuketmez() => YedekDizininde("2", async f =>
    {
        using var anonim = Istemci(f, "198.51.100.30");
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.PostAsync("/api/yedek", null)).StatusCode);
        using (var editor = await f.EditorClientAsync())
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" })).EnsureSuccessStatusCode();
        using var izleyici = Istemci(f, "198.51.100.30");
        (await Giris(izleyici, null, "izleyici-sifresi")).EnsureSuccessStatusCode();
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsync("/api/yedek", null)).StatusCode);

        using var ofis = await Editor(f, "198.51.100.30");
        for (var i = 0; i < 2; i++)
            using (var yanit = await ofis.PostAsync("/api/yedek", null))
                Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        await SinirDoldu(await ofis.PostAsync("/api/yedek", null), "2");
    });

    [Fact]
    public async Task Yedek_ucu_ayri_yedek_politikasinda_uretimde_saatte_bes()
    {
        await using var f = new KasaWebFactory();
        _ = f.CreateClient();
        var uc = f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Single(u => u.RoutePattern.RawText == "/api/yedek"
            && u.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));
        Assert.Equal("yedek", uc.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
        var uretim = new Kasa.Api.Auth.HizSiniriAyarlari();
        Assert.Equal((5, 60), (uretim.YedekIzni, uretim.YedekPencereDakika));
    }
}
