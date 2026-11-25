using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// İşlem hafızasında unutulan anahtarlar (docs/specs/2026-11-25-islem-hafizasi.md): boş <c>EkstreHafizaUnutulanlar</c> tablosu
/// (<see cref="EkstreHafizaUnutulanEntity"/>). Hafızanın kendisi mevcut kayıtlardan hesaplanır; mevcut satırlara ve raporlara dokunmaz.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class EkstreHafizasi : Migration
{
    public const string Kimlik = "20261125000200_EkstreHafizasi";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "EkstreHafizaUnutulanlar" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "Kaynak" TEXT NOT NULL, "Anahtar" TEXT NOT NULL,
          "Yon" TEXT NOT NULL, "SonKayitId" INTEGER NOT NULL, "SonYokSayId" INTEGER NOT NULL);
        CREATE UNIQUE INDEX "IX_EkstreHafizaUnutulanlar_Kaynak_Anahtar_Yon" ON "EkstreHafizaUnutulanlar" ("Kaynak", "Anahtar", "Yon");
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Unutulan hafıza kayıtları kullanıcı kararıdır; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => EkstreHafizasiSchemaModel.Build(b);
}
