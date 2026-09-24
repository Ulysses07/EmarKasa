using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using static Kasa.Api.Tests.PaketD;

namespace Kasa.Api.Tests;

/// <summary>Kart ekstresi mutabakatı API'si (Paket D, özellik 34). Kart borcu hesabı değişmez.</summary>
public class KartMutabakatApiTests : IClassFixture<PaketDFactory>
{
    private readonly PaketDFactory _factory;
    public KartMutabakatApiTests(PaketDFactory factory) => _factory = factory;

    /// <summary>Kesim 15, son ödeme 25, açılış 100. Dönem 16 Ağu–15 Eyl: devreden 300, harcama 350, ödeme 100 → 550.</summary>
    private async Task<(HttpClient C, int KartId, int[] Islemler)> Hazirla()
    {
        var c = await _factory.EditorClientAsync();
        var kart = await Basarili(await c.PostAsJsonAsync("/api/kredikartlari",
            new { ad = "Kart " + Guid.NewGuid().ToString("N")[..6], kesimTarihi = "2026-01-15", sonOdemeTarihi = "2026-01-25", limit = 50_000m, borc = 100m }));
        var id = kart.Id();
        var i1 = await IslemEkle(c, "Market", 200m, "2026-08-10", krediKartiId: id);
        var i2 = await IslemEkle(c, "Market", 300m, "2026-08-20", krediKartiId: id);
        var i3 = await IslemEkle(c, "A", 50m, "2026-09-15", krediKartiId: id);
        var i4 = await IslemEkle(c, "B", 70m, "2026-09-20", krediKartiId: id);
        await Basarili(await c.PostAsJsonAsync("/api/kartodemeler", new { krediKartiId = id, tarih = "2026-08-25", tutar = 100m }));
        return (c, id, [i1, i2, i3, i4]);
    }

    private static async Task<JsonElement> Kart(HttpClient c, int id)
        => (await GetJson(c, "/api/kredikartlari")).EnumerateArray().Single(k => k.Id() == id);

    [Fact]
    public async Task Donemler_ve_detay_uygulamanin_donem_borcunu_gosterir()
    {
        var (c, kartId, islemler) = await Hazirla();
        var donemler = (await GetJson(c, $"/api/kartmutabakat/donemler?krediKartiId={kartId}&adet=3")).EnumerateArray().ToList();
        Assert.Equal(["2026-09-15", "2026-08-15", "2026-07-15"], donemler.Select(d => d.Str("kesim")));
        Assert.Equal("2026-08-16", donemler[0].Str("baslangic"));
        Assert.Equal("2026-09-25", donemler[0].Str("sonOdeme"));
        Assert.Equal([550m, 300m, 100m], donemler.Select(d => d.Dec("hesaplananBorc")));
        Assert.All(donemler, d => Assert.True(d.Null("mutabakatId")));
        Assert.Equal(12, (await GetJson(c, $"/api/kartmutabakat/donemler?krediKartiId={kartId}")).GetArrayLength());

        var d0 = await GetJson(c, $"/api/kartmutabakat?krediKartiId={kartId}&kesim=2026-09-15");
        Assert.Equal(300m, d0.Dec("devredenBorc"));
        Assert.Equal(350m, d0.Dec("donemHarcama"));
        Assert.Equal(100m, d0.Dec("donemOdeme"));
        Assert.Equal(550m, d0.Dec("hesaplananBorc"));
        Assert.Equal([islemler[1], islemler[2]], d0.GetProperty("islemler").EnumerateArray().Select(i => i.Id()));
        Assert.Single(d0.GetProperty("odemeler").EnumerateArray());
        Assert.True(d0.Null("fark"));
        Assert.True(d0.Null("durum"));
    }

    [Fact]
    public async Task Mutabakat_kaydi_fark_durum_ve_tikler_kart_borcunu_degistirmez()
    {
        var (c, kartId, islemler) = await Hazirla();
        var once = await Kart(c, kartId);

        var r = await c.PutAsJsonAsync("/api/kartmutabakat", new
        {
            krediKartiId = kartId, kesim = "2026-09-15", ekstreTutari = 560m, tikliIslemIdleri = new[] { islemler[1] },
            not = "  banka 10 TL fazla  ", farkKabul = false,
        });
        var m = await Basarili(r);
        Assert.Equal(10m, m.Dec("fark"));
        Assert.Equal("Acik", m.Str("durum"));
        Assert.Equal(50m, m.Dec("tiksizToplam"));
        Assert.Equal("banka 10 TL fazla", m.Str("not"));
        Assert.Equal(550m, m.Dec("kayittakiHesaplanan"));
        Assert.Equal([false, true], m.GetProperty("islemler").EnumerateArray()
            .OrderBy(i => i.Str("tarih")).Select(i => i.Bool("tikli")).Reverse());

        // Aynı dönem için ikinci kayıt güncellemedir (tek satır); farkı kabul et.
        var kabul = await Basarili(await c.PutAsJsonAsync("/api/kartmutabakat", new
        {
            krediKartiId = kartId, kesim = "2026-09-15", ekstreTutari = 560m, tikliIslemIdleri = islemler[1..3], farkKabul = true,
        }));
        Assert.Equal(m.GetProperty("mutabakatId").GetInt32(), kabul.GetProperty("mutabakatId").GetInt32());
        Assert.Equal("FarkKabul", kabul.Str("durum"));
        Assert.Equal(0m, kabul.Dec("tiksizToplam"));

        var mutabik = await Basarili(await c.PutAsJsonAsync("/api/kartmutabakat", new { krediKartiId = kartId, kesim = "2026-09-15", ekstreTutari = 550m, farkKabul = true }));
        Assert.Equal("Mutabik", mutabik.Str("durum"));
        Assert.Equal(0m, mutabik.Dec("fark"));

        var donem = (await GetJson(c, $"/api/kartmutabakat/donemler?krediKartiId={kartId}&adet=1")).EnumerateArray().Single();
        Assert.Equal(550m, donem.Dec("ekstreTutari"));
        Assert.Equal("Mutabik", donem.Str("durum"));

        // Kural değişmez: kart borcu ve ekstre borcu mutabakattan önceki gibi.
        var sonra = await Kart(c, kartId);
        Assert.Equal(once.Dec("guncelBorc"), sonra.Dec("guncelBorc"));
        Assert.Equal(once.Dec("ekstreBorc"), sonra.Dec("ekstreBorc"));
        Assert.Contains(await Gecmis(c, GecmisTurleri.KartMutabakati), g => g.Str("eylem") == "Eklendi");

        // Sonradan eklenen harcama dönemin borcunu değiştirir: mutabık kayıt yeniden "açık" olur
        // (kabul edilmiş bir fark yoktu); kayıttaki anlık görüntü aynı kalır.
        await IslemEkle(c, "A", 5m, "2026-09-01", krediKartiId: kartId);
        var canli = await GetJson(c, $"/api/kartmutabakat?krediKartiId={kartId}&kesim=2026-09-15");
        Assert.Equal(-5m, canli.Dec("fark"));
        Assert.Equal("Acik", canli.Str("durum"));
        Assert.Equal(550m, canli.Dec("kayittakiHesaplanan"));
    }

    [Fact]
    public async Task Dogrulamalar_ve_yetki()
    {
        var (c, kartId, islemler) = await Hazirla();
        async Task<string> Put(object g) => await Hata(await c.PutAsJsonAsync("/api/kartmutabakat", g));

        Assert.Equal("Kesim tarihi kartın kesim gününe (15) denk gelmiyor.",
            await Put(new { krediKartiId = kartId, kesim = "2026-09-14", ekstreTutari = 1m, farkKabul = false }));
        Assert.Equal("Bu ekstre dönemi henüz kapanmadı.",
            await Put(new { krediKartiId = kartId, kesim = "2026-10-15", ekstreTutari = 1m, farkKabul = false }));
        Assert.Equal("Kredi kartı bulunamadı.",
            await Put(new { krediKartiId = 987_654, kesim = "2026-09-15", ekstreTutari = 1m, farkKabul = false }));
        Assert.Equal("Ekstre tutarı en fazla 2 ondalık basamak içerebilir.",
            await Put(new { krediKartiId = kartId, kesim = "2026-09-15", ekstreTutari = 1.001m, farkKabul = false }));
        Assert.Contains("bu dönemin kart harcamalarından değil",
            await Put(new { krediKartiId = kartId, kesim = "2026-09-15", ekstreTutari = 1m, tikliIslemIdleri = new[] { islemler[3] }, farkKabul = false }));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/kartmutabakat?krediKartiId={kartId}&kesim=2026-09-16")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/kartmutabakat/donemler?krediKartiId=987654")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/kartmutabakat/donemler?krediKartiId={kartId}&adet=0")).StatusCode);

        var m = await Basarili(await c.PutAsJsonAsync("/api/kartmutabakat", new { krediKartiId = kartId, kesim = "2026-08-15", ekstreTutari = -20m, farkKabul = false }));
        Assert.Equal(-320m, m.Dec("fark"));   // alacaklı ekstre (negatif) de girilebilir

        var izleyici = await _factory.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync($"/api/kartmutabakat?krediKartiId={kartId}&kesim=2026-08-15")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PutAsJsonAsync("/api/kartmutabakat",
            new { krediKartiId = kartId, kesim = "2026-08-15", ekstreTutari = 1m, farkKabul = false })).StatusCode);
        var mid = m.GetProperty("mutabakatId").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.DeleteAsync($"/api/kartmutabakat/{mid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kartmutabakat/{mid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/kartmutabakat/{mid}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync($"/api/kartmutabakat/donemler?krediKartiId={kartId}")).StatusCode);
    }
}
