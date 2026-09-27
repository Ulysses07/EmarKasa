using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Kasa.Api.Data;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Data.Sqlite;

namespace Kasa.Api.Servisler;

public record PushAnahtarlar(string PublicKey, string PrivateKey);
public record PushIleti(int Id, string Baslik, string Mesaj, string Url, string Tag);
/// <summary><see cref="YapilandirmaHatasi"/>: sunucunun bildirim anahtarı kullanılamıyor; abonelik suçlu değildir ve
/// düzelene kadar hiçbir cihaza gönderilemez (teslim deneme hakkı harcamadan bekler).</summary>
public enum PushSonuc { Basarili, GeciciHata, KaliciHata, AbonelikBitti, YapilandirmaHatasi }
public interface IPushGonderici
{
    Task<PushSonuc> Gonder(PushAbonelikEntity abonelik, PushIleti ileti, int ttl, CancellationToken ct);
}

public static class PushDogrulama
{
    // Subscriptions are user input, never a general-purpose server-side HTTP destination.
    public static bool Endpoint(string? value)
    {
        if (value is null || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath == "/" || IPAddress.TryParse(uri.Host, out _)) return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return host == "fcm.googleapis.com" || host == "web.push.apple.com"
            || host.EndsWith(".push.apple.com", StringComparison.Ordinal)
            || host.EndsWith(".push.services.mozilla.com", StringComparison.Ordinal)
            || host.EndsWith(".notify.windows.com", StringComparison.Ordinal);
    }

    public static byte[] Decode(string value)
    {
        if (value.Length > 256 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new FormatException();
        return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    }

    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool Anahtarlar(string? p256dh, string? auth)
    {
        try
        {
            if (p256dh is null || auth is null || Decode(auth).Length != 16) return false;
            var point = Decode(p256dh);
            if (point.Length != 65 || point[0] != 4) return false;
            using var key = ECDiffieHellman.Create(new ECParameters
            { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[1..33], Y = point[33..65] } });
            return true;
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException or PlatformNotSupportedException) { return false; }
    }
}

/// <summary>Stable VAPID identity lives beside the database and survives application releases.</summary>
public sealed class PushKimligi(IConfiguration cfg, IWebHostEnvironment environment)
{
    private readonly object gate = new();
    private PushAnahtarlar? keys;
    public bool Etkin => cfg.GetValue<bool?>("Bildirim:PushEtkin") ?? environment.IsProduction();
    public PushAnahtarlar? Get()
    {
        if (!Etkin) return null;
        lock (gate)
        {
            if (keys is not null) return keys;
            var publicKey = cfg["Bildirim:PublicKey"]; var privateKey = cfg["Bildirim:PrivateKey"];
            if (!string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(privateKey))
                return keys = new(publicKey, privateKey);
            var database = new SqliteConnectionStringBuilder(cfg.GetConnectionString("Kasa") ?? "Data Source=kasa.db").DataSource;
            var path = cfg["Bildirim:AnahtarDosyasi"] ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database))!, ".kasa-push-keys.json");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var p = ec.ExportParameters(true);
                var generated = new PushAnahtarlar(PushDogrulama.Encode([4, .. p.Q.X!, .. p.Q.Y!]), PushDogrulama.Encode(p.D!));
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(temp, options)) JsonSerializer.Serialize(stream, generated);
                try { File.Move(temp, path, false); }
                catch (IOException) when (File.Exists(path)) { File.Delete(temp); }
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return keys = JsonSerializer.Deserialize<PushAnahtarlar>(File.ReadAllText(path))
                ?? throw new InvalidOperationException("Bildirim anahtarı okunamadı.");
        }
    }
}

/// <summary>Web Push göndericisi. Sağlayıcının her yanıtı sınıflandırılır ve abonelik kimliği, sağlayıcı adı ve durum koduyla
/// loglanır (abonelik adresinin yetki taşıyan yolu yazılmaz; bkz. <see cref="SaglayiciYaniti"/>). <paramref name="saglayici"/>
/// yalnız testte verilir; üretimde yönlendirme izlemeyen varsayılan işleyici kullanılır.</summary>
public sealed class WebPushGonderici(PushKimligi identity, IConfiguration cfg, ILogger<WebPushGonderici> logger,
    HttpMessageHandler? saglayici = null) : IPushGonderici, IDisposable
{
    // No automatic HTTP logging, redirects or hidden retries for subscription capability URLs.
    private readonly HttpClient http = new(saglayici ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Sağlayıcının hata yanıtının sonucu ve log düzeyi. 404/410: abonelik sona ermiş (cihaz kapatılır, Information). 401/403:
    /// VAPID kimliği reddedildi — sunucu anahtarı ya da aboneliğin bağlı olduğu anahtar uyumsuz; yeniden denemek düzeltmez
    /// (Error). 413: ileti sağlayıcının boyut sınırını aşıyor (Error). 408/429/5xx: sağlayıcı geçici olarak yanıt veremiyor,
    /// teslim geri çekilerek yeniden denenir (Warning). Diğer 4xx: istek reddedildi, teslim kapatılır (Warning).
    /// </summary>
    public static (PushSonuc Sonuc, LogLevel Seviye, string Aciklama) SaglayiciYaniti(HttpStatusCode durum) => (int)durum switch
    {
        404 or 410 => (PushSonuc.AbonelikBitti, LogLevel.Information, "abonelik sağlayıcıda sona ermiş; cihaz kaydı kapatılıyor"),
        401 or 403 => (PushSonuc.KaliciHata, LogLevel.Error,
            "sağlayıcı sunucu kimliğini (VAPID) reddetti; sunucu anahtarı ya da aboneliğin bağlı olduğu anahtar uyumsuz, teslim kapatıldı"),
        413 => (PushSonuc.KaliciHata, LogLevel.Error, "ileti sağlayıcının boyut sınırını aşıyor; teslim kapatıldı"),
        408 or 429 or >= 500 => (PushSonuc.GeciciHata, LogLevel.Warning, "sağlayıcı geçici olarak yanıt veremiyor; teslim yeniden denenecek"),
        _ => (PushSonuc.KaliciHata, LogLevel.Warning, "sağlayıcı isteği reddetti; teslim kapatıldı"),
    };
    private string? bildirilenYapilandirmaHatasi;
    public async Task<PushSonuc> Gonder(PushAbonelikEntity abonelik, PushIleti ileti, int ttl, CancellationToken ct)
    {
        if (!PushDogrulama.Endpoint(abonelik.Endpoint)) return PushSonuc.KaliciHata;
        VapidAuthentication authentication;
        try
        {
            var keys = identity.Get();
            if (keys is null) return PushSonuc.GeciciHata;
            authentication = new VapidAuthentication(keys.PublicKey, keys.PrivateKey)
            { Subject = cfg["Bildirim:Subject"] ?? "https://kasa.emarglobal.com" };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Anahtar değeri, anahtar dosyası (JSON, izin, IO) ya da VAPID konusu: suçlu abonelik değil sunucu yapılandırması;
            // hiçbir cihaza gönderilemez. Her tur aynı hatayı üretir, yalnız ileti değiştiğinde bir kez loglanır.
            if (Interlocked.Exchange(ref bildirilenYapilandirmaHatasi, e.Message) != e.Message)
                logger.LogError(e, "Bildirim anahtarı veya yapılandırması kullanılamıyor; cihaz bildirimleri gönderilemiyor.");
            return PushSonuc.YapilandirmaHatasi;
        }
        using (authentication)
        {
            var client = new PushServiceClient(http) { AutoRetryAfter = false, MaxRetriesAfter = 1 };
            try
            {
                var subscription = new PushSubscription { Endpoint = abonelik.Endpoint };
                subscription.SetKey(PushEncryptionKeyName.P256DH, abonelik.P256dh);
                subscription.SetKey(PushEncryptionKeyName.Auth, abonelik.Auth);
                var message = new PushMessage(JsonSerializer.Serialize(ileti, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                { TimeToLive = Math.Clamp(ttl, 0, 86400), Topic = ileti.Tag, Urgency = PushMessageUrgency.Normal };
                await client.RequestPushMessageDeliveryAsync(subscription, message, authentication, ct);
                Interlocked.Exchange(ref bildirilenYapilandirmaHatasi, null);
                return PushSonuc.Basarili;
            }
            catch (PushServiceClientException e)
            {
                var (sonuc, seviye, aciklama) = SaglayiciYaniti(e.StatusCode);
                logger.Log(seviye, "Bildirim sağlayıcısı ({Saglayici}) abonelik #{AbonelikId} için {Durum} ({Neden}) döndü: {Aciklama}.",
                    Saglayici(abonelik), abonelik.Id, (int)e.StatusCode, e.Message, aciklama);
                return sonuc;
            }
            catch (HttpRequestException e)
            {
                logger.LogWarning(e, "Bildirim sağlayıcısına ({Saglayici}) ulaşılamadı (abonelik #{AbonelikId}); geçici hata sayıldı, teslim yeniden denenecek.",
                    Saglayici(abonelik), abonelik.Id);
                return PushSonuc.GeciciHata;
            }
            catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "Bildirim sağlayıcısı ({Saglayici}) süresinde yanıt vermedi (abonelik #{AbonelikId}); geçici hata sayıldı, teslim yeniden denenecek.",
                    Saglayici(abonelik), abonelik.Id);
                return PushSonuc.GeciciHata;
            }
            catch (Exception e) when (e is FormatException or ArgumentException or CryptographicException)
            {
                // Aboneliğin şifreleme anahtarı ya da iletinin kendisi kullanılamıyor: yeniden denemek düzeltmez.
                logger.LogError(e, "Abonelik #{AbonelikId} için bildirim hazırlanamadı; kalıcı hata sayıldı.", abonelik.Id);
                return PushSonuc.KaliciHata;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Sınıflandırılamayan hata: deneme sınırı (5) içinde geçici sayılır, istisnasıyla loglanır.
                logger.LogError(e, "Abonelik #{AbonelikId} için bildirim gönderilirken beklenmeyen hata; geçici hata sayıldı.", abonelik.Id);
                return PushSonuc.GeciciHata;
            }
        }
    }
    // Abonelik adresinin yolu cihaza özgü yetki taşır; loga yalnız sağlayıcının adı yazılır.
    private static string Saglayici(PushAbonelikEntity abonelik)
        => Uri.TryCreate(abonelik.Endpoint, UriKind.Absolute, out var uri) ? uri.IdnHost : "bilinmeyen";
    public void Dispose() => http.Dispose();
}
