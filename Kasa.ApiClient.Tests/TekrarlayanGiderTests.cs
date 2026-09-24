using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class TekrarlayanGiderTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task Liste_ve_bekleyen_dogru_yoldan_okunur()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":3,"kalem":"Kira","kanal":"MEZAT","tutar":25000.0,"ayinGunu":31,"aktif":true,"baslangicAyi":"2026-01-01"}]""");
        var liste = await c.TekrarlayanGiderlerAsync();
        Assert.Equal(HttpMethod.Get, h.SonIstek!.Method);
        Assert.EndsWith("/api/tekrarlayangiderler", h.SonIstek.RequestUri!.AbsolutePath);
        var t = Assert.Single(liste);
        Assert.Equal(new TekrarlayanGiderDto(3, "Kira", "MEZAT", 25000m, 31, true, new DateOnly(2026, 1, 1)), t);

        h.Kuyrukla(HttpStatusCode.OK, """[{"tekrarlayanGiderId":3,"kalem":"Kira","kanal":"MEZAT","tutar":25000.0,"ay":"2026-02-01","vade":"2026-02-28"}]""");
        var bekleyen = await c.BekleyenGiderlerAsync();
        Assert.EndsWith("/api/tekrarlayangiderler/bekleyen", h.SonIstek!.RequestUri!.AbsolutePath);
        var b = Assert.Single(bekleyen);
        Assert.Equal(new DateOnly(2026, 2, 28), b.Vade);
        Assert.Equal(new DateOnly(2026, 2, 1), b.Ay);
    }

    [Fact]
    public async Task Olustur_baslangic_ayi_yoksa_alani_gondermez()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":5,"kalem":"SGK","kanal":"Ortak","tutar":8000.0,"ayinGunu":15,"aktif":true,"baslangicAyi":"2026-09-01"}""");

        var e = await c.TekrarlayanGiderOlusturAsync(new TekrarlayanGiderYaz("SGK", "Ortak", 8000m, 15, true));

        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/tekrarlayangiderler", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("SGK", doc.RootElement.GetProperty("kalem").GetString());
        Assert.Equal("Ortak", doc.RootElement.GetProperty("kanal").GetString());
        Assert.Equal(8000m, doc.RootElement.GetProperty("tutar").GetDecimal());
        Assert.Equal(15, doc.RootElement.GetProperty("ayinGunu").GetInt32());
        Assert.True(doc.RootElement.GetProperty("aktif").GetBoolean());
        // null gönderilseydi sunucu DateOnly'ye çeviremezdi: alan hiç yazılmamalı.
        Assert.False(doc.RootElement.TryGetProperty("baslangicAyi", out _));
        Assert.Equal(5, e.Id);
    }

    [Fact]
    public async Task Guncelle_put_ve_baslangic_ayi_verilirse_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"id":5,"kalem":"SGK","kanal":"MEZAT","tutar":9000.0,"ayinGunu":20,"aktif":false,"baslangicAyi":"2026-03-01"}""");

        var e = await c.TekrarlayanGiderGuncelleAsync(5, new TekrarlayanGiderYaz("SGK", "MEZAT", 9000m, 20, false, new DateOnly(2026, 3, 1)));

        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.EndsWith("/api/tekrarlayangiderler/5", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("2026-03-01", doc.RootElement.GetProperty("baslangicAyi").GetString());
        Assert.False(doc.RootElement.GetProperty("aktif").GetBoolean());
        Assert.False(e.Aktif);
    }

    [Fact]
    public async Task Sil_delete_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.TekrarlayanGiderSilAsync(8);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.EndsWith("/api/tekrarlayangiderler/8", h.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Onayla_ay_tarih_tutar_gonderir_ve_islemi_doner()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":41,"tarih":"2026-02-28","cari":"Kira","tutarTl":26000.0,"kanal":"MEZAT","tip":"SabitGider","not":"Tekrarlayan gider"}""");

        var islem = await c.TekrarlayanOnaylaAsync(3, new TekrarlayanOnayYaz(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), 26000m));

        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/tekrarlayangiderler/3/onayla", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("2026-02-01", doc.RootElement.GetProperty("ay").GetString());
        Assert.Equal("2026-02-28", doc.RootElement.GetProperty("tarih").GetString());
        Assert.Equal(26000m, doc.RootElement.GetProperty("tutar").GetDecimal());
        Assert.Equal(41, islem.Id);
        Assert.Equal(GiderTipi.SabitGider, islem.Tip);
    }

    [Fact]
    public async Task Atla_ay_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"id":1,"tekrarlayanGiderId":3,"ay":"2026-02-01","durum":"Atlandi","islemId":null,"zaman":"2026-03-01T09:00:00Z"}""");

        await c.TekrarlayanAtlaAsync(3, new DateOnly(2026, 2, 1));

        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/tekrarlayangiderler/3/atla", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("2026-02-01", doc.RootElement.GetProperty("ay").GetString());
    }

    [Fact]
    public async Task Ikinci_karar_409_sunucu_mesajiyla_hata_verir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Conflict, """{"hata":"Bu gider şubat 2026 için zaten girildi."}""");

        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.TekrarlayanAtlaAsync(3, new DateOnly(2026, 2, 1)));

        Assert.Equal(HttpStatusCode.Conflict, ex.DurumKodu);
        Assert.Contains("zaten girildi", ex.Message);
    }
}
