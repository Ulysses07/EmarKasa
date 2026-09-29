using Kasa.Api.Data;

namespace Kasa.Api.Servisler;

/// <summary>
/// Finans takibinin periyodik bakım adımı: okumalar Sync yapmadığından tarihe bağlı türetmeyi (aktif kartın yeni kesim
/// ekstresi) gün dönümünden sonra kalıcı yazar. Açılışta ve İstanbul günü değiştiğinde bir kez, kendi kısa yazma
/// transaction'ında <see cref="FinansTakipServisi.Bakim"/> çalıştırır; dakikada bir yalnız günü denetler. Başarısız tur
/// sonraki dakikada yeniden denenir. Okumalar bu adımı beklemez: eksik ekstreyi okuma anında (yazmadan) türetir.
/// Üretimde açıktır; <c>Finans:BakimEtkin</c> ile ayarlanır (geliştirme ve testlerde varsayılan kapalı).
/// </summary>
public sealed class FinansBakimi(IServiceScopeFactory scopes, IConfiguration cfg, IWebHostEnvironment env, TimeProvider saat,
    ILogger<FinansBakimi> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(cfg.GetValue<bool?>("Finans:BakimEtkin") ?? !env.IsDevelopment()))
            return;
        DateOnly? sonGun = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            var bugun = saat.IstanbulBugun();
            if (sonGun == bugun)
                continue;
            try
            {
                using var scope = scopes.CreateScope();
                FinansTakipServisi.Bakim(scope.ServiceProvider.GetRequiredService<KasaDbContext>());
                sonGun = bugun;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Finans bakım adımı tamamlanamadı; sonraki dakikada yeniden denenecek."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
