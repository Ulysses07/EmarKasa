namespace Kasa.Api.Data;

/// <summary>
/// Kart ekstresi mutabakatı: bir kartın bir ekstre dönemi için bankanın ekstre tutarı ile
/// uygulamanın hesapladığı dönem sonu borcunun karşılaştırması. Yalnız kayıt tutar; kart borcunu
/// ve kasayı hiçbir şekilde değiştirmez. Kart + kesim başına tek satır.
/// </summary>
[Gecmis(GecmisTurleri.KartMutabakati, nameof(KrediKartiId), nameof(DonemBitis), nameof(EkstreTutari))]
public class KartMutabakatEntity
{
    public int Id { get; set; }
    public int KrediKartiId { get; set; }
    /// <summary>Önceki kesimin ertesi günü.</summary>
    public DateOnly DonemBaslangic { get; set; }
    /// <summary>Kesim günü (dönemin son günü).</summary>
    public DateOnly DonemBitis { get; set; }
    /// <summary>Bankanın ekstresindeki dönem borcu.</summary>
    public decimal EkstreTutari { get; set; }
    /// <summary>Kayıt anında uygulamanın hesapladığı dönem sonu borcu (anlık görüntü).</summary>
    public decimal HesaplananBorc { get; set; }
    /// <summary>Ekstrede görülüp işaretlenen işlem Id'leri, virgülle ayrılmış.</summary>
    public string? TikliIslemIdleri { get; set; }
    public string? Not { get; set; }
    public KartMutabakatDurumu Durum { get; set; }
    public DateTime KayitZamaniUtc { get; set; }
}

/// <summary>Mutabakatın durumu (sunucu farka göre belirler).</summary>
public enum KartMutabakatDurumu
{
    Acik,       // fark var, kabul edilmedi
    Mutabik,    // fark yok
    FarkKabul   // fark var, olduğu gibi kabul edildi
}
