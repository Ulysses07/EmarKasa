# Kasa 2.1.1 — mevcut ekranlarda dört iyileştirme

Kullanıcı dört iyileştirmeyi onayladı. Rapor ve istatistikler için bu aşamada öneri istedi; yeni rapor modülü kapsamda değil. ERP12 dışındaki cari/stok işlevleri eklenmez.

23 Eylül 2026: 2.1.1 yayına alındı. Yayın kaydı `/opt/kasa/releases/20260923-usability/usability-published.json`. Canlı veri klasörü her dağıtımda bu kayıttan veya container'ın `/data` bağından bulunmalıdır. Önceki veri klasörü ve bildirim kimliği korundu.

## Uygulanan davranış

- Kasalar ekranında kanal bakiyesinin yanında, bütün kartlardaki o kanala ait kalan kart borcu ayrı görünür. Kart ayrıntısında da kanal borcu payları vardır. Bu bilgi ikinci bir kasa çıkışı oluşturmaz. Bilinmeyen paylar ve eski takipsiz kart borçları ayrı gösterilir. Bir karttaki alacak başka kartın borcunu azaltmaz.
- Alış ödeme satırında kullanılan kartın güncel adı görünür. Finans erişimi olan kullanıcı kart ayrıntısına geçebilir; alıcı kendi alışında yalnız kart adını görür.
- Banka ekstrenizden girilen asgari tutarın ne kadarının kaldığı, uygulamadaki aktif ödeme kayıtlarına göre gösterilir. Banka doğrulaması yapılmaz. İade banka ödemesi sayılmaz; asgari kalan tutar gerçek kalan ekstre borcunu aşmaz.
- Yeni gider, alış ödemesi, kart harcaması ve kart ödemesinde aynı tarih/tutar/kart veya ilgili kanalda benzer kayıt varsa uyarı çıkar. Kullanıcı vazgeçebilir veya açıkça ayrı işlem olarak devam edebilir. Mevcut gideri alışa bağlama ve düzenleme ikinci kayıt oluşturmadığından kapsam dışıdır. Form değişirse kontrol yenilenir; belirsiz ödeme yanıtını tekrar denemek mevcut tekrar gönderim anahtarını korur.

Mevcut kasa ve ödeme dağıtım algoritmaları, geçmiş hareketler ve veritabanı şeması değişmez. Kanal kimlikleri raporun aynı veritabanı okumasından alınır. Yeni benzerlik uç noktası yalnız editöre açıktır; veri yazmaz.

## Sözleşme ekleri

- KartTakipDto.KanalKartBorclari ve TakipOzetDto.KanalKartBorclari: kanal kimliği, ad ve kalan tutar; bilinmeyen paylarda kimlik null.
- TakipOzetDto.KartBorcu: kartların pozitif borçları toplamı. KartAlacakBakiyesi ayrı pozitif tutardır.
- KartEkstreDto.AsgariKalan: asgari girilmemişse null; diğer durumda min(kalan borç, max(0, asgari − kayıtlı ödeme)).
- AlisOdemeDto.KrediKartiAdi ve KanalBakiye.KanalId: geriye uyumlu bilgi alanları.
- POST /api/islemler/benzerlik: Gider / AlisOdeme / KartHarcama / KartOdeme türü, tarih, tutar ve ilgili kart/kanal/alış bilgisi. En çok 10 olası benzer kayıt; hiçbir kaydı silmez veya engellemez.

API ve Windows sürümü 2.1.1; minimum istemci 2.1.0 kalır. Web ve Windows aynı sunucu hesaplarını kullanır.

## Raporlama önerileri — henüz uygulanmadı

1. **Kanal karşılaştırması:** Seçilen ayda giriş, çıkış, net kasa katkısı ve önceki aya göre değişim. Kredi girişi faaliyet gelirinden ayrı gösterilmeli; sonuç kâr diye adlandırılmamalı.
2. **30 günlük ödeme yükü:** Mevcut 7/30 günlük ödeme listesini kanal paylarıyla zenginleştirme. Kartlarda tam ekstre / girilmiş asgari tutar senaryoları ve kredilerde planlı taksitler. Gelir tahmini yapılmayan tutar açıkça belirtilmeli; kesim olayı ödeme gibi iki kez toplanmamalı.
3. **Kasa ve borç eğilimi:** Son 3/6/12 ayda genel ve kanal kasalarının yönü. Kart borcu geçmişi, yalnız güvenilir tarihli kayıtlarla geriye hesaplanabildiğinde eklenmeli; bugünkü borç geçmiş aylara taşınmamalı.
4. **Kart kullanım özeti:** Kart bazında dönem harcaması, kaydedilen ödeme, kalan borç, limit kullanımı ve kanal payları. Harcama ile kart ödemesi toplam giderde iki kez sayılmamalı.

İlk iki rapor öncelikli. Mevcut Haftalık/Aylık/Kart ekranlarını genişletmek yeterlidir; yeni ana menü gerekmez.

## Doğrulama

API 209/209, web 58/58, App.Core 110/110 ve ApiClient 71/71 olmak üzere 448 farklı test geçti. Kanal kimliğinin aynı okumada alınması düzeltmesinden sonra ilgili API/rapor/borç testleri 23/23 tekrar geçti. Kısmi ödeme, kuruşlar, iade, avans, iptal, eski geçiş, farklı kart alacağı, rol sınırları, benzerlikten sonra ayrı kayıt ve form tekrar denemeleri kapsanır. Windows Release/publish hatasız; `artifacts/Emar-Kasa-2.1.1-Windows.zip` hazır ve .NET 10 x64 gerektirir.

Gerçek tarayıcı bağlantısı bu oturumda sağlanamadı; web otomatik testleri ve yerel sentetik API akışı kullanıldı. 100 TL kart harcaması ve 20 TL ödeme sonucunda iki kanalın kalan borcu 48/32, kasa çıkışı 12/8 ve asgari kalan 0 doğrulandı. Benzerlik okuması veriyi değiştirmedi; aynı istek tekrarında yeni kayıt oluşmadı.

Canlı veriler yerel bilgisayara kopyalanmadı. Yayın öncesi ve son kopyalama sonrası tüm eski veritabanı satırları birebir eşit, panel/haftalık/aylık rapor tutarları aynı, sunucuda yedekten geri açma başarılı. Yeni bilgi alanları ayrıca doğrulandı. HTTPS, oturum, yeni sürüm ve cihaz bildirim anahtarının korunması yayın sonrasında kontrol edildi.
