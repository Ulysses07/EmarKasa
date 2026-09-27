using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using static Kasa.Api.Tests.BildirimIscisiTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Veritabanı hatalarının tek sınıflandırıcısı (gap-okuma-yolu-maliyet-kilit-cekismesi-5, gap-denetim-izi-gozlemlenebilirlik-11):
/// kilit beklemesi 503 + Retry-After, UNIQUE çakışması 409, kodun doğrulamadığı FK ihlali 500; 409/503'e çevrilen hata
/// 'Kasa.Veritabani' kategorisinde uç, rol ve iz kimliğiyle Warning olarak loglanır. Hatalar gerçek SQLite hatalarıdır:
/// yazma kilidini ikinci bir bağlantı tutar, kısıt ihlallerini testin eklediği tetikleyiciler üretir.
/// Bağlantı düzeyi kilit beklemesi (gap-okuma-yolu-maliyet-kilit-cekismesi-7) ve üretim log düzeyleri
/// (gap-denetim-izi-gozlemlenebilirlik-13) de burada sınanır.
/// </summary>
public sealed class VeritabaniHataTests
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun;

    [Fact]
    public async Task Yazma_kilidi_alinamazsa_Mutate_ucu_503_RetryAfter_ve_uyari_logu_doner()
    {
        await using var f = new HataFabrikasi();
        using var c = await f.EditorClientAsync();
        var sure = Stopwatch.StartNew();
        HttpResponseMessage yanit;
        using (f.YazmaKilidiTut())
            yanit = await c.PutAsJsonAsync("/api/kasa-esikleri/1", new { surum = 0, tutar = 100m, etkin = true });
        sure.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, yanit.StatusCode);
        Assert.Equal("2", yanit.Headers.GetValues("Retry-After").Single());
        Assert.Contains("Birkaç saniye sonra tekrar deneyin", await Hata(yanit));
        // Bağlantı dizesindeki bekleme süresi (1 sn) uygulanır; sürücünün varsayılan 30 sn'si değil.
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(15), $"Kilit beklemesi {sure.Elapsed} sürdü.");
        var kayit = Assert.Single(f.Log.Kayitlar, x => x.Kategori == "Kasa.Veritabani");
        Assert.Equal(LogLevel.Warning, kayit.Seviye);
        Assert.Contains("Mesgul → 503", kayit.Mesaj);
        Assert.Contains("SQLite 5/", kayit.Mesaj);
        Assert.Contains("uç PUT /api/kasa-esikleri/{kanalId:int}", kayit.Mesaj);
        Assert.Contains("rol editor", kayit.Mesaj);
        Assert.Matches(@"iz \S+", kayit.Mesaj);

        // Kilit bırakılınca aynı istek başarılı olur (meşgul yanıtı hiçbir şey yazmamıştır).
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/kasa-esikleri/1", new { surum = 0, tutar = 100m, etkin = true })).StatusCode);
    }

    [Fact]
    public async Task Yazma_kilidi_alinamazsa_genel_isleyicideki_uc_da_503_doner()
    {
        await using var f = new HataFabrikasi();
        using var c = await f.EditorClientAsync();
        HttpResponseMessage yanit;
        using (f.YazmaKilidiTut())
            yanit = await c.PostAsJsonAsync("/api/islemler", Islem());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, yanit.StatusCode);
        Assert.Equal("2", yanit.Headers.GetValues("Retry-After").Single());
        Assert.Contains("Birkaç saniye sonra tekrar deneyin", await Hata(yanit));
        var kayit = Assert.Single(f.Log.Kayitlar, x => x.Kategori == "Kasa.Veritabani");
        Assert.Equal(LogLevel.Warning, kayit.Seviye);
        Assert.Contains("uç POST /api/islemler", kayit.Mesaj);
        Assert.Contains("rol editor", kayit.Mesaj);
    }

    [Fact]
    public async Task Unique_ihlali_her_iki_yolda_409_genel_cakisma_iletisi_ve_uyari_logu_doner()
    {
        await using var f = new HataFabrikasi();
        using var c = await f.EditorClientAsync();
        // Her yazma, var olan bir kanalın adını yeniden ekler: Kanallar.Ad tekil dizini gerçek bir UNIQUE ihlali üretir.
        f.Sql("""
            CREATE TRIGGER test_esik_cakisma AFTER INSERT ON KasaEsikleri BEGIN
              INSERT INTO Kanallar (Ad, Aktif, Sira, AcilisDevri) SELECT Ad, Aktif, Sira, AcilisDevri FROM Kanallar WHERE Id = NEW.KanalId;
            END;
            CREATE TRIGGER test_islem_cakisma AFTER INSERT ON Islemler BEGIN
              INSERT INTO Kanallar (Ad, Aktif, Sira, AcilisDevri) SELECT Ad, Aktif, Sira, AcilisDevri FROM Kanallar ORDER BY Id LIMIT 1;
            END;
            """);

        var mutate = await c.PutAsJsonAsync("/api/kasa-esikleri/1", new { surum = 0, tutar = 100m, etkin = true });
        var genel = await c.PostAsJsonAsync("/api/islemler", Islem());

        foreach (var yanit in new[] { mutate, genel })
        {
            Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
            // İleti alışa özgü değildir ("ödeme kaydedilmiş" denmez).
            Assert.Equal("Kayıt başka bir kayıtla çakışıyor. Listeyi yenileyip tekrar deneyin.", await Hata(yanit));
        }
        var kayitlar = f.Log.Kayitlar.Where(x => x.Kategori == "Kasa.Veritabani").ToList();
        Assert.Equal(2, kayitlar.Count);
        Assert.All(kayitlar, x =>
        {
            Assert.Equal(LogLevel.Warning, x.Seviye);
            Assert.Contains("Cakisma → 409", x.Mesaj);
            Assert.Contains("SQLite 19/2067", x.Mesaj);
            Assert.Contains("UNIQUE constraint failed: Kanallar.Ad", x.Mesaj);
        });
        Assert.Contains(kayitlar, x => x.Mesaj.Contains("uç PUT /api/kasa-esikleri/{kanalId:int}", StringComparison.Ordinal));
        Assert.Contains(kayitlar, x => x.Mesaj.Contains("uç POST /api/islemler", StringComparison.Ordinal));
        using var db = f.Baglam();
        Assert.Equal(0, db.KasaEsikleri.Count());
        Assert.Equal(0, db.Islemler.Count());
    }

    [Fact]
    public async Task Fk_ihlali_cakisma_diye_ortulmez_500_ProblemDetails_ve_hata_logu_doner()
    {
        await using var f = new HataFabrikasi();
        using var c = await f.EditorClientAsync();
        // Kodun doğrulamadığı bir yazma var olmayan kanala bağlanır: gerçek FOREIGN KEY ihlali (787).
        f.Sql("""
            CREATE TRIGGER test_esik_fk AFTER INSERT ON KasaEsikleri BEGIN
              UPDATE KasaEsikleri SET KanalId = 999999 WHERE Id = NEW.Id;
            END;
            CREATE TRIGGER test_islem_fk AFTER INSERT ON Islemler BEGIN
              UPDATE Islemler SET KanalId = 999999 WHERE Id = NEW.Id;
            END;
            """);

        var mutate = await c.PutAsJsonAsync("/api/kasa-esikleri/1", new { surum = 0, tutar = 100m, etkin = true });
        var genel = await c.PostAsJsonAsync("/api/islemler", Islem());

        foreach (var yanit in new[] { mutate, genel })
        {
            Assert.Equal(HttpStatusCode.InternalServerError, yanit.StatusCode);
            var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Veri bütünlüğü hatası", govde.GetProperty("title").GetString());
            Assert.Contains("iz kimliğini", govde.GetProperty("detail").GetString());
            var iz = govde.GetProperty("traceId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(iz));
            // Kullanıcının bildireceği iz kimliği sunucu kaydında aranabilir.
            Assert.Contains(f.Log.Kayitlar, x => x.Kategori == "Kasa.Veritabani" && x.Mesaj.Contains($"iz {iz}.", StringComparison.Ordinal));
        }
        var kayitlar = f.Log.Kayitlar.Where(x => x.Kategori == "Kasa.Veritabani").ToList();
        Assert.Equal(2, kayitlar.Count);
        Assert.All(kayitlar, x =>
        {
            Assert.Equal(LogLevel.Error, x.Seviye);
            Assert.Contains("Butunluk → 500", x.Mesaj);
            Assert.Contains("SQLite 19/787", x.Mesaj);
            Assert.NotNull(x.Istisna);
        });
    }

    [Fact]
    public async Task Kilitli_doneme_yazma_denemesi_409_ve_uyari_olarak_gorunur()
    {
        await using var f = new HataFabrikasi();
        using var c = await f.EditorClientAsync();
        var gecen = new DateOnly(Bugun.Year, Bugun.Month, 1).AddMonths(-1);
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = gecen.AddMonths(-2), kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        var durum = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        (await c.PostAsJsonAsync("/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), durum.Surum, gecen.Year, gecen.Month, "Ay tamamlandı"))).EnsureSuccessStatusCode();

        var yanit = await c.PostAsJsonAsync("/api/islemler", Islem() with { Tarih = gecen });

        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
        Assert.Contains("kilitli", await Hata(yanit));
        var kayit = Assert.Single(f.Log.Kayitlar, x => x.Kategori == "Kasa.Veritabani");
        Assert.Equal(LogLevel.Warning, kayit.Seviye);
        Assert.Contains("KilitliDonem → 409", kayit.Mesaj);
        Assert.Contains("uç POST /api/islemler", kayit.Mesaj);
        Assert.Contains("rol editor", kayit.Mesaj);
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData(";Default Timeout=1", 1)]
    [InlineData(";Command Timeout=3", 3)]
    public void Kilit_beklemesi_baglanti_duzeyinde_busy_timeout_olarak_uygulanir(string ek, int saniye)
    {
        var yol = Path.Combine(Path.GetTempPath(), $"kasa-bekleme-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite($"Data Source={yol};Pooling=False{ek}").Options);
            db.Database.OpenConnection();
            var baglanti = (SqliteConnection)db.Database.GetDbConnection();
            // Sürücünün Thread.Sleep yoklaması yerine SQLite'ın kendi bekleyicisi; açıkça verilmeyen süre 30 sn değil 10 sn.
            Assert.Equal(saniye, baglanti.DefaultTimeout);
            using var komut = baglanti.CreateCommand();
            komut.CommandText = "PRAGMA busy_timeout";
            Assert.Equal(saniye * 1000L, Convert.ToInt64(komut.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
            db.Database.CloseConnection();
        }
        finally
        {
            foreach (var ek2 in new[] { "", "-wal", "-shm", "-journal" })
                try { File.Delete(yol + ek2); } catch (IOException) { }
        }
    }

    [Fact]
    public void Uygulama_thread_havuzunu_kilit_beklemesine_yetecek_iscilerle_baslatir()
    {
        // 0,5 CPU'lu konteynerde havuz tek işçiyle başlar ve yeni işçiyi yarım saniyede bir ekler; kilit bekleyen (uyuyan)
        // birkaç istek statik dosya ve /api/auth/me gibi hafif istekleri de bekletir. En az işçi sayısı çalışma ayarıyla
        // (Kasa.Api runtimeconfig) verilir; Program.cs'ye bağlı değildir.
        using var ayar = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Kasa.Api.runtimeconfig.json")));
        var ozellikler = ayar.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
        Assert.True(ozellikler.TryGetProperty("System.Threading.ThreadPool.MinThreads", out var enAz), "System.Threading.ThreadPool.MinThreads tanımlı değil.");
        Assert.True(enAz.GetInt32() >= 16, $"En az işçi sayısı {enAz.GetInt32()}.");
    }

    [Fact]
    public async Task Uretimde_EF_komut_loglari_Warning_esiginde_uygulama_kategorileri_Information()
    {
        var log = new LogToplayici();
        await using var f = new OrtamliFabrika("Production", log);
        using var c = await f.GirisliIstemci();
        (await c.GetAsync("/api/rapor/panel")).EnsureSuccessStatusCode();

        var fabrika = f.Services.GetRequiredService<ILoggerFactory>();
        var komut = fabrika.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
        Assert.False(komut.IsEnabled(LogLevel.Information));
        Assert.True(komut.IsEnabled(LogLevel.Warning));
        // 'Failed executing DbCommand' Error'dur: eşiği geçer.
        Assert.True(komut.IsEnabled(LogLevel.Error));
        Assert.True(fabrika.CreateLogger("Kasa.Veritabani").IsEnabled(LogLevel.Information));
        // Her komutta yazılan 'Executed DbCommand' (20101) üretimde akmaz.
        Assert.DoesNotContain(log.Kayitlar, x => x.OlayId == 20101);
        Assert.DoesNotContain(log.Kayitlar, x => x.Kategori == "Microsoft.EntityFrameworkCore.Database.Command" && x.Seviye < LogLevel.Warning);
    }

    [Fact]
    public async Task Gelistirmede_EF_komut_loglari_degismez()
    {
        var log = new LogToplayici();
        await using var f = new OrtamliFabrika("Development", log);
        using var c = await f.GirisliIstemci();
        (await c.GetAsync("/api/rapor/panel")).EnsureSuccessStatusCode();

        Assert.True(f.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").IsEnabled(LogLevel.Information));
        Assert.Contains(log.Kayitlar, x => x.OlayId == 20101);
    }

    private static IslemYazDto Islem() => new(Bugun, "Tedarikçi", 100m, "MEZAT", GiderTipi.Cari);

    private static async Task<string?> Hata(HttpResponseMessage yanit)
    {
        var metin = await yanit.Content.ReadAsStringAsync();
        Assert.True(JsonDocument.Parse(metin).RootElement.TryGetProperty("hata", out var hata), metin);
        return hata.GetString();
    }

    /// <summary>Fiziksel SQLite dosyasıyla (WAL, havuzsuz, 1 sn kilit beklemesi) çalışan ve logları toplayan uygulama.</summary>
    private sealed class HataFabrikasi : KasaWebFactory
    {
        private readonly string yol = Path.Combine(Path.GetTempPath(), $"kasa-hata-{Guid.NewGuid():N}.db");
        public LogToplayici Log { get; } = new();
        private string Baglanti => new SqliteConnectionStringBuilder { DataSource = yol, Pooling = false, DefaultTimeout = 1 }.ToString();
        public HataFabrikasi() => Saat = new SabitSaat(VarsayilanBugun);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(l => l.AddProvider(Log));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(Baglanti));
            });
        }

        public KasaDbContext Baglam() => new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(Baglanti).Options);

        public void Sql(string sql)
        {
            _ = Services;
            using var db = Baglam();
            db.Database.ExecuteSqlRaw(sql);
        }

        /// <summary>İkinci bir bağlantı yazma kilidini (BEGIN IMMEDIATE) tutar; bırakılınca geri alınır.</summary>
        public IDisposable YazmaKilidiTut()
        {
            _ = Services;
            var baglanti = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = yol, Pooling = false }.ToString());
            baglanti.Open();
            var islem = baglanti.BeginTransaction(deferred: false);
            return new Kilit(baglanti, islem);
        }

        private sealed class Kilit(SqliteConnection baglanti, SqliteTransaction islem) : IDisposable
        {
            public void Dispose() { islem.Dispose(); baglanti.Dispose(); }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) foreach (var ek in new[] { "", "-wal", "-shm", "-journal" })
                try { File.Delete(yol + ek); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Verilen ortamda (appsettings.{Ortam}.json) açılan uygulama; arka plan işleri kapalıdır.</summary>
    private sealed class OrtamliFabrika(string ortam, LogToplayici log) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(ortam);
            builder.ConfigureLogging(l => l.AddProvider(log));
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bildirim:WorkerEtkin"] = "false", ["Bildirim:PushEtkin"] = "false",
                ["Finans:BakimEtkin"] = "false", ["Yedek:Etkin"] = "false",
            }));
        }

        /// <summary>Editör oturumlu istemci; geliştirme dışında oturum çerezi Secure olduğundan HTTPS adresiyle açılır.</summary>
        public async Task<HttpClient> GirisliIstemci()
        {
            var c = CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            (await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "kasa123" })).EnsureSuccessStatusCode();
            return c;
        }
    }
}
