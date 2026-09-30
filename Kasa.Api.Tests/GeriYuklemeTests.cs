using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Kasa.Core.Kodlar;
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
/// Geri yükleme (gap-geri-yukleme-durum-geri-sarma-1, -2, -6): yedekten geri yüklenen veritabanı bütün durumu yedek anına
/// sarar: editör şifresi ve kurtarma kodu, izleyici şifresi, alıcı hesapları, oturum iptalleri, bildirim abonelikleri,
/// AUTOINCREMENT sayaçları ve kayıt sürümleri. Uygulamanın yedekleri (ve restore_backup.py'nin açtığı dosya) geri yükleme
/// işareti ve yedek anı taşır; uygulama işaretli dosyayla ilk açılışta yeni oturum dönemi açar (bütün eski oturumlar düşer),
/// kurtarma kodunu, izleyici girişini ve bildirim kayıtlarını kapatır, yedekten sonraki güvenlik kararlarını veritabanı dışındaki
/// güvenlik günlüğünden yeniden uygular ve kimlikleri güvenli bir pay kadar ileri alır. Kasa kayıtları ve raporlar değişmez.
/// Canlıdaki gibi dosya veritabanıyla: yedek uygulamanın kendi yedek servisiyle alınır, geri yükleme ZIP'teki kasa.db ile yeni
/// dosyaya yapılır.
/// </summary>
public class GeriYuklemeTests
{
    // Üretimdeki değerlerle aynı olmalı (Sabitler_sunucu_ve_restore_araci_arasinda_aynidir).
    private const int Isaret = 0x4B534759;
    private const int KimlikAraligi = 1_000_000;
    private const string OlayTuru = "GeriYuklemeIslendi";
    private const string IsaretTablosu = "__KasaGeriYukleme";
    private const string JwtAnahtari = "test-jwt-anahtari-en-az-32-bayt-olmali!!";

    private const string EskiIzleyiciSifresi = "eski-izleyici-sifresi"; // yedek anındaki: ayrılan kişi biliyor
    private const string YeniIzleyiciSifresi = "yeni-izleyici-sifresi"; // yedekten sonra değiştirilen
    private const string AliciSifresi = "gizlisifre123";
    private const string EditorP1 = "birinci-editor-sifresi";  // yedek anındaki editör şifresi (çalınan cihazda oturumu olan)
    private const string EditorP2 = "ikinci-editor-sifresi";   // yedekten sonra, cihaz çalınınca belirlenen
    private const string YeniOrtamSifresi = "operatorun-yeni-ortam-sifresi"; // runbook: KASA_EDITOR_SIFRE geri yüklemeden önce yenilenir
    private const string IkinciOrtamSifresi = "operatorun-ikinci-ortam-sifresi";

    private static DateOnly Ay => new(KasaWebFactory.VarsayilanBugun.Year, KasaWebFactory.VarsayilanBugun.Month, 1);
    private static DateOnly Haziran => Ay.AddMonths(-3);
    private static DateOnly Temmuz => Ay.AddMonths(-2);
    private static DateOnly Agustos => Ay.AddMonths(-1);

    /// <summary>Verilen dosyayla çalışan uygulama: canlıdaki gibi dosya veritabanı, yedek dizini geçici. <paramref name="gunluk"/>:
    /// güvenlik günlüğü açık (varsayılan yer: yedek dizini/guvenlik-gunlugu.jsonl); <paramref name="editorSifresi"/>: ortamdaki
    /// editör şifresi (KASA_EDITOR_SIFRE; verilmezse test fabrikasının 'kasa123'ü); <paramref name="sifirla"/>: editör şifresi
    /// sıfırlama bayrağı (KASA_EDITOR_SIFRE_SIFIRLA, <see cref="EditorSifreSifirlama"/>).</summary>
    private sealed class Fabrika : KasaWebFactory
    {
        private readonly string _baglanti;
        private readonly string _dizin;
        private readonly bool _gunluk;
        private readonly string? _editorSifresi;
        private readonly bool _sifirla;

        public Fabrika(string yol, string dizin, SabitSaat? saat = null, bool gunluk = false, string? editorSifresi = null, bool sifirla = false)
        {
            Saat = saat ?? new SabitSaat(VarsayilanBugun);
            _baglanti = Baglanti(yol);
            _dizin = dizin;
            _gunluk = gunluk;
            _editorSifresi = editorSifresi;
            _sifirla = sifirla;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                var ayarlar = new Dictionary<string, string?>
                {
                    ["Yedek:Dizin"] = _dizin,
                    ["Yedek:Etkin"] = "false",
                    ["Bildirim:PushEtkin"] = "false",
                    ["Bildirim:WorkerEtkin"] = "false",
                    ["Finans:BakimEtkin"] = "false",
                    ["Bildirim:AnahtarDosyasi"] = Path.Combine(_dizin + "-anahtar", ".kasa-push-keys.json"),
                    ["GuvenlikGunlugu:Etkin"] = _gunluk ? "true" : "false",
                };
                if (_editorSifresi is not null)
                    ayarlar["Kasa:EditorSifre"] = _editorSifresi;
                if (_sifirla)
                    ayarlar[EditorSifreSifirlama.Ayar] = "true";
                cfg.AddInMemoryCollection(ayarlar);
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(_baglanti));
            });
        }
    }

    private static string Baglanti(string yol, bool saltOkunur = false) => new SqliteConnectionStringBuilder
    {
        DataSource = yol,
        Pooling = false,
        DefaultTimeout = 5,
        Mode = saltOkunur ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
    }.ToString();

    private static string GeciciYol(string ek) => Path.Combine(Path.GetTempPath(), "kasa-geri-" + Guid.NewGuid().ToString("N") + ek);

    private static void Temizle(IEnumerable<string> dosyalar, string dizin)
    {
        SqliteConnection.ClearAllPools();
        foreach (var dosya in dosyalar)
            foreach (var ek in new[] { "", "-wal", "-shm", "-journal" })
                try
                { File.Delete(dosya + ek); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var d in new[] { dizin, dizin + "-anahtar" })
            try
            { if (Directory.Exists(d)) Directory.Delete(d, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Çerezsiz istemci: oturum yalnız verilen Bearer belirteciyle taşınır (masaüstü gibi).</summary>
    private static HttpClient Oturumlu(KasaWebFactory f, string? jwt)
    {
        var c = f.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        if (jwt is not null)
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
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

    private static async Task<HttpStatusCode> OturumDurumu(KasaWebFactory f, string jwt)
    {
        using var c = Oturumlu(f, jwt);
        using var yanit = await c.GetAsync("/api/auth/me");
        return yanit.StatusCode;
    }

    private static async Task<HttpStatusCode> Kurtar(KasaWebFactory f, string kod)
    {
        using var c = Oturumlu(f, null);
        using var yanit = await c.PostAsJsonAsync("/api/auth/kurtar", new { kullanici = "editor", kod, yeniSifre = "kurtarmayla-yeni-sifre" });
        return yanit.StatusCode;
    }

    private static async Task<string> KurtarmaKodu(HttpClient editor, string sifre) =>
        (await AlisWorkflowTests.Read<JsonElement>(await editor.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = sifre }))).GetProperty("kod").GetString()!;

    private static async Task<int> AlisAc(HttpClient editor)
    {
        var kanallar = await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar");
        return (await AlisWorkflowTests.Read<AlisDto>(await editor.PostAsJsonAsync("/api/alis", AlisWorkflowTests.Draft(kanallar!)))).Id;
    }

    private static async Task<int> AliciAc(HttpClient editor, string kullanici) =>
        (await AlisWorkflowTests.Read<AliciDto>(await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz(kullanici, kullanici, AliciSifresi)))).Id;

    /// <summary>Raporlara giren veri: takip başlangıcı Haziran, iki gelir ve iki gider.</summary>
    private static async Task RaporVerisi(HttpClient c)
    {
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Haziran, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Haziran, "MEZAT", 1_000m))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Temmuz, "PERAKENDE", 2_500.50m))).EnsureSuccessStatusCode();
        await MonthlyExpenseTests.Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Temmuz.AddDays(4), "Tedarik", 300m, "MEZAT", GiderTipi.Cari));
        await MonthlyExpenseTests.Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Agustos.AddDays(9), "Kira", 900.01m, KanalEtiketleri.Ortak, GiderTipi.SabitGider));
    }

    /// <summary>Panel, haftalık ve takip başlangıcından bu aya kadar her ayın aylık raporu (yanıt metni).</summary>
    private static async Task<Dictionary<string, string>> Raporlar(HttpClient c)
    {
        var yollar = new List<string> { "/api/rapor/panel", "/api/rapor/haftalik" };
        for (var ay = Haziran; ay <= Ay; ay = ay.AddMonths(1))
            yollar.Add($"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}");
        var sonuc = new Dictionary<string, string>();
        foreach (var yol in yollar)
        {
            using var yanit = await c.GetAsync(yol);
            Assert.True(yanit.IsSuccessStatusCode, $"{yol}: {yanit.StatusCode}");
            sonuc[yol] = await yanit.Content.ReadAsStringAsync();
        }
        return sonuc;
    }

    /// <summary>Editör damgasıyla etkin bildirim abonelikleri (cihaz kayıtları); kimlikleri ve uç adresleri.</summary>
    private static List<(int Id, string Endpoint)> CihazEkle(KasaWebFactory f, int sayi)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var damga = OturumDamgasi.Uret("editor", scope.ServiceProvider.GetRequiredService<IConfiguration>(), db)!;
        var satirlar = Enumerable.Range(1, sayi).Select(i => new PushAbonelikEntity
        {
            Endpoint = $"https://push.example.test/gizli-uc-{i}-{Guid.NewGuid():N}",
            P256dh = "p256dh-anahtari",
            Auth = "auth-anahtari",
            CihazId = Guid.NewGuid().ToString("D"),
            CihazAdi = $"Cihaz {i}",
            OturumDamgasi = damga,
            Olusturuldu = 1,
            Etkin = true,
        }).ToList();
        db.AddRange(satirlar);
        db.SaveChanges();
        return satirlar.Select(s => (s.Id, s.Endpoint)).ToList();
    }

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

    private static object? Oku(string yol, string sql)
    {
        using var c = new SqliteConnection(Baglanti(yol, saltOkunur: true));
        c.Open();
        using var k = c.CreateCommand();
        k.CommandText = sql;
        var deger = k.ExecuteScalar();
        return deger is DBNull ? null : deger;
    }

    private static void Calistir(string yol, string sql)
    {
        using (var c = new SqliteConnection(Baglanti(yol)))
        { c.Open(); using var k = c.CreateCommand(); k.CommandText = sql; k.ExecuteNonQuery(); }
        SqliteConnection.ClearAllPools();
    }

    private static long UserVersion(string yol) => Convert.ToInt64(Oku(yol, "PRAGMA user_version;"));

    private static bool TabloVar(string yol, string tablo) => Oku(yol, $"SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{tablo}';") is not null;

    /// <summary>AUTOINCREMENT'li her tablo için verilebilecek en yüksek kimlik: MAX(sayaç, en yüksek rowid). Sayaç satırı olmayan
    /// (hiç kayıt almamış) tablo da listededir.</summary>
    private static Dictionary<string, long> KimlikTabanlari(string yol)
    {
        using var c = new SqliteConnection(Baglanti(yol, saltOkunur: true));
        c.Open();
        var tablolar = new List<string>();
        using (var k = c.CreateCommand())
        {
            k.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND sql LIKE '%AUTOINCREMENT%' ORDER BY name;";
            using var r = k.ExecuteReader();
            while (r.Read())
                tablolar.Add(r.GetString(0));
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

    /// <summary>Operatörün editör şifresi sıfırlamalarının denetim olayları (YeniJson), eskiden yeniye.</summary>
    private static List<JsonElement> SifirlamaOlaylari(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().DenetimOlaylari.AsNoTracking()
            .Where(o => o.Tur == GuvenlikOlaylari.EditorSifresiSifirlandi).OrderBy(o => o.Id).AsEnumerable()
            .Select(o => JsonDocument.Parse(o.YeniJson!).RootElement.Clone()).ToList();
    }

    private static JsonElement GeriYuklemeOlayi(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        var olay = Assert.Single(scope.ServiceProvider.GetRequiredService<KasaDbContext>().DenetimOlaylari.AsNoTracking().Where(o => o.Tur == OlayTuru).ToList());
        return JsonDocument.Parse(olay.YeniJson!).RootElement.Clone();
    }

    private static async Task<(DateTimeOffset? SonGeriYukleme, List<string> Rapor)> YedekDurumu(HttpClient editor)
    {
        var durum = await editor.GetFromJsonAsync<JsonElement>("/api/yedek/durum");
        var son = durum.GetProperty("sonGeriYukleme");
        var rapor = durum.GetProperty("geriYuklemeRaporu");
        return (son.ValueKind == JsonValueKind.Null ? null : son.GetDateTimeOffset(),
            rapor.ValueKind == JsonValueKind.Null ? [] : rapor.EnumerateArray().Select(m => m.GetString()!).ToList());
    }

    /// <summary>Yerel (İstanbul) gösterim: rapor maddesindeki yedek anı.</summary>
    private static string Yerel(DateTimeOffset an) => TimeZoneInfo.ConvertTime(an, KasaSaati.Istanbul).ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

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
                    var istek = new DefaultHttpContext().Request;
                    istek.Headers[TanidikCihaz.BaslikAdi] = editorCihazi;
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
                    var istek = new DefaultHttpContext().Request;
                    istek.Headers[TanidikCihaz.BaslikAdi] = editorCihazi;
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
                    Assert.True(Guid.TryParse(olay.VarlikId, out var soy));
                    // Oturum dönemi veri soyunun kimliğidir.
                    Assert.Equal(soy.ToString("N"), db.SistemDurumu.AsNoTracking().Single().OturumDonemi);
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

    /// <summary>
    /// gap-geri-yukleme-durum-geri-sarma-1 (ve kapsadığı -3, -4, -11, -14): yedek anında editör şifresi P1, kurtarma kodu K1, iki
    /// alıcı ve iki cihaz kaydı var. Yedekten sonra editörün dizüstü bilgisayarı çalınır: editör şifreyi P2'ye çevirir (çalınan
    /// cihazdaki oturum ve K1 düşer), yeni kurtarma kodu K2 üretir, izleyici şifresini değiştirir, bir alıcıyı pasife alır,
    /// ötekinin şifresini değiştirir, yeni alıcı açar, çalınan cihazın bildirim kaydını kaldırır ve Temmuz'u kapatır. Bu kararlar
    /// veritabanı dışındaki güvenlik günlüğündedir (gizli bilgi içermeden). Yedek geri yüklenince: hiçbir eski belirteç ve kurtarma
    /// kodu geçmez, P1 yeniden geçerli olmaz (P2 de kaybolmuştur) ve editör girişi kilitlenir; operatör runbook'a göre geri
    /// yüklemeden önce ortam şifresini yenileyip sıfırlama bayrağını açtığı için kilit aynı açılışta kalkar ve giriş yeni ortam
    /// şifresiyledir (ilk kurulumun şifresi geçmez); alıcılar pasif, cihaz kayıtları kapalı; rapor bunları söyler. Panel, haftalık ve aylık raporlar yedek
    /// anındakiyle birebir aynıdır. Yeniden başlatma işlemi tekrarlamaz; aynı yedeğin ikinci kez geri yüklenmesi ilk geri
    /// yüklemeden sonra alınmış oturumu da düşürür (yeni oturum dönemi).
    /// </summary>
    [Fact]
    public async Task Yedekten_sonraki_guvenlik_kararlari_geri_yuklemede_geri_sarilmaz_raporlar_ayni_kalir()
    {
        var dizin = GeciciYol("");
        var canli = GeciciYol(".db");
        var geri = GeciciYol(".db");
        var ikinci = GeciciYol(".db");
        var saat = new SabitSaat(KasaWebFactory.VarsayilanBugun);
        var gunlukYolu = Path.Combine(dizin, GuvenlikGunlugu.DosyaAdi);
        try
        {
            string zip, e1, e2, izleyiciJwt, alici1Jwt, alici2Jwt, k1, k2;
            DateTimeOffset yedekAni;
            Dictionary<string, string> raporlar;
            List<(int Id, string Endpoint)> cihazlar;
            var fA = new Fabrika(canli, dizin, saat, gunluk: true);
            try
            {
                using (var ilk = Oturumlu(fA, (await GirisYap(fA, "editor", "kasa123")).Jwt))
                    (await ilk.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = EditorP1 })).EnsureSuccessStatusCode();
                (e1, _) = await GirisYap(fA, "editor", EditorP1);
                using var editor = Oturumlu(fA, e1);
                await RaporVerisi(editor);
                (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = EskiIzleyiciSifresi })).EnsureSuccessStatusCode();
                (izleyiciJwt, _) = await GirisYap(fA, null, EskiIzleyiciSifresi);
                var alici1 = await AliciAc(editor, "alici1");
                var alici2 = await AliciAc(editor, "alici2");
                (alici1Jwt, _) = await GirisYap(fA, "alici1", AliciSifresi);
                (alici2Jwt, _) = await GirisYap(fA, "alici2", AliciSifresi);
                cihazlar = CihazEkle(fA, 2);
                k1 = await KurtarmaKodu(editor, EditorP1);
                raporlar = await Raporlar(editor);

                saat.Ilerlet(TimeSpan.FromMinutes(1));
                yedekAni = saat.GetUtcNow();
                zip = await YedekAl(fA);
                saat.Ilerlet(TimeSpan.FromMinutes(1));

                // Yedekten sonra (dizüstü çalındı):
                (await editor.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = EditorP1, yeniSifre = EditorP2 })).EnsureSuccessStatusCode();
                Assert.Equal(HttpStatusCode.Unauthorized, await OturumDurumu(fA, e1));
                (e2, _) = await GirisYap(fA, "editor", EditorP2);
                using var editor2 = Oturumlu(fA, e2);
                k2 = await KurtarmaKodu(editor2, EditorP2);
                (await editor2.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = YeniIzleyiciSifresi })).EnsureSuccessStatusCode();
                (await editor2.PutAsJsonAsync($"/api/alicilar/{alici1}", new AliciYaz("alici1", "alici1", null, Aktif: false))).EnsureSuccessStatusCode();
                (await editor2.PutAsJsonAsync($"/api/alicilar/{alici2}", new AliciYaz("alici2", "alici2", "alici2-yeni-sifresi"))).EnsureSuccessStatusCode();
                await AliciAc(editor2, "alici3");
                (await editor2.DeleteAsync($"/api/bildirimler/push/abonelikler/{cihazlar[0].Id}")).EnsureSuccessStatusCode();
                var kilit = (await editor2.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
                await MonthlyExpenseTests.Post<AyKilidiDto>(editor2, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, Temmuz.Year, Temmuz.Month, "Temmuz kapandı"));
            }
            finally { fA.Dispose(); }

            // Güvenlik günlüğü: kararlar veritabanı dışında, gizli bilgi olmadan.
            var gunluk = File.ReadAllText(gunlukYolu);
            var satirlar = gunluk.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonDocument.Parse(s).RootElement.Clone()).ToList();
            Assert.Equal(GuvenlikGunlugu.Basladi, satirlar[0].GetProperty("tur").GetString());
            var turler = satirlar.Select(s => s.GetProperty("tur").GetString()).ToList();
            foreach (var tur in new[] { "SifreDegisti", "KurtarmaKoduUretildi", "IzleyiciSifresiDegisti", "AliciOlusturuldu", "AliciGuncellendi", "PushAboneligiKaldirildi", "AyKilidiKapatildi" })
                Assert.Contains(tur, turler);
            Assert.Contains(satirlar, s => s.GetProperty("tur").GetString() == "AliciGuncellendi" && s.GetProperty("kullanici").GetString() == "alici1"
                && !s.GetProperty("ayrinti").GetProperty("aktif").GetBoolean() && !s.GetProperty("ayrinti").GetProperty("sifreDegisti").GetBoolean());
            Assert.Contains(satirlar, s => s.GetProperty("tur").GetString() == "AyKilidiKapatildi" && s.GetProperty("ayrinti").GetProperty("yeni").GetString() == "2026-07-31");
            var gizliler = new List<string> { EditorP1, EditorP2, k1, k2, EskiIzleyiciSifresi, YeniIzleyiciSifresi, AliciSifresi, "alici2-yeni-sifresi", "p256dh-anahtari", "auth-anahtari" };
            gizliler.AddRange(cihazlar.Select(c => c.Endpoint));
            gizliler.Add((string)Oku(canli, "SELECT \"SifreHash\" FROM \"EditorGuvenlik\" WHERE \"Id\" = 1;")!);
            gizliler.Add((string)Oku(canli, "SELECT \"IzleyiciSifreHash\" FROM \"Ayarlar\" LIMIT 1;")!);
            gizliler.Add((string)Oku(canli, "SELECT \"SifreHash\" FROM \"Alicilar\" WHERE \"Kullanici\" = 'alici2';")!);
            Assert.All(gizliler, g => Assert.DoesNotContain(g, gunluk, StringComparison.OrdinalIgnoreCase));

            // Yedek kopyası yedek anını taşır, canlı dosya taşımaz.
            KasaDbCikar(zip, geri);
            Assert.Equal(yedekAni, DateTimeOffset.Parse((string)Oku(geri, "SELECT \"YedekZamani\" FROM \"SistemDurumu\" WHERE \"Id\" = 1;")!, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Null(Oku(canli, "SELECT \"YedekZamani\" FROM \"SistemDurumu\" WHERE \"Id\" = 1;"));

            // Geri yükleme: operatör runbook'a göre KASA_EDITOR_SIFRE'yi yeni bir değere çevirip sıfırlama bayrağıyla başlatır.
            saat.Ilerlet(TimeSpan.FromMinutes(1));
            string e3;
            var fB = new Fabrika(geri, dizin, saat, gunluk: true, editorSifresi: YeniOrtamSifresi, sifirla: true);
            try
            {
                (e3, _) = await GirisYap(fB, "editor", YeniOrtamSifresi);
                // (a) Yedekten önce ve sonra alınmış bütün oturumlar geçersiz.
                foreach (var jwt in new[] { e1, e2, izleyiciJwt, alici1Jwt, alici2Jwt })
                    Assert.Equal(HttpStatusCode.Unauthorized, await OturumDurumu(fB, jwt));
                // (c) Yedekteki P1 yeniden geçerli olmaz; kaybolan P2 ve ilk kurulumun ortam şifresi de geçmez.
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "editor", EditorP1));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "editor", EditorP2));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "editor", "kasa123"));
                // (b) Yedekteki K1 de yedekten sonraki K2 de kurtarmaya yaramaz.
                Assert.Equal(HttpStatusCode.Unauthorized, await Kurtar(fB, k1));
                Assert.Equal(HttpStatusCode.Unauthorized, await Kurtar(fB, k2));
                // (d) İzleyici girişi kapalı.
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, null, EskiIzleyiciSifresi));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, null, YeniIzleyiciSifresi));
                using var editor = Oturumlu(fB, e3);
                Assert.False((await editor.GetFromJsonAsync<JsonElement>("/api/ayarlar")).GetProperty("izleyiciSifreVarMi").GetBoolean());
                // (e) Yedekten sonra pasife alınan ve şifresi değişen alıcı pasif: yedekteki şifreyle giremez.
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "alici1", AliciSifresi));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "alici2", AliciSifresi));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "alici2", "alici2-yeni-sifresi"));
                var alicilar = (await editor.GetFromJsonAsync<List<AliciDto>>("/api/alicilar"))!;
                Assert.Equal(new[] { "alici1", "alici2" }, alicilar.Select(a => a.Kullanici).Order());
                Assert.All(alicilar, a => Assert.False(a.Aktif));
                // (f) Bütün cihaz kayıtları (kaldırılmış kayıp cihaz dahil) kapalı.
                using (var scope = fB.Services.CreateScope())
                    Assert.All(scope.ServiceProvider.GetRequiredService<KasaDbContext>().Set<PushAbonelikEntity>().AsNoTracking().ToList(), p => Assert.False(p.Etkin));
                // (g) Yedek durumunda son geri yükleme ve rapor.
                var (son, rapor) = await YedekDurumu(editor);
                Assert.Equal(saat.GetUtcNow(), son);
                Assert.Equal($"Veritabanı {Yerel(yedekAni)} tarihli yedekten geri yüklendi. Bu andan sonra girilen kayıtlar yedekte yok; yeniden girilmeli.", rapor[0]);
                Assert.Contains("Bütün oturumlar kapatıldı; herkes yeniden giriş yapmalı.", rapor);
                Assert.Contains(GeriYuklemeIsleyici.EditorGirisiKilitlendi, rapor);
                Assert.Equal($"Editör girişi {Yerel(saat.GetUtcNow())} tarihinde açıldı: şifre sunucudaki KASA_EDITOR_SIFRE'ye sıfırlandı "
                    + "(Kasa:EditorSifreSifirla). Editör şifresini değiştirmediyse hemen değiştirmeli.", rapor[^1]);
                Assert.Contains(rapor, m => m.StartsWith("Kurtarma kodu iptal edildi", StringComparison.Ordinal));
                Assert.Contains(rapor, m => m.StartsWith("İzleyici girişi kapatıldı", StringComparison.Ordinal));
                Assert.Contains(rapor, m => m.StartsWith("'alici1' alıcısı yedekten sonra pasife alınmıştı", StringComparison.Ordinal));
                Assert.Contains(rapor, m => m.StartsWith("'alici2' alıcısı yedekten sonra şifresi değiştirilmişti", StringComparison.Ordinal));
                Assert.Contains(rapor, m => m.StartsWith("'alici3' alıcı hesabı yedekten sonra açılmıştı", StringComparison.Ordinal));
                Assert.Contains("2 cihazın bildirim kaydı kapatıldı: bildirim kullanan cihazlarda bildirimleri yeniden açın.", rapor);
                Assert.DoesNotContain(rapor, m => m.Contains("Güvenlik günlüğü", StringComparison.Ordinal));
                var olay = GeriYuklemeOlayi(fB);
                Assert.True(olay.GetProperty("editorGirisiKilitlendi").GetBoolean());
                Assert.True(Assert.Single(SifirlamaOlaylari(fB)).GetProperty("girisKilidiKaldirildi").GetBoolean());
                Assert.True(olay.GetProperty("kurtarmaKoduIptal").GetBoolean());
                Assert.Equal("tam", olay.GetProperty("guvenlikGunlugu").GetString());
                Assert.Equal("yedek", olay.GetProperty("yedekAniKaynagi").GetString());
                Assert.Equal(2, olay.GetProperty("pasifAlicilar").GetArrayLength());
                Assert.Equal(2, olay.GetProperty("bildirimKayitlariKapatildi").GetInt32());
                // (l) Kasa kayıtları ve raporlar yedek anındakiyle birebir aynı.
                var sonraki = await Raporlar(editor);
                Assert.Equal(raporlar.Keys, sonraki.Keys);
                foreach (var (yol, once) in raporlar)
                    Assert.True(once == sonraki[yol], $"{yol} değişti:\n{once}\n{sonraki[yol]}");
            }
            finally { fB.Dispose(); }
            Assert.Contains(File.ReadAllLines(gunlukYolu), s => s.Contains("\"tur\":\"GeriYuklemeIslendi\"", StringComparison.Ordinal));

            // (h) Yeniden başlatma işlemi tekrarlamaz: geri yüklemeden sonra açılan oturum geçerli, tek olay. Bayrak açık unutulsa da
            // aynı ortam şifresiyle sıfırlama yeniden uygulanmaz (oturum düşmez).
            var fC = new Fabrika(geri, dizin, saat, gunluk: true, editorSifresi: YeniOrtamSifresi, sifirla: true);
            try
            {
                Assert.Equal(HttpStatusCode.OK, await OturumDurumu(fC, e3));
                GeriYuklemeOlayi(fC);
                Assert.Single(SifirlamaOlaylari(fC));
            }
            finally { fC.Dispose(); }

            // Aynı yedeğin ikinci kez geri yüklenmesi (bu kez bayraksız): veritabanındaki şifre ve sürümler ilk geri yüklemedekiyle
            // aynıdır; yeni oturum dönemi ilk geri yüklemeden sonra açılan oturumu da düşürür. Günlük yedekten sonraki şifre
            // değişikliğini ve sıfırlamayı gösterdiği için editör girişi yeniden kilitlenir: ortam şifresi dahil hiçbir şifre geçmez.
            KasaDbCikar(zip, ikinci);
            saat.Ilerlet(TimeSpan.FromMinutes(1));
            var fD = new Fabrika(ikinci, dizin, saat, gunluk: true, editorSifresi: YeniOrtamSifresi);
            try
            {
                Assert.Equal(HttpStatusCode.Unauthorized, await OturumDurumu(fD, e3));
                foreach (var sifre in new[] { YeniOrtamSifresi, EditorP1, EditorP2, "kasa123" })
                    Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fD, "editor", sifre));
                Assert.True(GeriYuklemeOlayi(fD).GetProperty("editorGirisiKilitlendi").GetBoolean());
            }
            finally { fD.Dispose(); }
            // Operatör yeni ortam şifresiyle sıfırlar: kilit kalkar, önceki ortam şifresi geçmez.
            var fE = new Fabrika(ikinci, dizin, saat, gunluk: true, editorSifresi: IkinciOrtamSifresi, sifirla: true);
            try
            {
                Assert.Equal(HttpStatusCode.OK, await GirisDurumu(fE, "editor", IkinciOrtamSifresi));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fE, "editor", YeniOrtamSifresi));
            }
            finally { fE.Dispose(); }
        }
        finally { Temizle([canli, geri, ikinci], dizin); }
    }

    /// <summary>
    /// Güvenlik günlüğü yoksa (ör. sunucu kaybında yedek dizini de gitti) yedekten sonraki kararlar bilinemez: koşulsuz adımlar
    /// (oturumlar, kurtarma kodu, izleyici, cihazlar) yine uygulanır; yedek anındaki editör şifresi geçerli kalır ve rapor şifrenin
    /// hemen değiştirilmesini ister (runbook'taki operatör adımı).
    /// </summary>
    [Fact]
    public async Task Guvenlik_gunlugu_yoksa_kosulsuz_adimlar_uygulanir_ve_rapor_sifre_degisikligini_ister()
    {
        var dizin = GeciciYol("");
        var canli = GeciciYol(".db");
        var geri = GeciciYol(".db");
        try
        {
            string zip, k1;
            var fA = new Fabrika(canli, dizin);
            try
            {
                using (var ilk = Oturumlu(fA, (await GirisYap(fA, "editor", "kasa123")).Jwt))
                    (await ilk.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = EditorP1 })).EnsureSuccessStatusCode();
                using var editor = Oturumlu(fA, (await GirisYap(fA, "editor", EditorP1)).Jwt);
                k1 = await KurtarmaKodu(editor, EditorP1);
                zip = await YedekAl(fA);
                (await editor.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = EditorP1, yeniSifre = EditorP2 })).EnsureSuccessStatusCode();
            }
            finally { fA.Dispose(); }
            Assert.False(File.Exists(Path.Combine(dizin, GuvenlikGunlugu.DosyaAdi)));

            KasaDbCikar(zip, geri);
            var fB = new Fabrika(geri, dizin, gunluk: true);
            try
            {
                Assert.Equal(HttpStatusCode.Unauthorized, await Kurtar(fB, k1));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "editor", EditorP2));
                using var editor = Oturumlu(fB, (await GirisYap(fB, "editor", EditorP1)).Jwt);
                var (_, rapor) = await YedekDurumu(editor);
                Assert.Contains(rapor, m => m.StartsWith("Güvenlik günlüğü bulunamadı", StringComparison.Ordinal) && m.Contains("Editör şifresini hemen değiştirin", StringComparison.Ordinal));
                Assert.DoesNotContain(GeriYuklemeIsleyici.EditorGirisiKilitlendi, rapor);
                var olay = GeriYuklemeOlayi(fB);
                Assert.Equal("bulunamadi", olay.GetProperty("guvenlikGunlugu").GetString());
                Assert.False(olay.GetProperty("editorGirisiKilitlendi").GetBoolean());
            }
            finally { fB.Dispose(); }
            // Günlük açılışta oluşturulur ve geri yüklemeyi kaydeder.
            Assert.Contains(File.ReadAllLines(Path.Combine(dizin, GuvenlikGunlugu.DosyaAdi)), s => s.Contains("\"tur\":\"GeriYuklemeIslendi\"", StringComparison.Ordinal));
        }
        finally { Temizle([canli, geri], dizin); }
    }

    /// <summary>
    /// Yayın kimseyi düşürmez: SistemDurumu'nu ekleyen migration oturum dönemini boş tohumlar ve boş dönemde damga bu sürümden
    /// öncekiyle birebir aynıdır. Önceki sürümün açtığı editör, izleyici ve alıcı oturumları, tanıdık cihaz belirteci ve bildirim
    /// aboneliği güncellemeden sonra geçerli kalır; geri yükleme işlemi çalışmaz.
    /// </summary>
    [Fact]
    public async Task Yayin_mevcut_oturumlari_tanidik_cihazlari_ve_bildirim_aboneliklerini_dusurmez()
    {
        var dizin = GeciciYol("");
        var yol = GeciciYol(".db");
        try
        {
            string editorJwt, izleyiciJwt, aliciJwt, cihaz, abonelikDamgasi;
            var fA = new Fabrika(yol, dizin);
            try
            {
                (editorJwt, var c) = await GirisYap(fA, "editor", "kasa123");
                cihaz = c!;
                using var editor = Oturumlu(fA, editorJwt);
                (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = EskiIzleyiciSifresi })).EnsureSuccessStatusCode();
                (izleyiciJwt, _) = await GirisYap(fA, null, EskiIzleyiciSifresi);
                await AliciAc(editor, "alici1");
                (aliciJwt, _) = await GirisYap(fA, "alici1", AliciSifresi);
                CihazEkle(fA, 1);
                using var scope = fA.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                abonelikDamgasi = db.Set<PushAbonelikEntity>().AsNoTracking().Single().OturumDamgasi;
                // Damgalar bu sürümden önceki formülle (dönemsiz HMAC) birebir aynı.
                var izleyiciHash = db.Ayarlar.AsNoTracking().Single().IzleyiciSifreHash!;
                var alici = db.Alicilar.AsNoTracking().Single();
                Assert.Equal(EskiDamga("editor\neditor\nkasa123"), Damga(editorJwt));
                Assert.Equal(EskiDamga("editor\neditor\nkasa123"), abonelikDamgasi);
                Assert.Equal(EskiDamga(izleyiciHash), Damga(izleyiciJwt));
                Assert.Equal(EskiDamga($"alici\n{alici.Id}\n{alici.Kullanici}\n{alici.SifreHash}\n{alici.OturumSurumu}"), Damga(aliciJwt));
            }
            finally { fA.Dispose(); }

            // Güncellemeden önceki veritabanı: SistemDurumu yok, migration'ları uygulanmamış.
            Calistir(yol, "DROP TABLE \"SistemDurumu\"; DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" IN "
                + $"('{Kasa.Api.Migrations.GeriYuklemeGuvenligi.Kimlik}', '{Kasa.Api.Migrations.EditorSifirlamaIzi.Kimlik}');");
            var fB = new Fabrika(yol, dizin);
            try
            {
                foreach (var jwt in new[] { editorJwt, izleyiciJwt, aliciJwt })
                    Assert.Equal(HttpStatusCode.OK, await OturumDurumu(fB, jwt));
                using var scope = fB.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var durum = db.SistemDurumu.AsNoTracking().Single();
                Assert.Equal("", durum.OturumDonemi);
                Assert.Null(durum.SonGeriYukleme);
                Assert.Null(durum.GeriYuklemeRaporu);
                Assert.Null(durum.EditorSifirlamaIzi);
                var istek = new DefaultHttpContext().Request;
                istek.Headers[TanidikCihaz.BaslikAdi] = cihaz;
                Assert.NotNull(fB.Services.GetRequiredService<TanidikCihaz>().Dogrula(istek, GirisSiniri.EditorHedefi, OturumDamgasi.Uret("editor", cfg, db)));
                Assert.True(OturumDamgasi.Esit(abonelikDamgasi, OturumDamgasi.Uret("editor", cfg, db)));
                Assert.Empty(db.DenetimOlaylari.AsNoTracking().Where(o => o.Tur == OlayTuru).ToList());
            }
            finally { fB.Dispose(); }
        }
        finally { Temizle([yol], dizin); }
    }

    private static string EskiDamga(string kaynak) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(JwtAnahtari), Encoding.UTF8.GetBytes(kaynak)));

    /// <summary>JWT'nin oturum damgası (kasa_session) talebi.</summary>
    private static string Damga(string jwt)
    {
        var govde = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        govde = govde.PadRight(govde.Length + (4 - govde.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(govde)).RootElement.GetProperty(OturumDamgasi.ClaimAdi).GetString()!;
    }

    [Fact]
    public async Task Isaretsiz_canli_veritabani_yeniden_acilista_degismez()
    {
        var dizin = GeciciYol("");
        var yol = GeciciYol(".db");
        try
        {
            string editorJwt;
            var f1 = new Fabrika(yol, dizin, gunluk: true);
            try
            {
                editorJwt = (await GirisYap(f1, "editor", "kasa123")).Jwt;
                using var editor = Oturumlu(f1, editorJwt);
                (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = EskiIzleyiciSifresi })).EnsureSuccessStatusCode();
            }
            finally { f1.Dispose(); }
            var tabanlar = KimlikTabanlari(yol);
            var f2 = new Fabrika(yol, dizin, gunluk: true);
            try
            {
                Assert.Equal(HttpStatusCode.OK, await GirisDurumu(f2, null, EskiIzleyiciSifresi));
                Assert.Equal(HttpStatusCode.OK, await OturumDurumu(f2, editorJwt));
                using var scope = f2.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                Assert.Empty(db.DenetimOlaylari.AsNoTracking().Where(o => o.Tur == OlayTuru).ToList());
                var durum = db.SistemDurumu.AsNoTracking().Single();
                Assert.Equal("", durum.OturumDonemi);
                Assert.Null(durum.SonGeriYukleme);
            }
            finally { f2.Dispose(); }
            IleriAlinmadi(tabanlar, KimlikTabanlari(yol));
            Assert.All(KimlikTabanlari(yol).Values, v => Assert.True(v < KimlikAraligi));
            // Günlük açılışta oluşturulur; olağan açılış geri yükleme yazmaz.
            var satirlar = File.ReadAllLines(Path.Combine(dizin, GuvenlikGunlugu.DosyaAdi));
            Assert.Contains("\"tur\":\"GunlukBasladi\"", satirlar[0], StringComparison.Ordinal);
            Assert.DoesNotContain(satirlar, s => s.Contains("GeriYuklemeIslendi", StringComparison.Ordinal));
        }
        finally { Temizle([yol], dizin); }
    }

    /// <summary>
    /// Bu sürümden önce alınmış (işaretsiz, SistemDurumu'suz) yedek: restore_backup.py geri açarken işaretler ve manifestteki yedek
    /// anını işaret tablosuna yazar; uygulama ilk açılışta migration'dan sonra işler ve güvenlik günlüğünü o andan keser: yedekten
    /// önceki şifre değişikliği yeniden uygulanmaz (editör yedekteki şifreyle girer), yedekten sonra pasife alınan alıcı pasif
    /// kalır. Ortamda Python yoksa sınama atlanır (araç deploy/tests altında ayrıca sınanır).
    /// </summary>
    [Fact]
    public async Task Isaretsiz_eski_yedek_restore_araciyla_isaretlenir_ve_ilk_acilista_yedek_anindan_islenir()
    {
        var dizin = GeciciYol("");
        var canli = GeciciYol(".db");
        var geri = GeciciYol(".db");
        var eskiDb = GeciciYol(".db");
        var saat = new SabitSaat(KasaWebFactory.VarsayilanBugun);
        try
        {
            string zip;
            DateTimeOffset yedekAni;
            var fA = new Fabrika(canli, dizin, saat, gunluk: true);
            try
            {
                using (var ilk = Oturumlu(fA, (await GirisYap(fA, "editor", "kasa123")).Jwt))
                    (await ilk.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = EditorP1 })).EnsureSuccessStatusCode();
                using var editor = Oturumlu(fA, (await GirisYap(fA, "editor", EditorP1)).Jwt);
                (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = EskiIzleyiciSifresi })).EnsureSuccessStatusCode();
                var alici = await AliciAc(editor, "alici1");
                saat.Ilerlet(TimeSpan.FromMinutes(1));
                yedekAni = saat.GetUtcNow();
                zip = await YedekAl(fA);
                saat.Ilerlet(TimeSpan.FromMinutes(1));
                (await editor.PutAsJsonAsync($"/api/alicilar/{alici}", new AliciYaz("alici1", "alici1", null, Aktif: false))).EnsureSuccessStatusCode();
            }
            finally { fA.Dispose(); }

            // Eski biçim: işaretsiz, SistemDurumu'suz kasa.db ve ona göre manifest özeti (2.3 öncesi uygulamanın yazdığı gibi).
            KasaDbCikar(zip, eskiDb);
            Calistir(eskiDb, "PRAGMA user_version = 0; DROP TABLE \"SistemDurumu\"; DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" IN "
                + $"('{Kasa.Api.Migrations.GeriYuklemeGuvenligi.Kimlik}', '{Kasa.Api.Migrations.EditorSifirlamaIzi.Kimlik}');");
            var eskiZip = Path.Combine(dizin, "kasa-elle-20260920-030000-0a1b2c3d.zip");
            JsonNode manifest;
            using (var arsiv = ZipFile.OpenRead(zip))
            using (var akis = arsiv.GetEntry("manifest.json")!.Open())
                manifest = JsonNode.Parse(akis)!;
            manifest["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(eskiDb)));
            // 2.3 öncesinin biçimi (2.1.0): belge listesi girdisi ve belge deposu alanları yoktur.
            manifest["surum"] = "2.1.0";
            manifest["belgelerDahil"] = true;
            foreach (var alan in new[] { "belgeDeposu", "belgeSayisi", "belgeToplamBayt", "belgeListesiSha256", "eksikBelgeSayisi", "belgelerGomulu" })
                manifest.AsObject().Remove(alan);
            using (var yeni = ZipFile.Open(eskiZip, ZipArchiveMode.Create))
            {
                yeni.CreateEntryFromFile(eskiDb, "kasa.db");
                using var akis = yeni.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(akis, manifest);
            }
            Assert.Equal(0, UserVersion(eskiDb));

            var cikti = RestoreAraci(eskiZip, geri);
            if (cikti is null)
                return; // Python yok
            Assert.Contains("izleyici şifresi", cikti);
            Assert.Equal(Isaret, UserVersion(geri));
            Assert.Equal(manifest["olusturuldu"]!.GetValue<string>(), Oku(geri, $"SELECT \"YedekZamani\" FROM \"{IsaretTablosu}\";"));
            Assert.Equal(yedekAni, DateTimeOffset.Parse(manifest["olusturuldu"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));

            saat.Ilerlet(TimeSpan.FromMinutes(1));
            var fB = new Fabrika(geri, dizin, saat, gunluk: true);
            try
            {
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, null, EskiIzleyiciSifresi));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "alici1", AliciSifresi));
                // Yedekten önceki şifre değişikliği (günlükte) yeniden uygulanmaz: yedek anı işaret tablosundan okundu.
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "editor", "kasa123"));
                using var editor = Oturumlu(fB, (await GirisYap(fB, "editor", EditorP1)).Jwt);
                var (_, rapor) = await YedekDurumu(editor);
                Assert.StartsWith($"Veritabanı {Yerel(yedekAni)} tarihli yedekten geri yüklendi.", rapor[0], StringComparison.Ordinal);
                Assert.Contains(rapor, m => m.StartsWith("'alici1' alıcısı yedekten sonra pasife alınmıştı", StringComparison.Ordinal));
                Assert.DoesNotContain(GeriYuklemeIsleyici.EditorGirisiKilitlendi, rapor);
                Assert.Equal("restore_backup.py", GeriYuklemeOlayi(fB).GetProperty("yedekAniKaynagi").GetString());
            }
            finally { fB.Dispose(); }
            Assert.Equal(0, UserVersion(geri));
            Assert.False(TabloVar(geri, IsaretTablosu));
        }
        finally { Temizle([canli, geri, eskiDb], dizin); }
    }

    /// <summary>
    /// gap-geri-yukleme-durum-geri-sarma-10: yedek anında editör şifresi P1 ve kurtarma kodu K1. Yedekten sonra operatör şifreyi
    /// sıfırlar (ör. P1 ele geçti; güvenlik günlüğünde EditorSifresiSifirlandi). Yedek, bayrak kaldırılmış ve ortam şifresi ilk
    /// kurulumdaki değerde ('kasa123') bırakılmışken geri yüklenir: yedekteki P1 yeniden geçerli olmaz, ilk kurulumun ortam şifresi
    /// de sıfırlamada kullanılan şifre de geçmez, K1 kurtarmaya yaramaz: editör girişi kilitlidir. Operatör yeni ortam şifresiyle
    /// sıfırlayınca kilit kalkar; geri yükleme raporu kilidi ve açılışını söyler.
    /// </summary>
    [Fact]
    public async Task Yedekten_sonraki_sifirlama_geri_yuklemede_girisi_kilitler_ilk_kurulum_sifresi_gecmez()
    {
        var dizin = GeciciYol("");
        var canli = GeciciYol(".db");
        var geri = GeciciYol(".db");
        var saat = new SabitSaat(KasaWebFactory.VarsayilanBugun);
        try
        {
            string zip, k1;
            var fA = new Fabrika(canli, dizin, saat, gunluk: true);
            try
            {
                using (var ilk = Oturumlu(fA, (await GirisYap(fA, "editor", "kasa123")).Jwt))
                    (await ilk.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = EditorP1 })).EnsureSuccessStatusCode();
                using var editor = Oturumlu(fA, (await GirisYap(fA, "editor", EditorP1)).Jwt);
                k1 = await KurtarmaKodu(editor, EditorP1);
                saat.Ilerlet(TimeSpan.FromMinutes(1));
                zip = await YedekAl(fA);
                saat.Ilerlet(TimeSpan.FromMinutes(1));
            }
            finally { fA.Dispose(); }
            var fS = new Fabrika(canli, dizin, saat, gunluk: true, editorSifresi: YeniOrtamSifresi, sifirla: true);
            try
            {
                Assert.Equal(HttpStatusCode.OK, await GirisDurumu(fS, "editor", YeniOrtamSifresi));
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fS, "editor", EditorP1));
            }
            finally { fS.Dispose(); }
            Assert.Contains(File.ReadAllLines(Path.Combine(dizin, GuvenlikGunlugu.DosyaAdi)),
                s => s.Contains($"\"tur\":\"{GuvenlikGunlugu.EditorSifresiSifirlandi}\"", StringComparison.Ordinal));

            KasaDbCikar(zip, geri);
            saat.Ilerlet(TimeSpan.FromMinutes(1));
            var fB = new Fabrika(geri, dizin, saat, gunluk: true);
            try
            {
                foreach (var sifre in new[] { EditorP1, "kasa123", YeniOrtamSifresi })
                    Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fB, "editor", sifre));
                Assert.Equal(HttpStatusCode.Unauthorized, await Kurtar(fB, k1));
                Assert.True(GeriYuklemeOlayi(fB).GetProperty("editorGirisiKilitlendi").GetBoolean());
            }
            finally { fB.Dispose(); }

            saat.Ilerlet(TimeSpan.FromMinutes(1));
            var fC = new Fabrika(geri, dizin, saat, gunluk: true, editorSifresi: IkinciOrtamSifresi, sifirla: true);
            try
            {
                using var editor = Oturumlu(fC, (await GirisYap(fC, "editor", IkinciOrtamSifresi)).Jwt);
                Assert.Equal(HttpStatusCode.Unauthorized, await GirisDurumu(fC, "editor", EditorP1));
                var (_, rapor) = await YedekDurumu(editor);
                Assert.Contains(GeriYuklemeIsleyici.EditorGirisiKilitlendi, rapor);
                Assert.StartsWith($"Editör girişi {Yerel(saat.GetUtcNow())} tarihinde açıldı", rapor[^1], StringComparison.Ordinal);
                Assert.True(Assert.Single(SifirlamaOlaylari(fC)).GetProperty("girisKilidiKaldirildi").GetBoolean());
            }
            finally { fC.Dispose(); }
        }
        finally { Temizle([canli, geri], dizin); }
    }

    [Fact]
    public void Sabitler_sunucu_ve_restore_araci_arasinda_aynidir()
    {
        Assert.Equal((Isaret, KimlikAraligi, OlayTuru, IsaretTablosu),
            (GeriYuklemeIsleyici.Isaret, GeriYuklemeIsleyici.KimlikAraligi, GeriYuklemeIsleyici.OlayTuru, GeriYuklemeIsleyici.IsaretTablosu));
        var arac = File.ReadAllText(AracYolu());
        var isaret = Regex.Match(arac, @"^GERI_YUKLEME_ISARETI = 0x([0-9A-Fa-f]+)\r?$", RegexOptions.Multiline);
        var aralik = Regex.Match(arac, @"^KIMLIK_ARALIGI = ([0-9_]+)\r?$", RegexOptions.Multiline);
        var tablo = Regex.Match(arac, @"^GERI_YUKLEME_TABLOSU = ""([^""]+)""\r?$", RegexOptions.Multiline);
        Assert.True(isaret.Success && aralik.Success && tablo.Success, "restore_backup.py sabitleri bulunamadı.");
        Assert.Equal(GeriYuklemeIsleyici.Isaret, Convert.ToInt32(isaret.Groups[1].Value, 16));
        Assert.Equal(GeriYuklemeIsleyici.KimlikAraligi, int.Parse(aralik.Groups[1].Value.Replace("_", ""), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(GeriYuklemeIsleyici.IsaretTablosu, tablo.Groups[1].Value);
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
                var bilgi = new ProcessStartInfo(python)
                {
                    ArgumentList = { arac, zip, "--output", cikti },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                bilgi.Environment["PYTHONIOENCODING"] = "utf-8";
                p = Process.Start(bilgi);
            }
            catch (System.ComponentModel.Win32Exception) { continue; }
            if (p is null)
                continue;
            using (p)
            {
                var cikis = p.StandardOutput.ReadToEndAsync();
                var hata = p.StandardError.ReadToEndAsync();
                Assert.True(p.WaitForExit(60_000), "restore_backup.py zamanında bitmedi.");
                // Windows'taki 'python3' uygulama mağazası kısayolu olabilir (9009): gerçek Python değilse sonrakine geç.
                if (p.ExitCode == 9009)
                    continue;
                Assert.True(p.ExitCode == 0, $"restore_backup.py yedeği reddetti: {hata.Result} {cikis.Result}");
                return cikis.Result;
            }
        }
        return null;
    }
}
