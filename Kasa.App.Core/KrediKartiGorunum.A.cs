namespace Kasa.App.Core;

// Paket A — kart limit uyarısı (yalnız gösterim).
public sealed partial class KrediKartiGorunum
{
    /// <summary>Güncel borç limitin %80'ine ulaştı mı (limit girilmişse)?</summary>
    public bool LimitUyarisi => KartLimit.UyariVar(GuncelBorc, Limit);

    /// <summary>"Limitin %85'i kullanıldı" / "Limit aşıldı (%112)"; uyarı yoksa null.</summary>
    public string? LimitUyariMetni => KartLimit.Metin(GuncelBorc, Limit);
}
