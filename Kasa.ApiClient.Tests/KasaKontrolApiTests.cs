using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class KasaKontrolApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static KasaApiClient Client(SahteHandler h) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
    [Fact] public async Task Faiz_onizlemesi_ve_kaydi_hash_surumu_ve_kurusu_korur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"kartId":2,"ekstreId":7,"tarih":"2026-09-26","tutar":10.01,"devredenBorc":100,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":10.01}],"dagilimOzeti":"paylar"}""");
        var c = Client(h); var g = new KartMasrafYaz(Guid.NewGuid(), 4, 7, new(2026, 9, 26), 10.01m, "Faiz"); var p = await c.KartMasrafOnizleAsync(2, g);
        Assert.Equal("/api/takip/kartlar/2/masraf-onizleme", h.SonIstek!.RequestUri!.AbsolutePath); Assert.Equal(g, JsonSerializer.Deserialize<KartMasrafYaz>(h.SonGovde!, Json)); Assert.Equal(10.01m, p.Dagilimlar[0].Tutar);
        h.Kuyrukla(HttpStatusCode.OK, """{"id":2,"surum":5,"ad":"Kart","yeniTakip":true,"aktif":true,"kesimGunu":1,"sonOdemeGunu":10,"limit":1000,"borc":110.01,"ekstreBorc":110.01,"ekstreler":[],"harcamalar":[],"odemeler":[]}""");
        var s = await c.KartMasrafKaydetAsync(2, g with { DagilimOzeti = p.DagilimOzeti }); Assert.Equal(5, s.Surum); Assert.Equal("/api/takip/kartlar/2/masraflar", h.SonIstek!.RequestUri!.AbsolutePath); Assert.Equal("paylar", JsonSerializer.Deserialize<KartMasrafYaz>(h.SonGovde!, Json)!.DagilimOzeti);
    }
    [Fact] public async Task Bakiye_signed_onizleme_hash_ve_istek_kimligiyle_kaydedilir()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"sistemBakiye":100,"gercekBakiye":-1.25,"fark":-101.25,"kontrolOzeti":"bakiye"}""").Kuyrukla(HttpStatusCode.OK, """{"id":1,"kaydedildi":"2026-09-26T12:00:00+03:00","sistemBakiye":100,"gercekBakiye":-1.25,"fark":-101.25,"not":"sayım"}""");
        var c = Client(h); var p = await c.KasaKontrolOnizleAsync(new(-1.25m, "sayım")); Assert.Equal(-101.25m, p.Fark); Assert.Equal("/api/kasa-kontrol/onizleme", h.SonIstek!.RequestUri!.AbsolutePath);
        var g = new KasaKontrolYaz(Guid.NewGuid(), -1.25m, p.KontrolOzeti, "sayım"); var s = await c.KasaKontrolKaydetAsync(g); Assert.Equal(g, JsonSerializer.Deserialize<KasaKontrolYaz>(h.SonGovde!, Json)); Assert.Equal(HttpMethod.Post, h.SonIstek!.Method); Assert.Equal("/api/kasa-kontrol", h.SonIstek.RequestUri!.AbsolutePath); Assert.Equal(-1.25m, s.GercekBakiye);
    }
    [Fact] public async Task Esik_sifir_ve_kapali_durumu_put_ile_surumu_koruyarak_gonderir()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"kanalId":3,"kanal":"MEZAT","surum":2,"tutar":0,"etkin":false,"bakiye":-10,"esikAltinda":false}""");
        var g = new KasaEsikYaz(1, 0, false); var s = await Client(h).KasaEsigiKaydetAsync(3, g);
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method); Assert.Equal("/api/kasa-esikleri/3", h.SonIstek.RequestUri!.AbsolutePath); Assert.Equal(g, JsonSerializer.Deserialize<KasaEsikYaz>(h.SonGovde!, Json)); Assert.False(s.Etkin);
    }
    [Theory] [InlineData(true, "/api/ay-kilidi/kapat")] [InlineData(false, "/api/ay-kilidi/ac")]
    public async Task Ay_kilidi_tarihi_surumu_ve_gerekceyi_gonderir(bool kapat, string yol)
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"surum":2,"kilitliSonTarih":"2026-08-31","gecmis":[]}"""); var g = new AyKilidiYaz(Guid.NewGuid(), 1, 2026, 8, "Onaylandı");
        var s = await Client(h).AyKilidiDegistirAsync(kapat, g); Assert.Equal(yol, h.SonIstek!.RequestUri!.AbsolutePath); Assert.Equal(g, JsonSerializer.Deserialize<AyKilidiYaz>(h.SonGovde!, Json)); Assert.Equal(new DateOnly(2026, 8, 31), s.KilitliSonTarih);
    }
    [Fact] public async Task Aylik_odeme_tutar_uydurmadan_surumu_ve_ayi_gonderir_iptal_ayri_uctadir()
    {
        const string row = """{"sablonId":3,"sablonSurum":4,"ad":"Kira","tur":"Kira","tutar":100,"planlananTarih":"2026-09-01","dagilimTuru":"Genel","dagilimlar":[],"durum":"Odendi","odemeId":8,"odemeTarihi":"2026-09-26","islemId":12}""";
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, row).Kuyrukla(HttpStatusCode.OK, row); var c = Client(h); var g = new AylikGiderOdemeYaz(Guid.NewGuid(), 4, 2026, 9, new(2026, 9, 26), "Havale");
        var s = await c.AylikGiderOdeAsync(3, g); Assert.Equal(8, s.OdemeId); Assert.Equal("/api/aylik-giderler/3/ode", h.SonIstek!.RequestUri!.AbsolutePath); Assert.Equal(g, JsonSerializer.Deserialize<AylikGiderOdemeYaz>(h.SonGovde!, Json)); Assert.DoesNotContain("tutar", h.SonGovde!);
        var iptal = new AylikGiderIptalYaz(Guid.NewGuid(), "Yanlış kayıt"); await c.AylikGiderIptalAsync(8, iptal); Assert.Equal("/api/aylik-giderler/odemeler/8/iptal", h.SonIstek!.RequestUri!.AbsolutePath); Assert.Equal(iptal, JsonSerializer.Deserialize<AylikGiderIptalYaz>(h.SonGovde!, Json));
    }
    [Fact] public async Task Sablon_esit_dagilim_ids_sifir_ve_gelecek_ay_korunur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"id":3,"surum":4,"ad":"Maaş","tur":"Maas","tutar":100,"odemeGunu":30,"dagilimTuru":"Esit","dagilimlar":[{"kanalId":2,"kanal":"MEZAT","tutar":100}],"gecerliAy":"2026-10-01","aktif":true}""");
        var g = new AylikGiderSablonYaz(Guid.NewGuid(), 3, "Maaş", "Maas", 100, 30, "Esit", new[] { new KanalPayYaz(2, 0) }, new(2026, 10, 1)); var s = await Client(h).AylikGiderSablonKaydetAsync(3, g);
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method); Assert.Equal("/api/aylik-giderler/sablonlar/3", h.SonIstek.RequestUri!.AbsolutePath); var body = JsonSerializer.Deserialize<AylikGiderSablonYaz>(h.SonGovde!, Json)!; Assert.Equal(g.GecerliAy, body.GecerliAy); Assert.Equal(new KanalPayYaz(2, 0), Assert.Single(body.Dagilimlar)); Assert.Equal(100, s.Dagilimlar[0].Tutar);
    }
    [Fact] public async Task Ay_listesi_okuma_yapar_ve_genel_gider_ayri_okunur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"yil":2026,"ay":9,"planlananToplam":100,"odenenToplam":0,"kayitlar":[]}""").Kuyrukla(HttpStatusCode.OK, """{"yil":2026,"ay":9,"kanallar":[],"dagilimBekleyenTutar":2,"genelGider":100}""");
        var c = Client(h); await c.AylikGiderlerAsync(2026, 9); Assert.Equal(HttpMethod.Get, h.SonIstek!.Method); Assert.Equal("?yil=2026&ay=9", h.SonIstek.RequestUri!.Query);
        var r = await c.AylikAsync(2026, 9); Assert.Equal(100, r.GenelGider); Assert.Equal(2, r.DagilimBekleyenTutar);
    }
}
