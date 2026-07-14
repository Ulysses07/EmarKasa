using System.Net;

namespace Kasa.ApiClient;

/// <summary>API başarısız durum kodu döndürdüğünde fırlatılır. 401 → app katmanı token silip Login'e döner.</summary>
public sealed class KasaApiException : Exception
{
    public HttpStatusCode DurumKodu { get; }
    public KasaApiException(HttpStatusCode kod, string? mesaj = null)
        : base(mesaj ?? $"API hatası: {(int)kod} {kod}") => DurumKodu = kod;
}
