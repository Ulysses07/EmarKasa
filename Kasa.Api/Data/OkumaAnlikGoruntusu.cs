using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kasa.Api.Data;

/// <summary>
/// Salt okunur, tutarlı okuma. Microsoft.Data.Sqlite'ın normal <c>BeginTransaction()</c>'ı BEGIN IMMEDIATE açar ve
/// okuma boyunca yazma kilidini tutar; bu anlık görüntü ise DEFERRED transaction'dır: WAL kipinde ilk okumada alınan
/// görüntüyü sonuna kadar görür, yazanı bekletmez, yazan da onu bekletmez. Bağlantı süre boyunca
/// <c>PRAGMA query_only</c> ile yazmaya kapalıdır: okuma yolundan yanlışlıkla yapılan bir yazma SQLITE_READONLY ile
/// reddedilir (sessizce kilit almaz). Dış bir transaction varsa (yazma yolunun içinden çağrı) onu kullanır ve
/// hiçbir şey değiştirmez. Bitince bağlantı yazmaya açılır ve kapatılır (havuza temiz döner).
/// </summary>
public sealed class OkumaAnlikGoruntusu : IDisposable
{
    private readonly KasaDbContext? _db;
    private readonly SqliteConnection? _connection;
    private readonly SqliteTransaction? _transaction;
    private readonly IDbContextTransaction? _efTransaction;
    private OkumaAnlikGoruntusu(KasaDbContext? db, SqliteConnection? connection, SqliteTransaction? transaction, IDbContextTransaction? efTransaction)
    { _db = db; _connection = connection; _transaction = transaction; _efTransaction = efTransaction; }

    internal static OkumaAnlikGoruntusu Baslat(KasaDbContext db)
    {
        if (db.Database.CurrentTransaction is not null)
            return new(null, null, null, null);
        db.Database.OpenConnection();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        SqliteTransaction? transaction = null;
        try
        {
            Calistir(connection, "PRAGMA query_only = 1;");
            transaction = connection.BeginTransaction(deferred: true);
            // EF'in sarmalayıcısı dış transaction'ın sahibi değildir; bırakılırken yalnız EF'in bağlantı sayacını düşürür.
            var efTransaction = db.Database.UseTransaction(transaction)!;
            return new(db, connection, transaction, efTransaction);
        }
        catch
        {
            transaction?.Dispose();
            Calistir(connection, "PRAGMA query_only = 0;");
            db.Database.CloseConnection();
            throw;
        }
    }

    public void Dispose()
    {
        if (_db is null || _connection is null)
            return;
        try
        {
            _efTransaction!.Dispose();
            _transaction!.Dispose(); // Geri alma: okuma hiçbir şey yazmadı.
        }
        finally
        {
            // Havuzdaki bağlantı sonraki kullanıcıya yazılabilir dönmeli.
            if (_connection.State == System.Data.ConnectionState.Open)
                Calistir(_connection, "PRAGMA query_only = 0;");
            _db.Database.CloseConnection();
        }
    }

    private static void Calistir(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

public static class OkumaAnlikGoruntusuUzantilari
{
    /// <summary>Okuma boyunca tutarlı, yazmaya kapalı anlık görüntü başlatır (bkz. <see cref="OkumaAnlikGoruntusu"/>).</summary>
    public static OkumaAnlikGoruntusu OkumaBaslat(this KasaDbContext db) => OkumaAnlikGoruntusu.Baslat(db);
}
