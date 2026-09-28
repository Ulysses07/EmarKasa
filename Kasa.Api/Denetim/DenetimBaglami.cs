using System.Diagnostics;
using System.Security.Claims;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kasa.Api.Denetim;

/// <summary>Olayı yapan: rol, kişisel hesap kimliği (alıcı), gerçek istemci IP'si ve isteği loglara bağlayan iz.</summary>
public sealed record DenetimAktoru(string Rol, int? Id, string? Ip, string? TraceId)
{
    /// <summary>İstek dışı yazma: açılış tohumu, bakım adımı, bildirim işçisi, doğrudan servis çağrısı.</summary>
    public static readonly DenetimAktoru Sistem = new("sistem", null, null, null);
}

/// <summary>
/// Denetim olayının aktörü isteğin kimliğinden okunur (HttpContext; ForwardedHeaders sonrası bağlantı adresi gerçek istemci
/// IP'sidir). Editör hesabı paylaşılan tek hesaptır: aktör rolü 'editor' olur, kişi ayrımı yoktur; alıcıda kimlik de
/// yazılır. İstek yoksa aktör 'sistem'dir. Bağlam uygulama servislerinden (IHttpContextAccessor) okunur; uygulama
/// servisi olmayan bağlamda (doğrudan kurulan test bağlamı) da 'sistem'e düşer.
/// </summary>
public static class DenetimBaglami
{
    /// <summary>Program.cs: denetim aktörünün istekten okunması ve güvenlik olayı yazım sınırı.</summary>
    public static IServiceCollection AddKasaDenetim(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<GuvenlikOlayiSiniri>();
        return services;
    }

    /// <summary>Bağlamı kullanan geçerli istek; istek dışında null.</summary>
    internal static HttpContext? Istek(KasaDbContext db) =>
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?.GetService<IHttpContextAccessor>()?.HttpContext;

    public static DenetimAktoru Aktor(HttpContext? http)
    {
        if (http is null) return DenetimAktoru.Sistem;
        var user = http.User;
        var rol = user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.Role) ?? "anonim" : "anonim";
        int? id = int.TryParse(user.FindFirstValue("alici_id"), out var aliciId) && aliciId > 0 ? aliciId : null;
        return new(rol, id, Ip(http), Iz(http));
    }

    internal static string? Ip(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is null) return null;
        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    // Loglardaki iz: W3C Activity varsa onun TraceId'si, yoksa isteğin kimliği.
    private static string Iz(HttpContext http) =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } a ? a.TraceId.ToString() : http.TraceIdentifier;
}
