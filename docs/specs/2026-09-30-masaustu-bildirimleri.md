# Masaüstü Windows bildirimleri — tasarım

Tarih: 30 Eylül 2026. Kapsam yalnız masaüstü (Kasa.App, Kasa.App.Core, Kasa.ApiClient istemci çağrıları). Sunucu, API sözleşmesi, veritabanı ve web değişmez.

Kullanıcının sorusu: "Bildirimler tarayıcıdan mı atılıyor, bilgisayarın kendisi atmıyor mu?" Bugün yanıt evet, bildirimler yalnız tarayıcıdan geliyor:

- Sunucu hatırlatmaları üretiyor ve web push ile tarayıcıya gönderiyor.
- Masaüstü uygulamasında yalnız ayar ekranı ve bildirim listesi var; Windows bildirimi göstermiyor.
- Uygulama kapalıyken masaüstünde hiçbir şey çalışmıyor.

Eski masaüstü, 2.1'e kadar kendi hesapladığı hatırlatmaları 09:00'da çalışan bir Windows göreviyle gösteriyordu. Bu yol 2.1'de "hatırlatmalar tek kaynaktan, sunucudan gelir" kararıyla kaldırıldı (`docs/specs/2026-07-15-kart-odeme-hatirlatici-design.md`, `docs/deploy/kasa-2.1.md`). Görev bugün `EskiHatirlatmaGorevi` ile siliniyor.

Bu tasarım o kararı korur. Masaüstü, sunucunun ürettiği bildirim listesini çekip Windows bildirimi olarak gösterir; kendi hesabını yapmaz. Böylece telefona gelenle aynı bildirim, aynı zamanda gelir.

## 1. Bildirimi alma ve gösterme

### Kaynak

- **Liste:** Masaüstü, sunucunun bugünkü bildirim listesini (`GET /api/bildirimler`, `IKasaApi.BildirimlerAsync`) okur.
- **Gösterilecekler:** Yalnız okunmamış bildirimler gösterilir; istemci okunmuşları atlar. İptal bilgisi istemciye gelmez (`BildirimDto`'da iptal alanı yok); sunucu iptal edilmiş bildirimi yalnız okunmuşsa ya da bir tarayıcıya gönderilmişse listeler, bu yüzden istemci iptali ayrıca denetleyemez (bkz. "Uygulamada verilen kararlar").
- **Rol:** Uç yalnız editöre açık. Bu yüzden masaüstü bildirimleri yalnız editör oturumunda çalışır.
- **Türler:** Sunucunun ürettiği her tür gösterilir: kart kesimi, son ödeme, kredi taksiti, kanal kasası alt sınırı ve sistem hata uyarısı.

### Ne zaman bakılır

- **Uygulama açıkken:**
  - Uygulama açılınca ve editör oturumu açılınca bir kez bakılır.
  - Sonra 5 dakikada bir bakılır.
  - Çıkış yapılınca, editör olmayan bir oturumda ya da ayar kapalıyken bakılmaz.
- **Uygulama kapalıyken:** Windows'un zamanlanmış görevi `EmarKasaBildirim` uygulamayı pencere açmadan `--bildirim-kontrol` argümanıyla çalıştırır. Görev iki anda tetiklenir:
  - her gün bildirim saatinden 5 dakika sonra (sunucudaki `BildirimAyarDto.Saat/Dakika` + 5 dakika);
  - kullanıcı Windows oturumu açınca.

  Görev kullanıcı düzeyinde kurulur (yönetici hakkı gerekmez). Kurulum ve güncelleme `schtasks` ile yapılır, pencere açılmaz (`CreateNoWindow`), hatalar yutulur; bu, `EskiHatirlatmaGorevi` ile aynı desen.
- **Pencere açmadan çalışma:**
  1. Kayıtlı oturum belirteciyle (`ITokenStore`) listeyi çeker.
  2. Yeni bildirimleri gösterir.
  3. Kapanır; en fazla 60 saniye çalışır.

  Aşağıdaki durumlarda sessizce çıkar ve hiçbir şey göstermez. Uygulama bir sonraki açılışında zaten bakar.
  - Belirteç yok, süresi dolmuş ya da rol editör değil.
  - Ayar kapalı.
  - Sunucuya ulaşılamıyor.
- **Eski görev:** `EmarKasaHatirlatici` görevini silen mevcut kod aynen kalır. Yeni görevin adı farklı olduğu için silinmez.

### Tekrar göstermeme

- Her bildirim bu bilgisayarda yalnız bir kez gösterilir.
- Gösterilen bildirim kimlikleri yerel bir dosyada tutulur: `%LOCALAPPDATA%\EmarKasa\gosterilen-bildirimler.json`.
  - Dosya en fazla son 500 kimliği tutar; eskiler atılır.
  - Hem uygulama hem pencere açmadan çalışan görev bu dosyayı kullanır.
  - Aynı anda yazmaya karşı dosya kilidi (`FileShare.None` ile yeniden deneme) kullanılır.
- Dosya bozuksa ya da okunamazsa yeni bir dosya ile başlanır. Bunun sonucu en fazla bir kez fazladan bildirim olur, kayıp olmaz.

### Gösterim

- **Araç:** Windows App SDK `AppNotificationManager`. MAUI ile örtük geliyor, yeni paket gerekmez. Uygulama paketsiz (`WindowsPackageType=None`), paketsiz uygulamada kayıt `AppNotificationManager.Default.Register()` ile yapılır.
- **İçerik:**
  - Başlık, sunucunun `Baslik` alanı.
  - Metin, sunucunun `Mesaj` alanı.
  - Tıklama argümanında bildirim kimliği ve hedef var: `Hedef` alanı `/#cards/{id}` ya da `/#loans/{id}`.
- **Tıklama:**
  - Uygulama açılır; kapalıysa bu tıklamayla başlar.
  - Hedef bir kartsa `//kartlar?KartId={id}`, krediyse `//krediler?KrediId={id}` açılır. Krediler sayfası bu parametreyi karşılamıyorsa tasarım bu parametreyi ekler. Tanınmayan hedefte `//bildirimler` açılır.
  - Bildirim sunucuda okundu işaretlenir (`BildirimOkunduAsync`).
- **Okundu kuralı:** Bildirimi yalnız göstermek okundu yapmaz. Bu yüzden telefonda ve listede görünmeye devam eder.

## 2. Bildirimler ekranı ve ayarlar

### "Bu bilgisayarda Windows bildirimleri" kartı

Bugünkü "Masaüstü ve telefon bildirimleri" kartındaki ana düğme web sitesinin bildirim kurulumunu açıyor. Bu kartın yerine gelir:

- **Açık/Kapalı anahtarı:**
  - Ayar bu bilgisayara özeldir ve editör için varsayılan olarak açıktır.
  - Açınca zamanlanmış görev kurulur ve uygulama açıkken bakma başlar.
  - Kapatınca görev silinir ve bakma durur.
  - Ayar `%LOCALAPPDATA%\EmarKasa\bildirim-ayari.json` dosyasında tutulur (MAUI `Preferences` değil; bkz. "Uygulamada verilen kararlar"). Pencere açmadan çalışan görev de aynı ayarı okur.
- **"Deneme bildirimi göster" düğmesi:** Sunucuya gitmeden hemen örnek bir Windows bildirimi gösterir.
- **Durum satırı:**
  - Son bakmanın zamanını ve sonucunu gösterir, örneğin "Son kontrol 14:05 · 2 yeni bildirim" ya da "Son kontrol 14:05 · sunucuya ulaşılamadı".
  - Windows ayarlarında bu uygulamanın bildirimleri kapatılmışsa (`AppNotificationManager.Default.Setting`) "Windows ayarlarında kapalı" uyarısı çıkar. Yanında Windows bildirim ayarlarını açan bir bağlantı olur (`ms-settings:notifications`).
- **Telefon:** Telefon için web kurulumu şimdilik küçük, ikincil bir bağlantı olarak kalır. Mobil uygulama gelince kaldırılacak.

### Diğer kartlar

- **Hatırlatma saati:** Aynen kalır; sunucudaki ortak ayar. Saat kaydedilince bu bilgisayardaki görevin saati de kendiliğinden güncellenir. Uygulama her açılışta sunucudaki saati okur ve görev farklıysa onu da günceller.
- **Hatırlatma listesi:** Aynen kalır.
- **Menü rozeti:** Menüdeki "Bildirimler" öğesinin yanında okunmamış bildirim sayısı küçük bir rozetle görünür. Rozet açıkken yapılan 5 dakikalık bakmayla ve "Okundu işaretle" ile güncellenir; sayı 0'sa görünmez. Rozet `MenuOgesi` üzerinde yeni bir özelliktir; menü düzeni değişmez.
- **İzin verilmiş cihazlar:** Aynen kalır. Altına şu not eklenir: "Aynı bilgisayarda Edge ya da Chrome da bildirime kayıtlıysa her bildirim iki kez gelir. Tarayıcı kaydını bu listeden kapatabilirsiniz."

### Oturum ve rol

- Çıkış yapılınca ya da oturum editör değilse bakma durur ve rozet gizlenir.
- Zamanlanmış görev ayar açıkken kalır. Çalıştığında editör oturumu bulamazsa hiçbir şey göstermez.

## Yapı

**Kasa.App.Core** (Windows'tan bağımsız, testli):

- **`BildirimYoklayici`:** Listeyi çeker, yeni ve okunmamış olanları seçer, gösterir, gösterilenleri kaydeder, okunmamış sayısını bildirir. Bağımlılıkları arayüz olarak alır:
  - `IKasaApi`;
  - `IBildirimGosterici` (Windows bildirimi);
  - `IGosterilenBildirimDeposu` (yerel dosya);
  - `IBildirimAyari` (açık/kapalı);
  - `TimeProvider`.
- **`BildirimGorevZamani`:** Sunucu saatinden görev saatini hesaplar (+5 dakika, gün sınırı dahil).
- **`BildirimHedefi`:** `Hedef` alanını uygulama rotasına çevirir.
- **`BildirimViewModel` ekleri:** anahtar, deneme bildirimi, durum satırı, saat kaydedilince görevin güncellenmesi.
- **`MenuOgesi` eki:** rozet sayısı.

**Kasa.App** (Windows'a özgü ince katman):

- **`WindowsBildirimGosterici`:** `AppNotificationManager` ile kayıt, gösterme ve tıklama etkinleştirmesi. Etkinleştirme, uygulama açıkken `NotificationInvoked` ile, kapalıyken `AppInstance.GetActivatedEventArgs` ile işlenir.
- **`BildirimGorevi`:** `schtasks` ile görevi kurar ve siler. `EskiHatirlatmaGorevi` desenini izler: süre sınırı, pencere yok, hatalar yutulur.
- **Uygulama açıkken bakma:** 5 dakikalık zamanlayıcı (`IDispatcherTimer` ya da `PeriodicTimer`).
- **Pencere açmadan çalışma:** `App.xaml.cs` içinde `--bildirim-kontrol` argümanında pencere açılmaz; kontrol çalıştırılır ve süreç kapanır. Bugün bu argümana benzer `--hatirlatma-kontrol` yalnız `Exit()` yapıyor.
- **Bildirimler sayfası ve menü:** yukarıdaki değişiklikler.

## Test

- **`BildirimYoklayici` (sahte API, sahte gösterici, sahte depo, sahte saat):**
  - yeni bildirim yalnız bir kez gösterilir;
  - okunmuş ve iptal edilmiş bildirimler atlanır;
  - ayar kapalıyken, oturum yokken ve rol editör değilken bakılmaz;
  - sunucu hatasında sessizce durulur ve durum satırına yazılır;
  - okunmamış sayısı doğru hesaplanır;
  - tıklama okundu işaretler ve doğru rotayı döndürür;
  - depo 500 kimlik sınırında eskileri atar.
- **`BildirimGorevZamani`:** 09:00 → 09:05, 23:58 → 00:03 (ertesi gün değil, aynı günlük tetikleyici).
- **`BildirimHedefi`:** kart, kredi, tanınmayan ve boş hedef.
- **`BildirimGorevi`:** komut argümanları sınanır; `EskiHatirlatmaGorevi` testleriyle aynı yöntem.
- **Görünüm modeli:** anahtar, deneme bildirimi, durum satırı, saat değişince görev güncellemesi, menü rozeti.
- **Gerçek Windows bildirimi:** Bildirimler her zaman kullanıcının ekranında çıkar; görünmeyen test masaüstünde denenemez. Bu yüzden son adımda, kullanıcının onayıyla ve onun ekranında denenir:
  - deneme bildirimi;
  - yerel test sunucusundan gelen bildirimin tıklanınca kartı açması;
  - kapalı uygulamada görevin çalışması.

  Görev denemesi bitince silinir. Kullanıcının oturum dosyası yedeklenip geri yüklenir.

## Kapsam dışı

- Sunucu değişiklikleri: yeni uç, istemci başına teslim kaydı.
- Tepside sürekli çalışan uygulama.
- Web ve mobil.
- Bildirim türlerini seçmeli yapmak; hepsi gelir.

## Uygulamada verilen kararlar

Uygulama sırasında tasarımdan ayrılan ya da tasarımın açık bıraktığı noktalar (plan: `docs/specs/2026-09-30-masaustu-bildirimleri-plan.md`, "Verilmiş teknik kararlar"; kullanıcı kararları ayrıca belirtildi):

- **Ayar dosyası:** "Bu bilgisayarda Windows bildirimleri" ayarı `%LOCALAPPDATA%\EmarKasa\bildirim-ayari.json` dosyasındadır, MAUI `Preferences` kullanılmaz. Paketsiz uygulamada `Preferences` dosyayı süreç başında bir kez okuyup bellekte tutar ve her yazışta kilitsiz baştan yazar; uygulama ile pencere açmadan çalışan görev aynı ayarı iki ayrı süreçte kullandığı için ayar küçük bir JSON dosyasına geçici dosyaya yazıp yerine taşıyarak kaydedilir.
- **Okunamayan ya da bozuk ayar dosyası kapalı sayılır** (kullanıcı kararı). Varsayılan "açık" yalnız dosya hiç yoksa geçerlidir; dosya ancak kullanıcı anahtarı değiştirince oluşur, bu yüzden bilinmeyen değer "açık" varsayılıp kapatılmış bildirim gösterilmez.
- **İptal bilgisi yok:** `BildirimDto`'da iptal alanı olmadığı için iptal edilmiş ama bir tarayıcıya gönderilmiş bildirim masaüstünde nadiren bir kez görünebilir. Sunucu değişikliği kapsam dışı olduğu için kabul edildi.
- **Yalnız bugünün okunmamışları Windows bildirimi olur:** tarih bilgisayarın yerel günüdür. Önceki günlerin okunmamışları Windows bildirimi olarak gelmez, listede ve rozette görünür (ilk kurulumda eski bildirimler bir kerede gösterilmez).
- **Ayar kapalıyken rozet gizlidir** (kullanıcı kararı). Bildirimler ekranı ayar kapalıyken açılsa da rozet görünmez; ayar açılınca yapılan bakma rozete sunucudaki okunmamış sayısını yazar.
- **Windows ayarı kapalıyken bildirim "gösterildi" sayılmaz:** Windows ayarlarında uygulamanın bildirimleri kapalıyken gösterim yapılamaz; bildirimler yerel kayda yazılmaz, ayar açılınca gösterilir.
- **Zamanlanmış görev XML ile tek görevdir:** `schtasks /Create /XML` ile kurulur; iki tetikleyici (günlük bildirim saati + 5 dakika ve oturum açılışı) tek görevdedir, görev bu kullanıcının hesabıyla `InteractiveToken` ve `LeastPrivilege` ile çalışır (yönetici hakkı istemez). XML dosyası UTF-16'dır ve geçici adla (`bildirim-gorevi-<guid>.xml`) yazılıp kurulumdan sonra silinir.
- **Kart ya da kredi sayfası açıkken tıklama seçimi hemen uygular:** Shell zaten görünen sayfaya gezinmede `OnAppearing` çağırmayabilir; sayfa görünürken gelen `KartId`/`KrediId` hemen uygulanır, görünmezken sayfa belirirken uygulanır. Başarılı yüklemeden sonra istenen kimlik, kayıt bulunamasa da temizlenir.
- **Tıklama sayfayı hemen açar:** okundu işareti arkada gönderilir; rozet yalnız işaret başarılı olunca düşer.
