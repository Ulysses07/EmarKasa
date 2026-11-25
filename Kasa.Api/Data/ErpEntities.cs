using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>
/// ERP12'den aktarılan cari hareketi (docs/specs/2026-11-25-erp12-aktarimi.md). Aslı ERP12'dedir; burada değiştirilmez ve tek başına kasayı
/// etkilemez. Kasa hareketi olan satır belgenin <see cref="SatirNo"/> numaralı satırıdır (eşleşme, nakit kaydı, yok sayma orada); fatura,
/// devir ve düzeltmede null.
/// </summary>
public sealed class ErpHareketEntity
{
    public int Id { get; set; }
    public int BelgeId { get; set; }
    public int? SatirNo { get; set; }
    public DateOnly Tarih { get; set; }
    public string? CariKodu { get; set; }
    public string CariAdi { get; set; } = "";
    /// <summary>Carinin kimliği: kodu varsa kodu, yoksa sadeleştirilmiş adı (<see cref="ErpEslestirme.CariAnahtari"/>).</summary>
    public string CariAnahtari { get; set; } = "";
    public string? IslemTuru { get; set; }
    public string? BelgeNo { get; set; }
    public string? Aciklama { get; set; }
    public decimal Borc { get; set; }
    public decimal Alacak { get; set; }
    /// <summary>Hareketin içerik özeti (aynı içerikten kaçıncı olduğu dahil): örtüşen dışa aktarımda aynı hareket ikinci kez alınmaz.</summary>
    public string Ozet { get; set; } = "";
}

public partial class KasaDbContext
{
    public DbSet<ErpHareketEntity> ErpHareketler => Set<ErpHareketEntity>();

    static void ConfigureErp(ModelBuilder b)
    {
        b.Entity<ErpHareketEntity>().HasOne<EkstreBelgeEntity>().WithMany().HasForeignKey(h => h.BelgeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ErpHareketEntity>().HasIndex(h => h.Ozet).IsUnique();
        b.Entity<ErpHareketEntity>().HasIndex(h => new { h.BelgeId, h.SatirNo });
        b.Entity<ErpHareketEntity>().HasIndex(h => h.CariAnahtari);
    }
}
