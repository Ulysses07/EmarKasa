using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public class KasaDbContext : DbContext
{
    private readonly IHttpContextAccessor? _http;
    private readonly TimeProvider _saat;

    /// <param name="http">Değişikliği yapanın rolü için (DI verir; doğrudan kurulan context'te yok).</param>
    /// <param name="saat">Geçmiş satırının zamanı için (DI verir; yoksa sistem saati).</param>
    public KasaDbContext(DbContextOptions<KasaDbContext> options, IHttpContextAccessor? http = null, TimeProvider? saat = null)
        : base(options)
    {
        _http = http;
        _saat = saat ?? TimeProvider.System;
    }

    public DbSet<KanalEntity> Kanallar => Set<KanalEntity>();
    public DbSet<CariEntity> Cariler => Set<CariEntity>();
    public DbSet<GiderKalemiEntity> GiderKalemleri => Set<GiderKalemiEntity>();
    public DbSet<IslemEntity> Islemler => Set<IslemEntity>();
    public DbSet<GelenEntity> Gelenler => Set<GelenEntity>();
    public DbSet<AyarEntity> Ayarlar => Set<AyarEntity>();
    public DbSet<KrediKartiEntity> KrediKartlari => Set<KrediKartiEntity>();
    public DbSet<KartOdemeEntity> KartOdemeler => Set<KartOdemeEntity>();
    public DbSet<CekEntity> Cekler => Set<CekEntity>();
    public DbSet<IptalEdilenTokenEntity> IptalEdilenTokenlar => Set<IptalEdilenTokenEntity>();
    /// <summary>Kasa sayımları. Var olan DB'lerde tabloyu SemaGuncelleyici ekler.</summary>
    public DbSet<KasaSayimEntity> KasaSayimlari => Set<KasaSayimEntity>();
    public DbSet<DegisiklikEntity> Degisiklikler => Set<DegisiklikEntity>();
    public DbSet<TekrarlayanGiderEntity> TekrarlayanGiderler => Set<TekrarlayanGiderEntity>();
    public DbSet<TekrarlayanGirisEntity> TekrarlayanGirisler => Set<TekrarlayanGirisEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Adlar iş anahtarıdır (işlem/gelen kanala ve cariye adla bağlı): tekil olmalı.
        // Var olan DB'lerde bu index'leri SemaGuncelleyici, çiftleri temizledikten sonra kurar.
        b.Entity<KanalEntity>().HasIndex(k => k.Ad).IsUnique();
        b.Entity<CariEntity>().HasIndex(c => c.Ad).IsUnique();
        b.Entity<GiderKalemiEntity>().HasIndex(k => k.Ad).IsUnique();
        // Dönem + kanal başına tek gelen satırı (eşzamanlı upsert çift satır üretemesin).
        b.Entity<GelenEntity>().HasIndex(g => new { g.DonemStart, g.Kanal }).IsUnique();

        b.Entity<IptalEdilenTokenEntity>().HasKey(t => t.Jti);

        // Çekler: raporlar işlem tarihine, liste ve özet vadeye göre okur.
        b.Entity<CekEntity>().HasIndex(c => c.IslemTarihi);
        b.Entity<CekEntity>().HasIndex(c => c.VadeTarihi);
        // Geçmiş: tür filtresi ve açılıştaki saklama temizliği için.
        b.Entity<DegisiklikEntity>().HasIndex(d => d.Tur);
        b.Entity<DegisiklikEntity>().HasIndex(d => d.ZamanUtc);

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

        // Tekrarlayan gider: ay başına tek karar (aynı ay iki kez onaylanamaz/atlanamaz).
        // Şablon silinince kararları da silinir; oluşan işlemler kalır. İşlem silinirse bağ kopar.
        b.Entity<TekrarlayanGirisEntity>().HasIndex(g => new { g.TekrarlayanGiderId, g.Ay }).IsUnique();
        b.Entity<TekrarlayanGirisEntity>()
            .HasOne<TekrarlayanGiderEntity>()
            .WithMany()
            .HasForeignKey(g => g.TekrarlayanGiderId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<TekrarlayanGirisEntity>()
            .HasOne<IslemEntity>()
            .WithMany()
            .HasForeignKey(g => g.IslemId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    // ------------------------------------------------------------------ değişiklik geçmişi

    private string? _rol;
    private bool _gecmisYaziliyor;
    private readonly List<(string Tur, int? KayitId, string Ozet, string? EskiJson, string? YeniJson)> _bekleyenToplu = new();

    /// <summary>
    /// Geçmişe yazılacak rol: HTTP isteğinde JWT'deki rol (editor / viewer). HTTP isteği DIŞINDAKİ
    /// yazmalarda (açılıştaki seed, şema göçü, testlerin doğrudan DB tohumlaması) null'dır ve bu
    /// yazmalar geçmişe YAZILMAZ: geçmiş, kullanıcıların yaptığı değişiklikler içindir. İleride bir
    /// arka plan işi yaptığı değişikliklerin görünmesini isterse bunu açıkça atar (ör. "sistem").
    /// </summary>
    public string? DegistirenRol
    {
        get => _rol ?? (_http?.HttpContext is { } h ? h.User.FindFirstValue(ClaimTypes.Role) ?? "anonim" : null);
        set => _rol = value;
    }

    /// <summary>true iken bu context'te eklenen kayıtlar "Eklendi (geri alındı)" olarak yazılır.</summary>
    public bool GeriAlmaKaydi { get; set; }

    /// <summary>
    /// ExecuteUpdate/ExecuteDelete değişiklik izleyiciyi atlar; onlar için tek bir özet satırı
    /// buradan eklenir ve sonraki SaveChanges ile aynı transaction'da yazılır.
    /// </summary>
    public void TopluDegisiklikEkle(string tur, int? kayitId, string ozet, object? eski = null, object? yeni = null)
        => _bekleyenToplu.Add((tur, kayitId, ozet,
            eski is null ? null : GecmisJson.Yaz(eski), yeni is null ? null : GecmisJson.Yaz(yeni)));

    /// <summary>
    /// Değişiklikleri kaydeder ve (HTTP isteğindeyse) her eklenen/güncellenen/silinen kayıt için
    /// geçmiş satırı yazar. Geçmiş satırları, eklenen kayıtların Id'si belli olsun diye ikinci bir
    /// turda yazılır; iki tur da aynı transaction'dadır (dışarıda açık yoksa burada açılır), yani
    /// kayıt geçmişsiz ya da geçmiş kayıtsız kalamaz.
    /// </summary>
    /// <remarks>
    /// Geçmiş yazılırken ilk tur değişiklikleri her zaman kabul eder (acceptAllChangesOnSuccess=true):
    /// aksi halde ikinci tur onları yeniden yazardı. Bu kod tabanı false kullanmıyor.
    /// </remarks>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (_gecmisYaziliyor) return base.SaveChanges(acceptAllChangesOnSuccess);
        if (DegistirenRol is not { } rol)
        {
            _bekleyenToplu.Clear();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        var yakalanan = DegisiklikKaydedici.Yakala(this);
        if (yakalanan.Count == 0 && _bekleyenToplu.Count == 0) return base.SaveChanges(acceptAllChangesOnSuccess);

        _gecmisYaziliyor = true;
        var tx = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        try
        {
            var sonuc = base.SaveChanges(acceptAllChangesOnSuccess: true);
            Degisiklikler.AddRange(GecmisSatirlari(yakalanan, rol));
            base.SaveChanges(acceptAllChangesOnSuccess: true);
            tx?.Commit();
            return sonuc;
        }
        finally
        {
            _gecmisYaziliyor = false;
            _bekleyenToplu.Clear();
            tx?.Dispose();
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (_gecmisYaziliyor) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (DegistirenRol is not { } rol)
        {
            _bekleyenToplu.Clear();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        var yakalanan = DegisiklikKaydedici.Yakala(this);
        if (yakalanan.Count == 0 && _bekleyenToplu.Count == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        _gecmisYaziliyor = true;
        var tx = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var sonuc = await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
            Degisiklikler.AddRange(GecmisSatirlari(yakalanan, rol));
            await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            return sonuc;
        }
        finally
        {
            _gecmisYaziliyor = false;
            _bekleyenToplu.Clear();
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    private List<DegisiklikEntity> GecmisSatirlari(List<DegisiklikKaydedici.Bekleyen> yakalanan, string rol)
    {
        var zaman = _saat.GetUtcNow().UtcDateTime;
        var satirlar = yakalanan.Select(b => DegisiklikKaydedici.Satir(this, b, rol, zaman, GeriAlmaKaydi)).ToList();
        satirlar.AddRange(_bekleyenToplu.Select(t => new DegisiklikEntity
        {
            ZamanUtc = zaman, Rol = rol, Tur = t.Tur, KayitId = t.KayitId, Eylem = Eylemler.Guncellendi,
            Ozet = t.Ozet, EskiJson = t.EskiJson, YeniJson = t.YeniJson,
        }));
        return satirlar;
    }
}
