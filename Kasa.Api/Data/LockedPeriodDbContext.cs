using Kasa.Api.Denetim;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public partial class KasaDbContext
{
    // Kilit kuralları geçen değişiklikler kaydedilir, ardından aynı transaction'da denetim olayları yazılır: olay yazılamazsa
    // değişiklik de geri alınır, değişiklik geri alınırsa olay da kalmaz. Eklenen kaydın anahtarı kayıttan sonra okunur.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.HasChanges()) return base.SaveChanges(acceptAllChangesOnSuccess);
        using var transaction = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        AyKilidiKurallari.Dogrula(this);
        var yakalanan = DenetimYakalayici.Yakala(this);
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        DenetimYakalayici.Yaz(this, yakalanan);
        transaction?.Commit(); return result;
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.HasChanges()) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        await using var transaction = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        AyKilidiKurallari.Dogrula(this);
        var yakalanan = DenetimYakalayici.Yakala(this);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        DenetimYakalayici.Yaz(this, yakalanan);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
