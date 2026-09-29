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
    /// <summary>
    /// İsteğe bağlı değişiklik gerekçesi başlığı (yüzde kodlu UTF-8, ör. JS <c>encodeURIComponent</c>, .NET
    /// <c>Uri.EscapeDataString</c>): gövdesinde gerekçe alanı olmayan uçlar da (gider düzenleme ve gövdesiz silme, gelir,
    /// genel kasa açılışı) değişikliğe gerekçe iliştirebilir; başlığı göndermeyen eski istemciler etkilenmez. Ucun kendi
    /// gerekçesi (<see cref="KasaDbContext.Denetle"/>, iptal açıklaması) başlıktan önce gelir. En çok 2000 karakter yazılır.
    /// Yalnız oturumlu editör ya da alıcı isteğinde okunur ve güvenlik olaylarına hiç yazılmaz: kimliksiz giriş/kurtarma
    /// isteği değiştirilemez izin gerekçe alanına kendi metnini yazamaz.
    /// </summary>
    public const string GerekceBasligi = "X-Kasa-Gerekce";

    /// <summary>Program.cs: denetim aktörünün istekten okunması, güvenlik olayı yazım sınırı ve kimlik doğrulama retlerinin
    /// güvenlik logu (<see cref="GuvenlikOlaylari.AddKasaGuvenlikLoglari"/>).</summary>
    public static IServiceCollection AddKasaDenetim(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<GuvenlikOlayiSiniri>();
        services.AddKasaGuvenlikLoglari();
        return services;
    }

    /// <summary>İsteğin <see cref="GerekceBasligi"/> başlığındaki gerekçe (çözülmüş, kırpılmış); yoksa ya da boşsa null.
    /// Geçersiz yüzde kodu olduğu gibi kalır.</summary>
    internal static string? IstekGerekcesi(HttpContext? http)
    {
        if (http is null || !http.Request.Headers.TryGetValue(GerekceBasligi, out var deger))
            return null;
        var metin = Uri.UnescapeDataString(deger.ToString()).Trim();
        return metin.Length == 0 ? null : metin;
    }

    /// <summary>Bağlamı kullanan geçerli istek; istek dışında null.</summary>
    internal static HttpContext? Istek(KasaDbContext db) =>
        db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?.GetService<IHttpContextAccessor>()?.HttpContext;

    public static DenetimAktoru Aktor(HttpContext? http)
    {
        if (http is null)
            return DenetimAktoru.Sistem;
        var user = http.User;
        var rol = user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.Role) ?? "anonim" : "anonim";
        int? id = int.TryParse(user.FindFirstValue("alici_id"), out var aliciId) && aliciId > 0 ? aliciId : null;
        return new(rol, id, Ip(http), Iz(http));
    }

    internal static string? Ip(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is null)
            return null;
        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }

    // Loglardaki iz: W3C Activity varsa onun TraceId'si, yoksa isteğin kimliği.
    private static string Iz(HttpContext http) =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } a ? a.TraceId.ToString() : http.TraceIdentifier;
}
