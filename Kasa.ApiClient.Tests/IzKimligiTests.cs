using System.Net;

namespace Kasa.ApiClient.Tests;

/// <summary>IST4: sunucu hatası (5xx) yanıtındaki ProblemDetails iz kimliği (traceId) istisnada taşınır; kullanıcıya kısa
/// "Hata kodu" olarak gösterilir, yönetici sunucu logunda tam iz kimliğini bu parçayla bulur.</summary>
public class IzKimligiTests
{
    private const string W3cIz = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        return (new KasaApiClient(new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") }, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task Sunucu_hatasinin_problem_details_iz_kimligi_istisnada_tasinir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.InternalServerError, $$"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.1","title":"Veri bütünlüğü hatası","status":500,"detail":"Kayıt bir veri bütünlüğü kuralına takıldığı için kaydedilmedi.","traceId":"{{W3cIz}}"}""");

        var hata = await Assert.ThrowsAsync<KasaApiException>(() => c.KanalSilAsync(3));

        Assert.Equal(HttpStatusCode.InternalServerError, hata.DurumKodu);
        Assert.Equal(W3cIz, hata.IzKimligi);
        Assert.Equal("4bf92f35", hata.HataKodu);
    }

    [Fact]
    public async Task Iz_kimligi_yalniz_sunucu_hatasinda_okunur_govdesiz_ya_da_json_disi_yanit_bozulmaz()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Conflict, $$"""{"hata":"Kayıt değişti.","traceId":"{{W3cIz}}"}""")
         .Kuyrukla(HttpStatusCode.BadGateway)
         .Kuyrukla(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("<html>hata</html>") });

        var cakisma = await Assert.ThrowsAsync<KasaApiException>(() => c.KanalSilAsync(1));
        var govdesiz = await Assert.ThrowsAsync<KasaApiException>(() => c.KanalSilAsync(2));
        var html = await Assert.ThrowsAsync<KasaApiException>(() => c.KanalSilAsync(3));

        Assert.Equal("Kayıt değişti.", cakisma.Message);
        Assert.Null(cakisma.HataKodu);
        Assert.Null(govdesiz.IzKimligi);
        Assert.Null(html.IzKimligi);
        Assert.Null(html.HataKodu);
        Assert.Equal(3, h.Istekler.Count);
    }

    [Theory]
    [InlineData(W3cIz, "4bf92f35")]
    [InlineData("0HN7ABCDEF:00000002", "0HN7ABCDEF:00000002")]
    [InlineData("0HN7ABCDEFGHIJKLMNOPQRSTU:00000002", "0HN7ABCDEFGH")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    [InlineData("<script>", null)]
    public void Kisa_iz_w3c_kimliginin_ilk_sekiz_hanesi_ya_da_kisa_kimligin_kendisidir(string? iz, string? beklenen)
        => Assert.Equal(beklenen, KasaApiException.KisaIz(iz));
}
