# ERP12 cari hareketlerinin aktarımı

Tarih: 2026-11-25 · Dal: `ozellik/erp12` · Kaynak: "KASA DÜZENLEMELER" madde 4

İstek: "Cari kaydı tutulacak ama kayıtlardan asıl ERP12'de; düzenli olarak işlemler ERP12'den export edilip kasa uygulamasına import
edilecek." Karar (25.11.2026): **kasayı ekstre etkiler**. ERP12 kaydı kasadaki kayıtla eşleşip cari bilgisini verir; ekstrede karşılığı
olmayan (nakit) hareket kullanıcıya sorulur.

Bu belge `docs/roadmap.md`'deki "cari ERP12'de kalır" kararını şöyle değiştirir: aslı ERP12'de kalır, kasa uygulaması dışa aktarılan
hareketlerin **aynasını** tutar. Cari kartı elle açılmaz, bakiye buradan düzeltilmez.

## Yükleme

Ekstre ekranında belge türü **"ERP12 cari hareketleri (Excel)"**. Banka ve hesap seçilmez. Dosya Excel okuyucusuyla okunur
([excel](2026-11-25-excel-yukleme.md)); tek satır hatalıysa dosya bütünüyle reddedilir.

Aranan sütunlar: **Tarih**, **Cari** (Cari adı, Ünvan, Firma, Müşteri, Tedarikçi, Hesap adı…) ve **Borç + Alacak** (ya da Tutar). Varsa Cari kodu,
İşlem türü / Evrak tipi / Ödeme şekli, Belge / Evrak / Fiş no ve Açıklama da okunur. Başlık tanınmazsa görülen başlıklar iletide yazar.
Yalnız TL: para birimi hücresi, tutar başlığı ("Borç (USD)") ya da üst bilgi döviz yazıyorsa satır hatalıdır.
ERP12'nin gerçek dışa aktarım dosyası henüz elimizde değil; sütun adları geldiğinde eşleme genişletilir.

### Kasa hareketi mi

| İşlem türü ya da açıklama | Sonuç |
|---|---|
| Tahsilat, ödeme, tediye, nakit, kasa, havale, EFT, FAST, banka, POS, kredi kartı, çek, senet | Kasa hareketi: ekranda **bekler** |
| Fatura, irsaliye, devir, açılış, vade farkı, mahsup, virman, dekont… | Yalnız cari hesapta görünür |
| Tür yazmıyor | Kasa hareketi sayılır, kullanıcı karar verir |

Yön: cari **Alacak** = bize ödeme (Giriş), cari **Borç** = bizim ödememiz (Çıkış). İşlem türündeki "tahsilat" / "ödeme" bununla çelişirse satır
belirsiz kalır; satırların çoğunda çelişirse dosya reddedilir. "Nakit" ya da "kasa" geçen (ve banka geçmeyen) hareket **nakittir**.

### Aynı hareket ikinci kez aktarılmaz

ERP12'den her seferinde örtüşen tarih aralığı alınabilir. Daha önce aktarılmış hareket (tarih, cari, tutar, tür, belge no ve açıklaması aynı;
aynı gün birden çoksa adet adet) yeniden alınmaz, "Atlanan satırlar"da sayılır. Aynı cari ve belge numarasının tutarı ya da tarihi değiştiyse satır
alınır ve uyarı yazar. Önceki aktarımda olup aynı tarih aralığındaki yeni dosyada olmayan hareketler belge uyarısında sayılır.

## Eşleştirme

Bekleyen her satır için kasadaki karşılığı aranır: aynı tutar ve yön, en çok 3 gün fark, başka ERP12 satırına bağlanmamış.

| Yön | Adaylar |
|---|---|
| Çıkış | gider (banka ekstresi, elle, alış ödemesi, aylık gider), kart harcaması, verilen çek, alınan çekin cirosu |
| Giriş | banka ekstresi geliri, alınan çek |

Nakit satır banka ekstresi kaydıyla (ekstreden yazılmış ya da ekstreyle eşleştirilmiş giderle) eşleştirilmez. Alınan çek müşterinin girişiyle, aynı çekin cirosu
tedarikçinin çıkışıyla ayrı ayrı eşleşir.

| Durum | Satır |
|---|---|
| Tek aday ve adı cari adıyla uyuşuyor (ya da birden çok adaydan yalnız biri uyuşuyor) | "Mevcut kayıtla eşleştir" seçili, **hazır** |
| Aday var ama kesin değil | Aday listesi dolu, kullanıcı seçer |
| Aday yok, nakit | "Nakit: kasaya yazılsın mı?" Tür dolu, kanal dağılımını kullanıcı seçer (hafıza biliyorsa dolu gelir) |
| Aday yok, nakit değil | Bekler: "ekstresi yüklenince eşleşir". Yine de kaydedilirse önizleme çift sayım uyarısı verir |

Eşleşme yalnız bağdır: kasa, kart borcu ve raporlar değişmez. Bir kayıt yalnız bir ERP12 satırına bağlanır. Eşleşen kayıt sonradan iptal
edilirse satırda "eşleştiği kayıt silinmiş" yazar; eşleşme iptal edilip yeniden yapılabilir.

Nakit satırın kaydı banka gelir/gideriyle aynı yoldan yazılır (genel kasa ya da kanal payı, ay kilidi, önizleme, iptal).

## Cari bilgisi

- **Cariler** ekranı (menü "Kayıtlar"; İzleyici ve Editör): cari adı, borç, alacak, bakiye (yalnız aktarılan hareketlere göre), son hareket ve bekleyen
  sayısı; arama. Cari açılınca hareketleri ve her birinin durumu (kasa dışı, bekliyor, eşleşti, kaydedildi, yok sayıldı).
- İşlemler listesinde ve ekstrenin Kapanan satırlarında eşleşen carinin adı ("ERP12: Ahmet Yılmaz").
- Yanlış yüklenen ERP12 dosyası, hiçbir satırı kaydedilmemiş ya da eşleştirilmemişse silinebilir.

## Uçlar

- `POST /api/ekstre-aktar/yukle` (`kaynak=Erp`), önizleme/kaydet/iptal/yok say aynı uçlar. `eslesme-adaylari` isteğine `satirNo` eklenir (ERP12'de zorunlu).
- `GET /api/erp/cariler?ara=`, `GET /api/erp/cariler/hareketler?anahtar=` (Finans: İzleyici, Editör).
- `DELETE /api/ekstre-aktar/{id}/erp` (Editör): işlenmemiş ERP12 belgesini siler.
- Veritabanı: `ErpHareketler` tablosu (migration `20261125000300_ErpHareketleri`). Mevcut veriye dokunulmaz.

## Muhasebe örnekleri

1. ERP12: "03.11 Ahmet Yılmaz, Havale ödeme, 5.000". Akbank ekstresinde 03.11 "FAST AHMET YILMAZ −5.000" gider olarak kayıtlı → satır hazır gelir,
   onaylanınca eşleşir. Kasa değişmez; İşlemler'de gider satırında "ERP12: Ahmet Yılmaz" görünür.
2. ERP12: "04.11 Kırtasiye, Nakit ödeme, 350". Ekstrede yok → "kasaya yazılsın mı" diye bekler. Kullanıcı kanalı seçip kaydederse kasadan 350 düşer.
3. ERP12: "05.11 Satış faturası 12.000" → kasa hareketi değildir; yalnız Cariler'de borç olarak görünür.

## Kapsam dışı

ERP12'ye veri göndermek, cari kartı açmak/düzeltmek, vade takibi, döviz. Elle yazılan haftalık gelir (Gelen) aday değildir.

## Testler

Okuma (sütunlar, tür ayrımı, yön, çelişki), tekrar aktarım, değişen belge, eşleştirme adayları ve kesinlik, nakit, çift bağ, kasa etkisi, iptal, hafıza,
cari listesi ve durumları, silme, yetki; istemcide belge türü, kendiliğinden eşleşme, aday seçimi, hazırlar, Cariler ekranı, menü ve roller.
