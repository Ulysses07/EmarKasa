using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20260925000200_Notifications")]
public sealed class Notifications : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE BildirimAyarlari (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          Etkin INTEGER NOT NULL, Saat INTEGER NOT NULL, Dakika INTEGER NOT NULL, Surum INTEGER NOT NULL);
        INSERT INTO BildirimAyarlari VALUES (1,1,9,0,1);
        CREATE TABLE Bildirimler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          OlayAnahtari TEXT NOT NULL, Baslik TEXT NOT NULL, Mesaj TEXT NOT NULL, Tarih TEXT NOT NULL,
          Hedef TEXT NOT NULL, Tur TEXT NOT NULL, KaynakId INTEGER NOT NULL, Okundu INTEGER NOT NULL, Iptal INTEGER NOT NULL);
        CREATE UNIQUE INDEX IX_Bildirimler_OlayAnahtari ON Bildirimler(OlayAnahtari);
        CREATE TABLE PushAbonelikler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          Endpoint TEXT NOT NULL, P256dh TEXT NOT NULL, Auth TEXT NOT NULL, CihazId TEXT NOT NULL,
          CihazAdi TEXT NOT NULL, OturumDamgasi TEXT NOT NULL, Olusturuldu INTEGER NOT NULL,
          SonBasarili INTEGER NULL, Etkin INTEGER NOT NULL);
        CREATE UNIQUE INDEX IX_PushAbonelikler_Endpoint ON PushAbonelikler(Endpoint);
        CREATE TABLE BildirimTeslimler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          BildirimId INTEGER NOT NULL REFERENCES Bildirimler(Id) ON DELETE CASCADE,
          AbonelikId INTEGER NOT NULL REFERENCES PushAbonelikler(Id) ON DELETE CASCADE,
          Deneme INTEGER NOT NULL, SonrakiDeneme INTEGER NOT NULL, KilitBitis INTEGER NOT NULL,
          Kilit TEXT NULL, Gonderildi INTEGER NULL, Iptal INTEGER NOT NULL);
        CREATE UNIQUE INDEX IX_BildirimTeslimler_BildirimId_AbonelikId ON BildirimTeslimler(BildirimId,AbonelikId);
        CREATE INDEX IX_BildirimTeslimler_AbonelikId ON BildirimTeslimler(AbonelikId);
        """);

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Bildirim geçmişi otomatik silinemez. Geri dönüş için doğrulanmış yedek kullanın.");

    protected override void BuildTargetModel(ModelBuilder b) => NotificationsSchemaModel.Build(b);
}
