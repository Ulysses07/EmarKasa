# Emar Kasa — Çekler (alınan ve verilen) — Tasarım

- **Tarih:** 2026-09-24
- **Durum:** Uygulandı (dal `cek-ozelligi`).
- **İlgili spec:** `docs/specs/2026-07-13-kasa-defteri-design.md` (hesap kuralları),
  `docs/specs/2026-07-15-kart-borc-hareketleri-design.md` (kasadan gerçek ödemeyle çıkış fikri)

## 1. Amaç ve kapsam

İşletme müşteriden çek **alıyor** (tahsil edilecek) ve tedarikçiye çek **veriyor** (ödenecek).
Bugüne kadar bunlar deftere ancak nakde döndüğü gün elle gelen/işlem olarak yazılıyordu.
Portföyde ne kadar çek beklediği ve hangilerinin vadesinin yaklaştığı ise hiç görünmüyordu.

**Hedef:**

- Çeki kendi kaydı olarak tutmak: yön, kişi, tutar, tarihler, kanal ve durum.
- Kasayı yalnız paranın gerçekten girdiği ya da çıktığı gün etkilemek.
- Portföyü, yaklaşan vadeleri ve vadesi geçenleri özetlemek.

**Kapsam dışı:** Çek bildirimi/hatırlatıcısı, ciro zinciri takibi (kime ciro edildi), kısmi
tahsilat, döviz çek. Çek verisi hiç yoksa mevcut her rakam (haftalık, aylık, panel) birebir aynı kalır.

## 2. Veri

Yeni tablo **`Cekler`** (`CekEntity`):

| Alan | Tip | Kural |
|---|---|---|
| `Id` | int | |
| `Yon` | `Alinan` / `Verilen` | |
| `CekNo` | string? | En çok 50; boşsa null |
| `Banka` | string? | En çok 100; boşsa null |
| `Kisi` | string | Zorunlu, en çok 200 (alınanda çeki veren, verilende çekin verildiği kişi/firma) |
| `Tutar` | decimal | > 0; işlemlerle aynı tutar doğrulaması (`TutarHatasi`) |
| `DuzenlemeTarihi` | DateOnly | 2000–2100 |
| `VadeTarihi` | DateOnly | 2000–2100, düzenleme tarihinden önce olamaz |
| `Kanal` | string | Var olan bir kanal; **`Ortak` yalnız verilen çekte** |
| `Durum` | `CekDurumu` | Yöne göre izinli durumlardan biri (aşağıda) |
| `IslemTarihi` | DateOnly? | Tahsil / ödeme / ciro tarihi |
| `Not` | string? | En çok 1000 |

İndeksler: `IslemTarihi` (rapor yükleme), `VadeTarihi` (liste ve özet).

### Durumlar

| Yön | İzinli durumlar |
|---|---|
| Alınan | `Portfoyde` (Portföyde), `TahsilEdildi`, `CiroEdildi`, `Karsiliksiz`, `IadeEdildi` |
| Verilen | `Portfoyde` (ekranda **"Ödenecek"**), `Odendi`, `IadeEdildi` |

- `IslemTarihi` **zorunlu**: `TahsilEdildi`, `Odendi`, `CiroEdildi`.
- `Portfoyde` durumunda `IslemTarihi` sunucuda **null'a çekilir** (eski tarih kalmaz).
- Diğer durumlarda (`Karsiliksiz`, `IadeEdildi`) isteğe bağlıdır ve kasaya etkisi yoktur.
- İşlem tarihi düzenleme tarihinden önce olamaz.

## 3. Kasa kuralı: "vadede kasaya"

Kural tek yerde, `Kasa.Core/Cek.cs` → `CekKurali.KasaHareketi` içindedir:

| Yön + durum | Kasa etkisi | Tarih |
|---|---|---|
| Alınan + `TahsilEdildi` | **+Tutar**: o kanalın **ek geleni** gibi | `IslemTarihi` |
| Verilen + `Odendi` | **−Tutar**: o kanalın **Cari gideri** gibi; kanal `Ortak` ise Ortak Cari gider gibi | `IslemTarihi` |
| Diğer her durum | **Etkisi yok** (portföyde, ciro, karşılıksız, iade) | — |

- Çek **alındığında/verildiğinde** kasa değişmez; para ancak tahsil/ödeme günü hareket eder.
- Ciro edilen alınan çek kasaya hiç girmez: çek elden çıkmıştır, nakit görülmemiştir.
- **İleri tarihli** tahsil/ödeme: rapor yüklemesi takvim sonuna (bugüne) kadardır.
  İleri tarihli işlem gibi, bu hareket de tarihi gelene kadar hiçbir rapora girmez.
- Tutar kuruşa yuvarlanarak (`Para.Yuvarla`) işlenir.

### 3.1 Haftalık (`HesapMotoru.HaftalikHesapla`)

- Hareket, `IslemTarihi`'nin düştüğü dönemde sayılır. Dönem aralığı ikili aramayla bulunur;
  karmaşıklık değişmez.
- **Kanal satırı:** `Sonuc = Gelen + CekGelen − Giden − CekGiden`. Kanal `Devir` bu sonuçla
  zincirlenir. Çek, kanal devrinde Cari gibi davranır.
- **Kasa:** `KasaSonucu = ToplamGelen + ToplamCekGelen − ToplamGiden − ToplamCekGiden`, `KasaDevir` zincirlenir.
- **Ortak** verilen çek ödemesi, Ortak Cari gider gibi yalnız kasadan düşer. Hiçbir kanal satırına yazılmaz.
- `Gelen`/`Giden`/`ToplamGelen`/`ToplamGiden` **eski anlamını korur**: çek hariçtir.
  Çek tutarları yeni `CekGelen`/`CekGiden` ve `ToplamCekGelen`/`ToplamCekGiden` alanlarındadır.
  Yeni alanlar varsayılanı 0 olan son parametrelerdir; eski çağrılar ve karşılaştırmalar bozulmaz.

### 3.2 Aylık (`HesapMotoru.AylikHesapla`)

- **Tahsil**, `IslemTarihi` ayın bir dönemine düşüyorsa o kanalın `CekGelen`'idir (gelen gibi).
- **Ödeme**, `IslemTarihi` bu aydaysa ve takip başlangıcından önce değilse o kanalın
  `CekGiden`'idir (Cari gibi).
- Çek hareketi kanalı o ay **"hareketli"** yapar; Ortak payı alacak kanallar buna göre seçilir.
- **Ortak çek ödemesi** Ortak Cari gider gibi **aynı havuzda** bölünür:
  - `paylar = KurusBol(ortakToplam)` → `OrtakPay` (çeksiz anlamı korunur).
  - `cekliPaylar = KurusBol(ortakToplam + ortakCek)`. Kanala düşen fark
    (`cekliPaylar[i] − paylar[i]`) **`CekGiden`**'e eklenir.
  - Böylece ay sonucu, aynı tutarlı bir Ortak Cari işlemle **kuruşu kuruşuna aynı** çıkar.
    Örnek: 100 ₺ Ortak çek, 3 kanal → toplam pay [33,34; 33,33; 33,33]. Bu, Ortak Cari işlemle birebir aynıdır.
- `AySonucu = Gelen + CekGelen − CariGiden − SabitGider − KrediKarti − OrtakPay − CekGiden`.

### 3.3 Servis (`HesapServisi`)

- `CekleriYukle(baslangic, bitis)` yalnız etkili olabilecek çekleri yükler: `TahsilEdildi` ya da
  `Odendi`, `IslemTarihi` dolu ve `≤ bitis`.
- `Haftalik`, `Panel` ve `HaftalikAylaraBolerek` çekleri alır. Ay ay bölerek hesap, çekleri
  `IslemTarihi`'nin ayına göre dağıtır ve tek parça hesapla **birebir aynı** sonucu verir
  (rastgele veriyle test edilir).
- `Aylik` yalnız o ayın çeklerini yükler.
- **Panel:** Güncel kasa ve kanal bakiyeleri haftalık devirden gelir, bu ayın sonucu aylık
  rapordan gelir. İkisi de çek etkisini içerir.

## 4. API

Hepsi `/api` altında, JSON'da enum'lar metin olarak taşınır (`"Alinan"`, `"TahsilEdildi"`).

| Uç | Yetki | Açıklama |
|---|---|---|
| `GET /cekler?yon=&durum=&baslangic=&bitis=` | Giriş yapmış | Filtreler isteğe bağlıdır. Tarih aralığı **vadeye** uygulanır. Sıra: vade azalan (en yeni önce), eşitte Id azalan. Geçersiz yön/durum → 400. |
| `GET /cekler/ozet` | Giriş yapmış | Portföydeki alınan toplam/adet, ödenecek verilen toplam/adet. **Vadesi 30 gün içinde** (iki yön; `bugün ≤ vade ≤ bugün+30`) ve **vadesi geçmiş ama hâlâ portföyde** listeleri vadeye göre artan sırada. |
| `POST /cekler` | Editör | Oluştur (201). |
| `PUT /cekler/{id}` | Editör | Güncelle (404 / 400). |
| `DELETE /cekler/{id}` | Editör | Sil (204 / 404). |

- Doğrulama hataları, diğer uçlar gibi `400 {"hata": "..."}` döner. Mesajlar Türkçedir,
  örneğin "Tahsil edilen çek için tahsil tarihi girilmeli.".
- İzleyici rolü yazma uçlarında 403 alır. Oturumsuz istek 401 alır.
- **Kanal yeniden adlandırma** çeklerin `Kanal` alanını da günceller.
- **Kanal silme**: kanalı kullanan çek varsa, geçmiş kaydı olan kanal gibi 409 döner ("Silmek yerine pasif yapın.").
- Rapor DTO'ları (`KanalHaftalik`, `HaftalikOzet`, `KanalAylik`) yeni çek alanlarını ek olarak döner.
  Eski istemci bu alanları yok sayar. Yeni istemci eski sunucudan gelen yanıtta bunları 0 kabul eder.

## 5. Şema

`Cekler` tablosu ve indeksleri, uygulama açılışında `SemaGuncelleyici` tarafından **otomatik**
eklenir. Bekleyen değişiklik varsa önce göç öncesi yedek alınır, log'da `tablo+ Cekler` görünür.
Elle SQL ya da DB yeniden oluşturma gerekmez.

## 6. Uygulama

- **Menü:** "Çekler", Kredi Kartları'ndan sonra gelir. İki rolde de görünür
  (`SekmeModeli`: `Bolum.Cekler`).
- **Sayfa** (`CeklerPage` + `CeklerViewModel`, VM `Kasa.App.Core`'da ve birim testli):
  - **Özet:** dört rakam. Portföydeki alınan, ödenecek verilen, vadesi 30 gün içinde (adet +
    yön bazlı tutar) ve vadesi geçmiş. Vadesi geçenler ayrıca kırmızı kutuda listelenir.
  - **Liste:** yön ve durum filtre çipleri. Durum çipleri seçili yöne göre değişir;
    verilende "Ödenecek" yazar. Satırda vade, kişi, ayrıntı, tarih, durum rozeti ve işaretli
    tutar bulunur (alınan +, yeşil; verilen −, kırmızı). Portföyde olup vadesi geçenlere
    "Vadesi geçti" etiketi konur.
  - **Editör formu** (yalnız editör):
    - Alanlar: yön, durum, kişi, tutar, çek no, banka, düzenleme ve vade tarihi, not, kanal.
    - Tahsil/ödeme/ciro tarihi alanı yalnız o durumlarda görünür. Durum tarih isteyen bir
      duruma geçtiğinde bu alan **bugüne** ayarlanır.
    - `Ortak` kanal çipi yalnız verilen çekte çıkar.
    - Formun altında seçili değerlere göre bir **kasa etkisi** cümlesi yazar. Örnek:
      "24 Eylül 2026 günü kasaya ve MEZAT kanalına girer.".
    - Satırlarda Düzenle ve Sil düğmeleri vardır.
- **Haftalık / Aylık:** Sonuç rakamının altında, yalnız çek hareketi olan satırlarda,
  "Çek +X / −Y" satırı görünür. Sonuç rakamı zaten çeki içerir; bu satır bilgi amaçlıdır.

## 7. Testler

- `Kasa.Core.Tests/CekHesapTests.cs`:
  - Kural matrisi.
  - Haftalık tahsil, kanal ödemesi ve Ortak ödeme.
  - Etkisiz durumların hiçbir rakamı değiştirmemesi.
  - Takvim dışı tarih.
  - Aylık tahsil, Ortak kuruş bölüşümü, hareketlilik ve takip başlangıcı.
  - Rastgele veriyle "çek = eşdeğer gelen/Cari işlem" denkliği.
- `Kasa.Api.Tests/CekTests.cs`:
  - CRUD ve filtreler.
  - Doğrulama mesajları.
  - İzleyicide 403, oturumsuz istekte 401.
  - Özet.
  - Haftalık, aylık ve panel rakamları.
  - Kanal adlandırma ve silme.
  - Şema otomatik oluşturma.
  - `HaftalikAylaraBolerek` denkliği.
- `Kasa.ApiClient.Tests/CekTests.cs`:
  - Sorgu dizesi, gövde ve yollar.
  - Özet eşlemesi.
  - Eski sunucuda çek alanlarının 0 gelmesi.
- `Kasa.App.Core.Tests/CeklerViewModelTests.cs`:
  - Yükleme, filtreler, form kuralları ve kasa etkisi metni.
  - Kaydet/sil akışı ve rol bölümü.
