namespace Kasa.Api;

/// <summary>Eski masaüstü istemcilerinin yeni API'de sürümsüz yazmasını önler.
/// Tarayıcı çerez kullanır; masaüstü Bearer gönderir. Bu bir kimlik doğrulama sınırı değildir:
/// amaç dağıtılmış eski uygulamanın yanlışlıkla yeni kayıtları değiştirmesini durdurmaktır.</summary>
internal static class IstemciSurumKapisi
{
    internal const string BaslikAdi = "X-Kasa-Istemci-Surumu";
    internal const string Ileti = "Bu uygulama sürümü artık desteklenmiyor. Kayıtları değiştirmek için uygulamayı güncelleyin.";

    internal static bool YazmaEngellenmeli(HttpContext http)
    {
        var istek = http.Request;
        if (!istek.Path.StartsWithSegments("/api") || HttpMethods.IsGet(istek.Method) || HttpMethods.IsHead(istek.Method)
            || HttpMethods.IsOptions(istek.Method) || istek.Path == "/api/auth/logout"
            || http.User.Identity?.IsAuthenticated != true
            || !istek.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var cfg = http.RequestServices.GetRequiredService<IConfiguration>();
        var minimum = Version.Parse(IstemciYayinAyarlari.Minimum(cfg, istek.Headers[IstemciYayinAyarlari.PlatformBasligi].ToString()));
        return !Version.TryParse(istek.Headers[BaslikAdi].ToString(), out var surum) || surum < minimum;
    }
}
