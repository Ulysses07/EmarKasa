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

// Belgeler veritabanıyla aynı tutarlı yedeğe girer; web kökünde dosya tutulmaz.
public class BelgeEntity
{
    public int Id { get; set; }
    public int AlisId { get; set; }
    public int? OdemeId { get; set; }
    public string DosyaAdi { get; set; } = "";
    public string IcerikTuru { get; set; } = "";
    public long Boyut { get; set; }
    public DateTimeOffset Yuklendi { get; set; }
    [JsonIgnore] public byte[] Icerik { get; set; } = [];
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
        b.Entity<BelgeEntity>().HasOne<AlisOdemeEntity>().WithMany()
            .HasForeignKey(e => e.OdemeId).OnDelete(DeleteBehavior.SetNull);
    }
}
