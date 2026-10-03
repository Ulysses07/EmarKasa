using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>Günlük append ile DB commit'i arasındaki an, yedek zaman kesiminden ayrılamaz.</summary>
public class GuvenlikYedekYarisiTests
{
    private const string EskiSifre = "kasa123";
    private const string YeniSifre = "yedek-yarisi-yeni-sifre";

    private sealed class SayacliSaat(DateTimeOffset an) : TimeProvider
    {
        private readonly long _utcTicks = an.UtcTicks;
        private int _cagriSayisi;
        public int CagriSayisi => Volatile.Read(ref _cagriSayisi);
        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Increment(ref _cagriSayisi);
            return new DateTimeOffset(_utcTicks, TimeSpan.Zero);
        }
    }

    private sealed class CommitDuragi : DbTransactionInterceptor, IDisposable
    {
        private readonly ManualResetEventSlim _devam = new(false);
        private readonly TaskCompletionSource _commitBekliyor = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _kurulu;

        public Task CommitBekliyor => _commitBekliyor.Task;
        public void Kur() => Interlocked.Exchange(ref _kurulu, 1);
        public void Devam() => _devam.Set();

        public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            if (Interlocked.CompareExchange(ref _kurulu, 0, 1) == 1)
            {
                _commitBekliyor.TrySetResult();
                if (!_devam.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Güvenlik transaction'ı testte serbest bırakılmadı.");
            }
            return result;
        }

        public void Dispose() => _devam.Dispose();
    }

    private sealed class DosyaFabrikasi : KasaWebFactory
    {
        private readonly string _veritabani;
        private readonly string _dizin;
        private readonly CommitDuragi _durak;

        public DosyaFabrikasi(string dizin, SayacliSaat saat, CommitDuragi durak)
        {
            _dizin = dizin;
            _veritabani = Path.Combine(dizin, "kasa.db");
            _durak = durak;
            Saat = saat;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Yedek:Dizin"] = Path.Combine(_dizin, "yedekler"),
                ["Yedek:Etkin"] = "false",
                ["GuvenlikGunlugu:Etkin"] = "true",
                ["GuvenlikGunlugu:Yol"] = Path.Combine(_dizin, GuvenlikGunlugu.DosyaAdi),
                ["Bildirim:PushEtkin"] = "false",
                ["Bildirim:WorkerEtkin"] = "false",
                ["Finans:BakimEtkin"] = "false",
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                var baglanti = new SqliteConnectionStringBuilder { DataSource = _veritabani, Pooling = false }.ToString();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(baglanti).AddInterceptors(_durak));
            });
        }
    }

    private static async Task Bekle(Func<bool> kosul, CancellationToken ct)
    {
        using var sure = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sure.CancelAfter(TimeSpan.FromSeconds(10));
        while (!kosul())
            await Task.Delay(1, sure.Token);
    }

    private static void Temizle(string dizin)
    {
        var mutlak = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dizin));
        var ad = Path.GetFileName(mutlak);
        const string onEk = "kasa-yedek-yarisi-";
        var geciciKok = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var karsilastirma = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(mutlak), geciciKok, karsilastirma)
            || !ad.StartsWith(onEk, StringComparison.Ordinal)
            || !Guid.TryParseExact(ad[onEk.Length..], "N", out _))
            throw new InvalidOperationException($"Test dışı dizin silinemez: {mutlak}");
        if (Directory.Exists(mutlak))
            Directory.Delete(mutlak, recursive: true);
    }

    [Fact]
    public async Task Gunluk_yazilmis_ama_db_commit_beklerken_yedek_zaman_kesimi_almaz()
    {
        var ct = TestContext.Current.CancellationToken;
        var dizin = Path.Combine(Path.GetTempPath(), "kasa-yedek-yarisi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dizin);
        try
        {
            using var durak = new CommitDuragi();
            var saat = new SayacliSaat(new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero));
            using var host = new DosyaFabrikasi(dizin, saat, durak);
            using var editor = await host.EditorClientAsync();
            var seriKilit = host.Services.GetRequiredService<GuvenlikYedekSeriKilidi>();
            durak.Kur();
            var degistir = Task.Run(() => editor.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = EskiSifre, yeniSifre = YeniSifre }, ct), ct);
            Task<string>? yedek = null;
            int oncekiSaatCagrisi;
            try
            {
                await durak.CommitBekliyor.WaitAsync(TimeSpan.FromSeconds(10), ct);
                Assert.Contains(File.ReadAllLines(Path.Combine(dizin, GuvenlikGunlugu.DosyaAdi)),
                    s => s.Contains($"\"tur\":\"{GuvenlikGunlugu.EditorSifresiDegisti}\"", StringComparison.Ordinal));
                oncekiSaatCagrisi = saat.CagriSayisi;
                yedek = Task.Run(async () =>
                {
                    using var scope = host.Services.CreateScope();
                    return await host.Services.GetRequiredService<YedekServisi>().Olustur(
                        scope.ServiceProvider.GetRequiredService<KasaDbContext>(), YedekTuru.Elle, ct);
                }, ct);
                await Bekle(() => seriKilit.BekleyenYedekSayisi == 1, ct);
                Assert.Equal(oncekiSaatCagrisi, saat.CagriSayisi);
                Assert.False(yedek.IsCompleted);
            }
            finally { durak.Devam(); }

            using var yanit = await degistir;
            Assert.Equal(HttpStatusCode.NoContent, yanit.StatusCode);
            var yedekYolu = await yedek!;
            Assert.True(saat.CagriSayisi > oncekiSaatCagrisi);
            var kopya = Path.Combine(dizin, "kopya.db");
            using (var zip = ZipFile.OpenRead(yedekYolu))
                zip.GetEntry("kasa.db")!.ExtractToFile(kopya);
            using var baglanti = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = kopya, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            baglanti.Open();
            using var komut = baglanti.CreateCommand();
            komut.CommandText = "SELECT \"SifreHash\" FROM \"EditorGuvenlik\" WHERE \"Id\" = 1;";
            Assert.True(SifreHasher.Dogrula(YeniSifre, Assert.IsType<string>(komut.ExecuteScalar())));
        }
        finally { Temizle(dizin); }
    }
}
