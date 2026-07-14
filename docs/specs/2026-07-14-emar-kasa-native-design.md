# Emar Kasa — Native uygulamalar (MAUI) + Kredi Kartı özelliği — Tasarım

- **Tarih:** 2026-07-14
- **Durum:** Onaylandı (brainstorm), uygulama planı bekliyor
- **Konum:** `C:\Users\burak\source\repos\Kasa` (OrderDeck/LiveDeck'ten AYRI repo, git REMOTE'u YOK)
- **İlgili spec:** `docs/specs/2026-07-13-kasa-defteri-design.md` (hesap kuralları, §4)

## 1. Amaç ve kapsam

Mevcut Kasa web uygulamasını **gerçek native** uygulamalara taşımak (WebView sarmalayıcı DEĞİL):

- **iOS + Android** telefon uygulaması (mağazada yayınlanacak)
- **Windows masaüstü** `.exe`

Ek olarak yeni bir **Kredi Kartları** özelliği (ekran + hesap motoru değişikliği).

Backend (ASP.NET Core API + SQLite) **olduğu gibi kalır**; native uygulamalar aynı REST API'ye
(`https://kasa.royalmezat.com/api`) bağlanır. Mevcut web SPA **emekliye ayrılır** (sadece API sunulur).

**Uygulama adı:** "Emar Kasa" · **Paket kimliği:** `com.royalmezat.kasa`

## 2. Çatı kararı — .NET MAUI

Tek kod tabanından gerçek native kontroller (webview değil) üretir: `net10.0-android`,
`net10.0-ios`, `net10.0-windows`. Kullanıcı .NET'çi (backend + OrderDeck.App WPF), okunur/bakımı
kolay. Windows `.exe` aynı koddan bedava çıkar.

**Elenen alternatif:** platform başına saf native (SwiftUI + Kotlin + WPF) = 3 kod tabanı, 3 kat
bakım; 5-6 kişilik iç araç için fazla.

## 3. Çözüm yapısı

Kasa reposuna eklenecek projeler:

### `Kasa.ApiClient` (yeni saf .NET kütüphanesi)
EF'e / `Kasa.Core`'a bağımlı DEĞİL (o sunucuda kalır); bağımsız test edilebilir.
- **DTO record'ları** — API JSON'unu birebir yansıtır: `KanalDto`, `CariDto`, `IslemDto`,
  `HaftalikRaporDto`, `AylikRaporDto`, `PanelDto`, `DonemDto`, `KrediKartiDto`.
- **`KasaApiClient`** — tiplı `HttpClient` sarmalayıcı:
  - `LoginAsync(kullanici, sifre)` → `(token, rol)`
  - Okumalar: `Haftalik`, `Aylik(yil, ay)`, `Panel`, `Cariler(ara?)`, `Islemler(filtre)`,
    `Donemler`, `KrediKartlari`
  - Editör mutasyonları: kanal / cari / işlem / gelen / ayar / **kredi kartı** CRUD
  - Her isteğe `Authorization: Bearer <token>` ekler.
- **`ITokenStore`** — platform-bağımsız soyutlama. MAUI `SecureStorage`'lı impl verir; testler
  bellek-içi impl verir.

### `Kasa.ApiClient.Tests` (xUnit)
Sahte `HttpMessageHandler` ile: login token ayrıştırma, Bearer ekleme, her okuma DTO eşleme, 401
akışı, editör mutasyon serileştirme.

### `Kasa.App` (MAUI head)
`net10.0-android;net10.0-ios;net10.0-windows`. MVVM (`CommunityToolkit.Mvvm`).
`Kasa.ApiClient`'e referans.

## 4. Backend değişiklikleri (`Kasa.Api` / `Kasa.Core`)

1. **Login token'ı gövdede döndür** (native token'ı okuyup `SecureStorage`'a yazsın):
   `Kasa.Api/Program.cs` login → `return Results.Ok(new { rol, token });`. Cookie kalır (zararsız).
   Mevcut 18 test bozulmaz.
2. **SPA sunumunu kaldır** (web emekli): `UseDefaultFiles`/`UseStaticFiles`/`MapFallbackToFile` +
   `wwwroot` + Dockerfile'daki `node` web build aşaması çıkarılır. API + SQLite aynı VPS'te kalır.
3. **Kredi kartı domaini** — bkz. §7.
4. CORS gerekmez (native istemci tarayıcı değil).

## 5. Kimlik doğrulama akışı

1. Açılışta `SecureStorage`'da token var mı bak.
2. Varsa → `/api/auth/me` ile doğrula + rol al. 200 → ana kabuk (rolüyle); 401 → token sil, Login.
3. **Login ekranı** (rol henüz bilinmediği için form YALNIZCA burada platforma göre uyarlanır):
   - Masaüstü (editör bağlamı): kullanıcı adı + şifre.
   - Telefon (izleyici bağlamı): sadece şifre.
4. Başarıda: token `SecureStorage`'a yazılır, rol yanıttan okunur, kabuğa geçilir.
5. Her API isteğine `Bearer <token>`. Herhangi bir **401 → token sil, Login'e dön** (oturum-ortası
   düşüşü baştan temiz karşılanır; web'deki fast-follow eksiği burada yok).
6. Çıkış: token sil, Login'e.

Roller: `editor`, `viewer` (backend'de mevcut; değişmez).

## 6. Rol-bazlı navigasyon (MAUI Shell)

Platforma göre `#if` YOK — **rol kapısı**. Windows'ta editör girer, telefonda izleyici; doğal eşleşir.

- **Editör (Windows)** — koyu yan menü (Shell flyout): Panel · Haftalık Özet · Aylık Rapor ·
  İşlemler (düzenle) · Cariler (düzenle) · **Kredi Kartları (düzenle)** · Ayarlar. Tam CRUD.
- **İzleyici (telefon)** — alt sekmeler: Panel · Haftalık · Aylık · Cariler (salt-okunur) ·
  İşlemler (salt-okunur) · **Kredi Kartları (salt-okunur)**.

| Ekran | Editör (Win) | İzleyici (telefon) |
|---|---|---|
| Panel / Haftalık / Aylık | görüntüle | görüntüle |
| Cariler | liste + ekle/düzenle | salt-okunur |
| İşlemler | liste + ekle/düzenle/sil + Gelen girişi | salt-okunur |
| Kredi Kartları | liste + ekle/düzenle/sil | salt-okunur |
| Ayarlar | evet (izleyici şifresi, kanallar) | — |

## 7. Kredi Kartları özelliği

### 7.1 Entity + CRUD (`Kasa.Core` + `Kasa.Api`)
`KrediKartiEntity`: `Id`, `Ad`, `KesimTarihi`, `SonOdemeTarihi`, `Limit` (decimal), `Borc` (decimal).
Limit ve borç **elle girilir** (bilgi amaçlı snapshot; hesaba girmez). Uçlar
`/api/kredikartlari`: GET (her oturum), POST/PUT/DELETE (Editor policy). Editör istediği kadar
kart ekler/düzenler/siler.

**Kalan limit = Limit − Borç** — görünümde hesaplanır (sunucuda saklanmaz).

### 7.2 Hesap motoru değişikliği (`HesapServisi`) — KRİTİK

Mevcut: `tip=KrediKarti` işlemi **bu ay** düşülüyor. Yeni kural: bu takvim ayındaki K.K harcaması
**bir sonraki ay sonunda**, kullanıldığı **kanaldan** düşülür (kart gelecek ay ödeniyor).

- M ayı, X kanalı için `− K.K` terimi artık **M−1 ayının** `tip=KrediKarti, kanal=X`
  harcamalarını kullanır (bir ay ileri kaydırma).
- **Kayma hem kanal aylık sonucunu HEM toplam kasa (nakit çıkışı) etkisini** kaydırır (para
  gelecek ay ödendiği için ikisi de M+1'e taşınır).
- Kart tarihleri (kesim/son ödeme) **bilgi amaçlı**; erteleme takvim ayına göre (kesim döngüsü
  DEĞİL) — basit model.
- En erken ayda M−1 verisi yoksa terim 0.

**Somut örnek:** Mart'ta MEZAT ile kartla 10.000 TL harcandı → Mart AY SONUCU'ndan ve Mart kasasından
DÜŞÜLMEZ; **Nisan ay sonunda** MEZAT kanalından ve toplam kasadan düşülür.

**Test:** Haziran doğrulama testi gibi ağır birim testleriyle kilitlenir (spec §4 formülleri +
erteleme senaryoları: ay geçişi, kanal eşleşmesi, M−1 boş, çok kanallı).

## 8. Tema / marka

`web/src/theme.ts` token'ları MAUI `Resources/Styles/Colors.xaml` + `Styles.xaml`'e birebir taşınır
(aynı hex): kâğıt zemin `#F5F4EF`, kart `#FFFFFF`, marka yeşili `#1E5F46`, koyu sidebar `#1F2A23`,
metin `#20261F`, pozitif `#1B7A4E` / negatif `#C13A2E`. IBM Plex Sans/Mono `Resources/Fonts`'a
bundle edilir; para rakamları mono. Kanal renkleri (`MEZAT/PERAKENDE/TOPTAN/Ortak`) resource olarak.
Düzen: masaüstü koyu sidebar (flyout), telefon alt sekme.

## 9. Test stratejisi

- **`Kasa.ApiClient.Tests`** (xUnit + sahte `HttpMessageHandler`) — istemci saf/hızlı testleri.
- **`HesapServisi` birim testleri** — kredi kartı erteleme dahil formül doğruluğu (kritik).
- **ViewModel testleri** — rol→görünür sekme, biçimlendirme, kalan-limit hesabı.
- **MAUI UI headless doğrulanamaz** (WPF gibi): tüm TFM'lerde build geçmeli; kullanıcı gerçek
  telefon + Windows'ta, iOS'u Mac/cihazında elle smoke test eder (mağazaya göndermeden önce).

## 10. Yayın / dağıtım — sorumluluk sınırı

- **Backend:** Kasa.Api'yi token-gövde + SPA-kaldırma ile yeniden dağıt (VPS 72.61.187.202).
- **Android:** MAUI → AAB. **Kullanıcı:** Google Play Console ($25 tek sefer) açar + yükler.
  **Claude:** imza keystore + build config + mağaza metni/görselleri.
- **iOS:** Mac + Apple Developer ($99/yıl) — **kullanıcı** kaydolur + öder. **Claude:** proje +
  bundle `com.royalmezat.kasa` + build config; kullanıcının Mac'inde `.ipa` alınıp App Store
  Connect'e gönderilir.
- **Windows:** MAUI Windows → **paketsiz `.exe`** (çift tık, mağaza yok — iç kullanım). İmzalama
  sertifikası opsiyonel (SmartScreen; iç kullanımda atlanabilir).
- **Sınır:** kod / build / imza kurulumu / mağaza metin-görsel = Claude. Hesap açma + ödeme + son
  "yayınla" tıklaması = kullanıcı (mağaza kuralı + ödeme/hesap işlemlerini Claude yapmaz).

## 11. Uygulama sıralaması (plan taslağı)

1. **Backend:** login token-gövde; kredi kartı entity + CRUD; `HesapServisi` erteleme kuralı +
   ağır testler; SPA sunumu kaldırma.
2. **`Kasa.ApiClient`** + testleri.
3. **`Kasa.App`** iskele: MAUI head, tema, Shell, auth akışı, `SecureStorage` token store.
4. **Ekranlar:** Panel/Haftalık/Aylık (okuma) → Cariler/İşlemler (rol'e göre) → Kredi Kartları →
   Ayarlar.
5. **Yayın:** Android AAB + iOS ipa + Windows exe build config; mağaza materyalleri; kullanıcı
   hesap/ödeme/gönderim adımları.

## 12. Kapsam dışı (YAGNI)

- Çevrimdışı mod / yerel önbellek (v1 çevrimiçi, web gibi).
- Telefonda editör/veri girişi (mobil salt-görüntüleme).
- Gerçek kart ekstre döngüsü (kesim tarihine göre erteleme) — basit takvim-ayı modeli seçildi.
- Push bildirim.
- Windows kod imzalama sertifikası (iç kullanımda gereksiz).
