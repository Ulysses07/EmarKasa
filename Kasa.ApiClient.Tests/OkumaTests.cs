using System.Net;

namespace Kasa.ApiClient.Tests;

public class OkumaTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task Panel_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"guncelKasa":90000.0,"kanallar":[{"kanal":"MEZAT","bakiye":150.5}],"buHaftaSonucu":10.0,"buAySonucu":-5.0}
        """);
        var p = await c.PanelAsync();
        Assert.Equal(90000.0m, p.GuncelKasa);
        Assert.Equal("MEZAT", p.Kanallar[0].Kanal);
        Assert.Equal(150.5m, p.Kanallar[0].Bakiye);
        Assert.Equal(-5.0m, p.BuAySonucu);
        Assert.EndsWith("/api/rapor/panel", h.SonIstek!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Haftalik_donem_ve_kanallar_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"donem":{"start":"2026-03-02","end":"2026-03-08","yil":2026,"ay":3},
              "kanallar":[{"kanal":"MEZAT","gelen":500.0,"giden":200.0,"sonuc":300.0,"devir":100.0}],
              "toplamGelen":500.0,"toplamGiden":200.0,"kasaSonucu":300.0,"kasaDevir":1000.0}]
        """);
        var liste = await c.HaftalikAsync();
        Assert.Single(liste);
        Assert.Equal(new DateOnly(2026, 3, 2), liste[0].Donem.Start);
        Assert.Equal(3, liste[0].Donem.Ay);
        Assert.Equal("MEZAT", liste[0].Kanallar[0].Kanal);
        Assert.Equal(300.0m, liste[0].Kanallar[0].Sonuc);
        Assert.Equal(1000.0m, liste[0].KasaDevir);
        Assert.EndsWith("/api/rapor/haftalik", h.SonIstek!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Aylik_yil_ay_query_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"yil":2026,"ay":4,"kanallar":[{"kanal":"MEZAT","gelen":1.0,"cariGiden":2.0,"sabitGider":3.0,"krediKarti":4.0,"ortakPay":5.0,"aySonucu":6.0}]}""");
        var r = await c.AylikAsync(2026, 4);
        Assert.Equal(2026, r.Yil);
        Assert.Equal(4, r.Ay);
        Assert.Equal(4.0m, r.Kanallar[0].KrediKarti);
        var uri = h.SonIstek!.RequestUri!;
        Assert.Contains("yil=2026", uri.Query);
        Assert.Contains("ay=4", uri.Query);
    }

    [Fact]
    public async Task Donemler_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"start":"2026-03-02","end":"2026-03-08","yil":2026,"ay":3}]""");
        var liste = await c.DonemlerAsync();
        Assert.Equal(new DateOnly(2026, 3, 2), liste[0].Start);
        Assert.Equal(new DateOnly(2026, 3, 8), liste[0].End);
        Assert.EndsWith("/api/donemler", h.SonIstek!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task KrediKartlari_liste_ve_dateonly_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":1,"ad":"Bonus","kesimTarihi":"2026-03-01","sonOdemeTarihi":"2026-03-15","limit":50000.0,"borc":12345.67}]
        """);
        var liste = await c.KrediKartlariAsync();
        Assert.Equal("Bonus", liste[0].Ad);
        Assert.Equal(new DateOnly(2026, 3, 1), liste[0].KesimTarihi);
        Assert.Equal(new DateOnly(2026, 3, 15), liste[0].SonOdemeTarihi);
        Assert.Equal(12345.67m, liste[0].Borc);
        Assert.EndsWith("/api/kredikartlari", h.SonIstek!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Islemler_tip_stringi_enuma_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":1,"tarih":"2026-03-05","cari":"K.K","tutarTl":10000.0,"kanal":"MEZAT","tip":"KrediKarti","not":null}]
        """);
        var liste = await c.IslemlerAsync();
        Assert.Equal(GiderTipi.KrediKarti, liste[0].Tip);
        Assert.Equal(new DateOnly(2026, 3, 5), liste[0].Tarih);
        Assert.Equal("api/islemler", h.SonIstek!.RequestUri!.AbsolutePath.TrimStart('/'));
        Assert.Equal("", h.SonIstek.RequestUri!.Query);
    }

    [Fact]
    public async Task Islemler_filtreleri_query_stringe_yazar()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, "[]");
        await c.IslemlerAsync(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "MEZAT", "K.K");
        var q = h.SonIstek!.RequestUri!.Query;
        Assert.Contains("baslangic=2026-03-01", q);
        Assert.Contains("bitis=2026-03-31", q);
        Assert.Contains("kanal=MEZAT", q);
        Assert.Contains("cari=K.K", q);
    }

    [Fact]
    public async Task Cariler_ara_query_iletir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":1,"ad":"Ahmet","aktif":true}]""");
        var liste = await c.CarilerAsync("Ahmet");
        Assert.Equal("Ahmet", liste[0].Ad);
        Assert.Contains("ara=Ahmet", h.SonIstek!.RequestUri!.Query);
    }

    [Fact]
    public async Task Cariler_ara_yoksa_query_yok()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, "[]");
        await c.CarilerAsync();
        Assert.Equal("", h.SonIstek!.RequestUri!.Query);
        Assert.EndsWith("/api/cariler", h.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Ayarlar_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"takipBaslangic":"2026-01-01","kasaAcilisDevri":85000.0,"izleyiciSifreVarMi":true}
        """);
        var a = await c.AyarlarAsync();
        Assert.Equal(new DateOnly(2026, 1, 1), a.TakipBaslangic);
        Assert.Equal(85000.0m, a.KasaAcilisDevri);
        Assert.True(a.IzleyiciSifreVarMi);
        Assert.EndsWith("/api/ayarlar", h.SonIstek!.RequestUri!.AbsolutePath);
    }
}
