using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>
    /// Sınırları küçültülmüş, bağlantı adresini başlıktan alan fabrika. Geliştirme ortamı (varsayılan test
    /// fabrikası) sınırları gevşettiği için bu senaryonun bütün sınırları burada açıkça verilir. Eşzamanlı istek
    /// sınayan testler dosya veritabanı verir: ortak in-memory bağlantı aynı anda birden çok istekte kullanılamaz.
    /// </summary>
    internal sealed class VekilFabrikasi(Dictionary<string, string?>? ek = null, UyariToplayici? loglar = null, TimeProvider? saat = null,
        string? dosyaVeritabani = null) : KasaWebFactory
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
                ["Kasa:HizSiniri:GirisAgIzni"] = "100",
                ["Kasa:HizSiniri:PencereDakika"] = "5",
                ["Kasa:HizSiniri:HedefBasarisizIzni"] = "100",
                ["Kasa:HizSiniri:AgBasarisizIzni"] = "50",
                ["Kasa:HizSiniri:HedefPencereDakika"] = "15",
                ["Kasa:HizSiniri:TanidikCihazGun"] = "30",
                ["Kasa:HizSiniri:SifreDogrulamaEszamanli"] = "2",
                ["Kasa:HizSiniri:SifreDogrulamaKuyrugu"] = "60",
            };
            foreach (var (k, v) in ek ?? new()) ayarlar[k] = v;
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(ayarlar));
            builder.ConfigureServices(s => s.AddSingleton<IStartupFilter, BaglantiAdresi>());
            if (saat is not null) builder.ConfigureTestServices(s => s.AddSingleton(saat));
            if (dosyaVeritabani is not null)
                builder.ConfigureServices(s =>
                {
                    s.RemoveAll<DbContextOptions<KasaDbContext>>();
                    s.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                    s.AddDbContext<KasaDbContext>(o => o.UseSqlite($"Data Source={dosyaVeritabani};Pooling=False"));
                });
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

    internal static HttpClient Istemci(KasaWebFactory f, string? xff, string? baglanti = null)
    {
        var c = f.CreateClient();
        if (xff is not null) c.DefaultRequestHeaders.Add("X-Forwarded-For", xff);
        if (baglanti is not null) c.DefaultRequestHeaders.Add(Vekil, baglanti);
        return c;
    }

    internal static Task<HttpResponseMessage> Giris(HttpClient c, string? kullanici, string sifre)
        => c.PostAsJsonAsync("/api/auth/login", new { kullanici, sifre });

    /// <summary>Şifre denenmeden verilen 429: Retry-After ve Türkçe ileti; iletiyi döner.</summary>
    internal static async Task<string> Reddedildi(HttpResponseMessage yanit)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, yanit.StatusCode);
        Assert.True(yanit.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After başlığı saniye olarak gelmeli.");
        var hata = (await yanit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString()!;
        Assert.StartsWith("Çok fazla deneme yapıldı.", hata);
        return hata;
    }

    /// <summary>Geçici dosya veritabanıyla fabrika kurup senaryoyu koşar; dosyalar sonunda silinir.</summary>
    internal static async Task DosyaVeritabaniyla(Dictionary<string, string?> ayarlar, Func<VekilFabrikasi, Task> senaryo)
    {
        var yol = Path.Combine(Path.GetTempPath(), $"kasa-giris-{Guid.NewGuid():N}.db");
        try
        {
            await using var f = new VekilFabrikasi(ayarlar, dosyaVeritabani: yol);
            await senaryo(f);
        }
        finally
        {
            foreach (var ek in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(yol + ek);
        }
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
            using var c = Istemci(f, "198.51.100.60", baglanti: "10.20.0.1");
            await Giris(c, "editor", "yanlis");
        }
        using var guvenilir = Istemci(f, "198.51.100.61");
        await Giris(guvenilir, "editor", "yanlis");
        using var basliksiz = Istemci(f, null, baglanti: "203.0.113.9");
        await Giris(basliksiz, "editor", "yanlis");

        var uyari = Assert.Single(loglar.Uyarilar, u => u.Contains("Kasa:GuvenilirVekiller"));
        Assert.Contains("10.20.0.1", uyari);
    }

    [Fact]
    public async Task Guvenilmeyen_vekil_uyarisi_ayarlarda_editore_gosterilir()
    {
        await using var f = new VekilFabrikasi();
        using var editor = await f.EditorClientAsync();
        Assert.Equal(JsonValueKind.Null, (await Ayarlar(editor)).GetProperty("vekilUyarisi").ValueKind);

        // Güvenilen vekilden gelen başlık ya da başlıksız doğrudan bağlantı uyarı üretmez.
        using (var guvenilir = Istemci(f, "198.51.100.62", baglanti: "172.18.0.1")) await Giris(guvenilir, "editor", "yanlis");
        using (var basliksiz = Istemci(f, null, baglanti: "203.0.113.9")) await Giris(basliksiz, "editor", "yanlis");
        Assert.Equal(JsonValueKind.Null, (await Ayarlar(editor)).GetProperty("vekilUyarisi").ValueKind);

        using (var c = Istemci(f, "198.51.100.63", baglanti: "10.20.0.1")) await Giris(c, "editor", "yanlis");
        var uyari = (await Ayarlar(editor)).GetProperty("vekilUyarisi").GetString();
        Assert.Contains("10.20.0.1", uyari);
        Assert.Contains("Kasa:GuvenilirVekiller", uyari);

        // İşletim uyarısı yalnız editöre gösterilir; izleyici ayarları okuyabilse de görmez.
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" })).EnsureSuccessStatusCode();
        using var izleyici = Istemci(f, "198.51.100.64");
        (await Giris(izleyici, null, "izleyici-sifresi")).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await Ayarlar(izleyici)).GetProperty("vekilUyarisi").ValueKind);
    }

    private static async Task<JsonElement> Ayarlar(HttpClient c) => await c.GetFromJsonAsync<JsonElement>("/api/ayarlar");

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
    public void Guvenilir_vekiller_varsayilani_loopback_ve_docker_varsayilan_havuzlaridir()
    {
        var aglar = HizSinirlari.GuvenilirAglar(null);
        // Docker'ın varsayılan adres havuzları: 172.17–172.31/16, ardından 192.168.0.0/16 içinden /20'lik ağlar.
        foreach (var ip in new[] { "127.0.0.1", "::1", "172.17.0.1", "172.18.0.1", "172.31.255.254", "192.168.0.1", "192.168.16.1", "192.168.240.1" })
            Assert.Contains(aglar, a => a.Contains(IPAddress.Parse(ip)));
        foreach (var ip in new[] { "203.0.113.5", "10.0.0.1", "172.32.0.1", "192.169.0.1", "fd00::1" })
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
        Assert.Equal(4, o.KnownIPNetworks.Count);
    }

    [Fact]
    public async Task Uretim_ayari_varsayilan_sinirlari_ve_vekilleri_kullanir_gelistirme_ortami_sinirlari_gevsetir()
    {
        await using var f = new KasaWebFactory();
        var gelistirme = f.Services.GetRequiredService<IOptions<HizSiniriAyarlari>>().Value;
        foreach (var izin in new[] { gelistirme.GuvenlikIzni, gelistirme.GirisIpIzni, gelistirme.GirisKullaniciIzni, gelistirme.GirisAgIzni, gelistirme.HedefBasarisizIzni, gelistirme.AgBasarisizIzni, gelistirme.YedekIzni })
            Assert.True(izin >= 10_000, "Geliştirme ortamında (test fabrikası) sınırlar gevşek olmalı.");

        // Üretim appsettings.json sınıf varsayılanlarını birebir taşır; gevşeme yalnız Development dosyasında.
        var kok = f.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var uretim = new ConfigurationBuilder().AddJsonFile(Path.Combine(kok, "appsettings.json")).Build();
        Assert.Equivalent(new HizSiniriAyarlari(), uretim.GetSection("Kasa:HizSiniri").Get<HizSiniriAyarlari>(), strict: true);
        // Varsayılanlar ve gevşek geliştirme değerleri de başlangıç tutarlılık kurallarını karşılar.
        Assert.Empty(new HizSiniriAyarlari().Hatalar());
        Assert.Empty(gelistirme.Hatalar());
        Assert.Equal(HizSinirlari.VarsayilanGuvenilirVekiller, uretim["Kasa:GuvenilirVekiller"]);
    }

    [Fact]
    public async Task Varsayilan_test_fabrikasinda_art_arda_girisler_hiz_sinirina_takilmaz()
    {
        await using var f = new KasaWebFactory();
        for (var i = 0; i < 15; i++)
        {
            using var c = await f.EditorClientAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "editor", "yanlis")).StatusCode);
        }
    }

    // --- IP'den bağımsız hedef bütçesi (dağıtık kaba kuvvet), tanınan ağ muafiyeti, doğrulama eşzamanlılığı ---

    private const string HedefIzni = "Kasa:HizSiniri:HedefBasarisizIzni";
    private const string AgIzni = "Kasa:HizSiniri:AgBasarisizIzni";

    [Fact]
    public async Task Dagitik_kaba_kuvvet_hedef_basina_ipden_bagimsiz_basarisiz_siniri_asamaz()
    {
        await using var f = new VekilFabrikasi(new() { [HedefIzni] = "4", [AgIzni] = "3" });
        using (var editor = await f.EditorClientAsync())
            (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı", "alici-sifre-1"))).EnsureSuccessStatusCode();
        // Her IP kendi sınırının çok altında kalır; toplam başarısızlık hedefin bütçesini doldurur.
        for (var i = 0; i < 4; i++)
        {
            using var c = Istemci(f, $"198.51.100.{70 + i}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "editor", "yanlis")).StatusCode);
        }
        // Yeni IP'den doğru şifre de denenmeden reddedilir: dağıtık deneme IP sayısıyla çoğalamaz.
        using var yeni = Istemci(f, "198.51.100.80");
        await Reddedildi(await Giris(yeni, "editor", "kasa123"));
        // Başka hedefler (alıcı, izleyici) etkilenmez.
        Assert.Equal(HttpStatusCode.OK, (await Giris(yeni, "alici-1", "alici-sifre-1")).StatusCode);
    }

    [Fact]
    public async Task Izleyici_sifresi_kullanici_adi_ve_ip_dondurulerek_kuresel_sinirdan_kacirilamaz()
    {
        await using var f = new VekilFabrikasi(new() { [HedefIzni] = "4", [AgIzni] = "3" });
        using (var editor = await f.EditorClientAsync())
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" })).EnsureSuccessStatusCode();
        for (var i = 0; i < 4; i++)
        {
            using var c = Istemci(f, $"198.51.100.{90 + i}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, $"rastgele-{i}", "yanlis")).StatusCode);
        }
        using var yeni = Istemci(f, "198.51.100.99");
        await Reddedildi(await Giris(yeni, "baska-ad", "izleyici-sifresi"));
        await Reddedildi(await Giris(yeni, null, "izleyici-sifresi"));
        Assert.Equal(HttpStatusCode.OK, (await Giris(yeni, "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Tek_ag_hedef_butcesini_tek_basina_tuketip_hedefi_herkese_kilitleyemez()
    {
        await using var f = new VekilFabrikasi(new() { [HedefIzni] = "5", [AgIzni] = "2", ["Kasa:HizSiniri:GirisKullaniciIzni"] = "50", ["Kasa:HizSiniri:GirisIpIzni"] = "50" });
        using (var editor = await f.EditorClientAsync())
        {
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" })).EnsureSuccessStatusCode();
            (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı", "alici-sifre-1"))).EnsureSuccessStatusCode();
        }
        using var saldirgan = Istemci(f, "198.51.100.180");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(saldirgan, "editor", "yanlis")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(saldirgan, "ad-0", "yanlis")).StatusCode);
        // Ağ bütçesi bütün hedeflerde ortak: kullanıcı adı döndürmek ağı sınırdan kaçırmaz, yanıtlar da hangi
        // adın alıcı olduğunu ele vermez.
        foreach (var (ad, sifre) in new[] { ("editor", "kasa123"), ("ad-1", "izleyici-sifresi"), ("alici-1", "alici-sifre-1"), ("yok-1", "x") })
            await Reddedildi(await Giris(saldirgan, ad, sifre));
        // Aynı IPv6 /48 bloğunun başka /64'ü aynı ağ sayılır.
        using var blokA = Istemci(f, "2001:db8:7:1::1");
        using var blokB = Istemci(f, "2001:db8:7:2::1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(blokA, "editor", "yanlis")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(blokB, "editor", "yanlis")).StatusCode);
        await Reddedildi(await Giris(blokB, "editor", "kasa123"));
        // Hedef bütçesi (5) dolmadı: başka ağlardaki kullanıcılar girer.
        using var editorAgi = Istemci(f, "198.51.100.181");
        Assert.Equal(HttpStatusCode.OK, (await Giris(editorAgi, "editor", "kasa123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Giris(editorAgi, "izleyici", "izleyici-sifresi")).StatusCode);
    }

    [Fact]
    public async Task Ortak_butce_dolunca_alici_adlarini_ele_vermez_alicinin_tanidik_cihazi_girer()
    {
        await using var f = new VekilFabrikasi(new() { [HedefIzni] = "3", [AgIzni] = "2" });
        using (var editor = await f.EditorClientAsync())
        {
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" })).EnsureSuccessStatusCode();
            (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı", "alici-sifre-1"))).EnsureSuccessStatusCode();
        }
        using var aliciAgi = Istemci(f, "198.51.100.190");
        var ilk = await Giris(aliciAgi, "alici-1", "alici-sifre-1");
        Assert.Equal(HttpStatusCode.OK, ilk.StatusCode);
        var belirtec = (await ilk.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cihaz").GetString();
        for (var i = 0; i < 3; i++)
        {
            using var c = Istemci(f, $"198.51.100.{191 + i}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, $"yok-{i}", "yanlis")).StatusCode);
        }
        // Ortak bütçe doldu: tanıdık cihazı olmayan istemciden alıcı adı da, olmayan ad da aynı yanıtı alır.
        using var yabanci = Istemci(f, "198.51.100.199");
        await Reddedildi(await Giris(yabanci, "alici-1", "yanlis"));
        await Reddedildi(await Giris(yabanci, "yok-9", "yanlis"));
        // Alıcının tanıdık cihazı ve editör etkilenmez; muafiyet ağa değil cihaza bağlıdır.
        using var aliciCihazi = Istemci(f, "198.51.100.190");
        aliciCihazi.DefaultRequestHeaders.Add("X-Kasa-Cihaz", belirtec);
        Assert.Equal(HttpStatusCode.OK, (await Giris(aliciCihazi, "alici-1", "alici-sifre-1")).StatusCode);
        await Reddedildi(await Giris(aliciAgi, "alici-1", "alici-sifre-1"));
        Assert.Equal(HttpStatusCode.OK, (await Giris(yabanci, "editor", "kasa123")).StatusCode);
    }

    [Fact]
    public async Task Hedef_butcesi_dolunca_bir_kez_uyari_loglanir()
    {
        var loglar = new UyariToplayici();
        await using var f = new VekilFabrikasi(new() { [HedefIzni] = "2", [AgIzni] = "1" }, loglar: loglar);
        for (var i = 0; i < 5; i++)
        {
            using var c = Istemci(f, $"198.51.100.{160 + i}");
            await Giris(c, "editor", "yanlis");
        }
        var uyari = Assert.Single(loglar.Uyarilar, u => u.Contains("başarısız deneme sınırı"));
        Assert.Contains("editor", uyari);
    }

    [Fact]
    public async Task Ipv6_48_blogu_64_bolumlerine_yayilarak_giris_penceresini_cogaltamaz()
    {
        await using var f = new VekilFabrikasi(new() { ["Kasa:HizSiniri:GirisAgIzni"] = "4" });
        for (var i = 1; i <= 4; i++)
        {
            using var c = Istemci(f, $"2001:db8:5:{i}::1");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "editor", "yanlis")).StatusCode);
        }
        using var ayniBlok = Istemci(f, "2001:db8:5:ff::1");
        await Reddedildi(await Giris(ayniBlok, "editor", "kasa123"));
        using var baskaBlok = Istemci(f, "2001:db8:6::1");
        Assert.Equal(HttpStatusCode.OK, (await Giris(baskaBlok, "editor", "kasa123")).StatusCode);
        Assert.Equal("2001:db8:5::/48", HizSinirlari.AgAnahtari(IPAddress.Parse("2001:db8:5:ff::1")));
        Assert.Null(HizSinirlari.AgAnahtari(IPAddress.Parse("198.51.100.1")));
        Assert.Null(HizSinirlari.AgAnahtari(IPAddress.Parse("::ffff:198.51.100.1")));
    }

    [Fact]
    public async Task Sifre_dogrulamasi_kuyrukta_bekler_izin_bosalinca_tamamlanir()
    {
        await using var f = new VekilFabrikasi(new() { [AgIzni] = "1", [HedefIzni] = "2", ["Kasa:HizSiniri:SifreDogrulamaEszamanli"] = "1", ["Kasa:HizSiniri:SifreDogrulamaKuyrugu"] = "1" });
        using var c = Istemci(f, "198.51.100.171");
        var sinir = f.Services.GetRequiredService<GirisSiniri>();
        var tutulan = await sinir.DogrulamaIzniAsync(CancellationToken.None);
        var giris = Giris(c, "editor", "kasa123");
        using (var zaman = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            while (sinir.KuyruktakiDogrulama == 0) await Task.Delay(10, zaman.Token);
        Assert.False(giris.IsCompleted);
        tutulan.Dispose();
        Assert.Equal(HttpStatusCode.OK, (await giris).StatusCode);
    }

    // --- Atomik sayım: bütçe, şifre doğrulamasından önce ayrılır; denetim ile harcama arasında yarış yoktur ---

    /// <summary>Tek doğrulama izni testte tutulur: istekler bütçe denetimini geçip doğrulama kuyruğunda birikir.
    /// Bütün istekler ya yanıtlanmış ya da kuyruğa girmiş olunca izin bırakılır; 401 alan her istek PBKDF2'ye ulaşmıştır.</summary>
    private static async Task<HttpResponseMessage[]> EszamanliGirisler(VekilFabrikasi f, IReadOnlyList<(HttpClient Istemci, string Kullanici, string Sifre)> denemeler)
    {
        var sinir = f.Services.GetRequiredService<GirisSiniri>();
        var tutulan = await sinir.DogrulamaIzniAsync(CancellationToken.None);
        Assert.True(tutulan.IsAcquired);
        var istekler = denemeler.Select(d => Giris(d.Istemci, d.Kullanici, d.Sifre)).ToList();
        using (var zaman = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
            while (istekler.Count(t => t.IsCompleted) + sinir.KuyruktakiDogrulama < istekler.Count) await Task.Delay(10, zaman.Token);
        tutulan.Dispose();
        return await Task.WhenAll(istekler);
    }

    private static Dictionary<string, string?> PatlamaAyari(string agIzni, string hedefIzni) => new()
    {
        [AgIzni] = agIzni, [HedefIzni] = hedefIzni,
        ["Kasa:HizSiniri:SifreDogrulamaEszamanli"] = "1", ["Kasa:HizSiniri:SifreDogrulamaKuyrugu"] = "60",
        ["Kasa:HizSiniri:GirisKullaniciIzni"] = "1000", ["Kasa:HizSiniri:GirisIpIzni"] = "1000",
    };

    [Fact]
    public Task Tek_agdan_eszamanli_40_basarisiz_istekte_ag_butcesinden_fazlasi_sifre_dogrulamasina_ulasmaz()
        => DosyaVeritabaniyla(PatlamaAyari(agIzni: "5", hedefIzni: "100"), async f =>
    {
        using var c = Istemci(f, "198.51.100.200");
        var yanitlar = await EszamanliGirisler(f, Enumerable.Range(0, 40).Select(_ => (c, "editor", "yanlis")).ToList());

        Assert.Equal(5, yanitlar.Count(y => y.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Equal(35, yanitlar.Count(y => y.StatusCode == HttpStatusCode.TooManyRequests));
        foreach (var y in yanitlar.Where(y => y.StatusCode == HttpStatusCode.TooManyRequests)) await Reddedildi(y);
        // Doğru şifre de ağın bütçesi dolduğu için denenmez; başka ağ etkilenmez.
        await Reddedildi(await Giris(c, "editor", "kasa123"));
        using var baska = Istemci(f, "198.51.100.201");
        Assert.Equal(HttpStatusCode.OK, (await Giris(baska, "editor", "kasa123")).StatusCode);
    });

    [Fact]
    public Task Dagitik_eszamanli_patlamada_hedef_butcesinden_fazlasi_sifre_dogrulamasina_ulasmaz()
        => DosyaVeritabaniyla(PatlamaAyari(agIzni: "3", hedefIzni: "5"), async f =>
    {
        var istemciler = Enumerable.Range(1, 40).Select(i => Istemci(f, $"203.0.113.{i}")).ToList();
        try
        {
            var yanitlar = await EszamanliGirisler(f, istemciler.Select(c => (c, "editor", "yanlis")).ToList());
            Assert.Equal(5, yanitlar.Count(y => y.StatusCode == HttpStatusCode.Unauthorized));
            Assert.Equal(35, yanitlar.Count(y => y.StatusCode == HttpStatusCode.TooManyRequests));
        }
        finally { istemciler.ForEach(c => c.Dispose()); }
        // Hedef kilitli: yeni ağdan doğru şifre de denenmez.
        using var yeni = Istemci(f, "203.0.113.99");
        await Reddedildi(await Giris(yeni, "editor", "kasa123"));
    });

    [Fact]
    public async Task Basarili_giris_ayrilan_butceyi_iade_eder_ag_butcesini_harcamaz()
    {
        await using var f = new VekilFabrikasi(new() { [AgIzni] = "2", [HedefIzni] = "3", ["Kasa:HizSiniri:GirisKullaniciIzni"] = "50", ["Kasa:HizSiniri:GirisIpIzni"] = "50" });
        using var ofis = Istemci(f, "198.51.100.210");
        for (var i = 0; i < 6; i++) Assert.Equal(HttpStatusCode.OK, (await Giris(ofis, "editor", "kasa123")).StatusCode);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(ofis, "editor", "yanlis")).StatusCode);
        await Reddedildi(await Giris(ofis, "editor", "kasa123"));
    }

    [Fact]
    public async Task Dogrulama_kuyrugu_doluyken_reddedilen_giris_butce_harcamaz()
    {
        await using var f = new VekilFabrikasi(new() { [AgIzni] = "1", [HedefIzni] = "2", ["Kasa:HizSiniri:SifreDogrulamaEszamanli"] = "1", ["Kasa:HizSiniri:SifreDogrulamaKuyrugu"] = "1" });
        using var c = Istemci(f, "198.51.100.211");
        var sinir = f.Services.GetRequiredService<GirisSiniri>();
        using (var tutulan = await sinir.DogrulamaIzniAsync(CancellationToken.None))
        {
            var bekleyen = sinir.DogrulamaIzniAsync(CancellationToken.None);
            Assert.False(bekleyen.IsCompleted);
            var yanit = await Giris(c, "editor", "yanlis");
            Assert.Equal(HttpStatusCode.TooManyRequests, yanit.StatusCode);
            Assert.True(yanit.Headers.RetryAfter?.Delta > TimeSpan.Zero);
            Assert.Contains("yoğun", (await yanit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString());
            tutulan.Dispose();
            (await bekleyen).Dispose();
        }
        // Şifre denenmediği için ağın tek izni duruyor: bir yanlış deneme 401, sonrası 429.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, "editor", "yanlis")).StatusCode);
        await Reddedildi(await Giris(c, "editor", "kasa123"));
    }

    // --- Ad sayımı: editör dışındaki adlar (alıcılar ve izleyici şifresi) tek ortak başarısız deneme bütçesi ---

    [Theory]
    [InlineData("alici-1,alici-1,alici-1,alici-1")]
    [InlineData("yok-1,yok-2,yok-3,yok-4")]
    [InlineData("alici-1,yok-1,alici-1,")]
    public async Task Alici_adinin_var_olup_olmadigi_401_429_farkindan_anlasilamaz(string basarisizAdlar)
    {
        var saat = new ElleSaat(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        await using var f = new VekilFabrikasi(new() { [HedefIzni] = "4", [AgIzni] = "3", ["Kasa:HizSiniri:GirisKullaniciIzni"] = "50", ["Kasa:HizSiniri:GirisIpIzni"] = "50" }, saat: saat);
        using (var editor = await f.EditorClientAsync())
        {
            (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" })).EnsureSuccessStatusCode();
            (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı", "alici-sifre-1"))).EnsureSuccessStatusCode();
        }
        var adlar = basarisizAdlar.Split(',');
        for (var i = 0; i < adlar.Length; i++)
        {
            using var c = Istemci(f, $"198.51.100.{220 + i}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(c, adlar[i].Length == 0 ? null : adlar[i], "yanlis")).StatusCode);
        }

        // Başarısız denemeler hangi adda yapılmış olursa olsun, alıcı adı da olmayan ad da aynı yanıtı alır.
        using var yabanci = Istemci(f, "198.51.100.230");
        var metinler = new List<string>();
        foreach (var (ad, sifre) in new[] { ("alici-1", "alici-sifre-1"), ("alici-1", "yanlis"), ("yok-9", "yanlis"), ((string?)null, "izleyici-sifresi") })
            metinler.Add(await Reddedildi(await Giris(yabanci, ad, sifre)));
        Assert.Single(metinler.Distinct());
        Assert.Equal(HttpStatusCode.OK, (await Giris(yabanci, "editor", "kasa123")).StatusCode);

        // Pencere bitince bütün adlar birlikte açılır: pencere farkı da adı ele vermez.
        saat.Simdi = saat.Simdi.AddMinutes(16);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(yabanci, "alici-1", "yanlis")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Giris(yabanci, "yok-9", "yanlis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Giris(yabanci, "alici-1", "alici-sifre-1")).StatusCode);
    }

    /// <summary>Tanınan ağ süresini takvimden bağımsız sınamak için elle ilerletilen saat.</summary>
    internal sealed class ElleSaat(DateTimeOffset baslangic) : TimeProvider
    {
        public DateTimeOffset Simdi { get; set; } = baslangic;
        public override DateTimeOffset GetUtcNow() => Simdi;
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

    [Theory]
    [InlineData("30", "30", "2", "40")]   // ağ bütçesi hedef bütçesine eşit: tek ağ hedefi herkese kilitleyebilir
    [InlineData("50", "22", "2", "20")]   // ağ bütçesi doğrulama kapasitesine eşit: tek ağ kuyruğu tek başına doldurabilir
    public async Task Ag_butcesi_hedef_butcesinden_ve_dogrulama_kapasitesinden_kucuk_olmalidir(string hedef, string ag, string eszamanli, string kuyruk)
    {
        await using var f = new VekilFabrikasi(new()
        {
            [HedefIzni] = hedef, [AgIzni] = ag,
            ["Kasa:HizSiniri:SifreDogrulamaEszamanli"] = eszamanli, ["Kasa:HizSiniri:SifreDogrulamaKuyrugu"] = kuyruk,
        });
        var hata = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Kasa:HizSiniri:AgBasarisizIzni", hata.ToString());
    }

    [Fact]
    public async Task Hiz_siniri_ayarlari_sifirdan_buyuk_olmalidir()
    {
        await using var f = new VekilFabrikasi(new() { ["Kasa:HizSiniri:GirisIpIzni"] = "0" });
        var hata = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Kasa:HizSiniri", hata.ToString());
    }
}
