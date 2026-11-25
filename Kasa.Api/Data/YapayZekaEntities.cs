using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>Bir yapay zekâ çağrısı (docs/specs/2026-11-25-yapay-zeka-onerileri.md): harcama sınırının ve tekrar isteğin kaydı. Gönderilen metin ve
/// yanıt saklanmaz.</summary>
public sealed class YapayZekaCagriEntity
{
    public int Id { get; set; }
    public Guid IstekId { get; set; }
    /// <summary><see cref="Kasa.Core.Kodlar.YapayZekaCagriTurleri"/>.</summary>
    public string Tur { get; set; } = "";
    public int? BelgeId { get; set; }
    /// <summary>İstanbul ayı (yyyy-MM): harcama sınırının dönemi.</summary>
    public string Ay { get; set; } = "";
    /// <summary>Çağrının zamanı (Unix milisaniye).</summary>
    public long Zaman { get; set; }
    public string Model { get; set; } = "";
    public int SatirSayisi { get; set; }
    public long GirdiJetonu { get; set; }
    public long CiktiJetonu { get; set; }
    /// <summary>Çağrı sürerken (ve sonucu bilinmiyorsa) ayrılan en yüksek maliyet, sonra gerçek maliyet; ABD doları.</summary>
    public decimal MaliyetUsd { get; set; }
    /// <summary><see cref="Kasa.Core.Kodlar.YapayZekaCagriDurumlari"/>.</summary>
    public string Durum { get; set; } = "";
    /// <summary>İstemciye dönen sonuç (YapayZekaSonucDto ya da YapayZekaOlcumDto): yanıtı kaybolan istek aynı kimlikle yinelenince aynen döner.</summary>
    public string? SonucJson { get; set; }
}

/// <summary>Belgenin sorulan satırı ve gelen öneri. Satır kaydedilse de kalır: doğruluk, kaydedilen kararla karşılaştırılarak okunur. Öneri gelmediyse
/// <see cref="IslemTuru"/> boştur: satır yeniden sorulabilir ama hiç sorulmamışlardan sonra.</summary>
public sealed class YapayZekaOneriEntity
{
    public int Id { get; set; }
    public int BelgeId { get; set; }
    public int SatirNo { get; set; }
    public int CagriId { get; set; }
    /// <summary><see cref="Kasa.Core.Kodlar.EkstreIslemTurleri"/>; öneri gelmediyse boş.</summary>
    public string IslemTuru { get; set; } = "";
    public string? DagilimTuru { get; set; }
    public string KanalIdsJson { get; set; } = "[]";
    public string Kategori { get; set; } = "";
    public string? Aciklama { get; set; }
    /// <summary><see cref="Kasa.Core.Kodlar.YapayZekaGuvenleri"/>.</summary>
    public string Guven { get; set; } = "";
}

public partial class KasaDbContext
{
    public DbSet<YapayZekaCagriEntity> YapayZekaCagrilar => Set<YapayZekaCagriEntity>();
    public DbSet<YapayZekaOneriEntity> YapayZekaOneriler => Set<YapayZekaOneriEntity>();

    static void ConfigureYapayZeka(ModelBuilder b)
    {
        b.Entity<YapayZekaCagriEntity>().HasIndex(c => c.IstekId).IsUnique();
        b.Entity<YapayZekaCagriEntity>().HasIndex(c => c.Ay);
        b.Entity<YapayZekaCagriEntity>().HasOne<EkstreBelgeEntity>().WithMany().HasForeignKey(c => c.BelgeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<YapayZekaOneriEntity>().HasIndex(o => new { o.BelgeId, o.SatirNo }).IsUnique();
        b.Entity<YapayZekaOneriEntity>().HasOne<EkstreBelgeEntity>().WithMany().HasForeignKey(o => o.BelgeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<YapayZekaOneriEntity>().HasOne<YapayZekaCagriEntity>().WithMany().HasForeignKey(o => o.CagriId).OnDelete(DeleteBehavior.Restrict);
    }
}
