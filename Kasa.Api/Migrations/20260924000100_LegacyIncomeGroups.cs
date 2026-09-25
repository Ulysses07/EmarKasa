using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260924000100_LegacyIncomeGroups")]
public sealed class LegacyIncomeGroups : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "Gelenler" ADD COLUMN "EskiYinelenenGrup" INTEGER NOT NULL DEFAULT 0;
        DROP INDEX IF EXISTS "IX_Gelenler_DonemStart_KanalId";
        DROP INDEX IF EXISTS "IX_Gelenler_DonemStart_Kanal";
        UPDATE "Gelenler" SET "EskiYinelenenGrup" = 1
        WHERE EXISTS (
            SELECT 1 FROM "Gelenler" other
            WHERE other."Id" <> "Gelenler"."Id" AND other."DonemStart" = "Gelenler"."DonemStart"
              AND (other."Kanal" = "Gelenler"."Kanal" COLLATE NOCASE
                   OR (other."KanalId" IS NOT NULL AND other."KanalId" = "Gelenler"."KanalId")));
        CREATE UNIQUE INDEX "IX_Gelenler_DonemStart_KanalId" ON "Gelenler" ("DonemStart", "KanalId") WHERE "EskiYinelenenGrup" = 0;
        CREATE UNIQUE INDEX "IX_Gelenler_DonemStart_Kanal" ON "Gelenler" ("DonemStart", "Kanal") WHERE "EskiYinelenenGrup" = 0;

        CREATE TRIGGER "TR_Gelenler_EskiGrup_Insert" BEFORE INSERT ON "Gelenler"
        WHEN NEW."EskiYinelenenGrup" <> 0 OR EXISTS (
            SELECT 1 FROM "Gelenler" old WHERE old."EskiYinelenenGrup" = 1
              AND (old."Id" = NEW."Id" OR (old."DonemStart" = NEW."DonemStart"
                AND (old."Kanal" = NEW."Kanal" COLLATE NOCASE OR (old."KanalId" IS NOT NULL AND old."KanalId" = NEW."KanalId")))))
        BEGIN SELECT RAISE(ABORT, 'Eski yinelenen gelir grubu salt okunurdur.'); END;

        CREATE TRIGGER "TR_Gelenler_EskiGrup_Update" BEFORE UPDATE ON "Gelenler"
        WHEN NEW."EskiYinelenenGrup" IS NOT OLD."EskiYinelenenGrup"
          OR (OLD."EskiYinelenenGrup" = 1 AND (
               NEW."Id" IS NOT OLD."Id" OR NEW."DonemStart" IS NOT OLD."DonemStart"
               OR NEW."KanalId" IS NOT OLD."KanalId" OR NEW."TutarTl" IS NOT OLD."TutarTl"
               OR (OLD."KanalId" IS NULL AND NEW."Kanal" COLLATE BINARY IS NOT OLD."Kanal")))
          OR (OLD."EskiYinelenenGrup" = 0 AND EXISTS (
               SELECT 1 FROM "Gelenler" old WHERE old."EskiYinelenenGrup" = 1
                 AND (old."Id" = NEW."Id" OR (old."DonemStart" = NEW."DonemStart"
                   AND (old."Kanal" = NEW."Kanal" COLLATE NOCASE OR (old."KanalId" IS NOT NULL AND old."KanalId" = NEW."KanalId"))))))
        BEGIN SELECT RAISE(ABORT, 'Eski yinelenen gelir grubu salt okunurdur.'); END;

        CREATE TRIGGER "TR_Gelenler_EskiGrup_Delete" BEFORE DELETE ON "Gelenler"
        WHEN OLD."EskiYinelenenGrup" = 1
        BEGIN SELECT RAISE(ABORT, 'Eski yinelenen gelir grubu salt okunurdur.'); END;
        """);

    // Önceki şema bu satırların tümünü temsil edemez. Otomatik veri silen veya
    // birleştiren geri alma yerine korunmuş eski veritabanı yedeği kullanılmalı.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException(
        "Eski gelir grupları veri kaybetmeden önceki tekil şemaya geri alınamaz. Korunmuş veritabanı yedeğini kullanın.");

    protected override void BuildTargetModel(ModelBuilder b) => LegacyIncomeGroupsSchemaModel.Build(b);
}
