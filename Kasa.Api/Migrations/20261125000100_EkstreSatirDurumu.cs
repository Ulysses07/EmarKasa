using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Ekstre satırının durumu (docs/specs/2026-11-25-pdf-ekrani.md): boş <c>EkstreYokSayilanlar</c> tablosu
/// (<see cref="EkstreYokSayilanEntity"/>) ve belgenin atlanan satırları (<see cref="EkstreBelgeEntity.AtlananJson"/>, eski belgede boş
/// liste). Mevcut satırların içeriğine ve raporlara dokunmaz.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class EkstreSatirDurumu : Migration
{
    public const string Kimlik = "20261125000100_EkstreSatirDurumu";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "EkstreBelgeler" ADD COLUMN "AtlananJson" TEXT NOT NULL DEFAULT '[]';
        CREATE TABLE "EkstreYokSayilanlar" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          "BelgeId" INTEGER NOT NULL REFERENCES "EkstreBelgeler" ("Id") ON DELETE RESTRICT, "SatirNo" INTEGER NOT NULL);
        CREATE UNIQUE INDEX "IX_EkstreYokSayilanlar_BelgeId_SatirNo" ON "EkstreYokSayilanlar" ("BelgeId", "SatirNo");
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Yok sayılan ekstre satırları kullanıcı kararıdır; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => EkstreSatirDurumuSchemaModel.Build(b);
}
