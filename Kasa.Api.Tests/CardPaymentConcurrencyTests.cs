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

public class CardPaymentConcurrencyTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Fiziksel_SQLite_farkli_baglantili_kart_odemeleri_surumu_ve_istek_tekrarini_korur(bool sameRequest)
    {
        var path = Path.Combine(Path.GetTempPath(), "kasa-card-race-" + Guid.NewGuid().ToString("N") + ".db");
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        using var rendezvous = new TransactionRendezvous();
        try
        {
            await using var factory = new FileFactory(cs, rendezvous); using var c = await factory.EditorClientAsync();
            var today = FinansTakipServisi.Bugun;
            (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = today, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
            var create = await c.PostAsJsonAsync("/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Yarış", 1000m, 5, 25, today, 100m, [new(1, 100m)]));
            create.EnsureSuccessStatusCode(); var card = (await create.Content.ReadFromJsonAsync<KartTakipDto>())!;
            var request = new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, today, 60m);
            rendezvous.Enabled = true;
            var responses = await Task.WhenAll(c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/odemeler", request),
                c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/odemeler", sameRequest ? request : request with { IstekId = Guid.NewGuid() }));
            rendezvous.Enabled = false;
            try
            {
                Assert.Equal(sameRequest ? 2 : 1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
                Assert.Equal(sameRequest ? 0 : 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            }
            finally { foreach (var response in responses) response.Dispose(); }
            Assert.Equal(2, rendezvous.Connections.Count);
            using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(cs).Options);
            Assert.Single(db.TakipKartOdemeler); Assert.Equal(60m, db.TakipKartOdemeler.Single().Tutar);
            Assert.Equal(1, db.FinansIstekler.Count(r => r.Tur == "KartOdeme"));
            Assert.Equal(940m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
        }
        finally { foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + suffix); }
    }
    private sealed class FileFactory(string cs, TransactionRendezvous rendezvous) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(cs).AddInterceptors(rendezvous));
            });
        }
    }
    private sealed class TransactionRendezvous : DbTransactionInterceptor, IDisposable
    {
        private readonly Barrier _barrier = new(2);
        public volatile bool Enabled;
        public ConcurrentDictionary<DbConnection, byte> Connections { get; } = new(ReferenceEqualityComparer.Instance);
        public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        {
            if (!Enabled) return result;
            Connections.TryAdd(connection, 0);
            if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(20))) throw new TimeoutException("İki kart ödemesi aynı transaction başlangıcına ulaşamadı.");
            return result;
        }
        public void Dispose() => _barrier.Dispose();
    }
}
