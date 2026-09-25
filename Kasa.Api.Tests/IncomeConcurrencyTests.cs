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

public class IncomeConcurrencyTests
{
    [Fact]
    public async Task Eszamanli_ilk_gelir_girisleri_ayri_baglantilarda_tek_kaydi_gunceller()
    {
        const int requestCount = 4;
        var path = Path.Combine(Path.GetTempPath(), "kasa-income-race-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        using var rendezvous = new IncomeInsertRendezvous(requestCount);
        try
        {
            await using var factory = new FileDatabaseFactory(connectionString, rendezvous);
            using var editor = await factory.EditorClientAsync();
            var amounts = new[] { 100.01m, 200.02m, 300.03m, 400.04m };
            var period = new DateOnly(2026, 9, 1);
            using (var settings = await editor.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = period, kasaAcilisDevri = 0m }))
                settings.EnsureSuccessStatusCode();

            // Interceptor, her isteği INSERT çalışmadan hemen önce bekletir. Dördü
            // de ayrı açık bağlantıyla aynı noktaya gelmeden hiçbir yazma ilerlemez.
            var responses = await Task.WhenAll(amounts.Select(amount => editor.PutAsJsonAsync("/api/gelenler",
                new { donemStart = period, kanal = "MEZAT", tutarTl = amount })));
            try
            {
                Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            }
            finally
            {
                foreach (var response in responses) response.Dispose();
            }

            Assert.Equal(requestCount, rendezvous.ContextCount);
            Assert.Equal(requestCount, rendezvous.ConnectionCount);
            using var verification = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connectionString).Options);
            var saved = Assert.Single(await verification.Gelenler.Where(g => g.DonemStart == period).ToListAsync());
            Assert.NotNull(saved.KanalId);
            Assert.Equal("MEZAT", saved.Kanal);
            Assert.Contains(saved.TutarTl, amounts); // Son yazan kazanır; dört tutar toplanmaz.
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + suffix);
        }
    }

    private sealed class FileDatabaseFactory(string connectionString, IncomeInsertRendezvous rendezvous) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                // Ortak test fabrikasının açık in-memory bağlantısını paylaşma;
                // her HTTP isteğinin context'i kendi dosya bağlantısını açsın.
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(options => options.UseSqlite(connectionString).AddInterceptors(rendezvous));
            });
        }
    }

    private sealed class IncomeInsertRendezvous(int participants) : DbCommandInterceptor, IDisposable
    {
        private readonly Barrier _barrier = new(participants);
        private readonly ConcurrentDictionary<Guid, byte> _contexts = new();
        private readonly ConcurrentDictionary<DbConnection, byte> _connections = new(ReferenceEqualityComparer.Instance);

        public int ContextCount => _contexts.Count;
        public int ConnectionCount => _connections.Count;

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            MeetBeforeInsert(command, eventData);
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            MeetBeforeInsert(command, eventData);
            return result;
        }

        private void MeetBeforeInsert(DbCommand command, CommandEventData eventData)
        {
            if (!command.CommandText.TrimStart().StartsWith("INSERT INTO \"Gelenler\"", StringComparison.OrdinalIgnoreCase)) return;
            _contexts.TryAdd(eventData.Context!.ContextId.InstanceId, 0);
            _connections.TryAdd(command.Connection!, 0);
            if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("Gelir yarış testi için bütün istekler aynı INSERT noktasına ulaşamadı.");
        }

        public void Dispose() => _barrier.Dispose();
    }
}
