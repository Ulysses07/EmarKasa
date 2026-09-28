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

        // Tutar gibi okunan arama metni: metin ve tutar okuması birlikte gider; sunucu ikisinden birine uyanı döndürür.
        (api, handler) = Kur("""{"ogeler":[],"sonrakiImlec":null,"devamVar":false}""");
        await api.BaglanabilirGiderlerAsync("2024", aramaTutari: 2024m, imlec: "20260921-91");
        Assert.Equal("?arama=2024&aramaTutari=2024.00&imlec=20260921-91", handler.SonIstek!.RequestUri!.Query);
    }

    /// <summary>Yolu kaydeden ve yola göre yanıt veren işleyici (eski sunucu benzetimi).</summary>
    private sealed class YolaGoreHandler(Func<string, HttpResponseMessage> yanit) : HttpMessageHandler
    {
        public List<string> Yollar { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken ct)
        {
            Yollar.Add(istek.RequestUri!.AbsolutePath);
            return Task.FromResult(yanit(istek.RequestUri.AbsolutePath));
        }
    }

    private static KasaApiClient Istemci(HttpMessageHandler h) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
    private static HttpResponseMessage Json(string govde) => new(HttpStatusCode.OK) { Content = new StringContent(govde, System.Text.Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    public async Task Eski_sunucuda_baglanabilir_gider_ucu_yoksa_gider_listesine_istemcide_suzerek_geri_duser(HttpStatusCode yok)
    {
        // Yeni masaüstü eski sunucuya bağlanırsa (uç yok) editörün Alışlar ekranı düşmez: eski uçtan bütün giderler okunur;
        // ödeme ucunun kabul etmeyeceği giderler ile arama/tutar süzgeci istemcide uygulanır, hepsi tek sayfadır. Eski sunucu
        // yolu 404 ile ya da (yalnız PUT kabul eden /api/alis/{id:int} deseni yüzünden) 405 ile reddeder. Uç bir kez yok
        // yanıtı verince sonraki aramalar yeniden denemez (ana sayfa özetindeki geri düşüş gibi).
        const string giderler = """
            [{"id":1,"tarih":"2026-09-20","cari":"Kargo AŞ","tutarTl":40,"kanal":"MEZAT","tip":"Cari","not":null},
             {"id":2,"tarih":"2026-09-22","cari":"Firma","tutarTl":25,"kanal":"MEZAT","tip":"KrediKarti","not":"kargo bedeli","krediKartiId":4},
             {"id":3,"tarih":"2026-09-22","cari":"Bağlı","tutarTl":10,"kanal":"MEZAT","tip":"Cari","not":null,"alisId":7},
             {"id":4,"tarih":"2026-09-22","cari":"Kira","tutarTl":10,"kanal":"MEZAT","tip":"Cari","not":null,"aylikGiderOdemeId":3},
             {"id":5,"tarih":"2026-09-22","cari":"Ekstre","tutarTl":10,"kanal":"MEZAT","tip":"Cari","not":null,"ekstreKayitId":2},
             {"id":6,"tarih":"2026-09-22","cari":"Sabit","tutarTl":10,"kanal":"MEZAT","tip":"SabitGider","not":null},
             {"id":7,"tarih":"2026-09-22","cari":"İade","tutarTl":-5,"kanal":"MEZAT","tip":"Cari","not":null},
             {"id":8,"tarih":"2026-09-22","cari":"Ambalaj","tutarTl":10,"kanal":"PERAKENDE","tip":"Cari","not":null}]
            """;
        var h = new YolaGoreHandler(yol => yol == "/api/islemler" ? Json(giderler) : new HttpResponseMessage(yok));
        var api = Istemci(h);

        var sayfa = await api.BaglanabilirGiderlerAsync();
        Assert.Equal([8, 2, 1], sayfa.Ogeler.Select(g => g.Id));
        Assert.Equal(((string?)null, false), (sayfa.SonrakiImlec, sayfa.DevamVar));
        Assert.Equal((GiderTipi.KrediKarti, (int?)4, "kargo bedeli"), (sayfa.Ogeler[1].Tip, sayfa.Ogeler[1].KrediKartiId, sayfa.Ogeler[1].Not));
        Assert.Equal([2, 1], (await api.BaglanabilirGiderlerAsync("KARGO")).Ogeler.Select(g => g.Id));
        Assert.Equal([1], (await api.BaglanabilirGiderlerAsync(tutar: 40m)).Ogeler.Select(g => g.Id));
        Assert.Equal([8, 2, 1], (await api.BaglanabilirGiderlerAsync("kargo", aramaTutari: 10m)).Ogeler.Select(g => g.Id));
        Assert.Equal(["/api/alis/baglanabilir-giderler", "/api/islemler", "/api/islemler", "/api/islemler", "/api/islemler"], h.Yollar);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Baglanabilir_gider_ucunun_404_disi_hatasi_eski_uca_dusmeden_hata_olarak_kalir(HttpStatusCode kod)
    {
        var h = new YolaGoreHandler(_ => new HttpResponseMessage(kod));
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => Istemci(h).BaglanabilirGiderlerAsync());
        Assert.Equal(kod, hata.DurumKodu);
        Assert.Equal(["/api/alis/baglanabilir-giderler"], h.Yollar);
    }

    /// <summary>Ters sıra (gap-coklu-giris-cift-sayim-mutabakat-1): bağlanabilir kart harcamaları yolu ve sorgusu, ödemenin
    /// MevcutKartHarcamaId alanı ve gider sayfasındaki ekstre kaynağı; eski sunucuda (uç yok) boş liste.</summary>
    [Fact]
    public async Task Baglanabilir_kart_harcamalari_ve_mevcut_kart_harcamasiyla_odeme()
    {
        var (api, handler) = Kur("""[{"id":31,"krediKartiId":4,"tarih":"2026-09-20","aciklama":"MEZAT","tutar":18000,"ekstreKayitId":7}]""");
        var harcama = Assert.Single(await api.BaglanabilirKartHarcamalariAsync(4, 18000m));
        Assert.Equal("/api/alis/baglanabilir-kart-harcamalari", handler.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("?krediKartiId=4&tutar=18000.00", handler.SonIstek.RequestUri.Query);
        Assert.Equal((31, 4, 18000m, (int?)7), (harcama.Id, harcama.KrediKartiId, harcama.Tutar, harcama.EkstreKayitId));

        (api, handler) = Kur(AlisJson);
        await api.AlisOdemeKaydetAsync(7, new AlisOdemeYaz(3, Guid.NewGuid(), new DateOnly(2026, 9, 20), 18000m, 4, MevcutKartHarcamaId: 31));
        using (var govde = JsonDocument.Parse(handler.SonGovde!))
            Assert.Equal((31, JsonValueKind.Null), (govde.RootElement.GetProperty("mevcutKartHarcamaId").GetInt32(), govde.RootElement.GetProperty("mevcutIslemId").ValueKind));

        (api, _) = Kur("""{"ogeler":[{"id":90,"tarih":"2026-09-20","cari":"PDF","tutarTl":12.5,"kanal":"Genel kasa","kanalId":null,"tip":"Cari","not":null,"krediKartiId":null,"ekstreKayitId":5}],"sonrakiImlec":null,"devamVar":false}""");
        Assert.Equal(5, Assert.Single((await api.BaglanabilirGiderlerAsync()).Ogeler).EkstreKayitId);

        foreach (var kod in new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed })
            Assert.Empty(await Istemci(new YolaGoreHandler(_ => new HttpResponseMessage(kod))).BaglanabilirKartHarcamalariAsync(4));
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => Istemci(new YolaGoreHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest))).BaglanabilirKartHarcamalariAsync(4));
        Assert.Equal(HttpStatusCode.BadRequest, hata.DurumKodu);
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
