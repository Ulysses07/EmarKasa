using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>2026-09 incelemesindeki API bulgularının kilit testleri.</summary>
public class BulguDuzeltmeApiTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public BulguDuzeltmeApiTests(KasaWebFactory factory) => _factory = factory;

    private record KanalYanit(int Id, string Ad, bool Aktif, int Sira, decimal AcilisDevri);
    private record IslemYanit(int Id, string Kanal);
    private record GelenYanit(int Id, DateOnly DonemStart, string Kanal, decimal TutarTl);

    private async Task<KanalYanit> KanalEkle(HttpClient c, string ad)
    {
        var r = await c.PostAsJsonAsync("/api/kanallar", new { ad, aktif = true, sira = 9, acilisDevri = 0m });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<KanalYanit>())!;
    }

    [Fact]
    public async Task Kanal_yeniden_adlandirilinca_islem_ve_gelenler_tasinir()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1));
        var k = await KanalEkle(c, "ESKI-AD");
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-06-10", cari = "X", tutarTl = 50m, kanal = "ESKI-AD", tip = "Cari" })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-08", kanal = "ESKI-AD", tutarTl = 100m })).EnsureSuccessStatusCode();

        var r = await c.PutAsJsonAsync($"/api/kanallar/{k.Id}", new { ad = "YENI-AD", aktif = true, sira = 9, acilisDevri = 0m });
        r.EnsureSuccessStatusCode();

        var islemler = await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler?kanal=YENI-AD");
        var gelenler = await c.GetFromJsonAsync<List<GelenYanit>>("/api/gelenler");
        Assert.Single(islemler!);
        Assert.Contains(gelenler!, g => g.Kanal == "YENI-AD" && g.TutarTl == 100m);
        Assert.DoesNotContain(gelenler!, g => g.Kanal == "ESKI-AD");
    }

    [Fact]
    public async Task Gecmisi_olan_kanal_silinemez_409()
    {
        var c = await _factory.EditorClientAsync();
        var k = await KanalEkle(c, "SILINMEZ");
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-06-10", cari = "X", tutarTl = 5m, kanal = "SILINMEZ", tip = "Cari" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/kanallar/{k.Id}")).StatusCode);

        var bos = await KanalEkle(c, "BOS-KANAL");
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kanallar/{bos.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(-5, "MEZAT", null)]
    [InlineData(0, "MEZAT", null)]
    [InlineData(10, "YOK-BOYLE-KANAL", null)]
    [InlineData(10, "MEZAT", 99999)]
    public async Task Gecersiz_islem_400_doner(decimal tutar, string kanal, int? kartId)
    {
        var c = await _factory.EditorClientAsync();
        var r = await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-06-10", cari = "X", tutarTl = tutar, kanal, tip = "Cari", krediKartiId = kartId });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Ayni_adla_ikinci_kanal_ve_Ortak_adi_reddedilir()
    {
        var c = await _factory.EditorClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/kanallar", new { ad = "MEZAT" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/kanallar", new { ad = "Ortak" })).StatusCode);
    }

    [Fact]
    public async Task Izleyici_sifresi_degisince_eski_izleyici_oturumu_kapanir()
    {
        var editor = await _factory.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "ilk-sifre" })).EnsureSuccessStatusCode();

        var izleyici = _factory.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { sifre = "ilk-sifre" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/kanallar")).StatusCode);

        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "ikinci-sifre" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await izleyici.GetAsync("/api/kanallar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/kanallar")).StatusCode); // editör etkilenmez
    }

    [Fact]
    public async Task Oturumlari_kapat_editor_tokenini_de_gecersiz_kilar()
    {
        var editor = await _factory.EditorClientAsync();
        (await editor.PostAsync("/api/ayarlar/oturumlari-kapat", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await editor.GetAsync("/api/kanallar")).StatusCode);

        var yeni = await _factory.EditorClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await yeni.GetAsync("/api/kanallar")).StatusCode);
    }

    [Fact]
    public async Task Takip_baslangici_degisince_gelenler_yeni_doneme_hizalanir()
    {
        var c = await _factory.EditorClientAsync();
        await KanalEkle(c, "HIZA");
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2025-03-05", kasaAcilisDevri = 0m })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2025-03-05", kanal = "HIZA", tutarTl = 700m })).EnsureSuccessStatusCode();
        // Takip başlangıcından önceki (eski sürümden kalma) bir gelen satırı: API artık takvim
        // dışına yazdırmaz, bu yüzden doğrudan DB'ye eklenir.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Gelenler.Add(new GelenEntity { DonemStart = new DateOnly(2025, 3, 3), Kanal = "HIZA", TutarTl = 300m });
            db.SaveChanges();
        }

        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2025-03-03", kasaAcilisDevri = 0m })).EnsureSuccessStatusCode();

        var gelenler = (await c.GetFromJsonAsync<List<GelenYanit>>("/api/gelenler"))!.Where(g => g.Kanal == "HIZA").ToList();
        var tek = Assert.Single(gelenler);
        Assert.Equal(new DateOnly(2025, 3, 3), tek.DonemStart);
        Assert.Equal(1_000m, tek.TutarTl);
    }
}

public class GirisSiniriTests : IClassFixture<GirisSiniriTests.DusukLimitFactory>
{
    public class DusukLimitFactory : KasaWebFactory { protected override int GirisLimiti => 3; }

    private readonly DusukLimitFactory _factory;
    public GirisSiniriTests(DusukLimitFactory factory) => _factory = factory;

    [Fact]
    public async Task Cok_fazla_giris_denemesi_429_doner()
    {
        var c = _factory.CreateClient();
        var kodlar = new List<HttpStatusCode>();
        for (int i = 0; i < 5; i++)
            kodlar.Add((await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "yanlis" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, kodlar[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, kodlar[^1]);
    }
}

public class SemaVeYedekTests
{
    [Fact]
    public void Eski_semali_db_guncel_modele_getirilir()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        // Kredi kartı özelliklerinden önceki şema: KartOdemeler yok, KrediKartiId ve oturum sürümleri yok.
        foreach (var sql in new[]
        {
            "CREATE TABLE \"Ayarlar\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, \"TakipBaslangic\" TEXT NOT NULL, \"KasaAcilisDevri\" TEXT NOT NULL, \"IzleyiciSifreHash\" TEXT NULL)",
            "CREATE TABLE \"Islemler\" (\"Id\" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, \"Tarih\" TEXT NOT NULL, \"Cari\" TEXT NOT NULL, \"TutarTl\" TEXT NOT NULL, \"Kanal\" TEXT NOT NULL, \"Tip\" INTEGER NOT NULL, \"Not\" TEXT NULL)",
            "INSERT INTO \"Ayarlar\" (\"TakipBaslangic\",\"KasaAcilisDevri\") VALUES ('2026-06-01','0.0')",
            "INSERT INTO \"Islemler\" (\"Tarih\",\"Cari\",\"TutarTl\",\"Kanal\",\"Tip\") VALUES ('2026-06-02','A','10.0','MEZAT',0)",
        })
        {
            using var cmd = conn.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
        }

        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        var yapilan = SemaGuncelleyici.Guncelle(db);

        Assert.Contains("tablo+ KartOdemeler", yapilan);
        Assert.Contains("tablo+ KrediKartlari", yapilan);
        Assert.Contains("sütun+ Islemler.KrediKartiId", yapilan);
        Assert.Contains("sütun+ Ayarlar.IzleyiciOturumSurumu", yapilan);
        Assert.Equal(1, db.Islemler.Count());                       // veri korunur
        Assert.Equal(0, db.Ayarlar.Single().EditorOturumSurumu);
        Assert.Empty(SemaGuncelleyici.Guncelle(db));                // ikinci çalıştırma bir şey yapmaz
    }

    [Fact]
    public void Yedek_alinir_ve_eskiler_temizlenir()
    {
        var klasor = Path.Combine(Path.GetTempPath(), "kasa-yedek-" + Guid.NewGuid().ToString("N"));
        var dbDosya = Path.Combine(Path.GetTempPath(), "kasa-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite($"Data Source={dbDosya}").Options);
            db.Database.EnsureCreated();
            db.Cariler.Add(new CariEntity { Ad = "Yedeklenen" }); db.SaveChanges();

            for (int g = 1; g <= 4; g++)
                YedekServisi.YedekAl(db, klasor, new DateOnly(2026, 9, g), sakla: 2);

            var dosyalar = Directory.GetFiles(klasor).Select(Path.GetFileName).OrderBy(f => f).ToList();
            Assert.Equal(["kasa-2026-09-03.db", "kasa-2026-09-04.db"], dosyalar);

            using var yedek = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>()
                .UseSqlite($"Data Source={Path.Combine(klasor, "kasa-2026-09-04.db")}").Options);
            Assert.Equal("Yedeklenen", yedek.Cariler.Single().Ad);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(klasor)) Directory.Delete(klasor, true);
            if (File.Exists(dbDosya)) File.Delete(dbDosya);
        }
    }
}

public class KasaKartOdemeTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KasaKartOdemeTests(KasaWebFactory factory) => _factory = factory;

    private record PanelYanit(decimal GuncelKasa);
    private record KartYanit(int Id);

    [Fact]
    public async Task Panel_kasasi_karta_bagli_harcamayi_degil_kart_odemesini_duser()
    {
        var c = await _factory.EditorClientAsync();
        var bugun = Saat.Bugun();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = bugun.AddDays(-40).ToString("yyyy-MM-dd"), kasaAcilisDevri = 10_000m })).EnsureSuccessStatusCode();
        var kart = (await (await c.PostAsJsonAsync("/api/kredikartlari", new
            { ad = "Bonus", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25", limit = 50_000m, borc = 0m }))
            .Content.ReadFromJsonAsync<KartYanit>())!;

        (await c.PostAsJsonAsync("/api/islemler", new { tarih = bugun.AddDays(-35).ToString("yyyy-MM-dd"), cari = "Market", tutarTl = 3_000m, kanal = "MEZAT", tip = "KrediKarti", krediKartiId = kart.Id })).EnsureSuccessStatusCode();
        Assert.Equal(10_000m, (await c.GetFromJsonAsync<PanelYanit>("/api/rapor/panel"))!.GuncelKasa);

        (await c.PostAsJsonAsync("/api/kartodemeler", new { krediKartiId = kart.Id, tarih = bugun.AddDays(-1).ToString("yyyy-MM-dd"), tutar = 1_200m })).EnsureSuccessStatusCode();
        Assert.Equal(8_800m, (await c.GetFromJsonAsync<PanelYanit>("/api/rapor/panel"))!.GuncelKasa);
    }
}
