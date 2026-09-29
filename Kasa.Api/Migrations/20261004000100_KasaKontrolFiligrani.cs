using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Kasa kontrolünün filigranı (gap-denetim-izi-gozlemlenebilirlik-3, gap-coklu-giris-cift-sayim-mutabakat-17). Yalnız ekler:
/// KasaKontrolleri'ne sürüm (mevcut satırlarda 1), hesap günü, kanal bakiyeleri, son gider/mali istek/denetim olayı kimlikleri ve
/// fark açıklaması sütunları. Mevcut satırların filigranı null kalır (veri dönüşümü yok): istemciler onları "eski kayıt, filigran
/// yok" diye gösterir. Rapor ve panel bu tabloyu okumaz; sonuçları değişmez.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class KasaKontrolFiligrani : Migration
{
    public const string Kimlik = "20261004000100_KasaKontrolFiligrani";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "KasaKontrolleri" ADD COLUMN "Surum" INTEGER NOT NULL DEFAULT 1;
        ALTER TABLE "KasaKontrolleri" ADD COLUMN "HesapTarihi" TEXT NULL;
        ALTER TABLE "KasaKontrolleri" ADD COLUMN "KanalBakiyeleriJson" TEXT NULL;
        ALTER TABLE "KasaKontrolleri" ADD COLUMN "SonIslemId" INTEGER NULL;
        ALTER TABLE "KasaKontrolleri" ADD COLUMN "SonFinansIstekId" INTEGER NULL;
        ALTER TABLE "KasaKontrolleri" ADD COLUMN "SonDenetimOlayId" INTEGER NULL;
        ALTER TABLE "KasaKontrolleri" ADD COLUMN "FarkAciklamasi" TEXT NULL;
        ALTER TABLE "KasaKontrolleri" ADD COLUMN "FarkAciklamaZamani" INTEGER NULL;
        """);
    // Sütunları düşürmek kontrol kayıtlarının filigranını ve fark açıklamalarını siler; geri dönüş doğrulanmış yedekle.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Kasa kontrol filigranı denetim geçmişidir; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => KasaKontrolFiligraniSchemaModel.Build(b);
}
