namespace Kasa.App.Core;

/// <summary>Masaüstü uygulamasının bu bilgisayara özel dosyalarının klasörü: %LOCALAPPDATA%\EmarKasa (07-15 hatırlatıcısının da
/// klasörü; bkz. EskiHatirlatmaGorevi.Varsayilan). Uygulama ve pencere açmadan çalışan bildirim görevi aynı dosyaları kullanır.</summary>
public static class YerelKlasor
{
    public static string Yol => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmarKasa");
}
