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
        // Font paketlenmez: yazı ailesi Styles.xaml'daki YaziAilesi anahtarındaki Windows sistem fontudur (₺ içerir).
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddSingleton(sp =>
        {
            // Native oturumun tek kaynağı güvenli depodaki Bearer token'dır.
            // API'nin web çerezleri çıkıştan sonra gizli bir ikinci oturum oluşturmamalı.
            var http = new HttpClient(new HttpClientHandler { UseCookies = false })
            {
                BaseAddress = ApiAdresi.Coz(Environment.GetEnvironmentVariable("KASA_API_URL")),
                // Süre sınırı istek başına KasaApiClient'ta, gövde okuması dahil uygulanır: normal 15 sn, yükleme 2 dk,
                // indirme 5 dk, yedek 15 dk. Genel HttpClient sınırı uzun dosya işlemlerini kesmesin diye kapalıdır.
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
            return new KasaApiClient(http, sp.GetRequiredService<ITokenStore>(), KasaZamanAsimlari.Varsayilanlar);
        });
        builder.Services.AddSingleton<IKasaApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IAlisApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IYonetimApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IAlisOdemeApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IFinansTakipApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IBildirimApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IBenzerKayitApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IKasaKontrolApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IAylikGiderApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IEkstreAktarmaApi>(sp => sp.GetRequiredService<KasaApiClient>());

        builder.Services.AddSingleton<AuthViewModel>();
        builder.Services.AddTransient<PanelViewModel>();
        builder.Services.AddTransient<HaftalikViewModel>();
        builder.Services.AddTransient<AylikViewModel>();
        builder.Services.AddTransient<IslemlerViewModel>();
        builder.Services.AddTransient<AyarlarViewModel>();
        builder.Services.AddTransient<AlislarViewModel>();
        builder.Services.AddTransient<GuvenlikViewModel>();
        builder.Services.AddTransient<DisariAktarViewModel>();
        builder.Services.AddTransient<KartTakipViewModel>();
        builder.Services.AddTransient<KrediTakipViewModel>();
        builder.Services.AddTransient<TakipOzetViewModel>();
        builder.Services.AddTransient<BildirimViewModel>();
        builder.Services.AddTransient<AylikGiderViewModel>();
        builder.Services.AddTransient<KasaKontrolViewModel>();
        builder.Services.AddTransient<KasaEsikViewModel>();
        builder.Services.AddTransient<AyKilidiViewModel>();
        builder.Services.AddTransient<EkstreAktarmaViewModel>();

        builder.Services.AddSingleton<App>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<Views.LoginPage>();
        builder.Services.AddTransient<Views.PanelPage>();
        builder.Services.AddTransient<Views.HaftalikPage>();
        builder.Services.AddTransient<Views.AylikPage>();
        builder.Services.AddTransient<Views.IslemlerPage>();
        builder.Services.AddTransient<Views.AyarlarPage>();
        builder.Services.AddTransient<Views.AlislarPage>();
        builder.Services.AddTransient<Views.DisariAktarPage>();
        builder.Services.AddTransient<Views.KartTakipPage>();
        builder.Services.AddTransient<Views.KrediTakipPage>();
        builder.Services.AddTransient<Views.BildirimPage>();
        builder.Services.AddTransient<Views.AylikGiderPage>();
        builder.Services.AddTransient<Views.EkstreAktarmaPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
