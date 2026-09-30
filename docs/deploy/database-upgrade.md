# Kasa veritabanını veri koruyarak yükseltme

Bu kılavuz, 19 Eylül 2026 sağlamlaştırma sürümü ve sonrasındaki başlangıç sürecini açıklar. Yerel testler tamamlanmıştır; canlı veritabanında işlem veya dağıtım yapılmamıştır.

## Başlangıç davranışı

API `KasaVeritabaniBaslatici.Baslat(db)` çağırır:

- Boş veritabanına EF migrations ile güncel tablolar kurulur.
- Migration geçmişi olmayan desteklenen `EnsureCreated` tabloları tek transaction içinde güncellenir. Eksik kart/kredi tabloları eklenir; kayıt kimlikleri, tutarlar ve mevcut ilişkiler korunur.
- Kanal ilişkileri kalıcı `KanalId` ile bağlanır. Eski hareketlerdeki sahipsiz kanal adları pasif kanal olarak korunur; `Ortak` ve `__KREDI__` özel etiket olarak kalır.
- Aynı dönem+kanalda birden fazla gelir, yinelenen kanal adı, kopuk ilişki veya tanınmayan özel şema varsa işlem geri alınır ve uygulama açıklayıcı hatayla durur. Kayıtlar otomatik birleştirilmez veya silinmez.
- Sonraki açılışlarda uygulanmış migration tekrarlanmaz. Yeni şema değişiklikleri yeni migration olarak eklenmelidir; ilk migration dosyaları sonradan değiştirilmemelidir.
- `20260919000200_PurchaseWorkflow`, ilk şemadan sonra alıcı, alış, kalem, kanal dağılımı ve ödeme bağlantı tablolarını ekler. Mevcut giderler otomatik olarak alışa dönüştürülmez; editör gerektiğinde eşleştirir. Geçmiş gider kimlikleri ve tutarları korunur.
- Göç öncesi yedek: dosya veritabanında bekleyen iş (eski şema köprüsü, migration ya da veri adımı) varsa başlatıcı hiçbir şeyi değiştirmeden önce yedek dizinine (`Yedek:Dizin`, canlıda `/yedekler`) `kasa-goc-oncesi-YYYYMMDD-HHMMSS-xxxxxxxx.zip` yazar; biçim olağan yedekle aynıdır ve `restore_backup.py` ile açılır. Yedek alınamaz ya da doğrulanamazsa (`integrity_check`) migration çalışmaz, uygulama açıklayıcı hatayla durur. Aynı veritabanı için ikinci kez yazılmaz; rotasyon bu dosyaları silmez, gereksiz olanları operatör kaldırır.
- `20261002000100_BelgeDeposuHazirlik` + veri adımı + `20261002000200_BelgeDeposu`: alış belgeleri ve ekstre PDF'leri veritabanından belge deposuna taşınır (aşağıda "Belge deposu geçişi").
- `20260929000300_AyRaporAnlikGoruntuleri` kilitli ayların rapor görüntüsü tablosunu ekler; aynı açılıştaki veri adımı o anda kilitli ayların raporunu eski kuralla (kural 1) dondurur. Kapatılmış ayların rakamları değişmez; açık aylarda takipli kredi çekimi "Gelen" ve "Ay sonucu" yerine ayrı "Kredi girişi" alanında gösterilir.

## Belge deposu geçişi (2.4)

Bu sürümden önce alış belgeleri (`Belgeler.Icerik`) ve ekstre PDF'leri (`EkstreBelgeler.Dosya`) `kasa.db` içinde BLOB olarak duruyordu; her yedek bütün belgeleri yeniden kopyalıyordu. Artık içerikler **belge deposundadır**: `Belge:Dizin` (compose'da `/data/belgeler`, yani `KASA_DATA_DIR/belgeler`; ayar verilmezse veritabanı dosyasının klasörü altında `belgeler`). Dosya adı içeriğin SHA-256 özetidir (`belgeler/<ilk iki hane>/<özet>`); veritabanı yalnız özeti tutar. Aynı içerik tek dosyadır; belge silme yumuşak olduğundan depodan dosya silinmez (yalnız hiçbir kaydın göstermediği, yazılıp kaydedilemeyen ve 24 saatten eski yüklemeler yedek sırasında temizlenir).

Geçiş ilk açılışta, HTTP açılmadan ve otomatik olarak şu sırayla yapılır (uygulama günlüğünde her adım yazılır):

1. **Göç öncesi yedek** (`kasa-goc-oncesi-*.zip`, yedek dizininde): içerikleri BLOB olarak taşıyan eski biçim (manifest 2.1.0); geri dönüşün kaynağıdır. Bekleyen işler listesinde "Belge içeriklerinin belge deposuna aktarımı: N alış belgesi, M ekstre PDF'i (X MB)" görünür.
2. **Disk denetimi**: belge deposunun diskinde taşınacak içeriklerin toplamı + 256 MB yoksa açılış durur; veritabanı değişmez.
3. **Ekstre PDF'leri**: her biri depoya yazılır ve özeti kayıtlı `DosyaOzeti` ile karşılaştırılır. Tek bir uyuşmazlıkta açılış "… içeriği kayıtlı özetle eşleşmiyor … veritabanı değiştirilmedi" iletisiyle durur (hiçbir migration uygulanmaz); göç öncesi yedeği ve o belgeyi inceleyin, veritabanını silmeyin.
4. `20261002000100_BelgeDeposuHazirlik`: özet ve yumuşak silme/yükleyen izi sütunları eklenir (mevcut satırların değerleri değişmez).
5. **Alış belgeleri**: her içerik akışla depoya yazılır (fsync + atomik taşıma), geri okunup özeti doğrulanır ve satırın özeti tek satırlık transaction'da kaydedilir. Süreç yarıda kalırsa (kesinti, disk dolması) yeniden başlatma kaldığı yerden sürer; depoda zaten bulunan içerik yeniden yazılmaz.
6. `20261002000200_BelgeDeposu`: özeti boş belge kalmadığı SQL içinde denetlenir (kalmışsa migration geri alınır), BLOB sütunları düşürülür, özet dizini ve "özet zorunlu" tetikleyicileri kurulur. Kimlikler, sayaçlar (`sqlite_sequence`), ilişkiler ve bütün diğer sütunlar aynen kalır; raporlar ve indirilen belgeler bayt bayt aynıdır.
7. **VACUUM** (bir kez): dosya yeniden yazılır, silinmiş belgelerin serbest sayfalarda kalan içerikleri de gider ve `kasa.db` belgelerin toplamı kadar küçülür.

Süre ve disk:

- Disk G/Ç'si yaklaşık veritabanı boyutunun 10 katıdır (göç öncesi yedek, depoya yazma ve doğrulama, satır güncellemeleri, sütun düşürme, VACUUM); VPS SSD'sinde GB başına birkaç dakika sürer ve açılış bu sürede HTTP'ye kapalıdır (nginx 502 döner). Geçişten önce `du -sh <KASA_DATA_DIR>/kasa.db` ile boyutu ölçüp bakım aralığını ona göre seçin.
- Geçici ek alan: yedek dizininde göç öncesi yedek (veritabanı boyutu kadar; yazılırken kısa süre iki katı), veri dizininde belge deposu (belge içeriklerinin toplamı) ve VACUUM için küçülmüş veritabanı boyutu kadar. Geçişten sonra `kasa.db` belgelerin toplamı kadar küçülür; göç öncesi yedek rotasyonla silinmez, uzak kopyası doğrulandıktan sonra operatör kaldırır.
- Veri dizini taşınırken ya da elle yedeklenirken `belgeler/` klasörü `kasa.db` ile birlikte kopyalanmalıdır; yalnız `kasa.db` belgeleri içermez.

Geri dönüş (yeni sürüm sorun çıkarırsa): uygulamayı durdurun; göç öncesi yedeği `restore_backup.py` ile **yeni** bir veri dizinine açın (`python3 deploy/restore_backup.py <yedek-dizini>/kasa-goc-oncesi-….zip --output <yeni-veri-dizini>/kasa.db`; bu eski biçim belgeleri `kasa.db` içinde taşır), `KASA_DATA_DIR`'i o dizine çevirin ve [deploy/README.md](../../deploy/README.md) "Geri dönüş" adımlarıyla **önceki imajı** başlatın. Yeni sürümün `belgeler/` klasörünü silmeyin; önceki sürüm onu kullanmaz, yeniden yükseltmede içerikler zaten depoda bulunur. Göç öncesi yedekten sonra girilen kayıtlar geri dönüşte yoktur.

## Canlıya geçiş sırası

1. Mevcut uygulama sürümünü/imajını ve ortam yapılandırmasını geri dönüş için saklayın.
2. Tutarlı bir SQLite yedeği alın. Uygulama çalışıyorsa SQLite'ın yedekleme yöntemini kullanın. Dosya kopyası kullanılacaksa önce uygulamayı durdurun; ana DB ile varsa WAL/SHM dosyalarını içeren veri dizininin tamamını birlikte kopyalayın.
3. Yedeği üretimden ayrı bir dizine geri yükleyin. Yeni sürümü yalnız bu kopyaya bağlayarak başlatın. Test için ayrı bağlantı, dinleme adresi ve kimlik ayarları kullanın.
4. Geçiş sonrası kayıt sayılarını, bilinen dönem gelir/gider toplamlarını ve kanal bağlantılarını karşılaştırın. Ay ortasında kart giderlerinin artık erken düşmediğini hesaba katın: bu düzeltme bazı önceki yanlış kasa sonuçlarını değiştirir.
5. Kopya üzerinde geçiş ve geri yükleme doğrulandıktan sonra yeni sürümü yayınlayın. API sağlık, giriş, panel ve aylık/haftalık rapor uçlarını kontrol edin.

Kopya denemesini canlı Compose projesiyle yapmayın: şablondaki sabit `kasa-app` konteyner adı ve `127.0.0.1:8080` portu canlı konteynerle çakışır. Deneme konteynerini ayrı adla, ayrı localhost portunda ve kopya dizinini `/data`'ya bağlayarak `docker run` ile başlatın.

Yayında `/data` bağlaması yalnız `deploy/.env` içindeki `KASA_DATA_DIR` değerinden gelir; değer tanımsız veya boşsa Compose durur. Yayından önce bu değerin çalışan konteynerin `/data` kaynağı ve yayın manifestinin `dataDirectory` alanıyla aynı olduğunu `docker compose ... config` ile doğrulayın ([deploy/README.md](../../deploy/README.md) "Güncelleme"). `/opt/kasa/deploy/kasa-data` 2.0 öncesinden korunmuş eski veritabanıdır. Migration geçmişi olmadığından yanlışlıkla bağlanırsa başlatıcı onu hata vermeden eski şema olarak yerinde yükseltir ve korunmuş kopya kalıcı olarak değişir.

Geçiş hatası varsa hata mesajındaki verileri yedek üzerinde inceleyin. Canlı DB'yi silerek sorunu aşmayın. Geri dönüş gerekirse eski uygulama sürümüyle birlikte eşleşen yedeği geri yükleyin; eski kodu yeni şema üzerinde çalıştırmayı varsayılan geri dönüş yöntemi olarak kullanmayın.

## Kullanıcılara yansıyan değişiklikler

- Mevcut oturumlar bir kez yeniden giriş gerektirir. İzleyici şifresi değiştiğinde eski oturumlar artık geçersiz olur.
- Hareketi veya açılış bakiyesi olan kanallar silinemez; pasifleştirilebilir. İsim değişikliği geçmiş hareketleri aynı kanal kimliğinde tutar.
- Hareket kaydı varken takip başlangıcı değiştirilemez; dönem gelirlerinin rapordan kopması engellenir.
- Geçersiz kredi, kart ve gelir girdileri kaydedilmeden alan bazlı hatayla reddedilir.
- Editör Alışlar ekranından alıcı hesabı açabilir. Şifre değişimi veya pasife alma alıcının eski oturumlarını iptal eder; yeniden aktifleştirmek eski oturumları geri açmaz.
- Alışa bağlanan gider genel işlem ekranından değiştirilip silinemez. Dağılım düzeltmesi alışın iade/düzenleme/yeniden onay akışından yapılır.

## Testler

`Kasa.Api.Tests/DatabaseMigrationTests.cs`, eski ve boş veritabanı, kimlik/tutar koruma, özel etiketler, benzersizlik, FK davranışı, tekrarlı/eşzamanlı başlangıç ve hatada geri alma senaryolarını kapsar. Üretim yedeğinin kopyasıyla yapılacak kontrol, bu testlerin tamamlayıcısıdır.
