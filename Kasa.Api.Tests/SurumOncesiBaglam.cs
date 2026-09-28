using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kasa.Api.Tests;

/// <summary>
/// Çekirdek sürüm sütunlarından (CekirdekSurumleri, contract-6) önceki şemada çalışan bağlam. Çalışma modeli Islemler, Gelenler,
/// Kanallar ve Ayarlar'da Surum sütununu okur ve yazar; o sütun henüz yokken (önceki sürümün şeması) göç testleri önceki sürümün
/// verisini ve göç öncesi raporunu bu bağlamla kurar ve okur, göçü ve sonrasını olağan bağlamla. Aynı seçeneklerle (bağlantı,
/// saat) kurulur; sütun yoksayılmış kendi modeli vardır. Migration uygulamaz ve bekleyen migration'ları göremez (EF migration'ları
/// bağlam türüne bağlar): Migrate ve GetPendingMigrations olağan bağlamla çağrılır.
/// </summary>
public sealed class SurumOncesiBaglam(DbContextOptions<KasaDbContext> secenekler) : KasaDbContext(secenekler)
{
    /// <summary>Verilen bağlamın seçenekleriyle (aynı bağlantı ve saat) sürüm öncesi bağlam.</summary>
    public static SurumOncesiBaglam Ayni(KasaDbContext db) => new((DbContextOptions<KasaDbContext>)db.GetService<IDbContextOptions>());

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<IslemEntity>().Ignore(x => x.Surum);
        b.Entity<GelenEntity>().Ignore(x => x.Surum);
        b.Entity<KanalEntity>().Ignore(x => x.Surum);
        b.Entity<AyarEntity>().Ignore(x => x.Surum);
    }
}
