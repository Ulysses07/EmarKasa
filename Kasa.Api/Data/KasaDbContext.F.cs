using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

// Paket F: işlem ekleri ve POS tabloları. Var olan DB'lerde tabloları ve işlemlerin belge
// sütunlarını SemaGuncelleyici ekler (hepsi yeni tablo ya da NULL/varsayılanlı sütun).
public partial class KasaDbContext
{
    public DbSet<IslemEkiEntity> IslemEkleri => Set<IslemEkiEntity>();
    public DbSet<PosTanimEntity> PosTanimlari => Set<PosTanimEntity>();
    public DbSet<PosSatisEntity> PosSatislari => Set<PosSatisEntity>();

    /// <summary>Context'in saatine göre şu an (UTC): silinen işlemin eklerine silinme zamanı yazılırken kullanılır.</summary>
    internal DateTime SimdiUtc => _saat.GetUtcNow().UtcDateTime;

    private static void BelgeVePosModeli(ModelBuilder b)
    {
        // Ek, işleme FK'siz bağlıdır (işlem silinince 30 gün geri alınabilsin diye kalır).
        b.Entity<IslemEkiEntity>().HasIndex(e => e.IslemId);
        b.Entity<IslemEkiEntity>().HasIndex(e => e.SilinenIslemId);
        b.Entity<IslemEkiEntity>().HasIndex(e => e.DepoAdi).IsUnique();
        // DB varsayılanı: işlem tablosuna belge sütunlarını bilmeden (ör. elle SQL, eski araç) yazılan
        // satır da geçerli olsun. Yeni kurulan DB'de de sütun "NOT NULL DEFAULT 0" olur.
        b.Entity<IslemEntity>().Property(i => i.FaturaBekleniyor).HasDefaultValue(false);

        b.Entity<PosTanimEntity>().HasIndex(p => p.Ad).IsUnique();
        // Kanal silinirse POS kalır, kanalı boşalır.
        b.Entity<PosTanimEntity>()
            .HasOne<KanalEntity>()
            .WithMany()
            .HasForeignKey(p => p.KanalId)
            .OnDelete(DeleteBehavior.SetNull);

        b.Entity<PosSatisEntity>().HasIndex(s => s.Tarih);
        // Satışın kanalı kayıt anında sabitlenir; kanal silinirse satış kalır, "Kanalsız" olur.
        b.Entity<PosSatisEntity>()
            .HasOne<KanalEntity>()
            .WithMany()
            .HasForeignKey(s => s.KanalId)
            .OnDelete(DeleteBehavior.SetNull);
        // Satışı olan POS silinemez (önce pasif yapılır ya da satışlar silinir).
        b.Entity<PosSatisEntity>()
            .HasOne<PosTanimEntity>()
            .WithMany()
            .HasForeignKey(s => s.PosId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
