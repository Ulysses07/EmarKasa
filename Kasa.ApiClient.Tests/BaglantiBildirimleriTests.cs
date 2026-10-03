using System.Net;
using System.Text;

namespace Kasa.ApiClient.Tests;

/// <summary>İstemcinin tek gönderim noktası sunucuya ulaşılabilirliği bildirir (<see cref="IBaglantiBildirimleri"/>): yanıt alınan
/// her istek (4xx ve 5xx dahil) ulaşıldı, ağ hatası ve süre sınırı ulaşılamadı sayılır; çağıranın iptali bildirilmez. 502, 503 ve
/// 504 (proxy/ağ geçidi hataları) de ulaşılamadı sayılır (ürün sahibi kararı 2026-10-03).</summary>
public class BaglantiBildirimleriTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => fn(r, c);
    }

    private static readonly KasaZamanAsimlari Kisa = new(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

    private static (KasaApiClient Istemci, List<string> Olaylar) Kur(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn)
    {
        var istemci = new KasaApiClient(new HttpClient(new Handler(fn)) { BaseAddress = new Uri("https://ornek.test/") }, new BellekTokenStore(), Kisa);
        var olaylar = new List<string>();
        IBaglantiBildirimleri bildirimler = istemci;
        bildirimler.SunucuyaUlasildi += (_, _) => olaylar.Add("ulaşıldı");
        bildirimler.SunucuyaUlasilamadi += (_, e) => olaylar.Add("ulaşılamadı:" + e.GetType().Name);
        return (istemci, olaylar);
    }

    private static HttpResponseMessage Yanit(HttpStatusCode kod, string json) => new(kod) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Ag_hatasi_ulasilamadi_bildirir_ve_istisna_aynen_cikar()
    {
        var (c, olaylar) = Kur((_, _) => throw new HttpRequestException("Bağlantı reddedildi."));

        await Assert.ThrowsAsync<HttpRequestException>(c.KanallarAsync);

        Assert.Equal(["ulaşılamadı:HttpRequestException"], olaylar);
    }

    [Fact]
    public async Task Sure_siniri_ulasilamadi_bildirir()
    {
        var (c, olaylar) = Kur(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); });

        await Assert.ThrowsAsync<TimeoutException>(c.KanallarAsync);

        Assert.Equal(["ulaşılamadı:TimeoutException"], olaylar);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "[]")]
    [InlineData(HttpStatusCode.BadRequest, "{\"hata\":\"x\"}")]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    public async Task Yanit_alinan_istek_durum_kodundan_bagimsiz_ulasildi_bildirir(HttpStatusCode kod, string json)
    {
        var (c, olaylar) = Kur((_, _) => Task.FromResult(Yanit(kod, json)));

        try
        { await c.KanallarAsync(); }
        catch (KasaApiException) { }

        Assert.Equal(["ulaşıldı"], olaylar);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Proxy_502_503_504_ulasilamadi_bildirir(HttpStatusCode kod)
    {
        var (c, olaylar) = Kur((_, _) => Task.FromResult(Yanit(kod, "{}")));

        try
        { await c.KanallarAsync(); }
        catch (KasaApiException) { }

        Assert.Equal(["ulaşılamadı:HttpRequestException"], olaylar);
    }

    [Fact]
    public async Task Cagiranin_iptali_bildirilmez()
    {
        var (c, olaylar) = Kur(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); });
        using var iptal = new CancellationTokenSource();

        var istek = c.HaftalikAsync(iptal.Token);
        iptal.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => istek);
        Assert.Empty(olaylar);
    }
}
