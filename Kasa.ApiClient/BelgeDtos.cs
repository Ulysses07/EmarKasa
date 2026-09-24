using System.Text.Json.Serialization;

namespace Kasa.ApiClient;

/// <summary>İşlemin belge türü.</summary>
public enum BelgeTuru { EFatura, EArsiv, Fis, Makbuz, Belgesiz }

/// <summary>İşlemin belge bilgisi (üçü de isteğe bağlı; boş = bugünkü gibi).</summary>
public record BelgeBilgisi(BelgeTuru? Tur, string? No, bool FaturaBekleniyor)
{
    public static readonly BelgeBilgisi Bos = new(null, null, false);
}

public partial record IslemDto
{
    public BelgeTuru? BelgeTuru { get; init; }
    public string? BelgeNo { get; init; }
    public bool FaturaBekleniyor { get; init; }

    /// <summary>İşlemin ek (fiş/fatura dosyası) sayısı; GET /islemler doldurur (sıfırsa sunucu alanı yazmaz).</summary>
    public int EkSayisi { get; init; }

    [JsonIgnore]
    public BelgeBilgisi Belge => new(BelgeTuru, BelgeNo, FaturaBekleniyor);

    /// <summary>Görünüm: listede belge özeti gösterilsin mi (tür, no veya fatura bekleniyor dolu).</summary>
    [JsonIgnore]
    public bool BelgeVar => BelgeTuru is not null || !string.IsNullOrWhiteSpace(BelgeNo) || FaturaBekleniyor;

    /// <summary>Görünüm: listede "Ekler (n)" düğmesi (her iki rol).</summary>
    [JsonIgnore]
    public bool EkVar => EkSayisi > 0;

    [JsonIgnore]
    public string EkEtiketi => $"Ekler ({EkSayisi})";
}

public partial record IslemYaz
{
    /// <summary>
    /// Gönderilecek belge bilgisi. null (varsayılan) ise belge alanları gövdeye HİÇ yazılmaz ve
    /// sunucudaki değer korunur: belge alanını bilmeyen bir çağıran (ör. başka bir ekran) işlemi
    /// düzeltirken belgeyi silmez. Doluysa üç alan da (null'lar dahil) gönderilir.
    /// </summary>
    [JsonIgnore]
    public BelgeBilgisi? Belge { get; init; }

    /// <summary>Belge alanlarının JSON'a yazılışı (yalnız <see cref="Belge"/> doluysa).</summary>
    [JsonExtensionData]
    public Dictionary<string, object?>? BelgeAlanlari => Belge is not { } b ? null : new()
    {
        ["belgeTuru"] = b.Tur?.ToString(),
        ["belgeNo"] = b.No,
        ["faturaBekleniyor"] = b.FaturaBekleniyor,
    };
}

/// <summary>İşlem ekinin bilgisi.</summary>
public record EkDto(int Id, int IslemId, string Ad, string IcerikTipi, long Boyut, DateTime YuklemeZamaniUtc)
{
    /// <summary>Görünüm: PDF mi (değilse fotoğraf).</summary>
    [JsonIgnore]
    public bool PdfMi => IcerikTipi == "application/pdf";
}

public record FaturaIslemDto(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip,
    int? KrediKartiId, BelgeTuru? BelgeTuru, string? BelgeNo, bool FaturaBekleniyor, int EkSayisi, string? Not);
public record FaturaBekleyenCariDto(string Cari, decimal Toplam, int Adet, DateOnly EnEskiTarih, IReadOnlyList<FaturaIslemDto> Islemler);
/// <summary>Ayın belge türü toplamı; <see cref="Tur"/> null = belirtilmemiş.</summary>
public record BelgeTuruToplamDto(BelgeTuru? Tur, string Ad, decimal Toplam, int Adet);
public record FaturaTakibiDto(int Yil, int Ay,
    IReadOnlyList<FaturaBekleyenCariDto> Bekleyenler, decimal BekleyenToplam, int BekleyenAdet,
    IReadOnlyList<BelgeTuruToplamDto> AyOzeti, decimal AyToplam, int AyAdet,
    decimal AyBelgesizToplam, int AyBelgesizAdet);
