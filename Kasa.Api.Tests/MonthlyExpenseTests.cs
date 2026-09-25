using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

public class MonthlyExpenseTests
{
    internal static DateOnly Today => FinansTakipServisi.Bugun;
    internal static DateOnly Month => new(Today.Year, Today.Month, 1);

    [Theory]
    [InlineData("Genel", 0)] [InlineData("Esit", 1)] [InlineData("Ozel", 2)]
    public async Task Plan_kasayi_degistirmez_manuel_odeme_genel_kasaya_bir_kez_kanallara_secilen_payla_yansir(string mode, int variant)
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var template = await Create(c, mode, variant == 0 ? [] : variant == 1 ? [new(1, 0), new(2, 0), new(3, 0)] : [new(1, 70m), new(2, 30m)]);
        var plan = (await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Today.Year}&ay={Today.Month}"))!;
        Assert.Equal(100m, plan.PlanlananToplam); Assert.Equal(0m, plan.OdenenToplam);
        Assert.Equal(1000m, (await Panel(c)).GuncelKasa);
        var request = Payment(template);
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", request);
        Assert.Equal("Odendi", paid.Durum);
        var result = await Panel(c); Assert.Equal(900m, result.GuncelKasa); Assert.Equal(-100m, result.BuAySonucu); Assert.Equal(0m, result.DagilimBekleyenTutar);
        Assert.Equal(variant == 0 ? 0m : -100m, result.Kanallar.Sum(k => k.Bakiye));
        if (variant == 1) Assert.Equal(new[] { -33.34m, -33.33m, -33.33m }, result.Kanallar.Select(k => k.Bakiye));
        if (variant == 2) Assert.Equal(-70m, result.Kanallar.Single(k => k.KanalId == 1).Bakiye);
        var replay = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", request);
        Assert.Equal(paid.OdemeId, replay.OdemeId); Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/aylik-giderler/{template.Id}/ode", request with { IstekId = Guid.NewGuid() })).StatusCode);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web); json.Converters.Add(new JsonStringEnumConverter());
        var expense = Assert.Single((await c.GetFromJsonAsync<List<IslemOkuDto>>("/api/islemler", json))!);
        var paidMonth = (await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Today.Year}&ay={Today.Month}"))!;
        Assert.Equal(100m, paidMonth.PlanlananToplam); Assert.Equal(100m, paidMonth.OdenenToplam);
        Assert.Equal(paid.OdemeId, expense.AylikGiderOdemeId);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/islemler/{expense.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/islemler/{expense.Id}", new IslemYazDto(Today, "Değişiklik", 200, "MEZAT", GiderTipi.SabitGider))).StatusCode);
        Assert.Equal(900m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Sablon_degisse_ve_pasife_alinsa_bile_odeme_kopyasi_korunur_iptal_yeniden_odeme_tek_kaydi_yaratir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var template = await Create(c, "Ozel", [new(1, 100m)]);
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", Payment(template));
        var updated = await c.PutAsJsonAsync($"/api/aylik-giderler/sablonlar/{template.Id}", new AylikGiderSablonYaz(Guid.NewGuid(), template.Surum, "Yeni kira", "Kira", 200m, 31, "Genel", [], Month));
        updated.EnsureSuccessStatusCode();
        template = (await updated.Content.ReadFromJsonAsync<AylikGiderSablonDto>())!;
        var old = Assert.Single((await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Month.Year}&ay={Month.Month}"))!.Kayitlar);
        Assert.Equal(100m, old.Tutar); Assert.Equal("Kira", old.Ad); Assert.Equal(1, Assert.Single(old.Dagilimlar).KanalId);
        var cancelled = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/odemeler/{paid.OdemeId}/iptal", new AylikGiderIptalYaz(Guid.NewGuid(), "Hatalı ödeme"));
        Assert.Equal("Iptal", cancelled.Durum); Assert.Equal(1000m, (await Panel(c)).GuncelKasa);
        var next = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", Payment(template));
        Assert.Equal(200m, next.Tutar); Assert.NotEqual(paid.OdemeId, next.OdemeId); Assert.Equal(800m, (await Panel(c)).GuncelKasa);
        var archive = await c.PutAsJsonAsync($"/api/aylik-giderler/sablonlar/{template.Id}", new AylikGiderSablonYaz(Guid.NewGuid(), template.Surum, "Arşiv", "Kira", 300m, 1, "Genel", [], Month, false));
        archive.EnsureSuccessStatusCode();
        Assert.Equal(200m, Assert.Single((await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Month.Year}&ay={Month.Month}"))!.Kayitlar).Tutar);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(2, db.AylikGiderOdemeler.Count()); Assert.Single(db.Islemler); Assert.Equal(3, db.AylikGiderRevizyonlar.Count());
    }

    [Fact]
    public async Task Ileri_ay_revizyonu_onceki_planlari_degistirmez_ve_kisa_ay_odemesi_son_gune_uyarlanir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var template = await Create(c, "Genel", []);
        var nextMonth = Month.AddMonths(1);
        var update = await c.PutAsJsonAsync($"/api/aylik-giderler/sablonlar/{template.Id}", new AylikGiderSablonYaz(Guid.NewGuid(), template.Surum, "Yeni", "Maas", 250m, 31, "Genel", [], nextMonth));
        update.EnsureSuccessStatusCode();
        Assert.Equal(100m, Assert.Single((await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Month.Year}&ay={Month.Month}"))!.Kayitlar).Tutar);
        var future = Assert.Single((await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={nextMonth.Year}&ay={nextMonth.Month}"))!.Kayitlar);
        Assert.Equal(250m, future.Tutar); Assert.Equal(DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month), future.PlanlananTarih.Day);
        Assert.Equal(1000m, (await Panel(c)).GuncelKasa);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Eski", "Kira", 100m, 1, "Genel", [], Month.AddMonths(-1)))).StatusCode);
    }

    [Fact]
    public async Task Dagilim_kimlikleri_sabittir_yeni_kanal_eklemek_ve_pasiflik_odeme_paylarini_degistirmez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var template = await Create(c, "Esit", [new(1, 0), new(2, 0)]);
        (await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", false))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("Yeni kanal"))).EnsureSuccessStatusCode();
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", Payment(template));
        Assert.Equal(new[] { 1, 2 }, paid.Dagilimlar.Select(p => p.KanalId!.Value));
        Assert.All(paid.Dagilimlar, p => Assert.Equal(50m, p.Tutar));
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync("/api/kanallar/2")).StatusCode);
    }

    [Fact]
    public async Task Aylik_odeme_ayri_alisa_baglanamaz_ve_alici_erisemez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var t = await Create(c, "Genel", []); var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{t.Id}/ode", Payment(t));
        var purchase = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Satıcı", null, [new("Mal", 100, [new(1, 100)])]));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/alis/{purchase.Id}/odemeler", new AlisOdemeYaz(purchase.Surum, Guid.NewGuid(), Today, 100, MevcutIslemId: paid.IslemId))).StatusCode);
        var buyer = await Post<AliciDto>(c, "/api/alicilar", new AliciYaz("aylik-alici", "Alıcı", "alici12345"));
        using var b = f.CreateClient(); (await b.PostAsJsonAsync("/api/auth/login", new { kullanici = buyer.Kullanici, sifre = "alici12345" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/aylik-giderler/sablonlar")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/ay-kilidi")).StatusCode);
    }

    [Fact]
    public async Task Ayni_odeme_eszamanli_farkli_baglantilardan_tekrarlansa_bir_nakit_cikisi_olusur()
    {
        var path = Path.Combine(Path.GetTempPath(), "kasa-monthly-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var f = new MonthlyFileFactory(path); using var c = await Editor(f);
            var t = await Create(c, "Genel", []); var request = Payment(t);
            using var start = new Barrier(3);
            var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            { Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10))); return await c.PostAsJsonAsync($"/api/aylik-giderler/{t.Id}/ode", request); })));
            foreach (var r in results) { Assert.Equal(HttpStatusCode.OK, r.StatusCode); r.Dispose(); }
            using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Single(db.AylikGiderOdemeler); Assert.Single(db.Islemler); Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        }
        finally { foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + suffix); }
    }

    internal sealed class MonthlyFileFactory(string path) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString()));
            });
        }
    }
    internal static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Month.AddMonths(-3), kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode(); return c;
    }
    internal static Task<AylikGiderSablonDto> Create(HttpClient c, string mode, IReadOnlyList<KanalPayYaz> shares) => Post<AylikGiderSablonDto>(c, "/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Kira", "Kira", 100m, 31, mode, shares, Month));
    internal static AylikGiderOdemeYaz Payment(AylikGiderSablonDto t) => new(Guid.NewGuid(), t.Surum, Month.Year, Month.Month, Today);
    internal static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body); Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web); json.Converters.Add(new JsonStringEnumConverter());
        return (await r.Content.ReadFromJsonAsync<T>(json))!;
    }
    internal static async Task<PanelDto> Panel(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
}
