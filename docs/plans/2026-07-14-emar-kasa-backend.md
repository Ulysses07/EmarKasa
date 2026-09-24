# Emar Kasa — Backend (Plan 1/4) Uygulama Planı

> **Tarihsel plan:** Bu belge yazıldığı günün planıdır; içindeki `EnsureCreated`/elle SQL/DB yeniden oluşturma adımları artık geçersizdir — şema açılışta `SemaGuncelleyici` ile güncellenir, dağıtım/yedek için `deploy/README.md`'ye bakın.

> **Ajan işçiler için:** GEREKLİ ALT-SKILL: Bu planı görev görev uygulamak için
> superpowers:subagent-driven-development (önerilir) veya superpowers:executing-plans kullan.
> Adımlar takip için checkbox (`- [ ]`) sözdizimi kullanır.

**Goal:** Backend'i native uygulamalara hazırla: login token'ı gövdede döndür, Kredi Kartı entity + CRUD ekle, ve `HesapMotoru`'nu kredi kartı ertelemesi kuralına göre değiştir (ağır testli).

**Architecture:** Değişiklikler saf çekirdek (`Kasa.Core/HesapMotoru.cs`) + API katmanında (`Kasa.Api`). Hesap kuralı: bir ayın `KrediKarti` harcaması o ay değil, **bir sonraki ay sonunda** kasadan ve kanaldan düşülür. Kart entity'sinin limit/borç alanları elle girilir, hesaba girmez (yalnız görüntüleme).

**Tech Stack:** .NET 10, minimal API, EF Core 10 (Sqlite, `EnsureCreated`), xUnit, `WebApplicationFactory<Program>`.

**Referans spec:** `docs/specs/2026-07-14-emar-kasa-native-design.md` §4, §7.

---

## Dosya yapısı

- Değiştir: `Kasa.Core/HesapMotoru.cs` — aylık + haftalık K.K erteleme.
- Değiştir: `Kasa.Core/Domain.cs:6-9` — `KrediKarti` yorumunu güncelle (opsiyonel, davranış değil).
- Oluştur test: `Kasa.Core.Tests/KrediKartiErtelemeTests.cs`.
- Değiştir: `Kasa.Api/Data/Entities.cs` — `KrediKartiEntity` ekle.
- Değiştir: `Kasa.Api/Data/KasaDbContext.cs` — `DbSet<KrediKartiEntity>` ekle.
- Değiştir: `Kasa.Api/Program.cs` — `/api/kredikartlari` CRUD; login yanıtına `token`.
- Oluştur test: `Kasa.Api.Tests/KrediKartiCrudTests.cs`, `Kasa.Api.Tests/TokenGovdeTests.cs`.

**Kapsam dışı (Plan 4'e ertelendi):** SPA sunumunun kaldırılması (`UseDefaultFiles`/`UseStaticFiles`/`MapFallbackToFile` + `wwwroot` + Dockerfile web aşaması). Native yayınlanana kadar web çalışır kalmalı; boşluk oluşmasın.

---

## Task 1: HesapMotoru — kredi kartı ertelemesi (aylık + haftalık birlikte)

Aylık ve haftalık değişiklik **aynı görevde** yapılır: mevcut değişmez test
`Haftalik_kasa_sonucu_toplami_aylik_ay_sonucu_toplamina_esittir` yalnız ikisi birlikte
uygulanınca yeşil kalır (birini tek başına yaparsan Σ haftalık ≠ Σ aylık olur).

**Files:**
- Test: `Kasa.Core.Tests/KrediKartiErtelemeTests.cs` (oluştur)
- Modify: `Kasa.Core/HesapMotoru.cs` (`AylikHesapla` ~64-110, `HaftalikHesapla` ~20-57)
- Modify: `Kasa.Core/Domain.cs:8` (yorum)

- [ ] **Step 1: Başarısız testleri yaz**

`Kasa.Core.Tests/KrediKartiErtelemeTests.cs`:

```csharp
using Kasa.Core;

namespace Kasa.Core.Tests;

public class KrediKartiErtelemeTests
{
    private static readonly Kanal[] UcKanal =
    {
        new("MEZAT"), new("PERAKENDE"), new("TOPTAN"),
    };

    // ---- AYLIK ----

    [Fact]
    public void Bu_ayin_kk_si_bu_ayin_sonucundan_dusulmez()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 5), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var rapor = HesapMotoru.AylikHesapla(2026, 3, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        var mezat = rapor.Kanallar.Single(k => k.Kanal == "MEZAT");

        Assert.Equal(0m, mezat.KrediKarti);   // Mart K.K'sı Mart'a yansımaz
        Assert.Equal(0m, mezat.AySonucu);
    }

    [Fact]
    public void Onceki_ayin_kk_si_bu_ayin_sonucundan_dusulur()
    {
        // Mart'ta MEZAT ile 10.000 kart harcaması → Nisan sonucunda -10.000.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 5), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var nisan = HesapMotoru.AylikHesapla(2026, 4, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        var mezat = nisan.Kanallar.Single(k => k.Kanal == "MEZAT");

        Assert.Equal(10_000m, mezat.KrediKarti);
        Assert.Equal(-10_000m, mezat.AySonucu);
    }

    [Fact]
    public void Ocak_ayinin_kk_terimi_bir_onceki_yilin_aralik_ayindan_gelir()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 31));
        var islemler = new[]
        {
            new Islem(new DateOnly(2025, 12, 10), "K.K", 5_000m, "TOPTAN", GiderTipi.KrediKarti),
        };

        var ocak = HesapMotoru.AylikHesapla(2026, 1, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        Assert.Equal(5_000m, ocak.Kanallar.Single(k => k.Kanal == "TOPTAN").KrediKarti);
    }

    // ---- HAFTALIK (KASA) ----

    [Fact]
    public void Kk_kendi_haftasinda_kasadan_dusmez()
    {
        // Mart'ta tek dönem, sadece 10.000 kart harcaması. Kasa değişmemeli.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 3), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var ozet = HesapMotoru.HaftalikHesapla(100_000m, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        Assert.Equal(0m, ozet[^1].ToplamGiden);       // KK bu hafta çıkmadı
        Assert.Equal(100_000m, ozet[^1].KasaDevir);   // kasa aynı
    }

    [Fact]
    public void Kk_bir_sonraki_ayin_son_doneminde_kasadan_duser()
    {
        // Mart K.K 10.000 → Nisan'ın SON döneminde kasadan çıkar.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 5), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var ozetler = HesapMotoru.HaftalikHesapla(100_000m, UcKanal, islemler, Array.Empty<Gelen>(), donemler);

        // Nisan'ın son dönemi = ay=4 olan dönemlerin en geç Start'lısı.
        var nisanSon = ozetler.Where(o => o.Donem.Ay == 4).OrderBy(o => o.Donem.Start).Last();
        Assert.Equal(10_000m, nisanSon.ToplamGiden);   // ertelenen KK burada çıktı

        // En sondaki kasa devri: 100.000 - 10.000 = 90.000
        Assert.Equal(90_000m, ozetler[^1].KasaDevir);
    }

    [Fact]
    public void Kk_kanal_haftalik_devrini_etkilemez()
    {
        var donemler = DonemUretici.Uret(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 3, 3), "K.K", 10_000m, "MEZAT", GiderTipi.KrediKarti),
        };

        var ozet = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, Array.Empty<Gelen>(), donemler);
        var mezat = ozet[^1].Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal(0m, mezat.Giden);   // kanal devri yalnız Cari sayar
        Assert.Equal(0m, mezat.Devir);
    }

    [Fact]
    public void Iki_ay_boyunca_haftalik_kasa_toplami_aylik_ay_sonucu_toplamina_esittir()
    {
        // Haziran KK'sı Temmuz'a ertelenir; Σ haftalık (Haz+Tem) == Σ aylık (Haz+Tem).
        var donemler = DonemUretici.Uret(new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 31));
        var ilk = donemler[0].Start;
        var gelenler = new[] { new Gelen(ilk, "MEZAT", 200_000m) };
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 6, 3), "MEZAT-cari", 50_000m, "MEZAT", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 6, 10), "PER-kk", 45_500m, "PERAKENDE", GiderTipi.KrediKarti),
        };

        var haftalik = HesapMotoru.HaftalikHesapla(0m, UcKanal, islemler, gelenler, donemler);
        decimal haftalikToplam = haftalik.Sum(o => o.KasaSonucu);

        var haziran = HesapMotoru.AylikHesapla(2026, 6, UcKanal, islemler, gelenler, donemler);
        var temmuz = HesapMotoru.AylikHesapla(2026, 7, UcKanal, islemler, gelenler, donemler);
        decimal aylikToplam = haziran.Kanallar.Sum(k => k.AySonucu) + temmuz.Kanallar.Sum(k => k.AySonucu);

        Assert.Equal(haftalikToplam, aylikToplam);
    }
}
```

- [ ] **Step 2: Testleri çalıştır, başarısız olduğunu doğrula**

Run: `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj --filter FullyQualifiedName~KrediKartiErteleme`
Expected: FAIL (aylık/haftalık henüz eski davranışta — beklenen değerler tutmaz).

- [ ] **Step 3: `AylikHesapla`'yı değiştir (kk terimi önceki aydan)**

`Kasa.Core/HesapMotoru.cs` içinde `AylikHesapla` gövdesinde, `var satirlar = ...` satırından ÖNCE şunu ekle:

```csharp
        // Kredi kartı ertelemesi: bu ayın K.K'sı bu ay DÜŞÜLMEZ; ödemesi gelecek ay
        // yapıldığı için bir ÖNCEKİ ayın K.K'sı bu ayın sonucundan düşülür.
        int oncekiYil = ay == 1 ? yil - 1 : yil;
        int oncekiAy = ay == 1 ? 12 : ay - 1;
        var oncekiAyKk = islemler
            .Where(i => i.Tarih.Year == oncekiYil && i.Tarih.Month == oncekiAy
                        && i.Tip == GiderTipi.KrediKarti)
            .ToList();
```

Ardından kanal döngüsündeki `kk` satırını değiştir:

```csharp
            // ÖNCE: ayinIslemleri.Where(...KrediKarti...)
            decimal kk = oncekiAyKk.Where(i => i.Kanal == kanal.Ad).Sum(i => i.TutarTl);
```

- [ ] **Step 4: `HaftalikHesapla`'yı değiştir (kasa ertelemesi)**

`Kasa.Core/HesapMotoru.cs` içinde `HaftalikHesapla`'da, `foreach (var donem in sirali)` döngüsünden ÖNCE şunu ekle:

```csharp
        // Kredi kartı ertelemesi (kasa): bir ayın K.K'sı o ay kasadan çıkmaz;
        // ödemesi bir SONRAKİ ayın SON döneminde toplu olarak kasadan çıkar.
        var aylikKkToplam = islemler
            .Where(i => i.Tip == GiderTipi.KrediKarti)
            .GroupBy(i => (i.Tarih.Year, i.Tarih.Month))
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TutarTl));

        var ayinSonDonemi = sirali
            .GroupBy(d => (d.Yil, d.Ay))
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.Start).Last());
```

Döngü içinde `decimal toplamGiden = donemIslem.Sum(i => i.TutarTl);` satırını şu blokla değiştir:

```csharp
            // KK kendi döneminde kasadan çıkmaz (ertelenir).
            decimal toplamGiden = donemIslem.Where(i => i.Tip != GiderTipi.KrediKarti).Sum(i => i.TutarTl);
            // Bu dönem ayının SON dönemiyse: bir önceki ayın KK'sı şimdi kasadan çıkar.
            if (ayinSonDonemi.TryGetValue((donem.Yil, donem.Ay), out var sonDonem) && sonDonem == donem)
            {
                int oncekiYil = donem.Ay == 1 ? donem.Yil - 1 : donem.Yil;
                int oncekiAy = donem.Ay == 1 ? 12 : donem.Ay - 1;
                if (aylikKkToplam.TryGetValue((oncekiYil, oncekiAy), out var ertelenenKk))
                    toplamGiden += ertelenenKk;
            }
```

Ayrıca sınıf özet yorumunu (docstring, ~16-18. satır) güncelle:

```csharp
    /// <summary>
    /// Dönem dönem haftalık özet üretir. Kanal devri yalnız o kanalın Cari tipli
    /// gidenini sayar. Kasa devri Cari + SabitGider + Ortak gidenleri kendi döneminde
    /// sayar; KrediKarti ise ertelenir — bir sonraki ayın son döneminde kasadan çıkar.
    /// Devirler tarih sırasına göre zincirlenir; ilk dönemin girişi açılış bakiyeleridir.
    /// </summary>
```

`Domain.cs:8` yorumunu da güncelle:

```csharp
    KrediKarti   // K.K — bir sonraki ay sonunda kasadan/kanaldan düşülür (ertelemeli)
```

- [ ] **Step 5: Tüm çekirdek testlerini çalıştır**

Run: `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj`
Expected: PASS (yeni 7 test + mevcut 11 test dahil hepsi yeşil; özellikle
`Haftalik_kasa_sonucu_toplami_aylik_ay_sonucu_toplamina_esittir` hâlâ geçer).

- [ ] **Step 6: Commit**

```bash
git add Kasa.Core/HesapMotoru.cs Kasa.Core/Domain.cs Kasa.Core.Tests/KrediKartiErtelemeTests.cs
git commit -m "feat(hesap): kredi kartı harcamasını bir sonraki ay sonuna ertele (kasa + aylık)"
```

---

## Task 2: KrediKartiEntity + DbContext

**Files:**
- Modify: `Kasa.Api/Data/Entities.cs`
- Modify: `Kasa.Api/Data/KasaDbContext.cs`

- [ ] **Step 1: Entity'yi ekle**

`Kasa.Api/Data/Entities.cs` sonuna:

```csharp
public class KrediKartiEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public DateOnly KesimTarihi { get; set; }
    public DateOnly SonOdemeTarihi { get; set; }
    public decimal Limit { get; set; }
    public decimal Borc { get; set; }
}
```

- [ ] **Step 2: DbSet'i ekle**

`Kasa.Api/Data/KasaDbContext.cs` içine (diğer DbSet'lerin yanına):

```csharp
    public DbSet<KrediKartiEntity> KrediKartlari => Set<KrediKartiEntity>();
```

- [ ] **Step 3: Derlemenin geçtiğini doğrula**

Run: `dotnet build Kasa.Api/Kasa.Api.csproj`
Expected: PASS (0 hata).

- [ ] **Step 4: Commit**

```bash
git add Kasa.Api/Data/Entities.cs Kasa.Api/Data/KasaDbContext.cs
git commit -m "feat(api): KrediKartiEntity + DbSet"
```

---

## Task 3: Kredi kartı CRUD uçları

**Files:**
- Test: `Kasa.Api.Tests/KrediKartiCrudTests.cs` (oluştur)
- Modify: `Kasa.Api/Program.cs` (Cariler CRUD bloğundan sonra, ~178. satır civarı)

- [ ] **Step 1: Başarısız testi yaz**

`Kasa.Api.Tests/KrediKartiCrudTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class KrediKartiCrudTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public KrediKartiCrudTests(KasaWebFactory factory) => _factory = factory;

    private record KartYanit(int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc);

    [Fact]
    public async Task Editor_kart_ekleyip_listeleyip_guncelleyip_silebilir()
    {
        var client = await _factory.EditorClientAsync();

        var olustur = await client.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Bonus", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 100_000m, borc = 30_000m,
        });
        Assert.Equal(HttpStatusCode.Created, olustur.StatusCode);
        var eklenen = await olustur.Content.ReadFromJsonAsync<KartYanit>();
        Assert.NotNull(eklenen);
        Assert.Equal("Bonus", eklenen!.Ad);
        Assert.Equal(100_000m, eklenen.Limit);

        var guncelle = await client.PutAsJsonAsync($"/api/kredikartlari/{eklenen.Id}", new
        {
            ad = "Bonus", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25",
            limit = 100_000m, borc = 45_000m,
        });
        Assert.Equal(HttpStatusCode.OK, guncelle.StatusCode);

        var liste = await client.GetFromJsonAsync<List<KartYanit>>("/api/kredikartlari");
        Assert.Equal(45_000m, liste!.Single(k => k.Id == eklenen.Id).Borc);

        var sil = await client.DeleteAsync($"/api/kredikartlari/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
    }

    [Fact]
    public async Task Izleyici_kart_okur_ama_ekleyemez()
    {
        var editor = await _factory.EditorClientAsync();
        await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" });

        var izleyici = _factory.CreateClient();
        var giris = await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle123" });
        giris.EnsureSuccessStatusCode();

        var okuma = await izleyici.GetAsync("/api/kredikartlari");
        Assert.Equal(HttpStatusCode.OK, okuma.StatusCode);

        var yazma = await izleyici.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "X", kesimTarihi = "2026-07-05", sonOdemeTarihi = "2026-07-25", limit = 1m, borc = 0m,
        });
        Assert.Equal(HttpStatusCode.Forbidden, yazma.StatusCode);
    }
}
```

- [ ] **Step 2: Testi çalıştır, başarısız olduğunu doğrula**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter FullyQualifiedName~KrediKartiCrud`
Expected: FAIL (404 — uçlar yok).

- [ ] **Step 3: CRUD uçlarını ekle**

`Kasa.Api/Program.cs` içinde `// Islemler` yorumundan ÖNCE (Cariler bloğunun sonrası, ~179. satır) şunu ekle:

```csharp
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
```

`KrediKartiEntity` `Kasa.Api.Data` isim alanında; `Program.cs` zaten `using Kasa.Api.Data;`
içeriyorsa ek using gerekmez (yoksa dosya başına ekle).

- [ ] **Step 4: Testi çalıştır, geçtiğini doğrula**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter FullyQualifiedName~KrediKartiCrud`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Kasa.Api/Program.cs Kasa.Api.Tests/KrediKartiCrudTests.cs
git commit -m "feat(api): /api/kredikartlari CRUD uçları (editör yazar, izleyici okur)"
```

---

## Task 4: Login yanıtına token ekle

Native istemci token'ı gövdeden okuyup `SecureStorage`'a yazar. Cookie zararsız kalır.

**Files:**
- Test: `Kasa.Api.Tests/TokenGovdeTests.cs` (oluştur)
- Modify: `Kasa.Api/Program.cs:113` (login `return`)

- [ ] **Step 1: Başarısız testi yaz**

`Kasa.Api.Tests/TokenGovdeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Kasa.Api.Tests;

public class TokenGovdeTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public TokenGovdeTests(KasaWebFactory factory) => _factory = factory;

    private record GirisYanit(string Rol, string Token);

    [Fact]
    public async Task Login_govdede_token_doner()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "kasa123" });
        resp.EnsureSuccessStatusCode();

        var yanit = await resp.Content.ReadFromJsonAsync<GirisYanit>();
        Assert.NotNull(yanit);
        Assert.Equal("editor", yanit!.Rol);
        Assert.False(string.IsNullOrWhiteSpace(yanit.Token));
    }

    [Fact]
    public async Task Govdeden_alinan_token_bearer_olarak_calisir()
    {
        // 1) login ol, token'ı gövdeden al
        var loginClient = _factory.CreateClient();
        var resp = await loginClient.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "kasa123" });
        var yanit = await resp.Content.ReadFromJsonAsync<GirisYanit>();

        // 2) HİÇ login olmamış (cookie'siz) ayrı istemci: yalnız Bearer header ile
        var bearerClient = _factory.CreateClient();
        bearerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", yanit!.Token);

        var me = await bearerClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
}
```

- [ ] **Step 2: Testi çalıştır, başarısız olduğunu doğrula**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter FullyQualifiedName~TokenGovde`
Expected: FAIL (`Login_govdede_token_doner` → Token boş/null; deserialize başarısız).

- [ ] **Step 3: Login yanıtını değiştir**

`Kasa.Api/Program.cs` login gövdesindeki son satırı değiştir:

```csharp
    // ÖNCE: return Results.Ok(new { rol });
    return Results.Ok(new { rol, token });
```

- [ ] **Step 4: Testi çalıştır, geçtiğini doğrula**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter FullyQualifiedName~TokenGovde`
Expected: PASS (ikisi de).

- [ ] **Step 5: Commit**

```bash
git add Kasa.Api/Program.cs Kasa.Api.Tests/TokenGovdeTests.cs
git commit -m "feat(api): login yanıtına token ekle (native Bearer için)"
```

---

## Task 5: Tam suite doğrulama

- [ ] **Step 1: Her iki test projesini de çalıştır**

Run:
```bash
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj
```
Expected: İkisi de PASS. Core: 11 mevcut + 7 yeni = 18. Api: 18 mevcut + 2 (kredi kartı) + 2 (token) = 22.

- [ ] **Step 2: (Doğrulama, commit yok)** Mevcut `AuthTests`, `CrudTests`, `RaporTests`, `StatikServisTests` hâlâ yeşil olmalı — değiştirilmedi.

---

## Dağıtım notu (Plan 4'te uygulanacak, burada DEĞİL)

- `EnsureCreated()` mevcut DB'ye yeni `KrediKartlari` tablosunu **eklemez**. Canlı VPS DB'sinde
  henüz gerçek veri yok; Plan 4 deploy'unda `/opt/kasa/deploy/kasa-data/kasa.db` silinip yeniden
  oluşturulacak (veya migration'a geçilecek). Bu planda kod EnsureCreated ile çalışır — testlerde
  in-memory DB her seferinde sıfırdan kurulur, sorun yok.
- SPA sunumu kaldırma + Dockerfile web aşaması çıkarma Plan 4'te (native yayınlandıktan sonra).
```
