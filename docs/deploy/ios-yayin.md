# Emar Kasa — iOS App Store Yayın Kılavuzu

## 1. Amaç ve Sorumluluk Sınırı

Emar Kasa'yı Apple App Store'da yayınlamak.

| Görev | Sorumlu |
|---|---|
| Kod, build yapılandırması, imzalama kurulumu, mağaza metni/görselleri | Claude / mühendis |
| Apple Developer Program kaydı ($99/yıl) ve ödeme | **Kullanıcı** |
| Mac + Xcode ortamı ve IPA derleme | **Kullanıcı** |
| App Store Connect'te son "İncelemeye gönder" tıklaması | **Kullanıcı** |

> iOS build **Mac + Xcode gerektirir.** Bu Windows makinesinde IPA üretmek mümkün değildir
> ve proje dosyasında iOS TFM şu an yorum satırı olarak bırakılmıştır (bkz. §2).

---

## 2. Ön Koşullar

### Ortam (Mac'te)

| Gereksinim | Komut |
|---|---|
| macOS (Apple silicon veya Intel) | — |
| Xcode 16+ (App Store'dan) | `xcode-select --version` |
| .NET 10 SDK | `dotnet --version` → `10.x.x` |
| MAUI workload | `dotnet workload install maui` |

### iOS TFM'ini Geri Aç

`Kasa.App/Kasa.App.csproj` dosyasında iOS TFM şu an yorum satırı içinde yer almaktadır:

```xml
<!-- Windows-only for now (this machine builds Windows). Re-add mobile TFMs on Mac:
     net10.0-android;net10.0-ios;net10.0-maccatalyst -->
<TargetFrameworks>net10.0-windows10.0.19041.0</TargetFrameworks>
```

Mac'te derlemeden önce bu satırı şu şekilde değiştir:

```xml
<TargetFrameworks>net10.0-android;net10.0-ios;net10.0-windows10.0.19041.0</TargetFrameworks>
```

> Yalnızca iOS build'i istiyorsan `net10.0-ios` tek başına da yeterlidir:
> `<TargetFrameworks>net10.0-ios</TargetFrameworks>`

---

## 3. İmzalama

### Apple Developer Hesabı

1. [developer.apple.com/programs](https://developer.apple.com/programs/) adresine git.
2. **Kullanıcı** Apple Developer Program'a kaydolur ve yıllık $99 öder.
3. Üyelik onaylandıktan sonra aşağıdaki adımlar yapılabilir.

### App ID Kaydı

- **App Store Connect** → Identifiers → `+` → App IDs
- Platform: iOS
- Bundle ID: **`com.royalmezat.kasa`** (Explicit)
- Capabilities: gerekirse Push Notifications vs. etkinleştir (v1 için ek capability gerekmez)

### Distribution Sertifikası

1. Keychain Access → Certificate Assistant → "Request a Certificate from a Certificate Authority" → CSR dosyası oluştur.
2. Apple Developer Portal → Certificates → `+` → **Apple Distribution** → CSR yükle → `.cer` indir.
3. `.cer`'i Keychain Access'e çift tıkla ile import et.

### Provisioning Profile

1. Developer Portal → Profiles → `+` → **App Store Connect** dağıtım profili.
2. App ID: `com.royalmezat.kasa` seç.
3. Sertifikayı seç → profili indir (`.mobileprovision`).

### Xcode Otomatik İmzalama (Önerilen)

`Kasa.App.csproj` ya da Xcode içinden `Automatically manage signing` açık bırakılabilir.
`dotnet publish` ile manuel imzalama yapıyorsan aşağıdaki property'leri kullan:

```
-p:CodesignKey="Apple Distribution: <Ad Soyad veya Şirket Adı> (<Team ID>)"
-p:CodesignProvision="<Provisioning Profile adı>"
```

> **Güvenlik:** Sertifika özel anahtarları (`.p12`, `.mobileprovision`) repoya eklenmez.
> CI ortamı kullanılırsa GitHub Actions Secrets veya Apple Fastlane Match tercih edilir.

---

## 4. IPA Derleme (Mac'te)

### Temel Komut

Repo kökünden çalıştırılır:

```bash
dotnet publish Kasa.App/Kasa.App.csproj \
  -f net10.0-ios \
  -c Release \
  -r ios-arm64 \
  -p:CodesignKey="Apple Distribution: <Ad> (<TeamID>)" \
  -p:CodesignProvision="<Profil Adı>"
```

### Çıktı Konumu

```
Kasa.App/bin/Release/net10.0-ios/ios-arm64/publish/
```

Ana dosya: `Kasa.App.ipa`

### Notlar

- `-r ios-arm64` yalnızca fiziksel cihaz (App Store) içindir; simülatör build'i için `iossimulator-x64` kullanılır ama simülatör IPA App Store'a gönderilemez.
- Simulator build'i App Store Connect'e **yüklenemez**; dağıtım için mutlaka `ios-arm64` kullan.
- Xcode 16 ile `xcodebuild archive` yöntemi de kullanılabilir; `.xcarchive` → `.ipa` Xcode Organizer aracılığıyla aktarılır.

---

## 5. Sürüm Numaraları

`Kasa.App/Kasa.App.csproj` içindeki değerler:

| csproj Property | iOS Karşılığı | Mevcut Değer |
|---|---|---|
| `ApplicationDisplayVersion` | `CFBundleShortVersionString` (kullanıcıya gösterilen) | `1.0` |
| `ApplicationVersion` | `CFBundleVersion` (derleme numarası) | `1` |

Yeni sürüm yayınlarken:
- `ApplicationDisplayVersion`: kullanıcıya gösterilen sürüm (ör. `1.0.1`, `1.1`)
- `ApplicationVersion`: her App Store yüklemesinde artırılmalıdır (App Store Connect bunu zorunlu tutar; ör. `2`, `3`, ...)

---

## 6. App Store Connect Adımları

### Uygulama Kaydı

1. [appstoreconnect.apple.com](https://appstoreconnect.apple.com) → My Apps → `+` → New App.
2. Platform: iOS.
3. Bundle ID: `com.royalmezat.kasa` seç (önceden Developer Portal'da tanımlanmış olmalı).
4. Ad: **Emar Kasa**.
5. SKU: `emar-kasa` (herhangi bir dahili tanımlayıcı, değiştirilmez).

### TestFlight (Önerilir)

1. IPA'yı Transporter uygulaması veya `xcrun altool --upload-app` ile App Store Connect'e yükle.
2. App Store Connect → TestFlight sekmesi → iç test kullanıcıları ekle.
3. Smoke test'leri geçince App Store'a taşı.

### IPA Yükleme

**Transporter (Mac App Store'dan):**
```
Transporter → Add → .ipa dosyasını seç → Deliver
```

**Komut satırı (`altool`, Xcode 14 öncesi):**
```bash
xcrun altool --upload-app -f Kasa.App.ipa \
  -t ios \
  -u <apple-id-email> \
  -p <app-specific-password>
```

**Komut satırı (`notarytool` / Xcode 14+, önerilen):**
```bash
xcrun notarytool submit Kasa.App.ipa \
  --apple-id <apple-id-email> \
  --team-id <TeamID> \
  --password <app-specific-password> \
  --wait
```

> App-specific password: [appleid.apple.com](https://appleid.apple.com) → Güvenlik → Uygulamaya özel şifreler.

### Mağaza Sayfası Doldurma (Kullanıcı)

App Store Connect'te şunları doldur:
- Açıklama, anahtar kelimeler, ekran görüntüleri (bkz. `docs/store/`)
- Destek URL, gizlilik politikası URL
- Yaş derecelendirmesi soruları
- Şifreleme ihracat uyumu bildirimi
- App Privacy (Nutrition Label)

### İncelemeye Gönder (Kullanıcı)

Tüm bilgiler dolunca **"Submit for Review"** — bu adım **kullanıcı** tarafından yapılır.

---

## 7. Apple'a Özgü Gereksinimler

### App Privacy (Nutrition Label)

App Store Connect → App Privacy bölümünde hangi verilerin toplandığı beyan edilmelidir.

Emar Kasa v1 için beklenen beyan:

| Veri Türü | Toplanıyor mu? | Kullanım |
|---|---|---|
| Finansal bilgi | Hayır (sunucuda; kullanıcı kendi girer) | — |
| Kimlik bilgisi | Evet (kullanıcı adı + şifre) | Kimlik doğrulama |
| Kullanım verileri | Hayır | — |
| Tanımlayıcı (cihaz ID) | Hayır | — |

> Kesin beyanı uygulama geliştirme tamamlandıktan sonra gözden geçir; veri akışı değişirse güncelle.

### Şifreleme İhracat Uyumu (Export Compliance)

App Store Connect, uygulamanın şifreleme kullandığını sorar. .NET MAUI + HTTPS standart şifreleme kullanır (AES/TLS) — bu **muaf kategoriye** girer (ABD EAR §742.15(b) istisnası).

Beyan: "Evet, şifreleme kullanıyor" → "Standart şifreleme, muafiyetten yararlanıyor" seçeneğini işaretle. `Info.plist`'e `ITSAppUsesNonExemptEncryption = NO` eklemek bu soruyu otomatik atlar:

```xml
<!-- Kasa.App/Platforms/iOS/Info.plist -->
<key>ITSAppUsesNonExemptEncryption</key>
<false/>
```

### Yaş Derecelendirmesi

App Store Connect → Age Rating → anketi doldur.
Emar Kasa finansal takip uygulaması; şiddet, alkol, kumar vb. yok → **4+** beklenir.

---

## Hızlı Başvuru

| Alan | Değer |
|---|---|
| TFM (iOS) | `net10.0-ios` |
| RID | `ios-arm64` |
| ApplicationId | `com.royalmezat.kasa` |
| ApplicationTitle | `Emar Kasa` |
| ApplicationDisplayVersion | `1.0` |
| ApplicationVersion | `1` |
| Min iOS | `15.0` |
| API sunucusu | `https://kasa.emarglobal.com/` |
| App Store Connect | [appstoreconnect.apple.com](https://appstoreconnect.apple.com) |
| Developer Portal | [developer.apple.com](https://developer.apple.com) |
