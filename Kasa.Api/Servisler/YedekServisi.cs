using System.Text.RegularExpressions;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>Son başarılı yedeğin zamanı (/health bunu raporlar).</summary>
public sealed class YedekDurumu
{
    private long _sonBasariliTicks;

    public bool Etkin { get; init; }

    public DateTime? SonBasariliUtc
    {
        get { var t = Interlocked.Read(ref _sonBasariliTicks); return t == 0 ? null : new DateTime(t, DateTimeKind.Utc); }
        set => Interlocked.Exchange(ref _sonBasariliTicks, value?.ToUniversalTime().Ticks ?? 0);
    }

    public string? SonHata { get; set; }
}

/// <summary>
/// Günlük otomatik yedek: SQLite <c>VACUUM INTO</c> ile tutarlı bir kopya alır.
/// <list type="bullet">
/// <item>Önce geçici dosyaya yazar, başarılıysa atomik olarak <c>kasa-yyyy-MM-dd.db</c>'ye
///       taşır: başarısız bir yedek (ör. disk dolu) var olan günlük yedeği bozmaz.</item>
/// <item>Günün yedeği zaten varsa o gün yeniden alınmaz (aynı gün yeniden başlatma günün
///       ilk yedeğini ezmez; göç öncesi kopya zaten ayrıca <c>kasa-once-*.db</c> olarak alınır).</item>
/// <item>Saklama (<c>Kasa:YedekSakla</c>, varsayılan 30) yalnız <c>kasa-yyyy-MM-dd.db</c> adlı
///       dosyalara uygulanır; elle alınan ve göç öncesi yedekler silinmez, sayıya da girmez.</item>
/// <item>Uygulama açılışını bloke etmez: ilk yedek uygulama başladıktan sonra alınır.</item>
/// </list>
/// <c>Kasa:YedekKlasoru</c> boşsa kapalıdır. Sunucu dışına kopya için deploy/README.md'ye bakın.
/// </summary>
public sealed partial class YedekServisi : BackgroundService
{
    [GeneratedRegex(@"^kasa-\d{4}-\d{2}-\d{2}\.db$")]
    private static partial Regex GunlukYedekAdi();

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<YedekServisi> _log;
    private readonly IHostApplicationLifetime _omur;
    private readonly YedekDurumu _durum;
    private readonly string? _klasor;
    private readonly int _sakla;

    public YedekServisi(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<YedekServisi> log,
        IHostApplicationLifetime omur, YedekDurumu durum)
    {
        _scopes = scopes; _log = log; _omur = omur; _durum = durum;
        _klasor = cfg["Kasa:YedekKlasoru"];
        _sakla = cfg.GetValue("Kasa:YedekSakla", 30);
    }

    protected override async Task ExecuteAsync(CancellationToken iptal)
    {
        if (string.IsNullOrWhiteSpace(_klasor)) return;

        // Açılışı bekletme: uygulama isteklere hazır olduktan sonra başla.
        var basladi = new TaskCompletionSource();
        using (_omur.ApplicationStarted.Register(() => basladi.TrySetResult()))
        using (iptal.Register(() => basladi.TrySetCanceled()))
        {
            try { await basladi.Task; } catch (TaskCanceledException) { return; }
        }

        _durum.SonBasariliUtc ??= SonYedekZamani(_klasor);
        while (!iptal.IsCancellationRequested)
        {
            var gun = Saat.Bugun();
            if (!File.Exists(GunlukDosya(_klasor, gun)))
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                    var dosya = YedekAl(db, _klasor, gun, _sakla);
                    _durum.SonBasariliUtc = DateTime.UtcNow;
                    _durum.SonHata = null;
                    _log.LogInformation("Yedek alındı: {Dosya}", dosya);
                }
                catch (Exception ex)
                {
                    _durum.SonHata = ex.Message;
                    _log.LogError(ex, "Yedek alınamadı");
                }
            }
            // Saatlik kontrol: gün değişince yeni günün yedeği alınır; hata varsa bir saat sonra yeniden denenir.
            try { await Task.Delay(TimeSpan.FromHours(1), iptal); } catch (TaskCanceledException) { }
        }
    }

    public static string GunlukDosya(string klasor, DateOnly gun) => Path.Combine(klasor, $"kasa-{gun:yyyy-MM-dd}.db");

    /// <summary>Klasördeki en yeni günlük yedeğin yazılma zamanı (yoksa null).</summary>
    public static DateTime? SonYedekZamani(string klasor)
    {
        if (!Directory.Exists(klasor)) return null;
        return Directory.GetFiles(klasor)
            .Where(f => GunlukYedekAdi().IsMatch(Path.GetFileName(f)))
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty()
            .Max() is { Ticks: > 0 } t ? t : null;
    }

    /// <summary>
    /// Günün yedeğini alır (geçici dosyaya yazıp atomik adlandırma; varsa üzerine yazar)
    /// ve eski günlük yedekleri temizler.
    /// </summary>
    public static string YedekAl(KasaDbContext db, string klasor, DateOnly gun, int sakla)
    {
        Directory.CreateDirectory(klasor);
        var dosya = GunlukDosya(klasor, gun);
        var gecici = Path.Combine(klasor, $".{Path.GetFileName(dosya)}.{Guid.NewGuid():N}.tmp");
        try
        {
            db.Database.ExecuteSql($"VACUUM INTO {gecici}");
            File.Move(gecici, dosya, overwrite: true);
        }
        finally
        {
            if (File.Exists(gecici)) File.Delete(gecici);
        }

        foreach (var eski in Directory.GetFiles(klasor)
                     .Where(f => GunlukYedekAdi().IsMatch(Path.GetFileName(f)))
                     .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                     .Skip(Math.Max(1, sakla)))
            File.Delete(eski);

        // Çökmüş eski denemelerden kalan geçici dosyalar.
        foreach (var t in Directory.GetFiles(klasor, ".kasa-*.tmp"))
            if (File.GetLastWriteTimeUtc(t) < DateTime.UtcNow.AddHours(-6)) File.Delete(t);
        return dosya;
    }
}
