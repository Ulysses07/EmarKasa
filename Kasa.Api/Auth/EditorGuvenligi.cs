using System.Security.Cryptography;
using System.Text;
using Kasa.Api.Data;

namespace Kasa.Api.Auth;

public static class EditorGuvenligi
{
    /// <summary>
    /// Editör girişi kilitli (<see cref="EditorGuvenlikEntity.SifreHash"/> bu değerdeyken): geri yükleme güvenlik günlüğünde yedekten
    /// sonraki bir şifre değişikliği, kurtarma ya da sıfırlama bulunca yedekteki şifreyi bununla geçersiz kılar
    /// (<see cref="GeriYuklemeIsleyici"/>). Hiçbir şifre doğrulanmaz: ne yedekteki ya da sonradan kaybolan şifre ne de ortamdaki
    /// Kasa:EditorSifre (ilk kurulumdaki ya da unutulmuş eski bir değer olabilir). Kurtarma kodu geri yüklemede iptal edildiği için
    /// kilidi yalnız operatörün bilinçli sıfırlaması açar (<see cref="EditorSifreSifirlama"/>, yeni ortam şifresiyle). Değer
    /// <see cref="SifreHasher"/> biçiminde ("tuz.özet") değildir: bu sürümü tanımayan kod da onu doğrulayamaz (kilit her sürümde kapalı kalır).
    /// </summary>
    public const string GirisKilidi = "kilitli:geri-yukleme";

    public static bool Kilitli(EditorGuvenlikEntity? kayit) => kayit?.SifreHash == GirisKilidi;

    public static bool Dogrula(string? sifre, IConfiguration cfg, EditorGuvenlikEntity? kayit)
    {
        if (string.IsNullOrEmpty(sifre) || sifre.Length > 1024)
            return false;
        if (kayit?.SifreHash is { } hash)
            return hash != GirisKilidi && SifreHasher.Dogrula(sifre, hash);
        var eski = cfg["Kasa:EditorSifre"];
        return !string.IsNullOrEmpty(eski) && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(sifre)), SHA256.HashData(Encoding.UTF8.GetBytes(eski)));
    }

    /// <summary>Editör oturum damgasının kaynağı. Ortam şifresiyle çalışan editörün kaydı yalnız kurtarma kodu üretiminde (sürüm 0)
    /// ya da geri yüklemede (<see cref="GeriYuklemeIsleyici"/>, sürüm artar) oluşur; sürüm 0'dan büyükse damgaya girer. Böylece
    /// geri yükleme ortam şifresindeki editörün eski oturumlarını da kapatır; bugünkü oturumlar bu değişiklikle düşmez. Geri yükleme
    /// güvenlik günlüğünde yedekten sonraki bir şifre değişikliği bulursa girişi kilitler (<see cref="GirisKilidi"/>); operatörün
    /// sıfırlaması (<see cref="EditorSifreSifirlama"/>) şifre özetini ortam şifresinin özetiyle değiştirir ve sürümü artırır.</summary>
    public static string Kaynak(IConfiguration cfg, EditorGuvenlikEntity? kayit) =>
        kayit?.SifreHash is { } hash
            ? $"editor\n{cfg["Kasa:EditorKullanici"]}\n{hash}\n{kayit.Surum}"
            : kayit is { Surum: > 0 }
                ? $"editor\n{cfg["Kasa:EditorKullanici"]}\n{cfg["Kasa:EditorSifre"]}\n{kayit.Surum}"
                : $"editor\n{cfg["Kasa:EditorKullanici"]}\n{cfg["Kasa:EditorSifre"]}";

    public static WebApplication MapGuvenlikEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/sifre", (SifreDegistir dto, KasaDbContext db, IConfiguration cfg, HttpContext http, TanidikCihaz tanidikCihaz, GuvenlikGunlugu gunluk) =>
        {
            if (YeniSifreHatasi(dto.YeniSifre) is { } hata)
                return hata;
            using var tx = db.Database.BeginTransaction();
            var kayit = db.EditorGuvenlik.SingleOrDefault(e => e.Id == 1);
            if (!Dogrula(dto.MevcutSifre, cfg, kayit))
            {
                // Başarısız deneme de kalıcı güvenlik olayıdır: yalnız olay yazılmış transaction kaydedilir.
                GuvenlikOlaylari.Yaz(http, db, GuvenlikOlaylari.SifreDegistirmeBasarisiz, cfg["Kasa:EditorKullanici"], varlikId: "1");
                tx.Commit();
                return MevcutSifreHatali();
            }
            kayit = KayitOlustur(db, kayit);
            kayit.SifreHash = SifreHasher.Hashle(dto.YeniSifre);
            kayit.KurtarmaHash = null;
            kayit.Surum++;
            db.SaveChanges();
            // Değişiklikle aynı transaction'da: olay yazılamazsa hata fırlar, commit edilmez ve şifre değişmez (zorunlu). Böylece
            // şifre değişti ise olay da vardır. Eski oturumlar ve kurtarma kodu düşer.
            GuvenlikOlaylari.Yaz(http, db, GuvenlikOlaylari.SifreDegisti, cfg["Kasa:EditorKullanici"], new { oturumlarKapatildi = true, kurtarmaKoduGecersiz = true }, varlikId: "1", zorunlu: true);
            tx.Commit();
            // Veritabanı dışındaki iz: yedekten geri yüklenirse yedekteki eski şifre geçersiz kılınır (GeriYuklemeIsleyici).
            gunluk.Yaz(GuvenlikGunlugu.EditorSifresiDegisti, kullanici: cfg["Kasa:EditorKullanici"]);
            http.Response.Cookies.Delete("kasa_auth");
            // Eski tanıdık cihaz belirteçleri damgayla düşer; işlemi yapan cihaz yenisini alır (saldırı sürerken
            // şifresini değiştiren editör kendi cihazından yeniden girebilir).
            tanidikCihaz.GovdesizVer(http, GirisSiniri.EditorHedefi, OturumDamgasi.EditorIcin(kayit, cfg, db));
            return Results.NoContent();
        }).RequireAuthorization("Editor").RequireRateLimiting(HizSinirlari.Guvenlik);

        app.MapPost("/api/auth/kurtarma-kodu", (KurtarmaOlustur dto, KasaDbContext db, IConfiguration cfg, HttpContext http, GuvenlikGunlugu gunluk) =>
        {
            using var tx = db.Database.BeginTransaction();
            var kayit = db.EditorGuvenlik.SingleOrDefault(e => e.Id == 1);
            if (!Dogrula(dto.MevcutSifre, cfg, kayit))
            {
                GuvenlikOlaylari.Yaz(http, db, GuvenlikOlaylari.KurtarmaKoduUretimiBasarisiz, cfg["Kasa:EditorKullanici"], varlikId: "1");
                tx.Commit();
                return MevcutSifreHatali();
            }
            kayit = KayitOlustur(db, kayit);
            var kod = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            kayit.KurtarmaHash = KodHash(kod);
            db.SaveChanges();
            // Kod yalnız yanıtta bir kez döner; olaya kod da özeti de yazılmaz. Olay yazılamazsa yeni kod kaydedilmez (zorunlu).
            GuvenlikOlaylari.Yaz(http, db, GuvenlikOlaylari.KurtarmaKoduUretildi, cfg["Kasa:EditorKullanici"], new { oncekiKodGecersiz = true }, varlikId: "1", zorunlu: true);
            tx.Commit();
            gunluk.Yaz(GuvenlikGunlugu.KurtarmaKoduUretildi, kullanici: cfg["Kasa:EditorKullanici"]);
            return Results.Ok(new { kod });
        }).RequireAuthorization("Editor").RequireRateLimiting(HizSinirlari.Guvenlik);

        app.MapPost("/api/auth/kurtar", (SifreKurtar dto, KasaDbContext db, IConfiguration cfg, HttpContext http, TanidikCihaz tanidikCihaz, GuvenlikGunlugu gunluk) =>
        {
            if (YeniSifreHatasi(dto.YeniSifre) is { } hata)
                return hata;
            if (dto.Kod is null || dto.Kod.Length > 200)
                return KurtarmaHatali();
            using var tx = db.Database.BeginTransaction();
            var kayit = db.EditorGuvenlik.SingleOrDefault(e => e.Id == 1);
            var beklenen = kayit?.KurtarmaHash;
            if (dto.Kullanici != cfg["Kasa:EditorKullanici"] || beklenen is null
                || !OturumDamgasi.Esit(KodHash(dto.Kod), beklenen))
                return KurtarmaHatali();
            kayit!.SifreHash = SifreHasher.Hashle(dto.YeniSifre);
            kayit.KurtarmaHash = null;
            kayit.Surum++;
            db.SaveChanges();
            // Başarılı kurtarma değişiklikle aynı transaction'da yazılır (yazılamazsa şifre değişmez, kod geçerli kalır: zorunlu);
            // başarısız deneme ve 429 giriş filtresinde.
            GuvenlikOlaylari.Yaz(http, db, GuvenlikOlaylari.KurtarmaKullanildi, dto.Kullanici, new { oturumlarKapatildi = true }, varlikId: "1", zorunlu: true);
            tx.Commit();
            gunluk.Yaz(GuvenlikGunlugu.KurtarmaKullanildi, kullanici: dto.Kullanici);
            http.Response.Cookies.Delete("kasa_auth");
            // Kurtarma kodu editör şifresi kadar güçlü bir kanıttır: kurtaran cihaz tanıdık cihaz olur.
            tanidikCihaz.GovdesizVer(http, GirisSiniri.EditorHedefi, OturumDamgasi.EditorIcin(kayit, cfg, db));
            return Results.NoContent();
        }).GirisSiniriUygula<SifreKurtar>(d => d.Kullanici, kurtarma: true);
        return app;
    }

    private static EditorGuvenlikEntity KayitOlustur(KasaDbContext db, EditorGuvenlikEntity? kayit)
    {
        if (kayit is not null)
            return kayit;
        kayit = new EditorGuvenlikEntity();
        db.EditorGuvenlik.Add(kayit);
        return kayit;
    }
    private static string KodHash(string kod) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kod.Trim().ToUpperInvariant())));
    private static IResult? YeniSifreHatasi(string? sifre) => SifreKurallari.YeniSifreHatasi(sifre, "yeniSifre", "Yeni şifre");
    // Oturum geçerliyken yanlış mevcut şifre bir alan hatasıdır; 401 yalnız gerçek oturum sonu içindir
    // (istemciler 401'de oturumu kapatır).
    private static IResult MevcutSifreHatali() =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["mevcutSifre"] = ["Mevcut şifre hatalı."] });
    private static IResult KurtarmaHatali() =>
        Results.Json(new { hata = "Kullanıcı adı veya kurtarma kodu hatalı." }, statusCode: StatusCodes.Status401Unauthorized);
}

public record SifreDegistir(string MevcutSifre, string YeniSifre);
public record KurtarmaOlustur(string MevcutSifre);
public record SifreKurtar(string Kullanici, string Kod, string YeniSifre);
