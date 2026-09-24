# Emar Kasa — Paket E: Kullanıcılar, güvenlik ve sorular — Tasarım

- **Tarih:** 2026-09-24
- **Durum:** Uygulandı (dal `paket-e`).
- **Özellikler:** 13 (ortak başına ayrı giriş), 38 (kim, hangi cihazdan girdi), 39 (editör şifresi
  ve iki adımlı giriş), 25 (kayda soru sorma), 37 (sistem ve risk kartı).
- **Kural:** Hiçbir para kuralı değişmez. `HesapMotoru` çıktıları, kart kuralı ve çekin
  "vadede kasaya" kuralı aynı kalır. Yeni tablolar rakamlara dokunmaz.

## 1. Amaç ve kapsam

Uygulamada şimdiye kadar iki giriş vardı: `.env`'deki tek editör ve ortakların paylaştığı bir
izleyici şifresi. Kimin girdiği ve kimin neyi değiştirdiği yalnız rol düzeyinde görünüyordu.

**Hedef:**

- Her ortağa ayrı kullanıcı. Kişinin adı giriş günlüğünde ve Geçmiş'te görünür.
- Her girişin günlüğü: zaman, kişi, rol, başarılı/başarısız, neden, IP, cihaz adı. Açık
  oturumların listesi ve tek bir oturumu kapatabilme.
- Editör şifresi uygulamadan değişir. İsteğe bağlı iki adımlı giriş (TOTP) ve kurtarma kodları.
  Editör oturumu isteğe bağlı 7 gün; izleyici 30 gün.
- İzleyici bir işleme, haftaya, çeke ya da genel soru yazar. Editör cevaplar ve kapatır.
  Cevaplar herkese görünür.
- Panel'de yalnız editörün gördüğü salt okunur bir "Sistem ve risk" kartı. Gece yedek doğrulaması.

**Geriye uyumluluk:**

- Mevcut şifreler aynen çalışır.
- Güncellemeden önce verilmiş token'lar geçerli kalır.
- İki adımlı giriş, açılana kadar kapalıdır.

**Kapsam dışı:**

- E-posta ile şifre sıfırlama.
- Kullanıcı başına ayrıntılı yetki (yalnız editör ve izleyici var).
- Soruya dosya eki.
- Risk kartından bildirim gönderme.

## 2. Veri

Yeni tablolar ayrı dosyalarda tanımlanır. `SemaGuncelleyici` bunları açılışta kendiliğinden ekler.
Yeni sütunların hepsi boş olabilir (nullable).

| Tablo | Entity | Geçmiş | Not |
|---|---|---|---|
| `Kullanicilar` | `KullaniciEntity` | `Kullanıcı` | Aşağıda ayrıca anlatılıyor. |
| `GirisKayitlari` | `GirisKaydiEntity` | dışı | Her giriş denemesi. `TotpAdim`: kullanılan TOTP adımı (aynı kod iki kez geçmesin diye). Başarısız denemeler toplanır: `Tekrar` (deneme sayısı) ve `SonZamanUtc` (son deneme); bkz. §3 "Giriş günlüğünün sınırı". |
| `OturumKayitlari` | `OturumKaydiEntity` | dışı | Anahtar `Jti`. Tutulanlar: kişi, rol, cihaz, IP, başlangıç, bitiş, son görülme, kapatılma zamanı. `Eski`: güncellemeden önce açılmış oturum. |
| `GuvenlikAyarlari` | `GuvenlikAyariEntity` | `Güvenlik ayarı` | Tek satır. `EditorOturumGun`: 1–30 gün, varsayılan 30. |
| `YedekDogrulamalari` | `YedekDogrulamaEntity` | dışı | Gece doğrulamasının sonucu. Son 90 satır tutulur. |
| `Sorular` | `SoruEntity` | `Soru` | Aşağıda ayrıca anlatılıyor. |

**`Kullanicilar` tablosu:**

- Alanlar: `AdSoyad`, `KullaniciAdi`, `Rol`, `Aktif`, `Yerlesik`, `SifreHash`, `OturumSurumu`,
  `TotpSir`, `KurtarmaKodlari`, `OlusturmaUtc`.
- Kullanıcı adı tekildir (unique index). Uç ayrıca Türkçe büyük/küçük harf farkını gözetmeden de
  tekilliği denetler.
- Şifre ve iki adımlı girişle ilgili alanlar `[Gizli]`'dir: geçmişe değer yazılmaz, yalnız
  "Şifre değiştirildi" gibi bir cümle yazılır. Bu cümleler kişinin adıyla yazılır
  (`GecmisAttribute.GizlideKimlikYaz`).

**`Sorular` tablosu:**

- Alanlar: `HedefTur` (`Genel`, `Islem`, `Hafta`, `Cek`), `HedefId`, `Hafta`, `HedefOzet`, `Metin`,
  `SoranId`, `SoranAd`, `SoranRol`, `SorulmaUtc`, `Cevap`, `CevaplayanAd`, `CevaplanmaUtc`, `Durum`
  (`Acik` ya da `Kapali`), `KapanmaUtc`.
- `HedefOzet`, soru sorulduğu andaki kaydın kısa özetidir. Kayıt sonradan değişse ya da silinse
  de soru bağlamını kaybetmez.

**`Degisiklikler` tablosuna** iki sütun eklenir:

- `Kullanici`: değişikliği yapan kişinin güncel adı.
- `Cihaz`: değişikliğin yapıldığı cihaz.
- Eski satırlarda ve ortak izleyici şifresiyle yapılan değişikliklerde ikisi de boştur. Geçmiş
  satırı "Editör · EMAR · EMAR-LAPTOP" gibi görünür.

**Yerleşik editör:**

- `.env`'de `Kasa:EditorKullanici` ve `Kasa:EditorSifre` doluysa, açılışta bu editör için tek bir
  hesap açılır (`Yerlesik = true`). Açılış HTTP dışında çalıştığı için geçmişe yazılmaz.
- Ad, kullanıcı adının Türkçe büyük harflisidir ("editor" → "EDİTOR").
- `SifreHash` boş kaldıkça `.env` şifresi geçerlidir. Editör uygulamadan yeni şifre verince
  veritabanındaki şifre geçerli olur ve `.env` şifresi artık girişte kullanılmaz.
- Silinemez; rolü ve aktifliği değişmez. Kullanıcı adı `.env` ile birlikte değişir; başka bir
  kullanıcının adıyla çakışırsa değişmez ve log'a uyarı yazılır.
- `Kasa:EditorSifresiniSifirla=true` iken açılışta yerleşik editörün uygulama şifresi, iki adımlı
  girişi ve kurtarma kodları silinir. Oturumları da kapanır. Bu, sunucudan kurtarma yoludur.

## 3. Giriş ve oturum

`POST /api/auth/login` şu gövdeyi alır: `{kullanici, sifre, kod?}`. Hesap şu sırayla aranır:

1. **Kişisel hesap** (yerleşik olmayan). Kullanıcı adı Türkçe büyük/küçük harf farkı gözetilmeden
   eşleşir. Pasif hesap için 401 ve `{hata:"Bu hesap pasif. Editörle görüşün."}` döner.
2. **Yerleşik editör.** Kullanıcı adı `.env` ile birebir aynı olmalıdır; şifre ya hash'le ya da
   `.env` şifresiyle doğrulanır.
3. **Ortak izleyici şifresi.** Kullanıcı adı ne olursa olsun kabul edilir. Günlüğe "Ortak izleyici
   şifresi" olarak yazılır.

Yanlış şifrede, eskisi gibi, gövdesiz 401 döner.

**İki adımlı giriş.** Hesapta TOTP açıksa:

- Kod yoksa 401 ve `{hata, kodGerekli:true}` döner. Günlüğe `Kod bekleniyor` yazılır; bu satır
  başarısız giriş sayılmaz.
- Kod hatalıysa yine `kodGerekli` ile 401 döner.
- 15 dakikada 5 hatalı kod girilirse kod girişi kilitlenir.
- TOTP şöyle doğrulanır: HMAC-SHA1, 30 saniyelik adım, 6 hane, ±1 adım tolerans. Aynı adım iki kez
  kullanılamaz.
- Kurtarma kodu da kabul edilir: `XXXXX-XXXXX` biçiminde, 10 adet, PBKDF2 ile saklanır, her biri
  bir kez geçer.
- Kod denetimi ve tüketimi süreç içi tek bir kilidin (`GirisIslemi.KodKilidi`) altında yapılır:
  hesap kilidin içinde veritabanından yeniden okunur, TOTP adımı ya da kurtarma kodu kilit
  bırakılmadan yazılır. Aynı kodla eşzamanlı gelen girişlerden yalnız biri geçer (sunucu tek
  süreçtir).

**Token:**

- Standart claim'ler (`role`, `sv`, `jti`) aynı kalır. `iat` eklenir.
- Kişisel hesapta ek claim'ler: `kul` (kullanıcı Id), `kulsv` (kişinin oturum sürümü),
  `kulad` (ad), `cihaz`.
- Süre: editör için `GuvenlikAyari.EditorOturumGun` (varsayılan 30), izleyici için 30 gün.

**Doğrulama** (`OturumDogrulama.Dogrula`, her istekte, önbellekten okunur):

- genel oturum sürümü (`sv`) ve iptal listesi (eskisi gibi);
- kişinin hesabı: var mı, aktif mi, `kulsv` eşleşiyor mu, rol aynı mı;
- editör token'ının yaşı (`iat`) güncel `EditorOturumGun`'u aşmamış mı. Süre kısaltılırsa eski
  token'lar da bu kurala uyar. `iat`'i olmayan (Paket E öncesi) token'larda veriliş anı
  `exp − 30 gün` sayılır (`OturumDogrulama.VerilisAni`); böylece onlar da kısaltılan süreye uyar.
- `kul` claim'i olmayan eski editör token'ları yerleşik editöre aittir; kişi sürümü 0 sayılır.
  Ortak izleyici token'ları izleyici sürümüne bağlı kalır.

**Oturum kaydı:**

- Her başarılı girişte bir satır açılır.
- Son görülme zamanı en fazla 5 dakikada bir yazılır (`OturumIzleyici`).
- Güncellemeden önce açılmış bir token ilk kullanıldığında, `Eski = true` işaretiyle bir satır
  eklenir. Satırın başlangıcı token'ın veriliş anıdır (yukarıdaki kural), ilk görüldüğü an değil.
- Oturumlar listesi editör süresini bütün editör satırlarına uygular: süresi dolmuş eski ya da
  yeni oturum listede görünmez.
- Çıkışta ve oturum kapatılınca `KapatmaUtc` yazılır ve jti iptal listesine girer.

**Cihaz adı:**

- Uygulama her isteğe `X-Kasa-Cihaz` başlığını ekler. Değer, URL kodlanmış bilgisayar adıdır
  (`Environment.MachineName`, ör. `EMAR-LAPTOP`).
- Sunucu değeri temizler: denetim karakterleri atılır, en fazla 64 karakter tutulur.
- Değer girişte token'a, günlüğe ve oturum satırına yazılır.
- Token'da `cihaz` claim'i yoksa (eski token, ortak izleyici) istek başlığındaki değer kullanılır:
  geçmiş satırına ve oturum satırına yazılır. Cihazı boş kalmış oturum satırı, başlık geldiği ilk
  istekte doldurulur.

**Giriş günlüğünün sınırı** (girişsiz uç, sınırsız büyümesin):

- Başarılı girişler ve hatalı kod denemeleri (kilit sayımı için) her zaman ayrı satırdır.
- Diğer başarısız denemeler (hatalı şifre, pasif hesap, `Kod bekleniyor`) aynı saat içinde aynı
  IP'den, aynı hesaba (ya da hiçbir hesaba) ve aynı nedenle geldiyse tek satırda toplanır:
  `Tekrar` bir artar, `SonZamanUtc` güncellenir. Farklı uydurma adlar denendiyse ad
  `(çeşitli adlar)` olur.
- Günlüğe yazılan kullanıcı adı en çok 50 karakterdir; hesap araması yazılan adın tamamıyla
  yapılır (kısaltılan ad başka bir hesaba denk gelmez).
- Tanınan hesabın başarısız denemesine hesabın rolü yazılır (yerleşik editör dahil).

## 4. API

Yeni uçların hepsi kimlik ister. Yazma uçları `Editor` politikasındadır; soru yazma bunun
dışındadır ve `Soru` politikasını kullanır (editör ve izleyici).

**Hesabım (editör):**

| Uç | Ne yapar |
|---|---|
| `GET /api/hesap` | Hesap özeti; `.env` şifresi mi geçerli, iki adımlı giriş açık mı, kaç kurtarma kodu kaldı. |
| `POST /api/hesap/sifre` `{mevcutSifre, yeniSifre}` | Yanlış mevcut şifrede 400 döner. Başarıda kişi sürümü artar (diğer cihazlar çıkar) ve bu cihaza yeni token verilir. |
| `POST /api/hesap/iki-adim/baslat` | Sır ve `otpauth://` adresi üretir. Sır 10 dakika bekler. |
| `POST /api/hesap/iki-adim/onayla` `{sifre, kod}` | İki adımlı girişi açar; hesap şifresi ister (yalnız token'ı ele geçiren açamasın). Yanlış şifrede 400 "Şifre hatalı.". Kurtarma kodlarını ve yeni token'ı döner. |
| `POST /api/hesap/iki-adim/kapat` `{sifre, kod}` | İki adımlı girişi kapatır. Kurtarma kodu da geçer. |
| `POST /api/hesap/kurtarma-kodlari` `{sifre, kod}` | Yeni kodlar üretir. Şifre ve TOTP ister (kurtarma kodu geçmez). |

**Kullanıcılar (editör):**

| Uç | Ne yapar |
|---|---|
| `GET /api/kullanicilar` | Liste; son giriş, son cihaz ve açık oturum sayısıyla. `ben`: isteği yapan editörün kendi satırı. |
| `POST /api/kullanicilar` `{adSoyad, kullaniciAdi, rol, sifre}` | Ekler. Kullanıcı adı `^[\p{L}\p{N}._-]{3,50}$` kalıbına uymalı. Şifre 8–200 karakter. |
| `PUT /api/kullanicilar/{id}` `{adSoyad, rol, aktif}` | Yerleşik hesabın ve kendi hesabınızın rolü ve aktifliği değişmez (kendi hesabınızda yalnız ad). En az bir aktif editör kalmalıdır. |
| `POST /api/kullanicilar/{id}/sifre` | Şifre sıfırlar. Yerleşik hesapta ve kendi hesabınızda kullanılamaz. |
| `POST /api/kullanicilar/{id}/oturumlari-kapat` | Kişinin tüm oturumlarını kapatır. Kendi hesabınıza uygulanamaz (bu cihaz da çıkardı): diğer cihazlar Oturumlar listesinden kapatılır. |
| `POST /api/kullanicilar/{id}/iki-adim-kapat` | Kişinin iki adımlı girişini kapatır; kendinize uygulanamaz. |
| `DELETE /api/kullanicilar/{id}` | Siler; yerleşik hesap ve kendi hesabınız silinemez. |
| `DELETE /api/ayarlar/izleyici-sifre` | Ortak izleyici şifresini kaldırır; o şifreyle açılmış oturumlar kapanır. |

**Oturumlar ve günlük (editör):**

| Uç | Ne yapar |
|---|---|
| `GET /api/oturumlar` | Açık oturumlar; bu cihazın oturumu işaretlidir. |
| `POST /api/oturumlar/{jti}/kapat` | Tek bir oturumu kapatır. |
| `GET /api/guvenlik/girisler?limit&offset&basarisiz&kullaniciId` | Giriş günlüğü, en yeni önce; toplanan satırlarda `tekrar` ve `sonZamanUtc`. `basarisiz=true` `Kod bekleniyor`u içermez. Toplam satır sayısı `X-Toplam-Kayit` başlığındadır. |
| `GET /api/guvenlik/ayar`, `PUT /api/guvenlik/ayar` `{editorOturumGun}` | Editör oturum süresi, 1–30 gün. |

**Sorular** (okuma her iki rolde):

| Uç | Ne yapar |
|---|---|
| `GET /api/sorular?durum&hedefTur&hedefId&hafta` | Liste; önce açıklar, sonra en yeniler. |
| `GET /api/sorular/ozet` | Açık soru sayısı, cevap bekleyen sayısı ve son 5 açık soru. |
| `POST /api/sorular` `{hedefTur, hedefId?, hafta?, metin}` | `Soru` politikası. Metin en çok 2000 karakter. Kişi başına en çok 50 açık soru. |
| `POST /api/sorular/{id}/cevap` `{cevap, kapat=true}` | Editör. Cevap en çok 4000 karakter. |
| `POST /api/sorular/{id}/kapat`, `POST /api/sorular/{id}/ac`, `DELETE /api/sorular/{id}` | Editör. |

**Sistem (editör):**

| Uç | Ne yapar |
|---|---|
| `GET /api/sistem/risk` | Genel durum (`ok`, `sari` ya da `kirmizi`), uyarılar, son doğrulama, boş disk ve dünkü başarısız giriş sayısı. |

## 5. Risk kartı ve gece bakımı

`RiskHesaplayici` salt okunurdur. Yedek bilgisi `/health`'in zaten hesapladığı değerlerden gelir.

| Konu | Sarı | Kırmızı |
|---|---|---|
| Sunucu dışı yedek | Yapılandırılmamış, durum okunamadı ya da 48 saatten az eskimiş | Hata; hiç başarılı olmamış; 48 saatten fazla eskimiş |
| Yerel günlük yedek | Kapalı, henüz alınmamış ya da 26 saatten eski | Hata ya da 48 saatten eski |
| Yedek doğrulaması | Hiç yapılmamış ya da 48 saattir yapılmamış | Son doğrulama başarısız |
| Boş disk | 1024 MB'tan az | 300 MB'tan az |
| Dünkü başarısız giriş (Türkiye günü, `Kod bekleniyor` hariç; toplanan satır `Tekrar` kadar sayılır) | 5 ve üstü | 20 ve üstü |
| Karşılıksız alınan çek | Hepsi takipte (takip notu var ya da Paket D konumu İcrada) | Takipte olmayan var |

- Eşikler `Kasa:Risk*` ayarlarıyla değiştirilebilir (bkz. `deploy/README.md`).
- Kırmızı uyarılar önce listelenir.

**`GuvenlikBakimServisi`:**

- Açılıştan 1 dakika sonra başlar, sonra saatte bir çalışır.
- Süresi dolmuş giriş günlüğünü siler (varsayılan 180 gün, `Kasa:GirisGunluguGun`, 7–3650).
  Başarısız satırlar en çok 30 gün (ayar daha kısaysa o kadar) saklanır; sayıları 20.000'i aşarsa
  en eskileri silinir. Başarılı girişler ayardaki süre boyunca kalır.
- Bitişinden 7 günden fazla geçmiş oturum satırlarını siler.
- En yeni günlük yedeği doğrular. Aynı dosya ve aynı değişiklik zamanıyla bir kez doğrulanır.
  Doğrulama adımları:
  1. Yedek salt okunur ve havuzsuz açılır; `quick_check` çalıştırılır.
  2. Tabloların satır sayısı canlı DB ile karşılaştırılır. Liste veritabanı modelinden okunur
     (`YedekDogrulayici.Tablolar`): Paket D'nin `KartMutabakatlari`, bu paketin `Kullanicilar`, `Sorular`,
     `GuvenlikAyarlari` tabloları da sayılır; geçmiş ve günlükler (`Degisiklikler`, `IptalEdilenTokenlar`,
     `GirisKayitlari`, `OturumKayitlari`, `YedekDogrulamalari`) sayılmaz. Her sürümde bulunan 11 temel tablodan
     biri yedekte yoksa doğrulama başarısızdır; sonradan eklenmiş bir tablo yedekte yoksa yedek, sürüm
     yükseltmeden önce alınmıştır: o tablo sayılmaz ve mesajda belirtilir.
  3. Yedek dosyasının zamanından (10 dakika pay ile) sonra `Degisiklikler` tablosuna yazılan satır
     sayısı kadar fark hoş görülür.
  4. Hata mesajı "Cariler: yedekte 0, canlıda 3" biçimindedir.

## 6. Uygulama (Windows)

**Giriş ekranı:**

- Kod istenirse "Doğrulama kodu" alanı açılır. Kurtarma kodu da bu alana yazılır.
- Hatalı kodda sunucunun mesajı gösterilir.
- "Başka hesapla gir" düğmesi şifre adımına döner.
- Pasif hesap mesajı olduğu gibi gösterilir.

**Menü:**

- Menünün altında oturumdaki kişi görünür ("EMAR · Editör").
- Yeni "Sorular" sayfası her iki rolde açıktır ve Geçmiş'ten önce gelir.

**Ayarlar › Güvenlik** (`GuvenlikBolumu`, editör):

- *Hesabım:* şifre değiştirme; iki adımlı giriş kurulumu. `otpauth://` adresi QR kodu olarak
  çizilir (`QrKodu`: ISO/IEC 18004 bayt kipi, M düzeltme seviyesi, paket eklemeden;
  `QrGorunumu` GraphicsView). Sır ayrıca 4'lü gruplar hâlinde yazılır; anahtar, adres ve kurtarma
  kodları "kopyala" düğmeleriyle panoya alınır (`IPano`, Windows'ta `MauiPano`). Kurulumu onaylamak
  ve kodları yenilemek hesap şifresini de ister. Kurtarma kodları bir kez gösterilir; iki adımlı
  giriş buradan kapatılır.
- *Kullanıcılar:* ekleme (rol çipleri); düzenleme (ad, rol, aktiflik, yeni şifre); oturumlarını
  kapatma; iki adımlı girişi kapatma; iki basışlı silme; ortak izleyici şifresini kaldırma. Kendi
  satırınız "siz" diye işaretlidir; orada rol/aktiflik, şifre, oturum kapatma, iki adım kapatma ve
  silme görünmez (Hesabım ve Oturumlar bölümleri kullanılır).
- *Oturumlar:* tek tek kapatma. Bu cihazın oturumu kapatılamaz; onun için Çıkış kullanılır.
  Oturum süresi anahtarı: 7 ya da 30 gün.
- *Giriş günlüğü:* 50'lik sayfalar; "yalnız başarısızlar" süzgeci. Çip üç renklidir: Başarılı
  (yeşil), Başarısız (kırmızı), Kod bekleniyor (gri, ara adım). Toplanan satırda "25 deneme, son
  12:40" yazar.

**Panel:**

- "Sistem ve risk" kartı yalnız editörde görünür.
- "Açık sorular (N)" kartı açık soru yoksa gizlidir. "Sorulara git" düğmesi Sorular sayfasını açar.

**Kayda soru sorma:**

- İşlemler, Çekler ve Haftalık satırlarında "Soru sor" düğmesi vardır (her iki rolde).
- Düğme, kaydı `SoruYonlendirme` üzerinden Sorular sayfasına taşır ve form o kayıtla açılır.
- Satırlar sayfa görünüm modellerine dokunmaz; düğmeler statik bir komuta
  (`SoruYonlendirme.SorKomutu`) bağlanır.

**Geçmiş:** alt satır "zaman · rol · kişi · cihaz · tür" olur.

## 7. Testler

- **Api:**
  - Kullanıcı yönetimi.
  - Kişisel giriş, pasif hesap, oturum sürümleri.
  - Giriş günlüğü ve oturumlar.
  - Eski token uyumu.
  - TOTP: RFC 6238 test vektörleri, tekrar koruması, kilit, kurtarma kodları.
  - Şifre değişimi.
  - Sorular: politika, hedef özetleri, sınırlar.
  - Risk hesaplayıcı: her kural.
  - Yedek doğrulaması: başarılı, sayım farkı, bozuk dosya.
  - Şema yükseltme (master şemasından yeni tablo ve sütunlar).
  - İnceleme düzeltmeleri (`GuvenlikIncelemeTests`): başarısız denemelerin toplanması ve ad
    kısaltma, risk sayımı, bakımda süre ve satır sınırı, iki adımı açmada ve kod yenilemede şifre,
    aynı kurtarma/telefon kodunun eşzamanlı girişlerde bir kez geçmesi (dosyalı SQLite), kısaltılan
    editör süresinin `iat`'siz eski token'lara uyması, başlıktan cihaz adı, kendi hesabına yıkıcı
    işlemlerin reddi.
- **ApiClient:** yeni uçların yolları ve gövdeleri; kod isteme yanıtı; token saklama; cihaz başlığı.
- **App.Core:**
  - İki adımlı giriş akışı; oturumdaki kişi.
  - Geçmişte kişi ve cihaz.
  - Soru hedefi ve yönlendirme; Sorular sayfası; Panel kartları.
  - Hesabım, Kullanıcılar, Oturumlar; giriş günlüğü sayfalaması.
  - Risk kartı.
  - QR kodlayıcı (`QrKoduTests`): başvuru uygulamasının (Nayuki qrcodegen) matrisleriyle birebir;
    sürüm 1, 7 (sürüm bilgisi), çok bloklu L/Q/H, sürüm 40; sığmayan veri ve geçersiz maske.
  - İnceleme düzeltmeleri (`PaketEIncelemeTests`): şifre isteme, QR ve pano, "Kod bekleniyor" tonu,
    toplanan deneme metni, kendi satırındaki düğmeler.
- Mevcut testlerin beklentileri değişmedi.
