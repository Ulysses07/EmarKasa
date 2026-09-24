namespace Kasa.Api.Data;

/// <summary>İşlemin belge türü (muhasebeci listesinde ve fatura takibinde gruplanır).</summary>
public enum BelgeTuru
{
    EFatura,
    EArsiv,
    Fis,
    Makbuz,
    Belgesiz,
}

/// <summary>
/// İşleme eklenen fiş/fatura fotoğrafı ya da PDF'in bilgisi. Dosyanın kendisi diskte,
/// <c>Kasa:BelgeKlasoru</c> (varsayılan: veritabanının yanındaki <c>belgeler/</c>) altında
/// <see cref="DepoAdi"/> adıyla durur; kullanıcının verdiği ad yalnız gösterim içindir.
/// </summary>
/// <remarks>
/// <see cref="IslemId"/> bilerek FK DEĞİLDİR: işlem silinince ek 30 gün (geri alma süresi) boyunca
/// kalır ve işlem geçmişten geri alınırsa yeni işleme bağlanır. Silme anında ek işlemden AYRILIR
/// (<see cref="IslemId"/> = 0, eski Id <see cref="SilinenIslemId"/>'de): Id bir gün yeniden
/// kullanılsa bile (ör. tablo yeniden kurulurken sayaç sıfırlanırsa) yeni işlem eski işlemin eklerini
/// devralmaz. Süresi dolan yetim ekleri gece temizliği (<c>BelgeTemizleyici</c>) siler.
/// </remarks>
[Gecmis(GecmisTurleri.IslemEki, nameof(OrijinalAd), nameof(IslemId))]
public class IslemEkiEntity
{
    /// <summary>İşlemi silinmiş (geri alınmayı bekleyen) ekin <see cref="IslemId"/> değeri.</summary>
    public const int IslemsizId = 0;

    public int Id { get; set; }
    /// <summary>Bağlı işlem; işlem silindiyse <see cref="IslemsizId"/> (0).</summary>
    public int IslemId { get; set; }
    /// <summary>İşlem silindiyse eski Id'si (geri alınınca yeni işleme bu alanla bağlanır); değilse null.</summary>
    public int? SilinenIslemId { get; set; }
    /// <summary>İşlemin silindiği an (UTC); gece temizliği saklama süresini buradan sayar.</summary>
    public DateTime? SilinmeZamaniUtc { get; set; }
    /// <summary>Temizlenmiş özgün dosya adı (yalnız gösterim/indirme adı).</summary>
    public string OrijinalAd { get; set; } = "";
    /// <summary>Diskteki ad: 32 hex rastgele + uzantı (ör. <c>3f…a1.pdf</c>). Kullanıcı girdisi içermez.</summary>
    public string DepoAdi { get; set; } = "";
    public string IcerikTipi { get; set; } = "";
    public long Boyut { get; set; }
    public DateTime YuklemeZamaniUtc { get; set; }
}
