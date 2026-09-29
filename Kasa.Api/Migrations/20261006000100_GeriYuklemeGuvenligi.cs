using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Geri yükleme güvenliği (gap-geri-yukleme-durum-geri-sarma-1). Yalnız ekler: tek satırlık SistemDurumu tablosu (oturum dönemi,
/// yedek anı, son geri yükleme ve raporu). Satır boş oturum dönemiyle tohumlanır: oturum damgası bu sürümden öncekiyle birebir
/// aynı kalır, yayın hiçbir oturumu, tanıdık cihaz belirtecini ya da bildirim aboneliğini düşürmez. Mevcut veri dönüşmez; rapor ve
/// panel bu tabloyu okumaz, sonuçları değişmez.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class GeriYuklemeGuvenligi : Migration
{
    public const string Kimlik = "20261006000100_GeriYuklemeGuvenligi";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "SistemDurumu" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_SistemDurumu" PRIMARY KEY,
            "OturumDonemi" TEXT NOT NULL DEFAULT '',
            "YedekZamani" TEXT NULL,
            "SonGeriYukleme" TEXT NULL,
            "GeriYuklemeRaporu" TEXT NULL);
        INSERT INTO "SistemDurumu" ("Id", "OturumDonemi") VALUES (1, '');
        """);
    // Tabloyu düşürmek oturum dönemini boşa döndürür: bir geri yüklemeden önce alınmış oturumlar yeniden geçerli olabilir. Diğer
    // migration'lar gibi otomatik geri alınmaz (geri alma denemesi hiçbir şeyi değiştirmeden reddedilir); geri dönüş doğrulanmış yedekle.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Geri yükleme güvenliği tablosu otomatik geri alınmaz; geri dönüş için doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => GeriYuklemeGuvenligiSchemaModel.Build(b);
}
