# Masaüstü: form hataları ve bağlantı kopması — uygulama planı

> **Ajanlar için:** GEREKLİ ALT BECERİ: görev görev uygulanır (önerilen superpowers:subagent-driven-development; tek oturumda
> superpowers:executing-plans). Adımlar `- [ ]` onay kutularıyla izlenir. Tasarım (bağlayıcı):
> `docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md`.

**Amaç:** Masaüstünde form hatalarını alanın altında ve formun içinde göstermek, düzenleme modunu ve kaydedilmemiş değişikliği
belli etmek, kart ödemesini tek kontrol düğmesine indirmek ve sunucuya ulaşılamayınca tek bir kabuk şeridiyle son başarılı veriyi
koruyarak çalışmak (UX denetimi K-02, İŞ-02, İŞ-03, AL-01, ÇK-01, KR-01, KR-04, AG-02, HD-01..03).

**Mimari:** İstemci (`Kasa.ApiClient`) sunucunun doğrulama sözlüğünü `KasaApiException.AlanHatalari`'na taşır ve tek gönderim
noktasında ulaşılabilirliği bildirir (`IBaglantiBildirimleri`). Görünüm modeli katmanı (`Kasa.App.Core`) ortak yapıları kurar:
`AlanHatalari` (alan → ileti, genel hata, sıra, dizinleyici bildirimi), `TemelViewModel.FormIsleAsync` (tekil işlem + ön doğrulama +
sunucu eşlemesi + hataya kaydırma isteği), `KaydedilmemisDegisiklik` (açılış değerleriyle karşılaştırma) ve `IKaydedilmemisForm`,
`BaglantiDurumu` (tekil; `AuthViewModel.Baglanti`), son veriyi koruma (`OturumluViewModel.VeriYukleAsync`, `VeriEski`,
`GovdeGorunur`, `SonGuncellemeMetni`; `RaporViewModel` aynı kural). Arayüz (`Kasa.App`) `FormAlani` denetimi, `GorunurYapici`
(eski `KartTakipPage.GorunurYap`), kabuk `TitleView`'ındaki `BaglantiSeridi` ve `AppShell.OnNavigating` erteleme onayıyla bunları
gösterir. Formlar tek tek bu yapılara taşınır; sunucu değişmez.

**Teknoloji:** .NET 10, MAUI 10.0.110 (Windows, paketsiz), CommunityToolkit.Mvvm 8.4.2, xUnit v3 (`xunit.v3.mtp-off` 4.0.1).

**Kanıt:** Bu plandaki bütün kod `c0fb1b2`'nin (taban `origin/release/2.x` 9c47c1c + tasarım) ayrı bir kopyasında görev görev
uygulandı; her görevin commit'inde `Kasa.ApiClient.Tests` ve `Kasa.App.Core.Tests` yeşildi (sayılar görevlerde), Windows uygulaması
her arayüz görevinden sonra `0 Uyarı, 0 Hata` derlendi, `maui-lint` taban içinde kaldı ve her commit
`dotnet format whitespace … --verify-no-changes` denetiminden geçti. Kopya yol uzunluğu (MAX_PATH, 260) yüzünden
Windows derlemesi kısa yoldan (`subst`) yapıldı; gerçek çalışma ağacının yolu kısa olduğu için bu gerekmez. Test sayıları bu
denemeden alınmıştır. Ekran denemesi (Görev 23)
kopyada **çalıştırılmadı**: kabuk şeridinin `TitleView`'daki gerçek görünümü, `OnNavigating` onayı ve odak/kaydırma ilk kez orada
görülecek (kodları derleme, MAUI tutarlılık ve görünüm ağacı testleriyle sınandı).

---

## Doğrulanan varsayımlar (kanıt: dosya:satır, taban c0fb1b2)

1. **Sunucu doğrulama yanıtı ve alan adları.**
   - Biçim: `GirdiDogrulama.Kontrol` alanı tek iletiyle yazar, aynı alanın sonraki ihlali öncekini ezer (`Kasa.Api/GirdiDogrulama.cs:15-19`,
     `_hatalar[alan] = [mesaj]`); yanıt `Results.ValidationProblem(_hatalar)`dır (`:154`): `{ "title": …, "status": 400,
     "errors": { "<alan>": ["<ileti>"] } }`. Alan adı kodda yazıldığı gibidir (camelCase); testler de öyle okur
     (`Kasa.Api.Tests/GirdiDogrulamaTests.cs:285`, `KartKrediTakipTests.cs:218` `errors.tutarTl`).
   - **İşlemler** (gider; `KayitGirdileri.Islem`, `Kasa.Api/KayitGirdileri.cs:41-77`): `tarih` (:44, :50, :58), `cari` (:51), `not` (:52),
     `tutarTl` (:53), `tip` (:54), `kanal` (`GirdiDogrulama.cs:97`), `krediKartiId` (`GirdiDogrulama.cs:103,105`; `KayitGirdileri.cs:160`),
     `taksitSayisi` (:63, :125-131), `ilkKesimTarihi` (`TaksitKurali` :136-140); yinelemede `istekId` (`FinansHesaplari.cs:19`).
   - **Alışlar** (`AlisEndpoints.Validate`, `Kasa.Api/AlisEndpoints.cs:305-344`): `tarih` (:308), `tedarikci` (:309), `not` (:310),
     `tedarikciId`, `vade`, `kalemler`, `kalemler[i]`, `kalemler[i].aciklama`, `kalemler[i].tutar`, `kalemler[i].dagilimlar`,
     `kalemler[i].dagilimlar[j].kanalId`, `kalemler[i].dagilimlar[j].tutar`, `toplam`. Onay/iade: `not`, `kalemler`, `dagilimlar` (:101-104, :126-127).
   - **Ayarlar kanal formu** (`Kasa.Api/KanalEndpoints.cs:16-19`, `:44-47`): `ad`, `acilisDevri`; ad çakışması `{ hata }` (409, :24, :52).
   - **Alan adı olmayan uçlar:** kart, kredi ve çek `FinansTakipEndpoints.Require` → `{ hata }` (`Kasa.Api/FinansTakipEndpoints.cs:555-557`;
     çek kuralları `Kasa.Api/CekEndpoints.cs:121-150`, `CekMetni` `:222-230`); aylık gider `Need` → `{ hata }`
     (`Kasa.Api/AylikGiderEndpoints.cs:260`, `:267`, `:273`). Bu formlarda alan hatası yalnız istemcinin ön doğrulamasından gelir, sunucu
     iletisi formun genel hatasına gider (tasarımda yazılı değil; "Uygulama notları"na eklendi).
   - İstemci bugün alan adlarını atıp iletileri birleştirir: `Kasa.ApiClient/KasaApiClient.cs:258-277` (`Ileti`).
2. **Ağ hatası istemcide nerede yakalanıyor.** Bütün istekler `YanitAlAsync`'ten geçer (`KasaApiClient.cs:197-218`); `_http.SendAsync`
   (`:206`) `HttpRequestException`'ı sarmalamadan fırlatır. Süre sınırı `SureliAsync`'te (`:184-194`) `OperationCanceledException` →
   `TimeoutException`'a çevrilir (`:192`); çağıranın iptali iptal kalır. `KasaApiException` yalnız başarısız durum koduyla oluşur
   (`:207-216`). Uzun indirme de aynı yoldan geçer (`KasaApiClient.Yonetim.cs:70-73`). Görünüm modelleri iletiyi
   `Yurutucu.HataMesaji`/`OkumaHataMesaji` ile seçer (`Kasa.App.Core/Yurutucu.cs:107-124`: `HttpRequestException` → "Sunucuya
   ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.", `TimeoutException` → kayıtta "… tamamlanmış olabilir …"). Gönderim
   noktası olay bildirimi için yeterlidir; `DelegatingHandler` gerekmez (süre sınırı handler'ın dışındadır).
3. **Hata anında veri nerede siliniyor.**
   - `RaporViewModel.RaporYukleAsync` her yüklemenin başında `VeriVar = false` yazar (`Kasa.App.Core/RaporViewModel.cs:38`); Kasalar,
     Haftalık ve Aylık kartları `IsVisible="{Binding VeriVar}"` (`Kasa.App/Views/PanelPage.xaml:34,49`, `HaftalikPage.xaml:30`,
     `AylikPage.xaml:39`). Ayrıca `PanelViewModel.cs:57` (`TakipsizUyari = ""`), `HaftalikViewModel.cs:19` (`VeriSagligiUyarisi = null`),
     `AylikViewModel.cs:61` (`Rapor = null`). Kasalar alt bölümleri `VeriVar` false olunca gizlenir (`PanelPage.xaml.cs:19-35`).
   - `TakipSayfasi` gövdesi `VeriHazir`'a bağlıdır (`Kasa.App/Views/TakipUi.cs:373`); yükleme başında `VeriHazir = false` yazanlar:
     `AylikGiderViewModel.cs:52`, `CekTakipViewModel.cs:174`, `BildirimViewModel.cs:58`, `KasaKontrolViewModel.cs:51,381`. Kartlar ve
     Krediler listeyi silmez ama ilk yükleme hata verince gövde hiç görünmez (denetim görüntüsü `30-hata-kartlar-sunucu-yok.png`).
   - İşlemler: liste yüklemesi başında `VeriVar = false` (`IslemlerViewModel.cs:203`), hatada `ListeyiBosalt()` (`:233`); tip çipleri yalnız
     başarılı kaynak yüklemesinde doldurulur (`:134-136`) — HD-02.
   - Alışlar: `VeriHazir = false` ve `Alislar.Clear()` istekten önce (`AlislarViewModel.cs:175-177`); ızgara `VeriHazir`'a bağlı
     (`AlislarPage.xaml:35`).
   - Takip sayfalarının son güncelleme satırı `SonGuncelleme` + `stringFormat` ile bağlıdır, boşken boş kalır (`TakipUi.cs:281`) — HD-03.
4. **Kaydırma yardımcısının bugünkü hâli.** Yalnız Kartlar'da, sayfaya özel: `KartTakipPage.cs:145` (`KaydirmaKarari` temsilcisi),
   `:158-184` (`GorunurYap`: SizeChanged ya da 100 ms yoklama, en çok 1 sn, son istek kazanır), `:186-200` (`Kaydir`), `:204-215`
   (`KaydiriciyaGoreY`); karar `Kasa.App.Core/KaydirmaHesabi.cs` (`FormKaydirmasi`, `BasaKaydirilmali`). Odaklanma yok.
5. **Alışlar'daki kaydedilmemiş değişiklik uyarısı.** Bayraktır, karşılaştırma değildir: `[ObservableProperty] _kaydedilmemisDegisiklikVar`
   (`AlislarViewModel.cs:75`), alan değişince `KirliYap` (`:526`) doğru yapar; değer geri alınsa da kalkmaz. `Sec` (`:224-232`), `Yeni`
   (`:234-241`) ve `YukleAsync` (`:173-174`) kirliyken çalışmaz, `KaydetmeUyarisi` (`:271`) sayfa başına "Kaydedilmemiş değişiklikler var.
   Önce kaydedin veya ‘Değişiklikleri bırak’…" yazar; "Değişiklikleri bırak" sayfada onay sorar (`AlislarPage.xaml.cs:48-52`).
6. **Shell'de sayfadan çıkış onayı MAUI 10'da yapılabilir.** Microsoft Learn ".NET MAUI Shell navigation" (net-maui-10.0), "Navigation
   deferral": `Shell` alt sınıfında `OnNavigating` geçersiz kılınır, `args.GetDeferral()` ile `ShellNavigatingDeferral` alınır, kullanıcı
   reddederse `args.Cancel()`, sonunda `token.Complete()` çağrılır; erteleme bekliyorken `GoToAsync` `InvalidOperationException` atar
   (<https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/navigation?view=net-maui-10.0#navigation-deferral>). Bu uygulamada
   menü `GoToAsync("//rota")` ile gider (`AppShell.xaml.cs:147-152`) ve istisnayı yakalar; seçili menü öğesi yalnız `OnNavigated`'da
   değişir (`:68-72`), iptal edilen gezinme menüyü bozmaz. Tasarımın "sayfadan çıkılırsa" kısmı uygulanır; ürün sahibine soru yok.
7. **Tek kabuk şeridinin yeri.** `Shell.TitleView` kabuğun kendisine konursa her sayfada geçerlidir: MAUI `ShellToolbar`
   `GetEffectiveValue(Shell.TitleViewProperty, Shell.GetTitleView(_shell))` ile sayfadan kabuğa yürür
   (<https://github.com/dotnet/maui/blob/main/src/Controls/src/Core/ShellToolbar.cs>); Windows `MauiToolbar`'da başlık metni ve
   TitleView ayrı sütundadır ve birbirini gizlemez (<https://github.com/dotnet/maui/blob/main/src/Core/src/Platform/Windows/MauiToolbar.xaml.cs>).
   Giriş sayfası gezinme çubuğunu gizler (`LoginPage.xaml:12`), şerit orada görünmez. Görünüşü Görev 23'te ekran görüntüsüyle doğrulanır.
8. **Dizinli bağlama.** `Hatalar[DuzenCari]` bağlaması MAUI'nin bağlama motorunda "Item[DuzenCari]" ve "Item" bildirimiyle yenilenir;
   Görev 8'deki `FormAlaniTests` bunu gerçek `Microsoft.Maui.Controls` ile sınar. `MauiKayitTutarliligiTests` XAML bağlama denetimi
   dizinli yolu bugün reddeder (`MauiKayitTutarliligiTests.Baglamalar.cs` `Coz`: "dizinli yol desteklenmiyor"); Görev 12 string
   dizinleyiciyi ve `AlanHatalari` anahtarının görünüm modelinde özellik olmasını denetleyecek şekilde genişletir.

## Ürün sahibine sorulacak

1. **nginx'in 502/504'ü.** Tasarım "4xx ve 5xx yanıtları bağlantı hatası sayılmaz" der; plan buna uyar. Canlıda API kapsayıcısı durunca
   nginx 502 Bad Gateway döner: istemci yanıt aldığı için şerit **çıkmaz**, her sayfa "Sunucu işlemi tamamlayamadı. Lütfen yeniden
   deneyin." gösterir. Yerelde (doğrudan bağlantı) şerit çıkar. Örnek: sunucu güncellenirken kullanıcı Kasalar'ı yeniler → şeritsiz kırmızı
   sunucu hatası, son bakiye soluk. İstenirse 502/503/504 "kopuk" sayılabilir (tek satırlık değişiklik: `KasaApiClient.YanitAlAsync`);
   plan bunu yapmaz.

Engelleyici değildir; plan tasarımın sözüyle uygulanır.

---

## Çalışma kuralları (her görevde geçerli)

- **Yer:** çalışma ağacı `C:/Users/burak/source/repos/Kasa-paket/ux-form-baglanti`, dal `ozellik/ux-form-baglanti` (taban
  `origin/release/2.x` 9c47c1c + tasarım c0fb1b2 + bu plan). Her komut bu klasörde çalışır. **Push yok.**
- **dotnet:** aynı anda tek `dotnet` komutu; derleme ve test komutlarına `-c Release -m:2 -nodeReuse:false` eklenir.
  - `dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj -c Release -m:2 -nodeReuse:false`
  - `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false`
  - Windows: `dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false`
  - Çıktı Türkçedir: başarı `Başarılı!  - Başarısız:     0, Başarılı:  N`, başarısızlık `Başarısız! - Başarısız: …`; derleme `0 Uyarı`, `0 Hata`.
  - Başlangıç test sayıları (c0fb1b2): Kasa.ApiClient.Tests 182, Kasa.App.Core.Tests 1085.
- **xUnit1051:** `CancellationToken` alan yönteme test metodunda `TestContext.Current.CancellationToken` verilir. Bu planın testleri
  belirteç alan yöntemi yalnız `BaglantiBildirimleriTests.Cagiranin_iptali_bildirilmez`'de çağırır ve orada kendi belirtecini verir.
- **Linux CI:** App.Core testleri Windows'a bağlı değildir; `Kasa.App/Controls/*.cs` ve `Kasa.App/Views/TakipUi.cs` test projesinde
  gerçek MAUI denetimleriyle derlenir (`Kasa.App.Core.Tests.csproj`), bu dosyalar MAUI örtük using'lerine dayanmaz (`using Microsoft.Maui;`
  `using Microsoft.Maui.Controls;` yazılır). Saat sabittir (`IslemEditorTests.SabitZaman`), tarih biçimi saat dilimi farkı taşıyan
  `DateTimeOffset` ile sınanır.
- **Kaynak anahtarları** `Application.Current!.Resources["X"]` biçiminde yazılır (maui-lint (a) denetler); renk yalnız `Colors.xaml`'dan;
  Kasa.App'te satır en çok 200 karakter; `maui-lint` tabanı artmaz.
- **Bul / Yerine:** değişen dosyalarda "Bul" metni dosyada bir kez geçer (planı üreten betik denetledi); "Yerine" ile değiştirilir.
  Satır sonları dosyanın kendi satır sonudur (Git `text=auto`). Yeni dosyalar tam içerikle verilir.
- **Biçim:** `.editorconfig` `csharp_preserve_single_line_statements = false` der ve Roslyn varsayılanı nesne/sözlük başlatıcısında
  her üyeyi ayrı satıra ister; plandaki kod buna uyar (CI'daki `dotnet format whitespace … --verify-no-changes` işi bunu denetler).
  Kendi eklediğiniz satırlarda şüphe varsa commit'ten önce
  `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes` çalıştırın; fark gösterirse aynı komutu
  `--verify-no-changes` olmadan iki kez çalıştırıp farkı göreve katın.
- **Commit:** Türkçe konu, boş satır, `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`:

  ```bash
  git commit -F - <<'MESAJ'
  feat(app-core): form alan hataları yapısı

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  MESAJ
  ```

---

## Verilmiş teknik kararlar

1. **Alan adı** görünüm modelindeki özelliğin adıdır (`nameof(DuzenCari)`); sunucu alanı küçük harfe indirilip formun eşlem sözlüğüyle
   çevrilir (`["cari"] = nameof(DuzenCari)`). Eşlenmeyen iletiler birleşip genel hataya gider; alan sözlüğü olmayan ret
   (`{ hata }`, 409…) `Yurutucu.HataMesaji` ile genel hataya.
2. **Temizleme kendiliğinden:** `TemelViewModel.OnPropertyChanged` her `Formlar` üyesinde değişen özelliğin hatasını kaldırır;
   `FormIsleAsync` kaydın başında bütün hataları kaldırır; Yeni, başka kayıt, Vazgeç ve başarı formu açan yardımcılar `Temizle()` çağırır.
3. **"Kaydedilmemiş" ölçütü** `KaydedilmemisDegisiklik`: açılıştaki değerlerin JSON'u ile şimdikinin karşılaştırılması. Onay başka kayda
   geçişte, "Yeni"de ve sayfadan çıkışta sorulur; **"Vazgeç" sormaz** (bilerek bırakmak), Kartlar'da aynı kartın formları arasında geçiş
   sormaz. Onay sayfanın `DisplayAlertAsync`'idir (`OturumluViewModel.BirakmaOnayi`); bağlanmamışsa (testler) form bırakılmaz.
4. **Bağlantı:** `HttpRequestException` ve süre sınırı kopuk; alınan her yanıt bağlı (4xx/5xx dahil). Kopukken *okuma* hatasının
   bağlantı iletisi sayfaya yazılmaz (`Yurutucu.OkumaHatasiniYaz`); *kayıt* hatası her zaman formun genel hatasına yazılır:
   `HttpRequestException` → "Sunucuya ulaşılamadı. Kayıt yapılmadı; bağlantı gelince yeniden kaydedin.", zaman aşımı → mevcut
   "… tamamlanmış olabilir …" (kayıt sunucuda yapılmış olabilir).
5. **Kayıttan sonraki yenileme** (Aylık giderler ayı, Ayarlar kanal listesi) hata verirse bu formun hatası değildir; sayfanın okuma
   hatası olarak yazılır (yoksa "Kayıt yapılmadı" yanlış olurdu).
6. **Son veri yalnız aynı sorgunun verisidir:** Aylık'ta başka aya, İşlemler'de başka süzgece geçince eski veri gösterilmez.
7. **Kasalar alt bölümleri** `VeriVar` değişimini değil `RaporViewModel.Yuklendi` olayını dinler (VeriVar artık hatada inmez).
8. **Takip formlarının yeri:** Kartlar, Krediler, Çekler, Aylık giderler takip sayfalarıdır (`TakipSayfasi`); form hataları `TakipUi.Alan(ad,
   girdi, hatalarYolu, alan)` ve `TakipUi.FormHatasi(yol)` ile; kaydırma `TakipSayfasi.Gorunur` (`GorunurYapici`). XAML formları
   (İşlemler, Alışlar, Ayarlar) `ctl:FormAlani` kullanır.
9. **Kapsam sınırı:** İşlemler'in gelir formu, Alışlar'ın ödeme/iade/alıcı formları, Kartlar'ın harcama/masraf/ekstre/geçiş formları ve
   Krediler'in taksit/kapama/geçiş formları sayfa hatasıyla kalır (tasarım §1 kapsamı: İşlemler gider formu, Alışlar formu, Çekler iki
   form, Kartlar yeni kart/düzeltme ve ödeme, Krediler yeni kredi, Aylık giderler şablon ve ödeme, Ayarlar kanal formu).

---

## Dosya yapısı

| Dosya | Durum | Görev |
|---|---|---|
| `Kasa.ApiClient.Tests/AlanHatalariTests.cs` | Yeni | 1 |
| `Kasa.ApiClient/KasaApiClient.cs` | Değişir | 1, 2 |
| `Kasa.ApiClient/KasaApiException.cs` | Değişir | 1 |
| `Kasa.ApiClient.Tests/BaglantiBildirimleriTests.cs` | Yeni | 2 |
| `Kasa.ApiClient/IBaglantiBildirimleri.cs` | Yeni | 2 |
| `Kasa.App.Core.Tests/AlanHatalariTests.cs` | Yeni | 3 |
| `Kasa.App.Core/AlanHatalari.cs` | Yeni | 3 |
| `Kasa.App.Core.Tests/FormIsleTests.cs` | Yeni | 4 |
| `Kasa.App.Core.Tests/YurutucuTests.cs` | Değişir | 4, 12 |
| `Kasa.App.Core/TemelViewModel.cs` | Değişir | 4 |
| `Kasa.App.Core/Yurutucu.cs` | Değişir | 4 |
| `Kasa.App.Core.Tests/KaydedilmemisDegisiklikTests.cs` | Yeni | 5 |
| `Kasa.App.Core/KaydedilmemisDegisiklik.cs` | Yeni | 5 |
| `Kasa.App.Core/OturumluViewModel.cs` | Değişir | 5, 6, 7, 21 |
| `Kasa.App.Core.Tests/BaglantiDurumuTests.cs` | Yeni | 6 |
| `Kasa.App.Core/AuthViewModel.cs` | Değişir | 6 |
| `Kasa.App.Core/AylikViewModel.cs` | Değişir | 6, 7 |
| `Kasa.App.Core/BaglantiDurumu.cs` | Yeni | 6 |
| `Kasa.App.Core/HaftalikViewModel.cs` | Değişir | 6, 7 |
| `Kasa.App.Core/PanelViewModel.cs` | Değişir | 6, 7 |
| `Kasa.App.Core/RaporViewModel.cs` | Değişir | 6, 7 |
| `Kasa.App/MauiProgram.cs` | Değişir | 6 |
| `Kasa.App.Core.Tests/AnaSayfaVeRaporIptalTests.cs` | Değişir | 7 |
| `Kasa.App.Core.Tests/RaporDurumuTests.cs` | Değişir | 7 |
| `Kasa.App.Core.Tests/SonVeriTests.cs` | Yeni | 7 |
| `Kasa.App.Core/Bicim.cs` | Değişir | 7 |
| `Kasa.App.Core/IslemlerViewModel.cs` | Değişir | 7, 12, 21 |
| `Kasa.App/Views/PanelPage.xaml.cs` | Değişir | 7, 10 |
| `Kasa.App.Core.Tests/Donusturuculer/FormAlaniTests.cs` | Yeni | 8 |
| `Kasa.App/Controls/FormAlani.cs` | Yeni | 8 |
| `Kasa.App/Resources/Styles/Colors.xaml` | Değişir | 8 |
| `Kasa.App/Views/TakipUi.cs` | Değişir | 8, 9, 10, 11 |
| `Kasa.App.Core.Tests/Donusturuculer/GorunurYapiciTests.cs` | Yeni | 9 |
| `Kasa.App/Controls/GorunurYapici.cs` | Yeni | 9 |
| `Kasa.App/Views/KartTakipPage.cs` | Değişir | 9, 15, 16 |
| `Kasa.App.Core.Tests/Donusturuculer/BaglantiSeridiTests.cs` | Yeni | 10 |
| `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglanti.cs` | Yeni | 10 |
| `Kasa.App/AppShell.xaml.cs` | Değişir | 10 |
| `Kasa.App/Controls/BaglantiSeridi.cs` | Yeni | 10 |
| `Kasa.App/Views/AlislarPage.xaml.cs` | Değişir | 10, 13 |
| `Kasa.App/Views/AyarlarPage.xaml.cs` | Değişir | 10, 19 |
| `Kasa.App/Views/AylikPage.xaml.cs` | Değişir | 10 |
| `Kasa.App/Views/DisariAktarPage.cs` | Değişir | 10 |
| `Kasa.App/Views/HaftalikPage.xaml.cs` | Değişir | 10 |
| `Kasa.App/Views/IslemlerPage.xaml.cs` | Değişir | 10, 12 |
| `Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.TakipUi.cs` | Değişir | 11 |
| `Kasa.App.Core.Tests/Donusturuculer/TakipSayfasiSonVeriTests.cs` | Yeni | 11 |
| `Kasa.App.Core.Tests/AlislarViewModelTests.cs` | Değişir | 12, 13 |
| `Kasa.App.Core.Tests/BenzerKayitTests.cs` | Değişir | 12 |
| `Kasa.App.Core.Tests/CekirdekSurumVmTests.cs` | Değişir | 12, 19 |
| `Kasa.App.Core.Tests/Donusturuculer/DonusturucuTests.cs` | Değişir | 12 |
| `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglamalar.cs` | Değişir | 12 |
| `Kasa.App.Core.Tests/GecersizTutarTests.cs` | Değişir | 12, 13, 15, 16, 17, 18, 19 |
| `Kasa.App.Core.Tests/IslemEditorTests.cs` | Değişir | 12 |
| `Kasa.App.Core.Tests/IslemFormuTests.cs` | Yeni | 12 |
| `Kasa.App.Core.Tests/RaporKuraliVmTests.cs` | Değişir | 12 |
| `Kasa.App/App.xaml` | Değişir | 12 |
| `Kasa.App/Converters/EsitIseConverter.cs` | Yeni | 12 |
| `Kasa.App/Views/IslemlerPage.xaml` | Değişir | 12, 21 |
| `Kasa.App.Core.Tests/AlisFormuTests.cs` | Yeni | 13 |
| `Kasa.App.Core/AlislarViewModel.OdemelerVeBelgeler.cs` | Değişir | 13 |
| `Kasa.App.Core/AlislarViewModel.cs` | Değişir | 13, 21 |
| `Kasa.App/Views/AlislarPage.xaml` | Değişir | 13, 21 |
| `Kasa.App.Core.Tests/CekFormuTests.cs` | Yeni | 14 |
| `Kasa.App.Core.Tests/CekTakipViewModelTests.cs` | Değişir | 14 |
| `Kasa.App.Core/CekTakipViewModel.cs` | Değişir | 14, 21 |
| `Kasa.App/Views/CekTakipPage.cs` | Değişir | 14 |
| `Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs` | Değişir | 15, 16 |
| `Kasa.App.Core.Tests/KartFormuTests.cs` | Yeni | 15 |
| `Kasa.App.Core.Tests/KartTakipGorunumTests.cs` | Değişir | 15, 16 |
| `Kasa.App.Core/KartTakipViewModel.Gorunum.cs` | Değişir | 15, 16 |
| `Kasa.App.Core/KartTakipViewModel.cs` | Değişir | 15, 16, 21 |
| `Kasa.App.Core.Tests/FinansTakipTests.cs` | Değişir | 16 |
| `Kasa.App.Core.Tests/KartOdemeAkisiTests.cs` | Yeni | 16 |
| `Kasa.App.Core.Tests/OnizlemeOnayTests.cs` | Değişir | 16 |
| `Kasa.App.Core.Tests/KrediFormuTests.cs` | Yeni | 17 |
| `Kasa.App.Core/KrediTakipViewModel.cs` | Değişir | 17, 21 |
| `Kasa.App/Views/KrediTakipPage.cs` | Değişir | 17 |
| `Kasa.App.Core.Tests/AylikGiderFormuTests.cs` | Yeni | 18 |
| `Kasa.App.Core.Tests/KasaKontrolVeAylikGiderTests.cs` | Değişir | 18 |
| `Kasa.App.Core/AylikGiderViewModel.cs` | Değişir | 18, 21 |
| `Kasa.App/Views/AylikGiderPage.cs` | Değişir | 18 |
| `Kasa.App.Core.Tests/KanalFormuTests.cs` | Yeni | 19 |
| `Kasa.App.Core/AyarlarViewModel.cs` | Değişir | 19 |
| `Kasa.App/Views/AyarlarPage.xaml` | Değişir | 19 |
| `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.SonVeri.cs` | Yeni | 20 |
| `Kasa.App/Views/AylikPage.xaml` | Değişir | 20 |
| `Kasa.App/Views/HaftalikPage.xaml` | Değişir | 20 |
| `Kasa.App/Views/PanelPage.xaml` | Değişir | 20 |
| `Kasa.App.Core.Tests/SonVeriEkranTests.cs` | Yeni | 21 |
| `Kasa.App.Core/BildirimViewModel.cs` | Değişir | 21 |
| `docs/deploy/kasa-2.4.md` | Değişir | 22 |
| `docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md` | Değişir | 22 |

Yeni üretim dosyalarının sorumlulukları: `Kasa.ApiClient/IBaglantiBildirimleri.cs` (ulaşılabilirlik olayları), `Kasa.App.Core/AlanHatalari.cs` (form hataları), `KaydedilmemisDegisiklik.cs` (açılış değerleriyle karşılaştırma ve `IKaydedilmemisForm`), `BaglantiDurumu.cs` (tek bağlantı durumu), `Kasa.App/Controls/FormAlani.cs` (alan, çerçeve ve alan altı ileti), `GorunurYapici.cs` (kaydırma ve hataya git), `BaglantiSeridi.cs` (kabuk şeridi ve `IYenilenebilir`), `Kasa.App/Converters/EsitIseConverter.cs` (düzenlenen satır vurgusu).

---

## Görev 1: ApiClient — doğrulama yanıtının alan hataları

Sunucunun `errors` sözlüğü `KasaApiException.AlanHatalari`'na (küçük harf alan adı → ilk ileti) taşınır; birleşik `Message` aynı kalır.

**Dosyalar:**
- Değiştir: `Kasa.ApiClient/KasaApiClient.cs`
- Değiştir: `Kasa.ApiClient/KasaApiException.cs`
- Test (oluştur): `Kasa.ApiClient.Tests/AlanHatalariTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.ApiClient.Tests/AlanHatalariTests.cs` (yeni dosya):

```csharp
using System.Net;

namespace Kasa.ApiClient.Tests;

/// <summary>Sunucunun doğrulama yanıtındaki alan sözlüğü (ValidationProblem "errors") <see cref="KasaApiException.AlanHatalari"/>'na
/// taşınır: alan adı küçük harfe iner, ilk ileti alınır; birleşik ileti (Message) eskisi gibi kalır.</summary>
public class AlanHatalariTests
{
    private static KasaApiClient Kur(SahteHandler h)
        => new(new HttpClient(h) { BaseAddress = new Uri("https://ornek.test/") }, new BellekTokenStore());

    [Fact]
    public async Task Dogrulama_sozlugu_alan_hatalarina_ilk_iletiyle_kucuk_harfle_aktarilir()
    {
        const string govde = """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"cari":["Bu alan boş olamaz.","En fazla 200 karakter girilebilir."],"krediKartiId":["Kayıtlı bir kredi kartı seçin."],
                       "kalemler[0].aciklama":["Bu alan boş olamaz."],"bos":[]}}
            """;
        var c = Kur(new SahteHandler().Kuyrukla(HttpStatusCode.BadRequest, govde));

        var hata = await Assert.ThrowsAsync<KasaApiException>(() => c.IslemOlusturAsync(new IslemYaz(new DateOnly(2026, 10, 2), "", 5m, "MEZAT", GiderTipi.Cari, null)));

        Assert.Equal(3, hata.AlanHatalari.Count);
        Assert.Equal("Bu alan boş olamaz.", hata.AlanHatalari["cari"]);
        Assert.Equal("Kayıtlı bir kredi kartı seçin.", hata.AlanHatalari["kredikartiid"]);
        Assert.Equal("Bu alan boş olamaz.", hata.AlanHatalari["kalemler[0].aciklama"]);
        // Geriye uyum: birleşik ileti aynı kalır (her iletinin bir kez geçtiği satırlar).
        Assert.Equal("Bu alan boş olamaz.\nEn fazla 200 karakter girilebilir.\nKayıtlı bir kredi kartı seçin.", hata.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "{\"hata\":\"Kanal kullanımda.\"}", "Kanal kullanımda.")]
    [InlineData(HttpStatusCode.BadRequest, "{\"detail\":\"Geçersiz tarih.\"}", "Geçersiz tarih.")]
    [InlineData(HttpStatusCode.BadRequest, "\"Düz ileti.\"", "Düz ileti.")]
    [InlineData(HttpStatusCode.BadRequest, "<html>proxy error</html>", "Girilen bilgileri kontrol edin.")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"errors\":{\"cari\":[\"x\"]},\"traceId\":\"abc\"}", "API hatası: 500 InternalServerError")]
    public async Task Alan_sozlugu_olmayan_yanit_bos_alan_hatasi_ve_eski_iletiyi_verir(HttpStatusCode kod, string govde, string ileti)
    {
        var c = Kur(new SahteHandler().Kuyrukla(kod, govde));

        var hata = await Assert.ThrowsAsync<KasaApiException>(c.KanallarAsync);

        Assert.Empty(hata.AlanHatalari);
        Assert.Equal(ileti, hata.Message);
    }

    [Fact]
    public void Kurucu_alan_hatasi_verilmezse_bos_sozluk_tasir()
    {
        var hata = new KasaApiException(HttpStatusCode.BadRequest, "x");
        Assert.Empty(hata.AlanHatalari);
        var dolu = new KasaApiException(HttpStatusCode.BadRequest, "x", alanHatalari: new Dictionary<string, string> { ["ad"] = "Bu alan boş olamaz." });
        Assert.Equal("Bu alan boş olamaz.", dolu.AlanHatalari["ad"]);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~AlanHatalariTests"
```

Beklenen (Kasa.ApiClient.Tests): derleme hatası, 2 farklı ileti; örnekler:

- `AlanHatalariTests.cs: CS1061: 'KasaApiException' bir 'AlanHatalari' tanımı içermiyor ve 'KasaApiException' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'AlanHatalari' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `AlanHatalariTests.cs: CS1739: 'KasaApiException' için en iyi yeniden yükleme, 'alanHatalari' adlı bir parametre içermiyor`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.ApiClient/KasaApiClient.cs` (1/2) — Bul:

```csharp
            {
                if (yanit.StatusCode == HttpStatusCode.Unauthorized && tokenEkle)
                    await OturumuGecersizKilAsync(token);
                var (mesaj, iz) = await HataAyrintisiAsync(yanit, ct);
                throw new KasaApiException(yanit.StatusCode, mesaj, iz);
            }
        }
        return yanit;
```

Yerine:

```csharp
            {
                if (yanit.StatusCode == HttpStatusCode.Unauthorized && tokenEkle)
                    await OturumuGecersizKilAsync(token);
                var (mesaj, iz, alanlar) = await HataAyrintisiAsync(yanit, ct);
                throw new KasaApiException(yanit.StatusCode, mesaj, iz, alanlar);
            }
        }
        return yanit;
```

`Kasa.ApiClient/KasaApiClient.cs` (2/2) — Bul:

```csharp
            OturumSonlandi?.Invoke(this, new OturumSonlandiEventArgs(neden));
    }

    /// <summary>Hata yanıtından kullanıcıya taşınan ileti (yalnız sunucunun anlamlı Türkçe ileti verdiği durumlarda) ve sunucu
    /// hatasının (5xx) ProblemDetails iz kimliği (traceId; kullanıcıya kısa "Hata kodu" olarak gösterilir).</summary>
    private static async Task<(string? Mesaj, string? Iz)> HataAyrintisiAsync(HttpResponseMessage yanit, CancellationToken ct)
    {
        var iletiVar = yanit.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
        var sunucuHatasi = (int)yanit.StatusCode >= 500;
        if (!iletiVar && !sunucuHatasi)
            return (null, null);
        try
        {
            using var belge = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync(ct));
            var kok = belge.RootElement;
            var iz = sunucuHatasi && kok.ValueKind == JsonValueKind.Object && kok.TryGetProperty("traceId", out var izDegeri) && izDegeri.ValueKind == JsonValueKind.String
                ? izDegeri.GetString() : null;
            return (iletiVar ? Ileti(kok) : null, iz);
        }
        catch (JsonException) { /* JSON dışındaki hata gövdesini kullanıcıya taşıma. */ }
        return (null, null);
    }

    private static string? Ileti(JsonElement kok)
```

Yerine:

```csharp
            OturumSonlandi?.Invoke(this, new OturumSonlandiEventArgs(neden));
    }

    /// <summary>Hata yanıtından kullanıcıya taşınan ileti (yalnız sunucunun anlamlı Türkçe ileti verdiği durumlarda), sunucu
    /// hatasının (5xx) ProblemDetails iz kimliği (traceId; kullanıcıya kısa "Hata kodu" olarak gösterilir) ve iletili yanıttaki
    /// alan hataları (<see cref="AlanHatalari"/>).</summary>
    private static async Task<(string? Mesaj, string? Iz, IReadOnlyDictionary<string, string>? Alanlar)> HataAyrintisiAsync(HttpResponseMessage yanit, CancellationToken ct)
    {
        var iletiVar = yanit.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
        var sunucuHatasi = (int)yanit.StatusCode >= 500;
        if (!iletiVar && !sunucuHatasi)
            return (null, null, null);
        try
        {
            using var belge = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync(ct));
            var kok = belge.RootElement;
            var iz = sunucuHatasi && kok.ValueKind == JsonValueKind.Object && kok.TryGetProperty("traceId", out var izDegeri) && izDegeri.ValueKind == JsonValueKind.String
                ? izDegeri.GetString() : null;
            return iletiVar ? (Ileti(kok), iz, AlanHatalari(kok)) : (null, iz, null);
        }
        catch (JsonException) { /* JSON dışındaki hata gövdesini kullanıcıya taşıma. */ }
        return (null, null, null);
    }

    /// <summary>Doğrulama yanıtının "errors" sözlüğü: alan adı küçük harfe iner (sunucu camelCase yazar: "krediKartiId" →
    /// "kredikartiid"), her alandan ilk dolu ileti alınır. Sözlük yoksa ya da boşsa null.</summary>
    private static Dictionary<string, string>? AlanHatalari(JsonElement kok)
    {
        if (kok.ValueKind != JsonValueKind.Object || !kok.TryGetProperty("errors", out var hatalar) || hatalar.ValueKind != JsonValueKind.Object)
            return null;
        var alanlar = new Dictionary<string, string>();
        foreach (var alan in hatalar.EnumerateObject())
        {
            if (alan.Value.ValueKind != JsonValueKind.Array)
                continue;
            var ilk = alan.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString())
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
            if (ilk is not null)
                alanlar.TryAdd(alan.Name.ToLowerInvariant(), ilk);
        }
        return alanlar.Count > 0 ? alanlar : null;
    }

    private static string? Ileti(JsonElement kok)
```

`Kasa.ApiClient/KasaApiException.cs` (1/2) — Bul:

```csharp
    public string? IzKimligi { get; }
    /// <summary>Kullanıcıya gösterilen kısa iz ("Hata kodu: …"); bkz. <see cref="KisaIz"/>.</summary>
    public string? HataKodu => KisaIz(IzKimligi);
    public KasaApiException(HttpStatusCode kod, string? mesaj = null, string? izKimligi = null)
        : base(mesaj ?? (kod switch
        {
            HttpStatusCode.BadRequest => "Girilen bilgileri kontrol edin.",
```

Yerine:

```csharp
    public string? IzKimligi { get; }
    /// <summary>Kullanıcıya gösterilen kısa iz ("Hata kodu: …"); bkz. <see cref="KisaIz"/>.</summary>
    public string? HataKodu => KisaIz(IzKimligi);
    /// <summary>Sunucunun doğrulama yanıtındaki (ValidationProblem "errors") alan hataları: alan adı (küçük harf; "kalemler[0].aciklama"
    /// gibi önekler korunur) → ilk ileti. Alan sözlüğü olmayan yanıtta boştur; birleşik ileti (Message) ayrıca durur.</summary>
    public IReadOnlyDictionary<string, string> AlanHatalari { get; }
    public KasaApiException(HttpStatusCode kod, string? mesaj = null, string? izKimligi = null, IReadOnlyDictionary<string, string>? alanHatalari = null)
        : base(mesaj ?? (kod switch
        {
            HttpStatusCode.BadRequest => "Girilen bilgileri kontrol edin.",
```

`Kasa.ApiClient/KasaApiException.cs` (2/2) — Bul:

```csharp
    {
        DurumKodu = kod;
        IzKimligi = izKimligi;
    }

    /// <summary>İz kimliğinin kullanıcıya gösterilen kısa biçimi (web traceCode ile aynı kural): W3C biçiminde
    /// ("00-&lt;32 hex&gt;-&lt;16 hex&gt;-&lt;2 hex&gt;") iz numarasının ilk 8 hanesi, diğer kimlikte (TraceIdentifier) en çok
    /// 24 karakterse kendisi, daha uzunsa ilk 12 karakteri. Parça tam kimliğin içindedir: yönetici logda arar. Beklenmeyen
```

Yerine:

```csharp
    {
        DurumKodu = kod;
        IzKimligi = izKimligi;
        AlanHatalari = alanHatalari ?? BosAlanHatalari;
    }

    private static readonly IReadOnlyDictionary<string, string> BosAlanHatalari = new Dictionary<string, string>();

    /// <summary>İz kimliğinin kullanıcıya gösterilen kısa biçimi (web traceCode ile aynı kural): W3C biçiminde
    /// ("00-&lt;32 hex&gt;-&lt;16 hex&gt;-&lt;2 hex&gt;") iz numarasının ilk 8 hanesi, diğer kimlikte (TraceIdentifier) en çok
    /// 24 karakterse kendisi, daha uzunsa ilk 12 karakteri. Parça tam kimliğin içindedir: yönetici logda arar. Beklenmeyen
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
```

Beklenen: Kasa.ApiClient.Tests `Başarılı:   189`; Kasa.App.Core.Tests `Başarılı:  1085`, ikisinde de `Başarısız:     0`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.ApiClient.Tests/AlanHatalariTests.cs Kasa.ApiClient/KasaApiClient.cs Kasa.ApiClient/KasaApiException.cs
git commit -F - <<'MESAJ'
feat(apiclient): doğrulama yanıtının alan hatalarını taşı

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 2: ApiClient — sunucuya ulaşılabilirlik bildirimi

Tek gönderim noktası ağ hatasında ve süre sınırında `SunucuyaUlasilamadi`, alınan her yanıtta `SunucuyaUlasildi` bildirir (`IBaglantiBildirimleri`).

**Dosyalar:**
- Oluştur: `Kasa.ApiClient/IBaglantiBildirimleri.cs`
- Değiştir: `Kasa.ApiClient/KasaApiClient.cs`
- Test (oluştur): `Kasa.ApiClient.Tests/BaglantiBildirimleriTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.ApiClient.Tests/BaglantiBildirimleriTests.cs` (yeni dosya):

```csharp
using System.Net;
using System.Text;

namespace Kasa.ApiClient.Tests;

/// <summary>İstemcinin tek gönderim noktası sunucuya ulaşılabilirliği bildirir (<see cref="IBaglantiBildirimleri"/>): yanıt alınan
/// her istek (4xx ve 5xx dahil) ulaşıldı, ağ hatası ve süre sınırı ulaşılamadı sayılır; çağıranın iptali bildirilmez.</summary>
public class BaglantiBildirimleriTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => fn(r, c);
    }

    private static readonly KasaZamanAsimlari Kisa = new(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

    private static (KasaApiClient Istemci, List<string> Olaylar) Kur(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn)
    {
        var istemci = new KasaApiClient(new HttpClient(new Handler(fn)) { BaseAddress = new Uri("https://ornek.test/") }, new BellekTokenStore(), Kisa);
        var olaylar = new List<string>();
        IBaglantiBildirimleri bildirimler = istemci;
        bildirimler.SunucuyaUlasildi += (_, _) => olaylar.Add("ulaşıldı");
        bildirimler.SunucuyaUlasilamadi += (_, e) => olaylar.Add("ulaşılamadı:" + e.GetType().Name);
        return (istemci, olaylar);
    }

    private static HttpResponseMessage Yanit(HttpStatusCode kod, string json) => new(kod) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Ag_hatasi_ulasilamadi_bildirir_ve_istisna_aynen_cikar()
    {
        var (c, olaylar) = Kur((_, _) => throw new HttpRequestException("Bağlantı reddedildi."));

        await Assert.ThrowsAsync<HttpRequestException>(c.KanallarAsync);

        Assert.Equal(["ulaşılamadı:HttpRequestException"], olaylar);
    }

    [Fact]
    public async Task Sure_siniri_ulasilamadi_bildirir()
    {
        var (c, olaylar) = Kur(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); });

        await Assert.ThrowsAsync<TimeoutException>(c.KanallarAsync);

        Assert.Equal(["ulaşılamadı:TimeoutException"], olaylar);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "[]")]
    [InlineData(HttpStatusCode.BadRequest, "{\"hata\":\"x\"}")]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    public async Task Yanit_alinan_istek_durum_kodundan_bagimsiz_ulasildi_bildirir(HttpStatusCode kod, string json)
    {
        var (c, olaylar) = Kur((_, _) => Task.FromResult(Yanit(kod, json)));

        try
        { await c.KanallarAsync(); }
        catch (KasaApiException) { }

        Assert.Equal(["ulaşıldı"], olaylar);
    }

    [Fact]
    public async Task Cagiranin_iptali_bildirilmez()
    {
        var (c, olaylar) = Kur(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); });
        using var iptal = new CancellationTokenSource();

        var istek = c.HaftalikAsync(iptal.Token);
        iptal.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => istek);
        Assert.Empty(olaylar);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~BaglantiBildirimleriTests"
```

Beklenen (Kasa.ApiClient.Tests): derleme hatası, 1 farklı ileti; örnekler:

- `BaglantiBildirimleriTests.cs: CS0246: 'IBaglantiBildirimleri' türü veya ad alanı adı bulunamadı (bir using yönergeniz veya derleme başvurunuz mu eksik?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.ApiClient/IBaglantiBildirimleri.cs` (yeni dosya):

```csharp
namespace Kasa.ApiClient;

/// <summary>Sunucuya ulaşılabilirliğin bildirimi (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3); UI bağımlılığı
/// içermez. Olaylar isteği gönderen iş parçacığında gelir; dinleyen UI'ya kendisi aktarır.</summary>
public interface IBaglantiBildirimleri
{
    /// <summary>Sunucudan HTTP yanıtı alındı. Durum kodu ne olursa olsun (4xx ve 5xx dahil) sunucuya ulaşılmıştır.</summary>
    event EventHandler? SunucuyaUlasildi;

    /// <summary>İstek sunucuya ulaşamadı (<see cref="HttpRequestException"/>) ya da süre sınırında yanıt gelmedi
    /// (<see cref="TimeoutException"/>). Olay istisna istemciden çıkmadan önce gelir; çağıranın iptali bildirilmez.</summary>
    event EventHandler<Exception>? SunucuyaUlasilamadi;
}
```

`Kasa.ApiClient/KasaApiClient.cs` (1/4) — Bul:

```csharp
namespace Kasa.ApiClient;

/// <summary>Kasa REST API'sinin tiplı istemcisi. Her isteğe Bearer token ekler; başarısız durumda KasaApiException.</summary>
public sealed partial class KasaApiClient : IKasaApi, IOturumBildirimleri
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private readonly KasaZamanAsimlari _zaman;
    private readonly SemaphoreSlim _oturumKilidi = new(1, 1);
    public event EventHandler? OturumSonlandi;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
```

Yerine:

```csharp
namespace Kasa.ApiClient;

/// <summary>Kasa REST API'sinin tiplı istemcisi. Her isteğe Bearer token ekler; başarısız durumda KasaApiException.</summary>
public sealed partial class KasaApiClient : IKasaApi, IOturumBildirimleri, IBaglantiBildirimleri
{
    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private readonly KasaZamanAsimlari _zaman;
    private readonly SemaphoreSlim _oturumKilidi = new(1, 1);
    public event EventHandler? OturumSonlandi;
    public event EventHandler? SunucuyaUlasildi;
    public event EventHandler<Exception>? SunucuyaUlasilamadi;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
```

`Kasa.ApiClient/KasaApiClient.cs` (2/4) — Bul:

```csharp

    /// <summary>İşlemi istek başına süre sınırıyla çalıştırır; sınır, işlemin gövde okuması dahil tamamını kapsar.
    /// Süre (ya da HttpClient.Timeout) dolarsa <see cref="TimeoutException"/>; çağıranın iptali OperationCanceledException
    /// olarak kalır. Kullanıcıya "sunucu yanıt vermedi" ile "vazgeçildi" farklı anlatılır.</summary>
    private static async Task<T> SureliAsync<T>(TimeSpan sure, CancellationToken iptal, Func<CancellationToken, Task<T>> islem)
    {
        using var kaynak = CancellationTokenSource.CreateLinkedTokenSource(iptal);
        kaynak.CancelAfter(sure);
```

Yerine:

```csharp

    /// <summary>İşlemi istek başına süre sınırıyla çalıştırır; sınır, işlemin gövde okuması dahil tamamını kapsar.
    /// Süre (ya da HttpClient.Timeout) dolarsa <see cref="TimeoutException"/>; çağıranın iptali OperationCanceledException
    /// olarak kalır. Kullanıcıya "sunucu yanıt vermedi" ile "vazgeçildi" farklı anlatılır. Süre sınırı sunucuya ulaşılamadı
    /// sayılır (<see cref="IBaglantiBildirimleri.SunucuyaUlasilamadi"/>).</summary>
    private async Task<T> SureliAsync<T>(TimeSpan sure, CancellationToken iptal, Func<CancellationToken, Task<T>> islem)
    {
        using var kaynak = CancellationTokenSource.CreateLinkedTokenSource(iptal);
        kaynak.CancelAfter(sure);
```

`Kasa.ApiClient/KasaApiClient.cs` (3/4) — Bul:

```csharp
        { return await islem(kaynak.Token); }
        catch (OperationCanceledException e) when (!iptal.IsCancellationRequested && (kaynak.IsCancellationRequested || e.InnerException is TimeoutException))
        {
            throw new TimeoutException(KasaZamanAsimlari.Ileti, e);
        }
    }

    /// <summary>Bearer ekleyip gönderir; başarısız yanıtı KasaApiException'a çevirir (401 oturumu kapatır).</summary>
    private async Task<HttpResponseMessage> YanitAlAsync(HttpRequestMessage istek, bool tokenEkle, HttpCompletionOption tamamlama, CancellationToken ct)
    {
        string? token = null;
```

Yerine:

```csharp
        { return await islem(kaynak.Token); }
        catch (OperationCanceledException e) when (!iptal.IsCancellationRequested && (kaynak.IsCancellationRequested || e.InnerException is TimeoutException))
        {
            var zamanAsimi = new TimeoutException(KasaZamanAsimlari.Ileti, e);
            SunucuyaUlasilamadi?.Invoke(this, zamanAsimi);
            throw zamanAsimi;
        }
    }

    /// <summary>Bearer ekleyip gönderir; başarısız yanıtı KasaApiException'a çevirir (401 oturumu kapatır). Uygulamanın bütün
    /// istekleri buradan geçer: ağ hatası sunucuya ulaşılamadı, alınan her yanıt (durum kodu ne olursa olsun) ulaşıldı bildirir.</summary>
    private async Task<HttpResponseMessage> YanitAlAsync(HttpRequestMessage istek, bool tokenEkle, HttpCompletionOption tamamlama, CancellationToken ct)
    {
        string? token = null;
```

`Kasa.ApiClient/KasaApiClient.cs` (4/4) — Bul:

```csharp
            if (!string.IsNullOrEmpty(token))
                istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        var yanit = await _http.SendAsync(istek, tamamlama, ct);
        if (!yanit.IsSuccessStatusCode)
        {
            using (yanit)
```

Yerine:

```csharp
            if (!string.IsNullOrEmpty(token))
                istek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        HttpResponseMessage yanit;
        try
        { yanit = await _http.SendAsync(istek, tamamlama, ct); }
        catch (HttpRequestException e)
        {
            SunucuyaUlasilamadi?.Invoke(this, e);
            throw;
        }
        SunucuyaUlasildi?.Invoke(this, EventArgs.Empty);
        if (!yanit.IsSuccessStatusCode)
        {
            using (yanit)
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
```

Beklenen: Kasa.ApiClient.Tests `Başarılı:   195`; Kasa.App.Core.Tests `Başarılı:  1085`, ikisinde de `Başarısız:     0`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.ApiClient.Tests/BaglantiBildirimleriTests.cs Kasa.ApiClient/IBaglantiBildirimleri.cs Kasa.ApiClient/KasaApiClient.cs
git commit -F - <<'MESAJ'
feat(apiclient): sunucuya ulaşılabilirliği bildir

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 3: App.Core — `AlanHatalari`

Form başına alan → ileti, genel hata, sıra, ön doğrulama yardımcısı (`Denetle`), sunucu eşlemesi ve dizinleyici bildirimi.

**Dosyalar:**
- Oluştur: `Kasa.App.Core/AlanHatalari.cs`
- Test (oluştur): `Kasa.App.Core.Tests/AlanHatalariTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/AlanHatalariTests.cs` (yeni dosya):

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Form alan hataları: alan → ileti, genel hata, sıra, temizleme, sunucu eşlemesi ve değişiklik bildirimi.</summary>
public class AlanHatalariTests
{
    private static List<string> Bildirimleri(AlanHatalari h)
    {
        var adlar = new List<string>();
        h.PropertyChanged += (_, e) => adlar.Add(e.PropertyName!);
        return adlar;
    }

    [Fact]
    public void Ayarla_dizinleyiciyi_ve_Var_i_bildirir_sirayi_korur()
    {
        var h = new AlanHatalari();
        var adlar = Bildirimleri(h);

        h.Ayarla("DuzenTutar", "Tutar sıfırdan büyük olmalı.");
        h.Ayarla("DuzenCari", "Açıklama boş olamaz.");
        h.Ayarla("DuzenTutar", "Tutar sıfır olamaz.");

        Assert.Equal("Tutar sıfır olamaz.", h["DuzenTutar"]);
        Assert.Equal("Açıklama boş olamaz.", h["DuzenCari"]);
        Assert.Null(h["DuzenNot"]);
        Assert.True(h.Var);
        Assert.Equal("DuzenTutar", h.IlkAlan);
        Assert.Equal(["DuzenTutar", "DuzenCari"], h.Alanlar);
        Assert.Equal(["Item[DuzenTutar]", "Var", "Item[DuzenCari]", "Item[DuzenTutar]"], adlar);
    }

    [Fact]
    public void Alan_temizlenince_yalniz_o_alan_kalkar_tum_temizlik_genel_hatayi_da_kaldirir()
    {
        var h = new AlanHatalari();
        h.Ayarla("A", "a");
        h.Ayarla("B", "b");
        h.Genel = "Sunucuya ulaşılamadı.";
        var adlar = Bildirimleri(h);

        h.Temizle("A");
        h.Temizle("Yok");
        h.Temizle((string?)null);
        Assert.Null(h["A"]);
        Assert.Equal("b", h["B"]);
        Assert.Equal("B", h.IlkAlan);
        Assert.Equal(["Item[A]"], adlar);

        adlar.Clear();
        h.Temizle();
        Assert.False(h.Var);
        Assert.Null(h.Genel);
        Assert.Null(h["B"]);
        Assert.Null(h.IlkAlan);
        Assert.Equal(["Item[B]", "Item", "Genel", "Var"], adlar);

        adlar.Clear();
        h.Temizle();
        Assert.Empty(adlar);
    }

    [Fact]
    public void Genel_bosluk_ise_null_olur()
    {
        var h = new AlanHatalari { Genel = "  " };
        Assert.Null(h.Genel);
        Assert.False(h.Var);
        h.Genel = "Hata";
        Assert.True(h.Var);
    }

    [Fact]
    public void Sunucu_hatalari_eslenir_eslenmeyenler_bir_kez_doner()
    {
        var h = new AlanHatalari();
        var eslem = new Dictionary<string, string> { ["cari"] = "DuzenCari", ["tutartl"] = "DuzenTutar" };
        var sunucu = new Dictionary<string, string>
        {
            ["cari"] = "Bu alan boş olamaz.",
            ["istekid"] = "Geçerli bir istek kimliği gerekir.",
            ["kalemler"] = "Geçerli bir istek kimliği gerekir.",
            ["tutartl"] = "Tutar en fazla iki ondalık basamak içerebilir.",
        };

        var kalan = h.SunucuHatalariniYaz(sunucu, eslem);

        Assert.Equal("Bu alan boş olamaz.", h["DuzenCari"]);
        Assert.Equal("Tutar en fazla iki ondalık basamak içerebilir.", h["DuzenTutar"]);
        Assert.Equal(["Geçerli bir istek kimliği gerekir."], kalan);
    }

    [Fact]
    public void Denetle_gecersizde_yazar_ilk_kural_kazanir()
    {
        var h = new AlanHatalari();
        Assert.True(h.Denetle(true, "A", "a"));
        Assert.False(h.Var);
        Assert.False(h.Denetle(false, "A", "ilk"));
        Assert.False(h.Denetle(false, "A", "ikinci"));
        Assert.Equal("ilk", h["A"]);
    }

    [Fact]
    public void Goster_istegi_yalniz_hata_varken_gelir()
    {
        var h = new AlanHatalari();
        var istek = 0;
        h.GosterIstendi += (_, _) => istek++;
        h.GosterIste();
        Assert.Equal(0, istek);
        h.Ayarla("A", "a");
        h.GosterIste();
        Assert.Equal(1, istek);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~AlanHatalariTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 1 farklı ileti; örnekler:

- `AlanHatalariTests.cs: CS0246: 'AlanHatalari' türü veya ad alanı adı bulunamadı (bir using yönergeniz veya derleme başvurunuz mu eksik?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/AlanHatalari.cs` (yeni dosya):

```csharp
using System.ComponentModel;

namespace Kasa.App.Core;

/// <summary>
/// Bir formun hataları (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §1): alan → ileti ve formun genel hatası
/// (<see cref="Genel"/>, "FormHatasi"). Alan adı formun görünüm modelindeki özelliğin adıdır ("DuzenCari"); görünüm
/// <c>Hatalar[DuzenCari]</c> diye bağlar. Değişiklik dizinleyici için "Item[alan]" (hepsi silinince ayrıca "Item"), genel hata
/// için <see cref="Genel"/> ve <see cref="Var"/> adıyla bildirilir. Hatalar konuldukları sırayı korur: kaydırma ilk hatalı alana
/// gider (<see cref="IlkAlan"/>).
/// </summary>
public sealed class AlanHatalari : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _hatalar = new(StringComparer.Ordinal);
    private readonly List<string> _sira = [];
    private string? _genel;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Kaydetme bittiğinde hata varsa görünüm ilk hatalı alana (yoksa genel hata kutusuna) kaydırır ve odaklanır.</summary>
    public event EventHandler? GosterIstendi;

    /// <summary>Alanın iletisi; hata yoksa null.</summary>
    public string? this[string alan] => _hatalar.GetValueOrDefault(alan);

    /// <summary>Formun genel hatası: alana eşlenemeyen sunucu iletisi, bağlantı ve sunucu hatası. Formun en üstünde gösterilir.</summary>
    public string? Genel
    {
        get => _genel;
        set
        {
            var yeni = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_genel == yeni)
                return;
            var onceVar = Var;
            _genel = yeni;
            Bildir(nameof(Genel));
            if (onceVar != Var)
                Bildir(nameof(Var));
        }
    }

    /// <summary>Alan ya da genel hata var mı.</summary>
    public bool Var => _genel is not null || _hatalar.Count > 0;

    /// <summary>Hatalı alanlar, konuldukları sırayla.</summary>
    public IReadOnlyList<string> Alanlar => _sira;

    /// <summary>İlk hatalı alan; yoksa null.</summary>
    public string? IlkAlan => _sira.Count > 0 ? _sira[0] : null;

    public void Ayarla(string alan, string ileti)
    {
        var onceVar = Var;
        if (!_hatalar.ContainsKey(alan))
            _sira.Add(alan);
        _hatalar[alan] = ileti;
        Bildir($"Item[{alan}]");
        if (onceVar != Var)
            Bildir(nameof(Var));
    }

    /// <summary>Ön doğrulama: koşul tutmuyorsa alana iletiyi yazar (alanda önceki hata varsa korunur: ilk kural kazanır).</summary>
    /// <returns><paramref name="gecerli"/>.</returns>
    public bool Denetle(bool gecerli, string alan, string ileti)
    {
        if (!gecerli && !_hatalar.ContainsKey(alan))
            Ayarla(alan, ileti);
        return gecerli;
    }

    /// <summary>Alanın hatasını kaldırır (kullanıcı alanı değiştirdi); hata yoksa bir şey yapmaz.</summary>
    public void Temizle(string? alan)
    {
        if (alan is null || !_hatalar.Remove(alan))
            return;
        _sira.Remove(alan);
        Bildir($"Item[{alan}]");
        if (!Var)
            Bildir(nameof(Var));
    }

    /// <summary>Bütün alan hatalarını ve genel hatayı kaldırır (kayıt değişti, Yeni, Vazgeç, başarılı kayıt).</summary>
    public void Temizle()
    {
        if (!Var)
            return;
        var alanlar = _sira.ToList();
        _hatalar.Clear();
        _sira.Clear();
        _genel = null;
        foreach (var alan in alanlar)
            Bildir($"Item[{alan}]");
        Bildir("Item");
        Bildir(nameof(Genel));
        Bildir(nameof(Var));
    }

    /// <summary>Sunucunun alan hatalarını (küçük harf sunucu alan adı → ileti) <paramref name="eslem"/> ile formun alanlarına yazar.</summary>
    /// <returns>Formun alanına eşlenemeyen iletiler (genel hataya gider), sırayla ve her biri bir kez.</returns>
    public IReadOnlyList<string> SunucuHatalariniYaz(IReadOnlyDictionary<string, string> sunucu, IReadOnlyDictionary<string, string> eslem)
    {
        var eslenmeyen = new List<string>();
        foreach (var (sunucuAlani, ileti) in sunucu)
        {
            if (eslem.TryGetValue(sunucuAlani, out var alan))
                Ayarla(alan, ileti);
            else if (!eslenmeyen.Contains(ileti))
                eslenmeyen.Add(ileti);
        }
        return eslenmeyen;
    }

    /// <summary>Görünümden hataya kaydırmasını ister (<see cref="GosterIstendi"/>); hata yoksa bir şey yapmaz.</summary>
    public void GosterIste()
    {
        if (Var)
            GosterIstendi?.Invoke(this, EventArgs.Empty);
    }

    private void Bildir(string ad) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ad));
}
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1091`, `Başarısız:     0`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/AlanHatalariTests.cs Kasa.App.Core/AlanHatalari.cs
git commit -F - <<'MESAJ'
feat(app-core): form alan hataları yapısı

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 4: App.Core — ortak form kaydı ve kopukken okuma hatası

`Yurutucu` hata işleyicisi alır; `TemelViewModel.FormIsleAsync` ön doğrulama, sunucu eşlemesi ve hataya kaydırma isteğini toplar; kopukken okumanın bağlantı hatası sayfaya yazılmaz.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/TemelViewModel.cs`
- Değiştir: `Kasa.App.Core/Yurutucu.cs`
- Test (oluştur): `Kasa.App.Core.Tests/FormIsleTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/YurutucuTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/FormIsleTests.cs` (yeni dosya):

```csharp
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Ortak form kaydı (TemelViewModel.FormIsleAsync) ve okuma hatasının bağlantı kopukken sayfaya yazılmaması.</summary>
public partial class FormIsleTests
{
    private sealed partial class DenemeVm : TemelViewModel
    {
        public AlanHatalari Hatalar { get; } = new();
        [ObservableProperty] private string _ad = "";
        [ObservableProperty] private decimal _tutar;
        public bool Kopuk { get; set; }
        protected override bool BaglantiKopuk => Kopuk;
        protected override IEnumerable<AlanHatalari> Formlar => [Hatalar];
        public static readonly Dictionary<string, string> Eslem = new() { ["ad"] = nameof(Ad), ["tutar"] = nameof(Tutar) };

        public Task KaydetAsync(Func<Task> istek) => FormIsleAsync(Hatalar, async _ =>
        {
            Hatalar.Denetle(!string.IsNullOrWhiteSpace(Ad), nameof(Ad), "Ad boş olamaz.");
            Hatalar.Denetle(Tutar > 0, nameof(Tutar), "Tutar sıfırdan büyük olmalı.");
            if (Hatalar.Var)
                return;
            await istek();
        }, Eslem);

        public Task OkuAsync(Exception hata) => new SonIstekHatti(Yurutucu).YukleAsync<int>(_ => Task.FromException<int>(hata), _ => { });
    }

    private static (DenemeVm Vm, List<int> Gosterim) Kur()
    {
        var vm = new DenemeVm();
        var gosterim = new List<int>();
        vm.Hatalar.GosterIstendi += (_, _) => gosterim.Add(1);
        return (vm, gosterim);
    }

    [Fact]
    public async Task On_dogrulama_istek_gondermez_alanlara_yazar_ve_kaydirma_ister()
    {
        var (vm, gosterim) = Kur();
        var gonderildi = false;

        await vm.KaydetAsync(() => { gonderildi = true; return Task.CompletedTask; });

        Assert.False(gonderildi);
        Assert.Equal("Ad boş olamaz.", vm.Hatalar[nameof(DenemeVm.Ad)]);
        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.Hatalar[nameof(DenemeVm.Tutar)]);
        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);
        Assert.Single(gosterim);
    }

    [Fact]
    public async Task Alan_degisince_o_alanin_hatasi_kalkar_kayit_basinda_hepsi_kalkar()
    {
        var (vm, _) = Kur();
        await vm.KaydetAsync(() => Task.CompletedTask);

        vm.Ad = "Ege Gıda";
        Assert.Null(vm.Hatalar[nameof(DenemeVm.Ad)]);
        Assert.NotNull(vm.Hatalar[nameof(DenemeVm.Tutar)]);

        vm.Hatalar.Genel = "eski";
        vm.Tutar = 5m;
        await vm.KaydetAsync(() => Task.CompletedTask);
        Assert.False(vm.Hatalar.Var);
    }

    [Fact]
    public async Task Sunucu_alan_hatalari_eslenir_eslenmeyen_genel_hataya_gider()
    {
        var (vm, gosterim) = Kur();
        vm.Ad = "x";
        vm.Tutar = 1m;
        var alanlar = new Dictionary<string, string> { ["ad"] = "Bu alan boş olamaz.", ["istekid"] = "Geçerli bir istek kimliği gerekir." };

        await vm.KaydetAsync(() => throw new KasaApiException(HttpStatusCode.BadRequest, "birleşik", alanHatalari: alanlar));

        Assert.Equal("Bu alan boş olamaz.", vm.Hatalar[nameof(DenemeVm.Ad)]);
        Assert.Equal("Geçerli bir istek kimliği gerekir.", vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
        Assert.Single(gosterim);
    }

    [Fact]
    public async Task Alan_sozlugu_olmayan_ret_genel_hataya_iletisiyle_gider()
    {
        var (vm, _) = Kur();
        vm.Ad = "x";
        vm.Tutar = 1m;

        await vm.KaydetAsync(() => throw new KasaApiException(HttpStatusCode.Conflict, "Kayıt başka oturumda değişti."));

        Assert.Equal("Kayıt başka oturumda değişti.", vm.Hatalar.Genel);
    }

    [Fact]
    public async Task Ag_hatasinda_kayit_yapilmadi_zaman_asiminda_kontrol_istenir_form_degerleri_korunur()
    {
        var (vm, _) = Kur();
        vm.Ad = "Ege Gıda";
        vm.Tutar = 150m;

        await vm.KaydetAsync(() => throw new HttpRequestException());
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.Hatalar.Genel);
        Assert.Equal(("Ege Gıda", 150m), (vm.Ad, vm.Tutar));

        await vm.KaydetAsync(() => throw new TimeoutException(KasaZamanAsimlari.Ileti));
        Assert.Contains("tamamlanmış olabilir", vm.Hatalar.Genel);
    }

    [Fact]
    public async Task Baglanti_kopukken_okumanin_baglanti_hatasi_sayfaya_yazilmaz_diger_hatalar_yazilir()
    {
        var vm = new DenemeVm { Kopuk = true };
        await vm.OkuAsync(new HttpRequestException());
        Assert.Null(vm.Hata);
        await vm.OkuAsync(new TimeoutException(KasaZamanAsimlari.Ileti));
        Assert.Null(vm.Hata);
        await vm.OkuAsync(new KasaApiException(HttpStatusCode.Forbidden));
        Assert.Equal("Bu işlem için yetkiniz yok.", vm.Hata);

        vm.Kopuk = false;
        await vm.OkuAsync(new HttpRequestException());
        Assert.Equal("Sunucuya ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.", vm.Hata);
    }
}
```

`Kasa.App.Core.Tests/YurutucuTests.cs` — Bul:

```csharp
        public string? Hata { get; set; }
        public string? Mesaj { get; set; }
        public void IletiyiTemizle() => Mesaj = null;
    }

    // ---- Tekil işlem (yazma, ekran yüklemesi) ----
```

Yerine:

```csharp
        public string? Hata { get; set; }
        public string? Mesaj { get; set; }
        public void IletiyiTemizle() => Mesaj = null;
        public bool BaglantiKopuk => false;
    }

    // ---- Tekil işlem (yazma, ekran yüklemesi) ----
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~FormIsleTests|FullyQualifiedName~YurutucuTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 2 farklı ileti; örnekler:

- `FormIsleTests.cs: CS0115: 'FormIsleTests.DenemeVm.BaglantiKopuk': geçersiz kılmak için uygun yöntem bulunamadı`
- `FormIsleTests.cs: CS0115: 'FormIsleTests.DenemeVm.Formlar': geçersiz kılmak için uygun yöntem bulunamadı`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/TemelViewModel.cs` (1/3) — Bul:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;
```

Yerine:

```csharp
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;
```

`Kasa.App.Core/TemelViewModel.cs` (2/3) — Bul:

```csharp
    protected Yurutucu Yurutucu { get; }

    /// <summary>Tekil işlem (<see cref="Yurutucu.YurutAsync"/>): sürerken ikincisi başlamaz; eskiyen işin hatası ve bitişi yansımaz.</summary>
    protected Task YurutAsync(Func<int, Task> islem, bool mesgulkenBildir = false) => Yurutucu.YurutAsync(islem, mesgulkenBildir);
    protected bool Gecerli(int nesil) => Yurutucu.Gecerli(nesil);

    /// <summary>Yeni işlem başlarken önceki başarı iletisi kalkar; Mesaj taşıyan model geçersiz kılar.</summary>
```

Yerine:

```csharp
    protected Yurutucu Yurutucu { get; }

    /// <summary>Tekil işlem (<see cref="Yurutucu.YurutAsync"/>): sürerken ikincisi başlamaz; eskiyen işin hatası ve bitişi yansımaz.</summary>
    protected Task YurutAsync(Func<int, Task> islem, bool mesgulkenBildir = false, Action<Exception>? hataIsle = null)
        => Yurutucu.YurutAsync(islem, mesgulkenBildir, hataIsle);
    protected bool Gecerli(int nesil) => Yurutucu.Gecerli(nesil);

    /// <summary>Yeni işlem başlarken önceki başarı iletisi kalkar; Mesaj taşıyan model geçersiz kılar.</summary>
```

`Kasa.App.Core/TemelViewModel.cs` (3/3) — Bul:

```csharp

    /// <summary>Salt okuma çağrısının (liste, rapor) hata iletisi; bkz. <see cref="Yurutucu.OkumaHataMesaji"/>.</summary>
    protected static string OkumaHataMesaji(Exception hata) => Yurutucu.OkumaHataMesaji(hata);
}
```

Yerine:

```csharp

    /// <summary>Salt okuma çağrısının (liste, rapor) hata iletisi; bkz. <see cref="Yurutucu.OkumaHataMesaji"/>.</summary>
    protected static string OkumaHataMesaji(Exception hata) => Yurutucu.OkumaHataMesaji(hata);

    /// <summary>Uygulamanın bağlantısı kopuk mu; oturumlu ve rapor modelleri BaglantiDurumu'ndan okur.</summary>
    protected virtual bool BaglantiKopuk => false;
    bool IYurutmeYuzeyi.BaglantiKopuk => BaglantiKopuk;

    /// <summary>Modelin formlarının hataları: bir özellik değişince her formda o adlı alanın hatası kalkar (kullanıcı alanı düzeltti).</summary>
    protected virtual IEnumerable<AlanHatalari> Formlar => [];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        foreach (var form in Formlar)
            form.Temizle(e.PropertyName);
    }

    /// <summary>Formun kaydı (tasarım §1): tekil işlemdir; başlarken formun hataları kalkar. İşlem ön doğrulamada formun alanlarına
    /// yazıp dönebilir (istek gönderilmez). Hata Hata'ya değil forma yazılır (<see cref="FormHatasiniYaz"/>). Bittiğinde formda hata
    /// varsa görünümden ilk hatalı alana kaydırması istenir.</summary>
    /// <param name="eslem">Sunucu alan adı (küçük harf) → formun alanı; verilmezse sunucunun alan hataları genel hataya gider.</param>
    protected Task FormIsleAsync(AlanHatalari form, Func<int, Task> islem, IReadOnlyDictionary<string, string>? eslem = null)
        => YurutAsync(async n =>
        {
            form.Temizle();
            await islem(n);
            if (Gecerli(n))
                form.GosterIste();
        }, hataIsle: hata =>
        {
            FormHatasiniYaz(form, hata, eslem);
            form.GosterIste();
        });

    /// <summary>Kayıt hatasını forma yazar: sunucunun alan hataları eşlenen alanlara, eşlenemeyenler genel hataya; istek sunucuya
    /// ulaşamadıysa <see cref="Yurutucu.KayitBaglantiIletisi"/>; diğerleri <see cref="Yurutucu.HataMesaji"/> ile genel hataya.
    /// Zaman aşımı bağlantı iletisi almaz: istek sunucuya ulaşmış ve kayıt tamamlanmış olabilir.</summary>
    public static void FormHatasiniYaz(AlanHatalari form, Exception hata, IReadOnlyDictionary<string, string>? eslem = null)
    {
        if (hata is KasaApiException { AlanHatalari.Count: > 0 } api && eslem is not null)
        {
            var kalan = form.SunucuHatalariniYaz(api.AlanHatalari, eslem);
            form.Genel = kalan.Count > 0 ? string.Join("\n", kalan) : null;
            return;
        }
        form.Genel = hata is HttpRequestException ? Yurutucu.KayitBaglantiIletisi : HataMesaji(hata);
    }
}
```

`Kasa.App.Core/Yurutucu.cs` (1/6) — Bul:

```csharp
    string? Hata { get; set; }
    /// <summary>Yeni işlem başlarken önceki işlemin başarı iletisini (Mesaj) kaldırır; iletisi olmayan modelde boştur.</summary>
    void IletiyiTemizle();
}

/// <summary>Görünüm modellerinin tek async yürütme deseni (appcore-10). Her ekran aynı garantileri buradan alır:
```

Yerine:

```csharp
    string? Hata { get; set; }
    /// <summary>Yeni işlem başlarken önceki işlemin başarı iletisini (Mesaj) kaldırır; iletisi olmayan modelde boştur.</summary>
    void IletiyiTemizle();
    /// <summary>Uygulamanın bağlantı durumu kopuk mu (BaglantiDurumu). Kopukken okumanın bağlantı hatası sayfaya yazılmaz:
    /// kabuktaki şerit tek yerde söyler.</summary>
    bool BaglantiKopuk { get; }
}

/// <summary>Görünüm modellerinin tek async yürütme deseni (appcore-10). Her ekran aynı garantileri buradan alır:
```

`Kasa.App.Core/Yurutucu.cs` (2/6) — Bul:

```csharp
    /// <summary>Tekil işlem: Mesgul iken çalışmaz. Varsayılan sessizce dönmektir (çift tıklamanın ikinci basışı iletiyle
    /// karışmasın); <paramref name="mesgulkenBildir"/> onaydan sonra gelen işlemde (silme) yapılmadığını Hata'ya yazar. Göstergeyi
    /// yalnız yüzeydeki okuma tutuyorsa işlem engellenmez: okuma eskitilip iptal edilir (bkz. <see cref="Yurutucu"/>).
    /// <paramref name="islem"/> başladığı nesli alır; sonucu yazmadan önce <see cref="Gecerli"/> ile denetler.</summary>
    public async Task YurutAsync(Func<int, Task> islem, bool mesgulkenBildir = false)
    {
        if (yuzey.Mesgul)
        {
```

Yerine:

```csharp
    /// <summary>Tekil işlem: Mesgul iken çalışmaz. Varsayılan sessizce dönmektir (çift tıklamanın ikinci basışı iletiyle
    /// karışmasın); <paramref name="mesgulkenBildir"/> onaydan sonra gelen işlemde (silme) yapılmadığını Hata'ya yazar. Göstergeyi
    /// yalnız yüzeydeki okuma tutuyorsa işlem engellenmez: okuma eskitilip iptal edilir (bkz. <see cref="Yurutucu"/>).
    /// <paramref name="islem"/> başladığı nesli alır; sonucu yazmadan önce <see cref="Gecerli"/> ile denetler.
    /// <paramref name="hataIsle"/> verilirse geçerli işlemin hatası Hata'ya değil ona gider (form hataları, okuma hatası).</summary>
    public async Task YurutAsync(Func<int, Task> islem, bool mesgulkenBildir = false, Action<Exception>? hataIsle = null)
    {
        if (yuzey.Mesgul)
        {
```

`Kasa.App.Core/Yurutucu.cs` (3/6) — Bul:

```csharp
        yuzey.IletiyiTemizle();
        try
        { await islem(nesil); }
        catch (Exception hata) { if (Gecerli(nesil)) yuzey.Hata = HataMesaji(hata); }
        finally
        {
            if (Gecerli(nesil))
```

Yerine:

```csharp
        yuzey.IletiyiTemizle();
        try
        { await islem(nesil); }
        catch (Exception hata) when (Gecerli(nesil))
        {
            if (hataIsle is null)
                yuzey.Hata = HataMesaji(hata);
            else
                hataIsle(hata);
        }
        catch (Exception) { /* eskiyen işin hatası yansımaz */ }
        finally
        {
            if (Gecerli(nesil))
```

`Kasa.App.Core/Yurutucu.cs` (4/6) — Bul:

```csharp
        }
    }

    /// <summary>Yüzeyde yükleyen okuma başladı: göstergeyi (tekil işlemle birlikte) tutar.</summary>
    internal void OkumaBasladi(SonIstekHatti hat, IstekBileti bilet) { _okuyanHat = hat; _okumaBileti = bilet; }
```

Yerine:

```csharp
        }
    }

    /// <summary>Sunucuya ulaşılamadı ya da süre sınırında yanıt gelmedi: istemci bu hatalarda bağlantıyı kopuk bildirir
    /// (Kasa.ApiClient.IBaglantiBildirimleri).</summary>
    public static bool BaglantiHatasi(Exception hata) => hata is HttpRequestException or TimeoutException;

    /// <summary>Kopukken kaydetme denendi ve istek sunucuya ulaşamadı: form ve girilen değerler korunur (tasarım §3).</summary>
    public const string KayitBaglantiIletisi = "Sunucuya ulaşılamadı. Kayıt yapılmadı; bağlantı gelince yeniden kaydedin.";

    /// <summary>Okuma hatasını sayfanın hatasına yazar; bağlantı kopukken bağlantı hatası yazılmaz (kabuk şeridi söyler).</summary>
    public void OkumaHatasiniYaz(Exception hata)
    {
        if (BaglantiHatasi(hata) && yuzey.BaglantiKopuk)
            return;
        yuzey.Hata = OkumaHataMesaji(hata);
    }

    /// <summary>Yüzeyde yükleyen okuma başladı: göstergeyi (tekil işlemle birlikte) tutar.</summary>
    internal void OkumaBasladi(SonIstekHatti hat, IstekBileti bilet) { _okuyanHat = hat; _okumaBileti = bilet; }
```

`Kasa.App.Core/Yurutucu.cs` (5/6) — Bul:

```csharp
    /// <summary>Yürütücünün yüzeyinde (Mesgul, Hata) okuma: başlarken Hata ve ileti temizlenir; yalnız son isteğin sonucu
    /// uygulanır, hatası yazılır ve bitişi Mesgul'u indirir. Bu hattın iptali hata sayılmaz. Tekil işlemle birlikte: sürerken
    /// başlayan tekil işlem bu okumayı eskitir; tekil işlem sürerken başlayan okuma yazmanın hatasını ve iletisini temizlemez,
    /// göstergeyi yazma bitene dek indirmez (bkz. <see cref="Yurutucu"/>).</summary>
    public async Task YukleAsync<T>(Func<CancellationToken, Task<T>> getir, Action<T> uygula)
    {
        var yuzey = yurutucu.Yuzey;
        var bilet = Baslat();
```

Yerine:

```csharp
    /// <summary>Yürütücünün yüzeyinde (Mesgul, Hata) okuma: başlarken Hata ve ileti temizlenir; yalnız son isteğin sonucu
    /// uygulanır, hatası yazılır ve bitişi Mesgul'u indirir. Bu hattın iptali hata sayılmaz. Tekil işlemle birlikte: sürerken
    /// başlayan tekil işlem bu okumayı eskitir; tekil işlem sürerken başlayan okuma yazmanın hatasını ve iletisini temizlemez,
    /// göstergeyi yazma bitene dek indirmez (bkz. <see cref="Yurutucu"/>). Hata <see cref="Yurutucu.OkumaHatasiniYaz"/> ile yazılır;
    /// <paramref name="hataIsle"/> verilirse güncel isteğin hatası (iptal dışında) ona gider (son veriyi eski işaretlemek için).</summary>
    public async Task YukleAsync<T>(Func<CancellationToken, Task<T>> getir, Action<T> uygula, Action<Exception>? hataIsle = null)
    {
        var yuzey = yurutucu.Yuzey;
        var bilet = Baslat();
```

`Kasa.App.Core/Yurutucu.cs` (6/6) — Bul:

```csharp
            uygula(veri);
        }
        catch (OperationCanceledException) when (bilet.Iptal.IsCancellationRequested) { /* vazgeçildi: hata değil */ }
        catch (Exception hata) { if (Guncel(bilet)) yuzey.Hata = Yurutucu.OkumaHataMesaji(hata); }
        finally
        {
            if (Guncel(bilet))
```

Yerine:

```csharp
            uygula(veri);
        }
        catch (OperationCanceledException) when (bilet.Iptal.IsCancellationRequested) { /* vazgeçildi: hata değil */ }
        catch (Exception hata) when (Guncel(bilet)) { (hataIsle ?? yurutucu.OkumaHatasiniYaz)(hata); }
        catch (Exception) { /* eskiyen isteğin hatası yansımaz */ }
        finally
        {
            if (Guncel(bilet))
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1097`, `Başarısız:     0`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/FormIsleTests.cs Kasa.App.Core.Tests/YurutucuTests.cs Kasa.App.Core/TemelViewModel.cs Kasa.App.Core/Yurutucu.cs
git commit -F - <<'MESAJ'
feat(app-core): ortak form kaydı ve kopukken okuma hatası

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 5: App.Core — kaydedilmemiş değişiklik ve bırakma onayı

`KaydedilmemisDegisiklik` (açılış değerleriyle karşılaştırma), `IKaydedilmemisForm` ve `OturumluViewModel.BirakilabilirAsync`.

**Dosyalar:**
- Oluştur: `Kasa.App.Core/KaydedilmemisDegisiklik.cs`
- Değiştir: `Kasa.App.Core/OturumluViewModel.cs`
- Test (oluştur): `Kasa.App.Core.Tests/KaydedilmemisDegisiklikTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/KaydedilmemisDegisiklikTests.cs` (yeni dosya):

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core.Tests;

/// <summary>Kaydedilmemiş değişiklik ölçütü (form açıldığı andaki değerlerden farklı) ve bırakma onayı.</summary>
public partial class KaydedilmemisDegisiklikTests
{
    private sealed partial class FormVm : OturumluViewModel
    {
        public FormVm() : base(TestOturumu.Ac()) => Degisiklik = new(() => new { Ad, Tutar });
        public KaydedilmemisDegisiklik Degisiklik { get; }
        [ObservableProperty] private string _ad = "";
        [ObservableProperty] private decimal _tutar;
        public Task<bool> BirakilabilirMi() => BirakilabilirAsync(Degisiklik);
        protected override void OturumTemizle() { }
    }

    [Fact]
    public void Acilmamis_form_degisiklik_saymaz_acilinca_farki_gorur_geri_donunce_kalkar()
    {
        var vm = new FormVm();
        vm.Ad = "Ege";
        Assert.False(vm.Degisiklik.Var);

        vm.Degisiklik.Ac();
        Assert.False(vm.Degisiklik.Var);
        vm.Tutar = 10m;
        Assert.True(vm.Degisiklik.Var);
        vm.Tutar = 0m;
        Assert.False(vm.Degisiklik.Var);

        vm.Ad = "Başka";
        vm.Degisiklik.Kapat();
        Assert.False(vm.Degisiklik.Var);
        Assert.False(vm.Degisiklik.Acik);
    }

    [Fact]
    public async Task Degisiklik_varken_onay_sorulur_onaysiz_model_birakmaz()
    {
        var vm = new FormVm();
        vm.Degisiklik.Ac();
        Assert.True(await vm.BirakilabilirMi());

        vm.Ad = "Yazıldı";
        Assert.False(await vm.BirakilabilirMi());   // onay bağlı değil: form korunur

        var sorulan = new List<string>();
        vm.BirakmaOnayi = ileti => { sorulan.Add(ileti); return Task.FromResult(false); };
        Assert.False(await vm.BirakilabilirMi());
        vm.BirakmaOnayi = ileti => { sorulan.Add(ileti); return Task.FromResult(true); };
        Assert.True(await vm.BirakilabilirMi());
        Assert.Equal([KaydedilmemisDegisiklik.Ileti, KaydedilmemisDegisiklik.Ileti], sorulan);
        Assert.Equal("Kaydedilmemiş değişiklik var. Bırakılsın mı?", KaydedilmemisDegisiklik.Ileti);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KaydedilmemisDegisiklikTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 1 farklı ileti; örnekler:

- `KaydedilmemisDegisiklikTests.cs: CS0246: 'KaydedilmemisDegisiklik' türü veya ad alanı adı bulunamadı (bir using yönergeniz veya derleme başvurunuz mu eksik?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/KaydedilmemisDegisiklik.cs` (yeni dosya):

```csharp
using System.Text.Json;

namespace Kasa.App.Core;

/// <summary>
/// Formda kaydedilmemiş değişiklik var mı (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §2): form açıldığı andaki
/// değerlerden farklıysa vardır. <paramref name="durum"/> formun karşılaştırılan değerlerini verir (anonim nesne ya da kayıt; JSON
/// ile karşılaştırılır). Form açılınca (yeni, düzenleme, kaydedildi) <see cref="Ac"/>, kapanınca <see cref="Kapat"/> çağrılır;
/// kapalı formda değişiklik sayılmaz.
/// </summary>
public sealed class KaydedilmemisDegisiklik(Func<object?> durum)
{
    public const string Baslik = "Kaydedilmemiş değişiklik";
    public const string Ileti = "Kaydedilmemiş değişiklik var. Bırakılsın mı?";
    public const string Birak = "Bırak";
    public const string FormaDon = "Forma dön";

    private string? _acilis;

    /// <summary>Form bu değerlerle açıldı: şimdiki değerler karşılaştırmanın tabanıdır.</summary>
    public void Ac() => _acilis = Anlik();

    /// <summary>Form kapandı: değişiklik sayılmaz.</summary>
    public void Kapat() => _acilis = null;

    public bool Acik => _acilis is not null;

    /// <summary>Form açık ve değerleri açıldığı andakinden farklı.</summary>
    public bool Var => _acilis is not null && Anlik() != _acilis;

    private string Anlik() => JsonSerializer.Serialize(durum());
}

/// <summary>Kaydedilmemiş değişikliği olabilen ekran modeli: kabuk sayfadan çıkışta onay sorar ("Bırak" seçilirse
/// <see cref="DegisiklikleriBirak"/>).</summary>
public interface IKaydedilmemisForm
{
    bool KaydedilmemisDegisiklikVar { get; }

    /// <summary>Yazılmış değişiklikleri bırakır: form açıldığı hale döner ya da kapanır.</summary>
    void DegisiklikleriBirak();
}
```

`Kasa.App.Core/OturumluViewModel.cs` — Bul:

```csharp
        await islem(gerekce, oturum);
    }

    protected override void IletiyiTemizle() => Mesaj = null;
    protected void BekleyenleriIptalEt() { Yurutucu.GecersizKil(); Mesgul = false; }
    protected abstract void OturumTemizle();
```

Yerine:

```csharp
        await islem(gerekce, oturum);
    }

    /// <summary>"Kaydedilmemiş değişiklik var. Bırakılsın mı?" onayı (Bırak: true, Forma dön: false). Sayfa bağlar
    /// (DisplayAlertAsync); bağlı değilse yazılmış form bırakılmaz.</summary>
    public Func<string, Task<bool>>? BirakmaOnayi { get; set; }

    /// <summary>Başka kayda geçiş, Yeni ve Vazgeç'ten önce (tasarım §2): form değişmediyse ya da kullanıcı "Bırak" derse true.</summary>
    protected async Task<bool> BirakilabilirAsync(KaydedilmemisDegisiklik form)
        => !form.Var || (BirakmaOnayi is { } sor && await sor(KaydedilmemisDegisiklik.Ileti));

    protected override void IletiyiTemizle() => Mesaj = null;
    protected void BekleyenleriIptalEt() { Yurutucu.GecersizKil(); Mesgul = false; }
    protected abstract void OturumTemizle();
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1099`, `Başarısız:     0`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/KaydedilmemisDegisiklikTests.cs Kasa.App.Core/KaydedilmemisDegisiklik.cs Kasa.App.Core/OturumluViewModel.cs
git commit -F - <<'MESAJ'
feat(app-core): kaydedilmemiş değişiklik izleyicisi ve bırakma onayı

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 6: App.Core — tek bağlantı durumu

`BaglantiDurumu` tekil hizmeti, `AuthViewModel.Baglanti`, rapor modellerinin bağlantı durumu ve DI kaydı.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/AuthViewModel.cs`
- Değiştir: `Kasa.App.Core/AylikViewModel.cs`
- Oluştur: `Kasa.App.Core/BaglantiDurumu.cs`
- Değiştir: `Kasa.App.Core/HaftalikViewModel.cs`
- Değiştir: `Kasa.App.Core/OturumluViewModel.cs`
- Değiştir: `Kasa.App.Core/PanelViewModel.cs`
- Değiştir: `Kasa.App.Core/RaporViewModel.cs`
- Değiştir: `Kasa.App/MauiProgram.cs`
- Test (oluştur): `Kasa.App.Core.Tests/BaglantiDurumuTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/BaglantiDurumuTests.cs` (yeni dosya):

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Tek bağlantı durumu: istemcinin ağ hatası kopuk, yanıtı bağlı yapar; kopuktan bağlıya dönüş bir kez bildirilir;
/// kopukken sayfaların okuma bağlantı hatası yazılmaz.</summary>
public class BaglantiDurumuTests
{
    private sealed class SahteBildirimler : IBaglantiBildirimleri
    {
        public event EventHandler? SunucuyaUlasildi;
        public event EventHandler<Exception>? SunucuyaUlasilamadi;
        public void Ulas() => SunucuyaUlasildi?.Invoke(this, EventArgs.Empty);
        public void Kop() => SunucuyaUlasilamadi?.Invoke(this, new HttpRequestException());
    }

    private static (BaglantiDurumu Durum, SahteBildirimler Istemci, List<string> Bildirimler) Kur()
    {
        var istemci = new SahteBildirimler();
        var durum = new BaglantiDurumu(istemci, new IslemEditorTests.SabitZaman(new DateOnly(2026, 10, 2)));
        var bildirimler = new List<string>();
        durum.PropertyChanged += (_, e) => bildirimler.Add(e.PropertyName!);
        durum.BaglantiGeldi += (_, _) => bildirimler.Add("geldi");
        return (durum, istemci, bildirimler);
    }

    [Fact]
    public void Ag_hatasi_kopuk_yapar_bir_kez_bildirir()
    {
        var (durum, istemci, bildirimler) = Kur();
        Assert.False(durum.Kopuk);

        istemci.Kop();
        istemci.Kop();

        Assert.True(durum.Kopuk);
        Assert.Equal(["Kopuk"], bildirimler);
        Assert.Equal("Sunucuya ulaşılamıyor", durum.SeritMetni);
    }

    [Fact]
    public void Yanit_bagli_yapar_kopuktan_donus_bir_kez_bildirilir_son_baglanti_yazilir()
    {
        var (durum, istemci, bildirimler) = Kur();
        istemci.Ulas();
        Assert.Equal(["SonBaglanti", "SeritMetni"], bildirimler);

        bildirimler.Clear();
        istemci.Kop();
        istemci.Ulas();

        Assert.False(durum.Kopuk);
        Assert.Equal(["Kopuk", "SonBaglanti", "SeritMetni", "Kopuk", "geldi"], bildirimler);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero), durum.SonBaglanti);

        istemci.Kop();
        Assert.Equal("Sunucuya ulaşılamıyor · Son bağlantı 12:00", durum.SeritMetni);
    }

    [Fact]
    public void Oturum_bagimli_modeller_auth_uzerinden_ayni_durumu_okur()
    {
        var durum = new BaglantiDurumu();
        var auth = new AuthViewModel(new SahteApi(), durum);
        Assert.Same(durum, auth.Baglanti);
        Assert.NotNull(new AuthViewModel(new SahteApi()).Baglanti);
    }

    [Fact]
    public async Task Kopukken_rapor_okumasinin_baglanti_hatasi_sayfaya_yazilmaz()
    {
        var durum = new BaglantiDurumu();
        var vm = new PanelViewModel(new SahteApi { YuklemeHatasi = new HttpRequestException() }, durum);
        durum.Ulasilamadi();

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);

        durum.Ulasildi();
        await vm.YukleAsync();
        Assert.Contains("Sunucuya ulaşılamadı", vm.Hata);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~BaglantiDurumuTests|FullyQualifiedName~MauiKayitTutarliligiTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 1 farklı ileti; örnekler:

- `BaglantiDurumuTests.cs: CS0246: 'BaglantiDurumu' türü veya ad alanı adı bulunamadı (bir using yönergeniz veya derleme başvurunuz mu eksik?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/AuthViewModel.cs` (1/2) — Bul:

```csharp
{
    private readonly IKasaApi _api;

    public AuthViewModel(IKasaApi api)
    {
        _api = api;
        if (api is IOturumBildirimleri bildirimler)
            bildirimler.OturumSonlandi += (_, e) =>
            {
```

Yerine:

```csharp
{
    private readonly IKasaApi _api;

    /// <param name="baglanti">Uygulamanın tek bağlantı durumu (DI'da tekil); verilmezse istemcinin bildirimleriyle yenisi kurulur.</param>
    public AuthViewModel(IKasaApi api, BaglantiDurumu? baglanti = null)
    {
        _api = api;
        Baglanti = baglanti ?? new BaglantiDurumu(api as IBaglantiBildirimleri);
        if (api is IOturumBildirimleri bildirimler)
            bildirimler.OturumSonlandi += (_, e) =>
            {
```

`Kasa.App.Core/AuthViewModel.cs` (2/2) — Bul:

```csharp

    public event EventHandler? OturumSonlandi;

    [ObservableProperty] private string? _kullanici;
    [ObservableProperty] private string _sifre = "";
    [ObservableProperty] private string? _hata;
```

Yerine:

```csharp

    public event EventHandler? OturumSonlandi;

    /// <summary>Uygulamanın bağlantı durumu; oturumlu ekranlar (OturumluViewModel) buradan okur.</summary>
    public BaglantiDurumu Baglanti { get; }

    [ObservableProperty] private string? _kullanici;
    [ObservableProperty] private string _sifre = "";
    [ObservableProperty] private string? _hata;
```

`Kasa.App.Core/AylikViewModel.cs` — Bul:

```csharp
public partial class AylikViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public AylikViewModel(IKasaApi api)
    {
        _api = api;
        var bugun = DateTime.Today;
```

Yerine:

```csharp
public partial class AylikViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public AylikViewModel(IKasaApi api, BaglantiDurumu? baglanti = null) : base(baglanti)
    {
        _api = api;
        var bugun = DateTime.Today;
```

`Kasa.App.Core/BaglantiDurumu.cs` (yeni dosya):

```csharp
using System.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Uygulamanın tek bağlantı durumu (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3). API istemcisinin ağ hatası ve
/// süre sınırı durumu kopuk, alınan her yanıt bağlı yapar (<see cref="IBaglantiBildirimleri"/>). <see cref="Kopuk"/> bildirim
/// geldiği an değişir (okuma hatası sayfaya yazılırken doğru okunur); değişiklik bildirimleri ve <see cref="BaglantiGeldi"/>
/// modelin kurulduğu UI bağlamında gelir. Kabuk şeridi (BaglantiSeridi) buna bağlanır.
/// </summary>
public sealed class BaglantiDurumu : INotifyPropertyChanged
{
    private readonly TimeProvider _zaman;
    private readonly SynchronizationContext? _ui;
    private volatile bool _kopuk;
    private DateTimeOffset? _sonBaglanti;

    /// <param name="bildirimler">API istemcisi; verilmezse durum yalnız <see cref="Ulasildi"/> / <see cref="Ulasilamadi"/> ile değişir.</param>
    /// <param name="zaman">Son bağlantı saati; verilmezse sistem saati.</param>
    public BaglantiDurumu(IBaglantiBildirimleri? bildirimler = null, TimeProvider? zaman = null)
    {
        _zaman = zaman ?? TimeProvider.System;
        _ui = SynchronizationContext.Current;
        if (bildirimler is null)
            return;
        bildirimler.SunucuyaUlasildi += (_, _) => Ulasildi();
        bildirimler.SunucuyaUlasilamadi += (_, _) => Ulasilamadi();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Bağlantı kopuktan bağlıya döndü: açık sayfa bir kez yenilenir (kabuk dinler).</summary>
    public event EventHandler? BaglantiGeldi;

    /// <summary>Son istek sunucuya ulaşamadı (ağ hatası ya da süre sınırı).</summary>
    public bool Kopuk => _kopuk;

    /// <summary>Sunucudan son yanıtın alındığı an (yerel saat); hiç yanıt yoksa null.</summary>
    public DateTimeOffset? SonBaglanti => _sonBaglanti;

    /// <summary>Kabuk şeridinin metni: "Sunucuya ulaşılamıyor · Son bağlantı 14:05".</summary>
    public string SeritMetni => _sonBaglanti is { } zaman ? $"Sunucuya ulaşılamıyor · Son bağlantı {zaman:HH:mm}" : "Sunucuya ulaşılamıyor";

    public void Ulasildi()
    {
        var oncedenKopuk = _kopuk;
        _sonBaglanti = _zaman.GetLocalNow();
        _kopuk = false;
        UiBaglaminda(() =>
        {
            Bildir(nameof(SonBaglanti));
            Bildir(nameof(SeritMetni));
            if (!oncedenKopuk)
                return;
            Bildir(nameof(Kopuk));
            BaglantiGeldi?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Ulasilamadi()
    {
        if (_kopuk)
            return;
        _kopuk = true;
        UiBaglaminda(() => Bildir(nameof(Kopuk)));
    }

    private void UiBaglaminda(Action eylem)
    {
        if (_ui is not null && SynchronizationContext.Current != _ui)
            _ui.Post(_ => eylem(), null);
        else
            eylem();
    }

    private void Bildir(string ad) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ad));
}
```

`Kasa.App.Core/HaftalikViewModel.cs` — Bul:

```csharp
public partial class HaftalikViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public HaftalikViewModel(IKasaApi api) => _api = api;

    public ObservableCollection<HaftalikSatir> Donemler { get; } = new();
    /// <summary>Sunucunun veri sağlığı uyarısı (ör. rapor ufkunun ötesinde tarihli kayıt). Yalnız son dönemde gelir ama
```

Yerine:

```csharp
public partial class HaftalikViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public HaftalikViewModel(IKasaApi api, BaglantiDurumu? baglanti = null) : base(baglanti) => _api = api;

    public ObservableCollection<HaftalikSatir> Donemler { get; } = new();
    /// <summary>Sunucunun veri sağlığı uyarısı (ör. rapor ufkunun ötesinde tarihli kayıt). Yalnız son dönemde gelir ama
```

`Kasa.App.Core/OturumluViewModel.cs` — Bul:

```csharp
        => !form.Var || (BirakmaOnayi is { } sor && await sor(KaydedilmemisDegisiklik.Ileti));

    protected override void IletiyiTemizle() => Mesaj = null;
    protected void BekleyenleriIptalEt() { Yurutucu.GecersizKil(); Mesgul = false; }
    protected abstract void OturumTemizle();
    protected void Tamamlandi() { VeriHazir = true; SonGuncelleme = DateTimeOffset.Now; }
```

Yerine:

```csharp
        => !form.Var || (BirakmaOnayi is { } sor && await sor(KaydedilmemisDegisiklik.Ileti));

    protected override void IletiyiTemizle() => Mesaj = null;
    protected override bool BaglantiKopuk => Auth.Baglanti.Kopuk;
    protected void BekleyenleriIptalEt() { Yurutucu.GecersizKil(); Mesgul = false; }
    protected abstract void OturumTemizle();
    protected void Tamamlandi() { VeriHazir = true; SonGuncelleme = DateTimeOffset.Now; }
```

`Kasa.App.Core/PanelViewModel.cs` — Bul:

```csharp
public partial class PanelViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private decimal _guncelKasa;
    [ObservableProperty] private decimal _buHaftaSonucu;
```

Yerine:

```csharp
public partial class PanelViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api, BaglantiDurumu? baglanti = null) : base(baglanti) => _api = api;

    [ObservableProperty] private decimal _guncelKasa;
    [ObservableProperty] private decimal _buHaftaSonucu;
```

`Kasa.App.Core/RaporViewModel.cs` — Bul:

```csharp
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private DateTimeOffset? _sonGuncelleme;

    protected RaporViewModel() => _hat = new SonIstekHatti(Yurutucu);

    public string SonGuncellemeMetni => SonGuncelleme is { } zaman
        ? $"Son başarılı güncelleme: {zaman:dd.MM.yyyy HH:mm:ss}"
```

Yerine:

```csharp
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private DateTimeOffset? _sonGuncelleme;

    private readonly BaglantiDurumu? _baglanti;

    /// <param name="baglanti">Uygulamanın bağlantı durumu; kopukken okumanın bağlantı hatası sayfaya yazılmaz.</param>
    protected RaporViewModel(BaglantiDurumu? baglanti = null)
    {
        _baglanti = baglanti;
        _hat = new SonIstekHatti(Yurutucu);
    }

    protected override bool BaglantiKopuk => _baglanti?.Kopuk == true;

    public string SonGuncellemeMetni => SonGuncelleme is { } zaman
        ? $"Son başarılı güncelleme: {zaman:dd.MM.yyyy HH:mm:ss}"
```

`Kasa.App/MauiProgram.cs` — Bul:

```csharp
        builder.Services.AddSingleton<IAylikGiderApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IEkstreAktarmaApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<ICekApi>(sp => sp.GetRequiredService<KasaApiClient>());

        // Masaüstü Windows bildirimleri (tasarım 2026-09-30): yerel dosyalar %LOCALAPPDATA%\EmarKasa altında; gösterici ve tıklama
        // kuyruğu Windows katmanının tek örneğidir (Platforms/Windows/App.xaml.cs onu DI kurulmadan önce başlatır).
```

Yerine:

```csharp
        builder.Services.AddSingleton<IAylikGiderApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<IEkstreAktarmaApi>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<ICekApi>(sp => sp.GetRequiredService<KasaApiClient>());
        // Tek bağlantı durumu (tasarım 2026-10-02 §3): istemcinin ağ hatası kopuk, her yanıt bağlı yapar; kabuk şeridi ve
        // ekranlar (AuthViewModel, rapor modelleri) aynı örneği okur.
        builder.Services.AddSingleton<IBaglantiBildirimleri>(sp => sp.GetRequiredService<KasaApiClient>());
        builder.Services.AddSingleton<BaglantiDurumu>();

        // Masaüstü Windows bildirimleri (tasarım 2026-09-30): yerel dosyalar %LOCALAPPDATA%\EmarKasa altında; gösterici ve tıklama
        // kuyruğu Windows katmanının tek örneğidir (Platforms/Windows/App.xaml.cs onu DI kurulmadan önce başlatır).
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1103`, `Başarısız:     0`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/BaglantiDurumuTests.cs Kasa.App.Core/AuthViewModel.cs Kasa.App.Core/AylikViewModel.cs Kasa.App.Core/BaglantiDurumu.cs Kasa.App.Core/HaftalikViewModel.cs Kasa.App.Core/OturumluViewModel.cs Kasa.App.Core/PanelViewModel.cs Kasa.App.Core/RaporViewModel.cs Kasa.App/MauiProgram.cs
git commit -F - <<'MESAJ'
feat(app-core): tek bağlantı durumu hizmeti

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 7: App.Core — son başarılı veriyi koruma

`VeriEski`, `SonGuncellemeMetni` ("Henüz yüklenmedi."), `GovdeGorunur`, `VeriYukleAsync`; `RaporViewModel` hatada veriyi silmez, `Yuklendi` olayı; Aylık'ta başka aya geçince eski ay gizlenir; Kasalar alt bölümleri `Yuklendi`'yi dinler.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/AylikViewModel.cs`
- Değiştir: `Kasa.App.Core/Bicim.cs`
- Değiştir: `Kasa.App.Core/HaftalikViewModel.cs`
- Değiştir: `Kasa.App.Core/IslemlerViewModel.cs`
- Değiştir: `Kasa.App.Core/OturumluViewModel.cs`
- Değiştir: `Kasa.App.Core/PanelViewModel.cs`
- Değiştir: `Kasa.App.Core/RaporViewModel.cs`
- Değiştir: `Kasa.App/Views/PanelPage.xaml.cs`
- Test (değiştir): `Kasa.App.Core.Tests/AnaSayfaVeRaporIptalTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/RaporDurumuTests.cs`
- Test (oluştur): `Kasa.App.Core.Tests/SonVeriTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/AnaSayfaVeRaporIptalTests.cs` — Bul:

```csharp
        await vm.YukleAsync();
        Assert.Null(vm.VeriSagligiUyarisi);

        api.HaftalikGetir = _ => Task.FromException<IReadOnlyList<HaftalikOzetDto>>(new HttpRequestException());
        await vm.YukleAsync();
        Assert.Null(vm.VeriSagligiUyarisi);
        Assert.False(vm.VeriVar);
    }

    /// <summary>"Dağılım bekleyen" yalnız tutar sıfırdan farklı dönemde görünür (her satırda "0,00 ₺" yazmaz); tutar
```

Yerine:

```csharp
        await vm.YukleAsync();
        Assert.Null(vm.VeriSagligiUyarisi);

        // Hata son başarılı raporu silmez (tasarım 2026-10-02 §3): veri eski işaretlenir.
        api.HaftalikGetir = _ => Task.FromException<IReadOnlyList<HaftalikOzetDto>>(new HttpRequestException());
        await vm.YukleAsync();
        Assert.Null(vm.VeriSagligiUyarisi);
        Assert.True(vm.VeriVar);
        Assert.True(vm.VeriEski);
        Assert.Equal(2, vm.Donemler.Count);
    }

    /// <summary>"Dağılım bekleyen" yalnız tutar sıfırdan farklı dönemde görünür (her satırda "0,00 ₺" yazmaz); tutar
```

`Kasa.App.Core.Tests/RaporDurumuTests.cs` — Bul:

```csharp
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Panel_yenilenirken_ve_hatada_onceki_bakiye_gizlenir()
    {
        var api = new SahteApi { Panel = new PanelDto(123m, new List<KanalBakiyeDto>(), 0, 0) };
        var vm = new PanelViewModel(api);
        await vm.YukleAsync();
        Assert.True(vm.VeriVar);
        var bekleyen = new TaskCompletionSource<PanelDto>();
        api.PanelGetir = () => bekleyen.Task;
        var yenile = vm.YukleAsync();
        Assert.False(vm.VeriVar);
        Assert.True(vm.Mesgul);
        bekleyen.SetException(new HttpRequestException());
        await yenile;
        Assert.False(vm.VeriVar);
        Assert.False(vm.Mesgul);
        Assert.NotNull(vm.SonGuncelleme);
    }

    [Theory]
```

Yerine:

```csharp
        Assert.NotNull(vm.Hata);
    }

    /// <summary>HD-01: yenileme ve hata son başarılı bakiyeyi silmez; hata sonrası veri eski işaretlenir, başarı işareti kaldırır.</summary>
    [Fact]
    public async Task Panel_yenilenirken_ve_hatada_onceki_bakiye_korunur_ve_eski_isaretlenir()
    {
        var api = new SahteApi { Panel = new PanelDto(123m, new List<KanalBakiyeDto>(), 0, 0) };
        var vm = new PanelViewModel(api);
        var yuklendi = 0;
        vm.Yuklendi += (_, _) => yuklendi++;
        await vm.YukleAsync();
        Assert.True(vm.VeriVar);
        Assert.Equal(1, yuklendi);
        var bekleyen = new TaskCompletionSource<PanelDto>();
        api.PanelGetir = () => bekleyen.Task;
        var yenile = vm.YukleAsync();
        Assert.True(vm.VeriVar);
        Assert.True(vm.Mesgul);
        bekleyen.SetException(new HttpRequestException());
        await yenile;
        Assert.True(vm.VeriVar);
        Assert.True(vm.VeriEski);
        Assert.Equal(123m, vm.GuncelKasa);
        Assert.False(vm.Mesgul);
        Assert.NotNull(vm.Hata);
        Assert.EndsWith(" · güncel olmayabilir", vm.SonGuncellemeMetni);
        Assert.Equal(1, yuklendi);

        api.PanelGetir = null;
        await vm.YukleAsync();
        Assert.False(vm.VeriEski);
        Assert.DoesNotContain("güncel olmayabilir", vm.SonGuncellemeMetni);
        Assert.Equal(2, yuklendi);
    }

    [Fact]
    public void Hic_yukleme_yokken_iki_sayfa_ailesi_ayni_metni_yazar()
    {
        Assert.Equal("Henüz yüklenmedi.", new HaftalikViewModel(new SahteApi()).SonGuncellemeMetni);
        Assert.Equal("Henüz yüklenmedi.", new KrediTakipViewModel(new FinansTakipTests.Sahte(), new SahteApi(), TestOturumu.Ac()).SonGuncellemeMetni);
    }

    [Fact]
    public async Task Ayni_ayin_yenilemesinde_rapor_korunur_ay_degisince_kalkar()
    {
        var api = new SahteApi { AylikRapor = Ay(8) };
        var vm = new AylikViewModel(api) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();

        await vm.YenileCommand.ExecuteAsync(null);
        Assert.Equal(8, vm.Rapor!.Ay);
        Assert.True(vm.VeriVar);
        Assert.True(vm.VeriEski);

        vm.Ay = 9;
        Assert.Null(vm.Rapor);
        Assert.False(vm.VeriVar);
        Assert.False(vm.VeriEski);
    }

    [Theory]
```

`Kasa.App.Core.Tests/SonVeriTests.cs` (yeni dosya):

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Oturumlu ekranların ortak yüklemesi (OturumluViewModel.VeriYukleAsync): hata son başarılı veriyi silmez, veri eski
/// işaretlenir ve gövde görünür kalır; bağlantı kopukken bağlantı hatası sayfaya yazılmaz.</summary>
public class SonVeriTests
{
    private sealed class YukleyenVm(AuthViewModel auth) : OturumluViewModel(auth)
    {
        public List<string> Veri { get; } = [];
        public Task YukleAsync(Func<Task<string>> getir) => VeriYukleAsync(async n =>
        {
            var v = await getir();
            if (!Gecerli(n))
                return;
            Veri.Clear();
            Veri.Add(v);
            Tamamlandi();
        });
        public void HazirDegil() => VeriHazir = false;
        protected override void OturumTemizle() => Veri.Clear();
    }

    [Fact]
    public async Task Ilk_yukleme_hatasinda_govde_gizli_sonraki_hatada_son_veri_eski_isaretlenir()
    {
        var vm = new YukleyenVm(TestOturumu.Ac());

        await vm.YukleAsync(() => throw new HttpRequestException());
        Assert.False(vm.GovdeGorunur);
        Assert.False(vm.VeriEski);
        Assert.Equal("Henüz yüklenmedi.", vm.SonGuncellemeMetni);
        Assert.Contains("Sunucuya ulaşılamadı", vm.Hata);

        await vm.YukleAsync(() => Task.FromResult("kartlar"));
        Assert.True(vm.GovdeGorunur);
        Assert.Null(vm.Hata);

        vm.HazirDegil();   // takip ekranları yüklemeye başlarken VeriHazir'ı indirir
        await vm.YukleAsync(() => throw new HttpRequestException());
        Assert.True(vm.GovdeGorunur);
        Assert.True(vm.VeriEski);
        Assert.Equal(["kartlar"], vm.Veri);
        Assert.StartsWith("Son güncelleme: ", vm.SonGuncellemeMetni);
        Assert.EndsWith(" · güncel olmayabilir", vm.SonGuncellemeMetni);

        await vm.YukleAsync(() => Task.FromResult("yeni"));
        Assert.False(vm.VeriEski);
    }

    [Fact]
    public async Task Kopukken_yuklemenin_baglanti_hatasi_yazilmaz_oturum_degisince_son_veri_kalkar()
    {
        var auth = TestOturumu.Ac();
        var vm = new YukleyenVm(auth);
        await vm.YukleAsync(() => Task.FromResult("kartlar"));
        auth.Baglanti.Ulasilamadi();

        await vm.YukleAsync(() => throw new TimeoutException());

        Assert.Null(vm.Hata);
        Assert.True(vm.VeriEski);

        TestOturumu.YeniOturum(auth, Rol.Editor);
        Assert.False(vm.GovdeGorunur);
        Assert.False(vm.VeriEski);
        Assert.Empty(vm.Veri);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~RaporDurumuTests|FullyQualifiedName~AnaSayfaVeRaporIptalTests|FullyQualifiedName~SonVeriTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 9 farklı ileti; örnekler:

- `AnaSayfaVeRaporIptalTests.cs: CS1061: 'HaftalikViewModel' bir 'VeriEski' tanımı içermiyor ve 'HaftalikViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'VeriEski' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `RaporDurumuTests.cs: CS1061: 'PanelViewModel' bir 'Yuklendi' tanımı içermiyor ve 'PanelViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'Yuklendi' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `RaporDurumuTests.cs: CS1061: 'PanelViewModel' bir 'VeriEski' tanımı içermiyor ve 'PanelViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'VeriEski' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `RaporDurumuTests.cs: CS1061: 'KrediTakipViewModel' bir 'SonGuncellemeMetni' tanımı içermiyor ve 'KrediTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'SonGuncellemeMetni' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/AylikViewModel.cs` — Bul:

```csharp
    {
        var yil = Yil;
        var ay = Ay;
        Rapor = null;
        return RaporYukleAsync(() => _api.AylikAsync(yil, ay), rapor => Rapor = rapor);
    }

    [RelayCommand]
    private Task OncekiAy()
    {
```

Yerine:

```csharp
    {
        var yil = Yil;
        var ay = Ay;
        return RaporYukleAsync(() => _api.AylikAsync(yil, ay), rapor => Rapor = rapor);
    }

    partial void OnYilChanged(int value) => AyDegisti();
    partial void OnAyChanged(int value) => AyDegisti();

    /// <summary>Başka aya geçilince önceki ayın raporu bu ayınki gibi gösterilmez (son veri yalnız aynı ayın yenilemesinde korunur).</summary>
    private void AyDegisti()
    {
        if (Rapor is not { } r || (r.Yil == Yil && r.Ay == Ay))
            return;
        Rapor = null;
        VeriVar = false;
        VeriEski = false;
    }

    [RelayCommand]
    private Task OncekiAy()
    {
```

`Kasa.App.Core/Bicim.cs` — Bul:

```csharp

    public static string Tl(decimal n) => n.ToString("#,##0.00", Tr);

    public static string ImzaliTl(decimal n) => (n < 0 ? "-" : "+") + Tl(Math.Abs(n));

    /// <summary>Dönem seçici etiketi: "13 Tem – 19 Tem"; yıllı "13 Tem 2026 – 19 Tem 2026" (gelir formu dönem
```

Yerine:

```csharp

    public static string Tl(decimal n) => n.ToString("#,##0.00", Tr);

    /// <summary>Hiç başarılı yükleme yokken son güncelleme satırı (iki sayfa ailesinde aynı; tasarım 2026-10-02 §3).</summary>
    public const string HenuzYuklenmedi = "Henüz yüklenmedi.";

    /// <summary>Son yükleme hata verdi, gösterilen veri son başarılı yüklemeden: son güncelleme satırının eki.</summary>
    public const string EskiVeriEki = " · güncel olmayabilir";

    public static string ImzaliTl(decimal n) => (n < 0 ? "-" : "+") + Tl(Math.Abs(n));

    /// <summary>Dönem seçici etiketi: "13 Tem – 19 Tem"; yıllı "13 Tem 2026 – 19 Tem 2026" (gelir formu dönem
```

`Kasa.App.Core/HaftalikViewModel.cs` — Bul:

```csharp

    public ObservableCollection<HaftalikSatir> Donemler { get; } = new();
    /// <summary>Sunucunun veri sağlığı uyarısı (ör. rapor ufkunun ötesinde tarihli kayıt). Yalnız son dönemde gelir ama
    /// raporun tamamı için geçerlidir; yüklenirken ve hatada null.</summary>
    [ObservableProperty] private string? _veriSagligiUyarisi;

    public override Task YukleAsync()
    {
        VeriSagligiUyarisi = null;
        return RaporYukleAsync(ct => _api.HaftalikAsync(ct), liste =>
        {
            // HF-01: sunucu eskiden yeniye döner; içinde bulunulan hafta her seferinde sona kaydırmadan görünsün diye
```

Yerine:

```csharp

    public ObservableCollection<HaftalikSatir> Donemler { get; } = new();
    /// <summary>Sunucunun veri sağlığı uyarısı (ör. rapor ufkunun ötesinde tarihli kayıt). Yalnız son dönemde gelir ama
    /// raporun tamamı için geçerlidir; son başarılı yüklemeden kalır.</summary>
    [ObservableProperty] private string? _veriSagligiUyarisi;

    public override Task YukleAsync()
    {
        return RaporYukleAsync(ct => _api.HaftalikAsync(ct), liste =>
        {
            // HF-01: sunucu eskiden yeniye döner; içinde bulunulan hafta her seferinde sona kaydırmadan görünsün diye
```

`Kasa.App.Core/IslemlerViewModel.cs` (1/3) — Bul:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
```

Yerine:

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
```

`Kasa.App.Core/IslemlerViewModel.cs` (2/3) — Bul:

```csharp
        ListeTemizle();
    }

    /// <summary><see cref="SonGuncellemeMetni"/> tabandaki <see cref="OturumluViewModel.SonGuncelleme"/>'ye bağlıdır.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(SonGuncelleme))
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(SonGuncellemeMetni)));
    }

    /// <summary>Kanal filtresi "tüm kanallar" çip/etiket metni (gerçek kanal olamaz).</summary>
    private const string TumKanal = "Tümü";
```

Yerine:

```csharp
        ListeTemizle();
    }

    /// <summary>Kanal filtresi "tüm kanallar" çip/etiket metni (gerçek kanal olamaz).</summary>
    private const string TumKanal = "Tümü";
```

`Kasa.App.Core/IslemlerViewModel.cs` (3/3) — Bul:

```csharp
    [ObservableProperty] private bool _veriVar;
    [ObservableProperty] private string _bosListeBasligi = "Henüz işlem yok";
    [ObservableProperty] private string _bosListeAciklamasi = "İlk kayıtla liste burada oluşur.";
    public string SonGuncellemeMetni => SonGuncelleme is { } zaman ? $"Son başarılı güncelleme: {zaman:dd.MM.yyyy HH:mm}" : "Liste henüz yüklenmedi.";

    /// <summary>Son başlatılan liste yüklemesi (dönem seçimi gibi beklenmeden başlayan yüklemeler için).</summary>
    public Task ListeYuklemesi { get; private set; } = Task.CompletedTask;
```

Yerine:

```csharp
    [ObservableProperty] private bool _veriVar;
    [ObservableProperty] private string _bosListeBasligi = "Henüz işlem yok";
    [ObservableProperty] private string _bosListeAciklamasi = "İlk kayıtla liste burada oluşur.";

    /// <summary>Son başlatılan liste yüklemesi (dönem seçimi gibi beklenmeden başlayan yüklemeler için).</summary>
    public Task ListeYuklemesi { get; private set; } = Task.CompletedTask;
```

`Kasa.App.Core/OturumluViewModel.cs` (1/3) — Bul:

```csharp
    private void OturumuSifirla()
    {
        VeriHazir = false;
        Mesgul = false;
        Hata = null;
        Mesaj = null;
```

Yerine:

```csharp
    private void OturumuSifirla()
    {
        VeriHazir = false;
        VeriEski = false;
        Mesgul = false;
        Hata = null;
        Mesaj = null;
```

`Kasa.App.Core/OturumluViewModel.cs` (2/3) — Bul:

```csharp

    public bool EditorMu => Auth.AktifRol == Rol.Editor;
    public int OturumNesli => Yurutucu.Nesil;
    [ObservableProperty] private bool _veriHazir;
    [ObservableProperty] private string? _mesaj;
    /// <summary>Son başarılı yükleme anı (yerel saat ve farkı); rapor ve işlem listesiyle aynı tür.</summary>
    [ObservableProperty] private DateTimeOffset? _sonGuncelleme;

    /// <summary>Gerekçe isteyen işlemin tek yolu (iptal, durum değişimi, belge kaldırma, ay kilidi): oturum gerekçe penceresi
    /// açılmadan ÖNCE yakalanır. Pencere açıkken oturum değişirse (çıkış, oturumun sona ermesi, yeni giriş) gerekçe yeni oturumun
```

Yerine:

```csharp

    public bool EditorMu => Auth.AktifRol == Rol.Editor;
    public int OturumNesli => Yurutucu.Nesil;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GovdeGorunur))]
    private bool _veriHazir;
    [ObservableProperty] private string? _mesaj;
    /// <summary>Son başarılı yükleme anı (yerel saat ve farkı); rapor ve işlem listesiyle aynı tür.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni), nameof(GovdeGorunur))]
    private DateTimeOffset? _sonGuncelleme;
    /// <summary>Son yükleme hata verdi; gösterilen veri son başarılı yüklemeden (soluk gösterilir, tasarım §3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private bool _veriEski;

    /// <summary>"Son güncelleme: 02.10.2026 14:05" (veri eskiyse " · güncel olmayabilir" ekiyle); hiç yükleme yoksa "Henüz yüklenmedi.".</summary>
    public string SonGuncellemeMetni => SonGuncelleme is { } zaman
        ? $"Son güncelleme: {zaman:dd.MM.yyyy HH:mm}" + (VeriEski ? Bicim.EskiVeriEki : "")
        : Bicim.HenuzYuklenmedi;

    /// <summary>Ekranın gövdesi görünür mü: veri hazır ya da (yükleme hata verse de) son başarılı veri var (tasarım §3).</summary>
    public bool GovdeGorunur => VeriHazir || SonGuncelleme is not null;

    /// <summary>Gerekçe isteyen işlemin tek yolu (iptal, durum değişimi, belge kaldırma, ay kilidi): oturum gerekçe penceresi
    /// açılmadan ÖNCE yakalanır. Pencere açıkken oturum değişirse (çıkış, oturumun sona ermesi, yeni giriş) gerekçe yeni oturumun
```

`Kasa.App.Core/OturumluViewModel.cs` (3/3) — Bul:

```csharp
    protected override bool BaglantiKopuk => Auth.Baglanti.Kopuk;
    protected void BekleyenleriIptalEt() { Yurutucu.GecersizKil(); Mesgul = false; }
    protected abstract void OturumTemizle();
    protected void Tamamlandi() { VeriHazir = true; SonGuncelleme = DateTimeOffset.Now; }
}
```

Yerine:

```csharp
    protected override bool BaglantiKopuk => Auth.Baglanti.Kopuk;
    protected void BekleyenleriIptalEt() { Yurutucu.GecersizKil(); Mesgul = false; }
    protected abstract void OturumTemizle();
    protected void Tamamlandi() { VeriHazir = true; VeriEski = false; SonGuncelleme = DateTimeOffset.Now; }

    /// <summary>Ekran yüklemesi (tekil işlem): hata okuma iletisiyle yazılır, bağlantı kopukken bağlantı hatası yazılmaz (kabuk
    /// şeridi söyler); son başarılı veri silinmez, varsa eski işaretlenir. Başarılı yükleme <see cref="Tamamlandi"/>'yı çağırır.</summary>
    protected Task VeriYukleAsync(Func<int, Task> islem) => YurutAsync(islem, hataIsle: hata =>
    {
        Yurutucu.OkumaHatasiniYaz(hata);
        VeriEski = SonGuncelleme is not null;
    });
}
```

`Kasa.App.Core/PanelViewModel.cs` — Bul:

```csharp
    public override Task YukleAsync()
    {
        var gun = TakipGunu;
        // Uyarı öbür panel alanları gibi yükleme sürerken ve yükleme başarısızsa görünmez.
        TakipsizUyari = "";
        return RaporYukleAsync(ct => _api.AnaSayfaAsync(gun, ct), a =>
        {
            var p = a.Panel;
```

Yerine:

```csharp
    public override Task YukleAsync()
    {
        var gun = TakipGunu;
        // Uyarı öbür panel alanları gibi son başarılı yüklemeden kalır (yenileme ve hata silmez).
        return RaporYukleAsync(ct => _api.AnaSayfaAsync(gun, ct), a =>
        {
            var p = a.Panel;
```

`Kasa.App.Core/RaporViewModel.cs` (1/4) — Bul:

```csharp

namespace Kasa.App.Core;

/// <summary>Raporlarda yalnız son isteğin sonucunu gösterir; yüklenirken/eski veride finansal rakamları gizler. Yeni yükleme
/// ve ekrandan ayrılma süren isteği iptal eder (istek ağda da bırakılır, sunucu hesabı keser); iptal hata sayılmaz.
/// Yürütme yürütücünün son istek hattıdır (<see cref="SonIstekHatti"/>).</summary>
public abstract partial class RaporViewModel : TemelViewModel
{
    private readonly SonIstekHatti _hat;
```

Yerine:

```csharp

namespace Kasa.App.Core;

/// <summary>Raporlarda yalnız son isteğin sonucunu gösterir. Yenileme ve hata son başarılı veriyi silmez (tasarım 2026-10-02 §3):
/// hata verirse veri eski işaretlenir (<see cref="VeriEski"/>, soluk gösterilir); başka sorguya geçen alt sınıf (Aylık'ta ay)
/// eski sorgunun verisini kendisi kaldırır. Yeni yükleme ve ekrandan ayrılma süren isteği iptal eder (istek ağda da bırakılır,
/// sunucu hesabı keser); iptal hata sayılmaz. Yürütme yürütücünün son istek hattıdır (<see cref="SonIstekHatti"/>).</summary>
public abstract partial class RaporViewModel : TemelViewModel
{
    private readonly SonIstekHatti _hat;
```

`Kasa.App.Core/RaporViewModel.cs` (2/4) — Bul:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private DateTimeOffset? _sonGuncelleme;

    private readonly BaglantiDurumu? _baglanti;
```

Yerine:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private DateTimeOffset? _sonGuncelleme;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SonGuncellemeMetni))]
    private bool _veriEski;

    /// <summary>Her başarılı yüklemeden sonra (değerler yazıldıktan sonra); Kasalar alt bölümleri bunu dinler.</summary>
    public event EventHandler? Yuklendi;

    private readonly BaglantiDurumu? _baglanti;
```

`Kasa.App.Core/RaporViewModel.cs` (3/4) — Bul:

```csharp
    protected override bool BaglantiKopuk => _baglanti?.Kopuk == true;

    public string SonGuncellemeMetni => SonGuncelleme is { } zaman
        ? $"Son başarılı güncelleme: {zaman:dd.MM.yyyy HH:mm:ss}"
        : "Veriler henüz yüklenmedi.";

    public abstract Task YukleAsync();
    [RelayCommand] private Task YenileAsync() => YukleAsync();
```

Yerine:

```csharp
    protected override bool BaglantiKopuk => _baglanti?.Kopuk == true;

    public string SonGuncellemeMetni => SonGuncelleme is { } zaman
        ? $"Son başarılı güncelleme: {zaman:dd.MM.yyyy HH:mm:ss}" + (VeriEski ? Bicim.EskiVeriEki : "")
        : Bicim.HenuzYuklenmedi;

    public abstract Task YukleAsync();
    [RelayCommand] private Task YenileAsync() => YukleAsync();
```

`Kasa.App.Core/RaporViewModel.cs` (4/4) — Bul:

```csharp
    protected Task RaporYukleAsync<T>(Func<Task<T>> getir, Action<T> uygula) => RaporYukleAsync(_ => getir(), uygula);

    protected Task RaporYukleAsync<T>(Func<CancellationToken, Task<T>> getir, Action<T> uygula)
    {
        VeriVar = false;
        return _hat.YukleAsync(getir, veri =>
        {
            uygula(veri);
            SonGuncelleme = DateTimeOffset.Now;
            VeriVar = true;
        });
    }
}
```

Yerine:

```csharp
    protected Task RaporYukleAsync<T>(Func<Task<T>> getir, Action<T> uygula) => RaporYukleAsync(_ => getir(), uygula);

    protected Task RaporYukleAsync<T>(Func<CancellationToken, Task<T>> getir, Action<T> uygula)
        => _hat.YukleAsync(getir, veri =>
        {
            uygula(veri);
            SonGuncelleme = DateTimeOffset.Now;
            VeriEski = false;
            VeriVar = true;
            Yuklendi?.Invoke(this, EventArgs.Empty);
        }, hata =>
        {
            Yurutucu.OkumaHatasiniYaz(hata);
            VeriEski = VeriVar;
        });
}
```

`Kasa.App/Views/PanelPage.xaml.cs` — Bul:

```csharp
        BindingContext = _vm = vm;
        _takip = takip;
        _kontrol = kontrol;
        _vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(vm.VeriVar))
            {
                // Takip özeti ve kanal eşikleri panelle aynı ana sayfa yanıtından gelir (bakiye, uyarı ve kart borcu aynı
                // andan); eski sunucuda null'dır ve eski uçlardan ayrıca yüklenir.
                if (vm.VeriVar)
                {
                    await takip.PaneldenYukleAsync(vm.TakipOzeti, vm.TakipOzetiGunu);
                    await kontrol.YukleAsync(vm.KasaEsikleri);
                    await cekler.YukleAsync();
                }
                else
                {
                    takip.VeriHazir = false;
                    kontrol.VeriHazir = false;
                    cekler.VeriHazir = false;
                }
            }
        };
        takip.PropertyChanged += (_, e) =>
        {
```

Yerine:

```csharp
        BindingContext = _vm = vm;
        _takip = takip;
        _kontrol = kontrol;
        // Takip özeti ve kanal eşikleri panelle aynı ana sayfa yanıtından gelir (bakiye, uyarı ve kart borcu aynı andan); eski
        // sunucuda null'dır ve eski uçlardan ayrıca yüklenir. Her başarılı panel yüklemesinden sonra yenilenir; panelin hatası alt
        // bölümlerin son verisini silmez (tasarım 2026-10-02 §3).
        _vm.Yuklendi += async (_, _) =>
        {
            await takip.PaneldenYukleAsync(vm.TakipOzeti, vm.TakipOzetiGunu);
            await kontrol.YukleAsync(vm.KasaEsikleri);
            await cekler.YukleAsync();
        };
        takip.PropertyChanged += (_, e) =>
        {
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1107`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/AnaSayfaVeRaporIptalTests.cs Kasa.App.Core.Tests/RaporDurumuTests.cs Kasa.App.Core.Tests/SonVeriTests.cs Kasa.App.Core/AylikViewModel.cs Kasa.App.Core/Bicim.cs Kasa.App.Core/HaftalikViewModel.cs Kasa.App.Core/IslemlerViewModel.cs Kasa.App.Core/OturumluViewModel.cs Kasa.App.Core/PanelViewModel.cs Kasa.App.Core/RaporViewModel.cs Kasa.App/Views/PanelPage.xaml.cs
git commit -F - <<'MESAJ'
feat(app-core): son başarılı veriyi koru ve eski işaretle

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 8: Arayüz — form alanı hata gösterimi

`FormAlani` denetimi (başlık, çerçeve, alan altı ileti, erişilebilir ad), `BrushNeg`, `TakipUi.Alan` hata yüklemesi ve `TakipUi.FormHatasi`.

**Dosyalar:**
- Oluştur: `Kasa.App/Controls/FormAlani.cs`
- Değiştir: `Kasa.App/Resources/Styles/Colors.xaml`
- Değiştir: `Kasa.App/Views/TakipUi.cs`
- Test (oluştur): `Kasa.App.Core.Tests/Donusturuculer/FormAlaniTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/Donusturuculer/FormAlaniTests.cs` (yeni dosya):

```csharp
using Kasa.App.Controls;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Form alanı (FormAlani, TakipUi.Alan, TakipUi.FormHatasi) gerçek MAUI bağlama motoruyla: AlanHatalari dizinleyicisine
/// bağlanan hata alanın altında görünür, çerçeve kırmızılaşır, ekran okuyucu adı iletiyi içerir; hata kalkınca hepsi geri döner.</summary>
public class FormAlaniTests
{
    public sealed class Baglam
    {
        public AlanHatalari Hatalar { get; } = new();
        public string Ad { get; set; } = "";
    }

    private static Brush Firca(string anahtar) => (Brush)Application.Current!.Resources[anahtar];

    [Fact]
    public void Xaml_ailesi_hata_varken_kirmizi_cerceve_ileti_ve_erisilebilir_ad_gosterir()
    {
        GorunumOrtami.Kur();
        var girdi = new Entry();
        var alan = new FormAlani { Baslik = "Açıklama", Alan = "Ad", Icerik = girdi };
        alan.SetBinding(FormAlani.HataProperty, "Hatalar[Ad]");
        var baglam = new Baglam();
        alan.BindingContext = baglam;
        var cerceve = alan.GetVisualTreeDescendants().OfType<Border>().Single();
        var ileti = alan.GetVisualTreeDescendants().OfType<Label>().Last();

        Assert.Same(girdi, cerceve.Content);
        Assert.Same((Style)Application.Current!.Resources["FieldBorder"], cerceve.Style);
        Assert.False(ileti.IsVisible);
        Assert.Null(SemanticProperties.GetDescription(girdi));

        baglam.Hatalar.Ayarla("Ad", "Açıklama boş olamaz.");
        Assert.Equal("Açıklama boş olamaz.", alan.Hata);
        Assert.True(ileti.IsVisible);
        Assert.Equal("Açıklama boş olamaz.", ileti.Text);
        Assert.Same(Firca("BrushNeg"), cerceve.Stroke);
        Assert.Equal("Açıklama. Açıklama boş olamaz.", SemanticProperties.GetDescription(girdi));

        baglam.Hatalar.Temizle("Ad");
        Assert.Null(alan.Hata);
        Assert.False(ileti.IsVisible);
        Assert.Same(Firca("BrushFieldStroke"), cerceve.Stroke);
        Assert.Null(SemanticProperties.GetDescription(girdi));

        baglam.Hatalar.Ayarla("Ad", "x");
        baglam.Hatalar.Temizle();
        Assert.Null(alan.Hata);
    }

    [Fact]
    public void Cercevesiz_xaml_alani_cip_grubunu_sarar_cerceve_yalniz_hatada_gorunur()
    {
        GorunumOrtami.Kur();
        var cipler = new HorizontalStackLayout();
        var alan = new FormAlani { Baslik = "Kanal", Alan = "Kanal", Icerik = cipler, Cerceveli = false, Hata = "Kanal seçin." };
        var cerceve = alan.GetVisualTreeDescendants().OfType<Border>().Single();

        Assert.Same((Style)Application.Current!.Resources["LblField"], alan.GetVisualTreeDescendants().OfType<Label>().First().Style);
        Assert.Null(cerceve.Style);
        Assert.Same(Firca("BrushNeg"), cerceve.Stroke);
        alan.Hata = null;
        Assert.Same(Brush.Transparent, cerceve.Stroke);
    }

    [Fact]
    public void Takip_ailesi_cercevesi_yalniz_hatada_gorunur_genel_hata_kutusu_yalniz_doluyken()
    {
        GorunumOrtami.Kur();
        var baglam = new Baglam();
        var alan = TakipUi.Alan("Tutar", TakipUi.Girdi("Ad"), nameof(Baglam.Hatalar), "Tutar");
        var kutu = TakipUi.FormHatasi("Hatalar.Genel");
        var panel = new VerticalStackLayout { Children = { kutu, alan }, BindingContext = baglam };
        var cerceve = alan.GetVisualTreeDescendants().OfType<Border>().Single();

        Assert.Equal("Tutar", alan.Alan);
        Assert.Same((Style)Application.Current!.Resources["LblTakipKucuk"], alan.GetVisualTreeDescendants().OfType<Label>().First().Style);
        Assert.Same(Brush.Transparent, cerceve.Stroke);
        Assert.False(kutu.IsVisible);

        baglam.Hatalar.Ayarla("Tutar", "Tutar sıfırdan büyük olmalı.");
        baglam.Hatalar.Genel = "Sunucuya ulaşılamadı. Kayıt yapılmadı; bağlantı gelince yeniden kaydedin.";
        Assert.Same(Firca("BrushNeg"), cerceve.Stroke);
        Assert.True(kutu.IsVisible);
        Assert.Same((Style)Application.Current!.Resources["ErrorBox"], kutu.Style);
        Assert.Equal(baglam.Hatalar.Genel, ((Label)kutu.Content!).Text);

        baglam.Hatalar.Temizle();
        Assert.Same(Brush.Transparent, cerceve.Stroke);
        Assert.False(kutu.IsVisible);
        Assert.NotNull(panel);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~FormAlaniTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 5 farklı ileti; örnekler:

- `FormAlaniTests.cs: CS0246: 'FormAlani' türü veya ad alanı adı bulunamadı (bir using yönergeniz veya derleme başvurunuz mu eksik?)`
- `FormAlaniTests.cs: CS0103: 'FormAlani' adı geçerli bağlamda yok`
- `FormAlaniTests.cs: CS1501: 'Alan' yöntemi için hiçbir tekrar yükleme 4 bağımsız değişken almaz`
- `FormAlaniTests.cs: CS0117: 'TakipUi' bir 'FormHatasi' tanımı içermiyor`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App/Controls/FormAlani.cs` (yeni dosya):

```csharp
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Kasa.App.Controls;

/// <summary>
/// Form alanı (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §1): başlık, çerçeve içinde girdi (<see cref="Icerik"/>)
/// ve hata varken alanın altında ileti. Hata varken çerçeve kırmızıdır (Neg) ve ekran okuyucu girdinin adına iletiyi ekler
/// ("Tutar. Tutar sıfırdan büyük olmalı."). <see cref="Alan"/> görünüm modelindeki alanın adıdır: kaydırma yardımcısı ilk hatalı
/// alanı bununla bulur. <see cref="Cerceveli"/> girdiyi FieldBorder içine alır (XAML sayfalarının metin, tutar ve tarih alanları);
/// çerçevesiz alanda (çip grupları, kodla yazılmış takip sayfaları) çerçeve yalnız hata varken (kırmızı) görünür. Başlık XAML
/// ailesinde LblField, <see cref="TakipStili"/> ile takip ailesinde LblTakipKucuk'tur (TakipUi.Alan).
/// </summary>
[ContentProperty(nameof(Icerik))]
public class FormAlani : ContentView
{
    public static readonly BindableProperty BaslikProperty = Ozellik(nameof(Baslik), typeof(string), null);
    public static readonly BindableProperty HataProperty = Ozellik(nameof(Hata), typeof(string), null);
    public static readonly BindableProperty AlanProperty = BindableProperty.Create(nameof(Alan), typeof(string), typeof(FormAlani));
    public static readonly BindableProperty IcerikProperty = Ozellik(nameof(Icerik), typeof(View), null);
    public static readonly BindableProperty CerceveliProperty = Ozellik(nameof(Cerceveli), typeof(bool), true);
    public static readonly BindableProperty TakipStiliProperty = Ozellik(nameof(TakipStili), typeof(bool), false);

    public string? Baslik { get => (string?)GetValue(BaslikProperty); set => SetValue(BaslikProperty, value); }
    public string? Hata { get => (string?)GetValue(HataProperty); set => SetValue(HataProperty, value); }
    /// <summary>Görünüm modelindeki alanın adı (AlanHatalari anahtarı, ör. "DuzenCari").</summary>
    public string? Alan { get => (string?)GetValue(AlanProperty); set => SetValue(AlanProperty, value); }
    public View? Icerik { get => (View?)GetValue(IcerikProperty); set => SetValue(IcerikProperty, value); }
    public bool Cerceveli { get => (bool)GetValue(CerceveliProperty); set => SetValue(CerceveliProperty, value); }
    /// <summary>Kodla yazılmış takip sayfalarının başlık stili (LblTakipKucuk) ve aralığı (5).</summary>
    public bool TakipStili { get => (bool)GetValue(TakipStiliProperty); set => SetValue(TakipStiliProperty, value); }

    private readonly VerticalStackLayout _yigin;
    private readonly Label _baslik;
    private readonly Border _cerceve;
    private readonly Label _hata;

    public FormAlani()
    {
        _baslik = new Label();
        _cerceve = new Border();
        _hata = new Label { Style = (Style)Application.Current!.Resources["LblError"], Margin = new Thickness(0, 4, 0, 0) };
        _yigin = new VerticalStackLayout { Children = { _baslik, _cerceve, _hata } };
        Content = _yigin;
        Guncelle();
    }

    private static BindableProperty Ozellik(string ad, Type tur, object? varsayilan)
        => BindableProperty.Create(ad, tur, typeof(FormAlani), varsayilan, propertyChanged: (s, _, _) => ((FormAlani)s).Guncelle());

    /// <summary>Hata varsa girdiye odaklanır (kaydırma yardımcısı ilk hatalı alana gidince).</summary>
    public bool Odaklan() => Icerik?.Focus() == true;

    private void Guncelle()
    {
        var hataVar = !string.IsNullOrWhiteSpace(Hata);
        _yigin.Spacing = TakipStili ? 5 : 0;
        _baslik.Style = (Style)(TakipStili ? Application.Current!.Resources["LblTakipKucuk"] : Application.Current!.Resources["LblField"]);
        _baslik.Text = Baslik;
        _baslik.IsVisible = !string.IsNullOrEmpty(Baslik);
        if (Cerceveli)
        {
            foreach (var ozellik in new[] { Border.PaddingProperty, Border.StrokeThicknessProperty, Border.StrokeShapeProperty })
                _cerceve.ClearValue(ozellik);
            _cerceve.Style = (Style)Application.Current!.Resources["FieldBorder"];
            if (hataVar)
                _cerceve.Stroke = (Brush)Application.Current!.Resources["BrushNeg"];
            else
                _cerceve.ClearValue(Border.StrokeProperty);
        }
        else
        {
            _cerceve.ClearValue(StyleProperty);
            _cerceve.Padding = 0;
            _cerceve.StrokeThickness = 1;
            _cerceve.StrokeShape = new RoundRectangle { CornerRadius = 6 };
            _cerceve.Stroke = hataVar ? (Brush)Application.Current!.Resources["BrushNeg"] : Brush.Transparent;
        }
        _cerceve.Content = Icerik;
        _hata.Text = Hata;
        _hata.IsVisible = hataVar;
        if (Icerik is null)
            return;
        if (hataVar)
            SemanticProperties.SetDescription(Icerik, string.IsNullOrEmpty(Baslik) ? Hata : $"{Baslik}. {Hata}");
        else
            Icerik.ClearValue(SemanticProperties.DescriptionProperty);
    }
}
```

`Kasa.App/Resources/Styles/Colors.xaml` — Bul:

```xml
    <SolidColorBrush x:Key="BrushGreen" Color="{StaticResource Green}" />
    <SolidColorBrush x:Key="BrushGreenSoft" Color="{StaticResource GreenSoft}" />
    <SolidColorBrush x:Key="BrushNegBorder" Color="{StaticResource NegBorder}" />
    <SolidColorBrush x:Key="BrushSidebar" Color="{StaticResource Sidebar}" />
    <SolidColorBrush x:Key="BrushDateChipBorder" Color="{StaticResource DateChipBorder}" />
    <SolidColorBrush x:Key="BrushUyariKenar" Color="{StaticResource UyariKenar}" />
```

Yerine:

```xml
    <SolidColorBrush x:Key="BrushGreen" Color="{StaticResource Green}" />
    <SolidColorBrush x:Key="BrushGreenSoft" Color="{StaticResource GreenSoft}" />
    <SolidColorBrush x:Key="BrushNegBorder" Color="{StaticResource NegBorder}" />
    <!-- Hatalı form alanının çerçevesi (FormAlani; tasarım 2026-10-02 §1) -->
    <SolidColorBrush x:Key="BrushNeg" Color="{StaticResource Neg}" />
    <SolidColorBrush x:Key="BrushSidebar" Color="{StaticResource Sidebar}" />
    <SolidColorBrush x:Key="BrushDateChipBorder" Color="{StaticResource DateChipBorder}" />
    <SolidColorBrush x:Key="BrushUyariKenar" Color="{StaticResource UyariKenar}" />
```

`Kasa.App/Views/TakipUi.cs` — Bul:

```csharp
        Children = { new Label { Text = ad, Style = (Style)Application.Current!.Resources["LblTakipKucuk"] }, v },
    };

    public static Entry Girdi(string yol, bool para = false, bool sayi = false)
    {
        if (para)
```

Yerine:

```csharp
        Children = { new Label { Text = ad, Style = (Style)Application.Current!.Resources["LblTakipKucuk"] }, v },
    };

    /// <summary>Hatası gösterilen alan (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §1; Controls.FormAlani): başlık,
    /// girdi ve hata varken altında ileti; çerçeve yalnız hata varken (kırmızı) görünür. <paramref name="hatalar"/> modelin
    /// AlanHatalari özelliğinin yolu ("Hatalar"), <paramref name="alan"/> alanın adı ("Tutar"): hata "Hatalar[Tutar]" yolundan gelir.</summary>
    public static Controls.FormAlani Alan(string ad, View v, string hatalar, string alan)
    {
        var f = new Controls.FormAlani { Baslik = ad, Icerik = v, Alan = alan, Cerceveli = false, TakipStili = true };
        f.SetBinding(Controls.FormAlani.HataProperty, $"{hatalar}[{alan}]");
        return f;
    }

    /// <summary>Formun genel hatası (tasarım §1): formun en üstünde, formun içinde kırmızı kutu (ErrorBox); yalnız doluyken görünür.
    /// Sayfanın başındaki hata satırı yalnız yükleme hataları için kalır.</summary>
    public static Border FormHatasi(string yol)
    {
        var metin = new Label { Style = (Style)Application.Current!.Resources["LblError"] };
        metin.SetBinding(Label.TextProperty, yol);
        var kutu = new Border { Style = (Style)Application.Current!.Resources["ErrorBox"], Content = metin };
        kutu.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(Label.Text), source: metin, converter: new Converters.DoluIseConverter()));
        return kutu;
    }

    public static Entry Girdi(string yol, bool para = false, bool sayi = false)
    {
        if (para)
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1110`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/Donusturuculer/FormAlaniTests.cs Kasa.App/Controls/FormAlani.cs Kasa.App/Resources/Styles/Colors.xaml Kasa.App/Views/TakipUi.cs
git commit -F - <<'MESAJ'
feat(app): form alanı hata gösterimi

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 9: Arayüz — ortak kaydırma yardımcısı

`KartTakipPage.GorunurYap` `GorunurYapici`'ya taşınır; `HatayaGit` ilk hatalı alana (yoksa genel hata kutusuna) kaydırır ve odaklanır.

**Dosyalar:**
- Oluştur: `Kasa.App/Controls/GorunurYapici.cs`
- Değiştir: `Kasa.App/Views/KartTakipPage.cs`
- Değiştir: `Kasa.App/Views/TakipUi.cs`
- Test (oluştur): `Kasa.App.Core.Tests/Donusturuculer/GorunurYapiciTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/Donusturuculer/GorunurYapiciTests.cs` (yeni dosya):

```csharp
using Kasa.App.Controls;

namespace Kasa.App.Core.Tests;

/// <summary>Hataya kaydırmanın hedefi: hataların konulduğu sırayla ilk hatası olan görünür form alanı.</summary>
public class GorunurYapiciTests
{
    [Fact]
    public void Ilk_hatali_alan_hata_sirasiyla_ve_yalniz_gorunur_alanlardan_secilir()
    {
        GorunumOrtami.Kur();
        var ad = new FormAlani { Alan = "Ad", Icerik = new Entry() };
        var tutar = new FormAlani { Alan = "Tutar", Icerik = new Entry() };
        var gizli = new FormAlani { Alan = "Taksit", Icerik = new Entry() };
        var gizliKap = new VerticalStackLayout { IsVisible = false, Children = { gizli } };
        var form = new VerticalStackLayout { Children = { ad, gizliKap, tutar } };
        var hatalar = new AlanHatalari();

        Assert.Null(GorunurYapici.IlkHataliAlan(form, hatalar));

        hatalar.Ayarla("Taksit", "Taksit sayısı 1 ile 60 arasında olmalı.");
        hatalar.Ayarla("Tutar", "Tutar sıfırdan büyük olmalı.");
        hatalar.Ayarla("Ad", "Ad boş olamaz.");
        Assert.Same(tutar, GorunurYapici.IlkHataliAlan(form, hatalar));

        hatalar.Temizle("Tutar");
        Assert.Same(ad, GorunurYapici.IlkHataliAlan(form, hatalar));
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~GorunurYapiciTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 1 farklı ileti; örnekler:

- `GorunurYapiciTests.cs: CS0103: 'GorunurYapici' adı geçerli bağlamda yok`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App/Controls/GorunurYapici.cs` (yeni dosya):

```csharp
using System.Diagnostics;
using Kasa.App.Core;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;

namespace Kasa.App.Controls;

/// <summary>
/// Kaydırıcının içindeki öğeyi görünür yapan ortak yardımcı (eski KartTakipPage.GorunurYap; tasarım 2026-10-02 §1 "Hataya
/// kaydırma"). Karar <see cref="KaydirmaHesabi"/>'ndadır (hedef görünüyorsa kaydırılmaz). Zamanlama: hedefin bir sonraki
/// SizeChanged'i (yerleşim turunda üst öğeleri de yerleşmiş olur) ya da, boyutu değişmeden yalnız yeri değişirse, 100 ms aralıklı
/// yoklama; yoklama hedef yerleşmiş (genişliği olan) bulunca ya da 1 sn sonra biter. Kaydırma yerleşim turunun bitimine
/// (Dispatch) bırakılır. Yeni istek eskisini geçersiz kılar; istek bir kez kaydırır. <see cref="HatayaGit"/> kaydetme başarısız
/// olunca ilk hatalı alana (yoksa formun genel hata kutusuna) kaydırır ve alana odaklanır.
/// </summary>
public sealed class GorunurYapici(ScrollView kaydirici)
{
    /// <summary>Hedefin (üst, yükseklik) ve kaydırıcının (kaydırma konumu, görünür yükseklik) değerlerinden yeni kaydırma konumu;
    /// null: kaydırılmaz (KaydirmaHesabi).</summary>
    public delegate double? KaydirmaKarari(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik);

    /// <summary>Son kaydırma isteğinin sırası ve kaydırması yapılmış istek: yalnız en son istek, bir kez kaydırır.</summary>
    private int _istek, _kaydirilan;

    /// <summary>Hedefi görünür yapar; <paramref name="sonra"/> kaydırma denendikten sonra çalışır (ör. odaklanma).</summary>
    public void Yap(View hedef, KaydirmaKarari karar, Action? sonra = null)
    {
        var istek = ++_istek;
        var deneme = 0;
        void Boyutlandi(object? sender, EventArgs e) => Yerlesti();
        void Yerlesti()
        {
            hedef.SizeChanged -= Boyutlandi;
            kaydirici.Dispatcher.Dispatch(() => Kaydir(hedef, karar, istek, sonra));
        }
        void Yokla()
        {
            if (istek != _istek || istek == _kaydirilan)
            {
                hedef.SizeChanged -= Boyutlandi;
                return;
            }
            if (hedef.Width > 0)
                Yerlesti();
            else if (++deneme < 10)
                kaydirici.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), Yokla);
            else
                hedef.SizeChanged -= Boyutlandi;
        }
        hedef.SizeChanged += Boyutlandi;
        kaydirici.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), Yokla);
    }

    /// <summary>Formun ilk hatalı alanına (yoksa genel hata kutusuna) kaydırır ve alana odaklanır.</summary>
    /// <param name="form">Formun alanlarını içeren görünüm (FormAlani'ler bunun altındadır).</param>
    /// <param name="genelKutu">Formun en üstündeki genel hata kutusu.</param>
    public void HatayaGit(View form, AlanHatalari hatalar, View genelKutu)
    {
        if (IlkHataliAlan(form, hatalar) is { } alan)
            Yap(alan, KaydirmaHesabi.FormKaydirmasi, () => alan.Odaklan());
        else if (hatalar.Genel is not null)
            Yap(genelKutu, KaydirmaHesabi.FormKaydirmasi);
    }

    /// <summary>Hataların konulduğu sırayla ilk hatası olan, görünür (kendisi ve bütün ataları) form alanı; yoksa null.</summary>
    public static FormAlani? IlkHataliAlan(View form, AlanHatalari hatalar)
    {
        var alanlar = form.GetVisualTreeDescendants().OfType<FormAlani>().Where(GorunurMu).ToList();
        return hatalar.Alanlar.Select(ad => alanlar.FirstOrDefault(f => f.Alan == ad)).FirstOrDefault(f => f is not null);
    }

    private static bool GorunurMu(VisualElement v)
    {
        for (Element? e = v; e is not null; e = e.Parent)
            if (e is VisualElement { IsVisible: false })
                return false;
        return true;
    }

    private async void Kaydir(View hedef, KaydirmaKarari karar, int istek, Action? sonra)
    {
        if (istek != _istek || istek == _kaydirilan || !hedef.IsVisible)
            return;
        _kaydirilan = istek;
        try
        {
            if (karar(KaydiriciyaGoreY(hedef), hedef.Height, kaydirici.ScrollY, kaydirici.Height) is { } y)
                await kaydirici.ScrollToAsync(kaydirici.ScrollX, y, true);
            sonra?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ekran kaydırılamadı: {ex}");
        }
    }

    /// <summary>Hedefin kaydırılan içeriğe göre üstü: ata zinciri boyunca her öğenin üst öğesine göre yeri (Frame.Y) toplanır;
    /// hedef kaydırıcının içinde değilse NaN (kaydırılmaz).</summary>
    private double KaydiriciyaGoreY(VisualElement hedef)
    {
        var y = 0d;
        for (Element? e = hedef; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, kaydirici))
                return y;
            if (e is VisualElement v)
                y += v.Frame.Y;
        }
        return double.NaN;
    }
}
```

`Kasa.App/Views/KartTakipPage.cs` (1/2) — Bul:

```csharp

    private readonly SorguSecimi _secim = new("KartId");
    private int? _gosterilenKartId;
    /// <summary>Son kaydırma isteğinin sırası ve kaydırması yapılmış istek: yalnız en son istek, bir kez kaydırır.</summary>
    private int _kaydirmaIstegi, _kaydirilanIstek;
    private readonly View _ayrinti;
    private readonly View _formAlani;
    /// <summary>Formun tepesindeki hata satırı (FormHatasi): uzun formun altındaki düğmeden gelen hata görünür yere kaydırılır.</summary>
```

Yerine:

```csharp

    private readonly SorguSecimi _secim = new("KartId");
    private int? _gosterilenKartId;
    private readonly View _ayrinti;
    private readonly View _formAlani;
    /// <summary>Formun tepesindeki hata satırı (FormHatasi): uzun formun altındaki düğmeden gelen hata görünür yere kaydırılır.</summary>
```

`Kasa.App/Views/KartTakipPage.cs` (2/2) — Bul:

```csharp
            {
                _gosterilenKartId = vm.AcikKartId;
                if (vm.AcikKartId is not null)
                    GorunurYap(_ayrinti, AyrintiKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.AcikForm) && vm.FormAcik)
            {
                GorunurYap(_formAlani, KaydirmaHesabi.FormKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.FormHatasi) && !string.IsNullOrWhiteSpace(vm.FormHatasi))
            {
                GorunurYap(_formHataSatiri, KaydirmaHesabi.FormKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.SayfaHatasi) && !string.IsNullOrWhiteSpace(vm.SayfaHatasi))
            {
                GorunurYap(HataSatiri, KaydirmaHesabi.FormKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.Mesaj) && !string.IsNullOrWhiteSpace(vm.Mesaj))
            {
                GorunurYap(MesajSatiri, KaydirmaHesabi.FormKaydirmasi);
            }
        };
    }

    /// <summary>Hedefin (üst, yükseklik) ve kaydırıcının (kaydırma konumu, görünür yükseklik) değerlerinden yeni kaydırma konumu;
    /// null: kaydırılmaz (KaydirmaHesabi).</summary>
    private delegate double? KaydirmaKarari(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik);

    /// <summary>Ayrıntı: üstü görünür alandaysa kaydırılmaz (kutular görünür kalır), değilse üstü görünür alanın başına gelir.</summary>
    private static double? AyrintiKaydirmasi(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik)
        => KaydirmaHesabi.BasaKaydirilmali(ust, kaydirmaY, gorunurYukseklik) ? ust : null;

    /// <summary>
    /// Hedef yerleştikten sonra (konumu okunabilir olunca) <paramref name="karar"/>'ın verdiği konuma kaydırır (KaydirmaHesabi;
    /// hedef görünüyorsa kaydırılmaz). Zamanlama: hedefin bir sonraki SizeChanged'i (yerleşim turunda üst öğeleri de yerleşmiş olur) ya da,
    /// boyutu değişmeden yalnız yeri değişirse (başka satırdaki kart, aynı boyda form), 100 ms aralıklı yoklama; yoklama hedef
    /// yerleşmiş (genişliği olan) bulunca ya da 1 sn sonra biter. Kaydırma her iki yolda da yerleşim turunun bitimine
    /// (Dispatch) bırakılır. Yeni istek eskisini geçersiz kılar; istek bir kez kaydırır.
    /// </summary>
    private void GorunurYap(View hedef, KaydirmaKarari karar)
    {
        var istek = ++_kaydirmaIstegi;
        var deneme = 0;
        void Boyutlandi(object? sender, EventArgs e) => Yerlesti();
        void Yerlesti()
        {
            hedef.SizeChanged -= Boyutlandi;
            Dispatcher.Dispatch(() => Kaydir(hedef, karar, istek));
        }
        void Yokla()
        {
            if (istek != _kaydirmaIstegi || istek == _kaydirilanIstek)
            {
                hedef.SizeChanged -= Boyutlandi;
                return;
            }
            if (hedef.Width > 0)
                Yerlesti();
            else if (++deneme < 10)
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), Yokla);
            else
                hedef.SizeChanged -= Boyutlandi;
        }
        hedef.SizeChanged += Boyutlandi;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), Yokla);
    }

    private async void Kaydir(View hedef, KaydirmaKarari karar, int istek)
    {
        if (istek != _kaydirmaIstegi || istek == _kaydirilanIstek || !hedef.IsVisible)
            return;
        _kaydirilanIstek = istek;
        try
        {
            if (karar(KaydiriciyaGoreY(hedef), hedef.Height, Kaydirici.ScrollY, Kaydirici.Height) is { } y)
                await Kaydirici.ScrollToAsync(Kaydirici.ScrollX, y, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Kartlar ekranı kaydırılamadı: {ex}");
        }
    }

    /// <summary>Hedefin kaydırılan içeriğe göre üstü: ata zinciri boyunca her öğenin üst öğesine göre yeri (Frame.Y) toplanır;
    /// hedef kaydırıcının içinde değilse NaN (kaydırılmaz).</summary>
    private double KaydiriciyaGoreY(VisualElement hedef)
    {
        var y = 0d;
        for (Element? e = hedef; e is not null; e = e.Parent)
        {
            if (ReferenceEquals(e, Kaydirici))
                return y;
            if (e is VisualElement v)
                y += v.Frame.Y;
        }
        return double.NaN;
    }

    // ---- Özet ----

    private static View Ozet(KartTakipViewModel vm)
```

Yerine:

```csharp
            {
                _gosterilenKartId = vm.AcikKartId;
                if (vm.AcikKartId is not null)
                    Gorunur.Yap(_ayrinti, AyrintiKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.AcikForm) && vm.FormAcik)
            {
                Gorunur.Yap(_formAlani, KaydirmaHesabi.FormKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.FormHatasi) && !string.IsNullOrWhiteSpace(vm.FormHatasi))
            {
                Gorunur.Yap(_formHataSatiri, KaydirmaHesabi.FormKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.SayfaHatasi) && !string.IsNullOrWhiteSpace(vm.SayfaHatasi))
            {
                Gorunur.Yap(HataSatiri, KaydirmaHesabi.FormKaydirmasi);
            }
            else if (e.PropertyName == nameof(vm.Mesaj) && !string.IsNullOrWhiteSpace(vm.Mesaj))
            {
                Gorunur.Yap(MesajSatiri, KaydirmaHesabi.FormKaydirmasi);
            }
        };
    }

    /// <summary>Ayrıntı: üstü görünür alandaysa kaydırılmaz (kutular görünür kalır), değilse üstü görünür alanın başına gelir.</summary>
    private static double? AyrintiKaydirmasi(double ust, double yukseklik, double kaydirmaY, double gorunurYukseklik)
        => KaydirmaHesabi.BasaKaydirilmali(ust, kaydirmaY, gorunurYukseklik) ? ust : null;

    // ---- Özet ----

    private static View Ozet(KartTakipViewModel vm)
```

`Kasa.App/Views/TakipUi.cs` (1/2) — Bul:

```csharp
    protected readonly T Vm;
    protected readonly VerticalStackLayout Govde = new() { Spacing = 18 };
    protected readonly ScrollView Kaydirici;
    /// <summary>Sayfa başındaki hata satırı (DurumSatirlari).</summary>
    protected readonly Label HataSatiri;
    /// <summary>Sayfa başındaki ileti satırı (Mesaj; başarılı kayıt).</summary>
```

Yerine:

```csharp
    protected readonly T Vm;
    protected readonly VerticalStackLayout Govde = new() { Spacing = 18 };
    protected readonly ScrollView Kaydirici;
    /// <summary>Sayfanın kaydırma yardımcısı (açılan ayrıntı, form ve ilk hatalı alan görünür yere kaydırılır).</summary>
    protected readonly Controls.GorunurYapici Gorunur;
    /// <summary>Sayfa başındaki hata satırı (DurumSatirlari).</summary>
    protected readonly Label HataSatiri;
    /// <summary>Sayfa başındaki ileti satırı (Mesaj; başarılı kayıt).</summary>
```

`Kasa.App/Views/TakipUi.cs` (2/2) — Bul:

```csharp
        Govde.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        root.Add(Govde);
        Kaydirici = new ScrollView { Content = root };
        Content = Kaydirici;
    }
```

Yerine:

```csharp
        Govde.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        root.Add(Govde);
        Kaydirici = new ScrollView { Content = root };
        Gorunur = new Controls.GorunurYapici(Kaydirici);
        Content = Kaydirici;
    }
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1111`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/Donusturuculer/GorunurYapiciTests.cs Kasa.App/Controls/GorunurYapici.cs Kasa.App/Views/KartTakipPage.cs Kasa.App/Views/TakipUi.cs
git commit -F - <<'MESAJ'
feat(app): ortak kaydırma yardımcısı ve hataya kaydırma

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 10: Arayüz — kabuk bağlantı şeridi ve sayfadan çıkış onayı

`BaglantiSeridi` kabuğun `TitleView`'ında; "Yeniden dene" ve bağlantının geri gelmesi açık sayfayı yeniler (`IYenilenebilir`); `AppShell.OnNavigating` kaydedilmemiş değişiklikte erteleme ile onay sorar; takip sayfaları `BirakmaOnayi`'nı bağlar.

**Dosyalar:**
- Değiştir: `Kasa.App/AppShell.xaml.cs`
- Oluştur: `Kasa.App/Controls/BaglantiSeridi.cs`
- Değiştir: `Kasa.App/Views/AlislarPage.xaml.cs`
- Değiştir: `Kasa.App/Views/AyarlarPage.xaml.cs`
- Değiştir: `Kasa.App/Views/AylikPage.xaml.cs`
- Değiştir: `Kasa.App/Views/DisariAktarPage.cs`
- Değiştir: `Kasa.App/Views/HaftalikPage.xaml.cs`
- Değiştir: `Kasa.App/Views/IslemlerPage.xaml.cs`
- Değiştir: `Kasa.App/Views/PanelPage.xaml.cs`
- Değiştir: `Kasa.App/Views/TakipUi.cs`
- Test (oluştur): `Kasa.App.Core.Tests/Donusturuculer/BaglantiSeridiTests.cs`
- Test (oluştur): `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglanti.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/Donusturuculer/BaglantiSeridiTests.cs` (yeni dosya):

```csharp
using Kasa.App.Controls;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Kabuğun bağlantı şeridi: yalnız kopukken görünür, son bağlantı saatini yazar, "Yeniden dene" kabuğa bildirilir.</summary>
public class BaglantiSeridiTests
{
    [Fact]
    public void Serit_yalniz_kopukken_gorunur_metni_ve_yeniden_dene_bildirimi()
    {
        GorunumOrtami.Kur();
        var durum = new BaglantiDurumu(zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 10, 2)));
        var serit = new BaglantiSeridi { BindingContext = durum };
        var istek = 0;
        serit.YenidenDeneIstendi += (_, _) => istek++;
        var metin = serit.GetVisualTreeDescendants().OfType<Label>().Single();
        var dugme = serit.GetVisualTreeDescendants().OfType<Button>().Single();

        Assert.False(serit.IsVisible);

        durum.Ulasildi();
        durum.Ulasilamadi();
        Assert.True(serit.IsVisible);
        Assert.Equal("Sunucuya ulaşılamıyor · Son bağlantı 12:00", metin.Text);
        Assert.Equal("Yeniden dene", dugme.Text);
        ((IButtonController)dugme).SendClicked();
        Assert.Equal(1, istek);

        serit.Yenileniyor = true;
        Assert.False(dugme.IsEnabled);
        serit.Yenileniyor = false;
        durum.Ulasildi();
        Assert.False(serit.IsVisible);
    }
}
```

`Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglanti.cs` (yeni dosya):

```csharp
using System.Text.RegularExpressions;

namespace Kasa.App.Core.Tests;

/// <summary>Kabuğun bağlantı şeridi ve sayfadan çıkış onayı (tasarım 2026-10-02 §2–3). AppShell Windows'a bağlıdır ve test projesinde
/// derlenmez; kaynak düzeyinde denetlenir.</summary>
public partial class MauiKayitTutarliligiTests
{
    [Fact]
    public void Kabuk_baglanti_seridini_tek_yerde_kurar_ve_acik_sayfayi_yeniler()
    {
        var kod = Oku("AppShell.xaml.cs");
        Assert.Contains("BaglantiDurumu baglanti)", kod);
        Assert.Contains("SetTitleView(this, _baglantiSeridi);", kod);
        Assert.Contains("baglanti.BaglantiGeldi += async (_, _) => await AcikSayfayiYenileAsync();", kod);
        Assert.Contains("_baglantiSeridi.YenidenDeneIstendi += async (_, _) => await AcikSayfayiYenileAsync();", kod);
        Assert.Matches(@"private async Task AcikSayfayiYenileAsync\(\)\s*\{\s*if \(_yenileniyor \|\| CurrentPage is not IYenilenebilir sayfa\)\s*return;", kod);
    }

    /// <summary>Kaydedilmemiş değişiklikte sayfadan çıkış: MAUI Shell gezinme ertelemesi (GetDeferral → Cancel/Complete). Erteleme
    /// her yolda tamamlanır (yoksa sonraki GoToAsync InvalidOperationException verir); girişe dönüş sorulmaz.</summary>
    [Fact]
    public void Kabuk_sayfadan_cikista_kaydedilmemis_degisiklik_onayini_ertelemeyle_sorar()
    {
        var kod = Oku("AppShell.xaml.cs");
        Assert.Matches(@"protected override async void OnNavigating\(ShellNavigatingEventArgs args\)", kod);
        Assert.Contains("CurrentPage?.BindingContext is not IKaydedilmemisForm { KaydedilmemisDegisiklikVar: true } form", kod);
        Assert.Contains("Contains(\"login\", StringComparison.Ordinal)", kod);
        Assert.Matches(@"var erteleme = args\.GetDeferral\(\);[\s\S]*?args\.Cancel\(\);[\s\S]*?finally\s*\{[\s\S]*?erteleme\.Complete\(\);", kod);
    }

    /// <summary>"Yeniden dene" her menü sayfasında çalışır: sayfa ya TakipSayfasi'ndan türer ya da IYenilenebilir'dir.</summary>
    [Fact]
    public void Kabuktaki_her_menu_sayfasi_yenilenebilir()
    {
        var sayfalar = KabukSayfasi().Matches(Oku("AppShell.xaml")).Select(m => m.Groups[1].Value).Distinct().Where(s => s != "LoginPage").ToList();
        Assert.True(sayfalar.Count >= 12);
        var eksik = sayfalar.Where(s =>
        {
            var dosya = File.Exists(Path.Combine(Uygulama, "Views", s + ".xaml.cs")) ? s + ".xaml.cs" : s + ".cs";
            var kod = Oku(Path.Combine("Views", dosya));
            return !Regex.IsMatch(kod, $@"class {s} : (TakipSayfasi<\w+>|ContentPage, Controls\.IYenilenebilir)");
        }).ToList();
        Assert.True(eksik.Count == 0, "Yenilenemeyen sayfalar: " + string.Join(", ", eksik));
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~BaglantiSeridiTests|FullyQualifiedName~MauiKayitTutarliligiTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 1 farklı ileti; örnekler:

- `BaglantiSeridiTests.cs: CS0246: 'BaglantiSeridi' türü veya ad alanı adı bulunamadı (bir using yönergeniz veya derleme başvurunuz mu eksik?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App/AppShell.xaml.cs` (1/3) — Bul:

```csharp
using System.Diagnostics;
using Kasa.App.Core;

namespace Kasa.App;
```

Yerine:

```csharp
using System.Diagnostics;
using Kasa.App.Controls;
using Kasa.App.Core;

namespace Kasa.App;
```

`Kasa.App/AppShell.xaml.cs` (2/3) — Bul:

```csharp
    private readonly BildirimTiklamalari _bildirimTiklamalari;
    /// <summary>Uygulama açıkken 5 dakikada bir bildirim bakması (BildirimNobetcisi.Aralik); oturum açılınca başlar, girişe dönüşte durur.</summary>
    private readonly IDispatcherTimer _bildirimZamanlayicisi;

    public AppShell(AuthViewModel auth, BildirimNobetcisi bildirimNobetcisi, BildirimTiklamalari bildirimTiklamalari)
    {
        InitializeComponent();
        _auth = auth;
```

Yerine:

```csharp
    private readonly BildirimTiklamalari _bildirimTiklamalari;
    /// <summary>Uygulama açıkken 5 dakikada bir bildirim bakması (BildirimNobetcisi.Aralik); oturum açılınca başlar, girişe dönüşte durur.</summary>
    private readonly IDispatcherTimer _bildirimZamanlayicisi;
    /// <summary>Gezinme çubuğundaki bağlantı şeridi (Shell.TitleView; yalnız kopukken görünür).</summary>
    private readonly BaglantiSeridi _baglantiSeridi;
    private bool _yenileniyor, _birakmaSoruluyor;

    public AppShell(AuthViewModel auth, BildirimNobetcisi bildirimNobetcisi, BildirimTiklamalari bildirimTiklamalari, BaglantiDurumu baglanti)
    {
        InitializeComponent();
        _auth = auth;
```

`Kasa.App/AppShell.xaml.cs` (3/3) — Bul:

```csharp
                MainThread.BeginInvokeOnMainThread(() => _menuModeli.RozetAyarla(Bolum.Bildirimler, _bildirimNobetcisi.Yoklayici.Okunmamis));
        };
        Loaded += async (_, _) => await AcilistaYonlendirAsync();
    }

    /// <summary>Her gezinmede (menü, sayfalar arası bağlantı, girişe dönüş) seçili menü öğesi yeni konumdan belirlenir.</summary>
```

Yerine:

```csharp
                MainThread.BeginInvokeOnMainThread(() => _menuModeli.RozetAyarla(Bolum.Bildirimler, _bildirimNobetcisi.Yoklayici.Okunmamis));
        };
        Loaded += async (_, _) => await AcilistaYonlendirAsync();
        // Bağlantı şeridi (tasarım 2026-10-02 §3): kabukta tek şerit, bütün sayfaların gezinme çubuğunda (TitleView kabuktan
        // devralınır). "Yeniden dene" ve bağlantının geri gelmesi açık sayfayı bir kez yeniler.
        _baglantiSeridi = new BaglantiSeridi { BindingContext = baglanti };
        _baglantiSeridi.YenidenDeneIstendi += async (_, _) => await AcikSayfayiYenileAsync();
        baglanti.BaglantiGeldi += async (_, _) => await AcikSayfayiYenileAsync();
        SetTitleView(this, _baglantiSeridi);
    }

    /// <summary>Açık sayfayı yeniler (IYenilenebilir). Yenileme sürerken gelen ikinci istek (ör. yenilemenin ilk yanıtı bağlantıyı
    /// geri getirdi) yok sayılır; hata günlüğe yazılır (async void işleyiciden istisna çıkmaz).</summary>
    private async Task AcikSayfayiYenileAsync()
    {
        if (_yenileniyor || CurrentPage is not IYenilenebilir sayfa)
            return;
        _yenileniyor = true;
        _baglantiSeridi.Yenileniyor = true;
        try
        { await sayfa.YenileAsync(); }
        catch (Exception ex) { Debug.WriteLine($"Sayfa yenilenemedi: {ex}"); }
        finally
        {
            _yenileniyor = false;
            _baglantiSeridi.Yenileniyor = false;
        }
    }

    /// <summary>Sayfadan çıkış onayı (tasarım §2; MAUI Shell gezinme ertelemesi): açık sayfanın formunda kaydedilmemiş değişiklik
    /// varsa "Kaydedilmemiş değişiklik var. Bırakılsın mı?" sorulur. "Forma dön" gezinmeyi iptal eder; "Bırak" değişiklikleri bırakıp
    /// devam eder. Girişe dönüş (çıkış, oturumun sona ermesi) sorulmaz.</summary>
    protected override async void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);
        if (_birakmaSoruluyor || !args.CanCancel || args.Target?.Location?.OriginalString.Contains("login", StringComparison.Ordinal) == true
            || CurrentPage?.BindingContext is not IKaydedilmemisForm { KaydedilmemisDegisiklikVar: true } form)
            return;
        var erteleme = args.GetDeferral();
        _birakmaSoruluyor = true;
        try
        {
            if (await DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, KaydedilmemisDegisiklik.Ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon))
                form.DegisiklikleriBirak();
            else
                args.Cancel();
        }
        catch (Exception ex) { Debug.WriteLine($"Sayfadan çıkış onayı gösterilemedi: {ex}"); }
        finally
        {
            _birakmaSoruluyor = false;
            erteleme.Complete();
        }
    }

    /// <summary>Her gezinmede (menü, sayfalar arası bağlantı, girişe dönüş) seçili menü öğesi yeni konumdan belirlenir.</summary>
```

`Kasa.App/Controls/BaglantiSeridi.cs` (yeni dosya):

```csharp
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;

namespace Kasa.App.Controls;

/// <summary>
/// Kabuğun bağlantı şeridi (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3): bağlam BaglantiDurumu'dur; yalnız
/// kopukken görünür ve "Sunucuya ulaşılamıyor · Son bağlantı 14:05 · Yeniden dene" yazar. Kabuk onu Shell.TitleView olarak
/// bütün sayfaların gezinme çubuğuna koyar (tek şerit). "Yeniden dene" <see cref="YenidenDeneIstendi"/>'yi bildirir; kabuk açık
/// sayfayı yeniler.
/// </summary>
public class BaglantiSeridi : ContentView
{
    private readonly Button _dugme;

    public event EventHandler? YenidenDeneIstendi;

    public BaglantiSeridi()
    {
        var metin = new Label { Style = (Style)Application.Current!.Resources["LblError"], VerticalOptions = LayoutOptions.Center };
        metin.SetBinding(Label.TextProperty, "SeritMetni");
        _dugme = new Button { Text = "Yeniden dene", Style = (Style)Application.Current!.Resources["BtnSecondary"], VerticalOptions = LayoutOptions.Center };
        _dugme.Clicked += (_, _) => YenidenDeneIstendi?.Invoke(this, EventArgs.Empty);
        Content = new Border
        {
            Style = (Style)Application.Current!.Resources["ErrorBox"],
            Padding = new Thickness(12, 4),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout { Spacing = 12, Children = { metin, _dugme } },
        };
        SemanticProperties.SetHeadingLevel(metin, SemanticHeadingLevel.Level2);
        this.SetBinding(IsVisibleProperty, "Kopuk");
    }

    /// <summary>Yenileme sürerken düğme kapalıdır (çift tıklama ikinci yenilemeyi başlatmaz).</summary>
    public bool Yenileniyor { set => _dugme.IsEnabled = !value; }
}

/// <summary>Kabuğun "Yeniden dene" ve bağlantı geri gelince yenileyebildiği sayfa.</summary>
public interface IYenilenebilir
{
    Task YenileAsync();
}
```

`Kasa.App/Views/AlislarPage.xaml.cs` — Bul:

```csharp

namespace Kasa.App.Views;

public partial class AlislarPage : ContentPage, IQueryAttributable
{
    private readonly AlislarViewModel _vm;
    private int? _istenenAlisId;
    public void ApplyQueryAttributes(IDictionary<string, object> query)
```

Yerine:

```csharp

namespace Kasa.App.Views;

public partial class AlislarPage : ContentPage, Controls.IYenilenebilir, IQueryAttributable
{
    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    private readonly AlislarViewModel _vm;
    private int? _istenenAlisId;
    public void ApplyQueryAttributes(IDictionary<string, object> query)
```

`Kasa.App/Views/AyarlarPage.xaml.cs` (1/2) — Bul:

```csharp

namespace Kasa.App.Views;

public partial class AyarlarPage : ContentPage
{
    private readonly AyarlarViewModel _vm;
    private readonly GuvenlikViewModel _guvenlik;
```

Yerine:

```csharp

namespace Kasa.App.Views;

public partial class AyarlarPage : ContentPage, Controls.IYenilenebilir
{
    private readonly AyarlarViewModel _vm;
    private readonly GuvenlikViewModel _guvenlik;
```

`Kasa.App/Views/AyarlarPage.xaml.cs` (2/2) — Bul:

```csharp
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
        await _guvenlik.YukleAsync();
        await _esik.YukleAsync();
```

Yerine:

```csharp
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await YenileAsync();
    }

    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3): ayarlar, güvenlik ve eşikler.</summary>
    public async Task YenileAsync()
    {
        await _vm.YukleAsync();
        await _guvenlik.YukleAsync();
        await _esik.YukleAsync();
```

`Kasa.App/Views/AylikPage.xaml.cs` (1/2) — Bul:

```csharp

namespace Kasa.App.Views;

public partial class AylikPage : ContentPage
{
    private readonly AylikViewModel _vm;
    private readonly AyKilidiViewModel _kilit;
```

Yerine:

```csharp

namespace Kasa.App.Views;

public partial class AylikPage : ContentPage, Controls.IYenilenebilir
{
    private readonly AylikViewModel _vm;
    private readonly AyKilidiViewModel _kilit;
```

`Kasa.App/Views/AylikPage.xaml.cs` (2/2) — Bul:

```csharp
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
        await _kilit.YukleAsync();
    }
```

Yerine:

```csharp
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await YenileAsync();
    }

    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3): rapor ve ay kilidi.</summary>
    public async Task YenileAsync()
    {
        await _vm.YukleAsync();
        await _kilit.YukleAsync();
    }
```

`Kasa.App/Views/DisariAktarPage.cs` — Bul:

```csharp

namespace Kasa.App.Views;

public sealed class DisariAktarPage : ContentPage
{
    private readonly DisariAktarViewModel _vm;

    public DisariAktarPage(DisariAktarViewModel vm)
    {
        _vm = vm;
```

Yerine:

```csharp

namespace Kasa.App.Views;

public sealed class DisariAktarPage : ContentPage, Controls.IYenilenebilir
{
    private readonly DisariAktarViewModel _vm;

    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    public DisariAktarPage(DisariAktarViewModel vm)
    {
        _vm = vm;
```

`Kasa.App/Views/HaftalikPage.xaml.cs` — Bul:

```csharp

namespace Kasa.App.Views;

public partial class HaftalikPage : ContentPage
{
    private readonly HaftalikViewModel _vm;

    public HaftalikPage(HaftalikViewModel vm)
```

Yerine:

```csharp

namespace Kasa.App.Views;

public partial class HaftalikPage : ContentPage, Controls.IYenilenebilir
{
    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    private readonly HaftalikViewModel _vm;

    public HaftalikPage(HaftalikViewModel vm)
```

`Kasa.App/Views/IslemlerPage.xaml.cs` — Bul:

```csharp

namespace Kasa.App.Views;

public partial class IslemlerPage : ContentPage
{
    private readonly IslemlerViewModel _vm;

    // Rol ve oturum modelden gelir (OturumluViewModel): sayfa rolü ekrana atamaz.
```

Yerine:

```csharp

namespace Kasa.App.Views;

public partial class IslemlerPage : ContentPage, Controls.IYenilenebilir
{
    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    private readonly IslemlerViewModel _vm;

    // Rol ve oturum modelden gelir (OturumluViewModel): sayfa rolü ekrana atamaz.
```

`Kasa.App/Views/PanelPage.xaml.cs` — Bul:

```csharp

namespace Kasa.App.Views;

public partial class PanelPage : ContentPage
{
    private readonly PanelViewModel _vm;
    private readonly TakipOzetViewModel _takip;
    private readonly KasaKontrolViewModel _kontrol;
```

Yerine:

```csharp

namespace Kasa.App.Views;

public partial class PanelPage : ContentPage, Controls.IYenilenebilir
{
    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi (tasarım 2026-10-02 §3).</summary>
    public Task YenileAsync() => _vm.YukleAsync();

    private readonly PanelViewModel _vm;
    private readonly TakipOzetViewModel _takip;
    private readonly KasaKontrolViewModel _kontrol;
```

`Kasa.App/Views/TakipUi.cs` (1/2) — Bul:

```csharp
    }
}

public abstract class TakipSayfasi<T> : ContentPage where T : OturumluViewModel
{
    protected readonly T Vm;
    protected readonly VerticalStackLayout Govde = new() { Spacing = 18 };
```

Yerine:

```csharp
    }
}

public abstract class TakipSayfasi<T> : ContentPage, Controls.IYenilenebilir where T : OturumluViewModel
{
    protected readonly T Vm;
    protected readonly VerticalStackLayout Govde = new() { Spacing = 18 };
```

`Kasa.App/Views/TakipUi.cs` (2/2) — Bul:

```csharp
        Kaydirici = new ScrollView { Content = root };
        Gorunur = new Controls.GorunurYapici(Kaydirici);
        Content = Kaydirici;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
```

Yerine:

```csharp
        Kaydirici = new ScrollView { Content = root };
        Gorunur = new Controls.GorunurYapici(Kaydirici);
        Content = Kaydirici;
        // Başka kayda geçiş, Yeni ve Vazgeç'te kaydedilmemiş değişiklik onayı (tasarım 2026-10-02 §2).
        vm.BirakmaOnayi = ileti => DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon);
    }

    /// <summary>Kabuğun "Yeniden dene"si ve bağlantının geri gelmesi: sayfanın yüklemesi.</summary>
    public Task YenileAsync() => _yukle();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1115`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/Donusturuculer/BaglantiSeridiTests.cs Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglanti.cs Kasa.App/AppShell.xaml.cs Kasa.App/Controls/BaglantiSeridi.cs Kasa.App/Views/AlislarPage.xaml.cs Kasa.App/Views/AyarlarPage.xaml.cs Kasa.App/Views/AylikPage.xaml.cs Kasa.App/Views/DisariAktarPage.cs Kasa.App/Views/HaftalikPage.xaml.cs Kasa.App/Views/IslemlerPage.xaml.cs Kasa.App/Views/PanelPage.xaml.cs Kasa.App/Views/TakipUi.cs
git commit -F - <<'MESAJ'
feat(app): kabuk bağlantı şeridi ve sayfadan çıkış onayı

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 11: Arayüz — takip sayfalarında son veri ve "Henüz yüklenmedi"

Durum satırları `SonGuncellemeMetni`'ne bağlanır; `TakipSayfasi` gövdesi `GovdeGorunur`'a bağlı ve eski veride soluk.

**Dosyalar:**
- Değiştir: `Kasa.App/Views/TakipUi.cs`
- Test (değiştir): `Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.TakipUi.cs`
- Test (oluştur): `Kasa.App.Core.Tests/Donusturuculer/TakipSayfasiSonVeriTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.TakipUi.cs` (1/6) — Bul:

```csharp
/// doluyken görünür (boşken Yenile ile son güncelleme arasında ~130 px boşluk kalıyordu). (2) Onay kutusunun en küçük genişliği 0
/// (WinUI CheckBox'ın MinWidth=120'si etiketi ~110 px uzağa itiyordu). (3) Gösterge ayrı satırda değil, Yenile düğmesinin sağında
/// "İşleniyor…" metniyle aynı yatay satırdadır (gösterge görününce içerik 48 px aşağı kayıyordu); Kasa kontrolünün göstergesi de
/// artık bu satırdadır.</para></summary>
public partial class GorunumEsdegerligiTests
{
    public sealed record ListeSatiri(string Baslik, string Ozet, bool Acik);
```

Yerine:

```csharp
/// doluyken görünür (boşken Yenile ile son güncelleme arasında ~130 px boşluk kalıyordu). (2) Onay kutusunun en küçük genişliği 0
/// (WinUI CheckBox'ın MinWidth=120'si etiketi ~110 px uzağa itiyordu). (3) Gösterge ayrı satırda değil, Yenile düğmesinin sağında
/// "İşleniyor…" metniyle aynı yatay satırdadır (gösterge görününce içerik 48 px aşağı kayıyordu); Kasa kontrolünün göstergesi de
/// artık bu satırdadır. (4) Son güncelleme satırı modelin SonGuncellemeMetni'ne bağlıdır: hiç yükleme yokken "Henüz yüklenmedi.",
/// eski veride " · güncel olmayabilir" eki (tasarım 2026-10-02 §3). (5) TakipSayfasi gövdesi GovdeGorunur'a bağlıdır (son veri
/// varken hata da olsa görünür) ve eski veride soluktur.</para></summary>
public partial class GorunumEsdegerligiTests
{
    public sealed record ListeSatiri(string Baslik, string Ozet, bool Acik);
```

`Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.TakipUi.cs` (2/6) — Bul:

```csharp
        public string? Hata { get; set; }
        public string? Mesaj { get; set; }
        public DateTimeOffset? SonGuncelleme { get; set; } = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(3));
        public string Ozet { get; set; } = "Kart borcu 12.500,00 ₺";
        public string Metin { get; set; } = "Açıklama metni";
        public decimal Tutar { get; set; } = 250m;
```

Yerine:

```csharp
        public string? Hata { get; set; }
        public string? Mesaj { get; set; }
        public DateTimeOffset? SonGuncelleme { get; set; } = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(3));
        public string SonGuncellemeMetni => SonGuncelleme is { } z ? $"Son güncelleme: {z:dd.MM.yyyy HH:mm}" : "Henüz yüklenmedi.";
        public string Ozet { get; set; } = "Kart borcu 12.500,00 ₺";
        public string Metin { get; set; } = "Açıklama metni";
        public decimal Tutar { get; set; } = 250m;
```

`Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.TakipUi.cs` (3/6) — Bul:

```csharp
            }

            public static CheckBox OnayKutusu() => new() { MinimumWidthRequest = 0 };
        }

        public static Label Metin(string text) => new() { Text = text, FontSize = 13 };
```

Yerine:

```csharp
            }

            public static CheckBox OnayKutusu() => new() { MinimumWidthRequest = 0 };

            /// <summary>(4): son güncelleme satırı SonGuncellemeMetni'ne bağlı.</summary>
            public static Label Zaman()
            {
                var zaman = new Label { FontSize = 12 };
                zaman.SetBinding(Label.TextProperty, "SonGuncellemeMetni");
                return zaman;
            }

            /// <summary>(5): gövde GovdeGorunur'a bağlı, eski veride soluk.</summary>
            public static void Govde(VerticalStackLayout govde)
            {
                govde.SetBinding(VisualElement.IsVisibleProperty, "GovdeGorunur");
                var tetik = new DataTrigger(typeof(VerticalStackLayout)) { Binding = new Binding("VeriEski"), Value = true };
                tetik.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = 0.55 });
                govde.Triggers.Add(tetik);
            }
        }

        public static Label Metin(string text) => new() { Text = text, FontSize = 13 };
```

`Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.TakipUi.cs` (4/6) — Bul:

```csharp
            var mesaj = Bagli(nameof(vm.Mesaj));
            mesaj.TextColor = Colors.DarkGreen;
            root.Add(Bilincli.Doluysa(mesaj));
            var zaman = new Label { FontSize = 12 };
            zaman.SetBinding(Label.TextProperty, new Binding(nameof(vm.SonGuncelleme), stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}"));
            root.Add(zaman);
            govde.SetBinding(VisualElement.IsVisibleProperty, nameof(vm.VeriHazir));
            govde.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.Mesgul), converter: new Kasa.App.Converters.TersIseConverter());
            root.Add(govde);
            sayfa.Content = new ScrollView { Content = root };
```

Yerine:

```csharp
            var mesaj = Bagli(nameof(vm.Mesaj));
            mesaj.TextColor = Colors.DarkGreen;
            root.Add(Bilincli.Doluysa(mesaj));
            root.Add(Bilincli.Zaman());
            Bilincli.Govde(govde);
            govde.SetBinding(VisualElement.IsEnabledProperty, nameof(vm.Mesgul), converter: new Kasa.App.Converters.TersIseConverter());
            root.Add(govde);
            sayfa.Content = new ScrollView { Content = root };
```

`Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.TakipUi.cs` (5/6) — Bul:

```csharp
            panel.Add(Bilincli.YenileSatiri(Tikla("Yenile / tekrar dene", () => Task.CompletedTask), busy));
            panel.Add(Bilincli.Doluysa(hata));
            panel.Add(Bilincli.Doluysa(Bagli("Mesaj")));
            var tarih = new Label { FontSize = 12 };
            tarih.SetBinding(Label.TextProperty, new Binding("SonGuncelleme", stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}"));
            panel.Add(tarih);
            return panel;
        }
```

Yerine:

```csharp
            panel.Add(Bilincli.YenileSatiri(Tikla("Yenile / tekrar dene", () => Task.CompletedTask), busy));
            panel.Add(Bilincli.Doluysa(hata));
            panel.Add(Bilincli.Doluysa(Bagli("Mesaj")));
            panel.Add(Bilincli.Zaman());
            return panel;
        }
```

`Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.TakipUi.cs` (6/6) — Bul:

```csharp
            var hata = new Label { TextColor = Colors.DarkRed };
            hata.SetBinding(Label.TextProperty, "Hata");
            root.Add(Bilincli.Doluysa(hata));
            var zaman = new Label { FontSize = 12 };
            zaman.SetBinding(Label.TextProperty, new Binding("SonGuncelleme", stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}"));
            root.Add(zaman);
            root.Add(new Label { Text = "PDF için rapor tarayıcıda açılır. Ctrl+P menüsünden PDF yazıcısını seçin.", FontSize = 13 });
            root.Add(new VerticalStackLayout { Spacing = 4, Children = { new Label { Text = "Başlangıç tarihi", FontSize = 12 }, new DatePicker() } });
            return root;
```

Yerine:

```csharp
            var hata = new Label { TextColor = Colors.DarkRed };
            hata.SetBinding(Label.TextProperty, "Hata");
            root.Add(Bilincli.Doluysa(hata));
            root.Add(Bilincli.Zaman());
            root.Add(new Label { Text = "PDF için rapor tarayıcıda açılır. Ctrl+P menüsünden PDF yazıcısını seçin.", FontSize = 13 });
            root.Add(new VerticalStackLayout { Spacing = 4, Children = { new Label { Text = "Başlangıç tarihi", FontSize = 12 }, new DatePicker() } });
            return root;
```

`Kasa.App.Core.Tests/Donusturuculer/TakipSayfasiSonVeriTests.cs` (yeni dosya):

```csharp
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Kodla yazılmış takip sayfalarında son veri (tasarım 2026-10-02 §3): hiç yükleme yokken "Henüz yüklenmedi.", yükleme
/// hata verse de son veri görünür ve soluk, son güncelleme satırı "güncel olmayabilir" der.</summary>
public class TakipSayfasiSonVeriTests
{
    private sealed class DenemeSayfasi(AyarlarViewModel vm) : TakipSayfasi<AyarlarViewModel>(vm, "Deneme", "Açıklama", () => Task.CompletedTask)
    {
        public VerticalStackLayout GovdeAlani => Govde;
    }

    [Fact]
    public void Govde_son_veri_varken_gorunur_eski_veride_soluk_zaman_satiri_durumu_soyler()
    {
        GorunumOrtami.Kur();
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api, new AuthViewModel(api));
        var sayfa = new DenemeSayfasi(vm);
        var zaman = sayfa.GetVisualTreeDescendants().OfType<Label>().Single(l => l.Text?.Contains("yüklenmedi") == true || l.Text?.StartsWith("Son güncelleme") == true);

        Assert.Equal("Henüz yüklenmedi.", zaman.Text);
        Assert.False(sayfa.GovdeAlani.IsVisible);

        vm.SonGuncelleme = new DateTimeOffset(2026, 10, 2, 14, 5, 0, TimeSpan.FromHours(3));
        vm.VeriEski = true;
        Assert.True(sayfa.GovdeAlani.IsVisible);
        Assert.Equal(TakipUi.EskiVeriOpakligi, sayfa.GovdeAlani.Opacity);
        Assert.Equal("Son güncelleme: 02.10.2026 14:05 · güncel olmayabilir", zaman.Text);

        vm.VeriEski = false;
        Assert.Equal(1, sayfa.GovdeAlani.Opacity);
        Assert.Equal("Son güncelleme: 02.10.2026 14:05", zaman.Text);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~TakipSayfasiSonVeriTests|FullyQualifiedName~GorunumEsdegerligiTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 1 farklı ileti; örnekler:

- `TakipSayfasiSonVeriTests.cs: CS0117: 'TakipUi' bir 'EskiVeriOpakligi' tanımı içermiyor`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App/Views/TakipUi.cs` (1/4) — Bul:

```csharp

    /// <summary>Kodla yazılmış sayfaların durum satırları (Yenile ve sağında yükleniyor göstergesi, hata, isteğe bağlı ileti, son
    /// güncelleme): TakipSayfasi, Kasa kontrolü ve Dışa aktar aynı sırayı ve stilleri kullanır. Bağlam modelinde Mesgul,
    /// Hata (ya da <paramref name="hataYolu"/>) ve SonGuncelleme beklenir. Kartlar ekranı hatayı form açıkken formun içinde
    /// gösterdiği için buraya SayfaHatasi'nı bağlar. Hata ve ileti yalnız doluyken yer kaplar (boşken Yenile ile son güncelleme
    /// arasında ~130 px boşluk kalıyordu). Dönen değer hata satırıdır (sayfa onu görünür yere kaydırabilir).</summary>
    public static Label DurumSatirlari(Layout hedef, View yenile, Label? mesaj = null, string hataYolu = "Hata")
```

Yerine:

```csharp

    /// <summary>Kodla yazılmış sayfaların durum satırları (Yenile ve sağında yükleniyor göstergesi, hata, isteğe bağlı ileti, son
    /// güncelleme): TakipSayfasi, Kasa kontrolü ve Dışa aktar aynı sırayı ve stilleri kullanır. Bağlam modelinde Mesgul,
    /// Hata (ya da <paramref name="hataYolu"/>) ve SonGuncellemeMetni beklenir (hiç yükleme yokken "Henüz yüklenmedi.", veri
    /// eskiyse " · güncel olmayabilir" ekiyle; OturumluViewModel). Kartlar ekranı hatayı form açıkken formun içinde
    /// gösterdiği için buraya SayfaHatasi'nı bağlar. Hata ve ileti yalnız doluyken yer kaplar (boşken Yenile ile son güncelleme
    /// arasında ~130 px boşluk kalıyordu). Dönen değer hata satırıdır (sayfa onu görünür yere kaydırabilir).</summary>
    public static Label DurumSatirlari(Layout hedef, View yenile, Label? mesaj = null, string hataYolu = "Hata")
```

`Kasa.App/Views/TakipUi.cs` (2/4) — Bul:

```csharp
        if (mesaj is not null)
            hedef.Add(DoluysaGoster(mesaj));
        var zaman = new Label { Style = (Style)Application.Current!.Resources["LblTakipKucuk"] };
        zaman.SetBinding(Label.TextProperty, new Binding("SonGuncelleme", stringFormat: "Son güncelleme: {0:dd.MM.yyyy HH:mm}"));
        hedef.Add(zaman);
        return hata;
    }
```

Yerine:

```csharp
        if (mesaj is not null)
            hedef.Add(DoluysaGoster(mesaj));
        var zaman = new Label { Style = (Style)Application.Current!.Resources["LblTakipKucuk"] };
        zaman.SetBinding(Label.TextProperty, nameof(OturumluViewModel.SonGuncellemeMetni));
        hedef.Add(zaman);
        return hata;
    }
```

`Kasa.App/Views/TakipUi.cs` (3/4) — Bul:

```csharp
        return new HorizontalStackLayout { Spacing = 12, Children = { yenile, gosterge, metin } };
    }

    /// <summary>Etiket yalnız metni doluyken görünür (boş hata/ileti satırı yığında yer ve aralık kaplamaz).</summary>
    private static Label DoluysaGoster(Label etiket)
    {
```

Yerine:

```csharp
        return new HorizontalStackLayout { Spacing = 12, Children = { yenile, gosterge, metin } };
    }

    /// <summary>Eski veri soluk (opaklık <see cref="EskiVeriOpakligi"/>): bağlamın VeriEski'si doğruyken (tasarım 2026-10-02 §3).</summary>
    public static DataTrigger EskiVeriSolugu(Type hedef)
    {
        var tetik = new DataTrigger(hedef) { Binding = new Binding("VeriEski"), Value = true };
        tetik.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = EskiVeriOpakligi });
        return tetik;
    }

    /// <summary>Eski verinin opaklığı (XAML sayfaları da aynı değeri yazar).</summary>
    public const double EskiVeriOpakligi = 0.55;

    /// <summary>Etiket yalnız metni doluyken görünür (boş hata/ileti satırı yığında yer ve aralık kaplamaz).</summary>
    private static Label DoluysaGoster(Label etiket)
    {
```

`Kasa.App/Views/TakipUi.cs` (4/4) — Bul:

```csharp
        mesaj.SetBinding(Label.TextProperty, nameof(vm.Mesaj));
        MesajSatiri = mesaj;
        HataSatiri = TakipUi.DurumSatirlari(root, TakipUi.Tikla("Yenile / tekrar dene", yukle), mesaj, hataYolu: hataYolu);
        Govde.SetBinding(IsVisibleProperty, nameof(vm.VeriHazir));
        Govde.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        root.Add(Govde);
        Kaydirici = new ScrollView { Content = root };
        Gorunur = new Controls.GorunurYapici(Kaydirici);
```

Yerine:

```csharp
        mesaj.SetBinding(Label.TextProperty, nameof(vm.Mesaj));
        MesajSatiri = mesaj;
        HataSatiri = TakipUi.DurumSatirlari(root, TakipUi.Tikla("Yenile / tekrar dene", yukle), mesaj, hataYolu: hataYolu);
        // Yükleme hata verse de son başarılı veri görünür kalır ve soluk gösterilir (tasarım 2026-10-02 §3).
        Govde.SetBinding(IsVisibleProperty, nameof(vm.GovdeGorunur));
        Govde.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new Converters.TersIseConverter());
        Govde.Triggers.Add(TakipUi.EskiVeriSolugu(typeof(VerticalStackLayout)));
        root.Add(Govde);
        Kaydirici = new ScrollView { Content = root };
        Gorunur = new Controls.GorunurYapici(Kaydirici);
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1116`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/Donusturuculer/GorunumEsdegerligiTests.TakipUi.cs Kasa.App.Core.Tests/Donusturuculer/TakipSayfasiSonVeriTests.cs Kasa.App/Views/TakipUi.cs
git commit -F - <<'MESAJ'
feat(app): takip sayfalarında son veri ve Henüz yüklenmedi

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 12: İşlemler — alan hataları, düzenleme modu, kaydedilmemiş değişiklik

Gider formu `Hatalar` ve sunucu eşlemesiyle; "Yeni işlem" / "Düzenleniyor: …" başlığı, "Değişikliği kaydet", Vazgeç, satır vurgusu (`EsitIseConverter`); "Düzenle" ve "Yeni" onay ister; tip seçenekleri sabit (HD-02).

**Dosyalar:**
- Değiştir: `Kasa.App.Core/IslemlerViewModel.cs`
- Değiştir: `Kasa.App/App.xaml`
- Oluştur: `Kasa.App/Converters/EsitIseConverter.cs`
- Değiştir: `Kasa.App/Views/IslemlerPage.xaml`
- Değiştir: `Kasa.App/Views/IslemlerPage.xaml.cs`
- Test (değiştir): `Kasa.App.Core.Tests/AlislarViewModelTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/BenzerKayitTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/CekirdekSurumVmTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/Donusturuculer/DonusturucuTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglamalar.cs`
- Test (değiştir): `Kasa.App.Core.Tests/GecersizTutarTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/IslemEditorTests.cs`
- Test (oluştur): `Kasa.App.Core.Tests/IslemFormuTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/RaporKuraliVmTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/YurutucuTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/AlislarViewModelTests.cs` — Bul:

```csharp
        var gider = new IslemDto(9, new(2026, 9, 20), "Firma", 10m, "Dağılım bekliyor", GiderTipi.Cari, null, AlisId: 7);
        vm.Duzenle(gider);
        Assert.Equal(0, vm.DuzenId);
        Assert.Contains("Alışlar", vm.Hata);
        await vm.SilCommand.ExecuteAsync(gider);
        Assert.Null(api.SonIslemSil);
        Assert.Contains("Alışlar", vm.Hata);
```

Yerine:

```csharp
        var gider = new IslemDto(9, new(2026, 9, 20), "Firma", 10m, "Dağılım bekliyor", GiderTipi.Cari, null, AlisId: 7);
        vm.Duzenle(gider);
        Assert.Equal(0, vm.DuzenId);
        Assert.Contains("Alışlar", vm.Hatalar.Genel);
        await vm.SilCommand.ExecuteAsync(gider);
        Assert.Null(api.SonIslemSil);
        Assert.Contains("Alışlar", vm.Hata);
```

`Kasa.App.Core.Tests/BenzerKayitTests.cs` — Bul:

```csharp
        await vm.GideriAyriKaydetCommand.ExecuteAsync(null);
        Assert.Equal(100, finans.SonIslemOlustur!.TutarTl);
        Assert.Equal(1, lookup.Cagri);
        var hatali = new IslemlerViewModel(new SahteApi(), TestOturumu.Ac(), new Sahte { Hata = true }) { DuzenTutar = 100 };
        await hatali.KaydetCommand.ExecuteAsync(null);
        Assert.Contains("ulaşılamadı", hatali.Hata);
        Assert.False(hatali.GiderBenzerlik.Onayla());
    }
    [Fact]
```

Yerine:

```csharp
        await vm.GideriAyriKaydetCommand.ExecuteAsync(null);
        Assert.Equal(100, finans.SonIslemOlustur!.TutarTl);
        Assert.Equal(1, lookup.Cagri);
        var hatali = new IslemlerViewModel(new SahteApi(), TestOturumu.Ac(), new Sahte { Hata = true }) { DuzenCari = "Mal", DuzenTutar = 100, DuzenKanal = "MEZAT" };
        await hatali.KaydetCommand.ExecuteAsync(null);
        Assert.Contains("ulaşılamadı", hatali.Hatalar.Genel);
        Assert.False(hatali.GiderBenzerlik.Onayla());
    }
    [Fact]
```

`Kasa.App.Core.Tests/CekirdekSurumVmTests.cs` — Bul:

```csharp

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(GiderIletisi, vm.Hata);
        Assert.True(api.IslemlerCagri > okuma, "Liste güncel kayıtlarla yenilendi.");
        Assert.Equal((5, 80m), (vm.DuzenId, vm.DuzenTutar));
        Assert.False(vm.Mesgul);
```

Yerine:

```csharp

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(GiderIletisi, vm.Hatalar.Genel);
        Assert.True(api.IslemlerCagri > okuma, "Liste güncel kayıtlarla yenilendi.");
        Assert.Equal((5, 80m), (vm.DuzenId, vm.DuzenTutar));
        Assert.False(vm.Mesgul);
```

`Kasa.App.Core.Tests/Donusturuculer/DonusturucuTests.cs` — Bul:

```csharp
        Assert.Equal(bilinen.Select(t => t.Name).Order(), derlenen.Select(t => t.Name).Order());
    }

    [Theory]
    [InlineData(1, "Ocak")]
    [InlineData(2, "Şubat")]
```

Yerine:

```csharp
        Assert.Equal(bilinen.Select(t => t.Name).Order(), derlenen.Select(t => t.Name).Order());
    }

    /// <summary>Düzenlenen satırın vurgusu: satırın Id'si formun DuzenId'sine eşitse (yeni formda DuzenId 0, hiçbir satır eşleşmez).</summary>
    [Fact]
    public void EsitIse_iki_dolu_ve_esit_degerde_true()
    {
        var d = new EsitIseConverter();
        Assert.Equal(true, d.Convert([5, 5], typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(false, d.Convert([5, 0], typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(false, d.Convert([null, null], typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(false, d.Convert([5], typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(1, "Ocak")]
    [InlineData(2, "Şubat")]
```

`Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglamalar.cs` (1/2) — Bul:

```csharp
            { Hatalar.Add($"{yer}: bağlam türü bilinmiyor."); return null; }
            if (yol == ".")
                return tur;
            foreach (var parca in yol.Split('.'))
            {
                if (parca.Contains('['))
                { Hatalar.Add($"{yer}: dizinli yol desteklenmiyor ({yol})."); return null; }
                var t = Nullable.GetUnderlyingType(tur!) ?? tur!;
                var ozellik = Ozellik(t, parca);
                if (ozellik is null)
```

Yerine:

```csharp
            { Hatalar.Add($"{yer}: bağlam türü bilinmiyor."); return null; }
            if (yol == ".")
                return tur;
            var kok = tur;
            foreach (var parca in yol.Split('.'))
            {
                if (parca.Contains('['))
                {
                    tur = DizinliCoz(parca, tur!, kok, yol, yer);
                    if (tur is null)
                        return null;
                    continue;
                }
                var t = Nullable.GetUnderlyingType(tur!) ?? tur!;
                var ozellik = Ozellik(t, parca);
                if (ozellik is null)
```

`Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglamalar.cs` (2/2) — Bul:

```csharp
            return tur;
        }

        private static PropertyInfo? Ozellik(Type t, string ad)
        {
            var turler = t.IsInterface ? t.GetInterfaces().Prepend(t) : [t];
```

Yerine:

```csharp
            return tur;
        }

        /// <summary>Dizinli parça ("Hatalar[DuzenCari]"): özellik string dizinleyicisi olan bir türdür; AlanHatalari'nın anahtarı
        /// bağlamın (formun görünüm modeli) gerçek bir özelliğidir (alan hatası yanlış adla bağlanıp hiç görünmez kalmasın).</summary>
        private Type? DizinliCoz(string parca, Type tur, Type kok, string yol, string yer)
        {
            var ac = parca.IndexOf('[');
            var (ad, anahtar) = (parca[..ac], parca[(ac + 1)..^1]);
            var t = Nullable.GetUnderlyingType(tur) ?? tur;
            if (Ozellik(t, ad) is not { } ozellik)
            { Hatalar.Add($"{yer}: {t.Name} türünde '{ad}' özelliği yok ({yol})."); return null; }
            var dizin = ozellik.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.GetIndexParameters() is [{ ParameterType: var pt }] && pt == typeof(string));
            if (dizin is null)
            { Hatalar.Add($"{yer}: dizinli yol yalnız string dizinleyicide desteklenir ({yol})."); return null; }
            if (ozellik.PropertyType == typeof(AlanHatalari) && Ozellik(kok, anahtar) is null)
            { Hatalar.Add($"{yer}: AlanHatalari anahtarı '{anahtar}' {kok.Name} türünde özellik değil ({yol})."); return null; }
            return dizin.PropertyType;
        }

        private static PropertyInfo? Ozellik(Type t, string ad)
        {
            var turler = t.IsInterface ? t.GetInterfaces().Prepend(t) : [t];
```

`Kasa.App.Core.Tests/GecersizTutarTests.cs` — Bul:

```csharp
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.IslemOlusturCagri);
        Assert.Null(api.SonIslemOlustur);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
    }

    [Fact]
```

Yerine:

```csharp
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.IslemOlusturCagri);
        Assert.Null(api.SonIslemOlustur);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hatalar[nameof(IslemlerViewModel.DuzenTutar)]);
    }

    [Fact]
```

`Kasa.App.Core.Tests/IslemEditorTests.cs` — Bul:

```csharp
        var vm = new IslemlerViewModel(api, TestOturumu.Ac()) { DuzenTarih = new DateTime(2026, 3, 5), DuzenCari = "Kargo", DuzenTutar = 75m, DuzenKanal = "MEZAT", DuzenTip = GiderTipi.Cari };

        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Hata);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.IslemOlusturmalari.Count);
        Assert.NotNull(api.IslemOlusturmalari[0].IstekId);
```

Yerine:

```csharp
        var vm = new IslemlerViewModel(api, TestOturumu.Ac()) { DuzenTarih = new DateTime(2026, 3, 5), DuzenCari = "Kargo", DuzenTutar = 75m, DuzenKanal = "MEZAT", DuzenTip = GiderTipi.Cari };

        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Hatalar.Genel);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.IslemOlusturmalari.Count);
        Assert.NotNull(api.IslemOlusturmalari[0].IstekId);
```

`Kasa.App.Core.Tests/IslemFormuTests.cs` (yeni dosya):

```csharp
using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>İşlemler gider formu (tasarım 2026-10-02 §1-3; İŞ-02, İŞ-03, HD-02): ön doğrulama, sunucu alan eşlemesi, temizleme,
/// düzenleme başlığı, kaydedilmemiş değişiklik onayı ve kopukken kaydetme.</summary>
public class IslemFormuTests
{
    private static readonly IslemDto Kargo = new(5, new DateOnly(2026, 3, 5), "Kargo", 75m, "MEZAT", GiderTipi.Cari, null);

    private static IslemlerViewModel Vm(SahteApi? api = null) => new(api ?? new SahteApi(), TestOturumu.Ac());

    private static void Doldur(IslemlerViewModel vm)
    {
        vm.DuzenTarih = new DateTime(2026, 3, 5);
        vm.DuzenCari = "Ege Gıda";
        vm.DuzenTutar = 150m;
        vm.DuzenKanal = "MEZAT";
    }

    [Fact]
    public async Task Bos_form_istek_gondermez_alanlari_adiyla_soyler_ilk_alan_aciklamadir()
    {
        var api = new SahteApi();
        var vm = Vm(api);
        var gosterim = 0;
        vm.Hatalar.GosterIstendi += (_, _) => gosterim++;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(0, api.IslemOlusturCagri);
        Assert.Equal("Açıklama boş olamaz.", vm.Hatalar[nameof(vm.DuzenCari)]);
        Assert.Equal("Tutar sıfır olamaz.", vm.Hatalar[nameof(vm.DuzenTutar)]);
        Assert.Equal("Kanal seçin.", vm.Hatalar[nameof(vm.DuzenKanal)]);
        Assert.Equal(nameof(vm.DuzenCari), vm.Hatalar.IlkAlan);
        Assert.Null(vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
        Assert.Equal(1, gosterim);
    }

    [Fact]
    public async Task Sunucu_alan_hatasi_alana_eslenmeyen_genel_hataya_gider()
    {
        var alanlar = new Dictionary<string, string> { ["cari"] = "En fazla 200 karakter girilebilir.", ["kredikartiid"] = "Bu kart yeni kullanıma kapalı.", ["istekid"] = "Geçerli bir istek kimliği gerekir." };
        var api = new SahteApi { IslemOlusturHatasi = new KasaApiException(HttpStatusCode.BadRequest, "birleşik", alanHatalari: alanlar) };
        var vm = Vm(api);
        Doldur(vm);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal("En fazla 200 karakter girilebilir.", vm.Hatalar[nameof(vm.DuzenCari)]);
        Assert.Equal("Bu kart yeni kullanıma kapalı.", vm.Hatalar[nameof(vm.DuzenKrediKartiId)]);
        Assert.Equal("Geçerli bir istek kimliği gerekir.", vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Hata_alan_degisince_kayit_degisince_yenide_ve_basarida_kalkar()
    {
        var vm = Vm();
        await vm.KaydetCommand.ExecuteAsync(null);

        vm.DuzenCari = "Ege";
        Assert.Null(vm.Hatalar[nameof(vm.DuzenCari)]);
        Assert.NotNull(vm.Hatalar[nameof(vm.DuzenTutar)]);

        vm.Duzenle(Kargo);
        Assert.False(vm.Hatalar.Var);

        vm.DuzenCari = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.Hatalar.Var);
        vm.DegisiklikleriBirak();
        Assert.False(vm.Hatalar.Var);

        vm.Hatalar.Genel = "eski";
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.False(vm.Hatalar.Var);

        Doldur(vm);
        vm.Hatalar.Genel = "eski";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.False(vm.Hatalar.Var);
        Assert.Equal("Gider kaydedildi.", vm.Mesaj);
    }

    [Fact]
    public void Duzenleme_basligi_ve_dugmeleri_modu_soyler()
    {
        var vm = Vm();
        Assert.Equal(("Yeni işlem", "Kaydet", false), (vm.FormBasligi, vm.KaydetMetni, vm.DuzenlemeModu));

        vm.Duzenle(Kargo);
        vm.DuzenCari = "Kargo (düzeltildi)";

        Assert.Equal(("Düzenleniyor: 05.03.2026 · Kargo", "Değişikliği kaydet", true), (vm.FormBasligi, vm.KaydetMetni, vm.DuzenlemeModu));
    }

    [Fact]
    public async Task Yazilmis_form_baska_kayda_gecmeden_ve_yeniden_once_onay_ister()
    {
        var vm = Vm();
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        vm.DuzenCari = "Deneme gideri";
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var sorulan = 0;
        var cevap = false;
        vm.BirakmaOnayi = ileti => { sorulan++; Assert.Equal(KaydedilmemisDegisiklik.Ileti, ileti); return Task.FromResult(cevap); };

        await vm.DuzenlemeyeGecCommand.ExecuteAsync(Kargo);
        Assert.Equal(("Deneme gideri", 0), (vm.DuzenCari, vm.DuzenId));   // "Forma dön"
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal("Deneme gideri", vm.DuzenCari);

        cevap = true;                                                      // "Bırak"
        await vm.DuzenlemeyeGecCommand.ExecuteAsync(Kargo);
        Assert.Equal((5, "Kargo"), (vm.DuzenId, vm.DuzenCari));
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        Assert.Equal(3, sorulan);

        vm.DuzenTutar = 80m;
        vm.DegisiklikleriBirak();
        Assert.Equal(75m, vm.DuzenTutar);
        vm.DuzenTutar = 90m;
        vm.VazgecCommand.Execute(null);   // "Vazgeç" bilerek bırakmaktır: sorulmaz
        Assert.Equal((0, 0m), (vm.DuzenId, vm.DuzenTutar));
        Assert.Equal(3, sorulan);
    }

    [Fact]
    public async Task Kopukken_kaydetme_formu_korur_ve_kayit_yapilmadigini_soyler()
    {
        var api = new SahteApi { IslemOlusturHatasi = new HttpRequestException() };
        var vm = Vm(api);
        Doldur(vm);

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal("Sunucuya ulaşılamadı. Kayıt yapılmadı; bağlantı gelince yeniden kaydedin.", vm.Hatalar.Genel);
        Assert.Equal(("Ege Gıda", 150m, "MEZAT"), (vm.DuzenCari, vm.DuzenTutar, vm.DuzenKanal));
        Assert.True(vm.KaydedilmemisDegisiklikVar);
    }

    [Fact]
    public async Task Tip_secenekleri_sabit_kanal_secenekleri_son_basarili_yuklemeden_gelir()
    {
        var api = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0m)] };
        var vm = Vm(api);
        Assert.Equal(["Diğer gider", "Sabit gider", "Kredi kartı"], vm.TipCipleri.Select(c => c.Ad));
        Assert.True(vm.TipCipleri[0].Secili);

        await vm.YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();
        await vm.YukleAsync();

        Assert.Equal(3, vm.TipCipleri.Count);
        Assert.Equal(["MEZAT", "Ortak"], vm.GiderKanallari.Select(c => c.Ad));
    }
}
```

`Kasa.App.Core.Tests/RaporKuraliVmTests.cs` (1/3) — Bul:

```csharp
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));
        Assert.Null(vm.KartUyarisi);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartIletisi, vm.Hata);
        Assert.Null(api.SonIslemOlustur);

        vm.SecKartCommand.Execute(vm.KartCipleri.Single());
```

Yerine:

```csharp
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));
        Assert.Null(vm.KartUyarisi);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartIletisi, vm.Hatalar[nameof(IslemlerViewModel.DuzenKrediKartiId)]);
        Assert.Null(api.SonIslemOlustur);

        vm.SecKartCommand.Execute(vm.KartCipleri.Single());
```

`Kasa.App.Core.Tests/RaporKuraliVmTests.cs` (2/3) — Bul:

```csharp
        Assert.Equal(new[] { (2, "Takipli") }, vm.KartCipleri.Select(k => (k.Id, k.Ad))); // önceki kaydın eski kartı listeden kalkar
        vm.DuzenNot = "Dekont";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal((21, (int?)null, "Dekont"), (api.SonIslemGuncelle!.Value.Id, api.SonIslemGuncelle.Value.G.KrediKartiId, api.SonIslemGuncelle.Value.G.Not));

        // Yeni kayda dönünce kartsız kredi kartı gideri yine reddedilir.
```

Yerine:

```csharp
        Assert.Equal(new[] { (2, "Takipli") }, vm.KartCipleri.Select(k => (k.Id, k.Ad))); // önceki kaydın eski kartı listeden kalkar
        vm.DuzenNot = "Dekont";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.False(vm.Hatalar.Var);
        Assert.Equal((21, (int?)null, "Dekont"), (api.SonIslemGuncelle!.Value.Id, api.SonIslemGuncelle.Value.G.KrediKartiId, api.SonIslemGuncelle.Value.G.Not));

        // Yeni kayda dönünce kartsız kredi kartı gideri yine reddedilir.
```

`Kasa.App.Core.Tests/RaporKuraliVmTests.cs` (3/3) — Bul:

```csharp
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));
        api.SonIslemOlustur = null;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartIletisi, vm.Hata);
        Assert.Null(api.SonIslemOlustur);
    }
}
```

Yerine:

```csharp
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));
        api.SonIslemOlustur = null;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartIletisi, vm.Hatalar[nameof(IslemlerViewModel.DuzenKrediKartiId)]);
        Assert.Null(api.SonIslemOlustur);
    }
}
```

`Kasa.App.Core.Tests/YurutucuTests.cs` — Bul:

```csharp
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var vm = new IslemlerViewModel(new SahteApi(), auth: auth) { DuzenTutar = ParaAyristirici.Gecersiz };
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        auth.OturumSurumu++;

        Assert.Null(vm.Hata);
    }
}
```

Yerine:

```csharp
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var vm = new IslemlerViewModel(new SahteApi(), auth: auth) { DuzenTutar = ParaAyristirici.Gecersiz };
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hatalar[nameof(IslemlerViewModel.DuzenTutar)]);

        auth.OturumSurumu++;

        Assert.False(vm.Hatalar.Var);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~IslemFormuTests|FullyQualifiedName~IslemEditorTests|FullyQualifiedName~RaporKuraliVmTests|FullyQualifiedName~MauiKayitTutarliligiTests|FullyQualifiedName~DonusturucuTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 17 farklı ileti; örnekler:

- `CekirdekSurumVmTests.cs: CS1061: 'IslemlerViewModel' bir 'Hatalar' tanımı içermiyor ve 'IslemlerViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'Hatalar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `BenzerKayitTests.cs: CS1061: 'IslemlerViewModel' bir 'Hatalar' tanımı içermiyor ve 'IslemlerViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'Hatalar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `GecersizTutarTests.cs: CS1061: 'IslemlerViewModel' bir 'Hatalar' tanımı içermiyor ve 'IslemlerViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'Hatalar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `DonusturucuTests.cs: CS0246: 'EsitIseConverter' türü veya ad alanı adı bulunamadı (bir using yönergeniz veya derleme başvurunuz mu eksik?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/IslemlerViewModel.cs` (1/8) — Bul:

```csharp

namespace Kasa.App.Core;

public partial class IslemlerViewModel : OturumluViewModel
{
    private readonly IKasaApi _api;
    /// <summary>Yeni gider için tekrar anahtarı (appcore-5): istek zaman aşımına uğrayıp sunucuda yine de kaydedildiyse aynı
```

Yerine:

```csharp

namespace Kasa.App.Core;

public partial class IslemlerViewModel : OturumluViewModel, IKaydedilmemisForm
{
    private readonly IKasaApi _api;
    /// <summary>Yeni gider için tekrar anahtarı (appcore-5): istek zaman aşımına uğrayıp sunucuda yine de kaydedildiyse aynı
```

`Kasa.App.Core/IslemlerViewModel.cs` (2/8) — Bul:

```csharp
        _listeHatti = new(Yurutucu);
        _kaynakHatti = new(Yurutucu);
        _gelenHatti = new(Yurutucu);
    }

    /// <summary>Oturum değişince bekleyen kayıt, liste ve gelir yanıtları eskir (sonuçları, hataları ve bitişleri yansımaz; gösterge,
    /// hata ve ileti tabanda kalkar); önceki oturumun formu, listesi ve gelir formu UI bağlamında kalkar.</summary>
    protected override void OturumTemizle()
    {
        GiderBenzerlik.Temizle();
        Yeni();
        GelenTemizle();
        ListeTemizle();
    }
```

Yerine:

```csharp
        _listeHatti = new(Yurutucu);
        _kaynakHatti = new(Yurutucu);
        _gelenHatti = new(Yurutucu);
        // HD-02: tip seçenekleri sabittir; kaynak yüklemesi başarısız olsa da görünür.
        foreach (var t in new[] { GiderTipi.Cari, GiderTipi.SabitGider, GiderTipi.KrediKarti })
            TipCipleri.Add(new SecimCipi(TipAdi(t)));
        TipVurgu();
        _form = new(() => FormGovdesi());
        _form.Ac();
    }

    /// <summary>Gider formunun hataları (tasarım 2026-10-02 §1): alan → ileti ve formun genel hatası.</summary>
    public AlanHatalari Hatalar { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [Hatalar];

    /// <summary>Sunucunun gider doğrulama alanları (KayitGirdileri.Islem; küçük harf) → formun alanları.</summary>
    private static readonly Dictionary<string, string> SunucuAlanlari = new()
    {
        ["tarih"] = nameof(DuzenTarih),
        ["cari"] = nameof(DuzenCari),
        ["tutartl"] = nameof(DuzenTutar),
        ["kanal"] = nameof(DuzenKanal),
        ["tip"] = nameof(DuzenTip),
        ["not"] = nameof(DuzenNot),
        ["kredikartiid"] = nameof(DuzenKrediKartiId),
        ["taksitsayisi"] = nameof(DuzenTaksitSayisi),
        ["ilkkesimtarihi"] = nameof(DuzenIlkKesimTarihi),
    };

    /// <summary>Gider formunun açıldığı andaki değerleri (yeni ya da düzenlenen kayıt): kaydedilmemiş değişiklik ölçütü.</summary>
    private readonly KaydedilmemisDegisiklik _form;
    public bool KaydedilmemisDegisiklikVar => _form.Var;

    /// <summary>Yazılmış değişiklikleri bırakır: düzenlenen kayıt açıldığı değerlerine, yeni form boşa döner.</summary>
    public void DegisiklikleriBirak()
    {
        if (_duzenlenen is { } kayit)
            Duzenle(kayit);
        else
            FormuSifirla();
    }

    /// <summary>Form başlığı (İŞ-03): yeni kayıtta "Yeni işlem", düzenlemede "Düzenleniyor: 02.08.2026 · Ege Gıda".</summary>
    public string FormBasligi => DuzenId == 0 || _duzenlenen is null ? "Yeni işlem" : $"Düzenleniyor: {_duzenlenen.Tarih:dd.MM.yyyy} · {_duzenlenen.Cari}";
    public string KaydetMetni => DuzenId == 0 ? "Kaydet" : "Değişikliği kaydet";
    /// <summary>Düzenleme modunda "Vazgeç" görünür ve düzenlenen satır listede vurgulanır.</summary>
    public bool DuzenlemeModu => DuzenId != 0;

    /// <summary>Oturum değişince bekleyen kayıt, liste ve gelir yanıtları eskir (sonuçları, hataları ve bitişleri yansımaz; gösterge,
    /// hata ve ileti tabanda kalkar); önceki oturumun formu, listesi ve gelir formu UI bağlamında kalkar.</summary>
    protected override void OturumTemizle()
    {
        GiderBenzerlik.Temizle();
        FormuSifirla();
        GelenTemizle();
        ListeTemizle();
    }
```

`Kasa.App.Core/IslemlerViewModel.cs` (3/8) — Bul:

```csharp
        GiderKanallari.Add(new SecimCipi(KanalEtiketleri.Ortak));
        SenkronSecim();

        if (TipCipleri.Count == 0)
            foreach (var t in new[] { GiderTipi.Cari, GiderTipi.SabitGider, GiderTipi.KrediKarti })
                TipCipleri.Add(new SecimCipi(TipAdi(t)));

        _kartlar = kartlar;
        KartCipleriniKur();
        TipVurgu();
```

Yerine:

```csharp
        GiderKanallari.Add(new SecimCipi(KanalEtiketleri.Ortak));
        SenkronSecim();

        _kartlar = kartlar;
        KartCipleriniKur();
        TipVurgu();
```

`Kasa.App.Core/IslemlerViewModel.cs` (4/8) — Bul:

```csharp
    public bool KartSeciciGorunur => DuzenTip == GiderTipi.KrediKarti;
    /// <summary>Taksit yalnız yeni takipli kart giderinde girilir; düzenlemede plan değişmez (ödenmemişse gider silinip yeniden girilir).</summary>
    public bool TaksitGirilebilir => DuzenId == 0 && DuzenTip == GiderTipi.KrediKarti && DuzenKrediKartiId is not null;
    partial void OnDuzenIdChanged(int value) => OnPropertyChanged(nameof(TaksitGirilebilir));

    /// <summary>Formun gönderilecek gövdesi; taksit alanları yalnız taksit girilebilirken ve tek taksitten farklıysa doludur.</summary>
    private IslemYaz FormGovdesi()
```

Yerine:

```csharp
    public bool KartSeciciGorunur => DuzenTip == GiderTipi.KrediKarti;
    /// <summary>Taksit yalnız yeni takipli kart giderinde girilir; düzenlemede plan değişmez (ödenmemişse gider silinip yeniden girilir).</summary>
    public bool TaksitGirilebilir => DuzenId == 0 && DuzenTip == GiderTipi.KrediKarti && DuzenKrediKartiId is not null;
    partial void OnDuzenIdChanged(int value)
    {
        foreach (var ad in new[] { nameof(TaksitGirilebilir), nameof(FormBasligi), nameof(KaydetMetni), nameof(DuzenlemeModu) })
            OnPropertyChanged(ad);
    }

    /// <summary>Formun gönderilecek gövdesi; taksit alanları yalnız taksit girilebilirken ve tek taksitten farklıysa doludur.</summary>
    private IslemYaz FormGovdesi()
```

`Kasa.App.Core/IslemlerViewModel.cs` (5/8) — Bul:

```csharp
            k.Secili = k.Id == DuzenKrediKartiId;
    }

    [RelayCommand]
    private void Yeni()
    {
        GiderBenzerlik.Temizle();
        _giderAnahtari.Temizle();
```

Yerine:

```csharp
            k.Secili = k.Id == DuzenKrediKartiId;
    }

    /// <summary>"Yeni": yazılmış değişiklik varsa önce onay sorulur (tasarım §2).</summary>
    [RelayCommand]
    private async Task YeniAsync()
    {
        if (await BirakilabilirAsync(_form))
            FormuSifirla();
    }

    /// <summary>"Vazgeç" (düzenleme modu): bilerek bırakmaktır, onay sorulmaz; form boş yeni kayda döner.</summary>
    [RelayCommand]
    private void Vazgec() => FormuSifirla();

    /// <summary>Listedeki "Düzenle": yazılmış değişiklik varsa önce onay sorulur; sonra kayıt forma açılır.</summary>
    [RelayCommand]
    private async Task DuzenlemeyeGecAsync(IslemDto i)
    {
        if (await BirakilabilirAsync(_form))
            Duzenle(i);
    }

    /// <summary>Formu boş yeni kayda döndürür; formun hataları kalkar.</summary>
    private void FormuSifirla()
    {
        GiderBenzerlik.Temizle();
        _giderAnahtari.Temizle();
```

`Kasa.App.Core/IslemlerViewModel.cs` (6/8) — Bul:

```csharp
        _duzenlenen = null;
        _duzenSurum = 0;
        KartCipleriniKur();
    }

    [RelayCommand]
    public void Duzenle(IslemDto i)
    {
        if (i.EkstreKayitId is not null)
        { Hata = "Bu kayıt PDF ekstresinden aktarıldı. Ekstre İçe Aktar bölümünden iptal edip doğru bilgilerle yeniden kaydedin."; return; }
        if (i.AylikGiderOdemeId is not null)
        { Hata = "Bu ödeme Aylık Giderler bölümüne bağlı. Düzeltmek için o bölümde iptal edip yeniden ödeme kaydedin."; return; }
        if (i.AlisId is not null)
        { Hata = "Bu gider bir alışa bağlı. Dağılımı Alışlar ekranında iade / düzenle / onayla adımlarıyla değiştirin."; return; }
        GiderBenzerlik.Temizle();
        _giderAnahtari.Temizle();
        _duzenlenen = i;
```

Yerine:

```csharp
        _duzenlenen = null;
        _duzenSurum = 0;
        KartCipleriniKur();
        OnPropertyChanged(nameof(FormBasligi));
        Hatalar.Temizle();
        _form.Ac();
    }

    /// <summary>Kaydı forma açar (onay sormaz; listedeki düğme <see cref="DuzenlemeyeGecCommand"/> ile önce sorar). Başka bölüme bağlı
    /// kayıt açılmaz, nedeni formun genel hatasına yazılır.</summary>
    public void Duzenle(IslemDto i)
    {
        Hatalar.Temizle();
        if (i.EkstreKayitId is not null)
        { Hatalar.Genel = "Bu kayıt PDF ekstresinden aktarıldı. Ekstre İçe Aktar bölümünden iptal edip doğru bilgilerle yeniden kaydedin."; return; }
        if (i.AylikGiderOdemeId is not null)
        { Hatalar.Genel = "Bu ödeme Aylık Giderler bölümüne bağlı. Düzeltmek için o bölümde iptal edip yeniden ödeme kaydedin."; return; }
        if (i.AlisId is not null)
        { Hatalar.Genel = "Bu gider bir alışa bağlı. Dağılımı Alışlar ekranında iade / düzenle / onayla adımlarıyla değiştirin."; return; }
        GiderBenzerlik.Temizle();
        _giderAnahtari.Temizle();
        _duzenlenen = i;
```

`Kasa.App.Core/IslemlerViewModel.cs` (7/8) — Bul:

```csharp
        DuzenNot = i.Not;
        DuzenKrediKartiId = i.KrediKartiId;
        KartCipleriniKur();
    }

    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu)
            return;
        if (!ParaAyristirici.GecerliMi(DuzenTutar))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (DuzenTip == GiderTipi.KrediKarti && DuzenKrediKartiId is null && !KartsizEskiKayit)
        { Hata = TakipliKartIletisi; return; }
        if (TaksitGirilebilir && DuzenTaksitSayisi is < 1 or > 60)
        { Hata = "Taksit sayısı 1 ile 60 arasında olmalı."; return; }
        if (TaksitGirilebilir && DuzenIlkKesimVar && DuzenIlkKesimTarihi.Date < DuzenTarih.Date)
        { Hata = "İlk kesim tarihi harcamadan önce olamaz."; return; }
        var g = FormGovdesi();
        var id = DuzenId;
        if (id == 0 && !await GiderBenzerlik.DevamEdilebilirAsync(new(BenzerAramaTurleri.Gider, g.Tarih, g.TutarTl, g.KrediKartiId, g.Kanal), g,
```

Yerine:

```csharp
        DuzenNot = i.Not;
        DuzenKrediKartiId = i.KrediKartiId;
        KartCipleriniKur();
        OnPropertyChanged(nameof(FormBasligi));
        Hatalar.Temizle();
        _form.Ac();
    }

    /// <summary>Ön doğrulama (tasarım §1): en sık hatalar istek gönderilmeden alanın altında söylenir; sunucu kuralı yine denetler.</summary>
    private bool FormGecerli()
    {
        var h = Hatalar;
        h.Denetle(!string.IsNullOrWhiteSpace(DuzenCari), nameof(DuzenCari), "Açıklama boş olamaz.");
        h.Denetle(ParaAyristirici.GecerliMi(DuzenTutar), nameof(DuzenTutar), ParaAyristirici.GecersizMesaji);
        h.Denetle(DuzenTutar != 0, nameof(DuzenTutar), "Tutar sıfır olamaz.");
        h.Denetle(!string.IsNullOrWhiteSpace(DuzenKanal), nameof(DuzenKanal), "Kanal seçin.");
        h.Denetle(DuzenTip != GiderTipi.KrediKarti || DuzenKrediKartiId is not null || KartsizEskiKayit, nameof(DuzenKrediKartiId), TakipliKartIletisi);
        h.Denetle(!TaksitGirilebilir || DuzenTaksitSayisi is >= 1 and <= 60, nameof(DuzenTaksitSayisi), "Taksit sayısı 1 ile 60 arasında olmalı.");
        h.Denetle(!TaksitGirilebilir || !DuzenIlkKesimVar || DuzenIlkKesimTarihi.Date >= DuzenTarih.Date, nameof(DuzenIlkKesimTarihi),
            "İlk kesim tarihi harcamadan önce olamaz.");
        return !h.Var;
    }

    [RelayCommand]
    private Task KaydetAsync() => FormIsleAsync(Hatalar, async n =>
    {
        if (!EditorMu || !FormGecerli())
            return;
        var g = FormGovdesi();
        var id = DuzenId;
        if (id == 0 && !await GiderBenzerlik.DevamEdilebilirAsync(new(BenzerAramaTurleri.Gider, g.Tarih, g.TutarTl, g.KrediKartiId, g.Kanal), g,
```

`Kasa.App.Core/IslemlerViewModel.cs` (8/8) — Bul:

```csharp
        // Liste yenilenemese de kayıt alınmıştır: başarı ayrı söylenir (liste hatası durum şeridinde), form temizlenir.
        Mesaj = (id == 0 ? "Gider kaydedildi" : "Gider güncellendi")
            + (SuzgecteGorunur(g.Tarih, g.Kanal) ? "." : $"; seçili süzgeç ({SuzgecMetni()}) dışında kaldığı için listede görünmüyor.");
        Yeni();
        await ListeyiYenile(tam: true);
    });

    [RelayCommand] private async Task GideriAyriKaydetAsync() { if (GiderBenzerlik.Onayla()) await KaydetAsync(); }
```

Yerine:

```csharp
        // Liste yenilenemese de kayıt alınmıştır: başarı ayrı söylenir (liste hatası durum şeridinde), form temizlenir.
        Mesaj = (id == 0 ? "Gider kaydedildi" : "Gider güncellendi")
            + (SuzgecteGorunur(g.Tarih, g.Kanal) ? "." : $"; seçili süzgeç ({SuzgecMetni()}) dışında kaldığı için listede görünmüyor.");
        FormuSifirla();
        await ListeyiYenile(tam: true);
    }, SunucuAlanlari);

    [RelayCommand] private async Task GideriAyriKaydetAsync() { if (GiderBenzerlik.Onayla()) await KaydetAsync(); }
```

`Kasa.App/App.xaml` — Bul:

```xml
            <conv:AyAdiConverter x:Key="AyAdi" />
            <conv:DonemBicimConverter x:Key="DonemBicim" />
            <conv:BelgeAciklamasiConverter x:Key="BelgeAciklamasi" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

Yerine:

```xml
            <conv:AyAdiConverter x:Key="AyAdi" />
            <conv:DonemBicimConverter x:Key="DonemBicim" />
            <conv:BelgeAciklamasiConverter x:Key="BelgeAciklamasi" />
            <conv:EsitIseConverter x:Key="EsitIse" />
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`Kasa.App/Converters/EsitIseConverter.cs` (yeni dosya):

```csharp
using System.Globalization;

namespace Kasa.App.Converters;

/// <summary>İki bağlı değer eşit ve boş değilse true (düzenlenen satırın listede vurgusu: satırın Id'si ile formun DuzenId'si).</summary>
public sealed class EsitIseConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        => values is [{ } ilk, { } ikinci] && Equals(ilk, ikinci);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

`Kasa.App/Views/IslemlerPage.xaml` (1/4) — Bul:

```xml
        <Grid Grid.Row="1" ColumnDefinitions="Auto,*" ColumnSpacing="20">

            <!-- SOL: editör form sütunu -->
            <ScrollView IsVisible="{Binding EditorMu}" VerticalOptions="Fill">
                <VerticalStackLayout WidthRequest="360" Spacing="16">

                    <!-- Bölüm 1 · İşlem -->
                    <Border Style="{StaticResource CardForm}">
                        <VerticalStackLayout Spacing="12">
                            <Label Text="İŞLEM" Style="{StaticResource LblSection}" />
                            <VerticalStackLayout Spacing="0">
                                <Label Text="Tarih" Style="{StaticResource LblField}" />
                                <Border Style="{StaticResource FieldBorder}">
                                    <DatePicker Date="{Binding DuzenTarih}" />
                                </Border>
                            </VerticalStackLayout>
                            <VerticalStackLayout Spacing="0">
                                <Label Text="Açıklama / ödeme yapılan yer" Style="{StaticResource LblField}" />
                                <Border Style="{StaticResource FieldBorder}">
                                    <Entry Text="{Binding DuzenCari}" Placeholder="Örn. Yılmaz Gıda — mal alımı" />
                                </Border>
                            </VerticalStackLayout>
                            <VerticalStackLayout Spacing="0">
                                <Label Text="Tutar" Style="{StaticResource LblField}" />
                                <Border Style="{StaticResource FieldBorder}">
                                    <ctl:ParaGirisi Tutar="{Binding DuzenTutar}" Placeholder="0,00 ₺" />
                                </Border>
                            </VerticalStackLayout>
                            <VerticalStackLayout Spacing="0">
                                <Label Text="Kanal" Style="{StaticResource LblField}" />
                                <ctl:CipGrubu BindableLayout.ItemsSource="{Binding GiderKanallari}" SecCommand="{Binding SecGiderKanalCommand}" />
                            </VerticalStackLayout>
                            <VerticalStackLayout Spacing="0">
                                <Label Text="Tip" Style="{StaticResource LblField}" />
                                <ctl:CipGrubu BindableLayout.ItemsSource="{Binding TipCipleri}" SecCommand="{Binding SecTipCommand}" />
                            </VerticalStackLayout>
                            <VerticalStackLayout Spacing="0" IsVisible="{Binding KartSeciciGorunur}">
                                <Label Text="Kart (yeni takipteki kartlar)" Style="{StaticResource LblField}" />
                                <Label Text="{Binding KartUyarisi}" TextColor="{StaticResource UyariMetin}" IsVisible="{Binding KartUyarisi, Converter={StaticResource DoluIse}}" />
                                <ctl:CipGrubu BindableLayout.ItemsSource="{Binding KartCipleri}" SecCommand="{Binding SecKartCommand}" />
                            </VerticalStackLayout>
                            <VerticalStackLayout Spacing="6" IsVisible="{Binding TaksitGirilebilir}">
                                <Label Text="Taksit sayısı" Style="{StaticResource LblField}" />
                                <Border Style="{StaticResource FieldBorder}" WidthRequest="120" HorizontalOptions="Start">
                                    <Entry Text="{Binding DuzenTaksitSayisi}" Keyboard="Numeric" />
                                </Border>
                                <HorizontalStackLayout Spacing="10">
                                    <Switch IsToggled="{Binding DuzenIlkKesimVar}" />
                                    <Label Text="İlk kesim tarihini belirt (isteğe bağlı)" VerticalOptions="Center" />
                                </HorizontalStackLayout>
                                <Border Style="{StaticResource FieldBorder}" IsVisible="{Binding DuzenIlkKesimVar}"><DatePicker Date="{Binding DuzenIlkKesimTarihi}" /></Border>
                                <Label Text="Bankada taksitle çekildiyse her ekstreye yalnız taksit tutarı yazılır." Style="{StaticResource LblPageSub}" />
                            </VerticalStackLayout>
                            <VerticalStackLayout Spacing="0">
                                <Label Text="Not" Style="{StaticResource LblField}" />
                                <Border Style="{StaticResource FieldBorder}">
                                    <Entry Text="{Binding DuzenNot}" Placeholder="İsteğe bağlı" />
                                </Border>
                            </VerticalStackLayout>
                            <Grid ColumnDefinitions="*,Auto" ColumnSpacing="8" Margin="0,2,0,0">
                                <Button Text="Kaydet" Command="{Binding KaydetCommand}" />
                                <Button Grid.Column="1" Text="Yeni" Command="{Binding YeniCommand}"
                                        Style="{StaticResource BtnSecondary}" />
                            </Grid>
                            <Border Style="{StaticResource CardForm}" IsVisible="{Binding GiderBenzerlik.UyariVar}">
```

Yerine:

```xml
        <Grid Grid.Row="1" ColumnDefinitions="Auto,*" ColumnSpacing="20">

            <!-- SOL: editör form sütunu -->
            <ScrollView x:Name="FormKaydirici" IsVisible="{Binding EditorMu}" VerticalOptions="Fill">
                <VerticalStackLayout WidthRequest="360" Spacing="16">

                    <!-- Bölüm 1 · İşlem. Başlık ve düğmeler düzenleme modunu söyler; genel hata formun en üstünde, alan hataları
                         alanın altında (tasarım 2026-10-02 §1-2). -->
                    <Border Style="{StaticResource CardForm}" x:Name="IslemFormu">
                        <VerticalStackLayout Spacing="12">
                            <Label Text="{Binding FormBasligi}" Style="{StaticResource LblRowPrimary}" FontAttributes="Bold"
                                   SemanticProperties.HeadingLevel="Level2" />
                            <Border x:Name="FormHataKutusu" Style="{StaticResource ErrorBox}"
                                    IsVisible="{Binding Hatalar.Genel, Converter={StaticResource DoluIse}}">
                                <Label Text="{Binding Hatalar.Genel}" Style="{StaticResource LblError}" />
                            </Border>
                            <ctl:FormAlani Baslik="Tarih" Alan="DuzenTarih" Hata="{Binding Hatalar[DuzenTarih]}">
                                <DatePicker Date="{Binding DuzenTarih}" />
                            </ctl:FormAlani>
                            <ctl:FormAlani Baslik="Açıklama / ödeme yapılan yer" Alan="DuzenCari" Hata="{Binding Hatalar[DuzenCari]}">
                                <Entry Text="{Binding DuzenCari}" Placeholder="Örn. Yılmaz Gıda — mal alımı" />
                            </ctl:FormAlani>
                            <ctl:FormAlani Baslik="Tutar" Alan="DuzenTutar" Hata="{Binding Hatalar[DuzenTutar]}">
                                <ctl:ParaGirisi Tutar="{Binding DuzenTutar}" Placeholder="0,00 ₺" />
                            </ctl:FormAlani>
                            <ctl:FormAlani Baslik="Kanal" Alan="DuzenKanal" Hata="{Binding Hatalar[DuzenKanal]}" Cerceveli="False">
                                <ctl:CipGrubu BindableLayout.ItemsSource="{Binding GiderKanallari}" SecCommand="{Binding SecGiderKanalCommand}" />
                            </ctl:FormAlani>
                            <ctl:FormAlani Baslik="Tip" Alan="DuzenTip" Hata="{Binding Hatalar[DuzenTip]}" Cerceveli="False">
                                <ctl:CipGrubu BindableLayout.ItemsSource="{Binding TipCipleri}" SecCommand="{Binding SecTipCommand}" />
                            </ctl:FormAlani>
                            <VerticalStackLayout Spacing="0" IsVisible="{Binding KartSeciciGorunur}">
                                <Label Text="{Binding KartUyarisi}" TextColor="{StaticResource UyariMetin}" IsVisible="{Binding KartUyarisi, Converter={StaticResource DoluIse}}" />
                                <ctl:FormAlani Baslik="Kart (yeni takipteki kartlar)" Alan="DuzenKrediKartiId" Hata="{Binding Hatalar[DuzenKrediKartiId]}" Cerceveli="False">
                                    <ctl:CipGrubu BindableLayout.ItemsSource="{Binding KartCipleri}" SecCommand="{Binding SecKartCommand}" />
                                </ctl:FormAlani>
                            </VerticalStackLayout>
                            <VerticalStackLayout Spacing="6" IsVisible="{Binding TaksitGirilebilir}">
                                <ctl:FormAlani Baslik="Taksit sayısı" Alan="DuzenTaksitSayisi" Hata="{Binding Hatalar[DuzenTaksitSayisi]}"
                                               WidthRequest="120" HorizontalOptions="Start">
                                    <Entry Text="{Binding DuzenTaksitSayisi}" Keyboard="Numeric" />
                                </ctl:FormAlani>
                                <HorizontalStackLayout Spacing="10">
                                    <Switch IsToggled="{Binding DuzenIlkKesimVar}" />
                                    <Label Text="İlk kesim tarihini belirt (isteğe bağlı)" VerticalOptions="Center" />
                                </HorizontalStackLayout>
                                <ctl:FormAlani Alan="DuzenIlkKesimTarihi" Hata="{Binding Hatalar[DuzenIlkKesimTarihi]}" IsVisible="{Binding DuzenIlkKesimVar}">
                                    <DatePicker Date="{Binding DuzenIlkKesimTarihi}" />
                                </ctl:FormAlani>
                                <Label Text="Bankada taksitle çekildiyse her ekstreye yalnız taksit tutarı yazılır." Style="{StaticResource LblPageSub}" />
                            </VerticalStackLayout>
                            <ctl:FormAlani Baslik="Not" Alan="DuzenNot" Hata="{Binding Hatalar[DuzenNot]}">
                                <Entry Text="{Binding DuzenNot}" Placeholder="İsteğe bağlı" />
                            </ctl:FormAlani>
                            <Grid ColumnDefinitions="*,Auto,Auto" ColumnSpacing="8" Margin="0,2,0,0">
                                <Button Text="{Binding KaydetMetni}" Command="{Binding KaydetCommand}" />
                                <Button Grid.Column="1" Text="Vazgeç" Command="{Binding VazgecCommand}" IsVisible="{Binding DuzenlemeModu}"
                                        Style="{StaticResource BtnSecondary}" />
                                <Button Grid.Column="2" Text="Yeni" Command="{Binding YeniCommand}"
                                        Style="{StaticResource BtnSecondary}" />
                            </Grid>
                            <Border Style="{StaticResource CardForm}" IsVisible="{Binding GiderBenzerlik.UyariVar}">
```

`Kasa.App/Views/IslemlerPage.xaml` (2/4) — Bul:

```xml
                                    <Button Text="Ayrı bir işlem, devam et" Command="{Binding GideriAyriKaydetCommand}" IsEnabled="{Binding Mesgul, Converter={StaticResource TersIse}}" />
                                </VerticalStackLayout>
                            </Border>
                            <Border Style="{StaticResource ErrorBox}"
                                    IsVisible="{Binding Hata, Converter={StaticResource DoluIse}}">
                                <Label Text="{Binding Hata}" Style="{StaticResource LblError}" />
```

Yerine:

```xml
                                    <Button Text="Ayrı bir işlem, devam et" Command="{Binding GideriAyriKaydetCommand}" IsEnabled="{Binding Mesgul, Converter={StaticResource TersIse}}" />
                                </VerticalStackLayout>
                            </Border>
                            <!-- Formdan bağımsız işlem hataları (silme, gelir formu) -->
                            <Border Style="{StaticResource ErrorBox}"
                                    IsVisible="{Binding Hata, Converter={StaticResource DoluIse}}">
                                <Label Text="{Binding Hata}" Style="{StaticResource LblError}" />
```

`Kasa.App/Views/IslemlerPage.xaml` (3/4) — Bul:

```xml
                                    <BoxView Style="{StaticResource RowSeparator}" />
                                    <Grid Padding="18,6" MinimumHeightRequest="58"
                                          ColumnDefinitions="Auto,*,Auto,Auto" ColumnSpacing="14">
                                        <Border Style="{StaticResource DateChip}" VerticalOptions="Center">
                                            <Label Text="{Binding Tarih, StringFormat='{0:dd MMM}'}"
                                                   Style="{StaticResource LblDateChip}" />
```

Yerine:

```xml
                                    <BoxView Style="{StaticResource RowSeparator}" />
                                    <Grid Padding="18,6" MinimumHeightRequest="58"
                                          ColumnDefinitions="Auto,*,Auto,Auto" ColumnSpacing="14">
                                        <!-- Düzenlenen satır vurgulanır (İŞ-03). -->
                                        <Grid.Triggers>
                                            <DataTrigger TargetType="Grid" Value="True">
                                                <DataTrigger.Binding>
                                                    <MultiBinding Converter="{StaticResource EsitIse}">
                                                        <Binding Path="Id" />
                                                        <Binding Path="BindingContext.DuzenId" Source="{x:Reference Sayfa}" />
                                                    </MultiBinding>
                                                </DataTrigger.Binding>
                                                <Setter Property="BackgroundColor" Value="{StaticResource GreenSoft}" />
                                            </DataTrigger>
                                        </Grid.Triggers>
                                        <Border Style="{StaticResource DateChip}" VerticalOptions="Center">
                                            <Label Text="{Binding Tarih, StringFormat='{0:dd MMM}'}"
                                                   Style="{StaticResource LblDateChip}" />
```

`Kasa.App/Views/IslemlerPage.xaml` (4/4) — Bul:

```xml
                                        <HorizontalStackLayout Grid.Column="3" Spacing="6" VerticalOptions="Center"
                                                               IsVisible="{Binding BindingContext.EditorMu, Source={x:Reference Sayfa}}">
                                            <Button Text="Düzenle" Style="{StaticResource BtnRowEdit}"
                                                    Command="{Binding BindingContext.DuzenleCommand, Source={x:Reference Sayfa}}"
                                                    CommandParameter="{Binding .}" />
                                            <Button Text="Sil" Style="{StaticResource BtnRowDelete}"
                                                    Clicked="SilTiklandi"
```

Yerine:

```xml
                                        <HorizontalStackLayout Grid.Column="3" Spacing="6" VerticalOptions="Center"
                                                               IsVisible="{Binding BindingContext.EditorMu, Source={x:Reference Sayfa}}">
                                            <Button Text="Düzenle" Style="{StaticResource BtnRowEdit}"
                                                    Command="{Binding BindingContext.DuzenlemeyeGecCommand, Source={x:Reference Sayfa}}"
                                                    CommandParameter="{Binding .}" />
                                            <Button Text="Sil" Style="{StaticResource BtnRowDelete}"
                                                    Clicked="SilTiklandi"
```

`Kasa.App/Views/IslemlerPage.xaml.cs` — Bul:

```csharp
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
```

Yerine:

```csharp
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        var gorunur = new Controls.GorunurYapici(FormKaydirici);
        vm.Hatalar.GosterIstendi += (_, _) => gorunur.HatayaGit(IslemFormu, vm.Hatalar, FormHataKutusu);
        vm.BirakmaOnayi = ileti => DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon);
    }

    protected override async void OnAppearing()
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1124`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/AlislarViewModelTests.cs Kasa.App.Core.Tests/BenzerKayitTests.cs Kasa.App.Core.Tests/CekirdekSurumVmTests.cs Kasa.App.Core.Tests/Donusturuculer/DonusturucuTests.cs Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.Baglamalar.cs Kasa.App.Core.Tests/GecersizTutarTests.cs Kasa.App.Core.Tests/IslemEditorTests.cs Kasa.App.Core.Tests/IslemFormuTests.cs Kasa.App.Core.Tests/RaporKuraliVmTests.cs Kasa.App.Core.Tests/YurutucuTests.cs Kasa.App.Core/IslemlerViewModel.cs Kasa.App/App.xaml Kasa.App/Converters/EsitIseConverter.cs Kasa.App/Views/IslemlerPage.xaml Kasa.App/Views/IslemlerPage.xaml.cs
git commit -F - <<'MESAJ'
feat(app): İşlemler formunda alan hataları ve düzenleme modu

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 13: Alışlar — alan hataları ve ortak kaydedilmemiş değişiklik

Tedarikçi hatası alanın altında, kalem kuralları genel hatada; bayrak yerine açılış değerleriyle karşılaştırma; "Kaydı aç" ve "Yeni alış" onay ister; başlık ve satır vurgusu (AL-01).

**Dosyalar:**
- Değiştir: `Kasa.App.Core/AlislarViewModel.OdemelerVeBelgeler.cs`
- Değiştir: `Kasa.App.Core/AlislarViewModel.cs`
- Değiştir: `Kasa.App/Views/AlislarPage.xaml`
- Değiştir: `Kasa.App/Views/AlislarPage.xaml.cs`
- Test (oluştur): `Kasa.App.Core.Tests/AlisFormuTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/AlislarViewModelTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/GecersizTutarTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/AlisFormuTests.cs` (yeni dosya):

```csharp
using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Alışlar formu (tasarım 2026-10-02 §1-2; AL-01): tedarikçi hatası alanın altında, kalem kuralları formun genel hatasında;
/// sunucu alanları eşlenir; başka kayda geçiş ve Yeni onay ister; kaydedilmemiş değişiklik form açıldığı andaki değerlere göredir.</summary>
public class AlisFormuTests
{
    private static AlisDto Alis(int id, string tedarikci) => new(
        id, 2, 3, "Ayşe", new(2026, 9, 21), tedarikci, null, "Taslak", null, 100m, 0m, 100m,
        [new AlisKalemDto(1, "Mal alımı", 100m, [new AlisDagilimDto(1, "MEZAT", 60m), new AlisDagilimDto(2, "PERAKENDE", 40m)])],
        Array.Empty<AlisOdemeDto>());

    private static async Task<(AlislarViewModel Vm, AlislarViewModelTests.SahteAlisApi Api)> Kur()
    {
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = [Alis(7, "Ege Gıda"), Alis(8, "Akdeniz")] };
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac());
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Bos_taslak_tedarikciyi_alanda_kalem_kuralini_formun_ustunde_soyler()
    {
        var (vm, api) = await Kur();

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonYaz);
        Assert.Equal("Tedarikçi adını yazın.", vm.Hatalar[nameof(vm.Tedarikci)]);
        Assert.Equal("Her kaleme açıklama ve sıfırdan büyük, kuruş hassasiyetinde tutar girin.", vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
        Assert.Equal(("Yeni alış", "Kaydet"), (vm.FormBasligi, vm.KaydetMetni));
    }

    [Fact]
    public async Task Sunucu_alan_hatasi_tedarikciye_kalem_hatasi_genel_hataya_gider()
    {
        var alanlar = new Dictionary<string, string> { ["tedarikci"] = "En fazla 200 karakter girilebilir.", ["kalemler[0].aciklama"] = "En fazla 500 karakter girilebilir." };
        var (vm, api) = await Kur();
        api.OlusturmaHatasi = new KasaApiException(HttpStatusCode.BadRequest, "birleşik", alanHatalari: alanlar);
        vm.Tedarikci = "Firma";
        vm.Kalemler[0].Aciklama = "Mal";
        vm.Kalemler[0].Tutar = 10m;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal("En fazla 200 karakter girilebilir.", vm.Hatalar[nameof(vm.Tedarikci)]);
        Assert.Equal("En fazla 500 karakter girilebilir.", vm.Hatalar.Genel);
    }

    [Fact]
    public async Task Baska_kayda_gecis_onay_ister_birakinca_hata_ve_degisiklik_kalkar()
    {
        var (vm, _) = await Kur();
        await vm.SecCommand.ExecuteAsync(vm.Alislar.Single(a => a.Veri.Id == 7));
        Assert.Equal(("Düzenleniyor: 21.09.2026 · Ege Gıda", "Değişiklikleri kaydet"), (vm.FormBasligi, vm.KaydetMetni));
        vm.Tedarikci = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.Hatalar.Var);
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var sorulan = 0;
        var cevap = false;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(cevap); };
        await vm.SecCommand.ExecuteAsync(vm.Alislar.Single(a => a.Veri.Id == 8));
        Assert.Equal(7, vm.Secili!.Id);

        cevap = true;
        await vm.SecCommand.ExecuteAsync(vm.Alislar.Single(a => a.Veri.Id == 8));
        Assert.Equal(8, vm.Secili!.Id);
        Assert.False(vm.Hatalar.Var);
        Assert.False(vm.KaydedilmemisDegisiklikVar);

        vm.Tedarikci = "x";
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Null(vm.Secili);
        Assert.Equal(3, sorulan);
    }

    [Fact]
    public async Task Deger_geri_alininca_kaydedilmemis_degisiklik_kalkar()
    {
        var (vm, _) = await Kur();
        await vm.SecCommand.ExecuteAsync(vm.Alislar.Single(a => a.Veri.Id == 7));

        vm.Tedarikci = "Başka";
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        vm.Tedarikci = "Ege Gıda";
        Assert.False(vm.KaydedilmemisDegisiklikVar);

        vm.Kalemler[0].Tutar = 90m;
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        vm.DegisiklikleriBirak();
        Assert.Equal(100m, vm.Kalemler[0].Tutar);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }
}
```

`Kasa.App.Core.Tests/AlislarViewModelTests.cs` (1/2) — Bul:

```csharp
        vm.SecCommand.Execute(vm.Alislar[0]);
        vm.Kalemler[0].Dagilimlar.RemoveAt(1);
        await vm.OnaylaCommand.ExecuteAsync(null);
        Assert.Contains("tamamını", vm.Hata);
        Assert.Null(api.SonDurum);
        vm.DegisiklikleriBirakCommand.Execute(null);
        await vm.IadeCommand.ExecuteAsync(null);
```

Yerine:

```csharp
        vm.SecCommand.Execute(vm.Alislar[0]);
        vm.Kalemler[0].Dagilimlar.RemoveAt(1);
        await vm.OnaylaCommand.ExecuteAsync(null);
        Assert.Contains("tamamını", vm.Hatalar.Genel);
        Assert.Null(api.SonDurum);
        vm.DegisiklikleriBirakCommand.Execute(null);
        await vm.IadeCommand.ExecuteAsync(null);
```

`Kasa.App.Core.Tests/AlislarViewModelTests.cs` (2/2) — Bul:

```csharp
        { vm.Tedarikci = "Firma"; vm.Kalemler[0].Aciklama = "Mal"; vm.Kalemler[0].Tutar = 100m; }
        Doldur();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Hata);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.Olusturmalar.Count);
        Assert.NotNull(api.Olusturmalar[0].IstekId);
```

Yerine:

```csharp
        { vm.Tedarikci = "Firma"; vm.Kalemler[0].Aciklama = "Mal"; vm.Kalemler[0].Tutar = 100m; }
        Doldur();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Hatalar.Genel);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.Olusturmalar.Count);
        Assert.NotNull(api.Olusturmalar[0].IstekId);
```

`Kasa.App.Core.Tests/GecersizTutarTests.cs` (1/2) — Bul:

```csharp
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.Kalemler[0].DagilimOzeti);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonYaz);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        vm.Kalemler[0].Tutar = 100m;
        vm.Kalemler[0].Dagilimlar.Add(new(vm.Kanallar.ToList()) { Kanal = vm.Kanallar[0], Tutar = G });
```

Yerine:

```csharp
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.Kalemler[0].DagilimOzeti);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonYaz);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hatalar.Genel);

        vm.Kalemler[0].Tutar = 100m;
        vm.Kalemler[0].Dagilimlar.Add(new(vm.Kanallar.ToList()) { Kanal = vm.Kanallar[0], Tutar = G });
```

`Kasa.App.Core.Tests/GecersizTutarTests.cs` (2/2) — Bul:

```csharp
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.DagilimOzeti);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonYaz);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
    }

    [Fact]
```

Yerine:

```csharp
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.DagilimOzeti);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonYaz);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hatalar.Genel);
    }

    [Fact]
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~AlisFormuTests|FullyQualifiedName~AlislarViewModelTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 8 farklı ileti; örnekler:

- `AlisFormuTests.cs: CS1061: 'AlislarViewModel' bir 'Hatalar' tanımı içermiyor ve 'AlislarViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'Hatalar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `AlisFormuTests.cs: CS1061: 'AlislarViewModel' bir 'FormBasligi' tanımı içermiyor ve 'AlislarViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'FormBasligi' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `AlisFormuTests.cs: CS1061: 'AlislarViewModel' bir 'KaydetMetni' tanımı içermiyor ve 'AlislarViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KaydetMetni' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `AlislarViewModelTests.cs: CS1061: 'AlislarViewModel' bir 'Hatalar' tanımı içermiyor ve 'AlislarViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'Hatalar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/AlislarViewModel.OdemelerVeBelgeler.cs` — Bul:

```csharp
    partial void OnDuzeltilecekOdemeChanged(AlisOdemeSatiri? value) => OnPropertyChanged(nameof(DuzeltmeAcik));
    partial void OnDuzeltmeTakipliChanged(bool value) { OnPropertyChanged(nameof(DuzeltmeAlanlariAcik)); OnPropertyChanged(nameof(AyirmaPaylariGorunur)); }
    partial void OnHarcamayiKoruChanged(bool value) => OnPropertyChanged(nameof(AyirmaPaylariGorunur));
    public void IdIleSec(int id) { var satir = Alislar.FirstOrDefault(a => a.Veri.Id == id); if (satir is not null) Sec(satir); }
    [RelayCommand]
    private void OdemeDuzelt(AlisOdemeSatiri odeme)
    {
```

Yerine:

```csharp
    partial void OnDuzeltilecekOdemeChanged(AlisOdemeSatiri? value) => OnPropertyChanged(nameof(DuzeltmeAcik));
    partial void OnDuzeltmeTakipliChanged(bool value) { OnPropertyChanged(nameof(DuzeltmeAlanlariAcik)); OnPropertyChanged(nameof(AyirmaPaylariGorunur)); }
    partial void OnHarcamayiKoruChanged(bool value) => OnPropertyChanged(nameof(AyirmaPaylariGorunur));
    /// <summary>Bildirimden gelen alış: yazılmış değişiklik varsa açılmaz, uyarı yazılır (onay penceresi sayfa açılırken gösterilmez).</summary>
    public void IdIleSec(int id)
    {
        if (Mesgul || Alislar.FirstOrDefault(a => a.Veri.Id == id) is not { } satir)
            return;
        if (KaydedilmemisDegisiklikVar)
        { KaydetmeUyarisi(); return; }
        SeciliyiGoster(satir.Veri);
    }
    [RelayCommand]
    private void OdemeDuzelt(AlisOdemeSatiri odeme)
    {
```

`Kasa.App.Core/AlislarViewModel.cs` (1/15) — Bul:

```csharp

namespace Kasa.App.Core;

public partial class AlislarViewModel : OturumluViewModel
{
    private readonly IAlisApi _api;
    private readonly IKasaApi _finans;
```

Yerine:

```csharp

namespace Kasa.App.Core;

public partial class AlislarViewModel : OturumluViewModel, IKaydedilmemisForm
{
    private readonly IAlisApi _api;
    private readonly IKasaApi _finans;
```

`Kasa.App.Core/AlislarViewModel.cs` (2/15) — Bul:

```csharp
        _odemelerApi = odemelerApi;
        _yonetim = yonetim;
        OdemeBenzerlik = new(benzerlikApi ?? finans as IBenzerKayitApi);
        Kalemler.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
```

Yerine:

```csharp
        _odemelerApi = odemelerApi;
        _yonetim = yonetim;
        OdemeBenzerlik = new(benzerlikApi ?? finans as IBenzerKayitApi);
        _form = new(() => new
        {
            Tarih,
            Tedarikci,
            AlisNotu,
            Kalemler = Kalemler.Select(k => new { k.Aciklama, k.Tutar, Paylar = k.Dagilimlar.Select(d => new { Kanal = d.Kanal?.Id, d.Tutar }).ToList() }).ToList(),
        });
        Kalemler.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
```

`Kasa.App.Core/AlislarViewModel.cs` (3/15) — Bul:

```csharp
        OturumTemizle();
    }

    public ObservableCollection<AlisSatiri> Alislar { get; } = new();
    public BenzerKayitKontrolu OdemeBenzerlik { get; }
    public ObservableCollection<AlisKanalDto> Kanallar { get; } = new();
```

Yerine:

```csharp
        OturumTemizle();
    }

    /// <summary>Alış formunun hataları (tasarım 2026-10-02 §1; AL-01): tarih, tedarikçi ve not alanın altında; kalem kuralları ve
    /// eşlenemeyen sunucu iletileri formun genel hatasında.</summary>
    public AlanHatalari Hatalar { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [Hatalar];

    /// <summary>Sunucunun alış doğrulama alanları (AlisEndpoints.Validate; küçük harf) → formun alanları. Kalem alanları
    /// ("kalemler[0].aciklama") eşlenmez: genel hataya gider.</summary>
    private static readonly Dictionary<string, string> SunucuAlanlari = new()
    {
        ["tarih"] = nameof(Tarih),
        ["tedarikci"] = nameof(Tedarikci),
        ["not"] = nameof(AlisNotu),
    };

    /// <summary>Formun açıldığı andaki değerleri (tarih, tedarikçi, not, kalemler ve payları): kaydedilmemiş değişiklik ölçütü.</summary>
    private readonly KaydedilmemisDegisiklik _form;

    public ObservableCollection<AlisSatiri> Alislar { get; } = new();
    public BenzerKayitKontrolu OdemeBenzerlik { get; }
    public ObservableCollection<AlisKanalDto> Kanallar { get; } = new();
```

`Kasa.App.Core/AlislarViewModel.cs` (4/15) — Bul:

```csharp

    public AlisDto? Secili => _secili;
    public string Baslik => _secili is null ? "Yeni alış" : $"Alış #{_secili.Id}";
    public string Durum => AlisSatiri.DurumAdi(_secili?.Durum ?? AlisDurumlari.Taslak);
    public string KaydiAcan => _secili?.Alici ?? (EditorMu ? "Editör" : "Sizin alışınız");
    public string? EditorNotu => _secili?.EditorNotu;
```

Yerine:

```csharp

    public AlisDto? Secili => _secili;
    public string Baslik => _secili is null ? "Yeni alış" : $"Alış #{_secili.Id}";
    /// <summary>Formun başlığı (tasarım §2): yeni kayıtta "Yeni alış", düzenlenebilir kayıtta "Düzenleniyor: 02.08.2026 · Ege Gıda".</summary>
    public string FormBasligi => _secili is null ? "Yeni alış" : $"Düzenleniyor: {_secili.Tarih:dd.MM.yyyy} · {_secili.Tedarikci}";
    public string KaydetMetni => _secili is null ? "Kaydet" : "Değişiklikleri kaydet";
    public string Durum => AlisSatiri.DurumAdi(_secili?.Durum ?? AlisDurumlari.Taslak);
    public string KaydiAcan => _secili?.Alici ?? (EditorMu ? "Editör" : "Sizin alışınız");
    public string? EditorNotu => _secili?.EditorNotu;
```

`Kasa.App.Core/AlislarViewModel.cs` (5/15) — Bul:

```csharp
    });

    [RelayCommand] private Task YenileAsync() => YukleAsync();
    [RelayCommand]
    private void Sec(AlisSatiri satir)
    {
        if (Mesgul)
            return;
        if (KaydedilmemisDegisiklikVar)
        { KaydetmeUyarisi(); return; }
        SeciliyiGoster(satir.Veri);
    }

    [RelayCommand]
    private void Yeni()
    {
        if (Mesgul && !_yansitiliyor && VeriHazir)
            return;
        if (KaydedilmemisDegisiklikVar && !_yansitiliyor)
        { KaydetmeUyarisi(); return; }
        _yansitiliyor = true;
        _secili = null;
        _olusturAnahtari.Temizle();
```

Yerine:

```csharp
    });

    [RelayCommand] private Task YenileAsync() => YukleAsync();

    /// <summary>"Kaydı aç": yazılmış değişiklik varsa önce onay sorulur (tasarım §2; "Bırak" seçilirse değişiklikler bırakılır).</summary>
    [RelayCommand]
    private async Task SecAsync(AlisSatiri satir)
    {
        if (Mesgul || !await BirakilabilirAsync(_form) || Mesgul)
            return;
        SeciliyiGoster(satir.Veri);
    }

    /// <summary>"+ Yeni alış": yazılmış değişiklik varsa önce onay sorulur.</summary>
    [RelayCommand]
    private async Task YeniAsync()
    {
        if (Mesgul || !await BirakilabilirAsync(_form) || Mesgul)
            return;
        Yeni();
    }

    /// <summary>Boş yeni alış formu (onay sormaz; yükleme, oturum ve değişiklikleri bırakma çağırır).</summary>
    private void Yeni()
    {
        if (Mesgul && !_yansitiliyor && VeriHazir)
            return;
        _yansitiliyor = true;
        _secili = null;
        _olusturAnahtari.Temizle();
```

`Kasa.App.Core/AlislarViewModel.cs` (6/15) — Bul:

```csharp
        Odemeler.Clear();
        OdemeFormunuTemizle();
        _yansitiliyor = false;
        KaydedilmemisDegisiklikVar = false;
        DurumuYenile();
    }

    [RelayCommand]
    private void DegisiklikleriBirak()
    {
        if (Mesgul)
            return;
```

Yerine:

```csharp
        Odemeler.Clear();
        OdemeFormunuTemizle();
        _yansitiliyor = false;
        FormuAc();
        DurumuYenile();
    }

    /// <summary>Form şimdiki değerleriyle açıldı: kaydedilmemiş değişiklik ve formun hataları kalkar.</summary>
    private void FormuAc()
    {
        _form.Ac();
        KaydedilmemisDegisiklikVar = false;
        Hatalar.Temizle();
    }

    [RelayCommand]
    public void DegisiklikleriBirak()
    {
        if (Mesgul)
            return;
```

`Kasa.App.Core/AlislarViewModel.cs` (7/15) — Bul:

```csharp
        Degistir(Odemeler, alis.Odemeler.OrderByDescending(o => o.Tarih).Select(o => new AlisOdemeSatiri(o)));
        OdemeFormunuTemizle();
        _yansitiliyor = false;
        KaydedilmemisDegisiklikVar = false;
        DurumuYenile();
    }
```

Yerine:

```csharp
        Degistir(Odemeler, alis.Odemeler.OrderByDescending(o => o.Tarih).Select(o => new AlisOdemeSatiri(o)));
        OdemeFormunuTemizle();
        _yansitiliyor = false;
        FormuAc();
        DurumuYenile();
    }
```

`Kasa.App.Core/AlislarViewModel.cs` (8/15) — Bul:

```csharp
    }

    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async nesil =>
    {
        if (!Duzenlenebilir || !KaydiDogrula())
            return;
        if (!await KaydetCoreAsync(nesil))
            return;
        Mesaj = "Alış kaydedildi. Dağılım tamamlanınca incelemeye gönderebilirsiniz.";
    });

    private async Task<bool> KaydetCoreAsync(int nesil)
    {
```

Yerine:

```csharp
    }

    [RelayCommand]
    private Task KaydetAsync() => FormIsleAsync(Hatalar, async nesil =>
    {
        if (!Duzenlenebilir || !KaydiDogrula())
            return;
        if (!await KaydetCoreAsync(nesil))
            return;
        Mesaj = "Alış kaydedildi. Dağılım tamamlanınca incelemeye gönderebilirsiniz.";
    }, SunucuAlanlari);

    private async Task<bool> KaydetCoreAsync(int nesil)
    {
```

`Kasa.App.Core/AlislarViewModel.cs` (9/15) — Bul:

```csharp
    }

    [RelayCommand]
    private Task GonderAsync() => YurutAsync(async nesil =>
    {
        if (!Gonderilebilir || !KaydiDogrula())
            return;
```

Yerine:

```csharp
    }

    [RelayCommand]
    private Task GonderAsync() => FormIsleAsync(Hatalar, async nesil =>
    {
        if (!Gonderilebilir || !KaydiDogrula())
            return;
```

`Kasa.App.Core/AlislarViewModel.cs` (10/15) — Bul:

```csharp
        if (!SonucuUygula(await _api.AlisGonderAsync(_secili!.Id, new(_secili.Surum)), nesil))
            return;
        Mesaj = "Alış incelemeye gönderildi. Editörün kararını buradan takip edebilirsiniz.";
    });

    [RelayCommand]
    private Task OnaylaAsync() => YurutAsync(async nesil =>
    {
        if (!Onaylanabilir || !KaydiDogrula(tamDagilim: true))
            return;
```

Yerine:

```csharp
        if (!SonucuUygula(await _api.AlisGonderAsync(_secili!.Id, new(_secili.Surum)), nesil))
            return;
        Mesaj = "Alış incelemeye gönderildi. Editörün kararını buradan takip edebilirsiniz.";
    }, SunucuAlanlari);

    [RelayCommand]
    private Task OnaylaAsync() => FormIsleAsync(Hatalar, async nesil =>
    {
        if (!Onaylanabilir || !KaydiDogrula(tamDagilim: true))
            return;
```

`Kasa.App.Core/AlislarViewModel.cs` (11/15) — Bul:

```csharp
        if (!SonucuUygula(await _api.AlisOnaylaAsync(_secili!.Id, new(_secili.Surum)), nesil))
            return;
        Mesaj = "Alış onaylandı. Ödemelerin kanal dağılımı artık raporlara yansır.";
    });

    [RelayCommand]
    private Task IadeAsync() => YurutAsync(async nesil =>
```

Yerine:

```csharp
        if (!SonucuUygula(await _api.AlisOnaylaAsync(_secili!.Id, new(_secili.Surum)), nesil))
            return;
        Mesaj = "Alış onaylandı. Ödemelerin kanal dağılımı artık raporlara yansır.";
    }, SunucuAlanlari);

    [RelayCommand]
    private Task IadeAsync() => YurutAsync(async nesil =>
```

`Kasa.App.Core/AlislarViewModel.cs` (12/15) — Bul:

```csharp
        Mesaj = "Alış taslağa iade edildi. Ödemeler korundu; yeniden onaya kadar dağılım bekliyor.";
    });

    private bool KaydiDogrula(bool tamDagilim = false)
    {
        if (!TutarlarGecerli)
            return HataYaz(ParaAyristirici.GecersizMesaji);
        if (string.IsNullOrWhiteSpace(Tedarikci))
            return HataYaz("Tedarikçi adını yazın.");
        if (Kalemler.Count == 0)
            return HataYaz("En az bir alış kalemi ekleyin.");
        foreach (var k in Kalemler)
        {
            if (string.IsNullOrWhiteSpace(k.Aciklama) || k.Tutar <= 0 || decimal.Round(k.Tutar, 2) != k.Tutar)
                return HataYaz("Her kaleme açıklama ve sıfırdan büyük, kuruş hassasiyetinde tutar girin.");
            if (k.Dagilimlar.Any(d => d.Kanal is null || d.Tutar <= 0 || decimal.Round(d.Tutar, 2) != d.Tutar))
                return HataYaz("Her dağılım satırında kanal seçin ve sıfırdan büyük tutar girin; kullanılmayan satırı kaldırın.");
            if (k.Dagilimlar.Select(d => d.Kanal!.Id).Distinct().Count() != k.Dagilimlar.Count)
                return HataYaz("Bir kalemde aynı kanalı iki kez seçmeyin.");
            if (k.Dagilan > k.Tutar || (tamDagilim && k.Dagilan != k.Tutar))
                return HataYaz(tamDagilim ? "Onay için her kalemin tamamını kanallara dağıtın." : "Kanal payları kalem tutarını aşamaz.");
        }
        if (Toplam < Odenen)
            return HataYaz("Alış toplamı mevcut ödemelerden küçük olamaz.");
        return true;
    }

    [RelayCommand]
```

Yerine:

```csharp
        Mesaj = "Alış taslağa iade edildi. Ödemeler korundu; yeniden onaya kadar dağılım bekliyor.";
    });

    /// <summary>Ön doğrulama (tasarım §1): tedarikçi alanın altında; kalem ve dağılım kuralları formun genel hatasında.</summary>
    private bool KaydiDogrula(bool tamDagilim = false)
    {
        Hatalar.Denetle(!string.IsNullOrWhiteSpace(Tedarikci), nameof(Tedarikci), "Tedarikçi adını yazın.");
        Hatalar.Genel = KalemHatasi(tamDagilim);
        return !Hatalar.Var;
    }

    private string? KalemHatasi(bool tamDagilim)
    {
        if (!TutarlarGecerli)
            return ParaAyristirici.GecersizMesaji;
        if (Kalemler.Count == 0)
            return "En az bir alış kalemi ekleyin.";
        foreach (var k in Kalemler)
        {
            if (string.IsNullOrWhiteSpace(k.Aciklama) || k.Tutar <= 0 || decimal.Round(k.Tutar, 2) != k.Tutar)
                return "Her kaleme açıklama ve sıfırdan büyük, kuruş hassasiyetinde tutar girin.";
            if (k.Dagilimlar.Any(d => d.Kanal is null || d.Tutar <= 0 || decimal.Round(d.Tutar, 2) != d.Tutar))
                return "Her dağılım satırında kanal seçin ve sıfırdan büyük tutar girin; kullanılmayan satırı kaldırın.";
            if (k.Dagilimlar.Select(d => d.Kanal!.Id).Distinct().Count() != k.Dagilimlar.Count)
                return "Bir kalemde aynı kanalı iki kez seçmeyin.";
            if (k.Dagilan > k.Tutar || (tamDagilim && k.Dagilan != k.Tutar))
                return tamDagilim ? "Onay için her kalemin tamamını kanallara dağıtın." : "Kanal payları kalem tutarını aşamaz.";
        }
        return Toplam < Odenen ? "Alış toplamı mevcut ödemelerden küçük olamaz." : null;
    }

    [RelayCommand]
```

`Kasa.App.Core/AlislarViewModel.cs` (13/15) — Bul:

```csharp
    partial void OnTedarikciChanged(string value) => KirliYap();
    partial void OnAlisNotuChanged(string? value) => KirliYap();
    partial void OnKaydedilmemisDegisiklikVarChanged(bool value) => OnizlemeyiYenile();
    private void KirliYap() { if (!_yansitiliyor) KaydedilmemisDegisiklikVar = true; }

    private void OnizlemeyiYenile()
    {
```

Yerine:

```csharp
    partial void OnTedarikciChanged(string value) => KirliYap();
    partial void OnAlisNotuChanged(string? value) => KirliYap();
    partial void OnKaydedilmemisDegisiklikVarChanged(bool value) => OnizlemeyiYenile();
    /// <summary>Kaydedilmemiş değişiklik ölçütü form açıldığı andaki değerlerdir (tasarım §2): değer geri alınınca uyarı da kalkar.</summary>
    private void KirliYap() { if (!_yansitiliyor) KaydedilmemisDegisiklikVar = _form.Var; }

    private void OnizlemeyiYenile()
    {
```

`Kasa.App.Core/AlislarViewModel.cs` (14/15) — Bul:

```csharp
        Mesaj = "Alıcı hesabı kaydedildi. Pasifleştirme veya şifre değişimi eski oturumu kapatır.";
    });

    private bool HataYaz(string mesaj) { Hata = mesaj; return false; }
    private void KalemDegisti(object? sender, PropertyChangedEventArgs e) { ToplamlariYenile(); KirliYap(); }
    private void ToplamlariYenile()
    {
```

Yerine:

```csharp
        Mesaj = "Alıcı hesabı kaydedildi. Pasifleştirme veya şifre değişimi eski oturumu kapatır.";
    });

    private void KalemDegisti(object? sender, PropertyChangedEventArgs e) { ToplamlariYenile(); KirliYap(); }
    private void ToplamlariYenile()
    {
```

`Kasa.App.Core/AlislarViewModel.cs` (15/15) — Bul:

```csharp
    }
    private void DurumuYenile()
    {
        foreach (var ad in new[] { nameof(Secili), nameof(Baslik), nameof(Durum), nameof(KaydiAcan), nameof(EditorNotu), nameof(KayitVar), nameof(Duzenlenebilir), nameof(Gonderilebilir), nameof(Onaylanabilir), nameof(IadeEdilebilir), nameof(OdemeAlaniGorunur), nameof(Odenen) })
            OnPropertyChanged(ad);
        ToplamlariYenile();
        OnizlemeyiYenile();
```

Yerine:

```csharp
    }
    private void DurumuYenile()
    {
        foreach (var ad in new[] { nameof(Secili), nameof(Baslik), nameof(FormBasligi), nameof(KaydetMetni), nameof(Durum), nameof(KaydiAcan), nameof(EditorNotu), nameof(KayitVar), nameof(Duzenlenebilir), nameof(Gonderilebilir), nameof(Onaylanabilir), nameof(IadeEdilebilir), nameof(OdemeAlaniGorunur), nameof(Odenen) })
            OnPropertyChanged(ad);
        ToplamlariYenile();
        OnizlemeyiYenile();
```

`Kasa.App/Views/AlislarPage.xaml` (1/3) — Bul:

```xml
                    <CollectionView Grid.Row="1" ItemsSource="{Binding Alislar}" SelectionMode="None">
                        <CollectionView.ItemTemplate><DataTemplate x:DataType="core:AlisSatiri">
                            <VerticalStackLayout Spacing="6" Padding="0,12">
                                <Label Text="{Binding Baslik}" Style="{StaticResource LblRowPrimary}" />
                                <Label Text="{Binding Alt}" Style="{StaticResource LblRowSecondary}" />
                                <Label Text="{Binding Tutar}" FontSize="12" FontAttributes="Bold" />
```

Yerine:

```xml
                    <CollectionView Grid.Row="1" ItemsSource="{Binding Alislar}" SelectionMode="None">
                        <CollectionView.ItemTemplate><DataTemplate x:DataType="core:AlisSatiri">
                            <VerticalStackLayout Spacing="6" Padding="0,12">
                                <!-- Açık (düzenlenen) kayıt listede vurgulanır (tasarım 2026-10-02 §2). -->
                                <VerticalStackLayout.Triggers>
                                    <DataTrigger TargetType="VerticalStackLayout" Value="True">
                                        <DataTrigger.Binding>
                                            <MultiBinding Converter="{StaticResource EsitIse}">
                                                <Binding Path="Veri.Id" />
                                                <Binding Path="BindingContext.Secili.Id" Source="{x:Reference Sayfa}" />
                                            </MultiBinding>
                                        </DataTrigger.Binding>
                                        <Setter Property="BackgroundColor" Value="{StaticResource GreenSoft}" />
                                    </DataTrigger>
                                </VerticalStackLayout.Triggers>
                                <Label Text="{Binding Baslik}" Style="{StaticResource LblRowPrimary}" />
                                <Label Text="{Binding Alt}" Style="{StaticResource LblRowSecondary}" />
                                <Label Text="{Binding Tutar}" FontSize="12" FontAttributes="Bold" />
```

`Kasa.App/Views/AlislarPage.xaml` (2/3) — Bul:

```xml
                        </VerticalStackLayout>
                    </Border>

                    <Border Style="{StaticResource CardForm}">
                        <VerticalStackLayout Spacing="14">
                            <Label Text="1 · ALIŞ BİLGİLERİ VE KALEMLER" Style="{StaticResource LblSection}" />
                            <VerticalStackLayout Spacing="12" IsEnabled="{Binding Duzenlenebilir}">
                                <Grid ColumnDefinitions="160,*" ColumnSpacing="12">
                                    <VerticalStackLayout>
                                        <Label Text="Tarih" Style="{StaticResource LblField}" />
                                        <Border Style="{StaticResource FieldBorder}">
                                            <DatePicker Date="{Binding Tarih}" />
                                        </Border>
                                    </VerticalStackLayout>
                                    <VerticalStackLayout Grid.Column="1">
                                        <Label Text="Tedarikçi / açıklama" Style="{StaticResource LblField}" />
                                        <Border Style="{StaticResource FieldBorder}">
                                            <Entry Text="{Binding Tedarikci}" Placeholder="Firma veya kişi adı" />
                                        </Border>
                                    </VerticalStackLayout>
                                </Grid>
                                <Border Style="{StaticResource FieldBorder}"><Entry Text="{Binding AlisNotu}" Placeholder="Alış notu (isteğe bağlı)" /></Border>
                                <VerticalStackLayout Spacing="12" BindableLayout.ItemsSource="{Binding Kalemler}">
                                    <BindableLayout.ItemTemplate><DataTemplate x:DataType="core:AlisKalemEditor">
                                        <Border BackgroundColor="{StaticResource KalemZemin}" Stroke="{StaticResource BrushBorder}" StrokeShape="RoundRectangle 10" Padding="14">
```

Yerine:

```xml
                        </VerticalStackLayout>
                    </Border>

                    <Border Style="{StaticResource CardForm}" x:Name="AlisFormu">
                        <VerticalStackLayout Spacing="14">
                            <Label Text="1 · ALIŞ BİLGİLERİ VE KALEMLER" Style="{StaticResource LblSection}" />
                            <!-- Düzenleme modu ve formun genel hatası formun en üstünde (tasarım 2026-10-02 §1-2; AL-01). -->
                            <Label Text="{Binding FormBasligi}" Style="{StaticResource LblRowPrimary}" FontAttributes="Bold"
                                   IsVisible="{Binding Duzenlenebilir}" SemanticProperties.HeadingLevel="Level2" />
                            <Border x:Name="FormHataKutusu" Style="{StaticResource ErrorBox}"
                                    IsVisible="{Binding Hatalar.Genel, Converter={StaticResource DoluIse}}">
                                <Label Text="{Binding Hatalar.Genel}" Style="{StaticResource LblError}" />
                            </Border>
                            <VerticalStackLayout Spacing="12" IsEnabled="{Binding Duzenlenebilir}">
                                <Grid ColumnDefinitions="160,*" ColumnSpacing="12">
                                    <ctl:FormAlani Baslik="Tarih" Alan="Tarih" Hata="{Binding Hatalar[Tarih]}">
                                        <DatePicker Date="{Binding Tarih}" />
                                    </ctl:FormAlani>
                                    <ctl:FormAlani Grid.Column="1" Baslik="Tedarikçi / açıklama" Alan="Tedarikci" Hata="{Binding Hatalar[Tedarikci]}">
                                        <Entry Text="{Binding Tedarikci}" Placeholder="Firma veya kişi adı" />
                                    </ctl:FormAlani>
                                </Grid>
                                <ctl:FormAlani Alan="AlisNotu" Hata="{Binding Hatalar[AlisNotu]}">
                                    <Entry Text="{Binding AlisNotu}" Placeholder="Alış notu (isteğe bağlı)" />
                                </ctl:FormAlani>
                                <VerticalStackLayout Spacing="12" BindableLayout.ItemsSource="{Binding Kalemler}">
                                    <BindableLayout.ItemTemplate><DataTemplate x:DataType="core:AlisKalemEditor">
                                        <Border BackgroundColor="{StaticResource KalemZemin}" Stroke="{StaticResource BrushBorder}" StrokeShape="RoundRectangle 10" Padding="14">
```

`Kasa.App/Views/AlislarPage.xaml` (3/3) — Bul:

```xml
                            <Label Text="{Binding DagilimOzeti}" FontAttributes="Bold" />
                            <Label Text="Kaydedilmemiş değişiklikler var." TextColor="{StaticResource UyariMetin}" IsVisible="{Binding KaydedilmemisDegisiklikVar}" />
                            <FlexLayout Wrap="Wrap" AlignItems="Center">
                                <Button Text="Değişiklikleri kaydet" Command="{Binding KaydetCommand}" IsVisible="{Binding Duzenlenebilir}" Margin="0,0,10,6" />
                                <Button Text="İncelemeye gönder" Command="{Binding GonderCommand}" IsVisible="{Binding Gonderilebilir}" Style="{StaticResource BtnSecondary}" Margin="0,0,10,6" />
                                <Button Text="Kaydet ve onayla" Command="{Binding OnaylaCommand}" IsVisible="{Binding Onaylanabilir}" Margin="0,0,10,6" />
                                <Button Text="Değişiklikleri bırak" Clicked="DegisiklikleriBirakTiklandi"
```

Yerine:

```xml
                            <Label Text="{Binding DagilimOzeti}" FontAttributes="Bold" />
                            <Label Text="Kaydedilmemiş değişiklikler var." TextColor="{StaticResource UyariMetin}" IsVisible="{Binding KaydedilmemisDegisiklikVar}" />
                            <FlexLayout Wrap="Wrap" AlignItems="Center">
                                <Button Text="{Binding KaydetMetni}" Command="{Binding KaydetCommand}" IsVisible="{Binding Duzenlenebilir}" Margin="0,0,10,6" />
                                <Button Text="İncelemeye gönder" Command="{Binding GonderCommand}" IsVisible="{Binding Gonderilebilir}" Style="{StaticResource BtnSecondary}" Margin="0,0,10,6" />
                                <Button Text="Kaydet ve onayla" Command="{Binding OnaylaCommand}" IsVisible="{Binding Onaylanabilir}" Margin="0,0,10,6" />
                                <Button Text="Değişiklikleri bırak" Clicked="DegisiklikleriBirakTiklandi"
```

`Kasa.App/Views/AlislarPage.xaml.cs` — Bul:

```csharp
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }
    protected override async void OnAppearing()
    {
```

Yerine:

```csharp
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        var gorunur = new Controls.GorunurYapici(DetayKaydirici);
        vm.Hatalar.GosterIstendi += (_, _) => gorunur.HatayaGit(AlisFormu, vm.Hatalar, FormHataKutusu);
        vm.BirakmaOnayi = ileti => DisplayAlertAsync(KaydedilmemisDegisiklik.Baslik, ileti, KaydedilmemisDegisiklik.Birak, KaydedilmemisDegisiklik.FormaDon);
    }
    protected override async void OnAppearing()
    {
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1128`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/AlisFormuTests.cs Kasa.App.Core.Tests/AlislarViewModelTests.cs Kasa.App.Core.Tests/GecersizTutarTests.cs Kasa.App.Core/AlislarViewModel.OdemelerVeBelgeler.cs Kasa.App.Core/AlislarViewModel.cs Kasa.App/Views/AlislarPage.xaml Kasa.App/Views/AlislarPage.xaml.cs
git commit -F - <<'MESAJ'
feat(app): Alışlar formunda alan hataları ve ortak kaydedilmemiş değişiklik

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 14: Çekler — çek formu ve hareket formu hataları

Ön doğrulama (no, banka, kişi, tutar, vade, kasa), sunucu iletisi genel hatada, hataya kaydırma (ÇK-01); düzeltme başlığı; "Yeni çek" ve "Çeki düzelt" onay ister.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/CekTakipViewModel.cs`
- Değiştir: `Kasa.App/Views/CekTakipPage.cs`
- Test (oluştur): `Kasa.App.Core.Tests/CekFormuTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/CekTakipViewModelTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/CekFormuTests.cs` (yeni dosya):

```csharp
using System.Net;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

/// <summary>Çekler formları (tasarım 2026-10-02 §1-2; ÇK-01): çek formu ve hareket formu hataları formun içinde ve alanın altında,
/// sunucunun alan adsız iletisi genel hataya; düzenleme başlığı; Vazgeç ve Yeni çek kaydedilmemiş değişikliği sorar.</summary>
public class CekFormuTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);

    private static async Task<(CekTakipViewModel Vm, CekTakipViewModelTests.Sahte Api)> Kur(params CekDto[] cekler)
    {
        var api = new CekTakipViewModelTests.Sahte { Liste = [.. cekler] };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new CekTakipViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor }, new IslemEditorTests.SabitZaman(Bugun));
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Bos_cek_formu_istek_gondermez_alanlari_formun_icinde_soyler()
    {
        var (vm, api) = await Kur();
        await vm.YeniCekCommand.ExecuteAsync(null);
        var gosterim = 0;
        vm.Hatalar.GosterIstendi += (_, _) => gosterim++;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Empty(api.Kayitlar);
        Assert.Equal("Çek / senet numarası boş olamaz.", vm.Hatalar[nameof(vm.No)]);
        Assert.Equal("Banka boş olamaz.", vm.Hatalar[nameof(vm.Banka)]);
        Assert.Equal("Kişi boş olamaz.", vm.Hatalar[nameof(vm.Kisi)]);
        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.Hatalar[nameof(vm.Tutar)]);
        Assert.Null(vm.Hata);
        Assert.True(vm.FormAcik);
        Assert.Equal(1, gosterim);

        vm.FormYon = vm.YonSecenekleri[1];
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("Ödeneceği kasayı (kanal ya da Ortak) seçin.", vm.Hatalar[nameof(vm.CekKasasi)]);
    }

    [Fact]
    public async Task Sunucunun_alan_adsiz_reddi_formun_genel_hatasina_yazilir()
    {
        var (vm, api) = await Kur();
        await vm.YeniCekCommand.ExecuteAsync(null);
        vm.No = "1";
        vm.Banka = "Ziraat";
        vm.Kisi = "Ayşe";
        vm.Tutar = 100m;
        api.KayitHatasi = new KasaApiException(HttpStatusCode.BadRequest, "Vade tarihi 01.01.2000 tarihinden önce olamaz.");

        await vm.YineDeKaydetCommand.ExecuteAsync(null);

        Assert.Equal("Vade tarihi 01.01.2000 tarihinden önce olamaz.", vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
        Assert.True(vm.FormAcik);
    }

    [Fact]
    public async Task Duzeltme_basligi_kaydet_metni_yeni_cekte_onay_vazgecte_onaysiz_kapanis()
    {
        var (vm, _) = await Kur(CekTakipViewModelTests.Cek(1));
        vm.SecCommand.Execute(vm.Cekler[0]);
        await vm.DuzeltCommand.ExecuteAsync(null);
        Assert.Equal(("Düzenleniyor: 30.09.2026 · Ahmet Yılmaz", "Değişikliği kaydet"), (vm.FormBasligi, vm.KaydetMetni));
        Assert.False(vm.KaydedilmemisDegisiklikVar);

        vm.Kisi = "Başka";
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        var cevap = false;
        vm.BirakmaOnayi = _ => Task.FromResult(cevap);
        await vm.YeniCekCommand.ExecuteAsync(null);
        Assert.Equal(("Başka", 1), (vm.Kisi, vm.Duzenlenen));   // "Forma dön"

        cevap = true;                                            // "Bırak"
        await vm.YeniCekCommand.ExecuteAsync(null);
        Assert.Equal(("Yeni çek / senet", "Kaydet", ""), (vm.FormBasligi, vm.KaydetMetni, vm.Kisi));

        vm.Kisi = "Yazıldı";
        vm.FormuKapatCommand.Execute(null);                     // "Vazgeç" sormaz
        Assert.False(vm.FormAcik);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }

    [Fact]
    public async Task Hareket_formunda_kasa_secilmeden_istek_gitmez_hata_alanin_altinda()
    {
        var (vm, api) = await Kur(CekTakipViewModelTests.Cek(1));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single(c => c.Kod == CekHareketTurleri.Tahsilat));
        vm.HareketKasasi = null;

        await vm.HareketKaydetCommand.ExecuteAsync(null);

        Assert.Empty(api.Hareketler);
        Assert.Equal("Kasa (kanal) seçin.", vm.HareketHatalari[nameof(vm.HareketKasasi)]);
        vm.HareketKasasi = "MEZAT";
        Assert.False(vm.HareketHatalari.Var);
    }
}
```

`Kasa.App.Core.Tests/CekTakipViewModelTests.cs` (1/5) — Bul:

```csharp
        var ilk = Assert.Single(api.Kayitlar).Govde;
        Assert.NotEqual(Guid.Empty, ilk.IstekId);
        Assert.True(vm.FormAcik);
        Assert.NotNull(vm.Hata);
        await vm.YineDeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.Kayitlar.Count);
        var (id, g) = api.Kayitlar[1];
```

Yerine:

```csharp
        var ilk = Assert.Single(api.Kayitlar).Govde;
        Assert.NotEqual(Guid.Empty, ilk.IstekId);
        Assert.True(vm.FormAcik);
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.Hatalar.Genel);
        await vm.YineDeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.Kayitlar.Count);
        var (id, g) = api.Kayitlar[1];
```

`Kasa.App.Core.Tests/CekTakipViewModelTests.cs` (2/5) — Bul:

```csharp
    }

    [Fact]
    public async Task Numara_bossa_ayni_cek_denetimi_yapilmaz()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        var sorguSayisi = api.Sorgular.Count;
        vm.YeniCekCommand.Execute(null);
        vm.No = "   ";
        vm.Kisi = "Deneme";
        vm.Tutar = 1_000m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(sorguSayisi, api.Sorgular.Count);
        Assert.Single(api.Kayitlar);
        Assert.Null(vm.AyniCekUyarisi);
    }

    [Fact]
```

Yerine:

```csharp
    }

    [Fact]
    public async Task Numara_bossa_ayni_cek_denetimi_ve_kayit_yapilmaz_alan_hatasi_yazilir()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        var sorguSayisi = api.Sorgular.Count;
        vm.YeniCekCommand.Execute(null);
        vm.No = "   ";
        vm.Banka = "Ziraat";
        vm.Kisi = "Deneme";
        vm.Tutar = 1_000m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(sorguSayisi, api.Sorgular.Count);
        Assert.Empty(api.Kayitlar);
        Assert.Null(vm.AyniCekUyarisi);
        Assert.Equal("Çek / senet numarası boş olamaz.", vm.Hatalar[nameof(vm.No)]);
    }

    [Fact]
```

`Kasa.App.Core.Tests/CekTakipViewModelTests.cs` (3/5) — Bul:

```csharp

        vm.YeniCekCommand.Execute(null);
        vm.No = "99999"; // arama metnine ("12345") uymaz
        vm.Kisi = "Deneme";
        vm.Tutar = 1_000m;
        await vm.KaydetCommand.ExecuteAsync(null);
```

Yerine:

```csharp

        vm.YeniCekCommand.Execute(null);
        vm.No = "99999"; // arama metnine ("12345") uymaz
        vm.Banka = "Ziraat";
        vm.Kisi = "Deneme";
        vm.Tutar = 1_000m;
        await vm.KaydetCommand.ExecuteAsync(null);
```

`Kasa.App.Core.Tests/CekTakipViewModelTests.cs` (4/5) — Bul:

```csharp
        var (vm, _) = await Vm(Rol.Editor, Cek(1, vade: Bugun.AddDays(5)), Cek(2, vade: Bugun.AddDays(10)));
        vm.YeniCekCommand.Execute(null);
        vm.No = "999";
        vm.Kisi = "Deneme";
        vm.Tutar = 1_000m;
        vm.Vade = Bugun.AddDays(20).ToDateTime(TimeOnly.MinValue);
```

Yerine:

```csharp
        var (vm, _) = await Vm(Rol.Editor, Cek(1, vade: Bugun.AddDays(5)), Cek(2, vade: Bugun.AddDays(10)));
        vm.YeniCekCommand.Execute(null);
        vm.No = "999";
        vm.Banka = "Ziraat";
        vm.Kisi = "Deneme";
        vm.Tutar = 1_000m;
        vm.Vade = Bugun.AddDays(20).ToDateTime(TimeOnly.MinValue);
```

`Kasa.App.Core.Tests/CekTakipViewModelTests.cs` (5/5) — Bul:

```csharp
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        vm.YeniCekCommand.Execute(null);
        vm.FormYon = vm.YonSecenekleri[1];
        vm.No = "999";
        vm.Kisi = "Mehmet";
        vm.Tutar = 5_000m;
```

Yerine:

```csharp
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        vm.YeniCekCommand.Execute(null);
        vm.FormYon = vm.YonSecenekleri[1];
        vm.FormTur = vm.TurSecenekleri[1];   // senette banka boş olabilir (sunucu çekte bankayı ister)
        vm.No = "999";
        vm.Kisi = "Mehmet";
        vm.Tutar = 5_000m;
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekFormuTests|FullyQualifiedName~CekTakipViewModelTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 6 farklı ileti; örnekler:

- `CekFormuTests.cs: CS1061: 'IRelayCommand' bir 'ExecuteAsync' tanımı içermiyor ve 'IRelayCommand' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'ExecuteAsync' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `CekFormuTests.cs: CS1061: 'CekTakipViewModel' bir 'Hatalar' tanımı içermiyor ve 'CekTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'Hatalar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `CekFormuTests.cs: CS1061: 'CekTakipViewModel' bir 'KaydetMetni' tanımı içermiyor ve 'CekTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KaydetMetni' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `CekFormuTests.cs: CS1061: 'CekTakipViewModel' bir 'KaydedilmemisDegisiklikVar' tanımı içermiyor ve 'CekTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KaydedilmemisDegisiklikVar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/CekTakipViewModel.cs` (1/14) — Bul:

```csharp
/// banka ve numaralı kayıt kaydetmeden önce uyarılır; kullanıcı onaylarsa kaydedilir.
/// </summary>
/// <param name="zaman">Vade rozeti ve hazır süzgeçlerin günü (yerel); verilmezse sistem saati.</param>
public partial class CekTakipViewModel(ICekApi api, IKasaApi finans, AuthViewModel auth, TimeProvider? zaman = null) : OturumluViewModel(auth)
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
    private readonly TekrarAnahtari _kayit = new();
```

Yerine:

```csharp
/// banka ve numaralı kayıt kaydetmeden önce uyarılır; kullanıcı onaylarsa kaydedilir.
/// </summary>
/// <param name="zaman">Vade rozeti ve hazır süzgeçlerin günü (yerel); verilmezse sistem saati.</param>
public partial class CekTakipViewModel(ICekApi api, IKasaApi finans, AuthViewModel auth, TimeProvider? zaman = null) : OturumluViewModel(auth), IKaydedilmemisForm
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
    private readonly TekrarAnahtari _kayit = new();
```

`Kasa.App.Core/CekTakipViewModel.cs` (2/14) — Bul:

```csharp
    // Düzeltme formu açılırken çekin sürümü: kaydetme bunu gönderir (listeden okunmaz); liste yenilenince sürüm değiştiyse form kapanır.
    private int _duzenlenenSurum;

    /// <summary>Liste ve özet okumasının son istek kazanır hattı (yükleme göstergesi ve hatası ekranın Mesgul ve Hata'sıdır).</summary>
    private SonIstekHatti ListeHatti => _listeHatti ??= new(Yurutucu);
```

Yerine:

```csharp
    // Düzeltme formu açılırken çekin sürümü: kaydetme bunu gönderir (listeden okunmaz); liste yenilenince sürüm değiştiyse form kapanır.
    private int _duzenlenenSurum;

    /// <summary>Çek / senet formunun (yeni ya da düzeltme) hataları (tasarım 2026-10-02 §1; ÇK-01): alan → ileti ve formun genel hatası.
    /// Sunucu çek hatalarını alan adı olmadan ({ hata }) döndürür: sunucu iletisi genel hataya gider.</summary>
    public AlanHatalari Hatalar { get; } = new();
    /// <summary>Açık çekin hareket formunun hataları.</summary>
    public AlanHatalari HareketHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [Hatalar, HareketHatalari];

    /// <summary>Çek formunun açıldığı andaki değerleri: kaydedilmemiş değişiklik ölçütü (form kapalıyken değişiklik sayılmaz).</summary>
    private KaydedilmemisDegisiklik? _formIzi;
    private KaydedilmemisDegisiklik FormIzi => _formIzi ??= new(() => new
    {
        Yon = FormYon?.Kod,
        Tur = FormTur?.Kod,
        No,
        Banka,
        Kisi,
        Tutar,
        Vade,
        CekKasasi,
        Teminat,
        Konum = Konum?.Kod,
        Not,
    });
    public bool KaydedilmemisDegisiklikVar => FormIzi.Var;

    /// <summary>Kabuktan çıkışta "Bırak": çek formu kapanır.</summary>
    public void DegisiklikleriBirak() => FormuKapatOnaysiz();

    /// <summary>Kaydet düğmesi: yeni çekte "Kaydet", düzeltmede "Değişikliği kaydet".</summary>
    public string KaydetMetni => Duzenlenen is null ? "Kaydet" : "Değişikliği kaydet";

    /// <summary>Liste ve özet okumasının son istek kazanır hattı (yükleme göstergesi ve hatası ekranın Mesgul ve Hata'sıdır).</summary>
    private SonIstekHatti ListeHatti => _listeHatti ??= new(Yurutucu);
```

`Kasa.App.Core/CekTakipViewModel.cs` (3/14) — Bul:

```csharp
    [ObservableProperty] private string _karsi = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi))] private bool _formAcik;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi))] private int? _duzenlenen;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormVerilen))] private KodCipi? _formYon;
    [ObservableProperty] private KodCipi? _formTur;
    [ObservableProperty] private string _no = "";
```

Yerine:

```csharp
    [ObservableProperty] private string _karsi = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi))] private bool _formAcik;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi), nameof(KaydetMetni))] private int? _duzenlenen;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormVerilen))] private KodCipi? _formYon;
    [ObservableProperty] private KodCipi? _formTur;
    [ObservableProperty] private string _no = "";
```

`Kasa.App.Core/CekTakipViewModel.cs` (4/14) — Bul:

```csharp
    public bool FormVerilen => FormYon?.Kod == CekYonleri.Verilen;
    public bool AyniCekVar => AyniCekUyarisi is not null;
    public bool VadeSuzgeciVar => VadeBas is not null || VadeSon is not null;
    public string FormBasligi => Duzenlenen is null ? "Yeni çek / senet" : "Çeki düzelt";
    public string VadeSuzgeci => (VadeBas, VadeSon) switch
    {
        ({ } bas, { } son) => $"Vade {bas:dd.MM.yyyy} – {son:dd.MM.yyyy}",
```

Yerine:

```csharp
    public bool FormVerilen => FormYon?.Kod == CekYonleri.Verilen;
    public bool AyniCekVar => AyniCekUyarisi is not null;
    public bool VadeSuzgeciVar => VadeBas is not null || VadeSon is not null;
    /// <summary>Formun başlığı (tasarım §2): "Yeni çek / senet" ya da "Düzenleniyor: 15.10.2026 · Ahmet Yılmaz" (vade · kişi, açıldığı andaki).</summary>
    public string FormBasligi => Duzenlenen is null ? "Yeni çek / senet" : $"Düzenleniyor: {_duzenlenenOzet}";
    private string _duzenlenenOzet = "";
    public string VadeSuzgeci => (VadeBas, VadeSon) switch
    {
        ({ } bas, { } son) => $"Vade {bas:dd.MM.yyyy} – {son:dd.MM.yyyy}",
```

`Kasa.App.Core/CekTakipViewModel.cs` (5/14) — Bul:

```csharp

    partial void OnAcikChanged(CekDto? value)
    {
        TakipMetni.Doldur(Hareketler, value?.Hareketler.Select(h => new CekHareketSatiri(h)) ?? []);
        TakipMetni.Doldur(HareketCipleri, value?.IzinliHareketler.Select(t => new KodCipi(t, CekMetni.Hareket(t))) ?? []);
        HareketTuru = null;
```

Yerine:

```csharp

    partial void OnAcikChanged(CekDto? value)
    {
        HareketHatalari.Temizle();
        TakipMetni.Doldur(Hareketler, value?.Hareketler.Select(h => new CekHareketSatiri(h)) ?? []);
        TakipMetni.Doldur(HareketCipleri, value?.IzinliHareketler.Select(t => new KodCipi(t, CekMetni.Hareket(t))) ?? []);
        HareketTuru = null;
```

`Kasa.App.Core/CekTakipViewModel.cs` (6/14) — Bul:

```csharp
        // Düzeltilen çek arada başka bir işlemle değiştiyse form eski veriyle yeni sürümü ezmesin: kapanır, yeniden açılması istenir.
        if (FormAcik && Duzenlenen is { } d && cekler.FirstOrDefault(c => c.Id == d) is { } guncel && guncel.Surum != _duzenlenenSurum)
        {
            FormAcik = false;
            Mesaj = "Çek başka bir işlemle değişti; formu yeniden açın.";
        }
        Tamamlandi();
```

Yerine:

```csharp
        // Düzeltilen çek arada başka bir işlemle değiştiyse form eski veriyle yeni sürümü ezmesin: kapanır, yeniden açılması istenir.
        if (FormAcik && Duzenlenen is { } d && cekler.FirstOrDefault(c => c.Id == d) is { } guncel && guncel.Surum != _duzenlenenSurum)
        {
            FormuKapatOnaysiz();
            Mesaj = "Çek başka bir işlemle değişti; formu yeniden açın.";
        }
        Tamamlandi();
```

`Kasa.App.Core/CekTakipViewModel.cs` (7/14) — Bul:

```csharp
    {
        if (Acik is not { } c)
            return;
        HareketTuru = cip.Kod;
        foreach (var h in HareketCipleri)
            h.Secili = h.Kod == cip.Kod;
```

Yerine:

```csharp
    {
        if (Acik is not { } c)
            return;
        HareketHatalari.Temizle();
        HareketTuru = cip.Kod;
        foreach (var h in HareketCipleri)
            h.Secili = h.Kod == cip.Kod;
```

`Kasa.App.Core/CekTakipViewModel.cs` (8/14) — Bul:

```csharp
    }

    [RelayCommand]
    private Task HareketKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Acik is not { } c || HareketTuru is not { } tur)
            return;
        if (!ParaAyristirici.HepsiGecerli(HareketTutari, NetTutar))
        {
            Hata = ParaAyristirici.GecersizMesaji;
            return;
        }
        var g = new CekHareketYaz(Guid.Empty, c.Surum, tur, DateOnly.FromDateTime(HareketTarihi), HareketTutari, NetGerekli ? NetTutar : null,
            KasaGerekli ? HareketKasasi : null, KarsiGerekli ? Karsi.Trim() : null);
        g = g with { IstekId = _hareket.Al(c.Id, new { c.Id, g }) };
```

Yerine:

```csharp
    }

    [RelayCommand]
    private Task HareketKaydetAsync() => FormIsleAsync(HareketHatalari, async n =>
    {
        if (!EditorMu || Acik is not { } c || HareketTuru is not { } tur)
            return;
        var h = HareketHatalari;
        h.Denetle(ParaAyristirici.GecerliMi(HareketTutari), nameof(HareketTutari), ParaAyristirici.GecersizMesaji);
        h.Denetle(!NetGerekli || ParaAyristirici.GecerliMi(NetTutar), nameof(NetTutar), ParaAyristirici.GecersizMesaji);
        h.Denetle(!KasaGerekli || !string.IsNullOrWhiteSpace(HareketKasasi), nameof(HareketKasasi), "Kasa (kanal) seçin.");
        if (h.Var)
            return;
        var g = new CekHareketYaz(Guid.Empty, c.Surum, tur, DateOnly.FromDateTime(HareketTarihi), HareketTutari, NetGerekli ? NetTutar : null,
            KasaGerekli ? HareketKasasi : null, KarsiGerekli ? Karsi.Trim() : null);
        g = g with { IstekId = _hareket.Al(c.Id, new { c.Id, g }) };
```

`Kasa.App.Core/CekTakipViewModel.cs` (9/14) — Bul:

```csharp
        }
    }

    [RelayCommand]
    private void YeniCek()
    {
        Duzenlenen = null;
        FormYon = YonSecenekleri.First(y => y.Kod == Yon);
```

Yerine:

```csharp
        }
    }

    /// <summary>"Yeni çek / senet": yazılmış çek formu varsa önce onay sorulur (tasarım §2).</summary>
    [RelayCommand]
    private async Task YeniCekAsync()
    {
        if (await BirakilabilirAsync(FormIzi))
            YeniCekFormu();
    }

    private void YeniCekFormu()
    {
        Duzenlenen = null;
        FormYon = YonSecenekleri.First(y => y.Kod == Yon);
```

`Kasa.App.Core/CekTakipViewModel.cs` (10/14) — Bul:

```csharp
        _duzenlenenSurum = 0;
        _kayit.Temizle();
        AyniCekSifirla();
        FormAcik = true;
    }

    [RelayCommand]
    private void Duzelt()
    {
        if (Acik is not { } c)
            return;
        Duzenlenen = c.Id;
        _duzenlenenSurum = c.Surum;
        FormYon = YonSecenekleri.First(y => y.Kod == c.Yon);
        FormTur = TurSecenekleri.First(t => t.Kod == c.Tur);
```

Yerine:

```csharp
        _duzenlenenSurum = 0;
        _kayit.Temizle();
        AyniCekSifirla();
        FormuAc();
    }

    /// <summary>"Çeki düzelt": yazılmış çek formu varsa önce onay sorulur; sonra açık çek forma açılır.</summary>
    [RelayCommand]
    private async Task DuzeltAsync()
    {
        if (Acik is not { } c || !await BirakilabilirAsync(FormIzi))
            return;
        _duzenlenenOzet = $"{c.VadeTarihi:dd.MM.yyyy} · {c.Kisi}";
        Duzenlenen = c.Id;
        OnPropertyChanged(nameof(FormBasligi));
        _duzenlenenSurum = c.Surum;
        FormYon = YonSecenekleri.First(y => y.Kod == c.Yon);
        FormTur = TurSecenekleri.First(t => t.Kod == c.Tur);
```

`Kasa.App.Core/CekTakipViewModel.cs` (11/14) — Bul:

```csharp
        Konum = KonumSecenekleri.FirstOrDefault(k => k.Kod == c.Konum) ?? KonumSecenekleri[0];
        _kayit.Temizle();
        AyniCekSifirla();
        FormAcik = true;
    }

    [RelayCommand] private void FormuKapat() => FormAcik = false;

    [RelayCommand]
    private Task YineDeKaydetAsync()
```

Yerine:

```csharp
        Konum = KonumSecenekleri.FirstOrDefault(k => k.Kod == c.Konum) ?? KonumSecenekleri[0];
        _kayit.Temizle();
        AyniCekSifirla();
        FormuAc();
    }

    /// <summary>Form şimdiki değerleriyle açıldı: hataları kalkar, kaydedilmemiş değişiklik tabanı bu değerlerdir.</summary>
    private void FormuAc()
    {
        Hatalar.Temizle();
        FormAcik = true;
        FormIzi.Ac();
    }

    /// <summary>"Vazgeç": bilerek bırakmaktır, onay sorulmaz; form kapanır.</summary>
    [RelayCommand]
    private void FormuKapat() => FormuKapatOnaysiz();

    private void FormuKapatOnaysiz()
    {
        FormAcik = false;
        FormIzi.Kapat();
        Hatalar.Temizle();
    }

    [RelayCommand]
    private Task YineDeKaydetAsync()
```

`Kasa.App.Core/CekTakipViewModel.cs` (12/14) — Bul:

```csharp
        return KaydetAsync();
    }

    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || FormYon is not { } yon || FormTur is not { } tur)
            return;
        if (!ParaAyristirici.GecerliMi(Tutar))
        {
            Hata = ParaAyristirici.GecersizMesaji;
            return;
        }
        var g = new CekYaz(Guid.Empty, Duzenlenen is null ? 0 : _duzenlenenSurum, tur.Kod, yon.Kod, No.Trim(), string.IsNullOrWhiteSpace(Banka) ? null : Banka.Trim(), Kisi.Trim(), Tutar,
            DateOnly.FromDateTime(Vade), yon.Kod == CekYonleri.Verilen ? CekKasasi : null, Teminat, yon.Kod == CekYonleri.Alinan ? Konum?.Kod : null,
            string.IsNullOrWhiteSpace(Not) ? null : Not.Trim());
```

Yerine:

```csharp
        return KaydetAsync();
    }

    /// <summary>Ön doğrulama (tasarım §1): sunucunun çek kuralları (CekEndpoints.CekAlanlari) alanın altında, istek gönderilmeden.</summary>
    private bool CekFormuGecerli(KodCipi yon, KodCipi tur)
    {
        var h = Hatalar;
        h.Denetle(!string.IsNullOrWhiteSpace(No), nameof(No), "Çek / senet numarası boş olamaz.");
        h.Denetle(tur.Kod != CekTurleri.Cek || !string.IsNullOrWhiteSpace(Banka), nameof(Banka), "Banka boş olamaz.");
        h.Denetle(!string.IsNullOrWhiteSpace(Kisi), nameof(Kisi), "Kişi boş olamaz.");
        h.Denetle(ParaAyristirici.GecerliMi(Tutar), nameof(Tutar), ParaAyristirici.GecersizMesaji);
        h.Denetle(Tutar > 0, nameof(Tutar), "Tutar sıfırdan büyük olmalı.");
        h.Denetle(Vade.Year >= 2000, nameof(Vade), "Vade tarihi 01.01.2000 tarihinden önce olamaz.");
        h.Denetle(yon.Kod != CekYonleri.Verilen || !string.IsNullOrWhiteSpace(CekKasasi), nameof(CekKasasi), "Ödeneceği kasayı (kanal ya da Ortak) seçin.");
        return !h.Var;
    }

    [RelayCommand]
    private Task KaydetAsync() => FormIsleAsync(Hatalar, async n =>
    {
        if (!EditorMu || FormYon is not { } yon || FormTur is not { } tur || !CekFormuGecerli(yon, tur))
            return;
        var g = new CekYaz(Guid.Empty, Duzenlenen is null ? 0 : _duzenlenenSurum, tur.Kod, yon.Kod, No.Trim(), string.IsNullOrWhiteSpace(Banka) ? null : Banka.Trim(), Kisi.Trim(), Tutar,
            DateOnly.FromDateTime(Vade), yon.Kod == CekYonleri.Verilen ? CekKasasi : null, Teminat, yon.Kod == CekYonleri.Alinan ? Konum?.Kod : null,
            string.IsNullOrWhiteSpace(Not) ? null : Not.Trim());
```

`Kasa.App.Core/CekTakipViewModel.cs` (13/14) — Bul:

```csharp
            return;
        _kayit.Temizle();
        AyniCekSifirla();
        FormAcik = false;
        Mesaj = (Duzenlenen is null ? "Çek kaydedildi" : "Çek güncellendi") + GorunurlukEki(Guncelle(sonuc));
        await OzetiYenileAsync(n);
    });
```

Yerine:

```csharp
            return;
        _kayit.Temizle();
        AyniCekSifirla();
        FormuKapatOnaysiz();
        Mesaj = (Duzenlenen is null ? "Çek kaydedildi" : "Çek güncellendi") + GorunurlukEki(Guncelle(sonuc));
        await OzetiYenileAsync(n);
    });
```

`Kasa.App.Core/CekTakipViewModel.cs` (14/14) — Bul:

```csharp
        CekKasaSecenekleri.Clear();
        Acik = null;
        Ozet = null;
        FormAcik = false;
        _sonKanal = null;
        SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, null);
        Ara = "";
```

Yerine:

```csharp
        CekKasaSecenekleri.Clear();
        Acik = null;
        Ozet = null;
        FormuKapatOnaysiz();
        _sonKanal = null;
        SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, null);
        Ara = "";
```

`Kasa.App/Views/CekTakipPage.cs` (1/5) — Bul:

```csharp
    private readonly SorguSecimi _secim = new("CekId");
    private CekHazirSuzgec? _bekleyenSuzgec;
    private readonly View _ayrinti;

    /// <summary>Sorgudaki süzgeç adı ("Alinan30" gibi). Yalnız tanımlı ad kabul edilir: Enum.TryParse "1" ya da "7" gibi sayıları da
    /// çözer, bunlar yok sayılır.</summary>
```

Yerine:

```csharp
    private readonly SorguSecimi _secim = new("CekId");
    private CekHazirSuzgec? _bekleyenSuzgec;
    private readonly View _ayrinti;
    /// <summary>Çek formu ve hareket formu ile genel hata kutuları: kaydetme başarısız olunca ilk hatalı alana kaydırılır (ÇK-01).</summary>
    private readonly View _form, _formHataKutusu, _hareketFormu, _hareketHataKutusu;

    /// <summary>Sorgudaki süzgeç adı ("Alinan30" gibi). Yalnız tanımlı ad kabul edilir: Enum.TryParse "1" ya da "7" gibi sayıları da
    /// çözer, bunlar yok sayılır.</summary>
```

`Kasa.App/Views/CekTakipPage.cs` (2/5) — Bul:

```csharp
    public CekTakipPage(CekTakipViewModel vm) : base(vm, "Çekler ve senetler",
        "Çek kasayı yalnız tahsil, ödeme, ciro, kırdırma ya da dönüş gününde etkiler; kayıt ve vade günü kasayı değiştirmez.", vm.YukleAsync)
    {
        _ayrinti = Ayrinti(vm);
        Govde.Add(Serit(vm));
        Govde.Add(Kart("Süzgeçler", Cipler(nameof(vm.YonCipleri), nameof(vm.SecYonCommand)), Cipler(nameof(vm.DurumCipleri), nameof(vm.SecDurumCommand)),
            Alan("Kişi, banka ya da numara", Girdi(nameof(vm.Ara))), Dugme("Ara", nameof(vm.AraCommand)),
            Goster(new VerticalStackLayout { Spacing = 6, Children = { Bagli(nameof(vm.VadeSuzgeci)), Dugme("Vade süzgecini kaldır", nameof(vm.VadeSuzgeciniKaldirCommand)) } },
                nameof(vm.VadeSuzgeciVar))));
        Govde.Add(Editor(Dugme("Yeni çek / senet", nameof(vm.YeniCekCommand))));
        Govde.Add(Editor(Goster(Form(vm), nameof(vm.FormAcik))));
        Govde.Add(Kart("Liste", SatirListesi(vm, nameof(vm.OncekiSatirlar), true), _ayrinti,
            SatirListesi(vm, nameof(vm.SonrakiSatirlar), false)));
    }
```

Yerine:

```csharp
    public CekTakipPage(CekTakipViewModel vm) : base(vm, "Çekler ve senetler",
        "Çek kasayı yalnız tahsil, ödeme, ciro, kırdırma ya da dönüş gününde etkiler; kayıt ve vade günü kasayı değiştirmez.", vm.YukleAsync)
    {
        _formHataKutusu = FormHatasi(nameof(vm.Hatalar) + ".Genel");
        _hareketHataKutusu = FormHatasi(nameof(vm.HareketHatalari) + ".Genel");
        _hareketFormu = HareketFormu(vm, _hareketHataKutusu);
        _ayrinti = Ayrinti(vm, _hareketFormu);
        _form = Form(vm, _formHataKutusu);
        vm.Hatalar.GosterIstendi += (_, _) => Gorunur.HatayaGit(_form, vm.Hatalar, _formHataKutusu);
        vm.HareketHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(_hareketFormu, vm.HareketHatalari, _hareketHataKutusu);
        Govde.Add(Serit(vm));
        Govde.Add(Kart("Süzgeçler", Cipler(nameof(vm.YonCipleri), nameof(vm.SecYonCommand)), Cipler(nameof(vm.DurumCipleri), nameof(vm.SecDurumCommand)),
            Alan("Kişi, banka ya da numara", Girdi(nameof(vm.Ara))), Dugme("Ara", nameof(vm.AraCommand)),
            Goster(new VerticalStackLayout { Spacing = 6, Children = { Bagli(nameof(vm.VadeSuzgeci)), Dugme("Vade süzgecini kaldır", nameof(vm.VadeSuzgeciniKaldirCommand)) } },
                nameof(vm.VadeSuzgeciVar))));
        Govde.Add(Editor(Dugme("Yeni çek / senet", nameof(vm.YeniCekCommand))));
        Govde.Add(Editor(Goster(_form, nameof(vm.FormAcik))));
        Govde.Add(Kart("Liste", SatirListesi(vm, nameof(vm.OncekiSatirlar), true), _ayrinti,
            SatirListesi(vm, nameof(vm.SonrakiSatirlar), false)));
    }
```

`Kasa.App/Views/CekTakipPage.cs` (3/5) — Bul:

```csharp
        return Task.CompletedTask;
    }, "Aç / kapat", bosMetin: bosMetin, aciklama: s => $"{s.Baslik}, {(vm.Acik?.Id == s.Veri.Id ? "açık" : "kapalı")}");

    private View Ayrinti(CekTakipViewModel vm)
    {
        var hareketFormu = Goster(new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Alan("Tarih", Tarih(nameof(vm.HareketTarihi))), Alan("Tutar", Girdi(nameof(vm.HareketTutari), para: true)),
                Goster(Alan("Kasa (kanal)", Secim(nameof(vm.KasaSecenekleri), nameof(vm.HareketKasasi), ".")), nameof(vm.KasaGerekli)),
                Goster(Alan("Ciro edilen kişi / banka ya da faktoring", Girdi(nameof(vm.Karsi))), nameof(vm.KarsiGerekli)),
                Goster(Alan("Hesaba geçen tutar", Girdi(nameof(vm.NetTutar), para: true)), nameof(vm.NetGerekli)),
                Bagli(nameof(vm.MasrafMetni)), Dugme("Hareketi kaydet", nameof(vm.HareketKaydetCommand)),
            },
        }, nameof(vm.HareketFormuAcik));
        var duzenleme = new VerticalStackLayout
        {
            Spacing = 10,
```

Yerine:

```csharp
        return Task.CompletedTask;
    }, "Aç / kapat", bosMetin: bosMetin, aciklama: s => $"{s.Baslik}, {(vm.Acik?.Id == s.Veri.Id ? "açık" : "kapalı")}");

    /// <summary>Hareket formu: genel hata formun en üstünde, alan hataları alanın altında (tasarım 2026-10-02 §1).</summary>
    private static View HareketFormu(CekTakipViewModel vm, View hataKutusu)
    {
        const string h = nameof(vm.HareketHatalari);
        return Goster(new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                hataKutusu,
                Alan("Tarih", Tarih(nameof(vm.HareketTarihi)), h, nameof(vm.HareketTarihi)),
                Alan("Tutar", Girdi(nameof(vm.HareketTutari), para: true), h, nameof(vm.HareketTutari)),
                Goster(Alan("Kasa (kanal)", Secim(nameof(vm.KasaSecenekleri), nameof(vm.HareketKasasi), "."), h, nameof(vm.HareketKasasi)), nameof(vm.KasaGerekli)),
                Goster(Alan("Ciro edilen kişi / banka ya da faktoring", Girdi(nameof(vm.Karsi)), h, nameof(vm.Karsi)), nameof(vm.KarsiGerekli)),
                Goster(Alan("Hesaba geçen tutar", Girdi(nameof(vm.NetTutar), para: true), h, nameof(vm.NetTutar)), nameof(vm.NetGerekli)),
                Bagli(nameof(vm.MasrafMetni)), Dugme("Hareketi kaydet", nameof(vm.HareketKaydetCommand)),
            },
        }, nameof(vm.HareketFormuAcik));
    }

    private View Ayrinti(CekTakipViewModel vm, View hareketFormu)
    {
        var duzenleme = new VerticalStackLayout
        {
            Spacing = 10,
```

`Kasa.App/Views/CekTakipPage.cs` (4/5) — Bul:

```csharp
            await islem();
    }

    private static View Form(CekTakipViewModel vm)
    {
        var konum = Alan("Konum", Secim(nameof(vm.KonumSecenekleri), nameof(vm.Konum)));
        konum.SetBinding(IsVisibleProperty, nameof(vm.FormVerilen), converter: new Converters.TersIseConverter());
        var ayni = Goster(new VerticalStackLayout
```

Yerine:

```csharp
            await islem();
    }

    /// <summary>Çek / senet formu: başlık düzenleme modunu söyler; genel hata formun en üstünde, alan hataları alanın altında
    /// (tasarım 2026-10-02 §1-2; ÇK-01).</summary>
    private static View Form(CekTakipViewModel vm, View hataKutusu)
    {
        const string h = nameof(vm.Hatalar);
        var konum = Alan("Konum", Secim(nameof(vm.KonumSecenekleri), nameof(vm.Konum)));
        konum.SetBinding(IsVisibleProperty, nameof(vm.FormVerilen), converter: new Converters.TersIseConverter());
        var ayni = Goster(new VerticalStackLayout
```

`Kasa.App/Views/CekTakipPage.cs` (5/5) — Bul:

```csharp
            Spacing = 8,
            Children = { BagliHata(nameof(vm.AyniCekUyarisi)), Dugme("Yine de kaydet", nameof(vm.YineDeKaydetCommand)) },
        }, nameof(vm.AyniCekVar));
        var kart = Kart("Çek / senet bilgileri",
            Bagli(nameof(vm.FormBasligi)),
            Alan("Tür", Secim(nameof(vm.TurSecenekleri), nameof(vm.FormTur))), Alan("Yön", Secim(nameof(vm.YonSecenekleri), nameof(vm.FormYon))),
            Alan("Çek / senet numarası", Girdi(nameof(vm.No))), Alan("Banka (senette boş olabilir)", Girdi(nameof(vm.Banka))),
            Alan("Kişi (alınanda kimden, verilende kime)", Girdi(nameof(vm.Kisi))), Alan("Tutar", Girdi(nameof(vm.Tutar), para: true)),
            Alan("Vade", Tarih(nameof(vm.Vade))),
            Goster(Alan("Ödeneceği kasa", Secim(nameof(vm.CekKasaSecenekleri), nameof(vm.CekKasasi), ".")), nameof(vm.FormVerilen)),
            konum, Onay("Teminat çeki (bildirim çıkmaz, panel toplamlarına girmez)", nameof(vm.Teminat)), Alan("Not", Girdi(nameof(vm.Not))),
            ayni, Dugme("Kaydet", nameof(vm.KaydetCommand)), Dugme("Vazgeç", nameof(vm.FormuKapatCommand)));
        return kart;
    }
}
```

Yerine:

```csharp
            Spacing = 8,
            Children = { BagliHata(nameof(vm.AyniCekUyarisi)), Dugme("Yine de kaydet", nameof(vm.YineDeKaydetCommand)) },
        }, nameof(vm.AyniCekVar));
        var baslik = Bagli(nameof(vm.FormBasligi));
        baslik.FontAttributes = FontAttributes.Bold;
        var kaydet = Dugme("Kaydet", nameof(vm.KaydetCommand));
        kaydet.SetBinding(Button.TextProperty, nameof(vm.KaydetMetni));
        var kart = Kart("Çek / senet bilgileri",
            baslik, hataKutusu,
            Alan("Tür", Secim(nameof(vm.TurSecenekleri), nameof(vm.FormTur))), Alan("Yön", Secim(nameof(vm.YonSecenekleri), nameof(vm.FormYon))),
            Alan("Çek / senet numarası", Girdi(nameof(vm.No)), h, nameof(vm.No)),
            Alan("Banka (senette boş olabilir)", Girdi(nameof(vm.Banka)), h, nameof(vm.Banka)),
            Alan("Kişi (alınanda kimden, verilende kime)", Girdi(nameof(vm.Kisi)), h, nameof(vm.Kisi)),
            Alan("Tutar", Girdi(nameof(vm.Tutar), para: true), h, nameof(vm.Tutar)),
            Alan("Vade", Tarih(nameof(vm.Vade)), h, nameof(vm.Vade)),
            Goster(Alan("Ödeneceği kasa", Secim(nameof(vm.CekKasaSecenekleri), nameof(vm.CekKasasi), "."), h, nameof(vm.CekKasasi)), nameof(vm.FormVerilen)),
            konum, Onay("Teminat çeki (bildirim çıkmaz, panel toplamlarına girmez)", nameof(vm.Teminat)), Alan("Not", Girdi(nameof(vm.Not)), h, nameof(vm.Not)),
            ayni, kaydet, Dugme("Vazgeç", nameof(vm.FormuKapatCommand)));
        return kart;
    }
}
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1132`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/CekFormuTests.cs Kasa.App.Core.Tests/CekTakipViewModelTests.cs Kasa.App.Core/CekTakipViewModel.cs Kasa.App/Views/CekTakipPage.cs
git commit -F - <<'MESAJ'
feat(app): Çekler formlarında alan hataları ve hataya kaydırma

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 15: Kartlar — yeni kart ve kart düzenleme formu

Alan hataları (KR-04), "Yeni kart" / "Düzenleniyor: …" başlığı; başka karta geçiş ve yeni kart onay ister, aynı kartın formları arasında sormaz.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/KartTakipViewModel.Gorunum.cs`
- Değiştir: `Kasa.App.Core/KartTakipViewModel.cs`
- Değiştir: `Kasa.App/Views/KartTakipPage.cs`
- Test (değiştir): `Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/GecersizTutarTests.cs`
- Test (oluştur): `Kasa.App.Core.Tests/KartFormuTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/KartTakipGorunumTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs` (1/3) — Bul:

```csharp
        vm.AcilisTarihi = new DateTime(2026, 9, 1);
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Contains("ulaşılamadı", vm.Hata);
        Assert.Equal(3, vm.Secili!.Surum);
        await vm.KaydetCommand.ExecuteAsync(null);
        var duzenleme = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
```

Yerine:

```csharp
        vm.AcilisTarihi = new DateTime(2026, 9, 1);
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.KartHatalari.Genel);
        Assert.Equal(3, vm.Secili!.Surum);
        await vm.KaydetCommand.ExecuteAsync(null);
        var duzenleme = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
```

`Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs` (2/3) — Bul:

```csharp
        vm.AcilisPaylari[0].Tutar = 50;
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Contains("ulaşılamadı", vm.Hata);
        Assert.Null(vm.Secili);
        await vm.KaydetCommand.ExecuteAsync(null);
        var yeni = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
```

Yerine:

```csharp
        vm.AcilisPaylari[0].Tutar = 50;
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.KartHatalari.Genel);
        Assert.Null(vm.Secili);
        await vm.KaydetCommand.ExecuteAsync(null);
        var yeni = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
```

`Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs` (3/3) — Bul:

```csharp
        vm.Ad = "   ";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Cagrilar);
        Assert.Contains("Kart adını", vm.Hata);
        vm.Ad = "Kart";
        vm.KesimGunu = 32;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Cagrilar);
        Assert.Contains("1–31", vm.Hata);

        var (izleyici, izleyiciApi) = await KartVm(Rol.Izleyici);
        izleyici.Ad = "Kart 2";
```

Yerine:

```csharp
        vm.Ad = "   ";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Cagrilar);
        Assert.Equal("Kart / banka adı boş olamaz.", vm.KartHatalari[nameof(vm.Ad)]);
        vm.Ad = "Kart";
        vm.KesimGunu = 32;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Cagrilar);
        Assert.Equal("Kesim günü 1 ile 31 arasında olmalı.", vm.KartHatalari[nameof(vm.KesimGunu)]);

        var (izleyici, izleyiciApi) = await KartVm(Rol.Izleyici);
        izleyici.Ad = "Kart 2";
```

`Kasa.App.Core.Tests/GecersizTutarTests.cs` — Bul:

```csharp
        vm.Limit = G;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
        vm.Limit = 1000;
        vm.AcilisBorc = G;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
    }

    [Fact]
```

Yerine:

```csharp
        vm.Limit = G;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.KartHatalari[nameof(vm.Limit)]);
        vm.Limit = 1000;
        vm.AcilisBorc = G;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.KartHatalari[nameof(vm.AcilisBorc)]);
    }

    [Fact]
```

`Kasa.App.Core.Tests/KartFormuTests.cs` (yeni dosya):

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kartlar: yeni kart ve "Kartı düzenle" formu (tasarım 2026-10-02 §1-2; KR-04): başlık, alan hataları ve kaydedilmemiş
/// değişiklikte başka karta geçiş / yeni kart onayı.</summary>
public class KartFormuTests
{
    private static async Task<(KartTakipViewModel Vm, FinansTakipTests.Sahte Api)> Kur()
    {
        var api = new FinansTakipTests.Sahte();
        var ikinci = FinansTakipTests.Sahte.OrnekKart() with { Id = 2, Ad = "World" };
        api.KartlarYaniti = Task.FromResult<IReadOnlyList<KartTakipDto>>([api.Kart, ikinci]);
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new KartTakipViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Yeni_kart_formu_bos_kayitta_alanlari_ayri_ayri_soyler()
    {
        var (vm, api) = await Kur();
        await vm.YeniKartAcCommand.ExecuteAsync(null);
        Assert.Equal(("Yeni kart", "Kartı kaydet"), (vm.KartFormuBasligi, vm.KartKaydetMetni));
        vm.KesimGunu = 0;
        vm.SonOdemeGunu = 40;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal("Kart / banka adı boş olamaz.", vm.KartHatalari[nameof(vm.Ad)]);
        Assert.Equal("Kesim günü 1 ile 31 arasında olmalı.", vm.KartHatalari[nameof(vm.KesimGunu)]);
        Assert.Equal("Son ödeme günü 1 ile 31 arasında olmalı.", vm.KartHatalari[nameof(vm.SonOdemeGunu)]);
        Assert.Equal(nameof(vm.Ad), vm.KartHatalari.IlkAlan);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);

        vm.Ad = "Bonus";
        Assert.Null(vm.KartHatalari[nameof(vm.Ad)]);
        vm.VazgecCommand.Execute(null);
        Assert.False(vm.KartHatalari.Var);
    }

    [Fact]
    public async Task Duzenleme_basligi_kartin_adini_soyler()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        Assert.Equal(("Düzenleniyor: Kart", "Değişikliği kaydet"), (vm.KartFormuBasligi, vm.KartKaydetMetni));
    }

    [Fact]
    public async Task Yazilmis_form_baska_karta_ve_yeni_karta_gecmeden_once_onay_ister_ayni_kartin_formlari_arasinda_sormaz()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        vm.OdemeTutari = 250m;
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var sorulan = 0;
        var cevap = false;
        vm.BirakmaOnayi = _ => { sorulan++; return Task.FromResult(cevap); };
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[1]);
        Assert.Equal((1, KartFormu.Odeme, 250m), (vm.AcikKartId, vm.AcikForm, vm.OdemeTutari));
        await vm.YeniKartAcCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.AcikKartId);

        vm.FormAcCommand.Execute(KartFormu.Harcama);   // aynı kart: sorulmaz
        Assert.Equal(KartFormu.Harcama, vm.AcikForm);
        Assert.Equal(2, sorulan);

        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "Yeni ad";
        cevap = true;
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[1]);
        Assert.Equal((2, KartFormu.Yok), (vm.AcikKartId, vm.AcikForm));
        Assert.Equal("World", vm.Ad);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        Assert.Equal(3, sorulan);
    }

    [Fact]
    public async Task Degisiklikleri_birakmak_formu_kapatir_ve_kartin_kayitli_degerlerine_doner()
    {
        var (vm, _) = await Kur();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "Değişti";
        vm.Limit = 5m;

        vm.DegisiklikleriBirak();

        Assert.Equal((KartFormu.Yok, "Kart", 1000m), (vm.AcikForm, vm.Ad, vm.Limit));
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }
}
```

`Kasa.App.Core.Tests/KartTakipGorunumTests.cs` (1/3) — Bul:

```csharp
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Hata);
        vm.VazgecCommand.Execute(null);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.Null(vm.Hata);
    }
```

Yerine:

```csharp
        vm.FormAcCommand.Execute(KartFormu.KartBilgisi);
        vm.Ad = "";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.KartHatalari[nameof(vm.Ad)]);
        vm.VazgecCommand.Execute(null);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
        Assert.False(vm.KartHatalari.Var);
        Assert.Null(vm.Hata);
    }
```

`Kasa.App.Core.Tests/KartTakipGorunumTests.cs` (2/3) — Bul:

```csharp
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);
        Assert.Equal("Kart adını, limiti ve 1–31 arası günleri kontrol edin.", vm.FormHatasi);
        Assert.Null(vm.SayfaHatasi);
        vm.VazgecCommand.Execute(null);
        vm.Hata = "Sunucuya ulaşılamadı.";
```

Yerine:

```csharp
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(KartFormu.KartBilgisi, vm.AcikForm);
        Assert.Equal("Kart / banka adı boş olamaz.", vm.KartHatalari[nameof(vm.Ad)]);
        Assert.Null(vm.SayfaHatasi);
        vm.VazgecCommand.Execute(null);
        vm.Hata = "Sunucuya ulaşılamadı.";
```

`Kasa.App.Core.Tests/KartTakipGorunumTests.cs` (3/3) — Bul:

```csharp
        Assert.Contains(nameof(vm.AcikKartId), bildirilen);

        bildirilen.Clear();
        await vm.KaydetCommand.ExecuteAsync(null);   // boş ad: formun hatası
        Assert.NotNull(vm.FormHatasi);
        Assert.Contains(nameof(vm.FormHatasi), bildirilen);
        Assert.Contains(nameof(vm.SayfaHatasi), bildirilen);

        bildirilen.Clear();
        vm.VazgecCommand.Execute(null);
```

Yerine:

```csharp
        Assert.Contains(nameof(vm.AcikKartId), bildirilen);

        bildirilen.Clear();
        await vm.KaydetCommand.ExecuteAsync(null);   // boş ad: formun alanında
        Assert.Equal("Kart / banka adı boş olamaz.", vm.KartHatalari[nameof(vm.Ad)]);
        Assert.Null(vm.SayfaHatasi);

        bildirilen.Clear();
        vm.VazgecCommand.Execute(null);
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartFormuTests|FullyQualifiedName~KartTakipGorunumTests|FullyQualifiedName~TakipKomutlariTests|FullyQualifiedName~GecersizTutarTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 10 farklı ileti; örnekler:

- `GecersizTutarTests.cs: CS1061: 'KartTakipViewModel' bir 'KartHatalari' tanımı içermiyor ve 'KartTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KartHatalari' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `KartFormuTests.cs: CS1061: 'IRelayCommand' bir 'ExecuteAsync' tanımı içermiyor ve 'IRelayCommand' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'ExecuteAsync' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `KartFormuTests.cs: CS1061: 'KartTakipViewModel' bir 'KartFormuBasligi' tanımı içermiyor ve 'KartTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KartFormuBasligi' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `KartFormuTests.cs: CS1061: 'KartTakipViewModel' bir 'KartKaydetMetni' tanımı içermiyor ve 'KartTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KartKaydetMetni' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/KartTakipViewModel.Gorunum.cs` (1/5) — Bul:

```csharp
    /// <summary>Formdan bağımsız hata (yükleme, iptal …) ve kaynağı artık açık olmayan formun hatası sayfa başında gösterilir.</summary>
    public string? SayfaHatasi => FormHatasi is null ? Hata : null;

    /// <summary>Hatası <paramref name="kaynak"/> formuna ait tekil işlem (<see cref="KartFormu.Yok"/>: sayfaya ait).</summary>
    private Task YurutAsync(KartFormu kaynak, Func<int, Task> islem) => YurutAsync(n =>
    {
```

Yerine:

```csharp
    /// <summary>Formdan bağımsız hata (yükleme, iptal …) ve kaynağı artık açık olmayan formun hatası sayfa başında gösterilir.</summary>
    public string? SayfaHatasi => FormHatasi is null ? Hata : null;

    /// <summary>Kart bilgileri formunun (yeni kart ve "Kartı düzenle") hataları (tasarım 2026-10-02 §1; KR-04). Sunucu kart hatalarını
    /// alan adı olmadan ({ hata }) döndürür: sunucu iletisi genel hataya gider.</summary>
    public AlanHatalari KartHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [KartHatalari];

    /// <summary>Kart bilgileri formunun başlığı: yeni kartta "Yeni kart", düzenlemede "Düzenleniyor: Bonus".</summary>
    public string KartFormuBasligi => Secili is { } kart ? $"Düzenleniyor: {kart.Ad}" : "Yeni kart";
    public string KartKaydetMetni => Secili is null ? "Kartı kaydet" : "Değişikliği kaydet";

    /// <summary>Açık formun açıldığı andaki değerleri (kart bilgileri, ödeme, harcama): kaydedilmemiş değişiklik ölçütü (tasarım §2).
    /// Diğer formlar (masraf, ekstre, geçiş) izlenmez.</summary>
    private KaydedilmemisDegisiklik? _formIzi;
    private KaydedilmemisDegisiklik FormIzi => _formIzi ??= new(() => AcikForm switch
    {
        KartFormu.KartBilgisi => new
        {
            Ad,
            Limit,
            KesimGunu,
            SonOdemeGunu,
            AcilisTarihi,
            AcilisBorc,
            Paylar = AcilisPaylari.Select(p => new { Kanal = p.Kanal?.Id, p.Tutar }).ToList(),
        },
        KartFormu.Odeme => new { OdemeTarihi, OdemeTutari, Ekstre = OdemeEkstresi?.Veri.Id, OdemeNotu },
        KartFormu.Harcama => (object)new
        {
            HarcamaTarihi,
            HarcamaAciklama,
            HarcamaTutari,
            TaksitSayisi,
            IlkKesimVar,
            IlkKesimTarihi,
            Iade = IadeKaynagi?.Veri.Id,
            Paylar = HarcamaPaylari.Select(p => new { Kanal = p.Kanal?.Id, p.Tutar }).ToList(),
        },
        _ => null,
    });

    public bool KaydedilmemisDegisiklikVar => FormIzi.Var;

    /// <summary>Yazılmış değişiklikleri bırakır: form kapanır, kartın form alanları kartın kayıtlı değerlerine (yeni kartta boşa) döner.</summary>
    public void DegisiklikleriBirak()
    {
        AcikForm = KartFormu.Yok;
        KartFormlariniTemizle();
        if (Secili is { } kart)
            Sec(new KartTakipSatiri(kart, _zaman));
        else
            Yeni();
    }

    /// <summary>Başka karta geçiş, kartı kapatma ve yeni kart öncesi: yazılmış değişiklik varsa onay sorulur; "Bırak" seçilirse
    /// değişiklikler bırakılır. Aynı kartın formları arasında geçiş sorulmaz (aynı kayıt).</summary>
    private async Task<bool> FormdanCikilabilirAsync()
    {
        if (!FormIzi.Var)
            return true;
        if (!await BirakilabilirAsync(FormIzi))
            return false;
        DegisiklikleriBirak();
        return true;
    }

    /// <summary>Hatası <paramref name="kaynak"/> formuna ait tekil işlem (<see cref="KartFormu.Yok"/>: sayfaya ait).</summary>
    private Task YurutAsync(KartFormu kaynak, Func<int, Task> islem) => YurutAsync(n =>
    {
```

`Kasa.App.Core/KartTakipViewModel.Gorunum.cs` (2/5) — Bul:

```csharp
        _ => false,
    };

    /// <summary>Formu kapatır; formun hatası ve benzer kayıt uyarısı da kalkar (<see cref="OnAcikFormChanged"/>).</summary>
    [RelayCommand]
    private void Vazgec() => AcikForm = KartFormu.Yok;

    /// <summary>Kutuya tıklandı: kart açık değilse açılır (<see cref="Sec"/>), açıksa kapanır.</summary>
    [RelayCommand]
    private void KutuSec(KartTakipSatiri? satir)
    {
        if (satir is null)
            return;
        if (Secili?.Id == satir.Veri.Id)
            Yeni();
```

Yerine:

```csharp
        _ => false,
    };

    /// <summary>Formu kapatır; formun hatası ve benzer kayıt uyarısı da kalkar (<see cref="OnAcikFormChanged"/>). "Vazgeç" bilerek
    /// bırakmaktır: onay sorulmaz (tasarım §2 onayı başka kayda geçiş, Yeni ve sayfadan çıkışta ister).</summary>
    [RelayCommand]
    private void Vazgec() => AcikForm = KartFormu.Yok;

    /// <summary>Kutuya tıklandı: kart açık değilse açılır (<see cref="Sec"/>), açıksa kapanır. Yazılmış form varsa önce onay sorulur.</summary>
    [RelayCommand]
    private async Task KutuSecAsync(KartTakipSatiri? satir)
    {
        if (satir is null || !await FormdanCikilabilirAsync())
            return;
        if (Secili?.Id == satir.Veri.Id)
            Yeni();
```

`Kasa.App.Core/KartTakipViewModel.Gorunum.cs` (3/5) — Bul:

```csharp
            Sec(satir);
    }

    /// <summary>"Yeni kart ekle" kutusu: boş kart bilgileri formunu açar; form açıkken tekrar tıklamak kapatır.</summary>
    [RelayCommand]
    private void YeniKartAc()
    {
        if (!EditorMu)
            return;
        if (YeniKartFormuAcik)
        {
```

Yerine:

```csharp
            Sec(satir);
    }

    /// <summary>"Yeni kart ekle" kutusu: boş kart bilgileri formunu açar; form açıkken tekrar tıklamak kapatır. Yazılmış form varsa
    /// önce onay sorulur.</summary>
    [RelayCommand]
    private async Task YeniKartAcAsync()
    {
        if (!EditorMu || !await FormdanCikilabilirAsync())
            return;
        if (YeniKartFormuAcik)
        {
```

`Kasa.App.Core/KartTakipViewModel.Gorunum.cs` (4/5) — Bul:

```csharp
    /// formundan çıkılınca benzer kayıt uyarısı ve onayı kalkar.</summary>
    partial void OnAcikFormChanged(KartFormu oldValue, KartFormu newValue)
    {
        if (HataKaynagi == oldValue && oldValue != KartFormu.Yok)
        {
            HataKaynagi = KartFormu.Yok;
```

Yerine:

```csharp
    /// formundan çıkılınca benzer kayıt uyarısı ve onayı kalkar.</summary>
    partial void OnAcikFormChanged(KartFormu oldValue, KartFormu newValue)
    {
        KartHatalari.Temizle();
        if (newValue == KartFormu.Yok)
            FormIzi.Kapat();
        else
            FormIzi.Ac();
        if (HataKaynagi == oldValue && oldValue != KartFormu.Yok)
        {
            HataKaynagi = KartFormu.Yok;
```

`Kasa.App.Core/KartTakipViewModel.Gorunum.cs` (5/5) — Bul:

```csharp
        }
        OnPropertyChanged(nameof(AcikKartId));
        OnPropertyChanged(nameof(YeniKartFormuAcik));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
```

Yerine:

```csharp
        }
        OnPropertyChanged(nameof(AcikKartId));
        OnPropertyChanged(nameof(YeniKartFormuAcik));
        OnPropertyChanged(nameof(KartFormuBasligi));
        OnPropertyChanged(nameof(KartKaydetMetni));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
```

`Kasa.App.Core/KartTakipViewModel.cs` (1/2) — Bul:

```csharp
/// <param name="zaman">Kart kutularındaki "Son ödeme geçti" kuralının saati (yerel gün); verilmezse sistem saati. DI'da kayıtlı
/// değildir (isteğe bağlı parametre varsayılana düşer); testler sabit saat verir.</param>
public partial class KartTakipViewModel(IFinansTakipApi api, IKasaApi finans, AuthViewModel auth, IBenzerKayitApi? benzerlikApi = null,
    IKasaKontrolApi? kontrolApi = null, TimeProvider? zaman = null) : OturumluViewModel(auth)
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
    public BenzerKayitKontrolu HarcamaBenzerlik { get; } = new(benzerlikApi ?? finans as IBenzerKayitApi);
```

Yerine:

```csharp
/// <param name="zaman">Kart kutularındaki "Son ödeme geçti" kuralının saati (yerel gün); verilmezse sistem saati. DI'da kayıtlı
/// değildir (isteğe bağlı parametre varsayılana düşer); testler sabit saat verir.</param>
public partial class KartTakipViewModel(IFinansTakipApi api, IKasaApi finans, AuthViewModel auth, IBenzerKayitApi? benzerlikApi = null,
    IKasaKontrolApi? kontrolApi = null, TimeProvider? zaman = null) : OturumluViewModel(auth), IKaydedilmemisForm
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
    public BenzerKayitKontrolu HarcamaBenzerlik { get; } = new(benzerlikApi ?? finans as IBenzerKayitApi);
```

`Kasa.App.Core/KartTakipViewModel.cs` (2/2) — Bul:

```csharp
        }
        liste.Add(pay);
    }
    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(KartFormu.KartBilgisi, async n =>
    {
        if (!EditorMu)
            return;
        // Açılış borcu, tarihi ve dağılımı yalnız yeni kartta girilir ve okunur (bölüm yalnız YeniKart iken görünür); sunucu
        // güncellemede bu alanları yok sayar. Mevcut kartta görünmeyen bir açılış satırı kaydı reddettirmez.
        var yeni = Secili is null;
        if (!ParaAyristirici.HepsiGecerli(Limit, yeni ? AcilisBorc : 0))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (string.IsNullOrWhiteSpace(Ad) || Limit < 0 || KesimGunu is < 1 or > 31 || SonOdemeGunu is < 1 or > 31)
        { Hata = "Kart adını, limiti ve 1–31 arası günleri kontrol edin."; return; }
        var g = yeni
            ? new KartTakipYaz(Guid.Empty, 0, Ad.Trim(), Limit, KesimGunu, SonOdemeGunu, DateOnly.FromDateTime(AcilisTarihi), AcilisBorc, TakipMetni.Paylar(AcilisPaylari))
            : new KartTakipYaz(Guid.Empty, Secili!.Surum, Ad.Trim(), Limit, KesimGunu, SonOdemeGunu, Secili.TakipBaslangic ?? DateOnly.FromDateTime(AcilisTarihi), 0, Array.Empty<KanalPayYaz>());
```

Yerine:

```csharp
        }
        liste.Add(pay);
    }
    /// <summary>Kart bilgileri formunun ön doğrulaması (tasarım 2026-10-02 §1; KR-04): her kural kendi alanının altında.</summary>
    private bool KartFormuGecerli(bool yeni)
    {
        var h = KartHatalari;
        h.Denetle(!string.IsNullOrWhiteSpace(Ad), nameof(Ad), "Kart / banka adı boş olamaz.");
        h.Denetle(ParaAyristirici.GecerliMi(Limit), nameof(Limit), ParaAyristirici.GecersizMesaji);
        h.Denetle(Limit >= 0, nameof(Limit), "Limit negatif olamaz.");
        h.Denetle(KesimGunu is >= 1 and <= 31, nameof(KesimGunu), "Kesim günü 1 ile 31 arasında olmalı.");
        h.Denetle(SonOdemeGunu is >= 1 and <= 31, nameof(SonOdemeGunu), "Son ödeme günü 1 ile 31 arasında olmalı.");
        h.Denetle(!yeni || ParaAyristirici.GecerliMi(AcilisBorc), nameof(AcilisBorc), ParaAyristirici.GecersizMesaji);
        return !h.Var;
    }

    [RelayCommand]
    private Task KaydetAsync() => FormIsleAsync(KartHatalari, async n =>
    {
        // Açılış borcu, tarihi ve dağılımı yalnız yeni kartta girilir ve okunur (bölüm yalnız YeniKart iken görünür); sunucu
        // güncellemede bu alanları yok sayar. Mevcut kartta görünmeyen bir açılış satırı kaydı reddettirmez.
        var yeni = Secili is null;
        if (!EditorMu || !KartFormuGecerli(yeni))
            return;
        var g = yeni
            ? new KartTakipYaz(Guid.Empty, 0, Ad.Trim(), Limit, KesimGunu, SonOdemeGunu, DateOnly.FromDateTime(AcilisTarihi), AcilisBorc, TakipMetni.Paylar(AcilisPaylari))
            : new KartTakipYaz(Guid.Empty, Secili!.Surum, Ad.Trim(), Limit, KesimGunu, SonOdemeGunu, Secili.TakipBaslangic ?? DateOnly.FromDateTime(AcilisTarihi), 0, Array.Empty<KanalPayYaz>());
```

`Kasa.App/Views/KartTakipPage.cs` (1/5) — Bul:

```csharp
    private readonly View _formAlani;
    /// <summary>Formun tepesindeki hata satırı (FormHatasi): uzun formun altındaki düğmeden gelen hata görünür yere kaydırılır.</summary>
    private readonly Label _formHataSatiri;

    /// <summary>Kart bildirimine tıklanınca //kartlar?KartId={id}: sayfa zaten açıkken istek hemen, değilse sayfa belirirken uygulanır
    /// (SorguSecimi).</summary>
```

Yerine:

```csharp
    private readonly View _formAlani;
    /// <summary>Formun tepesindeki hata satırı (FormHatasi): uzun formun altındaki düğmeden gelen hata görünür yere kaydırılır.</summary>
    private readonly Label _formHataSatiri;
    /// <summary>Kart bilgileri formunun genel hata kutusu (KartHatalari.Genel; tasarım 2026-10-02 §1).</summary>
    private readonly View _kartHataKutusu;

    /// <summary>Kart bildirimine tıklanınca //kartlar?KartId={id}: sayfa zaten açıkken istek hemen, değilse sayfa belirirken uygulanır
    /// (SorguSecimi).</summary>
```

`Kasa.App/Views/KartTakipPage.cs` (2/5) — Bul:

```csharp
    public KartTakipPage(KartTakipViewModel vm) : base(vm, "Kredi Kartları", SayfaAciklamasi, vm.YukleAsync, nameof(vm.SayfaHatasi))
    {
        _formHataSatiri = BagliHata(nameof(vm.FormHatasi));
        _formAlani = FormAlani(vm);
        _ayrinti = new Border
        {
```

Yerine:

```csharp
    public KartTakipPage(KartTakipViewModel vm) : base(vm, "Kredi Kartları", SayfaAciklamasi, vm.YukleAsync, nameof(vm.SayfaHatasi))
    {
        _formHataSatiri = BagliHata(nameof(vm.FormHatasi));
        _kartHataKutusu = FormHatasi(nameof(vm.KartHatalari) + ".Genel");
        _formAlani = FormAlani(vm);
        _ayrinti = new Border
        {
```

`Kasa.App/Views/KartTakipPage.cs` (3/5) — Bul:

```csharp
                Gorunur.Yap(MesajSatiri, KaydirmaHesabi.FormKaydirmasi);
            }
        };
    }

    /// <summary>Ayrıntı: üstü görünür alandaysa kaydırılmaz (kutular görünür kalır), değilse üstü görünür alanın başına gelir.</summary>
```

Yerine:

```csharp
                Gorunur.Yap(MesajSatiri, KaydirmaHesabi.FormKaydirmasi);
            }
        };
        // Kaydetme başarısız olunca ilk hatalı alana (yoksa formun genel hata kutusuna) kaydırılır ve odaklanılır (tasarım §1).
        vm.KartHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(_formAlani, vm.KartHatalari, _kartHataKutusu);
    }

    /// <summary>Ayrıntı: üstü görünür alandaysa kaydırılmaz (kutular görünür kalır), değilse üstü görünür alanın başına gelir.</summary>
```

`Kasa.App/Views/KartTakipPage.cs` (4/5) — Bul:

```csharp
        };
    }

    private View KartBilgileri(KartTakipViewModel vm)
    {
        var acilis = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Alan("Açılış tarihi", Tarih(nameof(vm.AcilisTarihi))), Alan("Açılış borcu", Girdi(nameof(vm.AcilisBorc), true)),
                Metin(AcilisNotu), Paylar(vm.AcilisPaylari, () => vm.PayEkle(vm.AcilisPaylari)),
            },
        };
```

Yerine:

```csharp
        };
    }

    /// <summary>Kart bilgileri formu (yeni kart ve "Kartı düzenle"): başlık modu söyler ("Yeni kart" / "Düzenleniyor: Bonus"); genel
    /// hata formun en üstünde, alan hataları alanın altında (tasarım 2026-10-02 §1-2; KR-04).</summary>
    private View KartBilgileri(KartTakipViewModel vm)
    {
        const string h = nameof(vm.KartHatalari);
        var acilis = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                Alan("Açılış tarihi", Tarih(nameof(vm.AcilisTarihi))), Alan("Açılış borcu", Girdi(nameof(vm.AcilisBorc), true), h, nameof(vm.AcilisBorc)),
                Metin(AcilisNotu), Paylar(vm.AcilisPaylari, () => vm.PayEkle(vm.AcilisPaylari)),
            },
        };
```

`Kasa.App/Views/KartTakipPage.cs` (5/5) — Bul:

```csharp
                vm.Gerekce = gerekce;
                return vm.DurumDegistirAsync();
            })));
        return Form("Kart bilgileri",
            Alan("Kart / banka adı", Girdi(nameof(vm.Ad))), Alan("Limit", Girdi(nameof(vm.Limit), true)),
            Alan("Hesap kesim günü (1–31)", Girdi(nameof(vm.KesimGunu), sayi: true)), Alan("Son ödeme günü (1–31)", Girdi(nameof(vm.SonOdemeGunu), sayi: true)),
            Goster(acilis, nameof(vm.YeniKart)), Dugme("Kartı kaydet", nameof(vm.KaydetCommand)),
            Goster(durum, nameof(vm.KartSecili)), Goster(Devir(vm), nameof(vm.GecisKaydi), true));
    }

    private static View Devir(KartTakipViewModel vm)
```

Yerine:

```csharp
                vm.Gerekce = gerekce;
                return vm.DurumDegistirAsync();
            })));
        var baslik = Bagli(nameof(vm.KartFormuBasligi));
        baslik.Style = (Style)Application.Current!.Resources["LblTakipKartBaslik"];
        var kaydet = Dugme("Kartı kaydet", nameof(vm.KaydetCommand));
        kaydet.SetBinding(Button.TextProperty, nameof(vm.KartKaydetMetni));
        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                baslik, _kartHataKutusu,
                Alan("Kart / banka adı", Girdi(nameof(vm.Ad)), h, nameof(vm.Ad)), Alan("Limit", Girdi(nameof(vm.Limit), true), h, nameof(vm.Limit)),
                Alan("Hesap kesim günü (1–31)", Girdi(nameof(vm.KesimGunu), sayi: true), h, nameof(vm.KesimGunu)),
                Alan("Son ödeme günü (1–31)", Girdi(nameof(vm.SonOdemeGunu), sayi: true), h, nameof(vm.SonOdemeGunu)),
                Goster(acilis, nameof(vm.YeniKart)), kaydet,
                Goster(durum, nameof(vm.KartSecili)), Goster(Devir(vm), nameof(vm.GecisKaydi), true),
            },
        };
    }

    private static View Devir(KartTakipViewModel vm)
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1136`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs Kasa.App.Core.Tests/GecersizTutarTests.cs Kasa.App.Core.Tests/KartFormuTests.cs Kasa.App.Core.Tests/KartTakipGorunumTests.cs Kasa.App.Core/KartTakipViewModel.Gorunum.cs Kasa.App.Core/KartTakipViewModel.cs Kasa.App/Views/KartTakipPage.cs
git commit -F - <<'MESAJ'
feat(app): Kartlar yeni kart ve düzenleme formunda alan hataları

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 16: Kartlar — ödeme akışı

Tek "Ödemeyi kontrol et"; alanlar eksikse önce alan hatası, tamsa önizleme ve "Onayla ve kaydet"; girdi değişince onay kalkar (KR-01). Form kapandıktan sonra gelen kayıt hatası sayfa başına yazılır.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/KartTakipViewModel.Gorunum.cs`
- Değiştir: `Kasa.App.Core/KartTakipViewModel.cs`
- Değiştir: `Kasa.App/Views/KartTakipPage.cs`
- Test (değiştir): `Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/FinansTakipTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/GecersizTutarTests.cs`
- Test (oluştur): `Kasa.App.Core.Tests/KartOdemeAkisiTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/KartTakipGorunumTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/OnizlemeOnayTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs` (1/2) — Bul:

```csharp
        vm.AcilisTarihi = new DateTime(2026, 9, 1);
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.KartHatalari.Genel);
        Assert.Equal(3, vm.Secili!.Surum);
        await vm.KaydetCommand.ExecuteAsync(null);
        var duzenleme = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
```

Yerine:

```csharp
        vm.AcilisTarihi = new DateTime(2026, 9, 1);
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Contains("ulaşılamadı", vm.SayfaHatasi);   // form açık değilken kayıt hatası sayfa başında
        Assert.Equal(3, vm.Secili!.Surum);
        await vm.KaydetCommand.ExecuteAsync(null);
        var duzenleme = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
```

`Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs` (2/2) — Bul:

```csharp
        vm.AcilisPaylari[0].Tutar = 50;
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.KartHatalari.Genel);
        Assert.Null(vm.Secili);
        await vm.KaydetCommand.ExecuteAsync(null);
        var yeni = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
```

Yerine:

```csharp
        vm.AcilisPaylari[0].Tutar = 50;
        api.SonrakiHata = new HttpRequestException();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Contains("ulaşılamadı", vm.SayfaHatasi);   // form açık değilken kayıt hatası sayfa başında
        Assert.Null(vm.Secili);
        await vm.KaydetCommand.ExecuteAsync(null);
        var yeni = api.Hepsi<KartTakipYaz>(nameof(IFinansTakipApi.TakipKartKaydetAsync));
```

`Kasa.App.Core.Tests/FinansTakipTests.cs` — Bul:

```csharp
        Assert.Contains("MEZAT", vm.OdemeOnizleme);
        Assert.NotEqual(Guid.Empty, api.OnizlenenOdeme!.IstekId);
        vm.OdemeTutari = 11;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.OdemeIstekleri);
        Assert.Contains("önizlemeyi", vm.Hata);
    }
    [Fact]
    public async Task Kart_odeme_ag_hatasinda_onizlemedeki_ayni_anahtarla_tekrarlanir()
```

Yerine:

```csharp
        Assert.Contains("MEZAT", vm.OdemeOnizleme);
        Assert.NotEqual(Guid.Empty, api.OnizlenenOdeme!.IstekId);
        vm.OdemeTutari = 11;
        Assert.False(vm.OdemeOnizlemeGuncel);
        Assert.False(vm.OdemeKaydetCommand.CanExecute(null));
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.OdemeIstekleri);
        Assert.Contains("önizlemeyi", vm.OdemeHatalari.Genel);
    }
    [Fact]
    public async Task Kart_odeme_ag_hatasinda_onizlemedeki_ayni_anahtarla_tekrarlanir()
```

`Kasa.App.Core.Tests/GecersizTutarTests.cs` — Bul:

```csharp
        vm.OdemeTutari = G;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        Assert.Null(api.OnizlenenOdeme);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        vm.AsgariVar = true;
```

Yerine:

```csharp
        vm.OdemeTutari = G;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        Assert.Null(api.OnizlenenOdeme);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.OdemeHatalari[nameof(vm.OdemeTutari)]);

        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        vm.AsgariVar = true;
```

`Kasa.App.Core.Tests/KartOdemeAkisiTests.cs` (yeni dosya):

```csharp
namespace Kasa.App.Core.Tests;

/// <summary>Kart ödemesi akışı (tasarım 2026-10-02 §4; KR-01): tek "Ödemeyi kontrol et" önce eksik alanı söyler, sonra önizler;
/// "Onayla ve kaydet" yalnız güncel önizlemede çalışır, girdi değişince kalkar.</summary>
public class KartOdemeAkisiTests
{
    private static async Task<(KartTakipViewModel Vm, FinansTakipTests.Sahte Api)> Kur()
    {
        var api = new FinansTakipTests.Sahte();
        var vm = new KartTakipViewModel(api, new SahteApi { KanallarListe = [new Kasa.ApiClient.KanalDto(1, "MEZAT", true, 0, 0)] },
            new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        await vm.KutuSecCommand.ExecuteAsync(vm.Kartlar[0]);
        vm.FormAcCommand.Execute(KartFormu.Odeme);
        return (vm, api);
    }

    [Fact]
    public async Task Bos_tutarla_kontrol_once_alani_soyler_onizleme_istemez()
    {
        var (vm, api) = await Kur();

        await vm.OdemeOnizleCommand.ExecuteAsync(null);

        Assert.Null(api.OnizlenenOdeme);
        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.OdemeHatalari[nameof(vm.OdemeTutari)]);
        Assert.False(vm.OdemeOnizlemeGuncel);
        Assert.False(vm.OdemeKaydetCommand.CanExecute(null));
        Assert.Null(vm.OdemeOnizleme);
    }

    [Fact]
    public async Task Onizleme_sonrasi_girdi_degisince_onay_kalkar_yeniden_kontrol_ister()
    {
        var (vm, api) = await Kur();
        vm.OdemeTutari = 40m;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        Assert.True(vm.OdemeOnizlemeGuncel);
        Assert.True(vm.OdemeKaydetCommand.CanExecute(null));
        Assert.Contains("MEZAT", vm.OdemeOnizleme);

        vm.OdemeTarihi = vm.OdemeTarihi.AddDays(-1);
        Assert.False(vm.OdemeOnizlemeGuncel);
        Assert.Null(vm.OdemeOnizleme);

        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        vm.OdemeNotu = "Dekont 12";
        Assert.False(vm.OdemeKaydetCommand.CanExecute(null));

        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        vm.OdemeEkstresi = vm.Ekstreler[0];
        Assert.False(vm.OdemeOnizlemeGuncel);

        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(40m, Assert.Single(api.OdemeIstekleri).Tutar);
        Assert.False(vm.OdemeOnizlemeGuncel);
        Assert.Equal(KartFormu.Yok, vm.AcikForm);
    }
}
```

`Kasa.App.Core.Tests/KartTakipGorunumTests.cs` — Bul:

```csharp
        api.OdemeHata = true;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
        Assert.NotNull(vm.FormHatasi);
        Assert.Null(vm.SayfaHatasi);
    }
```

Yerine:

```csharp
        api.OdemeHata = true;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(KartFormu.Odeme, vm.AcikForm);
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.OdemeHatalari.Genel);
        Assert.Null(vm.SayfaHatasi);
    }
```

`Kasa.App.Core.Tests/OnizlemeOnayTests.cs` — Bul:

```csharp

        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.OdemeIstekleri);
        Assert.Equal("Ödeme bilgileri için önce güncel önizlemeyi alın.", vm.Hata);
    }

    [Fact]
```

Yerine:

```csharp

        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.OdemeIstekleri);
        Assert.Equal("Önce “Ödemeyi kontrol et” ile güncel önizlemeyi alın.", vm.OdemeHatalari.Genel);
    }

    [Fact]
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KartOdemeAkisiTests|FullyQualifiedName~FinansTakipTests|FullyQualifiedName~OnizlemeOnayTests|FullyQualifiedName~KartTakipGorunumTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 7 farklı ileti; örnekler:

- `FinansTakipTests.cs: CS1061: 'KartTakipViewModel' bir 'OdemeOnizlemeGuncel' tanımı içermiyor ve 'KartTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'OdemeOnizlemeGuncel' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `FinansTakipTests.cs: CS1061: 'KartTakipViewModel' bir 'OdemeHatalari' tanımı içermiyor ve 'KartTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'OdemeHatalari' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `KartOdemeAkisiTests.cs: CS1061: 'KartTakipViewModel' bir 'OdemeHatalari' tanımı içermiyor ve 'KartTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'OdemeHatalari' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `KartOdemeAkisiTests.cs: CS1061: 'KartTakipViewModel' bir 'OdemeOnizlemeGuncel' tanımı içermiyor ve 'KartTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'OdemeOnizlemeGuncel' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/KartTakipViewModel.Gorunum.cs` (1/2) — Bul:

```csharp
    /// <summary>Kart bilgileri formunun (yeni kart ve "Kartı düzenle") hataları (tasarım 2026-10-02 §1; KR-04). Sunucu kart hatalarını
    /// alan adı olmadan ({ hata }) döndürür: sunucu iletisi genel hataya gider.</summary>
    public AlanHatalari KartHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [KartHatalari];

    /// <summary>Kart bilgileri formunun başlığı: yeni kartta "Yeni kart", düzenlemede "Düzenleniyor: Bonus".</summary>
    public string KartFormuBasligi => Secili is { } kart ? $"Düzenleniyor: {kart.Ad}" : "Yeni kart";
```

Yerine:

```csharp
    /// <summary>Kart bilgileri formunun (yeni kart ve "Kartı düzenle") hataları (tasarım 2026-10-02 §1; KR-04). Sunucu kart hatalarını
    /// alan adı olmadan ({ hata }) döndürür: sunucu iletisi genel hataya gider.</summary>
    public AlanHatalari KartHatalari { get; } = new();
    /// <summary>Kart ödemesi formunun hataları (KR-01): eksik alan önce alanın altında, sunucu iletisi genel hatada.</summary>
    public AlanHatalari OdemeHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [KartHatalari, OdemeHatalari];

    /// <summary>Kart bilgileri formunun başlığı: yeni kartta "Yeni kart", düzenlemede "Düzenleniyor: Bonus".</summary>
    public string KartFormuBasligi => Secili is { } kart ? $"Düzenleniyor: {kart.Ad}" : "Yeni kart";
```

`Kasa.App.Core/KartTakipViewModel.Gorunum.cs` (2/2) — Bul:

```csharp
    partial void OnAcikFormChanged(KartFormu oldValue, KartFormu newValue)
    {
        KartHatalari.Temizle();
        if (newValue == KartFormu.Yok)
            FormIzi.Kapat();
        else
```

Yerine:

```csharp
    partial void OnAcikFormChanged(KartFormu oldValue, KartFormu newValue)
    {
        KartHatalari.Temizle();
        OdemeHatalari.Temizle();
        if (newValue == KartFormu.Yok)
            FormIzi.Kapat();
        else
```

`Kasa.App.Core/KartTakipViewModel.cs` (1/6) — Bul:

```csharp
    [ObservableProperty] private EkstreSatiri? _odemeEkstresi;
    [ObservableProperty] private string _odemeNotu = "";
    [ObservableProperty] private string? _odemeOnizleme;
    [ObservableProperty] private EkstreSatiri? _duzenlenenEkstre;
    [ObservableProperty] private DateTime _ekstreSonOdeme = DateTime.Today;
    [ObservableProperty] private bool _asgariVar;
```

Yerine:

```csharp
    [ObservableProperty] private EkstreSatiri? _odemeEkstresi;
    [ObservableProperty] private string _odemeNotu = "";
    [ObservableProperty] private string? _odemeOnizleme;
    /// <summary>Gösterilen ödeme önizlemesi güncel girdiye ait: "Onayla ve kaydet" yalnız bu doğruyken görünür ve çalışır.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OdemeKaydetCommand))]
    private bool _odemeOnizlemeGuncel;
    [ObservableProperty] private EkstreSatiri? _duzenlenenEkstre;
    [ObservableProperty] private DateTime _ekstreSonOdeme = DateTime.Today;
    [ObservableProperty] private bool _asgariVar;
```

`Kasa.App.Core/KartTakipViewModel.cs` (2/6) — Bul:

```csharp
        GecisKalanBorc = Secili.Borc;
        OncedenSayilanOner(Math.Max(0, Secili.Borc));
        GecisDurumu(null, null);
        _odemeOnizlemesi.Temizle();
        OdemeOnizleme = null;
        OdemeEkstresi = null;
        DuzenlenenEkstre = null;
        HarcamaPaylari.Clear();
```

Yerine:

```csharp
        GecisKalanBorc = Secili.Borc;
        OncedenSayilanOner(Math.Max(0, Secili.Borc));
        GecisDurumu(null, null);
        OdemeOnizlemesiniKaldir();
        OdemeEkstresi = null;
        DuzenlenenEkstre = null;
        HarcamaPaylari.Clear();
```

`Kasa.App.Core/KartTakipViewModel.cs` (3/6) — Bul:

```csharp
        IadeKaynaklari.Clear();
        IadeKaynagi = null;
        Odemeler.Clear();
        OdemeOnizleme = null;
        _odemeOnizlemesi.Temizle();
        DuzenlenenEkstre = null;
        GecisDurumu(null, null);
        DevirTemizle();
```

Yerine:

```csharp
        IadeKaynaklari.Clear();
        IadeKaynagi = null;
        Odemeler.Clear();
        OdemeOnizlemesiniKaldir();
        DuzenlenenEkstre = null;
        GecisDurumu(null, null);
        DevirTemizle();
```

`Kasa.App.Core/KartTakipViewModel.cs` (4/6) — Bul:

```csharp
    }

    [RelayCommand]
    private Task KaydetAsync() => FormIsleAsync(KartHatalari, async n =>
    {
        // Açılış borcu, tarihi ve dağılımı yalnız yeni kartta girilir ve okunur (bölüm yalnız YeniKart iken görünür); sunucu
        // güncellemede bu alanları yok sayar. Mevcut kartta görünmeyen bir açılış satırı kaydı reddettirmez.
```

Yerine:

```csharp
    }

    [RelayCommand]
    private Task KaydetAsync() => KartFormIsleAsync(KartFormu.KartBilgisi, KartHatalari, async n =>
    {
        // Açılış borcu, tarihi ve dağılımı yalnız yeni kartta girilir ve okunur (bölüm yalnız YeniKart iken görünür); sunucu
        // güncellemede bu alanları yok sayar. Mevcut kartta görünmeyen bir açılış satırı kaydı reddettirmez.
```

`Kasa.App.Core/KartTakipViewModel.cs` (5/6) — Bul:

```csharp
        if (Uygula(await api.TakipKartKaydetAsync(Secili?.Id, g), n))
        { _kayit.Temizle(); FormuKapat(KartFormu.KartBilgisi); Mesaj = "Kart kaydedildi."; }
    });
    [RelayCommand]
    private Task HarcamaKaydetAsync() => YurutAsync(KartFormu.Harcama, async n =>
    {
```

Yerine:

```csharp
        if (Uygula(await api.TakipKartKaydetAsync(Secili?.Id, g), n))
        { _kayit.Temizle(); FormuKapat(KartFormu.KartBilgisi); Mesaj = "Kart kaydedildi."; }
    });

    /// <summary>Kart formunun kaydı (tasarım 2026-10-02 §1): tekil işlem, başlarken formun hataları kalkar, bitince hata varsa ilk
    /// hatalı alana kaydırılır. Hata işlem sürerken form hâlâ açıksa formun içine, form kapandıysa ya da başka form açıldıysa sayfa
    /// başına yazılır (önceki form hata kaynağı kuralı).</summary>
    private Task KartFormIsleAsync(KartFormu kaynak, AlanHatalari form, Func<int, Task> islem) => YurutAsync(async n =>
    {
        HataKaynagi = kaynak;
        form.Temizle();
        await islem(n);
        if (Gecerli(n))
            form.GosterIste();
    }, hataIsle: hata =>
    {
        if (AcikForm != kaynak)
        {
            SayfaHatasiYaz(HataMesaji(hata));
            return;
        }
        FormHatasiniYaz(form, hata);
        form.GosterIste();
    });

    /// <summary>Gösterilen ödeme önizlemesi ve "Onayla ve kaydet" kalkar: önizlemeden sonra tutar, tarih, ekstre ya da not değişti
    /// (KR-01; önizleme yeniden istenir).</summary>
    private void OdemeOnizlemesiniKaldir()
    {
        _odemeOnizlemesi.Temizle();
        OdemeOnizleme = null;
        OdemeOnizlemeGuncel = false;
    }

    partial void OnOdemeTutariChanged(decimal value) => OdemeOnizlemesiniKaldir();
    partial void OnOdemeTarihiChanged(DateTime value) => OdemeOnizlemesiniKaldir();
    partial void OnOdemeEkstresiChanged(EkstreSatiri? value) => OdemeOnizlemesiniKaldir();
    partial void OnOdemeNotuChanged(string value) => OdemeOnizlemesiniKaldir();

    /// <summary>Ödeme formunun ön doğrulaması: kontrol ve kayıt aynı kuralı uygular.</summary>
    private bool OdemeFormuGecerli()
    {
        var h = OdemeHatalari;
        h.Denetle(ParaAyristirici.GecerliMi(OdemeTutari), nameof(OdemeTutari), ParaAyristirici.GecersizMesaji);
        h.Denetle(OdemeTutari > 0, nameof(OdemeTutari), "Tutar sıfırdan büyük olmalı.");
        return !h.Var;
    }
    [RelayCommand]
    private Task HarcamaKaydetAsync() => YurutAsync(KartFormu.Harcama, async n =>
    {
```

`Kasa.App.Core/KartTakipViewModel.cs` (6/6) — Bul:

```csharp
        var g = new KartTakipOdemeYaz(Guid.Empty, Secili!.Surum, DateOnly.FromDateTime(OdemeTarihi), OdemeTutari, OdemeEkstresi?.Veri.Id, OdemeNotu);
        return g with { IstekId = _odeme.Al(Secili.Id, new { Secili.Id, g }) };
    }
    [RelayCommand]
    private Task OdemeOnizleAsync() => YurutAsync(KartFormu.Odeme, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart)
            return;
        _odemeOnizlemesi.Temizle();
        OdemeOnizleme = null;
        if (!ParaAyristirici.GecerliMi(OdemeTutari))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (OdemeTutari <= 0)
        { Hata = "Pozitif ödeme tutarı girin."; return; }
        if (!await _odemeOnizlemesi.IsteAsync(OdemeGovde, g => api.TakipOdemeOnizlemeAsync(kart.Id, g), () => Gecerli(n) && Secili?.Id == kart.Id))
            return;
        var sonuc = _odemeOnizlemesi.Onizleme!;
        OdemeOnizleme = $"Kasa çıkışı: {Bicim.Tl(sonuc.KasaEtkisi)} ₺\n{TakipMetni.Paylar(sonuc.Dagilimlar)}\n" + string.Join(" · ", sonuc.Ekstreler.Select(e => $"Ekstre #{e.EkstreId}: {Bicim.Tl(e.Tutar)} ₺"));
    });
    [RelayCommand]
    private Task OdemeKaydetAsync() => YurutAsync(KartFormu.Odeme, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart)
            return;
        var g = OdemeGovde();
        if (!_odemeOnizlemesi.Gecerli(g))
        { Hata = "Ödeme bilgileri için önce güncel önizlemeyi alın."; return; }
        if (!await OdemeBenzerlik.DevamEdilebilirAsync(new(BenzerAramaTurleri.KartOdeme, g.Tarih, g.Tutar, kart.Id), new { kart.Id, g }, () => Gecerli(n) && Secili?.Id == kart.Id))
            return;
        if (Uygula(await api.TakipOdemeKaydetAsync(kart.Id, g), n))
        { _odeme.Temizle(kart.Id); OdemeBenzerlik.Temizle(); _odemeOnizlemesi.Temizle(); OdemeOnizleme = null; OdemeTutari = 0; OdemeNotu = ""; FormuKapat(KartFormu.Odeme); Mesaj = "Kart ödemesi kaydedildi; kasa etkisi bir kez işlendi."; }
    });
    [RelayCommand] private async Task OdemeyiAyriKaydetAsync() { if (OdemeBenzerlik.Onayla()) await OdemeKaydetAsync(); }
    [RelayCommand]
```

Yerine:

```csharp
        var g = new KartTakipOdemeYaz(Guid.Empty, Secili!.Surum, DateOnly.FromDateTime(OdemeTarihi), OdemeTutari, OdemeEkstresi?.Veri.Id, OdemeNotu);
        return g with { IstekId = _odeme.Al(Secili.Id, new { Secili.Id, g }) };
    }
    /// <summary>"Ödemeyi kontrol et" (KR-01): eksik alan önce alanın altında söylenir; alanlar tamsa kanal payları önizlenir ve
    /// "Onayla ve kaydet" belirir (<see cref="OdemeOnizlemeGuncel"/>).</summary>
    [RelayCommand]
    private Task OdemeOnizleAsync() => KartFormIsleAsync(KartFormu.Odeme, OdemeHatalari, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart)
            return;
        OdemeOnizlemesiniKaldir();
        if (!OdemeFormuGecerli())
            return;
        if (!await _odemeOnizlemesi.IsteAsync(OdemeGovde, g => api.TakipOdemeOnizlemeAsync(kart.Id, g), () => Gecerli(n) && Secili?.Id == kart.Id))
            return;
        var sonuc = _odemeOnizlemesi.Onizleme!;
        OdemeOnizleme = $"Kasa çıkışı: {Bicim.Tl(sonuc.KasaEtkisi)} ₺\n{TakipMetni.Paylar(sonuc.Dagilimlar)}\n" + string.Join(" · ", sonuc.Ekstreler.Select(e => $"Ekstre #{e.EkstreId}: {Bicim.Tl(e.Tutar)} ₺"));
        OdemeOnizlemeGuncel = true;
    });

    /// <summary>"Onayla ve kaydet": yalnız güncel önizleme varken çalışır (önizleme bayatsa düğme kapalıdır; sunucu kuralı aynıdır).</summary>
    [RelayCommand(CanExecute = nameof(OdemeOnizlemeGuncel))]
    private Task OdemeKaydetAsync() => KartFormIsleAsync(KartFormu.Odeme, OdemeHatalari, async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart || !OdemeFormuGecerli())
            return;
        var g = OdemeGovde();
        if (!_odemeOnizlemesi.Gecerli(g))
        { OdemeHatalari.Genel = "Önce “Ödemeyi kontrol et” ile güncel önizlemeyi alın."; return; }
        if (!await OdemeBenzerlik.DevamEdilebilirAsync(new(BenzerAramaTurleri.KartOdeme, g.Tarih, g.Tutar, kart.Id), new { kart.Id, g }, () => Gecerli(n) && Secili?.Id == kart.Id))
            return;
        if (Uygula(await api.TakipOdemeKaydetAsync(kart.Id, g), n))
        { _odeme.Temizle(kart.Id); OdemeBenzerlik.Temizle(); OdemeOnizlemesiniKaldir(); OdemeTutari = 0; OdemeNotu = ""; FormuKapat(KartFormu.Odeme); Mesaj = "Kart ödemesi kaydedildi; kasa etkisi bir kez işlendi."; }
    });
    [RelayCommand] private async Task OdemeyiAyriKaydetAsync() { if (OdemeBenzerlik.Onayla()) await OdemeKaydetAsync(); }
    [RelayCommand]
```

`Kasa.App/Views/KartTakipPage.cs` (1/5) — Bul:

```csharp
    private readonly View _formAlani;
    /// <summary>Formun tepesindeki hata satırı (FormHatasi): uzun formun altındaki düğmeden gelen hata görünür yere kaydırılır.</summary>
    private readonly Label _formHataSatiri;
    /// <summary>Kart bilgileri formunun genel hata kutusu (KartHatalari.Genel; tasarım 2026-10-02 §1).</summary>
    private readonly View _kartHataKutusu;

    /// <summary>Kart bildirimine tıklanınca //kartlar?KartId={id}: sayfa zaten açıkken istek hemen, değilse sayfa belirirken uygulanır
    /// (SorguSecimi).</summary>
```

Yerine:

```csharp
    private readonly View _formAlani;
    /// <summary>Formun tepesindeki hata satırı (FormHatasi): uzun formun altındaki düğmeden gelen hata görünür yere kaydırılır.</summary>
    private readonly Label _formHataSatiri;
    /// <summary>Kart bilgileri ve ödeme formlarının genel hata kutuları (KartHatalari.Genel, OdemeHatalari.Genel; tasarım 2026-10-02 §1).</summary>
    private readonly View _kartHataKutusu, _odemeHataKutusu;

    /// <summary>Kart bildirimine tıklanınca //kartlar?KartId={id}: sayfa zaten açıkken istek hemen, değilse sayfa belirirken uygulanır
    /// (SorguSecimi).</summary>
```

`Kasa.App/Views/KartTakipPage.cs` (2/5) — Bul:

```csharp
    {
        _formHataSatiri = BagliHata(nameof(vm.FormHatasi));
        _kartHataKutusu = FormHatasi(nameof(vm.KartHatalari) + ".Genel");
        _formAlani = FormAlani(vm);
        _ayrinti = new Border
        {
```

Yerine:

```csharp
    {
        _formHataSatiri = BagliHata(nameof(vm.FormHatasi));
        _kartHataKutusu = FormHatasi(nameof(vm.KartHatalari) + ".Genel");
        _odemeHataKutusu = FormHatasi(nameof(vm.OdemeHatalari) + ".Genel");
        _formAlani = FormAlani(vm);
        _ayrinti = new Border
        {
```

`Kasa.App/Views/KartTakipPage.cs` (3/5) — Bul:

```csharp
        };
        // Kaydetme başarısız olunca ilk hatalı alana (yoksa formun genel hata kutusuna) kaydırılır ve odaklanılır (tasarım §1).
        vm.KartHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(_formAlani, vm.KartHatalari, _kartHataKutusu);
    }

    /// <summary>Ayrıntı: üstü görünür alandaysa kaydırılmaz (kutular görünür kalır), değilse üstü görünür alanın başına gelir.</summary>
```

Yerine:

```csharp
        };
        // Kaydetme başarısız olunca ilk hatalı alana (yoksa formun genel hata kutusuna) kaydırılır ve odaklanılır (tasarım §1).
        vm.KartHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(_formAlani, vm.KartHatalari, _kartHataKutusu);
        vm.OdemeHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(_formAlani, vm.OdemeHatalari, _odemeHataKutusu);
    }

    /// <summary>Ayrıntı: üstü görünür alandaysa kaydırılmaz (kutular görünür kalır), değilse üstü görünür alanın başına gelir.</summary>
```

`Kasa.App/Views/KartTakipPage.cs` (4/5) — Bul:

```csharp
                new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] },
                Goster(_formHataSatiri, nameof(vm.FormHatasi), true),
                Durumda(KartBilgileri(vm), nameof(vm.AcikForm), KartFormu.KartBilgisi),
                Durumda(Odeme(vm), nameof(vm.AcikForm), KartFormu.Odeme),
                Durumda(Harcama(vm), nameof(vm.AcikForm), KartFormu.Harcama),
                Durumda(Masraf(vm), nameof(vm.AcikForm), KartFormu.Masraf),
                Durumda(Ekstre(vm), nameof(vm.AcikForm), KartFormu.Ekstre),
```

Yerine:

```csharp
                new BoxView { Style = (Style)Application.Current!.Resources["TakipAyirici"] },
                Goster(_formHataSatiri, nameof(vm.FormHatasi), true),
                Durumda(KartBilgileri(vm), nameof(vm.AcikForm), KartFormu.KartBilgisi),
                Durumda(Odeme(vm, _odemeHataKutusu), nameof(vm.AcikForm), KartFormu.Odeme),
                Durumda(Harcama(vm), nameof(vm.AcikForm), KartFormu.Harcama),
                Durumda(Masraf(vm), nameof(vm.AcikForm), KartFormu.Masraf),
                Durumda(Ekstre(vm), nameof(vm.AcikForm), KartFormu.Ekstre),
```

`Kasa.App/Views/KartTakipPage.cs` (5/5) — Bul:

```csharp
            Bagli(nameof(vm.DevirOzeti)), Goster(devirFormu, nameof(vm.DevirDuzeltilebilir)));
    }

    private static View Odeme(KartTakipViewModel vm) => Form("Kart ödemesi kaydet",
        Alan("Tarih", Tarih(nameof(vm.OdemeTarihi))), Alan("Tutar", Girdi(nameof(vm.OdemeTutari), true)),
        Alan("Ekstre (boş: en eski açık ekstreler)", Secim(nameof(vm.Ekstreler), nameof(vm.OdemeEkstresi))),
        Tikla("En eski açık ekstrelere dağıt", () =>
        {
            vm.OdemeEkstresi = null;
            return Task.CompletedTask;
        }),
        Alan("Ödeme notu / dekont referansı", Girdi(nameof(vm.OdemeNotu))),
        Dugme("Ödeme ve kanal paylarını göster", nameof(vm.OdemeOnizleCommand)), Bagli(nameof(vm.OdemeOnizleme)),
        Dugme("Ödemeyi kaydet", nameof(vm.OdemeKaydetCommand)), Benzerlik(nameof(vm.OdemeBenzerlik), nameof(vm.OdemeyiAyriKaydetCommand)));

    private static View Harcama(KartTakipViewModel vm) => Form("Bağımsız kart hareketi",
        Metin(HarcamaNotu),
```

Yerine:

```csharp
            Bagli(nameof(vm.DevirOzeti)), Goster(devirFormu, nameof(vm.DevirDuzeltilebilir)));
    }

    /// <summary>Kart ödemesi (KR-01): tek "Ödemeyi kontrol et" düğmesi önce eksik alanı söyler, alanlar tamsa kanal paylarını gösterir
    /// ve "Onayla ve kaydet" belirir; önizlemeden sonra tutar, tarih, ekstre ya da not değişirse "Onayla ve kaydet" kalkar.</summary>
    private static View Odeme(KartTakipViewModel vm, View hataKutusu)
    {
        const string h = nameof(vm.OdemeHatalari);
        return Form("Kart ödemesi kaydet", hataKutusu,
            Alan("Tarih", Tarih(nameof(vm.OdemeTarihi)), h, nameof(vm.OdemeTarihi)),
            Alan("Tutar", Girdi(nameof(vm.OdemeTutari), true), h, nameof(vm.OdemeTutari)),
            Alan("Ekstre (boş: en eski açık ekstreler)", Secim(nameof(vm.Ekstreler), nameof(vm.OdemeEkstresi))),
            Tikla("En eski açık ekstrelere dağıt", () =>
            {
                vm.OdemeEkstresi = null;
                return Task.CompletedTask;
            }),
            Alan("Ödeme notu / dekont referansı", Girdi(nameof(vm.OdemeNotu)), h, nameof(vm.OdemeNotu)),
            Dugme("Ödemeyi kontrol et", nameof(vm.OdemeOnizleCommand)), Bagli(nameof(vm.OdemeOnizleme)),
            Goster(Dugme("Onayla ve kaydet", nameof(vm.OdemeKaydetCommand)), nameof(vm.OdemeOnizlemeGuncel)),
            Benzerlik(nameof(vm.OdemeBenzerlik), nameof(vm.OdemeyiAyriKaydetCommand)));
    }

    private static View Harcama(KartTakipViewModel vm) => Form("Bağımsız kart hareketi",
        Metin(HarcamaNotu),
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1138`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/Donusturuculer/TakipKomutlariTests.cs Kasa.App.Core.Tests/FinansTakipTests.cs Kasa.App.Core.Tests/GecersizTutarTests.cs Kasa.App.Core.Tests/KartOdemeAkisiTests.cs Kasa.App.Core.Tests/KartTakipGorunumTests.cs Kasa.App.Core.Tests/OnizlemeOnayTests.cs Kasa.App.Core/KartTakipViewModel.Gorunum.cs Kasa.App.Core/KartTakipViewModel.cs Kasa.App/Views/KartTakipPage.cs
git commit -F - <<'MESAJ'
feat(app): kart ödemesinde tek kontrol düğmesi ve güncel önizleme

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 17: Krediler — yeni kredi formu

"Yeni kredi" formunda alan hataları; yazılmış form krediye geçmeden ve "Yeni"den önce onay ister.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/KrediTakipViewModel.cs`
- Değiştir: `Kasa.App/Views/KrediTakipPage.cs`
- Test (değiştir): `Kasa.App.Core.Tests/GecersizTutarTests.cs`
- Test (oluştur): `Kasa.App.Core.Tests/KrediFormuTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/GecersizTutarTests.cs` — Bul:

```csharp
        vm.TumKanallariSecCommand.Execute(null);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.KrediKayit);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        vm.SecCommand.Execute(vm.Krediler[0]);
        vm.Gerekce = "Banka yazısı";
        vm.KapatmaTutari = G;
```

Yerine:

```csharp
        vm.TumKanallariSecCommand.Execute(null);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.KrediKayit);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hatalar[nameof(vm.AylikOdeme)]);

        vm.BirakmaOnayi = _ => Task.FromResult(true);   // yazılmış yeni kredi formu bırakılır
        vm.SecCommand.Execute(vm.Krediler[0]);
        vm.Gerekce = "Banka yazısı";
        vm.KapatmaTutari = G;
```

`Kasa.App.Core.Tests/KrediFormuTests.cs` (yeni dosya):

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Krediler "Yeni kredi" formu (tasarım 2026-10-02 §1-2): alan hataları, sunucu iletisi genel hatada, yazılmış formdan
/// krediye geçişte ve "Yeni"de onay.</summary>
public class KrediFormuTests
{
    private static async Task<(KrediTakipViewModel Vm, FinansTakipTests.Sahte Api)> Kur()
    {
        var api = new FinansTakipTests.Sahte();
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new KrediTakipViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Bos_form_alanlari_ayri_ayri_soyler_istek_gitmez()
    {
        var (vm, api) = await Kur();
        vm.TaksitSayisi = 0;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.KrediKayit);
        Assert.Equal("Banka / kredi adı boş olamaz.", vm.Hatalar[nameof(vm.Ad)]);
        Assert.Equal("Çekilen tutar sıfırdan büyük olmalı.", vm.Hatalar[nameof(vm.CekilenTutar)]);
        Assert.Equal("Taksit sayısı 1 ile 600 arasında olmalı.", vm.Hatalar[nameof(vm.TaksitSayisi)]);
        Assert.Equal("Aylık taksit tutarı sıfırdan büyük olmalı.", vm.Hatalar[nameof(vm.AylikOdeme)]);
        Assert.Equal("En az bir kanal seçin.", vm.Hatalar[nameof(vm.Kanallar)]);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Yazilmis_yeni_kredi_formu_krediye_gecmeden_ve_yeniden_once_onay_ister()
    {
        var (vm, _) = await Kur();
        Assert.False(vm.KaydedilmemisDegisiklikVar);
        vm.Ad = "Ziraat";
        Assert.True(vm.KaydedilmemisDegisiklikVar);

        var cevap = false;
        vm.BirakmaOnayi = _ => Task.FromResult(cevap);
        await vm.SecCommand.ExecuteAsync(vm.Krediler[0]);
        Assert.Null(vm.Secili);
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal("Ziraat", vm.Ad);

        cevap = true;
        await vm.SecCommand.ExecuteAsync(vm.Krediler[0]);
        Assert.NotNull(vm.Secili);
        Assert.False(vm.KaydedilmemisDegisiklikVar);   // kredi seçiliyken yeni kredi formu kapalıdır

        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal(("", false), (vm.Ad, vm.KaydedilmemisDegisiklikVar));
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KrediFormuTests|FullyQualifiedName~GecersizTutarTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 5 farklı ileti; örnekler:

- `GecersizTutarTests.cs: CS1061: 'KrediTakipViewModel' bir 'Hatalar' tanımı içermiyor ve 'KrediTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'Hatalar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `KrediFormuTests.cs: CS1061: 'KrediTakipViewModel' bir 'Hatalar' tanımı içermiyor ve 'KrediTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'Hatalar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `KrediFormuTests.cs: CS1061: 'KrediTakipViewModel' bir 'KaydedilmemisDegisiklikVar' tanımı içermiyor ve 'KrediTakipViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KaydedilmemisDegisiklikVar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `KrediFormuTests.cs: CS1061: 'IRelayCommand<KrediTakipSatiri>' bir 'ExecuteAsync' tanımı içermiyor ve 'IRelayCommand<KrediTakipSatiri>' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'ExecuteAsync' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/KrediTakipViewModel.cs` (1/7) — Bul:

```csharp

namespace Kasa.App.Core;

public partial class KrediTakipViewModel(IFinansTakipApi api, IKasaApi finans, AuthViewModel auth) : OturumluViewModel(auth)
{
    private readonly TekrarAnahtari _kayit = new(), _taksit = new(), _kapat = new(), _durum = new(), _gecis = new();
    private readonly OnizlemeOnay<KrediGecisYaz, TakipGecisDto> _gecisOnizlemesi = new();
    public ObservableCollection<KrediTakipSatiri> Krediler { get; } = new();
```

Yerine:

```csharp

namespace Kasa.App.Core;

public partial class KrediTakipViewModel(IFinansTakipApi api, IKasaApi finans, AuthViewModel auth) : OturumluViewModel(auth), IKaydedilmemisForm
{
    /// <summary>"Yeni kredi" formunun hataları (tasarım 2026-10-02 §1). Sunucu kredi hatalarını alan adı olmadan ({ hata }) döndürür:
    /// sunucu iletisi genel hataya gider.</summary>
    public AlanHatalari Hatalar { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [Hatalar];

    /// <summary>Yeni kredi formunun açıldığı andaki değerleri (tasarım §2); kredi seçiliyken form kapalıdır, değişiklik sayılmaz.</summary>
    private KaydedilmemisDegisiklik? _formIzi;
    private KaydedilmemisDegisiklik FormIzi => _formIzi ??= new(() => new
    {
        Ad,
        CekilenTutar,
        CekimTarihi,
        IlkTaksitTarihi,
        TaksitSayisi,
        AylikOdeme,
        MevcutKredi,
        Kanallar = Kanallar.Where(k => k.Secili).Select(k => k.Veri.Id).ToList(),
    });
    public bool KaydedilmemisDegisiklikVar => Secili is null && FormIzi.Var;

    /// <summary>Kabuktan çıkışta "Bırak": yeni kredi formu boşalır.</summary>
    public void DegisiklikleriBirak()
    {
        if (Secili is null)
            YeniForm();
    }

    private readonly TekrarAnahtari _kayit = new(), _taksit = new(), _kapat = new(), _durum = new(), _gecis = new();
    private readonly OnizlemeOnay<KrediGecisYaz, TakipGecisDto> _gecisOnizlemesi = new();
    public ObservableCollection<KrediTakipSatiri> Krediler { get; } = new();
```

`Kasa.App.Core/KrediTakipViewModel.cs` (2/7) — Bul:

```csharp
        TakipMetni.Doldur(Kanallar, kanallar.Select(k => new TakipKanalSecimi(k)));
        TakipMetni.Doldur(Krediler, krediler.Select(k => new KrediTakipSatiri(k)));
        if (Secili is { } eski)
        { var mevcut = krediler.FirstOrDefault(k => k.Id == eski.Id); if (mevcut is not null) Sec(new(mevcut)); else Yeni(); }
        Tamamlandi();
    });
    /// <summary>Kimliği verilen krediyi seçer (bildirim tıklaması: //krediler?KrediId=…); kredi listede yoksa sayfa hatası yazılır.
```

Yerine:

```csharp
        TakipMetni.Doldur(Kanallar, kanallar.Select(k => new TakipKanalSecimi(k)));
        TakipMetni.Doldur(Krediler, krediler.Select(k => new KrediTakipSatiri(k)));
        if (Secili is { } eski)
        { var mevcut = krediler.FirstOrDefault(k => k.Id == eski.Id); if (mevcut is not null) KrediyiAc(new(mevcut)); else YeniForm(); }
        else
            FormIzi.Ac();   // yeni kredi formu yüklenen kanal seçimleriyle açıldı
        Tamamlandi();
    });
    /// <summary>Kimliği verilen krediyi seçer (bildirim tıklaması: //krediler?KrediId=…); kredi listede yoksa sayfa hatası yazılır.
```

`Kasa.App.Core/KrediTakipViewModel.cs` (3/7) — Bul:

```csharp
            Hata = "Kredi bulunamadı. Listeyi yenileyip tekrar deneyin.";
            return false;
        }
        Sec(satir);
        return true;
    }

    [RelayCommand]
    private void Sec(KrediTakipSatiri satir)
    {
        Secili = satir.Veri;
        TakipMetni.Doldur(Taksitler, Secili.Taksitler.OrderBy(t => t.Tarih).ThenBy(t => t.No).Select(t => new TaksitSatiri(t)));
        foreach (var k in Kanallar)
```

Yerine:

```csharp
            Hata = "Kredi bulunamadı. Listeyi yenileyip tekrar deneyin.";
            return false;
        }
        KrediyiAc(satir);
        return true;
    }

    /// <summary>Listeden kredi seçimi: yeni kredi formu yazılmışsa önce onay sorulur (tasarım §2).</summary>
    [RelayCommand]
    private async Task SecAsync(KrediTakipSatiri satir)
    {
        if (await BirakilabilirAsync(FormIzi) || Secili is not null)
            KrediyiAc(satir);
    }

    private void KrediyiAc(KrediTakipSatiri satir)
    {
        Hatalar.Temizle();
        Secili = satir.Veri;
        TakipMetni.Doldur(Taksitler, Secili.Taksitler.OrderBy(t => t.Tarih).ThenBy(t => t.No).Select(t => new TaksitSatiri(t)));
        foreach (var k in Kanallar)
```

`Kasa.App.Core/KrediTakipViewModel.cs` (4/7) — Bul:

```csharp
        _gecisOnizlemesi.Temizle();
        Gerekce = "";
    }
    [RelayCommand]
    private void Yeni()
    {
        Secili = null;
        Ad = "";
```

Yerine:

```csharp
        _gecisOnizlemesi.Temizle();
        Gerekce = "";
    }
    /// <summary>"Yeni kredi / mevcut krediyi ekle": yazılmış yeni kredi formu varsa önce onay sorulur.</summary>
    [RelayCommand]
    private async Task YeniAsync()
    {
        if (Secili is not null || await BirakilabilirAsync(FormIzi))
            YeniForm();
    }

    /// <summary>Boş yeni kredi formu; formun hataları kalkar.</summary>
    internal void YeniForm()
    {
        Secili = null;
        Ad = "";
```

`Kasa.App.Core/KrediTakipViewModel.cs` (5/7) — Bul:

```csharp
        GecisOnizleme = null;
        GecisOnay = KapatmaOnay = false;
        _gecisOnizlemesi.Temizle();
    }
    [RelayCommand] private void TumKanallariSec() { foreach (var k in Kanallar) k.Secili = k.Veri.Aktif; }
    private IReadOnlyList<int> SecilenKanallar() => Kanallar.Where(k => k.Secili).Select(k => k.Veri.Id).ToList();
```

Yerine:

```csharp
        GecisOnizleme = null;
        GecisOnay = KapatmaOnay = false;
        _gecisOnizlemesi.Temizle();
        Hatalar.Temizle();
        FormIzi.Ac();
    }
    [RelayCommand] private void TumKanallariSec() { foreach (var k in Kanallar) k.Secili = k.Veri.Aktif; }
    private IReadOnlyList<int> SecilenKanallar() => Kanallar.Where(k => k.Secili).Select(k => k.Veri.Id).ToList();
```

`Kasa.App.Core/KrediTakipViewModel.cs` (6/7) — Bul:

```csharp
        Tamamlandi();
        return true;
    }
    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not null)
            return;
        var kanallar = SecilenKanallar();
        if (!ParaAyristirici.HepsiGecerli(CekilenTutar, AylikOdeme))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (string.IsNullOrWhiteSpace(Ad) || CekilenTutar <= 0 || AylikOdeme <= 0 || TaksitSayisi is < 1 or > 600 || kanallar.Count == 0)
        { Hata = "Kredi adı, pozitif tutarlar, taksit sayısı ve en az bir kanal seçin."; return; }
        var g = new KrediTakipYaz(Guid.Empty, Ad.Trim(), CekilenTutar, DateOnly.FromDateTime(CekimTarihi), DateOnly.FromDateTime(IlkTaksitTarihi), TaksitSayisi, AylikOdeme, kanallar, MevcutKredi);
        g = g with { IstekId = _kayit.Al(g) };
        if (Uygula(await api.TakipKrediKaydetAsync(g), n))
```

Yerine:

```csharp
        Tamamlandi();
        return true;
    }
    /// <summary>Yeni kredi formunun ön doğrulaması (tasarım §1): her kural kendi alanının altında.</summary>
    private bool KrediFormuGecerli(IReadOnlyList<int> kanallar)
    {
        var h = Hatalar;
        h.Denetle(!string.IsNullOrWhiteSpace(Ad), nameof(Ad), "Banka / kredi adı boş olamaz.");
        h.Denetle(ParaAyristirici.GecerliMi(CekilenTutar), nameof(CekilenTutar), ParaAyristirici.GecersizMesaji);
        h.Denetle(CekilenTutar > 0, nameof(CekilenTutar), "Çekilen tutar sıfırdan büyük olmalı.");
        h.Denetle(TaksitSayisi is >= 1 and <= 600, nameof(TaksitSayisi), "Taksit sayısı 1 ile 600 arasında olmalı.");
        h.Denetle(ParaAyristirici.GecerliMi(AylikOdeme), nameof(AylikOdeme), ParaAyristirici.GecersizMesaji);
        h.Denetle(AylikOdeme > 0, nameof(AylikOdeme), "Aylık taksit tutarı sıfırdan büyük olmalı.");
        h.Denetle(kanallar.Count > 0, nameof(Kanallar), "En az bir kanal seçin.");
        return !h.Var;
    }

    [RelayCommand]
    private Task KaydetAsync() => FormIsleAsync(Hatalar, async n =>
    {
        if (!EditorMu || Secili is not null)
            return;
        var kanallar = SecilenKanallar();
        if (!KrediFormuGecerli(kanallar))
            return;
        var g = new KrediTakipYaz(Guid.Empty, Ad.Trim(), CekilenTutar, DateOnly.FromDateTime(CekimTarihi), DateOnly.FromDateTime(IlkTaksitTarihi), TaksitSayisi, AylikOdeme, kanallar, MevcutKredi);
        g = g with { IstekId = _kayit.Al(g) };
        if (Uygula(await api.TakipKrediKaydetAsync(g), n))
```

`Kasa.App.Core/KrediTakipViewModel.cs` (7/7) — Bul:

```csharp
    {
        Krediler.Clear();
        Kanallar.Clear();
        Yeni();
        Gerekce = GecisAciklama = TaksitNotu = "";
        KapatmaTutari = TaksitTutari = 0;
        foreach (var key in new[] { _kayit, _taksit, _kapat, _durum, _gecis })
```

Yerine:

```csharp
    {
        Krediler.Clear();
        Kanallar.Clear();
        YeniForm();
        Gerekce = GecisAciklama = TaksitNotu = "";
        KapatmaTutari = TaksitTutari = 0;
        foreach (var key in new[] { _kayit, _taksit, _kapat, _durum, _gecis })
```

`Kasa.App/Views/KrediTakipPage.cs` (1/2) — Bul:

```csharp
public sealed class KrediTakipPage : TakipSayfasi<KrediTakipViewModel>, IQueryAttributable
{
    private readonly View _ozet;
    private readonly SorguSecimi _secim = new("KrediId");

    public void ApplyQueryAttributes(IDictionary<string, object> query)
```

Yerine:

```csharp
public sealed class KrediTakipPage : TakipSayfasi<KrediTakipViewModel>, IQueryAttributable
{
    private readonly View _ozet;
    /// <summary>Yeni kredi formu ve genel hata kutusu: kaydetme başarısız olunca ilk hatalı alana kaydırılır (tasarım 2026-10-02 §1).</summary>
    private readonly View _yeniKredi, _hataKutusu;
    private readonly SorguSecimi _secim = new("KrediId");

    public void ApplyQueryAttributes(IDictionary<string, object> query)
```

`Kasa.App/Views/KrediTakipPage.cs` (2/2) — Bul:

```csharp
        var ozet = Kart("Kredi ayrıntısı", BagliBuyuk(nameof(vm.KrediOzeti)));
        _ozet = ozet;
        Govde.Add(Kart("Krediler",
            Liste<KrediTakipSatiri>(nameof(vm.Krediler), async s => { vm.SecCommand.Execute(s); await Kaydirici.ScrollToAsync(ozet, ScrollToPosition.Start, true); }),
            Editor(Dugme("Yeni kredi / mevcut krediyi ekle", nameof(vm.YeniCommand)))));
        Govde.Add(ozet);
        Govde.Add(Editor(Goster(Kart("Kredi ekle", Alan("Banka / kredi adı", Girdi(nameof(vm.Ad))), Onay("Önceden çekilmiş mevcut kredi: yeni kasa girişi oluşturma", nameof(vm.MevcutKredi)),
            Alan("Çekilen tutar", Girdi(nameof(vm.CekilenTutar), true)), Alan("Çekim tarihi", Tarih(nameof(vm.CekimTarihi))), Alan("İlk / kalan ilk taksit tarihi", Tarih(nameof(vm.IlkTaksitTarihi))),
            Alan("Taksit sayısı", Girdi(nameof(vm.TaksitSayisi), sayi: true)), Alan("Aylık taksit tutarı", Girdi(nameof(vm.AylikOdeme), true)),
            Metin("Tek kanal seçerseniz tutarın tamamı o kanala gider. Birden fazla kanalda kredi girişi ve her taksit eşit, kuruşları korunarak bölünür. Seçili kanallar sonradan kendiliğinden değişmez."),
            KanalSecimleri(nameof(vm.Kanallar)), Dugme("Tüm aktif kanalları seç", nameof(vm.TumKanallariSecCommand)),
            Metin("Yeni kredi genel kasaya bir kez girer; kanal payları aynı girişin dağılımıdır."), Dugme("Krediyi kaydet", nameof(vm.KaydetCommand))), nameof(vm.YeniKredi))));
        Govde.Add(Editor(Goster(Kart("Eski krediyi yeni takibe al",
            Metin("Eski kredi çekimi ikinci kez genel kasaya girmez. Geçmiş kanal bakiyeleri sessizce değiştirilmez; ileri taksitler seçtiğiniz kanallara bölünür."),
            Alan("Geçiş tarihi", Tarih(nameof(vm.GecisTarihi))), KanalSecimleri(nameof(vm.Kanallar)),
```

Yerine:

```csharp
        var ozet = Kart("Kredi ayrıntısı", BagliBuyuk(nameof(vm.KrediOzeti)));
        _ozet = ozet;
        Govde.Add(Kart("Krediler",
            Liste<KrediTakipSatiri>(nameof(vm.Krediler), async s => { await vm.SecCommand.ExecuteAsync(s); await Kaydirici.ScrollToAsync(ozet, ScrollToPosition.Start, true); }),
            Editor(Dugme("Yeni kredi / mevcut krediyi ekle", nameof(vm.YeniCommand)))));
        Govde.Add(ozet);
        const string h = nameof(vm.Hatalar);
        _hataKutusu = FormHatasi(h + ".Genel");
        _yeniKredi = Kart("Yeni kredi", _hataKutusu,
            Alan("Banka / kredi adı", Girdi(nameof(vm.Ad)), h, nameof(vm.Ad)), Onay("Önceden çekilmiş mevcut kredi: yeni kasa girişi oluşturma", nameof(vm.MevcutKredi)),
            Alan("Çekilen tutar", Girdi(nameof(vm.CekilenTutar), true), h, nameof(vm.CekilenTutar)), Alan("Çekim tarihi", Tarih(nameof(vm.CekimTarihi))),
            Alan("İlk / kalan ilk taksit tarihi", Tarih(nameof(vm.IlkTaksitTarihi))),
            Alan("Taksit sayısı", Girdi(nameof(vm.TaksitSayisi), sayi: true), h, nameof(vm.TaksitSayisi)),
            Alan("Aylık taksit tutarı", Girdi(nameof(vm.AylikOdeme), true), h, nameof(vm.AylikOdeme)),
            Metin("Tek kanal seçerseniz tutarın tamamı o kanala gider. Birden fazla kanalda kredi girişi ve her taksit eşit, kuruşları korunarak bölünür. Seçili kanallar sonradan kendiliğinden değişmez."),
            Alan("Kanallar", KanalSecimleri(nameof(vm.Kanallar)), h, nameof(vm.Kanallar)), Dugme("Tüm aktif kanalları seç", nameof(vm.TumKanallariSecCommand)),
            Metin("Yeni kredi genel kasaya bir kez girer; kanal payları aynı girişin dağılımıdır."), Dugme("Krediyi kaydet", nameof(vm.KaydetCommand)));
        vm.Hatalar.GosterIstendi += (_, _) => Gorunur.HatayaGit(_yeniKredi, vm.Hatalar, _hataKutusu);
        Govde.Add(Editor(Goster(_yeniKredi, nameof(vm.YeniKredi))));
        Govde.Add(Editor(Goster(Kart("Eski krediyi yeni takibe al",
            Metin("Eski kredi çekimi ikinci kez genel kasaya girmez. Geçmiş kanal bakiyeleri sessizce değiştirilmez; ileri taksitler seçtiğiniz kanallara bölünür."),
            Alan("Geçiş tarihi", Tarih(nameof(vm.GecisTarihi))), KanalSecimleri(nameof(vm.Kanallar)),
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1140`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/GecersizTutarTests.cs Kasa.App.Core.Tests/KrediFormuTests.cs Kasa.App.Core/KrediTakipViewModel.cs Kasa.App/Views/KrediTakipPage.cs
git commit -F - <<'MESAJ'
feat(app): Krediler yeni kredi formunda alan hataları

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 18: Aylık giderler — Öde düğmesi, şablon ve ödeme formu

Satır düğmesi "Öde" / "Ödemeyi iptal et" (AG-02); şablon ve ödeme formu hataları; ödeme formu seçilince görünür yere kaydırılır; yazılmış şablon "Yeni"den önce onay ister; kayıttan sonraki yenileme hatası formun hatası sayılmaz.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/AylikGiderViewModel.cs`
- Değiştir: `Kasa.App/Views/AylikGiderPage.cs`
- Test (oluştur): `Kasa.App.Core.Tests/AylikGiderFormuTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/GecersizTutarTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/KasaKontrolVeAylikGiderTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/AylikGiderFormuTests.cs` (yeni dosya):

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Aylık giderler (tasarım 2026-10-02 §1, §2, §5; AG-02): satır düğmesi "Öde" / "Ödemeyi iptal et", şablon ve ödeme formu
/// hataları alanda, yazılmış şablon formunda onay, kayıttan sonraki yenileme hatası formun hatası değildir.</summary>
public class AylikGiderFormuTests
{
    private static async Task<(AylikGiderViewModel Vm, KasaKontrolVeAylikGiderTests.Sahte Api)> Kur()
    {
        var api = new KasaKontrolVeAylikGiderTests.Sahte();
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };
        var vm = new AylikGiderViewModel(api, finans, new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Satir_dugmesi_odenmemiste_Ode_odenmiste_Odemeyi_iptal_et_der()
    {
        var (vm, api) = await Kur();
        Assert.Equal("Öde", AylikGiderViewModel.SatirDugmesi(vm.Kayitlar[0]));
        api.Odendi = true;
        await vm.YukleAsync();
        Assert.Equal("Ödemeyi iptal et", AylikGiderViewModel.SatirDugmesi(vm.Kayitlar[0]));
    }

    [Fact]
    public async Task Bos_sablon_formu_alanlari_ayri_ayri_soyler()
    {
        var (vm, api) = await Kur();
        vm.OdemeGunu = 0;

        await vm.SablonKaydetCommand.ExecuteAsync(null);

        Assert.Null(api.Sablon);
        Assert.Equal("Ad / açıklama boş olamaz.", vm.SablonHatalari[nameof(vm.Ad)]);
        Assert.Equal("Gider türünü seçin.", vm.SablonHatalari[nameof(vm.Tur)]);
        Assert.Equal("Aylık tutar sıfırdan büyük olmalı.", vm.SablonHatalari[nameof(vm.Tutar)]);
        Assert.Equal("Ödeme günü 1 ile 31 arasında olmalı.", vm.SablonHatalari[nameof(vm.OdemeGunu)]);
        Assert.Equal("Dağılım biçimini seçin.", vm.SablonHatalari[nameof(vm.DagilimTuru)]);

        vm.DagilimTuru = vm.DagilimTurleri[1];
        await vm.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Dağıtılacak kanalları seçin.", vm.SablonHatalari[nameof(vm.KanalSecimleri)]);
    }

    [Fact]
    public async Task Yazilmis_sablon_formu_yeniden_once_onay_ister()
    {
        var (vm, _) = await Kur();
        vm.Ad = "Kira";
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        var cevap = false;
        vm.BirakmaOnayi = _ => Task.FromResult(cevap);

        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal("Kira", vm.Ad);

        cevap = true;
        await vm.YeniCommand.ExecuteAsync(null);
        Assert.Equal(("", false), (vm.Ad, vm.KaydedilmemisDegisiklikVar));
    }

    [Fact]
    public async Task Odeme_onaysiz_kaydedilmez_hata_onay_kutusunun_altinda()
    {
        var (vm, api) = await Kur();
        vm.OdemeSec(vm.Kayitlar[0]);

        await vm.OdeCommand.ExecuteAsync(null);

        Assert.Empty(api.Odemeler);
        Assert.Equal("Gösterilen ödeme tutarını ve kanal etkisini onaylayın.", vm.OdemeHatalari[nameof(vm.OdemeOnay)]);
        vm.OdemeOnay = true;
        Assert.False(vm.OdemeHatalari.Var);
    }

    [Fact]
    public async Task Kayittan_sonraki_yenileme_hatasi_formun_hatasi_degil_sayfanin_okuma_hatasidir()
    {
        var (vm, api) = await Kur();
        vm.OdemeSec(vm.Kayitlar[0]);
        vm.OdemeOnay = true;
        api.BekleyenAy = Task.FromException<AylikGiderAyDto>(new HttpRequestException());

        await vm.OdeCommand.ExecuteAsync(null);

        Assert.Single(api.Odemeler);
        Assert.False(vm.OdemeHatalari.Var);
        Assert.Equal("Sunucuya ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.", vm.Hata);
        Assert.True(vm.VeriEski);
    }
}
```

`Kasa.App.Core.Tests/GecersizTutarTests.cs` (1/2) — Bul:

```csharp
        vm.Tutar = G;
        await vm.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        vm.Tutar = 100;
        vm.DagilimTuru = vm.DagilimTurleri[2];
```

Yerine:

```csharp
        vm.Tutar = G;
        await vm.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.SablonHatalari[nameof(vm.Tutar)]);

        vm.Tutar = 100;
        vm.DagilimTuru = vm.DagilimTurleri[2];
```

`Kasa.App.Core.Tests/GecersizTutarTests.cs` (2/2) — Bul:

```csharp
        vm.Paylar[0].Tutar = G;
        await vm.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
    }

    [Fact]
```

Yerine:

```csharp
        vm.Paylar[0].Tutar = G;
        await vm.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.SablonHatalari.Genel);
    }

    [Fact]
```

`Kasa.App.Core.Tests/KasaKontrolVeAylikGiderTests.cs` (1/2) — Bul:

```csharp
        SablonFormu(v);
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Contains("dağılım", v.Hata);
        v.DagilimTuru = v.DagilimTurleri[0];
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Genel", f.Sablon!.DagilimTuru);
```

Yerine:

```csharp
        SablonFormu(v);
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Equal("Dağılım biçimini seçin.", v.SablonHatalari[nameof(v.DagilimTuru)]);
        v.DagilimTuru = v.DagilimTurleri[0];
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Genel", f.Sablon!.DagilimTuru);
```

`Kasa.App.Core.Tests/KasaKontrolVeAylikGiderTests.cs` (2/2) — Bul:

```csharp
        v.Paylar[0].Tutar = 99;
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Contains("toplamı", v.Hata);
    }
    [Fact]
    public async Task Aylik_odeme_onay_ister_ve_ag_hatasinda_ayni_istegi_tekrarlar()
```

Yerine:

```csharp
        v.Paylar[0].Tutar = 99;
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Contains("toplamı", v.SablonHatalari.Genel);
    }
    [Fact]
    public async Task Aylik_odeme_onay_ister_ve_ag_hatasinda_ayni_istegi_tekrarlar()
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~AylikGiderFormuTests|FullyQualifiedName~KasaKontrolVeAylikGiderTests|FullyQualifiedName~GecersizTutarTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 7 farklı ileti; örnekler:

- `AylikGiderFormuTests.cs: CS0117: 'AylikGiderViewModel' bir 'SatirDugmesi' tanımı içermiyor`
- `AylikGiderFormuTests.cs: CS1061: 'AylikGiderViewModel' bir 'SablonHatalari' tanımı içermiyor ve 'AylikGiderViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'SablonHatalari' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `AylikGiderFormuTests.cs: CS1061: 'AylikGiderViewModel' bir 'KaydedilmemisDegisiklikVar' tanımı içermiyor ve 'AylikGiderViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KaydedilmemisDegisiklikVar' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `AylikGiderFormuTests.cs: CS1061: 'IRelayCommand' bir 'ExecuteAsync' tanımı içermiyor ve 'IRelayCommand' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'ExecuteAsync' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/AylikGiderViewModel.cs` (1/7) — Bul:

```csharp
namespace Kasa.App.Core;

public record GiderSecimi(string Kod, string Ad);
public partial class AylikGiderViewModel(IAylikGiderApi api, IKasaApi finans, AuthViewModel auth) : OturumluViewModel(auth)
{
    private readonly TekrarAnahtari _sablonKey = new(), _odemeKey = new(), _iptalKey = new();
    private AylikGiderAyDto? _ayVerisi;
    private AylikGiderSablonDto? _duzenlenen;
```

Yerine:

```csharp
namespace Kasa.App.Core;

public record GiderSecimi(string Kod, string Ad);
public partial class AylikGiderViewModel(IAylikGiderApi api, IKasaApi finans, AuthViewModel auth) : OturumluViewModel(auth), IKaydedilmemisForm
{
    /// <summary>Şablon formunun hataları (tasarım 2026-10-02 §1). Sunucu aylık gider hatalarını alan adı olmadan ({ hata }) döndürür:
    /// sunucu iletisi genel hataya gider.</summary>
    public AlanHatalari SablonHatalari { get; } = new();
    /// <summary>"Bu ayın ödemesini kaydet" formunun hataları.</summary>
    public AlanHatalari OdemeHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [SablonHatalari, OdemeHatalari];

    /// <summary>Şablon formunun açıldığı andaki değerleri (yeni ya da düzenlenen şablon): kaydedilmemiş değişiklik ölçütü (tasarım §2).</summary>
    private KaydedilmemisDegisiklik? _formIzi;
    private KaydedilmemisDegisiklik FormIzi => _formIzi ??= new(() => new
    {
        Ad,
        Tur = Tur?.Kod,
        Tutar,
        OdemeGunu,
        Dagilim = DagilimTuru?.Kod,
        GecerliAy,
        Aktif,
        Kanallar = KanalSecimleri.Where(k => k.Secili).Select(k => k.Veri.Id).ToList(),
        Paylar = Paylar.Select(p => new { Kanal = p.Kanal?.Id, p.Tutar }).ToList(),
    });
    public bool KaydedilmemisDegisiklikVar => FormIzi.Var;

    /// <summary>Kabuktan çıkışta "Bırak": şablon formu boş yeni şablona döner.</summary>
    public void DegisiklikleriBirak() => YeniForm();

    /// <summary>Listedeki satırın düğmesi (AG-02): ödenmemiş satırda "Öde", ödenmiş satırda "Ödemeyi iptal et".</summary>
    public static string SatirDugmesi(AylikGiderSatiri satir) => satir.OdendiMi ? "Ödemeyi iptal et" : "Öde";

    private readonly TekrarAnahtari _sablonKey = new(), _odemeKey = new(), _iptalKey = new();
    private AylikGiderAyDto? _ayVerisi;
    private AylikGiderSablonDto? _duzenlenen;
```

`Kasa.App.Core/AylikGiderViewModel.cs` (2/7) — Bul:

```csharp
        var secili = KanalSecimleri.Where(x => x.Secili).Select(x => x.Veri.Id).ToHashSet();
        TakipMetni.Doldur(KanalSecimleri, k.Select(x => new TakipKanalSecimi(x) { Secili = secili.Contains(x.Id) }));
        AyiYansit(a);
        Tamamlandi();
    });
    private void AyiYansit(AylikGiderAyDto a)
```

Yerine:

```csharp
        var secili = KanalSecimleri.Where(x => x.Secili).Select(x => x.Veri.Id).ToHashSet();
        TakipMetni.Doldur(KanalSecimleri, k.Select(x => new TakipKanalSecimi(x) { Secili = secili.Contains(x.Id) }));
        AyiYansit(a);
        if (!FormIzi.Acik)
            FormIzi.Ac();   // ilk yükleme: boş şablon formu kanal seçenekleriyle açıldı
        Tamamlandi();
    });
    private void AyiYansit(AylikGiderAyDto a)
```

`Kasa.App.Core/AylikGiderViewModel.cs` (3/7) — Bul:

```csharp
        OnPropertyChanged(nameof(AySecimiDegisti));
    }
    public Task AyDegistirAsync(int fark) { if (Mesgul) return Task.CompletedTask; AyTarihi = AyTarihi.AddMonths(fark); return YukleAsync(); }
    [RelayCommand] private void Yeni() { _duzenlenen = null; Ad = ""; Tutar = 0; Tur = null; DagilimTuru = null; OdemeGunu = 1; Aktif = true; GecerliAy = new(DateTime.Today.Year, DateTime.Today.Month, 1); Paylar.Clear(); foreach (var k in KanalSecimleri) k.Secili = false; OnPropertyChanged(nameof(SablonBasligi)); }
    public void SablonSec(AylikSablonSatiri satir)
    {
        if (!EditorMu || Mesgul)
            return;
        _duzenlenen = satir.Veri;
        Ad = _duzenlenen.Ad;
        Tutar = _duzenlenen.Tutar;
```

Yerine:

```csharp
        OnPropertyChanged(nameof(AySecimiDegisti));
    }
    public Task AyDegistirAsync(int fark) { if (Mesgul) return Task.CompletedTask; AyTarihi = AyTarihi.AddMonths(fark); return YukleAsync(); }
    /// <summary>"Yeni şablon": yazılmış şablon formu varsa önce onay sorulur (tasarım §2).</summary>
    [RelayCommand]
    private async Task YeniAsync()
    {
        if (await BirakilabilirAsync(FormIzi))
            YeniForm();
    }

    /// <summary>Boş yeni şablon formu; formun hataları kalkar.</summary>
    private void YeniForm()
    {
        _duzenlenen = null;
        Ad = "";
        Tutar = 0;
        Tur = null;
        DagilimTuru = null;
        OdemeGunu = 1;
        Aktif = true;
        GecerliAy = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        Paylar.Clear();
        foreach (var k in KanalSecimleri)
            k.Secili = false;
        OnPropertyChanged(nameof(SablonBasligi));
        SablonHatalari.Temizle();
        FormIzi.Ac();
    }

    /// <summary>"Şablonu düzenle": yazılmış şablon formu varsa önce onay sorulur.</summary>
    public async Task SablonSecAsync(AylikSablonSatiri satir)
    {
        if (await BirakilabilirAsync(FormIzi))
            SablonSec(satir);
    }

    public void SablonSec(AylikSablonSatiri satir)
    {
        if (!EditorMu || Mesgul)
            return;
        SablonHatalari.Temizle();
        _duzenlenen = satir.Veri;
        Ad = _duzenlenen.Ad;
        Tutar = _duzenlenen.Tutar;
```

`Kasa.App.Core/AylikGiderViewModel.cs` (4/7) — Bul:

```csharp
        foreach (var p in _duzenlenen.Dagilimlar.Where(x => x.KanalId is not null))
            Paylar.Add(new(Kanallar.ToList()) { Kanal = Kanallar.FirstOrDefault(k => k.Id == p.KanalId), Tutar = p.Tutar });
        OnPropertyChanged(nameof(SablonBasligi));
    }
    public void PayEkle() => Paylar.Add(new(Kanallar.ToList()));
    [RelayCommand]
    private Task SablonKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu)
            return;
        if (!ParaAyristirici.GecerliMi(Tutar))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (string.IsNullOrWhiteSpace(Ad) || Tur is null || DagilimTuru is null || Tutar <= 0 || OdemeGunu is < 1 or > 31)
        { Hata = "Ad, tür, pozitif tutar, ödeme günü ve dağılım biçimini seçin."; return; }
        IReadOnlyList<KanalPayYaz> paylar = DagilimTuru.Kod switch { DagilimBicimleri.Genel => Array.Empty<KanalPayYaz>(), DagilimBicimleri.Esit => KanalSecimleri.Where(k => k.Secili).Select(k => new KanalPayYaz(k.Veri.Id, 0)).ToList(), _ => TakipMetni.Paylar(Paylar) };
        if (DagilimTuru.Kod != DagilimBicimleri.Genel && paylar.Count == 0)
        { Hata = "Dağıtılacak kanalları seçin."; return; }
        if (DagilimTuru.Kod == DagilimBicimleri.Ozel && paylar.Sum(p => p.Tutar) != Tutar)
        { Hata = "Kanal paylarının toplamı gider tutarıyla aynı olmalıdır."; return; }
        var g = new AylikGiderSablonYaz(Guid.Empty, _duzenlenen?.Surum ?? 0, Ad.Trim(), Tur.Kod, Tutar, OdemeGunu, DagilimTuru.Kod, paylar, new(GecerliAy.Year, GecerliAy.Month, 1), Aktif);
        var id = _duzenlenen?.Id;
        g = g with { IstekId = _sablonKey.Al(new { id, g }) };
        var sonuc = await api.AylikGiderSablonKaydetAsync(id, g);
```

Yerine:

```csharp
        foreach (var p in _duzenlenen.Dagilimlar.Where(x => x.KanalId is not null))
            Paylar.Add(new(Kanallar.ToList()) { Kanal = Kanallar.FirstOrDefault(k => k.Id == p.KanalId), Tutar = p.Tutar });
        OnPropertyChanged(nameof(SablonBasligi));
        FormIzi.Ac();
    }
    public void PayEkle() => Paylar.Add(new(Kanallar.ToList()));
    /// <summary>Şablon formunun ön doğrulaması (tasarım §1): her kural kendi alanının altında; özel dağılımın tutar kuralları genel hatada.</summary>
    private bool SablonFormuGecerli()
    {
        var h = SablonHatalari;
        h.Denetle(!string.IsNullOrWhiteSpace(Ad), nameof(Ad), "Ad / açıklama boş olamaz.");
        h.Denetle(Tur is not null, nameof(Tur), "Gider türünü seçin.");
        h.Denetle(ParaAyristirici.GecerliMi(Tutar), nameof(Tutar), ParaAyristirici.GecersizMesaji);
        h.Denetle(Tutar > 0, nameof(Tutar), "Aylık tutar sıfırdan büyük olmalı.");
        h.Denetle(OdemeGunu is >= 1 and <= 31, nameof(OdemeGunu), "Ödeme günü 1 ile 31 arasında olmalı.");
        h.Denetle(DagilimTuru is not null, nameof(DagilimTuru), "Dağılım biçimini seçin.");
        h.Denetle(DagilimTuru?.Kod != DagilimBicimleri.Esit || KanalSecimleri.Any(k => k.Secili), nameof(KanalSecimleri), "Dağıtılacak kanalları seçin.");
        return !h.Var;
    }

    [RelayCommand]
    private Task SablonKaydetAsync() => FormIsleAsync(SablonHatalari, async n =>
    {
        if (!EditorMu || !SablonFormuGecerli())
            return;
        IReadOnlyList<KanalPayYaz> paylar = DagilimTuru!.Kod switch { DagilimBicimleri.Genel => Array.Empty<KanalPayYaz>(), DagilimBicimleri.Esit => KanalSecimleri.Where(k => k.Secili).Select(k => new KanalPayYaz(k.Veri.Id, 0)).ToList(), _ => TakipMetni.Paylar(Paylar) };
        if (DagilimTuru.Kod != DagilimBicimleri.Genel && paylar.Count == 0)
        { SablonHatalari.Genel = "Dağıtılacak kanalları seçin."; return; }
        if (DagilimTuru.Kod == DagilimBicimleri.Ozel && paylar.Sum(p => p.Tutar) != Tutar)
        { SablonHatalari.Genel = "Kanal paylarının toplamı gider tutarıyla aynı olmalıdır."; return; }
        var tur = Tur!;
        var g = new AylikGiderSablonYaz(Guid.Empty, _duzenlenen?.Surum ?? 0, Ad.Trim(), tur.Kod, Tutar, OdemeGunu, DagilimTuru.Kod, paylar, new(GecerliAy.Year, GecerliAy.Month, 1), Aktif);
        var id = _duzenlenen?.Id;
        g = g with { IstekId = _sablonKey.Al(new { id, g }) };
        var sonuc = await api.AylikGiderSablonKaydetAsync(id, g);
```

`Kasa.App.Core/AylikGiderViewModel.cs` (5/7) — Bul:

```csharp
            Sablonlar[Sablonlar.IndexOf(eski)] = new(sonuc);
        else
            Sablonlar.Add(new(sonuc));
        Yeni();
        Mesaj = "Şablon kaydedildi; ödeme ve kasa hareketi oluşturulmadı.";
        VeriHazir = false;
        var ay = AyTarihi;
        var a = await api.AylikGiderlerAsync(ay.Year, ay.Month);
        if (Gecerli(n) && ay == AyTarihi)
        { SeciliOdeme = null; AyiYansit(a); Tamamlandi(); }
    });
    public void OdemeSec(AylikGiderSatiri satir) { if (Mesgul || !EditorMu || AySecimiDegisti || satir.OdendiMi) return; SeciliOdeme = satir; OdemeTarihi = DateTime.Today; OdemeNotu = ""; OdemeOnay = false; }
    [RelayCommand]
    private Task OdeAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || SeciliOdeme is null || _ayVerisi is null)
            return;
        if (AySecimiDegisti || !OdemeOnay)
        { Hata = "Ayı yenileyin ve gösterilen ödeme tutarı ile kanal etkisini onaylayın."; return; }
        var secili = SeciliOdeme.Veri;
        var ay = _ayVerisi;
        var g = new AylikGiderOdemeYaz(Guid.Empty, secili.SablonSurum, ay.Yil, ay.Ay, DateOnly.FromDateTime(OdemeTarihi), OdemeNotu.Trim());
```

Yerine:

```csharp
            Sablonlar[Sablonlar.IndexOf(eski)] = new(sonuc);
        else
            Sablonlar.Add(new(sonuc));
        YeniForm();
        Mesaj = "Şablon kaydedildi; ödeme ve kasa hareketi oluşturulmadı.";
        VeriHazir = false;
        var ay = AyTarihi;
        await KayittanSonraAyiYenileAsync(n, ay.Year, ay.Month, () => ay == AyTarihi);
    });

    /// <summary>Kayıt alındıktan sonra ayın yenilenmesi: okuma hatası kaydı geri almaz ve formun hatası değildir; sayfa başına okuma
    /// iletisiyle yazılır (kopukken yazılmaz, kabuk şeridi söyler).</summary>
    private async Task KayittanSonraAyiYenileAsync(int n, int yil, int ay, Func<bool> halaAyni)
    {
        try
        {
            var a = await api.AylikGiderlerAsync(yil, ay);
            if (Gecerli(n) && halaAyni())
            { SeciliOdeme = null; AyiYansit(a); Tamamlandi(); }
        }
        catch (Exception hata) when (Gecerli(n))
        {
            Yurutucu.OkumaHatasiniYaz(hata);
            VeriEski = SonGuncelleme is not null;
        }
    }
    public void OdemeSec(AylikGiderSatiri satir) { if (Mesgul || !EditorMu || AySecimiDegisti || satir.OdendiMi) return; OdemeHatalari.Temizle(); SeciliOdeme = satir; OdemeTarihi = DateTime.Today; OdemeNotu = ""; OdemeOnay = false; }
    [RelayCommand]
    private Task OdeAsync() => FormIsleAsync(OdemeHatalari, async n =>
    {
        if (!EditorMu || SeciliOdeme is null || _ayVerisi is null)
            return;
        if (AySecimiDegisti)
        { OdemeHatalari.Genel = "Ay seçimi değişti. Seçilen ayı gösterip ödemeyi yeniden seçin."; return; }
        if (!OdemeHatalari.Denetle(OdemeOnay, nameof(OdemeOnay), "Gösterilen ödeme tutarını ve kanal etkisini onaylayın."))
            return;
        var secili = SeciliOdeme.Veri;
        var ay = _ayVerisi;
        var g = new AylikGiderOdemeYaz(Guid.Empty, secili.SablonSurum, ay.Yil, ay.Ay, DateOnly.FromDateTime(OdemeTarihi), OdemeNotu.Trim());
```

`Kasa.App.Core/AylikGiderViewModel.cs` (6/7) — Bul:

```csharp
        SeciliOdeme = null;
        Mesaj = "Nakit / havale ödemesi kaydedildi. Kasa etkisi bir kez işlendi.";
        VeriHazir = false;
        var a = await api.AylikGiderlerAsync(ay.Yil, ay.Ay);
        if (Gecerli(n) && ay.Yil == AyTarihi.Year && ay.Ay == AyTarihi.Month)
        { AyiYansit(a); Tamamlandi(); }
    });
    public Task IptalAsync(AylikGiderSatiri satir, string aciklama, int onayOturumu) => YurutAsync(async n =>
    {
```

Yerine:

```csharp
        SeciliOdeme = null;
        Mesaj = "Nakit / havale ödemesi kaydedildi. Kasa etkisi bir kez işlendi.";
        VeriHazir = false;
        await KayittanSonraAyiYenileAsync(n, ay.Yil, ay.Ay, () => ay.Yil == AyTarihi.Year && ay.Ay == AyTarihi.Month);
    });
    public Task IptalAsync(AylikGiderSatiri satir, string aciklama, int onayOturumu) => YurutAsync(async n =>
    {
```

`Kasa.App.Core/AylikGiderViewModel.cs` (7/7) — Bul:

```csharp
        if (Gecerli(n) && ay == AyTarihi)
        { AyiYansit(a); Tamamlandi(); }
    });
    protected override void OturumTemizle() { _ayVerisi = null; Kayitlar.Clear(); Iptaller.Clear(); OnPropertyChanged(nameof(IptalVar)); Sablonlar.Clear(); Kanallar.Clear(); KanalSecimleri.Clear(); SeciliOdeme = null; AyOzeti = OdemeNotu = ""; Yeni(); foreach (var k in new[] { _sablonKey, _odemeKey, _iptalKey }) k.Temizle(); }
}
public record AylikGiderSatiri(AylikGiderSatirDto Veri)
{
```

Yerine:

```csharp
        if (Gecerli(n) && ay == AyTarihi)
        { AyiYansit(a); Tamamlandi(); }
    });
    protected override void OturumTemizle() { _ayVerisi = null; Kayitlar.Clear(); Iptaller.Clear(); OnPropertyChanged(nameof(IptalVar)); Sablonlar.Clear(); Kanallar.Clear(); KanalSecimleri.Clear(); SeciliOdeme = null; AyOzeti = OdemeNotu = ""; YeniForm(); OdemeHatalari.Temizle(); foreach (var k in new[] { _sablonKey, _odemeKey, _iptalKey }) k.Temizle(); }
}
public record AylikGiderSatiri(AylikGiderSatirDto Veri)
{
```

`Kasa.App/Views/AylikGiderPage.cs` (1/2) — Bul:

```csharp
        vm.YukleAsync)
    {
        var ay = new HorizontalStackLayout { Spacing = 10, Children = { Tikla("Önceki ay", () => vm.AyDegistirAsync(-1)), Tikla("Sonraki ay", () => vm.AyDegistirAsync(1)) } };
        Govde.Add(Kart("Ayın giderleri", ay, Alan("Gösterilecek ay", Tarih(nameof(vm.AyTarihi))), Tikla("Seçilen ayı göster", vm.YukleAsync),
            Goster(Metin("Ay seçimi değişti. Kayıtları ve ödeme tutarlarını yenilemek için seçilen ayı gösterin."), nameof(vm.AySecimiDegisti)), BagliBuyuk(nameof(vm.AyOzeti)),
            Liste<AylikGiderSatiri>(nameof(vm.Kayitlar), async s =>
```

Yerine:

```csharp
        vm.YukleAsync)
    {
        var ay = new HorizontalStackLayout { Spacing = 10, Children = { Tikla("Önceki ay", () => vm.AyDegistirAsync(-1)), Tikla("Sonraki ay", () => vm.AyDegistirAsync(1)) } };
        // AG-02: ödenmemiş satırda "Öde", ödenmiş satırda "Ödemeyi iptal et" (iptal gerekçe penceresiyle onay ister).
        Govde.Add(Kart("Ayın giderleri", ay, Alan("Gösterilecek ay", Tarih(nameof(vm.AyTarihi))), Tikla("Seçilen ayı göster", vm.YukleAsync),
            Goster(Metin("Ay seçimi değişti. Kayıtları ve ödeme tutarlarını yenilemek için seçilen ayı gösterin."), nameof(vm.AySecimiDegisti)), BagliBuyuk(nameof(vm.AyOzeti)),
            Liste<AylikGiderSatiri>(nameof(vm.Kayitlar), async s =>
```

`Kasa.App/Views/AylikGiderPage.cs` (2/2) — Bul:

```csharp
                    await GerekceyleAsync("Aylık gider ödemesini iptal et", (gerekce, oturum) => vm.IptalAsync(s, gerekce, oturum));
                else
                    vm.OdemeSec(s);
            }, "Ödemeyi aç / iptal et", _ => vm.EditorMu)));
        Govde.Add(Goster(Kart("İptal edilen ödemeler", Metin("İptal edilen ödeme kasaya yansımaz ve ay toplamlarına girmez; planı yukarıda yeniden ödeme bekler."),
            Liste<AylikGiderIptalSatiri>(nameof(vm.Iptaller))), nameof(vm.IptalVar)));
        Govde.Add(Editor(Goster(Kart("Bu ayın ödemesini kaydet", Bagli(nameof(vm.OdemeEtkisi)),
            Alan("Gerçek ödeme tarihi", Tarih(nameof(vm.OdemeTarihi))), Alan("Not / dekont açıklaması", Girdi(nameof(vm.OdemeNotu))),
            Onay("Ödeme gerçekleşti; gösterilen tutar ve kanal paylarını onaylıyorum.", nameof(vm.OdemeOnay)), Dugme("Nakit / havale ödemesini kaydet", nameof(vm.OdeCommand))),
            nameof(vm.OdemeSecili))));
        Govde.Add(Kart("Düzenli gider şablonları", Metin("Kaydedilmiş ödemelerin tutarı ve kanal payları sabit kalır. Değişiklikler seçilen geçerlilik ayından itibaren uygulanır."),
            Liste<AylikSablonSatiri>(nameof(vm.Sablonlar), s => { vm.SablonSec(s); return Task.CompletedTask; }, "Şablonu düzenle", _ => vm.EditorMu),
            Editor(Dugme("Yeni şablon", nameof(vm.YeniCommand)))));
        Govde.Add(Editor(Kart("Şablon bilgileri", Bagli(nameof(vm.SablonBasligi)), Alan("Ad / açıklama", Girdi(nameof(vm.Ad))),
            Alan("Gider türü", Secim(nameof(vm.Turler), nameof(vm.Tur))), Alan("Aylık tutar", Girdi(nameof(vm.Tutar), true)),
            Alan("Ödeme günü (1–31; kısa ayda son gün)", Girdi(nameof(vm.OdemeGunu), sayi: true)), Alan("Dağılım biçimi — seçin", Secim(nameof(vm.DagilimTurleri), nameof(vm.DagilimTuru))),
            Goster(KanalSecimleri(nameof(vm.KanalSecimleri)), nameof(vm.EsitDagilim)), Goster(Paylar(vm.Paylar, vm.PayEkle), nameof(vm.OzelDagilim)),
            Metin("Yalnız genel kasa seçilirse kanallar değişmez. Eşit dağılım yalnız seçtiğiniz kanalları kullanır; yeni kanallar sonradan otomatik eklenmez."),
            Alan("Geçerlilik ayı (cari ay veya sonrası)", Tarih(nameof(vm.GecerliAy))), Onay("Şablon aktif (kapatırsanız sonraki planlar arşivlenir)", nameof(vm.Aktif)),
            Dugme("Şablonu kaydet — ödeme oluşturmaz", nameof(vm.SablonKaydetCommand)))));
    }
}
```

Yerine:

```csharp
                    await GerekceyleAsync("Aylık gider ödemesini iptal et", (gerekce, oturum) => vm.IptalAsync(s, gerekce, oturum));
                else
                    vm.OdemeSec(s);
            }, "Öde", _ => vm.EditorMu, AylikGiderViewModel.SatirDugmesi)));
        Govde.Add(Goster(Kart("İptal edilen ödemeler", Metin("İptal edilen ödeme kasaya yansımaz ve ay toplamlarına girmez; planı yukarıda yeniden ödeme bekler."),
            Liste<AylikGiderIptalSatiri>(nameof(vm.Iptaller))), nameof(vm.IptalVar)));

        // Ödeme formu: genel hata formun en üstünde, onay hatası onay kutusunun altında; seçilince görünür yere kaydırılır (AG-02).
        const string o = nameof(vm.OdemeHatalari);
        var odemeHataKutusu = FormHatasi(o + ".Genel");
        var odemeFormu = Kart("Bu ayın ödemesini kaydet", odemeHataKutusu, Bagli(nameof(vm.OdemeEtkisi)),
            Alan("Gerçek ödeme tarihi", Tarih(nameof(vm.OdemeTarihi))), Alan("Not / dekont açıklaması", Girdi(nameof(vm.OdemeNotu))),
            Alan("", Onay("Ödeme gerçekleşti; gösterilen tutar ve kanal paylarını onaylıyorum.", nameof(vm.OdemeOnay)), o, nameof(vm.OdemeOnay)),
            Dugme("Nakit / havale ödemesini kaydet", nameof(vm.OdeCommand)));
        Govde.Add(Editor(Goster(odemeFormu, nameof(vm.OdemeSecili))));

        Govde.Add(Kart("Düzenli gider şablonları", Metin("Kaydedilmiş ödemelerin tutarı ve kanal payları sabit kalır. Değişiklikler seçilen geçerlilik ayından itibaren uygulanır."),
            Liste<AylikSablonSatiri>(nameof(vm.Sablonlar), vm.SablonSecAsync, "Şablonu düzenle", _ => vm.EditorMu),
            Editor(Dugme("Yeni şablon", nameof(vm.YeniCommand)))));

        // Şablon formu: başlık modu söyler; genel hata formun en üstünde, alan hataları alanın altında (tasarım 2026-10-02 §1).
        const string h = nameof(vm.SablonHatalari);
        var sablonHataKutusu = FormHatasi(h + ".Genel");
        var sablonFormu = Kart("Şablon bilgileri", Bagli(nameof(vm.SablonBasligi)), sablonHataKutusu,
            Alan("Ad / açıklama", Girdi(nameof(vm.Ad)), h, nameof(vm.Ad)),
            Alan("Gider türü", Secim(nameof(vm.Turler), nameof(vm.Tur)), h, nameof(vm.Tur)),
            Alan("Aylık tutar", Girdi(nameof(vm.Tutar), true), h, nameof(vm.Tutar)),
            Alan("Ödeme günü (1–31; kısa ayda son gün)", Girdi(nameof(vm.OdemeGunu), sayi: true), h, nameof(vm.OdemeGunu)),
            Alan("Dağılım biçimi — seçin", Secim(nameof(vm.DagilimTurleri), nameof(vm.DagilimTuru)), h, nameof(vm.DagilimTuru)),
            Goster(Alan("Kanallar", KanalSecimleri(nameof(vm.KanalSecimleri)), h, nameof(vm.KanalSecimleri)), nameof(vm.EsitDagilim)),
            Goster(Paylar(vm.Paylar, vm.PayEkle), nameof(vm.OzelDagilim)),
            Metin("Yalnız genel kasa seçilirse kanallar değişmez. Eşit dağılım yalnız seçtiğiniz kanalları kullanır; yeni kanallar sonradan otomatik eklenmez."),
            Alan("Geçerlilik ayı (cari ay veya sonrası)", Tarih(nameof(vm.GecerliAy))), Onay("Şablon aktif (kapatırsanız sonraki planlar arşivlenir)", nameof(vm.Aktif)),
            Dugme("Şablonu kaydet — ödeme oluşturmaz", nameof(vm.SablonKaydetCommand)));
        Govde.Add(Editor(sablonFormu));

        vm.OdemeHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(odemeFormu, vm.OdemeHatalari, odemeHataKutusu);
        vm.SablonHatalari.GosterIstendi += (_, _) => Gorunur.HatayaGit(sablonFormu, vm.SablonHatalari, sablonHataKutusu);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.OdemeSecili) && vm.OdemeSecili)
                Gorunur.Yap(odemeFormu, KaydirmaHesabi.FormKaydirmasi);
        };
    }
}
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1145`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/AylikGiderFormuTests.cs Kasa.App.Core.Tests/GecersizTutarTests.cs Kasa.App.Core.Tests/KasaKontrolVeAylikGiderTests.cs Kasa.App.Core/AylikGiderViewModel.cs Kasa.App/Views/AylikGiderPage.cs
git commit -F - <<'MESAJ'
feat(app): Aylık giderlerde Öde düğmesi, form hataları ve kaydırma

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 19: Ayarlar — kanal formu

Kanal adı ve açılış devri alan hataları, sunucu eşlemesi (`ad`, `acilisDevri`), çakışma iletisi formun içinde.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/AyarlarViewModel.cs`
- Değiştir: `Kasa.App/Views/AyarlarPage.xaml`
- Değiştir: `Kasa.App/Views/AyarlarPage.xaml.cs`
- Test (değiştir): `Kasa.App.Core.Tests/CekirdekSurumVmTests.cs`
- Test (değiştir): `Kasa.App.Core.Tests/GecersizTutarTests.cs`
- Test (oluştur): `Kasa.App.Core.Tests/KanalFormuTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/CekirdekSurumVmTests.cs` — Bul:

```csharp
        vm.DuzenKanalSira = 5;
        api.KanallarListe = [new(1, "MEZAT", false, 0, 0m, Surum: 3)];
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal(kanalIletisi, vm.Hata);
        Assert.False(vm.Kanallar.Single().Aktif); // liste güncel kayıtlarla yenilendi
        Assert.Equal((1, 5), (vm.DuzenKanalId, vm.DuzenKanalSira)); // form korunur
    }
```

Yerine:

```csharp
        vm.DuzenKanalSira = 5;
        api.KanallarListe = [new(1, "MEZAT", false, 0, 0m, Surum: 3)];
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal(kanalIletisi, vm.KanalHatalari.Genel);
        Assert.False(vm.Kanallar.Single().Aktif); // liste güncel kayıtlarla yenilendi
        Assert.Equal((1, 5), (vm.DuzenKanalId, vm.DuzenKanalSira)); // form korunur
    }
```

`Kasa.App.Core.Tests/GecersizTutarTests.cs` — Bul:

```csharp
        vm.DuzenKanalAcilisDevri = G;
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonKanalOlustur);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
    }

    [Fact]
```

Yerine:

```csharp
        vm.DuzenKanalAcilisDevri = G;
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonKanalOlustur);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.KanalHatalari[nameof(vm.DuzenKanalAcilisDevri)]);
    }

    [Fact]
```

`Kasa.App.Core.Tests/KanalFormuTests.cs` (yeni dosya):

```csharp
using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Ayarlar kanal formu (tasarım 2026-10-02 §1): ad ön doğrulaması, sunucu alan eşlemesi ("ad", "acilisDevri"), eşlenmeyen
/// çakışma iletisi genel hatada, başka kanala geçince hatalar kalkar.</summary>
public class KanalFormuTests
{
    private static async Task<(AyarlarViewModel Vm, SahteApi Api)> Kur()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false), KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0m)] };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac());
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Bos_ad_istek_gondermez_alanin_altinda_soylenir()
    {
        var (vm, api) = await Kur();

        await vm.KanalKaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonKanalOlustur);
        Assert.Equal("Kanal adı boş olamaz.", vm.KanalHatalari[nameof(vm.DuzenKanalAd)]);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Sunucu_alan_hatasi_eslenir_hatalar_baska_kanala_gecince_kalkar()
    {
        var (vm, api) = await Kur();
        vm.KanalDuzenle(vm.Kanallar[0]);
        api.KanalGuncelleHatasi = new KasaApiException(HttpStatusCode.BadRequest, "birleşik",
            alanHatalari: new Dictionary<string, string> { ["ad"] = "Bu ad sistem tarafından kullanılıyor.", ["acilisdevri"] = "Tutar en fazla iki ondalık basamak içerebilir." });
        vm.DuzenKanalAd = "Ortak";

        await vm.KanalKaydetCommand.ExecuteAsync(null);

        Assert.Equal("Bu ad sistem tarafından kullanılıyor.", vm.KanalHatalari[nameof(vm.DuzenKanalAd)]);
        Assert.Equal("Tutar en fazla iki ondalık basamak içerebilir.", vm.KanalHatalari[nameof(vm.DuzenKanalAcilisDevri)]);
        vm.YeniKanalCommand.Execute(null);
        Assert.False(vm.KanalHatalari.Var);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~KanalFormuTests|FullyQualifiedName~AyarlarViewModelTests|FullyQualifiedName~CekirdekSurumVmTests"
```

Beklenen (Kasa.App.Core.Tests): derleme hatası, 3 farklı ileti; örnekler:

- `CekirdekSurumVmTests.cs: CS1061: 'AyarlarViewModel' bir 'KanalHatalari' tanımı içermiyor ve 'AyarlarViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KanalHatalari' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `GecersizTutarTests.cs: CS1061: 'AyarlarViewModel' bir 'KanalHatalari' tanımı içermiyor ve 'AyarlarViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KanalHatalari' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`
- `KanalFormuTests.cs: CS1061: 'AyarlarViewModel' bir 'KanalHatalari' tanımı içermiyor ve 'AyarlarViewModel' türünde bir ilk bağımsız değişken kabul eden hiçbir erişilebilir 'KanalHatalari' genişletme yöntemi bulunamadı (bir kullanma yönergeniz veya derleme başvurunuz eksik olabilir mi?)`

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/AyarlarViewModel.cs` (1/4) — Bul:

```csharp
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AyarKaydetCommand))] private bool _ayarlarYuklendi;
    public const string AyarlarYuklenmediMesaji = "Ayarlar sunucudan yüklenemedi; kayıtlı değerlerin üzerine varsayılanlar yazılmasın diye kaydedilmedi. Ekranı yenileyip yeniden deneyin.";

    // Kanal düzenleme
    [ObservableProperty] private int _duzenKanalId;      // 0 = yeni
    [ObservableProperty] private string _duzenKanalAd = "";
```

Yerine:

```csharp
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AyarKaydetCommand))] private bool _ayarlarYuklendi;
    public const string AyarlarYuklenmediMesaji = "Ayarlar sunucudan yüklenemedi; kayıtlı değerlerin üzerine varsayılanlar yazılmasın diye kaydedilmedi. Ekranı yenileyip yeniden deneyin.";

    /// <summary>Kanal formunun hataları (tasarım 2026-10-02 §1): ad ve açılış devri alanın altında, eşlenemeyen sunucu iletisi
    /// (ör. "Bu kanal adı zaten kullanılıyor.") formun genel hatasında.</summary>
    public AlanHatalari KanalHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [KanalHatalari];

    /// <summary>Sunucunun kanal doğrulama alanları (KanalEndpoints; küçük harf) → formun alanları.</summary>
    private static readonly Dictionary<string, string> KanalSunucuAlanlari = new()
    {
        ["ad"] = nameof(DuzenKanalAd),
        ["acilisdevri"] = nameof(DuzenKanalAcilisDevri),
    };

    // Kanal düzenleme
    [ObservableProperty] private int _duzenKanalId;      // 0 = yeni
    [ObservableProperty] private string _duzenKanalAd = "";
```

`Kasa.App.Core/AyarlarViewModel.cs` (2/4) — Bul:

```csharp
        DuzenKanalSira = 0;
        DuzenKanalAcilisDevri = _kayitliKanalAcilisDevri = 0;
        _duzenKanalSurum = 0;
    }

    [RelayCommand]
```

Yerine:

```csharp
        DuzenKanalSira = 0;
        DuzenKanalAcilisDevri = _kayitliKanalAcilisDevri = 0;
        _duzenKanalSurum = 0;
        KanalHatalari.Temizle();
    }

    [RelayCommand]
```

`Kasa.App.Core/AyarlarViewModel.cs` (3/4) — Bul:

```csharp
        DuzenKanalSira = k.Sira;
        DuzenKanalAcilisDevri = _kayitliKanalAcilisDevri = k.AcilisDevri;
        _duzenKanalSurum = k.Surum;
    }

    [RelayCommand]
    private Task KanalKaydetAsync() => YurutAsync(async n =>
    {
        if (!ParaAyristirici.GecerliMi(DuzenKanalAcilisDevri))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (SifirOnayMetni($"{DuzenKanalAd} açılış devri", _kayitliKanalAcilisDevri, DuzenKanalAcilisDevri, _kanalSifirOnayi) is { } onay)
        {
            _kanalSifirOnayi = true;
```

Yerine:

```csharp
        DuzenKanalSira = k.Sira;
        DuzenKanalAcilisDevri = _kayitliKanalAcilisDevri = k.AcilisDevri;
        _duzenKanalSurum = k.Surum;
        KanalHatalari.Temizle();
    }

    [RelayCommand]
    private Task KanalKaydetAsync() => FormIsleAsync(KanalHatalari, async n =>
    {
        KanalHatalari.Denetle(!string.IsNullOrWhiteSpace(DuzenKanalAd), nameof(DuzenKanalAd), "Kanal adı boş olamaz.");
        KanalHatalari.Denetle(ParaAyristirici.GecerliMi(DuzenKanalAcilisDevri), nameof(DuzenKanalAcilisDevri), ParaAyristirici.GecersizMesaji);
        if (KanalHatalari.Var)
            return;
        if (SifirOnayMetni($"{DuzenKanalAd} açılış devri", _kayitliKanalAcilisDevri, DuzenKanalAcilisDevri, _kanalSifirOnayi) is { } onay)
        {
            _kanalSifirOnayi = true;
```

`Kasa.App.Core/AyarlarViewModel.cs` (4/4) — Bul:

```csharp
            return;
        YeniKanal();
        KanalOnayiniSifirla();
        await DoldurAsync(n);
    });

    /// <summary>Onay diyaloğundan sonra gelir: başka işlem (yükleme, kayıt) sürerken silme yapılmaz ve bu söylenir (sessizce yok
    /// sayılmaz).</summary>
```

Yerine:

```csharp
            return;
        YeniKanal();
        KanalOnayiniSifirla();
        // Kanal kaydedildi: listenin yeniden okunamaması kaydı geri almaz, formun hatası değildir (sayfanın okuma hatasıdır).
        try
        { await DoldurAsync(n); }
        catch (Exception hata) when (Gecerli(n))
        { Yurutucu.OkumaHatasiniYaz(hata); }
    }, KanalSunucuAlanlari);

    /// <summary>Onay diyaloğundan sonra gelir: başka işlem (yükleme, kayıt) sürerken silme yapılmaz ve bu söylenir (sessizce yok
    /// sayılmaz).</summary>
```

`Kasa.App/Views/AyarlarPage.xaml` (1/2) — Bul:

```xml
    Title="Ayarlar"
    BackgroundColor="{StaticResource AppBg}">

    <ScrollView>
        <VerticalStackLayout x:Name="AyarlarAlani" Padding="{OnIdiom Desktop='28,22', Default='16,14'}" Spacing="20"
                             MaximumWidthRequest="1160">
            <VerticalStackLayout Spacing="3">
```

Yerine:

```xml
    Title="Ayarlar"
    BackgroundColor="{StaticResource AppBg}">

    <ScrollView x:Name="AyarlarKaydirici">
        <VerticalStackLayout x:Name="AyarlarAlani" Padding="{OnIdiom Desktop='28,22', Default='16,14'}" Spacing="20"
                             MaximumWidthRequest="1160">
            <VerticalStackLayout Spacing="3">
```

`Kasa.App/Views/AyarlarPage.xaml` (2/2) — Bul:

```xml
            <!-- Kanallar -->
            <Border Style="{StaticResource Card}">
                <VerticalStackLayout Spacing="0">
                    <VerticalStackLayout Padding="18,18,18,0" Spacing="12">
                        <Label Text="KANALLAR" Style="{StaticResource LblSection}" />
                        <!-- Kanal ve dönem kilidi (web kanal diyaloğu ile aynı kural): tamamlanmış ayların kanal kümesi dondurulur. -->
                        <Label Text="Ortak giderler aylık raporda o ayın aktif kanallarına sıralarına göre bölünür. Tamamlanmış ayların kanal kümesi dondurulur: kanal eklemek, pasife almak ya da sırasını değiştirmek yalnız içinde bulunulan ve sonraki ayların Ortak dağılımını etkiler, tamamlanmış ayların raporu değişmez; bu değişiklikler ay kilidi varken de yapılabilir. Açılış devri takip başlangıcından itibaren kanal bakiyesini değiştirir; ay kilidi varken değiştirilemez. Geçmiş kaydı olan ya da tamamlanmış bir ayın kanal kümesinde yer alan kanal silinemez; silmek yerine pasife alın."
                               Style="{StaticResource LblPageSub}" />
                        <Grid ColumnDefinitions="{OnIdiom Desktop='1.4*,90,*,Auto,Auto,Auto', Default='*'}"
                              RowDefinitions="{OnIdiom Desktop='Auto', Default='Auto,Auto,Auto,Auto,Auto,Auto'}"
                              ColumnSpacing="10" RowSpacing="12">
                            <VerticalStackLayout Spacing="0">
                                <Label Text="Kanal adı" Style="{StaticResource LblField}" />
                                <Border Style="{StaticResource FieldBorder}">
                                    <Entry Text="{Binding DuzenKanalAd}" Placeholder="Örn. Nakit" />
                                </Border>
                            </VerticalStackLayout>
                            <VerticalStackLayout Grid.Column="{OnIdiom Desktop=1, Default=0}"
                                                 Grid.Row="{OnIdiom Desktop=0, Default=1}" Spacing="0">
                                <Label Text="Sıra" Style="{StaticResource LblField}" />
                                <Border Style="{StaticResource FieldBorder}">
                                    <Entry Text="{Binding DuzenKanalSira}" Placeholder="1" Keyboard="Numeric"
                                           HorizontalTextAlignment="End" />
                                </Border>
                            </VerticalStackLayout>
                            <VerticalStackLayout Grid.Column="{OnIdiom Desktop=2, Default=0}"
                                                 Grid.Row="{OnIdiom Desktop=0, Default=2}" Spacing="0">
                                <Label Text="Açılış devri" Style="{StaticResource LblField}" />
                                <Border Style="{StaticResource FieldBorder}">
                                    <ctl:ParaGirisi Tutar="{Binding DuzenKanalAcilisDevri}" Placeholder="0,00 ₺" />
                                </Border>
                            </VerticalStackLayout>
                            <HorizontalStackLayout Grid.Column="{OnIdiom Desktop=3, Default=0}"
                                                   Grid.Row="{OnIdiom Desktop=0, Default=3}"
                                                   Spacing="9" VerticalOptions="End" HeightRequest="44">
```

Yerine:

```xml
            <!-- Kanallar -->
            <Border Style="{StaticResource Card}">
                <VerticalStackLayout Spacing="0">
                    <VerticalStackLayout x:Name="KanalFormu" Padding="18,18,18,0" Spacing="12">
                        <Label Text="KANALLAR" Style="{StaticResource LblSection}" />
                        <!-- Kanal ve dönem kilidi (web kanal diyaloğu ile aynı kural): tamamlanmış ayların kanal kümesi dondurulur. -->
                        <Label Text="Ortak giderler aylık raporda o ayın aktif kanallarına sıralarına göre bölünür. Tamamlanmış ayların kanal kümesi dondurulur: kanal eklemek, pasife almak ya da sırasını değiştirmek yalnız içinde bulunulan ve sonraki ayların Ortak dağılımını etkiler, tamamlanmış ayların raporu değişmez; bu değişiklikler ay kilidi varken de yapılabilir. Açılış devri takip başlangıcından itibaren kanal bakiyesini değiştirir; ay kilidi varken değiştirilemez. Geçmiş kaydı olan ya da tamamlanmış bir ayın kanal kümesinde yer alan kanal silinemez; silmek yerine pasife alın."
                               Style="{StaticResource LblPageSub}" />
                        <!-- Kanal formunun genel hatası formun en üstünde, alan hataları alanın altında (tasarım 2026-10-02 §1). -->
                        <Border x:Name="KanalHataKutusu" Style="{StaticResource ErrorBox}"
                                IsVisible="{Binding KanalHatalari.Genel, Converter={StaticResource DoluIse}}">
                            <Label Text="{Binding KanalHatalari.Genel}" Style="{StaticResource LblError}" />
                        </Border>
                        <Grid ColumnDefinitions="{OnIdiom Desktop='1.4*,90,*,Auto,Auto,Auto', Default='*'}"
                              RowDefinitions="{OnIdiom Desktop='Auto', Default='Auto,Auto,Auto,Auto,Auto,Auto'}"
                              ColumnSpacing="10" RowSpacing="12">
                            <ctl:FormAlani Baslik="Kanal adı" Alan="DuzenKanalAd" Hata="{Binding KanalHatalari[DuzenKanalAd]}">
                                <Entry Text="{Binding DuzenKanalAd}" Placeholder="Örn. Nakit" />
                            </ctl:FormAlani>
                            <ctl:FormAlani Grid.Column="{OnIdiom Desktop=1, Default=0}" Grid.Row="{OnIdiom Desktop=0, Default=1}"
                                           Baslik="Sıra" Alan="DuzenKanalSira" Hata="{Binding KanalHatalari[DuzenKanalSira]}">
                                <Entry Text="{Binding DuzenKanalSira}" Placeholder="1" Keyboard="Numeric"
                                       HorizontalTextAlignment="End" />
                            </ctl:FormAlani>
                            <ctl:FormAlani Grid.Column="{OnIdiom Desktop=2, Default=0}" Grid.Row="{OnIdiom Desktop=0, Default=2}"
                                           Baslik="Açılış devri" Alan="DuzenKanalAcilisDevri" Hata="{Binding KanalHatalari[DuzenKanalAcilisDevri]}">
                                <ctl:ParaGirisi Tutar="{Binding DuzenKanalAcilisDevri}" Placeholder="0,00 ₺" />
                            </ctl:FormAlani>
                            <HorizontalStackLayout Grid.Column="{OnIdiom Desktop=3, Default=0}"
                                                   Grid.Row="{OnIdiom Desktop=0, Default=3}"
                                                   Spacing="9" VerticalOptions="End" HeightRequest="44">
```

`Kasa.App/Views/AyarlarPage.xaml.cs` — Bul:

```csharp
        _esik = esik;
        AyarlarAlani.Children.Add(KasaKontrolAlanlari.Esikler(esik));
        AyarlarAlani.Children.Add(new GuvenlikAlani(guvenlik, this));
    }

    protected override async void OnAppearing()
```

Yerine:

```csharp
        _esik = esik;
        AyarlarAlani.Children.Add(KasaKontrolAlanlari.Esikler(esik));
        AyarlarAlani.Children.Add(new GuvenlikAlani(guvenlik, this));
        var gorunur = new Controls.GorunurYapici(AyarlarKaydirici);
        vm.KanalHatalari.GosterIstendi += (_, _) => gorunur.HatayaGit(KanalFormu, vm.KanalHatalari, KanalHataKutusu);
    }

    protected override async void OnAppearing()
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1147`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/CekirdekSurumVmTests.cs Kasa.App.Core.Tests/GecersizTutarTests.cs Kasa.App.Core.Tests/KanalFormuTests.cs Kasa.App.Core/AyarlarViewModel.cs Kasa.App/Views/AyarlarPage.xaml Kasa.App/Views/AyarlarPage.xaml.cs
git commit -F - <<'MESAJ'
feat(app): Ayarlar kanal formunda alan hataları

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 20: Rapor sayfalarında son veri

Kasalar, Haftalık ve Aylık kartları eski veride soluk (HD-01).

**Dosyalar:**
- Değiştir: `Kasa.App/Views/AylikPage.xaml`
- Değiştir: `Kasa.App/Views/HaftalikPage.xaml`
- Değiştir: `Kasa.App/Views/PanelPage.xaml`
- Test (oluştur): `Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.SonVeri.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.SonVeri.cs` (yeni dosya):

```csharp
using System.Text.RegularExpressions;
using Kasa.App.Views;

namespace Kasa.App.Core.Tests;

/// <summary>Rapor sayfalarında son veri (tasarım 2026-10-02 §3; HD-01): VeriVar'a bağlı her kart, son yükleme hata verince
/// (VeriEski) takip sayfalarıyla aynı opaklıkta soluk gösterilir.</summary>
public partial class MauiKayitTutarliligiTests
{
    [Theory]
    [InlineData("PanelPage.xaml", 2)]
    [InlineData("HaftalikPage.xaml", 1)]
    [InlineData("AylikPage.xaml", 1)]
    public void Rapor_sayfalarinin_veri_kartlari_eski_veride_soluktur(string dosya, int kartSayisi)
    {
        var xaml = Oku(Path.Combine("Views", dosya));
        var kartlar = Regex.Matches(xaml, @"<Border Style=""\{StaticResource (Card|CardHero)\}"" IsVisible=""\{Binding VeriVar\}"">\s*<!--[^>]*-->\s*<Border\.Triggers>\s*"
            + @"<DataTrigger TargetType=""Border"" Binding=""\{Binding VeriEski\}"" Value=""True"">\s*<Setter Property=""Opacity"" Value=""([0-9.]+)"" />");
        Assert.Equal(kartSayisi, Regex.Matches(xaml, @"IsVisible=""\{Binding VeriVar\}""").Count);
        Assert.Equal(kartSayisi, kartlar.Count);
        Assert.All(kartlar, k => Assert.Equal(TakipUi.EskiVeriOpakligi, double.Parse(k.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Kasalar alt bölümleri (takip özeti, kasa kontrolü, çekler) her başarılı panel yüklemesinden sonra yenilenir; panelin
    /// hatası onların son verisini silmez (VeriVar artık hatada inmez).</summary>
    [Fact]
    public void Kasalar_alt_bolumleri_panel_yuklendikce_yenilenir()
    {
        var kod = Oku(Path.Combine("Views", "PanelPage.xaml.cs"));
        Assert.Contains("_vm.Yuklendi += async (_, _) =>", kod);
        Assert.DoesNotContain("nameof(vm.VeriVar)", kod);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MauiKayitTutarliligiTests"
```

Beklenen (Kasa.App.Core.Tests): derlenir, `Rapor_sayfalarinin_veri_kartlari_eski_veride_soluktur` teorisinin üç durumu (`PanelPage.xaml`, `HaftalikPage.xaml`, `AylikPage.xaml`) düşer (`Başarısız:     3`): sayfalarda `VeriEski` tetikleyicisi yok.

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App/Views/AylikPage.xaml` — Bul:

```xml
                             SonGuncellemeMetni="{Binding SonGuncellemeMetni}" YenileCommand="{Binding YenileCommand}" />

            <Border Style="{StaticResource Card}" IsVisible="{Binding VeriVar}">
                <VerticalStackLayout Spacing="0">
                    <!-- K4: kapatılmış ay, kapatıldığı andaki raporla gösterilir. -->
                    <Border BackgroundColor="{StaticResource KilitZemin}" StrokeThickness="0" Padding="18,12" IsVisible="{Binding Dondurulmus}">
```

Yerine:

```xml
                             SonGuncellemeMetni="{Binding SonGuncellemeMetni}" YenileCommand="{Binding YenileCommand}" />

            <Border Style="{StaticResource Card}" IsVisible="{Binding VeriVar}">
                <!-- Son yükleme hata verdiyse son başarılı veri soluk gösterilir (tasarım 2026-10-02 §3; HD-01). -->
                <Border.Triggers>
                    <DataTrigger TargetType="Border" Binding="{Binding VeriEski}" Value="True">
                        <Setter Property="Opacity" Value="0.55" />
                    </DataTrigger>
                </Border.Triggers>
                <VerticalStackLayout Spacing="0">
                    <!-- K4: kapatılmış ay, kapatıldığı andaki raporla gösterilir. -->
                    <Border BackgroundColor="{StaticResource KilitZemin}" StrokeThickness="0" Padding="18,12" IsVisible="{Binding Dondurulmus}">
```

`Kasa.App/Views/HaftalikPage.xaml` — Bul:

```xml
            </Border>

            <Border Style="{StaticResource Card}" IsVisible="{Binding VeriVar}">
                <VerticalStackLayout Spacing="0">
                    <Grid Padding="18,16,18,12" ColumnDefinitions="*,Auto">
                        <Label Text="DÖNEM" Style="{StaticResource LblSection}" />
```

Yerine:

```xml
            </Border>

            <Border Style="{StaticResource Card}" IsVisible="{Binding VeriVar}">
                <!-- Son yükleme hata verdiyse son başarılı veri soluk gösterilir (tasarım 2026-10-02 §3; HD-01). -->
                <Border.Triggers>
                    <DataTrigger TargetType="Border" Binding="{Binding VeriEski}" Value="True">
                        <Setter Property="Opacity" Value="0.55" />
                    </DataTrigger>
                </Border.Triggers>
                <VerticalStackLayout Spacing="0">
                    <Grid Padding="18,16,18,12" ColumnDefinitions="*,Auto">
                        <Label Text="DÖNEM" Style="{StaticResource LblSection}" />
```

`Kasa.App/Views/PanelPage.xaml` (1/2) — Bul:

```xml
            </Border>

            <Border Style="{StaticResource CardHero}" IsVisible="{Binding VeriVar}">
                <VerticalStackLayout Spacing="0">
                    <Label Text="GENEL KASA" Style="{StaticResource LblHeroLabel}" />
                    <Label Text="{Binding GuncelKasa, Converter={StaticResource ParaBicim}}"
```

Yerine:

```xml
            </Border>

            <Border Style="{StaticResource CardHero}" IsVisible="{Binding VeriVar}">
                <!-- Son yükleme hata verdiyse son başarılı veri soluk gösterilir (tasarım 2026-10-02 §3; HD-01). -->
                <Border.Triggers>
                    <DataTrigger TargetType="Border" Binding="{Binding VeriEski}" Value="True">
                        <Setter Property="Opacity" Value="0.55" />
                    </DataTrigger>
                </Border.Triggers>
                <VerticalStackLayout Spacing="0">
                    <Label Text="GENEL KASA" Style="{StaticResource LblHeroLabel}" />
                    <Label Text="{Binding GuncelKasa, Converter={StaticResource ParaBicim}}"
```

`Kasa.App/Views/PanelPage.xaml` (2/2) — Bul:

```xml
            </Border>

            <Border Style="{StaticResource Card}" IsVisible="{Binding VeriVar}">
                <VerticalStackLayout Spacing="0">
                    <Label Text="KANAL BAKİYELERİ" Style="{StaticResource LblSection}" Margin="18,16,18,12" />
                    <CollectionView ItemsSource="{Binding Kanallar}" SelectionMode="None">
```

Yerine:

```xml
            </Border>

            <Border Style="{StaticResource Card}" IsVisible="{Binding VeriVar}">
                <!-- Son yükleme hata verdiyse son başarılı veri soluk gösterilir (tasarım 2026-10-02 §3; HD-01). -->
                <Border.Triggers>
                    <DataTrigger TargetType="Border" Binding="{Binding VeriEski}" Value="True">
                        <Setter Property="Opacity" Value="0.55" />
                    </DataTrigger>
                </Border.Triggers>
                <VerticalStackLayout Spacing="0">
                    <Label Text="KANAL BAKİYELERİ" Style="{StaticResource LblSection}" Margin="18,16,18,12" />
                    <CollectionView ItemsSource="{Binding Kanallar}" SelectionMode="None">
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1151`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/Donusturuculer/MauiKayitTutarliligiTests.SonVeri.cs Kasa.App/Views/AylikPage.xaml Kasa.App/Views/HaftalikPage.xaml Kasa.App/Views/PanelPage.xaml
git commit -F - <<'MESAJ'
feat(app): rapor sayfalarında son veriyi soluk göster

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 21: Takip, İşlemler ve Alışlar ekranlarında son veri

Kartlar, Krediler, Çekler, Aylık giderler, Bildirimler ve Alışlar `VeriYukleAsync`/`YuklemeHatasi` ile; İşlemler aynı süzgecin yenilemesinde listeyi korur; kopukken bağlantı hatası yazılmaz.

**Dosyalar:**
- Değiştir: `Kasa.App.Core/AlislarViewModel.cs`
- Değiştir: `Kasa.App.Core/AylikGiderViewModel.cs`
- Değiştir: `Kasa.App.Core/BildirimViewModel.cs`
- Değiştir: `Kasa.App.Core/CekTakipViewModel.cs`
- Değiştir: `Kasa.App.Core/IslemlerViewModel.cs`
- Değiştir: `Kasa.App.Core/KartTakipViewModel.cs`
- Değiştir: `Kasa.App.Core/KrediTakipViewModel.cs`
- Değiştir: `Kasa.App.Core/OturumluViewModel.cs`
- Değiştir: `Kasa.App/Views/AlislarPage.xaml`
- Değiştir: `Kasa.App/Views/IslemlerPage.xaml`
- Test (oluştur): `Kasa.App.Core.Tests/SonVeriEkranTests.cs`

- [ ] **Adım 1: Testleri yaz.**

`Kasa.App.Core.Tests/SonVeriEkranTests.cs` (yeni dosya):

```csharp
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>HD-01 ekran ekran (tasarım 2026-10-02 §3): yükleme hata verince son başarılı veri silinmez, eski işaretlenir; bağlantı
/// kopukken bağlantı hatası sayfaya yazılmaz.</summary>
public class SonVeriEkranTests
{
    private static AuthViewModel Editor() => new(new SahteApi()) { AktifRol = Rol.Editor };
    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)] };

    [Fact]
    public async Task Kartlar_hatada_son_listeyi_korur_ve_eski_isaretler()
    {
        var finans = Finans();
        var vm = new KartTakipViewModel(new FinansTakipTests.Sahte(), finans, Editor());
        await vm.YukleAsync();
        finans.YuklemeHatasi = new HttpRequestException();

        await vm.YukleAsync();

        Assert.Single(vm.Kartlar);
        Assert.True(vm.VeriEski);
        Assert.True(vm.GovdeGorunur);
        Assert.Contains("Sunucuya ulaşılamadı", vm.SayfaHatasi);
    }

    [Fact]
    public async Task Krediler_kopukken_hatayi_sayfaya_yazmaz_son_listeyi_korur()
    {
        var finans = Finans();
        var auth = Editor();
        var vm = new KrediTakipViewModel(new FinansTakipTests.Sahte(), finans, auth);
        await vm.YukleAsync();
        finans.YuklemeHatasi = new HttpRequestException();
        auth.Baglanti.Ulasilamadi();

        await vm.YukleAsync();

        Assert.Single(vm.Krediler);
        Assert.True(vm.VeriEski);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Cekler_hatada_son_listeyi_korur()
    {
        var api = new CekTakipViewModelTests.Sahte { Liste = [CekTakipViewModelTests.Cek(1)] };
        var finans = Finans();
        var vm = new CekTakipViewModel(api, finans, Editor(), new IslemEditorTests.SabitZaman(new DateOnly(2026, 9, 25)));
        await vm.YukleAsync();
        finans.YuklemeHatasi = new HttpRequestException();

        await vm.YukleAsync();

        Assert.Single(vm.Cekler);
        Assert.True(vm.VeriEski);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Islemler_ayni_suzgecin_yenilemesinde_listeyi_korur_kopukken_hata_yazmaz()
    {
        var api = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0)], IslemlerListe = [new IslemDto(1, new DateOnly(2026, 7, 8), "Kargo", 75m, "MEZAT", GiderTipi.Cari, null)] };
        var auth = Editor();
        var vm = new IslemlerViewModel(api, auth, zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 7, 15)));
        await vm.YukleAsync();
        api.YuklemeHatasi = new HttpRequestException();

        await vm.YukleAsync();
        Assert.Single(vm.Islemler);
        Assert.True(vm.VeriVar);
        Assert.True(vm.VeriEski);
        Assert.Contains("Sunucuya ulaşılamadı", vm.YuklemeHatasi);
        Assert.EndsWith(" · güncel olmayabilir", vm.SonGuncellemeMetni);

        auth.Baglanti.Ulasilamadi();
        await vm.YukleAsync();
        Assert.Null(vm.YuklemeHatasi);
        Assert.Single(vm.Islemler);

        api.YuklemeHatasi = null;
        await vm.YukleAsync();
        Assert.False(vm.VeriEski);
    }

    [Fact]
    public async Task Alislar_hatada_son_listeyi_ve_govdeyi_korur()
    {
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = [] };
        var vm = new AlislarViewModel(api, new SahteApi(), Editor());
        await vm.YukleAsync();
        api.ListeGetir = () => Task.FromException<IReadOnlyList<AlisDto>>(new HttpRequestException());

        await vm.YukleAsync();

        Assert.True(vm.GovdeGorunur);
        Assert.True(vm.VeriEski);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Aylik_giderler_hatada_son_veriyi_korur()
    {
        var api = new KasaKontrolVeAylikGiderTests.Sahte();
        var vm = new AylikGiderViewModel(api, Finans(), Editor());
        await vm.YukleAsync();
        api.BekleyenAy = Task.FromException<AylikGiderAyDto>(new HttpRequestException());

        await vm.YukleAsync();

        Assert.Single(vm.Kayitlar);
        Assert.True(vm.GovdeGorunur);
        Assert.True(vm.VeriEski);
    }
}
```

- [ ] **Adım 2: Testleri çalıştır, düştüklerini gör.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~SonVeriEkranTests"
```

Beklenen (Kasa.App.Core.Tests): derlenir, 6 test düşer: `Islemler_ayni_suzgecin_yenilemesinde_listeyi_korur_kopukken_hata_yazmaz`, `Alislar_hatada_son_listeyi_ve_govdeyi_korur`, `Cekler_hatada_son_listeyi_korur`, `Aylik_giderler_hatada_son_veriyi_korur`, `Krediler_kopukken_hatayi_sayfaya_yazmaz_son_listeyi_korur`, `Kartlar_hatada_son_listeyi_korur_ve_eski_isaretler`.

- [ ] **Adım 3: Üretim kodunu yaz.**

`Kasa.App.Core/AlislarViewModel.cs` (1/2) — Bul:

```csharp
        OnPropertyChanged(nameof(DagilimBekliyor));
    }

    public Task YukleAsync() => YurutAsync(async nesil =>
    {
        if (KaydedilmemisDegisiklikVar && VeriHazir)
        { KaydetmeUyarisi(); return; }
        VeriHazir = false;
        var oncekiId = _secili?.Id;
        Alislar.Clear();
        OnPropertyChanged(nameof(DagilimBekleyenTutar));
        OnPropertyChanged(nameof(DagilimBekliyor));
        var kanalIsi = _api.AlisKanallariAsync();
        var alisIsi = _api.AlislarAsync();
        await Task.WhenAll(kanalIsi, alisIsi);
```

Yerine:

```csharp
        OnPropertyChanged(nameof(DagilimBekliyor));
    }

    /// <summary>Alışlar, kanallar ve (editörde) kartlar, giderler, alıcılar. Hata son başarılı listeyi silmez, eski işaretler (tasarım
    /// 2026-10-02 §3); liste yalnız yeni veri gelince değişir.</summary>
    public Task YukleAsync() => VeriYukleAsync(async nesil =>
    {
        if (KaydedilmemisDegisiklikVar && VeriHazir)
        { KaydetmeUyarisi(); return; }
        VeriHazir = false;
        var oncekiId = _secili?.Id;
        var kanalIsi = _api.AlisKanallariAsync();
        var alisIsi = _api.AlislarAsync();
        await Task.WhenAll(kanalIsi, alisIsi);
```

`Kasa.App.Core/AlislarViewModel.cs` (2/2) — Bul:

```csharp
        GiderSecenekleriniYenile();
        OnPropertyChanged(nameof(DagilimBekleyenTutar));
        OnPropertyChanged(nameof(DagilimBekliyor));
        VeriHazir = true;
    });

    [RelayCommand] private Task YenileAsync() => YukleAsync();
```

Yerine:

```csharp
        GiderSecenekleriniYenile();
        OnPropertyChanged(nameof(DagilimBekleyenTutar));
        OnPropertyChanged(nameof(DagilimBekliyor));
        Tamamlandi();
    });

    [RelayCommand] private Task YenileAsync() => YukleAsync();
```

`Kasa.App.Core/AylikGiderViewModel.cs` — Bul:

```csharp
    partial void OnOdemeNotuChanged(string value) => OdemeOnay = false;
    partial void OnAyTarihiChanged(DateTime value) { SeciliOdeme = null; OnPropertyChanged(nameof(AySecimiDegisti)); }
    public bool AySecimiDegisti => _ayVerisi is null || _ayVerisi.Yil != AyTarihi.Year || _ayVerisi.Ay != AyTarihi.Month;
    public Task YukleAsync() => YurutAsync(async n =>
    {
        VeriHazir = false;
        SeciliOdeme = null;
```

Yerine:

```csharp
    partial void OnOdemeNotuChanged(string value) => OdemeOnay = false;
    partial void OnAyTarihiChanged(DateTime value) { SeciliOdeme = null; OnPropertyChanged(nameof(AySecimiDegisti)); }
    public bool AySecimiDegisti => _ayVerisi is null || _ayVerisi.Yil != AyTarihi.Year || _ayVerisi.Ay != AyTarihi.Month;
    /// <summary>Ayın giderleri, şablonlar ve kanallar; hata son başarılı veriyi silmez, eski işaretler (tasarım 2026-10-02 §3).</summary>
    public Task YukleAsync() => VeriYukleAsync(async n =>
    {
        VeriHazir = false;
        SeciliOdeme = null;
```

`Kasa.App.Core/BildirimViewModel.cs` — Bul:

```csharp
    /// <summary>Windows ayarlarında bu uygulamanın bildirimleri kapalı: uyarı ve ayar bağlantısı görünür.</summary>
    [ObservableProperty] private bool _windowsAyarindaKapali;

    public Task YukleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu)
            return;
```

Yerine:

```csharp
    /// <summary>Windows ayarlarında bu uygulamanın bildirimleri kapalı: uyarı ve ayar bağlantısı görünür.</summary>
    [ObservableProperty] private bool _windowsAyarindaKapali;

    /// <summary>Bildirimler, ayar ve cihazlar; hata son başarılı veriyi silmez, eski işaretler (tasarım 2026-10-02 §3).</summary>
    public Task YukleAsync() => VeriYukleAsync(async n =>
    {
        if (!EditorMu)
            return;
```

`Kasa.App.Core/CekTakipViewModel.cs` (1/2) — Bul:

```csharp
            var cekler = await api.CeklerAsync(yon, durum, ara, bas, son);
            var ozet = await api.CekOzetAsync();
            return (kanallar, cekler, ozet);
        }, v => Yansit(v.kanallar, v.cekler, v.ozet));
    }

    /// <summary>Bildirimden gelen çek (//cekler?CekId=…): çek okunur, yönüne ve "Hepsi" durumuna geçilir (süzgeç onu gizlemesin),
```

Yerine:

```csharp
            var cekler = await api.CeklerAsync(yon, durum, ara, bas, son);
            var ozet = await api.CekOzetAsync();
            return (kanallar, cekler, ozet);
        }, v => Yansit(v.kanallar, v.cekler, v.ozet), YuklemeHatasi);
    }

    /// <summary>Bildirimden gelen çek (//cekler?CekId=…): çek okunur, yönüne ve "Hepsi" durumuna geçilir (süzgeç onu gizlemesin),
```

`Kasa.App.Core/CekTakipViewModel.cs` (2/2) — Bul:

```csharp
            SuzgecleriYaz(v.Yon, CekSuzgecleri.Hepsi, null, null);
            Ara = "";
            Yansit(v.kanallar, v.cekler, v.ozet);
        });
    }

    private void Yansit(IReadOnlyList<KanalDto> kanallar, IReadOnlyList<CekDto> cekler, CekOzetDto ozet)
```

Yerine:

```csharp
            SuzgecleriYaz(v.Yon, CekSuzgecleri.Hepsi, null, null);
            Ara = "";
            Yansit(v.kanallar, v.cekler, v.ozet);
        }, YuklemeHatasi);
    }

    private void Yansit(IReadOnlyList<KanalDto> kanallar, IReadOnlyList<CekDto> cekler, CekOzetDto ozet)
```

`Kasa.App.Core/IslemlerViewModel.cs` (1/3) — Bul:

```csharp
    private async Task<bool> ListeYukleAsync(IstekBileti istek, IstekBileti? kaynakIstek)
    {
        bool Guncel() => _listeHatti.Guncel(istek);
        ListeYukleniyor = true;
        YuklemeHatasi = null;
        VeriVar = false;
        var kaynaklar = kaynakIstek is null;
        try
        {
```

Yerine:

```csharp
    private async Task<bool> ListeYukleAsync(IstekBileti istek, IstekBileti? kaynakIstek)
    {
        bool Guncel() => _listeHatti.Guncel(istek);
        // Aynı süzgecin yenilemesinde gösterilen liste yükleme sürerken ve hatada kalır (tasarım 2026-10-02 §3); başka süzgeçte eski
        // süzgecin listesi gösterilmez.
        var ayniSuzgec = _gosterilenSuzgec is not null && _gosterilenSuzgec == SuzgecMetni();
        ListeYukleniyor = true;
        YuklemeHatasi = null;
        if (!ayniSuzgec)
            VeriVar = false;
        var kaynaklar = kaynakIstek is null;
        try
        {
```

`Kasa.App.Core/IslemlerViewModel.cs` (2/3) — Bul:

```csharp
            if (!Guncel())
                return kaynaklar;
            ListeyiUygula(liste, suzgec, bas is not null || bit is not null || kanal is not null);
            VeriVar = true;
            SonGuncelleme = _zaman.GetLocalNow();
        }
        catch (Exception hata)
        {
            // Hatada eski süzgecin listesi ve toplamı gösterilmez; "Henüz işlem yok" da görünmez (VeriVar false). Liste ve
            // kaynaklar salt okumadır: zaman aşımında "sunucuda tamamlanmış olabilir" denmez.
            if (Guncel())
            { YuklemeHatasi = OkumaHataMesaji(hata); ListeyiBosalt(); }
        }
        finally { if (Guncel()) ListeYukleniyor = false; }
        return kaynaklar;
```

Yerine:

```csharp
            if (!Guncel())
                return kaynaklar;
            ListeyiUygula(liste, suzgec, bas is not null || bit is not null || kanal is not null);
            _gosterilenSuzgec = suzgec;
            VeriVar = true;
            VeriEski = false;
            SonGuncelleme = _zaman.GetLocalNow();
        }
        catch (Exception hata)
        {
            // Aynı süzgecin listesi hatada kalır ve eski işaretlenir; başka süzgecin listesi ve toplamı gösterilmez, "Henüz işlem yok"
            // da görünmez (VeriVar false). Liste ve kaynaklar salt okumadır: zaman aşımında "sunucuda tamamlanmış olabilir" denmez.
            // Bağlantı kopukken bağlantı hatası listenin üstüne yazılmaz (kabuk şeridi söyler).
            if (Guncel())
            {
                if (!(Yurutucu.BaglantiHatasi(hata) && BaglantiKopuk))
                    YuklemeHatasi = OkumaHataMesaji(hata);
                if (ayniSuzgec && VeriVar)
                    VeriEski = true;
                else
                { ListeyiBosalt(); VeriVar = false; }
            }
        }
        finally { if (Guncel()) ListeYukleniyor = false; }
        return kaynaklar;
```

`Kasa.App.Core/IslemlerViewModel.cs` (3/3) — Bul:

```csharp
            : ("Henüz işlem yok", "İlk kayıtla liste burada oluşur.");
    }

    private void ListeyiBosalt() { Islemler.Clear(); FiltreSayi = 0; FiltreToplam = 0; FiltreOzet = ""; }

    /// <summary>Oturum değişince bekleyen liste yanıtları uygulanmaz, önceki oturumun listesi ve iletileri kalkar.</summary>
    private void ListeTemizle()
```

Yerine:

```csharp
            : ("Henüz işlem yok", "İlk kayıtla liste burada oluşur.");
    }

    private void ListeyiBosalt() { Islemler.Clear(); FiltreSayi = 0; FiltreToplam = 0; FiltreOzet = ""; _gosterilenSuzgec = null; }

    /// <summary>Gösterilen listenin süzgeci (başarılı yüklemenin); liste yoksa null.</summary>
    private string? _gosterilenSuzgec;

    /// <summary>Oturum değişince bekleyen liste yanıtları uygulanmaz, önceki oturumun listesi ve iletileri kalkar.</summary>
    private void ListeTemizle()
```

`Kasa.App.Core/KartTakipViewModel.cs` — Bul:

```csharp
        return true;
    }

    public Task YukleAsync() => YurutAsync(KartFormu.Yok, async n =>
    {
        var kanallar = await finans.KanallarAsync();
        var kartlar = await api.TakipKartlarAsync();
        if (!Gecerli(n))
```

Yerine:

```csharp
        return true;
    }

    /// <summary>Kartlar ve kanallar; hata son başarılı listeyi silmez, eski işaretler (tasarım 2026-10-02 §3).</summary>
    public Task YukleAsync() => VeriYukleAsync(async n =>
    {
        HataKaynagi = KartFormu.Yok;
        var kanallar = await finans.KanallarAsync();
        var kartlar = await api.TakipKartlarAsync();
        if (!Gecerli(n))
```

`Kasa.App.Core/KrediTakipViewModel.cs` — Bul:

```csharp
    partial void OnDuzenlenenTaksitChanged(TaksitSatiri? value) => OnPropertyChanged(nameof(TaksitDuzenlenebilir));
    partial void OnKapatmaTutariChanged(decimal value) { KapatmaOnay = false; OnPropertyChanged(nameof(KapatmaOzeti)); }
    partial void OnKapatmaTarihiChanged(DateTime value) { KapatmaOnay = false; OnPropertyChanged(nameof(KapatmaOzeti)); }
    public Task YukleAsync() => YurutAsync(async n =>
    {
        var kanallar = await finans.KanallarAsync();
        var krediler = await api.TakipKredilerAsync();
```

Yerine:

```csharp
    partial void OnDuzenlenenTaksitChanged(TaksitSatiri? value) => OnPropertyChanged(nameof(TaksitDuzenlenebilir));
    partial void OnKapatmaTutariChanged(decimal value) { KapatmaOnay = false; OnPropertyChanged(nameof(KapatmaOzeti)); }
    partial void OnKapatmaTarihiChanged(DateTime value) { KapatmaOnay = false; OnPropertyChanged(nameof(KapatmaOzeti)); }
    /// <summary>Krediler ve kanallar; hata son başarılı listeyi silmez, eski işaretler (tasarım 2026-10-02 §3).</summary>
    public Task YukleAsync() => VeriYukleAsync(async n =>
    {
        var kanallar = await finans.KanallarAsync();
        var krediler = await api.TakipKredilerAsync();
```

`Kasa.App.Core/OturumluViewModel.cs` — Bul:

```csharp

    /// <summary>Ekran yüklemesi (tekil işlem): hata okuma iletisiyle yazılır, bağlantı kopukken bağlantı hatası yazılmaz (kabuk
    /// şeridi söyler); son başarılı veri silinmez, varsa eski işaretlenir. Başarılı yükleme <see cref="Tamamlandi"/>'yı çağırır.</summary>
    protected Task VeriYukleAsync(Func<int, Task> islem) => YurutAsync(islem, hataIsle: hata =>
    {
        Yurutucu.OkumaHatasiniYaz(hata);
        VeriEski = SonGuncelleme is not null;
    });
}
```

Yerine:

```csharp

    /// <summary>Ekran yüklemesi (tekil işlem): hata okuma iletisiyle yazılır, bağlantı kopukken bağlantı hatası yazılmaz (kabuk
    /// şeridi söyler); son başarılı veri silinmez, varsa eski işaretlenir. Başarılı yükleme <see cref="Tamamlandi"/>'yı çağırır.</summary>
    protected Task VeriYukleAsync(Func<int, Task> islem) => YurutAsync(islem, hataIsle: YuklemeHatasi);

    /// <summary>Ekran yüklemesinin hatası: okuma iletisiyle yazılır (kopukken bağlantı hatası yazılmaz), son başarılı veri varsa eski
    /// işaretlenir. Son istek hattıyla yükleyen ekranlar (Çekler) bunu hattın hata işleyicisi olarak verir.</summary>
    protected void YuklemeHatasi(Exception hata)
    {
        Yurutucu.OkumaHatasiniYaz(hata);
        VeriEski = SonGuncelleme is not null;
    }
}
```

`Kasa.App/Views/AlislarPage.xaml` (1/2) — Bul:

```xml
            <VerticalStackLayout Spacing="3">
                <Label Text="Alışlar" Style="{StaticResource LblPageTitle}" />
                <Label Text="Kalemleri kaydedin, kanallara dağıtın, inceleme ve ödemeleri takip edin." Style="{StaticResource LblPageSub}" />
            </VerticalStackLayout>
            <Button Grid.Column="1" Text="Yenile" Command="{Binding YenileCommand}" Style="{StaticResource BtnSecondary}" VerticalOptions="Center" />
            <Button Grid.Column="2" Text="+ Yeni alış" Command="{Binding YeniCommand}" VerticalOptions="Center" IsEnabled="{Binding Mesgul, Converter={StaticResource TersIse}}" />
```

Yerine:

```xml
            <VerticalStackLayout Spacing="3">
                <Label Text="Alışlar" Style="{StaticResource LblPageTitle}" />
                <Label Text="Kalemleri kaydedin, kanallara dağıtın, inceleme ve ödemeleri takip edin." Style="{StaticResource LblPageSub}" />
                <!-- Son güncelleme: hiç yükleme yokken "Henüz yüklenmedi.", eski veride "güncel olmayabilir" (tasarım 2026-10-02 §3). -->
                <Label Text="{Binding SonGuncellemeMetni}" Style="{StaticResource LblPageSub}" />
            </VerticalStackLayout>
            <Button Grid.Column="1" Text="Yenile" Command="{Binding YenileCommand}" Style="{StaticResource BtnSecondary}" VerticalOptions="Center" />
            <Button Grid.Column="2" Text="+ Yeni alış" Command="{Binding YeniCommand}" VerticalOptions="Center" IsEnabled="{Binding Mesgul, Converter={StaticResource TersIse}}" />
```

`Kasa.App/Views/AlislarPage.xaml` (2/2) — Bul:

```xml
            </Border>
        </VerticalStackLayout>

        <Grid Grid.Row="2" ColumnDefinitions="300,*" ColumnSpacing="18" IsVisible="{Binding VeriHazir}" IsEnabled="{Binding Mesgul, Converter={StaticResource TersIse}}">
            <Border Style="{StaticResource Card}">
                <Grid RowDefinitions="Auto,*,Auto" RowSpacing="12" Padding="14">
                    <Label Text="ALIŞ KAYITLARI" Style="{StaticResource LblSection}" />
```

Yerine:

```xml
            </Border>
        </VerticalStackLayout>

        <Grid Grid.Row="2" ColumnDefinitions="300,*" ColumnSpacing="18" IsVisible="{Binding GovdeGorunur}" IsEnabled="{Binding Mesgul, Converter={StaticResource TersIse}}">
            <!-- Yükleme hata verse de son başarılı liste görünür kalır ve soluk gösterilir (HD-01). -->
            <Grid.Triggers>
                <DataTrigger TargetType="Grid" Binding="{Binding VeriEski}" Value="True">
                    <Setter Property="Opacity" Value="0.55" />
                </DataTrigger>
            </Grid.Triggers>
            <Border Style="{StaticResource Card}">
                <Grid RowDefinitions="Auto,*,Auto" RowSpacing="12" Padding="14">
                    <Label Text="ALIŞ KAYITLARI" Style="{StaticResource LblSection}" />
```

`Kasa.App/Views/IslemlerPage.xaml` — Bul:

```xml
                               IsVisible="{Binding VeriVar}" />
                    </VerticalStackLayout>
                    <CollectionView Grid.Row="1" ItemsSource="{Binding Islemler}" SelectionMode="None">
                        <CollectionView.ItemTemplate>
                            <DataTemplate x:DataType="api:IslemDto">
                                <VerticalStackLayout>
```

Yerine:

```xml
                               IsVisible="{Binding VeriVar}" />
                    </VerticalStackLayout>
                    <CollectionView Grid.Row="1" ItemsSource="{Binding Islemler}" SelectionMode="None">
                        <!-- Son yükleme hata verdiyse aynı süzgecin son listesi soluk gösterilir (tasarım 2026-10-02 §3; HD-01). -->
                        <CollectionView.Triggers>
                            <DataTrigger TargetType="CollectionView" Binding="{Binding VeriEski}" Value="True">
                                <Setter Property="Opacity" Value="0.55" />
                            </DataTrigger>
                        </CollectionView.Triggers>
                        <CollectionView.ItemTemplate>
                            <DataTemplate x:DataType="api:IslemDto">
                                <VerticalStackLayout>
```

- [ ] **Adım 4: Testleri ve derlemeyi çalıştır.**

```bash
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
bash .github/scripts/maui-lint.sh
```

Beklenen: Kasa.App.Core.Tests `Başarılı:  1157`, `Başarısız:     0`; Windows derlemesi `0 Uyarı`, `0 Hata`; `maui-lint: taban içinde.`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.App.Core.Tests/SonVeriEkranTests.cs Kasa.App.Core/AlislarViewModel.cs Kasa.App.Core/AylikGiderViewModel.cs Kasa.App.Core/BildirimViewModel.cs Kasa.App.Core/CekTakipViewModel.cs Kasa.App.Core/IslemlerViewModel.cs Kasa.App.Core/KartTakipViewModel.cs Kasa.App.Core/KrediTakipViewModel.cs Kasa.App.Core/OturumluViewModel.cs Kasa.App/Views/AlislarPage.xaml Kasa.App/Views/IslemlerPage.xaml
git commit -F - <<'MESAJ'
feat(app): takip, İşlemler ve Alışlar ekranlarında son veriyi koru

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 22: Belge — uygulama notları ve sürüm notu

Tasarıma "Uygulama notları", sürüm taslağına kullanıcıya görünen değişiklik ve elle bakılacaklar.

**Dosyalar:**
- Değiştir: `docs/deploy/kasa-2.4.md`
- Değiştir: `docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md`

- [ ] **Adım 1: Değişiklikleri yaz.**

`docs/deploy/kasa-2.4.md` (1/2) — Bul:

```markdown
günü bildirimle uyarılır. Önerilen sürüm notu: "Çek ve senet takibi: tahsil, ödeme, ciro, kırdırma, vade hatırlatması."
`/api/surum` `notlar` metnine eklenmesi yayın kararıdır.

**Kullanıcıya görünmeyenler:** PR #13, #14 (bağımlılık güvenlik sürümleri: .NET paketleri 10.0.12, MAUI 10.0.110), #17, #19, #20 (biçim), #23 (PDF araç başlarken dolan zaman sınırı artık "bozuk PDF" değil zaman aşımı olarak bildirilir), #24–#26, #28, #29 (yapı; görünüm eşdeğerliği testle sabit).

## 2. Rapor ve muhasebe etkisi
```

Yerine:

```markdown
günü bildirimle uyarılır. Önerilen sürüm notu: "Çek ve senet takibi: tahsil, ödeme, ciro, kırdırma, vade hatırlatması."
`/api/surum` `notlar` metnine eklenmesi yayın kararıdır.

**Masaüstü form hataları ve bağlantı kopması** ([tasarım](../specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md),
[plan](../specs/2026-10-02-masaustu-form-hatalari-ve-baglanti-plan.md)): form hataları alanın altında ve formun içinde gösterilir,
kaydetme başarısız olunca ilk hatalı alana kaydırılır; düzenlenen kayıt başlıkta ve listede belli olur; yazılmış form başka kayda
geçmeden, "Yeni"den ve sayfadan çıkmadan önce "Kaydedilmemiş değişiklik var. Bırakılsın mı?" diye sorar. Kart ödemesi tek "Ödemeyi
kontrol et" düğmesiyle önizlenir, "Onayla ve kaydet" yalnız güncel önizlemede çalışır. Aylık giderlerde satır düğmesi "Öde" /
"Ödemeyi iptal et". Sunucuya ulaşılamayınca gezinme çubuğunda tek şerit ("Sunucuya ulaşılamıyor · Son bağlantı 14:05 · Yeniden
dene") çıkar, son başarılı veri silinmez ve soluk gösterilir; bağlantı gelince açık sayfa bir kez yenilenir. Sunucu değişmez.

**Kullanıcıya görünmeyenler:** PR #13, #14 (bağımlılık güvenlik sürümleri: .NET paketleri 10.0.12, MAUI 10.0.110), #17, #19, #20 (biçim), #23 (PDF araç başlarken dolan zaman sınırı artık "bozuk PDF" değil zaman aşımı olarak bildirilir), #24–#26, #28, #29 (yapı; görünüm eşdeğerliği testle sabit).

## 2. Rapor ve muhasebe etkisi
```

`docs/deploy/kasa-2.4.md` (2/2) — Bul:

```markdown
- **İşlemler:** gider/gelir formu yalnız editörde; ekstre "Kaynak" düğmesi; 6 çip grubu (seçili/seçili değil, tıklama), zaman çiplerinin alt boşluğu, dönem Picker'ları, benzer kayıt uyarısı, boş liste, hata kutusu, yeşil ileti.
- **Alışlar (web ve masaüstü):** ayrıntıda etiketler ve "Kanal dağılımı bekliyor" rozeti, ödeme satırları; yeni/düzenle penceresi; ödeme kaydet (Ara, Daha eski giderler, kart harcaması, taksit); düzelt/taşı ve iptal; belge ekle/kaldır; durum geçişleri, onay/iade; alıcı hesabıyla akış; "Kartı aç" yalnız editörde; şerit boşken dağılım kutusunun en üstte başlaması; sarı kutular, yeşil kart üzerindeki açık metinler, kalem/pay zeminleri; ana sayfa inceleme kutusu; gider penceresindeki taksit alanları; alış ödemesi ayırma ve devir düzeltme önerisi.
- **Kartlar, Krediler, Aylık Giderler, Ekstre, Bildirimler, Dışa aktar (masaüstü):** başlık boyutları, koyu kırmızı hata/koyu yeşil ileti, liste ayırıcı, geçiş uyarısının kırmızıya dönmesi.
- **Gerekçe pencereleri:** kart hareket/ödeme/kullanım, kredi arşiv, aylık gider iptali, ekstre iptali, ay kilidi, belge kaldırma — metinler, Vazgeç, boş gerekçe.
- **Önizleme-onay:** kart ödemesi, faiz/masraf, kart ve kredi geçişi; önizlemeden sonra girdi değişince kaydın reddedilmesi.
- **Aylık Giderler (web):** şablon penceresi (Eşit/Özel, kip değişimi); ödenmiş satır iptal penceresini, bekleyen satır ödeme formunu açar.
```

Yerine:

```markdown
- **İşlemler:** gider/gelir formu yalnız editörde; ekstre "Kaynak" düğmesi; 6 çip grubu (seçili/seçili değil, tıklama), zaman çiplerinin alt boşluğu, dönem Picker'ları, benzer kayıt uyarısı, boş liste, hata kutusu, yeşil ileti.
- **Alışlar (web ve masaüstü):** ayrıntıda etiketler ve "Kanal dağılımı bekliyor" rozeti, ödeme satırları; yeni/düzenle penceresi; ödeme kaydet (Ara, Daha eski giderler, kart harcaması, taksit); düzelt/taşı ve iptal; belge ekle/kaldır; durum geçişleri, onay/iade; alıcı hesabıyla akış; "Kartı aç" yalnız editörde; şerit boşken dağılım kutusunun en üstte başlaması; sarı kutular, yeşil kart üzerindeki açık metinler, kalem/pay zeminleri; ana sayfa inceleme kutusu; gider penceresindeki taksit alanları; alış ödemesi ayırma ve devir düzeltme önerisi.
- **Kartlar, Krediler, Aylık Giderler, Ekstre, Bildirimler, Dışa aktar (masaüstü):** başlık boyutları, koyu kırmızı hata/koyu yeşil ileti, liste ayırıcı, geçiş uyarısının kırmızıya dönmesi.
- **Form hataları ve bağlantı (masaüstü):** gezinme çubuğundaki bağlantı şeridi (sunucu durdurulunca, "Yeniden dene", bağlantı gelince
  kalkması); sayfadan çıkış onayı (menüden başka sayfaya geçiş, "Forma dön" ile kalma); alan altındaki iletiyi ekran okuyucunun
  alan adıyla okuması; hataya kaydırma ve odak.
- **Gerekçe pencereleri:** kart hareket/ödeme/kullanım, kredi arşiv, aylık gider iptali, ekstre iptali, ay kilidi, belge kaldırma — metinler, Vazgeç, boş gerekçe.
- **Önizleme-onay:** kart ödemesi, faiz/masraf, kart ve kredi geçişi; önizlemeden sonra girdi değişince kaydın reddedilmesi.
- **Aylık Giderler (web):** şablon penceresi (Eşit/Özel, kip değişimi); ödenmiş satır iptal penceresini, bekleyen satır ödeme formunu açar.
```

`docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md` (1/2) — Bul:

```markdown
# Masaüstü: form hataları ve bağlantı kopması

Tarih: 2026-10-02 · Durum: tasarım onaylandı, uygulama planı bekliyor · Dal: `ozellik/ux-form-baglanti` (taban `release/2.x`)

## Amaç
```

Yerine:

```markdown
# Masaüstü: form hataları ve bağlantı kopması

Tarih: 2026-10-02 · Durum: tasarım onaylandı, uygulandı ([plan](2026-10-02-masaustu-form-hatalari-ve-baglanti-plan.md)) · Dal:
`ozellik/ux-form-baglanti` (taban `release/2.x`)

## Amaç
```

`docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md` (2/2) — Bul:

```markdown
- **Düğme:** Ödenmemiş satırda "Öde", ödenmiş satırda "Ödemeyi iptal et" yazar. İptal onay ister.
- **Form yeri:** Ödeme formu satırın hemen altında açılır, ya da ortak yardımcıyla görünür yere kaydırılır.

## Dışarıda kalanlar

Bunlar diğer paketlerin işi:
```

Yerine:

```markdown
- **Düğme:** Ödenmemiş satırda "Öde", ödenmiş satırda "Ödemeyi iptal et" yazar. İptal onay ister.
- **Form yeri:** Ödeme formu satırın hemen altında açılır, ya da ortak yardımcıyla görünür yere kaydırılır.

## Uygulama notları

Plan sırasında netleşen ve tasarımın sözünü değiştirmeyen okumalar:

- **Sunucu alan adları:** Alan adlı doğrulama yanıtı (`errors`) yalnız `GirdiDogrulama` kullanan uçlardadır: gider (İşlemler), alış
  ve kanal. Kart, kredi, çek ve aylık gider uçları `{ hata }` döndürür; bu formlarda sunucu iletisi formun genel hatasına gider, alan
  hataları istemcinin ön doğrulamasından gelir.
- **"Vazgeç" onay sormaz:** onay başka kayda geçişte, "Yeni"de ve sayfadan çıkışta sorulur; "Vazgeç" bilerek bırakmaktır. Kartlar'da
  aynı kartın formları arasında geçiş de sorulmaz (aynı kayıt).
- **Bağlı / kopuk:** yanıt alınan her istek (4xx ve 5xx dahil) bağlı sayılır. Sunucu kapalıyken nginx'in döndüğü 502/504 de yanıttır:
  canlıda API durunca şerit çıkmaz, sayfa sunucu hatasını kendi yerinde gösterir.
- **Son veri yalnız aynı sorgunun verisidir:** Aylık'ta başka aya, İşlemler'de başka süzgece geçince önceki sorgunun verisi
  gösterilmez (eski davranış); aynı ay ya da süzgecin yenilemesinde son veri soluk kalır.
- **Kopukken kaydetme:** iletisi yalnız istek sunucuya ulaşamadığında (`HttpRequestException`) yazılır. Zaman aşımında kayıt sunucuda
  tamamlanmış olabileceği için eski "önce listeyi yenileyip kontrol edin" iletisi kalır.
- **İşlemler tutarı:** sunucu eksi gideri (iade) kabul ettiği için ön doğrulama "Tutar sıfır olamaz." der, "sıfırdan büyük" demez.

## Dışarıda kalanlar

Bunlar diğer paketlerin işi:
```

- [ ] **Adım 2: Denetle.**

```bash
git diff --check
```

Beklenen: çıktı yok.

- [ ] **Adım 3: Commit.**

```bash
git add docs/deploy/kasa-2.4.md docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md
git commit -F - <<'MESAJ'
docs(spec): form hataları ve bağlantı uygulama notları ve sürüm notu

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 23: Ekran denemesi (ayrı masaüstü, yerel test sunucusu)

Kod değişmez. Tasarımın "Ekran denemesi" maddesindeki durumlar gerçek pencerede ekran görüntüsüyle doğrulanır. Uygulama yalnız ayrı
**KasaTest** masaüstünde açılır (bekçi `CreateDesktop`); kullanıcının ekranına, faresine ve klavyesine dokunulmaz. Kullanıcının oturum
dosyası (`%LOCALAPPDATA%\User Name\com.royalmezat.kasa\Settings\securestorage.dat`) koşu başında yedeklenir, sonunda geri yüklenir ve
özgün kopyayla karşılaştırılır. Bir durum beklenenden farklıysa ilgili görevin testine dönülür; düzeltme kendi `fix(...)` commit'iyle
yapılır ve deneme yinelenir.

Hazır betikler: `C:/Users/burak/AppData/Local/Temp/claude/C--Users-burak-source-repos-Kasa/2cf5258e-ca1b-45f0-9c5a-07f7d4bbbb74/scratchpad/`
altında `menu-kartlar-gorsel/{kosu.ps1,bekci.ps1,Pencere.cs,Masaustu.cs,Nobetci.cs}` ve `cek-deneme/{sunucu.ps1,tohum.ps1,kimlik.json}`.
Aşağıda bu klasör `$S`, deneme klasörü `$D = $S\ux-form-deneme`'dir.

- [ ] **Adım 1: Derlemeler.** Uygulama ve yerel test sunucusu (sırayla):

```bash
dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false
dotnet build Kasa.Ui.E2E/Sunucu/Kasa.Ui.E2E.Sunucu.csproj -c Release -m:2 -nodeReuse:false
```

Beklenen: ikisinde de `0 Hata`. Uygulama `Kasa.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Kasa.App.exe`, sunucu
`Kasa.Ui.E2E/Sunucu/bin/Release/net10.0/Kasa.Ui.E2E.Sunucu.exe`.

- [ ] **Adım 2: Deneme klasörü.** PowerShell:

```powershell
$S = 'C:\Users\burak\AppData\Local\Temp\claude\C--Users-burak-source-repos-Kasa\2cf5258e-ca1b-45f0-9c5a-07f7d4bbbb74\scratchpad'
$D = "$S\ux-form-deneme"
New-Item -ItemType Directory -Force $D | Out-Null
Copy-Item "$S\menu-kartlar-gorsel\kosu.ps1", "$S\menu-kartlar-gorsel\bekci.ps1", "$S\menu-kartlar-gorsel\Pencere.cs", "$S\menu-kartlar-gorsel\Masaustu.cs", "$S\menu-kartlar-gorsel\Nobetci.cs" $D
Copy-Item "$S\cek-deneme\sunucu.ps1", "$S\cek-deneme\tohum.ps1", "$S\cek-deneme\kimlik.json" $D
$depo = Join-Path $env:LOCALAPPDATA 'User Name\com.royalmezat.kasa\Settings\securestorage.dat'
if (Test-Path $depo) { Copy-Item $depo "$D\securestorage.dat.ozgun" -Force }
```

`sunucu.ps1` bu çalışma ağacının sunucusunu başlatsın: `$exe = '…\cekler\…'` satırı şununla değiştirilir:

```powershell
$exe = 'C:\Users\burak\source\repos\Kasa-paket\ux-form-baglanti\Kasa.Ui.E2E\Sunucu\bin\Release\net10.0\Kasa.Ui.E2E.Sunucu.exe'
```

`tohum.ps1`'in sonuna İşlemler listesi için iki gider eklenir (aynı `Gonder` yardımcısıyla):

```powershell
Gonder Post 'islemler' @{ tarih = '2026-09-29'; cari = 'Ege Gıda'; tutarTl = 8460.69; kanal = $kasa; tip = 'Cari'; not = $null } | Out-Null
Gonder Post 'islemler' @{ tarih = '2026-09-30'; cari = 'Kargo'; tutarTl = 75; kanal = $kasa; tip = 'Cari'; not = $null } | Out-Null
"giderler eklendi"
```

`kosu.ps1`'in adım komutlarına sunucuyu yeniden başlatan `SunucuBaslat` eklenir. Komut deseni:

```powershell
if ($adim -match '^(Menu|Kaydir|Mesgul|SunucuDurdur|SunucuBaslat|Bekle|Isabet|SonBas|Yaz)(\s+(.*))?$') { $komut = $Matches[1]; $arg = $Matches[3] }
```

ve `switch ($komut)` içinde `'SunucuDurdur'` kolunun altına:

```powershell
'SunucuBaslat' { $not = ((& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $G 'sunucu.ps1') -Is baslat) + (& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $G 'tohum.ps1'))) -join ' ' }
```

Sunucu her başlatılışta geçici veritabanını yeniden kurar; editör oturum damgası yapılandırmadan türediği için (yeni veritabanında
geri yükleme dönemi yok) uygulamanın oturumu geçerli kalır.

- [ ] **Adım 3: Adımlar.** `$D\ux-form.adimlar.txt` (UTF-8; satır başına bir adım, `# etiket` ekran görüntüsünün adı):

```text
Menu İşlemler
Bekle 2500
Bas Kaydet # 01-islemler-bos-kayit
Yaz Açıklama / ödeme yapılan yer|Deneme gideri
Bekle 800 # 02-islemler-alan-duzeltildi
Bas Düzenle # 03-islemler-kaydedilmemis-onay
Bas Forma dön # 04-islemler-forma-don
Bas Düzenle
Bas Bırak # 05-islemler-duzenleme-modu
Yaz Açıklama / ödeme yapılan yer|Ege Gıda (düzeltme)
Menu Kasalar # 06-kabuk-cikis-onayi
Bas Forma dön # 07-kabuk-forma-don
Bas Vazgeç
Menu Kartlar
Bekle 2500
Bas Yeni kart ekle
Bas Kartı kaydet # 08-kartlar-yeni-kart-alan-hatalari
Menu Çekler
Bekle 2500
Bas Yeni çek / senet
Bas Kaydet # 09-cekler-bos-kayit
Menu Haftalık
Bekle 2500 # 10-haftalik-bagli
SunucuDurdur
Bas Yenile / tekrar dene
Bekle 16000 # 11-haftalik-serit-ve-soluk-veri
Menu İşlemler
Bekle 16000 # 12-islemler-kopuk-liste-korunur
Bas Düzenle
Yaz Tutar|9000
Bas Değişikliği kaydet
Bekle 3000 # 13-islemler-kopukken-kaydetme
SunucuBaslat
Bas Yeniden dene
Bekle 4000 # 14-serit-kalkti-sayfa-yenilendi
```

Not: çipler (kanal, tip) UI Otomasyonu'nda düğme değildir (denetim K-01); bu yüzden kopukken kaydetme kanalı zaten seçili olan mevcut
kaydın düzeltmesiyle denenir.

- [ ] **Adım 4: Koşu.** Yerel sunucuyu başlatıp tohumlayın, sonra bekçiyle koşun (en çok 6 dakika):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$D\sunucu.ps1" -Is baslat
powershell -NoProfile -ExecutionPolicy Bypass -File "$D\tohum.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "$D\bekci.ps1" -Dizin 'C:\Users\burak\source\repos\Kasa-paket\ux-form-baglanti\Kasa.App\bin\Release\net10.0-windows10.0.19041.0\win-x64' -Ad ux-form -Konum masaustu -Izle 8 -Sure 360
powershell -NoProfile -ExecutionPolicy Bypass -File "$D\sunucu.ps1" -Is durdur
```

Beklenen bekçi son satırları: `açık Kasa.App: 0; oturum dosyası = özgün: True` (özgün dosya yoksa `False` olabilir; o durumda
`securestorage.dat` koşudan önce de yoktu, yedek geri yüklenmez) ve `ux-form.log`'da her adım için `ekran adim-<n>-<etiket>`.

- [ ] **Adım 5: Görüntüleri incele.** `$D\ux-form\adim-*.png` dosyaları açılıp şu durumlar görülür (görmeden "geçti" denmez):

| Görüntü | Beklenen |
|---|---|
| `01-islemler-bos-kayit` | Başlık "Yeni işlem"; "Açıklama boş olamaz.", "Tutar sıfır olamaz.", "Kanal seçin." ilgili alanların altında, çerçeveler kırmızı; sayfa başında hata yok. |
| `02-islemler-alan-duzeltildi` | Açıklama altındaki ileti kalktı; diğerleri duruyor. |
| `03-islemler-kaydedilmemis-onay` | "Kaydedilmemiş değişiklik" penceresi: "Kaydedilmemiş değişiklik var. Bırakılsın mı?", düğmeler "Bırak" ve "Forma dön". |
| `04-islemler-forma-don` | Form "Deneme gideri" ile duruyor, başlık "Yeni işlem". |
| `05-islemler-duzenleme-modu` | Başlık "Düzenleniyor: 29.09.2026 · Ege Gıda", düğmeler "Değişikliği kaydet", "Vazgeç", "Yeni"; listede Ege Gıda satırı yeşil zeminli; eski hatalar yok. |
| `06-kabuk-cikis-onayi` | Menüden Kasalar'a geçerken aynı onay penceresi; sayfa hâlâ İşlemler. |
| `07-kabuk-forma-don` | İşlemler, form yazılan değerle. |
| `08-kartlar-yeni-kart-alan-hatalari` | Başlık "Yeni kart"; "Kart / banka adı boş olamaz." alanın altında; form içinde, görünür alanda. |
| `09-cekler-bos-kayit` | Çek formunda "Çek / senet numarası boş olamaz.", "Banka boş olamaz.", "Kişi boş olamaz.", "Tutar sıfırdan büyük olmalı."; ilk hatalı alan görünür yerde. |
| `11-haftalik-serit-ve-soluk-veri` | Gezinme çubuğunda "Sunucuya ulaşılamıyor · Son bağlantı HH:mm" ve "Yeniden dene"; dönem listesi silinmemiş ve soluk; "Son başarılı güncelleme: … · güncel olmayabilir"; sayfada ayrıca "Sunucuya ulaşılamadı" satırı yok. |
| `12-islemler-kopuk-liste-korunur` | Şerit duruyor; gider listesi soluk ama dolu; tip çipleri (Diğer gider, Sabit gider, Kredi kartı) görünür; listenin üstünde bağlantı hatası yok. |
| `13-islemler-kopukken-kaydetme` | Düzenleme modunda formun üstünde "Sunucuya ulaşılamadı. Kayıt yapılmadı; bağlantı gelince yeniden kaydedin."; tutar 9.000,00 olarak duruyor. |
| `14-serit-kalkti-sayfa-yenilendi` | Şerit yok; liste soluk değil; "güncel olmayabilir" eki yok. |

Görüntülerin yolu ve gözlenen sapmalar rapora yazılır. Ardından deneme ürün sahibinin kendi ekranında, onunla birlikte yinelenir
(tasarım "Ekran denemesi"; bu adım ajan tarafından yapılmaz).

---

## Görev 24: Tam doğrulama

Kod değişmez; bir adım düşerse ilgili görevin testine dönülür, düzeltme kendi `fix(...)` commit'iyle yapılır.

- [ ] **Adım 1: Bütün test projeleri** (sırayla, aynı anda tek `dotnet`):

```bash
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.Sozlesme.Tests/Kasa.Sozlesme.Tests.csproj -c Release -m:2 -nodeReuse:false
```

Beklenen: hepsinde `Başarısız:     0`; `Kasa.ApiClient.Tests` `Başarılı:   195`, `Kasa.App.Core.Tests` `Başarılı:  1157`; Kasa.Core.Tests, Kasa.Api.Tests
ve Kasa.Sozlesme.Tests taban sayılarıyla aynı (`Başarılı:   133`, `Başarılı:  1281`, `Başarılı:    57`; sunucu ve Kasa.Core değişmez).

- [ ] **Adım 2: Windows Release derlemesi (0 uyarı).**

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 --no-incremental -m:2 -nodeReuse:false`
Beklenen: `0 Uyarı`, `0 Hata`.

- [ ] **Adım 3: Biçim.**

Çalıştır: `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes --verbosity minimal`
Beklenen: çıktı yok, çıkış kodu 0.

- [ ] **Adım 4: Boşluk hataları.**

Çalıştır: `git diff --check origin/release/2.x...HEAD`
Beklenen: çıktı yok.

- [ ] **Adım 5: MAUI denetimi.**

Çalıştır: `bash .github/scripts/maui-lint.sh`
Beklenen: `maui-lint: taban içinde.` (doğrudan hex renk ve 200 karakteri aşan satır sayıları artmaz; `.github/scripts/maui-lint-tabani.txt`
değişmez). Çıktı "tabanı düşürün" derse taban `bash .github/scripts/maui-lint.sh --tabani-guncelle` ile düşürülür ve ayrı commit'lenir.

- [ ] **Adım 6: Durum.** `git status` temiz; `git log --oneline origin/release/2.x..HEAD` tasarım, plan ve 22 görev commit'ini gösterir.
  Push yapılmaz.

---

## Tasarım → görev eşlemesi (öz inceleme)

| Tasarım maddesi | Görev |
|---|---|
| §1 İstemci: `AlanHatalari` (küçük harf, `.`/`[i]` önekleri korunur, ilk ileti), birleşik `Message` aynı | 1 |
| §1 Görünüm modeli: `AlanHatalari` (eşlem, `Ayarla`, `Temizle(alan)`, `Temizle()`, bildirim, `Hatalar["Tutar"]`); eşlenemeyen → `FormHatasi` (genel hata) | 3, 4 |
| §1 Sunucu alan adlarının form alanlarına eşlenmesi (`cari` → `DuzenCari`…) | 12 (İşlemler), 13 (Alışlar), 19 (kanal); takip formları sunucudan alan adı almaz (varsayım 1) |
| §1 Ön doğrulama: boş metin, tutar, kanal/tip seçimi, kart günleri 1–31, çek no/kişi/tutar/vade; hata varsa istek gitmez | 12, 13, 14, 15, 16, 17, 18, 19 |
| §1 Gösterim: alan çerçevesi kırmızı, ileti alanın altında, ekran okuyucu adı; genel hata formun üstünde; takip sayfa başı satırı yalnız yükleme hatası | 8, 11 ve form görevleri |
| §1 Hataya kaydırma ve odak; `GorunurYap` ortak yardımcıya | 9; form görevleri `HatayaGit` bağlar |
| §1 Temizleme: alan değişince, başka kayıt, Yeni, Vazgeç, başarı | 4 (`Formlar`, `FormIsleAsync`), form görevleri |
| §1 Kapsam: İşlemler, Alışlar, Çekler (iki form), Kartlar (kart ve ödeme), Krediler, Aylık giderler (şablon ve ödeme), Ayarlar kanal | 12–19 |
| §2 Düzenleme başlığı "Düzenleniyor: {tarih} · {ad}", "Değişikliği kaydet", Vazgeç, satır vurgusu; yeni kayıtta "Yeni …" | 12 (İşlemler), 13 (Alışlar), 14 (Çek), 15 (Kart: ad), 17 ("Yeni kredi") |
| §2 Kaydedilmemiş değişiklik onayı (başka kayıt, Yeni, sayfadan çıkış; "Bırak"/"Forma dön"; açılış değerlerinden farklı) | 5, 10 (kabuk), 12–15, 17, 18 |
| §2 Alışlar'ın uyarısı ortak yapıya | 13 |
| §3 `BaglantiDurumu` tekil; ağ hatası/zaman aşımı kopuk, yanıt bağlı; gönderim noktası | 2, 6 |
| §3 Kabuk şeridi, "Yeniden dene", bağlantı gelince bir kez yenileme; sayfalarda bağlantı iletisi tekrarlanmaz | 4, 10, 21 |
| §3 Son veri korunur (`VeriVar` yazılmaz; `Govde` son veriyle görünür), soluk, "Son güncelleme … · güncel olmayabilir"; kapsam 10 ekran | 7, 11, 20, 21 (Bildirimler 21) |
| §3 Seçenekler: tip sabit, kanal son başarılı yüklemeden | 12 |
| §3 "Henüz yüklenmedi" iki ailede | 7, 11 |
| §3 Kopukken kaydetme: form korunur, ileti, `istekId` aynı | 4, 12 (testli), 14 ve 15 (aynı anahtarla yineleme testleri) |
| §4 Kart ödemesi: tek "Ödemeyi kontrol et", "Onayla ve kaydet", bayat önizlemede kayıt yok | 16 |
| §5 Aylık giderler: "Öde" / "Ödemeyi iptal et", iptal onayı (gerekçe penceresi), form görünür yere kaydırılır | 18 |
| Testler: ApiClient, App.Core (form başına), MAUI tutarlılık ve görünüm eşdeğerliği, 0 uyarı, maui-lint | 1–21, 24 |
| Ekran denemesi | 23 |

**Kapsam dışı bırakılanlar (bilerek):** buton hiyerarşisi, renk, terim, sayfa iskeleti, klavye ve çipler, menü düzeni, dar pencere,
geçici bildirim (tasarım "Dışarıda kalanlar"). Teknik karar 9'daki alt formlar sayfa hatasıyla kalır.

**Yer tutucu taraması:** planda "TBD", "TODO", "benzer şekilde", "uygun hata işleme" yok; her kod adımı ya tam dosya ya da birebir
Bul/Yerine metnidir (betikle üretildi, her "Bul" metninin dosyada bir kez geçtiği denetlendi).

**Tür ve ad tutarlılığı:** `KasaApiException.AlanHatalari`; `IBaglantiBildirimleri` (`SunucuyaUlasildi`, `SunucuyaUlasilamadi`);
`AlanHatalari` (`this[alan]`, `Genel`, `Var`, `Alanlar`, `IlkAlan`, `Ayarla`, `Denetle`, `Temizle`, `SunucuHatalariniYaz`,
`GosterIste`, `GosterIstendi`); `Yurutucu` (`YurutAsync(…, hataIsle)`, `BaglantiHatasi`, `KayitBaglantiIletisi`, `OkumaHatasiniYaz`),
`SonIstekHatti.YukleAsync(…, hataIsle)`; `TemelViewModel` (`BaglantiKopuk`, `Formlar`, `FormIsleAsync`, `FormHatasiniYaz`);
`KaydedilmemisDegisiklik` (`Ac`, `Kapat`, `Acik`, `Var`, `Ileti`, `Baslik`, `Birak`, `FormaDon`), `IKaydedilmemisForm`
(`KaydedilmemisDegisiklikVar`, `DegisiklikleriBirak`); `OturumluViewModel` (`BirakmaOnayi`, `BirakilabilirAsync`, `VeriEski`,
`SonGuncellemeMetni`, `GovdeGorunur`, `VeriYukleAsync`, `YuklemeHatasi`); `BaglantiDurumu` (`Kopuk`, `SonBaglanti`, `SeritMetni`,
`Ulasildi`, `Ulasilamadi`, `BaglantiGeldi`), `AuthViewModel.Baglanti`; `RaporViewModel` (`VeriEski`, `Yuklendi`); `Bicim.HenuzYuklenmedi`,
`Bicim.EskiVeriEki`; arayüzde `FormAlani` (`Baslik`, `Hata`, `Alan`, `Icerik`, `Cerceveli`, `TakipStili`, `Odaklan`), `GorunurYapici`
(`Yap`, `HatayaGit`, `IlkHataliAlan`), `BaglantiSeridi` (`YenidenDeneIstendi`, `Yenileniyor`), `IYenilenebilir.YenileAsync`,
`TakipUi.Alan(ad, v, hatalar, alan)`, `TakipUi.FormHatasi`, `TakipUi.EskiVeriSolugu`, `TakipUi.EskiVeriOpakligi`,
`TakipSayfasi.Gorunur`, `EsitIseConverter` (`EsitIse`). Form başına hatalar: `IslemlerViewModel.Hatalar`, `AlislarViewModel.Hatalar`,
`CekTakipViewModel.Hatalar`/`HareketHatalari`, `KartTakipViewModel.KartHatalari`/`OdemeHatalari`, `KrediTakipViewModel.Hatalar`,
`AylikGiderViewModel.SablonHatalari`/`OdemeHatalari`, `AyarlarViewModel.KanalHatalari` — görevler arasında aynı adlarla kullanılır.
