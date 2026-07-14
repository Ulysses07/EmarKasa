# Emar Kasa — Android (Google Play Store) Yayın Kılavuzu

## Amaç & Sorumluluk Sınırı

Emar Kasa Android sürümünü (`com.royalmezat.kasa`) Google Play Store'a yayınlamak.

| Görev | Sorumlu |
|---|---|
| Kod, build yapılandırması, imzalama kurulumu, mağaza metni | Mühendis/Claude |
| Google Play Developer hesabı oluşturma ($25 tek seferlik ücret) | **Kullanıcı** |
| Ödeme | **Kullanıcı** |
| Play Console'da son "Yayınla" tıklaması | **Kullanıcı** |

---

## Ön Koşullar

### 1. Makine Gereksinimleri

Bu kılavuzdaki derleme adımları **Mac veya Android workload yüklü bir Linux/Windows
makinesinde** çalıştırılmalıdır. Bu Windows makinesi şu an **yalnızca Windows TFM
derleyebilmektedir** (aşağıya bakınız).

| Gereksinim | Komut / Not |
|---|---|
| .NET 10 SDK | `dotnet --version` → `10.x.x` |
| MAUI workload | `dotnet workload install maui` |
| Android workload | `dotnet workload install android` |
| JDK 17+ | `java -version` |
| Android SDK | `sdkmanager --version` (Android Studio ile gelir) |

### 2. Mobil TFM'i Geri Açma

`Kasa.App/Kasa.App.csproj` dosyasının 4–6. satırları şu an şöyle görünür:

```xml
<!-- Windows-only for now (this machine builds Windows). Re-add mobile TFMs on Mac:
     net10.0-android;net10.0-ios;net10.0-maccatalyst -->
<TargetFrameworks>net10.0-windows10.0.19041.0</TargetFrameworks>
```

Mac'te (veya Android workload'lu makinede) `<TargetFrameworks>` satırını şöyle değiştir:

```xml
<TargetFrameworks>net10.0-android;net10.0-windows10.0.19041.0</TargetFrameworks>
```

> Yalnızca Android derlemek istiyorsan Windows TFM'i geçici olarak kaldırabilirsin;
> ancak CI tekrar Windows'a dönmeden önce geri ekle.

---

## Keystore — İmzalama Anahtarı Oluşturma

Android uygulamaları Play Store'a yüklenmeden önce imzalanmalıdır. Bu anahtar
**bir kez oluşturulur ve sonsuza dek korunur** — kaybedilirse aynı paket adıyla
(`com.royalmezat.kasa`) bir daha güncelleme yayınlanamaz.

### Keystore Dosyası Oluşturma

```bash
keytool -genkeypair \
  -v \
  -keystore emar-kasa-release.keystore \
  -alias emar-kasa \
  -keyalg RSA \
  -keysize 2048 \
  -validity 10000 \
  -storepass <keystore-parolası> \
  -keypass <anahtar-parolası> \
  -dname "CN=Emar Global Ltd, OU=Mobile, O=Emar Global Ltd, L=Istanbul, ST=Istanbul, C=TR"
```

`<keystore-parolası>` ve `<anahtar-parolası>` yerine güçlü, benzersiz parolalar gir.

### Keystore Güvenliği

- `.keystore` dosyasını repoya **ASLA koyma** — `.gitignore`'a ekle:
  ```
  *.keystore
  *.jks
  ```
- Güvenli bir yerde sakla: şifre yöneticisi (Bitwarden, 1Password) veya şifreli
  harici disk.
- Parolayı da aynı yerde sakla; dosya olmadan parola veya parola olmadan dosya
  işe yaramaz.

### Play App Signing (Önerilen)

Play Console → **Kurulum → Uygulama imzalama** adımında "Google tarafından
yönetilen imzalama" seçeneğini etkinleştir. Bu sayede Google anahtarın bir
yedek kopyasını güvenli şekilde saklar; yerel keystore'unu yitirsen bile
uygulama güncellenebilir kalır.

---

## AAB Derleme

### Temel Komut

Repo kökünden çalıştırılır:

```bash
dotnet publish Kasa.App/Kasa.App.csproj \
  -f net10.0-android \
  -c Release \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=/tam/yol/emar-kasa-release.keystore \
  -p:AndroidSigningKeyAlias=emar-kasa \
  -p:AndroidSigningKeyPass=<anahtar-parolası> \
  -p:AndroidSigningStorePass=<keystore-parolası>
```

`AndroidSigningKeyStore` için **tam yolu** ver (tilde `~` çalışmayabilir).

### Çıktı Konumu

```
Kasa.App/bin/Release/net10.0-android/publish/*-Signed.aab
```

Play Console'a yüklenecek dosya bu `*-Signed.aab` dosyasıdır.

### Doğrulama

Derleme başarılıysa son satır şuna benzer görünür:

```
Build succeeded.
```

İmzayı doğrulamak için:

```bash
jarsigner -verify -verbose -certs Kasa.App/bin/Release/net10.0-android/publish/*-Signed.aab
```

---

## Sürüm Numaraları

`Kasa.App/Kasa.App.csproj` içindeki mevcut değerler:

| Property | Mevcut Değer | Açıklama |
|---|---|---|
| `ApplicationDisplayVersion` | `1.0` | Kullanıcıya gösterilen sürüm ("1.0", "1.1", "2.0" vb.) |
| `ApplicationVersion` | `1` | Play Store'daki integer `versionCode`; her yeni yüklemede artırılmalı |

**Kural:** Her yeni Play yüklemesinde `ApplicationVersion` mutlaka bir öncekinden
büyük olmalı (örn. 1 → 2 → 3). Aynı veya küçük `versionCode` ile yükleme
Play Console tarafından reddedilir.

Güncelleme yaparken her iki değeri de csproj'da düzenle, ardından yeniden derle.

---

## Play Console Adımları

> **Kullanıcı adımları** aşağıda `[KULLANICI]` ile işaretlenmiştir.

### 1. Geliştirici Hesabı Oluşturma `[KULLANICI]`

1. [play.google.com/console](https://play.google.com/console) adresine git.
2. Google hesabınla giriş yap.
3. Geliştirici profili oluştur (şirket/bireysel).
4. **$25 tek seferlik kayıt ücretini** öde. `[KULLANICI]`

### 2. Uygulama Oluşturma `[KULLANICI]`

1. Play Console ana ekranında **"Uygulama oluştur"** tıkla.
2. Uygulama adı: `Emar Kasa`
3. Varsayılan dil: Türkçe
4. Uygulama veya oyun: **Uygulama**
5. Ücretsiz veya ücretli: ücret politikanıza göre seç.
6. Paket adı otomatik atanır; **`com.royalmezat.kasa`** olduğunu doğrula
   (AAB yüklendikten sonra değiştirilemez).

### 3. İç Test Sürümü Yükleme

1. Play Console → **Test → İç test** → **Sürüm oluştur**.
2. `*-Signed.aab` dosyasını yükle.
3. Sürüm notları ekle (Türkçe + İngilizce).
4. **Kaydet** → **İncele** → **İç teste yayınla**.
5. Birkaç test cihazında kurulumu ve temel işlevleri doğrula.

### 4. Prodüksiyona Geçiş `[KULLANICI]`

1. Play Console → **Prodüksiyon** → **Sürüm oluştur**.
2. İç testten onaylanmış AAB'yi seç veya yeni AAB yükle.
3. **Kademeli yayın yüzdesi** belirle (örn. %10 ile başla, sorunsuzsa %100'e çıkar).
4. **İncele** → **Prodüksiyona gönder** → son onayı ver. `[KULLANICI]`

> Google incelemesi genellikle birkaç saat ile birkaç gün sürer (ilk yayın
> daha uzun sürebilir).

---

## Gerekli Mağaza Materyalleri (Checklist)

Tam metinler `docs/store/` altında hazırlanacak (Task 5). Aşağıdaki tüm
materyaller Play Console'da zorunludur:

### Temel Bilgiler
- [ ] **Uygulama adı** — en fazla 30 karakter
- [ ] **Kısa açıklama** — en fazla 80 karakter
- [ ] **Uzun açıklama** — en fazla 4.000 karakter

### Grafikler (Android Gereksinimleri)
- [ ] **Uygulama ikonu** — 512×512 px, PNG, alfa kanallı
- [ ] **Öne çıkan grafik** — 1.024×500 px, JPG veya PNG
- [ ] **Telefon ekran görüntüleri** — en az 2, en fazla 8 adet;
  minimum 320 dp, maksimum 3.840 px, en boy oranı 16:9 veya 9:16
- [ ] **7" tablet ekran görüntüleri** (opsiyonel ama önerilir)
- [ ] **10" tablet ekran görüntüleri** (opsiyonel)

### Gizlilik & İçerik
- [ ] **Gizlilik politikası URL'i** — uygulamanın hangi verileri topladığını
  açıklayan herkese açık bir URL; Play Store zorunlu kılıyor
- [ ] **İçerik derecelendirmesi** — Play Console içinde anket doldurularak
  otomatik hesaplanır (PEGI / IARC)
- [ ] **Veri güvenliği bölümü** — uygulamanın hangi kullanıcı verilerini
  topladığı, sakladığı ve paylaştığını beyan et (Play Console formu)

### Teknik Kontrol
- [ ] `ApplicationVersion` (versionCode) bir önceki yüklemeden büyük
- [ ] `ApplicationDisplayVersion` (versionName) güncellendi
- [ ] AAB imzalandı (`*-Signed.aab` mevcut)
- [ ] Minimum Android API: **21** (csproj'da tanımlı)

---

## Hızlı Başvuru

| Alan | Değer |
|---|---|
| ApplicationId (paket adı) | `com.royalmezat.kasa` |
| ApplicationTitle | `Emar Kasa` |
| ApplicationDisplayVersion | `1.0` |
| ApplicationVersion (versionCode) | `1` |
| Minimum Android API | 21 (Android 5.0) |
| TFM | `net10.0-android` |
| AAB çıktısı | `Kasa.App/bin/Release/net10.0-android/publish/*-Signed.aab` |
