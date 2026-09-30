namespace Kasa.App.Core;

/// <summary>
/// Kartlar ekranında açılan ayrıntıyı, formu ve sayfa hata satırını görünür yapma kararı (KartTakipPage). Ayrıntı
/// (<see cref="BasaKaydirilmali"/>): üstü görünür alanın içindeyse kaydırılmaz (kutular görünür kalır), değilse üstü görünür
/// alanın başına gelir. Form ve hata satırı (<see cref="FormKaydirmasi"/>): tamamı görünüyorsa kaydırılmaz; görünür alana
/// sığıyorsa tamamı görünecek kadar, sığmıyorsa başı görünür alanın başına gelecek biçimde kaydırılır (yalnız başlığı görünen
/// form kalmaz). MAUI'nin MakeVisible'ı görünür alana sığmayan öğeyi alttan hizalar (başı ekranın üstünde kalır); kullanılmaz.
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

    /// <param name="ust">Hedefin kaydırılan içeriğe göre üst kenarı.</param>
    /// <param name="yukseklik">Hedefin yüksekliği.</param>
    /// <param name="kaydirmaY">Kaydırıcının güncel kaydırma konumu (ScrollY).</param>
    /// <param name="gorunurYukseklik">Kaydırıcının görünür yüksekliği.</param>
    /// <returns>Yeni kaydırma konumu; hedef zaten görünüyorsa (sığmayan hedefte başı görünür alanın başındaysa) ya da değerler
    /// ölçülmemiş/geçersizse null (kaydırılmaz).</returns>
    public static double? FormKaydirmasi(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik)
    {
        if (!double.IsFinite(ust) || !double.IsFinite(yukseklik) || !double.IsFinite(kaydirmaY) || !double.IsFinite(gorunurYukseklik)
            || gorunurYukseklik <= 0 || yukseklik < 0)
            return null;
        if (yukseklik > gorunurYukseklik)
            return Math.Abs(ust - kaydirmaY) <= UstPay ? null : ust;
        var alt = ust + yukseklik;
        if (ust >= kaydirmaY - UstPay && alt <= kaydirmaY + gorunurYukseklik + UstPay)
            return null;
        return ust < kaydirmaY ? ust : alt - gorunurYukseklik;
    }
}
