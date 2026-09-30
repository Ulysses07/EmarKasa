using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

/// <summary>
/// Takipli kart taksitlerinin ekstreye bağlanması (finance-3). İlk kesim tarihi yalnız ilk taksidin girdiği
/// döngüyü seçer; bütün taksitler kartın kendi kesim gününe (kısa ayda ay sonuna) düşer. Böylece bankada
/// olmayan, kartın döngüsüne paralel ikinci ekstre ve aynı ay ikinci kesim bildirimi oluşmaz. İlk kesimsiz
/// harcamanın ekstre ataması değişmez. Eski kuralla yazılmış paralel ekstreler dönüştürülmez, açılışta uyarılır.
/// Tarihler sabittir; sunucunun "bugün"ü her testte ayrıca verilir.
/// </summary>
public class KartKesimTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);
    private static readonly DateOnly Baslangic = new(2026, 9, 1);

    private static async Task<HttpClient> Editor(KasaWebFactory f, DateOnly baslangic)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = baslangic, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
    private static Task<KartTakipDto> Kart(HttpClient c, int kesimGunu, int sonOdemeGunu, DateOnly acilis) =>
        Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Kesim kartı", 100_000m, kesimGunu, sonOdemeGunu, acilis, 0, []));
    private static Task<HttpResponseMessage> HarcamaIstegi(HttpClient c, KartTakipDto kart, DateOnly tarih, decimal tutar, int taksit, DateOnly? ilkKesim) =>
        c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, tarih, "Taksitli alış", tutar, taksit, ilkKesim, [new(1, tutar)]));
    private static async Task<KartTakipDto> Harcama(HttpClient c, KartTakipDto kart, DateOnly tarih, decimal tutar, int taksit, DateOnly? ilkKesim)
    {
        var r = await HarcamaIstegi(c, kart, tarih, tutar, taksit, ilkKesim);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<KartTakipDto>())!;
    }
    /// <summary>Harcamanın taksitlerinin bağlı olduğu ekstrelerin kesim tarihleri (taksit sırasıyla).</summary>
    private static DateOnly[] TaksitKesimleri(KasaWebFactory f, int harcamaId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        return db.TakipKartTaksitler.Where(t => t.HarcamaId == harcamaId).OrderBy(t => t.Id)
            .Join(db.TakipEkstreler, t => t.EkstreId, e => e.Id, (t, e) => e.KesimTarihi).ToArray();
    }
    private static DateOnly Gun(int yil, int ay, int gun) => new(yil, ay, Math.Min(gun, DateTime.DaysInMonth(yil, ay)));

    [Fact]
    public async Task Kaymis_ilk_kesim_sonraki_taksitleri_sabitlemez_her_ay_tek_ekstre_ve_tek_kesim_bildirimi_olur()
    {
        // Kesim günü 5 olan kartta banka Ekim kesimini 6'sına kaydırmış; editör ilk kesimi 6 Ekim giriyor.
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f, Baslangic);
        var kart = await Kart(c, 5, 25, Baslangic);
        kart = await Harcama(c, kart, new(2026, 9, 20), 600m, 6, new(2026, 10, 6));

        var harcama = Assert.Single(kart.Harcamalar);
        Assert.Equal(new DateOnly[] { new(2026, 10, 5), new(2026, 11, 5), new(2026, 12, 5), new(2027, 1, 5), new(2027, 2, 5), new(2027, 3, 5) },
            TaksitKesimleri(f, harcama.Id));
        Assert.All(kart.Ekstreler.Where(s => s.Borc > 0), s => Assert.Equal(100m, s.Borc));
        Assert.Equal(new DateOnly(2026, 10, 25), kart.Ekstreler.Single(s => s.KesimTarihi == new DateOnly(2026, 10, 5)).SonOdemeTarihi);
        // Kartın döngüsüne paralel ikinci (6'sı) ekstre yok: her ay tek ekstre.
        Assert.All(kart.Ekstreler.GroupBy(s => (s.KesimTarihi.Year, s.KesimTarihi.Month)), g => Assert.Single(g));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var kesimler = FinansTakipServisi.GetNotificationEvents(db, Bugun).Where(e => e.Kaynak == "Kart" && e.KaynakId == kart.Id && e.Tur == "Kesim").ToList();
        Assert.All(kesimler.GroupBy(e => (e.Tarih.Year, e.Tarih.Month)), g => Assert.Single(g));
        Assert.All(kesimler, e => Assert.Equal(5, e.Tarih.Day));
        Assert.All(db.TakipEkstreler.Where(s => s.KrediKartiId == kart.Id).ToList(), s => Assert.Equal(5, s.KesimTarihi.Day));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public async Task Kisa_ayda_verilen_ilk_kesim_sonraki_taksitleri_kartin_gunune_dondurur(int kesimGunu)
    {
        // Şubat 2027 28 gün: kesim günü 29–31 olan kartın Şubat kesimi 28'idir; sonraki taksitler 28'ine sabitlenmez.
        var bugun = new DateOnly(2027, 3, 10);
        var baslangic = new DateOnly(2027, 1, 1);
        await using var f = KasaWebFactory.Sabit(bugun);
        using var c = await Editor(f, baslangic);
        var kart = await Kart(c, kesimGunu, 10, baslangic);
        kart = await Harcama(c, kart, new(2027, 2, 10), 90m, 3, new(2027, 2, 28));
        Assert.Equal(new[] { new DateOnly(2027, 2, 28), Gun(2027, 3, kesimGunu), Gun(2027, 4, kesimGunu) }, TaksitKesimleri(f, Assert.Single(kart.Harcamalar).Id));
        Assert.All(kart.Ekstreler.GroupBy(s => (s.KesimTarihi.Year, s.KesimTarihi.Month)), g => Assert.Single(g));
    }

    [Theory]
    [InlineData(2026, 10, 20)] // 5 Ekim'e 15, 5 Kasım'a 16 gün
    [InlineData(2026, 10, 13)] // 5 Ekim'e 8 gün
    public async Task Kart_dongusunden_uzak_ilk_kesim_veri_yazmadan_reddedilir(int yil, int ay, int gun)
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f, Baslangic);
        var kart = await Kart(c, 5, 25, Baslangic);
        var r = await HarcamaIstegi(c, kart, new(2026, 9, 20), 600m, 6, new(yil, ay, gun));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("hesap kesim gününe (5)", (await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("hata").GetString());
        var sonra = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Empty(sonra.Harcamalar);
        Assert.Equal(kart.Surum, sonra.Surum);
        Assert.All(sonra.Ekstreler, s => Assert.Equal(0m, s.Borc));
    }

    [Theory]
    [InlineData(2026, 10, 12, 2026, 10, 5)] // banka kesimi 7 gün kaydırmış: aynı döngü
    [InlineData(2026, 9, 29, 2026, 10, 5)]  // banka kesimi öne almış
    [InlineData(2026, 11, 5, 2026, 11, 5)]  // provizyon kesimden sonra düştü: bir sonraki döngü
    public async Task Yakin_ilk_kesim_kartin_en_yakin_duzenli_kesimine_baglanir(int yil, int ay, int gun, int beklenenYil, int beklenenAy, int beklenenGun)
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f, Baslangic);
        var kart = await Kart(c, 5, 25, Baslangic);
        kart = await Harcama(c, kart, new(2026, 9, 20), 30m, 3, new(yil, ay, gun));
        var ilk = new DateOnly(beklenenYil, beklenenAy, beklenenGun);
        Assert.Equal(new[] { ilk, ilk.AddMonths(1), ilk.AddMonths(2) }, TaksitKesimleri(f, Assert.Single(kart.Harcamalar).Id));
        Assert.All(kart.Ekstreler.GroupBy(s => (s.KesimTarihi.Year, s.KesimTarihi.Month)), g => Assert.Single(g));
    }

    [Fact]
    public async Task Ileri_kaymis_kesim_harcamayi_iceriyorsa_harcama_kesim_gunu_harcamadan_once_olan_ekstreye_baglanir()
    {
        // Bilinçli kural: banka 5 Ekim kesimini 12'sine kaydırdıysa 9 Ekim harcaması Ekim döngüsünün ekstresindedir.
        // Ekstre kartın düzenli günüyle (5 Ekim) tutulur; bu yüzden ekstre tarihi harcamadan önce olabilir. Pencere
        // dar: ilk kesim harcamadan önce olamaz ve düzenli kesimden en çok 7 gün uzaktır. Vade düzenli kesimden
        // hesaplanır (25 Ekim): banka vadeyi de kaydırdıysa hatırlatma erken gelir, geç kalmaz.
        var bugun = new DateOnly(2026, 10, 15);
        await using var f = KasaWebFactory.Sabit(bugun);
        using var c = await Editor(f, Baslangic);
        var kart = await Kart(c, 5, 25, Baslangic);
        kart = await Harcama(c, kart, new(2026, 10, 9), 90m, 3, new(2026, 10, 12));
        var kaymis = Assert.Single(kart.Harcamalar);
        Assert.Equal(new DateOnly[] { new(2026, 10, 5), new(2026, 11, 5), new(2026, 12, 5) }, TaksitKesimleri(f, kaymis.Id));
        Assert.Equal(new DateOnly(2026, 10, 25), kart.Ekstreler.Single(s => s.KesimTarihi == new DateOnly(2026, 10, 5)).SonOdemeTarihi);

        // Pencerenin dışı: 8 gün kaymış ilk kesim ve harcamadan önceki ilk kesim veri yazmadan reddedilir.
        foreach (var ilk in new[] { new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 8) })
            Assert.Equal(HttpStatusCode.BadRequest, (await HarcamaIstegi(c, kart, new(2026, 10, 9), 90m, 3, ilk)).StatusCode);
        Assert.Single((await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}", cancellationToken: TestContext.Current.CancellationToken))!.Harcamalar);
        // Kesimden sonraki harcamanın banka kesimi bir sonraki döngüdeyse o döngüye bağlanır.
        kart = await Harcama(c, kart, new(2026, 10, 9), 60m, 2, new(2026, 11, 5));
        var sonraki = kart.Harcamalar.Single(h => h.Id != kaymis.Id);
        Assert.Equal(new DateOnly[] { new(2026, 11, 5), new(2026, 12, 5) }, TaksitKesimleri(f, sonraki.Id));
    }

    [Fact]
    public async Task Takip_baslangicindan_onceki_duzenli_kesime_yuvarlanan_ilk_kesim_veri_yazmadan_reddedilir()
    {
        // Takip 3 Ekim'de başladı, kesim günü 30: 3 ve 5 Ekim'lik ilk kesim en yakın düzenli kesim olan 30 Eylül'e düşer.
        // Takipten önceki ekstre izlenmez (bakım adımı da açmaz); harcama başlangıçtan önceki bir ekstre açmaz.
        var bugun = new DateOnly(2026, 10, 20);
        var baslangic = new DateOnly(2026, 10, 3);
        await using var f = KasaWebFactory.Sabit(bugun);
        using var c = await Editor(f, baslangic);
        var kart = await Kart(c, 30, 10, baslangic);
        foreach (var ilk in new[] { new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 5) })
        {
            var r = await HarcamaIstegi(c, kart, baslangic, 100m, 2, ilk);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var hata = (await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("hata").GetString()!;
            Assert.Contains("takip başlangıcından (03.10.2026)", hata);
            Assert.Contains("30.09.2026", hata);
        }
        var sonra = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Empty(sonra.Harcamalar);
        Assert.Equal(kart.Surum, sonra.Surum);
        using (var scope = f.Services.CreateScope())
            Assert.DoesNotContain(scope.ServiceProvider.GetRequiredService<KasaDbContext>().TakipEkstreler.ToList(), s => s.KesimTarihi < baslangic);

        // Harcamanın düştüğü sonraki kesim kabul edilir.
        kart = await Harcama(c, sonra, baslangic, 100m, 2, new(2026, 10, 30));
        Assert.Equal(new DateOnly[] { new(2026, 10, 30), new(2026, 11, 30) }, TaksitKesimleri(f, Assert.Single(kart.Harcamalar).Id));
    }

    [Fact]
    public async Task Ilk_kesimsiz_harcamanin_ekstre_atamasi_degismez()
    {
        // Eşitlik: ilk kesim verilmeyen (mevcut verideki bütün yollar: gider, açılış, geçiş, masraf, içe aktarma)
        // harcamanın taksitleri önceki kuralla aynı ekstrelere bağlanır.
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f, Baslangic);
        var kart = await Kart(c, 31, 10, Baslangic);
        kart = await Harcama(c, kart, new(2026, 9, 20), 50m, 5, null);
        Assert.Equal(new DateOnly[] { new(2026, 9, 30), new(2026, 10, 31), new(2026, 11, 30), new(2026, 12, 31), new(2027, 1, 31) },
            TaksitKesimleri(f, Assert.Single(kart.Harcamalar).Id));
    }

    [Fact]
    public async Task Kesim_gunu_degisince_yeni_taksitler_yeni_gune_duser_eski_ekstre_atamalari_degismez()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f, Baslangic);
        var kart = await Kart(c, 5, 25, Baslangic);
        kart = await Harcama(c, kart, new(2026, 9, 10), 90m, 3, null);
        var eski = Assert.Single(kart.Harcamalar);
        var eskiKesimler = TaksitKesimleri(f, eski.Id);
        Assert.Equal(new DateOnly[] { new(2026, 10, 5), new(2026, 11, 5), new(2026, 12, 5) }, eskiKesimler);

        // Banka kesim gününü 20'ye aldı.
        var duzelt = await c.PutAsJsonAsync($"/api/takip/kartlar/{kart.Id}", new KartTakipYaz(Guid.NewGuid(), kart.Surum, kart.Ad, kart.Limit, 20, 10, Baslangic, 0, []), cancellationToken: ct);
        Assert.True(duzelt.IsSuccessStatusCode, await duzelt.Content.ReadAsStringAsync(ct));
        kart = (await duzelt.Content.ReadFromJsonAsync<KartTakipDto>(cancellationToken: ct))!;
        Assert.Equal(20, kart.KesimGunu);

        // Eski kesim gününe (5) yakın ilk kesim artık kartın döngüsünde değil.
        var r = await HarcamaIstegi(c, kart, new(2026, 9, 22), 60m, 3, new(2026, 10, 5));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);

        // Yeni gün etrafında bir gün kaymış ilk kesim: taksitler 20'sinde.
        kart = await Harcama(c, kart, new(2026, 9, 22), 60m, 3, new(2026, 10, 21));
        var yeni = kart.Harcamalar.Single(h => h.Id != eski.Id);
        Assert.Equal(new DateOnly[] { new(2026, 10, 20), new(2026, 11, 20), new(2026, 12, 20) }, TaksitKesimleri(f, yeni.Id));
        Assert.Equal(eskiKesimler, TaksitKesimleri(f, eski.Id));
        Assert.Equal(30m, kart.Ekstreler.Single(s => s.KesimTarihi == new DateOnly(2026, 11, 5)).Borc);
        Assert.Equal(20m, kart.Ekstreler.Single(s => s.KesimTarihi == new DateOnly(2026, 11, 20)).Borc);
    }

    [Fact]
    public async Task Eski_kuralla_yazilmis_paralel_ekstre_ve_kaynaksiz_eksi_kart_gideri_acilista_uyarilir_kayitlar_ve_raporlar_degismez()
    {
        // finance-3 ve finance-9 düzeltmeleri yalnız yeni yazmaları kapsar; eski kuralla yazılmış kayıtlar otomatik
        // dönüştürülmez (doğru hali ancak banka ekstresiyle belirlenebilir), her açılışta uyarı olarak görünür kılınır.
        using var baglanti = new SqliteConnection("Data Source=:memory:");
        baglanti.Open();
        KartTakipDto eski, temiz;
        string once;
        await using (var f = new HazirFabrika(baglanti) { Saat = new SabitSaat(Bugun) })
        {
            using var c = await Editor(f, Baslangic);
            eski = await Kart(c, 5, 25, Baslangic);
            temiz = await Kart(c, 5, 25, Baslangic);
            eski = await Harcama(c, eski, new(2026, 9, 20), 300m, 3, null);
            // Yeni kuralla kaymış ilk kesim (6 Ekim) kartın gününe bağlanır: uyarı üretmez.
            temiz = await Harcama(c, temiz, new(2026, 9, 20), 300m, 3, new(2026, 10, 6));
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                // Eski kural (finance-3): 6 Ekim ilk kesimli taksitler 6'sına sabitlenmiş, kartın döngüsüne paralel ekstrelerdeydi.
                var harcamaId = Assert.Single(eski.Harcamalar).Id;
                var taksitler = db.TakipKartTaksitler.Where(t => t.HarcamaId == harcamaId).OrderBy(t => t.Id).ToList();
                for (var i = 0; i < taksitler.Count; i++)
                {
                    var paralel = new TakipEkstreEntity { KrediKartiId = eski.Id, KesimTarihi = new DateOnly(2026, 10, 6).AddMonths(i), SonOdemeTarihi = new DateOnly(2026, 10, 25).AddMonths(i) };
                    db.TakipEkstreler.Add(paralel);
                    db.SaveChanges();
                    taksitler[i].EkstreId = paralel.Id;
                }
                // Eski kural (finance-9): genel gider ekranından takipli karta girilen eksi gider kaynaksız alacak olurdu.
                db.Islemler.Add(new IslemEntity
                {
                    Tarih = new(2026, 9, 22),
                    Cari = "PERAKENDE iadesi",
                    TutarTl = -30m,
                    Kanal = "PERAKENDE",
                    KanalId = 2,
                    Tip = GiderTipi.KrediKarti,
                    KrediKartiId = eski.Id
                });
                db.SaveChanges();
                FinansTakipServisi.Bakim(db);
                Assert.Single(db.TakipHarcamalar.Where(h => h.KrediKartiId == eski.Id && h.IslemId != null).ToList(), h => h.Tutar == -30m);
            }
            once = await Raporlar(c, eski.Id);
        }

        var loglar = new UyariToplayici();
        await using var f2 = new HazirFabrika(baglanti, loglar) { Saat = new SabitSaat(Bugun) };
        using var c2 = await f2.EditorClientAsync();
        Assert.Equal(once, await Raporlar(c2, eski.Id));
        Assert.Contains(loglar.Uyarilar, m => m.StartsWith($"Kart {eski.Id} (Kesim kartı)") && m.Contains("kesim günü (5) dışında kesilmiş 3 ekstre")
            && m.Contains("06.10.2026–06.12.2026") && m.Contains("3 tanesi aynı ay") && m.Contains("otomatik dönüştürülmez"));
        Assert.Contains(loglar.Uyarilar, m => m.StartsWith($"Kart {eski.Id} (Kesim kartı)") && m.Contains("1 eksi kart gideri")
            && m.Contains("30,00 TL") && m.Contains("22.09.2026") && m.Contains("otomatik dönüştürülmez"));
        Assert.DoesNotContain(loglar.Uyarilar, m => m.StartsWith($"Kart {temiz.Id} ("));
    }

    private static async Task<string> Raporlar(HttpClient c, int kartId) =>
        await c.GetStringAsync("/api/rapor/panel") + await c.GetStringAsync($"/api/takip/kartlar/{kartId}") + await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=9");

    // Uygulamayı önceden hazırlanmış bağlantı üzerinde başlatır: ikinci açılış ilkinin yazdığı veriyi görür.
    private sealed class HazirFabrika(SqliteConnection hazir, UyariToplayici? loglar = null) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (loglar is not null)
                builder.ConfigureLogging(logging => logging.AddProvider(loglar));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(hazir));
            });
        }
    }
}
