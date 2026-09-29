using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260929000300_AyRaporAnlikGoruntuleri")]
public sealed class AyRaporAnlikGoruntuleri : Migration
{
    // Yalnız tablo ve tetikleyici ekler, hiçbir satırı dönüştürmez. Bu sürümden önce kilitlenmiş ayların görüntüsü
    // açılıştaki geçiş tohumunda (AyRaporAnlikGoruntusu.GecisTohumu) kural 1 ile, göç öncesi yedekten sonra yazılır.
    // Tetikleyiciler: görüntü yalnız kilitli ay için eklenir, hiç güncellenmez, kilitli ayın görüntüsü silinmez
    // (kilit önce açılır). İletideki "Kilitli ay" API'de anlaşılır kilit hatasına çevrilir.
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE AyRaporAnlikGoruntuleri (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          Yil INTEGER NOT NULL, Ay INTEGER NOT NULL, KuralSurumu INTEGER NOT NULL, Json TEXT NOT NULL, Zaman TEXT NOT NULL);
        CREATE UNIQUE INDEX IX_AyRaporAnlikGoruntuleri_Yil_Ay ON AyRaporAnlikGoruntuleri(Yil,Ay);
        CREATE TRIGGER TR_AyRaporAnlikGoruntuleri_Ekleme BEFORE INSERT ON AyRaporAnlikGoruntuleri
        WHEN (SELECT KilitliSonTarih FROM AyKilidi WHERE Id=1) IS NULL
          OR date(printf('%04d-%02d-01', NEW.Yil, NEW.Ay), '+1 month', '-1 day') > (SELECT KilitliSonTarih FROM AyKilidi WHERE Id=1)
        BEGIN SELECT RAISE(ABORT,'Kilitli ay: rapor goruntusu yalniz kilitli ay icin yazilir.'); END;
        CREATE TRIGGER TR_AyRaporAnlikGoruntuleri_Guncelleme BEFORE UPDATE ON AyRaporAnlikGoruntuleri
        BEGIN SELECT RAISE(ABORT,'Kilitli ay: rapor goruntusu degistirilemez.'); END;
        CREATE TRIGGER TR_AyRaporAnlikGoruntuleri_Silme BEFORE DELETE ON AyRaporAnlikGoruntuleri
        WHEN date(printf('%04d-%02d-01', OLD.Yil, OLD.Ay), '+1 month', '-1 day') <= (SELECT KilitliSonTarih FROM AyKilidi WHERE Id=1)
        BEGIN SELECT RAISE(ABORT,'Kilitli ay: once donemi acin.'); END;
        """);
    // Görüntüleri silmek kapatılmış ayların raporunu güncel kurala düşürür; geri dönüş doğrulanmış yedekle yapılır.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Kilitli ay rapor görüntüleri silinemez; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => AyRaporAnlikGoruntuleriSchemaModel.Build(b);
}
