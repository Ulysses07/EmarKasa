using System.Net;

namespace Kasa.ApiClient.Tests;

public class GecmisTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task Gecmis_sayfasi_eslenir_tur_kodlanir_toplam_basliktan_okunur()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":9,"zamanUtc":"2026-09-24T09:00:00Z","rol":"editor","tur":"İşlem","kayitId":42,"eylem":"Silindi",
              "ozet":"İşlem silindi: 12.03.2026 · Market · 1.250,00 ₺ · MEZAT","eskiJson":"{\"id\":42}","yeniJson":null,
              "geriAlindi":false,"geriAlmaZamaniUtc":null,"geriAlinabilir":true}]
        """, ("X-Toplam-Kayit", "120"));

        var s = await c.GecmisAsync("İşlem", 50, 100);

        Assert.Equal(120, s.Toplam);
        var d = Assert.Single(s.Kayitlar);
        Assert.Equal(9, d.Id);
        Assert.Equal(new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), d.ZamanUtc.ToUniversalTime());
        Assert.Equal("Silindi", d.Eylem);
        Assert.Equal(42, d.KayitId);
        Assert.True(d.GeriAlinabilir);
        Assert.Null(d.GeriAlmaZamaniUtc);
        var uri = h.SonIstek!.RequestUri!;
        Assert.EndsWith("/api/gecmis", uri.AbsolutePath);
        Assert.Contains("limit=50", uri.Query);
        Assert.Contains("offset=100", uri.Query);
        Assert.Contains("tur=%C4%B0%C5%9Flem", uri.Query);
    }

    [Fact]
    public async Task Tur_yoksa_filtre_gonderilmez_baslik_yoksa_toplam_turetilir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, "[]");
        var s = await c.GecmisAsync(null, 50, 0);
        Assert.Empty(s.Kayitlar);
        Assert.Equal(0, s.Toplam);
        Assert.DoesNotContain("tur=", h.SonIstek!.RequestUri!.Query);
    }

    [Fact]
    public async Task Turler_listesi_okunur()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """["Cari","İşlem"]""");
        Assert.Equal(["Cari", "İşlem"], await c.GecmisTurleriAsync());
        Assert.EndsWith("/api/gecmis/turler", h.SonIstek!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Geri_al_post_gonderir_hata_mesajini_tasir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"id":77}""");
        await c.GeriAlAsync(9);
        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/gecmis/9/geri-al", h.SonIstek.RequestUri!.AbsolutePath);

        h.Kuyrukla(HttpStatusCode.Conflict, """{"hata":"Bu silme zaten geri alındı."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.GeriAlAsync(9));
        Assert.Equal(HttpStatusCode.Conflict, ex.DurumKodu);
        Assert.Equal("Bu silme zaten geri alındı.", ex.SunucuMesaji);
    }
}
