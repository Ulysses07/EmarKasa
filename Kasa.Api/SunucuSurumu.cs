using System.Reflection;

namespace Kasa.Api;

/// <summary>
/// Sunucu sürümü (/api/surum yanıtındaki "surum"). Tek kaynağı depo kökündeki Directory.Build.props'taki KasaSurumu'dur:
/// Kasa.Api.csproj onu Version olarak derlemeye yazar, burada derleme meta verisinden (AssemblyInformationalVersion) okunur;
/// kodda sürüm dizesi yoktur. .NET 8 SDK'sından beri bilgi sürümü git deposunda derlenince "+&lt;commit&gt;" eki taşır; o ek
/// atılır. Desteklenen en eski istemci (minimumIstemci) ayrı bir kavramdır, <see cref="YonetimEndpoints"/>'te açıkça yazılır.
/// </summary>
public static class SunucuSurumu
{
    public static string Deger { get; } = Ayikla(typeof(SunucuSurumu).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Bilgi sürümünden derleme ekini ("+" sonrası) atar: "1.2.3+abc123" → "1.2.3".</summary>
    public static string Ayikla(string? bilgiSurumu) =>
        string.IsNullOrWhiteSpace(bilgiSurumu)
            ? throw new InvalidOperationException("Kasa.Api derlemesinde AssemblyInformationalVersion yok (KasaSurumu, Directory.Build.props).")
            : bilgiSurumu.Split('+')[0];
}
