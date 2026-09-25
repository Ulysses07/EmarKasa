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
public enum PushSonuc { Basarili, GeciciHata, KaliciHata, AbonelikBitti }
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

public sealed class WebPushGonderici(PushKimligi identity, IConfiguration cfg) : IPushGonderici, IDisposable
{
    // No automatic HTTP logging, redirects or hidden retries for subscription capability URLs.
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    public async Task<PushSonuc> Gonder(PushAbonelikEntity abonelik, PushIleti ileti, int ttl, CancellationToken ct)
    {
        if (!PushDogrulama.Endpoint(abonelik.Endpoint)) return PushSonuc.KaliciHata;
        var keys = identity.Get();
        if (keys is null) return PushSonuc.GeciciHata;
        var client = new PushServiceClient(http) { AutoRetryAfter = false, MaxRetriesAfter = 1 };
        using var authentication = new VapidAuthentication(keys.PublicKey, keys.PrivateKey)
        { Subject = cfg["Bildirim:Subject"] ?? "https://kasa.emarglobal.com" };
        var subscription = new PushSubscription { Endpoint = abonelik.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, abonelik.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, abonelik.Auth);
        var message = new PushMessage(JsonSerializer.Serialize(ileti, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
        { TimeToLive = Math.Clamp(ttl, 0, 86400), Topic = ileti.Tag, Urgency = PushMessageUrgency.Normal };
        try
        {
            await client.RequestPushMessageDeliveryAsync(subscription, message, authentication, ct);
            return PushSonuc.Basarili;
        }
        catch (PushServiceClientException e)
        {
            if (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return PushSonuc.AbonelikBitti;
            return (int)e.StatusCode >= 500 || e.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
                ? PushSonuc.GeciciHata : PushSonuc.KaliciHata;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return PushSonuc.GeciciHata; }
    }
    public void Dispose() => http.Dispose();
}
