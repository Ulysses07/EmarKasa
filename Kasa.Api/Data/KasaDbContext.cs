using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public class KasaDbContext : DbContext
{
    public KasaDbContext(DbContextOptions<KasaDbContext> options) : base(options) { }

    public DbSet<KanalEntity> Kanallar => Set<KanalEntity>();
    public DbSet<CariEntity> Cariler => Set<CariEntity>();
    public DbSet<IslemEntity> Islemler => Set<IslemEntity>();
    public DbSet<GelenEntity> Gelenler => Set<GelenEntity>();
    public DbSet<AyarEntity> Ayarlar => Set<AyarEntity>();
    public DbSet<KrediKartiEntity> KrediKartlari => Set<KrediKartiEntity>();
    public DbSet<KartOdemeEntity> KartOdemeler => Set<KartOdemeEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Kart silinince harcama işlemi kalır, bağ kopar (SET NULL).
        b.Entity<IslemEntity>()
            .HasOne<KrediKartiEntity>()
            .WithMany()
            .HasForeignKey(i => i.KrediKartiId)
            .OnDelete(DeleteBehavior.SetNull);

        // Kart silinince ödemeleri de silinir (CASCADE).
        b.Entity<KartOdemeEntity>()
            .HasOne<KrediKartiEntity>()
            .WithMany()
            .HasForeignKey(o => o.KrediKartiId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
