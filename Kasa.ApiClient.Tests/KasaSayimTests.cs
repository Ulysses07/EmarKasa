using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class KasaSayimTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task Gecmis_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":2,"tarih":"2026-09-22","sayilanTutar":11550.5,"hesaplananTutar":11600.0,"fark":-49.5,
              "guncelHesaplanan":11500.0,"not":"Akşam","kayitZamaniUtc":"2026-09-24T09:00:00Z"},
             {"id":1,"tarih":"2026-06-30","sayilanTutar":1.0,"hesaplananTutar":1.0,"fark":0.0,
              "guncelHesaplanan":null,"not":null,"kayitZamaniUtc":"2026-07-01T09:00:00"}]
        """);
        var l = await c.KasaSayimlariAsync();
        Assert.Equal(2, l.Count);
        Assert.Equal(new DateOnly(2026, 9, 22), l[0].Tarih);
        Assert.Equal(11_550.5m, l[0].SayilanTutar);
        Assert.Equal(-49.5m, l[0].Fark);
        Assert.Equal(11_500m, l[0].GuncelHesaplanan);
        Assert.Equal("Akşam", l[0].Not);
        Assert.Null(l[1].GuncelHesaplanan);
        Assert.Equal("/api/kasasayimlari", h.SonIstek!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Hesapla_tarihi_iso_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"tarih":"2026-09-05","hesaplananTutar":10900.25}""");
        var r = await c.KasaHesaplaAsync(new DateOnly(2026, 9, 5));
        Assert.Equal(10_900.25m, r.HesaplananTutar);
        Assert.Equal("/api/kasasayimlari/hesapla", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("?tarih=2026-09-05", h.SonIstek.RequestUri.Query);
    }

    [Fact]
    public async Task Kaydet_govdeyi_gonderir_sil_yolu()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """
            {"id":7,"tarih":"2026-09-24","sayilanTutar":100.0,"hesaplananTutar":90.0,"fark":10.0,
             "guncelHesaplanan":90.0,"not":"n","kayitZamaniUtc":"2026-09-24T09:00:00Z"}
        """);
        var s = await c.KasaSayimKaydetAsync(new KasaSayimYaz(new DateOnly(2026, 9, 24), 100m, "n"));
        Assert.Equal(7, s.Id);
        Assert.Equal(10m, s.Fark);
        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        var govde = JsonDocument.Parse(h.SonGovde!).RootElement;
        Assert.Equal("2026-09-24", govde.GetProperty("tarih").GetString());
        Assert.Equal(100m, govde.GetProperty("sayilanTutar").GetDecimal());
        Assert.Equal("n", govde.GetProperty("not").GetString());

        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.KasaSayimSilAsync(7);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.Equal("/api/kasasayimlari/7", h.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Ileri_tarih_sunucu_mesajiyla_firlatilir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.BadRequest, """{"hata":"Sayım tarihi ileri bir gün olamaz."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.KasaHesaplaAsync(new DateOnly(2030, 1, 1)));
        Assert.Equal("Sayım tarihi ileri bir gün olamaz.", ex.SunucuMesaji);
    }
}
