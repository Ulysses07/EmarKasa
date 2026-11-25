# Çekler: verilen öncelikli ekran ve toplu giriş

Tarih: 2026-11-25 · Dal: `ozellik/cekler-toplu` (taban `release/2.x`) · Kaynak: "KASA DÜZENLEMELER" madde 5
Temel tasarım: [2026-10-01-cekler.md](2026-10-01-cekler.md). Kasa etkisi, durumlar ve geçişler değişmez.

Ürün sahibinin isteği: "Toplu olarak çek girme. Bir de genelde çek almıyoruz veriyoruz, çek kısmında önemli olan verilenler."
25.11.2026'da kararlar uygulayıcıya bırakıldı ("tüm kararları sana bırakıyorum").

## Verilen öncelikli ekran

- Çekler sayfası **Verilen** ile açılır. Yön çiplerinin sırası Verilen, Alınan. Yeni çek formu ve toplu giriş seçili yönle açılır.
- Verilen çekte "portföyde" sözü yerine **"ödenecek"** yazılır (durum çipi "Ödenecek", satırda "ödenecek"). Alınanda değişmez.
  Saklanan kod değerleri ve sunucu süzgeçleri aynıdır.
- Üst şeridin sırası: 30 gün içinde ödenecek · vadesi geçmiş ödenmemiş · **ödenecek toplam** (yeni) · portföydeki alınan · 30 gün
  içinde tahsil edilecek · vadesi geçmiş tahsil edilmemiş.
- Panel kutusu aynı sırayla: önce verilen üç satır, sonra alınan iki satır.
- `GET /api/takip/cekler/ozet` yanıtına `portfoydekiVerilen` eklenir: ödenecek ve kısmen ödenmiş, teminatsız verilen çeklerin kalanı.
  Alan kaydın sonundadır. Eski uygulama yeni alanı yok sayar; yeni uygulama eski sunucuda bu kutuyu boş bırakır.

## Toplu giriş

### Sunucu

`POST /api/takip/cekler/toplu` (Editör): `{ istekId, satirlar: [{ tur, yon, no, banka, kisi, tutar, vadeTarihi, kanal, teminat, konum, not }] }`

- En az 1, en çok 200 satır. Her satır tek çek girişiyle aynı kurallardan geçer (`CekAlanlari`).
- Ya hep ya hiç: bir satır hatalıysa hiçbiri kaydedilmez. Bütün hatalar bir kerede `Satır N: …` satırları olarak döner (400).
- `istekId` tekrar koruması tek çekle aynıdır: aynı istek ikinci kez çek oluşturmaz, aynı çekleri döndürür.
- Yanıt: `{ cekler: [CekDto] }` (giriş sırasıyla).

### Masaüstü ve telefon ekranı

"Toplu çek gir" düğmesi formu açar:

1. **Ortak bilgiler:** tür, yön, banka, ödeneceği kasa (verilende), kişi, not. Bütün satırlara yazılır; satırda kişi doluysa o geçer.
2. **Satırlar:** numara, kişi, tutar, vade. Üç yoldan eklenir:
   - *Boş satır.*
   - *Seri:* adet, ilk numara, her birinin tutarı, ilk vade, kaç ayda bir. Örnek: 6 adet, ilk numara `0012345`, 10.000 ₺, ilk vade
     31.01.2027, 1 ay → `0012345`…`0012350`, vadeler 31.01, 28.02, 31.03, 30.04, 31.05, 30.06. Numaranın sonundaki rakamlar artar,
     baştaki sıfırlar korunur.
   - *Excel'den yapıştır:* sütunlar Numara, Kişi, Tutar, Vade. İlk satır başlıksa sütunlar başlığa göre bulunur. Kişi sütunu yoksa
     (Numara, Tutar, Vade) ortak kişi kullanılır.
3. Altta adet ve toplam yazar. Hatalı satırın altında nedeni görünür; düzeltilmeden hiçbiri kaydedilmez.

Yapıştırılan metin kuralları (tahmin yok):

- Tutar: `12500`, `12500,50`, `12.500`, `12.500,00`, `1.250.000` kabul edilir (nokta binlik ayırıcıdır; kuruş en çok iki hanedir).
  `12,500` reddedilir (12 bin 500 mü, 12 buçuk mu belli değil). Okunan tutarlar kaydetmeden önce satırlarda ve toplamda görünür.
- Tarih: `31.01.2027`, `31/01/2027`, `31-01-2027`, `2027-01-31`.
- Bir satır bile okunamazsa hiçbiri eklenmez; okunamayanlar satır numarasıyla listelenir, metin düzeltilmek üzere kutuda kalır.
- Tanınmayan başlık (ör. Banka) reddedilir: sessizce atlanmaz.

Aynı çek: kaydetmeden önce aynı yön, banka ve numaralı kayıt (veritabanında ya da listede) varsa uyarı çıkar; "Yine de kaydet" ile
kaydedilir (tek çekteki kural).

## Kapsam dışı

Çek fotoğrafından okuma, toplu ödeme, banka hareketiyle çek eşleştirme (işlem hafızası işinde değerlendirilir), web ekranı.

## Testler

Sunucu: ekleme, ya hep ya hiç, satır hataları, sınır, tekrar koruması, yetki, özet alanı. İstemci: varsayılan yön, sıra, seri (ay sonu,
baştaki sıfır), yapıştırma (başlıklı/başlıksız, belirsiz tutar, hatalı tarih, tanınmayan başlık), ortak alanlar, aynı çek uyarısı,
sunucu satır hatası, kaydedilmemiş değişiklik.
