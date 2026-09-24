using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>Paket D testleri: bugün = 24 Eylül 2026 Perşembe, İstanbul 12:00 (UTC 09:00).</summary>
public class PaketDFactory : KasaWebFactory
{
    public static readonly DateTime SimdiUtc = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new Saat())));
    }

    private sealed class Saat : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(SimdiUtc);
    }

    /// <summary>İzleyici şifresi ayarlanır ve izleyici olarak giriş yapılır.</summary>
    public async Task<HttpClient> IzleyiciAsync()
    {
        var editor = await EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" })).EnsureSuccessStatusCode();
        var izleyici = CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" })).EnsureSuccessStatusCode();
        return izleyici;
    }
}

/// <summary>Paket D testlerinin ortak HTTP yardımcıları.</summary>
public static class PaketD
{
    public static async Task<JsonElement> Json(HttpResponseMessage r)
        => await r.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<JsonElement> GetJson(HttpClient c, string yol)
    {
        var r = await c.GetAsync(yol);
        r.EnsureSuccessStatusCode();
        return await Json(r);
    }

    /// <summary>Başarılı yanıtın gövdesi; başarısızsa gövdeyi de gösteren bir hata.</summary>
    public static async Task<JsonElement> Basarili(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await Json(r);
    }

    public static async Task<string> Hata(HttpResponseMessage r)
        => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString()!;

    public static async Task<decimal> GuncelKasa(HttpClient c)
        => (await GetJson(c, "/api/rapor/panel")).GetProperty("guncelKasa").GetDecimal();

    public static async Task<int> IslemEkle(HttpClient c, string cari, decimal tutar, string tarih = "2026-09-10",
        string kanal = "MEZAT", string tip = "Cari", int? krediKartiId = null, string? not = null)
    {
        var r = await c.PostAsJsonAsync("/api/islemler", new { tarih, cari, tutarTl = tutar, kanal, tip, not, krediKartiId });
        return (await Basarili(r)).GetProperty("id").GetInt32();
    }

    /// <summary>Geçmiş satırları (en yeni önce).</summary>
    public static async Task<List<JsonElement>> Gecmis(HttpClient c, string? tur = null)
    {
        var yol = "/api/gecmis?limit=1000" + (tur is null ? "" : $"&tur={Uri.EscapeDataString(tur)}");
        return (await GetJson(c, yol)).EnumerateArray().ToList();
    }

    public static Task<HttpResponseMessage> GeriAl(HttpClient c, int satirId) => c.PostAsync($"/api/gecmis/{satirId}/geri-al", null);

    public static int Id(this JsonElement e) => e.GetProperty("id").GetInt32();
    public static string Str(this JsonElement e, string ad) => e.GetProperty(ad).GetString()!;
    public static decimal Dec(this JsonElement e, string ad) => e.GetProperty(ad).GetDecimal();
    public static bool Bool(this JsonElement e, string ad) => e.GetProperty(ad).GetBoolean();
    public static bool Null(this JsonElement e, string ad) => e.GetProperty(ad).ValueKind == JsonValueKind.Null;
}
