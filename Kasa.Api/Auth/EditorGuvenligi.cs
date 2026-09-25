using System.Security.Cryptography;
using System.Text;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Auth;

public static class EditorGuvenligi
{
    public static bool Dogrula(string? sifre, IConfiguration cfg, EditorGuvenlikEntity? kayit)
    {
        if (string.IsNullOrEmpty(sifre) || sifre.Length > 1024) return false;
        if (kayit?.SifreHash is { } hash) return SifreHasher.Dogrula(sifre, hash);
        var eski = cfg["Kasa:EditorSifre"];
        return !string.IsNullOrEmpty(eski) && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(sifre)), SHA256.HashData(Encoding.UTF8.GetBytes(eski)));
    }

    public static string Kaynak(IConfiguration cfg, EditorGuvenlikEntity? kayit) =>
        kayit?.SifreHash is { } hash
            ? $"editor\n{cfg["Kasa:EditorKullanici"]}\n{hash}\n{kayit.Surum}"
            : $"editor\n{cfg["Kasa:EditorKullanici"]}\n{cfg["Kasa:EditorSifre"]}";

    public static WebApplication MapGuvenlikEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/sifre", (SifreDegistir dto, KasaDbContext db, IConfiguration cfg, HttpContext http) =>
        {
            if (YeniSifreHatasi(dto.YeniSifre) is { } hata) return hata;
            using var tx = db.Database.BeginTransaction();
            var kayit = db.EditorGuvenlik.SingleOrDefault(e => e.Id == 1);
            if (!Dogrula(dto.MevcutSifre, cfg, kayit)) return Results.Unauthorized();
            kayit = KayitOlustur(db, kayit);
            kayit.SifreHash = SifreHasher.Hashle(dto.YeniSifre);
            kayit.KurtarmaHash = null;
            kayit.Surum++;
            db.SaveChanges(); tx.Commit();
            http.Response.Cookies.Delete("kasa_auth");
            return Results.NoContent();
        }).RequireAuthorization("Editor").RequireRateLimiting("guvenlik");

        app.MapPost("/api/auth/kurtarma-kodu", (KurtarmaOlustur dto, KasaDbContext db, IConfiguration cfg) =>
        {
            using var tx = db.Database.BeginTransaction();
            var kayit = db.EditorGuvenlik.SingleOrDefault(e => e.Id == 1);
            if (!Dogrula(dto.MevcutSifre, cfg, kayit)) return Results.Unauthorized();
            kayit = KayitOlustur(db, kayit);
            var kod = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            kayit.KurtarmaHash = KodHash(kod);
            db.SaveChanges(); tx.Commit();
            return Results.Ok(new { kod });
        }).RequireAuthorization("Editor").RequireRateLimiting("guvenlik");

        app.MapPost("/api/auth/kurtar", (SifreKurtar dto, KasaDbContext db, IConfiguration cfg, HttpContext http) =>
        {
            if (YeniSifreHatasi(dto.YeniSifre) is { } hata) return hata;
            if (dto.Kod is null || dto.Kod.Length > 200) return Results.Unauthorized();
            using var tx = db.Database.BeginTransaction();
            var kayit = db.EditorGuvenlik.SingleOrDefault(e => e.Id == 1);
            var beklenen = kayit?.KurtarmaHash;
            if (dto.Kullanici != cfg["Kasa:EditorKullanici"] || beklenen is null
                || !OturumDamgasi.Esit(KodHash(dto.Kod), beklenen)) return Results.Unauthorized();
            kayit!.SifreHash = SifreHasher.Hashle(dto.YeniSifre);
            kayit.KurtarmaHash = null;
            kayit.Surum++;
            db.SaveChanges(); tx.Commit();
            http.Response.Cookies.Delete("kasa_auth");
            return Results.NoContent();
        }).RequireRateLimiting("guvenlik");
        return app;
    }

    private static EditorGuvenlikEntity KayitOlustur(KasaDbContext db, EditorGuvenlikEntity? kayit)
    {
        if (kayit is not null) return kayit;
        kayit = new EditorGuvenlikEntity(); db.EditorGuvenlik.Add(kayit); return kayit;
    }
    private static string KodHash(string kod) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kod.Trim().ToUpperInvariant())));
    private static IResult? YeniSifreHatasi(string? sifre) => string.IsNullOrWhiteSpace(sifre) || sifre.Length is < 12 or > 1024
        ? Results.ValidationProblem(new Dictionary<string, string[]> { ["yeniSifre"] = ["Yeni şifre 12–1024 karakter olmalıdır."] }) : null;
}

public record SifreDegistir(string MevcutSifre, string YeniSifre);
public record KurtarmaOlustur(string MevcutSifre);
public record SifreKurtar(string Kullanici, string Kod, string YeniSifre);
