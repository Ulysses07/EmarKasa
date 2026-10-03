using System.Net;
using System.Text;

namespace Kasa.ApiClient.Tests;

/// <summary>İstemcinin tek gönderim noktası sunucuya ulaşılabilirliği bildirir (<see cref="IBaglantiBildirimleri"/>): yanıt alınan
/// her istek (4xx ve 5xx dahil) ulaşıldı, ağ hatası ve süre sınırı ulaşılamadı sayılır; çağıranın iptali bildirilmez. 502, 504 ve
/// iletisiz 503 (proxy/ağ geçidi hataları) ulaşılamadı sayılır ve sırasıyla HttpRequestException/TimeoutException fırlatılır;
/// sunucunun kendi iletili 503'ü (ör. "Veritabanı meşgul.") ulaşıldı sayılır ve KasaApiException olarak kalır (ürün sahibi kararı
/// 2026-10-03: anlamlı sunucu yanıtı bağlantı kopması değildir).</summary>
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

    [Fact]
    public async Task Proxy_502_ulasilamadi_bildirir_baglanti_hatasi_firlatir()
    {
        var (c, olaylar) = Kur((_, _) => Task.FromResult(Yanit(HttpStatusCode.BadGateway, "{}")));

        var hata = await Assert.ThrowsAsync<HttpRequestException>(c.KanallarAsync);

        Assert.Equal(HttpStatusCode.BadGateway, hata.StatusCode);
        Assert.Equal(["ulaşılamadı:HttpRequestException"], olaylar);
    }

    [Fact]
    public async Task Govdesiz_503_ulasilamadi_bildirir_baglanti_hatasi_firlatir()
    {
        // "{}" gövdesi API'nin anlamlı iletisi değildir (errors/detail/hata/message/title yok): proxy/ağ geçidi hatası sayılır.
        var (c, olaylar) = Kur((_, _) => Task.FromResult(Yanit(HttpStatusCode.ServiceUnavailable, "{}")));

        var hata = await Assert.ThrowsAsync<HttpRequestException>(c.KanallarAsync);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, hata.StatusCode);
        Assert.Equal(["ulaşılamadı:HttpRequestException"], olaylar);
    }

    [Fact]
    public async Task Iletili_503_ulasildi_bildirir_sunucunun_iletisiyle_kasaapiexception_firlatir()
    {
        var (c, olaylar) = Kur((_, _) => Task.FromResult(Yanit(HttpStatusCode.ServiceUnavailable, "{\"hata\":\"Veritabanı meşgul.\"}")));

        var hata = await Assert.ThrowsAsync<KasaApiException>(c.KanallarAsync);

        Assert.Equal("Veritabanı meşgul.", hata.Message);
        Assert.Equal(["ulaşıldı"], olaylar);
    }

    [Fact]
    public async Task Proxy_504_ulasilamadi_bildirir_zaman_asimi_firlatir()
    {
        var (c, olaylar) = Kur((_, _) => Task.FromResult(Yanit(HttpStatusCode.GatewayTimeout, "{}")));

        var hata = await Assert.ThrowsAsync<TimeoutException>(c.KanallarAsync);

        Assert.Equal(KasaZamanAsimlari.Ileti, hata.Message);
        Assert.Equal(["ulaşılamadı:TimeoutException"], olaylar);
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
