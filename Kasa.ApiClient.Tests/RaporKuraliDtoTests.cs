using System.Net;

namespace Kasa.ApiClient.Tests;

/// <summary>Rapor kuralı kararlarının yanıt alanları (K1–K4) istemci DTO'larına okunur; eski sunucu yanıtı varsayılanlarla açılır.</summary>
public class RaporKuraliDtoTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task Aylik_raporun_kredi_girisi_kural_dondurulmus_ve_uyari_alanlari_okunur()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"yil":2026,"ay":9,"kanallar":[{"kanal":"MEZAT","gelen":80000,"cariGiden":100000,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":-20000,"krediGirisi":120000}],
             "dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0,"krediGirisi":170000,"kuralSurumu":2,
             "veriSagligiUyarisi":"Takip başlangıcından önce tarihli 2 kayıt","dondurulmus":true}
            """);
        var r = await c.AylikAsync(2026, 9);
        Assert.Equal(120000m, r.Kanallar[0].KrediGirisi);
        Assert.Equal((170000m, 2, true), (r.KrediGirisi, r.KuralSurumu, r.Dondurulmus));
        Assert.Equal("Takip başlangıcından önce tarihli 2 kayıt", r.VeriSagligiUyarisi);
    }

    [Fact]
    public async Task Eski_sunucu_aylik_raporu_ve_kart_listesi_varsayilanlarla_acilir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"yil":2026,"ay":6,"kanallar":[{"kanal":"MEZAT","gelen":10,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":10}]}
            """);
        var r = await c.AylikAsync(2026, 6);
        Assert.Equal((0m, (decimal?)null, (int?)null, false, (string?)null), (r.Kanallar[0].KrediGirisi, r.KrediGirisi, r.KuralSurumu, r.Dondurulmus, r.VeriSagligiUyarisi));

        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":1,"ad":"Eski","kesimTarihi":"2026-01-10","sonOdemeTarihi":"2026-01-20","limit":0,"borc":0},
             {"id":2,"ad":"Takipli","kesimTarihi":"2026-01-10","sonOdemeTarihi":"2026-01-20","limit":0,"borc":0,"yeniTakip":true,"aktif":true},
             {"id":3,"ad":"Kapalı","kesimTarihi":"2026-01-10","sonOdemeTarihi":"2026-01-20","limit":0,"borc":0,"yeniTakip":true,"aktif":false}]
            """);
        var kartlar = await c.KrediKartlariAsync();
        Assert.Equal(new[] { (false, true), (true, true), (true, false) }, kartlar.Select(k => (k.YeniTakip, k.Aktif)));
    }
}
