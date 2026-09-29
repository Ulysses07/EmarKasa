using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>
/// Merkezi denetim olayı (yalnız eklenir): kasayı değiştiren her ekleme/değiştirme/silme, ay kilidi açma/kapatma ve
/// güvenlik olayları (giriş, hız sınırı, şifre/kurtarma, oturum iptali). Satır değiştirilemez ve silinemez: veritabanı
/// tetikleyicileri (TR_DenetimOlaylari_*) UPDATE/DELETE'i, <see cref="KasaDbContext"/> kancası EF üzerinden değişikliği
/// reddeder. Parola, parola özeti, kurtarma kodu ve oturum belirteci hiçbir alana yazılmaz (bkz. Denetim/DenetimYakalayici).
/// </summary>
public class DenetimOlayEntity
{
    public int Id { get; set; }
    /// <summary>UTC an, Unix milisaniye (SQLite'ta sıralanabilir tam sayı).</summary>
    public long ZamanUtc { get; set; }
    /// <summary>'editor', 'viewer', 'alici', 'anonim' (kimliksiz istek: giriş, kurtarma) ya da 'sistem' (açılış, bakım, işçi).</summary>
    public string AktorRol { get; set; } = "";
    /// <summary>Kişisel hesabı olan aktörün kimliği (alıcı); paylaşılan editör/izleyici hesabında null.</summary>
    public int? AktorId { get; set; }
    /// <summary>Güvenilen vekilin bildirdiği gerçek istemci IP'si (ForwardedHeaders sonrası); istek dışında null.</summary>
    public string? IstemciIp { get; set; }
    /// <summary>'Ekle' | 'Degistir' | 'Sil' ya da özel olay ('KilitAc', 'GirisBasarisiz', 'GecmisAyEtkisi', 'BagKoptu': üst kaydın
    /// silinmesiyle veritabanının kopardığı bağ...).</summary>
    public string Tur { get; set; } = "";
    /// <summary>Varlık adı ('Entity' eki atılmış CLR adı: Islem, Gelen, TakipKartOdeme...) ya da 'Oturum'.</summary>
    public string Varlik { get; set; } = "";
    /// <summary>Birincil anahtar (bileşikte '|' ile birleşik).</summary>
    public string? VarlikId { get; set; }
    /// <summary>Değişiklikte yalnız değişen alanların önceki değerleri; silmede bütün alanlar.</summary>
    public string? OncekiJson { get; set; }
    /// <summary>Değişiklikte yalnız değişen alanların yeni değerleri; eklemede bütün alanlar.</summary>
    public string? YeniJson { get; set; }
    public string? Gerekce { get; set; }
    /// <summary>İsteğin tekrar koruma kimliği (FinansIstekler.IstekId ile aynı biçim); yoksa null.</summary>
    public Guid? IstekId { get; set; }
    /// <summary>İsteği loglara bağlayan iz (Activity TraceId ya da HttpContext.TraceIdentifier).</summary>
    public string? TraceId { get; set; }
    /// <summary>Değişiklik, bir ay kilidi açılışının açtığı pencereye (o açılışla açılmış ve henüz yeniden kilitlenmemiş
    /// tarihlere) düşüyorsa o açılışın <see cref="AyKilidiOlayEntity.Id"/>'si.</summary>
    public int? KilitAcmaOlayiId { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<DenetimOlayEntity> DenetimOlaylari => Set<DenetimOlayEntity>();

    /// <summary>Uç kodunun bu bağlamdaki sonraki kayıtlara iliştirdiği gerekçe (ör. takip iptali açıklaması). Yalnız
    /// <see cref="DenetimKapsami"/> ile atanır; kapsam bitince önceki değere döner.</summary>
    internal string? DenetimGerekcesi { get; set; }
    /// <summary>Uç kodunun bu bağlamdaki sonraki kayıtlara iliştirdiği istek kimliği (FinansIstek ile aynı).</summary>
    internal Guid? DenetimIstekId { get; set; }
    /// <summary>DenetimOlaylari tablosu bu bağlantıda görüldü mü (bir kez görülünce yeniden sorulmaz; yoksa her kayıtta
    /// sorulur: migration'dan önce açılan bağlam da tablo kurulduktan sonra olay yazar).</summary>
    internal bool DenetimTablosuGoruldu { get; set; }

    /// <summary>Kapsam boyunca bu bağlamın kayıtlarına gerekçe ve istek kimliği iliştirir; boş gerekçe yok sayılır.</summary>
    internal DenetimKapsami Denetle(string? gerekce, Guid? istekId = null) => new(this, gerekce, istekId);

    internal sealed class DenetimKapsami : IDisposable
    {
        private readonly KasaDbContext _db;
        private readonly string? _oncekiGerekce;
        private readonly Guid? _oncekiIstekId;

        internal DenetimKapsami(KasaDbContext db, string? gerekce, Guid? istekId)
        {
            _db = db;
            _oncekiGerekce = db.DenetimGerekcesi;
            _oncekiIstekId = db.DenetimIstekId;
            if (!string.IsNullOrWhiteSpace(gerekce))
                db.DenetimGerekcesi = gerekce.Trim();
            if (istekId is { } id && id != Guid.Empty)
                db.DenetimIstekId = id;
        }

        public void Dispose() { _db.DenetimGerekcesi = _oncekiGerekce; _db.DenetimIstekId = _oncekiIstekId; }
    }

    partial void ConfigureDenetim(ModelBuilder b)
    {
        b.Entity<DenetimOlayEntity>().ToTable("DenetimOlaylari");
        b.Entity<DenetimOlayEntity>().HasIndex(o => new { o.Varlik, o.VarlikId });
        b.Entity<DenetimOlayEntity>().HasIndex(o => o.ZamanUtc);
        b.Entity<DenetimOlayEntity>().HasIndex(o => o.KilitAcmaOlayiId);
    }
}
