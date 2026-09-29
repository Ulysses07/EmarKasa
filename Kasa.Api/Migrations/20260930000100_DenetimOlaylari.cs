using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260930000100_DenetimOlaylari")]
public sealed class DenetimOlaylari : Migration
{
    // Yalnız tablo, indeks ve tetikleyici ekler; hiçbir satırı dönüştürmez (raporlar değişmez). Göç öncesi otomatik yedek
    // başlatıcıda alınır. Olay tablosu yalnız eklenir: tetikleyiciler UPDATE ve DELETE'i reddeder (EF yolu ayrıca kancada
    // reddedilir). KilitAcmaOlayiId AyKilidiOlaylar.Id'yi gösterir; kilit olayları silinmediğinden FK gerekmez.
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "DenetimOlaylari" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          "ZamanUtc" INTEGER NOT NULL, "AktorRol" TEXT NOT NULL, "AktorId" INTEGER NULL, "IstemciIp" TEXT NULL,
          "Tur" TEXT NOT NULL, "Varlik" TEXT NOT NULL, "VarlikId" TEXT NULL, "OncekiJson" TEXT NULL, "YeniJson" TEXT NULL,
          "Gerekce" TEXT NULL, "IstekId" TEXT NULL, "TraceId" TEXT NULL, "KilitAcmaOlayiId" INTEGER NULL);
        CREATE INDEX "IX_DenetimOlaylari_Varlik_VarlikId" ON "DenetimOlaylari" ("Varlik", "VarlikId");
        CREATE INDEX "IX_DenetimOlaylari_ZamanUtc" ON "DenetimOlaylari" ("ZamanUtc");
        CREATE INDEX "IX_DenetimOlaylari_KilitAcmaOlayiId" ON "DenetimOlaylari" ("KilitAcmaOlayiId");
        CREATE TRIGGER "TR_DenetimOlaylari_Degistirilemez" BEFORE UPDATE ON "DenetimOlaylari"
        BEGIN SELECT RAISE(ABORT,'Denetim kaydi degistirilemez.'); END;
        CREATE TRIGGER "TR_DenetimOlaylari_Silinemez" BEFORE DELETE ON "DenetimOlaylari"
        BEGIN SELECT RAISE(ABORT,'Denetim kaydi silinemez.'); END;
        """);
    // Tabloyu düşürmek denetim izini siler; geri dönüş doğrulanmış yedekle yapılır.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Denetim olayları silinemez; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => DenetimOlaylariSchemaModel.Build(b);
}
