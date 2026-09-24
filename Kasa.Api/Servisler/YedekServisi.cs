using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// Günlük otomatik yedek: SQLite <c>VACUUM INTO</c> ile tutarlı bir kopya alır,
/// son <c>Kasa:YedekSakla</c> (varsayılan 30) dosyayı tutar. <c>Kasa:YedekKlasoru</c>
/// boşsa kapalıdır. Sunucu dışına kopya için deploy/README.md'ye bakın.
/// </summary>
public sealed class YedekServisi : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<YedekServisi> _log;
    private readonly string? _klasor;
    private readonly int _sakla;

    public YedekServisi(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<YedekServisi> log)
    {
        _scopes = scopes; _log = log;
        _klasor = cfg["Kasa:YedekKlasoru"];
        _sakla = cfg.GetValue("Kasa:YedekSakla", 30);
    }

    protected override async Task ExecuteAsync(CancellationToken iptal)
    {
        if (string.IsNullOrWhiteSpace(_klasor)) return;
        while (!iptal.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                var dosya = YedekAl(db, _klasor, Saat.Bugun(), _sakla);
                _log.LogInformation("Yedek alındı: {Dosya}", dosya);
            }
            catch (Exception ex) { _log.LogError(ex, "Yedek alınamadı"); }
            try { await Task.Delay(TimeSpan.FromHours(24), iptal); } catch (TaskCanceledException) { }
        }
    }

    /// <summary>Günün yedeğini alır (aynı gün tekrar çağrılırsa üzerine yazar) ve eskileri temizler.</summary>
    public static string YedekAl(KasaDbContext db, string klasor, DateOnly gun, int sakla)
    {
        Directory.CreateDirectory(klasor);
        var dosya = Path.Combine(klasor, $"kasa-{gun:yyyy-MM-dd}.db");
        if (File.Exists(dosya)) File.Delete(dosya);
        db.Database.ExecuteSql($"VACUUM INTO {dosya}");

        foreach (var eski in Directory.GetFiles(klasor, "kasa-*.db").OrderByDescending(f => f).Skip(Math.Max(1, sakla)))
            File.Delete(eski);
        return dosya;
    }
}
