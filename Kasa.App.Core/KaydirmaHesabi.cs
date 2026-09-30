namespace Kasa.App.Core;

/// <summary>
/// Kartlar ekranında açılan ayrıntıyı ya da formu görünür yapma kararı (KartTakipPage): hedefin üstü görünür alanın içindeyse
/// sayfa kaydırılmaz, değilse hedefin üstü görünür alanın başına getirilir (ScrollToPosition.Start). MAUI'nin MakeVisible'ı
/// görünür alana sığmayan öğeyi alttan hizalar (uzun ayrıntı ya da formun başı ekranın üstünde kalır); bu yüzden kullanılmaz.
/// </summary>
public static class KaydirmaHesabi
{
    /// <summary>Üstün görünür alanın başından yukarıda kalmasına izin verilen yuvarlama payı (piksel).</summary>
    public const double UstPay = 1;
    /// <summary>Üst kenar görünür alanın altına bundan yakınsa (yalnız kenarı görünür, başlığı okunmaz) görünmüyor sayılır.</summary>
    public const double AltPay = 48;

    /// <param name="hedefUst">Hedefin kaydırılan içeriğe göre üst kenarı.</param>
    /// <param name="kaydirmaY">Kaydırıcının güncel kaydırma konumu (ScrollY).</param>
    /// <param name="gorunurYukseklik">Kaydırıcının görünür yüksekliği.</param>
    /// <returns>Hedefin üstü görünür alanın dışındaysa true; değerler ölçülmemiş ya da geçersizse false (kaydırılmaz).</returns>
    public static bool BasaKaydirilmali(double hedefUst, double kaydirmaY, double gorunurYukseklik)
    {
        if (!double.IsFinite(hedefUst) || !double.IsFinite(kaydirmaY) || !double.IsFinite(gorunurYukseklik) || gorunurYukseklik <= 0)
            return false;
        return hedefUst < kaydirmaY - UstPay || hedefUst > kaydirmaY + gorunurYukseklik - AltPay;
    }
}
