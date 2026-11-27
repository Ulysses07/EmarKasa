# Ekstre hareket penceresi ve yapay zekânın satırları hazırlaması

Tarih: 26 Kasım 2026 · Kapsam: masaüstü (MAUI Windows; aynı sayfa iOS'ta da derlenir) · Durum: tasarım

## Neden

Kullanıcı Ekstre İçe Aktar ekranını kullandı ve üç şikâyeti var:

1. **Kaydırma takılıyor.** Hareket listesi, sayfanın kaydırıcısının içinde 520 px sabit yükseklikte ikinci bir kaydırıcı
   (`EkstreAktarmaPage.BekleyenBolumu`, `CollectionView.HeightRequest = 520`). Fare listenin üstündeyken tekerlek sayfayı değil
   listeyi kaydırıyor. Kullanıcının isteği: hareket satırları **açılır pencerede** olsun.
2. **Göz yoruyor, karmaşık, "AI slop" hissi.** Deneme kopyasında 52 satırlık belgeyle görülenler:
   - her satırda koyu yeşil "İncele / düzenle" düğmesi (52 aynı ağır düğme);
   - her satırda kalın renkli "Çıkış – · tutarın işaretinden / Giriş + · bakiyeden" yazısı;
   - tutar sütunda değil, metnin sonunda;
   - listeden önce 6 satır belge uyarısı ve iki açıklama paragrafı;
   - yükleme formu belge incelenirken de en üstte duruyor;
   - "Toplu uygula" ve "Kontrol ve kayıt" listenin çok altında.
3. **Yapay zekâ "hiçbir şey yapmıyor".** Öneri satırın altına küçük gri yazı olarak düşüyor; satırı işaretlemiyor, toplu
   işaretlemeye girmiyor (`EkstreSatirEditor.HazirOneri` yapay zekâ önerisini saymaz). Kullanıcının isteği: **"satırları kendisi
   doldurup hazırlasın, ben sadece kontrol edip kaydedeyim."**

## Amaçlar

- Belge açılınca hareketler uygulama penceresinin tamamını kaplayan bir pencerede gelir; içinde **tek kaydırma** vardır (tablo).
- Satırlar tablo gibi okunur: tarih, açıklama, tür · kanal, tutar (sağa hizalı). Satır başına düğme yoktur.
- Yapay zekâ belge açılınca kendiliğinden çalışır, tanınmayan satırları doldurur; güvenilir satırlar **işaretli gelir**.
  Kullanıcı yalnız "Kontrol edin" satırlarına bakar, etkisini görür ve kaydeder.

## Amaç olmayanlar

- Web arayüzü (kullanıcı yalnız masaüstü istiyor).
- PDF/Excel okuyucuları, maskeleme kuralları, sunucudaki öneri sırası (kural → hafıza → yapay zekâ → okuyucu) değişmez.
- Klavyeyle satırlar arası gezinme, sütuna göre sıralama, sütun genişliği ayarı (sonra istenebilir).
- iOS'ta ayrı bir tasarım: aynı pencere çalışır olmalı, inceltmesi ayrı iş.

## Olmaması gerekenler (yapılsa bile başarısızlık sayılır)

- Herhangi bir satırın kendiliğinden **kaydedilmesi**. Kayıt yine "Etkisini göster" → onay → "Kaydet" ile olur.
- Yönü okunamayan, uyarısı olan, düşük güvenli ya da kanalı eksik satırın kendiliğinden işaretlenmesi.
- Kullanıcının kaldırdığı işaretin ya da elle değiştirdiği alanın yeniden ezilmesi.
- Kırmızının giden tutar dışında bir şey için kullanılması; ekranda birden fazla birincil (koyu dolgulu) düğme.
- Aynı belge için yapay zekânın tekrar tekrar ücretlendirilmesi. Sunucu, sorulup yanıt alamamış satırı bilerek yeniden sorar
  (EmarKasa#65); kendiliğinden çalışma bu yüzden yalnız **hiç sorulmamış** satırları sorar (§3).

## Kısıtlar

- MAUI (C# ile kurulan görünüm; `TakipUi` yardımcıları ve `Colors.xaml`/stil anahtarları). Yeni NuGet paketi eklenmez
  (CommunityToolkit.Maui popup'ı yok; `Navigation.PushModalAsync` yeterli).
- `EkstreAktarmaViewModel` (App.Core) test edilebilir kalır; görünüm mantığı model tarafında, sayfa yalnız bağlar.
- `MimariTests`, `maui-lint.sh` (uzun satır, sabit renk), `GorunumEsdegerligiTests` ve sözleşme testleri yeşil kalır.
- Sunucu sözleşmesine yalnız geriye uyumlu ekleme yapılır (eski istemci aynı davranışı görür); yapay zekâ çağrısı mevcut uçlarla
  yapılır (`GET/POST /api/ekstre-aktar/{id}/yapay-zeka`).
- Aylık harcama sınırı (varsayılan 2 $) ve "aynı anda tek çağrı" kuralı aynen geçerli.

## Yaklaşımlar

| | Yol | Kazanç | Bedel | Karar |
|---|---|---|---|---|
| A | Uygulama içinde **tam pencere açılan sayfa** (`PushModalAsync`), aynı görünüm modeli | Tek kaydırma; Esc/× ile kapanır; iOS'ta da tam ekran çalışır | Açıkken yan menü görünmez | **Seçildi** (kullanıcı: "A ile devam et") |
| B | Ayrı Windows penceresi (`Application.OpenWindow`) | İkinci ekrana taşınabilir | İki pencere aynı belgeyi gösterirken durum eşitleme; iOS'ta yok | Elendi |
| C | Açılır pencere olmadan sayfayı tek kaydırmalı düzene çevirmek | En az kod | Kullanıcı açıkça açılır pencere istedi; yükleme formu ve diğer bölümler aynı sayfada kalır | Elendi |

## Tasarım

### 1. Ana sayfa (Ekstre İçe Aktar)

- Belge incelenirken kullanılan her şey ("2. Hareket satırları", "3. Kontrol ve kayıt") ana sayfadan kalkar ve pencereye taşınır.
- Kalanlar: kısa "Yeni belge yükle" kartı (belge türü, banka/kart, hesap adı, "PDF ya da Excel seç"), "İçe aktarma geçmişi" listesi,
  mevcut İşlem hafızası, Kişisel kurallar ve Yapay zekâ kartları.
- Yükleme başarılı olunca ya da geçmişte "Belgeyi incele"ye basınca pencere açılır. `KaynakAcAsync` (işlemlerden kaynağa gitme) da
  pencereyi açar.
- Açıklama paragrafları en çok bir kısa cümleye iner. Sayfa başlığının altındaki uzun metin ve "1." "2." numaralı kart başlıkları
  kalkar. Bunlar slop hissinin kaynağı.

### 2. Hareket penceresi

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ HesapHareketleri.pdf                                                 ⋯   ×   │
│ QNB · Ek hesap · 1–31 Ocak · 501 hareket                                     │
│ [Tümü 501] [Hazır 462] [Kontrol edin 39] [Kapanan 0]              🔍 Ara     │
├────────────────────────────────────────────────────┬─────────────────────────┤
│ ☐  Tarih  Açıklama            Tür · kanal     Tutar │ Satır 2                 │
│ ☑  01.01  POS satış · k.kartı Gelir · Mağaza  +18.500,00 │ 01.01.2026 · −553,15 ₺  │
│            (yeşil)                    hafıza          │ Açıklama [..........]   │
│ ☑  01.01  Üye işyeri ücreti   Gider · Genel     −553,15 │ Tür [Gider ▾]          │
│            (kırmızı)                 yapay z.         │ Kanal [Genel ▾]        │
│▌☐  02.01  Havale gelen [ad]   Gelir · kanal seçin +1.500 │ Yapay zekâ · yüksek ·  │
│▌☐  03.01  Kredi taksidi       Kontrol edin    −8.250,00 │ banka masrafı          │
│  …                                                   │ PDF: ŞB Kart İşl…      │
│                                                      │ [Yok say] [Hatırla]    │
├────────────────────────────────────────────────────┴─────────────────────────┤
│ 462 işaretli · giriş +41.908,12 · çıkış −119.338,49   [Toplu tür/kanal] [Yok say] [Etkisini göster ve kaydet] │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Başlık:** dosya adı; altında banka · hesap/kart · tarih aralığı · hareket sayısı. Sağda ⋯ menüsü ve × (Esc de kapatır; açık
  ayrıntı paneli varsa Esc önce paneli kapatır).
- **⋯ menüsü:** Kaynak dosyayı indir · Dosyayı yeniden oku · (ERP12) Bu dosyayı sil · Atlanan satırlar (n) · Belge uyarıları (n) ·
  Yapay zekâya gönderilen metni göster. Belge uyarıları artık listenin üstünde düz metin olarak durmaz.
- **Süzgeçler:** Tümü · Hazır · Kontrol edin · Kapanan (sayılarıyla). "Hazır" ve "Kontrol edin" bekleyen satırların iki parçasıdır.
  Arama kutusu açıklamada geçen metne göre süzer.
- **Tablo:** tek kaydırıcı. Sütunlar: işaret kutusu · tarih (gg.aa) · açıklama (tek satır, taşan kısım "…") · tür · kanal ve altında
  küçük gri kaynak (hafıza / yapay z. / kural / ERP12 / boş) · tutar (sağa hizalı, eşit genişlikli rakam).
  - Gelen tutar yeşil "+", giden tutar **kırmızı "−"** (kullanıcı isteği), yönü belirsiz tutar sarı "?". Satırın kendi yazısı
    renksizdir; kalın yazı yoktur.
  - "Kontrol edin" satırlarında solda sarı ince çizgi ve tür sütununda nedeni: "kanal seçin", "yön belirsiz", "emin değil",
    "uyarı var", "kasa hareketi olmayabilir".
  - Satıra tıklamak ayrıntı panelini açar ya da (aynı satırsa) kapatır; işaret kutusuna tıklamak yalnız işareti değiştirir.
  - "Tutarın işaretinden / bakiyeden" gibi kaynak notları tabloda görünmez, panelde görünür.
- **Ayrıntı paneli (sağda, ~320 px):** bugünkü satır formunun içeriği (`SatirFormunuKur`): tarih, tutar, açıklama, tür, kanal
  dağılımı ve paylar, kart / iade kaynağı / eşleşme adayı, öneri ve dayanağı, PDF'teki satır, satır uyarıları, "Öneriyi uygula",
  "Bu seçimi hatırla", "Bu hafızayı unut", "Yok say". Kapanan satırda "Yok saymayı geri al" / "Gerekçeyle iptal et".
  Pencere 900 px'ten darsa panel tablonun üstüne tam genişlikte açılır, "Geri" ile kapanır.
- **Alt çubuk (hep görünür):** işaretli satır sayısı, işaretlilerin giriş/çıkış toplamı; "Toplu tür / kanal" (bugünkü toplu
  uygulama alanlarını küçük bir pencerede açar), "Yok say" (işaretliler, bugünkü onayla), birincil düğme
  **"Etkisini göster ve kaydet"**.
- **Etki ve kayıt:** "Etkisini göster ve kaydet" bugünkü önizlemeyi çalıştırır. Sonuç sağ panelde "Kayıt özeti" olarak açılır:
  toplamlar, kanal bazında kasa etkisi ve varsa uyarılar (tablo düzeni: docs/specs/2026-11-26-slop-temizligi.md §9), benzer kayıt onayı (gerekirse), "Seçilen satırları, kanal paylarını ve kasa etkisini kontrol ettim" onayı,
  "Kaydet". Önizleme hataları satır bağlantılı listede durur; "Satıra git" tabloyu o satıra kaydırıp paneli açar.
- **Boş ve yükleniyor durumları:** belge okunamadıysa tablo yerine tek cümle ve "Kaynak dosyayı indir". Yapay zekâ çalışırken başlığın
  altında ince bir satır: "Yapay zekâ 114 satırı dolduruyor…"; bitince "Yapay zekâ 98 satırı doldurdu" (birkaç saniye sonra
  kaybolur).

### 3. Yapay zekânın satırları hazırlaması

- **Ne zaman:** pencere bir belgeyle açılınca (yükleme, yeniden okuma, geçmişten açma, kaynağa gitme). Yapay zekâ açıksa ve belge ERP12
  değilse istemci soruyu **onay penceresi olmadan** gönderir.
- **Yalnız hiç sorulmamış satırlar (karar):** istek gövdesine geriye uyumlu `YalnizSorulmamis` (varsayılan false) eklenir. Doğruysa
  sunucu `Hazirla`'da daha önce sorulmuş satırları dışarıda bırakır; sorulacak satır kalmadıysa bugünkü 409 döner ve döngü sessizce
  biter. Önizleme ucu (`GET`) aynı süzgeci sorgu değişkeniyle (`?yalnizSorulmamis=true`) alır. Böylece pencereyi her açışta yeniden
  ücret doğmaz; yanıt alamamış satırlar yalnız ⋯ menüsündeki **"Yanıtsız satırları yeniden sor"** ile (bugünkü onay metniyle) sorulur.
- **Döngü:** sunucu bir çağrıda en çok 60 grup sorar; istemci "kalan satır" bitene, 409 gelene ya da bir turda yeni öneri gelmeyene
  kadar sırayla yeniden sorar (en çok 10 tur). Bir hata (bağlantı, sınır, 5xx) döngüyü durdurur ve alt çubukta tek satır gösterir.
- **Çalışırken (karar):** döngü bugünkü tek-işlem yürütücüsünde (`YurutAsync`) çalışır; pencere bu sürede meşguldür, başlığın altında
  ilerleme yazar ("Yapay zekâ 114 satırı dolduruyor… 1/2"). Yalnız belgenin ilk açılışında sorulacak satır olur; 500 satırlık ekstre
  yaklaşık 2 tur, 10–40 saniye. Böylece önizleme ve kayıt yapay zekâyla yarışmaz. Kullanıcı beklemeden çalışmak isterse sonraki iş.
- Bugünkü "Tanınmayan N satırı yapay zekâya sor" düğmesi ve her gönderimdeki onay penceresi kalkar. ⋯ menüsündeki
  **"Yapay zekâya gönderilen metni göster"** bu oturumda son gönderilen maskeli satırları gösterir; oturumda gönderim olmadıysa
  sonraki gönderimde gidecekleri (bugünkü önizleme) gösterir (karar).
- **Ne doldurur:** bugünkü gibi tür, kanal dağılımı, açıklama (`HafizayiUygula` yapay zekâ önerisini de uygular).
- **Hazır sayılan (işaretli gelen) satırlar:**
  - hafızadan eksiksiz dolan satır (bugünkü `HazirOneri`),
  - kesin kişisel kural önerisi (bugünkü `OneriUygulanabilir`),
  - ERP12 eşleşmesi (bugünkü kural),
  - **yeni:** yapay zekâ önerisi; güveni yüksek ya da orta, tür "kasa hareketi değil" değil, satır eksiksiz kaydedilebilir
    (`Hazirla().Yaz` dolu; yani kanal dağılımı da biliniyor) ve hafızanın "kontrol edin" nedenlerinden hiçbiri yok (karar: hafızayla
    aynı ölçü, `EkstreHafizaServisi.Nedenler`): tarih/tutar/TL kesin, satırda okuma uyarısı yok, yön Giriş ya da Çıkış ve önerilen
    türle çelişmiyor, Gelir/Gider önerilen satır hesaplar arası transfer sınıfında değil, kanal etkin, özel pay yok, karta ödemede kart
    takipte, iade ve eşleşme değil. Yön kart ekstresinde okuyucunun varsaydığı "Çıkış" da sayılır (hafızada da öyle). Sunucu bu
    nedenleri yapay zekâ önerisi için de hesaplar ve öneriyi `Hazir` ya da `Kontrol` durumuyla döner (geriye uyumlu: eski istemci
    `YapayZekaOnerisi` görür gibi davranır; ayrıntı plana kalır).
- **Kendiliğinden işaretleme:** pencere açılınca ve yapay zekâ her turdan sonra, hazır olup daha önce kendiliğinden işaretlenmemiş
  satırlar işaretlenir. Kullanıcının kaldırdığı işaret ve elle değiştirdiği satır bir daha kendiliğinden işaretlenmez (satır başına
  "kendiliğinden işaretlendi" izi; taslak yenilemede taşınır). "Hazırları işaretle ve etkisini göster" düğmesi kalkar.
- **Hazır / Kontrol edin ayrımı (karar):** satırın hangi süzgeçte olduğu öneriden hesaplanır, işaretten değil; pencere açılınca,
  her yapay zekâ turundan sonra ve yenilemede belirlenir, çalışırken satır süzgeç değiştirmez (açık paneldeki satır listeden kaybolmaz).
  "Kontrol edin" satırındaki sarı çizgi ve neden canlıdır: kullanıcı eksiği tamamlayınca (ör. kanal seçince) kalkar, satır bir sonraki
  hesaplamada "Hazır"a geçer. Hazır satırı elle düzenlemek, işaretini kaldırmak ya da toplu tür/kanal vermek onu "Kontrol edin"e
  taşımaz. Süzgeç sayıları her hesaplamada güncellenir.
- **Kontrol edin:** bekleyen ama hazır olmayan her satır. Yapay zekânın "kasa hareketi değil" önerisi işaretlenmez ve yok sayılmaz;
  nedeni "kasa hareketi olmayabilir" yazar, kullanıcı panelden ya da toplu "Yok say"la kapatır. Hafızanın yok sayma önerileri için
  bugünkü toplu düğme ⋯ menüsüne değil "Kontrol edin" süzgecinin üstüne, tek satır olarak gelir.

## Değişen belgeler

- `docs/specs/2026-11-25-yapay-zeka-onerileri.md`: "yalnız düğmeyle sorulur", "öneri satırı seçmez, hazır saymaz, toplu işaretlemeye
  girmez" kuralları bu belgedeki §3 ile değişir; gerekçe: kullanıcı kararı (26.11.2026).
- `docs/specs/2026-11-25-pdf-ekrani.md` ve `2026-11-25-islem-hafizasi.md`: "Hazırları işaretle" ve satır düzeni bölümleri bu belgeye
  bağlanır.

## Builder'a kalanlar

- Pencerenin sınıf ve dosya bölümlemesi (`EkstreAktarmaPage` parçalarının yeni sayfaya taşınması), stil anahtarlarının adları.
- Tablo satırının hücre yapısı (sanallaştırmayla uyumlu tek `Grid`), sütun genişlikleri ve 900 px dar düzen eşiğinin ince ayarı.
- Süzgeç ve arama için görünüm modelinde hangi koleksiyonların tutulacağı (500+ satırda akıcı kalmalı).
- Yapay zekâ döngüsünün en çok tur sayısı (öneri: 10) ve iptal (pencere kapanınca ya da oturum değişince durur).

## Sınama

- App.Core: hazır kuralı (yapay zekâ güven/yön/uyarı/kanal durumları), kendiliğinden işaretleme ve kullanıcının kaldırdığı işaretin
  korunması, yapay zekâ döngüsü (kalan, 409, ilerleme yok, hata), süzgeç sayıları, alt çubuk toplamları.
- Görünüm: deneme kopyasıyla ayrı masaüstünde ekran görüntüleri (`scratchpad/deneme/kosum`): 52 ve 500 satırlık belge, açık panel,
  dar pencere, Kayıt özeti. Tablonun tek kaydırıcı olduğu UIA ile doğrulanır.
- Mevcut testler: `EkstreSatirGorunumuTests`, `EkstreYapayZekaTests`, hafıza ve kural testleri yeni davranışa göre güncellenir.
