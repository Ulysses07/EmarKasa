using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;

namespace Kasa.Api;

/// <summary>
/// Paket E uçlarının ortak yardımcıları (Program.cs'teki yerel yardımcılar başka dosyadan
/// görülemediği için aynı davranışla burada).
/// </summary>
internal static class GuvenlikUcYardimci
{
    public const string CerezAdi = "kasa_auth";

    public static IResult HataYaniti(string mesaj) => Results.BadRequest(new { hata = mesaj });

    /// <summary>Program.cs'teki Yaz ile aynı: tek transaction, kısıt ihlalinde 409.</summary>
    public static IResult YazIslem(KasaDbContext db, string cakismaMesaji, Func<IResult> islem)
    {
        try
        {
            using var tx = db.Database.BeginTransaction();
            var sonuc = islem();
            tx.Commit();
            return sonuc;
        }
        catch (Exception ex) when (KisitIhlaliMi(ex))
        {
            return Results.Conflict(new { hata = cakismaMesaji });
        }
    }

    public static bool KisitIhlaliMi(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is SqliteException { SqliteErrorCode: 19 }) return true; // SQLITE_CONSTRAINT
        return false;
    }

    public static bool SabitZamanEsitMi(string? a, string b)
        => CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a ?? "")), SHA256.HashData(Encoding.UTF8.GetBytes(b)));

    public static string? SifreHatasi(string? sifre)
    {
        if (string.IsNullOrWhiteSpace(sifre)) return "Şifre boş olamaz.";
        if (sifre.Length < GuvenlikKurallari.SifreEnAz) return $"Şifre en az {GuvenlikKurallari.SifreEnAz} karakter olmalı.";
        if (sifre.Length > GuvenlikKurallari.SifreEnCok) return $"Şifre en fazla {GuvenlikKurallari.SifreEnCok} karakter olabilir.";
        return null;
    }

    /// <summary>Hesabın şifresi: kayıtlı hash, yerleşik editörde hash yoksa sunucu ayarındaki şifre.</summary>
    public static bool SifreDogruMu(KullaniciEntity k, string? sifre, IConfiguration cfg)
    {
        if (k.SifreHash is string h) return SifreHasher.Dogrula(sifre ?? "", h);
        return k.Yerlesik && cfg["Kasa:EditorSifre"] is { Length: > 0 } env && SabitZamanEsitMi(sifre, env);
    }

    public static void CerezYaz(HttpContext http, string token, DateTime bitisUtc, bool secure)
        => http.Response.Cookies.Append(CerezAdi, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = secure,
            MaxAge = bitisUtc - DateTime.UtcNow,
        });

    public static string? Jti(ClaimsPrincipal u) => u.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti);

    public static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
}
