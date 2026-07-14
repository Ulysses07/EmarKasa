# Emar Kasa — Kasa.ApiClient (Plan 2/4) Uygulama Planı

> **Ajan işçiler için:** GEREKLİ ALT-SKILL: Bu planı görev görev uygulamak için
> superpowers:subagent-driven-development (önerilir) veya superpowers:executing-plans kullan.
> Adımlar takip için checkbox (`- [ ]`) sözdizimi kullanır.

**Goal:** Native MAUI uygulamasının kullanacağı, saf .NET, EF'e ve `Kasa.Core`'a bağımlı OLMAYAN,
bağımsız test edilebilir bir API istemci kütüphanesi (`Kasa.ApiClient`) + xUnit test projesi
(`Kasa.ApiClient.Tests`) yaz. İstemci: DTO record'ları, tiplı `KasaApiClient` (`HttpClient`
sarmalayıcı), `ITokenStore` soyutlaması. Her isteğe `Bearer <token>` ekler; 401'i tiplı
istisnayla yüzeye çıkarır.

**Architecture:** İstemci gerçek `Kasa.Api` yüzeyini birebir yansıtır. Sunucu JSON'u
**ASP.NET Core Web varsayılanı** (camelCase, `JsonStringEnumConverter` ile enum'lar STRING,
`DateOnly` → `"2026-07-05"`, `decimal` → sayı). İstemci de aynı seçenekleri kullanır
(`JsonSerializerDefaults.Web` + `JsonStringEnumConverter`). `Kasa.Core`'a referans YOK — bu yüzden
`GiderTipi` enum'u istemcide yeniden tanımlanır (string eşleşmeyle uyumlu kalır).

**Tech Stack:** .NET 10, `System.Net.Http.Json` (framework içi), `System.Text.Json`, xUnit,
sahte `HttpMessageHandler`. Harici NuGet bağımlılığı YOK.

**Referans:** spec `docs/specs/2026-07-14-emar-kasa-native-design.md` §3, §5. API yüzeyi:
`Kasa.Api/Program.cs`, DTO şekilleri `Kasa.Api/Dtos.cs` + `Kasa.Core/HesapMotoru.cs` record'ları.

---

## API yüzeyi referansı (uygulama sırasında buna göre eşle)

Tüm gövde/yanıt alanları **camelCase**. Enum'lar **string**. `DateOnly` **"yyyy-MM-dd"**.

**Auth (grup dışı):**
- `POST /api/auth/login` gövde `{ kullanici: string?, sifre: string }` → `{ rol: string, token: string }` (401 = başarısız)
- `POST /api/auth/logout` → 200
- `GET  /api/auth/me` → `{ rol: string }` (401 = token yok/geçersiz)

**Korumalı grup `/api` (oturum açmış herkes okur; mutasyonlar Editor):**
- `GET  /kanallar` → `[{ id, ad, aktif, sira, acilisDevri }]`
- `POST /kanallar` (Editor) gövde `{ ad, aktif, sira, acilisDevri }` → 201 entity
- `PUT  /kanallar/{id}` (Editor) → 200 entity · `DELETE /kanallar/{id}` (Editor) → 204
- `GET  /cariler?ara=` → `[{ id, ad, aktif }]`
- `POST /cariler` (Editor) `{ ad, aktif }` → 201 · `PUT/DELETE /cariler/{id}` (Editor)
- `GET  /kredikartlari` → `[{ id, ad, kesimTarihi, sonOdemeTarihi, limit, borc }]`
- `POST /kredikartlari` (Editor) `{ ad, kesimTarihi, sonOdemeTarihi, limit, borc }` → 201 · `PUT/DELETE /kredikartlari/{id}` (Editor)
- `GET  /islemler?baslangic=&bitis=&kanal=&cari=` → `[{ id, tarih, cari, tutarTl, kanal, tip, not }]` (tip = "Cari"|"SabitGider"|"KrediKarti")
- `POST /islemler` (Editor) `{ tarih, cari, tutarTl, kanal, tip, not }` → 201 · `PUT/DELETE /islemler/{id}` (Editor)
- `GET  /gelenler?donemStart=` → `[{ id, donemStart, kanal, tutarTl }]`
- `PUT  /gelenler` (Editor, upsert) `{ donemStart, kanal, tutarTl }` → 200 entity
- `GET  /ayarlar` → `{ takipBaslangic, kasaAcilisDevri, izleyiciSifreVarMi }`
- `PUT  /ayarlar` (Editor) `{ takipBaslangic, kasaAcilisDevri }` → 200
- `PUT  /ayarlar/izleyici-sifre` (Editor) `{ yeniSifre }` → 200 (boşsa 400)
- `GET  /donemler` → `[{ start, end, yil, ay }]`
- `GET  /rapor/haftalik` → `[{ donem:{start,end,yil,ay}, kanallar:[{kanal,gelen,giden,sonuc,devir}], toplamGelen, toplamGiden, kasaSonucu, kasaDevir }]`
- `GET  /rapor/aylik?yil=&ay=` → `{ yil, ay, kanallar:[{kanal,gelen,cariGiden,sabitGider,krediKarti,ortakPay,aySonucu}] }`
- `GET  /rapor/panel` → `{ guncelKasa, kanallar:[{kanal,bakiye}], buHaftaSonucu, buAySonucu }`

---

## Dosya yapısı

- Oluştur: `Kasa.ApiClient/Kasa.ApiClient.csproj` (net10.0, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`).
- Oluştur: `Kasa.ApiClient/Dtos.cs` — tüm DTO record'ları + `GiderTipi` enum.
- Oluştur: `Kasa.ApiClient/ITokenStore.cs` — `ITokenStore` + `BellekTokenStore` (test/varsayılan).
- Oluştur: `Kasa.ApiClient/KasaApiException.cs` — tiplı hata (StatusCode taşır).
- Oluştur: `Kasa.ApiClient/KasaApiClient.cs` — istemci.
- Oluştur: `Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj` (net10.0, xunit, `Kasa.ApiClient` referansı).
- Oluştur: `Kasa.ApiClient.Tests/SahteHandler.cs` — kaydeden/canned yanıt veren `HttpMessageHandler`.
- Oluştur test dosyaları: `TokenStoreTests.cs`, `AuthTests.cs`, `OkumaTests.cs`, `MutasyonTests.cs`.
- Değiştir: `Kasa.sln` — iki yeni projeyi ekle.

**Kapsam dışı (YAGNI):** MAUI `SecureStorage` implementasyonu (Plan 3'te, MAUI head'de), retry/polly,
önbellek, offline. `BellekTokenStore` hem testte hem varsayılanda yeter; gerçek güvenli depolama Plan 3.

---

## Task 1: Projeleri iskele + çözüme ekle + duman testi

**Files:**
- Oluştur: `Kasa.ApiClient/Kasa.ApiClient.csproj`
- Oluştur: `Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj`
- Oluştur: `Kasa.ApiClient.Tests/DumanTests.cs`
- Değiştir: `Kasa.sln`

- [ ] **Step 1: Projeleri oluştur**

Önce mevcut bir csproj'u (`Kasa.Core/Kasa.Core.csproj`, `Kasa.Api.Tests/Kasa.Api.Tests.csproj`) OKU;
TargetFramework, xunit paket sürümlerini birebir eşle (sürüm uydurma).

`Kasa.ApiClient/Kasa.ApiClient.csproj` (kütüphane; `Kasa.Core`'a referans YOK):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

`Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj` — `Kasa.Api.Tests.csproj`'daki xunit/Microsoft.NET.Test.Sdk/
coverlet PackageReference'larını AYNI sürümlerle kopyala, tek ProjectReference `../Kasa.ApiClient/Kasa.ApiClient.csproj`.

- [ ] **Step 2: Çözüme ekle**
```bash
cd "C:\Users\burak\source\repos\Kasa"
dotnet sln add Kasa.ApiClient/Kasa.ApiClient.csproj
dotnet sln add Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj
```

- [ ] **Step 3: Duman testi yaz** — `Kasa.ApiClient.Tests/DumanTests.cs`:
```csharp
namespace Kasa.ApiClient.Tests;

public class DumanTests
{
    [Fact]
    public void Proje_derlenir_ve_test_kosar() => Assert.True(true);
}
```

- [ ] **Step 4: Derle + test**
```bash
dotnet build Kasa.ApiClient/Kasa.ApiClient.csproj
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj
```
Expected: PASS (1 test).

- [ ] **Step 5: Commit**
```bash
git add Kasa.ApiClient/ Kasa.ApiClient.Tests/ Kasa.sln
git commit -m "feat(apiclient): Kasa.ApiClient + test projesi iskelesi"
```

---

## Task 2: DTO'lar + JSON seçenekleri + ITokenStore

**Files:**
- Oluştur: `Kasa.ApiClient/Dtos.cs`, `Kasa.ApiClient/ITokenStore.cs`
- Oluştur test: `Kasa.ApiClient.Tests/TokenStoreTests.cs`

- [ ] **Step 1: DTO'ları yaz** — `Kasa.ApiClient/Dtos.cs`:
```csharp
namespace Kasa.ApiClient;

public enum GiderTipi { Cari, SabitGider, KrediKarti }

public record LoginYanit(string Rol, string Token);

public record KanalDto(int Id, string Ad, bool Aktif, int Sira, decimal AcilisDevri);
public record CariDto(int Id, string Ad, bool Aktif);
public record IslemDto(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not);
public record GelenDto(int Id, DateOnly DonemStart, string Kanal, decimal TutarTl);
public record KrediKartiDto(int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc);
public record AyarlarDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri, bool IzleyiciSifreVarMi);

public record DonemDto(DateOnly Start, DateOnly End, int Yil, int Ay);
public record KanalHaftalikDto(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir);
public record HaftalikOzetDto(
    DonemDto Donem,
    IReadOnlyList<KanalHaftalikDto> Kanallar,
    decimal ToplamGelen,
    decimal ToplamGiden,
    decimal KasaSonucu,
    decimal KasaDevir);
public record KanalAylikDto(string Kanal, decimal Gelen, decimal CariGiden, decimal SabitGider, decimal KrediKarti, decimal OrtakPay, decimal AySonucu);
public record AylikRaporDto(int Yil, int Ay, IReadOnlyList<KanalAylikDto> Kanallar);
public record KanalBakiyeDto(string Kanal, decimal Bakiye);
public record PanelDto(decimal GuncelKasa, IReadOnlyList<KanalBakiyeDto> Kanallar, decimal BuHaftaSonucu, decimal BuAySonucu);

// Mutasyon gövdeleri (Id sunucuda atanır; create'te gönderilmez)
public record KanalYaz(string Ad, bool Aktif, int Sira, decimal AcilisDevri);
public record CariYaz(string Ad, bool Aktif);
public record IslemYaz(DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not);
public record GelenYaz(DateOnly DonemStart, string Kanal, decimal TutarTl);
public record KrediKartiYaz(string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc);
public record AyarYaz(DateOnly TakipBaslangic, decimal KasaAcilisDevri);
```

- [ ] **Step 2: ITokenStore + BellekTokenStore** — `Kasa.ApiClient/ITokenStore.cs`:
```csharp
namespace Kasa.ApiClient;

/// <summary>Token'ın platforma bağımsız saklanması. MAUI head SecureStorage'lı impl verir (Plan 3).</summary>
public interface ITokenStore
{
    Task<string?> OkuAsync();
    Task YazAsync(string token);
    Task TemizleAsync();
}

/// <summary>Bellek-içi varsayılan (testler + geçici). Kalıcı değildir.</summary>
public sealed class BellekTokenStore : ITokenStore
{
    private string? _token;
    public Task<string?> OkuAsync() => Task.FromResult(_token);
    public Task YazAsync(string token) { _token = token; return Task.CompletedTask; }
    public Task TemizleAsync() { _token = null; return Task.CompletedTask; }
}
```

- [ ] **Step 3: Token store testi yaz** — `Kasa.ApiClient.Tests/TokenStoreTests.cs`:
```csharp
namespace Kasa.ApiClient.Tests;

public class TokenStoreTests
{
    [Fact]
    public async Task Yaz_oku_temizle_dongusu()
    {
        var store = new BellekTokenStore();
        Assert.Null(await store.OkuAsync());

        await store.YazAsync("abc");
        Assert.Equal("abc", await store.OkuAsync());

        await store.TemizleAsync();
        Assert.Null(await store.OkuAsync());
    }
}
```

- [ ] **Step 4: Derle + test**
```bash
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj
```
Expected: PASS (2 test).

- [ ] **Step 5: Commit**
```bash
git add Kasa.ApiClient/Dtos.cs Kasa.ApiClient/ITokenStore.cs Kasa.ApiClient.Tests/TokenStoreTests.cs
git commit -m "feat(apiclient): DTO'lar + ITokenStore (bellek impl)"
```

---

## Task 3: KasaApiClient — auth + Bearer + 401 (TDD)

**Files:**
- Oluştur: `Kasa.ApiClient/KasaApiException.cs`, `Kasa.ApiClient/KasaApiClient.cs`
- Oluştur test: `Kasa.ApiClient.Tests/SahteHandler.cs`, `Kasa.ApiClient.Tests/AuthTests.cs`

- [ ] **Step 1: Sahte handler + başarısız auth testleri yaz**

`Kasa.ApiClient.Tests/SahteHandler.cs`:
```csharp
using System.Net;
using System.Text;

namespace Kasa.ApiClient.Tests;

/// <summary>Son isteği kaydeden ve sıradaki canned yanıtı döndüren test handler'ı.</summary>
public sealed class SahteHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _yanitlar = new();
    public HttpRequestMessage? SonIstek { get; private set; }
    public string? SonGovde { get; private set; }

    public SahteHandler Kuyrukla(HttpStatusCode kod, string? json = null)
    {
        var resp = new HttpResponseMessage(kod);
        if (json is not null) resp.Content = new StringContent(json, Encoding.UTF8, "application/json");
        _yanitlar.Enqueue(resp);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        SonIstek = request;
        SonGovde = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return _yanitlar.Count > 0 ? _yanitlar.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK);
    }
}
```

`Kasa.ApiClient.Tests/AuthTests.cs`:
```csharp
using System.Net;

namespace Kasa.ApiClient.Tests;

public class AuthTests
{
    private static (KasaApiClient client, SahteHandler handler, BellekTokenStore store) Kur()
    {
        var handler = new SahteHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://ornek.test/") };
        var store = new BellekTokenStore();
        return (new KasaApiClient(http, store), handler, store);
    }

    [Fact]
    public async Task Login_token_ve_rol_dondurur_ve_tokeni_saklar()
    {
        var (client, handler, store) = Kur();
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"editor","token":"jwt-123"}""");

        var yanit = await client.LoginAsync("admin", "sifre");

        Assert.Equal("editor", yanit.Rol);
        Assert.Equal("jwt-123", yanit.Token);
        Assert.Equal("jwt-123", await store.OkuAsync());
        Assert.Equal(HttpMethod.Post, handler.SonIstek!.Method);
        Assert.EndsWith("/api/auth/login", handler.SonIstek.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Sonraki_istek_bearer_basligi_ekler()
    {
        var (client, handler, store) = Kur();
        await store.YazAsync("jwt-xyz");
        handler.Kuyrukla(HttpStatusCode.OK, """{"rol":"viewer"}""");

        await client.BenKimAsync();

        Assert.Equal("Bearer", handler.SonIstek!.Headers.Authorization!.Scheme);
        Assert.Equal("jwt-xyz", handler.SonIstek.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Yetkisiz_401_KasaApiException_firlatir()
    {
        var (client, handler, _) = Kur();
        handler.Kuyrukla(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<KasaApiException>(() => client.PanelAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.DurumKodu);
    }

    [Fact]
    public async Task Login_401_KasaApiException_firlatir()
    {
        var (client, handler, _) = Kur();
        handler.Kuyrukla(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<KasaApiException>(() => client.LoginAsync("x", "y"));
    }
}
```

- [ ] **Step 2: Testleri çalıştır, FAIL doğrula** (`KasaApiClient`/`KasaApiException` yok → derlenmez = kırmızı).

- [ ] **Step 3: `KasaApiException` yaz** — `Kasa.ApiClient/KasaApiException.cs`:
```csharp
using System.Net;

namespace Kasa.ApiClient;

/// <summary>API başarısız durum kodu döndürdüğünde fırlatılır. 401 → app katmanı token silip Login'e döner.</summary>
public sealed class KasaApiException : Exception
{
    public HttpStatusCode DurumKodu { get; }
    public KasaApiException(HttpStatusCode kod, string? mesaj = null)
        : base(mesaj ?? $"API hatası: {(int)kod} {kod}") => DurumKodu = kod;
}
```

- [ ] **Step 4: `KasaApiClient` çekirdeğini yaz** — `Kasa.ApiClient/KasaApiClient.cs` (bu görevde auth + altyapı; okuma/mutasyon Task 4-5'te EKLENİR):
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kasa.ApiClient;

/// <summary>Kasa REST API'sinin tiplı istemcisi. Her isteğe Bearer token ekler; başarısız durumda KasaApiException.</summary>
public sealed partial class KasaApiClient
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public KasaApiClient(HttpClient http, ITokenStore store)
    {
        _http = http;
        _store = store;
    }

    public async Task<LoginYanit> LoginAsync(string? kullanici, string sifre)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { kullanici, sifre }, options: Json),
        };
        using var yanit = await GonderAsync(istek, tokenEkle: false);
        var login = (await yanit.Content.ReadFromJsonAsync<LoginYanit>(Json))!;
        await _store.YazAsync(login.Token);
        return login;
    }

    public async Task<string?> BenKimAsync()
    {
        var el = await GetAsync<RolYanit>("api/auth/me");
        return el.Rol;
    }

    public async Task CikisAsync()
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
        using var _ = await GonderAsync(istek);
        await _store.TemizleAsync();
    }

    private record RolYanit(string Rol);

    // ---- altyapı ----

    private async Task<HttpResponseMessage> GonderAsync(HttpRequestMessage istek, bool tokenEkle = true)
    {
        if (tokenEkle)
        {
            var token = await _store.OkuAsync();
            if (!string.IsNullOrEmpty(token))
                istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        var yanit = await _http.SendAsync(istek);
        if (!yanit.IsSuccessStatusCode)
        {
            var kod = yanit.StatusCode;
            yanit.Dispose();
            throw new KasaApiException(kod);
        }
        return yanit;
    }

    private async Task<T> GetAsync<T>(string yol)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, yol);
        using var yanit = await GonderAsync(istek);
        return (await yanit.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<T> GonderJsonAsync<T>(HttpMethod metot, string yol, object govde)
    {
        using var istek = new HttpRequestMessage(metot, yol) { Content = JsonContent.Create(govde, options: Json) };
        using var yanit = await GonderAsync(istek);
        return (await yanit.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task GonderJsonAsync(HttpMethod metot, string yol, object govde)
    {
        using var istek = new HttpRequestMessage(metot, yol) { Content = JsonContent.Create(govde, options: Json) };
        using var _ = await GonderAsync(istek);
    }

    private async Task SilAsync(string yol)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Delete, yol);
        using var _ = await GonderAsync(istek);
    }
}
```

> Not: `PanelAsync` bu görevde henüz yok; `Yetkisiz_401...` testi onu çağırıyor. Bu testi
> yeşile almak için Task 4'teki `PanelAsync` gerekli. Bu yüzden **Task 3 ve Task 4 aynı görev
> gövdesinde uygulanabilir**; ancak ayrı tutmak için: Task 3 Step 4'te geçici olarak minimal
> `public Task<PanelDto> PanelAsync() => GetAsync<PanelDto>("api/rapor/panel");` ekle (Task 4'te
> okuma bloğuna taşınır). Böylece Task 3 testleri kendi başına yeşil olur.

- [ ] **Step 5: Testleri çalıştır, PASS doğrula** — `dotnet test Kasa.ApiClient.Tests/...` (auth + token + duman).

- [ ] **Step 6: Commit**
```bash
git add Kasa.ApiClient/KasaApiException.cs Kasa.ApiClient/KasaApiClient.cs Kasa.ApiClient.Tests/SahteHandler.cs Kasa.ApiClient.Tests/AuthTests.cs
git commit -m "feat(apiclient): KasaApiClient auth + Bearer + 401 (KasaApiException)"
```

---

## Task 4: Okuma metotları + DTO eşleme (TDD)

`KasaApiClient` içinde tüm okuma uçlarını ekle; sahte handler ile DTO eşlemesini doğrula.

**Files:**
- Değiştir: `Kasa.ApiClient/KasaApiClient.cs` (okuma metotları)
- Oluştur test: `Kasa.ApiClient.Tests/OkumaTests.cs`

- [ ] **Step 1: Okuma testlerini yaz** — `Kasa.ApiClient.Tests/OkumaTests.cs`. En az şunları kapsa:
`PanelAsync` (guncelKasa + kanal bakiyeleri eşlenir), `HaftalikAsync` (iç içe donem + kanallar +
kasaDevir), `AylikAsync(yil,ay)` (query string + krediKarti alanı), `KrediKartlariAsync` (liste +
DateOnly alanları), `IslemlerAsync` (tip **string→enum** eşlenir; ör. `"KrediKarti"` → `GiderTipi.KrediKarti`),
`DonemlerAsync`, `CarilerAsync(ara)` (ara query'si iletiliyor), `AyarlarAsync`.

Örnek çekirdek (kalanları aynı desende ekle):
```csharp
using System.Net;

namespace Kasa.ApiClient.Tests;

public class OkumaTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task Panel_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            {"guncelKasa":90000.0,"kanallar":[{"kanal":"MEZAT","bakiye":150.5}],"buHaftaSonucu":10.0,"buAySonucu":-5.0}
        """);
        var p = await c.PanelAsync();
        Assert.Equal(90000.0m, p.GuncelKasa);
        Assert.Equal("MEZAT", p.Kanallar[0].Kanal);
        Assert.Equal(150.5m, p.Kanallar[0].Bakiye);
        Assert.Equal(-5.0m, p.BuAySonucu);
    }

    [Fact]
    public async Task Islemler_tip_stringi_enuma_eslenir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """
            [{"id":1,"tarih":"2026-03-05","cari":"K.K","tutarTl":10000.0,"kanal":"MEZAT","tip":"KrediKarti","not":null}]
        """);
        var liste = await c.IslemlerAsync();
        Assert.Equal(GiderTipi.KrediKarti, liste[0].Tip);
        Assert.Equal(new DateOnly(2026, 3, 5), liste[0].Tarih);
    }

    [Fact]
    public async Task Aylik_yil_ay_query_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.OK, """{"yil":2026,"ay":4,"kanallar":[]}""");
        await c.AylikAsync(2026, 4);
        var uri = h.SonIstek!.RequestUri!;
        Assert.Contains("yil=2026", uri.Query);
        Assert.Contains("ay=4", uri.Query);
    }
}
```

- [ ] **Step 2: Testleri çalıştır, FAIL doğrula** (metotlar yok).

- [ ] **Step 3: Okuma metotlarını ekle** — `KasaApiClient.cs`'e (Task 3'teki geçici `PanelAsync`'i
buraya taşı/temizle). `IslemlerAsync` isteğe bağlı filtreleri query string'e ekler (null olanları atla):

```csharp
    public Task<PanelDto> PanelAsync() => GetAsync<PanelDto>("api/rapor/panel");
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => GetAsync<IReadOnlyList<HaftalikOzetDto>>("api/rapor/haftalik");
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) => GetAsync<AylikRaporDto>($"api/rapor/aylik?yil={yil}&ay={ay}");
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => GetAsync<IReadOnlyList<DonemDto>>("api/donemler");
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => GetAsync<IReadOnlyList<KanalDto>>("api/kanallar");
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => GetAsync<IReadOnlyList<KrediKartiDto>>("api/kredikartlari");
    public Task<AyarlarDto> AyarlarAsync() => GetAsync<AyarlarDto>("api/ayarlar");

    public Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null)
        => GetAsync<IReadOnlyList<CariDto>>(ara is null ? "api/cariler" : $"api/cariler?ara={Uri.EscapeDataString(ara)}");

    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null)
        => GetAsync<IReadOnlyList<GelenDto>>(donemStart is { } d ? $"api/gelenler?donemStart={d:yyyy-MM-dd}" : "api/gelenler");

    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(
        DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null)
    {
        var q = new List<string>();
        if (baslangic is { } b) q.Add($"baslangic={b:yyyy-MM-dd}");
        if (bitis is { } s) q.Add($"bitis={s:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(kanal)) q.Add($"kanal={Uri.EscapeDataString(kanal)}");
        if (!string.IsNullOrWhiteSpace(cari)) q.Add($"cari={Uri.EscapeDataString(cari)}");
        var yol = q.Count > 0 ? $"api/islemler?{string.Join("&", q)}" : "api/islemler";
        return GetAsync<IReadOnlyList<IslemDto>>(yol);
    }
```

- [ ] **Step 4: Testleri çalıştır, PASS doğrula.**

- [ ] **Step 5: Commit**
```bash
git add Kasa.ApiClient/KasaApiClient.cs Kasa.ApiClient.Tests/OkumaTests.cs
git commit -m "feat(apiclient): okuma metotları + DTO eşleme (panel/haftalık/aylık/işlem/kart/...)"
```

---

## Task 5: Editör mutasyon metotları + serileştirme (TDD)

**Files:**
- Değiştir: `Kasa.ApiClient/KasaApiClient.cs` (mutasyon metotları)
- Oluştur test: `Kasa.ApiClient.Tests/MutasyonTests.cs`

- [ ] **Step 1: Mutasyon testlerini yaz** — gönderilen HTTP metodu, yol ve **gövde JSON'unu**
`SonGovde` üzerinden doğrula. En az: kredi kartı oluştur (POST /api/kredikartlari, gövde ad/limit/borc +
DateOnly'ler), işlem oluştur (tip **string** olarak gidiyor: `"KrediKarti"` — sayı DEĞİL), kanal güncelle
(PUT /api/kanallar/{id}), cari sil (DELETE /api/cariler/{id}), gelen upsert (PUT /api/gelenler),
izleyici şifre (PUT /api/ayarlar/izleyici-sifre gövde `{ yeniSifre }`).

```csharp
using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

public class MutasyonTests
{
    private static (KasaApiClient c, SahteHandler h) Kur()
    {
        var h = new SahteHandler();
        var http = new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") };
        return (new KasaApiClient(http, new BellekTokenStore()), h);
    }

    [Fact]
    public async Task KrediKarti_olustur_dogru_govde_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":7,"ad":"Bonus","kesimTarihi":"2026-07-05","sonOdemeTarihi":"2026-07-25","limit":100000.0,"borc":30000.0}""");

        var eklenen = await c.KrediKartiOlusturAsync(new KrediKartiYaz("Bonus", new DateOnly(2026,7,5), new DateOnly(2026,7,25), 100000m, 30000m));

        Assert.Equal(HttpMethod.Post, h.SonIstek!.Method);
        Assert.EndsWith("/api/kredikartlari", h.SonIstek.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("Bonus", doc.RootElement.GetProperty("ad").GetString());
        Assert.Equal(100000m, doc.RootElement.GetProperty("limit").GetDecimal());
        Assert.Equal("2026-07-05", doc.RootElement.GetProperty("kesimTarihi").GetString());
        Assert.Equal(7, eklenen.Id);
    }

    [Fact]
    public async Task Islem_olustur_tip_string_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.Created, """{"id":1,"tarih":"2026-03-05","cari":"K.K","tutarTl":10000.0,"kanal":"MEZAT","tip":"KrediKarti","not":null}""");

        await c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026,3,5), "K.K", 10000m, "MEZAT", GiderTipi.KrediKarti, null));

        using var doc = JsonDocument.Parse(h.SonGovde!);
        Assert.Equal("KrediKarti", doc.RootElement.GetProperty("tip").GetString());  // sayı değil, string
    }

    [Fact]
    public async Task Cari_sil_delete_gonderir()
    {
        var (c, h) = Kur();
        h.Kuyrukla(HttpStatusCode.NoContent);
        await c.CariSilAsync(3);
        Assert.Equal(HttpMethod.Delete, h.SonIstek!.Method);
        Assert.EndsWith("/api/cariler/3", h.SonIstek.RequestUri!.AbsolutePath);
    }
}
```

- [ ] **Step 2: Testleri çalıştır, FAIL doğrula.**

- [ ] **Step 3: Mutasyon metotlarını ekle** — `KasaApiClient.cs`:
```csharp
    // Kanal
    public Task<KanalDto> KanalOlusturAsync(KanalYaz g) => GonderJsonAsync<KanalDto>(HttpMethod.Post, "api/kanallar", g);
    public Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g) => GonderJsonAsync<KanalDto>(HttpMethod.Put, $"api/kanallar/{id}", g);
    public Task KanalSilAsync(int id) => SilAsync($"api/kanallar/{id}");

    // Cari
    public Task<CariDto> CariOlusturAsync(CariYaz g) => GonderJsonAsync<CariDto>(HttpMethod.Post, "api/cariler", g);
    public Task<CariDto> CariGuncelleAsync(int id, CariYaz g) => GonderJsonAsync<CariDto>(HttpMethod.Put, $"api/cariler/{id}", g);
    public Task CariSilAsync(int id) => SilAsync($"api/cariler/{id}");

    // İşlem
    public Task<IslemDto> IslemOlusturAsync(IslemYaz g) => GonderJsonAsync<IslemDto>(HttpMethod.Post, "api/islemler", g);
    public Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g) => GonderJsonAsync<IslemDto>(HttpMethod.Put, $"api/islemler/{id}", g);
    public Task IslemSilAsync(int id) => SilAsync($"api/islemler/{id}");

    // Kredi kartı
    public Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g) => GonderJsonAsync<KrediKartiDto>(HttpMethod.Post, "api/kredikartlari", g);
    public Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g) => GonderJsonAsync<KrediKartiDto>(HttpMethod.Put, $"api/kredikartlari/{id}", g);
    public Task KrediKartiSilAsync(int id) => SilAsync($"api/kredikartlari/{id}");

    // Gelen upsert
    public Task<GelenDto> GelenKaydetAsync(GelenYaz g) => GonderJsonAsync<GelenDto>(HttpMethod.Put, "api/gelenler", g);

    // Ayarlar
    public Task AyarGuncelleAsync(AyarYaz g) => GonderJsonAsync(HttpMethod.Put, "api/ayarlar", g);
    public Task IzleyiciSifreAsync(string yeniSifre) => GonderJsonAsync(HttpMethod.Put, "api/ayarlar/izleyici-sifre", new { yeniSifre });
```

- [ ] **Step 4: Testleri çalıştır, PASS doğrula.**

- [ ] **Step 5: Commit**
```bash
git add Kasa.ApiClient/KasaApiClient.cs Kasa.ApiClient.Tests/MutasyonTests.cs
git commit -m "feat(apiclient): editör mutasyon metotları + serileştirme testleri"
```

---

## Task 6: Tam doğrulama

- [ ] **Step 1: Tüm çözümü derle + tüm testleri çalıştır**
```bash
cd "C:\Users\burak\source\repos\Kasa"
dotnet build
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj
```
Expected: hepsi PASS. ApiClient testleri (duman 1 + token 1 + auth 4 + okuma ≥8 + mutasyon ≥6),
Core 18, Api 22 bozulmadan.

- [ ] **Step 2: (Doğrulama, commit yok)** `Kasa.ApiClient` `Kasa.Core`/EF'e referans VERMEMELİ —
`Kasa.ApiClient.csproj`'da ProjectReference olmadığını teyit et (bağımsızlık = spec §3).

---

## Notlar

- **Bağımsızlık:** İstemci `Kasa.Core`'a bağımlı değil; `GiderTipi` istemcide ayrı tanımlı. Sunucu
  string enum döndürdüğü için değerler eşleşir. Yeni bir gider tipi eklenirse iki yerde güncellenir
  (kabul edilen küçük ikilik — tam bağımsızlık uğruna).
- **401 politikası:** İstemci 401'i `KasaApiException(DurumKodu=Unauthorized)` ile yüzeye çıkarır;
  token silme + Login'e yönlendirme **Plan 3** (app katmanı) işi (spec §5). İstemci burada token
  SİLMEZ — politika app'te merkezi olsun.
- **SecureStorage:** Gerçek güvenli token deposu Plan 3'te MAUI head'de `ITokenStore` implementasyonu
  olarak gelir; bu planda yalnız `BellekTokenStore` var.
