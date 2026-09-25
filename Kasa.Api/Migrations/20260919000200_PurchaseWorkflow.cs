using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260919000200_PurchaseWorkflow")]
public sealed class PurchaseWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "Alicilar" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Alicilar" PRIMARY KEY AUTOINCREMENT,
                "Kullanici" TEXT COLLATE NOCASE NOT NULL, "Ad" TEXT NOT NULL,
                "SifreHash" TEXT NOT NULL, "Aktif" INTEGER NOT NULL, "OturumSurumu" INTEGER NOT NULL);
            CREATE UNIQUE INDEX "IX_Alicilar_Kullanici" ON "Alicilar" ("Kullanici");
            CREATE TABLE "Alislar" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Alislar" PRIMARY KEY AUTOINCREMENT,
                "Surum" INTEGER NOT NULL, "AliciId" INTEGER NULL, "Tarih" TEXT NOT NULL,
                "Tedarikci" TEXT NOT NULL, "Not" TEXT NULL, "Durum" TEXT NOT NULL, "EditorNotu" TEXT NULL,
                CONSTRAINT "FK_Alislar_Alicilar_AliciId" FOREIGN KEY ("AliciId") REFERENCES "Alicilar" ("Id") ON DELETE RESTRICT);
            CREATE INDEX "IX_Alislar_AliciId" ON "Alislar" ("AliciId");
            CREATE TABLE "AlisKalemler" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AlisKalemler" PRIMARY KEY AUTOINCREMENT,
                "AlisId" INTEGER NOT NULL, "Aciklama" TEXT NOT NULL, "Tutar" TEXT NOT NULL,
                CONSTRAINT "FK_AlisKalemler_Alislar_AlisId" FOREIGN KEY ("AlisId") REFERENCES "Alislar" ("Id") ON DELETE CASCADE);
            CREATE INDEX "IX_AlisKalemler_AlisId" ON "AlisKalemler" ("AlisId");
            CREATE TABLE "AlisDagilimlar" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AlisDagilimlar" PRIMARY KEY AUTOINCREMENT,
                "AlisKalemId" INTEGER NOT NULL, "KanalId" INTEGER NOT NULL, "Tutar" TEXT NOT NULL,
                CONSTRAINT "FK_AlisDagilimlar_AlisKalemler_AlisKalemId" FOREIGN KEY ("AlisKalemId") REFERENCES "AlisKalemler" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_AlisDagilimlar_Kanallar_KanalId" FOREIGN KEY ("KanalId") REFERENCES "Kanallar" ("Id") ON DELETE RESTRICT);
            CREATE UNIQUE INDEX "IX_AlisDagilimlar_AlisKalemId_KanalId" ON "AlisDagilimlar" ("AlisKalemId", "KanalId");
            CREATE INDEX "IX_AlisDagilimlar_KanalId" ON "AlisDagilimlar" ("KanalId");
            CREATE TABLE "AlisOdemeler" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AlisOdemeler" PRIMARY KEY AUTOINCREMENT,
                "AlisId" INTEGER NOT NULL, "IslemId" INTEGER NOT NULL, "IstekId" TEXT NOT NULL, "IstekOzeti" TEXT NOT NULL,
                CONSTRAINT "FK_AlisOdemeler_Alislar_AlisId" FOREIGN KEY ("AlisId") REFERENCES "Alislar" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_AlisOdemeler_Islemler_IslemId" FOREIGN KEY ("IslemId") REFERENCES "Islemler" ("Id") ON DELETE RESTRICT);
            CREATE INDEX "IX_AlisOdemeler_AlisId" ON "AlisOdemeler" ("AlisId");
            CREATE UNIQUE INDEX "IX_AlisOdemeler_IslemId" ON "AlisOdemeler" ("IslemId");
            CREATE UNIQUE INDEX "IX_AlisOdemeler_IstekId" ON "AlisOdemeler" ("IstekId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AlisOdemeler");
        migrationBuilder.DropTable("AlisDagilimlar");
        migrationBuilder.DropTable("AlisKalemler");
        migrationBuilder.DropTable("Alislar");
        migrationBuilder.DropTable("Alicilar");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => PurchaseSchemaModel.Build(modelBuilder);
}
