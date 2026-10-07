# Emar Kasa — iOS bulut derlemesi ve kişisel TestFlight dağıtımı

> Bu akış kaynak kod ve yayın araçlarını hazırlar. Bir iOS derlemesi geçmeden,
> Apple yüklemeyi işleyip gerçek iPhone'da denenmeden “yayınlandı / kullanıma hazır” denmez.
> Kaynak denetimi tarihi: 7 Ekim 2026.

## Ürünler ve araç eşlemesi

Windows masaüstü uygulaması korunur; macOS üzerinde aynı MAUI projesi `net10.0-ios` hedefini derler.
Ürün sürümü [Directory.Build.props](../../Directory.Build.props) içindeki `KasaSurumu`,
alt derleme sınırı [Kasa.App.csproj](../../Kasa.App/Kasa.App.csproj) içindeki `ApplicationVersion` değeridir.
Bu yayın akışı ürün sürümünü değiştirmez. IPA için verilen derleme numarası yalnız o yayının
`ApplicationVersion` değerini değiştirir.

| Alan | Sabit / kaynak |
| --- | --- |
| Bundle ID | `com.royalmezat.kasa` |
| MAUI | `10.0.110` |
| .NET SDK | `10.0.401` |
| Workload set | `10.0.401.1` |
| Xcode | Kararlı `27.0`; beta kullanılmaz |
| GitHub Mac runner | `xcode-27`, arm64 |
| Fiziksel cihaz RID | `ios-arm64` |
| API | `https://kasa.emarglobal.com/` |

[Microsoft'un .NET 10 / Xcode 27 sürümü](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.0-10722)
bu SDK/workload eşlemesini verir ve MAUI 10.0.110+ önerir. Paket sürümü yükseltmek gerekmez.
Xcode 27.0 için macOS 26.6+ gerekir; Sequoia yüklü eski Mac bu yeni yerel araç zincirini
çalıştıramıyorsa derleme [GitHub'ın xcode-27 runner'ında](https://github.com/actions/runner-images/blob/main/images/macos/xcode-27-arm64-Readme.md)
yapılır. Bu runner görüntüsü GitHub'da halen public preview olarak etiketlidir; içinde kararlı 27.0 vardır.
[apple-toolchain.sh](../../.github/scripts/apple-toolchain.sh) tam Xcode yolu ve SDK sürümünü denetler;
uygun araç kaldırılmışsa başka sürüme sessizce geçmez, durur.

## 1. Apple hesabı olmadan iOS derlemesini doğrula

[iOS unsigned validation](../../.github/workflows/ios-validation.yml) push ve pull request'te gerçek
`Release / net10.0-ios / ios-arm64` uygulamasını derler. İmzalama ve provisioning kapalıdır;
Apple özel anahtarı kullanmaz, IPA yüklemez.

Bu kapı C#/XAML, iOS API, native bağlama ve Release derleme hatalarını yakalar.
Cihazda açılışı, klavyeyi, dosya seçiciyi, SecureStorage'ı, imzalamayı veya sunucu oturumunu doğrulamaz.
Release yayını varsayılan yönetilen iOS çalışma zamanı/trim ayarlarını kullanır; NativeAOT veya
`TrimMode=full` zorlanmaz. API istemcisinin reflection kullanan JSON yolları cihaz testinde de denenmeli.

Manuel iş akışlarını çalıştırmak için dosyalar deponun varsayılan dalında bulunmalı.
Bu depoda varsayılan dal `release/2.x`; yalnız özellik dalına eklemek Run workflow düğmesini
hazırlamaya yetmez. [GitHub manuel iş akışı kuralları](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)
bu gereksinimi ve dal seçimini açıklar.

## 2. Windows'ta dağıtım özel anahtarı ve CSR oluştur

Geçerli Apple Distribution sertifikasının özel anahtarı/P12 dosyası zaten varsa onu kullan.
Yeni sertifika gereken durumda Git for Windows ile gelen **Git Bash / OpenSSL** yeterlidir.
İmzalama dosyalarını depo dışında, örneğin Belgeler içindeki `Kasa-signing` klasöründe tut.
Aşağıdaki işlemler bu klasörde yapılır:

```bash
openssl genpkey -algorithm RSA -aes-256-cbc -pkeyopt rsa_keygen_bits:2048 -out distribution.key
openssl req -new -key distribution.key -out distribution.certSigningRequest
```

İlk komut özel anahtar için parola ister. İkinci komutta Apple hesabındaki gerçek ad/e-posta
bilgilerini kullan; Common Name ve Email Address sorularını doldur. Özel anahtarın parolası
komut satırına yazılmaz. Apple'a **yalnız CSR** yüklenir; `distribution.key` gönderilmez.

[Apple Developer — Certificates, Identifiers & Profiles](https://developer.apple.com/account/resources/certificates/list)
sayfasında:

1. **Identifiers** altında explicit App ID kaydet: `com.royalmezat.kasa`.
   Başka uygulamanın Bundle ID'sini değiştirme; mevcut doğru App ID varsa onu kullan.
2. **Certificates → + → Apple Distribution** seç. CSR dosyasını yükle ve sertifikayı indir.
   İndirilen DER `.cer` dosyasını bu klasörde `distribution.cer` olarak tut.
3. Sertifikayı CSR ile oluşan özel anahtarla, parola korumalı P12'ye dönüştür:

```bash
openssl x509 -inform DER -in distribution.cer -out distribution.pem
openssl pkcs12 -export -inkey distribution.key -in distribution.pem -out distribution.p12
```

Son komut önce özel anahtar parolasını, sonra **P12 export parolasını** ister.
CI'a `distribution.p12` ve onun export parolası verilir. CSR'yi imzalayan özel anahtar
olmayan bir `.cer` dosyası tek başına uygulamayı imzalayamaz.
Apple'ın [CSR açıklaması](https://developer.apple.com/help/account/certificates/create-a-certificate-signing-request)
sertifika isteği ile özel anahtarın ilişkisini açıklar; burada aynı CSR standardı OpenSSL ile üretilir.

## 3. App Store profili ve uygulama kaydı

Apple Developer portalında **Profiles → + → Distribution → App Store Connect**:

1. `com.royalmezat.kasa` App ID'sini seç.
2. Yukarıdaki Apple Distribution sertifikasını seç.
3. Profil adını ver, oluştur ve `.mobileprovision` dosyasını indir.

Profil Development, Ad Hoc veya Enterprise türünde olmamalı. Betik süreyi, tam Bundle ID'yi,
takımı ve P12'nin profildeki dağıtım sertifikasıyla eşleşmesini kontrol eder.
[Apple App Store profil adımları](https://developer.apple.com/help/account/provisioning-profiles/create-an-app-store-provisioning-profile)
tek dağıtım sertifikalı profili tarif eder.

[App Store Connect](https://appstoreconnect.apple.com/) → **My Apps → + → New App** ile
aynı Bundle ID'ye bağlı iOS uygulama kaydını oluştur. Ad Emar Kasa olabilir;
SKU hesabında benzersiz olmalı. Oluşturulmuş doğru kayıt varsa yenisini açma.

## 4. App Store Connect takım API anahtarı

App Store Connect → **Users and Access → Integrations → App Store Connect API → Team Keys**
altından takım API anahtarı oluştur. Hesap ilk kullanımda API erişimi talebi gösterebilir.
Yükleme için yetkili bir rol seç; Developer rolünün bu hesaptaki build yükleme yetkisini
[rol izinleri](https://developer.apple.com/help/app-store-connect/reference/role-permissions/)
üzerinden doğrula.

Key ID, Issuer ID ve indirilen `AuthKey_....p8` dosyası gerekir.
Workflow takım API anahtarı kullanır; individual key için Issuer ID varsayımı yapılmaz.
Apple özel anahtarı yalnız bir kez indirilebilir; güvenli yedeğini al.
[Apple API anahtarı rehberi](https://developer.apple.com/help/app-store-connect/get-started/app-store-connect-api)
takım anahtarı oluşturma ve indirme adımlarını açıklar.

## 5. GitHub secrets

GitHub deposunda **Settings → Environments → New environment → ios-release** oluştur.
İmzalama secrets'larını bu ortamda sakla. **Deployment branches and tags** bölümünde
**Selected branches and tags** seçip yalnız **branch: release/2.x** kuralını ekle.
Workflow imzalı seçenekleri, secrets'a erişmeyen ilk işte aynı dal için denetler;
GitHub ortam kuralı da credentials erişimini yayın dalıyla sınırlar.
Betik özel anahtarları depoya yazmaz.

| Secret adı | İçerik |
| --- | --- |
| `IOS_DISTRIBUTION_P12_BASE64` | `distribution.p12` dosyasının Base64'ü |
| `IOS_DISTRIBUTION_P12_PASSWORD` | P12 export parolası |
| `IOS_APPSTORE_PROFILE_BASE64` | App Store `.mobileprovision` dosyasının Base64'ü |
| `APP_STORE_CONNECT_KEY_ID` | Takım API Key ID |
| `APP_STORE_CONNECT_ISSUER_ID` | Takım API Issuer ID |
| `APP_STORE_CONNECT_PRIVATE_KEY_BASE64` | İndirilen `.p8` dosyasının Base64'ü |

İlk üç değer `build-ipa` için yeterlidir. Son üç yalnız Apple yüklemesinde gerekir.
Base64 şifreleme değildir; bunları normal Variables yerine **Secrets** olarak ekle.

GitHub CLI ile dosya içeriğini konsola dökmeden göndermek için PowerShell'de:

```powershell
function Set-KasaFileSecret([string]$Name, [string]$Path) {
    $taskBytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path)
    [Convert]::ToBase64String($taskBytes) | gh secret set $Name --repo Ulysses07/EmarKasa --env ios-release
    if ($LASTEXITCODE -ne 0) { throw 'Secret yüklenemedi.' }
}
# İmzalama klasöründe çalıştır; dosya adlarını indirdiğin dosyaya göre seç.
Set-KasaFileSecret IOS_DISTRIBUTION_P12_BASE64 .\distribution.p12
Set-KasaFileSecret IOS_APPSTORE_PROFILE_BASE64 .\distribution.mobileprovision
# API .p8 dosyasının gerçek adını kullan.
# Set-KasaFileSecret APP_STORE_CONNECT_PRIVATE_KEY_BASE64 .\AuthKey_....p8

$taskPassword = Read-Host 'P12 export parolası' -AsSecureString
[System.Net.NetworkCredential]::new('', $taskPassword).Password | gh secret set IOS_DISTRIBUTION_P12_PASSWORD --repo Ulysses07/EmarKasa --env ios-release
```

Bu komutlar hedef depoyu açıkça belirtir; imzalama klasörünün bir Git deposunda olması gerekmez.
Key ID/Issuer ID GitHub'ın secret ekranından eklenebilir. Özel anahtar veya parola sohbet mesajına,
issue'ya, workflow input'una, commit'e ya da ekran görüntüsüne konmaz.

## 6. Manuel IPA veya TestFlight yüklemesi

GitHub **Actions → iOS TestFlight release → Run workflow**; imzalı seçenekler için `release/2.x` dalını seç:

- **compile-only** (varsayılan): yalnız secrets gerektirmeyen iOS derlemesi.
- **build-ipa**: imzalı IPA, SHA-256 ve açık build metadata'sı üretir; Apple'a göndermez.
- **upload-testflight**: aynı IPA'yı `altool --validate-app` ile doğrular ve yükler;
  ayrıca **confirm_upload** işaretlenmeli.

İmzalı seçeneklerde App Store Connect'te aynı ürün sürümü için daha önce kabul edilmiş numaradan
büyük `build_number` yaz. Betik proje numarasının altındaki değeri reddeder; Apple'daki geçmişi
kendiliğinden sorgulamaz. İlk yayında geçerli proje numarası kullanılabilir.
Yükleme sonrası tekrar denenecek yeni IPA için numarayı artır.

[iOS release workflow](../../.github/workflows/release-ios.yml) geçici keychain oluşturur.
[Apple release betiği](../../.github/scripts/apple-release.py) secrets'ları yalnız runner'ın geçici
dizininde açar, API `.p8` dosyasını yalnız yükleme adımında kurar ve `always()` temizliğinde
keychain/profil/özel anahtarı kaldırır. Artifact yalnız IPA, checksum ve build.json içerir.
GitHub-hosted runner ayrıca iş bitince silinir.
[GitHub imzalama rehberi](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications)
geçici keychain ve temizleme yaklaşımını açıklar.

IPA üretiminde `ArchiveOnBuild=true`, `BuildIpa=true`, `ios-arm64` ve geçerli App Store
imzası kullanılır. [Microsoft publish CLI](https://learn.microsoft.com/en-us/dotnet/maui/ios/deployment/publish-cli?view=net-maui-10.0)
bu yayın parametrelerini açıklar.
Yükleme Apple'ın desteklediği **altool** ve takım API anahtarıyla yapılır;
`notarytool` macOS noterleme aracıdır, iOS TestFlight yükleme aracı değildir.
[Apple build yükleme rehberi](https://developer.apple.com/help/app-store-connect/manage-builds/upload-builds)
App Store Connect ve TestFlight yükleme yolunu açıklar.

## 7. iPhone'a kur ve kullanıma hazır olma kontrolünü tamamla

App Store Connect build işlemesinin tamamlanmasını bekle. İstenirse export compliance sorularını
uygulamanın gerçek şifreleme kullanımına göre yanıtla. Kendi App Store Connect hesabını
**TestFlight → Internal Testing** grubuna ekle; iPhone'da TestFlight ile yükle.
TestFlight build'leri [Apple'a göre 90 gün](https://developer.apple.com/help/app-store-connect/test-a-beta-version/testflight-overview/)
kullanılabilir; kişisel kullanımı sürdürmek için yeni build yüklemek gerekir.

Gerçek iPhone kabul kontrolü:

1. Açılış, giriş/çıkış ve uygulama yeniden açıldığında güvenli oturum saklama.
2. Dar ekran, büyük yazı, ekran klavyesi, güvenli alan ve menü gezintisi.
3. Dosyalar/iCloud'dan PDF ve alış belgesi seçimi, kaynak PDF indirme/paylaşma.
4. Ekstre satırını kendi altında açma, taslak düzenleme, mali seçimin bağımsız kalması.
5. Önizleme, uyarı/onaylar, tek kayıt ve tekrar denemede çift kayıt oluşmaması.
6. Gerçek API JSON okuma/yazma yolları; finans verisini test ederek gerekçeli iptal akışı.
7. Uygulama aktifken bildirim kontrolü ve iOS bildirim izinleri.
   Arka planda Windows zamanlanmış görevinin eşdeğeri olduğu varsayılmaz.

Apple yüklemeyi kabul etti mesajı bu kontrollerin tamamlandığı anlamına gelmez.
Kişisel TestFlight dağıtımı için herkese açık App Store sayfasını yayımlamak gerekmez.
