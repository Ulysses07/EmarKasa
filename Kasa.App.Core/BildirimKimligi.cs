namespace Kasa.App.Core;

/// <summary>
/// Masaüstü bildirimlerinin Windows kimliği (tasarım 2026-09-30 masaüstü bildirimleri, "Uygulamada verilen kararlar"): süreç sabit,
/// okunabilir bir AppUserModelID ile çalışır; Windows App SDK paketsiz kayıtta (AppNotificationManager.Register) süreç kimliği
/// açıkça verilmişse onu kullanır, verilmemişse exe yolundan her klasör için ayrı bir {GUID} üretir (WindowsAppSDK
/// dev/AppNotifications/AppNotificationUtility.cpp, RetrieveUnpackagedNotificationAppId). Uygulama ZIP'ten her sürümde başka
/// klasöre açılabildiği için sabit kimlik bildirim ayarını ve Başlat menüsü kısayolunu sürümler arasında korur.
/// <para>Sabit kimlikte Windows App SDK etkinleştirici CLSID'sini kimliğin kaydından yeniden kullanır ve COM sunucusu kaydındaki
/// exe yolunu güncellemez; uygulama her pencereli açılışta bu kaydı ve kısayolu kendi exe'sine göre düzeltir. Bu sınıf yalnız
/// karşılaştırmayı yapar (Windows'tan bağımsız, sınanır); kayıt defteri ve kısayol yazımı Kasa.App/Platforms/Windows'tadır.</para>
/// </summary>
public static class BildirimKimligi
{
    /// <summary>Sürecin AppUserModelID'si (SetCurrentProcessExplicitAppUserModelID) ve kısayoldaki System.AppUserModel.ID.</summary>
    public const string Aumid = "EmarKasa.Masaustu";

    /// <summary>Başlat menüsü kısayolunun dosya adı (%APPDATA%\Microsoft\Windows\Start Menu\Programs altında).</summary>
    public const string KisayolAdi = "Emar Kasa.lnk";

    /// <summary>Windows App SDK'nın COM sunucusu komut satırına eklediği etkinleştirme argümanı (AppNotificationUtility.h,
    /// c_notificationActivatedArgument); uygulama kapalıyken tıklamada exe bu argümanla başlar.</summary>
    public const string EtkinlestirmeArgumani = "----AppNotificationActivated:";

    /// <summary>HKCU\Software\Classes\CLSID\{etkinleştirici}\LocalServer32 varsayılan değeri: Windows App SDK'nın yazdığı biçim
    /// (tırnaklı exe yolu, boşluk, etkinleştirme argümanı).</summary>
    public static string ComSunucusuKomutu(string exe) => $"\"{exe}\" {EtkinlestirmeArgumani}";

    /// <summary>Kayıtlı COM sunucusu komutu bu exe'yi gösteriyor mu. Windows App SDK yolu küçük harfle yazar; Windows yolları
    /// büyük/küçük harf duyarsızdır.</summary>
    public static bool ComSunucusuGuncelMi(string? kayitli, string exe) =>
        kayitli is not null && string.Equals(kayitli.Trim(), ComSunucusuKomutu(exe), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Başlat menüsü kısayolunun uygulamanın denetlediği alanları: hedef exe, çalışma klasörü, simge, AppUserModelID ve bildirim
/// etkinleştiricisi (System.AppUserModel.ToastActivatorCLSID; bilinmiyorsa null).</summary>
public sealed record KisayolBilgisi(string Hedef, string CalismaDizini, string SimgeDosyasi, int SimgeSirasi, string? Aumid, Guid? EtkinlestiriciClsid)
{
    /// <summary>Kısayol yeniden yazılmadan kalabilir mi: yollar büyük/küçük harf duyarsız, kimlik ve CLSID birebir karşılaştırılır.</summary>
    public bool AyniMi(KisayolBilgisi istenen) =>
        string.Equals(Hedef, istenen.Hedef, StringComparison.OrdinalIgnoreCase)
        && string.Equals(CalismaDizini, istenen.CalismaDizini, StringComparison.OrdinalIgnoreCase)
        && string.Equals(SimgeDosyasi, istenen.SimgeDosyasi, StringComparison.OrdinalIgnoreCase)
        && SimgeSirasi == istenen.SimgeSirasi
        && string.Equals(Aumid, istenen.Aumid, StringComparison.Ordinal)
        && EtkinlestiriciClsid == istenen.EtkinlestiriciClsid;
}
