using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Kasa.ApiClient.Tests;

/// <summary>İstek başına süre sınırı: normal çağrılar kısa, dosya işlemleri (yükleme, indirme, yedek) uzun sürelidir;
/// indirilen gövde belleğe toplanmadan hedef akışa yazılır, çağıranın iptali zaman aşımından ayrı kalır.</summary>
public class ZamanAsimiTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => fn(r, c); }

    // Normal çağrı 150 ms'de kesilir; dosya işlemlerinin sınırı 10 sn.
    private static readonly KasaZamanAsimlari Kisa = new(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    private static KasaApiClient Client(HttpMessageHandler h, KasaZamanAsimlari? z = null, TimeSpan? httpSuresi = null)
        => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/"), Timeout = httpSuresi ?? Timeout.InfiniteTimeSpan }, new BellekTokenStore(), z ?? Kisa);
    private static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Dosya(HttpContent icerik, string ad)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = icerik };
        r.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = ad };
        r.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return r;
    }
    private const string BelgeJson = "{\"id\":8,\"alisId\":7,\"odemeId\":null,\"dosyaAdi\":\"dekont.pdf\",\"icerikTuru\":\"application/pdf\",\"boyut\":4,\"yuklendi\":\"2026-09-23T12:00:00Z\"}";
    private const string EkstreJson = "{\"id\":8,\"surum\":2,\"kaynak\":\"Banka\",\"banka\":\"QNB\",\"hesapAdi\":\"Ana\",\"kartId\":null,\"dosyaAdi\":\"hareket.pdf\",\"yuklendi\":\"2026-09-23T12:00:00Z\",\"uyarilar\":[],\"satirlar\":[],\"kayitlar\":[]}";

    /// <summary>İlk <paramref name="ilkParca"/> baytı hemen, kalanını <paramref name="bekle"/> bitince veren gövde; okuma iptali dinler.</summary>
    private sealed class ParcaliAkis(byte[] veri, int ilkParca, Func<CancellationToken, Task> bekle) : Stream
    {
        private int _konum;
        private bool _beklendi;
        public override async ValueTask<int> ReadAsync(Memory<byte> hedef, CancellationToken ct = default)
        {
            if (_konum >= ilkParca && !_beklendi) { _beklendi = true; await bekle(ct); }
            var sinir = _beklendi ? veri.Length : ilkParca;
            var n = Math.Min(hedef.Length, sinir - _konum);
            if (n <= 0) return 0;
            veri.AsSpan(_konum, n).CopyTo(hedef.Span);
            _konum += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] b, int o, int c, CancellationToken ct) => ReadAsync(b.AsMemory(o, c), ct).AsTask();
        public override int Read(byte[] b, int o, int c) => ReadAsync(b, o, c, default).GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _konum; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    /// <summary>İlk yazmayı haber veren hedef (gövde bitmeden diske yazıldığını sınamak için).</summary>
    private sealed class IzlenenHedef : MemoryStream
    {
        public TaskCompletionSource IlkYazma { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Write(byte[] b, int o, int c) { base.Write(b, o, c); IlkYazma.TrySetResult(); }
        public override Task WriteAsync(byte[] b, int o, int c, CancellationToken ct) { Write(b, o, c); return Task.CompletedTask; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct = default) { base.Write(b.Span); IlkYazma.TrySetResult(); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task Normal_istek_kisa_surede_zaman_asimina_duser_ve_anlasilir_ileti_verir()
    {
        var c = Client(new Handler(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(5), ct); return Json("{}"); }));

        var hata = await Assert.ThrowsAsync<TimeoutException>(() => c.PanelAsync());

        Assert.Contains("zamanında yanıt vermedi", hata.Message);
    }

    [Fact]
    public async Task HttpClient_suresi_dolarsa_da_zaman_asimi_olarak_bildirilir()
    {
        var c = Client(new Handler(async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(5), ct); return Json("{}"); }),
            new KasaZamanAsimlari(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAsync<TimeoutException>(() => c.PanelAsync());
    }

    [Fact]
    public async Task Dosya_islemleri_normal_sureyi_asan_sunucu_hazirligina_takilmaz()
    {
        var c = Client(new Handler(async (r, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct);   // normal sürenin (150 ms) dört katı
            var yol = r.RequestUri!.AbsolutePath;
            if (yol.EndsWith("/belgeler")) return Json(BelgeJson);
            if (yol.EndsWith("/yukle")) return Json(EkstreJson);
            return Dosya(new ByteArrayContent([1, 2, 3]), "dosya.bin");
        }));
        using MemoryStream yedek = new(), belge = new(), rapor = new(), ekstre = new();

        await Task.WhenAll(
            c.YedekIndirAsync(yedek),
            c.BelgeIndirAsync(8, belge),
            c.DisariAktarAsync(new(2016, 1, 1), new(2026, 9, 23), null, "xlsx", rapor),
            c.EkstreDosyaAsync(8, ekstre),
            c.BelgeYukleAsync(7, "dekont.pdf", "application/pdf", Encoding.UTF8.GetBytes("%PDF")),
            c.EkstreYukleAsync(Encoding.UTF8.GetBytes("%PDF"), "hareket.pdf", "Banka", "QNB", "Ana", null));

        foreach (var akis in new[] { yedek, belge, rapor, ekstre }) Assert.Equal(new byte[] { 1, 2, 3 }, akis.ToArray());
    }

    [Fact]
    public async Task Indirme_govdesi_normal_sureye_degil_indirme_suresine_tabidir()
    {
        var veri = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        HttpResponseMessage Yavas(TimeSpan bekleme) => Dosya(new StreamContent(new ParcaliAkis(veri, 100, ct => Task.Delay(bekleme, ct))), "belge.pdf");

        // Başlıklar hemen gelir, gövde 600 ms duraklar: normal süre (150 ms) gövde okumasını kesmez.
        var hedef = new MemoryStream();
        var bilgi = await Client(new Handler((_, _) => Task.FromResult(Yavas(TimeSpan.FromMilliseconds(600))))).BelgeIndirAsync(8, hedef);
        Assert.Equal(veri, hedef.ToArray());
        Assert.Equal(1000, bilgi.Boyut);

        // Gövde indirme süresini aşarsa aynı sayaç keser: akış sınırsız asılı kalmaz.
        var kisaIndirme = new KasaZamanAsimlari(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<TimeoutException>(() => Client(new Handler((_, _) => Task.FromResult(Yavas(TimeSpan.FromSeconds(30)))), kisaIndirme).BelgeIndirAsync(8, new MemoryStream()));
    }

    [Fact]
    public async Task Yedek_govdesi_tamami_okunmadan_hedefe_akis_olarak_yazilir()
    {
        var veri = new byte[3 * 1024 * 1024];
        new Random(42).NextBytes(veri);
        var hedef = new IzlenenHedef();
        var tamponlandi = false;
        HttpRequestMessage? istek = null;
        var c = Client(new Handler((r, _) =>
        {
            istek = r;
            // İlk parçadan sonra gövde, hedefe ilk yazma gelene kadar bekler; istemci yanıtı belleğe toplasaydı bu bekleme dolardı.
            return Task.FromResult(Dosya(new StreamContent(new ParcaliAkis(veri, 64 * 1024, async ct =>
            {
                var biten = await Task.WhenAny(hedef.IlkYazma.Task, Task.Delay(TimeSpan.FromSeconds(5), ct));
                if (biten != hedef.IlkYazma.Task) tamponlandi = true;
            })), "kasa-20260923-120000.zip"));
        }));

        var bilgi = await c.YedekIndirAsync(hedef);

        Assert.False(tamponlandi);
        Assert.Equal(HttpMethod.Post, istek!.Method);
        Assert.EndsWith("/api/yedek", istek.RequestUri!.AbsolutePath);
        Assert.Equal(veri.LongLength, bilgi.Boyut);
        Assert.Equal("kasa-20260923-120000.zip", bilgi.DosyaAdi);
        Assert.Equal(veri, hedef.ToArray());
    }

    [Fact]
    public async Task Iptal_dosya_islemlerine_yayilir_ve_zaman_asimi_sayilmaz()
    {
        var baslayan = 0;
        var gorulenIptal = 0;
        var tumuBasladi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var c = Client(new Handler(async (_, ct) =>
        {
            if (Interlocked.Increment(ref baslayan) == 5) tumuBasladi.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { Interlocked.Increment(ref gorulenIptal); throw; }
            return Json("{}");
        }));
        using var iptal = new CancellationTokenSource();
        var islemler = new Task[]
        {
            c.YedekIndirAsync(new MemoryStream(), iptal.Token),
            c.BelgeIndirAsync(8, new MemoryStream(), iptal.Token),
            c.DisariAktarAsync(new(2026, 9, 1), new(2026, 9, 23), null, "csv", new MemoryStream(), iptal.Token),
            c.EkstreDosyaAsync(8, new MemoryStream(), iptal.Token),
            c.BelgeYukleAsync(7, "dekont.pdf", "application/pdf", [1], null, iptal.Token),
        };
        await tumuBasladi.Task;

        iptal.Cancel();

        foreach (var islem in islemler)
        {
            var hata = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => islem);
            Assert.IsNotType<TimeoutException>(hata.InnerException);
        }
        Assert.Equal(5, gorulenIptal);
    }

    [Fact]
    public async Task Belge_yukleme_bos_ve_10_mb_ustu_dosyayi_sunucuya_gondermez()
    {
        var cagri = 0;
        var c = Client(new Handler((_, _) => { cagri++; return Task.FromResult(Json(BelgeJson)); }));

        await Assert.ThrowsAsync<ArgumentException>(() => c.BelgeYukleAsync(7, "bos.pdf", "application/pdf", []));
        await Assert.ThrowsAsync<ArgumentException>(() => c.BelgeYukleAsync(7, "buyuk.pdf", "application/pdf", new byte[10 * 1024 * 1024 + 1]));
        Assert.Equal(0, cagri);

        await c.BelgeYukleAsync(7, "sinir.pdf", "application/pdf", new byte[10 * 1024 * 1024]);
        Assert.Equal(1, cagri);
    }

    [Fact]
    public void Varsayilan_sureler_normal_istegi_kisa_dosya_islemlerini_uzun_tutar()
    {
        var z = KasaZamanAsimlari.Varsayilanlar;
        Assert.Equal(TimeSpan.FromSeconds(15), z.Varsayilan);
        Assert.True(z.Yukleme >= TimeSpan.FromMinutes(2));
        Assert.True(z.Indirme >= TimeSpan.FromMinutes(5));
        Assert.True(z.Yedek >= z.Indirme);
    }
}
