using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>
/// Paket F şema göçü: master şemasındaki (belge sütunları ve ek/POS tabloları olmayan) bir DB açılışta
/// güncellenir, var olan veri korunur, yeni sütunlar boş/varsayılan gelir.
/// </summary>
public class PaketFSemaGocTests
{
    private static KasaDbContext Ac(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);

    private static void Calistir(SqliteConnection conn, params string[] sqller)
    {
        foreach (var sql in sqller)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private static List<string> Oku(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(Convert.ToString(r.GetValue(0))!);
        return l;
    }

    /// <summary>Güncel modeli kurar, Paket F eklerini kaldırır: sonuç master'ın EnsureCreated şemasıdır.</summary>
    private static void MasterSemasiKur(SqliteConnection conn)
    {
        using (var db = Ac(conn)) db.Database.EnsureCreated();
        Calistir(conn,
            "DROP TABLE PosSatislari",
            "DROP TABLE PosTanimlari",
            "DROP TABLE IslemEkleri",
            "ALTER TABLE Islemler DROP COLUMN BelgeTuru",
            "ALTER TABLE Islemler DROP COLUMN BelgeNo",
            "ALTER TABLE Islemler DROP COLUMN FaturaBekleniyor");
    }

    [Fact]
    public void Master_semali_db_guncellenir_veri_korunur_yeni_alanlar_bos_gelir()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        MasterSemasiKur(conn);
        Assert.DoesNotContain("BelgeTuru", Oku(conn, "SELECT name FROM pragma_table_info('Islemler')"));
        Calistir(conn,
            "INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1,'MEZAT',1,0,'0.0')",
            "INSERT INTO Cariler (Id, Ad, Aktif) VALUES (1,'Market',1)",
            "INSERT INTO Islemler (Id, Tarih, Cari, TutarTl, Kanal, Tip, \"Not\", KrediKartiId) VALUES " +
            "(1,'2026-09-01','Market','10.5','MEZAT',0,'eski not',NULL),(2,'2026-09-02','Market','20.0','MEZAT',0,NULL,NULL)");

        using var db = Ac(conn);
        var yapilan = SemaGuncelleyici.Guncelle(db, NullLogger.Instance, null);

        Assert.Contains("tablo+ IslemEkleri", yapilan);
        Assert.Contains("tablo+ PosTanimlari", yapilan);
        Assert.Contains("tablo+ PosSatislari", yapilan);
        Assert.Contains("sütun+ Islemler.BelgeTuru", yapilan);
        Assert.Contains("sütun+ Islemler.BelgeNo", yapilan);
        Assert.Contains("sütun+ Islemler.FaturaBekleniyor", yapilan);
        Assert.Empty(SemaGuncelleyici.Guncelle(db, NullLogger.Instance, null));   // idempotent
        // Belge sütunlarını bilmeyen bir yazıcı (eski araç / elle SQL) hâlâ işlem ekleyebilir.
        Calistir(conn, "INSERT INTO Islemler (Id, Tarih, Cari, TutarTl, Kanal, Tip) VALUES (3,'2026-09-03','Market','1.0','MEZAT',0)");
        Assert.Equal(["0"], Oku(conn, "SELECT FaturaBekleniyor FROM Islemler WHERE Id = 3"));

        var islemler = db.Islemler.AsNoTracking().OrderBy(i => i.Id).ToList();
        Assert.Equal([10.5m, 20m, 1m], islemler.Select(i => i.TutarTl));
        Assert.Equal("eski not", islemler[0].Not);
        Assert.All(islemler, i => Assert.Equal((null, null, false), (i.BelgeTuru, i.BelgeNo, i.FaturaBekleniyor)));

        // Yeni tablolar ve sütunlar kullanılabilir; FK'ler kurulu.
        var k = db.Kanallar.Single();
        var pos = new PosTanimEntity { Ad = "POS", KanalId = k.Id, KomisyonOrani = 1.5m, BlokajGunu = 1 };
        db.PosTanimlari.Add(pos);
        db.SaveChanges();
        db.PosSatislari.Add(new PosSatisEntity { Tarih = new DateOnly(2026, 9, 3), PosId = pos.Id, BrutTutar = 100m, KomisyonOrani = 1.5m, BlokajGunu = 1 });
        db.IslemEkleri.Add(new IslemEkiEntity { IslemId = 1, OrijinalAd = "a.pdf", DepoAdi = new string('a', 32) + ".pdf", IcerikTipi = "application/pdf", Boyut = 3 });
        var i1 = db.Islemler.Single(i => i.Id == 1);
        i1.BelgeTuru = BelgeTuru.EFatura; i1.BelgeNo = "F1"; i1.FaturaBekleniyor = true;
        db.SaveChanges();
        // Satış: POS'a (Restrict) ve kayıt anında sabitlenen kanala (SetNull) bağlı.
        Assert.Equal(["Kanallar", "PosTanimlari"], Oku(conn, "SELECT \"table\" FROM pragma_foreign_key_list('PosSatislari')").Order());
        Assert.Equal(["Kanallar"], Oku(conn, "SELECT \"table\" FROM pragma_foreign_key_list('PosTanimlari')"));
        Assert.Throws<DbUpdateException>(() =>
        {
            db.PosSatislari.Add(new PosSatisEntity { Tarih = new DateOnly(2026, 9, 3), PosId = 9999, BrutTutar = 1m });
            db.SaveChanges();
        });
    }

    /// <summary>
    /// Islemler'i KrediKartiId FK'si OLMADAN yeniden kurar (canlıdaki eski, FK'siz tablo gibi): güncelleyici
    /// tabloyu yeniden kurmak zorunda kalır. Boş tabloda yapılır; veri sonra eklenir.
    /// </summary>
    private static void FksizIslemlerKur(SqliteConnection conn)
    {
        var sql = Oku(conn, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Islemler'").Single();
        var fksiz = System.Text.RegularExpressions.Regex.Replace(sql,
            @",\s*CONSTRAINT\s+""FK_Islemler_[^""]+""\s+FOREIGN KEY[^,)]*\)\s*REFERENCES\s+""[^""]+""\s*\([^)]*\)(\s+ON DELETE [A-Z ]+?)?(?=\s*[,)])", "");
        Assert.NotEqual(sql, fksiz);
        Assert.Contains("AUTOINCREMENT", fksiz);
        Calistir(conn, "PRAGMA foreign_keys = OFF", "DROP TABLE Islemler", fksiz, "PRAGMA foreign_keys = ON");
        Assert.Empty(Oku(conn, "SELECT \"table\" FROM pragma_foreign_key_list('Islemler')"));
    }

    private static long Sayac(SqliteConnection conn, string tablo)
        => long.Parse(Oku(conn, $"SELECT seq FROM sqlite_sequence WHERE name = '{tablo}'").Single());

    [Fact]
    public void FK_icin_yeniden_kurulan_tablo_AUTOINCREMENT_sayacini_korur_silinen_Id_yeni_isleme_verilmez()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var db0 = Ac(conn)) db0.Database.EnsureCreated();
        FksizIslemlerKur(conn);
        Calistir(conn,
            "INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1,'MEZAT',1,0,'0.0')",
            "INSERT INTO Islemler (Id, Tarih, Cari, TutarTl, Kanal, Tip) VALUES (1,'2026-09-01','A','1.0','MEZAT',0),(2,'2026-09-02','B','2.0','MEZAT',0),(3,'2026-09-03','C','3.0','MEZAT',0)",
            // İşlem 3'ün eki; işlem API dışından (eski yol) silinmiş, ek hâlâ Id 3'ü gösteriyor.
            "INSERT INTO IslemEkleri (IslemId, OrijinalAd, DepoAdi, IcerikTipi, Boyut, YuklemeZamaniUtc) VALUES (3,'fis.jpg','" + new string('d', 32) + ".jpg','image/jpeg',10,'2026-09-03 10:00:00')",
            "DELETE FROM Islemler WHERE Id = 3");
        Assert.Equal(3, Sayac(conn, "Islemler"));

        using var db = Ac(conn);
        var yapilan = SemaGuncelleyici.Guncelle(db, NullLogger.Instance, null);

        Assert.Contains("yeniden kuruldu (FK) Islemler", yapilan);
        Assert.Equal(["KrediKartlari"], Oku(conn, "SELECT \"table\" FROM pragma_foreign_key_list('Islemler')"));
        Assert.Equal(3, Sayac(conn, "Islemler"));   // DROP + RENAME sayacı MAX(Id)=2'ye düşürmedi
        Assert.Equal(["1", "2"], Oku(conn, "SELECT Id FROM Islemler ORDER BY Id"));   // veri aynen taşındı

        var yeni = new IslemEntity { Tarih = new DateOnly(2026, 9, 4), Cari = "D", TutarTl = 4m, Kanal = "MEZAT" };
        db.Islemler.Add(yeni);
        db.SaveChanges();
        Assert.Equal(4, yeni.Id);
        Assert.Equal(0, db.IslemEkleri.Count(e => e.IslemId == yeni.Id));   // eski işlemin fişini devralmadı
        Assert.Empty(SemaGuncelleyici.Guncelle(db, NullLogger.Instance, null));   // idempotent
    }

    [Fact]
    public void Yeniden_kurulan_bos_tablo_da_sayacini_korur_hic_kayit_girilmemis_tabloda_sayac_uydurulmaz()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var db0 = Ac(conn)) db0.Database.EnsureCreated();
        FksizIslemlerKur(conn);
        // Hiç kayıt yok: korunacak sayaç yok; yeniden kurulum sayaç uydurmaz (SQLite en fazla 0 yazar).
        using (var db1 = Ac(conn)) Assert.Contains("yeniden kuruldu (FK) Islemler", SemaGuncelleyici.Guncelle(db1, NullLogger.Instance, null));
        Assert.All(Oku(conn, "SELECT seq FROM sqlite_sequence WHERE name = 'Islemler'"), s => Assert.Equal("0", s));

        // Tüm kayıtları silinmiş tablo: sayaç 7'de kalır, yeni kayıt 8 olur (1 değil).
        FksizIslemlerKur(conn);
        Calistir(conn,
            "INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1,'MEZAT',1,0,'0.0')",
            "INSERT INTO Islemler (Id, Tarih, Cari, TutarTl, Kanal, Tip) VALUES (7,'2026-09-01','A','1.0','MEZAT',0)",
            "DELETE FROM Islemler");
        using var db = Ac(conn);
        Assert.Contains("yeniden kuruldu (FK) Islemler", SemaGuncelleyici.Guncelle(db, NullLogger.Instance, null));
        Assert.Equal(7, Sayac(conn, "Islemler"));
        var yeni = new IslemEntity { Tarih = new DateOnly(2026, 9, 4), Cari = "D", TutarTl = 4m, Kanal = "MEZAT" };
        db.Islemler.Add(yeni);
        db.SaveChanges();
        Assert.Equal(8, yeni.Id);
    }
}
