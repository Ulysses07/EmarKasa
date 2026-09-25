using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260926000200_CashControls")]
public sealed class CashControls : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE KasaEsikleri (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          KanalId INTEGER NOT NULL REFERENCES Kanallar(Id) ON DELETE RESTRICT,
          Surum INTEGER NOT NULL, Tutar TEXT NOT NULL, Etkin INTEGER NOT NULL,
          AlarmAcik INTEGER NOT NULL, OlaySayisi INTEGER NOT NULL, UyariTarihi TEXT NULL);
        CREATE UNIQUE INDEX IX_KasaEsikleri_KanalId ON KasaEsikleri(KanalId);
        CREATE TABLE KasaKontrolleri (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          Kaydedildi INTEGER NOT NULL, SistemBakiye TEXT NOT NULL, GercekBakiye TEXT NOT NULL,
          Fark TEXT NOT NULL, "Not" TEXT NULL);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Kasa kontrol geçmişi silinemez; doğrulanmış yedekle geri dönün.");
    protected override void BuildTargetModel(ModelBuilder b) => CashControlsSchemaModel.Build(b);
}
