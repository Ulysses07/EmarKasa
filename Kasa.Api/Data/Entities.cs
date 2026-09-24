using Kasa.Core;

namespace Kasa.Api.Data;

[Gecmis(GecmisTurleri.Kanal, nameof(KanalEntity.Ad))]
public class KanalEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
    public int Sira { get; set; }
    public decimal AcilisDevri { get; set; }
}

[Gecmis(GecmisTurleri.Cari, nameof(CariEntity.Ad))]
public class CariEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
}

/// <summary>Sabit gider kalemi (Kira, SGK, Maaş…). Sabit gider işleminin "Cari" alanı bu adı taşır.</summary>
[Gecmis(GecmisTurleri.GiderKalemi, nameof(GiderKalemiEntity.Ad))]
public class GiderKalemiEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
}

[Gecmis(GecmisTurleri.Islem, nameof(IslemEntity.Tarih), nameof(IslemEntity.Cari), nameof(IslemEntity.TutarTl), nameof(IslemEntity.Kanal))]
public class IslemEntity
{
    public int Id { get; set; }
    public DateOnly Tarih { get; set; }
    public string Cari { get; set; } = "";
    public decimal TutarTl { get; set; }
    public string Kanal { get; set; } = "";
    public GiderTipi Tip { get; set; }
    public string? Not { get; set; }
    public int? KrediKartiId { get; set; }
}

[Gecmis(GecmisTurleri.Gelen, nameof(GelenEntity.DonemStart), nameof(GelenEntity.Kanal), nameof(GelenEntity.TutarTl))]
public class GelenEntity
{
    public int Id { get; set; }
    public DateOnly DonemStart { get; set; }
    public string Kanal { get; set; } = "";
    public decimal TutarTl { get; set; }
}

/// <summary>Tek satırlık uygulama ayarları.</summary>
[Gecmis(GecmisTurleri.Ayar)]
public class AyarEntity
{
    public int Id { get; set; }
    public DateOnly TakipBaslangic { get; set; }
    public decimal KasaAcilisDevri { get; set; }
    [Gizli("İzleyici şifresi değiştirildi")]
    public string? IzleyiciSifreHash { get; set; }
    /// <summary>Artınca eski izleyici token'ları geçersiz olur (şifre değişimi, oturum kapatma).</summary>
    [Gizli]
    public int IzleyiciOturumSurumu { get; set; }
    /// <summary>Artınca eski editör token'ları geçersiz olur.</summary>
    /// <remarks>Yalnız "tüm oturumları kapat" artırır (izleyici sürümüyle birlikte).</remarks>
    [Gizli("Tüm oturumlar kapatıldı")]
    public int EditorOturumSurumu { get; set; }
}

[Gecmis(GecmisTurleri.KrediKarti, nameof(KrediKartiEntity.Ad))]
public class KrediKartiEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public DateOnly KesimTarihi { get; set; }
    public DateOnly SonOdemeTarihi { get; set; }
    public decimal Limit { get; set; }
    public decimal Borc { get; set; }
}

[Gecmis(GecmisTurleri.KartOdemesi, nameof(KartOdemeEntity.Tarih), nameof(KartOdemeEntity.KrediKartiId), nameof(KartOdemeEntity.Tutar))]
public class KartOdemeEntity
{
    public int Id { get; set; }
    public int KrediKartiId { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal Tutar { get; set; }
    public string? Not { get; set; }
}

/// <summary>Çıkışta iptal edilen token'ın kimliği (jti); süresi dolunca temizlenir.</summary>
[GecmisDisi]
public class IptalEdilenTokenEntity
{
    public string Jti { get; set; } = "";
    public DateTime BitisUtc { get; set; }
}

/// <summary>
/// Değişiklik geçmişi satırı: kim (rol), ne zaman, hangi kayıt, ne oldu. KasaDbContext.SaveChanges
/// değişiklik izleyiciden otomatik yazar; toplu güncellemeler (ExecuteUpdate) için özet satırı
/// uçta açıkça eklenir. Gizli alanlar (şifre hash'i, oturum sürümleri) hiçbir sütuna yazılmaz.
/// </summary>
[GecmisDisi]
public class DegisiklikEntity
{
    public int Id { get; set; }
    public DateTime ZamanUtc { get; set; }
    /// <summary>JWT'deki rol (editor / viewer).</summary>
    public string Rol { get; set; } = "";
    /// <summary>Kayıt türü (İşlem, Gelen, Cari…; bkz. <see cref="GecmisTurleri"/>).</summary>
    public string Tur { get; set; } = "";
    public int? KayitId { get; set; }
    /// <summary>Eklendi / Güncellendi / Silindi / Eklendi (geri alındı) (bkz. <see cref="Eylemler"/>).</summary>
    public string Eylem { get; set; } = "";
    /// <summary>Kısa Türkçe özet, ör. "İşlem silindi: 12.03.2026 · Market · 1.250,00 ₺ · MEZAT".</summary>
    public string Ozet { get; set; } = "";
    public string? EskiJson { get; set; }
    public string? YeniJson { get; set; }
    /// <summary>Silinen kayıt bu satırdan geri getirildi (ikinci kez geri alınamaz).</summary>
    public bool GeriAlindi { get; set; }
    public DateTime? GeriAlmaZamaniUtc { get; set; }
}
