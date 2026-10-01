using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Çek ve senet takibi (docs/specs/2026-10-01-cekler.md). Yalnız iki boş tablo ve dizinlerini ekler: <c>Cekler</c>
/// (<see cref="CekEntity"/>) ve <c>CekHareketler</c> (<see cref="CekHareketEntity"/>). Mevcut satırlara dokunmaz; çek
/// girilmedikçe raporlar aynıdır. Kanal ve çek bağları ON DELETE RESTRICT: çeki ya da hareketi olan kanal silinemez.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class Cekler : Migration
{
    public const string Kimlik = "20261008000100_Cekler";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "Cekler" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "Tur" TEXT NOT NULL, "Yon" TEXT NOT NULL,
          "No" TEXT NOT NULL, "Banka" TEXT NULL, "Kisi" TEXT NOT NULL, "Tutar" TEXT NOT NULL, "VadeTarihi" TEXT NOT NULL,
          "KanalId" INTEGER NULL REFERENCES "Kanallar" ("Id") ON DELETE RESTRICT, "Teminat" INTEGER NOT NULL, "Konum" TEXT NULL,
          "Not" TEXT NULL, "Surum" INTEGER NOT NULL);
        CREATE INDEX "IX_Cekler_KanalId" ON "Cekler" ("KanalId");
        CREATE TABLE "CekHareketler" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          "CekId" INTEGER NOT NULL REFERENCES "Cekler" ("Id") ON DELETE RESTRICT, "Sira" INTEGER NOT NULL, "Tur" TEXT NOT NULL,
          "Tarih" TEXT NOT NULL, "Tutar" TEXT NOT NULL, "NetTutar" TEXT NULL,
          "KanalId" INTEGER NULL REFERENCES "Kanallar" ("Id") ON DELETE RESTRICT, "Karsi" TEXT NULL);
        CREATE UNIQUE INDEX "IX_CekHareketler_CekId_Sira" ON "CekHareketler" ("CekId", "Sira");
        CREATE INDEX "IX_CekHareketler_KanalId" ON "CekHareketler" ("KanalId");
        """);
    // Tabloları düşürmek çek kayıtlarını ve türettikleri kasa hareketlerini siler; diğer migration'lar gibi otomatik geri alınmaz.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Çek tabloları mali geçmiş taşır; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => CeklerSchemaModel.Build(b);
}
