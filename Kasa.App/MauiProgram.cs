using Kasa.ApiClient;
using Kasa.App.Core;
using Kasa.App.Services;
using Microsoft.Extensions.Logging;

namespace Kasa.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                // Tasarım PlexSans* alias'ları — gerçek IBM Plex ttf yoksa OpenSans'a maplenir.
                fonts.AddFont("OpenSans-Regular.ttf", "PlexSans");
                fonts.AddFont("OpenSans-Regular.ttf", "PlexSansMedium");
                fonts.AddFont("OpenSans-Semibold.ttf", "PlexSansSemiBold");
                fonts.AddFont("OpenSans-Semibold.ttf", "PlexSansBold");
            });

        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
#if WINDOWS
        builder.Services.AddSingleton<IBildirimServisi, Platforms.Windows.WindowsBildirimServisi>();
#endif
        builder.Services.AddSingleton(sp =>
        {
            var http = new HttpClient { BaseAddress = new Uri("https://kasa.royalmezat.com/") };
            return new KasaApiClient(http, sp.GetRequiredService<ITokenStore>());
        });
        builder.Services.AddSingleton<IKasaApi>(sp => sp.GetRequiredService<KasaApiClient>());

        builder.Services.AddSingleton<AuthViewModel>();
        builder.Services.AddTransient<PanelViewModel>();
        builder.Services.AddTransient<HaftalikViewModel>();
        builder.Services.AddTransient<AylikViewModel>();
        builder.Services.AddTransient<CarilerViewModel>();
        builder.Services.AddTransient<IslemlerViewModel>();
        builder.Services.AddTransient<KrediKartlariViewModel>();
        builder.Services.AddTransient<AyarlarViewModel>();

        builder.Services.AddSingleton<App>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<Views.LoginPage>();
        builder.Services.AddTransient<Views.PanelPage>();
        builder.Services.AddTransient<Views.HaftalikPage>();
        builder.Services.AddTransient<Views.AylikPage>();
        builder.Services.AddTransient<Views.CarilerPage>();
        builder.Services.AddTransient<Views.IslemlerPage>();
        builder.Services.AddTransient<Views.KrediKartlariPage>();
        builder.Services.AddTransient<Views.AyarlarPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
