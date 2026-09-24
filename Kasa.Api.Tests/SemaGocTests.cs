using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>SemaGuncelleyici: var olan (canlı benzeri) DB'lerin güvenli göçü.</summary>
public class SemaGocTests
{
    // Canlı DB'nin olası hali: master'daki EnsureCreated şeması (oturum sürümü sütunları yok,
    // tekil index yok) + Islemler.KrediKartiId planın el-SQL'iyle ALTER ile eklendiği için FK'siz.
    private static readonly string[] CanliSema =
    [
        "CREATE TABLE \"Ayarlar\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Ayarlar\" PRIMARY KEY AUTOINCREMENT, \"TakipBaslangic\" TEXT NOT NULL, \"KasaAcilisDevri\" TEXT NOT NULL, \"IzleyiciSifreHash\" TEXT NULL)",
        "CREATE TABLE \"Cariler\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Cariler\" PRIMARY KEY AUTOINCREMENT, \"Ad\" TEXT NOT NULL, \"Aktif\" INTEGER NOT NULL)",
        "CREATE TABLE \"Gelenler\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Gelenler\" PRIMARY KEY AUTOINCREMENT, \"DonemStart\" TEXT NOT NULL, \"Kanal\" TEXT NOT NULL, \"TutarTl\" TEXT NOT NULL)",
        "CREATE TABLE \"Kanallar\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Kanallar\" PRIMARY KEY AUTOINCREMENT, \"Ad\" TEXT NOT NULL, \"Aktif\" INTEGER NOT NULL, \"Sira\" INTEGER NOT NULL, \"AcilisDevri\" TEXT NOT NULL)",
        "CREATE TABLE \"KrediKartlari\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_KrediKartlari\" PRIMARY KEY AUTOINCREMENT, \"Ad\" TEXT NOT NULL, \"KesimTarihi\" TEXT NOT NULL, \"SonOdemeTarihi\" TEXT NOT NULL, \"Limit\" TEXT NOT NULL, \"Borc\" TEXT NOT NULL)",
        "CREATE TABLE \"Islemler\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_Islemler\" PRIMARY KEY AUTOINCREMENT, \"Tarih\" TEXT NOT NULL, \"Cari\" TEXT NOT NULL, \"TutarTl\" TEXT NOT NULL, \"Kanal\" TEXT NOT NULL, \"Tip\" INTEGER NOT NULL, \"Not\" TEXT NULL)",
        "ALTER TABLE Islemler ADD COLUMN KrediKartiId INTEGER NULL",
        "CREATE TABLE KartOdemeler (Id INTEGER NOT NULL CONSTRAINT PK_KartOdemeler PRIMARY KEY AUTOINCREMENT, KrediKartiId INTEGER NOT NULL, Tarih TEXT NOT NULL, Tutar TEXT NOT NULL, \"Not\" TEXT NULL, CONSTRAINT FK_KartOdemeler_KrediKartlari_KrediKartiId FOREIGN KEY (KrediKartiId) REFERENCES KrediKartlari (Id) ON DELETE CASCADE)",
        "CREATE INDEX IX_KartOdemeler_KrediKartiId ON KartOdemeler (KrediKartiId)",
        "CREATE INDEX IX_Islemler_KrediKartiId ON Islemler (KrediKartiId)",
    ];

    private static readonly string[] CanliVeri =
    [
        "INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri) VALUES ('2026-06-01', '1000.0')",
        "INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1,'MEZAT',1,0,'0.0'),(2,'PERAKENDE',1,1,'0.0'),(3,'TOPTAN',1,2,'0.0'),(4,'MEZAT',1,9,'5.0'),(5,'MEZAT',1,9,'0.0')",
        "INSERT INTO Cariler (Id, Ad, Aktif) VALUES (1,'Market',1),(2,'Market',1)",
        // 10 Haziran → dönem 8 Haziran'a çekilir ve 8 Haziran satırıyla çakışır: en son yazılan (büyük Id) kalır.
        "INSERT INTO Gelenler (Id, DonemStart, Kanal, TutarTl) VALUES (1,'2026-06-08','MEZAT','100.0'),(2,'2026-06-10','MEZAT','200.0'),(3,'2026-06-15','MEZAT','1.0'),(4,'2026-06-15','MEZAT','2.0'),(5,'2026-05-20','MEZAT','9.0'),(6,'2026-06-08','TOPTAN','3.0')",
        "INSERT INTO KrediKartlari (Id, Ad, KesimTarihi, SonOdemeTarihi, \"Limit\", Borc) VALUES (1,'Bonus','2026-07-05','2026-07-25','1000.0','0.0')",
        "INSERT INTO Islemler (Id, Tarih, Cari, TutarTl, Kanal, Tip, KrediKartiId) VALUES (1,'2026-06-10','Market','10.0','MEZAT',2,1),(2,'2026-06-11','Market','20.0','MEZAT',2,9999),(3,'2026-06-12','Serbest Yazı','30.0','MEZAT',0,NULL),(4,'2026-06-13','Kira','40.0','Ortak',1,NULL),(5,'2026-06-14','Kira','41.0','Ortak',1,NULL)",
    ];

    private static KasaDbContext Ac(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);

    private static void Calistir(SqliteConnection conn, IEnumerable<string> sqller)
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

    [Fact]
    public void Canli_benzeri_db_tekillestirilir_indexler_ve_FK_eklenir_veri_korunur()
    {
        var klasor = Path.Combine(Path.GetTempPath(), "kasa-goc-" + Guid.NewGuid().ToString("N"));
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Calistir(conn, CanliSema);
        Calistir(conn, CanliVeri);
        try
        {
            using var db = Ac(conn);
            var yapilan = SemaGuncelleyici.Guncelle(db, NullLogger.Instance, klasor);

            Assert.Contains("tablo+ IptalEdilenTokenlar", yapilan);
            Assert.Contains("sütun+ Ayarlar.EditorOturumSurumu", yapilan);
            Assert.Contains("yeniden kuruldu (FK) Islemler", yapilan);
            Assert.Contains("index+ IX_Kanallar_Ad", yapilan);
            Assert.Contains("index+ IX_Gelenler_DonemStart_Kanal", yapilan);
            Assert.Contains("index+ IX_Cariler_Ad", yapilan);
            Assert.Contains("tablo+ GiderKalemleri", yapilan);
            // Sabit gider işlemlerindeki ad gider kalemi olarak eklenir (cari listesine değil).
            Assert.Equal(["Kira"], db.GiderKalemleri.Select(k => k.Ad).ToList());

            // Göç öncesi yedek alındı.
            Assert.Single(Directory.GetFiles(klasor, "kasa-once-*.db"));

            // Çift kanal: en küçük Id kalır.
            Assert.Equal([1, 2, 3], db.Kanallar.OrderBy(k => k.Id).Select(k => k.Id).ToList());
            // Çift cari: en küçük Id kalır; işlemdeki listede olmayan cari eklenir.
            Assert.Equal(["Market", "Serbest Yazı"], db.Cariler.OrderBy(c => c.Id).Select(c => c.Ad).ToList());
            // Gelen: dönem başına çekildi, çiftlerde en son yazılan (büyük Id) kaldı; takip öncesi satır dokunulmadı.
            var gelen = db.Gelenler.OrderBy(g => g.Id).Select(g => new { g.Id, g.DonemStart, g.Kanal, g.TutarTl }).ToList();
            Assert.Equal(4, gelen.Count);
            Assert.Contains(gelen, g => g.Id == 2 && g.DonemStart == new DateOnly(2026, 6, 8) && g.TutarTl == 200m);
            Assert.Contains(gelen, g => g.Id == 4 && g.DonemStart == new DateOnly(2026, 6, 15) && g.TutarTl == 2m);
            Assert.Contains(gelen, g => g.Id == 5 && g.DonemStart == new DateOnly(2026, 5, 20));
            Assert.Contains(gelen, g => g.Id == 6 && g.Kanal == "TOPTAN");

            // İşlemler korunur; yetim kart bağı NULL'lanır; FK artık tanımlı ve çalışır.
            Assert.Equal(5, db.Islemler.Count());
            Assert.Equal(1, db.Islemler.Single(i => i.Id == 1).KrediKartiId);
            Assert.Null(db.Islemler.Single(i => i.Id == 2).KrediKartiId);
            Assert.Contains("KrediKartlari", Oku(conn, "SELECT \"table\" FROM pragma_foreign_key_list('Islemler')"));
            Assert.Contains("IX_Islemler_KrediKartiId", Oku(conn, "SELECT name FROM pragma_index_list('Islemler')"));
            Assert.Empty(Oku(conn, "SELECT \"table\" FROM pragma_foreign_key_check"));
            Calistir(conn, ["PRAGMA foreign_keys = ON"]);
            Assert.ThrowsAny<SqliteException>(() => Calistir(conn,
                ["INSERT INTO Islemler (Tarih, Cari, TutarTl, Kanal, Tip, KrediKartiId) VALUES ('2026-06-01','Market','1.0','MEZAT',2,4242)"]));

            // İdempotent: ikinci çalıştırma bir şey yapmaz ve yeni yedek almaz.
            Assert.Empty(SemaGuncelleyici.Guncelle(db, NullLogger.Instance, klasor));
            Assert.Single(Directory.GetFiles(klasor, "kasa-once-*.db"));
        }
        finally
        {
            if (Directory.Exists(klasor)) Directory.Delete(klasor, true);
        }
    }

    [Theory]
    // Yeniden adlandırma şüphesi: Borc yok, AcilisBorc var.
    [InlineData("KrediKartlari", "CREATE TABLE \"KrediKartlari\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, \"Ad\" TEXT NOT NULL, \"KesimTarihi\" TEXT NOT NULL, \"SonOdemeTarihi\" TEXT NOT NULL, \"Limit\" TEXT NOT NULL, \"AcilisBorc\" TEXT NOT NULL)", "yeniden adlandırma")]
    // Tip değişikliği.
    [InlineData("Kanallar", "CREATE TABLE \"Kanallar\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, \"Ad\" TEXT NOT NULL, \"Aktif\" INTEGER NOT NULL, \"Sira\" TEXT NOT NULL, \"AcilisDevri\" TEXT NOT NULL)", "tip değişmiş")]
    // NULL kuralı değişikliği.
    [InlineData("Cariler", "CREATE TABLE \"Cariler\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, \"Ad\" TEXT NULL, \"Aktif\" INTEGER NOT NULL)", "NULL kuralı")]
    // Modelden kaldırılmış, varsayılansız NOT NULL sütun.
    [InlineData("Cariler", "CREATE TABLE \"Cariler\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, \"Ad\" TEXT NOT NULL, \"Aktif\" INTEGER NOT NULL, \"Eski\" INTEGER NOT NULL)", "NOT NULL ve varsayılansız")]
    public void Desteklenmeyen_sema_farki_acilisi_durdurur_ve_hicbir_sey_degistirmez(string tablo, string ddl, string beklenen)
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Calistir(conn, CanliSema.Where(s => !s.Contains($"TABLE \"{tablo}\"")).Append(ddl));
        using var db = Ac(conn);

        var ex = Assert.Throws<InvalidOperationException>(() => SemaGuncelleyici.Guncelle(db));
        Assert.Contains(beklenen, ex.Message);
        Assert.DoesNotContain("IptalEdilenTokenlar", Oku(conn, "SELECT name FROM sqlite_master WHERE type='table'"));
    }

    [Fact]
    public void Modelde_olmayan_nullable_sutun_uyariyla_birakilir()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Calistir(conn, CanliSema);
        Calistir(conn, ["ALTER TABLE Cariler ADD COLUMN Telefon TEXT NULL"]);
        using var db = Ac(conn);
        SemaGuncelleyici.Guncelle(db);
        Assert.Contains("Telefon", Oku(conn, "SELECT name FROM pragma_table_info('Cariler')"));
    }

    [Fact]
    public void Zorunlu_FKde_yetim_kayit_acilisi_durdurur()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Calistir(conn, CanliSema);
        Calistir(conn, ["PRAGMA foreign_keys = OFF", "INSERT INTO KartOdemeler (KrediKartiId, Tarih, Tutar) VALUES (77, '2026-06-01', '5.0')"]);
        using var db = Ac(conn);
        var ex = Assert.Throws<InvalidOperationException>(() => SemaGuncelleyici.Guncelle(db));
        Assert.Contains("KartOdemeler.KrediKartiId", ex.Message);
    }

    [Fact]
    public void Acilista_WAL_modu_zorlanir()
    {
        var dosya = Path.Combine(Path.GetTempPath(), "kasa-wal-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            // VACUUM INTO yedeğinden geri yüklenmiş gibi: DELETE modunda bir DB.
            using (var c = new SqliteConnection($"Data Source={dosya}"))
            {
                c.Open();
                Calistir(c, ["PRAGMA journal_mode=DELETE"]);
                Calistir(c, CanliSema);
                Calistir(c, CanliVeri.Take(1));
            }
            using (var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite($"Data Source={dosya}").Options))
                VeritabaniBaslatici.Baslat(db, NullLogger.Instance, yedekKlasoru: null);

            using var k = new SqliteConnection($"Data Source={dosya}");
            k.Open();
            Assert.Equal("wal", Oku(k, "PRAGMA journal_mode").Single());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { dosya, dosya + "-wal", dosya + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }
}

public class YedekTests
{
    [Fact]
    public void Yedek_basarisizsa_var_olan_gunluk_yedek_bozulmaz()
    {
        var klasor = Path.Combine(Path.GetTempPath(), "kasa-yedek-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        try
        {
            var gunluk = YedekServisi.GunlukDosya(klasor, new DateOnly(2026, 9, 24));
            File.WriteAllText(gunluk, "onceki-yedek");
            // Açılamayan DB: VACUUM INTO başarısız olur.
            using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>()
                .UseSqlite($"Data Source={Path.Combine(klasor, "yok", "kasa.db")}").Options);
            Assert.ThrowsAny<Exception>(() => YedekServisi.YedekAl(db, klasor, new DateOnly(2026, 9, 24), 30));
            Assert.Equal("onceki-yedek", File.ReadAllText(gunluk));
            Assert.Single(Directory.GetFiles(klasor)); // geçici dosya kalmadı
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(klasor, true);
        }
    }

    [Fact]
    public void Saklama_yalniz_gunluk_yedek_adlarina_uygulanir()
    {
        var klasor = Path.Combine(Path.GetTempPath(), "kasa-yedek-" + Guid.NewGuid().ToString("N"));
        var dbDosya = Path.Combine(Path.GetTempPath(), "kasa-" + Guid.NewGuid().ToString("N") + ".db");
        Directory.CreateDirectory(klasor);
        try
        {
            foreach (var elle in new[] { "kasa-manual-2026.db", "kasa-zz-elle.db", "kasa-once-20260901-101010.db" })
                File.WriteAllText(Path.Combine(klasor, elle), "elle");
            using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite($"Data Source={dbDosya}").Options);
            db.Database.EnsureCreated();
            for (int g = 1; g <= 4; g++)
                YedekServisi.YedekAl(db, klasor, new DateOnly(2026, 9, g), sakla: 2);

            var dosyalar = Directory.GetFiles(klasor).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.Equal(["kasa-2026-09-03.db", "kasa-2026-09-04.db", "kasa-manual-2026.db", "kasa-once-20260901-101010.db", "kasa-zz-elle.db"], dosyalar);
            Assert.NotNull(YedekServisi.SonYedekZamani(klasor));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(klasor, true);
            if (File.Exists(dbDosya)) File.Delete(dbDosya);
        }
    }
}
