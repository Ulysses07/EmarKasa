using System.Net;
using System.Text;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Masaüstü şifre akışı gerçek KasaApiClient ile: yanlış mevcut şifre (400) oturumu kapatmaz ve Güvenlik
/// ekranında görünür; başarılı değişim girişte bilgi olarak gösterilir; izleyici şifresi sunucuyla aynı kural.
/// </summary>
public class SifreAkisiTests
{
    private sealed class Sunucu(Func<HttpRequestMessage, HttpResponseMessage> yanit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken ct) => Task.FromResult(yanit(istek));
    }

    private static HttpResponseMessage Json(HttpStatusCode kod, string govde) => new(kod) { Content = new StringContent(govde, Encoding.UTF8, "application/json") };

    private static async Task<(KasaApiClient Api, AuthViewModel Auth, BellekTokenStore Store)> GirisYapmis(Func<HttpRequestMessage, HttpResponseMessage> sifreYaniti)
    {
        var store = new BellekTokenStore();
        var api = new KasaApiClient(new HttpClient(new Sunucu(istek => istek.RequestUri!.AbsolutePath switch
        {
            "/api/auth/login" => Json(HttpStatusCode.OK, """{"rol":"editor","token":"jwt"}"""),
            _ => sifreYaniti(istek),
        })) { BaseAddress = new("https://ornek.test/") }, store);
        var auth = new AuthViewModel(api) { Kullanici = "editor", Sifre = "kasa-sifresi" };
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.True(auth.GirisYapildi);
        return (api, auth, store);
    }

    [Fact]
    public async Task Yanlis_mevcut_sifre_400_oturum_sonu_sayilmaz_ve_guvenlik_ekraninda_gorunur()
    {
        var (api, auth, store) = await GirisYapmis(_ => Json(HttpStatusCode.BadRequest, """{"status":400,"errors":{"mevcutSifre":["Mevcut şifre hatalı."]}}"""));
        var surum = auth.OturumSurumu;
        var vm = new GuvenlikViewModel(api, auth) { MevcutSifre = "yanlis", YeniSifre = "yepyeni-sifre-123" };

        await vm.SifreDegistirCommand.ExecuteAsync(null);

        Assert.Equal("Mevcut şifre hatalı.", vm.Hata);
        Assert.True(auth.GirisYapildi);
        Assert.Equal(surum, auth.OturumSurumu);
        Assert.Null(auth.Hata);
        Assert.Equal("jwt", await store.OkuAsync());

        vm.MevcutSifre = "yanlis";
        await vm.KurtarmaKoduOlusturCommand.ExecuteAsync(null);
        Assert.Equal("Mevcut şifre hatalı.", vm.Hata);
        Assert.True(auth.GirisYapildi);
    }

    [Fact]
    public async Task Basarili_sifre_degisimi_giriste_bilgi_olarak_gosterilir_hata_olarak_degil()
    {
        var (api, auth, _) = await GirisYapmis(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var vm = new GuvenlikViewModel(api, auth) { MevcutSifre = "kasa-sifresi", YeniSifre = "yepyeni-sifre-123" };

        await vm.SifreDegistirCommand.ExecuteAsync(null);

        Assert.False(auth.GirisYapildi);
        Assert.Null(auth.Hata);
        Assert.Equal("Şifreniz değişti. Yeni şifrenizle giriş yapın.", auth.Bilgi);
    }

    [Fact]
    public async Task Gercek_oturum_sonu_hata_olarak_gosterilir_ve_yeni_giris_bilgiyi_temizler()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt") };
        var auth = new AuthViewModel(api);
        api.OturumuSonlandir(OturumSonuNedeni.SifreDegisti);
        Assert.NotNull(auth.Bilgi);
        api.OturumuSonlandir();
        Assert.Equal("Oturumunuz sona erdi. Yeniden giriş yapın.", auth.Hata);
        Assert.Null(auth.Bilgi);
        api.OturumuSonlandir(OturumSonuNedeni.SifreDegisti);
        auth.Sifre = "sifre";
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.Null(auth.Bilgi);
    }

    [Fact]
    public async Task Hiz_siniri_giriste_sunucu_iletisiyle_gosterilir()
    {
        var api = new SahteApi { LoginHatasi = new KasaApiException(HttpStatusCode.TooManyRequests, "Çok fazla deneme yapıldı. 5 dakika sonra yeniden deneyin.") };
        var auth = new AuthViewModel(api) { Sifre = "x" };
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.Equal("Çok fazla deneme yapıldı. 5 dakika sonra yeniden deneyin.", auth.Hata);

        api.LoginHatasi = new KasaApiException(HttpStatusCode.Unauthorized);
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.Equal("Kullanıcı adı veya şifre hatalı.", auth.Hata);
    }

    [Theory]
    [InlineData("gizli123")]
    [InlineData("on-bir-harf")]
    [InlineData("            ")]
    public async Task Izleyici_sifresi_12_karakterden_kisaysa_gonderilmez(string sifre)
    {
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api) { YeniIzleyiciSifre = sifre };

        await vm.IzleyiciSifreKaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonIzleyiciSifre);
        Assert.Equal("İzleyici şifresi 12–1024 karakter olmalıdır.", vm.IzleyiciSifreHatasi);
        Assert.Null(vm.IzleyiciSifreMesaji);
        Assert.Equal(sifre, vm.YeniIzleyiciSifre);
    }

    [Fact]
    public async Task Izleyici_sifresi_12_karakterle_gonderilir_ve_onay_gosterilir()
    {
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api) { YeniIzleyiciSifre = "on-iki-harf!" };

        await vm.IzleyiciSifreKaydetCommand.ExecuteAsync(null);

        Assert.Equal("on-iki-harf!", api.SonIzleyiciSifre);
        Assert.Equal("", vm.YeniIzleyiciSifre);
        Assert.Null(vm.IzleyiciSifreHatasi);
        Assert.Equal("İzleyici şifresi güncellendi. Eski izleyici oturumları kapandı.", vm.IzleyiciSifreMesaji);
        Assert.False(vm.Mesgul);
    }
}
