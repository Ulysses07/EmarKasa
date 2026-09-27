using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260928000100_KartGecisKurali")]
public sealed class KartGecisKurali : Migration
{
    // Yalnız sütun ekler, hiçbir satırı dönüştürmez. Mevcut geçişler 0 (etki tarihi
    // kuralı) kalır; canlıdaki geçişlerin geçmiş ve ileri raporları birebir korunur.
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "TakipKartlar" ADD COLUMN "EskiDusumKurali" INTEGER NOT NULL DEFAULT 0;
        """);
    // Sütunu düşürmek yeni kuralla yapılmış geçişlerin bekleyen eski düşümlerini
    // raporlardan sessizce siler; geri dönüş doğrulanmış yedekle yapılır.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Kart geçiş kuralı mali sonucu belirler; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => KartGecisKuraliSchemaModel.Build(b);
}
