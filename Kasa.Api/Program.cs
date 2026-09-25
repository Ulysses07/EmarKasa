using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Kasa.Api;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<KasaDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Kasa") ?? "Data Source=kasa.db"));

builder.Services.AddScoped<HesapServisi>();
builder.Services.AddScoped<IslemListeServisi>();
builder.Services.AddSingleton<IPdfMetinOkuyucu, PdfMetinOkuyucu>();
builder.Services.AddKasaBildirimleri();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<VeritabaniHataIsleyici>();
builder.Services.AddSingleton<YedekServisi>();
builder.Services.AddHostedService<OtomatikYedek>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("guvenlik", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 60, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
});

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var jwtKey = KasaKimlikAyarlari.AnahtariDogrula(builder.Configuration, builder.Environment.IsDevelopment());
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
                if (!ctx.Request.Headers.ContainsKey("Authorization")
                    && ctx.Request.Cookies.TryGetValue("kasa_auth", out var t))
                    ctx.Token = t;
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                var db = ctx.HttpContext.RequestServices.GetRequiredService<KasaDbContext>();
                var cfg = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var rol = ctx.Principal?.FindFirstValue(ClaimTypes.Role);
                var damga = ctx.Principal?.FindFirstValue(OturumDamgasi.ClaimAdi);
                int? aliciId = int.TryParse(ctx.Principal?.FindFirstValue("alici_id"), out var id) ? id : null;
                if (!OturumDamgasi.Esit(damga, OturumDamgasi.Uret(rol, cfg, db, aliciId)))
                    ctx.Fail("Oturum geçersiz. Yeniden giriş yapın.");
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Editor", p => p.RequireRole("editor"));
    o.AddPolicy("Finans", p => p.RequireRole("editor", "viewer"));
    o.AddPolicy("Alis", p => p.RequireRole("editor", "alici"));
});

var app = builder.Build();

// Üretimde (Caddy TLS arkasında) çerez yalnızca HTTPS'te gitmeli.
var cerezSecure = !app.Environment.IsDevelopment();

// --- DB başlat + seed ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
    KasaDatabaseInitializer.Initialize(db);
    if (!db.Kanallar.Any())
    {
        db.Kanallar.AddRange(
            new KanalEntity { Ad = "MEZAT", Sira = 0 },
            new KanalEntity { Ad = "PERAKENDE", Sira = 1 },
            new KanalEntity { Ad = "TOPTAN", Sira = 2 });
    }
    if (!db.Ayarlar.Any())
    {
        db.Ayarlar.Add(new AyarEntity
        {
            TakipBaslangic = DateOnly.FromDateTime(DateTime.Today),
            KasaAcilisDevri = 0m,
            IzleyiciSifreHash = null,
        });
    }
    db.SaveChanges();
}

app.UseExceptionHandler();
app.Use(async (http, next) =>
{
    http.Response.Headers.XContentTypeOptions = "nosniff";
    http.Response.Headers["Referrer-Policy"] = "same-origin";
    http.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    if (http.Request.Path.StartsWithSegments("/api"))
    {
        http.Response.Headers.CacheControl = "no-store";
        var unsafeMethod = !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method);
        // Masaüstü Bearer istekleri Origin taşımaz. Tarayıcı mutasyonları aynı
        // kaynaktan ve özel başlıkla gelmelidir; çerez tek başına yeterli değildir.
        if (unsafeMethod && !http.Request.Headers.ContainsKey("Authorization") &&
            (http.Request.Headers.ContainsKey("Origin") || http.Request.Headers.ContainsKey("Sec-Fetch-Site")))
        {
            var origin = http.Request.Headers.Origin.ToString();
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme is not ("http" or "https")
                || http.Request.Headers["X-Kasa-Request"] != "1"
                || http.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            { http.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
        }
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { durum = "ok" }));

// --- Auth ---
app.MapPost("/api/auth/login", (LoginDto dto, KasaDbContext db, IConfiguration cfg, HttpContext http) =>
{
    var editorKullanici = cfg["Kasa:EditorKullanici"];
    var editorSifre = cfg["Kasa:EditorSifre"];
    var editorKaydi = db.EditorGuvenlik.AsNoTracking().SingleOrDefault(e => e.Id == 1);
    if (string.IsNullOrWhiteSpace(dto.Sifre) || dto.Sifre.Length > 1024)
        return Results.Unauthorized();

    string? rol = null;
    int? aliciId = null;
    string? dogrulanmisDamga = null;
    // Editör bilgileri config'te tanımlı DEĞİLSE editör girişi kapalıdır
    // (aksi halde eksik config null==null ile şifresiz editör erişimine yol açar).
    if (!string.IsNullOrEmpty(editorKullanici) && !string.IsNullOrEmpty(editorSifre)
        && dto.Kullanici == editorKullanici && EditorGuvenligi.Dogrula(dto.Sifre, cfg, editorKaydi))
    { rol = "editor"; dogrulanmisDamga = OturumDamgasi.EditorIcin(editorKaydi, cfg); }
    else
    {
        var kullanici = dto.Kullanici?.Trim().ToLowerInvariant();
        var alici = kullanici is { Length: > 0 and <= 64 }
            ? db.Alicilar.AsNoTracking().FirstOrDefault(a => a.Kullanici == kullanici) : null;
        if (alici is not null)
        {
            if (alici.Aktif && SifreHasher.Dogrula(dto.Sifre, alici.SifreHash))
            { rol = "alici"; aliciId = alici.Id; dogrulanmisDamga = OturumDamgasi.AliciIcin(alici, cfg); }
        }
        else
        {
            var ayar = db.Ayarlar.FirstOrDefault();
            if (ayar?.IzleyiciSifreHash is string h && SifreHasher.Dogrula(dto.Sifre, h))
            { rol = "viewer"; dogrulanmisDamga = OturumDamgasi.IzleyiciIcin(h, cfg); }
        }
    }

    if (rol is null) return Results.Unauthorized();

    var token = JwtYardimci.Uret(rol, cfg["Kasa:JwtKey"]!, dogrulanmisDamga ?? OturumDamgasi.Uret(rol, cfg, db, aliciId)!, aliciId);
    http.Response.Cookies.Append("kasa_auth", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = cerezSecure,
        MaxAge = TimeSpan.FromDays(30),
    });
    return Results.Ok(new { rol, token });
}).RequireRateLimiting("guvenlik");

app.MapPost("/api/auth/logout", (HttpContext http) =>
{
    http.Response.Cookies.Delete("kasa_auth");
    return Results.NoContent();
});

app.MapGet("/api/auth/me", (ClaimsPrincipal u) =>
    Results.Ok(new { rol = u.FindFirstValue(ClaimTypes.Role) })).RequireAuthorization();

app.MapAlisEndpoints();
app.MapFinansTakipEndpoints();
app.MapBenzerKayitEndpoints();
app.MapAylikGiderEndpoints();
app.MapAyKilidiEndpoints();
app.MapKasaKontrolEndpoints();
app.MapEkstreImportEndpoints();
app.MapBildirimEndpoints();
app.MapAliciEndpoints();
app.MapGuvenlikEndpoints();
app.MapBelgeEndpoints();
app.MapYonetimEndpoints();

// Finansal bilgiler yalnız editör ve izleyiciye açıktır.
var api = app.MapGroup("/api").RequireAuthorization("Finans");

// Kanallar
api.MapGet("/kanallar", (KasaDbContext db) => db.Kanallar.OrderBy(k => k.Sira).ToList());
api.MapPost("/kanallar", (KanalYazDto dto, KasaDbContext db) =>
{
    var v = new GirdiDogrulama();
    v.Metin(dto.Ad, "ad");
    v.Para(dto.AcilisDevri, "acilisDevri", negatifOlabilir: true);
    v.Kontrol(!GirdiDogrulama.AyrilmisKanalAdi(dto.Ad?.Trim() ?? ""), "ad", "Bu ad sistem tarafından kullanılıyor.");
    if (v.Sonuc() is { } hata) return hata;
    var ad = dto.Ad!.Trim();
    if (db.Kanallar.AsEnumerable().Any(k => string.Equals(k.Ad, ad, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { hata = "Bu kanal adı zaten kullanılıyor." });
    var e = new KanalEntity { Ad = ad, Aktif = dto.Aktif, Sira = dto.Sira, AcilisDevri = dto.AcilisDevri };
    db.Kanallar.Add(e); db.SaveChanges();
    return Results.Created($"/api/kanallar/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/kanallar/{id:int}", (int id, KanalYazDto gelen, KasaDbContext db) =>
{
    var e = db.Kanallar.Find(id);
    if (e is null) return Results.NotFound();
    var v = new GirdiDogrulama();
    v.Metin(gelen.Ad, "ad");
    v.Para(gelen.AcilisDevri, "acilisDevri", negatifOlabilir: true);
    v.Kontrol(!GirdiDogrulama.AyrilmisKanalAdi(gelen.Ad?.Trim() ?? ""), "ad", "Bu ad sistem tarafından kullanılıyor.");
    if (v.Sonuc() is { } hata) return hata;
    var ad = gelen.Ad!.Trim();
    if (db.Kanallar.AsEnumerable().Any(k => k.Id != id && string.Equals(k.Ad, ad, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict(new { hata = "Bu kanal adı zaten kullanılıyor." });

    using var transaction = db.Database.BeginTransaction();
    var eskiAd = e.Ad;
    foreach (var i in db.Islemler.Where(i => i.KanalId == id || (i.KanalId == null && i.Kanal == eskiAd)))
    { i.KanalId = id; i.Kanal = ad; }
    foreach (var g in db.Gelenler.Where(g => g.KanalId == id || (g.KanalId == null && g.Kanal == eskiAd)))
    { g.KanalId = id; g.Kanal = ad; }
    foreach (var k in db.Krediler.Where(k => k.KanalId == id || (k.KanalId == null && k.Kanal == eskiAd)))
    { k.KanalId = id; k.Kanal = ad; }
    e.Ad = ad; e.Aktif = gelen.Aktif; e.Sira = gelen.Sira; e.AcilisDevri = gelen.AcilisDevri;
    db.SaveChanges();
    transaction.Commit();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/kanallar/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.Kanallar.Find(id);
    if (e is null) return Results.NotFound();
    if (e.AcilisDevri != 0
        || db.Islemler.Any(i => i.KanalId == id || i.Kanal == e.Ad)
        || db.Gelenler.Any(g => g.KanalId == id || g.Kanal == e.Ad)
        || db.Krediler.Any(k => k.KanalId == id || k.Kanal == e.Ad)
        || db.HesapHareketler.Any(h => h.KanalId == id)
        || FinansTakipServisi.KanalKullaniliyor(db, id)
        || db.AlisDagilimlar.Any(d => d.KanalId == id))
        return Results.Conflict(new { hata = "Geçmişi veya açılış bakiyesi olan kanal silinemez. Kanalı pasifleştirebilirsiniz." });
    db.Kanallar.Remove(e); db.SaveChanges();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Kredi kartları (güncel borç türetilir: açılış + harcama − ödeme)
api.MapGet("/kredikartlari", (KasaDbContext db) =>
{
    var bugun = DateOnly.FromDateTime(DateTime.Today);
    var kartlar = db.KrediKartlari.OrderBy(k => k.Ad).ToList();
    var harcamaKayit = db.Islemler.Where(i => i.KrediKartiId != null)
        .Select(i => new { Id = i.KrediKartiId!.Value, i.Tarih, i.TutarTl })
        .ToList()
        .GroupBy(x => x.Id)
        .ToDictionary(g => g.Key, g => g.ToList());
    var odeme = db.KartOdemeler
        .GroupBy(o => o.KrediKartiId)
        .ToDictionary(g => g.Key, g => g.Sum(o => o.Tutar));
    return kartlar.Select(k =>
    {
        var kh = harcamaKayit.GetValueOrDefault(k.Id);
        var h = kh?.Sum(x => x.TutarTl) ?? 0m;
        var o = odeme.GetValueOrDefault(k.Id, 0m);
        var sonKesim = KartDonem.SonKesim(k.KesimTarihi.Day, bugun);
        var kesimSonrasi = kh?.Where(x => x.Tarih > sonKesim).Sum(x => x.TutarTl) ?? 0m;
        var guncel = k.Borc + h - o;
        return new KrediKartiTuretilmisDto(
            k.Id, k.Ad, k.KesimTarihi, k.SonOdemeTarihi, k.Limit,
            Borc: k.Borc, GuncelBorc: guncel, AcilisBorc: k.Borc,
            HarcamaToplam: h, OdemeToplam: o, EkstreBorc: guncel - kesimSonrasi);
    }).ToList();
});
api.MapPost("/kredikartlari", (KrediKartiYazDto dto, KasaDbContext db) =>
{
    return Results.Conflict(new { hata = "Yeni kartı güncel uygulamanın Kredi Kartları ekranından oluşturun." });
}).RequireAuthorization("Editor");
api.MapPut("/kredikartlari/{id:int}", (int id, KrediKartiYazDto dto, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    if (db.TakipKartlar.Any(k => k.KrediKartiId == id)) return Results.Conflict(new { hata = "Bu kart yeni takipte; Kredi Kartları ekranından düzenleyin." });
    var e = db.KrediKartlari.Find(id);
    if (e is null) return Results.NotFound();
    var (gelen, hata) = KayitGirdileri.Kart(dto);
    if (hata is not null) return hata;
    gelen.Id = id;
    db.Entry(e).CurrentValues.SetValues(gelen);
    db.SaveChanges();
    transaction.Commit();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/kredikartlari/{id:int}", (int id, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    if (db.TakipKartlar.Any(k => k.KrediKartiId == id)) return Results.Conflict(new { hata = "Takip edilen kart silinemez; yeni kullanıma kapatın." });
    if (db.HesapHareketler.Any(h => h.KartOdeme != null && h.KartOdeme.KrediKartiId == id))
        return Results.Conflict(new { hata = "Hesaba bağlı ödemesi bulunan kart silinemez." });
    if (db.AlisOdemeler.Any(o => o.Islem.KrediKartiId == id))
        return Results.Conflict(new { hata = "Bu kart alış ödemelerine bağlı; ödeme bağlantısı korunmalıdır." });
    var e = db.KrediKartlari.Find(id);
    if (e is null) return Results.NotFound();
    // Harcama işlemlerinin bağını kopar (işlem kalır), ödemeleri sil.
    foreach (var i in db.Islemler.Where(i => i.KrediKartiId == id)) i.KrediKartiId = null;
    db.KartOdemeler.RemoveRange(db.KartOdemeler.Where(o => o.KrediKartiId == id));
    db.KrediKartlari.Remove(e); db.SaveChanges();
    transaction.Commit();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Krediler (banka kredileri)
api.MapGet("/krediler", (KasaDbContext db) => db.Krediler.ToList());
api.MapPost("/krediler", (KrediYazDto dto, KasaDbContext db) =>
{
    return Results.Conflict(new { hata = "Yeni krediyi güncel uygulamanın Krediler ekranından oluşturun." });
}).RequireAuthorization("Editor");
api.MapPut("/krediler/{id:int}", (int id, KrediYazDto dto, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    if (db.TakipKrediler.Any(k => k.KrediId == id)) return Results.Conflict(new { hata = "Bu kredi yeni takipte; Krediler ekranından düzenleyin." });
    var e = db.Krediler.Find(id);
    if (e is null) return Results.NotFound();
    if (db.KrediTaksitOdemeler.Any(o => o.KrediId == id) || db.HesapHareketler.Any(h => h.KrediId == id))
        return Results.Conflict(new { hata = "Ödemesi veya hesap bağlantısı bulunan kredi değiştirilemez." });
    var (gelen, hata) = KayitGirdileri.Kredi(dto, db);
    if (hata is not null) return hata;
    gelen.Id = id;
    gelen.GerceklesmeTakibi = e.GerceklesmeTakibi;
    db.Entry(e).CurrentValues.SetValues(gelen);
    db.SaveChanges();
    transaction.Commit();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/krediler/{id:int}", (int id, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    if (db.TakipKrediler.Any(k => k.KrediId == id)) return Results.Conflict(new { hata = "Takip edilen kredi silinemez; arşivleyin." });
    var e = db.Krediler.Find(id);
    if (e is null) return Results.NotFound();
    if (db.KrediTaksitOdemeler.Any(o => o.KrediId == id) || db.HesapHareketler.Any(h => h.KrediId == id))
        return Results.Conflict(new { hata = "Ödemesi veya hesap bağlantısı bulunan kredi silinemez." });
    db.Krediler.Remove(e); db.SaveChanges();
    transaction.Commit();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Islemler
api.MapGet("/islemler", (DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari, IslemListeServisi svc) =>
    svc.Liste(baslangic, bitis, kanal, cari));
api.MapPost("/islemler", (IslemYazDto dto, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    var (e, hata) = KayitGirdileri.Islem(dto, db);
    if (hata is not null) return hata;
    db.Islemler.Add(e); db.SaveChanges();
    FinansTakipServisi.Sync(db);
    transaction.Commit();
    return Results.Created($"/api/islemler/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/islemler/{id:int}", (int id, IslemYazDto dto, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    if (db.HesapHareketler.Any(h => h.IslemId == id) || db.KrediTaksitOdemeler.Any(o => o.IslemId == id))
        return Results.Conflict(new { hata = "Hesap veya krediye bağlı hareket genel gider ekranından değiştirilemez." });
    if (db.AlisOdemeler.Any(o => o.IslemId == id))
        return Results.Conflict(new { hata = "Bu gider bir alışa bağlı. Kanal dağılımını Alışlar ekranından düzenleyin; ödeme tutarı ve tarihi burada değiştirilemez." });
    var e = db.Islemler.Find(id);
    if (e is null) return Results.NotFound();
    if (FinansTakipServisi.IslemYonetiliyor(db, e) || (dto.KrediKartiId is { } newCard && db.TakipKartlar.Any(t => t.KrediKartiId == newCard)))
        return Results.Conflict(new { hata = "Kart takibine bağlı hareket için Kredi Kartları ekranından açıklamalı iade/düzeltme girin." });
    var (gelen, hata) = KayitGirdileri.Islem(dto, db);
    if (hata is not null) return hata;
    gelen.Id = id;
    db.Entry(e).CurrentValues.SetValues(gelen);
    db.SaveChanges();
    transaction.Commit();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/islemler/{id:int}", (int id, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    if (db.HesapHareketler.Any(h => h.IslemId == id) || db.KrediTaksitOdemeler.Any(o => o.IslemId == id))
        return Results.Conflict(new { hata = "Hesap veya krediye bağlı hareket genel gider ekranından silinemez." });
    if (db.AlisOdemeler.Any(o => o.IslemId == id))
        return Results.Conflict(new { hata = "Alışa bağlı ödeme silinemez; alış ve kasa bağlantısı korunmalıdır." });
    var e = db.Islemler.Find(id);
    if (e is null) return Results.NotFound();
    if (FinansTakipServisi.IslemYonetiliyor(db, e)) return Results.Conflict(new { hata = "Kart takibine bağlı hareket silinemez; açıklamalı iade girin." });
    db.Islemler.Remove(e); db.SaveChanges();
    transaction.Commit();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Kart ödemeleri (borç-only; kasa motoruna girmez)
api.MapGet("/kartodemeler", (int? krediKartiId, KasaDbContext db) =>
{
    var q = db.KartOdemeler.AsQueryable();
    if (krediKartiId is { } id) q = q.Where(o => o.KrediKartiId == id);
    return q.OrderByDescending(o => o.Tarih).ThenByDescending(o => o.Id).ToList();
});
api.MapPost("/kartodemeler", (KartOdemeYazDto dto, KasaDbContext db) =>
{
    if (db.TakipKartlar.Any(k => k.KrediKartiId == dto.KrediKartiId)) return Results.Conflict(new { hata = "Yeni takipteki kartın ödemesini Kredi Kartları ekranından kaydedin." });
    var (e, hata) = KayitGirdileri.KartOdeme(dto, db);
    if (hata is not null) return hata;
    db.KartOdemeler.Add(e); db.SaveChanges();
    return Results.Created($"/api/kartodemeler/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapDelete("/kartodemeler/{id:int}", (int id, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    var e = db.KartOdemeler.Find(id);
    if (e is null) return Results.NotFound();
    if (db.TakipKartlar.Any(k => k.KrediKartiId == e.KrediKartiId)) return Results.Conflict(new { hata = "Geçişi yapılmış kartın eski ödemeleri korunur." });
    if (db.HesapHareketler.Any(h => h.KartOdemeId == id))
        return Results.Conflict(new { hata = "Hesaba bağlı kart ödemesi silinemez." });
    db.KartOdemeler.Remove(e); db.SaveChanges();
    transaction.Commit();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Yeni gelirlerde dönem+kanal başına tek satır; eski yinelenen gruplar salt okunur.
api.MapGet("/gelenler", (DateOnly? donemStart, KasaDbContext db) =>
{
    var q = db.Gelenler.AsQueryable();
    if (donemStart is { } d) q = q.Where(g => g.DonemStart == d);
    return q.ToList();
});
api.MapPut("/gelenler", (GelenUpsertDto dto, KasaDbContext db) =>
{
    var v = new GirdiDogrulama();
    v.Tarih(dto.DonemStart, "donemStart");
    v.Para(dto.TutarTl, "tutarTl", negatifOlabilir: true);
    var kanal = v.Kanal(db, dto.Kanal, ortakOlabilir: false);
    var takipBaslangic = db.Ayarlar.Select(a => a.TakipBaslangic).First();
    v.Kontrol(dto.DonemStart >= takipBaslangic
              && (dto.DonemStart == takipBaslangic || dto.DonemStart.Day == 1 || dto.DonemStart.DayOfWeek == DayOfWeek.Monday),
        "donemStart", "Gelir için takip başlangıcından itibaren geçerli bir dönem başlangıcı seçin.");
    if (v.Sonuc() is { } hata) return hata;
    if (db.Gelenler.Any(g => g.EskiYinelenenGrup && g.DonemStart == dto.DonemStart
        && (g.KanalId == kanal!.Id || g.Kanal == kanal.Ad)))
        return Results.Conflict(new { hata = "Bu dönem ve kanalda birden fazla eski gelir kaydı var. Bütün kayıtlar tutarlarıyla korunur; bu eski grup salt okunurdur. Yeni dönemlere gelir girebilirsiniz." });
    // Tek SQL ifadesi: eşzamanlı ilk girişler çift gelir kaydı üretemez.
    var affected = db.Database.ExecuteSqlInterpolated($"""
        INSERT INTO "Gelenler" ("DonemStart", "Kanal", "KanalId", "TutarTl")
        VALUES ({dto.DonemStart}, {kanal!.Ad}, {kanal.Id}, {dto.TutarTl})
        ON CONFLICT ("DonemStart", "KanalId") WHERE "EskiYinelenenGrup" = 0
        DO UPDATE SET "TutarTl" = excluded."TutarTl", "Kanal" = excluded."Kanal"
        WHERE NOT EXISTS (SELECT 1 FROM "HesapHareketler" h WHERE h."GelenId" = "Gelenler"."Id")
        """);
    var e = db.Gelenler.AsNoTracking().Single(g => g.DonemStart == dto.DonemStart && g.KanalId == kanal.Id);
    if (affected == 0 && e.TutarTl != dto.TutarTl)
        return Results.Conflict(new { hata = "Hesaba bağlı gelir tutarı buradan değiştirilemez." });
    return Results.Ok(e);
}).RequireAuthorization("Editor");

// Ayarlar
api.MapGet("/ayarlar", (KasaDbContext db) =>
{
    var a = db.Ayarlar.First();
    return Results.Ok(new
    {
        a.TakipBaslangic,
        a.KasaAcilisDevri,
        IzleyiciSifreVarMi = a.IzleyiciSifreHash != null,
    });
});
api.MapPut("/ayarlar", (AyarGuncelleDto dto, KasaDbContext db) =>
{
    using var transaction = db.Database.BeginTransaction();
    var v = new GirdiDogrulama();
    v.Tarih(dto.TakipBaslangic, "takipBaslangic");
    v.Para(dto.KasaAcilisDevri, "kasaAcilisDevri", negatifOlabilir: true);
    if (v.Sonuc() is { } hata) return hata;
    var a = db.Ayarlar.First();
    if (a.TakipBaslangic != dto.TakipBaslangic
        && (db.Islemler.Any() || db.Gelenler.Any() || db.Krediler.Any() || db.HesapHareketler.Any()
            || db.HesapTransferler.Any() || db.KartOdemeler.Any()))
        return Results.Conflict(new { hata = "Hareketler kaydedildikten sonra takip başlangıcı değiştirilemez; mevcut dönem bağlantıları korunmalıdır." });
    a.TakipBaslangic = dto.TakipBaslangic;
    a.KasaAcilisDevri = dto.KasaAcilisDevri;
    db.SaveChanges();
    transaction.Commit();
    return Results.Ok();
}).RequireAuthorization("Editor");
api.MapPut("/ayarlar/izleyici-sifre", (IzleyiciSifreDto dto, KasaDbContext db) =>
{
    var v = new GirdiDogrulama();
    v.Metin(dto.YeniSifre, "yeniSifre", 1024);
    if (v.Sonuc() is { } hata) return hata;
    var a = db.Ayarlar.First();
    a.IzleyiciSifreHash = SifreHasher.Hashle(dto.YeniSifre);
    db.SaveChanges();
    return Results.Ok();
}).RequireAuthorization("Editor");

// Raporlar (okuma — her iki rol)
api.MapGet("/donemler", (HesapServisi svc) => svc.Donemler());
api.MapGet("/rapor/haftalik", (HesapServisi svc) => svc.Haftalik());
api.MapGet("/rapor/aylik", (int yil, int ay, HesapServisi svc) =>
    yil is >= 1 and < 9999 && ay is >= 1 and <= 12
        ? Results.Ok(svc.Aylik(yil, ay))
        : Results.BadRequest(new { hata = "Geçerli bir yıl ve ay seçin." }));
api.MapGet("/rapor/panel", (HesapServisi svc) => svc.Panel());

app.Run();

public record AyarGuncelleDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri);

public partial class Program { }
