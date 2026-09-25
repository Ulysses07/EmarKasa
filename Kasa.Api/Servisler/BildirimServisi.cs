using System.Globalization;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

public interface IBildirimKaynaklari
{
    IReadOnlyList<TakipOlayDto> Oku(KasaDbContext db, DateOnly today);
}
public sealed class FinansBildirimKaynaklari : IBildirimKaynaklari
{
    public IReadOnlyList<TakipOlayDto> Oku(KasaDbContext db, DateOnly today) => FinansTakipServisi.GetNotificationEvents(db, today);
}

public record BildirimTaslagi(string Anahtar, string Baslik, string Mesaj, DateOnly Tarih, string Hedef, string Tur, int KaynakId);

public static class BildirimTakvimi
{
    public static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    public static DateTime Yerel(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Istanbul).DateTime;

    public static IReadOnlyList<BildirimTaslagi> Olustur(IEnumerable<TakipOlayDto> events, DateOnly today)
    {
        var result = new List<BildirimTaslagi>();
        foreach (var e in events)
        {
            var isCard = e.Kaynak == "Kart";
            if (e.Tur == "Kesim" && e.Tarih != today) continue;
            if (e.Tur != "Kesim" && e.Tarih != today && e.Tarih != today.AddDays(3)) continue;
            if (e.Tur != "Kesim" && e.Tutar <= 0) continue;
            var offset = e.Tarih.DayNumber - today.DayNumber;
            var key = $"{e.Kaynak}:{e.KaynakId}:{e.KalemId}:{e.Tur}:{e.Tarih:yyyy-MM-dd}:{offset}";
            var amount = e.Tutar.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " TL";
            var title = e.Tur == "Kesim" ? "Bugün hesap kesim günü" : offset == 3 ? "Ödemeye 3 gün kaldı" : "Bugün ödeme günü";
            var message = e.Tur == "Kesim"
                ? $"{e.Ad}: kayıtlı ekstre borcu {amount}. Kasadan ancak ödeme kaydettiğinde düşer."
                : isCard ? $"{e.Ad}: kalan ödeme {amount}. Son gün {e.Tarih:dd.MM.yyyy}."
                : $"{e.Ad}: taksit {amount}, {e.Tarih:dd.MM.yyyy}. " + (offset == 0
                    ? "Taksit bugün kasaya otomatik işlendi; bankadaki ödemeyi ayrıca kontrol et."
                    : "Taksit tarihinde ilgili kanal kasalarından otomatik düşecek.");
            result.Add(new(key, title, message, today, isCard ? $"/#cards/{e.KaynakId}" : $"/#loans/{e.KaynakId}", e.Tur, e.KaynakId));
        }
        return result;
    }
}

public sealed class BildirimServisi(KasaDbContext db, IBildirimKaynaklari sources, IPushGonderici sender,
    IConfiguration cfg, TimeProvider clock)
{
    private IReadOnlyList<BildirimTaslagi> Taslaklar(DateOnly today, bool yeniUyariEtkin) =>
        BildirimTakvimi.Olustur(sources.Oku(db, today), today)
            .Concat(KasaEsikServisi.Oku(db, today, yeniUyariEtkin)).ToList();

    public BildirimAyarEntity Ayarlar()
    {
        db.Database.ExecuteSqlRaw("INSERT OR IGNORE INTO BildirimAyarlari (Id,Etkin,Saat,Dakika,Surum) VALUES (1,1,9,0,1)");
        return db.Set<BildirimAyarEntity>().AsNoTracking().Single(x => x.Id == 1);
    }

    public async Task Yenile(CancellationToken ct = default)
    {
        var now = BildirimTakvimi.Yerel(clock.GetUtcNow());
        var today = DateOnly.FromDateTime(now); var settings = Ayarlar();
        var drafts = Taslaklar(today, settings.Etkin);
        var keys = drafts.Select(x => x.Anahtar).ToHashSet(StringComparer.Ordinal);
        // A changed due date, cancellation or full payment cancels any unsent reminder immediately.
        var todays = await db.Set<BildirimEntity>().Where(x => x.Tarih == today).ToListAsync(ct);
        foreach (var row in todays) row.Iptal = !keys.Contains(row.OlayAnahtari);
        await db.SaveChangesAsync(ct);
        if (!settings.Etkin || now.TimeOfDay < new TimeSpan(settings.Saat, settings.Dakika, 0)) return;
        foreach (var d in drafts)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Bildirimler (OlayAnahtari,Baslik,Mesaj,Tarih,Hedef,Tur,KaynakId,Okundu,Iptal)
                VALUES ({d.Anahtar},{d.Baslik},{d.Mesaj},{d.Tarih},{d.Hedef},{d.Tur},{d.KaynakId},0,0)
                ON CONFLICT(OlayAnahtari) DO UPDATE SET Baslik=excluded.Baslik,Mesaj=excluded.Mesaj,
                  Tarih=excluded.Tarih,Hedef=excluded.Hedef,Iptal=0
                WHERE Bildirimler.Okundu=0 AND NOT EXISTS
                  (SELECT 1 FROM BildirimTeslimler t WHERE t.BildirimId=Bildirimler.Id AND t.Gonderildi IS NOT NULL)
                """, ct);
        }
        db.ChangeTracker.Clear();
    }

    public async Task Gonder(CancellationToken ct = default)
    {
        await Yenile(ct);
        var settings = Ayarlar();
        if (!settings.Etkin) return;
        var instant = clock.GetUtcNow(); var queryAt = instant.ToUnixTimeSeconds();
        var local = BildirimTakvimi.Yerel(instant); var today = DateOnly.FromDateTime(local);
        if (local.TimeOfDay < new TimeSpan(settings.Saat, settings.Dakika, 0)) return;
        var stamp = OturumDamgasi.Uret("editor", cfg, db);
        var devices = await db.Set<PushAbonelikEntity>().Where(x => x.Etkin).AsNoTracking().ToListAsync(ct);
        foreach (var s in devices.Where(x => !OturumDamgasi.Esit(x.OturumDamgasi, stamp)))
            await db.Set<PushAbonelikEntity>().Where(x => x.Id == s.Id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Etkin, false), ct);
        devices = devices.Where(x => OturumDamgasi.Esit(x.OturumDamgasi, stamp)).ToList();
        var notifications = await db.Set<BildirimEntity>().Where(x => x.Tarih == today && !x.Iptal).AsNoTracking().ToListAsync(ct);
        foreach (var n in notifications)
        foreach (var s in devices)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT OR IGNORE INTO BildirimTeslimler (BildirimId,AbonelikId,Deneme,SonrakiDeneme,KilitBitis,Iptal)
                VALUES ({n.Id},{s.Id},0,0,0,0)
                """, ct);
        var ids = notifications.Select(x => x.Id).ToArray();
        var deliveries = await db.Set<BildirimTeslimEntity>().AsNoTracking()
            .Where(x => ids.Contains(x.BildirimId) && x.Gonderildi == null && !x.Iptal && x.Deneme < 5
                && x.SonrakiDeneme <= queryAt && x.KilitBitis <= queryAt).OrderBy(x => x.Id).Take(100).ToListAsync(ct);
        foreach (var d in deliveries)
        {
            ct.ThrowIfCancellationRequested();
            var attempt = clock.GetUtcNow(); var now = attempt.ToUnixTimeSeconds();
            var localAttempt = BildirimTakvimi.Yerel(attempt);
            if (DateOnly.FromDateTime(localAttempt) != today) break;
            var ttl = Math.Max(1, (int)(localAttempt.Date.AddDays(1) - localAttempt).TotalSeconds);
            var token = Guid.NewGuid().ToString("N");
            // Database lease gives multiple workers and process restarts the same single delivery record.
            var claimed = await db.Set<BildirimTeslimEntity>().Where(x => x.Id == d.Id && x.Gonderildi == null
                && !x.Iptal && x.KilitBitis <= now && x.SonrakiDeneme <= now && x.Deneme < 5)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.Kilit, token).SetProperty(x => x.KilitBitis, now + 120)
                    .SetProperty(x => x.Deneme, x => x.Deneme + 1), ct);
            if (claimed != 1) continue;
            var n = notifications.Single(x => x.Id == d.BildirimId);
            var subscription = await db.Set<PushAbonelikEntity>().AsNoTracking().SingleAsync(x => x.Id == d.AbonelikId, ct);
            // Recheck debt and session immediately before handing off to the external provider.
            var current = Taslaklar(today, settings.Etkin).FirstOrDefault(x => x.Anahtar == n.OlayAnahtari);
            var currentSettings = Ayarlar();
            if (current is null || !currentSettings.Etkin || !subscription.Etkin
                || localAttempt.TimeOfDay < new TimeSpan(currentSettings.Saat, currentSettings.Dakika, 0)
                || !OturumDamgasi.Esit(subscription.OturumDamgasi, OturumDamgasi.Uret("editor", cfg, db)))
            {
                await db.Set<BildirimTeslimEntity>().Where(x => x.Id == d.Id && x.Kilit == token)
                    .ExecuteUpdateAsync(p => p.SetProperty(x => x.SonrakiDeneme, now + 60)
                        .SetProperty(x => x.KilitBitis, 0).SetProperty(x => x.Kilit, (string?)null)
                        .SetProperty(x => x.Deneme, x => x.Deneme - 1), ct);
                continue;
            }
            var result = await sender.Gonder(subscription, new(n.Id, current.Baslik, current.Mesaj, current.Hedef, $"kasa-{n.Id}"), ttl, ct);
            now = clock.GetUtcNow().ToUnixTimeSeconds();
            if (result == PushSonuc.AbonelikBitti)
                await db.Set<PushAbonelikEntity>().Where(x => x.Id == subscription.Id)
                    .ExecuteUpdateAsync(p => p.SetProperty(x => x.Etkin, false), ct);
            if (result == PushSonuc.Basarili)
            {
                await db.Set<PushAbonelikEntity>().Where(x => x.Id == subscription.Id)
                    .ExecuteUpdateAsync(p => p.SetProperty(x => x.SonBasarili, (long?)now), ct);
                await db.Set<BildirimTeslimEntity>().Where(x => x.Id == d.Id && x.Kilit == token)
                    .ExecuteUpdateAsync(p => p.SetProperty(x => x.Gonderildi, (long?)now).SetProperty(x => x.Kilit, (string?)null), ct);
            }
            else
            {
                var terminal = result is PushSonuc.KaliciHata or PushSonuc.AbonelikBitti;
                var retryAt = now + (long)Math.Min(3600, 60 * Math.Pow(2, d.Deneme));
                await db.Set<BildirimTeslimEntity>().Where(x => x.Id == d.Id && x.Kilit == token)
                    .ExecuteUpdateAsync(p => p.SetProperty(x => x.Iptal, terminal).SetProperty(x => x.SonrakiDeneme, retryAt)
                        .SetProperty(x => x.KilitBitis, 0).SetProperty(x => x.Kilit, (string?)null), ct);
            }
        }
    }
}

public sealed class BildirimWorker(IServiceScopeFactory scopes, IConfiguration cfg, IWebHostEnvironment env,
    ILogger<BildirimWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(cfg.GetValue<bool?>("Bildirim:WorkerEtkin") ?? env.IsProduction())) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BildirimServisi>().Gonder(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("Bildirim denetimi tamamlanamadı; sonraki turda yeniden denenecek."); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
