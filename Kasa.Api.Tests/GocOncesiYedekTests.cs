using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>
/// Göç öncesi otomatik yedek (kullanıcı kararı: veri dönüştüren her migration'dan önce otomatik, tutarlı yedek) ve
/// adımlı yedek kopyası (data-10). Başlatıcı dosya tabanlı, boş olmayan veritabanında bekleyen iş varsa Migrate'ten önce
/// yedek alır; yedek alınamazsa migration çalışmaz ve açılış durur; bekleyen iş yoksa ve aynı kaynak için yedek alınmaz.
/// </summary>
public class GocOncesiYedekTests
{
    private const string OncekiMigration = "20260928000100_KartGecisKurali";

    private static string GeciciDizin() => Path.Combine(Path.GetTempPath(), "kasa-goc-" + Guid.NewGuid().ToString("N"));
    private static string Baglanti(string yol) => new SqliteConnectionStringBuilder { DataSource = yol, Pooling = false }.ToString();
    private static KasaDbContext Baglam(string yol) => new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(Baglanti(yol)).Options);
    private static void Temizle(string? dosya, string? dizin)
    {
        SqliteConnection.ClearAllPools();
        if (dosya is not null) foreach (var ek in new[] { "", "-wal", "-shm", "-journal" }) try { File.Delete(dosya + ek); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (dizin is not null) try { if (Directory.Exists(dizin)) Directory.Delete(dizin, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    private static string[] GocOncesiYedekleri(string dizin) => Directory.Exists(dizin)
        ? Directory.GetFiles(dizin, YedekSaklama.GocOncesiOnEki + "*.zip").Select(y => Path.GetFileName(y)!).ToArray() : [];
    private static void Calistir(SqliteConnection baglanti, string sql)
    {
        using var komut = baglanti.CreateCommand(); komut.CommandText = sql; komut.ExecuteNonQuery();
    }
    private static object? Deger(SqliteConnection baglanti, string sql)
    {
        using var komut = baglanti.CreateCommand(); komut.CommandText = sql; return komut.ExecuteScalar();
    }

    /// <summary>Canlıdaki gibi bir önceki sürümün şemasında, kayıt içeren dosya veritabanı.</summary>
    private static void OncekiSurumVeritabani(string yol)
    {
        using (var db = Baglam(yol)) db.GetService<IMigrator>().Migrate(OncekiMigration);
        using var baglanti = new SqliteConnection(Baglanti(yol)); baglanti.Open();
        Calistir(baglanti, """
            INSERT INTO Kanallar (Ad, Aktif, Sira, AcilisDevri) VALUES ('MEZAT', 1, 0, '125.50');
            INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri, IzleyiciSifreHash) VALUES ('2026-09-01', '1000.0', NULL);
            INSERT INTO Islemler (Tarih, Cari, TutarTl, Kanal, KanalId, Tip, "Not") VALUES ('2026-09-02', 'Firma', '345.67', 'MEZAT', 1, 0, 'göç öncesi');
            """);
    }

    /// <summary>Uygulamanın kendi yedek servisi: yedek dizini ve bildirim anahtarı yalnız geçici dizinde.</summary>
    internal static YedekServisi TestYedegi(string dizin)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Yedek:Dizin"] = dizin, ["Yedek:Etkin"] = "false", ["Bildirim:PushEtkin"] = "false",
        }).Build();
        var ortam = new TestOrtami();
        return new YedekServisi(cfg, ortam, new PushKimligi(cfg, ortam), NullLogger<YedekServisi>.Instance, TimeProvider.System);
    }

    private sealed class TestOrtami : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Kasa.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }

    private static DosyaFabrikasi Fabrika(string yedekDizini) => new()
    {
        EkAyarlar = new()
        {
            ["Yedek:Dizin"] = yedekDizini, ["Yedek:Etkin"] = "false", ["Bildirim:PushEtkin"] = "false", ["Bildirim:WorkerEtkin"] = "false",
            ["Finans:BakimEtkin"] = "false", ["Bildirim:AnahtarDosyasi"] = Path.Combine(yedekDizini + "-anahtar", ".kasa-push-keys.json"),
        }
    };

    [Fact]
    public void Bekleyen_migration_varsa_acilista_once_restore_bicimindeki_goc_oncesi_yedek_alinir()
    {
        var dizin = GeciciDizin();
        var f = Fabrika(dizin);
        try
        {
            OncekiSurumVeritabani(f.Yol);
            _ = f.Services; // açılış: başlatıcı yedek alır, sonra migration çalışır

            var ad = Assert.Single(GocOncesiYedekleri(dizin));
            // Rotasyon kalıbına uymaz: saklama kuralı bu yedeği hiç silmez, günlük/elle sayılarına girmez.
            Assert.Null(YedekSaklama.Tani(ad));
            Assert.DoesNotContain(Directory.GetFiles(dizin), y => !Path.GetFileName(y).StartsWith(YedekSaklama.GocOncesiOnEki, StringComparison.Ordinal));

            var acilan = Path.Combine(dizin, "acilan.db");
            JsonElement manifest;
            using (var arsiv = ZipFile.OpenRead(Path.Combine(dizin, ad)))
            {
                // restore_backup.py'nin kabul ettiği içerik: kasa.db + manifest.json (bildirim anahtarı yoksa).
                Assert.Equal(new[] { "kasa.db", "manifest.json" }, arsiv.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
                using (var akis = arsiv.GetEntry("manifest.json")!.Open()) manifest = JsonDocument.Parse(akis).RootElement.Clone();
                arsiv.GetEntry("kasa.db")!.ExtractToFile(acilan);
            }
            Assert.Equal("2.1.0", manifest.GetProperty("surum").GetString());
            Assert.Equal("goc-oncesi", manifest.GetProperty("tur").GetString());
            Assert.False(manifest.GetProperty("bildirimAnahtariDahil").GetBoolean());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(acilan))), manifest.GetProperty("sha256").GetString());
            Assert.Contains("20260928000200_KartGecisIzi", manifest.GetProperty("bekleyenIsler").EnumerateArray().Select(e => e.GetString()));
            Assert.StartsWith(YedekSaklama.GocOncesiOnEki, ad);
            Assert.Contains(manifest.GetProperty("sha256").GetString()![..8].ToLowerInvariant(), ad);

            // Tek dosya (geri alma günlüğü kipi), bütünlüğü sağlam ve göç ÖNCESİ durumu taşır.
            var baslik = new byte[100];
            using (var akis = File.OpenRead(acilan)) akis.ReadExactly(baslik);
            Assert.Equal((byte)1, baslik[18]); Assert.Equal((byte)1, baslik[19]);
            using (var oku = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = acilan, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                oku.Open();
                Assert.Equal("ok", Deger(oku, "PRAGMA integrity_check;"));
                Assert.Equal(1L, Deger(oku, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260923000400_Operations';"));
                Assert.Equal(0L, Deger(oku, "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20260928000200_KartGecisIzi';"));
                Assert.Equal("345.67", Deger(oku, "SELECT TutarTl FROM Islemler WHERE Cari = 'Firma';"));
            }

            // Ortamda Python varsa yedek gerçekten restore_backup.py ile doğrulanıp yeni dosyaya geri açılır.
            RestoreAraciylaAc(Path.Combine(dizin, ad), Path.Combine(dizin, "restore.db"));

            // Yedekten sonra migration tamamlandı; kayıtlar korundu.
            using var db = f.Baglam();
            Assert.Empty(db.Database.GetPendingMigrations());
            Assert.Equal(345.67m, db.Islemler.Single().TutarTl);
        }
        finally { f.Dispose(); Temizle(null, dizin); Temizle(null, dizin + "-anahtar"); }
    }

    [Fact]
    public void Bekleyen_veri_adimi_varsa_da_once_goc_oncesi_yedek_alinir_sonra_kilitli_aylar_dondurulur()
    {
        var dizin = GeciciDizin();
        var f = Fabrika(dizin);
        try
        {
            // Güncel şema, veri adımı (kilitli ay rapor görüntüsü tohumu) henüz çalışmamış: Ağustos sonuna kadar kilitli, görüntü yok.
            using (var db = Baglam(f.Yol)) db.GetService<IMigrator>().Migrate();
            using (var baglanti = new SqliteConnection(Baglanti(f.Yol)))
            {
                baglanti.Open();
                Calistir(baglanti, """
                    INSERT INTO Kanallar (Ad, Aktif, Sira, AcilisDevri) VALUES ('MEZAT', 1, 0, '0');
                    INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri, IzleyiciSifreHash) VALUES ('2026-07-01', '0', NULL);
                    INSERT INTO Islemler (Tarih, Cari, TutarTl, Kanal, KanalId, Tip, "Not") VALUES ('2026-07-02', 'Firma', '10', 'MEZAT', 1, 0, NULL);
                    UPDATE AyKilidi SET KilitliSonTarih = '2026-08-31', Surum = 2 WHERE Id = 1;
                    """);
            }
            _ = f.Services;

            var ad = Assert.Single(GocOncesiYedekleri(dizin));
            var acilan = Path.Combine(dizin, "acilan.db");
            using (var arsiv = ZipFile.OpenRead(Path.Combine(dizin, ad)))
            {
                using (var akis = arsiv.GetEntry("manifest.json")!.Open())
                {
                    var isler = JsonDocument.Parse(akis).RootElement.GetProperty("bekleyenIsler").EnumerateArray().Select(e => e.GetString()).ToList();
                    Assert.Equal(new[] { "Kilitli ay rapor görüntüsü (kural 1): 2026-07", "Kilitli ay rapor görüntüsü (kural 1): 2026-08" }, isler);
                }
                arsiv.GetEntry("kasa.db")!.ExtractToFile(acilan);
            }
            using (var oku = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = acilan, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                oku.Open();
                Assert.Equal(0L, Deger(oku, "SELECT COUNT(*) FROM AyRaporAnlikGoruntuleri;")); // veri adımından önceki durum
            }
            using (var db = f.Baglam())
                Assert.Equal(new[] { (2026, 7, 1), (2026, 8, 1) }, db.AyRaporAnlikGoruntuleri.OrderBy(g => g.Ay).Select(g => new { g.Yil, g.Ay, g.KuralSurumu }).AsEnumerable().Select(g => (g.Yil, g.Ay, g.KuralSurumu)));

            // Veri adımı bittikten sonraki açılışlarda bekleyen iş yoktur: yeni yedek alınmaz.
            using (var db = f.Baglam()) KasaDatabaseInitializer.Initialize(db, f.Services.GetRequiredService<YedekServisi>());
            Assert.Single(GocOncesiYedekleri(dizin));
        }
        finally { f.Dispose(); Temizle(null, dizin); Temizle(null, dizin + "-anahtar"); }
    }

    [Fact]
    public void Yedek_alinamazsa_migration_calismaz_ve_acilis_aciklayici_hatayla_durur()
    {
        // Yedek dizini yerinde aynı adlı bir DOSYA var: dizin oluşturulamaz, yedek alınamaz.
        var engel = Path.Combine(Path.GetTempPath(), "kasa-goc-engel-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(engel, "dizin değil");
        var f = Fabrika(engel);
        try
        {
            OncekiSurumVeritabani(f.Yol);
            var hata = Assert.ThrowsAny<Exception>(() => f.Services);
            var ileti = string.Join(" | ", Zincir(hata).Select(e => e.Message));
            Assert.Contains("göç öncesi yedek alınamadı", ileti);
            Assert.Contains("Güncelleme çalıştırılmadı", ileti);

            using var db = Baglam(f.Yol);
            Assert.Contains("20260928000200_KartGecisIzi", db.Database.GetPendingMigrations());
            Assert.Equal(345.67m, db.Islemler.Single().TutarTl);
        }
        finally { try { f.Dispose(); } catch (Exception) { /* açılmamış fabrika */ } Temizle(f.Yol, null); File.Delete(engel); Temizle(null, engel + "-anahtar"); }
    }

    /// <summary>deploy/restore_backup.py ile geri açar ve sonucu sınar; Python yoksa (ör. yalnız .NET kurulu makine) sessizce geçer:
    /// biçim aynı testte yapısal olarak da doğrulanır.</summary>
    private static void RestoreAraciylaAc(string zip, string cikti, [System.Runtime.CompilerServices.CallerFilePath] string kaynak = "")
    {
        var arac = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(kaynak)!, "..", "deploy", "restore_backup.py"));
        foreach (var python in new[] { "python3", "python" })
        {
            Process? p;
            try
            {
                p = Process.Start(new ProcessStartInfo(python) { ArgumentList = { arac, zip, "--output", cikti }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            }
            catch (System.ComponentModel.Win32Exception) { continue; }
            if (p is null) continue;
            using (p)
            {
                var cikis = p.StandardOutput.ReadToEndAsync(); var hata = p.StandardError.ReadToEndAsync();
                Assert.True(p.WaitForExit(60_000), "restore_backup.py zamanında bitmedi.");
                // Windows'taki 'python3' uygulama mağazası kısayolu olabilir (9009): gerçek Python değilse sonrakine geç.
                if (p.ExitCode == 9009) continue;
                Assert.True(p.ExitCode == 0, $"restore_backup.py göç öncesi yedeği reddetti: {hata.Result} {cikis.Result}");
                Assert.Contains("goc-oncesi", cikis.Result);
            }
            using var oku = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = cikti, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            oku.Open();
            Assert.Equal("345.67", Deger(oku, "SELECT TutarTl FROM Islemler WHERE Cari = 'Firma';"));
            return;
        }
    }

    private static IEnumerable<Exception> Zincir(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
        {
            yield return x;
            if (x is AggregateException a) foreach (var ic in a.InnerExceptions.SelectMany(Zincir)) yield return ic;
        }
    }

    [Fact]
    public void Bekleyen_is_yoksa_yedek_alinmaz_yeni_bos_dosyada_da_alinmaz()
    {
        var dizin = GeciciDizin();
        var f = Fabrika(dizin);
        try
        {
            _ = f.Services; // yeni, boş dosya: korunacak veri yok
            Assert.Empty(GocOncesiYedekleri(dizin));
            using (var db = f.Baglam()) { db.Kanallar.Add(new() { Ad = "YENİ", Sira = 9 }); db.SaveChanges(); }
            // Olağan yeniden açılış: bütün migration'lar uygulanmış, veri adımı yok.
            using (var db = f.Baglam()) KasaDatabaseInitializer.Initialize(db, f.Services.GetRequiredService<YedekServisi>());
            Assert.Empty(GocOncesiYedekleri(dizin));
        }
        finally { f.Dispose(); Temizle(null, dizin); Temizle(null, dizin + "-anahtar"); }
    }

    [Fact]
    public void Ayni_kaynak_icin_ikinci_goc_oncesi_yedek_alinmaz()
    {
        // Güvenle köprülenemeyen eski şema (aynı adlı iki kanal): her açılış köprüde geri alınır, veri değişmez.
        // Yeniden başlayan konteyner her seferinde yeni yedek yazıp diski doldurmamalı.
        var yol = Path.Combine(Path.GetTempPath(), "kasa-goc-eski-" + Guid.NewGuid().ToString("N") + ".db");
        var dizin = GeciciDizin();
        try
        {
            using (var baglanti = new SqliteConnection(Baglanti(yol)))
            {
                baglanti.Open();
                Calistir(baglanti, """
                    CREATE TABLE Kanallar (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Ad TEXT NOT NULL, Aktif INTEGER NOT NULL, Sira INTEGER NOT NULL, AcilisDevri TEXT NOT NULL);
                    CREATE TABLE Cariler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Ad TEXT NOT NULL, Aktif INTEGER NOT NULL);
                    CREATE TABLE Ayarlar (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, TakipBaslangic TEXT NOT NULL, KasaAcilisDevri TEXT NOT NULL, IzleyiciSifreHash TEXT NULL);
                    CREATE TABLE Islemler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Tarih TEXT NOT NULL, Cari TEXT NOT NULL, TutarTl TEXT NOT NULL, Kanal TEXT NOT NULL, Tip INTEGER NOT NULL, "Not" TEXT NULL);
                    CREATE TABLE Gelenler (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, DonemStart TEXT NOT NULL, Kanal TEXT NOT NULL, TutarTl TEXT NOT NULL);
                    INSERT INTO Kanallar VALUES (7, 'MEZAT', 1, 0, '1234.56');
                    INSERT INTO Kanallar VALUES (8, 'mezat', 1, 1, '900.0');
                    INSERT INTO Ayarlar VALUES (1, '2026-09-01', '10000.0', NULL);
                    """);
            }
            var yedek = TestYedegi(dizin);
            for (var i = 0; i < 2; i++)
            {
                using var db = Baglam(yol);
                var hata = Assert.Throws<InvalidOperationException>(() => KasaDatabaseInitializer.Initialize(db, yedek));
                Assert.Contains("birden fazla kanal", hata.Message);
            }
            Assert.Single(GocOncesiYedekleri(dizin));
            // Aynı servisin yeni örneği de (yeniden başlayan süreç) dizindeki yedeği tanır.
            using (var db = Baglam(yol)) Assert.Throws<InvalidOperationException>(() => KasaDatabaseInitializer.Initialize(db, TestYedegi(dizin)));
            Assert.Single(GocOncesiYedekleri(dizin));
        }
        finally { Temizle(yol, dizin); }
    }

    [Fact]
    public void Yedek_servisi_verilmeden_bekleyen_is_yedeksiz_calistirilmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "kasa-goc-servissiz-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            OncekiSurumVeritabani(yol);
            using (var db = Baglam(yol))
            {
                var hata = Assert.Throws<InvalidOperationException>(() => KasaDatabaseInitializer.Initialize(db));
                Assert.Contains("yedeksiz güncelleme yapılmaz", hata.Message);
            }
            using (var db = Baglam(yol)) Assert.Contains("20260928000200_KartGecisIzi", db.Database.GetPendingMigrations());
        }
        finally { Temizle(yol, null); }
    }

    // ---- data-10: adımlı kopya ve meşgul kaynakta sınırlı yeniden deneme ----

    /// <summary>Geri alma günlüğü (DELETE) kipinde, birkaç yüz sayfalık kaynak: kopya sırasında okuyucu yazanı bekletir.</summary>
    private static string BuyukKaynak()
    {
        var yol = Path.Combine(Path.GetTempPath(), "kasa-adim-" + Guid.NewGuid().ToString("N") + ".db");
        using var baglanti = new SqliteConnection(Baglanti(yol)); baglanti.Open();
        Calistir(baglanti, "PRAGMA journal_mode = DELETE; CREATE TABLE Veri (Id INTEGER PRIMARY KEY, Icerik BLOB NOT NULL);");
        using var tx = baglanti.BeginTransaction();
        using var komut = baglanti.CreateCommand();
        komut.CommandText = "INSERT INTO Veri (Icerik) VALUES (randomblob(3000));";
        for (var i = 0; i < 300; i++) komut.ExecuteNonQuery();
        tx.Commit();
        return yol;
    }

    [Fact]
    public void Adimli_kopya_sayfa_gruplari_arasinda_kilidi_birakir_yazan_uzun_sure_beklemez()
    {
        var kaynakYol = BuyukKaynak();
        var hedefYol = kaynakYol + ".kopya";
        try
        {
            using var kaynak = new SqliteConnection(Baglanti(kaynakYol)); kaynak.Open();
            using var hedef = new SqliteConnection(Baglanti(hedefYol)); hedef.Open();
            // Yazanın kilit bekleme süresi 1 sn: kopya bütün dosya boyunca paylaşılan kilidi tutsaydı yazma düşerdi.
            using var yazan = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = kaynakYol, Pooling = false, DefaultTimeout = 1 }.ToString());
            yazan.Open();
            var adimlar = 0; TimeSpan? yazmaSuresi = null;
            YedekServisi.AdimliKopyala(kaynak, hedef, sayfaGrubu: 16, adimSonrasi: () =>
            {
                if (++adimlar != 3) return;
                var sure = Stopwatch.StartNew();
                Calistir(yazan, "INSERT INTO Veri (Icerik) VALUES (x'CAFE');");
                yazmaSuresi = sure.Elapsed;
            });
            Assert.True(adimlar > 3, $"Kopya sayfa grupları halinde alınmalı (adım: {adimlar}).");
            Assert.True(yazmaSuresi < TimeSpan.FromMilliseconds(500), $"Yazan adımlar arasında beklemeden işlemeli ({yazmaSuresi}).");
            // Yazma başka bağlantıdan geldiği için SQLite kopyayı baştan aldı: sonuç yazma sonrası tutarlı görüntüdür.
            Assert.Equal("ok", Deger(hedef, "PRAGMA integrity_check;"));
            Assert.Equal(301L, Deger(hedef, "SELECT COUNT(*) FROM Veri;"));
        }
        finally { Temizle(kaynakYol, null); Temizle(hedefYol, null); }
    }

    [Fact]
    public async Task Kaynak_mesgulken_kopya_sinirli_yeniden_denenir_ve_kilit_kalkinca_tamamlanir()
    {
        var kaynakYol = BuyukKaynak();
        var hedefYol = kaynakYol + ".kopya";
        try
        {
            using var tutan = new SqliteConnection(Baglanti(kaynakYol)); tutan.Open();
            Calistir(tutan, "BEGIN EXCLUSIVE; INSERT INTO Veri (Icerik) VALUES (x'BEEF');");
            var birak = Task.Run(async () => { await Task.Delay(400); Calistir(tutan, "COMMIT;"); });
            using var kaynak = new SqliteConnection(Baglanti(kaynakYol)); kaynak.Open();
            using var hedef = new SqliteConnection(Baglanti(hedefYol)); hedef.Open();
            var sure = Stopwatch.StartNew();
            YedekServisi.AdimliKopyala(kaynak, hedef);
            await birak;
            Assert.True(sure.Elapsed >= TimeSpan.FromMilliseconds(300), "Kopya meşgul kaynakta beklemeli ve yeniden denemeli.");
            Assert.Equal("ok", Deger(hedef, "PRAGMA integrity_check;"));
            Assert.Equal(301L, Deger(hedef, "SELECT COUNT(*) FROM Veri;"));
        }
        finally { Temizle(kaynakYol, null); Temizle(hedefYol, null); }
    }

    [Fact]
    public void Mesgul_kaynakta_bekleyen_kopya_iptal_edilebilir()
    {
        var kaynakYol = BuyukKaynak();
        var hedefYol = kaynakYol + ".kopya";
        try
        {
            using var tutan = new SqliteConnection(Baglanti(kaynakYol)); tutan.Open();
            Calistir(tutan, "BEGIN EXCLUSIVE; INSERT INTO Veri (Icerik) VALUES (x'BEEF');");
            using var kaynak = new SqliteConnection(Baglanti(kaynakYol)); kaynak.Open();
            using var hedef = new SqliteConnection(Baglanti(hedefYol)); hedef.Open();
            using var iptal = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var sure = Stopwatch.StartNew();
            Assert.ThrowsAny<OperationCanceledException>(() => YedekServisi.AdimliKopyala(kaynak, hedef, iptal.Token));
            Assert.True(sure.Elapsed < TimeSpan.FromSeconds(5));
            Calistir(tutan, "ROLLBACK;");
        }
        finally { Temizle(kaynakYol, null); Temizle(hedefYol, null); }
    }
}
