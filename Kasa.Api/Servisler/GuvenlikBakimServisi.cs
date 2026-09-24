using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// Saatlik güvenlik bakımı (uygulama başladıktan sonra):
/// <list type="bullet">
/// <item>saklama süresini (<c>Kasa:GirisGunluguGun</c>, varsayılan 180) aşan giriş günlüğü satırlarını;
///       başarısız denemeleri daha kısa (<see cref="GuvenlikKurallari.BasarisizGirisGun"/> gün) ve en çok
///       <see cref="GuvenlikKurallari.BasarisizGirisEnCokSatir"/> satır tutar (giriş yapmadan yazılabilirler),</item>
/// <item>süresi 7 günden önce dolmuş oturum satırlarını siler,</item>
/// <item>günlük yedek açıksa en yeni, henüz doğrulanmamış yedeği doğrular (<see cref="YedekDogrulayici"/>);
///       son 90 sonuç tutulur.</item>
/// </list>
/// <c>Kasa:GuvenlikBakimi=false</c> ile kapatılır (testler kendi DB'lerini paylaştığı için kapatır).
/// </summary>
public sealed class GuvenlikBakimServisi(IServiceScopeFactory scopes, IConfiguration cfg, ILogger<GuvenlikBakimServisi> log,
    IHostApplicationLifetime omur, TimeProvider saat) : BackgroundService
{
    public const int DogrulamaSakla = 90;

    protected override async Task ExecuteAsync(CancellationToken iptal)
    {
        if (!cfg.GetValue("Kasa:GuvenlikBakimi", true)) return;

        var basladi = new TaskCompletionSource();
        using (omur.ApplicationStarted.Register(() => basladi.TrySetResult()))
        using (iptal.Register(() => basladi.TrySetCanceled()))
        {
            try { await basladi.Task; } catch (TaskCanceledException) { return; }
        }
        // Günlük yedek servisi önce çalışsın (günün yedeği alınıp sonra doğrulansın).
        try { await Task.Delay(TimeSpan.FromMinutes(1), iptal); } catch (TaskCanceledException) { return; }

        while (!iptal.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                Calistir(db, cfg, saat.GetUtcNow().UtcDateTime, log);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Güvenlik bakımı çalışmadı");
            }
            try { await Task.Delay(TimeSpan.FromHours(1), iptal); } catch (TaskCanceledException) { }
        }
    }

    /// <summary>Bakımın bir turu (testler doğrudan çağırır). Doğrulama yaptıysa sonucunu döner.</summary>
    public static YedekDogrulamaEntity? Calistir(KasaDbContext db, IConfiguration cfg, DateTime simdiUtc, ILogger? log = null,
        int basarisizEnCok = GuvenlikKurallari.BasarisizGirisEnCokSatir)
    {
        var gun = KimlikEndpoints.GirisGunluguGun(cfg);
        var girisSiniri = simdiUtc.AddDays(-gun);
        var basarisizSiniri = simdiUtc.AddDays(-Math.Min(gun, GuvenlikKurallari.BasarisizGirisGun));
        var silinenGiris = db.GirisKayitlari.Where(g => g.ZamanUtc < girisSiniri || (!g.Basarili && g.ZamanUtc < basarisizSiniri)).ExecuteDelete();
        // Sayı sınırı: en yeni başarısız satırlar kalır.
        if (db.GirisKayitlari.Where(g => !g.Basarili).OrderByDescending(g => g.Id).Skip(basarisizEnCok).Select(g => (int?)g.Id).FirstOrDefault() is { } esik)
            silinenGiris += db.GirisKayitlari.Where(g => !g.Basarili && g.Id <= esik).ExecuteDelete();
        var oturumSiniri = simdiUtc.AddDays(-7);
        db.OturumKayitlari.Where(o => o.BitisUtc < oturumSiniri).ExecuteDelete();
        if (silinenGiris > 0) log?.LogInformation("{Sayi} eski giriş günlüğü satırı silindi.", silinenGiris);

        if (YedekDogrulayici.EnYeniGunlukYedek(cfg["Kasa:YedekKlasoru"]) is not { } dosya) return null;
        var ad = Path.GetFileName(dosya);
        var zaman = File.GetLastWriteTimeUtc(dosya);
        if (db.YedekDogrulamalari.AsNoTracking().Any(d => d.Dosya == ad && d.DosyaZamaniUtc == zaman)) return null;

        var sonuc = YedekDogrulayici.Dogrula(db, dosya, simdiUtc);
        db.YedekDogrulamalari.Add(sonuc);
        db.SaveChanges();
        var fazla = db.YedekDogrulamalari.OrderByDescending(d => d.Id).Skip(DogrulamaSakla).Select(d => d.Id).ToList();
        if (fazla.Count > 0) db.YedekDogrulamalari.Where(d => fazla.Contains(d.Id)).ExecuteDelete();
        if (sonuc.Basarili) log?.LogInformation("Yedek doğrulandı: {Dosya}", ad);
        else log?.LogError("Yedek doğrulaması başarısız: {Dosya} — {Mesaj}", ad, sonuc.Mesaj);
        return sonuc;
    }
}
