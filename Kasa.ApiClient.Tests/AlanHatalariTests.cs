using System.Net;

namespace Kasa.ApiClient.Tests;

/// <summary>Sunucunun doğrulama yanıtındaki alan sözlüğü (ValidationProblem "errors") <see cref="KasaApiException.AlanHatalari"/>'na
/// taşınır: alan adı küçük harfe iner, ilk ileti alınır; birleşik ileti (Message) eskisi gibi kalır.</summary>
public class AlanHatalariTests
{
    private static KasaApiClient Kur(SahteHandler h)
        => new(new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") }, new BellekTokenStore());

    [Fact]
    public async Task Dogrulama_sozlugu_alan_hatalarina_ilk_iletiyle_kucuk_harfle_aktarilir()
    {
        const string govde = """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"cari":["Bu alan boş olamaz.","En fazla 200 karakter girilebilir."],"krediKartiId":["Kayıtlı bir kredi kartı seçin."],
                       "kalemler[0].aciklama":["Bu alan boş olamaz."],"bos":[]}}
            """;
        var c = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.BadRequest, govde));

        var hata = await Assert.ThrowsAsync<KasaApiException>(() => c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 10, 2), "", 5m, "MEZAT", GiderTipi.Cari, null)));

        Assert.Equal(3, hata.AlanHatalari.Count);
        Assert.Equal("Bu alan boş olamaz.", hata.AlanHatalari["cari"]);
        Assert.Equal("Kayıtlı bir kredi kartı seçin.", hata.AlanHatalari["kredikartiid"]);
        Assert.Equal("Bu alan boş olamaz.", hata.AlanHatalari["kalemler[0].aciklama"]);
        // Geriye uyum: birleşik ileti aynı kalır (her iletinin bir kez geçtiği satırlar).
        Assert.Equal("Bu alan boş olamaz.\nEn fazla 200 karakter girilebilir.\nKayıtlı bir kredi kartı seçin.", hata.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "{\"hata\":\"Kanal kullanımda.\"}", "Kanal kullanımda.")]
    [InlineData(HttpStatusCode.BadRequest, "{\"detail\":\"Geçersiz tarih.\"}", "Geçersiz tarih.")]
    [InlineData(HttpStatusCode.BadRequest, "\"Düz ileti.\"", "Düz ileti.")]
    [InlineData(HttpStatusCode.BadRequest, "<html>proxy error</html>", "Girilen bilgileri kontrol edin.")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"errors\":{\"cari\":[\"x\"]},\"traceId\":\"abc\"}", "API hatası: 500 InternalServerError")]
    public async Task Alan_sozlugu_olmayan_yanit_bos_alan_hatasi_ve_eski_iletiyi_verir(HttpStatusCode kod, string govde, string ileti)
    {
        var c = Kur(new SahteHandler().Kuyrukla(kod, govde));

        var hata = await Assert.ThrowsAsync<KasaApiException>(c.KanallarAsync);

        Assert.Empty(hata.AlanHatalari);
        Assert.Equal(ileti, hata.Message);
    }

    [Fact]
    public void Kurucu_alan_hatasi_verilmezse_bos_sozluk_tasir()
    {
        var hata = new KasaApiException(HttpStatusCode.BadRequest, "x");
        Assert.Empty(hata.AlanHatalari);
        var dolu = new KasaApiException(HttpStatusCode.BadRequest, "x", alanHatalari: new Dictionary<string, string> { ["ad"] = "Bu alan boş olamaz." });
        Assert.Equal("Bu alan boş olamaz.", dolu.AlanHatalari["ad"]);
    }
}
