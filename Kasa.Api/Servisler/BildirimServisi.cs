using System.Diagnostics;
using System.Globalization;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>Hesaplanamayan bildirim kaynağı (kart, kredi, kasa alt sınırı). Diğer kaynakların hatırlatmalarını durdurmaz.</summary>
public record BildirimKaynakHatasi(string Kaynak, int KaynakId, string Ad, Exception Hata);

public interface IBildirimKaynaklari
{
    /// <summary>Hesaplanamayan kart/kredi atlanıp <paramref name="hatalar"/>'a eklenir; diğerlerinin olayları döner.</summary>
    IReadOnlyList<TakipOlayDto> Oku(KasaDbContext db, DateOnly today, ICollection<BildirimKaynakHatasi> hatalar);
}

/// <summary><see cref="FinansTakipServisi.GetNotificationEvents"/> ile aynı olayları üretir; farkı her kart ve kredinin
/// ayrı hesaplanmasıdır: bozuk bir kayıt atlanır, ötekilerin hatırlatmaları sürer. Yeni kart hareketleri kısa ve ayrı
/// bir yazma adımında eşitlenir; hesabın kendisi yazma kilidi almadan, salt okunur anlık görüntüde (<see cref="OkumaAnlikGoruntusu"/>)
/// ve tur boyunca paylaşılan izlemesiz hesap bağlamıyla (<see cref="TakipHesapBaglami"/>) yapılır: eşitlemenin izlediği
/// kayıtlar değil, anlık görüntünün verisi okunur.</summary>
public sealed class FinansBildirimKaynaklari : IBildirimKaynaklari
{
    public IReadOnlyList<TakipOlayDto> Oku(KasaDbContext db, DateOnly today, ICollection<BildirimKaynakHatasi> hatalar)
    {
        // Eşitlenemeyen hareketler kayıtlı veriyle okumayı durdurmaz; hata ayrıca bildirilir.
        Dene(db, hatalar, "Kart", 0, "Kart hareketleri eşitlemesi", () =>
        {
            using var yazma = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
            FinansTakipServisi.Sync(db); yazma?.Commit();
        });
        using var okuma = db.OkumaBaslat();
        // Hesaplanamayan kart bağlamda yarım sonuç bırakmaz (paylar ve etkiler yalnız başarıyla hesaplanınca saklanır).
        var baglam = new TakipHesapBaglami(db);
        var result = new List<TakipOlayDto>();
        var kartAdlari = db.KrediKartlari.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        foreach (var card in db.TakipKartlar.AsNoTracking().ToList())
            Dene(db, hatalar, "Kart", card.KrediKartiId, kartAdlari.GetValueOrDefault(card.KrediKartiId, $"Kart #{card.KrediKartiId}"), () =>
            {
                var dto = FinansTakipServisi.Kart(baglam, card.KrediKartiId);
                var olaylar = new List<TakipOlayDto>();
                foreach (var s in dto.Ekstreler)
                {
                    if (card.Aktif) olaylar.Add(new("Kart", dto.Id, s.Id, dto.Ad, s.KesimTarihi, s.Borc, "Kesim", false));
                    if (s.Kalan > 0) olaylar.Add(new("Kart", dto.Id, s.Id, dto.Ad, s.SonOdemeTarihi, s.Kalan, "SonOdeme", false));
                }
                result.AddRange(olaylar);
            });
        var krediAdlari = db.Krediler.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        foreach (var loan in db.TakipKrediler.AsNoTracking().ToList())
            Dene(db, hatalar, "Kredi", loan.KrediId, krediAdlari.GetValueOrDefault(loan.KrediId, $"Kredi #{loan.KrediId}"), () =>
            {
                var dto = FinansTakipServisi.Kredi(baglam, loan.KrediId);
                result.AddRange(dto.Taksitler.Where(t => t.Durum != "Iptal").Select(t => new TakipOlayDto("Kredi", dto.Id, t.Id, dto.Ad + " / " + t.No + ". taksit", t.Tarih, t.Tutar, "Taksit", true)).ToList());
            });
        return result;
    }

    private static void Dene(KasaDbContext db, ICollection<BildirimKaynakHatasi> hatalar, string kaynak, int id, string ad, Action hesap)
    {
        try { hesap(); }
        catch (Exception e) when (!BildirimHatalari.Gecici(e))
        {
            // Yarım kalan izlenen değişiklikler sonraki SaveChanges ile yazılmasın.
            db.ChangeTracker.Clear();
            hatalar.Add(new(kaynak, id, ad, e));
        }
    }
}

internal static class BildirimHatalari
{
    /// <summary>Kilit beklemesi (SQLITE_BUSY/LOCKED) ve iptal kayda özgü değildir: tur yarım veriyle sürmez
    /// (bekleyen hatırlatmalar iptal edilmez), işçi hatayı loglar ve sonraki turda yeniden dener.</summary>
    public static bool Gecici(Exception e) => e switch
    {
        OperationCanceledException => true,
        SqliteException { SqliteErrorCode: 5 or 6 } => true,
        { InnerException: { } ic } => Gecici(ic),
        _ => false
    };
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

    /// <summary>Hesaplanamayan kaynak için editöre günde bir kez giden uyarı: o kaynağın hatırlatmaları sessizce durmaz.</summary>
    public static BildirimTaslagi Hata(BildirimKaynakHatasi h, DateOnly today)
    {
        var hedef = h.Kaynak switch
        {
            "Kart" => h.KaynakId > 0 ? $"/#cards/{h.KaynakId}" : "/#cards",
            "Kredi" => $"/#loans/{h.KaynakId}",
            _ => "/#home"
        };
        return new($"Hata:{h.Kaynak}:{h.KaynakId}:{today:yyyy-MM-dd}", "Kayıt hesaplanamadı",
            $"{h.Ad}: kayıt hesaplanamadığı için hatırlatmaları eksik ya da güncel olmayabilir. Kaydı açıp kontrol et; sorun sürerse yöneticiye bildir.",
            today, hedef, "Hata", h.KaynakId);
    }
}

/// <summary>Bildirim hattının son sunucu hatası; ayarlar yanıtında (sonHata) editöre görünür. Ayrıntı ve yığın izi
/// sunucu kaydındadır. Tur hatası başarılı turla, gönderim (yapılandırma) hatası başarılı gönderimle temizlenir.</summary>
public sealed class BildirimSagligi
{
    private readonly object gate = new();
    private (string Mesaj, DateTimeOffset An)? tur, gonderim;
    public (string Mesaj, DateTimeOffset An)? Son { get { lock (gate) return tur ?? gonderim; } }
    public void TurHatasi(string mesaj, DateTimeOffset an) { lock (gate) tur = (mesaj, an); }
    public void TurBasarili() { lock (gate) tur = null; }
    public void GonderimHatasi(string mesaj, DateTimeOffset an) { lock (gate) gonderim = (mesaj, an); }
    public void GonderimBasarili() { lock (gate) gonderim = null; }
}

public sealed class BildirimServisi(KasaDbContext db, IBildirimKaynaklari sources, IPushGonderici sender,
    IConfiguration cfg, TimeProvider clock, BildirimSagligi saglik, ILogger<BildirimServisi> logger)
{
    /// <summary>Logları isteğe/tura bağlayan kimlik: istek içinde TraceId, işçide yeni tur kimliği.</summary>
    public string Iz { get; } = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")[..12];

    // Her kaynak (kart, kredi, kasa alt sınırı) ayrı yalıtılır; hesaplanamayan kaynak loglanır ve editöre uyarı olur.
    private IReadOnlyList<BildirimTaslagi> Taslaklar(DateOnly today, bool yeniUyariEtkin)
    {
        var hatalar = new List<BildirimKaynakHatasi>();
        var olaylar = sources.Oku(db, today, hatalar);
        IReadOnlyList<BildirimTaslagi> esik = [];
        try { esik = KasaEsikServisi.Oku(db, today, yeniUyariEtkin); }
        catch (Exception e) when (!BildirimHatalari.Gecici(e))
        {
            // Yarım kalan alarm durumu sonraki SaveChanges ile yazılmasın.
            db.ChangeTracker.Clear();
            hatalar.Add(new("KasaEsik", 0, "Kasa alt sınırı denetimi", e));
        }
        foreach (var h in hatalar)
            logger.LogError(h.Hata, "Bildirim kaynağı hesaplanamadı: {Kaynak} #{KaynakId} ({Ad}), iz {Iz}. Diğer hatırlatmalar sürüyor.",
                h.Kaynak, h.KaynakId, h.Ad, Iz);
        return [.. BildirimTakvimi.Olustur(olaylar, today), .. esik, .. hatalar.Select(h => BildirimTakvimi.Hata(h, today))];
    }

    private static Dictionary<string, BildirimTaslagi> Sozluk(IEnumerable<BildirimTaslagi> taslaklar)
    {
        var result = new Dictionary<string, BildirimTaslagi>(StringComparer.Ordinal);
        foreach (var t in taslaklar) result.TryAdd(t.Anahtar, t);
        return result;
    }

    public BildirimAyarEntity Ayarlar()
    {
        db.Database.ExecuteSqlRaw("INSERT OR IGNORE INTO BildirimAyarlari (Id,Etkin,Saat,Dakika,Surum) VALUES (1,1,9,0,1)");
        return db.Set<BildirimAyarEntity>().AsNoTracking().Single(x => x.Id == 1);
    }

    public async Task Yenile(CancellationToken ct = default) => await YenileIc(ct);

    private async Task<(DateOnly Gun, IReadOnlyList<BildirimTaslagi> Taslaklar)> YenileIc(CancellationToken ct)
    {
        var now = BildirimTakvimi.Yerel(clock.GetUtcNow());
        var today = DateOnly.FromDateTime(now); var settings = Ayarlar();
        var drafts = Taslaklar(today, settings.Etkin);
        var keys = drafts.Select(x => x.Anahtar).ToHashSet(StringComparer.Ordinal);
        // A changed due date, cancellation or full payment cancels any unsent reminder immediately.
        var todays = await db.Set<BildirimEntity>().Where(x => x.Tarih == today).ToListAsync(ct);
        foreach (var row in todays) row.Iptal = !keys.Contains(row.OlayAnahtari);
        await db.SaveChangesAsync(ct);
        if (!settings.Etkin || now.TimeOfDay < new TimeSpan(settings.Saat, settings.Dakika, 0)) return (today, drafts);
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
        return (today, drafts);
    }

    public async Task Gonder(CancellationToken ct = default)
    {
        // Tur boyunca tek bağlantı: PRAGMA data_version yalnız başka bağlantıların commit'leriyle değişir ve
        // "tur hesabından sonra veri değişti mi" sorusunu teslim başına tam hesap yapmadan yanıtlar.
        await db.Database.OpenConnectionAsync(ct);
        try { await GonderIc(ct); }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private async Task<long> VeriSurumu(CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA data_version";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private async Task GonderIc(CancellationToken ct)
    {
        // Sürüm hesaptan önce okunur: hesap sırasında gelen commit de teslimden önce yeniden hesaplatır.
        var version = await VeriSurumu(ct);
        var (day, computed) = await YenileIc(ct);
        var settings = Ayarlar();
        if (!settings.Etkin) return;
        var instant = clock.GetUtcNow(); var queryAt = instant.ToUnixTimeSeconds();
        var local = BildirimTakvimi.Yerel(instant); var today = DateOnly.FromDateTime(local);
        if (today != day || local.TimeOfDay < new TimeSpan(settings.Saat, settings.Dakika, 0)) return;
        var drafts = Sozluk(computed);
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
        // Kapanmış ya da silinmiş aboneliğin bekleyen teslimi kiralanmaz: her dakika dönmez, 100'lük kotayı tüketmez.
        var deliveries = await db.Set<BildirimTeslimEntity>().AsNoTracking()
            .Where(x => ids.Contains(x.BildirimId) && x.Gonderildi == null && !x.Iptal && x.Deneme < 5
                && x.SonrakiDeneme <= queryAt && x.KilitBitis <= queryAt
                && db.Set<PushAbonelikEntity>().Any(s => s.Id == x.AbonelikId && s.Etkin))
            .OrderBy(x => x.Id).Take(100).ToListAsync(ct);
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
            var subscription = await db.Set<PushAbonelikEntity>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == d.AbonelikId, ct);
            // Ucuz denetimler önce: kapanmış, silinmiş ya da eski oturumlu aboneliğin teslimi hesap yapılmadan kalıcı kapanır.
            if (subscription is null || !subscription.Etkin
                || !OturumDamgasi.Esit(subscription.OturumDamgasi, OturumDamgasi.Uret("editor", cfg, db)))
            {
                if (subscription is { Etkin: true })
                    await db.Set<PushAbonelikEntity>().Where(x => x.Id == subscription.Id)
                        .ExecuteUpdateAsync(p => p.SetProperty(x => x.Etkin, false), ct);
                await Kapat(d.Id, token, ct);
                continue;
            }
            var currentSettings = Ayarlar();
            if (!currentSettings.Etkin || localAttempt.TimeOfDay < new TimeSpan(currentSettings.Saat, currentSettings.Dakika, 0))
            {
                await Ertele(d.Id, token, now, ct);
                continue;
            }
            // Dış sağlayıcıya vermeden hemen önce borç yeniden denetlenir: tur hesabı, başka bir bağlantı commit edene
            // (ör. ödeme kaydı) kadar geçerlidir; ancak o zaman yeniden hesaplanır.
            var currentVersion = await VeriSurumu(ct);
            if (currentVersion != version)
            {
                db.ChangeTracker.Clear();
                version = currentVersion;
                drafts = Sozluk(Taslaklar(today, currentSettings.Etkin));
            }
            if (!drafts.TryGetValue(n.OlayAnahtari, out var current))
            {
                await Ertele(d.Id, token, now, ct);
                continue;
            }
            PushSonuc result;
            try { result = await sender.Gonder(subscription, new(n.Id, current.Baslik, current.Mesaj, current.Hedef, $"kasa-{n.Id}"), ttl, ct); }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                // Bir cihazın beklenmeyen hatası ötekileri durdurmaz; geçici hata gibi yeniden denenir.
                logger.LogError(e, "Bildirim #{BildirimId} abonelik #{AbonelikId} cihazına gönderilemedi (iz {Iz}); geçici hata sayıldı, yeniden denenecek.",
                    n.Id, subscription.Id, Iz);
                result = PushSonuc.GeciciHata;
            }
            now = clock.GetUtcNow().ToUnixTimeSeconds();
            if (result == PushSonuc.YapilandirmaHatasi)
            {
                // Sunucu anahtarı düzelene kadar hiçbir cihaza gönderilemez: deneme hakkı harcanmaz, tur burada durur.
                await Ertele(d.Id, token, now, ct);
                saglik.GonderimHatasi("Sunucudaki bildirim anahtarı hatalı yapılandırılmış; cihaz bildirimleri gönderilemiyor.", clock.GetUtcNow());
                break;
            }
            if (result == PushSonuc.AbonelikBitti)
                await db.Set<PushAbonelikEntity>().Where(x => x.Id == subscription.Id)
                    .ExecuteUpdateAsync(p => p.SetProperty(x => x.Etkin, false), ct);
            if (result == PushSonuc.Basarili)
            {
                saglik.GonderimBasarili();
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

    // Geçici erteleme (saat, ayar, güncel borç): bir dakika sonra, deneme hakkı harcanmadan yeniden denenir.
    private Task Ertele(int id, string token, long now, CancellationToken ct) =>
        db.Set<BildirimTeslimEntity>().Where(x => x.Id == id && x.Kilit == token)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.SonrakiDeneme, now + 60)
                .SetProperty(x => x.KilitBitis, 0).SetProperty(x => x.Kilit, (string?)null)
                .SetProperty(x => x.Deneme, x => x.Deneme - 1), ct);

    private Task Kapat(int id, string token, CancellationToken ct) =>
        db.Set<BildirimTeslimEntity>().Where(x => x.Id == id && x.Kilit == token)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.Iptal, true)
                .SetProperty(x => x.KilitBitis, 0).SetProperty(x => x.Kilit, (string?)null), ct);
}

public sealed class BildirimWorker(IServiceScopeFactory scopes, IConfiguration cfg, IWebHostEnvironment env,
    ILogger<BildirimWorker> logger, BildirimSagligi saglik, TimeProvider clock) : BackgroundService
{
    private int ardisikHata;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(cfg.GetValue<bool?>("Bildirim:WorkerEtkin") ?? env.IsProduction())) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Tur(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Tek bildirim turu. Hata işçiyi durdurmaz: istisna nesnesi, tur kimliği ve ardışık hata sayısıyla
    /// loglanır, ayarlar yanıtında (sonHata) görünür; sonraki tur yeniden dener.</summary>
    public async Task Tur(CancellationToken ct)
    {
        var iz = Guid.NewGuid().ToString("N")[..12];
        try
        {
            using var scope = scopes.CreateScope();
            var servis = scope.ServiceProvider.GetRequiredService<BildirimServisi>();
            iz = servis.Iz;
            await servis.Gonder(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            ardisikHata++;
            logger.LogError(ex, "Bildirim denetimi tamamlanamadı ({Ardisik}. ardışık hata, iz {Iz}); sonraki turda yeniden denenecek.", ardisikHata, iz);
            if (ardisikHata == 5)
                logger.LogCritical("Bildirim denetimi {Ardisik} turdur tamamlanamıyor (iz {Iz}); hatırlatmalar gönderilmiyor.", ardisikHata, iz);
            saglik.TurHatasi($"Bildirim denetimi tamamlanamadı; ayrıntı sunucu kaydında (iz {iz}).", clock.GetUtcNow());
            return;
        }
        if (ardisikHata > 0)
            logger.LogInformation("Bildirim denetimi {Ardisik} ardışık hatadan sonra yeniden tamamlandı (iz {Iz}).", ardisikHata, iz);
        ardisikHata = 0; saglik.TurBasarili();
    }
}
