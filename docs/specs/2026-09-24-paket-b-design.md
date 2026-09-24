# Emar Kasa — Paket B: Raporlar ve ay kapanışı — Tasarım

- **Tarih:** 2026-09-24
- **Durum:** Uygulandı (dal `paket-b`).
- **İlgili spec:** `docs/specs/2026-07-13-kasa-defteri-design.md` (hesap kuralları),
  `docs/specs/2026-07-15-kart-borc-hareketleri-design.md` (kart kuralı),
  `docs/specs/2026-09-24-cekler-design.md` (çek kuralı "vadede kasaya")

## 1. Amaç ve kapsam

Mevcut raporlar rakamı gösteriyor ama nedenini göstermiyordu. Rakamdan kayda inilemiyordu.
Ay sonunda kapanmış bir ay sessizce değişebiliyordu. Bu paket şunları ekler:

| No | Özellik | Kısa tanım |
|---|---|---|
| 21 | Kasa neden değişti? | Hafta ya da ay için açılış kasasından kapanışa adım adım döküm |
| 22 | Rapordan İşlemler'e iniş | Rapordaki rakama dokununca İşlemler o dönem, kanal (ve tip) ile süzülür |
| 24 | Yazdır (Aylık) | Sunucu A4 HTML üretir; uygulama Belgeler\Emar Kasa'ya kaydedip tarayıcıda açar |
| 04 | Grafikler | Son 12 ay + geçen yılın aynı ayı; nominal TL, reel TL, USD, EUR, gram altın |
| 05 | Hedef ve bütçe | Kanal gelir hedefi, sabit gider kalemi bütçesi, geçen aydan kopyala; Aylık'ta gerçekleşen/hedef % |
| 07 | Cari özeti | Bir cari ya da sabit gider kalemi için yılın ay ay toplamları |
| 40 | Dışa aktarma | "Ay paketini indir" (ZIP); Çekler, Kasa Sayımı, Geçmiş'te "Excel'e aktar" |
| 11 | Ay kilidi ve yayın | Kilitli aya yazma merkezî olarak 409; "Ayı yayınla" anlık görüntü; yayından sonra değişenler kırmızı şeritte |

**Kural:** Hiçbir para kuralı değişmez. `HesapMotoru` çıktıları, kart kuralı (K.K bir sonraki
ayda kasadan düşer) ve çek kuralı (vadede kasaya: `IslemTarihi`) birebir aynıdır. Yeni rakamların
hepsi motorun çıktısından okunur. Motor çıktısı `MotorAltinCiktiTests` altın testiyle sabitlenmiştir
(rastgele ama tohumlu veriyle üretilen tüm haftalık/aylık rakamların özeti değişirse test kırılır).

**Kapsam dışı:** Cari özetinden İşlemler'e iniş, sunucu tarafında tip süzgeci, TÜFE/altın
değerlerinin otomatik çekilmesi (hiçbir değer tahmin edilmez).

## 2. Roller

- Tüm yeni uç noktalar oturum ister (korumalı `/api` grubu).
- Yazan her uç nokta mevcut **`Editor`** politikasıyla korunur: ay kilitle / kilit aç / yayınla,
  hedef-bütçe kaydet / kopyala, kur kaydet / TCMB'den doldur.
- İzleyici (viewer) tüm raporları, grafikleri, cari özetini, kilit/yayın durumunu ve kırmızı şeridi
  görür, dosya indirebilir; düğmeleri görmez ve yazamaz (sunucu 403 döner).

## 3. Veri

Beş yeni tablo. Var olan veritabanlarında açılışta `SemaGuncelleyici` otomatik ekler (öncesinde
`kasa-once-<zaman>.db` yedeği alınır). Hepsi geçmişe (Değişiklik geçmişi) yazılır.

| Tablo | Varlık | Alanlar | Tekil |
|---|---|---|---|
| `AyKilitleri` | `AyKilidiEntity` | `Ay` (ay başı), `Etiket` ("Ağustos 2026"), `KilitZamaniUtc` | `Ay` |
| `AyYayinlari` | `AyYayinEntity` | `Ay`, `Etiket`, `YayinZamaniUtc`, `SonDegisiklikId`, `AnlikJson` (geçmişte gizli) | `Ay` |
| `KanalHedefleri` | `KanalHedefEntity` | `Ay`, `KanalId` (FK, kanal silinince silinir), `GelirHedefi` | `Ay`+`KanalId` |
| `GiderButceleri` | `GiderButceEntity` | `Ay`, `GiderKalemiId` (FK), `Tutar` | `Ay`+`GiderKalemiId` |
| `Kurlar` | `KurEntity` | `Ay`, `TufeEndeksi?`, `UsdTry?`, `EurTry?`, `AltinGramTry?` | `Ay` |

Hedef ve bütçe Id'ye bağlıdır: kanal ya da kalem adı değişse de kalır.

Motor: `HaftalikOzet`'e yalnız bilgi amaçlı `Kalemler` (işaretli kasa kalemleri, Σ = `KasaSonucu`)
eklendi. `[JsonIgnore]` olduğundan haftalık raporun JSON biçimi aynı kalır. Hiçbir toplam değişmedi.

## 4. Özellikler

### 21 · Kasa neden değişti?

- `Kasa.Core/KasaDokumu.cs`: seçilen aralıkla çakışan dönemlerin kalemlerini (tür + kanal)
  toplar ve sıralar: gelen → çek tahsilatı → cari gider → sabit gider → ortak gider →
  çek ödemesi → kart ödemesi → ertelenen K.K (geçen ayın kartsız K.K'sı); tür içinde kanal sırası.
- **Açılış + Σ adımlar = kapanış.** Açılış ilk dönemden önceki kasa devri, kapanış son dönemin
  `KasaDevir`'idir. Testler bunu haftalık raporla karşılaştırır.
- Aralık takvimde hiçbir dönemle çakışmıyorsa (takip öncesi, gelecek) 400 ve Türkçe mesaj.
- Uygulama: Haftalık'ta satıra dokununca ayrıntı kartı açılır (kanal satırları ve döküm), Aylık'ta
  "Kasa neden değişti?" kartı var. "Ayrıntı" ayrı sayfayı açar (Hafta/Ay kipi, önceki/sonraki,
  "Excel'e aktar").

### 22 · Rapordan İşlemler'e iniş

- Rota: `//islemler?baslangic=YYYY-MM-DD&bitis=YYYY-MM-DD&kanal=…(&tip=Cari|SabitGider|KrediKarti)`.
  `IslemSuzgeci.Coz` bozuk ya da tanımsız değeri yok sayar; kanal en çok 100 karakter.
- `IslemlerViewModel.SuzgecUygulaAsync` (yeni `IslemlerViewModel.B.cs`): hızlı zaman çipi ve hafta
  seçimi temizlenir, tarih + kanal (+ tip) kurulur, liste yeniden yüklenir. Tip süzgeci varken
  listenin üstünde "Yalnız … işlemleri · Tüm tipler" şeridi görünür.
- Aylık kanal satırındaki rakamlar:
  - Cari → o ay, kanal, tip Cari.
  - Sabit gider → o ay, kanal, tip Sabit gider.
  - **Kredi kartı (geçen ay)** → **bir önceki ay**, kanal, tip Kredi kartı. Kart kuralı gereği bu
    aya düşen K.K geçen ayın harcamasıdır.
  - Ortak pay → o ay, kanal `Ortak`.
  - Gelen → iniş yok (gelenler işlem değildir).
- Kasa dökümü adımları: cari/sabit gider → kanal + tip; ortak gider → kanal `Ortak`. Gelen,
  çek, kart ödemesi ve ertelenen K.K adımlarında iniş yok.
- Haftalık ayrıntı: kanal satırı → o hafta ve kanal.

### 24 · Yazdır

- `GET /api/rapor/aylik-yazdir?yil=&ay=` → `text/html; charset=utf-8`, tek A4 sayfa (`@page`):
  kanal kârlılığı, kasa (açılış, tür başına döküm, kapanış), kart borçları, çek portföyü, son kasa
  sayımı; başlıkta "Ay kilitli" / "Yayınlandı: …" durumu.
- Tüm kullanıcı metni (kanal, cari, kalem adları, notlar) HTML-escape edilir.
- Bu yanıta özel sıkı CSP (başlıkta ve `<meta>`'da):
  `default-src 'none'; style-src 'unsafe-inline'; script-src 'sha256-…'; img-src data:; base-uri 'none'; form-action 'none'`
  ve başlıkta ayrıca `frame-ancestors 'none'`. Tek betik "Yazdır" düğmesinin `window.print()`
  çağrısıdır; yalnız onun SHA-256 özeti izinlidir.
- Uygulama dosyayı `IDosyaKaydedici` ile `Belgeler\Emar Kasa\kasa-aylik-rapor-YYYY-MM.html`
  olarak kaydeder ve varsayılan tarayıcıda açar. Kaydedilen uzantılar yalnız `.csv`, `.html`,
  `.zip` olabilir (`DosyaAktarma.GuvenliAd`); başka her şey `.csv` olur.

### 04 · Grafikler

- `GET /api/rapor/grafik?yil=&ay=`: seçilen aya kadar 24 ay (eskiden yeniye), ay başına kanal
  geliri (gelen + çek tahsilatı) ve ay sonucu (nominal TL, aylık raporla aynı), takip öncesi
  bayrağı ve ayın kur satırı.
- Uygulama son 12 ayı çizer; her ayın yanında geçen yılın aynı ayı açık renkte. Kanal (Toplam ya da
  tek kanal), ölçü (gelir / ay sonucu) ve birim çipleri. Çizim `Microsoft.Maui.Graphics`
  (`GrafikCizimi : IDrawable`); veri ve dönüşüm `GrafikVerisi`'nde birim testlidir.
- Birimler:
  - Nominal TL: dönüşüm yok.
  - Reel TL: `nominal × TÜFE(referans) / TÜFE(ay)`. Referans = pencerede TÜFE'si olan **en son ay**.
  - USD / EUR: `nominal / kur(ay)`.
  - Gram altın: `nominal / gram fiyatı(ay)`.
- Gereken değer yoksa sonuç **"kur yok"** yazılır ve çubuk çizilmez. Takip başlangıcından önceki
  aylar "takip öncesi". Hiçbir değer uydurulmaz ya da enterpolasyon yapılmaz.
- Ayarlar → **Kur ve Endeks Tablosu**: ay seçici (varsayılan geçen ay), TÜFE, USD/TRY, EUR/TRY,
  gram altın. Kutu boş bırakılırsa "kur yok". Hepsi boşsa satır silinir. Türkçe biçim, en çok 4
  ondalık (`KurGiris`).
- **"TCMB'den doldur"**: `POST /api/kurlar/tcmb` ayın iş günleri için
  `https://www.tcmb.gov.tr/kurlar/YYYYMM/GGAAYYYY.xml` dosyalarını okur (en çok 4 eşzamanlı istek,
  istek başına 10 sn, toplam 30 sn). USD ve EUR döviz satış kurlarının ortalamasını alır. Tatil
  (404) günleri atlanır. Bir gün bile ağ hatasıyla alınamazsa eksik ortalama yazılmaz (502 + Türkçe
  mesaj). Yalnız USD/EUR yazılır; formdaki TÜFE ve altın kutuları korunur.

### 05 · Hedef ve bütçe

- `GET /api/hedef-butce?yil=&ay=`, `PUT /api/hedef-butce`, `POST /api/hedef-butce/kopyala`.
- Kanal hedefi gerçekleşeni = gelen + çek tahsilatı (Aylık'taki "Gelen" ile aynı).
- Kalem bütçesi gerçekleşeni = o ay o kalemle girilmiş **kartsız** sabit gider işlemleri. Karta
  bağlı sabit gider K.K sayılır. Yanında şablon (aktif tekrarlayan giderlerin aylık tutarı) gösterilir.
- Yüzde = gerçekleşen / hedef; hedef yoksa ya da 0 ise yüzde yok.
- PUT yalnız gönderilen satırları yazar; `Tutar: null` satırı siler. Uygulama yalnız değişen
  kutuları gönderir; boş kutu "hedef yok" demektir. Tutarlar `ParaGiris` ile okunur.
- Kopyala: geçen ayın satırlarını bu aya kopyalar; bu ay zaten değeri olan satır **korunur**
  (üzerine yazılmaz). Sonuç "N kopyalandı, M atlandı".
- Hedef/bütçe planlama verisidir, para kuralı değildir: ay kilidine tabi değildir.
- Aylık: kanal satırında "Hedef … ₺ · gerçekleşen … ₺ · %…" ve ilerleme çubuğu; "Sabit gider
  bütçeleri" kartı. "Hedef ve bütçe" düğmesi o ayın hedef sayfasını açar.

### 07 · Cari özeti

- `GET /api/rapor/cari-ozeti?ad=&yil=&tur=cari|kalem`.
- Cari: ay başına nakit (etkin tipi K.K olmayan işlemler), kredi kartı (karta bağlı ya da K.K
  işlemler), çek (bu kişiye verilmiş ve **ödenmiş** çekler, ödeme ayına göre), toplam, kayıt sayısı.
  Kartsız sabit gider işlemleri kaleme aittir, cariye sayılmaz.
- Kalem: ay başına girilen (kartsız sabit gider), şablon ve tekrarlayan gider kararı
  (Girildi / Atlandı). Şablondan farklı girilen tutar kırmızı.
- Ad eşleşmesi büyük/küçük harf duyarsız ve Türkçe (`Metin.EsitBuyukKucukDuyarsiz`).

### 40 · Dışa aktarma

- `GET /api/disaaktar/ay-paketi.zip?yil=&ay=`: islemler, haftalik, aylik, kasa-dokumu, gelenler,
  cekler, kart-odemeleri, kasa-sayimlari, gecmis (hepsi `-YYYY-MM.csv`) ve yazdırılabilir aylık
  HTML. Her dosya mevcut "Excel'e aktar" yazıcılarıyla üretilir; rakamlar ekrandakilerle aynıdır.
- `GET /api/disaaktar/cekler.csv?yon=&durum=` (Çekler sayfasındaki süzgeçle),
  `/api/disaaktar/kasasayimlari.csv`, `/api/disaaktar/gecmis.csv?tur=`,
  `/api/disaaktar/kasa-dokumu.csv?baslangic=&bitis=`.
- CSV'ler mevcut biçimdedir: UTF-8 BOM, `;` ayraç, Türkçe sayı. Formül enjeksiyonuna karşı `= + - @`
  ile başlayan hücreler kaçırılır.
- Çekler, Kasa Sayımı ve Geçmiş sayfalarına yalnız başlık satırına bir "Excel'e aktar" düğmesi ve
  "Kaydedildi: …" satırı eklendi. Görünüm modeli kodu ayrı dosyada (`DisaAktarmaEk.cs`, partial).

### 11 · Ay kilidi ve yayın

**Kilit (merkezî).** `[AyKilidi]` işaretli tarih alanları kaydın ayını belirler:

| Varlık | Alan |
|---|---|
| `IslemEntity` | `Tarih` |
| `GelenEntity` | `DonemStart` |
| `KartOdemeEntity` | `Tarih` |
| `CekEntity` | `IslemTarihi` (portföydeki çek kilitten etkilenmez) |
| `KasaSayimEntity` | `Tarih` |

- `AyKilidiDenetcisi` (EF `SaveChangesInterceptor`) her `SaveChanges`'te değişiklik izleyiciyi
  tarar. Eklenen, değişen ya da silinen kaydın eski **veya** yeni ayı kilitliyse `AyKilitliHatasi`
  fırlatır ve hiçbir şey yazılmaz. Grup filtresi bunu Türkçe mesajlı **409**'a çevirir. Böylece
  yazma hangi uç noktadan gelirse gelsin (işlem, gelen, çek, kart ödemesi, sayım, geri alma,
  tekrarlayan gider onayı) aynı kural uygulanır.
- K.K işlemi (kartsız ya da karta bağlı) **bir sonraki ayı da** değiştirir, o ay da denetlenir.
- Yalnız bitmiş ay kilitlenebilir. Kilit açmak satırı silmektir. İkisi de yalnız editör.

**Tüm ayları etkileyen ayarlar** (karar):

| Ayar | Kilitli ay varken |
|---|---|
| Takip başlangıcı | Değişim noktasından (eski ile yeninin küçüğü) sonra biten kilitli ay varsa **engellenir** |
| Kasa açılış devri | **Engellenir** (tüm ayların kasasını değiştirir) |
| Kanal açılış devri; açılış devri olan kanalı ekleme/silme | **Engellenir** |
| Kanal / cari / kalem adı değişimi, kanalın Aktif bayrağı | Serbest (yalnız etiket) |
| Hedef, bütçe, kur | Serbest (para kuralı değil) |

**Yayın.** "Ayı yayınla" ayın o anki rakamlarının anlık görüntüsünü (kanal satırları, kasa açılışı
ve kapanışı) ve o ana kadarki son geçmiş satırının Id'sini saklar. Yeniden yayınlamak görüntüyü
yeniler. Başlamış her ay yayınlanabilir; kilit şart değildir.

**Kırmızı şerit.** `GET /api/ay-kapanisi?yil=&ay=` bugünkü rakamları görüntüyle karşılaştırır.
Kanal ve alan başına farklar ("MEZAT · Cari gider: eski → yeni") ve yayından sonra o aya
dokunan geçmiş satırları döner. Aylık bunları kırmızı şeritte gösterir. Kilit/yayın satırları ve
yalnız ad değişimleri sayılmaz.

## 5. Uç noktalar

Hepsi `/api` altında ve oturum ister. ✎ = `Editor` politikası.

| Yöntem | Yol | Açıklama |
|---|---|---|
| GET | `/rapor/kasa-dokumu?baslangic&bitis` | 21 · kasa dökümü |
| GET | `/ay-kapanisi?yil&ay` | 11 · kilit/yayın durumu, farklar, değişiklikler |
| GET | `/ay-kapanisi/kilitler` | Kilitli aylar |
| POST ✎ | `/ay-kapanisi/kilitle` | `{yil, ay}`; bitmemiş ay 400, zaten kilitli 409 |
| POST ✎ | `/ay-kapanisi/kilit-ac` | `{yil, ay}`; kilitli değilse 409 |
| POST ✎ | `/ay-kapanisi/yayinla` | `{yil, ay}`; anlık görüntü |
| GET | `/rapor/aylik-yazdir?yil&ay` | 24 · A4 HTML (kendi CSP'si) |
| GET | `/kurlar` | Kur tablosu (en yeni önce) |
| PUT ✎ | `/kurlar` | Ayın satırı; hepsi boşsa silinir |
| POST ✎ | `/kurlar/tcmb` | `{ay}`; USD/EUR ortalaması |
| GET | `/rapor/grafik?yil&ay` | 04 · 24 aylık veri |
| GET | `/hedef-butce?yil&ay` | 05 |
| PUT ✎ | `/hedef-butce` | Değişen satırlar; `null` siler |
| POST ✎ | `/hedef-butce/kopyala` | `{ay}`; geçen aydan, var olanı korur |
| GET | `/rapor/cari-ozeti?ad&yil&tur` | 07 |
| GET | `/disaaktar/ay-paketi.zip?yil&ay` | 40 |
| GET | `/disaaktar/cekler.csv?yon&durum` | 40 |
| GET | `/disaaktar/kasasayimlari.csv` | 40 |
| GET | `/disaaktar/gecmis.csv?tur` | 40 |
| GET | `/disaaktar/kasa-dokumu.csv?baslangic&bitis` | 21/40 |

Kilitli aya dokunan **her** yazma (mevcut uç noktalar dahil): `409 {"hata": "Ağustos 2026 kilitli: …"}`.

## 6. Uygulama (MAUI)

- Yeni sayfalar: `KasaDokumuPage` (rota `kasadokumu`), `HedefButcePage` (rota `hedefbutce`),
  `GrafiklerPage` (menü, Aylık'ın altında), `CariOzetiPage` (menü, Cariler'in altında).
- Gezinme `IGezinti` → `ShellGezinti` (Shell `GoToAsync`, ana iş parçacığında). Görünüm modelleri
  Shell'e bağlı değildir; testlerde `SahteGezinti` kullanılır.
- Kayıtlar tek satırla: `MauiProgram` → `builder.Services.AddPaketB()` (`PaketBKayitlari.cs`).
- Mevcut görünüm modellerine eklemeler ayrı partial dosyalarda (`AylikViewModel.B.cs`,
  `HaftalikViewModel.B.cs`, `IslemlerViewModel.B.cs`, `AyarlarViewModel.B.cs`, `DisaAktarmaEk.cs`).
  Yeni kurucular ekstra parametre alır; eski kurucular ve testleri değişmedi.
- Tarih her yerde `Saat.Bugun(TimeProvider)`; tutarlar `ParaGiris`, kurlar `KurGiris` ile okunur.

## 7. Varsayılan kararlar

1. Reel TL referansı: grafik penceresinde TÜFE'si girilmiş en son ay.
2. Aylık'taki K.K rakamından iniş **geçen aya** gider (kart kuralı).
3. Kur formu varsayılan olarak **geçen ayı** açar (bu ayın ortalaması henüz tamam değildir).
4. TCMB ortalaması: ayın bugüne kadarki iş günlerinin döviz satış kuru. Eksik gün varsa yazılmaz.
5. İşlemler'deki **tip süzgeci istemcide** uygulanır. Sunucunun liste ucu ve mevcut istemci
   değiştirilmedi.
6. Yalnız bitmiş ay kilitlenir. Yayın için kilit gerekmez.
7. Hedef/bütçe kopyalama var olan değerin üzerine yazmaz.
8. Grafik, Hedef ve bütçe, Kasa dökümü (Ay kipi) bu ayla açılır; Cari özeti bu yılla açılır.

## 8. Bilinen sınırlar

- İşlemler'de 500'den çok kayıt olan aralıkta tip süzgeci yalnız yüklenen sayfaya uygulanır.
  "N işlem" sayısı sunucunun toplamını gösterir. İşlemler'in "Excel'e aktar"ı tip süzgecini
  dikkate almaz.
- Cari özetinden İşlemler'e iniş yoktur.
- MAUI projesi Linux'ta derlenemediği için XAML ve C# ayrı bir net10.0 projesinde kaynak üretici
  (`MauiXamlInflator=SourceGen`) ile derlenerek, bağlama yolları yansımayla denetlendi. Windows'ta
  bir kez elle açılıp bakılmalıdır.

## 9. Sunucu

Tek gereksinim: **`www.tcmb.gov.tr`'ye dışarı HTTPS (443)**, yalnız "TCMB'den doldur" için
(`deploy/README.md` → "Dışarı giden bağlantı: TCMB kurları"). Yeni tablolar açılışta otomatik
eklenir. Başka ortam değişkeni ya da ayar yoktur.
