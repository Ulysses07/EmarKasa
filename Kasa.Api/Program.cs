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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<KasaDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Kasa") ?? "Data Source=kasa.db"));

builder.Services.AddScoped<HesapServisi>();
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
            // Oturum sürümü eşleşmeyen (şifre değişmiş / oturumlar kapatılmış) token'ı reddet.
            OnTokenValidated = ctx =>
            {
                var db = ctx.HttpContext.RequestServices.GetRequiredService<KasaDbContext>();
                var ayar = db.Ayarlar.AsNoTracking().FirstOrDefault();
                var rol = ctx.Principal?.FindFirstValue(ClaimTypes.Role);
                var surum = ctx.Principal?.FindFirstValue(JwtYardimci.SurumClaim);
                if (ayar is null || surum != GecerliSurum(ayar, rol).ToString())
                    ctx.Fail("Oturum geçersiz kılındı.");
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(o =>
    o.AddPolicy("Editor", p => p.RequireRole("editor")));

// Giriş denemesi sınırı: IP başına dakikada Kasa:GirisLimiti (varsayılan 10) deneme.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("giris", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "bilinmiyor",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Kasa:GirisLimiti", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// Caddy/nginx arkasında gerçek istemci IP'si X-Forwarded-For'dan gelir. Konteyner yalnız
// iç ağdan/localhost'tan erişilebilir olduğu için başlık güvenilir kabul edilir.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// Üretimde (Caddy TLS arkasında) çerez yalnızca HTTPS'te gitmeli.
var cerezSecure = !app.Environment.IsDevelopment();

// --- DB başlat + şemayı güncelle + seed ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
    db.Database.EnsureCreated();
    foreach (var degisiklik in SemaGuncelleyici.Guncelle(db))
        app.Logger.LogInformation("Şema güncellendi: {Degisiklik}", degisiklik);
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
            TakipBaslangic = Saat.Bugun(),
            KasaAcilisDevri = 0m,
            IzleyiciSifreHash = null,
        });
    }
    db.SaveChanges();
}

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var surumEtiketi = app.Configuration["KASA_SURUM"] ?? "yerel";
app.MapGet("/health", () => Results.Ok(new { durum = "ok", surum = surumEtiketi }));

// --- Auth ---
app.MapPost("/api/auth/login", (LoginDto dto, KasaDbContext db, IConfiguration cfg, HttpContext http) =>
{
    var editorKullanici = cfg["Kasa:EditorKullanici"];
    var editorSifre = cfg["Kasa:EditorSifre"];
    var ayar = db.Ayarlar.First();

    string? rol = null;
    // Editör bilgileri config'te tanımlı DEĞİLSE editör girişi kapalıdır
    // (aksi halde eksik config null==null ile şifresiz editör erişimine yol açar).
    if (!string.IsNullOrEmpty(editorKullanici) && !string.IsNullOrEmpty(editorSifre)
        && dto.Kullanici == editorKullanici && SabitZamanEsit(dto.Sifre, editorSifre))
        rol = "editor";
    else if (ayar.IzleyiciSifreHash is string h && SifreHasher.Dogrula(dto.Sifre ?? "", h))
        rol = "viewer";

    if (rol is null) return Results.Unauthorized();

    var token = JwtYardimci.Uret(rol, jwtKey, GecerliSurum(ayar, rol));
    http.Response.Cookies.Append("kasa_auth", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = cerezSecure,
        MaxAge = TimeSpan.FromDays(30),
    });
    return Results.Ok(new { rol, token });
}).RequireRateLimiting("giris");

app.MapPost("/api/auth/logout", (HttpContext http) =>
{
    http.Response.Cookies.Delete("kasa_auth");
    return Results.Ok();
});

app.MapGet("/api/auth/me", (ClaimsPrincipal u) =>
    Results.Ok(new { rol = u.FindFirstValue(ClaimTypes.Role) })).RequireAuthorization();

// --- Korumalı grup: oturum açmış herkes okuyabilir ---
var api = app.MapGroup("/api").RequireAuthorization();

// Kanallar
api.MapGet("/kanallar", (KasaDbContext db) => db.Kanallar.OrderBy(k => k.Sira).ToList());
api.MapPost("/kanallar", (KanalEntity e, KasaDbContext db) =>
{
    e.Id = 0;
    e.Ad = e.Ad?.Trim() ?? "";
    if (KanalAdHatasi(db, e.Ad, null) is string hata) return Hata(hata);
    db.Kanallar.Add(e); db.SaveChanges();
    return Results.Created($"/api/kanallar/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/kanallar/{id:int}", (int id, KanalEntity gelen, KasaDbContext db) =>
{
    var e = db.Kanallar.Find(id);
    if (e is null) return Results.NotFound();
    var yeniAd = gelen.Ad?.Trim() ?? "";
    if (KanalAdHatasi(db, yeniAd, id) is string hata) return Hata(hata);

    using var tx = db.Database.BeginTransaction();
    if (yeniAd != e.Ad)
    {
        // İşlem ve gelenler kanalı adıyla tutar: yeniden adlandırmada geçmişi de taşı.
        var eskiAd = e.Ad;
        db.Islemler.Where(i => i.Kanal == eskiAd).ExecuteUpdate(s => s.SetProperty(i => i.Kanal, yeniAd));
        db.Gelenler.Where(g => g.Kanal == eskiAd).ExecuteUpdate(s => s.SetProperty(g => g.Kanal, yeniAd));
    }
    e.Ad = yeniAd; e.Aktif = gelen.Aktif; e.Sira = gelen.Sira; e.AcilisDevri = gelen.AcilisDevri;
    db.SaveChanges();
    tx.Commit();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/kanallar/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.Kanallar.Find(id);
    if (e is null) return Results.NotFound();
    if (db.Islemler.Any(i => i.Kanal == e.Ad) || db.Gelenler.Any(g => g.Kanal == e.Ad))
        return Results.Conflict(new { hata = "Bu kanalın geçmiş kayıtları var. Silmek yerine pasif yapın." });
    db.Kanallar.Remove(e); db.SaveChanges();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Cariler
api.MapGet("/cariler", (string? ara, KasaDbContext db) =>
{
    var q = db.Cariler.AsQueryable();
    if (!string.IsNullOrWhiteSpace(ara))
        q = q.Where(c => c.Ad.Contains(ara));
    return q.OrderBy(c => c.Ad).ToList();
});
api.MapPost("/cariler", (CariEntity e, KasaDbContext db) =>
{
    e.Id = 0;
    e.Ad = e.Ad?.Trim() ?? "";
    if (e.Ad.Length == 0) return Hata("Cari adı boş olamaz.");
    db.Cariler.Add(e); db.SaveChanges();
    return Results.Created($"/api/cariler/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/cariler/{id:int}", (int id, CariEntity gelen, KasaDbContext db) =>
{
    var e = db.Cariler.Find(id);
    if (e is null) return Results.NotFound();
    var ad = gelen.Ad?.Trim() ?? "";
    if (ad.Length == 0) return Hata("Cari adı boş olamaz.");
    e.Ad = ad; e.Aktif = gelen.Aktif;
    db.SaveChanges();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/cariler/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.Cariler.Find(id);
    if (e is null) return Results.NotFound();
    db.Cariler.Remove(e); db.SaveChanges();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Kredi kartları (güncel borç türetilir: açılış + harcama − ödeme)
api.MapGet("/kredikartlari", (KasaDbContext db) =>
{
    var bugun = Saat.Bugun();
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
api.MapPost("/kredikartlari", (KrediKartiEntity e, KasaDbContext db) =>
{
    e.Id = 0;
    e.Ad = e.Ad?.Trim() ?? "";
    if (KartHatasi(e) is string hata) return Hata(hata);
    db.KrediKartlari.Add(e); db.SaveChanges();
    return Results.Created($"/api/kredikartlari/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/kredikartlari/{id:int}", (int id, KrediKartiEntity gelen, KasaDbContext db) =>
{
    var e = db.KrediKartlari.Find(id);
    if (e is null) return Results.NotFound();
    gelen.Ad = gelen.Ad?.Trim() ?? "";
    if (KartHatasi(gelen) is string hata) return Hata(hata);
    e.Ad = gelen.Ad; e.KesimTarihi = gelen.KesimTarihi; e.SonOdemeTarihi = gelen.SonOdemeTarihi;
    e.Limit = gelen.Limit; e.Borc = gelen.Borc;
    db.SaveChanges();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/kredikartlari/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.KrediKartlari.Find(id);
    if (e is null) return Results.NotFound();
    // Kart ödemeleri kasadan çıkan nakittir; kart silinirse geçmiş kasa değişir. Hareketi olan kart silinemez.
    if (db.KartOdemeler.Any(o => o.KrediKartiId == id) || db.Islemler.Any(i => i.KrediKartiId == id))
        return Results.Conflict(new { hata = "Bu kartın harcama veya ödeme kayıtları var; kasa geçmişi bozulmasın diye silinemez." });
    db.KrediKartlari.Remove(e); db.SaveChanges();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Islemler
api.MapGet("/islemler", (DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari, KasaDbContext db) =>
{
    var q = db.Islemler.AsQueryable();
    if (baslangic is { } b) q = q.Where(i => i.Tarih >= b);
    if (bitis is { } s) q = q.Where(i => i.Tarih <= s);
    if (!string.IsNullOrWhiteSpace(kanal)) q = q.Where(i => i.Kanal == kanal);
    if (!string.IsNullOrWhiteSpace(cari)) q = q.Where(i => i.Cari.Contains(cari));
    return q.OrderBy(i => i.Tarih).ThenBy(i => i.Id).ToList();
});
api.MapPost("/islemler", (IslemEntity e, KasaDbContext db) =>
{
    e.Id = 0;
    if (e.KrediKartiId is not null) e.Tip = GiderTipi.KrediKarti; // kart harcaması tutarlılığı
    if (IslemHatasi(db, e) is string hata) return Hata(hata);
    db.Islemler.Add(e); db.SaveChanges();
    return Results.Created($"/api/islemler/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/islemler/{id:int}", (int id, IslemEntity gelen, KasaDbContext db) =>
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
}).RequireAuthorization("Editor");
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
    var q = db.KartOdemeler.AsQueryable();
    if (krediKartiId is { } id) q = q.Where(o => o.KrediKartiId == id);
    return q.OrderByDescending(o => o.Tarih).ThenByDescending(o => o.Id).ToList();
});
api.MapPost("/kartodemeler", (KartOdemeEntity e, KasaDbContext db) =>
{
    e.Id = 0;
    if (e.Tutar <= 0) return Hata("Ödeme tutarı sıfırdan büyük olmalı.");
    if (!db.KrediKartlari.Any(k => k.Id == e.KrediKartiId)) return Hata("Kredi kartı bulunamadı.");
    db.KartOdemeler.Add(e); db.SaveChanges();
    return Results.Created($"/api/kartodemeler/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapDelete("/kartodemeler/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.KartOdemeler.Find(id);
    if (e is null) return Results.NotFound();
    db.KartOdemeler.Remove(e); db.SaveChanges();
    return Results.NoContent();
}).RequireAuthorization("Editor");

// Gelenler (dönem+kanal başına tek satır — upsert)
api.MapGet("/gelenler", (DateOnly? donemStart, KasaDbContext db) =>
{
    var q = db.Gelenler.AsQueryable();
    if (donemStart is { } d) q = q.Where(g => g.DonemStart == d);
    return q.ToList();
});
api.MapPut("/gelenler", (GelenUpsertDto dto, KasaDbContext db) =>
{
    if (dto.TutarTl < 0) return Hata("Gelen tutarı negatif olamaz.");
    if (!db.Kanallar.Any(k => k.Ad == dto.Kanal)) return Hata($"'{dto.Kanal}' adında bir kanal yok.");
    var e = db.Gelenler.FirstOrDefault(g => g.DonemStart == dto.DonemStart && g.Kanal == dto.Kanal);
    if (e is null)
    {
        e = new GelenEntity { DonemStart = dto.DonemStart, Kanal = dto.Kanal, TutarTl = dto.TutarTl };
        db.Gelenler.Add(e);
    }
    else
    {
        e.TutarTl = dto.TutarTl;
    }
    db.SaveChanges();
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
    var a = db.Ayarlar.First();
    using var tx = db.Database.BeginTransaction();
    if (dto.TakipBaslangic != a.TakipBaslangic)
        GelenleriDonemlereHizala(db, dto.TakipBaslangic);
    a.TakipBaslangic = dto.TakipBaslangic;
    a.KasaAcilisDevri = dto.KasaAcilisDevri;
    db.SaveChanges();
    tx.Commit();
    return Results.Ok();
}).RequireAuthorization("Editor");
api.MapPut("/ayarlar/izleyici-sifre", (IzleyiciSifreDto dto, KasaDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.YeniSifre))
        return Results.BadRequest(new { hata = "Şifre boş olamaz." });
    var a = db.Ayarlar.First();
    a.IzleyiciSifreHash = SifreHasher.Hashle(dto.YeniSifre);
    a.IzleyiciOturumSurumu++;   // eski izleyici oturumları kapanır
    db.SaveChanges();
    return Results.Ok();
}).RequireAuthorization("Editor");
api.MapPost("/ayarlar/oturumlari-kapat", (KasaDbContext db) =>
{
    // Tüm cihazlardaki oturumları (editör dahil) kapatır; herkes yeniden giriş yapar.
    var a = db.Ayarlar.First();
    a.IzleyiciOturumSurumu++;
    a.EditorOturumSurumu++;
    db.SaveChanges();
    return Results.Ok();
}).RequireAuthorization("Editor");

// Raporlar (okuma — her iki rol)
api.MapGet("/donemler", (HesapServisi svc) => svc.Donemler());
api.MapGet("/rapor/haftalik", (HesapServisi svc) => svc.Haftalik());
api.MapGet("/rapor/aylik", (int yil, int ay, HesapServisi svc) => svc.Aylik(yil, ay));
api.MapGet("/rapor/panel", (HesapServisi svc) => svc.Panel());

app.Run();

static int GecerliSurum(AyarEntity a, string? rol) => rol == "editor" ? a.EditorOturumSurumu : a.IzleyiciOturumSurumu;

static bool SabitZamanEsit(string? a, string b)
    => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(a ?? "")), SHA256.HashData(Encoding.UTF8.GetBytes(b)));

static IResult Hata(string mesaj) => Results.BadRequest(new { hata = mesaj });

static string? KanalAdHatasi(KasaDbContext db, string ad, int? haricId)
{
    if (ad.Length == 0) return "Kanal adı boş olamaz.";
    if (ad == Kanallar.Ortak) return $"'{Kanallar.Ortak}' ayrılmış bir addır.";
    if (db.Kanallar.Any(k => k.Ad == ad && k.Id != haricId)) return $"'{ad}' adında bir kanal zaten var.";
    return null;
}

static string? KartHatasi(KrediKartiEntity e)
{
    if (e.Ad.Length == 0) return "Kart adı boş olamaz.";
    if (e.Limit < 0) return "Limit negatif olamaz.";
    if (e.Borc < 0) return "Açılış borcu negatif olamaz.";
    return null;
}

static string? IslemHatasi(KasaDbContext db, IslemEntity e)
{
    if (e.TutarTl <= 0) return "Tutar sıfırdan büyük olmalı.";
    if (string.IsNullOrWhiteSpace(e.Cari)) return "Cari boş olamaz.";
    if (!Enum.IsDefined(e.Tip)) return "Geçersiz gider tipi.";
    if (e.Kanal != Kanallar.Ortak && !db.Kanallar.Any(k => k.Ad == e.Kanal))
        return $"'{e.Kanal}' adında bir kanal yok.";
    if (e.KrediKartiId is int kid && !db.KrediKartlari.Any(k => k.Id == kid))
        return "Kredi kartı bulunamadı.";
    return null;
}

// Takip başlangıcı değişince gelen kayıtlarını yeni dönem başlangıçlarına taşır; aynı
// döneme düşen iki kayıt (aynı kanal) toplanarak birleşir. Böylece upsert çift kayıt üretmez.
static void GelenleriDonemlereHizala(KasaDbContext db, DateOnly yeniBaslangic)
{
    var gelenler = db.Gelenler.Where(g => g.DonemStart >= yeniBaslangic).ToList();
    if (gelenler.Count == 0) return;
    var donemler = DonemUretici.Uret(yeniBaslangic, gelenler.Max(g => g.DonemStart));
    foreach (var grup in gelenler.GroupBy(g => (Start: donemler.First(d => d.Icerir(g.DonemStart)).Start, g.Kanal)))
    {
        var kalan = grup.OrderBy(g => g.Id).First();
        kalan.DonemStart = grup.Key.Start;
        kalan.TutarTl = grup.Sum(g => g.TutarTl);
        db.Gelenler.RemoveRange(grup.Where(g => g != kalan));
    }
}

public record AyarGuncelleDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri);

public partial class Program { }
