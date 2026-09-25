using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public partial class KasaDbContext
{
    public DbSet<TakipKartEntity> TakipKartlar => Set<TakipKartEntity>();
    public DbSet<TakipEkstreEntity> TakipEkstreler => Set<TakipEkstreEntity>();
    public DbSet<TakipHarcamaEntity> TakipHarcamalar => Set<TakipHarcamaEntity>();
    public DbSet<TakipKartTaksitEntity> TakipKartTaksitler => Set<TakipKartTaksitEntity>();
    public DbSet<TakipKartOdemeEntity> TakipKartOdemeler => Set<TakipKartOdemeEntity>();
    public DbSet<TakipKrediEntity> TakipKrediler => Set<TakipKrediEntity>();
    public DbSet<TakipKrediTaksitEntity> TakipKrediTaksitler => Set<TakipKrediTaksitEntity>();

    private static void ConfigureCardLoanTracking(ModelBuilder b)
    {
        b.Entity<TakipKartEntity>().HasKey(x => x.KrediKartiId);
        b.Entity<TakipKartEntity>().Property(x => x.KrediKartiId).ValueGeneratedNever();
        b.Entity<TakipKartEntity>().Property(x => x.Surum).IsConcurrencyToken();
        b.Entity<TakipKartEntity>().HasOne<KrediKartiEntity>().WithMany().HasForeignKey(x => x.KrediKartiId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipEkstreEntity>().HasOne<TakipKartEntity>().WithMany().HasForeignKey(x => x.KrediKartiId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipEkstreEntity>().HasIndex(x => new { x.KrediKartiId, x.KesimTarihi }).IsUnique();
        b.Entity<TakipHarcamaEntity>().HasOne<TakipKartEntity>().WithMany().HasForeignKey(x => x.KrediKartiId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipHarcamaEntity>().HasOne<IslemEntity>().WithMany().HasForeignKey(x => x.IslemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipHarcamaEntity>().HasIndex(x => x.IslemId).IsUnique();
        b.Entity<TakipHarcamaEntity>().HasOne<TakipHarcamaEntity>().WithMany().HasForeignKey(x => x.KaynakHarcamaId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipKartTaksitEntity>().HasOne<TakipHarcamaEntity>().WithMany().HasForeignKey(x => x.HarcamaId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipKartTaksitEntity>().HasOne<TakipEkstreEntity>().WithMany().HasForeignKey(x => x.EkstreId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipKartOdemeEntity>().HasOne<TakipKartEntity>().WithMany().HasForeignKey(x => x.KrediKartiId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipKrediEntity>().HasKey(x => x.KrediId);
        b.Entity<TakipKrediEntity>().Property(x => x.KrediId).ValueGeneratedNever();
        b.Entity<TakipKrediEntity>().Property(x => x.Surum).IsConcurrencyToken();
        b.Entity<TakipKrediEntity>().HasOne<KrediEntity>().WithMany().HasForeignKey(x => x.KrediId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipKrediTaksitEntity>().HasOne<TakipKrediEntity>().WithMany().HasForeignKey(x => x.KrediId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TakipKrediTaksitEntity>().HasIndex(x => new { x.KrediId, x.No }).IsUnique();
    }
}
