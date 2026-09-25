using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class FinansTakipApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static KasaApiClient Client(SahteHandler h, ITokenStore? store = null) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, store ?? new BellekTokenStore());
    [Fact] public async Task Odeme_onizlemesi_tarih_surumu_kuruslari_ve_dagilim_bekleyen_payi_korur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"tutar":10.01,"kasaEtkisi":8.01,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":5.00},{"kanalId":null,"kanal":"","tutar":3.01}],"ekstreler":[{"ekstreId":9,"tutar":10.01}]}""");
        var g = new KartTakipOdemeYaz(Guid.NewGuid(), 8, new(2026, 9, 23), 10.01m, 9, "Dekont");
        var result = await Client(h).TakipOdemeOnizlemeAsync(7, g);
        Assert.Equal("/api/takip/kartlar/7/odeme-onizleme", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, h.SonIstek.Method); Assert.Equal(g, JsonSerializer.Deserialize<KartTakipOdemeYaz>(h.SonGovde!, Json));
        Assert.Equal(8.01m, result.KasaEtkisi); Assert.Null(result.Dagilimlar[1].KanalId); Assert.Equal(3.01m, result.Dagilimlar[1].Tutar);
    }
    [Fact] public async Task Kart_odeme_mutasyonu_tam_yanit_ve_iptal_gecmisini_okur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"id":7,"surum":9,"ad":"Banka","yeniTakip":true,"aktif":true,"takipBaslangic":"2026-09-23","kesimGunu":1,"sonOdemeGunu":10,"limit":1000,"borc":90,"ekstreBorc":90,"ekstreler":[],"harcamalar":[],"odemeler":[{"id":2,"tarih":"2026-09-23","tutar":10,"kasaEtkisi":10,"not":null,"iptal":true,"dagilimlar":[]}]}""");
        var result = await Client(h).TakipOdemeIptalAsync(7, 2, new(Guid.NewGuid(), 8, "Yanlış tarih"));
        Assert.Equal("/api/takip/kartlar/7/odemeler/2/iptal", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal(9, result.Surum); Assert.True(result.Odemeler.Single().Iptal); Assert.Equal(new DateOnly(2026, 9, 23), result.TakipBaslangic);
    }
    [Fact] public async Task Mevcut_kredi_istegi_cift_girisi_engelleyen_bayragi_ve_kanallari_gonderir()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"id":1,"surum":1,"ad":"Kredi","yeniTakip":true,"aktif":true,"takipBaslangic":"2026-09-23","cekilenTutar":100,"cekimTarihi":"2026-08-01","kalanPlanliOdeme":120,"kanalPaylari":[],"taksitler":[]}""");
        var g = new KrediTakipYaz(Guid.NewGuid(), "Kredi", 100, new(2026, 8, 1), new(2026, 10, 1), 12, 10, new[] { 1, 3 }, true);
        var result = await Client(h).TakipKrediKaydetAsync(g);
        Assert.Equal("/api/takip/krediler", h.SonIstek!.RequestUri!.AbsolutePath);
        var sent = JsonSerializer.Deserialize<KrediTakipYaz>(h.SonGovde!, Json)!;
        Assert.True(sent.MevcutKredi); Assert.Equal(g.KanalIdleri, sent.KanalIdleri); Assert.Equal(g.IstekId, sent.IstekId); Assert.Equal(120, result.KalanPlanliOdeme);
    }
    [Fact] public async Task Gecis_onayi_farklar_ve_aciklamalarla_doner()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"kaynak":"Kart","kaynakId":7,"baslangic":"2026-09-23","genelKasaAnlikFarki":0,"kanalAnlikFarki":0,"eskiKasadaSayilanTutar":30.03,"aciklamalar":["Geçmiş korunur"],"kabulEdilebilir":true}""");
        var g = new KartGecisYaz(Guid.NewGuid(), 4, new(2026, 9, 23), 100, 30.03m, new[] { new KanalPayYaz(1, 100) }, "Kontrol edildi", false);
        var result = await Client(h).TakipKartGecisOnizlemeAsync(7, g);
        Assert.Equal(30.03m, result.EskiKasadaSayilanTutar); Assert.True(result.KabulEdilebilir); Assert.Single(result.Aciklamalar);
        Assert.False(JsonSerializer.Deserialize<KartGecisYaz>(h.SonGovde!, Json)!.Onay);
    }
    [Fact] public async Task Bildirimler_tarih_ve_okundu_uclarini_kullanir()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """[{"id":3,"baslik":"Kart","mesaj":"Son ödeme","tarih":"2026-09-23","okundu":false,"hedef":"/#cards/7","tur":"SonOdeme","kaynakId":7}]""").Kuyrukla(HttpStatusCode.NoContent);
        var c = Client(h); var list = await c.BildirimlerAsync(); Assert.Equal(new DateOnly(2026, 9, 23), list.Single().Tarih);
        await c.BildirimOkunduAsync(3); Assert.Equal(HttpMethod.Post, h.SonIstek!.Method); Assert.Equal("/api/bildirimler/3/okundu", h.SonIstek.RequestUri!.AbsolutePath);
    }
    [Fact] public async Task Bildirim_ayarinda_409_kullanici_mesaji_korunur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.Conflict, """{"hata":"Bildirim ayarları değişti. Yenileyip tekrar deneyin."}""");
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => Client(h).BildirimAyarKaydetAsync(new(true, 9, 30, 2)));
        Assert.Contains("Yenileyip", hata.Message); Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.Equal(new BildirimAyarYaz(true, 9, 30, 2), JsonSerializer.Deserialize<BildirimAyarYaz>(h.SonGovde!, Json));
    }
    [Fact] public async Task Yeni_finans_endpointinde_401_tokeni_merkezi_temizler()
    {
        var store = new BellekTokenStore(); await store.YazAsync("eski"); var h = new SahteHandler().Kuyrukla(HttpStatusCode.Unauthorized); var c = Client(h, store);
        var bitti = false; c.OturumSonlandi += (_, _) => bitti = true;
        await Assert.ThrowsAsync<KasaApiException>(() => c.TakipKartlarAsync()); Assert.Null(await store.OkuAsync()); Assert.True(bitti);
    }
    [Fact] public async Task Iade_kaynak_harcama_kimligini_ve_eksi_tutari_gonderir()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"id":7,"surum":9,"ad":"Banka","yeniTakip":true,"aktif":true,"kesimGunu":1,"sonOdemeGunu":10,"limit":1000,"borc":90,"ekstreBorc":90,"ekstreler":[],"harcamalar":[],"odemeler":[]}""");
        var g = new KartHarcamaYaz(Guid.NewGuid(), 8, new(2026, 9, 23), "Mal iadesi", -10.01m, 1, null, Array.Empty<KanalPayYaz>(), 42);
        await Client(h).TakipHarcamaKaydetAsync(7, g);
        var sent = JsonSerializer.Deserialize<KartHarcamaYaz>(h.SonGovde!, Json)!;
        Assert.Equal(42, sent.KaynakHarcamaId); Assert.Equal(-10.01m, sent.Tutar); Assert.Empty(sent.Dagilimlar);
        Assert.Equal("/api/takip/kartlar/7/harcamalar", h.SonIstek!.RequestUri!.AbsolutePath);
    }
}
