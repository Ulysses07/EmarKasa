# Kasa 2.0 dağıtımı ve geri dönüş

Bu sürüm kanal/genel kasa odaklı mobil web ekranı, alış ödeme düzeltmeleri, belge içerikleri ve editör parola kaydı ekler. Cari, stok, ayrı hesap ve vade modülleri kapsam dışıdır. Önizleme şeması veri kaybı olmadan korunur; kaldırılan modüllerin uçları sunulmaz ve otomatik cari üretilmez. Canlı veriyi yerel geliştirme ortamına taşımadan geçiş sınaması sunucuda yapılabilir.

## Canlı durum: tam editör sürümü

23 Eylül 2026'da `kasa:2.0.0-editor-20260923` başarıyla yayımlandı. Normal proxy yapılandırması `deploy/nginx/kasa.emarglobal.com.conf` etkindir; kök sayfa ve API aynı uygulamadan gelir. `GET /kasa-runtime.json` tam sürümde bilerek 404 döner; geçici salt okunur kip kapalıdır. Alış/onay, ödeme düzenleme, alıcı yönetimi, belgeler, rapor indirme ve editör ayarları canlıdır. Eski JWT oturumları bir kez yeniden giriş gerektirir.

Geçişte eski veritabanı ve veri dizini değiştirilmeden korundu. Son yedekten ayrı bir veri dizini oluşturuldu; diğer bütün veri dosyaları da göreli yol ve içerik hash'i doğrulanarak taşındı. Etkin veri dizini, önceki yapılandırmalar ve son geçiş yedeği `/opt/kasa/releases/20260923-editor/editor-published.json` içinde kayıtlıdır. Eski gelir tekrarları birleştirilmedi veya silinmedi; yalnız yinelenen dönem/kanal grupları salt okunur olarak korunur.

Yayın öncesi ve son bakım snapshot'ında eski satır/sütun koruması, beş migration, SQLite bütünlüğü, ilişkiler ve bütün geçmiş haftalık/aylık/genel kasa/kanal raporlarının eşitliği sunucu içinde doğrulandı. Yedek ZIP'i ayrı dosyaya geri yüklenerek aynı kontrollerden geçti. Canlıya deneme finansal kaydı eklenmedi. HTTPS kök, editör girişi, ayarlar ve alış erişimi başarılıdır. Core 79 + API 146 + ApiClient 59 + App.Core 75 + web 33 = **392 test** geçti; Windows Release derlemesi 0 hata/0 uyarı verdi.

### Geçmiş: geçici kasa görüntüleme kipi

23 Eylül'deki ilk geçiş denemesi eski yinelenen gelirler nedeniyle durmuştu. Bu sırada `kasa:2.0.0-sade-20260923` canlı API olarak yayımlanmadı; `kasa.emarglobal.com.readonly.conf` üzerinden `/var/www/kasa-web/current` statik arayüzü eski API ile kullanıldı. Runtime JSON `{"saltOkunur":true,"surum":"1.0-web"}` idi. Bu geçici düzen tam editör yayınıyla sona erdi. Önceden derlenen geniş kapsamlı `kasa:2.0.0-20260923` imajı da güncel ürünü temsil etmez ve yayımlanmamalıdır.

## Yayından önce

1. Dört .NET test projesini, Windows Release derlemesini ve web testlerini çalıştır.
2. Çalışan SQLite kaynağından `sqlite3.Connection.backup` veya SQLite Backup API ile tutarlı yedek al; yalnız `.db` dosyasını canlıyken kopyalama. Yedeği sunucuda erişimi sınırlı dizinde tut.
3. Yeni Docker imajını ayrı etiketle oluştur. Mevcut imaj kimliğini geri dönüş için sakla.
4. Yeni uygulamayı **yedekten oluşturulan ayrı veritabanı** ile, yalnız localhost'taki ayrı portta çalıştır. Geçiş öncesi ve sonrası eski tabloların bütün satırlarını karşılaştır; değişmesi beklenen kanal kimliği alanlarını ve yeni alanları ayrıca denetle. Kayıt sayısı/toplamı tek başına veri bütünlüğü kanıtı değildir.
5. Sağlık, giriş, yetki sınırları, ana web sayfası ve kasa/alış/ayar okuma uçlarını sına. Eski ve yeni raporları bütün dönem ve kanal sütunlarıyla sunucu içinde karşılaştır; finansal içerikleri dışarı yazma. Deneme konteynerini kaldır; canlı veriye deneme alış/ödeme kaydı ekleme.

## Yayın

- Kullanılan dosya `deploy/docker-compose.nginx.yml` dosyasıdır. Yeni sürüm `/data` kalıcı verisinin yanında `/yedekler` kalıcı alanını kullanır. Otomatik yedekleme günlük; son 30 dosya saklanır.
- Sunucudaki etkin Compose `/data` için mutlak `/opt/kasa/deploy/kasa-data-editor-<stamp>` yolunu kullanır; kesin yol yayın manifestindedir. Depo şablonundaki `./kasa-data` eski, korunmuş veritabanını gösterir. Sonraki dağıtımlarda etkin `/data` bağlantısını koru; sunucu Compose dosyasını şablonla doğrudan değiştirme.
- Uygulama içindeki tüm belgeler SQLite yedeğinin içindedir. `Kasa__JwtKey` ve diğer ortam sırları ayrı korunmalıdır; yedek ZIP'ine konmaz.
- Bakım ekranını açıp servisi durdur; son tutarlı snapshot'ı ve veritabanı dışındaki dosyaları yeni bir veri dizinine aktar. Eski dizini değiştirme. Dosya yolları/hash'leri ve son snapshot üzerinden eski/yeni raporlar eşleşmeden trafiği açma. Public erişim açılmadan hata olursa önceki imaj ve korunmuş eski dizine dönülebilir. Public erişim açıldıktan sonra eski yedeği **otomatik geri koyma**; yeni kullanıcı kayıtlarını koruyarak incele.
- Uygulama saat dilimi `TZ=Europe/Istanbul` olarak tanımlıdır; doğrulama konteynerleri aynı saat dilimini kullanır.
- Nginx `client_max_body_size 11m` olmalı; `nginx -t` başarılı olmadan yeniden yükleme.
- HTTPS ana sayfa, `/health`, giriş ve yeni API uçlarını gerçek alan adıyla doğrula.
- `Kasa__IndirmeAdresi` varsa yalnız HTTPS Windows indirme bağlantısı verilir. Uygulama kendiliğinden dosya indirip çalıştırmaz.

## Geri yükleme denemesi

Editör Ayarlar'dan yedek ZIP dosyasını indirebilir. Şu komut dosyayı doğrulayıp **yeni** bir veritabanına geri açar; mevcut dosyanın üzerine yazmaz:

```sh
python3 deploy/restore_backup.py /safe/kasa-....zip --output /safe/recovered.db
```

Araç manifest sürümünü, SHA-256 değerini, SQLite bütünlüğünü, yabancı anahtar ilişkilerini ve beklenen şema sürümünü kontrol eder. Canlı geri yükleme sırasında servis durdurulmalı, mevcut veri ayrıca korunmalı ve sonrasında giriş/rapor denemesi yapılmalıdır.

Sunucu içindeki ikinci dizin, tüm VPS kaybına karşı yedek değildir. Düzenli ZIP indirmesi veya kurumun sunucu dışı yedek alanına kopyalama da sürdürülmelidir. Üçüncü taraf bir depolama hesabı bu proje tarafından kendiliğinden oluşturulmaz.
