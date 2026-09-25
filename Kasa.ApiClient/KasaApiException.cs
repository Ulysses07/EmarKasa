using System.Net;

namespace Kasa.ApiClient;

/// <summary>API başarısız durum kodu döndürdüğünde fırlatılır. 401 → app katmanı token silip Login'e döner.</summary>
public sealed class KasaApiException : Exception
{
    public HttpStatusCode DurumKodu { get; }
    public KasaApiException(HttpStatusCode kod, string? mesaj = null)
        : base(mesaj ?? (kod switch
        {
            HttpStatusCode.BadRequest => "Girilen bilgileri kontrol edin.",
            HttpStatusCode.Conflict => "Bu kayıt başka bir kayıtla çakışıyor veya kullanımda olduğu için değiştirilemiyor.",
            HttpStatusCode.UnprocessableEntity => "PDF okunamadı. Metin içeren, şifresiz belgeyi kontrol edip yeniden deneyin.",
            HttpStatusCode.RequestEntityTooLarge => "PDF en fazla 10 MB ve 50 sayfa olabilir.",
            HttpStatusCode.ServiceUnavailable => "Sunucu şu anda işlemi tamamlayamıyor. Bir süre sonra yeniden deneyin.",
            _ => $"API hatası: {(int)kod} {kod}",
        })) => DurumKodu = kod;
}
