using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>
/// Paket D şema göçü: master şemasındaki bir DB (çek/sayım/tekrarlayan tablolarında yeni sütunlar yok,
/// kart mutabakatı tablosu yok) açılışta güncellenir ve eski veri aynen kalır.
/// </summary>
public class PaketDSemaTests
{
    private static void Calistir(SqliteConnection conn, params string[] sqller)
    {
        foreach (var sql in sqller)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private static List<string> Sutunlar(SqliteConnection conn, string tablo)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM pragma_table_info('{tablo}')";
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    /// <summary>Güncel modelden kurulup Paket D eklerinden arındırılmış (master'daki haliyle) DB.</summary>
    private static SqliteConnection MasterSemasi()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options))
            db.Database.EnsureCreated();
        Calistir(conn,
            "PRAGMA foreign_keys = OFF",
            "DROP TABLE \"KartMutabakatlari\"",
            "ALTER TABLE \"Cekler\" DROP COLUMN \"Tur\"",
            "ALTER TABLE \"Cekler\" DROP COLUMN \"Konum\"",
            "ALTER TABLE \"Cekler\" DROP COLUMN \"CiroEdilenCari\"",
            "ALTER TABLE \"KasaSayimlari\" DROP COLUMN \"SatirlarJson\"",
            "ALTER TABLE \"KasaSayimlari\" DROP COLUMN \"FarkDurumu\"",
            "ALTER TABLE \"KasaSayimlari\" DROP COLUMN \"FarkAciklamasi\"",
            // Kart FK'si ve index'i olan sütun DROP COLUMN ile atılamaz: master tablosu yeniden kurulur.
            "DROP TABLE \"TekrarlayanGiderler\"",
            "CREATE TABLE \"TekrarlayanGiderler\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_TekrarlayanGiderler\" PRIMARY KEY AUTOINCREMENT, " +
            "\"Kalem\" TEXT NOT NULL, \"Kanal\" TEXT NOT NULL, \"Tutar\" TEXT NOT NULL, \"AyinGunu\" INTEGER NOT NULL, \"Aktif\" INTEGER NOT NULL, \"BaslangicAyi\" TEXT NOT NULL)",
            "PRAGMA foreign_keys = ON");
        Calistir(conn,
            "INSERT INTO \"Ayarlar\" (\"TakipBaslangic\", \"KasaAcilisDevri\", \"IzleyiciOturumSurumu\", \"EditorOturumSurumu\") VALUES ('2026-06-01', '0.0', 0, 0)",
            "INSERT INTO \"KrediKartlari\" (\"Id\", \"Ad\", \"KesimTarihi\", \"SonOdemeTarihi\", \"Limit\", \"Borc\") VALUES (1, 'Bonus', '2026-01-15', '2026-01-25', '1000.0', '0.0')",
            "INSERT INTO \"Cekler\" (\"Id\", \"Yon\", \"Kisi\", \"Tutar\", \"DuzenlemeTarihi\", \"VadeTarihi\", \"Kanal\", \"Durum\", \"IslemTarihi\") " +
            "VALUES (7, 0, 'Eski Çek', '1250.5', '2026-06-01', '2026-07-01', 'MEZAT', 1, '2026-07-01')",
            "INSERT INTO \"KasaSayimlari\" (\"Id\", \"Tarih\", \"SayilanTutar\", \"HesaplananTutar\", \"Not\", \"KayitZamaniUtc\") " +
            "VALUES (3, '2026-07-10', '500.0', '480.0', 'eski sayım', '2026-07-10 10:00:00')",
            "INSERT INTO \"TekrarlayanGiderler\" (\"Id\", \"Kalem\", \"Kanal\", \"Tutar\", \"AyinGunu\", \"Aktif\", \"BaslangicAyi\") " +
            "VALUES (4, 'Kira', 'Ortak', '15000.0', 5, 1, '2026-06-01')",
            "INSERT INTO \"TekrarlayanGirisler\" (\"TekrarlayanGiderId\", \"Ay\", \"Durum\", \"Zaman\") VALUES (4, '2026-06-01', 1, '2026-06-05 10:00:00')");
        return conn;
    }

    [Fact]
    public void Master_semasindaki_db_guncellenir_veri_korunur()
    {
        using var conn = MasterSemasi();
        Assert.DoesNotContain("Tur", Sutunlar(conn, "Cekler"));
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);

        var yapilan = SemaGuncelleyici.Guncelle(db, NullLogger.Instance);

        Assert.Contains("tablo+ KartMutabakatlari", yapilan);
        foreach (var s in new[] { "Cekler.Tur", "Cekler.Konum", "Cekler.CiroEdilenCari", "KasaSayimlari.SatirlarJson",
                                  "KasaSayimlari.FarkDurumu", "KasaSayimlari.FarkAciklamasi", "TekrarlayanGiderler.Siklik",
                                  "TekrarlayanGiderler.KrediKartiId", "TekrarlayanGiderler.TutarDegisken" })
            Assert.Contains($"sütun+ {s}", yapilan);
        Assert.Contains("yeniden kuruldu (FK) TekrarlayanGiderler", yapilan);

        // Eski satırlar varsayılanları alır; eski değerler aynen durur.
        var cek = db.Cekler.AsNoTracking().Single();
        Assert.Equal((7, "Eski Çek", 1250.5m, CekDurumu.TahsilEdildi), (cek.Id, cek.Kisi, cek.Tutar, cek.Durum));
        Assert.Equal((CekTuru.Cek, CekKonumu.Elde, (string?)null), (cek.Tur, cek.Konum, cek.CiroEdilenCari));
        var sayim = db.KasaSayimlari.AsNoTracking().Single();
        Assert.Equal((500m, 480m, "eski sayım"), (sayim.SayilanTutar, sayim.HesaplananTutar, sayim.Not));
        Assert.Equal((SayimFarkDurumu.Acik, (string?)null, (string?)null), (sayim.FarkDurumu, sayim.FarkAciklamasi, sayim.SatirlarJson));
        var t = db.TekrarlayanGiderler.AsNoTracking().Single();
        Assert.Equal((4, "Kira", 15000m, 5), (t.Id, t.Kalem, t.Tutar, t.AyinGunu));
        Assert.Equal((TekrarSikligi.Aylik, (int?)null, false), (t.Siklik, t.KrediKartiId, t.TutarDegisken));
        Assert.Single(db.TekrarlayanGirisler.AsNoTracking());   // bağlı karar yeniden kurulumda kalır

        // Yeni alanlar yazılabilir; kart silinince tekrarlayan giderin kart bağı kopar (SET NULL).
        t = db.TekrarlayanGiderler.Single();
        t.KrediKartiId = 1;
        db.KartMutabakatlari.Add(new KartMutabakatEntity { KrediKartiId = 1, DonemBaslangic = new DateOnly(2026, 6, 16), DonemBitis = new DateOnly(2026, 7, 15), EkstreTutari = 5m });
        db.SaveChanges();
        db.KrediKartlari.Where(k => k.Id == 1).ExecuteDelete();
        Assert.Null(db.TekrarlayanGiderler.AsNoTracking().Single().KrediKartiId);
        Assert.Empty(db.KartMutabakatlari.AsNoTracking());

        Assert.Empty(SemaGuncelleyici.Guncelle(db, NullLogger.Instance));   // idempotent
    }
}
