using Kasa.App.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Kasa.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Pencereli sürecin kilidi (PencereKilidi): süreç boyunca tutulur; pencere açmadan çalışan bildirim görevi bunu görünce
    /// çıkar (açık uygulama zaten bakar).</summary>
    private static PencereKilidi? Kilit { get; set; }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 07-15 hatırlatıcısının eski zamanlanmış görevi (EmarKasaHatirlatici) bir kez silinir: pencere açmadan, arka planda,
        // hataları yutarak; başarıdan sonra yerel işaretle bir daha denenmez (bkz. EskiHatirlatmaGorevi).
        var gorev = EskiHatirlatmaGorevi.Varsayilan();
        var argumanlar = Environment.GetCommandLineArgs();
        if (EskiHatirlatmaGorevi.KontrolModu(argumanlar))
        {
            // Eski zamanlanmış görev bu sürümde pencere veya bildirim açmaz: görevi siler ve çıkar.
            await Task.Run(() => gorev.TemizleAsync());
            Exit();
            return;
        }
        if (BildirimKontrolu.KontrolModu(argumanlar))
        {
            // Bildirim görevi (EmarKasaBildirim, tasarım 2026-09-30): pencere açmadan bakar, yeni bildirimleri gösterir ve hemen
            // çıkar (süre dolunca alttaki istek iptal edilmez; süreç kapanarak biter).
            await BildirimKontroluAsync();
            Exit();
            return;
        }
        Kilit = PencereKilidi.Al();
        // Bildirim tıklaması: işleyici kayıttan önce bağlanır (uygulama açıkken tıklama bu süreçte işlenir); uygulama bildirim
        // tıklamasıyla başladıysa tıklama bekletilir, kabuk oturum açılınca uygular. Kayıt süreç biterken kaldırılır.
        WindowsBildirimGosterici.Ortak.Baslat();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WindowsBildirimGosterici.Ortak.Bitir();
        _ = Task.Run(() => gorev.TemizleAsync());
        base.OnLaunched(args);
    }

    /// <summary>Pencere açmadan bildirim kontrolü (BildirimKontrolu, en çok 60 sn): uygulama penceresi açıksa hiçbir şey yapmaz.
    /// DI 2.0'daki --hatirlatma-kontrol yolu gibi CreateMauiApp ile kurulur; base.OnLaunched çağrılmaz, pencere açılmaz. Oturum
    /// belirteci SecureStorage'dan okunur (paketsiz uygulamada dosya; pencere gerekmez). Hatalar yutulur (async void OnLaunched'dan
    /// istisna çıkmaz); bildirim kaydı çıkmadan kaldırılır.</summary>
    private static async Task BildirimKontroluAsync()
    {
        try
        {
            if (PencereKilidi.AcikMi())
                return;
            var uygulama = MauiProgram.CreateMauiApp();
            await uygulama.Services.GetRequiredService<BildirimKontrolu>().CalistirAsync();
        }
        catch (Exception)
        {
            // Sessiz: uygulama bir sonraki açılışında zaten bakar.
        }
        finally
        {
            WindowsBildirimGosterici.Ortak.Bitir();
        }
    }
}
