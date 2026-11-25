# PDF ekranı: etki gösterimi, Bekleyen / Kapanan, giriş–çıkış (tasarım)

Onay: 25.11.2026. Kaynak: "KASA DÜZENLEMELER" notlarının 2. ve 6. maddesi. Kod tabanı f88880d.

Raporlar, kasa formülü, kart borcu ve "seçim + önizleme + onay" zinciri **değişmez**. Hiçbir satır kendiliğinden kaydedilmez.

## 1. Etkisini göster

Sorun: sonuç düğmenin altında, hata sayfanın en başında çıkıyor; ekran ikisine de kaymıyor. Doğrulama ilk hatalı satırda duruyor,
sunucu iletisi satır numarası taşımıyor, bazı durumlarda hiçbir şey yazılmadan dönülüyor.

- Sonuç ve hatalar düğmenin hemen altındadır; işlem bitince ekran oraya kayar.
- Eksikler satır satır **hepsi birden** listelenir ("Satır 4: …"). Satıra dokunmak Bekleyen sekmesinde o satırı açar.
- Hiç satır işaretli değilken bir satır "İncele" ile açıksa ileti bunu söyler: açmak seçmek değildir, kutu işaretlenmelidir.
- Veri hazır değilse ya da beklerken satır değiştiyse sessizce dönülmez, nedeni yazılır.
- Sunucu önizlemesi doğrulama hatalarını satır numarasıyla ve toplu döner: her satır `Satır N: ileti`, satırlar yeni satırla ayrılır
  (gövde biçimi aynı: `{ "hata": "…" }`). Hatalı satır varken hiçbir satır uygulanmaz.
- Önizlemede işlem türü kod adıyla değil Türkçe adıyla yazılır.
- **Seçilenlere toplu uygula:** işlem türü ("Satırdaki tür kalsın" ya da belgenin gelir/gider/kart harcaması) ve kanal dağılımı
  (genel kasa — yalnız banka — ya da seçilen kanallara eşit) işaretli satırlara yazılır. Karta ödeme, iade ve eşleştirme satırları
  atlanır ve sayısı söylenir. Kaydetmez.

## 2. Bekleyen / Kapanan

- Satır **kapalıdır**: iptal edilmemiş kaydı (eşleştirme dahil) varsa ya da yok sayıldıysa. Diğerleri bekler.
- Belgede "Bekleyen (n)" ve "Kapanan (n)" sekmeleri; geçmiş listesinde "n bekliyor · n kapandı".
- Kapanan satırda sonucu (kayıt, eşleşme, yok sayıldı) ve düğmesi görünür: kaydı gerekçeyle iptal et ya da yok saymayı geri al.
  İptal edilen kayıtlar aynı sekmenin altında ayrı listede kalır.
- **Yok say:** satır mali kayıt üretmeden kapanır; tek tek ya da işaretlilere toplu. Geri alınabilir. Yok sayılan satır önizlenemez ve
  kaydedilemez. Kasaya, kart borcuna ve raporlara etkisi yoktur; ay kilidi aranmaz; denetim izine yazılır.
- Kaydetme, iptal, yok sayma ve geri almadan sonra aynı belgenin **diğer** satırlarında doldurulmuş taslaklar ve işaretler korunur.

## 3. Giriş / çıkış

- Her satırda **Giriş +**, **Çıkış −** ya da **Belirsiz** metni (renkle birlikte) ve okunduysa dayanağı ("bakiyeden", "açıklamadan").
  Kullanıcı tür seçince yön türden gelir; PDF'de okunan yönle çelişirse "PDF: …" eklenir.
- Belgenin üstünde giriş, çıkış toplamı ve belirsiz sayısı; düğmenin üstünde işaretlilerin toplamları.
- **Bakiyeden çözme (yalnız banka):** satırda tutar ve bakiye varsa, komşu satırın bakiyesiyle fark satırdaki tutarlardan tam
  birine kuruşu kuruşuna eşitse tutar ve yön farktan alınır. Belge eskiden yeniye ya da yeniden eskiye sıralı olabilir; sıra
  tutan bağların sayısından (eşitse tarihlerden) bulunur. Başlıkta bakiye kolonu yoksa en az iki bağ ve bağların yarısı tutmalıdır.
  "Devir / önceki / açılış bakiyesi" satırı ilk satırın komşusudur. Tutarı satırda yazmayan hareketin tutarı farktan **uydurulmaz**.
- Borç/Alacak kolonu, B/A eki ya da işaretten okunan yön bakiyeyle **çelişirse** yön "Belirsiz" olur ve uyarı yazılır.
- Bakiye zinciri kurulmuşken bir satırda kopuyorsa belge uyarısı satır numaralarını söyler (arada okunamayan hareket olabilir).
- **Anahtar kelime (yalnız banka, başka kanıt yoksa):** giriş — gelen EFT/havale/FAST, para yatırma, POS satışı/bloke çözümü/üye işyeri,
  mevduat faizi; çıkış — giden EFT/havale/FAST, ATM/nakit para çekme, POS harcaması/alışveriş, kurum faturası (elektrik, su, doğal
  gaz, telefon…), otomatik ödeme, kredi kartı ödemesi, kredi taksidi, çek ödemesi, komisyon/vergi/ücret. "Maaş", "çek tahsilatı",
  yalın "POS", "faiz", müşterinin de yazabileceği yalın "fatura ödemesi" ve iade/iptal geçen satırlar iki anlamlıdır, belirsiz kalır.
  İki yöne de uyan açıklama belirsizdir.
- **Atlanan satırlar:** tutar içerdiği hâlde harekete alınmayan satırlar nedeniyle saklanır (en çok 300) ve belgede açılıp görülür.
- **PDF'yi yeniden oku:** yalnız hiç kaydı (iptal edilmişler dahil) ve yok sayılan satırı olmayan belgede; saklanan PDF yeniden okunur.
  Eski belgeler kendiliğinden değişmez.

## Sunucu

- `EkstreOkunanSatir.YonKaynagi` (`YonKaynaklari`: Sutun, Isaret, Bakiye, Sozcuk; eski satırda null).
- `EkstreBelgeler.AtlananJson`; yeni tablo `EkstreYokSayilanlar(Id, BelgeId, SatirNo)`, (BelgeId, SatirNo) tekil. Göç
  `20261125000100_EkstreSatirDurumu`.
- `EkstreBelgeDto`: `YokSayilanlar`, `Atlananlar`, `YenidenOkunabilir`; `EkstreBelgeOzetDto`: `YokSayilanSayisi`. Hepsi sona eklenir;
  eski istemci görmez, eski sunucuda istemci "Yok say" ve "Yeniden oku"yu göstermez.
- `POST /api/ekstre-aktar/{id}/yok-say` (`IstekId, Surum, SatirNolar, YokSay`) ve `POST /api/ekstre-aktar/{id}/yeniden-oku`
  (`IstekId, Surum`): yalnız Editor, istek kimliğiyle tekrar güvenli, belge sürümü eskiyse 409. Yeniden oku yükleme ile aynı hız sınırındadır.
- Kişisel kural önerisinde yok sayılan satır `YokSayildi` durumundadır (uygulanamaz).

## İstemci

- Masaüstü ve iOS (aynı sayfa). Web: yalnız yok sayılan satır kapalı görünür (kaydedilemez); yeni sekme/toplu işlem eklenmez.

## Sınamalar

Okuyucu: iki sıra, devir satırı, sarılı satır, çelişki, kopan zincir, yetersiz kanıt, kart belgesinde etkisiz, anahtar kelime ve iki
anlamlılar, atlananlar. Uç: yetki, sürüm, tekrar, kayıtlı satır, yok sayılanın önizlemesi, yeniden okuma koşulları, toplu satır hataları.
Model: hata listesi, kaydırma isteği, toplu uygula, sekmeler, taslak koruma, yön metni ve toplamlar, eski sunucu. Sözleşme ve göç testleri.
