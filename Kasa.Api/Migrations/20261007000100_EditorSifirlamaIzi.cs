using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Editör şifresi sıfırlama izi (gap-geri-yukleme-durum-geri-sarma-10). Yalnız ekler: SistemDurumu'na boş (NULL)
/// "EditorSifirlamaIzi" sütunu (<see cref="SistemDurumuEntity.EditorSifirlamaIzi"/>). Mevcut satır dönüşmez; editör şifresi,
/// oturumlar ve raporlar değişmez. Sıfırlama bayrağı (Kasa:EditorSifreSifirla) açılmadıkça sütun boş kalır.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class EditorSifirlamaIzi : Migration
{
    public const string Kimlik = "20261007000100_EditorSifirlamaIzi";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "SistemDurumu" ADD COLUMN "EditorSifirlamaIzi" TEXT NULL;
        """);
    // Sütunu düşürmek sıfırlama izini siler: bayrak açık kaldıysa editörün sonradan değiştirdiği şifre bir sonraki açılışta ortam
    // şifresiyle ezilirdi. Diğer migration'lar gibi otomatik geri alınmaz; geri dönüş doğrulanmış yedekle.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Editör şifresi sıfırlama izi otomatik geri alınmaz; geri dönüş için doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => EditorSifirlamaIziSchemaModel.Build(b);
}
