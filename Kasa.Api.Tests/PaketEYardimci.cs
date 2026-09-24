using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Auth;

namespace Kasa.Api.Tests;

/// <summary>Paket E testlerinin ortak yardımcıları (giriş, kullanıcı ekleme, JSON okuma).</summary>
internal static class PaketEYardimci
{
    public const string EditorSifre = "kasa123";

    /// <summary>Giriş denemesi; başarılıysa istemci çerezle oturum açmış olur.</summary>
    public static async Task<(HttpResponseMessage Yanit, HttpClient Istemci)> GirisAsync(KasaWebFactory f, string? kullanici, string sifre,
        string? kod = null, string? cihaz = null, string? ip = null)
    {
        var c = f.CreateClient();
        if (cihaz is not null) c.DefaultRequestHeaders.Add(CihazAdi.Baslik, Uri.EscapeDataString(cihaz));
        using var istek = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { kullanici, sifre, kod }),
        };
        if (ip is not null) istek.Headers.Add(KasaWebFactory.TestIpBasligi, ip);
        var r = await c.SendAsync(istek);
        return (r, c);
    }

    public static async Task<HttpClient> GirisliAsync(KasaWebFactory f, string? kullanici, string sifre, string? kod = null, string? cihaz = null)
    {
        var (r, c) = await GirisAsync(f, kullanici, sifre, kod, cihaz);
        Assert.True(r.IsSuccessStatusCode, $"Giriş başarısız: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return c;
    }

    public static Task<HttpClient> EditorAsync(KasaWebFactory f, string? cihaz = null) => GirisliAsync(f, "editor", EditorSifre, cihaz: cihaz);

    /// <summary>Bearer token'lı istemci (çerezsiz): uygulamanın kullandığı yol.</summary>
    public static HttpClient Bearer(KasaWebFactory f, string token)
    {
        var c = f.CreateClient(new() { HandleCookies = false });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public static async Task<string> TokenAsync(HttpResponseMessage r)
        => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage r)
        => await r.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string?> HataAsync(HttpResponseMessage r)
    {
        var j = await JsonAsync(r);
        return j.TryGetProperty("hata", out var h) ? h.GetString() : null;
    }

    /// <summary>Editör kişisel kullanıcı ekler; Id döner.</summary>
    public static async Task<int> KullaniciEkleAsync(HttpClient editor, string adSoyad, string kullaniciAdi, string rol, string sifre)
    {
        var r = await editor.PostAsJsonAsync("/api/kullanicilar", new { adSoyad, kullaniciAdi, rol, sifre });
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"Kullanıcı eklenemedi: {await r.Content.ReadAsStringAsync()}");
        return (await JsonAsync(r)).GetProperty("id").GetInt32();
    }

    /// <summary>Şu anki TOTP kodu (sunucu gerçek saati kullanır).</summary>
    public static string SuAnkiKod(string sir, int kaydir = 0)
        => Totp.Kod(Base32.Oku(sir)!, Totp.AdimNo(DateTimeOffset.UtcNow) + kaydir);
}
