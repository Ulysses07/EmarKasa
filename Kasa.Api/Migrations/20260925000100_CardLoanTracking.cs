using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260925000100_CardLoanTracking")]
public sealed class CardLoanTracking : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE TakipKartlar (KrediKartiId INTEGER NOT NULL PRIMARY KEY REFERENCES KrediKartlari(Id) ON DELETE RESTRICT,
          Surum INTEGER NOT NULL, Baslangic TEXT NOT NULL, Aktif INTEGER NOT NULL, EskiKayit INTEGER NOT NULL);
        CREATE TABLE TakipEkstreler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          KrediKartiId INTEGER NOT NULL REFERENCES TakipKartlar(KrediKartiId) ON DELETE RESTRICT,
          KesimTarihi TEXT NOT NULL, SonOdemeTarihi TEXT NOT NULL, AsgariOdeme TEXT NULL);
        CREATE UNIQUE INDEX IX_TakipEkstreler_KrediKartiId_KesimTarihi ON TakipEkstreler(KrediKartiId,KesimTarihi);
        CREATE TABLE TakipHarcamalar (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          KrediKartiId INTEGER NOT NULL REFERENCES TakipKartlar(KrediKartiId) ON DELETE RESTRICT,
          IslemId INTEGER NULL REFERENCES Islemler(Id) ON DELETE RESTRICT,
          KaynakHarcamaId INTEGER NULL REFERENCES TakipHarcamalar(Id) ON DELETE RESTRICT,
          Tarih TEXT NOT NULL, Aciklama TEXT NOT NULL, Tutar TEXT NOT NULL, TaksitSayisi INTEGER NOT NULL,
          Iptal INTEGER NOT NULL, DagilimJson TEXT NOT NULL, KasadaOncedenSayilanTutar TEXT NOT NULL);
        CREATE INDEX IX_TakipHarcamalar_KrediKartiId ON TakipHarcamalar(KrediKartiId);
        CREATE UNIQUE INDEX IX_TakipHarcamalar_IslemId ON TakipHarcamalar(IslemId);
        CREATE INDEX IX_TakipHarcamalar_KaynakHarcamaId ON TakipHarcamalar(KaynakHarcamaId);
        CREATE TABLE TakipKartTaksitler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          HarcamaId INTEGER NOT NULL REFERENCES TakipHarcamalar(Id) ON DELETE RESTRICT,
          EkstreId INTEGER NOT NULL REFERENCES TakipEkstreler(Id) ON DELETE RESTRICT, Tutar TEXT NOT NULL);
        CREATE INDEX IX_TakipKartTaksitler_HarcamaId ON TakipKartTaksitler(HarcamaId);
        CREATE INDEX IX_TakipKartTaksitler_EkstreId ON TakipKartTaksitler(EkstreId);
        CREATE TABLE TakipKartOdemeler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          KrediKartiId INTEGER NOT NULL REFERENCES TakipKartlar(KrediKartiId) ON DELETE RESTRICT,
          Tarih TEXT NOT NULL, Tutar TEXT NOT NULL, "Not" TEXT NULL, Iptal INTEGER NOT NULL, PaylarJson TEXT NOT NULL);
        CREATE INDEX IX_TakipKartOdemeler_KrediKartiId ON TakipKartOdemeler(KrediKartiId);
        CREATE TABLE TakipKrediler (KrediId INTEGER NOT NULL PRIMARY KEY REFERENCES Krediler(Id) ON DELETE RESTRICT,
          Surum INTEGER NOT NULL, Baslangic TEXT NOT NULL, Aktif INTEGER NOT NULL, EskiKayit INTEGER NOT NULL,
          MevcutKredi INTEGER NOT NULL, KanalIdleriJson TEXT NOT NULL, CekimPaylariJson TEXT NOT NULL);
        CREATE TABLE TakipKrediTaksitler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          KrediId INTEGER NOT NULL REFERENCES TakipKrediler(KrediId) ON DELETE RESTRICT,
          No INTEGER NOT NULL, Tarih TEXT NOT NULL, Tutar TEXT NOT NULL, Iptal INTEGER NOT NULL, "Not" TEXT NULL, DagilimJson TEXT NOT NULL);
        CREATE UNIQUE INDEX IX_TakipKrediTaksitler_KrediId_No ON TakipKrediTaksitler(KrediId,No);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Kart/kredi takibi mali geçmiş taşır; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => CardLoanTrackingSchemaModel.Build(b);
}
