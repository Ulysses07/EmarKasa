using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kasa.Api.Tests;

public class KartOdemeKayitIslemleriTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KartOdemeKayitIslemleriTests(KasaWebFactory factory)
    {
        _factory = factory;
        TarihSiniriTests.TakipBaslangiciAyarla(factory, new DateOnly(2026, 7, 1)); // Temmuz giderleri takip içinde
    }

    private record KartYanit(int Id, string Ad, decimal Borc, decimal GuncelBorc,
        decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam);

    private record OdemeYanit(int Id, int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);

    [Fact]
    public async Task Editor_kart_odeme_ekleyip_listeleyip_silebilir()
    {
        var client = await _factory.EditorClientAsync();

        // Kart oluştur
        var kart = EskiFinansTohumu.Kart(_factory, new("OdemeTest", new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 25), 50_000m, 5_000m));
        Assert.NotNull(kart);

        // Ödeme ekle → 201
        var ekle = await client.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart!.Id,
            tarih = "2026-07-20",
            tutar = 1_500m,
            not = (string?)null,
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, ekle.StatusCode);
        var eklenen = await ekle.Content.ReadFromJsonAsync<OdemeYanit>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(eklenen);
        Assert.Equal(1_500m, eklenen!.Tutar);

        // Liste → ödeme görünür
        var liste = await client.GetFromJsonAsync<List<OdemeYanit>>(
            $"/api/kartodemeler?krediKartiId={kart.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(liste);
        Assert.Contains(liste, o => o.Id == eklenen.Id && o.Tutar == 1_500m);

        // Sil → 204
        var sil = await client.DeleteAsync($"/api/kartodemeler/{eklenen.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);

        // Liste → ödeme artık yok
        var listeSonra = await client.GetFromJsonAsync<List<OdemeYanit>>(
            $"/api/kartodemeler?krediKartiId={kart.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(listeSonra);
        Assert.DoesNotContain(listeSonra, o => o.Id == eklenen.Id);
    }

    [Fact]
    public async Task Izleyici_odeme_okur_ama_ekleyemez()
    {
        // Önce editor ile kart ve ödeme oluştur
        var editor = await _factory.EditorClientAsync();
        await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifre-123" }, cancellationToken: TestContext.Current.CancellationToken);

        var kart = EskiFinansTohumu.Kart(_factory, new("IzleyiciTest", new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 25), 20_000m, 1_000m));
        Assert.NotNull(kart);

        await editor.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart!.Id,
            tarih = "2026-07-15",
            tutar = 500m,
            not = (string?)null,
        }, cancellationToken: TestContext.Current.CancellationToken);

        // İzleyici girişi
        var izleyici = _factory.CreateClient();
        var giris = await izleyici.PostAsJsonAsync("/api/auth/login",
            new { kullanici = (string?)null, sifre = "izleyici-sifre-123" }, cancellationToken: TestContext.Current.CancellationToken);
        giris.EnsureSuccessStatusCode();

        // GET → 200
        var okuma = await izleyici.GetAsync($"/api/kartodemeler?krediKartiId={kart.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, okuma.StatusCode);

        // POST → 403
        var yazma = await izleyici.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart.Id,
            tarih = "2026-07-16",
            tutar = 200m,
            not = (string?)null,
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, yazma.StatusCode);
    }

    [Fact]
    public async Task Kart_silinince_odemeler_silinir_ve_islem_krediKartiId_null_olur()
    {
        var client = await _factory.EditorClientAsync();

        // Kart oluştur
        var kart = EskiFinansTohumu.Kart(_factory, new("SilmeTest", new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 25), 30_000m, 2_000m));
        Assert.NotNull(kart);

        // Kart ödemesi ekle
        var odemeEkle = await client.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart!.Id,
            tarih = "2026-07-10",
            tutar = 800m,
            not = (string?)null,
        }, cancellationToken: TestContext.Current.CancellationToken);
        odemeEkle.EnsureSuccessStatusCode();
        var odeme = await odemeEkle.Content.ReadFromJsonAsync<OdemeYanit>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(odeme);

        // Eski karta bağlı mevcut harcama (K3: yeni gider takipteki karta bağlanır)
        var islemId = EskiFinansTohumu.KartGideri(_factory, new DateOnly(2026, 7, 11), "Market", 400m, "MEZAT", kart.Id).Id;

        // Kartı sil
        var sil = await client.DeleteAsync($"/api/kredikartlari/{kart.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);

        // Ödemeler silindi → liste boş
        var odemeler = await client.GetFromJsonAsync<List<OdemeYanit>>(
            $"/api/kartodemeler?krediKartiId={kart.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(odemeler);
        Assert.Empty(odemeler);

        // İşlem hâlâ var ama krediKartiId null
        var islemler = await client.GetFromJsonAsync<List<JsonElement>>("/api/islemler", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(islemler);
        var bulunan = islemler!.FirstOrDefault(i => i.GetProperty("id").GetInt32() == islemId);
        Assert.NotEqual(default, bulunan);
        Assert.True(
            bulunan.GetProperty("krediKartiId").ValueKind == JsonValueKind.Null,
            "İşlemin krediKartiId alanı null olmalı");
    }
}
