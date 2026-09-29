using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Masaüstü giriş akışı gerçek KasaApiClient ile: sunucunun verdiği tanıdık cihaz belirteci depoda kalır, çıkış ve
/// oturum sonundan sonra da sonraki girişte X-Kasa-Cihaz başlığıyla gider; açılıştaki oturum doğrulaması (/me)
/// belirteci yeniler. Böylece dağıtık bir saldırı hedefi kilitlese de editör, oturumu dolduğunda bile kendi
/// cihazından girer. Kilitli hedefte sunucunun Türkçe 429 iletisi gösterilir; güvenli depo hatası girişi engellemez.
/// </summary>
public class TanidikCihazTests
{
    private sealed class Sunucu : HttpMessageHandler
    {
        public List<string?> GirisBasliklari { get; } = [];
        public int Sayac;
        public bool Kilitli;
        /// <summary>Açılış doğrulaması (/me) geçerli oturum görür; aksi halde oturum gerektiren her istek 401'dir.</summary>
        public bool OturumGecerli;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken ct)
        {
            var yol = istek.RequestUri!.AbsolutePath;
            if (yol == "/api/auth/login")
            {
                var baslik = istek.Headers.TryGetValues("X-Kasa-Cihaz", out var d) ? d.Single() : null;
                GirisBasliklari.Add(baslik);
                // Hedef kilitliyken yalnız tanıdık cihaz (başlıkla) girer; diğerleri şifre denenmeden 429 alır.
                if (Kilitli && baslik is null)
                    return Task.FromResult(Json(HttpStatusCode.TooManyRequests, """{"hata":"Çok fazla deneme yapıldı. 15 dakika sonra yeniden deneyin."}"""));
                Sayac++;
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""{"rol":"editor","token":"jwt-{{Sayac}}","cihaz":"c1.cihaz-{{Sayac}}"}"""));
            }
            if (yol == "/api/auth/me" && OturumGecerli && istek.Headers.Authorization is not null)
                return Task.FromResult(Json(HttpStatusCode.OK, """{"rol":"editor","cihaz":"c1.acilis"}"""));
            if (yol == "/api/auth/logout") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        private static HttpResponseMessage Json(HttpStatusCode kod, string govde) => new(kod) { Content = new StringContent(govde, Encoding.UTF8, "application/json") };
    }

    /// <summary>Tanıdık cihaz kayıtları çözülemeyen güvenli depo (ör. Windows profili ya da DPAPI anahtarı değişti).</summary>
    private sealed class KirikCihazDeposu : ITokenStore
    {
        private readonly BellekTokenStore _ic = new();
        public Task<string?> OkuAsync() => _ic.OkuAsync();
        public Task YazAsync(string token) => _ic.YazAsync(token);
        public Task TemizleAsync() => _ic.TemizleAsync();
        public Task<string?> CihazOkuAsync(string rol) => throw new CryptographicException("Kayıt çözülemedi.");
        public Task CihazYazAsync(string rol, string belirtec) => throw new CryptographicException("Kayıt yazılamadı.");
    }

    private static (AuthViewModel Auth, KasaApiClient Api, ITokenStore Store, Sunucu Sunucu) Kur(ITokenStore? depo = null)
    {
        var sunucu = new Sunucu();
        var store = depo ?? new BellekTokenStore();
        var api = new KasaApiClient(new HttpClient(sunucu) { BaseAddress = new("https://ornek.test/") }, store);
        return (new AuthViewModel(api) { Kullanici = "editor" }, api, store, sunucu);
    }

    [Fact]
    public async Task Belirtec_saklanir_cikistan_sonra_sonraki_giriste_gonderilir()
    {
        var (auth, _, store, sunucu) = Kur();
        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.True(auth.GirisYapildi);
        Assert.Equal("c1.cihaz-1", await store.CihazOkuAsync("editor"));

        await auth.CikisAsync();
        Assert.Null(await store.OkuAsync());
        Assert.Equal("c1.cihaz-1", await store.CihazOkuAsync("editor"));

        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.True(auth.GirisYapildi);
        Assert.Equal([null, "c1.cihaz-1"], sunucu.GirisBasliklari);
        Assert.Equal("c1.cihaz-2", await store.CihazOkuAsync("editor"));
    }

    [Fact]
    public async Task Oturum_sonu_belirteci_silmez_kilitli_hedefe_kendi_cihazindan_girilir()
    {
        var (auth, api, store, sunucu) = Kur();
        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);

        // Oturum düşer (401): yalnız JWT silinir.
        await Assert.ThrowsAsync<KasaApiException>(api.KanallarAsync);
        Assert.False(auth.GirisYapildi);
        Assert.Null(await store.OkuAsync());

        sunucu.Kilitli = true;
        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.True(auth.GirisYapildi);
        Assert.Null(auth.Hata);
        Assert.Equal("c1.cihaz-1", sunucu.GirisBasliklari[^1]);
    }

    [Fact]
    public async Task Acilis_dogrulamasi_belirteci_yeniler_oturum_dolunca_kilitli_hedefe_kendi_cihazindan_girilir()
    {
        // Belirteç saklanmadan açılmış oturum (ör. güncellemeden önce): açılıştaki /me doğrulaması belirteci verir.
        var (auth, api, store, sunucu) = Kur();
        await store.YazAsync("jwt-eski-oturum");
        sunucu.OturumGecerli = true;
        Assert.True(await auth.AcilistaDogrulaAsync());
        Assert.Equal("c1.acilis", await store.CihazOkuAsync("editor"));

        // Oturum dolar; saldırı hedefi kilitlemiştir.
        sunucu.OturumGecerli = false;
        await Assert.ThrowsAsync<KasaApiException>(api.KanallarAsync);
        sunucu.Kilitli = true;
        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);

        Assert.True(auth.GirisYapildi);
        Assert.Null(auth.Hata);
        Assert.Equal(["c1.acilis"], sunucu.GirisBasliklari);
    }

    [Fact]
    public async Task Belirtecsiz_cihaz_kilitli_hedefte_sunucunun_iletisini_gorur()
    {
        var (auth, _, _, sunucu) = Kur();
        sunucu.Kilitli = true;
        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.False(auth.GirisYapildi);
        Assert.Equal("Çok fazla deneme yapıldı. 15 dakika sonra yeniden deneyin.", auth.Hata);
    }

    [Fact]
    public async Task Guvenli_depo_hatasi_giris_ve_acilis_dogrulamasini_engellemez()
    {
        var (auth, _, store, sunucu) = Kur(new KirikCihazDeposu());
        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);

        Assert.True(auth.GirisYapildi);
        Assert.Null(auth.Hata);
        Assert.Equal([null], sunucu.GirisBasliklari);
        Assert.Equal("jwt-1", await store.OkuAsync());

        sunucu.OturumGecerli = true;
        Assert.True(await auth.AcilistaDogrulaAsync());
    }
}
