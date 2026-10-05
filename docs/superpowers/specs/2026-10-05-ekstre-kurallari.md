# Yapay zekâ olmadan ekstre sınıflandırma

## Amaç

PDF'den okunan banka/kart hareketlerinde kullanıcı kurallarıyla işlem türü ve mevcut kasa/kanal dağılımı önermek. Kullanıcı planı ve ardından uygulamayı birlikte istedi.

## Kullanıcı akışı ve kapsam

- Kuralları ekle, düzenle, devre dışı bırak, sil. Kural adı, banka/kart kaynağı, isteğe bağlı banka, açıklamadaki sözcük/ifade, isteğe bağlı yön ve hedef tür/kanallar seçilir.
- Sonuç banka gelir/gideri, kart harcaması veya “Atla / elle kontrol” olur. Dağılım genel kasa veya seçilen kanallara eşittir.
- “Bu seçimi hatırla” mevcut satırın seçimlerini kural formuna taşır; kullanıcı açıklama koşulunu ve sonucu görerek kaydeder.
- Kargo/stok gibi adlar öneride kural adı olarak görünür. Yeni gider kategorisi raporları kapsam dışıdır.
- Tekli/toplu öneri uygulama yalnız tür/dağılımı doldurur; satır seçimi ve kayıt kullanıcı onayında kalır. Toplu uygulama elle değiştirilen satırları ezmez.
- Kurallar boş başlar; örnek açıklamalardan varsayımsal finans kuralları üretilmez.

## Eşleşme ve doğruluk

- Türkçe harf/büyük-küçük harf/noktalama normalleştirilir; sözcük sınırları korunur. MIGROS, MIGROSAN ile eşleşmez.
- Birden fazla aktif kural farklı hedef önerirse çelişki gösterilir; aynı hedefte kural adları birlikte gösterilir. Liste sırası kararı değiştirmez.
- Gelir girişe, gider/kart harcaması çıkışa uymalıdır; belirsiz yön tahmin edilmez.
- Parser transfer/taksit/ödeme/iade uyarıları korunur. Atla önerilen veya uyarılı satırdaki yeni mali kayıt önerisi elle kontrol gerektirir.
- Eksik tarih/tutar, yabancı/belirsiz para, kayıtlı satır ve pasif/silinmiş kanal otomatik uygulanmaz.
- Sabit parasal özel dağılım başka tutara kopyalanmaz. Tek kanallı özel dağılım eşit dağılım olarak hatırlanabilir; çok kanallı özel dağılım yeni formda kullanıcı tarafından açıkça tanımlanır.
- Kural finans kaydı üretmez; tarih/tutar, TL, ay kilidi, önizleme, tekrar onayı ve mükerrer denetimleri korunur.

## Teknik sınırlar ve kabul

- Web (telefon dahil), Windows ve ortak istemci aynı kalıcı sunucu kurallarını kullanır.
- Dış servis/API veya yapay zekâ eklenmez; OCR kapsamı değişmez.
- SQLite kuralları mevcut tam veritabanı yedeğiyle geri gelir; göç mevcut belgeleri/finans kayıtlarını değiştirmez.
- Eski sunucuda yeni uçlar 404 ise açıklama gösterilir; mevcut PDF akışı kullanılabilir kalır.
- CRUD, Türkçe/sözcük sınırı/banka/yön eşleşmesi, çelişki, geçersiz kanal, sürüm çakışması, mali etkisizlik, eski sunucu ve seçimsizlik/elle değişiklik koruması davranış testleriyle doğrulanır. İstemci sözleşmeleri, web testleri ve Windows derlemesi doğrulanır; son bağımsız inceleme yapılır.
