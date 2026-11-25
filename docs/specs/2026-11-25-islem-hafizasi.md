# İşlem hafızası (ekstre ve hesap hareketleri)

Tarih: 2026-11-25 · Dal: `ozellik/islem-hafizasi` · Kaynak: "KASA DÜZENLEMELER" madde 3

İstek: "Bir işlem daha önce kaydedildi ise onu tekrar tekrar elle düzeltip girmemem gerek. Kendiliğinden ilk girişten sonra hafızaya
göre otomatik işlemeli. Bu, işlem hareketleri ve kredi kartı ekstreleri için gerekli."
Karar (25.11.2026): satırlar hafızadan **kendiliğinden doldurulur**, kayıt **toplu onayla** olur. Hiçbir satır kendiliğinden kaydedilmez.

## Nasıl çalışır

1. Kullanıcı bir ekstre satırını kaydeder (ya da yok sayar). Bu bir **karardır**.
2. Sonraki belgede açıklamasının değişmeyen kısmı ve yönü aynı olan satır bu kararla kendiliğinden doldurulur: işlem türü, kanal
   dağılımı, (karta ödemede) kart ve düzeltilmiş açıklama. Satırda "Hafızadan: 3 kez aynı işlendi, son 12.10.2026" yazar.
3. "Hazır öneriler" bölümü bu satırları sayar ve toplamını gösterir. Tek düğme hepsini işaretler ve etkisini gösterir; kullanıcı
   önizlemeyi onaylayıp kaydeder. Benzer kayıt uyarıları aynen çalışır.

Ayrı bir öğrenme ekranı ya da tablosu yoktur: hafıza, iptal edilmemiş ekstre kayıtlarından ve yok sayılan satırlardan o anda hesaplanır.
Bir kaydı iptal etmek ya da yok saymayı geri almak o kararı hafızadan da çıkarır.

## Anahtar

Satırın PDF'deki açıklaması sadeleştirilir (`EkstreKuralServisi.Normalize`: büyük harf, Türkçe harf ve noktalama farkı yok). Sonra
değişen parçalar atılır:

- yalnız rakam olan ya da dört ve daha çok rakam içeren sözcükler (tarih, saat, referans, IBAN, kart no),
- ay adları (`KASIM`), `TL` ve `TRY`.

`A101`, `N11`, `3M` gibi az rakamlı adlar kalır. Örnek: `FAST 20261125 AHMET YILMAZ KASIM KİRA 1234567` → `FAST AHMET YILMAZ KIRA`.

En az üç harfli bir sözcük kalmıyorsa anahtar yoktur. Kalan sözcüklerin hepsi genelse (`GELEN HAVALE`, `ATM PARA YATIRMA` gibi: kimin
olduğu belli değil) hafıza ancak son **iki** karar aynıysa kullanılır. Eşleşme tam eşitliktir; benzetme yapılmaz.

## Hangi karar

Aynı belge türü (banka hareketi / kart ekstresi), anahtar ve yöndeki kararlardan en yakın kapsam seçilir: aynı hesap ya da kart → aynı
banka → bütün bankalar. Kapsamın en yeni kararı (hareket tarihi, belge, satır) önerilir:

- Son iki karar farklıysa öneri doldurulmaz, "daha önce farklı işlendi" yazar (örnek: geçen ay Gider, bu ay Gelir kaydedilmiş).
- Açıklama yalnız düzeltilmiş açıklama rakam ve ay adı içermiyorsa ve son iki kararda aynıysa taşınır (`Yurtiçi Kargo` taşınır,
  `Kira Kasım` taşınmaz).
- Kişisel kural eşleşiyorsa kural önceliklidir; hafıza o satıra karışmaz.

## Hazır / kontrol

| Durum | Anlamı |
|---|---|
| `Hafiza` | Satır dolduruldu, toplu işaretlemeye girer. Yok sayma kararıysa "Yok say" önerisidir. |
| `HafizaKontrol` | Doldurulabilenler dolduruldu ama toplu işaretlemeye **girmez**; nedeni yazar. |

Kontrol nedenleri: tarih/tutar/para birimi/yön kesin değil ya da PDF uyarısı var; yön kararla çelişiyor; hesaplar arası transfer; kanal pasif ya
da silinmiş; kanal tutarları satıra özeldi; kart takipte değil; iade kaynağı ya da eşleşecek kayıt seçilmeli; son iki karar farklı.

## Unutma

- Satırdaki "Bu hafızayı unut" ya da hafıza listesindeki "Unut": o anahtar ve yönün **bugüne kadarki** kararları hafızadan çıkar
  (`EkstreHafizaUnutulanlar`: son kayıt ve son yok sayma kimliği). Kayıtların kendisi ve raporlar değişmez. Sonraki kayıtlar yeniden öğrenilir.
- Liste: `GET /api/ekstre-aktar/hafiza` (en yeni 500 anahtar: son karar, kaç kez, son tarih, çelişki).

## Uçlar

- `GET /api/ekstre-aktar/{id}/oneriler`: yanıta `hafiza` alanı eklenir (`anahtar, adet, sonTarih, krediKartiId, aciklama`). Eski
  istemciler `Hafiza`/`HafizaKontrol` durumunu yalnız metin olarak gösterir, uygulamaz.
- `GET /api/ekstre-aktar/hafiza`, `POST /api/ekstre-aktar/hafiza/unut` (Editör; istekId tekrar koruması).
- Veritabanı: boş `EkstreHafizaUnutulanlar` tablosu (migration `20261125000200_EkstreHafizasi`). Mevcut veriye dokunulmaz.

## Ekran (Windows ve iPhone)

- Satır özetinin altında hafıza notu. Satır düzenleyicisinde not ve "Bu hafızayı unut".
- "Hazır öneriler" kutusu (Bekleyen): `12 satır · Giriş + 1.500,00 ₺ (3 satır) · Çıkış − 8.250,00 ₺ (9 satır)`; "Hazırları işaretle ve etkisini
  göster". Kişisel kural önerileri de buraya girer. "Daha önce yok sayılanları yok say (4)" ayrı düğmedir ve onay ister.
- Elle değiştirilmiş satır hafızadan ezilmez.
- "Kişisel sınıflandırma kuralları" kartında hafıza listesi (aç/kapat) ve "Unut".

## Değişmeyenler

Kasa, kart borcu, raporlar, ay kilidi, benzer kayıt ve önizleme onayı. Kayıt yalnız bugünkü kaydetme ucundan olur.

## Testler

Anahtar (tarih, referans, IBAN, ay adı, A101, genel sözcük), kapsam sırası, son iki karar kuralı, iptal ve yok sayma, unutma ve yeniden
öğrenme, kontrol nedenleri, kural önceliği, kart ekstresi, uç yetkisi ve tekrar koruması; istemcide kendiliğinden doldurma, elle değişikliğin
korunması, hazırları işaretleme, yok say önerisi, liste ve unutma.
