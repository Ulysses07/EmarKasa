using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Belge içerikleri belge deposunda (data-3, gap-okuma-yolu-maliyet-kilit-cekismesi-8). Başlatıcı bu migration'dan önce bütün
/// içerikleri depoya yazıp doğrulamış ve Belgeler.IcerikOzeti'ni doldurmuş olmalıdır (<see cref="BelgeDeposuAktarimi"/>).
/// <list type="number">
/// <item>Güvenlik denetimi SQL içindedir: özeti boş kalan tek bir belge varsa CHECK kısıtı migration'ı düşürür (transaction geri
/// alınır; hiçbir sütun silinmez).</item>
/// <item>Belgeler.Icerik ve EkstreBelgeler.Dosya düşürülür (SQLite ≥ 3.35; satırlar, kimlikler, sqlite_sequence ve ilişkiler
/// korunur).</item>
/// <item>IcerikOzeti dizini ve 'özet zorunlu' tetikleyicileri: eklenen ya da özeti değiştirilen belgenin özeti 64 haneli büyük
/// harf onaltılık olmalıdır (sütun ALTER ile eklendiğinden NOT NULL yerine tetikleyiciyle).</item>
/// </list>
/// Silinmiş belge içeriklerinin serbest sayfalardan da gitmesi için başlatıcı bu migration'ın uygulandığı açılışta bir kez VACUUM
/// çalıştırır. Geri alınamaz: dönüş göç öncesi yedek ve önceki sürümün imajıyladır.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class BelgeDeposuGocu : Migration
{
    public const string Kimlik = "20261002000200_BelgeDeposu";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TEMP TABLE "__belge_deposu_denetimi" ("Eksik" INTEGER NOT NULL
            CONSTRAINT "Belge_icerikleri_once_belge_deposuna_aktarilmali" CHECK ("Eksik" = 0));
        INSERT INTO "__belge_deposu_denetimi" SELECT COUNT(*) FROM "Belgeler" WHERE "IcerikOzeti" IS NULL;
        DROP TABLE "__belge_deposu_denetimi";
        ALTER TABLE "Belgeler" DROP COLUMN "Icerik";
        ALTER TABLE "EkstreBelgeler" DROP COLUMN "Dosya";
        CREATE INDEX "IX_Belgeler_IcerikOzeti" ON "Belgeler" ("IcerikOzeti");
        CREATE TRIGGER "TR_Belgeler_IcerikOzeti_Ekle" BEFORE INSERT ON "Belgeler"
        WHEN NEW."IcerikOzeti" IS NULL OR length(NEW."IcerikOzeti") <> 64 OR NEW."IcerikOzeti" GLOB '*[^0-9A-F]*'
        BEGIN SELECT RAISE(ABORT, 'Belge icerik ozeti zorunlu (64 haneli buyuk harf SHA-256).'); END;
        CREATE TRIGGER "TR_Belgeler_IcerikOzeti_Guncelle" BEFORE UPDATE OF "IcerikOzeti" ON "Belgeler"
        WHEN NEW."IcerikOzeti" IS NULL OR length(NEW."IcerikOzeti") <> 64 OR NEW."IcerikOzeti" GLOB '*[^0-9A-F]*'
        BEGIN SELECT RAISE(ABORT, 'Belge icerik ozeti zorunlu (64 haneli buyuk harf SHA-256).'); END;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Belge içerikleri veritabanına geri taşınamaz; göç öncesi yedekle ve önceki sürümün imajıyla dönün.");
    protected override void BuildTargetModel(ModelBuilder b) => BelgeDeposuSchemaModel.Build(b);
}
