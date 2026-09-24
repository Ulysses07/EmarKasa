namespace Kasa.Api.Data;

/// <summary>POS'un sağlayıcısı (bilgi amaçlı).</summary>
public enum PosSaglayici
{
    BankaPosu,
    Iyzico,
    PayTr,
    Diger,
}

/// <summary>
/// POS tanımı: komisyon oranı ve blokaj günü, yeni satış girilirken varsayılan olarak kullanılır.
/// POS kayıtları yalnız bilgi amaçlıdır; kasa ve kârlılık hesabına girmez.
/// </summary>
[Gecmis(GecmisTurleri.Pos, nameof(Ad))]
public class PosTanimEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public PosSaglayici Saglayici { get; set; }
    /// <summary>POS'un bağlı olduğu satış kanalı (kanal silinirse boşalır: "Kanalsız").</summary>
    public int? KanalId { get; set; }
    /// <summary>Yüzde: 1,79 → %1,79. 0 ile 100 arası, en fazla 4 ondalık.</summary>
    public decimal KomisyonOrani { get; set; }
    /// <summary>Paranın hesaba geçmesi için beklenen takvim günü (0–365).</summary>
    public int BlokajGunu { get; set; }
    public bool Aktif { get; set; } = true;
}

/// <summary>
/// Bir günün POS satışı (brüt). Komisyon oranı, blokaj günü ve kanal kayıt anında POS tanımından
/// kopyalanır: POS'un oranı ya da kanalı sonradan değişse de geçmiş satışların neti ve geçmiş ayların
/// kanal dökümü değişmez.
/// </summary>
[Gecmis(GecmisTurleri.PosSatisi, nameof(Tarih), nameof(PosId), nameof(BrutTutar))]
public class PosSatisEntity
{
    public int Id { get; set; }
    public DateOnly Tarih { get; set; }
    public int PosId { get; set; }
    /// <summary>Satışın kanalı (kayıtta POS'un o anki kanalı; null = "Kanalsız", kanal silinirse de boşalır).</summary>
    public int? KanalId { get; set; }
    public decimal BrutTutar { get; set; }
    public decimal KomisyonOrani { get; set; }
    public int BlokajGunu { get; set; }
    public string? Not { get; set; }
}
