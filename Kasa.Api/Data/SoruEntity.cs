using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>Sorunun neyle ilgili olduğu.</summary>
public enum SoruHedefTuru { Genel, Islem, Hafta, Cek }

public enum SoruDurumu { Acik, Kapali }

/// <summary>
/// Kayda soru: ortak (izleyici) bir işleme, haftaya ya da çeke soru yazar; editör cevaplar ve kapatır.
/// Cevap herkese görünür. Hiçbir rakama dokunmaz (ayrı tablo). <see cref="HedefOzet"/> soru anındaki
/// kaydın kısa özetidir: kayıt sonradan değişse ya da silinse de soru bağlamını korur.
/// </summary>
[Gecmis(GecmisTurleri.Soru, nameof(HedefOzet), nameof(Metin))]
[Index(nameof(Durum))]
public class SoruEntity
{
    public int Id { get; set; }
    public SoruHedefTuru HedefTur { get; set; }
    /// <summary>İşlem ya da çek Id'si (hafta ve genel soruda null).</summary>
    public int? HedefId { get; set; }
    /// <summary>Haftanın (dönemin) başlangıç günü (yalnız hafta sorusunda).</summary>
    public DateOnly? Hafta { get; set; }
    public string? HedefOzet { get; set; }
    public string Metin { get; set; } = "";
    public int? SoranId { get; set; }
    public string SoranAd { get; set; } = "";
    public string SoranRol { get; set; } = "";
    public DateTime SorulmaUtc { get; set; }
    public string? Cevap { get; set; }
    public string? CevaplayanAd { get; set; }
    public DateTime? CevaplanmaUtc { get; set; }
    public SoruDurumu Durum { get; set; }
    public DateTime? KapanmaUtc { get; set; }
}
