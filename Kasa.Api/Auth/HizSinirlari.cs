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
/// (appsettings.Development.json, test fabrikası dahil) pencereleri gevşetir. Değerler başlangıçta
/// <see cref="Hatalar"/> ile doğrulanır; tutarsız yapılandırma uygulamayı başlatmaz.
/// </summary>
public sealed class HizSiniriAyarlari
{
    /// <summary>'guvenlik' politikası: istemci IP'si başına pencere izni (PDF, push, şifre, kurtarma kodu).</summary>
    public int GuvenlikIzni { get; set; } = 60;
    /// <summary>'giris' politikası: giriş ve kurtarma ile giriş için istemci IP'si başına genel pencere izni.</summary>
    public int GirisIpIzni { get; set; } = 30;
    /// <summary>Giriş ve kurtarma: (istemci IP'si, normalize kullanıcı adı) başına daha sıkı pencere izni.</summary>
    public int GirisKullaniciIzni { get; set; } = 10;
    /// <summary>Giriş ve kurtarma: IPv6 istemcilerde /48 bloğu başına toplu pencere izni (IPv4'te uygulanmaz).
    /// Tek kişinin /48 ya da /56 bloğu /64 bölümlerine yayılarak IP pencerelerini çoğaltamaz.</summary>
    public int GirisAgIzni { get; set; } = 60;
    public int PencereDakika { get; set; } = 5;
    /// <summary>Giriş: hedef bütçesi başına IP'den bağımsız başarısız deneme izni. Editörün kendi bütçesi, editör
    /// dışındaki bütün adların (alıcılar ve izleyici şifresi) tek ortak bütçesi vardır.</summary>
    public int HedefBasarisizIzni { get; set; } = 30;
    /// <summary>Giriş: ağ başına, bütün hedefler için ortak başarısız deneme izni (ağ: IPv4 adresi ya da IPv6 /48 bloğu).
    /// Hedef izninden küçük olmalıdır: tek bir ağ bir hedefin bütçesini tek başına tüketip hedefi herkese kilitleyemez.
    /// Doğrulama kapasitesinden (eşzamanlı + kuyruk) de küçük olmalıdır: tek ağ PBKDF2 kuyruğunu tek başına dolduramaz.</summary>
    public int AgBasarisizIzni { get; set; } = 15;
    public int HedefPencereDakika { get; set; } = 15;
    /// <summary>Tanıdık cihaz belirtecinin ömrü (gün, 0–365; 0: kapalı). Başarılı girişte verilen belirteç bu süre
    /// boyunca cihazı hedef kilidinden muaf tutar (<see cref="TanidikCihaz"/>); her başarılı girişte yenilenir.</summary>
    public int TanidikCihazGun { get; set; } = 30;
    /// <summary>Aynı anda yürüyen şifre doğrulaması (PBKDF2) ve bekleyebilecek giriş sayısı; kuyruk doluysa 429.</summary>
    public int SifreDogrulamaEszamanli { get; set; } = 2;
    public int SifreDogrulamaKuyrugu { get; set; } = 20;
    /// <summary>'yedek' politikası: elle yedek için (kimliği doğrulanmış kullanıcı, istemci IP'si) başına pencere izni.</summary>
    public int YedekIzni { get; set; } = 5;
    public int YedekPencereDakika { get; set; } = 60;

    /// <summary>Yapılandırma hataları (boşsa geçerli). Sınırlar pozitif olmalı; ağ bütçesi hedef bütçesinden ve
    /// şifre doğrulama kapasitesinden küçük olmalıdır (aksi halde tek ağ bir hedefi herkese kilitleyebilir ya da
    /// doğrulama kuyruğunu tek başına doldurup başka ağlardaki girişlere "sunucu yoğun" yanıtı aldırabilir).</summary>
    public IEnumerable<string> Hatalar()
    {
        if (this is not
            {
                GuvenlikIzni: > 0, GirisIpIzni: > 0, GirisKullaniciIzni: > 0, GirisAgIzni: > 0, PencereDakika: > 0,
                HedefBasarisizIzni: > 0, AgBasarisizIzni: > 0, HedefPencereDakika: > 0, TanidikCihazGun: >= 0 and <= 365,
                SifreDogrulamaEszamanli: > 0, SifreDogrulamaKuyrugu: >= 0, YedekIzni: > 0, YedekPencereDakika: > 0,
            })
        {
            yield return "Kasa:HizSiniri değerleri sıfırdan büyük olmalıdır (SifreDogrulamaKuyrugu sıfır, TanidikCihazGun 0–365 olabilir).";
            yield break;
        }
        if (AgBasarisizIzni >= HedefBasarisizIzni)
            yield return $"Kasa:HizSiniri:AgBasarisizIzni ({AgBasarisizIzni}) HedefBasarisizIzni'nden ({HedefBasarisizIzni}) küçük olmalıdır: tek ağ bir hedefin bütçesini tek başına tüketip hedefi herkese kilitleyememeli.";
        if (AgBasarisizIzni >= (long)SifreDogrulamaEszamanli + SifreDogrulamaKuyrugu)
            yield return $"Kasa:HizSiniri:AgBasarisizIzni ({AgBasarisizIzni}) SifreDogrulamaEszamanli + SifreDogrulamaKuyrugu toplamından ({(long)SifreDogrulamaEszamanli + SifreDogrulamaKuyrugu}) küçük olmalıdır: tek ağ şifre doğrulama kuyruğunu tek başına dolduramamalı.";
    }
}

/// <summary>Başlangıç doğrulaması: <see cref="HizSiniriAyarlari.Hatalar"/> boş değilse uygulama başlamaz.</summary>
internal sealed class HizSiniriDogrulayici : IValidateOptions<HizSiniriAyarlari>
{
    public ValidateOptionsResult Validate(string? name, HizSiniriAyarlari ayarlar)
        => ayarlar.Hatalar().ToList() is { Count: > 0 } hatalar ? ValidateOptionsResult.Fail(hatalar) : ValidateOptionsResult.Success;
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
    /// <summary>Elle yedek politikası (<see cref="ElleYedekPolitikasi"/>).</summary>
    public const string Yedek = "yedek";
    /// <summary>
    /// Loopback + Docker'ın varsayılan adres havuzları (172.17–172.31/16, ardından 192.168.0.0/16 içinden /20'lik
    /// ağlar): compose ağı hangi varsayılan havuzdan adres alırsa alsın gerçek istemci IP'si görülür. Konteyner
    /// yalnız 127.0.0.1:8080'e yayınlandığından bu adreslerden yalnız yerel vekil ve aynı ağdaki konteynerler
    /// bağlanabilir. Vekilsiz, doğrudan yerel ağa açılan bir kurulumda liste daraltılmalıdır.
    /// <para>Daraltma (önerilen sertleştirme): varsayılan liste, aynı Docker ağındaki ya da bu havuzlardan adres alan
    /// herhangi bir konteynerin X-Forwarded-For ile istediği istemci IP'sini bildirmesine izin verir. Böyle bir
    /// konteyner IP başına pencereleri ve ağ bütçesini her istekte başka IP bildirerek aşabilir; IP'den bağımsız
    /// hedef bütçesi ve tanıdık cihaz bütçesi yine uygulanır. Kalıcı çözüm Kasa:GuvenilirVekiller'i yalnız vekilin
    /// gerçek bağlantı adresine indirmektir (ör. "127.0.0.1/32;172.18.0.1/32": docker-proxy'nin bağlandığı köprü ağ
    /// geçidi); bunun için compose ağının alt ağı sabitlenmelidir, aksi halde ağ yeniden oluşunca geçit değişir ve
    /// bütün istemciler tek IP'ye düşer (VekilDurumu uyarısı bunu Ayarlar'da gösterir).</para>
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
        services.AddOptions<HizSiniriAyarlari>().BindConfiguration("Kasa:HizSiniri").ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<HizSiniriAyarlari>, HizSiniriDogrulayici>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<VekilDurumu>();
        services.AddSingleton<GirisSiniri>();
        services.AddSingleton<TanidikCihaz>();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (baglam, _) => new ValueTask(Red(baglam.HttpContext, baglam.Lease).ExecuteAsync(baglam.HttpContext));
            // Bölümleme ForwardedHeaders sonrası RemoteIpAddress'e göredir; politika adları ayrı kova tutar.
            o.AddPolicy(Guvenlik, http => IpBolumu(http, a => a.GuvenlikIzni));
            o.AddPolicy(Giris, http => IpBolumu(http, a => a.GirisIpIzni));
            o.AddPolicy(Yedek, new ElleYedekPolitikasi());
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
        => Red(http, lease.TryGetMetadata(MetadataName.RetryAfter, out var sure) ? sure : null);

    /// <summary>429 yanıtı: Türkçe 'hata' gövdesi ve (biliniyorsa) saniye cinsinden Retry-After.</summary>
    public static IResult Red(HttpContext http, TimeSpan? bekleme)
    {
        var mesaj = "Çok fazla deneme yapıldı. Birkaç dakika sonra yeniden deneyin.";
        if (bekleme is { } sure && sure > TimeSpan.Zero)
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
/// <item>Başarısız deneme bütçeleri şifre doğrulanmadan ÖNCE ayrılır (<see cref="Baslat"/>): ağın ve hedefin
/// kovalarından birer izin tek kilit altında ya hep ya hiç alınır ve doğrulama bitene kadar sayılır. Denetim ile
/// harcama arasında yarış yoktur: tek ağdan eşzamanlı patlamada ağ bütçesinden, dağıtık patlamada hedef
/// bütçesinden fazlası PBKDF2'ye ulaşmaz. Başarılı girişte ayrılan izin iade edilir: bütçeler "başarısız deneme"
/// bütçesidir, aynı ağdan giren meşru kullanıcılar birbirinin bütçesini tüketmez; iade yalnız doğru şifreyle
/// alınır, kaba kuvvet denemelerinin hepsi sayılır. Sonuç bildirilmeden kapanan deneme (doğrulama kuyruğu dolu,
/// istek iptal edildi) şifre denenmediği için iade edilir.</item>
/// <item>Hedef bütçesi IP'den bağımsızdır: dağıtık kaba kuvvet IP sayısıyla çoğalamaz. Editörün kendi bütçesi,
/// editör dışındaki bütün adların (her alıcı ve izleyici şifresi) tek ortak bütçesi vardır: izleyici şifresine
/// kaba kuvvet kullanıcı adı döndürerek çoğalamaz ve 401/429 yanıtı yalnız bu ortak bütçeye ve istemcinin ağına
/// bağlı olduğundan bir adın alıcıya ait olup olmadığını ele vermez (ayrı alıcı kovası ve kovaların farklı
/// anlarda biten pencereleri bu farkı sızdırıyordu). Bedeli: bir alıcıya dağıtık saldırı, kilitten muaf olmayan
/// istemcilerden bütün alıcı ve izleyici girişlerini pencere boyunca kilitler; bu zaten rastgele adlarla da
/// yapılabiliyordu.</item>
/// <item>Hedef kilidinden yalnız geçerli bir tanıdık cihaz belirteci (<see cref="TanidikCihaz"/>) muaf tutar: dağıtık
/// saldırgan meşru kullanıcıyı kendi cihazından kilitleyemez. Muafiyet ağa değil cihaza bağlıdır (aynı NAT ya da
/// operatör ağındaki başka biri yararlanamaz) ve kalıcıdır (yeniden başlatmada kaybolmaz). Muaf deneme hedef
/// bütçesi yerine cihaz başına bir bütçeden (ağ bütçesi kadar) ayrılır; başarısızlığı hedef bütçesine de yazılır.</item>
/// <item>Ağ başına, bütün hedefler için ortak ve hedef bütçesinden küçük başarısız deneme bütçesi (ağ: IPv4 adresi
/// ya da IPv6 /48 bloğu). Her zaman uygulanır, tanıdık cihaza da: tek ağ bir hedefin bütçesini tek başına
/// tüketemez, muaf istemci de sınırsız deneyemez; ortak olduğu için kilitlenen ağın yanıtları adları ayırt ettirmez.</item>
/// <item>Eşzamanlı şifre doğrulaması (PBKDF2) sınırlı ve kısa kuyruklu: giriş selinin CPU tüketimi uygulamanın geri
/// kalanını yavaşlatamaz. Ağ bütçesi doğrulama kapasitesinden küçük olduğundan tek ağ kuyruğu tek başına dolduramaz.</item>
/// </list>
/// Kalıntı riskler (bilerek kabul edilen):
/// <list type="bullet">
/// <item>Sayaçlar süreç belleğindedir: yeniden başlatma pencereleri sıfırlar (saldırgan yeniden başlatmayı
/// tetikleyemez; en kötü durumda bir pencere kadar ek deneme kazanır). Tek örnekli dağıtım varsayılır; birden çok
/// örnek her biri kendi bütçesini tutar.</item>
/// <item>Hedef kilidi bir erişilebilirlik bedelidir: çok ağlı saldırgan, tanıdık cihazı olmayan (yeni cihaz,
/// silinmiş çerez, ilk kez giren) meşru kullanıcıyı pencere boyunca (15 dk) dışarıda bırakabilir. Açık oturumlar
/// (30 günlük çerez/JWT) ve tanıdık cihazlar etkilenmez; kilit bir kez uyarı olarak loglanır.</item>
/// <item>Ağ bütçesi tanıdık cihaza da uygulanır: operatör NAT'ı (CGNAT) ya da ortak ofis IP'si paylaşan biri o
/// ağın bütçesini tüketirse aynı IP'deki meşru kullanıcılar da pencere boyunca 429 alır.</item>
/// <item>İstemci IP'si güvenilen vekilin X-Forwarded-For bildirimine dayanır; varsayılan güvenilen ağlar geniştir
/// (<see cref="HizSinirlari.VarsayilanGuvenilirVekiller"/>, daraltma orada anlatılır). IP'den bağımsız hedef ve
/// cihaz bütçeleri IP bildirimi sahte olsa da geçerlidir.</item>
/// <item>Zamanlama: izleyici şifresi tanımlı değilse alıcı olmayan adlarda PBKDF2 çalışmaz; yanıt süresi farkı
/// adın varlığını sızdırabilir (401/429 farkı sızdırmaz).</item>
/// </list>
/// </summary>
public sealed class GirisSiniri : IDisposable
{
    public const string EditorHedefi = "editor";
    public const string IzleyiciHedefi = "izleyici";
    public static string AliciHedefi(string kullanici) => "alici:" + kullanici;
    /// <summary>Editör dışındaki bütün adların (alıcılar ve izleyici şifresi) ortak başarısız deneme bütçesi.</summary>
    public const string EditorDisiButcesi = "editor-disi";

    private const string AgOnEki = "ag\n";
    private const string CihazOnEki = "cihaz\n";
    private readonly PartitionedRateLimiter<string> _pencere;
    private readonly DenemeButcesi _butce;
    private readonly ConcurrencyLimiter _dogrulama;
    private readonly IOptionsMonitor<HizSiniriAyarlari> _ayarlar;
    private readonly ILogger<GirisSiniri> _log;

    public GirisSiniri(IOptionsMonitor<HizSiniriAyarlari> ayarlar, TimeProvider saat, ILogger<GirisSiniri> log)
    {
        _ayarlar = ayarlar;
        _log = log;
        // İstek pencereleri: "ag\n<IPv6 /48>" ya da "<IP>\n<kullanıcı adı>".
        _pencere = PartitionedRateLimiter.Create<string, string>(anahtar => RateLimitPartition.GetFixedWindowLimiter(anahtar, _ =>
        {
            var a = ayarlar.CurrentValue;
            return new FixedWindowRateLimiterOptions
            {
                PermitLimit = anahtar.StartsWith(AgOnEki, StringComparison.Ordinal) ? a.GirisAgIzni : a.GirisKullaniciIzni,
                Window = TimeSpan.FromMinutes(a.PencereDakika),
                QueueLimit = 0,
            };
        }));
        _butce = new DenemeButcesi(saat);
        var baslangic = ayarlar.CurrentValue;
        _dogrulama = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = baslangic.SifreDogrulamaEszamanli,
            QueueLimit = baslangic.SifreDogrulamaKuyrugu,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

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

    /// <summary>Hedefin başarısız deneme bütçesi: editörün kendisi, diğer bütün adlar için tek ortak kova.</summary>
    public static string HedefButcesi(string hedef) => hedef == EditorHedefi ? EditorHedefi : EditorDisiButcesi;

    /// <summary>
    /// Şifre doğrulanmadan önce çağrılır. Ağın bütçesinden, ayrıca tanıdık cihaz kimliği (geçerli belirteç) yoksa
    /// hedefin bütçesinden, varsa cihazın bütçesinden birer izin ayırır; biri doluysa hiçbirine dokunmadan
    /// reddedilmiş deneme döner (Retry-After ile).
    /// </summary>
    public GirisDenemesi Baslat(string hedef, IPAddress? ip, string? tanidikCihaz)
    {
        var a = _ayarlar.CurrentValue;
        var pencere = TimeSpan.FromMinutes(a.HedefPencereDakika);
        var hedefKovasi = new DenemeButcesi.Kova(HedefButcesi(hedef), a.HedefBasarisizIzni, pencere);
        DenemeButcesi.Kova[] kovalar =
        [
            new(AgOnEki + Ag(ip), a.AgBasarisizIzni, pencere),
            tanidikCihaz is null ? hedefKovasi : new(CihazOnEki + tanidikCihaz, a.AgBasarisizIzni, pencere),
        ];
        var muaf = tanidikCihaz is not null;
        return _butce.Ayir(kovalar, out var ayrilanlar, out var bekleme)
            ? new GirisDenemesi(basarili => Sonuc(hedefKovasi, muaf, ayrilanlar, basarili), () => _butce.Iade(ayrilanlar))
            : new GirisDenemesi(bekleme);
    }

    private void Sonuc(DenemeButcesi.Kova hedefKovasi, bool muaf, DenemeButcesi.Ayrilan[] ayrilanlar, bool basarili)
    {
        if (basarili)
        {
            _butce.Iade(ayrilanlar);
            return;
        }
        // Tanıdık cihazın başarısızlığı da hedefin bütçesine yazılır: bütçe hedefe yapılan bütün denemeleri gösterir.
        if (muaf) _butce.Say(hedefKovasi);
        if (!_butce.IlkKezDoldu(hedefKovasi)) return;
        _log.LogWarning("Giriş bütçesi '{Butce}' için başarısız deneme sınırı doldu ({Izin} deneme / {Dakika} dk). Pencere bitene kadar kilitten muaf olmayan istemcilerin bu bütçeye bağlı adlarla girişi şifre denenmeden reddedilir; dağıtık bir kaba kuvvet denemesi olabilir.",
            hedefKovasi.Anahtar, hedefKovasi.Izin, hedefKovasi.Pencere.TotalMinutes);
    }

    /// <summary>Eşzamanlı PBKDF2 doğrulaması için izin; kuyruk doluysa hemen reddedilen lease döner.</summary>
    public ValueTask<RateLimitLease> DogrulamaIzniAsync(CancellationToken ct) => _dogrulama.AcquireAsync(1, ct);

    /// <summary>İzin bekleyen doğrulama sayısı.</summary>
    public long KuyruktakiDogrulama => _dogrulama.GetStatistics()?.CurrentQueuedCount ?? 0;

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
        _dogrulama.Dispose();
    }
}

/// <summary>
/// Şifre doğrulamasından önce bütçesi ayrılmış giriş denemesi. <see cref="Sonuc"/> doğrulamanın hemen ardından
/// (arada await olmadan) çağrılır: başarı ayrılan izni iade eder, başarısızlık harcanmış bırakır. Sonuç
/// bildirilmeden kapanan deneme (doğrulama kuyruğu dolu, istek iptal edildi) şifre denenmediği için iade edilir.
/// </summary>
public sealed class GirisDenemesi : IDisposable
{
    private readonly Action<bool>? _sonuc;
    private readonly Action? _iade;
    private int _kapandi;

    internal GirisDenemesi(Action<bool> sonuc, Action iade)
    {
        _sonuc = sonuc;
        _iade = iade;
    }

    internal GirisDenemesi(TimeSpan bekleme)
    {
        RedSuresi = bekleme;
        _kapandi = 1;
    }

    /// <summary>Bütçe doluysa bekleme süresi (şifre denenmeden 429 verilir); null ise deneme ayrıldı.</summary>
    public TimeSpan? RedSuresi { get; }

    public void Sonuc(bool basarili)
    {
        if (Interlocked.Exchange(ref _kapandi, 1) == 0) _sonuc!(basarili);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _kapandi, 1) == 0) _iade!();
    }
}

/// <summary>
/// Başarısız deneme bütçeleri: anahtar başına sabit pencereli sayaç (pencere ilk denemeyle başlar, saat DI'daki
/// <see cref="TimeProvider"/>'dan okunur). Birden çok kova tek kilit altında ya hep ya hiç ayrılır; ayrılan izin
/// yalnız ayrıldığı pencere sürerken iade edilir (yeni pencerenin sayacına dokunmaz).
/// </summary>
internal sealed class DenemeButcesi(TimeProvider saat)
{
    internal readonly record struct Kova(string Anahtar, int Izin, TimeSpan Pencere);
    internal readonly record struct Ayrilan(string Anahtar, DateTimeOffset Bitis);

    private sealed class Sayac
    {
        public DateTimeOffset Bitis;
        public int Deger;
        public bool Uyarildi;
    }

    private const int TemizlikEsigi = 1024;
    private readonly Lock _kilit = new();
    private readonly Dictionary<string, Sayac> _sayaclar = new(StringComparer.Ordinal);
    private DateTimeOffset _sonTemizlik;

    /// <summary>Kovaların hepsinde yer varsa her birinden bir izin ayırır; biri doluysa hiçbirine dokunmaz ve en uzun bekleme süresini döner.</summary>
    public bool Ayir(IReadOnlyList<Kova> kovalar, out Ayrilan[] ayrilanlar, out TimeSpan bekleme)
    {
        lock (_kilit)
        {
            var simdi = saat.GetUtcNow();
            Temizle(simdi);
            bekleme = TimeSpan.Zero;
            var sayaclar = new Sayac[kovalar.Count];
            for (var i = 0; i < kovalar.Count; i++)
            {
                sayaclar[i] = Guncel(kovalar[i], simdi);
                if (sayaclar[i].Deger >= kovalar[i].Izin && sayaclar[i].Bitis - simdi > bekleme) bekleme = sayaclar[i].Bitis - simdi;
            }
            if (bekleme > TimeSpan.Zero)
            {
                ayrilanlar = [];
                return false;
            }
            ayrilanlar = new Ayrilan[kovalar.Count];
            for (var i = 0; i < kovalar.Count; i++)
            {
                sayaclar[i].Deger++;
                ayrilanlar[i] = new(kovalar[i].Anahtar, sayaclar[i].Bitis);
            }
            return true;
        }
    }

    /// <summary>Ayrılan izinleri, ayrıldıkları pencere sürüyorsa geri verir.</summary>
    public void Iade(IEnumerable<Ayrilan> ayrilanlar)
    {
        lock (_kilit)
            foreach (var a in ayrilanlar)
                if (_sayaclar.TryGetValue(a.Anahtar, out var s) && s.Bitis == a.Bitis && s.Deger > 0) s.Deger--;
    }

    /// <summary>Sınır denetlemeden bir deneme sayar (kilitten muaf istemcinin başarısızlığı için).</summary>
    public void Say(Kova kova)
    {
        lock (_kilit)
        {
            var s = Guncel(kova, saat.GetUtcNow());
            if (s.Deger < int.MaxValue) s.Deger++;
        }
    }

    /// <summary>Kova sınıra ulaştıysa ve bu pencerede henüz bildirilmediyse true (pencere başına bir kez).</summary>
    public bool IlkKezDoldu(Kova kova)
    {
        lock (_kilit)
        {
            if (!_sayaclar.TryGetValue(kova.Anahtar, out var s) || s.Bitis <= saat.GetUtcNow() || s.Deger < kova.Izin || s.Uyarildi) return false;
            s.Uyarildi = true;
            return true;
        }
    }

    private Sayac Guncel(Kova kova, DateTimeOffset simdi)
    {
        if (!_sayaclar.TryGetValue(kova.Anahtar, out var s))
            _sayaclar[kova.Anahtar] = s = new Sayac { Bitis = simdi + kova.Pencere };
        else if (s.Bitis <= simdi)
        {
            s.Bitis = simdi + kova.Pencere;
            s.Deger = 0;
            s.Uyarildi = false;
        }
        return s;
    }

    // Süresi dolan kovalar (ör. saldırganın tek seferlik ağları) aralıkla atılır: sözlük bir pencerede görülen ağ sayısıyla sınırlı kalır.
    private void Temizle(DateTimeOffset simdi)
    {
        if (_sayaclar.Count < TemizlikEsigi || simdi - _sonTemizlik < TimeSpan.FromMinutes(1)) return;
        _sonTemizlik = simdi;
        foreach (var (anahtar, s) in _sayaclar)
            if (s.Bitis <= simdi) _sayaclar.Remove(anahtar);
    }
}
