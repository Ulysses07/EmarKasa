using System.Globalization;
using System.Text;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>Test fabrikalarının veritabanı şablondan kopyalanır (<see cref="KasaWebFactory"/>): kopya, boş bellek içi veritabanında
/// çalışan ilk açılışla (migration'lar ve veri adımları) aynı şemayı, migration geçmişini, satırları ve bağlantı ayarlarını verir.</summary>
public class SablonVeritabaniTests
{
    [Fact]
    public void Sablon_kopyasi_bos_veritabaninda_calisan_ilk_acilisla_ayni()
    {
        using var taze = new SqliteConnection("Data Source=:memory:");
        taze.Open();
        using (var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(taze).Options))
            KasaVeritabaniBaslatici.Baslat(db);
        using var kopya = new SqliteConnection("Data Source=:memory:");
        kopya.Open();
        KasaWebFactory.SablonuKopyala(kopya);

        var beklenen = Dokum(taze);
        Assert.Contains("__EFMigrationsHistory", beklenen);
        Assert.Equal(beklenen, Dokum(kopya));
    }

    [Fact]
    public async Task Sablondan_acilan_uygulama_yabanci_anahtarlari_uygular_ve_bekleyen_migration_birakmaz()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Empty(db.Database.GetPendingMigrations());
        var baglanti = (SqliteConnection)db.Database.GetDbConnection();
        Assert.Equal(1L, Deger(baglanti, "PRAGMA foreign_keys;"));
        Assert.Equal("ok", Deger(baglanti, "PRAGMA integrity_check;"));
    }

    private static object? Deger(SqliteConnection c, string sql)
    {
        using var k = c.CreateCommand();
        k.CommandText = sql;
        return k.ExecuteScalar();
    }

    /// <summary>Şema (sqlite_master), her tablonun bütün satırları (sıralı) ve bağlantıya bağlı PRAGMA'lar.</summary>
    private static string Dokum(SqliteConnection c)
    {
        var s = new StringBuilder();
        foreach (var pragma in (string[])["foreign_keys", "user_version", "application_id", "journal_mode", "recursive_triggers", "encoding", "auto_vacuum"])
            s.Append(pragma).Append('=').Append(Convert.ToString(Deger(c, $"PRAGMA {pragma};"), CultureInfo.InvariantCulture)).Append('\n');
        var tablolar = new List<string>();
        using (var k = c.CreateCommand())
        {
            k.CommandText = "SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY type, name;";
            using var r = k.ExecuteReader();
            while (r.Read())
            {
                s.Append(r.GetString(0)).Append(' ').Append(r.GetString(1)).Append(" (").Append(r.GetString(2)).Append("): ")
                    .Append(r.IsDBNull(3) ? "" : r.GetString(3)).Append('\n');
                if (r.GetString(0) == "table")
                    tablolar.Add(r.GetString(1));
            }
        }
        foreach (var tablo in tablolar)
        {
            using var k = c.CreateCommand();
            k.CommandText = $"SELECT * FROM \"{tablo}\" ORDER BY 1;";
            using var r = k.ExecuteReader();
            while (r.Read())
            {
                s.Append(tablo).Append(':');
                for (var i = 0; i < r.FieldCount; i++)
                    s.Append(' ').Append(r.IsDBNull(i) ? "NULL" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture));
                s.Append('\n');
            }
        }
        return s.ToString();
    }
}
