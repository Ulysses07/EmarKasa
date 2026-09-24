using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Kasa.Api;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Sunucu yazılımını ifşa etme (Server: Kestrel başlığı).
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.Services.AddDbContext<KasaDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Kasa") ?? "Data Source=kasa.db"));

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddScoped<HesapServisi>();
builder.Services.AddSingleton<OturumOnbellegi>();
builder.Services.AddSingleton(sp => new YedekDurumu
{
    Etkin = !string.IsNullOrWhiteSpace(sp.GetRequiredService<IConfiguration>()["Kasa:YedekKlasoru"]),
});
builder.Services.AddHostedService<YedekServisi>();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// JWT anahtarı zorunlu: varsayılan anahtarla imzalanmış token'lar herkesçe üretilebilir.
var jwtKey = builder.Configuration["Kasa:JwtKey"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException(
        "Kasa:JwtKey tanımlı değil ya da 32 bayttan kısa. deploy/.env içinde KASA_JWT_KEY ayarlayın.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.Request.Cookies.TryGetValue("kasa_auth", out var t))
                    ctx.Token = t;
                return Task.CompletedTask;
            },
            // Oturum sürümü eşleşmeyen (şifre değişmiş / oturumlar kapatılmış) ya da çıkışta
            // iptal edilmiş token'ı reddet. Sürümler ve iptaller bellekte önbellekli: istek
            // başına DB sorgusu yok.
            OnTokenValidated = ctx =>
            {
                var sp = ctx.HttpContext.RequestServices;
                var oturum = sp.GetRequiredService<OturumOnbellegi>();
                var db = sp.GetRequiredService<KasaDbContext>();
                var rol = ctx.Principal?.FindFirstValue(ClaimTypes.Role);
                var surum = ctx.Principal?.FindFirstValue(JwtYardimci.SurumClaim);
                var gecerli = oturum.GecerliSurum(db, rol);
                if (gecerli is null || surum != gecerli.Value.ToString())
                    ctx.Fail("Oturum geçersiz kılındı.");
                else if (oturum.IptalMi(db, ctx.SecurityToken?.Id))
                    ctx.Fail("Oturum kapatıldı.");
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(o =>
    o.AddPolicy("Editor", p => p.RequireRole("editor")));

// Giriş denemesi sınırı:
//  - IP başına dakikada Kasa:GirisLimiti (varsayılan 10) deneme,
//  - ayrıca TÜM istemciler için toplam dakikada Kasa:GirisGlobalLimiti (varsayılan 60) deneme.
// Genel sınır, IP tespiti yanıltılsa bile (X-Forwarded-For sahteciliği) tahmin hızını sabitler.
// Bedeli: saldırı sırasında meşru girişler de bir dakikalığına 429 alabilir.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            ? RateLimitPartition.GetFixedWindowLimiter("giris-genel", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Kasa:GirisGlobalLimiti", 60),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter("serbest"));
    o.AddPolicy("giris", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "bilinmiyor",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Kasa:GirisLimiti", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// Caddy/nginx arkasında gerçek istemci IP'si X-Forwarded-For'dan gelir. Başlık YALNIZ
// Kasa:GuvenilirAglar'daki (CIDR; tek değer, virgüllü liste ya da config dizisi) adreslerden
// gelen bağlantılarda dikkate alınır ve yalnız en sağdaki (proxy'nin eklediği) girdi kullanılır.
var guvenilirAglar = GuvenilirAglar(builder.Configuration);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
    foreach (var ag in guvenilirAglar) o.KnownIPNetworks.Add(ag);
});

var app = builder.Build();

// Üretimde (Caddy TLS arkasında) çerez yalnızca HTTPS'te gitmeli.
var cerezSecure = !app.Environment.IsDevelopment();

// --- DB başlat (WAL + şema güncelle + seed) ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
    VeritabaniBaslatici.Baslat(db, app.Logger, app.Configuration["Kasa:YedekKlasoru"]);
    scope.ServiceProvider.GetRequiredService<OturumOnbellegi>().Yukle(db);
}

// Güvenlik başlıkları uygulama katmanında da garanti: API yalnız JSON döner, hiçbir
// şeyin çerçevelenmesine, script/stil yüklemesine ya da MIME tahminine gerek yok.
app.Use((ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    return next();
});
app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var surumEtiketi = app.Configuration["KASA_SURUM"] ?? "yerel";

// Sağlık: DB'ye ucuz bir sorgu atar; DB erişilemezse ya da disk dolmak üzereyse 503.
// Kimlik doğrulaması istemez (Docker HEALTHCHECK kullanır). "uzakYedek" yalnız bilgi amaçlıdır:
// sunucu dışı yedeğin durumu yanıt kodunu etkilemez.
app.MapGet("/health", (KasaDbContext db, YedekDurumu yedek, IConfiguration cfg, ILogger<Program> log, TimeProvider saat) =>
{
    double? yedekYasSaat = yedek.SonBasariliUtc is { } t ? Math.Round((DateTime.UtcNow - t).TotalHours, 1) : null;
    long? diskBosMb = null;
    try
    {
        _ = db.Ayarlar.AsNoTracking().Any();
        diskBosMb = DiskBosMb(db);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Sağlık kontrolü: veritabanına erişilemedi");
        return Results.Json(new { durum = "hata", surum = surumEtiketi, hata = "Veritabanına erişilemiyor.", sonYedekYasSaat = yedekYasSaat },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    var minDisk = cfg.GetValue("Kasa:SaglikMinDiskMb", 16L);
    if (diskBosMb is { } bos && bos < minDisk)
        return Results.Json(new { durum = "hata", surum = surumEtiketi, hata = $"Disk dolmak üzere ({bos} MB boş).", sonYedekYasSaat = yedekYasSaat, diskBosMb },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    return Results.Ok(new
    {
        durum = "ok",
        surum = surumEtiketi,
        yedek = yedek.Etkin ? "acik" : "kapali",
        sonYedekYasSaat = yedekYasSaat,
        yedekHatasi = yedek.SonHata,
        diskBosMb,
        uzakYedek = UzakYedekDurumu.Oku(cfg["Kasa:UzakYedekDurumDosyasi"], saat.GetUtcNow(),
            cfg.GetValue("Kasa:UzakYedekEskiSaat", UzakYedekDurumu.VarsayilanEskiSaat)),
    });
}).AllowAnonymous();

// --- Auth ---
app.MapPost("/api/auth/login", (LoginDto dto, KasaDbContext db, IConfiguration cfg, HttpContext http, OturumOnbellegi oturum) =>
{
    var editorKullanici = cfg["Kasa:EditorKullanici"];
    var editorSifre = cfg["Kasa:EditorSifre"];
    var ayar = db.Ayarlar.AsNoTracking().OrderBy(x => x.Id).First();

    string? rol = null;
    // Editör bilgileri config'te tanımlı DEĞİLSE editör girişi kapalıdır
    // (aksi halde eksik config null==null ile şifresiz editör erişimine yol açar).
    if (!string.IsNullOrEmpty(editorKullanici) && !string.IsNullOrEmpty(editorSifre)
        && dto.Kullanici == editorKullanici && SabitZamanEsit(dto.Sifre, editorSifre))
        rol = "editor";
    else if (ayar.IzleyiciSifreHash is string h && SifreHasher.Dogrula(dto.Sifre ?? "", h))
        rol = "viewer";

    if (rol is null) return Results.Unauthorized();

    oturum.SurumleriAyarla(ayar);
    var token = JwtYardimci.Uret(rol, jwtKey, GecerliSurum(ayar, rol));
    http.Response.Cookies.Append("kasa_auth", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = cerezSecure,
        MaxAge = JwtYardimci.Omur,
    });
    return Results.Ok(new { rol, token });
}).RequireRateLimiting("giris");

// Çıkış: sunulan token'ı (çerez ya da Bearer) iptal eder; token süresi dolana kadar reddedilir.
app.MapPost("/api/auth/logout", (HttpContext http, KasaDbContext db, OturumOnbellegi oturum) =>
{
    var u = http.User;
    var jti = u.FindFirstValue(JwtRegisteredClaimNames.Jti);
    if (u.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(jti))
    {
        var bitis = long.TryParse(u.FindFirstValue(JwtRegisteredClaimNames.Exp), out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime
            : DateTime.UtcNow.Add(JwtYardimci.Omur);
        oturum.IptalEt(db, jti, bitis);
    }
    http.Response.Cookies.Delete("kasa_auth");
    return Results.Ok();
});

app.MapGet("/api/auth/me", (ClaimsPrincipal u) =>
    Results.Ok(new { rol = u.FindFirstValue(ClaimTypes.Role) })).RequireAuthorization();

// --- Korumalı grup: oturum açmış herkes okuyabilir ---
var api = app.MapGroup("/api").RequireAuthorization();

// Kanallar
api.MapGet("/kanallar", (KasaDbContext db) =>
    db.Kanallar.AsNoTracking().ToList().OrderBy(k => k.Sira).ThenBy(k => k.Ad, Metin.Sirala).ToList());
api.MapPost("/kanallar", (KanalEntity e, KasaDbContext db) => Yaz(db, "Bu adla bir kanal zaten var.", () =>
{
    e.Id = 0;
    e.Ad = e.Ad?.Trim() ?? "";
    if (KanalHatasi(db, e, null) is string hata) return Hata(hata);
    db.Kanallar.Add(e); db.SaveChanges();
    return Results.Created($"/api/kanallar/{e.Id}", e);
})).RequireAuthorization("Editor");
api.MapPut("/kanallar/{id:int}", (int id, KanalEntity gelen, KasaDbContext db) => Yaz(db, "Bu adla bir kanal zaten var.", () =>
{
    var e = db.Kanallar.Find(id);
    if (e is null) return Results.NotFound();
    gelen.Ad = gelen.Ad?.Trim() ?? "";
    if (KanalHatasi(db, gelen, id) is string hata) return Hata(hata);
    if (gelen.Ad != e.Ad)
    {
        // İşlem ve gelenler kanalı adıyla tutar: yeniden adlandırmada geçmişi de taşı.
        var eskiAd = e.Ad; var yeniAd = gelen.Ad;
        db.Islemler.Where(i => i.Kanal == eskiAd).ExecuteUpdate(s => s.SetProperty(i => i.Kanal, yeniAd));
        db.Gelenler.Where(g => g.Kanal == eskiAd).ExecuteUpdate(s => s.SetProperty(g => g.Kanal, yeniAd));
    }
    e.Ad = gelen.Ad; e.Aktif = gelen.Aktif; e.Sira = gelen.Sira; e.AcilisDevri = gelen.AcilisDevri;
    db.SaveChanges();
    return Results.Ok(e);
})).RequireAuthorization("Editor");
api.MapDelete("/kanallar/{id:int}", (int id, KasaDbContext db) => Yaz(db, "Kanal silinemedi; tekrar deneyin.", () =>
{
    var e = db.Kanallar.Find(id);
    if (e is null) return Results.NotFound();
    if (db.Islemler.Any(i => i.Kanal == e.Ad) || db.Gelenler.Any(g => g.Kanal == e.Ad))
        return Results.Conflict(new { hata = "Bu kanalın geçmiş kayıtları var. Silmek yerine pasif yapın." });
    db.Kanallar.Remove(e); db.SaveChanges();
    return Results.NoContent();
})).RequireAuthorization("Editor");

// Cariler (işlemler cariye adla bağlı: ad değişimi işlemlere taşınır, işlemi olan cari silinmez)
api.MapGet("/cariler", (string? ara, KasaDbContext db) =>
{
    IEnumerable<CariEntity> l = db.Cariler.AsNoTracking().ToList();
    if (!string.IsNullOrWhiteSpace(ara))
    {
        var a = ara.Trim();
        l = l.Where(c => Metin.Icerir(c.Ad, a));
    }
    return l.OrderBy(c => c.Ad, Metin.Sirala).ToList();
});
api.MapPost("/cariler", (CariEntity e, KasaDbContext db) => Yaz(db, "Bu adla bir cari zaten var.", () =>
{
    e.Id = 0;
    e.Ad = e.Ad?.Trim() ?? "";
    if (CariHatasi(db, e.Ad, null) is string hata) return Hata(hata);
    db.Cariler.Add(e); db.SaveChanges();
    return Results.Created($"/api/cariler/{e.Id}", e);
})).RequireAuthorization("Editor");
api.MapPut("/cariler/{id:int}", (int id, CariEntity gelen, KasaDbContext db) => Yaz(db, "Bu adla bir cari zaten var.", () =>
{
    var e = db.Cariler.Find(id);
    if (e is null) return Results.NotFound();
    var ad = gelen.Ad?.Trim() ?? "";
    if (CariHatasi(db, ad, id) is string hata) return Hata(hata);
    if (ad != e.Ad)
    {
        var eskiAd = e.Ad;
        // Sabit gider işlemlerinin adı gider kalemine aittir; cari adı değişince onlara dokunulmaz.
        db.Islemler.Where(i => i.Cari == eskiAd && !(i.Tip == GiderTipi.SabitGider && i.KrediKartiId == null))
            .ExecuteUpdate(s => s.SetProperty(i => i.Cari, ad));
    }
    e.Ad = ad; e.Aktif = gelen.Aktif;
    db.SaveChanges();
    return Results.Ok(e);
})).RequireAuthorization("Editor");
api.MapDelete("/cariler/{id:int}", (int id, KasaDbContext db) => Yaz(db, "Cari silinemedi; tekrar deneyin.", () =>
{
    var e = db.Cariler.Find(id);
    if (e is null) return Results.NotFound();
    if (db.Islemler.Any(i => i.Cari == e.Ad && !(i.Tip == GiderTipi.SabitGider && i.KrediKartiId == null)))
        return Results.Conflict(new { hata = "Bu carinin işlem kayıtları var. Silmek yerine pasif yapın." });
    db.Cariler.Remove(e); db.SaveChanges();
    return Results.NoContent();
})).RequireAuthorization("Editor");

// Sabit gider kalemleri (Kira, SGK, Maaş…): sabit gider işleminin adı bu listeden gelir.
api.MapGet("/giderkalemleri", (KasaDbContext db) =>
    db.GiderKalemleri.AsNoTracking().ToList().OrderBy(k => k.Ad, Metin.Sirala).ToList());
api.MapPost("/giderkalemleri", (GiderKalemiEntity e, KasaDbContext db) => Yaz(db, "Bu adla bir gider kalemi zaten var.", () =>
{
    e.Id = 0;
    e.Ad = e.Ad?.Trim() ?? "";
    if (KalemHatasi(db, e.Ad, null) is string hata) return Hata(hata);
    db.GiderKalemleri.Add(e); db.SaveChanges();
    return Results.Created($"/api/giderkalemleri/{e.Id}", e);
})).RequireAuthorization("Editor");
api.MapPut("/giderkalemleri/{id:int}", (int id, GiderKalemiEntity gelen, KasaDbContext db) => Yaz(db, "Bu adla bir gider kalemi zaten var.", () =>
{
    var e = db.GiderKalemleri.Find(id);
    if (e is null) return Results.NotFound();
    var ad = gelen.Ad?.Trim() ?? "";
    if (KalemHatasi(db, ad, id) is string hata) return Hata(hata);
    if (ad != e.Ad)
    {
        // Ad değişince bu kalemle girilmiş eski sabit gider işlemleri de yeni adı alır.
        var eskiAd = e.Ad;
        db.Islemler.Where(i => i.Cari == eskiAd && i.Tip == GiderTipi.SabitGider && i.KrediKartiId == null)
            .ExecuteUpdate(s => s.SetProperty(i => i.Cari, ad));
    }
    e.Ad = ad; e.Aktif = gelen.Aktif;
    db.SaveChanges();
    return Results.Ok(e);
})).RequireAuthorization("Editor");
api.MapDelete("/giderkalemleri/{id:int}", (int id, KasaDbContext db) => Yaz(db, "Gider kalemi silinemedi; tekrar deneyin.", () =>
{
    var e = db.GiderKalemleri.Find(id);
    if (e is null) return Results.NotFound();
    if (db.Islemler.Any(i => i.Cari == e.Ad && i.Tip == GiderTipi.SabitGider && i.KrediKartiId == null))
        return Results.Conflict(new { hata = "Bu kalemle girilmiş işlemler var. Silmek yerine pasif yapın." });
    db.GiderKalemleri.Remove(e); db.SaveChanges();
    return Results.NoContent();
})).RequireAuthorization("Editor");

// Kredi kartları (güncel borç türetilir: açılış + harcama − ödeme)
api.MapGet("/kredikartlari", (KasaDbContext db, TimeProvider saat) =>
{
    var bugun = Saat.Bugun(saat);
    var kartlar = db.KrediKartlari.AsNoTracking().ToList().OrderBy(k => k.Ad, Metin.Sirala).ToList();
    var harcamalar = db.Islemler.AsNoTracking().Where(i => i.KrediKartiId != null)
        .Select(i => new { Id = i.KrediKartiId!.Value, i.Tarih, i.TutarTl })
        .ToList()
        .GroupBy(x => x.Id)
        .ToDictionary(g => g.Key, g => g.Select(x => new KartHarcama(x.Tarih, x.TutarTl)).ToList());
    var odemeler = db.KartOdemeler.AsNoTracking()
        .Select(o => new { o.KrediKartiId, o.Tarih, o.Tutar })
        .ToList()
        .GroupBy(o => o.KrediKartiId)
        .ToDictionary(g => g.Key, g => g.Select(o => new KartOdeme(o.Tarih, o.Tutar)).ToList());
    return kartlar.Select(k =>
    {
        // İleri tarihli harcama/ödeme bugünkü borca girmez; ekstre borcu eksiye düşmez (KartHesap.Durum).
        var d = KartHesap.Durum(k.Borc,
            harcamalar.GetValueOrDefault(k.Id) ?? [],
            odemeler.GetValueOrDefault(k.Id) ?? [],
            k.KesimTarihi.Day, bugun);
        return new KrediKartiTuretilmisDto(
            k.Id, k.Ad, k.KesimTarihi, k.SonOdemeTarihi, k.Limit,
            Borc: k.Borc, GuncelBorc: d.GuncelBorc, AcilisBorc: k.Borc,
            HarcamaToplam: d.HarcamaToplam, OdemeToplam: d.OdemeToplam, EkstreBorc: d.EkstreBorc);
    }).ToList();
});
api.MapPost("/kredikartlari", (KrediKartiEntity e, KasaDbContext db) => Yaz(db, "Kart kaydedilemedi; tekrar deneyin.", () =>
{
    e.Id = 0;
    e.Ad = e.Ad?.Trim() ?? "";
    if (KartHatasi(e) is string hata) return Hata(hata);
    db.KrediKartlari.Add(e); db.SaveChanges();
    return Results.Created($"/api/kredikartlari/{e.Id}", e);
})).RequireAuthorization("Editor");
api.MapPut("/kredikartlari/{id:int}", (int id, KrediKartiEntity gelen, KasaDbContext db) => Yaz(db, "Kart kaydedilemedi; tekrar deneyin.", () =>
{
    var e = db.KrediKartlari.Find(id);
    if (e is null) return Results.NotFound();
    gelen.Ad = gelen.Ad?.Trim() ?? "";
    if (KartHatasi(gelen) is string hata) return Hata(hata);
    e.Ad = gelen.Ad; e.KesimTarihi = gelen.KesimTarihi; e.SonOdemeTarihi = gelen.SonOdemeTarihi;
    e.Limit = gelen.Limit; e.Borc = gelen.Borc;
    db.SaveChanges();
    return Results.Ok(e);
})).RequireAuthorization("Editor");
api.MapDelete("/kredikartlari/{id:int}", (int id, KasaDbContext db) => Yaz(db, "Kart silinemedi; tekrar deneyin.", () =>
{
    var e = db.KrediKartlari.Find(id);
    if (e is null) return Results.NotFound();
    // Kart ödemeleri kasadan çıkan nakittir; kart silinirse geçmiş kasa değişir. Hareketi olan kart silinemez.
    if (db.KartOdemeler.Any(o => o.KrediKartiId == id) || db.Islemler.Any(i => i.KrediKartiId == id))
        return Results.Conflict(new { hata = "Bu kartın harcama veya ödeme kayıtları var; kasa geçmişi bozulmasın diye silinemez." });
    db.KrediKartlari.Remove(e); db.SaveChanges();
    return Results.NoContent();
})).RequireAuthorization("Editor");

// Islemler. limit/offset verilmezse tüm eşleşenler döner (eski davranış); verilirse sayfa
// döner ve toplam kayıt sayısı X-Toplam-Kayit başlığında gelir.
api.MapGet("/islemler", (DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari, int? limit, int? offset,
    KasaDbContext db, HttpContext http) =>
{
    if (limit is < 1 or > 10_000) return Hata("limit 1 ile 10000 arasında olmalı.");
    if (offset is < 0) return Hata("offset negatif olamaz.");
    var q = db.Islemler.AsNoTracking();
    if (baslangic is { } b) q = q.Where(i => i.Tarih >= b);
    if (bitis is { } s) q = q.Where(i => i.Tarih <= s);
    if (!string.IsNullOrWhiteSpace(kanal)) q = q.Where(i => i.Kanal == kanal);
    q = q.OrderBy(i => i.Tarih).ThenBy(i => i.Id);

    List<IslemEntity> sonuc;
    int toplam;
    if (!string.IsNullOrWhiteSpace(cari))
    {
        // Türkçe büyük/küçük harf duyarsız arama bellekte yapılır (SQLite instr duyarlı ve I/ı bilmez).
        var aranan = cari.Trim();
        var hepsi = q.AsEnumerable().Where(i => Metin.Icerir(i.Cari, aranan)).ToList();
        toplam = hepsi.Count;
        IEnumerable<IslemEntity> sayfa = hepsi;
        if (offset is { } o1) sayfa = sayfa.Skip(o1);
        if (limit is { } l1) sayfa = sayfa.Take(l1);
        sonuc = sayfa.ToList();
    }
    else
    {
        toplam = limit is null ? -1 : q.Count();
        if (offset is { } o2) q = q.Skip(o2);
        if (limit is { } l2) q = q.Take(l2);
        sonuc = q.ToList();
    }
    if (limit is not null) http.Response.Headers["X-Toplam-Kayit"] = toplam.ToString();
    return Results.Ok(sonuc);
});
api.MapPost("/islemler", (IslemEntity e, KasaDbContext db) => Yaz(db, "İşlem kaydedilemedi; ilgili kayıt değişmiş olabilir.", () =>
{
    e.Id = 0;
    if (e.KrediKartiId is not null) e.Tip = GiderTipi.KrediKarti; // kart harcaması tutarlılığı
    if (IslemHatasi(db, e) is string hata) return Hata(hata);
    db.Islemler.Add(e); db.SaveChanges();
    return Results.Created($"/api/islemler/{e.Id}", e);
})).RequireAuthorization("Editor");
api.MapPut("/islemler/{id:int}", (int id, IslemEntity gelen, KasaDbContext db) => Yaz(db, "İşlem kaydedilemedi; ilgili kayıt değişmiş olabilir.", () =>
{
    var e = db.Islemler.Find(id);
    if (e is null) return Results.NotFound();
    if (gelen.KrediKartiId is not null) gelen.Tip = GiderTipi.KrediKarti;
    if (IslemHatasi(db, gelen) is string hata) return Hata(hata);
    e.Tarih = gelen.Tarih; e.Cari = gelen.Cari; e.TutarTl = gelen.TutarTl;
    e.Kanal = gelen.Kanal; e.Tip = gelen.Tip; e.Not = gelen.Not;
    e.KrediKartiId = gelen.KrediKartiId;
    db.SaveChanges();
    return Results.Ok(e);
})).RequireAuthorization("Editor");
api.MapDelete("/islemler/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.Islemler.Find(id);
    if (e is null) return Results.NotFound();
    db.Islemler.Remove(e); db.SaveChanges();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Kart ödemeleri: kart borcunu düşer ve ödeme tarihinde kasadan çıkar
api.MapGet("/kartodemeler", (int? krediKartiId, KasaDbContext db) =>
{
    var q = db.KartOdemeler.AsNoTracking();
    if (krediKartiId is { } id) q = q.Where(o => o.KrediKartiId == id);
    return q.OrderByDescending(o => o.Tarih).ThenByDescending(o => o.Id).ToList();
});
api.MapPost("/kartodemeler", (KartOdemeEntity e, KasaDbContext db) => Yaz(db, "Ödeme kaydedilemedi; kart silinmiş olabilir.", () =>
{
    e.Id = 0;
    if (e.Tutar <= 0) return Hata("Ödeme tutarı sıfırdan büyük olmalı.");
    if (TutarHatasi(e.Tutar, "Ödeme tutarı") is string th) return Hata(th);
    if (TarihHatasi(e.Tarih) is string tar) return Hata(tar);
    if (e.Not is { Length: > 1000 }) return Hata("Not en fazla 1000 karakter olabilir.");
    if (!db.KrediKartlari.Any(k => k.Id == e.KrediKartiId)) return Hata("Kredi kartı bulunamadı.");
    db.KartOdemeler.Add(e); db.SaveChanges();
    return Results.Created($"/api/kartodemeler/{e.Id}", e);
})).RequireAuthorization("Editor");
api.MapDelete("/kartodemeler/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.KartOdemeler.Find(id);
    if (e is null) return Results.NotFound();
    db.KartOdemeler.Remove(e); db.SaveChanges();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Gelenler (dönem+kanal başına tek satır — upsert). DonemStart, içinde bulunduğu dönemin
// başına çekilir; takvim dışındaki tarih reddedilir.
api.MapGet("/gelenler", (DateOnly? donemStart, KasaDbContext db) =>
{
    var q = db.Gelenler.AsNoTracking();
    if (donemStart is { } d) q = q.Where(g => g.DonemStart == d);
    return q.ToList();
});
api.MapPut("/gelenler", (GelenUpsertDto dto, KasaDbContext db, TimeProvider saat) => Yaz(db, "Gelen aynı anda başka bir yerden kaydedildi; tekrar deneyin.", () =>
{
    if (dto.TutarTl < 0) return Hata("Gelen tutarı negatif olamaz.");
    if (TutarHatasi(dto.TutarTl, "Gelen tutarı") is string th) return Hata(th);
    var kanal = dto.Kanal ?? "";
    if (!db.Kanallar.Any(k => k.Ad == kanal)) return Hata($"'{Metin.Kisalt(kanal)}' adında bir kanal yok.");
    var takip = db.Ayarlar.AsNoTracking().OrderBy(a => a.Id).Select(a => a.TakipBaslangic).First();
    var bitis = Takvim.Bitis(takip, Saat.Bugun(saat));
    if (dto.DonemStart > bitis || Takvim.DonemBaslangici(dto.DonemStart, takip) is not { } donemStart)
        return Hata($"Tarih takip dönemlerinin dışında ({takip:dd.MM.yyyy} – {bitis:dd.MM.yyyy}).");

    var e = db.Gelenler.FirstOrDefault(g => g.DonemStart == donemStart && g.Kanal == kanal);
    if (e is null)
    {
        e = new GelenEntity { DonemStart = donemStart, Kanal = kanal, TutarTl = dto.TutarTl };
        db.Gelenler.Add(e);
    }
    else
    {
        e.TutarTl = dto.TutarTl;
    }
    db.SaveChanges();
    return Results.Ok(e);
})).RequireAuthorization("Editor");

// Ayarlar
api.MapGet("/ayarlar", (KasaDbContext db) =>
{
    var a = db.Ayarlar.AsNoTracking().OrderBy(x => x.Id).First();
    return Results.Ok(new
    {
        a.TakipBaslangic,
        a.KasaAcilisDevri,
        IzleyiciSifreVarMi = a.IzleyiciSifreHash != null,
    });
});
api.MapPut("/ayarlar", (AyarGuncelleDto dto, KasaDbContext db, TimeProvider saat) =>
{
    var enGec = Saat.Bugun(saat).AddYears(1);
    if (dto.TakipBaslangic < new DateOnly(2000, 1, 1) || dto.TakipBaslangic > enGec)
        return Hata($"Takip başlangıcı 01.01.2000 ile {enGec:dd.MM.yyyy} arasında olmalı.");
    if (TutarHatasi(dto.KasaAcilisDevri, "Kasa açılış devri", negatifOlabilir: true) is string th) return Hata(th);
    return Yaz(db, "Ayarlar kaydedilemedi; tekrar deneyin.", () =>
    {
        var a = db.Ayarlar.OrderBy(x => x.Id).First();
        if (dto.TakipBaslangic != a.TakipBaslangic)
            GelenleriDonemlereHizala(db, dto.TakipBaslangic);
        a.TakipBaslangic = dto.TakipBaslangic;
        a.KasaAcilisDevri = dto.KasaAcilisDevri;
        db.SaveChanges();
        return Results.Ok();
    });
}).RequireAuthorization("Editor");
api.MapPut("/ayarlar/izleyici-sifre", (IzleyiciSifreDto dto, KasaDbContext db, OturumOnbellegi oturum) =>
{
    if (string.IsNullOrWhiteSpace(dto.YeniSifre))
        return Results.BadRequest(new { hata = "Şifre boş olamaz." });
    if (dto.YeniSifre.Length > 200)
        return Results.BadRequest(new { hata = "Şifre en fazla 200 karakter olabilir." });
    var a = db.Ayarlar.OrderBy(x => x.Id).First();
    a.IzleyiciSifreHash = SifreHasher.Hashle(dto.YeniSifre);
    a.IzleyiciOturumSurumu++;   // eski izleyici oturumları kapanır
    db.SaveChanges();
    oturum.SurumleriAyarla(a);
    return Results.Ok();
}).RequireAuthorization("Editor");
api.MapPost("/ayarlar/oturumlari-kapat", (KasaDbContext db, OturumOnbellegi oturum) =>
{
    // Tüm cihazlardaki oturumları (editör dahil) kapatır; herkes yeniden giriş yapar.
    var a = db.Ayarlar.OrderBy(x => x.Id).First();
    a.IzleyiciOturumSurumu++;
    a.EditorOturumSurumu++;
    db.SaveChanges();
    oturum.SurumleriAyarla(a);
    return Results.Ok();
}).RequireAuthorization("Editor");

// Raporlar (okuma — her iki rol)
api.MapGet("/donemler", (HesapServisi svc) => svc.Donemler());
api.MapGet("/rapor/haftalik", (HesapServisi svc) => svc.Haftalik());
api.MapGet("/rapor/aylik", (int yil, int ay, HesapServisi svc) =>
{
    if (ay is < 1 or > 12) return Hata("Ay 1 ile 12 arasında olmalı.");
    if (yil is < 2000 or > 2100) return Hata("Yıl 2000 ile 2100 arasında olmalı.");
    return Results.Ok(svc.Aylik(yil, ay));
});
api.MapGet("/rapor/panel", (HesapServisi svc) => svc.Panel());

app.Run();

static int GecerliSurum(AyarEntity a, string? rol) => rol == "editor" ? a.EditorOturumSurumu : a.IzleyiciOturumSurumu;

static bool SabitZamanEsit(string? a, string b)
    => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(a ?? "")), SHA256.HashData(Encoding.UTF8.GetBytes(b)));

static IResult Hata(string mesaj) => Results.BadRequest(new { hata = mesaj });

// Yazma işlemini tek (SQLite'ta BEGIN IMMEDIATE) transaction'da çalıştırır: doğrulama
// kontrolleri ile yazma aynı kilit altında olur. Yine de bir kısıt ihlali (tekil index,
// FK) olursa 500 yerine anlaşılır bir 409 döner.
static IResult Yaz(KasaDbContext db, string cakismaMesaji, Func<IResult> islem)
{
    try
    {
        using var tx = db.Database.BeginTransaction();
        var sonuc = islem();
        tx.Commit();
        return sonuc;
    }
    catch (Exception ex) when (KisitIhlali(ex))
    {
        return Results.Conflict(new { hata = cakismaMesaji });
    }
}

static bool KisitIhlali(Exception? ex)
{
    for (; ex is not null; ex = ex.InnerException)
        if (ex is SqliteException { SqliteErrorCode: 19 }) return true; // SQLITE_CONSTRAINT
    return false;
}

// Tutar: en fazla 2 ondalık, mutlak değeri 100 milyarı geçmez.
static string? TutarHatasi(decimal tutar, string alan, bool negatifOlabilir = false)
{
    if (!negatifOlabilir && tutar < 0) return $"{alan} negatif olamaz.";
    if (Math.Abs(tutar) > 100_000_000_000m) return $"{alan} çok büyük (en fazla 100.000.000.000).";
    if (decimal.Round(tutar, 2) != tutar) return $"{alan} en fazla 2 ondalık basamak içerebilir.";
    return null;
}

static string? TarihHatasi(DateOnly t)
    => t < new DateOnly(2000, 1, 1) || t > new DateOnly(2100, 12, 31) ? "Tarih 2000 ile 2100 arasında olmalı." : null;

static string? KanalHatasi(KasaDbContext db, KanalEntity e, int? haricId)
{
    var ad = e.Ad;
    if (ad.Length == 0) return "Kanal adı boş olamaz.";
    if (ad.Length > 100) return "Kanal adı en fazla 100 karakter olabilir.";
    if (Metin.EsitBuyukKucukDuyarsiz.Equals(ad, Kanallar.Ortak)) return $"'{Kanallar.Ortak}' ayrılmış bir addır.";
    if (TutarHatasi(e.AcilisDevri, "Açılış devri", negatifOlabilir: true) is string th) return th;
    var digerleri = db.Kanallar.AsNoTracking().Where(k => k.Id != haricId).Select(k => k.Ad).ToList();
    if (digerleri.Any(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, ad))) return $"'{Metin.Kisalt(ad)}' adında bir kanal zaten var.";
    return null;
}

static string? CariHatasi(KasaDbContext db, string ad, int? haricId)
{
    if (ad.Length == 0) return "Cari adı boş olamaz.";
    if (ad.Length > 200) return "Cari adı en fazla 200 karakter olabilir.";
    var digerleri = db.Cariler.AsNoTracking().Where(c => c.Id != haricId).Select(c => c.Ad).ToList();
    if (digerleri.Any(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, ad))) return $"'{Metin.Kisalt(ad)}' adında bir cari zaten var.";
    return null;
}

static string? KalemHatasi(KasaDbContext db, string ad, int? haricId)
{
    if (ad.Length == 0) return "Gider kalemi adı boş olamaz.";
    if (ad.Length > 200) return "Gider kalemi adı en fazla 200 karakter olabilir.";
    var digerleri = db.GiderKalemleri.AsNoTracking().Where(k => k.Id != haricId).Select(k => k.Ad).ToList();
    if (digerleri.Any(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, ad))) return $"'{Metin.Kisalt(ad)}' adında bir gider kalemi zaten var.";
    return null;
}

static string? KartHatasi(KrediKartiEntity e)
{
    if (e.Ad.Length == 0) return "Kart adı boş olamaz.";
    if (e.Ad.Length > 100) return "Kart adı en fazla 100 karakter olabilir.";
    if (e.Limit < 0) return "Limit negatif olamaz.";
    if (e.Borc < 0) return "Açılış borcu negatif olamaz.";
    if (TutarHatasi(e.Limit, "Limit") is string l) return l;
    if (TutarHatasi(e.Borc, "Açılış borcu") is string b) return b;
    return null;
}

static string? IslemHatasi(KasaDbContext db, IslemEntity e)
{
    if (e.TutarTl <= 0) return "Tutar sıfırdan büyük olmalı.";
    if (TutarHatasi(e.TutarTl, "Tutar") is string th) return th;
    if (TarihHatasi(e.Tarih) is string tar) return tar;
    e.Cari = e.Cari?.Trim() ?? "";
    if (e.Cari.Length == 0) return "Cari boş olamaz.";
    if (e.Not is { Length: > 1000 }) return "Not en fazla 1000 karakter olabilir.";
    if (!Enum.IsDefined(e.Tip)) return "Geçersiz gider tipi.";
    if (e.Kanal != Kanallar.Ortak && !db.Kanallar.Any(k => k.Ad == e.Kanal))
        return $"'{Metin.Kisalt(e.Kanal)}' adında bir kanal yok.";
    if (e.KrediKartiId is int kid && !db.KrediKartlari.Any(k => k.Id == kid))
        return "Kredi kartı bulunamadı.";
    var cari = e.Cari;
    // Sabit gider (karta bağlı değilse) adı gider kalemleri listesinden seçilir.
    if (e.Tip == GiderTipi.SabitGider && e.KrediKartiId is null)
    {
        if (db.GiderKalemleri.Any(k => k.Ad == cari)) return null;
        var kalem = db.GiderKalemleri.AsNoTracking().Select(k => k.Ad).AsEnumerable()
            .FirstOrDefault(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, cari));
        if (kalem is null) return $"'{Metin.Kisalt(cari)}' adında bir sabit gider kalemi yok. Önce kalemi ekleyin.";
        e.Cari = kalem;
        return null;
    }
    // Cari listeden seçilmeli: ad/silme işlemleri işlemlere tutarlı yansısın. Büyük/küçük
    // harf farkı (Türkçe) tolere edilir ve kayıtlı yazım kullanılır.
    if (!db.Cariler.Any(c => c.Ad == cari))
    {
        var kayitli = db.Cariler.AsNoTracking().Select(c => c.Ad).AsEnumerable()
            .FirstOrDefault(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, cari));
        if (kayitli is null) return $"'{Metin.Kisalt(cari)}' adında bir cari yok. Önce Cariler sayfasından ekleyin.";
        e.Cari = kayitli;
    }
    return null;
}

static IReadOnlyList<System.Net.IPNetwork> GuvenilirAglar(IConfiguration cfg)
{
    // Kasa:GuvenilirAglar tanımlı değilse loopback + özel (RFC1918) ağlar güvenilir sayılır:
    // Docker köprü ağındaki reverse proxy (Caddy/nginx) bu aralıktadır. RİSK: aynı Docker
    // ağındaki BAŞKA bir konteyner (ya da host'taki yerel bir süreç) da bu aralıktan bağlanır
    // ve X-Forwarded-For ile IP'sini seçebilir. Üretimde yalnız proxy'nin adresini/ağını
    // KASA_GUVENILIR_AGLAR ile verin. Genel giriş sınırı bu durumda da tahmin hızını sınırlar.
    var bolum = cfg.GetSection("Kasa:GuvenilirAglar");
    var degerler = new List<string>();
    if (!string.IsNullOrWhiteSpace(bolum.Value))
        degerler.AddRange(bolum.Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    degerler.AddRange(bolum.GetChildren().Select(c => c.Value).OfType<string>()
        .SelectMany(v => v.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
    if (degerler.Count == 0)
        degerler.AddRange(["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"]);

    return degerler.Select(d =>
    {
        var cidr = d.Contains('/') ? d : d + (d.Contains(':') ? "/128" : "/32");
        return System.Net.IPNetwork.TryParse(cidr, out var ag)
            ? ag
            : throw new InvalidOperationException($"Kasa:GuvenilirAglar içinde geçersiz CIDR: '{d}'.");
    }).ToList();
}

static long? DiskBosMb(KasaDbContext db)
{
    var kaynak = db.Database.GetDbConnection().DataSource;
    if (string.IsNullOrEmpty(kaynak) || kaynak.Contains(":memory:", StringComparison.OrdinalIgnoreCase)) return null;
    var klasor = Path.GetDirectoryName(Path.GetFullPath(kaynak));
    if (string.IsNullOrEmpty(klasor) || !Directory.Exists(klasor)) return null;
    return new DriveInfo(klasor).AvailableFreeSpace / (1024 * 1024);
}

// Takip başlangıcı değişince gelen kayıtlarını yeni dönem başlangıçlarına taşır; aynı
// döneme düşen iki kayıt (aynı kanal) toplanarak birleşir. Böylece upsert çift kayıt üretmez.
static void GelenleriDonemlereHizala(KasaDbContext db, DateOnly yeniBaslangic)
{
    var gelenler = db.Gelenler.Where(g => g.DonemStart >= yeniBaslangic).ToList();
    if (gelenler.Count == 0) return;
    foreach (var grup in gelenler.GroupBy(g => (Start: Takvim.DonemBaslangici(g.DonemStart, yeniBaslangic)!.Value, g.Kanal)))
    {
        var kalan = grup.OrderBy(g => g.Id).First();
        var silinecek = grup.Where(g => g != kalan).ToList();
        kalan.TutarTl = grup.Sum(g => g.TutarTl);
        // Tekil (DonemStart, Kanal) index'i ara durumda çakışmasın: önce fazlaları sil.
        if (silinecek.Count > 0) { db.Gelenler.RemoveRange(silinecek); db.SaveChanges(); }
        kalan.DonemStart = grup.Key.Start;
    }
    db.SaveChanges();
}

public record AyarGuncelleDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri);

public partial class Program { }
