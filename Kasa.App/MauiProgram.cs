using Kasa.ApiClient;
using Kasa.App.Core;
using Kasa.App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

namespace Kasa.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        // Platform sistem fontları Styles.xaml içindeki YaziAilesi kaynağından seçilir.
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
        builder.Services.AddSingleton<ICekApi>(sp => sp.GetRequiredService<KasaApiClient>());
        // Tek bağlantı durumu (tasarım 2026-10-02 §3): istemcinin ağ hatası kopuk, her yanıt bağlı yapar; kabuk şeridi ve
        // ekranlar (AuthViewModel, rapor modelleri) aynı örneği okur.
        builder.Services.AddSingleton<IBaglantiBildirimleri>(sp => sp.GetRequiredService<KasaApiClient>());
        // Kopukken 15 sn'de bir GET /health (H-2): sunucu geri gelince şerit kendiliğinden kalkar; pencere kapanınca durur.
#if IOS
        builder.Services.AddSingleton<UygulamaIciBildirimAyari>();
        builder.Services.AddSingleton<IBaglantiYoklamasi>(sp => new OnPlanBaglantiYoklamasi(
            sp.GetRequiredService<KasaApiClient>(), sp.GetRequiredService<UygulamaIciBildirimAyari>()));
#else
        builder.Services.AddSingleton<IBaglantiYoklamasi>(sp => sp.GetRequiredService<KasaApiClient>());
#endif
        builder.Services.AddSingleton<BaglantiDurumu>();

        // Masaüstü Windows bildirimleri (tasarım 2026-09-30): yerel dosyalar %LOCALAPPDATA%\EmarKasa altında; gösterici ve tıklama
        // kuyruğu Windows katmanının tek örneğidir (Platforms/Windows/App.xaml.cs onu DI kurulmadan önce başlatır).
#if WINDOWS
        builder.Services.AddSingleton<IBildirimAyari>(_ => DosyaBildirimAyari.Varsayilan());
        builder.Services.AddSingleton<IGosterilenBildirimDeposu>(_ => DosyaGosterilenBildirimDeposu.Varsayilan());
        builder.Services.AddSingleton<IBildirimGorevi>(_ => BildirimGorevi.Varsayilan());
        builder.Services.AddSingleton<IBildirimGosterici>(WinUI.WindowsBildirimGosterici.Ortak);
        builder.Services.AddSingleton<BildirimTiklamalari>(WinUI.WindowsBildirimGosterici.Ortak.Tiklamalar);
        // Pencere kapanınca bildirim kaydı kaldırılır (ProcessExit her kapanışta tetiklenmez; Bitir birden çok çağrılabilir).
        builder.ConfigureLifecycleEvents(olaylar => olaylar.AddWindows(windows => windows.OnClosed((_, _) =>
        {
            WinUI.WindowsBildirimGosterici.Ortak.Bitir();
            IPlatformApplication.Current?.Services.GetService<BaglantiDurumu>()?.Dispose();
        })));
#elif IOS
        // Yerel sistem bildirimi/APNs yok: liste ve rozet yalnız uygulama açıkken yenilenir. Windows görevi kurulmaz.
        builder.Services.AddSingleton<IBildirimAyari>(sp => sp.GetRequiredService<UygulamaIciBildirimAyari>());
        builder.Services.AddSingleton<IGosterilenBildirimDeposu>(_ => new DosyaGosterilenBildirimDeposu(FileSystem.AppDataDirectory));
        builder.Services.AddSingleton<IBildirimGorevi, BildirimGoreviYok>();
        builder.Services.AddSingleton<IBildirimGosterici, UygulamaIciBildirimGosterici>();
        builder.Services.AddSingleton<BildirimTiklamalari>();
        builder.ConfigureLifecycleEvents(olaylar => olaylar.AddiOS(ios => ios
            .OnActivated(_ => IosOnPlanAyarla(true))
            .OnResignActivation(_ => IosOnPlanAyarla(false))
            .DidEnterBackground(_ => IosOnPlanAyarla(false))
            .WillTerminate(_ => IPlatformApplication.Current?.Services.GetService<BaglantiDurumu>()?.Dispose())));
#endif
        builder.Services.AddSingleton<BildirimYoklayici>();
        builder.Services.AddSingleton<BildirimNobetcisi>();
        builder.Services.AddSingleton<BildirimKontrolu>();

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
        builder.Services.AddTransient<CekTakipViewModel>();
        builder.Services.AddTransient<CekOzetViewModel>();

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
        builder.Services.AddTransient<Views.CekTakipPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

#if IOS
    private static void IosOnPlanAyarla(bool onPlanda)
    {
        var hizmetler = IPlatformApplication.Current?.Services;
        if (hizmetler?.GetService<UygulamaIciBildirimAyari>() is not { } ayar)
            return;
        if (ayar.OnPlanAyarla(onPlanda) && onPlanda)
            _ = IosOnPlandaYenileAsync(hizmetler);
    }

    private static async Task IosOnPlandaYenileAsync(IServiceProvider hizmetler)
    {
        try
        {
            if (hizmetler.GetService<BaglantiDurumu>() is { } baglanti)
                await baglanti.HemenYoklaAsync();
            if (hizmetler.GetService<BildirimNobetcisi>() is { } nobetci)
                await nobetci.TikAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ön plan bağlantı/bildirim yenilemesi başarısız: {ex}");
        }
    }
#endif
}
