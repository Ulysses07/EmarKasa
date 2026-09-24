# Kasa Defteri — Tasarım Dokümanı

**Tarih:** 2026-07-13
**Durum:** Uygulandı. Aşağıdaki "Güncel durum" bölümü bu belgenin geri kalanından önce gelir (2026-09).

## Güncel durum (2026-09)

Bu belge ilk tasarımdır. Uygulama birkaç noktada bundan ayrıldı:

- **Arayüz:** React SPA emekliye ayrıldı; `web/` klasörü kaldırıldı. İstemci artık native
  .NET MAUI uygulamasıdır (`Kasa.App`: Windows `.exe`, Android, iOS). Sunucu yalnız API'dir.
  Bkz. `docs/specs/2026-07-14-emar-kasa-native-design.md`.
- **Kredi kartı (haftalık kasa):**
  - Karta bağlı K.K harcaması (`KrediKartiId` dolu) kasadan harcama tarihinde düşmez.
    Kartın **gerçek ödeme tarihinde** düşer: `KartOdemeler` kayıtları ödemenin
    dönemine gider olarak yazılır.
  - Karta bağlı olmayan (eski) K.K harcaması ertelenir. **Bir sonraki ayın son döneminde**
    kasadan toplu çıkar; son dönem, ayın son gününü içeren dönemdir.
  - Bkz. `docs/specs/2026-07-15-kart-borc-hareketleri-design.md`.
- **Kredi kartı (aylık kâr raporu):** Her K.K harcaması kanalına **bir sonraki ayda** yazılır.
  Kartlı ya da kartsız ayrımı yapılmaz.
- **Ortak payı:** Ayın K.K dışı Ortak giderleri + **önceki ayın** Ortak K.K'sı. Toplam aktif
  kanallara kuruş bazında bölünür. Artan kuruşlar ilk aktif kanallara verilir.
- **Şema:** Yeni tablo, sütun ve index'ler açılışta `SemaGuncelleyici` ile otomatik eklenir.
  Elle SQL yazılmaz, DB yeniden oluşturulmaz. Şema değişmeden önce otomatik yedek alınır.
- **Uygulanmadı:**
  - hafta başlangıç günü ayarı (hafta sabit Pazartesi başlar)
  - cari birleştirme
  - Excel/CSV dışa aktarma
  - yedeklerin R2'ye kopyalanması
  - aylık AY SONUCU trend grafiği


## 1. Amaç

Excel'de tutulan haftalık nakit akış defterini web uygulamasına taşımak.
İşletmenin 3 gelir kanalı (MEZAT / PERAKENDE / TOPTAN) için gelen/giden takibi,
haftalar arası devir, aylık kârlılık ve toplam kasa pozisyonunu otomatik hesaplar.

Kişisel/işletme-içi kullanım. 5-6 ortak izler; muhasebeyi tutan tek ortak veri girer.

**Kapsam DIŞI (bilinçli):** döviz/kur, cari bakiye/borç takibi (bunlar ERP12'de
tutuluyor), denetim logu, yıllık karşılaştırma, bildirimler. Bu bir **kasa
defteri**, muhasebe/cari sistemi değil.

## 2. Temel Kavramlar & Kararlar

- **Kanallar:** MEZAT / PERAKENDE / TOPTAN. Ayarlardan eklenip çıkarılabilir.
- **Para birimi:** yalnız TL (kuruş dahil). Döviz yok.
- **Gelen:** haftalık, kanal başına tek rakam (dönem başına girilir).
- **Giden:** kalem kalem işlem (tarih, cari, tutar, kanal, tip).
- **Tek kaynak:** tüm haftalık/aylık özetler işlemlerden hesaplanır; elle özet
  rakamı girilmez.
- **Geçmişe dönük hesap:** bir işlem/açılış değeri düzeltilince sonraki tüm
  devirler otomatik yeniden hesaplanır (kapanmış dönem kilidi yok).
- **Başlangıç:** temiz başlangıç — bir başlangıç tarihi + açılış bakiyeleri
  girilir; geçmiş işlemler girilmez.

### 2.1 Dönem (devir segmenti)

Bir **dönem**, devir alınan en küçük zaman birimidir. Sınırı **hafta** veya **ay
sonu**ndan hangisi önce gelirse orada kapanır:

- Haftalar **Pazartesi** başlar (Mon–Sun 7 günlük bloklar).
- Bir hafta ay sonunu geçerse ikiye bölünür (her parça ayrı dönem), böylece her
  dönem tek bir takvim ayına aittir ve aylık toplamlar temiz çıkar.
- Devir zinciri tüm dönemlerden sırayla akar.

Örnek (Haziran 2026, 15 Haziran Pazartesi):
`15–21 Haz` → `22–28 Haz` → `29–30 Haz` (ay kesildi) → `1–5 Tem` → `6–12 Tem` …

Dönemler ayarlardaki başlangıç tarihinden itibaren **otomatik üretilir** (elle
açılmaz). İlk dönem hafta ortasında başlıyorsa kısmi olabilir.

### 2.2 İki devir zinciri

1. **Kanal bazlı devir** — her kanalın kümülatif bakiyesi (kanal kârlılığı
   göstergesi). Yalnız o kanalın geleni ve `Cari` tipli gideni sayılır; sabit
   gider / K.K / ortak giderleri saymaz.
2. **Toplam kasa devri** — gerçek nakit pozisyonu. Tüm gelen − tüm giden (her
   kanal + ortak, her tip).

İki zincir bağımsızdır; birbirine eşitlenmez (her birinin kendi açılış değeri
vardır).

## 3. Veri Modeli

**Kanal:** `Id, Ad, Aktif, Sıra, AçılışDevri`

**Cari:** `Id, Ad, Aktif` — yalnız isim (otomatik tamamlama). Bakiye tutulmaz.

**Giden (İşlem):** `Id, Tarih, CariId/Ad, TutarTL, Kanal (MEZAT|PERAKENDE|TOPTAN|Ortak), Tip (Cari|SabitGider|KrediKarti), Not`
- `Cari` tipi → haftalık kanal devrine girer.
- `SabitGider` / `KrediKarti` → aylık kârlılığa girer, haftalık kanal devrine girmez.
  *[Güncel] Haftalık **kasa** devrine girerler. K.K için kural "Güncel durum" bölümündedir.*
- `Ortak` kanal → aylık raporda aktif kanal sayısına bölünür; haftalık yalnız
  kasayı düşürür.
- K.K için `Not` alanında harcama ayı etiketi tutulabilir (ör. "Mayıs"); sadece
  görsel, hesaba etkisi yok — gider ödendiği aya işler.
- *[Güncel] İşlem ayrıca `KrediKartiId` (nullable) taşır. Kart ödemeleri ayrı bir
  `KartOdemeler` tablosundadır.*

**Gelen:** `Id, DönemId, Kanal, TutarTL` — haftalık, kanal başına. Ay sonunu
geçen haftada iki dönem satırı girilir.

**Dönem:** `StartTarih, EndTarih, Ay, Sıra` — otomatik üretilir. Devirler
saklanmaz, hesaplanır.

**Ayarlar:** hafta başlangıç günü (Pazartesi) *(Uygulanmadı: ayar yok, hafta sabit
Pazartesi)*, takip başlangıç tarihi, kasa açılış devri, izleyici şifresi.

## 4. Hesap Motoru

Girdiler: işlemler + gelen + açılış bakiyeleri + dönem takvimi. Dönemler tarih
sırasına dizilir, devirler zincirlenir.

**Haftalık — kanal başına (dönem P, kanal C):**
```
giden_cari[C]  = Σ (P içinde, kanal=C, tip=Cari)
haftaSonucu[C] = gelen[C] − giden_cari[C]
kanalDevir[C]  = öncekiDönem.kanalDevir[C] + haftaSonucu[C]   (ilk dönem: kanal açılış devri)
```

**Haftalık — toplam kasa:**
```
toplamGelen = Σ_C gelen[C]
toplamGiden = Σ (P içindeki K.K dışı işlemler — her kanal + ortak)
            + (P, ayın son dönemiyse) Σ (önceki ay, tip=KrediKarti, kartsız)
            + Σ (P içindeki kart ödemeleri, KartOdemeler)
            # karta bağlı K.K harcaması kasaya doğrudan girmez; ödemesiyle girer
kasaSonucu  = toplamGelen − toplamGiden
kasaDevir   = öncekiDönem.kasaDevir + kasaSonucu              (ilk dönem: kasa açılış devri)
```

**Aylık — kanal başına AY SONUCU (ay M, kanal C):**
```
aylikGelen[C]      = Σ gelen[C] (M içindeki dönemler)
aylikCariGiden[C]  = Σ (M, kanal=C, tip=Cari)
aylikSabitGider[C] = Σ (M, kanal=C, tip=SabitGider)
aylikKK[C]         = Σ (M−1, kanal=C, tip=KrediKarti)          # önceki ayın K.K'sı
ortakPay[C]        = [ Σ (M, kanal=Ortak, tip≠KrediKarti)
                     + Σ (M−1, kanal=Ortak, tip=KrediKarti) ] ÷ (aktif kanal sayısı)
                     # kuruş bazında; artan kuruşlar ilk aktif kanallara
aySonucu[C] = aylikGelen[C] − aylikCariGiden[C] − aylikSabitGider[C] − aylikKK[C] − ortakPay[C]
```

### 4.1 Excel doğrulaması (Haziran 2026)

- MEZAT haftalık: `289.425 − 1.308.800 = −1.019.375` → `4.991.052 + (−1.019.375) = 3.971.677` ✓
- Kasa: `767.431 − 3.345.495 = −2.578.064` → `2.907.053,21 + (−2.578.064) = 328.989,21` ✓

Bu rakamlar hesap motorunun sabit birim testleri olur.

## 5. Ekranlar

1. **İşlem + Defter (editör, PC):** giriş formu (tarih, cari-otomatik tamamlama,
   tutar, kanal, tip, not) + filtrelenebilir işlem tablosu (düzenle/sil). Gelen
   girişi: dönem seç → kanal başına tutar gir.
2. **Haftalık özet:** dönem dönem → kanal gelen/giden/sonuç/kanalDevir + toplam
   gelen/giden/kasaSonucu/kasaDevir (Excel'deki sarı blok).
3. **Aylık rapor:** ay seç → kanal başına aylıkGelen/cariGiden/sabitGider/K.K/
   ortakPay/AY SONUCU + toplam; altında kanal AY SONUCU trend grafiği
   *(trend grafiği: Uygulanmadı)*.
4. **Ortak paneli (mobil, salt-görüntüleme):** güncel kasa (büyük) → kanal
   kümülatif bakiyeleri → bu hafta / bu ay sonucu → trend. Öncelik sırası:
   kasa → kanal kârlılığı → trend.
5. **Cari yönetimi:** ekle/düzenle/birleştir/pasifleştir *(birleştir: Uygulanmadı)*.
6. **Ayarlar:** kanallar (ekle/çıkar/adlandır/açılış devri), kasa açılış devri,
   hafta başlangıcı *(Uygulanmadı)*, takip başlangıç tarihi, izleyici şifresi,
   Excel/CSV dışa aktarma *(Uygulanmadı)*.

Her yerde arama/filtre (cari / kanal / tarih aralığı).

## 6. Giriş / Roller

- **Editör:** tek hesap (kullanıcı+şifre), düzenleme yetkisi.
- **İzleyici:** tek ortak şifre, salt-görüntüleme.
- **Mobilde düzenleme yok:** dar ekranda giriş UI'ı gizli; herkes yalnız görür.
  Düzenleme yalnız PC/geniş ekran.
- Basit JWT/cookie oturum.

## 7. Mimari & Dağıtım

- **Backend:** ASP.NET Core (REST API + hesap motoru).
- **DB:** SQLite (tek dosya, kalıcı volume).
- ~~**Frontend:** React SPA, mobil-uyumlu.~~ *[Güncel] SPA emekli. Native MAUI istemci
  (`Kasa.App`) kullanılır.*
- **Barındırma:** VPS'te Docker container, Caddy subdomain (ör.
  `kasa.emarglobal.com`), otomatik HTTPS.
- **Yedek:** SQLite dosyasının günlük otomatik kopyası (opsiyonel R2'ye).
  *[Güncel] Günlük `VACUUM INTO` yedeği alınır, son 30 gün tutulur. R2 kopyası:
  Uygulanmadı. Sunucu dışı kopya için `deploy/README.md` → "Yedek" bölümüne bakın.*

## 8. Test

- Hesap motoru birim testleri: devir zincirleri, ay-sınırı bölme, ortak/N,
  açılış bakiyeleri — §4.1 Haziran rakamları sabit doğrulama.
- API entegrasyon testleri.

## 9. Açık İş Kalemleri (implementasyon aşamasında)

- Subdomain adının kesinleşmesi.
- Tasarım/UI dışarıda (Claude/design) yapılacak; bu spec içeriği ona prompt
  olarak verilecek.
