using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// CSV dışı indirmeler (yazdırılabilir aylık rapor .html, ay paketi .zip) için ad güvenliği ve
/// kaydetme akışı. Kurallar <see cref="DosyaKayit.GuvenliAd"/> ile aynıdır; tek fark izinli
/// uzantıların (.html, .zip) korunmasıdır. Başka her uzantı yine .csv'ye çevrilir: dosya varsayılan
/// uygulamayla açıldığı için çalıştırılabilir bir tür hiçbir zaman kaydedilmez.
/// </summary>
public static class DosyaAktarma
{
    /// <summary>CSV'ye ek olarak korunan uzantılar.</summary>
    public static readonly IReadOnlyList<string> EkUzantilar = [".html", ".zip"];

    /// <summary>
    /// Sunucunun önerdiği adı güvenli bir Windows dosya adına çevirir. ".html" / ".zip" korunur
    /// (uzantı küçük harfe iner); diğer her şey <see cref="DosyaKayit.GuvenliAd"/> kuralıyla ".csv" olur.
    /// </summary>
    public static string GuvenliAd(string? ad)
    {
        var csv = DosyaKayit.GuvenliAd(ad);
        var ham = (ad ?? "").Trim().TrimEnd('.', ' ');
        if (ham.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return csv;
        foreach (var uzanti in EkUzantilar)
        {
            var sonek = uzanti + ".csv";
            if (csv.EndsWith(sonek, StringComparison.OrdinalIgnoreCase) && csv.Length > sonek.Length)
                return csv[..^sonek.Length] + uzanti;
        }
        return csv;
    }

    /// <summary>İndir → güvenli adla kaydet (kaydedici dosyayı varsayılan uygulamayla açar) → tam yolu dön.</summary>
    public static async Task<string> KaydetAsync(IDosyaKaydedici? kaydedici, Func<Task<IndirilenDosya>> indir)
    {
        if (kaydedici is null) throw new DogrulamaHatasi(ExcelAktarma.KaydediciYok);
        var dosya = await indir();
        try
        {
            return await kaydedici.KaydetAsync(GuvenliAd(dosya.DosyaAdi), dosya.Icerik);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DogrulamaHatasi(ExcelAktarma.KaydedilemediMesaji);
        }
    }
}
