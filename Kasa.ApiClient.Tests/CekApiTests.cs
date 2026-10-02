using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

/// <summary>Çek istemcisi: yollar, yöntemler, sorgu dizesi, DELETE gövdesi ve yanıt okuma.</summary>
public class CekApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static KasaApiClient Client(SahteHandler h) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
    private const string Cek = """{"id":7,"surum":3,"tur":"Cek","yon":"Alinan","no":"12345","banka":"Ziraat","kisi":"Ahmet","tutar":50000.00,"vadeTarihi":"2026-11-30","kanalId":null,"kanal":null,"teminat":false,"konum":"Elde","not":null,"durum":"KismenTahsilEdildi","kalan":29999.50,"izinliHareketler":["Tahsilat","Karsiliksiz","Iade"],"hareketler":[{"id":4,"sira":1,"tur":"Tahsilat","tarih":"2026-09-25","tutar":20000.50,"netTutar":null,"kanalId":1,"kanal":"MEZAT","karsi":null}],"uyari":"Aynı çek"}""";

    [Fact]
    public async Task Liste_suzgecleri_sorgu_dizesine_kacisli_yazar_bos_suzgec_yazilmaz()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, "[" + Cek + "]").Kuyrukla(HttpStatusCode.OK, "[]");
        var liste = await Client(h).CeklerAsync("Alinan", "Portfoyde", " Ahmet Y ", new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 25));
        Assert.Equal("/api/takip/cekler?yon=Alinan&durum=Portfoyde&ara=Ahmet%20Y&vadeBas=2026-09-25&vadeSon=2026-10-25", h.SonIstek!.RequestUri!.PathAndQuery);
        var cek = Assert.Single(liste);
        Assert.Equal((29_999.50m, "KismenTahsilEdildi", 3, "Aynı çek"), (cek.Kalan, cek.Durum, cek.IzinliHareketler.Count, cek.Uyari));
        Assert.Equal(("MEZAT", 20_000.50m), (cek.Hareketler[0].Kanal, cek.Hareketler[0].Tutar));
        await Client(h).CeklerAsync();
        Assert.Equal("/api/takip/cekler", h.SonIstek!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Kaydet_ekler_ya_da_duzeltir_hareket_ve_geri_alma_govdeyle_gider()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, Cek).Kuyrukla(HttpStatusCode.OK, Cek).Kuyrukla(HttpStatusCode.OK, Cek).Kuyrukla(HttpStatusCode.OK, Cek)
            .Kuyrukla(HttpStatusCode.NoContent);
        var c = Client(h);
        var yaz = new CekYaz(Guid.NewGuid(), 0, "Cek", "Verilen", "777", "Halk", "Mehmet", 30_000m, new(2026, 10, 5), "Ortak", false, null, null);
        await c.CekKaydetAsync(null, yaz);
        Assert.Equal((HttpMethod.Post, "/api/takip/cekler"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        Assert.Equal(yaz, JsonSerializer.Deserialize<CekYaz>(h.SonGovde!, Json));
        await c.CekKaydetAsync(7, yaz with { Surum = 3 });
        Assert.Equal((HttpMethod.Put, "/api/takip/cekler/7"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        var hareket = new CekHareketYaz(Guid.NewGuid(), 3, "Kirdirma", new(2026, 9, 25), 50_000m, 48_750.25m, "MEZAT", "Faktoring");
        await c.CekHareketEkleAsync(7, hareket);
        Assert.Equal((HttpMethod.Post, "/api/takip/cekler/7/hareketler"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        Assert.Equal(hareket, JsonSerializer.Deserialize<CekHareketYaz>(h.SonGovde!, Json));
        var geriAl = new CekSilYaz(Guid.NewGuid(), 4);
        Assert.Equal(7, (await c.CekHareketGeriAlAsync(7, geriAl)).Id);
        Assert.Equal((HttpMethod.Delete, "/api/takip/cekler/7/hareketler/son"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        Assert.Equal(geriAl, JsonSerializer.Deserialize<CekSilYaz>(h.SonGovde!, Json));
        await c.CekSilAsync(7, geriAl);
        Assert.Equal((HttpMethod.Delete, "/api/takip/cekler/7"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task Ozet_ve_tek_kayit_okunur()
    {
        var h = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, """{"tarih":"2026-09-25","portfoydekiAlinan":{"adet":3,"toplam":33000.00},"alinan30":{"adet":1,"toplam":10000},"verilen30":{"adet":0,"toplam":0},"vadesiGecmis":{"adet":1,"toplam":3000.5}}""")
            .Kuyrukla(HttpStatusCode.OK, Cek);
        var ozet = await Client(h).CekOzetAsync();
        Assert.Equal("/api/takip/cekler/ozet", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal((new CekOzetKalemi(3, 33_000m), new CekOzetKalemi(1, 3_000.5m)), (ozet.PortfoydekiAlinan, ozet.VadesiGecmis));
        Assert.Equal(7, (await Client(h).CekAsync(7)).Id);
        Assert.Equal("/api/takip/cekler/7", h.SonIstek!.RequestUri!.AbsolutePath);
    }
}
