using System.Net;

namespace Kasa.ApiClient;

/// <summary>
/// API başarısız durum kodu döndürdüğünde fırlatılır. Token'lı bir istek 401 alırsa istemci
/// token'ı siler ve <see cref="IKasaApi.OturumSonaErdi"/> olayını tetikler; uygulama Login'e döner.
/// </summary>
public sealed class KasaApiException : Exception
{
    public HttpStatusCode DurumKodu { get; }
    /// <summary>Sunucunun kullanıcıya gösterilebilir açıklaması (doğrulama hatası vb.); yoksa null.</summary>
    public string? SunucuMesaji { get; }
    public KasaApiException(HttpStatusCode kod, string? mesaj = null)
        : base(mesaj ?? $"API hatası: {(int)kod} {kod}")
    {
        DurumKodu = kod;
        SunucuMesaji = mesaj;
    }
}
