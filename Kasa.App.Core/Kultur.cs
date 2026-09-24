using System.Globalization;

namespace Kasa.App.Core;

/// <summary>Uygulama kültürü: tarih/sayı biçimleri OS diline değil tr-TR'ye bağlı olsun.</summary>
public static class Kultur
{
    public static CultureInfo Turkce { get; } = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Geçerli ve sonradan açılacak tüm iş parçacıklarının kültürünü tr-TR yapar.</summary>
    public static void Uygula()
    {
        CultureInfo.DefaultThreadCurrentCulture = Turkce;
        CultureInfo.DefaultThreadCurrentUICulture = Turkce;
        CultureInfo.CurrentCulture = Turkce;
        CultureInfo.CurrentUICulture = Turkce;
    }
}
