using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kasa.Api.Auth;

/// <summary>
/// 'Kasa:HizSiniri' bölümü. Pencere sınırları istek anında okunur (test fabrikası küçültebilir), doğrulama
/// eşzamanlılığı başlangıçta. Üretim değerleri bu varsayılanlardır (appsettings.json); geliştirme ortamı
/// (appsettings.Development.json, test fabrikası dahil) pencereleri gevşetir.
/// </summary>
public sealed class HizSiniriAyarlari
{
    /// <summary>'guvenlik' politikası: istemci IP'si başına pencere izni (yedek, PDF, push, şifre, kurtarma kodu).</summary>
    public int GuvenlikIzni { get; set; } = 60;
    /// <summary>'giris' politikası: giriş ve kurtarma ile giriş için istemci IP'si başına genel pencere izni.</summary>
    public int GirisIpIzni { get; set; } = 30;
    /// <summary>Giriş ve kurtarma: (istemci IP'si, normalize kullanıcı adı) başına daha sıkı pencere izni.</summary>
    public int GirisKullaniciIzni { get; set; } = 10;
    /// <summary>Giriş ve kurtarma: IPv6 istemcilerde /48 bloğu başına toplu pencere izni (IPv4'te uygulanmaz).
    /// Tek kişinin /48 ya da /56 bloğu /64 bölümlerine yayılarak IP pencerelerini çoğaltamaz.</summary>
    public int GirisAgIzni { get; set; } = 60;
    public int PencereDakika { get; set; } = 5;
    /// <summary>Giriş: hedef (editör, her alıcı, izleyici şifresi) başına IP'den bağımsız başarısız deneme izni.</summary>
    public int HedefBasarisizIzni { get; set; } = 30;
    /// <summary>Giriş: ağ başına, bütün hedefler için ortak başarısız deneme izni (ağ: IPv4 adresi ya da IPv6 /48 bloğu).
    /// Hedef izninden küçük tutulur: tek bir ağ bir hedefin bütçesini tek başına tüketip hedefi herkese kilitleyemez.</summary>
    public int AgBasarisizIzni { get; set; } = 15;
    public int HedefPencereDakika { get; set; } = 15;
    /// <summary>Hedefe son bu kadar günde başarıyla girilmiş ağ, hedef bütçesi dolduğunda da girebilir (0: kapalı).</summary>
    public int TanidikAgGun { get; set; } = 30;
    /// <summary>Aynı anda yürüyen şifre doğrulaması (PBKDF2) ve bekleyebilecek giriş sayısı; kuyruk doluysa 429.</summary>
    public int SifreDogrulamaEszamanli { get; set; } = 2;
    public int SifreDogrulamaKuyrugu { get; set; } = 20;
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
    /// <summary>
    /// Loopback + Docker'ın varsayılan adres havuzları (172.17–172.31/16, ardından 192.168.0.0/16 içinden /20'lik
    /// ağlar): compose ağı hangi varsayılan havuzdan adres alırsa alsın gerçek istemci IP'si görülür. Konteyner
    /// yalnız 127.0.0.1:8080'e yayınlandığından bu adreslerden yalnız yerel vekil ve aynı ağdaki konteynerler
    /// bağlanabilir. Vekilsiz, doğrudan yerel ağa açılan bir kurulumda liste daraltılmalıdır.
    /// </summary>
    public const string VarsayilanGuvenilirVekiller = "127.0.0.0/8;::1/128;172.16.0.0/12;192.168.0.0/16";

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
            .Validate(a => a is
                {
                    GuvenlikIzni: > 0, GirisIpIzni: > 0, GirisKullaniciIzni: > 0, GirisAgIzni: > 0, PencereDakika: > 0,
                    HedefBasarisizIzni: > 0, AgBasarisizIzni: > 0, HedefPencereDakika: > 0, TanidikAgGun: >= 0,
                    SifreDogrulamaEszamanli: > 0, SifreDogrulamaKuyrugu: >= 0,
                },
                "Kasa:HizSiniri değerleri sıfırdan büyük olmalıdır (TanidikAgGun ve SifreDogrulamaKuyrugu sıfır olabilir).")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<VekilDurumu>();
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
    /// Hattın ilk adımı. Güvenilmeyen bir bağlantıdan X-Forwarded-For gelirse başlık yok sayılır; ilk kaynak
    /// bir kez uyarı olarak loglanır ve Ayarlar ekranında editöre gösterilir (VekilDurumu): vekilin adresi
    /// Kasa:GuvenilirVekiller dışında kalmışsa (ör. compose ağı özel bir havuzdan adres aldıysa) tüm istemciler
    /// tek IP'ye düşer; bu iki işaret yanlış yapılandırmayı görünür kılar.
    /// </summary>
    public static IApplicationBuilder UseKasaVekilBasliklari(this IApplicationBuilder app)
    {
        var secenekler = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        var durum = app.ApplicationServices.GetRequiredService<VekilDurumu>();
        var log = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Kasa.Vekil");
        app.Use((http, sonraki) =>
        {
            if (durum.Uyari is null && http.Request.Headers.ContainsKey("X-Forwarded-For")
                && http.Connection.RemoteIpAddress is { } ip && !Guvenilir(secenekler, ip) && durum.Kaydet(ip))
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
                aglar.Add(new System.Net.IPNetwork(ip, ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128));
            else throw new InvalidOperationException($"Kasa:GuvenilirVekiller geçersiz CIDR içeriyor: '{parca}'.");
        }
        return aglar;
    }

    /// <summary>IPv4 adresin kendisi; IPv6 istemciler /64 önekiyle tek bölüme düşer (adres döndürerek kaçamaz).</summary>
    public static string IstemciAnahtari(IPAddress? ip)
    {
        if (ip is null) return "yerel";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var baytlar = ip.GetAddressBytes();
        Array.Clear(baytlar, 8, 8);
        return new IPAddress(baytlar) + "/64";
    }

    /// <summary>IPv6 istemcinin /48 bloğu (giriş için toplu pencere); IPv4 ve IPv4 eşlemeli adreslerde null.</summary>
    public static string? AgAnahtari(IPAddress? ip)
    {
        if (ip is null || ip.IsIPv4MappedToIPv6 || ip.AddressFamily != AddressFamily.InterNetworkV6) return null;
        var baytlar = ip.GetAddressBytes();
        Array.Clear(baytlar, 6, 10);
        return new IPAddress(baytlar) + "/48";
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

    /// <summary>Şifre doğrulama kuyruğu dolu: 429, Türkçe ileti ve kısa Retry-After (istemciler 429'u zaten gösterir).</summary>
    public static IResult Yogun(HttpContext http)
    {
        http.Response.Headers.RetryAfter = "5";
        return Results.Json(new { hata = "Sunucu şu anda yoğun: çok sayıda giriş isteği işleniyor. Birkaç saniye sonra yeniden deneyin." },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Kullanıcı adı gövdede olduğundan (IP, kullanıcı adı) ve IPv6 /48 sınırları bağlama sonrası uç
    /// filtresiyle uygulanır; IP başına genel pencere ('giris' politikası) ondan önce ara katmanda işler.</summary>
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

/// <summary>
/// Güvenilmeyen bir bağlantıdan gelen X-Forwarded-For'un ilk görüldüğü adres. Vekilin adresi
/// Kasa:GuvenilirVekiller dışında kalmış olabilir; o durumda tüm istemciler tek IP sayılır. Ayarlar'da gösterilir.
/// </summary>
public sealed class VekilDurumu
{
    private IPAddress? _guvenilmeyenKaynak;

    /// <summary>Yalnız ilk kayıtta true döner.</summary>
    public bool Kaydet(IPAddress kaynak) => Interlocked.CompareExchange(ref _guvenilmeyenKaynak, kaynak, null) is null;

    public string? Uyari => Volatile.Read(ref _guvenilmeyenKaynak) is { } ip
        ? $"Sunucu, güvenilmeyen {ip} bağlantısından gelen X-Forwarded-For başlığını yok saydı. Uygulama bir vekil arkasındaysa vekilin adresini Kasa:GuvenilirVekiller ayarına ekleyin; aksi halde hız sınırları tüm istemcileri tek IP sayar."
        : null;
}

/// <summary>
/// Giriş korumaları (süreç içi, bellekte):
/// <list type="number">
/// <item>(istemci IP'si, normalize kullanıcı adı) ve IPv6 /48 bloğu başına istek penceresi (uç filtresi). Kimliksiz
/// bir saldırgan yalnız kendi IP'sinin kovasını tüketir; başka IP'lerdeki kullanıcıların girişi kilitlenmez.</item>
/// <item>Hedef (editör, her alıcı, izleyici şifresi) başına IP'den bağımsız başarısız deneme bütçesi. Dağıtık kaba
/// kuvvet IP sayısıyla, izleyici şifresine kaba kuvvet kullanıcı adı döndürerek çoğalamaz. Bütçe dolunca hedefe
/// tanınmayan ağlardan gelen denemeler şifre doğrulanmadan reddedilir. Hedefe son TanidikAgGun günde başarıyla
/// girilmiş ağ girmeye devam eder: dağıtık saldırgan meşru kullanıcıyı alışık olduğu ağdan kilitleyemez. İzleyici
/// kilidi editör dışındaki bütün adlara uygulanır; yanıt, bir adın alıcıya ait olup olmadığını ele vermez.</item>
/// <item>Ağ başına, bütün hedefler için ortak ve hedef bütçesinden küçük başarısız deneme bütçesi (ağ: IPv4 adresi ya
/// da IPv6 /48 bloğu). Her zaman uygulanır: tek ağ bir hedefin bütçesini tek başına tüketemez, tanınan ağ da
/// sınırsız deneyemez; ortak olduğu için kilitlenen ağın yanıtları da adları ayırt ettirmez.</item>
/// <item>Eşzamanlı şifre doğrulaması (PBKDF2) sınırlı ve kısa kuyruklu: giriş selinin CPU tüketimi uygulamanın geri
/// kalanını yavaşlatamaz.</item>
/// </list>
/// </summary>
public sealed class GirisSiniri : IDisposable
{
    public const string EditorHedefi = "editor";
    public const string IzleyiciHedefi = "izleyici";
    public static string AliciHedefi(string kullanici) => "alici:" + kullanici;

    private const string AgOnEki = "ag\n";
    private const int TanidikUstSinir = 4096;
    private readonly PartitionedRateLimiter<string> _pencere;
    private readonly PartitionedRateLimiter<string> _basarisiz;
    private readonly ConcurrencyLimiter _dogrulama;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tanidik = new();
    private readonly IOptionsMonitor<HizSiniriAyarlari> _ayarlar;
    private readonly TimeProvider _saat;
    private readonly ILogger<GirisSiniri> _log;

    public GirisSiniri(IOptionsMonitor<HizSiniriAyarlari> ayarlar, TimeProvider saat, ILogger<GirisSiniri> log)
    {
        _ayarlar = ayarlar;
        _saat = saat;
        _log = log;
        // İstek pencereleri: "ag\n<IPv6 /48>" ya da "<IP>\n<kullanıcı adı>".
        _pencere = PartitionedRateLimiter.Create<string, string>(anahtar => RateLimitPartition.GetFixedWindowLimiter(anahtar, _ =>
        {
            var a = ayarlar.CurrentValue;
            return Pencere(anahtar.StartsWith(AgOnEki, StringComparison.Ordinal) ? a.GirisAgIzni : a.GirisKullaniciIzni, a.PencereDakika);
        }));
        // Başarısız deneme bütçeleri: "ag\n<ağ>" ya da hedef adı.
        _basarisiz = PartitionedRateLimiter.Create<string, string>(anahtar => RateLimitPartition.GetFixedWindowLimiter(anahtar, _ =>
        {
            var a = ayarlar.CurrentValue;
            return Pencere(anahtar.StartsWith(AgOnEki, StringComparison.Ordinal) ? a.AgBasarisizIzni : a.HedefBasarisizIzni, a.HedefPencereDakika);
        }));
        var baslangic = ayarlar.CurrentValue;
        _dogrulama = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = baslangic.SifreDogrulamaEszamanli,
            QueueLimit = baslangic.SifreDogrulamaKuyrugu,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    private static FixedWindowRateLimiterOptions Pencere(int izin, int dakika)
        => new() { PermitLimit = izin, Window = TimeSpan.FromMinutes(dakika), QueueLimit = 0 };

    /// <summary>Uç filtresi: IPv6 /48 bloğu ve (IP, kullanıcı adı) pencereleri; biri doluysa reddedilen lease.</summary>
    public RateLimitLease Dene(IPAddress? ip, string? kullanici)
    {
        if (HizSinirlari.AgAnahtari(ip) is { } blok)
        {
            var blokIzni = _pencere.AttemptAcquire(AgOnEki + blok);
            if (!blokIzni.IsAcquired) return blokIzni;
            blokIzni.Dispose();
        }
        return _pencere.AttemptAcquire(HizSinirlari.IstemciAnahtari(ip) + "\n" + Normalize(kullanici));
    }

    /// <summary>Şifre doğrulanmadan önce çağrılır: hedef bu ağ için kilitliyse reddedilen lease (Retry-After taşır), açıksa null.</summary>
    public RateLimitLease? HedefKilidi(string hedef, IPAddress? ip)
    {
        var ag = _basarisiz.AttemptAcquire(AgOnEki + Ag(ip), 0);
        if (!ag.IsAcquired) return ag;
        ag.Dispose();
        if (Tanidik(hedef, ip)) return null;
        // Alıcı adları izleyici kilidine de bağlıdır: izleyici bütçesi dolduğunda editör dışındaki her ad aynı yanıtı alır.
        string[] kovalar = hedef is EditorHedefi or IzleyiciHedefi ? [hedef] : [hedef, IzleyiciHedefi];
        foreach (var kova in kovalar)
        {
            var genel = _basarisiz.AttemptAcquire(kova, 0);
            if (!genel.IsAcquired) return genel;
            genel.Dispose();
        }
        return null;
    }

    /// <summary>Doğrulama sonucu: başarı ağı hedef için tanınır kılar; başarısızlık ağın ve hedefin bütçesini tüketir.</summary>
    public void Sonuc(string hedef, IPAddress? ip, bool basarili)
    {
        if (basarili) { TanidikYap(hedef, ip); return; }
        _basarisiz.AttemptAcquire(AgOnEki + Ag(ip)).Dispose();
        using var harcanan = _basarisiz.AttemptAcquire(hedef);
        if (!harcanan.IsAcquired) return;
        using var kalan = _basarisiz.AttemptAcquire(hedef, 0);
        if (kalan.IsAcquired) return;
        var a = _ayarlar.CurrentValue;
        _log.LogWarning("Giriş hedefi '{Hedef}' için başarısız deneme sınırı doldu ({Izin} deneme / {Dakika} dk). Pencere bitene kadar tanınmayan ağlardan bu hedefe giriş reddedilir; dağıtık bir kaba kuvvet denemesi olabilir.",
            hedef, a.HedefBasarisizIzni, a.HedefPencereDakika);
    }

    /// <summary>Eşzamanlı PBKDF2 doğrulaması için izin; kuyruk doluysa hemen reddedilen lease döner.</summary>
    public ValueTask<RateLimitLease> DogrulamaIzniAsync(CancellationToken ct) => _dogrulama.AcquireAsync(1, ct);

    /// <summary>İzin bekleyen doğrulama sayısı.</summary>
    public long KuyruktakiDogrulama => _dogrulama.GetStatistics()?.CurrentQueuedCount ?? 0;

    private bool Tanidik(string hedef, IPAddress? ip)
    {
        var gun = _ayarlar.CurrentValue.TanidikAgGun;
        return gun > 0 && _tanidik.TryGetValue(hedef + "\n" + Ag(ip), out var son) && _saat.GetUtcNow() - son < TimeSpan.FromDays(gun);
    }

    private void TanidikYap(string hedef, IPAddress? ip)
    {
        var gun = _ayarlar.CurrentValue.TanidikAgGun;
        if (gun <= 0) return;
        var anahtar = hedef + "\n" + Ag(ip);
        var simdi = _saat.GetUtcNow();
        // Yalnız doğru şifreyle büyür; yine de üst sınıra gelinirse önce süresi dolanlar atılır.
        if (!_tanidik.ContainsKey(anahtar) && _tanidik.Count >= TanidikUstSinir)
        {
            foreach (var (k, son) in _tanidik)
                if (simdi - son >= TimeSpan.FromDays(gun)) _tanidik.TryRemove(k, out _);
            if (_tanidik.Count >= TanidikUstSinir) return;
        }
        _tanidik[anahtar] = simdi;
    }

    /// <summary>Ağ: IPv4 adresi ya da IPv6 /48 bloğu (tek kişinin bloğu tek ağ sayılır).</summary>
    private static string Ag(IPAddress? ip) => HizSinirlari.AgAnahtari(ip) ?? HizSinirlari.IstemciAnahtari(ip);

    /// <summary>Giriş ucuyla aynı normalleştirme (kırpılmış, küçük harf); aşırı uzun adlar anahtarı büyütmez.</summary>
    public static string Normalize(string? kullanici)
    {
        var k = (kullanici ?? "").Trim().ToLowerInvariant();
        return k.Length <= 64 ? k : k[..64];
    }

    public void Dispose()
    {
        _pencere.Dispose();
        _basarisiz.Dispose();
        _dogrulama.Dispose();
    }
}
