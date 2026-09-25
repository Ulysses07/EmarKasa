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
    public async Task Eski_yinelenen_gelir_bayragi_satirlari_ve_buyuk_kucuk_harfi_korur()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":10,"donemStart":"2026-03-02","kanal":"MEZAT","tutarTl":100.01,"kanalId":2,"eskiYinelenenGrup":true},
             {"id":11,"donemStart":"2026-03-02","kanal":"mezat","tutarTl":20.02,"kanalId":2,"eskiYinelenenGrup":true},
             {"id":12,"donemStart":"2026-03-02","kanal":"TOPTAN","tutarTl":5.03}]
            """);

        var satirlar = await c.GelenlerAsync(new(2026, 3, 2));

        Assert.Equal(3, satirlar.Count);
        Assert.Equal(new[] { 10, 11, 12 }, satirlar.Select(g => g.Id));
        Assert.Equal("MEZAT", satirlar[0].Kanal); Assert.Equal("mezat", satirlar[1].Kanal);
        Assert.True(satirlar[0].EskiYinelenenGrup); Assert.True(satirlar[1].EskiYinelenenGrup);
        Assert.Equal(2, satirlar[0].KanalId); Assert.Equal(100.01m, satirlar[0].TutarTl); Assert.Equal(20.02m, satirlar[1].TutarTl);
        Assert.False(satirlar[2].EskiYinelenenGrup); Assert.Null(satirlar[2].KanalId);
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

    [Fact]
    public async Task KrediKartlari_ekstreBorc_alanini_cozer()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":1,"ad":"A","kesimTarihi":"2026-07-15","sonOdemeTarihi":"2026-07-22","limit":100000.0,"borc":1000.0,"guncelBorc":1800.0,"acilisBorc":1000.0,"harcamaToplam":800.0,"odemeToplam":0.0,"ekstreBorc":1500.0}]""");
        var liste = await c.KrediKartlariAsync();
        Assert.Equal(1500m, liste.Single().EkstreBorc);
    }

    [Fact]
    public async Task Krediler_alanlari_cozer()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":3,"ad":"Taşıt Kredisi","cekilenTutar":120000.0,"cekimTarihi":"2026-08-03","taksitSayisi":12,"aylikOdeme":11000.0,"odemeGunu":15,"kanal":"MEZAT"}]
        """);
        var liste = await c.KredilerAsync();
        var k = liste.Single();
        Assert.Equal(3, k.Id);
        Assert.Equal("Taşıt Kredisi", k.Ad);
        Assert.Equal(120000.0m, k.CekilenTutar);
        Assert.Equal(new DateOnly(2026, 8, 3), k.CekimTarihi);
        Assert.Equal(12, k.TaksitSayisi);
        Assert.Equal(11000.0m, k.AylikOdeme);
        Assert.Equal(15, k.OdemeGunu);
        Assert.Equal("MEZAT", k.Kanal);
        Assert.EndsWith("/api/krediler", h.SonIstek!.RequestUri!.AbsolutePath);
    }
}
