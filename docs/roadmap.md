# Emar Kasa kapsamı

23 Eylül 2026 kullanıcı kararı: ERP12 zaten kullanılıyor. Emar Kasa'nın işi ERP işlemlerini tekrarlamak değil, **kanal ve genel kasa takibi ile alıcıdan gelen alış bilgisini doğru kanallara dağıtmaktır.**

## Temel akış

1. Alıcı, yaptığı alışın tarihini, nereden/ne alındığını ve toplam tutarlarını taslak olarak girer.
2. Editör taslağı inceler; kalemleri bir veya birden fazla kanala böler.
3. Editör onaylar veya açıklamayla alıcıya iade eder.
4. Gerçek ödeme yeni gider olarak kaydedilir ya da mevcut gider alışa bağlanır. Kısmi ödemeler ve kalan tutar aynı alış içinde görülür.
5. Kanal dağılımı tamamlanmamış ödeme genel kasada kaybolmaz; dağılımı bekleyen tutar olarak görünür. Onay, aynı ödemeyi ikinci kez gider oluşturmaz.

Alıcı malı alan kişidir; editör dağılımı ve ödeme kaydını kesinleştirir. Ödeme yapılan yerin adı serbest açıklamadır: alış kaydetmek otomatik cari veya tedarikçi kartı oluşturmaz.

## Korunan özellikler

| Alan | Davranış |
| --- | --- |
| Kanal ve genel kasa | Kanal gelirleri, giderler, haftalık devir ve aylık sonuçlar; mevcut kredi ve kart rapor kuralları korunur. |
| Alıcı ve editör | Ayrı kullanıcı rolleri, alıcının yalnız kendi taslaklarına erişmesi, editör onayı/iadesi ve çoklu kanal dağılımı. |
| Ödeme güvenilirliği | Açıklamalı düzeltme, iptal ve başka alışa taşıma; aynı isteğin yeniden gönderilmesi yeni gider oluşturmaz. Eşzamanlı değişikliklerde eski kayıt üzerine sessizce yazılmaz. |
| Belgeler | Alışa fotoğraf/belge ve ödemeye dekont; dosyalar yetki kontrolüyle açılır ve veritabanıyla birlikte yedeklenir. |
| Erişim | Windows uygulaması ve telefona uyumlu web arayüzü aynı kayıtları kullanır. |
| Dışa aktarma | Tarih ve kanal filtreli gider listesi Excel veya CSV olarak indirilir; yazdırılabilir rapor tarayıcıdan PDF kaydedilir. |
| Güvenlik ve yedek | Parola değiştirme, tek kullanımlık kurtarma kodu, alıcı yönetimi, sürüm bilgisi, günlük tutarlı yedek ve elle yedek indirme. |

## ERP12'de kalan işler

Cari/tedarikçi kartı yönetimi, toplu tedarikçi borcu ve vade takibi, miktar-birim fiyat hesabı, ayrı kasa/banka hesabı, hesaplar arası transfer, 30 günlük nakit planı ve kredi taksidinin ayrı gerçekleşme ekranı bu uygulamanın aktif kapsamından çıkarılmıştır. Bunlara ait API uçları ve kullanıcı ekranları yayınlanmaz.

Alış içindeki ödenen/kalan tutar, o alışın gider bağlantısını kontrol etmek içindir; ayrı bir cari hesap veya borç modülü değildir. Stok, e-fatura, banka entegrasyonu, çift taraflı muhasebe ve çok şirketli kullanım da kapsamda değildir.

## Hesaplama ve kullanım sınırları

- Yeni takipte kart harcaması borcu artırır; yalnız kaydedilen kart ödemesi genel ve ilgili kanal kasasından düşer. Kısmi ödemeler o kartın harcama paylarına dağıtılır. Eski kayıtlar açıkça yeni takibe geçirilene kadar eski rapor kuralı korunur.
- Yeni kredi çekimi seçilen kanallara eşit dağılır; taksit tarihinde ödeme aynı kanallardan otomatik düşer. Önceden çekilmiş kredi yalnız kalan ileri taksitleriyle eklenebilir. Bu işlem bankadan ödeme doğrulamaz.
- Bir gider tek alışa bağlanabilir. Bir gideri birden fazla alışa bölme henüz desteklenmez; yanlış bağlantı ödeme taşıma ile düzeltilir.
- Çok kanallı ödeme dışa aktarılan gider listesinde tam tutarıyla bir kez görünür. Kanal filtresi kanal payını değil, ilgili ödemenin tamamını getirir.
- Sunucudaki son 30 yedek saklanır; sunucu dışı ikinci kopya ayrıca korunmalıdır. Geri yükleme, doğrulanmış yedeğin ayrı dosyaya açılması ve uygulama durdurularak kontrollü değiştirilmesiyle yapılır.
- Sürüm bildirimi otomatik kurulum değildir. Windows paketi ayrıca güncellenir; telefondan web arayüzü kullanılır.

## Veri ve yayın

Mevcut veriyi silen bir geri dönüş yapılmaz. Önceki geliştirme denemelerinde oluşmuş şema alanları uyumluluk için kalabilir; bu, kaldırılan ERP özelliklerinin aktif olduğu anlamına gelmez. Yeni alışlar bu alanları doldurmaz. Gerekli ödeme tekrar-koruma kayıtları korunur; aksi halde iptal edilmiş bir ödemenin eski isteği yeniden gider oluşturabilir.

Geliştirme testlerinin geçmesi ile canlı yayın birbirinden ayrı doğrulanır. Veri geçişi ve geri dönüş için [dağıtım kılavuzuna](deploy/kasa-2.0.md), aktif kullanım için [ana belgeye](../README.md) bakın. Önceki geniş kapsamlı planlar tarihsel kararları gösterir; bu sade kapsamın yerine geçmez.

## Aylık giderler ve kasa kontrolleri

Kira, maaş, fatura ve diğer sabit giderler Aylık Giderler bölümünde planlanır; yalnız elle ödeme kaydedildiğinde kasaya yansır. Genel kasa, seçilen kanallara eşit pay veya özel tutarlar seçilebilir. Kart faizi/masrafı kalan borca göre önizlenir. Kanal alt sınır uyarısı, gerçek kasa karşılaştırma geçmişi ve gerekçeli ay kapatma/açma sunulur. Ayrıntılar [2.2 kılavuzunda](deploy/kasa-2.2.md).
