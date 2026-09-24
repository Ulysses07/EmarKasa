using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Platform dosya kaydedici (Windows: Belgeler\Emar Kasa klasörü). İçeriği verilen adla kaydeder,
/// varsayılan uygulamayla (Excel) açmayı dener ve kaydedilen tam yolu döner. Ad güvenliği ve
/// çakışma kuralları <see cref="DosyaKayit"/>'tadır.
/// </summary>
public interface IDosyaKaydedici
{
    Task<string> KaydetAsync(string dosyaAdi, byte[] icerik);
}

/// <summary>Kaydedicilerin platformdan bağımsız kuralları (birim testli).</summary>
public static class DosyaKayit
{
    /// <summary>Belgeler altındaki klasör.</summary>
    public const string KlasorAdi = "Emar Kasa";
    public const string VarsayilanAd = "kasa-aktarim.csv";
    public const int EnUzunAd = 120;

    private static readonly char[] Yasak = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly string[] AyrilmisAdlar =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>
    /// Sunucudan gelen adı güvenli bir Windows dosya adına çevirir: klasör parçaları atılır
    /// (yalnız son parça), yasak ve kontrol karakterleri '-' olur, sondaki nokta/boşluk silinir,
    /// ayrılmış adların (CON, NUL…) önüne "kasa-" eklenir, uzantı .csv değilse eklenir (dosya
    /// varsayılan uygulamayla açılacağı için başka tür çalıştırılamasın). Boşsa <see cref="VarsayilanAd"/>.
    /// </summary>
    public static string GuvenliAd(string? ad)
    {
        var s = (ad ?? "").Replace('\\', '/');
        s = s[(s.LastIndexOf('/') + 1)..];
        s = new string(s.Select(c => c < 32 || Yasak.Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (!s.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) s += ".csv";
        var govde = s[..^4].TrimEnd('.', ' ');
        if (govde.Length == 0) return VarsayilanAd;
        if (govde.Length > EnUzunAd) govde = govde[..EnUzunAd];
        var kok = govde.Split('.')[0];
        if (AyrilmisAdlar.Contains(kok, StringComparer.OrdinalIgnoreCase)) govde = "kasa-" + govde;
        return govde + ".csv";
    }

    /// <summary>
    /// Klasörde bu adla dosya varsa "ad (2).csv", "ad (3).csv"… döner: var olan dosya (belki
    /// düzenlenmiş ya da Excel'de açık) ezilmez.
    /// </summary>
    public static string BenzersizYol(string klasor, string ad, Func<string, bool> varMi)
    {
        var yol = Path.Combine(klasor, ad);
        if (!varMi(yol)) return yol;
        var govde = Path.GetFileNameWithoutExtension(ad);
        var uzanti = Path.GetExtension(ad);
        for (int i = 2; ; i++)
        {
            yol = Path.Combine(klasor, $"{govde} ({i}){uzanti}");
            if (!varMi(yol)) return yol;
        }
    }
}

/// <summary>"Excel'e aktar" düğmelerinin ortak akışı: indir → güvenli adla kaydet → yolu dön.</summary>
public static class ExcelAktarma
{
    public const string KaydediciYok = "Bu cihazda dosya kaydedilemiyor.";
    public const string KaydedilemediMesaji =
        "Dosya kaydedilemedi. Belgeler\\Emar Kasa klasörüne yazılabildiğini ve aynı adlı dosyanın açık olmadığını kontrol edin.";

    /// <summary>Kaydedilen dosyanın tam yolu. Hata istisna olarak (Türkçe mesajla) yükselir.</summary>
    public static async Task<string> AktarAsync(IDosyaKaydedici? kaydedici, Func<Task<IndirilenDosya>> indir)
    {
        if (kaydedici is null) throw new DogrulamaHatasi(KaydediciYok);
        var dosya = await indir();
        try
        {
            return await kaydedici.KaydetAsync(DosyaKayit.GuvenliAd(dosya.DosyaAdi), dosya.Icerik);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DogrulamaHatasi(KaydedilemediMesaji);
        }
    }
}
