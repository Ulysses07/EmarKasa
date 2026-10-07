# Windows masaüstü ve iOS yayın akışı

Bu depo iki yerel istemci üretir: **Windows masaüstü uygulaması** ve **iOS uygulaması**. İkisi de aynı Kasa API sunucusunu kullanır. PDF ekstre ve finansal kayıt davranışı ortak istemci mantığıyla korunur; iOS’a özel dosya seçimi, güvenli depolama ve ekran davranışları ayrıca gerçek iPhone’da kontrol edilir.

## Bulutta önce doğrulama

PR ve push koşusunda mevcut **Kasa CI / Build and smoke-test Windows MAUI app** işi `.github/workflows/windows-validation.yml` akışını çağırır. Akış uygulamayı Windows x64 için kendine yeterli yayımlar, **aynı çıktıdaki** `Kasa.App.exe` dosyasını ulaşılamayan yerel API adresiyle açar ve tüm klasörü ZIP olarak yükler. Paket adı `kasa-windows-<sürüm>-win-x64` olur. Ayrı bir duman testi derlemesi dağıtım paketinin yerine kullanılmaz.

**iOS unsigned validation** PR ve push üzerinde Xcode 27.0 ile Release / ios-arm64 derler. Apple hesabı, sertifika veya API anahtarı gerekmez. İmzasız derlemenin geçmesi, uygulamanın iPhone’da açıldığını veya TestFlight’a kabul edileceğini kanıtlamaz.

Bu iki kontrolün başarıyla çalıştığı koşu ve kaynak commit’i doğrulanmadan “iki platform da hazır” sonucu çıkarılmaz. İmzalı iOS akışı için Apple yapılandırması, Windows paketi için temiz Windows üzerinde ilk kullanım kontrolü kalır.

## Windows ZIP hazırlama

GitHub Actions → **Windows desktop release** → **Run workflow**. `create_draft_release` varsayılan olarak kapalıdır; koşu yalnız indirilebilir ZIP ve SHA-256 üretir. Bu elle çalıştırma CI ile aynı ortak paketleme işini çağırır. Açıkça seçilirse ayrı ve yazma yetkisi olan iş yeni bir **taslak** GitHub sürümü oluşturur; mevcut sürümü değiştirmez.

Tam klasörü çıkarıp `Kasa.App.exe` açılır. Paket x64 içindir ve EXE imzalı değildir. [Adımlar ve kabul kontrolü](windows-exe.md).

## iOS TestFlight hazırlama

GitHub Actions → **iOS TestFlight release** → **Run workflow**:

- `compile-only`: varsayılan, yalnız imzasız derleme.
- `build-ipa`: geçerli dağıtım sertifikası ve App Store profiliyle imzalı IPA üretir; Apple’a yüklemez.
- `upload-testflight`: imzalı IPA üretir ve **confirm_upload** ayrıca seçilmişse Apple’a yükler.

Sertifika ve profil Windows’ta OpenSSL ile hazırlanabilir; Apple Developer ve App Store Connect adımları tarayıcıdan yapılır. Bu yayın yöntemi eski yerel Mac’in yeni Xcode’u çalıştırmasına dayanmaz. [Sertifika, GitHub secrets ve TestFlight adımları](ios-yayin.md).

İmzalı seçenekler yalnız `release/2.x` dalından başlatılır; `compile-only` herhangi bir dalda kullanılabilir.

## Hazırlığın sınırı

GitHub’a dosyaları eklemek bir bulut derlemesinin geçtiği anlamına gelmez. İmzalı IPA ve Apple yüklemesi için gerçek hesap bilgileri gerekir. TestFlight’ın işlenmiş derlemesini iPhone’a kurup oturum, PDF seçimi, satır düzenleme, finansal önizleme/onay ve yeniden açılış davranışlarını kontrol ettikten sonra kişisel iOS kullanımı için yayın tamamlanır.
