using System.Net;
using System.Text;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Masaüstü giriş akışı gerçek KasaApiClient ile: sunucunun verdiği tanıdık cihaz belirteci depoda kalır, çıkış ve
/// oturum sonundan sonra da sonraki girişte X-Kasa-Cihaz başlığıyla gider; böylece dağıtık bir saldırı hedefi
/// kilitlese de editör kendi cihazından girer. Kilitli hedefte sunucunun Türkçe 429 iletisi gösterilir.
/// </summary>
public class TanidikCihazTests
{
    private sealed class Sunucu : HttpMessageHandler
    {
        public List<string?> GirisBasliklari { get; } = [];
        public int Sayac;
        public bool Kilitli;

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
            if (yol == "/api/auth/logout") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            // Oturum gerektiren her istek bu testte oturum sonudur.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        private static HttpResponseMessage Json(HttpStatusCode kod, string govde) => new(kod) { Content = new StringContent(govde, Encoding.UTF8, "application/json") };
    }

    private static (AuthViewModel Auth, KasaApiClient Api, BellekTokenStore Store, Sunucu Sunucu) Kur()
    {
        var sunucu = new Sunucu();
        var store = new BellekTokenStore();
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
        Assert.Equal("c1.cihaz-1", await store.CihazOkuAsync());

        await auth.CikisAsync();
        Assert.Null(await store.OkuAsync());
        Assert.Equal("c1.cihaz-1", await store.CihazOkuAsync());

        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.True(auth.GirisYapildi);
        Assert.Equal([null, "c1.cihaz-1"], sunucu.GirisBasliklari);
        Assert.Equal("c1.cihaz-2", await store.CihazOkuAsync());
    }

    [Fact]
    public async Task Oturum_sonu_belirteci_silmez_kilitli_hedefe_kendi_cihazindan_girilir()
    {
        var (auth, api, store, sunucu) = Kur();
        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);

        // Oturum düşer (401): yalnız JWT silinir.
        await Assert.ThrowsAsync<KasaApiException>(api.PanelAsync);
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
    public async Task Belirtecsiz_cihaz_kilitli_hedefte_sunucunun_iletisini_gorur()
    {
        var (auth, _, _, sunucu) = Kur();
        sunucu.Kilitli = true;
        auth.Sifre = "editor-sifresi";
        await auth.GirisCommand.ExecuteAsync(null);
        Assert.False(auth.GirisYapildi);
        Assert.Equal("Çok fazla deneme yapıldı. 15 dakika sonra yeniden deneyin.", auth.Hata);
    }
}
