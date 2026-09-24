namespace Kasa.App.Core;

/// <summary>
/// Kart limit uyarısı (yalnız gösterim; hiçbir hesaba girmez): güncel borç limitin
/// <see cref="Esik"/>'ine ulaştıysa uyarı. Limiti 0 (girilmemiş) olan kartta uyarı yok.
/// </summary>
public static class KartLimit
{
    /// <summary>Uyarı eşiği: limitin %80'i.</summary>
    public const decimal Esik = 0.80m;

    /// <summary>Kullanılan oran (borç / limit); limit yoksa null. Alacaklı kartta 0.</summary>
    public static decimal? Oran(decimal guncelBorc, decimal limit)
        => limit > 0 ? Math.Max(0m, guncelBorc) / limit : null;

    public static bool UyariVar(decimal guncelBorc, decimal limit)
        => Oran(guncelBorc, limit) is { } o && o >= Esik;

    /// <summary>Tam sayı yüzde (aşağı yuvarlanır: %79,9 → %79, eşik altı kalır).</summary>
    public static int Yuzde(decimal guncelBorc, decimal limit)
        => Oran(guncelBorc, limit) is { } o ? (int)Math.Floor(o * 100m) : 0;

    /// <summary>"Limitin %85'i kullanıldı" / "Limit aşıldı (%112)"; uyarı yoksa null.</summary>
    public static string? Metin(decimal guncelBorc, decimal limit)
    {
        if (!UyariVar(guncelBorc, limit)) return null;
        var y = Yuzde(guncelBorc, limit);
        return guncelBorc > limit ? $"Limit aşıldı (%{y})" : $"Limitin %{y}'{PanelMetin.SayiEki(y)} kullanıldı";
    }
}
