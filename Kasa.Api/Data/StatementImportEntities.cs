using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public class EkstreBelgeEntity
{
    public int Id { get; set; }
    public int Surum { get; set; } = 1;
    public string Kaynak { get; set; } = "";
    public string Banka { get; set; } = "";
    public string HesapAdi { get; set; } = "";
    public int? KartId { get; set; }
    public string DosyaAdi { get; set; } = "";
    public string DosyaOzeti { get; set; } = "";
    public byte[] Dosya { get; set; } = [];
    public long Yuklendi { get; set; }
    public string SatirlarJson { get; set; } = "[]";
    public string UyarilarJson { get; set; } = "[]";
}

public class EkstreKayitEntity
{
    public int Id { get; set; }
    public int BelgeId { get; set; }
    public int SatirNo { get; set; }
    public DateOnly Tarih { get; set; }
    public string Aciklama { get; set; } = "";
    public decimal Tutar { get; set; }
    public string IslemTuru { get; set; } = "";
    public string DagilimTuru { get; set; } = "";
    public string DagilimJson { get; set; } = "[]";
    public int? KrediKartiId { get; set; }
    public int? IslemId { get; set; }
    public int? KartHarcamaId { get; set; }
    public int? KartOdemeId { get; set; }
    public bool Iptal { get; set; }
    public string? IptalAciklamasi { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<EkstreBelgeEntity> EkstreBelgeler => Set<EkstreBelgeEntity>();
    public DbSet<EkstreKayitEntity> EkstreKayitlar => Set<EkstreKayitEntity>();
    internal bool EkstreDegisikligi { get; set; }

    partial void ConfigureStatementImports(ModelBuilder b)
    {
        b.Entity<EkstreBelgeEntity>().HasIndex(d => d.DosyaOzeti).IsUnique();
        b.Entity<EkstreBelgeEntity>().Property(d => d.Surum).IsConcurrencyToken();
        b.Entity<EkstreBelgeEntity>().HasOne<KrediKartiEntity>().WithMany().HasForeignKey(d => d.KartId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EkstreKayitEntity>().HasOne<EkstreBelgeEntity>().WithMany().HasForeignKey(d => d.BelgeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EkstreKayitEntity>().HasOne<KrediKartiEntity>().WithMany().HasForeignKey(d => d.KrediKartiId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EkstreKayitEntity>().HasOne<IslemEntity>().WithMany().HasForeignKey(d => d.IslemId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<EkstreKayitEntity>().HasOne<TakipHarcamaEntity>().WithMany().HasForeignKey(d => d.KartHarcamaId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EkstreKayitEntity>().HasOne<TakipKartOdemeEntity>().WithMany().HasForeignKey(d => d.KartOdemeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EkstreKayitEntity>().HasIndex(d => new { d.BelgeId, d.SatirNo }).IsUnique().HasFilter("\"Iptal\" = 0");
        b.Entity<EkstreKayitEntity>().HasIndex(d => d.IslemId).IsUnique();
        b.Entity<EkstreKayitEntity>().HasIndex(d => d.KartHarcamaId).IsUnique();
        b.Entity<EkstreKayitEntity>().HasIndex(d => d.KartOdemeId).IsUnique();
    }
}
