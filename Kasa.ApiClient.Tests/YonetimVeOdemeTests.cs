using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class YonetimVeOdemeTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> fn) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => fn(r); }
    private static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
    private static KasaApiClient Client(HttpMessageHandler h, ITokenStore? store = null) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, store ?? new BellekTokenStore());

    [Fact] public async Task Odeme_tasima_kaynak_hedef_surumu_ve_gerekceyi_korur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, "{\"id\":7,\"surum\":4,\"kalemler\":[],\"odemeler\":[]}");
        var g = new AlisOdemeDuzeltYaz(3, Guid.NewGuid(), new(2026, 9, 23), 10m, null, null, "Yanlış alış", 9, 5);
        Assert.Equal(4, (await Client(h).AlisOdemeDuzeltAsync(7, 2, g)).Surum);
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.EndsWith("/api/alis/7/odemeler/2", h.SonIstek.RequestUri!.AbsolutePath);
        Assert.Equal(g, JsonSerializer.Deserialize<AlisOdemeDuzeltYaz>(h.SonGovde!, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    [Fact] public async Task Sifre_degistiginde_token_temizlenir_ve_girise_donus_bildirilir()
    {
        var store = new BellekTokenStore(); await store.YazAsync("eski");
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.NoContent); var c = Client(h, store); var bildirim = 0;
        c.OturumSonlandi += (_, _) => bildirim++;
        await c.SifreDegistirAsync(new("eski-sifre", "yeni-guvenli-sifre"));
        Assert.Null(await store.OkuAsync()); Assert.Equal(1, bildirim); Assert.EndsWith("/api/auth/sifre", h.SonIstek!.RequestUri!.AbsolutePath);
    }
    [Fact] public async Task Kurtarma_anonimdir_ve_401_mevcut_tokeni_silmez()
    {
        var store = new BellekTokenStore(); await store.YazAsync("eski");
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<KasaApiException>(() => Client(h, store).SifreKurtarAsync(new("admin", "kod", "yeni-guvenli-sifre")));
        Assert.Null(h.SonIstek!.Headers.Authorization); Assert.Equal("eski", await store.OkuAsync());
    }
    [Fact] public async Task Eski_sifre_degisikliginin_geciken_basarisi_yeni_oturumu_silmez()
    {
        var bekleyen = new TaskCompletionSource<HttpResponseMessage>();
        var store = new BellekTokenStore(); await store.YazAsync("eski");
        var c = Client(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/sifre") ? bekleyen.Task : Task.FromResult(Json("{\"rol\":\"editor\",\"token\":\"yeni\"}"))), store);
        var bildirildi = false; c.OturumSonlandi += (_, _) => bildirildi = true;
        var sifre = c.SifreDegistirAsync(new("eski-sifre", "yeni-guvenli-sifre"));
        await c.LoginAsync("yeni", "baska");
        bekleyen.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent)); await sifre;
        Assert.Equal("yeni", await store.OkuAsync()); Assert.False(bildirildi);
    }
    [Fact] public async Task Belge_multipart_dosya_ve_odeme_alaniyla_gonderilir()
    {
        string? body = null;
        var c = Client(new Handler(async r =>
        {
            Assert.EndsWith("/api/alis/7/belgeler", r.RequestUri!.AbsolutePath);
            Assert.Equal("multipart/form-data", r.Content!.Headers.ContentType!.MediaType);
            body = await r.Content.ReadAsStringAsync(); return Json("{\"id\":8,\"alisId\":7,\"odemeId\":3,\"dosyaAdi\":\"dekont.pdf\",\"icerikTuru\":\"application/pdf\",\"boyut\":4,\"yuklendi\":\"2026-09-23T12:00:00Z\"}");
        }));
        var b = await c.BelgeYukleAsync(7, "C:\\gizli\\dekont.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF"), 3);
        Assert.Equal(8, b.Id); Assert.Contains("name=dosya", body); Assert.Contains("name=odemeId", body); Assert.DoesNotContain("gizli", body);
    }
    [Fact] public async Task Dosya_indirme_yol_gecisini_temizler_ve_baytlari_korur()
    {
        var c = Client(new Handler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
            r.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = "../../gizli/rapor.xlsx" };
            return Task.FromResult(r);
        }));
        var d = await c.DisariAktarAsync(new(2026, 9, 1), new(2026, 9, 23), "A & B", "xlsx");
        Assert.Equal("rapor.xlsx", d.DosyaAdi); Assert.Equal(new byte[] { 1, 2, 3 }, d.Icerik);
    }
}
