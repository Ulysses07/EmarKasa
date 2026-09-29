using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Belge deposu hazırlığı (data-3, gap-okuma-yolu-maliyet-kilit-cekismesi-8, gap-denetim-izi-gozlemlenebilirlik-9). Yalnız
/// Belgeler'e sütun ve dizin ekler; mevcut satırların hiçbir değeri değişmez:
/// <list type="bullet">
/// <item>IcerikOzeti: içeriğin belge deposundaki SHA-256 özeti. Mevcut satırlarda boş başlar; başlatıcının veri adımı
/// (<see cref="BelgeDeposuAktarimi"/>) her içeriği depoya yazıp geri okuyarak doğruladıktan sonra satır satır doldurur.</item>
/// <item>Yumuşak silme ve yükleyen izi: YukleyenRol/YukleyenId (eski belgelerde bilinmez: null), Silindi (mevcutlar 0),
/// SilinmeZamani, SilenRol, SilenId, SilmeGerekcesi; (AlisId, Silindi) dizini.</item>
/// </list>
/// BLOB sütunları bir sonraki migration'da (<see cref="BelgeDeposuGocu"/>) düşürülür. Göç öncesi otomatik yedek başlatıcıda
/// alınır; geri dönüş o yedekle yapılır.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class BelgeDeposuHazirlik : Migration
{
    public const string Kimlik = "20261002000100_BelgeDeposuHazirlik";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "Belgeler" ADD COLUMN "IcerikOzeti" TEXT NULL;
        ALTER TABLE "Belgeler" ADD COLUMN "YukleyenRol" TEXT NULL;
        ALTER TABLE "Belgeler" ADD COLUMN "YukleyenId" INTEGER NULL;
        ALTER TABLE "Belgeler" ADD COLUMN "Silindi" INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE "Belgeler" ADD COLUMN "SilinmeZamani" TEXT NULL;
        ALTER TABLE "Belgeler" ADD COLUMN "SilenRol" TEXT NULL;
        ALTER TABLE "Belgeler" ADD COLUMN "SilenId" INTEGER NULL;
        ALTER TABLE "Belgeler" ADD COLUMN "SilmeGerekcesi" TEXT NULL;
        CREATE INDEX "IX_Belgeler_AlisId_Silindi" ON "Belgeler" ("AlisId", "Silindi");
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Belge deposu geçişi geri alınamaz; göç öncesi yedekle ve önceki sürümün imajıyla dönün.");
    protected override void BuildTargetModel(ModelBuilder b) => BelgeDeposuHazirlikSchemaModel.Build(b);
}
