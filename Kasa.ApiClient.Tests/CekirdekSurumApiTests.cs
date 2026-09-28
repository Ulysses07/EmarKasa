using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

/// <summary>
/// contract-6: gider, gelir, kanal ve ayarların sürümü okunur ve düzenleme gövdesinde her zaman gönderilir (yeni istemci); eski
/// sunucu sürüm göndermezse 0 okunur. Sürüm çakışmasının 409 iletisi istemciye taşınır.
/// </summary>
public class CekirdekSurumApiTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    private static int GovdeSurumu(SahteHandler h)
    {
        using var doc = JsonDocument.Parse(h.SonGovde!);
        return doc.RootElement.GetProperty("surum").GetInt32();
    }

    [Fact]
    public async Task Okuma_yanitlarindaki_surum_eslenir_eski_sunucuda_sifir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":1,"ad":"MEZAT","aktif":true,"sira":0,"acilisDevri":0,"surum":3},{"id":2,"ad":"TOPTAN","aktif":true,"sira":1,"acilisDevri":0}]""");
        Assert.Equal(new[] { 3, 0 }, (await c.KanallarAsync()).Select(k => k.Surum));
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":1,"tarih":"2026-03-05","cari":"K","tutarTl":5.0,"kanal":"MEZAT","tip":"Cari","not":null,"surum":7}]""");
        Assert.Equal(7, (await c.IslemlerAsync()).Single().Surum);
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":9,"donemStart":"2026-03-02","kanal":"MEZAT","tutarTl":25000.0,"kanalId":1,"eskiYinelenenGrup":false,"surum":2}]""");
        Assert.Equal(2, (await c.GelenlerAsync()).Single().Surum);
        h.Kuyrukla(HttpStatusCode.OK, """{"takipBaslangic":"2026-01-01","kasaAcilisDevri":0,"izleyiciSifreVarMi":false,"surum":5}""");
        Assert.Equal(5, (await c.AyarlarAsync()).Surum);
    }

    [Fact]
    public async Task Duzenleme_govdeleri_surumu_her_zaman_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"id":5,"tarih":"2026-03-05","cari":"K","tutarTl":6.0,"kanal":"MEZAT","tip":"Cari","not":null,"surum":5}""");
        var gider = await c.IslemGuncelleAsync(5, new IslemYaz(new DateOnly(2026, 3, 5), "K", 6m, "MEZAT", GiderTipi.Cari, null, Surum: 4));
        Assert.Equal((4, 5), (GovdeSurumu(h), gider.Surum));

        h.Kuyrukla(HttpStatusCode.OK, """{"id":4,"ad":"MEZAT","aktif":true,"sira":2,"acilisDevri":0,"surum":2}""");
        await c.KanalGuncelleAsync(4, new KanalYaz("MEZAT", true, 2, 0m, 1));
        Assert.Equal(1, GovdeSurumu(h));

        h.Kuyrukla(HttpStatusCode.OK, """{"id":9,"donemStart":"2026-03-02","kanal":"MEZAT","tutarTl":1.0,"surum":1}""");
        await c.GelenKaydetAsync(new GelenYaz(new DateOnly(2026, 3, 2), "MEZAT", 1m));
        Assert.Equal(0, GovdeSurumu(h)); // satır yok: 0 da gönderilir (atlanmaz)

        h.Kuyrukla(HttpStatusCode.OK);
        await c.AyarGuncelleAsync(new AyarYaz(new DateOnly(2026, 1, 1), 0m, 6));
        Assert.Equal(6, GovdeSurumu(h));
    }

    [Fact]
    public async Task Surum_cakismasi_409_iletisiyle_tasinir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Conflict, """{"hata":"Gider başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin."}""");
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => c.IslemGuncelleAsync(5, new IslemYaz(new DateOnly(2026, 3, 5), "K", 6m, "MEZAT", GiderTipi.Cari, null)));
        Assert.Equal((HttpStatusCode.Conflict, "Gider başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin."), (hata.DurumKodu, hata.Message));
    }
}
