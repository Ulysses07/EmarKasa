using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Kilitli ay rapor anlık görüntüsü (K4: rapor kuralı değişiklikleri kapatılmış ayın raporunu değiştirmez). Takip başlangıcı
/// Haziran 2026 başı, bugün 25 Eylül 2026 (sabit saat).
/// </summary>
public class AyRaporuAnlikGoruntusuTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static DateOnly Haziran => Month.AddMonths(-3);
    private static DateOnly Temmuz => Month.AddMonths(-2);
    private static DateOnly Agustos => Month.AddMonths(-1);
    private static string Url(DateOnly ay) => $"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}";

    /// <summary>Kilitli ayın beklenen yanıtı: kapatılmadan hemen önceki rapor + kural sürümü + dondurulmuş işareti.</summary>
    internal static string Dondurulmus(string canli, int kural)
    {
        var rapor = JsonNode.Parse(canli)!.AsObject();
        rapor["kuralSurumu"] = kural;
        rapor["dondurulmus"] = true;
        return rapor.ToJsonString();
    }

    private static async Task Veri(HttpClient c)
    {
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Haziran, "MEZAT", 1_000m))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Temmuz, "PERAKENDE", 2_500.50m))).EnsureSuccessStatusCode();
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Temmuz.AddDays(4), "Tedarik", 300m, "MEZAT", GiderTipi.Cari));
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Agustos.AddDays(9), "Kira", 900.01m, KanalEtiketleri.Ortak, GiderTipi.SabitGider));
    }

    private static async Task<AyKilidiDto> Kilit(HttpClient c, DateOnly ay, bool ac = false)
    {
        var state = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        return await Post<AyKilidiDto>(c, ac ? "/api/ay-kilidi/ac" : "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), state.Surum, ay.Year, ay.Month, ac ? "Düzeltme için açıldı" : "Ay tamamlandı"));
    }

    /// <summary>Ham SQL (EF'in {0} biçimlendirmesi olmadan; JSON metni süslü parantez içerir).</summary>
    private static void Calistir(KasaDbContext db, string sql)
    {
        var baglanti = db.Database.GetDbConnection();
        if (baglanti.State != System.Data.ConnectionState.Open)
            db.Database.OpenConnection();
        using var komut = baglanti.CreateCommand();
        komut.CommandText = sql;
        komut.ExecuteNonQuery();
    }

    private static List<AyRaporAnlikGoruntuEntity> Goruntuler(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().AyRaporAnlikGoruntuleri.AsNoTracking().OrderBy(g => g.Yil).ThenBy(g => g.Ay).ToList();
    }

    [Fact]
    public async Task Ay_kapatilinca_ara_aylar_dahil_rapor_kapatma_anindaki_haliyle_saklanir_ve_ondan_doner()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Veri(c);
        var once = new Dictionary<DateOnly, string>();
        foreach (var ay in new[] { Haziran, Temmuz, Agustos, Month })
            once[ay] = await c.GetStringAsync(Url(ay));

        await Kilit(c, Agustos); // Haziran–Ağustos birlikte kilitlenir

        var goruntuler = Goruntuler(f);
        Assert.Equal(new[] { (2026, 6), (2026, 7), (2026, 8) }, goruntuler.Select(g => (g.Yil, g.Ay)));
        Assert.All(goruntuler, g => Assert.Equal(HesapServisi.AcikAyKurali, g.KuralSurumu));
        Assert.All(goruntuler, g => Assert.Equal(f.Saat!.GetUtcNow(), g.Zaman));
        foreach (var ay in new[] { Haziran, Temmuz, Agustos })
            Assert.Equal(Dondurulmus(once[ay], HesapServisi.AcikAyKurali), await c.GetStringAsync(Url(ay)));
        // Açık ay canlıdır, işaret taşımaz.
        Assert.Equal(once[Month], await c.GetStringAsync(Url(Month)));
        Assert.DoesNotContain("dondurulmus", once[Month]);
    }

    [Fact]
    public async Task Kilit_acilinca_acilan_aylarin_goruntusu_silinir_ve_rapor_yeniden_canli_hesaplanir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Veri(c);
        await Kilit(c, Agustos);
        var kilit = await Kilit(c, Temmuz, ac: true); // Temmuz ve sonrası açılır, Haziran kilitli kalır
        Assert.Equal(Temmuz.AddDays(-1), kilit.KilitliSonTarih);
        Assert.Equal(new[] { (2026, 6) }, Goruntuler(f).Select(g => (g.Yil, g.Ay)));

        // Açılan ayda değişiklik yapılabilir ve rapora canlı yansır.
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Temmuz.AddDays(20), "Nakliye", 50m, "MEZAT", GiderTipi.Cari));
        var temmuz = await c.GetStringAsync(Url(Temmuz));
        Assert.DoesNotContain("dondurulmus", temmuz);
        using (var scope = f.Services.CreateScope())
            Assert.Equal(JsonSerializer.Serialize(scope.ServiceProvider.GetRequiredService<HesapServisi>().Aylik(Temmuz.Year, Temmuz.Month), Web), temmuz);
        Assert.Equal(350m, JsonNode.Parse(temmuz)!["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == "MEZAT")!["cariGiden"]!.GetValue<decimal>());
        Assert.Contains("\"dondurulmus\":true", await c.GetStringAsync(Url(Haziran)));

        // Bütün kilit açılınca hiç görüntü kalmaz; yeniden kapatınca güncel veriyle yeniden yazılır.
        await Kilit(c, Haziran, ac: true);
        Assert.Empty(Goruntuler(f));
        var yeniTemmuz = await c.GetStringAsync(Url(Temmuz));
        await Kilit(c, Temmuz);
        Assert.Equal(Dondurulmus(yeniTemmuz, HesapServisi.AcikAyKurali), await c.GetStringAsync(Url(Temmuz)));
    }

    /// <summary>R3 notu: görüntü yalnız ay gerçekten kilitliyken döner. Görüntüyü silmeyen bir önceki sürüme dönülüp ay orada
    /// açılırsa satır artık kalır; açık ayın raporu yine canlı hesaplanır (bayat "dondurulmus" sunulmaz) ve artık satır ay
    /// başına bir kez Warning olarak loglanır. Kilitli ayın görüntüsü etkilenmez.</summary>
    [Fact]
    public async Task Kilidi_acilmis_ayin_artik_goruntusu_sunulmaz_rapor_canli_hesaplanir_ve_bir_kez_loglanir()
    {
        var loglar = new UyariToplayici();
        await using var f = new LogluFabrika(loglar);
        using var c = await Editor(f);
        await Veri(c);
        await Kilit(c, Agustos);
        var haziran = await c.GetStringAsync(Url(Haziran));
        using (var scope = f.Services.CreateScope())
        {
            // Eski sürümdeki kilit açma: kilit sonu Haziran'a çekilir, Temmuz ve Ağustos görüntüleri silinmez.
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Database.ExecuteSqlRaw("UPDATE AyKilidi SET KilitliSonTarih = '2026-06-30', Surum = Surum + 1 WHERE Id = 1;");
        }
        Assert.Equal(3, Goruntuler(f).Count);

        // Açılan ayda değişiklik yapılabilir ve rapora canlı yansır.
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Temmuz.AddDays(20), "Nakliye", 50m, "MEZAT", GiderTipi.Cari));
        foreach (var _ in Enumerable.Range(0, 2))
            foreach (var ay in new[] { Temmuz, Agustos })
            {
                var rapor = await c.GetStringAsync(Url(ay));
                Assert.DoesNotContain("dondurulmus", rapor);
                using var scope = f.Services.CreateScope();
                Assert.Equal(JsonSerializer.Serialize(scope.ServiceProvider.GetRequiredService<HesapServisi>().Aylik(ay.Year, ay.Month), Web), rapor);
            }
        Assert.Equal(350m, JsonNode.Parse(await c.GetStringAsync(Url(Temmuz)))!["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == "MEZAT")!["cariGiden"]!.GetValue<decimal>());
        Assert.Equal(haziran, await c.GetStringAsync(Url(Haziran)));
        Assert.Contains("\"dondurulmus\":true", haziran);

        Assert.Single(loglar.Uyarilar, m => m.Contains("2026-07 ayının rapor görüntüsü", StringComparison.Ordinal));
        Assert.Single(loglar.Uyarilar, m => m.Contains("2026-08 ayının rapor görüntüsü", StringComparison.Ordinal));
        Assert.DoesNotContain(loglar.Uyarilar, m => m.Contains("2026-06 ayının", StringComparison.Ordinal));
    }

    private sealed class LogluFabrika : KasaWebFactory
    {
        private readonly UyariToplayici _loglar;
        public LogluFabrika(UyariToplayici loglar) { _loglar = loglar; Saat = new SabitSaat(Today); }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(l => l.AddProvider(_loglar));
        }
    }

    [Fact]
    public async Task Kilitli_ayin_goruntusu_degistirilemez_silinemez_kilitsiz_aya_goruntu_yazilamaz()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Veri(c);
        await Kilit(c, Agustos);
        var haziran = await c.GetStringAsync(Url(Haziran));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();

        // Uygulama katmanı (kilit kuralları).
        var satir = db.AyRaporAnlikGoruntuleri.Single(g => g.Yil == 2026 && g.Ay == 6);
        satir.Json = satir.Json.Replace("1000", "9999", StringComparison.Ordinal);
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        db.AyRaporAnlikGoruntuleri.Remove(db.AyRaporAnlikGoruntuleri.Single(g => g.Yil == 2026 && g.Ay == 6));
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        db.AyRaporAnlikGoruntuleri.Add(new() { Yil = 2026, Ay = 9, KuralSurumu = AylikKural.V1, Json = "{}", Zaman = DateTimeOffset.UnixEpoch });
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();

        // Veritabanı katmanı (ham SQL tetikleyicileri): iletisi API'de kilit hatasına çevrilir.
        foreach (var sql in new[]
        {
            "UPDATE AyRaporAnlikGoruntuleri SET Json = '{}' WHERE Yil = 2026 AND Ay = 6;",
            "UPDATE AyRaporAnlikGoruntuleri SET KuralSurumu = 2;",
            "DELETE FROM AyRaporAnlikGoruntuleri WHERE Yil = 2026 AND Ay = 8;",
            "INSERT INTO AyRaporAnlikGoruntuleri (Yil, Ay, KuralSurumu, Json, Zaman) VALUES (2026, 9, 1, '{}', '2026-09-25');",
        })
        {
            var hata = Assert.Throws<SqliteException>(() => Calistir(db, sql));
            Assert.Contains("Kilitli ay", hata.Message);
        }
        Assert.Equal(haziran, await c.GetStringAsync(Url(Haziran)));
        Assert.Equal(3, Goruntuler(f).Count);
    }

    [Fact]
    public async Task Gecis_oncesi_kilitlenmis_aylar_kural_1_ile_bir_kez_dondurulur_ve_tohum_idempotenttir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Veri(c);
        var kural1 = new Dictionary<DateOnly, string>();
        using (var scope = f.Services.CreateScope())
        {
            var hesap = scope.ServiceProvider.GetRequiredService<HesapServisi>();
            foreach (var ay in new[] { Haziran, Temmuz })
                kural1[ay] = JsonSerializer.Serialize(hesap.Aylik(ay.Year, ay.Month, kuralSurumu: AylikKural.V1), Web);
        }
        // Bu sürümden önce Temmuz'a kadar kapatılmış veritabanı: kilit var, görüntü yok.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.AyKilidi.Single().KilitliSonTarih = Agustos.AddDays(-1);
            db.SaveChanges();
            Assert.Equal(new[] { (2026, 6), (2026, 7) }, AyRaporAnlikGoruntusu.EksikAylar(db));
            var ilk = AyRaporAnlikGoruntusu.GecisTohumu(db, f.Saat!.GetUtcNow());
            Assert.Equal(new[] { (2026, 6), (2026, 7) }, ilk);
            // İkinci açılış: bekleyen iş yok, satırlar değişmez.
            Assert.Empty(AyRaporAnlikGoruntusu.GecisTohumu(db, f.Saat.GetUtcNow().AddDays(1)));
            Assert.Empty(AyRaporAnlikGoruntusu.EksikAylar(db));
        }
        var goruntuler = Goruntuler(f);
        Assert.Equal(2, goruntuler.Count);
        Assert.All(goruntuler, g => { Assert.Equal(AylikKural.V1, g.KuralSurumu); Assert.Equal(f.Saat!.GetUtcNow(), g.Zaman); });
        foreach (var ay in new[] { Haziran, Temmuz })
            Assert.Equal(Dondurulmus(kural1[ay], AylikKural.V1), await c.GetStringAsync(Url(ay)));
    }

    /// <summary>Geçiş tohumu bozuk kayıtla hesaplanan raporu dondurmaz: karantina kaydının dokunduğu kilitli ay atlanır ve ayı ve
    /// kaydı söyleyen Warning olarak loglanır; öteki aylar dondurulur, atlanan ay bekleyen tohumda kalır (sonraki açılış yeniden
    /// dener). Görüntüsü olmayan kilitli ay yalnız bu sürümden önce kilitlenmiş olabilir: raporu kural 1 ile canlı hesaplanır ve
    /// karantina uyarısını taşır; dondurulmuş işareti yoktur.</summary>
    [Fact]
    public async Task Gecis_tohumu_karantinali_ayi_dondurmaz_ay_kural_1_ile_canli_ve_uyariyla_doner()
    {
        var loglar = new UyariToplayici();
        await using var f = new LogluFabrika(loglar);
        using var c = await Editor(f);
        await Veri(c);
        int hareket;
        using (var scope = f.Services.CreateScope())
        {
            // Temmuz'da kanalı olmayan eski ek gelir (geri yüklenmiş veri): karantinaya alınır, genel kasaya gelir yazılır.
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var hesap = new HesapEntity { Ad = "Eski hesap", Tur = "Kasa", AcilisTarihi = Haziran };
            db.Hesaplar.Add(hesap);
            db.SaveChanges();
            var h = new HesapHareketEntity { HesapId = hesap.Id, KanalId = null, Tarih = Temmuz.AddDays(13), Tutar = 450m, Aciklama = "Kanalsız eski ek gelir" };
            db.HesapHareketler.Add(h);
            db.SaveChanges();
            hareket = h.Id;
        }
        var kural1 = new Dictionary<DateOnly, string>();
        using (var scope = f.Services.CreateScope())
        {
            var hesap = scope.ServiceProvider.GetRequiredService<HesapServisi>();
            foreach (var ay in new[] { Haziran, Temmuz })
                kural1[ay] = JsonSerializer.Serialize(hesap.Aylik(ay.Year, ay.Month, kuralSurumu: AylikKural.V1), Web);
        }
        Assert.Contains($"Ek gelir #{hareket} (14.07.2026): kanalı yok", (string)JsonNode.Parse(kural1[Temmuz])!["veriSagligiUyarisi"]!);
        Assert.DoesNotContain("veriSagligiUyarisi", kural1[Haziran]);

        // Bu sürümden önce Temmuz'a kadar kapatılmış veritabanı: kilit var, görüntü yok.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.AyKilidi.Single().KilitliSonTarih = Agustos.AddDays(-1);
            db.SaveChanges();
            Assert.Equal(new[] { (2026, 6) }, AyRaporAnlikGoruntusu.GecisTohumu(db, f.Saat!.GetUtcNow()));
            Assert.Equal(new[] { (2026, 7) }, AyRaporAnlikGoruntusu.EksikAylar(db));
        }
        Assert.Equal(new[] { (2026, 6) }, Goruntuler(f).Select(g => (g.Yil, g.Ay)));
        Assert.Equal(Dondurulmus(kural1[Haziran], AylikKural.V1), await c.GetStringAsync(Url(Haziran)));
        // Kilitli ama görüntüsüz Temmuz: kural 1 ile canlı, karantina uyarısıyla (açık ay kuralına geçmez). Uyarı Türkçe harf
        // taşıdığından metin değil JSON ağacı karşılaştırılır (API ASCII dışı harfi kaçışsız yazar).
        var temmuz = JsonNode.Parse(await c.GetStringAsync(Url(Temmuz)))!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(kural1[Temmuz]), temmuz), temmuz.ToJsonString());
        Assert.Null(temmuz["kuralSurumu"]); // kural 1 (eski biçim); açık ay kuralı kuralSurumu yazardı
        Assert.Null(temmuz["dondurulmus"]);
        Assert.Single(loglar.Uyarilar, m => m.Contains("2026-07 ayının raporu geçiş tohumunda dondurulmadı", StringComparison.Ordinal)
            && m.Contains($"Ek gelir #{hareket} (14.07.2026)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Kural_degisince_kilitli_ay_ayni_kalir_acik_ay_yeni_kuralla_hesaplanir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        // Temmuz ve Eylül'de aynı yapı: satış 80.000, takipli kredi çekimi 120.000, cari gider 100.000 (MEZAT).
        foreach (var ay in new[] { Temmuz, Month })
        {
            // Taksitler Kasım'da başlar: Ağustos ve Eylül'de yalnız çekim etkisi görünür.
            await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), $"Kredi {ay:MM}", 120_000m, ay.AddDays(9), new DateOnly(2026, 11, 10), 12, 11_000m, [1]));
            (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(ay, "MEZAT", 80_000m))).EnsureSuccessStatusCode();
            await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(ay.AddDays(14), "Tedarik", 100_000m, "MEZAT", GiderTipi.Cari));
        }
        string kural1Temmuz;
        using (var scope = f.Services.CreateScope())
            kural1Temmuz = JsonSerializer.Serialize(scope.ServiceProvider.GetRequiredService<HesapServisi>().Aylik(Temmuz.Year, Temmuz.Month, kuralSurumu: AylikKural.V1), Web);
        // Temmuz bu sürümden (K2'den) önce kapatılmıştı: açılıştaki geçiş tohumu onu kural 1 ile dondurur.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.AyKilidi.Single().KilitliSonTarih = Agustos.AddDays(-1);
            db.SaveChanges();
            AyRaporAnlikGoruntusu.GecisTohumu(db, f.Saat!.GetUtcNow());
        }

        // Kilitli Temmuz: kural 1 rakamlarıyla birebir (kredi Gelen'de ve Ay sonucunda).
        var temmuz = await c.GetStringAsync(Url(Temmuz));
        Assert.Equal(Dondurulmus(kural1Temmuz, AylikKural.V1), temmuz);
        var temmuzMezat = JsonNode.Parse(temmuz)!["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == "MEZAT")!;
        Assert.Equal((200_000m, 100_000m), (temmuzMezat["gelen"]!.GetValue<decimal>(), temmuzMezat["aySonucu"]!.GetValue<decimal>()));
        Assert.Null(JsonNode.Parse(temmuz)!["krediGirisi"]);

        // Açık Eylül: güncel kural (K2) — kredi Gelen ve Ay sonucu dışında, ayrı alanda.
        var eylul = JsonNode.Parse(await c.GetStringAsync(Url(Month)))!;
        var eylulMezat = eylul["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == "MEZAT")!;
        Assert.Equal((80_000m, -20_000m, 120_000m), (eylulMezat["gelen"]!.GetValue<decimal>(), eylulMezat["aySonucu"]!.GetValue<decimal>(), eylul["krediGirisi"]!.GetValue<decimal>()));
        Assert.Equal(AylikKural.Guncel, eylul["kuralSurumu"]!.GetValue<int>());

        // Kural değiştikten sonra kapatılan ay, kapatıldığı andaki (güncel kural) raporla donar.
        var agustosOnce = await c.GetStringAsync(Url(Agustos));
        await Kilit(c, Agustos);
        Assert.Equal(Dondurulmus(agustosOnce, AylikKural.Guncel), await c.GetStringAsync(Url(Agustos)));
        Assert.Equal(new[] { AylikKural.V1, AylikKural.Guncel }, Goruntuler(f).Where(g => g.Ay >= 7).Select(g => g.KuralSurumu));
    }

    [Fact]
    public void Goc_oncesi_kilitli_ay_raporu_goc_sonrasinda_birebir_ayni_kalir()
    {
        // Bir önceki sürümün şeması (20260928000200_KartGecisIzi), Temmuz sonuna kadar kilitli; takipli kredi Temmuz'da çekilmiş.
        using var baglanti = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        baglanti.Open();
        var saat = new ServiceCollection().AddSingleton<TimeProvider>(new SabitSaat(Today)).BuildServiceProvider();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(baglanti).UseApplicationServiceProvider(saat).Options);
        db.GetService<IMigrator>().Migrate("20260928000200_KartGecisIzi");
        Calistir(db, """
            INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1, 'MEZAT', 1, 0, '100.0'), (2, 'TOPTAN', 1, 1, '0');
            INSERT INTO Ayarlar (TakipBaslangic, KasaAcilisDevri, IzleyiciSifreHash) VALUES ('2026-06-01', '1000.0', NULL);
            INSERT INTO Gelenler (DonemStart, Kanal, KanalId, TutarTl) VALUES ('2026-07-01', 'MEZAT', 1, '80000.00'), ('2026-06-01', 'TOPTAN', 2, '2500.5');
            INSERT INTO Islemler (Tarih, Cari, TutarTl, Kanal, KanalId, Tip, "Not") VALUES ('2026-07-15', 'Tedarik', '100000', 'MEZAT', 1, 0, NULL);
            INSERT INTO Krediler (Id, Ad, CekilenTutar, CekimTarihi, TaksitSayisi, AylikOdeme, OdemeGunu, Kanal, KanalId, GerceklesmeTakibi)
                VALUES (1, 'Takipli kredi', '120000', '2026-07-10', 12, '11000', 10, 'MEZAT', 1, 1);
            INSERT INTO TakipKrediler (KrediId, Surum, Baslangic, Aktif, MevcutKredi, EskiKayit, KanalIdleriJson, CekimPaylariJson)
                VALUES (1, 1, '2026-07-10', 1, 0, 0, '[1]', '[{"KanalId":1,"Tutar":120000}]');
            UPDATE AyKilidi SET KilitliSonTarih = '2026-07-31', Surum = 2 WHERE Id = 1;
            """);
        var once = new Dictionary<(int, int), string>();
        using (var eski = SurumOncesiBaglam.Ayni(db)) // önceki sürümün şeması: çekirdek sürüm sütunları yok
            foreach (var ay in new[] { 6, 7, 8 })
                once[(2026, ay)] = JsonSerializer.Serialize(new HesapServisi(eski).Aylik(2026, ay, kuralSurumu: AylikKural.V1), Web);
        Assert.Contains("\"gelen\":200000", once[(2026, 7)]); // kural 1: takipli kredi Gelen'de

        KasaVeritabaniBaslatici.Baslat(db); // migration + geçiş tohumu (bellek içi: yedek gerekmez)

        Assert.Empty(db.Database.GetPendingMigrations());
        foreach (var ay in new[] { 6, 7 })
            Assert.Equal(Dondurulmus(once[(2026, ay)], AylikKural.V1), JsonSerializer.Serialize(new HesapServisi(db).AylikYanit(2026, ay), Web));
        // Açık ay görüntü almaz; tekrar başlatma yeni görüntü üretmez.
        Assert.Equal(new[] { (2026, 6), (2026, 7) }, db.AyRaporAnlikGoruntuleri.AsNoTracking().OrderBy(g => g.Ay).Select(g => new { g.Yil, g.Ay }).AsEnumerable().Select(g => (g.Yil, g.Ay)));
        KasaVeritabaniBaslatici.Baslat(db);
        Assert.Equal(2, db.AyRaporAnlikGoruntuleri.Count());
        Assert.DoesNotContain("dondurulmus", JsonSerializer.Serialize(new HesapServisi(db).AylikYanit(2026, 8), Web));
    }
}
