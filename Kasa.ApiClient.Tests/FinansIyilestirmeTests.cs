using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class FinansIyilestirmeTests
{
    private static KasaApiClient Client(SahteHandler h) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
    [Fact] public async Task Benzerlik_sorgusu_yazma_yapmadan_ayri_endpoint_ve_tam_kriterleri_kullanir()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """[{"kaynak":"Islem","id":8,"tarih":"2026-09-24","tutar":12.34,"aciklama":"Mal","krediKartiId":3,"alisId":5}]""");
        var g = new BenzerlikYaz("AlisOdeme", new(2026, 9, 24), 12.34m, 3, null, 5); var sonuc = await Client(h).BenzerKayitlarAsync(g);
        Assert.Equal("/api/islemler/benzerlik", h.SonIstek!.RequestUri!.AbsolutePath); Assert.Equal(HttpMethod.Post, h.SonIstek.Method);
        Assert.Equal(g, JsonSerializer.Deserialize<BenzerlikYaz>(h.SonGovde!, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(8, sonuc.Single().Id); Assert.Equal(3, sonuc.Single().KrediKartiId); Assert.Equal(5, sonuc.Single().AlisId);
    }
    [Fact] public async Task Ozet_borc_alacak_ve_belirsiz_payi_ayri_okur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"tarih":"2026-09-24","kartBorcu":100.01,"kalanKrediPlani":30,"olaylar":[],"kanalKartBorclari":[{"kanalId":2,"kanal":"MEZAT","tutar":70},{"kanalId":null,"kanal":"Dağılım bekliyor","tutar":30.01}],"kartAlacakBakiyesi":20}""");
        var sonuc = await Client(h).TakipOzetAsync(); Assert.Equal(100.01m, sonuc.KartBorcu); Assert.Equal(20, sonuc.KartAlacakBakiyesi);
        Assert.Null(sonuc.KanalKartBorclari![1].KanalId); Assert.Equal(100.01m, sonuc.KanalKartBorclari.Sum(k => k.Tutar));
    }
    [Fact] public async Task Kart_detayinda_asgari_kalan_ve_kanal_borcu_okunur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"id":1,"surum":2,"ad":"Kart","yeniTakip":true,"aktif":true,"kesimGunu":1,"sonOdemeGunu":10,"limit":1000,"borc":80,"ekstreBorc":80,"harcamalar":[],"odemeler":[],"ekstreler":[{"id":9,"kesimTarihi":"2026-09-01","sonOdemeTarihi":"2026-09-10","borc":100,"odenen":20,"kalan":80,"asgariOdeme":20,"asgariKalan":0}],"kanalKartBorclari":[{"kanalId":2,"kanal":"MEZAT","tutar":80}]}""");
        var kart = await Client(h).TakipKartAsync(1); Assert.Equal(0, kart.Ekstreler.Single().AsgariKalan); Assert.Equal(80, kart.KanalKartBorclari!.Single().Tutar);
    }
    [Fact] public async Task Kanal_kimligi_ve_alis_odeme_kart_adi_geriye_uyumlu_okunur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"guncelKasa":90,"kanallar":[{"kanal":"MEZAT","bakiye":90,"kanalId":2}],"buHaftaSonucu":0,"buAySonucu":0}""")
            .Kuyrukla(HttpStatusCode.OK, """[{"id":5,"surum":2,"alicId":null,"alici":"Editör","tarih":"2026-09-24","tedarikci":"Mal","durum":"Onaylandi","toplam":100,"odenen":100,"kalan":0,"kalemler":[],"odemeler":[{"id":7,"islemId":8,"tarih":"2026-09-24","tutar":100,"krediKartiId":3,"krediKartiAdi":"Banka Kartı","dagilimBekliyor":false,"dagilimlar":[]}]}]""");
        var client = Client(h); Assert.Equal(2, (await client.PanelAsync()).Kanallar.Single().KanalId);
        Assert.Equal("Banka Kartı", (await client.AlislarAsync()).Single().Odemeler.Single().KrediKartiAdi);
        var eski = JsonSerializer.Deserialize<KartEkstreDto>("""{"id":1,"kesimTarihi":"2026-09-01","sonOdemeTarihi":"2026-09-10","borc":100,"odenen":0,"kalan":100,"asgariOdeme":20}""", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Null(eski!.AsgariKalan);
    }
}
