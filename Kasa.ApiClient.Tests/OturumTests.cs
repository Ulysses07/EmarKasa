using System.Net;

namespace Kasa.ApiClient.Tests;

/// <summary>Merkezi 401 işleme, çıkış ve oturum kapatma davranışı (Y1/Y2/O5).</summary>
public class OturumTests
{
    private static (KasaApiClient client, SahteHandler handler, BellekTokenStore store, List<OturumBitisNedeni> olaylar) Kur()
    {
        var handler = new SahteHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") };
        var store = new BellekTokenStore();
        var client = new KasaApiClient(http, store);
        var olaylar = new List<OturumBitisNedeni>();
        client.OturumSonaErdi += (_, n) => olaylar.Add(n);
        return (client, handler, store, olaylar);
    }

    [Fact]
    public async Task Tokenli_istek_401_alinca_token_silinir_ve_olay_tetiklenir()
    {
        var (client, handler, store, olaylar) = Kur();
        await store.YazAsync("dolmus");
        handler.Kuyrukla(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<KasaApiException>(() => client.PanelAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.DurumKodu);
        Assert.Null(await store.OkuAsync());
        Assert.Equal(new[] { OturumBitisNedeni.Yetkisiz }, olaylar);
    }

    [Fact]
    public async Task Tokensiz_istek_401_olay_tetiklemez()
    {
        // İlk açılış: depoda token yok, /me 401 döner → "oturum sona erdi" denmemeli.
        var (client, handler, _, olaylar) = Kur();
        handler.Kuyrukla(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<KasaApiException>(() => client.BenKimAsync());

        Assert.Empty(olaylar);
    }

    [Fact]
    public async Task Login_401_olay_tetiklemez_ve_mevcut_tokena_dokunmaz()
    {
        var (client, handler, store, olaylar) = Kur();
        await store.YazAsync("eski");
        handler.Kuyrukla(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<KasaApiException>(() => client.LoginAsync("x", "y"));

        Assert.Empty(olaylar);
        Assert.Equal("eski", await store.OkuAsync());
    }

    [Fact]
    public async Task Diger_hata_kodlari_tokeni_silmez()
    {
        var (client, handler, store, olaylar) = Kur();
        await store.YazAsync("gecerli");
        handler.Kuyrukla(HttpStatusCode.Forbidden)
               .Kuyrukla(HttpStatusCode.InternalServerError)
               .Kuyrukla(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<KasaApiException>(() => client.PanelAsync());
        await Assert.ThrowsAsync<KasaApiException>(() => client.PanelAsync());
        await Assert.ThrowsAsync<KasaApiException>(() => client.PanelAsync());

        Assert.Equal("gecerli", await store.OkuAsync());
        Assert.Empty(olaylar);
    }

    [Fact]
    public async Task Eski_istegin_401i_yeni_girisle_gelen_tokeni_silmez()
    {
        // İstek "eski" token'la gider; yanıt gelene kadar kullanıcı yeniden giriş yapar.
        var store = new BellekTokenStore();
        await store.YazAsync("eski");
        var handler = new GecikmeliHandler(async () =>
        {
            await store.YazAsync("yeni");
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        var client = new KasaApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") }, store);
        var olay = 0;
        client.OturumSonaErdi += (_, _) => olay++;

        await Assert.ThrowsAsync<KasaApiException>(() => client.PanelAsync());

        Assert.Equal("yeni", await store.OkuAsync());
        Assert.Equal(0, olay);
    }

    [Fact]
    public async Task Cikis_cevrimdisiyken_de_tokeni_siler()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("eski-token");
        var handler = new GecikmeliHandler(() => throw new HttpRequestException("ağ yok"));
        var client = new KasaApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") }, store);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CikisAsync());

        Assert.Null(await store.OkuAsync());
    }

    [Fact]
    public async Task Cikis_401de_tokeni_siler_ve_oturum_olayi_tetiklemez()
    {
        var (client, handler, store, olaylar) = Kur();
        await store.YazAsync("eski-token");
        handler.Kuyrukla(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<KasaApiException>(() => client.CikisAsync());

        Assert.Null(await store.OkuAsync());
        Assert.Empty(olaylar);
    }

    [Fact]
    public async Task Cikis_basarida_tokeni_siler()
    {
        var (client, handler, store, olaylar) = Kur();
        await store.YazAsync("t");
        handler.Kuyrukla(HttpStatusCode.OK);

        await client.CikisAsync();

        Assert.Null(await store.OkuAsync());
        Assert.Empty(olaylar);
    }

    [Fact]
    public async Task Oturumlari_kapat_basarida_olay_tetikler()
    {
        var (client, handler, store, olaylar) = Kur();
        await store.YazAsync("t");
        handler.Kuyrukla(HttpStatusCode.NoContent);

        await client.OturumlariKapatAsync();

        Assert.Null(await store.OkuAsync());
        Assert.Equal(new[] { OturumBitisNedeni.OturumlarKapatildi }, olaylar);
    }

    [Fact]
    public void Http_istemcisi_cerez_saklamaz_ve_30sn_zaman_asimi_vardir()
    {
        using var isleyici = KasaApiClient.IsleyiciOlustur();
        Assert.False(isleyici.UseCookies);

        using var http = KasaApiClient.HttpOlustur(new Uri("https://kasa.emarglobal.com/"), new SahteHandler());
        Assert.Equal(TimeSpan.FromSeconds(30), http.Timeout);
        Assert.Equal(new Uri("https://kasa.emarglobal.com/"), http.BaseAddress);
    }

    /// <summary>Yanıtı verilen fonksiyonla üreten handler (yarış / ağ hatası senaryoları için).</summary>
    private sealed class GecikmeliHandler(Func<Task<HttpResponseMessage>> uret) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => uret();
    }
}
