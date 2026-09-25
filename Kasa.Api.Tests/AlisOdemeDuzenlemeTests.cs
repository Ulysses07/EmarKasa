using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class AlisOdemeDuzenlemeTests
{
    private static readonly DateOnly Date = new(2026, 9, 1);

    [Fact]
    public async Task Odeme_duzelt_tasi_iptal_tekrar_guvenlidir_belge_ve_mali_kayit_birlikte_korunur()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync(); await AlisWorkflowTests.Prepare(c);
        var source = await Purchase(c, "Kaynak", 100m); var target = await Purchase(c, "Hedef", 200m);
        var create = new AlisOdemeYaz(source.Surum, Guid.NewGuid(), Date, 40m);
        source = await Read<AlisDto>(await c.PostAsJsonAsync($"/api/alis/{source.Id}/odemeler", create));
        var payment = source.Odemeler.Single();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Belgeler.Add(new BelgeEntity { AlisId = source.Id, OdemeId = payment.Id, DosyaAdi = "test.pdf", IcerikTuru = "application/pdf", Icerik = [1], Boyut = 1 }); db.SaveChanges();
        }
        var change = new AlisOdemeDuzelt(source.Surum, Guid.NewGuid(), Date, 70m, "Yanlış alış ve tutar düzeltildi", HedefAlisId: target.Id, HedefSurum: target.Surum);
        var emptied = await Read<AlisDto>(await c.PutAsJsonAsync($"/api/alis/{source.Id}/odemeler/{payment.Id}", change));
        Assert.Equal(0m, emptied.Odenen);
        var replay = await Read<AlisDto>(await c.PutAsJsonAsync($"/api/alis/{source.Id}/odemeler/{payment.Id}", change with { Surum = 999, HedefSurum = 999 }));
        Assert.Equal(emptied.Surum, replay.Surum);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/alis/{source.Id}/odemeler/{payment.Id}", change with { Tutar = 71m })).StatusCode);
        target = (await c.GetFromJsonAsync<List<AlisDto>>("/api/alis"))!.Single(a => a.Id == target.Id);
        Assert.Equal(70m, target.Odenen); Assert.Equal(930m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>(); Assert.Equal(target.Id, db.Belgeler.Single().AlisId); Assert.Single(db.Islemler);
        }
        var cancel = new AlisOdemeIptal(target.Surum, Guid.NewGuid(), "Ödeme gerçekleşmedi");
        var canceled = await Read<AlisDto>(await c.PostAsJsonAsync($"/api/alis/{target.Id}/odemeler/{payment.Id}/iptal", cancel));
        Assert.Equal(0m, canceled.Odenen); Assert.Empty(canceled.Odemeler);
        var canceledAgain = await Read<AlisDto>(await c.PostAsJsonAsync($"/api/alis/{target.Id}/odemeler/{payment.Id}/iptal", cancel));
        Assert.Equal(canceled.Surum, canceledAgain.Surum);
        await Read<AlisDto>(await c.PostAsJsonAsync($"/api/alis/{source.Id}/odemeler", create));
        Assert.Equal(1000m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
        using var finalScope = f.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Empty(finalDb.Islemler); Assert.Empty(finalDb.AlisOdemeler); Assert.Null(finalDb.Belgeler.Single().OdemeId);
        Assert.Equal(3, finalDb.FinansIstekler.Count());
        using var stored = System.Text.Json.JsonDocument.Parse(finalDb.FinansIstekler.Single(x => x.Tur == "OdemeIptal").OncekiJson!);
        Assert.Equal("Ödeme gerçekleşmedi", stored.RootElement.GetProperty("aciklama").GetString());
    }

    [Fact]
    public async Task Duzeltmede_eski_surum_fazla_odeme_ve_bos_aciklama_mali_kaydi_degistirmez()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync(); await AlisWorkflowTests.Prepare(c);
        var a = await Purchase(c, "Firma", 100m);
        a = await Read<AlisDto>(await c.PostAsJsonAsync($"/api/alis/{a.Id}/odemeler", new AlisOdemeYaz(a.Surum, Guid.NewGuid(), Date, 30m)));
        var o = a.Odemeler.Single(); var change = new AlisOdemeDuzelt(a.Surum, Guid.NewGuid(), Date, 120m, "Düzeltme");
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/alis/{a.Id}/odemeler/{o.Id}", change)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/alis/{a.Id}/odemeler/{o.Id}", change with { Surum = 0, Tutar = 50m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/alis/{a.Id}/odemeler/{o.Id}", change with { Tutar = 50m, Aciklama = " " })).StatusCode);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(30m, db.Islemler.Single().TutarTl); Assert.Equal(a.Surum, db.Alislar.Single().Surum); Assert.Single(db.FinansIstekler);
    }

    [Fact]
    public async Task Kart_kaydi_olmayan_eski_harcama_duzeltme_ve_tasimada_nakde_donusmez()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync(); await AlisWorkflowTests.Prepare(c);
        int expenseId;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var old = new IslemEntity { Tarih = Date, Cari = "Eski kart", TutarTl = 100m, Kanal = "Ortak", Tip = GiderTipi.KrediKarti };
            db.Islemler.Add(old); db.SaveChanges(); expenseId = old.Id;
        }
        var source = await Purchase(c, "Kaynak", 100m); var target = await Purchase(c, "Hedef", 100m);
        source = await Read<AlisDto>(await c.PostAsJsonAsync($"/api/alis/{source.Id}/odemeler", new AlisOdemeYaz(source.Surum, Guid.NewGuid(), Date, 100m, MevcutIslemId: expenseId)));
        var payment = source.Odemeler.Single();
        var change = new AlisOdemeDuzelt(source.Surum, Guid.NewGuid(), Date.AddDays(3), 100m, "Doğru alışa taşındı", HedefAlisId: target.Id, HedefSurum: target.Surum);
        var moved = await Read<AlisDto>(await c.PutAsJsonAsync($"/api/alis/{source.Id}/odemeler/{payment.Id}", change));
        Assert.Empty(moved.Odemeler);
        using var finalScope = f.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var expense = Assert.Single(finalDb.Islemler);
        Assert.Equal(GiderTipi.KrediKarti, expense.Tip); Assert.Null(expense.KrediKartiId); Assert.Equal(Date.AddDays(3), expense.Tarih);
        Assert.Equal(target.Id, finalDb.AlisOdemeler.Single().AlisId); Assert.Empty(finalDb.HesapHareketler);
        var month = (await c.GetFromJsonAsync<AylikRapor>("/api/rapor/aylik?yil=2026&ay=9"))!;
        Assert.Equal(0m, month.DagilimBekleyenTutar); Assert.All(month.Kanallar, k => Assert.Equal(0m, k.CariGiden));
    }

    [Fact]
    public async Task Alis_serbest_tedarikci_metni_cari_kaydi_uretmez_ve_mevcut_cariye_baglanmaz()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync(); await AlisWorkflowTests.Prepare(c);
        int count;
        using (var scope = f.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>(); db.Cariler.Add(new CariEntity { Ad = "Eski firma" }); db.SaveChanges(); count = db.Cariler.Count(); }
        var first = await Purchase(c, "Eski firma", 50m); var second = await Purchase(c, "Yeni serbest firma", 50m);
        Assert.Null(first.TedarikciId); Assert.Null(second.TedarikciId);
        using var check = f.Services.CreateScope(); var database = check.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(count, database.Cariler.Count()); Assert.All(database.Alislar, a => Assert.Null(a.TedarikciId));
    }

    [Theory]
    [InlineData("tedarikci")]
    [InlineData("vade")]
    [InlineData("miktar")]
    public async Task Yeni_ERP_alanlari_alis_isteginde_reddedilir(string field)
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync(); await AlisWorkflowTests.Prepare(c);
        var dto = new AlisYaz(0, Date, "Firma", null, [new("Kalem", 100m, [])]);
        dto = field switch { "tedarikci" => dto with { TedarikciId = 1 }, "vade" => dto with { Vade = Date }, _ => dto with { Kalemler = [new("Kalem", 100m, [], 1m, 100m)] } };
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/alis", dto)).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<List<AlisDto>>("/api/alis"))!);
    }

    [Fact]
    public async Task Kapsam_disi_ERP_ve_plan_uclari_yayinlanmaz()
    {
        await using var f = new KasaWebFactory(); using var c = await f.EditorClientAsync();
        foreach (var path in new[] { "/api/alis/tedarikciler", "/api/tedarikciler/borclar", "/api/tedarikciler/1/alislar", "/api/hesaplar", "/api/hesaplar/1/hareketler", "/api/is-listesi", "/api/nakit-takvimi" })
        {
            var response = await c.GetAsync(path);
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{path}: {response.StatusCode}");
        }
        foreach (var path in new[] { "/api/hesaplar", "/api/hesaplar/transferler", "/api/hesaplar/1/hareketler", "/api/krediler/1/taksitler/1/ode", "/api/krediler/1/gerceklesme-takibi" })
        {
            // Yönlendirici eşleşmeyen yöntem için 405 de verebilir; işlem erişilebilir olmamalı.
            var response = await c.PostAsJsonAsync(path, new { });
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{path}: {response.StatusCode}");
        }
    }

    private static async Task<AlisDto> Purchase(HttpClient c, string name, decimal amount) => await Read<AlisDto>(await c.PostAsJsonAsync("/api/alis", new AlisYaz(0, Date, name, null, [new("Ürün", amount, [])])));
    private static Task<T> Read<T>(HttpResponseMessage response) => AlisWorkflowTests.Read<T>(response);
}
