# Emar Kasa — Paket C: Hızlı ve hatasız giriş — Tasarım

- **Tarih:** 2026-09-24
- **Durum:** Uygulandı (dal `paket-c`).
- **İlgili spec:** `docs/specs/2026-07-13-kasa-defteri-design.md` (hesap kuralları),
  `docs/specs/2026-09-24-cekler-design.md` (çek kuralları)

## 1. Amaç ve kapsam

Günlük giriş hızlansın ve yanlış kayıt azalsın: klavyeyle kaydetme, formdan cari ekleme, kaydetmeden
önce "emin misiniz" uyarıları, bir haftanın bütün gelenlerini tek ekranda girme, iki basışlı silme ve
geri alma, gelişmiş işlem arama, işlem kopyalama / seri giriş ve Excel'den toplu yükleme.

**Değişmeyenler (sert kural):**

- Hiçbir para kuralı değişmedi. `HesapMotoru` çıktıları, kart kuralı ve çekin "vadede kasaya"
  kuralı aynı rakamları üretir. Yeni uçlar yalnız okur, uyarır ya da mevcut doğrulamalarla yazar.
- Veritabanı şeması değişmedi (yeni tablo ya da sütun yok), bu yüzden `SemaGoc` testi gerekmedi.
- Sunucu yapılandırması değişmedi. `deploy/README.md`'ye dokunulmadı.
- Mevcut testlerin beklentileri değişmedi.
- Yeni uçların hepsi oturum ister ve yazanlar `Editor` politikasındadır. "Bugün" sunucuda
  `Saat.Bugun(TimeProvider)` ile hesaplanır.

## 2. Özellikler

### 26 · Klavye kısayolları (İşlemler)

| Tuş | Eylem |
|---|---|
| Enter | Cari kutusunda: yazılan kayıtlı değilse ilk öneriyi seçer. Tutar henüz girilmediyse (0) kaydetmez, tutara geçer (kayıtlı ad kayıtlı yazımına çevrilir); tutar varsa kaydeder. Tutar ve Not kutularında kaydeder. Gelen tutarında gelen kaydeder. |
| Ctrl+S | Kaydeder. |
| Ctrl+N | Yeni satır açar: form temizlenir, odak cariye geçer. |
| Esc | Önce açık olanı kapatır: uyarı, ileri tarih onayı, benzer cari sorusu, gelen çakışması, silme onayı. Hiçbiri açık değilse düzenlemeden çıkar. Yeni kayıtta formu silmez. |
| Alt+1…4 | Gider kanalı çiplerinden 1.–4.'yü seçer (Ortak da bir çiptir). |
| Tarih kutusunda B / D | B bugünü, D dünü seçer (işlem ve gelen tarihinde). |

- Eşleme platformdan bağımsızdır ve test edilmiştir (`KlavyeKisayollari`). Eylemi
  `IslemlerViewModel.KisayolCalistir` uygular.
- Sayfa yalnız WinUI olaylarını bağlar (`IslemlerPage.Kisayol.cs`, `#if WINDOWS`). Sayfa kökünde
  gizli `KeyboardAccelerator`'lar vardır. Tarih kutusunun `PreviewKeyDown`'u B ve D'yi yakalar.
- İzleyicide kısayol çalışmaz.
- Uyarı ya da onay beklerken Enter ve Ctrl+S kaydetmez. Uyarı klavyeyle geçilemez, düğmeyle geçilir.
- **Ekrandaki tutar = kaydedilen tutar.** Geçersiz tutar metni (ör. "1500o", "1.250.00", "-50")
  bağlı tutarı değiştirmez; kutu son geçerli değere ancak odak çıkınca döner. Kaydet düğmesi odağı
  aldığı için sorun yoktur, ama Enter ve Ctrl+S odağı değiştirmez. Bu yüzden sayfa tutar kutularının
  anlık metnini VM'e iter (`TutarKutusuMetni`, `GelenTutarKutusuMetni`). Metin geçersizse Enter/Ctrl+S
  kaydetmez ve "Tutar geçersiz: … Kaydedilmedi; tutarı düzeltin." yazar. Gelen tutarında Enter da
  aynı denetimden geçer; "Üzerine yaz / Üstüne ekle" sorusu açıkken hiçbir şey yapmaz.
- Formun altında kısa bir yardım satırı gösterilir.

### 27 · Formdan cari ekleme

- Yazılan ad kayıtlı bir cari değilse (sabit gider kipinde değilken) **"Cari olarak ekle"** düğmesi
  görünür.
- Eklemeden önce kayıtlı adlarla benzerliğe bakılır (`CariBenzerlik`):
  - Ad Türkçe kurallarla normalleştirilir: küçük harf, İ/ı, ş, ğ, ç, ö, ü Latin karşılığına çevrilir,
    noktalama atılır. "Ltd. Şti.", "A.Ş.", "Tic.", "San." gibi ekler atılır.
  - Kalan ad Damerau-Levenshtein mesafesiyle karşılaştırılır. Uzunluğa göre eşik: 3 harfe kadar 0,
    5'e kadar 1, 10'a kadar 2, daha uzunsa 3. Ayrıca en az 4 harfli tam bir kelime öteki adın
    içinde geçiyorsa benzer sayılır.
- Benzer ad varsa eklenmez, **"Bunu mu demek istediniz: A / B?"** diye sorulur:
  - Benzerlerden biri seçilirse forma o ad yazılır.
  - "Yine de ekle" yeni cari açar.
  - "Vazgeç" soruyu kapatır.

### 28 · Kayıt öncesi uyarılar

**`POST /api/islemler/uyarilar`** (Editor). Gövde: işlem alanları ve düzenlenen kaydın `haricId`'si.
Dönüş: `[{kod, mesaj}]`. Sunucu kaydı hiçbir zaman engellemez.

| Kod | Kural |
|---|---|
| `AyniTutar` | Aynı cariye aynı tutar ±3 gün içinde girilmiş. |
| `OlaganDisiTutar` | Tutar, carinin son 20 işleminin ortancasının 10 katı ya da fazlası (en az 3 işlem varsa). |
| `EskiTarih` | Tarih ya da düzenlenen kaydın eski tarihi 45 günden eski: ortakların gördüğü raporlar değişir. |
| `CekCiftDusme` | Aynı kişiye aynı tutarda verilmiş bir çek var. Çek ödendiyse ödeme tarihi, ödenmediyse vadesi ±7 gün içindedir. |

- Uygulama önce ileri tarih onayını sorar (mevcut davranış), sonra uyarıları gösterir.
- Uyarı kutusunda "Yine de kaydet" ya da "Vazgeç" seçilir.
- Formda bir alan değişince uyarı kapanır.
- Uyarı ucu hata verirse ya da yoksa (404/405) kayıt normal akar.

### 29 · Gelenler tablosu ve korumalı gelen

- **`GET /api/gelenler/tablo?donemStart=`** seçili dönemin bütün kanallarını tek ekranda verir.
  - Satırlar aktif kanallardır; o dönemde geleni olan pasif kanallar da listelenir.
  - `donemStart` verilmezse bugünün dönemi kullanılır.
  - Önceki ve sonraki dönemin başlangıcı da döner.
  - Takip aralığının dışındaki bir dönem 400 döner.
- **`GET /api/gelenler/eksik-liste`** tamamlanmış dönemlerde geleni girilmemiş aktif kanalları listeler.
  - Yalnız dönemin doğal sonu bugünden önceyse dönem tamamlanmış sayılır.
  - En yeniler önce gelir, en çok 200 kayıt döner. Toplam `X-Toplam-Kayit` başlığındadır.
  - Kanalın başlangıcından önce biten dönemler sayılmaz. Başlangıç, kanalın geçmişteki "Eklendi"
    (ya da "Eklendi (geri alındı)") satırı ile ilk hareketinin (ilk geleni ya da ilk işlemi) erkenidir.
    Geçmiş tek başına yetmez: eski kurulumlarda yoktur ve 2 yıldan eskisi açılışta silinir. Başlangıcı
    hiç bilinmeyen (geçmişi ve hareketi olmayan) kanal listelenmez.
  - Kanalın baştan sona pasif olduğu dönemler sayılmaz. Aktif/pasif geçişleri kanalın "Güncellendi"
    geçmiş satırlarının eski/yeni JSON'undaki `aktif` alanından okunur. Dönem içinde yeniden aktif
    olduysa o dönem sayılır. Geçmişi silinmiş geçişler bilinemez; o dönemler aktif sayılır.
  - 0 girilmiş gelen, girilmiş sayılır.
- **`PUT /api/gelenler`** (mevcut uç) isteğe bağlı `beklenenTutar` alanını kabul eder. Bu alan
  verilmişse ve kayıtlı tutar ondan farklıysa uç 409 `{hata, mevcutTutar}` döner. Alan yoksa
  davranış eskisiyle aynıdır.
- **Gelenler sayfası:** hafta gezinmesi, kanal başına kayıtlı tutar ve yeni tutar kutusu.
  - Yalnız değişen satırlar gönderilir. Her satır, kullanıcının gördüğü tutarla korumalı yazılır.
  - Kayıtlı geleni olan satırda "Üzerine yaz" ya da "Üstüne ekle" seçilmeden kaydedilmez.
    Önizleme "→ 15.000,00 ₺" biçimindedir.
  - 409 alan satır güncel tutarla yeniden sorar. Öteki satırlar kaydedilir.
  - Altta "MEZAT geleni girilmedi · 14 Eyl – 20 Eyl 2026" listesi vardır. "O haftaya git" o haftayı
    açar ve sayfayı en üste kaydırır (tablo sayfanın başındadır). Açık haftaya gidiliyorsa yeniden
    yüklemez, yalnız kaydırır.
  - **Kaydedilmemiş girişler kaybolmaz:**
    - Yazılmış ama kaydedilmemiş satır varken başka haftaya geçmek (Önceki, Sonraki, Bu hafta,
      O haftaya git) önce sorar: "Kaydedilmemiş giriş var: MEZAT, TOPTAN. Başka haftaya geçerseniz bu
      girişler silinir." Seçenekler "Kaydetmeden geç" ve "Vazgeç". "Değişenleri kaydet" soruyu kapatır.
    - Başka sayfaya gidip dönünce aynı hafta yeniden yüklenir ve girişler korunur. Kayıtlı tutar bu
      arada değiştiyse (ör. İşlemler'deki gelen formundan) giriş kalır ama "Üzerine yaz / Üstüne ekle"
      seçimi sıfırlanır ve satır "Siz yazarken kayıtlı tutar değişti (A → B)…" diye uyarır.
- **İşlemler sayfasındaki gelen formu** da kayıtlı geleni sessizce ezmez. Farklı bir kayıtlı tutar
  varsa aynı "Üzerine yaz / Üstüne ekle / Vazgeç" sorusunu sorar ve yazımı korumalı yapar.

### 32 · İki basışlı silme ve geri al

- `SilmeOnayi` yeniden kullanılır. Sil düğmesine ilk basışta düğme **"Emin misiniz?"** olur ve yalnız
  o satırda değişir. 5 saniye içinde ikinci basış siler. İlk basış sayfada başka hiçbir şeyi
  değiştirmez (ipucu satırı yok), bu yüzden liste kaymaz ve ikinci basış aynı düğmeye gelir.
- Başka bir satıra basmak, süre dolması ya da Esc onayı düşürür.
- Geri alınamayan silmelerde (kanal, kredi kartı, tekrarlayan gider) düğme
  **"Geri alınamaz · Emin misiniz?"** der.
- Geri alınabilen silmeden sonra (işlem, kart ödemesi, cari, gider kalemi, çek, kasa sayımı)
  **"Silindi: … · Geri al"** şeridi çıkar.
  - Şerit, yeni **`GET /api/gecmis/son-silme?tur=&kayitId=`** ucuyla kaydın son "Silindi"
    geçmiş satırını bulur. Uç kayıt yoksa 404, eksik parametrede 400 döner.
  - "Geri al", mevcut geçmiş geri alma ucunu çağırır. 30 gün kuralı aynen geçerlidir.
  - Arama başarısız olursa şerit görünmez. Silme yine de gerçekleşmiştir.
  - Silme başarılı olup ardından liste tazelemesi hata verirse de şerit çıkar (hata ayrıca görünür).
    Onaylı silme, silme ucu ile tazelemeyi `TemelViewModel.SilVeTazeleAsync` ile ayrı değerlendirir.
- Şerit sayfanın altında, içeriğin üstünde yüzen bir çubuktur (`SilmeSeridi`: yeşil kutu, "Geri al",
  "Kapat"). Akışta yer kaplamaz; uzun sayfada aşağıdaki bir satır silinince de görünür.
  - İşlemler'de liste kartının içinde, listenin üstüne biner.
  - Çekler, Kredi Kartları, Ayarlar ve Cariler'de sayfa kökü `Grid` içine alındı: `ScrollView` ve onun
    üstünde tek satırlık `<v:SilmeSeridi/>`.
  - Kasa Sayımı'nda şerit form sütununda kalır.
- Cariler sayfasına Sil düğmesi eklendi (mevcut `DELETE /api/cariler/{id}`). İşlemi olan cari
  sunucuda 409 ile reddedilir ve pasif yapılması önerilir.

### 06 · Gelişmiş işlem arama

- `GET /api/islemler` ve `GET /api/disaaktar/islemler.csv` yeni isteğe bağlı parametreler alır:

  | Parametre | Anlamı |
  |---|---|
  | `notAra` | Notta geçen metin (Türkçe harf duyarsız). |
  | `tip` | `Cari`, `SabitGider` ya da `KrediKarti`. `KrediKarti` karta bağlı her harcamayı kapsar. |
  | `kartId` | Yalnız bu karta bağlı harcamalar. |
  | `minTutar` / `maxTutar` | Tutar aralığı (dahil). |

- Mevcut parametreler ve sayfalama değişmedi.
- Geçersiz tip, negatif tutar, en az > en çok ya da 200 karakterden uzun not araması 400 döner.
- Uygulamada "Gelişmiş arama" paneli açılır. Süzgeç "Uygula" ile etkinleşir ve kanal/zaman
  süzgeciyle birleşir.
- Etkin süzgecin özeti gösterilir, "Süzgeci kaldır" ile kaldırılır.
- "Excel'e aktar" aynı süzgeçle çalışır.

### 15 · Kopyala, cariye göre öneri, seri giriş

- **Kopyala:** işlem yeni kayıt olarak forma alınır. Tarih bugün olur ve odak tarihe geçer; yalnız
  tarihi değiştirip kaydetmek için tasarlanmıştır.
- **Öneri:** yeni kayıtta kayıtlı bir cari (sabit gider kipinde kalem) yazılınca
  **`GET /api/islemler/son?cari=`** son işlemi getirir. Uç işlem yoksa 204 döner.
  - Kanal ve tip, kullanıcı o girişte kendisi seçmediyse doldurulur. Kart, K.K ise doldurulur.
  - Açıklama "Son işlem: … (dolduruldu: …)" olarak gösterilir.
  - Düzenlemede öneri yoktur.
- **Seri giriş** anahtarı: kayıttan sonra tarih, kanal, tip (kart) ve not kalır. Yalnız cari ve tutar
  temizlenir (spec'teki gibi), odak cariye geçer. Seri kapalıyken kayıttan sonra form tamamen sıfırlanır.
- **Kaydedilen şeridi:** "Kaydedildi: 24 Eyl · Market · 250,00 ₺ · MEZAT". Şeritte "Düzelt"
  (düzenlemeye alır), "Sil" (iki basışlı) ve "Kapat" vardır.

### 16 · Excel'den toplu yükleme

- **Kaynak:**
  - Excel'den yapıştırma (sekmeyle ayrılmış metin) ya da dosya seçimi (MAUI `FilePicker`): Excel
    (`.xlsx`) ya da CSV/TXT/TSV.
  - **xlsx** NuGet'siz okunur (`TopluXlsx`): dosya bir ZIP'tir, `System.IO.Compression` ve
    `System.Xml` ile ilk sayfa (`workbook.xml` + ilişkiler; bulunamazsa ilk `xl/worksheets/*.xml`)
    okunur. Paylaşılan metinler (zengin metin birleştirilir, fonetik `rPh` atlanır), satır içi metin,
    sayı ve mantıksal hücreler desteklenir. Tarih biçimli sayı hücreleri (yerleşik tarih biçimleri ve
    gün ya da yıl içeren özel biçimler; 1900 ve 1904 tarih sistemi) `gg.aa.yyyy` olur. Sayılar Türkçe
    biçimde yazılır. Okunan tablo sekmeli metne çevrilip yapıştırmayla aynı yoldan geçer.
  - xlsx sınırları: en çok 64 sütun ve 1021 satır okunur, açılmış parça en çok 64 MB (zip bombası
    koruması), DTD yasak. Bozuk dosya "Excel dosyası okunamadı…" der. Eski `.xls` okunmaz ve
    "Excel'de 'Farklı kaydet' ile .xlsx ya da CSV seçin" der.
  - Kodlama UTF-8'dir (BOM'lu ya da BOM'suz). Geçerli UTF-8 değilse Windows-1254 denenir; Excel'in
    Türkçe CSV'si bu kodlamadadır.
  - Ayırıcı, boş olmayan ilk 20 satırda tırnak dışında aranır; öncelik sekme, `;`, `,`.
  - Tırnaklı hücre, `""` kaçışı ve hücre içi satır sonu desteklenir.
  - **Virgülle ayrılmış CSV yapı denetimi:** Türkçe tutarın ondalık virgülü ya da addaki virgül
    hücreyi bölebilir. Satırda başlıktan fazla dolu sütun varsa ya da Tutar hücresinin yanındaki hücre
    kuruşa benziyorsa (1–3 rakam, isteğe bağlı nokta ve 1–2 rakam; yan sütun Borç/Alacak/Bakiye
    değilse) satır hatalı olur ve "Dosyayı ';' ayırıcıyla kaydedin ya da tutarı düzeltin" der. Tutar
    düzeltilince hata kalkar.
- **Sütunlar:** Tarih · Cari · Tutar · Kanal · Tip · Not · Kart (banka dökümünde Borç · Alacak · Bakiye).
  - Başlık ilk 20 satırda aranır (banka dökümünde başlıktan önce hesap bilgisi satırları olur); önceki
    satırlar veri sayılmaz ve "Başlık satırından önceki n satır atlandı" notu çıkar. Başlık, en az iki
    tanınan hücre ve bunlardan biri Tarih, Tutar, Borç ya da Alacak demektir.
  - Takma adlar: Tutar için tutar, tutarı, işlem tutarı, miktar, meblağ, bedel…; Borç (borç, borç
    tutarı), Alacak (alacak, alacak tutarı), Bakiye (bakiye, kalan bakiye, hesap bakiyesi).
  - "Açıklama", Cari sütunu yoksa cari, varsa not sayılır. Cari açıklamadan alınınca "Cari,
    'Açıklama' sütunundan alındı." notu çıkar.
  - **Borç/Alacak sütunları** tek tutara çevrilir: borç eksi, alacak artı tutar olur. İkisi birden
    doluysa satır hatalıdır.
  - Tarih `gg.aa.yyyy`, `g.a.yyyy`, `/`, `-`, `yyyy-aa-gg` biçimlerinde yazılabilir. Excel'in eklediği
    saat kısmı atılır.
  - Tutar Türkçe biçimdedir (`ParaGiris`). İşlem her zaman giderdir; gönderilen tutar mutlak değerdir.
  - **İşaret kuralı:** Tablo banka dökümüyse (Borç/Alacak/Bakiye başlığı) ya da geçerli tutarlar
    arasında hem eksi (ya da parantezli) hem artı varsa işaret anlamlıdır. O zaman yalnız bir taraf
    gider sayılır: varsayılan olarak eksiler (borç, hesaptan çıkan). Öteki taraftaki satırlar hatalıdır
    ("Artı tutar: hesaba giren para (alacak), gider değil. Kaydedilmez; satırı kaldırın.") ve
    "Gider olmayan n satırı kaldır" düğmesi hepsini kaldırır. "Eksi tutarlar gider" anahtarı kapatılırsa
    artılar gider, eksiler (iade) hata olur. Tek işaretli tablo (hepsi artı ya da hepsi eksi) eskisi gibi
    mutlak değerle alınır. Bir tutar düzenlenip tablo tek işaretten iki işarete geçerse bütün satırlar
    yeniden doğrulanır. Bölme işareti korur.
  - Tip için takma adlar kabul edilir: cari/c, sabit/sg/gider, kk/kart/kredi kartı. Tip boşsa,
    yalnız kalem olan ad sabit gider sayılır, öteki adlar cari sayılır.
  - Kart sütunu doluysa satır K.K olur. Tek kart varsa kart boşken o kart kullanılır.
- **Canlı doğrulama:**
  - Her hücre değişince yalnız o satır yeniden doğrulanır (işaret durumu değişirse hepsi).
  - Önizlemedeki Tutar hücresi düz metin kutusudur: yazılan metin olduğu gibi kalır ve hatası satırın
    altında görünür (para kutusunun "odak çıkınca son geçerli değere dön" davranışı burada yok).
  - Hata satırın altında kırmızı gösterilir, bilgi gri gösterilir. Bilgiler kaydı engellemez:
    yeni cari (varsa benzeri de yazılır), ileri tarih, pasif kanal.
  - Özet: "n satır · geçerli · hatalı · toplam · yeni cari".
- **Seçenekler:**
  - "Kayıtlı olmayan carileri ekle" varsayılan olarak açıktır. Kaynak banka dökümüyse ya da cari
    "Açıklama" sütunundan geliyorsa kendiliğinden kapanır (her açıklama yeni cari olmasın) ve bunu
    söyleyen bir not çıkar. Yalnız kaynak türü değişince kendiliğinden değişir; kullanıcının sonraki
    seçimi korunur.
  - Bilinmeyen cari hatasında kayıtlı benzer ad varsa " Benzer: X." eklenir.
  - Kanalı boş satırlar için varsayılan bir kanal seçilebilir.
  - **Bölme:** satırın tutarı seçili kanallara eşit bölünür ve satır çoğalır. Kuruş artığı ilk kanala
    eklenir.
- **`POST /api/islemler/toplu`** (Editor): `{satirlar:[…], yeniCarileriEkle}`.
  - Tek transaction'dır: ya hep ya hiç.
  - Her satır tek işlem eklemedeki doğrulamadan (`IslemHatasi`) geçer.
  - Bir satır bile hatalıysa 400 `{hata, satirlar:[{sira, hata}]}` döner ve hiçbir satır yazılmaz.
    Uygulama hatayı o satıra yazar.
  - Yeni cariler aynı transaction'da eklenir.
  - Her işlem geçmişe ayrı satır olarak yazılır. Ayrıca `TopluDegisiklikEkle` özet satırı
    "Toplu yükleme: n işlem eklendi (toplam X ₺, tarih aralığı)[; k yeni cari eklendi]" eklenir.
  - En çok 1000 satır kabul edilir. Boş liste 400 döner. Kısıt ihlali 409 döner.
- İzleyici menüde bu sayfayı görmez. Sunucu da `Editor` ister.

## 3. API özeti

| Yöntem | Yol | Yetki | Not |
|---|---|---|---|
| POST | `/api/islemler/uyarilar` | Editor | Uyarı listesi; kaydı engellemez. |
| GET | `/api/islemler/son?cari=` | Oturum | Carinin son işlemi; yoksa 204. |
| POST | `/api/islemler/toplu` | Editor | Ya hep ya hiç; satır hataları 400. |
| GET | `/api/gelenler/tablo?donemStart=` | Oturum | Dönem × kanal. |
| GET | `/api/gelenler/eksik-liste` | Oturum | En çok 200; `X-Toplam-Kayit`. |
| GET | `/api/gecmis/son-silme?tur=&kayitId=` | Oturum | Son "Silindi" satırı; yoksa 404. |
| GET | `/api/islemler`, `/api/disaaktar/islemler.csv` | Oturum | Yeni: `notAra`, `tip`, `kartId`, `minTutar`, `maxTutar`. |
| PUT | `/api/gelenler` | Editor | Yeni isteğe bağlı `beklenenTutar`; farklıysa 409. |

- Uçlar `Kasa.Api/Endpoints/HizliGirisEndpoints*.cs` dosyalarındadır. `Program.cs`'e tek satırla
  bağlanırlar.
- DTO'lar `HizliGirisDtos.cs`'tedir. Arama süzgeci `IslemAramaFiltresi.cs`'tedir. Uyarı kuralları
  `Servisler/IslemUyarilari.cs`'tedir.
- İstemci tarafı:
  - Yeni metodlar `IKasaApi.HizliGiris.cs`, `KasaApiClient.HizliGiris.cs` ve `Dtos.HizliGiris.cs`'tedir.
  - Uç eski bir sunucuda yoksa (404/405) istemci yumuşak düşer:
    - uyarılar boş döner;
    - öneri ve son silme `null` döner.

## 4. Uygulama yapısı

- **App.Core:**
  - Yardımcılar: `KlavyeKisayollari`, `CariBenzerlik`, `SilmeOnayi`, `TopluGiris`
    (`TopluMetin`, `TopluBaslik`, `TopluSatir`, `TopluDogrulama`), `TopluXlsx`.
  - `TemelViewModel.Silme.cs`: `SilVeTazeleAsync` (silme başarısı tazelemeden ayrı).
  - `GelenlerViewModel.Gecis.cs`: hafta değişimi sorusu, giriş koruma, yukarı kaydırma olayı.
  - View model'ler: `TopluGirisViewModel`, `GelenlerViewModel`.
  - `IslemlerViewModel` bölüm dosyalarına ayrıldı: `.Form`, `.Kisayol`, `.CariEkle`, `.Uyari`, `.Seri`,
    `.Arama`, `.Silme`, `.Gelen`. Ana dosyada yalnız birkaç çağrı satırı değişti.
  - Öteki VM'lerde `*.Silme.cs` bölüm dosyaları var: Çekler, Kasa Sayımı, Kredi Kartları, Ayarlar,
    Cariler.
- **Kasa.Api:** `KanalDonemleri` (eksik gelen için kanalın başlangıcı ve pasif dönemleri).
- **Kasa.App:**
  - Yeni sayfalar: `GelenlerPage`, `TopluGirisPage`.
  - Paylaşılan görünüm ve servisler: `SilmeSeridi` (ContentView), `SilDugmeMetniConverter`
    (satırdaki Sil metni), `MauiDosyaSecici`.
  - Menü: "Gelenler" her iki rolde görünür, "Toplu Yükleme" yalnız editörde görünür.

## 5. Seçilen varsayılanlar

- **Klavye:**
  - Esc önce açık uyarıyı ya da soruyu kapatır, sonra düzenlemeden çıkar. Yeni kayıt formunu silmez.
  - Enter ve Ctrl+S, uyarı beklerken hiçbir şey yapmaz; tutar kutusu geçersizken kaydetmez.
  - Cari kutusunda Enter, tutar 0 iken tutara geçer.
- **Kopyala ve seri giriş:**
  - Kopyada tarih bugündür ve odak tarihtedir.
  - Seri girişte tarih, kanal, tip, kart ve not kalır. Kalanlar o seride kullanıcının seçimi sayılır.
- **Öneri:**
  - Yalnız yeni kayıtta çalışır.
  - Kanalı yalnız aktifse doldurur.
  - Cari kipinden sabit gidere geçirmez.
  - Hatasını yutar.
- **Uyarılar:** uç hata verirse kayıt engellenmez.
- **Silme:**
  - Onay süresi 5 saniyedir.
  - Şerit, kapatılana ya da yeni bir silme gelene kadar kalır. Altta yüzer; ilk basış sayfayı kaydırmaz.
- **Gelen:**
  - Tabloda dönemin doğal sonu kullanılır.
  - Kayıtlı geleni olan satırda seçim zorunludur. "0" girilebilir.
  - Kaydedilmemiş giriş varken hafta değişimi sorulur; sayfaya dönünce aynı haftanın girişleri korunur.
    Uygulama kapanırsa ya da oturum kapanırsa girişler kaybolur (sunucuya taslak yazılmaz).
  - Eksik listesinde başlangıcı bilinmeyen kanal listelenmez; geçmişi silinmiş pasif dönemler aktif
    sayılır.
  - Eksik listesi en çok 200 kayıttır. 0 girilmiş sayılır.
- **Toplu yükleme:**
  - Dosya sınırları: en çok 1000 satır, en çok 2 MB (xlsx için sıkıştırılmış boyut).
  - İşaret anlamlıysa eksiler gider sayılır; anahtar ile değiştirilir.
  - Tip boşsa otomatik bulunur. Tek kart varsayılandır.
  - "Yeni carileri ekle" açıktır.
- **Kalem adları:** `/api/islemler/son` sabit gider kalem adını da tanır.

## 6. Testler

- **Kasa.Api.Tests:** `HizliGirisApiTests` ve `GelenTablosuApiTests` şunları kapsar:
  - uyarı kuralları ve sınırları;
  - toplu yüklemede ya hep ya hiç, satır hataları, yeni cariler ve geçmiş özeti;
  - öneri, son silme ve gelişmiş arama;
  - korumalı gelen (409);
  - tablo sınırları ve eksik listesi (geçmişi silinmiş kanal, pasif dönemler, ilk hareket).
- **Kasa.ApiClient.Tests:** `HizliGirisTests` istek biçimlerini ve yumuşak düşmeyi kapsar.
- **Kasa.App.Core.Tests** şu dosyaları içerir:
  - `HizliGirisYardimciTests`: kısayol eşlemesi, benzerlik, silme onayı.
  - `IslemHizliGirisTests`: İşlemler akışları.
  - `TopluGirisTests`: ayrıştırma, doğrulama, bölme ve VM.
  - `TopluGirisDokumTests`: işaret kuralı, banka dökümü (Borç/Alacak, başlık arama), virgüllü CSV
    yapı denetimi, ayırıcı bulma, xlsx okuma (bellekte üretilen dosyalarla) ve bozuk dosya mesajları.
  - `GelenlerViewModelTests`: tablo, korumalı kayıt, hafta değişimi sorusu, giriş koruma, kaydırma.
  - `SilmeOnayiSayfaTests`: öteki sayfaların onaylı silmesi; tazeleme hatasında da şerit.

## 7. Dağıtım

Şema ya da yapılandırma değişmedi. Yalnız yeni API sürümü yayınlanır ve uygulama güncellenir.
Eski uygulama yeni sunucuyla aynen çalışır, çünkü yeni parametrelerin ve alanların hepsi isteğe
bağlıdır. Yeni uygulama eski sunucuda da açılır ve yeni uçlar yoksa yumuşak düşer. Ancak şunlar
eski sunucuda çalışmaz:

- toplu yükleme;
- gelen tablosu;
- gelişmiş arama süzgeci;
- gelen korumalı yazımı.

Bu yüzden önce API yayınlanmalıdır.
