# Emar Kasa 2.4.1 — kişisel kullanım düzeltmeleri

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
