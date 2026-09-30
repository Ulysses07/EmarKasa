# Masaüstü gruplu menü ve kredi kartı kutuları — uygulama planı

> **Ajanlar için:** Bu plan görev görev uygulanır (superpowers:subagent-driven-development). Adımlar `- [ ]` onay kutusudur.

**Amaç:** Masaüstü uygulamasında (Kasa.App) sol menüyü dört gruba ayırmak ve Kartlar ekranını banka rengindeki kart kutularına, kutunun altında açılan ayrıntıya ve tek form alanına dönüştürmek (tasarım: `docs/specs/2026-09-30-masaustu-menu-ve-kartlar.md`).

**Mimari:** Menünün içeriği ve rol kuralı `Kasa.App.Core`'daki yeni `MenuModeli`'nden gelir; `AppShell.xaml` onu `Shell.FlyoutContent` içinde gruplu bir liste olarak çizer, `FlyoutItem`'lar yalnız rota kaynağı ve erişim kuralı olarak kalır. Kartlar ekranında kutu içeriği (`KartTakipSatiri`), renk (`KartRengi`) ve yerleşim hesabı (`KartIzgarasiHesabi`) App.Core'da saf kod olarak sınanır; `Kasa.App/Controls` altındaki `KartKutusu`, `YeniKartKutusu` ve `KartIzgarasi` (özel `Layout`) yalnız bu hesapları kullanır. Açık form ve seçili sekme `KartTakipViewModel`'in yeni arayüz durumudur; hesap, doğrulama ve sunucu mantığı değişmez.

**Teknoloji:** .NET 10, MAUI 10.0.110 (Windows), CommunityToolkit.Mvvm, xUnit v3

---

## Çalışma kuralları (her görevde geçerli)

- Worktree: `C:/Users/burak/source/repos/Kasa-paket/menu-kartlar`, dal `ozellik/menu-kartlar`. Bütün komutlar bu dizinde çalışır.
- Makine 16 GB: aynı anda tek `dotnet` komutu. Her `dotnet` komutunda `-m:2 -nodeReuse:false`.
- Test komutu: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~X"`. Başarı: çıktıda `Passed!` ve `Failed: 0`. Derleme hatası beklenen adımlarda çıktı `error CS…` satırıyla biter.
- Windows derlemesi: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`. Beklenen: `0 Warning(s)` ve `0 Error(s)`.
- `bash .github/scripts/maui-lint.sh` → son satır `maui-lint: taban içinde.` (ya da azalma uyarısı; o zaman `--tabani-guncelle`).
- `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes` → çıktı yok, çıkış kodu 0. Fark çıkarsa `--verify-no-changes`'sız bir kez (gerekirse iki kez) çalıştırıp yeniden denetle.
- xUnit v3: `CancellationToken` alan bir API çağrılırsa `TestContext.Current.CancellationToken` verilir (xUnit1051). Bu plandaki testlerin çağırdığı model yöntemleri token almaz.
- Kod stili: Türkçe adlar, mevcut yorum yoğunluğu, Allman ayraçları, 4 boşluk; satırlar 200 karakteri geçmez (maui-lint (c)); `Kasa.App` altında Resources/ dışında onaltılık renk yazılmaz (maui-lint (b)); yazı ailesi yalnız `{StaticResource YaziAilesi}` / `{StaticResource SimgeAilesi}` ile yazılır.
- Commit mesajı: `feat(app): …` / `test(app): …` Türkçe, sonunda boş satır + `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Push yok.

## Verilmiş teknik kararlar

1. **Simge kaynağı: Windows sistem simge yazı tipi, paket yok.** Uygulamada yazı tipi ya da görsel simge kaynağı yok (`Resources/Fonts`, `Resources/Images` yok; `MauiKayitTutarliligiTests.Yazi_ailesi_tek_anahtardan_gelir…` paketlenmiş font ve `AddFont` kaydını yasaklıyor). Uygulama yalnız Windows ve en düşük sürümü 10.0.17763 (Windows 10). Microsoft Learn'e göre (30.09.2026'da doğrulandı) *Segoe Fluent Icons* Windows 11'le gelir, Windows 10'da yoktur ve "başka platforma gönderilemez"; *Segoe MDL2 Assets* Windows 10 ve sonrasında hazırdır. Bu yüzden `YaziAilesi` ile aynı desenle virgüllü yedek listesi kullanılır: `SimgeAilesi = "Segoe Fluent Icons, Segoe MDL2 Assets"`. Seçilen 13 kod noktası (E80F Home, E8C0 CalendarWeek, E787 Calendar, E8FD BulletedList, E7BF ShoppingCart, E8EE RepeatAll, E8B5 Import, E8C7 PaymentCard, E825 Bank, EA8F Ringer, EDE1 Export, E713 Settings, F3B1 SignOut) iki yazı tipinin Learn sayfalarında da aynı adla listelendi. Simge `Label` metni olarak çizilir (FontImageSource değil): renk ve boyut stil anahtarlarıyla gelir, Win2D yazı tipi yedeği riski yoktur.
2. **Menü öğesi = şeffaf düğme + üstünde girdiyi geçiren yazılar.** Eski Shell öğeleri klavyeyle gezilebilir ve UI Otomasyonu'nda seçilebilirdi; yalnız `TapGestureRecognizer`'lı bir `Grid` ikisini de kaybettirir. Her öğenin altında `SeffafDugme` stilli bir `Button` durur (Tab ile odaklanır, Enter ile çalışır, UIA'da `SemanticProperties.Description` = öğe başlığıyla `Button` olarak görünür). Zemini düğme değil kök `Grid` çizer: `MenuOgesi.Secili` → `SidebarActive`, `MenuOgesi.UzerindeVurgu` (= üzerinde ve seçili değil) → `SidebarHover`. İki koşul aynı anda doğru olmadığı için iki `DataTrigger` çakışmaz; üzerine gelme `PointerGestureRecognizer` komutlarıyla modele yazılır. MAUI'nin Windows düğmesi `BackgroundColor`'ı `ButtonBackgroundPointerOver/Pressed` kaynaklarına da yazdığı için (dotnet/maui `ButtonExtensions.UpdateBackground`, doğrulandı) şeffaf düğme fareyle renk değiştirmez. Aynı desen kart kutularında da kullanılır (ekran görüntüsü betiği kutuyu adıyla açar).
3. **`GorunumEsdegerligiTests.KabukMenusu` testinin akıbeti: yerine yenisi.** Eski test, `Shell.ItemTemplate`'in `Background`→`BackgroundColor` geçişinde görünümün değişmediğini kanıtlıyordu. Şablon artık yok ve görünüm bilerek değişiyor (simge, grup başlığı); eski şablonla eşdeğerlik anlamsız. Korunan sözleşme: öğe zemininin her durumda (normal, üzerinde, seçili, seçili+üzerinde) doğru `Sidebar*` renginde düz dolgu olması ve `Background` fırçasının yazılmaması. Yeni test `Shell.FlyoutContent`'teki `MenuOgesi` şablonunu gerçek MAUI stil/tetik motoruyla (`GorunumOrtami`) yükleyip bunu sınar. `Kabuk_menu_sablonlari_Background_firca_ozelligini_yazmaz` ise `Shell.FlyoutContent`'i de tarayacak biçimde genişletilir.
4. **Doğrudan rota erişim kuralı aynen korunur.** Bugün yetkisiz bölümün `FlyoutItem.IsVisible`'ı `false`'tur. `AppShell.xaml.cs`'teki bölüm → `FlyoutItem` sözlüğü ve `MenuyuGoster` bunu yapmaya devam eder (`FlyoutContent` ayarlı olduğunda `FlyoutItem`'lar menüde çizilmez); menüde görünen içerik ise `MenuModeli.Goster(bolumler)` ile aynı bölüm listesinden gelir. Rota adları `MenuModeli.Duzen`'de de yazılıdır; bir test ikisinin aynı olduğunu sınar.
5. **Yerleşim: özel `Layout` + saf hesap.** `KartIzgarasi : Layout` çocuklarını (kart kutuları, "Yeni kart ekle" kutusu, ayrıntı görünümü) kendisi kurar; `LayoutManager`'ı ölçtüğü yükseklikleri `KartIzgarasiHesabi.Hesapla`'ya verir, dönen dikdörtgenlere yerleştirir. Sütun sayısı `floor((genişlik + aralık) / (220 + aralık))`, kutular satıra eşit genişlikte yayılır, satır yüksekliği satırdaki en yüksek kutudur; ayrıntı açık kutunun satırından sonra tam genişlikte yer alır. Açık kutunun sırası da saf işlevdir (`KartIzgarasiHesabi.AcikIndeks`).
6. **Form içindeki hata.** Form açıkken model hatası (`Hata`) sayfanın üstünde değil formun içinde gösterilir: `KartTakipViewModel.FormHatasi` / `SayfaHatasi`; `TakipSayfasi` ve `TakipUi.DurumSatirlari` isteğe bağlı `hataYolu` parametresi alır (varsayılan `"Hata"`, diğer sayfalar değişmez).
7. **Tasarımda yeri yazılmayan iki bölüm "Kartı düzenle" formuna girer:** "Kullanım durumu" (aktif/pasif) ve geçişli kartta "Eski borç devri". Harcama formundaki "…faiz / masrafı yukarıdaki ayrı bölümden girin." cümlesi artık yanlış yeri gösterdiği için "…faiz / masrafı \"Faiz / masraf\" formundan girin." olur (tek metin değişikliği).

---

## Dosya yapısı

Oluşturulacak:

| Dosya | Sorumluluk |
|---|---|
| `Kasa.App.Core/MenuModeli.cs` | `MenuSimgeleri` (glyph sabitleri), `MenuTanimi`, `MenuGrupTanimi`, `MenuOgesi` (seçili/üzerinde durumu, komutlar), `MenuGrubu`, `MenuModeli` (grup düzeni, role göre süzme, seçili rota, gezinme ve çıkış olayları) |
| `Kasa.App.Core/KartRengi.cs` | `KartRenkAilesi`, `KartRenkParcasi`, `KartRengi.Sec(ad, kimlik)` (banka → renk ailesi; Türkçe/büyük-küçük harf bağımsız) ve `KartRengi.Anahtar(aile, parça)` (Colors.xaml anahtarı) |
| `Kasa.App.Core/KartIzgarasiHesabi.cs` | `Dikdortgen`, `KartIzgarasiYerlesimi`, `KartIzgarasiHesabi` (sütun sayısı, kutu genişliği, kutu/ayrıntı dikdörtgenleri, açık kutu sırası) |
| `Kasa.App.Core/KartTakipViewModel.Gorunum.cs` | `KartFormu`, `KartSekmesi` ve modelin arayüz durumu: `AcikForm`, `SeciliSekme`, `Sekmeler`, `FormAcCommand`, `VazgecCommand`, `KutuSecCommand`, `YeniKartAcCommand`, `SekmeSecCommand`, `AcikKartId`, `YeniKartFormuAcik`, `FormHatasi`, `SayfaHatasi` |
| `Kasa.App/Controls/KartKutusu.cs` | Tek kartın kutusu (banka rengi, borç, doluluk çubuğu, limit, son ödeme, durum etiketleri, şeffaf tıklama düğmesi) |
| `Kasa.App/Controls/YeniKartKutusu.cs` | Kesik çizgili "Yeni kart ekle" kutusu |
| `Kasa.App/Controls/KartIzgarasi.cs` | Kutuları kaynaktan kuran, açık kutuyu işaretleyen ve ayrıntıyı açık kutunun satırının altına yerleştiren `Layout` |
| `Kasa.App.Core.Tests/MenuModeliTests.cs` | Rol → görünen gruplar/öğeler, boş grup, seçili rota, girişe dönüş, gezinme/çıkış olayları, üzerine gelme |
| `Kasa.App.Core.Tests/KartRengiTests.cs` | Banka eşlemeleri, Türkçe karakter/büyük-küçük harf, tanınmayan bankada sabit palet rengi |
| `Kasa.App.Core.Tests/KartTakipSatiriTests.cs` | Doluluk oranı, metinler, son ödeme, "Son ödeme geçti" (sabit saat), etiket sırası, renk |
| `Kasa.App.Core.Tests/KartIzgarasiHesabiTests.cs` | Sütun sayısı, kutu genişliği, dikdörtgenler, ayrıntı yeri, açık kutu sırası |
| `Kasa.App.Core.Tests/KartTakipGorunumTests.cs` | Tek form, form değiştirme, başarılı kayıtta kapanma, hatada açık kalma, kart değişince kapanma, izleyici, `IdIleSec`, sekmeler, yeni kart |
| `Kasa.App.Core.Tests/Donusturuculer/KartKutusuTests.cs` | `KartKutusu` ve `KartIzgarasi` bileşenleri (gerçek MAUI stil motoru, `GorunumOrtami`) |

Değiştirilecek:

| Dosya | Değişiklik |
|---|---|
| `Kasa.App/AppShell.xaml` | `Shell.ItemTemplate`, `Shell.MenuItemTemplate` ve `MenuItem CikisMenu` kalkar; `Shell.FlyoutContent` gruplu menü gelir |
| `Kasa.App/AppShell.xaml.cs` | `MenuModeli` bağlanır; gezinme `GitIstendi`, çıkış `CikisIstendi`, seçili öğe `OnNavigated` ile |
| `Kasa.App/Resources/Styles/Styles.xaml` | `SimgeAilesi`, `SeffafDugme` |
| `Kasa.App/Resources/Styles/Colors.xaml` | 10 renk ailesi × Zemin/Kenar/Yazi (30 anahtar) |
| `Kasa.App.Core/FinansTakipModelleri.cs` | `KartTakipSatiri` kutu özellikleri + `TimeProvider`; `KartEtiketTuru`, `KartEtiketi` |
| `Kasa.App.Core/KartTakipViewModel.cs` | `TimeProvider? zaman` parametresi; başarılı kayıtlarda `AcikForm = KartFormu.Yok`; `EkstreSec` ekstre formunu açar; `OturumTemizle` |
| `Kasa.App.Core/KartTakipViewModel.Masraf.cs` | Başarılı masraf kaydında form kapanır |
| `Kasa.App/Views/TakipUi.cs` | `DurumSatirlari` ve `TakipSayfasi` kurucusu isteğe bağlı `hataYolu` |
| `Kasa.App/Views/KartTakipPage.cs` | Yeniden düzen: `KartIzgarasi` + ayrıntı (özet, düğmeler, form alanı, sekmeler) |
| `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.KabukSablonu.cs` | `Shell.FlyoutContent` de taranır |
| `Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.KabukMenusu.cs` | Yeni menü öğesi zemin testi (eski eşdeğerlik testi kalkar) |
| `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglamalar.cs` | Kabuk menüsünün bağlamı `MenuModeli` |
| `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.YaziTipi.cs` | `SimgeAilesi` anahtarı |
| `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.cs` | Menü rotaları = kabuk rotaları testi |
| `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Kontrast.cs` | Kart renk aileleri ve kart durum etiketleri kontrastı |
| `.github/scripts/maui-lint-tabani.txt` | `KartTakipPage.cs` uzun satır tabanı düşer |

---

### Görev 1: MenuModeli (menü içeriği ve rol kuralı)

**Dosyalar:**
- Oluştur: `Kasa.App.Core/MenuModeli.cs`
- Test: `Kasa.App.Core.Tests/MenuModeliTests.cs`

- [ ] **Adım 1: Başarısız testi yaz**

`Kasa.App.Core.Tests/MenuModeliTests.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>
/// Masaüstü menüsü (tasarım 2026-09-30 §1): gruplar ve öğeler MenuModeli'nden gelir, görünürlük bugünkü rol kuralıyla
/// (SekmeModeli.Bolumler) süzülür, öğesi kalmayan grup gizlenir, Çıkış grupların altında ayrı durur. Seçili öğe Shell'in
/// konumundan (//rota?sorgu) belirlenir.
/// </summary>
public class MenuModeliTests
{
    private static MenuModeli Menu(Rol rol)
    {
        var menu = new MenuModeli();
        menu.Goster(SekmeModeli.Bolumler(rol));
        return menu;
    }

    /// <summary>"Grup: öğe, öğe" satırları; başlıksız (Çıkış) grup "—".</summary>
    private static string[] Ozet(MenuModeli menu)
        => menu.Gruplar.Select(g => $"{g.Baslik ?? "—"}: {string.Join(", ", g.Ogeler.Select(o => o.Baslik))}").ToArray();

    [Fact]
    public void Editor_butun_gruplari_ve_ogeleri_sirayla_gorur()
        => Assert.Equal(new[]
        {
            "Özet: Kasalar, Haftalık, Aylık",
            "Kayıtlar: İşlemler, Alışlar, Aylık giderler, Ekstre içe aktar",
            "Kart ve kredi: Kartlar, Krediler",
            "Diğer: Bildirimler, Rapor dışa aktar, Ayarlar",
            "—: Çıkış",
        }, Ozet(Menu(Rol.Editor)));

    [Fact]
    public void Izleyici_bugunku_rol_kuralindaki_ogeleri_gorur()
        => Assert.Equal(new[]
        {
            "Özet: Kasalar, Haftalık, Aylık",
            "Kayıtlar: İşlemler, Aylık giderler",
            "Kart ve kredi: Kartlar, Krediler",
            "Diğer: Rapor dışa aktar",
            "—: Çıkış",
        }, Ozet(Menu(Rol.Izleyici)));

    [Fact]
    public void Alici_yalniz_alislari_ve_cikisi_gorur()
        => Assert.Equal(new[] { "Kayıtlar: Alışlar", "—: Çıkış" }, Ozet(Menu(Rol.Alici)));

    [Fact]
    public void Gorunur_ogesi_kalmayan_grubun_basligi_da_gizlenir()
    {
        var menu = new MenuModeli();
        menu.Goster(new[] { Bolum.Kartlar });
        Assert.Equal(new[] { "Kart ve kredi: Kartlar", "—: Çıkış" }, Ozet(menu));
        Assert.All(menu.Gruplar, g => Assert.NotEmpty(g.Ogeler));
        Assert.True(menu.Gruplar[0].BaslikVar);
        Assert.False(menu.Gruplar[0].Ayri);
        Assert.True(menu.Gruplar[^1].Ayri);
    }

    [Fact]
    public void Giristen_donuste_menu_bosalir()
    {
        var menu = Menu(Rol.Editor);
        menu.Goster(Array.Empty<Bolum>());
        Assert.Empty(menu.Gruplar);
        Assert.Empty(menu.Ogeler);
    }

    [Fact]
    public void Duzen_her_bolumu_bir_kez_ve_ayri_simgeyle_tasir()
    {
        var ogeler = MenuModeli.Duzen.SelectMany(g => g.Ogeler).ToList();
        Assert.Equal(Enum.GetValues<Bolum>().Order(), ogeler.Select(o => o.Bolum).Order());
        Assert.All(ogeler, o => Assert.False(string.IsNullOrWhiteSpace(o.Simge)));
        Assert.Equal(ogeler.Count + 1, ogeler.Select(o => o.Simge).Append(MenuSimgeleri.Cikis).Distinct().Count());
        Assert.Equal("kartlar", MenuModeli.Rota(Bolum.Kartlar));
    }

    [Theory]
    [InlineData("//kartlar", "kartlar")]
    [InlineData("//kartlar?KartId=3", "kartlar")]
    [InlineData("//panel/IMPL_panel", "panel")]
    [InlineData("aylik", "aylik")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Rota_adi_konumun_ilk_parcasidir(string? konum, string? beklenen)
        => Assert.Equal(beklenen, MenuModeli.RotaAdi(konum));

    [Fact]
    public void Secili_oge_rotaya_gore_belirlenir()
    {
        var menu = Menu(Rol.Editor);
        menu.RotaSecildi("//kartlar?KartId=3");
        Assert.Equal(new[] { "Kartlar" }, menu.Ogeler.Where(o => o.Secili).Select(o => o.Baslik));
        menu.RotaSecildi("//panel");
        Assert.Equal(new[] { "Kasalar" }, menu.Ogeler.Where(o => o.Secili).Select(o => o.Baslik));
        menu.RotaSecildi("//login");
        Assert.DoesNotContain(menu.Ogeler, o => o.Secili);
    }

    [Fact]
    public void Menu_kurulmadan_gelen_rota_menu_kurulunca_secili_olur()
    {
        var menu = new MenuModeli();
        menu.RotaSecildi("//aylik");
        menu.Goster(SekmeModeli.Bolumler(Rol.Izleyici));
        Assert.Equal(new[] { "Aylık" }, menu.Ogeler.Where(o => o.Secili).Select(o => o.Baslik));
    }

    [Fact]
    public void Ogeye_tiklamak_rotaya_gitmeyi_cikis_oturumu_kapatmayi_ister()
    {
        var menu = Menu(Rol.Editor);
        string? gidilen = null;
        var cikis = 0;
        menu.GitIstendi += (_, rota) => gidilen = rota;
        menu.CikisIstendi += (_, _) => cikis++;
        menu.Ogeler.Single(o => o.Baslik == "Kartlar").SecCommand.Execute(null);
        Assert.Equal("kartlar", gidilen);
        gidilen = null;
        menu.Ogeler.Single(o => o.Baslik == "Çıkış").SecCommand.Execute(null);
        Assert.Null(gidilen);
        Assert.Equal(1, cikis);
    }

    [Fact]
    public void Uzerine_gelme_vurgusu_secili_ogede_gosterilmez()
    {
        var oge = Menu(Rol.Editor).Ogeler.First();
        var bildirilen = new List<string?>();
        oge.PropertyChanged += (_, e) => bildirilen.Add(e.PropertyName);
        oge.UzerineGelCommand.Execute(null);
        Assert.True(oge.UzerindeVurgu);
        oge.Secili = true;
        Assert.False(oge.UzerindeVurgu);
        oge.Secili = false;
        Assert.True(oge.UzerindeVurgu);
        oge.AyrilCommand.Execute(null);
        Assert.False(oge.UzerindeVurgu);
        Assert.Contains(nameof(MenuOgesi.UzerindeVurgu), bildirilen);
    }
}
```

- [ ] **Adım 2: Testi çalıştır, derlenmediğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MenuModeliTests"`
Beklenen: `error CS0246: The type or namespace name 'MenuModeli' could not be found`

- [ ] **Adım 3: MenuModeli'ni yaz**

`Kasa.App.Core/MenuModeli.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

/// <summary>Menü simgeleri: Segoe Fluent Icons (Windows 11) ile Segoe MDL2 Assets'in (Windows 10) ortak kod noktaları. İki yazı
/// tipi de Windows'la gelir, uygulama paketlemez (Styles.xaml SimgeAilesi). Yorumdaki adlar Microsoft Learn'deki
/// segoe-fluent-icons-font ve segoe-ui-symbol-font sayfalarındaki simge adlarıdır; her kod noktası iki sayfada aynı simgedir.</summary>
public static class MenuSimgeleri
{
    public const string Kasalar = "\uE80F";        // Home
    public const string Haftalik = "\uE8C0";       // CalendarWeek
    public const string Aylik = "\uE787";          // Calendar
    public const string Islemler = "\uE8FD";       // BulletedList
    public const string Alislar = "\uE7BF";        // ShoppingCart
    public const string AylikGiderler = "\uE8EE";  // RepeatAll
    public const string EkstreAktar = "\uE8B5";    // Import
    public const string Kartlar = "\uE8C7";        // PaymentCard
    public const string Krediler = "\uE825";       // Bank
    public const string Bildirimler = "\uEA8F";    // Ringer
    public const string DisariAktar = "\uEDE1";    // Export
    public const string Ayarlar = "\uE713";        // Settings
    public const string Cikis = "\uF3B1";          // SignOut
}

/// <summary>Menü öğesinin tanımı: rol bölümü, başlık, simge ve Shell rotası (AppShell.xaml FlyoutItem Route).</summary>
public sealed record MenuTanimi(Bolum Bolum, string Baslik, string Simge, string Rota);

/// <summary>Menü grubunun tanımı: başlık ve sıralı öğeler.</summary>
public sealed record MenuGrupTanimi(string Baslik, IReadOnlyList<MenuTanimi> Ogeler);

/// <summary>Çizilen menü öğesi. Zemini iki durumdan biri belirler: <see cref="Secili"/> (SidebarActive) ya da
/// <see cref="UzerindeVurgu"/> (SidebarHover); ikisi aynı anda doğru olmaz, şablondaki iki DataTrigger çakışmaz.</summary>
public sealed partial class MenuOgesi : ObservableObject
{
    private readonly Action<MenuOgesi> _sec;

    /// <param name="bolum">Rol bölümü; Çıkış öğesinde null.</param>
    /// <param name="sec">Öğeye tıklanınca çağrılır (gezinme ya da çıkış).</param>
    public MenuOgesi(Bolum? bolum, string baslik, string simge, string rota, Action<MenuOgesi> sec)
    {
        Bolum = bolum;
        Baslik = baslik;
        Simge = simge;
        Rota = rota;
        _sec = sec;
    }

    public Bolum? Bolum { get; }
    public string Baslik { get; }
    public string Simge { get; }
    public string Rota { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UzerindeVurgu))]
    private bool _secili;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UzerindeVurgu))]
    private bool _uzerinde;

    /// <summary>Fare üzerinde ve öğe seçili değil: seçili öğe fareyle de seçili zemininde kalır.</summary>
    public bool UzerindeVurgu => Uzerinde && !Secili;

    [RelayCommand]
    private void Sec() => _sec(this);

    [RelayCommand]
    private void UzerineGel() => Uzerinde = true;

    [RelayCommand]
    private void Ayril() => Uzerinde = false;
}

/// <summary>Çizilen grup. Başlıksız grup (Çıkış) üstündeki ayırıcı çizgiyle ayrı durur.</summary>
public sealed record MenuGrubu(string? Baslik, IReadOnlyList<MenuOgesi> Ogeler)
{
    public bool BaslikVar => Baslik is not null;
    public bool Ayri => Baslik is null;
}

/// <summary>
/// Masaüstü menüsünün tek kaynağı (tasarım 2026-09-30 §1): grup ve öğe sırası, başlık, simge ve rota. Görünen öğeler
/// bugünkü rol kuralıyla (<see cref="SekmeModeli.Bolumler"/>) verilen bölümlerdir; öğesi kalmayan grup çizilmez, Çıkış
/// grupların altında ayrı durur. Girişe dönüşte (boş bölüm listesi) menü boşalır. Seçili öğe Shell'in konumundan gelir
/// (<see cref="RotaSecildi"/>). Gezinme ve çıkış kabuğun işidir: model yalnız olay bildirir (Windows'tan bağımsız sınanır).
/// </summary>
public sealed partial class MenuModeli : ObservableObject
{
    public const string CikisBasligi = "Çıkış";

    public static IReadOnlyList<MenuGrupTanimi> Duzen { get; } =
    [
        new("Özet",
        [
            new(Bolum.Panel, "Kasalar", MenuSimgeleri.Kasalar, "panel"),
            new(Bolum.Haftalik, "Haftalık", MenuSimgeleri.Haftalik, "haftalik"),
            new(Bolum.Aylik, "Aylık", MenuSimgeleri.Aylik, "aylik"),
        ]),
        new("Kayıtlar",
        [
            new(Bolum.Islemler, "İşlemler", MenuSimgeleri.Islemler, "islemler"),
            new(Bolum.Alislar, "Alışlar", MenuSimgeleri.Alislar, "alislar"),
            new(Bolum.AylikGiderler, "Aylık giderler", MenuSimgeleri.AylikGiderler, "aylikgiderler"),
            new(Bolum.EkstreAktar, "Ekstre içe aktar", MenuSimgeleri.EkstreAktar, "ekstreaktar"),
        ]),
        new("Kart ve kredi",
        [
            new(Bolum.Kartlar, "Kartlar", MenuSimgeleri.Kartlar, "kartlar"),
            new(Bolum.Krediler, "Krediler", MenuSimgeleri.Krediler, "krediler"),
        ]),
        new("Diğer",
        [
            new(Bolum.Bildirimler, "Bildirimler", MenuSimgeleri.Bildirimler, "bildirimler"),
            new(Bolum.DisariAktar, "Rapor dışa aktar", MenuSimgeleri.DisariAktar, "disariaktar"),
            new(Bolum.Ayarlar, "Ayarlar", MenuSimgeleri.Ayarlar, "ayarlar"),
        ]),
    ];

    private string? _seciliRota;

    [ObservableProperty]
    private IReadOnlyList<MenuGrubu> _gruplar = [];

    /// <summary>Bir öğeye tıklandı: kabuk bu rotaya (//rota) gider.</summary>
    public event EventHandler<string>? GitIstendi;

    /// <summary>Çıkış'a tıklandı: kabuk oturumu kapatıp girişe döner.</summary>
    public event EventHandler? CikisIstendi;

    public IEnumerable<MenuOgesi> Ogeler => Gruplar.SelectMany(g => g.Ogeler);

    /// <summary>Menüyü verilen bölümlerle kurar (AppShell.MenuyuGoster: role göre ya da girişe dönüşte boş).</summary>
    public void Goster(IReadOnlyCollection<Bolum> bolumler)
    {
        var gruplar = new List<MenuGrubu>();
        foreach (var grup in Duzen)
        {
            var ogeler = grup.Ogeler.Where(o => bolumler.Contains(o.Bolum))
                .Select(o => new MenuOgesi(o.Bolum, o.Baslik, o.Simge, o.Rota, Sec)).ToList();
            if (ogeler.Count > 0)
                gruplar.Add(new MenuGrubu(grup.Baslik, ogeler));
        }
        if (gruplar.Count > 0)
            gruplar.Add(new MenuGrubu(null, [new MenuOgesi(null, CikisBasligi, MenuSimgeleri.Cikis, "", _ => CikisIstendi?.Invoke(this, EventArgs.Empty))]));
        Gruplar = gruplar;
        SeciliyiYansit();
    }

    /// <summary>Shell'in yeni konumu (ShellNavigatedEventArgs.Current.Location): o rotanın öğesi seçili olur.</summary>
    public void RotaSecildi(string? konum)
    {
        _seciliRota = RotaAdi(konum);
        SeciliyiYansit();
    }

    /// <summary>"//kartlar?KartId=3" → "kartlar": baştaki eğik çizgiler atılır, ilk '/' ya da '?' işaretine kadar alınır.</summary>
    public static string? RotaAdi(string? konum)
    {
        if (string.IsNullOrWhiteSpace(konum))
            return null;
        var ad = konum.TrimStart('/');
        var son = ad.IndexOfAny(['/', '?']);
        return son < 0 ? ad : ad[..son];
    }

    /// <summary>Bölümün Shell rotası (AppShell.xaml FlyoutItem Route ile aynı; MauiKayitTutarliligiTests sınar).</summary>
    public static string Rota(Bolum bolum) => Duzen.SelectMany(g => g.Ogeler).Single(o => o.Bolum == bolum).Rota;

    private void SeciliyiYansit()
    {
        foreach (var oge in Ogeler)
            oge.Secili = oge.Bolum is not null && oge.Rota == _seciliRota;
    }

    private void Sec(MenuOgesi oge) => GitIstendi?.Invoke(this, oge.Rota);
}
```

- [ ] **Adım 4: Testleri çalıştır, geçtiğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MenuModeliTests|FullyQualifiedName~RolTests"`
Beklenen: `Passed!` ve `Failed: 0` (MenuModeliTests ve değişmeyen RolTests).

- [ ] **Adım 5: Commit**

```bash
git add Kasa.App.Core/MenuModeli.cs Kasa.App.Core.Tests/MenuModeliTests.cs
git commit -F - <<'EOF'
feat(app): gruplu masaüstü menüsünün modeli (MenuModeli)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Görev 2: Menü XAML'i, kabuk bağlantısı ve XAML tarama testleri

**Dosyalar:**
- Değiştir: `Kasa.App/AppShell.xaml` (tamamı), `Kasa.App/AppShell.xaml.cs` (tamamı), `Kasa.App/Resources/Styles/Styles.xaml`
- Değiştir (test): `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.KabukSablonu.cs`, `…/GorunumEsdegerligiTests.KabukMenusu.cs` (tamamı), `…/MauiKayitTutarliligiTests.Baglamalar.cs`, `…/MauiKayitTutarliligiTests.YaziTipi.cs`, `…/MauiKayitTutarliligiTests.cs`

- [ ] **Adım 1: Kabuk şablonu taramasını FlyoutContent'e genişlet (test)**

`MauiKayitTutarliligiTests.KabukSablonu.cs`'te sınıf özetinin son cümlesinden sonra (satır 13, `/// ve öğelere uygulanan stiller düz renk için BackgroundColor kullanır (görünüm aynı: düz dolgu).`) şu satırları ekle, `/// </summary>` satırından önce:

```csharp
/// Menü artık Shell.FlyoutContent'teki kendi listemizdir (ShellFlyoutItemView kullanılmaz); kural onu da kapsar: sorunlu
/// sürümlerde menüye Background fırçası girmez, görünüm düz dolguyla aynıdır.
```

Aynı dosyada:

```csharp
    private static readonly string[] KabukSablonuOzellikleri = ["Shell.ItemTemplate", "Shell.MenuItemTemplate"];
```
→
```csharp
    private static readonly string[] KabukSablonuOzellikleri = ["Shell.ItemTemplate", "Shell.MenuItemTemplate", "Shell.FlyoutContent"];
```

ve

```csharp
        Assert.True(sablonSayisi >= 2, $"Kabuk menü şablonları okunamadı ({sablonSayisi}); AppShell.xaml'da Shell.ItemTemplate ve Shell.MenuItemTemplate beklenir.");
```
→
```csharp
        Assert.True(sablonSayisi >= 1, $"Kabuk menüsü okunamadı ({sablonSayisi}); AppShell.xaml'da Shell.FlyoutContent beklenir.");
        Assert.Contains("<Shell.FlyoutContent>", File.ReadAllText(Path.Combine(Uygulama, "AppShell.xaml")));
```

- [ ] **Adım 2: Menü öğesi zemin testini yenisiyle değiştir (test)**

`Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.KabukMenusu.cs` dosyasının tamamını şununla değiştir:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kabuk menüsünün öğe şablonu (AppShell.xaml Shell.FlyoutContent, x:DataType MenuOgesi) gerçek uygulama kaynaklarıyla
/// yüklenir. Zemin durumun rengidir: normal saydam, fareyle üzerinde SidebarHover, seçili (fare üzerinde olsa da)
/// SidebarActive. Zemin BackgroundColor'dan gelen düz boyadır; Background (Brush) yazılmaz (dotnet/maui#38813 dersi). Açık
/// nokta yalnız seçili öğede görünür. Eski Shell.ItemTemplate ile görünüm eşdeğerliği testi, menü bilerek değiştiği için
/// kaldırıldı (tasarım 2026-09-30 §1); korunan sözleşme durum → zemin rengidir.
/// </summary>
public partial class GorunumEsdegerligiTests
{
    private static Grid MenuOgesiYukle(MenuOgesi oge)
    {
        var sablon = Regex.Match(GorunumOrtami.Oku("AppShell.xaml"), @"<DataTemplate x:DataType=""core:MenuOgesi"">(.*?)</DataTemplate>", RegexOptions.Singleline);
        Assert.True(sablon.Success, "AppShell.xaml'da MenuOgesi şablonu bulunamadı.");
        return Assert.IsType<Grid>(GorunumOrtami.Yukle(sablon.Groups[1].Value, oge).Content);
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(false, true, "SidebarHover")]
    [InlineData(true, false, "SidebarActive")]
    [InlineData(true, true, "SidebarActive")]
    public void Kabuk_menu_ogesi_zemini_durumun_rengidir(bool secili, bool uzerinde, string? renkAnahtari)
    {
        var oge = new MenuOgesi(Bolum.Kartlar, "Kartlar", MenuSimgeleri.Kartlar, "kartlar", _ => { }) { Secili = secili, Uzerinde = uzerinde };
        var kok = MenuOgesiYukle(oge);
        var beklenen = renkAnahtari is null ? Colors.Transparent : (Color)Application.Current!.Resources[renkAnahtari];
        Assert.Equal(beklenen.ToArgbHex(true), Zemin(kok));
        Assert.True(Brush.IsNullOrEmpty(kok.Background), "Menü öğesi Background (Brush) yazmamalı (dotnet/maui#38813).");
        Assert.Equal(secili, kok.Children.OfType<Ellipse>().Single().IsVisible);
    }

    [Fact]
    public void Kabuk_menu_ogesi_zemini_durum_degisince_guncellenir_ve_dugme_basligi_tasir()
    {
        var oge = new MenuOgesi(Bolum.Kartlar, "Kartlar", MenuSimgeleri.Kartlar, "kartlar", _ => { });
        var kok = MenuOgesiYukle(oge);
        oge.UzerineGelCommand.Execute(null);
        Assert.Equal(((Color)Application.Current!.Resources["SidebarHover"]).ToArgbHex(true), Zemin(kok));
        oge.Secili = true;
        Assert.Equal(((Color)Application.Current!.Resources["SidebarActive"]).ToArgbHex(true), Zemin(kok));
        oge.Secili = false;
        oge.AyrilCommand.Execute(null);
        Assert.Equal(Colors.Transparent.ToArgbHex(true), Zemin(kok));
        var dugme = kok.Children.OfType<Button>().Single();
        Assert.Equal("Kartlar", SemanticProperties.GetDescription(dugme));
        Assert.Same(oge.SecCommand, dugme.Command);
        Assert.All(kok.Children.OfType<Label>(), l => Assert.True(l.InputTransparent));
    }

    /// <summary>İşleyicinin çizdiği zemin: IView.Background düz boya olmalı; rengi ARGB onaltılık.</summary>
    private static string Zemin(IView gorunum)
    {
        var boya = Assert.IsType<SolidPaint>(gorunum.Background, exactMatch: false);
        return boya.Color.ToArgbHex(true);
    }
}
```

- [ ] **Adım 3: Yazı ailesi testine SimgeAilesi'ni ekle (test)**

`MauiKayitTutarliligiTests.YaziTipi.cs`'te:

```csharp
        Assert.Equal(new[] { "Segoe UI Variable Text", "Segoe UI" }, aile.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries));

        var dogrudan = UygulamaKaynaklari("*.xaml", "*.cs")
            .SelectMany(d => AileKullanimi().Matches(File.ReadAllText(d)).Select(m => (Dosya: Path.GetFileName(d), Deger: m.Groups[1].Value)))
            .Where(k => k.Deger != "{StaticResource YaziAilesi}").Select(k => $"{k.Dosya}: {k.Deger}").ToList();
```
→
```csharp
        Assert.Equal(new[] { "Segoe UI Variable Text", "Segoe UI" }, aile.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries));
        // Menü simgeleri: Windows 11'in Segoe Fluent Icons'u, onun olmadığı Windows 10'da Segoe MDL2 Assets (ikisi de sistemle gelir).
        var simge = SimgeAilesiTanimi().Match(Oku("Resources/Styles/Styles.xaml"));
        Assert.True(simge.Success, "Styles.xaml'da <x:String x:Key=\"SimgeAilesi\"> tanımı yok.");
        Assert.Equal(new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }, simge.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries));

        var dogrudan = UygulamaKaynaklari("*.xaml", "*.cs")
            .SelectMany(d => AileKullanimi().Matches(File.ReadAllText(d)).Select(m => (Dosya: Path.GetFileName(d), Deger: m.Groups[1].Value)))
            .Where(k => k.Deger is not ("{StaticResource YaziAilesi}" or "{StaticResource SimgeAilesi}")).Select(k => $"{k.Dosya}: {k.Deger}").ToList();
```

ve dosyanın sonundaki `YaziAilesiTanimi` bildiriminden sonra:

```csharp
    [GeneratedRegex(@"<x:String x:Key=""SimgeAilesi"">([^<]+)</x:String>")]
    private static partial Regex SimgeAilesiTanimi();
```

- [ ] **Adım 4: Menü rotaları = kabuk rotaları testini ekle (test)**

`MauiKayitTutarliligiTests.cs`'te `Her_rol_bolumu_kabukta_menu_ogesine_bagli_ve_giriste_gizlenir` testinden hemen sonra:

```csharp
    /// <summary>Menü (MenuModeli) her bölümü, kabuktaki aynı adlı FlyoutItem'ın rotasına gönderir: menü ile rota kaynağı ayrışmaz.</summary>
    [Fact]
    public void Menu_modeli_rotalari_kabuktaki_sayfa_rotalariyla_ayni()
    {
        var rotalar = KabukRotasi().Matches(Oku("AppShell.xaml")).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Assert.Equal(Enum.GetValues<Bolum>().Length, rotalar.Count);
        Assert.All(Enum.GetValues<Bolum>(), b => Assert.Equal(rotalar[b.ToString()], MenuModeli.Rota(b)));
    }
```

ve dosyanın sonundaki `MenuSozlugu` bildiriminden sonra:

```csharp
    [GeneratedRegex(@"<FlyoutItem x:Name=""(\w+)Item""[^>]*?Route=""(\w+)""")]
    private static partial Regex KabukRotasi();
```

- [ ] **Adım 5: Testleri çalıştır, başarısız olduğunu gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MauiKayitTutarliligiTests|FullyQualifiedName~GorunumEsdegerligiTests"`
Beklenen: `Failed:` en az 3 — `Kabuk_menu_sablonlari_Background_firca_ozelligini_yazmaz` (`<Shell.FlyoutContent>` yok), `Kabuk_menu_ogesi_*` (MenuOgesi şablonu bulunamadı), `Yazi_ailesi_tek_anahtardan_gelir…` (SimgeAilesi tanımı yok). `Menu_modeli_rotalari…` geçer.

- [ ] **Adım 6: Stil anahtarlarını ekle**

`Kasa.App/Resources/Styles/Styles.xaml`'da `<x:String x:Key="YaziAilesi">Segoe UI Variable Text, Segoe UI</x:String>` satırından hemen sonra:

```xml

    <!-- Simge yazı tipi (menü simgeleri, Kasa.App.Core MenuSimgeleri): Windows 11'de Segoe Fluent Icons, onun olmadığı Windows
         10'da Segoe MDL2 Assets. İkisi de Windows'la gelir, paketlenmez (Segoe Fluent Icons başka platforma dağıtılamaz);
         kullanılan kod noktaları iki yazı tipinde aynı simgedir. -->
    <x:String x:Key="SimgeAilesi">Segoe Fluent Icons, Segoe MDL2 Assets</x:String>
```

Aynı dosyada `<!-- ===== KARTLAR & KUTULAR ===== -->` satırından hemen önce:

```xml
    <!-- Şeffaf düğme: menü öğesinin ve kart kutusunun tıklama/klavye yüzeyi (Tab ile odaklanır, UI Otomasyonu'nda adıyla
         görünür). Zemin ve görsel durum yazmaz, zemini üstündeki öğe çizer; açık anahtarlı stil örtük Button stilinin yeşil
         zeminini ve PointerOver durumunu uygulamaz. Düz renk BackgroundColor ile (dotnet/maui#38813). -->
    <Style x:Key="SeffafDugme" TargetType="Button">
        <Setter Property="BackgroundColor" Value="Transparent" />
        <Setter Property="BorderWidth" Value="0" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="MinimumHeightRequest" Value="0" />
        <Setter Property="MinimumWidthRequest" Value="0" />
    </Style>

```

- [ ] **Adım 7: AppShell.xaml'ı yeniden yaz**

`Kasa.App/AppShell.xaml` dosyasının tamamı:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<!-- Emar Kasa · AppShell — koyu yeşil flyout (tasarım handoff).
     Menü Shell'in öğe şablonu değil, Shell.FlyoutContent'teki gruplu listedir; içerik ve rol kuralı Kasa.App.Core MenuModeli'nden
     gelir (docs/specs/2026-09-30-masaustu-menu-ve-kartlar.md §1). FlyoutItem'lar menüde çizilmez: rota kaynağıdır ve yetkisi
     olmayan bölümün öğesi gizli (IsVisible=false) kalır. Görünürlük/route mantığı code-behind'da: MenuyuGoster, GiriseDonAsync. -->
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:v="clr-namespace:Kasa.App.Views"
       xmlns:core="clr-namespace:Kasa.App.Core;assembly=Kasa.App.Core"
       x:Class="Kasa.App.AppShell"
       Title="Emar Kasa"
       FlyoutBehavior="Locked"
       FlyoutBackgroundColor="{StaticResource Sidebar}"
       FlyoutWidth="248"
       Shell.BackgroundColor="{StaticResource Card}"
       Shell.ForegroundColor="{StaticResource Ink}"
       Shell.TitleColor="{StaticResource Ink}">

    <!-- Marka başlığı -->
    <Shell.FlyoutHeader>
        <Grid Padding="18,22,18,14" ColumnDefinitions="Auto,*" ColumnSpacing="10" RowDefinitions="Auto,Auto">
            <Border Grid.RowSpan="2" WidthRequest="30" HeightRequest="30" StrokeThickness="0"
                    StrokeShape="RoundRectangle 9" Background="{StaticResource BrushGreen}"
                    VerticalOptions="Center">
                <Label Text="₺" TextColor="White" FontAttributes="Bold" FontSize="15"
                       HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
            <Label Grid.Column="1" Text="Emar Kasa" FontAttributes="Bold" FontSize="15.5"
                   TextColor="{StaticResource SidebarTitle}" />
            <Label Grid.Column="1" Grid.Row="1" Text="KASA DEFTERİ" FontSize="10.5"
                   CharacterSpacing="1" TextColor="{StaticResource SidebarMuted}" />
        </Grid>
    </Shell.FlyoutHeader>

    <!-- Gruplu menü (MenuModeli.Gruplar; bağlam code-behind'da MenuAlani.BindingContext). Öğe zemini BackgroundColor ile yazılır,
         Background (Brush) ile değil (dotnet/maui#38813 dersi; MauiKayitTutarliligiTests.Kabuk_menu_sablonlari_Background_firca_ozelligini_yazmaz
         bu içeriği de tarar). Seçili öğe SidebarActive zemin + açık nokta, fareyle üzerine gelinen öğe SidebarHover zemin
         (MenuOgesi.Secili / UzerindeVurgu; ikisi aynı anda doğru olmaz). Tıklama ve klavye yüzeyi alttaki şeffaf düğmedir
         (SeffafDugme): Tab ile odaklanır, UI Otomasyonu'nda öğe başlığıyla adlanır; yazılar girdiyi geçirir. -->
    <Shell.FlyoutContent>
        <ScrollView x:Name="MenuAlani" x:DataType="core:MenuModeli">
            <VerticalStackLayout Padding="0,4,0,16" BindableLayout.ItemsSource="{Binding Gruplar}">
                <BindableLayout.ItemTemplate>
                    <DataTemplate x:DataType="core:MenuGrubu">
                        <VerticalStackLayout Spacing="2">
                            <BoxView IsVisible="{Binding Ayri}" HeightRequest="1" Margin="20,10,20,6"
                                     Color="{StaticResource SidebarLine}" />
                            <Label IsVisible="{Binding BaslikVar}" Text="{Binding Baslik}" Margin="22,14,22,4"
                                   FontSize="11.5" FontAttributes="Bold" TextColor="{StaticResource SidebarMuted}" />
                            <VerticalStackLayout Spacing="2" BindableLayout.ItemsSource="{Binding Ogeler}">
                                <BindableLayout.ItemTemplate>
                                    <DataTemplate x:DataType="core:MenuOgesi">
                                        <Grid Margin="10,0" HeightRequest="38" ColumnDefinitions="Auto,*,Auto" ColumnSpacing="10"
                                              BackgroundColor="Transparent">
                                            <Grid.Triggers>
                                                <DataTrigger TargetType="Grid" Binding="{Binding UzerindeVurgu}" Value="True">
                                                    <Setter Property="BackgroundColor" Value="{StaticResource SidebarHover}" />
                                                </DataTrigger>
                                                <DataTrigger TargetType="Grid" Binding="{Binding Secili}" Value="True">
                                                    <Setter Property="BackgroundColor" Value="{StaticResource SidebarActive}" />
                                                </DataTrigger>
                                            </Grid.Triggers>
                                            <Grid.GestureRecognizers>
                                                <PointerGestureRecognizer PointerEnteredCommand="{Binding UzerineGelCommand}"
                                                                          PointerExitedCommand="{Binding AyrilCommand}" />
                                            </Grid.GestureRecognizers>
                                            <Button Grid.ColumnSpan="3" Style="{StaticResource SeffafDugme}"
                                                    Command="{Binding SecCommand}"
                                                    SemanticProperties.Description="{Binding Baslik}" />
                                            <Label Text="{Binding Simge}" Margin="12,0,0,0" FontFamily="{StaticResource SimgeAilesi}"
                                                   FontSize="16" TextColor="{StaticResource SidebarText}" VerticalOptions="Center"
                                                   InputTransparent="True" />
                                            <Label Grid.Column="1" Text="{Binding Baslik}" FontSize="13.5"
                                                   TextColor="{StaticResource SidebarText}" VerticalOptions="Center"
                                                   InputTransparent="True" />
                                            <Ellipse Grid.Column="2" IsVisible="{Binding Secili}" Margin="0,0,12,0" WidthRequest="6"
                                                     HeightRequest="6" Fill="{StaticResource SidebarAccent}" VerticalOptions="Center"
                                                     InputTransparent="True" />
                                        </Grid>
                                    </DataTemplate>
                                </BindableLayout.ItemTemplate>
                            </VerticalStackLayout>
                        </VerticalStackLayout>
                    </DataTemplate>
                </BindableLayout.ItemTemplate>
            </VerticalStackLayout>
        </ScrollView>
    </Shell.FlyoutContent>

    <ShellContent Route="login" ContentTemplate="{DataTemplate v:LoginPage}" FlyoutItemIsVisible="False" />
    <FlyoutItem x:Name="PanelItem" Title="Kasalar" Route="panel" IsVisible="False">
        <ShellContent ContentTemplate="{DataTemplate v:PanelPage}" />
    </FlyoutItem>
    <FlyoutItem x:Name="HaftalikItem" Title="Haftalık" Route="haftalik" IsVisible="False">
        <ShellContent ContentTemplate="{DataTemplate v:HaftalikPage}" />
    </FlyoutItem>
    <FlyoutItem x:Name="AylikItem" Title="Aylık" Route="aylik" IsVisible="False">
        <ShellContent ContentTemplate="{DataTemplate v:AylikPage}" />
    </FlyoutItem>
    <FlyoutItem x:Name="IslemlerItem" Title="İşlemler" Route="islemler" IsVisible="False">
        <ShellContent ContentTemplate="{DataTemplate v:IslemlerPage}" />
    </FlyoutItem>
    <FlyoutItem x:Name="AylikGiderlerItem" Title="Aylık Giderler" Route="aylikgiderler" IsVisible="False"><ShellContent ContentTemplate="{DataTemplate v:AylikGiderPage}" /></FlyoutItem>
    <FlyoutItem x:Name="EkstreAktarItem" Title="Ekstre İçe Aktar" Route="ekstreaktar" IsVisible="False"><ShellContent ContentTemplate="{DataTemplate v:EkstreAktarmaPage}" /></FlyoutItem>
    <FlyoutItem x:Name="AyarlarItem" Title="Ayarlar" Route="ayarlar" IsVisible="False">
        <ShellContent ContentTemplate="{DataTemplate v:AyarlarPage}" />
    </FlyoutItem>
    <FlyoutItem x:Name="AlislarItem" Title="Alışlar" Route="alislar" IsVisible="False">
        <ShellContent ContentTemplate="{DataTemplate v:AlislarPage}" />
    </FlyoutItem>
    <FlyoutItem x:Name="DisariAktarItem" Title="Rapor Dışa Aktar" Route="disariaktar" IsVisible="False"><ShellContent ContentTemplate="{DataTemplate v:DisariAktarPage}" /></FlyoutItem>
    <FlyoutItem x:Name="KartlarItem" Title="Kartlar" Route="kartlar" IsVisible="False"><ShellContent ContentTemplate="{DataTemplate v:KartTakipPage}" /></FlyoutItem>
    <FlyoutItem x:Name="KredilerItem" Title="Krediler" Route="krediler" IsVisible="False"><ShellContent ContentTemplate="{DataTemplate v:KrediTakipPage}" /></FlyoutItem>
    <FlyoutItem x:Name="BildirimlerItem" Title="Bildirimler" Route="bildirimler" IsVisible="False"><ShellContent ContentTemplate="{DataTemplate v:BildirimPage}" /></FlyoutItem>
</Shell>
```

- [ ] **Adım 8: AppShell.xaml.cs'i yeniden yaz**

`Kasa.App/AppShell.xaml.cs` dosyasının tamamı:

```csharp
using Kasa.App.Core;

namespace Kasa.App;

public partial class AppShell : Shell
{
    private readonly AuthViewModel _auth;
    /// <summary>Menünün içeriği (gruplar, öğeler, seçili öğe): Shell.FlyoutContent kökünün (MenuAlani) bağlamı.</summary>
    private readonly MenuModeli _menuModeli = new();
    /// <summary>Rol bölümü → sayfa öğesi (tek kaynak). Öğeler menüde çizilmez (menüyü MenuModeli çizer) ama rota kaynağıdır;
    /// yetkisi olmayan bölümün öğesi gizli kalır, doğrudan rotayla erişim kuralı önceki menüdekiyle aynıdır.</summary>
    private readonly IReadOnlyDictionary<Bolum, FlyoutItem> _menu;
    private bool _giriseDonuluyor;

    public AppShell(AuthViewModel auth)
    {
        InitializeComponent();
        _auth = auth;
        _menu = new Dictionary<Bolum, FlyoutItem>
        {
            [Bolum.Panel] = PanelItem,
            [Bolum.Haftalik] = HaftalikItem,
            [Bolum.Aylik] = AylikItem,
            [Bolum.Islemler] = IslemlerItem,
            [Bolum.AylikGiderler] = AylikGiderlerItem,
            [Bolum.Ayarlar] = AyarlarItem,
            [Bolum.Alislar] = AlislarItem,
            [Bolum.DisariAktar] = DisariAktarItem,
            [Bolum.Kartlar] = KartlarItem,
            [Bolum.Krediler] = KredilerItem,
            [Bolum.Bildirimler] = BildirimlerItem,
            [Bolum.EkstreAktar] = EkstreAktarItem,
        };
        MenuAlani.BindingContext = _menuModeli;
        _menuModeli.GitIstendi += async (_, rota) => await GoToAsync("//" + rota);
        _menuModeli.CikisIstendi += async (_, _) => await CikisAsync();
        _auth.OturumSonlandi += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await GiriseDonAsync());
        Loaded += async (_, _) => await AcilistaYonlendirAsync();
    }

    /// <summary>Her gezinmede (menü, sayfalar arası bağlantı, girişe dönüş) seçili menü öğesi yeni konumdan belirlenir.</summary>
    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        _menuModeli.RotaSecildi(args.Current?.Location?.OriginalString);
    }

    private async Task AcilistaYonlendirAsync()
    {
        var girildi = await _auth.AcilistaDogrulaAsync();
        if (girildi)
            MenuyuAc();
        else
            await GiriseDonAsync();
    }

    public void MenuyuAc()
    {
        MenuyuGoster(SekmeModeli.Bolumler(_auth.AktifRol));
        _ = GoToAsync(_auth.AktifRol == Rol.Alici ? "//alislar" : "//panel");
    }

    /// <summary>Yalnız verilen bölümlerin sayfa öğeleri erişilebilir ve menüde görünür (girişe dönüşte hiçbiri).</summary>
    private void MenuyuGoster(IReadOnlyCollection<Bolum> bolumler)
    {
        foreach (var (bolum, oge) in _menu)
            oge.IsVisible = bolumler.Contains(bolum);
        _menuModeli.Goster(bolumler);
    }

    private async Task CikisAsync()
    {
        await _auth.CikisAsync();
        await GiriseDonAsync();
    }

    private async Task GiriseDonAsync()
    {
        if (_giriseDonuluyor)
            return;
        _giriseDonuluyor = true;
        try
        {
            MenuyuGoster([]);
            await GoToAsync("//login");
        }
        finally { _giriseDonuluyor = false; }
    }
}
```

- [ ] **Adım 9: XAML bağlama denetimine kabuk menüsünün bağlamını öğret**

`MauiKayitTutarliligiTests.Baglamalar.cs`'te:

```csharp
/// çipe uyguladığı Chip/ChipText stilleri ve "Ad" yolu grubun öğe türüyle); kabuk şablonları MAUI'nin öğe türleriyle
/// denetlenir.</item>
```
→
```csharp
/// çipe uyguladığı Chip/ChipText stilleri ve "Ad" yolu grubun öğe türüyle); kabuk menüsü (Shell.FlyoutContent) MenuModeli
/// bağlamıyla denetlenir.</item>
```

```csharp
    /// <summary>Kabuk şablonlarının bağlamı MAUI'nin kendi öğesidir (menü öğesi ve menü komutu).</summary>
    private static readonly Dictionary<string, Type> KabukSablonlari = new()
    {
        ["Shell.ItemTemplate"] = typeof(BaseShellItem),
        ["Shell.MenuItemTemplate"] = typeof(MenuItem),
    };
```
→
```csharp
    /// <summary>Kabuk menüsünün bağlamı: AppShell.xaml.cs, Shell.FlyoutContent kökünün (MenuAlani) BindingContext'ine MenuModeli atar.</summary>
    private static readonly Dictionary<string, Type> KabukIcerikleri = new()
    {
        ["Shell.FlyoutContent"] = typeof(MenuModeli),
    };
```

```csharp
            // Kabuk şablonları (menü öğesi, menü komutu) Aşama 4 kapsamı dışında: bağlamaları çalışma anında çözülür.
```
→
```csharp
            // Kabuk menüsü (Shell.FlyoutContent, bağlamı MenuModeli) Aşama 4'ün x:DataType zorunluluğu dışında; yolları burada çözülür.
```

```csharp
                // Özellik öğesi (CollectionView.ItemTemplate, Label.FormattedText ...): bağlam değişmez; kabuk şablonları hariç.
                if (KabukSablonlari.TryGetValue(ad, out var kabuk))
                    sablon = kabuk;
```
→
```csharp
                // Özellik öğesi (CollectionView.ItemTemplate, Label.FormattedText ...): bağlam değişmez; kabuk menüsü hariç.
                if (KabukIcerikleri.TryGetValue(ad, out var kabuk))
                    baglam = kabuk;
```

- [ ] **Adım 10: Testleri çalıştır, geçtiğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MauiKayitTutarliligiTests|FullyQualifiedName~GorunumEsdegerligiTests|FullyQualifiedName~MenuModeliTests"`
Beklenen: `Passed!` ve `Failed: 0`. (`Xaml_baglama_yollari_baglamin_gercek_turunde_var` AppShell'deki 13 bağlamayı MenuModeli/MenuGrubu/MenuOgesi üzerinde çözer.)

- [ ] **Adım 11: Windows derlemesi (XAML kaynak üreteci ve derlenmiş bağlamalar)**

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
Beklenen: `0 Warning(s)`, `0 Error(s)`. MAUIG2045 çıkarsa bağlama yolu x:DataType türünde yok demektir: yolu `MenuModeli.cs`'teki adla düzelt.

- [ ] **Adım 12: maui-lint ve biçim**

Çalıştır: `bash .github/scripts/maui-lint.sh`
Beklenen: `maui-lint: … (0 tanımsız) …` ve `maui-lint: taban içinde.`
Çalıştır: `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes`
Beklenen: çıktı yok, çıkış kodu 0.

- [ ] **Adım 13: Commit**

```bash
git add Kasa.App/AppShell.xaml Kasa.App/AppShell.xaml.cs Kasa.App/Resources/Styles/Styles.xaml Kasa.App.Core.Tests/Donusturuculer/
git commit -F - <<'EOF'
feat(app): gruplu, simgeli masaüstü menüsü (Shell.FlyoutContent)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Görev 3: Kart rengi (KartRengi), renk anahtarları ve kontrast testi

**Dosyalar:**
- Oluştur: `Kasa.App.Core/KartRengi.cs`, `Kasa.App.Core.Tests/KartRengiTests.cs`
- Değiştir: `Kasa.App/Resources/Styles/Colors.xaml`, `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Kontrast.cs`

- [ ] **Adım 1: Başarısız testleri yaz**

`Kasa.App.Core.Tests/KartRengiTests.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Kart kutusunun rengi kart adındaki bankadan gelir (tasarım 2026-09-30 §2 Renk); büyük/küçük harf ve Türkçe
/// karakter farkı gözetilmez, tanınmayan banka kart kimliğine göre sabit bir palet rengi alır.</summary>
public class KartRengiTests
{
    [Theory]
    [InlineData("Garanti Bonus", KartRenkAilesi.Yesil)]
    [InlineData("GARANTİ BBVA", KartRenkAilesi.Yesil)]
    [InlineData("Akbank Axess", KartRenkAilesi.Kirmizi)]
    [InlineData("İş Bankası Maximum", KartRenkAilesi.Mavi)]
    [InlineData("İş", KartRenkAilesi.Mavi)]
    [InlineData("is", KartRenkAilesi.Mavi)]
    [InlineData("ISBANK", KartRenkAilesi.Mavi)]
    [InlineData("QNB Card Finans", KartRenkAilesi.Mor)]
    [InlineData("Finansbank", KartRenkAilesi.Mor)]
    [InlineData("VakıfBank World", KartRenkAilesi.Sari)]
    [InlineData("VAKIFBANK", KartRenkAilesi.Sari)]
    [InlineData("Yapı Kredi World", KartRenkAilesi.Lacivert)]
    [InlineData("YAPI KREDİ", KartRenkAilesi.Lacivert)]
    [InlineData("Ziraat Bankkart", KartRenkAilesi.Kirmizi)]
    [InlineData("Halkbank Paraf", KartRenkAilesi.Mavi)]
    [InlineData("DenizBank", KartRenkAilesi.Mavi)]
    [InlineData("Enpara.com", KartRenkAilesi.Mor)]
    [InlineData("TEB Bonus", KartRenkAilesi.Yesil)]
    public void Banka_adi_renk_ailesini_belirler(string ad, KartRenkAilesi beklenen)
        => Assert.Equal(beklenen, KartRengi.Sec(ad, 1));

    [Theory]
    [InlineData("Visa kartım")]   // "is" yalnız tam sözcükse İş Bankası'dır; "visa" içinde geçmesi sayılmaz
    [InlineData("Şirket kartı")]
    [InlineData("")]
    [InlineData(null)]
    public void Taninmayan_banka_kimlige_gore_sabit_palet_rengi_alir(string? ad)
    {
        Assert.Contains(KartRengi.Sec(ad, 7), KartRengi.Palet);
        Assert.Equal(KartRengi.Sec(ad, 7), KartRengi.Sec(ad, 7));
        Assert.Equal(KartRengi.Palet[7 % KartRengi.Palet.Count], KartRengi.Sec(ad, 7));
        Assert.NotEqual(KartRengi.Sec(ad, 1), KartRengi.Sec(ad, 2));
    }

    [Fact]
    public void Renk_anahtari_aile_ve_parca_adindan_olusur()
    {
        Assert.Equal("KartYesilZemin", KartRengi.Anahtar(KartRenkAilesi.Yesil, KartRenkParcasi.Zemin));
        Assert.Equal("KartLacivertYazi", KartRengi.Anahtar(KartRenkAilesi.Lacivert, KartRenkParcasi.Yazi));
    }
}
```

`Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Kontrast.cs`'te `Ikincil_yazi_renkleri_zemininde_en_az_4_5_kontrast_verir` teorisinin son `InlineData` satırından (`[InlineData("Neg", "Card")] …`) sonra:

```csharp
    [InlineData("Neg", "NegSoft")]          // kart kutusu "Son ödeme geçti" etiketi
    [InlineData("UyariMetin", "UyariZemin")] // kart kutusu "Geçiş farkını doğrulayın" etiketi
    [InlineData("Ink", "ChipBg")]           // kart kutusu "Eski takip" ve "Pasif" etiketleri
```

ve `Form_alani_kenarligi_zemininde_en_az_3_kontrast_verir` teorisinden önce:

```csharp
    /// <summary>Kart kutuları (KartKutusu): her renk ailesinde kutu yazısı zeminine karşı en az 4,5:1 (WCAG 1.4.3), doluluk
    /// çubuğunun dolgusu (Yazi) izine (Kenar) karşı en az 3:1 (WCAG 1.4.11 grafik nesne) verir. Anahtarlar KartRengi.Anahtar'dır.</summary>
    [Fact]
    public void Kart_renk_ailelerinde_yazi_zemininde_4_5_cubuk_izinde_3_kontrast_verir()
    {
        var renkler = RenkTanimi().Matches(Oku("Resources/Styles/Colors.xaml")).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
        var hatalar = new List<string>();
        foreach (var aile in Enum.GetValues<KartRenkAilesi>())
        {
            var anahtarlar = Enum.GetValues<KartRenkParcasi>().ToDictionary(p => p, p => KartRengi.Anahtar(aile, p));
            var eksik = anahtarlar.Values.Where(a => !renkler.ContainsKey(a)).ToList();
            if (eksik.Count > 0)
            { hatalar.Add($"{aile}: Colors.xaml'da tanımsız {string.Join(", ", eksik)}"); continue; }
            var yazi = KontrastOrani(renkler[anahtarlar[KartRenkParcasi.Yazi]], renkler[anahtarlar[KartRenkParcasi.Zemin]]);
            var cubuk = KontrastOrani(renkler[anahtarlar[KartRenkParcasi.Yazi]], renkler[anahtarlar[KartRenkParcasi.Kenar]]);
            if (yazi < 4.5)
                hatalar.Add($"{aile}: yazı/zemin {yazi:0.00}:1 (en az 4,5)");
            if (cubuk < 3)
                hatalar.Add($"{aile}: çubuk dolgu/iz {cubuk:0.00}:1 (en az 3)");
        }
        Assert.True(hatalar.Count == 0, string.Join("\n", hatalar));
    }

```

- [ ] **Adım 2: Testleri çalıştır, derlenmediğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartRengiTests|FullyQualifiedName~Kontrast"`
Beklenen: `error CS0103: The name 'KartRengi' does not exist in the current context` (ve `KartRenkAilesi` için CS0246).

- [ ] **Adım 3: KartRengi'ni yaz**

`Kasa.App.Core/KartRengi.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Kasa.App.Core;

/// <summary>Kart kutusunun renk ailesi; her aile Colors.xaml'da Zemin, Kenar ve Yazi anahtarıyla tanımlıdır.</summary>
public enum KartRenkAilesi { Yesil, Kirmizi, Mavi, Mor, Sari, Lacivert, Turuncu, Camgobegi, Kahve, Gri }

/// <summary>Renk ailesinin parçası: açık zemin, kenar (ve doluluk çubuğunun izi), koyu yazı (ve çubuğun dolgusu).</summary>
public enum KartRenkParcasi { Zemin, Kenar, Yazi }

/// <summary>
/// Kart kutusunun rengi kart adında geçen bankadan seçilir (tasarım 2026-09-30 §2 Renk); veritabanında renk alanı yoktur.
/// Ad tr-TR küçük harfe çevrilir, aksanlar atılır (ş→s, ğ→g, ü→u, ö→o, ç→c, ı→i) ve sözcüklere bölünür: "İş", "is" ve
/// "ISBANK" aynı sayılır. Kısa anahtarlar ("is", "teb", "halk", "vakif") yalnız tam sözcük olarak eşleşir ("visa" İş Bankası
/// sayılmaz); uzun anahtarlar sözcüklerin bitişik yazımında aranır ("Yapı Kredi" → "yapikredi"). Tanınmayan banka kart
/// kimliğine göre <see cref="Palet"/>'ten sabit bir renk alır: aynı kart her açılışta aynı rengi alır.
/// </summary>
public static class KartRengi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private sealed record Kural(string Anahtar, KartRenkAilesi Aile, bool TamSozcuk);

    private static readonly Kural[] Kurallar =
    [
        new("garanti", KartRenkAilesi.Yesil, false),
        new("akbank", KartRenkAilesi.Kirmizi, false),
        new("isbank", KartRenkAilesi.Mavi, false),
        new("is", KartRenkAilesi.Mavi, true),
        new("qnb", KartRenkAilesi.Mor, false),
        new("finansbank", KartRenkAilesi.Mor, false),
        new("vakifbank", KartRenkAilesi.Sari, false),
        new("vakif", KartRenkAilesi.Sari, true),
        new("yapikredi", KartRenkAilesi.Lacivert, false),
        new("ziraat", KartRenkAilesi.Kirmizi, false),
        new("halkbank", KartRenkAilesi.Mavi, false),
        new("halk", KartRenkAilesi.Mavi, true),
        new("denizbank", KartRenkAilesi.Mavi, false),
        new("enpara", KartRenkAilesi.Mor, false),
        new("teb", KartRenkAilesi.Yesil, true),
    ];

    /// <summary>Tanınmayan bankaların renkleri (bankalara ayrılan ailelerden ayrı).</summary>
    public static IReadOnlyList<KartRenkAilesi> Palet { get; } =
        [KartRenkAilesi.Turuncu, KartRenkAilesi.Camgobegi, KartRenkAilesi.Kahve, KartRenkAilesi.Gri];

    public static KartRenkAilesi Sec(string? ad, int kimlik)
    {
        var sozcukler = Sozcukler(ad);
        var bitisik = string.Concat(sozcukler);
        foreach (var kural in Kurallar)
            if (kural.TamSozcuk ? sozcukler.Contains(kural.Anahtar) : bitisik.Contains(kural.Anahtar, StringComparison.Ordinal))
                return kural.Aile;
        return Palet[(int)((uint)kimlik % (uint)Palet.Count)];
    }

    /// <summary>Colors.xaml anahtarı: "Kart" + aile + parça (ör. KartYesilZemin).</summary>
    public static string Anahtar(KartRenkAilesi aile, KartRenkParcasi parca) => $"Kart{aile}{parca}";

    private static string[] Sozcukler(string? ad)
    {
        if (string.IsNullOrWhiteSpace(ad))
            return [];
        var ayrik = ad.ToLower(Tr).Normalize(NormalizationForm.FormD);
        var sade = new StringBuilder(ayrik.Length);
        foreach (var c in ayrik)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sade.Append(c == 'ı' ? 'i' : char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sade.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
```

- [ ] **Adım 4: Renk anahtarlarını ekle**

`Kasa.App/Resources/Styles/Colors.xaml`'da `<Color x:Key="KoyuYesil">DarkGreen</Color>` satırından sonra, `<!-- Sık kullanılan fırçalar -->` satırından önce:

```xml

    <!-- Kart kutuları (KartKutusu; Kasa.App.Core KartRengi): banka renk ailesi başına açık zemin, kenar (doluluk çubuğunun izi)
         ve koyu yazı (çubuğun dolgusu). Yazı zeminine en az 4,5:1, çubuk dolgusu izine en az 3:1 verir; Kasa.App.Core.Tests sınar.
         Turuncu, Camgobegi, Kahve ve Gri tanınmayan bankaların paletidir. -->
    <Color x:Key="KartYesilZemin">#E6F2EA</Color>
    <Color x:Key="KartYesilKenar">#A9D1B6</Color>
    <Color x:Key="KartYesilYazi">#1D5B3A</Color>
    <Color x:Key="KartKirmiziZemin">#FBE9E7</Color>
    <Color x:Key="KartKirmiziKenar">#EDB3AB</Color>
    <Color x:Key="KartKirmiziYazi">#8E2419</Color>
    <Color x:Key="KartMaviZemin">#E6EFFA</Color>
    <Color x:Key="KartMaviKenar">#A8C4EA</Color>
    <Color x:Key="KartMaviYazi">#1B4A86</Color>
    <Color x:Key="KartMorZemin">#F0EAF8</Color>
    <Color x:Key="KartMorKenar">#C8B5E6</Color>
    <Color x:Key="KartMorYazi">#55307F</Color>
    <Color x:Key="KartSariZemin">#FBF3D9</Color>
    <Color x:Key="KartSariKenar">#E6CF7F</Color>
    <Color x:Key="KartSariYazi">#6B4E00</Color>
    <Color x:Key="KartLacivertZemin">#E7EAF4</Color>
    <Color x:Key="KartLacivertKenar">#AEB8D8</Color>
    <Color x:Key="KartLacivertYazi">#1F2D5C</Color>
    <Color x:Key="KartTuruncuZemin">#FCEEE3</Color>
    <Color x:Key="KartTuruncuKenar">#EDBE98</Color>
    <Color x:Key="KartTuruncuYazi">#8A3F0C</Color>
    <Color x:Key="KartCamgobegiZemin">#E3F3F3</Color>
    <Color x:Key="KartCamgobegiKenar">#9ED0CF</Color>
    <Color x:Key="KartCamgobegiYazi">#115A5A</Color>
    <Color x:Key="KartKahveZemin">#F3ECE6</Color>
    <Color x:Key="KartKahveKenar">#CDB8A6</Color>
    <Color x:Key="KartKahveYazi">#5E3F27</Color>
    <Color x:Key="KartGriZemin">#EEF0EC</Color>
    <Color x:Key="KartGriKenar">#C3C8BE</Color>
    <Color x:Key="KartGriYazi">#3E453B</Color>
```

(Plan yazılırken WCAG formülüyle hesaplanan oranlar: yazı/zemin 6,61–11,00; çubuk dolgu/iz 4,44–6,70; Neg/NegSoft 4,70; UyariMetin/UyariZemin 6,43.)

- [ ] **Adım 5: Testleri çalıştır, geçtiğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartRengiTests|FullyQualifiedName~Kontrast"`
Beklenen: `Passed!` ve `Failed: 0`.

- [ ] **Adım 6: Commit**

```bash
git add Kasa.App.Core/KartRengi.cs Kasa.App.Core.Tests/KartRengiTests.cs Kasa.App/Resources/Styles/Colors.xaml Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Kontrast.cs
git commit -F - <<'EOF'
feat(app): banka adından kart kutusu rengi ve renk anahtarları

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Görev 4: KartTakipSatiri kutu özellikleri (+ TimeProvider)

**Dosyalar:**
- Değiştir: `Kasa.App.Core/FinansTakipModelleri.cs:120-126`, `Kasa.App.Core/KartTakipViewModel.cs`
- Test: `Kasa.App.Core.Tests/KartTakipSatiriTests.cs`

- [ ] **Adım 1: Başarısız testi yaz**

`Kasa.App.Core.Tests/KartTakipSatiriTests.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kart kutusunun içeriği KartTakipSatiri'nın hesaplanmış özelliklerinden gelir (tasarım 2026-09-30 §2 Kart kutuları):
/// doluluk oranı, borç ve limit metni, ilk açık ekstrenin son ödemesi, en çok iki durum etiketi (önem sırasıyla) ve renk.
/// "Son ödeme geçti" kuralının günü TimeProvider'dan gelir; testte sabittir.</summary>
public class KartTakipSatiriTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 21);

    private static KartTakipDto Kart(decimal borc = 250, decimal limit = 1000, bool yeniTakip = true, bool aktif = true,
        KartGecisDto? gecis = null, KartEkstreDto[]? ekstreler = null)
        => new(1, 1, "Garanti Bonus", yeniTakip, aktif, null, 10, 20, limit, borc, 0, ekstreler ?? [], [], [], null, gecis);

    private static KartEkstreDto Ekstre(DateOnly sonOdeme, decimal kalan) => new(7, sonOdeme.AddDays(-10), sonOdeme, kalan, 0, kalan, null);

    private static KartTakipSatiri Satir(KartTakipDto kart, DateOnly? bugun = null) => new(kart, new IslemEditorTests.SabitZaman(bugun ?? Bugun));

    [Theory]
    [InlineData(250, 1000, 0.25)]
    [InlineData(1000, 1000, 1.0)]
    [InlineData(1500, 1000, 1.0)]   // borç limitten büyük: çubuk dolu
    [InlineData(-50, 1000, 0.0)]    // kart alacaklı: çubuk boş
    public void Doluluk_orani_borcun_limite_orani_0_ile_1_arasinda(decimal borc, decimal limit, double beklenen)
    {
        var satir = Satir(Kart(borc, limit));
        Assert.Equal(beklenen, satir.Doluluk);
        Assert.True(satir.DolulukVar);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Limit_sifir_ya_da_eksiyse_cubuk_yok(decimal limit)
    {
        var satir = Satir(Kart(limit: limit));
        Assert.Null(satir.Doluluk);
        Assert.False(satir.DolulukVar);
    }

    [Fact]
    public void Borc_ve_limit_metni_tl_bicimindedir()
    {
        var satir = Satir(Kart(borc: 12500, limit: 40000));
        Assert.Equal("12.500,00 ₺", satir.BorcMetni);
        Assert.Equal("Limit 40.000,00 ₺", satir.LimitMetni);
    }

    [Fact]
    public void Son_odeme_metni_kalan_borcu_olan_en_yakin_ekstreden_gelir()
    {
        var satir = Satir(Kart(ekstreler: [Ekstre(new(2026, 10, 20), 50), Ekstre(new(2026, 10, 5), 30), Ekstre(new(2026, 9, 5), 0)]));
        Assert.Equal("Son ödeme 05.10.2026", satir.SonOdemeMetni);
        Assert.Equal(new DateOnly(2026, 10, 5), satir.IlkAcikEkstre!.SonOdemeTarihi);
    }

    [Fact]
    public void Acik_ekstre_yoksa_son_odeme_yazilmaz()
    {
        Assert.Null(Satir(Kart(ekstreler: [Ekstre(new(2026, 9, 5), 0)])).SonOdemeMetni);
        Assert.Null(Satir(Kart()).SonOdemeMetni);
    }

    [Fact]
    public void Son_odeme_gecti_kalan_borclu_ekstrenin_son_odemesi_bugunden_onceyse()
    {
        var kart = Kart(ekstreler: [Ekstre(new(2026, 9, 20), 10)]);
        Assert.True(Satir(kart, new DateOnly(2026, 9, 21)).SonOdemeGecti);
        Assert.False(Satir(kart, new DateOnly(2026, 9, 20)).SonOdemeGecti);   // son ödeme günü henüz geçmedi
        Assert.False(Satir(Kart(ekstreler: [Ekstre(new(2026, 9, 20), 0)])).SonOdemeGecti);
    }

    [Fact]
    public void Etiketler_onem_sirasiyla_en_cok_iki_tanedir()
    {
        var gecis = new KartGecisDto("EtkiTarihi", null, null, TahminiKasaFarki: 120);
        var satir = Satir(Kart(yeniTakip: false, aktif: false, gecis: gecis, ekstreler: [Ekstre(new(2026, 9, 20), 10)]));
        Assert.Equal(new[] { "Son ödeme geçti", "Geçiş farkını doğrulayın" }, satir.Etiketler.Select(e => e.Metin));
        Assert.Equal(new[] { KartEtiketTuru.Tehlike, KartEtiketTuru.Uyari }, satir.Etiketler.Select(e => e.Tur));
    }

    [Fact]
    public void Eski_takip_ve_pasif_etiketleri_notr()
    {
        var satir = Satir(Kart(yeniTakip: false, aktif: false));
        Assert.Equal(new[] { "Eski takip", "Pasif" }, satir.Etiketler.Select(e => e.Metin));
        Assert.All(satir.Etiketler, e => Assert.Equal(KartEtiketTuru.Notr, e.Tur));
        Assert.Empty(Satir(Kart()).Etiketler);
    }

    [Fact]
    public void Renk_kart_adindaki_bankadan_gelir()
        => Assert.Equal(KartRenkAilesi.Yesil, Satir(Kart()).Renk);
}
```

- [ ] **Adım 2: Testi çalıştır, derlenmediğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartTakipSatiriTests"`
Beklenen: `error CS1729: 'KartTakipSatiri' does not contain a constructor that takes 2 arguments` (ve `KartEtiketTuru` için CS0103).

- [ ] **Adım 3: KartTakipSatiri'nı genişlet**

`Kasa.App.Core/FinansTakipModelleri.cs`'te bugünkü kayıt:

```csharp
public record KartTakipSatiri(KartTakipDto Veri)
{
    // Web kart listesindeki "Geçiş farkını doğrulayın" rozetinin karşılığı.
    public string Baslik => Veri.Ad + (!Veri.Aktif ? " · pasif" : "") + (!Veri.YeniTakip ? " · eski takip" : "") + (Veri.Gecis is { TahminiKasaFarki: not 0 } ? " · geçiş farkını doğrulayın" : "");
    public string Ozet => $"Kart borcu {Bicim.Tl(Veri.Borc)} ₺ · açık ekstre {Bicim.Tl(Veri.EkstreBorc)} ₺ · limit {Bicim.Tl(Veri.Limit)} ₺" +
        (Veri.Ekstreler.Where(e => e.Kalan > 0).OrderBy(e => e.SonOdemeTarihi).FirstOrDefault() is { } e ? $"\nİlk açık ekstrenin son ödemesi: {e.SonOdemeTarihi:dd.MM.yyyy}" : "");
}
```

yerine:

```csharp
/// <summary>Kart satırı: kart ayrıntısının özeti ve kart kutusunun içeriği (KartKutusu, tasarım 2026-09-30 §2).</summary>
/// <param name="Zaman">"Son ödeme geçti" kuralının saati (yerel gün); verilmezse sistem saati. Model kendi saatini verir,
/// testler sabit saat verir.</param>
public record KartTakipSatiri(KartTakipDto Veri, TimeProvider? Zaman = null)
{
    // Web kart listesindeki "Geçiş farkını doğrulayın" rozetinin karşılığı.
    public string Baslik => Veri.Ad + (!Veri.Aktif ? " · pasif" : "") + (!Veri.YeniTakip ? " · eski takip" : "") + (Veri.Gecis is { TahminiKasaFarki: not 0 } ? " · geçiş farkını doğrulayın" : "");
    public string Ozet => $"Kart borcu {Bicim.Tl(Veri.Borc)} ₺ · açık ekstre {Bicim.Tl(Veri.EkstreBorc)} ₺ · limit {Bicim.Tl(Veri.Limit)} ₺" +
        (IlkAcikEkstre is { } e ? $"\nİlk açık ekstrenin son ödemesi: {e.SonOdemeTarihi:dd.MM.yyyy}" : "");

    private DateOnly Bugun => DateOnly.FromDateTime((Zaman ?? TimeProvider.System).GetLocalNow().DateTime);

    /// <summary>Kalan borcu olan, son ödemesi en yakın ekstre; yoksa null.</summary>
    public KartEkstreDto? IlkAcikEkstre => Veri.Ekstreler.Where(e => e.Kalan > 0).OrderBy(e => e.SonOdemeTarihi).FirstOrDefault();
    public string BorcMetni => $"{Bicim.Tl(Veri.Borc)} ₺";
    public string LimitMetni => $"Limit {Bicim.Tl(Veri.Limit)} ₺";
    /// <summary>İlk açık ekstrenin son ödemesi; açık ekstre yoksa null (kutuda yazılmaz).</summary>
    public string? SonOdemeMetni => IlkAcikEkstre is { } e ? $"Son ödeme {e.SonOdemeTarihi:dd.MM.yyyy}" : null;
    /// <summary>Limit doluluğu: borç ÷ limit, 0 ile 1 arasına sıkıştırılır; limit 0 (ya da eksi) ise null ve çubuk gösterilmez.</summary>
    public double? Doluluk => Veri.Limit > 0 ? (double)Math.Clamp(Veri.Borc / Veri.Limit, 0m, 1m) : null;
    public bool DolulukVar => Doluluk is not null;
    /// <summary>Kalan borcu olan bir ekstrenin son ödeme tarihi bugünden önce.</summary>
    public bool SonOdemeGecti => Veri.Ekstreler.Any(e => e.Kalan > 0 && e.SonOdemeTarihi < Bugun);

    /// <summary>Kutudaki durum etiketleri: önem sırasıyla en çok iki tane.</summary>
    public IReadOnlyList<KartEtiketi> Etiketler
    {
        get
        {
            var etiketler = new List<KartEtiketi>();
            if (SonOdemeGecti)
                etiketler.Add(new("Son ödeme geçti", KartEtiketTuru.Tehlike));
            if (Veri.Gecis is { TahminiKasaFarki: not 0 })
                etiketler.Add(new("Geçiş farkını doğrulayın", KartEtiketTuru.Uyari));
            if (!Veri.YeniTakip)
                etiketler.Add(new("Eski takip", KartEtiketTuru.Notr));
            if (!Veri.Aktif)
                etiketler.Add(new("Pasif", KartEtiketTuru.Notr));
            return etiketler.Take(2).ToList();
        }
    }

    public KartRenkAilesi Renk => KartRengi.Sec(Veri.Ad, Veri.Id);
}

/// <summary>Kart kutusu durum etiketinin türü: rengini belirler (Tehlike NegSoft/Neg, Uyari UyariZemin/UyariMetin, Notr ChipBg/Ink).</summary>
public enum KartEtiketTuru { Tehlike, Uyari, Notr }

public sealed record KartEtiketi(string Metin, KartEtiketTuru Tur);
```

- [ ] **Adım 4: Modelin kendi saatini satırlara ver**

`Kasa.App.Core/KartTakipViewModel.cs`'te:

```csharp
public partial class KartTakipViewModel(IFinansTakipApi api, IKasaApi finans, AuthViewModel auth, IBenzerKayitApi? benzerlikApi = null, IKasaKontrolApi? kontrolApi = null) : OturumluViewModel(auth)
{
```
→
```csharp
/// <param name="zaman">Kart kutularındaki "Son ödeme geçti" kuralının saati (yerel gün); verilmezse sistem saati. DI'da kayıtlı
/// değildir (isteğe bağlı parametre varsayılana düşer); testler sabit saat verir.</param>
public partial class KartTakipViewModel(IFinansTakipApi api, IKasaApi finans, AuthViewModel auth, IBenzerKayitApi? benzerlikApi = null,
    IKasaKontrolApi? kontrolApi = null, TimeProvider? zaman = null) : OturumluViewModel(auth)
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
```

`YukleAsync` içinde:

```csharp
        TakipMetni.Doldur(Kartlar, kartlar.Select(k => new KartTakipSatiri(k)));
        if (Secili is { } eski)
        { var mevcut = kartlar.FirstOrDefault(k => k.Id == eski.Id); if (mevcut is not null) Sec(new(mevcut)); else Yeni(); }
```
→
```csharp
        TakipMetni.Doldur(Kartlar, kartlar.Select(k => new KartTakipSatiri(k, _zaman)));
        if (Secili is { } eski)
        { var mevcut = kartlar.FirstOrDefault(k => k.Id == eski.Id); if (mevcut is not null) Sec(new(mevcut, _zaman)); else Yeni(); }
```

`Uygula` içinde:

```csharp
        if (eski is not null)
            Kartlar[Kartlar.IndexOf(eski)] = new(sonuc);
        else
            Kartlar.Add(new(sonuc));
```
→
```csharp
        if (eski is not null)
            Kartlar[Kartlar.IndexOf(eski)] = new(sonuc, _zaman);
        else
            Kartlar.Add(new(sonuc, _zaman));
```

- [ ] **Adım 5: Testleri çalıştır, geçtiğini gör (mevcut kart testleri dahil)**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartTakipSatiriTests|FullyQualifiedName~FinansTakipTests|FullyQualifiedName~TakipKomutlariTests|FullyQualifiedName~MauiKayitTutarliligiTests"`
Beklenen: `Passed!` ve `Failed: 0` (`Kayitli_viewmodel_bagimliliklari_kayitli` TimeProvider'ı çerçeve türü olarak kabul eder).

- [ ] **Adım 6: Commit**

```bash
git add Kasa.App.Core/FinansTakipModelleri.cs Kasa.App.Core/KartTakipViewModel.cs Kasa.App.Core.Tests/KartTakipSatiriTests.cs
git commit -F - <<'EOF'
feat(app): kart kutusu içeriği KartTakipSatiri'nda (doluluk, son ödeme, etiketler, renk)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Görev 5: KartIzgarasiHesabi (yerleşim hesabı)

**Dosyalar:**
- Oluştur: `Kasa.App.Core/KartIzgarasiHesabi.cs`
- Test: `Kasa.App.Core.Tests/KartIzgarasiHesabiTests.cs`

- [ ] **Adım 1: Başarısız testi yaz**

`Kasa.App.Core.Tests/KartIzgarasiHesabiTests.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Kart ızgarası (tasarım 2026-09-30 §2): satırdaki kutu sayısı genişliğe göre değişir, kutu en az 220 px'tir;
/// ayrıntı açık kutunun bulunduğu satırın hemen altında tam genişlikte yer alır, sonraki satırlar aşağı kayar.</summary>
public class KartIzgarasiHesabiTests
{
    private const double Aralik = 16;

    [Theory]
    [InlineData(1136, 4)]
    [InlineData(700, 3)]
    [InlineData(456, 2)]
    [InlineData(455, 1)]
    [InlineData(220, 1)]
    [InlineData(200, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void Sutun_sayisi_en_az_220_piksellik_kutu_sigdirir(double genislik, int beklenen)
        => Assert.Equal(beklenen, KartIzgarasiHesabi.Olcu(genislik, Aralik).Sutun);

    [Fact]
    public void Kutular_satira_esit_genislikte_yayilir()
    {
        var olcu = KartIzgarasiHesabi.Olcu(1136, Aralik);
        Assert.Equal(272, olcu.KutuGenisligi);
        Assert.Equal(1136, olcu.Genislik);
        Assert.Equal(220, KartIzgarasiHesabi.Olcu(double.PositiveInfinity, Aralik).Genislik);
        Assert.Equal(200, KartIzgarasiHesabi.Olcu(200, Aralik).KutuGenisligi);
    }

    [Fact]
    public void Acik_kutu_yokken_kutular_satir_satir_dizilir()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [150, 150, 150, 150, 150], -1, 300);
        Assert.Equal(4, y.Sutun);
        Assert.Equal(new[] { 0d, 288, 576, 864, 0 }, y.Kutular.Select(k => k.X));
        Assert.Equal(new[] { 0d, 0, 0, 0, 166 }, y.Kutular.Select(k => k.Y));
        Assert.All(y.Kutular, k => Assert.Equal(272, k.Genislik));
        Assert.Null(y.Ayrinti);
        Assert.Equal(316, y.Yukseklik);
    }

    [Fact]
    public void Ayrinti_acik_kutunun_satirindan_sonra_tam_genislikte_yer_alir()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [150, 150, 150, 150, 150], 1, 300);
        Assert.Equal(new Dikdortgen(0, 166, 1136, 300), y.Ayrinti!.Value);
        Assert.Equal(482, y.Kutular[4].Y);   // ikinci satır ayrıntının altına kayar
        Assert.Equal(632, y.Yukseklik);
    }

    [Fact]
    public void Son_satirdaki_kutu_acilinca_ayrinti_en_alta_gelir()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [150, 150, 150, 150, 150], 4, 300);
        Assert.Equal(166, y.Kutular[4].Y);
        Assert.Equal(new Dikdortgen(0, 332, 1136, 300), y.Ayrinti!.Value);
        Assert.Equal(632, y.Yukseklik);
    }

    [Fact]
    public void Satir_yuksekligi_satirdaki_en_yuksek_kutudur()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [100, 180, 120, 90, 100], -1, 0);
        Assert.All(y.Kutular.Take(4), k => Assert.Equal(180, k.Yukseklik));
        Assert.Equal(196, y.Kutular[4].Y);
        Assert.Equal(100, y.Kutular[4].Yukseklik);
    }

    [Fact]
    public void Kutu_yoksa_yukseklik_sifir()
    {
        var y = KartIzgarasiHesabi.Hesapla(1136, Aralik, [], 0, 300);
        Assert.Empty(y.Kutular);
        Assert.Null(y.Ayrinti);
        Assert.Equal(0, y.Yukseklik);
    }

    [Theory]
    [InlineData(7, false, 1)]
    [InlineData(null, true, 3)]   // yeni kart formu: "Yeni kart ekle" kutusu (son kutu) açık
    [InlineData(42, false, -1)]   // listede olmayan kart
    [InlineData(null, false, -1)]
    public void Acik_kutu_sirasi_kimlikten_ya_da_yeni_kart_formundan_gelir(int? acikKimlik, bool yeniAcik, int beklenen)
        => Assert.Equal(beklenen, KartIzgarasiHesabi.AcikIndeks([5, 7, 9], acikKimlik, yeniAcik));
}
```

- [ ] **Adım 2: Testi çalıştır, derlenmediğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartIzgarasiHesabiTests"`
Beklenen: `error CS0103: The name 'KartIzgarasiHesabi' does not exist in the current context`

- [ ] **Adım 3: Hesabı yaz**

`Kasa.App.Core/KartIzgarasiHesabi.cs`:

```csharp
namespace Kasa.App.Core;

/// <summary>Izgaradaki bir öğenin yeri (ızgaranın iç alanına göre, piksel).</summary>
public readonly record struct Dikdortgen(double X, double Y, double Genislik, double Yukseklik);

/// <summary>Izgaranın yerleşimi: sütun sayısı, kullanılan genişlik, kutu genişliği, görünen kutuların ve (açıksa) ayrıntının
/// dikdörtgenleri, toplam yükseklik.</summary>
public sealed record KartIzgarasiYerlesimi(int Sutun, double Genislik, double KutuGenisligi, IReadOnlyList<Dikdortgen> Kutular,
    Dikdortgen? Ayrinti, double Yukseklik);

/// <summary>
/// Kart ızgarasının saf yerleşim hesabı (Kasa.App/Controls/KartIzgarasi yalnız bunu kullanır; Windows'tan bağımsız sınanır).
/// Satırdaki kutu sayısı genişliğe sığan en çok kutudur (kutu en az <see cref="EnAzKutuGenisligi"/>); kutular satıra eşit
/// genişlikte yayılır, satırın yüksekliği satırdaki en yüksek kutudur ve satırdaki bütün kutular o yüksekliği alır. Açık kutu
/// varsa ayrıntı, o kutunun satırından bir aralık sonra tam genişlikte yer alır; sonraki satırlar onun altına kayar.
/// </summary>
public static class KartIzgarasiHesabi
{
    public const double EnAzKutuGenisligi = 220;

    /// <summary>Genişlik ölçüsü: sütun sayısı, kullanılan genişlik (sonsuz ya da sıfırsa tek kutu genişliği) ve kutu genişliği.</summary>
    public static (int Sutun, double Genislik, double KutuGenisligi) Olcu(double genislik, double aralik)
    {
        var g = double.IsFinite(genislik) && genislik > 0 ? genislik : EnAzKutuGenisligi;
        var sutun = Math.Max(1, (int)Math.Floor((g + aralik) / (EnAzKutuGenisligi + aralik)));
        return (sutun, g, (g - (sutun - 1) * aralik) / sutun);
    }

    /// <param name="kutuYukseklikleri">Görünen kutuların ölçülmüş yükseklikleri, sırayla.</param>
    /// <param name="acikIndeks">Açık kutunun sırası (<see cref="AcikIndeks"/>); yoksa -1.</param>
    public static KartIzgarasiYerlesimi Hesapla(double genislik, double aralik, IReadOnlyList<double> kutuYukseklikleri, int acikIndeks,
        double ayrintiYuksekligi)
    {
        var (sutun, g, kutuGenisligi) = Olcu(genislik, aralik);
        var kutular = new Dikdortgen[kutuYukseklikleri.Count];
        Dikdortgen? ayrinti = null;
        var y = 0d;
        for (var bas = 0; bas < kutular.Length; bas += sutun)
        {
            if (bas > 0)
                y += aralik;
            var son = Math.Min(bas + sutun, kutular.Length);
            var satir = 0d;
            for (var i = bas; i < son; i++)
                satir = Math.Max(satir, kutuYukseklikleri[i]);
            for (var i = bas; i < son; i++)
                kutular[i] = new Dikdortgen((i - bas) * (kutuGenisligi + aralik), y, kutuGenisligi, satir);
            y += satir;
            if (acikIndeks >= bas && acikIndeks < son)
            {
                y += aralik;
                ayrinti = new Dikdortgen(0, y, g, ayrintiYuksekligi);
                y += ayrintiYuksekligi;
            }
        }
        return new KartIzgarasiYerlesimi(sutun, g, kutuGenisligi, kutular, ayrinti, y);
    }

    /// <summary>Açık kutunun sırası: yeni kart formu açıksa son kutu ("Yeni kart ekle", sırası kart sayısı), değilse açık kartın
    /// sırası; açık kart listede yoksa ya da yoksa -1.</summary>
    public static int AcikIndeks(IReadOnlyList<int> kartKimlikleri, int? acikKartId, bool yeniKartAcik)
    {
        if (yeniKartAcik)
            return kartKimlikleri.Count;
        if (acikKartId is not { } kimlik)
            return -1;
        for (var i = 0; i < kartKimlikleri.Count; i++)
            if (kartKimlikleri[i] == kimlik)
                return i;
        return -1;
    }
}
```

- [ ] **Adım 4: Testleri çalıştır, geçtiğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartIzgarasiHesabiTests"`
Beklenen: `Passed!` ve `Failed: 0`.

- [ ] **Adım 5: Commit**

```bash
git add Kasa.App.Core/KartIzgarasiHesabi.cs Kasa.App.Core.Tests/KartIzgarasiHesabiTests.cs
git commit -F - <<'EOF'
feat(app): kart ızgarasının saf yerleşim hesabı (KartIzgarasiHesabi)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Görev 6: KartKutusu, YeniKartKutusu ve KartIzgarasi bileşenleri

**Dosyalar:**
- Oluştur: `Kasa.App/Controls/KartKutusu.cs`, `Kasa.App/Controls/YeniKartKutusu.cs`, `Kasa.App/Controls/KartIzgarasi.cs`
- Test: `Kasa.App.Core.Tests/Donusturuculer/KartKutusuTests.cs`

(Kasa.App.Core.Tests `..\Kasa.App\Controls\*.cs` dosyalarını kendisi derler: yeni bileşenler Windows'a özgü tür kullanmaz ve MAUI örtük using'lerine dayanmaz.)

- [ ] **Adım 1: Başarısız testi yaz**

`Kasa.App.Core.Tests/Donusturuculer/KartKutusuTests.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.App.Controls;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Kart kutusu ve kart ızgarası bileşenleri gerçek uygulama kaynaklarıyla (GorunumOrtami): kutu satırın bilgilerini
/// banka renginde gösterir, tıklama yüzeyi adlı şeffaf düğmedir; ızgara kutuları kaynaktan kurar, açık kutuyu işaretler,
/// ayrıntıyı yalnız bir kutu açıkken gösterir. Yerleşimin kendisi KartIzgarasiHesabiTests'te sınanır.</summary>
public class KartKutusuTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);

    private static KartTakipSatiri Satir(int id, string ad, decimal borc = 12500, decimal limit = 40000, bool yeniTakip = true,
        KartEkstreDto[]? ekstreler = null)
        => new(new KartTakipDto(id, 1, ad, yeniTakip, true, null, 10, 20, limit, borc, 0, ekstreler ?? [], [], []),
            new IslemEditorTests.SabitZaman(Bugun));

    private static Color Renk(string anahtar) => (Color)Application.Current!.Resources[anahtar];

    [Fact]
    public void Kutu_satirin_bilgilerini_banka_renginde_gosterir()
    {
        GorunumOrtami.Kur();
        var satir = Satir(3, "Garanti Bonus", yeniTakip: false, ekstreler: [new KartEkstreDto(7, new(2026, 9, 10), new(2026, 9, 20), 12500, 0, 12500, null)]);
        var kutu = new KartKutusu { BindingContext = satir };
        var metinler = kutu.GetVisualTreeDescendants().OfType<Label>().Where(l => l.IsVisible).Select(l => l.Text).Order().ToList();
        var beklenen = new[] { "Garanti Bonus", "Kart borcu", "12.500,00 ₺", "Limit 40.000,00 ₺", "Son ödeme 20.09.2026", "Son ödeme geçti", "Eski takip" };
        Assert.Equal(beklenen.Order(), metinler);
        Assert.Same(satir, kutu.Satir);
        Assert.Equal(Renk("KartYesilZemin"), kutu.BackgroundColor);
        Assert.All(kutu.GetVisualTreeDescendants().OfType<Label>().Where(l => l.Text is "Garanti Bonus" or "12.500,00 ₺"),
            l => Assert.Equal(Renk("KartYesilYazi"), l.TextColor));
        var dugme = kutu.GetVisualTreeDescendants().OfType<Button>().Single();
        Assert.Equal("Garanti Bonus kartı", SemanticProperties.GetDescription(dugme));
    }

    [Fact]
    public void Doluluk_cubugu_orani_gosterir_limit_sifirsa_gizlenir()
    {
        GorunumOrtami.Kur();
        static Grid Cubuk(KartKutusu k) => k.GetVisualTreeDescendants().OfType<Grid>().Single(g => g.Children.OfType<BoxView>().Count() == 2);
        var kutu = new KartKutusu { BindingContext = Satir(3, "Garanti Bonus") };
        Assert.True(Cubuk(kutu).IsVisible);
        Assert.Equal(0.3125, Cubuk(kutu).ColumnDefinitions[0].Width.Value);
        Assert.Equal(0.6875, Cubuk(kutu).ColumnDefinitions[1].Width.Value);
        Assert.False(Cubuk(new KartKutusu { BindingContext = Satir(4, "Akbank", limit: 0) }).IsVisible);
    }

    [Fact]
    public void Secili_kutunun_kenari_kalinlasir_ve_yazi_rengini_alir()
    {
        GorunumOrtami.Kur();
        var kutu = new KartKutusu { BindingContext = Satir(3, "Garanti Bonus") };
        Assert.Equal(1, kutu.StrokeThickness);
        kutu.Secili = true;
        Assert.Equal(2.5, kutu.StrokeThickness);
        Assert.Equal(Renk("KartYesilYazi"), Assert.IsType<SolidColorBrush>(kutu.Stroke).Color);
    }

    [Fact]
    public void Izgara_kutulari_kaynaktan_kurar_yeni_kart_kutusu_ve_ayrinti_sondadir()
    {
        GorunumOrtami.Kur();
        var kartlar = new ObservableCollection<KartTakipSatiri> { Satir(5, "Garanti"), Satir(7, "Akbank") };
        var ayrinti = new Label();
        var izgara = new KartIzgarasi { ItemsSource = kartlar, Ayrinti = ayrinti, YeniGorunur = true };
        Assert.Equal(2, izgara.Kutular.Count);
        Assert.Equal(new object[] { izgara.Kutular[0], izgara.Kutular[1], izgara.YeniKutusu, ayrinti }, izgara.Cast<object>().ToArray());
        kartlar.Add(Satir(9, "Ziraat"));
        Assert.Equal(3, izgara.Kutular.Count);
        Assert.Same(izgara.YeniKutusu, izgara[3]);
        Assert.Same(ayrinti, izgara[4]);
        Assert.False(ayrinti.IsVisible);   // açık kart yok
    }

    [Fact]
    public void Acik_kartin_kutusu_isaretlenir_ve_ayrinti_gorunur()
    {
        GorunumOrtami.Kur();
        var ayrinti = new Label();
        var izgara = new KartIzgarasi { ItemsSource = new[] { Satir(5, "Garanti"), Satir(7, "Akbank") }, Ayrinti = ayrinti, YeniGorunur = true };
        izgara.AcikKartId = 7;
        Assert.Equal(1, izgara.AcikIndeks);
        Assert.Equal(new[] { false, true }, izgara.Kutular.Select(k => k.Secili));
        Assert.True(ayrinti.IsVisible);
        izgara.AcikKartId = null;
        Assert.Equal(-1, izgara.AcikIndeks);
        Assert.DoesNotContain(izgara.Kutular, k => k.Secili);
        Assert.False(ayrinti.IsVisible);
    }

    [Fact]
    public void Yeni_kart_formu_acikken_yeni_kart_kutusu_acik_izleyicide_yeni_kart_kutusu_yok()
    {
        GorunumOrtami.Kur();
        var izgara = new KartIzgarasi { ItemsSource = new[] { Satir(5, "Garanti"), Satir(7, "Akbank") }, Ayrinti = new Label(), YeniGorunur = true, YeniAcik = true };
        Assert.Equal(2, izgara.AcikIndeks);
        Assert.True(izgara.YeniKutusu.Secili);
        izgara.YeniGorunur = false;
        Assert.False(izgara.YeniKutusu.IsVisible);
        Assert.Equal(-1, izgara.AcikIndeks);
    }

    [Fact]
    public void Kutuya_ve_yeni_kart_kutusuna_dokunmak_komutlari_calistirir()
    {
        GorunumOrtami.Kur();
        var kartlar = new[] { Satir(5, "Garanti"), Satir(7, "Akbank") };
        KartTakipSatiri? secilen = null;
        var yeni = 0;
        var izgara = new KartIzgarasi
        {
            ItemsSource = kartlar,
            SecCommand = new RelayCommand<KartTakipSatiri>(s => secilen = s),
            YeniCommand = new RelayCommand(() => yeni++),
            YeniGorunur = true,
        };
        var dugme = izgara.Kutular[1].GetVisualTreeDescendants().OfType<Button>().Single();
        dugme.Command!.Execute(dugme.CommandParameter);
        Assert.Same(kartlar[1], secilen);
        var yeniDugme = izgara.YeniKutusu.GetVisualTreeDescendants().OfType<Button>().Single();
        Assert.Equal("Yeni kart ekle", SemanticProperties.GetDescription(yeniDugme));
        yeniDugme.Command!.Execute(null);
        Assert.Equal(1, yeni);
    }
}
```

- [ ] **Adım 2: Testi çalıştır, derlenmediğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartKutusuTests"`
Beklenen: `error CS0246: The type or namespace name 'KartKutusu' could not be found`

- [ ] **Adım 3: KartKutusu'nu yaz**

`Kasa.App/Controls/KartKutusu.cs`:

```csharp
using System.Windows.Input;
using Kasa.App.Core;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Kasa.App.Controls;

/// <summary>Kredi kartı kutusu (Kartlar ekranı, tasarım 2026-09-30 §2): banka rengindeki zeminde kart adı, "Kart borcu" ve borç,
/// limit doluluk çubuğu, limit ve ilk açık ekstrenin son ödemesi, en çok iki durum etiketi. İçerik bağlamdaki
/// <see cref="KartTakipSatiri"/>'ndan gelir; satır değişince KartIzgarasi kutuyu yeniden kurar. Tıklama ve klavye yüzeyi alttaki
/// şeffaf düğmedir (SeffafDugme): Tab ile odaklanır, UI Otomasyonu'nda "{kart adı} kartı" adlı düğmedir; yazılar girdiyi geçirir.</summary>
public class KartKutusu : Border
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(KartKutusu),
        propertyChanged: (b, _, y) => ((KartKutusu)b)._dugme.Command = (ICommand?)y);
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(KartKutusu),
        propertyChanged: (b, _, y) => ((KartKutusu)b)._dugme.CommandParameter = y);

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private readonly Button _dugme;
    private readonly Label _ad, _borcEtiketi, _borc, _limit, _sonOdeme;
    private readonly Grid _cubuk;
    private readonly BoxView _iz, _dolu;
    private readonly HorizontalStackLayout _etiketler;
    private Color? _kenar, _yazi;
    private bool _secili;

    public KartKutusu()
    {
        StrokeShape = new RoundRectangle { CornerRadius = 14 };
        StrokeThickness = 1;
        _dugme = new Button { Style = (Style)Application.Current!.Resources["SeffafDugme"] };
        _ad = new Label { FontAttributes = FontAttributes.Bold, FontSize = 15, LineBreakMode = LineBreakMode.TailTruncation };
        _borcEtiketi = new Label { Text = "Kart borcu", FontSize = 12 };
        _borc = new Label { FontAttributes = FontAttributes.Bold, FontSize = 22 };
        _iz = new BoxView { HeightRequest = 6, CornerRadius = 3 };
        _dolu = new BoxView { HeightRequest = 6, CornerRadius = 3 };
        _cubuk = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, Margin = new Thickness(0, 2) };
        _cubuk.Add(_iz);
        Grid.SetColumnSpan(_iz, 2);
        _cubuk.Add(_dolu);
        _limit = new Label { FontSize = 12 };
        _sonOdeme = new Label { FontSize = 12, HorizontalTextAlignment = TextAlignment.End };
        var alt = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        alt.Add(_limit);
        alt.Add(_sonOdeme, 1);
        _etiketler = new HorizontalStackLayout { Spacing = 6 };
        var icerik = new VerticalStackLayout
        {
            Padding = new Thickness(16, 14),
            Spacing = 6,
            InputTransparent = true,
            Children = { _ad, _borcEtiketi, _borc, _cubuk, alt, _etiketler },
        };
        var kok = new Grid();
        kok.Add(_dugme);
        kok.Add(icerik);
        Content = kok;
    }

    /// <summary>Kutunun gösterdiği satır (bağlam).</summary>
    public KartTakipSatiri? Satir { get; private set; }

    /// <summary>Açık kartın kutusu: kenar kalınlaşır ve yazı rengini alır.</summary>
    public bool Secili
    {
        get => _secili;
        set
        {
            _secili = value;
            Cerceve();
        }
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is KartTakipSatiri satir)
            Doldur(satir);
    }

    private void Doldur(KartTakipSatiri satir)
    {
        Satir = satir;
        BackgroundColor = Renk(KartRengi.Anahtar(satir.Renk, KartRenkParcasi.Zemin));
        _kenar = Renk(KartRengi.Anahtar(satir.Renk, KartRenkParcasi.Kenar));
        _yazi = Renk(KartRengi.Anahtar(satir.Renk, KartRenkParcasi.Yazi));
        foreach (var etiket in new[] { _ad, _borcEtiketi, _borc, _limit, _sonOdeme })
            etiket.TextColor = _yazi;
        _ad.Text = satir.Veri.Ad;
        _borc.Text = satir.BorcMetni;
        _limit.Text = satir.LimitMetni;
        _sonOdeme.Text = satir.SonOdemeMetni;
        _sonOdeme.IsVisible = satir.SonOdemeMetni is not null;
        var oran = satir.Doluluk ?? 0;
        _cubuk.IsVisible = satir.DolulukVar;
        _cubuk.ColumnDefinitions[0].Width = new GridLength(oran, GridUnitType.Star);
        _cubuk.ColumnDefinitions[1].Width = new GridLength(1 - oran, GridUnitType.Star);
        _iz.Color = _kenar;
        _dolu.Color = _yazi;
        _etiketler.Clear();
        foreach (var etiket in satir.Etiketler)
            _etiketler.Add(Etiket(etiket));
        _etiketler.IsVisible = satir.Etiketler.Count > 0;
        SemanticProperties.SetDescription(_dugme, $"{satir.Veri.Ad} kartı");
        Cerceve();
    }

    private void Cerceve()
    {
        if (_kenar is null || _yazi is null)
            return;
        Stroke = new SolidColorBrush(_secili ? _yazi : _kenar);
        StrokeThickness = _secili ? 2.5 : 1;
    }

    /// <summary>Durum etiketi (StatusChip): tehlike NegSoft/Neg, uyarı UyariZemin/UyariMetin, nötr ChipBg/Ink (kontrast testli).</summary>
    private static Border Etiket(KartEtiketi etiket)
    {
        var (zemin, yazi) = etiket.Tur switch
        {
            KartEtiketTuru.Tehlike => ((Color)Application.Current!.Resources["NegSoft"], (Color)Application.Current!.Resources["Neg"]),
            KartEtiketTuru.Uyari => ((Color)Application.Current!.Resources["UyariZemin"], (Color)Application.Current!.Resources["UyariMetin"]),
            _ => ((Color)Application.Current!.Resources["ChipBg"], (Color)Application.Current!.Resources["Ink"]),
        };
        return new Border
        {
            Style = (Style)Application.Current!.Resources["StatusChip"],
            BackgroundColor = zemin,
            Content = new Label { Text = etiket.Metin, Style = (Style)Application.Current!.Resources["LblStatusChip"], TextColor = yazi },
        };
    }

    /// <summary>Renk ailesi anahtarı (KartRengi.Anahtar); anahtarların varlığı ve kontrastı Kasa.App.Core.Tests'te sınanır.</summary>
    private static Color Renk(string anahtar) => (Color)Application.Current!.Resources[anahtar];
}
```

- [ ] **Adım 4: YeniKartKutusu'nu yaz**

`Kasa.App/Controls/YeniKartKutusu.cs`:

```csharp
using System.Windows.Input;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Kasa.App.Controls;

/// <summary>Kart ızgarasının sonundaki kesik çizgili "Yeni kart ekle" kutusu (yalnız editörde görünür; KartIzgarasi.YeniGorunur).
/// Tıklama yüzeyi KartKutusu'ndaki gibi şeffaf düğmedir (UI Otomasyonu'nda "Yeni kart ekle").</summary>
public class YeniKartKutusu : Border
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(YeniKartKutusu),
        propertyChanged: (b, _, y) => ((YeniKartKutusu)b)._dugme.Command = (ICommand?)y);

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    private readonly Button _dugme;
    private bool _secili;

    public YeniKartKutusu()
    {
        StrokeShape = new RoundRectangle { CornerRadius = 14 };
        StrokeDashArray = new DoubleCollection { 4, 3 };
        MinimumHeightRequest = 120;
        BackgroundColor = Colors.Transparent;
        _dugme = new Button { Style = (Style)Application.Current!.Resources["SeffafDugme"] };
        SemanticProperties.SetDescription(_dugme, "Yeni kart ekle");
        var yesil = (Color)Application.Current!.Resources["Green"];
        var yazi = new VerticalStackLayout
        {
            Spacing = 2,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "+", FontSize = 26, HorizontalTextAlignment = TextAlignment.Center, TextColor = yesil },
                new Label { Text = "Yeni kart ekle", FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, TextColor = yesil },
            },
        };
        var kok = new Grid();
        kok.Add(_dugme);
        kok.Add(yazi);
        Content = kok;
        Cerceve();
    }

    /// <summary>Yeni kart formu açık: kenar yeşil ve kalın.</summary>
    public bool Secili
    {
        get => _secili;
        set
        {
            _secili = value;
            Cerceve();
        }
    }

    private void Cerceve()
    {
        Stroke = new SolidColorBrush(_secili ? (Color)Application.Current!.Resources["Green"] : (Color)Application.Current!.Resources["FieldStroke"]);
        StrokeThickness = _secili ? 2.5 : 1.5;
    }
}
```

- [ ] **Adım 5: KartIzgarasi'ni yaz**

`Kasa.App/Controls/KartIzgarasi.cs`:

```csharp
using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Kasa.App.Core;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;

namespace Kasa.App.Controls;

/// <summary>
/// Kart ızgarası (Kartlar ekranı, tasarım 2026-09-30 §2): <see cref="ItemsSource"/>'taki her <see cref="KartTakipSatiri"/> için
/// bir <see cref="KartKutusu"/>, sonunda "Yeni kart ekle" kutusu (<see cref="YeniGorunur"/>) ve en sonda <see cref="Ayrinti"/>.
/// Açık kutu açık kartın kimliğinden (<see cref="AcikKartId"/>) ya da yeni kart formundan (<see cref="YeniAcik"/>) gelir;
/// ayrıntı yalnız bir kutu açıkken görünür ve o kutunun satırının hemen altına tam genişlikte yerleşir. Satırdaki kutu sayısı
/// genişliğe göre değişir. Ölçüm ve yerleştirmenin hesabı KartIzgarasiHesabi'ndadır (Kasa.App.Core, sınanır); bu sınıf yalnız
/// çocukları ölçüp o hesabın dikdörtgenlerine yerleştirir.
/// </summary>
public class KartIzgarasi : Layout
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(KartIzgarasi),
        propertyChanged: (b, e, y) => ((KartIzgarasi)b).KaynakDegisti((IEnumerable?)e, (IEnumerable?)y));
    public static readonly BindableProperty SecCommandProperty = BindableProperty.Create(nameof(SecCommand), typeof(ICommand), typeof(KartIzgarasi));
    public static readonly BindableProperty YeniCommandProperty = BindableProperty.Create(nameof(YeniCommand), typeof(ICommand), typeof(KartIzgarasi));
    public static readonly BindableProperty YeniGorunurProperty = Durum(nameof(YeniGorunur), typeof(bool), false);
    public static readonly BindableProperty AcikKartIdProperty = Durum(nameof(AcikKartId), typeof(int?), null);
    public static readonly BindableProperty YeniAcikProperty = Durum(nameof(YeniAcik), typeof(bool), false);
    public static readonly BindableProperty AralikProperty = Durum(nameof(Aralik), typeof(double), 16d);
    public static readonly BindableProperty AyrintiProperty = BindableProperty.Create(nameof(Ayrinti), typeof(View), typeof(KartIzgarasi),
        propertyChanged: (b, e, y) => ((KartIzgarasi)b).AyrintiDegisti((View?)e, (View?)y));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    /// <summary>Kutuya dokunulunca kutunun satırıyla çalışır.</summary>
    public ICommand? SecCommand { get => (ICommand?)GetValue(SecCommandProperty); set => SetValue(SecCommandProperty, value); }
    /// <summary>"Yeni kart ekle" kutusuna dokunulunca çalışır.</summary>
    public ICommand? YeniCommand { get => (ICommand?)GetValue(YeniCommandProperty); set => SetValue(YeniCommandProperty, value); }
    public bool YeniGorunur { get => (bool)GetValue(YeniGorunurProperty); set => SetValue(YeniGorunurProperty, value); }
    public int? AcikKartId { get => (int?)GetValue(AcikKartIdProperty); set => SetValue(AcikKartIdProperty, value); }
    public bool YeniAcik { get => (bool)GetValue(YeniAcikProperty); set => SetValue(YeniAcikProperty, value); }
    /// <summary>Kutular ve satırlar arası boşluk (varsayılan 16).</summary>
    public double Aralik { get => (double)GetValue(AralikProperty); set => SetValue(AralikProperty, value); }
    public View? Ayrinti { get => (View?)GetValue(AyrintiProperty); set => SetValue(AyrintiProperty, value); }

    private readonly List<KartKutusu> _kutular = [];
    private readonly YeniKartKutusu _yeni = new();

    public KartIzgarasi()
    {
        _yeni.SetBinding(YeniKartKutusu.CommandProperty, new Binding(nameof(YeniCommand), source: this));
        Add(_yeni);
        Guncelle();
    }

    public IReadOnlyList<KartKutusu> Kutular => _kutular;
    public YeniKartKutusu YeniKutusu => _yeni;
    /// <summary>Açık kutunun sırası (kart kutuları, ardından "Yeni kart ekle"); açık kutu yoksa -1.</summary>
    public int AcikIndeks { get; private set; } = -1;

    private static BindableProperty Durum(string ad, Type tur, object? varsayilan)
        => BindableProperty.Create(ad, tur, typeof(KartIzgarasi), varsayilan, propertyChanged: (b, _, _) => ((KartIzgarasi)b).Guncelle());

    private void KaynakDegisti(IEnumerable? eski, IEnumerable? yeni)
    {
        if (eski is INotifyCollectionChanged e)
            e.CollectionChanged -= KaynakDegisti;
        if (yeni is INotifyCollectionChanged y)
            y.CollectionChanged += KaynakDegisti;
        KutulariKur();
    }

    private void KaynakDegisti(object? sender, NotifyCollectionChangedEventArgs e) => KutulariKur();

    /// <summary>Kutular kaynaktan yeniden kurulur (satır kayıtları değişmez: güncellenen kart yeni satırdır).</summary>
    private void KutulariKur()
    {
        foreach (var kutu in _kutular)
            Remove(kutu);
        _kutular.Clear();
        foreach (var satir in ItemsSource?.OfType<KartTakipSatiri>() ?? Enumerable.Empty<KartTakipSatiri>())
        {
            var kutu = new KartKutusu { BindingContext = satir, CommandParameter = satir };
            kutu.SetBinding(KartKutusu.CommandProperty, new Binding(nameof(SecCommand), source: this));
            Insert(_kutular.Count, kutu);
            _kutular.Add(kutu);
        }
        Guncelle();
    }

    private void AyrintiDegisti(View? eski, View? yeni)
    {
        if (eski is not null)
            Remove(eski);
        if (yeni is not null)
            Add(yeni);
        Guncelle();
    }

    private void Guncelle()
    {
        _yeni.IsVisible = YeniGorunur;
        var kimlikler = _kutular.Select(k => k.Satir!.Veri.Id).ToList();
        AcikIndeks = KartIzgarasiHesabi.AcikIndeks(kimlikler, AcikKartId, YeniAcik && YeniGorunur);
        for (var i = 0; i < _kutular.Count; i++)
            _kutular[i].Secili = i == AcikIndeks;
        _yeni.Secili = AcikIndeks == _kutular.Count;
        if (Ayrinti is { } ayrinti)
            ayrinti.IsVisible = AcikIndeks >= 0;
        InvalidateMeasure();
    }

    protected override ILayoutManager CreateLayoutManager() => new Yerlestirici(this);

    /// <summary>Çocukları kutu genişliğinde ölçer, KartIzgarasiHesabi'nın dikdörtgenlerine yerleştirir. Görünmeyen "Yeni kart ekle"
    /// kutusu yer tutmaz; açık kutunun sırası görünen kutular arasındadır (yeni kart formu yalnız kutu görünürken açık sayılır).</summary>
    private sealed class Yerlestirici(KartIzgarasi izgara) : LayoutManager(izgara)
    {
        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var yerlesim = Hesapla(widthConstraint, olc: true);
            return new Size(yerlesim.Genislik + izgara.Padding.HorizontalThickness, yerlesim.Yukseklik + izgara.Padding.VerticalThickness);
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var yerlesim = Hesapla(bounds.Width, olc: false);
            var x = bounds.X + izgara.Padding.Left;
            var y = bounds.Y + izgara.Padding.Top;
            var kutular = GorunenKutular();
            for (var i = 0; i < kutular.Count; i++)
                kutular[i].Arrange(Dortgen(yerlesim.Kutular[i], x, y));
            if (izgara.Ayrinti is IView ayrinti && yerlesim.Ayrinti is { } alan)
                ayrinti.Arrange(Dortgen(alan, x, y));
            return new Size(bounds.Width, yerlesim.Yukseklik + izgara.Padding.VerticalThickness);
        }

        private List<IView> GorunenKutular()
            => izgara.Where(v => !ReferenceEquals(v, izgara.Ayrinti) && v.Visibility != Visibility.Collapsed).ToList();

        /// <summary>Ölçümde çocuklar ölçülür; yerleştirmede ölçülmüş yükseklikleri (DesiredSize) kullanılır.</summary>
        private KartIzgarasiYerlesimi Hesapla(double genislik, bool olc)
        {
            var ic = genislik - izgara.Padding.HorizontalThickness;
            var (_, kullanilan, kutuGenisligi) = KartIzgarasiHesabi.Olcu(ic, izgara.Aralik);
            var yukseklikler = GorunenKutular()
                .Select(k => olc ? k.Measure(kutuGenisligi, double.PositiveInfinity).Height : k.DesiredSize.Height).ToList();
            var ayrintiYuksekligi = 0d;
            if (izgara.AcikIndeks >= 0 && izgara.Ayrinti is IView ayrinti && ayrinti.Visibility != Visibility.Collapsed)
                ayrintiYuksekligi = olc ? ayrinti.Measure(kullanilan, double.PositiveInfinity).Height : ayrinti.DesiredSize.Height;
            return KartIzgarasiHesabi.Hesapla(ic, izgara.Aralik, yukseklikler, izgara.AcikIndeks, ayrintiYuksekligi);
        }

        private static Rect Dortgen(Dikdortgen d, double x, double y) => new(x + d.X, y + d.Y, d.Genislik, d.Yukseklik);
    }
}
```

- [ ] **Adım 6: Testleri çalıştır, geçtiğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartKutusuTests|FullyQualifiedName~GorunumEsdegerligiTests|FullyQualifiedName~MauiKayitTutarliligiTests"`
Beklenen: `Passed!` ve `Failed: 0`.

- [ ] **Adım 7: Windows derlemesi, maui-lint, biçim**

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
Beklenen: `0 Warning(s)`, `0 Error(s)`.
Çalıştır: `bash .github/scripts/maui-lint.sh` → `maui-lint: taban içinde.` (C#'taki `Resources["…"]` anahtarları: SeffafDugme, Green, FieldStroke, NegSoft, Neg, UyariZemin, UyariMetin, ChipBg, Ink, StatusChip, LblStatusChip — hepsi tanımlı.)
Çalıştır: `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes` → çıktı yok.

- [ ] **Adım 8: Commit**

```bash
git add Kasa.App/Controls/KartKutusu.cs Kasa.App/Controls/YeniKartKutusu.cs Kasa.App/Controls/KartIzgarasi.cs Kasa.App.Core.Tests/Donusturuculer/KartKutusuTests.cs
git commit -F - <<'EOF'
feat(app): kart kutusu, yeni kart kutusu ve kart ızgarası bileşenleri

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Görev 7: KartTakipViewModel form ve sekme durumu

**Dosyalar:**
- Oluştur: `Kasa.App.Core/KartTakipViewModel.Gorunum.cs`
- Değiştir: `Kasa.App.Core/KartTakipViewModel.cs`, `Kasa.App.Core/KartTakipViewModel.Masraf.cs`
- Test: `Kasa.App.Core.Tests/KartTakipGorunumTests.cs`

- [ ] **Adım 1: Başarısız testi yaz**

`Kasa.App.Core.Tests/KartTakipGorunumTests.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Kartlar ekranının arayüz durumu (tasarım 2026-09-30 §2): kutuya tıklamak kartı açar/kapatır, aynı anda tek form açıktır,
/// başka düğme açık formu değiştirir, başarılı kayıt ve Vazgeç formu kapatır, hata olursa form açık kalır ve hata formun
/// içinde gösterilir, kart değişince form kapanır, izleyici form açamaz, başka ekrandan gelen kart açık gelir. Mevcut
/// ödeme/harcama/masraf/geçiş testleri (FinansTakipTests, TakipKomutlariTests, OnizlemeOnayTests …) değişmeden geçer.
/// </summary>
public class KartTakipGorunumTests
{
    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };

    private static async Task<(KartTakipViewModel Vm, FinansTakipTests.Sahte Api)> Vm(Rol rol = Rol.Editor, FinansTakipTests.Sahte? api = null,
        AuthViewModel? auth = null)
    {
        api ??= new FinansTakipTests.Sahte();
        var vm = new KartTakipViewModel(api, Finans(), auth ?? TestOturumu.Ac(rol));
        await vm.YukleAsync();
        return (vm, api);
    }

    private static FinansTakipTests.Sahte IkiKartli()
    {
        var kart = FinansTakipTests.Sahte.OrnekKart();
        return new FinansTakipTests.Sahte { KartlarYaniti = Task.FromResult<IReadOnlyList<KartTakipDto>>([kart, kart with { Id = 2, Ad = "Akbank" }]) };
    }

    [Fact]
    public async Task Baslangicta_acik_kart_form_yok_varsayilan_sekme_ekstreler()
    {
        var (vm, _) = await Vm();
        Assert.Null(vm.AcikKartId);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.False(vm.FormAcik);
        Assert.Equal(KartSekmesi.Ekstreler, vm.SeciliSekme);
        Assert.Equal(new[] { true, false, false }, vm.Sekmeler.Select(s => s.Secili));
        Assert.Equal(new[] { "Ekstreler", "Harcamalar", "Ödemeler" }, vm.Sekmeler.Select(s => s.Ad));
    }

    [Fact]
    public async Task Kutuya_tiklamak_karti_acar_ayni_kutuya_tekrar_tiklamak_kapatir()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        Assert.Equal(1, vm.AcikKartId);
        Assert.True(vm.KartSecili);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        Assert.Null(vm.AcikKartId);
        Assert.False(vm.KartSecili);
        Assert.False(vm.YeniKartFormuAcik);
    }

    [Fact]
    public async Task Ayni_anda_tek_form_acik_baska_dugme_acik_formu_degistirir()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
        Assert.True(vm.FormAcik);
        vm.FormAcCommand.Execute(KartFormu.Harcama);
        Assert.Equal(KartFormu.Harcama, vm.AcikForm);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);
    }

    [Fact]
    public async Task Vazgec_formu_kapatir_ve_form_hatasini_temizler()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Hata);
        vm.VazgecCommand.Execute(null);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Basarili_odeme_kaydindan_sonra_form_kapanir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 10;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);   // önizleme formu kapatmaz
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Single(api.OdemeIstekleri);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Contains("kaydedildi", vm.Mesaj);
        Assert.Equal(1, vm.AcikKartId);   // kart açık kalır
    }

    [Fact]
    public async Task Basarili_kart_bilgisi_ve_ekstre_kaydindan_sonra_form_kapanir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Limit = 2000;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, api.KartKayitSayisi);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);

        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        Assert.Equal(KartFormu.Ekstre, vm.AcikForm);
        vm.Gerekce = "Banka ekstresiyle kontrol edildi";
        await vm.EkstreKaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, api.EkstreKayitSayisi);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Null(vm.DuzenlenenEkstre);
    }

    [Fact]
    public async Task Hata_olursa_form_acik_kalir_ve_hata_formun_icinde_gosterilir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);
        Assert.Equal("Kart adını, limiti ve 1–31 arası günleri kontrol edin.", vm.FormHatasi);
        Assert.Null(vm.SayfaHatasi);
        vm.VazgecCommand.Execute(null);
        vm.Hata = "Sunucuya ulaşılamadı.";
        Assert.Null(vm.FormHatasi);
        Assert.Equal("Sunucuya ulaşılamadı.", vm.SayfaHatasi);
    }

    [Fact]
    public async Task Kart_degisince_form_kapanir_ve_sekme_ekstrelere_doner()
    {
        var (vm, _) = await Vm(api: IkiKartli());
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.SekmeSecCommand.Execute(vm.Sekmeler[2]);
        vm.KutuSecCommand.Execute(vm.Kartlar[1]);
        Assert.Equal(2, vm.AcikKartId);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Equal(KartSekmesi.Ekstreler, vm.SeciliSekme);
    }

    [Fact]
    public async Task Izleyici_karti_acar_ama_form_acamaz()
    {
        var (vm, _) = await Vm(Rol.Izleyici);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        Assert.Equal(1, vm.AcikKartId);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        vm.YeniKartAcCommand.Execute(null);
        Assert.False(vm.YeniKartFormuAcik);
        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Rol_izleyiciye_donunce_acik_form_kapanir()
    {
        var auth = TestOturumu.Ac(Rol.Editor);
        var (vm, _) = await Vm(auth: auth);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        auth.AktifRol = Rol.Izleyici;
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Eski_takipte_yalniz_gecis_ve_kart_bilgisi_formu_acilir()
    {
        var api = new FinansTakipTests.Sahte { Kart = FinansTakipTests.Sahte.OrnekKart() with { YeniTakip = false } };
        var (vm, _) = await Vm(api: api);
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        foreach (var form in new[] { KartFormu.Odeme, KartFormu.Harcama, KartFormu.Masraf })
        {
            vm.FormAcCommand.Execute(form);
            Assert.Equal(KartFormu.Yok, vm.AcikForm);
        }
        vm.FormAcCommand.Execute(KartFormu.Gecis);
        Assert.Equal(KartFormu.Gecis, vm.AcikForm);
    }

    [Fact]
    public async Task Ekstre_formundan_baska_forma_gecince_duzenlenen_ekstre_birakilir()
    {
        var (vm, _) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        Assert.NotNull(vm.DuzenlenenEkstre);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        Assert.Null(vm.DuzenlenenEkstre);
        vm.FormAcCommand.Execute(KartFormu.Ekstre);   // seçili ekstre yokken ekstre formu açılmaz
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
    }

    [Fact]
    public async Task Baska_ekrandan_gelen_kart_acik_olur()
    {
        var (vm, _) = await Vm(api: IkiKartli());
        Assert.True(vm.IdIleSec(2));
        Assert.Equal(2, vm.AcikKartId);
        Assert.True(vm.KartSecili);
    }

    [Fact]
    public async Task Yeni_kart_kutusu_bos_kart_formunu_acar_kayittan_sonra_yeni_kart_acik_gelir()
    {
        var (vm, api) = await Vm();
        vm.KutuSecCommand.Execute(vm.Kartlar[0]);
        vm.YeniKartAcCommand.Execute(null);
        Assert.Null(vm.Secili);
        Assert.True(vm.YeniKartFormuAcik);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);
        Assert.Equal("", vm.Ad);
        vm.Ad = "Yeni kart";
        vm.Limit = 5000;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.KartKayit!.Value.Id);   // yeni kart olarak gitti
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.False(vm.YeniKartFormuAcik);
        Assert.Equal(api.Kart.Id, vm.AcikKartId);
    }

    [Fact]
    public async Task Yeni_kart_kutusuna_tekrar_tiklamak_formu_kapatir()
    {
        var (vm, _) = await Vm();
        vm.YeniKartAcCommand.Execute(null);
        vm.YeniKartAcCommand.Execute(null);
        Assert.False(vm.YeniKartFormuAcik);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }

    [Fact]
    public async Task Sekme_secimi_tek_sekmeyi_isaretler()
    {
        var (vm, _) = await Vm();
        vm.SekmeSecCommand.Execute(vm.Sekmeler[2]);
        Assert.Equal(KartSekmesi.Odemeler, vm.SeciliSekme);
        Assert.Equal(new[] { false, false, true }, vm.Sekmeler.Select(s => s.Secili));
        vm.SekmeSecCommand.Execute(vm.Sekmeler[1]);
        Assert.Equal(KartSekmesi.Harcamalar, vm.SeciliSekme);
        Assert.Equal(new[] { false, true, false }, vm.Sekmeler.Select(s => s.Secili));
    }
}
```

- [ ] **Adım 2: Testi çalıştır, derlenmediğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartTakipGorunumTests"`
Beklenen: `error CS1061: 'KartTakipViewModel' does not contain a definition for 'AcikKartId'` (ve `KartFormu` için CS0103).

- [ ] **Adım 3: Arayüz durumunu yaz**

`Kasa.App.Core/KartTakipViewModel.Gorunum.cs`:

```csharp
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kartlar ekranında açık form; aynı anda tek form açıktır (Yok: hiçbiri).</summary>
public enum KartFormu { Yok, Odeme, Harcama, Masraf, Ekstre, KartBilgisi, Gecis }

/// <summary>Kart ayrıntısının sekmeleri (varsayılan Ekstreler).</summary>
public enum KartSekmesi { Ekstreler, Harcamalar, Odemeler }

/// <summary>
/// Kartlar ekranının arayüz durumu (tasarım 2026-09-30 §2). Açık kart <see cref="KartTakipViewModel.Secili"/>'dir (kutuya
/// tıklamak açar, aynı kutuya tekrar tıklamak kapatır); Secili null iken <see cref="KartFormu.KartBilgisi"/> yeni kart formudur.
/// Formlar yalnız editörde ve kartın takibine uygunsa açılır; başarılı kayıt, Vazgeç, kart değişimi ve izleyiciye dönüş formu
/// kapatır, hata formu açık bırakır (hata formun içinde gösterilir: <see cref="FormHatasi"/>). Hesap, doğrulama ve sunucu
/// mantığı değişmez.
/// </summary>
public partial class KartTakipViewModel
{
    private readonly SecimCipi[] _sekmeler = [new("Ekstreler") { Secili = true }, new("Harcamalar"), new("Ödemeler")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormAcik), nameof(YeniKartFormuAcik), nameof(FormHatasi), nameof(SayfaHatasi))]
    private KartFormu _acikForm;

    [ObservableProperty]
    private KartSekmesi _seciliSekme;

    /// <summary>Sekme çipleri (CipGrubu); sırası <see cref="KartSekmesi"/> ile aynı.</summary>
    public IReadOnlyList<SecimCipi> Sekmeler => _sekmeler;
    public bool FormAcik => AcikForm != KartFormu.Yok;
    /// <summary>Kutusu açık kartın kimliği (KartIzgarasi.AcikKartId); açık kart yoksa null.</summary>
    public int? AcikKartId => Secili?.Id;
    /// <summary>"Yeni kart ekle" kutusunun formu açık (KartIzgarasi.YeniAcik).</summary>
    public bool YeniKartFormuAcik => Secili is null && AcikForm == KartFormu.KartBilgisi;
    /// <summary>Form açıkken hata formun içinde gösterilir; sayfa başındaki hata satırı o sırada boştur.</summary>
    public string? FormHatasi => FormAcik ? Hata : null;
    public string? SayfaHatasi => FormAcik ? null : Hata;

    [RelayCommand]
    private void FormAc(KartFormu form)
    {
        if (!EditorMu || !Acilabilir(form))
            return;
        Hata = null;
        AcikForm = form;
    }

    /// <summary>Ödeme, harcama ve masraf yeni takipteki kartta; ekstre bilgisi yeni takipte seçili ekstreyle; geçiş eski takipte.</summary>
    private bool Acilabilir(KartFormu form) => form switch
    {
        KartFormu.Odeme or KartFormu.Harcama or KartFormu.Masraf => YeniTakip,
        KartFormu.Ekstre => YeniTakip && DuzenlenenEkstre is not null,
        KartFormu.Gecis => EskiTakip,
        KartFormu.KartBilgisi => true,
        _ => false,
    };

    [RelayCommand]
    private void Vazgec()
    {
        AcikForm = KartFormu.Yok;
        Hata = null;
    }

    /// <summary>Kutuya tıklandı: kart açık değilse açılır (<see cref="Sec"/>), açıksa kapanır.</summary>
    [RelayCommand]
    private void KutuSec(KartTakipSatiri satir)
    {
        if (Secili?.Id == satir.Veri.Id)
            Yeni();
        else
            Sec(satir);
    }

    /// <summary>"Yeni kart ekle" kutusu: boş kart bilgileri formunu açar; form açıkken tekrar tıklamak kapatır.</summary>
    [RelayCommand]
    private void YeniKartAc()
    {
        if (!EditorMu)
            return;
        if (YeniKartFormuAcik)
        {
            AcikForm = KartFormu.Yok;
            return;
        }
        Yeni();
        FormAc(KartFormu.KartBilgisi);
    }

    [RelayCommand]
    private void SekmeSec(SecimCipi sekme)
    {
        var sira = Array.IndexOf(_sekmeler, sekme);
        if (sira >= 0)
            SeciliSekme = (KartSekmesi)sira;
    }

    partial void OnSeciliSekmeChanged(KartSekmesi value)
    {
        for (var i = 0; i < _sekmeler.Length; i++)
            _sekmeler[i].Secili = i == (int)value;
    }

    /// <summary>Ekstre formundan çıkılınca düzenlenen ekstre bırakılır (ekstre formu yalnız ekstreye tıklanarak açılır).</summary>
    partial void OnAcikFormChanged(KartFormu oldValue, KartFormu newValue)
    {
        if (oldValue == KartFormu.Ekstre && newValue != KartFormu.Ekstre)
            DuzenlenenEkstre = null;
    }

    /// <summary>Başka karta geçiş (kutu, yeni kart, yeni kartın kaydı, liste yenilemesinde kaybolan kart) açık formu kapatır ve
    /// Ekstreler sekmesine döner; aynı kartın güncellenmesi (kayıt sonucu) formu ve sekmeyi korur.</summary>
    partial void OnSeciliChanged(KartTakipDto? oldValue, KartTakipDto? newValue)
    {
        if (oldValue?.Id != newValue?.Id)
        {
            AcikForm = KartFormu.Yok;
            SeciliSekme = KartSekmesi.Ekstreler;
        }
        OnPropertyChanged(nameof(AcikKartId));
        OnPropertyChanged(nameof(YeniKartFormuAcik));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Hata))
        {
            OnPropertyChanged(nameof(FormHatasi));
            OnPropertyChanged(nameof(SayfaHatasi));
        }
    }

    /// <summary>İzleyiciye dönen oturumda form açık kalmaz.</summary>
    protected override void RolDegisti()
    {
        if (!EditorMu)
            AcikForm = KartFormu.Yok;
    }
}
```

- [ ] **Adım 4: Başarılı kayıtlarda formu kapat, ekstre formunu bağla**

`Kasa.App.Core/KartTakipViewModel.cs`'te her değişiklik tek satırdır (Edit aracıyla birebir):

`KaydetAsync`:
```csharp
        { _kayit.Temizle(); Mesaj = "Kart kaydedildi."; }
```
→
```csharp
        { _kayit.Temizle(); AcikForm = KartFormu.Yok; Mesaj = "Kart kaydedildi."; }
```

`HarcamaKaydetAsync`:
```csharp
        { _harcama.Temizle(kart.Id); HarcamaBenzerlik.Temizle(); HarcamaTutari = 0; HarcamaAciklama = ""; HarcamaPaylari.Clear(); Mesaj = "Kart hareketi kaydedildi. Henüz kasa çıkışı oluşmadı."; }
```
→ (tek satırlık blok kalır: `csharp_preserve_single_line_statements = false` çok satırlı blokta aynı satırdaki deyimleri böler)
```csharp
        { _harcama.Temizle(kart.Id); HarcamaBenzerlik.Temizle(); HarcamaTutari = 0; HarcamaAciklama = ""; HarcamaPaylari.Clear(); AcikForm = KartFormu.Yok; Mesaj = "Kart hareketi kaydedildi. Henüz kasa çıkışı oluşmadı."; }
```

`OdemeKaydetAsync`:
```csharp
        { _odeme.Temizle(kart.Id); OdemeBenzerlik.Temizle(); _odemeOnizlemesi.Temizle(); OdemeOnizleme = null; OdemeTutari = 0; OdemeNotu = ""; Mesaj = "Kart ödemesi kaydedildi; kasa etkisi bir kez işlendi."; }
```
→
```csharp
        { _odeme.Temizle(kart.Id); OdemeBenzerlik.Temizle(); _odemeOnizlemesi.Temizle(); OdemeOnizleme = null; OdemeTutari = 0; OdemeNotu = ""; AcikForm = KartFormu.Yok; Mesaj = "Kart ödemesi kaydedildi; kasa etkisi bir kez işlendi."; }
```

`EkstreSec` (editörde ve yeni takipte ekstre formunu açar; izleyicide yalnız seçim):
```csharp
    [RelayCommand] private void EkstreSec(EkstreSatiri satir) { DuzenlenenEkstre = satir; EkstreSonOdeme = satir.Veri.SonOdemeTarihi.ToDateTime(TimeOnly.MinValue); AsgariVar = satir.Veri.AsgariOdeme is not null; AsgariTutar = satir.Veri.AsgariOdeme ?? 0; }
```
→
```csharp
    [RelayCommand]
    private void EkstreSec(EkstreSatiri satir)
    {
        DuzenlenenEkstre = satir;
        EkstreSonOdeme = satir.Veri.SonOdemeTarihi.ToDateTime(TimeOnly.MinValue);
        AsgariVar = satir.Veri.AsgariOdeme is not null;
        AsgariTutar = satir.Veri.AsgariOdeme ?? 0;
        FormAc(KartFormu.Ekstre);
    }
```

`EkstreKaydetAsync`:
```csharp
        { _ekstre.Temizle(Secili.Id); DuzenlenenEkstre = null; Mesaj = "Ekstre bilgisi kaydedildi."; }
```
→
```csharp
        { _ekstre.Temizle(Secili.Id); DuzenlenenEkstre = null; AcikForm = KartFormu.Yok; Mesaj = "Ekstre bilgisi kaydedildi."; }
```

`DurumDegistirAsync` (Kartı düzenle formundaki aktif/pasif):
```csharp
        { _durum.Temizle(Secili.Id); Mesaj = "Kartın kullanım durumu değiştirildi; geçmiş korundu."; }
```
→
```csharp
        { _durum.Temizle(Secili.Id); AcikForm = KartFormu.Yok; Mesaj = "Kartın kullanım durumu değiştirildi; geçmiş korundu."; }
```

`GecisiOnaylaAsync`:
```csharp
        { _gecis.Temizle(kart.Id); GecisDurumu(null, null); Mesaj = "Yeni takip açıldı; geçmiş kayıtlar korundu."; }
```
→
```csharp
        { _gecis.Temizle(kart.Id); GecisDurumu(null, null); AcikForm = KartFormu.Yok; Mesaj = "Yeni takip açıldı; geçmiş kayıtlar korundu."; }
```

`DevirDuzeltAsync` (Kartı düzenle formundaki eski borç devri):
```csharp
        { _devirDuzelt.Temizle(kart.Id); DevirTemizle(); Mesaj = "Eski borç devri düzeltildi; geçmiş kasa sonuçları korundu."; }
```
→
```csharp
        { _devirDuzelt.Temizle(kart.Id); DevirTemizle(); AcikForm = KartFormu.Yok; Mesaj = "Eski borç devri düzeltildi; geçmiş kasa sonuçları korundu."; }
```

`OturumTemizle`'de `Yeni();` satırından sonra:
```csharp
        AcikForm = KartFormu.Yok;
        SeciliSekme = KartSekmesi.Ekstreler;
```

`Kasa.App.Core/KartTakipViewModel.Masraf.cs`'te:
```csharp
            { MasrafTemizle(); Mesaj = "Faiz / masraf kanal paylarıyla karta kaydedildi. Henüz kasa çıkışı oluşmadı."; }
```
→
```csharp
            { MasrafTemizle(); AcikForm = KartFormu.Yok; Mesaj = "Faiz / masraf kanal paylarıyla karta kaydedildi. Henüz kasa çıkışı oluşmadı."; }
```

(İptaller — `IptalAsync` — sekmelerdeki satırlardan yapılır, form açmaz; dokunulmaz.)

- [ ] **Adım 5: Yeni ve mevcut kart testlerini çalıştır**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartTakipGorunumTests|FullyQualifiedName~FinansTakipTests|FullyQualifiedName~TakipKomutlariTests|FullyQualifiedName~OnizlemeOnayTests|FullyQualifiedName~KartDevirVmTests|FullyQualifiedName~GecersizTutarTests|FullyQualifiedName~GerekceTests|FullyQualifiedName~OturumVeRolTests|FullyQualifiedName~KasaKontrolVeAylikGiderTests"`
Beklenen: `Passed!` ve `Failed: 0`.

- [ ] **Adım 6: Biçim ve commit**

Çalıştır: `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes` → çıktı yok.

```bash
git add Kasa.App.Core/KartTakipViewModel.Gorunum.cs Kasa.App.Core/KartTakipViewModel.cs Kasa.App.Core/KartTakipViewModel.Masraf.cs Kasa.App.Core.Tests/KartTakipGorunumTests.cs
git commit -F - <<'EOF'
feat(app): kartlar ekranında açık kart, tek form ve sekme durumu

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Görev 8: KartTakipPage yeniden düzeni

**Dosyalar:**
- Değiştir: `Kasa.App/Views/TakipUi.cs` (`DurumSatirlari`, `TakipSayfasi` kurucusu), `Kasa.App/Views/KartTakipPage.cs` (tamamı), `.github/scripts/maui-lint-tabani.txt`

Bu görevde yeni mantık yoktur (mantık Görev 4–7'de sınandı); doğrulama Windows derlemesi, sınama paketinin tamamı ve Görev 9'daki gerçek pencere görüntüleridir.

- [ ] **Adım 1: Hata satırının yolunu isteğe bağlı yap**

`Kasa.App/Views/TakipUi.cs`'te:

```csharp
    /// <summary>Kodla yazılmış sayfaların durum satırları (Yenile, yükleniyor göstergesi, hata, isteğe bağlı ileti, son
    /// güncelleme): TakipSayfasi, Kasa kontrolü ve Dışa aktar aynı sırayı ve stilleri kullanır. Bağlam modelinde Mesgul,
    /// Hata ve SonGuncelleme beklenir. <paramref name="gostergeSolda"/> false iken gösterge satır boyunca yerleşir (Kasa
    /// kontrolünün önceki görünümü).</summary>
    public static void DurumSatirlari(Layout hedef, View yenile, Label? mesaj = null, bool gostergeSolda = true)
    {
```
→
```csharp
    /// <summary>Kodla yazılmış sayfaların durum satırları (Yenile, yükleniyor göstergesi, hata, isteğe bağlı ileti, son
    /// güncelleme): TakipSayfasi, Kasa kontrolü ve Dışa aktar aynı sırayı ve stilleri kullanır. Bağlam modelinde Mesgul,
    /// Hata (ya da <paramref name="hataYolu"/>) ve SonGuncelleme beklenir. <paramref name="gostergeSolda"/> false iken gösterge
    /// satır boyunca yerleşir (Kasa kontrolünün önceki görünümü). Kartlar ekranı hatayı form açıkken formun içinde gösterdiği
    /// için buraya SayfaHatasi'nı bağlar.</summary>
    public static void DurumSatirlari(Layout hedef, View yenile, Label? mesaj = null, bool gostergeSolda = true, string hataYolu = "Hata")
    {
```

aynı yöntemde:
```csharp
        hedef.Add(BagliHata("Hata"));
```
→
```csharp
        hedef.Add(BagliHata(hataYolu));
```

`TakipSayfasi` kurucusunda:
```csharp
    protected TakipSayfasi(T vm, string title, string aciklama, Func<Task> yukle)
    {
```
→
```csharp
    protected TakipSayfasi(T vm, string title, string aciklama, Func<Task> yukle, string hataYolu = nameof(TemelViewModel.Hata))
    {
```
ve
```csharp
        TakipUi.DurumSatirlari(root, TakipUi.Tikla("Yenile / tekrar dene", yukle), mesaj);
```
→
```csharp
        TakipUi.DurumSatirlari(root, TakipUi.Tikla("Yenile / tekrar dene", yukle), mesaj, hataYolu: hataYolu);
```

- [ ] **Adım 2: TakipUi eşdeğerlik testlerinin değişmediğini gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~GorunumEsdegerligiTests"`
Beklenen: `Passed!` ve `Failed: 0` (varsayılan `"Hata"` yolu eski görünümle aynı).

- [ ] **Adım 3: KartTakipPage'i yeniden yaz**

`Kasa.App/Views/KartTakipPage.cs` dosyasının tamamı:

```csharp
using System.Globalization;
using Kasa.App.Controls;
using Kasa.App.Core;
using Microsoft.Maui.Layouts;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>
/// Kredi kartları (docs/specs/2026-09-30-masaustu-menu-ve-kartlar.md §2): kartlar banka rengindeki kutulardır (KartKutusu);
/// kutuya tıklanınca kartın ayrıntısı kutunun satırının hemen altında tam genişlikte açılır (KartIzgarasi). Ayrıntı yukarıdan
/// aşağıya: özet, düğmeler (editör), tek form alanı (editör) ve Ekstreler / Harcamalar / Ödemeler sekmeleri. Açık kart, açık form
/// ve seçili sekme modeldedir (KartTakipViewModel.AcikKartId, AcikForm, SeciliSekme). Formların alanları, önizlemeleri, benzer
/// kayıt uyarıları, onay kutuları ve iletileri önceki tek sayfalık düzendekiyle aynıdır; yalnız yerleri değişti. Önceki sayfada
/// ayrı kartlar olan "Kullanım durumu" ve "Eski borç devri" Kartı düzenle formundadır.
/// </summary>
public sealed class KartTakipPage : TakipSayfasi<KartTakipViewModel>, IQueryAttributable
{
    private const string SayfaAciklamasi = "Yeni takipte kartla harcama nakit çıkışı oluşturmaz; kasa kaydettiğiniz ödeme ile azalır. "
        + "Geçiş yapılmamış eski kartlarda önceki ay sonu kuralı korunur.";
    private const string KasaNotu = "Bu tutarlar mevcut kasadan düşülmüş değildir. Kasa, kaydedilen kart ödemesiyle değişir.";
    private const string AcilisNotu = "Açılış borcunun bilinen kanal paylarını girin. Bilinmeyen dağılım tahmin edilmez.";
    private const string DurumNotu = "Kartı pasife almak geçmiş hareketleri silmez.";
    private const string GecisNotu = "Bankanızdaki kalan borcu girin. Kasada önceden sayılan kısım, sistemin eski kuralla kasadan düştüğü/düşeceği borçtur; "
        + "siz değiştirmedikçe alan kalan borç ile sistem kart borcunun küçüğünü izler. Önizleme farklı bir tutar önerirse "
        + "\"Önerilen tutarla yeniden önizle\" ile uygulayabilirsiniz. Önerilenin altı yalnız açılış borcu kasadan ayrıca ödenecekse girilebilir. "
        + "Girdiler değişirse geçiş, yeni önizleme alınmadan onaylanamaz. Bu işlem yeni harcama oluşturmaz.";
    private const string EkstreNotu = "Asgari ödeme ve tarihler bankanın ekstresinden girilir; uygulama oran veya tatil günü tahmini yapmaz.";
    private const string MasrafNotu = "Bankanın bildirdiği tutarı girin. Seçilen kesilmiş ekstre ve önceki ekstrelerin kalan borcuna göre kanallara dağıtılır; "
        + "ileri taksitler ağırlığa katılmaz. Bilinmeyen kanal payı varsa kaydetmeden önce düzeltilmelidir.";
    private const string HarcamaNotu = "Alışlar veya İşlemler'den bu karta bağlanan harcamayı tekrar girmeyin. İade için eksi tutar ve tek taksit kullanın. "
        + "Kalan borca dağıtılacak faiz / masrafı \"Faiz / masraf\" formundan girin.";
    private const string HarcamaPayNotu = "Bilinen kanal paylarını girin. Taksit toplamı borca ikinci kez eklenmez.";
    private const string DevirNotu = "Devrin ödemesi kasada önceden sayılan kısım kadar kasadan ikinci kez düşmez; devre yapılan iadenin önceden sayılmış kısmı "
        + "iade tarihinde kasaya döner. Hatalı devir iptal edilmez: düzeltme etkin devri iptal edip aynı tarihle yeni tutarı yazar, gerekçe denetim izine kaydedilir.";

    private int? _istenenKartId;
    private readonly View _ayrinti;
    private readonly View _formAlani;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("KartId", out var value) && int.TryParse(value.ToString(), out var id) && id > 0)
            _istenenKartId = id;
    }

    protected override async void OnAppearing()
    {
        if (_istenenKartId is { } id)
        {
            await Vm.YukleAsync();
            if (Vm.VeriHazir && Vm.Hata is null && Vm.IdIleSec(id))
                _istenenKartId = null;
        }
        base.OnAppearing();
    }

    public KartTakipPage(KartTakipViewModel vm) : base(vm, "Kredi Kartları", SayfaAciklamasi, vm.YukleAsync, nameof(vm.SayfaHatasi))
    {
        _formAlani = FormAlani(vm);
        _ayrinti = new Border
        {
            Style = (Style)Application.Current!.Resources["CardForm"],
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children =
                {
                    Goster(Ozet(vm), nameof(vm.KartSecili)),
                    Editor(Goster(Dugmeler(vm), nameof(vm.KartSecili))),
                    Editor(Goster(_formAlani, nameof(vm.FormAcik))),
                    Goster(Sekmeler(vm), nameof(vm.KartSecili)),
                },
            },
        };
        var izgara = new KartIzgarasi { Ayrinti = _ayrinti };
        izgara.SetBinding(KartIzgarasi.ItemsSourceProperty, nameof(vm.Kartlar));
        izgara.SetBinding(KartIzgarasi.SecCommandProperty, nameof(vm.KutuSecCommand));
        izgara.SetBinding(KartIzgarasi.YeniCommandProperty, nameof(vm.YeniKartAcCommand));
        izgara.SetBinding(KartIzgarasi.YeniGorunurProperty, nameof(vm.EditorMu));
        izgara.SetBinding(KartIzgarasi.AcikKartIdProperty, nameof(vm.AcikKartId));
        izgara.SetBinding(KartIzgarasi.YeniAcikProperty, nameof(vm.YeniKartFormuAcik));
        Govde.Add(izgara);
        // Açılan kartın ayrıntısı ya da açılan form ekranın dışındaysa görünür yere kaydırılır.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.AcikKartId))
                GorunurYap(_ayrinti);
            else if (e.PropertyName == nameof(vm.AcikForm) && vm.FormAcik)
                GorunurYap(_formAlani);
        };
    }

    private void GorunurYap(View hedef) => Dispatcher.Dispatch(async () =>
    {
        if (hedef.IsVisible)
            await Kaydirici.ScrollToAsync(hedef, ScrollToPosition.MakeVisible, true);
    });

    // ---- Özet ----

    private static View Ozet(KartTakipViewModel vm)
    {
        // İlk sürüm geçiş kalıntısı (web'deki notice): tahmini kasa farkı varsa koyu kırmızı, yalnız düşüş tarihi farklıysa bilgi.
        var gecisUyarisi = Bagli(nameof(vm.GecisUyarisi));
        gecisUyarisi.FontAttributes = FontAttributes.Bold;
        gecisUyarisi.Triggers.Add(new DataTrigger(typeof(Label))
        {
            Binding = new Binding(nameof(vm.GecisUyarisiTehlikeli)),
            Value = true,
            Setters = { new Setter { Property = Label.TextColorProperty, Value = (Color)Application.Current!.Resources["KoyuKirmizi"] } }
        });
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Baslik("Kart ayrıntısı"),
                BagliBuyuk(nameof(vm.KartOzeti)),
                Goster(gecisUyarisi, nameof(vm.GecisUyarisi), true),
                AltBolum("Kanalların kalan kart borcu", Bagli(nameof(vm.KanalBorcOzeti)), Metin(KasaNotu)),
                Goster(AltBolum("Eski karttan geçiş", Bagli(nameof(vm.GecisKaydi))), nameof(vm.GecisKaydi), true),
            },
        };
    }

    // ---- Düğmeler ----

    private static View Dugmeler(KartTakipViewModel vm) => new VerticalStackLayout
    {
        Children =
        {
            Goster(DugmeSirasi(FormDugmesi("Ödeme kaydet", KartFormu.Odeme, birincil: true), FormDugmesi("Harcama ekle", KartFormu.Harcama),
                FormDugmesi("Faiz / masraf", KartFormu.Masraf), FormDugmesi("Kartı düzenle", KartFormu.KartBilgisi)), nameof(vm.YeniTakip)),
            Goster(DugmeSirasi(FormDugmesi("Yeni takibe al", KartFormu.Gecis, birincil: true), FormDugmesi("Kartı düzenle", KartFormu.KartBilgisi)),
                nameof(vm.EskiTakip)),
        },
    };

    private static Button FormDugmesi(string metin, KartFormu form, bool birincil = false)
    {
        var dugme = Dugme(metin, nameof(KartTakipViewModel.FormAcCommand));
        dugme.CommandParameter = form;
        if (!birincil)
            dugme.Style = (Style)Application.Current!.Resources["BtnSecondary"];
        return dugme;
    }

    private static FlexLayout DugmeSirasi(params Button[] dugmeler)
    {
        var sira = new FlexLayout { Wrap = FlexWrap.Wrap };
        foreach (var dugme in dugmeler)
        {
            dugme.Margin = new Thickness(0, 0, 10, 10);
            sira.Add(dugme);
        }
        return sira;
    }

    // ---- Form alanı: aynı anda tek form (AcikForm); hata formun içinde (FormHatasi) ----

    private View FormAlani(KartTakipViewModel vm)
    {
        var vazgec = Dugme("Vazgeç", nameof(vm.VazgecCommand));
        vazgec.Style = (Style)Application.Current!.Resources["BtnSecondary"];
        return new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] },
                Goster(BagliHata(nameof(vm.FormHatasi)), nameof(vm.FormHatasi), true),
                Durumda(KartBilgileri(vm), nameof(vm.AcikForm), KartFormu.KartBilgisi),
                Durumda(Odeme(vm), nameof(vm.AcikForm), KartFormu.Odeme),
                Durumda(Harcama(vm), nameof(vm.AcikForm), KartFormu.Harcama),
                Durumda(Masraf(vm), nameof(vm.AcikForm), KartFormu.Masraf),
                Durumda(Ekstre(vm), nameof(vm.AcikForm), KartFormu.Ekstre),
                Durumda(Gecis(vm), nameof(vm.AcikForm), KartFormu.Gecis),
                vazgec,
            },
        };
    }

    private View KartBilgileri(KartTakipViewModel vm)
    {
        var acilis = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Alan("Açılış tarihi", Tarih(nameof(vm.AcilisTarihi))), Alan("Açılış borcu", Girdi(nameof(vm.AcilisBorc), true)),
                Metin(AcilisNotu), Paylar(vm.AcilisPaylari, () => vm.PayEkle(vm.AcilisPaylari)),
            },
        };
        var durum = AltBolum("Kullanım durumu", Metin(DurumNotu), Tikla("Aktif / pasif durumunu değiştir",
            () => GerekceyleAsync("Kartın kullanım durumunu değiştir", (gerekce, _) => { vm.Gerekce = gerekce; return vm.DurumDegistirAsync(); })));
        return Form("Kart bilgileri",
            Alan("Kart / banka adı", Girdi(nameof(vm.Ad))), Alan("Limit", Girdi(nameof(vm.Limit), true)),
            Alan("Hesap kesim günü (1–31)", Girdi(nameof(vm.KesimGunu), sayi: true)), Alan("Son ödeme günü (1–31)", Girdi(nameof(vm.SonOdemeGunu), sayi: true)),
            Goster(acilis, nameof(vm.YeniKart)), Dugme("Kartı kaydet", nameof(vm.KaydetCommand)),
            Goster(durum, nameof(vm.KartSecili)), Goster(Devir(vm), nameof(vm.GecisKaydi), true));
    }

    private static View Devir(KartTakipViewModel vm)
    {
        // Geçişli kartın eski borç devri (web'deki "Eski borç devri" bölümü): okunur, engeli yoksa gerekçeyle düzeltilir.
        var devirFormu = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Alan("Doğru kalan borç", Girdi(nameof(vm.DevirKalanBorc), true)),
                Alan("Bu borcun kasada önceden sayılmış kısmı", Girdi(nameof(vm.DevirOncedenSayilan), true)),
                Paylar(vm.DevirPaylari, () => vm.PayEkle(vm.DevirPaylari)), Alan("Düzeltme gerekçesi", Girdi(nameof(vm.DevirAciklama))),
                Dugme("Devri düzelt", nameof(vm.DevirDuzeltCommand)),
            },
        };
        return AltBolum("Eski borç devri", Metin(DevirNotu), Dugme("Devir bilgisini göster", nameof(vm.DevirYukleCommand)),
            Bagli(nameof(vm.DevirOzeti)), Goster(devirFormu, nameof(vm.DevirDuzeltilebilir)));
    }

    private static View Odeme(KartTakipViewModel vm) => Form("Kart ödemesi kaydet",
        Alan("Tarih", Tarih(nameof(vm.OdemeTarihi))), Alan("Tutar", Girdi(nameof(vm.OdemeTutari), true)),
        Alan("Ekstre (boş: en eski açık ekstreler)", Secim(nameof(vm.Ekstreler), nameof(vm.OdemeEkstresi))),
        Tikla("En eski açık ekstrelere dağıt", () => { vm.OdemeEkstresi = null; return Task.CompletedTask; }),
        Alan("Ödeme notu / dekont referansı", Girdi(nameof(vm.OdemeNotu))),
        Dugme("Ödeme ve kanal paylarını göster", nameof(vm.OdemeOnizleCommand)), Bagli(nameof(vm.OdemeOnizleme)),
        Dugme("Ödemeyi kaydet", nameof(vm.OdemeKaydetCommand)), Benzerlik(nameof(vm.OdemeBenzerlik), nameof(vm.OdemeyiAyriKaydetCommand)));

    private static View Harcama(KartTakipViewModel vm) => Form("Bağımsız kart hareketi",
        Metin(HarcamaNotu),
        Alan("Tarih", Tarih(nameof(vm.HarcamaTarihi))), Alan("Açıklama", Girdi(nameof(vm.HarcamaAciklama))),
        Alan("Tutar (iade için eksi)", Girdi(nameof(vm.HarcamaTutari), true)), Alan("Taksit sayısı", Girdi(nameof(vm.TaksitSayisi), sayi: true)),
        Onay("İlk hesap kesim tarihini belirle", nameof(vm.IlkKesimVar)), Goster(Alan("İlk kesim tarihi", Tarih(nameof(vm.IlkKesimTarihi))), nameof(vm.IlkKesimVar)),
        Goster(Alan("İade edilen harcama (kanal payları kaynaktan alınır)", Secim(nameof(vm.IadeKaynaklari), nameof(vm.IadeKaynagi), "Baslik")), nameof(vm.IadeGirisi)),
        Goster(new VerticalStackLayout
        {
            Spacing = 10,
            Children = { Metin(HarcamaPayNotu), Paylar(vm.HarcamaPaylari, () => vm.PayEkle(vm.HarcamaPaylari)) }
        }, nameof(vm.HarcamaGirisi)),
        Dugme("Kart hareketini kaydet", nameof(vm.HarcamaKaydetCommand)),
        Benzerlik(nameof(vm.HarcamaBenzerlik), nameof(vm.HarcamayiAyriKaydetCommand)));

    private static View Masraf(KartTakipViewModel vm) => Form("Faiz / masrafı kalan borca dağıt",
        Metin(MasrafNotu),
        Alan("Kesilmiş açık ekstre", Secim(nameof(vm.MasrafEkstreleri), nameof(vm.MasrafEkstresi))),
        Alan("Tarih", Tarih(nameof(vm.MasrafTarihi))), Alan("Bankanın bildirdiği faiz / masraf", Girdi(nameof(vm.MasrafTutari), true)),
        Alan("Açıklama", Girdi(nameof(vm.MasrafAciklama))),
        Dugme("Kanal dağılımını göster", nameof(vm.MasrafOnizleCommand)), Bagli(nameof(vm.MasrafOnizleme)),
        Dugme("Gösterilen masrafı kaydet", nameof(vm.MasrafKaydetCommand)));

    private static View Ekstre(KartTakipViewModel vm) => Form("Ekstre bilgisi",
        Metin(EkstreNotu),
        Alan("Son ödeme tarihi", Tarih(nameof(vm.EkstreSonOdeme))),
        Onay("Banka asgari ödeme tutarı girildi", nameof(vm.AsgariVar)), Alan("Asgari ödeme", Girdi(nameof(vm.AsgariTutar), true)),
        Alan("Açıklama", Girdi(nameof(vm.Gerekce))), Dugme("Ekstreyi kaydet", nameof(vm.EkstreKaydetCommand)));

    private static View Gecis(KartTakipViewModel vm)
    {
        // Kabul edilemez önizlemede (KabulEdilebilir=false) onay kutusu ve düğme kapalıdır; neden kırmızı yazılır.
        var gecisEngeli = BagliHata(nameof(vm.GecisEngeli));
        var gecisOnayi = Onay("Gösterilen kasa ve kanal etkisini inceledim; geçişi onaylıyorum.", nameof(vm.GecisOnay));
        gecisOnayi.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.GecisOnaylanabilir));
        // Sunucu farklı bir tutar önerirse alana kendiliğinden yazılmaz; web'deki gibi açık eylemle uygulanıp yeniden önizlenir.
        var gecisOnerisi = new Button { HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
        gecisOnerisi.SetBinding(Button.TextProperty, nameof(vm.GecisOneriMetni));
        gecisOnerisi.SetBinding(Button.CommandProperty, nameof(vm.OnerilenleGecisOnizleCommand));
        return Form("Eski kartı yeni takibe al",
            Metin(GecisNotu),
            Alan("Geçiş tarihi", Tarih(nameof(vm.GecisTarihi))), Alan("Kalan kart borcu", Girdi(nameof(vm.GecisKalanBorc), true)),
            Alan("Bu borcun kasada önceden sayılmış kısmı", Girdi(nameof(vm.OncedenSayilan), true)),
            Paylar(vm.GecisPaylari, () => vm.PayEkle(vm.GecisPaylari)), Alan("Geçiş açıklaması", Girdi(nameof(vm.GecisAciklama))),
            Dugme("Geçiş farkını göster", nameof(vm.GecisOnizleCommand)), Bagli(nameof(vm.GecisOnizleme)),
            Goster(gecisEngeli, nameof(vm.GecisEngeli), true), Goster(gecisOnerisi, nameof(vm.GecisOneriVar)), gecisOnayi,
            Dugme("Yeni takibi aç", nameof(vm.GecisiOnaylaCommand)));
    }

    // ---- Sekmeler ----

    private View Sekmeler(KartTakipViewModel vm)
    {
        var sekmeler = new CipGrubu();
        sekmeler.SetBinding(BindableLayout.ItemsSourceProperty, nameof(vm.Sekmeler));
        sekmeler.SetBinding(CipGrubu.SecCommandProperty, nameof(vm.SekmeSecCommand));
        var ekstreler = Liste<EkstreSatiri>(nameof(vm.Ekstreler), s => { vm.EkstreSecCommand.Execute(s); return Task.CompletedTask; }, "Tarih / asgari ödeme",
            _ => vm.EditorMu && vm.YeniTakip);
        // Eski borç devri iptal edilmez ("Eski borç devri" bölümünden düzeltilir); kilitli avans dağıtımı da ayrıca iptal edilmez.
        var harcamalar = Liste<HarcamaSatiri>(nameof(vm.Harcamalar), s => HarcamayiAcAsync(vm, s), "Hareketi iptal et",
            s => vm.EditorMu && vm.YeniTakip && !vm.DevirSatiri(s) && (!s.Veri.Iptal || s.Veri.EkstreKayitId is not null),
            s => s.Veri.EkstreKayitId is not null ? "Kaynak PDF / iptal" : "Hareketi iptal et");
        var odemeler = Liste<KartOdemeSatiri>(nameof(vm.Odemeler), s => OdemeyiAcAsync(vm, s), "Ödemeyi iptal et",
            s => vm.EditorMu && vm.YeniTakip && !s.AvansDagitimi && (!s.Veri.Iptal || s.Veri.EkstreKayitId is not null),
            s => s.Veri.EkstreKayitId is not null ? "Kaynak PDF / iptal" : "Ödemeyi iptal et");
        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] },
                sekmeler,
                Durumda(ekstreler, nameof(vm.SeciliSekme), KartSekmesi.Ekstreler),
                Durumda(harcamalar, nameof(vm.SeciliSekme), KartSekmesi.Harcamalar),
                Durumda(odemeler, nameof(vm.SeciliSekme), KartSekmesi.Odemeler),
            },
        };
    }

    private async Task HarcamayiAcAsync(KartTakipViewModel vm, HarcamaSatiri s)
    {
        if (!vm.EditorMu)
            return;
        if (s.Veri.EkstreKayitId is { } id)
        {
            await Shell.Current.GoToAsync($"//ekstreaktar?KayitId={id}");
            return;
        }
        await GerekceyleAsync("Kart hareketini iptal et", (gerekce, _) =>
        {
            vm.Gerekce = gerekce;
            return vm.HarcamaIptalAsync(s);
        });
    }

    private async Task OdemeyiAcAsync(KartTakipViewModel vm, KartOdemeSatiri s)
    {
        if (!vm.EditorMu)
            return;
        if (s.Veri.EkstreKayitId is { } id)
        {
            await Shell.Current.GoToAsync($"//ekstreaktar?KayitId={id}");
            return;
        }
        await GerekceyleAsync("Kart ödemesini iptal et", (gerekce, _) =>
        {
            vm.Gerekce = gerekce;
            return vm.OdemeIptalAsync(s);
        });
    }

    // ---- Yapı taşları ----

    private static Label Baslik(string metin) => new() { Text = metin, Style = (Style)Application.Current!.Resources["LblTakipKartBaslik"] };

    /// <summary>Form: kart başlığı stilinde başlık ve alanlar (önceki sayfadaki kartın içeriği).</summary>
    private static VerticalStackLayout Form(string baslik, params View[] icerik)
    {
        var form = new VerticalStackLayout { Spacing = 12 };
        form.Add(Baslik(baslik));
        foreach (var v in icerik)
            form.Add(v);
        return form;
    }

    /// <summary>Özet ya da form içindeki alt bölüm: kalın küçük başlık ve içerik.</summary>
    private static VerticalStackLayout AltBolum(string baslik, params View[] icerik)
    {
        var bolum = new VerticalStackLayout { Spacing = 8 };
        bolum.Add(new Label { Text = baslik, FontAttributes = FontAttributes.Bold, Style = (Style)Application.Current!.Resources["LblTakipKucuk"] });
        foreach (var v in icerik)
            bolum.Add(v);
        return bolum;
    }

    /// <summary>Görünüm yalnız bağlı durum <paramref name="deger"/> iken görünür (açık form, seçili sekme).</summary>
    private static View Durumda<TDurum>(View gorunum, string yol, TDurum deger) where TDurum : struct, Enum
    {
        gorunum.SetBinding(IsVisibleProperty, yol, converter: new DurumdaIse<TDurum>(deger));
        return gorunum;
    }

    private sealed class DurumdaIse<TDurum>(TDurum deger) : IValueConverter where TDurum : struct, Enum
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is TDurum durum && durum.Equals(deger);
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
```

- [ ] **Adım 4: Windows derlemesi**

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
Beklenen: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Adım 5: Sayfa kaynağı denetimleri (App.Core.Tests)**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MauiKayitTutarliligiTests|FullyQualifiedName~GorunumEsdegerligiTests"`
Beklenen: `Passed!` ve `Failed: 0` (`Sayfalar_gerekce_penceresini_yalniz_GerekceyleAsync_ile_acar`, `Sayfalar_rolu_ekrana_atamaz_rolu_modelden_okur`, `Kodla_kurulan_liste_sablonlarinin_oge_turleri_baslik_ve_ozet_tasir` sayfayı tarar).

- [ ] **Adım 6: maui-lint tabanı**

Çalıştır: `bash .github/scripts/maui-lint.sh`
Beklenen: `UYARI Kasa.App/Views/KartTakipPage.cs  200 karakterden uzun satır sayısı 4 → 0 azaldı; tabanı düşürün…` ve `maui-lint: 1 dosyada sayı tabanın altında; tabanı düşürün.` (çıkış kodu 0). `HATA` satırı çıkarsa (uzun satır arttı ya da tanımsız anahtar) önce onu düzelt.
Çalıştır: `bash .github/scripts/maui-lint.sh --tabani-guncelle`
Beklenen: `Taban yazıldı: .github/scripts/maui-lint-tabani.txt (9 satır).` ve dosyadan `satir	Kasa.App/Views/KartTakipPage.cs	4` satırı kalkar.
Çalıştır: `bash .github/scripts/maui-lint.sh` → `maui-lint: taban içinde.`

- [ ] **Adım 7: Biçim ve commit**

Çalıştır: `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes` → çıktı yok.

```bash
git add Kasa.App/Views/TakipUi.cs Kasa.App/Views/KartTakipPage.cs .github/scripts/maui-lint-tabani.txt
git commit -F - <<'EOF'
feat(app): kartlar ekranı kutular, açılan ayrıntı, tek form alanı ve sekmelerle

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Görev 9: Doğrulama ve gerçek pencerede ekran görüntüleri

**Dosyalar:** depoda değişiklik yok (yalnız taban/biçim düzeltmesi gerekirse). Betikler ve görüntüler oturum karalama klasöründedir.

Karalama kökü (bundan sonra `$K`): `C:\Users\burak\AppData\Local\Temp\claude\C--Users-burak-source-repos-Kasa\2cf5258e-ca1b-45f0-9c5a-07f7d4bbbb74\scratchpad`. Önceki koşunun betikleri: `$K\donma\` (kosu.ps1, bekci.ps1, sunucu.ps1, Pencere.cs, Masaustu.cs, kimlik.json) ve `$K\gorsel\`. Yeni betik yazılmaz; `donma` betikleri kopyalanır ve yalnız aşağıdaki üç yerde değiştirilir (sunucu yolu, menü öğesi bulma, kutu/düğme adımları).

- [ ] **Adım 1: Bütün testler ve derleme**

Çalıştır (sırayla, tek tek):
- `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false` → `Passed!`, `Failed: 0`
- `dotnet test Kasa.Sozlesme.Tests/Kasa.Sozlesme.Tests.csproj -c Release -m:2 -nodeReuse:false` → `Passed!`, `Failed: 0`
- `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false` → `0 Warning(s)`, `0 Error(s)`
- `bash .github/scripts/maui-lint.sh` → `maui-lint: taban içinde.`
- `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes` → çıktı yok

- [ ] **Adım 2: Test sunucusunu derle**

Çalıştır: `dotnet build Kasa.Ui.E2E/Sunucu/Kasa.Ui.E2E.Sunucu.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `0 Error(s)`; `Kasa.Ui.E2E\Sunucu\bin\Release\net10.0\Kasa.Ui.E2E.Sunucu.exe` oluşur.

- [ ] **Adım 3: Betikleri ve uygulamayı karalama klasörüne kopyala (PowerShell)**

```powershell
$K = 'C:\Users\burak\AppData\Local\Temp\claude\C--Users-burak-source-repos-Kasa\2cf5258e-ca1b-45f0-9c5a-07f7d4bbbb74\scratchpad'
$G = Join-Path $K 'menu-kartlar-gorsel'
New-Item -ItemType Directory -Force $G | Out-Null
foreach ($d in 'kosu.ps1', 'bekci.ps1', 'sunucu.ps1', 'Pencere.cs', 'Masaustu.cs', 'kimlik.json') { Copy-Item (Join-Path $K "donma\$d") $G -Force }
# bekci.ps1 sonunda oturum dosyasını bu kopyayla karşılaştırır: kullanıcının GÜNCEL oturum dosyası alınır.
$depo = Join-Path $env:LOCALAPPDATA 'User Name\com.royalmezat.kasa\Settings\securestorage.dat'
if (Test-Path $depo) { Copy-Item $depo (Join-Path $G 'securestorage.dat.ozgun') -Force }
$exe = Get-ChildItem 'C:\Users\burak\source\repos\Kasa-paket\menu-kartlar\Kasa.App\bin\Release\net10.0-windows10.0.19041.0' -Recurse -Filter Kasa.App.exe | Select-Object -First 1
Copy-Item $exe.DirectoryName (Join-Path $G 'app') -Recurse -Force
Test-Path (Join-Path $G 'app\Kasa.App.exe')
```
Beklenen: `True`.

- [ ] **Adım 4: Kopyalanan betiklerde üç değişiklik**

(a) `$G\sunucu.ps1`'de (Edit aracıyla):
```powershell
$exe = 'C:\Users\burak\source\repos\Kasa-paket\menu-donmasi\Kasa.Ui.E2E\Sunucu\bin\Release\net10.0\Kasa.Ui.E2E.Sunucu.exe'
```
→
```powershell
$exe = 'C:\Users\burak\source\repos\Kasa-paket\menu-kartlar\Kasa.Ui.E2E\Sunucu\bin\Release\net10.0\Kasa.Ui.E2E.Sunucu.exe'
```

(b) `$G\kosu.ps1`'de menü öğesi bulma: menü artık `ShellFlyoutItemView` değil, adıyla bir düğmedir. Şu bloğu:
```powershell
# Menü öğeleri UIA'da ad taşımaz (ListItem 'ShellFlyoutItemView'); AppShell.xaml sırasıyla seçilir.
$MenuSirasi = @('Kasalar', 'Haftalık', 'Aylık', 'İşlemler', 'Aylık Giderler', 'Ekstre İçe Aktar', 'Ayarlar', 'Alışlar', 'Rapor Dışa Aktar', 'Kartlar', 'Krediler', 'Bildirimler')
function MenuSec([string]$ad) {
```
ve fonksiyonun gövdesini kapanış `}` satırına kadar (son satırı `    return "$yol, UYARI sayfa başlığı '$ad' 10 sn içinde görünmedi"` ve ardından `}`) şununla değiştir:
```powershell
# Menü öğesi, kart kutusu ve sayfa düğmeleri UIA'da adlı Button'dur (menü: SemanticProperties.Description = öğe başlığı;
# kart kutusu: "<kart adı> kartı"; TakipUi düğmeleri: metni). Görünmeyen (Collapsed) öğe ağaçta yoktur.
function Dugme([string]$ad) {
    $kosul = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $ad)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)))
    for ($i = 0; $i -lt 40; $i++) { $e = $script:Kok.FindFirst($TS::Descendants, $kosul); if ($e) { return $e }; Start-Sleep -Milliseconds 250 }
    throw "düğme bulunamadı: $ad"
}
function Bas([string]$ad) {
    (Dugme $ad).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 1500
}
function MenuSec([string]$ad) { Bas $ad; return "Invoke '$ad'" }
```
Aynı dosyada menü döngüsündeki şu üç satırı:
```powershell
            $yol = Yakala "menu-$m"
            $sira = [array]::IndexOf($MenuSirasi, $m)
            Log ("ZEMIN menu-{0}: secili oge (y={1}) {2}; digerleri: {3}" -f $m, (127 + 46 * $sira), [Pencere]::Piksel($yol, 200, 127 + 46 * $sira), ((0..3 | Where-Object { $_ -ne $sira } | ForEach-Object { [Pencere]::Piksel($yol, 200, 127 + 46 * $_) }) -join ","))
```
şununla değiştir:
```powershell
            Yakala "menu-$m" | Out-Null
```
ve hemen sonraki `        }` (foreach kapanışı) ile `        if ($Menu.Count) {` arasına:
```powershell
        # Koşuya özel adımlar: $G\<Ad>.adimlar.txt (UTF-8, satır başına bir düğme adı); her adımdan sonra ekran görüntüsü.
        $adimDosyasi = Join-Path $G "$Ad.adimlar.txt"
        if (Test-Path $adimDosyasi) {
            $n = 0
            foreach ($adim in (Get-Content -Encoding utf8 $adimDosyasi | Where-Object { $_ })) {
                $n++
                Bas $adim
                Log "adım $n '$adim'"
                Yakala "adim-$n" | Out-Null
            }
        }
```

(c) `$G\kosu.ps1`'de kimlik dosyası koşuya özel olabilir:
```powershell
    $k = Get-Content (Join-Path $G 'kimlik.json') -Raw | ConvertFrom-Json
```
→
```powershell
    $kimlikYolu = Join-Path $G "$Ad.kimlik.json"; if (-not (Test-Path $kimlikYolu)) { $kimlikYolu = Join-Path $G 'kimlik.json' }
    $k = Get-Content $kimlikYolu -Raw | ConvertFrom-Json
```

- [ ] **Adım 5: Test sunucusunu başlat**

Çalıştır: `powershell -NoProfile -ExecutionPolicy Bypass -File "$G\sunucu.ps1" -Is baslat`
Beklenen: `sunucu hazır (PID …)`. (Sunucu geçici SQLite, sabit gün 2026-09-25, yalnız 127.0.0.1:5390; canlı veriye dokunmaz.)

- [ ] **Adım 6: Test verisini API ile tohumla (PowerShell)**

Boş veritabanında takip başlangıcı sunucunun bugünüdür (Program.cs: `TakipBaslangic = db.Bugunu()`); geçmiş ekstre için önce 2026-08-01'e çekilir (henüz mali kayıt yokken izinlidir). Eski (takipsiz) kart API'yle oluşturulamaz (`POST /api/kredikartlari` her zaman 409): uyarı etiketleri "Son ödeme geçti" ve "Pasif"tir. Kanallar sunucuca kurulur (1 MEZAT, 2 PERAKENDE, 3 TOPTAN).

```powershell
$G = 'C:\Users\burak\AppData\Local\Temp\claude\C--Users-burak-source-repos-Kasa\2cf5258e-ca1b-45f0-9c5a-07f7d4bbbb74\scratchpad\menu-kartlar-gorsel'
$k = Get-Content (Join-Path $G 'kimlik.json') -Raw | ConvertFrom-Json
$u = 'http://127.0.0.1:5390/api'
$giris = Invoke-RestMethod -Method Post "$u/auth/login" -ContentType 'application/json' -Body (@{ kullanici = $k.kullanici; sifre = $k.sifre } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($giris.token)" }
function Gonder($yontem, $yol, $govde) {
    Invoke-RestMethod -Method $yontem "$u/$yol" -Headers $h -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($govde | ConvertTo-Json -Depth 5)))
}
function Kart($ad, $limit, $kesim, $sonOdeme) {
    Gonder Post 'takip/kartlar' @{ istekId = [guid]::NewGuid().ToString(); surum = 0; ad = $ad; limit = $limit; kesimGunu = $kesim; sonOdemeGunu = $sonOdeme; acilisTarihi = '2026-08-01'; acilisBorc = 0; acilisDagilimlari = @() }
}
function Harcama($kart, $tarih, $aciklama, $tutar, $kanal) {
    Gonder Post "takip/kartlar/$($kart.id)/harcamalar" @{ istekId = [guid]::NewGuid().ToString(); surum = $kart.surum; tarih = $tarih; aciklama = $aciklama; tutar = $tutar; taksitSayisi = 1; ilkKesimTarihi = $null; dagilimlar = @(@{ kanalId = $kanal; tutar = $tutar }) }
}
$ayar = Invoke-RestMethod "$u/ayarlar" -Headers $h
Gonder Put 'ayarlar' @{ takipBaslangic = '2026-08-01'; kasaAcilisDevri = 0; surum = $ayar.surum } | Out-Null
$garanti = Harcama (Kart 'Garanti Bonus' 40000 10 20) '2026-08-05' 'Malzeme alışı' 12500 1
$akbank = Harcama (Kart 'Akbank Axess' 20000 25 5) '2026-09-15' 'Yakıt' 3200 2
$yapi = Kart 'Yapı Kredi World' 15000 1 10
Gonder Post "takip/kartlar/$($yapi.id)/durum" @{ istekId = [guid]::NewGuid().ToString(); surum = $yapi.surum; aktif = $false; aciklama = 'Kart kullanılmıyor' } | Out-Null
# İzleyici şifresi (12+ karakter) üretilir, koşuya özel kimlik dosyasına yazılır; sohbete yazılmaz.
$izleyici = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 20 | ForEach-Object { [char]$_ })
Gonder Put 'ayarlar/izleyici-sifre' @{ yeniSifre = $izleyici } | Out-Null
@{ kullanici = ''; sifre = $izleyici; jwt = $k.jwt } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $G 'kartlar-izleyici.kimlik.json')
Invoke-RestMethod "$u/takip/kartlar" -Headers $h | Select-Object ad, borc, aktif, @{ n = 'acikEkstreSonOdeme'; e = { ($_.ekstreler | Where-Object kalan -gt 0 | ForEach-Object sonOdemeTarihi) -join ',' } } | Format-Table -AutoSize
```
Beklenen tablo: `Garanti Bonus 12500 True` (açık ekstre son ödemesi 2026-09-25'ten önce, ör. `2026-08-20`), `Akbank Axess 3200 True` (son ödemesi 2026-09-25'ten sonra ya da boş), `Yapı Kredi World 0 False`. Garanti'nin açık ekstresi yoksa (sunucu ekstreyi farklı kesiyorsa) görüntüde "Son ödeme geçti" etiketi çıkmaz; bunu raporda belirt, "Pasif" etiketi yine uyarılı kutudur. Bir istek 4xx dönerse yanıt gövdesini oku ve alan adını `Kasa.Api/FinansTakipDtos.cs`'teki kayıtla karşılaştır.

- [ ] **Adım 7: Koşu adımlarını yaz**

```powershell
Set-Content -Encoding utf8 (Join-Path $G 'kartlar-editor.adimlar.txt') -Value @('Garanti Bonus kartı', 'Ödeme kaydet')
Set-Content -Encoding utf8 (Join-Path $G 'kartlar-izleyici.adimlar.txt') -Value @('Garanti Bonus kartı')
```

- [ ] **Adım 8: Editör koşusu (ayrı masaüstü "KasaTest", TEST başlıklı pencere)**

Çalıştır: `powershell -NoProfile -ExecutionPolicy Bypass -File "$G\bekci.ps1" -Dizin "$G\app" -Ad kartlar-editor -Konum masaustu -Izle 8 -Menu Kartlar -Sure 240`
Beklenen günlük satırları: `oturum dosyası yedeklendi`, `giriş tıklandı`, `OZET giris-sonrasi: … yanitsiz 0 sn`, `menü 'Kartlar': Invoke 'Kartlar'`, `adım 1 'Garanti Bonus kartı'`, `adım 2 'Ödeme kaydet'`, `oturum dosyası geri yüklendi`; son satır `açık Kasa.App: 0; oturum dosyası = özgün: True`.
Görüntüler (`$G\kartlar-editor\`): `ana.png` (editör menüsü, Kasalar seçili), `menu-Kartlar.png` (kutular: üç kart + "Yeni kart ekle"), `adim-1.png` (Garanti açık: özet, düğmeler, sekmeler), `adim-2.png` (açık ödeme formu).

- [ ] **Adım 9: İzleyici koşusu**

Çalıştır: `powershell -NoProfile -ExecutionPolicy Bypass -File "$G\bekci.ps1" -Dizin "$G\app" -Ad kartlar-izleyici -Konum masaustu -Izle 8 -Menu Kartlar -Sure 240`
Beklenen: günlükte `adım 1 'Garanti Bonus kartı'`, son satır `açık Kasa.App: 0; oturum dosyası = özgün: True`. Görüntüler (`$G\kartlar-izleyici\`): `ana.png` (izleyici menüsü: Özet, Kayıtlar › İşlemler/Aylık giderler, Kart ve kredi, Diğer › Rapor dışa aktar, Çıkış), `menu-Kartlar.png` ("Yeni kart ekle" kutusu yok), `adim-1.png` (açık kart: özet ve sekmeler var, düğme ve form yok).

- [ ] **Adım 10: Sunucuyu durdur**

Çalıştır: `powershell -NoProfile -ExecutionPolicy Bypass -File "$G\sunucu.ps1" -Is durdur`
Beklenen: `sunucu durdu; 5390 dinleyen: 0`.

- [ ] **Adım 11: Görüntüleri incele**

Read aracıyla altı PNG'yi aç ve denetle: menü dört grup + ayrı Çıkış, simgeler kare/boş değil (glyph çiziliyor), seçili öğe koyu zemin + nokta; kutular banka renklerinde (Garanti yeşil, Akbank kırmızı, Yapı Kredi lacivert), en az 220 px, bir satırda; Garanti'de "Son ödeme geçti", Yapı Kredi'de "Pasif" etiketi; açık kartta ayrıntı kutu satırının altında tam genişlikte; ödeme formu düğmelerin altında ve "Vazgeç" görünür; izleyicide düğme/form/"Yeni kart ekle" yok. Sorun görürsen düzelt, ilgili görevin testlerini ve bu görevin 1. adımını yeniden çalıştır, görüntüleri yeniden al.

- [ ] **Adım 12: Görüntüleri PR için topla ve son durum**

```powershell
$E = Join-Path $G 'pr-ekran'
New-Item -ItemType Directory -Force $E | Out-Null
Copy-Item "$G\kartlar-editor\ana.png" "$E\1-menu-editor.png"
Copy-Item "$G\kartlar-editor\menu-Kartlar.png" "$E\2-kart-kutulari.png"
Copy-Item "$G\kartlar-editor\adim-1.png" "$E\3-acik-kart.png"
Copy-Item "$G\kartlar-editor\adim-2.png" "$E\4-odeme-formu.png"
Copy-Item "$G\kartlar-izleyici\adim-1.png" "$E\5-izleyici.png"
Get-ChildItem $E | Select-Object Name, Length
```
Beklenen: beş dosya. Görüntüler PR açıklamasına eklenecektir (PR bu planın dışında, denetleyen ajan açar).

Çalıştır: `git status --short` → yalnız bu görevde düzeltme yapıldıysa değişiklik görünür; yapıldıysa:

```bash
git add -A Kasa.App Kasa.App.Core Kasa.App.Core.Tests .github/scripts/maui-lint-tabani.txt
git commit -F - <<'EOF'
fix(app): menü ve kartlar ekran görüntüsü denetiminde bulunan düzeltmeler

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Tasarım maddeleri → görevler

| Tasarım maddesi | Görev |
|---|---|
| Menü dört grup, sıra, Çıkış ayrı | 1 (model), 2 (XAML) |
| Rollere göre görünen menü (alıcı, izleyici, editör), boş grup gizli | 1 |
| Öğe simgesi; seçili SidebarActive + nokta, üzerinde SidebarHover | 2 |
| Menü üst bilgisi değişmez | 2 (FlyoutHeader aynen) |
| FlyoutContent, Shell rotası/geri tuşu/yaşam döngüsü aynı, FlyoutItem yalnız rota kaynağı | 2 |
| MenuModeli seçili rotayı tutar; girişe dönüşte boşalır | 1, 2 (OnNavigated, MenuyuGoster([])) |
| Doğrudan rota erişim kuralı korunur | 2 (IsVisible sözlüğü aynen) |
| BackgroundColor; `Kabuk_menu_sablonlari…` yeni menüyü kapsar; maui-lint | 2 |
| Simge kaynağı, sürüm/lisans doğrulaması | 2 (SimgeAilesi; karar 1) |
| Kutular, en az 220 px, genişliğe göre satır, "Yeni kart ekle" yalnız editörde | 5, 6, 8 |
| Kutu içeriği (ad, borç, doluluk, limit, son ödeme, ≤2 etiket, sıra) | 4, 6 |
| Renk: banka eşlemesi, Türkçe/büyük-küçük harf, tanınmayan bankada sabit renk | 3 |
| Colors.xaml anahtarları, 4,5:1 kontrast testi | 3 |
| Tıklayınca ayrıntı kutu satırının altında tam genişlikte; tekrar tıklama kapatır; tek kart açık | 5, 6, 7, 8 |
| Ayrıntı: özet (kanal payları, geçiş uyarısı), düğmeler, form alanı, sekmeler | 8 |
| Tek form, düğme formu değiştirir, kayıt/Vazgeç kapatır, hata formda | 7, 8 |
| Formların alanları/önizlemeleri/uyarıları aynı | 8 (içerik taşındı) |
| Sekmeler (varsayılan Ekstreler), ekstre → Ekstre bilgisi formu, iptal/ekstre aktarımı aynı | 7, 8 |
| Yeni kart formu kutuların altında; kayıttan sonra yeni kart açık | 7, 8 |
| İzleyicide düğme, form, "Yeni kart ekle" yok | 6, 7, 8 |
| `//kartlar?KartId=` ile gelen kart açık | 7 (IdIleSec), 8 (OnAppearing aynen) |
| TimeProvider ile "Son ödeme geçti" | 4 |
| Mevcut kart testleri değişmeden geçer | 4, 7, 9 |
| Gerçek pencere görüntüleri (ayrı masaüstü, TEST penceresi, oturum dosyası yedeği) | 9 |
| Release 0 uyarı, App.Core ve Sözleşme testleri, maui-lint, dotnet format | 2, 6, 8, 9 |
