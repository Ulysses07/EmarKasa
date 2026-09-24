# Kasa Kalıcılık + API + Auth Implementation Plan

> **Tarihsel plan:** Bu belge yazıldığı günün planıdır; içindeki `EnsureCreated`/elle SQL/DB yeniden oluşturma adımları artık geçersizdir — şema açılışta `SemaGuncelleyici` ile güncellenir, dağıtım/yedek için `deploy/README.md`'ye bakın.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kasa defterine SQLite kalıcılık, REST API ve basit rol tabanlı kimlik doğrulama (editör/izleyici) ekle; mevcut `Kasa.Core` hesap motorunu API üzerinden servis et.

**Architecture:** ASP.NET Core minimal API (`Kasa.Api`), `Kasa.Core`'a başvurur. EF Core 10 + SQLite tek dosya DB. EF entity sınıfları (Id'li, değişebilir) DB'de tutulur; hesap yapılırken `Kasa.Core`'un değişmez `record`'larına eşlenip `HesapMotoru`'na verilir. Kimlik: JWT (HttpOnly cookie). Editör kullanıcı/şifre + JWT anahtarı **config'ten** (appsettings/env), izleyici şifresi **Ayarlar tablosunda PBKDF2 hash'li**. Mutasyon uçları editör rolü ister; okuma/rapor uçları oturum açmış olmayı yeter sayar.

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core minimal API, EF Core 10 (Sqlite), JWT bearer, xUnit + `WebApplicationFactory` entegrasyon testleri.

Bu, 4 planlık setin **2. planıdır** (1: hesap motoru ✅; 3: React arayüz ✅ mock veriyle; 4: dağıtım). Bu plan **yalnız backend**tir — React'i gerçek API'ye bağlamak kapsam dışıdır (ayrı iş). Tasarım: `docs/specs/2026-07-13-kasa-defteri-design.md`.

**Kararlar (kullanıcı onaylı 2026-07-13):**
- Editör giriş bilgileri + JWT anahtarı config'te; izleyici şifresi Ayarlar tablosunda hash'li.
- Plan 2 sadece backend; React mock veriyle kalır.

---

## Dosya Yapısı

- `Kasa.Api/Kasa.Api.csproj` — ASP.NET Core Web (Sdk.Web), `net10.0`, `Kasa.Core`'a başvurur
- `Kasa.Api/Program.cs` — uygulama kurulumu: DB, auth, seed, endpoint grupları
- `Kasa.Api/Data/Entities.cs` — EF entity sınıfları (KanalEntity, CariEntity, IslemEntity, GelenEntity, AyarEntity)
- `Kasa.Api/Data/KasaDbContext.cs` — DbContext
- `Kasa.Api/Data/CoreMapping.cs` — Entity ↔ `Kasa.Core` record eşlemeleri
- `Kasa.Api/Auth/SifreHasher.cs` — PBKDF2 şifre hash/doğrula
- `Kasa.Api/Auth/JwtYardimci.cs` — JWT üretimi
- `Kasa.Api/Servisler/HesapServisi.cs` — DB'den yükle + `HesapMotoru`'nu çağır (haftalık/aylık/panel/dönemler)
- `Kasa.Api/Dtos.cs` — istek/yanıt DTO'ları (LoginDto, GelenUpsertDto, PanelDto, KanalBakiye)
- `Kasa.Api.Tests/Kasa.Api.Tests.csproj` — entegrasyon test projesi
- `Kasa.Api.Tests/KasaWebFactory.cs` — SQLite in-memory + test config ile `WebApplicationFactory`
- `Kasa.Api.Tests/SifreHasherTests.cs`
- `Kasa.Api.Tests/CoreMappingTests.cs`
- `Kasa.Api.Tests/AuthTests.cs`
- `Kasa.Api.Tests/CrudTests.cs`
- `Kasa.Api.Tests/RaporTests.cs`

**Genel konvansiyonlar (tüm task'lar için):**
- Namespace: uygulama kodu `Kasa.Api` (+ alt: `Kasa.Api.Data`, `Kasa.Api.Auth`, `Kasa.Api.Servisler`). Test namespace `Kasa.Api.Tests`.
- Para `decimal`, tarih `DateOnly` (Core ile birebir).
- Her task sonunda commit. Commit mesajları Türkçe, imperative; sonuna
  `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>` ekle.
- Komutlar repo kökünde çalışır: `<repo>`.

---

### Task 0: API + test projelerini oluştur ve solution'a ekle

**Files:**
- Create: `Kasa.Api/Kasa.Api.csproj`, `Kasa.Api/Program.cs`, `Kasa.Api.Tests/Kasa.Api.Tests.csproj`
- Modify: `Kasa.slnx`
- Test: `Kasa.Api.Tests/HealthTests.cs`

- [ ] **Step 1: Projeleri oluştur ve referansları bağla**

Run:
```bash
dotnet new web -n Kasa.Api -f net10.0
dotnet new xunit -n Kasa.Api.Tests -f net10.0
dotnet sln add Kasa.Api/Kasa.Api.csproj Kasa.Api.Tests/Kasa.Api.Tests.csproj
dotnet add Kasa.Api/Kasa.Api.csproj reference Kasa.Core/Kasa.Core.csproj
dotnet add Kasa.Api.Tests/Kasa.Api.Tests.csproj reference Kasa.Api/Kasa.Api.csproj
rm Kasa.Api.Tests/UnitTest1.cs
```

- [ ] **Step 2: Paketleri ekle**

Run:
```bash
dotnet add Kasa.Api/Kasa.Api.csproj package Microsoft.EntityFrameworkCore.Sqlite
dotnet add Kasa.Api/Kasa.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add Kasa.Api.Tests/Kasa.Api.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add Kasa.Api.Tests/Kasa.Api.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite
```
Not: Restore paket sürümü bulamazsa aynı komutu `--prerelease` ile tekrar dene. `net10.0` GA olduğundan stable sürümler beklenir.

- [ ] **Step 3: `Program.cs`'i sadeleştir (health smoke)**

`Kasa.Api/Program.cs` (şablonu tümüyle bununla değiştir):
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { durum = "ok" }));

app.Run();

// Entegrasyon testlerinin WebApplicationFactory<Program> kullanabilmesi için:
public partial class Program { }
```

- [ ] **Step 4: Health testini yaz**

`Kasa.Api.Tests/HealthTests.cs`:
```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kasa.Api.Tests;

public class HealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public HealthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_ok_doner()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.True(resp.IsSuccessStatusCode);
    }
}
```

- [ ] **Step 5: Derle ve testi çalıştır**

Run: `dotnet build` sonra `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj`
Expected: build succeeded; 1 test PASS.

- [ ] **Step 6: Commit**

```bash
git add Kasa.slnx Kasa.Api Kasa.Api.Tests
git commit -m "chore(api): Kasa.Api + test projeleri iskeleti (health smoke)"
```

---

### Task 1: EF entity'leri, DbContext ve Core eşlemesi

`Kasa.Core` record'ları Id'siz ve değişmez; DB için Id'li entity sınıfları ayrı tutulur ve hesap yapılırken Core record'larına eşlenir.

**Files:**
- Create: `Kasa.Api/Data/Entities.cs`, `Kasa.Api/Data/KasaDbContext.cs`, `Kasa.Api/Data/CoreMapping.cs`
- Test: `Kasa.Api.Tests/CoreMappingTests.cs`

- [ ] **Step 1: Eşleme için failing test yaz**

`Kasa.Api.Tests/CoreMappingTests.cs`:
```csharp
using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api.Tests;

public class CoreMappingTests
{
    [Fact]
    public void IslemEntity_core_islem_e_donusur()
    {
        var e = new IslemEntity
        {
            Id = 5,
            Tarih = new DateOnly(2026, 6, 30),
            Cari = "PORT KARGO",
            TutarTl = 3874.03m,
            Kanal = "MEZAT",
            Tip = GiderTipi.Cari,
            Not = null,
        };

        Islem core = e.ToCore();

        Assert.Equal(new DateOnly(2026, 6, 30), core.Tarih);
        Assert.Equal("PORT KARGO", core.Cari);
        Assert.Equal(3874.03m, core.TutarTl);
        Assert.Equal("MEZAT", core.Kanal);
        Assert.Equal(GiderTipi.Cari, core.Tip);
    }

    [Fact]
    public void KanalEntity_ve_GelenEntity_core_e_donusur()
    {
        var k = new KanalEntity { Id = 1, Ad = "TOPTAN", Aktif = false, Sira = 2, AcilisDevri = 100m };
        Kanal ck = k.ToCore();
        Assert.Equal("TOPTAN", ck.Ad);
        Assert.False(ck.Aktif);
        Assert.Equal(2, ck.Sira);
        Assert.Equal(100m, ck.AcilisDevri);

        var g = new GelenEntity { Id = 3, DonemStart = new DateOnly(2026, 6, 29), Kanal = "MEZAT", TutarTl = 289_425m };
        Gelen cg = g.ToCore();
        Assert.Equal(new DateOnly(2026, 6, 29), cg.DonemStart);
        Assert.Equal("MEZAT", cg.Kanal);
        Assert.Equal(289_425m, cg.TutarTl);
    }
}
```

- [ ] **Step 2: Test başarısız olsun (derlenmez)**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter CoreMappingTests`
Expected: derleme hatası — `IslemEntity`, `ToCore` yok.

- [ ] **Step 3: Entity'leri yaz**

`Kasa.Api/Data/Entities.cs`:
```csharp
using Kasa.Core;

namespace Kasa.Api.Data;

public class KanalEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
    public int Sira { get; set; }
    public decimal AcilisDevri { get; set; }
}

public class CariEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
}

public class IslemEntity
{
    public int Id { get; set; }
    public DateOnly Tarih { get; set; }
    public string Cari { get; set; } = "";
    public decimal TutarTl { get; set; }
    public string Kanal { get; set; } = "";
    public GiderTipi Tip { get; set; }
    public string? Not { get; set; }
}

public class GelenEntity
{
    public int Id { get; set; }
    public DateOnly DonemStart { get; set; }
    public string Kanal { get; set; } = "";
    public decimal TutarTl { get; set; }
}

/// <summary>Tek satırlık uygulama ayarları.</summary>
public class AyarEntity
{
    public int Id { get; set; }
    public DateOnly TakipBaslangic { get; set; }
    public decimal KasaAcilisDevri { get; set; }
    public string? IzleyiciSifreHash { get; set; }
}
```

- [ ] **Step 4: DbContext'i yaz**

`Kasa.Api/Data/KasaDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public class KasaDbContext : DbContext
{
    public KasaDbContext(DbContextOptions<KasaDbContext> options) : base(options) { }

    public DbSet<KanalEntity> Kanallar => Set<KanalEntity>();
    public DbSet<CariEntity> Cariler => Set<CariEntity>();
    public DbSet<IslemEntity> Islemler => Set<IslemEntity>();
    public DbSet<GelenEntity> Gelenler => Set<GelenEntity>();
    public DbSet<AyarEntity> Ayarlar => Set<AyarEntity>();
}
```

- [ ] **Step 5: Eşlemeleri yaz**

`Kasa.Api/Data/CoreMapping.cs`:
```csharp
using Kasa.Core;

namespace Kasa.Api.Data;

/// <summary>EF entity'lerini hesap motorunun beklediği Core record'larına eşler.</summary>
public static class CoreMapping
{
    public static Kanal ToCore(this KanalEntity e) => new(e.Ad, e.AcilisDevri, e.Aktif, e.Sira);
    public static Cari ToCore(this CariEntity e) => new(e.Ad, e.Aktif);
    public static Islem ToCore(this IslemEntity e) => new(e.Tarih, e.Cari, e.TutarTl, e.Kanal, e.Tip, e.Not);
    public static Gelen ToCore(this GelenEntity e) => new(e.DonemStart, e.Kanal, e.TutarTl);
}
```

- [ ] **Step 6: Test geçsin**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter CoreMappingTests`
Expected: PASS (2 test).

- [ ] **Step 7: Commit**

```bash
git add Kasa.Api/Data Kasa.Api.Tests/CoreMappingTests.cs
git commit -m "feat(api): EF entity'leri, DbContext ve Core eşlemesi"
```

---

### Task 2: PBKDF2 şifre hash yardımcısı

İzleyici şifresi düz metin tutulmaz; PBKDF2 ile hash'lenir. Harici bağımlılık yok (`System.Security.Cryptography`).

**Files:**
- Create: `Kasa.Api/Auth/SifreHasher.cs`
- Test: `Kasa.Api.Tests/SifreHasherTests.cs`

- [ ] **Step 1: Failing test yaz**

`Kasa.Api.Tests/SifreHasherTests.cs`:
```csharp
using Kasa.Api.Auth;

namespace Kasa.Api.Tests;

public class SifreHasherTests
{
    [Fact]
    public void Dogru_sifre_dogrulanir_yanlis_reddedilir()
    {
        var hash = SifreHasher.Hashle("gizli123");

        Assert.True(SifreHasher.Dogrula("gizli123", hash));
        Assert.False(SifreHasher.Dogrula("yanlis", hash));
    }

    [Fact]
    public void Ayni_sifre_farkli_salt_ile_farkli_hash_uretir()
    {
        var h1 = SifreHasher.Hashle("aynisifre");
        var h2 = SifreHasher.Hashle("aynisifre");

        Assert.NotEqual(h1, h2);                       // rastgele salt
        Assert.True(SifreHasher.Dogrula("aynisifre", h1));
        Assert.True(SifreHasher.Dogrula("aynisifre", h2));
    }

    [Fact]
    public void Bozuk_hash_dogrulamada_false_doner()
    {
        Assert.False(SifreHasher.Dogrula("x", "bozuk-veri"));
    }
}
```

- [ ] **Step 2: Test başarısız olsun**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter SifreHasherTests`
Expected: derleme hatası — `SifreHasher` yok.

- [ ] **Step 3: SifreHasher'ı yaz**

`Kasa.Api/Auth/SifreHasher.cs`:
```csharp
using System.Security.Cryptography;

namespace Kasa.Api.Auth;

/// <summary>PBKDF2 (SHA256) tabanlı şifre hash'leme. Format: base64(salt).base64(hash).</summary>
public static class SifreHasher
{
    private const int SaltBoyutu = 16;
    private const int HashBoyutu = 32;
    private const int Iterasyon = 100_000;

    public static string Hashle(string sifre)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBoyutu);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(sifre, salt, Iterasyon, HashAlgorithmName.SHA256, HashBoyutu);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Dogrula(string sifre, string kayitliHash)
    {
        var parcalar = kayitliHash.Split('.');
        if (parcalar.Length != 2) return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parcalar[0]);
            byte[] beklenen = Convert.FromBase64String(parcalar[1]);
            byte[] gelen = Rfc2898DeriveBytes.Pbkdf2(sifre, salt, Iterasyon, HashAlgorithmName.SHA256, beklenen.Length);
            return CryptographicOperations.FixedTimeEquals(gelen, beklenen);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Test geçsin**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter SifreHasherTests`
Expected: PASS (3 test).

- [ ] **Step 5: Commit**

```bash
git add Kasa.Api/Auth/SifreHasher.cs Kasa.Api.Tests/SifreHasherTests.cs
git commit -m "feat(api): PBKDF2 şifre hash yardımcısı"
```

---

### Task 3: Uygulama kurulumu — DB, seed, JWT auth, endpoint'ler

Bu task `Program.cs`'i tam hâline getirir: DB kaydı + başlangıç seed, JWT cookie auth, login/logout/me, CRUD uçları, rapor uçları. Yardımcılar (`JwtYardimci`, `HesapServisi`, DTO'lar) da burada eklenir. Testler Task 4-5-6'da.

**Files:**
- Create: `Kasa.Api/Auth/JwtYardimci.cs`, `Kasa.Api/Servisler/HesapServisi.cs`, `Kasa.Api/Dtos.cs`
- Modify: `Kasa.Api/Program.cs`, `Kasa.Api/appsettings.json`

- [ ] **Step 1: DTO'ları yaz**

`Kasa.Api/Dtos.cs`:
```csharp
namespace Kasa.Api;

public record LoginDto(string? Kullanici, string Sifre);
public record GelenUpsertDto(DateOnly DonemStart, string Kanal, decimal TutarTl);
public record IzleyiciSifreDto(string YeniSifre);

public record KanalBakiye(string Kanal, decimal Bakiye);
public record PanelDto(
    decimal GuncelKasa,
    IReadOnlyList<KanalBakiye> Kanallar,
    decimal BuHaftaSonucu,
    decimal BuAySonucu);
```

- [ ] **Step 2: JWT yardımcısını yaz**

`Kasa.Api/Auth/JwtYardimci.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Kasa.Api.Auth;

public static class JwtYardimci
{
    public static string Uret(string rol, string jwtKey)
    {
        var anahtar = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var kimlik = new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: new[] { new Claim(ClaimTypes.Role, rol) },
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: kimlik);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 3: HesapServisi'ni yaz**

`Kasa.Api/Servisler/HesapServisi.cs`:
```csharp
using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api.Servisler;

/// <summary>DB'den veriyi yükler, dönem takvimini üretir ve HesapMotoru'nu çağırır.</summary>
public class HesapServisi
{
    private readonly KasaDbContext _db;
    public HesapServisi(KasaDbContext db) => _db = db;

    private record Yuk(
        IReadOnlyList<Kanal> Kanallar,
        IReadOnlyList<Islem> Islemler,
        IReadOnlyList<Gelen> Gelenler,
        IReadOnlyList<Donem> Donemler,
        decimal KasaAcilis);

    private Yuk Yukle()
    {
        var kanallar = _db.Kanallar.OrderBy(k => k.Sira).ToList().Select(e => e.ToCore()).ToList();
        var islemler = _db.Islemler.ToList().Select(e => e.ToCore()).ToList();
        var gelenler = _db.Gelenler.ToList().Select(e => e.ToCore()).ToList();
        var ayar = _db.Ayarlar.First();

        var baslangic = ayar.TakipBaslangic;
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        var enGecIslem = islemler.Select(i => i.Tarih).DefaultIfEmpty(bugun).Max();
        var bitis = new[] { bugun, enGecIslem, baslangic }.Max();

        var donemler = DonemUretici.Uret(baslangic, bitis);
        return new Yuk(kanallar, islemler, gelenler, donemler, ayar.KasaAcilisDevri);
    }

    public IReadOnlyList<HaftalikOzet> Haftalik()
    {
        var y = Yukle();
        return HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
    }

    public AylikRapor Aylik(int yil, int ay)
    {
        var y = Yukle();
        return HesapMotoru.AylikHesapla(yil, ay, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
    }

    public IReadOnlyList<Donem> Donemler() => Yukle().Donemler;

    public PanelDto Panel()
    {
        var y = Yukle();
        var haftalik = HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        var son = haftalik.Count > 0 ? haftalik[^1] : null;

        var guncelKasa = son?.KasaDevir ?? y.KasaAcilis;
        var buHafta = son?.KasaSonucu ?? 0m;
        var kanalBakiyeleri = son is not null
            ? son.Kanallar.Select(k => new KanalBakiye(k.Kanal, k.Devir)).ToList()
            : y.Kanallar.Select(k => new KanalBakiye(k.Ad, k.AcilisDevri)).ToList();

        var bugun = DateOnly.FromDateTime(DateTime.Today);
        var buAyRapor = HesapMotoru.AylikHesapla(bugun.Year, bugun.Month, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        var buAy = buAyRapor.Kanallar.Sum(k => k.AySonucu);

        return new PanelDto(guncelKasa, kanalBakiyeleri, buHafta, buAy);
    }
}
```

- [ ] **Step 4: `Program.cs`'i tam hâline getir**

`Kasa.Api/Program.cs` (health sürümünü tümüyle bununla değiştir):
```csharp
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
    if (dto.Kullanici is not null && dto.Kullanici == editorKullanici && dto.Sifre == editorSifre)
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
        Secure = false, // prod'da Caddy TLS arkasında true'ya çevrilebilir
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

app.Run();

public record AyarGuncelleDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri);

public partial class Program { }
```

- [ ] **Step 5: appsettings'e editör/JWT alanlarını ekle**

`Kasa.Api/appsettings.json` (tüm dosya):
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Kasa": "Data Source=kasa.db"
  },
  "Kasa": {
    "EditorKullanici": "editor",
    "EditorSifre": "degistir-beni",
    "JwtKey": "gelistirme-icin-varsayilan-anahtar-en-az-32-bayt!!"
  }
}
```
Not: prod'da `EditorSifre` ve `JwtKey` env değişkeniyle (`Kasa__EditorSifre`, `Kasa__JwtKey`) override edilir; bu değerler yalnız yerel geliştirme içindir.

- [ ] **Step 6: Derle**

Run: `dotnet build Kasa.Api/Kasa.Api.csproj`
Expected: `Build succeeded`, 0 error. (SQLite decimal için uyarı çıkabilir — hata değil.)

- [ ] **Step 7: Commit**

```bash
git add Kasa.Api/Program.cs Kasa.Api/Dtos.cs Kasa.Api/Auth/JwtYardimci.cs Kasa.Api/Servisler/HesapServisi.cs Kasa.Api/appsettings.json
git commit -m "feat(api): DB seed, JWT cookie auth, CRUD ve rapor uçları"
```

---

### Task 4: Test altyapısı + auth entegrasyon testleri

`WebApplicationFactory`'yi SQLite in-memory (açık tutulan bağlantı) ve test config'iyle özelleştir; login akışını test et.

**Files:**
- Create: `Kasa.Api.Tests/KasaWebFactory.cs`, `Kasa.Api.Tests/AuthTests.cs`

- [ ] **Step 1: Test factory'sini yaz**

`Kasa.Api.Tests/KasaWebFactory.cs`:
```csharp
using System.Collections.Generic;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Testler için uygulamayı açık tutulan bir SQLite in-memory bağlantısıyla
/// (kalıcı şema) ve sabit editör/JWT config'iyle ayağa kaldırır.
/// </summary>
public class KasaWebFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _conn.Open(); // bağlantı açık kaldıkça in-memory DB yaşar

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kasa:EditorKullanici"] = "editor",
                ["Kasa:EditorSifre"] = "kasa123",
                ["Kasa:JwtKey"] = "test-jwt-anahtari-en-az-32-bayt-olmali!!",
            });
        });

        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<KasaDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<KasaDbContext>(o => o.UseSqlite(_conn));
        });
    }

    /// <summary>Editör olarak login olmuş bir HttpClient döner (auth cookie set).</summary>
    public async Task<HttpClient> EditorClientAsync()
    {
        var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "kasa123" });
        resp.EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _conn.Dispose();
    }
}
```
Not: `PostAsJsonAsync` için `using System.Net.Http.Json;` gerekir — dosyanın başına ekle.

- [ ] **Step 2: Auth testlerini yaz**

`Kasa.Api.Tests/AuthTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class AuthTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public AuthTests(KasaWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Dogru_editor_giris_200_ve_cookie_doner()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "kasa123" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(resp.Headers.GetValues("Set-Cookie"), v => v.StartsWith("kasa_auth="));
    }

    [Fact]
    public async Task Yanlis_sifre_401_doner()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "yanlis" });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Me_giris_yapmadan_401_giris_yapinca_rol_doner()
    {
        var anonim = _factory.CreateClient();
        var anonimResp = await anonim.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonimResp.StatusCode);

        var editor = await _factory.EditorClientAsync();
        var me = await editor.GetFromJsonAsync<RolYanit>("/api/auth/me");
        Assert.Equal("editor", me!.Rol);
    }

    private record RolYanit(string Rol);
}
```
Not: JSON alan adı camelCase serileşir (`rol`); record `Rol` ile eşleşmesi için System.Text.Json varsayılan case-insensitive bağlaması yeterlidir (`GetFromJsonAsync` varsayılanı case-insensitive).

- [ ] **Step 3: Testleri çalıştır**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter AuthTests`
Expected: PASS (3 test). Başarısızsa cookie/JWT kurulumunu (`OnMessageReceived`, `RequireAuthorization`) gözden geçir.

- [ ] **Step 4: Commit**

```bash
git add Kasa.Api.Tests/KasaWebFactory.cs Kasa.Api.Tests/AuthTests.cs
git commit -m "test(api): test factory + auth entegrasyon testleri"
```

---

### Task 5: CRUD + yetkilendirme entegrasyon testleri

**Files:**
- Create: `Kasa.Api.Tests/CrudTests.cs`

- [ ] **Step 1: CRUD testlerini yaz**

`Kasa.Api.Tests/CrudTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Kasa.Core;

namespace Kasa.Api.Tests;

public class CrudTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public CrudTests(KasaWebFactory factory) => _factory = factory;

    private record IslemYanit(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not);

    [Fact]
    public async Task Editor_islem_ekleyip_listeleyip_silebilir()
    {
        var client = await _factory.EditorClientAsync();

        var olustur = await client.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-06-29",
            cari = "PORT KARGO",
            tutarTl = 3874.03m,
            kanal = "MEZAT",
            tip = "Cari",
            not = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, olustur.StatusCode);
        var eklenen = await olustur.Content.ReadFromJsonAsync<IslemYanit>();
        Assert.NotNull(eklenen);
        Assert.Equal("PORT KARGO", eklenen!.Cari);
        Assert.Equal(GiderTipi.Cari, eklenen.Tip);

        var liste = await client.GetFromJsonAsync<List<IslemYanit>>("/api/islemler");
        Assert.Contains(liste!, i => i.Id == eklenen.Id);

        var sil = await client.DeleteAsync($"/api/islemler/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);

        var listeSonra = await client.GetFromJsonAsync<List<IslemYanit>>("/api/islemler");
        Assert.DoesNotContain(listeSonra!, i => i.Id == eklenen.Id);
    }

    [Fact]
    public async Task Izleyici_mutasyon_yapamaz_403()
    {
        // İzleyici şifresini editör olarak ayarla, sonra izleyici olarak login ol.
        var editor = await _factory.EditorClientAsync();
        var setSifre = await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" });
        setSifre.EnsureSuccessStatusCode();

        var izleyici = _factory.CreateClient();
        var giris = await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" });
        giris.EnsureSuccessStatusCode();

        // Okuma serbest:
        var okuma = await izleyici.GetAsync("/api/islemler");
        Assert.Equal(HttpStatusCode.OK, okuma.StatusCode);

        // Yazma yasak:
        var yazma = await izleyici.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-06-29", cari = "X", tutarTl = 1m, kanal = "MEZAT", tip = "Cari", not = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Forbidden, yazma.StatusCode);
    }

    [Fact]
    public async Task Gelen_upsert_ayni_donem_kanal_icin_gunceller()
    {
        var client = await _factory.EditorClientAsync();

        await client.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-29", kanal = "MEZAT", tutarTl = 100m });
        await client.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-29", kanal = "MEZAT", tutarTl = 289_425m });

        var liste = await client.GetFromJsonAsync<List<GelenYanit>>("/api/gelenler?donemStart=2026-06-29");
        var mezat = liste!.Where(g => g.Kanal == "MEZAT").ToList();
        Assert.Single(mezat);                       // upsert: tek satır
        Assert.Equal(289_425m, mezat[0].TutarTl);   // güncellenmiş değer
    }

    private record GelenYanit(int Id, DateOnly DonemStart, string Kanal, decimal TutarTl);
}
```

- [ ] **Step 2: Testleri çalıştır**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter CrudTests`
Expected: PASS (3 test). 403 yerine 401 alıyorsan izleyici cookie'si düşmüş demektir — login yanıtındaki Set-Cookie'nin HttpClient handler'ında taşındığını doğrula (`CreateClient` varsayılan `HandleCookies = true`).

- [ ] **Step 3: Commit**

```bash
git add Kasa.Api.Tests/CrudTests.cs
git commit -m "test(api): CRUD + rol yetkilendirme entegrasyon testleri"
```

---

### Task 6: Rapor uçları — uçtan uca doğrulama

Bilinen Haziran rakamlarını (spec §4.1) DB'ye tohumlayıp `/api/rapor/haftalik` çıktısını doğrula.

**Files:**
- Create: `Kasa.Api.Tests/RaporTests.cs`

- [ ] **Step 1: Rapor testini yaz**

`Kasa.Api.Tests/RaporTests.cs`:
```csharp
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class RaporTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public RaporTests(KasaWebFactory factory) => _factory = factory;

    private record KanalHaftalikYanit(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir);
    private record HaftalikOzetYanit(Donem Donem, List<KanalHaftalikYanit> Kanallar, decimal ToplamGelen, decimal ToplamGiden, decimal KasaSonucu, decimal KasaDevir);

    private void Tohumla()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();

        // Seed'den gelen varsayılan kanalları Haziran açılış devirleriyle güncelle.
        db.Kanallar.RemoveRange(db.Kanallar);
        db.Kanallar.AddRange(
            new KanalEntity { Ad = "MEZAT", Sira = 0, AcilisDevri = 4_991_052m },
            new KanalEntity { Ad = "PERAKENDE", Sira = 1, AcilisDevri = 2_013_516m },
            new KanalEntity { Ad = "TOPTAN", Sira = 2, AcilisDevri = 619_647m });

        var ayar = db.Ayarlar.First();
        ayar.TakipBaslangic = new DateOnly(2026, 6, 29);
        ayar.KasaAcilisDevri = 2_907_053.21m;

        db.Gelenler.AddRange(
            new GelenEntity { DonemStart = new DateOnly(2026, 6, 29), Kanal = "MEZAT", TutarTl = 289_425m },
            new GelenEntity { DonemStart = new DateOnly(2026, 6, 29), Kanal = "PERAKENDE", TutarTl = 271_006m },
            new GelenEntity { DonemStart = new DateOnly(2026, 6, 29), Kanal = "TOPTAN", TutarTl = 207_000m });

        db.Islemler.AddRange(
            new IslemEntity { Tarih = new DateOnly(2026, 6, 29), Cari = "MEZAT-cari", TutarTl = 1_308_800m, Kanal = "MEZAT", Tip = GiderTipi.Cari },
            new IslemEntity { Tarih = new DateOnly(2026, 6, 29), Cari = "PER-cari", TutarTl = 1_221_374m, Kanal = "PERAKENDE", Tip = GiderTipi.Cari },
            new IslemEntity { Tarih = new DateOnly(2026, 6, 29), Cari = "TOP-cari", TutarTl = 360_000m, Kanal = "TOPTAN", Tip = GiderTipi.Cari },
            new IslemEntity { Tarih = new DateOnly(2026, 6, 30), Cari = "SGK/Vergi", TutarTl = 455_321m, Kanal = Kanallar.Ortak, Tip = GiderTipi.SabitGider });

        db.SaveChanges();
    }

    [Fact]
    public async Task Haftalik_rapor_haziran_excel_rakamlarini_uretir()
    {
        Tohumla();
        var client = await _factory.EditorClientAsync();

        var ozetler = await client.GetFromJsonAsync<List<HaftalikOzetYanit>>("/api/rapor/haftalik");
        var d = ozetler!.Single(o => o.Donem.Start == new DateOnly(2026, 6, 29));

        var mezat = d.Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(-1_019_375m, mezat.Sonuc);
        Assert.Equal(3_971_677m, mezat.Devir);

        Assert.Equal(767_431m, d.ToplamGelen);
        Assert.Equal(3_345_495m, d.ToplamGiden);
        Assert.Equal(328_989.21m, d.KasaDevir);
    }

    [Fact]
    public async Task Panel_guncel_kasayi_ve_kanal_bakiyelerini_doner()
    {
        Tohumla();
        var client = await _factory.EditorClientAsync();

        var panel = await client.GetFromJsonAsync<PanelDto>("/api/rapor/panel");
        Assert.NotNull(panel);
        // 29-30 Haziran sonrası boş dönemler kasayı değiştirmez.
        Assert.Equal(328_989.21m, panel!.GuncelKasa);
        Assert.Equal(3_971_677m, panel.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye);
    }
}
```
Not: `RaporTests` `IClassFixture<KasaWebFactory>` paylaşır; `Tohumla` her testte veriyi yeniden kurar (kanalları silip ekler, gelen/işlem ekler). İki test aynı DB'yi paylaştığından, `Tohumla` içinde `Gelenler`/`Islemler` için de `RemoveRange` ile temizle: metodun başına
```csharp
db.Islemler.RemoveRange(db.Islemler);
db.Gelenler.RemoveRange(db.Gelenler);
```
ekle (kanalların zaten `RemoveRange`'i var). Böylece testler sıradan bağımsız olur.

- [ ] **Step 2: Testleri çalıştır**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter RaporTests`
Expected: PASS (2 test). `PanelDto` bağlaması için alan adları camelCase gelir; `GetFromJsonAsync` case-insensitive eşler.

- [ ] **Step 3: Tüm testleri çalıştır**

Run: `dotnet test`
Expected: `Kasa.Core.Tests` (11) + `Kasa.Api.Tests` (14) tümü PASS.

- [ ] **Step 4: Commit**

```bash
git add Kasa.Api.Tests/RaporTests.cs
git commit -m "test(api): rapor uçları uçtan uca Haziran doğrulaması"
```

---

## Self-Review (yazar kontrolü — tamamlandı)

**Spec kapsamı:** §3 veri modeli → Task 1 (entity + eşleme). §6 giriş/roller → Task 2-3-4 (PBKDF2, JWT cookie, editör/izleyici, mobil salt-görüntüleme spec'i UI'da; API iki rolü ayırır). §7 mimari (ASP.NET Core + SQLite + REST) → Task 0-3. §5 ekranların ihtiyacı olan CRUD + rapor uçları → Task 3-5-6. §4/§4.1 hesap → mevcut `Kasa.Core` (`HesapServisi` üzerinden) + Task 6 uçtan uca doğrulama. (Dağıtım = Plan 4; React↔API bağlama = kapsam dışı.)

**Placeholder taraması:** Yok — her adımda tam kod/komut var. "TBD"/"uygun hata işleme" gibi ifade yok.

**Tip tutarlılığı:**
- `KasaDbContext` DbSet adları (`Kanallar/Cariler/Islemler/Gelenler/Ayarlar`) Task 1'de tanımlı, Task 3/6'da aynı kullanılıyor.
- `CoreMapping.ToCore` imzaları Task 1'de tanımlı; `HesapServisi` (Task 3) ve testler (Task 6) aynı çağırıyor.
- `SifreHasher.Hashle/Dogrula` Task 2'de tanımlı; `Program.cs` login + izleyici-şifre ucu (Task 3) ve testler (Task 4-5) aynı imzayı kullanıyor.
- `JwtYardimci.Uret(string rol, string jwtKey)` Task 3'te tanımlı, login ucunda aynı çağrılıyor.
- DTO'lar (`LoginDto`, `GelenUpsertDto`, `IzleyiciSifreDto`, `PanelDto`, `KanalBakiye`, `AyarGuncelleDto`) Task 3'te tanımlı; testler alan adlarıyla (`donemStart/kanal/tutarTl`, `yeniSifre`, `GuncelKasa/Bakiye`) uyumlu.
- Cookie adı `kasa_auth` login (Task 3), `OnMessageReceived` (Task 3) ve auth testi (Task 4) arasında tutarlı.
- Config anahtarları `Kasa:EditorKullanici/EditorSifre/JwtKey` appsettings (Task 3) ve test factory (Task 4) arasında tutarlı.

**Notlar / riskler:**
- SQLite'ta `decimal` TEXT olarak saklanır; toplamlar Core motorunda bellek içinde `decimal` yapıldığından kesinlik korunur. EF, decimal sıralaması için uyarı loglayabilir (hata değil).
- `DateOnly` EF Core 10 SQLite sağlayıcısında yerel desteklidir (TEXT `yyyy-MM-dd`).
- `Database.EnsureCreated` kullanıldı (migration yok) — tek dosyalık kişisel uygulama için yeterli. İleride şema değişirse migration'a geçiş Plan 4'te değerlendirilir.
- Test DB'si açık tutulan tek SQLite in-memory bağlantısıdır; `RaporTests` verisini `Tohumla` idempotent kurar.
