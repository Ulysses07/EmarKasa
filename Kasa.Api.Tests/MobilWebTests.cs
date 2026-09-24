using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kasa.Api.Endpoints;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kasa.Api.Tests;

/// <summary>
/// Paket F, madde 19: telefon için salt okunur web uygulaması (/m). Sunum, güvenlik başlıkları,
/// yalnız /m'ye özgü "index.html'e düş" davranışı ve /api yazmalarında aynı kaynak koşulu.
/// </summary>
public class MobilWebTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _f;
    public MobilWebTests(KasaWebFactory f) => _f = f;

    private HttpClient Istemci() => _f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Kaynak ağacındaki wwwroot/m (testler dosya içeriğini de denetler).</summary>
    private static string Klasor([System.Runtime.CompilerServices.CallerFilePath] string yol = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(yol)!, "..", "Kasa.Api", "wwwroot", "m"));

    [Fact]
    public async Task M_yolu_m_slasha_yonlenir()
    {
        var r = await Istemci().GetAsync("/m");
        Assert.True(r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.MovedPermanently,
            $"Beklenmeyen durum: {r.StatusCode}");
        Assert.Equal("/m/", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Index_html_sikı_guvenlik_basliklariyla_sunulur()
    {
        var r = await Istemci().GetAsync("/m/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/html", r.Content.Headers.ContentType?.MediaType);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("<script src=\"/m/app.js\" defer></script>", html);

        Assert.Equal(MobilWeb.Csp, r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("same-origin", r.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Contains("camera=()", r.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("no-cache", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public void Csp_satir_ici_script_ve_stile_izin_vermez()
    {
        Assert.DoesNotContain("unsafe-inline", MobilWeb.Csp);
        Assert.DoesNotContain("unsafe-eval", MobilWeb.Csp);
        Assert.Contains("default-src 'none'", MobilWeb.Csp);
        Assert.Contains("script-src 'self'", MobilWeb.Csp);
        Assert.Contains("connect-src 'self'", MobilWeb.Csp);
        Assert.Contains("frame-ancestors 'none'", MobilWeb.Csp);
    }

    [Theory]
    [InlineData("/m/index.html", "text/html")]
    [InlineData("/m/app.js", "javascript")]
    [InlineData("/m/sw.js", "javascript")]
    [InlineData("/m/app.css", "text/css")]
    [InlineData("/m/manifest.webmanifest", "application/manifest+json")]
    [InlineData("/m/icons/icon-192.png", "image/png")]
    [InlineData("/m/icons/icon-512.png", "image/png")]
    [InlineData("/m/icons/icon-maskable-512.png", "image/png")]
    [InlineData("/m/icons/apple-touch-icon.png", "image/png")]
    public async Task Kabuk_dosyalari_dogru_turle_ve_csp_ile_sunulur(string yol, string tur)
    {
        var r = await Istemci().GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(tur, r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(MobilWeb.Csp, r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Theory]
    [InlineData("/m/panel")]
    [InlineData("/m/aylik/2026")]
    public async Task M_altinda_uzantisiz_yol_index_htmle_duser(string yol)
    {
        var r = await Istemci().GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/html", r.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Emar Kasa", await r.Content.ReadAsStringAsync());
        Assert.Equal(MobilWeb.Csp, r.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/haftalik")]
    [InlineData("/mobil")]
    [InlineData("/mx/")]
    [InlineData("/app.js")]
    [InlineData("/sw.js")]
    [InlineData("/m/yok.js")]
    [InlineData("/m/icons/yok.png")]
    [InlineData("/m/%2e%2e/appsettings.json")]
    [InlineData("/m/..%2fappsettings.json")]
    [InlineData("/wwwroot/m/index.html")]
    public async Task M_disi_ve_bilinmeyen_dosyalar_404(string yol)
    {
        var r = await Istemci().GetAsync(yol);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        var govde = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JwtKey", govde);
    }

    [Fact]
    public async Task M_altinda_get_disi_istek_404()
    {
        var r = await Istemci().PostAsync("/m/panel", new StringContent("x"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Kabuk_oturumsuz_acilir_ama_veri_oturum_ister()
    {
        var c = Istemci();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/m/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/rapor/panel")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Tarayici_gibi_ayni_kaynaktan_cerezle_giris_ve_okuma()
    {
        var c = Istemci();
        var giris = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { kullanici = "editor", sifre = PaketEYardimci.EditorSifre }),
        };
        giris.Headers.Add("Origin", "http://localhost");
        giris.Headers.Add("Sec-Fetch-Site", "same-origin");
        var r = await c.SendAsync(giris);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cerez = Assert.Single(r.Headers.GetValues("Set-Cookie"), s => s.StartsWith("kasa_auth="));
        Assert.Contains("httponly", cerez, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cerez, StringComparison.OrdinalIgnoreCase);

        // Telefon ekranlarının okuduğu uçlar çerezle açılır.
        foreach (var yol in new[] { "/api/auth/me", "/api/rapor/panel", "/api/rapor/haftalik", "/api/rapor/aylik?yil=2026&ay=9",
                     "/api/cekler/ozet", "/api/kredikartlari" })
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(yol)).StatusCode);

        var cikis = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        cikis.Headers.Add("Sec-Fetch-Site", "same-origin");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(cikis)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/rapor/panel")).StatusCode);
    }

    // ---------------------------------------------------------------- aynı kaynak koşulu

    private static HttpRequestMessage GirisIstegi(string? origin = null, string? secFetchSite = null)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { kullanici = "editor", sifre = PaketEYardimci.EditorSifre }),
        };
        if (origin is not null) m.Headers.TryAddWithoutValidation("Origin", origin);
        if (secFetchSite is not null) m.Headers.Add("Sec-Fetch-Site", secFetchSite);
        return m;
    }

    [Theory]
    [InlineData("https://evil.example", null)]
    [InlineData("http://localhost.evil.example", null)]
    [InlineData("http://localhost:5001", null)]
    [InlineData("null", null)]
    [InlineData(null, "cross-site")]
    [InlineData(null, "same-site")]
    [InlineData("http://localhost", "cross-site")]   // Sec-Fetch-Site önceliklidir
    public async Task Baska_kaynaktan_yazma_istegi_403(string? origin, string? site)
    {
        var r = await Istemci().SendAsync(GirisIstegi(origin, site));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Başka bir siteden gelen istek reddedildi.", j.GetProperty("hata").GetString());
        Assert.False(r.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Theory]
    [InlineData(null, null)]                  // masaüstü uygulaması / curl: başlık yok
    [InlineData("http://localhost", null)]    // eski tarayıcı: Origin sunucuyla aynı
    [InlineData(null, "same-origin")]
    [InlineData("https://evil.example", "same-origin")]   // Sec-Fetch-Site önceliklidir (tarayıcı yazar, sayfa değiştiremez)
    [InlineData(null, "none")]
    public async Task Ayni_kaynak_ve_tarayici_disi_istemci_gecer(string? origin, string? site)
    {
        var r = await Istemci().SendAsync(GirisIstegi(origin, site));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Oturumlu_istemci_de_baska_kaynaktan_yazamaz_okuma_etkilenmez()
    {
        var c = await _f.EditorClientAsync();
        var sil = new HttpRequestMessage(HttpMethod.Put, "/api/ayarlar")
        {
            Content = JsonContent.Create(new { takipBaslangic = "2020-01-01", kasaAcilisDevri = 999 }),
        };
        sil.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(sil)).StatusCode);
        var ayar = await c.GetFromJsonAsync<JsonElement>("/api/ayarlar");
        Assert.NotEqual(999m, ayar.GetProperty("kasaAcilisDevri").GetDecimal());

        var oku = new HttpRequestMessage(HttpMethod.Get, "/api/rapor/panel");
        oku.Headers.Add("Origin", "https://evil.example");
        oku.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(oku)).StatusCode);
    }

    [Fact]
    public async Task Api_disi_yazma_istegine_kosul_uygulanmaz()
    {
        var m = new HttpRequestMessage(HttpMethod.Post, "/m/panel") { Content = new StringContent("x") };
        m.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.NotFound, (await Istemci().SendAsync(m)).StatusCode);
    }

    // ---------------------------------------------------------------- dosya içeriği denetimi

    [Fact]
    public void Index_html_satir_ici_script_stil_ve_olay_ozelligi_icermez()
    {
        var html = File.ReadAllText(Path.Combine(Klasor(), "index.html"));
        foreach (Match m in Regex.Matches(html, @"<script\b[^>]*>", RegexOptions.IgnoreCase))
            Assert.Contains("src=", m.Value);
        Assert.DoesNotMatch(new Regex(@"<script\b[^>]*>\s*[^<\s]", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"<style\b", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"\sstyle\s*=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"javascript:", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"https?://", RegexOptions.IgnoreCase), html);   // dış kaynak yok
    }

    [Fact]
    public void App_js_html_yorumlamaz_ve_veriyi_cihazda_saklamaz()
    {
        var js = File.ReadAllText(Path.Combine(Klasor(), "app.js"));
        foreach (var yasak in new[] { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write", "eval(", "new Function",
                     "localStorage", "sessionStorage", "indexedDB", "setAttribute('style'", ".style." })
            Assert.DoesNotContain(yasak, js);
        Assert.Contains("credentials: 'same-origin'", js);
        Assert.Contains("cache: 'no-store'", js);
        Assert.DoesNotMatch(new Regex(@"https?://"), js);
    }

    /// <summary>
    /// Bulgu: "bugün" cihaz saatinden alınıyordu; saati Türkiye'nin gerisinde olan telefonda başlamış dönem
    /// gizleniyor, aylık ekran önceki ayda açılıyordu. Bugün Türkiye saatiyle alınır; haftalık liste
    /// sunucunun (Türkiye saatiyle bugünde kesilen) takvimini olduğu gibi gösterir.
    /// </summary>
    [Fact]
    public void App_js_bugunu_turkiye_saatiyle_alir_cihaz_tarihine_gore_donem_gizlemez()
    {
        var js = File.ReadAllText(Path.Combine(Klasor(), "app.js"));
        Assert.Contains("timeZone: 'Europe/Istanbul'", js);
        foreach (var yasak in new[] { "getFullYear(", "getMonth(", "getDate(", "getDay(", "toLocaleDateString(", "donem.start <=" })
            Assert.DoesNotContain(yasak, js);
        Assert.Contains("const b = turkiyeBugun(); durum.yil = b.yil; durum.ay = b.ay;", js);
    }

    [Fact]
    public void Service_worker_apiyi_asla_onbellege_almaz_yalniz_kabugu_alir()
    {
        var sw = File.ReadAllText(Path.Combine(Klasor(), "sw.js"));
        Assert.Contains("if (url.pathname === '/api' || url.pathname.startsWith('/api/')) return;", sw);
        Assert.Contains("if (istek.method !== 'GET') return;", sw);
        Assert.Contains("if (url.origin !== self.location.origin) return;", sw);
        Assert.Contains("KABUK.includes(url.pathname)", sw);   // yalnız kabuk dosyaları yazılır
        var kabuk = Regex.Match(sw, @"const KABUK = \[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        Assert.NotEmpty(kabuk);
        Assert.DoesNotContain("/api", kabuk);
        foreach (Match m in Regex.Matches(kabuk, @"'(/m/[^']*)'"))
        {
            var yol = m.Groups[1].Value["/m/".Length..];
            if (yol.Length > 0) Assert.True(File.Exists(Path.Combine(Klasor(), yol)), $"Kabukta olmayan dosya: {yol}");
        }
    }

    [Fact]
    public void Manifest_gecerli_ve_simgeleri_var()
    {
        using var j = JsonDocument.Parse(File.ReadAllText(Path.Combine(Klasor(), "manifest.webmanifest")));
        var k = j.RootElement;
        Assert.Equal("/m/", k.GetProperty("start_url").GetString());
        Assert.Equal("/m/", k.GetProperty("scope").GetString());
        Assert.Equal("standalone", k.GetProperty("display").GetString());
        var simgeler = k.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(simgeler, s => s.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(simgeler, s => s.GetProperty("sizes").GetString() == "512x512");
        Assert.Contains(simgeler, s => s.GetProperty("purpose").GetString() == "maskable");
        foreach (var s in simgeler)
        {
            var dosya = Path.Combine(Klasor(), s.GetProperty("src").GetString()!["/m/".Length..]);
            var b = File.ReadAllBytes(dosya);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, b[..4]);   // PNG imzası
        }
    }

    [Fact]
    public void Klasor_bulucu_icerik_kokunde_ya_da_uygulama_klasorunde_arar()
    {
        Assert.Equal(Klasor(), MobilWeb.KlasorBul(Path.GetFullPath(Path.Combine(Klasor(), "..", ".."))));
        // Derleme çıktısına da kopyalanır (Kasa.Api.csproj: wwwroot → çıktı).
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "m", "index.html")));
        Assert.NotNull(MobilWeb.KlasorBul(Path.Combine(Path.GetTempPath(), "olmayan-" + Guid.NewGuid().ToString("N"))));
    }
}
