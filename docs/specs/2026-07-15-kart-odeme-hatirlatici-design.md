# Kart Ödeme Hatırlatıcı — Tasarım

**Tarih:** 2026-07-15
**Kapsam:** Kredi kartlarının kesim ve son ödeme tarihleri için Windows bildirimi (toast) + uygulama-içi "ödedin mi?" onay akışı.
**Durum:** Onaylı — uygulama planına hazır.

## Amaç

Editör, kredi kartlarının ekstre kesim ve son ödeme tarihlerini kaçırmasın. Uygulama
kapalıyken bile Windows bildirimi gelsin; bildirime tıklayınca uygulama açılıp ilgili
kartın ödeme formuna gitsin. Borcu olmayan ya da bu dönem zaten ödenmiş kartlar için
gereksiz bildirim çıkmasın.

## Kullanıcı akışı

1. Kartın **kesim günü** → "Ekstre kesildi — güncel borç ₺X" bilgi bildirimi.
2. **Son ödemeye 3 gün kala** → "Son ödemeye 3 gün — ₺X" hatırlatma.
3. **Son ödeme günü** → "Son ödeme bugün — ödedin mi?" hatırlatma.
4. Bildirimdeki **"Uygulamada işaretle"** butonu → editör açılır, Kredi Kartları
   sayfası + ilgili kart.
5. Sayfada o kartta vurgulu şerit: *"Son ödeme bugün — Ödedin mi?"* + **"Evet, ödedim"**
   → kartın ödeme formu açılır/odaklanır. Tutar girilip kaydedilince borç düşer ve o
   dönem "ele alınmış" sayılıp bir sonraki kesime kadar susar.

## Kapılama kuralı (ne zaman bildirim çıkar)

Bir kart için hatırlatma **yalnızca ikisi de doğruysa** çıkar:

1. **Ekstre borcu > 0** — açılış borcu + (yalnız kesim tarihine kadar olan harcamalar)
   − ödemeler. Kesimden *sonraki* harcamalar bu hesaba **girmez**; dolayısıyla o
   harcamalar bu dönemin hatırlatmasını tetiklemez (bir sonraki ekstreye yansır).
2. **Bu dönemde ödeme kaydı yok** — son kesim tarihinden bugüne kadar karta ödeme
   (KartOdeme) girilmemiş olmalı. Kullanıcı "Evet, ödedim" ile ödeme girince bu koşul
   bozulur ve sonraki kesime kadar susar.

Sonuçlar:
- **Borç yoksa** (ekstre borcu ≤ 0): hiçbir bildirim çıkmaz (kesim dahil).
- **Kesimde ödemişse** (ekstre borcu ≤ 0): −3 gün ve son ödeme bildirimi çıkmaz.
- **Kesimden sonra harcarsa**: o harcama ekstre borcuna sayılmaz, bu dönemi etkilemez.

**Bilinen basitleştirme:** Kısmi ödeme = dönem "ele alınmış" sayılır (susar). Tam-kapatma
ayrımı bu kapsamda yok.

## Yinelenme (recurrence)

Kartın `KesimTarihi` / `SonOdemeTarihi` alanları tek tarih tutuyor ama kredi kartı
günleri **aylık tekrar eder**. Bu tarihler **ayın günü** olarak yorumlanır:

- `SonrakiTarih(gun, referans)` = gün numarası `gun` olan, `referans`'tan büyük/eşit ilk
  tarih; ay kısa ise (ör. gün 31, Şubat) ay sonuna kırpılır.
- `SonKesim(bugün)` = gün numarası `KesimTarihi.Day` olan, bugüne küçük/eşit en son tarih
  (kapılama ve ödeme-dönemi için).

Şema değişikliği yok — yalnız `KesimTarihi.Day` / `SonOdemeTarihi.Day` kullanılır.

## Bildirim teknolojisi

**Windows App SDK `AppNotificationManager` + kullanıcı-düzeyi Windows Zamanlanmış Görev.**

`AppNotificationManager`'ın yerleşik zamanlayıcısı yoktur (yalnız anlık `Show()`), ama
paketsiz .exe için AUMID + COM aktivatör kaydını `Register()` ile yönetir ve aksiyon
butonu tıklamasını (uygulama açma/yönlendirme) düzgün taşır. "Kapalıyken tetikleme" için
onu bir Windows Zamanlanmış Görev ile eşleriz:

- Görev (admin gerekmez, kullanıcı-düzeyi) günde bir kez (sabah) `.exe --hatirlatma-kontrol`
  çalıştırır.
- Bu mod ana pencereyi açmaz; SecureStorage token'ıyla kartları+ödemeleri çeker,
  `KartHatirlatici`'yi çalıştırır, vadesi gelenler için `Show()` yapar ve çıkar.
- Her çalışmada **canlı** veri değerlendirilir → bayat toast sorunu yok (ödeme yapılmışsa
  bir sonraki kontrol zaten göndermez).

Alternatifler (elenen): klasik `ScheduledToastNotification` (iki bildirim yığınını
karıştırmak paketsiz .exe'de kırılgan, fire-and-forget canlı veriyi kontrol edemez);
Plugin.LocalNotification (üçüncü-parti bağımlılık — kullanıcı doğrudan WinAppSDK istedi).

## Bileşenler ve dosya yapısı

### Sunucu (küçük ekleme)
- `Kasa.Api` derivation: `/api/kredikartlari` yanıtına **`EkstreBorc`** alanı.
  `EkstreBorc = GuncelBorc − Σ(harcama: Islem.Tarih > SonKesim)`; burada `SonKesim` =
  `KesimTarihi.Day` gününün bugüne ≤ en son tekrarı. Mevcut türetme (GuncelBorc,
  HarcamaToplam…) korunur, tek yeni hesaplama.
- `Kasa.ApiClient` `KrediKartiDto`: sona `decimal EkstreBorc = 0m` eklenir.

### İstemci — Kasa.App.Core (saf mantık, ağır birim test)
- **`KartHatirlatici`**: girdi = kartlar (EkstreBorc + tarihler dahil) + kart ödemeleri +
  bugün. Çıktı = `IReadOnlyList<Hatirlatma>` (kart Id/Ad, tür). Yinelenme + 2'li kapı
  burada. Platform bağımsız, tamamen test edilebilir.
- **`HatirlatmaTuru`** enum: `Kesim`, `SonOdeme3Gun`, `SonOdemeGunu`.
- **`Hatirlatma`** kaydı: `int KartId`, `string KartAd`, `HatirlatmaTuru Tur`,
  `decimal EkstreBorc`, `DateOnly Tarih`.
- **`IBildirimServisi`** seam: `Task GosterAsync(IReadOnlyList<Hatirlatma>)` + kayıt/init.

### İstemci — Kasa.App (MAUI Windows)
- **`WindowsBildirimServisi : IBildirimServisi`**: `AppNotificationManager.Register()`;
  her hatırlatma için toast (başlık/metin + "Uygulamada işaretle" butonu, argümanda
  KartId); `NotificationInvoked` → uygulamayı Kredi Kartları sayfasına + karta yönlendir.
- **Headless kontrol modu**: `--hatirlatma-kontrol` argümanı; ana pencere yerine token'la
  veri çek → `KartHatirlatici` → `GosterAsync` → çık. Token yok/expired ise sessizce atla.
- **Zamanlanmış görev kaydı**: ilk normal açılışta kullanıcı-düzeyi görev yoksa oluştur
  (günlük, `.exe --hatirlatma-kontrol`).
- **Uygulama-içi hatırlatma şeridi**: `KrediKartlariPage`'de vadesi gelen kartlarda vurgulu
  *"Son ödeme bugün — Ödedin mi?"* + **"Evet, ödedim"** → kartın ödeme formunu odaklar
  (karta-özel `OdemeTutarGiris`). VM, `KartHatirlatici` çıktısını kartlara işaretler.

## Bildirim metinleri

| Tür | Başlık | Metin |
|---|---|---|
| Kesim | `{KartAd}` | "Ekstre kesildi — güncel borç {₺EkstreBorc}" |
| SonOdeme3Gun | `{KartAd}` | "Son ödemeye 3 gün — {₺EkstreBorc}" |
| SonOdemeGunu | `{KartAd}` | "Son ödeme bugün — ödedin mi?" |

## Test stratejisi

- `KartHatirlatici`: yinelenme (ay kırpma, yıl sarması), kapılama (borç yok / kesimde
  ödenmiş / kesim sonrası harcama / kısmi ödeme / ödeme dönemi), üç tür için ağır birim
  test. Hesap mantığı gibi kilitlenir.
- Sunucu `EkstreBorc`: kesim öncesi/sonrası harcama ayrımını doğrulayan entegrasyon testi.
- ApiClient DTO round-trip.
- Platform (WindowsBildirimServisi, görev kaydı, headless mod): birim-test edilemez;
  seam arkasında tutulur, manuel doğrulanır.

## Kapsam dışı

- Mobil (iOS/Android) bildirimleri — native app henüz derlenmedi; `IBildirimServisi`
  seam'i ileride platform implementasyonu eklemeye açık.
- Sunucu-güdümlü push (FCM/APNs).
- Ekstre anlık-görüntü (statement snapshot) tabanlı tam kredi kartı muhasebesi.
