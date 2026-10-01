using System.Text.RegularExpressions;

namespace Kasa.App.Core;

/// <summary>Sunucu bildiriminin <c>Hedef</c> alanını (web rotası: "/#cards/3", "/#loans/7") masaüstü Shell rotasına çevirir
/// (tasarım 2026-09-30 masaüstü bildirimleri §1 Tıklama). Kart → Kartlar sayfasında o kart (KartId), kredi → Krediler sayfasında
/// o kredi (KrediId); kimliksiz kart/kredi hedefi yalnız sayfayı açar; tanınmayan ya da boş hedef Bildirimler sayfasını açar.
/// Sunucunun ürettiği diğer hedefler (kasa alt sınırı ve kaynak hatası: "/#home") tanınmayan sayılır.</summary>
public static partial class BildirimHedefi
{
    /// <summary>Tanınmayan hedefte açılan rota.</summary>
    public const string Varsayilan = "//bildirimler";

    public static string Rota(string? hedef)
    {
        var eslesme = HedefDeseni().Match(hedef ?? "");
        if (!eslesme.Success)
            return Varsayilan;
        var kimlik = eslesme.Groups["id"];
        return (eslesme.Groups["tur"].Value, kimlik.Success) switch
        {
            ("cards", true) => "//kartlar?KartId=" + kimlik.Value,
            ("cards", false) => "//kartlar",
            ("loans", true) => "//krediler?KrediId=" + kimlik.Value,
            _ => "//krediler",
        };
    }

    [GeneratedRegex(@"^/#(?<tur>cards|loans)(?:/(?<id>[1-9][0-9]{0,8}))?\z")]
    private static partial Regex HedefDeseni();
}
