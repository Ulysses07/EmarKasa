# Emar Kasa — Paket D: Çek, kart, sayım, tekrarlayan gider ve geri alma — Tasarım

- **Tarih:** 2026-09-24
- **Durum:** Uygulandı (dal `paket-d`).
- **İlgili spec:** `2026-09-24-cekler-design.md` (çek kuralı), `2026-07-15-kart-borc-hareketleri-design.md`
  (kart borcu), `2026-07-13-kasa-defteri-design.md` (hesap kuralları).

## 1. Amaç ve sınır

Altı özellik, hiçbir para kuralını değiştirmeden eklenir:

| No | Özellik | Özet |
|---|---|---|
| 30 | Tek tuşla çek tahsili ve kart ekstresi ödeme | Çek satırında Tahsil edildi / Ödendi / Ciro et / Karşılıksız; kartta "Ekstreyi öde" / "Tamamını öde" |
| 33 | Tekrarlayan giderler, ikinci adım | Onayda tarih/kanal/not; "Bu ay atla" geri alınır; sıklık, karta bağlı şablon, hazır vergi şablonları |
| 34 | Kart ekstresi mutabakatı | Ekstredeki dönem borcu ile uygulamanın hesabı; dönem işlemleri tik kutularıyla; kırmızı fark |
| 43 | Senet ve çek konumu | Tür (Çek/Senet), konum, ciro edilen cari, filtre, çoklu seçimde ortalama vade, risk dağılımı |
| 35 | Kasa sayımının devamı | Satırlı sayım + küpür sayacı, fark durumu ve notu, "Neden değişti?", ay ay açık fark, son sayım tarihi |
| 36 | Düzenlemeyi de geri alma | Geçmiş'te "Güncellendi" satırında "Önceki haline döndür" |

**Değişmeyenler (testle kanıtlı):** HesapMotoru çıktıları (haftalık kasa, devirler, kanal başına aylık
kâr), kart kuralı (kasa kart borcunu gerçek ödeme gününde düşer; karta bağlı olmayan eski K.K işlemi
ay sonu kuralıyla), çek kuralı ("vadede kasaya": alınan çek tahsil günü girer, verilen çek ödeme günü
çıkar). Senet çekle birebir aynı kurala tabidir. Yeni alanlar varsayılanla eklenir; eski kayıtlar
ve eski istemciler aynı sonucu üretir.

## 2. Şema (SemaGuncelleyici ile kendiliğinden)

| Tablo | Yeni sütun | Tip / varsayılan |
|---|---|---|
| `Cekler` | `Tur` | int, NOT NULL DEFAULT 0 (`Cek`) |
| | `Konum` | int, NOT NULL DEFAULT 0 (`Elde`) |
| | `CiroEdilenCari` | TEXT NULL |
| `KasaSayimlari` | `SatirlarJson` | TEXT NULL (null = eski tek tutarlı sayım) |
| | `FarkDurumu` | int, NOT NULL DEFAULT 0 (`Acik`) |
| | `FarkAciklamasi` | TEXT NULL |
| `TekrarlayanGiderler` | `Siklik` | int, NOT NULL DEFAULT 0 (`Aylik`) |
| | `KrediKartiId` | int NULL, FK → `KrediKartlari` **ON DELETE SET NULL** (tablo yeniden kurulur, veri korunur; API şablonu olan kartın silinmesine zaten izin vermez) |
| | `TutarDegisken` | int (bool), NOT NULL DEFAULT 0 |
| `KartMutabakatlari` (yeni) | `Id, KrediKartiId, DonemBaslangic, DonemBitis, EkstreTutari, HesaplananBorc, TikliIslemIdleri, Not, Durum, KayitZamaniUtc` | benzersiz (`KrediKartiId`, `DonemBitis`); FK kart silinince **CASCADE** |

Master şemasındaki bir veritabanının yükseltilip verisini koruduğu `PaketDSemaTests` ile doğrulanır
(ikinci çalıştırma hiçbir şey yapmaz).

## 3. Uçlar

Hepsi `/api` altında ve kimlik doğrulamalı; yazanlar `Editor` politikasında.

| Yöntem | Yol | Rol | Açıklama |
|---|---|---|---|
| POST | `/cekler/{id}/durum` | editör | Tek dokunuş: `{durum, tarih?, ciroEdilenCari?}` |
| GET | `/cekler/risk?tur=` | her iki | Portföydeki alınan evrak: keşideci ve banka dağılımı |
| GET | `/cekler?tur=&konum=` | her iki | Mevcut listeye iki filtre eklendi |
| GET | `/tekrarlayangiderler/atlananlar` | her iki | Bu ay + önceki 2 ayın geri alınabilen atlanan ayları (aktif şablon, bugünkü sıklığına uyan ay) |
| POST | `/tekrarlayangiderler/{id}/atlamayi-geri-al` | editör | `{ay}`: karar silinir, ay bekleyene döner (geçmişe yazılır) |
| GET/POST | `/tekrarlayangiderler/hazir` | her iki / editör | Hazır şablonlar ve `{kod}` ile ekleme |
| GET | `/kartmutabakat/donemler?krediKartiId=&adet=` | her iki | Kapanmış ekstre dönemleri (varsayılan 12, en çok 36); kesim günü değişmeden önceki kayıtlar da |
| GET | `/kartmutabakat?krediKartiId=&kesim=` | her iki | Dönem ayrıntısı |
| PUT | `/kartmutabakat` | editör | Mutabakatı yazar (dönem başına tek kayıt) |
| DELETE | `/kartmutabakat/{id}` | editör | Mutabakat kaydını siler (geri alınamaz; uygulamada Paket C'nin iki basışlı silmesiyle: "Geri alınamaz · Emin misiniz?") |
| GET | `/kasasayimlari/son` | her iki | Son sayımın tarihi ve geçen gün (Panel hatırlatması için) |
| PUT | `/kasasayimlari/{id}/fark` | editör | `{durum, aciklama}` |
| GET | `/kasasayimlari/{id}/nedendegisti` | her iki | Sayımdan sonra o günü etkileyen geçmiş satırları |
| POST | `/gecmis/{id}/geri-al` | editör | Artık "Güncellendi" satırlarını da geri alır |

Değişen mevcut uçlar (geri uyumlu): `POST/PUT /cekler` (`tur`, `konum`, `ciroEdilenCari`),
`POST /kasasayimlari` (`satirlar`), `POST/PUT /tekrarlayangiderler` (`siklik`, `krediKartiId`,
`tutarDegisken`), `GET /tekrarlayangiderler/bekleyen` (aynı alanlar), `POST .../onayla` (`kanal`, `not`).

## 4. Kurallar

### 30 · Tek dokunuş
- Yalnız **portföydeki** evrakta; aksi 409 ("Düzenle'yi kullanın"). Alınanda Tahsil edildi / Ciro et /
  Karşılıksız, verilende Ödendi. Düğmeler yalnız bu geçişlerde ve editörde görünür.
- Tarih verilmezse sunucunun bugünü (`Saat.Bugun`). Karşılıksızda tarih tutulmaz. Ciro cari ister.
- Kayıt, tam formdaki PUT ile **aynı doğrulamadan** (`CekHatasi`) geçer ve aynı satırı yazar; testler
  tek dokunuşla formun aynı kaydı ve aynı kasayı ürettiğini kanıtlar.
- Kredi Kartları: "Ekstreyi öde" ödeme formunu ekstre borcuyla, "Tamamını öde" güncel borçla ve
  bugünle doldurur. Kayıt yine "Ekle" ile mevcut kart ödemesi olarak girer.

### 43 · Senet ve konum
- `Tur`: Cek / Senet; `Konum`: Elde / BankadaTahsilde / Teminatta / Icrada (kasaya etkisi yok).
  Verilen evrak her zaman Elde kaydedilir.
- `CiroEdilenCari` yalnız "Ciro edildi" durumunda tutulur; kayıtlı bir cariyle eşleşirse onun yazımı
  kullanılır (serbest metin de kabul).
- Sayfa: tür ve konum filtresi (konum yalnız alınan evrakta), satırlarda seçim kutusu; seçilenlerin
  adedi, toplamı ve **tutar ağırlıklı ortalama vadesi** (Σ tutar × gün / Σ tutar, yarım gün yukarı).
  Risk: portföydeki alınan evrak keşideciye (`Kisi`) ve bankaya göre (banka boşsa "Banka belirtilmemiş").

### 33 · Tekrarlayan gider ikinci adım
- Onay: tarih (ileri tarih bugüne çekilir), kanal (kayıtlı kanal ya da Ortak), not. Kanal/not
  değişmediyse istek eskisiyle aynıdır.
- `Siklik`: Aylık (varsayılan) / 3 ayda / 6 ayda / yılda bir; ilk ay başlangıç ayıdır. Aylık şablon
  ve `TekrarlayanTakvim` davranışı değişmez.
- `KrediKartiId`: onaylanan ay o kartın K.K işlemi olur (mevcut kart kuralı işler). Bu şablonda kalem
  kayıtlı bir **cari**dir (K.K işlemi kayıtlı cari ister); cari yeniden adlandırılınca şablon da izler,
  şablonda kullanılan cari silinemez. Şablonun bağlı olduğu kart da silinemez (409; önce şablon silinir
  ya da başka karta bağlanır): kart bağı kopsaydı kalemi cari adı olan şablon sabit gidere dönerdi.
- `TutarDegisken`: tutar 0 olabilir; onayda tutar yazılmadan kaydedilemez.
- Panel'in nakit tahmini (Paket A) bu alanları okur: sıklığına uymayan ay düşülmez; değişken tutarlı şablon kayıtlı
  tutarıyla ("tahmini tutar") girer, tutarı yoksa girmez; karta bağlı şablon kasadan vadede değil, kartın
  ekstresine eklenip son ödeme gününde düşer.
- "Bu ay atla" geri alınır: yalnız bu ay ve önceki 2 ay; girilmiş ay geri alınamaz (409). Pasif şablonun
  kararı geri alınamaz (400; ay bekleyene dönmezdi) ve atlananlar listesinde görünmez; sıklığı sonradan
  değişen şablonun artık tekrar ayı olmayan atlanan ayı da listelenmez.
- Hazır şablonlar (kanal Ortak, tutar boş, değişken): KDV her ay 28'i; Muhtasar ve prim hizmet her ay
  26'sı; SGK primi ay sonu; Geçici vergi 17 Mayıs/Ağustos/Kasım (üç yıllık şablon); MTV 31 Ocak ve
  31 Temmuz; Emlak vergisi 31 Mayıs ve 30 Kasım. Başlangıç ayı, vadesi bugünden önce olmayan ilk
  uygun aydır. Gider kalemi yoksa eklenir; aynı kalemle şablon varsa 409. Ekranda "Tarihleri
  muhasebecinizle doğrulayın." yazar.

### 34 · Kart ekstresi mutabakatı
- Dönem: kartın kesim günüyle `(önceki kesim + 1 gün) … kesim`. Yalnız kapanmış (kesimi bugün ya da daha önce olan)
  dönemler.
- Hesaplanan borç: `KartHesap.Durum(..., kesim).GuncelBorc` (mevcut kart hesabı; değişmez).
  Devreden = başlangıçtan bir gün önceki borç; dönem harcaması ve ödemesi ayrıca gösterilir.
- Kayıt: ekstre tutarı, tiklenen işlem id'leri (dönemin kartlı işlemlerinden olmalı), not ve durum.
  Fark 0 → Mutabık; fark kabul edildiyse → Fark kabul; aksi Açık. Liste durumu bugünkü kayıtlarla
  yeniden hesaplanır; kayıttan sonra dönem değiştiyse ekranda uyarı çıkar.
- Kartın kesim günü sonradan değişirse eski kayıtlar kaybolmaz: bugünkü dönemlere denk gelmeyen kayıt,
  kayıttaki dönemiyle (`DonemBaslangic … DonemBitis`) listeye katılır (listenin kapsadığı aralıkta olanlar)
  ve o kesimle açılır, güncellenir, silinir. Kaydı olmayan eski kesim yine 400.
- Ekstre kutusu metne bağlıdır: boş kutu "yazılmadı", "0" ise "ekstre borcu sıfır"dır (hareketsiz dönem
  0 ile mutabık kaydedilir). Fazla ödenmiş kartın ekstresi eksiyle yazılır ("-250,00"; yalnız bu alan
  eksi kabul eder). Kart uygulamaya göre alacaklıyken ekstre sıfır/borç yazılırsa fark metni eksi işaretini
  hatırlatır.

### 35 · Kasa sayımı
- Satırlar (en çok 20): Nakit / Banka / POS / Diğer, ad ve tutar (≥ 0). Nakitte isteğe bağlı küpür
  sayacı (200, 100, 50, 20, 10, 5 TL; 1 TL, 50/25/10/5 kr); küpür toplamı satır tutarına eşit olmalı.
  Boş (null) satır ya da küpür 400 döner.
  `SayilanTutar` satırların toplamıdır, böylece mevcut fark ve geçmiş aynen çalışır. Satırsız
  (eski tek tutarlı) sayım aynen kaydedilir ve görünür. Varsayılan satırlar: son satırlı sayımın
  satırları, yoksa Nakit, İş Bankası, Ziraat, POS'ta bekleyen.
- Fark durumu: Açık (varsayılan) / Açıklandı (açıklama zorunlu) / Kabul edildi. Fark yoksa 400.
- "Neden değişti?": sayım kaydından sonra yazılmış, kayıt tarihi sayım gününe kadar olan ve kasayı
  etkileyebilen geçmiş satırları (işlem, gelen, çek, kart ödemesi, kanal açılış devri, kasa açılış
  devri / takip başlangıcı).
- Sayfada ay ay açıklanmamış fark (son 12 ay) ve son sayım hatırlatması (7 günden eski). Panel'deki
  7 günlük hatırlatmayı Paket A gösterir; `GET /kasasayimlari/son` onun için veriyi sağlar.

### 36 · Önceki haline döndür
- Desteklenen türler: İşlem, Çek, Gelen (tutar), Kanal (açılış devri, aktiflik, sıra; ad değişmediyse),
  Ayar (kasa açılış devri; takip başlangıcı değişmediyse). 30 gün içinde, tek kayıtlık değişikliklerde.
- Eski değerler kaydın **bugünkü haline** bindirilir ve normal kayıt doğrulamasından geçer
  (`IslemHatasi`, `CekHatasi`, `GelenHatasi`, `KanalHatasi`, `TutarHatasi`).
- Kayıt bu değişiklikten sonra yeniden değiştiyse (bugünkü hal geçmiş satırının yeni değerlerini
  içermiyorsa) 409: "önce daha yeni değişikliği geri alın". Gelen birleştirmesiyle oluşmuş satırlar
  geri alınmaz (çift sayım olmasın). Silinmiş kayıt 409.
- Dönüş geçmişe "Güncellendi (geri alındı)" olarak yazılır; asıl satır "Geri alındı" işaretlenir. Bu satırı
  okuyanlar onu da güncelleme sayar: Gelenler sayfasının eksik listesi (Paket C) kanalın aktif/pasif geçişini
  "Güncellendi (geri alındı)" satırından da okur.
- Yanıt kaydın istemciye açık halidir: Ayar dönüşü `GET /api/ayarlar` ile aynı biçimi döner (`takipBaslangic`,
  `kasaAcilisDevri`, `izleyiciSifreVarMi`); izleyici şifre özeti ve oturum sürümleri yanıta girmez.

## 5. İstemci

- `IKasaApi` ve `SahteApi` partial; Paket D uçları `IKasaApi.D.cs` / `KasaApiClient.D.cs`,
  sözleşmeler `DtosD.cs`. Yeni alanlar varsayılanlı (eski sunucudan gelen yanıt da okunur).
- Görünüm modelleri ayrı dosyalarda (`*.D.cs`, `CekEvrak.cs`, `KartMutabakatViewModel.cs`); ana
  dosyalara yalnız "Paket D" yorumlu tek satırlık kancalar eklendi.
- Sayfalar: Çekler, Kredi Kartları, Kasa Sayımı, Panel, Ayarlar ve Geçmiş'e `Paket D` başlıklı ayrı
  bölümler; yeni `KartMutabakatPage` (`kartmutabakat?kartId=` rotası, AppShell'de kayıtlı).

## 6. Varsayılan kararlar

- Tek dokunuşta tarih istemciden gönderilmez; sunucu Türkiye gününü koyar.
- Konum filtresi seçiliyken verilen evrak listelenmez (konum yalnız alınan evrakta anlamlı).
- Tür/konum filtresi istemcide uygulanır (sunucu da destekler).
- Ekstre tutarı uygulamada eksi yazılabilir (alacaklı kart); diğer para kutuları eskisi gibi eksiyi reddeder.
- Eski istemcinin PUT'u yeni alanları göndermez: `PUT /cekler/{id}` gövdede olmayan `tur`, `konum`,
  `ciroEdilenCari`; `PUT /tekrarlayangiderler/{id}` gövdede olmayan `siklik`, `krediKartiId`,
  `tutarDegisken` için kayıttaki değeri korur (başlangıç ayındaki kuralın aynısı). Alan gönderilirse
  (null dahil) gönderilen yazılır. Böylece sunucu uygulamadan önce güncellense de veri bozulmaz.
- Kart silinince mutabakat kayıtları da silinir; silmeler geçmişe "Kart mutabakatı · Silindi" olarak yazılır.

## 7. Sunucu kurulumu

Yeni yapılandırma yok. Yeni sürüm yayınlanınca şema açılışta kendiliğinden yükselir (her zamanki gibi
önce veritabanı yedeği alınır). Sunucu Windows uygulamasından önce yayınlanabilir: eski uygulamanın çek
ve tekrarlayan gider güncellemeleri yeni alanları (tür, konum, ciro, sıklık, kart, değişken tutar) korur.
