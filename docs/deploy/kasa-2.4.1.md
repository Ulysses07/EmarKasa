# Emar Kasa 2.4.1 — kişisel kullanım düzeltmeleri

## Canlı yayın durumu (4 Ekim 2026)

`v2.4.1` etiketi `d26c909b292cbb815b1889b5cc6f880f56f26621` commit'ini gösterir. Canlı `/health` başarılıdır; `/api/surum` yanıtında `surum` ve `minimumIstemci` **2.4.1**'dir. Çalışan API imajı `sha256:b08a048f962d5a44a44af098a96ab5ed5b5dfbce71fe36113f9bc3cc76f830c3`; yayın manifesti `/opt/kasa/releases/20261004-2.4.1/kasa-2.4.1-published.json` içindedir.

- Yükseltme öncesinde etkin veritabanı, belge deposu ve güvenlik günlüğü aynı VPS'de `/opt/kasa/releases/20261004-2.4.1/pre-upgrade` altına kopyalanıp dosya özetleri ve SQLite bütünlüğü doğrulandı. Yayından sonra `/data` ve `/yedekler` bağlamaları ile 51 tablonun kayıt sayıları değişmedi; canlı günlüğün yeni kontrol dosyası onun bayt uzunluğu ve SHA-256 özetiyle eşleşiyor.
- Canlı verinin ayrı kopyasında 2.4.1 açılışı, eski masaüstünün yedek yazma isteğinin 409 ile engellenmesi, 2.4.1 yedeğinin kesin günlük kesimi ve yedekten açılan API'de editör girişi doğrulandı. Canlı 2.4.1'den ayrıca elle yedek alınıp aynı VPS'de ayrı dizine geri açıldı: `kasa-post-2.4.1-20261004T201906Z-158b5496.zip`, SHA-256 `158b54968019c552af74174707a61725ed90553f25f79aec63160b0e5e7b3e93`.
- Windows paketi `Emar-Kasa-2.4.1-Windows-959989b.zip` (SHA-256 `92DE0B513B67A7632F9DCF0386C6C8FAA023840BD2162236AA23897D5F29CC41`) geliştirici çalışma alanında üretildi; sunucuda dağıtılmıyor. Gerçek Windows arayüzünde giriş, sayfa geçişleri ve ekran okuyucu; farklı bankaların gerçek PDF'leri bu yayında elle sınanmadı. Sunucu dışı yedek henüz yapılandırılmadı; veri girilmeden önce ayrı depolama hedefi gereklidir.

**Geri dönüş imajı:** Yükseltmeden önceki özgün 2.4.0 imaj kimliği, yeni imajla konteyner yeniden oluşturulunca Docker'da korunmadı. Bunun yerine manifestteki `v2.4.0` commit'inden `kasa:2.4.0-rollback` imajı yeniden üretildi (`sha256:1e7d743baa12f9f77356e2cd6a216cbd98a829507ed0daf6afddb69fdaa590c2`). Geri dönüşte `imaj-onceki.txt` içindeki artık bulunmayan kimlik yerine manifestteki `rollbackImageId` kullanılmalı; günlük ve kontrol dosyası için aşağıdaki kural ayrıca geçerlidir.

## Değişiklikler

- Yedekler, güvenlik günlüğünün o andaki bayt konumunu ve SHA-256 özetini yedek veritabanında saklar. Geri yükleme, şifre ve alıcı hesabı kararlarını bu noktadan sonraki sıraya göre uygular; sistem saatinin geri alınması kararları atlatmaz.
- Günlüğün yanındaki `guvenlik-gunlugu.jsonl.kontrol.json`, günlük sonundan biçimce geçerli kayıtların kaybolmasını da saptar. Var olan 2.4 günlükleri ilk açılışta bu kontrole alınır. Eski bölümün tamlığı kanıtlanamadığından eski yedeklerin geri yüklemesinde editör ve alıcı erişimi güvenli yönde kilitlenir; geri yükleme runbook'undaki operatör sıfırlaması gerekir.
- Eski Windows istemcilerinin sunucuya kayıt yazması durdurulur. 2.4.1 Windows uygulaması sürüm başlığı gönderir; 2.4.0 ve önceki paketler okuma yapabilir, yazma girişiminde güncelleme iletisi alır. Web çerez oturumları etkilenmez.
- Windows kabuğu başarısız sayfa geçişini kullanıcıya bildirir. Kaydedilmemiş formun çıkış onayı açılamazsa form korunur.

## Yayın sırası

1. 2.4.1 Windows ZIP'ini üretip sürümünü ve içeriğini doğrulayın. Eski paketin yazma yeteneği sunucu güncellemesinden sonra kapanacağı için yeni paketi erişilebilir tutun.
2. [Dağıtım rehberine](../../deploy/README.md) göre çalışan veri ve yedek bağlamalarını doğrulayın; bütünlüğü sınanmış yükseltme öncesi yedek ve eski imajı saklayın.
3. Canlı veritabanının ayrı bir kopyasında 2.4.1 açılışını ve geri dönüşü deneyin. Bu yama yeni EF veritabanı migration'ı içermez; güvenlik bütünlük dosyası yedek dizininde ilk açılışta oluşturulur.
4. Sunucuyu güncelleyin. `/health`, `/api/surum`, kayıt okuma, yeni Windows istemcisiyle yazma ve web çereziyle yazma akışlarını doğrulayın. `guvenlik-gunlugu.jsonl` ile `.kontrol.json` eşinin varlığını ve erişim izinlerini kontrol edin.
5. Normal bir elle yedek alıp kopyanın kesin günlük kesimini taşıdığını ve ayrı bir dizinde geri yüklenebildiğini doğrulayın. Bu tatbikatı canlı veritabanına uygulamayın.

## Geri dönüş notu

2.4.0 sunucusu bütünlük dosyasını okumaz. 2.4.1'den 2.4.0'a geri dönüldüğü sırada güvenlik kararı yazılırsa, sonraki 2.4.1 açılışında günlük ve bütünlük dosyası uyuşmaz; uygulama güvenli yönde açılışı durdurur. Yalnız bu durum doğrulanırsa [geri yükleme runbook'undaki](operasyon-runbook.md) dosyaları koruma ve bütünlük kapsamını yeniden başlatma prosedürünü izleyin. Bu işlemden önceki yedekler kimlik erişimini güvenli yönde kilitler. Nedeni belirsiz bir uyuşmazlıkta bütünlük dosyasını kenara taşımayın.
