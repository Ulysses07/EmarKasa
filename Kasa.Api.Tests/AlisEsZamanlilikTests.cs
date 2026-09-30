using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

public class AlisEsZamanlilikTests
{
    [Fact]
    public async Task Eszamanli_duzeltme_ve_iptalde_yalniz_bir_islem_kazanir()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), "kasa-correction-race-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        using var rendezvous = new IslemBulusmasi();
        try
        {
            await using var factory = new DosyaliFabrika(connectionString, rendezvous);
            using var editor = await factory.EditorClientAsync();
            await AlisIsAkisiTests.Prepare(editor);
            var channels = (await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar", cancellationToken: ct))!;
            var purchase = await AlisIsAkisiTests.Read<AlisDto>(await editor.PostAsJsonAsync("/api/alis", AlisIsAkisiTests.Draft(channels), cancellationToken: ct));
            purchase = await AlisIsAkisiTests.Read<AlisDto>(await editor.PostAsJsonAsync($"/api/alis/{purchase.Id}/odemeler",
                new AlisOdemeYaz(purchase.Surum, Guid.NewGuid(), purchase.Tarih, 40m), cancellationToken: ct));
            var payment = purchase.Odemeler.Single();
            rendezvous.Enabled = true;
            var results = await Task.WhenAll(
                editor.PutAsJsonAsync($"/api/alis/{purchase.Id}/odemeler/{payment.Id}",
                new AlisOdemeDuzelt(purchase.Surum, Guid.NewGuid(), purchase.Tarih, 60m, "Düzeltilen tutar"), cancellationToken: ct),
                editor.PostAsJsonAsync($"/api/alis/{purchase.Id}/odemeler/{payment.Id}/iptal", new AlisOdemeIptal(purchase.Surum, Guid.NewGuid(), "Ödenmedi"), cancellationToken: ct));
            rendezvous.Enabled = false;
            try
            { Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict); }
            finally { foreach (var result in results) result.Dispose(); }
            Assert.Equal(2, rendezvous.Connections.Count);
            using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connectionString).Options);
            Assert.Equal(purchase.Surum + 1, db.Alislar.Single().Surum);
            Assert.Equal(2, db.FinansIstekler.Count());
            Assert.Equal(db.Islemler.Count(), db.AlisOdemeler.Count());
            Assert.InRange(db.Islemler.Count(), 0, 1);
            if (db.Islemler.Any())
                Assert.Equal(60m, db.Islemler.Single().TutarTl);
        }
        finally { foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + suffix); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Eszamanli_odeme_fazla_odeme_veya_yinelenen_gider_uretmez(bool sameRequest)
    {
        var path = Path.Combine(Path.GetTempPath(), "kasa-purchase-race-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        using var rendezvous = new IslemBulusmasi();
        try
        {
            await using var factory = new DosyaliFabrika(connectionString, rendezvous);
            using var editor = await factory.EditorClientAsync();
            await AlisIsAkisiTests.Prepare(editor);
            var channels = (await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar", cancellationToken: TestContext.Current.CancellationToken))!;
            var purchase = await AlisIsAkisiTests.Read<AlisDto>(await editor.PostAsJsonAsync("/api/alis", AlisIsAkisiTests.Draft(channels), cancellationToken: TestContext.Current.CancellationToken));
            var first = new AlisOdemeYaz(purchase.Surum, Guid.NewGuid(), purchase.Tarih, 70m);
            var second = sameRequest ? first : first with { IstekId = Guid.NewGuid() };
            rendezvous.Enabled = true;

            var responses = await Task.WhenAll(
                editor.PostAsJsonAsync($"/api/alis/{purchase.Id}/odemeler", first, cancellationToken: TestContext.Current.CancellationToken),
                editor.PostAsJsonAsync($"/api/alis/{purchase.Id}/odemeler", second, cancellationToken: TestContext.Current.CancellationToken));
            rendezvous.Enabled = false;
            try
            {
                Assert.Equal(sameRequest ? 2 : 1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
                Assert.Equal(sameRequest ? 0 : 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            }
            finally { foreach (var response in responses) response.Dispose(); }
            Assert.Equal(2, rendezvous.Connections.Count);
            using var verification = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connectionString).Options);
            Assert.Single(verification.AlisOdemeler);
            Assert.Equal(70m, Assert.Single(verification.Islemler).TutarTl);
            Assert.Equal(purchase.Surum + 1, verification.Alislar.Single().Surum);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                File.Delete(path + suffix);
        }
    }

    private sealed class DosyaliFabrika(string connectionString, IslemBulusmasi rendezvous) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(options => options.UseSqlite(connectionString).AddInterceptors(rendezvous));
            });
        }
    }

    private sealed class IslemBulusmasi : DbTransactionInterceptor, IDisposable
    {
        private readonly Barrier _barrier = new(2);
        public volatile bool Enabled;
        public ConcurrentDictionary<DbConnection, byte> Connections { get; } = new(ReferenceEqualityComparer.Instance);
        public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        {
            if (!Enabled)
                return result;
            Connections.TryAdd(connection, 0);
            if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("Ödeme istekleri eşzamanlı transaction başlangıcına ulaşamadı.");
            return result;
        }
        public void Dispose() => _barrier.Dispose();
    }
}
