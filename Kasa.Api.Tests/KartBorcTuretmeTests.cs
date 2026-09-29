using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class KartBorcTuretmeTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KartBorcTuretmeTests(KasaWebFactory factory)
    {
        _factory = factory;
        TarihSiniriTests.TakipBaslangiciAyarla(factory, new DateOnly(2026, 7, 1)); // Temmuz giderleri takip içinde
    }

    private record KartYanit(int Id, string Ad, decimal Borc, decimal GuncelBorc,
        decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam);

    [Fact]
    public async Task GuncelBorc_acilis_arti_harcama_eksi_odeme_olur()
    {
        var client = await _factory.EditorClientAsync();

        var kart = LegacyFinanceSeed.Kart(_factory, new("Türetme", new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 25), 100_000m, 1000m));

        // Eski karta bağlı mevcut harcama (K3: yeni gider takipteki karta bağlanır).
        LegacyFinanceSeed.KartGideri(_factory, new DateOnly(2026, 7, 10), "Market", 500m, "MEZAT", kart!.Id);

        await client.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart.Id,
            tarih = "2026-07-20",
            tutar = 200m,
            not = (string?)null,
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
        // K3: yeni kart gideri yalnız yeni takipteki karta bağlanabilir.
        var kartYanit = await client.PostAsJsonAsync("/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "TipZorla", 10_000m, 5, 25,
            new DateOnly(2026, 7, 1), 0m, []));
        kartYanit.EnsureSuccessStatusCode();
        var kart = await kartYanit.Content.ReadFromJsonAsync<KartTakipDto>();

        var olustur = await client.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-07-11",
            cari = "X",
            tutarTl = 90m,
            kanal = "MEZAT",
            tip = "Cari",
            not = (string?)null,
            krediKartiId = kart!.Id,
        });
        var olusan = await olustur.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("KrediKarti", olusan.GetProperty("tip").GetString());
    }
}
