using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Kasa.Api.Auth;

/// <summary>
/// Güvenlik olayları merkezi denetim tablosuna (DenetimOlaylari, Varlik 'Oturum') gerçek istemci IP'siyle yazılır ve
/// 'Kasa.Guvenlik' kategorisinde loglanır (başarısız/ret Warning, başarılı Information). Olaya yalnız bilinen bir hesabın
/// (editör ya da alıcı) normalize kullanıcı adı ve sonuç ayrıntısı girer: bilinmeyen ad <see cref="BilinmeyenAd"/> olarak
/// yazılır (ad alanına yanlışlıkla girilen parola olaya ve loga düşmez); parola, kurtarma kodu, oturum belirteci ve tanıdık
/// cihaz belirteci hiçbir alana yazılmaz; isteğin gerekçe başlığı da yazılmaz. Giriş, hız sınırı ve başarısız denemede yazma
/// hatası isteği bozmaz (loglanır); şifre değişimi, kurtarma kodu üretimi ve kurtarmada olay değişiklikle aynı transaction'dadır,
/// yazılamazsa değişiklik geri alınır (<c>zorunlu</c>). Türler: GirisBasarili, GirisBasarisiz,
/// HizSiniri, GirisYogun (şifre doğrulama kuyruğu dolu: sunucu yoğun, saldırı reddi değil), KurtarmaKullanildi,
/// KurtarmaBasarisiz, SifreDegisti, SifreDegistirmeBasarisiz, KurtarmaKoduUretildi, KurtarmaKoduUretimiBasarisiz.
/// İzleyici şifresi değişimi ve alıcı şifre/oturum iptalleri ilgili kaydın değişikliğiyle aynı tabloya yazılır
/// (IzleyiciSifresiDegisti, AliciSifresiDegisti, AliciOturumlariKapatildi; bkz. DenetimYakalayici). Geçersiz oturum
/// belirteci ve yetki reddi (403) yalnız seyrek loglanır (<see cref="AddKasaGuvenlikLoglari"/>).
/// </summary>
public static class GuvenlikOlaylari
{
    public const string Varlik = "Oturum";
    public const string GirisBasarili = "GirisBasarili";
    public const string GirisBasarisiz = "GirisBasarisiz";
    public const string HizSiniri = "HizSiniri";
    public const string GirisYogun = "GirisYogun";
    public const string KurtarmaKullanildi = "KurtarmaKullanildi";
    public const string KurtarmaBasarisiz = "KurtarmaBasarisiz";
    public const string SifreDegisti = "SifreDegisti";
    public const string SifreDegistirmeBasarisiz = "SifreDegistirmeBasarisiz";
    public const string KurtarmaKoduUretildi = "KurtarmaKoduUretildi";
    public const string KurtarmaKoduUretimiBasarisiz = "KurtarmaKoduUretimiBasarisiz";
    /// <summary>Yalnız loglanan (olay tablosuna yazılmayan) kimlik doğrulama retleri.</summary>
    public const string GecersizBelirtec = "GecersizBelirtec";
    public const string YetkiReddi = "YetkiReddi";
    /// <summary>Hiçbir hesaba ait olmayan kullanıcı adının olaydaki ve logdaki yeri.</summary>
    public const string BilinmeyenAd = "(bilinmeyen ad)";

    private static readonly HashSet<string> Olumsuz = [GirisBasarisiz, HizSiniri, GirisYogun, KurtarmaBasarisiz, SifreDegistirmeBasarisiz, KurtarmaKoduUretimiBasarisiz];

    /// <summary>Olayı isteğin bağlamında yazar: açık transaction varsa onunla (çağıran commit eder), yoksa hemen. İsteğin
    /// gerekçe başlığı (<see cref="DenetimBaglami.GerekceBasligi"/>) güvenlik olayına yazılmaz.</summary>
    /// <param name="kullanici">Denenen ya da oturumdaki kullanıcı adı (<see cref="YazilacakAd"/>).</param>
    /// <param name="ayrinti">Parola/kod/belirteç içermeyen ek bilgi (ör. hız sınırı politikası ve uç).</param>
    /// <param name="aktor">Başarılı girişte oturumu açılan rol ve alıcı kimliği; verilmezse isteğin kimliği.</param>
    /// <param name="zorunlu">Olay, çağıranın aynı transaction'daki değişikliğinin (şifre, kurtarma kodu) tek izidir: yazılamazsa
    /// hata loglanır ve yeniden fırlatılır, çağıran commit etmez (değişiklik geri alınır). Kapalıyken (başarısız deneme,
    /// giriş, hız sınırı) yazma hatası loglanır, isteğin sonucu değişmez.</param>
    public static void Yaz(HttpContext http, KasaDbContext db, string tur, string? kullanici = null, object? ayrinti = null,
        (string Rol, int? Id)? aktor = null, string? varlikId = null, bool zorunlu = false)
    {
        var log = Log(http);
        var ip = DenetimBaglami.Ip(http);
        string? ad = null;
        try
        {
            ad = kullanici is null ? null : YazilacakAd(http, db, kullanici);
            var istek = DenetimBaglami.Aktor(http);
            DenetimYazici.Yaz(db, new DenetimOlayi(tur, Varlik, varlikId, null,
                DenetimYazici.Json(new { kullanici = ad, uc = $"{http.Request.Method} {http.Request.Path}", ayrinti }),
                Aktor: aktor is { } a ? istek with { Rol = a.Rol, Id = a.Id } : istek, BaslikGerekcesi: false));
        }
        catch (Exception e)
        {
            // Ad çözülemediyse (veritabanı hatası) logda da düz metin yer almaz.
            if (ad is null && !string.IsNullOrWhiteSpace(kullanici)) ad = BilinmeyenAd;
            if (zorunlu)
            {
                log.LogError(e, "Güvenlik olayı ({Tur}) denetim kaydına yazılamadı; değişiklik geri alınıyor.", tur);
                throw;
            }
            log.LogError(e, "Güvenlik olayı ({Tur}) denetim kaydına yazılamadı; istek etkilenmedi.", tur);
        }
        if (Olumsuz.Contains(tur)) log.LogWarning("Güvenlik olayı {Tur}: kullanıcı {Kullanici}, istemci {Ip}.", tur, ad ?? "-", ip ?? "-");
        else log.LogInformation("Güvenlik olayı {Tur}: kullanıcı {Kullanici}, istemci {Ip}.", tur, ad ?? "-", ip ?? "-");
    }

    /// <summary>
    /// Olaya ve loga yazılacak kullanıcı adı: normalize ad (kırpılmış, küçük harf, en çok 64 karakter) yalnız bilinen bir
    /// hesaba, editöre ya da bir alıcıya aitse yazılır; başka her ad <see cref="BilinmeyenAd"/> olur. Kullanıcı adı alanına
    /// yanlışlıkla yazılan parola böylece düz metin olarak hiçbir yere düşmez; bedeli, var olmayan adlarla yapılan
    /// denemelerin hangi adı denediğinin görünmemesidir (IP, zaman ve sayı görünür). Boş ad (izleyici şifresi adsız
    /// denenir) yazılmaz.
    /// </summary>
    internal static string? YazilacakAd(HttpContext http, KasaDbContext db, string kullanici)
    {
        var ad = GirisSiniri.Normalize(kullanici);
        if (ad.Length == 0) return null;
        var editor = http.RequestServices.GetService<IConfiguration>()?["Kasa:EditorKullanici"];
        if (!string.IsNullOrEmpty(editor) && GirisSiniri.Normalize(editor) == ad) return ad;
        return db.Alicilar.AsNoTracking().Any(a => a.Kullanici == ad) ? ad : BilinmeyenAd;
    }

    /// <summary>Hız sınırı reddi: saldırı sırasında her ret satır üretmesin diye (tür, istemci ağı, politika, uç) başına
    /// dakikada en çok bir olay yazılır; log da aynı sıklıkta.</summary>
    public static void HizSiniriYaz(HttpContext http, string? politika, string? kullanici = null)
    {
        if (!Seyrek(http, $"{HizSiniri}\n{HizSinirlari.IstemciAnahtari(http.Connection.RemoteIpAddress)}\n{politika}\n{http.Request.Path}")) return;
        Yaz(http, http.RequestServices.GetRequiredService<KasaDbContext>(), HizSiniri, kullanici, new { politika });
    }

    /// <summary>Şifre doğrulama kuyruğu dolu (<see cref="HizSinirlari.Yogun"/>): sunucu yoğundur, istemci reddedilmemiştir.
    /// Hız sınırı reddiyle karışmasın diye ayrı türle, (istemci ağı, uç) başına dakikada en çok bir kez yazılır.</summary>
    public static void YogunYaz(HttpContext http, string? kullanici = null)
    {
        if (!Seyrek(http, $"{GirisYogun}\n{HizSinirlari.IstemciAnahtari(http.Connection.RemoteIpAddress)}\n{http.Request.Path}")) return;
        Yaz(http, http.RequestServices.GetRequiredService<KasaDbContext>(), GirisYogun, kullanici, new { kuyruk = "sifre-dogrulama" });
    }

    /// <summary>
    /// Kimlik doğrulama retleri 'Kasa.Guvenlik' logunda: geçersiz oturum belirteci (imza ya da biçim hatası Warning; süresi
    /// dolmuş belirteç olağandır, Information) ve oturumu olan ama rolü yetmeyen istek (403, Warning). Olay tablosuna
    /// yazılmaz: her istekte bir yazım olmasın. (tür, istemci ağı) başına dakikada en çok bir kez loglanır. Varsayılan
    /// 'Microsoft.AspNetCore: Warning' ayarı JwtBearer ve yetkilendirme kayıtlarını düşürdüğünden bu retler başka yerde
    /// görünmez. Logda yalnız istisna türü, rol, istemci IP'si ve uç (sorgu dizesi olmadan) yer alır; belirteç yazılmaz.
    /// Program.cs'in JwtBearer olaylarını sarar (sonradan yapılandırma: onlardan sonra kurulur, onları da çağırır).
    /// </summary>
    internal static IServiceCollection AddKasaGuvenlikLoglari(this IServiceCollection services)
    {
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).PostConfigure(o =>
        {
            var olaylar = o.Events;
            var dogrulamaHatasi = olaylar.OnAuthenticationFailed;
            olaylar.OnAuthenticationFailed = ctx => { GecersizBelirtecLogla(ctx.HttpContext, ctx.Exception); return dogrulamaHatasi(ctx); };
            var yasak = olaylar.OnForbidden;
            olaylar.OnForbidden = ctx => { YetkiReddiLogla(ctx.HttpContext); return yasak(ctx); };
        });
        return services;
    }

    private static void GecersizBelirtecLogla(HttpContext http, Exception? hata)
    {
        var suresiDolmus = hata is SecurityTokenExpiredException;
        if (!Seyrek(http, $"{GecersizBelirtec}\n{suresiDolmus}\n{HizSinirlari.IstemciAnahtari(http.Connection.RemoteIpAddress)}")) return;
        var (ip, uc) = (DenetimBaglami.Ip(http) ?? "-", $"{http.Request.Method} {http.Request.Path}");
        if (suresiDolmus) Log(http).LogInformation("Güvenlik olayı {Tur}: süresi dolmuş oturum belirteci, istemci {Ip}, uç {Uc}.", GecersizBelirtec, ip, uc);
        else Log(http).LogWarning("Güvenlik olayı {Tur}: istemci {Ip}, uç {Uc}, neden {Neden}.", GecersizBelirtec, ip, uc, hata?.GetType().Name ?? "-");
    }

    private static void YetkiReddiLogla(HttpContext http)
    {
        if (!Seyrek(http, $"{YetkiReddi}\n{HizSinirlari.IstemciAnahtari(http.Connection.RemoteIpAddress)}")) return;
        var aktor = DenetimBaglami.Aktor(http);
        Log(http).LogWarning("Güvenlik olayı {Tur}: rol {Rol}, istemci {Ip}, uç {Uc}.", YetkiReddi,
            aktor.Id is { } id ? $"{aktor.Rol} #{id}" : aktor.Rol, aktor.Ip ?? "-", $"{http.Request.Method} {http.Request.Path}");
    }

    private static ILogger Log(HttpContext http) => http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Kasa.Guvenlik");

    private static bool Seyrek(HttpContext http, string anahtar)
    {
        var sinir = http.RequestServices.GetService<GuvenlikOlayiSiniri>();
        var saat = http.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        return sinir is null || sinir.Yazilsin(anahtar, saat.GetUtcNow());
    }
}

/// <summary>Seyrek yazılan güvenlik olaylarının ve loglarının yazım sıklığı (uygulama başına, bellekte): anahtar başına
/// dakikada bir.</summary>
public sealed class GuvenlikOlayiSiniri
{
    private static readonly TimeSpan Aralik = TimeSpan.FromMinutes(1);
    private readonly Lock _kilit = new();
    private readonly Dictionary<string, DateTimeOffset> _son = new(StringComparer.Ordinal);

    public bool Yazilsin(string anahtar, DateTimeOffset simdi)
    {
        lock (_kilit)
        {
            if (_son.TryGetValue(anahtar, out var son) && simdi - son < Aralik) return false;
            // Sözlük saldırıda görülen ağ sayısıyla büyümesin: dolunca süresi geçenler atılır.
            if (_son.Count >= 1024) foreach (var (k, t) in _son) if (simdi - t >= Aralik) _son.Remove(k);
            _son[anahtar] = simdi;
            return true;
        }
    }
}
