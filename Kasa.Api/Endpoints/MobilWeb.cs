using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace Kasa.Api.Endpoints;

/// <summary>
/// Telefon için salt okunur web uygulaması (PWA): <c>/m</c> altında <c>wwwroot/m</c>'deki statik
/// dosyalar. Statik sunum ve "index.html'e düş" davranışı YALNIZ <c>/m</c> içindir: <c>/</c> ve
/// başka yollar eskisi gibi 404 döner. Veri yalnız oturumla (kasa_auth çerezi) <c>/api</c>'den gelir;
/// kabuk dosyaları veri içermez.
/// <para>
/// Ayrıca <c>/api</c>'ye gelen durum değiştiren (POST/PUT/DELETE…) tarayıcı isteklerinde aynı
/// kaynak koşulu aranır (CSRF'e karşı çerezin SameSite=Strict'ine ek savunma): <c>Sec-Fetch-Site</c>
/// başka site diyorsa ya da (başlık yoksa) <c>Origin</c> sunucunun adresi değilse 403. Masaüstü
/// uygulaması (HttpClient) bu başlıkları göndermez, etkilenmez.
/// </para>
/// </summary>
public static class MobilWeb
{
    public const string Yol = "/m";

    /// <summary>/m'nin kendi sıkı CSP'si: yalnız kendi script/stil dosyaları, satır içi script yok, veri yalnız aynı kaynaktan.</summary>
    public const string Csp = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
                              "connect-src 'self'; manifest-src 'self'; worker-src 'self'; font-src 'self'; " +
                              "base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    public static WebApplication UseMobilWeb(this WebApplication app)
    {
        app.Use(AyniKaynakKorumasi);

        var klasor = KlasorBul(app.Environment.ContentRootPath);
        if (klasor is null)
        {
            app.Logger.LogWarning("wwwroot/m bulunamadı; telefon uygulaması (/m) kapalı.");
            return app;
        }

        var dosyalar = new PhysicalFileProvider(klasor);
        var turler = new FileExtensionContentTypeProvider();
        turler.Mappings[".webmanifest"] = "application/manifest+json";

        ((IApplicationBuilder)app).Map(new PathString(Yol), m =>
        {
            m.Use((ctx, next) =>
            {
                var h = ctx.Response.Headers;
                h[HeaderNames.ContentSecurityPolicy] = Csp;
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                // Kabuk küçük: her açılışta yeniden doğrulansın (ETag), güncelleme hemen gelsin.
                h[HeaderNames.CacheControl] = "no-cache";
                // "/m" → "/m/" (göreli dosya yolları doğru çözülsün).
                if (!ctx.Request.Path.HasValue)
                {
                    ctx.Response.Redirect(ctx.Request.PathBase + "/" + ctx.Request.QueryString);
                    return Task.CompletedTask;
                }
                return next();
            });
            m.UseDefaultFiles(new DefaultFilesOptions { FileProvider = dosyalar });
            m.UseStaticFiles(new StaticFileOptions { FileProvider = dosyalar, ContentTypeProvider = turler });
            // Uzantısız bilinmeyen yol (ör. /m/panel) → index.html; uzantılı bilinmeyen dosya → 404.
            m.Run(async ctx =>
            {
                var yol = ctx.Request.Path.Value ?? "";
                if ((HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
                    && !Path.HasExtension(yol) && dosyalar.GetFileInfo("index.html") is { Exists: true } index)
                {
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    if (HttpMethods.IsGet(ctx.Request.Method)) await ctx.Response.SendFileAsync(index);
                    return;
                }
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            });
        });
        return app;
    }

    /// <summary>wwwroot/m: içerik kökünde (geliştirme, testler, Docker /app) ya da uygulama klasöründe.</summary>
    public static string? KlasorBul(string icerikKoku)
    {
        foreach (var kok in new[] { icerikKoku, AppContext.BaseDirectory })
        {
            var k = Path.Combine(kok, "wwwroot", "m");
            if (File.Exists(Path.Combine(k, "index.html"))) return k;
        }
        return null;
    }

    private static Task AyniKaynakKorumasi(HttpContext ctx, Func<Task> next)
    {
        var r = ctx.Request;
        if (r.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(r.Method) && !HttpMethods.IsHead(r.Method)
            && !HttpMethods.IsOptions(r.Method) && BaskaKaynak(r))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return ctx.Response.WriteAsJsonAsync(new { hata = "Başka bir siteden gelen istek reddedildi." });
        }
        return next();
    }

    /// <summary>İstek başka bir siteden mi geliyor (tarayıcı başlıklarına göre).</summary>
    public static bool BaskaKaynak(HttpRequest r)
    {
        var site = r.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0) return site is "cross-site" or "same-site";
        var origin = r.Headers.Origin.ToString();
        if (origin.Length == 0) return false;   // tarayıcı dışı istemci (masaüstü uygulaması, curl)
        return !Uri.TryCreate(origin, UriKind.Absolute, out var u)
               || !string.Equals(u.Authority, r.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
