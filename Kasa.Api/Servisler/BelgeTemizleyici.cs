using System.Text.RegularExpressions;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// Gece temizliği (günde bir kez, Türkiye saatiyle 04:00'ten sonra; açılıştan 10 dakika sonra başlar):
/// <list type="bullet">
/// <item>İşlemi silinmiş ve geri alma süresi (30 gün) + 1 gün geçmiş eklerin kaydı ve dosyası silinir.
///       Silinme zamanı ekin kendisinden (işlem silinirken yazılır), yoksa geçmişteki "İşlem silindi"
///       satırından okunur; ikisi de yoksa yükleme zamanı esas alınır.</item>
/// <item>Klasörde kaydı olmayan ek dosyaları 30 günden eskiyse silinir (yarım kalan yükleme / silinemeyen dosya).</item>
/// <item>6 saatten eski yarım kalmış geçici dosyalar (<c>.*.tmp</c>) silinir.</item>
/// </list>
/// </summary>
public sealed partial class BelgeTemizleyici : BackgroundService
{
    /// <summary>İşlemi silinen ekin en az bu kadar saklanır (geri alma süresi + 1 gün pay).</summary>
    public static readonly TimeSpan YetimSaklama = GecmisKurallari.GeriAlmaSuresi + TimeSpan.FromDays(1);
    /// <summary>Kaydı olmayan dosyanın silinmesi için gereken yaş.</summary>
    public static readonly TimeSpan KayitsizDosyaYasi = TimeSpan.FromDays(30);
    public static readonly TimeSpan GeciciDosyaYasi = TimeSpan.FromHours(6);
    public const int CalismaSaati = 4;

    [GeneratedRegex("^\\.[0-9a-f]{32}\\.(jpg|png|webp|heic|pdf)\\.tmp$")]
    private static partial Regex GeciciAd();

    private readonly IServiceScopeFactory _scopes;
    private readonly BelgeDeposu _depo;
    private readonly TimeProvider _saat;
    private readonly ILogger<BelgeTemizleyici> _log;
    private readonly IHostApplicationLifetime _omur;

    public BelgeTemizleyici(IServiceScopeFactory scopes, BelgeDeposu depo, TimeProvider saat,
        ILogger<BelgeTemizleyici> log, IHostApplicationLifetime omur)
    {
        _scopes = scopes; _depo = depo; _saat = saat; _log = log; _omur = omur;
    }

    protected override async Task ExecuteAsync(CancellationToken iptal)
    {
        var basladi = new TaskCompletionSource();
        using (_omur.ApplicationStarted.Register(() => basladi.TrySetResult()))
        using (iptal.Register(() => basladi.TrySetCanceled()))
        {
            try { await basladi.Task; } catch (TaskCanceledException) { return; }
        }
        try { await Task.Delay(TimeSpan.FromMinutes(10), iptal); } catch (TaskCanceledException) { return; }

        DateOnly? sonGun = null;
        while (!iptal.IsCancellationRequested)
        {
            var yerel = Saat.Simdi(_saat.GetUtcNow().UtcDateTime);
            var gun = DateOnly.FromDateTime(yerel);
            if (yerel.Hour >= CalismaSaati && sonGun != gun)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                    var s = Temizle(db, _depo, _saat.GetUtcNow().UtcDateTime);
                    if (s.Toplam > 0)
                        _log.LogInformation("Ek temizliği: {Kayit} yetim ek, {Dosya} kayıtsız dosya, {Gecici} geçici dosya silindi.",
                            s.YetimKayit, s.KayitsizDosya, s.GeciciDosya);
                    sonGun = gun;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Ek temizliği başarısız; bir saat sonra yeniden denenecek.");
                }
            }
            try { await Task.Delay(TimeSpan.FromHours(1), iptal); } catch (TaskCanceledException) { }
        }
    }

    public sealed record Sonuc(int YetimKayit, int KayitsizDosya, int GeciciDosya)
    {
        public int Toplam => YetimKayit + KayitsizDosya + GeciciDosya;
    }

    /// <summary>Temizliği bir kez çalıştırır (testler doğrudan çağırır).</summary>
    public static Sonuc Temizle(KasaDbContext db, BelgeDeposu depo, DateTime simdiUtc)
    {
        // 1) İşlemi olmayan ekler.
        var islemIdleri = db.Islemler.AsNoTracking().Select(i => i.Id).ToHashSet();
        var yetimler = db.IslemEkleri.AsNoTracking().ToList().Where(e => !islemIdleri.Contains(e.IslemId)).ToList();
        var silinecekler = new List<IslemEkiEntity>();
        foreach (var e in yetimler)
        {
            // Silinme anı: ekte yazılı olan (işlem silinirken yazılır); yoksa geçmişteki "İşlem silindi" satırı.
            var eskiIslemId = e.SilinenIslemId ?? e.IslemId;
            var silinme = e.SilinmeZamaniUtc ?? db.Degisiklikler.AsNoTracking()
                .Where(d => d.Tur == GecmisTurleri.Islem && d.Eylem == Eylemler.Silindi && d.KayitId == eskiIslemId)
                .OrderByDescending(d => d.ZamanUtc).Select(d => (DateTime?)d.ZamanUtc).FirstOrDefault();
            var esas = silinme ?? e.YuklemeZamaniUtc;
            if (simdiUtc - esas > YetimSaklama) silinecekler.Add(e);
        }
        if (silinecekler.Count > 0)
        {
            var idler = silinecekler.Select(e => e.Id).ToList();
            var eski = db.DegistirenRol;
            db.DegistirenRol = "sistem";
            try
            {
                using var tx = db.Database.BeginTransaction();
                db.IslemEkleri.Where(e => idler.Contains(e.Id)).ExecuteDelete();
                db.TopluDegisiklikEkle(GecmisTurleri.IslemEki, null,
                    $"Gece temizliği: işlemi {YetimSaklama.TotalDays - 1:0} günden önce silinmiş {silinecekler.Count} ek silindi",
                    eski: silinecekler.Select(e => new { e.Id, IslemId = e.SilinenIslemId ?? e.IslemId, e.OrijinalAd }).ToList());
                db.SaveChanges();
                tx.Commit();
            }
            finally { db.DegistirenRol = eski; }
            foreach (var e in silinecekler) depo.Sil(e.DepoAdi);
        }

        // 2) Kayıtsız dosyalar ve yarım kalan geçici dosyalar.
        int kayitsiz = 0, gecici = 0;
        if (Directory.Exists(depo.Klasor))
        {
            var kayitli = db.IslemEkleri.AsNoTracking().Select(e => e.DepoAdi).ToHashSet();
            foreach (var yol in Directory.EnumerateFiles(depo.Klasor))
            {
                var ad = Path.GetFileName(yol);
                var yas = simdiUtc - File.GetLastWriteTimeUtc(yol);
                try
                {
                    if (BelgeDeposu.GecerliDepoAdi(ad) && !kayitli.Contains(ad) && yas > KayitsizDosyaYasi)
                    { File.Delete(yol); kayitsiz++; }
                    else if (GeciciAd().IsMatch(ad) && yas > GeciciDosyaYasi)
                    { File.Delete(yol); gecici++; }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return new Sonuc(silinecekler.Count, kayitsiz, gecici);
    }
}

/// <summary>Ek deposu ve gece temizliğinin DI kaydı (Program.cs'te tek satır).</summary>
public static class BelgeServisKaydi
{
    public static IServiceCollection AddBelgeEkleri(this IServiceCollection services)
    {
        services.AddSingleton<BelgeDeposu>(sp => new BelgeDeposu(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IWebHostEnvironment>()));
        services.AddHostedService<BelgeTemizleyici>();
        return services;
    }
}
