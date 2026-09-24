# Emar Kasa — Kart borcu hareketleri (harcama + ödeme) — Tasarım

- **Tarih:** 2026-07-15
- **Durum:** Onaylandı (brainstorm), uygulama planı bekliyor
- **Konum:** `<repo>` (OrderDeck/LiveDeck'ten AYRI repo)
- **İlgili spec:** `docs/specs/2026-07-14-emar-kasa-native-design.md` (Kredi Kartı ekranı),
  `docs/specs/2026-07-13-kasa-defteri-design.md` (hesap kuralları)

## 1. Amaç ve kapsam

Bugün kredi kartı borcu ile işlemler **iki ayrı dünya**:

- Kart **harcaması** = `GiderTipi.KrediKarti` tipli işlem. Kasadan çıkışı bir sonraki ayın
  son döneminde ertelemeli düşer (`HesapMotoru`), ama kartın `Borc` alanına **dokunmaz**.
- Kartın `Borc` alanı = tamamen **elle** tutulur, hiçbir işlemle bağlı değil.
- Kart **ödemesi** = bugün bir kavram olarak **yok**.

Ayrıca İşlem formunda `GiderTipi` seçici **hiç yok** — uygulamadan girilen her işlem otomatik
`Cari` tipiyle kaydediliyor; mevcut `KrediKarti` tipli işlemler Excel içe aktarımından geliyor.

**Hedef:** Kart harcaması ve ödemesini işlemlere bağlayıp borcu bunlardan **türetmek**, ama
kullanıcı yine de bir açılış/baz borç değerini elle ayarlayabilsin.

### Kapsam dışı (dokunulmaz)
- `HesapMotoru` (kasa/kanal muhasebesi, ertelemeli K.K. mantığı) **hiç değişmez**.
- Gelen / dönem / kanal mantığı değişmez.
- Mevcut işlem akışı: `KrediKartiId` null olan işlemler bugünkü davranışı birebir korur.

## 2. Temel kararlar (brainstorm'da onaylandı)

1. **Borç kaynağı:** Hem harcama (borcu artırır) hem ödeme (borcu azaltır) otomatik etkiler;
   ek olarak elle bir açılış/baz değeri tutulur.
2. **Borç modeli — türetilmiş:**
   `GüncelBorç = AçılışBorç + Σ(kart harcamaları) − Σ(kart ödemeleri)`.
   Kanal açılış devriyle aynı mantık. İşlem/ödeme silinince-düzeltilince borç kendiliğinden
   doğrulanır.
3. **Ödeme kasaya dokunmaz:** Kart ödemesi **yalnız** borcu düşürür. Kasadan çıkış zaten mevcut
   ertelemeli K.K. mekanizmasıyla otomatik oluyor; o değişmez. Böylece çift-sayma olmaz.
4. **Giriş yerleri:** Kart **harcaması** İşlemler formundan (tip + kart seçilerek), kart
   **ödemesi** Kredi Kartları sayfasından girilir.
5. **Elle ayar:** Yalnız **Açılış borcu** alanı düzenlenerek yapılır (ayrı "güncel borcu sabitle"
   kısayolu yok).
6. **Ödeme geçmişi:** Kart detayında son ödemeler listelenir, yanlış giriş silinebilir.

## 3. Veri modeli (sunucu şeması — yıkıcı olmayan)

### a) `Islemler.KrediKartiId` (nullable int) — yeni sütun
- `null` → normal işlem (bugünkü davranış, hiç değişmez).
- Dolu → o karta ait **kart harcaması**; kaydederken `Tip = KrediKarti` olur.
- Mevcut tüm işlemler `null` başlar → borca karışmazlar.
- FK `KrediKartlari(Id)`, `ON DELETE SET NULL` (kart silinince işlem kalır, bağ kopar).

### b) `KartOdemeler` — yeni tablo
```
Id            int  PK
KrediKartiId  int  FK → KrediKartlari(Id), ON DELETE CASCADE
Tarih         date
Tutar         decimal
Not           string?  (nullable)
```
Kasa motoruna **hiç** girmez (borç-only).

### c) `KrediKartlari.Borc` — anlam değişir
- Sütun aynen kalır; anlamı artık **"açılış borcu"** (baz/elle-düzeltme değeri).
- Prod'daki mevcut `Borc` değerleri açılış olur — doğru davranış: bugünkü borç baz alınır,
  yeni hareketler üstüne biner.
- (İsteğe bağlı netlik için ileride sütun adı `AcilisBorc`'a taşınabilir; ilk sürümde
  gereksiz migration'dan kaçınmak için **isim korunur**, anlam DTO/UI seviyesinde açıklanır.)

### Türetilen güncel borç (okuma tarafında hesap)
```
GüncelBorç(kart) = kart.AcilisBorc
                 + Σ(Islemler.Tutar  | KrediKartiId == kart.Id)
                 − Σ(KartOdemeler.Tutar | KrediKartiId == kart.Id)
```

## 4. Hesap mantığı (Kasa.Core — saf fonksiyon)

`Kasa.Core`'a saf, yan-etkisiz bir yardımcı eklenir (ör. `KartHesap.GuncelBorc(...)` veya
mevcut kart tipine metot). Girdi: açılış borcu + kart harcama tutarları + kart ödeme tutarları.
Çıktı: güncel borç. **`HesapMotoru`'na dokunulmaz.**

Sunucu okuma tarafı (KrediKartlari GET), her kart için harcama toplamı + ödeme toplamını
hesaplayıp DTO'ya güncel borcu + kırılımı (açılış / harcama / ödeme) koyar.

## 5. API (sunucu)

### Islemler — mevcut uçlar korunur, gövdeye alan eklenir
- `IslemEntity` / mutasyon gövdesine `KrediKartiId` (nullable) eklenir.
- POST/PUT `KrediKartiId` dolu gelirse `Tip` sunucuda `KrediKarti`'ye zorlanır (tutarlılık).

### KartOdemeler — yeni CRUD
- `GET  /api/kartodemeler?krediKartiId={id}` → o kartın ödemeleri (tarih sırası).
- `POST /api/kartodemeler` → ödeme ekle.
- `DELETE /api/kartodemeler/{id}` → ödeme sil.
- Hepsi `RequireAuthorization("Editor")` (okuma her iki rol).

### KrediKartlari GET — DTO zenginleşir
`KrediKartiDto`'ya türetilmiş alanlar: `GuncelBorc`, `AcilisBorc`, `HarcamaToplam`,
`OdemeToplam`. (Mevcut `Borc` alanı `AcilisBorc` anlamını taşır.)

### Migration
Yıkıcı olmayan: `KartOdemeler` tablosu oluştur + `Islemler.KrediKartiId` nullable sütun ekle.
Prod'a deploy **kullanıcı onayıyla** (mevcut Kredi Kartı migration deneyimi gibi).

## 6. ApiClient + test double

- `KasaApiClient`'a `KartOdemelerAsync(id)`, `KartOdemeKaydetAsync(...)`, `KartOdemeSilAsync(id)`.
- `IslemYaz` / `IslemDto`'ya `KrediKartiId` alanı.
- `SahteApi` test double: yeni metotların çağrı kayıtları + canned yanıtlar.

## 7. UI

### a) İşlemler sayfası — kart harcaması girişi (sol form)
- **Tip seçici (çipler):** `Cari · Sabit gider · Kredi kartı`. Bugün gizli olan `DuzenTip`'i
  görünür yapar (kart olmasa da faydalı eksik giderme).
- **Kart seçici (çipler):** yalnız "Kredi kartı" tipi seçiliyken görünür; aktif kartları listeler.
  Seçim → `KrediKartiId`. Diğer tiplerde gizli ve `KrediKartiId = null`.
- Kanal seçimi aynı kalır (kart harcaması da bir kanala/Ortak'a ait olabilir).
- Mevcut kanal/filtre çip stilleri (`Chip` / `ChipText`) yeniden kullanılır.

### b) Kredi Kartları sayfası — ödeme defteri + türetilen borç
- **Borç satırı** artık **güncel borç** (türetilmiş) gösterir. Altında küçük gri kırılım:
  `Açılış {x} · Harcama +{y} · Ödeme −{z}`.
- **"Ödeme ekle"** (editör): tarih + tutar + not → `KartOdemeler`'e yazar, borç anında düşer.
- **Düzenle** formundaki "Borç" alanı → **"Açılış borcu"** etiketine döner (elle ayar buradan).
- **Ödeme geçmişi:** kart detayında son ödemeler + her satırda sil.

## 8. Test

**Core (saf fonksiyon, birim test):**
- Açılış + harcama − ödeme = güncel borç.
- Harcama işlemi silinince/düzeltilince borç doğrulanır (türetildiği için otomatik).
- Ödeme silinince borç geri artar.
- Kart silinince: ödemeler silinir (cascade), işlemlerin `KrediKartiId`'si null olur (işlem kalır).

**HesapMotoru:** değişmediği için mevcut tüm testler aynen geçmeli (regresyon kalkanı).

**ApiClient / VM:** yeni endpoint metotları + İşlem formundan kart harcaması kaydının
`KrediKartiId` + `Tip=KrediKarti` ile gittiği; ödeme ekle/sil'in borç kırılımını güncellediği.

## 9. Sürüm ve dağıtım

- Sunucu migration prod'a **kullanıcı onayıyla** deploy edilir.
- İstemci (MAUI) yeni alanları kullanır; Windows `.exe` yeniden derlenir.
- Native (iOS/Android) build'i mevcut yayın akışına takılır (Mac gerektirir, ayrı adım).
