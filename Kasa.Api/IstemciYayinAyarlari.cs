namespace Kasa.Api;

/// <summary>Yayımlanmış istemci bilgisi sunucu ürün sürümünden ayrı tutulur. SonSurum yalnız
/// paket ilgili kanalda gerçekten kullanıma açıldıktan sonra yapılandırılır; boşken yayın var sayılmaz.</summary>
public static class IstemciYayinAyarlari
{
    public const string PlatformBasligi = "X-Kasa-Istemci-Platformu";
    public const string WindowsIndirmeAdresi = "https://github.com/Ulysses07/EmarKasa/releases";
    public const string IosIndirmeAdresi = "https://testflight.apple.com/";

    public static string Platform(string? deger) => string.Equals(deger?.Trim(), "ios", StringComparison.OrdinalIgnoreCase) ? "ios" : "windows";

    public static IstemciYayinBilgisi Oku(IConfiguration cfg, string? platform)
    {
        var ios = Platform(platform) == "ios";
        var onek = "Kasa:IstemciYayinlari:" + (ios ? "Ios" : "Windows") + ":";
        var minimum = Minimum(cfg, platform);
        var notlar = cfg[onek + "Notlar"] ?? (ios
            ? "Güncellemeler TestFlight üzerinden alınır. Her TestFlight build'i 90 gün geçerlidir; erişim için tester daveti gerekir."
            : "Windows kurulumunu yayımlanmış sürümün dosyalarından alın.");
        return new(Surum(cfg[onek + "SonSurum"]), minimum, ios ? IosIndirmeAdresi : WindowsIndirmeAdresi, notlar);
    }

    public static string Minimum(IConfiguration cfg, string? platform)
    {
        var anahtar = "Kasa:IstemciYayinlari:" + (Platform(platform) == "ios" ? "Ios" : "Windows") + ":MinimumIstemci";
        var belirlenen = Surum(cfg[anahtar]);
        // Mevcut uyumluluk sınırı sessizce düşürülmez. Gelecekteki artış her platform için açık yapılandırma ister.
        return belirlenen is not null && Version.Parse(belirlenen) >= Version.Parse(YonetimEndpoints.MinimumIstemci)
            ? belirlenen : YonetimEndpoints.MinimumIstemci;
    }

    private static string? Surum(string? deger) => Version.TryParse(deger?.Trim(), out var surum)
        && surum.Build >= 0 && surum.Revision < 0 ? surum.ToString(3) : null;
}

public sealed record IstemciYayinBilgisi(string? SonSurum, string MinimumIstemci, string IndirmeAdresi, string? Notlar);
