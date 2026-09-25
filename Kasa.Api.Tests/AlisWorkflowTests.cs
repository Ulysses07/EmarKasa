using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class AlisWorkflowTests
{
    private static readonly DateOnly Date = new(2026, 9, 1);

    [Fact]
    public async Task Alici_yalniz_kendi_taslagini_degistirir_finans_ve_onay_rolleri_ayrilir()
    {
        await using var factory = new KasaWebFactory();
        using var editor = await factory.EditorClientAsync();
        await Prepare(editor);
        using var buyer = await Buyer(factory, editor, "alici1");
        using var other = await Buyer(factory, editor, "alici2");
        var channels = await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar");
        var write = Draft(channels!);
        var created = await Read<AlisDto>(await buyer.PostAsJsonAsync("/api/alis", write));
        Assert.NotNull(created.AliciId);
        Assert.Equal(1, created.Surum);
        Assert.Single((await buyer.GetFromJsonAsync<List<AlisDto>>("/api/alis"))!);
        Assert.Empty((await other.GetFromJsonAsync<List<AlisDto>>("/api/alis"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/alis/{created.Id}", write with { Surum = created.Surum })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/alis/{created.Id}/gonder", new AlisDurumYaz(created.Surum))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync("/api/islemler")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsJsonAsync($"/api/alis/{created.Id}/onayla", new AlisDurumYaz(created.Surum))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", new AlisOdemeYaz(created.Surum, Guid.NewGuid(), Date, 10m))).StatusCode);

        var sent = await Read<AlisDto>(await buyer.PostAsJsonAsync($"/api/alis/{created.Id}/gonder", new AlisDurumYaz(created.Surum)));
        Assert.Equal(AlisDurumlari.Incelemede, sent.Durum);
        Assert.Equal(HttpStatusCode.Conflict, (await buyer.PutAsJsonAsync($"/api/alis/{created.Id}", write with { Surum = sent.Surum })).StatusCode);
        var edited = await Read<AlisDto>(await editor.PutAsJsonAsync($"/api/alis/{created.Id}", write with { Surum = sent.Surum, Tedarikci = "Düzeltilen firma" }));
        Assert.Equal(created.AliciId, edited.AliciId);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PutAsJsonAsync($"/api/alis/{created.Id}", write with { Surum = sent.Surum })).StatusCode);

        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici123" })).EnsureSuccessStatusCode();
        using var viewer = factory.CreateClient();
        (await viewer.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izleyici123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/alis")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/alis/kanallar")).StatusCode);
    }

    [Fact]
    public async Task Eksik_dagilim_onaylanmaz_editorde_tamamlanir_tekrar_onay_ve_iade_kurallari_korunur()
    {
        await using var factory = new KasaWebFactory();
        using var editor = await factory.EditorClientAsync();
        await Prepare(editor);
        var channels = await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar");
        var complete = Draft(channels!);
        var incomplete = complete with { Kalemler = [new("Ürün", 100m, [new(channels![0].Id, 40m)])] };
        var created = await Read<AlisDto>(await editor.PostAsJsonAsync("/api/alis", incomplete));
        Assert.Null(created.AliciId);
        Assert.Equal("Editör", created.Alici);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync($"/api/alis/{created.Id}/onayla", new AlisDurumYaz(created.Surum))).StatusCode);
        var sent = await Read<AlisDto>(await editor.PostAsJsonAsync($"/api/alis/{created.Id}/gonder", new AlisDurumYaz(created.Surum)));
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync($"/api/alis/{created.Id}/onayla", new AlisDurumYaz(sent.Surum))).StatusCode);
        var edited = await Read<AlisDto>(await editor.PutAsJsonAsync($"/api/alis/{created.Id}", complete with { Surum = sent.Surum }));
        var approved = await Read<AlisDto>(await editor.PostAsJsonAsync($"/api/alis/{created.Id}/onayla", new AlisDurumYaz(edited.Surum)));
        var replay = await Read<AlisDto>(await editor.PostAsJsonAsync($"/api/alis/{created.Id}/onayla", new AlisDurumYaz(edited.Surum)));
        Assert.Equal(approved.Surum, replay.Surum);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PutAsJsonAsync($"/api/alis/{created.Id}", complete with { Surum = approved.Surum })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync($"/api/alis/{created.Id}/iade", new AlisDurumYaz(approved.Surum, " "))).StatusCode);
        var returned = await Read<AlisDto>(await editor.PostAsJsonAsync($"/api/alis/{created.Id}/iade", new AlisDurumYaz(approved.Surum, "Dağılımı düzeltin")));
        Assert.Equal(AlisDurumlari.Taslak, returned.Durum);
        Assert.Equal("Dağılımı düzeltin", returned.EditorNotu);
    }

    [Fact]
    public async Task Odeme_istegi_tekrari_tek_gider_uretir_farkli_icerik_eski_surum_ve_fazla_odeme_reddedilir()
    {
        await using var factory = new KasaWebFactory();
        using var editor = await factory.EditorClientAsync();
        await Prepare(editor);
        var channels = await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar");
        var created = await Read<AlisDto>(await editor.PostAsJsonAsync("/api/alis", Draft(channels!)));
        var request = new AlisOdemeYaz(created.Surum, Guid.NewGuid(), Date, 40m, Not: "İlk ödeme");
        var paid = await Read<AlisDto>(await editor.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", request));
        var replay = await Read<AlisDto>(await editor.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", request with { Surum = 999 }));
        Assert.Equal(paid.Surum, replay.Surum);
        Assert.Equal(paid.Odemeler.Single().Id, replay.Odemeler.Single().Id);
        Assert.True(replay.Odemeler.Single().DagilimBekliyor);
        Assert.Empty(replay.Odemeler.Single().Dagilimlar);
        Assert.Equal(40m, replay.Odenen);
        Assert.Equal(60m, replay.Kalan);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", request with { Tutar = 41m })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", request with { IstekId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", request with { Surum = paid.Surum, IstekId = Guid.NewGuid(), Tutar = 61m })).StatusCode);
        var completed = await Read<AlisDto>(await editor.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", request with { Surum = paid.Surum, IstekId = Guid.NewGuid(), Tutar = 60m }));
        Assert.Equal(0m, completed.Kalan);
        var less = Draft(channels!) with { Surum = completed.Surum, Kalemler = [new("Ürün", 99m, [])] };
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PutAsJsonAsync($"/api/alis/{created.Id}", less)).StatusCode);
        var another = await Read<AlisDto>(await editor.PostAsJsonAsync("/api/alis", Draft(channels!)));
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync($"/api/alis/{another.Id}/odemeler", request)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(2, db.Islemler.Count());
        Assert.Equal(2, db.AlisOdemeler.Count());
        Assert.All(db.Islemler, i => { Assert.Equal(Kanallar.DagilimBekliyor, i.Kanal); Assert.Null(i.KanalId); });
    }

    [Fact]
    public async Task Gecerli_mevcut_gider_bir_kez_baglanir_uyusmayan_alanlar_ve_takip_oncesi_odeme_reddedilir()
    {
        await using var factory = new KasaWebFactory();
        using var editor = await factory.EditorClientAsync();
        await Prepare(editor);
        var channels = await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar");
        var created = await Read<AlisDto>(await editor.PostAsJsonAsync("/api/alis", Draft(channels!)));
        var expense = await Read<System.Text.Json.JsonElement>(await editor.PostAsJsonAsync("/api/islemler", new { tarih = Date, cari = "Eski gider", kanal = "Ortak", tip = "Cari", tutarTl = 30m }));
        var expenseId = expense.GetProperty("id").GetInt32();
        var request = new AlisOdemeYaz(created.Surum, Guid.NewGuid(), Date, 31m, MevcutIslemId: expenseId);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", request with { Tarih = Date.AddDays(-1), MevcutIslemId = null })).StatusCode);
        var linked = await Read<AlisDto>(await editor.PostAsJsonAsync($"/api/alis/{created.Id}/odemeler", request with { Tutar = 30m }));
        Assert.Equal(expenseId, Assert.Single(linked.Odemeler).IslemId);
        var second = await Read<AlisDto>(await editor.PostAsJsonAsync("/api/alis", Draft(channels!)));
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync($"/api/alis/{second.Id}/odemeler", request with { IstekId = Guid.NewGuid(), Tutar = 30m })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Single(db.Islemler);
        Assert.Equal("Ortak", db.Islemler.Single().Kanal);
    }

    [Theory]
    [InlineData("null_liste")]
    [InlineData("null_kalem")]
    [InlineData("negatif")]
    [InlineData("kusurat")]
    [InlineData("olmayan_kanal")]
    [InlineData("yinelenen_kanal")]
    [InlineData("fazla_kalem")]
    [InlineData("buyuk_toplam")]
    public async Task Gecersiz_taslak_verisi_kaydedilmez(string scenario)
    {
        await using var factory = new KasaWebFactory();
        using var editor = await factory.EditorClientAsync();
        var channels = await editor.GetFromJsonAsync<List<AlisKanalDto>>("/api/alis/kanallar");
        var draft = Draft(channels!);
        draft = scenario switch
        {
            "null_liste" => draft with { Kalemler = null! },
            "null_kalem" => draft with { Kalemler = [null!] },
            "negatif" => draft with { Kalemler = [new("Ürün", -1m, [])] },
            "kusurat" => draft with { Kalemler = [new("Ürün", 0.001m, [])] },
            "olmayan_kanal" => draft with { Kalemler = [new("Ürün", 100m, [new(99999, 100m)])] },
            "yinelenen_kanal" => draft with { Kalemler = [new("Ürün", 100m, [new(channels![0].Id, 50m), new(channels[0].Id, 50m)])] },
            "fazla_kalem" => draft with { Kalemler = Enumerable.Range(0, 101).Select(_ => new AlisKalemYaz("Ürün", 1m, [])).ToList() },
            "buyuk_toplam" => draft with { Kalemler = [new("Ürün1", 999_999_999_999.99m, []), new("Ürün2", 1m, [])] },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/alis", draft)).StatusCode);
        Assert.Empty((await editor.GetFromJsonAsync<List<AlisDto>>("/api/alis"))!);
    }

    internal static AlisYaz Draft(IReadOnlyList<AlisKanalDto> channels) => new(0, Date, "Tedarikçi", null,
        [new("Ürün", 100m, [new(channels[0].Id, 60m), new(channels[1].Id, 40m)])]);

    internal static async Task Prepare(HttpClient editor) =>
        (await editor.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Date, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();

    internal static async Task<T> Read<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
    }

    internal static async Task<HttpClient> Buyer(KasaWebFactory factory, HttpClient editor, string username)
    {
        (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz(username, username, "gizlisifre123"))).EnsureSuccessStatusCode();
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { kullanici = username, sifre = "gizlisifre123" })).EnsureSuccessStatusCode();
        return client;
    }
}
