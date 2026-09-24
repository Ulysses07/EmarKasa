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
/// Alınan ya da verilen (kesilen) çek. Kasayı yalnız gerçekten tahsil edildiği/ödendiği gün
/// (<see cref="IslemTarihi"/>) etkiler; kural <see cref="CekKurali"/>'ndadır.
/// </summary>
public class CekEntity
{
    public int Id { get; set; }
    public CekYonu Yon { get; set; }
    public string? CekNo { get; set; }
    public string? Banka { get; set; }
    /// <summary>Alınan çekte keşideci/veren, verilen çekte lehtar (serbest metin, zorunlu).</summary>
    public string Kisi { get; set; } = "";
    public decimal Tutar { get; set; }
    public DateOnly DuzenlemeTarihi { get; set; }
    public DateOnly VadeTarihi { get; set; }
    /// <summary>Kanal adı; <see cref="Kanallar.Ortak"/> yalnız verilen çekte.</summary>
    public string Kanal { get; set; } = "";
    public CekDurumu Durum { get; set; }
    /// <summary>Gerçek tahsil / ödeme / ciro günü (TahsilEdildi, Odendi, CiroEdildi'de zorunlu).</summary>
    public DateOnly? IslemTarihi { get; set; }
    public string? Not { get; set; }
}

/// <summary>Çıkışta iptal edilen token'ın kimliği (jti); süresi dolunca temizlenir.</summary>
public class IptalEdilenTokenEntity
{
    public string Jti { get; set; } = "";
    public DateTime BitisUtc { get; set; }
}
