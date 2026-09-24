using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>
/// Gelen tablosu (dönemin bütün kanalları), eksik gelen listesi ve gelen kaydında iyimser koruma
/// (<c>beklenenTutar</c> → 409). Bugün = 24 Eylül 2026 Perşembe; takip 1 Ağustos 2026 Cumartesi.
/// </summary>
public class GelenTablosuApiTests : IClassFixture<GelenTablosuApiTests.SabitSaatFactory>
{
    public class SabitSaatFactory : KasaWebFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SabitSaat())));
        }
    }

    private sealed class SabitSaat : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
    }

    private record Hucre(string Kanal, bool Aktif, decimal? TutarTl, int? GelenId);
    private record Tablo(DateOnly DonemStart, DateOnly DonemEnd, DateOnly? Onceki, DateOnly? Sonraki, List<Hucre> Satirlar);
    private record Eksik(DateOnly DonemStart, DateOnly DonemEnd, string Kanal);
    private record GelenYanit(int Id, DateOnly DonemStart, string Kanal, decimal TutarTl);
    private record CakismaYanit(string Hata, decimal MevcutTutar);
    private record HataYanit(string Hata);

    private readonly SabitSaatFactory _factory;
    public GelenTablosuApiTests(SabitSaatFactory factory) => _factory = factory;

    private async Task<HttpClient> HazirlaAsync()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 8, 1));
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<KasaDbContext>().Gelenler.ExecuteDelete();
        return c;
    }

    private static Task<HttpResponseMessage> GelenYaz(HttpClient c, string donem, string kanal, decimal tutar, decimal? beklenen = null)
        => beklenen is null
            ? c.PutAsJsonAsync("/api/gelenler", new { donemStart = donem, kanal, tutarTl = tutar })
            : c.PutAsJsonAsync("/api/gelenler", new { donemStart = donem, kanal, tutarTl = tutar, beklenenTutar = beklenen });

    [Fact]
    public async Task Tablo_varsayilan_olarak_bugunun_donemini_ve_aktif_kanallari_verir()
    {
        var c = await HazirlaAsync();
        (await GelenYaz(c, "2026-09-22", "PERAKENDE", 1_250.5m)).EnsureSuccessStatusCode();

        var t = (await c.GetFromJsonAsync<Tablo>("/api/gelenler/tablo"))!;
        Assert.Equal((new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27)), (t.DonemStart, t.DonemEnd));
        Assert.Equal(new DateOnly(2026, 9, 14), t.Onceki);
        Assert.Null(t.Sonraki); // bugünün dönemi son dönem
        var aktifler = t.Satirlar.Where(s => s.Aktif).Select(s => s.Kanal).ToList();
        Assert.Equal(["MEZAT", "PERAKENDE", "TOPTAN"], aktifler.Take(3));
        var p = t.Satirlar.Single(s => s.Kanal == "PERAKENDE");
        Assert.Equal(1_250.5m, p.TutarTl);
        Assert.NotNull(p.GelenId);
        Assert.Null(t.Satirlar.Single(s => s.Kanal == "MEZAT").TutarTl);
    }

    [Theory]
    [InlineData("2026-08-01", "2026-08-01", "2026-08-02", null, "2026-08-03")]      // takip başı (Cumartesi)
    [InlineData("2026-09-02", "2026-09-01", "2026-09-06", "2026-08-31", "2026-09-07")] // ay başı; tarih dönem başına çekilir
    [InlineData("2026-08-31", "2026-08-31", "2026-08-31", "2026-08-24", "2026-09-01")] // tek günlük ay sonu dönemi
    public async Task Tablo_donem_sinirlarini_ve_gezinmeyi_dogru_verir(string sorgu, string bas, string son, string? onceki, string? sonraki)
    {
        var c = await HazirlaAsync();
        var t = (await c.GetFromJsonAsync<Tablo>("/api/gelenler/tablo?donemStart=" + sorgu))!;
        Assert.Equal(DateOnly.Parse(bas), t.DonemStart);
        Assert.Equal(DateOnly.Parse(son), t.DonemEnd);
        Assert.Equal(onceki is null ? null : DateOnly.Parse(onceki), t.Onceki);
        Assert.Equal(sonraki is null ? null : DateOnly.Parse(sonraki), t.Sonraki);
    }

    [Theory]
    [InlineData("2026-07-31")]
    [InlineData("2026-09-25")]
    public async Task Tablo_takvim_disindaki_tarihi_reddeder(string tarih)
    {
        var c = await HazirlaAsync();
        var r = await c.GetAsync("/api/gelenler/tablo?donemStart=" + tarih);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("takip dönemlerinin dışında", (await r.Content.ReadFromJsonAsync<HataYanit>())!.Hata);
    }

    [Fact]
    public async Task Pasif_kanal_yalniz_o_donemde_geleni_varsa_gorunur()
    {
        var c = await HazirlaAsync();
        var k = await (await c.PostAsJsonAsync("/api/kanallar", new { ad = "ESKİ ŞUBE", aktif = true, sira = 50 })).Content.ReadFromJsonAsync<JsonElement>();
        var id = k.GetProperty("id").GetInt32();
        (await GelenYaz(c, "2026-09-07", "ESKİ ŞUBE", 10m)).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "ESKİ ŞUBE", aktif = false, sira = 50 })).EnsureSuccessStatusCode();

        var bu = (await c.GetFromJsonAsync<Tablo>("/api/gelenler/tablo?donemStart=2026-09-07"))!;
        var s = bu.Satirlar.Single(x => x.Kanal == "ESKİ ŞUBE");
        Assert.False(s.Aktif);
        Assert.Equal(10m, s.TutarTl);
        var baska = (await c.GetFromJsonAsync<Tablo>("/api/gelenler/tablo?donemStart=2026-09-14"))!;
        Assert.DoesNotContain(baska.Satirlar, x => x.Kanal == "ESKİ ŞUBE");
    }

    [Fact]
    public async Task Eksik_gelenler_bitmis_donemlerdeki_girilmemis_kanallari_en_yeni_once_listeler()
    {
        var c = await HazirlaAsync();
        (await GelenYaz(c, "2026-09-14", "MEZAT", 0m)).EnsureSuccessStatusCode();   // 0 da girilmiş sayılır
        (await GelenYaz(c, "2026-08-03", "MEZAT", 500m)).EnsureSuccessStatusCode();
        (await GelenYaz(c, "2026-08-01", "PERAKENDE", 100m)).EnsureSuccessStatusCode();
        // Takipten sonra (bugün) eklenen kanal geçmiş haftalar için eksik sayılmaz.
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/kanallar", new { ad = "YENİ ŞUBE", aktif = true, sira = 60 })).StatusCode);

        var r = await c.GetAsync("/api/gelenler/eksik-liste");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var l = (await r.Content.ReadFromJsonAsync<List<Eksik>>())!;
        Assert.Equal(l.Count.ToString(), r.Headers.GetValues("X-Toplam-Kayit").Single());
        Assert.DoesNotContain(l, e => e.Kanal == "YENİ ŞUBE");
        Assert.DoesNotContain(l, e => e.DonemStart == new DateOnly(2026, 9, 21)); // bitmemiş dönem
        Assert.DoesNotContain(l, e => e.Kanal == "MEZAT" && e.DonemStart == new DateOnly(2026, 9, 14));
        Assert.DoesNotContain(l, e => e.Kanal == "MEZAT" && e.DonemStart == new DateOnly(2026, 8, 3));
        // Kurulumla gelen kanalların "Eklendi" satırı yok: başlangıç ilk hareketleri. MEZAT'ın ilk geleni
        // 3 Ağustos haftasında: takip başındaki 1–2 Ağustos dönemi onun için sayılmaz; PERAKENDE'ninki sayılır.
        Assert.DoesNotContain(l, e => e.Kanal == "MEZAT" && e.DonemStart == new DateOnly(2026, 8, 1));
        Assert.Contains(l, e => e.Kanal == "PERAKENDE" && e.DonemStart == new DateOnly(2026, 8, 3) && e.DonemEnd == new DateOnly(2026, 8, 9));
        Assert.Equal(new DateOnly(2026, 9, 14), l[0].DonemStart);
        Assert.Equal(l.OrderByDescending(e => e.DonemStart).Select(e => e.DonemStart), l.Select(e => e.DonemStart));
        // 9 bitmiş dönem: MEZAT 8 dönemde var (2 girilmiş), PERAKENDE 9 dönemde (1 girilmiş). TOPTAN'ın
        // ne geçmişi ne hareketi var: başlangıcı bilinmez, listelenmez.
        Assert.Equal(8 - 2, l.Count(e => e.Kanal == "MEZAT"));
        Assert.Equal(9 - 1, l.Count(e => e.Kanal == "PERAKENDE"));
        Assert.DoesNotContain(l, e => e.Kanal == "TOPTAN");

        var izleyiciSifre = await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" });
        izleyiciSifre.EnsureSuccessStatusCode();
        var izleyici = _factory.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/gelenler/eksik-liste")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/gelenler/tablo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/gelenler/eksik-liste")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/gelenler/tablo")).StatusCode);
    }

    [Fact]
    public async Task Eksik_gelende_gecmisi_silinmis_kanal_ilk_hareketinden_onceki_haftalarda_sayilmaz()
    {
        var c = await HazirlaAsync();
        var k = await (await c.PostAsJsonAsync("/api/kanallar", new { ad = "SONRA ŞUBE", aktif = true, sira = 70 })).Content.ReadFromJsonAsync<JsonElement>();
        var id = k.GetProperty("id").GetInt32();
        using (var scope = _factory.Services.CreateScope())
        {
            // 2 yıllık saklama sınırı (ya da geçmişten önce eklenmiş kanal): "Eklendi" satırı yok.
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.True(db.Degisiklikler.Where(d => d.Tur == GecmisTurleri.Kanal && d.KayitId == id).ExecuteDelete() > 0);
        }
        var l = (await c.GetFromJsonAsync<List<Eksik>>("/api/gelenler/eksik-liste"))!;
        Assert.DoesNotContain(l, e => e.Kanal == "SONRA ŞUBE");       // başlangıç bilinmiyor: hiç sayılmaz

        // İlk hareketi (7 Eylül haftasının geleni) başlangıç olur: yalnız sonraki bitmiş dönem eksik.
        (await GelenYaz(c, "2026-09-08", "SONRA ŞUBE", 10m)).EnsureSuccessStatusCode();
        l = (await c.GetFromJsonAsync<List<Eksik>>("/api/gelenler/eksik-liste"))!;
        Assert.Equal([new DateOnly(2026, 9, 14)], l.Where(e => e.Kanal == "SONRA ŞUBE").Select(e => e.DonemStart));

        (await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "SONRA ŞUBE", aktif = false, sira = 70 })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Eksik_gelende_pasif_kalinan_donemler_sayilmaz_islem_de_baslangictir()
    {
        var c = await HazirlaAsync();
        int id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var kanal = new KanalEntity { Ad = "MEVSİMLİK", Aktif = true, Sira = 80 };
            db.Kanallar.Add(kanal);
            db.SaveChanges();                                           // rol yok: geçmiş yazılmaz
            id = kanal.Id;
            DegisiklikEntity Satir(DateTime zaman, string eylem, string? eski, string? yeni) => new()
            {
                ZamanUtc = zaman, Rol = "editor", Tur = GecmisTurleri.Kanal, KayitId = id, Eylem = eylem,
                Ozet = "test", EskiJson = eski, YeniJson = yeni,
            };
            db.Degisiklikler.AddRange(
                Satir(new DateTime(2026, 8, 1, 7, 0, 0, DateTimeKind.Utc), Eylemler.Eklendi, null, "{\"ad\":\"MEVSİMLİK\",\"aktif\":true}"),
                Satir(new DateTime(2026, 8, 10, 7, 0, 0, DateTimeKind.Utc), Eylemler.Guncellendi, "{\"aktif\":true}", "{\"aktif\":false}"),
                Satir(new DateTime(2026, 8, 20, 7, 0, 0, DateTimeKind.Utc), Eylemler.Guncellendi, "{\"aktif\":false,\"sira\":80}", "{\"aktif\":false,\"sira\":81}"),
                Satir(new DateTime(2026, 8, 25, 7, 0, 0, DateTimeKind.Utc), Eylemler.Guncellendi, "bozuk", "{\"aktif\":true}"),
                Satir(new DateTime(2026, 9, 16, 7, 0, 0, DateTimeKind.Utc), Eylemler.Guncellendi, "{\"aktif\":false}", "{\"aktif\":true}"));
            db.SaveChanges();
        }

        var l = (await c.GetFromJsonAsync<List<Eksik>>("/api/gelenler/eksik-liste"))!;
        // Aktif: 1–9 Ağustos, 10 Ağustos (o gün pasif oldu), 16 Eylül'den sonra. Arası pasif: sayılmaz.
        Assert.Equal(
            [new DateOnly(2026, 9, 14), new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 1)],
            l.Where(e => e.Kanal == "MEVSİMLİK").Select(e => e.DonemStart));

        // Geçmişsiz kanalda ilk işlem de başlangıçtır.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Kanallar.Add(new KanalEntity { Ad = "İŞLEMLİ", Aktif = true, Sira = 81 });
            db.Cariler.Add(new CariEntity { Ad = "Eksik Test Carisi" });
            db.SaveChanges();
        }
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-09-02", cari = "Eksik Test Carisi", tutarTl = 5m, kanal = "İŞLEMLİ", tip = "Cari" })).EnsureSuccessStatusCode();
        l = (await c.GetFromJsonAsync<List<Eksik>>("/api/gelenler/eksik-liste"))!;
        Assert.Equal([new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 1)],
            l.Where(e => e.Kanal == "İŞLEMLİ").Select(e => e.DonemStart));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Kanallar.Where(k => k.Ad == "MEVSİMLİK" || k.Ad == "İŞLEMLİ").ExecuteUpdate(u => u.SetProperty(k => k.Aktif, false));
        }
    }

    [Fact]
    public async Task Gelen_kaydi_beklenen_tutar_farkliysa_409_doner_eski_istek_sekli_calisir()
    {
        var c = await HazirlaAsync();
        // Kayıt yokken beklenen 0: yazılır.
        var r1 = await GelenYaz(c, "2026-09-15", "TOPTAN", 100m, beklenen: 0m);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(new DateOnly(2026, 9, 14), (await r1.Content.ReadFromJsonAsync<GelenYanit>())!.DonemStart);

        // Başka biri 100 yazdı; bu istemci hâlâ 0 görüyor: 409 + mevcut tutar, kayıt değişmez.
        var r2 = await GelenYaz(c, "2026-09-14", "TOPTAN", 50m, beklenen: 0m);
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        var h = (await r2.Content.ReadFromJsonAsync<CakismaYanit>())!;
        Assert.Equal(100m, h.MevcutTutar);
        Assert.Contains("100,00 ₺", h.Hata);

        // Üstüne ekle: mevcut + girilen, beklenen = mevcut.
        Assert.Equal(HttpStatusCode.OK, (await GelenYaz(c, "2026-09-14", "TOPTAN", 150m, beklenen: 100m)).StatusCode);
        // Eski istek şekli (beklenenTutar yok): koşulsuz upsert.
        var r4 = await GelenYaz(c, "2026-09-14", "TOPTAN", 75m);
        Assert.Equal(HttpStatusCode.OK, r4.StatusCode);
        Assert.Equal(75m, (await r4.Content.ReadFromJsonAsync<GelenYanit>())!.TutarTl);
        // Kayıt yokken beklenen 5: çakışma (mevcut 0).
        var r5 = await GelenYaz(c, "2026-09-14", "MEZAT", 5m, beklenen: 5m);
        Assert.Equal(HttpStatusCode.Conflict, r5.StatusCode);
        Assert.Equal(0m, (await r5.Content.ReadFromJsonAsync<CakismaYanit>())!.MevcutTutar);
        var gelenler = (await c.GetFromJsonAsync<List<GelenYanit>>("/api/gelenler?donemStart=2026-09-14"))!;
        Assert.DoesNotContain(gelenler, g => g.Kanal == "MEZAT");
        // Doğrulama hataları 409'dan önce gelir.
        Assert.Equal(HttpStatusCode.BadRequest, (await GelenYaz(c, "2026-09-14", "YOK", 5m, beklenen: 1m)).StatusCode);
    }
}
