using System.Globalization;
using System.Text;

namespace Kasa.ApiClient;

/// <summary>
/// İndirilen ve yüklenen dosyaların güvenli adı ve uzantısı (purchase-1, apiclient-3). Sunucu belge adını tespit ettiği
/// türle zaten normalize eder; istemci bunu yine de sunucunun bildirdiği içerik türüne göre yeniden kurar: uzantı yalnız
/// bilinen türden gelir, alıcının yüklediği '.hta'/'.cmd' gibi bir uzantı ya da U+202E gibi yön işareti masaüstünde
/// kaydedilen dosya adına geçemez. Tür bilinmiyorsa yalnız güvenli uzantılar korunur, gerisi '.bin' olur.
/// </summary>
public static class DosyaTurleri
{
    private static readonly Dictionary<string, string> TurUzantilari = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["application/zip"] = ".zip",
        ["text/csv"] = ".csv",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
        // Yalnız yazdırılabilir rapor (sunucunun kendi ürettiği HTML); bilinmeyen türde .html korunmaz.
        ["text/html"] = ".html",
    };
    /// <summary>İçerik türü bilinmezken (eski sunucu, application/octet-stream) addan korunabilecek uzantılar.</summary>
    private static readonly HashSet<string> GuvenliUzantilar = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".png", ".jpg", ".jpeg", ".zip", ".csv", ".xlsx" };
    private static readonly HashSet<string> AyrilmisAdlar = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>İçerik türünün uzantısı (".pdf", ".xlsx" ...); bilinmeyen ya da boş türde ".bin".</summary>
    public static string Uzanti(string? icerikTuru) => TurUzantilari.GetValueOrDefault(MedyaTuru(icerikTuru), ".bin");

    /// <summary>Kaydetme penceresinde gösterilecek tür açıklaması.</summary>
    public static string Aciklama(string? icerikTuru) => Uzanti(icerikTuru) switch
    {
        ".pdf" => "PDF belgesi", ".png" => "PNG görseli", ".jpg" => "JPEG görseli", ".zip" => "ZIP arşivi",
        ".csv" => "CSV dosyası", ".xlsx" => "Excel dosyası", ".html" => "HTML raporu", _ => "Dosya",
    };

    /// <summary>
    /// Güvenli dosya adı: yol parçaları; kontrol, Unicode biçim (yön işaretleri, sıfır genişlikli karakterler), satır ayırıcı
    /// ve eşi olmayan vekil karakterleri; Windows'ta geçersiz karakterler atılır. Uzantı <paramref name="icerikTuru"/>'nden
    /// gelir; addaki son uzantı ve onun önünde kalan türün kendi uzantısı çıkarılır. Gövde en çok 120 karakterdir. Boş ya da
    /// Windows ayrılmış adı (CON, NUL, COM1 ...) olan gövde <paramref name="varsayilan"/>'ın gövdesiyle değişir.
    /// </summary>
    public static string GuvenliAd(string? ad, string? icerikTuru, string varsayilan)
    {
        var govde = Temizle(ad?.Trim().Trim('"'));
        var son = UzantiBenzeri(govde);
        var uzanti = TurUzantilari.TryGetValue(MedyaTuru(icerikTuru), out var turden) ? turden
            : son is not null && GuvenliUzantilar.Contains(son) ? son : ".bin";
        if (son is not null) govde = govde[..^son.Length].TrimEnd(' ', '.');
        if (govde.EndsWith(uzanti, StringComparison.OrdinalIgnoreCase) || uzanti == ".jpg" && govde.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            govde = govde[..govde.LastIndexOf('.')].TrimEnd(' ', '.');
        if (govde.Length > 120) govde = govde[..(char.IsHighSurrogate(govde[119]) ? 119 : 120)].TrimEnd(' ', '.');
        if (govde.Length == 0 || AyrilmisAdlar.Contains(govde.Split('.')[0].TrimEnd(' ')))
            govde = Path.GetFileNameWithoutExtension(varsayilan);
        return govde + uzanti;
    }

    /// <summary>Yüklenen dosyanın gönderilen adı: yalnız temizlenir (tür ve uzantıyı sunucu sihirli baytlardan belirler).</summary>
    internal static string GonderilecekAd(string? ad, string varsayilan)
    {
        var temiz = Temizle(ad?.Trim().Trim('"'));
        return temiz.Length == 0 ? varsayilan : temiz;
    }

    private static string MedyaTuru(string? icerikTuru) => (icerikTuru ?? "").Split(';')[0].Trim();

    private static string Temizle(string? ad)
    {
        var s = (ad ?? "").Replace('\\', '/');
        s = s[(s.LastIndexOf('/') + 1)..];
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
            if (char.IsSurrogate(c) || char.IsControl(c) || "<>:\"|?*".Contains(c)) continue;
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) continue;
            sb.Append(c);
        }
        return sb.ToString().TrimStart(' ').TrimEnd(' ', '.');
    }

    /// <summary>Addaki son uzantı: harf içeren, en çok 8 karakterlik '.xxx'; yalnız rakamdan oluşan son ek uzantı sayılmaz.</summary>
    private static string? UzantiBenzeri(string ad)
    {
        var nokta = ad.LastIndexOf('.');
        if (nokta < 0 || ad.Length - nokta - 1 is < 1 or > 8) return null;
        var son = ad[nokta..];
        return son.Skip(1).All(char.IsLetterOrDigit) && son.Skip(1).Any(char.IsLetter) ? son : null;
    }
}
