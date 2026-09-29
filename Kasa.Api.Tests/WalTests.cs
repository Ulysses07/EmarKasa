using System.IO.Compression;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// WAL günlük kipi (gap-okuma-yolu-maliyet-kilit-cekismesi-2): başlatıcı dosya veritabanını kalıcı olarak WAL'a
/// alır; yedek (SQLite backup API) yalnız -wal dosyasında duran işlenmiş veriyi de içerir, ZIP'teki kasa.db
/// -wal/-shm gerektirmeyen tek dosyadır ve yedek dizininde kalıntı bırakılmaz.
/// </summary>
public class WalTests
{
    private static string GeciciYol(string ad) => Path.Combine(Path.GetTempPath(), $"kasa-wal-{ad}-{Guid.NewGuid():N}.db");
    private static void Sil(string yol) { foreach (var ek in new[] { "", "-wal", "-shm", "-journal" }) try { File.Delete(yol + ek); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    private static string Baglanti(string yol) => new SqliteConnectionStringBuilder { DataSource = yol, Pooling = false }.ToString();
    private static string Kip(SqliteConnection baglanti)
    {
        using var komut = baglanti.CreateCommand();
        komut.CommandText = "PRAGMA journal_mode;";
        return (string)komut.ExecuteScalar()!;
    }

    [Fact]
    public void Baslatici_dosya_veritabanini_kalici_WAL_kipine_alir()
    {
        var yol = GeciciYol("kip");
        try
        {
            using (var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(Baglanti(yol)).Options))
                KasaDatabaseInitializer.Initialize(db);
            // Kip dosya başlığında kalıcıdır: yeni bir bağlantı da WAL görür.
            using var yeni = new SqliteConnection(Baglanti(yol));
            yeni.Open();
            Assert.Equal("wal", Kip(yeni));
            // İkinci başlatma (uygulamanın her açılışı) sorunsuz ve kip değişmez.
            using (var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(Baglanti(yol)).Options))
                KasaDatabaseInitializer.Initialize(db);
            Assert.Equal("wal", Kip(yeni));
        }
        finally { SqliteConnection.ClearAllPools(); Sil(yol); }
    }

    [Fact]
    public void Bellek_ici_veritabani_baslatmada_bellek_kipinde_kalir()
    {
        using var baglanti = new SqliteConnection("Data Source=:memory:");
        baglanti.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(baglanti).Options);
        KasaDatabaseInitializer.Initialize(db);
        Assert.Equal("memory", Kip(baglanti));
    }

    [Fact]
    public async Task Yedek_WAL_dosyasindaki_veriyi_icerir_tek_dosyadir_ve_kalinti_birakmaz()
    {
        var dizin = Path.Combine(Path.GetTempPath(), "kasa-wal-yedek-" + Guid.NewGuid().ToString("N"));
        await using var f = new DosyaFabrikasi
        {
            EkAyarlar = new()
            {
                ["Yedek:Dizin"] = dizin,
                ["Yedek:Etkin"] = "false",
                ["Bildirim:PushEtkin"] = "false",
                ["Bildirim:WorkerEtkin"] = "false",
                ["Bildirim:AnahtarDosyasi"] = Path.Combine(dizin, ".kasa-push-keys.json")
            }
        };
        try
        {
            _ = f.Services; // Uygulama başlar: başlatıcı veritabanını WAL'a alır.
            // Açık kalan okuyucu, son bağlantı kapanırken yapılan denetim noktasını (checkpoint) önler: yeni kayıt yalnız -wal'dadır.
            using var tutucu = new SqliteConnection(f.Baglanti);
            tutucu.Open();
            using (var tut = tutucu.CreateCommand())
            { tut.CommandText = "SELECT COUNT(*) FROM Kanallar;"; tut.ExecuteScalar(); }
            using (var db = f.Baglam())
            { db.Kanallar.Add(new() { Ad = "WAL KANALI", Sira = 9 }); db.SaveChanges(); }
            Assert.True(new FileInfo(f.Yol + "-wal").Length > 0, "Kayıt WAL dosyasında olmalı.");

            string zip;
            using (var scope = f.Services.CreateScope())
                zip = await f.Services.GetRequiredService<YedekServisi>().Olustur(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), YedekTuru.Elle, CancellationToken.None);
            Assert.Equal(new[] { Path.GetFileName(zip) }, Directory.GetFiles(dizin).Select(Path.GetFileName));

            var acilan = Path.Combine(dizin, "acilan.db");
            using (var arsiv = ZipFile.OpenRead(zip))
                arsiv.GetEntry("kasa.db")!.ExtractToFile(acilan);
            var baslik = new byte[100];
            using (var akis = File.OpenRead(acilan))
                akis.ReadExactly(baslik);
            // Başlığın 18-19. baytları 1: geri alma günlüğü (rollback) kipi; 2 olsaydı dosya -wal/-shm isterdi.
            Assert.Equal((byte)1, baslik[18]);
            Assert.Equal((byte)1, baslik[19]);
            YedekServisi.Dogrula(acilan);
            using (var oku = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = acilan, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                oku.Open();
                using var komut = oku.CreateCommand();
                komut.CommandText = "SELECT COUNT(*) FROM Kanallar WHERE Ad = 'WAL KANALI';";
                Assert.Equal(1L, komut.ExecuteScalar());
            }
            Assert.False(File.Exists(acilan + "-wal"));
            Assert.False(File.Exists(acilan + "-shm"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            { if (Directory.Exists(dizin)) Directory.Delete(dizin, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
