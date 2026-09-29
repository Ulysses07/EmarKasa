using System.Net;

namespace Kasa.ApiClient.Tests;

/// <summary>Yanlış mevcut şifre (400) alan hatasıdır, oturum sonu değildir; hız sınırı iletisi korunur.</summary>
public class SifreAkisiTests
{
    private const string MevcutSifreHatali = """{"title":"One or more validation errors occurred.","status":400,"errors":{"mevcutSifre":["Mevcut şifre hatalı."]}}""";

    private static KasaApiClient Kur(HttpMessageHandler h, ITokenStore store) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, store);

    [Fact]
    public async Task Sifre_degisiminde_yanlis_mevcut_sifre_alan_hatasidir_oturumu_kapatmaz()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("gecerli");
        var c = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.BadRequest, MevcutSifreHatali), store);
        var bildirildi = false;
        c.OturumSonlandi += (_, _) => bildirildi = true;

        var hata = await Assert.ThrowsAsync<KasaApiException>(() => c.SifreDegistirAsync(new("yanlis", "yepyeni-sifre-123")));

        Assert.Equal(HttpStatusCode.BadRequest, hata.DurumKodu);
        Assert.Equal("Mevcut şifre hatalı.", hata.Message);
        Assert.Equal("gecerli", await store.OkuAsync());
        Assert.False(bildirildi);
    }

    [Fact]
    public async Task Kurtarma_kodunda_yanlis_mevcut_sifre_oturumu_kapatmaz()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("gecerli");
        var c = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.BadRequest, MevcutSifreHatali), store);
        var bildirildi = false;
        c.OturumSonlandi += (_, _) => bildirildi = true;

        var hata = await Assert.ThrowsAsync<KasaApiException>(() => c.KurtarmaKoduOlusturAsync("yanlis"));

        Assert.Equal("Mevcut şifre hatalı.", hata.Message);
        Assert.Equal("gecerli", await store.OkuAsync());
        Assert.False(bildirildi);
    }

    [Fact]
    public async Task Basarili_sifre_degisimi_oturum_sonunu_sifre_degisti_nedeniyle_bildirir()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("eski");
        var c = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.NoContent), store);
        var nedenler = new List<OturumSonuNedeni?>();
        c.OturumSonlandi += (_, e) => nedenler.Add((e as OturumSonlandiEventArgs)?.Neden);

        await c.SifreDegistirAsync(new("eski-sifre", "yepyeni-sifre-123"));

        Assert.Equal([OturumSonuNedeni.SifreDegisti], nedenler);
        Assert.Null(await store.OkuAsync());
    }

    [Fact]
    public async Task Gercek_401_oturum_gecersiz_nedeniyle_bildirilir()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("eski");
        var c = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.Unauthorized), store);
        var nedenler = new List<OturumSonuNedeni?>();
        c.OturumSonlandi += (_, e) => nedenler.Add((e as OturumSonlandiEventArgs)?.Neden);

        await Assert.ThrowsAsync<KasaApiException>(() => c.SifreDegistirAsync(new("eski-sifre", "yepyeni-sifre-123")));

        Assert.Equal([OturumSonuNedeni.OturumGecersiz], nedenler);
        Assert.Null(await store.OkuAsync());
    }

    [Theory]
    [InlineData("""{"hata":"Çok fazla deneme yapıldı. 5 dakika sonra yeniden deneyin."}""", "Çok fazla deneme yapıldı. 5 dakika sonra yeniden deneyin.")]
    [InlineData(null, "Çok fazla deneme yapıldı. Birkaç dakika sonra yeniden deneyin.")]
    public async Task Hiz_siniri_yaniti_turkce_iletiyle_gelir_ve_oturumu_kapatmaz(string? govde, string beklenen)
    {
        var store = new BellekTokenStore();
        await store.YazAsync("gecerli");
        var c = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.TooManyRequests, govde), store);

        var hata = await Assert.ThrowsAsync<KasaApiException>(() => c.LoginAsync("editor", "yanlis"));

        Assert.Equal(HttpStatusCode.TooManyRequests, hata.DurumKodu);
        Assert.Equal(beklenen, hata.Message);
        Assert.Equal("gecerli", await store.OkuAsync());
    }
}
