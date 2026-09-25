# Emar Kasa 2.3 — PDF ekstre ve hesap hareketleri

## Kullanım

Editör menüsündeki **Ekstre / Hareket Yükle** bölümünde PDF, belge türü ve banka seçilir. Kart ekstresinde uygulamadaki kart; banka hareketinde hesabı ayırt etmek için kısa bir ad girilir. Bu ad bir banka hesabı veya cari modülü oluşturmaz.

VakıfBank, Akbank, QNB, İş Bankası, Garanti BBVA ve DenizBank seçenekleri bulunur. Belgeden tanınan hareketler seçimsiz listelenir; faiz, komisyon, vergi, ücret ve transfer adayları işaretlenir. Tarih, tutar, işlem türü ve kanal dağılımı kontrol edilerek yalnız istenen satırlar seçilir. Kesin okunamayan bilgiler düzeltilmelidir. Toplam, devir, limit ve ekstre bilgi satırları yeni işlem sayılmaz.

**Önizleme**, genel kasa etkisini ve kanal paylarını gösterir. Kaydet yalnız seçili satırları, tek işlem halinde yazar. Önizleme sonrası bilgi değişirse yeniden önizleme gerekir. Benzer kayıt, belirsiz para birimi/yön veya eski kart avansının dağılım değişikliği ayrıca onay ister. Kendi hesapları arasındaki transferler başlangıçta atlanır; önceden kaydedilen kredi taksitleri ve kart ödemeleri tekrar seçilmemelidir.

| İşlem | Kasa ve kart etkisi |
| --- | --- |
| Banka girişi | Genel kasayı artırır; yalnız genel kasa veya seçilen kanallar |
| Banka çıkışı / komisyon / ücret | Genel kasayı azaltır; yalnız genel kasa veya seçilen kanallar |
| Kart harcaması / faiz / masraf | Kart borcunu artırır; yeni nakit çıkışı oluşturmaz |
| Kart borcu ödemesi | Mevcut kart borcu dağılımına göre genel kasa ve kanallardan düşer |
| Karta iade | Seçilen kaynak kart harcamasının borcunu azaltır; nakit girişi sayılmaz |

PDF üzerinden kart harcaması ve faiz/masrafın kanal paylarını editör seçer; mevcut kart borcundan otomatik oran tahmini yapılmaz. Banka giriş/çıkışında eşit veya kanal başına tutar dağılımı mümkündür. Tek karta ait belge başka kartın borcunu değiştiremez. Yeni kullanıma kapalı kartın borç ödemesi yapılabilir; yeni harcamayı sunucu engeller.

Yüklenen belgeler ve kaynak PDF korunur. Aynı dosya tekrar yüklendiğinde mevcut belge açılır. Aynı belge satırı ikinci kez kaydedilemez; başka belgedeki veya elle girilmiş benzer hareketler uyarılır. Kaynak finansal kayıt normal gider/kart ekranından değiştirilemez; belge geçmişinden gerekçeyle iptal edilir. İptal geçmişi silinmez. Düzeltme için iptal edilen satır yeniden seçilebilir. Ay kilidi ve ödenmiş kart harcamasının iptal kuralları geçerlidir. Geçmiş 50 belge halinde sayfalanır; finansal kayıttan eski belgeye doğrudan erişilebilir.

## Okuma sınırları ve veri güvenliği

- Metin içeren, şifresiz PDF; en fazla 10 MB, 50 sayfa, 1500 tanınan hareket. Tarama/fotoğraf için OCR bu sürümde yoktur.
- Yalnız TL hareketler kaydedilir. Açıkça yabancı para olan satırlar engellenir; para birimi okunamayan satır için TL doğrulaması istenir.
- Altı bankanın gerçek örnek ekstreleri henüz sağlanmadı. Banka bazında tüm PDF düzenlerinin doğru okunacağı doğrulanmış değildir. İlk belgelerde okunan satır sayısı, tutarlar ve yönler kaynak PDF ile karşılaştırılmalıdır. Anlaşılamayan bir düzen için örnek belgeyle ayrıştırıcı uyarlaması gerekir.
- Okuma sunucuda Poppler ile yapılır; belge üçüncü taraf yapay zekâ veya belge hizmetine gönderilmez. PDF ve okuma sonucu uygulama veritabanında, yedeklerle birlikte saklanır. Yalnız editör erişebilir; kaynak indirme eki `private, no-store` ve `nosniff` ile döner.
- Okuma en fazla iki eşzamanlı işlem, 25 saniye ve sınırlı çıktı ile yürür. Geçici dizin ve dosyalar işlem sonunda temizlenir. Hata mesajlarına PDF içeriği veya araç çıktısı eklenmez.

Yerel API'de `pdfinfo` ve `pdftotext` PATH üzerinde bulunmalı; Windows için gerekirse `Pdf__AracDizini` ayarlanır. Docker görüntüsü `poppler-utils` içerir. Üretimdeki Nginx yükleme sınırı 11 MB'dır; uygulama PDF'yi 10 MB ile sınırlar.

## Teknik kapsam

Minimum istemci 2.3.0. Migration10 yalnız ekstre belge/kayıt tablolarını ekler; eski mali satırları dönüştürmez. Banka gelirleri mevcut raporlara kaynak kayıtlarından katılır. Yalnız genel kasaya alınan gelir aylık raporda `genelGelir` alanıdır ve kanal bakiyelerine yazılmaz.

Önizleme gerçek mali kuralları SQLite savepoint içinde çalıştırıp geri alır. Kaydet belge sürümü, önizleme özeti ve istek kimliğiyle doğrulanır. Kaynak belge/satır, finansal kayıt bağlantıları ve mevcut ay kilidi birlikte korunur. Aynı pakette ödeme harcamadan önce seçilse de önizleme bütün satırların son dağılımını gösterir.

[API sözleşmesi](../specs/2026-09-27-ekstre-ice-aktarma-api.md). Dağıtım kontrolleri `/opt/kasa/releases/20260923-imports/` altında tutulur. Üretim karşılaştırmaları ve yedekler yalnız VPS üzerinde yapılır; üretim verileri yerel bilgisayara indirilmez.

## 23 Eylül 2026 yayın sonucu

`kasa:2.3.0-imports-20260923` canlıda. `imports-published.json` yayın manifesti, `post-publication-health.json` son sağlık kaydıdır. HTTPS, editör oturumu, sürüm/minimum istemci2.3.0 ve yayımlanan web dosyalarının kaynakla eşitliği doğrulandı. Hata ve yeniden başlama sayısı sıfır. Bildirim VAPID anahtarı önceki sürümle birebir aynı; dosya izni600.

Üretimden ayrı kopyada ve yayın öncesi son yedekte tüm eski tablo satırları/sütunları ve sıra değerleri korundu. SQLite bütünlüğü, yabancı anahtarlar ve yedekten geri yükleme doğrulandı. Eski ve yeni sürümün panel, haftalık ve tüm geçmiş aylık raporları eşit; yeni genel gelir alanı eski veride sıfır.

Temiz, yalnız sentetik verili Linux konteynerinde gerçek Poppler üzerinden PDF yükleme/okuma, üç hareketin listelenmesi, transfer önerisi, seçili banka giriş/çıkışı, önizlemenin mali kayıt bırakmaması, tekrar istek, kaynak koruması, iptal, kart faizinin kasayı değiştirmemesi, bozuk/metinsiz PDF reddi ve geçici dosya temizliği doğrulandı. Sonuç `synthetic-import-validation.json` içindedir.

691 farklı otomatik test: API tam tur269/269, son sayfalama eklemesi dahil hedefli14/14 (270 farklı API testi); Core81/81, web95/95, App.Core156/156, ApiClient89/89. Windows Release derlemesi ve publish başarılı. ApiClient/Core yerel testlerinde NuGet güvenlik metadata sorgusunda ağ kısıtı uyarısı vardı; test başarısızlığı yok.

Tarayıcıda ayrı yerel deneme verisiyle giriş, yeni menü, banka seçenekleri ve PDF yükleme formu görsel olarak kontrol edildi. Yerel Windows sandbox'ında EventLog/DataProtection yazma kısıtı, deneme panelinin yüklenmesini etkiledi; üretim Linux paneli ve geçmiş raporları ayrıca başarıyla doğrulandı. Native pencere fiziksel olarak çalıştırılmadı; VM/API istemci testleri ve MAUI derlemesi yapıldı.

Kaynak paket114 dosya; SHA256 `3fe3f554b19bdc3d019a266bcf0068f73a73f310df3f96384249980c01a1cebf`.

Windows ZIP: `artifacts/Emar-Kasa-2.3.0-Windows.zip`, 39.673.877 bayt, 426 dosya; SHA256 `606F556920F771B86D4173B163B3EA86975D4C73D504602EA118D3DCCACBC17E`. FileVersion2.3.0.0, build6. .NET10x64 gerektirir. ReadyToRun optimizasyonu kapalı paketlendi; eski paketler korundu.
