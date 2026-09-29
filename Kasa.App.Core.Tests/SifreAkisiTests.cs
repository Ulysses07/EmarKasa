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
        }))
        { BaseAddress = new("https://ornek.test/") }, store);
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
        var vm = new GuvenlikViewModel(api, auth) { MevcutSifre = "yanlis", YeniSifre = "yepyeni-sifre-123", YeniSifreTekrar = "yepyeni-sifre-123" };

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
        var vm = new GuvenlikViewModel(api, auth) { MevcutSifre = "kasa-sifresi", YeniSifre = "yepyeni-sifre-123", YeniSifreTekrar = "yepyeni-sifre-123" };

        await vm.SifreDegistirCommand.ExecuteAsync(null);

        Assert.False(auth.GirisYapildi);
        Assert.Null(auth.Hata);
        Assert.Equal("Şifreniz değişti. Yeni şifrenizle giriş yapın.", auth.Bilgi);
    }

    // maui-4: şifre değişimi token'ı ve kurtarma kodunu hemen geçersiz kılar; yazım hatalı yeni şifre tek editör hesabını
    // kilitler. Tekrar alanı uyuşmazsa istek hiç gönderilmez.
    [Fact]
    public async Task Yeni_sifre_tekrari_uyusmazsa_sifre_degisimi_gonderilmez_eslesince_gonderilir()
    {
        var istekler = new List<string>();
        var (api, auth, _) = await GirisYapmis(istek => { istekler.Add(istek.RequestUri!.AbsolutePath); return new HttpResponseMessage(HttpStatusCode.NoContent); });
        var vm = new GuvenlikViewModel(api, auth) { MevcutSifre = "kasa-sifresi", YeniSifre = "Kasa2026!Guvenli", YeniSifreTekrar = "Kasa2026!Guvenlı" };

        await vm.SifreDegistirCommand.ExecuteAsync(null);

        Assert.Empty(istekler);
        Assert.Equal("Yeni şifreler aynı olmalı.", vm.Hata);
        Assert.True(auth.GirisYapildi);
        Assert.Equal("Kasa2026!Guvenli", vm.YeniSifre);

        vm.YeniSifreTekrar = "Kasa2026!Guvenli";
        await vm.SifreDegistirCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "/api/auth/sifre" }, istekler);
        Assert.False(auth.GirisYapildi);
        Assert.Equal("", vm.YeniSifre);
        Assert.Equal("", vm.YeniSifreTekrar);
        Assert.Equal("", vm.MevcutSifre);
    }

    [Fact]
    public async Task Kurtarmada_yeni_sifre_tekrari_uyusmazsa_kod_harcanmaz_eslesince_alanlar_temizlenir()
    {
        var istekler = new List<string>();
        var api = new KasaApiClient(new HttpClient(new Sunucu(istek => { istekler.Add(istek.RequestUri!.AbsolutePath); return new HttpResponseMessage(HttpStatusCode.NoContent); }))
        { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
        var auth = new AuthViewModel(api) { Kullanici = "editor", KurtarmaAcik = true, KurtarmaKodu = "ABCD-EFGH", KurtarmaYeniSifre = "Kasa2026!Guvenli", KurtarmaYeniSifreTekrar = "Kasa2026!Guvenlı" };

        await auth.SifreKurtarCommand.ExecuteAsync(null);

        Assert.Empty(istekler);
        Assert.Equal("Yeni şifreler aynı olmalı.", auth.Hata);
        Assert.Equal("ABCD-EFGH", auth.KurtarmaKodu);
        Assert.True(auth.KurtarmaAcik);
        Assert.False(auth.Mesgul);

        auth.KurtarmaYeniSifreTekrar = "Kasa2026!Guvenli";
        await auth.SifreKurtarCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "/api/auth/kurtar" }, istekler);
        Assert.Null(auth.Hata);
        Assert.Equal("", auth.KurtarmaKodu);
        Assert.Equal("", auth.KurtarmaYeniSifre);
        Assert.Equal("", auth.KurtarmaYeniSifreTekrar);
        Assert.False(auth.KurtarmaAcik);
    }

    [Fact]
    public async Task Tekrar_alani_kurtarma_formu_kapaninca_giriste_ve_guvenlik_temizliginde_bosalir()
    {
        var auth = new AuthViewModel(new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt") }) { KurtarmaYeniSifre = "gizli-yeni-sifre", KurtarmaYeniSifreTekrar = "gizli-yeni-sifre" };
        auth.KurtarmayiAcKapatCommand.Execute(null);
        Assert.Equal("", auth.KurtarmaYeniSifre);
        Assert.Equal("", auth.KurtarmaYeniSifreTekrar);
        auth.KurtarmaYeniSifreTekrar = "gizli-yeni-sifre";
        auth.Sifre = "sifre";
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.Equal("", auth.KurtarmaYeniSifreTekrar);
        var guvenlik = new GuvenlikViewModel(new KasaApiClient(new HttpClient(), new BellekTokenStore()), auth) { YeniSifre = "gizli-yeni-sifre", YeniSifreTekrar = "gizli-yeni-sifre" };
        guvenlik.Temizle();
        Assert.Equal("", guvenlik.YeniSifre);
        Assert.Equal("", guvenlik.YeniSifreTekrar);
    }

    // IST4 (F3C notu): "Yeni şifreyi göster" kutusu görünüm modelinde tutulur; açık bırakılan kutu bir sonraki şifre girişinde
    // yazılanı açıkta göstermesin diye başarıda, formun/ekranın kapanmasında ve oturum sonunda kapanır.
    [Fact]
    public async Task Guvenlik_ekraninda_yeni_sifreyi_goster_basarida_ekrandan_ayrilinca_ve_oturum_sonunda_kapanir()
    {
        var (api, auth, _) = await GirisYapmis(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var vm = new GuvenlikViewModel(api, auth) { YeniSifreyiGoster = true };
        vm.EkrandanAyril();
        Assert.False(vm.YeniSifreyiGoster);

        vm.YeniSifreyiGoster = true;
        vm.MevcutSifre = "kasa-sifresi";
        vm.YeniSifre = vm.YeniSifreTekrar = "yepyeni-sifre-123";
        await vm.SifreDegistirCommand.ExecuteAsync(null);
        Assert.False(vm.YeniSifreyiGoster);

        var (api2, auth2, _) = await GirisYapmis(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var ikinci = new GuvenlikViewModel(api2, auth2) { YeniSifreyiGoster = true };
        await auth2.CikisAsync();
        Assert.False(ikinci.YeniSifreyiGoster);
    }

    [Fact]
    public async Task Kurtarmada_yeni_sifreyi_goster_form_kapaninca_basarida_ve_oturum_sonunda_kapanir()
    {
        var sahte = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt") };
        var auth = new AuthViewModel(sahte) { KurtarmaAcik = true, KurtarmaSifresiniGoster = true };
        auth.KurtarmayiAcKapatCommand.Execute(null);                // form kapandı
        Assert.False(auth.KurtarmaSifresiniGoster);

        auth.KurtarmayiAcKapatCommand.Execute(null);
        auth.KurtarmaSifresiniGoster = true;
        sahte.OturumuSonlandir();
        Assert.False(auth.KurtarmaSifresiniGoster);

        var api = new KasaApiClient(new HttpClient(new Sunucu(_ => new HttpResponseMessage(HttpStatusCode.NoContent))) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
        var kurtarma = new AuthViewModel(api) { Kullanici = "editor", KurtarmaAcik = true, KurtarmaKodu = "ABCD-EFGH", KurtarmaYeniSifre = "Kasa2026!Guvenli", KurtarmaYeniSifreTekrar = "Kasa2026!Guvenli", KurtarmaSifresiniGoster = true };
        await kurtarma.SifreKurtarCommand.ExecuteAsync(null);
        Assert.Null(kurtarma.Hata);
        Assert.False(kurtarma.KurtarmaSifresiniGoster);
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

    [Fact]
    public async Task Kurala_uymayan_mevcut_izleyici_sifresi_uyarisi_gosterilir_ve_yenilenince_kalkar()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, true, IzleyiciSifreKisa: true) };
        var vm = new AyarlarViewModel(api);

        await vm.YukleAsync();
        Assert.Equal("Mevcut izleyici şifresi 12 karakterden kısa (son izleyici girişinde görüldü). Kurala uygun yeni bir şifre belirleyin.", vm.IzleyiciSifreUyarisi);

        vm.YeniIzleyiciSifre = "kisa";                        // geçersiz deneme uyarıyı kaldırmaz
        await vm.IzleyiciSifreKaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.IzleyiciSifreUyarisi);

        vm.YeniIzleyiciSifre = "on-iki-harf!";
        await vm.IzleyiciSifreKaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.IzleyiciSifreUyarisi);
    }

    [Fact]
    public async Task Uyari_yoksa_izleyici_ve_vekil_uyarisi_bos_kalir_varsa_vekil_uyarisi_aynen_gosterilir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, true) };
        var vm = new AyarlarViewModel(api);
        await vm.YukleAsync();
        Assert.Null(vm.IzleyiciSifreUyarisi);
        Assert.Null(vm.VekilUyarisi);

        api.AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, true, VekilUyarisi: "Vekil ayarı hatalı.");
        await vm.YukleAsync();
        Assert.Equal("Vekil ayarı hatalı.", vm.VekilUyarisi);
    }
}
