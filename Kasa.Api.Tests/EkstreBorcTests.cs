using System.Net.Http.Json;
using System.Text.Json;

namespace Kasa.Api.Tests;

public class EkstreBorcTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Kesimden_sonraki_harcama_ekstre_borcuna_girmez()
    {
        await using var f = new KasaWebFactory();
        var c = await f.EditorClientAsync();
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        var kesim = bugun.AddDays(-5);      // en son kesim 5 gün önce
        var kart = LegacyFinanceSeed.Kart(f, new("Test", kesim, bugun.AddDays(5), 100000m, 1000m));
        int id = kart.Id;

        // kesimden ÖNCE harcama (ekstreye girer)
        await c.PostAsJsonAsync("/api/islemler", new { tarih = kesim.AddDays(-1), cari = "A",
            tutarTl = 500m, kanal = "MEZAT", tip = "KrediKarti", krediKartiId = id });
        // kesimden SONRA harcama (ekstreye GİRMEZ)
        await c.PostAsJsonAsync("/api/islemler", new { tarih = bugun, cari = "B",
            tutarTl = 300m, kanal = "MEZAT", tip = "KrediKarti", krediKartiId = id });

        var liste = await c.GetFromJsonAsync<List<JsonElement>>("/api/kredikartlari", Json);
        var k = liste!.Single(x => x.GetProperty("id").GetInt32() == id);

        Assert.Equal(1800m, k.GetProperty("guncelBorc").GetDecimal());   // 1000+500+300
        Assert.Equal(1500m, k.GetProperty("ekstreBorc").GetDecimal());   // 1000+500 (kesim sonrası hariç)
    }
}
