using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

/// <summary>
/// Operatörün editör şifresini zorla sıfırlaması (gap-geri-yukleme-durum-geri-sarma-10, <see cref="EditorSifreSifirlama"/>):
/// veritabanında editör şifresi varken ortamdaki Kasa:EditorSifre yok sayılır; Kasa:EditorSifreSifirla=true ile açılış şifreyi
/// ortamdaki değere sıfırlar, kurtarma kodunu ve bütün editör oturumlarını düşürür. Sıfırlama izi sayesinde bayrak açık
/// unutulsa da editörün sonradan arayüzden değiştirdiği şifre her açılışta ezilmez; ortam şifresi değişirse yeniden uygulanır.
/// Her açılış aynı bellek içi veritabanıyla yeni bir uygulamadır (<see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/>).
/// Geri yüklemenin kilitlediği girişin açılması <see cref="GeriYuklemeTests"/>'tedir.
/// </summary>
public class EditorSifirlamaTests
{
    private const string JwtAnahtari = "test-jwt-anahtari-en-az-32-bayt-olmali!!"; // KasaWebFactory ile aynı
    private const string Ortam1 = "operatorun-ortam-sifresi-1";
    private const string Ortam2 = "operatorun-ortam-sifresi-2";
    private const string P2 = "editorun-ikinci-sifresi";
    private const string P3 = "editorun-ucuncu-sifresi";

    /// <summary>Aynı veritabanıyla yeni bir açılış: ortamdaki editör şifresi, sıfırlama bayrağı ve güvenlik günlüğü (açık, verilen
    /// dosyada). Açılışın uyarı logları <paramref name="loglar"/>'a düşer.</summary>
    private static WebApplicationFactory<Program> Ac(KasaWebFactory f, string ortamSifresi, string? sifirla, string gunluk, UyariToplayici? loglar = null)
        => f.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kasa:EditorSifre"] = ortamSifresi,
                [EditorSifreSifirlama.Ayar] = sifirla,
                ["GuvenlikGunlugu:Etkin"] = "true",
                ["GuvenlikGunlugu:Yol"] = gunluk,
            }));
            if (loglar is not null)
                b.ConfigureLogging(l => l.AddProvider(loglar));
        });

    private static HttpClient Istemci(WebApplicationFactory<Program> f, string? jwt = null)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (jwt is not null)
        {
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            c.DefaultRequestHeaders.Add("X-Kasa-Istemci-Surumu", YonetimEndpoints.MinimumIstemci);
        }
        return c;
    }

    private static async Task<HttpStatusCode> Giris(WebApplicationFactory<Program> f, string sifre)
    {
        using var c = Istemci(f);
        using var yanit = await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre });
        return yanit.StatusCode;
    }

    private static async Task<string> Jwt(WebApplicationFactory<Program> f, string sifre)
    {
        using var c = Istemci(f);
        using var yanit = await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre });
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static async Task<HttpStatusCode> Oturum(WebApplicationFactory<Program> f, string jwt)
    {
        using var c = Istemci(f, jwt);
        using var yanit = await c.GetAsync("/api/auth/me");
        return yanit.StatusCode;
    }

    private static async Task SifreDegistir(WebApplicationFactory<Program> f, string mevcut, string yeni)
    {
        using var c = Istemci(f, await Jwt(f, mevcut));
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = mevcut, yeniSifre = yeni })).StatusCode);
    }

    private static List<JsonElement> SifirlamaOlaylari(WebApplicationFactory<Program> f)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().DenetimOlaylari.AsNoTracking()
            .Where(o => o.Tur == GuvenlikOlaylari.EditorSifresiSifirlandi).OrderBy(o => o.Id).AsEnumerable()
            .Select(o => JsonDocument.Parse(o.YeniJson!).RootElement.Clone()).ToList();
    }

    private static (string? SifreHash, string? KurtarmaHash, string? Iz) Durum(WebApplicationFactory<Program> f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var editor = db.EditorGuvenlik.AsNoTracking().SingleOrDefault(e => e.Id == 1);
        return (editor?.SifreHash, editor?.KurtarmaHash, db.SistemDurumu.AsNoTracking().Single(s => s.Id == 1).EditorSifirlamaIzi);
    }

    private static string GeciciGunluk() => Path.Combine(Path.GetTempPath(), "kasa-sifirla-" + Guid.NewGuid().ToString("N"), GuvenlikGunlugu.DosyaAdi);

    private static void GunluguSil(string gunluk)
    {
        try
        { Directory.Delete(Path.GetDirectoryName(gunluk)!, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    /// <summary>
    /// Editör şifresini P2'ye çevirmiş ve kurtarma kodu üretmiş; operatör ortam şifresini Ortam1 yapıp bayrakla başlatır: Ortam1 ile
    /// giriş açılır, P2, önceki oturum ve kurtarma kodu geçmez. Editör şifreyi P3'e çevirir; bayrak açık unutulup yeniden başlatılsa da
    /// P3 geçerli kalır (tek sıfırlama olayı, "bayrağı kaldırın" uyarısı). Ortam şifresi Ortam2'ye çevrilince sıfırlama yeniden
    /// uygulanır. Olay güvenlik günlüğüne ve 'Oturum ve güvenlik' izine yazılır; ne şifreler ne iz günlüğe düşer. İz, JWT
    /// anahtarıyla HMAC'tir (anahtarsız yedekten şifre denenemez).
    /// </summary>
    [Fact]
    public async Task Bayrak_ortam_sifresine_sifirlar_eski_oturum_ve_kurtarma_kodu_duser_ayni_ortam_sifresiyle_yeniden_uygulanmaz()
    {
        var gunluk = GeciciGunluk();
        try
        {
            await using var f = new KasaWebFactory();
            string eskiJwt, kod;
            await using (var once = Ac(f, Ortam1, null, gunluk))
            {
                // İlk kurulum: veritabanında şifre yok, ortam şifresi geçerli. Editör kendi şifresine geçer.
                await SifreDegistir(once, Ortam1, P2);
                eskiJwt = await Jwt(once, P2);
                using var editor = Istemci(once, eskiJwt);
                var yanit = await editor.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = P2 }, cancellationToken: TestContext.Current.CancellationToken);
                kod = (await yanit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("kod").GetString()!;
                // Bayrak yokken ortam şifresi yok sayılır.
                Assert.Equal(HttpStatusCode.Unauthorized, await Giris(once, Ortam1));
            }

            var loglar = new UyariToplayici();
            await using (var sifirla = Ac(f, Ortam1, "true", gunluk, loglar))
            {
                Assert.Equal(HttpStatusCode.OK, await Giris(sifirla, Ortam1));
                Assert.Equal(HttpStatusCode.Unauthorized, await Giris(sifirla, P2));
                Assert.Equal(HttpStatusCode.Unauthorized, await Oturum(sifirla, eskiJwt));
                using (var anonim = Istemci(sifirla))
                    Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.PostAsJsonAsync("/api/auth/kurtar",
                        new { kullanici = "editor", kod, yeniSifre = "kurtarmayla-yeni-sifre" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
                var (sifreHash, kurtarmaHash, iz) = Durum(sifirla);
                Assert.True(SifreHasher.Dogrula(Ortam1, sifreHash!));
                Assert.Null(kurtarmaHash);
                Assert.Equal(Iz(Ortam1), iz);
                var olay = Assert.Single(SifirlamaOlaylari(sifirla));
                Assert.Equal("editor", olay.GetProperty("kullanici").GetString());
                Assert.True(olay.GetProperty("kurtarmaKoduIptal").GetBoolean());
                Assert.False(olay.GetProperty("girisKilidiKaldirildi").GetBoolean());
                Assert.Contains(loglar.Uyarilar, u => u.StartsWith("Editör şifresi ortamdaki Kasa:EditorSifre değerine sıfırlandı", StringComparison.Ordinal));
                await SifreDegistir(sifirla, Ortam1, P3);
            }

            // Bayrak açık unutuldu, ortam şifresi aynı: editörün arayüzden belirlediği P3 ezilmez.
            loglar = new UyariToplayici();
            await using (var unutuldu = Ac(f, Ortam1, "true", gunluk, loglar))
            {
                Assert.Equal(HttpStatusCode.OK, await Giris(unutuldu, P3));
                Assert.Equal(HttpStatusCode.Unauthorized, await Giris(unutuldu, Ortam1));
                Assert.Single(SifirlamaOlaylari(unutuldu));
                Assert.Contains(loglar.Uyarilar, u => u.StartsWith("Kasa:EditorSifreSifirla hâlâ açık", StringComparison.Ordinal));
            }

            // Ortam şifresi değişti: sıfırlama yeniden uygulanır.
            await using (var yeni = Ac(f, Ortam2, "True", gunluk))
            {
                Assert.Equal(HttpStatusCode.OK, await Giris(yeni, Ortam2));
                Assert.Equal(HttpStatusCode.Unauthorized, await Giris(yeni, P3));
                Assert.Equal(2, SifirlamaOlaylari(yeni).Count);
                Assert.Equal(Iz(Ortam2), Durum(yeni).Iz);
            }

            // Bayrak kaldırıldı: hiçbir şey değişmez.
            await using (var kapali = Ac(f, Ortam2, "false", gunluk))
            {
                Assert.Equal(HttpStatusCode.OK, await Giris(kapali, Ortam2));
                Assert.Equal(2, SifirlamaOlaylari(kapali).Count);
            }

            var satirlar = File.ReadAllLines(gunluk);
            Assert.Equal(2, satirlar.Count(s => s.Contains($"\"tur\":\"{GuvenlikGunlugu.EditorSifresiSifirlandi}\"", StringComparison.Ordinal)));
            var metin = string.Join('\n', satirlar);
            foreach (var gizli in new[] { Ortam1, Ortam2, P2, P3, kod, Iz(Ortam1), Iz(Ortam2) })
                Assert.DoesNotContain(gizli, metin, StringComparison.OrdinalIgnoreCase);
        }
        finally { GunluguSil(gunluk); }
    }

    /// <summary>Bayrak kapalıyken (varsayılan) ortam şifresinin değiştirilmesi veritabanındaki şifreyi değiştirmez: giriş editörün
    /// belirlediği şifreyledir, iz yazılmaz.</summary>
    [Fact]
    public async Task Bayrak_kapaliyken_ortam_sifresi_degisse_de_editor_sifresi_degismez()
    {
        var gunluk = GeciciGunluk();
        try
        {
            await using var f = new KasaWebFactory();
            await using (var once = Ac(f, Ortam1, null, gunluk))
                await SifreDegistir(once, Ortam1, P2);
            await using var sonra = Ac(f, Ortam2, "", gunluk);
            Assert.Equal(HttpStatusCode.Unauthorized, await Giris(sonra, Ortam2));
            Assert.Equal(HttpStatusCode.OK, await Giris(sonra, P2));
            Assert.Null(Durum(sonra).Iz);
            Assert.Empty(SifirlamaOlaylari(sonra));
        }
        finally { GunluguSil(gunluk); }
    }

    /// <summary>Bayrak açık ama ortam şifresi 12–1024 karakter kuralına uymuyor ya da bayrak yanlış yazılmış: açılış durur, şifre
    /// değişmez (kısa bir ortam şifresi editör şifresi olamaz; yanlış yazılmış bayrak sessizce yok sayılmaz).</summary>
    [Theory]
    [InlineData("kisa-sifre", "true", "Kasa:EditorSifreSifirla için Kasa:EditorSifre 12–1024 karakter olmalıdır.")]
    [InlineData("            ", "true", "Kasa:EditorSifreSifirla için Kasa:EditorSifre 12–1024 karakter olmalıdır.")]
    [InlineData(Ortam1, "evet", "Kasa:EditorSifreSifirla 'true' ya da 'false' olmalıdır")]
    public async Task Gecersiz_sifirlama_ayari_acilisi_durdurur_sifre_degismez(string ortamSifresi, string sifirla, string ileti)
    {
        var gunluk = GeciciGunluk();
        try
        {
            await using var f = new KasaWebFactory();
            await using (var once = Ac(f, Ortam1, null, gunluk))
                await SifreDegistir(once, Ortam1, P2);
            await using (var hatali = Ac(f, ortamSifresi, sifirla, gunluk))
                Assert.Contains(ileti, Assert.ThrowsAny<Exception>(() => hatali.CreateClient()).ToString());
            await using var sonra = Ac(f, Ortam1, null, gunluk);
            Assert.Equal(HttpStatusCode.OK, await Giris(sonra, P2));
            Assert.Null(Durum(sonra).Iz);
        }
        finally { GunluguSil(gunluk); }
    }

    /// <summary>Kilitli giriş değeri (<see cref="EditorGuvenligi.GirisKilidi"/>) hiçbir şifreyle doğrulanmaz; ortam şifresine de düşülmez.</summary>
    [Fact]
    public void Kilitli_giris_hicbir_sifreyle_ve_ortam_sifresiyle_dogrulanmaz()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Kasa:EditorSifre"] = Ortam1 }).Build();
        var kayit = new EditorGuvenlikEntity { SifreHash = EditorGuvenligi.GirisKilidi };
        Assert.True(EditorGuvenligi.Kilitli(kayit));
        foreach (var sifre in new[] { Ortam1, EditorGuvenligi.GirisKilidi, "kilitli", "geri-yukleme", "" })
        {
            Assert.False(EditorGuvenligi.Dogrula(sifre, cfg, kayit));
            Assert.False(SifreHasher.Dogrula(sifre, EditorGuvenligi.GirisKilidi));
        }
        Assert.True(EditorGuvenligi.Dogrula(Ortam1, cfg, null));
    }

    /// <summary>Sözleşme: HMAC-SHA256(Kasa:JwtKey, "editor-sifirla\n" + şifre), büyük harf onaltılık.</summary>
    private static string Iz(string sifre)
        => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(JwtAnahtari), Encoding.UTF8.GetBytes("editor-sifirla\n" + sifre)));
}
