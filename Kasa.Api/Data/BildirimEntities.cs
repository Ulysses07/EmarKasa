using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public sealed class BildirimAyarEntity
{
    public int Id { get; set; } = 1;
    public bool Etkin { get; set; } = true;
    public int Saat { get; set; } = 9;
    public int Dakika { get; set; }
    public int Surum { get; set; } = 1;
}

public sealed class BildirimEntity
{
    public int Id { get; set; }
    public string OlayAnahtari { get; set; } = "";
    public string Baslik { get; set; } = "";
    public string Mesaj { get; set; } = "";
    public DateOnly Tarih { get; set; }
    public string Hedef { get; set; } = "";
    public string Tur { get; set; } = "";
    public int KaynakId { get; set; }
    public bool Okundu { get; set; }
    public bool Iptal { get; set; }
}

public sealed class PushAbonelikEntity
{
    public int Id { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string CihazId { get; set; } = "";
    public string CihazAdi { get; set; } = "";
    public string OturumDamgasi { get; set; } = "";
    public long Olusturuldu { get; set; }
    public long? SonBasarili { get; set; }
    public bool Etkin { get; set; } = true;
}

public sealed class BildirimTeslimEntity
{
    public int Id { get; set; }
    public int BildirimId { get; set; }
    public int AbonelikId { get; set; }
    public int Deneme { get; set; }
    public long SonrakiDeneme { get; set; }
    public long KilitBitis { get; set; }
    public string? Kilit { get; set; }
    public long? Gonderildi { get; set; }
    public bool Iptal { get; set; }
}

public partial class KasaDbContext
{
    partial void ConfigureNotifications(ModelBuilder b)
    {
        b.Entity<BildirimAyarEntity>().ToTable("BildirimAyarlari");
        b.Entity<BildirimAyarEntity>().Property(x => x.Surum).IsConcurrencyToken();
        b.Entity<BildirimEntity>().ToTable("Bildirimler");
        b.Entity<BildirimEntity>().HasIndex(x => x.OlayAnahtari).IsUnique();
        b.Entity<PushAbonelikEntity>().ToTable("PushAbonelikler");
        b.Entity<PushAbonelikEntity>().HasIndex(x => x.Endpoint).IsUnique();
        b.Entity<BildirimTeslimEntity>().ToTable("BildirimTeslimler");
        b.Entity<BildirimTeslimEntity>().HasIndex(x => new { x.BildirimId, x.AbonelikId }).IsUnique();
        b.Entity<BildirimTeslimEntity>().HasOne<BildirimEntity>().WithMany().HasForeignKey(x => x.BildirimId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BildirimTeslimEntity>().HasOne<PushAbonelikEntity>().WithMany().HasForeignKey(x => x.AbonelikId).OnDelete(DeleteBehavior.Cascade);
    }
}
