# Kişisel ekstre kuralları

**Ekstre / Hareket Yükle** ekranındaki kişisel kurallar, PDF'den okunmuş hareketlerin açıklamasına göre işlem türünü ve kasa/kanal dağılımını önerir. Kurallar sunucuda saklanır; Windows ve web aynı listeyi kullanır. Tam veritabanı yedeğine dahildir. Başlangıçta hazır kural yoktur.

## Bir seçimi hatırlatmak

1. PDF'yi yükle, bir satırın türünü ve kanal dağılımını kontrol et.
2. **Bu seçimi hatırla** düğmesini aç. Kural adını ve tekrar karşılaşacağın açıklama ifadesini düzenle; tarih, işlem numarası gibi değişen kısımları çıkar.
3. Belge türünü, bankayı ve yönü kontrol edip kuralı kaydet.
4. Önerileri satırda incele. **Öneriyi uygula** yalnız o satırın türünü/dağılımını doldurur. **Uygun önerileri uygula**, elle değiştirilen satırları korur.
5. Kaydetmek istediğin hareketleri ayrıca seç, mevcut önizlemeyi incele ve onayla.

Örnek: Banka hareketi, açıklamada `YURTİÇİ KARGO`, yön Çıkış → Banka çıkışı, seçilen kanallara eşit dağılım. `KARGO` gibi kısa bir koşul daha fazla açıklamaya uyabilir; kendi belgelerindeki ortak ifadeyi seç.

## Kural yönetimi

**Kural ekle** ile açıklama ifadesi, banka (istersen tüm bankalar), belge türü ve yön koşullarını belirle. Eşleşme için tüm koşullar birlikte aranır. Türkçe harfler, büyük/küçük harf ve noktalama sadeleştirilir; sözcük sınırı korunur: `MIGROS`, `MIGROSAN` ile eşleşmez. Düzenle, kapat/etkinleştir ve sil seçenekleri vardır. Windows'ta kapatma, düzenleme formundaki **Kural etkin** işaretini kaldırarak yapılır.

Dağılım genel kasa veya seçilen kanallara eşittir; kural sabit para tutarı saklamaz. Tek kanallı özel dağılım eşit tek kanal olarak hatırlanabilir; çok kanallı özel tutarlar için yönetim bölümünden eşit dağılımlı yeni kural tanımla. Kart harcaması için kanal seçimi gerekir. **Satırı seçmeden bırak** sonucu hareketi atlama önerisidir.

Aynı açıklamaya uyan kurallar aynı sonucu önerirse tek öneri gösterilir. Farklı sonuçlarda çelişki gösterilir ve otomatik seçim yapılmaz. Pasif/silinmiş kanallı kuralları güncelle. Kaydedilmiş satırlar yeniden öneriyle işlenmez. En fazla 200 kural ve kural başına 20 kanal saklanır.

## Okuma ve doğruluk sınırları

Bu özellik yapay zekâ veya dış servis kullanmaz. Yeni bir gelir/gider kategorisi raporu oluşturmaz; mevcut işlem türünü ve kanal dağılımını önerir. Kart ödemesi, iade ve mevcut kayıt eşleşmesi satırda seçilir. Tarih/tutar/yön/para birimi belirsizse veya PDF ayrıştırıcısı uyarı verdiyse mali öneri uygulanabilir sayılmaz; kaynak PDF'yi kontrol et.

Mevcut PDF okuyucu değişmedi: metin içeren şifresiz PDF gerekir, tarama/fotoğraf için OCR yoktur. Gerçek banka PDF örnekleri verilmediği için bütün banka düzenleri veya kişisel sınıflandırma doğruluğu için başarı yüzdesi verilemez.

Telefonda `/m/` görünümündeki **Masaüstü görünümüne geç** seçeneği ile telefona uyumlu tam web arayüzünün **Ekstre / Hareket Yükle** ekranını kullan. Eski sunucuda kural uçları yoksa açıklama gösterilir; PDF'leri elle işlemeye devam edebilirsin. Kural bağlantısı geçici olarak kesilirse yenile.
