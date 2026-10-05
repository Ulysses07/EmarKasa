using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class EkstreKuralTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static object Kural(string kosul = "YURTİÇİ KARGO", int kanal = 1, string? banka = "Akbank", string? yon = "Cikis", string tur = "Gider", int surum = 0, bool aktif = true, Guid? key = null) => new
    {
        istekId = key ?? Guid.NewGuid(),
        surum,
        ad = "Kargo " + kanal,
        kaynak = "Banka",
        banka,
        aciklamaIcerir = kosul,
        yon,
        islemTuru = tur,
        dagilimTuru = tur == "Atla" ? "Genel" : "Esit",
        kanalIds = tur == "Atla" ? Array.Empty<int>() : new[] { kanal },
        aktif
    };

    private static async Task<JsonNode> Post(HttpClient c, object value)
    {
        var r = await c.PostAsJsonAsync("/api/ekstre-aktar/kurallar", value);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private static int Belge(KasaWebFactory f, params EkstreOkunanSatir[] rows)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var doc = new EkstreBelgeEntity { Kaynak = "Banka", Banka = "Akbank", HesapAdi = "Ana", DosyaAdi = "test.pdf", DosyaOzeti = Guid.NewGuid().ToString(), SatirlarJson = JsonSerializer.Serialize(rows), UyarilarJson = "[]" };
        db.EkstreBelgeler.Add(doc);
        db.SaveChanges();
        return doc.Id;
    }

    private static EkstreOkunanSatir Satir(int no, string text = "Yurtiçi-Kargo tahsilatı", string yon = "Cikis", string para = "TRY", string tur = "Gider", params string[] warnings) => new(no, 1, text, KasaWebFactory.VarsayilanBugun, text, 100m, yon, tur, "Hareket", para, warnings);
    private static async Task<JsonArray> Oneriler(HttpClient c, int id) => (await c.GetFromJsonAsync<JsonArray>($"/api/ekstre-aktar/{id}/oneriler"))!;

    [Fact]
    public async Task Turkce_sozcuk_siniri_eslesir_ve_oneri_mali_veriyi_degistirmez()
    {
        await using var f = KasaWebFactory.Sabit(KasaWebFactory.VarsayilanBugun);
        using var c = await f.EditorClientAsync();
        await Post(c, Kural());
        var id = Belge(f, Satir(1), Satir(2, "YURTİÇİ KARGOLAR"), Satir(3, "YURTİÇİ KARGO", "Giris"));
        var before = await c.GetStringAsync("/api/rapor/panel", Ct);
        var docBefore = await c.GetStringAsync($"/api/ekstre-aktar/{id}", Ct);
        var list = await Oneriler(c, id);
        Assert.Equal("Oneri", list[0]!["durum"]!.GetValue<string>());
        Assert.Equal("Gider", list[0]!["islemTuru"]!.GetValue<string>());
        Assert.Equal(1, list[0]!["kanalIds"]![0]!.GetValue<int>());
        Assert.Equal("Yok", list[1]!["durum"]!.GetValue<string>());
        Assert.Equal("Yok", list[2]!["durum"]!.GetValue<string>());
        Assert.Equal(before, await c.GetStringAsync("/api/rapor/panel", Ct));
        Assert.Equal(docBefore, await c.GetStringAsync($"/api/ekstre-aktar/{id}", Ct));
    }

    [Fact]
    public async Task Farkli_hedefler_celisir_ayni_hedefler_tek_oneri_olur()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        await Post(c, Kural());
        await Post(c, Kural("KARGO", 1));
        var id = Belge(f, Satir(1));
        Assert.Equal("Oneri", (await Oneriler(c, id))[0]!["durum"]!.GetValue<string>());
        var other = await Post(c, Kural("KARGO", 2));
        var conflict = (await Oneriler(c, id))[0]!;
        Assert.Equal("Celiski", conflict["durum"]!.GetValue<string>());
        Assert.Null(conflict["islemTuru"]);
        Assert.Equal(3, conflict["kuralAdlari"]!.AsArray().Count);
        var del = await c.DeleteAsync($"/api/ekstre-aktar/kurallar/{other["id"]}?surum={other["surum"]}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal("Oneri", (await Oneriler(c, id))[0]!["durum"]!.GetValue<string>());
    }

    [Fact]
    public async Task Kural_tekrari_surumu_eski_duzenleme_ve_silme_korunur()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var request = Kural(key: Guid.NewGuid());
        var first = await Post(c, request);
        Assert.Equal(first["id"]!.GetValue<int>(), (await Post(c, request))["id"]!.GetValue<int>());
        var path = $"/api/ekstre-aktar/kurallar/{first["id"]}";
        var edit = await c.PutAsJsonAsync(path, Kural(surum: first["surum"]!.GetValue<int>(), aktif: false), Ct);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync(path, Kural(surum: first["surum"]!.GetValue<int>()), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync(path + $"?surum={first["surum"]}", Ct)).StatusCode);
        var id = Belge(f, Satir(1));
        Assert.Equal("Yok", (await Oneriler(c, id))[0]!["durum"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("USD", "Cikis", "Gider", "")]
    [InlineData("Belirsiz", "Cikis", "Gider", "")]
    [InlineData("TRY", "Belirsiz", "Gider", "")]
    [InlineData("TRY", "Giris", "Gider", "")]
    [InlineData("TRY", "Cikis", "Atla", "")]
    [InlineData("TRY", "Cikis", "Gider", "Taksit olabilir")]
    public async Task Belirsiz_ve_korumali_satir_kontrol_ister(string para, string yon, string tur, string warning)
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        await Post(c, Kural(yon: null));
        var row = Satir(1, para: para, yon: yon, tur: tur, warnings: warning.Length == 0 ? [] : [warning]);
        Assert.Equal("Kontrol", (await Oneriler(c, Belge(f, row)))[0]!["durum"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pasif_ve_silinmis_kanalli_kural_kapatilir_ama_yeniden_etkinlestirilemez(bool sil)
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var first = await Post(c, Kural());
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var channel = db.Kanallar.Single(k => k.Id == 1);
            if (sil)
                db.Kanallar.Remove(channel);
            else
                channel.Aktif = false;
            db.SaveChanges();
        }
        var id = Belge(f, Satir(1));
        Assert.Equal("Kontrol", (await Oneriler(c, id))[0]!["durum"]!.GetValue<string>());
        var path = $"/api/ekstre-aktar/kurallar/{first["id"]}";
        var disabled = await c.PutAsJsonAsync(path, Kural(surum: 1, aktif: false), Ct);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var result = (await disabled.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal(1, result["kanalIds"]![0]!.GetValue<int>());
        Assert.False(result["aktif"]!.GetValue<bool>());
        Assert.Equal("Yok", (await Oneriler(c, id))[0]!["durum"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync(path, Kural(surum: 2), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync(path, Kural(kanal: 999, surum: 2, aktif: false), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/ekstre-aktar/kurallar", Kural(aktif: false), Ct)).StatusCode);
    }

    [Fact]
    public async Task Banka_filtresi_pasif_kanal_ve_gecersiz_hedefler_korunur()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        await Post(c, Kural(banka: "QNB"));
        var id = Belge(f, Satir(1));
        Assert.Equal("Yok", (await Oneriler(c, id))[0]!["durum"]!.GetValue<string>());
        await Post(c, Kural());
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Kanallar.Single(k => k.Id == 1).Aktif = false;
            db.SaveChanges();
        }
        Assert.Equal("Kontrol", (await Oneriler(c, id))[0]!["durum"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/ekstre-aktar/kurallar", Kural(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/ekstre-aktar/kurallar", Kural(kanal: 2, tur: "Eslestir"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/ekstre-aktar/kurallar", Kural(kosul: "xx", kanal: 2), Ct)).StatusCode);
    }
}
