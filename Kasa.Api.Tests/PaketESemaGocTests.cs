using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>Paket E şema göçü: master'daki (paket D sonrası) DB'ye yeni tablolar ve geçmiş sütunları eklenir.</summary>
public class PaketESemaGocTests
{
    /// <summary>master'daki EnsureCreated şeması (sqlite_master'dan birebir).</summary>
    private const string MasterSema = """
        CREATE TABLE "Ayarlar" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Ayarlar" PRIMARY KEY AUTOINCREMENT,
            "TakipBaslangic" TEXT NOT NULL,
            "KasaAcilisDevri" TEXT NOT NULL,
            "IzleyiciSifreHash" TEXT NULL,
            "IzleyiciOturumSurumu" INTEGER NOT NULL,
            "EditorOturumSurumu" INTEGER NOT NULL
        )
        CREATE TABLE "Cariler" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Cariler" PRIMARY KEY AUTOINCREMENT,
            "Ad" TEXT NOT NULL,
            "Aktif" INTEGER NOT NULL
        )
        CREATE TABLE "Cekler" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Cekler" PRIMARY KEY AUTOINCREMENT,
            "Yon" INTEGER NOT NULL,
            "CekNo" TEXT NULL,
            "Banka" TEXT NULL,
            "Kisi" TEXT NOT NULL,
            "Tutar" TEXT NOT NULL,
            "DuzenlemeTarihi" TEXT NOT NULL,
            "VadeTarihi" TEXT NOT NULL,
            "Kanal" TEXT NOT NULL,
            "Durum" INTEGER NOT NULL,
            "IslemTarihi" TEXT NULL,
            "Not" TEXT NULL
        )
        CREATE TABLE "Degisiklikler" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Degisiklikler" PRIMARY KEY AUTOINCREMENT,
            "ZamanUtc" TEXT NOT NULL,
            "Rol" TEXT NOT NULL,
            "Tur" TEXT NOT NULL,
            "KayitId" INTEGER NULL,
            "Eylem" TEXT NOT NULL,
            "Ozet" TEXT NOT NULL,
            "EskiJson" TEXT NULL,
            "YeniJson" TEXT NULL,
            "GeriAlindi" INTEGER NOT NULL,
            "GeriAlmaZamaniUtc" TEXT NULL
        )
        CREATE TABLE "Gelenler" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Gelenler" PRIMARY KEY AUTOINCREMENT,
            "DonemStart" TEXT NOT NULL,
            "Kanal" TEXT NOT NULL,
            "TutarTl" TEXT NOT NULL
        )
        CREATE TABLE "GiderKalemleri" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_GiderKalemleri" PRIMARY KEY AUTOINCREMENT,
            "Ad" TEXT NOT NULL,
            "Aktif" INTEGER NOT NULL
        )
        CREATE TABLE "IptalEdilenTokenlar" (
            "Jti" TEXT NOT NULL CONSTRAINT "PK_IptalEdilenTokenlar" PRIMARY KEY,
            "BitisUtc" TEXT NOT NULL
        )
        CREATE TABLE "Islemler" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Islemler" PRIMARY KEY AUTOINCREMENT,
            "Tarih" TEXT NOT NULL,
            "Cari" TEXT NOT NULL,
            "TutarTl" TEXT NOT NULL,
            "Kanal" TEXT NOT NULL,
            "Tip" INTEGER NOT NULL,
            "Not" TEXT NULL,
            "KrediKartiId" INTEGER NULL,
            CONSTRAINT "FK_Islemler_KrediKartlari_KrediKartiId" FOREIGN KEY ("KrediKartiId") REFERENCES "KrediKartlari" ("Id") ON DELETE SET NULL
        )
        CREATE TABLE "Kanallar" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Kanallar" PRIMARY KEY AUTOINCREMENT,
            "Ad" TEXT NOT NULL,
            "Aktif" INTEGER NOT NULL,
            "Sira" INTEGER NOT NULL,
            "AcilisDevri" TEXT NOT NULL
        )
        CREATE TABLE "KartOdemeler" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_KartOdemeler" PRIMARY KEY AUTOINCREMENT,
            "KrediKartiId" INTEGER NOT NULL,
            "Tarih" TEXT NOT NULL,
            "Tutar" TEXT NOT NULL,
            "Not" TEXT NULL,
            CONSTRAINT "FK_KartOdemeler_KrediKartlari_KrediKartiId" FOREIGN KEY ("KrediKartiId") REFERENCES "KrediKartlari" ("Id") ON DELETE CASCADE
        )
        CREATE TABLE "KasaSayimlari" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_KasaSayimlari" PRIMARY KEY AUTOINCREMENT,
            "Tarih" TEXT NOT NULL,
            "SayilanTutar" TEXT NOT NULL,
            "HesaplananTutar" TEXT NOT NULL,
            "Not" TEXT NULL,
            "KayitZamaniUtc" TEXT NOT NULL
        )
        CREATE TABLE "KrediKartlari" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_KrediKartlari" PRIMARY KEY AUTOINCREMENT,
            "Ad" TEXT NOT NULL,
            "KesimTarihi" TEXT NOT NULL,
            "SonOdemeTarihi" TEXT NOT NULL,
            "Limit" TEXT NOT NULL,
            "Borc" TEXT NOT NULL
        )
        CREATE TABLE "TekrarlayanGiderler" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_TekrarlayanGiderler" PRIMARY KEY AUTOINCREMENT,
            "Kalem" TEXT NOT NULL,
            "Kanal" TEXT NOT NULL,
            "Tutar" TEXT NOT NULL,
            "AyinGunu" INTEGER NOT NULL,
            "Aktif" INTEGER NOT NULL,
            "BaslangicAyi" TEXT NOT NULL
        )
        CREATE TABLE "TekrarlayanGirisler" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_TekrarlayanGirisler" PRIMARY KEY AUTOINCREMENT,
            "TekrarlayanGiderId" INTEGER NOT NULL,
            "Ay" TEXT NOT NULL,
            "Durum" INTEGER NOT NULL,
            "IslemId" INTEGER NULL,
            "Zaman" TEXT NOT NULL,
            CONSTRAINT "FK_TekrarlayanGirisler_Islemler_IslemId" FOREIGN KEY ("IslemId") REFERENCES "Islemler" ("Id") ON DELETE SET NULL,
            CONSTRAINT "FK_TekrarlayanGirisler_TekrarlayanGiderler_TekrarlayanGiderId" FOREIGN KEY ("TekrarlayanGiderId") REFERENCES "TekrarlayanGiderler" ("Id") ON DELETE CASCADE
        )
        CREATE UNIQUE INDEX "IX_Cariler_Ad" ON "Cariler" ("Ad")
        CREATE INDEX "IX_Cekler_IslemTarihi" ON "Cekler" ("IslemTarihi")
        CREATE INDEX "IX_Cekler_VadeTarihi" ON "Cekler" ("VadeTarihi")
        CREATE INDEX "IX_Degisiklikler_Tur" ON "Degisiklikler" ("Tur")
        CREATE INDEX "IX_Degisiklikler_ZamanUtc" ON "Degisiklikler" ("ZamanUtc")
        CREATE UNIQUE INDEX "IX_Gelenler_DonemStart_Kanal" ON "Gelenler" ("DonemStart", "Kanal")
        CREATE UNIQUE INDEX "IX_GiderKalemleri_Ad" ON "GiderKalemleri" ("Ad")
        CREATE INDEX "IX_Islemler_KrediKartiId" ON "Islemler" ("KrediKartiId")
        CREATE UNIQUE INDEX "IX_Kanallar_Ad" ON "Kanallar" ("Ad")
        CREATE INDEX "IX_KartOdemeler_KrediKartiId" ON "KartOdemeler" ("KrediKartiId")
        CREATE INDEX "IX_TekrarlayanGirisler_IslemId" ON "TekrarlayanGirisler" ("IslemId")
        CREATE UNIQUE INDEX "IX_TekrarlayanGirisler_TekrarlayanGiderId_Ay" ON "TekrarlayanGirisler" ("TekrarlayanGiderId", "Ay")
        """;

    private static IEnumerable<string> Komutlar()
        => MasterSema.Split("\nCREATE ").Select((k, i) => i == 0 ? k : "CREATE " + k);

    private static void Calistir(SqliteConnection conn, IEnumerable<string> sqller)
    {
        foreach (var sql in sqller)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public void Master_db_paket_e_semasina_veri_kaybetmeden_gecer()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Calistir(conn, Komutlar());
        Calistir(conn,
        [
            "INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri, IzleyiciSifreHash, IzleyiciOturumSurumu, EditorOturumSurumu) VALUES ('2026-06-01', '1000.0', NULL, 2, 3)",
            "INSERT INTO Kanallar (Ad, Aktif, Sira, AcilisDevri) VALUES ('MEZAT', 1, 0, '0.0')",
            "INSERT INTO Cariler (Ad, Aktif) VALUES ('Market', 1)",
            "INSERT INTO Islemler (Tarih, Cari, TutarTl, Kanal, Tip) VALUES ('2026-06-10', 'Market', '10.0', 'MEZAT', 0)",
            "INSERT INTO Degisiklikler (ZamanUtc, Rol, Tur, KayitId, Eylem, Ozet, GeriAlindi) VALUES ('2026-06-10 09:00:00', 'editor', 'İşlem', 1, 'Eklendi', 'İşlem eklendi', 0)",
        ]);

        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        var yapilan = SemaGuncelleyici.Guncelle(db, NullLogger.Instance, null);
        foreach (var tablo in new[] { "Kullanicilar", "GirisKayitlari", "OturumKayitlari", "GuvenlikAyarlari", "YedekDogrulamalari", "Sorular" })
            Assert.Contains($"tablo+ {tablo}", yapilan);
        Assert.Contains("sütun+ Degisiklikler.Kullanici", yapilan);
        Assert.Contains("sütun+ Degisiklikler.Cihaz", yapilan);

        // Eski veri yerinde; geçmişin yeni sütunları boş.
        var d = db.Degisiklikler.AsNoTracking().Single();
        Assert.Equal("İşlem eklendi", d.Ozet);
        Assert.Null(d.Kullanici);
        Assert.Null(d.Cihaz);
        Assert.Equal(10m, db.Islemler.AsNoTracking().Single().TutarTl);
        var a = db.Ayarlar.AsNoTracking().Single();
        Assert.Equal((2, 3), (a.IzleyiciOturumSurumu, a.EditorOturumSurumu));

        // İkinci çalıştırma bir şey yapmaz; açılış tohumu yerleşik editörü ve güvenlik ayarını ekler.
        Assert.Empty(SemaGuncelleyici.Guncelle(db, NullLogger.Instance, null));
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kasa:EditorKullanici"] = "emar", ["Kasa:EditorSifre"] = "env",
        }).Build();
        KimlikTohumu.Hazirla(db, cfg, NullLogger.Instance, TimeProvider.System);
        KimlikTohumu.Hazirla(db, cfg, NullLogger.Instance, TimeProvider.System);
        var k = db.Kullanicilar.AsNoTracking().Single();
        Assert.True(k.Yerlesik);
        Assert.Null(k.SifreHash);
        Assert.Single(db.GuvenlikAyarlari.AsNoTracking());
        Assert.Empty(db.Degisiklikler.AsNoTracking().Where(x => x.Tur == GecmisTurleri.Kullanici));
    }

    [Fact]
    public void Editor_yapilandirilmamissa_yerlesik_hesap_olusmaz()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        KimlikTohumu.Hazirla(db, new ConfigurationBuilder().Build(), NullLogger.Instance, TimeProvider.System);
        Assert.Empty(db.Kullanicilar);
        Assert.Single(db.GuvenlikAyarlari);
    }
}
