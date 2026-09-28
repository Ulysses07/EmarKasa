using Kasa.Api.Data;
using Kasa.Api.Denetim;

namespace Kasa.Api.Auth;

/// <summary>
/// Güvenlik olayları merkezi denetim tablosuna (DenetimOlaylari, Varlik 'Oturum') gerçek istemci IP'siyle yazılır ve
/// 'Kasa.Guvenlik' kategorisinde loglanır (başarısız/ret Warning, başarılı Information). Olaya yalnız normalize kullanıcı
/// adı ve sonuç ayrıntısı girer: parola, kurtarma kodu, oturum belirteci ve tanıdık cihaz belirteci hiçbir alana yazılmaz.
/// Yazma hatası isteği bozmaz (loglanır). Türler: GirisBasarili, GirisBasarisiz, HizSiniri, KurtarmaKullanildi,
/// KurtarmaBasarisiz, SifreDegisti, SifreDegistirmeBasarisiz, KurtarmaKoduUretildi, KurtarmaKoduUretimiBasarisiz.
/// İzleyici şifresi değişimi ve alıcı şifre/oturum iptalleri ilgili kaydın değişikliğiyle aynı tabloya yazılır
/// (IzleyiciSifresiDegisti, AliciSifresiDegisti, AliciOturumlariKapatildi; bkz. DenetimYakalayici).
/// </summary>
public static class GuvenlikOlaylari
{
    public const string Varlik = "Oturum";
    public const string GirisBasarili = "GirisBasarili";
    public const string GirisBasarisiz = "GirisBasarisiz";
    public const string HizSiniri = "HizSiniri";
    public const string KurtarmaKullanildi = "KurtarmaKullanildi";
    public const string KurtarmaBasarisiz = "KurtarmaBasarisiz";
    public const string SifreDegisti = "SifreDegisti";
    public const string SifreDegistirmeBasarisiz = "SifreDegistirmeBasarisiz";
    public const string KurtarmaKoduUretildi = "KurtarmaKoduUretildi";
    public const string KurtarmaKoduUretimiBasarisiz = "KurtarmaKoduUretimiBasarisiz";

    private static readonly HashSet<string> Olumsuz = [GirisBasarisiz, HizSiniri, KurtarmaBasarisiz, SifreDegistirmeBasarisiz, KurtarmaKoduUretimiBasarisiz];

    /// <summary>Olayı isteğin bağlamında yazar: açık transaction varsa onunla (çağıran commit eder), yoksa hemen.</summary>
    /// <param name="kullanici">Denenen ya da oturumdaki kullanıcı adı (kırpılır, küçük harfe çevrilir, 64 karakterle sınırlanır).</param>
    /// <param name="ayrinti">Parola/kod/belirteç içermeyen ek bilgi (ör. hız sınırı politikası ve uç).</param>
    /// <param name="aktor">Başarılı girişte oturumu açılan rol ve alıcı kimliği; verilmezse isteğin kimliği.</param>
    public static void Yaz(HttpContext http, KasaDbContext db, string tur, string? kullanici = null, object? ayrinti = null,
        (string Rol, int? Id)? aktor = null, string? varlikId = null)
    {
        var log = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Kasa.Guvenlik");
        var ad = kullanici is null ? null : GirisSiniri.Normalize(kullanici);
        var ip = DenetimBaglami.Ip(http);
        try
        {
            var istek = DenetimBaglami.Aktor(http);
            DenetimYazici.Yaz(db, new DenetimOlayi(tur, Varlik, varlikId, null,
                DenetimYazici.Json(new { kullanici = ad, uc = $"{http.Request.Method} {http.Request.Path}", ayrinti }),
                Aktor: aktor is { } a ? istek with { Rol = a.Rol, Id = a.Id } : istek));
        }
        catch (Exception e)
        {
            log.LogError(e, "Güvenlik olayı ({Tur}) denetim kaydına yazılamadı; istek etkilenmedi.", tur);
        }
        if (Olumsuz.Contains(tur)) log.LogWarning("Güvenlik olayı {Tur}: kullanıcı {Kullanici}, istemci {Ip}.", tur, ad ?? "-", ip ?? "-");
        else log.LogInformation("Güvenlik olayı {Tur}: kullanıcı {Kullanici}, istemci {Ip}.", tur, ad ?? "-", ip ?? "-");
    }

    /// <summary>Hız sınırı reddi: saldırı sırasında her ret satır üretmesin diye (tür, istemci ağı, uç) başına dakikada
    /// en çok bir olay yazılır; log da aynı sıklıkta.</summary>
    public static void HizSiniriYaz(HttpContext http, string? politika, string? kullanici = null)
    {
        var anahtar = $"{HizSinirlari.IstemciAnahtari(http.Connection.RemoteIpAddress)}\n{politika}\n{http.Request.Path}";
        var sinir = http.RequestServices.GetService<GuvenlikOlayiSiniri>();
        var saat = http.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        if (sinir is not null && !sinir.Yazilsin(anahtar, saat.GetUtcNow())) return;
        Yaz(http, http.RequestServices.GetRequiredService<KasaDbContext>(), HizSiniri, kullanici, new { politika });
    }
}

/// <summary>Hız sınırı olaylarının yazım sıklığı (uygulama başına, bellekte): anahtar başına dakikada bir.</summary>
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
