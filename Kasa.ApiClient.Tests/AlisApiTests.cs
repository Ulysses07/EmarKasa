using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class AlisApiTests
{
    private const string AlisJson = """{"id":7,"surum":3,"aliciId":2,"alici":"Ayşe","tarih":"2026-09-21","tedarikci":"Firma","not":null,"durum":"Taslak","editorNotu":null,"toplam":100,"odenen":25,"kalan":75,"kalemler":[{"id":1,"aciklama":"Mal","tutar":100,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":100}]}],"odemeler":[{"id":1,"islemId":90,"tarih":"2026-09-21","tutar":25,"krediKartiId":null,"dagilimBekliyor":true,"dagilimlar":[]}]}""";

    private static (KasaApiClient Api, SahteHandler Handler) Kur(string yanit)
    {
        var handler = new SahteHandler().Kuyrukla(HttpStatusCode.OK, yanit);
        var store = new BellekTokenStore();
        store.YazAsync("alici-token").GetAwaiter().GetResult();
        return (new(new HttpClient(handler) { BaseAddress = new("https://ornek.test/") }, store), handler);
    }

    [Fact]
    public async Task Liste_alicinin_bearer_oturumuyla_ayrintilari_ve_odeme_durumunu_okur()
    {
        var (api, handler) = Kur("[" + AlisJson + "]");
        var alis = Assert.Single(await api.AlislarAsync());
        Assert.Equal("/api/alis", handler.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("alici-token", handler.SonIstek.Headers.Authorization!.Parameter);
        Assert.Equal(3, alis.Surum);
        Assert.Equal(75m, alis.Kalan);
        Assert.Equal(100m, Assert.Single(Assert.Single(alis.Kalemler).Dagilimlar).Tutar);
        Assert.True(Assert.Single(alis.Odemeler).DagilimBekliyor);
    }

    [Theory]
    [InlineData("gonder")]
    [InlineData("onayla")]
    [InlineData("iade")]
    public async Task Durum_komutlari_surumu_ve_aciklamayi_gonderir(string komut)
    {
        var (api, handler) = Kur(AlisJson);
        var g = new AlisDurumYaz(2, "Dağılımı düzeltin");
        var sonuc = komut switch
        {
            "gonder" => await api.AlisGonderAsync(7, g),
            "onayla" => await api.AlisOnaylaAsync(7, g),
            _ => await api.AlisIadeAsync(7, g),
        };
        Assert.Equal(HttpMethod.Post, handler.SonIstek!.Method);
        Assert.Equal($"/api/alis/7/{komut}", handler.SonIstek.RequestUri!.AbsolutePath);
        using var belge = JsonDocument.Parse(handler.SonGovde!);
        Assert.Equal(2, belge.RootElement.GetProperty("surum").GetInt32());
        Assert.Equal(g.Not, belge.RootElement.GetProperty("not").GetString());
        Assert.Equal(7, sonuc.Id);
    }

    [Fact]
    public async Task Kaydet_coklu_kanal_dagilimini_korur()
    {
        var (api, handler) = Kur(AlisJson);
        await api.AlisGuncelleAsync(7, new(2, new(2026, 9, 21), "Firma", null,
            new[] { new AlisKalemYaz("Mal", 100m, new[] { new AlisDagilimYaz(1, 60m), new AlisDagilimYaz(2, 40m) }) }));
        Assert.Equal(HttpMethod.Put, handler.SonIstek!.Method);
        Assert.Equal("/api/alis/7", handler.SonIstek.RequestUri!.AbsolutePath);
        using var belge = JsonDocument.Parse(handler.SonGovde!);
        var paylar = belge.RootElement.GetProperty("kalemler")[0].GetProperty("dagilimlar");
        Assert.Equal(2, paylar.GetArrayLength());
        Assert.Equal(40m, paylar[1].GetProperty("tutar").GetDecimal());
    }

    [Fact]
    public async Task Odeme_tekrar_anahtarini_ve_mevcut_gider_baglantisini_korur()
    {
        var (api, handler) = Kur(AlisJson);
        var istekId = Guid.NewGuid();
        await api.AlisOdemeKaydetAsync(7, new(2, istekId, new(2026, 9, 21), 25m, MevcutIslemId: 90));
        Assert.Equal("/api/alis/7/odemeler", handler.SonIstek!.RequestUri!.AbsolutePath);
        using var belge = JsonDocument.Parse(handler.SonGovde!);
        Assert.Equal(istekId, belge.RootElement.GetProperty("istekId").GetGuid());
        Assert.Equal(90, belge.RootElement.GetProperty("mevcutIslemId").GetInt32());
    }

    [Fact]
    public async Task Alici_guncellemede_bos_sifre_null_olarak_aktarilir()
    {
        var (api, handler) = Kur("""{"id":2,"kullanici":"ayse","ad":"Ayşe","aktif":false}""");
        var alici = await api.AliciGuncelleAsync(2, new("ayse", "Ayşe", null, false));
        Assert.False(alici.Aktif);
        Assert.Equal("/api/alicilar/2", handler.SonIstek!.RequestUri!.AbsolutePath);
        using var belge = JsonDocument.Parse(handler.SonGovde!);
        Assert.Equal(JsonValueKind.Null, belge.RootElement.GetProperty("sifre").ValueKind);
    }

    [Fact]
    public async Task Islemler_alis_baglantisini_ve_pending_durumunu_okur()
    {
        var (api, _) = Kur("""[{"id":90,"tarih":"2026-09-21","cari":"Firma","tutarTl":25,"kanal":"Dağılım bekliyor","tip":"Cari","not":null,"alisId":7,"dagilimBekliyor":true}]""");
        var islem = Assert.Single(await api.IslemlerAsync());
        Assert.Equal(7, islem.AlisId);
        Assert.True(islem.DagilimBekliyor);
    }

    [Fact]
    public async Task Alis_olustur_istek_kimligini_gonderir_ve_tekrar_yanitini_okur()
    {
        var (api, handler) = Kur(AlisJson);
        var istekId = Guid.NewGuid();
        var alis = await api.AlisOlusturAsync(new AlisYaz(0, new DateOnly(2026, 9, 21), "Firma", null, [], IstekId: istekId));
        Assert.Equal(7, alis.Id);
        Assert.Equal(HttpMethod.Post, handler.SonIstek!.Method);
        using var govde = JsonDocument.Parse(handler.SonGovde!);
        Assert.Equal(istekId, govde.RootElement.GetProperty("istekId").GetGuid());
    }

    [Fact]
    public async Task Baglanabilir_giderler_yolu_suzgecleri_ve_sayfayi_okur()
    {
        var (api, handler) = Kur("""{"ogeler":[{"id":90,"tarih":"2026-09-20","cari":"Kargo","tutarTl":12.5,"kanal":"MEZAT","kanalId":1,"tip":"KrediKarti","not":null,"krediKartiId":4}],"sonrakiImlec":"20260920-90","devamVar":true}""");
        var sayfa = await api.BaglanabilirGiderlerAsync("Kargo & Co", 12.5m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "20260921-91", 20);
        Assert.Equal("/api/alis/baglanabilir-giderler", handler.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("?arama=Kargo%20%26%20Co&tutar=12.50&baslangic=2026-09-01&bitis=2026-09-30&imlec=20260921-91&limit=20", handler.SonIstek.RequestUri.Query);
        var gider = Assert.Single(sayfa.Ogeler);
        Assert.Equal((90, GiderTipi.KrediKarti, (int?)4, 12.5m), (gider.Id, gider.Tip, gider.KrediKartiId, gider.TutarTl));
        Assert.Equal(("20260920-90", true), (sayfa.SonrakiImlec, sayfa.DevamVar));

        (api, handler) = Kur("""{"ogeler":[],"sonrakiImlec":null,"devamVar":false}""");
        Assert.Empty((await api.BaglanabilirGiderlerAsync()).Ogeler);
        Assert.Equal("", handler.SonIstek!.RequestUri!.Query);
    }

    [Fact]
    public async Task Odeme_eski_kart_harcamasi_bayragini_okur_eski_sunucuda_false()
    {
        var yeni = AlisJson.Replace("\"dagilimlar\":[]}]", "\"dagilimlar\":[],\"eskiKartHarcamasi\":true}]");
        Assert.NotEqual(AlisJson, yeni);
        var (api, _) = Kur("[" + yeni + "]");
        Assert.True(Assert.Single(Assert.Single(await api.AlislarAsync()).Odemeler).EskiKartHarcamasi);
        (api, _) = Kur("[" + AlisJson + "]");
        Assert.False(Assert.Single(Assert.Single(await api.AlislarAsync()).Odemeler).EskiKartHarcamasi);
    }
}
