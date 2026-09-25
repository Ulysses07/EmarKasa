# Kasa veritabanını veri koruyarak yükseltme

Bu kılavuz, 19 Eylül 2026 sağlamlaştırma sürümü ve sonrasındaki başlangıç sürecini açıklar. Yerel testler tamamlanmıştır; canlı veritabanında işlem veya dağıtım yapılmamıştır.

## Başlangıç davranışı

API `KasaDatabaseInitializer.Initialize(db)` çağırır:

- Boş veritabanına EF migrations ile güncel tablolar kurulur.
- Migration geçmişi olmayan desteklenen `EnsureCreated` tabloları tek transaction içinde güncellenir. Eksik kart/kredi tabloları eklenir; kayıt kimlikleri, tutarlar ve mevcut ilişkiler korunur.
- Kanal ilişkileri kalıcı `KanalId` ile bağlanır. Eski hareketlerdeki sahipsiz kanal adları pasif kanal olarak korunur; `Ortak` ve `__KREDI__` özel etiket olarak kalır.
- Aynı dönem+kanalda birden fazla gelir, yinelenen kanal adı, kopuk ilişki veya tanınmayan özel şema varsa işlem geri alınır ve uygulama açıklayıcı hatayla durur. Kayıtlar otomatik birleştirilmez veya silinmez.
- Sonraki açılışlarda uygulanmış migration tekrarlanmaz. Yeni şema değişiklikleri yeni migration olarak eklenmelidir; ilk migration dosyaları sonradan değiştirilmemelidir.
- `20260919000200_PurchaseWorkflow`, ilk şemadan sonra alıcı, alış, kalem, kanal dağılımı ve ödeme bağlantı tablolarını ekler. Mevcut giderler otomatik olarak alışa dönüştürülmez; editör gerektiğinde eşleştirir. Geçmiş gider kimlikleri ve tutarları korunur.

## Canlıya geçiş sırası

1. Mevcut uygulama sürümünü/imajını ve ortam yapılandırmasını geri dönüş için saklayın.
2. Tutarlı bir SQLite yedeği alın. Uygulama çalışıyorsa SQLite'ın yedekleme yöntemini kullanın. Dosya kopyası kullanılacaksa önce uygulamayı durdurun; ana DB ile varsa WAL/SHM dosyalarını içeren veri dizininin tamamını birlikte kopyalayın.
3. Yedeği üretimden ayrı bir dizine geri yükleyin. Yeni sürümü yalnız bu kopyaya bağlayarak başlatın. Test için ayrı bağlantı, dinleme adresi ve kimlik ayarları kullanın.
4. Geçiş sonrası kayıt sayılarını, bilinen dönem gelir/gider toplamlarını ve kanal bağlantılarını karşılaştırın. Ay ortasında kart giderlerinin artık erken düşmediğini hesaba katın: bu düzeltme bazı önceki yanlış kasa sonuçlarını değiştirir.
5. Kopya üzerinde geçiş ve geri yükleme doğrulandıktan sonra yeni sürümü yayınlayın. API sağlık, giriş, panel ve aylık/haftalık rapor uçlarını kontrol edin.

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
