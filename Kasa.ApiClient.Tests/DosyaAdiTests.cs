using System.Net;
using System.Net.Http.Headers;

namespace Kasa.ApiClient.Tests;

/// <summary>
/// İndirilen dosyanın adı (purchase-1, apiclient-3): uzantı sunucunun bildirdiği içerik türünden türetilir; alıcının
/// yüklediği ad '.hta'/'.cmd' gibi çalıştırılabilir bir uzantı ya da Unicode yön işaretiyle (U+202E) masaüstünde
/// kaydedilemez. Tür bilinmiyorsa yalnız güvenli uzantılar korunur, gerisi '.bin' olur.
/// </summary>
public class DosyaAdiTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(fn(r)); }

    private static async Task<IndirmeBilgisi> Indir(string? ad, string? tur, bool yildizli = true, int belgeId = 5)
    {
        var c = new KasaApiClient(new HttpClient(new Handler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            if (tur is not null)
                r.Content.Headers.ContentType = new MediaTypeHeaderValue(tur);
            if (ad is not null)
                r.Content.Headers.ContentDisposition = yildizli
                ? new ContentDispositionHeaderValue("attachment") { FileNameStar = ad }
                : new ContentDispositionHeaderValue("attachment") { FileName = ad };
            return r;
        }))
        { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
        return await c.BelgeIndirAsync(belgeId, new MemoryStream());
    }

    [Theory]
    [InlineData("fatura.pdf.hta", "application/pdf", "fatura.pdf")]
    [InlineData("fatura‮fdp.hta", "application/pdf", "faturafdp.pdf")]
    [InlineData("CON.pdf", "application/pdf", "belge-5.pdf")]
    [InlineData("fatura.pdf", "application/pdf", "fatura.pdf")]
    [InlineData("foto.jpeg", "image/jpeg", "foto.jpg")]
    [InlineData("kurulum.cmd", "application/octet-stream", "kurulum.bin")]
    [InlineData("kurulum.pdf", null, "kurulum.pdf")]
    [InlineData(null, "image/png", "belge-5.png")]
    public async Task Indirilen_belge_adi_icerik_turune_zorlanir(string? ad, string? tur, string beklenen)
        => Assert.Equal(beklenen, (await Indir(ad, tur)).DosyaAdi);

    [Fact]
    public async Task Yildizsiz_ad_da_yoldan_ve_turden_arindirilir()
        => Assert.Equal("x.jpg", (await Indir("\"..\\\\x.cmd\"", "image/jpeg", yildizli: false)).DosyaAdi);

    [Theory]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("image/png", ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("application/zip", ".zip")]
    [InlineData("text/csv", ".csv")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx")]
    [InlineData("text/html", ".html")]
    [InlineData("application/hta", ".bin")]
    [InlineData(null, ".bin")]
    public void Uzanti_yalniz_bilinen_turden_gelir(string? tur, string beklenen) => Assert.Equal(beklenen, DosyaTurleri.Uzanti(tur));

    [Theory]
    [InlineData("rapor.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "dosya", "rapor.xlsx")]
    [InlineData("rapor.xlsx", "text/html", "dosya", "rapor.html")]
    [InlineData("a​b⁦c.csv", "text/csv", "dosya", "abc.csv")]
    [InlineData("  ..  ", "application/zip", "kasa-yedek.zip", "kasa-yedek.zip")]
    [InlineData("lpt1.zip", "application/zip", "kasa-yedek.zip", "kasa-yedek.zip")]
    [InlineData("kasa-yedek.zip", "application/zip", "yedek", "kasa-yedek.zip")]
    [InlineData("belge.html", null, "dosya", "belge.bin")]
    [InlineData("belge.PNG", "", "dosya", "belge.PNG")]
    public void Guvenli_ad_kurallari(string? ad, string? tur, string varsayilan, string beklenen)
        => Assert.Equal(beklenen, DosyaTurleri.GuvenliAd(ad, tur, varsayilan));

    [Fact]
    public void Guvenli_ad_tekrar_uygulaninca_degismez()
    {
        foreach (var (ad, tur) in new[] { ("fatura.pdf.hta", "application/pdf"), ("x", "image/png"), ("rapor", "text/csv"), ("a.b.c", null) })
        {
            var bir = DosyaTurleri.GuvenliAd(ad, tur, "dosya");
            Assert.Equal(bir, DosyaTurleri.GuvenliAd(bir, tur, "dosya"));
        }
    }
}
