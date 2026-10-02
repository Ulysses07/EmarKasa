# Masaüstü: form hataları ve bağlantı kopması

Tarih: 2026-10-02 · Durum: tasarım onaylandı, uygulama planı bekliyor · Dal: `ozellik/ux-form-baglanti` (taban `release/2.x`)

## Amaç

Masaüstü uygulamasında kullanıcının bir formu doldururken ya da sunucu bağlantısı koptuğunda şaşırmaması. Kaynak: 2026-10-02 UX
denetimi. Paket A, ürün sahibinin ilk seçtiği paket. Denetim maddeleri:

- **Form hataları:** K-02, İŞ-02, İŞ-03, AL-01, ÇK-01, KR-01, KR-04, AG-02.
- **Bağlantı:** HD-01, HD-02, HD-03.

Yalnız masaüstü (`Kasa.App`, `Kasa.App.Core`, `Kasa.ApiClient`) değişir. Sunucu, raporlar ve muhasebe sonuçları değişmez. Web arayüzü
kapsam dışıdır.

## Denetimde görülen sorunlar

- **Alan belli değil:** Sunucunun doğrulama yanıtı alan adlı bir sözlüktür (`errors: { "aciklama": ["Bu alan boş olamaz."] }`,
  `Kasa.Api/GirdiDogrulama.cs`). İstemci (`KasaApiClient.Ileti`) alan adlarını atıp iletileri birleştiriyor. Kullanıcı yalnız "Bu
  alan boş olamaz." görüyor.
- **Hata görünmüyor:** Hata sayfanın tepesinde ya da formun dışında çıkıyor (Alışlar, Çekler, Takip sayfaları). Çekler'de boş formu
  kaydedince görünür hiçbir ileti yok.
- **Eski hata kalıyor:** Başka bir kayda geçince ya da "Yeni"ye basınca eski hata ekranda kalıyor (İşlemler, Alışlar).
- **Düzenleme modu belli değil:** Formun başlığı ve düğmeleri değişmiyor. Yazılmış ama kaydedilmemiş form, başka bir kaydı düzenlemeye
  geçince uyarısız eziliyor (İşlemler).
- **Kart ödemesi:** "Ödemeyi kaydet" önizleme alınmadan basılabiliyor. Tutar boşken bile ileti önizlemeden söz ediyor.
- **Aylık giderler:** "Ödemeyi aç / iptal et" tek düğmede iki anlam taşıyor. Form, görünmeyen bir yerde açılıyor.
- **Bağlantı kopunca veri siliniyor:** Sunucuya ulaşılamayınca Kasalar, Haftalık ve Kartlar boşalıyor. İşlemler formunda kanal ve tip
  seçenekleri kayboluyor. Her sayfa hatayı kendi biçiminde gösteriyor. "Son güncelleme:" etiketi boş kalıyor.

## Tasarım

### 1. Alan hataları

- **İstemci:** `KasaApiException` sunucunun alan sözlüğünü `AlanHatalari` olarak taşır: `IReadOnlyDictionary<string, string>`,
  alan adı → ilk ileti. Anahtarlar küçük harfe indirilir, `.`/`[i]` önekleri korunur. Birleşik `Message` aynı kalır (geriye uyum).
- **Görünüm modeli:** `Kasa.App.Core`'da ortak bir alan hatası yapısı (`AlanHatalari`) olur:
  - alan → ileti eşlemi;
  - `Ayarla(alan, ileti)`, `Temizle(alan)` ve `Temizle()`;
  - değişiklik bildirimi;
  - bağlanabilir dizinleyici, örneğin `Hatalar["Tutar"]`.

  Her form görünüm modeli sunucu alan adlarını kendi form alanlarına eşler, örneğin `aciklama` → `Aciklama`. Eşlenemeyen iletiler
  formun genel hatasına (`FormHatasi`) gider.
- **Ön doğrulama:** Kaydetmeden önce istemci en sık hataları kendisi yakalar. Bu, sunucu kuralının yerine geçmez; sunucu yine
  denetler. İletiler alanı adıyla söyler:
  - boş metin: "Açıklama boş olamaz.";
  - tutar sıfır ya da boş: "Tutar sıfırdan büyük olmalı.";
  - seçilmemiş kanal ya da tip: "Kanal seçin.";
  - kart günleri 1–31 dışında: "Kesim günü 1 ile 31 arasında olmalı.";
  - çek formunda no, kişi, tutar ve vade.

  Ön doğrulama hata bulursa istek gönderilmez.
- **Gösterim:**
  - Form alanı bileşeni (XAML `FieldBorder` ailesi ve `TakipUi.Alan`) hata varken çerçeveyi kırmızı yapar, iletiyi alanın altında
    gösterir. Ekran okuyucu alan adına iletiyi ekler.
  - Genel hata formun en üstünde, formun içinde kırmızı kutuda çıkar; sayfanın tepesinde değil.
  - Takip sayfalarının sayfa başındaki hata satırı yalnız yükleme hataları için kalır.
- **Hataya kaydırma:** Kaydetme başarısız olunca sayfa ilk hatalı alana (yoksa genel hata kutusuna) kaydırılır ve odak oraya gider.
  Kartlar'daki `GorunurYap` ortak bir yardımcıya taşınır ve bütün formlar onu kullanır.
- **Temizleme:**
  - kullanıcı hatalı alanı değiştirince o alanın hatası kalkar;
  - başka kayda geçince, "Yeni"ye basınca, "Vazgeç"e basınca ve kayıt başarılı olunca bütün form hataları kalkar.
- **Kapsam:** İşlemler, Alışlar, Çekler (yeni ya da düzeltme formu ve hareket formu), Kartlar (yeni kart ya da düzeltme ve ödeme),
  Krediler, Aylık giderler (şablon ve ödeme), Ayarlar (kanal formu).

### 2. Düzenleme modu ve kaydedilmemiş değişiklik

- **Görünüm:** Bir kayıt düzenlenirken:
  - form başlığı "Düzenleniyor: {tarih} · {ad}" olur (her formun kendi özeti);
  - kaydet düğmesi "Değişikliği kaydet" olur, yanında "Vazgeç" bulunur;
  - düzenlenen satır listede vurgulanır.

  Yeni kayıtta başlık "Yeni …" olur, örneğin "Yeni kart", "Yeni işlem".
- **Kaydedilmemiş değişiklik:** Formda kaydedilmemiş değişiklik varken başka kayda geçilirse, "Yeni"ye basılırsa ya da sayfadan
  çıkılırsa onay sorulur: "Kaydedilmemiş değişiklik var. Bırakılsın mı?" Seçenekler "Bırak" ve "Forma dön".
  - "Kaydedilmemiş" ölçütü: form, açıldığı andaki değerlerden farklı.
  - Alışlar'daki mevcut uyarı (`AlislarViewModel`) aynı ortak yapıya taşınır.
  - Kapsam: İşlemler, Alışlar, Çekler, Kartlar, Krediler, Aylık giderler.

### 3. Bağlantı durumu

- **Tek kaynak:** `BaglantiDurumu` tekil hizmeti `Kasa.App.Core`'da durur.
  - **Kopuk:** API istemcisinin ağ hatası (`HttpRequestException`, zaman aşımı) durumu "kopuk" yapar.
  - **Bağlı:** Herhangi bir başarılı yanıt durumu "bağlı" yapar. 4xx ve 5xx yanıtları bağlantı hatası sayılmaz.
  - İstemcide bunu `DelegatingHandler` ya da mevcut gönderim noktası yapar.
- **Kabuk şeridi:** Kopukken uygulamanın en üstünde tek bir şerit görünür: "Sunucuya ulaşılamıyor · Son bağlantı 14:05 · Yeniden
  dene".
  - "Yeniden dene" açık sayfayı yeniler.
  - Bağlantı geri gelince şerit kalkar ve açık sayfa bir kez yenilenir.
  - Sayfaların kendi bağlantı hatası iletileri kopukken gösterilmez (aynı ileti iki kez çıkmasın). Diğer hatalar (sunucu hatası,
    yetki) sayfada kalır.
- **Son veri korunur:**
  - Yükleme hata verirse görünüm modeli son başarılı veriyi silmez. Bu `RaporViewModel` (`VeriVar=false` yazılmaz) ve
    `TakipSayfasi` (`Govde`, `VeriHazir` false olsa da son veri varken görünür) için geçerli.
  - Veri eskiyse ekranda soluk görünür ve "Son güncelleme 14:05 · güncel olmayabilir" yazar.
  - Kapsam: Kasalar, Haftalık, Aylık, İşlemler, Alışlar, Kartlar, Krediler, Çekler, Aylık giderler, Bildirimler.
- **Seçenekler:** İşlemler formundaki tip seçenekleri sabittir, her zaman görünür. Kanal seçenekleri son başarılı yüklemeden gelir.
- **Boş zaman:** Hiç yükleme yoksa "Son güncelleme" yerine "Henüz yüklenmedi" yazar (iki sayfa ailesinde aynı).
- **Kopukken kaydetme:** Kaydet denenir. Ağ hatasında form ve girilen değerler korunur, formun genel hatasına şu ileti yazılır:
  "Sunucuya ulaşılamadı. Kayıt yapılmadı; bağlantı gelince yeniden kaydedin." Tekrar koruması (`istekId`) mevcut haliyle kalır.

### 4. Kart ödemesi akışı

- **Tek düğme:** İki birincil düğme ("Ödeme ve kanal paylarını göster", "Ödemeyi kaydet") yerine tek bir "Ödemeyi kontrol et" düğmesi
  olur.
  - Eksik alan varsa önce onu alan hatasıyla söyler.
  - Alanlar tamsa önizlemeyi (kanal payları) gösterir ve "Onayla ve kaydet" düğmesi belirir.
  - Önizlemeden sonra tutar, tarih ya da pay değişirse "Onayla ve kaydet" kalkar; önizleme yeniden istenir.
- **Kural:** Önizleme güncel değilken kaydetmek mümkün değildir. Sunucu kuralı aynı kalır.

### 5. Aylık giderler

- **Düğme:** Ödenmemiş satırda "Öde", ödenmiş satırda "Ödemeyi iptal et" yazar. İptal onay ister.
- **Form yeri:** Ödeme formu satırın hemen altında açılır, ya da ortak yardımcıyla görünür yere kaydırılır.

## Dışarıda kalanlar

Bunlar diğer paketlerin işi:

- buton hiyerarşisi, renk, terim ve sayfa iskeleti (Paket C);
- klavye ve çipler (Paket B);
- menü ve sayfa düzeni (Paket D);
- dar pencere (Paket E);
- geçici başarı bildirimi (toast).

## Testler

- **ApiClient:** Alan sözlüğü `AlanHatalari`'na doğru aktarılır; düz ileti ve `hata` gövdesi değişmez; bağlantı hatası ve başarı
  `BaglantiDurumu`'nu günceller.
- **App.Core:** Her form görünüm modeli için:
  - ön doğrulama iletileri;
  - sunucu alan eşlemesi (`aciklama` → `Aciklama`) ve eşlenemeyen iletinin genel hataya düşmesi;
  - temizleme kuralları (alan değişince, kayıt değişince, Yeni, Vazgeç, başarı);
  - düzenleme başlığı;
  - kaydedilmemiş değişiklik algısı;
  - kart ödemesinde önizleme bayatlayınca kaydetmenin kapanması;
  - bağlantı kopukken son verinin korunması ve kaydetmede form değerlerinin korunması.
- **MAUI:** `MauiKayitTutarliligiTests` ve `GorunumEsdegerligiTests` güncellenir. Windows derlemesi 0 uyarı, maui-lint taban içinde.
- **Ekran denemesi:** Uygulama ayrı masaüstünde, yerel test sunucusuyla açılır. Boş kayıt, alan hatası, düzenleme modu, kaydedilmemiş
  değişiklik onayı, sunucu durdurulunca şerit ve korunan veri, sunucu geri gelince yenileme ekran görüntüleriyle doğrulanır. Ardından
  ürün sahibiyle ekranında denenir.
