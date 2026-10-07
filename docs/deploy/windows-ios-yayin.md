# Windows masaüstü ve iOS yayın akışı

Bu depo iki yerel istemci üretir: **Windows masaüstü uygulaması** ve **iOS uygulaması**. İkisi aynı Kasa API sunucusunu kullanır. PDF ekstre ve finansal kayıt davranışı ortak istemci mantığıyla korunur; iOS dosya seçimi, güvenli depolama ve ekran davranışları ayrıca gerçek iPhone'da kontrol edilir.

## Bulutta önce doğrulama

PR/push üzerinde **Kasa CI / Build and smoke-test Windows MAUI app** ortak Windows paketleme işini çağırır. Tek self-contained publish'ten ZIP ve Velopack Setup/full paket/feed üretilir. Dağıtılacak Setup geçici Windows runner'a gerçekten kurulur; kurulu uygulamanın açılışı kontrol edilir. Aynı binary'nin sentetik bir sonraki paket sürümüyle gerçek yerel download/apply/restart ve güncellenmiş WinUI açılışı sınanır. Sentetik paket artifact'a yüklenmez; üretim sürümü değişmez. API adresi yalnız ulaşılamayan yerel döngü adresidir.

**iOS unsigned validation** PR/push üzerinde Xcode 27.0 ile Release / ios-arm64 derler. Apple hesabı, sertifika veya API anahtarı gerekmez. İmzasız derlemenin geçmesi iPhone açılışını veya TestFlight kabulünü kanıtlamaz.

Bu kontrollerin başarıyla çalıştığı koşu ve kaynak commit doğrulanmadan iki platformun hazır olduğu sonucu çıkarılmaz. İmzalı iOS için Apple yapılandırması ve cihaz testi; Windows için kullanıcıyla ilk kullanım ve yayımlanmış feed üzerinden ilk gerçek güncelleme kontrolü kalır.

## Windows Setup ve güncelleme hazırlama

GitHub Actions → **Windows desktop release** → **Run workflow**. Varsayılan seçenekler yalnız `kasa-windows-<sürüm>-win-x64` artifact'ını üretir: Setup, full.nupkg, `releases.win.json`, alternatif ZIP ve özet dosyaları. `create_draft_release` açıkça seçilirse aynı dosyalar yeni bir GitHub taslağına yüklenir. Mevcut etiket/release değiştirilmez ve halka açık sürüm otomatik yayımlanmaz.

İlk kurulumda Setup kullanılır. Eski ZIP kullanıcıları uygulama içi güncelleme için bir kez Setup ile kurmalıdır. Kurulu uygulama yalnız yayımlanmış kararlı GitHub sürümlerini görür; taslak/prerelease görünmez. Feed ve full paket aynı yayımlanmış sürümde, orijinal dosya adlarıyla bulunmalıdır.

Setup ile kurulu Windows uygulaması başlangıçta yalnız yeni sürüm bilgisini kontrol eder. **Uygulama güncellemeleri** görünümü açık ekranın üzerinde açılır; alttaki form korunur. Kontrol ve indirme her sayfadan yapılabilir; indirme ve kurulum açık kullanıcı onayıyla başlar. **Kur ve yeniden başlat** yalnız **Haftalık rapor** veya **Giriş** ekranından kullanılabilir. Başka bir ekrandaysan önce formunu kaydedip **Haftalık rapor** ekranına dön ve oradan kurulumu onayla. İndirilmiş paket açılışta kendiliğinden uygulanmaz.

Marka geçişinde `com.emar.kasa` kimliği Windows giriş depolama konumunu değiştirir; yeniden giriş gerekebilir. Eski yerel giriş bilgileri otomatik taşınmaz/silinmez, API sunucusundaki finans kayıtları etkilenmez. Paket x64 içindir ve EXE/Setup Authenticode ile imzalı değildir. [Adımlar ve kabul kontrolü](windows-exe.md).

## iOS TestFlight hazırlama

GitHub Actions → **iOS TestFlight release** → **Run workflow**:

- `compile-only`: varsayılan, yalnız imzasız derleme.
- `build-ipa`: geçerli dağıtım sertifikası ve App Store profiliyle imzalı IPA üretir; Apple'a yüklemez.
- `upload-testflight`: imzalı IPA üretir ve **confirm_upload** ayrıca seçilmişse Apple'a yükler.

Sertifika ve profil Windows'ta OpenSSL ile hazırlanabilir; Apple Developer/App Store Connect adımları tarayıcıdan yapılır. Bu yöntem eski yerel Mac'in yeni Xcode'u çalıştırmasına dayanmaz. [Sertifika, GitHub secrets ve TestFlight adımları](ios-yayin.md).

İmzalı seçenekler yalnız `release/2.x` dalından başlatılır; `compile-only` herhangi bir dalda kullanılabilir.

## Yayının tamamlanması

Dosyaların GitHub'da bulunması bir bulut derlemesinin geçtiği anlamına gelmez. Windows Setup/update kontrolü gerçek başarılı CI koşusuyla doğrulanır. İmzalı IPA ve Apple yüklemesi gerçek hesap bilgileri gerektirir. İşlenmiş TestFlight derlemesini iPhone'a kurup oturum, PDF seçimi, satır düzenleme, finansal önizleme/onay ve yeniden açılışı kontrol ettikten sonra kişisel iOS yayını tamamlanır.
