using System.Net;

namespace Kasa.ApiClient.Tests;

/// <summary>tests-8: test altyapısının kendisi. Kurgulanmamış istek 200 ile gizlenmez; bütün istekler sayılabilir.</summary>
public class SahteHandlerTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        return (new KasaApiClient(new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") }, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task Kuyruk_bosken_gelen_istek_test_hatasi_verir_200_uydurulmaz()
    {
        var (c, h) = Kur();

        var hata = await Assert.ThrowsAsync<InvalidOperationException>(() => c.KanalSilAsync(3));

        Assert.Contains("Beklenmeyen istek: DELETE https://ornek.test/api/kanallar/3", hata.Message);
        Assert.Single(h.Istekler);
    }

    [Fact]
    public async Task Butun_istekler_sirasiyla_kaydedilir_ve_kalan_yanit_sayilir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.NoContent).Kuyrukla(HttpStatusCode.OK, """{"id":4,"ad":"MEZAT","aktif":true,"sira":2,"acilisDevri":0}""").Kuyrukla(HttpStatusCode.NoContent);

        await c.KanalSilAsync(3);
        await c.KanalGuncelleAsync(4, new KanalYaz("MEZAT", true, 2, 0m));

        Assert.Equal(2, h.Istekler.Count);
        Assert.Equal(HttpMethod.Delete, h.Istekler[0].Istek.Method); Assert.Null(h.Istekler[0].Govde);
        Assert.Equal(HttpMethod.Put, h.Istekler[1].Istek.Method); Assert.Contains("\"sira\":2", h.Istekler[1].Govde);
        Assert.Same(h.Istekler[1].Istek, h.SonIstek); Assert.Equal(h.Istekler[1].Govde, h.SonGovde);
        Assert.Equal(1, h.KalanYanit);
    }
}
