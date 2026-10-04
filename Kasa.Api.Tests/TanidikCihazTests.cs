using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static Kasa.Api.Tests.VekilVeHizSiniriTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Kalıcı tanıdık cihaz belirteci: başarılı girişte, oturum doğrulamasında (/api/auth/me) ve editörün şifre
/// değişikliği/kurtarmasında verilir; tarayıcıda rol başına __Host- önekli HttpOnly/Secure/Strict çerez, masaüstünde
/// gövdedeki 'cihaz' alanı (gövdesiz yanıtta X-Kasa-Cihaz yanıt başlığı) ve girişte X-Kasa-Cihaz başlığı. Geçerli
/// belirteç hedef kilidinden muaf tutar, ağ bütçesinden tutmaz; durumsuzdur (yeniden başlatmada geçerli kalır),
/// hedefe ve şifre/oturum damgasına bağlıdır, ömrü oturumdan (30 gün) uzundur (fabrikada 180 gün).
/// Hedef kilidi dağıtık başarısız denemelerle kurulur (her IP bir kez, hedef bütçesi 4, ağ bütçesi 3).
/// </summary>
public class TanidikCihazTests
{
    private const string Baslik = "X-Kasa-Cihaz";
    private const string EditorCerezi = "__Host-kasa_cihaz_editor";
    private const string IzleyiciCerezi = "__Host-kasa_cihaz_viewer";
    private static readonly DateTimeOffset Baslangic = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, string?> Ayar(params (string Anahtar, string Deger)[] ek)
    {
        var ayar = new Dictionary<string, string?>
        {
            ["Kasa:HizSiniri:HedefBasarisizIzni"] = "4",
            ["Kasa:HizSiniri:AgBasarisizIzni"] = "3",
            ["Kasa:HizSiniri:GirisKullaniciIzni"] = "50",
            ["Kasa:HizSiniri:GirisIpIzni"] = "50",
        };
        foreach (var (k, v) in ek)
            ayar[k] = v;
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
        if (belirtec is not null)
            c.DefaultRequestHeaders.Add(Baslik, belirtec);
        return c;
    }

    private static async Task<string?> GovdedekiBelirtec(HttpResponseMessage yanit)
    {
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();
        return govde.TryGetProperty("cihaz", out var cihaz) && cihaz.ValueKind == JsonValueKind.String ? cihaz.GetString() : null;
    }

    /// <summary>Masaüstü girişi: (oturum JWT'si, gövdedeki belirteç).</summary>
    private static async Task<(string Jwt, string Belirtec)> MasaustuGirisi(KasaWebFactory f, string xff, string? kullanici, string sifre)
    {
        using var c = Masaustu(f, xff);
        var yanit = await Giris(c, kullanici, sifre);
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();
        return (govde.GetProperty("token").GetString()!, govde.GetProperty("cihaz").GetString()!);
    }

    /// <summary>Belirteç başlığı olmadan Bearer oturumuyla gelen masaüstü istemcisi.</summary>
    private static HttpClient Oturumlu(KasaWebFactory f, string xff, string jwt)
    {
        var c = Masaustu(f, xff);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        c.DefaultRequestHeaders.Add("X-Kasa-Istemci-Surumu", YonetimEndpoints.MinimumIstemci);
        return c;
    }

    private static string? CihazCerezi(HttpResponseMessage yanit, string ad = EditorCerezi)
        => yanit.Headers.TryGetValues("Set-Cookie", out var cerezler) ? cerezler.FirstOrDefault(c => c.StartsWith(ad + "=", StringComparison.Ordinal)) : null;

    private static bool CihazCereziVar(HttpResponseMessage yanit)
        => yanit.Headers.TryGetValues("Set-Cookie", out var cerezler) && cerezler.Any(c => c.StartsWith("__Host-kasa_cihaz", StringComparison.Ordinal));

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
        // Ömür oturumunkinden (30 gün) uzundur: fabrikada 180 gün.
        Assert.Contains("max-age=15552000", nitelikler);
        Assert.DoesNotContain(nitelikler, n => n.StartsWith("domain=", StringComparison.Ordinal));
        Assert.Matches(@"^__Host-kasa_cihaz_editor=c1\.[0-9]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$", cerez.Split(';')[0]);
    }

    [Fact]
    public async Task Masaustune_belirtec_govdede_doner_cerez_verilmez()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var masaustu = Masaustu(f, "198.51.100.11");
        var yanit = await Giris(masaustu, "editor", "kasa123");
        Assert.StartsWith("c1.", await GovdedekiBelirtec(yanit));
        Assert.False(CihazCereziVar(yanit));
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
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(cihaz, "editor", "yanlis")).StatusCode);
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
        Assert.Equal(HttpStatusCode.OK, (await tarayici.SendAsync(istek, TestContext.Current.CancellationToken)).StatusCode);
        await Reddedildi(await Giris(Masaustu(sonra, "198.51.100.52"), "editor", "kasa123"));
    }

    [Fact]
    public async Task Oturum_suresi_dolduktan_sonra_editor_kendi_cihazindan_girer()
    {
        // Yeniden girişin en sık nedeni oturum sonudur (JWT ve kasa_auth çerezi 30 gün): o anda belirteç hâlâ geçerli olmalı.
        var saat = new ElleSaat(Baslangic);
        await using var f = new VekilFabrikasi(Ayar(), saat: saat);
        var (_, masaustuBelirteci) = await MasaustuGirisi(f, "198.51.100.140", "editor", "kasa123");
        using var tarayici = Tarayici(f, "198.51.100.141");
        Assert.NotNull(CihazCerezi(await Giris(tarayici, "editor", "kasa123")));

        saat.Simdi = Baslangic.AddDays(30).AddMinutes(5);
        await HedefiKilitle(f, "editor", 140, "kasa123");

        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.140", masaustuBelirteci), "editor", "kasa123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Giris(tarayici, "editor", "kasa123")).StatusCode);
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.142"), "editor", "kasa123"));
    }

    [Fact]
    public async Task Omur_dolunca_belirtec_muaf_tutmaz()
    {
        var saat = new ElleSaat(Baslangic);
        await using var f = new VekilFabrikasi(Ayar(), saat: saat);
        var belirtec = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.100"), "editor", "kasa123"));

        saat.Simdi = Baslangic.AddDays(179);
        await HedefiKilitle(f, "editor", 100, "kasa123");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.100", belirtec), "editor", "kasa123")).StatusCode);

        // 179. gündeki giriş yeni belirteç verdi; eski belirteç 180. günün sonunda düşer.
        saat.Simdi = Baslangic.AddDays(181);
        await HedefiKilitle(f, "editor", 150, "kasa123");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.100", belirtec), "editor", "kasa123"));
    }

    [Theory]
    [InlineData("30")]   // oturumla aynı: oturum dolduğu anda belirteç de dolar
    [InlineData("7")]
    [InlineData("366")]
    public async Task Belirtec_omru_oturumdan_uzun_ve_bir_yili_asmamali(string gun)
    {
        await using var f = new VekilFabrikasi(Ayar(("Kasa:HizSiniri:TanidikCihazGun", gun)));
        var hata = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Kasa:HizSiniri:TanidikCihazGun", hata.ToString());
    }

    [Theory]
    [InlineData("0")]    // kapalı
    [InlineData("31")]
    [InlineData("365")]
    public async Task Gecerli_belirtec_omurleri_baslangici_durdurmaz(string gun)
    {
        await using var f = new VekilFabrikasi(Ayar(("Kasa:HizSiniri:TanidikCihazGun", gun)));
        using var c = Masaustu(f, "198.51.100.143");
        Assert.Equal(HttpStatusCode.OK, (await Giris(c, "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Oturum_dogrulamasi_masaustune_govdede_yeni_belirtec_verir_belirtecsiz_acik_oturum_da_tanidik_olur()
    {
        await using var f = new VekilFabrikasi(Ayar());
        // Belirteç saklanmamış açık oturum (ör. belirteç özelliğinden önce açılmış): yalnız JWT var.
        var (jwt, girisBelirteci) = await MasaustuGirisi(f, "198.51.100.150", "editor", "kasa123");
        using var oturum = Oturumlu(f, "198.51.100.150", jwt);
        var me = await oturum.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var govde = await me.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        var yenilenen = govde.GetProperty("cihaz").GetString();

        Assert.Equal("editor", govde.GetProperty("rol").GetString());
        Assert.StartsWith("c1.", yenilenen);
        Assert.NotEqual(girisBelirteci, yenilenen);
        Assert.False(CihazCereziVar(me));

        await HedefiKilitle(f, "editor", 1, "kasa123");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.150", yenilenen), "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Oturum_dogrulamasi_tarayiciya_yalniz_cerez_yazar()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var tarayici = Tarayici(f, "198.51.100.151");
        var giris = CihazCerezi(await Giris(tarayici, "editor", "kasa123"))!.Split(';')[0];

        var me = await tarayici.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Null(await GovdedekiBelirtec(me));
        Assert.False(me.Headers.Contains(Baslik));
        var yeni = CihazCerezi(me);
        Assert.NotNull(yeni);
        Assert.NotEqual(giris, yeni!.Split(';')[0]);
        Assert.Contains("httponly", yeni.ToLowerInvariant());

        await HedefiKilitle(f, "editor", 1, "kasa123");
        Assert.Equal(HttpStatusCode.OK, (await Giris(tarayici, "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Kullanilan_cihazin_belirteci_oturum_dogrulamasiyla_ileri_kayar()
    {
        var saat = new ElleSaat(Baslangic);
        await using var f = new VekilFabrikasi(Ayar(), saat: saat);
        var (jwt, girisBelirteci) = await MasaustuGirisi(f, "198.51.100.152", "editor", "kasa123");

        // 100. günde uygulama açılışı (/me) belirteci yeniler; girişten 200 gün sonra giriş belirteci düşmüş, yenisi geçerlidir.
        saat.Simdi = Baslangic.AddDays(100);
        using var oturum = Oturumlu(f, "198.51.100.152", jwt);
        var yenilenen = await GovdedekiBelirtec(await oturum.GetAsync("/api/auth/me", TestContext.Current.CancellationToken));

        saat.Simdi = Baslangic.AddDays(200);
        await HedefiKilitle(f, "editor", 1, "kasa123");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.152", girisBelirteci), "editor", "kasa123"));
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.152", yenilenen), "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Oturum_dogrulamasi_alici_ve_izleyici_icin_kendi_hedefinin_belirtecini_verir()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using (var editor = await f.EditorClientAsync())
        {
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
            (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı", "alici-sifre-1"), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        }
        var (aliciJwt, _) = await MasaustuGirisi(f, "198.51.100.153", "alici-1", "alici-sifre-1");
        var (izleyiciJwt, _) = await MasaustuGirisi(f, "198.51.100.154", null, "izleyici-sifresi");
        using var aliciOturumu = Oturumlu(f, "198.51.100.153", aliciJwt);
        var aliciBelirteci = await GovdedekiBelirtec(await aliciOturumu.GetAsync("/api/auth/me", TestContext.Current.CancellationToken));
        using var izleyiciOturumu = Oturumlu(f, "198.51.100.154", izleyiciJwt);
        var izleyiciBelirteci = await GovdedekiBelirtec(await izleyiciOturumu.GetAsync("/api/auth/me", TestContext.Current.CancellationToken));

        // Editör dışı ortak bütçe kilitlenir: iki belirteç de kendi hedefinde muaf tutar, diğerinde tutmaz.
        await HedefiKilitle(f, "yok-1", 1, "yanlis-ama-onemsiz");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.153", aliciBelirteci), "alici-1", "alici-sifre-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.154", izleyiciBelirteci), null, "izleyici-sifresi")).StatusCode);
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.153", aliciBelirteci), null, "izleyici-sifresi"));
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.154", izleyiciBelirteci), "alici-1", "alici-sifre-1"));
    }

    [Fact]
    public async Task Ayni_tarayicida_editor_ve_izleyici_belirtecleri_birbirini_ezmez()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using (var editor = await f.EditorClientAsync())
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var tarayici = Tarayici(f, "198.51.100.155");
        Assert.NotNull(CihazCerezi(await Giris(tarayici, "editor", "kasa123"), EditorCerezi));
        // Aynı tarayıcıda sonradan izleyici girer: editörün çerezi yerinde kalır.
        Assert.NotNull(CihazCerezi(await Giris(tarayici, null, "izleyici-sifresi"), IzleyiciCerezi));

        await HedefiKilitle(f, "editor", 1, "kasa123");
        await HedefiKilitle(f, null, 10, "izleyici-sifresi");
        Assert.Equal(HttpStatusCode.OK, (await Giris(tarayici, "editor", "kasa123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Giris(tarayici, null, "izleyici-sifresi")).StatusCode);
    }

    [Fact]
    public async Task Masaustu_sakli_belirteclerini_tek_baslikta_gonderir_denenen_hedefinki_muaf_tutar()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using (var editor = await f.EditorClientAsync())
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var (_, editorBelirteci) = await MasaustuGirisi(f, "198.51.100.156", "editor", "kasa123");
        var (_, izleyiciBelirteci) = await MasaustuGirisi(f, "198.51.100.156", null, "izleyici-sifresi");

        await HedefiKilitle(f, "editor", 1, "kasa123");
        await HedefiKilitle(f, null, 10, "izleyici-sifresi");
        var ikisi = $"{izleyiciBelirteci}, {editorBelirteci}";
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.156", ikisi), "editor", "kasa123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.156", ikisi), null, "izleyici-sifresi")).StatusCode);
        // Başlıkta en çok üç aday değerlendirilir: dördüncü sıradaki geçerli belirteç yok sayılır.
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.156", $"c1.1.a.b,c1.2.a.b,c1.3.a.b,{editorBelirteci}"), "editor", "kasa123"));
    }

    [Fact]
    public async Task Editor_sifresi_degisince_belirtec_gecersizlesir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new VekilFabrikasi(Ayar());
        using var cihaz = Masaustu(f, "198.51.100.60");
        var govde = await (await Giris(cihaz, "editor", "kasa123")).Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var belirtec = govde.GetProperty("cihaz").GetString();
        // Şifreyi başka bir cihaz değiştirir: bu cihazın belirteci eski damgayla kalır.
        using var baska = Oturumlu(f, "198.51.100.61", govde.GetProperty("token").GetString()!);
        (await baska.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = "yepyeni-editor-sifresi" }, cancellationToken: ct)).EnsureSuccessStatusCode();

        await HedefiKilitle(f, "editor", 40, "yepyeni-editor-sifresi");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.60", belirtec), "editor", "yepyeni-editor-sifresi"));
    }

    [Fact]
    public async Task Sifresini_degistiren_cihaz_yeni_damgayla_tanidik_kalir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new VekilFabrikasi(Ayar());
        // Masaüstü: gövdesiz 204 yanıtında X-Kasa-Cihaz yanıt başlığı.
        var (jwt, eskiBelirtec) = await MasaustuGirisi(f, "198.51.100.62", "editor", "kasa123");
        using var masaustu = Oturumlu(f, "198.51.100.62", jwt);
        var degisim = await masaustu.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = "yepyeni-editor-sifresi" }, cancellationToken: ct);
        Assert.Equal(HttpStatusCode.NoContent, degisim.StatusCode);
        Assert.False(CihazCereziVar(degisim));
        var yeniBelirtec = Assert.Single(degisim.Headers.GetValues(Baslik));

        await HedefiKilitle(f, "editor", 1, "yepyeni-editor-sifresi");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.62", yeniBelirtec), "editor", "yepyeni-editor-sifresi")).StatusCode);
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.62", eskiBelirtec), "editor", "yepyeni-editor-sifresi"));
    }

    [Fact]
    public async Task Tarayicida_sifre_degisikligi_cihaz_cerezini_yeni_damgayla_yeniler()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new VekilFabrikasi(Ayar());
        using var tarayici = Tarayici(f, "198.51.100.63");
        (await Giris(tarayici, "editor", "kasa123")).EnsureSuccessStatusCode();
        var degisim = await tarayici.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = "yepyeni-editor-sifresi" }, cancellationToken: ct);
        Assert.Equal(HttpStatusCode.NoContent, degisim.StatusCode);
        Assert.NotNull(CihazCerezi(degisim));
        Assert.False(degisim.Headers.Contains(Baslik));

        await HedefiKilitle(f, "editor", 1, "yepyeni-editor-sifresi");
        Assert.Equal(HttpStatusCode.OK, (await Giris(tarayici, "editor", "yepyeni-editor-sifresi")).StatusCode);
    }

    [Fact]
    public async Task Kurtarma_koduyla_sifre_yenileyen_cihaz_tanidik_olur()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new VekilFabrikasi(Ayar());
        var (jwt, _) = await MasaustuGirisi(f, "198.51.100.64", "editor", "kasa123");
        using var oturum = Oturumlu(f, "198.51.100.64", jwt);
        var kodYaniti = await oturum.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "kasa123" }, cancellationToken: ct);
        kodYaniti.EnsureSuccessStatusCode();
        var kod = (await kodYaniti.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("kod").GetString();

        // Şifresini unutan editör başka bir cihazdan (belirteçsiz, oturumsuz) kurtarır.
        using var yeniCihaz = Masaustu(f, "198.51.100.65");
        var kurtar = await yeniCihaz.PostAsJsonAsync("/api/auth/kurtar", new { kullanici = "editor", kod, yeniSifre = "kurtarilan-editor-sifresi" }, cancellationToken: ct);
        Assert.Equal(HttpStatusCode.NoContent, kurtar.StatusCode);
        var belirtec = Assert.Single(kurtar.Headers.GetValues(Baslik));

        await HedefiKilitle(f, "editor", 60, "kurtarilan-editor-sifresi");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.65", belirtec), "editor", "kurtarilan-editor-sifresi")).StatusCode);
    }

    [Fact]
    public async Task Alici_oturum_surumu_degisince_belirtec_gecersizlesir()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var editor = await f.EditorClientAsync();
        var olustur = await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı", "alici-sifre-1"), cancellationToken: TestContext.Current.CancellationToken);
        olustur.EnsureSuccessStatusCode();
        var id = (await olustur.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetInt32();
        var belirtec = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.70"), "alici-1", "alici-sifre-1"));

        await HedefiKilitle(f, "alici-1", 70, "alici-sifre-1");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.70", belirtec), "alici-1", "alici-sifre-1")).StatusCode);

        // Pasife alıp açmak oturum sürümünü artırır (şifre aynı kalsa da): eski belirteç düşer.
        (await editor.PutAsJsonAsync($"/api/alicilar/{id}", new AliciYaz("alici-1", "Alıcı", null, Aktif: false), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync($"/api/alicilar/{id}", new AliciYaz("alici-1", "Alıcı", null, Aktif: true), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.70", belirtec), "alici-1", "alici-sifre-1"));
    }

    [Fact]
    public async Task Izleyici_sifresi_degisince_belirtec_gecersizlesir()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using var editor = await f.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var belirtec = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.80"), null, "izleyici-sifresi"));

        await HedefiKilitle(f, null, 80, "izleyici-sifresi");
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.80", belirtec), null, "izleyici-sifresi")).StatusCode);

        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "yeni-izleyici-sifresi" }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.80", belirtec), null, "yeni-izleyici-sifresi"));
    }

    [Fact]
    public async Task Belirtec_verildigi_hedefe_baglidir()
    {
        await using var f = new VekilFabrikasi(Ayar());
        using (var editor = await f.EditorClientAsync())
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var editorBelirteci = await GovdedekiBelirtec(await Giris(Masaustu(f, "198.51.100.90"), "editor", "kasa123"));

        await HedefiKilitle(f, null, 90, "izleyici-sifresi");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.90", editorBelirteci), null, "izleyici-sifresi"));
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.90", editorBelirteci), "yok-1", "izleyici-sifresi"));
        Assert.Equal(HttpStatusCode.OK, (await Giris(Masaustu(f, "198.51.100.90", editorBelirteci), "editor", "kasa123")).StatusCode);
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
        var cikis = await tarayici.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, cikis.StatusCode);
        Assert.False(CihazCereziVar(cikis));
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
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(!govde.TryGetProperty("cihaz", out var cihaz) || cihaz.ValueKind == JsonValueKind.Null);
        Assert.False(CihazCereziVar(await Giris(tarayici, "editor", "kasa123")));
        Assert.False(CihazCereziVar(await tarayici.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)));
        using var oturum = Oturumlu(f, "198.51.100.130", govde.GetProperty("token").GetString()!);
        Assert.Null(await GovdedekiBelirtec(await oturum.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)));
        await HedefiKilitle(f, "editor", 130, "kasa123");
        await Reddedildi(await Giris(Masaustu(f, "198.51.100.130", belirtec), "editor", "kasa123"));
    }
}
