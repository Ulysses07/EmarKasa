# Kart Borcu Hareketleri (harcama + ödeme) Implementation Plan

> **Tarihsel plan:** Bu belge yazıldığı günün planıdır; içindeki `EnsureCreated`/elle SQL/DB yeniden oluşturma adımları artık geçersizdir — şema açılışta `SemaGuncelleyici` ile güncellenir, dağıtım/yedek için `deploy/README.md`'ye bakın.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kredi kartı harcamasını İşlemler'e, kart ödemesini yeni bir tabloya bağlayıp güncel borcu bunlardan türet; kullanıcı yalnızca açılış borcunu elle ayarlasın.

**Architecture:** Borç türetilir: `GüncelBorç = AçılışBorç + Σ(kart harcamaları) − Σ(kart ödemeleri)`. Harcama = `Islemler.KrediKartiId` dolu bir işlem (Tip=KrediKarti). Ödeme = yeni `KartOdemeler` tablosunda satır; kasa motoruna HİÇ girmez. `HesapMotoru` (kasa/ertelemeli K.K. muhasebesi) hiç değişmez — çift-sayma olmaz.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core 10 + SQLite (`EnsureCreated`, prod'a şema el-SQL'i ile), .NET MAUI (`net10.0-windows`), CommunityToolkit.Mvvm, xUnit.

**Referans spec:** `docs/specs/2026-07-15-kart-borc-hareketleri-design.md`

**Commit author:** Tüm commit'ler `Musa Sevinç <musa@royalmezat.com>` ile; **Co-Authored-By YOK**. Örnek:
```bash
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "..."
```

**Test/build komutları (repo kökü `<repo>`):**
- `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj`
- `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj`
- `dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj`
- `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj`
- `dotnet build Kasa.App/Kasa.App.csproj` (Windows; **önce `taskkill //F //IM Kasa.App.exe` — çalışan uygulama DLL'i kilitler**)

---

## File Structure

**Create:**
- `Kasa.Core/KartHesap.cs` — saf borç fonksiyonu (yan-etkisiz)
- `Kasa.Core.Tests/KartHesapTests.cs` — birim testler
- `Kasa.Api/Data` içinde `KartOdemeEntity` (Entities.cs'e eklenir), DbSet (KasaDbContext.cs'e eklenir)
- `Kasa.Api.Tests/KartOdemeCrudTests.cs` — CRUD entegrasyon testleri
- `Kasa.Api.Tests/KartBorcTuretmeTests.cs` — GET türetilmiş borç testleri
- `Kasa.App.Core/KartCipi.cs` — Id taşıyan seçilebilir çip

**Modify:**
- `Kasa.Api/Data/Entities.cs` — `IslemEntity.KrediKartiId` (nullable) + `KartOdemeEntity`
- `Kasa.Api/Data/KasaDbContext.cs` — `KartOdemeler` DbSet + `OnModelCreating` ilişkiler
- `Kasa.Api/Program.cs` — Islemler POST/PUT (Tip zorla + KrediKartiId), KrediKartlari GET (türetilmiş DTO), KartOdemeler CRUD, KrediKartlari DELETE (bağları temizle)
- `Kasa.ApiClient/Dtos.cs` — `IslemDto`/`IslemYaz` KrediKartiId; `KrediKartiDto` türetilmiş alanlar; `KartOdemeDto`/`KartOdemeYaz`
- `Kasa.ApiClient/IKasaApi.cs` — 3 yeni KartOdeme metodu
- `Kasa.ApiClient/KasaApiClient.cs` — 3 yeni metot implementasyonu
- `Kasa.ApiClient.Tests/MutasyonTests.cs` + yeni testler — KrediKartiId gövde + KartOdeme
- `Kasa.App.Core.Tests/SahteApi.cs` — 3 yeni metot + çağrı kaydı
- `Kasa.App.Core/IslemlerViewModel.cs` — Tip çipleri + Kart çipleri + KrediKartiId
- `Kasa.App.Core.Tests/IslemEditorTests.cs` — yeni testler
- `Kasa.App.Core/KrediKartiGorunum.cs` — türetilmiş alanlar + kırılım + ödeme listesi
- `Kasa.App.Core/KrediKartlariViewModel.cs` — açılış borcu + ödeme ekle/sil + geçmiş
- `Kasa.App.Core.Tests/KrediKartlariTests.cs` (varsa) / yeni test dosyası
- `Kasa.App/Views/IslemlerPage.xaml` — Tip çip grubu + Kart çip grubu
- `Kasa.App/Views/KrediKartlariPage.xaml` — türetilmiş borç + kırılım + ödeme ekle + geçmiş

---

## Task 1: Core — saf borç fonksiyonu

**Files:**
- Create: `Kasa.Core/KartHesap.cs`
- Test: `Kasa.Core.Tests/KartHesapTests.cs`

- [ ] **Step 1: Testi yaz (fail)**

Create `Kasa.Core.Tests/KartHesapTests.cs`:

```csharp
using Kasa.Core;

namespace Kasa.Core.Tests;

public class KartHesapTests
{
    [Fact]
    public void Acilis_arti_harcama_eksi_odeme()
    {
        var borc = KartHesap.GuncelBorc(acilisBorc: 1000m, harcamaToplam: 500m, odemeToplam: 200m);
        Assert.Equal(1300m, borc);
    }

    [Fact]
    public void Harcama_ve_odeme_yoksa_acilis_kalir()
    {
        Assert.Equal(1000m, KartHesap.GuncelBorc(1000m, 0m, 0m));
    }

    [Fact]
    public void Odeme_harcamayi_asarsa_negatif_olabilir()
    {
        Assert.Equal(-50m, KartHesap.GuncelBorc(100m, 0m, 150m));
    }
}
```

- [ ] **Step 2: Fail'i doğrula**

Run: `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj`
Expected: FAIL — `KartHesap` tanımlı değil (derleme hatası).

- [ ] **Step 3: Fonksiyonu yaz**

Create `Kasa.Core/KartHesap.cs`:

```csharp
namespace Kasa.Core;

/// <summary>Kredi kartı güncel borcunu türetir (saf, yan-etkisiz).
/// GüncelBorç = açılış + harcamalar − ödemeler. HesapMotoru'na dokunmaz.</summary>
public static class KartHesap
{
    public static decimal GuncelBorc(decimal acilisBorc, decimal harcamaToplam, decimal odemeToplam)
        => acilisBorc + harcamaToplam - odemeToplam;
}
```

- [ ] **Step 4: Pass'i doğrula**

Run: `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj`
Expected: PASS (yeni 3 test + mevcutlar).

- [ ] **Step 5: Commit**

```bash
git add Kasa.Core/KartHesap.cs Kasa.Core.Tests/KartHesapTests.cs
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "feat(core): kart güncel borcu türeten saf fonksiyon"
```

---

## Task 2: Server — Entity + DbSet + ilişkiler

**Files:**
- Modify: `Kasa.Api/Data/Entities.cs`
- Modify: `Kasa.Api/Data/KasaDbContext.cs`

Not: `EnsureCreated()` yeni/boş DB'de şemayı model'den kurar (test + yeni kurulum). Mevcut prod DB'ye kolon/tablo Task 12'de el-SQL'iyle eklenir.

- [ ] **Step 1: `IslemEntity`'ye nullable FK ekle**

Edit `Kasa.Api/Data/Entities.cs` — `IslemEntity` sınıfına (Not'tan sonra) ekle:

```csharp
    public string? Not { get; set; }
    public int? KrediKartiId { get; set; }
```

- [ ] **Step 2: `KartOdemeEntity`'yi ekle**

Edit `Kasa.Api/Data/Entities.cs` — dosya sonuna (KrediKartiEntity'den sonra) ekle:

```csharp
public class KartOdemeEntity
{
    public int Id { get; set; }
    public int KrediKartiId { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal Tutar { get; set; }
    public string? Not { get; set; }
}
```

- [ ] **Step 3: DbSet + ilişkiler**

Edit `Kasa.Api/Data/KasaDbContext.cs` — DbSet ekle ve `OnModelCreating` override et:

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
    public DbSet<KrediKartiEntity> KrediKartlari => Set<KrediKartiEntity>();
    public DbSet<KartOdemeEntity> KartOdemeler => Set<KartOdemeEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Kart silinince harcama işlemi kalır, bağ kopar (SET NULL).
        b.Entity<IslemEntity>()
            .HasOne<KrediKartiEntity>()
            .WithMany()
            .HasForeignKey(i => i.KrediKartiId)
            .OnDelete(DeleteBehavior.SetNull);

        // Kart silinince ödemeleri de silinir (CASCADE).
        b.Entity<KartOdemeEntity>()
            .HasOne<KrediKartiEntity>()
            .WithMany()
            .HasForeignKey(o => o.KrediKartiId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Derlemeyi doğrula**

Run: `dotnet build Kasa.Api/Kasa.Api.csproj`
Expected: PASS (kullanım yok, sadece şema; hata olmamalı).

- [ ] **Step 5: Commit**

```bash
git add Kasa.Api/Data/Entities.cs Kasa.Api/Data/KasaDbContext.cs
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "feat(api): kart ödeme tablosu + işlem-kart bağı (şema)"
```

---

## Task 3: Server — DTO'lar + KrediKartlari GET türetimi + Islemler Tip zorlaması

**Files:**
- Modify: `Kasa.Api/Program.cs`
- Test: `Kasa.Api.Tests/KartBorcTuretmeTests.cs`

- [ ] **Step 1: Türetme testini yaz (fail)**

Create `Kasa.Api.Tests/KartBorcTuretmeTests.cs`:

```csharp
using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class KartBorcTuretmeTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KartBorcTuretmeTests(KasaWebFactory factory) => _factory = factory;

    private record KartYanit(int Id, string Ad, decimal Borc, decimal GuncelBorc,
        decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam);
    private record OlusanIslem(int Id);

    [Fact]
    public async Task GuncelBorc_acilis_arti_harcama_eksi_odeme_olur()
    {
        var client = await _factory.EditorClientAsync();

        // Açılış borcu 1000 olan kart
        var kart = await (await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Türetme", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 100_000m, borc = 1000m,
        })).Content.ReadFromJsonAsync<KartYanit>();

        // 500 harcama (KrediKartiId dolu işlem)
        await client.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-07-10", cari = "Market", tutarTl = 500m,
            kanal = "MEZAT", tip = "Cari", not = (string?)null, krediKartiId = kart!.Id,
        });

        // 200 ödeme
        await client.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart.Id, tarih = "2026-07-20", tutar = 200m, not = (string?)null,
        });

        var liste = await client.GetFromJsonAsync<List<KartYanit>>("/api/kredikartlari");
        var g = liste!.Single(k => k.Id == kart.Id);
        Assert.Equal(1000m, g.AcilisBorc);
        Assert.Equal(500m, g.HarcamaToplam);
        Assert.Equal(200m, g.OdemeToplam);
        Assert.Equal(1300m, g.GuncelBorc);
    }

    [Fact]
    public async Task Islem_krediKartiId_dolu_gelirse_tip_KrediKarti_olur()
    {
        var client = await _factory.EditorClientAsync();
        var kart = await (await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "TipZorla", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 10_000m, borc = 0m,
        })).Content.ReadFromJsonAsync<KartYanit>();

        var olustur = await client.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-07-11", cari = "X", tutarTl = 90m, kanal = "MEZAT",
            tip = "Cari", not = (string?)null, krediKartiId = kart!.Id,   // Cari gönderdik ama...
        });
        var olusan = await olustur.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("KrediKarti", olusan.GetProperty("tip").GetString()); // ...sunucu KrediKarti'ye zorlar
    }
}
```

- [ ] **Step 2: Fail'i doğrula**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter KartBorcTuretmeTests`
Expected: FAIL — `/api/kartodemeler` yok (404) ve `guncelBorc` alanları yok.

- [ ] **Step 3: KrediKartlari GET'i türetilmiş DTO'ya çevir**

Edit `Kasa.Api/Program.cs` — `api.MapGet("/kredikartlari", ...)` satırını (mevcut tek satırlık) şununla değiştir:

```csharp
// Kredi kartları (güncel borç türetilir: açılış + harcama − ödeme)
api.MapGet("/kredikartlari", (KasaDbContext db) =>
{
    var kartlar = db.KrediKartlari.OrderBy(k => k.Ad).ToList();
    var harcama = db.Islemler.Where(i => i.KrediKartiId != null)
        .GroupBy(i => i.KrediKartiId!.Value)
        .ToDictionary(g => g.Key, g => g.Sum(i => i.TutarTl));
    var odeme = db.KartOdemeler
        .GroupBy(o => o.KrediKartiId)
        .ToDictionary(g => g.Key, g => g.Sum(o => o.Tutar));
    return kartlar.Select(k =>
    {
        var h = harcama.GetValueOrDefault(k.Id, 0m);
        var o = odeme.GetValueOrDefault(k.Id, 0m);
        return new KrediKartiTuretilmisDto(
            k.Id, k.Ad, k.KesimTarihi, k.SonOdemeTarihi, k.Limit,
            Borc: k.Borc, GuncelBorc: k.Borc + h - o, AcilisBorc: k.Borc,
            HarcamaToplam: h, OdemeToplam: o);
    }).ToList();
});
```

- [ ] **Step 4: Islemler POST/PUT — Tip zorlaması + KrediKartiId**

Edit `Kasa.Api/Program.cs` — POST'u değiştir:

```csharp
api.MapPost("/islemler", (IslemEntity e, KasaDbContext db) =>
{
    if (e.KrediKartiId is not null) e.Tip = GiderTipi.KrediKarti; // kart harcaması tutarlılığı
    db.Islemler.Add(e); db.SaveChanges();
    return Results.Created($"/api/islemler/{e.Id}", e);
}).RequireAuthorization("Editor");
```

PUT'ta `e.Not = gelen.Not;` satırından sonra ekle:

```csharp
    e.KrediKartiId = gelen.KrediKartiId;
    if (e.KrediKartiId is not null) e.Tip = GiderTipi.KrediKarti;
```

- [ ] **Step 5: KrediKartlari DELETE — bağları deterministik temizle**

Edit `Kasa.Api/Program.cs` — `api.MapDelete("/kredikartlari/{id:int}", ...)` gövdesini şununla değiştir:

```csharp
api.MapDelete("/kredikartlari/{id:int}", (int id, KasaDbContext db) =>
{
    var e = db.KrediKartlari.Find(id);
    if (e is null) return Results.NotFound();
    // Harcama işlemlerinin bağını kopar (işlem kalır), ödemeleri sil.
    foreach (var i in db.Islemler.Where(i => i.KrediKartiId == id)) i.KrediKartiId = null;
    db.KartOdemeler.RemoveRange(db.KartOdemeler.Where(o => o.KrediKartiId == id));
    db.KrediKartlari.Remove(e); db.SaveChanges();
    return Results.NoContent();
}).RequireAuthorization("Editor");
```

- [ ] **Step 6: Türetilmiş DTO record'unu ekle**

Edit `Kasa.Api/Dtos.cs` — dosya sonuna ekle:

```csharp
public record KrediKartiTuretilmisDto(
    int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit,
    decimal Borc, decimal GuncelBorc, decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam);
```

- [ ] **Step 7: KartOdemeler CRUD'unu ekle (Task 4 testleri de buna bağlı)**

Edit `Kasa.Api/Program.cs` — `// Gelenler` bloğundan ÖNCE ekle:

```csharp
// Kart ödemeleri (borç-only; kasa motoruna girmez)
api.MapGet("/kartodemeler", (int? krediKartiId, KasaDbContext db) =>
{
    var q = db.KartOdemeler.AsQueryable();
    if (krediKartiId is { } id) q = q.Where(o => o.KrediKartiId == id);
    return q.OrderByDescending(o => o.Tarih).ThenByDescending(o => o.Id).ToList();
});
api.MapPost("/kartodemeler", (KartOdemeEntity e, KasaDbContext db) =>
{
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
```

- [ ] **Step 8: Pass'i doğrula**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj`
Expected: PASS — yeni türetme testleri + mevcut tüm testler.

- [ ] **Step 9: Commit**

```bash
git add Kasa.Api/Program.cs Kasa.Api/Dtos.cs Kasa.Api.Tests/KartBorcTuretmeTests.cs
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "feat(api): türetilmiş kart borcu + kart harcaması Tip zorlaması + KartOdeme CRUD"
```

---

## Task 4: Server — KartOdeme CRUD entegrasyon testleri

**Files:**
- Test: `Kasa.Api.Tests/KartOdemeCrudTests.cs`

(CRUD uçları Task 3 Step 7'de eklendi; bu task rol/cascade davranışını sabitler.)

- [ ] **Step 1: Testi yaz (fail)**

Create `Kasa.Api.Tests/KartOdemeCrudTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class KartOdemeCrudTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KartOdemeCrudTests(KasaWebFactory factory) => _factory = factory;

    private record KartYanit(int Id, string Ad);
    private record OdemeYanit(int Id, int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);
    private record KartBorc(int Id, decimal GuncelBorc, decimal OdemeToplam);

    [Fact]
    public async Task Editor_odeme_ekleyip_listeleyip_silebilir()
    {
        var client = await _factory.EditorClientAsync();
        var kart = await (await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Ödeme", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 50_000m, borc = 5000m,
        })).Content.ReadFromJsonAsync<KartYanit>();

        var ekle = await client.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart!.Id, tarih = "2026-07-22", tutar = 1500m, not = "kısmi",
        });
        Assert.Equal(HttpStatusCode.Created, ekle.StatusCode);
        var eklenen = await ekle.Content.ReadFromJsonAsync<OdemeYanit>();
        Assert.Equal(1500m, eklenen!.Tutar);

        var liste = await client.GetFromJsonAsync<List<OdemeYanit>>($"/api/kartodemeler?krediKartiId={kart.Id}");
        Assert.Single(liste!);

        var sil = await client.DeleteAsync($"/api/kartodemeler/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
    }

    [Fact]
    public async Task Izleyici_odeme_okur_ama_ekleyemez()
    {
        var editor = await _factory.EditorClientAsync();
        await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" });

        var izleyici = _factory.CreateClient();
        await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" });

        var okuma = await izleyici.GetAsync("/api/kartodemeler");
        Assert.Equal(HttpStatusCode.OK, okuma.StatusCode);

        var yazma = await izleyici.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = 1, tarih = "2026-07-22", tutar = 1m, not = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Forbidden, yazma.StatusCode);
    }

    [Fact]
    public async Task Kart_silinince_odemeler_silinir_islem_bagi_kopar()
    {
        var client = await _factory.EditorClientAsync();
        var kart = await (await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Cascade", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 50_000m, borc = 0m,
        })).Content.ReadFromJsonAsync<KartYanit>();

        var islem = await (await client.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-07-10", cari = "Y", tutarTl = 300m, kanal = "MEZAT",
            tip = "Cari", not = (string?)null, krediKartiId = kart!.Id,
        })).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var islemId = islem.GetProperty("id").GetInt32();

        await client.PostAsJsonAsync("/api/kartodemeler", new
        {
            krediKartiId = kart.Id, tarih = "2026-07-20", tutar = 100m, not = (string?)null,
        });

        await client.DeleteAsync($"/api/kredikartlari/{kart.Id}");

        // Ödemeler silinmeli
        var odemeler = await client.GetFromJsonAsync<List<OdemeYanit>>($"/api/kartodemeler?krediKartiId={kart.Id}");
        Assert.Empty(odemeler!);

        // İşlem kalmalı ama krediKartiId null olmalı
        var islemler = await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/islemler");
        var kalan = islemler!.Single(i => i.GetProperty("id").GetInt32() == islemId);
        Assert.True(kalan.GetProperty("krediKartiId").ValueKind == System.Text.Json.JsonValueKind.Null);
    }
}
```

- [ ] **Step 2: Pass'i doğrula**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter KartOdemeCrudTests`
Expected: PASS (uçlar Task 3'te eklendi).

- [ ] **Step 3: Commit**

```bash
git add Kasa.Api.Tests/KartOdemeCrudTests.cs
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "test(api): kart ödeme CRUD + rol + silme davranışı"
```

---

## Task 5: ApiClient — DTO'lar + metotlar + testler

**Files:**
- Modify: `Kasa.ApiClient/Dtos.cs`, `Kasa.ApiClient/IKasaApi.cs`, `Kasa.ApiClient/KasaApiClient.cs`
- Test: `Kasa.ApiClient.Tests/MutasyonTests.cs`

- [ ] **Step 1: DTO'ları genişlet**

Edit `Kasa.ApiClient/Dtos.cs`:

`IslemDto` satırını değiştir (trailing nullable, mevcut 7-arg çağrılar bozulmaz):
```csharp
public record IslemDto(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not, int? KrediKartiId = null);
```

`KrediKartiDto` satırını değiştir (trailing default'lar, mevcut çağrılar bozulmaz):
```csharp
public record KrediKartiDto(int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc,
    decimal GuncelBorc = 0m, decimal AcilisBorc = 0m, decimal HarcamaToplam = 0m, decimal OdemeToplam = 0m);
```

`IslemYaz` satırını değiştir:
```csharp
public record IslemYaz(DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not, int? KrediKartiId = null);
```

Dosya sonuna KartOdeme DTO'larını ekle:
```csharp
public record KartOdemeDto(int Id, int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);
public record KartOdemeYaz(int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);
```

- [ ] **Step 2: Arayüze metotları ekle**

Edit `Kasa.ApiClient/IKasaApi.cs` — `Task KrediKartiSilAsync(int id);` satırından sonra ekle:
```csharp
    Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId);
    Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g);
    Task KartOdemeSilAsync(int id);
```

- [ ] **Step 3: İstemci testini yaz (fail)**

Edit `Kasa.ApiClient.Tests/MutasyonTests.cs` — sınıfa yeni testler ekle:

```csharp
    [Fact]
    public async Task Islem_olustur_krediKartiId_govdeye_girer()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":1,"tarih":"2026-07-10","cari":"Market","tutarTl":500.0,"kanal":"MEZAT","tip":"KrediKarti","not":null,"krediKartiId":7}""");

        await c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 7, 10), "Market", 500m, "MEZAT", GiderTipi.KrediKarti, null, 7));

        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal(7, doc.RootElement.GetProperty("krediKartiId").GetInt32());
    }

    [Fact]
    public async Task KartOdeme_kaydet_dogru_yol_ve_govde()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":3,"krediKartiId":7,"tarih":"2026-07-20","tutar":200.0,"not":null}""");

        var kaydedilen = await c.KartOdemeKaydetAsync(new KartOdemeYaz(7, new DateOnly(2026, 7, 20), 200m, null));

        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/kartodemeler", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal(7, doc.RootElement.GetProperty("krediKartiId").GetInt32());
        Assert.Equal(200m, doc.RootElement.GetProperty("tutar").GetDecimal());
        Assert.Equal(3, kaydedilen.Id);
    }

    [Fact]
    public async Task KartOdemeler_liste_query_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """[{"id":3,"krediKartiId":7,"tarih":"2026-07-20","tutar":200.0,"not":null}]""");

        var liste = await c.KartOdemelerAsync(7);

        Assert.Equal(HttpMethod.Get, h.SonIstek!.Method);
        Assert.Contains("krediKartiId=7", h.SonIstek.RequestUri!.Query);
        Assert.Single(liste);
    }

    [Fact]
    public async Task KartOdeme_sil_delete_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.KartOdemeSilAsync(3);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.EndsWith("/api/kartodemeler/3", h.SonIstek.RequestUri!.AbsolutePath);
    }
```

- [ ] **Step 4: Fail'i doğrula**

Run: `dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj --filter MutasyonTests`
Expected: FAIL — `KartOdemeKaydetAsync`/`KartOdemelerAsync`/`KartOdemeSilAsync` yok.

- [ ] **Step 5: İstemci metotlarını yaz**

Edit `Kasa.ApiClient/KasaApiClient.cs` — okuma bloğuna ekle (KrediKartlariAsync yakınına):
```csharp
    public Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId)
        => GetAsync<IReadOnlyList<KartOdemeDto>>($"api/kartodemeler?krediKartiId={krediKartiId}");
```

Mutasyon bloğuna (Kredi kartı bloğunun altına) ekle:
```csharp
    // Kart ödemeleri
    public Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g) => GonderJsonAsync<KartOdemeDto>(HttpMethod.Post, "api/kartodemeler", g);
    public Task KartOdemeSilAsync(int id) => SilAsync($"api/kartodemeler/{id}");
```

- [ ] **Step 6: Pass'i doğrula**

Run: `dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Kasa.ApiClient/Dtos.cs Kasa.ApiClient/IKasaApi.cs Kasa.ApiClient/KasaApiClient.cs Kasa.ApiClient.Tests/MutasyonTests.cs
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "feat(apiclient): kart ödeme metotları + işlem KrediKartiId"
```

---

## Task 6: SahteApi test double — yeni metotlar

**Files:**
- Modify: `Kasa.App.Core.Tests/SahteApi.cs`

- [ ] **Step 1: Kayıt alanları + metotları ekle**

Edit `Kasa.App.Core.Tests/SahteApi.cs`:

Kredi kartı liste alanının yanına canned ödeme listesi + kayıt alanları ekle (mutasyon kayıtları bölümüne):
```csharp
    public IReadOnlyList<KartOdemeDto> KartOdemelerListe = new List<KartOdemeDto>();
    public int? SonKartOdemelerId;
    public KartOdemeYaz? SonKartOdemeKaydet;
    public int? SonKartOdemeSil;
```

`KrediKartiSilAsync` metodundan sonra ekle:
```csharp
    public Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId)
    { SonKartOdemelerId = krediKartiId; return YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KartOdemeDto>>(YuklemeHatasi) : Task.FromResult(KartOdemelerListe); }
    public Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g)
    { SonKartOdemeKaydet = g; return Task.FromResult(new KartOdemeDto(0, g.KrediKartiId, g.Tarih, g.Tutar, g.Not)); }
    public Task KartOdemeSilAsync(int id) { SonKartOdemeSil = id; return Task.CompletedTask; }
```

- [ ] **Step 2: Derlemeyi doğrula**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj`
Expected: PASS (mevcut testler; yeni metotlar arayüzü tamamlar, derleme geçer).

- [ ] **Step 3: Commit**

```bash
git add Kasa.App.Core.Tests/SahteApi.cs
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "test(app): SahteApi'ye kart ödeme metotları"
```

---

## Task 7: İşlemler VM — Tip çipleri + Kart çipleri

**Files:**
- Create: `Kasa.App.Core/KartCipi.cs`
- Modify: `Kasa.App.Core/IslemlerViewModel.cs`
- Test: `Kasa.App.Core.Tests/IslemEditorTests.cs`

- [ ] **Step 1: Kart çipi tipini oluştur**

Create `Kasa.App.Core/KartCipi.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

/// <summary>İşlem formunda kart harcaması için seçilebilir kart çipi (Id taşır).</summary>
public partial class KartCipi : ObservableObject
{
    public int Id { get; }
    public string Ad { get; }
    public KartCipi(int id, string ad) { Id = id; Ad = ad; }
    [ObservableProperty] private bool _secili;
}
```

- [ ] **Step 2: VM testlerini yaz (fail)**

Edit `Kasa.App.Core.Tests/IslemEditorTests.cs` — sınıfa ekle:
```csharp
    [Fact]
    public async Task Kredi_karti_tipi_secilince_kart_secici_gorunur()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new[] { new KrediKartiDto(3, "Bonus", new DateOnly(2026,7,5), new DateOnly(2026,7,25), 100000m, 0m) },
        };
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();

        Assert.False(vm.KartSeciciGorunur);
        await vm.SecTipCommand.ExecuteAsync(vm.TipCipleri.First(t => t.Ad == "Kredi kartı"));
        Assert.True(vm.KartSeciciGorunur);
        Assert.Single(vm.KartCipleri);
    }

    [Fact]
    public async Task Kart_secili_kart_harcamasi_KrediKartiId_ile_kaydolur()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new[] { new KrediKartiDto(3, "Bonus", new DateOnly(2026,7,5), new DateOnly(2026,7,25), 100000m, 0m) },
        };
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();
        vm.DuzenTarih = new DateTime(2026, 7, 10);
        vm.DuzenCari = "Market";
        vm.DuzenTutar = 500m;
        vm.DuzenKanal = "MEZAT";

        await vm.SecTipCommand.ExecuteAsync(vm.TipCipleri.First(t => t.Ad == "Kredi kartı"));
        await vm.SecKartCommand.ExecuteAsync(vm.KartCipleri.First(k => k.Id == 3));
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemOlustur);
        Assert.Equal(3, api.SonIslemOlustur!.KrediKartiId);
        Assert.Equal(GiderTipi.KrediKarti, api.SonIslemOlustur!.Tip);
    }

    [Fact]
    public async Task Cari_tipe_donunce_KrediKartiId_temizlenir()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new[] { new KrediKartiDto(3, "Bonus", new DateOnly(2026,7,5), new DateOnly(2026,7,25), 100000m, 0m) },
        };
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();
        vm.DuzenCari = "X"; vm.DuzenTutar = 10m; vm.DuzenKanal = "MEZAT";

        await vm.SecTipCommand.ExecuteAsync(vm.TipCipleri.First(t => t.Ad == "Kredi kartı"));
        await vm.SecKartCommand.ExecuteAsync(vm.KartCipleri.First(k => k.Id == 3));
        await vm.SecTipCommand.ExecuteAsync(vm.TipCipleri.First(t => t.Ad == "Cari"));
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonIslemOlustur!.KrediKartiId);
        Assert.Equal(GiderTipi.Cari, api.SonIslemOlustur!.Tip);
    }
```

- [ ] **Step 3: Fail'i doğrula**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter IslemEditorTests`
Expected: FAIL — `TipCipleri`/`KartCipleri`/`KartSeciciGorunur`/`SecTipCommand`/`SecKartCommand` yok.

- [ ] **Step 4: VM'i genişlet**

Edit `Kasa.App.Core/IslemlerViewModel.cs`.

(a) Çip koleksiyonlarının yanına ekle (FiltreDonemler'den sonra):
```csharp
    /// <summary>İşlem tipi çipleri: Cari · Sabit gider · Kredi kartı.</summary>
    public ObservableCollection<SecimCipi> TipCipleri { get; } = new();

    /// <summary>Kart harcaması için kart çipleri (yalnız Kredi kartı tipinde görünür).</summary>
    public ObservableCollection<KartCipi> KartCipleri { get; } = new();

    /// <summary>Kart seçici yalnız Kredi kartı tipi seçiliyken görünür.</summary>
    public bool KartSeciciGorunur => DuzenTip == GiderTipi.KrediKarti;
```

(b) `DuzenKrediKartiId` alanını ekle (`_duzenNot`'un yanına):
```csharp
    [ObservableProperty] private int? _duzenKrediKartiId;
```

(c) Tip etiketi ↔ enum eşleme yardımcıları (dosya içinde, sınıfta private static):
```csharp
    private static string TipAdi(GiderTipi t) => t switch
    {
        GiderTipi.SabitGider => "Sabit gider",
        GiderTipi.KrediKarti => "Kredi kartı",
        _ => "Cari",
    };
    private static GiderTipi TipDegeri(string ad) => ad switch
    {
        "Sabit gider" => GiderTipi.SabitGider,
        "Kredi kartı" => GiderTipi.KrediKarti,
        _ => GiderTipi.Cari,
    };
```

(d) `DoldurAsync` içinde, `var kanallar = ...` ve `var donemler = ...` satırlarının yanına kart yüklemesini ve çip kurulumunu ekle. `var donemler = await _api.DonemlerAsync();` satırından sonra:
```csharp
        var kartlar = await _api.KrediKartlariAsync();
```
Ve `FiltreDonemler` doldurulduktan sonra, `FiltreVurgu();` satırından ÖNCE ekle:
```csharp
        if (TipCipleri.Count == 0)
            foreach (var t in new[] { GiderTipi.Cari, GiderTipi.SabitGider, GiderTipi.KrediKarti })
                TipCipleri.Add(new SecimCipi(TipAdi(t)));

        KartCipleri.Clear();
        foreach (var k in kartlar) KartCipleri.Add(new KartCipi(k.Id, k.Ad));
        TipVurgu();
```

(e) Tip/kart komutları + highlight — `SecGelenKanal` komutunun yanına ekle:
```csharp
    [RelayCommand] private void SecTip(SecimCipi s) => DuzenTip = TipDegeri(s.Ad);
    [RelayCommand] private void SecKart(KartCipi k) => DuzenKrediKartiId = k.Id;

    private void TipVurgu()
    {
        foreach (var t in TipCipleri) t.Secili = t.Ad == TipAdi(DuzenTip);
        foreach (var k in KartCipleri) k.Secili = k.Id == DuzenKrediKartiId;
    }
```

(f) `OnGelenKanalChanged`'in yanına tip değişim handler'ı ekle:
```csharp
    partial void OnDuzenTipChanged(GiderTipi value)
    {
        if (value != GiderTipi.KrediKarti) DuzenKrediKartiId = null;
        OnPropertyChanged(nameof(KartSeciciGorunur));
        TipVurgu();
    }

    partial void OnDuzenKrediKartiIdChanged(int? value)
    {
        foreach (var k in KartCipleri) k.Secili = k.Id == value;
    }
```

(g) `KaydetAsync` içinde `IslemYaz` yapımına KrediKartiId ekle:
```csharp
        var g = new IslemYaz(DateOnly.FromDateTime(DuzenTarih), DuzenCari, DuzenTutar, DuzenKanal, DuzenTip, DuzenNot, DuzenKrediKartiId);
```

(h) `Yeni()` içinde sıfırla — `DuzenTip = GiderTipi.Cari;` satırının yanına:
```csharp
        DuzenKrediKartiId = null;
```

(i) `Duzenle(IslemDto i)` içine ekle — `DuzenTip = i.Tip;` yanına:
```csharp
        DuzenKrediKartiId = i.KrediKartiId;
```

- [ ] **Step 5: Pass'i doğrula**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj`
Expected: PASS (yeni 3 test + mevcut 8 editör/filtre testi).

- [ ] **Step 6: Commit**

```bash
git add Kasa.App.Core/KartCipi.cs Kasa.App.Core/IslemlerViewModel.cs Kasa.App.Core.Tests/IslemEditorTests.cs
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "feat(app): işlem formunda tip çipleri + kart harcaması seçimi"
```

---

## Task 8: İşlemler XAML — Tip çip grubu + Kart çip grubu

**Files:**
- Modify: `Kasa.App/Views/IslemlerPage.xaml`

(XAML derleme ile doğrulanır; TDD birim testi yok.)

- [ ] **Step 1: Tip çip grubunu ekle**

Edit `Kasa.App/Views/IslemlerPage.xaml` — gider formunda, mevcut Kanal çip `FlexLayout`'unun (GiderKanallari) ÜSTÜNE, "Tip" başlıklı bir çip grubu ekle. `Sayfa` referansı ve `SecGiderKanalCommand` deseni birebir taklit edilir:

```xml
<VerticalStackLayout Spacing="4">
    <Label Text="Tip" Style="{StaticResource LblField}" />
    <FlexLayout BindableLayout.ItemsSource="{Binding TipCipleri}" Wrap="Wrap">
        <BindableLayout.ItemTemplate>
            <DataTemplate>
                <Border Style="{StaticResource Chip}" Margin="0,0,8,8">
                    <Border.GestureRecognizers>
                        <TapGestureRecognizer
                            Command="{Binding BindingContext.SecTipCommand, Source={x:Reference Sayfa}}"
                            CommandParameter="{Binding .}" />
                    </Border.GestureRecognizers>
                    <Label Text="{Binding Ad}" Style="{StaticResource ChipText}" />
                </Border>
            </DataTemplate>
        </BindableLayout.ItemTemplate>
    </FlexLayout>
</VerticalStackLayout>
```

- [ ] **Step 2: Kart çip grubunu ekle (yalnız Kredi kartı tipinde görünür)**

Gider kanal çiplerinin yanına (Tip grubunun altına) ekle:

```xml
<VerticalStackLayout Spacing="4" IsVisible="{Binding KartSeciciGorunur}">
    <Label Text="Kart" Style="{StaticResource LblField}" />
    <FlexLayout BindableLayout.ItemsSource="{Binding KartCipleri}" Wrap="Wrap">
        <BindableLayout.ItemTemplate>
            <DataTemplate>
                <Border Style="{StaticResource Chip}" Margin="0,0,8,8">
                    <Border.GestureRecognizers>
                        <TapGestureRecognizer
                            Command="{Binding BindingContext.SecKartCommand, Source={x:Reference Sayfa}}"
                            CommandParameter="{Binding .}" />
                    </Border.GestureRecognizers>
                    <Label Text="{Binding Ad}" Style="{StaticResource ChipText}" />
                </Border>
            </DataTemplate>
        </BindableLayout.ItemTemplate>
    </FlexLayout>
</VerticalStackLayout>
```

- [ ] **Step 3: Derlemeyi doğrula**

```bash
taskkill //F //IM Kasa.App.exe 2>/dev/null; dotnet build Kasa.App/Kasa.App.csproj
```
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Kasa.App/Views/IslemlerPage.xaml
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "feat(app): işlem formu tip + kart çip grubu (XAML)"
```

---

## Task 9: KrediKartlari VM + Görünüm — türetilmiş borç, ödeme ekle/sil, geçmiş

**Files:**
- Modify: `Kasa.App.Core/KrediKartiGorunum.cs`
- Modify: `Kasa.App.Core/KrediKartlariViewModel.cs`
- Test: `Kasa.App.Core.Tests/KrediKartlariTests.cs` (yoksa oluştur)

- [ ] **Step 1: Görünüm modelini genişlet**

Edit `Kasa.App.Core/KrediKartiGorunum.cs` — türetilmiş alanlar, kırılım metni, ödeme koleksiyonu:

```csharp
using System.Collections.ObjectModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kart + türetilmiş güncel borç (açılış + harcama − ödeme) + ödeme geçmişi.</summary>
public sealed class KrediKartiGorunum
{
    public int Id { get; }
    public string Ad { get; }
    public DateOnly KesimTarihi { get; }
    public DateOnly SonOdemeTarihi { get; }
    public decimal Limit { get; }
    public decimal AcilisBorc { get; }
    public decimal HarcamaToplam { get; }
    public decimal OdemeToplam { get; }
    public decimal GuncelBorc { get; }
    public decimal KalanLimit => Limit - GuncelBorc;
    public double KalanOran => Limit <= 0 ? 0 : Math.Clamp((double)(KalanLimit / Limit), 0, 1);

    /// <summary>Küçük gri kırılım: "Açılış x · Harcama +y · Ödeme −z".</summary>
    public string BorcKirilim =>
        $"Açılış {Bicim.Tl(AcilisBorc)} · Harcama +{Bicim.Tl(HarcamaToplam)} · Ödeme −{Bicim.Tl(OdemeToplam)}";

    /// <summary>Bu karta ait son ödemeler (VM doldurur).</summary>
    public ObservableCollection<KartOdemeDto> Odemeler { get; } = new();

    public KrediKartiGorunum(KrediKartiDto d)
    {
        Id = d.Id; Ad = d.Ad; KesimTarihi = d.KesimTarihi; SonOdemeTarihi = d.SonOdemeTarihi;
        Limit = d.Limit; AcilisBorc = d.AcilisBorc; HarcamaToplam = d.HarcamaToplam;
        OdemeToplam = d.OdemeToplam; GuncelBorc = d.GuncelBorc;
    }
}
```

- [ ] **Step 2: VM testlerini yaz (fail)**

Create/append `Kasa.App.Core.Tests/KrediKartlariTests.cs`:

```csharp
using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Core.Tests;

public class KrediKartlariTests
{
    private static KrediKartiDto Kart(int id, decimal guncel = 1300m) =>
        new(id, "Bonus", new DateOnly(2026,7,5), new DateOnly(2026,7,25), 100000m,
            Borc: 1000m, GuncelBorc: guncel, AcilisBorc: 1000m, HarcamaToplam: 500m, OdemeToplam: 200m);

    [Fact]
    public async Task Yukleme_turetilmis_borcu_ve_kirilimi_kurar()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new[] { Kart(3) },
            KartOdemelerListe = new[] { new KartOdemeDto(9, 3, new DateOnly(2026,7,20), 200m, null) },
        };
        var vm = new KrediKartlariViewModel(api);
        await vm.YukleAsync();

        var g = vm.Kartlar.Single();
        Assert.Equal(1300m, g.GuncelBorc);
        Assert.Single(g.Odemeler);
        Assert.Contains("Açılış", g.BorcKirilim);
    }

    [Fact]
    public async Task Odeme_ekle_kart_odeme_kaydeder()
    {
        var api = new SahteApi { KrediKartlariListe = new[] { Kart(3) } };
        var vm = new KrediKartlariViewModel(api);
        await vm.YukleAsync();
        vm.DuzenOdemeTutar = 250m;
        vm.DuzenOdemeTarih = new DateTime(2026, 7, 22);

        await vm.OdemeEkleCommand.ExecuteAsync(vm.Kartlar.Single());

        Assert.NotNull(api.SonKartOdemeKaydet);
        Assert.Equal(3, api.SonKartOdemeKaydet!.KrediKartiId);
        Assert.Equal(250m, api.SonKartOdemeKaydet!.Tutar);
    }

    [Fact]
    public async Task Odeme_sil_kart_odeme_siler()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new[] { Kart(3) },
            KartOdemelerListe = new[] { new KartOdemeDto(9, 3, new DateOnly(2026,7,20), 200m, null) },
        };
        var vm = new KrediKartlariViewModel(api);
        await vm.YukleAsync();

        await vm.OdemeSilCommand.ExecuteAsync(new KartOdemeDto(9, 3, new DateOnly(2026,7,20), 200m, null));

        Assert.Equal(9, api.SonKartOdemeSil);
    }

    [Fact]
    public async Task Duzenle_acilis_borcunu_forma_koyar()
    {
        var api = new SahteApi { KrediKartlariListe = new[] { Kart(3) } };
        var vm = new KrediKartlariViewModel(api);
        await vm.YukleAsync();

        vm.Duzenle(vm.Kartlar.Single());

        Assert.Equal(1000m, vm.DuzenBorc); // açılış borcu (güncel değil)
    }
}
```

- [ ] **Step 3: Fail'i doğrula**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter KrediKartlariTests`
Expected: FAIL — `DuzenOdemeTutar`/`OdemeEkleCommand`/`OdemeSilCommand` yok; `Duzenle` `Borc` alanını kullanıyor.

- [ ] **Step 4: VM'i genişlet**

Edit `Kasa.App.Core/KrediKartlariViewModel.cs`:

(a) `DoldurAsync`'i ödeme yüklemesiyle güncelle:
```csharp
    private async Task DoldurAsync()
    {
        var liste = await _api.KrediKartlariAsync();
        Kartlar.Clear();
        foreach (var k in liste)
        {
            var g = new KrediKartiGorunum(k);
            var odemeler = await _api.KartOdemelerAsync(k.Id);
            foreach (var o in odemeler) g.Odemeler.Add(o);
            Kartlar.Add(g);
        }
    }
```

(b) `Duzenle`'de açılış borcunu forma koy — `DuzenBorc = k.Borc;` satırını değiştir:
```csharp
        DuzenLimit = k.Limit; DuzenBorc = k.AcilisBorc;   // "Açılış borcu" alanı
```

(c) Ödeme formu state'i ekle (`_duzenBorc`'un yanına):
```csharp
    [ObservableProperty] private DateTime _duzenOdemeTarih = DateTime.Today;
    [ObservableProperty] private decimal _duzenOdemeTutar;
    [ObservableProperty] private string? _duzenOdemeNot;
```

(d) Ödeme komutlarını ekle (`SilAsync` komutunun yanına):
```csharp
    [RelayCommand]
    private Task OdemeEkleAsync(KrediKartiGorunum k) => CalistirAsync(async () =>
    {
        await _api.KartOdemeKaydetAsync(new KartOdemeYaz(
            k.Id, DateOnly.FromDateTime(DuzenOdemeTarih), DuzenOdemeTutar, DuzenOdemeNot));
        DuzenOdemeTutar = 0; DuzenOdemeNot = null; DuzenOdemeTarih = DateTime.Today;
        await DoldurAsync();
    });

    [RelayCommand]
    private Task OdemeSilAsync(KartOdemeDto o) => CalistirAsync(async () =>
    {
        await _api.KartOdemeSilAsync(o.Id);
        await DoldurAsync();
    });
```

- [ ] **Step 5: Pass'i doğrula**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Kasa.App.Core/KrediKartiGorunum.cs Kasa.App.Core/KrediKartlariViewModel.cs Kasa.App.Core.Tests/KrediKartlariTests.cs
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "feat(app): kart türetilmiş borç + ödeme ekle/sil + geçmiş (VM)"
```

---

## Task 10: KrediKartlari XAML — türetilmiş borç + kırılım + ödeme ekle + geçmiş

**Files:**
- Modify: `Kasa.App/Views/KrediKartlariPage.xaml`

- [ ] **Step 1: Editör form "Borç" etiketini "Açılış borcu" yap**

Edit `Kasa.App/Views/KrediKartlariPage.xaml` — editör formundaki `<Label Text="Borç" .../>` satırını değiştir:
```xml
                            <Label Text="Açılış borcu" Style="{StaticResource LblField}" />
```

- [ ] **Step 2: Kart ızgarasında "Borç" satırını güncel borca + kırılıma çevir**

DataTemplate'te "Borç" bloğunu (`<Grid ... Borc ...>`) değiştir — güncel borç + altında gri kırılım:
```xml
                                <BoxView Style="{StaticResource RowSeparator}" />
                                <Grid ColumnDefinitions="*,Auto" Padding="0,7">
                                    <Label Text="Borç" Style="{StaticResource LblPageSub}" FontSize="13" />
                                    <Label Grid.Column="1"
                                           Text="{Binding GuncelBorc, Converter={StaticResource ParaBicim}}"
                                           Style="{StaticResource LblMoneyNeg}" FontSize="13.5" />
                                </Grid>
                                <Label Text="{Binding BorcKirilim}" Style="{StaticResource LblPageSub}"
                                       FontSize="11" Margin="0,0,0,2" />
```

- [ ] **Step 3: Ödeme ekle + geçmiş bloğunu (editör) ekle**

DataTemplate'teki aksiyon `VerticalStackLayout` (`BindingContext.EditorMu`) içine, mevcut Düzenle/Sil butonlarının ALTINA ekle:
```xml
                                    <BoxView Style="{StaticResource RowSeparator}" Margin="0,12,0,0" />
                                    <Label Text="Ödeme ekle" Style="{StaticResource LblField}" Margin="0,10,0,4" />
                                    <Grid ColumnDefinitions="*,Auto" ColumnSpacing="6">
                                        <Border Style="{StaticResource FieldBorder}">
                                            <Entry Text="{Binding BindingContext.DuzenOdemeTutar, Source={x:Reference Sayfa}, Converter={StaticResource ParaGiris}}"
                                                   Placeholder="0,00 ₺" Style="{StaticResource EntryMoney}" />
                                        </Border>
                                        <Button Grid.Column="1" Text="Ekle"
                                                Command="{Binding BindingContext.OdemeEkleCommand, Source={x:Reference Sayfa}}"
                                                CommandParameter="{Binding .}" Padding="16,0" />
                                    </Grid>
                                    <VerticalStackLayout BindableLayout.ItemsSource="{Binding Odemeler}" Spacing="0" Margin="0,8,0,0">
                                        <BindableLayout.ItemTemplate>
                                            <DataTemplate>
                                                <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="8" Padding="0,3">
                                                    <Label Text="{Binding Tarih, StringFormat='{0:dd MMM}'}"
                                                           Style="{StaticResource LblPageSub}" FontSize="12" />
                                                    <Label Grid.Column="1" Text="{Binding Tutar, Converter={StaticResource ParaBicim}}"
                                                           Style="{StaticResource LblPageSub}" FontSize="12" />
                                                    <Button Grid.Column="2" Text="Sil" Style="{StaticResource BtnRowDelete}"
                                                            Command="{Binding BindingContext.OdemeSilCommand, Source={x:Reference Sayfa}}"
                                                            CommandParameter="{Binding .}" />
                                                </Grid>
                                            </DataTemplate>
                                        </BindableLayout.ItemTemplate>
                                    </VerticalStackLayout>
```

- [ ] **Step 4: Derlemeyi doğrula**

```bash
taskkill //F //IM Kasa.App.exe 2>/dev/null; dotnet build Kasa.App/Kasa.App.csproj
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Kasa.App/Views/KrediKartlariPage.xaml
git -c user.name="Musa Sevinç" -c user.email="musa@royalmezat.com" commit -m "feat(app): kart sayfası türetilmiş borç + kırılım + ödeme ekle/geçmiş (XAML)"
```

---

## Task 11: Tam test geçişi (regresyon kalkanı)

- [ ] **Step 1: Tüm test projelerini çalıştır**

```bash
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj
```
Expected: HEPSİ PASS. Özellikle `HaftalikHesapTests` / `AylikHesapTests` / `KrediKartiErtelemeTests` / `HaziranSenaryoTests` **değişmeden** geçmeli (HesapMotoru'na dokunulmadı — çift-sayma yok).

- [ ] **Step 2: WPF derlemesi**

```bash
taskkill //F //IM Kasa.App.exe 2>/dev/null; dotnet build Kasa.App/Kasa.App.csproj
```
Expected: PASS.

---

## Task 12: Prod migration + deploy — YALNIZ KULLANICI ONAYIYLA

> **DUR:** Bu task otomatik çalıştırılmaz. Kod merge edildikten sonra kullanıcıya sun, açık onay bekle. Prod DB'ye yıkıcı olmayan şema eklemesi (KrediKartlari tablosunun eklendiği gibi).

- [ ] **Step 1: Prod şema el-SQL'ini hazırla (deploy sırasında uygulanır)**

`EnsureCreated()` mevcut prod DB'sine kolon/tablo EKLEMEZ. Aşağıdaki SQL, prod SQLite dosyasına (VPS <VPS_IP>, docker compose `docker-compose.nginx.yml`) **yedek alındıktan sonra** elle uygulanır:

```sql
ALTER TABLE Islemler ADD COLUMN KrediKartiId INTEGER NULL;

CREATE TABLE KartOdemeler (
    Id INTEGER NOT NULL CONSTRAINT PK_KartOdemeler PRIMARY KEY AUTOINCREMENT,
    KrediKartiId INTEGER NOT NULL,
    Tarih TEXT NOT NULL,
    Tutar TEXT NOT NULL,
    "Not" TEXT NULL,
    CONSTRAINT FK_KartOdemeler_KrediKartlari_KrediKartiId
        FOREIGN KEY (KrediKartiId) REFERENCES KrediKartlari (Id) ON DELETE CASCADE
);
CREATE INDEX IX_KartOdemeler_KrediKartiId ON KartOdemeler (KrediKartiId);
CREATE INDEX IX_Islemler_KrediKartiId ON Islemler (KrediKartiId);
```

Not: SQLite `decimal`'i TEXT olarak saklar (EF Sqlite varsayılanı). Mevcut `Islemler.TutarTl`/`KrediKartlari.Borc` sütunlarının depolama tipini referans alarak doğrula; farklıysa aynı tipi kullan.

- [ ] **Step 2: Yedek → SQL uygula → backend redeploy → uçtan doğrula**

- Prod DB dosyasının yedeğini al.
- Yukarıdaki SQL'i uygula.
- Backend'i yeni imaja redeploy et.
- `GET /api/kredikartlari` türetilmiş alanları döndürüyor mu, bir test ödemesi ekleyip borç düştü mü — uçtan doğrula.

---

## Self-Review

**Spec coverage:**
- §3a Islemler.KrediKartiId → Task 2/3/5/7 ✅
- §3b KartOdemeler tablosu → Task 2/3/4 ✅
- §3c Borc = açılış anlamı (isim korunur) → Task 3 (Borc=AcilisBorc DTO), Task 9 (Duzenle açılış) ✅
- §4 Core saf fonksiyon → Task 1 ✅ (sunucu türetimi Task 3, HesapMotoru dokunulmadı)
- §5 API: Islemler KrediKartiId + Tip zorla, KartOdemeler CRUD, KrediKartiDto zenginleşir, migration → Task 3/4/12 ✅
- §6 ApiClient + SahteApi → Task 5/6 ✅
- §7a İşlemler Tip çipleri + Kart çipleri → Task 7/8 ✅
- §7b KrediKartlari türetilmiş borç + kırılım + ödeme ekle + açılış borcu + geçmiş → Task 9/10 ✅
- §8 testler: Core, HesapMotoru regresyon, ApiClient/VM → Task 1/4/5/7/9/11 ✅
- §9 sürüm/deploy kullanıcı onayıyla → Task 12 ✅

**Placeholder taraması:** TODO/TBD yok; tüm kod blokları tam.

**Tip tutarlılığı:** `KrediKartiTuretilmisDto` (server) alan adları camelCase JSON'da `guncelBorc/acilisBorc/harcamaToplam/odemeToplam` → client `KrediKartiDto` aynı isimli trailing alanlarla eşleşir. `KartOdemeYaz`/`KartOdemeDto` server `KartOdemeEntity` alanlarıyla (KrediKartiId/Tarih/Tutar/Not) birebir. `SecTipCommand`/`SecKartCommand`/`KartSeciciGorunur`/`TipCipleri`/`KartCipleri` VM ve XAML'de aynı adla. `OdemeEkleCommand`/`OdemeSilCommand`/`DuzenOdemeTutar` VM ve XAML'de aynı.

---

## Execution Handoff

Plan `docs/plans/2026-07-15-kart-borc-hareketleri.md`'ye yazıldı. İki yürütme seçeneği:

1. **Subagent-Driven (önerilen)** — her task için taze subagent, task arası inceleme, hızlı iterasyon.
2. **Inline Execution** — bu oturumda executing-plans ile checkpoint'li toplu yürütme.
