using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

/// <summary>Kart takibi düzeltmelerinin istemci sözleşmesi: eski borç devrinin okunması ve gerekçeli düzeltmesi (rota, yöntem,
/// gövde), devir iadesinin kasaya dönen önceden sayılmış tutarı ve kilitli avans dağıtımı alanları; eski sunucu yanıtında
/// yeni alanlar varsayılan değerle okunur.</summary>
public class KartTakipDuzeltmeApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static KasaApiClient Client(SahteHandler h) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());

    [Fact]
    public async Task Devir_okunur_duzeltme_rotasi_ve_govdesi_birebir_gider()
    {
        var h = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, """{"harcamaId":11,"tarih":"2026-09-25","kalanBorc":100.00,"kasadaOncedenSayilanTutar":80,"iadeDuzeltmesi":30.5,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":100}],"kural":"IslemTarihi","sistemKartBorcu":80,"raporDisiTutar":0,"acilisBorcu":0,"onerilenKasadaSayilanTutar":80,"enAzKasadaSayilanTutar":80,"duzeltilebilir":false,"engel":"Devre ödeme kaydedilmiş."}""")
            .Kuyrukla(HttpStatusCode.OK, """{"id":7,"surum":5,"ad":"Eski","yeniTakip":true,"aktif":true,"takipBaslangic":"2026-09-25","kesimGunu":5,"sonOdemeGunu":15,"limit":1000,"borc":50,"ekstreBorc":0,"ekstreler":[],"harcamalar":[{"id":12,"islemId":null,"tarih":"2026-09-26","aciklama":"İade","tutar":-30,"taksitSayisi":1,"iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":30}],"ekstreKayitId":null,"kasadaSayilanDuzeltme":30.25}],"odemeler":[{"id":9,"tarih":"2026-09-27","tutar":0,"kasaEtkisi":0,"not":"Kilitli avans dağıtımı","iptal":false,"dagilimlar":[],"ekstreKayitId":null,"avansKaynakOdemeId":3}]}""");
        var client = Client(h);
        var devir = await client.TakipKartDevirAsync(7);
        Assert.Equal((HttpMethod.Get, "/api/takip/kartlar/7/devir"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        Assert.Equal((11, 100m, 80m, 30.5m, false, "Devre ödeme kaydedilmiş."), (devir.HarcamaId, devir.KalanBorc, devir.KasadaOncedenSayilanTutar, devir.IadeDuzeltmesi, devir.Duzeltilebilir, devir.Engel));
        Assert.Equal((80m, 80m, "IslemTarihi"), (devir.OnerilenKasadaSayilanTutar, devir.EnAzKasadaSayilanTutar, devir.Kural));

        var g = new KartDevirDuzeltYaz(Guid.NewGuid(), 4, 11, 80.25m, 80.25m, [new KanalPayYaz(1, 80.25m)], "Banka ekstresi");
        var kart = await client.TakipKartDevirDuzeltAsync(7, g);
        Assert.Equal((HttpMethod.Post, "/api/takip/kartlar/7/devir-duzelt"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        var gonderilen = JsonSerializer.Deserialize<KartDevirDuzeltYaz>(h.SonGovde!, Json)!;
        Assert.Equal((g.IstekId, g.Surum, g.HarcamaId, g.KalanBorc, g.KasadaOncedenSayilanTutar, g.Aciklama), (gonderilen.IstekId, gonderilen.Surum, gonderilen.HarcamaId, gonderilen.KalanBorc, gonderilen.KasadaOncedenSayilanTutar, gonderilen.Aciklama));
        Assert.Equal(g.Dagilimlar, gonderilen.Dagilimlar);
        Assert.Equal(30.25m, kart.Harcamalar.Single().KasadaSayilanDuzeltme);
        Assert.Equal(3, kart.Odemeler.Single().AvansKaynakOdemeId);
    }

    [Fact]
    public async Task Eski_sunucu_yanitinda_yeni_alanlar_varsayilan_okunur_devirsiz_kart_kesilmis_devir_null()
    {
        var h = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, """{"id":8,"surum":1,"ad":"2.3.0","yeniTakip":true,"aktif":true,"takipBaslangic":"2026-09-25","kesimGunu":5,"sonOdemeGunu":15,"limit":1000,"borc":0,"ekstreBorc":0,"ekstreler":[],"harcamalar":[{"id":1,"islemId":null,"tarih":"2026-09-25","aciklama":"İade","tutar":-5,"taksitSayisi":1,"iptal":false,"dagilimlar":[]}],"odemeler":[{"id":2,"tarih":"2026-09-25","tutar":5,"kasaEtkisi":5,"not":null,"iptal":false,"dagilimlar":[]}]}""")
            .Kuyrukla(HttpStatusCode.OK, """{"harcamaId":null,"tarih":"2026-09-25","kalanBorc":0,"kasadaOncedenSayilanTutar":0,"iadeDuzeltmesi":0,"dagilimlar":[],"kural":"EtkiTarihi","sistemKartBorcu":50,"raporDisiTutar":20,"acilisBorcu":10,"onerilenKasadaSayilanTutar":0,"enAzKasadaSayilanTutar":0,"duzeltilebilir":true,"engel":null}""");
        var client = Client(h);
        var kart = await client.TakipKartAsync(8);
        Assert.Equal(0m, kart.Harcamalar.Single().KasadaSayilanDuzeltme); Assert.Null(kart.Odemeler.Single().AvansKaynakOdemeId);
        var devir = await client.TakipKartDevirAsync(8);
        Assert.Null(devir.HarcamaId); Assert.Equal((20m, 10m, true), (devir.RaporDisiTutar, devir.AcilisBorcu, devir.Duzeltilebilir));
    }
}
