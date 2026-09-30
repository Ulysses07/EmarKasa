using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Api.Migrations;
using Kasa.Api.Servisler;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>
/// Belge deposu geçişi (data-3, gap-okuma-yolu-maliyet-kilit-cekismesi-8): canlıdaki gibi bir önceki sürümün şemasında (belge ve
/// ekstre içerikleri BLOB) dosya veritabanı açılışta göç öncesi yedekle korunur, içerikler depoya bayt bayt aynı taşınır, BLOB
/// sütunları düşer ve dosya sıkıştırılır. Kimlikler, sayaçlar, bütün diğer sütunlar, rapor yanıtları ve indirmeler önce/sonra
/// birebir aynıdır. Özeti tutmayan ekstre belgesi açılışı veri değiştirmeden durdurur; yarıda kalan aktarım yeniden başlatmada
/// tamamlanır.
/// </summary>
public class BelgeDeposuGecisTests
{
    private const string OncekiSurum = KartTakipDuzeltmeleri.Kimlik;
    private static readonly byte[] Fatura = "%PDF-1.7 Kereste AŞ faturası 1500 TL"u8.ToArray();
    private static readonly byte[] Dekont = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 42];
    private static readonly byte[] EkstrePdf = "%PDF-1.4 Akbank hesap hareketleri Ağustos"u8.ToArray();
    // Silinmiş belgenin içeriği: eski sürümde serbest sayfalarda kalır; geçişteki VACUUM ile dosyadan tamamen gider.
    private static readonly byte[] SilinenIcerik = Encoding.ASCII.GetBytes("%PDF-silinmis-belge-izi-7f3a91 " + new string('x', 6000));
    private static readonly JsonSerializerOptions HttpJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static string Ozet(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    private static string Baglanti(string yol) => new SqliteConnectionStringBuilder { DataSource = yol, Pooling = false }.ToString();
    private static DbContextOptions<KasaDbContext> Secenekler(string yol) => new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(Baglanti(yol))
        .UseApplicationServiceProvider(new ServiceCollection().AddSingleton<TimeProvider>(new SabitSaat(KasaWebFactory.VarsayilanBugun)).BuildServiceProvider()).Options;
    private static KasaDbContext Baglam(string yol) => new(Secenekler(yol));
    /// <summary>Önceki sürümün şemasında (çekirdek sürüm sütunlarından önce) veri kuran ve rapor okuyan bağlam; migration uygulamaz.</summary>
    private static KasaDbContext EskiBaglam(string yol) => new SurumOncesiBaglam(Secenekler(yol));
    private static string GeciciDizin(string ad) => Path.Combine(Path.GetTempPath(), $"kasa-{ad}-" + Guid.NewGuid().ToString("N"));

    private static DosyaFabrikasi Fabrika(string yedekDizini) => new()
    {
        EkAyarlar = new()
        {
            ["Yedek:Dizin"] = yedekDizini,
            ["Yedek:Etkin"] = "false",
            ["Bildirim:PushEtkin"] = "false",
            ["Bildirim:WorkerEtkin"] = "false",
            ["Finans:BakimEtkin"] = "false",
            ["Bildirim:AnahtarDosyasi"] = Path.Combine(yedekDizini + "-anahtar", ".kasa-push-keys.json"),
        }
    };

    private static void Temizle(params string[] dizinler)
    {
        SqliteConnection.ClearAllPools();
        foreach (var d in dizinler)
            try
            { if (Directory.Exists(d)) Directory.Delete(d, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Önceki sürüm (2.3.x, KartTakipDuzeltmeleri) şeması: onaylı alışın ödemeye bağlı faturası ve dekontu, taslakta aynı
    /// faturanın kopyası (aynı içerik), silinmiş bir belge (sayaç 4, içerik serbest sayfalarda) ve bir ekstre PDF'i.</summary>
    private static (int Onayli, int Taslak) EskiSurumVeritabani(string yol, string? ekstreOzeti = null)
    {
        using (var db = Baglam(yol))
            db.GetService<IMigrator>().Migrate(OncekiSurum);
        int onayli, taslak, odeme, mezat;
        using (var db = EskiBaglam(yol))
        {
            var mezatKanal = new KanalEntity { Ad = "MEZAT", Sira = 0 };
            db.Kanallar.AddRange(mezatKanal, new KanalEntity { Ad = "TOPTAN", Sira = 1 });
            db.Ayarlar.Add(new AyarEntity { TakipBaslangic = new(2026, 7, 1), KasaAcilisDevri = 1000m });
            var alici = new AliciEntity { Kullanici = "ayse", Ad = "Ayşe Alıcı", SifreHash = "x" };
            db.Alicilar.Add(alici);
            db.SaveChanges();
            mezat = mezatKanal.Id;
            db.Gelenler.Add(new GelenEntity { DonemStart = new(2026, 8, 3), Kanal = "MEZAT", KanalId = mezat, TutarTl = 48000.5m });
            var islem = new IslemEntity { Tarih = new(2026, 8, 5), Cari = "Kereste AŞ", TutarTl = 1500m, Kanal = "MEZAT", KanalId = mezat, Tip = GiderTipi.Cari };
            var alis = new AlisEntity
            {
                AliciId = alici.Id,
                Tarih = new(2026, 8, 5),
                Tedarikci = "Kereste AŞ",
                Durum = AlisDurumlari.Onaylandi,
                Kalemler = [new AlisKalemEntity { Aciklama = "Kereste", Tutar = 1500m, Dagilimlar = [new AlisDagilimEntity { KanalId = mezat, Tutar = 1500m }] }]
            };
            var taslakAlis = new AlisEntity { AliciId = alici.Id, Tarih = new(2026, 9, 20), Tedarikci = "Boya Ltd", Durum = AlisDurumlari.Taslak };
            db.Islemler.Add(islem);
            db.Alislar.AddRange(alis, taslakAlis);
            db.SaveChanges();
            var odemeKaydi = new AlisOdemeEntity { AlisId = alis.Id, IslemId = islem.Id, IstekId = Guid.NewGuid(), IstekOzeti = "gecis" };
            db.AlisOdemeler.Add(odemeKaydi);
            db.SaveChanges();
            (onayli, taslak, odeme) = (alis.Id, taslakAlis.Id, odemeKaydi.Id);
        }
        using var c = new SqliteConnection(Baglanti(yol));
        c.Open();
        void Belge(int alis, int? odemeId, string ad, string tur, byte[] icerik, string yuklendi)
        {
            using var k = c.CreateCommand();
            k.CommandText = "INSERT INTO Belgeler (AlisId, OdemeId, DosyaAdi, IcerikTuru, Boyut, Yuklendi, Icerik) VALUES ($a, $o, $ad, $tur, $boyut, $y, $icerik);";
            k.Parameters.AddWithValue("$a", alis);
            k.Parameters.AddWithValue("$o", (object?)odemeId ?? DBNull.Value);
            k.Parameters.AddWithValue("$ad", ad);
            k.Parameters.AddWithValue("$tur", tur);
            k.Parameters.AddWithValue("$boyut", icerik.LongLength);
            k.Parameters.AddWithValue("$y", yuklendi);
            k.Parameters.AddWithValue("$icerik", icerik);
            k.ExecuteNonQuery();
        }
        Belge(onayli, odeme, "fatura.pdf", "application/pdf", Fatura, "2026-08-05 10:15:00+03:00");
        Belge(onayli, null, "dekont.png", "image/png", Dekont, "2026-08-05 10:16:30+03:00");
        Belge(taslak, null, "Fatura kopyası.pdf", "application/pdf", Fatura, "2026-09-20 09:00:00+03:00");
        Belge(taslak, null, "silinecek.pdf", "application/pdf", SilinenIcerik, "2026-09-20 09:01:00+03:00");
        using (var k = c.CreateCommand())
        { k.CommandText = "DELETE FROM Belgeler WHERE Id = 4;"; k.ExecuteNonQuery(); }
        using (var k = c.CreateCommand())
        {
            k.CommandText = """
                INSERT INTO EkstreBelgeler (Surum, Kaynak, Banka, HesapAdi, KartId, DosyaAdi, DosyaOzeti, Dosya, Yuklendi, SatirlarJson, UyarilarJson)
                VALUES (1, 'Banka', 'Akbank', 'İş hesabı', NULL, 'ağustos.pdf', $ozet, $dosya, 1788000000000, '[]', '[]');
                """;
            k.Parameters.AddWithValue("$ozet", ekstreOzeti ?? Ozet(EkstrePdf));
            k.Parameters.AddWithValue("$dosya", EkstrePdf);
            k.ExecuteNonQuery();
        }
        return (onayli, taslak);
    }

    /// <summary>Kullanıcı tablolarının bütün satırları (BLOB içerik sütunları hariç; verilen sütun kümesiyle) ve sqlite_sequence.</summary>
    private static Dictionary<string, List<string>> Sutunlar(string yol, params string[] haric)
    {
        using var c = new SqliteConnection(Baglanti(yol));
        c.Open();
        var tablolar = new List<string>();
        using (var k = c.CreateCommand())
        {
            k.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory' ORDER BY name;";
            using var r = k.ExecuteReader();
            while (r.Read())
                tablolar.Add(r.GetString(0));
        }
        var sonuc = new Dictionary<string, List<string>>();
        foreach (var t in tablolar)
        {
            using var k = c.CreateCommand();
            k.CommandText = $"SELECT name FROM pragma_table_info('{t}') ORDER BY cid;";
            using var r = k.ExecuteReader();
            var liste = new List<string>();
            while (r.Read())
                if (!haric.Contains($"{t}.{r.GetString(0)}"))
                    liste.Add(r.GetString(0));
            sonuc[t] = liste;
        }
        return sonuc;
    }

    private static string Dokum(string yol, Dictionary<string, List<string>> sutunlar)
    {
        using var c = new SqliteConnection(Baglanti(yol));
        c.Open();
        var sb = new StringBuilder();
        foreach (var (t, liste) in sutunlar.Append(new("sqlite_sequence", ["name", "seq"])))
        {
            using var k = c.CreateCommand();
            k.CommandText = $"SELECT {string.Join(", ", liste.Select(s => $"\"{s}\""))} FROM \"{t}\" ORDER BY 1, 2;";
            using var r = k.ExecuteReader();
            while (r.Read())
                sb.Append(t).Append(": ").AppendJoin(" | ", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL"
                    : r.GetDataTypeName(i) + ":" + (r.GetValue(i) is byte[] b ? Convert.ToHexString(b) : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)))).Append('\n');
        }
        return sb.ToString();
    }

    private static object? Deger(string yol, string sql)
    {
        using var c = new SqliteConnection(Baglanti(yol));
        c.Open();
        using var k = c.CreateCommand();
        k.CommandText = sql;
        return k.ExecuteScalar();
    }

    private static (int Yil, int Ay)[] Aylar => [(2026, 7), (2026, 8), (2026, 9)];

    /// <summary>Rapor uçlarının gövdesi, uçların kullandığı hesapla ve uygulamanın HTTP JSON ayarlarıyla (bu kod, eski şemada).</summary>
    private static Dictionary<string, string> Raporlar(string yol)
    {
        using var db = EskiBaglam(yol);
        var hesap = new HesapServisi(db);
        var sonuc = new Dictionary<string, string>
        {
            ["/api/rapor/panel"] = JsonSerializer.Serialize(hesap.Panel(), HttpJson),
            ["/api/rapor/haftalik"] = JsonSerializer.Serialize(hesap.Haftalik(), HttpJson),
        };
        foreach (var (y, a) in Aylar)
        {
            var yanit = hesap.AylikYanit(y, a);
            sonuc[$"/api/rapor/aylik?yil={y}&ay={a}"] = JsonSerializer.Serialize(yanit, yanit.GetType(), HttpJson);
        }
        return sonuc;
    }

    [Fact]
    public async Task Icerikler_depoya_bayt_bayt_tasinir_kimlikler_sayaclar_raporlar_ve_indirmeler_birebir_ayni()
    {
        var yedekDizini = GeciciDizin("belge-gecis-yedek");
        var f = Fabrika(yedekDizini);
        try
        {
            var (onayli, taslak) = EskiSurumVeritabani(f.Yol);
            Assert.Contains("%PDF-silinmis-belge-izi-7f3a91", Encoding.ASCII.GetString(File.ReadAllBytes(f.Yol)));
            var sutunlar = Sutunlar(f.Yol, "Belgeler.Icerik", "EkstreBelgeler.Dosya");
            var once = Dokum(f.Yol, sutunlar);
            var onceRaporlar = Raporlar(f.Yol);
            Assert.Contains("48000.5", onceRaporlar["/api/rapor/aylik?yil=2026&ay=8"]);
            Assert.Contains("1500", onceRaporlar["/api/rapor/aylik?yil=2026&ay=8"]);
            var onceBelgeler = new Dictionary<long, (string Ad, string Tur, byte[] Icerik)>();
            using (var c = new SqliteConnection(Baglanti(f.Yol)))
            {
                c.Open();
                using var k = c.CreateCommand();
                k.CommandText = "SELECT Id, DosyaAdi, IcerikTuru, Icerik FROM Belgeler ORDER BY Id;";
                using var r = k.ExecuteReader();
                while (r.Read())
                    onceBelgeler[r.GetInt64(0)] = (r.GetString(1), r.GetString(2), (byte[])r.GetValue(3));
            }
            Assert.Equal(3, onceBelgeler.Count);

            _ = f.Services; // açılış: göç öncesi yedek, ekstre doğrulaması, hazırlık, aktarım, BLOB'ların düşürülmesi, VACUUM

            // Şema: BLOB sütunları yok, bütün migration'lar uygulanmış, model eşleşiyor.
            Assert.Null(Deger(f.Yol, "SELECT 1 FROM pragma_table_info('Belgeler') WHERE name = 'Icerik';"));
            Assert.Null(Deger(f.Yol, "SELECT 1 FROM pragma_table_info('EkstreBelgeler') WHERE name = 'Dosya';"));
            using (var db = f.Baglam())
            {
                Assert.Equal(24, db.Database.GetAppliedMigrations().Count());
                Assert.Empty(db.Database.GetPendingMigrations());
                Assert.False(db.Database.HasPendingModelChanges());
            }
            // Kalan bütün sütunlar, kimlikler ve sqlite_sequence birebir aynı (HTTP isteğinden önce: giriş olayı yazılmadan).
            Assert.Equal(once, Dokum(f.Yol, sutunlar));
            Assert.Equal(0L, Deger(f.Yol, "SELECT COUNT(*) FROM Belgeler WHERE Silindi <> 0 OR YukleyenRol IS NOT NULL OR SilenRol IS NOT NULL;"));

            // İçerik depoda bayt bayt aynı; aynı içerikli iki belge tek dosyayı paylaşır; silinmiş belgenin içeriği hiçbir yerde yok.
            var depo = f.Services.GetRequiredService<BelgeDeposu>();
            Assert.Equal(Path.GetFullPath(f.BelgeDizini), depo.Kok);
            foreach (var (id, (_, _, icerik)) in onceBelgeler)
            {
                var ozet = (string)Deger(f.Yol, $"SELECT IcerikOzeti FROM Belgeler WHERE Id = {id};")!;
                Assert.Equal(Ozet(icerik), ozet);
                Assert.Equal(icerik, File.ReadAllBytes(depo.Yol(ozet)));
            }
            Assert.Equal(EkstrePdf, File.ReadAllBytes(depo.Yol(Ozet(EkstrePdf))));
            Assert.Equal(new[] { Ozet(Dekont), Ozet(EkstrePdf), Ozet(Fatura) }.Order(StringComparer.Ordinal), depo.Ozetler().Order(StringComparer.Ordinal));
            Assert.DoesNotContain("%PDF-silinmis-belge-izi-7f3a91", Encoding.ASCII.GetString(File.ReadAllBytes(f.Yol)));
            Assert.Equal(0L, Deger(f.Yol, "PRAGMA freelist_count;"));

            // Göç öncesi yedek geçişten önce alındı; içerikleri (BLOB'larıyla) taşır ve bekleyen işleri adlandırır.
            var gocOncesi = Assert.Single(Directory.GetFiles(yedekDizini, YedekSaklama.GocOncesiOnEki + "*.zip"));
            var acilan = Path.Combine(yedekDizini, "goc-oncesi.db");
            using (var arsiv = ZipFile.OpenRead(gocOncesi))
            {
                using (var akis = arsiv.GetEntry("manifest.json")!.Open())
                {
                    var isler = JsonDocument.Parse(akis).RootElement.GetProperty("bekleyenIsler").EnumerateArray().Select(e => e.GetString()).ToList();
                    Assert.Contains(BelgeDeposuHazirlik.Kimlik, isler);
                    Assert.Contains(BelgeDeposuGocu.Kimlik, isler);
                    Assert.Contains(isler, i => i!.StartsWith("Belge içeriklerinin belge deposuna aktarımı: 3 alış belgesi, 1 ekstre PDF'i (", StringComparison.Ordinal));
                }
                arsiv.GetEntry("kasa.db")!.ExtractToFile(acilan);
            }
            Assert.Equal("ok", Deger(acilan, "PRAGMA integrity_check;"));
            Assert.Equal(Fatura, (byte[])Deger(acilan, "SELECT Icerik FROM Belgeler WHERE Id = 1;")!);
            Assert.Equal(EkstrePdf, (byte[])Deger(acilan, "SELECT Dosya FROM EkstreBelgeler WHERE Id = 1;")!);

            // HTTP yanıtları: raporlar eski şemadaki hesapla birebir; indirmeler eski sürümün verdiği içerik, tür ve adla.
            using var editor = await f.EditorClientAsync();
            foreach (var (uc, beklenen) in onceRaporlar)
                Assert.True(beklenen == await editor.GetStringAsync(uc), $"{uc} geçişten sonra farklı.");
            foreach (var (id, (ad, tur, icerik)) in onceBelgeler)
            {
                using var r = await editor.GetAsync($"/api/belgeler/{id}");
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                Assert.Equal(icerik, await r.Content.ReadAsByteArrayAsync());
                Assert.Equal(tur, r.Content.Headers.ContentType!.MediaType);
                Assert.Equal("attachment", r.Content.Headers.ContentDisposition!.DispositionType);
                Assert.Equal(BelgeEndpoints.GuvenliBelgeAdi(ad, tur), r.Content.Headers.ContentDisposition.FileNameStar);
                Assert.Equal(icerik.LongLength, r.Content.Headers.ContentLength);
            }
            Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync("/api/belgeler/4")).StatusCode);
            using (var r = await editor.GetAsync("/api/ekstre-aktar/1/dosya"))
            {
                Assert.Equal(EkstrePdf, await r.Content.ReadAsByteArrayAsync());
                Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
                Assert.Equal("ağustos.pdf", r.Content.Headers.ContentDisposition!.FileNameStar);
            }
            var liste = (await editor.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{onayli}/belgeler"))!;
            Assert.Equal(new[] { (1, onayli, (int?)1, "fatura.pdf", "application/pdf", (long)Fatura.Length), (2, onayli, null, "dekont.png", "image/png", Dekont.Length) },
                liste.Select(b => (b.Id, b.AlisId, b.OdemeId, b.DosyaAdi, b.IcerikTuru, b.Boyut)));
            Assert.Equal(new DateTimeOffset(2026, 8, 5, 10, 15, 0, TimeSpan.FromHours(3)), liste[0].Yuklendi);
            Assert.Equal(new[] { 3 }, (await editor.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{taslak}/belgeler"))!.Select(b => b.Id));
            var ekstre = Assert.Single((await editor.GetFromJsonAsync<JsonElement>("/api/ekstre-aktar")).EnumerateArray());
            Assert.Equal(("ağustos.pdf", "Akbank"), (ekstre.GetProperty("dosyaAdi").GetString(), ekstre.GetProperty("banka").GetString()));

            // Yeniden açılış: bekleyen iş yok, ikinci göç öncesi yedek ve yeniden aktarım yok.
            using (var db = f.Baglam())
                KasaDatabaseInitializer.Initialize(db, f.Services.GetRequiredService<YedekServisi>(), depo);
            Assert.Single(Directory.GetFiles(yedekDizini, YedekSaklama.GocOncesiOnEki + "*.zip"));
        }
        finally { f.Dispose(); Temizle(yedekDizini, yedekDizini + "-anahtar"); }
    }

    [Fact]
    public void Ekstre_icerigi_kayitli_ozetle_eslesmezse_acilis_durur_veritabani_degismez()
    {
        var yedekDizini = GeciciDizin("belge-gecis-uyusmaz");
        var f = Fabrika(yedekDizini);
        try
        {
            EskiSurumVeritabani(f.Yol, ekstreOzeti: Ozet("%PDF-başka bir ekstre"u8.ToArray()));
            var hepsi = Sutunlar(f.Yol);
            var once = Dokum(f.Yol, hepsi);

            var hata = Assert.ThrowsAny<Exception>(() => f.Services);
            var ileti = string.Join(" | ", Zincir(hata).Select(e => e.Message));
            Assert.Contains("kayıtlı özetle eşleşmiyor", ileti);
            Assert.Contains("veritabanı değiştirilmedi", ileti);

            // Hiçbir migration uygulanmadı, BLOB'lar yerinde, bütün satırlar (içerikler dahil) aynı.
            Assert.Equal(once, Dokum(f.Yol, hepsi));
            using var db = Baglam(f.Yol);
            Assert.Equal(new[] { BelgeDeposuHazirlik.Kimlik, BelgeDeposuGocu.Kimlik, EkstreEslesmesi.Kimlik, KasaKontrolFiligrani.Kimlik, CekirdekSurumleri.Kimlik, GeriYuklemeGuvenligi.Kimlik, EditorSifirlamaIzi.Kimlik }, db.Database.GetPendingMigrations());
            Assert.NotNull(Deger(f.Yol, "SELECT 1 FROM pragma_table_info('Belgeler') WHERE name = 'Icerik';"));
            Assert.Null(Deger(f.Yol, "SELECT 1 FROM pragma_table_info('Belgeler') WHERE name = 'IcerikOzeti';"));
        }
        finally { try { f.Dispose(); } catch (Exception) { /* açılmamış fabrika */ } Temizle(yedekDizini, yedekDizini + "-anahtar"); }
    }

    [Fact]
    public void Yarida_kesilen_aktarim_yeniden_baslatmada_kaldigi_yerden_tamamlanir()
    {
        var yol = Path.Combine(Path.GetTempPath(), "kasa-belge-yarida-" + Guid.NewGuid().ToString("N") + ".db");
        var yedekDizini = GeciciDizin("belge-yarida-yedek");
        var depo = new BelgeDeposu(GeciciDizin("belge-yarida-depo"));
        try
        {
            EskiSurumVeritabani(yol);
            // Hazırlık uygulanmış, ilk belge aktarılmışken süreç düştü (disk doldu).
            using (var db = Baglam(yol))
            {
                db.GetService<IMigrator>().Migrate(BelgeDeposuHazirlik.Kimlik);
                var c = (SqliteConnection)db.Database.GetDbConnection();
                c.Open();
                var ex = Assert.Throws<IOException>(() => BelgeDeposuAktarimi.BelgeleriAktar(c, depo, NullLogger.Instance, id => throw new IOException("Disk doldu (benzetim).")));
                Assert.Equal("Disk doldu (benzetim).", ex.Message);
            }
            Assert.Equal(Ozet(Fatura), Deger(yol, "SELECT IcerikOzeti FROM Belgeler WHERE Id = 1;"));
            Assert.Equal(2L, Deger(yol, "SELECT COUNT(*) FROM Belgeler WHERE IcerikOzeti IS NULL;"));

            using (var db = Baglam(yol))
                KasaDatabaseInitializer.Initialize(db, GocOncesiYedekTests.TestYedegi(yedekDizini), depo);

            using (var db = Baglam(yol))
                Assert.Empty(db.Database.GetPendingMigrations());
            Assert.Equal(new[] { Ozet(Fatura), Ozet(Dekont), Ozet(Fatura) }, new[] { 1, 2, 3 }.Select(id => (string)Deger(yol, $"SELECT IcerikOzeti FROM Belgeler WHERE Id = {id};")!));
            Assert.Equal(Fatura, File.ReadAllBytes(depo.Yol(Ozet(Fatura))));
            Assert.Equal(Dekont, File.ReadAllBytes(depo.Yol(Ozet(Dekont))));
            Assert.Equal(EkstrePdf, File.ReadAllBytes(depo.Yol(Ozet(EkstrePdf))));
            // Kesintiden sonraki açılış da (BLOB'lar düşmeden önce) göç öncesi yedeğini aldı.
            Assert.Single(Directory.GetFiles(yedekDizini, YedekSaklama.GocOncesiOnEki + "*.zip"));
            // Tetikleyici: özetsiz belge eklenemez.
            var tetik = Assert.Throws<SqliteException>(() => Deger(yol, "INSERT INTO Belgeler (AlisId, DosyaAdi, IcerikTuru, Boyut, Yuklendi, Silindi) VALUES (1, 'x.pdf', 'application/pdf', 1, '2026-09-25', 0);"));
            Assert.Contains("ozeti zorunlu", tetik.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var ek in new[] { "", "-wal", "-shm", "-journal" })
                try
                { File.Delete(yol + ek); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Temizle(yedekDizini, depo.Kok);
        }
    }

    [Fact]
    public void Depo_ayni_icerigi_bir_kez_saklar_bozugu_onarir_ve_yalniz_eski_sahipsiz_dosyalari_temizler()
    {
        var saat = new SabitSaat(new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero));
        var depo = new BelgeDeposu(GeciciDizin("belge-depo"), saat: saat);
        try
        {
            var a = depo.Yaz(Fatura);
            var b = depo.Yaz(Fatura);
            Assert.Equal(a, b);
            Assert.Equal(Ozet(Fatura), a.Ozet);
            Assert.Equal(Fatura.LongLength, a.Boyut);
            Assert.Single(depo.Ozetler());
            Assert.Empty(Directory.GetFiles(depo.Kok)); // geçici dosya kalmaz
            using (var akis = depo.Ac(a.Ozet.ToLowerInvariant()))
            { using var m = new MemoryStream(); akis.CopyTo(m); Assert.Equal(Fatura, m.ToArray()); }
            Assert.Throws<BelgeDosyasiYokException>(() => depo.Ac(Ozet(Dekont)));
            Assert.Throws<BelgeDosyasiYokException>(() => depo.Ac("../../kasa.db"));

            // Bozulmuş dosya aynı içeriğin yeniden yazımında doğrulanmış içerikle değiştirilir.
            File.WriteAllBytes(depo.Yol(a.Ozet), [1, 2, 3]);
            Assert.False(depo.Dogrula(a.Ozet));
            depo.Yaz(Fatura);
            Assert.True(depo.Dogrula(a.Ozet));

            // Bakım: yalnız hiçbir kaydın göstermediği ve 24 saatten eski dosya silinir; yeniden yazım yaşı tazeler.
            var sahipsiz = depo.Yaz(Dekont).Ozet;
            saat.Ayarla(new DateOnly(2026, 9, 27));
            Assert.Equal(0, depo.Temizle(new HashSet<string> { a.Ozet, sahipsiz }, TimeSpan.FromHours(24)));
            depo.Yaz(Dekont); // ~2 gün sonra aynı içerik yeniden yüklendi: yaş tazelenir
            File.SetLastWriteTimeUtc(depo.Yol(a.Ozet), new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(0, depo.Temizle(new HashSet<string> { a.Ozet }, TimeSpan.FromHours(24)));
            File.SetLastWriteTimeUtc(depo.Yol(sahipsiz), new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
            Assert.Equal(1, depo.Temizle(new HashSet<string> { a.Ozet }, TimeSpan.FromHours(24)));
            Assert.Equal(new[] { a.Ozet }, depo.Ozetler());
        }
        finally { Temizle(depo.Kok); }
    }

    [Fact]
    public async Task Yuklenen_belge_ve_ekstre_depoya_yazilir_indirme_bayt_bayt_ayni_dosya_yoksa_acik_404()
    {
        await using var f = KasaWebFactory.Sabit(KasaWebFactory.VarsayilanBugun);
        using var c = await f.EditorClientAsync();
        var alis = (await (await c.PostAsJsonAsync("/api/alis", new AlisYaz(0, f.Bugun, "Firma", null, []))).Content.ReadFromJsonAsync<AlisDto>())!;
        async Task<BelgeDto> Yukle(byte[] icerik, string ad)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(icerik), "dosya", ad);
            var r = await c.PostAsync($"/api/alis/{alis.Id}/belgeler", form);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<BelgeDto>())!;
        }
        var bir = await Yukle(Fatura, "fatura.pdf");
        var iki = await Yukle(Fatura, "fatura-yine.pdf");
        var depo = f.Services.GetRequiredService<BelgeDeposu>();
        Assert.Single(depo.Ozetler());
        foreach (var belge in new[] { bir, iki })
            Assert.Equal(Fatura, await c.GetByteArrayAsync($"/api/belgeler/{belge.Id}"));
        using (var scope = f.Services.CreateScope())
            Assert.Equal(new[] { Ozet(Fatura), Ozet(Fatura) }, scope.ServiceProvider.GetRequiredService<KasaDbContext>().Belgeler.OrderBy(b => b.Id).Select(b => b.IcerikOzeti).ToArray());

        // Dosya depodan kaybolmuşsa (geri yüklemede unutulan belgeler/ gibi) istemci açık bir 404 alır, sunucu kaydı düşer.
        File.Delete(depo.Yol(Ozet(Fatura)));
        using var yok = await c.GetAsync($"/api/belgeler/{bir.Id}");
        Assert.Equal(HttpStatusCode.NotFound, yok.StatusCode);
        Assert.Equal(BelgeEndpoints.DosyaYok, (await yok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString());
    }

    [Fact]
    public async Task Depoda_olmayan_belge_icerigi_her_acilista_hata_olarak_loglanir()
    {
        await using var f = KasaWebFactory.Sabit(KasaWebFactory.VarsayilanBugun);
        using var c = await f.EditorClientAsync();
        var alis = (await (await c.PostAsJsonAsync("/api/alis", new AlisYaz(0, f.Bugun, "Firma", null, []))).Content.ReadFromJsonAsync<AlisDto>())!;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Belgeler.Add(new BelgeEntity { AlisId = alis.Id, DosyaAdi = "kayip.pdf", IcerikTuru = "application/pdf", Boyut = 3, Yuklendi = f.Saat!.GetUtcNow(), IcerikOzeti = Ozet("%PDF-kayip"u8.ToArray()) });
            db.SaveChanges();
        }
        var loglar = new HataToplayici();
        await using var yeniden = f.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(loglar)));
        _ = yeniden.Services; // aynı veritabanıyla yeniden açılış
        Assert.Contains(loglar.Hatalar, h => h.StartsWith("1 belge içeriği belge deposunda", StringComparison.Ordinal) && h.Contains("--belge-aynasi"));
    }

    private sealed class HataToplayici : Microsoft.Extensions.Logging.ILoggerProvider, Microsoft.Extensions.Logging.ILogger
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Hatalar { get; } = new();
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel >= Microsoft.Extensions.Logging.LogLevel.Error;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Error) Hatalar.Enqueue(formatter(state, exception)); }
        public void Dispose() { }
    }

    private static IEnumerable<Exception> Zincir(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException)
        {
            yield return x;
            if (x is AggregateException a)
                foreach (var ic in a.InnerExceptions.SelectMany(Zincir))
                    yield return ic;
        }
    }
}
