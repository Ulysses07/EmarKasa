using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Api.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

public class LegacyIncomeTests
{
    [Theory]
    [InlineData("UPDATE Gelenler SET TutarTl = '1.0' WHERE Id = 12;")]
    [InlineData("UPDATE Gelenler SET DonemStart = '2026-10-01' WHERE Id = 12;")]
    [InlineData("UPDATE Gelenler SET KanalId = 8 WHERE Id = 12;")]
    [InlineData("UPDATE Gelenler SET Id = 99 WHERE Id = 12;")]
    [InlineData("UPDATE Gelenler SET EskiYinelenenGrup = 0 WHERE Id = 12;")]
    [InlineData("DELETE FROM Gelenler WHERE Id = 12;")]
    [InlineData("INSERT INTO Gelenler (DonemStart, Kanal, KanalId, TutarTl) VALUES ('2026-09-01', 'MEZAT', 7, '2.0');")]
    [InlineData("INSERT INTO Gelenler (DonemStart, Kanal, TutarTl) VALUES ('2026-09-01', 'mezat', '2.0');")]
    [InlineData("INSERT INTO Gelenler (DonemStart, Kanal, KanalId, TutarTl, EskiYinelenenGrup) VALUES ('2026-10-01', 'MEZAT', 7, '2.0', 1);")]
    [InlineData("UPDATE Gelenler SET DonemStart = '2026-09-01' WHERE Id = 16;")]
    [InlineData("UPDATE Gelenler SET EskiYinelenenGrup = 1 WHERE Id = 16;")]
    [InlineData("INSERT OR REPLACE INTO Gelenler (Id, DonemStart, Kanal, KanalId, TutarTl) VALUES (12, '2026-10-01', 'YENİ', 8, '2.0');")]
    [InlineData("UPDATE OR REPLACE Gelenler SET Id = 12 WHERE Id = 16;")]
    public void Eski_grup_raw_sql_ile_de_degistirilemez_ve_gruba_yeni_gelir_eklenemez(string mutation)
    {
        using var connection = Open();
        SeedLegacy(connection);
        using var db = Context(connection);
        KasaDatabaseInitializer.Initialize(db);

        var error = Assert.Throws<SqliteException>(() => Execute(connection, mutation));

        Assert.Equal(19, error.SqliteErrorCode);
        Assert.Contains("salt okunurdur", error.Message);
        AssertOriginalRows(db);
    }

    [Fact]
    public void Kopru_sonrasi_yarim_kalmis_baslangic_history_ile_kalan_migrationlari_tamamlar()
    {
        using var connection = Open();
        SeedLegacy(connection);
        // Köprü ilk history kaydını commit ettikten sonra süreç durmuş olsun.
        // Unique indexler yinelenen gelirleri temsil edemediği için henüz yok.
        Execute(connection, """
            CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL);
            INSERT INTO __EFMigrationsHistory VALUES ('20260919000100_InitialStableSchema', '10.0.9');
            """);
        using var db = Context(connection);
        KasaDatabaseInitializer.Initialize(db);
        KasaDatabaseInitializer.Initialize(db);

        Assert.Equal(10, db.Database.GetAppliedMigrations().Count());
        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        AssertOriginalRows(db);
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().Migrate("20260923000400_Operations"));
        Assert.Equal(10, db.Database.GetAppliedMigrations().Count());
        AssertOriginalRows(db);
    }

    [Fact]
    public void Ayni_kanal_kimliginin_farkli_eski_adlari_da_bir_grup_olarak_korunur()
    {
        using var connection = Open();
        SeedLegacy(connection);
        Execute(connection, "UPDATE Gelenler SET Kanal = 'eski başka ad' WHERE Id = 14;");
        using var db = Context(connection);

        KasaDatabaseInitializer.Initialize(db);

        Assert.Equal(2, db.Gelenler.Count(g => g.EskiYinelenenGrup));
        Assert.Equal("eski başka ad", db.Gelenler.Single(g => g.Id == 14).Kanal);
        Assert.Equal(-20.20m, db.Gelenler.Single(g => g.Id == 14).TutarTl);
        Assert.False(db.Gelenler.Single(g => g.Id == 16).EskiYinelenenGrup);
    }

    [Fact]
    public async Task Http_eski_grubu_korur_yeni_donem_ve_kanala_yazmayi_acik_tutar_rename_kilidi_kaldirmaz()
    {
        await using var factory = new LegacyIncomeFactory();
        using var editor = await factory.EditorClientAsync();
        var rows = await editor.GetFromJsonAsync<GelenEntity[]>("/api/gelenler?donemStart=2026-09-01");
        Assert.NotNull(rows);
        Assert.Equal(new[] { 12, 14 }, rows.Select(g => g.Id).Order().ToArray());
        Assert.All(rows, g => Assert.True(g.EskiYinelenenGrup));
        Assert.Equal(79.90m, rows.Sum(g => g.TutarTl));

        foreach (var channel in new[] { "MEZAT", "mezat" })
        {
            var blocked = await editor.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-01", kanal = channel, tutarTl = 1m });
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("salt okunur", await blocked.Content.ReadAsStringAsync());
        }
        (await editor.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-01", kanal = "YENİ", tutarTl = 10m })).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-07", kanal = "MEZAT", tutarTl = 40m })).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-14", kanal = "MEZAT", tutarTl = 50m })).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync("/api/kanallar/7", new { ad = "MEZAT YENİ", aktif = true, sira = 0, acilisDevri = 0m })).EnsureSuccessStatusCode();

        var stillBlocked = await editor.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-01", kanal = "MEZAT YENİ", tutarTl = 500m });
        Assert.Equal(HttpStatusCode.Conflict, stillBlocked.StatusCode);
        using var db = factory.Context();
        var old = await db.Gelenler.Where(g => g.EskiYinelenenGrup).OrderBy(g => g.Id).ToListAsync();
        Assert.Equal(new[] { 12, 14 }, old.Select(g => g.Id).ToArray());
        Assert.Equal(new[] { 100.10m, -20.20m }, old.Select(g => g.TutarTl).ToArray());
        Assert.All(old, g => { Assert.Equal(7, g.KanalId); Assert.Equal("MEZAT YENİ", g.Kanal); });
        Assert.Equal(40m, db.Gelenler.Single(g => g.Id == 16).TutarTl);
        Assert.Equal(5, db.Gelenler.Count());
    }

    [Fact]
    public async Task Eszamanli_eski_grup_yazmalari_hicbir_satiri_degistirmez()
    {
        await using var factory = new LegacyIncomeFactory();
        using var editor = await factory.EditorClientAsync();
        var responses = await Task.WhenAll(Enumerable.Range(1, 4).Select(i => editor.PutAsJsonAsync("/api/gelenler",
            new { donemStart = "2026-09-01", kanal = "MEZAT", tutarTl = i * 100m })));
        foreach (var response in responses)
        {
            using (response) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        using var db = factory.Context();
        AssertOriginalRows(db);
    }

    private static void AssertOriginalRows(KasaDbContext db)
    {
        var rows = db.Gelenler.AsNoTracking().OrderBy(g => g.Id).ToArray();
        Assert.Equal(new[] { 12, 14, 16 }, rows.Select(g => g.Id).ToArray());
        Assert.Equal(new[] { 100.10m, -20.20m, 30.03m }, rows.Select(g => g.TutarTl).ToArray());
        Assert.Equal(new[] { "MEZAT", "mezat", "MEZAT" }, rows.Select(g => g.Kanal).ToArray());
        Assert.Equal(new[] { true, true, false }, rows.Select(g => g.EskiYinelenenGrup).ToArray());
        Assert.Equal(new[] { new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7) }, rows.Select(g => g.DonemStart).ToArray());
    }

    private static void SeedLegacy(SqliteConnection connection)
    {
        foreach (var operation in new InitialStableSchema().UpOperations.OfType<SqlOperation>()) Execute(connection, operation.Sql);
        Execute(connection, """
            DROP INDEX IX_Gelenler_DonemStart_KanalId;
            DROP INDEX IX_Gelenler_DonemStart_Kanal;
            INSERT INTO Kanallar VALUES (7, 'MEZAT', 1, 0, '0.0'), (8, 'YENİ', 1, 1, '0.0');
            INSERT INTO Ayarlar VALUES (1, '2026-09-01', '0.0', NULL);
            INSERT INTO Gelenler VALUES (12, '2026-09-01', 'MEZAT', '100.10', 7),
                (14, '2026-09-01', 'mezat', '-20.20', 7), (16, '2026-09-07', 'MEZAT', '30.03', 7);
            """);
    }

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        connection.Open();
        return connection;
    }
    private static KasaDbContext Context(SqliteConnection connection) => new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).Options);
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class LegacyIncomeFactory : KasaWebFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "kasa-legacy-income-" + Guid.NewGuid().ToString("N") + ".db");
        private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false, ForeignKeys = true }.ToString();
        public LegacyIncomeFactory()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            SeedLegacy(connection);
        }
        public KasaDbContext Context() => new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(ConnectionString).Options);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(options => options.UseSqlite(ConnectionString));
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(_path + suffix);
        }
    }
}
