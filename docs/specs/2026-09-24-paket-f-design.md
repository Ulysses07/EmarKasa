# Emar Kasa — Paket F: Belge, POS, ERP12 ve telefon — Tasarım

- **Tarih:** 2026-09-24
- **Durum:** Uygulandı (dal `paket-f`).
- **İlgili spec:** `docs/specs/2026-07-13-kasa-defteri-design.md` (hesap kuralları),
  `docs/specs/2026-09-24-cekler-design.md` (çek kuralı: "vadede kasaya")
- **Maddeler:** 41 fatura takibi · 17 fiş/fatura eki · 42 ERP12 tediye karşılaştırması ·
  44 POS blokaj/valör/komisyon · 19 telefon (salt okunur `/m`)

## 1. Amaç ve ana kural

Bu paket kasaya **bilgi** ekler, kasanın **hesabını değiştirmez**:

- `HesapMotoru`'nun çıktıları (kanal gelen/giden, sonuç, devir, kasa devri, kârlılık), kredi
  kartı kuralı ve çekin "vadede kasaya" kuralı aynen kalır.
- Yeni alanların hepsi isteğe bağlıdır. Hiç kullanılmazsa her ekran ve her rapor bugünkü
  rakamları birebir verir.
- POS kayıtları ayrı tablolardadır. Hesap motoru onları hiç görmez.
- ERP12 karşılaştırması ve telefon uygulaması salt okunurdur.

Bu kural testlerle kanıtlanır (bölüm 8). Var olan testlerin beklentilerine dokunulmadı.

**Kapsam dışı:** OCR (fişten tutar okuma), GİB e-fatura entegrasyonu, ERP12'ye yazma ya da
ERP12'den otomatik aktarma, telefondan kayıt girme, bildirim, POS'un banka ekstresiyle eşleştirilmesi.

## 2. Madde 41 — Fatura takibi

### 2.1 Veri

`Islemler` tablosuna üç sütun eklenir (`IslemBelgeAlanlari.cs`):

| Alan | Tip | Kural |
|---|---|---|
| `BelgeTuru` | `BelgeTuru?` | `EFatura`, `EArsiv`, `Fis`, `Makbuz`, `Belgesiz`; null = belirtilmemiş |
| `BelgeNo` | string? | Kırpılır; boşsa null; en çok 50; kontrol karakteri yok |
| `FaturaBekleniyor` | bool | Varsayılan `false` (DB'de `NOT NULL DEFAULT 0`) |

**Eski istemci güvencesi:** Belge alanları JSON'da gelmezse PUT onları silmez. Entity,
hangi setter'ın JSON'dan çağrıldığını `GelenBelgeAlanlari` bayrağında tutar
(`[NotMapped, JsonIgnore]`). `BelgeKurallari.Kopyala` yalnız gelen alanları kopyalar.
İstemci tarafında `IslemYaz` belge alanlarını `[JsonExtensionData]` ile yalnız `Belge` doluysa
gönderir. Böylece yeni istemci belge bölümüne dokunulmamış bir işlemde de alanları ezmez.

Doğrulama `IslemHatasi`'nın içinde çalışır: POST, PUT ve geçmişten geri almada.

**Belgesiz ile "fatura bekleniyor" birlikte olamaz** (400: "Belgesiz bir ödeme için fatura
beklenemez..."). Birlikte kaydedilirse fatura gelse bile ödeme ayın kırmızı "Belgesiz" toplamında
ve muhasebeci listesinde "Belgesiz" kalırdı. Faturası beklenen ödemenin türü boş bırakılır (ya da
e-Fatura gibi beklenen tür seçilir); tür fatura gelince seçilir. İşlem formu biri seçilince ötekini
kaldırır. Bu bir para kuralı değildir; tutarlar ve toplamlar aynı kalır.

### 2.2 Uç noktalar

| Yöntem | Yol | Rol | İş |
|---|---|---|---|
| GET | `/api/faturatakibi?yil&ay` | her iki rol | Bekleyen faturalar (cari cari) + ayın belge türü dökümü |
| PUT | `/api/islemler/{id}/belge` | editör | Yalnız belge alanları ("Fatura geldi") |
| GET | `/api/disaaktar/muhasebeci.csv?yil&ay` | her iki rol | Ay sonu muhasebeci listesi |

- **Bekleyenler:** `FaturaBekleniyor = true` olan **tüm** işlemler (tarih sınırı yok). Cariye
  göre gruplanır (büyük/küçük harf duyarsız), grup içinde en eski önce. Gruplar en eski
  tarihe göre sıralanır. Her satırda ek sayısı vardır.
- **Ay dökümü:** Ayın işlemleri belge türüne göre toplanır, sırası e-Fatura, e-Arşiv, Fiş,
  Makbuz, Belgesiz, Belirtilmemiş. "Belgesiz" toplamı ayrıca verilir.
- **Muhasebeci CSV:** Var olan `CsvYazici` biçimi (UTF-8 BOM, `;`, formül enjeksiyonu koruması).
  Sütunlar: Tarih, Cari/Kalem, Kanal, Tip, Kart, Tutar, Belge türü, Belge no, Fatura bekleniyor,
  Ek sayısı, Not. Sonda belge türü toplamları, "Fatura bekleniyor" toplamı ve genel toplam yer alır.
  Tutarlar işlemdeki haliyle yazılır; hesap yapılmaz. Ay paketinde (paket B) de
  `muhasebeci-YYYY-MM.csv` olarak birebir bulunur.
- Belge alanları ay kilidinin (paket B) dışındadır: kilitli aydaki işlemde yalnız belge değişiyorsa
  yazılır; "Önceki haline döndür" belge alanlarını da geri alır (paket D · 36).

### 2.3 Arayüz

- **İşlem formu:** Not alanının altında "Belge" bölümü. İçinde belge türü çipleri
  (Belirtilmedi · e-Fatura · e-Arşiv · Fiş · Makbuz · Belgesiz), belge no (50 karakter) ve
  "Fatura bekleniyor" kutusu vardır. Ekler de bu bölümdedir (bölüm 3.4).
- **Fatura Takibi sayfası** (menüde, route `faturatakibi`): bekleyenler cari cari listelenir.
  Editör satırda "Fatura geldi"ye basınca sayfanın üstünde küçük bir form açılır (sayfa oraya
  kayar): gelen faturanın türü (çipler: e-Fatura · e-Arşiv · Fiş · Makbuz; varsayılan e-Fatura,
  "Belgesiz" seçilemez), belge no (isteğe bağlı, en çok 50) ve isteğe bağlı fotoğraf/PDF eki.
  "Kaydet · fatura geldi" `PUT /api/islemler/{id}/belge` ile `(tür, no, bekleniyor = false)`
  gönderir, sonra ekleri yükler. Böylece muhasebeci listesi fatura gelince belge bilgili olur ve
  işlemi İşlemler'de yeniden aramak gerekmez. Bir ek yüklenemezse belge bilgisi yine kaydedilmiştir:
  form açık kalır, yüklenemeyenler bekler ve Kaydet yeniden dener. İzleyici formu açamaz. "Ekler"
  işlemin eklerini açar. Ay seçici, belge türü dökümü ve "Muhasebeci listesi (CSV)" düğmesi de
  buradadır. Dosya `Belgeler\Emar Kasa` altına kaydedilir.

## 3. Madde 17 — Fiş/fatura eki (fotoğraf, PDF)

### 3.1 Veri

Yeni tablo **`IslemEkleri`** (`IslemEkiEntity`): `Id`, `IslemId`, `OrijinalAd`, `DepoAdi`,
`IcerikTipi`, `Boyut`, `YuklemeZamaniUtc`, `SilinenIslemId?`, `SilinmeZamaniUtc?`. `IslemId` ve
`SilinenIslemId`'de index vardır; `DepoAdi` tekildir.

`IslemId` **bilerek FK değildir.** İşlem silinince ekleri 30 gün (geri alma süresi) kalır.
Bağ, Id'nin yeniden kullanılmasına dayanmaz:

- **Silme** (`DELETE /islemler/{id}`, tek transaction): `BelgeKurallari.IslemSiliniyor` işlemin
  eklerini işlemden ayırır: `IslemId = 0`, `SilinenIslemId = eski Id`, `SilinmeZamaniUtc = şimdi`.
  Geçmişe tek toplu satır yazılır ("n ek, silinen işlemle birlikte 30 gün saklanacak").
  Aynı Id bir gün yeniden verilse bile yeni işlem eski işlemin fiş/faturalarını devralmaz
  (işlem formu, fatura takibi, muhasebeci listesi ve işlem listesi aynı `IslemId` sayımını kullanır).
- **Geri alma:** işlem yeni Id alır; `GeriAlinanIslemeBagla` yalnız `SilinenIslemId` eski Id olan
  ekleri ona bağlar ve iki alanı temizler. Bu alanlar gelmeden (eski yoldan) yetim kalmış ekler,
  eski Id'de bugün bir işlem **yoksa** bağlanır; varsa o işleme ait sayılır ve dokunulmaz.
- **Sayaç güvencesi:** `SemaGuncelleyici` bir tabloyu FK için yeniden kurarken (`CREATE __yeni`,
  `INSERT…SELECT`, `DROP`, `RENAME`) `sqlite_sequence` değeri `MAX(Id)`'ye düşüyordu; silinen son
  işlemin Id'si yeni işleme verilebiliyordu. Artık eski sayaç `DROP`'tan önce okunur ve
  `RENAME`'den sonra `MAX(eski, yeni)` olarak geri yazılır (yalnız AUTOINCREMENT tablolarda).
  Böylece iki kat güvence vardır.

### 3.2 Depolama ve güvenlik (`BelgeDeposu`)

- Klasör `Kasa:BelgeKlasoru`. Tanımlı değilse DB dosyasının yanındaki `belgeler/` kullanılır
  (Docker'da `/data/belgeler`). DB bellekteyse geçici klasör kullanılır.
- Diskteki ad kullanıcıdan gelmez: 32 hex rastgele ad ve içerikten belirlenen uzantı. Her
  okumada `^[0-9a-f]{32}\.(jpg|png|webp|heic|pdf)$` ile doğrulanır; yol kaçışı olmaz.
- **Tür imzadan belirlenir:** JPEG, PNG, WEBP, HEIC (ftyp markaları) ve PDF. Uzantı imzayla
  uyuşmazsa ya da tür izinli değilse yükleme reddedilir.
- Dosya önce `.<ad>.tmp`'ye yazılır, sonra atomik olarak taşınır. DB kaydı oluşmazsa dosya
  silinir.
- Sınırlar: dosya başına 10 MB, işlem başına 10 ek. Sayı sınırı transaction içinde yeniden
  denetlenir. Özgün ad temizlenir (yol parçaları, kontrol ve yasak karakterler atılır) ve en
  çok 120 karakter tutulur.
- İndirme her zaman `attachment` olarak ve `Cache-Control: no-store` ile yapılır.

### 3.3 Uç noktalar

| Yöntem | Yol | Rol |
|---|---|---|
| GET | `/api/islemler/{id}/ekler` | her iki rol |
| POST | `/api/islemler/{id}/ekler` (multipart, tek dosya, alan `dosya`) | editör |
| GET | `/api/ekler/{id}` | her iki rol |
| DELETE | `/api/ekler/{id}` | editör |

Ek ekleme ve silme geçmişe "İşlem eki" türüyle yazılır. Ek silme geri alınamaz, çünkü dosya
hemen silinir. Silme onayı arayüzde iki adımlıdır.

`GET /api/islemler` (her iki rol) her işlemde `ekSayisi` verir; eki olmayan işlemde alan hiç
yazılmaz (eski istemci etkilenmez). Alan veritabanında yoktur (`[NotMapped]`) ve istek gövdesinden
okunmaz (setter `internal`); tek işlem yanıtlarında (POST/PUT) yer almaz.

### 3.4 Yaşam döngüsü ve arayüz

- **Gece temizliği** (`BelgeTemizleyici`, günde bir kez, 04:00'ten sonra): işlemi 31 günden önce
  silinmiş ekleri (kayıt ve dosya, toplu geçmiş satırıyla), kaydı olmayan ve 30 günden eski
  dosyaları, 6 saatten eski `.tmp` dosyalarını siler. Silinme zamanı ekteki `SilinmeZamaniUtc`'dir;
  eski yoldan kalmış eklerde geçmişteki "Silindi" satırına, o da yoksa yükleme zamanına bakılır.
- **İşlem listesi (her iki rol):** satırda belge rozeti (ör. "e-Fatura · F-12 · fatura bekleniyor")
  ve eki varsa "Ekler (n)" düğmesi görünür. Düğme listenin üstünde salt okunur bir ek paneli açar
  (yalnız "Aç"); düzenleme formuna dokunmaz. Böylece ortaklar da her işlemin fişini/faturasını
  açabilir, editör de eki olan işlemi açmadan görür. Formdan ek silinince satırdaki sayı ve açık
  panel hemen güncellenir; işlem silinince onun paneli kapanır. Formdaki ek silme onayını Esc
  kapatır; düzenleme açık kalır (ikinci Esc düzenlemeden çıkar).
- **İşlem formu:** "Fotoğraf/PDF ekle" çoklu seçimle çalışır. "Fotoğraf çek" yalnız kamera
  varsa görünür. Seçilen dosyalar "bekleyen ek" olur ve işlem kaydedildikten **sonra** yüklenir,
  çünkü yeni işlemin Id'si ancak o zaman bellidir. Biri yüklenemezse işlem yine kayıtlıdır:
  form o işlemin düzenlemesine geçer, yüklenemeyenler bekler ve Kaydet tekrar dener.
  İstemci boyutu, türü ve sayıyı sunucudan önce denetler (`EkKurallari`).
- **Açma:** Ek, önbellek klasörüne güvenli bir adla yazılır ve sistemin varsayılan uygulamasıyla
  açılır (`MauiEkAcici`).
- **Yedek:** Günlük DB yedeği dosyaları içermez. `kasa-yedek` servisi her gece yeni ekleri
  şifreli olarak uzaktaki `belgeler/` klasörüne kopyalar. Uzakta hiçbir dosya silinmez ya da
  üzerine yazılmaz. `ekleri-geri-al` komutu eksik ekleri geri indirir (`deploy/README.md`).
  Ekler gönderilemezse uzak yedek durumu "tamam + uyarı" olur; risk kartı (paket E) bunu sarı
  gösterir. Gece yedek doğrulaması `IslemEkleri`, `PosTanimlari` ve `PosSatislari` satır sayılarını da
  karşılaştırır.

## 4. Madde 42 — ERP12 tediye karşılaştırması (salt okunur)

Masaüstünde **ERP12** sayfası (route `erp12`). ERP12'den dışa aktarılan tediye listesi (CSV)
seçilir ve kasadaki işlemlerle karşılaştırılır. Hiçbir şey yazılmaz. Sunucuya yalnız var olan
`GET /api/islemler` isteği gider.

### 4.1 Dosya okuma (`Erp12Csv`)

- **Kodlama:** BOM varsa UTF-8 ya da UTF-16. BOM yoksa geçerli UTF-8 değilse Windows-1254
  (`CodePagesEncodingProvider`).
- **Ayırıcı:** `;`, `,`, sekme ya da `|`. İlk 20 dolu satırda, tırnak dışında en tutarlı çıkan
  seçilir; eşitlikte `;`. RFC 4180 tırnak kuralları ve hücre içi satır sonu desteklenir.
- **Başlık:** Baştaki rapor başlığı satırları ve boş satırlar atlanır. Başlık yoksa
  "Sütun 1…" adları üretilir; aynı adlar tekilleştirilir.
- **Tarih:** `gg.aa.yyyy`, `g.a.yyyy`, `/` ve `-` ayırıcılı biçimler, `yyyy-aa-gg`, iki haneli
  yıl, saatli biçimler ve Excel seri sayısı.
- **Tutar:** Türkçe (`1.234,56`) ve İngilizce (`1,234.56`) biçim, `₺`/`TL`, parantez ve sondaki
  eksi. İşaret yok sayılır; mutlak değer kuruşa yuvarlanır.
- Dosya en çok 20 MB olabilir.

### 4.2 Sütun eşleme

Tarih, Cari ve Tutar sütunları çiplerle seçilir. İlk açılışta başlık adlarından tahmin
edilir (`Erp12SutunTahmini`: "tarih", "cari/ünvan/firma", "tutar/borç/ödeme"…; "kod", "döviz",
"vade" gibi sütunlar dışlanır). Başlık adı yetmezse içerikten tahmin edilir. Seçilen eşleme,
başlık **adlarıyla** cihazda saklanır (`erp12.eslestirme`, Preferences) ve aynı biçimli bir
sonraki dosyada kendiliğinden uygulanır. İlk üç satır önizleme olarak gösterilir.

### 4.3 Eşleştirme kuralı (`Erp12Karsilastirici`)

Bir ERP12 satırı ile bir kasa işlemi şu üç koşulun hepsi tutarsa eşleşir:

1. Tutarlar **kuruşu kuruşuna** aynıdır (mutlak değer).
2. Tarihler en çok **±3 gün** farklıdır.
3. Cari adları Türkçe normalleştirmeyle benzerdir (≥ 0,5). Normalleştirme şunları yapar:
   - küçük harfe çevirir; ç/ğ/ı/İ/ö/ş/ü harflerini sadeleştirir; noktalamayı atar;
   - unvan eklerini (Ltd. Şti., A.Ş., San., Tic. …) yok sayar.

   Benzerlik, kısa addaki kelimelerin uzun adda bulunma oranıdır. En az 3 harflik bir baş
   da eşleşme sayılır. Bitişik yazılış da eşleşir ("YILMAZGIDA" = "Yılmaz Gıda").

Her kayıt en çok bir kez eşleşir. Adaylar önce benzerliğe, sonra gün farkına göre açgözlü seçilir.

**Aralık:** Başlangıç ve bitiş dosyadaki tarihlerden kurulur ve değiştirilebilir. Kasadan
aralığın ±3 gün genişletilmiş hali çekilir, çünkü sınırdaki ödemeler de eşleşebilmelidir.
"Yalnız kasada" listesi yine seçilen aralıkla sınırlanır. Varsayılan olarak yalnız **Cari**
tipli işlemler karşılaştırılır; "Tüm tipler" seçeneği vardır.

### 4.4 Sonuç

Üç liste ve bir özet gösterilir: **Eşleşenler**, **Yalnız ERP12'de**, **Yalnız kasada**.
Eşleşmeyen satırın altında, eşleşmeyi kıl payı kaçıran en yakın aday "Olası:" ipucu olarak
yazılır. Üç durumda ipucu çıkar:

- tutar ve tarih tutuyor ama cari farklı yazılmış;
- cari ve tutar tutuyor, tarih 31 güne kadar farklı;
- cari ve tarih tutuyor, tutar %1'e kadar farklı.

Okunamayan satırlar (tarih, tutar ya da cari eksik; tutar 0) sayılır ve ayrıca bildirilir.

## 5. Madde 44 — POS blokajı, valörü ve komisyonu

### 5.1 Veri (ayrı tablolar)

- **`PosTanimlari`** (`PosTanimEntity`):
  - `Ad` (tekil, en çok 100); `Saglayici` (BankaPosu, Iyzico, PayTr, Diger);
  - `KanalId?` (kanal silinirse null olur, "Kanalsız"; FK `SetNull`);
  - `KomisyonOrani` (yüzde, 0–100, en çok 4 ondalık); `BlokajGunu` (0–365); `Aktif`.
- **`PosSatislari`** (`PosSatisEntity`):
  - `Tarih`, `PosId` (FK `Restrict`), `BrutTutar` (> 0, işlem tutarı doğrulaması);
  - `KomisyonOrani` ve `BlokajGunu`: satış anında POS'tan kopyalanır; POS'un oranı sonradan
    değişse de eski satışın neti değişmez;
  - `KanalId?`: satışın kanalı da kayıt anında POS'un o anki kanalından kopyalanır (FK `SetNull`;
    kanal silinirse "Kanalsız"). POS'un kanalı sonradan değişirse geçmiş satışlar, bloke tutarları
    ve geçmiş ayların kanal dökümü **eski kanalda kalır**. Satış düzenlenirken POS değiştirilirse
    kanal yeni POS'tan alınır; aynı POS'ta kalırsa satışın kayıtlı kanalı korunur. Tablo bu
    pakette yeni olduğu için boş kanallı eski satır yoktur;
  - `Not`.
- Satışı olan POS silinemez (409, "pasif yapın").
- **Kanal düzeltmesi:** POS tanımı güncellenirken (`PUT /pos/tanimlar/{id}`) kanal değiştiyse ve
  `eskiSatislaraUygula = true` gönderildiyse POS'un tüm satışları yeni kanala taşınır (yanlış
  girilmiş kanalın düzeltilmesi; tek toplu geçmiş satırı: "POS adı: n POS satışının kanalı değişti:
  A → B"). Varsayılan `false`: yalnız bundan sonraki satışlar yeni kanala gider.

### 5.2 Hesap (`Kasa.Core.PosHesap`)

- Komisyon = kuruşa yuvarlı brüt × oran / 100, kuruşa yuvarlanır (`Para.Yuvarla`).
- Net = brüt − komisyon. Net ile komisyonun toplamı her zaman brüttür.
- Valör = satış tarihi + blokaj günü (takvim günü; tatil kaydırması yok).
- Bugün bloke: satış günü ≤ bugün < valör. Blokajı 0 olan satış hiç bloke olmaz. İleri
  tarihli satış henüz bloke sayılmaz.
- "Bugün" `Saat.Bugun(TimeProvider)`'dan gelir.
- `BlokeKanallar(kalemler, bugün)`: bugün bloke duran satışları kanal kanal toplar (net, adet).
  Ay sınırı yoktur; geçen aydan hâlâ bloke olan satış da girer. Toplamı `Bloke` ile aynıdır.

### 5.3 Uç noktalar

| Yöntem | Yol | Rol |
|---|---|---|
| GET/POST | `/api/pos/tanimlar` | GET her iki rol, POST editör |
| PUT/DELETE | `/api/pos/tanimlar/{id}` | editör |
| GET/POST | `/api/pos/satislar[?baslangic&bitis&posId]` | GET her iki rol, POST editör |
| PUT/DELETE | `/api/pos/satislar/{id}` | editör |
| GET | `/api/pos/ozet?yil&ay` | her iki rol |

Özet şunları verir: bugün bloke net (tutar, adet), bloke netin kanal kanal dökümü
(`blokeKanallar`: kanal, net, adet; "Kanalsız" en sonda, diğerleri ada göre), valör gününe göre
bekleyen döküm ve ayın kanal başına brüt/komisyon/net toplamı. Geçmiş türleri "POS" ve
"POS satışı"dır. Eski istemci `blokeKanallar` alanını yok sayar; yeni istemci alan yoksa boş liste
gösterir.

### 5.4 Arayüz

**POS** sayfası (route `pos`): üstte bloke tutar ve valör dökümü, ay seçici, kanal başına
komisyon tablosu ve satış listesi. Editör satış girer. Oran ve blokaj POS'tan gelir ama
satışta değiştirilebilir. Kaydetmeden önce komisyon, net ve valör önizlemesi gösterilir. Kayıtlı
bir satış düzenlenirken POS değiştirilirse, elle değiştirilmemiş oran ve blokaj boşaltılır; önizleme
ve kayıt yeni POS'un değerlerini kullanır (sunucu kuralı: boş = POS değiştiyse POS tanımındaki
değer). Satışın kendi POS'una geri dönülürse kayıtlı değerler geri gelir; elle yazılan değer
korunur. Bloke kartında bloke net kanal kanal listelenir. Satırdaki "Sil" iki basışlıdır (paket C ·
32): POS satışı geri alınamaz, ilk basış "Geri alınamaz · Emin misiniz?" der. POS tanımları aynı sayfada yönetilir;
kayıtlı bir POS'un kanalı değiştirilince "Eski satışlara da uygula" kutusu görünür (varsayılan
kapalı). Brüt tutar `ParaGiris` dönüştürücüsüyle okunur. Oran için
`OranGiris` kullanılır: 0–100, en çok 4 ondalık, virgül ya da nokta kabul edilir.

**Kasayla ilişki:** POS satışı kasaya **girmez**. Kasadaki para hareketi bugünkü gibi elle
girilen işlemdir. POS sayfası yalnız "bankada ne kadar bekliyor, ne kadar komisyon gitti"
sorusunu yanıtlar.

## 6. Madde 19 — Telefon (`/m`, salt okunur PWA)

### 6.1 Mimari

- **Kod:** `Kasa.Api/wwwroot/m/`: `index.html`, `app.css`, `app.js`, `sw.js`,
  `manifest.webmanifest`, `icons/`. Vanilla HTML/CSS/JS kullanılır; derleme adımı ve bağımlılık
  yoktur.
- **Yayın:** API ile aynı süreçten sunulur (`MobilWeb.UseMobilWeb`, Program.cs'te tek satır).
  `wwwroot/**` yayına kopyalanır (csproj `Content Update`).
- **Yönlendirme:**
  - `/m` → `/m/`. Uzantısız bilinmeyen yol `index.html`'e düşer (hash yönlendirme).
  - Uzantılı bilinmeyen dosya 404 döner. Yalnız GET/HEAD kabul edilir.
- **Ekranlar** (alt sekme çubuğu) ve kullandıkları uçlar:
  - Panel: `/api/rapor/panel`
  - Haftalık: `/api/rapor/haftalik` (yeniden eskiye, önce 6 hafta, sonra "Daha eski"). Sunucu
    takvimi Türkiye saatiyle bugünde keser; telefon cihaz tarihine göre ayrıca süzmez (saati geri
    ya da yurt dışında olan telefonda başlamış dönem gizlenmez).
  - Aylık: `/api/rapor/aylik` (ay gezinmeli). Açılıştaki ay Türkiye saatiyle bugünün ayıdır
    (`Intl.DateTimeFormat`, `Europe/Istanbul`), cihaz saat diliminden bağımsız.
  - Çekler: `/api/cekler/ozet`
  - Kredi kartları: `/api/kredikartlari`

  Uçların hepsi var olan okuma uçlarıdır. Rakamlar sunucudan geldiği gibi gösterilir.
  Telefon yalnız ekrandaki kart ve kanal toplamlarını görüntü için toplar.

### 6.2 Oturum

- Giriş var olan `POST /api/auth/login` ile yapılır. Oturum `kasa_auth` çerezinde durur
  (HttpOnly, SameSite=Strict, üretimde Secure, 30 gün).
- Token JavaScript'te tutulmaz, depolamaya yazılmaz.
- `GET /api/auth/me` rolü, `POST /api/auth/logout` sunucuda iptali verir.
- 401 gelirse giriş ekranı açılır.
- İki adımlı giriş (paket E): şifre doğru ama kod yoksa sunucu `401 {hata, kodGerekli: true}` döner;
  "Doğrulama kodu" alanı açılır ve aynı bilgiler kodla yeniden gönderilir. Alan 6 haneli kodu ya da
  kurtarma kodunu alır (`autocomplete="one-time-code"`; kurtarma kodu harf içerdiği için sayısal
  klavye zorlanmaz). Sunucunun mesajı (hatalı kod, pasif hesap, çok deneme) olduğu gibi gösterilir.
  Kullanıcı adı yalnız ortak izleyici şifresiyle girerken boş bırakılır.

### 6.3 Güvenlik

- **`/m` CSP'si:**
  `default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; manifest-src 'self'; worker-src 'self'; font-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'`
- **Diğer başlıklar:** `Cross-Origin-Opener-Policy: same-origin`, kısıtlayıcı
  `Permissions-Policy`, `Cache-Control: no-cache`.
- **Çizim:** Satır içi script, stil ya da olay niteliği yoktur. DOM yalnız `textContent`
  ve güvenli `el()` yardımcısıyla kurulur (`on*`/`style` niteliği yasak). `innerHTML`, `eval`
  ve depolama API'leri kullanılmaz; testler bunu dosya içeriğinden denetler.
- **Aynı kaynak koşulu (tüm `/api` yazmaları):** POST/PUT/DELETE/PATCH isteği 403 alır, eğer
  `Sec-Fetch-Site` `cross-site` ya da `same-site` ise. Bu başlık yoksa `Origin`'in adresi
  `Host`'tan farklıysa 403 döner. Başlıksız istemciler etkilenmez (masaüstü uygulaması, curl).
  Çerezle oturum açıldığı için CSRF'e karşı SameSite=Strict'e ek ikinci bir kilittir.
- **Service worker:** Kapsamı `/m/`'dir. Yalnız kabuk dosyalarını (`KABUK` listesi) ağ-önce
  önbelleğe alır. `/api` ve `/m/` dışındaki her istek, GET olmayan ve başka kaynağa giden her
  istek hiç ele alınmaz. Çevrimdışıyken kabuk açılır ve "Bağlantı yok" bandı görünür.
- **Manifest:** `id`, `start_url` ve `scope` `/m/`'dir. `display: standalone`. Simgeler 192,
  512 ve maskable 512; iOS için `apple-touch-icon` da vardır.

## 7. Masaüstü (MAUI) bağlantısı

- **Yeni sayfalar:** `FaturaTakibiPage`, `PosPage`, `Erp12Page`. Bunlar `MauiProgram.F.cs`
  (`AddPaketF`) ve `AppShell.F.cs` (`PaketFMenusu`) ile bağlanır. `MauiProgram.cs`,
  `AppShell.xaml` ve `AppShell.xaml.cs`'e yalnız birkaç satır eklendi. Menü öğeleri Geçmiş'in
  önüne girer. `IslemlerPage.xaml`'e Belge bölümü, liste satırına belge rozeti ve "Ekler (n)",
listenin üstüne salt okunur ek paneli eklendi (`BelgeOzetiConverter`).
- **Platform servisleri:**
  - `MauiDosyaSecici` (`FilePicker`, `MediaPicker`, boyut sınırlı okuma)
  - `MauiEkAcici`
  - `PreferencesAyarDeposu`
- **Test edilebilirlik:** App.Core'daki arayüzler (`IDosyaSecici`, `IEkAcici`, `IAyarDeposu`)
  view model'leri platformdan ayırır. Testler sahte uygulamalarla çalışır.
- **İstemci API'si:** `IKasaApi.F.cs` ve `KasaApiClient.F.cs` (partial). Sahte API
  `SahteApi.F.cs` (partial).

## 8. Para kuralı güvenceleri

| Güvence | Nasıl | Test |
|---|---|---|
| Hesap motoru POS'u görmez | POS ayrı tablolarda. `HesapMotoru`'na dokunulmadı | `PosTests.Pos_ve_belge_verisi_kasa_panel_haftalik_ve_aylik_rakamlarini_degistirmez`: POS satışı ve belge/ek eklenmeden önceki ve sonraki panel/haftalık/aylık JSON'u birebir aynı |
| Belge alanları tutarı etkilemez | Yalnız bilgi sütunları | `BelgeAlanlariTests`, aynı rapor testi |
| Eski istemci belge bilgisini silmez | Varlık bayrağı + `Kopyala` | `BelgeAlanlariTests.Eski_istemci_...` |
| Çek ve kart kuralı | Dokunulmadı | Var olan testler (beklentileri değişmedi) |
| Şema güvenli | Yeni tablo ya da NULL/DEFAULT'lu sütun | `PaketFSemaGocTests`: master şemalı DB güncellenir, veri korunur |
| Yeniden kurulum Id sayacını düşürmez | `SemaGuncelleyici.YenidenKur` sayacı korur | `PaketFSemaGocTests.FK_icin_yeniden_kurulan_tablo_AUTOINCREMENT_sayacini_korur_...` |
| POS kanal dökümü geriye dönük değişmez | Kanal satışa kopyalanır | `PosTests.Pos_kanali_degisince_gecmis_satislar_ve_gecmis_ay_ozeti_eski_kanalda_kalir` |
| POS hesabı | Kuruş yuvarlama, net + komisyon = brüt | `PosHesapTests`, App.Core'da `PosViewModelTests` (aynı örnekler) |

## 9. Varsayılan kararlar

| Konu | Karar |
|---|---|
| Belge türleri | e-Fatura, e-Arşiv, Fiş, Makbuz, Belgesiz (+ belirtilmemiş) |
| Belgesiz + fatura bekleniyor | Birlikte kaydedilemez; arayüz birini seçince ötekini kaldırır |
| "Fatura geldi" | Tür varsayılanı e-Fatura (Belgesiz seçilemez); no ve ek isteğe bağlı |
| İşlem listesinde ekler | Her iki rol görür ve açar (salt okunur panel) |
| Bekleyen fatura listesi | Tarih sınırı yok; ay seçimi yalnız ay dökümünü etkiler |
| Ek türleri / sınırlar | JPG, PNG, WEBP, HEIC, PDF · 10 MB · işlem başına 10 |
| Ek klasörü | DB'nin yanındaki `belgeler/` (`/data/belgeler`) |
| Silinen işlemin ekleri | 31 gün sonra gece temizliğinde |
| Ek silme | Hemen ve kalıcı; geri alınamaz (iki adımlı onay) |
| Ek yedeği | Yalnız ekleyen kopya (`--ignore-existing`), uzakta silme yok |
| ERP12 | Salt okunur, yalnız masaüstü; ±3 gün, kuruş eşitliği, benzerlik ≥ 0,5; varsayılan yalnız Cari tipi; eşleme cihazda başlık adıyla saklanır; dosya ≤ 20 MB |
| POS valörü | Takvim günü; hafta sonu/tatil kaydırması yok |
| POS oranı | Satışa kopyalanır (geçmiş satış sabit kalır) |
| POS kanalı | Satışa kopyalanır; POS'un kanalı değişince geçmiş satışlar eski kanalda kalır, "Eski satışlara da uygula" isteğe bağlı |
| Satış düzenlemede POS değişimi | Elle değiştirilmemiş oran/blokaj yeni POS'tan gelir |
| POS silme | Satışı varsa silinemez, pasif yapılır |
| Telefon | Salt okunur, 5 ekran, oturum çerezde 30 gün, yalnız kabuk önbellekte; "bugün" Türkiye saatiyle |

## 10. Testler

| Proje | Dosyalar |
|---|---|
| Kasa.Core.Tests | `PosHesapTests` |
| Kasa.Api.Tests | `BelgeAlanlariTests`, `IslemEkiTests`, `FaturaTakibiTests`, `PosTests`, `PaketFSemaGocTests`, `MobilWebTests` (`PaketFFactory` yardımcı) |
| Kasa.ApiClient.Tests | `PaketFTests` |
| Kasa.App.Core.Tests | `IslemBelgeTests` (+ `EkKurallariTests`), `FaturaTakibiViewModelTests`, `PosViewModelTests`, `Erp12CsvTests`, `Erp12KarsilastiriciTests`, `Erp12KarsilastirmaViewModelTests` |

Tam takım:

```bash
for p in Kasa.Core.Tests Kasa.Api.Tests Kasa.ApiClient.Tests Kasa.App.Core.Tests; do dotnet test $p --nologo || break; done
```

**Telefon duman testi (isteğe bağlı, yerel):** Gerçek bir Chromium'da iPhone 13 profiliyle
çalışır. Kendi geçici DB'siyle `Kasa.Api`'yi başlatır ve örnek veri girer. Şunları dener:

- yanlış ve doğru şifreyle giriş;
- beş sekmenin çizilmesi;
- service worker kapsamı ve önbellekte `/api` olmaması;
- çevrimdışı açılış, çıkış ve 401;
- aynı kaynaktan yazmanın 403 almaması.

```bash
PLAYWRIGHT_BROWSERS_PATH=/opt/pw-browsers node Kasa.Api.Tests/mobil-duman/duman.mjs
# hazır bir sunucuya karşı: KASA_URL=http://... KASA_KULLANICI=... KASA_SIFRE=... (örnek veri girmez)
```

**Masaüstü XAML:** MAUI Linux'ta derlenmez. XAML ve code-behind, MAUI 10 Controls paketine
karşı net10.0 bir kontrol projesinde derlendi. Bağlama yolları yansımayla doğrulandı. Windows
derlemesi yayın öncesi yapılmalıdır.

## 11. Bilinen sınırlar

- ERP12 yalnız CSV okur (XLSX için ERP12'den CSV alınmalı) ve yalnız masaüstündedir.
- Ek önizlemesi yoktur; ek, sistemin varsayılan uygulamasıyla açılır. Telefonda ek görüntüleme
  yoktur: `/m`'de işlem listesi ekranı yoktur (ekler masaüstünde her iki rolde açılır).
- POS'un gerçek hesaba geçişi banka ekstresiyle karşılaştırılmaz. Valör hesaplanan tarihtir.
- Telefon uygulaması yalnız okur; kayıt girişi masaüstündedir.
