using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class EkstreAktarmaApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => fn(r, c); }
    private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
    private static KasaApiClient Client(HttpMessageHandler h, ITokenStore? store = null) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/"), Timeout = TimeSpan.FromSeconds(60) }, store ?? new BellekTokenStore());
    private static EkstreBelgeDto Belge() => new(8, 2, "Banka", "QNB", "Son 1234", null, "hareket.pdf", DateTimeOffset.UtcNow, [], [], []);
    [Fact] public async Task Pdf_multipart_kaynak_banka_hesap_kart_ve_guvenli_adla_yuklenir()
    {
        var store = new BellekTokenStore(); await store.YazAsync("token"); string? body = null;
        var client = Client(new Handler(async (r, ct) =>
        {
            Assert.Equal("/api/ekstre-aktar/yukle", r.RequestUri!.AbsolutePath); Assert.Equal("Bearer", r.Headers.Authorization!.Scheme); Assert.Equal("token", r.Headers.Authorization.Parameter);
            Assert.True(ct.CanBeCanceled); Assert.IsType<MultipartFormDataContent>(r.Content); body = await r.Content!.ReadAsStringAsync(ct); return Response(Belge());
        }), store);
        var b = await client.EkstreYukleAsync(Encoding.UTF8.GetBytes("%PDF"), "C:\\gizli\\hareket.pdf", "Kart", "Vakifbank", "", 4);
        Assert.Equal(8, b.Id); Assert.Contains("name=dosya", body); Assert.Contains("application/pdf", body); Assert.Contains("name=kaynak", body); Assert.Contains("Vakifbank", body); Assert.Contains("name=kartId", body); Assert.DoesNotContain("gizli", body);
    }
    [Fact] public async Task Yukleme_iptal_tokenini_handlera_iletir()
    {
        var started = new TaskCompletionSource(); var client = Client(new Handler(async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Response(Belge()); }));
        using var cancel = new CancellationTokenSource(); var task = client.EkstreYukleAsync([1], "a.pdf", "Banka", "QNB", "Ana", null, cancel.Token);
        await started.Task; cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
    [Fact] public async Task Onizleme_ve_kayit_ayni_kaynak_satir_kurus_ve_istek_anahtarini_korur()
    {
        var requests = new List<(string Path, EkstreKaydetYaz Yaz)>();
        var client = Client(new Handler(async (r, ct) =>
        {
            var g = JsonSerializer.Deserialize<EkstreKaydetYaz>(await r.Content!.ReadAsStringAsync(ct), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            requests.Add((r.RequestUri!.AbsolutePath, g));
            return r.RequestUri!.AbsolutePath.EndsWith("onizleme") ? Response(new EkstreOnizlemeDto("hash", -12.34m, [], [], true)) : Response(Belge());
        }));
        var g = new EkstreKaydetYaz(Guid.NewGuid(), 2, [new(9, new(2026, 9, 27), "Ödeme", 12.34m, "KartOdemesi", "Otomatik", [], 3)]);
        var p = await client.EkstreOnizlemeAsync(8, g); await client.EkstreKaydetAsync(8, g with { OnizlemeOzeti = p.OnizlemeOzeti, TekrarOnay = true });
        Assert.Equal("/api/ekstre-aktar/8/onizleme", requests[0].Path); Assert.Equal("/api/ekstre-aktar/8/kaydet", requests[1].Path);
        Assert.Equal(g.IstekId, requests[1].Yaz.IstekId); Assert.Equal("hash", requests[1].Yaz.OnizlemeOzeti); Assert.True(requests[1].Yaz.TekrarOnay); Assert.Equal(12.34m, requests[1].Yaz.Satirlar.Single().Tutar); Assert.Equal(9, requests[1].Yaz.Satirlar.Single().SatirNo);
    }
    [Fact] public async Task Iptal_kaynak_belge_ve_kayit_yolunu_kullanir()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, JsonSerializer.Serialize(Belge(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var g = new EkstreIptalYaz(Guid.NewGuid(), "Yanlış seçim"); await Client(h).EkstreKayitIptalAsync(8, 17, g);
        Assert.Equal("/api/ekstre-aktar/8/kayitlar/17/iptal", h.SonIstek!.RequestUri!.AbsolutePath); Assert.Equal(g, JsonSerializer.Deserialize<EkstreIptalYaz>(h.SonGovde!, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    [Theory] [InlineData(422)] [InlineData(413)] [InlineData(503)]
    public async Task Pdf_hatasi_guvenli_json_mesajini_gosterir_html_gostermez(int code)
    {
        var json = new SahteHandler().Kuyrukla((HttpStatusCode)code, "{\"detail\":\"Şifresiz PDF seçin.\"}");
        Assert.Equal("Şifresiz PDF seçin.", (await Assert.ThrowsAsync<KasaApiException>(() => Client(json).EkstreBelgeAsync(1))).Message);
        var html = new SahteHandler().Kuyrukla((HttpStatusCode)code, "<html>gizli-sunucu</html>");
        Assert.DoesNotContain("gizli-sunucu", (await Assert.ThrowsAsync<KasaApiException>(() => Client(html).EkstreBelgeAsync(1))).Message);
    }
    [Fact] public async Task Kaynak_pdf_indirme_dosya_adini_temizler()
    {
        var c = Client(new Handler((r, ct) =>
        {
            Assert.Equal("/api/ekstre-aktar/8/dosya", r.RequestUri!.AbsolutePath);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = "../../gizli/ekstre.pdf" }; return Task.FromResult(response);
        }));
        var file = await c.EkstreDosyaAsync(8); Assert.Equal("ekstre.pdf", file.DosyaAdi); Assert.Equal(new byte[] { 1, 2, 3 }, file.Icerik);
    }
    [Fact] public void Eski_sunucu_jsonlari_yeni_opsiyonel_alanlar_olmadan_okunur()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var rapor = JsonSerializer.Deserialize<AylikRaporDto>("{\"yil\":2026,\"ay\":9,\"kanallar\":[]}", options)!; Assert.Equal(0, rapor.GenelGelir);
        var harcama = JsonSerializer.Deserialize<KartHarcamaDto>("{\"id\":1,\"tarih\":\"2026-09-27\",\"dagilimlar\":[]}", options)!; Assert.Null(harcama.EkstreKayitId);
        var odeme = JsonSerializer.Deserialize<KartTakipOdemeDto>("{\"id\":1,\"tarih\":\"2026-09-27\",\"dagilimlar\":[]}", options)!; Assert.Null(odeme.EkstreKayitId);
    }
    [Fact] public async Task Eski_belgeler_imlecle_ve_kaynak_kayit_idsiyle_erisilebilir()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, "[]").Kuyrukla(HttpStatusCode.OK, JsonSerializer.Serialize(Belge(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var c = Client(h); await c.EkstreBelgelerAsync(42); Assert.Equal("?beforeId=42", h.SonIstek!.RequestUri!.Query);
        Assert.Equal(8, (await c.EkstreKaynakBelgeAsync(99)).Id); Assert.Equal("/api/ekstre-aktar/kayitlar/99", h.SonIstek!.RequestUri!.AbsolutePath);
    }
}
