using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Çekirdek kasa kayıtlarının sürümü (contract-6). Yalnız ekler: Islemler, Gelenler, Kanallar ve Ayarlar'a iyimser eşzamanlılık
/// belirteci olan Surum sütunu; mevcut satırlarda 0 (veri dönüşümü yok). Gelenler tetikleyicileri (eski yinelenen grup, kilitli ay)
/// sütun eklemeden etkilenmez; hesap motoru ve raporlar sürümü okumaz, sonuçları değişmez.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class CekirdekSurumleri : Migration
{
    public const string Kimlik = "20261005000100_CekirdekSurumleri";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE "Islemler" ADD COLUMN "Surum" INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE "Gelenler" ADD COLUMN "Surum" INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE "Kanallar" ADD COLUMN "Surum" INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE "Ayarlar" ADD COLUMN "Surum" INTEGER NOT NULL DEFAULT 0;
        """);
    // Sütunu düşürmek SQLite'ta tabloyu yeniden kurmak demektir (Gelenler tetikleyicileri dahil); geri dönüş doğrulanmış yedekle.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Çekirdek kayıt sürümleri otomatik geri alınmaz; geri dönüş için doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => CekirdekSurumleriSchemaModel.Build(b);
}
