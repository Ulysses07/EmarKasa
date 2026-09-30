using System.Security.Claims;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Auth;

public static class OturumEndpoints
{
    public static WebApplication MapOturumEndpoints(this WebApplication app)
    {
        // Üretimde (Caddy TLS arkasında) çerez yalnızca HTTPS'te gitmeli.
        var cerezSecure = !app.Environment.IsDevelopment();

        app.MapPost("/api/auth/login", async (LoginDto dto, KasaDbContext db, IConfiguration cfg, HttpContext http,
            GirisSiniri sinir, TanidikCihaz tanidikCihaz, IzleyiciSifreDurumu izleyiciSifresi) =>
        {
            var editorKullanici = cfg["Kasa:EditorKullanici"];
            var editorSifre = cfg["Kasa:EditorSifre"];
            var editorKaydi = db.EditorGuvenlik.AsNoTracking().SingleOrDefault(e => e.Id == 1);
            var hatali = Results.Json(new { hata = "Kullanıcı adı veya şifre hatalı." }, statusCode: StatusCodes.Status401Unauthorized);
            if (string.IsNullOrWhiteSpace(dto.Sifre) || dto.Sifre.Length > 1024)
                return hatali;

            // Editör bilgileri config'te tanımlı DEĞİLSE editör girişi kapalıdır
            // (aksi halde eksik config null==null ile şifresiz editör erişimine yol açar).
            // Editör adıyla yalnız editör şifresi denenir; yanlış şifre izleyici şifresine düşmez.
            var editorAdi = !string.IsNullOrEmpty(editorKullanici) && !string.IsNullOrEmpty(editorSifre) && dto.Kullanici == editorKullanici;
            var kullanici = dto.Kullanici?.Trim().ToLowerInvariant();
            var alici = !editorAdi && kullanici is { Length: > 0 and <= 64 }
                ? db.Alicilar.AsNoTracking().FirstOrDefault(a => a.Kullanici == kullanici) : null;
            // Denenen şifrenin hedefi; başarısız deneme bütçesi IP'den bağımsızdır (editör ayrı, diğer bütün adlar ortak).
            // İzleyici şifresi kullanıcı adı istemez: alıcıya karşılık gelmeyen her ad aynı hedefi dener.
            var hedef = editorAdi ? GirisSiniri.EditorHedefi : alici is not null ? GirisSiniri.AliciHedefi(alici.Kullanici) : GirisSiniri.IzleyiciHedefi;
            var izleyiciHash = editorAdi || alici is not null ? null : db.Ayarlar.AsNoTracking().Select(a => a.IzleyiciSifreHash).FirstOrDefault();
            // Hedefin güncel oturum damgası: tanıdık cihaz belirteci buna bağlıdır, şifre ya da oturum sürümü değişince düşer.
            var hedefDamgasi = editorAdi ? OturumDamgasi.EditorIcin(editorKaydi, cfg, db)
                : alici is not null ? OturumDamgasi.AliciIcin(alici, cfg, db)
                : izleyiciHash is null ? null : OturumDamgasi.IzleyiciIcin(izleyiciHash, cfg, db);
            var ip = http.Connection.RemoteIpAddress;
            // Bütçeler şifre doğrulanmadan önce ayrılır: eşzamanlı istekler denetimi birlikte geçip bütçeyi aşamaz.
            // Başarı ayrılanı iade eder; sonuçsuz kapanan deneme (doğrulama kuyruğu dolu, iptal) şifre denenmediği için iade edilir.
            // Geçerli tanıdık cihaz belirteci hedef kilidinden muaf tutar, ağ bütçesinden tutmaz.
            using var deneme = sinir.Baslat(hedef, ip, tanidikCihaz.Dogrula(http.Request, hedef, hedefDamgasi));
            if (deneme.RedSuresi is { } bekleme)
                return HizSinirlari.Red(http, bekleme);
            // PBKDF2 doğrulaması eşzamanlılık sınırında: giriş seli CPU'yu tüketip uygulamanın geri kalanını yavaşlatamaz.
            using var izin = await sinir.DogrulamaIzniAsync(http.RequestAborted);
            if (!izin.IsAcquired)
                return HizSinirlari.Yogun(http);

            string? rol = null;
            int? aliciId = null;
            string? dogrulanmisDamga = null;
            if (editorAdi)
            {
                if (EditorGuvenligi.Dogrula(dto.Sifre, cfg, editorKaydi))
                { rol = "editor"; dogrulanmisDamga = OturumDamgasi.EditorIcin(editorKaydi, cfg, db); }
            }
            else if (alici is not null)
            {
                if (alici.Aktif && SifreHasher.Dogrula(dto.Sifre, alici.SifreHash))
                { rol = "alici"; aliciId = alici.Id; dogrulanmisDamga = OturumDamgasi.AliciIcin(alici, cfg, db); }
            }
            else
            {
                if (izleyiciHash is string h && SifreHasher.Dogrula(dto.Sifre, h))
                {
                    rol = "viewer";
                    dogrulanmisDamga = OturumDamgasi.IzleyiciIcin(h, cfg, db);
                    izleyiciSifresi.GirisYapildi(h, dto.Sifre);
                }
            }
            deneme.Sonuc(rol is not null);

            if (rol is null)
                return hatali;

            var damga = dogrulanmisDamga ?? OturumDamgasi.Uret(rol, cfg, db, aliciId)!;
            var token = JwtYardimci.Uret(rol, cfg["Kasa:JwtKey"]!, damga, aliciId);
            http.Response.Cookies.Append("kasa_auth", token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = cerezSecure,
                MaxAge = TimeSpan.FromDays(JwtYardimci.OturumGun),
            });
            // Tanıdık cihaz belirteci yenilenir: tarayıcıya HttpOnly çerez, masaüstüne gövdede 'cihaz' (güvenli depoda saklanır).
            return Results.Ok(new { rol, token, cihaz = tanidikCihaz.Ver(http, hedef, damga) });
        }).GirisSiniriUygula<LoginDto>(d => d.Kullanici);

        app.MapPost("/api/auth/logout", (HttpContext http) =>
        {
            http.Response.Cookies.Delete("kasa_auth");
            return Results.NoContent();
        });

        // Oturum doğrulaması tanıdık cihaz belirtecini de yeniler (web çerezle, masaüstü gövdedeki 'cihaz' ile).
        app.MapGet("/api/auth/me", (ClaimsPrincipal u, HttpContext http, KasaDbContext db, TanidikCihaz tanidikCihaz) =>
            Results.Ok(new { rol = u.FindFirstValue(ClaimTypes.Role), cihaz = tanidikCihaz.OturumlaYenile(http, db) })).RequireAuthorization();
        return app;
    }
}
