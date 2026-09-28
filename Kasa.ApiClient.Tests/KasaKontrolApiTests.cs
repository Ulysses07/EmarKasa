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
    // gap-denetim-izi-gozlemlenebilirlik-3 / gap-coklu-giris-cift-sayim-mutabakat-17: filigranlı liste, fark açıklaması, "kontrolden
    // beri değişenler" ve kasa hareket dökümü yolları, yöntemleri, gövdeleri ve alanları.
    [Fact] public async Task Filigranli_liste_fark_aciklamasi_sonrasi_ve_dokum_yollari_ve_alanlari()
    {
        var h = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, """[{"id":4,"kaydedildi":"2026-09-26T12:00:00+03:00","sistemBakiye":1000,"gercekBakiye":950.5,"fark":-49.5,"not":"sayım","surum":2,"hesapTarihi":"2026-09-26","kanalBakiyeleri":[{"kanalId":1,"kanal":"MEZAT","bakiye":600,"guncelBakiye":620}],"farkAciklamasi":"banka","farkAciklamaZamani":"2026-09-27T10:00:00+03:00","guncelSistemBakiye":1020,"guncelFark":-69.5,"sonradanDegisti":true}]""")
            .Kuyrukla(HttpStatusCode.OK, """{"id":4,"kaydedildi":"2026-09-26T12:00:00+03:00","sistemBakiye":1000,"gercekBakiye":950.5,"fark":-49.5,"not":"sayım","surum":3,"farkAciklamasi":"yeni"}""")
            .Kuyrukla(HttpStatusCode.OK, """{"kontrolId":4,"kaydedildi":"2026-09-26T12:00:00+03:00","esasTarih":"2026-09-26","filigranVar":true,"sistemBakiye":1000,"guncelSistemBakiye":1020,"bugunkuSistemBakiye":20,"degisiklikler":[{"id":9,"zaman":"2026-09-27T10:00:00+03:00","aktorRol":"editor","tur":"Sil","varlik":"Islem","varlikId":"812"}],"istekler":[{"istekId":"11111111-2222-3333-4444-555555555555","tur":"KartOdemeIptal","sonucId":2}],"hareketler":[{"etkiTarihi":"2026-09-28","kayitTarihi":"2026-09-28","tur":"KrediTaksidi","aciklama":"Kredi / 1. taksit","kanal":"MEZAT","kanalId":1,"genelKasaEtkisi":-1000,"kanalEtkisi":-1000,"kaynakAnahtari":"TakipKrediTaksit:9","otomatik":true}],"kirpildi":false}""")
            .Kuyrukla(HttpStatusCode.OK, """{"baslangic":"2026-09-01","bitis":"2026-09-26","kanalId":1,"kanal":"MEZAT","acilisBakiyesi":100,"kapanisBakiyesi":80.25,"hareketler":[{"etkiTarihi":"2026-09-30","kayitTarihi":"2026-08-20","tur":"KartAySonu","aciklama":"Eski kart","kanal":"MEZAT","kanalId":1,"genelKasaEtkisi":-19.75,"kanalEtkisi":0,"kaynakAnahtari":"Islem:5","otomatik":true}]}""");
        var c = Client(h);

        var k = Assert.Single(await c.KasaKontrolleriAsync());
        Assert.Equal((2, (DateOnly?)new DateOnly(2026, 9, 26), "banka", (decimal?)1020, (decimal?)-69.5m, true), (k.Surum, k.HesapTarihi, k.FarkAciklamasi, k.GuncelSistemBakiye, k.GuncelFark, k.SonradanDegisti));
        Assert.Equal(new KasaKontrolKanalDto(1, "MEZAT", 600, 620), Assert.Single(k.KanalBakiyeleri!));

        var g = new KasaKontrolAciklamaYaz(Guid.NewGuid(), 2, "yeni");
        Assert.Equal(3, (await c.KasaKontrolAciklaAsync(4, g)).Surum);
        Assert.Equal((HttpMethod.Put, "/api/kasa-kontrol/4/aciklama"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        Assert.Equal(g, JsonSerializer.Deserialize<KasaKontrolAciklamaYaz>(h.SonGovde!, Json));

        var s = await c.KasaKontrolSonrasiAsync(4);
        Assert.Equal((HttpMethod.Get, "/api/kasa-kontrol/4/sonrasi"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        Assert.Equal((true, 1020m, 20m), (s.FiligranVar, s.GuncelSistemBakiye, s.BugunkuSistemBakiye));
        Assert.Equal(("Islem", "812", "Sil"), (s.Degisiklikler[0].Varlik, s.Degisiklikler[0].VarlikId, s.Degisiklikler[0].Tur));
        Assert.Equal(("KartOdemeIptal", 2), (s.Istekler[0].Tur, s.Istekler[0].SonucId));
        Assert.True(s.Hareketler[0] is { Tur: "KrediTaksidi", Otomatik: true, GenelKasaEtkisi: -1000m, KaynakAnahtari: "TakipKrediTaksit:9" });

        var d = await c.KasaHareketleriAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 26), 1);
        Assert.Equal(("/api/kasa-hareketleri", "?baslangic=2026-09-01&bitis=2026-09-26&kanalId=1"), (h.SonIstek!.RequestUri!.AbsolutePath, h.SonIstek.RequestUri.Query));
        Assert.Equal((100m, 80.25m, "MEZAT"), (d.AcilisBakiyesi, d.KapanisBakiyesi, d.Kanal));
        Assert.Equal((new DateOnly(2026, 9, 30), new DateOnly(2026, 8, 20), -19.75m), (d.Hareketler[0].EtkiTarihi, d.Hareketler[0].KayitTarihi, d.Hareketler[0].GenelKasaEtkisi));
        const string bos = """{"baslangic":"2026-09-01","bitis":"2026-09-26","kanalId":null,"kanal":null,"acilisBakiyesi":0,"kapanisBakiyesi":0,"hareketler":[]}""";
        h.Kuyrukla(HttpStatusCode.OK, bos).Kuyrukla(HttpStatusCode.OK, bos);
        await c.KasaHareketleriAsync(); Assert.Equal("", h.SonIstek!.RequestUri!.Query);
        await c.KasaHareketleriAsync(bitis: new DateOnly(2026, 1, 31)); Assert.Equal("?bitis=2026-01-31", h.SonIstek!.RequestUri!.Query);
    }
    [Fact] public async Task Eski_sunucunun_kontrol_yanitlari_yeni_alanlari_varsayilanla_okunur()
    {
        var h = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, """{"sistemBakiye":100,"gercekBakiye":90,"fark":-10,"kontrolOzeti":"eski"}""")
            .Kuyrukla(HttpStatusCode.OK, """[{"id":1,"kaydedildi":"2026-09-26T12:00:00+03:00","sistemBakiye":100,"gercekBakiye":90,"fark":-10,"not":null}]""");
        var c = Client(h);
        var p = await c.KasaKontrolOnizleAsync(new(90m));
        Assert.Equal(((IReadOnlyList<KasaKontrolKanalDto>?)null, (DateOnly?)null), (p.KanalBakiyeleri, p.HesapTarihi));
        var k = Assert.Single(await c.KasaKontrolleriAsync());
        Assert.Equal((1, (DateOnly?)null, false, (decimal?)null), (k.Surum, k.HesapTarihi, k.SonradanDegisti, k.GuncelSistemBakiye));
        Assert.Null(k.KanalBakiyeleri);
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
