using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public class KasaDbContext : DbContext
{
    public KasaDbContext(DbContextOptions<KasaDbContext> options) : base(options) { }

    public DbSet<KanalEntity> Kanallar => Set<KanalEntity>();
    public DbSet<CariEntity> Cariler => Set<CariEntity>();
    public DbSet<GiderKalemiEntity> GiderKalemleri => Set<GiderKalemiEntity>();
    public DbSet<IslemEntity> Islemler => Set<IslemEntity>();
    public DbSet<GelenEntity> Gelenler => Set<GelenEntity>();
    public DbSet<AyarEntity> Ayarlar => Set<AyarEntity>();
    public DbSet<KrediKartiEntity> KrediKartlari => Set<KrediKartiEntity>();
    public DbSet<KartOdemeEntity> KartOdemeler => Set<KartOdemeEntity>();
    public DbSet<CekEntity> Cekler => Set<CekEntity>();
    public DbSet<IptalEdilenTokenEntity> IptalEdilenTokenlar => Set<IptalEdilenTokenEntity>();
    /// <summary>Kasa sayımları. Var olan DB'lerde tabloyu SemaGuncelleyici ekler.</summary>
    public DbSet<KasaSayimEntity> KasaSayimlari => Set<KasaSayimEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Adlar iş anahtarıdır (işlem/gelen kanala ve cariye adla bağlı): tekil olmalı.
        // Var olan DB'lerde bu index'leri SemaGuncelleyici, çiftleri temizledikten sonra kurar.
        b.Entity<KanalEntity>().HasIndex(k => k.Ad).IsUnique();
        b.Entity<CariEntity>().HasIndex(c => c.Ad).IsUnique();
        b.Entity<GiderKalemiEntity>().HasIndex(k => k.Ad).IsUnique();
        // Dönem + kanal başına tek gelen satırı (eşzamanlı upsert çift satır üretemesin).
        b.Entity<GelenEntity>().HasIndex(g => new { g.DonemStart, g.Kanal }).IsUnique();

        b.Entity<IptalEdilenTokenEntity>().HasKey(t => t.Jti);

        // Çekler: raporlar işlem tarihine, liste ve özet vadeye göre okur.
        b.Entity<CekEntity>().HasIndex(c => c.IslemTarihi);
        b.Entity<CekEntity>().HasIndex(c => c.VadeTarihi);

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
