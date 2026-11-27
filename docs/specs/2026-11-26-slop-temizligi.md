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

## §2 Metin (PR 2: kodla yazılmış sayfalar; PR 3: XAML sayfaları)

- `TakipSayfasi` açıklama paragrafı almaz; sayfa paragrafları ve XAML alt başlıkları kalkar.
- Kart başı kural paragrafları silinir (güvence ve tekrar) ya da kısıt anında tek kısa cümle olarak kalır ("İşlenmiş taksidin tarihi ve
  tutarı değiştirilemez."). Uzun ama gerekli kural açıklaması `TakipUi.Yardim` ile "Ayrıntılar" bağlantısının arkasındadır (aşamalı
  gösterim); ekranda kalan ipucu en çok 80 karakterdir. Kalan ipucu ve liste özeti ikincil (gri) yazıdır.
- "Değişmez / kaydetmez / oluşturmaz" yalnız geri alınamaz işlemin onay penceresinde ve kısıt anında.
- Mali işlemlerin onay kutuları (aylık gider ödemesi, kredi geçişi ve erken kapama, ekstre kaydı) muhasebe güvenliği için kalır, metinleri
  kısalır. Kaldırmak bir muhasebe kararıdır; sahibine bırakıldı.
- Düğmede tek fiil, tek nesne; "/" ve "—" yok. Parantezli niteleyiciler etiketten çıkar.
- Microsoft Türkçe yazım kılavuzu: "siz" hitabı, kısa etken cümle, durum iletisinde edilgen, hata iletisinde ne oldu ve ne yapılacak,
  uzun tire ve noktalı virgülle zincir yok.

## §3 Başlık satırı (PR 2)

- Beş Yenile/Son güncelleme uygulaması tek sayfa başlığı bileşenine (`Controls.SayfaBasligi`) iner: solda başlık, sağda yalnız meşgulken
  gösterge, yalnız veri eskiyken "Son güncelleme" ve küçük "Yenile" (`BtnKucuk`); altında yalnız doluyken hata ve ileti. Sayfaya özgü tek
  eylem (Alışlar'da "Yeni alış") Yenile'nin solunda. `DurumSeridi` ve `TakipUi.DurumSatirlari` kalktı.
- Yenile sayfanın yenilemesidir (`IYenilenebilir`, kabuğun "Yeniden dene"siyle aynı yol): gömülü bölümler (Kasalar'daki kasa kontrolü,
  Aylık'taki ay kilidi, Ayarlar'daki alt limitler) kendi Yenile satırlarını kaybeder (`TakipUi.BolumDurumu`).
- Sayfa başlıkları menü adıyla aynı ve cümle biçiminde ("Kartlar", "Aylık giderler", "Çekler", "Rapor dışa aktar").
- F5 kısayolu öteki kısayollarla birlikte PR 6'da.

## §4 Para rengi (PR 1)

- `ParaRenkConverter`: artı `Pos`, eksi `Neg`, sıfır `Sub`; renkler paletten. Parametre `Gider`: tutar gider sayılır.
- `ParaBicimConverter` parametre `Gider`: "−1.334,44", iade "+50,00" (ekstre penceresiyle aynı eksi işareti).
- İşlemler gider listesi tutarı yönüyle ve gider rengiyle yazılır.
- Kasalar'da eksi genel kasa `HeroNeg` renginde; özet kutusunun gölgesi hafif.
- Takip sayfalarının hata ve ileti rengi `Neg` / `Pos` (MAUI adlı DarkRed / DarkGreen ve `Colors.Red` kalktı).

## §5 Küçük işaretler (PR 3)

- BÜYÜK HARF `LblSection` etiketleri cümle biçiminde yarı kalın bölüm başlığı; harf aralığı yok.
- Alışlar'daki "1 ·", "2 ·" numaraları ve boş taslak için yeşil özet kutusu kalkar.
- Kanal satırlarındaki baş harf çipleri kalkar; sıfır olan "Kalan kart borcu" satırı gizlenir; Haftalık'ta tarih tekrarı kalkar;
  Ayarlar'da sıra 1'den başlar.
- Orta nokta yalnız veri alt satırlarında. Menü adı ile sayfa başlığı aynı; başlıklar cümle biçiminde.
- Üç boş durum biçimi tek bileşen: kısa cümle ve varsa tek eylem.

## §6 Tablolar (PR 5)

- İşlemler satırındaki "Düzenle / Sil / Kaynak PDF" üçlüsü kalkar: satıra tıklamak düzenlemeyi açar, öteki eylemler "⋯" menüsünde.
- `TakipUi.Liste` kullanan listeler ekstre tablosunun desenine geçer: tarih, açıklama, tür/kanal, sağa hizalı tutar; satıra tıklayınca
  ayrıntı. Satır yüksekliği 40 (yoğun 32).

## §7 Tek sistem (PR 4)

- Tip ölçeği XAML handoff ölçeği: sayfa başlığı 21, gövde 14, ikincil 12,5; takip sisteminin 28 ve 18 pt'si kalkar.
- Boşluklar 4'ün katı: düğmeler arası 8, denetim ile etiketi 12, yüzey kenarı ile metin 16.
- Form alanları tek görünüm; XAML alanlarındaki çift çerçeve (WinUI'nin kendi kenarlığı) kalkar. Kart içinde kart yok.
- Windows tipinde Bold yok: Semibold.

## §8 Yerleşim ve kısayollar (PR 6)

- Masaüstünde Shell gezinme çubuğu yalnız bağlantı koptuğunda (bağlantı şeridi için) görünür; sayfa başlığı sayfanın içinde olduğundan
  üstteki boş 48 px bant kalkar. iOS'ta menü düğmesi için çubuk kalır.
- Formlar yalnız istenince açılır: Krediler'in yeni kredi formu ("Kredi ekle") ve erken kapama formu ("Erken kapama"), Aylık giderler'in
  şablon formu ("Yeni şablon", "Şablonu düzenle"). Kayıt formu kapatır; "Vazgeç" formu temizleyip kapatır (gizli formda kaydedilmemiş
  değişiklik kalmaz). Krediler'in ayrıntı kartı yalnız kredi seçiliyken görünür.
- İçi gizli sarmalayıcı da gizlidir (`TakipUi.Editor(görünüm, yol)`): içi gizli ama kendisi görünür sarmalayıcı kartlar arasında fazladan
  boşluk bırakıyordu.
- Aylık giderler'de ay gezinmesi Aylık sayfasıyla aynı "‹ Kasım 2026 ›" satırı; ok basınca ay hemen yüklenir. Ay özeti yalnız toplamları
  yazar (ay adı gezginde).
- Kasalar'da 7 / 30 gün seçimi seçili durumu olan çip. Yan yana duran düğmeler `TakipUi.DugmeSatiri` ile içerik genişliğinde dizilir
  (kurallar, Kasalar, Bildirimler, çek ve kredi formları, Ayarlar'daki hesap güvenliği).
- Rapor: tarih aralığı yan yana, kanal seçici ("Tüm kanallar" ilk seçenek), biçim seçimi (Excel, CSV, PDF) ve tek "Dışa aktar".
- Menüdeki güncelleme düğmesi yalnız yeni sürüm bulununca görünür ("Güncelleme var"); el ile denetim Ayarlar → "Sürüm ve yedek"te.
- Kısayollar (Windows): F5 yenile (başlık satırının Yenile'siyle aynı yol), Ctrl+N yeni kayıt, Ctrl+S kaydet, Ctrl+F arama (ekstre
  penceresi). Sayfa `Controls.IKisayolHedefi` ile komutunu verir; Ctrl+S yalnız ilgili form açıkken vardır. Sayfa üstü pencere (silme
  onayı, güncellemeler) açıkken kısayol alttaki sayfaya gitmez. Tuş dinleyicisi `Platforms/Windows/KlavyeKisayollari.cs`.
- Sonraya kalan iki iş ayrı PR'larla yapıldı: ekstre kayıt özetinin yeniden düzeni (§9) ve Kasalar'daki kasa kontrolünün ayrı
  "Kasa kontrolü" sayfasına taşınması (§10).

## §9 Ekstre kayıt özeti (PR 7)

"Etkisini göster ve kaydet" sonrası sağ paneldeki "Kayıt özeti" seçilen her satırı ikinci kez düzyazıyla yazıyordu (34 satır için 34
paragraf; satırın ayrıntısı soldaki tabloda zaten var). Özet artık üç tablodur; satırı `TakipUi.Liste` satırıdır (solda başlık ve gri
özet, sağda işaretli ve renkli kasa etkisi), sütun adları hareket tablosunun başlığı gibi gri:

- **Toplam:** ilk satır "34 satır kaydedilecek · Genel kasa etkisi" ve net etki; ardından satır formundaki tür sırasıyla her işlem türünün
  satır sayısı ve kasa etkisi ("Gelir · 5 satır +16.354,02", "Gider · 29 satır −77.359,37"). Kasa etkisi olmayan tür (kart harcaması,
  mevcut kayıtla eşleştirme) "kasa etkisi yok" der, tutarı gri 0,00. Tür adı kullanılır, çünkü karta ödeme gider değildir ama kasadan çıkar;
  türlerin toplamı genel kasa etkisidir.
- **Kanal:** kasa etkisinin kanallara dağılımı. Sunucunun satır başına kanal payları kasa etkisinin yönüyle (gider eksi) toplanır; kanala
  dağıtılmayan etki "Yalnız genel kasa" satırındadır (satır formundaki dağılım seçeneğinin adı). Sıra: yalnız genel kasa, kanallar kendi
  sırasıyla, listede olmayan pay ("Dağılım bekliyor", pasif kanal) sonda. Satırların toplamı genel kasa etkisidir. Kasa etkisi olmayan
  satırın kanal payı kart borcunundur, sayılmaz; hiç kasa etkisi yoksa tablo görünmez.
- **Uyarılar:** yalnız varsa. Aynı uyarı bir kez yazılır, başlığı satır numaralarıdır ("Satır 2, 3"; beşten fazlası "ve 4 satır daha");
  "Satıra git" ilk satıra götürür. Seçimin tümüne ait uyarı ("Tüm seçim") sondadır.
- Mali onay kutusu ("Satırları ve kasa etkisini kontrol ettim.") ve gerekirse benzer kayıt onayı yerinde kalır (§2, muhasebe kararı).
  Kayıt özeti açıkken ana düğme özetin "Kaydet"idir (alt çubuğun düğmesi ikincil).
- Eylemsiz `TakipUi.Liste` satırı geniş düzende de boş eylem sütunu bırakmaz: boş sütunun aralığı tutarı sütun başlığından 16 px içeride
  bitiriyordu (dar pencerede tam genişlik açılan özette görünür). Panelin içeriği değişince kaydırıcı başa döner: özetin altındaki
  uyarıdan "Satıra git" satır formunu ortasından açıyordu.
- Hareket tablosundaki "yapay zekâ" kaynak etiketi yalnız öneri varsayılandan farklıyken görünür: belgeden okunan türü değiştirdiyse ya da
  kanal seçtiyse. Belgenin türünü yalnız genel kasaya (ya da türün kendi dağılımına) yazan öneri satırların çoğudur (deneme belgesinde 53
  satırın 34'ü), etiket bilgi taşımaz; yapay zekânın çalıştığı pencerenin durum satırında bir kez yazar. Yapay zekânın düzelttiği açıklama
  fark sayılmaz: her satırda olur ve açıklama sütununda görünür. "hafıza", "kural" ve "ERP12" etiketleri değişmez.

## §10 Kasa kontrolü sayfası (PR 8)

- Kasalar yalnız özettir: genel kasa, kanal bakiyeleri, kart ve kredi takibi, çekler. Kanal bakiyeleri kartının altında "Kasa kontrolü"
  bağlantısı vardır. Bakiyesi açık alt limit uyarısının altındaki kanalın satırında kırmızı tek satır yazar ("Alt limitin altında: limit
  1.000,00 ₺"); eşik ana sayfa yanıtından gelir, bakiye ile uyarı çelişmez. Eşiksiz yanıtta (eski sunucu) işaret yoktur.
- "Kasa kontrolü" menüde "Özet" grubunda Kasalar'ın altındadır (`Bolum.KasaKontrolu`, rota `kasakontrolu`). Kasalar'ı gören iki rol de görür:
  geçmiş, alt limit uyarıları ve hareket dökümü sunucuda okuma yetkisiyle (Finans) gelir, Kasalar'daki eski bölüm de izleyiciye açıktı.
  Karşılaştırma, "Değişenleri göster", fark açıklaması ve Ayarlar bağlantısı yalnız editörde görünür (sunucu da yalnız editöre açar).
- Sayfa `TakipSayfasi`'dır: başlık `SayfaBasligi`, F5 ve Yenile sayfanın yenilemesi, son veri soluk kalır. Sayfa her göründüğünde
  yenilenir; uyarılar ve geçmişin güncel durumu başka ekranlardaki kayıtlarla değişir.
- Kartlar sırasıyla: "Alt limit uyarıları" (editörde "Alt limitleri düzenle" Ayarlar'ı açar; limitler Ayarlar'da kalır), "Karşılaştırma
  geçmişi" (uzun kural "Ayrıntılar"da, editörde "Yeni karşılaştırma"), istenince açılan "Gerçek bakiye karşılaştırması", istenince açılan
  "Bu kontrolden beri değişenler" ("Kapat" ile kapanır) ve "Kasa hareket dökümü" (başlangıç, bitiş ve kasa yan yana; sorgudan önce boş
  liste metni yok).
- Karşılaştırma formu: "Farkı göster" ikincil, "Karşılaştırmayı kaydet" sayfanın tek ana düğmesi. Kayıt formu kapatıp alanları temizler;
  "Vazgeç" de temizleyip kapatır. Tekrar anahtarı "Vazgeç"te silinmez: yanıtı belirsiz kalan kayıt aynı değerlerle yeniden gönderilirse
  sunucu ikinci kez işlemez. "Açıklamayı kaydet" ikincildir; iki form birlikte açık olsa da sayfada en çok bir yeşil düğme kalır.
- Ctrl+N "Yeni karşılaştırma" (yalnız editör), Ctrl+S yalnız form açıkken kaydeder. Açılan form, kayıt hatası ve başarı iletisi görünür
  yere kaydırılır.

## Korunacaklar

Palet ve kontrast eşikleri, koyu yeşil gruplu menü, Haftalık ve Aylık tabloları, ekstre penceresi, `BtnRowEdit` / `BtnRowDelete`, anlam
taşıyan amber "Kontrol edin" şeridi, hata sonrası son verinin soluk kalması, Türkçe sayı biçimi. Paleti değiştirmek hedef değildir: liste
bir rengi değil karar verilmemişliği hedefler. Windows kuralı (tek yazı tipi, Semibold, cümle biçimi) web tavsiyelerine üstündür.

## Doğrulama

Her PR'da: App.Core testleri, biçim ve MAUI lint, uygulama derlemesi ve deneme kopyasıyla (ayrı uygulama kimliği, yerel sunucu, örnek veri)
17 ekranın yeniden çekilip öncekiyle karşılaştırılması. App.Core testleri sırayla koşar (`TestParalelligi.cs`): görünüm testleri tek MAUI
uygulamasını ve stillerin paylaşılan değer nesnelerini kullanır, paralel koşu ara sıra "concurrent update" ile düşüyordu.
