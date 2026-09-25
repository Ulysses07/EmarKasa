using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260927000100_StatementImports")]
public sealed class StatementImports : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE EkstreBelgeler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Surum INTEGER NOT NULL,
          Kaynak TEXT NOT NULL, Banka TEXT NOT NULL, HesapAdi TEXT NOT NULL,
          KartId INTEGER NULL REFERENCES KrediKartlari(Id) ON DELETE RESTRICT,
          DosyaAdi TEXT NOT NULL, DosyaOzeti TEXT NOT NULL, Dosya BLOB NOT NULL, Yuklendi INTEGER NOT NULL,
          SatirlarJson TEXT NOT NULL, UyarilarJson TEXT NOT NULL);
        CREATE UNIQUE INDEX IX_EkstreBelgeler_DosyaOzeti ON EkstreBelgeler(DosyaOzeti);
        CREATE INDEX IX_EkstreBelgeler_KartId ON EkstreBelgeler(KartId);
        CREATE TABLE EkstreKayitlar (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          BelgeId INTEGER NOT NULL REFERENCES EkstreBelgeler(Id) ON DELETE RESTRICT,
          SatirNo INTEGER NOT NULL, Tarih TEXT NOT NULL, Aciklama TEXT NOT NULL, Tutar TEXT NOT NULL,
          IslemTuru TEXT NOT NULL, DagilimTuru TEXT NOT NULL, DagilimJson TEXT NOT NULL,
          KrediKartiId INTEGER NULL REFERENCES KrediKartlari(Id) ON DELETE RESTRICT,
          IslemId INTEGER NULL REFERENCES Islemler(Id) ON DELETE SET NULL,
          KartHarcamaId INTEGER NULL REFERENCES TakipHarcamalar(Id) ON DELETE RESTRICT,
          KartOdemeId INTEGER NULL REFERENCES TakipKartOdemeler(Id) ON DELETE RESTRICT,
          Iptal INTEGER NOT NULL, IptalAciklamasi TEXT NULL);
        CREATE UNIQUE INDEX IX_EkstreKayitlar_BelgeId_SatirNo ON EkstreKayitlar(BelgeId,SatirNo) WHERE "Iptal" = 0;
        CREATE INDEX IX_EkstreKayitlar_KrediKartiId ON EkstreKayitlar(KrediKartiId);
        CREATE UNIQUE INDEX IX_EkstreKayitlar_IslemId ON EkstreKayitlar(IslemId);
        CREATE UNIQUE INDEX IX_EkstreKayitlar_KartHarcamaId ON EkstreKayitlar(KartHarcamaId);
        CREATE UNIQUE INDEX IX_EkstreKayitlar_KartOdemeId ON EkstreKayitlar(KartOdemeId);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Ekstre kaynak geçmişi silinemez; doğrulanmış yedekle geri dönün.");
    protected override void BuildTargetModel(ModelBuilder b) => StatementImportsSchemaModel.Build(b);
}
