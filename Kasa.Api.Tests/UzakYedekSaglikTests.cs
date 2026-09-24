using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Kasa.Api.Tests;

/// <summary>/health "uzakYedek": sunucu dışı yedek konteynerinin durum dosyası özetlenir.</summary>
public class UzakYedekSaglikTests : IClassFixture<UzakYedekSaglikTests.DurumDosyaliFactory>
{
    /// <summary>Durum dosyası yolu yapılandırılmış uygulama (dosyanın içeriğini testler yazar).</summary>
    public class DurumDosyaliFactory : KasaWebFactory
    {
        public string Dosya { get; } = Path.Combine(Path.GetTempPath(), $"kasa-uzak-durum-{Guid.NewGuid():N}.json");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kasa:UzakYedekDurumDosyasi"] = Dosya,
            }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && File.Exists(Dosya)) File.Delete(Dosya);
        }
    }

    private readonly DurumDosyaliFactory _factory;
    public UzakYedekSaglikTests(DurumDosyaliFactory factory) => _factory = factory;

    private static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private async Task<(HttpStatusCode Kod, JsonElement Govde)> Saglik(HttpClient c)
    {
        var r = await c.GetAsync("/health");
        return (r.StatusCode, await r.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Fact]
    public async Task Durum_dosyasi_tanimli_degilse_yapilandirilmadi_mevcut_alanlar_ayni()
    {
        using var f = new KasaWebFactory();
        var (kod, j) = await Saglik(f.CreateClient());

        Assert.Equal(HttpStatusCode.OK, kod);
        Assert.Equal("ok", j.GetProperty("durum").GetString());
        foreach (var alan in new[] { "surum", "yedek", "sonYedekYasSaat", "yedekHatasi", "diskBosMb" })
            Assert.True(j.TryGetProperty(alan, out _), alan);
        var uzak = j.GetProperty("uzakYedek");
        Assert.Equal("yapılandırılmadı", uzak.GetProperty("durum").GetString());
        Assert.Single(uzak.EnumerateObject());
    }

    [Fact]
    public async Task Dosya_yoksa_yapilandirilmadi()
    {
        if (File.Exists(_factory.Dosya)) File.Delete(_factory.Dosya);
        var (kod, j) = await Saglik(_factory.CreateClient());
        Assert.Equal(HttpStatusCode.OK, kod);
        Assert.Equal("yapılandırılmadı", j.GetProperty("uzakYedek").GetProperty("durum").GetString());
    }

    [Fact]
    public async Task Basarili_gonderim_ve_dogrulama_ok_ham_hata_ayrintisi_gosterilmez()
    {
        var simdi = DateTimeOffset.UtcNow;
        File.WriteAllText(_factory.Dosya, $$"""
            {"sonDeneme":"{{Utc(simdi.AddHours(-2))}}","sonBasari":"{{Utc(simdi.AddHours(-2))}}",
             "dosya":"kasa-2026-09-24.db.gz","boyutBayt":123456,"yerelYedek":"kasa-2026-09-24.db",
             "hata":null,"hataAyrinti":null,"uyari":null,
             "dogrulamaZamani":"{{Utc(simdi.AddDays(-3))}}","dogrulamaSonucu":"ok",
             "dogrulamaDosyasi":"kasa-2026-09-21.db.gz","dogrulamaHatasi":null,"dogrulamaIslemSayisi":42}
            """);

        var (kod, j) = await Saglik(_factory.CreateClient());

        Assert.Equal(HttpStatusCode.OK, kod);
        Assert.Equal("ok", j.GetProperty("durum").GetString());
        var uzak = j.GetProperty("uzakYedek");
        Assert.Equal("ok", uzak.GetProperty("durum").GetString());
        Assert.Equal("kasa-2026-09-24.db.gz", uzak.GetProperty("dosya").GetString());
        Assert.Equal(123456, uzak.GetProperty("boyutBayt").GetInt64());
        Assert.InRange(uzak.GetProperty("sonBasariYasSaat").GetDouble(), 1.9, 2.1);
        Assert.Equal("ok", uzak.GetProperty("dogrulamaSonucu").GetString());
        Assert.Equal("kasa-2026-09-21.db.gz", uzak.GetProperty("dogrulamaDosyasi").GetString());
        Assert.False(uzak.TryGetProperty("hata", out _));
        Assert.False(uzak.TryGetProperty("hataAyrinti", out _));
    }

    [Fact]
    public async Task Son_deneme_hataliysa_hata_ama_saglik_200_ve_rclone_ciktisi_gizli()
    {
        var simdi = DateTimeOffset.UtcNow;
        File.WriteAllText(_factory.Dosya, $$"""
            {"sonDeneme":"{{Utc(simdi.AddHours(-1))}}","sonBasari":"{{Utc(simdi.AddDays(-1))}}",
             "dosya":"kasa-2026-09-23.db.gz","boyutBayt":100,
             "hata":"Yedek gönderilemedi (gunluk/kasa-2026-09-24.db.gz).",
             "hataAyrinti":"Failed to copyto: dial tcp 10.0.0.9:22: connect: connection refused"}
            """);

        var (kod, j) = await Saglik(_factory.CreateClient());

        Assert.Equal(HttpStatusCode.OK, kod);
        Assert.Equal("ok", j.GetProperty("durum").GetString());
        var uzak = j.GetProperty("uzakYedek");
        Assert.Equal("hata", uzak.GetProperty("durum").GetString());
        Assert.Equal("Yedek gönderilemedi (gunluk/kasa-2026-09-24.db.gz).", uzak.GetProperty("hata").GetString());
        Assert.DoesNotContain("10.0.0.9", uzak.GetRawText());
    }

    [Fact]
    public async Task Dogrulama_hataliysa_durum_hata()
    {
        var simdi = DateTimeOffset.UtcNow;
        File.WriteAllText(_factory.Dosya, $$"""
            {"sonBasari":"{{Utc(simdi.AddHours(-3))}}","hata":null,
             "dogrulamaZamani":"{{Utc(simdi.AddHours(-1))}}","dogrulamaSonucu":"hata",
             "dogrulamaHatasi":"Bütünlük denetimi başarısız."}
            """);

        var uzak = (await Saglik(_factory.CreateClient())).Govde.GetProperty("uzakYedek");

        Assert.Equal("hata", uzak.GetProperty("durum").GetString());
        Assert.Equal("Bütünlük denetimi başarısız.", uzak.GetProperty("dogrulamaHatasi").GetString());
    }

    [Fact]
    public async Task Uzun_suredir_basari_yoksa_eski()
    {
        File.WriteAllText(_factory.Dosya, $$"""{"sonBasari":"{{Utc(DateTimeOffset.UtcNow.AddHours(-50))}}","hata":null}""");
        var uzak = (await Saglik(_factory.CreateClient())).Govde.GetProperty("uzakYedek");
        Assert.Equal("eski", uzak.GetProperty("durum").GetString());
    }

    [Fact]
    public async Task Bozuk_durum_dosyasi_okunamadi_saglik_yine_200()
    {
        File.WriteAllText(_factory.Dosya, "{ bozuk json");
        var (kod, j) = await Saglik(_factory.CreateClient());
        Assert.Equal(HttpStatusCode.OK, kod);
        Assert.Equal("ok", j.GetProperty("durum").GetString());
        Assert.Equal("okunamadı", j.GetProperty("uzakYedek").GetProperty("durum").GetString());
    }

    [Fact]
    public void Ozet_saf_fonksiyon_yol_bos_ya_da_dosya_yoksa_yapilandirilmadi()
    {
        Assert.Equal(UzakYedekDurumu.Yapilandirilmadi, UzakYedekDurumu.Oku(null, DateTimeOffset.UtcNow).Durum);
        Assert.Equal(UzakYedekDurumu.Yapilandirilmadi, UzakYedekDurumu.Oku("  ", DateTimeOffset.UtcNow).Durum);
        Assert.Equal(UzakYedekDurumu.Yapilandirilmadi,
            UzakYedekDurumu.Oku(Path.Combine(Path.GetTempPath(), $"yok-{Guid.NewGuid():N}.json"), DateTimeOffset.UtcNow).Durum);
    }
}
