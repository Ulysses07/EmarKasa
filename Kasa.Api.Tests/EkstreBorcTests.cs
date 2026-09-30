using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kasa.Api.Tests;

public class EkstreBorcTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Eski /api/kredikartlari ucu son kesimi kasa saatinden hesaplar: sabit gün ve takvim sınırları.
    [Theory]
    [InlineData("2026-09-25")] // KasaWebFactory.VarsayilanBugun
    [InlineData("2027-01-01")] // yıl başı: son kesim önceki yılın Aralık'ında
    [InlineData("2028-02-29")] // artık yılın Şubat sonu
    [InlineData("2027-03-31")] // kırpılan ay sonu
    public async Task Kesimden_sonraki_harcama_ekstre_borcuna_girmez(string gun)
    {
        var bugun = DateOnly.Parse(gun, CultureInfo.InvariantCulture);
        await using var f = KasaWebFactory.Sabit(bugun);
        var c = await f.EditorClientAsync();
        var kesim = bugun.AddDays(-5);      // en son kesim 5 gün önce
        TarihSiniriTests.TakipBaslangiciAyarla(f, kesim.AddDays(-1)); // kesim öncesi harcama takip içinde
        var kart = EskiFinansTohumu.Kart(f, new("Test", kesim, bugun.AddDays(5), 100000m, 1000m));
        int id = kart.Id;

        // Eski (takipsiz) kart harcamaları mevcut kayıttır (K3: yeni gider takipteki karta bağlanır).
        // kesimden ÖNCE harcama (ekstreye girer)
        EskiFinansTohumu.KartGideri(f, kesim.AddDays(-1), "A", 500m, "MEZAT", id);
        // kesimden SONRA harcama (ekstreye GİRMEZ)
        EskiFinansTohumu.KartGideri(f, bugun, "B", 300m, "MEZAT", id);

        var liste = await c.GetFromJsonAsync<List<JsonElement>>("/api/kredikartlari", Json);
        var k = liste!.Single(x => x.GetProperty("id").GetInt32() == id);

        Assert.Equal(1800m, k.GetProperty("guncelBorc").GetDecimal());   // 1000+500+300
        Assert.Equal(1500m, k.GetProperty("ekstreBorc").GetDecimal());   // 1000+500 (kesim sonrası hariç)
    }
}
