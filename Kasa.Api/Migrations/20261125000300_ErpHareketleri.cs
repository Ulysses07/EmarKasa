using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// ERP12 cari hareketleri (docs/specs/2026-11-25-erp12-aktarimi.md): boş <c>ErpHareketler</c> tablosu (<see cref="ErpHareketEntity"/>).
/// Mevcut satırlara ve raporlara dokunmaz.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class ErpHareketleri : Migration
{
    public const string Kimlik = "20261125000300_ErpHareketleri";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "ErpHareketler" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          "BelgeId" INTEGER NOT NULL REFERENCES "EkstreBelgeler" ("Id") ON DELETE RESTRICT, "SatirNo" INTEGER NULL, "Tarih" TEXT NOT NULL,
          "CariKodu" TEXT NULL, "CariAdi" TEXT NOT NULL, "CariAnahtari" TEXT NOT NULL, "IslemTuru" TEXT NULL, "BelgeNo" TEXT NULL, "Aciklama" TEXT NULL,
          "Borc" TEXT NOT NULL, "Alacak" TEXT NOT NULL, "Ozet" TEXT NOT NULL);
        CREATE UNIQUE INDEX "IX_ErpHareketler_Ozet" ON "ErpHareketler" ("Ozet");
        CREATE INDEX "IX_ErpHareketler_BelgeId_SatirNo" ON "ErpHareketler" ("BelgeId", "SatirNo");
        CREATE INDEX "IX_ErpHareketler_CariAnahtari" ON "ErpHareketler" ("CariAnahtari");
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("ERP12 hareketlerine bağlı eşleşmeler kullanıcı kararıdır; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => ErpHareketleriSchemaModel.Build(b);
}
