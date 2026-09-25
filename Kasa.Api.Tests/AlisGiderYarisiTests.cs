using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

public class AlisGiderYarisiTests
{
    [Fact]
    public async Task Gideri_alis_odemesine_baglama_ile_tutar_duzeltmesi_yarisinca_bagli_tutar_degismez()
    {
        var path = Path.Combine(Path.GetTempPath(), "kasa-expense-link-race-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        using var rendezvous = new TransactionRendezvous();
        try
        {
            await using var factory = new FileFactory(connectionString, rendezvous);
            using var editor = await factory.EditorClientAsync();
            await AlisWorkflowTests.Prepare(editor);
            var channels = (await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar"))!;
            var purchase = await AlisWorkflowTests.Read<AlisDto>(
                await editor.PostAsJsonAsync("/api/alis", AlisWorkflowTests.Draft(channels)));
            var expense = await AlisWorkflowTests.Read<JsonElement>(await editor.PostAsJsonAsync("/api/islemler",
                new IslemYazDto(purchase.Tarih, "Tedarikçi", 100m, "Ortak", GiderTipi.Cari)));
            int expenseId = expense.GetProperty("id").GetInt32();

            rendezvous.Enabled = true;
            HttpResponseMessage[] responses;
            try
            {
                responses = await Task.WhenAll(
                    editor.PostAsJsonAsync($"/api/alis/{purchase.Id}/odemeler",
                        new AlisOdemeYaz(purchase.Surum, Guid.NewGuid(), purchase.Tarih, 100m, MevcutIslemId: expenseId)),
                    editor.PutAsJsonAsync($"/api/islemler/{expenseId}",
                        new IslemYazDto(purchase.Tarih, "Tedarikçi", 150m, "Ortak", GiderTipi.Cari)));
            }
            finally { rendezvous.Enabled = false; }

            bool paymentWon;
            try
            {
                Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
                Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
                Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
                paymentWon = responses[0].StatusCode == HttpStatusCode.OK;
            }
            finally { foreach (var response in responses) response.Dispose(); }

            Assert.Equal(2, rendezvous.Connections.Count);
            using var verification = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connectionString).Options);
            var saved = Assert.Single(verification.Islemler);
            Assert.Equal(expenseId, saved.Id);
            if (paymentWon)
            {
                var linked = Assert.Single(verification.AlisOdemeler);
                Assert.Equal(expenseId, linked.IslemId);
                Assert.Equal(purchase.Id, linked.AlisId);
                Assert.Equal(100m, saved.TutarTl);
            }
            else
            {
                Assert.Empty(verification.AlisOdemeler);
                Assert.Equal(150m, saved.TutarTl);
            }

            // Aynı fiziksel gider ikinci defa kasaya yazılmamalı; bağlantı hangi
            // sırada kurulduysa kurulsun rapor başarılı ve DB tutarıyla mutabık kalır.
            var weekly = (await editor.GetFromJsonAsync<List<HaftalikOzet>>("/api/rapor/haftalik"))!;
            Assert.Equal(saved.TutarTl, weekly.Sum(h => h.ToplamGiden));
            Assert.Equal(1_000m - saved.TutarTl, weekly[^1].KasaDevir);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + suffix);
        }
    }

    private sealed class FileFactory(string connectionString, TransactionRendezvous rendezvous) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(connectionString).AddInterceptors(rendezvous));
            });
        }
    }

    private sealed class TransactionRendezvous : DbTransactionInterceptor, IDisposable
    {
        private readonly Barrier _barrier = new(2);
        public volatile bool Enabled;
        public ConcurrentDictionary<DbConnection, byte> Connections { get; } = new(ReferenceEqualityComparer.Instance);

        public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        {
            if (!Enabled) return result;
            Connections.TryAdd(connection, 0);
            // İki istek de yazma kilidini almadan buluşur; kilidi aldıktan sonra
            // beklemek testin kendisinin kilitlenmesine neden olurdu.
            if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(20)))
                throw new TimeoutException("Gider düzenleme ve alış ödeme istekleri aynı transaction başlangıcına ulaşamadı.");
            return result;
        }

        public void Dispose() => _barrier.Dispose();
    }
}
