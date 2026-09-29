using System.Security.Cryptography;
using System.Text;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Auth;

/// <summary>Şifre değişince eski oturumları geçersiz kılar; şifre/hash JWT'ye yazılmaz.
/// <para>Damga veri soyunun oturum dönemini de taşır (<see cref="SistemDurumuEntity.OturumDonemi"/>,
/// gap-geri-yukleme-durum-geri-sarma-1): geri yükleme yeni dönem açar, yedekten önce ya da sonra alınmış hiçbir oturum,
/// tanıdık cihaz belirteci ve bildirim aboneliği damgası yeni soyda eşleşmez; veritabanındaki şifre ve sürümler yedek anına
/// dönse de. Dönem boşken (geri yükleme hiç olmadıysa) damga bu sürümden öncekiyle birebir aynıdır.</para></summary>
public static class OturumDamgasi
{
    public const string ClaimAdi = "kasa_session";

    public static string? Uret(string? rol, IConfiguration cfg, KasaDbContext db, int? aliciId = null)
    {
        if (rol == "alici")
        {
            var alici = db.Alicilar.AsNoTracking().FirstOrDefault(a => a.Id == aliciId && a.Aktif);
            return alici is null ? null : AliciIcin(alici, cfg, db);
        }
        string? kaynak = rol switch
        {
            "editor" when !string.IsNullOrEmpty(cfg["Kasa:EditorSifre"]) =>
                EditorGuvenligi.Kaynak(cfg, db.EditorGuvenlik.AsNoTracking().SingleOrDefault(e => e.Id == 1)),
            "viewer" => db.Ayarlar.AsNoTracking().Select(a => a.IzleyiciSifreHash).FirstOrDefault(),
            _ => null
        };
        if (kaynak is null) return null;
        return Imzala(kaynak, cfg, Donem(db));
    }

    // Login, şifresini gerçekten doğruladığı kaydın damgasını kullanır. Arada yapılan
    // şifre değişikliği eski şifreyle yeni oturum açılmasını sağlamaz.
    public static string AliciIcin(AliciEntity a, IConfiguration cfg, KasaDbContext db)
        => Imzala($"alici\n{a.Id}\n{a.Kullanici}\n{a.SifreHash}\n{a.OturumSurumu}", cfg, Donem(db));

    public static string IzleyiciIcin(string dogrulanmisHash, IConfiguration cfg, KasaDbContext db) => Imzala(dogrulanmisHash, cfg, Donem(db));

    public static string EditorIcin(EditorGuvenlikEntity? kayit, IConfiguration cfg, KasaDbContext db) => Imzala(EditorGuvenligi.Kaynak(cfg, kayit), cfg, Donem(db));

    /// <summary>Veri soyunun oturum dönemi; satır yoksa (ör. EnsureCreated ile kurulmuş test veritabanı) boş.</summary>
    public static string Donem(KasaDbContext db)
        => db.SistemDurumu.AsNoTracking().Where(s => s.Id == 1).Select(s => s.OturumDonemi).FirstOrDefault() ?? "";

    /// <summary>Boş dönemde girdi yalnız kaynaktır (bu sürümden önceki damga); dolu dönemde dönem kaynağın önüne eklenir.</summary>
    internal static string Imzala(string kaynak, IConfiguration cfg, string donem)
        => Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(cfg["Kasa:JwtKey"]!), Encoding.UTF8.GetBytes(donem.Length == 0 ? kaynak : $"donem\n{donem}\n{kaynak}")));

    public static bool Esit(string? gelen, string? beklenen)
        => gelen is not null && beklenen is not null
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(gelen), Encoding.UTF8.GetBytes(beklenen));
}
