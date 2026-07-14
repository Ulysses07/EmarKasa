# Emar Kasa — Kasa.App MAUI (izleyici/okuma uygulaması) (Plan 3/…) Uygulama Planı

> **Ajan işçiler için:** GEREKLİ ALT-SKILL: superpowers:subagent-driven-development (önerilir)
> veya superpowers:executing-plans. Adımlar checkbox (`- [ ]`) sözdizimi kullanır.

**Goal:** `Kasa.App` MAUI head + test edilebilir `Kasa.App.Core` ViewModel katmanı: tema, rol-bazlı
Shell, auth akışı, SecureStorage token deposu ve **tüm okuma/rapor ekranları** (Panel, Haftalık,
Aylık, Cariler-okuma, İşlemler-okuma, Kredi Kartları-okuma). Bu plan **izleyici-tam** çalışan bir
uygulama üretir (telefon deneyimi bütünüyle). Editör CRUD **yazımı** (ekle/düzenle/sil, Gelen girişi,
Ayarlar) sonraki plana bırakılır.

**Architecture:** 3 proje. `Kasa.App.Core` (saf net10.0) ViewModel + biçim + rol/nav mantığını
tutar; `Kasa.ApiClient`'e ve `CommunityToolkit.Mvvm`'e bağlıdır; **tamamen test edilebilir**.
`Kasa.App` (MAUI head) yalnız View/XAML, tema, SecureStorage, DI'ı içerir; `Kasa.App.Core`'a
bağlıdır. `Kasa.App.Core.Tests` (xunit) VM/mantık testlerini tutar. VM'ler test kolaylığı için
`Kasa.ApiClient`'e Plan 2'ye eklenecek küçük `IKasaApi` arayüzü üzerinden bağlanır (elle sahte).

**Tech Stack:** .NET 10, .NET MAUI, `CommunityToolkit.Mvvm` (`ObservableObject`/`RelayCommand`),
xUnit. MAUI head TFM'leri: `net10.0-android;net10.0-ios;net10.0-windows`. Harici başka bağımlılık yok.

**Referans:** spec `docs/specs/2026-07-14-emar-kasa-native-design.md` §5 (auth), §6 (rol-nav), §8
(tema). Tema hex'leri `web/src/theme.ts`; biçim `web/src/format.ts`; ekran şekilleri `web/src/screens/`.
API metotları `Kasa.ApiClient/KasaApiClient.cs` (Plan 2, master'da).

---

## ⚠️ Ön koşullar (uygulamaya başlamadan)

1. **MAUI workload kurulu DEĞİL** (`dotnet workload list` yalnız `wasm-tools` gösterir). MAUI head
   derlenmeden önce **bir kez** çalıştır:
   ```bash
   dotnet workload install maui
   ```
   İzin gerektirir; uzun sürer. Kurulamıyorsa `Kasa.App.Core` + testleri yine de tam derlenir/test
   edilir (MAUI'ye bağlı değil); yalnız `Kasa.App` head'i build Task 7-8'de bekler.
2. **iOS TFM'i Mac ister.** Bu Windows makinesinde yalnız `net10.0-windows` (ve mümkünse
   `net10.0-android`) build edilir. iOS build + smoke, kullanıcının Mac'inde yapılır (spec §9).
   Bu yüzden Task 7-8 doğrulaması `-f net10.0-windows...` ile yapılır.
3. **Test odağı:** TDD işi `Kasa.App.Core` + `Kasa.App.Core.Tests`'te (MAUI'siz, hızlı). XAML
   View'lar headless doğrulanamaz → yalnız build + kullanıcı cihazda elle smoke.

---

## API yüzeyi (VM'lerin çağıracağı — Plan 2, master'da)

`KasaApiClient` public metotları (hepsi `Task`):
- `LoginAsync(string? kullanici, string sifre)` → `LoginYanit(Rol, Token)`; token'ı store'a yazar.
- `BenKimAsync()` → `string?` (rol; 401 → `KasaApiException`).
- `CikisAsync()` → token'ı temizler.
- Okuma: `PanelAsync()`→`PanelDto`, `HaftalikAsync()`→`IReadOnlyList<HaftalikOzetDto>`,
  `AylikAsync(int yil, int ay)`→`AylikRaporDto`, `DonemlerAsync()`→`IReadOnlyList<DonemDto>`,
  `KanallarAsync()`→`IReadOnlyList<KanalDto>`, `CarilerAsync(string? ara=null)`→`IReadOnlyList<CariDto>`,
  `IslemlerAsync(DateOnly? baslangic=null, DateOnly? bitis=null, string? kanal=null, string? cari=null)`
  →`IReadOnlyList<IslemDto>`, `KrediKartlariAsync()`→`IReadOnlyList<KrediKartiDto>`,
  `GelenlerAsync(DateOnly? donemStart=null)`→`IReadOnlyList<GelenDto>`, `AyarlarAsync()`→`AyarlarDto`.
- Mutasyonlar (bu planda KULLANILMAZ; sonraki plan): Kanal/Cari/Islem/KrediKarti Olustur/Guncelle/Sil,
  GelenKaydetAsync, AyarGuncelleAsync, IzleyiciSifreAsync.

DTO'lar (`Kasa.ApiClient/Dtos.cs`): `PanelDto(decimal GuncelKasa, IReadOnlyList<KanalBakiyeDto> Kanallar,
decimal BuHaftaSonucu, decimal BuAySonucu)`, `KanalBakiyeDto(string Kanal, decimal Bakiye)`,
`HaftalikOzetDto(DonemDto Donem, IReadOnlyList<KanalHaftalikDto> Kanallar, decimal ToplamGelen, decimal
ToplamGiden, decimal KasaSonucu, decimal KasaDevir)`, `DonemDto(DateOnly Start, DateOnly End, int Yil,
int Ay)`, `KanalHaftalikDto(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir)`,
`AylikRaporDto(int Yil, int Ay, IReadOnlyList<KanalAylikDto> Kanallar)`, `KanalAylikDto(string Kanal,
decimal Gelen, decimal CariGiden, decimal SabitGider, decimal KrediKarti, decimal OrtakPay, decimal
AySonucu)`, `CariDto(int Id, string Ad, bool Aktif)`, `IslemDto(int Id, DateOnly Tarih, string Cari,
decimal TutarTl, string Kanal, GiderTipi Tip, string? Not)`, `KrediKartiDto(int Id, string Ad, DateOnly
KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc)`.

---

## Tema token'ları (spec §8 · `web/src/theme.ts`'ten birebir)

Yüzey: `appBg #F5F4EF`, `card #FFFFFF`, `pageDark #E9E7E1`, `inputBg #FDFCFA`. Metin: `ink #20261F`,
`sub #6F7566`, `muted #9AA08F`. Çizgi: `border #E3E0D6`, `rowLine #F1EFE8`. Marka: `green #1E5F46`,
`greenDark #174D38`, `greenSoft #F0F5F1`. Durum: `pos #1B7A4E`, `neg #C13A2E`. Sidebar (koyu):
`sidebar #1F2A23`, `sidebarActive #33453A`, `sidebarHover #28352C`, `sidebarText #C9D2C6`,
`sidebarTitle #F2F5EE`. Kanal renkleri: MEZAT `#C98A12`, PERAKENDE `#3572C1`, TOPTAN `#7E5BBF`,
Ortak `#7A828E`.

Biçim (`web/src/format.ts`): `fmt(n)` = `tr-TR`, min/max 2 kesir (1.308.800,00). `sfmt(n)` = işaretli
(`+`/`-` + `fmt(|n|)`). Para rakamları mono font.

---

## Dosya yapısı

**`Kasa.ApiClient/` (mevcut — küçük ekleme):**
- Oluştur: `Kasa.ApiClient/IKasaApi.cs` — `KasaApiClient`'in public metot yüzeyinin arayüzü.
- Değiştir: `Kasa.ApiClient/KasaApiClient.cs` — `: IKasaApi` ekle (sealed kalır).

**`Kasa.App.Core/` (yeni, net10.0 kütüphane):**
- `Kasa.App.Core.csproj` (refs `Kasa.ApiClient` + `CommunityToolkit.Mvvm`).
- `Bicim.cs` — `Tl(decimal)`/`ImzaliTl(decimal)`/`KanalRengi(string)` saf yardımcılar.
- `Rol.cs` — `Rol` enum + `SekmeModeli` (rol→görünür bölümler).
- `AuthViewModel.cs` — login + açılış /me + logout + hata.
- `PanelViewModel.cs`, `HaftalikViewModel.cs`, `AylikViewModel.cs`, `CarilerViewModel.cs`,
  `IslemlerViewModel.cs`, `KrediKartlariViewModel.cs` — okuma ekranı VM'leri.
- `KrediKartiGorunum.cs` — kart + `KalanLimit` hesaplı görünüm.

**`Kasa.App.Core.Tests/` (yeni, xunit):**
- `Kasa.App.Core.Tests.csproj`, `SahteApi.cs` (elle `IKasaApi` sahtesi), `BicimTests.cs`,
  `RolTests.cs`, `AuthViewModelTests.cs`, `KrediKartlariViewModelTests.cs`, `PanelViewModelTests.cs`.

**`Kasa.App/` (yeni, MAUI head):**
- `Kasa.App.csproj`, `MauiProgram.cs`, `App.xaml(.cs)`, `AppShell.xaml(.cs)`.
- `Resources/Styles/Colors.xaml`, `Resources/Styles/Styles.xaml`.
- `Platforms/…` (MAUI şablonu), `Services/SecureStorageTokenStore.cs`.
- `Views/LoginPage.xaml(.cs)`, `Views/PanelPage.xaml(.cs)`, `Views/HaftalikPage.xaml(.cs)`,
  `Views/AylikPage.xaml(.cs)`, `Views/CarilerPage.xaml(.cs)`, `Views/IslemlerPage.xaml(.cs)`,
  `Views/KrediKartlariPage.xaml(.cs)`.

**`Kasa.slnx`** — 3 yeni proje eklenir.

**Kapsam dışı (YAGNI, sonraki plan):** editör mutasyon UI (ekle/düzenle/sil dialogları), Gelen girişi,
Ayarlar ekranı, offline/önbellek, push. Bu planda ekranlar salt-okunur; VM'ler yalnız `...Async` okur.

---

## Task 1: `IKasaApi` arayüzü + 3 proje iskelesi + çözüme ekle

**Files:**
- Oluştur: `Kasa.ApiClient/IKasaApi.cs`; Değiştir: `Kasa.ApiClient/KasaApiClient.cs`
- Oluştur: `Kasa.App.Core/Kasa.App.Core.csproj`, `Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj`,
  `Kasa.App.Core.Tests/DumanTests.cs`; Değiştir: `Kasa.slnx`

- [ ] **Step 1: `IKasaApi` yaz** — `Kasa.ApiClient/IKasaApi.cs` (public metotları birebir yansıtır):
```csharp
namespace Kasa.ApiClient;

/// <summary>KasaApiClient'in test edilebilir yüzeyi (VM'ler buna bağlanır).</summary>
public interface IKasaApi
{
    Task<LoginYanit> LoginAsync(string? kullanici, string sifre);
    Task<string?> BenKimAsync();
    Task CikisAsync();

    Task<PanelDto> PanelAsync();
    Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync();
    Task<AylikRaporDto> AylikAsync(int yil, int ay);
    Task<IReadOnlyList<DonemDto>> DonemlerAsync();
    Task<IReadOnlyList<KanalDto>> KanallarAsync();
    Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null);
    Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null);
    Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync();
    Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null);
    Task<AyarlarDto> AyarlarAsync();
}
```

- [ ] **Step 2: `KasaApiClient`'e arayüzü uygula** — `KasaApiClient.cs`'te sınıf bildirimini değiştir:
`public sealed partial class KasaApiClient : IKasaApi` (metot gövdeleri zaten uyumlu; imza değişmez).

- [ ] **Step 3: ApiClient testleri hâlâ yeşil mi** — `dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj`
  Expected: PASS (22).

- [ ] **Step 4: `Kasa.App.Core.csproj` yaz** (`Kasa.Core`'a referans YOK):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <ProjectReference Include="..\Kasa.ApiClient\Kasa.ApiClient.csproj" />
  </ItemGroup>
</Project>
```
> Not: CommunityToolkit.Mvvm sürümü sabit değilse en güncel 8.x'i `dotnet add package` ile çöz;
> uydurma. Kurulan sürümü csproj'a yaz.

- [ ] **Step 5: `Kasa.App.Core.Tests.csproj` yaz** — `Kasa.ApiClient.Tests.csproj`'daki xunit/
  Microsoft.NET.Test.Sdk/coverlet PackageReference'larını AYNI sürümlerle kopyala; tek ProjectReference
  `..\Kasa.App.Core\Kasa.App.Core.csproj`; `<Using Include="Xunit" />` ekle.

- [ ] **Step 6: Duman testi** — `Kasa.App.Core.Tests/DumanTests.cs`:
```csharp
namespace Kasa.App.Core.Tests;

public class DumanTests
{
    [Fact]
    public void Derlenir() => Assert.True(true);
}
```

- [ ] **Step 7: Çözüme ekle + derle + test**
```bash
cd "C:\Users\burak\source\repos\Kasa"
dotnet sln add Kasa.App.Core/Kasa.App.Core.csproj
dotnet sln add Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj
```
Expected: PASS (1).

- [ ] **Step 8: Commit**
```bash
git add Kasa.ApiClient/IKasaApi.cs Kasa.ApiClient/KasaApiClient.cs Kasa.App.Core/ Kasa.App.Core.Tests/ Kasa.slnx
git commit -m "feat(app): IKasaApi + Kasa.App.Core/Tests iskelesi"
```

---

## Task 2: Biçim yardımcıları (TDD)

**Files:**
- Oluştur: `Kasa.App.Core/Bicim.cs`; Oluştur test: `Kasa.App.Core.Tests/BicimTests.cs`

- [ ] **Step 1: Testleri yaz** — `Kasa.App.Core.Tests/BicimTests.cs`:
```csharp
namespace Kasa.App.Core.Tests;

public class BicimTests
{
    [Fact]
    public void Tl_binlik_nokta_kurus_virgul()
        => Assert.Equal("1.308.800,00", Bicim.Tl(1308800m));

    [Fact]
    public void Tl_negatifi_biciminde_gosterir()
        => Assert.Equal("-48.200,00", Bicim.Tl(-48200m));

    [Fact]
    public void ImzaliTl_pozitife_arti_koyar()
        => Assert.Equal("+145.000,00", Bicim.ImzaliTl(145000m));

    [Fact]
    public void ImzaliTl_negatife_eksi_koyar()
        => Assert.Equal("-48.200,00", Bicim.ImzaliTl(-48200m));

    [Fact]
    public void KanalRengi_bilinen_kanali_dondurur()
        => Assert.Equal("#C98A12", Bicim.KanalRengi("MEZAT"));

    [Fact]
    public void KanalRengi_bilinmeyene_ortak_rengi()
        => Assert.Equal("#7A828E", Bicim.KanalRengi("BILINMEYEN"));
}
```

- [ ] **Step 2: Çalıştır, FAIL doğrula** (`Bicim` yok).

- [ ] **Step 3: `Bicim` yaz** — `Kasa.App.Core/Bicim.cs`:
```csharp
using System.Globalization;

namespace Kasa.App.Core;

/// <summary>tr-TR para biçimi + kanal renkleri (web/src/format.ts + theme.ts aynası).</summary>
public static class Bicim
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Tl(decimal n) => n.ToString("#,##0.00", Tr);

    public static string ImzaliTl(decimal n) => (n < 0 ? "-" : "+") + Tl(Math.Abs(n));

    public static string KanalRengi(string kanal) => kanal switch
    {
        "MEZAT" => "#C98A12",
        "PERAKENDE" => "#3572C1",
        "TOPTAN" => "#7E5BBF",
        _ => "#7A828E",
    };
}
```

- [ ] **Step 4: Çalıştır, PASS doğrula.**

- [ ] **Step 5: Commit**
```bash
git add Kasa.App.Core/Bicim.cs Kasa.App.Core.Tests/BicimTests.cs
git commit -m "feat(app): tr-TR para biçimi + kanal renk yardımcıları"
```

---

## Task 3: Rol + sekme modeli (TDD)

**Files:**
- Oluştur: `Kasa.App.Core/Rol.cs`; Oluştur test: `Kasa.App.Core.Tests/RolTests.cs`

Rol-nav (spec §6): editör (Windows) tam bölümler, izleyici (telefon) salt-okunur. Bu planda tüm
ekranlar salt-okunur; fark yalnız **hangi bölümler görünür + Ayarlar yalnız editörde**. Bölüm
listesini rol'den üreten saf bir fonksiyon test edilir.

- [ ] **Step 1: Testleri yaz** — `Kasa.App.Core.Tests/RolTests.cs`:
```csharp
namespace Kasa.App.Core.Tests;

public class RolTests
{
    [Fact]
    public void Rolu_coz_editor()
        => Assert.Equal(Rol.Editor, SekmeModeli.RolCoz("editor"));

    [Fact]
    public void Rolu_coz_izleyici_varsayilan()
    {
        Assert.Equal(Rol.Izleyici, SekmeModeli.RolCoz("viewer"));
        Assert.Equal(Rol.Izleyici, SekmeModeli.RolCoz(null));
        Assert.Equal(Rol.Izleyici, SekmeModeli.RolCoz("saçma"));
    }

    [Fact]
    public void Izleyici_ayarlari_gormez()
    {
        var bolumler = SekmeModeli.Bolumler(Rol.Izleyici);
        Assert.DoesNotContain(Bolum.Ayarlar, bolumler);
        Assert.Contains(Bolum.Panel, bolumler);
        Assert.Contains(Bolum.KrediKartlari, bolumler);
    }

    [Fact]
    public void Editor_ayarlari_gorur()
        => Assert.Contains(Bolum.Ayarlar, SekmeModeli.Bolumler(Rol.Editor));

    [Fact]
    public void Bolum_sirasi_panelle_baslar()
        => Assert.Equal(Bolum.Panel, SekmeModeli.Bolumler(Rol.Izleyici)[0]);
}
```

- [ ] **Step 2: Çalıştır, FAIL doğrula.**

- [ ] **Step 3: `Rol` + `SekmeModeli` yaz** — `Kasa.App.Core/Rol.cs`:
```csharp
namespace Kasa.App.Core;

public enum Rol { Izleyici, Editor }

public enum Bolum { Panel, Haftalik, Aylik, Cariler, Islemler, KrediKartlari, Ayarlar }

/// <summary>Rol → görünür bölümler (spec §6). Ayarlar yalnız editörde.</summary>
public static class SekmeModeli
{
    public static Rol RolCoz(string? rol)
        => string.Equals(rol, "editor", StringComparison.OrdinalIgnoreCase) ? Rol.Editor : Rol.Izleyici;

    public static IReadOnlyList<Bolum> Bolumler(Rol rol)
    {
        var liste = new List<Bolum>
        {
            Bolum.Panel, Bolum.Haftalik, Bolum.Aylik,
            Bolum.Cariler, Bolum.Islemler, Bolum.KrediKartlari,
        };
        if (rol == Rol.Editor) liste.Add(Bolum.Ayarlar);
        return liste;
    }
}
```

- [ ] **Step 4: Çalıştır, PASS doğrula.**

- [ ] **Step 5: Commit**
```bash
git add Kasa.App.Core/Rol.cs Kasa.App.Core.Tests/RolTests.cs
git commit -m "feat(app): rol + rol-bazlı bölüm modeli"
```

---

## Task 4: Sahte API + AuthViewModel (TDD)

**Files:**
- Oluştur: `Kasa.App.Core/AuthViewModel.cs`; Oluştur test: `Kasa.App.Core.Tests/SahteApi.cs`,
  `Kasa.App.Core.Tests/AuthViewModelTests.cs`

Auth akışı (spec §5): açılışta store'da token varsa `/me` ile doğrula (200→rol, 401→temizle+login);
login formu (kullanıcı? + şifre) → başarıda rol'ü sakla; hata mesajı göster; çıkış token'ı siler.
`ITokenStore`'u `BellekTokenStore` ile besleriz; API'yi elle `SahteApi` ile.

- [ ] **Step 1: `SahteApi` yaz** — `Kasa.App.Core.Tests/SahteApi.cs` (yalnız bu planda kullanılan
  metotlar anlamlı; kalanı `NotImplementedException`):
```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Elle IKasaApi sahtesi — VM testleri için canned yanıt + çağrı kaydı.</summary>
public sealed class SahteApi : IKasaApi
{
    public LoginYanit? LoginYaniti;
    public Exception? LoginHatasi;
    public string? MeRol;
    public Exception? MeHatasi;
    public bool CikisCagrildi;

    public PanelDto? Panel;
    public IReadOnlyList<KrediKartiDto> KrediKartlariListe = new List<KrediKartiDto>();
    public IReadOnlyList<CariDto> CarilerListe = new List<CariDto>();
    public IReadOnlyList<IslemDto> IslemlerListe = new List<IslemDto>();
    public IReadOnlyList<HaftalikOzetDto> HaftalikListe = new List<HaftalikOzetDto>();
    public AylikRaporDto? AylikRapor;
    public int SonAylikYil, SonAylikAy;

    public Task<LoginYanit> LoginAsync(string? kullanici, string sifre)
        => LoginHatasi is not null ? Task.FromException<LoginYanit>(LoginHatasi)
                                   : Task.FromResult(LoginYaniti!);
    public Task<string?> BenKimAsync()
        => MeHatasi is not null ? Task.FromException<string?>(MeHatasi) : Task.FromResult(MeRol);
    public Task CikisAsync() { CikisCagrildi = true; return Task.CompletedTask; }

    public Task<PanelDto> PanelAsync() => Task.FromResult(Panel!);
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => Task.FromResult(HaftalikListe);
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) { SonAylikYil = yil; SonAylikAy = ay; return Task.FromResult(AylikRapor!); }
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => Task.FromResult<IReadOnlyList<DonemDto>>(new List<DonemDto>());
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => Task.FromResult<IReadOnlyList<KanalDto>>(new List<KanalDto>());
    public Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null) => Task.FromResult(CarilerListe);
    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null) => Task.FromResult(IslemlerListe);
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => Task.FromResult(KrediKartlariListe);
    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null) => Task.FromResult<IReadOnlyList<GelenDto>>(new List<GelenDto>());
    public Task<AyarlarDto> AyarlarAsync() => throw new NotImplementedException();
}
```

- [ ] **Step 2: AuthViewModel testlerini yaz** — `Kasa.App.Core.Tests/AuthViewModelTests.cs`:
```csharp
using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class AuthViewModelTests
{
    [Fact]
    public async Task Basarili_login_rolu_ayarlar_ve_hata_temizler()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt") };
        var vm = new AuthViewModel(api);
        vm.Sifre = "sifre";

        await vm.GirisCommand.ExecuteAsync(null);

        Assert.Equal(Rol.Editor, vm.AktifRol);
        Assert.True(vm.GirisYapildi);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Basarisiz_login_hata_gosterir_giris_yapilmaz()
    {
        var api = new SahteApi { LoginHatasi = new KasaApiException(HttpStatusCode.Unauthorized) };
        var vm = new AuthViewModel(api);
        vm.Sifre = "yanlis";

        await vm.GirisCommand.ExecuteAsync(null);

        Assert.False(vm.GirisYapildi);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Acilis_gecerli_token_rolu_dondurur()
    {
        var api = new SahteApi { MeRol = "viewer" };
        var vm = new AuthViewModel(api);

        var girildi = await vm.AcilistaDogrulaAsync();

        Assert.True(girildi);
        Assert.Equal(Rol.Izleyici, vm.AktifRol);
    }

    [Fact]
    public async Task Acilis_401_giris_yapilmamis_dondurur()
    {
        var api = new SahteApi { MeHatasi = new KasaApiException(HttpStatusCode.Unauthorized) };
        var vm = new AuthViewModel(api);

        var girildi = await vm.AcilistaDogrulaAsync();

        Assert.False(girildi);
    }

    [Fact]
    public async Task Cikis_api_cikisini_cagirir_ve_giris_durumu_sifirlanir()
    {
        var api = new SahteApi { LoginYaniti = new LoginYanit("editor", "jwt") };
        var vm = new AuthViewModel(api);
        vm.Sifre = "s";
        await vm.GirisCommand.ExecuteAsync(null);

        await vm.CikisAsync();

        Assert.True(api.CikisCagrildi);
        Assert.False(vm.GirisYapildi);
    }
}
```

- [ ] **Step 3: Çalıştır, FAIL doğrula.**

- [ ] **Step 4: `AuthViewModel` yaz** — `Kasa.App.Core/AuthViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Login + açılış /me doğrulama + çıkış (spec §5).</summary>
public partial class AuthViewModel : ObservableObject
{
    private readonly IKasaApi _api;

    public AuthViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private string? _kullanici;
    [ObservableProperty] private string _sifre = "";
    [ObservableProperty] private string? _hata;
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private bool _girisYapildi;
    [ObservableProperty] private Rol _aktifRol;

    [RelayCommand]
    private async Task GirisAsync()
    {
        Hata = null;
        Mesgul = true;
        try
        {
            var yanit = await _api.LoginAsync(string.IsNullOrWhiteSpace(Kullanici) ? null : Kullanici, Sifre);
            AktifRol = SekmeModeli.RolCoz(yanit.Rol);
            GirisYapildi = true;
            Sifre = "";
        }
        catch (KasaApiException)
        {
            Hata = "Giriş başarısız. Bilgileri kontrol edin.";
        }
        catch (Exception)
        {
            Hata = "Sunucuya ulaşılamadı.";
        }
        finally
        {
            Mesgul = false;
        }
    }

    /// <summary>Açılışta store'daki token'ı /me ile doğrular. true = geçerli oturum.</summary>
    public async Task<bool> AcilistaDogrulaAsync()
    {
        try
        {
            var rol = await _api.BenKimAsync();
            AktifRol = SekmeModeli.RolCoz(rol);
            GirisYapildi = true;
            return true;
        }
        catch (Exception)
        {
            GirisYapildi = false;
            return false;
        }
    }

    public async Task CikisAsync()
    {
        try { await _api.CikisAsync(); }
        catch (Exception) { /* çıkışta hata önemsiz */ }
        GirisYapildi = false;
    }
}
```

- [ ] **Step 5: Çalıştır, PASS doğrula.**

- [ ] **Step 6: Commit**
```bash
git add Kasa.App.Core/AuthViewModel.cs Kasa.App.Core.Tests/SahteApi.cs Kasa.App.Core.Tests/AuthViewModelTests.cs
git commit -m "feat(app): AuthViewModel (login + açılış doğrulama + çıkış)"
```

---

## Task 5: Kredi Kartları VM + kalan-limit + Panel VM (TDD)

**Files:**
- Oluştur: `Kasa.App.Core/KrediKartiGorunum.cs`, `Kasa.App.Core/KrediKartlariViewModel.cs`,
  `Kasa.App.Core/PanelViewModel.cs`
- Oluştur test: `Kasa.App.Core.Tests/KrediKartlariViewModelTests.cs`,
  `Kasa.App.Core.Tests/PanelViewModelTests.cs`

- [ ] **Step 1: Testleri yaz** — `Kasa.App.Core.Tests/KrediKartlariViewModelTests.cs`:
```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class KrediKartlariViewModelTests
{
    [Fact]
    public async Task Yukle_karti_kalan_limitle_doldurur()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = new List<KrediKartiDto>
            {
                new(1, "Bonus", new DateOnly(2026,7,5), new DateOnly(2026,7,25), 100000m, 30000m),
            },
        };
        var vm = new KrediKartlariViewModel(api);

        await vm.YukleAsync();

        Assert.Single(vm.Kartlar);
        Assert.Equal(70000m, vm.Kartlar[0].KalanLimit);   // limit - borç
        Assert.Equal("Bonus", vm.Kartlar[0].Ad);
    }

    [Fact]
    public async Task Yukle_bos_liste_bos_koleksiyon()
    {
        var vm = new KrediKartlariViewModel(new SahteApi());
        await vm.YukleAsync();
        Assert.Empty(vm.Kartlar);
    }
}
```
`Kasa.App.Core.Tests/PanelViewModelTests.cs`:
```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class PanelViewModelTests
{
    [Fact]
    public async Task Yukle_panel_alanlarini_doldurur()
    {
        var api = new SahteApi
        {
            Panel = new PanelDto(90000m, new List<KanalBakiyeDto> { new("MEZAT", 150.5m) }, 10m, -5m),
        };
        var vm = new PanelViewModel(api);

        await vm.YukleAsync();

        Assert.Equal(90000m, vm.GuncelKasa);
        Assert.Single(vm.Kanallar);
        Assert.Equal("MEZAT", vm.Kanallar[0].Kanal);
    }
}
```

- [ ] **Step 2: Çalıştır, FAIL doğrula.**

- [ ] **Step 3: Görünüm + VM'leri yaz** — `Kasa.App.Core/KrediKartiGorunum.cs`:
```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kart + görünümde hesaplanan kalan limit (limit − borç, sunucuda saklanmaz).</summary>
public sealed class KrediKartiGorunum
{
    public int Id { get; }
    public string Ad { get; }
    public DateOnly KesimTarihi { get; }
    public DateOnly SonOdemeTarihi { get; }
    public decimal Limit { get; }
    public decimal Borc { get; }
    public decimal KalanLimit => Limit - Borc;

    public KrediKartiGorunum(KrediKartiDto d)
    {
        Id = d.Id; Ad = d.Ad; KesimTarihi = d.KesimTarihi;
        SonOdemeTarihi = d.SonOdemeTarihi; Limit = d.Limit; Borc = d.Borc;
    }
}
```
`Kasa.App.Core/KrediKartlariViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KrediKartlariViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public KrediKartlariViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<KrediKartiGorunum> Kartlar { get; } = new();

    [ObservableProperty] private bool _mesgul;

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var liste = await _api.KrediKartlariAsync();
            Kartlar.Clear();
            foreach (var k in liste) Kartlar.Add(new KrediKartiGorunum(k));
        }
        finally { Mesgul = false; }
    }
}
```
`Kasa.App.Core/PanelViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class PanelViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private decimal _guncelKasa;
    [ObservableProperty] private decimal _buHaftaSonucu;
    [ObservableProperty] private decimal _buAySonucu;
    [ObservableProperty] private bool _mesgul;
    public ObservableCollection<KanalBakiyeDto> Kanallar { get; } = new();

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var p = await _api.PanelAsync();
            GuncelKasa = p.GuncelKasa;
            BuHaftaSonucu = p.BuHaftaSonucu;
            BuAySonucu = p.BuAySonucu;
            Kanallar.Clear();
            foreach (var k in p.Kanallar) Kanallar.Add(k);
        }
        finally { Mesgul = false; }
    }
}
```

- [ ] **Step 4: Çalıştır, PASS doğrula.**

- [ ] **Step 5: Commit**
```bash
git add Kasa.App.Core/KrediKartiGorunum.cs Kasa.App.Core/KrediKartlariViewModel.cs Kasa.App.Core/PanelViewModel.cs Kasa.App.Core.Tests/KrediKartlariViewModelTests.cs Kasa.App.Core.Tests/PanelViewModelTests.cs
git commit -m "feat(app): Panel + KrediKartları VM (kalan limit hesabı)"
```

---

## Task 6: Kalan okuma VM'leri (Haftalık/Aylık/Cariler/İşlemler) (TDD)

**Files:**
- Oluştur: `Kasa.App.Core/HaftalikViewModel.cs`, `Kasa.App.Core/AylikViewModel.cs`,
  `Kasa.App.Core/CarilerViewModel.cs`, `Kasa.App.Core/IslemlerViewModel.cs`
- Oluştur test: `Kasa.App.Core.Tests/OkumaVmTests.cs`

- [ ] **Step 1: Testleri yaz** — `Kasa.App.Core.Tests/OkumaVmTests.cs`:
```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class OkumaVmTests
{
    [Fact]
    public async Task Haftalik_donemleri_yukler()
    {
        var api = new SahteApi
        {
            HaftalikListe = new List<HaftalikOzetDto>
            {
                new(new DonemDto(new DateOnly(2026,3,2), new DateOnly(2026,3,8), 2026, 3),
                    new List<KanalHaftalikDto>(), 0, 0, 100m, 100m),
            },
        };
        var vm = new HaftalikViewModel(api);
        await vm.YukleAsync();
        Assert.Single(vm.Donemler);
        Assert.Equal(100m, vm.Donemler[0].KasaSonucu);
    }

    [Fact]
    public async Task Aylik_secilen_yil_ayi_ister()
    {
        var api = new SahteApi { AylikRapor = new AylikRaporDto(2026, 4, new List<KanalAylikDto>()) };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 4 };
        await vm.YukleAsync();
        Assert.Equal(2026, api.SonAylikYil);
        Assert.Equal(4, api.SonAylikAy);
        Assert.NotNull(vm.Rapor);
    }

    [Fact]
    public async Task Cariler_listeyi_yukler()
    {
        var api = new SahteApi { CarilerListe = new List<CariDto> { new(1, "Ahmet", true) } };
        var vm = new CarilerViewModel(api);
        await vm.YukleAsync();
        Assert.Single(vm.Cariler);
        Assert.Equal("Ahmet", vm.Cariler[0].Ad);
    }

    [Fact]
    public async Task Islemler_listeyi_yukler()
    {
        var api = new SahteApi
        {
            IslemlerListe = new List<IslemDto>
            {
                new(1, new DateOnly(2026,3,5), "K.K", 10000m, "MEZAT", GiderTipi.KrediKarti, null),
            },
        };
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();
        Assert.Single(vm.Islemler);
        Assert.Equal(GiderTipi.KrediKarti, vm.Islemler[0].Tip);
    }
}
```

- [ ] **Step 2: Çalıştır, FAIL doğrula.**

- [ ] **Step 3: VM'leri yaz.** `Kasa.App.Core/HaftalikViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class HaftalikViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public HaftalikViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private bool _mesgul;
    public ObservableCollection<HaftalikOzetDto> Donemler { get; } = new();

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var liste = await _api.HaftalikAsync();
            Donemler.Clear();
            foreach (var d in liste) Donemler.Add(d);
        }
        finally { Mesgul = false; }
    }
}
```
`Kasa.App.Core/AylikViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AylikViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public AylikViewModel(IKasaApi api)
    {
        _api = api;
        var bugun = DateTime.Today;
        _yil = bugun.Year;
        _ay = bugun.Month;
    }

    [ObservableProperty] private int _yil;
    [ObservableProperty] private int _ay;
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private AylikRaporDto? _rapor;

    public async Task YukleAsync()
    {
        Mesgul = true;
        try { Rapor = await _api.AylikAsync(Yil, Ay); }
        finally { Mesgul = false; }
    }
}
```
`Kasa.App.Core/CarilerViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class CarilerViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public CarilerViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private string? _ara;
    public ObservableCollection<CariDto> Cariler { get; } = new();

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var liste = await _api.CarilerAsync(string.IsNullOrWhiteSpace(Ara) ? null : Ara);
            Cariler.Clear();
            foreach (var c in liste) Cariler.Add(c);
        }
        finally { Mesgul = false; }
    }
}
```
`Kasa.App.Core/IslemlerViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class IslemlerViewModel : ObservableObject
{
    private readonly IKasaApi _api;
    public IslemlerViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private bool _mesgul;
    public ObservableCollection<IslemDto> Islemler { get; } = new();

    public async Task YukleAsync()
    {
        Mesgul = true;
        try
        {
            var liste = await _api.IslemlerAsync();
            Islemler.Clear();
            foreach (var i in liste) Islemler.Add(i);
        }
        finally { Mesgul = false; }
    }
}
```

- [ ] **Step 4: Çalıştır, PASS doğrula** (Core testleri: biçim + rol + auth + panel + kart + okuma).

- [ ] **Step 5: Commit**
```bash
git add Kasa.App.Core/HaftalikViewModel.cs Kasa.App.Core/AylikViewModel.cs Kasa.App.Core/CarilerViewModel.cs Kasa.App.Core/IslemlerViewModel.cs Kasa.App.Core.Tests/OkumaVmTests.cs
git commit -m "feat(app): Haftalık/Aylık/Cariler/İşlemler okuma VM'leri"
```

---

## Task 7: MAUI head — proje + tema + SecureStorage + Shell + Views (build-only)

MAUI head kabuğunu `dotnet new maui` şablonundan üret, `Kasa.App.Core`'a bağla, temayı + rol-bazlı
Shell'i + salt-okunur View'ları kur. **TDD yok** (headless doğrulanamaz); doğrulama = windows TFM build.

**Ön koşul:** `dotnet workload install maui` çalıştırılmış olmalı (yukarı bkz.).

**Files (başlıca):** `Kasa.App/Kasa.App.csproj`, `MauiProgram.cs`, `App.xaml(.cs)`,
`AppShell.xaml(.cs)`, `Resources/Styles/Colors.xaml`, `Resources/Styles/Styles.xaml`,
`Services/SecureStorageTokenStore.cs`, `Views/LoginPage.xaml(.cs)` + 6 okuma Page'i.

- [ ] **Step 1: MAUI projesini üret + çözüme ekle**
```bash
cd "C:\Users\burak\source\repos\Kasa"
dotnet new maui -n Kasa.App -o Kasa.App
dotnet sln add Kasa.App/Kasa.App.csproj
```
Sonra `Kasa.App/Kasa.App.csproj`'da: `<ApplicationTitle>Emar Kasa</ApplicationTitle>`,
`<ApplicationId>com.royalmezat.kasa</ApplicationId>`, TFM'leri `net10.0-android;net10.0-ios;net10.0-windows10.0.19041.0`
olarak ayarla (şablon farklıysa net10.0'a çek), ve ProjectReference ekle:
`<ProjectReference Include="..\Kasa.App.Core\Kasa.App.Core.csproj" />`. Şablonun ürettiği örnek
`MainPage.xaml`/`.cs`'i sil.

- [ ] **Step 2: SecureStorage token deposu** — `Kasa.App/Services/SecureStorageTokenStore.cs`
  (`ITokenStore`'un gerçek impl'i; spec §3/§5):
```csharp
using Kasa.ApiClient;

namespace Kasa.App.Services;

/// <summary>Token'ı MAUI SecureStorage'da tutar (platform güvenli deposu).</summary>
public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string Anahtar = "kasa_token";

    public async Task<string?> OkuAsync() => await SecureStorage.Default.GetAsync(Anahtar);
    public async Task YazAsync(string token) => await SecureStorage.Default.SetAsync(Anahtar, token);
    public Task TemizleAsync() { SecureStorage.Default.Remove(Anahtar); return Task.CompletedTask; }
}
```

- [ ] **Step 3: Colors.xaml** — `Kasa.App/Resources/Styles/Colors.xaml` (spec §8 hex'leri):
```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <Color x:Key="AppBg">#F5F4EF</Color>
    <Color x:Key="Card">#FFFFFF</Color>
    <Color x:Key="Ink">#20261F</Color>
    <Color x:Key="Sub">#6F7566</Color>
    <Color x:Key="Muted">#9AA08F</Color>
    <Color x:Key="Border">#E3E0D6</Color>
    <Color x:Key="RowLine">#F1EFE8</Color>
    <Color x:Key="Green">#1E5F46</Color>
    <Color x:Key="GreenDark">#174D38</Color>
    <Color x:Key="GreenSoft">#F0F5F1</Color>
    <Color x:Key="Pos">#1B7A4E</Color>
    <Color x:Key="Neg">#C13A2E</Color>
    <Color x:Key="Sidebar">#1F2A23</Color>
    <Color x:Key="SidebarActive">#33453A</Color>
    <Color x:Key="SidebarText">#C9D2C6</Color>
    <Color x:Key="SidebarTitle">#F2F5EE</Color>
</ResourceDictionary>
```

- [ ] **Step 4: Styles.xaml** — `Kasa.App/Resources/Styles/Styles.xaml` (temel implicit stiller):
```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <Style TargetType="ContentPage" ApplyToDerivedTypes="True">
        <Setter Property="BackgroundColor" Value="{StaticResource AppBg}" />
        <Setter Property="Padding" Value="16" />
    </Style>
    <Style TargetType="Label">
        <Setter Property="TextColor" Value="{StaticResource Ink}" />
        <Setter Property="FontFamily" Value="PlexSans" />
    </Style>
    <Style x:Key="Para" TargetType="Label">
        <Setter Property="FontFamily" Value="PlexMono" />
        <Setter Property="TextColor" Value="{StaticResource Ink}" />
    </Style>
    <Style x:Key="Kart" TargetType="Border">
        <Setter Property="BackgroundColor" Value="{StaticResource Card}" />
        <Setter Property="Stroke" Value="{StaticResource Border}" />
        <Setter Property="StrokeShape" Value="RoundRectangle 12" />
        <Setter Property="Padding" Value="16" />
    </Style>
    <Style TargetType="Button">
        <Setter Property="BackgroundColor" Value="{StaticResource Green}" />
        <Setter Property="TextColor" Value="#FFFFFF" />
        <Setter Property="FontFamily" Value="PlexSans" />
        <Setter Property="CornerRadius" Value="10" />
    </Style>
</ResourceDictionary>
```
> Fontlar: IBM Plex Sans/Mono `.ttf`'lerini `Kasa.App/Resources/Fonts/`'a koy ve `MauiProgram`'da
> `PlexSans`/`PlexMono` diye kaydet (Step 5). Font dosyaları repoda yoksa Google Fonts'tan indirilip
> eklenir; yoksa `FontFamily` satırlarını kaldır (sistem fontu). **Font indirme kullanıcı onayı ister.**

- [ ] **Step 5: MauiProgram DI** — `Kasa.App/MauiProgram.cs`:
```csharp
using CommunityToolkit.Maui;   // yalnız kullanılırsa; yoksa kaldır
using Kasa.ApiClient;
using Kasa.App.Core;
using Kasa.App.Services;
using Microsoft.Extensions.Logging;

namespace Kasa.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("IBMPlexSans-Regular.ttf", "PlexSans");
                fonts.AddFont("IBMPlexMono-Regular.ttf", "PlexMono");
            });

        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddSingleton(sp =>
        {
            var http = new HttpClient { BaseAddress = new Uri("https://kasa.royalmezat.com/") };
            return new KasaApiClient(http, sp.GetRequiredService<ITokenStore>());
        });
        builder.Services.AddSingleton<IKasaApi>(sp => sp.GetRequiredService<KasaApiClient>());

        builder.Services.AddSingleton<AuthViewModel>();
        builder.Services.AddTransient<PanelViewModel>();
        builder.Services.AddTransient<HaftalikViewModel>();
        builder.Services.AddTransient<AylikViewModel>();
        builder.Services.AddTransient<CarilerViewModel>();
        builder.Services.AddTransient<IslemlerViewModel>();
        builder.Services.AddTransient<KrediKartlariViewModel>();

        builder.Services.AddSingleton<App>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<Views.LoginPage>();
        builder.Services.AddTransient<Views.PanelPage>();
        builder.Services.AddTransient<Views.HaftalikPage>();
        builder.Services.AddTransient<Views.AylikPage>();
        builder.Services.AddTransient<Views.CarilerPage>();
        builder.Services.AddTransient<Views.IslemlerPage>();
        builder.Services.AddTransient<Views.KrediKartlariPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
```
> `BaseAddress` = `https://kasa.royalmezat.com/` (Plan 4'te SPA kaldırılıp yalnız API kalacak; uçlar
> `/api/...`). `CommunityToolkit.Maui` gerçekten kullanılmıyorsa o `using`'i ve paketi ekleme.

- [ ] **Step 6: App + AppShell (rol-bazlı)** — `Kasa.App/App.xaml.cs` açılışta auth doğrular:
```csharp
namespace Kasa.App;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(new AppShell());
}
```
`Kasa.App/AppShell.xaml` — Login + tüm bölümleri tanımlar; görünürlük rol'e göre kod-arkasında
ayarlanır:
```xml
<?xml version="1.0" encoding="UTF-8" ?>
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:v="clr-namespace:Kasa.App.Views"
       x:Class="Kasa.App.AppShell"
       FlyoutBackgroundColor="{StaticResource Sidebar}">
    <ShellContent Route="login" ContentTemplate="{DataTemplate v:LoginPage}" FlyoutItemIsVisible="False" />
    <FlyoutItem x:Name="AnaMenu" Title="Emar Kasa" IsVisible="False">
        <ShellContent Title="Panel" Route="panel" ContentTemplate="{DataTemplate v:PanelPage}" />
        <ShellContent Title="Haftalık" Route="haftalik" ContentTemplate="{DataTemplate v:HaftalikPage}" />
        <ShellContent Title="Aylık" Route="aylik" ContentTemplate="{DataTemplate v:AylikPage}" />
        <ShellContent Title="Cariler" Route="cariler" ContentTemplate="{DataTemplate v:CarilerPage}" />
        <ShellContent Title="İşlemler" Route="islemler" ContentTemplate="{DataTemplate v:IslemlerPage}" />
        <ShellContent Title="Kredi Kartları" Route="kartlar" ContentTemplate="{DataTemplate v:KrediKartlariPage}" />
    </FlyoutItem>
</Shell>
```
`Kasa.App/AppShell.xaml.cs` — açılış doğrulaması + giriş sonrası menüyü açma:
```csharp
using Kasa.App.Core;

namespace Kasa.App;

public partial class AppShell : Shell
{
    private readonly AuthViewModel _auth;

    public AppShell(AuthViewModel auth)
    {
        InitializeComponent();
        _auth = auth;
        Loaded += async (_, _) => await AcilistaYonlendirAsync();
    }

    private async Task AcilistaYonlendirAsync()
    {
        var girildi = await _auth.AcilistaDogrulaAsync();
        if (girildi) MenuyuAc();
        else await GoToAsync("//login");
    }

    public void MenuyuAc()
    {
        AnaMenu.IsVisible = true;
        // İzleyicide Ayarlar zaten kapsam dışı; bölüm görünürlüğü Plan 3b'de Ayarlar eklenince
        // SekmeModeli.Bolumler(_auth.AktifRol) ile filtrelenecek.
        _ = GoToAsync("//panel");
    }
}
```
> `AppShell` DI'dan `AuthViewModel` alır (Step 5'te singleton kayıtlı). `LoginPage` başarılı girişte
> `((AppShell)Shell.Current).MenuyuAc()` çağırır (Step 7).

- [ ] **Step 7: LoginPage + okuma Page'leri.** `Kasa.App/Views/LoginPage.xaml`:
```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="Kasa.App.Views.LoginPage"
             Shell.FlyoutBehavior="Disabled" Title="Giriş">
    <VerticalStackLayout Spacing="12" VerticalOptions="Center" MaximumWidthRequest="380">
        <Label Text="Emar Kasa" FontSize="26" TextColor="{StaticResource Green}" HorizontalOptions="Center" />
        <Entry x:Name="KullaniciGiris" Placeholder="Kullanıcı (editör)" Text="{Binding Kullanici}" />
        <Entry Placeholder="Şifre" IsPassword="True" Text="{Binding Sifre}" />
        <Label Text="{Binding Hata}" TextColor="{StaticResource Neg}" />
        <Button Text="Giriş" Command="{Binding GirisCommand}" />
        <ActivityIndicator IsRunning="{Binding Mesgul}" IsVisible="{Binding Mesgul}" />
    </VerticalStackLayout>
</ContentPage>
```
> Hata etiketinde converter YOK: `Text` boşken (null) etiket zaten yer kaplamaz. Görünürlük için
> converter/kod-arkası EKLEME — sadece `Text` binding yeterli.
`Kasa.App/Views/LoginPage.xaml.cs`:
```csharp
using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class LoginPage : ContentPage
{
    private readonly AuthViewModel _vm;

    public LoginPage(AuthViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthViewModel.GirisYapildi) && _vm.GirisYapildi)
                ((AppShell)Shell.Current).MenuyuAc();
        };
    }
}
```
Okuma Page'leri aynı desende (örnek `PanelPage`). `Kasa.App/Views/PanelPage.xaml`:
```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="Kasa.App.Views.PanelPage" Title="Panel">
    <ScrollView>
        <VerticalStackLayout Spacing="12">
            <Border Style="{StaticResource Kart}">
                <VerticalStackLayout>
                    <Label Text="Güncel Kasa" TextColor="{StaticResource Sub}" />
                    <Label Style="{StaticResource Para}" FontSize="28" Text="{Binding GuncelKasa}" />
                </VerticalStackLayout>
            </Border>
            <CollectionView ItemsSource="{Binding Kanallar}">
                <CollectionView.ItemTemplate>
                    <DataTemplate>
                        <Grid Padding="8" ColumnDefinitions="*,Auto">
                            <Label Text="{Binding Kanal}" />
                            <Label Grid.Column="1" Style="{StaticResource Para}" Text="{Binding Bakiye}" />
                        </Grid>
                    </DataTemplate>
                </CollectionView.ItemTemplate>
            </CollectionView>
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```
`Kasa.App/Views/PanelPage.xaml.cs`:
```csharp
using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class PanelPage : ContentPage
{
    private readonly PanelViewModel _vm;

    public PanelPage(PanelViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }
}
```
Diğer 5 Page (`HaftalikPage`, `AylikPage`, `CarilerPage`, `IslemlerPage`, `KrediKartlariPage`) AYNI
desen: `ContentPage` + `CollectionView` ilgili koleksiyona (`Donemler`/`Rapor.Kanallar`/`Cariler`/
`Islemler`/`Kartlar`) bağlanır; kod-arkası `OnAppearing`'de `_vm.YukleAsync()` çağırır; BindingContext
DI'dan gelen VM. `KrediKartlariPage` satırında `Ad`, `KalanLimit`, `Limit`, `Borc` gösterilir. Para
alanları `Style="{StaticResource Para}"`. (Her Page için ContentPage kökü + OnAppearing yükleme
şablonu birebir PanelPage'deki gibidir; yalnız ItemsSource ve satır alanları değişir.)

- [ ] **Step 8: Windows TFM build**
```bash
cd "C:\Users\burak\source\repos\Kasa"
dotnet build Kasa.App/Kasa.App.csproj -f net10.0-windows10.0.19041.0
```
Expected: BUILD SUCCEEDED (uyarılar kabul; hata yok). Derleme hatası varsa düzelt. iOS build
KULLANICININ Mac'inde (bu makinede atlanır).

- [ ] **Step 9: Commit**
```bash
git add Kasa.App/ Kasa.slnx
git commit -m "feat(app): MAUI head — tema, SecureStorage, rol-bazlı Shell, okuma ekranları"
```

---

## Task 8: Tam doğrulama + elle smoke notu

- [ ] **Step 1: Core testleri + build**
```bash
cd "C:\Users\burak\source\repos\Kasa"
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj
dotnet build Kasa.App/Kasa.App.csproj -f net10.0-windows10.0.19041.0
```
Expected: App.Core (~15) + ApiClient 22 + Core 18 + Api 22 PASS; MAUI head windows build başarılı.

- [ ] **Step 2: (Doğrulama, commit yok)** `Kasa.App.Core` `Kasa.Core`/EF'e ProjectReference VERMEMELİ
  (yalnız `Kasa.ApiClient` + `CommunityToolkit.Mvvm`).

- [ ] **Step 3: Elle smoke (kullanıcı)** — Windows head'i çalıştır (`dotnet build -t:Run` ya da VS'te
  F5), backend'e (`https://kasa.royalmezat.com`) editör bilgisiyle giriş; Panel/Haftalık/Aylık/Cariler/
  İşlemler/Kredi Kartları yükleniyor mu bak. iOS/Android: kullanıcı gerçek cihazda smoke eder. ⚠️
  Not: backend'de `KrediKartlari` tablosu Plan 4'te oluşturulana kadar Kredi Kartları ekranı "no such
  table" verebilir (spec §7 / Plan 4 ön koşulu).

---

## Notlar

- **Bağımsızlık:** `Kasa.App.Core` MAUI'ye bağlı değil → hızlı, headless test edilir. MAUI yalnız View
  katmanında. `IKasaApi` sayesinde VM'ler elle sahtelenir (HTTP'siz).
- **Kapsam sınırı:** Bu plan salt-okunur (izleyici-tam). Editör mutasyon UI (ekle/düzenle/sil, Gelen
  girişi, Ayarlar ekranı), rol'e göre bölüm filtreleme (Ayarlar gizleme), ve gerçekleştirimi Plan 3b.
- **Erteleme kuralı görünürlüğü:** Aylık/Haftalık zaten sunucudan KK-ertelemeli değerleri okur (Plan 1);
  istemci ekstra hesap yapmaz — yalnız gösterir.
- **Plan 4 bağı:** yayın öncesi VPS DB yeniden oluşturma (`KrediKartlari`) + SPA sunumu kaldırma hâlâ
  Plan 4'te; bu app onları varsayar.
