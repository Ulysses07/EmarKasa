using System.Net;

namespace Kasa.ApiClient.Tests;

/// <summary>
/// Masaüstü tanıdık cihaz belirteci: girişte gövdedeki 'cihaz' güvenli depoya yazılır ve sonraki girişlerde
/// X-Kasa-Cihaz başlığıyla gönderilir. Oturumdan bağımsızdır: çıkış ve oturum sonu yalnız JWT'yi siler.
/// </summary>
public class TanidikCihazTests
{
    private static (KasaApiClient Client, SahteHandler Handler, BellekTokenStore Store) Kur()
    {
        var handler = new SahteHandler();
        var store = new BellekTokenStore();
        return (new KasaApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") }, store), handler, store);
    }

    private static string? CihazBasligi(HttpRequestMessage istek)
        => istek.Headers.TryGetValues("X-Kasa-Cihaz", out var degerler) ? Assert.Single(degerler) : null;

    [Fact]
    public async Task Bellek_deposu_cihaz_belirtecini_oturumdan_ayri_tutar()
    {
        var store = new BellekTokenStore();
        Assert.Null(await store.CihazOkuAsync());
        await store.YazAsync("jwt");
        await store.CihazYazAsync("c1.cihaz");
        await store.TemizleAsync();
        Assert.Null(await store.OkuAsync());
        Assert.Equal("c1.cihaz", await store.CihazOkuAsync());
    }

    [Fact]
    public async Task Ilk_giriste_baslik_gitmez_yanittaki_belirtec_saklanir()
    {
        var (client, handler, store) = Kur();
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-1","cihaz":"c1.100.kimlik.imza"}""");

        var yanit = await client.LoginAsync("editor", "sifre");

        Assert.Null(CihazBasligi(handler.SonIstek!));
        Assert.Equal("c1.100.kimlik.imza", yanit.Cihaz);
        Assert.Equal("c1.100.kimlik.imza", await store.CihazOkuAsync());
        Assert.Equal("jwt-1", await store.OkuAsync());
    }

    [Fact]
    public async Task Sonraki_giris_saklanan_belirteci_basliga_koyar_ve_yenisiyle_degistirir()
    {
        var (client, handler, store) = Kur();
        await store.CihazYazAsync("c1.eski");
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-2","cihaz":"c1.yeni"}""");

        await client.LoginAsync("editor", "sifre");

        Assert.Equal("c1.eski", CihazBasligi(handler.SonIstek!));
        Assert.Equal("c1.yeni", await store.CihazOkuAsync());
    }

    [Fact]
    public async Task Belirtecsiz_yanit_ve_basarisiz_giris_saklanan_belirteci_silmez()
    {
        var (client, handler, store) = Kur();
        await store.CihazYazAsync("c1.saklanan");
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"viewer","token":"jwt-3"}""")
               .Kuyrukla(HttpStatusCode.Unauthorized)
               .Kuyrukla(HttpStatusCode.TooManyRequests, """{"hata":"Çok fazla deneme yapıldı. 15 dakika sonra yeniden deneyin."}""");

        Assert.Null((await client.LoginAsync(null, "sifre")).Cihaz);
        await Assert.ThrowsAsync<KasaApiException>(() => client.LoginAsync("editor", "yanlis"));
        var red = await Assert.ThrowsAsync<KasaApiException>(() => client.LoginAsync("editor", "sifre"));

        Assert.Equal(HttpStatusCode.TooManyRequests, red.DurumKodu);
        Assert.Equal("Çok fazla deneme yapıldı. 15 dakika sonra yeniden deneyin.", red.Message);
        Assert.Equal("c1.saklanan", CihazBasligi(handler.SonIstek!));
        Assert.Equal("c1.saklanan", await store.CihazOkuAsync());
    }

    [Fact]
    public async Task Cikis_ve_oturum_sonu_cihaz_belirtecini_silmez_diger_isteklere_eklenmez()
    {
        var (client, handler, store) = Kur();
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-4","cihaz":"c1.cihaz"}""")
               .Kuyrukla(HttpStatusCode.Unauthorized)
               .Kuyrukla(HttpStatusCode.NoContent);
        await client.LoginAsync("editor", "sifre");

        await Assert.ThrowsAsync<KasaApiException>(client.PanelAsync);
        Assert.Null(CihazBasligi(handler.SonIstek!));
        Assert.Null(await store.OkuAsync());
        await client.CikisAsync();

        Assert.Equal("c1.cihaz", await store.CihazOkuAsync());
    }
}
