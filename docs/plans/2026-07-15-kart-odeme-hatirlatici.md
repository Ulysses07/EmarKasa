# Kart Ödeme Hatırlatıcı — Uygulama Planı

> **Ajan işçiler için:** GEREKLİ ALT-BECERİ: Bu planı task-task uygulamak için
> superpowers:subagent-driven-development (önerilen) veya superpowers:executing-plans
> kullanın. Adımlar takip için checkbox (`- [ ]`) sözdizimi kullanır.

**Hedef:** Kredi kartı kesim/son ödeme tarihleri için Windows toast bildirimi (uygulama
kapalıyken de) + uygulama-içi "ödedin mi?" onay akışı; borcu olmayan/ödenmiş kartlar için
bildirim çıkmasın.

**Mimari:** Sunucu `/api/kredikartlari`'ya ekstre borcu (`EkstreBorc`) ekler. `Kasa.App.Core`'da
saf `KartHatirlatici` mantığı (yinelenme + 2'li kapı). `Kasa.App` (MAUI Windows) `AppNotificationManager`
ile toast gösterir; kullanıcı-düzeyi Windows Zamanlanmış Görev `.exe --hatirlatma-kontrol`'ü
günlük çalıştırır. HesapMotoru DEĞİŞMEZ.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, EF Core Sqlite, .NET MAUI (net10.0-windows),
Windows App SDK (`Microsoft.Windows.AppNotifications`), CommunityToolkit.Mvvm, xUnit.

**Referans spec:** `docs/specs/2026-07-15-kart-odeme-hatirlatici-design.md`

**Commit kuralı:** author `Musa Sevinç <musa@royalmezat.com>`, Co-Authored-By YOK. Her commit:
`git -c user.email=musa@royalmezat.com commit -m "..."`.

---

## Dosya yapısı

- **Değiştir:** `Kasa.Api/Dtos.cs` — `KrediKartiTuretilmisDto`'ya `EkstreBorc`
- **Değiştir:** `Kasa.Api/Program.cs:179-197` — kredikartlari GET ekstre borcu hesabı
- **Oluştur:** `Kasa.Core/KartDonem.cs` — sunucu kesim yinelenme yardımcısı
- **Oluştur:** `Kasa.Core.Tests/KartDonemTests.cs`
- **Değiştir:** `Kasa.Api.Tests` — EkstreBorc entegrasyon testi (yeni dosya)
- **Değiştir:** `Kasa.ApiClient/Dtos.cs:11` — `KrediKartiDto`'ya `EkstreBorc`
- **Değiştir:** `Kasa.ApiClient.Tests` — DTO round-trip testi
- **Oluştur:** `Kasa.App.Core/KartTarih.cs` — istemci yinelenme yardımcısı
- **Oluştur:** `Kasa.App.Core/KartHatirlatici.cs` — Hatirlatma + HatirlatmaTuru + mantık
- **Oluştur:** `Kasa.App.Core/IBildirimServisi.cs` — seam
- **Değiştir:** `Kasa.App.Core/KrediKartiGorunum.cs` — `EkstreBorc` + banner işaretleri
- **Değiştir:** `Kasa.App.Core/KrediKartlariViewModel.cs` — DoldurAsync sonrası hatırlatıcı
- **Oluştur:** `Kasa.App.Core.Tests/KartTarihTests.cs`, `KartHatirlaticiTests.cs`
- **Değiştir:** `Kasa.App.Core.Tests/KrediKartlariViewModelTests.cs`
- **Değiştir:** `Kasa.App/Views/KrediKartlariPage.xaml` — "Ödedin mi?" şeridi
- **Oluştur:** `Kasa.App/Platforms/Windows/WindowsBildirimServisi.cs`
- **Oluştur:** `Kasa.App/Platforms/Windows/HatirlatmaKontrol.cs` — headless mod + görev kaydı
- **Değiştir:** `Kasa.App/Platforms/Windows/App.xaml.cs` — arg tespiti
- **Değiştir:** `Kasa.App/MauiProgram.cs` — DI + KayitOl

---

## Task 1: Sunucu kesim yinelenme yardımcısı (`KartDonem`)

**Files:**
- Create: `Kasa.Core/KartDonem.cs`
- Test: `Kasa.Core.Tests/KartDonemTests.cs`

- [ ] **Step 1: Başarısız testi yaz**

`Kasa.Core.Tests/KartDonemTests.cs`:
```csharp
using Kasa.Core;

namespace Kasa.Core.Tests;

public class KartDonemTests
{
    [Fact]
    public void SonKesim_ay_icinde_gecmis_gunu_dondurur()
        => Assert.Equal(new DateOnly(2026, 7, 15),
            KartDonem.SonKesim(15, new DateOnly(2026, 7, 20)));

    [Fact]
    public void SonKesim_gun_gelmediyse_onceki_aya_gider()
        => Assert.Equal(new DateOnly(2026, 6, 25),
            KartDonem.SonKesim(25, new DateOnly(2026, 7, 10)));

    [Fact]
    public void SonKesim_kisa_ayda_ay_sonuna_kirpar()
        => Assert.Equal(new DateOnly(2026, 2, 28),
            KartDonem.SonKesim(31, new DateOnly(2026, 3, 1)));

    [Fact]
    public void SonKesim_ocakta_onceki_yila_sarar()
        => Assert.Equal(new DateOnly(2025, 12, 20),
            KartDonem.SonKesim(20, new DateOnly(2026, 1, 5)));
}
```

- [ ] **Step 2: Testi çalıştır, başarısız olduğunu gör**

Run: `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj --filter KartDonem`
Expected: FAIL (KartDonem yok — derleme hatası).

- [ ] **Step 3: Yardımcıyı yaz**

`Kasa.Core/KartDonem.cs`:
```csharp
namespace Kasa.Core;

/// <summary>Kredi kartı tarihlerini ayın günü olarak yorumlar (aylık yinelenme).</summary>
public static class KartDonem
{
    /// <summary><paramref name="gun"/> günlü, <paramref name="bugun"/>'e küçük/eşit en son tarih.
    /// Ay kısa ise ay sonuna kırpar.</summary>
    public static DateOnly SonKesim(int gun, DateOnly bugun)
    {
        int y = bugun.Year, m = bugun.Month;
        var t = GunClamp(y, m, gun);
        if (t <= bugun) return t;
        m--; if (m < 1) { m = 12; y--; }
        return GunClamp(y, m, gun);
    }

    private static DateOnly GunClamp(int yil, int ay, int gun)
        => new(yil, ay, Math.Min(gun, DateTime.DaysInMonth(yil, ay)));
}
```

- [ ] **Step 4: Testi çalıştır, geçtiğini gör**

Run: `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj --filter KartDonem`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add Kasa.Core/KartDonem.cs Kasa.Core.Tests/KartDonemTests.cs
git -c user.email=musa@royalmezat.com commit -m "feat(core): kart kesim yinelenme yardımcısı"
```

---

## Task 2: Sunucu `EkstreBorc` türetmesi + DTO

**Files:**
- Modify: `Kasa.Api/Dtos.cs:14-16`
- Modify: `Kasa.Api/Program.cs:179-197`
- Test: `Kasa.Api.Tests/EkstreBorcTests.cs` (yeni)

- [ ] **Step 1: Başarısız entegrasyon testini yaz**

`Kasa.Api.Tests/EkstreBorcTests.cs` (mevcut `KasaWebFactory` + `EditorClientAsync` desenini kullanır;
diğer test dosyalarındaki JSON options ve login yardımcılarına bak):
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api;

namespace Kasa.Api.Tests;

public class EkstreBorcTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Kesimden_sonraki_harcama_ekstre_borcuna_girmez()
    {
        await using var f = new KasaWebFactory();
        var c = await f.EditorClientAsync();
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        var kesim = bugun.AddDays(-5);      // en son kesim 5 gün önce
        var kart = await (await c.PostAsJsonAsync("/api/kredikartlari",
            new { ad = "Test", kesimTarihi = kesim, sonOdemeTarihi = bugun.AddDays(5),
                  limit = 100000m, borc = 1000m })).Content.ReadFromJsonAsync<JsonElement>(Json);
        int id = kart.GetProperty("id").GetInt32();

        // kesimden ÖNCE harcama (ekstreye girer)
        await c.PostAsJsonAsync("/api/islemler", new { tarih = kesim.AddDays(-1), cari = "A",
            tutarTl = 500m, kanal = "MEZAT", tip = "KrediKarti", krediKartiId = id });
        // kesimden SONRA harcama (ekstreye GİRMEZ)
        await c.PostAsJsonAsync("/api/islemler", new { tarih = bugun, cari = "B",
            tutarTl = 300m, kanal = "MEZAT", tip = "KrediKarti", krediKartiId = id });

        var liste = await c.GetFromJsonAsync<List<JsonElement>>("/api/kredikartlari", Json);
        var k = liste!.Single(x => x.GetProperty("id").GetInt32() == id);

        Assert.Equal(1800m, k.GetProperty("guncelBorc").GetDecimal());   // 1000+500+300
        Assert.Equal(1500m, k.GetProperty("ekstreBorc").GetDecimal());   // 1000+500 (kesim sonrası hariç)
    }
}
```

- [ ] **Step 2: Testi çalıştır, başarısız gör**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter EkstreBorc`
Expected: FAIL (ekstreBorc yok).

- [ ] **Step 3: DTO'ya alan ekle**

`Kasa.Api/Dtos.cs` — `KrediKartiTuretilmisDto` sonuna `decimal EkstreBorc` ekle:
```csharp
public record KrediKartiTuretilmisDto(
    int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit,
    decimal Borc, decimal GuncelBorc, decimal AcilisBorc, decimal HarcamaToplam, decimal OdemeToplam,
    decimal EkstreBorc);
```

- [ ] **Step 4: Türetmeyi güncelle**

`Kasa.Api/Program.cs` — `using Kasa.Core;` dosyada mevcut (islemler POST'u `GiderTipi` kullanıyor).
`api.MapGet("/kredikartlari", ...)` gövdesini şununla değiştir:
```csharp
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
```

- [ ] **Step 5: Testi çalıştır, geçtiğini gör**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter EkstreBorc`
Expected: PASS.

- [ ] **Step 6: Tüm sunucu testleri regresyon**

Run: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj`
Expected: hepsi PASS.

- [ ] **Step 7: Commit**

```bash
git add Kasa.Api/Dtos.cs Kasa.Api/Program.cs Kasa.Api.Tests/EkstreBorcTests.cs
git -c user.email=musa@royalmezat.com commit -m "feat(api): kredi kartı ekstre borcu (kesim sonrası harcama hariç)"
```

---

## Task 3: ApiClient `KrediKartiDto.EkstreBorc`

**Files:**
- Modify: `Kasa.ApiClient/Dtos.cs:11`
- Test: `Kasa.ApiClient.Tests/MutasyonTests.cs` (veya mevcut kart okuma test dosyası)

- [ ] **Step 1: Round-trip testi yaz**

Mevcut ApiClient test desenine (`SahteHandler.Kuyrukla`) uygun bir test ekle; sunucudan gelen
`ekstreBorc`'un DTO'ya çözüldüğünü doğrula:
```csharp
[Fact]
public async Task KrediKartlari_ekstreBorc_alanini_cozer()
{
    var handler = new SahteHandler();
    handler.Kuyrukla(System.Net.HttpStatusCode.OK,
        """[{"id":1,"ad":"A","kesimTarihi":"2026-07-15","sonOdemeTarihi":"2026-07-22",
             "limit":100000,"borc":1000,"guncelBorc":1800,"acilisBorc":1000,
             "harcamaToplam":800,"odemeToplam":0,"ekstreBorc":1500}]""");
    var api = YeniApi(handler);   // mevcut test yardımcısı

    var liste = await api.KrediKartlariAsync();

    Assert.Equal(1500m, liste.Single().EkstreBorc);
}
```
> Not: `YeniApi`/`SahteHandler` yardımcılarının tam adları için `Kasa.ApiClient.Tests` içindeki
> mevcut testlere bak; birebir aynı deseni kullan.

- [ ] **Step 2: Testi çalıştır, başarısız gör**

Run: `dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj --filter ekstreBorc`
Expected: FAIL (EkstreBorc yok — derleme hatası).

- [ ] **Step 3: DTO'ya alan ekle**

`Kasa.ApiClient/Dtos.cs:11` — `KrediKartiDto` sonuna trailing opsiyonel alan:
```csharp
public record KrediKartiDto(int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc, decimal GuncelBorc = 0m, decimal AcilisBorc = 0m, decimal HarcamaToplam = 0m, decimal OdemeToplam = 0m, decimal EkstreBorc = 0m);
```

- [ ] **Step 4: Testi çalıştır, geçtiğini gör**

Run: `dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj --filter ekstreBorc`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Kasa.ApiClient/Dtos.cs Kasa.ApiClient.Tests/
git -c user.email=musa@royalmezat.com commit -m "feat(apiclient): KrediKartiDto ekstre borcu alanı"
```

---

## Task 4: İstemci yinelenme yardımcısı (`KartTarih`)

**Files:**
- Create: `Kasa.App.Core/KartTarih.cs`
- Test: `Kasa.App.Core.Tests/KartTarihTests.cs`

- [ ] **Step 1: Başarısız testi yaz**

`Kasa.App.Core.Tests/KartTarihTests.cs`:
```csharp
using Kasa.App.Core;

namespace Kasa.App.Core.Tests;

public class KartTarihTests
{
    [Fact]
    public void OncekiGun_ay_icinde() =>
        Assert.Equal(new DateOnly(2026,7,15), KartTarih.OncekiGun(15, new DateOnly(2026,7,20)));

    [Fact]
    public void OncekiGun_gecmemisse_onceki_ay() =>
        Assert.Equal(new DateOnly(2026,6,25), KartTarih.OncekiGun(25, new DateOnly(2026,7,10)));

    [Fact]
    public void SonrakiGun_ay_icinde() =>
        Assert.Equal(new DateOnly(2026,7,22), KartTarih.SonrakiGun(22, new DateOnly(2026,7,15)));

    [Fact]
    public void SonrakiGun_gecmisse_sonraki_ay() =>
        Assert.Equal(new DateOnly(2026,8,5), KartTarih.SonrakiGun(5, new DateOnly(2026,7,15)));

    [Fact]
    public void SonrakiGun_kisa_ayda_kirpar() =>
        Assert.Equal(new DateOnly(2026,2,28), KartTarih.SonrakiGun(31, new DateOnly(2026,2,1)));
}
```

- [ ] **Step 2: Başarısız gör**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter KartTarih`
Expected: FAIL.

- [ ] **Step 3: Yardımcıyı yaz**

`Kasa.App.Core/KartTarih.cs`:
```csharp
namespace Kasa.App.Core;

/// <summary>Kart tarihlerini ayın günü olarak yorumlar (aylık yinelenme).</summary>
public static class KartTarih
{
    /// <summary><paramref name="gun"/> günlü, <paramref name="referans"/>'a küçük/eşit en son tarih.</summary>
    public static DateOnly OncekiGun(int gun, DateOnly referans)
    {
        int y = referans.Year, m = referans.Month;
        var t = Clamp(y, m, gun);
        if (t <= referans) return t;
        m--; if (m < 1) { m = 12; y--; }
        return Clamp(y, m, gun);
    }

    /// <summary><paramref name="gun"/> günlü, <paramref name="referans"/>'a büyük/eşit ilk tarih.</summary>
    public static DateOnly SonrakiGun(int gun, DateOnly referans)
    {
        int y = referans.Year, m = referans.Month;
        var t = Clamp(y, m, gun);
        if (t >= referans) return t;
        m++; if (m > 12) { m = 1; y++; }
        return Clamp(y, m, gun);
    }

    private static DateOnly Clamp(int yil, int ay, int gun)
        => new(yil, ay, Math.Min(gun, DateTime.DaysInMonth(yil, ay)));
}
```

- [ ] **Step 4: Geçtiğini gör**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter KartTarih`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**

```bash
git add Kasa.App.Core/KartTarih.cs Kasa.App.Core.Tests/KartTarihTests.cs
git -c user.email=musa@royalmezat.com commit -m "feat(app): kart tarih yinelenme yardımcısı"
```

---

## Task 5: `KrediKartiGorunum` — EkstreBorc + banner işareti

**Files:**
- Modify: `Kasa.App.Core/KrediKartiGorunum.cs`

- [ ] **Step 1: Başarısız testi yaz**

`Kasa.App.Core.Tests/KrediKartOdemeTests.cs`'e ekle:
```csharp
[Fact]
public void Gorunum_ekstre_borcunu_dtodan_alir()
{
    var g = new KrediKartiGorunum(new KrediKartiDto(1, "A", new DateOnly(2026,7,15),
        new DateOnly(2026,7,22), 100000m, 1000m, GuncelBorc: 1800m, AcilisBorc: 1000m,
        HarcamaToplam: 800m, OdemeToplam: 0m, EkstreBorc: 1500m));
    Assert.Equal(1500m, g.EkstreBorc);
}
```

- [ ] **Step 2: Başarısız gör**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter Gorunum_ekstre`
Expected: FAIL.

- [ ] **Step 3: Gorunum'a alanları ekle**

`Kasa.App.Core/KrediKartiGorunum.cs` — `GuncelBorc` yanına `EkstreBorc` property'si ve ctor ataması;
ayrıca banner için `[ObservableProperty] private bool _odemeBekliyor;` (sınıf zaten
`partial ObservableObject`). Property ekle:
```csharp
    public decimal EkstreBorc { get; }
```
Ctor içine:
```csharp
        EkstreBorc = d.EkstreBorc;
```
`_odemeTutarGiris` alanının yanına ekle:
```csharp
    /// <summary>Uygulama-içi "Ödedin mi?" şeridi görünürlüğü (VM doldurur).</summary>
    [ObservableProperty] private bool _odemeBekliyor;
```

- [ ] **Step 4: Geçtiğini gör**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter Gorunum_ekstre`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Kasa.App.Core/KrediKartiGorunum.cs Kasa.App.Core.Tests/KrediKartOdemeTests.cs
git -c user.email=musa@royalmezat.com commit -m "feat(app): görünüm modeli ekstre borcu + ödeme bekliyor işareti"
```

---

## Task 6: `KartHatirlatici` çekirdek mantık

**Files:**
- Create: `Kasa.App.Core/KartHatirlatici.cs`
- Test: `Kasa.App.Core.Tests/KartHatirlaticiTests.cs`

- [ ] **Step 1: Başarısız testleri yaz**

`Kasa.App.Core.Tests/KartHatirlaticiTests.cs`:
```csharp
using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Core.Tests;

public class KartHatirlaticiTests
{
    // kesim 15, son ödeme 22
    private static KrediKartiGorunum Kart(decimal ekstre, params (DateOnly tarih, decimal tutar)[] odemeler)
    {
        var g = new KrediKartiGorunum(new KrediKartiDto(1, "A", new DateOnly(2026,7,15),
            new DateOnly(2026,7,22), 100000m, 1000m, EkstreBorc: ekstre));
        foreach (var o in odemeler) g.Odemeler.Add(new KartOdemeDto(0, 1, o.tarih, o.tutar, null));
        return g;
    }

    [Fact]
    public void Borc_yoksa_hicbir_hatirlatma_yok()
    {
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(0m) }, new DateOnly(2026,7,22));
        Assert.Empty(h);
    }

    [Fact]
    public void Kesim_gununde_ekstre_bildirimi()
    {
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(1500m) }, new DateOnly(2026,7,15));
        Assert.Equal(HatirlatmaTuru.Kesim, h.Single().Tur);
    }

    [Fact]
    public void Son_odemeye_3_gun_kala_bildirim()
    {
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(1500m) }, new DateOnly(2026,7,19));
        Assert.Equal(HatirlatmaTuru.SonOdeme3Gun, h.Single().Tur);
    }

    [Fact]
    public void Son_odeme_gununde_bildirim()
    {
        var h = KartHatirlatici.VadesiGelenler(new[] { Kart(1500m) }, new DateOnly(2026,7,22));
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, h.Single().Tur);
    }

    [Fact]
    public void Bu_donem_odenmisse_son_odeme_bildirimi_yok()
    {
        // kesim 15'ten sonra ödeme var → susar (ama borç>0)
        var h = KartHatirlatici.VadesiGelenler(
            new[] { Kart(1500m, (new DateOnly(2026,7,16), 1500m)) }, new DateOnly(2026,7,22));
        Assert.Empty(h);
    }

    [Fact]
    public void Kesim_oncesi_odeme_bu_donemi_susturmaz()
    {
        // ödeme kesimden ÖNCE (14'ü) → bu dönem ödemesi sayılmaz
        var h = KartHatirlatici.VadesiGelenler(
            new[] { Kart(1500m, (new DateOnly(2026,7,14), 1500m)) }, new DateOnly(2026,7,22));
        Assert.Equal(HatirlatmaTuru.SonOdemeGunu, h.Single().Tur);
    }
}
```

- [ ] **Step 2: Başarısız gör**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter KartHatirlatici`
Expected: FAIL (KartHatirlatici yok).

- [ ] **Step 3: Mantığı yaz**

`Kasa.App.Core/KartHatirlatici.cs`:
```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core;

public enum HatirlatmaTuru { Kesim, SonOdeme3Gun, SonOdemeGunu }

public record Hatirlatma(int KartId, string KartAd, HatirlatmaTuru Tur, decimal EkstreBorc, DateOnly Tarih);

/// <summary>Kart kesim/son ödeme hatırlatmalarını üretir (yinelenme + borç/ödeme kapısı).</summary>
public static class KartHatirlatici
{
    public static IReadOnlyList<Hatirlatma> VadesiGelenler(
        IEnumerable<KrediKartiGorunum> kartlar, DateOnly bugun)
    {
        var sonuc = new List<Hatirlatma>();
        foreach (var k in kartlar)
        {
            if (k.EkstreBorc <= 0) continue;                      // borç yok → bildirim yok
            var sonKesim = KartTarih.OncekiGun(k.KesimTarihi.Day, bugun);

            if (bugun == sonKesim)                                // kesim günü (bilgi)
                sonuc.Add(new(k.Id, k.Ad, HatirlatmaTuru.Kesim, k.EkstreBorc, bugun));

            bool oDonemOdenmis = k.Odemeler.Any(o => o.Tarih >= sonKesim);
            if (oDonemOdenmis) continue;                          // ele alındı → ödeme hatırlatması yok

            var sonOdeme = KartTarih.SonrakiGun(k.SonOdemeTarihi.Day, sonKesim);
            if (bugun == sonOdeme.AddDays(-3))
                sonuc.Add(new(k.Id, k.Ad, HatirlatmaTuru.SonOdeme3Gun, k.EkstreBorc, bugun));
            else if (bugun == sonOdeme)
                sonuc.Add(new(k.Id, k.Ad, HatirlatmaTuru.SonOdemeGunu, k.EkstreBorc, bugun));
        }
        return sonuc;
    }

    /// <summary>Uygulama-içi şerit: son ödemeye 3 gün kala → bu dönem ödenene/yeni kesime kadar.</summary>
    public static bool OdemeBekliyor(KrediKartiGorunum k, DateOnly bugun)
    {
        if (k.EkstreBorc <= 0) return false;
        var sonKesim = KartTarih.OncekiGun(k.KesimTarihi.Day, bugun);
        if (k.Odemeler.Any(o => o.Tarih >= sonKesim)) return false;
        var sonOdeme = KartTarih.SonrakiGun(k.SonOdemeTarihi.Day, sonKesim);
        return bugun >= sonOdeme.AddDays(-3);
    }
}
```

- [ ] **Step 4: Geçtiğini gör**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter KartHatirlatici`
Expected: PASS (6/6).

- [ ] **Step 5: Commit**

```bash
git add Kasa.App.Core/KartHatirlatici.cs Kasa.App.Core.Tests/KartHatirlaticiTests.cs
git -c user.email=musa@royalmezat.com commit -m "feat(app): kart hatırlatma çekirdek mantığı"
```

---

## Task 7: `IBildirimServisi` seam

**Files:**
- Create: `Kasa.App.Core/IBildirimServisi.cs`

- [ ] **Step 1: Seam'i yaz** (arayüz — test yok, uygulama Task 9'da)

`Kasa.App.Core/IBildirimServisi.cs`:
```csharp
namespace Kasa.App.Core;

/// <summary>Platform bildirim servisi (Windows toast). Kayıt + gösterme.</summary>
public interface IBildirimServisi
{
    /// <summary>Uygulama açılışında bir kez: bildirim altyapısını kaydeder.</summary>
    void KayitOl();

    /// <summary>Verilen hatırlatmalar için bildirim gösterir.</summary>
    Task GosterAsync(IReadOnlyList<Hatirlatma> hatirlatmalar);
}
```

- [ ] **Step 2: Derlemeyi doğrula**

Run: `dotnet build Kasa.App.Core/Kasa.App.Core.csproj`
Expected: 0 hata.

- [ ] **Step 3: Commit**

```bash
git add Kasa.App.Core/IBildirimServisi.cs
git -c user.email=musa@royalmezat.com commit -m "feat(app): bildirim servisi seam"
```

---

## Task 8: VM — DoldurAsync sonrası hatırlatıcı + banner

**Files:**
- Modify: `Kasa.App.Core/KrediKartlariViewModel.cs`
- Test: `Kasa.App.Core.Tests/KrediKartlariViewModelTests.cs`

- [ ] **Step 1: Başarısız testi yaz**

`Kasa.App.Core.Tests/KrediKartlariViewModelTests.cs`'e ekle (kesim/son ödeme bugüne göre kur):
```csharp
[Fact]
public async Task Yukle_son_odeme_yaklasan_karti_odeme_bekliyor_isaretler()
{
    var bugun = DateOnly.FromDateTime(DateTime.Today);
    var api = new SahteApi
    {
        KrediKartlariListe = new List<KrediKartiDto>
        {
            // kesim dün, son ödeme bugün, ekstre borcu var, ödeme yok
            new(1, "A", bugun.AddDays(-1), bugun, 100000m, 1000m,
                GuncelBorc: 1000m, AcilisBorc: 1000m, EkstreBorc: 1000m),
        },
    };
    var vm = new KrediKartlariViewModel(api);
    await vm.YukleAsync();
    Assert.True(vm.Kartlar.Single().OdemeBekliyor);
}
```

- [ ] **Step 2: Başarısız gör**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter odeme_bekliyor_isaretler`
Expected: FAIL (OdemeBekliyor işaretlenmiyor).

- [ ] **Step 3: DoldurAsync'i güncelle**

`Kasa.App.Core/KrediKartlariViewModel.cs` — `DoldurAsync` sonunda (kartlar eklendikten sonra)
banner işaretle:
```csharp
    private async Task DoldurAsync()
    {
        var liste = await _api.KrediKartlariAsync();
        Kartlar.Clear();
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        foreach (var k in liste)
        {
            var g = new KrediKartiGorunum(k);
            var odemeler = await _api.KartOdemelerAsync(k.Id);
            foreach (var o in odemeler) g.Odemeler.Add(o);
            g.OdemeBekliyor = KartHatirlatici.OdemeBekliyor(g, bugun);
            Kartlar.Add(g);
        }
    }
```

- [ ] **Step 4: Geçtiğini gör + App.Core regresyon**

Run: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj`
Expected: hepsi PASS.

- [ ] **Step 5: Commit**

```bash
git add Kasa.App.Core/KrediKartlariViewModel.cs Kasa.App.Core.Tests/KrediKartlariViewModelTests.cs
git -c user.email=musa@royalmezat.com commit -m "feat(app): kart yüklemede ödeme bekliyor şeridini işaretle"
```

---

## Task 9: Uygulama-içi "Ödedin mi?" şeridi (XAML)

**Files:**
- Modify: `Kasa.App/Views/KrediKartlariPage.xaml`

Bu görsel bir değişiklik; birim-test yok. Adımlar mekanik.

- [ ] **Step 1: Şeridi ekle**

`Kasa.App/Views/KrediKartlariPage.xaml` — "Ödeme ekle" `Label`'ının (`Text="Ödeme ekle"`) HEMEN
ÜSTÜNE, `OdemeBekliyor`'a bağlı vurgulu bir şerit ekle:
```xml
                                    <!-- Ödeme hatırlatma şeridi -->
                                    <Border IsVisible="{Binding OdemeBekliyor}"
                                            Background="{StaticResource BrushGreen}"
                                            StrokeThickness="0" StrokeShape="RoundRectangle 8"
                                            Padding="10,7" Margin="0,12,0,0">
                                        <Label Text="Son ödeme yaklaştı — ödemeni girerek işaretle"
                                               TextColor="White" FontSize="12"
                                               FontFamily="PlexSansSemiBold" />
                                    </Border>
```
> Not: "Evet, ödedim" için ayrı buton gerekmez — şerit kullanıcıyı hemen altındaki mevcut
> "Ödeme ekle" formuna yönlendirir; ödeme kaydedilince (`OdemeEkleCommand`) `DoldurAsync`
> yeniden çalışır, `OdemeBekliyor` false olur ve şerit kaybolur.

- [ ] **Step 2: MAUI Windows build**

Run: `taskkill //F //IM Kasa.App.exe 2>/dev/null; dotnet build Kasa.App/Kasa.App.csproj -f net10.0-windows10.0.19041.0`
Expected: 0 hata.

- [ ] **Step 3: Manuel doğrula**

Uygulamayı aç, son ödemesi yaklaşan/gelen borçlu bir kartta yeşil şerit görünmeli; ödeme
girince kaybolmalı. (Ekran görüntüsüyle doğrula.)

- [ ] **Step 4: Commit**

```bash
git add Kasa.App/Views/KrediKartlariPage.xaml
git -c user.email=musa@royalmezat.com commit -m "feat(app): kart sayfası ödeme hatırlatma şeridi"
```

---

## Task 10: `WindowsBildirimServisi` (AppNotificationManager)

**Files:**
- Create: `Kasa.App/Platforms/Windows/WindowsBildirimServisi.cs`

Platform kodu; birim-test yok. `Microsoft.Windows.AppNotifications` ve
`Microsoft.Windows.AppNotifications.Builder` Windows App SDK ile gelir (MAUI Windows'ta mevcut).

- [ ] **Step 1: Servisi yaz**

`Kasa.App/Platforms/Windows/WindowsBildirimServisi.cs`:
```csharp
using Kasa.App.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Kasa.App.Platforms.Windows;

/// <summary>Windows App SDK toast bildirimleri (paketsiz .exe).</summary>
public sealed class WindowsBildirimServisi : IBildirimServisi
{
    private bool _kayitli;

    public void KayitOl()
    {
        if (_kayitli) return;
        var mgr = AppNotificationManager.Default;
        mgr.NotificationInvoked += (_, args) =>
        {
            // args.Arguments["kartId"] → uygulamayı Kredi Kartları sayfasına yönlendir
            if (args.Arguments.TryGetValue("git", out var hedef) && hedef == "kredikartlari")
                Yonlendir();
        };
        mgr.Register();
        _kayitli = true;
    }

    public Task GosterAsync(IReadOnlyList<Hatirlatma> hatirlatmalar)
    {
        foreach (var h in hatirlatmalar)
        {
            var metin = h.Tur switch
            {
                HatirlatmaTuru.Kesim        => $"Ekstre kesildi — güncel borç {Bicim.Tl(h.EkstreBorc)}",
                HatirlatmaTuru.SonOdeme3Gun => $"Son ödemeye 3 gün — {Bicim.Tl(h.EkstreBorc)}",
                _                           => "Son ödeme bugün — ödedin mi?",
            };
            var toast = new AppNotificationBuilder()
                .AddText(h.KartAd)
                .AddText(metin)
                .AddButton(new AppNotificationButton("Uygulamada işaretle")
                    .AddArgument("git", "kredikartlari")
                    .AddArgument("kartId", h.KartId.ToString()))
                .BuildNotification();
            AppNotificationManager.Default.Show(toast);
        }
        return Task.CompletedTask;
    }

    private static void Yonlendir()
    {
        // MAUI ana thread'inde Kredi Kartları sekmesine git.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Microsoft.Maui.Controls.Shell.Current is { } shell)
                await shell.GoToAsync("//kredikartlari");
        });
    }
}
```
> **Doğrulama noktaları (implementasyon sırasında):** (a) `AppNotificationBuilder`/`AppNotificationButton`
> API imzaları WindowsAppSDK sürümüne göre değişebilir — derleme hatası olursa mevcut SDK sürümünün
> imzasına uyarla. (b) `Shell.Current.GoToAsync` rota adı, `AppShell.xaml`'daki Kredi Kartları
> route'una göre ayarla (mevcut route adını AppShell'den oku). (c) `Bicim.Tl` `Kasa.App.Core`'da mevcut.

- [ ] **Step 2: Build**

Run: `taskkill //F //IM Kasa.App.exe 2>/dev/null; dotnet build Kasa.App/Kasa.App.csproj -f net10.0-windows10.0.19041.0`
Expected: 0 hata (API imzası sapması varsa düzelt).

- [ ] **Step 3: Commit**

```bash
git add Kasa.App/Platforms/Windows/WindowsBildirimServisi.cs
git -c user.email=musa@royalmezat.com commit -m "feat(app): Windows toast bildirim servisi"
```

---

## Task 11: Headless kontrol modu + görev kaydı

**Files:**
- Create: `Kasa.App/Platforms/Windows/HatirlatmaKontrol.cs`
- Modify: `Kasa.App/Platforms/Windows/App.xaml.cs`

Platform kodu; birim-test yok.

- [ ] **Step 1: Kontrol + görev yardımcısını yaz**

`Kasa.App/Platforms/Windows/HatirlatmaKontrol.cs`:
```csharp
using System.Diagnostics;
using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Platforms.Windows;

public static class HatirlatmaKontrol
{
    public const string Arg = "--hatirlatma-kontrol";

    /// <summary>Headless: token'la veriyi çek, hatırlatmaları hesapla, göster. Pencere açmaz.</summary>
    public static async Task CalistirAsync(IServiceProvider sp)
    {
        try
        {
            var api = sp.GetService(typeof(IKasaApi)) as IKasaApi;
            if (api is null) return;
            var kartlarDto = await api.KrediKartlariAsync();     // token yoksa 401 → catch
            var gorunumler = new List<KrediKartiGorunum>();
            foreach (var k in kartlarDto)
            {
                var g = new KrediKartiGorunum(k);
                foreach (var o in await api.KartOdemelerAsync(k.Id)) g.Odemeler.Add(o);
                gorunumler.Add(g);
            }
            var hatirlatmalar = KartHatirlatici.VadesiGelenler(
                gorunumler, DateOnly.FromDateTime(DateTime.Today));
            if (hatirlatmalar.Count > 0)
            {
                var bildirim = new WindowsBildirimServisi();
                bildirim.KayitOl();
                await bildirim.GosterAsync(hatirlatmalar);
            }
        }
        catch { /* token yok/expired/ağ — sessizce atla */ }
    }

    /// <summary>İlk normal açılışta kullanıcı-düzeyi günlük görev yoksa oluştur.</summary>
    public static void GoreviGarantile()
    {
        try
        {
            const string ad = "EmarKasaHatirlatici";
            var exe = Environment.ProcessPath!;
            var sorgu = Process.Start(new ProcessStartInfo("schtasks",
                $"/Query /TN {ad}") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true })!;
            sorgu.WaitForExit();
            if (sorgu.ExitCode == 0) return;                     // görev zaten var
            Process.Start(new ProcessStartInfo("schtasks",
                $"/Create /TN {ad} /SC DAILY /ST 09:00 /F /TR \"\\\"{exe}\\\" {Arg}\"")
                { UseShellExecute = false, CreateNoWindow = true })!.WaitForExit();
        }
        catch { /* görev kurulamazsa uygulama-içi şerit yine çalışır */ }
    }
}
```

- [ ] **Step 2: Arg tespitini bağla**

`Kasa.App/Platforms/Windows/App.xaml.cs` — `OnLaunched`'i override et; headless arg varsa pencere
açmadan kontrolü çalıştır ve çık:
```csharp
using Microsoft.UI.Xaml;
using Kasa.App.Platforms.Windows;

namespace Kasa.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App() => this.InitializeComponent();
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cmd = Environment.GetCommandLineArgs();
        if (cmd.Contains(HatirlatmaKontrol.Arg))
        {
            var app = CreateMauiApp();                 // DI konteyneri (pencere açmadan)
            await HatirlatmaKontrol.CalistirAsync(app.Services);
            Exit();
            return;
        }
        base.OnLaunched(args);
    }
}
```
> **Doğrulama:** `MauiWinUIApplication.OnLaunched` imzası ve `Exit()` çağrısı MAUI sürümüne göre
> teyit et. Headless yolda MAUI penceresi OLUŞMAMALI. `CreateMauiApp().Services` ile IKasaApi
> ve SecureStorage token'ına eriş.

- [ ] **Step 3: Build**

Run: `taskkill //F //IM Kasa.App.exe 2>/dev/null; dotnet build Kasa.App/Kasa.App.csproj -f net10.0-windows10.0.19041.0`
Expected: 0 hata.

- [ ] **Step 4: Manuel doğrula**

`bin/.../Kasa.App.exe --hatirlatma-kontrol` çalıştır → pencere açılmamalı; vadesi gelen borçlu kart
varsa toast çıkmalı. Görev: normal açılış sonrası `schtasks /Query /TN EmarKasaHatirlatici` görevi
görmeli.

- [ ] **Step 5: Commit**

```bash
git add Kasa.App/Platforms/Windows/HatirlatmaKontrol.cs Kasa.App/Platforms/Windows/App.xaml.cs
git -c user.email=musa@royalmezat.com commit -m "feat(app): headless hatırlatma kontrol modu + zamanlanmış görev"
```

---

## Task 12: DI kaydı + açılışta KayitOl + görev garantisi

**Files:**
- Modify: `Kasa.App/MauiProgram.cs`
- Modify: `Kasa.App/App.xaml.cs`

- [ ] **Step 1: DI kaydı**

`Kasa.App/MauiProgram.cs` — servis kayıtları arasına ekle (Windows implementasyonu koşullu):
```csharp
#if WINDOWS
        builder.Services.AddSingleton<IBildirimServisi, Platforms.Windows.WindowsBildirimServisi>();
#endif
```

- [ ] **Step 2: Açılışta kayıt + görev**

`Kasa.App/App.xaml.cs` — `CreateWindow` içinde (normal açılış), bildirim kaydı ve görev garantisi:
```csharp
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var svc = Current!.Handler!.MauiContext!.Services;
#if WINDOWS
        svc.GetService<Kasa.App.Core.IBildirimServisi>()?.KayitOl();
        Platforms.Windows.HatirlatmaKontrol.GoreviGarantile();
#endif
        var shell = svc.GetRequiredService<AppShell>();
        return new Window(shell);
    }
```
> Not: `using Microsoft.Extensions.DependencyInjection;` dosyada mevcut. `GetService<T>()` için
> aynı namespace yeterli.

- [ ] **Step 3: Build + tam regresyon**

Run: `taskkill //F //IM Kasa.App.exe 2>/dev/null; dotnet build Kasa.App/Kasa.App.csproj -f net10.0-windows10.0.19041.0`
Expected: 0 hata.

- [ ] **Step 4: Commit**

```bash
git add Kasa.App/MauiProgram.cs Kasa.App/App.xaml.cs
git -c user.email=musa@royalmezat.com commit -m "feat(app): bildirim servisi DI + açılışta kayıt/görev"
```

---

## Task 13: Tam regresyon + uçtan doğrulama

- [ ] **Step 1: Tüm testler**

Run:
```bash
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj
```
Expected: hepsi PASS. HesapMotoru testleri değişmeden yeşil (regresyon kalkanı).

- [ ] **Step 2: MAUI Windows build**

Run: `taskkill //F //IM Kasa.App.exe 2>/dev/null; dotnet build Kasa.App/Kasa.App.csproj -f net10.0-windows10.0.19041.0`
Expected: 0 hata.

- [ ] **Step 3: Manuel uçtan senaryo**

Bir kartın kesim/son ödeme günlerini bugüne/yakına ayarla, borç bırak → uygulama-içi şerit görünür;
`--hatirlatma-kontrol` toast üretir; toast butonu uygulamayı Kredi Kartları'na getirir; ödeme
girince şerit kaybolur. Borcu sıfırla → hiçbir hatırlatma çıkmaz.

- [ ] **Step 4: Deploy kararı**

Sunucu `EkstreBorc` değişikliği prod'a gider (SPA/istemci geriye uyumlu — alan opsiyonel).
Kullanıcı onayıyla: kod sync + `docker compose -f docker-compose.nginx.yml up -d --build`.
Not: DB şeması değişmedi (yalnız türetme), migration GEREKMEZ.

---

## Notlar

- **DRY istisnası:** `KartDonem` (sunucu/Kasa.Core) ve `KartTarih` (istemci/Kasa.App.Core) benzer
  yinelenme mantığını taşır; iki proje ortak kütüphane paylaşmadığından bilinçli küçük tekrar,
  ikisi de test edilir.
- **Platform doğrulaması:** Task 10-12 birim-test edilemez (Windows App SDK + Task Scheduler);
  API imza sapmalarını implementasyonda düzelt, manuel senaryoyla doğrula.
- **Bilinen basitleştirme:** kısmi ödeme = dönem "ele alınmış" (susar) — spec §Kapılama.
