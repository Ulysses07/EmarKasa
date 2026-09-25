using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Tests;

public class DatabaseMigrationTests
{
    [Fact]
    public void Kayitli_ilk_migration_alis_semasina_gecerken_finansal_veriyi_korur()
    {
        using var connection = Open();
        using var db = Context(connection);
        db.GetService<IMigrator>().Migrate("20260919000100_InitialStableSchema");
        Execute(connection, "INSERT INTO Kanallar VALUES (1, 'MEZAT', 1, 0, '125.50'); INSERT INTO Gelenler VALUES (1, '2026-09-01', 'MEZAT', '42.75', 1);");

        KasaDatabaseInitializer.Initialize(db);

        Assert.Equal(10, db.Database.GetAppliedMigrations().Count());
        Assert.Equal(42.75m, Assert.Single(db.Gelenler).TutarTl);
        Assert.Equal(125.50m, Assert.Single(db.Kanallar).AcilisDevri);
        Assert.Empty(db.Alislar);
        Assert.Empty(db.Alicilar);
        Assert.Empty(db.AlisOdemeler);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Bos_veritabani_migration_ile_kurulur_ve_model_snapshot_eslesir()
    {
        using var connection = Open();
        using var db = Context(connection);

        KasaDatabaseInitializer.Initialize(db);
        KasaDatabaseInitializer.Initialize(db);

        Assert.Equal(10, db.Database.GetAppliedMigrations().Count());
        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(db.Islemler);
        Assert.Empty(db.Krediler);
        Assert.Empty(db.KartOdemeler);
    }

    [Fact]
    public void Eski_temel_sema_kart_ve_kredi_tablolari_olmadan_veri_korumali_yukselir()
    {
        using var connection = Open();
        LegacyBase(connection);
        using var db = Context(connection);

        KasaDatabaseInitializer.Initialize(db);

        var kanal = Assert.Single(db.Kanallar);
        Assert.Equal(7, kanal.Id);
        Assert.Equal(1234.56m, kanal.AcilisDevri);
        var islem = Assert.Single(db.Islemler.Include(i => i.KanalKaydi));
        Assert.Equal(11, islem.Id);
        Assert.Equal(345.67m, islem.TutarTl);
        Assert.Equal("Özgün açıklama", islem.Not);
        Assert.Equal(7, islem.KanalId);
        Assert.Same(kanal, islem.KanalKaydi);
        Assert.Null(islem.KrediKartiId);
        Assert.Equal(7, Assert.Single(db.Gelenler).KanalId);
        Assert.Equal("saklanan-hash", Assert.Single(db.Ayarlar).IzleyiciSifreHash);
        Assert.Equal("Firma", Assert.Single(db.Cariler).Ad);
        Assert.Empty(db.KrediKartlari);
        Assert.Empty(db.Krediler);
        Assert.Empty(db.KartOdemeler);
        Assert.Equal(10, db.Database.GetAppliedMigrations().Count());
        Assert.Equal(1L, Scalar(connection, "PRAGMA foreign_keys;"));

        // Tekrar başlatma ne veri ne yeni migration kaydı üretir.
        KasaDatabaseInitializer.Initialize(db);
        Assert.Single(db.Islemler);
        Assert.Equal(10, db.Database.GetAppliedMigrations().Count());
    }

    [Fact]
    public void EnsureCreated_tam_semasi_gercek_fk_ve_silme_kurallariyla_veri_kaybetmeden_benimsenir()
    {
        using var connection = Open();
        using var db = Context(connection);
        // Migration geçmişi olmayan, alış tablolarından önceki sabit şema.
        foreach (var operation in new Kasa.Api.Migrations.InitialStableSchema().UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
            Execute(connection, operation.Sql);
        var kanal = new KanalEntity { Ad = "MEZAT" };
        var kart = new KrediKartiEntity { Ad = "Kart", KesimTarihi = new(2026, 9, 10), SonOdemeTarihi = new(2026, 9, 20) };
        db.AddRange(kanal, kart);
        db.SaveChanges();
        db.Islemler.Add(new IslemEntity { Tarih = new(2026, 9, 1), Cari = "Firma", Kanal = "MEZAT", KanalId = kanal.Id, KrediKartiId = kart.Id, TutarTl = 100.01m, Tip = GiderTipi.KrediKarti });
        db.KartOdemeler.Add(new KartOdemeEntity { KrediKartiId = kart.Id, Tarih = new(2026, 9, 11), Tutar = 50.02m });
        db.SaveChanges();
        Execute(connection, $"INSERT INTO Gelenler VALUES (1, '2026-09-01', 'MEZAT', '250.03', {kanal.Id});");
        db.ChangeTracker.Clear();

        KasaDatabaseInitializer.Initialize(db);

        Assert.Equal(kart.Id, Assert.Single(db.Islemler).KrediKartiId);
        Assert.Equal(kanal.Id, Assert.Single(db.Islemler).KanalId);
        Assert.Equal(50.02m, Assert.Single(db.KartOdemeler).Tutar);
        Assert.Equal(250.03m, Assert.Single(db.Gelenler).TutarTl);
        Assert.Equal(10, db.Database.GetAppliedMigrations().Count());

        // Geçişten sonra da FK'nin SET NULL ve CASCADE davranışları korunur.
        Execute(connection, "DELETE FROM KrediKartlari;");
        db.ChangeTracker.Clear();
        Assert.Null(Assert.Single(db.Islemler).KrediKartiId);
        Assert.Empty(db.KartOdemeler);
        Assert.Single(db.Gelenler);
    }

    [Fact]
    public void Eski_kart_odemeleri_ve_krediler_tutar_ve_kimlikleriyle_korunur()
    {
        using var connection = Open();
        LegacyBase(connection);
        Execute(connection, """
            CREATE TABLE KrediKartlari (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Ad TEXT NOT NULL,
                KesimTarihi TEXT NOT NULL, SonOdemeTarihi TEXT NOT NULL, "Limit" TEXT NOT NULL, Borc TEXT NOT NULL);
            INSERT INTO KrediKartlari VALUES (5, 'Kart', '2026-09-10', '2026-09-20', '10000.0', '1000.25');
            ALTER TABLE Islemler ADD COLUMN KrediKartiId INTEGER NULL;
            UPDATE Islemler SET KrediKartiId = 5, Tip = 2;
            CREATE TABLE KartOdemeler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, KrediKartiId INTEGER NOT NULL,
                Tarih TEXT NOT NULL, Tutar TEXT NOT NULL, "Not" TEXT NULL);
            INSERT INTO KartOdemeler VALUES (8, 5, '2026-09-11', '200.15', 'Havale');
            CREATE TABLE Krediler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Ad TEXT NOT NULL,
                CekilenTutar TEXT NOT NULL, CekimTarihi TEXT NOT NULL, TaksitSayisi INTEGER NOT NULL,
                AylikOdeme TEXT NOT NULL, OdemeGunu INTEGER NOT NULL, Kanal TEXT NOT NULL);
            INSERT INTO Krediler VALUES (3, 'Kredi', '5000.50', '2026-09-01', 12, '600.75', 15, 'MEZAT');
            """);
        using var db = Context(connection);

        KasaDatabaseInitializer.Initialize(db);

        Assert.Equal(1000.25m, Assert.Single(db.KrediKartlari).Borc);
        Assert.Equal(5, Assert.Single(db.Islemler).KrediKartiId);
        var odeme = Assert.Single(db.KartOdemeler);
        Assert.Equal(8, odeme.Id);
        Assert.Equal(200.15m, odeme.Tutar);
        var kredi = Assert.Single(db.Krediler.Include(k => k.KanalKaydi));
        Assert.Equal(3, kredi.Id);
        Assert.Equal(5000.50m, kredi.CekilenTutar);
        Assert.Equal(600.75m, kredi.AylikOdeme);
        Assert.Equal(7, kredi.KanalId);
        Assert.Equal("MEZAT", kredi.ToCore().Kanal);
    }

    [Fact]
    public void Silinmis_kanal_adlari_pasif_korunur_ozel_etiketler_gercek_kanala_donusmez()
    {
        using var connection = Open();
        LegacyBase(connection);
        Execute(connection, """
            INSERT INTO Islemler VALUES (12, '2026-09-02', 'Firma', '25.50', 'ESKI', 0, NULL);
            INSERT INTO Islemler VALUES (13, '2026-09-03', 'Firma', '10.0', 'ortak', 1, NULL);
            INSERT INTO Gelenler VALUES (13, '2026-09-07', 'eski', '250.0');
            INSERT INTO Gelenler VALUES (14, '2026-09-14', '__kredi__', '5000.0');
            """);
        using var db = Context(connection);

        KasaDatabaseInitializer.Initialize(db);

        var recovered = db.Kanallar.Single(k => k.Ad == "ESKI");
        Assert.False(recovered.Aktif);
        Assert.Equal(recovered.Id, db.Islemler.Single(i => i.Id == 12).KanalId);
        Assert.Equal(recovered.Id, db.Gelenler.Single(g => g.Id == 13).KanalId);
        Assert.Null(db.Islemler.Single(i => i.Id == 13).KanalId);
        Assert.Null(db.Gelenler.Single(g => g.Id == 14).KanalId);
        Assert.Equal(Kanallar.Ortak, db.Islemler.Single(i => i.Id == 13).ToCore().Kanal);
        Assert.Equal(KrediTuretici.KrediKanal, db.Gelenler.Single(g => g.Id == 14).ToCore().Kanal);
        Assert.DoesNotContain(db.Kanallar, k => k.Ad == Kanallar.Ortak || k.Ad == KrediTuretici.KrediKanal);
        Assert.Equal(250m, db.Gelenler.Single(g => g.Id == 13).TutarTl);
    }

    [Theory]
    [InlineData("MEZAT")]
    [InlineData("mezat")]
    public void Yinelenen_gelirlerin_tum_satirlari_tutarlari_ve_adlari_korunur_salt_okunur_isaretlenir(string kanal)
    {
        using var connection = Open();
        LegacyBase(connection);
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO Gelenler VALUES (14, '2026-09-01', $kanal, '999.99');";
            insert.Parameters.AddWithValue("$kanal", kanal);
            insert.ExecuteNonQuery();
        }
        using var db = Context(connection);

        KasaDatabaseInitializer.Initialize(db);
        KasaDatabaseInitializer.Initialize(db);

        Assert.Equal(2L, Scalar(connection, "SELECT COUNT(*) FROM Gelenler;"));
        Assert.Equal("2000.25", Scalar(connection, "SELECT TutarTl FROM Gelenler WHERE Id = 12;"));
        Assert.Equal("999.99", Scalar(connection, "SELECT TutarTl FROM Gelenler WHERE Id = 14;"));
        Assert.Equal(kanal, Scalar(connection, "SELECT Kanal FROM Gelenler WHERE Id = 14;"));
        Assert.Equal("2026-09-01", Scalar(connection, "SELECT DonemStart FROM Gelenler WHERE Id = 14;"));
        Assert.All(db.Gelenler, g => { Assert.True(g.EskiYinelenenGrup); Assert.Equal(7, g.KanalId); });
        Assert.Equal(10, db.Database.GetAppliedMigrations().Count());
        Assert.Equal(1L, Scalar(connection, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public void Yinelenen_kanal_adlari_ve_tanimlanmamis_ek_sutunlar_sessizce_silinmez()
    {
        using var connection = Open();
        LegacyBase(connection);
        Execute(connection, "INSERT INTO Kanallar VALUES (8, 'mezat', 1, 1, '900.0');");
        using var db = Context(connection);
        var duplicate = Assert.Throws<InvalidOperationException>(() => KasaDatabaseInitializer.Initialize(db));
        Assert.Contains("birden fazla kanal", duplicate.Message);
        Assert.Equal(2L, Scalar(connection, "SELECT COUNT(*) FROM Kanallar;"));

        Execute(connection, "DELETE FROM Kanallar WHERE Id = 8; ALTER TABLE Kanallar ADD COLUMN OzelNot TEXT NULL; UPDATE Kanallar SET OzelNot = 'korunmali';");
        var custom = Assert.Throws<InvalidOperationException>(() => KasaDatabaseInitializer.Initialize(db));
        Assert.Contains("tanınmayan alanlar", custom.Message);
        Assert.Equal("korunmali", Scalar(connection, "SELECT OzelNot FROM Kanallar;"));
    }

    [Fact]
    public void Yeni_schema_yinelenen_kanal_gelir_ve_gecersiz_iliskiyi_veritabaninda_reddeder()
    {
        using var connection = Open();
        using var db = Context(connection);
        KasaDatabaseInitializer.Initialize(db);
        Execute(connection, "INSERT INTO Kanallar VALUES (1, 'MEZAT', 1, 0, '0.0');");

        Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO Kanallar VALUES (2, 'mezat', 1, 0, '0.0');"));
        Execute(connection, "INSERT INTO Gelenler (Id, DonemStart, Kanal, TutarTl, KanalId) VALUES (1, '2026-09-01', 'MEZAT', '1.0', 1);");
        Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO Gelenler (Id, DonemStart, Kanal, TutarTl, KanalId) VALUES (2, '2026-09-01', 'MEZAT2', '2.0', 1);"));
        Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO Gelenler (Id, DonemStart, Kanal, TutarTl, KanalId) VALUES (2, '2026-09-01', 'mezat', '2.0', NULL);"));
        Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO Gelenler (Id, DonemStart, Kanal, TutarTl, KanalId) VALUES (2, '2026-09-07', 'olmayan', '2.0', 999);"));
        Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM Kanallar WHERE Id = 1;"));
    }

    [Fact]
    public void Kopuk_kart_iliskisi_gecisi_geri_alir_ve_silinmis_yuksek_kimlikler_yeniden_kullanilmaz()
    {
        using var connection = Open();
        LegacyBase(connection);
        Execute(connection, "ALTER TABLE Islemler ADD COLUMN KrediKartiId INTEGER NULL; UPDATE Islemler SET KrediKartiId = 999;");
        using var db = Context(connection);
        var broken = Assert.Throws<InvalidOperationException>(() => KasaDatabaseInitializer.Initialize(db));
        Assert.Contains("ilişkisi geçersiz", broken.Message);
        Assert.Equal(999L, Scalar(connection, "SELECT KrediKartiId FROM Islemler;"));

        Execute(connection, "UPDATE Islemler SET KrediKartiId = NULL; INSERT INTO Kanallar VALUES (80, 'gecici', 0, 1, '0.0'); DELETE FROM Kanallar WHERE Id = 80;");
        KasaDatabaseInitializer.Initialize(db);
        var yeni = new KanalEntity { Ad = "YENI" };
        db.Kanallar.Add(yeni);
        db.SaveChanges();
        Assert.True(yeni.Id > 80);
    }

    [Fact]
    public void Bosalmis_tablonun_gecmis_autoincrement_degeri_de_korunur()
    {
        using var connection = Open();
        LegacyBase(connection);
        Execute(connection, "INSERT INTO Cariler VALUES (100, 'silinen', 1); DELETE FROM Cariler;");
        using var db = Context(connection);

        KasaDatabaseInitializer.Initialize(db);

        var yeni = new CariEntity { Ad = "Yeni cari" };
        db.Cariler.Add(yeni);
        db.SaveChanges();
        Assert.True(yeni.Id > 100);
    }

    [Theory]
    [InlineData("ortak")]
    [InlineData("__kredi__")]
    public void Ozel_etiket_gercek_kanal_olarak_kayitliysa_belirsiz_gecis_reddedilir(string ad)
    {
        using var connection = Open();
        LegacyBase(connection);
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO Kanallar VALUES (8, $ad, 1, 1, '42.0');";
            insert.Parameters.AddWithValue("$ad", ad);
            insert.ExecuteNonQuery();
        }
        using var db = Context(connection);

        var error = Assert.Throws<InvalidOperationException>(() => KasaDatabaseInitializer.Initialize(db));

        Assert.Contains("özel muhasebe etiketi", error.Message);
        Assert.Equal(2L, Scalar(connection, "SELECT COUNT(*) FROM Kanallar;"));
        Assert.Equal("42.0", Scalar(connection, "SELECT AcilisDevri FROM Kanallar WHERE Id = 8;"));
    }

    [Fact]
    public async Task Eszamanli_iki_baslangic_legacy_verisini_ve_migration_gecmisini_cogaltmaz()
    {
        var path = Path.Combine(Path.GetTempPath(), "kasa-migration-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        try
        {
            using (var seed = new SqliteConnection(connectionString))
            {
                seed.Open();
                LegacyBase(seed);
            }

            using var start = new ManualResetEventSlim(false);
            var initializers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            {
                start.Wait();
                using var connection = new SqliteConnection(connectionString);
                using var db = Context(connection);
                KasaDatabaseInitializer.Initialize(db);
            })).ToArray();
            start.Set();
            await Task.WhenAll(initializers);

            using var verified = new SqliteConnection(connectionString);
            using var verify = Context(verified);
            Assert.Equal(10, verify.Database.GetAppliedMigrations().Count());
            Assert.Single(verify.Kanallar);
            Assert.Single(verify.Islemler);
            Assert.Single(verify.Gelenler);
            Assert.Equal(345.67m, verify.Islemler.Single().TutarTl);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + suffix);
        }
    }

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        connection.Open();
        return connection;
    }

    private static KasaDbContext Context(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).Options);

    private static void LegacyBase(SqliteConnection connection) => Execute(connection, """
        CREATE TABLE Kanallar (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Ad TEXT NOT NULL,
            Aktif INTEGER NOT NULL, Sira INTEGER NOT NULL, AcilisDevri TEXT NOT NULL);
        CREATE TABLE Cariler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Ad TEXT NOT NULL, Aktif INTEGER NOT NULL);
        CREATE TABLE Ayarlar (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, TakipBaslangic TEXT NOT NULL,
            KasaAcilisDevri TEXT NOT NULL, IzleyiciSifreHash TEXT NULL);
        CREATE TABLE Islemler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Tarih TEXT NOT NULL, Cari TEXT NOT NULL,
            TutarTl TEXT NOT NULL, Kanal TEXT NOT NULL, Tip INTEGER NOT NULL, "Not" TEXT NULL);
        CREATE TABLE Gelenler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, DonemStart TEXT NOT NULL,
            Kanal TEXT NOT NULL, TutarTl TEXT NOT NULL);
        INSERT INTO Kanallar VALUES (7, 'MEZAT', 1, 0, '1234.56');
        INSERT INTO Cariler VALUES (4, 'Firma', 1);
        INSERT INTO Ayarlar VALUES (1, '2026-09-01', '10000.0', 'saklanan-hash');
        INSERT INTO Islemler VALUES (11, '2026-09-01', 'Firma', '345.67', 'MEZAT', 0, 'Özgün açıklama');
        INSERT INTO Gelenler VALUES (12, '2026-09-01', 'MEZAT', '2000.25');
        """);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
