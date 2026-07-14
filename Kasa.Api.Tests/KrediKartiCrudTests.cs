using System.Net;
using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class KrediKartiCrudTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KrediKartiCrudTests(KasaWebFactory factory) => _factory = factory;

    private record KartYanit(int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc);

    [Fact]
    public async Task Editor_kart_ekleyip_listeleyip_guncelleyip_silebilir()
    {
        var client = await _factory.EditorClientAsync();

        var olustur = await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Bonus", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 100_000m, borc = 30_000m,
        });
        Assert.Equal(HttpStatusCode.Created, olustur.StatusCode);
        var eklenen = await olustur.Content.ReadFromJsonAsync<KartYanit>();
        Assert.NotNull(eklenen);
        Assert.Equal("Bonus", eklenen!.Ad);
        Assert.Equal(100_000m, eklenen.Limit);

        var guncelle = await client.PutAsJsonAsync($"/api/kredikartlari/{eklenen.Id}", new
        {
            ad = "Bonus", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 100_000m, borc = 45_000m,
        });
        Assert.Equal(HttpStatusCode.OK, guncelle.StatusCode);

        var liste = await client.GetFromJsonAsync<List<KartYanit>>("/api/kredikartlari");
        Assert.Equal(45_000m, liste!.Single(k => k.Id == eklenen.Id).Borc);

        var sil = await client.DeleteAsync($"/api/kredikartlari/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
    }

    [Fact]
    public async Task Izleyici_kart_okur_ama_ekleyemez()
    {
        var editor = await _factory.EditorClientAsync();
        await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" });

        var izleyici = _factory.CreateClient();
        var giris = await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" });
        giris.EnsureSuccessStatusCode();

        var okuma = await izleyici.GetAsync("/api/kredikartlari");
        Assert.Equal(HttpStatusCode.OK, okuma.StatusCode);

        var yazma = await izleyici.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "X", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25", limit = 1m, borc = 0m,
        });
        Assert.Equal(HttpStatusCode.Forbidden, yazma.StatusCode);
    }
}
