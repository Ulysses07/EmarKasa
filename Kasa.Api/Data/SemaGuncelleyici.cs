using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kasa.Api.Data;

/// <summary>
/// Var olan bir SQLite dosyasını güncel EF modeline getirir: eksik tabloları (ve
/// indekslerini) oluşturur, eksik sütunları ekler. İdempotenttir; her açılışta
/// EnsureCreated'dan sonra çalışır. Canlı DB migration geçmişi olmadan
/// EnsureCreated ile kurulduğu için EF migration yerine bu yol seçildi.
/// Not: sonradan eklenen sütunlara SQLite ALTER ile yabancı anahtar kısıtı eklenemez.
/// </summary>
public static class SemaGuncelleyici
{
    public static IReadOnlyList<string> Guncelle(KasaDbContext db)
    {
        var yapilan = new List<string>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        var mevcutTablolar = Oku(conn, "SELECT name FROM sqlite_master WHERE type='table'")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // EF'nin kendi CREATE betiğinden eksik tabloların ifadelerini seç.
        var ifadeler = db.Database.GenerateCreateScript()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entity in db.Model.GetEntityTypes())
        {
            var tablo = entity.GetTableName();
            if (tablo is null) continue;

            if (!mevcutTablolar.Contains(tablo))
            {
                foreach (var sql in ifadeler.Where(s => TabloyaAit(s, tablo)))
                    Calistir(conn, sql);
                yapilan.Add($"tablo+ {tablo}");
                continue;
            }

            var sutunlar = Oku(conn, $"SELECT name FROM pragma_table_info('{tablo}')")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nesne = StoreObjectIdentifier.Table(tablo, entity.GetSchema());
            foreach (var p in entity.GetProperties())
            {
                var ad = p.GetColumnName(nesne);
                if (ad is null || sutunlar.Contains(ad)) continue;
                var tip = p.GetColumnType();
                var tanim = p.IsNullable ? $"\"{ad}\" {tip} NULL" : $"\"{ad}\" {tip} NOT NULL DEFAULT {Varsayilan(p)}";
                Calistir(conn, $"ALTER TABLE \"{tablo}\" ADD COLUMN {tanim}");
                yapilan.Add($"sütun+ {tablo}.{ad}");
            }
        }
        return yapilan;
    }

    private static bool TabloyaAit(string sql, string tablo)
        => sql.StartsWith($"CREATE TABLE \"{tablo}\"", StringComparison.OrdinalIgnoreCase)
           || (sql.Contains("INDEX", StringComparison.OrdinalIgnoreCase)
               && sql.Contains($" ON \"{tablo}\"", StringComparison.OrdinalIgnoreCase));

    private static string Varsayilan(IProperty p)
    {
        var t = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
        if (t == typeof(string)) return "''";
        if (t == typeof(decimal)) return "'0.0'";
        if (t == typeof(DateOnly)) return "'0001-01-01'";
        return "0"; // int, bool, enum
    }

    private static List<string> Oku(System.Data.Common.DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    private static void Calistir(System.Data.Common.DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
