using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260926000100_MonthlyExpensesAndLocks")]
public sealed class MonthlyExpensesAndLocks : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE AylikGiderSablonlar (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Surum INTEGER NOT NULL);
        CREATE TABLE AylikGiderRevizyonlar (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          SablonId INTEGER NOT NULL REFERENCES AylikGiderSablonlar(Id) ON DELETE RESTRICT,
          Surum INTEGER NOT NULL, GecerliAy TEXT NOT NULL, Ad TEXT NOT NULL, Tur TEXT NOT NULL,
          Tutar TEXT NOT NULL, OdemeGunu INTEGER NOT NULL, DagilimTuru TEXT NOT NULL, DagilimJson TEXT NOT NULL, Aktif INTEGER NOT NULL);
        CREATE UNIQUE INDEX IX_AylikGiderRevizyonlar_SablonId_Surum ON AylikGiderRevizyonlar(SablonId,Surum);
        CREATE TABLE AylikGiderOdemeler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          SablonId INTEGER NOT NULL REFERENCES AylikGiderSablonlar(Id) ON DELETE RESTRICT,
          RevizyonId INTEGER NOT NULL REFERENCES AylikGiderRevizyonlar(Id) ON DELETE RESTRICT,
          Ay TEXT NOT NULL, Tarih TEXT NOT NULL, Tutar TEXT NOT NULL,
          IslemId INTEGER NULL REFERENCES Islemler(Id) ON DELETE SET NULL, Iptal INTEGER NOT NULL, IptalAciklamasi TEXT NULL);
        CREATE UNIQUE INDEX IX_AylikGiderOdemeler_SablonId_Ay ON AylikGiderOdemeler(SablonId,Ay) WHERE Iptal=0;
        CREATE UNIQUE INDEX IX_AylikGiderOdemeler_IslemId ON AylikGiderOdemeler(IslemId);
        CREATE INDEX IX_AylikGiderOdemeler_RevizyonId ON AylikGiderOdemeler(RevizyonId);
        CREATE TABLE AyKilidi (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Surum INTEGER NOT NULL, KilitliSonTarih TEXT NULL);
        INSERT INTO AyKilidi VALUES (1,1,NULL);
        CREATE TABLE AyKilidiOlaylar (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          OncekiSonTarih TEXT NULL, YeniSonTarih TEXT NULL, Aciklama TEXT NOT NULL, Zaman TEXT NOT NULL);
        CREATE TRIGGER TR_Gelenler_AyKilidi_Insert BEFORE INSERT ON Gelenler
        WHEN NEW.DonemStart <= (SELECT KilitliSonTarih FROM AyKilidi WHERE Id=1)
        BEGIN SELECT RAISE(ABORT,'Kilitli ay: once donemi acin.'); END;
        CREATE TRIGGER TR_Gelenler_AyKilidi_Update BEFORE UPDATE ON Gelenler
        WHEN MIN(NEW.DonemStart,OLD.DonemStart) <= (SELECT KilitliSonTarih FROM AyKilidi WHERE Id=1)
        BEGIN SELECT RAISE(ABORT,'Kilitli ay: once donemi acin.'); END;
        CREATE TRIGGER TR_Gelenler_AyKilidi_Delete BEFORE DELETE ON Gelenler
        WHEN OLD.DonemStart <= (SELECT KilitliSonTarih FROM AyKilidi WHERE Id=1)
        BEGIN SELECT RAISE(ABORT,'Kilitli ay: once donemi acin.'); END;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Aylık gider ve dönem kilidi geçmişi silinemez. Geri dönüş için doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => MonthlyExpensesAndLocksSchemaModel.Build(b);
}
