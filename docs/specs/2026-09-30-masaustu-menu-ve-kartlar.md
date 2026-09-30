# Masaüstü: gruplu menü ve kredi kartı kutuları — tasarım

Tarih: 30 Eylül 2026 · Kapsam: yalnız masaüstü (Kasa.App, Kasa.App.Core). Web arayüzü değişmez; mobil uygulama ayrıca yapılacak.

Kullanıcı isteği: soldaki menü "çok fazla şey var, dağınık"; Kartlar ekranı "çok karmaşık", kart bilgileri kredi kartı biçiminde kutularda gösterilsin, kutuya tıklayınca kartın bilgileri ve ödemeleri girilsin. Aynı istekteki çekler, masaüstü bildirimleri ve genel UX turu ayrı tasarımlardır; bu belgenin kapsamında değildir.

Bu tasarım hesap mantığına, sunucuya, API'ye ve veritabanına dokunmaz. Değişen yalnız masaüstü görünümü ve görünüm modelindeki arayüz durumudur.

## 1. Menü

### Davranış

Menü dört gruba ayrılır. Öğelerin görünürlüğü bugünkü rol kuralıyla aynıdır (`SekmeModeli.Bolumler`, `Kasa.App.Core/Rol.cs`). Görünür öğesi kalmayan grubun başlığı da gizlenir.

| Grup | Öğeler (sırayla) |
|---|---|
| Özet | Kasalar, Haftalık, Aylık |
| Kayıtlar | İşlemler, Alışlar, Aylık giderler, Ekstre içe aktar |
| Kart ve kredi | Kartlar, Krediler |
| Diğer | Bildirimler, Rapor dışa aktar, Ayarlar |

Grupların altında, ayrı durur biçimde "Çıkış" öğesi yer alır.

Rollere göre görünen menü:

- **Alıcı:** yalnız "Kayıtlar › Alışlar" ve Çıkış görünür.
- **İzleyici:** Özet, Kayıtlar (İşlemler, Aylık giderler), Kart ve kredi, Diğer (Rapor dışa aktar) görünür.
- **Editör:** bütün öğeler görünür.

Her öğenin solunda bir simge bulunur. Seçili öğe bugünkü `SidebarActive` zemini ve açık yeşil noktayla, fareyle üzerine gelinen öğe `SidebarHover` zeminiyle gösterilir. Menü üst bilgisi (logo, uygulama adı) değişmez.

### Yapı

Menü, MAUI'nin öğe şablonu (`Shell.ItemTemplate`) yerine kendi bileşenimizle çizilir: `Shell.FlyoutContent` içinde gruplu bir liste.

- Sayfa geçişi yine Shell üzerinden yapılır (`GoToAsync("//rota")`). Rotalar, `FlyoutItem` tanımları, geri tuşu ve sayfa yaşam döngüsü aynı kalır. `FlyoutItem`'lar menüde görünmez, yalnız rota kaynağıdır.
- Menü içeriği (grup, sıra, başlık, simge, rota, bölüm) tek bir görünüm modelinden gelir: `Kasa.App.Core`'da yeni `MenuModeli`. Bu model `SekmeModeli.Bolumler(rol)` ile süzülür ve seçili rotayı tutar. Böylece menünün içeriği ve rol kuralı Windows'tan bağımsız olarak testle sabitlenir.
- `AppShell.xaml.cs`'teki bölüm → `FlyoutItem` sözlüğü rota eşlemesine döner. Menü görünürlüğü artık `FlyoutItem.IsVisible` ile değil `MenuModeli` ile yönetilir. Doğrudan rotayla gidilen, yetkisi olmayan bir sayfaya erişimi bugün hangi kural engelliyorsa aynı kural korunur.
- Seçili ve üzerine gelinen öğenin zemini `BackgroundColor` ile yazılır (dotnet/maui#38813 dersi). `MauiKayitTutarliligiTests.Kabuk_menu_sablonlari_Background_firca_ozelligini_yazmaz` testi, yeni menü şablonunu da kapsayacak biçimde güncellenir.
- Simgeler, uygulamada zaten bulunan yazı tipi ya da görsel kaynaklarından seçilir. Yeni bir simge paketi gerekirse sürümü ve lisansı internetten doğrulanır.

### Test

- `MenuModeli`:
  - her rol için görünen gruplar ve öğeler (yukarıdaki üç rol listesi);
  - boş grubun gizlenmesi;
  - seçili öğenin rotaya göre belirlenmesi;
  - girişten dönüşte menünün boşalması.
- Bugünkü rol → menü testleri yeni modelde korunur.
- XAML taraması: menü şablonunda `Background` fırçası yazılmaz. Tanımsız kaynak anahtarı kalmaz (`maui-lint`).

## 2. Kartlar

### Kart kutuları

Kartlar yan yana dizilen kutular olarak gösterilir. Satırdaki kutu sayısı pencere genişliğine göre değişir; kutu genişliği en az 220 px'tir. Listenin sonunda kesik çizgili "Yeni kart ekle" kutusu bulunur; bu kutu yalnız editörde görünür.

Kutunun içeriği:

- banka ya da kart adı (`KartTakipDto.Ad`);
- "Kart borcu" etiketi ve borç tutarı büyük yazıyla (`Borc`);
- limit doluluk çubuğu: borç ÷ limit, 0 ile 1 arasına sıkıştırılır. Limit 0 ise çubuk gösterilmez;
- alt satırda "Limit ₺…" ve ilk açık ekstrenin son ödeme tarihi. Açık ekstre yoksa tarih yazılmaz;
- durum etiketleri, en fazla iki tanesi, önem sırasıyla:
  - "Son ödeme geçti": kalan borcu olan ekstrenin son ödeme tarihi bugünden önce;
  - "Geçiş farkını doğrulayın": `Gecis.TahminiKasaFarki != 0`;
  - "Eski takip": `YeniTakip == false`;
  - "Pasif": `Aktif == false`.

### Renk

Kutu rengi, kart adında geçen bankaya göre otomatik seçilir. Veritabanında renk alanı yoktur.

- Eşleme, `Kasa.App.Core`'da saf bir işlevle yapılır (`KartRengi.Sec(ad)`). Büyük/küçük harf ve Türkçe karakter farkı gözetilmez ("İş", "is", "ISBANK" aynı sayılır).
- İlk eşleme listesi:

  | Banka | Renk |
  |---|---|
  | Garanti | yeşil |
  | Akbank | kırmızı |
  | İş Bankası | mavi |
  | QNB / Finansbank | mor |
  | VakıfBank | sarı |
  | Yapı Kredi | lacivert |
  | Ziraat | kırmızı |
  | Halkbank | mavi |
  | DenizBank | mavi |
  | Enpara | mor |
  | TEB | yeşil |

- Tanınmayan banka, kart kimliğine göre sabit bir palet rengi alır. Böylece aynı kart her açılışta aynı rengi alır.
- Her renk `Colors.xaml`'da açık zemin, kenar ve koyu yazı anahtarlarıyla tanımlanır. Yazı ile zemin arasında en az 4,5:1 kontrast testle sabitlenir.

### Tıklayınca: ayrıntı kutunun altında açılır

Bir kutuya tıklanınca kutu vurgulanır ve kartın ayrıntısı, kutunun bulunduğu satırın hemen altında tam genişlikte açılır. Aynı kutuya tekrar tıklanınca ayrıntı kapanır; başka bir kutuya tıklanınca ayrıntı o karta geçer. Aynı anda yalnız bir kart açıktır.

Ayrıntının içeriği, yukarıdan aşağıya:

1. **Özet:** kart borcu, açık ekstre borcu, limit, kanal kart borcu payları (bugünkü "Kanalların kalan kart borcu" metni ve açıklaması) ve varsa geçiş uyarısı (tahmini kasa farkı varsa koyu kırmızı).
2. **Düğmeler** (yalnız editörde):
   - yeni takipteki kartta: Ödeme kaydet · Harcama ekle · Faiz / masraf · Kartı düzenle;
   - eski takipteki kartta: Yeni takibe al · Kartı düzenle.
3. **Form alanı:** Düğmeye basınca ilgili form düğmelerin hemen altında, sayfanın içinde açılır.
   - Aynı anda tek form açıktır: başka bir düğmeye basmak açık formu kapatıp yenisini açar.
   - Form başarılı kayıttan sonra ya da "Vazgeç" düğmesiyle kapanır.
   - Hata olursa form açık kalır ve hata metni formun içinde gösterilir.
   - Formların alanları, önizleme adımları, benzer kayıt uyarıları, onay kutuları ve iletileri bugünkü sayfadakiyle aynıdır; yalnız yerleri değişir.
4. **Sekmeler:** Ekstreler · Harcamalar · Ödemeler. Varsayılan sekme Ekstreler'dir.
   - Ekstreye tıklamak (editör, yeni takip) "Ekstre bilgisi" formunu form alanında açar.
   - Harcama ve ödeme satırlarındaki iptal ve ekstre aktarımına gitme davranışları bugünkü gibidir (gerekçe penceresi, `//ekstreaktar?KayitId=`).

"Yeni kart ekle" kutusu, kart bilgileri formunu (açılış tarihi, açılış borcu ve payları dahil) kutuların altında aynı form düzeniyle açar. Kayıttan sonra yeni kart açık olarak gelir.

İzleyici rolünde düğmeler, form alanı ve "Yeni kart ekle" kutusu görünmez. Özet ve sekmeler görünür.

Başka bir ekrandan belirli bir karta gidildiğinde (`//kartlar?KartId=…`, ör. ana sayfadaki takip kartından) o kart açık olarak gelir. Bu, bugünkü `IdIleSec` davranışının karşılığıdır.

### Yapı

- `KartTakipPage` yeniden düzenlenir. Kodla kurulan bugünkü tek uzun sayfa yerine şu yapı kurulur:
  - kutular için yeniden kullanılabilir bir `KartKutusu` bileşeni;
  - kutuları dizen ve seçili kutunun satırından sonra ayrıntı alanını yerleştiren bir yerleşim bileşeni.
  
  Mevcut ortak yapı taşları (`TakipUi`, `DurumSeridi`, stil anahtarları) kullanılır.
- `KartTakipViewModel`'in hesap, doğrulama ve sunucu mantığı değişmez. Eklenecekler:
  - açık form (`KartFormu`: Yok, Odeme, Harcama, Masraf, Ekstre, KartBilgisi, Gecis);
  - seçili sekme;
  - "Vazgeç" komutu;
  - kayıt başarılı olunca formu kapatma.
  
  Bugünkü `Secili` ve `YeniKart` durumu açık kartı ve yeni kart formunu belirlemeye devam eder.
- Kutu içeriği (etiketler, çubuk oranı, son ödeme metni, renk) `KartTakipSatiri`'na eklenen hesaplanmış özelliklerden gelir. Böylece testle sabitlenebilir.

### Test

- `KartTakipSatiri`:
  - doluluk oranı: limit 0, borç limitten büyük, negatif borç;
  - son ödeme metni: açık ekstre var ya da yok;
  - durum etiketleri ve sırası;
  - "Son ödeme geçti" kuralı `TimeProvider` ile sabit tarihte.
- `KartRengi.Sec`: listedeki her banka, Türkçe karakter ve büyük/küçük harf farkları, tanınmayan bankada kimliğe göre sabit renk.
- Kontrast: her renk çifti en az 4,5:1.
- `KartTakipViewModel`:
  - aynı anda tek form;
  - başka düğme açık formu değiştirir;
  - başarılı kayıttan sonra form kapanır;
  - hata olursa form açık kalır;
  - kart değişince form kapanır;
  - izleyici rolünde form açılamaz;
  - `IdIleSec` ile gelen kart açık olur.
- Mevcut `KartTakipViewModel` ve ödeme, harcama, masraf, geçiş testleri değişmeden geçer.
- Gerçek pencerede ekran görüntüleri:
  - kutular (2–3 kart, uyarı etiketli biri);
  - açık kart;
  - açık ödeme formu;
  - izleyici görünümü.
  
  Ekran görüntüleri görünmeyen ayrı bir Windows masaüstünde, "TEST" başlıklı ve odak almayan pencereyle, yerel test sunucusuna karşı alınır. Kullanıcının oturum dosyası yedeklenip geri yüklenir.

## Kapsam dışı

- Krediler sayfasının aynı biçime geçirilmesi: sonra ayrı iş.
- Çekler, masaüstü bildirimleri, genel UX turu: ayrı tasarımlar.
- Web arayüzü.
- Kart numarası, son kullanma tarihi gibi yeni kart bilgileri (veritabanında yok, eklenmiyor).

## Teslim

Tek dal, iki mantıksal adım; her biri kendi commit'leriyle:

1. Menü.
2. Kartlar.

Doğrulama:

- MAUI Release derlemesi 0 uyarı;
- App.Core ve Sözleşme testleri;
- `maui-lint` (onaltılık renk ve uzun satır tabanı artmaz);
- `dotnet format`;
- ekran görüntüleri PR açıklamasında.
