using System.Net;
using System.Text.RegularExpressions;

namespace Kasa.ApiClient;

/// <summary>API başarısız durum kodu döndürdüğünde fırlatılır. 401 → app katmanı token silip Login'e döner.</summary>
public sealed partial class KasaApiException : Exception
{
    public HttpStatusCode DurumKodu { get; }
    /// <summary>Sunucu hatası (5xx) yanıtının ProblemDetails iz kimliği (traceId); sunucu logundaki "iz" ile aynıdır. Yoksa null.</summary>
    public string? IzKimligi { get; }
    /// <summary>Kullanıcıya gösterilen kısa iz ("Hata kodu: …"); bkz. <see cref="KisaIz"/>.</summary>
    public string? HataKodu => KisaIz(IzKimligi);
    public KasaApiException(HttpStatusCode kod, string? mesaj = null, string? izKimligi = null)
        : base(mesaj ?? (kod switch
        {
            HttpStatusCode.BadRequest => "Girilen bilgileri kontrol edin.",
            HttpStatusCode.Conflict => "Bu kayıt başka bir kayıtla çakışıyor veya kullanımda olduğu için değiştirilemiyor.",
            HttpStatusCode.UnprocessableEntity => "PDF okunamadı. Metin içeren, şifresiz belgeyi kontrol edip yeniden deneyin.",
            HttpStatusCode.RequestEntityTooLarge => "PDF en fazla 10 MB ve 50 sayfa olabilir.",
            HttpStatusCode.TooManyRequests => "Çok fazla deneme yapıldı. Birkaç dakika sonra yeniden deneyin.",
            HttpStatusCode.ServiceUnavailable => "Sunucu şu anda işlemi tamamlayamıyor. Bir süre sonra yeniden deneyin.",
            _ => $"API hatası: {(int)kod} {kod}",
        }))
    {
        DurumKodu = kod;
        IzKimligi = izKimligi;
    }

    /// <summary>İz kimliğinin kullanıcıya gösterilen kısa biçimi (web traceCode ile aynı kural): W3C biçiminde
    /// ("00-&lt;32 hex&gt;-&lt;16 hex&gt;-&lt;2 hex&gt;") iz numarasının ilk 8 hanesi, diğer kimlikte (TraceIdentifier) en çok
    /// 24 karakterse kendisi, daha uzunsa ilk 12 karakteri. Parça tam kimliğin içindedir: yönetici logda arar. Beklenmeyen
    /// karakter içeren ya da boş değer gösterilmez (null).</summary>
    public static string? KisaIz(string? iz)
    {
        var metin = iz?.Trim();
        if (string.IsNullOrEmpty(metin) || !IzKarakterleri().IsMatch(metin)) return null;
        if (W3cIz().Match(metin) is { Success: true } w3c) return w3c.Groups["iz"].Value[..8];
        return metin.Length <= 24 ? metin : metin[..12];
    }

    [GeneratedRegex("^[A-Za-z0-9:._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IzKarakterleri();
    [GeneratedRegex("^[0-9a-f]{2}-(?<iz>[0-9a-f]{32})-[0-9a-f]{16}-[0-9a-f]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex W3cIz();
}
