using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Kasa.Api.Auth;

/// <summary>'Kasa:HizSiniri' bölümü; sınırlar istek anında okunur (test fabrikası küçültebilir).</summary>
public sealed class HizSiniriAyarlari
{
    /// <summary>'guvenlik' politikası: istemci IP'si başına pencere izni (yedek, PDF, push, şifre, kurtarma kodu).</summary>
    public int GuvenlikIzni { get; set; } = 60;
    /// <summary>'giris' politikası: giriş ve kurtarma ile giriş için istemci IP'si başına genel pencere izni.</summary>
    public int GirisIpIzni { get; set; } = 30;
    /// <summary>Giriş ve kurtarma: (istemci IP'si, normalize kullanıcı adı) başına daha sıkı pencere izni.</summary>
    public int GirisKullaniciIzni { get; set; } = 10;
    public int PencereDakika { get; set; } = 5;
}

/// <summary>
/// Ters vekil farkındalığı ve hız sınırları. Canlıda istek sistem nginx'inden 127.0.0.1:8080 yayınına,
/// oradan docker köprüsüyle konteynere gelir; bağlantı adresi vekilindir. X-Forwarded-For yalnız
/// güvenilen vekillerden (Kasa:GuvenilirVekiller) kabul edilir ve en sağdaki (vekilin eklediği) değer alınır.
/// </summary>
public static class HizSinirlari
{
    public const string Guvenlik = "guvenlik";
    public const string Giris = "giris";
    /// <summary>Loopback + docker köprü aralığı: bugünkü compose değişmeden gerçek istemci IP'si görülür.</summary>
    public const string VarsayilanGuvenilirVekiller = "127.0.0.0/8;::1/128;172.16.0.0/12";

    public static IServiceCollection AddKasaVekilVeHizSinirlari(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((o, cfg) =>
        {
            // X-Forwarded-Host açılmaz: CSRF denetimi Origin'i gerçek Host ile karşılaştırır.
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var ag in GuvenilirAglar(cfg["Kasa:GuvenilirVekiller"])) o.KnownIPNetworks.Add(ag);
        });
        services.AddOptions<HizSiniriAyarlari>().BindConfiguration("Kasa:HizSiniri")
            .Validate(a => a is { GuvenlikIzni: > 0, GirisIpIzni: > 0, GirisKullaniciIzni: > 0, PencereDakika: > 0 },
                "Kasa:HizSiniri değerleri sıfırdan büyük olmalıdır.")
            .ValidateOnStart();
        services.AddSingleton<GirisSiniri>();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (baglam, _) => new ValueTask(Red(baglam.HttpContext, baglam.Lease).ExecuteAsync(baglam.HttpContext));
            // Bölümleme ForwardedHeaders sonrası RemoteIpAddress'e göredir; politika adları ayrı kova tutar.
            o.AddPolicy(Guvenlik, http => IpBolumu(http, a => a.GuvenlikIzni));
            o.AddPolicy(Giris, http => IpBolumu(http, a => a.GirisIpIzni));
        });
        return services;
    }

    /// <summary>
    /// Hattın ilk adımı. Güvenilmeyen bir bağlantıdan X-Forwarded-For gelirse başlık yok sayılır ve bu bir kez
    /// uyarı olarak loglanır: vekilin adresi Kasa:GuvenilirVekiller dışında kalmışsa (ör. compose ağı
    /// 172.16.0.0/12 dışından adres aldıysa) tüm istemciler tek IP'ye düşer; log bunu görünür kılar.
    /// </summary>
    public static IApplicationBuilder UseKasaVekilBasliklari(this IApplicationBuilder app)
    {
        var secenekler = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        var log = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Kasa.Vekil");
        var uyarildi = 0;
        app.Use((http, sonraki) =>
        {
            if (Volatile.Read(ref uyarildi) == 0 && http.Request.Headers.ContainsKey("X-Forwarded-For")
                && http.Connection.RemoteIpAddress is { } ip && !Guvenilir(secenekler, ip)
                && Interlocked.Exchange(ref uyarildi, 1) == 0)
                log.LogWarning("X-Forwarded-For güvenilmeyen {Adres} bağlantısından geldi ve yok sayıldı. Uygulama bir vekil arkasındaysa vekilin adresini Kasa:GuvenilirVekiller ayarına ekleyin; aksi halde hız sınırları tüm istemcileri tek IP sayar.", ip);
            return sonraki(http);
        });
        return app.UseForwardedHeaders();
    }

    private static bool Guvenilir(ForwardedHeadersOptions o, IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return o.KnownProxies.Contains(ip) || o.KnownIPNetworks.Any(ag => ag.Contains(ip));
    }

    /// <summary>';' veya ',' ile ayrılmış CIDR/IP listesi; boşsa varsayılan. Geçersiz değer başlangıcı durdurur.</summary>
    public static IReadOnlyList<System.Net.IPNetwork> GuvenilirAglar(string? deger)
    {
        var liste = string.IsNullOrWhiteSpace(deger) ? VarsayilanGuvenilirVekiller : deger;
        var aglar = new List<System.Net.IPNetwork>();
        foreach (var parca in liste.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (System.Net.IPNetwork.TryParse(parca, out var ag)) aglar.Add(ag);
            else if (!parca.Contains('/') && IPAddress.TryParse(parca, out var ip))
                aglar.Add(new System.Net.IPNetwork(ip, ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
            else throw new InvalidOperationException($"Kasa:GuvenilirVekiller geçersiz CIDR içeriyor: '{parca}'.");
        }
        return aglar;
    }

    /// <summary>IPv4 adresin kendisi; IPv6 istemciler /64 önekiyle tek bölüme düşer (adres döndürerek kaçamaz).</summary>
    public static string IstemciAnahtari(IPAddress? ip)
    {
        if (ip is null) return "yerel";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();
        var baytlar = ip.GetAddressBytes();
        Array.Clear(baytlar, 8, 8);
        return new IPAddress(baytlar) + "/64";
    }

    /// <summary>429 yanıtı: Türkçe 'hata' gövdesi ve (biliniyorsa) saniye cinsinden Retry-After.</summary>
    public static IResult Red(HttpContext http, RateLimitLease lease)
    {
        var mesaj = "Çok fazla deneme yapıldı. Birkaç dakika sonra yeniden deneyin.";
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var sure) && sure > TimeSpan.Zero)
        {
            http.Response.Headers.RetryAfter = Math.Ceiling(sure.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            mesaj = $"Çok fazla deneme yapıldı. {Math.Max(1, (int)Math.Ceiling(sure.TotalMinutes))} dakika sonra yeniden deneyin.";
        }
        return Results.Json(new { hata = mesaj }, statusCode: StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Kullanıcı adı gövdede olduğundan (IP, kullanıcı adı) sınırı bağlama sonrası uç filtresiyle uygulanır;
    /// IP başına genel pencere ('giris' politikası) ondan önce ara katmanda işler.</summary>
    public static RouteHandlerBuilder GirisSiniriUygula<T>(this RouteHandlerBuilder uc, Func<T, string?> kullanici) =>
        uc.RequireRateLimiting(Giris).AddEndpointFilter(async (baglam, sonraki) =>
        {
            var http = baglam.HttpContext;
            var dto = baglam.Arguments.OfType<T>().FirstOrDefault();
            using var lease = http.RequestServices.GetRequiredService<GirisSiniri>()
                .Dene(http.Connection.RemoteIpAddress, dto is null ? null : kullanici(dto));
            return lease.IsAcquired ? await sonraki(baglam) : Red(http, lease);
        });

    private static RateLimitPartition<string> IpBolumu(HttpContext http, Func<HizSiniriAyarlari, int> izin)
    {
        var ayar = http.RequestServices.GetRequiredService<IOptionsMonitor<HizSiniriAyarlari>>().CurrentValue;
        return RateLimitPartition.GetFixedWindowLimiter(IstemciAnahtari(http.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = izin(ayar), Window = TimeSpan.FromMinutes(ayar.PencereDakika), QueueLimit = 0 });
    }
}

/// <summary>Giriş ve kurtarma için (istemci IP'si, normalize kullanıcı adı) başına sabit pencere. Kimliksiz bir
/// saldırgan yalnız kendi IP'sinin kovasını tüketir; başka IP'lerdeki kullanıcıların girişi kilitlenmez.</summary>
public sealed class GirisSiniri : IDisposable
{
    private readonly PartitionedRateLimiter<string> _sinirlayici;

    public GirisSiniri(IOptionsMonitor<HizSiniriAyarlari> ayarlar) =>
        _sinirlayici = PartitionedRateLimiter.Create<string, string>(anahtar => RateLimitPartition.GetFixedWindowLimiter(anahtar, _ =>
        {
            var a = ayarlar.CurrentValue;
            return new FixedWindowRateLimiterOptions { PermitLimit = a.GirisKullaniciIzni, Window = TimeSpan.FromMinutes(a.PencereDakika), QueueLimit = 0 };
        }));

    public RateLimitLease Dene(IPAddress? ip, string? kullanici) =>
        _sinirlayici.AttemptAcquire(HizSinirlari.IstemciAnahtari(ip) + "\n" + Normalize(kullanici));

    /// <summary>Giriş ucuyla aynı normalleştirme (kırpılmış, küçük harf); aşırı uzun adlar anahtarı büyütmez.</summary>
    public static string Normalize(string? kullanici)
    {
        var k = (kullanici ?? "").Trim().ToLowerInvariant();
        return k.Length <= 64 ? k : k[..64];
    }

    public void Dispose() => _sinirlayici.Dispose();
}
