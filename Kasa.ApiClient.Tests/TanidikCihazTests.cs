using System.Net;
using System.Security.Cryptography;

namespace Kasa.ApiClient.Tests;

/// <summary>
/// Masaüstü tanıdık cihaz belirteci: giriş ve oturum doğrulaması (/me) yanıtındaki 'cihaz', şifre değişikliği ve
/// kurtarmada X-Kasa-Cihaz yanıt başlığı rol başına güvenli depoya yazılır; sonraki girişlerde saklı belirteçlerin
/// hepsi tek X-Kasa-Cihaz başlığıyla gönderilir (sunucu denenen hedefe ait olanı kabul eder). Oturumdan bağımsızdır:
/// çıkış ve oturum sonu yalnız JWT'yi siler. Belirteç isteğe bağlıdır: depo hatası girişi engellemez.
/// </summary>
public class TanidikCihazTests
{
    private static (KasaApiClient Client, SahteHandler Handler, BellekTokenStore Store) Kur()
    {
        var handler = new SahteHandler();
        var store = new BellekTokenStore();
        return (new KasaApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") }, store), handler, store);
    }

    private static string? CihazBasligi(HttpRequestMessage istek)
        => istek.Headers.TryGetValues("X-Kasa-Cihaz", out var degerler) ? Assert.Single(degerler) : null;

    private static HttpResponseMessage BelirtecliBosYanit(string belirtec)
    {
        var yanit = new HttpResponseMessage(HttpStatusCode.NoContent);
        yanit.Headers.Add("X-Kasa-Cihaz", belirtec);
        return yanit;
    }

    /// <summary>Tanıdık cihaz kaydı okunamayan ya da yazılamayan güvenli depo (ör. Windows profili veya DPAPI anahtarı
    /// değişti); oturum token'ı sorunsuz çalışır.</summary>
    private sealed class KirikCihazDeposu(bool okuma, bool yazma) : ITokenStore
    {
        private readonly BellekTokenStore _ic = new();
        public int YazmaDenemesi { get; private set; }
        public Task<string?> OkuAsync() => _ic.OkuAsync();
        public Task YazAsync(string token) => _ic.YazAsync(token);
        public Task TemizleAsync() => _ic.TemizleAsync();
        public Task<string?> CihazOkuAsync(string rol) => okuma ? throw new CryptographicException("Kayıt çözülemedi.") : _ic.CihazOkuAsync(rol);
        public Task CihazYazAsync(string rol, string belirtec)
        {
            YazmaDenemesi++;
            return yazma ? throw new CryptographicException("Kayıt yazılamadı.") : _ic.CihazYazAsync(rol, belirtec);
        }
    }

    [Fact]
    public async Task Bellek_deposu_cihaz_belirteclerini_oturumdan_ve_rol_basina_ayri_tutar()
    {
        var store = new BellekTokenStore();
        Assert.Null(await store.CihazOkuAsync("editor"));
        await store.YazAsync("jwt");
        await store.CihazYazAsync("editor", "c1.editor");
        await store.CihazYazAsync("viewer", "c1.izleyici");
        await store.TemizleAsync();
        Assert.Null(await store.OkuAsync());
        Assert.Equal("c1.editor", await store.CihazOkuAsync("editor"));
        Assert.Equal("c1.izleyici", await store.CihazOkuAsync("viewer"));
        Assert.Null(await store.CihazOkuAsync("alici"));
    }

    [Fact]
    public async Task Ilk_giriste_baslik_gitmez_yanittaki_belirtec_rolune_saklanir()
    {
        var (client, handler, store) = Kur();
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-1","cihaz":"c1.100.kimlik.imza"}""");

        var yanit = await client.LoginAsync("editor", "sifre");

        Assert.Null(CihazBasligi(handler.SonIstek!));
        Assert.Equal("c1.100.kimlik.imza", yanit.Cihaz);
        Assert.Equal("c1.100.kimlik.imza", await store.CihazOkuAsync("editor"));
        Assert.Null(await store.CihazOkuAsync("viewer"));
        Assert.Equal("jwt-1", await store.OkuAsync());
    }

    [Fact]
    public async Task Sonraki_giris_sakli_belirteclerin_hepsini_tek_baslikta_gonderir_yalniz_kendi_rolunu_yeniler()
    {
        var (client, handler, store) = Kur();
        await store.CihazYazAsync("editor", "c1.editor");
        await store.CihazYazAsync("alici", "c1.alici");
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"alici","token":"jwt-2","cihaz":"c1.alici-yeni"}""");

        await client.LoginAsync("alici-1", "sifre");

        // İstemci adın hangi role düştüğünü bilmez; sunucu yalnız denenen hedefe ait belirteci kabul eder.
        Assert.Equal("c1.editor,c1.alici", CihazBasligi(handler.SonIstek!));
        Assert.Equal("c1.editor", await store.CihazOkuAsync("editor"));
        Assert.Equal("c1.alici-yeni", await store.CihazOkuAsync("alici"));
    }

    [Fact]
    public async Task Belirtecsiz_yanit_ve_basarisiz_giris_saklanan_belirteci_silmez()
    {
        var (client, handler, store) = Kur();
        await store.CihazYazAsync("viewer", "c1.saklanan");
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"viewer","token":"jwt-3"}""")
               .Kuyrukla(HttpStatusCode.Unauthorized)
               .Kuyrukla(HttpStatusCode.TooManyRequests, """{"hata":"Çok fazla deneme yapıldı. 15 dakika sonra yeniden deneyin."}""");

        Assert.Null((await client.LoginAsync(null, "sifre")).Cihaz);
        await Assert.ThrowsAsync<KasaApiException>(() => client.LoginAsync("editor", "yanlis"));
        var red = await Assert.ThrowsAsync<KasaApiException>(() => client.LoginAsync("editor", "sifre"));

        Assert.Equal(HttpStatusCode.TooManyRequests, red.DurumKodu);
        Assert.Equal("Çok fazla deneme yapıldı. 15 dakika sonra yeniden deneyin.", red.Message);
        Assert.Equal("c1.saklanan", CihazBasligi(handler.SonIstek!));
        Assert.Equal("c1.saklanan", await store.CihazOkuAsync("viewer"));
    }

    [Fact]
    public async Task Cikis_ve_oturum_sonu_cihaz_belirtecini_silmez_diger_isteklere_eklenmez()
    {
        var (client, handler, store) = Kur();
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-4","cihaz":"c1.cihaz"}""")
               .Kuyrukla(HttpStatusCode.Unauthorized)
               .Kuyrukla(HttpStatusCode.NoContent);
        await client.LoginAsync("editor", "sifre");

        await Assert.ThrowsAsync<KasaApiException>(client.PanelAsync);
        Assert.Null(CihazBasligi(handler.SonIstek!));
        Assert.Null(await store.OkuAsync());
        await client.CikisAsync();

        Assert.Equal("c1.cihaz", await store.CihazOkuAsync("editor"));
    }

    [Fact]
    public async Task Oturum_dogrulamasi_yanitindaki_belirtec_rolune_saklanir()
    {
        var (client, handler, store) = Kur();
        await store.YazAsync("jwt-5");
        await store.CihazYazAsync("alici", "c1.eski");
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"alici","cihaz":"c1.me"}""");

        Assert.Equal("alici", await client.BenKimAsync());

        Assert.Equal("jwt-5", handler.SonIstek!.Headers.Authorization!.Parameter);
        Assert.Null(CihazBasligi(handler.SonIstek));
        Assert.Equal("c1.me", await store.CihazOkuAsync("alici"));
    }

    [Fact]
    public async Task Belirtecsiz_oturum_dogrulamasi_saklanani_silmez()
    {
        var (client, handler, store) = Kur();
        await store.YazAsync("jwt-6");
        await store.CihazYazAsync("editor", "c1.saklanan");
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor"}""");

        Assert.Equal("editor", await client.BenKimAsync());
        Assert.Equal("c1.saklanan", await store.CihazOkuAsync("editor"));
    }

    [Fact]
    public async Task Sifre_degisikligi_ve_kurtarma_yanit_basligindaki_belirteci_editor_icin_saklar()
    {
        var (client, handler, store) = Kur();
        await store.YazAsync("jwt-7");
        await store.CihazYazAsync("editor", "c1.eski-damga");
        handler.Kuyrukla(BelirtecliBosYanit("c1.yeni-damga")).Kuyrukla(BelirtecliBosYanit("c1.kurtarilan"));

        await client.SifreDegistirAsync(new("eski-sifre", "yepyeni-sifre-123"));
        Assert.Null(await store.OkuAsync());
        Assert.Equal("c1.yeni-damga", await store.CihazOkuAsync("editor"));

        await client.SifreKurtarAsync(new("editor", "KOD", "kurtarilan-sifre-123"));
        Assert.Equal("c1.kurtarilan", await store.CihazOkuAsync("editor"));
    }

    [Fact]
    public async Task Depo_okuma_hatasi_girisi_engellemez_baslik_gitmez()
    {
        var handler = new SahteHandler().Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-8","cihaz":"c1.yeni"}""");
        var store = new KirikCihazDeposu(okuma: true, yazma: false);
        var client = new KasaApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") }, store);

        var yanit = await client.LoginAsync("editor", "sifre");

        Assert.Equal("editor", yanit.Rol);
        Assert.Null(CihazBasligi(handler.SonIstek!));
        Assert.Equal("jwt-8", await store.OkuAsync());
        Assert.Equal(1, store.YazmaDenemesi);
    }

    [Fact]
    public async Task Depo_yazma_hatasi_giris_oturum_dogrulamasi_ve_sifre_degisikligini_bozmaz()
    {
        var handler = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-9","cihaz":"c1.yeni"}""")
            .Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","cihaz":"c1.me"}""")
            .Kuyrukla(BelirtecliBosYanit("c1.sifre"));
        var store = new KirikCihazDeposu(okuma: false, yazma: true);
        var client = new KasaApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") }, store);
        var nedenler = new List<OturumSonuNedeni?>();
        client.OturumSonlandi += (_, e) => nedenler.Add((e as OturumSonlandiEventArgs)?.Neden);

        Assert.Equal("editor", (await client.LoginAsync("editor", "sifre")).Rol);
        Assert.Equal("jwt-9", await store.OkuAsync());
        Assert.Equal("editor", await client.BenKimAsync());
        await client.SifreDegistirAsync(new("eski-sifre", "yepyeni-sifre-123"));

        Assert.Equal(3, store.YazmaDenemesi);
        Assert.Equal([OturumSonuNedeni.SifreDegisti], nedenler);
        Assert.Null(await store.OkuAsync());
    }
}
