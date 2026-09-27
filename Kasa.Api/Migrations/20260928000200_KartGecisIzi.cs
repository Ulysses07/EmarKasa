using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260928000200_KartGecisIzi")]
public sealed class KartGecisIzi : Migration
{
    // Yalnız boş (NULL) sütun ekler, hiçbir satırı dönüştürmez. Geçiş açıklaması ve onay anındaki
    // önizleme özeti yeni geçişlerde yazılır; ilk sürüm geçişleri NULL kalır, raporlar değişmez.
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "TakipKartlar" ADD COLUMN "GecisAciklamasi" TEXT NULL;
        ALTER TABLE "TakipKartlar" ADD COLUMN "GecisOzetiJson" TEXT NULL;
        """);
    // Sütunları düşürmek mali kararın denetim izini siler; geri dönüş doğrulanmış yedekle yapılır.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Kart geçişinin denetim izi silinemez; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => KartGecisIziSchemaModel.Build(b);
}
