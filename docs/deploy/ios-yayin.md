# Emar Kasa — iOS hazırlık ve kişisel dağıtım rehberi

> Durum: 7 Ekim 2026. iOS sürümü henüz derlenip iPhone üzerinde doğrulanmadı.
> Bu belge hazırlık adımlarını ve yayın kapılarını anlatır; mevcut Windows yapılandırması hazır bir iOS paketi üretmez.

## 1. Mevcut durum

[Uygulama projesi](../../Kasa.App/Kasa.App.csproj) yalnız `net10.0-windows10.0.19041.0` hedefini etkinleştirir.
`Platforms/iOS` altındaki başlangıç, ikon ve gizlilik dosyaları bulunur; bunların bulunması iOS çalışırlığını kanıtlamaz.
[CI](../../.github/workflows/ci.yml) yerel uygulamayı Windows'ta derler ve açılışını sınar; iOS derleme/cihaz kapısı yoktur.

| Alan | Kaynaktaki değer |
| --- | --- |
| Uygulama adı | Emar Kasa |
| Bundle ID | `com.royalmezat.kasa` |
| Ürün sürümü | `2.4.1` — [Directory.Build.props](../../Directory.Build.props), `KasaSurumu` |
| Derleme numarası | `8` — `ApplicationVersion` |
| MAUI paket sürümü | `10.0.110` — `MauiVersion` |
| Planlanan iOS hedefi | `net10.0-ios` — şu an etkin değil |
| Yapılandırılan en düşük iOS | `15.0` — cihaz üzerinde doğrulanmadı |
| Fiziksel cihaz / dağıtım RID | `ios-arm64` |
| Sunucu | `https://kasa.emarglobal.com/` |

`ApplicationDisplayVersion`, `$(KasaSurumu)` değerini kullanır. Sürüm değişikliği ayrı yayın kararıdır.
Yeni App Store Connect yüklemesinde aynı ürün sürümü için daha önce kabul edilmiş derleme numaraları kontrol edilir ve gerekiyorsa
`ApplicationVersion` artırılır. Bu rehber sürüm değerlerini değiştirmez.

## 2. Kaynak kodda tamamlanacak hazırlıklar

| Kapı | Mevcut engel ve gereken iş |
| --- | --- |
| iOS hedefi | Mac'te iOS hazırlığı için `net10.0-ios` etkinleştirilmeli. Hedefler işletim sistemine göre ayrılmalı: Windows derlemesi Windows hedefini, Mac'teki bu çalışma iOS hedefini seçmeli. Android ve Windows hedeflerini aynı Mac hazırlığına topluca eklemeyin. |
| Açılış hizmetleri | [MauiProgram.cs](../../Kasa.App/MauiProgram.cs) içinde `IBildirimGosterici` ve `BildirimTiklamalari` yalnız `#if WINDOWS` altında kayıtlı. [AppShell](../../Kasa.App/AppShell.xaml.cs) bunları dolaylı/doğrudan zorunlu ister; [App](../../Kasa.App/App.xaml.cs) kabuğu açılışta çözer. iOS için uygun hizmetler veya açıkça bildirimsiz çalışma desteği kurulmadan yalnız hedefi açmak yeterli değildir. |
| Bildirimler | Windows zamanlanmış görevi koşulsuz kayıtlı; bildirim ekranı Windows ayar bağlantısı kullanır. iOS'ta Windows görevi ve ayar bağlantısı çalıştırılmamalı. iOS yerel/uzak bildirim desteği ayrıca tasarlanıp doğrulanmalı; web push desteği yerel uygulamanın bildirim desteği değildir. |
| PDF ve belge seçimi | [EkstreAktarmaPage.cs](../../Kasa.App/Views/EkstreAktarmaPage.cs) ve [AlislarPage.xaml.cs](../../Kasa.App/Views/AlislarPage.xaml.cs) dosya türlerini yalnız `DevicePlatform.WinUI` için tanımlar. iOS için PDF/görsel UTType değerleri ya da uygun yerleşik türler eklenmeli; Dosyalar/iCloud üzerinden seçim denenmeli. |
| Yazı ve simgeler | [Styles.xaml](../../Kasa.App/Resources/Styles/Styles.xaml) Windows'un Segoe yazı ve simge ailelerini kullanır. iOS sistem yazısı ve iOS'ta bulunan veya uygulamayla paketlenen simgeler tanımlanmalı. |
| Telefon yerleşimi | Giriş formu sabit 380 genişlikte; İşlemler ekranı 360 genişlikte formu listeyle yan yana tutar. Dar iPhone ekranında formlar, menü ve listeler uyarlanmalı; klavye, güvenli alan, yatay kullanım ve büyük yazı denenmeli. |
| Dışarı aktarma | [DosyaIslemleri.cs](../../Kasa.App/Views/DosyaIslemleri.cs) Windows dışı dosya paylaşımına bir yol içerir; ancak yazdırma yönergesi Ctrl+P/Microsoft Print to PDF der. iOS paylaşım/yazdırma akışı ve metni uyarlanıp cihazda denenmeli. |

Bunlar kaynak incelemesindeki engellerdir. iOS derlemesi henüz yapılmadığından liste bütün platform hatalarının
bulunduğu iddiasını taşımaz. [MAUI dosya seçici belgesi](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker?view=net-maui-10.0)
platform türleri için iOS UTType kullanımını açıklar.

## 3. Mac ve derleme araçlarını ayrı doğrulama

Mac modeli, işlemci, tam macOS sürümü ve kurulu Xcode sürümü yayın ortamı seçilmeden kaydedilmeli.
“16.x” tek başına yeterli değildir: Sequoia'nın sürümü `15.x` olur; ifade Xcode `16.x` sürümüne ait olabilir.

Mac Terminal'de salt okunur kontrol:

```bash
sw_vers -productVersion
sysctl -n hw.model
uname -m
xcodebuild -version
xcode-select -p
dotnet --version
dotnet workload --info
```

`uname -m` sonucuna göre .NET SDK paketi `arm64` (Apple silicon) veya `x64` (Intel) seçilir.
Sadece “Sequoia kurulu” bilgisiyle donanımın resmî desteği veya derleme kapasitesi doğrulanmış sayılmaz.

### Sequoia 15.6+ için doğrulanmış araç adayı

[Apple'ın sistem tablosunda](https://developer.apple.com/xcode/system-requirements) Xcode `26.2` ve `26.3`,
macOS Sequoia `15.6+` üzerinde desteklenir ve iOS `26.2` SDK içerir.
Xcode `26.4.1` ve `26.5` ise macOS Tahoe `26.2+` ister; bunların gereksinimleri Sequoia adayına uygulanmamalı.

| Parça | Resmî sürüm notunda doğrulanan aday |
| --- | --- |
| .NET SDK | `10.0.200` |
| Workload set | `10.0.200` |
| .NET iOS SDK | `26.2.10217` |
| Xcode | `26.2`; `26.3` için aşağıdaki sınırlı istisna |
| macOS | Sequoia `15.6+` |

[.NET iOS 26.2.10217 sürüm notu](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode26.2-10217)
bu eşleşmeyi ve workload setini belirtir. Aynı not, Xcode `26.3` ile SDK'lar aynı olduğu için
`ValidateXcodeVersion=false` kullanılabileceğini açıkça söyler. Bu istisna yalnız bu doğrulanmış eşleşmeye uygulanır;
başka Xcode/workload uyuşmazlıklarını bastırmak için genel proje ayarı yapılmaz.

Bu, tarihsel olarak doğrulanmış bir araç adayıdır; Emar Kasa'nın bu araçlarla iOS Release/IPA derlemesi henüz sınanmadı.
SDK'nin bakım/güvenlik güncellemeleri ve kurulacak tam workload sürümü hazırlık sırasında ayrıca değerlendirilip kaydedilmeli.
Denetimsiz `dotnet workload update` daha yeni Xcode/macOS gerektiren bir iOS SDK'sına geçirebilir.
[Microsoft Xcode eşleşme rehberi](https://learn.microsoft.com/en-us/dotnet/ios/troubleshooting/xcode-requirement)
belirli workload sürümlerinin belirli Xcode sürümlerini istediğini açıklar.

`MauiVersion=10.0.110`, uygulamanın MAUI NuGet sürümüdür; iOS workload/Xcode sürümünü tek başına belirlemez.
[MAUI sürüm açıklaması](https://github.com/dotnet/maui/wiki/Release-Versions) bu ayrımı belirtir.
Seçilen SDK ve workload seti, Mac'te yeniden üretilebilir derleme yapılandırmasıyla sabitlenmeli.

### App Store Connect SDK eşiği

[Apple'ın 28 Nisan 2026 gereksinimi](https://developer.apple.com/news/upcoming-requirements/?id=04282026a)
App Store Connect'e yüklenen uygulamalar için Xcode `26+` ve iOS `26+` SDK ister.
Xcode 26.2/26.3'ün iOS 26.2 SDK'sı bu eşiği sağlar. SDK sürümü uygulamanın en düşük desteklenen iOS sürümü değildir;
bu projedeki `15.0` cihaz hedefi ayrıca test edilmelidir.

## 4. İmzalama ve kişisel dağıtım seçimi

Etkin Apple Developer üyeliği, Mac/Xcode ortamı ve aynı Bundle ID için geçerli imzalama gerekir.
Yerel iOS derlemesi Apple araçlarına erişim gerektirir; Windows'tan çalışma da uygun bir Mac derleme sunucusuna bağlanır.
[Microsoft iOS yayın rehberi](https://learn.microsoft.com/en-us/dotnet/maui/ios/deployment/publish-cli?view=net-maui-10.0)

Kişisel kullanımda dağıtım yöntemi seçilir:

- **Geliştirme kurulumu:** Kayıtlı iPhone ve geliştirme sertifikası/profiliyle ilk cihaz denemesi.
- **Ad hoc dağıtım:** Kullanılacak cihazlar profile kaydedilir; aynı üyeliğin uygun dağıtım sertifikası/profili kullanılır.
- **TestFlight:** App Store Connect kaydı, dağıtım imzası ve yüklenen IPA gerekir. Her yapı 90 gün kullanılabilir;
  süre dolmadan yeni yapı yüklenir. Herkese açık App Store yayını ayrı karardır.
  [Apple TestFlight rehberi](https://developer.apple.com/help/app-store-connect/test-a-beta-version/testflight-overview/)

Apple Developer hesabında `com.royalmezat.kasa` App ID'sinin takıma ait olduğu doğrulanmalı.
Profil, seçilen yönteme ve bu App ID'ye uygun olmalı; sertifikanın özel anahtarı Mac anahtarlığında bulunmalı.
App Store Connect kaydı TestFlight/yayın seçilirse oluşturulur. Geliştirme/ad hoc kurulumda cihaz UDID'si ilgili profile eklenir.

`.p12`, provisioning profilleri, özel anahtarlar ve parolalar depoya yazılmaz.
İmzalama bilgileri uygulama koduna gömülmez; seçilen derleme ortamının güvenli deposunda tutulur.

## 5. Derleme ve iPhone doğrulama kapıları

Aşağıdaki kapılar tamamlanmadan dağıtım hazır kabul edilmez:

1. Bölüm 2'deki platform engelleri giderilmiş ve iOS hedefi Mac için etkinleştirilmiş.
2. Mac modeli/macOS, seçilen Xcode, .NET SDK ve workload sürümleri kaydedilmiş; sürüm eşleşmesi doğrulanmış.
3. iOS simülatöründe açılış, giriş, menüler ve dar ekran yerleşimi başarılı.
4. Fiziksel iPhone'da **Release** derlemesi açılıyor; imzalama ve güvenli oturum saklama çalışıyor.
5. Geçici test verileriyle kayıt, düzenleme, önizleme/onay, çıkış, bağlantı kesilmesi ve yeniden açma denenmiş.
6. Dosyalar'dan gerçek örnek PDF seçimi, ekstre uyarıları ve kaynak satırlarla önerilerin karşılaştırılması denenmiş;
   mali kayda geçmeden tarih/tutar/yön kontrolü yapılmış.
7. Paylaşım/indirme, klavye altında kalan alanlar, güvenli alan, yatay kullanım ve büyük yazı kontrol edilmiş.
   Yerel bildirim özelliği sunuluyorsa izin ve gerçek cihaz davranışı ayrıca doğrulanmış.
8. Dağıtım için üretilen IPA'nın Bundle ID'si, ürün sürümü, derleme numarası ve imza/profil eşleşmesi doğrulanmış.

Simülatör RID'si Mac işlemcisine göre `iossimulator-arm64` veya `iossimulator-x64` olur.
Simülatör çıktısı fiziksel cihaz paketi yerine kullanılamaz.

### Hazırlık tamamlandıktan sonraki IPA komut şablonu

Bu şablon **iOS hedefi etkinleştirildikten ve yukarıdaki doğrulama kapıları tamamlandıktan sonra**,
seçilen dağıtım sertifikası/profiliyle Mac'te uygulanır. Bugünkü Windows hedefiyle doğrudan çalışmaz.

```bash
dotnet publish Kasa.App/Kasa.App.csproj \
  -f net10.0-ios \
  -c Release \
  -r ios-arm64 \
  -p:ArchiveOnBuild=true \
  -p:CodesignKey="Apple Distribution: <Ad> (<TeamID>)" \
  -p:CodesignProvision="<Dağıtım Profilinin Adı>"
```

Yalnız bölüm 3'teki `26.2.10217` workload ve Xcode `26.3` birlikte seçildiyse,
sürüm notundaki `-p:ValidateXcodeVersion=false` parametresi bu derlemeye eklenir.

Beklenen dağıtım dizini: `Kasa.App/bin/Release/net10.0-ios/ios-arm64/publish/`.
Üretilen gerçek `.ipa` dosya adı ve içerik sürümü çıktıdan doğrulanır; dosyanın varlığı tek başına iPhone testini karşılamaz.
`ArchiveOnBuild=true` arşiv üretimi ve imzalama parametreleri için
[Microsoft komut satırı yayın rehberi](https://learn.microsoft.com/en-us/dotnet/maui/ios/deployment/publish-cli?view=net-maui-10.0) kullanılır.

## 6. TestFlight / App Store Connect yüklemesi

1. Doğru takım altında uygulama kaydı ve Bundle ID eşleşmesi kontrol edilir.
2. İmzalı IPA, Apple'ın desteklediği **Transporter** veya uygun Xcode yükleme akışıyla App Store Connect'e yüklenir.
3. İşlenen yapı, sürüm ve derleme numarası doğrulanır; şifreleme soruları gerçek uygulama içeriğine göre yanıtlanır.
4. TestFlight seçilirse kendi hesabı uygun iç test kullanıcısı olarak eklenir; cihazdan kurulum ve güncelleme denenir.
5. Haricî test kullanıcıları eklenecekse Apple'ın beta inceleme kuralları uygulanır.
6. Herkese açık App Store yayını istenirse mağaza bilgileri ve incelemeye gönderme ayrıca tamamlanır.

[Apple'ın desteklediği yükleme yöntemleri](https://developer.apple.com/help/app-store-connect/manage-builds/upload-builds/)
izlenir. Önceki rehberdeki `notarytool submit Kasa.App.ipa` örneği iOS App Store Connect yüklemesi için yanlıştı
ve kaldırıldı.

### Yayın beyanları

App Store yayını seçilirse gizlilik ve şifreleme beyanları uygulamanın gerçek veri akışına göre hazırlanır.
Finansal verinin kendi sunucusunda tutulması “finansal veri toplanmıyor” demek değildir:
uygulama kayıt ve PDF içeriğini sunucuya gönderip kalıcı olarak saklar.
[Apple veri toplama tanımı](https://developer.apple.com/app-store/app-privacy-details/) uyarınca
finansal bilgi, yüklenen içerik ve kimlik/oturum verileri ayrı değerlendirilir; kullanıcı kendi giriyor diye otomatik muafiyet yazılmaz.

`Platforms/iOS/Resources/PrivacyInfo.xcprivacy` mevcut MAUI temel bildirimlerini içerir; son uygulamanın API/SDK kullanımıyla
uyumu iOS paketinde kontrol edilmelidir. `Info.plist` içindeki şifreleme anahtarları da uygulama incelenmeden otomatik eklenmez.
[Apple şifreleme rehberi](https://developer.apple.com/help/app-store-connect/manage-app-information/determine-and-upload-app-encryption-documentation/)
izlenir. Yaş derecelendirmesi güncel Apple anketiyle belirlenir; burada hazır bir derece varsayılmaz.
