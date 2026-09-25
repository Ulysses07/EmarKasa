using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260923000400_Operations")]
public sealed class Operations : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "EditorGuvenlik" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_EditorGuvenlik" PRIMARY KEY AUTOINCREMENT,
            "SifreHash" TEXT NULL, "KurtarmaHash" TEXT NULL, "Surum" INTEGER NOT NULL);
        CREATE TABLE "Belgeler" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Belgeler" PRIMARY KEY AUTOINCREMENT,
            "AlisId" INTEGER NOT NULL, "OdemeId" INTEGER NULL,
            "DosyaAdi" TEXT NOT NULL, "IcerikTuru" TEXT NOT NULL, "Boyut" INTEGER NOT NULL,
            "Yuklendi" TEXT NOT NULL, "Icerik" BLOB NOT NULL,
            CONSTRAINT "FK_Belgeler_Alislar_AlisId" FOREIGN KEY ("AlisId") REFERENCES "Alislar" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_Belgeler_AlisOdemeler_OdemeId" FOREIGN KEY ("OdemeId") REFERENCES "AlisOdemeler" ("Id") ON DELETE SET NULL);
        CREATE INDEX "IX_Belgeler_AlisId" ON "Belgeler" ("AlisId");
        CREATE INDEX "IX_Belgeler_OdemeId" ON "Belgeler" ("OdemeId");
        """);
    protected override void Down(MigrationBuilder m) { m.DropTable("Belgeler"); m.DropTable("EditorGuvenlik"); }
    protected override void BuildTargetModel(ModelBuilder b) => OperationsSchemaModel.Build(b);
}
