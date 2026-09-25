# Alış–kanal–ödeme eşleştirme

Tarih: 19 Eylül 2026. Durum: API, Windows istemcisi ve otomatik testlerle uygulandı. Canlıya dağıtım bu çalışmanın parçası değildir.

## Sorun ve kullanıcı kararları

- Kayıtları kesinleştiren tek editör vardır. Hangi editörün hangi rakamı değiştirdiği öncelikli sorun değildir.
- Malı alan kişi editörden farklıdır. Hangi malın hangi kanal için alındığı belirsiz kalınca ödemeler yanlış kanala gider yazılır.
- Kullanıcı, **alıcının taslak girmesini ve editörün kontrol edip kesinleştirmesini** seçti.
- Kullanıcı, **aynı alışın birden fazla kanala bölünebildiğini** doğruladı.

## Uygulanan akış

1. Alıcı alış taslağı açar: alış tarihi, malı alan kişi, tedarikçi, mal açıklamaları ve tutarlar. Her mal kalemi için hedef kanal veya kanallara ayrılan tutarlar belirtilir. Açıklama/not eklenebilir.
2. Kanalı kesin olmayan kalem taslakta açıkça işaretlenir. Tahmini olarak gerçek bir kanala veya `Ortak` etiketiyle eşit dağıtıma atılmaz.
3. Editör, inceleme bekleyen taslakta mal açıklaması, alıcı, tedarikçi, toplam ve kanal dağılımını birlikte görür. Eksik bilgiyi tamamlatır/düzeltir; dağılım tutarlıysa kesinleştirir.
4. Ödeme girilirken onaylı alış seçilir. Kanal dağılımı alıştan gelir; ödeme ekranında yeniden tahmin edilmez. Hangi alış/kalemin ödendiği görünür kalır.
5. Alışın toplamı, eşleştirilmiş ödemeleri ve kalan tutarı birlikte görülebilir. Taslak ya da onay işlemi tek başına yeni kasa çıkışı oluşturmaz.

Örnek: tek tedarikçiden alınan 100.000 TL'lik malın 60.000 TL'si MEZAT, 25.000 TL'si PERAKENDE, 15.000 TL'si TOPTAN içindir. Her tutarın hangi mal kaleminden geldiği görülebilir; tamamı ödendiğinde dağılım aynı kalır ve toplam kasa çıkışı yalnız 100.000 TL olur.

## Veri ve hesap kuralları

- Alıcı, tedarikçi ve editör ayrı alan/kavramlardır. Tedarikçi mevcut cari anlamını korur.
- Kanal ilişkileri mevcut kalıcı `KanalId` ile tutulur. Ödeme alış kimliğine ve gerektiğinde mal kalemine bağlanır.
- Mal kalemine dağıtılan kanal tutarlarının toplamı o kalemin tutarına; kalem toplamları alış toplamına eşit olmalıdır. Eksik/fazla dağılım kesinleştirilemez.
- Mevcut gider kaydı bir alışa sonradan eşleştirildiğinde ikinci gider oluşturulmaz. Kartlı alış ile kart borcu ödemesi de iki kez kasa çıkışı üretemez.
- Kanalı belirsizken para zaten çıktıysa gerçek nakit çıkışı gizlenmez. Tutar ayrı bir **kanal dağılımı bekliyor** durumunda gösterilir; kanal sonuçlarının bu tutar kadar eksik sınıflandırıldığı açık olur. `Ortak` etiketi bunun yerine kullanılamaz; mevcut motor bu etiketi aktif kanallara eşit dağıtır.
- Kesinleşmiş ve ödemesi bağlanmış alış, alıcı tarafından doğrudan değiştirilemez. Editörün düzeltmesi ödeme bağlantılarını korumalı ve toplam nakdi tekrar değiştirmemelidir.
- Alıcı taslak hazırlayıp iletebilir; kasa bakiyesi, kesinleşmiş giderler ve onay yetkisi editörde kalır. Editör Alışlar ekranından ayrı kullanıcı adı ve şifreyle alıcı hesapları açar. Alıcı yalnız kendi alışlarını görebilir; genel finansal uçlar alıcı rolüne kapalıdır.

## Uygulama kararları ve kullanım

- **Kısmi ödeme:** alışın kanal toplamlarına orantılı dağıtım ödeme öncesinde görünür. Hesap, önceki ödemeleri ve kuruş farklarını birlikte ele alır. Son ödeme her kanalın onaylı alış toplamını tam tamamlar; tek bir mal kalemini öncelikli kapatma seçeneği yoktur. Teknik olarak kümülatif D’Hondt dağıtımı kullanılır; paylar hiçbir ara ödemede negatifleşmez.
- **Giriş:** editör alıcı hesabı açar; her alıcı kendi kullanıcı adı/şifresiyle girer. Alıcıya yalnız Alışlar menüsü açılır. Şifre değişimi ve pasife alma önceki oturumları geçersiz kılar.
- **Mevcut gider:** editör ödeme bölümünden mevcut gideri seçer. Tarih, tutar ve kart bilgileri sunucuda yeniden doğrulanır; gider daha önce başka alışa bağlanmışsa işlem reddedilir. Ek kasa kaydı oluşmaz.
- **Onaydan önce ödeme:** editör taslak veya incelemedeki alışa ödeme bağlayabilir. Kanal sonuçlarına dağıtım onayla başlar; bu arada kasa çıkışı ve dağılım bekleyen tutar görünür. Kredi kartının mevcut ertelenmiş kasa kuralı geçerlidir; kart borcu ödemesi ayrıca gider oluşturmaz.
- **Kanal düzeltmesi:** editör gerekçe ile onaylı alışını taslağa iade eder, dağılımı düzeltir, yeniden gönderir ve onaylar. Ödeme bağlantıları/kimlikleri ve genel kasa korunur. Alış toplamı ödenmiş tutarın altına indirilemez. İade süresince ödemeler dağılım bekler.
- **Eşzamanlı işlem:** ekranın sürümü eskimişse yazma reddedilir. Aynı ödeme isteği tekrarlandığında tek gider kalır; farklı iki ödeme aynı kalan tutarı birlikte aşamaz.
- **Sınırlar:** bir gider tek alışa bağlanabilir. Bağlanmış giderin tutar/tarih değişikliği ve silinmesi kapalıdır; ayrı ödeme iptal/düzeltme akışı ve tek giderin birkaç alışa bölünmesi sonraki işlerdir. Stok miktarı ve birim fiyat yerine mal açıklaması ve toplam tutar girilir.

## Kabul senaryoları

- Bir alış farklı kanallardaki mal kalemleriyle taslak olarak kaydedilebilir; taslak kasayı etkilemez.
- Eksik ya da fazla kanal dağılımı onaylanamaz; belirsiz kanal Ortak olarak sayılmaz.
- Editörün onayından sonra tam ödeme, onaylı dağılımla aynı kanal toplamlarını üretir.
- Aynı ödemenin tekrar eşleştirilmesi veya onay isteğinin tekrarlanması nakdi ikinci kez düşürmez.
- Kanal adı değişikliği alış ve ödeme bağlantısını koparmaz.
- Para çıkmış ama dağılımı belirlenmemiş kayıt genel kasada ve bekleyen dağılım listesinde görünür.
- Alıcı, kesinleşmiş kaydı veya kasa işlemini editör yetkisi olmadan değiştiremez.

Stok sayımı, tam cari muhasebesi ve otomatik banka entegrasyonu bu önerinin kendiliğinden kapsamına girmez.
