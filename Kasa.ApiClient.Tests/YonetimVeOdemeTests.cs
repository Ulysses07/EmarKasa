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

    [Fact]
    public async Task Odeme_tasima_kaynak_hedef_surumu_ve_gerekceyi_korur()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, "{\"id\":7,\"surum\":4,\"kalemler\":[],\"odemeler\":[]}");
        var g = new AlisOdemeDuzeltYaz(3, Guid.NewGuid(), new(2026, 9, 23), 10m, null, null, "Yanlış alış", 9, 5);
        Assert.Equal(4, (await Client(h).AlisOdemeDuzeltAsync(7, 2, g)).Surum);
        Assert.Equal(HttpMethod.Put, h.SonIstek!.Method);
        Assert.EndsWith("/api/alis/7/odemeler/2", h.SonIstek.RequestUri!.AbsolutePath);
        Assert.Equal(g, JsonSerializer.Deserialize<AlisOdemeDuzeltYaz>(h.SonGovde!, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    [Fact]
    public async Task Sifre_degistiginde_token_temizlenir_ve_girise_donus_bildirilir()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("eski");
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.NoContent);
        var c = Client(h, store);
        var bildirim = 0;
        c.OturumSonlandi += (_, _) => bildirim++;
        await c.SifreDegistirAsync(new("eski-sifre", "yeni-guvenli-sifre"));
        Assert.Null(await store.OkuAsync());
        Assert.Equal(1, bildirim);
        Assert.EndsWith("/api/auth/sifre", h.SonIstek!.RequestUri!.AbsolutePath);
    }
    [Fact]
    public async Task Kurtarma_anonimdir_ve_401_mevcut_tokeni_silmez()
    {
        var store = new BellekTokenStore();
        await store.YazAsync("eski");
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<KasaApiException>(() => Client(h, store).SifreKurtarAsync(new("admin", "kod", "yeni-guvenli-sifre")));
        Assert.Null(h.SonIstek!.Headers.Authorization);
        Assert.Equal("eski", await store.OkuAsync());
    }
    [Fact]
    public async Task Eski_sifre_degisikliginin_geciken_basarisi_yeni_oturumu_silmez()
    {
        var bekleyen = new TaskCompletionSource<HttpResponseMessage>();
        var store = new BellekTokenStore();
        await store.YazAsync("eski");
        var c = Client(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/sifre") ? bekleyen.Task : Task.FromResult(Json("{\"rol\":\"editor\",\"token\":\"yeni\"}"))), store);
        var bildirildi = false;
        c.OturumSonlandi += (_, _) => bildirildi = true;
        var sifre = c.SifreDegistirAsync(new("eski-sifre", "yeni-guvenli-sifre"));
        await c.LoginAsync("yeni", "baska");
        bekleyen.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await sifre;
        Assert.Equal("yeni", await store.OkuAsync());
        Assert.False(bildirildi);
    }
    [Fact]
    public async Task Belge_multipart_dosya_ve_odeme_alaniyla_gonderilir()
    {
        string? body = null;
        var c = Client(new Handler(async r =>
        {
            Assert.EndsWith("/api/alis/7/belgeler", r.RequestUri!.AbsolutePath);
            Assert.Equal("multipart/form-data", r.Content!.Headers.ContentType!.MediaType);
            body = await r.Content.ReadAsStringAsync();
            return Json("{\"id\":8,\"alisId\":7,\"odemeId\":3,\"dosyaAdi\":\"dekont.pdf\",\"icerikTuru\":\"application/pdf\",\"boyut\":4,\"yuklendi\":\"2026-09-23T12:00:00Z\"}");
        }));
        var b = await c.BelgeYukleAsync(7, "C:\\gizli\\dekont.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF"), 3);
        Assert.Equal(8, b.Id);
        Assert.Contains("name=dosya", body);
        Assert.Contains("name=odemeId", body);
        Assert.DoesNotContain("gizli", body);
    }
    [Fact]
    public async Task Yedek_durumu_rotasyon_uyarisini_okur_eski_sunucuda_bos_kalir()
    {
        var h = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, "{\"otomatikEtkin\":true,\"sonYedek\":null,\"sonDogrulama\":null,\"hata\":null,\"sonOtomatikYedek\":null,\"otomatikYedekSayisi\":3,\"sonElleYedek\":null,\"elleYedekSayisi\":1,\"rotasyonUyarisi\":\"Otomatik rotasyonu tamamlanamadı.\"}")
            .Kuyrukla(HttpStatusCode.OK, "{\"otomatikEtkin\":false,\"sonYedek\":null,\"sonDogrulama\":null,\"hata\":null}");
        var c = Client(h);
        Assert.Equal("Otomatik rotasyonu tamamlanamadı.", (await c.YedekDurumuAsync()).RotasyonUyarisi);
        Assert.EndsWith("/api/yedek/durum", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Null((await c.YedekDurumuAsync()).RotasyonUyarisi);
    }
    [Fact]
    public async Task Yedek_durumu_disk_alanlarini_okur_eski_sunucuda_bos_kalir()
    {
        var h = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, "{\"otomatikEtkin\":true,\"sonYedek\":null,\"sonDogrulama\":null,\"hata\":null,\"yedekDiskiBosAlanBayt\":104857600,\"veriDiskiBosAlanBayt\":5368709120,\"toplamYedekBayt\":1024,\"asgariBosAlanBayt\":2147483648,\"diskUyarisi\":\"Yedek diskinde 0,1 GB boş alan kaldı.\",\"belgeUyarisi\":\"1 belge bulunamadı.\"}")
            .Kuyrukla(HttpStatusCode.OK, "{\"otomatikEtkin\":false,\"sonYedek\":null,\"sonDogrulama\":null,\"hata\":null}");
        var c = Client(h);
        var d = await c.YedekDurumuAsync();
        Assert.Equal((104857600L, 5368709120L, 1024L, 2147483648L), (d.YedekDiskiBosAlanBayt, d.VeriDiskiBosAlanBayt, d.ToplamYedekBayt, d.AsgariBosAlanBayt));
        Assert.Equal(("Yedek diskinde 0,1 GB boş alan kaldı.", "1 belge bulunamadı."), (d.DiskUyarisi, d.BelgeUyarisi));
        var eski = await c.YedekDurumuAsync();
        Assert.Null(eski.YedekDiskiBosAlanBayt);
        Assert.Null(eski.DiskUyarisi);
        Assert.Null(eski.BelgeUyarisi);
    }
    [Fact]
    public async Task Yedek_durumu_son_geri_yukleme_raporunu_okur_eski_sunucuda_bos_kalir()
    {
        var h = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, "{\"otomatikEtkin\":true,\"sonYedek\":null,\"sonDogrulama\":null,\"hata\":null,\"sonGeriYukleme\":\"2026-09-28T10:15:00+00:00\",\"geriYuklemeRaporu\":[\"Bütün oturumlar kapatıldı; herkes yeniden giriş yapmalı.\",\"Kurtarma kodu iptal edildi.\"]}")
            .Kuyrukla(HttpStatusCode.OK, "{\"otomatikEtkin\":false,\"sonYedek\":null,\"sonDogrulama\":null,\"hata\":null}");
        var c = Client(h);
        var d = await c.YedekDurumuAsync();
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 10, 15, 0, TimeSpan.Zero), d.SonGeriYukleme);
        Assert.Equal(new[] { "Bütün oturumlar kapatıldı; herkes yeniden giriş yapmalı.", "Kurtarma kodu iptal edildi." }, d.GeriYuklemeRaporu);
        var eski = await c.YedekDurumuAsync();
        Assert.Null(eski.SonGeriYukleme);
        Assert.Null(eski.GeriYuklemeRaporu);
    }
    [Fact]
    public async Task Belge_silme_gerekceyi_govdede_gonderir_gerekcesiz_govdesizdir_silinenler_sorgusu()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.NoContent).Kuyrukla(HttpStatusCode.NoContent)
            .Kuyrukla(HttpStatusCode.OK, "[{\"id\":8,\"alisId\":7,\"odemeId\":null,\"dosyaAdi\":\"fis.pdf\",\"icerikTuru\":\"application/pdf\",\"boyut\":4,\"yuklendi\":\"2026-09-23T12:00:00Z\",\"yukleyenRol\":\"alici\",\"yukleyen\":\"Ayşe\",\"silindi\":true,\"silinmeZamani\":\"2026-09-24T08:00:00Z\",\"silenRol\":\"editor\",\"silen\":\"Editör\",\"silmeGerekcesi\":\"Yanlış fiş\"}]")
            .Kuyrukla(HttpStatusCode.OK, "[{\"id\":9,\"alisId\":7,\"odemeId\":null,\"dosyaAdi\":\"eski.pdf\",\"icerikTuru\":\"application/pdf\",\"boyut\":4,\"yuklendi\":\"2026-09-23T12:00:00Z\"}]");
        var c = Client(h);
        await c.BelgeSilAsync(8, "  Yanlış fiş  ");
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.EndsWith("/api/belgeler/8", h.SonIstek.RequestUri!.AbsolutePath);
        Assert.Equal("Yanlış fiş", JsonDocument.Parse(h.SonGovde!).RootElement.GetProperty("gerekce").GetString());
        await c.BelgeSilAsync(9);
        Assert.Null(h.SonIstek!.Content);
        var silinen = Assert.Single(await c.BelgelerAsync(7, silinenler: true));
        Assert.Equal("?silinenler=true", h.SonIstek!.RequestUri!.Query);
        Assert.Equal((true, "Ayşe", "Editör", "Yanlış fiş"), (silinen.Silindi, silinen.Yukleyen, silinen.Silen, silinen.SilmeGerekcesi));
        var eski = Assert.Single(await c.BelgelerAsync(7));
        Assert.Equal("", h.SonIstek!.RequestUri!.Query);
        Assert.Equal((false, (string?)null), (eski.Silindi, eski.YukleyenRol));
    }
    [Fact]
    public async Task Dosya_indirme_yol_gecisini_temizler_ve_baytlari_korur()
    {
        var c = Client(new Handler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
            r.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = "../../gizli/rapor.xlsx" };
            return Task.FromResult(r);
        }));
        var hedef = new MemoryStream();
        var d = await c.DisariAktarAsync(new(2026, 9, 1), new(2026, 9, 23), "A & B", "xlsx", hedef);
        Assert.Equal("rapor.xlsx", d.DosyaAdi);
        Assert.Equal(new byte[] { 1, 2, 3 }, hedef.ToArray());
        Assert.Equal(3, d.Boyut);
    }
}
