using System.Data;
using Kasa.Api.Migrations;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>Boş veritabanında migration çalıştırır. Migration geçmişi olmayan eski
/// EnsureCreated şemasını tek transaction içinde, kayıtları ve kimlikleri koruyarak
/// ilk migration'a eşler. Belirsiz/veri kaybettirecek bir dönüşümde geri alır.</summary>
public static class KasaDatabaseInitializer
{
    public static void Initialize(KasaDbContext db)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) connection.Open();
        try
        {
            if (!HasMigrationHistory(connection) && StableSchemaDefinition.Tables.Any(t => TableExists(connection, t.Name)))
                BridgeLegacyDatabase(connection);

            db.Database.Migrate();
        }
        finally
        {
            if (openedHere) connection.Close();
        }
    }

    private static void BridgeLegacyDatabase(SqliteConnection connection)
    {
        // SQLite tablo yeniden oluşturma sırasında eski CASCADE/SET NULL davranışlarının
        // canlı kayıtları değiştirmesini önle. Son durumda bütün FK'ler kontrol edilir.
        var foreignKeys = Convert.ToInt32(Scalar(connection, "PRAGMA foreign_keys;"));
        Execute(connection, "PRAGMA foreign_keys = OFF;");
        try
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            // İki süreç aynı anda açılmışsa ilk sürecin tamamladığı geçişi tekrarlama.
            if (HasMigrationHistory(connection, transaction))
            {
                transaction.Commit();
                return;
            }

            foreach (var table in StableSchemaDefinition.Tables)
            {
                if (!TableExists(connection, table.Name, transaction))
                {
                    Execute(connection, table.Create(), transaction);
                    continue;
                }

                CheckCustomSchema(connection, transaction, table);
                var columns = ColumnNames(connection, transaction, table.Name);
                foreach (var column in table.Columns.Where(c => !columns.Contains(c)))
                {
                    if (table.AdditiveColumns?.TryGetValue(column, out var declaration) != true)
                        throw CannotUpgrade($"{table.Name}.{column} zorunlu alanı bulunamadı.");
                    Execute(connection, $"ALTER TABLE \"{table.Name}\" ADD COLUMN \"{column}\" {declaration};", transaction);
                }
            }

            CheckDuplicates(connection, transaction);
            BackfillChannels(connection, transaction);
            var duplicateIncome = HasDuplicateIncome(connection, transaction);

            // Her sütun açıkça kopyalanır; decimal ve tarih metinleri dönüştürülmez.
            // AUTOINCREMENT'in silinmiş en yüksek Id bilgisini de koru.
            foreach (var table in StableSchemaDefinition.Tables)
            {
                var sequence = Scalar(connection, "SELECT seq FROM sqlite_sequence WHERE name = $name;", transaction, ("$name", table.Name));
                var temporaryName = "__kasa_upgrade_" + table.Name;
                if (TableExists(connection, temporaryName, transaction))
                    throw CannotUpgrade($"Geçici tablo adı kullanımda: {temporaryName}.");
                Execute(connection, table.Create(temporaryName), transaction);
                var columns = string.Join(", ", table.Columns.Select(c => $"\"{c}\""));
                Execute(connection, $"INSERT INTO \"{temporaryName}\" ({columns}) SELECT {columns} FROM \"{table.Name}\";", transaction);
                Execute(connection, $"DROP TABLE \"{table.Name}\"; ALTER TABLE \"{temporaryName}\" RENAME TO \"{table.Name}\";", transaction);
                if (sequence is not null and not DBNull)
                    Execute(connection, "UPDATE sqlite_sequence SET seq = MAX(seq, $sequence) WHERE name = $name;", transaction,
                        ("$sequence", sequence), ("$name", table.Name));
            }

            foreach (var (name, sql) in StableSchemaDefinition.Indexes)
            {
                // Eski gelirlerin tamamını koru. Bu iki index, ileri migration'da
                // eski gruplar işaretlendikten sonra filtreli olarak kurulacak.
                // Arada süreç durursa initial history sayesinde kalan migration'lar
                // sonraki başlangıçta tamamlanır; henüz HTTP sunucusu açılmamıştır.
                if (duplicateIncome && name is "IX_Gelenler_DonemStart_KanalId" or "IX_Gelenler_DonemStart_Kanal") continue;
                Execute(connection, sql, transaction);
            }

            using (var check = Command(connection, "PRAGMA foreign_key_check;", transaction))
            using (var reader = check.ExecuteReader())
            {
                if (reader.Read())
                    throw CannotUpgrade($"{reader.GetString(0)} tablosunda {reader.GetValue(1)} kimlikli kaydın ilişkisi geçersiz.");
            }

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL);
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ($id, $version);
                """, transaction, ("$id", StableSchemaDefinition.MigrationId), ("$version", StableSchemaDefinition.ProductVersion));
            transaction.Commit();
        }
        finally
        {
            Execute(connection, $"PRAGMA foreign_keys = {(foreignKeys == 1 ? "ON" : "OFF")};");
        }
    }

    private static void CheckCustomSchema(SqliteConnection connection, SqliteTransaction transaction, StableSchemaDefinition.Table table)
    {
        var unknown = ColumnNames(connection, transaction, table.Name).Except(table.Columns).ToArray();
        if (unknown.Length != 0)
            throw CannotUpgrade($"{table.Name} tablosunda tanınmayan alanlar var: {string.Join(", ", unknown)}.");

        using var command = Command(connection, "SELECT name, type FROM sqlite_master WHERE tbl_name = $name AND type IN ('trigger', 'index') AND sql IS NOT NULL;", transaction, ("$name", table.Name));
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1) == "trigger" || !StableSchemaDefinition.Indexes.Any(i => i.Name == reader.GetString(0)))
                throw CannotUpgrade($"{table.Name} tablosunda özel şema nesnesi var: {reader.GetString(0)}.");
    }

    private static void CheckDuplicates(SqliteConnection connection, SqliteTransaction transaction)
    {
        var duplicateChannel = Scalar(connection, "SELECT Ad FROM Kanallar GROUP BY Ad COLLATE NOCASE HAVING COUNT(*) > 1 LIMIT 1;", transaction);
        if (duplicateChannel is not null)
            throw CannotUpgrade($"Aynı adlı birden fazla kanal var: '{duplicateChannel}'. Kanal kimliklerini ve hareketlerini inceleyip birleştirmeden geçiş yapılamaz.");

        var reservedChannel = Scalar(connection, "SELECT Ad FROM Kanallar WHERE Ad COLLATE NOCASE IN ($common, $credit) LIMIT 1;", transaction,
            ("$common", Kanallar.Ortak), ("$credit", KrediTuretici.KrediKanal));
        if (reservedChannel is not null)
            throw CannotUpgrade($"'{reservedChannel}' özel muhasebe etiketi gerçek kanal olarak kullanılmış.");

    }

    private static void BackfillChannels(SqliteConnection connection, SqliteTransaction transaction)
    {
        foreach (var table in new[] { "Islemler", "Gelenler", "Krediler" })
        {
            Execute(connection, $"""
                INSERT INTO Kanallar (Ad, Aktif, Sira, AcilisDevri)
                SELECT MIN(v.Kanal), 0, COALESCE((SELECT MAX(Sira) + 1 FROM Kanallar), 0), '0.0'
                FROM "{table}" v
                WHERE v.KanalId IS NULL AND v.Kanal COLLATE NOCASE NOT IN ($common, $credit)
                    AND NOT EXISTS (SELECT 1 FROM Kanallar k WHERE k.Ad = v.Kanal COLLATE NOCASE)
                GROUP BY v.Kanal COLLATE NOCASE;
                UPDATE "{table}" SET KanalId = (SELECT k.Id FROM Kanallar k WHERE k.Ad = "{table}".Kanal COLLATE NOCASE)
                WHERE KanalId IS NULL AND Kanal COLLATE NOCASE NOT IN ($common, $credit);
                UPDATE "{table}" SET Kanal = $common WHERE KanalId IS NULL AND Kanal COLLATE NOCASE = $common;
                UPDATE "{table}" SET Kanal = $credit WHERE KanalId IS NULL AND Kanal COLLATE NOCASE = $credit;
                """, transaction, ("$common", Kanallar.Ortak), ("$credit", KrediTuretici.KrediKanal));
        }
    }

    private static bool HasDuplicateIncome(SqliteConnection connection, SqliteTransaction transaction) =>
        Scalar(connection, "SELECT 1 FROM Gelenler GROUP BY DonemStart, Kanal COLLATE NOCASE HAVING COUNT(*) > 1 LIMIT 1;", transaction) is not null
        || Scalar(connection, "SELECT 1 FROM Gelenler WHERE KanalId IS NOT NULL GROUP BY DonemStart, KanalId HAVING COUNT(*) > 1 LIMIT 1;", transaction) is not null;

    private static InvalidOperationException CannotUpgrade(string detail) => new(
        $"Kasa veritabanı güvenli biçimde güncellenemedi. {detail} Geçiş geri alındı; mevcut veriler korundu. Veritabanını silmeyin; yedek üzerinde verileri inceleyin.");

    private static HashSet<string> ColumnNames(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = Command(connection, $"PRAGMA table_info(\"{table}\");", transaction);
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    private static bool HasMigrationHistory(SqliteConnection connection, SqliteTransaction? transaction = null) =>
        TableExists(connection, "__EFMigrationsHistory", transaction) &&
        Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\";", transaction)) > 0;

    private static bool TableExists(SqliteConnection connection, string name, SqliteTransaction? transaction = null) =>
        Scalar(connection, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;", transaction, ("$name", name)) is not null;

    private static SqliteCommand Command(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static object? Scalar(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        using var command = Command(connection, sql, transaction, parameters);
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        using var command = Command(connection, sql, transaction, parameters);
        command.ExecuteNonQuery();
    }
}
