using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>Şifre akışı: yanlış mevcut şifre oturum sonu değildir; izleyici şifresinin alt sınırı.</summary>
public class SifreAkisiTests
{
    private static string[] AlanHatalari(JsonElement govde, string alan)
        => govde.GetProperty("errors").GetProperty(alan).EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Theory]
    [InlineData("/api/auth/sifre")]
    [InlineData("/api/auth/kurtarma-kodu")]
    public async Task Yanlis_mevcut_sifre_400_alan_hatasi_doner_ve_oturum_surer(string yol)
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        var yanit = await editor.PostAsJsonAsync(yol, new { mevcutSifre = "yanlis", yeniSifre = "yepyeni-sifre-123" });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Equal(["Mevcut şifre hatalı."], AlanHatalari(await yanit.Content.ReadFromJsonAsync<JsonElement>(), "mevcutSifre"));
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("kisa")]
    [InlineData("on-bir-harf")]          // 11 karakter
    [InlineData("            ")]         // 12 boşluk
    public async Task Izleyici_sifresi_12_karakterden_kisa_veya_bos_ise_reddedilir(string sifre)
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        var yanit = await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = sifre });

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        Assert.Equal(["İzleyici şifresi 12–1024 karakter olmalıdır."], AlanHatalari(await yanit.Content.ReadFromJsonAsync<JsonElement>(), "yeniSifre"));
        using var scope = f.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetRequiredService<KasaDbContext>().Ayarlar.Single().IzleyiciSifreHash);
    }

    [Fact]
    public async Task Izleyici_sifresi_12_karakterle_kaydedilir_ve_giris_yapilir()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "on-iki-harf!" })).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        var giris = await izleyici.PostAsJsonAsync("/api/auth/login", new { sifre = "on-iki-harf!" });
        Assert.Equal(HttpStatusCode.OK, giris.StatusCode);
        Assert.Equal("viewer", (await giris.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rol").GetString());
    }

    [Fact]
    public async Task Mevcut_kisa_izleyici_sifresiyle_giris_surer()
    {
        await using var f = new KasaWebFactory();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Ayarlar.Single().IzleyiciSifreHash = SifreHasher.Hashle("eski1234");
            db.SaveChanges();
        }
        using var izleyici = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.PostAsJsonAsync("/api/auth/login", new { sifre = "eski1234" })).StatusCode);
    }

    [Fact]
    public async Task Kurali_karsilamayan_mevcut_izleyici_sifresi_giriste_fark_edilir_ve_ayarlarda_isaretlenir()
    {
        var loglar = new UyariToplayici();
        await using var f = new VekilVeHizSiniriTests.VekilFabrikasi(loglar: loglar);
        using var editor = await f.EditorClientAsync();
        IzleyiciHashiniYaz(f, "eski1234");
        // Hash uzunluğu saklamaz: izleyici girene kadar bilinmez, işaretlenmez.
        Assert.False(await IzleyiciSifreKisa(editor));

        using var izleyici = f.CreateClient();
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await izleyici.PostAsJsonAsync("/api/auth/login", new { sifre = "eski1234" })).StatusCode);
        Assert.True(await IzleyiciSifreKisa(editor));
        Assert.False(await IzleyiciSifreKisa(izleyici));          // işaret yalnız editöre
        Assert.Single(loglar.Uyarilar, u => u.Contains("İzleyici şifresi") && u.Contains("12"));

        // Kurala uygun yeni şifre işareti kaldırır (hash değişir).
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "yeni-izleyici-sifresi" })).EnsureSuccessStatusCode();
        Assert.False(await IzleyiciSifreKisa(editor));
        Assert.Equal(HttpStatusCode.OK, (await izleyici.PostAsJsonAsync("/api/auth/login", new { sifre = "yeni-izleyici-sifresi" })).StatusCode);
        Assert.False(await IzleyiciSifreKisa(editor));
    }

    [Fact]
    public async Task Kurala_uyan_eski_izleyici_sifresi_isaretlenmez()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        IzleyiciHashiniYaz(f, "eski-ama-uzun-sifre");
        using var izleyici = f.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.PostAsJsonAsync("/api/auth/login", new { sifre = "eski-ama-uzun-sifre" })).StatusCode);
        Assert.False(await IzleyiciSifreKisa(editor));
    }

    private static void IzleyiciHashiniYaz(KasaWebFactory f, string sifre)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        db.Ayarlar.Single().IzleyiciSifreHash = SifreHasher.Hashle(sifre);
        db.SaveChanges();
    }

    private static async Task<bool> IzleyiciSifreKisa(HttpClient editor)
        => (await editor.GetFromJsonAsync<JsonElement>("/api/ayarlar")).GetProperty("izleyiciSifreKisa").GetBoolean();

    [Fact]
    public async Task Editor_kullanici_adiyla_yalniz_editor_sifresi_denenir()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi" })).EnsureSuccessStatusCode();
        using var c = f.CreateClient();
        var yanit = await c.PostAsJsonAsync("/api/auth/login", new { kullanici = "editor", sifre = "izleyici-sifresi" });
        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
        Assert.Equal("Kullanıcı adı veya şifre hatalı.", (await yanit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString());
    }
}
