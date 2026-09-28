using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Ekstre satırının mevcut kayıtla eşleşmesi (gap-coklu-giris-cift-sayim-mutabakat-1). Yalnız ekler: EkstreKayitlar'a boş
/// olabilir EslesmeTuru/EslesmeId sütunları ve eşleşme dizini. Mevcut satırlar null kalır (veri dönüşümü yok); sahiplik
/// sütunları (IslemId, KartHarcamaId, KartOdemeId) ve benzersiz dizinleri değişmez, raporlar birebir aynı kalır.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class EkstreEslesmesi : Migration
{
    public const string Kimlik = "20261003000100_EkstreEslesmesi";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "EkstreKayitlar" ADD COLUMN "EslesmeTuru" TEXT NULL;
        ALTER TABLE "EkstreKayitlar" ADD COLUMN "EslesmeId" INTEGER NULL;
        CREATE INDEX "IX_EkstreKayitlar_EslesmeTuru_EslesmeId" ON "EkstreKayitlar" ("EslesmeTuru", "EslesmeId");
        """);
    // Sütunları düşürmek, alışa bağlanıp sahipliğini bırakmış ekstre satırlarının kaynağını siler; geri dönüş doğrulanmış yedekle.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Ekstre eşleşmeleri kaynak geçmişidir; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => EkstreEslesmesiSchemaModel.Build(b);
}
