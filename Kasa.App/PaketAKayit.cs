using Kasa.App.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.App;

/// <summary>
/// Paket A kayıtları; MauiProgram'a tek satırla bağlanır. Yerel depo (cihaza özel ayarlar: bildirim anahtarları,
/// tahmin ufku, "son bakış"), kısa bildirim servisi, bildirim ayarları sayfası ve rotası.
/// </summary>
public static class PaketAKayit
{
    public const string BildirimAyarlariRotasi = "bildirimayarlari";

    public static IServiceCollection PaketAServisleriniEkle(this IServiceCollection s)
    {
        // Arka plan hatırlatıcısı da aynı klasörü okur (%LOCALAPPDATA%\EmarKasa\yerel).
        s.AddSingleton<IYerelDepo>(_ => DosyaYerelDepo.Varsayilan());
#if WINDOWS
        s.AddSingleton<IKisaBildirim>(sp =>
            (Platforms.Windows.WindowsBildirimServisi)sp.GetRequiredService<IBildirimServisi>());
#endif
        s.AddTransient<BildirimAyarlariViewModel>();
        s.AddTransient<Views.BildirimAyarlariPage>();
        Routing.RegisterRoute(BildirimAyarlariRotasi, typeof(Views.BildirimAyarlariPage));
        return s;
    }
}
