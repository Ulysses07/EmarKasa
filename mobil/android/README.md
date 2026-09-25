# Emar Kasa Android uygulaması

Bu klasör, canlıdaki web ekranını (https://kasa.emarglobal.com) Android'e
**Trusted Web Activity (TWA)** olarak paketler. Uygulama telefonda Chrome'un
motoruyla, adres çubuğu olmadan tam ekran açılır. Menüler, giriş ve web
bildirimleri web sürümüyle birebir aynıdır. Sunucuda yapılan her güncelleme
uygulamaya kendiliğinden gelir, uygulamayı yeniden dağıtmak gerekmez.

- Paket adı: `com.royalmezat.kasa`
- Uygulama adı: Emar Kasa
- En düşük Android sürümü: 5.0 (API 21), hedef API 36
- Proje, Bubblewrap 1.25 şablonuyla üretildi (`twa-manifest.json`).
  Simgelerin kaynağı `ikonlar/` klasöründe.

## Tam ekran için sunucu dosyası

Chrome, uygulamanın bu siteye ait olduğunu
`https://kasa.emarglobal.com/.well-known/assetlinks.json` dosyasından doğrular.
Dosya yoksa uygulama yine çalışır, ama üstte ince bir adres çubuğu görünür.
Yayınlanacak içerik bu klasördeki `assetlinks.json` dosyasıdır ve imza
anahtarının SHA-256 parmak izini taşır. Parmak izi gizli değildir.

Uygulama ileride Play Store'a Play App Signing ile konursa, Play Console'daki
"Uygulama imzalama anahtarı" parmak izi de bu dosyaya ikinci satır olarak
eklenir.

## İmza anahtarı

- Anahtar bu depoda **yoktur** ve olmamalıdır. Depo herkese açık.
- Anahtar ve şifresi proje dosyalarında `mobil/android-imza/` klasöründe
  saklanır. Aynı anahtar kaybolursa telefondaki uygulama güncellenemez:
  kaldırılıp yeniden kurulması ve `assetlinks.json`'un değiştirilmesi gerekir.
- Takma ad (alias): `emar-kasa`.

## Derleme

GitHub Actions'taki **Android** işi (`.github/workflows/android.yml`) her
değişiklikte imzasız release APK üretir. APK hem iş çıktısı
(`emar-kasa-android-imzasiz`) olarak saklanır hem de taslak bir sürüme
(`android-ci-<numara>`) eklenir. Taslak sürümler yalnız depo yetkililerine
görünür.

Yerelde derlemek için Android SDK ve JDK 17 gerekir:

```sh
cd mobil/android
./gradlew assembleRelease
```

`keystore.properties` dosyası varsa (git'e girmez) APK doğrudan imzalı çıkar:

```properties
storeFile=/tam/yol/emar-kasa.keystore
storePassword=...
keyAlias=emar-kasa
keyPassword=...
```

Aynı değerler `KASA_ANDROID_KEYSTORE_FILE`, `KASA_ANDROID_KEYSTORE_PASSWORD`,
`KASA_ANDROID_KEY_ALIAS` ve `KASA_ANDROID_KEY_PASSWORD` ortam değişkenleriyle
de verilebilir.

İmzasız APK'yı sonradan imzalamak için (Android SDK build-tools):

```sh
zipalign -p -f 4 app-release-unsigned.apk hizali.apk
apksigner sign --ks emar-kasa.keystore --ks-key-alias emar-kasa \
  --out EmarKasa.apk hizali.apk
apksigner verify --print-certs EmarKasa.apk
```

## Yeni sürüm

`app/build.gradle` içindeki `versionCode` her sürümde bir artırılır,
`versionName` görünen sürümdür (`twa-manifest.json`'daki `appVersionCode` ve
`appVersion` da aynı tutulur). Aynı anahtarla imzalanan yeni APK, telefondaki
uygulamanın üzerine kurulur ve giriş bilgileri korunur.
