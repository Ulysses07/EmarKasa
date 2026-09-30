using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class IsletimTests
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun;

    [Fact]
    public async Task Hesap_gelirine_bagli_kanal_silinemez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        var date = f.Bugun;
        var channelId = SeedLegacyAccountIncome(f, date);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/kanallar/{channelId}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Contains((await c.GetFromJsonAsync<KanalEntity[]>("/api/kanallar", cancellationToken: TestContext.Current.CancellationToken))!, k => k.Id == channelId);
    }

    [Fact]
    public async Task Hesap_geliri_kaydedilince_takip_baslangici_ileriye_alinamaz()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        var date = f.Bugun;
        SeedLegacyAccountIncome(f, date);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = date.AddDays(1), kasaAcilisDevri = 0m }, cancellationToken: ct)).StatusCode);
    }

    private static int SeedLegacyAccountIncome(KasaWebFactory factory, DateOnly date)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var account = new HesapEntity { Ad = "Eski test hesabı", Tur = "Kasa", AcilisTarihi = date };
        db.Hesaplar.Add(account);
        db.SaveChanges();
        var channel = db.Kanallar.First();
        db.HesapHareketler.Add(new HesapHareketEntity { HesapId = account.Id, KanalId = channel.Id, Tarih = date, Tutar = 100m, Aciklama = "Korunan eski bağlantı" });
        db.SaveChanges();
        return channel.Id;
    }

    [Fact]
    public async Task Cikis_bos_204_doner_ve_tarayıci_oturumu_kapanir()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Sifre_degisiminde_eski_oturum_iptal_olur_ve_config_parolasi_geri_acilmaz()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        using var old = await f.EditorClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "yanlis", yeniSifre = "yeni-parola-12345" }, cancellationToken: ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/auth/sifre", new { mevcutSifre = "kasa123", yeniSifre = "yeni-parola-12345" }, cancellationToken: ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/auth/me", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "kasa123" }, cancellationToken: ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "yeni-parola-12345" }, cancellationToken: ct)).StatusCode);
    }

    [Fact]
    public async Task Kurtarma_kodu_tek_kullanimlidir_ve_hash_olarak_saklanir()
    {
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        var r = await c.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "kasa123" }, cancellationToken: TestContext.Current.CancellationToken);
        r.EnsureSuccessStatusCode();
        var code = (await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("kod").GetString()!;
        using (var scope = f.Services.CreateScope())
        {
            var stored = scope.ServiceProvider.GetRequiredService<KasaDbContext>().EditorGuvenlik.Single();
            Assert.NotEqual(code, stored.KurtarmaHash);
        }
        using var anon = f.CreateClient();
        var body = new { kullanici = "editor", kod = code, yeniSifre = "kurtarilan-sifre-123" };
        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsJsonAsync("/api/auth/kurtar", body, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/kurtar", body, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Baska_kaynak_tarayıci_yazmasi_reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new KasaWebFactory();
        using var c = await f.EditorClientAsync();
        c.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        c.DefaultRequestHeaders.Add("X-Kasa-Request", "1");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "kasa123" }, cancellationToken: ct)).StatusCode);
        c.DefaultRequestHeaders.Remove("Origin");
        c.DefaultRequestHeaders.Add("Origin", "http://localhost");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/auth/kurtarma-kodu", new { mevcutSifre = "kasa123" }, cancellationToken: ct)).StatusCode);
    }

    [Fact]
    public async Task Belge_yalniz_sahibe_acilir_yanlis_dosya_reddedilir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var editor = await f.EditorClientAsync();
        (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("buyer", "Alıcı", "buyer12345"), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var buyer = f.CreateClient();
        (await buyer.PostAsJsonAsync("/api/auth/login", new { kullanici = "buyer", sifre = "buyer12345" }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var draft = await editor.PostAsJsonAsync("/api/alis", new AlisYaz(0, f.Bugun, "Firma", null, []), cancellationToken: TestContext.Current.CancellationToken);
        draft.EnsureSuccessStatusCode();
        var alis = (await draft.Content.ReadFromJsonAsync<AlisDto>(cancellationToken: TestContext.Current.CancellationToken))!;
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("%PDF-1.4 test"u8.ToArray()), "dosya", "fatura.pdf");
        var r = await editor.PostAsync($"/api/alis/{alis.Id}/belgeler", form, TestContext.Current.CancellationToken);
        r.EnsureSuccessStatusCode();
        var belge = (await r.Content.ReadFromJsonAsync<BelgeDto>(cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.GetAsync($"/api/belgeler/{belge.Id}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.DeleteAsync($"/api/belgeler/{belge.Id}", TestContext.Current.CancellationToken)).StatusCode);
        var download = await editor.GetAsync($"/api/belgeler/{belge.Id}", TestContext.Current.CancellationToken);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("%PDF-1.4 test", await download.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var invalid = new MultipartFormDataContent();
        invalid.Add(new ByteArrayContent("<script>alert(1)</script>"u8.ToArray()), "dosya", "fake.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsync($"/api/alis/{alis.Id}/belgeler", invalid, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Excel_ve_CSV_formul_calistirmaz_html_metni_kacar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        var date = f.Bugun;
        (await c.PostAsJsonAsync("/api/islemler",
            new { tarih = date, cari = "=2+2", tutarTl = 12.34m, kanal = "MEZAT", tip = "Cari", not = "<script>bad</script>" }, cancellationToken: ct)).EnsureSuccessStatusCode();
        string url = $"/api/disari-aktar?baslangic={date:yyyy-MM-dd}&bitis={date:yyyy-MM-dd}&bicim=";
        var csv = await c.GetStringAsync(url + "csv", ct);
        Assert.Contains("'=2+2", csv);
        Assert.Contains("12,34", csv);
        var html = await c.GetStringAsync(url + "html", ct);
        Assert.DoesNotContain("<script>bad", html);
        Assert.Contains("&lt;script&gt;", html);
        using var xlsx = new ZipArchive(new MemoryStream(await c.GetByteArrayAsync(url + "xlsx", ct)));
        using var reader = new StreamReader(xlsx.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = await reader.ReadToEndAsync(ct);
        Assert.Contains("=2+2", sheet);
        Assert.DoesNotContain("<f>", sheet);
        Assert.Contains("12.34", sheet);
    }

    [Fact]
    public async Task Yedek_ayri_veritabanina_geri_acilir_belgeler_ve_kayitlar_korunur()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new YedekliFabrika { Saat = new SabitSaat(Bugun) };
        using var c = await f.EditorClientAsync();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var a = new AlisEntity { Tarih = f.Bugun, Tedarikci = "Yedek firma", Durum = "Taslak" };
            db.Alislar.Add(a);
            db.SaveChanges();
            db.Belgeler.Add(new BelgeEntity { AlisId = a.Id, DosyaAdi = "test.pdf", IcerikTuru = "application/pdf", Boyut = 5, IcerikOzeti = TestBelgeDeposu.Yaz(scope.ServiceProvider, "%PDF-"u8.ToArray()), Yuklendi = f.Saat!.GetUtcNow() });
            db.SaveChanges();
        }
        var r = await c.PostAsync("/api/yedek", null, ct);
        r.EnsureSuccessStatusCode();
        using var zip = new ZipArchive(new MemoryStream(await r.Content.ReadAsByteArrayAsync(ct)));
        Assert.NotNull(zip.GetEntry(".kasa-push-keys.json"));
        using (var keyStream = zip.GetEntry(".kasa-push-keys.json")!.Open())
        using (var reader = new StreamReader(keyStream))
        {
            var backedUpKeys = await reader.ReadToEndAsync(ct);
            Assert.True(backedUpKeys == await File.ReadAllTextAsync(Path.Combine(f.DirectoryPath, ".kasa-push-keys.json"), ct), "Bildirim kimliği yedekte korunmalı.");
        }
        var restored = Path.Combine(f.DirectoryPath, "restored.db");
        zip.GetEntry("kasa.db")!.ExtractToFile(restored);
        YedekServisi.Dogrula(restored);
        using var conn = new SqliteConnection($"Data Source={restored};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Belgeler WHERE DosyaAdi='test.pdf';";
        Assert.Equal(1L, cmd.ExecuteScalar());
        var state = await c.GetFromJsonAsync<YedekDurumu>("/api/yedek/durum", cancellationToken: ct);
        Assert.NotNull(state!.SonDogrulama);
        Assert.Null(state.Hata);
    }

    private sealed class YedekliFabrika : KasaWebFactory
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "kasa-backup-test-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Yedek:Dizin"] = DirectoryPath,
                ["Yedek:Etkin"] = "false",
                ["Bildirim:PushEtkin"] = "true",
                ["Bildirim:WorkerEtkin"] = "false",
                ["Bildirim:AnahtarDosyasi"] = Path.Combine(DirectoryPath, ".kasa-push-keys.json")
            }));
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, true);
        }
    }
}
