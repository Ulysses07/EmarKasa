using Kasa.Core;

namespace Kasa.Api.Data;

public class KanalEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
    public int Sira { get; set; }
    public decimal AcilisDevri { get; set; }
}

public class CariEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
}

/// <summary>Sabit gider kalemi (Kira, SGK, Maaş…). Sabit gider işleminin "Cari" alanı bu adı taşır.</summary>
public class GiderKalemiEntity
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
    public GiderTipi Tip { get; set; }
    public string? Not { get; set; }
    public int? KrediKartiId { get; set; }
}

public class GelenEntity
{
    public int Id { get; set; }
    public DateOnly DonemStart { get; set; }
    public string Kanal { get; set; } = "";
    public decimal TutarTl { get; set; }
}

/// <summary>Tek satırlık uygulama ayarları.</summary>
public class AyarEntity
{
    public int Id { get; set; }
    public DateOnly TakipBaslangic { get; set; }
    public decimal KasaAcilisDevri { get; set; }
    public string? IzleyiciSifreHash { get; set; }
    /// <summary>Artınca eski izleyici token'ları geçersiz olur (şifre değişimi, oturum kapatma).</summary>
    public int IzleyiciOturumSurumu { get; set; }
    /// <summary>Artınca eski editör token'ları geçersiz olur.</summary>
    public int EditorOturumSurumu { get; set; }
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

/// <summary>
/// Her ay tekrarlayan sabit gider (kira, SGK, maaş …). Kendiliğinden işlem girmez; ayın
/// günü gelince "bekleyen" olarak listelenir, editör onaylayınca sabit gider işlemi olur.
/// </summary>
public class TekrarlayanGiderEntity
{
    public int Id { get; set; }
    /// <summary>Gider kalemi adı (GiderKalemleri'ndeki kayıtlı yazım; kalem adı değişince güncellenir).</summary>
    public string Kalem { get; set; } = "";
    /// <summary>Kanal adı ya da "Ortak".</summary>
    public string Kanal { get; set; } = "";
    /// <summary>Varsayılan tutar (onaylarken değiştirilebilir).</summary>
    public decimal Tutar { get; set; }
    /// <summary>Ayın günü (1–31); kısa aylarda ayın son günü.</summary>
    public int AyinGunu { get; set; }
    public bool Aktif { get; set; } = true;
    /// <summary>İlk geçerli ay (ayın 1'i).</summary>
    public DateOnly BaslangicAyi { get; set; }
}

public enum TekrarlayanDurum { Girildi, Atlandi }

/// <summary>Tekrarlayan giderin bir ayı için verilen karar: girildi (işlem oluştu) ya da atlandı.</summary>
public class TekrarlayanGirisEntity
{
    public int Id { get; set; }
    public int TekrarlayanGiderId { get; set; }
    /// <summary>Ay (ayın 1'i).</summary>
    public DateOnly Ay { get; set; }
    public TekrarlayanDurum Durum { get; set; }
    /// <summary>Girildi ise oluşan işlem (işlem silinirse NULL olur).</summary>
    public int? IslemId { get; set; }
    public DateTime Zaman { get; set; }
}

/// <summary>Çıkışta iptal edilen token'ın kimliği (jti); süresi dolunca temizlenir.</summary>
public class IptalEdilenTokenEntity
{
    public string Jti { get; set; } = "";
    public DateTime BitisUtc { get; set; }
}
