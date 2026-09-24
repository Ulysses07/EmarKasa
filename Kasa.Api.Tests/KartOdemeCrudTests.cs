using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kasa.Api.Tests;

public class KartOdemeCrudTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KartOdemeCrudTests(KasaWebFactory factory) => _factory = factory;

    private record KartYanit(int Id, string Ad, decimal Borc, decimal GuncelBorc,
        decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam);

    private record OdemeYanit(int Id, int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);

    [Fact]
    public async Task Editor_kart_odeme_ekleyip_listeleyip_silebilir()
    {
        var client = await _factory.EditorClientAsync();

        // Kart oluştur
        var kart = await (await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "OdemeTest", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 50_000m, borc = 5_000m,
        })).Content.ReadFromJsonAsync<KartYanit>();
        Assert.NotNull(kart);

        // Ödeme ekle → 201
        var ekle = await client.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart!.Id, tarih = "2026-07-20", tutar = 1_500m, not = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, ekle.StatusCode);
        var eklenen = await ekle.Content.ReadFromJsonAsync<OdemeYanit>();
        Assert.NotNull(eklenen);
        Assert.Equal(1_500m, eklenen!.Tutar);

        // Liste → ödeme görünür
        var liste = await client.GetFromJsonAsync<List<OdemeYanit>>(
            $"/api/kartodemeler?krediKartiId={kart.Id}");
        Assert.NotNull(liste);
        Assert.Contains(liste, o => o.Id == eklenen.Id && o.Tutar == 1_500m);

        // Sil → 204
        var sil = await client.DeleteAsync($"/api/kartodemeler/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);

        // Liste → ödeme artık yok
        var listeSonra = await client.GetFromJsonAsync<List<OdemeYanit>>(
            $"/api/kartodemeler?krediKartiId={kart.Id}");
        Assert.NotNull(listeSonra);
        Assert.DoesNotContain(listeSonra, o => o.Id == eklenen.Id);
    }

    [Fact]
    public async Task Izleyici_odeme_okur_ama_ekleyemez()
    {
        // Önce editor ile kart ve ödeme oluştur
        var editor = await _factory.EditorClientAsync();
        await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" });

        var kart = await (await editor.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "IzleyiciTest", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 20_000m, borc = 1_000m,
        })).Content.ReadFromJsonAsync<KartYanit>();
        Assert.NotNull(kart);

        await editor.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart!.Id, tarih = "2026-07-15", tutar = 500m, not = (string?)null,
        });

        // İzleyici girişi
        var izleyici = _factory.CreateClient();
        var giris = await izleyici.PostAsJsonAsync("/api/auth/login",
            new { kullanici = (string?)null, sifre = "izle123" });
        giris.EnsureSuccessStatusCode();

        // GET → 200
        var okuma = await izleyici.GetAsync($"/api/kartodemeler?krediKartiId={kart.Id}");
        Assert.Equal(HttpStatusCode.OK, okuma.StatusCode);

        // POST → 403
        var yazma = await izleyici.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart.Id, tarih = "2026-07-16", tutar = 200m, not = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Forbidden, yazma.StatusCode);
    }

    [Fact]
    public async Task Hareketi_olan_kart_silinemez_hareketler_kalkinca_silinir()
    {
        var client = await _factory.EditorClientAsync();

        // Kart oluştur
        var kart = await (await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "SilmeTest", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 30_000m, borc = 2_000m,
        })).Content.ReadFromJsonAsync<KartYanit>();
        Assert.NotNull(kart);

        // Kart ödemesi ekle
        var odemeEkle = await client.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart!.Id, tarih = "2026-07-10", tutar = 800m, not = (string?)null,
        });
        odemeEkle.EnsureSuccessStatusCode();
        var odeme = await odemeEkle.Content.ReadFromJsonAsync<OdemeYanit>();
        Assert.NotNull(odeme);

        // İşlem ekle (krediKartiId set edilince tip KrediKarti'ye zorlanır)
        var islemEkle = await client.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-07-11", cari = "Market", tutarTl = 400m,
            kanal = "MEZAT", tip = "Cari", not = (string?)null, krediKartiId = kart.Id,
        });
        islemEkle.EnsureSuccessStatusCode();
        var islem = await islemEkle.Content.ReadFromJsonAsync<JsonElement>();
        var islemId = islem.GetProperty("id").GetInt32();

        // Hareketi varken silinemez (kasa geçmişi korunur)
        var sil = await client.DeleteAsync($"/api/kredikartlari/{kart.Id}");
        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);

        // Ödeme ve harcama kaldırılınca silinir
        (await client.DeleteAsync($"/api/kartodemeler/{odeme!.Id}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/islemler/{islemId}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/kredikartlari/{kart.Id}")).StatusCode);
    }
}
