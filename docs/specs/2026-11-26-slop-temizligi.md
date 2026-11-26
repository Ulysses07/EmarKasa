# Masaüstü arayüz sadeleştirmesi ("AI slop" temizliği)

Tarih: 26 Kasım 2026. Kaynak: "Kasa arayüzünde AI slop nedenleri" araştırma raporu (sekiz adım). Kullanıcı kararı: "önerdiğin çözümlerin
hepsini yap". Hedef görünüm uygulamada zaten var: ekstre hareket penceresi ve Haftalık ekranı.

## Neden

Sahibine uygulama "yapay zekâ yapmış" gibi geliyor. Araştırmanın teşhisi: kararların varsayılanlara bırakılması. Dört kök neden:

1. Örtük düğme stili dolu yeşil; kodla yaratılan her düğme ana düğme oluyor (ekranda 4–10 yeşil leke).
2. Her takip sayfası zorunlu bir açıklama paragrafı, kartlar savunmacı kural cümleleri ("kasa değişmez", "oluşturmaz") taşıyor.
3. Tutarlar sütunda değil cümlenin içinde; her satırda ağır düğmeler; renk anlamı tutarsız (gider yeşil, sıfır yeşil).
4. İki görsel sistem (XAML "handoff" ile kodla yazılmış takip sayfaları) yan yana; beş ayrı Yenile şeridi.

Eylül'deki temizlik görünümü bilerek korudu (`GorunumEsdegerligiTests`); his bu yüzden değişmedi. Bu iş görünümü bilerek değiştirir.
Eşdeğerlik testleri yapıyı korumaya devam eder; görünüm beklentileri yeni kurallara göre güncellenir, gereksizleşenler kaldırılır.

## Kurallar (ekran başına sorulacak sorular)

| Soru | Hedef |
|---|---|
| Kaç dolu yeşil düğme görünüyor? | En çok 1 |
| Başlık altında ve kartlarda kaç açıklama cümlesi? | En çok 1 kısa cümle |
| Tutarlar sütunda, sağa hizalı ve işaretli mi? | Evet; gider "−" ve kırmızı, sıfır gri |
| Bir satırda kaç görünür düğme? | En çok 1; gerisi "⋯" menüsünde |
| Yeşil başka ne iş yapıyor? | Yalnız marka menüsü, ana düğme, gelir ve seçim (açık yeşil) |
| Düğme ya da etikette "/" ya da "—" var mı? | Hayır |

## §1 Düğmeler ve yeşil (PR 1)

- Örtük `Button` stili ikincildir: beyaz zemin, `Border` kenar, koyu yazı. Ana düğme `BtnPrimary` ile bilerek seçilir; yıkıcı eylem `BtnDanger`
  (kırmızı yazılı ikincil). Kodda `TakipUi.Birincil(...)` ve `TakipUi.Tehlikeli(...)`.
- Ana düğme: açık formun kaydetmesi ya da sayfanın tek asıl işi (Giriş, "PDF ya da Excel seç", Excel'e aktar). "Yeni …", Yenile, gezinme,
  süzgeç, Ara, Vazgeç ve form açan düğmeler ikincildir.
- Aynı anda sürekli açık birden çok form olan sayfada (Alışlar'da alış ve ödeme, Aylık giderler'de ödeme ve şablon, Krediler'de taksit ve
  erken kapama) yalnız asıl formun kaydı ana düğmedir; sürekli açık ikincil formun kaydı, form isteğe bağlı açılana kadar (§8) ikincildir.
  Birbirini dışlamayan iki form ender olarak birlikte açılırsa (Çekler'de düzeltme ile hareket) her biri kendi kaydını ana düğme tutar.
- Seçili çip açık yeşil zemin (`GreenSoft`), yeşil kenar ve yazı; dolu yeşil ana düğmeyle karışmaz. Onay kutusu yeşil.
- Test: kodla yazılmış her sayfa (yüklenmiş ve form açık hâlleri) için beklenen ana düğme sayısı (`DugmeVeRenkKurallariTests`).

## §2 Metin (PR 2)

- `TakipSayfasi` açıklama paragrafı isteğe bağlı ve varsayılanı yok; sayfa paragrafları kalkar.
- Kart başı kural paragrafları silinir ya da ilgili alanın yanında, kullanıcı kısıta çarptığı anda bir kez söylenir.
- "Değişmez / kaydetmez / oluşturmaz" yalnız geri alınamaz işlemin onay penceresinde ve kısıt anında.
- Önizleme gösterilen yerlerde "kontrol ettim / onaylıyorum" kutuları kalkar; kutu yalnız gerçekten tehlikeli toplu işlemde kalır.
- Düğmede tek fiil, tek nesne; "/" ve "—" yok. Parantezli niteleyiciler etiketten çıkar.
- Microsoft Türkçe yazım kılavuzu: "siz" hitabı, kısa etken cümle, durum iletisinde edilgen, hata iletisinde ne oldu ve ne yapılacak,
  uzun tire ve noktalı virgülle zincir yok.

## §3 Başlık satırı (PR 2)

- Beş Yenile/Son güncelleme uygulaması tek sayfa başlığı bileşenine iner: solda başlık, sağda küçük "Yenile", yalnız veri eskiyken
  "Son güncelleme". "Tekrar dene" yalnız hata kutusunda. F5 sayfayı yeniler.
- Gömülü bölümlerin (Kasalar, Aylık, Ayarlar) kendi Yenile satırları kalkar, sayfanın yenilemesine bağlanır.

## §4 Para rengi (PR 1)

- `ParaRenkConverter`: artı `Pos`, eksi `Neg`, sıfır `Sub`; renkler paletten. Parametre `Gider`: tutar gider sayılır.
- `ParaBicimConverter` parametre `Gider`: "−1.334,44", iade "+50,00" (ekstre penceresiyle aynı eksi işareti).
- İşlemler gider listesi tutarı yönüyle ve gider rengiyle yazılır.
- Kasalar'da eksi genel kasa `HeroNeg` renginde; özet kutusunun gölgesi hafif.
- Takip sayfalarının hata ve ileti rengi `Neg` / `Pos` (MAUI adlı DarkRed / DarkGreen ve `Colors.Red` kalktı).

## §5 Küçük işaretler (PR 2)

- BÜYÜK HARF `LblSection` etiketleri cümle biçiminde yarı kalın bölüm başlığı; harf aralığı yok.
- Alışlar'daki "1 ·", "2 ·" numaraları ve boş taslak için yeşil özet kutusu kalkar.
- Kanal satırlarındaki baş harf çipleri kalkar; sıfır olan "Kalan kart borcu" satırı gizlenir; Haftalık'ta tarih tekrarı kalkar;
  Ayarlar'da sıra 1'den başlar.
- Orta nokta yalnız veri alt satırlarında. Menü adı ile sayfa başlığı aynı; başlıklar cümle biçiminde.
- Üç boş durum biçimi tek bileşen: kısa cümle ve varsa tek eylem.

## §6 Tablolar (PR 4)

- İşlemler satırındaki "Düzenle / Sil / Kaynak PDF" üçlüsü kalkar: satıra tıklamak düzenlemeyi açar, öteki eylemler "⋯" menüsünde.
- `TakipUi.Liste` kullanan listeler ekstre tablosunun desenine geçer: tarih, açıklama, tür/kanal, sağa hizalı tutar; satıra tıklayınca
  ayrıntı. Satır yüksekliği 40 (yoğun 32).

## §7 Tek sistem (PR 3)

- Tip ölçeği XAML handoff ölçeği: sayfa başlığı 21, gövde 14, ikincil 12,5; takip sisteminin 28 ve 18 pt'si kalkar.
- Boşluklar 4'ün katı: düğmeler arası 8, denetim ile etiketi 12, yüzey kenarı ile metin 16.
- Form alanları tek görünüm; XAML alanlarındaki çift çerçeve (WinUI'nin kendi kenarlığı) kalkar. Kart içinde kart yok.
- Windows tipinde Bold yok: Semibold.

## §8 Yerleşim ve kısayollar (PR 5)

- Kasalar yalnız özet; alt limit, gerçek bakiye karşılaştırması ve hareket dökümü "Kasa kontrolü"ne taşınır; 7/30 gün çipe döner.
- Formlar yalnız "Yeni …" ile açılır (Krediler'in yeni kredi formu, Aylık giderler'in şablon formu, Krediler'in erken kapama formu).
- Ay gezinmesi her yerde "‹ Ay Yıl ›" bileşeni. Kısa alanlar içerik genişliğinde ve yan yana.
- Rapor: tarih aralığı yan yana, kanal seçici ("Tüm kanallar" ilk seçenek), biçim seçimi ve tek "Dışa aktar".
- Ekstre kayıt özeti: toplamlar, kanal bazında etki ve uyarılar; "yapay zekâ" kaynak etiketi yalnız varsayılandan farklıysa.
- "Uygulama güncellemeleri" Ayarlar'a; güncelleme varken menüde rozet.
- Kısayollar: Ctrl+N yeni kayıt, Ctrl+F arama, Ctrl+S kaydet, F5 yenile.

## Korunacaklar

Palet ve kontrast eşikleri, koyu yeşil gruplu menü, Haftalık ve Aylık tabloları, ekstre penceresi, `BtnRowEdit` / `BtnRowDelete`, anlam
taşıyan amber "Kontrol edin" şeridi, hata sonrası son verinin soluk kalması, Türkçe sayı biçimi. Paleti değiştirmek hedef değildir: liste
bir rengi değil karar verilmemişliği hedefler. Windows kuralı (tek yazı tipi, Semibold, cümle biçimi) web tavsiyelerine üstündür.

## Doğrulama

Her PR'da: App.Core testleri, biçim ve MAUI lint, uygulama derlemesi ve deneme kopyasıyla (ayrı uygulama kimliği, yerel sunucu, örnek veri)
17 ekranın yeniden çekilip öncekiyle karşılaştırılması.
