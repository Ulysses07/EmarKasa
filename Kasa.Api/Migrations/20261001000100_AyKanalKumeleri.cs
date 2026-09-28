using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Ay bazında kanal kümesi (core-1, ops-2): tamamlanmış ayın Ortak gider dağılımı ve rapor satırları o ayın kanal kümesiyle sabit
/// kalır (bkz. <see cref="AyKanalKumesi"/>). Yalnız tablo, indeks ve tetikleyici ekler; hiçbir satırı dönüştürmez. Veri adımı
/// (takip başlangıcından geçen aya kadar her tamamlanmış ayın bugünkü kanallarla dondurulması) bu migration'ın uygulandığı açılışta,
/// göç öncesi otomatik yedekten sonra ve İstanbul "bugün"üyle başlatıcıda çalışır (<see cref="AyKanalKumesi.GecisDondurmasi"/>);
/// geçişten önce bütün aylar bugünkü kanallarla hesaplandığından geçmiş raporlar birebir aynı kalır.
/// Değişmezlik: küme ve üyeleri güncellenmez ve silinmez (tetikleyiciler); kümede yer alan kanal silinmez (ON DELETE RESTRICT).
/// Tetikleyici iletileri API'de 409'a çevrilir.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration("20261001000100_AyKanalKumeleri")]
public sealed class AyKanalKumeleri : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "AyKanalKumeleri" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          "Yil" INTEGER NOT NULL, "Ay" INTEGER NOT NULL, "Kaynak" TEXT NOT NULL, "Zaman" TEXT NOT NULL);
        CREATE UNIQUE INDEX "IX_AyKanalKumeleri_Yil_Ay" ON "AyKanalKumeleri" ("Yil", "Ay");
        CREATE TABLE "AyKanalKumesiKanallari" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          "KumeId" INTEGER NOT NULL REFERENCES "AyKanalKumeleri" ("Id") ON DELETE RESTRICT,
          "KanalId" INTEGER NOT NULL REFERENCES "Kanallar" ("Id") ON DELETE RESTRICT,
          "Sira" INTEGER NOT NULL, "Aktif" INTEGER NOT NULL);
        CREATE UNIQUE INDEX "IX_AyKanalKumesiKanallari_KumeId_KanalId" ON "AyKanalKumesiKanallari" ("KumeId", "KanalId");
        CREATE INDEX "IX_AyKanalKumesiKanallari_KanalId" ON "AyKanalKumesiKanallari" ("KanalId");
        CREATE TRIGGER "TR_AyKanalKumeleri_Guncelleme" BEFORE UPDATE ON "AyKanalKumeleri"
        BEGIN SELECT RAISE(ABORT,'Kanal kumesi: tamamlanmis ayin kanal kumesi degistirilemez.'); END;
        CREATE TRIGGER "TR_AyKanalKumeleri_Silme" BEFORE DELETE ON "AyKanalKumeleri"
        BEGIN SELECT RAISE(ABORT,'Kanal kumesi: tamamlanmis ayin kanal kumesi silinemez.'); END;
        CREATE TRIGGER "TR_AyKanalKumesiKanallari_Guncelleme" BEFORE UPDATE ON "AyKanalKumesiKanallari"
        BEGIN SELECT RAISE(ABORT,'Kanal kumesi: tamamlanmis ayin kanal kumesi degistirilemez.'); END;
        CREATE TRIGGER "TR_AyKanalKumesiKanallari_Silme" BEFORE DELETE ON "AyKanalKumesiKanallari"
        BEGIN SELECT RAISE(ABORT,'Kanal kumesi: tamamlanmis ayin kanal kumesi silinemez.'); END;
        """);
    // Kümeleri silmek tamamlanmış ayların Ortak dağılımını bugünkü kanallara düşürür; geri dönüş doğrulanmış yedekle yapılır.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Ay kanal kümeleri geçmiş raporları korur; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => AyKanalKumeleriSchemaModel.Build(b);
}
