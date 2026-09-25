using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public partial class KasaDbContext
{
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.HasChanges()) return base.SaveChanges(acceptAllChangesOnSuccess);
        using var transaction = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        AyKilidiKurallari.Dogrula(this);
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        transaction?.Commit(); return result;
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.HasChanges()) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        await using var transaction = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        AyKilidiKurallari.Dogrula(this);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
