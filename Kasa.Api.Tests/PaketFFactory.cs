using System.Net.Http.Json;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>
/// Paket F testleri: bugün = 24 Eylül 2026 (İstanbul 12:00), ekler geçici bir klasöre yazılır
/// (testler kaynak ağacına dosya bırakmaz).
/// </summary>
public class PaketFFactory : KasaWebFactory
{
    public static readonly DateTime SimdiUtc = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);

    public string BelgeKlasoru { get; } = Path.Combine(Path.GetTempPath(), "kasa-belge-test-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Kasa:BelgeKlasoru"] = BelgeKlasoru }));
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SabitSaat())));
    }

    private sealed class SabitSaat : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(SimdiUtc);
    }

    /// <summary>İzleyici şifresini ayarlar ve izleyici olarak giriş yapmış istemci döner.</summary>
    public async Task<HttpClient> IzleyiciAsync()
    {
        var editor = await EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" })).EnsureSuccessStatusCode();
        var izleyici = CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" })).EnsureSuccessStatusCode();
        return izleyici;
    }

    /// <summary>Doğrudan DB erişimi (tohumlama / doğrulama).</summary>
    public T Db<T>(Func<KasaDbContext, T> islem)
    {
        using var scope = Services.CreateScope();
        return islem(scope.ServiceProvider.GetRequiredService<KasaDbContext>());
    }

    public void Db(Action<KasaDbContext> islem)
    {
        using var scope = Services.CreateScope();
        islem(scope.ServiceProvider.GetRequiredService<KasaDbContext>());
    }

    /// <summary>İşlemleri, ekleri ve POS kayıtlarını temizler (sınıf içi testler birbirini etkilemesin).</summary>
    public void Temizle() => Db(db =>
    {
        db.IslemEkleri.ExecuteDelete();
        db.PosSatislari.ExecuteDelete();
        db.PosTanimlari.ExecuteDelete();
        db.TekrarlayanGirisler.ExecuteDelete();
        db.Islemler.ExecuteDelete();
    });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            try { if (Directory.Exists(BelgeKlasoru)) Directory.Delete(BelgeKlasoru, recursive: true); }
            catch (IOException) { }
    }
}

/// <summary>Paket F testlerinin ortak istek yardımcıları.</summary>
internal static class PaketFYardimci
{
    public record IslemYanit(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, string Tip, string? Not,
        int? KrediKartiId, string? BelgeTuru, string? BelgeNo, bool FaturaBekleniyor);
    public record HataYanit(string Hata);

    public static async Task<IslemYanit> IslemEkleAsync(HttpClient c, string cari, decimal tutar, string tarih = "2026-09-10",
        string kanal = "MEZAT", string? belgeTuru = null, string? belgeNo = null, bool faturaBekleniyor = false, string? not = null)
    {
        var r = await c.PostAsJsonAsync("/api/islemler", new
        {
            tarih, cari, tutarTl = tutar, kanal, tip = "Cari", not, belgeTuru, belgeNo, faturaBekleniyor,
        });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<IslemYanit>())!;
    }

    public static async Task<string> HataAsync(HttpResponseMessage r)
        => (await r.Content.ReadFromJsonAsync<HataYanit>())!.Hata;
}
