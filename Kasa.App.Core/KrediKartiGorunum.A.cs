using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

// Paket A — kart limit uyarısı ve "Bugün yapılacaklar"dan gelinen kartın vurgusu (yalnız gösterim).
public sealed partial class KrediKartiGorunum
{
    /// <summary>"Ödeme gir" derin bağlantısıyla açılan kart (çerçevesi vurgulanır).</summary>
    [ObservableProperty] private bool _vurgulu;

    /// <summary>Güncel borç limitin %80'ine ulaştı mı (limit girilmişse)?</summary>
    public bool LimitUyarisi => KartLimit.UyariVar(GuncelBorc, Limit);

    /// <summary>"Limitin %85'i kullanıldı" / "Limit aşıldı (%112)"; uyarı yoksa null.</summary>
    public string? LimitUyariMetni => KartLimit.Metin(GuncelBorc, Limit);
}
