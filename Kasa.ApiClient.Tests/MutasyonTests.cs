using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class MutasyonTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task KrediKarti_olustur_dogru_govde_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":7,"ad":"Bonus","kesimTarihi":"2026-07-05","sonOdemeTarihi":"2026-07-25","limit":100000.0,"borc":30000.0}""");

        var eklenen = await c.KrediKartiOlusturAsync(new KrediKartiYaz("Bonus", new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 25), 100000m, 30000m));

        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/kredikartlari", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("Bonus", doc.RootElement.GetProperty("ad").GetString());
        Assert.Equal(100000m, doc.RootElement.GetProperty("limit").GetDecimal());
        Assert.Equal("2026-07-05", doc.RootElement.GetProperty("kesimTarihi").GetString());
        Assert.Equal(7, eklenen.Id);
    }

    [Fact]
    public async Task Islem_olustur_tip_string_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":1,"tarih":"2026-03-05","cari":"K.K","tutarTl":10000.0,"kanal":"MEZAT","tip":"KrediKarti","not":null}""");

        await c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 3, 5), "K.K", 10000m, "MEZAT", GiderTipi.KrediKarti, null));

        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/islemler", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("KrediKarti", doc.RootElement.GetProperty("tip").GetString());  // sayı değil, string
    }

    [Fact]
    public async Task Kanal_guncelle_put_dogru_yol_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"id":4,"ad":"MEZAT","aktif":true,"sira":2,"acilisDevri":5000.0}""");

        var guncel = await c.KanalGuncelleAsync(4, new KanalYaz("MEZAT", true, 2, 5000m));

        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.EndsWith("/api/kanallar/4", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("MEZAT", doc.RootElement.GetProperty("ad").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("sira").GetInt32());
        Assert.Equal(4, guncel.Id);
    }

    [Fact]
    public async Task Cari_sil_delete_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.CariSilAsync(3);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.EndsWith("/api/cariler/3", h.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Gelen_upsert_put_dogru_govde_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"id":9,"donemStart":"2026-03-02","kanal":"MEZAT","tutarTl":25000.0}""");

        var kaydedilen = await c.GelenKaydetAsync(new GelenYaz(new DateOnly(2026, 3, 2), "MEZAT", 25000m));

        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.EndsWith("/api/gelenler", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("2026-03-02", doc.RootElement.GetProperty("donemStart").GetString());
        Assert.Equal("MEZAT", doc.RootElement.GetProperty("kanal").GetString());
        Assert.Equal(25000m, doc.RootElement.GetProperty("tutarTl").GetDecimal());
        Assert.Equal(9, kaydedilen.Id);
    }

    [Fact]
    public async Task IzleyiciSifre_put_yeni_sifre_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.NoContent);

        await c.IzleyiciSifreAsync("gizli123");

        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.EndsWith("/api/ayarlar/izleyici-sifre", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("gizli123", doc.RootElement.GetProperty("yeniSifre").GetString());
    }

    [Fact]
    public async Task Kart_odeme_kaydet_post_dogru_govde_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":3,"krediKartiId":7,"tarih":"2026-07-20","tutar":500.0,"not":null}""");

        var eklenen = await c.KartOdemeKaydetAsync(new KartOdemeYaz(7, new DateOnly(2026, 7, 20), 500m, null));

        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/kartodemeler", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal(7, doc.RootElement.GetProperty("krediKartiId").GetInt32());
        Assert.Equal(500m, doc.RootElement.GetProperty("tutar").GetDecimal());
        Assert.Equal("2026-07-20", doc.RootElement.GetProperty("tarih").GetString());
        Assert.Equal(3, eklenen.Id);
    }

    [Fact]
    public async Task Kart_odemeler_listele_get_dogru_yol()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":1,"krediKartiId":7,"tarih":"2026-07-20","tutar":500.0,"not":null}]""");

        var liste = await c.KartOdemelerAsync(7);

        Assert.Equal(HttpMethod.Get, h.SonIstek!.Method);
        Assert.EndsWith("/api/kartodemeler", h.SonIstek.RequestUri!.AbsolutePath);
        Assert.Contains("krediKartiId=7", h.SonIstek.RequestUri!.Query);
        Assert.Single(liste);
        Assert.Equal(500m, liste[0].Tutar);
    }

    [Fact]
    public async Task Kart_odeme_sil_delete_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.NoContent);

        await c.KartOdemeSilAsync(5);

        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.EndsWith("/api/kartodemeler/5", h.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Islem_olustur_krediKartiId_govdede_gider()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":1,"tarih":"2026-07-11","cari":"X","tutarTl":90.0,"kanal":"MEZAT","tip":"KrediKarti","not":null,"krediKartiId":7}""");

        await c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 7, 11), "X", 90m, "MEZAT", GiderTipi.KrediKarti, null, 7));

        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal(7, doc.RootElement.GetProperty("krediKartiId").GetInt32());
    }
}
