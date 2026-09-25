using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Tests;

public class FinanceMigrationTests
{
    [Fact]
    public void Gecis_yeni_cari_uretmez_alis_adi_ve_odeme_istek_gecmisini_korur()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).Options);
        db.GetService<IMigrator>().Migrate("20260919000200_PurchaseWorkflow");
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Cariler(Id,Ad,Aktif) VALUES (10,'Mevcut',1),(20,'Belirsiz',1),(21,'belirsiz',0);
            INSERT INTO Alislar(Id,Surum,Tarih,Tedarikci,Durum) VALUES (1,2,'2026-09-01','mevcut','Taslak'),(2,1,'2026-09-01','Yeni','Taslak'),(3,1,'2026-09-01','Belirsiz','Taslak');
            INSERT INTO AlisKalemler(Id,AlisId,Aciklama,Tutar) VALUES (1,1,'Ürün','100.00');
            INSERT INTO Islemler(Id,Tarih,Cari,TutarTl,Kanal,Tip) VALUES (1,'2026-09-01','Mevcut','20.00','Dağılım bekliyor',0);
            INSERT INTO AlisOdemeler(Id,AlisId,IslemId,IstekId,IstekOzeti) VALUES (1,1,1,'1D2D3D4D-1111-4444-8888-123456789ABC','digest');
            INSERT INTO Krediler(Id,Ad,CekilenTutar,CekimTarihi,TaksitSayisi,AylikOdeme,OdemeGunu,Kanal) VALUES (1,'Eski kredi','100','2026-09-01',2,'50',10,'Ortak');
            """;
        command.ExecuteNonQuery();
        KasaDatabaseInitializer.Initialize(db);
        Assert.All(db.Alislar, a => Assert.Null(a.TedarikciId));
        Assert.Equal(new[] { "mevcut", "Yeni", "Belirsiz" }, db.Alislar.OrderBy(a => a.Id).Select(a => a.Tedarikci).ToArray());
        Assert.Equal(3, db.Cariler.Count()); Assert.DoesNotContain(db.Cariler, c => c.Ad == "Yeni"); Assert.Equal(20m, db.Islemler.Single().TutarTl);
        Assert.Null(db.AlisKalemler.Single().Miktar); Assert.Null(db.Alislar.Single(a => a.Id == 1).Vade);
        Assert.False(db.Krediler.Single().GerceklesmeTakibi);
        var history = Assert.Single(db.FinansIstekler); Assert.Equal("digest", history.Ozet); Assert.Equal("AlisOdeme", history.Tur); Assert.Equal(1, history.SonucId);
        Assert.Empty(db.HesapHareketler); Assert.False(db.Database.HasPendingModelChanges());
        KasaDatabaseInitializer.Initialize(db); Assert.Single(db.FinansIstekler);
    }
}
