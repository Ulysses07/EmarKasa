using System.Net;
using System.Text;

namespace Kasa.ApiClient.Tests;

public class OturumVeHataTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> yanit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => yanit(request);
    }

    private static KasaApiClient Kur(HttpMessageHandler handler, BellekTokenStore store)
        => new(new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") }, store);

    [Fact]
    public async Task Cikis_ag_hatasinda_da_tokeni_temizler()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("token");
        var client = Kur(new Handler(_ => throw new HttpRequestException("çevrimdışı")), store);
        await Assert.ThrowsAsync<HttpRequestException>(client.CikisAsync);
        Assert.Null(await store.OkuAsync());
    }

    [Fact]
    public async Task Cikis_sunucu_hatasinda_da_tokeni_temizler()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("token");
        var client = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.ServiceUnavailable), store);
        await Assert.ThrowsAsync<KasaApiException>(client.CikisAsync);
        Assert.Null(await store.OkuAsync());
    }

    [Fact]
    public async Task Yetkisiz_yanit_tokeni_temizler_ve_oturum_sonunu_bildirir()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("token");
        var client = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.Unauthorized), store);
        var bildirildi = 0;
        client.OturumSonlandi += (_, _) => bildirildi++;
        await Assert.ThrowsAsync<KasaApiException>(client.PanelAsync);
        Assert.Null(await store.OkuAsync());
        Assert.Equal(1, bildirildi);
    }

    [Fact]
    public async Task Basarisiz_login_mevcut_tokeni_temizlemez()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("token");
        var client = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.Unauthorized), store);
        var bildirildi = false;
        client.OturumSonlandi += (_, _) => bildirildi = true;
        await Assert.ThrowsAsync<KasaApiException>(() => client.LoginAsync("x", "yanlış"));
        Assert.Equal("token", await store.OkuAsync());
        Assert.False(bildirildi);
    }

    [Fact]
    public async Task Eski_istegin_401_yaniti_yeni_oturumu_silmez()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("eski");
        var bekleyen = new TaskCompletionSource<HttpResponseMessage>();
        var handler = new Handler(istek => istek.RequestUri!.AbsolutePath.EndsWith("login")
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"rol":"editor","token":"yeni"}""", Encoding.UTF8, "application/json"),
            })
            : bekleyen.Task);
        var client = Kur(handler, store);
        var eskiIstek = client.PanelAsync();
        await client.LoginAsync("x", "y");
        bekleyen.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<KasaApiException>(() => eskiIstek);
        Assert.Equal("yeni", await store.OkuAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"errors\":{\"Tutar\":[\"Tutar sıfırdan büyük olmalı.\"]}}", "Tutar sıfırdan büyük olmalı.")]
    [InlineData(HttpStatusCode.Conflict, "{\"hata\":\"Kanal kullanımda.\"}", "Kanal kullanımda.")]
    [InlineData(HttpStatusCode.BadRequest, "{\"detail\":\"Geçersiz tarih.\"}", "Geçersiz tarih.")]
    [InlineData(HttpStatusCode.BadRequest, "<html>proxy error</html>", "Girilen bilgileri kontrol edin.")]
    public async Task Dogrulama_ve_cakisma_mesajlarini_korur(HttpStatusCode kod, string govde, string beklenen)
    {
        var client = Kur(new SahteHandler().Kuyrukla(kod, govde), new BellekTokenStore());
        var hata = await Assert.ThrowsAsync<KasaApiException>(client.PanelAsync);
        Assert.Equal(kod, hata.DurumKodu);
        Assert.Equal(beklenen, hata.Message);
    }

    [Theory]
    [InlineData(null, "https://kasa.emarglobal.com/")]
    [InlineData("", "https://kasa.emarglobal.com/")]
    [InlineData("http://localhost:5187", "http://localhost:5187/")]
    [InlineData("https://test.example/kasa", "https://test.example/kasa/")]
    public void Api_adresi_varsayilan_ve_acik_yapilandirmayi_destekler(string? adres, string beklenen)
        => Assert.Equal(beklenen, ApiAdresi.Coz(adres).AbsoluteUri);

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("/api")]
    public void Gecersiz_api_adresi_reddedilir(string adres)
        => Assert.Throws<ArgumentException>(() => ApiAdresi.Coz(adres));
}
