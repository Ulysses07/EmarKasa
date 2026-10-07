using System.Text.Json;
using System.Text.RegularExpressions;
using Kasa.App.Core;
using Velopack;

namespace Kasa.App.WinUI;

public static class Program
{
    private const string GuncellemeTesti = "--guncelleme-duman-testi";

    [STAThread]
    public static int Main(string[] args)
    {
        // Kurulum/güncelleme kancaları UI ve finans bağlantısı başlatılmadan önce tamamlanır.
        // İndirilen paket yalnız kullanıcının açık kurulum onayıyla uygulanır.
        VelopackApp.Build()
            .SetAppUserModelId(BildirimKimligi.Aumid)
            .SetAutoApplyOnStartup(false)
            .Run();

        if (args.Length > 0 && args[0] == GuncellemeTesti)
            return GuncellemeDumanTestiAsync(args).GetAwaiter().GetResult();

        // WinUI'nin ürettiği Main ile aynı COM ve UI synchronization context kurulumu.
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

    private static async Task<int> GuncellemeDumanTestiAsync(string[] args)
    {
        try
        {
            // Gerçek exe'nin kurulum → indirme → yeniden başlatma testi yalnız geçici GitHub runner'ında çalışır.
            // Bu mod MAUI/oturum/veritabanı başlatmaz; yerel kullanımda dosya veya paket değiştiremez.
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
                || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted"
                || args.Length != 4)
                return 64;

            var geciciKok = Environment.GetEnvironmentVariable("RUNNER_TEMP");
            if (string.IsNullOrWhiteSpace(geciciKok)
                || !AltYolMu(geciciKok, args[1])
                || !AltYolMu(geciciKok, args[2])
                || !AltYolMu(geciciKok, Environment.ProcessPath)
                || !Directory.Exists(args[1])
                || !Regex.IsMatch(args[3], @"^[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant))
                return 64;

            var yonetici = new UpdateManager(Path.GetFullPath(args[1]));
            if (!yonetici.IsInstalled || yonetici.IsPortable || yonetici.AppId != "EmarKasa")
                return 65;

            var kuruluSurum = yonetici.CurrentVersion?.ToString();
            if (kuruluSurum == args[3])
            {
                // Yeni süreç bunu yalnız gerçek kurulu paket manifestindeki sürüm eşleşince yazabilir.
                using var rapor = new FileStream(Path.GetFullPath(args[2]), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await JsonSerializer.SerializeAsync(rapor, new
                {
                    currentVersion = kuruluSurum,
                    installed = true,
                    updated = true,
                });
                return 0;
            }

            using var sureSiniri = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var guncelleme = await yonetici.CheckForUpdatesAsync().WaitAsync(sureSiniri.Token);
            if (guncelleme?.TargetFullRelease.Version.ToString() != args[3])
                return 66;

            await yonetici.DownloadUpdatesAsync(guncelleme, cancelToken: sureSiniri.Token);
            yonetici.ApplyUpdatesAndRestart(guncelleme.TargetFullRelease, args);
            return 67; // ApplyUpdatesAndRestart başarılı olduğunda eski süreç burada devam etmez.
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Windows güncelleme duman testi başarısız: {ex.GetType().Name}");
            return 70;
        }
    }

    private static bool AltYolMu(string kok, string? aday)
    {
        if (string.IsNullOrWhiteSpace(aday))
            return false;
        var tamKok = Path.TrimEndingDirectorySeparator(Path.GetFullPath(kok)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(aday).StartsWith(tamKok, StringComparison.OrdinalIgnoreCase);
    }
}
