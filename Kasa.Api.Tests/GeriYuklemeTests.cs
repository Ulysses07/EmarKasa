using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>
/// Geri yükleme (gap-geri-yukleme-durum-geri-sarma-2, -6): yedekten geri yüklenen veritabanı bütün durumu yedek anına
/// sarar: izleyici şifresi değişikliği, oturum iptalleri, AUTOINCREMENT sayaçları ve kayıt sürümleri. Uygulamanın
/// yedekleri (ve restore_backup.py'nin açtığı dosya) geri yükleme işareti taşır; uygulama işaretli dosyayla ilk açılışta
/// bütün eski oturumları ve izleyici girişini kapatır, kimlikleri güvenli bir pay kadar ileri alır. Canlıdaki gibi dosya
/// veritabanıyla: yedek uygulamanın kendi yedek servisiyle alınır, geri yükleme ZIP'teki kasa.db ile yeni dosyaya yapılır.
/// </summary>
public class GeriYuklemeTests
{
    // Üretimdeki değerlerle aynı olmalı (Sabitler_sunucu_ve_restore_araci_arasinda_aynidir).
    private const int Isaret = 0x4B534759;
    private const int KimlikAraligi = 1_000_000;
    private const string OlayTuru = "GeriYuklemeIslendi";

    private const string EskiIzleyiciSifresi = "eski-izleyici-sifresi"; // yedek anındaki: ayrılan kişi biliyor
    private const string YeniIzleyiciSifresi = "yeni-izleyici-sifresi"; // yedekten sonra değiştirilen
    private const string AliciSifresi = "gizlisifre123";

    /// <summary>Verilen dosyayla çalışan uygulama: canlıdaki gibi dosya veritabanı, yedek dizini geçici.</summary>
    private sealed class Fabrika : KasaWebFactory
    {
        private readonly string _baglanti;
        private readonly string _dizin;

        public Fabrika(string yol, string dizin)
        {
            Saat = new SabitSaat(VarsayilanBugun);
            _baglanti = Baglanti(yol);
            _dizin = dizin;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Yedek:Dizin"] = _dizin, ["Yedek:Etkin"] = "false", ["Bildirim:PushEtkin"] = "false", ["Bildirim:WorkerEtkin"] = "false",
                ["Finans:BakimEtkin"] = "false", ["Bildirim:AnahtarDosyasi"] = Path.Combine(_dizin + "-anahtar", ".kasa-push-keys.json"),
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(_baglanti));
            });
        }
    }

    private static string Baglanti(string yol, bool saltOkunur = false) => new SqliteConnectionStringBuilder
    {
        DataSource = yol, Pooling = false, DefaultTimeout = 5, Mode = saltOkunur ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
    }.ToString();

    private static string GeciciYol(string ek) => Path.Combine(Path.GetTempPath(), "kasa-geri-" + Guid.NewGuid().ToString("N") + ek);

    private static void Temizle(IEnumerable<string> dosyalar, string dizin)
    {
        SqliteConnection.ClearAllPools();
        foreach (var dosya in dosyalar)
            foreach (var ek in new[] { "", "-wal", "-shm", "-journal" })
                try { File.Delete(dosya + ek); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var d in new[] { dizin, dizin + "-anahtar" })
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Çerezsiz istemci: oturum yalnız verilen Bearer belirteciyle taşınır (masaüstü gibi).</summary>
    private static HttpClient Oturumlu(KasaWebFactory f, string? jwt)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (jwt is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return c;
    }

    private static async Task<HttpResponseMessage> Giris(KasaWebFactory f, string? kullanici, string sifre)
    {
        using var c = Oturumlu(f, null);
        return await c.PostAsJsonAsync("/api/auth/login", new { kullanici, sifre });
    }

    /// <summary>Başarılı giriş: (oturum JWT'si, gövdedeki tanıdık cihaz belirteci).</summary>
    private static async Task<(string Jwt, string? Cihaz)> GirisYap(KasaWebFactory f, string? kullanici, string sifre)
    {
        using var yanit = await Giris(f, kullanici, sifre);
        Assert.True(yanit.StatusCode == HttpStatusCode.OK, $"{kullanici ?? "izleyici"} girişi: {yanit.StatusCode}");
        var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();
        return (govde.GetProperty("token").GetString()!,
            govde.TryGetProperty("cihaz", out var cihaz) && cihaz.ValueKind == JsonValueKind.String ? cihaz.GetString() : null);
    }

    private static async Task<HttpStatusCode> GirisDurumu(KasaWebFactory f, string? kullanici, string sifre)
    {
        using var yanit = await Giris(f, kullanici, sifre);
        return yanit.StatusCode;
    }

    private static async Task<int> AlisAc(HttpClient editor)
    {
        var kanallar = await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar");
        return (await AlisWorkflowTests.Read<AlisDto>(await editor.PostAsJsonAsync("/api/alis", AlisWorkflowTests.Draft(kanallar!)))).Id;
    }

    private static async Task<int> AliciAc(HttpClient editor, string kullanici) =>
        (await AlisWorkflowTests.Read<AliciDto>(await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz(kullanici, kullanici, AliciSifresi)))).Id;

    private static async Task<string> YedekAl(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        return await f.Services.GetRequiredService<YedekServisi>().Olustur(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), YedekTuru.Elle, CancellationToken.None);
    }

    private static void KasaDbCikar(string zip, string hedef)
    {
        using var arsiv = ZipFile.OpenRead(zip);
        arsiv.GetEntry("kasa.db")!.ExtractToFile(hedef);
    }

    private static long UserVersion(string yol)
    {
        using var c = new SqliteConnection(Baglanti(yol, saltOkunur: true)); c.Open();
        using var k = c.CreateCommand(); k.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(k.ExecuteScalar());
    }

    /// <summary>AUTOINCREMENT'li her tablo için verilebilecek en yüksek kimlik: MAX(sayaç, en yüksek rowid). Sayaç satırı olmayan
    /// (hiç kayıt almamış) tablo da listededir.</summary>
    private static Dictionary<string, long> KimlikTabanlari(string yol)
    {
        using var c = new SqliteConnection(Baglanti(yol, saltOkunur: true)); c.Open();
        var tablolar = new List<string>();
        using (var k = c.CreateCommand())
        {
            k.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND sql LIKE '%AUTOINCREMENT%' ORDER BY name;";
            using var r = k.ExecuteReader();
            while (r.Read()) tablolar.Add(r.GetString(0));
        }
        var sonuc = new Dictionary<string, long>();
        foreach (var t in tablolar)
        {
            using var k = c.CreateCommand();
            k.CommandText = $"SELECT MAX(COALESCE((SELECT seq FROM sqlite_sequence WHERE name = $ad), 0), COALESCE((SELECT MAX(rowid) FROM \"{t}\"), 0));";
            k.Parameters.AddWithValue("$ad", t);
            sonuc[t] = Convert.ToInt64(k.ExecuteScalar());
        }
        return sonuc;
    }

    /// <summary>Yeniden açılış hiçbir sayacı ileri almadı (giriş olayları DenetimOlaylari sayacını olağan biçimde artırır).</summary>
    private static void IleriAlinmadi(Dictionary<string, long> onceki, Dictionary<string, long> sonraki)
    {
        Assert.Equal(onceki.Keys.Order(), sonraki.Keys.Order());
        Assert.All(onceki, t => Assert.True(sonraki[t.Key] - t.Value < 100, $"{t.Key}: {t.Value} → {sonraki[t.Key]}"));
    }

    [Fact]
    public async Task Geri_yuklenen_veritabani_ilk_acilista_eski_oturumlari_ve_izleyici_girisini_kapatir_kimlikleri_ileri_alir()
    {
        var dizin = GeciciYol("");
        var canli = GeciciYol(".db");
        var geri = GeciciYol(".db");
        try
        {
            string zip, editorJwt, izleyiciJwt, aliciJwt, editorCihazi;
            int alisOnce, alisSonra, aliciSonra;
            List<AlisKanalDto> kanallar;
            var fA = new Fabrika(canli, dizin);
            try
            {
                (editorJwt, var cihaz) = await GirisYap(fA, "editor", "kasa123");
                editorCihazi = cihaz!;
                using var editor = Oturumlu(fA, editorJwt);
                await AlisWorkflowTests.Prepare(editor);
                (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = EskiIzleyiciSifresi })).EnsureSuccessStatusCode();
                (izleyiciJwt, _) = await GirisYap(fA, null, EskiIzleyiciSifresi);
                await AliciAc(editor, "alici1");
                (aliciJwt, _) = await GirisYap(fA, "alici1", AliciSifresi);
                alisOnce = await AlisAc(editor);
                kanallar = (await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar"))!;
                // Tanıdık cihaz belirteci canlıda geçerli (sınamanın dayanağı).
                using (var scope = fA.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                    var istek = new DefaultHttpContext().Request; istek.Headers[TanidikCihaz.BaslikAdi] = editorCihazi;
                    Assert.NotNull(fA.Services.GetRequiredService<TanidikCihaz>().Dogrula(istek, GirisSiniri.EditorHedefi,
                        OturumDamgasi.Uret("editor", scope.ServiceProvider.GetRequiredService<IConfiguration>(), db)));
                }

                zip = await YedekAl(fA);

                // Yedekten sonra: ayrılan kişi yüzünden izleyici şifresi değişir, yeni alış ve alıcı açılır.
                (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = YeniIzleyiciSifresi })).EnsureSuccessStatusCode();
                alisSonra = await AlisAc(editor);
                aliciSonra = await AliciAc(editor, "alici2");
                Assert.Equal(alisOnce + 1, alisSonra);
                using (var izleyici = Oturumlu(fA, izleyiciJwt))
                    Assert.Equal(HttpStatusCode.Unauthorized, (await izleyici.GetAsync("/api/rapor/panel")).StatusCode);
            }
            finally { fA.Dispose(); }
            Assert.Equal(0, UserVersion(canli)); // canlı dosya işaret taşımaz

            // Geri yükleme: yedekteki kasa.db yeni bir dosyaya açılır ve uygulama onunla başlatılır.
            KasaDbCikar(zip, geri);
            var yedektekiTabanlar = KimlikTabanlari(geri);
            var yedekIsareti = UserVersion(geri);
            var fB = new Fabrika(geri, dizin);
            try
            {
                // gR2: ne yedek anındaki (ayrılan kişinin bildiği) ne sonradan belirlenen izleyici şifresiyle giriş yapılır.
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, null, EskiIzleyiciSifresi));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, null, YeniIzleyiciSifresi));
                // Yedekten önce alınmış bütün oturumlar (izleyici, editör, alıcı) ve tanıdık cihaz belirteci geçersiz.
                using (var izleyici = Oturumlu(fB, izleyiciJwt))
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, (await izleyici.GetAsync("/api/rapor/panel")).StatusCode);
                    Assert.Equal(HttpStatusCode.Unauthorized, (await izleyici.GetAsync("/api/disari-aktar?baslangic=2026-01-01&bitis=2026-09-25&bicim=csv")).StatusCode);
                }
                using (var eskiEditor = Oturumlu(fB, editorJwt))
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, (await eskiEditor.GetAsync("/api/auth/me")).StatusCode);
                    // gR6: eski soydaki ekranın (Id, Surum) çiftiyle yazma oturum sonu alır.
                    Assert.Equal(HttpStatusCode.Unauthorized, (await eskiEditor.PutAsJsonAsync($"/api/alis/{alisSonra}", AlisWorkflowTests.Draft(kanallar) with { Surum = 1 })).StatusCode);
                }
                using (var alici = Oturumlu(fB, aliciJwt))
                    Assert.Equal(HttpStatusCode.Unauthorized, (await alici.GetAsync("/api/alis")).StatusCode);
                using (var scope = fB.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                    var istek = new DefaultHttpContext().Request; istek.Headers[TanidikCihaz.BaslikAdi] = editorCihazi;
                    Assert.Null(fB.Services.GetRequiredService<TanidikCihaz>().Dogrula(istek, GirisSiniri.EditorHedefi,
                        OturumDamgasi.Uret("editor", scope.ServiceProvider.GetRequiredService<IConfiguration>(), db)));
                }

                // Editör yeniden girer; izleyici şifresi yok görünür (editör yenisini belirleyene kadar izleyici kapalı).
                var (yeniJwt, _) = await GirisYap(fB, "editor", "kasa123");
                using var editor = Oturumlu(fB, yeniJwt);
                Assert.False((await editor.GetFromJsonAsync<JsonElement>("/api/ayarlar")).GetProperty("izleyiciSifreVarMi").GetBoolean());

                // gR6: AUTOINCREMENT'li her tablonun sayacı yedekteki en yüksek kimlikten en az 1.000.000 ileride; yedek anında hiç
                // kayıt almamış (sayaç satırı olmayan) tablolar da.
                var tabanlar = KimlikTabanlari(geri);
                Assert.Equal(yedektekiTabanlar.Keys.Order(), tabanlar.Keys.Order());
                Assert.Contains(yedektekiTabanlar, t => t.Value == 0);
                Assert.All(yedektekiTabanlar, t => Assert.True(tabanlar[t.Key] >= t.Value + KimlikAraligi, $"{t.Key}: {tabanlar[t.Key]} < {t.Value} + {KimlikAraligi}"));
                // Atılan soydaki kimlikler yeni kayda verilmez: eski ekranın (Id, Surum) çifti başka kayda ulaşamaz.
                var yeniAlis = await AlisAc(editor);
                Assert.Equal(yedektekiTabanlar["Alislar"] + KimlikAraligi + 1, yeniAlis);
                Assert.Equal(HttpStatusCode.NotFound, (await editor.PutAsJsonAsync($"/api/alis/{alisSonra}", AlisWorkflowTests.Draft(kanallar) with { Surum = 1 })).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await editor.PutAsJsonAsync($"/api/alicilar/{aliciSonra}", new AliciYaz("alici2", "alici2", null))).StatusCode);
                // Yedek anında var olan kayıt aynı kayıttır ve güncel sürümüyle yazılır.
                Assert.Equal(HttpStatusCode.OK, (await editor.PutAsJsonAsync($"/api/alis/{alisOnce}", AlisWorkflowTests.Draft(kanallar) with { Surum = 1, Tedarikci = "Yedekteki alış" })).StatusCode);

                // İşlem denetim izinde (aktör sistem) görünür; işaret silinir.
                using (var scope = fB.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                    var olay = Assert.Single(db.DenetimOlaylari.AsNoTracking().Where(o => o.Tur == OlayTuru).ToList());
                    Assert.Equal(("sistem", "Oturum"), (olay.AktorRol, olay.Varlik));
                    Assert.True(Guid.TryParse(olay.VarlikId, out _));
                    var ayrinti = JsonDocument.Parse(olay.YeniJson!).RootElement;
                    Assert.Equal(KimlikAraligi, ayrinti.GetProperty("kimlikAraligi").GetInt32());
                    Assert.True(ayrinti.GetProperty("izleyiciErisimiKapatildi").GetBoolean());
                    Assert.Contains(db.DenetimOlaylari.AsNoTracking().Where(o => o.Tur == "IzleyiciSifresiDegisti").ToList(), o => o.AktorRol == "sistem");
                }
                Assert.Equal(Isaret, yedekIsareti); // yedek kopyası geri yükleme işaretini taşır
                Assert.Equal(0, UserVersion(geri));

                // Editör yeni izleyici şifresi belirleyince izleyici girişi açılır.
                (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "ucuncu-izleyici-sifresi" })).EnsureSuccessStatusCode();
                Assert.Equal(HttpStatusCode.OK, await GirisDurumu(fB, null, "ucuncu-izleyici-sifresi"));
            }
            finally { fB.Dispose(); }

            // Yeniden başlatma geri yüklemeyi yeniden işlemez: izleyici açık kalır, sayaçlar yeniden ileri alınmaz.
            var islenmis = KimlikTabanlari(geri);
            var fC = new Fabrika(geri, dizin);
            try
            {
                Assert.Equal(HttpStatusCode.OK, await GirisDurumu(fC, null, "ucuncu-izleyici-sifresi"));
                using var scope = fC.Services.CreateScope();
                Assert.Single(scope.ServiceProvider.GetRequiredService<KasaDbContext>().DenetimOlaylari.AsNoTracking().Where(o => o.Tur == OlayTuru).ToList());
            }
            finally { fC.Dispose(); }
            IleriAlinmadi(islenmis, KimlikTabanlari(geri));
        }
        finally { Temizle([canli, geri], dizin); }
    }

    [Fact]
    public async Task Isaretsiz_canli_veritabani_yeniden_acilista_degismez()
    {
        var dizin = GeciciYol("");
        var yol = GeciciYol(".db");
        try
        {
            var f1 = new Fabrika(yol, dizin);
            try
            {
                using var editor = Oturumlu(f1, (await GirisYap(f1, "editor", "kasa123")).Jwt);
                (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = EskiIzleyiciSifresi })).EnsureSuccessStatusCode();
            }
            finally { f1.Dispose(); }
            var tabanlar = KimlikTabanlari(yol);
            var f2 = new Fabrika(yol, dizin);
            try
            {
                Assert.Equal(HttpStatusCode.OK, await GirisDurumu(f2, null, EskiIzleyiciSifresi));
                using var scope = f2.Services.CreateScope();
                Assert.Empty(scope.ServiceProvider.GetRequiredService<KasaDbContext>().DenetimOlaylari.AsNoTracking().Where(o => o.Tur == OlayTuru).ToList());
            }
            finally { f2.Dispose(); }
            IleriAlinmadi(tabanlar, KimlikTabanlari(yol));
            Assert.All(KimlikTabanlari(yol).Values, v => Assert.True(v < KimlikAraligi));
        }
        finally { Temizle([yol], dizin); }
    }

    /// <summary>
    /// Bu sürümden önce alınmış (işaretsiz) yedek: restore_backup.py geri açarken işaretler, uygulama ilk açılışta işler. Ortamda
    /// Python yoksa sınama atlanır (araç deploy/tests altında ayrıca sınanır).
    /// </summary>
    [Fact]
    public async Task Isaretsiz_eski_yedek_restore_araciyla_isaretlenir_ve_ilk_acilista_islenir()
    {
        var dizin = GeciciYol("");
        var canli = GeciciYol(".db");
        var geri = GeciciYol(".db");
        var eskiDb = GeciciYol(".db");
        try
        {
            string zip;
            var fA = new Fabrika(canli, dizin);
            try
            {
                using var editor = Oturumlu(fA, (await GirisYap(fA, "editor", "kasa123")).Jwt);
                (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = EskiIzleyiciSifresi })).EnsureSuccessStatusCode();
                zip = await YedekAl(fA);
            }
            finally { fA.Dispose(); }

            // Eski biçim: işaretsiz kasa.db ve ona göre manifest özeti (2.3 öncesi uygulamanın yazdığı gibi).
            KasaDbCikar(zip, eskiDb);
            using (var c = new SqliteConnection(Baglanti(eskiDb))) { c.Open(); using var k = c.CreateCommand(); k.CommandText = "PRAGMA user_version = 0;"; k.ExecuteNonQuery(); }
            SqliteConnection.ClearAllPools();
            var eskiZip = Path.Combine(dizin, "kasa-elle-20260920-030000-0a1b2c3d.zip");
            JsonNode manifest;
            using (var arsiv = ZipFile.OpenRead(zip)) using (var akis = arsiv.GetEntry("manifest.json")!.Open()) manifest = JsonNode.Parse(akis)!;
            manifest["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(eskiDb)));
            using (var yeni = ZipFile.Open(eskiZip, ZipArchiveMode.Create))
            {
                yeni.CreateEntryFromFile(eskiDb, "kasa.db");
                using var akis = yeni.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(akis, manifest);
            }
            Assert.Equal(0, UserVersion(eskiDb));

            var cikti = RestoreAraci(eskiZip, geri);
            if (cikti is null) return; // Python yok
            Assert.Contains("izleyici şifresi", cikti);
            Assert.Equal(Isaret, UserVersion(geri));

            var fB = new Fabrika(geri, dizin);
            try
            {
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, null, EskiIzleyiciSifresi));
                Assert.Equal(HttpStatusCode.OK, await GirisDurumu(fB, "editor", "kasa123"));
            }
            finally { fB.Dispose(); }
            Assert.Equal(0, UserVersion(geri));
        }
        finally { Temizle([canli, geri, eskiDb], dizin); }
    }

    [Fact]
    public void Sabitler_sunucu_ve_restore_araci_arasinda_aynidir()
    {
        Assert.Equal((Isaret, KimlikAraligi, OlayTuru), (GeriYuklemeIsleyici.Isaret, GeriYuklemeIsleyici.KimlikAraligi, GeriYuklemeIsleyici.OlayTuru));
        var arac = File.ReadAllText(AracYolu());
        var isaret = Regex.Match(arac, @"^GERI_YUKLEME_ISARETI = 0x([0-9A-Fa-f]+)\r?$", RegexOptions.Multiline);
        var aralik = Regex.Match(arac, @"^KIMLIK_ARALIGI = ([0-9_]+)\r?$", RegexOptions.Multiline);
        Assert.True(isaret.Success && aralik.Success, "restore_backup.py sabitleri bulunamadı.");
        Assert.Equal(GeriYuklemeIsleyici.Isaret, Convert.ToInt32(isaret.Groups[1].Value, 16));
        Assert.Equal(GeriYuklemeIsleyici.KimlikAraligi, int.Parse(aralik.Groups[1].Value.Replace("_", ""), System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string AracYolu([System.Runtime.CompilerServices.CallerFilePath] string kaynak = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(kaynak)!, "..", "deploy", "restore_backup.py"));

    /// <summary>deploy/restore_backup.py'yi çalıştırır; ortamda Python yoksa null. Araç hata verirse sınama düşer.</summary>
    private static string? RestoreAraci(string zip, string cikti)
    {
        var arac = AracYolu();
        foreach (var python in new[] { "python3", "python" })
        {
            Process? p;
            try
            {
                var bilgi = new ProcessStartInfo(python) { ArgumentList = { arac, zip, "--output", cikti }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                    StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
                bilgi.Environment["PYTHONIOENCODING"] = "utf-8";
                p = Process.Start(bilgi);
            }
            catch (System.ComponentModel.Win32Exception) { continue; }
            if (p is null) continue;
            using (p)
            {
                var cikis = p.StandardOutput.ReadToEndAsync(); var hata = p.StandardError.ReadToEndAsync();
                Assert.True(p.WaitForExit(60_000), "restore_backup.py zamanında bitmedi.");
                // Windows'taki 'python3' uygulama mağazası kısayolu olabilir (9009): gerçek Python değilse sonrakine geç.
                if (p.ExitCode == 9009) continue;
                Assert.True(p.ExitCode == 0, $"restore_backup.py yedeği reddetti: {hata.Result} {cikis.Result}");
                return cikis.Result;
            }
        }
        return null;
    }
}
