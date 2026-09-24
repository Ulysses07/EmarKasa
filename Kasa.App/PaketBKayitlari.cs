using Kasa.App.Core;
using Kasa.App.Services;

namespace Kasa.App;

/// <summary>Paket B (raporlar ve ay kapanışı): gezinme, yeni görünüm modelleri ve sayfalar.</summary>
public static class PaketBKayitlari
{
    public static IServiceCollection AddPaketB(this IServiceCollection s)
    {
        // Rapordan İşlemler'e iniş, kasa dökümü ve hedef sayfasına gezinme (Shell).
        s.AddSingleton<IGezinti, ShellGezinti>();

        s.AddTransient<KasaDokumuViewModel>();
        s.AddTransient<GrafiklerViewModel>();
        s.AddTransient<HedefButceViewModel>();
        s.AddTransient<CariOzetiViewModel>();

        s.AddTransient<Views.KasaDokumuPage>();
        s.AddTransient<Views.GrafiklerPage>();
        s.AddTransient<Views.HedefButcePage>();
        s.AddTransient<Views.CariOzetiPage>();
        return s;
    }
}
