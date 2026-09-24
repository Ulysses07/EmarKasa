# Emar Kasa — Plan 3b (editör CRUD + inceleme nit'leri) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plan 3'ün okuma-only MAUI uygulamasına editör CRUD yeteneği (Cari/İşlem/Kredi Kartı/Kanal/Ayar/Gelen) eklemek ve üç inceleme nit'ini kapatmak: (a) rol-nav Shell'e bağlama, (b) çıkış UI'ı, (c) okuma VM'lerinde ağ hatası yüzeyi.

**Architecture:** Testable çekirdek `Kasa.App.Core`'da: `IKasaApi` seam'ine mutation metotları eklenir (KasaApiClient onları zaten uyguluyor), ortak `TemelViewModel` (Mesgul+Hata+`CalistirAsync`) tüm VM'lerin altına konur, list VM'lerine editör komutları (Yeni/Duzenle/Kaydet/Sil) eklenir. MAUI head (`Kasa.App`, yalnız Windows) editör XAML formlarını, rol-nav'ı ve çıkışı barındırır (build-only). `SahteApi` elle sahte, çağrı kaydı yapar.

**Tech Stack:** .NET 10, .NET MAUI (`net10.0-windows10.0.19041.0`), CommunityToolkit.Mvvm 8.4.0 (`[ObservableProperty]`/`[RelayCommand]`), xUnit.

**Not (git):** Bu repo yerel-only, REMOTE YOK. Commit author "Musa Sevinç", **Co-Authored-By trailer YOK**. Dal: `feat/emar-kasa-app-3b`.

**⚠️ RelayCommand adlandırma:** `[RelayCommand] private async Task KaydetAsync()` → `KaydetCommand` üretir ("Async" atılır, "Command" eklenir). Parametreli metot `IRelayCommand<T>` üretir.

---

## Dosya yapısı

**Kasa.ApiClient (seam):**
- Modify `Kasa.ApiClient/IKasaApi.cs` — 15 mutation metodu eklenir. `KasaApiClient` bunları zaten public olarak uyguluyor; arayüz genişlemesi derlemeyi bozmaz.

**Kasa.App.Core (testable VM katmanı):**
- Create `Kasa.App.Core/TemelViewModel.cs` — `Mesgul` + `Hata` + `CalistirAsync(Func<Task>)` sarmalayıcı.
- Modify 6 okuma VM'i (Panel/Haftalik/Aylik/Cariler/Islemler/KrediKartlari) — `TemelViewModel`'e taşınır, gövde `CalistirAsync`'e sarılır (nit c).
- Modify `CarilerViewModel`, `KrediKartlariViewModel`, `IslemlerViewModel` — editör komutları.
- Create `Kasa.App.Core/AyarlarViewModel.cs` — Kanal CRUD + izleyici şifre + ayar.

**Kasa.App.Core.Tests:**
- Modify `SahteApi.cs` — mutation metotları (çağrı kaydı) + okuma-hata (`YuklemeHatasi`) + `AyarlarAsync` canned.
- Create `HataYuzeyiTests.cs`, `CariEditorTests.cs`, `KrediKartiEditorTests.cs`, `IslemEditorTests.cs`, `AyarlarViewModelTests.cs`.

**Kasa.App (MAUI head, build-only):**
- Modify `AppShell.xaml(.cs)` — Ayarlar sekmesi (editöre) + rol-nav (nit a) + Çıkış menüsü (nit b).
- Modify `Views/CarilerPage.xaml(.cs)`, `IslemlerPage.xaml(.cs)`, `KrediKartlariPage.xaml(.cs)` — editör formu (EditorMu ile görünür).
- Create `Views/AyarlarPage.xaml(.cs)`.
- Modify `MauiProgram.cs` — AyarlarViewModel + AyarlarPage DI.

---

## Task 1: IKasaApi mutation seam + SahteApi

**Files:**
- Modify: `Kasa.ApiClient/IKasaApi.cs`
- Modify: `Kasa.App.Core.Tests/SahteApi.cs`

- [ ] **Step 1: IKasaApi'ye mutation metotlarını ekle**

`Kasa.ApiClient/IKasaApi.cs` içinde `AyarlarAsync();` satırından sonra, kapanış `}`'dan önce ekle:

```csharp

    // Editör mutasyonları (KasaApiClient bunları zaten uyguluyor)
    Task<KanalDto> KanalOlusturAsync(KanalYaz g);
    Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g);
    Task KanalSilAsync(int id);
    Task<CariDto> CariOlusturAsync(CariYaz g);
    Task<CariDto> CariGuncelleAsync(int id, CariYaz g);
    Task CariSilAsync(int id);
    Task<IslemDto> IslemOlusturAsync(IslemYaz g);
    Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g);
    Task IslemSilAsync(int id);
    Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g);
    Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g);
    Task KrediKartiSilAsync(int id);
    Task<GelenDto> GelenKaydetAsync(GelenYaz g);
    Task AyarGuncelleAsync(AyarYaz g);
    Task IzleyiciSifreAsync(string yeniSifre);
```

- [ ] **Step 2: Derlemenin bozulduğunu doğrula**

Run: `cd <repo> && dotnet build Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj 2>&1 | tail -20`
Expected: FAIL — `'SahteApi' does not implement interface member 'IKasaApi.KanalOlusturAsync(...)'` (ve diğerleri).

- [ ] **Step 3: SahteApi'ye mutation üyelerini + çağrı kayıtlarını ekle**

`Kasa.App.Core.Tests/SahteApi.cs` içinde `AyarlarAsync` satırının hemen üstüne alan bloğu, kapanış `}`'dan önce metotları ekle:

```csharp
    // Mutasyon çağrı kayıtları (son çağrıyı tutar)
    public KanalYaz? SonKanalOlustur;
    public (int Id, KanalYaz G)? SonKanalGuncelle;
    public int? SonKanalSil;
    public CariYaz? SonCariOlustur;
    public (int Id, CariYaz G)? SonCariGuncelle;
    public int? SonCariSil;
    public IslemYaz? SonIslemOlustur;
    public (int Id, IslemYaz G)? SonIslemGuncelle;
    public int? SonIslemSil;
    public KrediKartiYaz? SonKartOlustur;
    public (int Id, KrediKartiYaz G)? SonKartGuncelle;
    public int? SonKartSil;
    public GelenYaz? SonGelen;
    public AyarYaz? SonAyar;
    public string? SonIzleyiciSifre;

    public Task<KanalDto> KanalOlusturAsync(KanalYaz g) { SonKanalOlustur = g; return Task.FromResult(new KanalDto(0, g.Ad, g.Aktif, g.Sira, g.AcilisDevri)); }
    public Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g) { SonKanalGuncelle = (id, g); return Task.FromResult(new KanalDto(id, g.Ad, g.Aktif, g.Sira, g.AcilisDevri)); }
    public Task KanalSilAsync(int id) { SonKanalSil = id; return Task.CompletedTask; }
    public Task<CariDto> CariOlusturAsync(CariYaz g) { SonCariOlustur = g; return Task.FromResult(new CariDto(0, g.Ad, g.Aktif)); }
    public Task<CariDto> CariGuncelleAsync(int id, CariYaz g) { SonCariGuncelle = (id, g); return Task.FromResult(new CariDto(id, g.Ad, g.Aktif)); }
    public Task CariSilAsync(int id) { SonCariSil = id; return Task.CompletedTask; }
    public Task<IslemDto> IslemOlusturAsync(IslemYaz g) { SonIslemOlustur = g; return Task.FromResult(new IslemDto(0, g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.Not)); }
    public Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g) { SonIslemGuncelle = (id, g); return Task.FromResult(new IslemDto(id, g.Tarih, g.Cari, g.TutarTl, g.Kanal, g.Tip, g.Not)); }
    public Task IslemSilAsync(int id) { SonIslemSil = id; return Task.CompletedTask; }
    public Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g) { SonKartOlustur = g; return Task.FromResult(new KrediKartiDto(0, g.Ad, g.KesimTarihi, g.SonOdemeTarihi, g.Limit, g.Borc)); }
    public Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g) { SonKartGuncelle = (id, g); return Task.FromResult(new KrediKartiDto(id, g.Ad, g.KesimTarihi, g.SonOdemeTarihi, g.Limit, g.Borc)); }
    public Task KrediKartiSilAsync(int id) { SonKartSil = id; return Task.CompletedTask; }
    public Task<GelenDto> GelenKaydetAsync(GelenYaz g) { SonGelen = g; return Task.FromResult(new GelenDto(0, g.DonemStart, g.Kanal, g.TutarTl)); }
    public Task AyarGuncelleAsync(AyarYaz g) { SonAyar = g; return Task.CompletedTask; }
    public Task IzleyiciSifreAsync(string yeniSifre) { SonIzleyiciSifre = yeniSifre; return Task.CompletedTask; }
```

- [ ] **Step 4: Derleme + mevcut testlerin yeşil olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj 2>&1 | tail -8`
Expected: PASS — mevcut 24 test yeşil, derleme temiz.

- [ ] **Step 5: Commit**

```bash
cd <repo> && git add Kasa.ApiClient/IKasaApi.cs Kasa.App.Core.Tests/SahteApi.cs && git commit -m "feat(app): IKasaApi seam'ine editör mutasyonları + SahteApi çağrı kaydı"
```

---

## Task 2: TemelViewModel + okuma VM hata yüzeyi (nit c)

**Files:**
- Create: `Kasa.App.Core/TemelViewModel.cs`
- Modify: `Kasa.App.Core/PanelViewModel.cs`, `HaftalikViewModel.cs`, `AylikViewModel.cs`, `CarilerViewModel.cs`, `IslemlerViewModel.cs`, `KrediKartlariViewModel.cs`
- Modify: `Kasa.App.Core.Tests/SahteApi.cs`
- Test: `Kasa.App.Core.Tests/HataYuzeyiTests.cs`

- [ ] **Step 1: Başarısız testi yaz**

Create `Kasa.App.Core.Tests/HataYuzeyiTests.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class HataYuzeyiTests
{
    [Fact]
    public async Task Panel_ag_hatasinda_hata_yazar_ve_spinner_iner()
    {
        var api = new SahteApi { YuklemeHatasi = new KasaApiException("kopuk") };
        var vm = new PanelViewModel(api);

        await vm.YukleAsync();

        Assert.False(vm.Mesgul);          // spinner asılı kalmaz
        Assert.NotNull(vm.Hata);          // kullanıcıya hata gösterilir
    }

    [Fact]
    public async Task Cariler_basarili_yuklemede_hata_null()
    {
        var api = new SahteApi { CarilerListe = new List<CariDto> { new(1, "Ahmet", true) } };
        var vm = new CarilerViewModel(api);

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Single(vm.Cariler);
    }
}
```

- [ ] **Step 2: Testin başarısız (derlenmez) olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter HataYuzeyiTests 2>&1 | tail -12`
Expected: FAIL — `'PanelViewModel' does not contain a definition for 'Hata'` ve `SahteApi` `YuklemeHatasi` yok.

- [ ] **Step 3: TemelViewModel oluştur**

Create `Kasa.App.Core/TemelViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

/// <summary>Ortak Mesgul + Hata durumu ve güvenli çalıştırma sarmalayıcısı (nit c).</summary>
public partial class TemelViewModel : ObservableObject
{
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private string? _hata;

    /// <summary>İşlemi Mesgul/Hata sarmalayıcısında çalıştırır; istisnada spinner iner, Hata yazılır.</summary>
    protected async Task CalistirAsync(Func<Task> islem)
    {
        Hata = null;
        Mesgul = true;
        try { await islem(); }
        catch (Exception) { Hata = "İşlem başarısız. Bağlantıyı kontrol edin."; }
        finally { Mesgul = false; }
    }
}
```

- [ ] **Step 4: SahteApi'ye okuma-hata desteği ekle**

`Kasa.App.Core.Tests/SahteApi.cs` içinde `public PanelDto? Panel;` satırının üstüne ekle:

```csharp
    /// <summary>Ayarlanırsa tüm okuma metotları bu istisnayı fırlatır (hata yüzeyi testi).</summary>
    public Exception? YuklemeHatasi;
```

Ardından okuma metotlarını `YuklemeHatasi` kontrolü ile değiştir (mevcut satırların yerine):

```csharp
    public Task<PanelDto> PanelAsync() => YuklemeHatasi is not null ? Task.FromException<PanelDto>(YuklemeHatasi) : Task.FromResult(Panel!);
    public Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<HaftalikOzetDto>>(YuklemeHatasi) : Task.FromResult(HaftalikListe);
    public Task<AylikRaporDto> AylikAsync(int yil, int ay) { SonAylikYil = yil; SonAylikAy = ay; return YuklemeHatasi is not null ? Task.FromException<AylikRaporDto>(YuklemeHatasi) : Task.FromResult(AylikRapor!); }
    public Task<IReadOnlyList<DonemDto>> DonemlerAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<DonemDto>>(YuklemeHatasi) : Task.FromResult<IReadOnlyList<DonemDto>>(new List<DonemDto>());
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KanalDto>>(YuklemeHatasi) : Task.FromResult<IReadOnlyList<KanalDto>>(new List<KanalDto>());
    public Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null) => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<CariDto>>(YuklemeHatasi) : Task.FromResult(CarilerListe);
    public Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null) => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<IslemDto>>(YuklemeHatasi) : Task.FromResult(IslemlerListe);
    public Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KrediKartiDto>>(YuklemeHatasi) : Task.FromResult(KrediKartlariListe);
    public Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null) => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<GelenDto>>(YuklemeHatasi) : Task.FromResult<IReadOnlyList<GelenDto>>(new List<GelenDto>());
```

- [ ] **Step 5: 6 okuma VM'ini TemelViewModel'e taşı**

`Kasa.App.Core/PanelViewModel.cs` — tam içerik:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class PanelViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private decimal _guncelKasa;
    [ObservableProperty] private decimal _buHaftaSonucu;
    [ObservableProperty] private decimal _buAySonucu;
    public ObservableCollection<KanalBakiyeDto> Kanallar { get; } = new();

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var p = await _api.PanelAsync();
        GuncelKasa = p.GuncelKasa;
        BuHaftaSonucu = p.BuHaftaSonucu;
        BuAySonucu = p.BuAySonucu;
        Kanallar.Clear();
        foreach (var k in p.Kanallar) Kanallar.Add(k);
    });
}
```

`Kasa.App.Core/HaftalikViewModel.cs` — tam içerik:

```csharp
using System.Collections.ObjectModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class HaftalikViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public HaftalikViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<HaftalikOzetDto> Donemler { get; } = new();

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var liste = await _api.HaftalikAsync();
        Donemler.Clear();
        foreach (var d in liste) Donemler.Add(d);
    });
}
```

`Kasa.App.Core/AylikViewModel.cs` — tam içerik:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AylikViewModel : TemelViewModel
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
    [ObservableProperty] private AylikRaporDto? _rapor;

    public Task YukleAsync() => CalistirAsync(async () => Rapor = await _api.AylikAsync(Yil, Ay));
}
```

`Kasa.App.Core/CarilerViewModel.cs` — tam içerik (editör komutları Task 3'te eklenir):

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class CarilerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public CarilerViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private string? _ara;
    public ObservableCollection<CariDto> Cariler { get; } = new();

    private async Task DoldurAsync()
    {
        var liste = await _api.CarilerAsync(string.IsNullOrWhiteSpace(Ara) ? null : Ara);
        Cariler.Clear();
        foreach (var c in liste) Cariler.Add(c);
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);
}
```

`Kasa.App.Core/IslemlerViewModel.cs` — tam içerik (editör komutları Task 5'te eklenir):

```csharp
using System.Collections.ObjectModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class IslemlerViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public IslemlerViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<IslemDto> Islemler { get; } = new();

    private async Task DoldurAsync()
    {
        var liste = await _api.IslemlerAsync();
        Islemler.Clear();
        foreach (var i in liste) Islemler.Add(i);
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);
}
```

`Kasa.App.Core/KrediKartlariViewModel.cs` — tam içerik (editör komutları Task 4'te eklenir):

```csharp
using System.Collections.ObjectModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KrediKartlariViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public KrediKartlariViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<KrediKartiGorunum> Kartlar { get; } = new();

    private async Task DoldurAsync()
    {
        var liste = await _api.KrediKartlariAsync();
        Kartlar.Clear();
        foreach (var k in liste) Kartlar.Add(new KrediKartiGorunum(k));
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);
}
```

- [ ] **Step 6: Testlerin yeşil olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj 2>&1 | tail -8`
Expected: PASS — yeni 2 test + mevcut 24 test yeşil (26 toplam).

- [ ] **Step 7: Commit**

```bash
cd <repo> && git add Kasa.App.Core/ Kasa.App.Core.Tests/ && git commit -m "feat(app): TemelViewModel + okuma VM'lerinde hata yüzeyi (nit c)"
```

---

## Task 3: Cari editör komutları (ekle/düzenle)

**Files:**
- Modify: `Kasa.App.Core/CarilerViewModel.cs`
- Test: `Kasa.App.Core.Tests/CariEditorTests.cs`

Spec §6: Cariler = liste + ekle/düzenle (sil YOK).

- [ ] **Step 1: Başarısız testi yaz**

Create `Kasa.App.Core.Tests/CariEditorTests.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class CariEditorTests
{
    [Fact]
    public async Task Yeni_cari_kaydi_olustur_cagirir()
    {
        var api = new SahteApi();
        var vm = new CarilerViewModel(api) { DuzenAd = "Mehmet", DuzenAktif = true };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonCariOlustur);
        Assert.Equal("Mehmet", api.SonCariOlustur!.Ad);
        Assert.Null(api.SonCariGuncelle);
        Assert.Equal("", vm.DuzenAd);   // kayıttan sonra form temizlenir
    }

    [Fact]
    public async Task Mevcut_cari_guncelle_cagirir()
    {
        var api = new SahteApi();
        var vm = new CarilerViewModel(api);
        vm.Duzenle(new CariDto(7, "Ali", true));
        vm.DuzenAd = "Ali Veli";

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonCariGuncelle);
        Assert.Equal(7, api.SonCariGuncelle!.Value.Id);
        Assert.Equal("Ali Veli", api.SonCariGuncelle!.Value.G.Ad);
        Assert.Null(api.SonCariOlustur);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter CariEditorTests 2>&1 | tail -12`
Expected: FAIL — `KaydetCommand`, `DuzenAd`, `Duzenle` yok.

- [ ] **Step 3: CarilerViewModel'e editör üyelerini ekle**

`Kasa.App.Core/CarilerViewModel.cs` — `using CommunityToolkit.Mvvm.Input;` ekle ve editör üyelerini `YukleAsync` satırından sonra ekle:

```csharp
    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private int _duzenId;        // 0 = yeni
    [ObservableProperty] private string _duzenAd = "";
    [ObservableProperty] private bool _duzenAktif = true;

    [RelayCommand]
    private void Yeni() { DuzenId = 0; DuzenAd = ""; DuzenAktif = true; }

    [RelayCommand]
    public void Duzenle(CariDto c) { DuzenId = c.Id; DuzenAd = c.Ad; DuzenAktif = c.Aktif; }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var g = new CariYaz(DuzenAd, DuzenAktif);
        if (DuzenId == 0) await _api.CariOlusturAsync(g);
        else await _api.CariGuncelleAsync(DuzenId, g);
        Yeni();
        await DoldurAsync();
    });
```

Dosyanın başındaki using listesine ekle (yoksa): `using CommunityToolkit.Mvvm.Input;`

- [ ] **Step 4: Testlerin yeşil olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj 2>&1 | tail -8`
Expected: PASS — CariEditorTests 2 + önceki testler yeşil.

- [ ] **Step 5: Commit**

```bash
cd <repo> && git add Kasa.App.Core/CarilerViewModel.cs Kasa.App.Core.Tests/CariEditorTests.cs && git commit -m "feat(app): Cari editör komutları (ekle/düzenle)"
```

---

## Task 4: Kredi Kartı editör komutları (ekle/düzenle/sil)

**Files:**
- Modify: `Kasa.App.Core/KrediKartlariViewModel.cs`
- Test: `Kasa.App.Core.Tests/KrediKartiEditorTests.cs`

- [ ] **Step 1: Başarısız testi yaz**

Create `Kasa.App.Core.Tests/KrediKartiEditorTests.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class KrediKartiEditorTests
{
    [Fact]
    public async Task Yeni_kart_olustur_cagirir()
    {
        var api = new SahteApi();
        var vm = new KrediKartlariViewModel(api)
        {
            DuzenAd = "Bonus",
            DuzenKesim = new DateTime(2026, 7, 5),
            DuzenSonOdeme = new DateTime(2026, 7, 25),
            DuzenLimit = 100000m,
            DuzenBorc = 30000m,
        };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonKartOlustur);
        Assert.Equal("Bonus", api.SonKartOlustur!.Ad);
        Assert.Equal(new DateOnly(2026, 7, 5), api.SonKartOlustur!.KesimTarihi);
        Assert.Equal(100000m, api.SonKartOlustur!.Limit);
    }

    [Fact]
    public async Task Mevcut_kart_guncelle_cagirir()
    {
        var api = new SahteApi();
        var vm = new KrediKartlariViewModel(api);
        vm.Duzenle(new KrediKartiGorunum(new KrediKartiDto(3, "World", new DateOnly(2026,7,1), new DateOnly(2026,7,20), 50000m, 10000m)));
        vm.DuzenBorc = 12000m;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonKartGuncelle);
        Assert.Equal(3, api.SonKartGuncelle!.Value.Id);
        Assert.Equal(12000m, api.SonKartGuncelle!.Value.G.Borc);
    }

    [Fact]
    public async Task Sil_kart_silme_cagirir()
    {
        var api = new SahteApi();
        var vm = new KrediKartlariViewModel(api);
        var kart = new KrediKartiGorunum(new KrediKartiDto(9, "Maximum", new DateOnly(2026,7,1), new DateOnly(2026,7,20), 20000m, 0m));

        await vm.SilCommand.ExecuteAsync(kart);

        Assert.Equal(9, api.SonKartSil);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter KrediKartiEditorTests 2>&1 | tail -12`
Expected: FAIL — `KaydetCommand`, `SilCommand`, `DuzenAd` vb. yok.

- [ ] **Step 3: KrediKartlariViewModel'e editör üyelerini ekle**

`Kasa.App.Core/KrediKartlariViewModel.cs` — using'lere `using CommunityToolkit.Mvvm.ComponentModel;` ve `using CommunityToolkit.Mvvm.Input;` ekle; editör üyelerini `YukleAsync` satırından sonra ekle:

```csharp
    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private int _duzenId;          // 0 = yeni
    [ObservableProperty] private string _duzenAd = "";
    [ObservableProperty] private DateTime _duzenKesim = DateTime.Today;
    [ObservableProperty] private DateTime _duzenSonOdeme = DateTime.Today;
    [ObservableProperty] private decimal _duzenLimit;
    [ObservableProperty] private decimal _duzenBorc;

    [RelayCommand]
    private void Yeni()
    {
        DuzenId = 0; DuzenAd = ""; DuzenKesim = DateTime.Today;
        DuzenSonOdeme = DateTime.Today; DuzenLimit = 0; DuzenBorc = 0;
    }

    [RelayCommand]
    public void Duzenle(KrediKartiGorunum k)
    {
        DuzenId = k.Id; DuzenAd = k.Ad;
        DuzenKesim = k.KesimTarihi.ToDateTime(TimeOnly.MinValue);
        DuzenSonOdeme = k.SonOdemeTarihi.ToDateTime(TimeOnly.MinValue);
        DuzenLimit = k.Limit; DuzenBorc = k.Borc;
    }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var g = new KrediKartiYaz(DuzenAd, DateOnly.FromDateTime(DuzenKesim), DateOnly.FromDateTime(DuzenSonOdeme), DuzenLimit, DuzenBorc);
        if (DuzenId == 0) await _api.KrediKartiOlusturAsync(g);
        else await _api.KrediKartiGuncelleAsync(DuzenId, g);
        Yeni();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task SilAsync(KrediKartiGorunum k) => CalistirAsync(async () =>
    {
        await _api.KrediKartiSilAsync(k.Id);
        await DoldurAsync();
    });
```

- [ ] **Step 4: Testlerin yeşil olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj 2>&1 | tail -8`
Expected: PASS — KrediKartiEditorTests 3 + önceki testler yeşil.

- [ ] **Step 5: Commit**

```bash
cd <repo> && git add Kasa.App.Core/KrediKartlariViewModel.cs Kasa.App.Core.Tests/KrediKartiEditorTests.cs && git commit -m "feat(app): Kredi kartı editör komutları (ekle/düzenle/sil)"
```

---

## Task 5: İşlem editör komutları (ekle/düzenle/sil) + Gelen girişi

**Files:**
- Modify: `Kasa.App.Core/IslemlerViewModel.cs`
- Test: `Kasa.App.Core.Tests/IslemEditorTests.cs`

- [ ] **Step 1: Başarısız testi yaz**

Create `Kasa.App.Core.Tests/IslemEditorTests.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class IslemEditorTests
{
    [Fact]
    public async Task Yeni_islem_olustur_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api)
        {
            DuzenTarih = new DateTime(2026, 3, 5),
            DuzenCari = "MEZAT alış",
            DuzenTutar = 2500m,
            DuzenKanal = "MEZAT",
            DuzenTip = GiderTipi.Cari,
        };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemOlustur);
        Assert.Equal("MEZAT", api.SonIslemOlustur!.Kanal);
        Assert.Equal(2500m, api.SonIslemOlustur!.TutarTl);
        Assert.Equal(new DateOnly(2026, 3, 5), api.SonIslemOlustur!.Tarih);
    }

    [Fact]
    public async Task Mevcut_islem_guncelle_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);
        vm.Duzenle(new IslemDto(11, new DateOnly(2026,3,5), "K.K", 10000m, "MEZAT", GiderTipi.KrediKarti, null));
        vm.DuzenTutar = 12000m;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemGuncelle);
        Assert.Equal(11, api.SonIslemGuncelle!.Value.Id);
        Assert.Equal(12000m, api.SonIslemGuncelle!.Value.G.TutarTl);
    }

    [Fact]
    public async Task Sil_islem_silme_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);

        await vm.SilCommand.ExecuteAsync(new IslemDto(5, new DateOnly(2026,3,5), "x", 1m, "MEZAT", GiderTipi.Cari, null));

        Assert.Equal(5, api.SonIslemSil);
    }

    [Fact]
    public async Task Gelen_kaydet_gelen_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api)
        {
            GelenTarih = new DateTime(2026, 3, 2),
            GelenKanal = "PERAKENDE",
            GelenTutar = 5000m,
        };

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonGelen);
        Assert.Equal("PERAKENDE", api.SonGelen!.Kanal);
        Assert.Equal(5000m, api.SonGelen!.TutarTl);
        Assert.Equal(new DateOnly(2026, 3, 2), api.SonGelen!.DonemStart);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter IslemEditorTests 2>&1 | tail -12`
Expected: FAIL — `KaydetCommand`, `SilCommand`, `GelenKaydetCommand`, `Duzenle` vb. yok.

- [ ] **Step 3: IslemlerViewModel'e editör + Gelen üyelerini ekle**

`Kasa.App.Core/IslemlerViewModel.cs` — using'lere `using CommunityToolkit.Mvvm.ComponentModel;` ve `using CommunityToolkit.Mvvm.Input;` ekle; üyeleri `YukleAsync` satırından sonra ekle:

```csharp
    [ObservableProperty] private bool _editorMu;

    // İşlem düzenleme
    [ObservableProperty] private int _duzenId;          // 0 = yeni
    [ObservableProperty] private DateTime _duzenTarih = DateTime.Today;
    [ObservableProperty] private string _duzenCari = "";
    [ObservableProperty] private decimal _duzenTutar;
    [ObservableProperty] private string _duzenKanal = "";
    [ObservableProperty] private GiderTipi _duzenTip = GiderTipi.Cari;
    [ObservableProperty] private string? _duzenNot;

    // Gelen girişi
    [ObservableProperty] private DateTime _gelenTarih = DateTime.Today;
    [ObservableProperty] private string _gelenKanal = "";
    [ObservableProperty] private decimal _gelenTutar;

    [RelayCommand]
    private void Yeni()
    {
        DuzenId = 0; DuzenTarih = DateTime.Today; DuzenCari = "";
        DuzenTutar = 0; DuzenKanal = ""; DuzenTip = GiderTipi.Cari; DuzenNot = null;
    }

    [RelayCommand]
    public void Duzenle(IslemDto i)
    {
        DuzenId = i.Id; DuzenTarih = i.Tarih.ToDateTime(TimeOnly.MinValue);
        DuzenCari = i.Cari; DuzenTutar = i.TutarTl; DuzenKanal = i.Kanal;
        DuzenTip = i.Tip; DuzenNot = i.Not;
    }

    [RelayCommand]
    private Task KaydetAsync() => CalistirAsync(async () =>
    {
        var g = new IslemYaz(DateOnly.FromDateTime(DuzenTarih), DuzenCari, DuzenTutar, DuzenKanal, DuzenTip, DuzenNot);
        if (DuzenId == 0) await _api.IslemOlusturAsync(g);
        else await _api.IslemGuncelleAsync(DuzenId, g);
        Yeni();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task SilAsync(IslemDto i) => CalistirAsync(async () =>
    {
        await _api.IslemSilAsync(i.Id);
        await DoldurAsync();
    });

    [RelayCommand]
    private Task GelenKaydetAsync() => CalistirAsync(async () =>
    {
        await _api.GelenKaydetAsync(new GelenYaz(DateOnly.FromDateTime(GelenTarih), GelenKanal, GelenTutar));
        GelenKanal = ""; GelenTutar = 0;
    });
```

- [ ] **Step 4: Testlerin yeşil olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj 2>&1 | tail -8`
Expected: PASS — IslemEditorTests 4 + önceki testler yeşil.

- [ ] **Step 5: Commit**

```bash
cd <repo> && git add Kasa.App.Core/IslemlerViewModel.cs Kasa.App.Core.Tests/IslemEditorTests.cs && git commit -m "feat(app): İşlem editör komutları (ekle/düzenle/sil) + Gelen girişi"
```

---

## Task 6: AyarlarViewModel (Kanal CRUD + izleyici şifre + ayar)

**Files:**
- Create: `Kasa.App.Core/AyarlarViewModel.cs`
- Modify: `Kasa.App.Core.Tests/SahteApi.cs`
- Test: `Kasa.App.Core.Tests/AyarlarViewModelTests.cs`

- [ ] **Step 1: Başarısız testi yaz**

Create `Kasa.App.Core.Tests/AyarlarViewModelTests.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class AyarlarViewModelTests
{
    [Fact]
    public async Task Yukle_ayar_ve_kanallari_doldurur()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 15000m, true),
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 0m) },
        };
        var vm = new AyarlarViewModel(api);

        await vm.YukleAsync();

        Assert.Equal(15000m, vm.KasaAcilisDevri);
        Assert.Single(vm.Kanallar);
    }

    [Fact]
    public async Task Yeni_kanal_olustur_cagirir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026,1,1), 0m, false) };
        var vm = new AyarlarViewModel(api) { DuzenKanalAd = "TOPTAN", DuzenKanalSira = 3 };

        await vm.KanalKaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonKanalOlustur);
        Assert.Equal("TOPTAN", api.SonKanalOlustur!.Ad);
        Assert.Equal(3, api.SonKanalOlustur!.Sira);
    }

    [Fact]
    public async Task Kanal_sil_cagirir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026,1,1), 0m, false) };
        var vm = new AyarlarViewModel(api);

        await vm.KanalSilCommand.ExecuteAsync(new KanalDto(4, "PERAKENDE", true, 1, 0m));

        Assert.Equal(4, api.SonKanalSil);
    }

    [Fact]
    public async Task Izleyici_sifre_kaydet_cagirir()
    {
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api) { YeniIzleyiciSifre = "gizli123" };

        await vm.IzleyiciSifreKaydetCommand.ExecuteAsync(null);

        Assert.Equal("gizli123", api.SonIzleyiciSifre);
        Assert.Equal("", vm.YeniIzleyiciSifre);
    }

    [Fact]
    public async Task Ayar_kaydet_cagirir()
    {
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api)
        {
            TakipBaslangic = new DateTime(2026, 2, 1),
            KasaAcilisDevri = 20000m,
        };

        await vm.AyarKaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonAyar);
        Assert.Equal(new DateOnly(2026, 2, 1), api.SonAyar!.TakipBaslangic);
        Assert.Equal(20000m, api.SonAyar!.KasaAcilisDevri);
    }
}
```

- [ ] **Step 2: Testin başarısız olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj --filter AyarlarViewModelTests 2>&1 | tail -12`
Expected: FAIL — `AyarlarViewModel` yok, `SahteApi.AyarlarSonuc`/`KanallarListe` yok.

- [ ] **Step 3: SahteApi'de AyarlarAsync + KanallarAsync'i canned yap**

`Kasa.App.Core.Tests/SahteApi.cs` — `YuklemeHatasi` alanının altına ekle:

```csharp
    public AyarlarDto? AyarlarSonuc;
    public IReadOnlyList<KanalDto> KanallarListe = new List<KanalDto>();
```

`KanallarAsync` metodunu değiştir (canned listeyi döndürsün):

```csharp
    public Task<IReadOnlyList<KanalDto>> KanallarAsync() => YuklemeHatasi is not null ? Task.FromException<IReadOnlyList<KanalDto>>(YuklemeHatasi) : Task.FromResult(KanallarListe);
```

`AyarlarAsync` metodunu değiştir (artık NotImplementedException fırlatmasın):

```csharp
    public Task<AyarlarDto> AyarlarAsync() => YuklemeHatasi is not null ? Task.FromException<AyarlarDto>(YuklemeHatasi) : Task.FromResult(AyarlarSonuc!);
```

- [ ] **Step 4: AyarlarViewModel oluştur**

Create `Kasa.App.Core/AyarlarViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Editör ayarları: kanal CRUD + izleyici şifre + takip başlangıç/açılış devri (spec §6).</summary>
public partial class AyarlarViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    public AyarlarViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<KanalDto> Kanallar { get; } = new();

    [ObservableProperty] private DateTime _takipBaslangic = DateTime.Today;
    [ObservableProperty] private decimal _kasaAcilisDevri;

    // Kanal düzenleme
    [ObservableProperty] private int _duzenKanalId;      // 0 = yeni
    [ObservableProperty] private string _duzenKanalAd = "";
    [ObservableProperty] private bool _duzenKanalAktif = true;
    [ObservableProperty] private int _duzenKanalSira;
    [ObservableProperty] private decimal _duzenKanalAcilisDevri;

    // İzleyici şifre
    [ObservableProperty] private string _yeniIzleyiciSifre = "";

    private async Task DoldurAsync()
    {
        var ayar = await _api.AyarlarAsync();
        TakipBaslangic = ayar.TakipBaslangic.ToDateTime(TimeOnly.MinValue);
        KasaAcilisDevri = ayar.KasaAcilisDevri;
        var kanallar = await _api.KanallarAsync();
        Kanallar.Clear();
        foreach (var k in kanallar) Kanallar.Add(k);
    }

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    [RelayCommand]
    private void YeniKanal()
    {
        DuzenKanalId = 0; DuzenKanalAd = ""; DuzenKanalAktif = true;
        DuzenKanalSira = 0; DuzenKanalAcilisDevri = 0;
    }

    [RelayCommand]
    public void KanalDuzenle(KanalDto k)
    {
        DuzenKanalId = k.Id; DuzenKanalAd = k.Ad; DuzenKanalAktif = k.Aktif;
        DuzenKanalSira = k.Sira; DuzenKanalAcilisDevri = k.AcilisDevri;
    }

    [RelayCommand]
    private Task KanalKaydetAsync() => CalistirAsync(async () =>
    {
        var g = new KanalYaz(DuzenKanalAd, DuzenKanalAktif, DuzenKanalSira, DuzenKanalAcilisDevri);
        if (DuzenKanalId == 0) await _api.KanalOlusturAsync(g);
        else await _api.KanalGuncelleAsync(DuzenKanalId, g);
        YeniKanal();
        await DoldurAsync();
    });

    [RelayCommand]
    private Task KanalSilAsync(KanalDto k) => CalistirAsync(async () =>
    {
        await _api.KanalSilAsync(k.Id);
        await DoldurAsync();
    });

    [RelayCommand]
    private Task AyarKaydetAsync() => CalistirAsync(async () =>
        await _api.AyarGuncelleAsync(new AyarYaz(DateOnly.FromDateTime(TakipBaslangic), KasaAcilisDevri)));

    [RelayCommand]
    private Task IzleyiciSifreKaydetAsync() => CalistirAsync(async () =>
    {
        await _api.IzleyiciSifreAsync(YeniIzleyiciSifre);
        YeniIzleyiciSifre = "";
    });
}
```

- [ ] **Step 5: Testlerin yeşil olduğunu doğrula**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj 2>&1 | tail -8`
Expected: PASS — AyarlarViewModelTests 5 + önceki testler yeşil.

- [ ] **Step 6: Commit**

```bash
cd <repo> && git add Kasa.App.Core/AyarlarViewModel.cs Kasa.App.Core.Tests/ && git commit -m "feat(app): AyarlarViewModel — kanal CRUD + izleyici şifre + ayar"
```

---

## Task 7: MAUI head — rol-nav (a) + çıkış (b) + editör formları + DI

**Files:**
- Modify: `Kasa.App/AppShell.xaml`, `AppShell.xaml.cs`
- Modify: `Kasa.App/Views/CarilerPage.xaml`, `CarilerPage.xaml.cs`
- Modify: `Kasa.App/Views/KrediKartlariPage.xaml`, `KrediKartlariPage.xaml.cs`
- Modify: `Kasa.App/Views/IslemlerPage.xaml`, `IslemlerPage.xaml.cs`
- Create: `Kasa.App/Views/AyarlarPage.xaml`, `AyarlarPage.xaml.cs`
- Modify: `Kasa.App/MauiProgram.cs`

**Not:** MAUI head build-only; headless test edilemez. Bu görevin doğrulaması `dotnet build`. MAUI workload gerekiyorsa `dotnet workload install maui` dene; kurulamıyorsa DURDUR ve rapor et (Task 1-6 zaten değerli ve merge edilebilir).

- [ ] **Step 1: AppShell.xaml — Ayarlar sekmesi + Çıkış menüsü ekle**

`Kasa.App/AppShell.xaml` — tam içerik:

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
        <ShellContent x:Name="AyarlarSekme" Title="Ayarlar" Route="ayarlar" ContentTemplate="{DataTemplate v:AyarlarPage}" />
    </FlyoutItem>
    <MenuItem x:Name="CikisMenu" Text="Çıkış" IsVisible="False" Clicked="CikisTiklandi" />
</Shell>
```

- [ ] **Step 2: AppShell.xaml.cs — rol-nav (a) + çıkış (b)**

`Kasa.App/AppShell.xaml.cs` — tam içerik:

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
        // nit (a): rol-nav SekmeModeli üzerinden — Ayarlar yalnız editörde
        var bolumler = SekmeModeli.Bolumler(_auth.AktifRol);
        AyarlarSekme.IsVisible = bolumler.Contains(Bolum.Ayarlar);
        CikisMenu.IsVisible = true;                 // nit (b)
        _ = GoToAsync("//panel");
    }

    private async void CikisTiklandi(object? sender, EventArgs e)   // nit (b)
    {
        await _auth.CikisAsync();
        AnaMenu.IsVisible = false;
        CikisMenu.IsVisible = false;
        await GoToAsync("//login");
    }
}
```

- [ ] **Step 3: LoginPage giriş sonrası MenuyuAc çağrısını doğrula**

`Kasa.App/Views/LoginPage.xaml.cs` dosyasını oku; giriş başarılıysa `((AppShell)Shell.Current).MenuyuAc()` çağrısı mevcut olmalı (Plan 3'ten). Yoksa `OnAppearing`/buton sonrası ekle. Değişiklik gerekmiyorsa bu adımı atla.

- [ ] **Step 4: CarilerPage — editör formu**

`Kasa.App/Views/CarilerPage.xaml` — tam içerik:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Name="Sayfa"
             x:Class="Kasa.App.Views.CarilerPage" Title="Cariler">
    <Grid RowDefinitions="Auto,Auto,*" Padding="12" RowSpacing="8">
        <VerticalStackLayout Grid.Row="0" Spacing="6" IsVisible="{Binding EditorMu}">
            <Entry Placeholder="Cari adı" Text="{Binding DuzenAd}" />
            <HorizontalStackLayout Spacing="8">
                <Switch IsToggled="{Binding DuzenAktif}" />
                <Label Text="Aktif" VerticalOptions="Center" />
            </HorizontalStackLayout>
            <HorizontalStackLayout Spacing="8">
                <Button Text="Kaydet" Command="{Binding KaydetCommand}" />
                <Button Text="Yeni" Command="{Binding YeniCommand}" />
            </HorizontalStackLayout>
        </VerticalStackLayout>
        <Label Grid.Row="1" Text="{Binding Hata}" TextColor="{StaticResource Neg}" IsVisible="{Binding Hata, Converter={StaticResource DoluIse}}" />
        <CollectionView Grid.Row="2" ItemsSource="{Binding Cariler}">
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid Padding="8" ColumnDefinitions="*,Auto">
                        <Label Text="{Binding Ad}" VerticalOptions="Center" />
                        <Button Grid.Column="1" Text="Düzenle"
                                Command="{Binding BindingContext.DuzenleCommand, Source={x:Reference Sayfa}}"
                                CommandParameter="{Binding .}" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
```

**Not:** `DoluIse` converter'ı Adım 8'de eklenir (null/boş değilse görünür). Önce onu ekleyip sonra sayfaları yazmak da olur; burada sıralama önemli değil, tümü Adım 9'da birlikte derlenir.

- [ ] **Step 5: CarilerPage.xaml.cs — EditorMu ayarla**

`Kasa.App/Views/CarilerPage.xaml.cs` — tam içerik:

```csharp
using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class CarilerPage : ContentPage
{
    private readonly CarilerViewModel _vm;
    private readonly AuthViewModel _auth;

    public CarilerPage(CarilerViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }
}
```

- [ ] **Step 6: KrediKartlariPage — editör formu + Düzenle/Sil**

`Kasa.App/Views/KrediKartlariPage.xaml` — tam içerik:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Name="Sayfa"
             x:Class="Kasa.App.Views.KrediKartlariPage" Title="Kredi Kartları">
    <Grid RowDefinitions="Auto,Auto,*" Padding="12" RowSpacing="8">
        <VerticalStackLayout Grid.Row="0" Spacing="6" IsVisible="{Binding EditorMu}">
            <Entry Placeholder="Kart adı" Text="{Binding DuzenAd}" />
            <Label Text="Kesim tarihi" TextColor="{StaticResource Sub}" />
            <DatePicker Date="{Binding DuzenKesim}" />
            <Label Text="Son ödeme tarihi" TextColor="{StaticResource Sub}" />
            <DatePicker Date="{Binding DuzenSonOdeme}" />
            <Entry Placeholder="Limit" Keyboard="Numeric" Text="{Binding DuzenLimit}" />
            <Entry Placeholder="Borç" Keyboard="Numeric" Text="{Binding DuzenBorc}" />
            <HorizontalStackLayout Spacing="8">
                <Button Text="Kaydet" Command="{Binding KaydetCommand}" />
                <Button Text="Yeni" Command="{Binding YeniCommand}" />
            </HorizontalStackLayout>
        </VerticalStackLayout>
        <Label Grid.Row="1" Text="{Binding Hata}" TextColor="{StaticResource Neg}" IsVisible="{Binding Hata, Converter={StaticResource DoluIse}}" />
        <CollectionView Grid.Row="2" ItemsSource="{Binding Kartlar}">
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Border Style="{StaticResource Kart}" Margin="0,4">
                        <Grid ColumnDefinitions="*,Auto" RowDefinitions="Auto,Auto,Auto,Auto">
                            <Label Text="{Binding Ad}" FontSize="16" />
                            <Label Grid.Row="1" Text="Limit" TextColor="{StaticResource Sub}" />
                            <Label Grid.Row="1" Grid.Column="1" Style="{StaticResource Para}" Text="{Binding Limit, Converter={StaticResource ParaBicim}}" />
                            <Label Grid.Row="2" Text="Borç" TextColor="{StaticResource Sub}" />
                            <Label Grid.Row="2" Grid.Column="1" Style="{StaticResource Para}" Text="{Binding Borc, Converter={StaticResource ParaBicim}}" />
                            <Label Grid.Row="3" Text="Kalan" TextColor="{StaticResource Sub}" />
                            <Label Grid.Row="3" Grid.Column="1" Style="{StaticResource Para}" Text="{Binding KalanLimit, Converter={StaticResource ParaBicim}}" />
                            <HorizontalStackLayout Grid.Column="1" HorizontalOptions="End" Spacing="6"
                                                   IsVisible="{Binding BindingContext.EditorMu, Source={x:Reference Sayfa}}">
                                <Button Text="Düzenle"
                                        Command="{Binding BindingContext.DuzenleCommand, Source={x:Reference Sayfa}}"
                                        CommandParameter="{Binding .}" />
                                <Button Text="Sil"
                                        Command="{Binding BindingContext.SilCommand, Source={x:Reference Sayfa}}"
                                        CommandParameter="{Binding .}" />
                            </HorizontalStackLayout>
                        </Grid>
                    </Border>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
```

`Kasa.App/Views/KrediKartlariPage.xaml.cs` — tam içerik:

```csharp
using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class KrediKartlariPage : ContentPage
{
    private readonly KrediKartlariViewModel _vm;
    private readonly AuthViewModel _auth;

    public KrediKartlariPage(KrediKartlariViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }
}
```

- [ ] **Step 7: IslemlerPage — editör formu + Gelen + Düzenle/Sil**

`Kasa.App/Views/IslemlerPage.xaml` — tam içerik:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Name="Sayfa"
             x:Class="Kasa.App.Views.IslemlerPage" Title="İşlemler">
    <Grid RowDefinitions="Auto,Auto,*" Padding="12" RowSpacing="8">
        <VerticalStackLayout Grid.Row="0" Spacing="6" IsVisible="{Binding EditorMu}">
            <Label Text="İşlem" FontAttributes="Bold" />
            <DatePicker Date="{Binding DuzenTarih}" />
            <Entry Placeholder="Cari / açıklama" Text="{Binding DuzenCari}" />
            <Entry Placeholder="Tutar" Keyboard="Numeric" Text="{Binding DuzenTutar}" />
            <Entry Placeholder="Kanal" Text="{Binding DuzenKanal}" />
            <Entry Placeholder="Not" Text="{Binding DuzenNot}" />
            <HorizontalStackLayout Spacing="8">
                <Button Text="Kaydet" Command="{Binding KaydetCommand}" />
                <Button Text="Yeni" Command="{Binding YeniCommand}" />
            </HorizontalStackLayout>
            <Label Text="Gelen (kanal geliri)" FontAttributes="Bold" Margin="0,8,0,0" />
            <DatePicker Date="{Binding GelenTarih}" />
            <Entry Placeholder="Kanal" Text="{Binding GelenKanal}" />
            <Entry Placeholder="Tutar" Keyboard="Numeric" Text="{Binding GelenTutar}" />
            <Button Text="Gelen kaydet" Command="{Binding GelenKaydetCommand}" />
        </VerticalStackLayout>
        <Label Grid.Row="1" Text="{Binding Hata}" TextColor="{StaticResource Neg}" IsVisible="{Binding Hata, Converter={StaticResource DoluIse}}" />
        <CollectionView Grid.Row="2" ItemsSource="{Binding Islemler}">
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid Padding="8" ColumnDefinitions="*,Auto,Auto" RowDefinitions="Auto,Auto">
                        <Label Text="{Binding Cari}" />
                        <Label Grid.Row="1" Text="{Binding Kanal}" TextColor="{StaticResource Sub}" FontSize="12" />
                        <Label Grid.Column="1" Style="{StaticResource Para}" Text="{Binding TutarTl, Converter={StaticResource ParaBicim}}" />
                        <HorizontalStackLayout Grid.Column="2" Grid.RowSpan="2" Spacing="6"
                                               IsVisible="{Binding BindingContext.EditorMu, Source={x:Reference Sayfa}}">
                            <Button Text="Düzenle"
                                    Command="{Binding BindingContext.DuzenleCommand, Source={x:Reference Sayfa}}"
                                    CommandParameter="{Binding .}" />
                            <Button Text="Sil"
                                    Command="{Binding BindingContext.SilCommand, Source={x:Reference Sayfa}}"
                                    CommandParameter="{Binding .}" />
                        </HorizontalStackLayout>
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
```

`Kasa.App/Views/IslemlerPage.xaml.cs` — tam içerik:

```csharp
using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class IslemlerPage : ContentPage
{
    private readonly IslemlerViewModel _vm;
    private readonly AuthViewModel _auth;

    public IslemlerPage(IslemlerViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }
}
```

- [ ] **Step 8: DoluIse converter + AyarlarPage oluştur**

Create `Kasa.App/Converters/DoluIseConverter.cs`:

```csharp
using System.Globalization;

namespace Kasa.App.Converters;

/// <summary>string null/boş değilse true (hata etiketini yalnız doluyken göster).</summary>
public sealed class DoluIseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

`Kasa.App/App.xaml` — `<conv:ParaBicimConverter x:Key="ParaBicim" />` satırının altına ekle:

```xml
            <conv:DoluIseConverter x:Key="DoluIse" />
```

Create `Kasa.App/Views/AyarlarPage.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Name="Sayfa"
             x:Class="Kasa.App.Views.AyarlarPage" Title="Ayarlar">
    <ScrollView>
        <VerticalStackLayout Padding="12" Spacing="10">
            <Label Text="Genel ayar" FontAttributes="Bold" />
            <Label Text="Takip başlangıç" TextColor="{StaticResource Sub}" />
            <DatePicker Date="{Binding TakipBaslangic}" />
            <Entry Placeholder="Kasa açılış devri" Keyboard="Numeric" Text="{Binding KasaAcilisDevri}" />
            <Button Text="Ayarı kaydet" Command="{Binding AyarKaydetCommand}" />

            <Label Text="İzleyici şifresi" FontAttributes="Bold" Margin="0,8,0,0" />
            <Entry Placeholder="Yeni izleyici şifresi" IsPassword="True" Text="{Binding YeniIzleyiciSifre}" />
            <Button Text="Şifreyi kaydet" Command="{Binding IzleyiciSifreKaydetCommand}" />

            <Label Text="Kanallar" FontAttributes="Bold" Margin="0,8,0,0" />
            <Entry Placeholder="Kanal adı" Text="{Binding DuzenKanalAd}" />
            <Entry Placeholder="Sıra" Keyboard="Numeric" Text="{Binding DuzenKanalSira}" />
            <Entry Placeholder="Açılış devri" Keyboard="Numeric" Text="{Binding DuzenKanalAcilisDevri}" />
            <HorizontalStackLayout Spacing="8">
                <Switch IsToggled="{Binding DuzenKanalAktif}" />
                <Label Text="Aktif" VerticalOptions="Center" />
            </HorizontalStackLayout>
            <HorizontalStackLayout Spacing="8">
                <Button Text="Kanal kaydet" Command="{Binding KanalKaydetCommand}" />
                <Button Text="Yeni kanal" Command="{Binding YeniKanalCommand}" />
            </HorizontalStackLayout>

            <Label Text="{Binding Hata}" TextColor="{StaticResource Neg}" IsVisible="{Binding Hata, Converter={StaticResource DoluIse}}" />

            <CollectionView ItemsSource="{Binding Kanallar}">
                <CollectionView.ItemTemplate>
                    <DataTemplate>
                        <Grid Padding="6" ColumnDefinitions="*,Auto,Auto">
                            <Label Text="{Binding Ad}" VerticalOptions="Center" />
                            <Button Grid.Column="1" Text="Düzenle"
                                    Command="{Binding BindingContext.KanalDuzenleCommand, Source={x:Reference Sayfa}}"
                                    CommandParameter="{Binding .}" />
                            <Button Grid.Column="2" Text="Sil"
                                    Command="{Binding BindingContext.KanalSilCommand, Source={x:Reference Sayfa}}"
                                    CommandParameter="{Binding .}" />
                        </Grid>
                    </DataTemplate>
                </CollectionView.ItemTemplate>
            </CollectionView>
        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

Create `Kasa.App/Views/AyarlarPage.xaml.cs`:

```csharp
using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class AyarlarPage : ContentPage
{
    private readonly AyarlarViewModel _vm;

    public AyarlarPage(AyarlarViewModel vm)
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

- [ ] **Step 9: MauiProgram.cs — AyarlarViewModel + AyarlarPage DI**

`Kasa.App/MauiProgram.cs` — `builder.Services.AddTransient<KrediKartlariViewModel>();` satırının altına ekle:

```csharp
        builder.Services.AddTransient<AyarlarViewModel>();
```

`builder.Services.AddTransient<Views.KrediKartlariPage>();` satırının altına ekle:

```csharp
        builder.Services.AddTransient<Views.AyarlarPage>();
```

- [ ] **Step 10: MAUI workload'u kontrol et, derle**

Run: `cd <repo> && dotnet build Kasa.App/Kasa.App.csproj 2>&1 | tail -20`
Expected: PASS — 0 hata. Eğer "workload maui not installed" gibi bir hata olursa: `dotnet workload install maui` çalıştır, sonra derlemeyi tekrarla. Workload kurulamıyorsa DURDUR ve durumu rapor et.

- [ ] **Step 11: Commit**

```bash
cd <repo> && git add Kasa.App/ && git commit -m "feat(app): editör formları + rol-nav (a) + çıkış (b) + Ayarlar sayfası"
```

---

## Task 8: Tam çözüm doğrulama

**Files:** (yok — yalnız doğrulama)

- [ ] **Step 1: Tüm App.Core testleri**

Run: `cd <repo> && dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj 2>&1 | tail -6`
Expected: PASS — tüm testler yeşil (24 mevcut + ~16 yeni ≈ 40).

- [ ] **Step 2: ApiClient + Core + Api testleri bozulmamış**

Run: `cd <repo> && dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj 2>&1 | tail -4`
Expected: PASS — ApiClient 22 test yeşil (seam genişlemesi bozmadı).

- [ ] **Step 3: MAUI head derleme (workload varsa)**

Run: `cd <repo> && dotnet build Kasa.App/Kasa.App.csproj 2>&1 | tail -6`
Expected: PASS — 0 hata. (Workload yoksa Task 7'de raporlanmıştır.)

---

## Self-review notları (plan yazarı)

**Spec kapsamı (§6 tablosu):** Cariler ekle/düzenle ✅(T3), İşlemler ekle/düzenle/sil+Gelen ✅(T5), Kredi Kartları ekle/düzenle/sil ✅(T4), Ayarlar izleyici-şifre+kanallar ✅(T6), rol-nav ✅(T7 nit a), çıkış ✅(T7 nit b), okuma hata yüzeyi ✅(T2 nit c). Sil kapsamı: Cari'de sil YOK (spec ekle/düzenle diyor) — bilinçli.

**Tip tutarlılığı:** `KaydetCommand`/`SilCommand`/`DuzenleCommand`/`YeniCommand` isimleri `[RelayCommand]` üretimiyle uyumlu ("Async" atılır). `EditorMu` bool, sayfada AuthViewModel'den set edilir. `DateTime`↔`DateOnly` dönüşümü VM sınırında (`DateOnly.FromDateTime`/`ToDateTime(TimeOnly.MinValue)`). `SahteApi` mutasyon kayıt tuple'ları `(int Id, T G)` adlandırılmış — testlerde `.Value.Id`/`.Value.G` ile erişilir.

**Placeholder taraması:** yok — tüm adımlar tam kod içerir.
