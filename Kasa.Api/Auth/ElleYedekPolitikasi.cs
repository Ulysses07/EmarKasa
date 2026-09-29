using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Kasa.Api.Auth;

/// <summary>
/// Elle yedek (POST /api/yedek) için ayrı ve sıkı hız politikası ('yedek'): kimliği doğrulanmış kullanıcı ve istemci
/// IP'si (IPv6'da /64) başına YedekIzni / YedekPencereDakika; üretimde saatte 5. Elle yedek bütün veritabanını
/// sıkıştırıp indirir (CPU, disk ve bant genişliği ister); IP başına genel 'guvenlik' kovasını artık tüketmez, bu
/// yüzden yedek almak şifre, kurtarma kodu, PDF ya da bildirim işlemlerine 429 aldırmaz. Kullanıcıyla birlikte IP'ye
/// bölünür: ele geçirilmiş bir oturumun başka ağdan yaptığı yedekler editörün kendi ağındaki kotasını tüketmez.
/// Hız sınırı yetkilendirmeden sonra çalıştığı için kimliksiz (401) ve yetkisiz (403) istek kota tüketmez.
/// Kalıntı risk: aynı oturumu birçok ağdan kullanan biri her ağda ayrı kota alır (oturum zaten bütün veriyi
/// okuyabilir); sunucuda elle yedeklerden yalnız en yeni 10'u tutulur, otomatik yedeklere dokunulmaz.
/// </summary>
internal sealed class ElleYedekPolitikasi : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } =
        (baglam, _) => new ValueTask(Red(baglam.HttpContext, baglam.Lease).ExecuteAsync(baglam.HttpContext));

    public RateLimitPartition<string> GetPartition(HttpContext http)
    {
        var ayar = http.RequestServices.GetRequiredService<IOptionsMonitor<HizSiniriAyarlari>>().CurrentValue;
        var kullanici = $"{http.User.FindFirstValue(ClaimTypes.Role)}:{http.User.FindFirstValue("alici_id")}";
        return RateLimitPartition.GetFixedWindowLimiter(kullanici + "\n" + HizSinirlari.IstemciAnahtari(http.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = ayar.YedekIzni, Window = TimeSpan.FromMinutes(ayar.YedekPencereDakika), QueueLimit = 0 });
    }

    /// <summary>429: sınırı ve bekleme süresini söyleyen Türkçe 'hata' gövdesi ve saniye cinsinden Retry-After.</summary>
    private static IResult Red(HttpContext http, RateLimitLease lease)
    {
        var ayar = http.RequestServices.GetRequiredService<IOptionsMonitor<HizSiniriAyarlari>>().CurrentValue;
        var mesaj = $"Elle yedek sınırına ulaşıldı: {ayar.YedekPencereDakika} dakikada en çok {ayar.YedekIzni} elle yedek alınabilir.";
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var sure) && sure > TimeSpan.Zero)
        {
            http.Response.Headers.RetryAfter = Math.Ceiling(sure.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            mesaj += $" {Math.Max(1, (int)Math.Ceiling(sure.TotalMinutes))} dakika sonra yeniden deneyin.";
        }
        else
            mesaj += " Birkaç dakika sonra yeniden deneyin.";
        return Results.Json(new { hata = mesaj + " Otomatik yedekleme bundan etkilenmez." }, statusCode: StatusCodes.Status429TooManyRequests);
    }
}
