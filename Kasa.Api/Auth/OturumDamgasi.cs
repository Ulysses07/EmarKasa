using System.Security.Cryptography;
using System.Text;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Auth;

/// <summary>Şifre değişince eski oturumları geçersiz kılar; şifre/hash JWT'ye yazılmaz.</summary>
public static class OturumDamgasi
{
    public const string ClaimAdi = "kasa_session";

    public static string? Uret(string? rol, IConfiguration cfg, KasaDbContext db, int? aliciId = null)
    {
        if (rol == "alici")
        {
            var alici = db.Alicilar.AsNoTracking().FirstOrDefault(a => a.Id == aliciId && a.Aktif);
            return alici is null ? null : AliciIcin(alici, cfg);
        }
        string? kaynak = rol switch
        {
            "editor" when !string.IsNullOrEmpty(cfg["Kasa:EditorSifre"]) =>
                EditorGuvenligi.Kaynak(cfg, db.EditorGuvenlik.AsNoTracking().SingleOrDefault(e => e.Id == 1)),
            "viewer" => db.Ayarlar.AsNoTracking().Select(a => a.IzleyiciSifreHash).FirstOrDefault(),
            _ => null
        };
        if (kaynak is null) return null;
        return Imzala(kaynak, cfg);
    }

    // Login, şifresini gerçekten doğruladığı kaydın damgasını kullanır. Arada yapılan
    // şifre değişikliği eski şifreyle yeni oturum açılmasını sağlamaz.
    public static string AliciIcin(AliciEntity a, IConfiguration cfg)
        => Imzala($"alici\n{a.Id}\n{a.Kullanici}\n{a.SifreHash}\n{a.OturumSurumu}", cfg);

    public static string IzleyiciIcin(string dogrulanmisHash, IConfiguration cfg) => Imzala(dogrulanmisHash, cfg);

    public static string EditorIcin(EditorGuvenlikEntity? kayit, IConfiguration cfg) => Imzala(EditorGuvenligi.Kaynak(cfg, kayit), cfg);

    private static string Imzala(string kaynak, IConfiguration cfg)
        => Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(cfg["Kasa:JwtKey"]!), Encoding.UTF8.GetBytes(kaynak)));

    public static bool Esit(string? gelen, string? beklenen)
        => gelen is not null && beklenen is not null
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(gelen), Encoding.UTF8.GetBytes(beklenen));
}
