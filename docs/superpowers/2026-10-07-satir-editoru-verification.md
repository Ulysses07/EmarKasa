# Satır düzenleyici ve kullanım hazırlığı — 7 Ekim 2026

## Kapsam

Başlangıç: `release/2.x`, `c0fb55b17eb44ac69facb014249042fceccc8de9` (ekstre kuralları PR #54 birleşmiş).
Çalışma dalı: `codex/kasa-satir-editoru-ios-hazirlik`.

- Windows MAUI: hareket düzenleyicisi listenin sonundan kaldırılıp ilgili satırın altına taşındı.
  Satır açıklaması veya inceleme düğmesi formu açar; Kapat formu kapatır. Yalnız bir satır açıktır.
  Sanallaştırılmış hücre başka satırda kullanılınca eski form kaldırılır; taslak modelde korunur.
- Tarih/tutar ve işlem/dağılım alanları geniş ekranda yan yana, dar ekranda alt alta yerleşir.
  Satır kartının dolgusu/boşlukları azaltıldı; dokunma düğmeleri en az 44 birim yüksekliğindedir.
- Web: mali seçim kutusu ile formun açılması ayrıldı. Her satır kendi erişilebilir düğmesiyle açılır.
  Aç/kapa seçimi, taslağı ve geçerli önizlemeyi değiştirmez. Geniş ekranda üç sütun, telefonda tek sütun kullanılır.
- iOS dağıtım rehberi gerçek proje hedefi, platform eksikleri ve güncel Apple araç gereksinimleriyle güncellendi.
  Bu değişiklik iOS portu veya IPA yayını değildir.

## Yerel doğrulama

- Gerçek `EkstreAktarmaPage` kaynakları, App.Core test projesinde aynı MAUI paketleriyle derlenir.
  İlk iki regresyon testi önce satır içindeki form bulunamadığı için başarısız oldu; düzeltmeden sonra geçti.
- Üç yeni MAUI görünüm testi: iki ayrı satırın açılması, mali seçimin bağımsızlığı, alanın iki yönlü bağlanması,
  kapatma/yeniden açma, hücre yeniden kullanımı, önizleme/onay korunması ve genişten dara yeniden yerleşim.
- `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj`: **1261 geçti**, 0 başarısız, 0 atlanan.
- Windows Release derlemesi: **0 hata, 0 uyarı**.
- MAUI kaynak denetimi: tanımsız kaynak yok, uzun satır tabanı içinde.
- Web Node testleri: **278 geçti**, 0 başarısız. Mali seçimden bağımsız aç/kapa, taslak/önizleme korunması,
  seçimsiz kart ödeme alanı, iade taslağının sonradan seçimde korunması, ilk iade kaynağı yüklemesi ve hatadan tekrar deneme sınandı.
- ESLint, Prettier, C# biçim denetimi ve `git diff --check` temiz.
- Playwright: **11 geçti** (2 kurulum, 9 özellik kontrolü). Masaüstü 1280 ve telefon 360/375 genişliklerinde
  yerleşim, aç/kapa, seçimden bağımsızlık, klavye erişimi, ARIA ilişkileri, yatay taşma ve axe denetimi yapıldı.
  Başlangıçta iade önerilen satırın kaynaklarını açılışta yükleme üç görünümde geçti.
  Son çıktılar yerel Temp dizinindeki `kasa-web-row-node-final.log` ve `kasa-web-row-browser-final.log` dosyalarında.

Bağımsız kaynak incelemesi, webde başlangıç türü KartIade olan satırın kaynak harcamalarının seçimsiz
düzenleme sırasında yüklenmediğini saptadı. Bu durum için ayrıca regresyon testi ve düzeltme yapıldı.
İlk GET hatasından sonra kapatıp açarak yeniden yükleme ve başarılı yüklemede eski hata metninin temizlenmesi de sınandı.
Native bağlama, hücre yeniden kullanımı ve önizleme korumasında başka doğrulanmış bulgu bulunmadı.
İlk tarayıcı koşusundaki üç iade başarısızlığı test seçicisinden kaynaklandı: iz kaydında GET 200 ve harcama
seçeneği vardı. Testler erişilebilir combobox/option rolleriyle düzeltilince geçti; bu başarısızlıklar ürün hatası sayılmadı.

## Kullanım hazırlığı ve sınırlar

- Kişisel Windows/web kullanımını kesin olarak durduran açık kritik kod hatası son kayıtlarda belgelenmiyor.
  CI ve test sonuçları bütün gerçek kullanım senaryolarının doğrulandığı anlamına gelmez.
- **Canlı sunucu dışı yedek kontrolü (7 Ekim):** `kasa-uzak-yedek.timer` ve `kasa-uzak-dogrula.timer`
  `LoadState=not-found`, `ActiveState=inactive`; `/etc/kasa/uzak-yedek.env` yok. Yalnız birim durumu ve
  dosyanın varlığı okundu; ayar içeriği okunmadı. Bu, runbook'taki otomatik uzak yedek kurulumunun eksik olduğunu doğrular.
  Başka bir sağlayıcı yedeğinin varlığı veya kullanılabilirliği bu kontrolde doğrulanmadı.
- Önemli finansal veri girmeden önce ayrı yedek hedefi kurulmalı veya kendi kendine yeterli elle yedek farklı
  cihaz/depolamada saklanmalı. Aynı VPS'deki yedek VPS kaybına karşı yeterli değildir.
- Gerçek banka PDF'lerinin tamamında tarih, tutar, yön ve satır eksiksizliği ölçülmedi. İlk gerçek belge kaynak
  PDF ile karşılaştırılmalı. Destek: TL, metin içeren ve şifresiz PDF; OCR yok.
- Native testler gerçek MAUI bağlama/yerleşim motorunu kullanır ancak platform handler'ı yoktur. Son formun gerçek
  Windows penceresinde kaydırma, odak ve klavye davranışı bu çalışmada uçtan uca çalıştırılmadı.
- **iOS bugün yayın hazır değil:** hedef, açılış DI hizmetleri, dosya türleri, yazı/simgeler ve dar ekranlar tamamlanmalı;
  Mac'te Release/imzalama ve fiziksel iPhone akışları doğrulanmalı. Ücretli Apple Developer üyeliği mevcut olduğu bildirildi;
  Mac modeli, tam macOS ve Xcode sürümü henüz kesinleşmedi.
- Sequoia 15.6+ için Xcode 26.2/26.3 araç adayı resmi kaynaklarda var; mevcut projede denenmiş bir iOS derlemesi değildir.
  Ayrıntılar ve resmi kaynaklar: [iOS rehberi](../deploy/ios-yayin.md).
