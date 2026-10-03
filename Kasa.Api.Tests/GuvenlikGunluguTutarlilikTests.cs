using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>Güvenlik günlüğü kullanılamaz olduğunda kimlik değişiklikleri ve geri yükleme işareti kalıcı olmamalıdır.</summary>
public class GuvenlikGunluguTutarlilikTests
{
    private const string EskiSifre = "kasa123";
    private const string YeniSifre = "yeni-editor-sifresi-123";
    private const string IzleyiciSifresi = "eski-izleyici-sifresi";

    private static string GeciciGunluk() => Path.Combine(Path.GetTempPath(), "kasa-gunluk-tutarlilik-" + Guid.NewGuid().ToString("N"), GuvenlikGunlugu.DosyaAdi);

    private static WebApplicationFactory<Program> Ac(KasaWebFactory temel, string yol) => temel.WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GuvenlikGunlugu:Etkin"] = "true",
            ["GuvenlikGunlugu:Yol"] = yol,
        })));

    private static async Task<HttpClient> Editor(WebApplicationFactory<Program> host)
    {
        var istemci = host.CreateClient();
        using var giris = await istemci.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = EskiSifre }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, giris.StatusCode);
        return istemci;
    }

    private static byte[] YazmayiBoz(string yol)
    {
        var onceki = File.ReadAllBytes(yol);
        File.Delete(yol);
        Directory.CreateDirectory(yol); // FileStream.Append aynı yolda artık yazamaz.
        return onceki;
    }

    private static void Onar(string yol, byte[] onceki)
    {
        Directory.Delete(yol);
        File.WriteAllBytes(yol, onceki);
    }

    private static void Temizle(string yol)
    {
        var mutlakYol = Path.GetFullPath(yol);
        var dizin = Path.GetDirectoryName(mutlakYol)!;
        var ad = Path.GetFileName(dizin);
        const string onEk = "kasa-gunluk-tutarlilik-";
        var geciciKok = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var karsilastirma = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.GetFileName(mutlakYol) != GuvenlikGunlugu.DosyaAdi
            || !string.Equals(Path.GetDirectoryName(dizin), geciciKok, karsilastirma)
            || !ad.StartsWith(onEk, StringComparison.Ordinal)
            || !Guid.TryParseExact(ad[onEk.Length..], "N", out _))
            throw new InvalidOperationException($"Test dışı dizin silinemez: {dizin}");
        if (Directory.Exists(dizin))
            Directory.Delete(dizin, recursive: true);
    }

    private static (string? Sifre, string? Kod, int Surum, string? Izleyici, int AliciSayisi, string AliciHash, bool AliciAktif, int OlaySayisi) Durum(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var editor = db.EditorGuvenlik.AsNoTracking().Single();
        var alici = db.Alicilar.AsNoTracking().Single();
        return (editor.SifreHash, editor.KurtarmaHash, editor.Surum,
            db.Ayarlar.AsNoTracking().Single().IzleyiciSifreHash, db.Alicilar.Count(), alici.SifreHash, alici.Aktif,
            db.DenetimOlaylari.Count());
    }

    [Fact]
    public async Task Gunluk_yazilamazsa_kimlik_uclari_veritabani_degisimlerini_geri_alir()
    {
        var ct = TestContext.Current.CancellationToken;
        var yol = GeciciGunluk();
        try
        {
            using var temel = new KasaWebFactory();
            using var host = Ac(temel, yol);
            using var editor = await Editor(host);
            using var izleyici = await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = IzleyiciSifresi }, ct);
            Assert.Equal(HttpStatusCode.OK, izleyici.StatusCode);
            using var alici = await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici1", "Alıcı 1", "alici-sifresi-123"), ct);
            Assert.Equal(HttpStatusCode.Created, alici.StatusCode);
            var aliciId = (await alici.Content.ReadFromJsonAsync<AliciDto>(ct))!.Id;
            using var kodYaniti = await editor.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = EskiSifre }, ct);
            Assert.Equal(HttpStatusCode.OK, kodYaniti.StatusCode);
            var kod = (await kodYaniti.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("kod").GetString()!;
            var once = Durum(host);

            var gunluk = YazmayiBoz(yol);
            try
            {
                using var sifre = await editor.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = EskiSifre, yeniSifre = YeniSifre }, ct);
                Assert.Equal(HttpStatusCode.InternalServerError, sifre.StatusCode);
                using var yeniKod = await editor.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = EskiSifre }, ct);
                Assert.Equal(HttpStatusCode.InternalServerError, yeniKod.StatusCode);
                using var kurtarmaIstemcisi = host.CreateClient();
                using var kurtar = await kurtarmaIstemcisi.PostAsJsonAsync("/api/auth/kurtar", new { kullanici = "editor", kod, yeniSifre = YeniSifre }, ct);
                Assert.Equal(HttpStatusCode.InternalServerError, kurtar.StatusCode);
                using var yeniIzleyici = await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "yeni-izleyici-sifresi" }, ct);
                Assert.Equal(HttpStatusCode.InternalServerError, yeniIzleyici.StatusCode);
                using var yeniAlici = await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici2", "Alıcı 2", "alici-sifresi-456"), ct);
                Assert.Equal(HttpStatusCode.InternalServerError, yeniAlici.StatusCode);
                using var degisenAlici = await editor.PutAsJsonAsync($"/api/alicilar/{aliciId}", new AliciYaz("alici1", "Alıcı 1", "degisen-alici-sifresi", Aktif: false), ct);
                Assert.Equal(HttpStatusCode.InternalServerError, degisenAlici.StatusCode);
                Assert.Equal(once, Durum(host));

                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Kasa:EditorKullanici"] = "editor",
                    ["Kasa:EditorSifre"] = "operatorun-yeni-sifresi",
                    ["Kasa:JwtKey"] = "test-jwt-anahtari-en-az-32-bayt-olmali!!",
                    [EditorSifreSifirlama.Ayar] = "true",
                }).Build();
                var hata = Record.Exception(() => EditorSifreSifirlama.Uygula(db, cfg, scope.ServiceProvider.GetRequiredService<GuvenlikGunlugu>()));
                Assert.True(hata is IOException or UnauthorizedAccessException, $"Beklenmeyen hata: {hata}");
                Assert.Equal(once, Durum(host));
                Assert.Null(DurumSifirlamaIzi(host));
            }
            finally { Onar(yol, gunluk); }
        }
        finally { Temizle(yol); }
    }

    private static string? DurumSifirlamaIzi(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().SistemDurumu.AsNoTracking().Single().EditorSifirlamaIzi;
    }

    [Fact]
    public void Uretimde_gunlugu_kapatma_ayari_baslangicta_reddedilir()
    {
        var yol = GeciciGunluk();
        try
        {
            using var temel = new KasaWebFactory();
            using var host = temel.WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GuvenlikGunlugu:Etkin"] = "false",
                    ["GuvenlikGunlugu:Yol"] = yol,
                }));
            });
            var hata = Record.Exception(() => host.CreateClient());
            Assert.NotNull(hata);
            Assert.Contains("GuvenlikGunlugu:Etkin=false", hata.ToString(), StringComparison.Ordinal);
        }
        finally { Temizle(yol); }
    }

    [Fact]
    public void Normal_acilista_gunlugun_ortasindaki_bozuk_satir_reddedilir()
    {
        var yol = GeciciGunluk();
        try
        {
            using (var temel = new KasaWebFactory())
            using (var host = Ac(temel, yol))
            using (var istemci = host.CreateClient())
            {
                host.Services.GetRequiredService<GuvenlikGunlugu>().Yaz(GuvenlikGunlugu.IzleyiciSifresiDegisti, zorunlu: true);
            }

            var satirlar = File.ReadAllLines(yol);
            Assert.Equal(2, satirlar.Length);
            File.WriteAllLines(yol, [satirlar[0], "{bozuk-json", satirlar[1]]);

            using var ikinciTemel = new KasaWebFactory();
            using var ikinciHost = Ac(ikinciTemel, yol);
            var hata = Record.Exception(() => ikinciHost.CreateClient());
            Assert.NotNull(hata);
            Assert.Contains("Güvenlik günlüğünde bozuk satır", hata.ToString(), StringComparison.Ordinal);

            File.WriteAllText(yol, string.Empty);
            using var ucuncuTemel = new KasaWebFactory();
            using var ucuncuHost = Ac(ucuncuTemel, yol);
            using var yeniIstemci = ucuncuHost.CreateClient();
            var ilkSatir = Assert.Single(File.ReadAllLines(yol));
            using var baslangic = JsonDocument.Parse(ilkSatir);
            Assert.Equal(GuvenlikGunlugu.Basladi, baslangic.RootElement.GetProperty("tur").GetString());
        }
        finally { Temizle(yol); }
    }

    [Fact]
    public async Task Geri_yukleme_gunluk_yazilamazsa_isareti_ve_kimlik_sikilastirmasini_geri_alir_sonra_yeniden_dener()
    {
        var ct = TestContext.Current.CancellationToken;
        var yol = GeciciGunluk();
        try
        {
            using var temel = new KasaWebFactory();
            using var host = Ac(temel, yol);
            using var editor = await Editor(host);
            using var izleyici = await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = IzleyiciSifresi }, ct);
            Assert.Equal(HttpStatusCode.OK, izleyici.StatusCode);
            using var alici = await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici1", "Alıcı 1", "alici-sifresi-123"), ct);
            Assert.Equal(HttpStatusCode.Created, alici.StatusCode);
            using var pasifAlici = await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici2", "Alıcı 2", "pasif-alici-sifresi", Aktif: false), ct);
            Assert.Equal(HttpStatusCode.Created, pasifAlici.StatusCode);
            var baslangic = JsonDocument.Parse(File.ReadLines(yol).First()).RootElement.GetProperty("zaman").GetDateTimeOffset();
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                db.SistemDurumu.Single().YedekZamani = baslangic; // Başlangıç ve yedek damgası eşit: bilinmeyen aralık güvenli sayılamaz.
                db.SaveChanges();
                db.Database.ExecuteSqlRaw($"PRAGMA user_version = {GeriYuklemeIsleyici.Isaret};");
            }
            var once = DurumIzleyiciVeIsaret(host);
            Assert.Equal(GeriYuklemeIsleyici.Isaret, once.Isaret);
            Assert.NotNull(once.Izleyici);

            var gunluk = YazmayiBoz(yol);
            try
            {
                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                var hata = Record.Exception(() => GeriYuklemeIsleyici.Isle(db, scope.ServiceProvider.GetRequiredService<GuvenlikGunlugu>()));
                Assert.True(hata is IOException or UnauthorizedAccessException, $"Beklenmeyen hata: {hata}");
                Assert.Equal(once, DurumIzleyiciVeIsaret(host));
                Assert.Empty(db.DenetimOlaylari.AsNoTracking().Where(o => o.Tur == GuvenlikOlaylari.GeriYuklemeIslendi).ToList());
            }
            finally { Onar(yol, gunluk); }

            // Okuma başarılı olduğu hâlde append engellenirse de DB işareti ve kimlikler değişmez.
            void YazilamazkenDene()
            {
                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                var hata = Record.Exception(() => GeriYuklemeIsleyici.Isle(db, scope.ServiceProvider.GetRequiredService<GuvenlikGunlugu>()));
                Assert.True(hata is IOException or UnauthorizedAccessException, $"Beklenmeyen hata: {hata}");
                Assert.Equal(once, DurumIzleyiciVeIsaret(host));
                Assert.Empty(db.DenetimOlaylari.AsNoTracking().Where(o => o.Tur == GuvenlikOlaylari.GeriYuklemeIslendi).ToList());
            }
            if (OperatingSystem.IsWindows())
            {
                using var kilit = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.Read);
                YazilamazkenDene();
            }
            else
            {
                var oncekiIzin = File.GetUnixFileMode(yol);
                File.SetUnixFileMode(yol, UnixFileMode.UserRead);
                try
                {
                    YazilamazkenDene();
                }
                finally
                {
                    File.SetUnixFileMode(yol, oncekiIzin);
                }
            }

            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                var sonuc = GeriYuklemeIsleyici.Isle(db, scope.ServiceProvider.GetRequiredService<GuvenlikGunlugu>());
                Assert.NotNull(sonuc);
                Assert.True(sonuc.EditorGirisiKilitlendi);
                Assert.Single(sonuc.PasifAlicilar);
                Assert.Contains(GeriYuklemeIsleyici.EditorGirisiKilidiEksikGunluk, sonuc.Rapor);
            }
            var sonra = DurumIzleyiciVeIsaret(host);
            Assert.Equal(0, sonra.Isaret);
            Assert.Null(sonra.Izleyici);
            Assert.Null(sonra.YedekZamani);
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                Assert.Equal(EditorGuvenligi.GirisKilidi, db.EditorGuvenlik.AsNoTracking().Single().SifreHash);
                Assert.All(db.Alicilar.AsNoTracking().ToList(), a =>
                {
                    Assert.False(a.Aktif);
                    Assert.Equal(GeriYuklemeIsleyici.AliciSifreKilidi, a.SifreHash);
                });
            }
            using var yeniIstemci = host.CreateClient();
            using var eskiEditor = await yeniIstemci.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = EskiSifre }, ct);
            using var eskiAlici = await yeniIstemci.PostAsJsonAsync("/api/auth/login", new { kullanici = "alici1", sifre = "alici-sifresi-123" }, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, eskiEditor.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, eskiAlici.StatusCode);
        }
        finally { Temizle(yol); }
    }

    private static (int Isaret, string? Izleyici, DateTimeOffset? YedekZamani, DateTimeOffset? SonGeriYukleme, string OturumDonemi) DurumIzleyiciVeIsaret(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        using var komut = db.Database.GetDbConnection().CreateCommand();
        komut.CommandText = "PRAGMA user_version;";
        var isaret = Convert.ToInt32(komut.ExecuteScalar());
        var durum = db.SistemDurumu.AsNoTracking().Single();
        return (isaret, db.Ayarlar.AsNoTracking().Single().IzleyiciSifreHash,
            durum.YedekZamani, durum.SonGeriYukleme, durum.OturumDonemi);
    }
}
