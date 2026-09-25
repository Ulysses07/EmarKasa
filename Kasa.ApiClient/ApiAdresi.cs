namespace Kasa.ApiClient;

public static class ApiAdresi
{
    public const string Varsayilan = "https://kasa.emarglobal.com/";

    /// <summary>Yerel geliştirme için KASA_API_URL ile açıkça değiştirilebilir.</summary>
    public static Uri Coz(string? adres)
    {
        if (string.IsNullOrWhiteSpace(adres)) return new Uri(Varsayilan);
        if (!Uri.TryCreate(adres.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("KASA_API_URL HTTPS adresi veya yerel HTTP adresi olmalıdır.", nameof(adres));
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
}
