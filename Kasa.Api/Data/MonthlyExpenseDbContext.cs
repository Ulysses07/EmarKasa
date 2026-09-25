using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public partial class KasaDbContext
{
    public DbSet<AylikGiderSablonEntity> AylikGiderSablonlar => Set<AylikGiderSablonEntity>();
    public DbSet<AylikGiderRevizyonEntity> AylikGiderRevizyonlar => Set<AylikGiderRevizyonEntity>();
    public DbSet<AylikGiderOdemeEntity> AylikGiderOdemeler => Set<AylikGiderOdemeEntity>();
    public DbSet<AyKilidiEntity> AyKilidi => Set<AyKilidiEntity>();
    public DbSet<AyKilidiOlayEntity> AyKilidiOlaylar => Set<AyKilidiOlayEntity>();
    internal bool AylikGiderDegisikligi { get; set; }
    internal bool GecmisEtkisizKrediOlusturma { get; set; }

    partial void ConfigureMonthlyExpensesAndLocks(ModelBuilder b)
    {
        b.Entity<AylikGiderSablonEntity>().Property(s => s.Surum).IsConcurrencyToken();
        b.Entity<AylikGiderRevizyonEntity>().HasOne<AylikGiderSablonEntity>().WithMany().HasForeignKey(s => s.SablonId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AylikGiderRevizyonEntity>().HasIndex(s => new { s.SablonId, s.Surum }).IsUnique();
        b.Entity<AylikGiderOdemeEntity>().HasOne<AylikGiderSablonEntity>().WithMany().HasForeignKey(s => s.SablonId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AylikGiderOdemeEntity>().HasOne<AylikGiderRevizyonEntity>().WithMany().HasForeignKey(s => s.RevizyonId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AylikGiderOdemeEntity>().HasOne<IslemEntity>().WithMany().HasForeignKey(s => s.IslemId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<AylikGiderOdemeEntity>().HasIndex(s => s.IslemId).IsUnique();
        b.Entity<AylikGiderOdemeEntity>().HasIndex(s => new { s.SablonId, s.Ay }).IsUnique().HasFilter("\"Iptal\" = 0");
        b.Entity<AyKilidiEntity>().Property(s => s.Surum).IsConcurrencyToken();
    }
}
