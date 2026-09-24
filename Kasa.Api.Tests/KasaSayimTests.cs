using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>
/// Bugün = 24 Eylül 2026 Perşembe (İstanbul). Tohum verisi (takip 1 Tem 2026, kasa açılışı 10.000):
/// <list type="bullet">
/// <item>20 Haz kartsız eski K.K 700 (takip öncesi) → Temmuz'un son döneminde (27–31 Tem) düşer.</item>
/// <item>6 Tem dönemi MEZAT geleni 5.000; 7 Tem cari 1.000; 15 Tem sabit gider (Ortak) 2.500.</item>
/// <item>10 Ağu karta bağlı K.K 400 → kasadan harcamayla değil, 23 Eyl'deki 400'lük kart ödemesiyle çıkar.</item>
/// <item>21 Eyl dönemi MEZAT geleni 1.000; 22 Eyl cari 200; 23 Eyl cari 300; 28 Eyl ileri tarihli 999.</item>
/// </list>
/// Beklenen defter kasası (gün sonu): 1–5 Tem 10.000 · 6 Tem 15.000 · 7 Tem 14.000 · 15 Tem 11.500 ·
/// 26 Tem 11.500 · 27 Tem 10.800 · … 20 Eyl 10.800 · 21 Eyl 11.800 · 22 Eyl 11.600 · 23–24 Eyl 10.900.
/// </summary>
public class KasaSayimFactory : KasaWebFactory
{
    public static readonly DateOnly Bugun = new(2026, 9, 24);

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new Saat())));
    }

    private sealed class Saat : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private bool _tohumlandi;
    private readonly SemaphoreSlim _kilit = new(1, 1);

    private record IdY(int Id);

    /// <summary>Editör istemcisi; veri ilk çağrıda bir kez tohumlanır.</summary>
    public async Task<HttpClient> TohumluEditorAsync()
    {
        var c = await EditorClientAsync();
        await _kilit.WaitAsync();
        try
        {
            if (_tohumlandi) return c;
            await TakipBaslangiciAyarla(c, new DateOnly(2026, 7, 1), kasaAcilis: 10_000m);
            async Task<int> Post(string yol, object govde)
            {
                var r = await c.PostAsJsonAsync(yol, govde, Json);
                r.EnsureSuccessStatusCode();
                return (await r.Content.ReadFromJsonAsync<IdY>(Json))!.Id;
            }
            async Task Gelen(string d, string k, decimal t)
                => (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = d, kanal = k, tutarTl = t })).EnsureSuccessStatusCode();

            await Post("/api/giderkalemleri", new { ad = "Kira", aktif = true });
            var kart = await Post("/api/kredikartlari", new { ad = "Bonus", kesimTarihi = "2026-08-05", sonOdemeTarihi = "2026-08-15", limit = 50_000m, borc = 0m });
            await Post("/api/islemler", new { tarih = "2026-06-20", cari = "A", tutarTl = 700m, kanal = "PERAKENDE", tip = "KrediKarti" });
            await Gelen("2026-07-06", "MEZAT", 5_000m);
            await Post("/api/islemler", new { tarih = "2026-07-07", cari = "Market", tutarTl = 1_000m, kanal = "MEZAT", tip = "Cari" });
            await Post("/api/islemler", new { tarih = "2026-07-15", cari = "Kira", tutarTl = 2_500m, kanal = "Ortak", tip = "SabitGider" });
            await Post("/api/islemler", new { tarih = "2026-08-10", cari = "B", tutarTl = 400m, kanal = "TOPTAN", tip = "Cari", krediKartiId = kart });
            await Gelen("2026-09-21", "MEZAT", 1_000m);
            await Post("/api/islemler", new { tarih = "2026-09-22", cari = "X", tutarTl = 200m, kanal = "MEZAT", tip = "Cari" });
            await Post("/api/islemler", new { tarih = "2026-09-23", cari = "X", tutarTl = 300m, kanal = "TOPTAN", tip = "Cari" });
            await Post("/api/kartodemeler", new { krediKartiId = kart, tarih = "2026-09-23", tutar = 400m });
            await Post("/api/islemler", new { tarih = "2026-09-28", cari = "X", tutarTl = 999m, kanal = "MEZAT", tip = "Cari" });
            _tohumlandi = true;
            return c;
        }
        finally { _kilit.Release(); }
    }

    public async Task<HttpClient> IzleyiciAsync()
    {
        var editor = await EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" })).EnsureSuccessStatusCode();
        var c = CreateClient();
        (await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izle123" })).EnsureSuccessStatusCode();
        return c;
    }
}

public record KasaSayimY(int Id, DateOnly Tarih, decimal SayilanTutar, decimal HesaplananTutar, decimal Fark,
    decimal? GuncelHesaplanan, string? Not, DateTime KayitZamaniUtc);
public record KasaHesapY(DateOnly Tarih, decimal HesaplananTutar);

/// <summary>Defter değeri hesabı ve doğrulamalar (veri değiştirmeyen testler).</summary>
public class KasaSayimHesapTests : IClassFixture<KasaSayimFactory>
{
    private readonly KasaSayimFactory _f;
    public KasaSayimHesapTests(KasaSayimFactory f) => _f = f;

    private static async Task<decimal> HesaplaAsync(HttpClient c, string tarih)
    {
        var r = await c.GetAsync($"/api/kasasayimlari/hesapla?tarih={tarih}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var y = (await r.Content.ReadFromJsonAsync<KasaHesapY>(KasaSayimFactory.Json))!;
        Assert.Equal(DateOnly.Parse(tarih), y.Tarih);
        return y.HesaplananTutar;
    }

    [Fact]
    public async Task Bugun_icin_panelin_guncel_kasasina_esittir()
    {
        var c = await _f.TohumluEditorAsync();
        var panel = await c.GetFromJsonAsync<JsonElement>("/api/rapor/panel");
        var guncel = panel.GetProperty("guncelKasa").GetDecimal();
        Assert.Equal(10_900m, guncel);   // ileri tarihli 999 düşülmez
        Assert.Equal(guncel, await HesaplaAsync(c, "2026-09-24"));
    }

    [Theory]
    [InlineData("2026-07-01", 10_000)]    // takibin ilk günü: açılış devri
    [InlineData("2026-07-06", 15_000)]    // dönemin geleni dönem başından sayılır; 7 Tem gideri henüz yok
    [InlineData("2026-07-07", 14_000)]
    [InlineData("2026-07-15", 11_500)]
    [InlineData("2026-07-26", 11_500)]
    [InlineData("2026-07-27", 10_800)]    // Temmuz'un son dönemi başladı: kartsız eski K.K düşer (panelle aynı kural)
    [InlineData("2026-08-10", 10_800)]    // karta bağlı K.K harcama günü kasadan çıkmaz
    [InlineData("2026-09-20", 10_800)]
    [InlineData("2026-09-21", 11_800)]
    [InlineData("2026-09-22", 11_600)]    // dönem ortası: 22'sine kadarki gider, 23'ü yok
    [InlineData("2026-09-23", 10_900)]    // cari 300 + kart ödemesi 400 ödeme gününde çıkar
    public async Task Donem_ortasi_dahil_gun_sonu_defter_kasasi(string tarih, int beklenen)
    {
        var c = await _f.TohumluEditorAsync();
        Assert.Equal(beklenen, await HesaplaAsync(c, tarih));
    }

    [Fact]
    public async Task Coklu_tarih_hesabi_tek_tarih_hesabiyla_her_gun_ayni()
    {
        await _f.TohumluEditorAsync();
        using var scope = _f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<HesapServisi>();
        var gunler = Enumerable.Range(0, KasaSayimFactory.Bugun.DayNumber - new DateOnly(2026, 7, 1).DayNumber + 1)
            .Select(i => new DateOnly(2026, 7, 1).AddDays(i)).ToList();
        var coklu = svc.KasaTarihlerde(gunler.Append(new DateOnly(2026, 6, 30)).Append(new DateOnly(2026, 9, 25)));

        Assert.Equal(gunler.Count, coklu.Count);   // takvim dışı tarihler yok
        foreach (var g in gunler)
            Assert.True(coklu[g] == svc.KasaTarihte(g), $"{g}: çoklu {coklu[g]} ≠ tekli {svc.KasaTarihte(g)}");
        Assert.Equal(svc.Panel().GuncelKasa, coklu[KasaSayimFactory.Bugun]);
    }

    [Theory]
    [InlineData("2026-09-25", "ileri bir gün")]
    [InlineData("2027-01-01", "ileri bir gün")]
    [InlineData("2026-06-30", "takip başlangıcından (01.07.2026) önce")]
    public async Task Ileri_ya_da_takip_oncesi_tarih_400(string tarih, string mesaj)
    {
        var c = await _f.TohumluEditorAsync();
        var r = await c.GetAsync($"/api/kasasayimlari/hesapla?tarih={tarih}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(mesaj, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString());

        var p = await c.PostAsJsonAsync("/api/kasasayimlari", new { tarih, sayilanTutar = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, p.StatusCode);
    }

    [Fact]
    public async Task Hesapla_tarihsiz_400()
    {
        var c = await _f.TohumluEditorAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/kasasayimlari/hesapla")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/kasasayimlari/hesapla?tarih=dun")).StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.005)]
    [InlineData(200_000_000_000)]
    public async Task Gecersiz_tutar_400(decimal tutar)
    {
        var c = await _f.TohumluEditorAsync();
        var r = await c.PostAsJsonAsync("/api/kasasayimlari", new { tarih = "2026-09-24", sayilanTutar = tutar });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Cok_uzun_not_400()
    {
        var c = await _f.TohumluEditorAsync();
        var r = await c.PostAsJsonAsync("/api/kasasayimlari", new { tarih = "2026-09-24", sayilanTutar = 1m, not = new string('x', 1001) });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Izleyici_gorur_ama_giremez_silemez()
    {
        await _f.TohumluEditorAsync();
        var izleyici = await _f.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/kasasayimlari")).StatusCode);
        Assert.Equal(10_900m, await HesaplaAsync(izleyici, "2026-09-24"));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await izleyici.PostAsJsonAsync("/api/kasasayimlari", new { tarih = "2026-09-24", sayilanTutar = 10_900m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.DeleteAsync("/api/kasasayimlari/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/kasasayimlari")).StatusCode);
    }
}

/// <summary>Sayım kaydı, geçmiş, anlık görüntü ve silme (veriyi değiştiren testler; ayrı DB).</summary>
public class KasaSayimKayitTests : IClassFixture<KasaSayimFactory>
{
    private readonly KasaSayimFactory _f;
    public KasaSayimKayitTests(KasaSayimFactory f) => _f = f;

    private static async Task<KasaSayimY> KaydetAsync(HttpClient c, object govde)
    {
        var r = await c.PostAsJsonAsync("/api/kasasayimlari", govde);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<KasaSayimY>(KasaSayimFactory.Json))!;
    }

    [Fact]
    public async Task Kayit_gecmis_anlik_goruntu_ve_silme()
    {
        var c = await _f.TohumluEditorAsync();
        var panelOnce = await c.GetStringAsync("/api/rapor/panel");
        var haftalikOnce = await c.GetStringAsync("/api/rapor/haftalik");

        var s1 = await KaydetAsync(c, new { tarih = "2026-09-22", sayilanTutar = 11_550.50m, not = "  Akşam sayımı  " });
        Assert.Equal(new DateOnly(2026, 9, 22), s1.Tarih);
        Assert.Equal(11_600m, s1.HesaplananTutar);
        Assert.Equal(-49.50m, s1.Fark);
        Assert.Equal(11_600m, s1.GuncelHesaplanan);
        Assert.Equal("Akşam sayımı", s1.Not);
        Assert.Equal(new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), s1.KayitZamaniUtc);

        var s2 = await KaydetAsync(c, new { tarih = "2026-09-24", sayilanTutar = 10_900m });
        Assert.Equal(0m, s2.Fark);
        Assert.Null(s2.Not);
        var s0 = await KaydetAsync(c, new { tarih = "2026-07-01", sayilanTutar = 10_000m, not = "Açılış" });

        // Sayım hiçbir kasa rakamını değiştirmez.
        Assert.Equal(panelOnce, await c.GetStringAsync("/api/rapor/panel"));
        Assert.Equal(haftalikOnce, await c.GetStringAsync("/api/rapor/haftalik"));

        // Geçmiş: en yeni tarih önce.
        var liste = (await c.GetFromJsonAsync<List<KasaSayimY>>("/api/kasasayimlari", KasaSayimFactory.Json))!;
        Assert.Equal([s2.Id, s1.Id, s0.Id], liste.Select(s => s.Id));
        Assert.All(liste, s => Assert.Equal(s.SayilanTutar - s.HesaplananTutar, s.Fark));
        Assert.All(liste, s => Assert.Equal(s.HesaplananTutar, s.GuncelHesaplanan));

        // Geçmiş sonradan düzeltilir (20 Eyl'e 100'lük gider): anlık görüntü aynı kalır, güncel değer değişir.
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-09-20", cari = "X", tutarTl = 100m, kanal = "MEZAT", tip = "Cari" }, KasaSayimFactory.Json))
            .EnsureSuccessStatusCode();
        liste = (await c.GetFromJsonAsync<List<KasaSayimY>>("/api/kasasayimlari", KasaSayimFactory.Json))!;
        var d1 = liste.Single(s => s.Id == s1.Id);
        Assert.Equal(11_600m, d1.HesaplananTutar);
        Assert.Equal(-49.50m, d1.Fark);
        Assert.Equal(11_500m, d1.GuncelHesaplanan);
        Assert.Equal(10_800m, liste.Single(s => s.Id == s2.Id).GuncelHesaplanan);
        Assert.Equal(10_000m, liste.Single(s => s.Id == s0.Id).GuncelHesaplanan);   // öncesi etkilenmez

        // Takip başlangıcı ileri alınınca takvim dışında kalan sayımın güncel değeri yok (null).
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 7, 2), kasaAcilis: 10_000m);
        liste = (await c.GetFromJsonAsync<List<KasaSayimY>>("/api/kasasayimlari", KasaSayimFactory.Json))!;
        Assert.Null(liste.Single(s => s.Id == s0.Id).GuncelHesaplanan);
        Assert.Equal(10_000m, liste.Single(s => s.Id == s0.Id).HesaplananTutar);
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 7, 1), kasaAcilis: 10_000m);

        // Silme
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kasasayimlari/{s1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/kasasayimlari/{s1.Id}")).StatusCode);
        liste = (await c.GetFromJsonAsync<List<KasaSayimY>>("/api/kasasayimlari", KasaSayimFactory.Json))!;
        Assert.DoesNotContain(liste, s => s.Id == s1.Id);
    }
}

/// <summary>
/// Çoklu tarih hızlı yolu, üretilmiş geniş veride (takip ay ortasında başlar; her ay kartsız K.K,
/// kartlı K.K, kart ödemesi, sabit gider, Ortak gider, gelen) her gün tek tarih hesabıyla aynı.
/// </summary>
public class KasaSayimEsdegerlikTests : IClassFixture<KasaSayimFactory>
{
    private readonly KasaSayimFactory _f;
    public KasaSayimEsdegerlikTests(KasaSayimFactory f) => _f = f;

    [Fact]
    public void Uretilmis_veride_her_gun_coklu_ve_tekli_ayni()
    {
        var takip = new DateOnly(2025, 11, 12);
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var ayar = db.Ayarlar.First();
        ayar.TakipBaslangic = takip;
        ayar.KasaAcilisDevri = 1_234.56m;
        var kart = new KrediKartiEntity { Ad = "Kart", KesimTarihi = new(2025, 11, 5), SonOdemeTarihi = new(2025, 11, 15) };
        db.KrediKartlari.Add(kart);
        db.SaveChanges();

        var rnd = new Random(42);
        var kanallar = db.Kanallar.Select(k => k.Ad).ToList();
        decimal Tutar() => rnd.Next(1, 500_000) / 100m;
        for (var d = new DateOnly(2025, 10, 1); d <= new DateOnly(2026, 10, 10); d = d.AddDays(1))
        {
            if (rnd.Next(3) == 0)
                db.Islemler.Add(new IslemEntity
                {
                    Tarih = d, Cari = "X", TutarTl = Tutar(),
                    Kanal = rnd.Next(5) == 0 ? Kasa.Core.Kanallar.Ortak : kanallar[rnd.Next(kanallar.Count)],
                    Tip = (Kasa.Core.GiderTipi)rnd.Next(3),
                    KrediKartiId = rnd.Next(4) == 0 ? kart.Id : null,
                });
            if (rnd.Next(9) == 0) db.KartOdemeler.Add(new KartOdemeEntity { KrediKartiId = kart.Id, Tarih = d, Tutar = Tutar() });
        }
        foreach (var donem in Kasa.Core.DonemUretici.Uret(takip, KasaSayimFactory.Bugun))
            foreach (var k in kanallar)
                if (rnd.Next(2) == 0)
                    db.Gelenler.Add(new GelenEntity { DonemStart = donem.Start, Kanal = k, TutarTl = Tutar() });
        db.SaveChanges();

        var svc = scope.ServiceProvider.GetRequiredService<HesapServisi>();
        var gunler = Enumerable.Range(0, KasaSayimFactory.Bugun.DayNumber - takip.DayNumber + 1).Select(takip.AddDays).ToList();
        var coklu = svc.KasaTarihlerde(gunler.Prepend(takip.AddDays(-1)));
        Assert.Equal(gunler.Count, coklu.Count);
        foreach (var g in gunler)
            Assert.True(coklu[g] == svc.KasaTarihte(g), $"{g}: çoklu {coklu[g]} ≠ tekli {svc.KasaTarihte(g)}");
        Assert.Equal(svc.Panel().GuncelKasa, coklu[KasaSayimFactory.Bugun]);
        // Dönem sonu günlerinde değer, haftalık rapordaki o dönemin kasa devridir.
        foreach (var h in svc.Haftalik())
            Assert.Equal(h.KasaDevir, coklu[h.Donem.End]);
    }
}

/// <summary>Var olan (sayım tablosu olmayan) DB'ye tablo otomatik eklenir.</summary>
public class KasaSayimSemaTests
{
    [Fact]
    public void Eski_db_ye_kasa_sayimlari_tablosu_eklenir()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE \"KasaSayimlari\"";
            cmd.ExecuteNonQuery();
        }

        var yapilan = SemaGuncelleyici.Guncelle(db, NullLogger.Instance);

        Assert.Contains("tablo+ KasaSayimlari", yapilan);
        db.KasaSayimlari.Add(new KasaSayimEntity { Tarih = new DateOnly(2026, 9, 24), SayilanTutar = 1.5m, HesaplananTutar = 2m, KayitZamaniUtc = DateTime.UtcNow });
        db.SaveChanges();
        Assert.Equal(1.5m, db.KasaSayimlari.AsNoTracking().Single().SayilanTutar);
        Assert.Empty(SemaGuncelleyici.Guncelle(db, NullLogger.Instance));   // idempotent
    }
}
