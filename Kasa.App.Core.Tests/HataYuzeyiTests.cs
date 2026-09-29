using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class HataYuzeyiTests
{
    [Fact]
    public async Task Panel_ag_hatasinda_hata_yazar_ve_spinner_iner()
    {
        var api = new SahteApi { YuklemeHatasi = new KasaApiException(HttpStatusCode.ServiceUnavailable, "kopuk") };
        var vm = new PanelViewModel(api);

        await vm.YukleAsync();

        Assert.False(vm.Mesgul);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Zaman_asimi_baglanti_hatasindan_ayri_anlatilir_kayitta_kontrol_ister_okumada_istemez()
    {
        var vm = new PanelViewModel(new SahteApi { YuklemeHatasi = new TimeoutException(KasaZamanAsimlari.Ileti) });

        await vm.YukleAsync();

        Assert.Contains("zamanında yanıt vermedi", vm.Hata);
        Assert.DoesNotContain("ulaşılamadı", vm.Hata);
        // Kayıt isteği sunucuya ulaşmış olabilir: önce kontrol istenir. Salt okuma (panel) sunucuda bir şey değiştirmez.
        Assert.Contains("tamamlanmış olabilir", HataMetni(new TimeoutException(KasaZamanAsimlari.Ileti)));
        Assert.DoesNotContain("tamamlanmış olabilir", vm.Hata);
        Assert.Equal("Sunucuya ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.", HataMetni(new TaskCanceledException()));
    }

    // IST4: sunucu hatasının (5xx) ProblemDetails iz kimliği iletide kısa "Hata kodu" olarak gösterilir; yönetici sunucu
    // logundaki tam iz kimliğini bu parçayla bulur. İz yoksa ya da istemci hatasıysa ileti değişmez.
    private const string Iz = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    [Fact]
    public async Task Sunucu_hatasinin_iz_kimligi_iletide_kisa_hata_kodu_olarak_gorunur()
    {
        Assert.Equal("Sunucu işlemi tamamlayamadı. Lütfen yeniden deneyin. Hata kodu: 4bf92f35",
            HataMetni(new KasaApiException(HttpStatusCode.InternalServerError, null, Iz)));
        Assert.Equal("Sunucu meşgul. Hata kodu: 4bf92f35", HataMetni(new KasaApiException(HttpStatusCode.ServiceUnavailable, "Sunucu meşgul.", Iz)));
        Assert.Equal("Sunucu işlemi tamamlayamadı. Lütfen yeniden deneyin.", HataMetni(new KasaApiException(HttpStatusCode.InternalServerError)));
        Assert.Equal("Kayıt değişti.", HataMetni(new KasaApiException(HttpStatusCode.Conflict, "Kayıt değişti.", Iz)));

        var vm = new PanelViewModel(new SahteApi { YuklemeHatasi = new KasaApiException(HttpStatusCode.InternalServerError, null, Iz) });
        await vm.YukleAsync();
        Assert.EndsWith("Hata kodu: 4bf92f35", vm.Hata);
    }

    [Fact]
    public async Task Giris_ve_kurtarma_sunucu_hatasinda_hata_kodunu_gosterir()
    {
        var hata = new KasaApiException(HttpStatusCode.InternalServerError, null, Iz);
        var auth = new AuthViewModel(new SahteApi { LoginHatasi = hata }) { Sifre = "x" };
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.Equal("Sunucu girişi tamamlayamadı. Lütfen yeniden deneyin. Hata kodu: 4bf92f35", auth.Hata);

        // Kurtarma gerçek istemciyle: sunucunun 500 ProblemDetails gövdesindeki iz kimliği iletiye kadar taşınır.
        var api = new KasaApiClient(new HttpClient(new SunucuHatasi()) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
        var kurtarma = new AuthViewModel(api) { Kullanici = "editor", KurtarmaKodu = "kod", KurtarmaYeniSifre = "yeni-sifre-123", KurtarmaYeniSifreTekrar = "yeni-sifre-123" };
        await kurtarma.SifreKurtarCommand.ExecuteAsync(null);
        Assert.Equal("Sunucu işlemi tamamlayamadı. Lütfen yeniden deneyin. Hata kodu: 4bf92f35", kurtarma.Hata);
    }

    private sealed class SunucuHatasi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($$"""{"title":"An error occurred while processing your request.","status":500,"traceId":"{{Iz}}"}""", System.Text.Encoding.UTF8, "application/problem+json"),
        });
    }

    private sealed class HataOkuyucu : TemelViewModel { public static string Oku(Exception e) => HataMesaji(e); }
    private static string HataMetni(Exception e) => HataOkuyucu.Oku(e);

}
