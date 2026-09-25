# Emar Kasa 2.0 kapsamı

23 Eylül 2026 kullanıcı düzeltmesi: ERP12 kullanılmaktadır. Bu uygulamanın amacı kanal kasaları ve genel kasanın görünmesi, mal alımlarının doğru kanala yansımasıdır.

## Ana akış

- Ana ekran genel kasa bakiyesini ve kanal kasalarını gösterir. Haftalık devir, aylık özet ve gelir/gider hareketleri aynı mevcut hesaplama kurallarını kullanır.
- Alıcı; tarih, ödeme yapılan yer/açıklama, mal açıklaması, toplam tutar ve kanal dağılımıyla taslak oluşturur. Bir alış birden fazla kanala bölünebilir.
- Editör taslağı kontrol eder, gerektiğinde düzeltme için iade eder ve onaylar. Alıcı yalnız kendi kayıtlarını görür.
- Taslak ve onay tek başına para düşürmez. Kayıtlı ödeme genel kasaya bir kez, onaylı kanal dağılımıyla ilgili kanallara yansır. Ödeme yapılmış ama dağılım onaylanmamışsa tutar dağılım bekliyor olarak görünür.
- Ödeme düzeltme, iptal ve yanlış alış bağlantısını taşıma açıklama gerektirir. Tekrar gönderme mükerrer gider oluşturmaz.
- İsteğe bağlı belge eki, Excel/CSV ve yazdır/PDF çıktısı, parola kurtarma ve yedekleme kasa takibini destekler.

## ERP12'de kalan işler

Cari kartları, tedarikçi borç listesi, stok/miktar/birim fiyat, vade, ayrı banka hesabı, hesap transferi ve 30 günlük ödeme planı bu uygulamanın kapsamı değildir. Yeni cari kartı oluşturulmaz; alıştaki firma adı serbest metin açıklamadır. ERP12 entegrasyonu veya otomatik veri aktarımı eklenmemiştir.

## Teknik uyumluluk

Önizleme verilerini silmemek için mevcut şema alanları ve tarihsel ilişkiler korunur. Kullanılmayan ERP uçları sunulmaz. Yeni alış talepleri kaldırılan alanları dolduramaz. Cari tablosu geçmiş şemadan kalsa da cari listesi/yönetimi yoktur ve alış kaydı cari üretmez.

Belge, güvenlik, yetki, kanal dağıtımı, ödeme tekrar denemesi ve veri geçişi kontrolleri korunur. Mobil web, Windows derlemesi ve canlı yayın ayrı ayrı doğrulanır. Genişletilmiş ERP sürümü canlıya alınmamıştır.
