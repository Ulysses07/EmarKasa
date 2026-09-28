using Kasa.Api.Denetim;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public partial class KasaDbContext
{
    // Kilit kuralları geçen değişiklikler kaydedilir, ardından aynı transaction'da denetim olayları yazılır: olay yazılamazsa
    // değişiklik de geri alınır, değişiklik geri alınırsa olay da kalmaz. Eklenen kaydın anahtarı kayıttan sonra okunur.
    // İzleyici değişiklikleri olaylar yazılıp transaction commit edildikten sonra kabul eder: olay yazımı ya da commit
    // başarısız olursa eklenen/değişen/silinen kayıtlar bekler (veritabanıyla tutarlı) ve aynı bağlamla yeniden kaydedilebilir.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.HasChanges()) return base.SaveChanges(acceptAllChangesOnSuccess);
        int result;
        using (var transaction = Database.CurrentTransaction is null ? Database.BeginTransaction() : null)
        {
            AyKilidiKurallari.Dogrula(this);
            var yakalanan = DenetimYakalayici.Yakala(this);
            result = base.SaveChanges(acceptAllChangesOnSuccess: false);
            DenetimYakalayici.Yaz(this, yakalanan);
            transaction?.Commit();
        }
        if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
        return result;
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.HasChanges()) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        int result;
        await using (var transaction = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null)
        {
            AyKilidiKurallari.Dogrula(this);
            var yakalanan = DenetimYakalayici.Yakala(this);
            result = await base.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
            DenetimYakalayici.Yaz(this, yakalanan);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
        return result;
    }
}
