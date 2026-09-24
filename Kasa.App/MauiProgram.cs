using Kasa.ApiClient;
using Kasa.App.Core;
using Kasa.App.Services;
using Microsoft.Extensions.Logging;

namespace Kasa.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Tarih/sayı biçimleri (XAML StringFormat dahil) Windows dilinden bağımsız Türkçe olsun.
        Kultur.Uygula();

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
        builder.Services.AddSingleton<Yonlendirme>();
        builder.Services.AddSingleton(TimeProvider.System);
#if WINDOWS
        builder.Services.AddSingleton<IBildirimServisi>(sp =>
            new Platforms.Windows.WindowsBildirimServisi(sp.GetRequiredService<Yonlendirme>()));
        // "Excel'e aktar": Belgeler\Emar Kasa'ya kaydeder ve açar (yoksa VM'ler anlaşılır hata gösterir).
        builder.Services.AddSingleton<IDosyaKaydedici, Platforms.Windows.WindowsDosyaKaydedici>();
#endif
        builder.Services.AddSingleton(sp =>
        {
            // Çerez kapalı (yalnız Bearer) + 30 sn zaman aşımı.
            var http = KasaApiClient.HttpOlustur(new Uri("https://kasa.emarglobal.com/"));
            return new KasaApiClient(http, sp.GetRequiredService<ITokenStore>());
        });
        builder.Services.AddSingleton<IKasaApi>(sp => sp.GetRequiredService<KasaApiClient>());

        builder.Services.AddSingleton<AuthViewModel>();
        builder.Services.AddTransient<PanelViewModel>();
        builder.Services.AddTransient<KasaSayimiViewModel>();
        builder.Services.AddTransient<HaftalikViewModel>();
        builder.Services.AddTransient<AylikViewModel>();
        builder.Services.AddTransient<CarilerViewModel>();
        builder.Services.AddTransient<IslemlerViewModel>();
        builder.Services.AddTransient<KrediKartlariViewModel>();
        builder.Services.AddTransient<CeklerViewModel>();
        builder.Services.AddTransient<AyarlarViewModel>();

        builder.Services.AddSingleton<App>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<Views.LoginPage>();
        builder.Services.AddTransient<Views.PanelPage>();
        builder.Services.AddTransient<Views.KasaSayimiPage>();
        builder.Services.AddTransient<Views.HaftalikPage>();
        builder.Services.AddTransient<Views.AylikPage>();
        builder.Services.AddTransient<Views.CarilerPage>();
        builder.Services.AddTransient<Views.IslemlerPage>();
        builder.Services.AddTransient<Views.KrediKartlariPage>();
        builder.Services.AddTransient<Views.CeklerPage>();
        builder.Services.AddTransient<Views.AyarlarPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
