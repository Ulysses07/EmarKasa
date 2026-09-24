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

/// <summary>
/// Alınan ya da verilen (kesilen) çek. Kasayı yalnız gerçekten tahsil edildiği/ödendiği gün
/// (<see cref="IslemTarihi"/>) etkiler; kural <see cref="CekKurali"/>'ndadır.
/// </summary>
[Gecmis(GecmisTurleri.Cek, nameof(CekEntity.Yon), nameof(CekEntity.Kisi), nameof(CekEntity.Tutar), nameof(CekEntity.VadeTarihi))]
public partial class CekEntity
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

/// <summary>
/// Kasa sayımı: editörün saydığı nakit ve kayıt anında defterdeki kasa (Tarih gününün sonunda).
/// HesaplananTutar bir anlık görüntüdür; geçmiş kayıtlar sonradan düzeltilse de değişmez.
/// </summary>
[Gecmis(GecmisTurleri.KasaSayimi, nameof(KasaSayimEntity.Tarih), nameof(KasaSayimEntity.SayilanTutar))]
public partial class KasaSayimEntity
{
    public int Id { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal SayilanTutar { get; set; }
    public decimal HesaplananTutar { get; set; }
    public string? Not { get; set; }
    public DateTime KayitZamaniUtc { get; set; }
}

/// <summary>
/// Her ay tekrarlayan sabit gider (kira, SGK, maaş …). Kendiliğinden işlem girmez; ayın
/// günü gelince "bekleyen" olarak listelenir, editör onaylayınca sabit gider işlemi olur.
/// </summary>
[Gecmis(GecmisTurleri.TekrarlayanGider, nameof(TekrarlayanGiderEntity.Kalem), nameof(TekrarlayanGiderEntity.Kanal), nameof(TekrarlayanGiderEntity.Tutar))]
public partial class TekrarlayanGiderEntity
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
[Gecmis(GecmisTurleri.TekrarlayanKarar, nameof(TekrarlayanGirisEntity.TekrarlayanGiderId), nameof(TekrarlayanGirisEntity.Ay), nameof(TekrarlayanGirisEntity.Durum))]
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
public partial class DegisiklikEntity
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
