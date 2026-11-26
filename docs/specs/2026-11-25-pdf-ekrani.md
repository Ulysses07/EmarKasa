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
- Kaydetme, iptal, yok sayma, geri alma ve **yenilemeden** (bağlantı dönünce kendiliğinden yenileme dahil) sonra aynı belgenin bekleyen
  satırlarında doldurulmuş taslaklar ve işaretler korunur; kanal ve kart listesi yenilendiyse taslak yeni listelere taşınır, artık
  olmayan kanalın payı kanalsız kalır. Satır bu arada başka cihazda yeniden okunduysa yalnız elle değiştirilenler taşınır, işaret kalkar. Yok sayılan satır geri alınınca taslağı (işaretsiz) gelir. Birden çok satır onayla yok sayılır,
  hepsi tek düğmeyle geri alınabilir.

## 3. Giriş / çıkış

- Her satırda **Giriş +**, **Çıkış −** ya da **Belirsiz** metni (renkle birlikte) ve okunduysa dayanağı ("bakiyeden", "açıklamadan").
  Kullanıcı tür seçince yön türden gelir; PDF'de okunan yönle çelişirse "PDF: …" eklenir.
- Belgenin üstünde giriş, çıkış toplamı ve belirsiz sayısı; düğmenin üstünde işaretlilerin toplamları.
- **Bakiyeden çözme (yalnız banka):** satırda tutar ve bakiye varsa, komşu satırın bakiyesiyle fark satırdaki tutarlardan
  birine kuruşu kuruşuna eşitse tutar ve yön farktan alınır. Belge eskiden yeniye ya da yeniden eskiye sıralı olabilir; sıra
  tutan bağların sayısından bulunur. Yanlış yön üretmemek çözmekten önce gelir:
  - Bağların en az üçte ikisi tutmalıdır; başlıkta bakiye kolonu yoksa ayrıca en az iki bağ gerekir.
  - Komşusuyla aynı tutarlı satır (aynı gün iki eşit çekim) ters sırada da tutar; yönü ancak sırası kanıtlanmış bir komşuya
    dayanıyorsa okunur.
  - Bakiyenin yanında okunamayan eksi (tire türleri, parantez, "BORÇ"; arada boşluk, para simgesi ya da TL olabilir) varsa bakiye
    kullanılmaz. Bakiyeden sonraki B/A eki işaret de işlem yönü de olabilir; yalnız birinin açıkça (bağların beşte dördü) tuttuğu belge
    çözülür, bütün satırları B olan belge çözülmez. Ardından tek boşlukla metin gelen A/B ("B BLOK", "A 101") ek sayılmaz, o bakiye de
    kullanılmaz.
  - Tutarlı bir satır atlandıysa (özet satırı dahil), hesap numarası/IBAN ya da para birimi bölümü değiştiyse, sayfada numarası okunamayan
    bir hesap başlığı varsa ya da sayfa ortasında tablo yeniden başladıysa iki yanındaki bakiyeler karşılaştırılmaz.
  - Bir günde komşusuyla tutmayan satır varsa o gün karışık basılmış olabilir: o günün satırlarının yönü bakiyeden okunmaz.
  - Bakiyeden okunan yönler yazılı kanıtla (kolon, ek, işaret) ya da birden çok "gelen/giden EFT-havale-FAST" ile en az uyuştuğu kadar
    çelişiyorsa zincir bütünüyle bırakılır ve belge uyarısı yazılır (işareti yazılmayan borç bakiye, kredi hesabı). Bırakılan zincirle
    çelişen açıklama tek başına yön vermez. Tek bir açıklama zinciri düşürmez; o satırda bakiye kazanır ve uyarı yazılır.
  - Yalnız "devir / önceki / açılış / başlangıç bakiyesi" yazan satır ilk satırın komşusudur; açıklamasında bakiye geçen hareket değildir.
  - Tutarı satırda yazmayan hareketin tutarı farktan **uydurulmaz**.
  - pdftotext bir sayfanın metnini başlıkla hizalı çıkarmayabilir (QNB hesap hareketi: ilk sayfadan sonra tutar ve bakiye 8–10 karakter
    sola kayar). Sayfanın her tablosu (aynı başlığın altındaki hareketler) ayrı değerlendirilir: bakiye kolonu olan tabloda tek satırlı, birden
    çok tutarlı hiçbir hareketin bakiye kolonunda tek tutarı yoksa ve tablo başlıksız okunduğunda kendi içinde bakiye zinciri kuruyorsa o tablo
    başlıksız okunur (son tutar bakiye adayıdır, başlıksız satırın eşiği geçerlidir) ve belge uyarısı sayfaları söyler. Zincir kurmayan tablo
    eskisi gibi kalır (tutar elle girilir); aynı sayfadaki hizalı başka bir tablonun kolonlarına dokunulmaz.
- Borç/Alacak kolonu, B/A eki ya da işaretten okunan yön bakiyeyle **çelişirse** yön "Belirsiz" olur ve uyarı yazılır.
- Bakiye zinciri kurulmuşken bir satırda kopuyorsa belge uyarısı satır numaralarını söyler (arada okunamayan hareket olabilir).
- **Anahtar kelime (yalnız banka, başka kanıt yoksa):** giriş — gelen EFT/havale/FAST, para yatırma, POS bloke çözümü, POS/üye işyeri
  alacağı ya da hakedişi, mevduat faizi; çıkış — giden EFT/havale/FAST, ATM/nakit para çekme, POS harcaması/alışveriş, kurum faturası
  (elektrik, su, doğal gaz, telefon…), otomatik ödeme, kredi kartı ödemesi, kredi taksidi, çek ödemesi, komisyon/vergi/stopaj/ücret.
  Giriş gibi görünen açıklamada kesinti, vergi, bedel, kira, kredili mevduat gibi sözcük varsa belirsiz kalır. Yönü yazmayan
  havale/EFT'nin açıklaması karşı tarafındır: yalnız işlemin kendi masrafı ("EFT ücreti", "havale komisyonu") yön verir. "Maaş", "çek tahsilatı", "POS satışı", yalın "faiz",
  müşterinin de yazabileceği "fatura ödemesi" ve iade/iptal geçen satırlar iki anlamlıdır, belirsiz kalır. İki yöne de uyan açıklama
  belirsizdir.
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
