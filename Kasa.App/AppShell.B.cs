using Kasa.App.Core;

namespace Kasa.App;

/// <summary>Paket B: menüde olmayan, üstüne açılan (geri düğmeli) sayfaların rotaları.</summary>
public partial class AppShell
{
    static AppShell()
    {
        Routing.RegisterRoute(Rotalar.KasaDokumu, typeof(Views.KasaDokumuPage));
        Routing.RegisterRoute(Rotalar.HedefButce, typeof(Views.HedefButcePage));
    }
}
