using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.GuvenlikUcYardimci;

namespace Kasa.Api;

/// <summary>
/// Giriş/çıkış, kişisel hesaplar (her ortağa ayrı giriş), kendi hesabım (şifre, iki adımlı giriş),
/// açık oturumlar, giriş günlüğü ve güvenlik ayarı. Okuma ve yazma uçlarının hepsi editöre özeldir
/// (giriş/çıkış/ben hariç).
/// </summary>
public static partial class KimlikEndpoints
{
    [GeneratedRegex(@"^[\p{L}\p{N}._-]{3,50}$")]
    private static partial Regex KullaniciAdiKalibi();

    public static WebApplication MapKimlikUclari(this WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            KimlikTohumu.Hazirla(db, app.Configuration, app.Logger, scope.ServiceProvider.GetRequiredService<TimeProvider>());
            scope.ServiceProvider.GetRequiredService<KullaniciOnbellegi>().Yukle(db);
        }

        var jwtKey = app.Configuration["Kasa:JwtKey"]!;
        // Üretimde (Caddy TLS arkasında) çerez yalnızca HTTPS'te gitmeli.
        var cerezSecure = !app.Environment.IsDevelopment();

        // --- Auth ---
        app.MapPost("/api/auth/login", (LoginDto dto, HttpContext http, KasaDbContext db, IConfiguration cfg, OturumOnbellegi oturum,
                KullaniciOnbellegi kullanicilar, OturumIzleyici izleyici, TimeProvider saat) =>
            GirisIslemi.Calistir(dto, http, db, cfg, oturum, kullanicilar, izleyici, saat, jwtKey, cerezSecure))
            .RequireRateLimiting("giris");

        // Çıkış: sunulan token'ı (çerez ya da Bearer) iptal eder; token süresi dolana kadar reddedilir.
        app.MapPost("/api/auth/logout", (HttpContext http, KasaDbContext db, OturumOnbellegi oturum, OturumIzleyici izleyici, TimeProvider saat) =>
        {
            var u = http.User;
            var jti = Jti(u);
            if (u.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(jti))
            {
                var bitis = long.TryParse(u.FindFirstValue(JwtRegisteredClaimNames.Exp), out var exp)
                    ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime
                    : DateTime.UtcNow.Add(JwtYardimci.Omur);
                OturumuKapat(db, oturum, izleyici, jti, bitis, saat.GetUtcNow().UtcDateTime);
            }
            http.Response.Cookies.Delete(CerezAdi);
            return Results.Ok();
        });

        app.MapGet("/api/auth/me", (HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar) =>
            Results.Ok(new BenDto(http.User.FindFirstValue(ClaimTypes.Role), KimlikBilgisi.Ad(http), kullanicilar.HesapId(db, http.User))))
            .RequireAuthorization();

        var editor = app.MapGroup("/api").RequireAuthorization("Editor");

        // --- Kendi hesabım (editör) ---
        editor.MapGet("/hesap", (HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar, IConfiguration cfg) =>
        {
            if (Hesabim(db, kullanicilar, http.User) is not { } k) return HesapYok();
            return Results.Ok(new HesapDto(k.Id, k.AdSoyad, k.KullaniciAdi, k.Rol, k.Yerlesik, k.Yerlesik && k.SifreHash is null,
                k.TotpSir is not null, KurtarmaKodlari.Kalan(k.KurtarmaKodlari), kullanicilar.EditorOturumGun(db)));
        });

        editor.MapPost("/hesap/sifre", (SifreDegistirDto dto, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar,
            OturumOnbellegi oturum, OturumIzleyici izleyici, IConfiguration cfg, TimeProvider saat) =>
        {
            if (Hesabim(db, kullanicilar, http.User) is not { } k) return HesapYok();
            // Yanlış mevcut şifre 400 (401 değil): istemci 401'de oturumu siler.
            if (!SifreDogruMu(k, dto.MevcutSifre, cfg)) return HataYaniti("Mevcut şifre hatalı.");
            if (SifreHatasi(dto.YeniSifre) is string h) return HataYaniti(h);
            if (dto.YeniSifre == dto.MevcutSifre) return HataYaniti("Yeni şifre eskisiyle aynı olamaz.");
            k.SifreHash = SifreHasher.Hashle(dto.YeniSifre!);
            k.OturumSurumu++;   // diğer cihazlardaki oturumlar kapanır; bu cihaza yeni token
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.Ok(new YeniTokenDto(TokenYenile(http, db, k, kullanicilar, oturum, izleyici, saat, jwtKey, cerezSecure)));
        }).RequireRateLimiting("giris");

        editor.MapPost("/hesap/iki-adim/baslat", (HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar,
            IkiAdimKurulumlari kurulumlar, TimeProvider saat) =>
        {
            if (Hesabim(db, kullanicilar, http.User) is not { } k) return HesapYok();
            if (k.TotpSir is not null) return Results.Conflict(new { hata = "İki adımlı giriş zaten açık." });
            var sir = kurulumlar.Baslat(k.Id, saat.GetUtcNow().UtcDateTime);
            return Results.Ok(new IkiAdimBaslatDto(sir, Totp.Adres(sir, k.KullaniciAdi)));
        });

        // Açmak şifre ister: yalnız token'ı ele geçiren (ör. kaybolan laptop) iki adımı kendi telefonuyla
        // açıp sahibini dışarıda bırakamasın (açılış diğer oturumları kapatır).
        editor.MapPost("/hesap/iki-adim/onayla", (KodDto dto, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar,
            IkiAdimKurulumlari kurulumlar, OturumOnbellegi oturum, OturumIzleyici izleyici, IConfiguration cfg, TimeProvider saat) =>
        {
            if (Hesabim(db, kullanicilar, http.User) is not { } k) return HesapYok();
            if (k.TotpSir is not null) return Results.Conflict(new { hata = "İki adımlı giriş zaten açık." });
            if (!SifreDogruMu(k, dto.Sifre, cfg)) return HataYaniti("Şifre hatalı.");
            var simdi = saat.GetUtcNow();
            if (kurulumlar.Bekleyen(k.Id, simdi.UtcDateTime) is not { } sir)
                return HataYaniti("Kurulum süresi doldu; yeniden başlatın.");
            if (Totp.Dogrula(sir, dto.Kod, simdi) is null)
                return HataYaniti("Kod hatalı. Uygulamaya eklediğiniz hesabın güncel kodunu girin.");
            var kodlar = KurtarmaKodlari.Uret();
            k.TotpSir = sir;
            k.KurtarmaKodlari = KurtarmaKodlari.Hashle(kodlar);
            k.OturumSurumu++;   // açılmadan önce alınmış oturumlar kapanır
            db.SaveChanges();
            kurulumlar.Bitir(k.Id);
            kullanicilar.Gecersiz();
            return Results.Ok(new KurtarmaKodlariDto(kodlar, TokenYenile(http, db, k, kullanicilar, oturum, izleyici, saat, jwtKey, cerezSecure)));
        }).RequireRateLimiting("giris");

        editor.MapPost("/hesap/iki-adim/kapat", (IkiAdimKapatDto dto, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar,
            IConfiguration cfg, TimeProvider saat) =>
        {
            if (Hesabim(db, kullanicilar, http.User) is not { } k) return HesapYok();
            if (k.TotpSir is null) return HataYaniti("İki adımlı giriş zaten kapalı.");
            if (!SifreDogruMu(k, dto.Sifre, cfg)) return HataYaniti("Şifre hatalı.");
            if (!KodDogruMu(db, k, dto.Kod, saat, kurtarmaKabul: true))
                return HataYaniti("Kod hatalı.");
            k.TotpSir = null;
            k.KurtarmaKodlari = null;
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.Ok();
        }).RequireRateLimiting("giris");

        editor.MapPost("/hesap/kurtarma-kodlari", (KodDto dto, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar,
            IConfiguration cfg, TimeProvider saat) =>
        {
            if (Hesabim(db, kullanicilar, http.User) is not { } k) return HesapYok();
            if (k.TotpSir is null) return HataYaniti("Önce iki adımlı girişi açın.");
            if (!SifreDogruMu(k, dto.Sifre, cfg)) return HataYaniti("Şifre hatalı.");
            if (!KodDogruMu(db, k, dto.Kod, saat, kurtarmaKabul: false))
                return HataYaniti("Kod hatalı. Uygulamadaki güncel kodu girin.");
            var kodlar = KurtarmaKodlari.Uret();
            k.KurtarmaKodlari = KurtarmaKodlari.Hashle(kodlar);
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.Ok(new KurtarmaKodlariDto(kodlar));
        }).RequireRateLimiting("giris");

        // --- Kullanıcılar (her ortağa ayrı giriş) ---
        editor.MapGet("/kullanicilar", (HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar) =>
        {
            var benId = kullanicilar.HesapId(db, http.User);
            var liste = db.Kullanicilar.AsNoTracking().ToList();
            var acik = GecerliOturumlar(db).Where(o => o.KullaniciId != null).GroupBy(o => o.KullaniciId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
            return liste
                .OrderByDescending(k => k.Yerlesik).ThenByDescending(k => k.Aktif).ThenBy(k => k.AdSoyad, Metin.Sirala)
                .Select(k =>
                {
                    // Kullanıcı sayısı az: kişi başına bir index sorgusu (KullaniciId, ZamanUtc).
                    var son = db.GirisKayitlari.AsNoTracking()
                        .Where(g => g.KullaniciId == k.Id && g.Basarili)
                        .OrderByDescending(g => g.ZamanUtc).ThenByDescending(g => g.Id)
                        .Select(g => new { g.ZamanUtc, g.Cihaz }).FirstOrDefault();
                    return new KullaniciDto(k.Id, k.AdSoyad, k.KullaniciAdi, k.Rol, k.Aktif, k.Yerlesik, k.TotpSir is not null,
                        Utc(k.OlusturmaUtc), son is null ? null : Utc(son.ZamanUtc), son?.Cihaz, acik.GetValueOrDefault(k.Id), k.Id == benId);
                })
                .ToList();
        });

        editor.MapPost("/kullanicilar", (KullaniciEkleDto dto, KasaDbContext db, KullaniciOnbellegi kullanicilar, IConfiguration cfg, TimeProvider saat) =>
            YazIslem(db, "Bu kullanıcı adı zaten var.", () =>
        {
            var ad = dto.AdSoyad?.Trim() ?? "";
            var kullaniciAdi = dto.KullaniciAdi?.Trim() ?? "";
            if (AdHatasi(ad) is string ah) return HataYaniti(ah);
            if (KullaniciAdiHatasi(db, cfg, kullaniciAdi, null) is string kh) return HataYaniti(kh);
            if (!Roller.GecerliMi(dto.Rol)) return HataYaniti("Rol editör (editor) ya da izleyici (viewer) olmalı.");
            if (SifreHatasi(dto.Sifre) is string sh) return HataYaniti(sh);
            var k = new KullaniciEntity
            {
                AdSoyad = ad, KullaniciAdi = kullaniciAdi, Rol = dto.Rol!, Aktif = true,
                SifreHash = SifreHasher.Hashle(dto.Sifre!), OlusturmaUtc = saat.GetUtcNow().UtcDateTime,
            };
            db.Kullanicilar.Add(k);
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.Created($"/api/kullanicilar/{k.Id}", new KullaniciDto(k.Id, k.AdSoyad, k.KullaniciAdi, k.Rol, k.Aktif,
                k.Yerlesik, false, Utc(k.OlusturmaUtc), null, null, 0, false));
        }));

        editor.MapPut("/kullanicilar/{id:int}", (int id, KullaniciGuncelleDto dto, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar) =>
            YazIslem(db, "Kullanıcı kaydedilemedi; tekrar deneyin.", () =>
        {
            var k = db.Kullanicilar.Find(id);
            if (k is null) return Results.NotFound();
            var ad = dto.AdSoyad?.Trim() ?? "";
            if (AdHatasi(ad) is string ah) return HataYaniti(ah);
            if (!Roller.GecerliMi(dto.Rol)) return HataYaniti("Rol editör (editor) ya da izleyici (viewer) olmalı.");
            if (k.Yerlesik && (dto.Rol != Roller.Editor || !dto.Aktif))
                return HataYaniti("Sunucu ayarındaki (yerleşik) editörün rolü değiştirilemez, pasif yapılamaz.");
            var yetkiDegisti = k.Rol != dto.Rol || k.Aktif != dto.Aktif;
            // Kendi rolünü düşüren ya da kendini pasif yapan editör bu cihazda da çıkar, geri dönemez.
            if (yetkiDegisti && k.Id == kullanicilar.HesapId(db, http.User))
                return HataYaniti("Kendi rolünüzü ve aktifliğinizi değiştiremezsiniz; başka bir editörden isteyin.");
            if (yetkiDegisti && k.Rol == Roller.Editor && k.Aktif
                && !db.Kullanicilar.Any(x => x.Id != k.Id && x.Rol == Roller.Editor && x.Aktif))
                return HataYaniti("En az bir aktif editör kalmalı.");
            k.AdSoyad = ad;
            k.Rol = dto.Rol!;
            k.Aktif = dto.Aktif;
            if (yetkiDegisti) k.OturumSurumu++;   // yeni yetkiyle yeniden giriş yapsın
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.Ok();
        }));

        editor.MapPost("/kullanicilar/{id:int}/sifre", (int id, YeniSifreDto dto, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar) =>
        {
            var k = db.Kullanicilar.Find(id);
            if (k is null) return Results.NotFound();
            if (k.Yerlesik) return HataYaniti("Yerleşik editörün şifresi yalnız kendi hesabından (Hesabım) değiştirilir.");
            if (k.Id == kullanicilar.HesapId(db, http.User)) return HataYaniti("Kendi şifrenizi Hesabım bölümünden değiştirin.");
            if (SifreHatasi(dto.YeniSifre) is string sh) return HataYaniti(sh);
            k.SifreHash = SifreHasher.Hashle(dto.YeniSifre!);
            k.OturumSurumu++;
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.Ok();
        });

        editor.MapPost("/kullanicilar/{id:int}/oturumlari-kapat", (int id, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar) =>
        {
            var k = db.Kullanicilar.Find(id);
            if (k is null) return Results.NotFound();
            // Kendi hesabında bu cihaz da çıkardı: diğer cihazlar Oturumlar listesinden tek tek kapatılır.
            if (k.Id == kullanicilar.HesapId(db, http.User))
                return HataYaniti("Kendi oturumlarınızı buradan kapatamazsınız; diğer cihazları Oturumlar listesinden kapatın, bu cihaz için Çıkış yapın.");
            k.OturumSurumu++;
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.Ok();
        });

        editor.MapPost("/kullanicilar/{id:int}/iki-adim-kapat", (int id, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar) =>
        {
            var k = db.Kullanicilar.Find(id);
            if (k is null) return Results.NotFound();
            if (k.Id == kullanicilar.HesapId(db, http.User)) return HataYaniti("Kendi iki adımlı girişinizi Hesabım bölümünden kapatın.");
            if (k.TotpSir is null) return HataYaniti("Bu kullanıcıda iki adımlı giriş kapalı.");
            k.TotpSir = null;
            k.KurtarmaKodlari = null;
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.Ok();
        });

        editor.MapDelete("/kullanicilar/{id:int}", (int id, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar) =>
        {
            var k = db.Kullanicilar.Find(id);
            if (k is null) return Results.NotFound();
            if (k.Yerlesik) return HataYaniti("Yerleşik editör silinemez.");
            if (k.Id == kullanicilar.HesapId(db, http.User)) return HataYaniti("Kendi hesabınızı silemezsiniz.");
            db.Kullanicilar.Remove(k);
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.NoContent();
        });

        // Ortak izleyici şifresini kaldırır (herkes kişisel hesapla girsin); ortak şifreyle açılmış oturumlar kapanır.
        editor.MapDelete("/ayarlar/izleyici-sifre", (KasaDbContext db, OturumOnbellegi oturum) =>
        {
            var a = db.Ayarlar.OrderBy(x => x.Id).First();
            if (a.IzleyiciSifreHash is null) return Results.Ok();
            a.IzleyiciSifreHash = null;
            a.IzleyiciOturumSurumu++;
            db.SaveChanges();
            oturum.SurumleriAyarla(a);
            return Results.Ok();
        });

        // --- Açık oturumlar ---
        editor.MapGet("/oturumlar", (HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar) =>
        {
            var bu = Jti(http.User);
            var yerlesik = kullanicilar.Yerlesik(db);
            return GecerliOturumlar(db)
                .OrderByDescending(o => o.SonGorulmeUtc)
                .Select(o =>
                {
                    // Kişi claim'i olmayan eski editör token'ı yerleşik editöre aittir.
                    var kid = o.KullaniciId ?? (o.Rol == Roller.Editor ? yerlesik?.Id : null);
                    var ad = kid is { } i && kullanicilar.Bul(db, i) is { } k ? k.AdSoyad
                        : o.AdSoyad ?? (o.Rol == Roller.Izleyici ? "Ortak izleyici şifresi" : null);
                    return new OturumDto(o.Jti, kid, ad, o.Rol, o.Cihaz, o.Ip, Utc(o.OlusturmaUtc), Utc(o.SonGorulmeUtc),
                        Utc(o.BitisUtc), o.Eski, o.Jti == bu);
                })
                .ToList();
        });

        editor.MapPost("/oturumlar/{jti}/kapat", (string jti, KasaDbContext db, OturumOnbellegi oturum, OturumIzleyici izleyici, TimeProvider saat) =>
        {
            var o = db.OturumKayitlari.AsNoTracking().FirstOrDefault(x => x.Jti == jti);
            if (o is null) return Results.NotFound();
            OturumuKapat(db, oturum, izleyici, jti, o.BitisUtc, saat.GetUtcNow().UtcDateTime);
            return Results.Ok();
        });

        // --- Giriş günlüğü ---
        editor.MapGet("/guvenlik/girisler", (int? limit, int? offset, bool? basarisiz, int? kullaniciId, KasaDbContext db, HttpContext http) =>
        {
            var sayfa = limit ?? 100;
            if (sayfa is < 1 or > 1000) return HataYaniti("limit 1 ile 1000 arasında olmalı.");
            if (offset is < 0) return HataYaniti("offset negatif olamaz.");
            var q = db.GirisKayitlari.AsNoTracking();
            if (basarisiz == true) q = q.Where(g => !g.Basarili && g.Neden != GirisNedenleri.KodBekleniyor);
            if (kullaniciId is { } kid) q = q.Where(g => g.KullaniciId == kid);
            http.Response.Headers["X-Toplam-Kayit"] = q.Count().ToString();
            var liste = q.OrderByDescending(g => g.ZamanUtc).ThenByDescending(g => g.Id).Skip(offset ?? 0).Take(sayfa).ToList();
            return Results.Ok(liste.Select(g => new GirisKaydiDto(g.Id, Utc(g.ZamanUtc), g.KullaniciAdi, g.AdSoyad, g.Rol,
                g.Basarili, g.Neden, g.Ip, g.Cihaz, g.Tekrar, g.SonZamanUtc is { } son ? Utc(son) : null)).ToList());
        });

        // --- Güvenlik ayarı ---
        editor.MapGet("/guvenlik/ayar", (KasaDbContext db, KullaniciOnbellegi kullanicilar, IConfiguration cfg) =>
            new GuvenlikAyariDto(kullanicilar.EditorOturumGun(db), GuvenlikKurallari.IzleyiciOturumGun, GirisGunluguGun(cfg)));

        editor.MapPut("/guvenlik/ayar", (GuvenlikAyariGuncelleDto dto, KasaDbContext db, KullaniciOnbellegi kullanicilar) =>
        {
            if (dto.EditorOturumGun is < GuvenlikKurallari.EnKisaOturumGun or > GuvenlikKurallari.EnUzunOturumGun)
                return HataYaniti($"Editör oturum süresi {GuvenlikKurallari.EnKisaOturumGun} ile {GuvenlikKurallari.EnUzunOturumGun} gün arasında olmalı.");
            var a = db.GuvenlikAyarlari.OrderBy(x => x.Id).FirstOrDefault();
            if (a is null) db.GuvenlikAyarlari.Add(a = new GuvenlikAyariEntity());
            a.EditorOturumGun = dto.EditorOturumGun;
            db.SaveChanges();
            kullanicilar.Gecersiz();
            return Results.Ok();
        });

        return app;
    }

    public static int GirisGunluguGun(IConfiguration cfg)
        => Math.Clamp(cfg.GetValue("Kasa:GirisGunluguGun", GuvenlikKurallari.VarsayilanGirisGunluguGun), 7, 3650);

    private static IResult HesapYok() => Results.NotFound(new { hata = "Bu oturumun bir kullanıcı hesabı yok." });

    /// <summary>Oturumdaki kişinin hesabı (izlenen: değişiklik kaydedilebilir).</summary>
    private static KullaniciEntity? Hesabim(KasaDbContext db, KullaniciOnbellegi kullanicilar, ClaimsPrincipal u)
        => kullanicilar.HesapId(db, u) is { } id ? db.Kullanicilar.Find(id) : null;

    private static bool KodDogruMu(KasaDbContext db, KullaniciEntity k, string? kod, TimeProvider saat, bool kurtarmaKabul)
    {
        var sonAdim = db.GirisKayitlari.Where(g => g.KullaniciId == k.Id && g.TotpAdim != null).Max(g => g.TotpAdim);
        if (Totp.Dogrula(k.TotpSir, kod, saat.GetUtcNow(), sonAdim) is not null) return true;
        return kurtarmaKabul && Totp.Normallestir(kod) is null && KurtarmaKodlari.Kullan(k.KurtarmaKodlari, kod, out _);
    }

    private static string? AdHatasi(string ad)
    {
        if (ad.Length == 0) return "Ad soyad boş olamaz.";
        if (ad.Length > 100) return "Ad soyad en fazla 100 karakter olabilir.";
        return null;
    }

    private static string? KullaniciAdiHatasi(KasaDbContext db, IConfiguration cfg, string ad, int? haricId)
    {
        if (!KullaniciAdiKalibi().IsMatch(ad))
            return "Kullanıcı adı 3–50 karakter olmalı; yalnız harf, rakam, nokta, tire ve alt çizgi içerebilir.";
        if (cfg["Kasa:EditorKullanici"] is { Length: > 0 } env && Metin.EsitBuyukKucukDuyarsiz.Equals(env, ad))
            return "Bu kullanıcı adı sunucu ayarındaki editöre ait.";
        var adlar = db.Kullanicilar.AsNoTracking().Where(k => k.Id != haricId).Select(k => k.KullaniciAdi).ToList();
        if (adlar.Any(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, ad)))
            return $"'{Metin.Kisalt(ad)}' kullanıcı adı zaten var.";
        return null;
    }

    /// <summary>
    /// Hâlâ kabul edilen oturumlar: süresi dolmamış, kapatılmamış, iptal edilmemiş ve sürümleri
    /// (genel/izleyici + kişi) güncel olanlar — <see cref="OturumDogrulama"/> ile aynı kurallar.
    /// </summary>
    public static List<OturumKaydiEntity> GecerliOturumlar(KasaDbContext db)
    {
        var simdi = DateTime.UtcNow;
        var ayar = db.Ayarlar.AsNoTracking().OrderBy(x => x.Id).First();
        var kisiler = db.Kullanicilar.AsNoTracking().ToDictionary(k => k.Id);
        var yerlesik = kisiler.Values.FirstOrDefault(k => k.Yerlesik);
        var iptaller = db.IptalEdilenTokenlar.AsNoTracking().Where(t => t.BitisUtc > simdi).Select(t => t.Jti).ToHashSet();
        var gun = db.GuvenlikAyarlari.AsNoTracking().OrderBy(a => a.Id).Select(a => (int?)a.EditorOturumGun).FirstOrDefault()
                  ?? GuvenlikKurallari.VarsayilanOturumGun;
        // Editör oturum süresi (eski satırlarda açılış token'dan tahmin edilmiştir; bkz. OturumKaydiEntity.Eski).
        bool SuresiIcinde(OturumKaydiEntity o) => o.Rol != Roller.Editor || simdi - o.OlusturmaUtc <= TimeSpan.FromDays(gun);
        return db.OturumKayitlari.AsNoTracking().Where(o => o.BitisUtc > simdi && o.KapatmaUtc == null).ToList()
            .Where(o => !iptaller.Contains(o.Jti) && SuresiIcinde(o))
            .Where(o =>
            {
                if (o.KullaniciId is { } id)
                    return o.Surum == ayar.EditorOturumSurumu && kisiler.TryGetValue(id, out var k) && k.Aktif
                           && k.Rol == o.Rol && k.OturumSurumu == o.KullaniciSurumu;
                if (o.Rol == Roller.Editor)
                    return o.Surum == ayar.EditorOturumSurumu && (yerlesik is null || (yerlesik.OturumSurumu == 0 && yerlesik.Aktif));
                return o.Surum == ayar.IzleyiciOturumSurumu;
            })
            .ToList();
    }

    /// <summary>Oturumu iptal eder (token süresi dolana kadar reddedilir) ve kapatıldı olarak işaretler.</summary>
    private static void OturumuKapat(KasaDbContext db, OturumOnbellegi oturum, OturumIzleyici izleyici, string jti, DateTime bitisUtc, DateTime simdiUtc)
    {
        oturum.IptalEt(db, jti, bitisUtc);
        db.OturumKayitlari.Where(o => o.Jti == jti).ExecuteUpdate(s => s.SetProperty(o => o.KapatmaUtc, simdiUtc));
        izleyici.Unut(jti);
    }

    /// <summary>Kişinin oturum sürümü arttı: bu cihaza (aynı cihaz adıyla) yeni token verir, çerezi yeniler.</summary>
    private static string TokenYenile(HttpContext http, KasaDbContext db, KullaniciEntity k, KullaniciOnbellegi kullanicilar,
        OturumOnbellegi oturum, OturumIzleyici izleyici, TimeProvider saat, string jwtKey, bool cerezSecure)
    {
        var simdi = saat.GetUtcNow().UtcDateTime;
        var cihaz = http.User.FindFirstValue(KimlikClaimleri.Cihaz) ?? CihazAdi.Oku(http.Request);
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var surum = oturum.GecerliSurum(db, Roller.Editor) ?? 0;
        var gun = k.Rol == Roller.Editor ? kullanicilar.EditorOturumGun(db) : GuvenlikKurallari.IzleyiciOturumGun;
        var t = OturumTokeni.Uret(new TokenIstegi(k.Rol, surum, k.Id, k.OturumSurumu, k.AdSoyad, cihaz, TimeSpan.FromDays(gun)), jwtKey);
        GirisIslemi.OturumEkle(db, t, k, k.Rol, surum, cihaz, ip, simdi);
        db.SaveChanges();
        izleyici.Yazildi(t.Jti, simdi);
        // Eski token artık geçersiz (kişi sürümü arttı); listeden de düşsün.
        if (Jti(http.User) is { } eski)
            db.OturumKayitlari.Where(o => o.Jti == eski).ExecuteUpdate(s => s.SetProperty(o => o.KapatmaUtc, simdi));
        CerezYaz(http, t.Token, t.BitisUtc, cerezSecure);
        return t.Token;
    }
}
