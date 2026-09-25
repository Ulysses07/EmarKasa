# Kasa 2.1 — kart, kredi ve bildirim takibi

23 Eylül 2026 önceki güncellemesi: **2.1.1**. Güncel sürüm [2.2.0](kasa-2.2.md); bu belge önceki yayını anlatır. Kanal kart borcu, alışta kart adı, asgari ödeme durumu ve benzer kayıt uyarısı eklendi. [Kapsam ve doğrulama](../plans/2026-09-23-kasa-gorunurlugu.md). 2.1.1 yayın kaydı `/opt/kasa/releases/20260923-usability/usability-published.json`; aşağıdaki 2.1.0 yayın kaydı geçmiş sürüme aittir.

23 Eylül 2026: `kasa:2.1.0-tracking-20260923` yayına alındı. API 192, hesap çekirdeği 79, istemci 67, Windows görünüm modeli 93 ve web 49 olmak üzere 480 test geçti. Windows Release ve publish 0 hata/0 uyarıyla tamamlandı. Canlı kayıtlardaki tüm özgün alanlar korundu; panel/haftalık/aylık eski rapor tutarları birebir eşit. Yeni `krediGirisi` alanı eski kayıtlarda sıfırdır. Yayın sonrası HTTPS, oturum, yeni ekran API'leri, Web Push anahtarı, manifest ve service worker doğrulandı; yeniden başlama ve hata sayısı sıfırdı. Gerçek telefonda/masaüstünde bildirim gösterimi henüz cihaz üzerinde test edilmedi.

Sunucu yayın kayıtları: `/opt/kasa/releases/20260923-tracking/tracking-published.json` ve `post-publication-health.json`. Güncel veri klasörü yayın kaydından/container bağından okunmalıdır; önceki `/opt/kasa/deploy/kasa-data` veya editor sürümünün klasörü üzerine yeni dağıtım yapılmamalıdır. Eski veri klasörü geri dönüş için korunmuştur.

## Kullanım

- **Kredi Kartları:** Kartı ve kesim/son ödeme günlerini ekleyin. Alış/gider ekranında bu karta bağlı harcamalar otomatik görünür; aynı harcamayı yeniden girmeyin. Ödeme kaydından önce genel kasa ve kanal payları gösterilir. Harcama ve vade günü kasayı değiştirmez; kaydedilen ödeme değiştirir.
- **Krediler:** Yeni kredi için çekim, ilk taksit ve tutarları girip kullanan kanalları seçin. Birden fazla kanal seçildiğinde hem kredi girişi hem taksitler eşit bölünür. Kanallar bu kredi için sabittir. Önceden çekilmiş kredi seçeneği yeniden para girişi yaratmaz.
- **Bildirimler → Bildirim ayarları:** Telefon ve bilgisayarda ayrı ayrı “Bu cihazda bildirimleri aç” seçeneğini kullanın, ardından test bildirimi gönderin. Varsayılan saat İstanbul 09.00; değiştirilebilir. Kart için kesim günü, son ödemeden 3 gün önce ve son ödeme günü; kredi için taksitten 3 gün önce ve taksit günü bildirim oluşturulur.
- iPhone/iPad'de iOS/iPadOS 16.4+ gerekir; siteyi **Ana Ekrana Ekle** ile kurup oradan açın. Windows uygulamasındaki masaüstü bildirim düğmesi aynı sitenin tarayıcı kurulumunu açar. Windows sistem bildirimleri Edge/Chrome/Web Push üzerinden gelir.
- Eski kart/krediler kendiliğinden yeni hesaplama yöntemine geçirilmez. Kart veya kredi ayrıntısından **Geçişi incele** ile kasa farkını görün, sonra onaylayın. Geçmişi değiştirecek geriye tarihli geçiş reddedilir.

Taksitin “Kasaya işlendi” durumu, bankanın parayı tahsil ettiğini doğrulamaz. Bildirim veya dekont notu eklemek ikinci bir kasa çıkışı oluşturmaz. İade, kaynak harcamaya bağlanır; bu sürümde kaynakta henüz ödenmemiş tutarla sınırlıdır. Ödenmiş harcama iadesini başka kanala kendiliğinden mahsup etmez.

## Sunucu

Şema 6 `20260925000100_CardLoanTracking`, şema 7 `20260925000200_Notifications` eklenir. Önceki tablolar ve eski hesaplama davranışı korunur. Yeni takip kayıtları yalnız `/api/takip` üzerinden açılır; eski istemcinin eski kurallı yeni kart/kredi açması 409 ile engellenir. Minimum istemci sürümü 2.1.0'dır.

Bildirim işçisi Production ortamında dakikada bir çalışır; uygulama ekranının açık olması gerekmez. Development/test ortamında varsayılan olarak kapalıdır. `Bildirim__WorkerEtkin` ve `Bildirim__PushEtkin` açıkça ayarlanabilir. Test kopyalarında işçi kapatılmalıdır.

VAPID anahtarı ilk kullanımda veritabanının yanında `.kasa-push-keys.json` olarak üretilir; Linux dosya izni 600'dür. İstenirse `Bildirim__AnahtarDosyasi` ile kalıcı bir konum veya `Bildirim__PublicKey`/`Bildirim__PrivateKey` ile dışarıdan kimlik sağlanabilir. Kimlik her dağıtımda korunmalıdır. Özel anahtar günlüklerde veya istemci yanıtlarında bulunmaz. HTTPS gerekli; CSP, kimlik doğrulama ve aynı kaynak/CSRF kontrolleri geçerlidir.

Abonelik hedefleri yalnız bilinen tarayıcı bildirim sunucularına HTTPS olarak kabul edilir; yönlendirmeler takip edilmez. Şifre değişikliği eski cihaz aboneliklerini geçersiz kılar. Çıkış yapan tarayıcı kendi aboneliğini kapatır. Editör diğer cihazları bildirim ayarlarından kaldırabilir. Alıcı ve izleyici bildirim ayarlarına erişemez.

Kalıcı olay anahtarı kaynak, ekstre/taksit, hedef tarih ve hatırlatma aşamasını içerir. Cihaz başına tek teslim satırı, atomik süreli kilit ve sınırlı yeniden deneme vardır. Başarılı teslim geçmişi saklanır. Göndermeden hemen önce borç ve oturum yeniden kontrol edilir; geçersiz abonelik 404/410'da kapatılır. Dış bildirim hizmetinin kabulü, cihazın gerçekten gösterdiği anlamına gelmez. Cihaz çevrimdışı veya işletim sistemi bildirimi kapalıysa teslim gecikebilir; gün sonunu aşan bildirim kuyruğa alınmaz. Aynı bildirim etiketi tekrar teslimde ayrı bir uyarı yığılmasını önler.

## Yedek ve geri dönüş

2.1 yedek ZIP'i `kasa.db`, `manifest.json` ve cihaz bildirimleri açıksa `.kasa-push-keys.json` içerir. Veritabanı ve anahtar için ayrı SHA-256 doğrulaması vardır. ZIP hassas kabul edilmeli ve özel saklanmalıdır.

`deploy/restore_backup.py` 2.0 ve 2.1 yedeklerini boş bir hedef klasörüne açar. Var olan veritabanını veya farklı bildirim anahtarını ezmez. Anahtar veritabanının yanına geri açılır; özel anahtar yolu/env kullanılan sunucuda aynı kimliğin yapılandırılması gerekir. Canlıya alınması ayrı, uygulama durdurularak gerçekleştirilen dağıtım adımıdır. Yeni şemaları düşürerek finans/bildirim geçmişini silmek desteklenmez; doğrulanmış yedek ve önceki uygulama birlikte kullanılır.

## Doğrulama kapsamı

Kartın harcama/vade sırasında sıfır kasa etkisi, manuel kısmi ödeme ve kaynak kanala dağılım; sabit eşit kredi payları, aylık tarih sınırları, otomatik taksit ve notun etkisizliği; eski kaydın geçişi; tekrar gönderim/sürüm yarışları; izin sınırları; İstanbul günü, bildirim saati, tarih değişikliği, yeniden deneme, birden fazla cihaz/işçi, ödeme sonrası bastırma, abonelik iptali ve anahtarın yedekte korunması için otomatik testler bulunur. Web form ve rol testleri ile Windows derlemesi ayrıca çalıştırılır.

Kaynaklar: [WebKit iOS/iPadOS Web Push](https://webkit.org/blog/13878/web-push-for-web-apps-on-ios-and-ipados/), [MDN arka plan işleyişi](https://developer.mozilla.org/en-US/docs/Web/Progressive_web_apps/Guides/Offline_and_background_operation), [Web Push istemci kütüphanesi](https://github.com/tpeczek/Lib.Net.Http.WebPush).
