using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public class EditorGuvenlikEntity
{
    public int Id { get; set; } = 1;
    [JsonIgnore] public string? SifreHash { get; set; }
    [JsonIgnore] public string? KurtarmaHash { get; set; }
    public int Surum { get; set; }
}

/// <summary>
/// Alış belgesi (fiş, fatura, dekont). İçerik veritabanında değil içerik adresli belge deposundadır
/// (<see cref="Servisler.BelgeDeposu"/>); satır yalnız özeti tutar. Silme yumuşaktır (gap-denetim-izi-gozlemlenebilirlik-9):
/// satır ve içerik korunur, kimin, ne zaman ve hangi gerekçeyle kaldırdığı yazılır; yükleyen de saklanır. Bu sürümden önce
/// yüklenmiş belgelerde yükleyen bilinmez (null).
/// </summary>
public class BelgeEntity
{
    public int Id { get; set; }
    public int AlisId { get; set; }
    public int? OdemeId { get; set; }
    public string DosyaAdi { get; set; } = "";
    public string IcerikTuru { get; set; } = "";
    public long Boyut { get; set; }
    public DateTimeOffset Yuklendi { get; set; }
    /// <summary>İçeriğin SHA-256 özeti (64 haneli büyük harf onaltılık); belge deposundaki dosyanın adı. Veritabanı
    /// tetikleyicisi boş ya da biçimsiz özeti reddeder.</summary>
    public string IcerikOzeti { get; set; } = "";
    /// <summary>'editor' ya da 'alici'; eski belgelerde null.</summary>
    public string? YukleyenRol { get; set; }
    /// <summary>Yükleyen alıcının kimliği (editör paylaşılan tek hesaptır: null).</summary>
    public int? YukleyenId { get; set; }
    public bool Silindi { get; set; }
    public DateTimeOffset? SilinmeZamani { get; set; }
    public string? SilenRol { get; set; }
    public int? SilenId { get; set; }
    public string? SilmeGerekcesi { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<EditorGuvenlikEntity> EditorGuvenlik => Set<EditorGuvenlikEntity>();
    public DbSet<BelgeEntity> Belgeler => Set<BelgeEntity>();

    partial void ConfigureOperations(ModelBuilder b)
    {
        b.Entity<EditorGuvenlikEntity>().Property(e => e.Surum).IsConcurrencyToken();
        b.Entity<BelgeEntity>().HasOne<AlisEntity>().WithMany()
            .HasForeignKey(e => e.AlisId).OnDelete(DeleteBehavior.Restrict);
        // Yabancı anahtar dizini (AlisId) (AlisId, Silindi) dizininin önekidir; kural gereği kaldırılmasın diye açıkça tanımlıdır
        // (migration şeması ikisini de taşır).
        b.Entity<BelgeEntity>().HasIndex(e => e.AlisId);
        b.Entity<BelgeEntity>().HasIndex(e => new { e.AlisId, e.Silindi });
        b.Entity<BelgeEntity>().HasIndex(e => e.IcerikOzeti);
        b.Entity<BelgeEntity>().HasOne<AlisOdemeEntity>().WithMany()
            .HasForeignKey(e => e.OdemeId).OnDelete(DeleteBehavior.SetNull);
    }
}
