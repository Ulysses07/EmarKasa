using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public partial class KasaDbContext
{
    public DbSet<CekEntity> Cekler => Set<CekEntity>();
    public DbSet<CekHareketEntity> CekHareketler => Set<CekHareketEntity>();

    partial void ConfigureCekler(ModelBuilder b)
    {
        b.Entity<CekEntity>().ToTable("Cekler");
        b.Entity<CekEntity>().Property(c => c.Surum).IsConcurrencyToken();
        b.Entity<CekEntity>().HasOne<KanalEntity>().WithMany().HasForeignKey(c => c.KanalId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CekHareketEntity>().ToTable("CekHareketler");
        b.Entity<CekHareketEntity>().HasOne<CekEntity>().WithMany().HasForeignKey(h => h.CekId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CekHareketEntity>().HasOne<KanalEntity>().WithMany().HasForeignKey(h => h.KanalId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CekHareketEntity>().HasIndex(h => new { h.CekId, h.Sira }).IsUnique();
    }
}
