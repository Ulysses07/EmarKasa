using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

/// <summary>
/// Kasa saati (tests-1/2/4): üretimde sistem saati ve İstanbul günü; testte fabrikanın sabit saati
/// sunucunun her "bugün" okumasına (tohum, doğrulama, rapor, istek dışı servis çağrısı) ve kayıt
/// damgalarına uygulanır. İki fabrikanın saati aynı süreçte karışmaz ve istek saati test akışına sızmaz.
/// </summary>
public class SaatTests
{
    private sealed record Ayar(DateOnly TakipBaslangic);

    [Theory]
    [InlineData("2026-09-30T20:59:59Z", "2026-09-30")]
    [InlineData("2026-09-30T21:00:00Z", "2026-10-01")]
    [InlineData("2028-02-28T21:30:00Z", "2028-02-29")]
    public void Istanbul_gunu_UTC_anina_uc_saat_eklenerek_bulunur(string utc, string gun) =>
        Assert.Equal(DateOnly.Parse(gun, CultureInfo.InvariantCulture), new SabitSaat(DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture)).IstanbulBugun());

    [Fact]
    public void Sabit_gun_Istanbul_ogleninde_durur_yalniz_test_ilerletir()
    {
        var saat = new SabitSaat(new DateOnly(2027, 1, 1));
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 9, 0, 0, TimeSpan.Zero), saat.GetUtcNow());
        Assert.Equal(saat.GetUtcNow(), saat.GetUtcNow());
        saat.Ayarla(new(2028, 2, 29));
        Assert.Equal(new DateOnly(2028, 2, 29), saat.IstanbulBugun());
    }

    [Fact]
    public async Task Uretimde_saat_sistem_saatidir_istek_icinde_ve_disinda_Istanbul_gunu_doner()
    {
        // Bilinçli istisna: üretim davranışı gerçek sistem saatiyle ölçülür. Sonuç takvime bağlı değildir; her
        // okuma yalnız ölçüm aralığında (once..sonra) olmalıdır.
        var once = TimeProvider.System.IstanbulBugun();
        Assert.Same(TimeProvider.System, KasaSaati.Gecerli);
        var disarida = new[] { KasaSaati.Bugun, FinansTakipServisi.Bugun };
        // Program.cs'nin kendi kablolaması: test fabrikasının saat kaydı yok.
        await using var f = new UretimKablolamasi();
        using var c = await f.EditorClientAsync();
        Assert.Same(TimeProvider.System, f.Services.GetRequiredService<TimeProvider>());
        var sunucu = (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet", cancellationToken: TestContext.Current.CancellationToken))!.Tarih;
        DateOnly servis;
        using (var scope = f.Services.CreateScope())
            servis = scope.ServiceProvider.GetRequiredService<KasaDbContext>().Bugunu();
        DateOnly bagimsizBaglam;
        using (var connection = new SqliteConnection("Data Source=:memory:"))
        using (var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).Options))
        {
            // Uygulama servisi olmayan bağlam da sistem saatine düşer.
            Assert.Same(TimeProvider.System, db.Saati());
            bagimsizBaglam = db.Bugunu();
        }
        var sonra = TimeProvider.System.IstanbulBugun();
        // Gece yarısında ölçüm iki güne yayılabilir; her okuma ölçüm aralığında kalmalı.
        Assert.All(disarida.Append(sunucu).Append(servis).Append(bagimsizBaglam), gun => Assert.InRange(gun, once, sonra));
    }

    [Fact]
    public async Task Program_kablolamasi_DI_saatini_uclara_istek_disi_servislere_ve_eski_kart_ucuna_uygular()
    {
        // Yalnız DI saati değişir; istek saatini uçlara taşıyan kayıt Program.cs'den gelmelidir.
        var bugun = new DateOnly(2091, 1, 15);
        await using var f = new UretimKablolamasi(new SabitSaat(bugun));
        using var c = await f.EditorClientAsync();
        Assert.Equal(bugun, (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet", cancellationToken: TestContext.Current.CancellationToken))!.Tarih);
        // Eski kredi kartları ucu son kesimi kasa saatinden hesaplar: kesimi bugün olan kartta bugünkü harcama ekstrededir.
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(bugun, db.Bugunu());
        var kart = new KrediKartiEntity { Ad = "Saat kartı", KesimTarihi = bugun, SonOdemeTarihi = bugun.AddDays(10), Limit = 1000m };
        db.KrediKartlari.Add(kart);
        db.SaveChanges();
        // Eski karta bağlı mevcut harcama (K3: yeni gider takipteki karta bağlanır).
        db.Islemler.Add(new IslemEntity { Tarih = bugun, Cari = "Bugünkü kart harcaması", TutarTl = 40m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.KrediKarti, KrediKartiId = kart.Id });
        db.SaveChanges();
        var kartlar = (await c.GetFromJsonAsync<List<KrediKartiTuretilmisDto>>("/api/kredikartlari", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal(40m, kartlar.Single(k => k.Id == kart.Id).EkstreBorc);
        Assert.Same(TimeProvider.System, KasaSaati.Gecerli);
    }

    [Theory]
    [InlineData(2021, 3, 10)] // sistem takviminden geride
    [InlineData(2091, 1, 15)] // sistem takviminden ileride
    public async Task Sabit_saatli_sunucu_tohumu_dogrulamayi_raporu_ve_istek_disi_cagrilari_ayni_gune_baglar(int yil, int ay, int gun)
    {
        var ct = TestContext.Current.CancellationToken;
        var bugun = new DateOnly(yil, ay, gun);
        await using var f = KasaWebFactory.Sabit(bugun);
        using var c = await f.EditorClientAsync();
        Assert.Equal(bugun, f.Bugun);
        Assert.Equal(bugun, (await c.GetFromJsonAsync<Ayar>("/api/ayarlar", cancellationToken: ct))!.TakipBaslangic);
        Assert.Equal(bugun, (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet", cancellationToken: ct))!.Tarih);
        // Panel dönemi sunucunun gününde biter: bugünkü gider bu ayın sonucuna girer.
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(bugun, "Bugünkü gider", 5m, "MEZAT", GiderTipi.Cari), cancellationToken: ct)).EnsureSuccessStatusCode();
        Assert.Equal(-5m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel", cancellationToken: ct))!.BuAySonucu);
        // Ödeme doğrulaması sunucunun gününe bakar: yarın reddedilir, bugün kabul edilir.
        var card = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Saat kartı", 1000m, 5, 25, bugun, 0, []));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, bugun.AddDays(1), 10m), cancellationToken: ct)).StatusCode);
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, bugun, 10m));
        // İstek dışında (arka plan işi, doğrudan servis çağrısı) bağlam fabrikanın saatini taşır; hesap servisi
        // paneli de aynı güne göre kurar (tek hesapta iki farklı "bugün" yok): istekteki panelle birebir aynıdır.
        var istek = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel", cancellationToken: ct))!;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Same(f.Saat, db.Saati());
        Assert.Equal(bugun, db.Bugunu());
        var servis = new HesapServisi(db).Panel(ct);
        Assert.Equal((istek.GuncelKasa, istek.BuHaftaSonucu, istek.BuAySonucu), (servis.GuncelKasa, servis.BuHaftaSonucu, servis.BuAySonucu));
        Assert.NotEqual(0m, servis.BuAySonucu);
        // İstek saati test akışına sızmaz.
        Assert.Same(TimeProvider.System, KasaSaati.Gecerli);
    }

    [Fact]
    public async Task Kayit_damgalari_sunucunun_saatinden_yazilir()
    {
        var ct = TestContext.Current.CancellationToken;
        var bugun = new DateOnly(2021, 3, 10);
        await using var f = new DamgaFabrikasi { Saat = new SabitSaat(bugun) };
        using var c = await f.EditorClientAsync();
        var an = f.Saat!.GetUtcNow();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2021, 1, 1), kasaAcilisDevri = 0m }, cancellationToken: ct)).EnsureSuccessStatusCode();

        // Fark sıfırdan farklı: açıklama zorunlu (gap-denetim-izi-gozlemlenebilirlik-3). Fark açıklamasının anı da sunucu saatidir.
        var onizleme = await Post<KasaKontrolOnizlemeDto>(c, "/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(10m, "Sayım"));
        var kontrol = await Post<KasaKontrolDto>(c, "/api/kasa-kontrol", new KasaKontrolYaz(Guid.NewGuid(), 10m, onizleme.KontrolOzeti, "Sayım"));
        Assert.Equal((an, (DateOnly?)bugun), (kontrol.Kaydedildi, kontrol.HesapTarihi));
        using (var r = await c.PutAsJsonAsync($"/api/kasa-kontrol/{kontrol.Id}/aciklama", new KasaKontrolAciklamaYaz(Guid.NewGuid(), kontrol.Surum, "Kasadaki fazla açıklandı"), cancellationToken: ct))
            Assert.Equal(an, (await r.Content.ReadFromJsonAsync<KasaKontrolDto>(cancellationToken: ct))!.FarkAciklamaZamani);

        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", cancellationToken: ct))!;
        kilit = await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, 2021, 2, "Şubat tamamlandı"));
        Assert.Equal(an, Assert.Single(kilit.Gecmis).Zaman);

        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, bugun, "Satıcı", null, [new("Mal", 100m, [new(1, 100m)])]));
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent("%PDF-1.7 fatura"u8.ToArray()), "dosya", "fatura.pdf");
            using var r = await c.PostAsync($"/api/alis/{alis.Id}/belgeler", form, ct);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync(ct));
            Assert.Equal(an, (await r.Content.ReadFromJsonAsync<BelgeDto>(cancellationToken: ct))!.Yuklendi);
        }

        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("Banka"), "kaynak");
            form.Add(new StringContent("Akbank"), "banka");
            form.Add(new StringContent("Ana hesap"), "hesapAdi");
            form.Add(new ByteArrayContent("%PDF-1.7 ekstre"u8.ToArray()), "dosya", "ekstre.pdf");
            using var r = await c.PostAsync("/api/ekstre-aktar/yukle", form, ct);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync(ct));
            Assert.Equal(an, (await r.Content.ReadFromJsonAsync<EkstreBelgeDto>(cancellationToken: ct))!.Yuklendi);
        }

        using (var r = await c.PostAsync("/api/yedek", null, ct))
        {
            r.EnsureSuccessStatusCode();
            Assert.Equal(an, YedekSaklama.Tani(r.Content.Headers.ContentDisposition!.FileName!.Trim('"'))!.Value.Zaman);
        }
        var durum = (await c.GetFromJsonAsync<YedekDurumu>("/api/yedek/durum", cancellationToken: ct))!;
        Assert.Equal(an, durum.SonYedek);
        Assert.Equal(an, durum.SonDogrulama);
        Assert.Equal(an, durum.SonElleYedek);
    }

    [Fact]
    public async Task Paralel_fabrikalar_birbirinin_saatini_gormez_istek_saati_cagirana_sizmaz()
    {
        DateOnly ocak = new(2027, 1, 1), subat = new(2028, 2, 29);
        await using var f1 = KasaWebFactory.Sabit(ocak);
        await using var f2 = KasaWebFactory.Sabit(subat);
        using var c1 = await f1.EditorClientAsync();
        using var c2 = await f2.EditorClientAsync();
        // Fabrika başına istekler sıralı (tek in-memory bağlantı), iki fabrikanınkiler eşzamanlı akar.
        async Task<DateOnly[]> Oku(HttpClient c)
        {
            var gunler = new List<DateOnly>();
            for (var i = 0; i < 20; i++)
                gunler.Add((await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet"))!.Tarih);
            return [.. gunler];
        }
        var okumalar = await Task.WhenAll(Task.Run(() => Oku(c1)), Task.Run(() => Oku(c2)));
        Assert.All(okumalar[0], gun => Assert.Equal(ocak, gun));
        Assert.All(okumalar[1], gun => Assert.Equal(subat, gun));
        Assert.Same(TimeProvider.System, KasaSaati.Gecerli);
    }

    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>
    /// Program.cs'nin kendi kablolaması: <see cref="KasaWebFactory"/>'nin saat kaydı (AddKasaSaati) yoktur; yalnız
    /// veritabanı bellek içine alınır. Saat verilirse yalnız DI'daki <see cref="TimeProvider"/> değiştirilir.
    /// </summary>
    private sealed class UretimKablolamasi(TimeProvider? saat = null) : SizdirmayanFabrika<Program>
    {
        private readonly SqliteConnection _conn = new("Data Source=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            _conn.Open();
            builder.ConfigureLogging(logging => logging.ClearProviders());
            // KasaWebFactory ile aynı test kimliği (JWT anahtarı derleme anında okunur; bkz. KasaWebFactory).
            Environment.SetEnvironmentVariable("Kasa__EditorKullanici", "editor");
            Environment.SetEnvironmentVariable("Kasa__EditorSifre", "kasa123");
            Environment.SetEnvironmentVariable("Kasa__JwtKey", "test-jwt-anahtari-en-az-32-bayt-olmali!!");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(_conn));
                if (saat is not null)
                { services.RemoveAll<TimeProvider>(); services.AddSingleton(saat); }
            });
        }

        public async Task<HttpClient> EditorClientAsync()
        {
            var client = CreateClient();
            (await client.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "kasa123" })).EnsureSuccessStatusCode();
            return client;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _conn.Dispose();
        }
    }

    /// <summary>Yedek dizini geçici klasörde, PDF metni sabit: damga yazan her uç tek fabrikada çağrılabilir.</summary>
    private sealed class DamgaFabrikasi : KasaWebFactory
    {
        private readonly string _dizin = Path.Combine(Path.GetTempPath(), "kasa-saat-damga-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Yedek:Dizin"] = _dizin,
                ["Yedek:Etkin"] = "false",
                ["Bildirim:PushEtkin"] = "false",
                ["Bildirim:WorkerEtkin"] = "false",
                ["Bildirim:AnahtarDosyasi"] = Path.Combine(_dizin, ".kasa-push-keys.json")
            }));
            builder.ConfigureServices(services => { services.RemoveAll<IPdfMetinOkuyucu>(); services.AddSingleton<IPdfMetinOkuyucu>(new SabitPdf()); });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_dizin))
                Directory.Delete(_dizin, true);
        }
    }

    private sealed class SabitPdf : IPdfMetinOkuyucu
    {
        public Task<string> OkuAsync(byte[] pdf, CancellationToken ct) =>
            Task.FromResult("İşlem Tarihi    Açıklama                Tutar        Bakiye\n10.03.2021    KOMİSYON                  -10,00 TL    990,00 TL\n");
    }
}
