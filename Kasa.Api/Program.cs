using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Kasa.Api;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<KasaDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Kasa") ?? "Data Source=kasa.db"));

builder.Services.AddScoped<HesapServisi>();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var jwtKey = builder.Configuration["Kasa:JwtKey"] ?? "gelistirme-icin-varsayilan-anahtar-degistir!!";
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
            }
        };
    });
builder.Services.AddAuthorization(o =>
    o.AddPolicy("Editor", p => p.RequireRole("editor")));

var app = builder.Build();

// Üretimde (Caddy TLS arkasında) çerez yalnızca HTTPS'te gitmeli.
var cerezSecure = !app.Environment.IsDevelopment();

// --- DB başlat + seed ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
    db.Database.EnsureCreated();
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

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { durum = "ok" }));

// --- Auth ---
app.MapPost("/api/auth/login", (LoginDto dto, KasaDbContext db, IConfiguration cfg, HttpContext http) =>
{
    var editorKullanici = cfg["Kasa:EditorKullanici"];
    var editorSifre = cfg["Kasa:EditorSifre"];
    var key = cfg["Kasa:JwtKey"] ?? "gelistirme-icin-varsayilan-anahtar-degistir!!";

    string? rol = null;
    // Editör bilgileri config'te tanımlı DEĞİLSE editör girişi kapalıdır
    // (aksi halde eksik config null==null ile şifresiz editör erişimine yol açar).
    if (!string.IsNullOrEmpty(editorKullanici) && !string.IsNullOrEmpty(editorSifre)
        && dto.Kullanici == editorKullanici && dto.Sifre == editorSifre)
        rol = "editor";
    else
    {
        var ayar = db.Ayarlar.FirstOrDefault();
        if (ayar?.IzleyiciSifreHash is string h && SifreHasher.Dogrula(dto.Sifre, h))
            rol = "viewer";
    }

    if (rol is null) return Results.Unauthorized();

    var token = JwtYardimci.Uret(rol, key);
    http.Response.Cookies.Append("kasa_auth", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = cerezSecure,
        MaxAge = TimeSpan.FromDays(30),
    });
    return Results.Ok(new { rol });
});

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
    db.Kanallar.Add(e); db.SaveChanges();
    return Results.Created($"/api/kanallar/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/kanallar/{id:int}", (int id, KanalEntity gelen, KasaDbContext db) =>
{
    var e = db.Kanallar.Find(id);
    if (e is null) return Results.NotFound();
    e.Ad = gelen.Ad; e.Aktif = gelen.Aktif; e.Sira = gelen.Sira; e.AcilisDevri = gelen.AcilisDevri;
    db.SaveChanges();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/kanallar/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.Kanallar.Find(id);
    if (e is null) return Results.NotFound();
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
    db.Cariler.Add(e); db.SaveChanges();
    return Results.Created($"/api/cariler/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/cariler/{id:int}", (int id, CariEntity gelen, KasaDbContext db) =>
{
    var e = db.Cariler.Find(id);
    if (e is null) return Results.NotFound();
    e.Ad = gelen.Ad; e.Aktif = gelen.Aktif;
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

// Kredi kartları
api.MapGet("/kredikartlari", (KasaDbContext db) => db.KrediKartlari.OrderBy(k => k.Ad).ToList());
api.MapPost("/kredikartlari", (KrediKartiEntity e, KasaDbContext db) =>
{
    db.KrediKartlari.Add(e); db.SaveChanges();
    return Results.Created($"/api/kredikartlari/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/kredikartlari/{id:int}", (int id, KrediKartiEntity gelen, KasaDbContext db) =>
{
    var e = db.KrediKartlari.Find(id);
    if (e is null) return Results.NotFound();
    e.Ad = gelen.Ad; e.KesimTarihi = gelen.KesimTarihi; e.SonOdemeTarihi = gelen.SonOdemeTarihi;
    e.Limit = gelen.Limit; e.Borc = gelen.Borc;
    db.SaveChanges();
    return Results.Ok(e);
}).RequireAuthorization("Editor");
api.MapDelete("/kredikartlari/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.KrediKartlari.Find(id);
    if (e is null) return Results.NotFound();
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
    db.Islemler.Add(e); db.SaveChanges();
    return Results.Created($"/api/islemler/{e.Id}", e);
}).RequireAuthorization("Editor");
api.MapPut("/islemler/{id:int}", (int id, IslemEntity gelen, KasaDbContext db) =>
{
    var e = db.Islemler.Find(id);
    if (e is null) return Results.NotFound();
    e.Tarih = gelen.Tarih; e.Cari = gelen.Cari; e.TutarTl = gelen.TutarTl;
    e.Kanal = gelen.Kanal; e.Tip = gelen.Tip; e.Not = gelen.Not;
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

// Gelenler (dönem+kanal başına tek satır — upsert)
api.MapGet("/gelenler", (DateOnly? donemStart, KasaDbContext db) =>
{
    var q = db.Gelenler.AsQueryable();
    if (donemStart is { } d) q = q.Where(g => g.DonemStart == d);
    return q.ToList();
});
api.MapPut("/gelenler", (GelenUpsertDto dto, KasaDbContext db) =>
{
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
    a.TakipBaslangic = dto.TakipBaslangic;
    a.KasaAcilisDevri = dto.KasaAcilisDevri;
    db.SaveChanges();
    return Results.Ok();
}).RequireAuthorization("Editor");
api.MapPut("/ayarlar/izleyici-sifre", (IzleyiciSifreDto dto, KasaDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(dto.YeniSifre))
        return Results.BadRequest(new { hata = "Şifre boş olamaz." });
    var a = db.Ayarlar.First();
    a.IzleyiciSifreHash = SifreHasher.Hashle(dto.YeniSifre);
    db.SaveChanges();
    return Results.Ok();
}).RequireAuthorization("Editor");

// Raporlar (okuma — her iki rol)
api.MapGet("/donemler", (HesapServisi svc) => svc.Donemler());
api.MapGet("/rapor/haftalik", (HesapServisi svc) => svc.Haftalik());
api.MapGet("/rapor/aylik", (int yil, int ay, HesapServisi svc) => svc.Aylik(yil, ay));
api.MapGet("/rapor/panel", (HesapServisi svc) => svc.Panel());

// React istemci-tarafı rotaları (/haftalik, /aylik, ...) index.html'e düşer.
// /api ve /health zaten eşleştiği için buraya gelmez; eşleşmeyen /api/* için
// aşağıdaki guard 404 üretir (HTML fallback yerine).
app.MapFallbackToFile("index.html");

app.Run();

public record AyarGuncelleDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri);

public partial class Program { }
