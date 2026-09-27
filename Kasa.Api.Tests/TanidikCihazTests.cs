using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static Kasa.Api.Tests.VekilVeHizSiniriTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Kalıcı tanıdık cihaz belirteci: başarılı girişte verilir; tarayıcıda __Host- önekli HttpOnly/Secure/Strict çerez,
/// masaüstünde gövdedeki 'cihaz' alanı ve X-Kasa-Cihaz başlığı. Geçerli belirteç hedef kilidinden muaf tutar, ağ
/// bütçesinden tutmaz; durumsuzdur (yeniden başlatmada geçerli kalır), hedefe ve şifre/oturum damgasına bağlıdır.
/// Hedef kilidi dağıtık başarısız denemelerle kurulur (her IP bir kez, hedef bütçesi 4, ağ bütçesi 3).
/// </summary>
public class TanidikCihazTests
{
    private const string Baslik = "X-Kasa-Cihaz";
    private const string Cerez = "__Host-kasa_cihaz";

    private static Dictionary<string, string?> Ayar(params (string Anahtar, string Deger)[] ek)
    {
        var ayar = new Dictionary<string, string?>
        {
            ["Kasa:HizSiniri:HedefBasarisizIzni"] = "4", ["Kasa:HizSiniri:AgBasarisizIzni"] = "3",
            ["Kasa:HizSiniri:GirisKullaniciIzni"] = "50", ["Kasa:HizSiniri:GirisIpIzni"] = "50",
        };
        foreach (var (k, v) in ek) ayar[k] = v;
        return ayar;
    }

    /// <summary>Tarayıcı gibi davranan istemci: HTTPS kökeni, aynı kökenli Origin/Sec-Fetch-Site ve CSRF başlığı; çerezleri tutar.</summary>
    private static HttpClient Tarayici(KasaWebFactory f, string xff)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        c.DefaultRequestHeaders.Add("X-Forwarded-For", xff);
        c.DefaultRequestHeaders.Add("Origin", "https://localhost");
        c.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        c.DefaultRequestHeaders.Add("X-Kasa-Request", "1");
        return c;
    }

    /// <summary>Masaüstü gibi davranan istemci: Origin göndermez; belirteç varsa X-Kasa-Cihaz başlığıyla gönderir.</summary>
    private static HttpClient Masaustu(KasaWebFactory f, string xff, string? belirtec = null)
    {
        var c = Istemci(f, xff);
        if (belirtec is not null) c.DefaultRequestHeaders.Add(Baslik, belirtec);
        return c;
    }

    private static async Task<string?> GovdedekiBelirtec(HttpResponseMessage yanit)
    {
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();
        return govde.TryGetProperty("cihaz", out var cihaz) && cihaz.ValueKind == JsonValueKind.String ? cihaz.GetString() : null;
    }

    private static string? CihazCerezi(HttpResponseMessage yanit)
        => yanit.Headers.TryGetValues("Set-Cookie", out var cerezler) ? cerezler.FirstOrDefault(c => c.StartsWith(Cerez + "=", StringComparison.Ordinal)) : null;

    /// <summary>Hedefin bütçesini dağıtık başarısız denemelerle doldurur; yeni ağdan belirteçsiz doğru şifre 429 alır.</summary>
    private static async Task HedefiKilitle(KasaWebFactory f, string? kullanici, int ilkIp, string dogruSifre)
    {
        for (var i = 0; i < 4; i++)
        {
            using var c = Istemci(f, $"203.0.113.{ilkIp + i}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, kullanici, "yanlis")).StatusCode);
        }
        using var yabanci = Istemci(f, $"203.0.113.{ilkIp + 50}");
        await Reddedildi(await Giris(yabanci, kullanici, dogruSifre));
    }

    [Fact]
    public async Task Tarayiciya_host_onekli_httponly_secure_strict_cerez_verilir_govdede_belirtec_donmez()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var tarayici = Tarayici(f, "198.51.100.10");
        var yanit = await Giris(tarayici, "editor", "kasa123");

        Assert.Null(await GovdedekiBelirtec(yanit));
        var cerez = CihazCerezi(yanit);
        Assert.NotNull(cerez);
        var nitelikler = cerez!.Split(';', StringSplitOptions.TrimEntries).Skip(1).Select(n => n.ToLowerInvariant()).ToList();
        Assert.Contains("httponly", nitelikler);
        Assert.Contains("secure", nitelikler);
        Assert.Contains("samesite=strict", nitelikler);
        Assert.Contains("path=/", nitelikler);
        Assert.Contains("max-age=2592000", nitelikler);
        Assert.DoesNotContain(nitelikler, n => n.StartsWith("domain=", StringComparison.Ordinal));
        Assert.Matches(@"^__Host-kasa_cihaz=c1\.[0-9]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$", cerez.Split(';')[0]);
    }

    [Fact]
    public async Task Masaustune_belirtec_govdede_doner_cerez_verilmez()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var masaustu = Masaustu(f, "198.51.100.11");
        var yanit = await Giris(masaustu, "editor", "kasa123");
        Assert.StartsWith("c1.", await GovdedekiBelirtec(yanit));
        Assert.Null(CihazCerezi(yanit));
    }

    [Fact]
    public async Task Tarayici_cerezi_ve_masaustu_basligi_hedef_kilidinden_muaf_tutar()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var tarayici = Tarayici(f, "198.51.100.20");
        Assert.NotNull(CihazCerezi(await Giris(tarayici, "editor", "kasa123")));
        var belirtec = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.21"), "editor", "kasa123"));

        await HedefiKilitle(f, "editor", 1, "kasa123");

        // Dağıtık saldırı editörü kendi cihazlarından kilitleyemez.
        Assert.Equal(HttpStatusCode.OK, (await Giris(tarayici, "editor", "kasa123")).StatusCode);
        using var masaustu = Masaustu(f, "198.51.100.21", belirtec);
        Assert.Equal(HttpStatusCode.OK, (await Giris(masaustu, "editor", "kasa123")).StatusCode);
        // Muafiyet ağa değil cihaza bağlıdır: aynı ağdan belirteçsiz istemci de kilitlidir.
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.21"), "editor", "kasa123"));
    }

    [Fact]
    public async Task Tanidik_cihaz_ag_butcesinden_muaf_degildir()
    {
        await using var f = new VekilFabrikasi(Ayar());
        var belirtec = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.30"), "editor", "kasa123"));
        await HedefiKilitle(f, "editor", 10, "kasa123");
        using var cihaz = Masaustu(f, "198.51.100.30", belirtec);
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(cihaz, "editor", "yanlis")).StatusCode);
        await Reddedildi(await Giris(cihaz, "editor", "kasa123"));
    }

    [Fact]
    public async Task Ele_gecirilen_belirtec_dagitik_denemeyi_ag_sayisiyla_cogaltamaz()
    {
        await using var f = new VekilFabrikasi(Ayar());
        var belirtec = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.40"), "editor", "kasa123"));
        await HedefiKilitle(f, "editor", 20, "kasa123");
        // Her ağdan tek deneme ağ bütçesine takılmaz; cihaz başına bütçe (ağ bütçesi kadar) dolunca belirteç de reddedilir.
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(Masaustu(f, $"192.0.2.{i + 1}", belirtec), "editor", "yanlis")).StatusCode);
        await Reddedildi(await Giris(Masaustu(f, "192.0.2.50", belirtec), "editor", "kasa123"));
    }

    [Fact]
    public async Task Yeniden_baslatmadan_sonra_editor_kendi_cihazindan_girer()
    {
        string? masaustuBelirteci;
        string cerez;
        await using (var once = new VekilFabrikasi(Ayar()))
        {
            masaustuBelirteci = await GovdedekiBelirtec(await Giris(Masaustu(once, "198.51.100.50"), "editor", "kasa123"));
            cerez = CihazCerezi(await Giris(Tarayici(once, "198.51.100.51"), "editor", "kasa123"))!.Split(';')[0];
        }

        // Yeni süreç: bellekteki bütün sayaçlar ve kayıtlar sıfır; belirteç kendi başına doğrulanır.
        await using var sonra = new VekilFabrikasi(Ayar());
        await HedefiKilitle(sonra, "editor", 30, "kasa123");
        using var masaustu = Masaustu(sonra, "198.51.100.50", masaustuBelirteci);
        Assert.Equal(HttpStatusCode.OK, (await Giris(masaustu, "editor", "kasa123")).StatusCode);
        using var tarayici = Tarayici(sonra, "198.51.100.51");
        using var istek = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { kullanici = "editor", sifre = "kasa123" }) };
        istek.Headers.Add("Cookie", cerez);
        Assert.Equal(HttpStatusCode.OK, (await tarayici.SendAsync(istek)).StatusCode);
        await Reddedildi(await Giris(Masaustu(sonra, "198.51.100.52"), "editor", "kasa123"));
    }

    [Fact]
    public async Task Editor_sifresi_degisince_belirtec_gecersizlesir()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var cihaz = Masaustu(f, "198.51.100.60");
        var govde = await (await Giris(cihaz, "editor", "kasa123")).Content.ReadFromJsonAsync<JsonElement>();
        var belirtec = govde.GetProperty("cihaz").GetString();
        cihaz.DefaultRequestHeaders.Authorization = new("Bearer", govde.GetProperty("token").GetString());
        (await cihaz.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = "yepyeni-editor-sifresi" })).EnsureSuccessStatusCode();

        await HedefiKilitle(f, "editor", 40, "yepyeni-editor-sifresi");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.60", belirtec), "editor", "yepyeni-editor-sifresi"));
    }

    [Fact]
    public async Task Alici_oturum_surumu_degisince_belirtec_gecersizlesir()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var editor = await f.EditorClientAsync();
        var olustur = await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı", "alici-sifre-1"));
        olustur.EnsureSuccessStatusCode();
        var id = (await olustur.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var belirtec = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.70"), "alici-1", "alici-sifre-1"));

        await HedefiKilitle(f, "alici-1", 60, "alici-sifre-1");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.70", belirtec), "alici-1", "alici-sifre-1")).StatusCode);

        // Pasife alıp açmak oturum sürümünü artırır (şifre aynı kalsa da): eski belirteç düşer.
        (await editor.PutAsJsonAsync($"/api/alicilar/{id}", new AliciYaz("alici-1", "Alıcı", null, Aktif: false))).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync($"/api/alicilar/{id}", new AliciYaz("alici-1", "Alıcı", null, Aktif: true))).EnsureSuccessStatusCode();
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.70", belirtec), "alici-1", "alici-sifre-1"));
    }

    [Fact]
    public async Task Izleyici_sifresi_degisince_belirtec_gecersizlesir()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var editor = await f.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" })).EnsureSuccessStatusCode();
        var belirtec = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.80"), null, "izleyici-sifresi"));

        await HedefiKilitle(f, null, 70, "izleyici-sifresi");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.80", belirtec), null, "izleyici-sifresi")).StatusCode);

        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "yeni-izleyici-sifresi" })).EnsureSuccessStatusCode();
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.80", belirtec), null, "yeni-izleyici-sifresi"));
    }

    [Fact]
    public async Task Belirtec_verildigi_hedefe_baglidir()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using (var editor = await f.EditorClientAsync())
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" })).EnsureSuccessStatusCode();
        var editorBelirteci = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.90"), "editor", "kasa123"));

        await HedefiKilitle(f, null, 80, "izleyici-sifresi");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.90", editorBelirteci), null, "izleyici-sifresi"));
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.90", editorBelirteci), "yok-1", "izleyici-sifresi"));
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.90", editorBelirteci), "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Otuz_gun_gecince_belirtec_muaf_tutmaz()
    {
        var saat = new ElleSaat(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        await using var f = new VekilFabrikasi(Ayar(), saat: saat);
        var belirtec = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.100"), "editor", "kasa123"));

        saat.Simdi = saat.Simdi.AddDays(29);
        await HedefiKilitle(f, "editor", 100, "kasa123");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.100", belirtec), "editor", "kasa123")).StatusCode);

        // 29. gündeki giriş yeni belirteç verdi; eski belirteç 30. günün sonunda düşer.
        saat.Simdi = saat.Simdi.AddDays(2);
        await HedefiKilitle(f, "editor", 150, "kasa123");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.100", belirtec), "editor", "kasa123"));
    }

    [Theory]
    [InlineData("imza")]
    [InlineData("sure")]
    [InlineData("kimlik")]
    [InlineData("bicim")]
    public async Task Degistirilmis_belirtec_muaf_tutmaz(string bozulan)
    {
        await using var f = new VekilFabrikasi(Ayar());
        var belirtec = (await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.110"), "editor", "kasa123")))!;
        var p = belirtec.Split('.');
        var bozuk = bozulan switch
        {
            "imza" => string.Join('.', p[0], p[1], p[2], (p[3][0] == 'A' ? "B" : "A") + p[3][1..]),
            "sure" => string.Join('.', p[0], (long.Parse(p[1]) - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), p[2], p[3]),
            "kimlik" => string.Join('.', p[0], p[1], (p[2][0] == 'A' ? "B" : "A") + p[2][1..], p[3]),
            _ => "c2." + string.Join('.', p[1..]),
        };
        await HedefiKilitle(f, "editor", 110, "kasa123");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.110", bozuk), "editor", "kasa123"));
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.110", belirtec), "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Cikis_tanidik_cihaz_cerezini_silmez()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var tarayici = Tarayici(f, "198.51.100.120");
        (await Giris(tarayici, "editor", "kasa123")).EnsureSuccessStatusCode();
        var cikis = await tarayici.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, cikis.StatusCode);
        Assert.Null(CihazCerezi(cikis));
        await HedefiKilitle(f, "editor", 120, "kasa123");
        Assert.Equal(HttpStatusCode.OK, (await Giris(tarayici, "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Ozellik_kapaliyken_belirtec_verilmez_ve_kabul_edilmez()
    {
        string? belirtec;
        await using (var acik = new VekilFabrikasi(Ayar()))
            belirtec = await GovdedekiBelirtec(await Giris(Masaustu(acik, "198.51.100.130"), "editor", "kasa123"));
        await using var f = new VekilFabrikasi(Ayar(("Kasa:HizSiniri:TanidikCihazGun", "0")));
        using var tarayici = Tarayici(f, "198.51.100.131");
        var yanit = await Giris(Masaustu(f, "198.51.100.130"), "editor", "kasa123");
        Assert.Null(await GovdedekiBelirtec(yanit));
        Assert.Null(CihazCerezi(await Giris(tarayici, "editor", "kasa123")));
        await HedefiKilitle(f, "editor", 130, "kasa123");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.130", belirtec), "editor", "kasa123"));
    }
}
