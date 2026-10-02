using System.Text.RegularExpressions;

namespace Kasa.App.Core;

/// <summary>Sunucu bildiriminin <c>Hedef</c> alanını (web rotası: "/#cards/3", "/#loans/7", "/#cheques/5") masaüstü Shell rotasına
/// çevirir (tasarım 2026-09-30 masaüstü bildirimleri §1 Tıklama; 2026-10-01 çekler "Bildirimler"). Kart → Kartlar sayfasında o kart
/// (KartId), kredi → Krediler sayfasında o kredi (KrediId), çek → Çekler sayfasında o çek (CekId); kimliksiz hedef yalnız sayfayı
/// açar; tanınmayan ya da boş hedef Bildirimler sayfasını açar.
/// Kasa alt sınırı ve çek dışı kaynak hatası "/#home" üretir, tanınmayan sayılır; çek kaynak hatası ise kimliksiz çek hedefi
/// gibi "/#cheques" üretir ve Çekler sayfasını açar.</summary>
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
            ("loans", false) => "//krediler",
            ("cheques", true) => "//cekler?CekId=" + kimlik.Value,
            ("cheques", false) => "//cekler",
            _ => Varsayilan,
        };
    }

    [GeneratedRegex(@"^/#(?<tur>cards|loans|cheques)(?:/(?<id>[1-9][0-9]{0,8}))?\z")]
    private static partial Regex HedefDeseni();
}
