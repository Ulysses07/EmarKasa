using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260923000100_FinanceWorkflow")]
public class FinanceWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE Alislar ADD COLUMN TedarikciId INTEGER NULL REFERENCES Cariler(Id) ON DELETE RESTRICT;
        ALTER TABLE Alislar ADD COLUMN Vade TEXT NULL;
        ALTER TABLE AlisKalemler ADD COLUMN Miktar TEXT NULL;
        ALTER TABLE AlisKalemler ADD COLUMN BirimFiyat TEXT NULL;
        ALTER TABLE Krediler ADD COLUMN GerceklesmeTakibi INTEGER NOT NULL DEFAULT 0;
        CREATE INDEX IX_Alislar_TedarikciId ON Alislar(TedarikciId);
        CREATE TABLE Hesaplar (
          Id INTEGER NOT NULL CONSTRAINT PK_Hesaplar PRIMARY KEY AUTOINCREMENT,
          Surum INTEGER NOT NULL, Ad TEXT COLLATE NOCASE NOT NULL, Tur TEXT NOT NULL,
          AcilisTarihi TEXT NOT NULL, AcilisBakiyesi TEXT NOT NULL, Aktif INTEGER NOT NULL);
        CREATE UNIQUE INDEX IX_Hesaplar_Ad ON Hesaplar(Ad);
        CREATE TABLE HesapHareketler (
          Id INTEGER NOT NULL CONSTRAINT PK_HesapHareketler PRIMARY KEY AUTOINCREMENT,
          HesapId INTEGER NOT NULL REFERENCES Hesaplar(Id) ON DELETE RESTRICT,
          IslemId INTEGER NULL REFERENCES Islemler(Id) ON DELETE RESTRICT,
          GelenId INTEGER NULL REFERENCES Gelenler(Id) ON DELETE RESTRICT,
          KartOdemeId INTEGER NULL REFERENCES KartOdemeler(Id) ON DELETE RESTRICT,
          KrediId INTEGER NULL REFERENCES Krediler(Id) ON DELETE RESTRICT,
          KanalId INTEGER NULL REFERENCES Kanallar(Id) ON DELETE RESTRICT,
          Tarih TEXT NOT NULL, Tutar TEXT NOT NULL, Aciklama TEXT NOT NULL);
        CREATE INDEX IX_HesapHareketler_HesapId ON HesapHareketler(HesapId);
        CREATE INDEX IX_HesapHareketler_KanalId ON HesapHareketler(KanalId);
        CREATE UNIQUE INDEX IX_HesapHareketler_IslemId ON HesapHareketler(IslemId);
        CREATE UNIQUE INDEX IX_HesapHareketler_GelenId ON HesapHareketler(GelenId);
        CREATE UNIQUE INDEX IX_HesapHareketler_KartOdemeId ON HesapHareketler(KartOdemeId);
        CREATE UNIQUE INDEX IX_HesapHareketler_KrediId ON HesapHareketler(KrediId);
        CREATE TABLE HesapTransferler (
          Id INTEGER NOT NULL CONSTRAINT PK_HesapTransferler PRIMARY KEY AUTOINCREMENT,
          KaynakHesapId INTEGER NOT NULL REFERENCES Hesaplar(Id) ON DELETE RESTRICT,
          HedefHesapId INTEGER NOT NULL REFERENCES Hesaplar(Id) ON DELETE RESTRICT,
          Tarih TEXT NOT NULL, Tutar TEXT NOT NULL, Aciklama TEXT NOT NULL);
        CREATE INDEX IX_HesapTransferler_KaynakHesapId ON HesapTransferler(KaynakHesapId);
        CREATE INDEX IX_HesapTransferler_HedefHesapId ON HesapTransferler(HedefHesapId);
        CREATE TABLE FinansIstekler (
          Id INTEGER NOT NULL CONSTRAINT PK_FinansIstekler PRIMARY KEY AUTOINCREMENT,
          IstekId TEXT NOT NULL, Ozet TEXT NOT NULL, Tur TEXT NOT NULL, SonucId INTEGER NOT NULL, OncekiJson TEXT NULL);
        CREATE UNIQUE INDEX IX_FinansIstekler_IstekId ON FinansIstekler(IstekId);
        INSERT INTO FinansIstekler(IstekId,Ozet,Tur,SonucId) SELECT IstekId,IstekOzeti,'AlisOdeme',AlisId FROM AlisOdemeler;
        CREATE TABLE KrediTaksitOdemeler (
          Id INTEGER NOT NULL CONSTRAINT PK_KrediTaksitOdemeler PRIMARY KEY AUTOINCREMENT,
          KrediId INTEGER NOT NULL REFERENCES Krediler(Id) ON DELETE RESTRICT,
          TaksitNo INTEGER NOT NULL, IslemId INTEGER NOT NULL REFERENCES Islemler(Id) ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IX_KrediTaksitOdemeler_KrediId_TaksitNo ON KrediTaksitOdemeler(KrediId,TaksitNo);
        CREATE UNIQUE INDEX IX_KrediTaksitOdemeler_IslemId ON KrediTaksitOdemeler(IslemId);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Finans kayıtlarını kaybetmemek için geri dönüş doğrulanmış yedekten yapılır.");
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => FinanceSchemaModel.Build(modelBuilder);
}
