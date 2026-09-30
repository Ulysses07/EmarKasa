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

/// <summary>
/// Kasa kontrolü: kayıt anındaki genel kasa (SistemBakiye), bildirilen gerçek bakiye ve farkı. Filigran
/// (gap-denetim-izi-gozlemlenebilirlik-3; KasaKontrolFiligrani migration'ından önceki kayıtlarda null): kaydın hesaplandığı
/// gün, kanal bakiyeleri ve o anda veritabanındaki son gider, mali istek ve denetim olayı kimlikleri; "kontrolden beri
/// değişenler" bu kimliklerden sonrakilerdir. Tutarlar sonradan değişmez; yalnız fark açıklaması sürüm denetimiyle yazılır.
/// </summary>
public sealed class KasaKontrolEntity
{
    public int Id { get; set; }
    public long Kaydedildi { get; set; }
    public decimal SistemBakiye { get; set; }
    public decimal GercekBakiye { get; set; }
    public decimal Fark { get; set; }
    public string? Not { get; set; }
    /// <summary>Fark açıklaması yazıldıkça artar (eski kayıtlarda 1).</summary>
    public int Surum { get; set; } = 1;
    /// <summary>SistemBakiye'nin hesaplandığı kasa günü (İstanbul).</summary>
    public DateOnly? HesapTarihi { get; set; }
    /// <summary>Kayıt anındaki kanal bakiyeleri: <c>[{ KanalId, Kanal, Bakiye }]</c> (panelin kanal kasaları).</summary>
    public string? KanalBakiyeleriJson { get; set; }
    /// <summary>Kayıt anındaki en büyük Islemler.Id (yoksa 0).</summary>
    public int? SonIslemId { get; set; }
    /// <summary>Kayıt anındaki en büyük FinansIstekler.Id (bu kontrolün kendi isteğinden önce; yoksa 0).</summary>
    public int? SonFinansIstekId { get; set; }
    /// <summary>Kayıt anındaki en büyük DenetimOlaylari.Id (bu kontrolün kendi olayından önce; yoksa 0).</summary>
    public int? SonDenetimOlayId { get; set; }
    /// <summary>Farkın sonradan yazılan açıklaması (PUT /api/kasa-kontrol/{id}/aciklama).</summary>
    public string? FarkAciklamasi { get; set; }
    /// <summary>Açıklamanın yazıldığı UTC an, Unix milisaniye.</summary>
    public long? FarkAciklamaZamani { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<KasaEsikEntity> KasaEsikleri => Set<KasaEsikEntity>();
    public DbSet<KasaKontrolEntity> KasaKontrolleri => Set<KasaKontrolEntity>();

    partial void ConfigureKasaKontrol(ModelBuilder b)
    {
        b.Entity<KasaEsikEntity>().ToTable("KasaEsikleri");
        b.Entity<KasaEsikEntity>().HasIndex(x => x.KanalId).IsUnique();
        b.Entity<KasaEsikEntity>().Property(x => x.Surum).IsConcurrencyToken();
        b.Entity<KasaEsikEntity>().HasOne<KanalEntity>().WithMany().HasForeignKey(x => x.KanalId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<KasaKontrolEntity>().ToTable("KasaKontrolleri");
        b.Entity<KasaKontrolEntity>().Property(x => x.Surum).IsConcurrencyToken();
    }
}
