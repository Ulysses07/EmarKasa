using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Kasa.Api.Tests;

public class KrediKartiKayitIslemleriTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KrediKartiKayitIslemleriTests(KasaWebFactory factory) => _factory = factory;

    private record KartYanit(int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc);

    [Fact]
    public async Task Eski_yoldan_yeni_kart_reddedilir_mevcut_kart_yonetilebilir()
    {
        var client = await _factory.EditorClientAsync();

        var olustur = await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Bonus",
            kesimTarihi = "2026-07-05",
            sonOdemeTarihi = "2026-07-25",
            limit = 100_000m,
            borc = 30_000m,
        });
        Assert.Equal(HttpStatusCode.Conflict, olustur.StatusCode);
        var eklenen = EskiFinansTohumu.Kart(_factory, new("Bonus", new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 25), 100_000m, 30_000m));
        Assert.NotNull(eklenen);
        Assert.Equal("Bonus", eklenen!.Ad);
        Assert.Equal(100_000m, eklenen.Limit);

        var guncelle = await client.PutAsJsonAsync($"/api/kredikartlari/{eklenen.Id}", new
        {
            ad = "Bonus",
            kesimTarihi = "2026-07-05",
            sonOdemeTarihi = "2026-07-25",
            limit = 100_000m,
            borc = 45_000m,
        });
        Assert.Equal(HttpStatusCode.OK, guncelle.StatusCode);

        var liste = await client.GetFromJsonAsync<List<KartYanit>>("/api/kredikartlari");
        Assert.Equal(45_000m, liste!.Single(k => k.Id == eklenen.Id).Borc);

        var sil = await client.DeleteAsync($"/api/kredikartlari/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
    }

    /// <summary>Eski istemcinin kart ve kredi ekleme uçları gövdeyi okumaz: geçerli, bozuk ya da JSON olmayan gövde ve boş istek aynı
    /// iletili 409'u alır (gövde bağlanırken bozuk JSON 400, başka içerik türü 415 alıyordu). Yetki denetimi değişmez.</summary>
    [Theory]
    [InlineData("/api/kredikartlari", "Yeni kartı güncel uygulamanın Kredi Kartları ekranından oluşturun.")]
    [InlineData("/api/krediler", "Yeni krediyi güncel uygulamanın Krediler ekranından oluşturun.")]
    public async Task Eski_ekleme_uclari_govdeden_bagimsiz_ayni_409_iletisini_doner(string yol, string ileti)
    {
        var client = await _factory.EditorClientAsync();
        HttpContent?[] govdeler =
        [
            JsonContent.Create(new { ad = "X", limit = 1m }),
            new StringContent("{\"ad\": ", Encoding.UTF8, "application/json"),
            new StringContent("ad=X", Encoding.UTF8, "text/plain"),
            null,
        ];
        foreach (var govde in govdeler)
        {
            using var yanit = await client.PostAsync(yol, govde);
            Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
            Assert.Equal(ileti, (await yanit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString());
        }
    }

    [Fact]
    public async Task Izleyici_kart_okur_ama_ekleyemez()
    {
        var editor = await _factory.EditorClientAsync();
        await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifre-123" });

        var izleyici = _factory.CreateClient();
        var giris = await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izleyici-sifre-123" });
        giris.EnsureSuccessStatusCode();

        var okuma = await izleyici.GetAsync("/api/kredikartlari");
        Assert.Equal(HttpStatusCode.OK, okuma.StatusCode);

        var yazma = await izleyici.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "X",
            kesimTarihi = "2026-07-05",
            sonOdemeTarihi = "2026-07-25",
            limit = 1m,
            borc = 0m,
        });
        Assert.Equal(HttpStatusCode.Forbidden, yazma.StatusCode);
    }
}
