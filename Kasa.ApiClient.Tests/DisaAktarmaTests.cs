using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Kasa.ApiClient.Tests;

/// <summary>Excel'e aktar: CSV baytları ve sunucunun önerdiği dosya adı istemciye gelir.</summary>
public class DisaAktarmaTests
{
    /// <summary>Dosya yanıtı (Content-Disposition'lı) döndüren handler.</summary>
    private sealed class DosyaHandler(HttpStatusCode kod, byte[]? icerik, string? disposition, string? json = null) : HttpMessageHandler
    {
        public HttpRequestMessage? SonIstek;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SonIstek = request;
            var r = new HttpResponseMessage(kod);
            if (json is not null) r.Content = new StringContent(json, Encoding.UTF8, "application/json");
            else if (icerik is not null)
            {
                r.Content = new ByteArrayContent(icerik);
                r.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv; charset=utf-8");
                if (disposition is not null) r.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(disposition);
            }
            return Task.FromResult(r);
        }
    }

    private static readonly byte[] Csv = [0xEF, 0xBB, 0xBF, (byte)'a', (byte)';', (byte)'b', (byte)'\r', (byte)'\n'];

    private static async Task<(KasaApiClient c, DosyaHandler h)> KurAsync(HttpStatusCode kod, byte[]? icerik, string? disposition, string? json = null)
    {
        var h = new DosyaHandler(kod, icerik, disposition, json);
        var store = new BellekTokenStore();
        await store.YazAsync("tok");
        return (new KasaApiClient(new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") }, store), h);
    }

    [Fact]
    public async Task Islemler_csv_filtreyi_sorguya_koyar_ad_ve_icerigi_doner()
    {
        var (c, h) = await KurAsync(HttpStatusCode.OK, Csv,
            "attachment; filename=kasa-islemler-2026-09.csv; filename*=UTF-8''kasa-islemler-2026-09.csv");
        var d = await c.IslemlerCsvAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "PERAKENDE ŞUBE", null);

        Assert.Equal("kasa-islemler-2026-09.csv", d.DosyaAdi);
        Assert.Equal(Csv, d.Icerik);
        var uri = h.SonIstek!.RequestUri!;
        Assert.Equal("/api/disaaktar/islemler.csv", uri.AbsolutePath);
        Assert.Contains("baslangic=2026-09-01", uri.Query);
        Assert.Contains("bitis=2026-09-30", uri.Query);
        Assert.Contains("kanal=PERAKENDE%20%C5%9EUBE", uri.Query);
        Assert.DoesNotContain("limit", uri.Query);
        Assert.Equal("Bearer", h.SonIstek.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task Filtresiz_islemler_csv_sorgusuz_istenir()
    {
        var (c, h) = await KurAsync(HttpStatusCode.OK, Csv, "attachment; filename=kasa-islemler-tumu.csv");
        var d = await c.IslemlerCsvAsync();
        Assert.Equal("kasa-islemler-tumu.csv", d.DosyaAdi);
        Assert.Equal("", h.SonIstek!.RequestUri!.Query);
    }

    [Fact]
    public async Task Haftalik_ve_aylik_csv_yollari()
    {
        var (c, h) = await KurAsync(HttpStatusCode.OK, Csv, "attachment; filename=\"kasa-haftalik-2026-09-24.csv\"");
        Assert.Equal("kasa-haftalik-2026-09-24.csv", (await c.HaftalikCsvAsync()).DosyaAdi);
        Assert.Equal("/api/disaaktar/haftalik.csv", h.SonIstek!.RequestUri!.AbsolutePath);

        (c, h) = await KurAsync(HttpStatusCode.OK, Csv, "attachment; filename=kasa-aylik-2026-04.csv");
        var d = await c.AylikCsvAsync(2026, 4);
        Assert.Equal("kasa-aylik-2026-04.csv", d.DosyaAdi);
        Assert.Equal("/api/disaaktar/aylik.csv", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal("?yil=2026&ay=4", h.SonIstek.RequestUri.Query);
    }

    [Fact]
    public async Task Content_disposition_yoksa_varsayilan_ad()
    {
        var (c, _) = await KurAsync(HttpStatusCode.OK, Csv, null);
        Assert.Equal("kasa-aylik-2026-04.csv", (await c.AylikCsvAsync(2026, 4)).DosyaAdi);
        Assert.Equal("kasa-haftalik.csv", (await c.HaftalikCsvAsync()).DosyaAdi);
    }

    [Fact]
    public async Task Sunucu_hatasi_mesajiyla_firlatilir()
    {
        var (c, _) = await KurAsync(HttpStatusCode.BadRequest, null, null, """{"hata":"Ay 1 ile 12 arasında olmalı."}""");
        var ex = await Assert.ThrowsAsync<KasaApiException>(() => c.AylikCsvAsync(2026, 13));
        Assert.Equal(HttpStatusCode.BadRequest, ex.DurumKodu);
        Assert.Equal("Ay 1 ile 12 arasında olmalı.", ex.SunucuMesaji);
    }
}
