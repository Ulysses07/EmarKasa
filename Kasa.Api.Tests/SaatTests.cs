using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Kasa saati (tests-1/2/4): üretimde sistem saati ve İstanbul günü; testte fabrikanın sabit saati
/// sunucunun her "bugün" okumasına (tohum, doğrulama, rapor, istek dışı servis çağrısı) uygulanır.
/// İki fabrikanın saati aynı süreçte karışmaz ve istek saati test akışına sızmaz.
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
        var anOnce = TimeProvider.System.GetUtcNow(); var once = TimeProvider.System.IstanbulBugun();
        Assert.Same(TimeProvider.System, KasaSaati.Gecerli);
        Assert.InRange(KasaSaati.Simdi, anOnce, TimeProvider.System.GetUtcNow());
        var disarida = new[] { KasaSaati.Bugun, FinansTakipServisi.Bugun };
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync();
        Assert.Null(f.Saat);
        Assert.Same(TimeProvider.System, f.Services.GetRequiredService<TimeProvider>());
        var sunucu = (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet"))!.Tarih;
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
        Assert.All(disarida.Append(sunucu).Append(bagimsizBaglam), gun => Assert.InRange(gun, once, sonra));
    }

    [Theory]
    [InlineData(2021, 3, 10)] // sistem takviminden geride
    [InlineData(2091, 1, 15)] // sistem takviminden ileride
    public async Task Sabit_saatli_sunucu_tohumu_dogrulamayi_raporu_ve_istek_disi_cagrilari_ayni_gune_baglar(int yil, int ay, int gun)
    {
        var bugun = new DateOnly(yil, ay, gun);
        await using var f = KasaWebFactory.Sabit(bugun); using var c = await f.EditorClientAsync();
        Assert.Equal(bugun, f.Bugun);
        Assert.Equal(bugun, (await c.GetFromJsonAsync<Ayar>("/api/ayarlar"))!.TakipBaslangic);
        Assert.Equal(bugun, (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet"))!.Tarih);
        // Panel dönemi sunucunun gününde biter: bugünkü gider bu ayın sonucuna girer.
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(bugun, "Bugünkü gider", 5m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        Assert.Equal(-5m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.BuAySonucu);
        // Ödeme doğrulaması sunucunun gününe bakar: yarın reddedilir, bugün kabul edilir.
        var card = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Saat kartı", 1000m, 5, 25, bugun, 0, []));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, bugun.AddDays(1), 10m))).StatusCode);
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, bugun, 10m));
        // İstek dışında (arka plan işi, doğrudan servis çağrısı) bağlam fabrikanın saatini taşır.
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Same(f.Saat, db.Saati()); Assert.Equal(bugun, db.Bugunu());
        // İstek saati test akışına sızmaz.
        Assert.Same(TimeProvider.System, KasaSaati.Gecerli);
    }

    [Fact]
    public async Task Paralel_fabrikalar_birbirinin_saatini_gormez_istek_saati_cagirana_sizmaz()
    {
        DateOnly ocak = new(2027, 1, 1), subat = new(2028, 2, 29);
        await using var f1 = KasaWebFactory.Sabit(ocak); await using var f2 = KasaWebFactory.Sabit(subat);
        using var c1 = await f1.EditorClientAsync(); using var c2 = await f2.EditorClientAsync();
        // Fabrika başına istekler sıralı (tek in-memory bağlantı), iki fabrikanınkiler eşzamanlı akar.
        async Task<DateOnly[]> Oku(HttpClient c)
        {
            var gunler = new List<DateOnly>();
            for (var i = 0; i < 20; i++) gunler.Add((await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet"))!.Tarih);
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
}
