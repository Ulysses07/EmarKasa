using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Yapay zekâ önerileri (docs/specs/2026-11-25-yapay-zeka-onerileri.md): boş <c>YapayZekaCagrilar</c> (<see cref="YapayZekaCagriEntity"/>) ve
/// <c>YapayZekaOneriler</c> (<see cref="YapayZekaOneriEntity"/>) tabloları. Mevcut satırlara ve raporlara dokunmaz.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class YapayZekaOnerileri : Migration
{
    public const string Kimlik = "20261125000400_YapayZeka";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "YapayZekaCagrilar" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "IstekId" TEXT NOT NULL, "Tur" TEXT NOT NULL,
          "BelgeId" INTEGER NULL REFERENCES "EkstreBelgeler" ("Id") ON DELETE RESTRICT, "Ay" TEXT NOT NULL, "Zaman" INTEGER NOT NULL, "Model" TEXT NOT NULL,
          "SatirSayisi" INTEGER NOT NULL, "GirdiJetonu" INTEGER NOT NULL, "CiktiJetonu" INTEGER NOT NULL, "MaliyetUsd" TEXT NOT NULL, "Durum" TEXT NOT NULL,
          "SonucJson" TEXT NULL);
        CREATE UNIQUE INDEX "IX_YapayZekaCagrilar_IstekId" ON "YapayZekaCagrilar" ("IstekId");
        CREATE INDEX "IX_YapayZekaCagrilar_Ay" ON "YapayZekaCagrilar" ("Ay");
        CREATE INDEX "IX_YapayZekaCagrilar_BelgeId" ON "YapayZekaCagrilar" ("BelgeId");
        CREATE TABLE "YapayZekaOneriler" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          "BelgeId" INTEGER NOT NULL REFERENCES "EkstreBelgeler" ("Id") ON DELETE RESTRICT, "SatirNo" INTEGER NOT NULL,
          "CagriId" INTEGER NOT NULL REFERENCES "YapayZekaCagrilar" ("Id") ON DELETE RESTRICT, "IslemTuru" TEXT NOT NULL, "DagilimTuru" TEXT NULL,
          "KanalIdsJson" TEXT NOT NULL, "Kategori" TEXT NOT NULL, "Aciklama" TEXT NULL, "Guven" TEXT NOT NULL);
        CREATE UNIQUE INDEX "IX_YapayZekaOneriler_BelgeId_SatirNo" ON "YapayZekaOneriler" ("BelgeId", "SatirNo");
        CREATE INDEX "IX_YapayZekaOneriler_CagriId" ON "YapayZekaOneriler" ("CagriId");
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Harcama kaydı otomatik geri alınmaz; doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => YapayZekaSchemaModel.Build(b);
}
