# Emar Kasa iPhone uygulaması

Bu klasör, canlıdaki web ekranını (https://kasa.emarglobal.com) iPhone'da
yerel bir kabuk içinde açar. Uygulama Safari'nin motoru olan **WKWebView**'u
kullanır; menüler, giriş ve bütün ekranlar web sürümüyle birebir aynıdır.
Ekranlar sunucudan geldiği için **sunucuda yapılan her güncelleme uygulamaya
kendiliğinden gelir**, bunun için yeni uygulama sürümü gerekmez. Yeni sürüm
yalnız yerel kabuk (bu klasör) değiştiğinde çıkar.

- Paket kimliği (bundle id): `com.royalmezat.kasa`
- Uygulama adı: Emar Kasa
- Sürüm: `1.0.0` (`project.yml` içinde `MARKETING_VERSION`); derleme numarası
  CI'dan gelir
- En düşük iOS sürümü: 16.0. Yalnız iPhone, dikey ekran.
- Dağıtım: önce TestFlight

## Uygulamanın yerel kısmı

- Uygulama içinde yalnız `https://kasa.emarglobal.com` açılır. Başka sitelere,
  e-posta ve telefon bağlantılarına giden adresler Safari'de ya da ilgili
  uygulamada açılır.
- Giriş çerezi (30 gün) kalıcı depoda durur; uygulama kapatılıp açılınca
  oturum korunur.
- İndirmeler: yedek (zip), Excel/CSV gider raporu, alış belgeleri ve ekstre
  PDF'i telefona iner. PDF, görsel ve tablolar önizlenir, yedek paylaşım
  menüsüyle (Dosyalar'a Kaydet) verilir. Geçici dosya ekran kapanınca silinir.
  Sunucu hata verirse (silinmiş belge, süresi dolmuş oturum) boş bir dosya
  inmez, uyarı çıkar.
- "Yazdır / PDF olarak kaydet" raporu ayrı bir sayfada açılır. "Yazdır"
  düğmesi sistem yazdırma ekranını açar; oradan PDF olarak da kaydedilir.
- Alışa belge eklerken kamera, fotoğraflar ve Dosyalar kullanılabilir.
- İnternet yoksa ya da sunucuya ulaşılamıyorsa "Tekrar dene" düğmeli bir
  hata ekranı çıkar. Sayfa aşağı çekilerek yenilenir; açık bir form
  penceresi ya da ekstre incelemesinde seçili satırlar varsa, yazılanlar
  silinmesin diye yenilenmez.
- Uygulamanın dili Türkçe olarak tanımlı: dosya seçici, tarih seçici gibi
  sistem ekranları telefon İngilizce olsa da Türkçe açılır.
- Face ID kilidi (`UygulamaKilidi.swift`): uygulama açılırken ve bir
  dakikadan uzun arka planda kaldıktan sonra Face ID ya da Touch ID ister,
  ikisi de yoksa telefon şifresini sorar. Telefonda şifre yoksa kilit
  çalışmaz. Uygulama etkinliğini kaybedince (uygulama değiştirici, Denetim
  Merkezi) içerik bir örtüyle gizlenir. Ayarlar > Emar Kasa > "Face ID
  kilidi" ile kapatılır (`Settings.bundle`, varsayılan açık).

**Bildirimler bu sürümde yok.** Sitenin web bildirimleri (Web Push) iPhone
uygulamalarının içinde çalışmaz. Site bunu kendisi algılar ve bildirim ayarında
"Bu tarayıcıda cihaz bildirimi desteklenmiyor" yazar. Uygulama içi bildirim
listesi çalışır. Telefon bildirimleri sonraki sürümde Apple'ın bildirim servisi
(APNs) ve sunucu tarafında ek bir çalışmayla gelecek.

iOS uygulaması için sunucuda değişiklik gerekmez. Android'deki
`assetlinks.json` gibi bir doğrulama dosyası yoktur.

## Proje dosyaları

Xcode projesi (`EmarKasa.xcodeproj`) ve `EmarKasa/Info.plist` depoda
tutulmaz. İkisini de [XcodeGen](https://github.com/yonaskolb/XcodeGen)
`project.yml` dosyasından üretir. Ayarlar, izin metinleri ve sürüm bu dosyada.

- `EmarKasa/`: Swift kaynakları, simge ve renkler (`Assets.xcassets`),
  gizlilik bildirimi (`PrivacyInfo.xcprivacy`: izleme yok, Apple'ın
  gerekçe istediği API'ler kullanılmıyor)
- `EmarKasaTests/`: hangi adresin uygulamada açılacağı, hangisinin
  indirileceği kurallarının birim testleri
- Simgenin kaynağı: `AppIcon-1024.png` (1024×1024, saydamlık yok)

Mac'te açmak için (Xcode 16 ya da yenisi; App Store Connect'e yükleme için
CI Xcode 26 kullanır):

```sh
brew install xcodegen
cd mobil/ios
xcodegen generate
open EmarKasa.xcodeproj
```

## Derleme (GitHub Actions)

`.github/workflows/ios.yml` iki iş çalıştırır:

1. **Derle ve simülatörde aç** (her değişiklikte): imzasız simülatör
   derlemesi ve birim testleri. Uygulama simülatörde açılır, önce Face ID
   kilit ekranının, sonra kilit kapatılarak canlı site yüklendikten sonra
   ekran görüntüsü alınır (CI simülatöründe Face ID penceresi açılamıyor). Görüntüler, derleme ve test
   günlükleri taslak bir sürüme (`ios-ci-<numara>`) eklenir. Taslak
   sürümler yalnız depo yetkililerine görünür.
2. **TestFlight'a yükle**: Apple sırları tanımlıysa master'daki her
   değişiklikte, elle çalıştırıldığında ve **ayda bir** otomatik olarak
   (TestFlight derlemeleri 90 günde düşer) arşivler, imzalar ve App Store
   Connect'e yükler. Derleme numarası çalıştırma numarasıdır. Arşiv ve yükleme
   günlükleri, sırlar `***` yapılarak `ios-testflight-<numara>` taslak
   sürümüne eklenir. Sırlar yoksa bu iş atlanır, simülatör işi yine çalışır.

GitHub, 60 gün hiç değişiklik olmayan herkese açık depolarda zamanlanmış
işleri kapatır. Kapanırsa Actions sekmesinden yeniden açılır.

## TestFlight için gerekenler

Depo herkese açık: anahtarlar ve kimlikler **hiçbir dosyaya yazılmaz**,
yalnız depo sırlarında durur.

### Apple tarafında bir kez yapılacaklar

1. Ücretli Apple Developer Program üyeliği. Hesap sahibi, App Store Connect'teki
   güncel sözleşmeleri kabul etmiş olmalı; yoksa yükleme reddedilir.
2. **Paket kimliğini kaydet:** developer.apple.com → Certificates, Identifiers
   & Profiles → Identifiers → "+" → App IDs → App. "Explicit" seçilir,
   `com.royalmezat.kasa` yazılır. Ek yetenek (capability) gerekmez.
3. **Uygulama kaydını oluştur:** App Store Connect → Uygulamalar → "+" → Yeni
   Uygulama. Platform iOS, ad "Emar Kasa" (mağazada başka uygulamada
   kullanılıyorsa farklı bir ad seçilir; telefondaki ad yine Emar Kasa
   kalır), birincil dil Türkçe, paket kimliği yukarıdaki, SKU örneğin
   `emar-kasa`. Bu kayıt olmadan yükleme "no suitable application records"
   hatasıyla düşer.
4. **API anahtarı:** App Store Connect → Kullanıcılar ve Erişim →
   Entegrasyonlar → App Store Connect API → Takım Anahtarları → "+". Rol
   **Admin** seçilir (imzalama sertifikası ve profilini bulutta oluşturmak
   için gerekir). Sayfadaki **Issuer ID** ve anahtarın **Key ID**'si not
   edilir, `AuthKey_XXXXXXXXXX.p8` dosyası indirilir. Dosya yalnız bir kez
   indirilebilir.
5. Takım kimliği (Team ID, 10 karakter): developer.apple.com → Account →
   Membership details.

### Depo sırları

GitHub → depo → Settings → Secrets and variables → Actions → New repository
secret:

| Sır | Değer |
|---|---|
| `ASC_KEY_ID` | API anahtarının Key ID'si |
| `ASC_ISSUER_ID` | Issuer ID |
| `ASC_KEY_P8_BASE64` | `.p8` dosyasının base64 hali: `base64 -i AuthKey_XXXXXXXXXX.p8` (Linux'ta `base64 -w0`). Dosyanın düz metni de kabul edilir. |
| `APPLE_TEAM_ID` | Takım kimliği |

**Kayıtlı iPhone yoksa:** imzalı arşiv bir geliştirme profili ister, Apple
da bu profili ancak hesapta en az bir kayıtlı cihaz (UDID) varsa oluşturur;
yoksa arşivleme "Your team has no devices" hatasıyla düşer. İki yol var:

- Certificates, Identifiers & Profiles → Devices'a bir iPhone'un UDID'si
  eklenir, ya da
- aynı Actions ayarları sayfasında **Variables** sekmesine
  `IOS_IMZASIZ_ARSIV` = `true` değişkeni eklenir (sır değildir). O zaman
  arşiv imzasız alınır, uygulama yalnız App Store Connect'e giderken
  dağıtım sertifikasıyla imzalanır. Master'a gönderim ve aylık çalıştırma
  da bu yolu kullanır. Elle çalıştırmada aynı şey `imzasiz_arsiv`
  kutusuyla seçilir. Bu yol henüz denenmedi (deneysel); dışa aktarma
  adımı hata verirse kesin çözüm cihaz eklemektir.

Sırlar girildikten sonra Actions → iOS → "Run workflow" ile ilk yükleme
başlatılır.

### Yüklemeden sonra

- Apple derlemeyi 5-30 dakikada işler, sonra TestFlight sekmesinde görünür.
- App Store Connect → TestFlight → İç Test (Internal Testing) grubuna kendinizi
  ekleyin; telefona TestFlight uygulaması kurulur ve Emar Kasa oradan
  yüklenir. İç test için Apple incelemesi gerekmez.
- Dış test kullanıcıları için Apple'ın beta incelemesi gerekir; inceleme
  ekibine bir deneme hesabı (kullanıcı adı ve şifre) verilmelidir.
- İmzalama "Your team has no devices" hatası verirse yukarıdaki "Kayıtlı
  iPhone yoksa" bölümüne bakın.
- Her CI makinesi yeni bir "Created via API" geliştirme sertifikası
  oluşturabilir. Sertifika sınırına gelinirse eskileri iptal edilir.

## Yeni sürüm

Yerel kabukta bir değişiklik olduğunda `project.yml` içindeki
`MARKETING_VERSION` artırılır (örneğin `1.0.1`). Derleme numarası her
çalıştırmada kendiliğinden artar. 1.0.0 App Store'da onaylandıktan sonra aynı
sürüm numarasıyla yükleme kabul edilmez; sürüm artırılmalıdır.

## Web tarafında sonra düzeltilecekler

- Tutar alanları `inputmode="decimal"` kullanıyor; iPhone'un bu klavyesinde
  eksi tuşu yok. İade gibi eksi tutarlar yalnız yapıştırılarak girilebiliyor.
- Bildirim ayarındaki "Ana Ekrana Ekle" açıklaması ve Ayarlar'daki "Windows
  uygulamasını indir" bağlantısı uygulamada anlamsız. Uygulama, kullanıcı
  aracısına (User-Agent) `EmarKasa-iOS/<sürüm>` ekler; site bunları buna göre
  gizleyebilir.
- `kasa-runtime.json` alınamazsa sayfa İngilizce "Load failed" yazıp giriş
  formunu gizliyor. Uygulamada sayfayı aşağı çekip yenilemek düzeltir.
