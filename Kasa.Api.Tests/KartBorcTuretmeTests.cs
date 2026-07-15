using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class KartBorcTuretmeTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KartBorcTuretmeTests(KasaWebFactory factory) => _factory = factory;

    private record KartYanit(int Id, string Ad, decimal Borc, decimal GuncelBorc,
        decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam);

    [Fact]
    public async Task GuncelBorc_acilis_arti_harcama_eksi_odeme_olur()
    {
        var client = await _factory.EditorClientAsync();

        var kart = await (await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Türetme", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 100_000m, borc = 1000m,
        })).Content.ReadFromJsonAsync<KartYanit>();

        await client.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-07-10", cari = "Market", tutarTl = 500m,
            kanal = "MEZAT", tip = "Cari", not = (string?)null, krediKartiId = kart!.Id,
        });

        await client.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart.Id, tarih = "2026-07-20", tutar = 200m, not = (string?)null,
        });

        var liste = await client.GetFromJsonAsync<List<KartYanit>>("/api/kredikartlari");
        var g = liste!.Single(k => k.Id == kart.Id);
        Assert.Equal(1000m, g.AcilisBorc);
        Assert.Equal(500m, g.HarcamaToplam);
        Assert.Equal(200m, g.OdemeToplam);
        Assert.Equal(1300m, g.GuncelBorc);
    }

    [Fact]
    public async Task Islem_krediKartiId_dolu_gelirse_tip_KrediKarti_olur()
    {
        var client = await _factory.EditorClientAsync();
        var kart = await (await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "TipZorla", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 10_000m, borc = 0m,
        })).Content.ReadFromJsonAsync<KartYanit>();

        var olustur = await client.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-07-11", cari = "X", tutarTl = 90m, kanal = "MEZAT",
            tip = "Cari", not = (string?)null, krediKartiId = kart!.Id,
        });
        var olusan = await olustur.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("KrediKarti", olusan.GetProperty("tip").GetString());
    }
}
