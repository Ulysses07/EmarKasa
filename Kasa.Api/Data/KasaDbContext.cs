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
}
