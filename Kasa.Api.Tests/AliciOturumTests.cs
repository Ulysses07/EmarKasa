using System.Net;
using System.Net.Http.Json;
using Kasa.Api;

namespace Kasa.Api.Tests;

public class AliciOturumTests
{
    [Fact]
    public async Task Alici_finansal_bilgilere_ve_editor_islemlerine_erisemez()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        var hesap = await Ekle(editor);
        using var alici = f.CreateClient();
        var login = await alici.PostAsJsonAsync("/api/auth/login", new { kullanici = "ALICI-1", sifre = "alici-sifre-1" });
        login.EnsureSuccessStatusCode();
        Assert.Equal("alici", (await login.Content.ReadFromJsonAsync<Login>())!.Rol);
        foreach (var yol in new[] { "/api/kanallar", "/api/islemler", "/api/krediler", "/api/kredikartlari",
            "/api/gelenler", "/api/kartodemeler", "/api/ayarlar", "/api/donemler", "/api/rapor/panel", "/api/rapor/haftalik", "/api/rapor/aylik?yil=2026&ay=9", "/api/alicilar" })
            Assert.Equal(HttpStatusCode.Forbidden, (await alici.GetAsync(yol)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alici.GetAsync("/api/alis")).StatusCode);
        var kanallar = await alici.GetStringAsync("/api/alis/kanallar");
        Assert.DoesNotContain("acilisDevri", kanallar, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await alici.PutAsJsonAsync($"/api/alicilar/{hesap.Id}",
            new AliciYaz("alici-1", "Kendi hesabım", "baska-sifre"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alici.PostAsJsonAsync("/api/alis/1/odemeler", new
        { surum = 1, istekId = Guid.NewGuid(), tarih = DateOnly.FromDateTime(DateTime.Today), tutar = 1m })).StatusCode);
    }

    [Fact]
    public async Task Sifre_degisimi_ve_pasife_alma_eski_oturumu_kalici_iptal_eder()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        var hesap = await Ekle(editor);
        using var alici = f.CreateClient();
        (await alici.PostAsJsonAsync("/api/auth/login", new { kullanici = "alici-1", sifre = "alici-sifre-1" })).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync($"/api/alicilar/{hesap.Id}", new AliciYaz("alici-1", "Alıcı", null, false))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await alici.GetAsync("/api/alis")).StatusCode);
        (await editor.PutAsJsonAsync($"/api/alicilar/{hesap.Id}", new AliciYaz("alici-1", "Alıcı", null, true))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await alici.GetAsync("/api/alis")).StatusCode);
        (await alici.PostAsJsonAsync("/api/auth/login", new { kullanici = "alici-1", sifre = "alici-sifre-1" })).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync($"/api/alicilar/{hesap.Id}", new AliciYaz("alici-1", "Alıcı", "yeni-alici-sifre"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await alici.GetAsync("/api/alis")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alici.PostAsJsonAsync("/api/auth/login", new { kullanici = "alici-1", sifre = "alici-sifre-1" })).StatusCode);
        (await alici.PostAsJsonAsync("/api/auth/login", new { kullanici = "alici-1", sifre = "yeni-alici-sifre" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await alici.GetAsync("/api/alis")).StatusCode);
        (await editor.PutAsJsonAsync($"/api/alicilar/{hesap.Id}", new AliciYaz("yeni-kullanici", "Alıcı", null))).EnsureSuccessStatusCode();
        (await editor.PutAsJsonAsync($"/api/alicilar/{hesap.Id}", new AliciYaz("alici-1", "Alıcı", null))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await alici.GetAsync("/api/alis")).StatusCode);
    }

    [Fact]
    public async Task Hesaplar_sifre_sizdirmaz_cakisan_ad_ve_zayif_girdi_reddedilir()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        await Ekle(editor);
        var list = await editor.GetStringAsync("/api/alicilar");
        Assert.DoesNotContain("sifre", list, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Conflict, (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("ALICI-1", "Başka", "gecerli-sifre"))).StatusCode);
        foreach (var dto in new[] { new AliciYaz("editor", "Alıcı", "gecerli-sifre"), new AliciYaz("yeni-alici", "Alıcı", "kisa"),
            new AliciYaz(null!, "Alıcı", "gecerli-sifre"), new AliciYaz("yeni-alici", null!, "gecerli-sifre") })
            Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync("/api/alicilar", dto)).StatusCode);
    }

    [Fact]
    public async Task Izleyici_alis_verisini_ve_hesaplari_goremez()
    {
        await using var f = new KasaWebFactory();
        using var editor = await f.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleme-sifre" })).EnsureSuccessStatusCode();
        using var viewer = f.CreateClient();
        (await viewer.PostAsJsonAsync("/api/auth/login", new { sifre = "izleme-sifre" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/alis")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/alicilar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/rapor/panel")).StatusCode);
    }

    private static async Task<AliciDto> Ekle(HttpClient editor)
    {
        var r = await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz("alici-1", "Alıcı Bir", "alici-sifre-1"));
        r.EnsureSuccessStatusCode();
        Assert.DoesNotContain("sifre", await r.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        return (await r.Content.ReadFromJsonAsync<AliciDto>())!;
    }
    private record Login(string Rol, string Token);
}
