using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

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

        KasaVeritabaniBaslatici.Baslat(db);

        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
        Assert.Equal(42.75m, Assert.Single(db.Gelenler).TutarTl);
        Assert.Equal(125.50m, Assert.Single(db.Kanallar).AcilisDevri);
        Assert.Empty(db.Alislar);
        Assert.Empty(db.Alicilar);
        Assert.Empty(db.AlisOdemeler);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    /// <summary>
    /// Çekirdek kayıt sürümleri (contract-6) yalnız sütun ekler: gider, gelir, kanal ve ayar satırlarının bütün değerleri aynen kalır,
    /// sürümleri 0'dır. Gelenler tetikleyicileri (eski yinelenen grup, kilitli ay) sütun eklemeden etkilenmez: tanımları birebir aynı
    /// kalır ve çalışmaya devam eder (kilitli ayın geliri sürüm artırılarak da değiştirilemez). Raporlar (haftalık, aylık, panel) göç
    /// öncesi ve sonrası birebir aynıdır.
    /// </summary>
    [Fact]
    public void Cekirdek_surumleri_mevcut_satirlari_ve_gelir_tetikleyicilerini_degistirmez_raporlar_ayni()
    {
        using var connection = Open();
        var saat = new ServiceCollection()
            .AddSingleton<TimeProvider>(new SabitSaat(new DateOnly(2026, 9, 25))).BuildServiceProvider();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).UseApplicationServiceProvider(saat).Options);
        db.GetService<IMigrator>().Migrate(Kasa.Api.Migrations.KasaKontrolFiligrani.Kimlik);
        Execute(connection, """
            INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1, 'MEZAT', 1, 0, '100.0'), (2, 'TOPTAN', 0, 1, '-25.5');
            INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri, IzleyiciSifreHash) VALUES ('2026-06-01', '1000.0', 'saklanan-hash');
            INSERT INTO Gelenler (DonemStart, Kanal, KanalId, TutarTl) VALUES ('2026-06-01', 'MEZAT', 1, '48000.00'), ('2026-08-03', 'toptan', 2, '2500.5');
            INSERT INTO Islemler (Tarih, Cari, TutarTl, Kanal, KanalId, Tip, "Not") VALUES ('2026-06-15', 'Kira', '100.01', 'MEZAT', 1, 1, NULL),
                ('2026-08-20', 'Ortak SGK', '333.35', 'Ortak', NULL, 0, 'Ağustos');
            UPDATE AyKilidi SET KilitliSonTarih = '2026-06-30', Surum = Surum + 1 WHERE Id = 1;
            """);
        string[] tablolar = ["Islemler", "Gelenler", "Kanallar", "Ayarlar"];
        const string tetikleyiciler = "SELECT group_concat(name || ':' || sql, char(10)) FROM (SELECT name, sql FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'Gelenler' ORDER BY name);";
        var kaynak = Dokum(connection, tablolar);
        var tanimlar = Scalar(connection, tetikleyiciler);
        string once;
        using (var eski = SurumOncesiBaglam.Ayni(db))
            once = RaporOzeti(eski, (2026, 6), (2026, 8), (2026, 9));

        KasaVeritabaniBaslatici.Baslat(db);

        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
        Assert.Contains(Kasa.Api.Migrations.CekirdekSurumleri.Kimlik, db.Database.GetAppliedMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(kaynak, Dokum(connection, tablolar, "Surum"));
        foreach (var tablo in tablolar)
            Assert.Equal(0L, Scalar(connection, $"SELECT COUNT(*) FROM \"{tablo}\" WHERE Surum <> 0;"));
        Assert.Equal(once, RaporOzeti(db, (2026, 6), (2026, 8), (2026, 9)));

        // Gelenler tetikleyicileri: altısı da tanımıyla yerinde ve çalışıyor.
        Assert.Equal(6L, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'Gelenler';"));
        Assert.Equal(tanimlar, Scalar(connection, tetikleyiciler));
        foreach (var sql in new[]
                 {
                     "UPDATE Gelenler SET TutarTl = '1' WHERE DonemStart = '2026-06-01';",
                     "UPDATE Gelenler SET Surum = Surum + 1 WHERE DonemStart = '2026-06-01';",
                     "INSERT INTO Gelenler (DonemStart, Kanal, KanalId, TutarTl) VALUES ('2026-06-08', 'TOPTAN', 2, '5');",
                 })
            Assert.Contains("Kilitli ay", Assert.Throws<SqliteException>(() => Execute(connection, sql)).Message);
        Assert.Contains("Eski yinelenen gelir grubu", Assert.Throws<SqliteException>(() => Execute(connection,
            "INSERT INTO Gelenler (DonemStart, Kanal, KanalId, TutarTl, EskiYinelenenGrup) VALUES ('2026-09-07', 'MEZAT', 1, '5', 1);")).Message);
        Execute(connection, "UPDATE Gelenler SET TutarTl = '2600', Surum = Surum + 1 WHERE DonemStart = '2026-08-03';");
        var agustos = db.Gelenler.AsNoTracking().Single(g => g.DonemStart == new DateOnly(2026, 8, 3));
        Assert.Equal((2600m, 1), (agustos.TutarTl, agustos.Surum));
    }

    /// <summary>Kasa kontrolü filigranı (gap-denetim-izi-gozlemlenebilirlik-3) yalnız sütun ekler: mevcut kontrol satırının bütün
    /// değerleri aynen kalır; sürümü 1, filigran ve açıklama sütunları boştur (istemciler "eski kayıt, filigran yok" gösterir).</summary>
    [Fact]
    public void Kasa_kontrolu_filigrani_mevcut_satiri_degistirmez_filigran_bos_kalir()
    {
        using var connection = Open();
        using var db = Context(connection);
        db.GetService<IMigrator>().Migrate("20261003000100_EkstreEslesmesi");
        Execute(connection, """INSERT INTO KasaKontrolleri (Id, Kaydedildi, SistemBakiye, GercekBakiye, Fark, "Not") VALUES (3, 1790000000000, '1250.50', '1200', '-50.50', 'Akşam sayımı');""");
        string[] yeniSutunlar = ["Surum", "HesapTarihi", "KanalBakiyeleriJson", "SonIslemId", "SonFinansIstekId", "SonDenetimOlayId", "FarkAciklamasi", "FarkAciklamaZamani"];
        var kaynak = Dokum(connection, ["KasaKontrolleri"]);

        KasaVeritabaniBaslatici.Baslat(db);

        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(kaynak, Dokum(connection, ["KasaKontrolleri"], yeniSutunlar));
        var row = db.KasaKontrolleri.AsNoTracking().Single();
        Assert.Equal((1250.50m, 1200m, -50.50m, "Akşam sayımı", 1), (row.SistemBakiye, row.GercekBakiye, row.Fark, row.Not, row.Surum));
        Assert.True(row is { HesapTarihi: null, KanalBakiyeleriJson: null, SonIslemId: null, SonFinansIstekId: null, SonDenetimOlayId: null, FarkAciklamasi: null, FarkAciklamaZamani: null });
    }

    /// <summary>Editör şifresi sıfırlama izi (gap-geri-yukleme-durum-geri-sarma-10) yalnız sütun ekler: sistem durumu satırı ve editör
    /// güvenlik kaydı aynen kalır (oturum dönemi ve şifre özeti değişmez: yayın kimseyi düşürmez), iz boştur.</summary>
    [Fact]
    public void Editor_sifirlama_izi_sistem_durumunu_ve_editor_kaydini_degistirmez_iz_bos_kalir()
    {
        using var connection = Open();
        using var db = Context(connection);
        db.GetService<IMigrator>().Migrate(Kasa.Api.Migrations.GeriYuklemeGuvenligi.Kimlik);
        Execute(connection, """
            UPDATE SistemDurumu SET OturumDonemi = 'donem-1', SonGeriYukleme = '2026-09-20T10:00:00+00:00', GeriYuklemeRaporu = '["madde"]' WHERE Id = 1;
            INSERT INTO EditorGuvenlik (Id, SifreHash, KurtarmaHash, Surum) VALUES (1, 'tuz.ozet', 'KOD', 3);
            """);
        var kaynak = Dokum(connection, ["SistemDurumu", "EditorGuvenlik"]);

        KasaVeritabaniBaslatici.Baslat(db);

        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(kaynak, Dokum(connection, ["SistemDurumu", "EditorGuvenlik"], "EditorSifirlamaIzi"));
        Assert.Null(db.SistemDurumu.AsNoTracking().Single().EditorSifirlamaIzi);
    }

    [Fact]
    public void Bos_veritabani_migration_ile_kurulur_ve_model_snapshot_eslesir()
    {
        using var connection = Open();
        using var db = Context(connection);

        KasaVeritabaniBaslatici.Baslat(db);
        KasaVeritabaniBaslatici.Baslat(db);

        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
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

        KasaVeritabaniBaslatici.Baslat(db);

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
        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
        Assert.Equal(1L, Scalar(connection, "PRAGMA foreign_keys;"));

        // Tekrar başlatma ne veri ne yeni migration kaydı üretir.
        KasaVeritabaniBaslatici.Baslat(db);
        Assert.Single(db.Islemler);
        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
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
        using (var eski = SurumOncesiBaglam.Ayni(db)) // eski şema: çekirdek sürüm sütunları yok
        {
            eski.AddRange(kanal, kart);
            eski.SaveChanges();
            eski.Islemler.Add(new IslemEntity { Tarih = new(2026, 9, 1), Cari = "Firma", Kanal = "MEZAT", KanalId = kanal.Id, KrediKartiId = kart.Id, TutarTl = 100.01m, Tip = GiderTipi.KrediKarti });
            eski.KartOdemeler.Add(new KartOdemeEntity { KrediKartiId = kart.Id, Tarih = new(2026, 9, 11), Tutar = 50.02m });
            eski.SaveChanges();
        }
        Execute(connection, $"INSERT INTO Gelenler VALUES (1, '2026-09-01', 'MEZAT', '250.03', {kanal.Id});");
        db.ChangeTracker.Clear();

        KasaVeritabaniBaslatici.Baslat(db);

        Assert.Equal(kart.Id, Assert.Single(db.Islemler).KrediKartiId);
        Assert.Equal(kanal.Id, Assert.Single(db.Islemler).KanalId);
        Assert.Equal(50.02m, Assert.Single(db.KartOdemeler).Tutar);
        Assert.Equal(250.03m, Assert.Single(db.Gelenler).TutarTl);
        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());

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

        KasaVeritabaniBaslatici.Baslat(db);

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

        KasaVeritabaniBaslatici.Baslat(db);

        var recovered = db.Kanallar.Single(k => k.Ad == "ESKI");
        Assert.False(recovered.Aktif);
        Assert.Equal(recovered.Id, db.Islemler.Single(i => i.Id == 12).KanalId);
        Assert.Equal(recovered.Id, db.Gelenler.Single(g => g.Id == 13).KanalId);
        Assert.Null(db.Islemler.Single(i => i.Id == 13).KanalId);
        Assert.Null(db.Gelenler.Single(g => g.Id == 14).KanalId);
        Assert.Equal(KanalEtiketleri.Ortak, db.Islemler.Single(i => i.Id == 13).ToCore().Kanal);
        Assert.Equal(KrediTuretici.KrediKanal, db.Gelenler.Single(g => g.Id == 14).ToCore().Kanal);
        Assert.DoesNotContain(db.Kanallar, k => k.Ad == KanalEtiketleri.Ortak || k.Ad == KrediTuretici.KrediKanal);
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

        KasaVeritabaniBaslatici.Baslat(db);
        KasaVeritabaniBaslatici.Baslat(db);

        Assert.Equal(2L, Scalar(connection, "SELECT COUNT(*) FROM Gelenler;"));
        Assert.Equal("2000.25", Scalar(connection, "SELECT TutarTl FROM Gelenler WHERE Id = 12;"));
        Assert.Equal("999.99", Scalar(connection, "SELECT TutarTl FROM Gelenler WHERE Id = 14;"));
        Assert.Equal(kanal, Scalar(connection, "SELECT Kanal FROM Gelenler WHERE Id = 14;"));
        Assert.Equal("2026-09-01", Scalar(connection, "SELECT DonemStart FROM Gelenler WHERE Id = 14;"));
        Assert.All(db.Gelenler, g => { Assert.True(g.EskiYinelenenGrup); Assert.Equal(7, g.KanalId); });
        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
        Assert.Equal(1L, Scalar(connection, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public void Yinelenen_kanal_adlari_ve_tanimlanmamis_ek_sutunlar_sessizce_silinmez()
    {
        using var connection = Open();
        LegacyBase(connection);
        Execute(connection, "INSERT INTO Kanallar VALUES (8, 'mezat', 1, 1, '900.0');");
        using var db = Context(connection);
        var duplicate = Assert.Throws<InvalidOperationException>(() => KasaVeritabaniBaslatici.Baslat(db));
        Assert.Contains("birden fazla kanal", duplicate.Message);
        Assert.Equal(2L, Scalar(connection, "SELECT COUNT(*) FROM Kanallar;"));

        Execute(connection, "DELETE FROM Kanallar WHERE Id = 8; ALTER TABLE Kanallar ADD COLUMN OzelNot TEXT NULL; UPDATE Kanallar SET OzelNot = 'korunmali';");
        var custom = Assert.Throws<InvalidOperationException>(() => KasaVeritabaniBaslatici.Baslat(db));
        Assert.Contains("tanınmayan alanlar", custom.Message);
        Assert.Equal("korunmali", Scalar(connection, "SELECT OzelNot FROM Kanallar;"));
    }

    [Fact]
    public void Yeni_schema_yinelenen_kanal_gelir_ve_gecersiz_iliskiyi_veritabaninda_reddeder()
    {
        using var connection = Open();
        using var db = Context(connection);
        KasaVeritabaniBaslatici.Baslat(db);
        Execute(connection, "INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1, 'MEZAT', 1, 0, '0.0');");

        Assert.Throws<SqliteException>(() => Execute(connection, "INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (2, 'mezat', 1, 0, '0.0');"));
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
        var broken = Assert.Throws<InvalidOperationException>(() => KasaVeritabaniBaslatici.Baslat(db));
        Assert.Contains("ilişkisi geçersiz", broken.Message);
        Assert.Equal(999L, Scalar(connection, "SELECT KrediKartiId FROM Islemler;"));

        Execute(connection, "UPDATE Islemler SET KrediKartiId = NULL; INSERT INTO Kanallar VALUES (80, 'gecici', 0, 1, '0.0'); DELETE FROM Kanallar WHERE Id = 80;");
        KasaVeritabaniBaslatici.Baslat(db);
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

        KasaVeritabaniBaslatici.Baslat(db);

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

        var error = Assert.Throws<InvalidOperationException>(() => KasaVeritabaniBaslatici.Baslat(db));

        Assert.Contains("özel muhasebe etiketi", error.Message);
        Assert.Equal(2L, Scalar(connection, "SELECT COUNT(*) FROM Kanallar;"));
        Assert.Equal("42.0", Scalar(connection, "SELECT AcilisDevri FROM Kanallar WHERE Id = 8;"));
    }

    [Fact]
    public async Task Eszamanli_iki_baslangic_legacy_verisini_ve_migration_gecmisini_cogaltmaz()
    {
        var path = Path.Combine(Path.GetTempPath(), "kasa-migration-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        // Dosya veritabanında bekleyen köprü/migration göç öncesi yedeksiz çalışmaz.
        var backupDirectory = path + "-yedek";
        var backup = GocOncesiYedekTests.TestYedegi(backupDirectory);
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
                KasaVeritabaniBaslatici.Baslat(db, backup);
            })).ToArray();
            start.Set();
            await Task.WhenAll(initializers);

            using var verified = new SqliteConnection(connectionString);
            using var verify = Context(verified);
            Assert.Equal(24, verify.Database.GetAppliedMigrations().Count());
            Assert.Single(verify.Kanallar);
            Assert.Single(verify.Islemler);
            Assert.Single(verify.Gelenler);
            Assert.Equal(345.67m, verify.Islemler.Single().TutarTl);
            Assert.NotEmpty(Directory.GetFiles(backupDirectory, "kasa-goc-oncesi-*.zip"));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                File.Delete(path + suffix);
            if (Directory.Exists(backupDirectory))
                Directory.Delete(backupDirectory, true);
        }
    }

    /// <summary>Denetim olayları migration'ı yalnız ekler: önceki sürümün verisi ve raporları (haftalık, aylık, panel)
    /// göç öncesi ve sonrası birebir aynıdır; tablo, indeksler ve değiştirilemezlik tetikleyicileri kurulur.</summary>
    [Fact]
    public void Denetim_olaylari_migrationi_yalniz_ekler_ve_gecmis_raporlari_degistirmez()
    {
        using var connection = Open();
        var saat = new ServiceCollection()
            .AddSingleton<TimeProvider>(new SabitSaat(new DateOnly(2026, 9, 25))).BuildServiceProvider();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).UseApplicationServiceProvider(saat).Options);
        db.GetService<IMigrator>().Migrate("20260929000300_AyRaporAnlikGoruntuleri");
        Execute(connection, """
            INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1, 'MEZAT', 1, 0, '100.0'), (2, 'TOPTAN', 1, 1, '0');
            INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri, IzleyiciSifreHash) VALUES ('2026-06-01', '1000.0', NULL);
            INSERT INTO Gelenler (DonemStart, Kanal, KanalId, TutarTl) VALUES ('2026-07-06', 'MEZAT', 1, '48000.00'), ('2026-08-03', 'TOPTAN', 2, '2500.5');
            INSERT INTO Islemler (Tarih, Cari, TutarTl, Kanal, KanalId, Tip, "Not") VALUES ('2026-07-15', 'Tedarik', '12500', 'MEZAT', 1, 0, NULL),
                ('2026-08-20', 'Ortak kira', '3000', 'ortak', NULL, 1, 'Kira');
            """);
        // Göç öncesi bağlamda da kayıt çalışır (olay tablosu yok: olay yazılmaz). Önceki sürümün şemasında çekirdek sürüm sütunları
        // yoktur: göç öncesi kayıt ve rapor o şemanın bağlamıyla (SurumOncesiBaglam).
        using var eski = SurumOncesiBaglam.Ayni(db);
        eski.Islemler.Add(new IslemEntity { Tarih = new(2026, 9, 1), Cari = "Göç öncesi", TutarTl = 10m, Kanal = "TOPTAN", KanalId = 2, Tip = GiderTipi.Cari });
        eski.SaveChanges();
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        string Raporlar(KasaDbContext baglam) => System.Text.Json.JsonSerializer.Serialize(new object[]
        {
            new Kasa.Api.Servisler.HesapServisi(baglam).Haftalik(), new Kasa.Api.Servisler.HesapServisi(baglam).Aylik(2026, 7),
            new Kasa.Api.Servisler.HesapServisi(baglam).Aylik(2026, 8), new Kasa.Api.Servisler.HesapServisi(baglam).Panel(),
        }, web);
        var once = Raporlar(eski);

        KasaVeritabaniBaslatici.Baslat(db);

        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(once, Raporlar(db));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM DenetimOlaylari;"));
        foreach (var nesne in new[] { "IX_DenetimOlaylari_Varlik_VarlikId", "IX_DenetimOlaylari_ZamanUtc", "IX_DenetimOlaylari_KilitAcmaOlayiId",
                     "TR_DenetimOlaylari_Degistirilemez", "TR_DenetimOlaylari_Silinemez" })
            Assert.Equal(1L, Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE name = '{nesne}';"));
        // Göç sonrası ilk kayıt olay üretir; aynı bağlam tabloyu artık görür.
        db.Islemler.Add(new IslemEntity { Tarih = new(2026, 9, 2), Cari = "Göç sonrası", TutarTl = 20m, Kanal = "TOPTAN", KanalId = 2, Tip = GiderTipi.Cari });
        db.SaveChanges();
        Assert.Equal("Ekle|Islem|sistem", Scalar(connection, "SELECT Tur || '|' || Varlik || '|' || AktorRol FROM DenetimOlaylari;"));
    }

    /// <summary>
    /// Göç öncesi saklanan gerekçeler ve önceki durumlar (aylık gider ve ekstre satırı iptal açıklamaları, alış ödemesi
    /// düzeltme/iptalinin FinansIstekler.OncekiJson'u) 'sistem' aktörlü 'GecmisKayit' olaylarına aktarılır; kaynak tablolar
    /// ve raporlar (haftalık, aylık, panel) göç öncesi ve sonrası birebir aynıdır. Kaydı zaten denetim izinde olan iptal ya
    /// da istek (olay tablosu kurulduktan sonra yazılan) yeniden aktarılmaz.
    /// </summary>
    [Fact]
    public void Denetim_gecmis_aktarimi_eski_gerekceleri_ve_onceki_durumu_olaya_tasir_raporlari_degistirmez()
    {
        using var connection = Open();
        var saat = new ServiceCollection()
            .AddSingleton<TimeProvider>(new SabitSaat(new DateOnly(2026, 4, 20))).BuildServiceProvider();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).UseApplicationServiceProvider(saat).Options);
        db.GetService<IMigrator>().Migrate("20260930000100_DenetimOlaylari");
        Execute(connection, """
            INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1, 'MEZAT', 1, 0, '100.0'), (2, 'TOPTAN', 1, 1, '0');
            INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri, IzleyiciSifreHash) VALUES ('2026-01-01', '1000.0', NULL);
            INSERT INTO Gelenler (DonemStart, Kanal, KanalId, TutarTl) VALUES ('2026-03-02', 'MEZAT', 1, '48000.00'), ('2026-04-06', 'TOPTAN', 2, '2500.5');
            INSERT INTO Islemler (Id, Tarih, Cari, TutarTl, Kanal, KanalId, Tip, "Not") VALUES (10, '2026-04-05', 'Kira', '15000', 'MEZAT', 1, 0, 'Nisan kirası');
            INSERT INTO AylikGiderSablonlar (Id, Surum) VALUES (1, 1);
            INSERT INTO AylikGiderRevizyonlar (Id, SablonId, Surum, GecerliAy, Ad, Tur, Tutar, OdemeGunu, DagilimTuru, DagilimJson, Aktif)
                VALUES (1, 1, 1, '2026-01-01', 'Kira', 'Kira', '15000', 5, 'Genel', '[]', 1);
            INSERT INTO AylikGiderOdemeler (Id, SablonId, RevizyonId, Ay, Tarih, Tutar, IslemId, Iptal, IptalAciklamasi) VALUES
                (5, 1, 1, '2026-03-01', '2026-03-05', '15000.0', NULL, 1, 'Mart kirası yanlış aya girildi'),
                (6, 1, 1, '2026-04-01', '2026-04-05', '15000.0', 10, 0, NULL);
            INSERT INTO EkstreBelgeler (Id, Surum, Kaynak, Banka, HesapAdi, KartId, DosyaAdi, DosyaOzeti, Dosya, Yuklendi, SatirlarJson, UyarilarJson)
                VALUES (1, 2, 'Kart', 'Banka', 'İş kartı', NULL, 'ekstre.pdf', '38523C087796E5D5DD1CF9BAD1FB026781A838DD9DD2CF8AF58B9F6502A46778', X'255044462D', 1775000000000, '[]', '[]');
            INSERT INTO EkstreKayitlar (Id, BelgeId, SatirNo, Tarih, Aciklama, Tutar, IslemTuru, DagilimTuru, DagilimJson, KrediKartiId, IslemId, KartHarcamaId, KartOdemeId, Iptal, IptalAciklamasi) VALUES
                (7, 1, 3, '2026-04-02', 'MARKET ALIŞVERİŞİ', '245.9', 'Gider', 'Genel', '[]', NULL, NULL, NULL, NULL, 1, 'Mükerrer satır'),
                (8, 1, 4, '2026-04-03', 'AKARYAKIT', '1200', 'Gider', 'Genel', '[]', NULL, NULL, NULL, NULL, 0, NULL);
            INSERT INTO FinansIstekler (IstekId, Ozet, Tur, SonucId, OncekiJson) VALUES
                ('11111111-2222-3333-4444-555555555555', 'ozet-1', 'OdemeIptal', 3, '{"aciklama":"Yanlış tedarikçiye girildi","alis":{"Id":3,"Tedarikci":"Toptancı","Odemeler":[{"Id":9,"Tutar":2500.5}]}}'),
                ('66666666-7777-8888-9999-AAAAAAAAAAAA', 'ozet-2', 'AlisOdeme', 3, NULL);
            """);
        // Olay tablosu kurulduktan sonra yazılan iptal ve istek kendi olaylarıyla zaten izdedir. Önceki sürümün şemasında çekirdek sürüm
        // sütunları yoktur: göç öncesi kayıt ve rapor o şemanın bağlamıyla (SurumOncesiBaglam).
        using var eski = SurumOncesiBaglam.Ayni(db);
        eski.AylikGiderOdemeler.Add(new AylikGiderOdemeEntity { SablonId = 1, RevizyonId = 1, Ay = new(2026, 2, 1), Tarih = new(2026, 2, 5), Tutar = 15000m, Iptal = true, IptalAciklamasi = "Şubat mükerrer" });
        eski.SaveChanges();
        var sonrakiIstek = Guid.NewGuid();
        eski.FinansIstekler.Add(new FinansIstekEntity { IstekId = sonrakiIstek, Ozet = "ozet-3", Tur = "OdemeIptal", SonucId = 4, OncekiJson = """{"aciklama":"Sürüm sonrası","alis":{"Id":4}}""" });
        eski.Islemler.Add(new IslemEntity { Tarih = new(2026, 4, 10), Cari = "Sürüm sonrası", TutarTl = 20m, Kanal = "TOPTAN", KanalId = 2, Tip = GiderTipi.Cari });
        eski.SaveChanges();
        Assert.Equal(2L, Scalar(connection, "SELECT COUNT(*) FROM DenetimOlaylari;"));
        string[] tablolar = ["AylikGiderOdemeler", "EkstreKayitlar", "EkstreBelgeler", "FinansIstekler", "Islemler", "Gelenler"];
        // Ekstre PDF'inin içeriği (Dosya) aynı açılışta belge deposuna taşınır; eşleşme sütunları (EkstreEslesmesi) eklenir ve mevcut satırlarda
        // boş kalır. Kalan bütün sütunlar birebir aynı kalmalı.
        var kaynak = Dokum(connection, tablolar, "Dosya", "EslesmeTuru", "EslesmeId");
        var once = RaporOzeti(eski, (2026, 3), (2026, 4));
        var baslangic = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        KasaVeritabaniBaslatici.Baslat(db);

        var bitis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
        Assert.Contains("20260930000200_DenetimGecmisAktarimi", db.Database.GetAppliedMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(once, RaporOzeti(db, (2026, 3), (2026, 4)));
        Assert.Equal(kaynak, Dokum(connection, tablolar, "Dosya", "EslesmeTuru", "EslesmeId", "Islemler.Surum", "Gelenler.Surum"));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM EkstreKayitlar WHERE EslesmeTuru IS NOT NULL OR EslesmeId IS NOT NULL;"));

        var aktarilan = db.DenetimOlaylari.AsNoTracking().Where(o => o.Tur == "GecmisKayit").OrderBy(o => o.Id).ToList();
        Assert.Equal([("AylikGiderOdeme", "5"), ("EkstreKayit", "7"), ("Alis", "3")], aktarilan.Select(o => (o.Varlik, o.VarlikId!)));
        Assert.All(aktarilan, o =>
        {
            Assert.Equal("sistem", o.AktorRol);
            Assert.Null(o.AktorId);
            Assert.Null(o.IstemciIp);
            Assert.Null(o.TraceId);
            Assert.Null(o.KilitAcmaOlayiId);
            // Zaman aktarım anıdır: değişikliğin kendi zamanı eski sürümde tutulmuyordu.
            Assert.InRange(o.ZamanUtc, baslangic - 1000, bitis + 1000);
        });

        var aylik = aktarilan[0];
        Assert.Equal("Mart kirası yanlış aya girildi", aylik.Gerekce);
        Assert.Null(aylik.OncekiJson);
        var aylikDurum = JsonNode.Parse(aylik.YeniJson!)!;
        Assert.Equal(("2026-03-01", "2026-03-05", 15000m, true), ((string?)aylikDurum["Ay"], (string?)aylikDurum["Tarih"], aylikDurum["Tutar"]!.GetValue<decimal>(), aylikDurum["Iptal"]!.GetValue<bool>()));
        Assert.Equal((1, 1, "Mart kirası yanlış aya girildi"), (aylikDurum["SablonId"]!.GetValue<int>(), aylikDurum["RevizyonId"]!.GetValue<int>(), (string?)aylikDurum["IptalAciklamasi"]));
        Assert.Null(aylikDurum["IslemId"]);

        var ekstre = aktarilan[1];
        Assert.Equal("Mükerrer satır", ekstre.Gerekce);
        var ekstreDurum = JsonNode.Parse(ekstre.YeniJson!)!;
        Assert.Equal(("MARKET ALIŞVERİŞİ", 245.9m, 1, 3, "2026-04-02"), ((string?)ekstreDurum["Aciklama"], ekstreDurum["Tutar"]!.GetValue<decimal>(),
            ekstreDurum["BelgeId"]!.GetValue<int>(), ekstreDurum["SatirNo"]!.GetValue<int>(), (string?)ekstreDurum["Tarih"]));
        Assert.True(ekstreDurum["Iptal"]!.GetValue<bool>());

        var alis = aktarilan[2];
        Assert.Equal(("Yanlış tedarikçiye girildi", Guid.Parse("11111111-2222-3333-4444-555555555555")), (alis.Gerekce, alis.IstekId));
        var alisOnceki = JsonNode.Parse(alis.OncekiJson!)!;
        Assert.Equal(("Toptancı", 2500.5m), ((string?)alisOnceki["Tedarikci"], alisOnceki["Odemeler"]![0]!["Tutar"]!.GetValue<decimal>()));
        Assert.Equal("OdemeIptal", (string?)JsonNode.Parse(alis.YeniJson!)!["IstekTuru"]);

        // Olay tablosundan sonra yazılanlar yeniden aktarılmadı (iki eski olay + üç aktarım); yeniden başlatma aktarmaz.
        Assert.Equal(5L, Scalar(connection, "SELECT COUNT(*) FROM DenetimOlaylari;"));
        Assert.Equal(1, db.DenetimOlaylari.Count(o => o.IstekId == sonrakiIstek));
        KasaVeritabaniBaslatici.Baslat(db);
        Assert.Equal(5L, Scalar(connection, "SELECT COUNT(*) FROM DenetimOlaylari;"));
    }

    /// <summary>
    /// Ay kanal kümesi migration'ı (core-1, ops-2): şema kurulur ve migration'ın uygulandığı açılışta takip başlangıcından geçen aya
    /// kadar her tamamlanmış ay bugünkü kanallarla, raporun sırasıyla (Sira; eşitlerde veritabanı sırası) dondurulur. Eşit sıralı
    /// kanallar, pasif kanal, kilitli ay ve üç geçmiş ayda kuruşlu Ortak giderlerle: raporlar (haftalık, aylık, panel) göç öncesi ve
    /// sonrası birebir aynıdır. Göçten sonra aktif kanal eklemek ve pasife almak geçmiş ayların raporunu değiştirmez, içinde
    /// bulunulan ay güncel kanallarla bölünür; yeniden başlatma küme yazmaz; küme değiştirilemez, kümedeki kanal silinemez.
    /// </summary>
    [Fact]
    public void Ay_kanal_kumeleri_migrationi_tamamlanmis_aylari_bugunku_kanallarla_dondurur_raporlar_birebir_ayni()
    {
        using var connection = Open();
        var saat = new ServiceCollection()
            .AddSingleton<TimeProvider>(new SabitSaat(new DateOnly(2026, 9, 25))).BuildServiceProvider();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).UseApplicationServiceProvider(saat).Options);
        db.GetService<IMigrator>().Migrate("20260930000200_DenetimGecmisAktarimi");
        Execute(connection, """
            INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1, 'MEZAT', 1, 0, '100.0'), (2, 'TOPTAN', 1, 1, '0'),
                (3, 'ESKI', 0, 0, '0'), (4, 'PERAKENDE', 1, 1, '0'), (5, 'ONLINE', 1, 2, '0');
            INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri, IzleyiciSifreHash) VALUES ('2026-06-01', '1000.0', NULL);
            INSERT INTO Gelenler (DonemStart, Kanal, KanalId, TutarTl) VALUES ('2026-06-01', 'MEZAT', 1, '48000.00'), ('2026-08-03', 'ESKI', 3, '2500.5');
            INSERT INTO Islemler (Tarih, Cari, TutarTl, Kanal, KanalId, Tip, "Not") VALUES ('2026-06-15', 'Ortak kira', '100.01', 'Ortak', NULL, 1, NULL),
                ('2026-07-15', 'Ortak kuruş', '0.03', 'Ortak', NULL, 0, NULL), ('2026-08-20', 'Ortak SGK', '333.35', 'Ortak', NULL, 1, NULL),
                ('2026-09-10', 'Bu ayın ortak gideri', '10.01', 'Ortak', NULL, 0, NULL), ('2026-07-05', 'Tedarik', '1200', 'TOPTAN', 2, 0, NULL);
            UPDATE AyKilidi SET KilitliSonTarih = '2026-06-30', Surum = Surum + 1 WHERE Id = 1;
            """);
        (int, int)[] gecmis = [(2026, 6), (2026, 7), (2026, 8)];
        string once, onceGecmis;
        using (var eski = SurumOncesiBaglam.Ayni(db)) // önceki sürümün şeması: çekirdek sürüm sütunları yok
        {
            once = RaporOzeti(eski, [.. gecmis, (2026, 9)]);
            onceGecmis = AylikOzeti(eski, gecmis);
        }

        KasaVeritabaniBaslatici.Baslat(db);

        Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
        Assert.Contains(AyKanalKumesi.MigrationId, db.Database.GetAppliedMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(once, RaporOzeti(db, [.. gecmis, (2026, 9)]));
        var kumeler = db.AyKanalKumeleri.AsNoTracking().OrderBy(k => k.Yil).ThenBy(k => k.Ay).ToList();
        Assert.Equal(new[] { (2026, 6, AyKanalKumesi.Gecis), (2026, 7, AyKanalKumesi.Gecis), (2026, 8, AyKanalKumesi.Gecis) }, kumeler.Select(k => (k.Yil, k.Ay, k.Kaynak)).ToArray());
        var agustos = kumeler[^1].Id;
        Assert.Equal(new[] { (1, 0, true), (3, 1, false), (2, 2, true), (4, 3, true), (5, 4, true) },
            db.AyKanalKumesiKanallari.AsNoTracking().Where(u => u.KumeId == agustos).OrderBy(u => u.Sira).ToList().Select(u => (u.KanalId, u.Sira, u.Aktif)).ToArray());
        // Dondurma türetilmiş veridir: denetim olayı üretmez.
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM DenetimOlaylari;"));
        foreach (var nesne in new[] { "IX_AyKanalKumeleri_Yil_Ay", "IX_AyKanalKumesiKanallari_KumeId_KanalId", "IX_AyKanalKumesiKanallari_KanalId",
                     "TR_AyKanalKumeleri_Guncelleme", "TR_AyKanalKumeleri_Silme", "TR_AyKanalKumesiKanallari_Guncelleme", "TR_AyKanalKumesiKanallari_Silme" })
            Assert.Equal(1L, Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE name = '{nesne}';"));

        // Göçten sonra aktif kanal eklenir ve bir kanal pasife alınır: geçmiş aylar kendi kümeleriyle aynı kalır, bu ay yeni kanallarla.
        db.Kanallar.Add(new KanalEntity { Ad = "YENI", Sira = 3 });
        db.SaveChanges();
        db.Kanallar.Single(k => k.Id == 4).Aktif = false;
        db.SaveChanges();
        Assert.Equal(onceGecmis, AylikOzeti(db, gecmis));
        var eylul = new Kasa.Api.Servisler.HesapServisi(db).Aylik(2026, 9);
        Assert.Equal(new[] { ("MEZAT", 2.51m), ("ESKI", 0m), ("TOPTAN", 2.50m), ("PERAKENDE", 0m), ("ONLINE", 2.50m), ("YENI", 2.50m) },
            eylul.Kanallar.Select(k => (k.Kanal, k.OrtakPay)).ToArray());

        // Yeniden başlatma küme yazmaz; küme ve üyeleri değiştirilemez, kümedeki kanal silinemez.
        KasaVeritabaniBaslatici.Baslat(db);
        Assert.Equal(3, db.AyKanalKumeleri.Count());
        foreach (var sql in new[] { "UPDATE AyKanalKumeleri SET Kaynak = 'sahte';", "DELETE FROM AyKanalKumesiKanallari;", "DELETE FROM Kanallar WHERE Id = 5;" })
            Assert.Equal(19, Assert.Throws<SqliteException>(() => Execute(connection, sql)).SqliteErrorCode);
    }

    [Fact]
    public void Denetim_olayi_veritabaninda_ve_ef_yolunda_degistirilemez_ve_silinemez()
    {
        using var connection = Open();
        using var db = Context(connection);
        KasaVeritabaniBaslatici.Baslat(db);
        db.Kanallar.Add(new KanalEntity { Ad = "MEZAT" });
        db.SaveChanges();
        Assert.Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM DenetimOlaylari;"));

        var guncelleme = Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE DenetimOlaylari SET Gerekce = 'sahte';"));
        Assert.Equal(19, guncelleme.SqliteErrorCode);
        Assert.Contains("Denetim kaydi degistirilemez.", guncelleme.Message);
        var silme = Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM DenetimOlaylari;"));
        Assert.Equal(19, silme.SqliteErrorCode);
        Assert.Contains("Denetim kaydi silinemez.", silme.Message);

        var olay = db.DenetimOlaylari.Single();
        olay.Gerekce = "sahte";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        db.DenetimOlaylari.Remove(db.DenetimOlaylari.Single());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        Assert.Null(db.DenetimOlaylari.AsNoTracking().Single().Gerekce);
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

    /// <summary>Haftalık, panel ve verilen ayların aylık raporu (bağlamın sabit saatiyle), JSON olarak.</summary>
    private static string RaporOzeti(KasaDbContext db, params (int Yil, int Ay)[] aylar)
    {
        var hesap = new Kasa.Api.Servisler.HesapServisi(db);
        var raporlar = new List<object> { hesap.Haftalik(), hesap.Panel() };
        raporlar.AddRange(aylar.Select(a => (object)hesap.Aylik(a.Yil, a.Ay)));
        return System.Text.Json.JsonSerializer.Serialize(raporlar, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }

    /// <summary>Verilen ayların aylık raporu (bağlamın sabit saatiyle), JSON olarak.</summary>
    private static string AylikOzeti(KasaDbContext db, IEnumerable<(int Yil, int Ay)> aylar)
    {
        var hesap = new Kasa.Api.Servisler.HesapServisi(db);
        return System.Text.Json.JsonSerializer.Serialize(aylar.Select(a => hesap.Aylik(a.Yil, a.Ay)).ToList(),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }

    /// <summary>Tabloların bütün satır ve sütunları, saklama türüyle (kimliğe göre sıralı). Hariç sütun adı ya da "Tablo.Sütun".</summary>
    private static string Dokum(SqliteConnection connection, IEnumerable<string> tablolar, params string[] haricSutunlar)
    {
        var satirlar = new List<string>();
        foreach (var tablo in tablolar)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{tablo}\" ORDER BY Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                satirlar.Add(tablo + ": " + string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Where(i => !haricSutunlar.Contains(reader.GetName(i)) && !haricSutunlar.Contains($"{tablo}.{reader.GetName(i)}")).Select(i => reader.IsDBNull(i) ? "NULL"
                    : reader.GetDataTypeName(i) + ":" + (reader.GetValue(i) is byte[] b ? Convert.ToHexString(b) : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)))));
        }
        return string.Join("\n", satirlar);
    }

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
