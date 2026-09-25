using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public sealed class KasaEsikEntity
{
    public int Id { get; set; }
    public int KanalId { get; set; }
    public int Surum { get; set; } = 1;
    public decimal Tutar { get; set; }
    public bool Etkin { get; set; }
    public bool AlarmAcik { get; set; }
    public int OlaySayisi { get; set; }
    public DateOnly? UyariTarihi { get; set; }
}

public sealed class KasaKontrolEntity
{
    public int Id { get; set; }
    public long Kaydedildi { get; set; }
    public decimal SistemBakiye { get; set; }
    public decimal GercekBakiye { get; set; }
    public decimal Fark { get; set; }
    public string? Not { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<KasaEsikEntity> KasaEsikleri => Set<KasaEsikEntity>();
    public DbSet<KasaKontrolEntity> KasaKontrolleri => Set<KasaKontrolEntity>();

    partial void ConfigureCashControls(ModelBuilder b)
    {
        b.Entity<KasaEsikEntity>().ToTable("KasaEsikleri");
        b.Entity<KasaEsikEntity>().HasIndex(x => x.KanalId).IsUnique();
        b.Entity<KasaEsikEntity>().Property(x => x.Surum).IsConcurrencyToken();
        b.Entity<KasaEsikEntity>().HasOne<KanalEntity>().WithMany().HasForeignKey(x => x.KanalId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<KasaKontrolEntity>().ToTable("KasaKontrolleri");
    }
}
