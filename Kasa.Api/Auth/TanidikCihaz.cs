using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kasa.Api.Auth;

/// <summary>
/// Kalıcı "tanıdık cihaz" belirteci. Durumsuzdur (sunucuda kayıt ve şema değişikliği yoktur), bu yüzden süreç
/// yeniden başlasa da geçerli kalır.
/// <para>Ömür oturumdan bağımsız ve daha uzundur: TanidikCihazGun (varsayılan 180) başlangıçta oturum ömründen
/// (<see cref="JwtYardimci.OturumGun"/>) uzun olmak zorundadır; yeniden girişin en sık nedeni olan oturum sonunda
/// belirteç hâlâ geçerlidir. Her başarılı girişte, her oturum doğrulamasında (GET /api/auth/me: web her sayfa
/// açılışında, masaüstü her açılışta çağırır) ve editörün kendi şifre değişikliği ya da kurtarmasında yeni damgayla
/// yenilenir; kullanan cihazda süre sürekli ileri kayar.</para>
/// <para>Biçim <c>c1.{sonKullanma}.{kimlik}.{imza}</c>: son kullanma Unix saniyesi, kimlik 16 rastgele bayt, imza
/// HMAC-SHA256. İmza anahtarı Kasa:JwtKey'den HKDF ile türetilir; JWT imzası ve oturum damgasıyla aynı anahtar
/// paylaşılmaz. İmza sürümü, hedefi (editör, alıcı, izleyici şifresi), son kullanmayı, kimliği ve hedefin güncel
/// oturum damgasını (<see cref="OturumDamgasi"/>) kapsar: şifre ya da oturum sürümü değişince damga, dolayısıyla
/// belirteç kendiliğinden geçersizleşir; belirteç başka hedefte işe yaramaz.</para>
/// <para>Taşıma: tarayıcıda rol başına ayrı, __Host- önekli, HttpOnly, Secure, SameSite=Strict çerez
/// (<see cref="CerezAdi"/>: betik okuyamaz, yalnız bu kökene ve yalnız HTTPS'te gider). Masaüstü istemcisi
/// belirteci giriş ve oturum doğrulaması yanıtının 'cihaz' alanından, gövdesiz yanıtlarda (şifre değişikliği,
/// kurtarma) X-Kasa-Cihaz yanıt başlığından alır, güvenli depoda rol başına saklar ve girişte saklı belirteçlerinin
/// hepsini (en çok <see cref="EnFazlaAday"/>) virgülle ayrılmış tek X-Kasa-Cihaz başlığıyla gönderir; sunucu yalnız
/// denenen hedefe ait olanı kabul eder. Tarayıcı isteğine belirteç gövdede ya da başlıkta verilmez.</para>
/// <para>Etkisi yalnız hedef kilidinden muafiyettir (<see cref="GirisSiniri.Baslat"/>): ağ bütçesi her zaman uygulanır
/// ve muaf denemeler cihaz başına (ağ bütçesi kadar) ayrı bir bütçeyle sınırlıdır; ele geçirilmiş bir belirteç dağıtık
/// denemeyi ağ sayısıyla çoğaltamaz. Doğru şifre yine gerekir. Oturum doğrulamasında verilmesi yetki genişletmez:
/// geçerli oturumu olan zaten içeridedir.</para>
/// <para>Kalıntı riskler: çıkış belirteci silmez (cihaz tanıdık kalır). Paylaşılan bilgisayarda her rolün son giren
/// hedefinin belirteci durur; o bilgisayarı kullanan biri hedef kilidine takılmadan ağ ve cihaz bütçesi kadar
/// deneyebilir. Aynı cihazda rol başına tek belirteç tutulur (erişilebilirlik bedeli): editör, izleyici ve alıcı
/// belirteçleri birbirini ezmez, ama aynı cihazdan giren ikinci alıcı ilkinin belirtecinin yerine geçer ve ilk
/// alıcı o cihazda muafiyetini kaybeder. Belirteç tek tek iptal edilemez; iptal için hedefin şifresi (ya da alıcının
/// oturum sürümü) değiştirilir veya Kasa:JwtKey döndürülür. Secure çerez düz HTTP'de saklanmaz (localhost dışında):
/// orada cihaz tanınmaz.</para>
/// </summary>
public sealed class TanidikCihaz
{
    public const string BaslikAdi = "X-Kasa-Cihaz";
    /// <summary>Girişte başlıkta değerlendirilen en çok belirteç sayısı (masaüstünün sakladığı rol sayısı).</summary>
    public const int EnFazlaAday = 3;
    private const string CerezOneki = "__Host-kasa_cihaz_";
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

    /// <summary>Hedefin oturum rolü: editör, izleyici şifresi ya da alıcı. Tarayıcı çerezi ve masaüstü deposu belirteci
    /// bu ada göre ayrı tutar; aynı cihazda farklı rollerin belirteçleri birbirini ezmez.</summary>
    public static string Rol(string hedef)
        => hedef == GirisSiniri.EditorHedefi ? "editor" : hedef == GirisSiniri.IzleyiciHedefi ? "viewer" : "alici";

    /// <summary>Hedefin tarayıcı çerezi: __Host-kasa_cihaz_{rol}.</summary>
    public static string CerezAdi(string hedef) => CerezOneki + Rol(hedef);

    /// <summary>
    /// İstekteki belirteçlerden (X-Kasa-Cihaz başlığındaki en çok <see cref="EnFazlaAday"/> aday, başlık yoksa hedefin
    /// çerezi) bu hedef ve damga için geçerli olanın cihaz kimliği; yoksa (yok, bozuk, süresi dolmuş, başka hedefin
    /// ya da eski damganın) null. Şifre doğrulamasından önce çağrılır.
    /// </summary>
    public string? Dogrula(HttpRequest istek, string hedef, string? damga)
    {
        var gun = _ayarlar.CurrentValue.TanidikCihazGun;
        if (gun <= 0 || damga is null) return null;
        var aday = 0;
        foreach (var deger in istek.Headers[BaslikAdi])
            foreach (var belirtec in (deger ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (++aday > EnFazlaAday) return null;
                if (Kimlik(belirtec, hedef, damga, gun) is { } kimlik) return kimlik;
            }
        return aday == 0 && istek.Cookies[CerezAdi(hedef)] is { } cerez ? Kimlik(cerez, hedef, damga, gun) : null;
    }

    private string? Kimlik(string belirtec, string hedef, string damga, int gun)
    {
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
    /// Başarılı girişte yeni belirteç üretir. Tarayıcı isteğinde (<see cref="TarayiciIstegi"/>) hedefin HttpOnly
    /// çerezine yazar ve null döner; masaüstü isteğinde gövdede dönecek belirteci verir. Özellik kapalıysa
    /// (TanidikCihazGun = 0) null.
    /// </summary>
    public string? Ver(HttpContext http, string hedef, string damga) => Ver(http, hedef, damga, !TarayiciIstegi(http.Request));

    /// <summary>
    /// Gövdesiz (204) yanıt veren uçlar için (editörün şifre değişikliği ve kurtarması): tarayıcıya çerez, masaüstüne
    /// X-Kasa-Cihaz yanıt başlığı. Şifre değişince eski belirteç düştüğünden işlemi yapan cihaz yeni damgayla tanıdık
    /// kalır; saldırı sürerken şifresini değiştiren editör kendi cihazından yeniden girer.
    /// </summary>
    public void GovdesizVer(HttpContext http, string hedef, string damga)
    {
        if (Ver(http, hedef, damga) is { } belirtec) http.Response.Headers[BaslikAdi] = belirtec;
    }

    /// <summary>
    /// Oturum doğrulamasında (GET /api/auth/me) oturumun hedefi için belirteci yeniler: bu uç her sayfa ve uygulama
    /// açılışında çağrıldığından kullanan cihazın belirteci hiç eskimez ve belirteç verilmeden önce açılmış oturumlar
    /// da tanıdık cihaz olur. Damga, doğrulanmış oturumun (JwtBearer güncel damgayla karşılaştırmıştır) damgasıdır.
    /// Masaüstü oturumu Bearer başlığıyla gelir ve tarayıcı işareti taşımaz; ona gövdede verilir. Çerezli (web)
    /// oturumda yalnız çerez yazılır (düz HTTP'de GET isteği tarayıcı işareti taşımasa da gövdeye düşmez).
    /// </summary>
    public string? OturumlaYenile(HttpContext http, KasaDbContext db)
    {
        var u = http.User;
        var damga = u.FindFirstValue(OturumDamgasi.ClaimAdi);
        var hedef = u.FindFirstValue(ClaimTypes.Role) switch
        {
            "editor" => GirisSiniri.EditorHedefi,
            "viewer" => GirisSiniri.IzleyiciHedefi,
            "alici" when int.TryParse(u.FindFirstValue("alici_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                => db.Alicilar.AsNoTracking().Where(a => a.Id == id).Select(a => a.Kullanici).FirstOrDefault() is { } kullanici
                    ? GirisSiniri.AliciHedefi(kullanici) : null,
            _ => null,
        };
        if (hedef is null || damga is null) return null;
        var masaustu = http.Request.Headers.ContainsKey("Authorization") && !TarayiciIstegi(http.Request);
        return Ver(http, hedef, damga, masaustu);
    }

    private string? Ver(HttpContext http, string hedef, string damga, bool masaustu)
    {
        var gun = _ayarlar.CurrentValue.TanidikCihazGun;
        if (gun <= 0) return null;
        var sonKullanma = (_saat.GetUtcNow().ToUnixTimeSeconds() + gun * 86_400L).ToString(CultureInfo.InvariantCulture);
        var kimlik = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var belirtec = $"{Surum}.{sonKullanma}.{kimlik}.{Imza(hedef, sonKullanma, kimlik, damga)}";
        if (masaustu) return belirtec;
        http.Response.Cookies.Append(CerezAdi(hedef), belirtec, new CookieOptions
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
