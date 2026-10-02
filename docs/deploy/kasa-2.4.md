# Emar Kasa 2.4 — denetim düzeltmeleri, telefon arayüzü ve belge deposu (yayın taslağı)

> **Durum: TASLAK, yayın yapılmadı.** Kapsam: canlıdaki `v2.3.0` (`fce8578`, yayın `kasa:2.3.0-imports-20260923`) → `origin/release/2.x` `ec2178b` (PR #12–#29, 379 commit). Sürüm `KasaSurumu` = **2.4.0**, minimum istemci 2.4.0 (aşağıda "Karar noktaları"). Her iddia koddan ya da PR gövdesinden alındı; alınamayan ya da yalnız sunucuda ölçülebilen bilgiler **doğrulanmadı** diye işaretlidir. Sunucuya bağlanılmadı, canlı veri okunmadı.
>
> Bu belgeyle birlikte birleşmesi beklenenler: PR #30 (kalan İngilizce dosya/tip/test adlarının Türkçeleşmesi) ve ardından "Kodlar kalan aileleri" (yalnız `Kasa.Core.Kodlar` sabitleri). İkisi de yalnız ad/sabit değişikliğidir; şema, rota, DTO alanı, davranış ve log kategorisi (`Kasa.Api.EkstreImportEndpoints`) aynı kalır. Log satırlarındaki sınıf adı PR #30'dan sonra `KasaVeritabaniBaslatici` olur (bugün `KasaDatabaseInitializer`).

## Karar noktaları (yayından önce, kullanıcı)

| Karar | Bugünkü değer | Not |
| --- | --- | --- |
| Sürüm numarası | **Karar verildi: 2.4.0** (`KasaSurumu`; masaüstü `ApplicationVersion` 6 → 7) | `/api/surum` ve yeni masaüstü "2.4.0" bildirir. |
| Minimum istemci | **Karar verildi: 2.4.0** (`YonetimEndpoints.MinimumIstemci`) | Eski 2.3.0 masaüstü engellenmez; Güvenlik ekranında "Devam etmek için uygulamayı güncelleyin." yazar (`GuvenlikViewModel`). Gerekçe: 2.3.0 masaüstünde K2 sonrası "Gelen" açıklamasız düşük görünür, editör belge silemez (bölüm 7). Yeni masaüstü paketi yayınla birlikte dağıtılmalıdır; dağıtılmazsa 2.3.0 kullanıcıları uyarıyı görür ama çalışmaya devam eder. |
| `/api/surum` `notlar` metni | **Yazıldı** | "Telefon arayüzü, kapatılan ayların raporunun dondurulması, kredi girişinin ayrı satırda gösterilmesi, belge deposu, kasa kontrolü ve değişiklik geçmişi." |
| Etiket | yok | [dal-durumu.md](dal-durumu.md) A9: yalnız etiketli commit dağıtılır. `v2.4.0`, yayına karar verilince yayımlanacak commit'e atılır (kullanıcı kararı). |
| Yayın zamanı ve kesinti | — | İlk açılışta belge taşıması bitene kadar HTTP kapalı (nginx 502). Süre canlı veritabanı boyutuna bağlı; kopya provasında ölçülmeli (doğrulanmadı). Kullanıcılara bakım aralığı bildirilir. |
| Masaüstü paketinin dağıtımı | — | Girişten sonraki askıda kalma `duzeltme/maui-menu-donmasi` dalında giderildi (dotnet/maui#38813 geçici çözümü, "Bilinen konular"); paket bu dal birleşmeden dağıtılmaz. Dağıtmadan önce yine normal (etkin, ekranda) pencerede giriş sonrası bütün menüler elle denenmeli. Sunucu yeni masaüstü olmadan da yayınlanabilir. |
| Sunucu dışı yedek | runbook tablosu "Henüz yapılmadı" | `uzak_yedek.py` + systemd bu sürümle gelir; kurulum isteğe bağlı ama belge deposundan sonra önerilir. Sunucuda kurulu olup olmadığı doğrulanmadı. |

## 1. Kullanıcıya görünen değişiklikler

**Telefon arayüzü `/m/`** (PR #11, #12 içinde): telefonlarda (dokunmatik, kısa kenar < 600 px) `telefon-yonlendir.js` ziyaretçiyi `/m/`'ye gönderir; `#` kısmı korunur. Beş sekme: Panel, Haftalık, Aylık, İşlemler, Diğer (Trend, kartlar, krediler, bildirimler). Editör "Hızlı işlem"le düz gider girer (benzer kayıt kontrolüyle, varsayılan kanal yok). "Masaüstü görünümü" tam arayüze geçer ve cihazda hatırlanır. Alıcılar masaüstü arayüzüne gider. Sunucu tarafı aynı API ve çerezdir.

**İş kuralı kararları (K1–K4, 27 Eylül):**

| Karar | Etki |
| --- | --- |
| K1 — takip başlangıcından önceki giderler | Mevcut kayıtlar olduğu gibi kalır; raporda "Takip başlangıcından önce tarihli N kayıt… Kayıtlar ve tutarlar olduğu gibi korunur." uyarısı (`veriSagligiUyarisi`). Yeni gider ve tarih değişikliği reddedilir: "Gider tarihi takip başlangıcından (…) önce olamaz." |
| K2 — takipli kredi çekimi ayrı satır | Açık aylarda kredi girişi "Gelen" ve "Ay sonucu" dışında, ayrı "Kredi girişi" alanında (`krediGirisi`, kural 2). Panelin "Bu ayın sonucu" da kredi girişini içermez; haftalık kasa bakiyeleri krediyi nakit olarak içermeye devam eder. |
| K3 — kredi kartı gideri takipli karta | Yeni kredi kartı gideri yalnız yeni takipteki karta girilir: "Kredi kartı gideri için yeni takipteki bir kart seçin…". Mevcut kayıtlar aynen kalır. |
| K4 — kilitli aylar dondurulur | Kapatılmış ayın raporu kilit anındaki görüntüden döner (`dondurulmus: true`, `kuralSurumu`). Bu sürümden önce kapatılmış aylar ilk açılışta kural 1 (2.1–2.3 kuralı) ile dondurulur. |

**Denetim düzeltmelerinin diğer görünür sonuçları** (PR #12):

- Alış belgeleri ve ekstre PDF'leri veritabanı dışında belge deposunda; belge silme yumuşak ve izli, editör için gerekçe zorunlu; silinenler editöre "silinenler" listesinde görünür.
- Ekstre satırının mevcut kayıtla eşleştirilmesi; ekstreden gelen kart harcamasına sonradan alış ödemesi bağlanması.
- Takipli kartlı alış ödemesini başka alışa taşıma / alıştan ayırma; kartlı ödeme ve giderde taksit.
- Kasa kontrolü: filigran, fark varsa zorunlu açıklama ("Fark varsa açıklama girin."), farkın sonradan açıklanması, "kontrolden beri değişenler" ve kasa hareket dökümü.
- Takipsiz kart ve krediler için hatırlatmalar ve ana sayfada kalıcı geçiş uyarısı. Masaüstü eski 09:00 Windows hatırlatma görevini (`EmarKasaHatirlatici`) bir kez siler.
- Gider, gelir, kanal ve ayarlarda iyimser eşzamanlılık: iki ekrandan aynı kayıt düzenlenirse ikincisi 409 alır (ör. "Gider başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin."). Sürüm göndermeyen eski istemci eskisi gibi son yazan kazanır.
- Değişiklik geçmişi (denetim izi); eski sürümün sakladığı iptal gerekçeleri "GecmisKayit" olarak aktarılır (zamanı aktarım anıdır).
- Geri yükleme sonrası oturum dönemi, veritabanı dışı güvenlik günlüğü ve yedekten sonraki güvenlik kararlarının yeniden uygulanması (bölüm 7).
- Hız sınırları artık gerçek istemci IP'sine göre işler (nginx'in `X-Forwarded-For`'u, `UseForwardedHeaders`; 2.3.0'da yoktu).

**Editör şifresini zorla sıfırlama** (PR #27): `KASA_EDITOR_SIFRE_SIFIRLA=true` açılışta editör şifresini `KASA_EDITOR_SIFRE`'ye döndürür, kurtarma kodunu iptal eder, editör oturumlarını ve tanıdık cihazları düşürür. Aynı ortam şifresiyle ikinci kez uygulanmaz. Varsayılan `false`.

**Banka listesi ucu** (PR #27): `GET /api/ekstre-aktar/bankalar` (editör). Web ve masaüstü banka seçeneklerini buradan alır; istemcide yedek liste yok, alınamazsa hata gösterilir.

**UI düzeltmeleri** (PR #15, #16, #18, #21, #22):

- Masaüstünde ₺ işareti (Segoe UI Variable), Haftalık'ta "Dağılım bekleyen: 0,00 ₺" yalnız sıfır değilken.
- Web hata iletileri kendiliğinden kaybolmaz, "Hata iletisini kapat" ile kapanır.
- Firefox/Safari'de "‹ Eylül 2026 ›" ay seçici (aylık kasa, aylık gider, şablon ilk ayı).
- Kontrast ve erişilebilirlik (axe 53 → 0); ana sayfadaki tek başına "0" kalktı.
- Kayan nokta artıkları: "-₺0,00" → "₺0,00", "36.010000000000005" → "36.01". Tutarlar değişmedi.
- Yeni uygulama ikonu (yeşil zeminde krem "K").
- Gerekçe pencereleri açıkken oturum düşerse eski gerekçe yeni oturuma taşınmaz (PR #22 hata düzeltmesi).

**Çek ve senet takibi** ([tasarım](../specs/2026-10-01-cekler.md), [plan](../specs/2026-10-01-cekler-plan.md)): masaüstünde
"Kart, kredi ve çek" menüsünde yeni **Çekler** sayfası (izleyici ve editör görür, alıcı görmez). Alınan ve verilen çek ve senet
eklenir; tahsil, ödeme, ciro, kırdırma, dönüş, karşılıksız ve iade girilir, son hareket geri alınır. Kayıt ve vade günü kasayı
değiştirmez; kasa yalnız hareket gününde değişir. Vadeye 3 gün kala ve vade günü hatırlatma (alınan çekte vadenin 7. günü ibraz
uyarısı), panelde Çekler kutusu. Vadesi geçmiş ama ödenmemiş verilen çek ve senet panelde, Çekler şeridinde ve vadenin ertesi
günü bildirimle uyarılır. Önerilen sürüm notu: "Çek ve senet takibi: tahsil, ödeme, ciro, kırdırma, vade hatırlatması."
`/api/surum` `notlar` metnine eklenmesi yayın kararıdır.

**Kullanıcıya görünmeyenler:** PR #13, #14 (bağımlılık güvenlik sürümleri: .NET paketleri 10.0.12, MAUI 10.0.110), #17, #19, #20 (biçim), #23 (PDF araç başlarken dolan zaman sınırı artık "bozuk PDF" değil zaman aşımı olarak bildirilir), #24–#26, #28, #29 (yapı; görünüm eşdeğerliği testle sabit).

## 2. Rapor ve muhasebe etkisi

- **Kilitli (kapatılmış) aylar değişmez.** İlk açılışta, o anda kilitli olan her ay kural 1 ile dondurulur (`AyRaporAnlikGoruntusu.GecisTohumu`); tutarlar birebir aynı kalır, yanıt yalnız `dondurulmus` ve `kuralSurumu: 1` alanlarını kazanır. Karantinaya alınmış kayıt içeren kilitli ay dondurulmaz, kural 1 ile canlı hesaplanır ve uyarı taşır.
- **Kilitsiz aylarda sonucu değişebilen tek kural K2'dir:** ayda takipli (ya da eski) kredi çekimi varsa o ayın kanal "Gelen"i ve "Ay sonucu" çekim tutarı kadar düşer, tutar `krediGirisi` alanına geçer. Kredi çekimi olmayan aylarda değerler aynıdır (`krediGirisi` 0). Panelin "Bu ayın sonucu" aynı kuralla hesaplanır. Haftalık rapor ve kasa/kanal bakiyeleri değişmez.
- K1 tutarları değiştirmez (yalnız uyarı). K3 yalnız yeni girişi kısıtlar.
- Tamamlanmış ayların kanal kümesi (Ortak gider dağılımı ve rapor satırları) ilk açılışta "bugünkü" kanallarla dondurulur (`AyKanalKumesi.GecisDondurmasi`, İstanbul günü). Geçişten önce bütün aylar bugünkü kanallarla hesaplandığı için rakamlar değişmez; sonradan eklenen/pasifleşen kanal geçmiş ayları etkilemez.
- Eski kart iadelerinin hesap kaydı yazılır (`FinansTakipServisi.IadeHesabiTohumu`); kod ve log "Raporlar değişmedi" der.
- Sınama: `AltinRaporTests` bütün rapor/okuma uçlarını K1–K4 öncesi koddan üretilmiş altın çıktıyla karşılaştırır; izinli farklar yalnız yukarıdaki alanlar (`dondurulmus`, `kuralSurumu`, `krediGirisi`), kart listesinde `yeniTakip`/`aktif`, alış ödemesinde `eskiKartHarcamasi` ve gider listesinde `surum`'dur. Tohum sentetiktir; **canlı verideki farklar doğrulanmadı** — kopya provasında eski/yeni karşılaştırma zorunludur (bölüm 4, 6. adım).
- **Çek hareketleri türetilmiş satırdır** (veritabanına gider ya da gelir yazılmaz): tahsilat o günün Gelen'i, verilen çekin ödemesi
  Cari gider (Ortak kasada ayın Ortak payına bölünür), ciro aynı kasada +Gelen ve aynı tutarda Cari gider (kasa ve ay sonucu
  değişmez), kırdırma +Gelen ve masraf kadar Cari gider, dönüş ters satırlar (eksi Gelen; ciroda eksi Cari gider de). Çek girilmediği
  sürece raporlar aynıdır (`AltinRaporTests`); kapatılmış aylar dondurulmuş görüntüden döner. Kasa hareket dökümünde yeni tür "Çek".

## 3. Migration'lar

v2.3.0'a göre 15 yeni migration (`git diff --name-status v2.3.0 origin/release/2.x -- Kasa.Api/Migrations`). `InitialStableSchema.cs`'teki fark yalnız satır bölmedir. Hiçbirinin `Down`'ı yoktur (`NotSupportedException`): **geri dönüş yalnız yedekle.**

| Migration | Ne yapar | Tür |
| --- | --- | --- |
| `20260928000100_KartGecisKurali` | `TakipKartlar.EskiDusumKurali` (varsayılan 0) | Sütun ekleme |
| `20260928000200_KartGecisIzi` | `TakipKartlar.GecisAciklamasi`, `GecisOzetiJson` (NULL) | Sütun ekleme |
| `20260929000300_AyRaporAnlikGoruntuleri` | Kilitli ay rapor görüntüsü tablosu + tetikleyiciler | Tablo; açılışta veri adımı: kilitli ayları kural 1 ile dondurur |
| `20260930000100_DenetimOlaylari` | Yalnız eklenen denetim tablosu (UPDATE/DELETE tetikleyiciyle yasak) | Tablo |
| `20260930000200_DenetimGecmisAktarimi` | İptal edilmiş aylık gider ödemeleri, ekstre satırları ve alış ödemesi düzeltme/iptal önceki durumlarını `DenetimOlaylari`'na kopyalar; kaynak satırlar değişmez | Veri ekleme (SQL) |
| `20261001000100_AyKanalKumeleri` | Ay kanal kümesi tabloları + tetikleyiciler | Tablo; açılışta veri adımı: tamamlanmış ayları dondurur |
| `20261001000200_KartTakipDuzeltmeleri` | `TakipIadeHesaplari`, `TakipAvansTahsisleri` | Tablo; açılışta veri adımı: eski iadelerin hesap kaydı |
| `20261002000100_BelgeDeposuHazirlik` | `Belgeler`'e özet, yükleyen ve yumuşak silme sütunları | Sütun ekleme; ardından içerikler depoya taşınır |
| `20261002000200_BelgeDeposu` | Özeti boş belge varsa geri alınır; `Belgeler.Icerik` ve `EkstreBelgeler.Dosya` **düşürülür**; özet tetikleyicileri; ardından bir kez `VACUUM` | **Veri taşıma, geri alınamaz, uzun sürer** |
| `20261003000100_EkstreEslesmesi` | `EkstreKayitlar.EslesmeTuru`, `EslesmeId` + dizin | Sütun ekleme |
| `20261004000100_KasaKontrolFiligrani` | `KasaKontrolleri`'ne sürüm, filigran ve fark açıklaması sütunları | Sütun ekleme |
| `20261005000100_CekirdekSurumleri` | `Islemler`, `Gelenler`, `Kanallar`, `Ayarlar`'a `Surum` (0) | Sütun ekleme |
| `20261006000100_GeriYuklemeGuvenligi` | Tek satırlık `SistemDurumu`, boş oturum dönemiyle | Tablo; oturumlar düşmez |
| `20261007000100_EditorSifirlamaIzi` | `SistemDurumu.EditorSifirlamaIzi` (NULL) | Sütun ekleme |
| `20261008000100_Cekler` | `Cekler` ve `CekHareketler` (boş; kanal ve çek bağları ON DELETE RESTRICT) | Tablo |

Ayrıca başlatıcı veritabanını kalıcı **WAL** kipine alır (2.3.0'da yoktu). Bundan sonra `kasa.db-wal` ve `kasa.db-shm` veritabanının parçasıdır: dosya kopyasıyla yedek yalnız uygulama durmuşken ve üç dosya birlikte alınır.

## 4. Yayın öncesi

Komutlar sunucuda `/opt/kasa/deploy` içinde; yer tutucular (`<...>`) gerçek değerlerle değiştirilir. Genel akış [deploy/README.md](../../deploy/README.md) "Güncelleme"dir; burada bu sürüme özgü adımlar var.

1. **Kod tarafı (geliştirme makinesi):** karar verildiyse `KasaSurumu`, `MinimumIstemci` ve `notlar` PR'ı; #30 ve "Kodlar" birleşir; CI yeşil. Dağıtılacak commit etiketlenir, SHA'sı bu belgeye ve yayın manifestine yazılır. Depo kökünde `python3 deploy/temel_imaj.py` 0 ile çıkmalı.
2. **Boyut ve disk (salt okunur):**
   ```sh
   du -sh <KASA_DATA_DIR>/kasa.db
   df -h <KASA_DATA_DIR> <KASA_BACKUP_DIR> /var/lib/docker
   ```
   Gereken boş alan (canlıdaki boyutlar doğrulanmadı):
   - **Veri diski:** taşınacak belge içeriklerinin toplamı + 256 MB (kod bunu denetler, yetmezse açılış veritabanını değiştirmeden durur) + VACUUM için veritabanı boyutunun iki katına kadar (SQLite belgesi: "as much as twice the size of the original database file"). VACUUM'un geçici kopyasının konteyner içinde hangi diske yazıldığı doğrulanmadı; Docker diskine de pay bırakın.
   - **Yedek diski:** göç öncesi yedek ≈ veritabanı boyutu (yazılırken kısa süre iki katı). İlk otomatik yedek belge aynasını (`KASA_BACKUP_DIR/belgeler`) bütün belgelerle doldurur. Her olağan yedek, geçici kopya + arşiv + yeni belgelerin üstüne ayrıca `Yedek__AsgariBosAlanMb` (varsayılan **2048 MB**, `YedekServisi.AsgariBosAlanBayt`) arar; yetmezse yedek alınmaz, "Şimdi yedek indir" 507 döner. Göç öncesi yedeğin kendisi bu eşiğe bakmaz; yazamazsa açılış durur.
3. **Kopya üzerinde prova** ([database-upgrade.md](database-upgrade.md) "Canlıya geçiş sırası" 2–5): ayrı konteyner adı ve localhost portu, kopya dizini `/data`'ya. Geçiş süresini (HTTP kapalı kalacak süre), `kasa.db` boyutunu önce/sonra, belge sayısını ve eski/yeni sürümün panel, haftalık ve bütün aylık raporlarını karşılaştırın. Beklenen farklar yalnız bölüm 2'dekilerdir; başka fark varsa durun.
4. **`.env`** (`deploy/.env.example` farkı, v2.3.0'a göre):
   - `KASA_DATA_DIR`, `KASA_BACKUP_DIR` — **zorunlu**, mutlak yol; boşsa Compose durur. Değer = son manifestin `dataDirectory` = çalışan konteynerin `/data` kaynağı ([deploy/README.md](../../deploy/README.md) "Güncelleme" 2–3). Bu şablonla ilk yayındır; 2.3'te compose'u yeniden yazan depo dışı `publish_*.py` betikleri bu şablonda durur ("Depo dışı yayın betikleri").
   - `KASA_EDITOR_SIFRE_SIFIRLA=false` — yeni; olağan yayında `false` kalır.
   - `KASA_GUVENILIR_VEKILLER` — yalnız Caddy şablonu (`docker-compose.yml`) ister; canlı nginx şablonu okumaz.
5. **Compose** (`docker-compose.nginx.yml`): bağlamalar uzun sözdizimi + `create_host_path: false`; yeni `Belge__Dizin: "/data/belgeler"` (değiştirmeyin) ve `Kasa__EditorSifreSifirla`. `TZ: Europe/Istanbul`, `image: kasa:latest`, port ve bellek sınırı değişmedi. Uygulama "bugün"ü TZ'den bağımsız olarak `Europe/Istanbul` ile hesaplar (`KasaSaati`); yedek dosya adlarındaki zaman UTC'dir.
6. **Dockerfile:** `COPY Directory.Build.props ./` (yoksa `/api/surum` 1.0.0 bildirirdi; artık derleme durur), temel imajlar özetle sabit (`sdk:10.0.401@sha256:35d4…`, `aspnet:10.0.12@sha256:2d58…`). Derleme yalnız `build --pull kasa`, sonra ayrı `up -d`; `up -d --build` kullanılmaz. `.dockerignore`: `Kasa.Api/belgeler` ve `Kasa.Api/yedekler` imaja girmez.
7. **nginx** (`deploy/nginx/kasa.emarglobal.com.conf`): vekil başlıkları `server` düzeyine taşındı; uzun uçlara ayrı süreler — `/api/yedek` 16 dk (`proxy_buffering off`), `/api/disari-aktar` ve belge/PDF indirme 6 dk, ekstre/alış belgesi yükleme 3 dk. `client_max_body_size 11m` aynı. Sunucudaki site dosyası elle güncellenir; sonra `sudo nginx -t` ve `sudo systemctl reload nginx`. Sunucudaki dosyanın bugün depodaki 2.3.0 hâliyle aynı olduğu doğrulanmadı; önce `diff` alın.
8. **Yedek araçları:** kaynak aktarımıyla `/opt/kasa/deploy/restore_backup.py` ve `uzak_yedek.py` güncellenir. Eski `restore_backup.py` 2.4 yedeklerini açamaz (belgeler ZIP'te değil, manifest 2.2.0; yeni araç `--belge-aynasi <KASA_BACKUP_DIR>/belgeler` ister). Uzak yedek kuruluysa `systemd/` birimleri de yeniden kurulur ([operasyon-runbook.md](operasyon-runbook.md) "Sunucu dışı yedek" 5).
   - **Ne zaman:** Yayın sırasında güncellenir, önceden değil. 30 Eylül'de yerelde denendi: yeni betik 2.3.0 yedeğini açar ve 2.3.0 bu dosyayla sorunsuz çalışır (veri, rapor, giriş ve yazma aynı). Ancak betik dosyaya geri yükleme işareti koyar: `user_version` ve `__KasaGeriYukleme`. 2.3.0 bu işareti silmez. Sonradan 2.4'e geçilince dosya bir kez daha "geri yüklenmiş" sayılır:
     - bütün oturumlar kapanır;
     - izleyici girişi kapanır;
     - kayıt numaraları 1.000.000 ileri alınır;
     - raporda "Bu andan sonra girilen kayıtlar yedekte yok" satırı çıkar. Bu satır yanıltıcıdır: o kayıtlar dosyada durur.
   - **2.3.0 döneminde acil geri yükleme:** Eski betik (`restore_backup.py.2.3.0` olarak saklanır) kullanılır. Yeni betik kullanıldıysa 2.3.0'ı başlatmadan önce işaret temizlenir: `sqlite3 <dosya> 'PRAGMA user_version=0; DROP TABLE "__KasaGeriYukleme";'`. Betiğin yazdığı "ZORUNLU" adımlar yalnız 2.4 içindir; 2.3.0 bunları uygulamaz.
   - **Python sürümü:** Yeni betik Python 3.8 veya üstünü ister. Sunucudaki sürüm doğrulanmadı.
9. **Güvenlik günlüğü:** ilk açılışta `KASA_BACKUP_DIR/guvenlik-gunlugu.jsonl` oluşur. Silinmez, elle düzenlenmez, yedek dizini temizliklerinde hariç tutulur.
10. **Son yedek:** yayından hemen önce uygulamada elle yedek; uzak yedek kuruluysa `sudo systemctl start kasa-uzak-yedek.service`. README "Güncelleme" 4 (compose ve çalışan imaj kimliği) atlanmaz.

## 5. İlk açılış

Sıra (`Program.cs`, `KasaDatabaseInitializer.Initialize`), tamamı HTTP açılmadan:

1. **Göç öncesi yedek** (`YedekServisi.GocOncesiYedekAl`): `KASA_BACKUP_DIR/kasa-goc-oncesi-YYYYMMDD-HHMMSS-xxxxxxxx.zip`, SQLite yedekleme API'si + `integrity_check`; belgeleri BLOB olarak taşıyan eski biçim (geri dönüş kaynağı). Alınamazsa hiçbir iş çalışmaz. Rotasyon silmez.
2. Belge deposu disk denetimi (içerik toplamı + 256 MB).
3. Ekstre PDF'leri depoya yazılır, kayıtlı `DosyaOzeti` ile karşılaştırılır; uyuşmazlıkta açılış veritabanını değiştirmeden durur.
4. `BelgeDeposuHazirlik`; alış belgeleri satır satır aktarılır (fsync, geri okuma, özet). Kesintide yeniden başlatma kaldığı yerden sürer.
5. Kalan migration'lar (`Migrate`), `BelgeDeposu` dahil; ardından bir kez `VACUUM`, sonra WAL.
6. Veri adımları: kanal kümesi dondurma → kilitli ay görüntüleri (kural 1) → kart iade kaydı.
7. Geri yükleme işareti yoksa geri yükleme işlemi yapılmaz; güvenlik günlüğü hazırlanır; bayrak kapalıysa editör şifresine dokunulmaz.
8. Depoda bulunmayan belge varsa hata logu. Ardından HTTP açılır.

**HTTP kapalı süre:** belge taşıma + VACUUM süresince nginx 502 döner. [database-upgrade.md](database-upgrade.md): disk G/Ç'si yaklaşık veritabanı boyutunun 10 katı, SSD'de GB başına birkaç dakika. Canlı süre doğrulanmadı; 4. bölüm 3. adımdaki provada ölçün. Aynı anda ikinci konteyner başlatmayın.

**Beklenen log satırları** (`docker compose -f docker-compose.nginx.yml logs -f kasa`; sayılar/yollar değişken):

```
Göç öncesi yedek hazır: /yedekler/kasa-goc-oncesi-….zip. Bekleyen işler: 20260928000100_KartGecisKurali, …, Belge içeriklerinin belge deposuna aktarımı: …
Belge deposu geçişi başlıyor: N alış belgesi, M ekstre PDF'i (X MB) → /data/belgeler.
M ekstre PDF'i belge deposuna yazıldı ve özetleri doğrulandı.
Belge deposuna aktarım sürüyor: i/N alış belgesi (X MB).
N alış belgesi (X MB) belge deposuna aktarıldı ve doğrulandı.
Belge içerikleri veritabanından çıkarıldı ve veritabanı sıkıştırıldı: A MB → B MB.
Tamamlanmış K ayın kanal kümesi (…) bugünkü kanallarla donduruldu: ….
Bu sürümden önce kilitlenmiş K ayın raporu kural 1 ile donduruldu: 2026-…
Kart iadelerinin hesap kaydı yazıldı: … Raporlar değişmedi.
Now listening on: http://[::]:8080
```

Belge yoksa belge satırları, kilitli ay/iade yoksa ilgili satırlar çıkmaz. Durdurucu hatalar (veritabanını **silmeyin**; göç öncesi yedeği ve iletiyi inceleyin):

- "Kasa veritabanı güncellenmeden önce göç öncesi yedek alınamadı: … Veritabanı değiştirilmedi." — yedek dizini, disk, izin.
- "Belge deposuna (…) N belge (… MB) taşınacak ancak diskte … MB boş alan var …" — veri diski.
- "… içeriği kayıtlı özetle eşleşmiyor … veritabanı değiştirilmedi" — o belgeyi ve göç öncesi yedeği inceleyin.
- Uyarı (açılışı durdurmaz): "Belge deposu geçişinden sonra VACUUM çalıştırılamadı; veriler doğrudur, veritabanı dosyası küçülmedi." ve "Veritabanı WAL kipine alınamadı …".

Yayın mevcut oturumları düşürmez (`SistemDurumu` boş oturum dönemiyle tohumlanır; migration açıklaması). Canlıda doğrulanmadı; kullanıcılara gerekirse yeniden giriş denebilir.

## 6. Geri yükleme prosedürü değişti

Geri yükleme artık `.env`'deki editör şifresini kendiliğinden geçerli kılmaz. Yedekten sonra editör şifresi değişmişse (güvenlik günlüğünden) **editör girişi kilitlenir**; kilit özet alanına hiçbir şifreyle doğrulanamayan sabit yazılarak yapılır, eski imaja dönülse de açılmaz (PR #27). Her geri yüklemede ([operasyon-runbook.md](operasyon-runbook.md) "Geri yüklemeden sonra"):

1. Uygulamayı geri yüklenen dosyayla açmadan **önce** `deploy/.env`'de `KASA_EDITOR_SIFRE`'yi yeni, en az 12 karakterlik, daha önce kullanılmamış bir değere çevirin; `KASA_EDITOR_SIFRE_SIFIRLA=true`.
2. `docker compose -f docker-compose.nginx.yml up -d` — `restart` değil; `restart` ortam değişikliğini almaz.
3. Log: "Geri yüklenmiş veritabanı tanındı ve işlendi …" ve "Editör şifresi ortamdaki Kasa:EditorSifre değerine sıfırlandı …".
4. Editör girer, hemen yeni şifre ve kurtarma kodu belirler; yeni izleyici şifresi; alıcılar ve bildirimler gözden geçirilir; geri yükleme raporu okunur (Araçlar > Yedekleme / masaüstünde Güvenlik).
5. `KASA_EDITOR_SIFRE_SIFIRLA=false` + yeniden `up -d`.

Yedek her zaman `restore_backup.py` ile açılır (ZIP'ten elle çıkarılmaz); sunucu yedeğinde `--belge-aynasi <KASA_BACKUP_DIR>/belgeler`, uzak kopyada önce hedefin `belgeler/` klasörü indirilir.

## 7. Eski istemci uyumu (canlıdaki 2.3.0 masaüstü)

Sunucu 2.3.0 istemcisini çalıştırmaya devam eder, ancak `minimumIstemci` 2.4.0 olduğundan Güvenlik ekranında güncelleme uyarısı görünür. Bilinen farklar:

| Durum | 2.3.0 masaüstünde ne olur | Kaynak |
| --- | --- | --- |
| Editör belge siler | 400 "Belge silme gerekçesi girin." — 2.3.0 `BelgeSilAsync` gövdesiz `DELETE` gönderir; editör belge silemez (web ya da yeni masaüstü kullanılır). Alıcı kendi belgesini gerekçesiz silebilir. | `BelgeEndpoints`, `BelgeSilmeTests`, v2.3.0 `KasaApiClient.Yonetim.cs` |
| Kasa kontrolünde fark varken not boş | 400 "Fark varsa açıklama girin." Not alanı doldurulunca kaydedilir. | `KasaKontrolEndpoints`, PR #12 |
| Yeni kredi kartı gideri takipsiz karta | Reddedilir: "Kredi kartı gideri için yeni takipteki bir kart seçin…" (2.3.0 formu takip durumunu bilmez). | `KayitGirdileri`, K3 |
| Takip başlangıcından önce tarihli gider | Reddedilir (K1). | `KayitGirdileri` |
| Aylık rapor | Rakamlar sunucuda kural 2 ile hesaplanır; 2.3.0 `AylikRaporDto`'da `krediGirisi` yok: kredi çekimi olan açık ayda "Gelen"/"Ay sonucu" düşük görünür, "Kredi girişi" satırı görünmez. Uyarı ve "dondurulmuş" işareti gösterilmez. | v2.3.0 `Kasa.ApiClient/Dtos.cs` |
| Aynı kaydı iki ekrandan düzenleme | Eski istemci sürüm göndermez: denetlenmez, son yazan kazanır. | `CekirdekSurumSozlesmeTests.Eski_istemcinin_surumsuz_govdesi_kabul_edilir_surum_yine_artar` |
| Ekstre banka listesi | 2.3.0 kendi gömülü listesini kullanır (değişmedi). | PR #27 |

Yeni masaüstü paketi dağıtılmadıkça editör belge silme ve kredi girişi görünümü için web arayüzü kullanılır. Minimum istemciyi 2.4'e çekmek yalnız uyarı gösterir, engellemez (karar noktası).

## 8. Yayın sonrası doğrulama

- [ ] `curl --fail https://kasa.emarglobal.com/health` → `{"durum":"ok"}`.
- [ ] `curl -s https://kasa.emarglobal.com/api/surum` → `surum` karar verilen değer (1.0.0 değil), `minimumIstemci`, `notlar`.
- [ ] `docker inspect kasa-app` `/data` kaynağı = `KASA_DATA_DIR`; `ls <KASA_DATA_DIR>/belgeler`, `kasa.db` küçüldü, `kasa.db-wal` var.
- [ ] Log: bölüm 5'teki satırlar; "belge içeriği belge deposunda (…) bulunamadı" hatası yok; Ayarlar'da vekil uyarısı yok.
- [ ] Telefonda `https://kasa.emarglobal.com/` → `/m/` açılıyor (2.3.0'da 404 idi); giriş, Panel, Haftalık, Aylık; editörle Hızlı işlem (deneme kaydı gerekirse iptal/silme ile).
- [ ] Panel, haftalık ve bütün aylık raporlar: yayın öncesi alınan eski sürüm çıktısıyla karşılaştırma (provadaki yöntem). Kilitli aylar birebir + `dondurulmus`; açık aylarda fark yalnız K2.
- [ ] Elle yedek alınıyor ve indiriliyor (16 dk süre sınırı); ertesi gün otomatik yedek ve `KASA_BACKUP_DIR/belgeler` aynası; Ayarlar > Yedekleme disk göstergesi.
- [ ] Uzak yedek kuruluysa `uzak_yedek.py gonder --kuru` hatasız.
- [ ] Bir alış belgesi ve bir ekstre PDF'i indiriliyor (depodan, bayt bayt aynı olmalı).
- [ ] Ekstre: banka listesi geliyor; sentetik ya da bilinen bir PDF önizleniyor (kaydetmeden).
- [ ] Bildirimler: ayar ekranı açılıyor, bir cihazda test bildirimi.
- [ ] Masaüstü 2.3.0: giriş, Kasalar, Haftalık, Aylık; yeni masaüstü dağıtıldıysa aynısı + Güvenlik'te "Uygulama … · sunucu …".
- [ ] İzleyici ve alıcı girişi.
- [ ] Hata/yeniden başlama sayısı 0 (`docker inspect kasa-app --format '{{.RestartCount}}'`); yayın manifestine etiket, SHA, `dataDirectory`, göç öncesi yedek adı yazıldı.
- [ ] Belgeler: [deploy/README.md](../../deploy/README.md) ve [README](../../README.md)'deki "Güncel yayın 2.3.0" satırları ve bu belgenin "yayın sonucu" bölümü güncellenir.

## 9. Elle bakılacaklar (PR gövdelerinden, birleşik)

Otomatik testlerin ve e2e ekran görüntülerinin kapsamadığı yerler; ekran ekran:

- **Genel (masaüstü MAUI):** ₺ ve metinlerin Segoe UI Variable ile çizilmesi, eski yarı kalın yerlerin kalın görünmesi; Windows 10'da Segoe UI'ye düşüş (denenmedi). Yeni ikon görev çubuğunda, Başlat'ta, 16–24 px'te (eski mor ikon önbellekte kalabilir). Kodla kurulan alanların kenarlığı (`TakipUi.Girdi`, `GuvenlikAlani`, `DisariAktarPage`).
- **Giriş ve menü:** editör, izleyici, alıcı girişinde menü öğeleri; çıkışta gizlenme.
- **Kasalar:** yükleniyor/hata/"Yenile" şeridi, boş kanal listesi, Kasa kontrolü kartı ve "Genel kasa" döküm seçeneği.
- **Haftalık, Aylık:** şerit ve boş liste; Aylık'ta sarı/yeşil uyarı kutuları; "Dağılım bekleyen" yalnız sıfır değilken.
- **İşlemler:** gider/gelir formu yalnız editörde; ekstre "Kaynak" düğmesi; 6 çip grubu (seçili/seçili değil, tıklama), zaman çiplerinin alt boşluğu, dönem Picker'ları, benzer kayıt uyarısı, boş liste, hata kutusu, yeşil ileti.
- **Alışlar (web ve masaüstü):** ayrıntıda etiketler ve "Kanal dağılımı bekliyor" rozeti, ödeme satırları; yeni/düzenle penceresi; ödeme kaydet (Ara, Daha eski giderler, kart harcaması, taksit); düzelt/taşı ve iptal; belge ekle/kaldır; durum geçişleri, onay/iade; alıcı hesabıyla akış; "Kartı aç" yalnız editörde; şerit boşken dağılım kutusunun en üstte başlaması; sarı kutular, yeşil kart üzerindeki açık metinler, kalem/pay zeminleri; ana sayfa inceleme kutusu; gider penceresindeki taksit alanları; alış ödemesi ayırma ve devir düzeltme önerisi.
- **Kartlar, Krediler, Aylık Giderler, Ekstre, Bildirimler, Dışa aktar (masaüstü):** başlık boyutları, koyu kırmızı hata/koyu yeşil ileti, liste ayırıcı, geçiş uyarısının kırmızıya dönmesi.
- **Gerekçe pencereleri:** kart hareket/ödeme/kullanım, kredi arşiv, aylık gider iptali, ekstre iptali, ay kilidi, belge kaldırma — metinler, Vazgeç, boş gerekçe.
- **Önizleme-onay:** kart ödemesi, faiz/masraf, kart ve kredi geçişi; önizlemeden sonra girdi değişince kaydın reddedilmesi.
- **Aylık Giderler (web):** şablon penceresi (Eşit/Özel, kip değişimi); ödenmiş satır iptal penceresini, bekleyen satır ödeme formunu açar.
- **Ekstre (web ve masaüstü):** banka seçenekleri; liste alınamayınca hata; satır dağılımı (Özel tutar, Genel'e geçip dönme, kart ödemesi).
- **Kart ve kredi etiketleri, bildirim ayarları düğmeleri (web).**
- **Güvenlik (masaüstü):** "Uygulama X · sunucu Y" metni.
- **Tarayıcılar:** Firefox ve Safari'de ay seçicinin görünümü ve ekran okuyucu duyurusu; `role="alert"` bölgesinde kapatılmamış eski hataların yeni hatayla yeniden okunması; ekran okuyucuyla (NVDA/VoiceOver) bölge adları ve ek sekme durakları.
- **Geri yükleme tatbikatı** (canlıdan ayrı kopyada): bayrakla açılış, bayraksız açılışta kilit, bayrağı kaldırma.

## 10. Bilinen konular

- **MAUI Release askıda kalma (PR #29'da görüldü, `duzeltme/maui-menu-donmasi` ile giderildi):** Release derlemesinde girişten birkaç saniye sonra UI iş parçacığı bir çekirdeği %100 kullanarak askıda kalıyordu. Kök neden MAUI gerilemesi [dotnet/maui#38813](https://github.com/dotnet/maui/issues/38813) (10.0.100–10.0.110): `Shell.ItemTemplate` görsel durum setter'ı `Background` (fırça) yazınca `ShellFlyoutItemView.UpdateVisualState → Setter → NotifyBackgroundChanges → OnResourcesChanged → ShellFlyoutItemView.OnResourcesChanged → GoToState` döngüsü kurulur; alınan yığın bununla eşleşir. Bizde PR #14'teki MAUI 10.0.20 → 10.0.110 yükseltmesiyle geldi (canlı 2.3.0 10.0.20 ile derlendi; `1efdcb4`'te de vardı, PR #29'dan gelmiyor). Düzeltme dalında `AppShell.xaml` menü öğesi setter'ları `BackgroundColor` yazar (görünüm aynı: `GorunumEsdegerligiTests` her görsel durumda aynı düz rengi doğrular, ekran görüntüsünde seçili öğe `#33453A`); `MauiKayitTutarliligiTests` kabuk menü şablonlarında `Background` yazımını yasaklar. Otomasyonla (yerel test sunucusu, görünmeyen ayrı masaüstü): düzeltme öncesi girişten 1–3 sn sonra pencere yanıtsız, CPU ~%100 (tek çekirdek), ekran dışı ve ekran içi (etkinleşen) pencerede aynı; düzeltme sonrası 62 sn boyunca yanıt veriyor, CPU boşta, menüde Haftalık, Aylık, İşlemler, Kasalar geçişleri çalışıyor. MAUI düzeltmesi ([dotnet/maui#38887](https://github.com/dotnet/maui/pull/38887)) .NET 10 SR12'de (10.0.120) gelir; 30 Eylül 2026'da NuGet'teki son sürüm 10.0.110. 10.0.120 yayımlanınca yükseltme ayrıca değerlendirilebilir; geçici çözüm o sürümde gereksizleşir ama zararsızdır. **Masaüstü paketini dağıtmadan önce** yine normal pencerede giriş ve bütün menülerde (fareyle üzerine gelme zemini dahil; otomasyon fare kullanmadı) birkaç dakika denenmeli; sunucu yayını bundan bağımsızdır.
- Altı bankanın gerçek örnek ekstreleri hâlâ yok (2.3'ten beri); ilk belgelerde okunan satırlar kaynak PDF'le karşılaştırılır.
- 2.0–2.3'ün depo dışı `publish_*.py` betikleri yeni compose şablonunda ilk denetimde durur; yayın README akışıyla elle ya da betik güncellenerek yapılır.
- Readonly nginx şablonu (`kasa.emarglobal.com.readonly.conf`) statik dosyaları nginx'ten sunar; `/m/`'nin orada yayımlanıp yayımlanmadığı doğrulanmadı (canlı tam şablon her şeyi uygulamaya iletir).
- Göç öncesi yedekler rotasyonla silinmez; yer gerekirse uzak kopyası doğrulandıktan sonra elle kaldırılır.
- Her `build --pull` eski imajı etiketsiz bırakır; geri dönüş gereği kalmayana kadar `docker image prune` çalıştırılmaz (geri dönüş imajını da siler).

## 11. Geri dönüş planı

Yeni migration'lar geri alınamaz; **eski imaj yeni şema üzerinde çalıştırılmaz.**

- **Açılış veritabanını değiştirmeden durduysa** (göç öncesi yedek, disk ya da özet hatası; logda "veritabanı değiştirilmedi"): nedeni giderip yeniden başlatın ya da [deploy/README.md](../../deploy/README.md) "Geri dönüş" ile önceki imaja dönün. Yeniden başlatma yarıda kalan belge aktarımını sürdürür.
- **Migration'lar uygulandıysa** ([database-upgrade.md](database-upgrade.md) "Belge deposu geçişi", geri dönüş):
  1. `/opt/kasa/deploy` içinde: `docker compose -f docker-compose.nginx.yml stop`.
  2. Göç öncesi yedeği **yeni** bir veri dizinine açın. Yol mutlak yazılır, çünkü komutlar `/opt/kasa/deploy` içinde çalışır ve göreli `deploy/…` orada yoktur:
     `python3 /opt/kasa/deploy/restore_backup.py <KASA_BACKUP_DIR>/kasa-goc-oncesi-….zip --output <yeni-veri-dizini>/kasa.db`
     Bu eski biçimdir; belgeler `kasa.db` içindedir.
  3. README "Geri dönüş" adımlarını uygulayın: saklanan compose `/opt/kasa/deploy/docker-compose.nginx.yml` üzerine kopyalanır, `docker tag` çalıştırılır, `--build` kullanılmaz.
     - `up`'tan **önce** kopyalanan dosyadaki `/data` bağlamasının kaynağını `<yeni-veri-dizini>` yapın.
     - 2.3'ün compose'u kısa sözdizimiyle sabit bir yol bağlar ve `.env`'deki `KASA_DATA_DIR`'i okumaz. Bu yüzden yalnız `.env`'yi değiştirmek yetmez. Kaynak düzeltilmezse eski imaj, zaten göç etmiş 2.4 veritabanına bağlanır.
     - `up`'tan önce `grep -n ':/data' docker-compose.nginx.yml` yalnız `<yeni-veri-dizini>:/data` göstermelidir.
     - `up`'tan sonra `docker inspect kasa-app --format '{{range .Mounts}}{{.Source}} -> {{.Destination}}{{println}}{{end}}'` çıktısında `/data` kaynağı `<yeni-veri-dizini>` olmalıdır. Değilse hemen durdurun.
  4. 2.4 veri dizinini ve `belgeler/` klasörünü silmeyin. Göç öncesi yedekten sonra girilen kayıtlar geri dönüşte yoktur; bunu kullanıcılara bildirin.
- **Editör kilidi etkisi:**
  - `restore_backup.py` dosyayı geri yükleme işaretiyle (`PRAGMA user_version`) açar. 2.3.0 bu işareti okumaz (v2.3.0 kodunda `user_version` yok): eski imajda editör şifresi yedek anındaki, yani yükseltme anındaki şifredir. 2.3.0 imajının bu araçla açılmış dosyayla açılışı yerelde denenmedi — doğrulanmadı.
  - Aynı dosyayla **sonradan yeniden 2.4'e geçilirse** işaret o açılışta işlenir: bütün oturumlar ve izleyici girişi kapanır; yedek anından sonra güvenlik günlüğüne yazılmış editör şifresi değişikliği varsa editör girişi kilitlenir. Yeniden yükseltmeden önce bölüm 6'daki 1–5. adımlar uygulanır.
  - 2.4 çalışırken yapılmış bir geri yüklemenin koyduğu editör kilidi eski imajda da açılmaz; o durumda eski imaja dönmeden önce 2.4'te sıfırlama yapılır.
- Güvenlik günlüğü (`guvenlik-gunlugu.jsonl`) geri dönüşte de silinmez.

## Doğrulanmayanlar (özet)

- Canlı `kasa.db` boyutu, belge içeriklerinin toplamı ve buna bağlı HTTP kapalı süre ile disk ihtiyacı.
- Canlı verideki rapor farkları (yalnız sentetik altın tohumda sınandı).
- VACUUM geçici kopyasının konteyner içinde yazıldığı disk.
- Sunucudaki nginx site dosyasının güncel içeriği; sunucuda uzak yedeğin kurulu olup olmadığı.
- Yayın sırasında oturumların düşmediği (kod ve migration açıklaması öyle der, canlıda denenmedi).
- Düzeltilmiş masaüstünün kullanıcının normal penceresinde elle denenmesi (otomasyon ayrı masaüstünde koştu; fareyle üzerine gelme zemini yalnız testle sınandı).
- Readonly nginx şablonunda `/m/`.

## Kaynak PR'lar

- #12 Denetim düzeltmeleri + telefon arayüzü (#11) — K1–K4, belge deposu, 13 migration, yayın notları
- #13 Linux CI'da kararsız uyarı testi
- #14 .NET 10.0.12 güvenlik paketleri, SQLite 3.0.5, MAUI 10.0.110
- #15 UI: 6 gerçek hata (₺, 0,00 ₺, hata iletisi, kuruş toplamı, kontrast, ay seçici)
- #16 UI Aşama 1 temizliği (`web/`, `docs/plans`, MAUI şablon artıkları, ikon)
- #17 Korumalar: yasak testi, ESLint, Playwright + axe, MAUI lint, MimariTests
- #18 Erişilebilirlik düzeltmeleri (axe 53 → 0)
- #19 Test araçları büyük sürümleri
- #20 Biçim (Prettier, `dotnet format`)
- #21 Web birleştirme, kuruşla toplamlar
- #22 App.Core birleştirme, gerekçe penceresi oturum düzeltmesi
- #23 Test sağlığı, PDF zaman aşımı bildirimi düzeltmesi
- #24 `Program.cs` uçları taşındı, uç envanteri altın dosyası
- #25 Web ES modülleri, `?v=` kaldırıldı, CSS katmanları
- #26 Sürüm tek kaynaktan (`KasaSurumu`, Dockerfile düzeltmesi), `Kasa.Core.Kodlar`, derlenmiş bağlamalar
- #27 Editör şifresi zorla sıfırlama, geri yükleme prosedürü, banka listesi ucu, migration `EditorSifirlamaIzi`
- #28 Web işlem düğmeleri ve alış ekranları
- #29 MAUI görsel bileşenler; bilinen askıda kalma notu
- (açık) `duzeltme/maui-menu-donmasi`: MAUI menü askıda kalması geçici çözümü (dotnet/maui#38813)
- #30 (açık) Türkçe adlar; ardından "Kodlar kalan aileleri" — davranış etkisi yok
