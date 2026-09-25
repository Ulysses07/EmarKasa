# Emar Kasa 2.2 — aylık giderler ve kasa kontrolleri

## Kullanım

- **Aylık Giderler:** kira, maaş, fatura ve diğer düzenli giderler; ay seçimi, planlanan/ödenen/kalan tutarlar. Her şablonda yalnız genel kasa, seçilen kanallara eşit dağılım veya kanal başına tutar seçilir. Plan kasayı değiştirmez. Ödemeyi kaydet yalnız nakit/banka ödemesini kasaya işler; kredi kartıyla yapılan gider mevcut kart/alış akışından girilir. Aynı ay ve şablon için bir aktif ödeme olabilir. İptal geçmişi korunur; yeniden ödeme ayrı istekle kaydedilir.
- **Kartlar → Faiz / masraf:** bankanın bildirdiği tutar girilir. Seçilen kesilmiş ekstre ve önceki ekstrelerde kalan borç kanallarına dağıtım önizlenir; ileri taksitler ağırlığa katılmaz. Dağılımı henüz kesinleşmemiş alış varsa önce dağılım tamamlanır. Faiz/masraf kart borcunu artırır; kasayı yalnız kart ödemesi azaltır. Banka faizi otomatik hesaplanmaz.
- **Ayarlar → Kanal kasa alt sınırı:** her kanal için isteğe bağlı tutar. Aynı düşük bakiye durumu devam ederken tekrar bildirim üretilmez; kasa sınıra ulaşıp yeniden altına düşünce yeni uyarı oluşur. Mevcut uygulama/masaüstü bildirim ayarı, saat ve cihaz izni kullanılır.
- **Kasalar → Kasa karşılaştırması:** gerçek toplam tutar girilir, sistem tutarı ve fark önizlenir. Karşılaştırma geçmişe kaydedilir; bakiye kendiliğinden düzeltilmez. Önizlemeden sonra kasa değişirse yeniden karşılaştırmak gerekir.
- **Aylık → Ay kilidi:** tamamlanmış ay gerekçeyle kapatılır. Seçilen ayın sonuna kadar doğrudan ve dolaylı finansal değişiklikler sunucuda engellenir. Bir ay yeniden açılırsa o ay ve sonraki aylar açılır; bu sonuç onay ekranında belirtilir. Önceki kart avansının yeni harcamaya eşleşmesi gibi geçmiş kanal payını değiştiren işlemler de kilide tabidir.

Şablon değişikliği cari veya ileri ay için yeni sürüm oluşturur. Önceden kaydedilmiş ödemenin tutarı/dağılımı korunur. Yalnız genel kasaya yazılan yeni aylık gider aylık raporda `genelGider` olarak ayrıca gösterilir; kanallara veya dağılım bekleyenlere eklenmez. Önceki sabit giderlerin hesaplama kuralı değişmez.

## Teknik kapsam ve yayın

Minimum istemci 2.2.0. Web arayüzü aynı sürümle önbellek yeniler. Windows paketi ayrıca güncellenir.

Migration8 `20260926000100_MonthlyExpensesAndLocks` ve migration9 `20260926000200_CashControls` yeni tablolar ekler; eski finansal kayıtları değiştirmez. Aylık gider ödemelerinin kendi bölümü dışından değiştirilmesi engellenir. Gelirlerin doğrudan SQL yazımları da kilit triggerlarıyla korunur. Yeni kasa eşikleri varsayılan kapalıdır.

Sunucudaki doğrulama ve yayın kaydı: `/opt/kasa/releases/20260923-monthly/`. Önceki tablo satırları ve sütunları birebir karşılaştırılır; yalnız yeni migration satırları kabul edilir. Panel, haftalık ve tüm geçmiş aylık raporlar eski sürümle karşılaştırılır; tek yeni aylık alan olan `genelGider` eski kayıtlarda sıfır olmalıdır. Sunucu verileri ve parolalar bu karşılaştırma için yerel bilgisayara aktarılmaz.

## 23 Eylül 2026 yayın sonucu

`kasa:2.2.0-monthly-20260923` canlıda. Yayın manifesti `monthly-published.json`, son sağlık sonucu `post-publication-health.json`. HTTPS, editör oturumu, yeni API okumaları ve yayımlanan web dosyaları doğrulandı. Hata ve yeniden başlama sayısı sıfır. VAPID anahtarı önceki yayınla birebir aynı ve dosya izni600. Eski veri dizini ayrı tutuldu.

Doğrulama: API tam tur239/239; son iki yeni senaryo ve son düzeltmeler hedefli22/22 (toplam241 farklı API testi). Core81/81, web73/73, App.Core129/129, ApiClient79/79: toplam603 farklı test. Windows Release ve publish başarılı, EXE2.2.0.0. Yerel NuGet güvenlik metadata sorgusunda ağ kısıtı uyarısı alınabilir; derleme/test hatası yok.

Sunucuda veri kopyasıyla ön prova, orijinal satır/sütun ve eski sıra değerlerinin korunması, SQLite bütünlüğü/FK, yedekten geri yükleme ve tüm geçmiş rapor eşitliği doğrulandı. Yayın sırasında son yedek üzerinden aynı kontroller tekrar geçti. Gerçek tarayıcı/Windows penceresi ve fiziksel cihazda bildirim gösterimi bu turda görsel olarak test edilmedi; web davranış testleri, native derleme ve sunucu/HTTPS kontrolleri yapıldı.

Kaynak paket SHA256: `8f40139643f5ffa68c5de45f3d4087a2a8f7835d8bd53ae82a428e3f586147eb` (106 dosya). Windows paketi `artifacts/Emar-Kasa-2.2.0-Windows.zip`; .NET10x64 gerektirir.


Windows ZIP: 39.648.425 bayt, 426 dosya; SHA256 `91EB9050888EBCCE7C8AA1FE75787E92B5C489C41F52DD190DA6A544EABABAA2`. FileVersion2.2.0.0, ProductVersion2.2.0. Eski paketler korundu.
