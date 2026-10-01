# Masaüstü Windows bildirimleri — uygulama planı

> **Ajanlar için:** görev görev uygulanır (superpowers:subagent-driven-development; tek oturumda superpowers:executing-plans).
> Adımlar `- [ ]` onay kutularıyla izlenir. Tasarım: `docs/specs/2026-09-30-masaustu-bildirimleri.md`.

**Amaç:** Sunucunun ürettiği bildirimleri (kart kesimi, son ödeme, kredi taksiti, kasa alt sınırı, sistem hatası) masaüstü uygulaması
açıkken 5 dakikada bir, kapalıyken kullanıcı düzeyi zamanlanmış görevle Windows bildirimi olarak göstermek; tıklanınca ilgili kartı
ya da krediyi açmak; Bildirimler ekranına "Bu bilgisayarda Windows bildirimleri" kartını ve menüye okunmamış rozetini eklemek.

**Mimari:** Karar veren her şey Windows'tan bağımsız Kasa.App.Core'dadır ve sahtelerle sınanır: `BildirimYoklayici` (liste → yeni
ve okunmamışları ayır → göster → sayıyı tut), `BildirimNobetcisi` (oturum, ayar, görev saati), `BildirimKontrolu` (pencere açmadan
çalışma), `BildirimGorevi` (schtasks, XML), yerel dosyalar (`%LOCALAPPDATA%\EmarKasa`). Kasa.App yalnız ince katmandır:
`WindowsBildirimGosterici` (AppNotificationManager), `Platforms/Windows/App.xaml.cs` (`--bildirim-kontrol`, kayıt, etkinleştirme),
AppShell (5 dakikalık zamanlayıcı, tıklama, rozet) ve Bildirimler sayfası.

**Teknoloji:** .NET 10, MAUI 10.0.110 (Windows, paketsiz: `WindowsPackageType=None`), Windows App SDK `AppNotificationManager`
(MAUI 10.0.110 ile örtük gelen `Microsoft.WindowsAppSDK` 1.8.260529003; bildirim API'si `Microsoft.WindowsAppSDK.Foundation`
1.8.260527000 içinde — kardeş çalışma ağacı `Kasa-paket/menu-kartlar/Kasa.App/obj/project.assets.json`'dan okundu, Görev 6
Adım 1'de bu çalışma ağacında yeniden doğrulanır), CommunityToolkit.Mvvm 8.4.2, xUnit v3 (`xunit.v3.mtp-off` 4.0.1).

---

## Çalışma kuralları (her görevde geçerli)

- **Yer:** çalışma ağacı `C:/Users/burak/source/repos/Kasa-paket/masaustu-bildirim`, dal `ozellik/masaustu-bildirim`
  (origin/release/2.x ccfe58b + tasarım commit'i f8b3d5b). Her komut bu klasörde çalışır. **Push yok.**
- **dotnet:** aynı anda tek `dotnet` komutu; her derleme ve test komutuna `-m:2 -nodeReuse:false` eklenir. Komutlar:
  - Birim test: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~X"`
  - Uygulama derlemesi: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
  - MAUI denetimi: `bash .github/scripts/maui-lint.sh`
  - Biçim: `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes`
  - Başlangıç test sayıları: Kasa.App.Core.Tests 910, Kasa.Sozlesme.Tests 55.
- **xUnit1051:** testte `CancellationToken` alan bir yöntem (ya da böyle bir aşırı yüklemesi olan yöntem, örn. `Task.Delay`)
  çağrılırsa `TestContext.Current.CancellationToken` verilir. Bu plandaki yeni üretim yöntemleri belirteç almaz.
- **Adlar Türkçe** (tür, yöntem, değişken, test adı); mevcut adlandırma desenine uyulur.
- **Kaçış dizileri:** yazma araçları C# kaynağındaki `\u00B7`, `\"`, `\\` gibi kaçışları çıplak karaktere çevirebilir. Plan kodu
  kaçış dizisi kullanmaz (tırnak için `@"..""..`, ters bölü için `@"..\.."`); dosyayı yazdıktan sonra
  `git diff` ile `@"` dizelerini ve `·` karakterini denetleyin.
- **Zeminler:** MAUI görünümlerinde zemin `BackgroundColor` ile yazılır, `Background` (Brush) yazılmaz (dotnet/maui#38813 dersi;
  `MauiKayitTutarliligiTests.Kabuk_menu_sablonlari_Background_firca_ozelligini_yazmaz`).
- **Renkler** yalnız `Kasa.App/Resources/Styles/Colors.xaml` anahtarlarından gelir; onaltılık renk yazılmaz. `maui-lint` tabanı
  (`.github/scripts/maui-lint-tabani.txt`) artmaz; Kasa.App'te satır en çok 200 karakterdir.
- **Tek satırlık çok deyimli blok yok:** `{ a; b; }` yazılmaz (`.editorconfig`: `csharp_preserve_single_line_statements = false`).
  `if (x)` sonrası deyim alt satıra yazılır. Tek deyimli tek satır blok (`catch (Exception) { }`) mevcut desendir, kullanılabilir.
- **Uygulama çalıştırılmaz** (Kasa.App.exe başlatılmaz, schtasks ile görev kurulmaz); tek istisna Görev 11'dir ve orada her adım
  kullanıcının sohbetteki açık onayıyla yapılır.
- **Commit biçimi:** Türkçe konu satırı, boş satır, gövde (gerekirse), boş satır, `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
  Örnek:

  ```bash
  git commit -F - <<'EOF'
  feat(app-core): bildirim hedefi ve görev saati

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  EOF
  ```

---

## Verilmiş teknik kararlar

1. **Paketsiz uygulamada kayıt ve tıklama.** Pencereli süreç `NotificationInvoked` işleyicisini **önce** bağlar, sonra
   `AppNotificationManager.Default.Register()` çağırır: paketsiz uygulamada Register çağıran süreci COM sunucusu olarak kaydeder,
   uygulama adı ve simgesi kabuktan alınır; işleyici Register'dan önce bağlanırsa uygulama açıkken tıklama aynı süreçte olay olarak
   gelir, bağlanmazsa Windows yeni süreç başlatır. Süreç bitmeden `Unregister()` çağrılır ("sonraki bildirimlerde uygulamanın
   başlatılması için"). `AppNotificationManager` Singleton paketine bağlı değildir (self-contained dağıtımda da çalışır).
   Kaynaklar:
   <https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register>,
   <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps#dependencies-on-additional-msix-packages>.
2. **Uygulama kapalıyken tıklama.** Windows exe'yi başlatır. Hızlı başlangıç belgesi iki yolu birlikte anlatıyor: etkinleştirme
   türü `ExtendedActivationKind.AppNotification` ise argümanlar `AppInstance.GetCurrent().GetActivatedEventArgs().Data`'dadır;
   COM etkinleştirmesinde tür `Launch` görünür ve argümanlar Register'dan sonra `NotificationInvoked` ile gelir. İkisi de aynı
   tıklama kuyruğuna (`BildirimTiklamalari`, son değer geçerli) yazılır; aynı tıklamanın iki kez gelmesi zararsızdır. Register,
   `GetActivatedEventArgs`'tan önce çağrılır. Kaynak:
   <https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart>.
3. **Tek örnek.** Uygulama açıkken tıklama çalışan sürece gelir (karar 1); `AppInstance.FindOrRegisterForKey` / yönlendirme
   gerekmez ve eklenmez. Pencere açmadan çalışan görev işleyici bağlamadan kaydolur: onun gösterdiği bildirime tıklanırsa Windows
   yeni (pencereli) süreç başlatır. Pencereli süreç adlı bir mutex tutar (`PencereKilidi`, `Local\EmarKasa.Pencere`); görev bu
   mutex'i görürse hiçbir şey yapmadan çıkar (açık uygulama zaten bakar; iki süreç aynı bildirimi yarışmaz, iki COM kaydı karışmaz).
   Tıklama arka plan iş parçacığında gelir; kuyruğa UI `DispatcherQueue` üzerinden yazılır (MAUI `MainThread` pencere yokken
   `InvalidOperationException` atar: <https://github.com/dotnet/maui/blob/release/10.0.1xx-sr1/src/Essentials/src/MainThread/MainThread.windows.cs>).
4. **Windows ayarı.** `AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled` ise "Windows ayarlarında kapalı"
   uyarısı ve `ms-settings:notifications` bağlantısı gösterilir (DisabledForApplication, DisabledForUser, DisabledByGroupPolicy,
   Unsupported). Kaynak: <https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationsetting>.
5. **Zamanlanmış görev: tek görev, XML ile.** `schtasks /SC ONLOGON` belgeye göre "herhangi bir kullanıcı oturum açınca" demektir;
   `LogonTrigger.UserId` boşsa tetikleyici her kullanıcı içindir. Her kullanıcı için oturum tetikleyicisi yönetici hakkı ister
   (Microsoft belgesinde doğrudan yazmıyor; birden çok projede "Access is denied" raporu:
   <https://github.com/kunchenguid/no-mistakes/issues/1192>). Bu yüzden görev `schtasks /Create /TN EmarKasaBildirim /XML <dosya> /F`
   ile kurulur; XML'de `LogonTrigger/UserId` ve `Principal/UserId` bu kullanıcıdır (`ETKİALANI\kullanıcı`), `LogonType`
   `InteractiveToken`, `RunLevel` `LeastPrivilege`: yönetici olmayan kullanıcı kendi hesabıyla, etkileşimli oturum türüyle parola
   vermeden görev kaydedebilir. İki tetikleyici tek görevde: günlük `CalendarTrigger` (sunucu saati + 5 dk) ve `LogonTrigger`
   (1 dk gecikme). `StartWhenAvailable=true` (kaçan günlük çalışma bilgisayar açılınca yapılır), `ExecutionTimeLimit=PT2M`,
   `MultipleInstancesPolicy=IgnoreNew`. Kaynaklar:
   <https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create>,
   <https://learn.microsoft.com/en-us/windows/win32/taskschd/logontrigger-userid>,
   <https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks>.
   Gerçek schtasks davranışı (yönetici istemeden kurulması) Görev 11'de kullanıcının bilgisayarında doğrulanır.
6. **XML kodlaması.** Dosya UTF-16 LE, BOM'lu, bildirimi `encoding="utf-16"` yazılır (`XmlWriterSettings.Encoding = Encoding.Unicode`);
   UTF-8 bildirimli dosyayı schtasks "unable to switch the encoding" / "The task XML is malformed" ile reddediyor. Kaynak:
   <https://learn.microsoft.com/en-us/archive/msdn-technet-forums/cdc10106-11b4-4ed4-b637-b33f0c1ce01c>.
7. **Yerel ayar Preferences yerine küçük JSON (tasarımdan sapma).** Paketsiz MAUI'de `Preferences` pencere açmadan da çalışır
   (dosya `FileSystem.AppDataDirectory/../Settings/preferences.dat`; yol uygulama bilgisinden hesaplanır, pencere gerekmez). Ama
   paketsiz uygulama dosyayı süreç başında bir kez okuyup bellekte tutar ve her yazışta kilitsiz `File.Create` ile baştan yazar
   (<https://github.com/dotnet/maui/blob/release/10.0.1xx-sr1/src/Essentials/src/Preferences/Preferences.windows.cs>,
   `UnpackagedPreferencesImplementation`). Uygulama ve görev aynı dosyayı iki süreçte kullanacağı için ayar
   `%LOCALAPPDATA%\EmarKasa\bildirim-ayari.json` dosyasında tutulur (geçici dosyaya yaz + yerine taşı). Gösterilen kimlikler de
   aynı klasördedir (tasarımdaki gibi).
8. **Oturum belirteci pencere açmadan.** `SecureStorage` paketsiz uygulamada `FileSystem.AppDataDirectory/../Settings/securestorage.dat`
   dosyasını DPAPI ile okur; pencere gerekmez
   (<https://github.com/dotnet/maui/blob/release/10.0.1xx-sr1/src/Essentials/src/SecureStorage/SecureStorage.windows.cs>,
   `FileSystem.windows.cs`). Bu bilgisayarda yol `%LOCALAPPDATA%\User Name\com.royalmezat.kasa\Settings\securestorage.dat`.
   2.0'daki `--hatirlatma-kontrol` yolu da aynı yöntemi kullanıyordu (`git show 6086373`: `OnLaunched` içinde
   `CreateMauiApp()` ile DI kurulur, `base.OnLaunched` çağrılmaz, iş bitince `Exit()`).
9. **Liste bugünle sınırlanır.** `GET /api/bildirimler` son 200 bildirimi döndürür (okunmuşlar dahil). Masaüstü yalnız
   `Okundu == false` ve `Tarih == bugün` (bilgisayarın yerel günü) olanları gösterir; sunucu web push'ta da yalnız bugünün
   bildirimlerini gönderir (`BildirimServisi.GonderIc`: `x.Tarih == today && !x.Iptal`). Böylece ilk kurulumda eski okunmamışlar
   bir kerede gösterilmez. **İptal bilgisi istemcide yok:** `BildirimDto`'da `Iptal` alanı yoktur; sunucu iptal edilmiş bildirimi
   yalnız okunmuşsa ya da bir tarayıcıya gönderilmişse listeler. Bu durumda masaüstü o (iptal edilmiş ama tarayıcıya gitmiş)
   bildirimi bir kez gösterebilir. Sunucu değişikliği kapsam dışı olduğu için kabul edildi; kullanıcıya soru olarak iletilir.
10. **Tekrar göstermeme kilitle ayırma.** Depo `YenileriAyir` tek dosya kilidi (`FileShare.None`, 10 × 100 ms yeniden deneme)
    altında adayları okur, yenileri yazar ve yenileri döndürür; gösterim bundan sonra yapılır. Böylece aynı anda bakan iki süreç
    aynı bildirimi iki kez göstermez. Gösterim ayırmadan sonra başarısız olursa o bildirim masaüstünde gösterilmez ama listede ve
    telefonda kalır (Windows `Show` hatası pratikte görülmez).
11. **Rozet ayar kapalıyken gizlenir.** Ayar kapatılınca bakma durur ve rozet 0'a iner (tasarım §2 yalnız çıkış/rol için
    söylüyor; kapalı ayarda rozet bayat kalacağı için sıfırlanır). Bildirimler ekranı açılınca liste yüklenir ve rozet yine
    güncellenir.
12. **Anahtar denetimi.** Tasarımdaki "Açık/Kapalı anahtarı" mevcut ekranlardaki gibi onay kutusudur (`TakipUi.Onay`); ekrandaki
    "Hatırlatmalar açık" ile aynı görünüm.
13. **Platform kodu adları.** `BildirimGorevi` tasarımda Kasa.App altında yazılı; `EskiHatirlatmaGorevi` gibi komut kurgusu
    sınanabilsin diye Kasa.App.Core'dadır. Windows dosyaları mevcut `Platforms/Windows/App.xaml.cs` ile aynı ad alanındadır
    (`Kasa.App.WinUI`).

---

## Dosya yapısı

**Kasa.App.Core (Windows'tan bağımsız, testli)**

| Dosya | Durum | Sorumluluk |
|---|---|---|
| `Kasa.App.Core/BildirimHedefi.cs` | yeni | `Hedef` (`/#cards/{id}`, `/#loans/{id}`) → Shell rotası; tanınmayan → `//bildirimler` |
| `Kasa.App.Core/BildirimGorevZamani.cs` | yeni | sunucu saati + 5 dk (gece yarısı sarması) |
| `Kasa.App.Core/YerelKlasor.cs` | yeni | `%LOCALAPPDATA%\EmarKasa` yolu |
| `Kasa.App.Core/GosterilenBildirimDeposu.cs` | yeni | `IGosterilenBildirimDeposu` + dosya uygulaması (500 sınırı, bozuk dosya, kilit) |
| `Kasa.App.Core/BildirimAyari.cs` | yeni | `IBildirimAyari` + `DosyaBildirimAyari` (bilgisayara özel açık/kapalı) |
| `Kasa.App.Core/BildirimGosterimi.cs` | yeni | `IBildirimGosterici`, `BildirimTiklamasi` (argümanlar), `BildirimTiklamalari` (bekleyen tıklama) |
| `Kasa.App.Core/YoklamaSonucu.cs` | yeni | `YoklamaDurumu`, `YoklamaSonucu` (durum satırı metni) |
| `Kasa.App.Core/BildirimYoklayici.cs` | yeni | liste → yeni + okunmamış → göster → kaydet; okunmamış sayısı; tıklama → okundu + rota |
| `Kasa.App.Core/BildirimGorevi.cs` | yeni | `IBildirimGorevi` + schtasks/XML ile kur, güncelle, sil |
| `Kasa.App.Core/BildirimNobetcisi.cs` | yeni | uygulama açıkken: oturum, ayar, görev saati, 5 dk bakma, deneme, tıklama |
| `Kasa.App.Core/BildirimKontrolu.cs` | yeni | pencere açmadan çalışma (`--bildirim-kontrol`, 60 sn) |
| `Kasa.App.Core/PencereKilidi.cs` | yeni | pencereli süreç mutex'i |
| `Kasa.App.Core/BildirimViewModel.cs` | değişir | Windows kartı (anahtar, deneme, durum), saat kaydında görev, rozet bildirimi |
| `Kasa.App.Core/MenuModeli.cs` | değişir | `MenuOgesi.Rozet`/`RozetVar`/`RozetMetni`/`ErisimAdi`, `MenuModeli.RozetAyarla` |
| `Kasa.App.Core/KrediTakipViewModel.cs` | değişir | `IdIleSec(int)` |

**Kasa.App (Windows'a özgü ince katman; derleme ile doğrulanır)**

| Dosya | Durum | Sorumluluk |
|---|---|---|
| `Kasa.App/Platforms/Windows/WindowsBildirimGosterici.cs` | yeni | AppNotificationManager: kayıt, gösterme, tıklama, Windows ayarı |
| `Kasa.App/Platforms/Windows/App.xaml.cs` | değişir | `--bildirim-kontrol` yolu, pencere kilidi, kayıt/çıkış |
| `Kasa.App/MauiProgram.cs` | değişir | DI kayıtları |
| `Kasa.App/AppShell.xaml.cs` | değişir | 5 dk zamanlayıcı, oturum açılışında bakma, tıklama, rozet |
| `Kasa.App/AppShell.xaml` | değişir | menü öğesi şablonunda rozet |
| `Kasa.App/Views/BildirimPage.cs` | değişir | "Bu bilgisayarda Windows bildirimleri" kartı, cihazlar notu |
| `Kasa.App/Views/KrediTakipPage.cs` | değişir | `KrediId` sorgu parametresi |

**Testler (Kasa.App.Core.Tests)**

| Dosya | Durum | Kapsam |
|---|---|---|
| `BildirimHedefiTests.cs`, `BildirimGorevZamaniTests.cs` | yeni | Görev 1 |
| `GosterilenBildirimDeposuTests.cs`, `BildirimAyariTests.cs` | yeni | Görev 2 |
| `BildirimSahteleri.cs` | yeni | ortak sahteler (`SahteBildirimApi`, `SahteGosterici`, `SahteDepo`, `SahteAyar`, `SahteGorev`, `SabitBildirimSaati`, `BildirimOrtami`) |
| `BildirimYoklayiciTests.cs` | yeni | Görev 3 |
| `BildirimGoreviTests.cs` | yeni | Görev 4 |
| `BildirimNobetcisiTests.cs`, `BildirimKontroluTests.cs`, `PencereKilidiTests.cs` | yeni | Görev 5 |
| `BildirimTests.cs` (değişir), `BildirimEkraniWindowsTests.cs` (yeni) | | Görev 7 |
| `MenuRozetiTests.cs`, `Donusturuculer/GorunumEsdegerligiTests.KabukRozeti.cs` (yeni), `Donusturuculer/MauiKayitTutarliligiTests.Kontrast.cs` (değişir) | | Görev 8 |
| `KrediIdIleSecimTests.cs` | yeni | Görev 9 |

---

## Görev 1: Bildirim hedefi ve görev saati (saf)

**Dosyalar:**
- Oluştur: `Kasa.App.Core/BildirimHedefi.cs`
- Oluştur: `Kasa.App.Core/BildirimGorevZamani.cs`
- Test: `Kasa.App.Core.Tests/BildirimHedefiTests.cs`, `Kasa.App.Core.Tests/BildirimGorevZamaniTests.cs`

- [ ] **Adım 1: Başarısız testleri yaz**

`Kasa.App.Core.Tests/BildirimHedefiTests.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Sunucu bildiriminin Hedef alanı (web rotası) masaüstü Shell rotasına çevrilir (tasarım 2026-09-30 masaüstü bildirimleri
/// §1 Tıklama): kart → Kartlar'da o kart, kredi → Krediler'de o kredi; tanınmayan ya da boş hedef Bildirimler'i açar.</summary>
public class BildirimHedefiTests
{
    [Theory]
    [InlineData("/#cards/3", "//kartlar?KartId=3")]
    [InlineData("/#loans/7", "//krediler?KrediId=7")]
    [InlineData("/#cards", "//kartlar")]
    [InlineData("/#loans", "//krediler")]
    [InlineData("/#home", "//bildirimler")]
    [InlineData("/#notifications", "//bildirimler")]
    [InlineData("/#cards/0", "//bildirimler")]
    [InlineData("/#cards/abc", "//bildirimler")]
    [InlineData("https://kasa.emarglobal.com/#cards/3", "//bildirimler")]
    [InlineData("", "//bildirimler")]
    [InlineData(null, "//bildirimler")]
    public void Hedef_uygulama_rotasina_cevrilir(string? hedef, string rota) => Assert.Equal(rota, BildirimHedefi.Rota(hedef));
}
```

`Kasa.App.Core.Tests/BildirimGorevZamaniTests.cs`:

```csharp
using System.Globalization;

namespace Kasa.App.Core.Tests;

/// <summary>Zamanlanmış görevin günlük saati sunucu bildirim saatinden 5 dakika sonradır; gece yarısını geçen saat aynı günlük
/// tetikleyicinin saatidir (23:58 → 00:03, tarih yok).</summary>
public class BildirimGorevZamaniTests
{
    [Theory]
    [InlineData(9, 0, "09:05")]
    [InlineData(23, 58, "00:03")]
    [InlineData(23, 55, "00:00")]
    [InlineData(0, 0, "00:05")]
    [InlineData(14, 57, "15:02")]
    public void Gorev_saati_bildirim_saatinden_bes_dakika_sonradir(int saat, int dakika, string beklenen)
        => Assert.Equal(beklenen, BildirimGorevZamani.Hesapla(saat, dakika).ToString("HH:mm", CultureInfo.InvariantCulture));
}
```

- [ ] **Adım 2: Testlerin başarısız olduğunu gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~BildirimHedefiTests|FullyQualifiedName~BildirimGorevZamaniTests"`
Beklenen: derleme hatası `CS0103: The name 'BildirimHedefi' does not exist in the current context` (ve `BildirimGorevZamani` için aynısı).

- [ ] **Adım 3: En küçük uygulamayı yaz**

`Kasa.App.Core/BildirimHedefi.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Kasa.App.Core;

/// <summary>Sunucu bildiriminin <c>Hedef</c> alanını (web rotası: "/#cards/3", "/#loans/7") masaüstü Shell rotasına çevirir
/// (tasarım 2026-09-30 masaüstü bildirimleri §1 Tıklama). Kart → Kartlar sayfasında o kart (KartId), kredi → Krediler sayfasında
/// o kredi (KrediId); kimliksiz kart/kredi hedefi yalnız sayfayı açar; tanınmayan ya da boş hedef Bildirimler sayfasını açar.
/// Sunucunun ürettiği diğer hedefler (kasa alt sınırı ve kaynak hatası: "/#home") tanınmayan sayılır.</summary>
public static partial class BildirimHedefi
{
    /// <summary>Tanınmayan hedefte açılan rota.</summary>
    public const string Varsayilan = "//bildirimler";

    public static string Rota(string? hedef)
    {
        var eslesme = HedefDeseni().Match(hedef ?? "");
        if (!eslesme.Success)
            return Varsayilan;
        var kimlik = eslesme.Groups["id"];
        return (eslesme.Groups["tur"].Value, kimlik.Success) switch
        {
            ("cards", true) => "//kartlar?KartId=" + kimlik.Value,
            ("cards", false) => "//kartlar",
            ("loans", true) => "//krediler?KrediId=" + kimlik.Value,
            _ => "//krediler",
        };
    }

    [GeneratedRegex(@"^/#(?<tur>cards|loans)(?:/(?<id>[1-9][0-9]{0,8}))?\z")]
    private static partial Regex HedefDeseni();
}
```

`Kasa.App.Core/BildirimGorevZamani.cs`:

```csharp
namespace Kasa.App.Core;

/// <summary>Zamanlanmış görevin (BildirimGorevi) günlük saati: sunucunun bildirim saatinden (BildirimAyarDto.Saat/Dakika) 5 dakika
/// sonra. Sunucu bildirimleri o saatte üretir; görev onları hazır bulur. Gece yarısını geçen saat aynı günlük tetikleyicinin saatidir
/// (23:58 → 00:03); tarih yoktur.</summary>
public static class BildirimGorevZamani
{
    public static readonly TimeSpan Gecikme = TimeSpan.FromMinutes(5);

    public static TimeOnly Hesapla(int saat, int dakika) => new TimeOnly(saat, dakika).Add(Gecikme);
}
```

- [ ] **Adım 4: Testlerin geçtiğini gör**

Çalıştır: Adım 2'deki komut.
Beklenen: `Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16`

- [ ] **Adım 5: Commit**

```bash
git add Kasa.App.Core/BildirimHedefi.cs Kasa.App.Core/BildirimGorevZamani.cs Kasa.App.Core.Tests/BildirimHedefiTests.cs Kasa.App.Core.Tests/BildirimGorevZamaniTests.cs
git commit -F - <<'EOF'
feat(app-core): bildirim hedefi rotası ve görev saati

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 2: Yerel dosyalar — gösterilenler deposu ve bilgisayar ayarı

**Dosyalar:**
- Oluştur: `Kasa.App.Core/YerelKlasor.cs`
- Oluştur: `Kasa.App.Core/GosterilenBildirimDeposu.cs`
- Oluştur: `Kasa.App.Core/BildirimAyari.cs`
- Test: `Kasa.App.Core.Tests/GosterilenBildirimDeposuTests.cs`, `Kasa.App.Core.Tests/BildirimAyariTests.cs`

- [ ] **Adım 1: Başarısız testleri yaz**

`Kasa.App.Core.Tests/GosterilenBildirimDeposuTests.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Bu bilgisayarda gösterilen bildirim kimlikleri (%LOCALAPPDATA%\EmarKasa\gosterilen-bildirimler.json): her kimlik bir kez
/// "yeni" sayılır, dosya en çok 500 kimlik tutar (eskiler atılır), bozuk dosya boş sayılır, başka süreç dosyayı kilitliyse beklenir;
/// kilit bırakılmazsa IOException.</summary>
public sealed class GosterilenBildirimDeposuTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "kasa-bildirim-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_klasor, true);
        }
        catch (DirectoryNotFoundException) { }
    }

    private string Yol => Path.Combine(_klasor, DosyaGosterilenBildirimDeposu.DosyaAdi);

    [Fact]
    public void Yeni_kimlikler_bir_kez_ayrilir_ve_dosyaya_yazilir()
    {
        var depo = new DosyaGosterilenBildirimDeposu(_klasor);
        Assert.Equal([3, 1], depo.YenileriAyir([3, 1, 3]));
        Assert.Equal([2], depo.YenileriAyir([1, 2, 3]));
        Assert.Empty(new DosyaGosterilenBildirimDeposu(_klasor).YenileriAyir([1, 2, 3]));
        Assert.Equal("[3,1,2]", File.ReadAllText(Yol));
    }

    [Fact]
    public void Sinirda_en_eski_kimlikler_atilir()
    {
        var depo = new DosyaGosterilenBildirimDeposu(_klasor);
        Assert.Equal(500, depo.YenileriAyir([.. Enumerable.Range(1, 500)]).Count);
        Assert.Equal([501, 502], depo.YenileriAyir([501, 502]));
        var kayitli = depo.Kayitlilar();
        Assert.Equal(500, kayitli.Count);
        Assert.Equal(3, kayitli[0]);
        Assert.Equal(502, kayitli[^1]);
        // Atılan en eski kimlik yeniden yeni sayılır; liste bugünle sınırlı olduğundan pratikte görülmez.
        Assert.Equal([1], depo.YenileriAyir([1]));
    }

    [Theory]
    [InlineData("bozuk{")]
    [InlineData("")]
    [InlineData(@"{""a"":1}")]
    public void Bozuk_dosya_bos_sayilir_ve_uzerine_yazilir(string icerik)
    {
        Directory.CreateDirectory(_klasor);
        File.WriteAllText(Yol, icerik);
        var depo = new DosyaGosterilenBildirimDeposu(_klasor);
        Assert.Equal([7], depo.YenileriAyir([7]));
        Assert.Equal("[7]", File.ReadAllText(Yol));
    }

    [Fact]
    public async Task Baska_surecin_kilidi_birakilinca_beklenir_birakilmazsa_IOException()
    {
        Directory.CreateDirectory(_klasor);
        var kilit = new FileStream(Yol, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => new DosyaGosterilenBildirimDeposu(_klasor, deneme: 3, bekleme: TimeSpan.FromMilliseconds(10)).YenileriAyir([1]));
        var birak = Task.Run(async () =>
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
            await kilit.DisposeAsync();
        }, TestContext.Current.CancellationToken);
        Assert.Equal([1], new DosyaGosterilenBildirimDeposu(_klasor, deneme: 100, bekleme: TimeSpan.FromMilliseconds(50)).YenileriAyir([1]));
        await birak;
    }
}
```

`Kasa.App.Core.Tests/BildirimAyariTests.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>"Bu bilgisayarda Windows bildirimleri" ayarı (%LOCALAPPDATA%\EmarKasa\bildirim-ayari.json): dosya yoksa ya da bozuksa
/// açıktır (editör için varsayılan), yazılan değeri başka nesne (başka süreç) okur, yazılamazsa değer bellekte kalır.</summary>
public sealed class BildirimAyariTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "kasa-ayar-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_klasor, true);
        }
        catch (DirectoryNotFoundException) { }
    }

    private string Yol => Path.Combine(_klasor, DosyaBildirimAyari.DosyaAdi);

    [Fact]
    public void Dosya_yoksa_acik_sayilir()
    {
        Assert.True(new DosyaBildirimAyari(_klasor).Acik);
        Assert.False(File.Exists(Yol));
    }

    [Fact]
    public void Yazilan_deger_dosyadan_baska_nesneyle_okunur()
    {
        var ayar = new DosyaBildirimAyari(_klasor);
        ayar.Acik = false;
        Assert.False(new DosyaBildirimAyari(_klasor).Acik);
        Assert.Equal(@"{""Acik"":false}", File.ReadAllText(Yol));
        ayar.Acik = true;
        Assert.True(new DosyaBildirimAyari(_klasor).Acik);
        Assert.False(File.Exists(Yol + ".yeni"));
    }

    [Theory]
    [InlineData("bozuk")]
    [InlineData("")]
    [InlineData("null")]
    public void Bozuk_dosyada_acik_sayilir(string icerik)
    {
        Directory.CreateDirectory(_klasor);
        File.WriteAllText(Yol, icerik);
        Assert.True(new DosyaBildirimAyari(_klasor).Acik);
    }

    [Fact]
    public void Yazilamayan_klasorde_deger_bellekte_kalir_hata_disari_cikmaz()
    {
        File.WriteAllText(_klasor + ".dosya", "");
        try
        {
            var ayar = new DosyaBildirimAyari(Path.Combine(_klasor + ".dosya", "alt"));
            ayar.Acik = false;
            Assert.False(ayar.Acik);
        }
        finally
        {
            File.Delete(_klasor + ".dosya");
        }
    }
}
```

- [ ] **Adım 2: Testlerin başarısız olduğunu gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~GosterilenBildirimDeposuTests|FullyQualifiedName~BildirimAyariTests"`
Beklenen: derleme hatası `CS0246: The type or namespace name 'DosyaGosterilenBildirimDeposu' could not be found` (ve `DosyaBildirimAyari` için aynısı).

- [ ] **Adım 3: En küçük uygulamayı yaz**

`Kasa.App.Core/YerelKlasor.cs`:

```csharp
namespace Kasa.App.Core;

/// <summary>Masaüstü uygulamasının bu bilgisayara özel dosyalarının klasörü: %LOCALAPPDATA%\EmarKasa (07-15 hatırlatıcısının da
/// klasörü; bkz. EskiHatirlatmaGorevi.Varsayilan). Uygulama ve pencere açmadan çalışan bildirim görevi aynı dosyaları kullanır.</summary>
public static class YerelKlasor
{
    public static string Yol => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmarKasa");
}
```

`Kasa.App.Core/GosterilenBildirimDeposu.cs`:

```csharp
using System.Text.Json;

namespace Kasa.App.Core;

/// <summary>Bu bilgisayarda Windows bildirimi olarak gösterilmiş sunucu bildirimlerinin kimlikleri (tekrar göstermeme; tasarım
/// 2026-09-30 masaüstü bildirimleri §1).</summary>
public interface IGosterilenBildirimDeposu
{
    /// <summary><paramref name="adaylar"/> içinden bu bilgisayarda daha önce gösterilmemiş olanları döndürür ve aynı kilit altında
    /// gösterilmiş olarak kaydeder: uygulama ile pencere açmadan çalışan görev aynı anda baksa da her bildirimi yalnız biri alır.
    /// Dosya kilitli kalır ya da açılamazsa <see cref="IOException"/> ya da <see cref="UnauthorizedAccessException"/> fırlatır.</summary>
    IReadOnlyList<int> YenileriAyir(IReadOnlyCollection<int> adaylar);
}

/// <summary>
/// %LOCALAPPDATA%\EmarKasa\gosterilen-bildirimler.json: kimliklerin JSON dizisi, eskiden yeniye; en çok <see cref="Sinir"/> kimlik
/// (en eskiler atılır). Dosya FileShare.None ile açılır; başka süreç tutuyorsa kısa aralıklarla yeniden denenir. Bozuk ya da boş
/// dosya boş liste sayılır ve üzerine yazılır: sonuç en çok bir kez fazladan bildirimdir, kayıp olmaz.
/// </summary>
public sealed class DosyaGosterilenBildirimDeposu : IGosterilenBildirimDeposu
{
    public const string DosyaAdi = "gosterilen-bildirimler.json";
    public const int VarsayilanSinir = 500;

    private readonly string _klasor;
    private readonly string _yol;
    private readonly int _deneme;
    private readonly TimeSpan _bekleme;

    /// <param name="deneme">Kilitli dosyada en çok deneme sayısı (varsayılan 10).</param>
    /// <param name="bekleme">Denemeler arası bekleme (varsayılan 100 ms).</param>
    public DosyaGosterilenBildirimDeposu(string klasor, int sinir = VarsayilanSinir, int deneme = 10, TimeSpan? bekleme = null)
    {
        _klasor = klasor;
        _yol = Path.Combine(klasor, DosyaAdi);
        Sinir = sinir;
        _deneme = deneme;
        _bekleme = bekleme ?? TimeSpan.FromMilliseconds(100);
    }

    public static DosyaGosterilenBildirimDeposu Varsayilan() => new(YerelKlasor.Yol);

    public int Sinir { get; }

    public IReadOnlyList<int> YenileriAyir(IReadOnlyCollection<int> adaylar)
    {
        Directory.CreateDirectory(_klasor);
        using var dosya = Ac();
        var kayitli = Oku(dosya);
        var bilinen = kayitli.ToHashSet();
        var yeniler = adaylar.Where(bilinen.Add).ToList();
        if (yeniler.Count == 0)
            return [];
        kayitli.AddRange(yeniler);
        var yazilacak = kayitli.Count > Sinir ? kayitli.GetRange(kayitli.Count - Sinir, Sinir) : kayitli;
        dosya.SetLength(0);
        dosya.Position = 0;
        JsonSerializer.Serialize(dosya, yazilacak);
        return yeniler;
    }

    /// <summary>Kayıtlı kimlikler (eskiden yeniye; testler ve tanı için). Dosya yoksa ya da bozuksa boş.</summary>
    public IReadOnlyList<int> Kayitlilar()
    {
        if (!File.Exists(_yol))
            return [];
        using var dosya = Ac();
        return Oku(dosya);
    }

    private FileStream Ac()
    {
        for (var sira = 1; ; sira++)
        {
            try
            {
                return new FileStream(_yol, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (sira < _deneme)
            {
                Thread.Sleep(_bekleme);
            }
        }
    }

    private static List<int> Oku(FileStream dosya)
    {
        if (dosya.Length == 0)
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<int>>(dosya) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
```

Not: `adaylar.Where(bilinen.Add)` hem dosyadakileri hem aynı çağrıdaki tekrarları eler (`[3, 1, 3]` → `[3, 1]`).

`Kasa.App.Core/BildirimAyari.cs`:

```csharp
using System.Text.Json;

namespace Kasa.App.Core;

/// <summary>"Bu bilgisayarda Windows bildirimleri" anahtarı (yalnız bu bilgisayar; tasarım 2026-09-30 masaüstü bildirimleri §2).
/// Uygulama ve pencere açmadan çalışan görev aynı değeri okur.</summary>
public interface IBildirimAyari
{
    bool Acik { get; set; }
}

/// <summary>
/// %LOCALAPPDATA%\EmarKasa\bildirim-ayari.json (<c>{"Acik":true}</c>). Dosya yoksa, okunamıyorsa ya da bozuksa açık sayılır (editör
/// için varsayılan açık). Yazma önce geçici dosyaya yapılır, sonra yerine taşınır: okuyan süreç yarım dosya görmez. Yazılamazsa değer
/// bu süreçte bellekte kalır, hata dışarı çıkmaz. MAUI Preferences kullanılmaz: paketsiz uygulamada Preferences dosyayı süreç başında
/// bir kez okuyup bellekte tutar ve her yazışta kilitsiz baştan yazar (dotnet/maui Preferences.windows.cs,
/// UnpackagedPreferencesImplementation); uygulama ile görev aynı ayarı iki süreçte kullanır.
/// </summary>
public sealed class DosyaBildirimAyari : IBildirimAyari
{
    public const string DosyaAdi = "bildirim-ayari.json";

    private readonly string _klasor;
    private readonly string _yol;
    private bool? _bellekte;

    public DosyaBildirimAyari(string klasor)
    {
        _klasor = klasor;
        _yol = Path.Combine(klasor, DosyaAdi);
    }

    public static DosyaBildirimAyari Varsayilan() => new(YerelKlasor.Yol);

    public bool Acik
    {
        get => _bellekte ?? Oku();
        set => Yaz(value);
    }

    private bool Oku()
    {
        try
        {
            if (!File.Exists(_yol))
                return true;
            return JsonSerializer.Deserialize<Kayit>(File.ReadAllText(_yol))?.Acik ?? true;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private void Yaz(bool acik)
    {
        _bellekte = acik;
        try
        {
            Directory.CreateDirectory(_klasor);
            var gecici = _yol + ".yeni";
            File.WriteAllText(gecici, JsonSerializer.Serialize(new Kayit(acik)));
            File.Move(gecici, _yol, overwrite: true);
            _bellekte = null;
        }
        catch (Exception) { }
    }

    private sealed record Kayit(bool Acik);
}
```

- [ ] **Adım 4: Testlerin geçtiğini gör**

Çalıştır: Adım 2'deki komut.
Beklenen: `Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12`

- [ ] **Adım 5: Commit**

```bash
git add Kasa.App.Core/YerelKlasor.cs Kasa.App.Core/GosterilenBildirimDeposu.cs Kasa.App.Core/BildirimAyari.cs Kasa.App.Core.Tests/GosterilenBildirimDeposuTests.cs Kasa.App.Core.Tests/BildirimAyariTests.cs
git commit -F - <<'EOF'
feat(app-core): gösterilen bildirimler deposu ve bilgisayar ayarı

Tekrar göstermeme dosyası (500 kimlik, bozuk dosya, kilit) ve Windows bildirimleri
anahtarı %LOCALAPPDATA%\EmarKasa altında; uygulama ve görev aynı dosyaları kullanır.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 3: Bildirim yoklayıcısı, gösterici arayüzü ve tıklama

**Dosyalar:**
- Oluştur: `Kasa.App.Core/BildirimGosterimi.cs` (`IBildirimGosterici`, `BildirimTiklamasi`, `BildirimTiklamalari`)
- Oluştur: `Kasa.App.Core/YoklamaSonucu.cs`
- Oluştur: `Kasa.App.Core/BildirimYoklayici.cs`
- Oluştur: `Kasa.App.Core.Tests/BildirimSahteleri.cs`
- Test: `Kasa.App.Core.Tests/BildirimYoklayiciTests.cs`

- [ ] **Adım 1: Ortak sahteleri yaz**

`Kasa.App.Core.Tests/BildirimSahteleri.cs` (Görev 5 bu dosyaya `SahteGorev` ve `Nobetci` ekler):

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Masaüstü bildirim testlerinin sunucusu: liste, ayar, okundu işareti; hata ve bekleyen yanıt kancaları.</summary>
internal sealed class SahteBildirimApi : IBildirimApi
{
    public List<BildirimDto> Liste { get; set; } = [];
    public Exception? ListeHatasi { get; set; }
    /// <summary>Ayarlanırsa liste yanıtı bu görevi bekler (süre sınırı testleri).</summary>
    public Task<IReadOnlyList<BildirimDto>>? Bekleyen { get; set; }
    public int ListeCagri { get; private set; }
    public BildirimAyarDto Ayar { get; set; } = new(true, 9, 0, "Europe/Istanbul", 3);
    public Exception? AyarHatasi { get; set; }
    public Exception? OkunduHatasi { get; set; }
    public List<int> Okunanlar { get; } = [];

    public static BildirimDto Bildirim(int id, DateOnly tarih, bool okundu = false, string hedef = "/#cards/1")
        => new(id, $"Başlık {id}", $"Mesaj {id}", tarih, okundu, hedef, "SonOdeme", 1);

    public Task<IReadOnlyList<BildirimDto>> BildirimlerAsync()
    {
        ListeCagri++;
        if (Bekleyen is not null)
            return Bekleyen;
        return ListeHatasi is not null
            ? Task.FromException<IReadOnlyList<BildirimDto>>(ListeHatasi)
            : Task.FromResult<IReadOnlyList<BildirimDto>>([.. Liste]);
    }

    public Task<BildirimAyarDto> BildirimAyarlariAsync()
        => AyarHatasi is not null ? Task.FromException<BildirimAyarDto>(AyarHatasi) : Task.FromResult(Ayar);

    public Task<BildirimAyarDto> BildirimAyarKaydetAsync(BildirimAyarYaz g)
    {
        Ayar = new(g.Etkin, g.Saat, g.Dakika, "Europe/Istanbul", g.Surum + 1);
        return Task.FromResult(Ayar);
    }

    public Task BildirimOkunduAsync(int id)
    {
        if (OkunduHatasi is not null)
            return Task.FromException(OkunduHatasi);
        Okunanlar.Add(id);
        return Task.CompletedTask;
    }

    public Task<PushAnahtarDto> BildirimAnahtariAsync() => Task.FromResult(new PushAnahtarDto(true, "public"));
    public Task<IReadOnlyList<BildirimCihaziDto>> BildirimCihazlariAsync() => Task.FromResult<IReadOnlyList<BildirimCihaziDto>>([]);
    public Task BildirimCihaziKaldirAsync(int id) => Task.CompletedTask;
}

/// <summary>Windows göstericisinin sahtesi: gösterilenleri ve deneme sayısını kaydeder.</summary>
internal sealed class SahteGosterici : IBildirimGosterici
{
    public List<BildirimDto> Gosterilenler { get; } = [];
    public int DenemeSayisi { get; private set; }
    /// <summary>Bu kimlikteki bildirim gösterilirken hata fırlatılır.</summary>
    public int? HataliKimlik { get; set; }
    public bool DenemeBasarili { get; set; } = true;
    public bool WindowsAyarindaKapali { get; set; }

    public void Goster(BildirimDto bildirim)
    {
        if (bildirim.Id == HataliKimlik)
            throw new InvalidOperationException("gösterilemedi");
        Gosterilenler.Add(bildirim);
    }

    public bool DenemeGoster()
    {
        DenemeSayisi++;
        return DenemeBasarili;
    }
}

/// <summary>Bellek içi gösterilenler deposu (dosya davranışı GosterilenBildirimDeposuTests'te sınanır).</summary>
internal sealed class SahteDepo : IGosterilenBildirimDeposu
{
    public HashSet<int> Kayitli { get; } = [];
    public Exception? Hata { get; set; }

    public IReadOnlyList<int> YenileriAyir(IReadOnlyCollection<int> adaylar)
    {
        if (Hata is not null)
            throw Hata;
        return adaylar.Where(Kayitli.Add).ToList();
    }
}

internal sealed class SahteAyar : IBildirimAyari
{
    public bool Acik { get; set; } = true;
}

/// <summary>Sabit an; yerel saat dilimi UTC (yerel saat = UTC saati).</summary>
internal sealed class SabitBildirimSaati(DateTimeOffset an) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => an;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Bir testin bildirim ortamı: bugün 30.09.2026, saat 14:05; oturum editör ve açık.</summary>
internal sealed class BildirimOrtami
{
    public static readonly DateOnly Bugun = new(2026, 9, 30);

    public BildirimOrtami(IBildirimApi? api = null, AuthViewModel? auth = null)
    {
        Sunucu = api ?? Api;
        Auth = auth ?? new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor, GirisYapildi = true };
        Yoklayici = new BildirimYoklayici(Sunucu, Gosterici, Depo, Ayar, Saat);
    }

    /// <summary>Varsayılan sunucu sahtesi; kurucuya başka API verilirse kullanılmaz.</summary>
    public SahteBildirimApi Api { get; } = new();
    /// <summary>Yoklayıcının kullandığı API (verilen ya da <see cref="Api"/>).</summary>
    public IBildirimApi Sunucu { get; }
    public AuthViewModel Auth { get; }
    public SahteGosterici Gosterici { get; } = new();
    public SahteDepo Depo { get; } = new();
    public SahteAyar Ayar { get; } = new();
    public TimeProvider Saat { get; } = new SabitBildirimSaati(new DateTimeOffset(2026, 9, 30, 14, 5, 0, TimeSpan.Zero));
    public BildirimYoklayici Yoklayici { get; }
}
```

Not: özellik ilkleyicileri (`Api`, `Gosterici`, ...) kurucu gövdesinden önce çalışır; `Sunucu = api ?? Api` doğru nesneyi alır.

- [ ] **Adım 2: Başarısız testleri yaz**

`Kasa.App.Core.Tests/BildirimYoklayiciTests.cs`:

```csharp
using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Masaüstü bildirim yoklayıcısı (tasarım 2026-09-30 masaüstü bildirimleri §1, Test): yeni bildirim bu bilgisayarda yalnız bir kez
/// gösterilir; okunmuş ve bugünden eski bildirim gösterilmez (iptal bilgisi BildirimDto'da yok; sunucu iptal edileni okunmamışsa ve
/// tarayıcıya gitmemişse listelemez); ayar kapalıyken ya da editör oturumu yokken bakılmaz; sunucu hatasında sessizce durulur ve
/// durum satırına yazılır; okunmamış sayısı listedeki bütün okunmamışlardır; tıklama okundu işaretler ve rotayı döndürür.
/// </summary>
public class BildirimYoklayiciTests
{
    private static BildirimDto B(int id, DateOnly? tarih = null, bool okundu = false, string hedef = "/#cards/1")
        => SahteBildirimApi.Bildirim(id, tarih ?? BildirimOrtami.Bugun, okundu, hedef);

    [Fact]
    public async Task Yeni_bildirim_yalniz_bir_kez_gosterilir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(2), B(1)];
        var ilk = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([1, 2], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal(new YoklamaSonucu(o.Saat.GetLocalNow(), YoklamaDurumu.Basarili, 2), ilk);
        Assert.Equal("Son kontrol 14:05 · 2 yeni bildirim", ilk!.Metin);
        o.Api.Liste = [B(1), B(2), B(3)];
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([1, 2, 3], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal(1, o.Yoklayici.SonSonuc!.YeniSayisi);
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(3, o.Gosterici.Gosterilenler.Count);
        Assert.Equal("Son kontrol 14:05 · yeni bildirim yok", o.Yoklayici.SonSonuc!.Metin);
    }

    [Fact]
    public async Task Okunmus_ve_bugunden_eski_bildirim_gosterilmez_okunmamis_sayisi_listenin_tamamidir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1, okundu: true), B(2, BildirimOrtami.Bugun.AddDays(-1)), B(3)];
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([3], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal([3], o.Depo.Kayitli.Order());
        Assert.Equal(2, o.Yoklayici.Okunmamis);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Editor_oturumu_yokken_ya_da_ayar_kapaliyken_bakilmaz(bool editorOturumu, bool ayarAcik)
    {
        var o = new BildirimOrtami();
        o.Ayar.Acik = ayarAcik;
        o.Api.Liste = [B(1)];
        Assert.Null(await o.Yoklayici.YoklaAsync(editorOturumu));
        Assert.Equal(0, o.Api.ListeCagri);
        Assert.Empty(o.Gosterici.Gosterilenler);
        Assert.Null(o.Yoklayici.SonSonuc);
    }

    [Theory]
    [InlineData(0, YoklamaDurumu.SunucuyaUlasilamadi, "Son kontrol 14:05 · sunucuya ulaşılamadı")]
    [InlineData(1, YoklamaDurumu.SunucuHatasi, "Son kontrol 14:05 · sunucu yanıt veremedi")]
    [InlineData(2, YoklamaDurumu.OturumGecersiz, "Son kontrol 14:05 · oturum geçersiz, yeniden giriş yapın")]
    public async Task Sunucu_hatasinda_sessizce_durulur_ve_durum_satirina_yazilir(int hata, YoklamaDurumu durum, string metin)
    {
        var o = new BildirimOrtami();
        o.Api.ListeHatasi = hata switch
        {
            0 => new HttpRequestException("bağlantı yok"),
            1 => new KasaApiException(HttpStatusCode.InternalServerError),
            _ => new KasaApiException(HttpStatusCode.Unauthorized),
        };
        o.Yoklayici.OkunmamisBildir(4);
        var sonuc = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(durum, sonuc!.Durum);
        Assert.Equal(metin, o.Yoklayici.SonSonuc!.Metin);
        Assert.Empty(o.Gosterici.Gosterilenler);
        Assert.Equal(4, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Yerel_kayit_acilamazsa_gosterilmez_ve_durum_yazilir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1)];
        o.Depo.Hata = new IOException("kilitli");
        var sonuc = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(YoklamaDurumu.YerelKayitHatasi, sonuc!.Durum);
        Assert.Equal("Son kontrol 14:05 · bu bilgisayardaki kayıt dosyası açılamadı", sonuc.Metin);
        Assert.Empty(o.Gosterici.Gosterilenler);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Bir_bildirimin_gosterilememesi_otekileri_durdurmaz()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1), B(2)];
        o.Gosterici.HataliKimlik = 1;
        var sonuc = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([2], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal(YoklamaDurumu.Basarili, sonuc!.Durum);
    }

    [Fact]
    public async Task Tiklama_okundu_isaretler_ve_rotayi_dondurur()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(5, hedef: "/#loans/7"), B(6)];
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(2, o.Yoklayici.Okunmamis);
        Assert.Equal("//krediler?KrediId=7", await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(5, "/#loans/7"), true));
        Assert.Equal([5], o.Api.Okunanlar);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
        // Deneme bildiriminin kimliği yok: okundu işaretlenmez, Bildirimler açılır.
        Assert.Equal("//bildirimler", await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(null, BildirimTiklamasi.DenemeHedefi), true));
        // Editör oturumu yok: hiçbir şey yapılmaz.
        Assert.Null(await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(6, "/#cards/1"), false));
        Assert.Equal([5], o.Api.Okunanlar);
    }

    [Fact]
    public async Task Okundu_isaretlenemezse_rota_yine_doner()
    {
        var o = new BildirimOrtami();
        o.Yoklayici.OkunmamisBildir(3);
        o.Api.OkunduHatasi = new HttpRequestException("bağlantı yok");
        Assert.Equal("//kartlar?KartId=4", await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(9, "/#cards/4"), true));
        Assert.Equal(3, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Sifirlama_rozeti_ve_durumu_temizler()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1)];
        await o.Yoklayici.YoklaAsync(true);
        o.Yoklayici.Sifirla();
        Assert.Equal(0, o.Yoklayici.Okunmamis);
        Assert.Null(o.Yoklayici.SonSonuc);
        o.Yoklayici.OkunmamisBildir(-3);
        Assert.Equal(0, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public void Tiklama_argumanlari_kimlik_ve_hedefe_cozulur()
    {
        Assert.Equal(new BildirimTiklamasi(12, "/#cards/3"),
            BildirimTiklamasi.Coz(new Dictionary<string, string> { ["bildirim"] = "12", ["hedef"] = "/#cards/3" }));
        Assert.Equal(new BildirimTiklamasi(null, "/#notifications"),
            BildirimTiklamasi.Coz(new Dictionary<string, string> { ["hedef"] = "/#notifications" }));
        Assert.Equal(new BildirimTiklamasi(null, null),
            BildirimTiklamasi.Coz(new Dictionary<string, string> { ["bildirim"] = "-1", ["baska"] = "x" }));
    }

    [Fact]
    public void Bekleyen_tiklama_bir_kez_alinir_son_tiklama_gecerlidir()
    {
        var tiklamalar = new BildirimTiklamalari();
        var olay = 0;
        tiklamalar.Istendi += (_, _) => olay++;
        tiklamalar.Ekle(new BildirimTiklamasi(1, "/#cards/1"));
        tiklamalar.Ekle(new BildirimTiklamasi(2, "/#loans/2"));
        Assert.Equal(2, olay);
        Assert.Equal(new BildirimTiklamasi(2, "/#loans/2"), tiklamalar.Al());
        Assert.Null(tiklamalar.Al());
    }
}
```

- [ ] **Adım 3: Testlerin başarısız olduğunu gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~BildirimYoklayiciTests"`
Beklenen: derleme hatası `CS0246: The type or namespace name 'IBildirimGosterici' could not be found` (ve `BildirimYoklayici`,
`YoklamaSonucu`, `BildirimTiklamasi` için aynısı).

- [ ] **Adım 4: Gösterici arayüzünü ve tıklama türlerini yaz**

`Kasa.App.Core/BildirimGosterimi.cs`:

```csharp
using System.Globalization;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Windows bildirimi gösteren platform katmanı (Kasa.App WindowsBildirimGosterici: AppNotificationManager). Kasa.App.Core
/// Windows'a bağlı değildir; testler sahtesini kullanır.</summary>
public interface IBildirimGosterici
{
    /// <summary>Sunucu bildirimini gösterir: başlık <see cref="BildirimDto.Baslik"/>, metin <see cref="BildirimDto.Mesaj"/>; tıklama
    /// argümanları kimlik ve hedeftir (<see cref="BildirimTiklamasi"/>).</summary>
    void Goster(BildirimDto bildirim);

    /// <summary>Sunucuya gitmeden örnek bildirim gösterir; gösterilemezse false.</summary>
    bool DenemeGoster();

    /// <summary>Windows ayarlarında bu uygulamanın bildirimleri kapalı (ya da desteklenmiyor).</summary>
    bool WindowsAyarindaKapali { get; }
}

/// <summary>Bildirime tıklama: sunucu bildiriminin kimliği (deneme bildiriminde yok) ve hedefi (BildirimDto.Hedef).</summary>
public sealed record BildirimTiklamasi(int? BildirimId, string? Hedef)
{
    /// <summary>Windows bildirim argümanı: sunucu bildiriminin kimliği.</summary>
    public const string KimlikAnahtari = "bildirim";
    /// <summary>Windows bildirim argümanı: sunucu bildiriminin hedefi (web rotası).</summary>
    public const string HedefAnahtari = "hedef";
    /// <summary>Deneme bildiriminin hedefi: Bildirimler sayfası (tanınmayan hedef).</summary>
    public const string DenemeHedefi = "/#notifications";

    /// <summary>Bildirim argümanlarından (AppNotificationActivatedEventArgs.Arguments) okur; eksik ya da geçersiz kimlik null'dır.</summary>
    public static BildirimTiklamasi Coz(IEnumerable<KeyValuePair<string, string>> argumanlar)
    {
        int? kimlik = null;
        string? hedef = null;
        foreach (var (anahtar, deger) in argumanlar)
        {
            if (anahtar == KimlikAnahtari && int.TryParse(deger, NumberStyles.None, CultureInfo.InvariantCulture, out var sayi) && sayi > 0)
                kimlik = sayi;
            else if (anahtar == HedefAnahtari)
                hedef = deger;
        }
        return new BildirimTiklamasi(kimlik, hedef);
    }
}

/// <summary>Bekleyen bildirim tıklaması: tıklama çalışan uygulamada olayla ya da uygulama tıklamayla başlarken gelir; kabuk (AppShell)
/// oturum açıkken <see cref="Al"/> ile alır ve uygular. Tek yer vardır: son tıklama geçerlidir.</summary>
public sealed class BildirimTiklamalari
{
    private readonly Lock _kilit = new();
    private BildirimTiklamasi? _bekleyen;

    /// <summary>Yeni tıklama geldi (Ekle'yi çağıran iş parçacığında).</summary>
    public event EventHandler? Istendi;

    public void Ekle(BildirimTiklamasi tiklama)
    {
        lock (_kilit)
            _bekleyen = tiklama;
        Istendi?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Bekleyen tıklamayı alır ve temizler (yoksa null).</summary>
    public BildirimTiklamasi? Al()
    {
        lock (_kilit)
        {
            var tiklama = _bekleyen;
            _bekleyen = null;
            return tiklama;
        }
    }
}
```

`Kasa.App.Core/YoklamaSonucu.cs`:

```csharp
using System.Globalization;

namespace Kasa.App.Core;

public enum YoklamaDurumu { Basarili, SunucuyaUlasilamadi, SunucuHatasi, OturumGecersiz, YerelKayitHatasi }

/// <summary>Bir bakmanın sonucu; <see cref="Metin"/> Bildirimler ekranındaki durum satırıdır ("Son kontrol 14:05 · 2 yeni bildirim").
/// Zaman yerel saattir.</summary>
public sealed record YoklamaSonucu(DateTimeOffset Zaman, YoklamaDurumu Durum, int YeniSayisi)
{
    public string Metin => "Son kontrol " + Zaman.ToString("HH:mm", CultureInfo.InvariantCulture) + " · " + Durum switch
    {
        YoklamaDurumu.Basarili when YeniSayisi > 0 => $"{YeniSayisi} yeni bildirim",
        YoklamaDurumu.Basarili => "yeni bildirim yok",
        YoklamaDurumu.SunucuyaUlasilamadi => "sunucuya ulaşılamadı",
        YoklamaDurumu.SunucuHatasi => "sunucu yanıt veremedi",
        YoklamaDurumu.OturumGecersiz => "oturum geçersiz, yeniden giriş yapın",
        _ => "bu bilgisayardaki kayıt dosyası açılamadı",
    };
}
```

- [ ] **Adım 5: Yoklayıcıyı yaz**

`Kasa.App.Core/BildirimYoklayici.cs`:

```csharp
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Masaüstü Windows bildirimlerinin çekirdeği (tasarım 2026-09-30 masaüstü bildirimleri §1): sunucunun bildirim listesini
/// (IBildirimApi.BildirimlerAsync) çeker; bugünün okunmamış bildirimlerinden bu bilgisayarda henüz gösterilmemiş olanları depodan
/// ayırıp gösterir ve okunmamış sayısını (menü rozeti) tutar. Kendi hatırlatma hesabı yapmaz. Göstermek okundu yapmaz; yalnız tıklama
/// okundu işaretler. Liste bugünle sınırlanır: sunucu web push'ta da yalnız bugünün bildirimlerini gönderir, eski okunmamışlar ilk
/// kurulumda bir kerede gösterilmez. İptal bilgisi listede yoktur (BildirimDto'da alan yok): sunucu iptal edilmiş bildirimi yalnız
/// okunmuş ya da bir tarayıcıya gönderilmişse döndürür. Uygulama içinde (BildirimNobetcisi, 5 dakikada bir) ve pencere açmadan çalışan
/// görevde (BildirimKontrolu) aynı sınıf kullanılır. Hiçbir hata dışarı çıkmaz.
/// </summary>
public sealed partial class BildirimYoklayici(IBildirimApi api, IBildirimGosterici gosterici, IGosterilenBildirimDeposu depo,
    IBildirimAyari ayar, TimeProvider? saat = null) : ObservableObject
{
    private readonly TimeProvider _saat = saat ?? TimeProvider.System;

    /// <summary>Son listedeki okunmamış bildirim sayısı (menü rozeti); oturum kapanınca ya da ayar kapanınca 0.</summary>
    [ObservableProperty] private int _okunmamis;

    /// <summary>Son bakmanın sonucu (durum satırı); henüz bakılmadıysa null.</summary>
    [ObservableProperty] private YoklamaSonucu? _sonSonuc;

    /// <summary>Editör oturumunda ve ayar açıkken bir kez bakar; aksi halde hiçbir şey yapmaz ve null döner.</summary>
    public async Task<YoklamaSonucu?> YoklaAsync(bool editorOturumu)
    {
        if (!editorOturumu || !ayar.Acik)
            return null;
        IReadOnlyList<BildirimDto> liste;
        try
        {
            liste = await api.BildirimlerAsync();
        }
        catch (KasaApiException e) when (e.DurumKodu is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Sonuc(YoklamaDurumu.OturumGecersiz, 0);
        }
        catch (KasaApiException)
        {
            return Sonuc(YoklamaDurumu.SunucuHatasi, 0);
        }
        catch (Exception)
        {
            return Sonuc(YoklamaDurumu.SunucuyaUlasilamadi, 0);
        }
        Okunmamis = liste.Count(b => !b.Okundu);
        var bugun = DateOnly.FromDateTime(_saat.GetLocalNow().DateTime);
        var adaylar = liste.Where(b => !b.Okundu && b.Tarih == bugun).OrderBy(b => b.Id).ToList();
        IReadOnlyList<int> yeniler;
        try
        {
            yeniler = adaylar.Count == 0 ? [] : depo.YenileriAyir(adaylar.Select(b => b.Id).ToList());
        }
        catch (Exception)
        {
            return Sonuc(YoklamaDurumu.YerelKayitHatasi, 0);
        }
        foreach (var bildirim in adaylar.Where(b => yeniler.Contains(b.Id)))
        {
            try
            {
                gosterici.Goster(bildirim);
            }
            catch (Exception)
            {
                // Tek bildirimin gösterilememesi ötekileri durdurmaz; bildirim listede ve telefonda görünmeye devam eder.
            }
        }
        return Sonuc(YoklamaDurumu.Basarili, yeniler.Count);
    }

    /// <summary>Tıklanan bildirimi sunucuda okundu işaretler (hata yutulur; sonraki bakmada sayı düzelir) ve açılacak Shell rotasını
    /// döner. Editör oturumu yoksa hiçbir şey yapmaz ve null döner.</summary>
    public async Task<string?> TiklandiAsync(BildirimTiklamasi tiklama, bool editorOturumu)
    {
        if (!editorOturumu)
            return null;
        if (tiklama.BildirimId is { } kimlik)
        {
            try
            {
                await api.BildirimOkunduAsync(kimlik);
                Okunmamis = Math.Max(0, Okunmamis - 1);
            }
            catch (Exception)
            {
                // Okundu işareti bir sonraki tıklamada ya da Bildirimler ekranında verilebilir; sayfa yine açılır.
            }
        }
        return BildirimHedefi.Rota(tiklama.Hedef);
    }

    /// <summary>Bildirimler ekranı listeyi yükleyince ya da okundu işaretleyince okunmamış sayısını bildirir.</summary>
    public void OkunmamisBildir(int sayi) => Okunmamis = Math.Max(0, sayi);

    /// <summary>Oturum kapandı, rol editör değil ya da ayar kapandı: rozet gizlenir, durum satırı boşalır.</summary>
    public void Sifirla()
    {
        Okunmamis = 0;
        SonSonuc = null;
    }

    private YoklamaSonucu Sonuc(YoklamaDurumu durum, int yeni)
    {
        var sonuc = new YoklamaSonucu(_saat.GetLocalNow(), durum, yeni);
        SonSonuc = sonuc;
        return sonuc;
    }
}
```

- [ ] **Adım 6: Testlerin geçtiğini gör**

Çalıştır: Adım 3'teki komut.
Beklenen: `Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14`

- [ ] **Adım 7: Kaçış denetimi ve commit**

Çalıştır: `git diff --cached --stat; grep -n "·" Kasa.App.Core/YoklamaSonucu.cs Kasa.App.Core.Tests/BildirimYoklayiciTests.cs | head -3`
Beklenen: `·` karakteri dosyalarda çıplak olarak görünür (`·` yok).

```bash
git add Kasa.App.Core/BildirimGosterimi.cs Kasa.App.Core/YoklamaSonucu.cs Kasa.App.Core/BildirimYoklayici.cs Kasa.App.Core.Tests/BildirimSahteleri.cs Kasa.App.Core.Tests/BildirimYoklayiciTests.cs
git commit -F - <<'EOF'
feat(app-core): bildirim yoklayıcısı, gösterici arayüzü ve tıklama kuyruğu

Sunucunun bugünkü okunmamış bildirimlerinden bu bilgisayarda gösterilmemiş olanlar
bir kez gösterilir; okunmamış sayısı rozet için tutulur, tıklama okundu işaretler.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 4: Zamanlanmış görev (schtasks, XML)

**Dosyalar:**
- Oluştur: `Kasa.App.Core/BildirimGorevi.cs` (`IBildirimGorevi`, `BildirimGorevi`)
- Test: `Kasa.App.Core.Tests/BildirimGoreviTests.cs`

Desen `EskiHatirlatmaGorevi` ile aynıdır: komut kurgusu ve işaret mantığı Kasa.App.Core'da, gerçek süreç çalıştırma
`EskiHatirlatmaGorevi.CalistirAsync` ile (pencere yok, 5 sn sınırı, hatalar yutulur); testler çalıştırıcıyı sahteyle değiştirir.

- [ ] **Adım 1: Başarısız testleri yaz**

`Kasa.App.Core.Tests/BildirimGoreviTests.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Kasa.App.Core.Tests;

/// <summary>Masaüstü bildirimlerinin zamanlanmış görevi (EmarKasaBildirim): schtasks pencere açmadan çalışır; görev UTF-16 XML ile
/// kurulur (günlük tetikleyici sunucu saati + 5 dk, bu kullanıcının oturum açılışı tetikleyicisi, yönetici hakkı istemeyen principal);
/// aynı saat/exe/kullanıcı ve görev duruyorsa yeniden kurulmaz; kurulamayan görev işaretlenmez; silme işareti kaldırır; hiçbir hata
/// dışarı çıkmaz. Gerçek schtasks Görev 11'de kullanıcının bilgisayarında denenir.</summary>
public sealed class BildirimGoreviTests : IDisposable
{
    private const string Exe = @"C:\Program Files\Emar Kasa\Kasa.App.exe";
    private const string Kullanici = @"MASA\burak";
    private static readonly XNamespace Gorev = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "kasa-bgorev-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_klasor, true);
        }
        catch (DirectoryNotFoundException) { }
    }

    private string Isaret => Path.Combine(_klasor, BildirimGorevi.IsaretDosyaAdi);

    /// <summary>Sırayla verilen çıkış kodlarını döner (null: başlatılamadı ya da süre doldu); /XML dosyasının baytlarını saklar.</summary>
    private sealed class Calistirici(params int?[] sonuclar)
    {
        private int _sira;
        public List<string> Ozet { get; } = [];
        public byte[]? Xml { get; private set; }

        public Task<int?> Calistir(ProcessStartInfo komut)
        {
            var argumanlar = komut.ArgumentList.ToList();
            var xmlSirasi = argumanlar.IndexOf("/XML");
            if (xmlSirasi >= 0)
            {
                Xml = File.ReadAllBytes(argumanlar[xmlSirasi + 1]);
                argumanlar[xmlSirasi + 1] = Path.GetFileName(argumanlar[xmlSirasi + 1]);
            }
            Ozet.Add(komut.FileName + " " + string.Join(" ", argumanlar));
            return Task.FromResult(sonuclar[_sira++]);
        }

        public List<string> Komutlar => Ozet.Select(o => o.Split(' ')[1]).ToList();
    }

    private BildirimGorevi Kur(Calistirici c) => new(_klasor, Exe, Kullanici, c.Calistir);

    [Fact]
    public void Komutlar_pencere_acmadan_schtasks_ile_calisir()
    {
        // (sonradan: XmlDosyaOneki + guid; geçici XML adı bildirim-gorevi-<guid>.xml)
        var k = BildirimGorevi.KurmaKomutu(@"C:\x\bildirim-gorevi.xml");
        Assert.Equal("schtasks", k.FileName);
        Assert.Equal(["/Create", "/TN", "EmarKasaBildirim", "/XML", @"C:\x\bildirim-gorevi.xml", "/F"], k.ArgumentList);
        Assert.Equal((false, true, true, true), (k.UseShellExecute, k.CreateNoWindow, k.RedirectStandardOutput, k.RedirectStandardError));
        Assert.Equal(["/Delete", "/TN", "EmarKasaBildirim", "/F"], BildirimGorevi.SilmeKomutu().ArgumentList);
        Assert.Equal(["/Query", "/TN", "EmarKasaBildirim"], BildirimGorevi.SorguKomutu().ArgumentList);
        Assert.Equal("--bildirim-kontrol", BildirimGorevi.KontrolArgumani);
    }

    [Fact]
    public void Gorev_belgesi_gunluk_ve_bu_kullanicinin_oturum_acilisi_tetikleyicisini_tasir()
    {
        var belge = BildirimGorevi.GorevBelgesi(new TimeOnly(0, 3), Exe, Kullanici);
        string Tek(string ad) => belge.Descendants(Gorev + ad).Single().Value;
        Assert.Equal("2026-01-01T00:03:00", Tek("StartBoundary"));
        Assert.Equal("1", Tek("DaysInterval"));
        Assert.Equal([Kullanici, Kullanici], belge.Descendants(Gorev + "UserId").Select(e => e.Value));
        Assert.Equal("LogonTrigger", belge.Descendants(Gorev + "UserId").First().Parent!.Name.LocalName);
        Assert.Equal("PT1M", Tek("Delay"));
        Assert.Equal("InteractiveToken", Tek("LogonType"));
        Assert.Equal("LeastPrivilege", Tek("RunLevel"));
        Assert.Equal("true", Tek("StartWhenAvailable"));
        Assert.Equal("IgnoreNew", Tek("MultipleInstancesPolicy"));
        Assert.Equal("PT2M", Tek("ExecutionTimeLimit"));
        Assert.Equal(Exe, Tek("Command"));
        Assert.Equal("--bildirim-kontrol", Tek("Arguments"));
    }

    [Fact]
    public async Task Gorev_utf16_xml_ile_kurulur_isaret_yazilir_ayni_saatte_yeniden_kurulmaz()
    {
        var c = new Calistirici(0, 0);
        Assert.True(await Kur(c).GuncelleAsync(9, 0));
        // (sonradan: XmlDosyaOneki + guid; XmlDosyaAdi yerine bildirim-gorevi-<guid>.xml ve kalıntı denetimi bildirim-gorevi-*.xml)
        Assert.Equal(["schtasks /Create /TN EmarKasaBildirim /XML bildirim-gorevi.xml /F"], c.Ozet);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, c.Xml![..2]);
        var metin = Encoding.Unicode.GetString(c.Xml, 2, c.Xml.Length - 2);
        Assert.StartsWith(@"<?xml version=""1.0"" encoding=""utf-16""?>", metin);
        Assert.Contains("2026-01-01T09:05:00", metin);
        Assert.False(File.Exists(Path.Combine(_klasor, BildirimGorevi.XmlDosyaAdi)));
        Assert.Equal(@"09:05|C:\Program Files\Emar Kasa\Kasa.App.exe|MASA\burak", File.ReadAllText(Isaret));
        // Aynı saat, exe ve kullanıcı; görev duruyor (sorgu 0): schtasks /Create yeniden çalışmaz.
        Assert.False(await Kur(c).GuncelleAsync(9, 0));
        Assert.Equal(["/Create", "/Query"], c.Komutlar);
    }

    [Fact]
    public async Task Saat_ya_da_exe_degisince_ya_da_gorev_silinmisse_yeniden_kurulur()
    {
        var c = new Calistirici(0, 0, 0, 1, 0);
        Assert.True(await Kur(c).GuncelleAsync(9, 0));
        Assert.True(await Kur(c).GuncelleAsync(10, 30));
        Assert.Contains("2026-01-01T10:35:00", Encoding.Unicode.GetString(c.Xml!));
        var yeniExe = new BildirimGorevi(_klasor, @"D:\Yeni\Kasa.App.exe", Kullanici, c.Calistir);
        Assert.True(await yeniExe.GuncelleAsync(10, 30));
        // İmza aynı ama görev elle silinmiş (sorgu 1): yeniden kurulur.
        Assert.True(await yeniExe.GuncelleAsync(10, 30));
        Assert.Equal(["/Create", "/Create", "/Create", "/Query", "/Create"], c.Komutlar);
    }

    [Theory]
    [InlineData(new int[] { 1 })]      // schtasks hata verdi
    [InlineData(new int[] { -1 })]     // başlatılamadı ya da süre doldu (null)
    public async Task Kurulamayan_gorev_isaretlenmez_sonraki_acilista_yeniden_denenir(int[] sonuclar)
    {
        var c = new Calistirici([.. sonuclar.Select(s => s < 0 ? (int?)null : s)]);
        Assert.False(await Kur(c).GuncelleAsync(9, 0));
        Assert.False(File.Exists(Isaret));
        Assert.False(File.Exists(Path.Combine(_klasor, BildirimGorevi.XmlDosyaAdi)));
        Assert.True(await Kur(new Calistirici(0)).GuncelleAsync(9, 0));
    }

    [Fact]
    public async Task Silme_isareti_kaldirir_gorev_yoksa_da_basarilidir()
    {
        var c = new Calistirici(0, 0, 1, 1, 1, 0);
        Assert.True(await Kur(c).GuncelleAsync(9, 0));
        Assert.True(await Kur(c).SilAsync());
        Assert.False(File.Exists(Isaret));
        // Silinemedi, sorgu başarısız: görev zaten yok.
        Assert.True(await Kur(c).SilAsync());
        // Silinemedi, sorgu başarılı: görev hâlâ duruyor.
        Assert.False(await Kur(c).SilAsync());
        Assert.Equal(["/Create", "/Delete", "/Delete", "/Query", "/Delete", "/Query"], c.Komutlar);
    }

    [Fact]
    public async Task Calistirici_klasor_ya_da_saat_hatasi_disari_cikmaz()
    {
        Assert.False(await new BildirimGorevi(_klasor, Exe, Kullanici, _ => throw new InvalidOperationException("çalıştırılamadı")).GuncelleAsync(9, 0));
        Assert.False(await new BildirimGorevi(_klasor, Exe, Kullanici, _ => throw new InvalidOperationException("çalıştırılamadı")).SilAsync());
        Assert.False(await Kur(new Calistirici(0)).GuncelleAsync(24, 0));
        File.WriteAllText(_klasor + ".dosya", "");
        try
        {
            var gorev = new BildirimGorevi(Path.Combine(_klasor + ".dosya", "alt"), Exe, Kullanici, new Calistirici(0).Calistir);
            Assert.False(await gorev.GuncelleAsync(9, 0));
        }
        finally
        {
            File.Delete(_klasor + ".dosya");
        }
    }
}
```

- [ ] **Adım 2: Testlerin başarısız olduğunu gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~BildirimGoreviTests"`
Beklenen: derleme hatası `CS0246: The type or namespace name 'BildirimGorevi' could not be found`.

- [ ] **Adım 3: En küçük uygulamayı yaz**

`Kasa.App.Core/BildirimGorevi.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Kasa.App.Core;

/// <summary>Bu bilgisayardaki bildirim görevini kuran ve silen arayüz (BildirimNobetcisi); testler sahtesini kullanır.</summary>
public interface IBildirimGorevi
{
    /// <summary>Görevi sunucu bildirim saatine göre kurar ya da günceller; görev aynı saatle zaten duruyorsa dokunmaz. Bu çağrıda
    /// kurulduysa true; değişiklik gerekmediyse ya da kurulamadıysa false. Hata dışarı çıkmaz.</summary>
    Task<bool> GuncelleAsync(int saat, int dakika);

    /// <summary>Görevi siler. Silindiyse ya da zaten yoksa true. Hata dışarı çıkmaz.</summary>
    Task<bool> SilAsync();
}

/// <summary>
/// Masaüstü bildirimlerinin kullanıcı düzeyi Windows zamanlanmış görevi (<see cref="GorevAdi"/>): uygulamayı pencere açmadan
/// <see cref="KontrolArgumani"/> ile çalıştırır (BildirimKontrolu). Tek görevde iki tetikleyici: her gün sunucu bildirim saatinden 5 dk
/// sonra (<see cref="BildirimGorevZamani"/>) ve bu kullanıcının Windows oturumu açılınca (1 dk gecikmeyle). Görev XML ile kurulur
/// (schtasks /Create /XML): "/SC ONLOGON" her kullanıcının oturumu demektir ve yönetici hakkı ister; XML'deki LogonTrigger UserId'si
/// görevi bu kullanıcıyla sınırlar. Principal InteractiveToken + LeastPrivilege: parola sorulmaz, görev yalnız kullanıcı oturumu
/// açıkken çalışır. XML dosyası UTF-16 (BOM'lu) yazılır; schtasks UTF-8 bildirimli dosyayı reddeder. Kurulum imzası (saat, exe yolu,
/// kullanıcı) yerel işaret dosyasındadır: imza aynı ve görev duruyorsa schtasks /Create çalıştırılmaz. EskiHatirlatmaGorevi deseni:
/// pencere yok (CreateNoWindow), süre sınırı, hatalar yutulur.
/// </summary>
public sealed class BildirimGorevi : IBildirimGorevi
{
    public const string GorevAdi = "EmarKasaBildirim";
    /// <summary>Görevin exe'ye verdiği argüman: pencere açılmaz, bakılır, çıkılır (Platforms/Windows/App.xaml.cs).</summary>
    public const string KontrolArgumani = "--bildirim-kontrol";
    public const string XmlDosyaAdi = "bildirim-gorevi.xml"; // (sonradan: XmlDosyaOneki = "bildirim-gorevi-" + guid + ".xml")
    public const string IsaretDosyaAdi = "bildirim-gorevi.txt";
    private static readonly XNamespace Ad = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly string _klasor;
    private readonly string _exeYolu;
    private readonly string _kullanici;
    private readonly Func<ProcessStartInfo, Task<int?>> _calistir;

    /// <param name="calistir">Komutu çalıştırıp çıkış kodunu döner; başlatılamaz ya da süre dolarsa null.</param>
    public BildirimGorevi(string klasor, string exeYolu, string kullanici, Func<ProcessStartInfo, Task<int?>> calistir)
    {
        _klasor = klasor;
        _exeYolu = exeYolu;
        _kullanici = kullanici;
        _calistir = calistir;
    }

    /// <summary>Gerçek ortam: %LOCALAPPDATA%\EmarKasa, çalışan exe, ETKİALANI\kullanıcı, gerçek schtasks (5 sn sınırı).</summary>
    public static BildirimGorevi Varsayilan() => new(YerelKlasor.Yol, Environment.ProcessPath ?? "Kasa.App.exe",
        $@"{Environment.UserDomainName}\{Environment.UserName}", k => EskiHatirlatmaGorevi.CalistirAsync(k, EskiHatirlatmaGorevi.ZamanAsimi));

    public static ProcessStartInfo KurmaKomutu(string xmlYolu) => Komut("/Create", "/TN", GorevAdi, "/XML", xmlYolu, "/F");
    public static ProcessStartInfo SilmeKomutu() => Komut("/Delete", "/TN", GorevAdi, "/F");
    public static ProcessStartInfo SorguKomutu() => Komut("/Query", "/TN", GorevAdi);

    private static ProcessStartInfo Komut(params string[] argumanlar)
    {
        var komut = new ProcessStartInfo("schtasks") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in argumanlar)
            komut.ArgumentList.Add(a);
        return komut;
    }

    /// <summary>Kurulumun imzası: görev saati, exe yolu ve kullanıcı; biri değişince görev yeniden kurulur.</summary>
    public static string Imza(TimeOnly zaman, string exeYolu, string kullanici)
        => string.Join("|", zaman.ToString("HH:mm", CultureInfo.InvariantCulture), exeYolu, kullanici);

    /// <summary>Task Scheduler 1.2 görev tanımı. StartBoundary'nin tarihi geçmiştedir (tetikleyici o günden beri her gün çalışır);
    /// saat dilimi yazılmaz: yerel saattir.</summary>
    public static XDocument GorevBelgesi(TimeOnly zaman, string exeYolu, string kullanici) => new(
        new XElement(Ad + "Task", new XAttribute("version", "1.2"),
            new XElement(Ad + "RegistrationInfo",
                new XElement(Ad + "Description", "Emar Kasa: sunucudaki hatırlatmaları Windows bildirimi olarak gösterir.")),
            new XElement(Ad + "Triggers",
                new XElement(Ad + "CalendarTrigger",
                    new XElement(Ad + "StartBoundary", "2026-01-01T" + zaman.ToString("HH:mm", CultureInfo.InvariantCulture) + ":00"),
                    new XElement(Ad + "ScheduleByDay", new XElement(Ad + "DaysInterval", 1))),
                new XElement(Ad + "LogonTrigger",
                    new XElement(Ad + "UserId", kullanici),
                    new XElement(Ad + "Delay", "PT1M"))),
            new XElement(Ad + "Principals",
                new XElement(Ad + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ad + "UserId", kullanici),
                    new XElement(Ad + "LogonType", "InteractiveToken"),
                    new XElement(Ad + "RunLevel", "LeastPrivilege"))),
            new XElement(Ad + "Settings",
                new XElement(Ad + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ad + "DisallowStartIfOnBatteries", false),
                new XElement(Ad + "StopIfGoingOnBatteries", false),
                new XElement(Ad + "StartWhenAvailable", true),
                new XElement(Ad + "AllowStartOnDemand", true),
                new XElement(Ad + "Enabled", true),
                new XElement(Ad + "ExecutionTimeLimit", "PT2M")),
            new XElement(Ad + "Actions", new XAttribute("Context", "Author"),
                new XElement(Ad + "Exec",
                    new XElement(Ad + "Command", exeYolu),
                    new XElement(Ad + "Arguments", KontrolArgumani)))));

    /// <summary>XML'i UTF-16 LE (BOM'lu, bildirimi encoding="utf-16") yazar.</summary>
    public static void XmlYaz(XDocument belge, string yol)
    {
        using var yazici = XmlWriter.Create(yol, new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true });
        belge.Save(yazici);
    }

    public async Task<bool> GuncelleAsync(int saat, int dakika)
    {
        try
        {
            var zaman = BildirimGorevZamani.Hesapla(saat, dakika);
            var imza = Imza(zaman, _exeYolu, _kullanici);
            var isaret = Path.Combine(_klasor, IsaretDosyaAdi);
            if (File.Exists(isaret) && File.ReadAllText(isaret) == imza && await _calistir(SorguKomutu()) == 0)
                return false;
            Directory.CreateDirectory(_klasor);
            var xml = Path.Combine(_klasor, XmlDosyaAdi);
            XmlYaz(GorevBelgesi(zaman, _exeYolu, _kullanici), xml);
            try
            {
                if (await _calistir(KurmaKomutu(xml)) != 0)
                    return false;
            }
            finally
            {
                File.Delete(xml);
            }
            File.WriteAllText(isaret, imza);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> SilAsync()
    {
        try
        {
            var isaret = Path.Combine(_klasor, IsaretDosyaAdi);
            if (File.Exists(isaret))
                File.Delete(isaret);
            var sonuc = await _calistir(SilmeKomutu());
            if (sonuc == 0)
                return true;
            // Silinemedi: görev yoksa (sorgu başarısız) iş bitmiştir; görev duruyor ya da sorgulanamadıysa başarısız.
            return sonuc is not null && await _calistir(SorguKomutu()) is not (null or 0);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
```

Not: `$@"{Environment.UserDomainName}\{Environment.UserName}"` tek ters bölü yazar (kaçış dizisi yok); yazdıktan sonra
`grep -n 'UserDomainName' Kasa.App.Core/BildirimGorevi.cs` ile satırın aynen durduğunu denetleyin.

- [ ] **Adım 4: Testlerin geçtiğini gör**

Çalıştır: Adım 2'deki komut.
Beklenen: `Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8`

- [ ] **Adım 5: Commit**

```bash
git add Kasa.App.Core/BildirimGorevi.cs Kasa.App.Core.Tests/BildirimGoreviTests.cs
git commit -F - <<'EOF'
feat(app-core): bildirim zamanlanmış görevi (schtasks XML, kullanıcı düzeyi)

Günlük (sunucu saati + 5 dk) ve bu kullanıcının oturum açılışı tetikleyicisi tek
görevde; UTF-16 XML, InteractiveToken + LeastPrivilege, yönetici hakkı gerekmez.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 5: Nöbetçi (uygulama açıkken), pencere açmadan kontrol ve pencere kilidi

**Dosyalar:**
- Oluştur: `Kasa.App.Core/BildirimNobetcisi.cs`
- Oluştur: `Kasa.App.Core/BildirimKontrolu.cs`
- Oluştur: `Kasa.App.Core/PencereKilidi.cs`
- Değiştir: `Kasa.App.Core.Tests/BildirimSahteleri.cs` (`SahteGorev`, `BildirimOrtami.Gorev`/`Nobetci`)
- Test: `Kasa.App.Core.Tests/BildirimNobetcisiTests.cs`, `Kasa.App.Core.Tests/BildirimKontroluTests.cs`, `Kasa.App.Core.Tests/PencereKilidiTests.cs`

- [ ] **Adım 1: Sahteleri genişlet**

`Kasa.App.Core.Tests/BildirimSahteleri.cs` içinde `internal sealed class SahteAyar : IBildirimAyari` satırının hemen üstüne ekle:

```csharp
/// <summary>Zamanlanmış görevin sahtesi: çağrıları "kur HH:mm" ve "sil" olarak kaydeder.</summary>
internal sealed class SahteGorev : IBildirimGorevi
{
    public List<string> Cagrilar { get; } = [];

    public Task<bool> GuncelleAsync(int saat, int dakika)
    {
        Cagrilar.Add($"kur {saat:00}:{dakika:00}");
        return Task.FromResult(true);
    }

    public Task<bool> SilAsync()
    {
        Cagrilar.Add("sil");
        return Task.FromResult(true);
    }
}

```

Aynı dosyada `BildirimOrtami` kurucusunda şu satırı:

```csharp
        Yoklayici = new BildirimYoklayici(Sunucu, Gosterici, Depo, Ayar, Saat);
```

şununla değiştir:

```csharp
        Yoklayici = new BildirimYoklayici(Sunucu, Gosterici, Depo, Ayar, Saat);
        Nobetci = new BildirimNobetcisi(Yoklayici, Sunucu, Gorev, Ayar, Gosterici, Auth);
```

ve `public BildirimYoklayici Yoklayici { get; }` satırının altına ekle:

```csharp
    public SahteGorev Gorev { get; } = new();
    public BildirimNobetcisi Nobetci { get; }
```

- [ ] **Adım 2: Başarısız testleri yaz**

`Kasa.App.Core.Tests/BildirimNobetcisiTests.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Uygulama açıkken bildirimler (tasarım 2026-09-30 masaüstü bildirimleri §1-2): editör oturumu açılınca görev sunucu saatine
/// göre kurulur ve bir kez bakılır; editör olmayan oturumda bakılmaz; çıkışta ve rol değişiminde rozet ve durum sıfırlanır; anahtar
/// kapatılınca görev silinir, açılınca kurulur; saat kaydedilince görev güncellenir; deneme ve tıklama nöbetçiden geçer.</summary>
public class BildirimNobetcisiTests
{
    private static BildirimDto B(int id) => SahteBildirimApi.Bildirim(id, BildirimOrtami.Bugun);

    [Fact]
    public async Task Editor_oturumu_acilinca_gorev_sunucu_saatine_gore_kurulur_ve_bir_kez_bakilir()
    {
        var o = new BildirimOrtami();
        o.Api.Ayar = new BildirimAyarDto(true, 10, 30, "Europe/Istanbul", 4);
        o.Api.Liste = [B(1)];
        await o.Nobetci.OturumAcildiAsync();
        Assert.Equal(["kur 10:30"], o.Gorev.Cagrilar);
        Assert.Equal([1], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal(1, o.Yoklayici.Okunmamis);
        Assert.Equal("Son kontrol 14:05 · 1 yeni bildirim", o.Nobetci.DurumMetni);
        Assert.Equal(TimeSpan.FromMinutes(5), BildirimNobetcisi.Aralik);
    }

    [Theory]
    [InlineData(Rol.Izleyici)]
    [InlineData(Rol.Alici)]
    public async Task Editor_olmayan_oturumda_bakilmaz_gorev_kurulmaz(Rol rol)
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1)];
        o.Auth.AktifRol = rol;
        await o.Nobetci.OturumAcildiAsync();
        Assert.Null(await o.Nobetci.TikAsync());
        Assert.Empty(o.Gorev.Cagrilar);
        Assert.Equal(0, o.Api.ListeCagri);
    }

    [Fact]
    public async Task Cikista_ve_rol_degisiminde_rozet_ve_durum_sifirlanir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1), B(2)];
        await o.Nobetci.TikAsync();
        Assert.Equal(2, o.Yoklayici.Okunmamis);
        o.Auth.GirisYapildi = false;
        Assert.Equal(0, o.Yoklayici.Okunmamis);
        Assert.Null(o.Yoklayici.SonSonuc);
        Assert.Null(await o.Nobetci.TikAsync());
        o.Auth.GirisYapildi = true;
        await o.Nobetci.TikAsync();
        Assert.Equal(2, o.Yoklayici.Okunmamis);
        o.Auth.AktifRol = Rol.Izleyici;
        Assert.Equal(0, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Ayar_kapatilinca_gorev_silinir_bakma_durur_acilinca_kurulur_ve_bakilir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1)];
        await o.Nobetci.AcikAyarlaAsync(false);
        Assert.False(o.Ayar.Acik);
        Assert.Equal(["sil"], o.Gorev.Cagrilar);
        Assert.Null(await o.Nobetci.TikAsync());
        Assert.Equal("Bu bilgisayarda Windows bildirimleri kapalı.", o.Nobetci.DurumMetni);
        await o.Nobetci.SaatDegistiAsync(11, 0);
        Assert.Equal(["sil"], o.Gorev.Cagrilar);
        await o.Nobetci.AcikAyarlaAsync(true);
        Assert.Equal(["sil", "kur 09:00"], o.Gorev.Cagrilar);
        Assert.Equal([1], o.Gosterici.Gosterilenler.Select(b => b.Id));
        // Değişmeyen değer: görev yeniden kurulmaz.
        await o.Nobetci.AcikAyarlaAsync(true);
        Assert.Equal(2, o.Gorev.Cagrilar.Count);
    }

    [Fact]
    public async Task Saat_kaydedilince_gorev_guncellenir_sunucu_saati_alinamazsa_bakma_surer()
    {
        var o = new BildirimOrtami();
        await o.Nobetci.SaatDegistiAsync(8, 15);
        Assert.Equal(["kur 08:15"], o.Gorev.Cagrilar);
        o.Api.AyarHatasi = new HttpRequestException("bağlantı yok");
        o.Api.Liste = [B(1)];
        await o.Nobetci.OturumAcildiAsync();
        Assert.Equal(["kur 08:15"], o.Gorev.Cagrilar);
        Assert.Single(o.Gosterici.Gosterilenler);
    }

    [Fact]
    public async Task Deneme_bildirimi_windows_ayari_ve_tiklama_nobetciden_gecer()
    {
        var o = new BildirimOrtami();
        Assert.True(o.Nobetci.DenemeGoster());
        Assert.Equal(1, o.Gosterici.DenemeSayisi);
        Assert.Equal(0, o.Api.ListeCagri);
        o.Gosterici.WindowsAyarindaKapali = true;
        Assert.True(o.Nobetci.WindowsAyarindaKapali);
        Assert.Equal("//kartlar?KartId=4", await o.Nobetci.TiklamayiIsleAsync(new BildirimTiklamasi(9, "/#cards/4")));
        Assert.Equal([9], o.Api.Okunanlar);
        o.Auth.GirisYapildi = false;
        Assert.Null(await o.Nobetci.TiklamayiIsleAsync(new BildirimTiklamasi(10, "/#cards/4")));
        Assert.Equal([9], o.Api.Okunanlar);
    }
}
```

`Kasa.App.Core.Tests/BildirimKontroluTests.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Pencere açmadan çalışma (--bildirim-kontrol, tasarım 2026-09-30 masaüstü bildirimleri §1): kayıtlı belirteçle rol sorulur,
/// editörse yeni bildirimler gösterilir; belirteç yok, süresi dolmuş, rol editör değil ya da ayar kapalıysa sessizce çıkılır; en çok
/// 60 saniye çalışılır.</summary>
public class BildirimKontroluTests
{
    private static (BildirimOrtami O, SahteApi Oturum, BildirimKontrolu Kontrol) Kur(string? rol = "editor")
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [SahteBildirimApi.Bildirim(1, BildirimOrtami.Bugun)];
        var oturum = new SahteApi { MeRol = rol };
        return (o, oturum, new BildirimKontrolu(oturum, o.Yoklayici, o.Ayar));
    }

    [Fact]
    public void Kontrol_argumani_taninir()
    {
        Assert.True(BildirimKontrolu.KontrolModu(["Kasa.App.exe", "--bildirim-kontrol"]));
        Assert.False(BildirimKontrolu.KontrolModu(["Kasa.App.exe", "--hatirlatma-kontrol"]));
        Assert.False(BildirimKontrolu.KontrolModu(["Kasa.App.exe"]));
        Assert.Equal(TimeSpan.FromSeconds(60), BildirimKontrolu.EnUzunSure);
    }

    [Fact]
    public async Task Editor_oturumunda_yeni_bildirimler_gosterilir()
    {
        var (o, _, kontrol) = Kur();
        var sonuc = await kontrol.CalistirAsync();
        Assert.Equal(1, sonuc!.YeniSayisi);
        Assert.Equal([1], o.Gosterici.Gosterilenler.Select(b => b.Id));
    }

    [Theory]
    [InlineData("viewer")]
    [InlineData("alici")]
    [InlineData(null)]
    public async Task Editor_olmayan_ya_da_rolsuz_oturumda_hicbir_sey_gosterilmez(string? rol)
    {
        var (o, _, kontrol) = Kur(rol);
        Assert.Null(await kontrol.CalistirAsync());
        Assert.Equal(0, o.Api.ListeCagri);
        Assert.Empty(o.Gosterici.Gosterilenler);
    }

    [Fact]
    public async Task Oturum_suresi_dolmussa_ya_da_ayar_kapaliysa_sessizce_cikilir()
    {
        var (o, oturum, kontrol) = Kur();
        oturum.MeHatasi = new KasaApiException(HttpStatusCode.Unauthorized);
        Assert.Null(await kontrol.CalistirAsync());
        oturum.MeHatasi = null;
        o.Ayar.Acik = false;
        Assert.Null(await kontrol.CalistirAsync());
        Assert.Equal(0, o.Api.ListeCagri);
        Assert.Empty(o.Gosterici.Gosterilenler);
    }

    [Fact]
    public async Task Sure_dolunca_beklemeden_cikilir()
    {
        var (o, _, kontrol) = Kur();
        o.Api.Bekleyen = new TaskCompletionSource<IReadOnlyList<BildirimDto>>().Task;
        var sure = Stopwatch.StartNew();
        Assert.Null(await kontrol.CalistirAsync(TimeSpan.FromMilliseconds(200)));
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(10), $"süre {sure.Elapsed}");
        Assert.Empty(o.Gosterici.Gosterilenler);
    }
}
```

`Kasa.App.Core.Tests/PencereKilidiTests.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Pencereli süreç adlı mutex tutar; pencere açmadan çalışan görev onu görünce çıkar. Kilit bırakılınca görünmez.</summary>
public class PencereKilidiTests
{
    [Fact]
    public void Kilit_tutulurken_acik_birakilinca_kapali_gorunur()
    {
        var ad = @"Local\kasa-test-" + Guid.NewGuid().ToString("N");
        Assert.False(PencereKilidi.AcikMi(ad));
        using (PencereKilidi.Al(ad))
            Assert.True(PencereKilidi.AcikMi(ad));
        Assert.False(PencereKilidi.AcikMi(ad));
        Assert.Equal(@"Local\EmarKasa.Pencere", PencereKilidi.VarsayilanAd);
    }
}
```

- [ ] **Adım 3: Testlerin başarısız olduğunu gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~BildirimNobetcisiTests|FullyQualifiedName~BildirimKontroluTests|FullyQualifiedName~PencereKilidiTests"`
Beklenen: derleme hatası `CS0246: The type or namespace name 'BildirimNobetcisi' could not be found` (ve `BildirimKontrolu`,
`PencereKilidi` için aynısı).

- [ ] **Adım 4: Nöbetçiyi yaz**

`Kasa.App.Core/BildirimNobetcisi.cs`:

```csharp
using System.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Uygulama açıkken masaüstü bildirimlerinin yöneticisi (tasarım 2026-09-30 masaüstü bildirimleri §1-2): editör oturumu açılınca
/// zamanlanmış görevi sunucudaki bildirim saatine göre günceller ve bir kez bakar; kabuk (AppShell) <see cref="Aralik"/>ta bir
/// <see cref="TikAsync"/> çağırır. Oturum kapanınca ya da rol editör değilse rozet ve durum sıfırlanır. Bildirimler ekranının anahtarı,
/// deneme bildirimi ve saat kaydı buradan geçer. Zamanlayıcı ve pencere Kasa.App'tedir; bu sınıf Windows'tan bağımsızdır ve sınanır.
/// </summary>
public sealed class BildirimNobetcisi
{
    public static readonly TimeSpan Aralik = TimeSpan.FromMinutes(5);

    private readonly IBildirimApi _api;
    private readonly IBildirimGorevi _gorev;
    private readonly IBildirimAyari _ayar;
    private readonly IBildirimGosterici _gosterici;
    private readonly AuthViewModel _auth;

    public BildirimNobetcisi(BildirimYoklayici yoklayici, IBildirimApi api, IBildirimGorevi gorev, IBildirimAyari ayar,
        IBildirimGosterici gosterici, AuthViewModel auth)
    {
        Yoklayici = yoklayici;
        _api = api;
        _gorev = gorev;
        _ayar = ayar;
        _gosterici = gosterici;
        _auth = auth;
        auth.PropertyChanged += OturumDegisti;
    }

    public BildirimYoklayici Yoklayici { get; }

    public bool EditorOturumu => _auth.GirisYapildi && _auth.AktifRol == Rol.Editor;

    /// <summary>Bu bilgisayarda Windows bildirimleri açık.</summary>
    public bool Acik => _ayar.Acik;

    public bool WindowsAyarindaKapali => _gosterici.WindowsAyarindaKapali;

    /// <summary>Bildirimler ekranındaki durum satırı.</summary>
    public string DurumMetni => !Acik ? "Bu bilgisayarda Windows bildirimleri kapalı."
        : Yoklayici.SonSonuc?.Metin ?? "Henüz kontrol edilmedi.";

    /// <summary>Uygulama açılışında ve girişte (AppShell.MenuyuAc): editörse görev saati güncellenir ve bir kez bakılır.</summary>
    public async Task OturumAcildiAsync()
    {
        if (!EditorOturumu)
        {
            Yoklayici.Sifirla();
            return;
        }
        if (!_ayar.Acik)
            return;
        await GoreviSunucuSaatineGoreGuncelleAsync();
        await Yoklayici.YoklaAsync(EditorOturumu);
    }

    /// <summary>5 dakikalık bakma (AppShell zamanlayıcısı).</summary>
    public Task<YoklamaSonucu?> TikAsync() => Yoklayici.YoklaAsync(EditorOturumu);

    /// <summary>Bildirimler ekranının anahtarı: açınca görev kurulur ve bakılır, kapatınca görev silinir ve rozet gizlenir.</summary>
    public async Task AcikAyarlaAsync(bool acik)
    {
        if (acik == _ayar.Acik)
            return;
        _ayar.Acik = acik;
        if (acik)
        {
            await GoreviSunucuSaatineGoreGuncelleAsync();
            await Yoklayici.YoklaAsync(EditorOturumu);
        }
        else
        {
            await _gorev.SilAsync();
            Yoklayici.Sifirla();
        }
    }

    /// <summary>Hatırlatma saati kaydedildi: bu bilgisayardaki görevin saati de güncellenir (ayar açıksa).</summary>
    public async Task SaatDegistiAsync(int saat, int dakika)
    {
        if (_ayar.Acik)
            await _gorev.GuncelleAsync(saat, dakika);
    }

    public bool DenemeGoster() => _gosterici.DenemeGoster();

    /// <summary>Bildirim tıklaması: okundu işaretlenir, açılacak rota döner; editör oturumu yoksa null.</summary>
    public Task<string?> TiklamayiIsleAsync(BildirimTiklamasi tiklama) => Yoklayici.TiklandiAsync(tiklama, EditorOturumu);

    private async Task GoreviSunucuSaatineGoreGuncelleAsync()
    {
        try
        {
            var ayar = await _api.BildirimAyarlariAsync();
            await _gorev.GuncelleAsync(ayar.Saat, ayar.Dakika);
        }
        catch (Exception)
        {
            // Sunucu saati alınamazsa görev bir sonraki açılışta güncellenir; bakma sürer.
        }
    }

    private void OturumDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AuthViewModel.GirisYapildi) or nameof(AuthViewModel.AktifRol) && !EditorOturumu)
            Yoklayici.Sifirla();
    }
}
```

- [ ] **Adım 5: Pencere açmadan kontrolü ve pencere kilidini yaz**

`Kasa.App.Core/BildirimKontrolu.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Pencere açmadan çalışma (zamanlanmış görev; argüman <see cref="BildirimGorevi.KontrolArgumani"/>): ayar açıksa kayıtlı oturum
/// belirteciyle rolü sorar (/me), editörse bir kez bakar ve biter; en çok <see cref="EnUzunSure"/> çalışır. Belirteç yok, süresi
/// dolmuş, rol editör değil, ayar kapalı ya da sunucuya ulaşılamıyorsa sessizce null döner; uygulama bir sonraki açılışta zaten
/// bakar. Çağıran platform kodu Kasa.App/Platforms/Windows/App.xaml.cs'tedir (pencere kilidi, Windows bildirim kaydı, süreç çıkışı).
/// </summary>
public sealed class BildirimKontrolu(IKasaApi api, BildirimYoklayici yoklayici, IBildirimAyari ayar)
{
    public static readonly TimeSpan EnUzunSure = TimeSpan.FromSeconds(60);

    public static bool KontrolModu(IEnumerable<string> argumanlar) => argumanlar.Contains(BildirimGorevi.KontrolArgumani);

    /// <param name="sure">En uzun çalışma süresi (varsayılan <see cref="EnUzunSure"/>; testler kısaltır).</param>
    public async Task<YoklamaSonucu?> CalistirAsync(TimeSpan? sure = null)
    {
        try
        {
            return await IcAsync().WaitAsync(sure ?? EnUzunSure);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<YoklamaSonucu?> IcAsync()
    {
        if (!ayar.Acik)
            return null;
        var rol = SekmeModeli.RolCoz(await api.BenKimAsync());
        return await yoklayici.YoklaAsync(rol == Rol.Editor);
    }
}
```

`Kasa.App.Core/PencereKilidi.cs`:

```csharp
namespace Kasa.App.Core;

/// <summary>Uygulama penceresi açık mı: pencereli süreç adlı bir mutex tutar (<see cref="Al"/>); pencere açmadan çalışan bildirim
/// görevi (BildirimKontrolu) mutex varsa hiçbir şey yapmadan çıkar. Açık uygulama zaten 5 dakikada bir bakar; görev ayrıca bakarsa
/// aynı bildirimi iki süreç yarışır ve iki sürecin Windows bildirim kaydı karışır. Ad Windows oturumuna özeldir (Local\).</summary>
public sealed class PencereKilidi : IDisposable
{
    public const string VarsayilanAd = @"Local\EmarKasa.Pencere";

    private readonly Mutex _mutex;

    private PencereKilidi(Mutex mutex) => _mutex = mutex;

    public static PencereKilidi Al(string ad = VarsayilanAd) => new(new Mutex(false, ad));

    public static bool AcikMi(string ad = VarsayilanAd)
    {
        if (!Mutex.TryOpenExisting(ad, out var mutex))
            return false;
        mutex.Dispose();
        return true;
    }

    public void Dispose() => _mutex.Dispose();
}
```

- [ ] **Adım 6: Testlerin geçtiğini gör**

Çalıştır: Adım 3'teki komut.
Beklenen: `Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15`

Ardından bütün bildirim testleri birlikte:
Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~Bildirim|FullyQualifiedName~PencereKilidiTests"`
Beklenen: `Failed:     0` (Görev 1-5'in 65 testi + mevcut `BildirimTests`'in 5 testi = 70).

- [ ] **Adım 7: Commit**

```bash
git add Kasa.App.Core/BildirimNobetcisi.cs Kasa.App.Core/BildirimKontrolu.cs Kasa.App.Core/PencereKilidi.cs Kasa.App.Core.Tests/BildirimSahteleri.cs Kasa.App.Core.Tests/BildirimNobetcisiTests.cs Kasa.App.Core.Tests/BildirimKontroluTests.cs Kasa.App.Core.Tests/PencereKilidiTests.cs
git commit -F - <<'EOF'
feat(app-core): bildirim nöbetçisi, pencere açmadan kontrol ve pencere kilidi

Oturum açılınca görev sunucu saatine göre güncellenir ve bakılır; çıkışta rozet
sıfırlanır. --bildirim-kontrol 60 sn içinde editör oturumunda bakar ve çıkar.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 6: Windows katmanı — gösterici, `--bildirim-kontrol`, DI, 5 dakikalık bakma ve tıklama

**Dosyalar:**
- Oluştur: `Kasa.App/Platforms/Windows/WindowsBildirimGosterici.cs`
- Değiştir: `Kasa.App/Platforms/Windows/App.xaml.cs` (tamamı)
- Değiştir: `Kasa.App/MauiProgram.cs` (DI kayıtları)
- Değiştir: `Kasa.App/AppShell.xaml.cs` (kurucu, `MenuyuAc`, `GiriseDonAsync`, yeni yardımcılar)
- Doğrulama: derleme + `MauiKayitTutarliligiTests` (DI ve kabuk kaynak denetimleri)

Bu katman Windows'a bağlıdır ve test projesinde derlenmez; doğrulaması derleme, mevcut kaynak denetimleri ve Görev 11'deki gerçek
denemedir. Uygulama bu görevde çalıştırılmaz.

- [ ] **Adım 1: Windows App SDK sürümünü doğrula**

Çalıştır: `dotnet restore Kasa.App/Kasa.App.csproj -m:2 -nodeReuse:false` ardından
`grep -o '"Microsoft\.WindowsAppSDK[A-Za-z.]*/[0-9.]*"' Kasa.App/obj/project.assets.json | sort -u`
Beklenen: listede `"Microsoft.WindowsAppSDK/1.8.260529003"` ve `"Microsoft.WindowsAppSDK.Foundation/1.8.260527000"` bulunur
(AppNotifications `Microsoft.WindowsAppSDK.Foundation` içindedir; yeni paket eklenmez). Farklı sürüm çıkarsa sürümü plana not
et ve devam et; `Microsoft.WindowsAppSDK` hiç yoksa dur ve bildir.

- [ ] **Adım 2: Windows göstericisini yaz**

`Kasa.App/Platforms/Windows/WindowsBildirimGosterici.cs`:

```csharp
using System.Globalization;
using Kasa.ApiClient;
using Kasa.App.Core;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Kasa.App.WinUI;

/// <summary>
/// Windows App SDK uygulama bildirimleri (AppNotificationManager; paketsiz uygulama, WindowsPackageType=None; tasarım 2026-09-30
/// masaüstü bildirimleri §1 Gösterim). Paketsiz uygulamada Register() çağıran süreci COM sunucusu olarak kaydeder; uygulama adı ve
/// simgesi kabuktan alınır. Pencereli süreç <see cref="Baslat"/> ile önce NotificationInvoked işleyicisini bağlar, sonra kaydolur:
/// uygulama açıkken tıklama bu süreçte olay olarak gelir. Uygulama kapalıyken tıklama yeni süreç başlatır; tıklama etkinleştirme
/// bağımsız değişkenlerinden (AppInstance.GetActivatedEventArgs, ExtendedActivationKind.AppNotification) ya da kayıttan sonra aynı
/// olayla gelir; ikisi de <see cref="Tiklamalar"/>'a yazılır (son değer geçerli). Pencere açmadan çalışan görev işleyici bağlamadan
/// kaydolur (ilk gösterimde): o bildirime tıklanırsa Windows yeni (pencereli) süreç başlatır. Süreç bitmeden <see cref="Bitir"/>
/// (Unregister) çağrılır: sonraki tıklamalar uygulamayı yeniden başlatabilsin. Tıklama UI iş parçacığına Baslat'ta yakalanan
/// DispatcherQueue ile aktarılır (MAUI MainThread pencere yokken InvalidOperationException atar).
/// Kaynaklar: learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager.register,
/// learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart.
/// </summary>
public sealed class WindowsBildirimGosterici : IBildirimGosterici
{
    /// <summary>Sürecin tek örneği: App.xaml.cs DI kurulmadan önce başlatır, MauiProgram aynı örneği kaydeder.</summary>
    public static WindowsBildirimGosterici Ortak { get; } = new();

    private readonly Lock _kilit = new();
    private DispatcherQueue? _arayuz;
    private bool _basladi;
    private bool _kayitli;

    public BildirimTiklamalari Tiklamalar { get; } = new();

    /// <summary>Pencereli süreçte bir kez (OnLaunched, base.OnLaunched'dan önce, UI iş parçacığında).</summary>
    public void Baslat()
    {
        lock (_kilit)
        {
            if (_basladi)
                return;
            _basladi = true;
            _arayuz = DispatcherQueue.GetForCurrentThread();
            try
            {
                AppNotificationManager.Default.NotificationInvoked += (_, e) => Tiklandi(e.Arguments);
                AppNotificationManager.Default.Register();
                _kayitli = true;
                var etkinlestirme = AppInstance.GetCurrent().GetActivatedEventArgs();
                if (etkinlestirme.Kind == ExtendedActivationKind.AppNotification && etkinlestirme.Data is AppNotificationActivatedEventArgs bildirim)
                    Tiklandi(bildirim.Arguments);
            }
            catch (Exception)
            {
                // Bildirim altyapısı yoksa uygulama bildirimsiz açılır; Bildirimler ekranı Windows ayarı uyarısını gösterir.
            }
        }
    }

    /// <summary>Süreç bitmeden: Windows bildirim kaydı kaldırılır (kayıtlı değilse bir şey yapmaz).</summary>
    public void Bitir()
    {
        lock (_kilit)
        {
            if (!_kayitli)
                return;
            _kayitli = false;
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception) { }
        }
    }

    public bool WindowsAyarindaKapali
    {
        get
        {
            try
            {
                return AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    public void Goster(BildirimDto bildirim) => Yayinla(new AppNotificationBuilder()
        .AddArgument(BildirimTiklamasi.KimlikAnahtari, bildirim.Id.ToString(CultureInfo.InvariantCulture))
        .AddArgument(BildirimTiklamasi.HedefAnahtari, bildirim.Hedef)
        .AddText(bildirim.Baslik)
        .AddText(bildirim.Mesaj));

    public bool DenemeGoster()
    {
        try
        {
            Yayinla(new AppNotificationBuilder()
                .AddArgument(BildirimTiklamasi.HedefAnahtari, BildirimTiklamasi.DenemeHedefi)
                .AddText("Emar Kasa deneme bildirimi")
                .AddText("Windows bildirimleri bu bilgisayarda çalışıyor. Kart ve kredi hatırlatmaları böyle görünecek."));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Gösterir; süreç kayıtlı değilse (pencere açmadan çalışan görev) önce işleyicisiz kaydolur.</summary>
    private void Yayinla(AppNotificationBuilder icerik)
    {
        lock (_kilit)
        {
            if (!_kayitli)
            {
                AppNotificationManager.Default.Register();
                _kayitli = true;
            }
        }
        AppNotificationManager.Default.Show(icerik.BuildNotification());
    }

    /// <summary>Tıklama: UI iş parçacığında kuyruğa yazılır; uygulama zaten açıksa pencere öne getirilir.</summary>
    private void Tiklandi(IDictionary<string, string> argumanlar)
    {
        var tiklama = BildirimTiklamasi.Coz(argumanlar);
        if (_arayuz is null || _arayuz.HasThreadAccess)
        {
            Tiklamalar.Ekle(tiklama);
            return;
        }
        _arayuz.TryEnqueue(() =>
        {
            Tiklamalar.Ekle(tiklama);
            if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window pencere)
                pencere.Activate();
        });
    }
}
```

- [ ] **Adım 3: `Platforms/Windows/App.xaml.cs`'i değiştir**

`Kasa.App/Platforms/Windows/App.xaml.cs` dosyasının tamamını şununla değiştir (mevcut `--hatirlatma-kontrol` yolu aynen kalır):

```csharp
using Kasa.App.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Kasa.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Pencereli sürecin kilidi (PencereKilidi): süreç boyunca tutulur; pencere açmadan çalışan bildirim görevi bunu görünce
    /// çıkar (açık uygulama zaten bakar).</summary>
    private static PencereKilidi? Kilit { get; set; }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 07-15 hatırlatıcısının eski zamanlanmış görevi (EmarKasaHatirlatici) bir kez silinir: pencere açmadan, arka planda,
        // hataları yutarak; başarıdan sonra yerel işaretle bir daha denenmez (bkz. EskiHatirlatmaGorevi).
        var gorev = EskiHatirlatmaGorevi.Varsayilan();
        var argumanlar = Environment.GetCommandLineArgs();
        if (EskiHatirlatmaGorevi.KontrolModu(argumanlar))
        {
            // Eski zamanlanmış görev bu sürümde pencere veya bildirim açmaz: görevi siler ve çıkar.
            await Task.Run(() => gorev.TemizleAsync());
            Exit();
            return;
        }
        if (BildirimKontrolu.KontrolModu(argumanlar))
        {
            // Bildirim görevi (EmarKasaBildirim, tasarım 2026-09-30): pencere açmadan bakar, yeni bildirimleri gösterir ve çıkar.
            await BildirimKontroluAsync();
            Exit();
            return;
        }
        Kilit = PencereKilidi.Al();
        // Bildirim tıklaması: işleyici kayıttan önce bağlanır (uygulama açıkken tıklama bu süreçte işlenir); uygulama bildirim
        // tıklamasıyla başladıysa tıklama bekletilir, kabuk oturum açılınca uygular. Kayıt süreç biterken kaldırılır.
        WindowsBildirimGosterici.Ortak.Baslat();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WindowsBildirimGosterici.Ortak.Bitir();
        _ = Task.Run(() => gorev.TemizleAsync());
        base.OnLaunched(args);
    }

    /// <summary>Pencere açmadan bildirim kontrolü (BildirimKontrolu, en çok 60 sn): uygulama penceresi açıksa hiçbir şey yapmaz.
    /// DI 2.0'daki --hatirlatma-kontrol yolu gibi CreateMauiApp ile kurulur; base.OnLaunched çağrılmaz, pencere açılmaz. Oturum
    /// belirteci SecureStorage'dan okunur (paketsiz uygulamada dosya; pencere gerekmez). Hatalar yutulur; bildirim kaydı çıkmadan
    /// kaldırılır.</summary>
    private static async Task BildirimKontroluAsync()
    {
        if (PencereKilidi.AcikMi())
            return;
        try
        {
            var uygulama = MauiProgram.CreateMauiApp();
            await uygulama.Services.GetRequiredService<BildirimKontrolu>().CalistirAsync();
        }
        catch (Exception)
        {
            // Sessiz: uygulama bir sonraki açılışında zaten bakar.
        }
        finally
        {
            WindowsBildirimGosterici.Ortak.Bitir();
        }
    }
}
```

- [ ] **Adım 4: DI kayıtlarını ekle**

`Kasa.App/MauiProgram.cs` içinde şu satırın:

```csharp
        builder.Services.AddSingleton<IEkstreAktarmaApi>(sp => sp.GetRequiredService<KasaApiClient>());
```

hemen altına ekle:

```csharp

        // Masaüstü Windows bildirimleri (tasarım 2026-09-30): yerel dosyalar %LOCALAPPDATA%\EmarKasa altında; gösterici ve tıklama
        // kuyruğu Windows katmanının tek örneğidir (Platforms/Windows/App.xaml.cs onu DI kurulmadan önce başlatır).
        builder.Services.AddSingleton<IBildirimAyari>(_ => DosyaBildirimAyari.Varsayilan());
        builder.Services.AddSingleton<IGosterilenBildirimDeposu>(_ => DosyaGosterilenBildirimDeposu.Varsayilan());
        builder.Services.AddSingleton<IBildirimGorevi>(_ => BildirimGorevi.Varsayilan());
#if WINDOWS
        builder.Services.AddSingleton<IBildirimGosterici>(WinUI.WindowsBildirimGosterici.Ortak);
        builder.Services.AddSingleton<BildirimTiklamalari>(WinUI.WindowsBildirimGosterici.Ortak.Tiklamalar);
#endif
        builder.Services.AddSingleton<BildirimYoklayici>();
        builder.Services.AddSingleton<BildirimNobetcisi>();
        builder.Services.AddSingleton<BildirimKontrolu>();
```

Not: `BildirimYoklayici`'nın isteğe bağlı `TimeProvider? saat = null` parametresi DI'da kayıtlı değildir; varsayılana düşer
(`TimeProvider.System`), bu bilinçlidir (`MauiKayitTutarliligiTests.Kayitli_viewmodel_bagimliliklari_kayitli` yalnız ViewModel'leri
ve Kasa türlerini ister).

- [ ] **Adım 5: AppShell'e bakmayı ve tıklamayı bağla**

`Kasa.App/AppShell.xaml.cs` içinde:

(a) Alanlara, `private bool _cikiliyor;` satırının altına ekle:

```csharp
    private readonly BildirimNobetcisi _bildirimNobetcisi;
    private readonly BildirimTiklamalari _bildirimTiklamalari;
    /// <summary>Uygulama açıkken 5 dakikada bir bildirim bakması (BildirimNobetcisi.Aralik); oturum açılınca başlar, girişe dönüşte durur.</summary>
    private readonly IDispatcherTimer _bildirimZamanlayicisi;
```

(b) Kurucu imzasını:

```csharp
    public AppShell(AuthViewModel auth)
    {
        InitializeComponent();
        _auth = auth;
```

şununla değiştir:

```csharp
    public AppShell(AuthViewModel auth, BildirimNobetcisi bildirimNobetcisi, BildirimTiklamalari bildirimTiklamalari)
    {
        InitializeComponent();
        _auth = auth;
        _bildirimNobetcisi = bildirimNobetcisi;
        _bildirimTiklamalari = bildirimTiklamalari;
        _bildirimZamanlayicisi = Dispatcher.CreateTimer();
        _bildirimZamanlayicisi.Interval = BildirimNobetcisi.Aralik;
        _bildirimZamanlayicisi.IsRepeating = true;
```

(c) Kurucuda `Loaded += async (_, _) => await AcilistaYonlendirAsync();` satırının hemen üstüne ekle:

```csharp
        _bildirimZamanlayicisi.Tick += async (_, _) => await BildirimYoklaAsync();
        _bildirimTiklamalari.Istendi += async (_, _) => await TiklamayiUygulaAsync();
```

(d) `MenuyuAc`'ı:

```csharp
    public void MenuyuAc()
    {
        MenuyuGoster(SekmeModeli.Bolumler(_auth.AktifRol));
        _ = GoToAsync(_auth.AktifRol == Rol.Alici ? "//alislar" : "//panel");
    }
```

şununla değiştir:

```csharp
    public void MenuyuAc()
    {
        MenuyuGoster(SekmeModeli.Bolumler(_auth.AktifRol));
        _ = AcilisaGitAsync(_auth.AktifRol == Rol.Alici ? "//alislar" : "//panel");
    }

    /// <summary>Oturum açılınca: ilk sayfa, sonra bekleyen bildirim tıklaması (uygulama tıklamayla başladıysa), sonra bildirim bakması
    /// (editörse görev saati de güncellenir) ve 5 dakikalık zamanlayıcı.</summary>
    private async Task AcilisaGitAsync(string rota)
    {
        try
        {
            await GoToAsync(rota);
            await TiklamayiUygulaAsync();
            _bildirimZamanlayicisi.Start();
            await _bildirimNobetcisi.OturumAcildiAsync();
        }
        catch (Exception ex) { Debug.WriteLine($"Açılış gezinmesi başarısız: {ex}"); }
    }

    /// <summary>5 dakikalık bildirim bakması; hata günlüğe yazılır (async void işleyiciden istisna çıkmaz).</summary>
    private async Task BildirimYoklaAsync()
    {
        try
        {
            await _bildirimNobetcisi.TikAsync();
        }
        catch (Exception ex) { Debug.WriteLine($"Bildirim bakması başarısız: {ex}"); }
    }

    /// <summary>Bekleyen bildirim tıklaması: oturum açık değilse beklemede kalır ve girişten sonra uygulanır; editör oturumunda
    /// bildirim okundu işaretlenir ve hedef sayfa açılır.</summary>
    private async Task TiklamayiUygulaAsync()
    {
        try
        {
            if (!_auth.GirisYapildi || _bildirimTiklamalari.Al() is not { } tiklama)
                return;
            if (await _bildirimNobetcisi.TiklamayiIsleAsync(tiklama) is { } rota)
                await GoToAsync(rota);
        }
        catch (Exception ex) { Debug.WriteLine($"Bildirim tıklaması uygulanamadı: {ex}"); }
    }
```

(e) `GiriseDonAsync` içinde `MenuyuGoster([]);` satırının hemen üstüne ekle:

```csharp
            _bildirimZamanlayicisi.Stop();
```

- [ ] **Adım 6: Derle**

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
Beklenen: `Build succeeded.` ve `0 Error(s)`. Yeni uyarı çıkarsa (örn. CS0414, CS8602) giderin; uyarı sayısı önceki derlemeden fazla
olmamalı.

- [ ] **Adım 7: Kaynak denetimlerini çalıştır**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MauiKayitTutarliligiTests"`
Beklenen: `Failed:     0`. Özellikle `Kayitli_sayfa_ve_kabuk_bagimliliklari_kayitli` (AppShell'in yeni `BildirimNobetcisi` ve
`BildirimTiklamalari` parametreleri MauiProgram'da kayıtlı), `Kabuk_menu_olaylari_istisnayi_yakalar_ve_cikis_yeniden_girmez` ve
`Her_rol_bolumu_kabukta_menu_ogesine_bagli_ve_giriste_gizlenir` geçer.

Ardından: `bash .github/scripts/maui-lint.sh`
Beklenen: hata yok (çıkış kodu 0); taban artmaz.

- [ ] **Adım 8: Commit**

```bash
git add Kasa.App/Platforms/Windows/WindowsBildirimGosterici.cs Kasa.App/Platforms/Windows/App.xaml.cs Kasa.App/MauiProgram.cs Kasa.App/AppShell.xaml.cs
git commit -F - <<'EOF'
feat(app): Windows bildirim göstericisi, --bildirim-kontrol ve 5 dakikalık bakma

AppNotificationManager ile kayıt (işleyici önce), gösterim ve tıklama; kapalı
uygulamada etkinleştirme argümanları. Görev pencere açmadan bakar, açık pencere
varsa çıkar. Oturum açılınca bakılır, tıklama hedef sayfayı açar.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 7: Bildirimler ekranı — Windows bildirimleri kartı

**Dosyalar:**
- Değiştir: `Kasa.App.Core/BildirimViewModel.cs` (tamamı)
- Değiştir: `Kasa.App/Views/BildirimPage.cs` (tamamı)
- Değiştir: `Kasa.App.Core.Tests/BildirimTests.cs` (kurucu çağrıları)
- Test: `Kasa.App.Core.Tests/BildirimEkraniWindowsTests.cs`

- [ ] **Adım 1: Başarısız testleri yaz**

`Kasa.App.Core.Tests/BildirimEkraniWindowsTests.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Bildirimler ekranındaki "Bu bilgisayarda Windows bildirimleri" kartı (tasarım 2026-09-30 masaüstü bildirimleri §2):
/// anahtar bu bilgisayarın ayarını gösterir, kapatınca görev silinir, açınca kurulur ve bakılır; deneme bildirimi sunucuya gitmez;
/// durum satırı son bakmayı ve Windows ayarı uyarısını gösterir; saat kaydedilince görev güncellenir; liste yüklenince ve okundu
/// işaretlenince menü rozeti güncellenir.</summary>
public class BildirimEkraniWindowsTests
{
    private static (BildirimOrtami O, BildirimViewModel Vm) Kur()
    {
        var o = new BildirimOrtami();
        return (o, new BildirimViewModel(o.Api, o.Auth, o.Nobetci));
    }

    [Fact]
    public void Anahtar_bu_bilgisayarin_ayarini_gosterir_kapatinca_gorev_silinir_acinca_kurulur()
    {
        var (o, vm) = Kur();
        Assert.True(vm.WindowsBildirimleri);
        Assert.Equal("Henüz kontrol edilmedi.", vm.WindowsDurumu);
        vm.WindowsBildirimleri = false;
        Assert.False(o.Ayar.Acik);
        Assert.Equal(["sil"], o.Gorev.Cagrilar);
        Assert.Equal("Bu bilgisayarda Windows bildirimleri kapalı.", vm.WindowsDurumu);
        Assert.Equal("Bu bilgisayarda Windows bildirimleri kapatıldı.", vm.Mesaj);
        vm.WindowsBildirimleri = true;
        Assert.True(o.Ayar.Acik);
        Assert.Equal(["sil", "kur 09:00"], o.Gorev.Cagrilar);
        Assert.Equal("Son kontrol 14:05 · yeni bildirim yok", vm.WindowsDurumu);
        Assert.Equal("Bu bilgisayarda Windows bildirimleri açıldı.", vm.Mesaj);
    }

    [Fact]
    public void Deneme_bildirimi_sunucuya_gitmeden_gosterilir_windows_ayari_kapaliysa_uyari_cikar()
    {
        var (o, vm) = Kur();
        Assert.False(vm.WindowsAyarindaKapali);
        o.Gosterici.WindowsAyarindaKapali = true;
        vm.DenemeGosterCommand.Execute(null);
        Assert.Equal(1, o.Gosterici.DenemeSayisi);
        Assert.Equal(0, o.Api.ListeCagri);
        Assert.True(vm.WindowsAyarindaKapali);
        Assert.StartsWith("Deneme bildirimi gösterildi.", vm.Mesaj);
        o.Gosterici.DenemeBasarili = false;
        vm.DenemeGosterCommand.Execute(null);
        Assert.StartsWith("Deneme bildirimi gösterilemedi.", vm.Mesaj);
    }

    [Fact]
    public async Task Durum_satiri_son_bakmayi_gosterir()
    {
        var (o, vm) = Kur();
        o.Api.Liste = [SahteBildirimApi.Bildirim(1, BildirimOrtami.Bugun), SahteBildirimApi.Bildirim(2, BildirimOrtami.Bugun)];
        await o.Nobetci.TikAsync();
        Assert.Equal("Son kontrol 14:05 · 2 yeni bildirim", vm.WindowsDurumu);
        o.Api.ListeHatasi = new HttpRequestException("bağlantı yok");
        await o.Nobetci.TikAsync();
        Assert.Equal("Son kontrol 14:05 · sunucuya ulaşılamadı", vm.WindowsDurumu);
    }

    [Fact]
    public async Task Saat_kaydedilince_bu_bilgisayardaki_gorev_guncellenir()
    {
        var (o, vm) = Kur();
        await vm.YukleAsync();
        vm.Saat = 7;
        vm.Dakika = 45;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(["kur 07:45"], o.Gorev.Cagrilar);
        Assert.Equal("Bildirim ayarları kaydedildi. Saat Türkiye saatidir.", vm.Mesaj);
    }

    [Fact]
    public async Task Liste_yuklenince_ve_okundu_isaretlenince_menu_rozeti_guncellenir()
    {
        var (o, vm) = Kur();
        o.Api.Liste =
        [
            SahteBildirimApi.Bildirim(1, BildirimOrtami.Bugun),
            SahteBildirimApi.Bildirim(2, BildirimOrtami.Bugun, okundu: true),
            SahteBildirimApi.Bildirim(3, BildirimOrtami.Bugun.AddDays(-2)),
        ];
        await vm.YukleAsync();
        Assert.Equal(2, o.Yoklayici.Okunmamis);
        await vm.OkunduAsync(vm.Bildirimler.First(b => b.Veri.Id == 1));
        Assert.Equal([1], o.Api.Okunanlar);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
    }
}
```

`Kasa.App.Core.Tests/BildirimTests.cs` içinde mevcut üç kurucu çağrısını yeni imzaya taşı. Sınıfın başındaki satırı:

```csharp
    private static AuthViewModel Auth() => new(new SahteApi()) { AktifRol = Rol.Editor };
```

şununla değiştir:

```csharp
    private static AuthViewModel Auth() => new(new SahteApi()) { AktifRol = Rol.Editor };
    /// <summary>Ekran modeli; Windows bildirim bağımlılıkları sahtedir (BildirimOrtami), API bu testin sahtesidir.</summary>
    private static BildirimViewModel Vm(IBildirimApi api, AuthViewModel auth) => new(api, auth, new BildirimOrtami(api, auth).Nobetci);
```

ve üç yerdeki `new BildirimViewModel(api, Auth())` → `Vm(api, Auth())`, `new BildirimViewModel(api, auth)` → `Vm(api, auth)` yap
(satır 12, 29, 42 civarı).

- [ ] **Adım 2: Testlerin başarısız olduğunu gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~BildirimTests|FullyQualifiedName~BildirimEkraniWindowsTests"`
Beklenen: derleme hatası `CS1729: 'BildirimViewModel' does not contain a constructor that takes 3 arguments` (ve
`WindowsBildirimleri`, `WindowsDurumu`, `DenemeGosterCommand` için CS1061).

- [ ] **Adım 3: Ekran modelini değiştir**

`Kasa.App.Core/BildirimViewModel.cs` dosyasının tamamını şununla değiştir (iki satır kaydı aynen kalır; birincil kurucu açık kurucuya
döner, çünkü kurucu yoklayıcının olayına abone olur):

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public record BildirimSatiri(BildirimDto Veri)
{
    public string Baslik => (Veri.Okundu ? "" : "● ") + Veri.Baslik;
    public string Ozet => $"{Veri.Tarih:dd.MM.yyyy} · {Veri.Mesaj}";
}
public record BildirimCihaziSatiri(BildirimCihaziDto Veri)
{
    public string Baslik => Veri.CihazAdi + (Veri.Etkin ? " · açık" : " · kapalı");
    public string Ozet => Veri.SonBasarili is { } t ? $"Son iletim: {t.ToLocalTime():dd.MM.yyyy HH:mm}" : "Henüz başarılı iletim kaydı yok.";
}

/// <summary>
/// Bildirimler ekranı: bu bilgisayarın Windows bildirimleri (anahtar, deneme bildirimi, durum satırı, Windows ayarı uyarısı;
/// BildirimNobetcisi), hatırlatma saati (sunucu; kaydedilince bu bilgisayardaki zamanlanmış görev de güncellenir), hatırlatma listesi
/// (yüklenince ve okundu işaretlenince menü rozeti güncellenir) ve izin verilmiş tarayıcı/telefon cihazları (tasarım 2026-09-30
/// masaüstü bildirimleri §2).
/// </summary>
public partial class BildirimViewModel : OturumluViewModel
{
    private readonly IBildirimApi _api;
    private readonly BildirimNobetcisi _nobetci;
    private int _surum;

    public BildirimViewModel(IBildirimApi api, AuthViewModel auth, BildirimNobetcisi nobetci) : base(auth)
    {
        _api = api;
        _nobetci = nobetci;
        nobetci.Yoklayici.PropertyChanged += YoklamaDegisti;
        WindowsDurumunuYenile();
    }

    public ObservableCollection<BildirimSatiri> Bildirimler { get; } = new();
    public ObservableCollection<BildirimCihaziSatiri> Cihazlar { get; } = new();
    [ObservableProperty] private bool _etkin;
    [ObservableProperty] private int _saat = 9;
    [ObservableProperty] private int _dakika;
    [ObservableProperty] private string _cihazDurumu = "Cihaz bildirimlerinin durumu alınmadı.";
    [ObservableProperty] private bool _cihazBildirimiEtkin;
    /// <summary>"Bu bilgisayarda Windows bildirimleri" anahtarı (yalnız bu bilgisayar).</summary>
    [ObservableProperty] private bool _windowsBildirimleri;
    /// <summary>Durum satırı: "Son kontrol 14:05 · 2 yeni bildirim", kapalıysa kapalı olduğu.</summary>
    [ObservableProperty] private string _windowsDurumu = "";
    /// <summary>Windows ayarlarında bu uygulamanın bildirimleri kapalı: uyarı ve ayar bağlantısı görünür.</summary>
    [ObservableProperty] private bool _windowsAyarindaKapali;

    public Task YukleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu)
            return;
        VeriHazir = false;
        var ayar = await _api.BildirimAyarlariAsync();
        var bildirimler = await _api.BildirimlerAsync();
        var cihazlar = await _api.BildirimCihazlariAsync();
        var anahtar = await _api.BildirimAnahtariAsync();
        if (!Gecerli(n))
            return;
        AyariYansit(ayar);
        TakipMetni.Doldur(Bildirimler, bildirimler.Select(x => new BildirimSatiri(x)));
        TakipMetni.Doldur(Cihazlar, cihazlar.Select(x => new BildirimCihaziSatiri(x)));
        _nobetci.Yoklayici.OkunmamisBildir(bildirimler.Count(x => !x.Okundu));
        CihazBildirimiEtkin = anahtar.Etkin;
        CihazDurumu = anahtar.Etkin
            ? "Telefon bildirimleri sunucuda açık. Telefonunuzda izin vermek için aşağıdaki web kurulumunu kullanın."
            : "Telefon bildirimleri sunucuda henüz açılmamış. Hatırlatmalar bu ekranda ve Windows bildirimlerinde görünür.";
        WindowsDurumunuYenile();
        Tamamlandi();
    });
    private void AyariYansit(BildirimAyarDto a)
    {
        Etkin = a.Etkin;
        Saat = a.Saat;
        Dakika = a.Dakika;
        _surum = a.Surum;
    }
    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || _surum == 0)
            return;
        if (Saat is < 0 or > 23 || Dakika is < 0 or > 59)
        {
            Hata = "Saati 0–23, dakikayı 0–59 arasında girin.";
            return;
        }
        var a = await _api.BildirimAyarKaydetAsync(new(Etkin, Saat, Dakika, _surum));
        if (!Gecerli(n))
            return;
        AyariYansit(a);
        await _nobetci.SaatDegistiAsync(a.Saat, a.Dakika);
        Tamamlandi();
        Mesaj = "Bildirim ayarları kaydedildi. Saat Türkiye saatidir.";
    });
    public Task OkunduAsync(BildirimSatiri satir) => YurutAsync(async n =>
    {
        if (!EditorMu || satir.Veri.Okundu)
            return;
        await _api.BildirimOkunduAsync(satir.Veri.Id);
        if (!Gecerli(n))
            return;
        var index = Bildirimler.IndexOf(satir);
        if (index >= 0)
            Bildirimler[index] = new(satir.Veri with { Okundu = true });
        _nobetci.Yoklayici.OkunmamisBildir(Bildirimler.Count(x => !x.Veri.Okundu));
    });
    public Task CihaziKaldirAsync(BildirimCihaziSatiri satir) => YurutAsync(async n =>
    {
        if (!EditorMu || !satir.Veri.Etkin)
            return;
        await _api.BildirimCihaziKaldirAsync(satir.Veri.Id);
        if (!Gecerli(n))
            return;
        var index = Cihazlar.IndexOf(satir);
        if (index >= 0)
            Cihazlar[index] = new(satir.Veri with { Etkin = false });
        Mesaj = "Bu cihazın bildirimleri kapatıldı.";
    });

    /// <summary>Anahtar değişti: değer bu bilgisayarın ayarından farklıysa görev kurulur/silinir (BildirimNobetcisi.AcikAyarlaAsync;
    /// hata dışarı çıkmaz). Ekran ayarı yansıtırken (WindowsDurumunuYenile) değer aynıdır, bir şey yapılmaz.</summary>
    partial void OnWindowsBildirimleriChanged(bool value)
    {
        if (value != _nobetci.Acik)
            _ = AnahtarUygulaAsync(value);
    }

    private async Task AnahtarUygulaAsync(bool acik)
    {
        await _nobetci.AcikAyarlaAsync(acik);
        WindowsDurumunuYenile();
        Mesaj = acik ? "Bu bilgisayarda Windows bildirimleri açıldı." : "Bu bilgisayarda Windows bildirimleri kapatıldı.";
    }

    [RelayCommand]
    private void DenemeGoster()
    {
        Mesaj = _nobetci.DenemeGoster()
            ? "Deneme bildirimi gösterildi. Görmediyseniz Windows bildirim ayarlarını kontrol edin."
            : "Deneme bildirimi gösterilemedi. Windows bildirim ayarlarını kontrol edin.";
        WindowsDurumunuYenile();
    }

    /// <summary>Anahtar, durum satırı ve Windows ayarı uyarısı (açılışta, sayfa her göründüğünde, her bakmadan sonra).</summary>
    public void WindowsDurumunuYenile()
    {
        WindowsBildirimleri = _nobetci.Acik;
        WindowsAyarindaKapali = _nobetci.WindowsAyarindaKapali;
        WindowsDurumu = _nobetci.DurumMetni;
    }

    private void YoklamaDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BildirimYoklayici.SonSonuc))
            WindowsDurumunuYenile();
    }

    public static Uri KurulumAdresi(string? apiAdresi) => new(ApiAdresi.Coz(apiAdresi), "#notifications");
    protected override void OturumTemizle()
    {
        Bildirimler.Clear();
        Cihazlar.Clear();
        _surum = 0;
        Etkin = CihazBildirimiEtkin = false;
        CihazDurumu = "Cihaz bildirimlerinin durumu alınmadı.";
    }
}
```

Not: eski dosyadaki tek satırlık çok deyimli bloklar (`AyariYansit`, saat doğrulaması, `OturumTemizle`) çalışma kuralı gereği
satırlara açıldı; davranış aynıdır. `KaydetAsync`'te görev güncellemesi `Tamamlandi()`'dan önce yapılır: süre sınırlı schtasks
çağrısı sürerken ekran meşgul kalır.

- [ ] **Adım 4: Testlerin geçtiğini gör**

Çalıştır: Adım 2'deki komut.
Beklenen: `Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10` (BildirimTests 5 + BildirimEkraniWindowsTests 5).

- [ ] **Adım 5: Bildirimler sayfasını değiştir**

`Kasa.App/Views/BildirimPage.cs` dosyasının tamamını şununla değiştir:

```csharp
using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>Bildirimler ekranı (tasarım 2026-09-30 masaüstü bildirimleri §2): bu bilgisayarın Windows bildirimleri (anahtar, deneme,
/// durum satırı, Windows ayarı uyarısı; telefon için web kurulumu ikincil bağlantı), hatırlatma saati, hatırlatma listesi ve izin
/// verilmiş tarayıcı/telefon cihazları.</summary>
public sealed class BildirimPage : TakipSayfasi<BildirimViewModel>
{
    public BildirimPage(BildirimViewModel vm) : base(vm, "Bildirimler", "Kart kesimi, son ödeme ve kredi taksiti hatırlatmaları", vm.YukleAsync)
    {
        var telefon = TakipUi.Tikla("Telefon için web bildirim kurulumunu aç", TelefonKurulumunuAcAsync);
        telefon.Style = (Style)Application.Current!.Resources["BtnSecondary"];
        Govde.Add(TakipUi.Kart("Bu bilgisayarda Windows bildirimleri",
            TakipUi.Metin("Açıkken sunucudaki hatırlatmalar bu bilgisayarda Windows bildirimi olarak da görünür. Uygulama kapalıyken "
                + "her gün hatırlatma saatinden 5 dakika sonra ve Windows oturumu açılınca kontrol edilir. Bildirimi görmek onu okundu "
                + "yapmaz; bildirime tıklayınca ilgili kart ya da kredi açılır ve bildirim okundu işaretlenir."),
            TakipUi.Onay("Windows bildirimleri açık (yalnız bu bilgisayar)", nameof(vm.WindowsBildirimleri)),
            TakipUi.BagliMetin(nameof(vm.WindowsDurumu)),
            TakipUi.Goster(WindowsAyariUyarisi(), nameof(vm.WindowsAyarindaKapali)),
            TakipUi.Dugme("Deneme bildirimi göster", nameof(vm.DenemeGosterCommand)),
            TakipUi.Bagli(nameof(vm.CihazDurumu)),
            telefon));
        Govde.Add(TakipUi.Kart("Hatırlatma saati",
            TakipUi.Onay("Hatırlatmalar açık", nameof(vm.Etkin)),
            TakipUi.Alan("Saat (0–23, Türkiye saati)", TakipUi.Girdi(nameof(vm.Saat), sayi: true)),
            TakipUi.Alan("Dakika (0–59)", TakipUi.Girdi(nameof(vm.Dakika), sayi: true)),
            TakipUi.Dugme("Ayarları kaydet", nameof(vm.KaydetCommand))));
        Govde.Add(TakipUi.Kart("Hatırlatmalar", TakipUi.Liste<BildirimSatiri>(nameof(vm.Bildirimler), vm.OkunduAsync, "Okundu işaretle", x => !x.Veri.Okundu),
            TakipUi.Tikla("Kartları aç", () => Shell.Current.GoToAsync("//kartlar")), TakipUi.Tikla("Kredileri aç", () => Shell.Current.GoToAsync("//krediler"))));
        Govde.Add(TakipUi.Kart("İzin verilmiş cihazlar",
            TakipUi.Metin("Aynı bilgisayarda Edge ya da Chrome da bildirime kayıtlıysa her bildirim iki kez gelir. Tarayıcı kaydını bu listeden kapatabilirsiniz."),
            TakipUi.Liste<BildirimCihaziSatiri>(nameof(vm.Cihazlar), async c =>
            {
                if (await DisplayAlertAsync("Cihaz bildirimleri", $"{c.Veri.CihazAdi} için bildirimler kapatılsın mı?", "Kapat", "Vazgeç"))
                    await vm.CihaziKaldirAsync(c);
            }, "Bu cihazı kapat", x => x.Veri.Etkin)));
    }

    /// <summary>Sayfa her göründüğünde anahtar, durum satırı ve Windows ayarı yeniden okunur (kullanıcı Windows ayarını değiştirmiş
    /// olabilir).</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Vm.WindowsDurumunuYenile();
    }

    private static View WindowsAyariUyarisi()
    {
        var uyari = new VerticalStackLayout { Spacing = 8 };
        uyari.Add(new Label
        {
            Text = "Windows ayarlarında bu uygulamanın bildirimleri kapalı. Bildirim görmek için Windows bildirim ayarlarından açın.",
            Style = (Style)Application.Current!.Resources["LblTakipHata"],
        });
        uyari.Add(TakipUi.Tikla("Windows bildirim ayarlarını aç", async () =>
        {
            try
            {
                await Launcher.Default.OpenAsync(new Uri("ms-settings:notifications"));
            }
            catch (Exception)
            {
                // Ayarlar açılamazsa uyarı metni yolu gösterir.
            }
        }));
        return uyari;
    }

    private async Task TelefonKurulumunuAcAsync()
    {
        try
        {
            var adres = BildirimViewModel.KurulumAdresi(Environment.GetEnvironmentVariable("KASA_API_URL"));
            await Browser.Default.OpenAsync(adres, BrowserLaunchMode.External);
        }
        catch
        {
            await DisplayAlertAsync("Tarayıcı açılamadı", "Kasa'nın web sitesindeki Bildirimler sayfasını tarayıcıda açın.", "Tamam");
        }
    }
}
```

- [ ] **Adım 6: Derle ve denetle**

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
Beklenen: `Build succeeded.`, `0 Error(s)`.

Çalıştır: `bash .github/scripts/maui-lint.sh`
Beklenen: hata yok; `BtnSecondary` ve `LblTakipHata` Styles.xaml'da tanımlıdır, satırlar 200 karakteri geçmez.

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MauiKayitTutarliligiTests"`
Beklenen: `Failed:     0` (`Kayitli_viewmodel_bagimliliklari_kayitli`: `BildirimNobetcisi` Görev 6'da kayıtlı;
`Sayfalar_rolu_ekrana_atamaz_rolu_modelden_okur`: sayfa `AktifRol` okumaz).

- [ ] **Adım 7: Commit**

```bash
git add Kasa.App.Core/BildirimViewModel.cs Kasa.App/Views/BildirimPage.cs Kasa.App.Core.Tests/BildirimTests.cs Kasa.App.Core.Tests/BildirimEkraniWindowsTests.cs
git commit -F - <<'EOF'
feat(app): Bildirimler ekranında "Bu bilgisayarda Windows bildirimleri" kartı

Anahtar (görev kur/sil), deneme bildirimi, son kontrol durumu ve Windows ayarı
uyarısı; telefon kurulumu ikincil bağlantı. Saat kaydı görevi günceller, liste ve
okundu işareti menü rozetini günceller. Cihazlar kartına çift bildirim notu.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 8: Menü rozeti (okunmamış bildirim sayısı)

**Dosyalar:**
- Değiştir: `Kasa.App.Core/MenuModeli.cs` (`MenuOgesi` rozet özellikleri, `MenuModeli.RozetAyarla`)
- Değiştir: `Kasa.App/AppShell.xaml` (menü öğesi şablonu)
- Değiştir: `Kasa.App/AppShell.xaml.cs` (yoklayıcı → rozet)
- Test: `Kasa.App.Core.Tests/MenuRozetiTests.cs`, `Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.KabukRozeti.cs`,
  `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Kontrast.cs` (bir satır)

Rozet menü düzenini değiştirmez: şablonun sütunları aynı kalır, rozet başlık sütununun sağ ucunda durur (başlıklar kısa; en uzun
"Rapor dışa aktar" ve "Ekstre içe aktar" öğelerinde rozet yoktur). Zemin `BackgroundColor` ile yazılır.

- [ ] **Adım 1: Başarısız testleri yaz**

`Kasa.App.Core.Tests/MenuRozetiTests.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Menü rozeti (tasarım 2026-09-30 masaüstü bildirimleri §2): Bildirimler öğesinin yanında okunmamış sayısı; 0 iken görünmez,
/// 99'dan büyükse "99+"; ekran okuyucu öğeyi "Bildirimler, 3 okunmamış" diye okur. Menü yeniden kurulunca korunur, girişe dönüşte silinir.</summary>
public class MenuRozetiTests
{
    private static MenuOgesi Bildirimler(MenuModeli menu) => menu.Ogeler.Single(o => o.Bolum == Bolum.Bildirimler);

    [Fact]
    public void Rozet_sifirken_gorunmez_sayi_ve_erisim_adi_tasir()
    {
        var menu = new MenuModeli();
        menu.Goster(SekmeModeli.Bolumler(Rol.Editor));
        var oge = Bildirimler(menu);
        Assert.False(oge.RozetVar);
        Assert.Equal("Bildirimler", oge.ErisimAdi);
        menu.RozetAyarla(Bolum.Bildirimler, 3);
        Assert.True(oge.RozetVar);
        Assert.Equal("3", oge.RozetMetni);
        Assert.Equal("Bildirimler, 3 okunmamış", oge.ErisimAdi);
        menu.RozetAyarla(Bolum.Bildirimler, 120);
        Assert.Equal("99+", oge.RozetMetni);
        menu.RozetAyarla(Bolum.Bildirimler, -1);
        Assert.False(oge.RozetVar);
        Assert.All(menu.Ogeler.Where(o => o.Bolum != Bolum.Bildirimler), o => Assert.False(o.RozetVar));
    }

    [Fact]
    public void Rozet_degisince_bagli_ozellikler_bildirilir()
    {
        var oge = new MenuOgesi(Bolum.Bildirimler, "Bildirimler", MenuSimgeleri.Bildirimler, "bildirimler", _ => { });
        var degisen = new List<string?>();
        oge.PropertyChanged += (_, e) => degisen.Add(e.PropertyName);
        oge.Rozet = 2;
        Assert.Equal(["ErisimAdi", "Rozet", "RozetMetni", "RozetVar"], degisen.Order());
    }

    [Fact]
    public void Rozet_menu_yeniden_kurulunca_korunur_giriste_silinir()
    {
        var menu = new MenuModeli();
        menu.Goster(SekmeModeli.Bolumler(Rol.Editor));
        menu.RozetAyarla(Bolum.Bildirimler, 4);
        menu.Goster(SekmeModeli.Bolumler(Rol.Editor));
        Assert.Equal(4, Bildirimler(menu).Rozet);
        menu.Goster([]);
        menu.Goster(SekmeModeli.Bolumler(Rol.Editor));
        Assert.Equal(0, Bildirimler(menu).Rozet);
        // İzleyici menüsünde Bildirimler yok: rozet ayarı hata vermez, öğe eklenmez.
        menu.Goster(SekmeModeli.Bolumler(Rol.Izleyici));
        menu.RozetAyarla(Bolum.Bildirimler, 2);
        Assert.DoesNotContain(menu.Ogeler, o => o.Bolum == Bolum.Bildirimler);
    }
}
```

`Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.KabukRozeti.cs`:

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Kabuk menüsü öğe şablonundaki rozet (AppShell.xaml) gerçek uygulama kaynaklarıyla yüklenir: sayı varken görünür, zemini
/// SidebarAccent düz boyadır (Background fırçası yazılmaz), girdiyi geçirir; öğe düğmesi ekran okuyucuya rozetli adı verir.</summary>
public partial class GorunumEsdegerligiTests
{
    [Fact]
    public void Kabuk_menu_ogesi_rozeti_sayi_varken_gorunur_ve_dugme_erisim_adini_tasir()
    {
        var oge = new MenuOgesi(Bolum.Bildirimler, "Bildirimler", MenuSimgeleri.Bildirimler, "bildirimler", _ => { });
        var kok = MenuOgesiYukle(oge);
        var rozet = kok.Children.OfType<Border>().Single();
        Assert.False(rozet.IsVisible);
        oge.Rozet = 5;
        Assert.True(rozet.IsVisible);
        Assert.Equal("5", Assert.IsType<Label>(rozet.Content).Text);
        Assert.Equal(((Color)Application.Current!.Resources["SidebarAccent"]).ToArgbHex(true), rozet.BackgroundColor.ToArgbHex(true));
        Assert.True(Brush.IsNullOrEmpty(rozet.Background), "Rozet Background (Brush) yazmamalı (dotnet/maui#38813).");
        Assert.True(rozet.InputTransparent);
        Assert.Equal("Bildirimler, 5 okunmamış", SemanticProperties.GetDescription(kok.Children.OfType<Button>().Single()));
    }
}
```

`Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Kontrast.cs` içinde şu satırın:

```csharp
    [InlineData("SidebarMuted", "Sidebar")]
```

hemen altına ekle:

```csharp
    [InlineData("Sidebar", "SidebarAccent")]   // menü rozeti: koyu yazı, açık yeşil zemin
```

- [ ] **Adım 2: Testlerin başarısız olduğunu gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MenuRozetiTests|FullyQualifiedName~GorunumEsdegerligiTests|FullyQualifiedName~MauiKayitTutarliligiTests|FullyQualifiedName~MenuModeliTests"`
Beklenen: derleme hatası `CS1061: 'MenuOgesi' does not contain a definition for 'RozetVar'` (ve `RozetAyarla`, `Rozet`, `RozetMetni`,
`ErisimAdi`).

- [ ] **Adım 3: Menü modeline rozeti ekle**

`Kasa.App.Core/MenuModeli.cs`:

(a) En üste, `using CommunityToolkit.Mvvm.ComponentModel;` satırının üstüne ekle:

```csharp
using System.Globalization;
```

(b) `MenuOgesi` içinde `public string Rota { get; }` satırının altına ekle:

```csharp

    /// <summary>Sayı rozeti (Bildirimler: okunmamış bildirim sayısı; AppShell yazar); 0 iken görünmez.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RozetVar))]
    [NotifyPropertyChangedFor(nameof(RozetMetni))]
    [NotifyPropertyChangedFor(nameof(ErisimAdi))]
    private int _rozet;

    public bool RozetVar => Rozet > 0;

    /// <summary>Rozette yazan sayı; 99'dan büyükse "99+".</summary>
    public string RozetMetni => Rozet > 99 ? "99+" : Rozet.ToString(CultureInfo.InvariantCulture);

    /// <summary>Ekran okuyucunun okuduğu ad (öğe düğmesinin açıklaması): başlık; rozet varsa "Bildirimler, 3 okunmamış".</summary>
    public string ErisimAdi => Rozet > 0 ? $"{Baslik}, {RozetMetni} okunmamış" : Baslik;
```

(c) `MenuModeli` içinde `private string? _seciliRota;` satırının altına ekle:

```csharp
    /// <summary>Bölüm rozetleri: menü yeniden kurulunca (Goster) korunur, girişe dönüşte (boş bölüm listesi) silinir.</summary>
    private readonly Dictionary<Bolum, int> _rozetler = [];
```

(d) `Goster` yönteminin başını:

```csharp
    public void Goster(IReadOnlyCollection<Bolum> bolumler)
    {
        var gruplar = new List<MenuGrubu>();
```

şununla değiştir:

```csharp
    public void Goster(IReadOnlyCollection<Bolum> bolumler)
    {
        if (bolumler.Count == 0)
            _rozetler.Clear();
        var gruplar = new List<MenuGrubu>();
```

ve aynı yöntemde şu satırı:

```csharp
                .Select(o => new MenuOgesi(o.Bolum, o.Baslik, o.Simge, o.Rota, Sec)).ToList();
```

şununla değiştir:

```csharp
                .Select(o => new MenuOgesi(o.Bolum, o.Baslik, o.Simge, o.Rota, Sec) { Rozet = _rozetler.GetValueOrDefault(o.Bolum) }).ToList();
```

(e) `RotaSecildi` yönteminin belge yorumunun (`/// <summary>Shell'in yeni konumu (ShellNavigatedEventArgs.Current.Location)...`) hemen üstüne ekle:

```csharp
    /// <summary>Bölümün rozet sayısı (AppShell: Bildirimler için okunmamış bildirim sayısı); negatif sayı 0'dır. Bölüm menüde yoksa
    /// yalnız saklanır.</summary>
    public void RozetAyarla(Bolum bolum, int sayi)
    {
        _rozetler[bolum] = Math.Max(0, sayi);
        foreach (var oge in Ogeler.Where(o => o.Bolum == bolum))
            oge.Rozet = _rozetler[bolum];
    }

```

- [ ] **Adım 4: Menü şablonuna rozeti ekle**

`Kasa.App/AppShell.xaml` içinde `DataTemplate x:DataType="core:MenuOgesi"` şablonunda düğmenin açıklamasını:

```xml
                                                    SemanticProperties.Description="{Binding Baslik}">
```

şununla değiştir:

```xml
                                                    SemanticProperties.Description="{Binding ErisimAdi}">
```

ve başlık etiketinin (`<Label Grid.Column="1" Text="{Binding Baslik}" ... />`, `AutomationProperties.IsInAccessibleTree="False" />` ile
biten blok) hemen altına, `<Ellipse Grid.Column="2"` satırının üstüne ekle:

```xml
                                            <Border Grid.Column="1" IsVisible="{Binding RozetVar}" HorizontalOptions="End"
                                                    VerticalOptions="Center" Padding="7,1" StrokeThickness="0"
                                                    StrokeShape="RoundRectangle 9" BackgroundColor="{StaticResource SidebarAccent}"
                                                    InputTransparent="True" AutomationProperties.IsInAccessibleTree="False">
                                                <Label Text="{Binding RozetMetni}" FontSize="11" FontAttributes="Bold"
                                                       TextColor="{StaticResource Sidebar}"
                                                       AutomationProperties.IsInAccessibleTree="False" />
                                            </Border>
```

Dosyanın başındaki açıklama yorumuna (`Grup başlığı ekran okuyucuda düzey 2 başlıktır.` cümlesinden sonra) şu cümleyi ekle:
`Bildirimler öğesinin rozeti (okunmamış sayısı) başlık sütununun sağındadır; ekran okuyucu sayıyı düğme açıklamasından (ErisimAdi) okur.`

- [ ] **Adım 5: AppShell'de yoklayıcıyı rozete bağla**

`Kasa.App/AppShell.xaml.cs` kurucusunda (Görev 6'da eklenen) şu satırın:

```csharp
        _bildirimTiklamalari.Istendi += async (_, _) => await TiklamayiUygulaAsync();
```

hemen altına ekle:

```csharp
        // Menü rozeti: okunmamış bildirim sayısı (5 dakikalık bakma, Bildirimler ekranı, tıklama); çıkışta 0 olur ve gizlenir.
        _bildirimNobetcisi.Yoklayici.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BildirimYoklayici.Okunmamis))
                MainThread.BeginInvokeOnMainThread(() => _menuModeli.RozetAyarla(Bolum.Bildirimler, _bildirimNobetcisi.Yoklayici.Okunmamis));
        };
```

- [ ] **Adım 6: Testlerin geçtiğini gör**

Çalıştır: Adım 2'deki komut.
Beklenen: `Failed:     0`; yeni testler: `MenuRozetiTests` 3, `Kabuk_menu_ogesi_rozeti_sayi_varken_gorunur_ve_dugme_erisim_adini_tasir`
1, kontrast satırı 1 (kontrast ~7,7:1). Mevcut `Kabuk_menu_ogesi_zemini_durum_degisince_guncellenir_ve_dugme_basligi_tasir`
("Kartlar" açıklaması) rozet 0 iken `ErisimAdi == Baslik` olduğu için geçer; `Xaml_baglama_yollari_baglamin_gercek_turunde_var`
yeni bağlama yollarını (`RozetVar`, `RozetMetni`, `ErisimAdi`) `MenuOgesi`'nde bulur.

- [ ] **Adım 7: Derle ve denetle**

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
Beklenen: `Build succeeded.`, `0 Error(s)` (MAUIG2045 yok: bağlama yolları derlenmiş bağlamada çözülür).

Çalıştır: `bash .github/scripts/maui-lint.sh`
Beklenen: hata yok.

- [ ] **Adım 8: Commit**

```bash
git add Kasa.App.Core/MenuModeli.cs Kasa.App/AppShell.xaml Kasa.App/AppShell.xaml.cs Kasa.App.Core.Tests/MenuRozetiTests.cs Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.KabukRozeti.cs Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Kontrast.cs
git commit -F - <<'EOF'
feat(app): menüde okunmamış bildirim rozeti

Bildirimler öğesinin yanında okunmamış sayısı (0 iken gizli, 99+); ekran okuyucu
sayıyı öğe adıyla okur. Bakma, Bildirimler ekranı ve tıklama rozeti günceller.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 9: Krediler sayfasına `KrediId` sorgu parametresi

Bugün `KrediTakipPage` sorgu parametresi almıyor (`IQueryAttributable` yok); kredi bildirimine tıklayınca `//krediler?KrediId={id}`
açıldığında kredi seçilmez. Kartlar'daki desen (`KartTakipPage.ApplyQueryAttributes` + `OnAppearing` + `KartTakipViewModel.IdIleSec`)
aynen uygulanır.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/KrediTakipViewModel.cs` (`IdIleSec`)
- Değiştir: `Kasa.App/Views/KrediTakipPage.cs`
- Test: `Kasa.App.Core.Tests/KrediIdIleSecimTests.cs`

- [ ] **Adım 1: Başarısız testi yaz**

`Kasa.App.Core.Tests/KrediIdIleSecimTests.cs`:

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kredi bildirimine tıklanınca (//krediler?KrediId=2) Krediler sayfası o krediyi seçer; kredi yoksa sayfa hatası yazılır;
/// alıcı rolü krediye geçemez (Kartlar'daki IdIleSec ile aynı kural).</summary>
public class KrediIdIleSecimTests
{
    [Fact]
    public async Task Bildirimden_gelen_kredi_kimligi_krediyi_secer_bulunamazsa_hata_yazar()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new KrediTakipViewModel(new FinansTakipTests.Sahte(), finans, auth);
        await vm.YukleAsync();
        Assert.True(vm.IdIleSec(2));
        Assert.Equal(2, vm.Secili!.Id);
        Assert.NotEmpty(vm.Taksitler);
        Assert.False(vm.IdIleSec(99));
        Assert.Equal("Kredi bulunamadı. Listeyi yenileyip tekrar deneyin.", vm.Hata);
        auth.AktifRol = Rol.Alici;
        Assert.False(vm.IdIleSec(2));
    }
}
```

(`FinansTakipTests.Sahte` `internal sealed` iç içe sınıftır ve `OrnekKredi()` kimliği 2 olan tek krediyi döndürür.)

- [ ] **Adım 2: Testin başarısız olduğunu gör**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KrediIdIleSecimTests"`
Beklenen: derleme hatası `CS1061: 'KrediTakipViewModel' does not contain a definition for 'IdIleSec'`.

- [ ] **Adım 3: Ekran modeline `IdIleSec` ekle**

`Kasa.App.Core/KrediTakipViewModel.cs` içinde şu iki satırın:

```csharp
    [RelayCommand]
    private void Sec(KrediTakipSatiri satir)
```

hemen üstüne ekle:

```csharp
    /// <summary>Kimliği verilen krediyi seçer (bildirim tıklaması: //krediler?KrediId=…); kredi listede yoksa sayfa hatası yazılır.
    /// Alıcı rolü krediye geçemez (KartTakipViewModel.IdIleSec ile aynı kural).</summary>
    public bool IdIleSec(int id)
    {
        if (Auth.AktifRol == Rol.Alici)
            return false;
        var satir = Krediler.FirstOrDefault(k => k.Veri.Id == id);
        if (satir is null)
        {
            Hata = "Kredi bulunamadı. Listeyi yenileyip tekrar deneyin.";
            return false;
        }
        Sec(satir);
        return true;
    }

```

- [ ] **Adım 4: Testin geçtiğini gör**

Çalıştır: Adım 2'deki komut.
Beklenen: `Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1`

- [ ] **Adım 5: Sayfaya sorgu parametresini ekle**

`Kasa.App/Views/KrediTakipPage.cs` içinde:

(a) Sınıf bildirimini ve açılışını:

```csharp
public sealed class KrediTakipPage : TakipSayfasi<KrediTakipViewModel>
{
```

şununla değiştir:

```csharp
/// <summary>Krediler. Kredi bildirimine tıklanınca //krediler?KrediId={id} ile açılır: sayfa belirirken liste yüklenir, kredi seçilir
/// ve ayrıntısı görünür yere kaydırılır (KartTakipPage ile aynı desen).</summary>
public sealed class KrediTakipPage : TakipSayfasi<KrediTakipViewModel>, IQueryAttributable
{
    private readonly View _ozet;
    private int? _istenenKrediId;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("KrediId", out var value) && int.TryParse(value.ToString(), out var id) && id > 0)
            _istenenKrediId = id;
    }

    protected override async void OnAppearing()
    {
        if (_istenenKrediId is { } id)
        {
            await Vm.YukleAsync();
            if (Vm.VeriHazir && Vm.Hata is null && Vm.IdIleSec(id))
            {
                _istenenKrediId = null;
                await Kaydirici.ScrollToAsync(_ozet, ScrollToPosition.Start, true);
            }
        }
        base.OnAppearing();
    }

```

(b) Kurucuda şu satırı:

```csharp
        var ozet = Kart("Kredi ayrıntısı", BagliBuyuk(nameof(vm.KrediOzeti)));
```

şununla değiştir:

```csharp
        var ozet = Kart("Kredi ayrıntısı", BagliBuyuk(nameof(vm.KrediOzeti)));
        _ozet = ozet;
```

- [ ] **Adım 6: Derle**

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
Beklenen: `Build succeeded.`, `0 Error(s)`.

- [ ] **Adım 7: Commit**

```bash
git add Kasa.App.Core/KrediTakipViewModel.cs Kasa.App/Views/KrediTakipPage.cs Kasa.App.Core.Tests/KrediIdIleSecimTests.cs
git commit -F - <<'EOF'
feat(app): Krediler sayfası KrediId sorgu parametresiyle krediyi seçer

Kredi bildirimine tıklama //krediler?KrediId={id} açar; Kartlar'daki desen.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 10: Tam doğrulama

Uygulama çalıştırılmaz. Komutlar tek tek, sırayla.

- [ ] **Adım 1: Bütün birim testleri**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Failed:     0, Passed:   986` (başlangıç 910 + yeni 76: Görev 1: 16, Görev 2: 12, Görev 3: 14, Görev 4: 8, Görev 5: 15,
Görev 7: 5, Görev 8: 5, Görev 9: 1). Sayı farklıysa farkı test sınıflarına göre açıklayın; başarısız test varsa durun.

Çalıştır: `dotnet test Kasa.Sozlesme.Tests/Kasa.Sozlesme.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Failed:     0, Passed:    55` (sunucu ve sözleşme değişmedi).

- [ ] **Adım 2: Uygulama derlemesi**

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
Beklenen: `Build succeeded.`, `0 Error(s)`; bu dalda eklenen dosyalar için uyarı yok (çıktıda `warning` satırlarını
`Bildirim|PencereKilidi|AppShell|KrediTakip|MenuModeli` ile süzün: boş olmalı).

- [ ] **Adım 3: MAUI denetimi ve biçim**

Çalıştır: `bash .github/scripts/maui-lint.sh`
Beklenen: hata yok; "taban düşürülmeli" uyarısı çıkarsa `bash .github/scripts/maui-lint.sh --tabani-guncelle` ile taban küçültülür
ve commit'e eklenir.

Çalıştır: `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes`
Beklenen: çıktı yok, çıkış kodu 0. Fark gösterirse `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/'` çalıştırın
(gerekirse iki kez) ve farkı commit edin.

- [ ] **Adım 4: Kaçış ve kural taraması**

Çalıştır: `git diff origin/release/2.x --stat` ve
`git diff origin/release/2.x -- '*.cs' '*.xaml' | grep -nE '^\+.*(\\u00|\{[^}]*;[^}]*;[^}]*\})' | grep -v 'get;' | head`
Beklenen: `\u00..` kaçışı yok; tek satırda iki deyimli `{ a; b; }` blok yok (`{ get; set; }` özellikleri süzülür; ilke yalnız
eklenen satırlara uygulanır).

Çalıştır: `git diff origin/release/2.x -- Kasa.App | grep -nE '^\+.*(Background=|\.Background =|#[0-9A-Fa-f]{6})' | head`
Beklenen: çıktı yok.

- [ ] **Adım 5: Commit (gerekirse)**

Biçim ya da taban değiştiyse:

```bash
git add -A
git commit -F - <<'EOF'
chore: masaüstü bildirimleri biçim ve denetim düzeltmeleri

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

## Görev 11: Kullanıcı onaylı gerçek Windows bildirimi denemesi

Windows bildirimi her zaman kullanıcının ekranında çıkar; görünmeyen test masaüstünde denenemez. Bu görev **yalnız kullanıcının
sohbetteki açık onayıyla** yapılır ve her alt bölüm (A–H) öncesinde kullanıcıya ne olacağı söylenir. Komutlar PowerShell'dedir.
Kullanıcının oturum dosyası yedeklenip geri yüklenir; zamanlanmış görev ve geçici ortam değişkeni sonunda kaldırılır. Bir adım
beklenenden farklı sonuç verirse durun, sonucu kullanıcıya bildirin, kendiliğinden düzeltme denemeyin.

Değişkenler (her PowerShell çağrısında yeniden tanımlayın; kabuk durumu korunmaz):

```powershell
$W = 'C:\Users\burak\source\repos\Kasa-paket\masaustu-bildirim'
$D = 'C:\Users\burak\AppData\Local\Temp\claude\C--Users-burak-source-repos-Kasa\2cf5258e-ca1b-45f0-9c5a-07f7d4bbbb74\scratchpad\masaustu-bildirim-deneme'
$Eski = 'C:\Users\burak\AppData\Local\Temp\claude\C--Users-burak-source-repos-Kasa\2cf5258e-ca1b-45f0-9c5a-07f7d4bbbb74\scratchpad\menu-kartlar-gorsel'
$Depo = Join-Path $env:LOCALAPPDATA 'User Name\com.royalmezat.kasa\Settings\securestorage.dat'
$Yerel = Join-Path $env:LOCALAPPDATA 'EmarKasa'
```

- [ ] **Adım 1: Kullanıcıdan onay al**

Sohbette şunu sorun ve açık "evet" bekleyin: "Masaüstü bildirimlerini sizin ekranınızda denemek istiyorum: yerel test sunucusu
(127.0.0.1:5390) açılacak, Emar Kasa'nın deneme derlemesi açılıp test hesabıyla giriş yapacak, ekranınızda Windows bildirimleri
çıkacak ve bazılarına sizin tıklamanız gerekecek. Bu sırada `EmarKasaBildirim` zamanlanmış görevi kurulup sonunda silinecek,
`KASA_API_URL` kullanıcı ortam değişkeni birkaç dakika test sunucusunu gösterecek ve sonra eski haline dönecek, oturum dosyanız
(securestorage.dat) yedeklenip geri yüklenecek. Açık bir Emar Kasa penceresi varsa kapatmanız gerekir. Başlayayım mı?"

- [ ] **Adım 2: Ön koşullar ve yedekler**

```powershell
if (Get-Process Kasa.App -ErrorAction SilentlyContinue) { throw 'Açık Emar Kasa var; kullanıcıdan kapatmasını isteyin.' }
schtasks /Query /TN EmarKasaBildirim 2>$null; if ($LASTEXITCODE -eq 0) { throw 'EmarKasaBildirim zaten var; kullanıcıya sorun.' }
New-Item -ItemType Directory -Force $D | Out-Null
Copy-Item (Join-Path $Eski 'kimlik.json') $D -Force
Copy-Item (Join-Path $Eski 'sunucu.ps1') $D -Force
if (Test-Path $Depo) { Copy-Item $Depo (Join-Path $D 'securestorage.yedek') -Force; "oturum yedeği: $((Get-FileHash $Depo).Hash.Substring(0,12))" }
$onceki = @(if (Test-Path $Yerel) { Get-ChildItem $Yerel -File | ForEach-Object Name })
$onceki | Set-Content (Join-Path $D 'emarkasa-onceki.txt')
[Environment]::GetEnvironmentVariable('KASA_API_URL', 'User') | Set-Content (Join-Path $D 'kasa-api-url-onceki.txt')
"EmarKasa klasöründe önceden: $($onceki -join ', ')"
```

Beklenen: hata yok; oturum yedeği özeti yazılır (dosya yoksa yazılmaz). `kimlik.json` içeriği ekrana yazılmaz.

- [ ] **Adım 3: Derlemeler ve betikler**

Sırayla (tek dotnet komutu aynı anda):

```powershell
Set-Location $W
dotnet build Kasa.Ui.E2E/Sunucu/Kasa.Ui.E2E.Sunucu.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release --framework net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
New-Item -ItemType Directory -Force (Join-Path $D 'app') | Out-Null
Copy-Item -Recurse -Force "$W\Kasa.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\*" (Join-Path $D 'app')
$s = Get-Content (Join-Path $D 'sunucu.ps1') -Raw
$s = $s.Replace('C:\Users\burak\source\repos\Kasa-paket\menu-kartlar\', "$W\")
Set-Content -Encoding utf8 (Join-Path $D 'sunucu.ps1') $s
Select-String -Path (Join-Path $D 'sunucu.ps1') -Pattern 'Kasa.Ui.E2E.Sunucu.exe'
```

Beklenen: iki derleme `Build succeeded.`; son satır `masaustu-bildirim\Kasa.Ui.E2E\Sunucu\bin\Release\net10.0\Kasa.Ui.E2E.Sunucu.exe`
yolunu gösterir.

Şu iki betiği `$D` altına yazın (Write aracıyla):

`$D\tohum-bildirim.ps1`:

```powershell
# Yerel test sunucusuna (yalnız 127.0.0.1:5390) bugün taksiti olan bir kredi ve -Kart ile bugün kesim günü olan bir kart ekler;
# -Liste yalnız bugünün bildirimlerini yazar. Kimlik ekrana yazılmaz.
param([string]$Ad = 'Deneme kredisi', [switch]$Kart, [switch]$Liste)
$ErrorActionPreference = 'Stop'
$k = Get-Content (Join-Path $PSScriptRoot 'kimlik.json') -Raw | ConvertFrom-Json
$u = 'http://127.0.0.1:5390/api'
$giris = Invoke-RestMethod -Method Post "$u/auth/login" -ContentType 'application/json' -Body (@{ kullanici = $k.kullanici; sifre = $k.sifre } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($giris.token)" }
function Gonder($yontem, $yol, $govde) {
    Invoke-RestMethod -Method $yontem "$u/$yol" -Headers $h -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($govde | ConvertTo-Json -Depth 6)))
}
$bugun = (Get-Date).ToString('yyyy-MM-dd')
$ayar = Invoke-RestMethod "$u/ayarlar" -Headers $h
if (-not $ayar.takipBaslangic) { Gonder Put 'ayarlar' @{ takipBaslangic = '2026-08-01'; kasaAcilisDevri = 0; surum = $ayar.surum } | Out-Null }
if (-not $Liste) {
$kredi = Gonder Post 'takip/krediler' @{ istekId = [guid]::NewGuid().ToString(); ad = $Ad; cekilenTutar = 30000; cekimTarihi = '2026-08-01'; ilkTaksitTarihi = $bugun; taksitSayisi = 3; aylikOdeme = 10000; kanalIdleri = @(1); mevcutKredi = $true }
"kredi $($kredi.id) eklendi (ilk taksit $bugun)"
if ($Kart) {
    $gun = (Get-Date).Day
    $kart = Gonder Post 'takip/kartlar' @{ istekId = [guid]::NewGuid().ToString(); surum = 0; ad = 'Deneme kartı'; limit = 20000; kesimGunu = $gun; sonOdemeGunu = [Math]::Min($gun + 10, 28); acilisTarihi = '2026-08-01'; acilisBorc = 0; acilisDagilimlari = @() }
    "kart $($kart.id) eklendi (kesim günü $gun)"
}
}
Invoke-RestMethod "$u/bildirimler" -Headers $h | Where-Object { $_.tarih -eq $bugun } | ForEach-Object { "bildirim $($_.id) okundu=$($_.okundu) hedef=$($_.hedef) : $($_.baslik)" }
```

`$D\uia.ps1`:

```powershell
# Deneme derlemesinin penceresinde UI Otomasyonu: giriş (kimlik.json; ekrana yazılmaz), düğmeye basma, görünen adları listeleme.
param([Parameter(Mandatory)][int]$SurecId, [ValidateSet('giris', 'bas', 'adlar')][string]$Is = 'adlar', [string]$Ad)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$p = Get-Process -Id $SurecId
for ($i = 0; $i -lt 300 -and $p.MainWindowHandle -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 100; $p.Refresh() }
if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw 'pencere açılmadı' }
$kok = $AE::FromHandle($p.MainWindowHandle)
function Dugme([string]$ad) {
    $kosul = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $ad)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)))
    for ($i = 0; $i -lt 40; $i++) { $e = $kok.FindFirst($TS::Descendants, $kosul); if ($e) { return $e }; Start-Sleep -Milliseconds 250 }
    throw "düğme bulunamadı: $ad"
}
switch ($Is) {
    'giris' {
        $k = Get-Content (Join-Path $PSScriptRoot 'kimlik.json') -Raw | ConvertFrom-Json
        $kosul = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Edit)
        for ($i = 0; $i -lt 40; $i++) { $kutular = $kok.FindAll($TS::Descendants, $kosul); if ($kutular.Count -ge 2) { break }; Start-Sleep -Milliseconds 250 }
        $kutular[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($k.kullanici)
        $kutular[1].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($k.sifre)
        (Dugme 'Giriş').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        'giriş tıklandı'
    }
    'bas' { (Dugme $Ad).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "basıldı: $Ad" }
    'adlar' {
        $kok.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current } |
            Where-Object { $_.Name } | ForEach-Object { "$($_.ControlType.ProgrammaticName.Replace('ControlType.', '')): $($_.Name)" }
    }
}
```

- [ ] **Adım 4: Test sunucusu ve veri**

```powershell
& (Join-Path $D 'sunucu.ps1') -Is baslat -Bugun (Get-Date -Format 'yyyy-MM-dd')
& (Join-Path $D 'tohum-bildirim.ps1') -Kart
```

Beklenen: `sunucu hazır (PID …)`; `kredi … eklendi`, `kart … eklendi`; ve en az iki satır `bildirim <id> okundu=False hedef=/#loans/<id>`
ve `hedef=/#cards/<id>`. (Sunucu saati bugünün 12:00'sinde durur; bildirim saati varsayılan 09:00 olduğundan liste isteği
bildirimleri üretir; `Bildirim:WorkerEtkin` gerekmez.) Bugünün bildirimi çıkmazsa durun ve kullanıcıya bildirin.

- [ ] **Adım 5 (A): Uygulama açılır, giriş, ilk bakma ve rozet**

Kullanıcıya: "Şimdi Emar Kasa deneme derlemesi açılıp giriş yapacak; ekranınızda iki Windows bildirimi (kredi ve kart) çıkmalı."

```powershell
if (Test-Path $Depo) { Remove-Item $Depo -Force }
$env:KASA_API_URL = 'http://127.0.0.1:5390/'
$p = Start-Process -FilePath (Join-Path $D 'app\Kasa.App.exe') -WorkingDirectory (Join-Path $D 'app') -PassThru
$p.Id | Set-Content (Join-Path $D 'uygulama.pid')
& (Join-Path $D 'uia.ps1') -SurecId $p.Id -Is giris
Start-Sleep -Seconds 8
& (Join-Path $D 'uia.ps1') -SurecId $p.Id -Is adlar | Select-String 'Bildirimler'
schtasks /Query /TN EmarKasaBildirim /XML
```

Beklenen: `Button: Bildirimler, 2 okunmamış`; görev XML'inde `<StartBoundary>2026-01-01T09:05:00</StartBoundary>`,
`<LogonTrigger>` içinde `<UserId>` bu kullanıcı (`ETKİALANI\burak` biçiminde), `<LogonType>InteractiveToken</LogonType>`,
`<Command>` deneme derlemesinin exe yolu, `<Arguments>--bildirim-kontrol</Arguments>`. Görev yoksa (sorgu hata verirse) schtasks
yönetici istemiş olabilir: durun ve bildirin. Kullanıcıya iki bildirimi görüp görmediğini sorun.

- [ ] **Adım 6 (B): Bildirime tıklama (uygulama açık)**

Kullanıcıdan **kart** bildirimine (ya da Bildirim Merkezi'ndeki kopyasına) tıklamasını isteyin. Sonra:

```powershell
$p = Get-Process -Id ([int](Get-Content (Join-Path $D 'uygulama.pid')))
& (Join-Path $D 'uia.ps1') -SurecId $p.Id -Is adlar | Select-String 'Bildirimler|Deneme kartı|Kartlar'
& (Join-Path $D 'tohum-bildirim.ps1') -Liste | Select-String 'hedef=/#cards'
```

Beklenen: kart bildirimi
`okundu=True`; menüde `Button: Bildirimler, 1 okunmamış`; pencere öne gelmiş ve Kartlar sayfasında "Deneme kartı" açık (kullanıcı
da doğrular).

- [ ] **Adım 7 (C): Deneme bildirimi ve durum satırı**

```powershell
$p = Get-Process -Id ([int](Get-Content (Join-Path $D 'uygulama.pid')))
& (Join-Path $D 'uia.ps1') -SurecId $p.Id -Is bas -Ad 'Bildirimler, 1 okunmamış'
Start-Sleep -Seconds 3
& (Join-Path $D 'uia.ps1') -SurecId $p.Id -Is bas -Ad 'Deneme bildirimi göster'
Start-Sleep -Seconds 2
& (Join-Path $D 'uia.ps1') -SurecId $p.Id -Is adlar | Select-String 'Son kontrol|Deneme bildirimi|Windows ayarlarında'
```

Beklenen: `Son kontrol HH:mm · …` satırı ve `Deneme bildirimi gösterildi.` iletisi; "Windows ayarlarında … kapalı" görünmez.
Kullanıcıya "Emar Kasa deneme bildirimi" başlıklı bildirimi görüp görmediğini sorun. (Menü öğesinin adı rozet sayısına göre
değişir; `adlar` çıktısındaki tam adı kullanın.)

- [ ] **Adım 8 (D): Uygulama kapalıyken pencere açmadan kontrol**

```powershell
$p = Get-Process -Id ([int](Get-Content (Join-Path $D 'uygulama.pid')))
$p.CloseMainWindow() | Out-Null
$p.WaitForExit(15000) | Out-Null
"uygulama kapandı: $($p.HasExited)"
& (Join-Path $D 'tohum-bildirim.ps1') -Ad 'Görev kredisi'
$env:KASA_API_URL = 'http://127.0.0.1:5390/'
$sure = [Diagnostics.Stopwatch]::StartNew()
$g = Start-Process -FilePath (Join-Path $D 'app\Kasa.App.exe') -ArgumentList '--bildirim-kontrol' -WorkingDirectory (Join-Path $D 'app') -PassThru
$g.WaitForExit(90000) | Out-Null
"görev süreci bitti: $($g.HasExited), süre $([int]$sure.Elapsed.TotalSeconds) sn, pencere: $($g.MainWindowHandle)"
```

Beklenen: `uygulama kapandı: True`; görev süreci 60 sn içinde biter, pencere tanıtıcısı `0`; kullanıcının ekranında "Görev kredisi"
bildirimi çıkar (kullanıcıya sorun).

- [ ] **Adım 9 (E): Zamanlanmış görevin kendisi ve kapalı uygulamada tıklama**

Kullanıcıya: "`KASA_API_URL` kullanıcı ortam değişkenini birkaç dakikalığına test sunucusuna çeviriyorum ki görev üretim sunucusuna
gitmesin; sonunda eski haline döner."

```powershell
[Environment]::SetEnvironmentVariable('KASA_API_URL', 'http://127.0.0.1:5390/', 'User')
& (Join-Path $D 'tohum-bildirim.ps1') -Ad 'Zamanlanmış görev kredisi'
schtasks /Run /TN EmarKasaBildirim
Start-Sleep -Seconds 30
schtasks /Query /TN EmarKasaBildirim /V /FO LIST | Select-String 'Last Result|Son Sonuç|Status|Durum'
```

Beklenen: `SUCCESS` / `BAŞARILI`; son sonuç `0`; kullanıcının ekranında "Zamanlanmış görev kredisi" bildirimi. Bildirim çıkmadıysa
(görev ortam değişkenini almamış olabilir) son sonucu ve bunu kullanıcıya bildirin; Adım 8 aynı kod yolunu zaten doğruladı.

Sonra kullanıcıdan uygulama **kapalıyken** "Zamanlanmış görev kredisi" (yoksa "Görev kredisi") bildirimine tıklamasını isteyin:

```powershell
Start-Sleep -Seconds 15
$a = Get-Process Kasa.App -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
"açılan süreç: $($a.Id)"
$a.Id | Set-Content (Join-Path $D 'uygulama.pid')
& (Join-Path $D 'uia.ps1') -SurecId $a.Id -Is adlar | Select-String 'Kredi ayrıntısı|kredisi|Krediler'
& (Join-Path $D 'tohum-bildirim.ps1') -Liste | Select-String 'okundu=True'
```

Beklenen: tıklama uygulamayı başlatır, kayıtlı oturumla girer ve Krediler sayfasında tıklanan krediyi açar ("Kredi ayrıntısı" kartı
görünür); sunucuda o bildirim `okundu=True`. Uygulama giriş ekranında kalırsa (tıklamayla başlayan süreç test sunucusunu değil
üretimi görmüş olabilir) bunu kullanıcıya bildirin.

- [ ] **Adım 10 (F): Temizlik ve geri yükleme**

```powershell
Get-Process Kasa.App -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null; $_.WaitForExit(15000) | Out-Null }
schtasks /Delete /TN EmarKasaBildirim /F
$url = Get-Content (Join-Path $D 'kasa-api-url-onceki.txt') -ErrorAction SilentlyContinue
[Environment]::SetEnvironmentVariable('KASA_API_URL', $(if ($url) { $url } else { $null }), 'User')
& (Join-Path $D 'sunucu.ps1') -Is durdur
$yedek = Join-Path $D 'securestorage.yedek'
if (Test-Path $yedek) { Copy-Item $yedek $Depo -Force; "oturum geri yüklendi: $((Get-FileHash $Depo).Hash.Substring(0,12))" } elseif (Test-Path $Depo) { Remove-Item $Depo -Force }
$onceki = @(Get-Content (Join-Path $D 'emarkasa-onceki.txt') -ErrorAction SilentlyContinue)
$adlar = @('gosterilen-bildirimler.json', 'bildirim-ayari.json', 'bildirim-ayari.json.yeni', 'bildirim-gorevi.txt')
$adlar += @(Get-ChildItem $Yerel -Filter 'bildirim-gorevi-*.xml' -ErrorAction SilentlyContinue | ForEach-Object Name)
foreach ($ad in $adlar) {
    if ($onceki -notcontains $ad -and (Test-Path (Join-Path $Yerel $ad))) { Remove-Item (Join-Path $Yerel $ad) -Force; "silindi: $ad" }
}
schtasks /Query /TN EmarKasaBildirim 2>$null; "görev sorgusu çıkış kodu: $LASTEXITCODE"
"KASA_API_URL (User): [$([Environment]::GetEnvironmentVariable('KASA_API_URL', 'User'))]"
"Kasa.App süreçleri: $((Get-Process Kasa.App -ErrorAction SilentlyContinue | Measure-Object).Count)"
```

Beklenen: `SUCCESS` (görev silindi); `sunucu durdu; 5390 dinleyen: 0`; oturum özeti Adım 2'deki özetle aynı; görev sorgusu çıkış kodu
`1`; `KASA_API_URL` önceki değerine döndü (önceden yoksa boş); Kasa.App süreci 0. Sonuçları (A–F, her biri geçti/kaldı ve
kullanıcının gözlemi) kullanıcıya özetleyin. Kod değişikliği gerekmedikçe commit yoktur.

---

## Tasarım → görev eşlemesi (öz inceleme)

| Tasarım maddesi (`2026-09-30-masaustu-bildirimleri.md`) | Görev |
|---|---|
| §1 Kaynak: `GET /api/bildirimler`, yalnız okunmamış (iptal: istemcide alan yok, karar 9) | 3 (`BildirimYoklayici.YoklaAsync`) |
| §1 Rol: yalnız editör oturumu | 3 (`editorOturumu`), 5 (`BildirimNobetcisi.EditorOturumu`, `BildirimKontrolu` /me) |
| §1 Bütün türler gösterilir (kart, son ödeme, kredi, kasa alt sınırı, hata) | 3 (türe göre süzme yok), 1 (`/#home` → Bildirimler) |
| §1 Açılışta ve editör girişinde bir kez bakılır | 5 (`OturumAcildiAsync`), 6 (`AppShell.AcilisaGitAsync`) |
| §1 5 dakikada bir bakılır; çıkışta, editör değilken, ayar kapalıyken bakılmaz | 5 (`Aralik`, `TikAsync`, `OturumDegisti`), 6 (zamanlayıcı başlat/durdur) |
| §1 Kapalıyken `EmarKasaBildirim` görevi `--bildirim-kontrol`; günlük saat + 5 dk ve oturum açılışı | 1 (`BildirimGorevZamani`), 4 (`BildirimGorevi`), 6 (`App.xaml.cs`) |
| §1 Kullanıcı düzeyi, yönetici yok, `schtasks`, pencere yok, hatalar yutulur | 4 (XML, InteractiveToken, LeastPrivilege; karar 5-6) |
| §1 Pencere açmadan: belirteçle liste, göster, çık; en fazla 60 sn; sessiz çıkış durumları | 5 (`BildirimKontrolu`), 6 (`BildirimKontroluAsync`, `PencereKilidi`) |
| §1 Eski `EmarKasaHatirlatici` silme kodu aynen kalır | 6 (Adım 3: `--hatirlatma-kontrol` yolu ve `TemizleAsync` korunur) |
| §1 Tekrar göstermeme: `%LOCALAPPDATA%\EmarKasa\gosterilen-bildirimler.json`, 500, kilit, bozuk dosya | 2 (`DosyaGosterilenBildirimDeposu`) |
| §1 Gösterim: AppNotificationManager, paketsiz `Register()`, başlık/metin, argüman kimlik + hedef | 6 (`WindowsBildirimGosterici`; karar 1) |
| §1 Tıklama: uygulama açılır/başlar, kart/kredi/tanınmayan rota, okundu işareti | 1 (`BildirimHedefi`), 3 (`TiklandiAsync`, `BildirimTiklamalari`), 6 (etkinleştirme, `TiklamayiUygulaAsync`), 9 (`KrediId`) |
| §1 Okundu kuralı: göstermek okundu yapmaz | 3 (yalnız `TiklandiAsync` okundu işaretler) |
| §2 "Bu bilgisayarda Windows bildirimleri" kartı: anahtar (bilgisayara özel, editörde varsayılan açık) | 2 (`DosyaBildirimAyari`; karar 7), 5 (`AcikAyarlaAsync`), 7 |
| §2 Açınca görev kurulur ve bakma başlar, kapatınca görev silinir ve bakma durur | 5, 7 |
| §2 Deneme bildirimi (sunucuya gitmeden) | 5 (`DenemeGoster`), 6 (`WindowsBildirimGosterici.DenemeGoster`), 7 |
| §2 Durum satırı ("Son kontrol 14:05 · 2 yeni bildirim" / "sunucuya ulaşılamadı") | 3 (`YoklamaSonucu.Metin`), 5 (`DurumMetni`), 7 |
| §2 Windows ayarında kapalı uyarısı + `ms-settings:notifications` | 6 (`WindowsAyarindaKapali`; karar 4), 7 (sayfa) |
| §2 Telefon: web kurulumu ikincil bağlantı | 7 |
| §2 Hatırlatma saati kaydedilince görev saati güncellenir; açılışta sunucu saati okunur | 5 (`SaatDegistiAsync`, `GoreviSunucuSaatineGoreGuncelleAsync`), 7 (`KaydetAsync`) |
| §2 Hatırlatma listesi ve izin verilmiş cihazlar aynen; çift bildirim notu | 7 |
| §2 Menü rozeti: `MenuOgesi` özelliği, 5 dk bakma ve okundu ile güncellenir, 0 iken gizli | 3 (`Okunmamis`), 7 (`OkunmamisBildir`), 8 |
| §2 Oturum ve rol: çıkışta/editör değilken bakma durur, rozet gizlenir; görev ayar açıkken kalır | 5 (`OturumDegisti`), 6 (`GiriseDonAsync` zamanlayıcıyı durdurur) |
| Yapı: `BildirimYoklayici` bağımlılıkları (`IKasaApi` yerine bildirim uçlarının arayüzü `IBildirimApi`) | 3 |
| Test listesi (yoklayıcı, görev zamanı, hedef, görev komutları, görünüm modeli, rozet) | 1, 2, 3, 4, 5, 7, 8 |
| Gerçek Windows bildirimi: deneme, test sunucusu bildirimine tıklama, kapalı uygulamada görev, görev silme, oturum dosyası yedeği | 11 |
| Kapsam dışı: sunucu, tepsi uygulaması, web/mobil, tür seçimi | dokunulmaz |

**Yer tutucu taraması:** "TBD", "TODO", "uygun hata işleme", "Görev N'e benzer" yok; her kod adımında tam kod, her komutta beklenen
çıktı var.

**Ad tutarlılığı:** `BildirimHedefi.Rota`, `BildirimGorevZamani.Hesapla`, `YerelKlasor.Yol`, `IGosterilenBildirimDeposu.YenileriAyir`,
`DosyaGosterilenBildirimDeposu.Kayitlilar`, `IBildirimAyari.Acik`, `IBildirimGosterici.Goster/DenemeGoster/WindowsAyarindaKapali`,
`BildirimTiklamasi(BildirimId, Hedef).Coz`, `BildirimTiklamalari.Ekle/Al/Istendi`, `YoklamaSonucu(Zaman, Durum, YeniSayisi).Metin`,
`BildirimYoklayici.YoklaAsync/TiklandiAsync/OkunmamisBildir/Sifirla/Okunmamis/SonSonuc`, `IBildirimGorevi.GuncelleAsync/SilAsync`,
`BildirimGorevi.GorevAdi/KontrolArgumani`, `BildirimNobetcisi.Aralik/OturumAcildiAsync/TikAsync/AcikAyarlaAsync/SaatDegistiAsync/
DenemeGoster/TiklamayiIsleAsync/DurumMetni`, `BildirimKontrolu.KontrolModu/CalistirAsync/EnUzunSure`, `PencereKilidi.Al/AcikMi`,
`WindowsBildirimGosterici.Ortak/Baslat/Bitir/Tiklamalar`, `MenuOgesi.Rozet/RozetVar/RozetMetni/ErisimAdi`,
`MenuModeli.RozetAyarla`, `KrediTakipViewModel.IdIleSec`, `BildirimViewModel.WindowsBildirimleri/WindowsDurumu/
WindowsAyarindaKapali/DenemeGosterCommand/WindowsDurumunuYenile` görevler arasında aynı yazılır.

## Kullanıcıya sorulacaklar (uygulamadan önce ya da Görev 11'de)

1. **Ayar dosyası:** Tasarım ayarı MAUI `Preferences`'ta istiyordu; plan aynı klasörde küçük JSON kullanıyor (karar 7). Onay?
2. **İptal edilmiş bildirim:** `BildirimDto`'da `Iptal` yok; iptal edilmiş ama bir tarayıcıya gönderilmiş bildirim masaüstünde bir kez
   görünebilir (karar 9). Sunucuya `Iptal` alanı eklemek kapsam dışı; kabul mü, ayrı iş mi?
3. **Rozet ayar kapalıyken:** plan rozeti ayar kapanınca gizliyor (karar 11). Rozet ayardan bağımsız kalsın mı?
4. **Bugünle sınırlama:** yalnız bugünün okunmamışları gösteriliyor (bilgisayarın yerel günü; sunucu İstanbul günü). Bilgisayar
   kapalı geçen günün okunmamışları Windows bildirimi olarak gelmez (listede ve rozette görünür). Uygun mu?
5. **Görev 11 adımları:** `KASA_API_URL` kullanıcı ortam değişkeninin birkaç dakika test sunucusuna çevrilmesi ve kullanıcının
   bildirimlere tıklaması gerekiyor; onay Görev 11 Adım 1'de alınır.
