using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Kasa.Api.Auth;

/// <summary>
/// Kalıcı "tanıdık cihaz" belirteci. Başarılı girişte verilir ve her başarılı girişte yenilenir; durumsuzdur
/// (sunucuda kayıt ve şema değişikliği yoktur), bu yüzden süreç yeniden başlasa da geçerli kalır.
/// <para>Biçim <c>c1.{sonKullanma}.{kimlik}.{imza}</c>: son kullanma Unix saniyesi (TanidikCihazGun sonrası), kimlik
/// 16 rastgele bayt, imza HMAC-SHA256. İmza anahtarı Kasa:JwtKey'den HKDF ile türetilir; JWT imzası ve oturum
/// damgasıyla aynı anahtar paylaşılmaz. İmza sürümü, hedefi (editör, alıcı, izleyici şifresi), son kullanmayı,
/// kimliği ve hedefin güncel oturum damgasını (<see cref="OturumDamgasi"/>) kapsar: şifre ya da oturum sürümü
/// değişince damga, dolayısıyla belirteç kendiliğinden geçersizleşir; belirteç başka hedefte işe yaramaz.</para>
/// <para>Taşıma: tarayıcıda __Host- önekli, HttpOnly, Secure, SameSite=Strict çerez (betik okuyamaz, yalnız bu
/// kökene ve yalnız HTTPS'te gider). Masaüstü istemcisi belirteci yanıt gövdesindeki 'cihaz' alanından alır, güvenli
/// depoda saklar ve girişte X-Kasa-Cihaz başlığıyla gönderir. Tarayıcı isteğine belirteç gövdede verilmez.</para>
/// <para>Etkisi yalnız hedef kilidinden muafiyettir (<see cref="GirisSiniri.Baslat"/>): ağ bütçesi her zaman uygulanır
/// ve muaf denemeler cihaz başına (ağ bütçesi kadar) ayrı bir bütçeyle sınırlıdır; ele geçirilmiş bir belirteç dağıtık
/// denemeyi ağ sayısıyla çoğaltamaz. Doğru şifre yine gerekir.</para>
/// <para>Kalıntı riskler: çıkış belirteci silmez (cihaz tanıdık kalır). Paylaşılan bilgisayarda son giren hedefin
/// belirteci durur; o bilgisayarı kullanan biri hedef kilidine takılmadan ağ ve cihaz bütçesi kadar deneyebilir.
/// Belirteç tek tek iptal edilemez; iptal için hedefin şifresi (ya da alıcının oturum sürümü) değiştirilir veya
/// Kasa:JwtKey döndürülür. Secure çerez düz HTTP'de saklanmaz (localhost dışında): orada cihaz tanınmaz.</para>
/// </summary>
public sealed class TanidikCihaz
{
    public const string CerezAdi = "__Host-kasa_cihaz";
    public const string BaslikAdi = "X-Kasa-Cihaz";
    private const string Surum = "c1";
    private const int EnUzun = 256;
    private readonly byte[] _anahtar;
    private readonly TimeProvider _saat;
    private readonly IOptionsMonitor<HizSiniriAyarlari> _ayarlar;

    public TanidikCihaz(IConfiguration cfg, TimeProvider saat, IOptionsMonitor<HizSiniriAyarlari> ayarlar)
    {
        var jwtKey = cfg["Kasa:JwtKey"];
        if (string.IsNullOrEmpty(jwtKey)) throw new InvalidOperationException("Kasa:JwtKey yapılandırılmalıdır.");
        _anahtar = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwtKey), 32,
            info: Encoding.UTF8.GetBytes("kasa/tanidik-cihaz/v1"));
        _saat = saat;
        _ayarlar = ayarlar;
    }

    /// <summary>
    /// İstekteki belirteç (X-Kasa-Cihaz başlığı, yoksa çerez) bu hedef ve damga için geçerliyse cihaz kimliği;
    /// değilse (yok, bozuk, süresi dolmuş, başka hedefin ya da eski damganın) null. Şifre doğrulamasından önce çağrılır.
    /// </summary>
    public string? Dogrula(HttpRequest istek, string hedef, string? damga)
    {
        var gun = _ayarlar.CurrentValue.TanidikCihazGun;
        if (gun <= 0 || damga is null) return null;
        var belirtec = istek.Headers[BaslikAdi].ToString();
        if (belirtec.Length == 0) belirtec = istek.Cookies[CerezAdi] ?? "";
        if (belirtec.Length is 0 or > EnUzun) return null;
        var parcalar = belirtec.Split('.');
        if (parcalar.Length != 4 || parcalar[0] != Surum
            || !long.TryParse(parcalar[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sonKullanma)) return null;
        // Süre imzanın içindedir; ayrıca ayarın izin verdiğinden uzun ömürlü belirteç kabul edilmez.
        var kalan = sonKullanma - _saat.GetUtcNow().ToUnixTimeSeconds();
        if (kalan <= 0 || kalan > gun * 86_400L) return null;
        return OturumDamgasi.Esit(parcalar[3], Imza(hedef, parcalar[1], parcalar[2], damga)) ? parcalar[2] : null;
    }

    /// <summary>
    /// Başarılı girişte yeni belirteç üretir. Tarayıcı isteğinde HttpOnly çerez olarak yazar ve null döner;
    /// masaüstü isteğinde gövdede dönecek belirteci verir. Özellik kapalıysa (TanidikCihazGun = 0) null.
    /// </summary>
    public string? Ver(HttpContext http, string hedef, string damga)
    {
        var gun = _ayarlar.CurrentValue.TanidikCihazGun;
        if (gun <= 0) return null;
        var sonKullanma = (_saat.GetUtcNow().ToUnixTimeSeconds() + gun * 86_400L).ToString(CultureInfo.InvariantCulture);
        var kimlik = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var belirtec = $"{Surum}.{sonKullanma}.{kimlik}.{Imza(hedef, sonKullanma, kimlik, damga)}";
        if (!TarayiciIstegi(http.Request)) return belirtec;
        http.Response.Cookies.Append(CerezAdi, belirtec, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = TimeSpan.FromDays(gun),
            IsEssential = true,
        });
        return null;
    }

    /// <summary>Tarayıcılar aynı kökenli POST'ta Origin ya da Sec-Fetch-Site gönderir, masaüstü istemcisi göndermez
    /// (Program.cs'teki CSRF denetimiyle aynı ölçüt).</summary>
    public static bool TarayiciIstegi(HttpRequest istek) => istek.Headers.ContainsKey("Origin") || istek.Headers.ContainsKey("Sec-Fetch-Site");

    private string Imza(string hedef, string sonKullanma, string kimlik, string damga)
        => Base64Url.EncodeToString(HMACSHA256.HashData(_anahtar, Encoding.UTF8.GetBytes($"{Surum}\n{hedef}\n{sonKullanma}\n{kimlik}\n{damga}")));
}
