namespace Kasa.App.Core;

// Paket A — "Bugün yapılacaklar" → "Ödeme gir" derin bağlantısı (DerinBaglanti.Kart).
public partial class KrediKartlariViewModel
{
    private int? _istenenKartId;

    /// <summary>
    /// Sorgudaki kart (<c>?id=</c>) bir sonraki yüklemede (sayfa açılışı) vurgulanır ve ödeme girişi
    /// hazırlanır; kaydetmez. Sorguda geçerli id yoksa hiçbir şey yapmaz.
    /// </summary>
    /// <returns>İstek kaydedildiyse true.</returns>
    public bool KartIstegiUygula(IDictionary<string, object>? sorgu)
    {
        if (DerinBaglanti.Id(sorgu) is not { } id) return false;
        _istenenKartId = id;
        return true;
    }

    /// <summary>
    /// Yükleme sonunda bekleyen istek bir kez uygulanır: kart vurgulanır (diğerlerinin vurgusu kalkar) ve
    /// ödeme tutarı boşsa ekstre borcuyla doldurulur (tarih zaten bugün). Yazılmış tutar ezilmez. Kart yoksa
    /// (silinmiş) istek sessizce düşer.
    /// </summary>
    private void KartIsteginiUygula()
    {
        if (_istenenKartId is not { } id) return;
        _istenenKartId = null;
        foreach (var k in Kartlar) k.Vurgulu = k.Id == id;
        if (Kartlar.FirstOrDefault(k => k.Id == id) is { } kart && kart.OdemeTutarGiris == 0m && kart.EkstreBorc > 0m)
            kart.OdemeTutarGiris = kart.EkstreBorc;
    }
}
