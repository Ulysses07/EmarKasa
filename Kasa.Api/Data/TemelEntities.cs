using Kasa.Core;
using System.Text.Json.Serialization;

namespace Kasa.Api.Data;

public class KanalEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
    public int Sira { get; set; }
    public decimal AcilisDevri { get; set; }
    /// <summary>İyimser eşzamanlılık belirteci (contract-6): kayıt her değiştiğinde bir artar (KasaDbContext kaydetme kancası;
    /// ham SQL gelir upsert'ünde elle). Yazma isteği sürüm gönderirse uyuşmazlıkta 409 döner; göndermeyen eski istemci denetlenmez.</summary>
    public int Surum { get; set; }
}

public class CariEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
}

public class IslemEntity
{
    public int Id { get; set; }
    public DateOnly Tarih { get; set; }
    public string Cari { get; set; } = "";
    public decimal TutarTl { get; set; }
    public string Kanal { get; set; } = "";
    public int? KanalId { get; set; }
    [JsonIgnore]
    public KanalEntity? KanalKaydi { get; set; }
    public GiderTipi Tip { get; set; }
    public string? Not { get; set; }
    public int? KrediKartiId { get; set; }
    [JsonIgnore]
    public HesapHareketEntity? HesapHareketi { get; set; }
    /// <summary>İyimser eşzamanlılık belirteci (bkz. <see cref="KanalEntity.Surum"/>).</summary>
    public int Surum { get; set; }
}

public class GelenEntity
{
    public int Id { get; set; }
    public DateOnly DonemStart { get; set; }
    public string Kanal { get; set; } = "";
    public int? KanalId { get; set; }
    [JsonIgnore]
    public KanalEntity? KanalKaydi { get; set; }
    public decimal TutarTl { get; set; }
    /// <summary>Eski aynı dönem/kanal grubunun bütün satırları korunur ve tutarları değiştirilemez.</summary>
    public bool EskiYinelenenGrup { get; set; }
    /// <summary>İyimser eşzamanlılık belirteci (bkz. <see cref="KanalEntity.Surum"/>). Migration öncesi satırlar 0; PUT /api/gelenler
    /// yeni satırı 1 ile ekler: satır görmeden 0 gönderen istemci, arada eklenmiş satırın üzerine yazamaz.</summary>
    public int Surum { get; set; }
}

/// <summary>Tek satırlık uygulama ayarları.</summary>
public class AyarEntity
{
    public int Id { get; set; }
    public DateOnly TakipBaslangic { get; set; }
    public decimal KasaAcilisDevri { get; set; }
    public string? IzleyiciSifreHash { get; set; }
    /// <summary>İyimser eşzamanlılık belirteci (bkz. <see cref="KanalEntity.Surum"/>): takip başlangıcı ve açılış devri formu için;
    /// izleyici şifresi değişikliği ayrı formdur, sürümü artırmaz.</summary>
    public int Surum { get; set; }
}

public class KrediKartiEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public DateOnly KesimTarihi { get; set; }
    public DateOnly SonOdemeTarihi { get; set; }
    public decimal Limit { get; set; }
    public decimal Borc { get; set; }
}

public class KartOdemeEntity
{
    public int Id { get; set; }
    public int KrediKartiId { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal Tutar { get; set; }
    public string? Not { get; set; }
}

public class KrediEntity
{
    public bool GerceklesmeTakibi { get; set; }
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public decimal CekilenTutar { get; set; }
    public DateOnly CekimTarihi { get; set; }
    public int TaksitSayisi { get; set; }
    public decimal AylikOdeme { get; set; }
    public int OdemeGunu { get; set; }
    public string Kanal { get; set; } = "";
    public int? KanalId { get; set; }
    [JsonIgnore]
    public KanalEntity? KanalKaydi { get; set; }
}
