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
    public async Task Islem_olustur_istek_kimligini_gonderir_eski_govde_null_birakir()
    {
        var (c, h) = Kur();
        var istekId = Guid.NewGuid();
        h.Kuyrukla(HttpStatusCode.OK, """{"id":1,"tarih":"2026-03-05","cari":"K","tutarTl":5.0,"kanal":"MEZAT","tip":"Cari","not":null}""");
        await c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 3, 5), "K", 5m, "MEZAT", GiderTipi.Cari, null, IstekId: istekId));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
            Assert.Equal(istekId, doc.RootElement.GetProperty("istekId").GetGuid());

        h.Kuyrukla(HttpStatusCode.Created, """{"id":2,"tarih":"2026-03-05","cari":"K","tutarTl":5.0,"kanal":"MEZAT","tip":"Cari","not":null}""");
        await c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 3, 5), "K", 5m, "MEZAT", GiderTipi.Cari, null));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("istekId").ValueKind);

        // gap-coklu-giris-cift-sayim-mutabakat-6: kartlı giderin taksit alanları gövdeye taşınır; verilmezse null (tek taksit).
        h.Kuyrukla(HttpStatusCode.Created, """{"id":3,"tarih":"2026-03-05","cari":"Tel","tutarTl":3000.0,"kanal":"MEZAT","tip":"KrediKarti","not":null,"krediKartiId":7}""");
        await c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 3, 5), "Tel", 3000m, "MEZAT", GiderTipi.KrediKarti, null, 7, TaksitSayisi: 6, IlkKesimTarihi: new DateOnly(2026, 4, 5)));
        using (var doc = JsonDocument.Parse(h.SonGovde!))
            Assert.Equal((6, "2026-04-05"), (doc.RootElement.GetProperty("taksitSayisi").GetInt32(), doc.RootElement.GetProperty("ilkKesimTarihi").GetString()));
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
    public async Task Islem_silme_okunan_surumu_sorguda_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.NoContent);

        await c.IslemSilAsync(17, 4);

        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.Equal("/api/islemler/17?surum=4", h.SonIstek.RequestUri!.PathAndQuery);
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
    public async Task Islem_olustur_krediKartiId_govdede_gider()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":1,"tarih":"2026-07-11","cari":"X","tutarTl":90.0,"kanal":"MEZAT","tip":"KrediKarti","not":null,"krediKartiId":7}""");

        await c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 7, 11), "X", 90m, "MEZAT", GiderTipi.KrediKarti, null, 7));

        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal(7, doc.RootElement.GetProperty("krediKartiId").GetInt32());
    }
}
