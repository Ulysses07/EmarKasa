using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public partial class KasaDbContext : DbContext
{
    public KasaDbContext(DbContextOptions<KasaDbContext> options) : base(options) { }

    public DbSet<KanalEntity> Kanallar => Set<KanalEntity>();
    public DbSet<CariEntity> Cariler => Set<CariEntity>();
    public DbSet<IslemEntity> Islemler => Set<IslemEntity>();
    public DbSet<GelenEntity> Gelenler => Set<GelenEntity>();
    public DbSet<AyarEntity> Ayarlar => Set<AyarEntity>();
    public DbSet<KrediKartiEntity> KrediKartlari => Set<KrediKartiEntity>();
    public DbSet<KartOdemeEntity> KartOdemeler => Set<KartOdemeEntity>();
    public DbSet<KrediEntity> Krediler => Set<KrediEntity>();
    public DbSet<AliciEntity> Alicilar => Set<AliciEntity>();
    public DbSet<AlisEntity> Alislar => Set<AlisEntity>();
    public DbSet<AlisKalemEntity> AlisKalemler => Set<AlisKalemEntity>();
    public DbSet<AlisDagilimEntity> AlisDagilimlar => Set<AlisDagilimEntity>();
    public DbSet<AlisOdemeEntity> AlisOdemeler => Set<AlisOdemeEntity>();
    public DbSet<HesapEntity> Hesaplar => Set<HesapEntity>();
    public DbSet<HesapHareketEntity> HesapHareketler => Set<HesapHareketEntity>();
    public DbSet<HesapTransferEntity> HesapTransferler => Set<HesapTransferEntity>();
    public DbSet<FinansIstekEntity> FinansIstekler => Set<FinansIstekEntity>();
    public DbSet<KrediTaksitOdemeEntity> KrediTaksitOdemeler => Set<KrediTaksitOdemeEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AliciEntity>().Property(a => a.Kullanici).UseCollation("NOCASE");
        b.Entity<AliciEntity>().HasIndex(a => a.Kullanici).IsUnique();
        b.Entity<AlisEntity>().Property(a => a.Surum).IsConcurrencyToken();
        b.Entity<AlisEntity>().HasOne(a => a.TedarikciKaydi).WithMany().HasForeignKey(a => a.TedarikciId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AlisEntity>().HasOne(a => a.AliciKaydi).WithMany()
            .HasForeignKey(a => a.AliciId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AlisEntity>().HasMany(a => a.Kalemler).WithOne()
            .HasForeignKey(k => k.AlisId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AlisKalemEntity>().HasMany(k => k.Dagilimlar).WithOne()
            .HasForeignKey(d => d.AlisKalemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AlisDagilimEntity>().HasOne(d => d.KanalKaydi).WithMany()
            .HasForeignKey(d => d.KanalId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AlisDagilimEntity>().HasIndex(d => new { d.AlisKalemId, d.KanalId }).IsUnique();
        b.Entity<AlisEntity>().HasMany(a => a.Odemeler).WithOne()
            .HasForeignKey(o => o.AlisId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AlisOdemeEntity>().HasOne(o => o.Islem).WithMany()
            .HasForeignKey(o => o.IslemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AlisOdemeEntity>().HasIndex(o => o.IslemId).IsUnique();
        b.Entity<AlisOdemeEntity>().HasIndex(o => o.IstekId).IsUnique();

        b.Entity<KanalEntity>().Property(k => k.Ad).UseCollation("NOCASE");
        b.Entity<KanalEntity>().HasIndex(k => k.Ad).IsUnique();

        b.Entity<IslemEntity>()
            .HasOne(i => i.KanalKaydi).WithMany().HasForeignKey(i => i.KanalId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<GelenEntity>()
            .HasOne(g => g.KanalKaydi).WithMany().HasForeignKey(g => g.KanalId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<KrediEntity>()
            .HasOne(k => k.KanalKaydi).WithMany().HasForeignKey(k => k.KanalId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<GelenEntity>().Property(g => g.EskiYinelenenGrup).HasDefaultValue(false);
        b.Entity<GelenEntity>().HasIndex(g => new { g.DonemStart, g.KanalId }).IsUnique()
            .HasFilter("\"EskiYinelenenGrup\" = 0");
        // Eski JSON sözleşmesindeki metin alanını da korur; NULL KanalId taşıyan
        // geçmiş/özel etiket kayıtlarında SQLite'ın NULL tekilliği atlanmasın.
        b.Entity<GelenEntity>().Property(g => g.Kanal).UseCollation("NOCASE");
        b.Entity<GelenEntity>().HasIndex(g => new { g.DonemStart, g.Kanal }).IsUnique()
            .HasFilter("\"EskiYinelenenGrup\" = 0");

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
        b.Entity<HesapEntity>().Property(h => h.Surum).IsConcurrencyToken();
        b.Entity<HesapEntity>().Property(h => h.Ad).UseCollation("NOCASE");
        b.Entity<HesapEntity>().HasIndex(h => h.Ad).IsUnique();
        b.Entity<HesapHareketEntity>().HasOne<HesapEntity>().WithMany().HasForeignKey(h => h.HesapId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HesapHareketEntity>().HasOne(h => h.Islem).WithOne(i => i.HesapHareketi).HasForeignKey<HesapHareketEntity>(h => h.IslemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HesapHareketEntity>().HasOne(h => h.Gelen).WithMany().HasForeignKey(h => h.GelenId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HesapHareketEntity>().HasOne(h => h.KartOdeme).WithMany().HasForeignKey(h => h.KartOdemeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HesapHareketEntity>().HasOne(h => h.Kredi).WithMany().HasForeignKey(h => h.KrediId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HesapHareketEntity>().HasOne(h => h.Kanal).WithMany().HasForeignKey(h => h.KanalId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HesapHareketEntity>().HasIndex(h => h.GelenId).IsUnique();
        b.Entity<HesapHareketEntity>().HasIndex(h => h.KartOdemeId).IsUnique();
        b.Entity<HesapHareketEntity>().HasIndex(h => h.KrediId).IsUnique();
        b.Entity<HesapTransferEntity>().HasOne<HesapEntity>().WithMany().HasForeignKey(t => t.KaynakHesapId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<HesapTransferEntity>().HasOne<HesapEntity>().WithMany().HasForeignKey(t => t.HedefHesapId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FinansIstekEntity>().HasIndex(i => i.IstekId).IsUnique();
        b.Entity<KrediTaksitOdemeEntity>().HasOne<KrediEntity>().WithMany().HasForeignKey(o => o.KrediId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<KrediTaksitOdemeEntity>().HasOne(o => o.Islem).WithMany().HasForeignKey(o => o.IslemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<KrediTaksitOdemeEntity>().HasIndex(o => new { o.KrediId, o.TaksitNo }).IsUnique();
        b.Entity<KrediTaksitOdemeEntity>().HasIndex(o => o.IslemId).IsUnique();
        ConfigureOperations(b);
        ConfigureCardLoanTracking(b);
        ConfigureNotifications(b);
        ConfigureMonthlyExpensesAndLocks(b);
        ConfigureCashControls(b);
        ConfigureStatementImports(b);
    }

    partial void ConfigureOperations(ModelBuilder b);
    partial void ConfigureNotifications(ModelBuilder b);
    partial void ConfigureMonthlyExpensesAndLocks(ModelBuilder b);
    partial void ConfigureCashControls(ModelBuilder b);
    partial void ConfigureStatementImports(ModelBuilder b);
}
