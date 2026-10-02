# Çekler ve senetler — uygulama planı

> **Ajanlar için:** GEREKLİ ALT BECERİ: görev görev uygulanır (önerilen superpowers:subagent-driven-development; tek oturumda
> superpowers:executing-plans). Adımlar `- [ ]` onay kutularıyla izlenir. Tasarım (bağlayıcı): `docs/specs/2026-10-01-cekler.md`.

**Amaç:** Alınan ve verilen çek ile senetleri sunucuda (kayıt, hareket, durum, ay kilidi, bildirim, panel özeti) ve masaüstünde (Çekler
sayfası, menü, panel kutusu, bildirim tıklaması) uygulamak; çek kasayı yalnız gerçek para hareketi gününde, türetilmiş satırla etkilesin.

**Mimari:** Kurallar saf Kasa.Core'dadır (`CekKurallari`: durum, kalan, geçiş tablosu, hareket hatası; `CekTuretici`: hareket →
sentetik `Gelen`/`Islem`). Sunucu iki tablo saklar (`Cekler`, `CekHareketler`); `HesapServisi.Yukle` kredi taksitlerindeki desenle
hareketlerden bellekte satır türetir, veritabanına gider/gelir yazmaz. Uçlar `/api/takip/cekler` altında `FinansTakipEndpoints`'in
parçalı sınıfıdır (istekId, sürüm, `{ hata }`). Masaüstü `CekTakipViewModel` (Kasa.App.Core) ile `CekTakipPage` (Kasa.App, kodla
kurulan `TakipSayfasi<T>`) kullanır.

**Teknoloji:** .NET 10, ASP.NET Core minimal API, EF Core 10 + **SQLite** (elle yazılmış SQL migration + donmuş `*SchemaModel`),
CommunityToolkit.Mvvm 8.4.2, MAUI 10.0.110 (Windows), xUnit v3 (`xunit.v3.mtp-off` 4.0.1).

**Kanıt:** Bu plandaki bütün kod, `origin/release/2.x` 92e6e36'nın bir kopyasında görev görev uygulandı ve sınandı (C# derlemesi ve
bütün test projeleri yeşil, `dotnet format whitespace` temiz, `maui-lint` taban içinde). Windows uygulamasının C#/XAML derlemesi 0
uyarı verdi; yalnız MSIX içerik kopyalama adımı kopyanın uzun geçici yolu yüzünden düştü; gerçek çalışma ağacında Görev 13'te yeniden
denenir. Test sayıları bu denemeden alınmıştır.

---

## Doğrulanan varsayımlar (kanıt: dosya:satır, taban 92e6e36)

1. **Negatif Gelen destekleniyor; yedek yola gerek yok, ürün sahibine soru yok.**
   - Motor gelirleri yalnız toplar, işaret denetimi yoktur: kanal geliri `Kasa.Core/HesapMotoru.cs:65`, dönem toplamı `:74`, kasa
     sonucu `:87`, aylık kanal geliri `:237-239`, kasa dökümü satırı işaretli tutarı aynen yazar `:121`.
   - Sunucu negatif dönem gelirini zaten kabul ediyor: `Kasa.Api/GelenEndpoints.cs:26` (`v.Para(dto.TutarTl, "tutarTl", negatifOlabilir: true)`).
   - Raporda gelir için `> 0` süzgeci yok: Kasa.Api, Kasa.Core ve Kasa.App.Core'da `TutarTl > 0`/`Gelen > 0` taraması yalnız
     `Kasa.Api/AlisOdemeIslemleri.cs:140`'ı buldu; o da veritabanındaki giderin alışa bağlanabilirliğidir, türetilmiş satıra dokunmaz.
     Aylık raporun kümeli kanal süzgeci `Kasa.Api/Servisler/HesapServisi.cs:556` `k.Gelen != 0` ile çalışır (eksi de sayılır).
   - Görev 1'deki `CekTureticiTests.Cirodan_donus_negatif_gelen_ve_negatif_cari_gider_uretir_motor_toplamlardan_duser` ve
     `Kirdirmadan_donus_...` bunu sabitler: Ekim'de MEZAT `Gelen` −50.000, `CariGiden` −50.000, ay sonucu 0; haftalık toplam gelen
     −50.000, kasa sonucu 0; kırdırma dönüşünde kasa −1.250.
2. **Negatif Cari gider destekleniyor.** Gider girişi negatif tutarı kabul eder: `Kasa.Api/KayitGirdileri.cs:53`; testli örnek
   `Kasa.Api.Tests/BaglanabilirGiderTests.cs:51` (`"İade", -10m`); motor yalnız toplar (`HesapMotoru.cs:66-68`, `:76`, `:240`);
   sunucu zaten eksi satır türetiyor (`HesapServisi.cs:517`, kart devri iadesi `-duzeltme`).
3. **Ortak kanal.** Ortak gider `Islem.Kanal == KanalEtiketleri.Ortak` (`Kasa.Core/Kodlar.cs:17`) taşır. Haftalıkta kanal devrine
   girmez (yalnız `i.Kanal == kanal.Ad`, `HesapMotoru.cs:66-67`), genel kasadan düşer (`:76`). Aylıkta ayın Ortak kümesine kuruş
   bazında bölünür, işaret korunur (`:213-232`, `Math.Sign(artanKurus)` `:228`); küme `HesapServisi.AyRaporu` (`:544-558`).
   Eski model kredi aynı yolu kullanır (`FinansTakipEndpoints.cs:310`). Verilen çek ödemesi `Islem(Kanal: Ortak, Tip: Cari)` ile
   **aynı yolu kullanır**; ek kod gerekmez (Görev 1 ve 3 testleri: 30.000,01 → 10.000,01 / 10.000 / 10.000).
4. **Altın test.** `Kasa.Api.Tests/AltinRaporTests.cs:24-53`: sabit saatli zengin tohumun 30 rapor/okuma ucu yanıtı
   `Altin/rapor-altin.json` ile birebir karşılaştırılır; `OnayliFarklar` (`:58-95`) dışındaki her fark hatadır. Tohumda çek yoktur;
   çek satırları `Yukle`'nin sonunda listelere eklendiği için (Görev 3) bu test **değişmeden** "çek yokken rapor değişmez"i korur.
   Ek test (Görev 3): aynı tohuma kasayı etkilemeyen çek kayıtları (hareketsiz, teminat, karşılıksız, iade) eklenir, 30 ucun
   yanıtı önceki hâliyle birebir aynı kalır.
5. **Rota ve `Bolum`.** Rota `cekler` (`//cekler`, bildirimde `//cekler?CekId={id}`, panelden `//cekler?Suzgec={CekHazirSuzgec}`).
   `Bolum.Cekler` eklemenin tam listesi:
   - `Kasa.App.Core/Rol.cs:93` enum'un **sonuna** `Cekler`; `SekmeModeli.Bolumler` (`:105-109`) `Krediler`'den sonra (izleyici ve
     editör; alıcının listesi `:103-104` değişmez).
   - `Kasa.App.Core/MenuModeli.cs`: `MenuSimgeleri.Cekler` (`:10-25`; Segoe MDL2 ve Fluent'te `E8A5` "Document", Microsoft Learn'de
     iki sayfada da doğrulandı), `Duzen` grubu "Kart ve kredi" → "Kart, kredi ve çek" (`:123-127`) ve öğesi.
   - `Kasa.App/AppShell.xaml:131` sonrası `CeklerItem`; `Kasa.App/AppShell.xaml.cs:32-46` `_menu` sözlüğü.
   - `Kasa.App/MauiProgram.cs`: `ICekApi`, `CekTakipViewModel`, `CekOzetViewModel`, `Views.CekTakipPage`.
   - Testler: `Kasa.App.Core.Tests/RolTests.cs:23,29`, `MenuModeliTests.cs:27,38,52,80`; `Donusturuculer/MauiKayitTutarliligiTests.cs`
     (`:100-115` bölüm ↔ öğe, `:131-137` rota, `:53-98` DI) kendiliğinden kapsar. Bu dört yer aynı görevde değişmelidir: `Bolum`
     eklenip AppShell eklenmezse `MauiKayitTutarliligiTests` kırılır (bu yüzden menü ve rol Görev 11'dedir).
   - `Kasa.App.Core/BildirimHedefi.cs:14-30` `/#cheques/{id}` deseni (Görev 7).
6. **Değişiklik geçmişi listeye eklenmeden yakalanır.** `Kasa.Api/Denetim/DenetimYakalayici.cs:66-90` izleyicideki bütün
   eklenen/değişen/silinen varlıkları alır; yalnız `Haric` (`:29-34`) dışarıdadır; varlık adı tür adından türer (`:301`: `CekEntity`
   → `Cek`, `CekHareketEntity` → `CekHareket`); yalnız `Surum` değişirse olay yoktur (`:38`, `:94-96`). Kod değişikliği gerekmez; yalnız
   masaüstündeki görünen adlar (`Kasa.App.Core/KasaKontrolViewModel.cs:247-281`) eklenir (Görev 6).

**Doğrulanmış diğer gerçekler (planı etkiler):**
- Veritabanı **SQLite**'tır (`Kasa.Api/Kasa.Api.csproj` `Microsoft.EntityFrameworkCore.Sqlite`, `Kasa.Api/Program.cs:16` `UseSqlite`).
  Migration'lar `dotnet ef migrations add` ile **üretilmez**: elle yazılmış `m.Sql("""...""")`, `Kimlik` sabiti, donmuş
  `*SchemaModel` ve `KasaDbContextModelSnapshot` zinciri (örnek `Kasa.Api/Migrations/20261007000100_EditorSifirlamaIzi.cs`). `Down`
  hiçbir migration'da çalışmaz, `NotSupportedException` atar; bu kurala uyulur. Ad: `20261008000100_Cekler` (sıradaki).
- `Kasa.Sozlesme.Tests/MimariTests.cs:115-141`: Kodlar sabitlerinin değeri başka dosyada dize olarak yazılamaz (`=> "x"` switch kolu
  hariç). Yeni değerlerden `"Odeme"` ve `"Iade"` `Kasa.Api/EkstreMetinOkuyucu.cs:154-300`'de zaten başka anlamla geçiyor; `"Cek"`
  denetim varlık adı olarak sözlükte geçecek. Üçü `CokAnlamliDegerler`'e gerekçesiyle eklenir (Görev 1).
- Göç testleri raporu çek tabloları olmayan eski şemada da hesaplar (`VeritabaniGocuTests`, `BelgeDeposuGecisTests`): türetme tablo
  yoksa atlanır (Görev 3; bu koruma olmadan 6 göç testi düştü).

## Ürün sahibinin onayladığı yorumlar (2026-10-01)

Engelleyici soru yok: negatif `Gelen` ve negatif Cari gider desteklendiği için tasarımın "dönüşü karşı yönlü gider olarak türet"
yedeği uygulanmaz; dönüş görevleri bir karara bağlı değildir. Uygulamayı durdurmayan, onay istenen yorumlar (plan bunları seçti;
farklı karar çıkarsa yalnız ilgili test ve satır değişir):

1. **"Vadesi geçmiş, tahsil edilmemiş"** (panel ve şerit): plan bunu *alınan, portföyde ya da kısmen tahsil edilmiş, vadesi bugünden
   önce, teminat değil* diye okur; karşılıksız çekler bu kutuya girmez (kendi çipleri var). Örnek: 22 Eylül vadeli 5.000 TL çekin
   2.000 TL'si tahsil edildiyse kutuda "3.000,00 ₺ (1 çek)" görünür; aynı çek karşılıksız işaretlenirse kutudan çıkar.
2. **Senet metinleri**: tasarım yalnız çek örneği verir; plan senette "Senet tahsili: …", "Senet vadesi", "Ödenecek senet" yazar.
3. **Bildirim tutar biçimi**: kart ve kredi bildirimleriyle aynı, her zaman kuruşlu: "50.000,00 TL", "29.999,50 TL" (ürün sahibi
   bu biçimi seçti; tasarımdaki "50.000 TL" örneği buna göre okunur).
4. **Sürüm notu**: çekler `release/2.x`'e girerse `docs/deploy/kasa-2.4.md`'ye yazılır (Görev 12). `/api/surum` `notlar` metnine
   eklenmesi yayın kararıdır; plan onu değiştirmez.

---

## Çalışma kuralları (her görevde geçerli)

- **Yer:** çalışma ağacı `C:/Users/burak/source/repos/Kasa-paket/cekler`, dal `ozellik/cekler` (taban `origin/release/2.x` 92e6e36 +
  bu plan). Her komut bu klasörde çalışır. **Push yok.**
- **dotnet:** aynı anda tek `dotnet` komutu; her derleme ve test komutuna `-c Release -m:2 -nodeReuse:false` eklenir. Testler:
  - `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~X"`
  - aynı biçimde `Kasa.Api.Tests`, `Kasa.ApiClient.Tests`, `Kasa.App.Core.Tests`, `Kasa.Sozlesme.Tests`.
  - Çıktı dili Türkçedir: başarı `Başarılı!  - Başarısız:     0, Başarılı:    N`, başarısızlık `Başarısız! - Başarısız: …`.
  - Başlangıç test sayıları (92e6e36): Kasa.Core.Tests 111, Kasa.Api.Tests 1223, Kasa.ApiClient.Tests 179, Kasa.App.Core.Tests 1050,
    Kasa.Sozlesme.Tests 55. Kasa.Api.Tests'in tamamı ≈ 4,5 dakika sürer.
- **xUnit1051:** test metodunda `CancellationToken` alan (ya da böyle aşırı yüklemesi olan) yönteme `TestContext.Current.CancellationToken`
  verilir. Planın test kodu buna uyar.
- **Linux CI:** test kodunda Windows yolu ya da API'si yok; tarihler sabit saatten (`KasaWebFactory.Sabit`, `AylikGiderTests.Fabrika`).
- **Adlar Türkçe**; yorumlar ve iletiler Türkçe; mevcut adlandırma.
- **Kod değerleri** (`Kasa.Core.Kodlar`) Kasa.Core/Api/ApiClient/App.Core/App kaynağında dize olarak yazılmaz; sabit kullanılır,
  görünen ad yalnız switch kolunun sağında (`CekTurleri.Senet => "Senet"`) yazılabilir (`MimariTests.Kod_degerleri_kaynakta_elle_yazilmaz`).
  Test projeleri bu kurala tabi değildir.
- **Kaçış dizileri:** `MenuSimgeleri.Cekler = "\uE8A5"` dışında plan kodu kaçış dizisi kullanmaz. Yazma araçları `\u` dizisini
  bozabilir; dosyayı yazdıktan sonra `git diff Kasa.App.Core/MenuModeli.cs` ile `"\uE8A5"` olduğunu denetleyin.
- **Satır uzunluğu** Kasa.App'te en çok 200 karakter; renk yalnız `Colors.xaml` anahtarlarından; `maui-lint` tabanı artmaz.
- **Tek satırda birden çok deyimli blok yazılmaz** (`{ a; b; }`); `if` gövdesi alt satırdadır. Görev sonunda
  `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes` temiz olmalıdır (Görev 13 tamamını denetler).
- **Uygulama çalıştırılmaz** (Kasa.App.exe başlatılmaz).
- **Commit biçimi:** Türkçe konu, boş satır, (gerekirse) gövde, boş satır, `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`:

  ```bash
  git commit -F - <<'MESAJ'
  feat(core): çek ve senet kuralları ve türetici

  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  MESAJ
  ```

---

## Verilmiş teknik kararlar

1. **Durum saklanmaz** (tasarım): `CekKurallari.Durum` hareketleri `Sira` sırasıyla okur. İade → İade edildi; açık ciro → Ciro edildi;
   açık kırdırma → Kırdırıldı; kalan ≤ 0 → Tahsil edildi/Ödendi; karşılıksız (ya da dönüş) → Karşılıksız; tahsilat var → Kısmen;
   yoksa Portföyde. Ciro edilmiş, kırdırılmış ve iade edilmiş çekte kalan 0 gösterilir.
2. **Geçiş tablosu sunucudadır**; masaüstü düğmeleri `CekDto.IzinliHareketler`'den çizer, kuralı yinelemez.
3. **Ek hareket kuralları** (tasarımda yazılı değil; tutarlılık için): hareket tarihi takip başlangıcı ile bugün arasında olmalı
   (gerçek para hareketi; K1 başlangıç öncesine satır düşmesin) ve önceki hareketin tarihinden önce olamaz; karşılıksız ve iadede tutar
   0'dır; ciroda ve kırdırmada karşı taraf adı zorunludur (Cari gider adı odur).
4. **Kasa**: verilen çekte `CekEntity.KanalId` ödeneceği kasadır, `null` **Ortak**'tır (istekte `"Ortak"`). Ödeme hareketi çekin
   kasasını **kopyalar**: sonradan çekin kasası değişse de geçmiş ödeme satırı değişmez. Alınan çekte kasa harekette seçilir (gerçek
   kanal; Ortak gelir olmaz). Dönüş, ters çevirdiği ciro/kırdırmanın kasasını ve karşı tarafını kopyalar.
5. **Türetilmiş satır anahtarları** tasarımdaki gibi `Cek:{hareketId}` ve gider ayağı `Cek:{hareketId}:gider`; `Islem.Kaynak` iki
   ayakta da `Cek:{hareketId}`. Kasa dökümü türü `KasaHareketTurleri.Cek` ("Cek"; masaüstünde "Çek"). Açıklamalar: "Çek tahsili:
   Ahmet Yılmaz / 12345", "Mehmet Ticaret · Çek ödemesi / 777", "Çek cirosu …", "Çek kırdırma masrafı …", "Çek dönüşü …".
6. **Hatalı veri raporu düşürmez**: kanalı çözülemeyen kasa etkili hareketin geliri genel kasaya, gideri "Dağılım bekliyor"a yazılır ve
   karantina uyarısı verilir (mevcut `VeriKarantinasi` deseni).
7. **Ay kilidi** kaydetme kancasındadır (`AyKilidiKurallari`), uç ayrıca denetlemez; ihlal mevcut 409 iletisiyle döner
   ("{tarih} tarihine kadar dönem kilitli. …").
8. **Aynı çek uyarısı** her okumada hesaplanır (`CekDto.Uyari`); POST yanıtı da onu taşır. Masaüstü kaydetmeden önce
   `CeklerAsync(yön, Hepsi, ara: no)` ile arar ve `CekKurallari.AyniCek` (Kasa.Core, sunucuyla aynı kural) ile karşılaştırır; yeni uç yok.
9. **Takip başlangıcı**: çek hareketi varsa takip başlangıcı değiştirilemez (`FinansHesaplari.IlkMaliKayitTuru`, "çek hareketi");
   hareketsiz çek başlangıcı sabitlemez.
10. **Kanal silme**: çeki ya da hareketi olan kanal silinemez (`KanalKurallari.SilmeEngeli`; tabloda ON DELETE RESTRICT).
11. **Bildirim kaynağı** ayrı yalıtılır (`KasaEsikServisi` deseni); hesaplanamazsa kaynak adı "Cekler" ile editöre günlük uyarı gider
    (hedef `/#cheques`).
12. **Masaüstü süzgeçleri sunucuda** uygulanır (her çip/arama yeniden yükler); bildirimden açılışta çekin yönüne ve "Hepsi" süzgecine
    geçilir ki çek listede bulunsun.
13. **Menü ve rol Görev 11'dedir** (önerilen sıradan sapma): `Bolum` değeri AppShell öğesiyle aynı committe eklenmezse
    `MauiKayitTutarliligiTests` kırılır.
14. **Silme**: `DELETE /api/takip/cekler/{id}` hareketleri ve çeki birlikte siler (her silme denetim olayıdır); kilitli kasa hareketi
    olan çek silinmez. DELETE gövdesi `{ istekId, surum }` (`[FromBody]`, `BelgeEndpoints`'teki gibi).

---

## Dosya yapısı

| Dosya | Durum | Sorumluluk | Görev |
|---|---|---|---|
| `Kasa.Core/Kodlar.cs` | Değişir | `CekTurleri`, `CekYonleri`, `CekKonumlari`, `CekHareketTurleri`, `CekDurumlari`, `CekSuzgecleri`; `KasaHareketTurleri.Cek` | 1 |
| `Kasa.Core/Cek.cs` | Yeni | `CekBilgisi`, `CekHareketi`, `CekDurumu`, `CekKurallari`, `CekTuretici` | 1 |
| `Kasa.Core.Tests/CekKurallariTests.cs`, `CekTureticiTests.cs` | Yeni | Kural ve türetme testleri | 1 |
| `Kasa.Core.Tests/KodlarTests.cs`, `Kasa.Sozlesme.Tests/MimariTests.cs` | Değişir | Kod değerleri; çok anlamlı değerler | 1 |
| `Kasa.Api/Data/CekEntities.cs`, `KasaDbContext.Cekler.cs` | Yeni | Varlıklar, DbSet, model yapılandırması | 2 |
| `Kasa.Api/Data/KasaDbContext.cs` | Değişir | `ConfigureCekler` çağrısı | 2 |
| `Kasa.Api/Migrations/20261008000100_Cekler.cs`, `CeklerSchemaModel.cs` | Yeni | İki tablo; donmuş model | 2 |
| `Kasa.Api/Migrations/KasaDbContextModelSnapshot.cs` | Değişir | Anlık model `CeklerSchemaModel` | 2 |
| `Kasa.Api/KanalKurallari.cs` | Değişir | Çeki olan kanal silinmez | 2 |
| `Kasa.Api.Tests/CekVeriModeliTests.cs` | Yeni | Tablo, tekillik, kanal silme | 2 |
| `Kasa.Api.Tests/{BelgeDeposuGecis,EskiGelir,VeritabaniGocu}Tests.cs` | Değişir | Migration sayısı 24 → 25 | 2 |
| `Kasa.Api/Servisler/HesapServisi.cs` | Değişir | `CekSatirlari` türetmesi | 3 |
| `Kasa.Api/Servisler/KasaDokumu.cs`, `Kasa.Api/KasaKontrolDtos.cs` | Değişir | Döküm türü `Cek` | 3 |
| `Kasa.Api/FinansHesaplari.cs` | Değişir | Çek hareketi takip başlangıcını sabitler | 3 |
| `Kasa.Api.Tests/CekRaporTests.cs` | Yeni | Haftalık/aylık/panel/döküm, Ortak, ciro, kırdırma, dondurulmuş ay | 3 |
| `Kasa.Api.Tests/AltinRaporTests.cs`, `TakipBaslangiciDegisikligiTests.cs` | Değişir | Kasasız çek raporu değiştirmez; başlangıç | 3 |
| `Kasa.Api/AyKilidiKurallari.cs`, `Kasa.Api.Tests/CekKilitTests.cs` | Değişir / Yeni | Ay kilidi | 4 |
| `Kasa.Api/CekDtos.cs`, `CekServisi.cs`, `CekEndpoints.cs` | Yeni | DTO, okuma, uçlar | 5 (8'de genişler) |
| `Kasa.Api/FinansTakipEndpoints.cs` | Değişir | `MapCekEndpoints(api)` | 5 |
| `Kasa.Api.Tests/CekUcTests.cs`, `UcYetkiTaramasiTests.cs`, `Altin/uc-envanteri.txt` | Yeni / Değişir | Uç testleri, yetki, envanter | 5, 8 |
| `Kasa.Api.Tests/CekDenetimTests.cs`, `Kasa.App.Core/KasaKontrolViewModel.cs`, `Kasa.App.Core.Tests/CekKasaKontrolMetniTests.cs` | Yeni / Değişir | Değişiklik geçmişi | 6 |
| `Kasa.Api/Servisler/CekBildirimleri.cs`, `BildirimServisi.cs`, `Kasa.App.Core/BildirimHedefi.cs` | Yeni / Değişir | Bildirim ve hedef | 7 |
| `Kasa.Api.Tests/CekBildirimTests.cs`, `BildirimTests.cs`, `Kasa.App.Core.Tests/BildirimHedefiTests.cs` | Yeni / Değişir | Bildirim testleri | 7 |
| `Kasa.Api.Tests/CekOzetTests.cs` | Yeni | Panel özeti | 8 |
| `Kasa.ApiClient/CekDtos.cs`, `KasaApiClient.Cekler.cs`, `Kasa.ApiClient.Tests/CekApiTests.cs` | Yeni | İstemci | 9 |
| `Kasa.Sozlesme.Tests/SozlesmeTemeli.cs`, `CekSozlesmeTests.cs` | Değişir / Yeni | Sözleşme | 9 |
| `Kasa.App.Core/CekTakipModelleri.cs`, `CekTakipViewModel.cs`, `CekOzetViewModel.cs`, `Kasa.App.Core.Tests/CekTakipViewModelTests.cs` | Yeni | Masaüstü modeli | 10 |
| `Kasa.App.Core/Rol.cs`, `MenuModeli.cs`, `RolTests.cs`, `MenuModeliTests.cs` | Değişir | Bölüm, menü | 11 |
| `Kasa.App/Views/CekTakipPage.cs`, `AppShell.xaml(.cs)`, `MauiProgram.cs`, `Views/PanelPage.xaml.cs` | Yeni / Değişir | Sayfa, kabuk, DI, panel kutusu | 11 |
| `docs/deploy/kasa-2.4.md`, `docs/specs/2026-10-01-cekler.md` | Değişir | Sürüm notu, migration kaydı | 12 |

---
## Görev 1: Kasa.Core — kod değerleri, durum, geçiş, kalan ve türetme

**Dosyalar:**
- Değiştir: `Kasa.Core/Kodlar.cs` (`KasaHareketTurleri` sonuna `Cek`; dosya sonuna altı sınıf)
- Oluştur: `Kasa.Core/Cek.cs`
- Değiştir: `Kasa.Core.Tests/KodlarTests.cs`, `Kasa.Sozlesme.Tests/MimariTests.cs`
- Oluştur: `Kasa.Core.Tests/CekKurallariTests.cs`, `Kasa.Core.Tests/CekTureticiTests.cs`

- [ ] **Adım 1: Kural testlerini yaz.** `Kasa.Core.Tests/CekKurallariTests.cs`:

```csharp
using Kasa.Core.Kodlar;

namespace Kasa.Core.Tests;

/// <summary>Çek durumu, kalan, geçiş tablosu ve hareket kuralları (docs/specs/2026-10-01-cekler.md "Durum").</summary>
public class CekKurallariTests
{
    private static readonly DateOnly Gun = new(2026, 10, 1);
    private const string A = CekYonleri.Alinan;
    private const string V = CekYonleri.Verilen;

    private static CekHareketi H(int sira, string tur, decimal tutar = 0, decimal? net = null, string? karsi = null, int gun = 0)
        => new(sira, sira, tur, Gun.AddDays(gun), tutar, net, "MEZAT", karsi);

    [Fact]
    public void Hareketsiz_cek_portfoyde_kalan_tutarin_tamami()
    {
        var d = CekKurallari.Durum(A, 50_000m, []);
        Assert.Equal((CekDurumlari.Portfoyde, 50_000m, 0m, true, false), (d.Durum, d.Kalan, d.Odenen, d.Acik, d.Kapali));
    }

    [Fact]
    public void Kismi_tahsil_kismen_tahsil_edildi_kalan_sifirlaninca_tahsil_edildi()
    {
        var kismi = CekKurallari.Durum(A, 50_000m, [H(1, CekHareketTurleri.Tahsilat, 20_000m)]);
        Assert.Equal((CekDurumlari.KismenTahsilEdildi, 30_000m, true), (kismi.Durum, kismi.Kalan, kismi.Acik));
        var tam = CekKurallari.Durum(A, 50_000m, [H(1, CekHareketTurleri.Tahsilat, 20_000m), H(2, CekHareketTurleri.Tahsilat, 30_000m)]);
        Assert.Equal((CekDurumlari.TahsilEdildi, 0m, true), (tam.Durum, tam.Kalan, tam.Kapali));
        var verilen = CekKurallari.Durum(V, 10m, [H(1, CekHareketTurleri.Odeme, 4m)]);
        Assert.Equal((CekDurumlari.KismenOdendi, 6m), (verilen.Durum, verilen.Kalan));
        Assert.Equal(CekDurumlari.Odendi, CekKurallari.Durum(V, 10m, [H(1, CekHareketTurleri.Odeme, 10m)]).Durum);
    }

    [Fact]
    public void Ciro_kirdirma_donus_karsiliksiz_ve_iade_durumlari()
    {
        var ciro = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "Mehmet")]);
        Assert.Equal((CekDurumlari.CiroEdildi, 0m, true), (ciro.Durum, ciro.Kalan, ciro.Kapali));
        Assert.Equal(1, ciro.AcikDevir!.Id);
        var kirdirma = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Kirdirma, 100m, 95m, "Banka")]);
        Assert.Equal(CekDurumlari.Kirdirildi, kirdirma.Durum);
        var donus = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "Mehmet"), H(2, CekHareketTurleri.Donus, 100m)]);
        Assert.Equal((CekDurumlari.Karsiliksiz, 100m, (CekHareketi?)null, false, false), (donus.Durum, donus.Kalan, donus.AcikDevir, donus.Acik, donus.Kapali));
        var gecTahsil = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Karsiliksiz), H(2, CekHareketTurleri.Tahsilat, 40m)]);
        Assert.Equal((CekDurumlari.Karsiliksiz, 60m), (gecTahsil.Durum, gecTahsil.Kalan));
        Assert.Equal(CekDurumlari.TahsilEdildi, CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Karsiliksiz), H(2, CekHareketTurleri.Tahsilat, 100m)]).Durum);
        var iade = CekKurallari.Durum(A, 100m, [H(1, CekHareketTurleri.Tahsilat, 10m), H(2, CekHareketTurleri.Iade)]);
        Assert.Equal((CekDurumlari.IadeEdildi, 0m, true), (iade.Durum, iade.Kalan, iade.Kapali));
    }

    [Fact]
    public void Gecis_tablosu_izinli_hareketleri_verir()
    {
        Assert.Equal([CekHareketTurleri.Tahsilat, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade],
            CekKurallari.IzinliHareketler(A, 100m, []));
        // Ciro ve kırdırma yalnız hiç tahsilat yokken.
        Assert.Equal([CekHareketTurleri.Tahsilat, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade],
            CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Tahsilat, 10m)]));
        Assert.Equal([CekHareketTurleri.Odeme, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade], CekKurallari.IzinliHareketler(V, 100m, []));
        Assert.Equal([CekHareketTurleri.Donus], CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "X")]));
        Assert.Equal([CekHareketTurleri.Donus], CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Kirdirma, 100m, 90m, "B")]));
        Assert.Equal([CekHareketTurleri.Tahsilat, CekHareketTurleri.Iade], CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Karsiliksiz)]));
        Assert.Equal([CekHareketTurleri.Odeme, CekHareketTurleri.Iade], CekKurallari.IzinliHareketler(V, 100m, [H(1, CekHareketTurleri.Karsiliksiz)]));
        Assert.Empty(CekKurallari.IzinliHareketler(A, 100m, [H(1, CekHareketTurleri.Tahsilat, 100m)]));
        Assert.Empty(CekKurallari.IzinliHareketler(V, 100m, [H(1, CekHareketTurleri.Iade)]));
    }

    [Fact]
    public void Hareket_kurallari_tutar_tarih_ve_gecis_ihlalini_turkce_iletir()
    {
        string? Hata(string yon, IReadOnlyList<CekHareketi> once, CekHareketi yeni) => CekKurallari.HareketHatasi(yon, 100m, once, yeni);
        Assert.Null(Hata(A, [], H(1, CekHareketTurleri.Tahsilat, 40m)));
        Assert.Equal("Tutar sıfırdan büyük olmalı ve kalan tutarı (100,00 TL) aşamaz.", Hata(A, [], H(1, CekHareketTurleri.Tahsilat, 100.01m)));
        Assert.Equal("Tutar sıfırdan büyük olmalı ve kalan tutarı (60,00 TL) aşamaz.", Hata(A, [H(1, CekHareketTurleri.Tahsilat, 40m)], H(2, CekHareketTurleri.Tahsilat, 0m)));
        Assert.Equal("Bu kayıt kısmen tahsil edildi; şu an yalnız şu hareketler girilebilir: Tahsilat, Karşılıksız, İade.",
            Hata(A, [H(1, CekHareketTurleri.Tahsilat, 40m)], H(2, CekHareketTurleri.Ciro, 60m, karsi: "Ali")));
        Assert.Equal("Bu kayıt tahsil edildi; yeni hareket girilemez. Gerekirse son hareketi geri alın.",
            Hata(A, [H(1, CekHareketTurleri.Tahsilat, 100m)], H(2, CekHareketTurleri.Iade)));
        Assert.Equal("Ciroda tutar kalan tutarın tamamı (100,00 TL) olmalı.", Hata(A, [], H(1, CekHareketTurleri.Ciro, 50m, karsi: "Ali")));
        Assert.Equal("Ciroda ciro edilen kişiyi, kırdırmada banka ya da faktoring adını yazın.", Hata(A, [], H(1, CekHareketTurleri.Ciro, 100m)));
        Assert.Null(Hata(A, [], H(1, CekHareketTurleri.Kirdirma, 100m, 97.5m, "Faktoring A.Ş.")));
        Assert.Equal("Hesaba geçen tutar 0 ile çek tutarı arasında olmalı.", Hata(A, [], H(1, CekHareketTurleri.Kirdirma, 100m, 100.01m, "Banka")));
        Assert.Equal("Hesaba geçen tutar 0 ile çek tutarı arasında olmalı.", Hata(A, [], H(1, CekHareketTurleri.Kirdirma, 100m, null, "Banka")));
        Assert.Equal("Hesaba geçen tutar yalnız kırdırmada girilir.", Hata(A, [], H(1, CekHareketTurleri.Tahsilat, 10m, 5m)));
        Assert.Equal("Dönüş tutarı ciro ya da kırdırma tutarına (100,00 TL) eşit olmalı.",
            Hata(A, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "Ali")], H(2, CekHareketTurleri.Donus, 90m)));
        Assert.Null(Hata(A, [H(1, CekHareketTurleri.Ciro, 100m, karsi: "Ali")], H(2, CekHareketTurleri.Donus, 100m)));
        Assert.Equal("Karşılıksız ve iade hareketinde tutar girilmez.", Hata(V, [], H(1, CekHareketTurleri.Karsiliksiz, 5m)));
        Assert.Equal("Hareket tarihi önceki hareketin tarihinden (03.10.2026) önce olamaz.",
            Hata(A, [H(1, CekHareketTurleri.Tahsilat, 10m, gun: 2)], H(2, CekHareketTurleri.Tahsilat, 10m, gun: 1)));
        Assert.Equal("Tutar en fazla iki ondalık basamak içerebilir.", Hata(A, [], H(1, CekHareketTurleri.Tahsilat, 1.001m)));
    }

    [Fact]
    public void Kasa_etkili_hareketler_ve_ayni_cek_karsilastirmasi()
    {
        Assert.All(new[] { CekHareketTurleri.Tahsilat, CekHareketTurleri.Odeme, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Donus },
            t => Assert.True(CekKurallari.KasaEtkili(t)));
        Assert.False(CekKurallari.KasaEtkili(CekHareketTurleri.Karsiliksiz));
        Assert.False(CekKurallari.KasaEtkili(CekHareketTurleri.Iade));
        Assert.True(CekKurallari.AyniCek(A, " Ziraat ", "AB-12", A, "ziraat", "ab-12"));
        Assert.True(CekKurallari.AyniCek(A, null, "7", A, "", "7"));
        Assert.False(CekKurallari.AyniCek(A, "Ziraat", "12", V, "Ziraat", "12"));
        Assert.False(CekKurallari.AyniCek(A, "Ziraat", "12", A, "Halk", "12"));
    }
}
```

- [ ] **Adım 2: Türetme testlerini yaz.** `Kasa.Core.Tests/CekTureticiTests.cs` (negatif Gelen ve gider, Ortak bölüşümü motorda):

```csharp
using Kasa.Core.Kodlar;

namespace Kasa.Core.Tests;

/// <summary>Çek hareketlerinden türetilen gelir/gider satırları ve hesap motorundaki etkileri (tasarım "Rapora etkisi"):
/// negatif Gelen ve negatif Cari gider motorda toplamlardan doğru düşer; Ortak kasalı ödeme aylık Ortak bölüşümüne girer.</summary>
public class CekTureticiTests
{
    private static readonly Kanal[] Kanallar = [new("MEZAT"), new("PERAKENDE")];
    private static readonly IReadOnlyList<Donem> Donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31));
    private static readonly CekBilgisi Alinan = new(7, CekTurleri.Cek, CekYonleri.Alinan, "12345", "Ahmet Yılmaz", 50_000m);
    private static readonly CekBilgisi Verilen = new(8, CekTurleri.Cek, CekYonleri.Verilen, "777", "Mehmet Ticaret", 30_000m);

    private static (IReadOnlyList<Gelen> G, IReadOnlyList<Islem> I) Turet(CekBilgisi cek, params CekHareketi[] h) => CekTuretici.Satirlar(cek, h, Donemler);

    private static decimal Kasa(IReadOnlyList<Gelen> g, IReadOnlyList<Islem> i) =>
        HesapMotoru.HaftalikHesapla(0m, Kanallar, i, g, Donemler)[^1].KasaDevir;

    [Fact]
    public void Tahsilat_tarihli_kanal_geliri_olur_anahtari_ve_aciklamasi_hareketi_gosterir()
    {
        var (g, i) = Turet(Alinan, new CekHareketi(31, 1, CekHareketTurleri.Tahsilat, new(2026, 9, 17), 20_000m, Kanal: "MEZAT"));
        Assert.Empty(i);
        var gelen = Assert.Single(g);
        Assert.Equal((new DateOnly(2026, 9, 14), "MEZAT", 20_000m, false), (gelen.DonemStart, gelen.Kanal, gelen.TutarTl, gelen.GenelGelir));
        Assert.Equal((new DateOnly(2026, 9, 17), "Cek:31", "Çek tahsili: Ahmet Yılmaz / 12345"), (gelen.Tarih, gelen.KaynakAnahtari, gelen.Aciklama));
        Assert.Equal(20_000m, Kasa(g, i));
    }

    [Fact]
    public void Odeme_cari_gider_olur_ortak_kasada_aylik_ortak_paya_bolunur()
    {
        var (g, i) = Turet(Verilen, new CekHareketi(40, 1, CekHareketTurleri.Odeme, new(2026, 9, 10), 30_000.01m, Kanal: KanalEtiketleri.Ortak));
        Assert.Empty(g);
        var gider = Assert.Single(i);
        Assert.Equal(("Mehmet Ticaret", KanalEtiketleri.Ortak, GiderTipi.Cari, "Çek ödemesi / 777", "Cek:40", "Cek:40"),
            (gider.Cari, gider.Kanal, gider.Tip, gider.Not, gider.Kaynak, gider.KaynakAnahtari));
        var ay = HesapMotoru.AylikHesapla(2026, 9, Kanallar, i, g, Donemler);
        Assert.Equal(new[] { 15_000.01m, 15_000m }, ay.Kanallar.Select(k => k.OrtakPay));
        Assert.Equal(-30_000.01m, Kasa(g, i));
    }

    [Fact]
    public void Ciro_ayni_kasada_gelir_ve_gider_olur_kasa_ve_ay_sonucu_degismez()
    {
        var (g, i) = Turet(Alinan, new CekHareketi(50, 1, CekHareketTurleri.Ciro, new(2026, 9, 21), 50_000m, Kanal: "PERAKENDE", Karsi: "Veli Toptan"));
        Assert.Equal(("PERAKENDE", 50_000m, "Cek:50", "Çek cirosu: Ahmet Yılmaz / 12345"), (g[0].Kanal, g[0].TutarTl, g[0].KaynakAnahtari, g[0].Aciklama));
        Assert.Equal(("Veli Toptan", "PERAKENDE", 50_000m, "Cek:50:gider", "Cek:50"), (i[0].Cari, i[0].Kanal, i[0].TutarTl, i[0].KaynakAnahtari, i[0].Kaynak));
        Assert.Equal(0m, Kasa(g, i));
        var ay = HesapMotoru.AylikHesapla(2026, 9, Kanallar, i, g, Donemler).Kanallar.Single(k => k.Kanal == "PERAKENDE");
        Assert.Equal((50_000m, 50_000m, 0m), (ay.Gelen, ay.CariGiden, ay.AySonucu));
    }

    [Fact]
    public void Kirdirma_cek_tutari_gelir_masraf_cari_gider_kasaya_net_girer()
    {
        var (g, i) = Turet(Alinan, new CekHareketi(60, 1, CekHareketTurleri.Kirdirma, new(2026, 9, 22), 50_000m, 48_750m, "MEZAT", "Faktoring A.Ş."));
        Assert.Equal(50_000m, Assert.Single(g).TutarTl);
        var masraf = Assert.Single(i);
        Assert.Equal(("Faktoring A.Ş.", 1_250m, "Çek kırdırma masrafı / 12345", "Cek:60:gider"), (masraf.Cari, masraf.TutarTl, masraf.Not, masraf.KaynakAnahtari));
        Assert.Equal(48_750m, Kasa(g, i));
        // Masrafsız kırdırma gider üretmez.
        Assert.Empty(Turet(Alinan, new CekHareketi(61, 1, CekHareketTurleri.Kirdirma, new(2026, 9, 22), 50_000m, 50_000m, "MEZAT", "Banka")).I);
    }

    [Fact]
    public void Cirodan_donus_negatif_gelen_ve_negatif_cari_gider_uretir_motor_toplamlardan_duser()
    {
        var ciro = new CekHareketi(70, 1, CekHareketTurleri.Ciro, new(2026, 9, 21), 50_000m, Kanal: "MEZAT", Karsi: "Veli Toptan");
        var donus = new CekHareketi(71, 2, CekHareketTurleri.Donus, new(2026, 10, 5), 50_000m, Kanal: "MEZAT");
        var (g, i) = Turet(Alinan, ciro, donus);
        Assert.Equal(new[] { 50_000m, -50_000m }, g.Select(x => x.TutarTl));
        Assert.Equal(new[] { 50_000m, -50_000m }, i.Select(x => x.TutarTl));
        Assert.Equal(("Veli Toptan", "Cek:71:gider", "Çek dönüşü / 12345"), (i[1].Cari, i[1].KaynakAnahtari, i[1].Not));
        Assert.Equal("Çek dönüşü: Ahmet Yılmaz / 12345", g[1].Aciklama);
        Assert.Equal(0m, Kasa(g, i));
        // Ekim: Gelen ve Cari gider eksi, ay sonucu sıfır.
        var ekim = HesapMotoru.AylikHesapla(2026, 10, Kanallar, i, g, Donemler).Kanallar.Single(k => k.Kanal == "MEZAT");
        Assert.Equal((-50_000m, -50_000m, 0m), (ekim.Gelen, ekim.CariGiden, ekim.AySonucu));
        var hafta = HesapMotoru.HaftalikHesapla(0m, Kanallar, i, g, Donemler).Single(h => h.Donem.Icerir(new(2026, 10, 5)));
        Assert.Equal((-50_000m, -50_000m, 0m), (hafta.ToplamGelen, hafta.ToplamGiden, hafta.KasaSonucu));
    }

    [Fact]
    public void Kirdirmadan_donus_yalniz_negatif_gelen_uretir_masraf_geri_alinmaz()
    {
        var (g, i) = Turet(Alinan,
            new CekHareketi(80, 1, CekHareketTurleri.Kirdirma, new(2026, 9, 22), 50_000m, 48_750m, "MEZAT", "Banka"),
            new CekHareketi(81, 2, CekHareketTurleri.Donus, new(2026, 10, 2), 50_000m, Kanal: "MEZAT"));
        Assert.Equal(new[] { 50_000m, -50_000m }, g.Select(x => x.TutarTl));
        Assert.Equal(1_250m, Assert.Single(i).TutarTl);
        Assert.Equal(-1_250m, Kasa(g, i));
        var dokum = HesapMotoru.KasaHareketleri(Kanallar, i, g, Donemler);
        Assert.Equal((-50_000m, -50_000m), (dokum.Single(h => h.KaynakAnahtari == "Cek:81").GenelKasaEtkisi, dokum.Single(h => h.KaynakAnahtari == "Cek:81").KanalEtkisi));
    }

    [Fact]
    public void Karsiliksiz_ve_iade_satir_uretmez_senet_ve_kasasiz_hareket_ayri_yazilir()
    {
        var (g, i) = Turet(Alinan, new CekHareketi(90, 1, CekHareketTurleri.Karsiliksiz, new(2026, 9, 2), 0m), new CekHareketi(91, 2, CekHareketTurleri.Iade, new(2026, 9, 3), 0m));
        Assert.Empty(g);
        Assert.Empty(i);
        var senet = Alinan with { Tur = CekTurleri.Senet };
        Assert.Equal("Senet tahsili: Ahmet Yılmaz / 12345",
            Assert.Single(Turet(senet, new CekHareketi(92, 1, CekHareketTurleri.Tahsilat, new(2026, 9, 2), 5m, Kanal: "MEZAT")).G).Aciklama);
        var kasasiz = Turet(Alinan, new CekHareketi(93, 1, CekHareketTurleri.Ciro, new(2026, 9, 2), 50_000m, Karsi: "X"));
        Assert.Equal((KanalEtiketleri.GenelKasa, true), (kasasiz.G[0].Kanal, kasasiz.G[0].GenelGelir));
        Assert.Equal((KanalEtiketleri.DagilimBekliyor, true), (kasasiz.I[0].Kanal, kasasiz.I[0].DagilimBekliyor));
        // Dönem dışı (takip başlangıcından önce) gelir üretilmez.
        Assert.Empty(Turet(Alinan, new CekHareketi(94, 1, CekHareketTurleri.Tahsilat, new(2026, 8, 31), 5m, Kanal: "MEZAT")).G);
    }
}
```

- [ ] **Adım 3: Kod değeri testlerini ekle.** `Kasa.Core.Tests/KodlarTests.cs` içinde `Kasa_hareket_turleri` testinin son satırını

```csharp
        ("KrediTaksidi", KasaHareketTurleri.KrediTaksidi), ("KartAySonu", KasaHareketTurleri.KartAySonu));
```

şununla değiştir:

```csharp
        ("KrediTaksidi", KasaHareketTurleri.KrediTaksidi), ("KartAySonu", KasaHareketTurleri.KartAySonu),
        ("Cek", KasaHareketTurleri.Cek));

    [Fact]
    public void Cek_kodlari() => Esit(
        ("Cek", CekTurleri.Cek), ("Senet", CekTurleri.Senet), ("Alinan", CekYonleri.Alinan), ("Verilen", CekYonleri.Verilen),
        ("Elde", CekKonumlari.Elde), ("BankadaTahsilde", CekKonumlari.BankadaTahsilde), ("Teminatta", CekKonumlari.Teminatta),
        ("Icrada", CekKonumlari.Icrada));

    [Fact]
    public void Cek_hareket_turleri() => Esit(
        ("Tahsilat", CekHareketTurleri.Tahsilat), ("Odeme", CekHareketTurleri.Odeme), ("Ciro", CekHareketTurleri.Ciro),
        ("Kirdirma", CekHareketTurleri.Kirdirma), ("Donus", CekHareketTurleri.Donus), ("Karsiliksiz", CekHareketTurleri.Karsiliksiz),
        ("Iade", CekHareketTurleri.Iade));

    [Fact]
    public void Cek_durumlari_ve_suzgecleri() => Esit(
        ("Portfoyde", CekDurumlari.Portfoyde), ("KismenTahsilEdildi", CekDurumlari.KismenTahsilEdildi), ("TahsilEdildi", CekDurumlari.TahsilEdildi),
        ("KismenOdendi", CekDurumlari.KismenOdendi), ("Odendi", CekDurumlari.Odendi), ("CiroEdildi", CekDurumlari.CiroEdildi),
        ("Kirdirildi", CekDurumlari.Kirdirildi), ("Karsiliksiz", CekDurumlari.Karsiliksiz), ("IadeEdildi", CekDurumlari.IadeEdildi),
        ("Portfoyde", CekSuzgecleri.Portfoyde), ("Karsiliksiz", CekSuzgecleri.Karsiliksiz), ("Kapanan", CekSuzgecleri.Kapanan),
        ("Hepsi", CekSuzgecleri.Hepsi));
```

- [ ] **Adım 4: Testlerin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~Cek|FullyQualifiedName~KodlarTests"`
Beklenen: derleme hatası (CS0103/CS0246: `CekKurallari`, `CekHareketi`, `CekTurleri` … bulunamadı).

- [ ] **Adım 5: Kod değerlerini ekle.** `Kasa.Core/Kodlar.cs` içinde `KasaHareketTurleri` sınıfının son sabitinin

```csharp
    public const string KartAySonu = "KartAySonu";
}
```

kapanışını şununla değiştir:

```csharp
    public const string KartAySonu = "KartAySonu";
    /// <summary>Çek ya da senet hareketinin türetilmiş satırı (tahsil, ödeme, ciro, kırdırma, dönüş).</summary>
    public const string Cek = "Cek";
}
```

Dosyanın sonuna ekle:

```csharp
/// <summary>Çek kaydının türü (CekEntity.Tur, CekYaz.Tur, CekDto.Tur). Senet çekle aynı kurallara tabidir.</summary>
public static class CekTurleri
{
    public const string Cek = "Cek";
    public const string Senet = "Senet";
}

/// <summary>Çekin yönü (CekEntity.Yon, CekYaz.Yon, CekDto.Yon; liste süzgecinin 'yon' parametresi).</summary>
public static class CekYonleri
{
    /// <summary>Müşteriden alınan: tahsil edilir, ciro edilir ya da kırdırılır.</summary>
    public const string Alinan = "Alinan";
    /// <summary>Tedarikçiye verilen: çekin kasasından ödenir.</summary>
    public const string Verilen = "Verilen";
}

/// <summary>Portföydeki alınan çekin yeri (CekEntity.Konum). Kasayı etkilemez; değişikliği hareket değildir.</summary>
public static class CekKonumlari
{
    public const string Elde = "Elde";
    public const string BankadaTahsilde = "BankadaTahsilde";
    public const string Teminatta = "Teminatta";
    public const string Icrada = "Icrada";
}

/// <summary>Çek hareketinin türü (CekHareketEntity.Tur, CekHareketYaz.Tur, CekHareketDto.Tur, CekDto.IzinliHareketler).</summary>
public static class CekHareketTurleri
{
    /// <summary>Alınan çekin tahsili (kısmi olabilir).</summary>
    public const string Tahsilat = "Tahsilat";
    /// <summary>Verilen çekin ödenmesi (kısmi olabilir).</summary>
    public const string Odeme = "Odeme";
    /// <summary>Alınan çekin başkasına ciro edilmesi (kalanın tamamı).</summary>
    public const string Ciro = "Ciro";
    /// <summary>Alınan çekin bankaya ya da faktoringe kırdırılması (iskonto).</summary>
    public const string Kirdirma = "Kirdirma";
    /// <summary>Ciro edilen ya da kırdırılan çekin karşılıksız dönmesi.</summary>
    public const string Donus = "Donus";
    public const string Karsiliksiz = "Karsiliksiz";
    /// <summary>Çekin sahibine geri verilmesi.</summary>
    public const string Iade = "Iade";
}

/// <summary>Çekin durumu (CekDto.Durum; saklanmaz, hareketlerden hesaplanır: Kasa.Core.CekKurallari.Durum).</summary>
public static class CekDurumlari
{
    public const string Portfoyde = "Portfoyde";
    public const string KismenTahsilEdildi = "KismenTahsilEdildi";
    public const string TahsilEdildi = "TahsilEdildi";
    public const string KismenOdendi = "KismenOdendi";
    public const string Odendi = "Odendi";
    public const string CiroEdildi = "CiroEdildi";
    public const string Kirdirildi = "Kirdirildi";
    public const string Karsiliksiz = "Karsiliksiz";
    public const string IadeEdildi = "IadeEdildi";
}

/// <summary>Çek listesinin durum süzgeci (GET /api/takip/cekler 'durum' parametresi, masaüstü durum çipleri).</summary>
public static class CekSuzgecleri
{
    /// <summary>Portföyde ve kısmen tahsil edilmiş ya da ödenmiş.</summary>
    public const string Portfoyde = "Portfoyde";
    public const string Karsiliksiz = "Karsiliksiz";
    /// <summary>Tahsil edildi, ödendi, ciro edildi, kırdırıldı ya da iade edildi.</summary>
    public const string Kapanan = "Kapanan";
    public const string Hepsi = "Hepsi";
}
```

- [ ] **Adım 6: Kuralları ve türeticiyi yaz.** `Kasa.Core/Cek.cs`:

```csharp
using System.Globalization;
using Kasa.Core.Kodlar;

namespace Kasa.Core;

/// <summary>Çek ya da senedin türetme için gereken alanları (docs/specs/2026-10-01-cekler.md).</summary>
public sealed record CekBilgisi(int Id, string Tur, string Yon, string No, string Kisi, decimal Tutar);

/// <summary>Çek hareketi. <paramref name="Kanal"/>: kasayı etkileyen hareketin kasası — gerçek kanal adı ya da (yalnız verilen
/// çekin ödemesinde) <see cref="KanalEtiketleri.Ortak"/>; kasası çözülemeyen harekette null. <paramref name="NetTutar"/> yalnız
/// kırdırmada hesaba geçen tutardır. <paramref name="Karsi"/>: ciroda ciro edilen kişi, kırdırmada banka ya da faktoring adı.</summary>
public sealed record CekHareketi(int Id, int Sira, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar = null, string? Kanal = null, string? Karsi = null);

/// <summary>Hareketlerden hesaplanan durum. <paramref name="Kalan"/>: tahsil ya da ödeme bekleyen tutar (ciro edilmiş, kırdırılmış
/// ve iade edilmiş çekte 0). <paramref name="Odenen"/>: tahsilat ya da ödeme toplamı. <paramref name="AcikDevir"/>: dönüşü
/// girilmemiş son ciro ya da kırdırma; yoksa null.</summary>
public sealed record CekDurumu(string Durum, decimal Kalan, decimal Odenen, CekHareketi? AcikDevir)
{
    /// <summary>Portföyde ya da kısmen tahsil edilmiş / ödenmiş: bildirim, panel ve "Portföyde" süzgecindeki çek.</summary>
    public bool Acik => Durum is CekDurumlari.Portfoyde or CekDurumlari.KismenTahsilEdildi or CekDurumlari.KismenOdendi;

    /// <summary>Tahsil edildi, ödendi, ciro edildi, kırdırıldı ya da iade edildi ("Kapananlar" süzgeci).</summary>
    public bool Kapali => Durum is CekDurumlari.TahsilEdildi or CekDurumlari.Odendi or CekDurumlari.CiroEdildi
        or CekDurumlari.Kirdirildi or CekDurumlari.IadeEdildi;
}

/// <summary>
/// Çek ve senet kuralları (tasarım "Durum" bölümü). Durum saklanmaz, hareketlerin sırasından hesaplanır. Geçiş tablosu:
/// portföyde/kısmen alınan çekte Tahsilat, Ciro ve Kırdırma (yalnız hiç tahsilat yokken), Karşılıksız, İade; verilende Ödeme,
/// Karşılıksız, İade. Ciro edilmiş ya da kırdırılmış çekte yalnız Dönüş (çek yeniden karşılıksız olur). Karşılıksız çekte
/// Tahsilat/Ödeme ve İade. Kapanmış çekte hareket girilmez, yalnız son hareket geri alınır.
/// </summary>
public static class CekKurallari
{
    private static readonly NumberFormatInfo TlBicimi = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NegativeSign = "-" };
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static string Tl(decimal tutar) => tutar.ToString("#,0.00", TlBicimi) + " TL";

    /// <summary>Kasayı etkileyen hareket: türetilmiş satır üretir ve ay kilidine takılır.</summary>
    public static bool KasaEtkili(string tur) => tur is CekHareketTurleri.Tahsilat or CekHareketTurleri.Odeme or CekHareketTurleri.Ciro
        or CekHareketTurleri.Kirdirma or CekHareketTurleri.Donus;

    public static CekDurumu Durum(string yon, decimal tutar, IReadOnlyList<CekHareketi> hareketler)
    {
        decimal odenen = 0;
        CekHareketi? devir = null;
        bool karsiliksiz = false, iade = false;
        foreach (var h in hareketler.OrderBy(h => h.Sira))
        {
            switch (h.Tur)
            {
                case CekHareketTurleri.Tahsilat or CekHareketTurleri.Odeme:
                    odenen += h.Tutar;
                    break;
                case CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma:
                    devir = h;
                    break;
                case CekHareketTurleri.Donus:
                    devir = null;
                    karsiliksiz = true;
                    break;
                case CekHareketTurleri.Karsiliksiz:
                    karsiliksiz = true;
                    break;
                case CekHareketTurleri.Iade:
                    iade = true;
                    break;
            }
        }
        var alinan = yon == CekYonleri.Alinan;
        var kalan = tutar - odenen;
        var durum = iade ? CekDurumlari.IadeEdildi
            : devir?.Tur == CekHareketTurleri.Ciro ? CekDurumlari.CiroEdildi
            : devir is not null ? CekDurumlari.Kirdirildi
            : kalan <= 0 ? (alinan ? CekDurumlari.TahsilEdildi : CekDurumlari.Odendi)
            : karsiliksiz ? CekDurumlari.Karsiliksiz
            : odenen > 0 ? (alinan ? CekDurumlari.KismenTahsilEdildi : CekDurumlari.KismenOdendi)
            : CekDurumlari.Portfoyde;
        return new(durum, iade || devir is not null ? 0 : Math.Max(0, kalan), odenen, devir);
    }

    /// <summary>Çekin şu anki durumunda girilebilecek hareket türleri (geçiş tablosu); kapanmış çekte boş.</summary>
    public static IReadOnlyList<string> IzinliHareketler(string yon, decimal tutar, IReadOnlyList<CekHareketi> hareketler)
    {
        var d = Durum(yon, tutar, hareketler);
        var alinan = yon == CekYonleri.Alinan;
        var odeme = alinan ? CekHareketTurleri.Tahsilat : CekHareketTurleri.Odeme;
        if (d.Acik)
            return alinan && d.Odenen == 0
                ? [odeme, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade]
                : [odeme, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade];
        return d.Durum switch
        {
            CekDurumlari.CiroEdildi or CekDurumlari.Kirdirildi => [CekHareketTurleri.Donus],
            CekDurumlari.Karsiliksiz => [odeme, CekHareketTurleri.Iade],
            _ => [],
        };
    }

    /// <summary>Yeni hareketin geçiş, tarih ve tutar kuralına aykırılığının iletisi; geçerliyse null. Kasa seçimi ve metin
    /// uzunlukları uçta denetlenir.</summary>
    public static string? HareketHatasi(string yon, decimal tutar, IReadOnlyList<CekHareketi> hareketler, CekHareketi yeni)
    {
        var d = Durum(yon, tutar, hareketler);
        var izinli = IzinliHareketler(yon, tutar, hareketler);
        if (!izinli.Contains(yeni.Tur))
            return izinli.Count == 0
                ? $"Bu kayıt {DurumAdi(d.Durum)}; yeni hareket girilemez. Gerekirse son hareketi geri alın."
                : $"Bu kayıt {DurumAdi(d.Durum)}; şu an yalnız şu hareketler girilebilir: {string.Join(", ", izinli.Select(HareketAdi))}.";
        if (hareketler.Count > 0 && yeni.Tarih < hareketler.Max(h => h.Tarih))
            return $"Hareket tarihi önceki hareketin tarihinden ({hareketler.Max(h => h.Tarih).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}) önce olamaz.";
        if (decimal.Round(yeni.Tutar, 2) != yeni.Tutar || yeni.NetTutar is { } n && decimal.Round(n, 2) != n)
            return "Tutar en fazla iki ondalık basamak içerebilir.";
        if (yeni.Tur != CekHareketTurleri.Kirdirma && yeni.NetTutar is not null)
            return "Hesaba geçen tutar yalnız kırdırmada girilir.";
        return yeni.Tur switch
        {
            CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma when string.IsNullOrWhiteSpace(yeni.Karsi)
                => "Ciroda ciro edilen kişiyi, kırdırmada banka ya da faktoring adını yazın.",
            CekHareketTurleri.Tahsilat or CekHareketTurleri.Odeme when yeni.Tutar <= 0 || yeni.Tutar > d.Kalan
                => $"Tutar sıfırdan büyük olmalı ve kalan tutarı ({Tl(d.Kalan)}) aşamaz.",
            CekHareketTurleri.Ciro when yeni.Tutar != d.Kalan
                => $"Ciroda tutar kalan tutarın tamamı ({Tl(d.Kalan)}) olmalı.",
            CekHareketTurleri.Kirdirma when yeni.Tutar != d.Kalan
                => $"Kırdırmada tutar çek tutarının tamamı ({Tl(d.Kalan)}) olmalı.",
            CekHareketTurleri.Kirdirma when yeni.NetTutar is not { } net || net < 0 || net > yeni.Tutar
                => "Hesaba geçen tutar 0 ile çek tutarı arasında olmalı.",
            CekHareketTurleri.Donus when yeni.Tutar != d.AcikDevir!.Tutar
                => $"Dönüş tutarı ciro ya da kırdırma tutarına ({Tl(d.AcikDevir!.Tutar)}) eşit olmalı.",
            CekHareketTurleri.Karsiliksiz or CekHareketTurleri.Iade when yeni.Tutar != 0
                => "Karşılıksız ve iade hareketinde tutar girilmez.",
            _ => null,
        };
    }

    /// <summary>Aynı çek: aynı yön, banka ve numara (baştaki ve sondaki boşluk ile büyük-küçük harf farkı yok sayılır; iki
    /// banka da boşsa eşittir). Aynı çek uyarısı kaydı engellemez.</summary>
    public static bool AyniCek(string yon1, string? banka1, string no1, string yon2, string? banka2, string no2)
        => yon1 == yon2 && Esit(banka1, banka2) && Esit(no1, no2);

    private static bool Esit(string? a, string? b) => string.Compare((a ?? "").Trim(), (b ?? "").Trim(), Tr, CompareOptions.IgnoreCase) == 0;

    /// <summary>Durumun görünen adı (küçük harfle; cümle içinde ve listede kullanılır).</summary>
    public static string DurumAdi(string durum) => durum switch
    {
        CekDurumlari.Portfoyde => "portföyde",
        CekDurumlari.KismenTahsilEdildi => "kısmen tahsil edildi",
        CekDurumlari.TahsilEdildi => "tahsil edildi",
        CekDurumlari.KismenOdendi => "kısmen ödendi",
        CekDurumlari.Odendi => "ödendi",
        CekDurumlari.CiroEdildi => "ciro edildi",
        CekDurumlari.Kirdirildi => "kırdırıldı",
        CekDurumlari.Karsiliksiz => "karşılıksız",
        CekDurumlari.IadeEdildi => "iade edildi",
        _ => durum,
    };

    /// <summary>Hareket türünün görünen adı.</summary>
    public static string HareketAdi(string tur) => tur switch
    {
        CekHareketTurleri.Tahsilat => "Tahsilat",
        CekHareketTurleri.Odeme => "Ödeme",
        CekHareketTurleri.Ciro => "Ciro",
        CekHareketTurleri.Kirdirma => "Kırdırma",
        CekHareketTurleri.Donus => "Dönüş",
        CekHareketTurleri.Karsiliksiz => "Karşılıksız",
        CekHareketTurleri.Iade => "İade",
        _ => tur,
    };
}

/// <summary>
/// Çek hareketlerini sentetik <see cref="Gelen"/>/<see cref="Islem"/> satırlarına çevirir (tasarım "Rapora etkisi"; kredi
/// taksitlerindeki desen). Saf: EF/I-O yok; satırlar yalnız hesap motoruna beslenir, veritabanına yazılmaz. Gelirin döküm
/// anahtarı "Cek:{hareketId}", ciro/kırdırma/dönüşün gider ayağı "Cek:{hareketId}:gider"; iki ayak da kaynak kaydı
/// (<see cref="Islem.Kaynak"/>) "Cek:{hareketId}" taşır. Gelir, tarihini içeren dönemi bulamazsa (takip başlangıcından önce ya da
/// ufkun ötesinde) üretilmez. Kasası çözülemeyen hareketin (<see cref="CekHareketi.Kanal"/> null) geliri genel kasaya, gideri
/// "Dağılım bekliyor"a yazılır.
/// </summary>
public static class CekTuretici
{
    public static (IReadOnlyList<Gelen> Gelenler, IReadOnlyList<Islem> Islemler) Satirlar(CekBilgisi cek, IReadOnlyList<CekHareketi> hareketler, IReadOnlyList<Donem> donemler)
    {
        var gelenler = new List<Gelen>();
        var islemler = new List<Islem>();
        var ad = cek.Tur switch { CekTurleri.Senet => "Senet", _ => "Çek" };
        CekHareketi? devir = null;
        foreach (var h in hareketler.OrderBy(h => h.Sira))
        {
            var anahtar = "Cek:" + h.Id.ToString(CultureInfo.InvariantCulture);
            void Gelir(decimal tutar, string aciklama)
            {
                if (donemler.FirstOrDefault(d => d.Icerir(h.Tarih)) is not { } donem)
                    return;
                gelenler.Add(h.Kanal is null
                    ? new Gelen(donem.Start, KanalEtiketleri.GenelKasa, tutar, GenelGelir: true) { Tarih = h.Tarih, KaynakAnahtari = anahtar, Aciklama = aciklama }
                    : new Gelen(donem.Start, h.Kanal, tutar) { Tarih = h.Tarih, KaynakAnahtari = anahtar, Aciklama = aciklama });
            }
            void Gider(string cari, decimal tutar, string not, string kaynakAnahtari) =>
                islemler.Add(new Islem(h.Tarih, cari, tutar, h.Kanal ?? KanalEtiketleri.DagilimBekliyor, GiderTipi.Cari, not, DagilimBekliyor: h.Kanal is null)
                { Kaynak = anahtar, KaynakAnahtari = kaynakAnahtari });
            switch (h.Tur)
            {
                case CekHareketTurleri.Tahsilat:
                    Gelir(h.Tutar, $"{ad} tahsili: {cek.Kisi} / {cek.No}");
                    break;
                case CekHareketTurleri.Odeme:
                    Gider(cek.Kisi, h.Tutar, $"{ad} ödemesi / {cek.No}", anahtar);
                    break;
                case CekHareketTurleri.Ciro:
                    devir = h;
                    Gelir(h.Tutar, $"{ad} cirosu: {cek.Kisi} / {cek.No}");
                    Gider(h.Karsi ?? "", h.Tutar, $"{ad} cirosu / {cek.No}", anahtar + ":gider");
                    break;
                case CekHareketTurleri.Kirdirma:
                    devir = h;
                    Gelir(h.Tutar, $"{ad} kırdırma: {cek.Kisi} / {cek.No}");
                    if (h.Tutar - (h.NetTutar ?? h.Tutar) is > 0 and var masraf)
                        Gider(h.Karsi ?? "", masraf, $"{ad} kırdırma masrafı / {cek.No}", anahtar + ":gider");
                    break;
                case CekHareketTurleri.Donus:
                    Gelir(-h.Tutar, $"{ad} dönüşü: {cek.Kisi} / {cek.No}");
                    if (devir?.Tur == CekHareketTurleri.Ciro)
                        Gider(devir.Karsi ?? "", -h.Tutar, $"{ad} dönüşü / {cek.No}", anahtar + ":gider");
                    devir = null;
                    break;
            }
        }
        return (gelenler, islemler);
    }
}
```

- [ ] **Adım 7: Çok anlamlı kod değerlerini işaretle.** `Kasa.Sozlesme.Tests/MimariTests.cs` içinde `CokAnlamliDegerler`'in özetinin son
  satırını ve kümesini

```csharp
    /// sözlük anahtarı); "Iptal": alan adı (kilit kuralı, denetim alanları); "Gelir", "Gider", "Kart", "Kredi", "Banka": görünen
    /// metin (seçenek adı, ad yedeği, alan etiketi).</summary>
    private static readonly HashSet<string> CokAnlamliDegerler = new(StringComparer.Ordinal)
    {
        "Gelir", "Gider", "KartHarcama", "KartOdeme", "AlisOdeme", "Islem", "Kart", "Kredi", "Banka", "Iptal",
    };
```

şununla değiştir:

```csharp
    /// sözlük anahtarı); "Iptal": alan adı (kilit kuralı, denetim alanları); "Gelir", "Gider", "Kart", "Kredi", "Banka": görünen
    /// metin (seçenek adı, ad yedeği, alan etiketi); "Cek": denetim varlık adı (CekEntity, KasaKontrolMetni'nde sözlük anahtarı);
    /// "Odeme", "Iade": ekstre metin okuyucusunun iç satır sınıfı (EkstreMetinOkuyucu).</summary>
    private static readonly HashSet<string> CokAnlamliDegerler = new(StringComparer.Ordinal)
    {
        "Gelir", "Gider", "KartHarcama", "KartOdeme", "AlisOdeme", "Islem", "Kart", "Kredi", "Banka", "Iptal", "Cek", "Odeme", "Iade",
    };
```

- [ ] **Adım 8: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:   127` (111 + 16).

Çalıştır: `dotnet test Kasa.Sozlesme.Tests/Kasa.Sozlesme.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MimariTests"`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:     7`.

- [ ] **Adım 9: Commit.**

```bash
git add Kasa.Core/Kodlar.cs Kasa.Core/Cek.cs Kasa.Core.Tests/KodlarTests.cs Kasa.Core.Tests/CekKurallariTests.cs Kasa.Core.Tests/CekTureticiTests.cs Kasa.Sozlesme.Tests/MimariTests.cs
git commit -F - <<'MESAJ'
feat(core): çek ve senet kuralları ve türetici

Durum hareketlerden hesaplanır; geçiş tablosu, kalan, tarih sırası ve tutar kuralları
CekKurallari'nda. CekTuretici hareketleri sentetik Gelen/Islem satırına çevirir
(dönüşte negatif Gelen ve negatif Cari gider; motor ikisini de toplamdan düşer).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 2: EF varlıkları, migration ve kanal silme engeli

**Dosyalar:**
- Oluştur: `Kasa.Api/Data/CekEntities.cs`, `Kasa.Api/Data/KasaDbContext.Cekler.cs`, `Kasa.Api/Migrations/20261008000100_Cekler.cs`,
  `Kasa.Api/Migrations/CeklerSchemaModel.cs`, `Kasa.Api.Tests/CekVeriModeliTests.cs`
- Değiştir: `Kasa.Api/Data/KasaDbContext.cs`, `Kasa.Api/Migrations/KasaDbContextModelSnapshot.cs`, `Kasa.Api/KanalKurallari.cs`,
  `Kasa.Api.Tests/BelgeDeposuGecisTests.cs`, `Kasa.Api.Tests/EskiGelirTests.cs`, `Kasa.Api.Tests/VeritabaniGocuTests.cs`

- [ ] **Adım 1: Veri modeli testini yaz.** `Kasa.Api.Tests/CekVeriModeliTests.cs` (sonraki görevler `Cek(...)` yardımcısını kullanır):

```csharp
using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek veri modeli (migration 20261008000100_Cekler): iki tablo, hareket sırası tekil, çek ya da hareketi olan kanal
/// silinmez (KanalKurallari.SilmeEngeli; veritabanında ON DELETE RESTRICT).</summary>
public class CekVeriModeliTests
{
    internal static CekEntity Cek(string yon = CekYonleri.Alinan, decimal tutar = 50_000m, int? kanalId = null, bool teminat = false,
        string kisi = "Ahmet Yılmaz", string no = "12345", DateOnly? vade = null) => new()
        {
            Tur = CekTurleri.Cek,
            Yon = yon,
            No = no,
            Banka = "Ziraat",
            Kisi = kisi,
            Tutar = tutar,
            VadeTarihi = vade ?? new(2026, 11, 30),
            KanalId = kanalId,
            Teminat = teminat,
            Surum = 1,
        };

    [Fact]
    public async Task Cek_ve_hareketleri_kaydedilir_hareket_sirasi_cek_basina_tekildir()
    {
        await using var f = Fabrika();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var cek = Cek();
        db.Cekler.Add(cek);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.CekHareketler.Add(new CekHareketEntity { CekId = cek.Id, Sira = 1, Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 100.01m, KanalId = 1 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var okunan = await db.CekHareketler.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((cek.Id, 1, 100.01m, (decimal?)null, 1), (okunan.CekId, okunan.Sira, okunan.Tutar, okunan.NetTutar, okunan.KanalId));
        db.CekHareketler.Add(new CekHareketEntity { CekId = cek.Id, Sira = 1, Tur = CekHareketTurleri.Iade, Tarih = Today });
        var hata = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("UNIQUE", hata.InnerException!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cek_ya_da_hareketi_olan_kanal_silinmez(bool yalnizHareket)
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var kanal = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("GECICI", true, 9));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var cek = Cek(yalnizHareket ? CekYonleri.Alinan : CekYonleri.Verilen, kanalId: yalnizHareket ? null : kanal.Id);
            db.Cekler.Add(cek);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            if (yalnizHareket)
            {
                db.CekHareketler.Add(new CekHareketEntity { CekId = cek.Id, Sira = 1, Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 10m, KanalId = kanal.Id });
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }
        var yanit = await c.DeleteAsync($"/api/kanallar/{kanal.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
        Assert.Contains("olan kanal silinemez", await yanit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }
}
```

- [ ] **Adım 2: Migration sayısı testlerini 25'e çıkar.** Üç dosyada `GetAppliedMigrations().Count()` beklenen değeri 24 → 25
  (BelgeDeposuGecisTests 1, EskiGelirTests 2, VeritabaniGocuTests 13 yer):

```bash
grep -rl "Assert.Equal(24, " Kasa.Api.Tests | xargs sed -i 's/Assert.Equal(24, \(db\|verify\)\.Database\.GetAppliedMigrations/Assert.Equal(25, \1.Database.GetAppliedMigrations/g'
grep -rn "GetAppliedMigrations().Count()" Kasa.Api.Tests | grep -vc "Equal(25"
```

Beklenen son satır: `0`. `Kasa.Api.Tests/BelgeDeposuGecisTests.cs` içindeki bekleyen migration listesinin sonuna yeni kimliği ekle:

```csharp
            Assert.Equal(new[] { BelgeDeposuHazirlik.Kimlik, BelgeDeposuGocu.Kimlik, EkstreEslesmesi.Kimlik, KasaKontrolFiligrani.Kimlik, CekirdekSurumleri.Kimlik, GeriYuklemeGuvenligi.Kimlik, EditorSifirlamaIzi.Kimlik, Kasa.Api.Migrations.Cekler.Kimlik }, db.Database.GetPendingMigrations());
```

- [ ] **Adım 3: Testlerin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekVeriModeliTests"`
Beklenen: derleme hatası (CS0246 `CekEntity`, CS1061 `Cekler`).

- [ ] **Adım 4: Varlıkları yaz.** `Kasa.Api/Data/CekEntities.cs`:

```csharp
namespace Kasa.Api.Data;

/// <summary>Alınan ya da verilen çek / senet (docs/specs/2026-10-01-cekler.md). Durum saklanmaz, hareketlerden hesaplanır
/// (<see cref="Kasa.Core.CekKurallari.Durum"/>). Kasayı yalnız hareketler etkiler (türetilmiş satır; HesapServisi).</summary>
public class CekEntity
{
    public int Id { get; set; }
    /// <summary><see cref="Kasa.Core.Kodlar.CekTurleri"/>.</summary>
    public string Tur { get; set; } = "";
    /// <summary><see cref="Kasa.Core.Kodlar.CekYonleri"/>.</summary>
    public string Yon { get; set; } = "";
    public string No { get; set; } = "";
    /// <summary>Çekte zorunlu, senette boş olabilir.</summary>
    public string? Banka { get; set; }
    /// <summary>Alınanda kimden (keşideci ya da ciro eden), verilende kime (lehtar).</summary>
    public string Kisi { get; set; } = "";
    public decimal Tutar { get; set; }
    public DateOnly VadeTarihi { get; set; }
    /// <summary>Verilen çekin ödeneceği kasa; verilende null Ortak'tır. Alınan çekte her zaman null (kasa harekette seçilir).</summary>
    public int? KanalId { get; set; }
    /// <summary>Teminat çeki: bildirim çıkmaz, panel toplamlarına girmez.</summary>
    public bool Teminat { get; set; }
    /// <summary>Portföydeki alınan çekin yeri (<see cref="Kasa.Core.Kodlar.CekKonumlari"/>); verilende null.</summary>
    public string? Konum { get; set; }
    public string? Not { get; set; }
    /// <summary>İyimser eşzamanlılık: çek ya da hareketleri her değiştiğinde bir artar.</summary>
    public int Surum { get; set; }
}

/// <summary>Çekin hareketi (tahsilat, ödeme, ciro, kırdırma, dönüş, karşılıksız, iade). <see cref="Sira"/> çekin kaçıncı
/// hareketi olduğudur; yalnız son hareket silinebilir (geri alma).</summary>
public class CekHareketEntity
{
    public int Id { get; set; }
    public int CekId { get; set; }
    public int Sira { get; set; }
    /// <summary><see cref="Kasa.Core.Kodlar.CekHareketTurleri"/>.</summary>
    public string Tur { get; set; } = "";
    /// <summary>Hareketin günü; ay kilidi bu tarihe bakar.</summary>
    public DateOnly Tarih { get; set; }
    /// <summary>Tahsilat, ödeme ve dönüşte tutar; ciroda kalanın tamamı; kırdırmada çek tutarı; karşılıksız ve iadede 0.</summary>
    public decimal Tutar { get; set; }
    /// <summary>Yalnız kırdırmada hesaba geçen tutar; masraf = Tutar − NetTutar.</summary>
    public decimal? NetTutar { get; set; }
    /// <summary>Kasayı etkileyen hareketin kasası. Alınanda seçilir (dönüşte ters çevrilen hareketinki); verilen çekin ödemesinde
    /// çekin kasasının kopyasıdır ve null Ortak'tır. Karşılıksız ve iadede null.</summary>
    public int? KanalId { get; set; }
    /// <summary>Ciroda ciro edilen kişi, kırdırmada banka ya da faktoring adı; dönüşte ters çevrilen hareketinki.</summary>
    public string? Karsi { get; set; }
}
```

`Kasa.Api/Data/KasaDbContext.Cekler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public partial class KasaDbContext
{
    public DbSet<CekEntity> Cekler => Set<CekEntity>();
    public DbSet<CekHareketEntity> CekHareketler => Set<CekHareketEntity>();

    partial void ConfigureCekler(ModelBuilder b)
    {
        b.Entity<CekEntity>().ToTable("Cekler");
        b.Entity<CekEntity>().Property(c => c.Surum).IsConcurrencyToken();
        b.Entity<CekEntity>().HasOne<KanalEntity>().WithMany().HasForeignKey(c => c.KanalId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CekHareketEntity>().ToTable("CekHareketler");
        b.Entity<CekHareketEntity>().HasOne<CekEntity>().WithMany().HasForeignKey(h => h.CekId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CekHareketEntity>().HasOne<KanalEntity>().WithMany().HasForeignKey(h => h.KanalId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CekHareketEntity>().HasIndex(h => new { h.CekId, h.Sira }).IsUnique();
    }
}
```

`Kasa.Api/Data/KasaDbContext.cs` içinde `OnModelCreating`'in son çağrısından sonra ve parçalı bildirimlerin sonuna birer satır:

```csharp
        ConfigureSistemDurumu(b);
        ConfigureCekler(b);
    }
```

```csharp
    partial void ConfigureSistemDurumu(ModelBuilder b);
    partial void ConfigureCekler(ModelBuilder b);
}
```

- [ ] **Adım 5: Migration'ı yaz.** `Kasa.Api/Migrations/20261008000100_Cekler.cs`:

```csharp
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Çek ve senet takibi (docs/specs/2026-10-01-cekler.md). Yalnız iki boş tablo ve dizinlerini ekler: <c>Cekler</c>
/// (<see cref="CekEntity"/>) ve <c>CekHareketler</c> (<see cref="CekHareketEntity"/>). Mevcut satırlara dokunmaz; çek
/// girilmedikçe raporlar aynıdır. Kanal ve çek bağları ON DELETE RESTRICT: çeki ya da hareketi olan kanal silinemez.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class Cekler : Migration
{
    public const string Kimlik = "20261008000100_Cekler";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "Cekler" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, "Tur" TEXT NOT NULL, "Yon" TEXT NOT NULL,
          "No" TEXT NOT NULL, "Banka" TEXT NULL, "Kisi" TEXT NOT NULL, "Tutar" TEXT NOT NULL, "VadeTarihi" TEXT NOT NULL,
          "KanalId" INTEGER NULL REFERENCES "Kanallar" ("Id") ON DELETE RESTRICT, "Teminat" INTEGER NOT NULL, "Konum" TEXT NULL,
          "Not" TEXT NULL, "Surum" INTEGER NOT NULL);
        CREATE INDEX "IX_Cekler_KanalId" ON "Cekler" ("KanalId");
        CREATE TABLE "CekHareketler" ("Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
          "CekId" INTEGER NOT NULL REFERENCES "Cekler" ("Id") ON DELETE RESTRICT, "Sira" INTEGER NOT NULL, "Tur" TEXT NOT NULL,
          "Tarih" TEXT NOT NULL, "Tutar" TEXT NOT NULL, "NetTutar" TEXT NULL,
          "KanalId" INTEGER NULL REFERENCES "Kanallar" ("Id") ON DELETE RESTRICT, "Karsi" TEXT NULL);
        CREATE UNIQUE INDEX "IX_CekHareketler_CekId_Sira" ON "CekHareketler" ("CekId", "Sira");
        CREATE INDEX "IX_CekHareketler_KanalId" ON "CekHareketler" ("KanalId");
        """);
    // Tabloları düşürmek çek kayıtlarını ve türettikleri kasa hareketlerini siler; diğer migration'lar gibi otomatik geri alınmaz.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Çek tabloları mali geçmiş taşır; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => CeklerSchemaModel.Build(b);
}
```

`Kasa.Api/Migrations/CeklerSchemaModel.cs` (çalışma modeliyle birebir; `HasPendingModelChanges` false kalmalı):

```csharp
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class CeklerSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        EditorSifirlamaIziSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.CekEntity", e =>
        {
            e.ToTable("Cekler");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            foreach (var p in new[] { "Tur", "Yon", "No", "Kisi" })
                e.Property<string>(p).IsRequired().HasColumnType("TEXT");
            foreach (var p in new[] { "Banka", "Konum", "Not" })
                e.Property<string>(p).HasColumnType("TEXT");
            e.Property<decimal>("Tutar").HasColumnType("TEXT");
            e.Property<DateOnly>("VadeTarihi").HasColumnType("TEXT");
            e.Property<int?>("KanalId").HasColumnType("INTEGER");
            e.Property<bool>("Teminat").HasColumnType("INTEGER");
            e.Property<int>("Surum").IsConcurrencyToken().HasColumnType("INTEGER");
            e.HasIndex("KanalId");
            e.HasOne("Kasa.Api.Data.KanalEntity", null).WithMany().HasForeignKey("KanalId").OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity("Kasa.Api.Data.CekHareketEntity", e =>
        {
            e.ToTable("CekHareketler");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            foreach (var p in new[] { "CekId", "Sira" })
                e.Property<int>(p).HasColumnType("INTEGER");
            e.Property<string>("Tur").IsRequired().HasColumnType("TEXT");
            e.Property<DateOnly>("Tarih").HasColumnType("TEXT");
            e.Property<decimal>("Tutar").HasColumnType("TEXT");
            e.Property<decimal?>("NetTutar").HasColumnType("TEXT");
            e.Property<int?>("KanalId").HasColumnType("INTEGER");
            e.Property<string>("Karsi").HasColumnType("TEXT");
            e.HasIndex("CekId", "Sira").IsUnique();
            e.HasIndex("KanalId");
            e.HasOne("Kasa.Api.Data.CekEntity", null).WithMany().HasForeignKey("CekId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.KanalEntity", null).WithMany().HasForeignKey("KanalId").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
```

`Kasa.Api/Migrations/KasaDbContextModelSnapshot.cs` içinde `EditorSifirlamaIziSchemaModel.Build(modelBuilder)` → `CeklerSchemaModel.Build(modelBuilder)`:

```csharp
    protected override void BuildModel(ModelBuilder modelBuilder) => CeklerSchemaModel.Build(modelBuilder);
```

- [ ] **Adım 6: Çeki olan kanalın silinmesini engelle.** `Kasa.Api/KanalKurallari.cs` `SilmeEngeli` içinde

```csharp
            || db.HesapHareketler.Any(h => h.KanalId == id)
```

satırının altına ekle (ileti aynı kalır: çek de "kayıt"tır):

```csharp
            || db.Cekler.Any(c => c.KanalId == id) || db.CekHareketler.Any(h => h.KanalId == id)
```

- [ ] **Adım 7: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekVeriModeliTests|FullyQualifiedName~VeritabaniGocuTests|FullyQualifiedName~EskiGelirTests|FullyQualifiedName~BelgeDeposuGecisTests|FullyQualifiedName~SablonVeritabaniTests|FullyQualifiedName~GeriYuklemeTests"`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:    58`.

Çalıştır (tamamı, ≈ 4 dk): `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:  1226`.

- [ ] **Adım 8: Commit.**

```bash
git add Kasa.Api/Data/CekEntities.cs Kasa.Api/Data/KasaDbContext.Cekler.cs Kasa.Api/Data/KasaDbContext.cs Kasa.Api/Migrations/20261008000100_Cekler.cs Kasa.Api/Migrations/CeklerSchemaModel.cs Kasa.Api/Migrations/KasaDbContextModelSnapshot.cs Kasa.Api/KanalKurallari.cs Kasa.Api.Tests/CekVeriModeliTests.cs Kasa.Api.Tests/BelgeDeposuGecisTests.cs Kasa.Api.Tests/EskiGelirTests.cs Kasa.Api.Tests/VeritabaniGocuTests.cs
git commit -F - <<'MESAJ'
feat(api): çek tabloları ve migration

20261008000100_Cekler yalnız iki boş tablo ekler (Cekler, CekHareketler); mevcut
satırlara dokunmaz, Down çalışmaz (yedekle geri dönülür). Çeki ya da hareketi olan
kanal silinmez.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 3: Hesap servisi türetmesi, kasa dökümü ve altın test

**Dosyalar:**
- Değiştir: `Kasa.Api/Servisler/HesapServisi.cs`, `Kasa.Api/Servisler/KasaDokumu.cs`, `Kasa.Api/KasaKontrolDtos.cs`, `Kasa.Api/FinansHesaplari.cs`
- Oluştur: `Kasa.Api.Tests/CekRaporTests.cs`
- Değiştir: `Kasa.Api.Tests/AltinRaporTests.cs`, `Kasa.Api.Tests/TakipBaslangiciDegisikligiTests.cs`

- [ ] **Adım 1: Rapor testlerini yaz.** `Kasa.Api.Tests/CekRaporTests.cs` (kayıtlar veritabanına doğrudan yazılır; `CekEkle` ve `HareketEkle`
  sonraki görevlerde de kullanılır):

```csharp
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Çek hareketlerinin raporlara etkisi (docs/specs/2026-10-01-cekler.md "Rapora etkisi"): hareketler HesapServisi'nde türetilmiş
/// satıra çevrilir; haftalık, aylık, panel ve kasa dökümü onları kendiliğinden görür. Bugün 25 Eylül 2026 (sabit saat), takip
/// başlangıcı 1 Haziran 2026, açılış kasası 1.000; kanallar MEZAT (1), PERAKENDE (2), TOPTAN (3). Kayıtlar veritabanına doğrudan
/// yazılır (uçlar ayrı görevde sınanır).
/// </summary>
public class CekRaporTests
{
    private static readonly DateOnly Agustos = Month.AddMonths(-1);

    internal static async Task<CekEntity> CekEkle(KasaWebFactory f, CekEntity cek, params CekHareketEntity[] hareketler)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        db.Cekler.Add(cek);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await HareketEkle(f, cek.Id, hareketler);
        return cek;
    }

    internal static async Task HareketEkle(KasaWebFactory f, int cekId, params CekHareketEntity[] hareketler)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var sira = db.CekHareketler.Where(h => h.CekId == cekId).Select(h => (int?)h.Sira).Max() ?? 0;
        foreach (var h in hareketler)
        {
            h.CekId = cekId;
            h.Sira = ++sira;
            db.CekHareketler.Add(h);
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<JsonNode> Json(HttpClient c, string yol) => JsonNode.Parse(await c.GetStringAsync(yol, TestContext.Current.CancellationToken))!;
    private static string Aylik(DateOnly ay) => $"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}";
    private static JsonNode Kanal(JsonNode rapor, string ad) => rapor["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == ad)!;

    [Fact]
    public async Task Tahsilat_gunu_ve_kasasiyla_haftalik_aylik_panel_ve_dokume_girer()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var tahsilat = new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = new(2026, 9, 16), Tutar = 20_000m, KanalId = 1 };
        await CekEkle(f, CekVeriModeliTests.Cek(), tahsilat);

        var hafta = (await Json(c, "/api/rapor/haftalik")).AsArray().Single(h => (string)h!["donem"]!["start"]! == "2026-09-14")!;
        Assert.Equal((20_000m, 20_000m), ((decimal)hafta["toplamGelen"]!, (decimal)Kanal(hafta, "MEZAT")["gelen"]!));
        var eylul = await Json(c, Aylik(Month));
        Assert.Equal((20_000m, 20_000m), ((decimal)Kanal(eylul, "MEZAT")["gelen"]!, (decimal)Kanal(eylul, "MEZAT")["aySonucu"]!));
        var panel = await Panel(c);
        Assert.Equal((21_000m, 20_000m), (panel.GuncelKasa, panel.Kanallar.Single(k => k.KanalId == 1).Bakiye));
        var dokum = (await c.GetFromJsonAsync<KasaHareketleriDto>("/api/kasa-hareketleri?baslangic=2026-09-01", TestContext.Current.CancellationToken))!;
        var satir = Assert.Single(dokum.Hareketler);
        Assert.Equal((KasaHareketTurleri.Cek, "Çek tahsili: Ahmet Yılmaz / 12345", "MEZAT", 20_000m, "Cek:" + tahsilat.Id, false, new DateOnly(2026, 9, 16)),
            (satir.Tur, satir.Aciklama, satir.Kanal, satir.GenelKasaEtkisi, satir.KaynakAnahtari, satir.Otomatik, satir.EtkiTarihi));
    }

    [Fact]
    public async Task Ortak_kasali_verilen_cek_odemesi_cari_gider_olur_aylik_ortak_paya_bolunur()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, 30_000.01m, kisi: "Mehmet Ticaret", no: "777"),
            new CekHareketEntity { Tur = CekHareketTurleri.Odeme, Tarih = new(2026, 9, 10), Tutar = 30_000.01m });

        var eylul = await Json(c, Aylik(Month));
        Assert.Equal(new[] { 10_000.01m, 10_000m, 10_000m }, eylul["kanallar"]!.AsArray().Select(k => (decimal)k!["ortakPay"]!));
        var panel = await Panel(c);
        Assert.Equal(1_000m - 30_000.01m, panel.GuncelKasa);
        Assert.All(panel.Kanallar, k => Assert.Equal(0m, k.Bakiye));
        var satir = Assert.Single((await c.GetFromJsonAsync<KasaHareketleriDto>("/api/kasa-hareketleri?baslangic=2026-09-01", TestContext.Current.CancellationToken))!.Hareketler);
        Assert.Equal((KasaHareketTurleri.Cek, "Mehmet Ticaret · Çek ödemesi / 777", KanalEtiketleri.Ortak, -30_000.01m),
            (satir.Tur, satir.Aciklama, satir.Kanal, satir.GenelKasaEtkisi));
    }

    [Fact]
    public async Task Ciro_kasa_ve_ay_sonucunu_degistirmez_donus_iki_ters_satirla_geri_alir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekEkle(f, CekVeriModeliTests.Cek(),
            new CekHareketEntity { Tur = CekHareketTurleri.Ciro, Tarih = new(2026, 9, 15), Tutar = 50_000m, KanalId = 2, Karsi = "Veli Toptan" });
        Assert.Equal(1_000m, (await Panel(c)).GuncelKasa);
        var perakende = Kanal(await Json(c, Aylik(Month)), "PERAKENDE");
        Assert.Equal((50_000m, 50_000m, 0m), ((decimal)perakende["gelen"]!, (decimal)perakende["cariGiden"]!, (decimal)perakende["aySonucu"]!));

        await HareketEkle(f, cek.Id, new CekHareketEntity { Tur = CekHareketTurleri.Donus, Tarih = new(2026, 9, 24), Tutar = 50_000m, KanalId = 2, Karsi = "Veli Toptan" });
        Assert.Equal(1_000m, (await Panel(c)).GuncelKasa);
        perakende = Kanal(await Json(c, Aylik(Month)), "PERAKENDE");
        Assert.Equal((0m, 0m, 0m), ((decimal)perakende["gelen"]!, (decimal)perakende["cariGiden"]!, (decimal)perakende["aySonucu"]!));
        var dokum = (await c.GetFromJsonAsync<KasaHareketleriDto>("/api/kasa-hareketleri?baslangic=2026-09-01", TestContext.Current.CancellationToken))!;
        Assert.Equal(new[] { 50_000m, -50_000m, -50_000m, 50_000m }.Order(), dokum.Hareketler.Select(h => h.GenelKasaEtkisi).Order());
        Assert.All(dokum.Hareketler, h => Assert.Equal(KasaHareketTurleri.Cek, h.Tur));
    }

    [Fact]
    public async Task Kirdirma_kasaya_net_tutari_yazar_kirdirilan_cekin_donusu_tutari_geri_alir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekEkle(f, CekVeriModeliTests.Cek(),
            new CekHareketEntity { Tur = CekHareketTurleri.Kirdirma, Tarih = new(2026, 9, 15), Tutar = 50_000m, NetTutar = 48_750m, KanalId = 1, Karsi = "Faktoring A.Ş." });
        Assert.Equal(1_000m + 48_750m, (await Panel(c)).GuncelKasa);
        var mezat = Kanal(await Json(c, Aylik(Month)), "MEZAT");
        Assert.Equal((50_000m, 1_250m, 48_750m), ((decimal)mezat["gelen"]!, (decimal)mezat["cariGiden"]!, (decimal)mezat["aySonucu"]!));

        await HareketEkle(f, cek.Id, new CekHareketEntity { Tur = CekHareketTurleri.Donus, Tarih = new(2026, 9, 24), Tutar = 50_000m, KanalId = 1, Karsi = "Faktoring A.Ş." });
        Assert.Equal(1_000m - 1_250m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Kapatilmis_ay_dondurulmus_goruntuden_doner_sonraki_hareket_onu_degistirmez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekEkle(f, CekVeriModeliTests.Cek(),
            new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Agustos.AddDays(19), Tutar = 5_000m, KanalId = 1 });
        var canli = await c.GetStringAsync(Aylik(Agustos), TestContext.Current.CancellationToken);
        Assert.Equal(5_000m, (decimal)Kanal(JsonNode.Parse(canli)!, "MEZAT")["gelen"]!);
        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", TestContext.Current.CancellationToken))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, Agustos.Year, Agustos.Month, "Ay tamamlandı"));

        await HareketEkle(f, cek.Id, new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 1_000m, KanalId = 1 });
        Assert.Equal(AyRaporuAnlikGoruntusuTests.Dondurulmus(canli, HesapServisi.AcikAyKurali), await c.GetStringAsync(Aylik(Agustos), TestContext.Current.CancellationToken));
        Assert.Equal(1_000m, (decimal)Kanal(await Json(c, Aylik(Month)), "MEZAT")["gelen"]!);
        Assert.Equal(7_000m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Kasasi_cozulemeyen_hareket_raporu_dusurmez_genel_kasaya_yazilir_ve_uyarilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await CekEkle(f, CekVeriModeliTests.Cek(), new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = new(2026, 9, 16), Tutar = 300m });
        Assert.Equal(1_300m, (await Panel(c)).GuncelKasa);
        var eylul = await Json(c, Aylik(Month));
        Assert.Equal(300m, (decimal)eylul["genelGelir"]!);
        Assert.Contains("Çek hareketi #", (string)eylul["veriSagligiUyarisi"]!, StringComparison.Ordinal);
    }
}
```

- [ ] **Adım 2: "Kasayı etkilemeyen çek raporu değiştirmez" testini yaz.** `Kasa.Api.Tests/AltinRaporTests.cs` içinde `OnayliFarklar()`
  metodunun özetinden (`/// <summary>` + `/// Altın dosyaya (kural kararlarından önceki kodun çıktısı) göre onaylı farklar.`) hemen önce ekle:

```csharp
    /// <summary>Çek girilmediği sürece raporlar aynıdır (yukarıdaki test, tohumda çek yokken altın çıktıyı birebir korur); kasayı
    /// etkilemeyen çek kayıtları da (hareketsiz çek, teminat çeki, karşılıksız ve iade hareketi) hiçbir rapor ve okuma ucunu
    /// değiştirmez (docs/specs/2026-10-01-cekler.md).</summary>
    [Fact]
    public async Task Kasayi_etkilemeyen_cek_kayitlari_rapor_ve_okuma_uclarini_degistirmez()
    {
        await using var f = KasaWebFactory.Sabit(AltinTohum.Bugun);
        using var c = await f.EditorClientAsync();
        var tohum = await AltinTohum.Kur(f, c);
        var once = await AltinTohum.Yanitlar(c, tohum);

        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek());
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(teminat: true, no: "T-1"));
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "K-1"),
            new CekHareketEntity { Tur = CekHareketTurleri.Karsiliksiz, Tarih = AltinTohum.Bugun });
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, kanalId: 1, no: "V-1"),
            new CekHareketEntity { Tur = CekHareketTurleri.Iade, Tarih = AltinTohum.Bugun });

        var sonra = await AltinTohum.Yanitlar(c, tohum);
        foreach (var (uc, deger) in once)
            Assert.True(deger!.ToJsonString() == sonra[uc]!.ToJsonString(), $"{uc} kasayı etkilemeyen çek kaydıyla değişti.");
    }
```

- [ ] **Adım 3: Takip başlangıcı testini yaz.** `Kasa.Api.Tests/TakipBaslangiciDegisikligiTests.cs` başına `using Kasa.Core.Kodlar;`
  ekle ve `Iptal_edilmis_ekstre_kaydi_baslangici_sabitlemez` testinden önce:

```csharp
    [Fact]
    public async Task Hareketsiz_cek_baslangici_sabitlemez_cek_hareketi_sabitler()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek());
        (await Ayar(c, YeniBaslangic)).EnsureSuccessStatusCode();
        (await Ayar(c, Baslangic)).EnsureSuccessStatusCode();
        await CekRaporTests.HareketEkle(f, cek.Id, new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = new(2026, 9, 16), Tutar = 5_000m, KanalId = 1 });
        await Reddedilir(c, "çek hareketi");
    }
```

- [ ] **Adım 4: Testlerin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekRaporTests|FullyQualifiedName~TakipBaslangiciDegisikligiTests|FullyQualifiedName~AltinRaporTests"`
Beklenen: `CekRaporTests` (6) ve `Hareketsiz_cek_...` düşer (panel 1.000 kalır, döküm boş, ayar 200 döner); `AltinRaporTests` iki testi geçer.

- [ ] **Adım 5: Türetmeyi yaz.** `Kasa.Api/Servisler/HesapServisi.cs` `Yukle` sonunda

```csharp
        foreach (var k in karantina)
            VeriKarantinasi.Logla(_db, k.Anahtar, k.Aciklama, k.Hata);
```

satırlarından hemen önce ekle:

```csharp
        CekSatirlari(_db, donemler, gelenler, islemler, KanalAdi, (anahtar, aciklama, tarih) => Karantinaya(anahtar, aciklama, tarih, tarih));

```

ve `AyRaporu` metodunun özetinden (`/// <summary>` + `/// Ayın raporu, ayın kanal kümesiyle (core-1; …`) hemen önce ekle:

```csharp
    /// <summary>Kanal kimliğini adına çözer (Yukle'deki KanalAdi).</summary>
    private delegate bool KanalCozucu(int? kanalId, [NotNullWhen(true)] out string? ad);

    /// <summary>
    /// Çek ve senet hareketlerinin türetilmiş satırları (docs/specs/2026-10-01-cekler.md "Rapora etkisi"): kredi taksitlerindeki gibi
    /// bellekte türetilir (<see cref="CekTuretici"/>), veritabanına Islem/Gelen yazılmaz. Satırlar listelerin sonuna eklenir: çek
    /// yokken satırlar, sıraları ve tutarlar aynıdır (altın rapor testi). Verilen çekin ödemesinde kasası boş hareket Ortak'tır;
    /// kanalı çözülemeyen kasa etkili hareket karantinaya alınır, geliri genel kasaya, gideri "Dağılım bekliyor"a yazılır.
    /// </summary>
    private static void CekSatirlari(KasaDbContext db, IReadOnlyList<Donem> donemler, List<Gelen> gelenler, List<Islem> islemler, KanalCozucu kanalAdi,
        Action<string, Func<string>, DateOnly> karantinaya)
    {
        // Çek tabloları migration 20261008000100_Cekler ile gelir: göç öncesi şemadaki rapor (göç testleri) tablolar olmadan hesaplanır.
        if (db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='Cekler'").Single() == 0)
            return;
        var cekler = db.Cekler.AsNoTracking().OrderBy(c => c.Id).ToList();
        if (cekler.Count == 0)
            return;
        var hareketler = db.CekHareketler.AsNoTracking().ToList().ToLookup(h => h.CekId);
        foreach (var cek in cekler)
        {
            var satirlar = new List<CekHareketi>();
            foreach (var h in hareketler[cek.Id].OrderBy(h => h.Sira))
            {
                string? kanal = null;
                if (kanalAdi(h.KanalId, out var ad))
                    kanal = ad;
                else if (h.KanalId is null && h.Tur == CekHareketTurleri.Odeme)
                    kanal = KanalEtiketleri.Ortak;
                else if (CekKurallari.KasaEtkili(h.Tur))
                    karantinaya("CekHareketi:" + h.Id, () => $"Çek hareketi #{h.Id} (çek #{cek.Id}, {h.Tarih.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}): "
                        + (h.KanalId is { } id ? $"kasası olan kanal (#{id}) bulunamadı" : "kasası boş") + "; geliri genel kasaya, gideri 'Dağılım bekliyor'a yazıldı", h.Tarih);
                satirlar.Add(new CekHareketi(h.Id, h.Sira, h.Tur, h.Tarih, h.Tutar, h.NetTutar, kanal, h.Karsi));
            }
            var (cekGelenleri, cekIslemleri) = CekTuretici.Satirlar(new CekBilgisi(cek.Id, cek.Tur, cek.Yon, cek.No, cek.Kisi, cek.Tutar), satirlar, donemler);
            gelenler.AddRange(cekGelenleri);
            islemler.AddRange(cekIslemleri);
        }
    }

```

- [ ] **Adım 6: Kasa dökümü türünü ekle.** `Kasa.Api/Servisler/KasaDokumu.cs` `Tur` metodunu şu başlangıçla değiştir (özet ve ilk iki satır):

```csharp
    /// <summary>Satır türü: Gelir (dönem geliri), EkstreGeliri, EkGelir, KrediCekimi; Gider, SabitGider, AylikGider, KartOdemesi,
    /// KartIadesi (önceden sayılan kart borcunun kasaya dönüşü), KrediTaksidi, KartAySonu (eski kartın ay sonu düşümü); Cek (çek ya da
    /// senet hareketinin gelir ya da gider ayağı, anahtarı "Cek:").</summary>
    public static string Tur(KasaHareketi h)
    {
        var anahtar = h.KaynakAnahtari ?? "";
        if (anahtar.StartsWith("Cek:", StringComparison.Ordinal))
            return KasaHareketTurleri.Cek;
```

(sonraki `if (h.Gelen is not null)` satırları aynen kalır). `Kasa.Api/KasaKontrolDtos.cs` `KasaHareketiDto` özetinde tür listesine ve
anahtar örneklerine çeki ekle:

```csharp
/// KrediCekimi, Gider, SabitGider, AylikGider, KartOdemesi, KartIadesi, KrediTaksidi, KartAySonu, Cek. <paramref name="KaynakAnahtari"/>:
/// denetim izindeki varlık ve kimlik ("Islem:812", "TakipKartOdeme:44", "TakipKrediTaksit:9", "Kredi:5", "EkstreKayit:9",
/// "HesapHareket:3", "Gelen:17", "TakipHarcama:12"); çek satırında hareketin kimliği ("Cek:31", gider ayağı "Cek:31:gider").
```

- [ ] **Adım 7: Çek hareketi takip başlangıcını sabitlesin.** `Kasa.Api/FinansHesaplari.cs` `IlkMaliKayitTuru` içinde

```csharp
        : db.AlisOdemeler.Any() ? "alış ödemesi"
        : null;
```

satırlarını şununla değiştir; özetin son satırındaki "kasa sayımı ve alış ödemesi." → "kasa sayımı, alış ödemesi ve çek hareketi.":

```csharp
        : db.AlisOdemeler.Any() ? "alış ödemesi"
        : db.CekHareketler.Any() ? "çek hareketi"
        : null;
```

- [ ] **Adım 8: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekRaporTests|FullyQualifiedName~TakipBaslangiciDegisikligiTests|FullyQualifiedName~AltinRaporTests|FullyQualifiedName~KasaHareketDokumuTests|FullyQualifiedName~RaporDayaniklilikTests|FullyQualifiedName~VeritabaniGocuTests|FullyQualifiedName~AyRaporuAnlikGoruntusuTests|FullyQualifiedName~BelgeDeposuGecisTests"`
Beklenen: hepsi başarılı. Tablo denetimi (`sqlite_master`) olmadan göç testlerinden 6'sı "no such table: Cekler" ile düşer.

Çalıştır (tamamı): `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:  1234`. `Altin/rapor-altin.json` değişmez (`git status` temiz).

- [ ] **Adım 9: Commit.**

```bash
git add Kasa.Api/Servisler/HesapServisi.cs Kasa.Api/Servisler/KasaDokumu.cs Kasa.Api/KasaKontrolDtos.cs Kasa.Api/FinansHesaplari.cs Kasa.Api.Tests/CekRaporTests.cs Kasa.Api.Tests/AltinRaporTests.cs Kasa.Api.Tests/TakipBaslangiciDegisikligiTests.cs
git commit -F - <<'MESAJ'
feat(api): çek hareketlerinden türetilmiş rapor satırları

HesapServisi hareketleri kredi taksitlerindeki gibi bellekte Gelen/Islem satırına
çevirir; satırlar listelerin sonuna eklenir, çek yokken raporlar birebir aynıdır
(altın test). Kasa dökümünde yeni tür Cek; çek hareketi takip başlangıcını sabitler.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 4: Ay kilidi

**Dosyalar:**
- Değiştir: `Kasa.Api/AyKilidiKurallari.cs`
- Oluştur: `Kasa.Api.Tests/CekKilitTests.cs`

- [ ] **Adım 1: Kilit testlerini yaz.** `Kasa.Api.Tests/CekKilitTests.cs`:

```csharp
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Çeklerde ay kilidi (docs/specs/2026-10-01-cekler.md "Ay kilidi"; AyKilidiKurallari): hareket eklenirken ya da silinirken tarihi
/// kapatılmış aydaysa reddedilir; kasayı etkileyen hareketi kapatılmış aydaysa çekin tutarı, kasası, yönü ve türü değiştirilemez,
/// çek silinemez. Vade, konum, not, kişi, banka ve no her zaman değiştirilebilir. Ağustos 2026 kapatılır; bugün 25 Eylül 2026.
/// </summary>
public class CekKilitTests
{
    private static readonly DateOnly Agustos = Month.AddMonths(-1);

    private static async Task Kapat(HttpClient c)
    {
        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", TestContext.Current.CancellationToken))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, Agustos.Year, Agustos.Month, "Ay tamamlandı"));
    }

    private static CekHareketEntity Tahsilat(DateOnly tarih) => new() { Tur = CekHareketTurleri.Tahsilat, Tarih = tarih, Tutar = 1_000m, KanalId = 1 };

    [Fact]
    public async Task Kapatilmis_aya_hareket_eklenemez_ve_oradaki_hareket_silinemez_acik_aya_eklenir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(), Tahsilat(Agustos.AddDays(10)));
        await Kapat(c);
        await Assert.ThrowsAsync<KilitliDonemException>(() => CekRaporTests.HareketEkle(f, cek.Id, Tahsilat(Agustos.AddDays(20))));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.CekHareketler.Remove(await db.CekHareketler.SingleAsync(TestContext.Current.CancellationToken));
            Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        }
        await CekRaporTests.HareketEkle(f, cek.Id, Tahsilat(Today));
    }

    [Fact]
    public async Task Kilitli_kasa_hareketi_olan_cekin_tutari_kasasi_yonu_turu_degismez_cek_silinmez_diger_alanlar_degisir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(), Tahsilat(Agustos.AddDays(10)));
        await Kapat(c);
        foreach (var degistir in new Action<CekEntity>[] { x => x.Tutar = 60_000m, x => x.KanalId = 2, x => x.Yon = CekYonleri.Verilen, x => x.Tur = CekTurleri.Senet })
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            degistir(await db.Cekler.SingleAsync(x => x.Id == cek.Id, TestContext.Current.CancellationToken));
            Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        }
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Cekler.Remove(await db.Cekler.SingleAsync(x => x.Id == cek.Id, TestContext.Current.CancellationToken));
            Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        }
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var kayit = await db.Cekler.SingleAsync(x => x.Id == cek.Id, TestContext.Current.CancellationToken);
            kayit.VadeTarihi = Today.AddDays(40);
            kayit.Konum = CekKonumlari.BankadaTahsilde;
            kayit.Not = "Bankaya verildi";
            kayit.Kisi = "Ahmet Y.";
            kayit.Banka = "Halk";
            kayit.No = "12346";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Kilitli_ayda_yalniz_kasayi_etkilemeyen_hareketi_olan_cekin_tutari_degisir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(),
            new CekHareketEntity { Tur = CekHareketTurleri.Karsiliksiz, Tarih = Agustos.AddDays(10) });
        await Kapat(c);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        (await db.Cekler.SingleAsync(x => x.Id == cek.Id, TestContext.Current.CancellationToken)).Tutar = 45_000m;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Adım 2: Testlerin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekKilitTests"`
Beklenen: ilk iki test düşer (`KilitliDonemException` beklenirdi, atılmadı); üçüncüsü geçer.

- [ ] **Adım 3: Kilit kuralını yaz.** `Kasa.Api/AyKilidiKurallari.cs` başına `using Kasa.Core;` ekle. `Dogrula` içinde
  `bool ExpenseChangesLaterClosedPurchasePayment(IslemEntity expense)` yerel fonksiyonundan hemen önce:

```csharp
        // Çekin kasayı etkileyen (türetilmiş satır üreten) hareketi kilitli dönemde mi (docs/specs/2026-10-01-cekler.md "Ay kilidi").
        bool CekKilitli(int cekId) => db.CekHareketler.AsNoTracking().Where(h => h.CekId == cekId && h.Tarih <= end).Select(h => h.Tur)
            .AsEnumerable().Any(CekKurallari.KasaEtkili);
```

ve `switch` içinde `HesapEntity => true,` kolundan hemen önce:

```csharp
                // Çek hareketi kilitli dönemde eklenemez ve silinemez (geri alma dahil). Kasayı etkileyen hareketi kilitli dönemde olan
                // çekin tutarı, kasası, yönü ve türü değişmez, çek silinmez; vade, konum, not, kişi, banka ve no her zaman değişir.
                CekHareketEntity h => DateLocked(e, nameof(h.Tarih)),
                CekEntity c => Changed(e, "Tutar", "KanalId", "Yon", "Tur") && CekKilitli(c.Id),
```

(`Changed` eklenen ve silinen kayıtta da true döner; eklenen çekin hareketi olmadığı için engellenmez.)

- [ ] **Adım 4: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekKilitTests|FullyQualifiedName~KilitliDonemTests|FullyQualifiedName~CekRaporTests"`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:    21`.

- [ ] **Adım 5: Commit.**

```bash
git add Kasa.Api/AyKilidiKurallari.cs Kasa.Api.Tests/CekKilitTests.cs
git commit -F - <<'MESAJ'
feat(api): çeklerde ay kilidi

Kapatılmış aya çek hareketi eklenemez, oradaki hareket silinemez; kasayı etkileyen
hareketi kilitli olan çekin tutarı, kasası, yönü ve türü değişmez, çek silinmez.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---
## Görev 5: Uçlar, yetki ve uç envanteri

**Dosyalar:**
- Oluştur: `Kasa.Api/CekDtos.cs`, `Kasa.Api/CekServisi.cs`, `Kasa.Api/CekEndpoints.cs`, `Kasa.Api.Tests/CekUcTests.cs`
- Değiştir: `Kasa.Api/FinansTakipEndpoints.cs`, `Kasa.Api.Tests/UcYetkiTaramasiTests.cs`, `Kasa.Api.Tests/Altin/uc-envanteri.txt` (yeniden yazılır)

Uçlar (`/api/takip` grubu `Finans` politikasıyla okunur; yazma `Editor`): `GET /cekler?yon=&durum=&ara=&vadeBas=&vadeSon=`,
`GET /cekler/{id}`, `POST /cekler`, `PUT /cekler/{id}`, `DELETE /cekler/{id}`, `POST /cekler/{id}/hareketler`,
`DELETE /cekler/{id}/hareketler/son`. Özet ucu (`GET /cekler/ozet`) Görev 8'dedir.

- [ ] **Adım 1: Uç testlerini yaz.** `Kasa.Api.Tests/CekUcTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Core.Kodlar;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Çek uçları (/api/takip/cekler; docs/specs/2026-10-01-cekler.md "Uçlar"): ekleme, düzeltme, silme, hareket ve geri alma; istekId
/// tekrar koruması, sürüm çakışması, aynı çek uyarısı, geçiş kuralları, ay kilidi ve yetki. Bugün 25 Eylül 2026 (sabit saat),
/// takip başlangıcı 1 Haziran 2026, açılış kasası 1.000; kanallar MEZAT, PERAKENDE, TOPTAN.
/// </summary>
public class CekUcTests
{
    private const string Yol = "/api/takip/cekler";

    private static CekYaz Alinan(string no = "12345", decimal tutar = 50_000m, bool teminat = false, DateOnly? vade = null) =>
        new(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Alinan, no, "Ziraat", "Ahmet Yılmaz", tutar, vade ?? Today.AddDays(20), null, teminat, null, null);

    private static CekYaz Verilen(string kanal = KanalEtiketleri.Ortak, decimal tutar = 30_000m, DateOnly? vade = null) =>
        new(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Verilen, "777", "Halk", "Mehmet Ticaret", tutar, vade ?? Today.AddDays(10), kanal, false, null, null);

    private static CekHareketYaz Hareket(CekDto cek, string tur, decimal tutar = 0m, DateOnly? tarih = null, string? kanal = null, decimal? net = null, string? karsi = null) =>
        new(Guid.NewGuid(), cek.Surum, tur, tarih ?? Today, tutar, net, kanal, karsi);

    private static async Task<(HttpStatusCode Durum, string Govde)> Gonder(HttpClient c, HttpMethod yontem, string yol, object govde)
    {
        using var istek = new HttpRequestMessage(yontem, yol) { Content = JsonContent.Create(govde) };
        using var yanit = await c.SendAsync(istek, TestContext.Current.CancellationToken);
        return (yanit.StatusCode, await yanit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static string Hata(string govde) => JsonDocument.Parse(govde).RootElement.GetProperty("hata").GetString()!;

    private static async Task<T> Delete<T>(HttpClient c, string yol, object govde)
    {
        var (durum, metin) = await Gonder(c, HttpMethod.Delete, yol, govde);
        Assert.True(durum == HttpStatusCode.OK, $"{durum}: {metin}");
        return JsonSerializer.Deserialize<T>(metin, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Fact]
    public async Task Alinan_cek_eklenir_kismi_tahsil_edilir_son_hareket_geri_alinir_cek_silinir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var istek = Alinan();
        var cek = await Post<CekDto>(c, Yol, istek);
        Assert.Equal((1, CekDurumlari.Portfoyde, 50_000m, CekKonumlari.Elde, (string?)null, (string?)null), (cek.Surum, cek.Durum, cek.Kalan, cek.Konum, cek.Kanal, cek.Uyari));
        Assert.Equal([CekHareketTurleri.Tahsilat, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade], cek.IzinliHareketler);
        // Aynı istek kimliği ikinci çek oluşturmaz.
        Assert.Equal(cek.Id, (await Post<CekDto>(c, Yol, istek)).Id);
        Assert.Single((await c.GetFromJsonAsync<List<CekDto>>(Yol, TestContext.Current.CancellationToken))!);

        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Tahsilat, 20_000m, kanal: "MEZAT"));
        Assert.Equal((CekDurumlari.KismenTahsilEdildi, 30_000m, 2), (cek.Durum, cek.Kalan, cek.Surum));
        var hareket = Assert.Single(cek.Hareketler);
        Assert.Equal((1, CekHareketTurleri.Tahsilat, 20_000m, "MEZAT"), (hareket.Sira, hareket.Tur, hareket.Tutar, hareket.Kanal));
        Assert.Equal(21_000m, (await Panel(c)).GuncelKasa);

        cek = await Delete<CekDto>(c, $"{Yol}/{cek.Id}/hareketler/son", new CekSilYaz(Guid.NewGuid(), cek.Surum));
        Assert.Equal((CekDurumlari.Portfoyde, 50_000m), (cek.Durum, cek.Kalan));
        Assert.Empty(cek.Hareketler);
        Assert.Equal(1_000m, (await Panel(c)).GuncelKasa);

        var (durum, _) = await Gonder(c, HttpMethod.Delete, $"{Yol}/{cek.Id}", new CekSilYaz(Guid.NewGuid(), cek.Surum));
        Assert.Equal(HttpStatusCode.NoContent, durum);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{Yol}/{cek.Id}", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Duzeltme_surumle_yapilir_eski_surum_409_konum_ve_vade_degisir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await Post<CekDto>(c, Yol, Alinan());
        var duzelt = Alinan() with { Surum = cek.Surum, Konum = CekKonumlari.BankadaTahsilde, VadeTarihi = Today.AddDays(45) };
        var yeni = (await c.PutAsJsonAsync($"{Yol}/{cek.Id}", duzelt, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, yeni.StatusCode);
        var sonra = (await yeni.Content.ReadFromJsonAsync<CekDto>(TestContext.Current.CancellationToken))!;
        Assert.Equal((CekKonumlari.BankadaTahsilde, Today.AddDays(45), 2), (sonra.Konum, sonra.VadeTarihi, sonra.Surum));
        var (durum, govde) = await Gonder(c, HttpMethod.Put, $"{Yol}/{cek.Id}", duzelt with { IstekId = Guid.NewGuid(), Konum = CekKonumlari.Icrada });
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.Equal("Çek başka bir işlemle değişti. Listeyi yenileyip tekrar deneyin.", Hata(govde));
    }

    [Fact]
    public async Task Ayni_yon_banka_ve_no_uyari_dondurur_kayit_yine_yapilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var ilk = await Post<CekDto>(c, Yol, Alinan());
        var ikinci = await Post<CekDto>(c, Yol, Alinan() with { Banka = " ziraat " });
        Assert.Equal($"Aynı yön, banka ve numarayla kayıtlı başka çek var: #{ilk.Id}.", ikinci.Uyari);
        Assert.Equal(2, (await c.GetFromJsonAsync<List<CekDto>>(Yol, TestContext.Current.CancellationToken))!.Count);
        Assert.Null((await Post<CekDto>(c, Yol, Verilen())).Uyari);
    }

    [Fact]
    public async Task Gecis_ve_tutar_kurallari_turkce_iletiyle_reddedilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var verilen = await Post<CekDto>(c, Yol, Verilen());
        var (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{verilen.Id}/hareketler", Hareket(verilen, CekHareketTurleri.Ciro, 30_000m, karsi: "X"));
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.Equal("Bu kayıt portföyde; şu an yalnız şu hareketler girilebilir: Ödeme, Karşılıksız, İade.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{verilen.Id}/hareketler", Hareket(verilen, CekHareketTurleri.Odeme, 30_000.01m));
        Assert.Equal(HttpStatusCode.BadRequest, durum);
        Assert.Equal("Tutar sıfırdan büyük olmalı ve kalan tutarı (30.000,00 TL) aşamaz.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{verilen.Id}/hareketler", Hareket(verilen, CekHareketTurleri.Odeme, 1m, kanal: "MEZAT"));
        Assert.Equal("Verilen çek çekin kasasından ödenir; harekette kasa seçilmez.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{verilen.Id}/hareketler", Hareket(verilen, CekHareketTurleri.Odeme, 1m, tarih: Today.AddDays(1)));
        Assert.Equal("Hareket tarihi takip başlangıcı (01.06.2026) ile bugün arasında olmalı.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, Yol, Alinan() with { Kanal = "MEZAT" });
        Assert.Equal("Alınan çekte kasa tahsilat, ciro ya da kırdırma hareketinde seçilir.", Hata(govde));
        (durum, govde) = await Gonder(c, HttpMethod.Post, Yol, Verilen(kanal: ""));
        Assert.Equal("Verilen çekin ödeneceği kasayı (kanal ya da Ortak) seçin.", Hata(govde));

        var alinan = await Post<CekDto>(c, Yol, Alinan());
        (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{alinan.Id}/hareketler", Hareket(alinan, CekHareketTurleri.Tahsilat, 10m));
        Assert.Equal("Tahsilatın, cironun ya da kırdırmanın kasasını (kanal) seçin.", Hata(govde));
    }

    [Fact]
    public async Task Ciro_ve_donus_ayni_kasada_ters_satir_uretir_donus_kasasi_cirodan_gelir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await Post<CekDto>(c, Yol, Alinan());
        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Ciro, 50_000m, Today.AddDays(-5), "PERAKENDE", karsi: "Veli Toptan"));
        Assert.Equal(CekDurumlari.CiroEdildi, cek.Durum);
        Assert.Equal([CekHareketTurleri.Donus], cek.IzinliHareketler);
        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Donus, 50_000m));
        Assert.Equal(CekDurumlari.Karsiliksiz, cek.Durum);
        Assert.Equal(("PERAKENDE", "Veli Toptan"), (cek.Hareketler[1].Kanal, cek.Hareketler[1].Karsi));
        Assert.Equal(1_000m, (await Panel(c)).GuncelKasa);
        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Tahsilat, 50_000m, kanal: "MEZAT"));
        Assert.Equal(CekDurumlari.TahsilEdildi, cek.Durum);
        Assert.Equal(51_000m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Kapatilmis_aydaki_hareket_geri_alinamaz_aya_hareket_girilemez_tutar_degismez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var agustos = Month.AddMonths(-1);
        var cek = await Post<CekDto>(c, Yol, Alinan());
        cek = await Post<CekDto>(c, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Tahsilat, 1_000m, agustos.AddDays(5), "MEZAT"));
        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", TestContext.Current.CancellationToken))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, agustos.Year, agustos.Month, "Ay tamamlandı"));

        var kilitIletisi = $"{agustos.AddMonths(1).AddDays(-1):yyyy-MM-dd} tarihine kadar dönem kilitli.";
        var (durum, govde) = await Gonder(c, HttpMethod.Post, $"{Yol}/{cek.Id}/hareketler", Hareket(cek, CekHareketTurleri.Tahsilat, 1_000m, agustos.AddDays(20), "MEZAT"));
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.StartsWith(kilitIletisi, Hata(govde), StringComparison.Ordinal);
        (durum, govde) = await Gonder(c, HttpMethod.Delete, $"{Yol}/{cek.Id}/hareketler/son", new CekSilYaz(Guid.NewGuid(), cek.Surum));
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.StartsWith(kilitIletisi, Hata(govde), StringComparison.Ordinal);
        (durum, govde) = await Gonder(c, HttpMethod.Put, $"{Yol}/{cek.Id}", Alinan(tutar: 60_000m) with { Surum = cek.Surum });
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.StartsWith(kilitIletisi, Hata(govde), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await Gonder(c, HttpMethod.Put, $"{Yol}/{cek.Id}", Alinan() with { Surum = cek.Surum, Not = "Not eklendi" })).Durum);
    }

    [Fact]
    public async Task Liste_yon_durum_arama_ve_vade_suzgecleriyle_vadeye_gore_siralanir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var yakin = await Post<CekDto>(c, Yol, Alinan("A-1", 10_000m, vade: Today.AddDays(5)));
        await Post<CekDto>(c, Yol, Alinan("A-2", 20_000m, vade: Today.AddDays(60)));
        await Post<CekDto>(c, Yol, Alinan("A-3", 40_000m, teminat: true, vade: Today.AddDays(3)));
        var gecmis = await Post<CekDto>(c, Yol, Alinan("A-4", 5_000m, vade: Today.AddDays(-2)));
        await Post<CekDto>(c, Yol, Verilen(tutar: 7_000m, vade: Today.AddDays(30)));
        var kapali = await Post<CekDto>(c, Yol, Alinan("A-5", 1_000m, vade: Today.AddDays(1)));
        await Post<CekDto>(c, $"{Yol}/{kapali.Id}/hareketler", Hareket(kapali, CekHareketTurleri.Iade));
        await Post<CekDto>(c, $"{Yol}/{gecmis.Id}/hareketler", Hareket(gecmis, CekHareketTurleri.Tahsilat, 2_000m, kanal: "MEZAT"));

        async Task<List<string>> Nolar(string sorgu) =>
            (await c.GetFromJsonAsync<List<CekDto>>(Yol + sorgu, TestContext.Current.CancellationToken))!.Select(x => x.No).ToList();
        Assert.Equal(["A-4", "A-3", "A-1", "A-2"], await Nolar($"?yon={CekYonleri.Alinan}&durum={CekSuzgecleri.Portfoyde}"));
        Assert.Equal(["A-5"], await Nolar($"?durum={CekSuzgecleri.Kapanan}"));
        Assert.Equal(["777"], await Nolar($"?yon={CekYonleri.Verilen}"));
        Assert.Equal(["A-1"], await Nolar($"?ara=a-1"));
        Assert.Equal(["777"], await Nolar("?ara=mehmet"));
        Assert.Equal(["A-3", "A-1"], await Nolar($"?yon={CekYonleri.Alinan}&durum={CekSuzgecleri.Portfoyde}&vadeBas={Today:yyyy-MM-dd}&vadeSon={Today.AddDays(30):yyyy-MM-dd}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Yol + "?durum=Yanlis", TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(yakin.Id, (await c.GetFromJsonAsync<CekDto>($"{Yol}/{yakin.Id}", TestContext.Current.CancellationToken))!.Id);
    }

    [Fact]
    public async Task Izleyici_cekleri_okur_yazamaz()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<CekDto>(c, Yol, Alinan());
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-cek-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-cek-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Single((await izleyici.GetFromJsonAsync<List<CekDto>>(Yol, TestContext.Current.CancellationToken))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsJsonAsync(Yol, Alinan(), TestContext.Current.CancellationToken)).StatusCode);
    }
}
```

- [ ] **Adım 2: İzleyici yazma taramasına çek uçlarını ekle.** `Kasa.Api.Tests/UcYetkiTaramasiTests.cs`
  `Izleyici_finans_mutasyonunda_403_alir` üstündeki `[InlineData("POST", "/api/takip/krediler/1/gecis")]` satırının altına:

```csharp
    [InlineData("POST", "/api/takip/cekler")]
    [InlineData("PUT", "/api/takip/cekler/1")]
    [InlineData("DELETE", "/api/takip/cekler/1")]
    [InlineData("POST", "/api/takip/cekler/1/hareketler")]
    [InlineData("DELETE", "/api/takip/cekler/1/hareketler/son")]
```

(`Api_mutasyonlari_editor_...` ve `Yetki_matrisi_...` yeni uçları uç kaynağından kendiliğinden kapsar: yazmalar `Editor`, okumalar
izleyiciye açık, alıcıya 403.)

- [ ] **Adım 3: Testlerin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekUcTests"`
Beklenen: derleme hatası (CS0246 `CekYaz`, `CekDto`, `CekHareketYaz`, `CekSilYaz`).

- [ ] **Adım 4: DTO'ları yaz.** `Kasa.Api/CekDtos.cs`:

```csharp
namespace Kasa.Api;

/// <summary>Yeni ya da düzeltilen çek / senet (POST, PUT /api/takip/cekler). <paramref name="Kanal"/>: verilen çekte ödeneceği kasanın
/// adı ya da "Ortak" (zorunlu); alınan çekte boş (kasa harekette seçilir). <paramref name="Konum"/>: yalnız alınan çekte; boşsa Elde.
/// Yeni kayıtta <paramref name="Surum"/> 0'dır.</summary>
public record CekYaz(Guid IstekId, int Surum, string Tur, string Yon, string No, string? Banka, string Kisi, decimal Tutar, DateOnly VadeTarihi,
    string? Kanal, bool Teminat, string? Konum, string? Not);

/// <summary>Çek hareketi (POST /api/takip/cekler/{id}/hareketler). <paramref name="Kanal"/>: alınan çekin tahsilat, ciro ve kırdırmasında
/// kasanın adı; dönüşte ve verilen çekte boş (sunucu ters çevrilen hareketin ya da çekin kasasını kullanır). <paramref name="NetTutar"/>
/// yalnız kırdırmada; <paramref name="Karsi"/> ciroda ve kırdırmada zorunlu.</summary>
public record CekHareketYaz(Guid IstekId, int Surum, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar, string? Kanal, string? Karsi);

/// <summary>Çeki silme ya da son hareketi geri alma (DELETE gövdesi): tekrar koruması ve sürüm.</summary>
public record CekSilYaz(Guid IstekId, int Surum);

public record CekHareketDto(int Id, int Sira, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar, int? KanalId, string? Kanal, string? Karsi);

/// <summary>Çek ve hesaplanan durumu. <paramref name="Kanal"/>: verilen çekin kasası (kanal adı ya da "Ortak"); alınanda null.
/// <paramref name="Durum"/> (CekDurumlari) ve <paramref name="Kalan"/> hareketlerden hesaplanır; <paramref name="IzinliHareketler"/>
/// şu an girilebilecek hareket türleridir. <paramref name="Uyari"/>: aynı yön, banka ve numarayla başka kayıt varsa uyarı; yoksa
/// null (kaydı engellemez).</summary>
public record CekDto(int Id, int Surum, string Tur, string Yon, string No, string? Banka, string Kisi, decimal Tutar, DateOnly VadeTarihi,
    int? KanalId, string? Kanal, bool Teminat, string? Konum, string? Not, string Durum, decimal Kalan, IReadOnlyList<string> IzinliHareketler,
    IReadOnlyList<CekHareketDto> Hareketler, string? Uyari);
```

- [ ] **Adım 5: Okuma servisini yaz.** `Kasa.Api/CekServisi.cs`:

```csharp
using System.Globalization;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>Çek okumaları (liste, tek kayıt; bildirim ve panel özeti de buradan okur): kayıtlar ve hareketler tek sorguda okunur, durum
/// <see cref="CekKurallari"/>'ndan hesaplanır. Girdi doğrulaması uçtadır (FinansTakipEndpoints.MapCekEndpoints).</summary>
internal static class CekServisi
{
    internal sealed record Kayit(CekEntity Cek, IReadOnlyList<CekHareketEntity> Hareketler, CekDurumu Durum);

    private static readonly CompareInfo Tr = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    internal static CekHareketi Cekirdek(CekHareketEntity h) => new(h.Id, h.Sira, h.Tur, h.Tarih, h.Tutar, h.NetTutar, null, h.Karsi);

    internal static List<Kayit> Oku(KasaDbContext db, int? id = null)
    {
        var cekler = (id is { } i ? db.Cekler.AsNoTracking().Where(c => c.Id == i) : db.Cekler.AsNoTracking()).OrderBy(c => c.Id).ToList();
        var kimlikler = cekler.Select(c => c.Id).ToArray();
        var hareketler = db.CekHareketler.AsNoTracking().Where(h => kimlikler.Contains(h.CekId)).ToList().ToLookup(h => h.CekId);
        return cekler.Select(c =>
        {
            var liste = hareketler[c.Id].OrderBy(h => h.Sira).ToList();
            return new Kayit(c, liste, CekKurallari.Durum(c.Yon, c.Tutar, liste.Select(Cekirdek).ToList()));
        }).ToList();
    }

    /// <summary>Tek çek; yoksa null.</summary>
    internal static CekDto? Tek(TakipHesapBaglami b, int id) => Oku(b.Db, id) is [var k] ? Dto(b, k, Benzerler(b.Db)) : null;

    internal static List<CekDto> Liste(TakipHesapBaglami b, string? yon, string? durum, string? ara, DateOnly? vadeBas, DateOnly? vadeSon)
    {
        var aranan = string.IsNullOrWhiteSpace(ara) ? null : ara.Trim();
        bool Icerir(string? alan) => alan is not null && Tr.IndexOf(alan, aranan!, CompareOptions.IgnoreCase) >= 0;
        var benzerler = Benzerler(b.Db);
        return Oku(b.Db)
            .Where(k => yon is null || k.Cek.Yon == yon)
            .Where(k => (durum ?? CekSuzgecleri.Hepsi) switch
            {
                CekSuzgecleri.Portfoyde => k.Durum.Acik,
                CekSuzgecleri.Karsiliksiz => k.Durum.Durum == CekDurumlari.Karsiliksiz,
                CekSuzgecleri.Kapanan => k.Durum.Kapali,
                _ => true,
            })
            .Where(k => aranan is null || Icerir(k.Cek.Kisi) || Icerir(k.Cek.Banka) || Icerir(k.Cek.No))
            .Where(k => (vadeBas is null || k.Cek.VadeTarihi >= vadeBas) && (vadeSon is null || k.Cek.VadeTarihi <= vadeSon))
            .OrderBy(k => k.Cek.VadeTarihi).ThenBy(k => k.Cek.Id)
            .Select(k => Dto(b, k, benzerler)).ToList();
    }

    private static List<(int Id, string Yon, string? Banka, string No)> Benzerler(KasaDbContext db) =>
        db.Cekler.AsNoTracking().Select(c => new { c.Id, c.Yon, c.Banka, c.No }).AsEnumerable().Select(c => (c.Id, c.Yon, c.Banka, c.No)).ToList();

    private static CekDto Dto(TakipHesapBaglami b, Kayit k, IReadOnlyList<(int Id, string Yon, string? Banka, string No)> benzerler)
    {
        var c = k.Cek;
        string? KanalAdi(int? kanalId) => kanalId is { } i ? b.KanalAdlari.GetValueOrDefault(i, $"Silinmiş kanal #{i}") : null;
        var verilen = c.Yon == CekYonleri.Verilen;
        var ayni = benzerler.Where(x => x.Id != c.Id && CekKurallari.AyniCek(c.Yon, c.Banka, c.No, x.Yon, x.Banka, x.No)).Select(x => "#" + x.Id).ToList();
        return new CekDto(c.Id, c.Surum, c.Tur, c.Yon, c.No, c.Banka, c.Kisi, c.Tutar, c.VadeTarihi, c.KanalId,
            verilen ? KanalAdi(c.KanalId) ?? KanalEtiketleri.Ortak : null, c.Teminat, c.Konum, c.Not, k.Durum.Durum, k.Durum.Kalan,
            CekKurallari.IzinliHareketler(c.Yon, c.Tutar, k.Hareketler.Select(Cekirdek).ToList()),
            k.Hareketler.Select(h => new CekHareketDto(h.Id, h.Sira, h.Tur, h.Tarih, h.Tutar, h.NetTutar, h.KanalId,
                KanalAdi(h.KanalId) ?? (verilen && h.Tur == CekHareketTurleri.Odeme ? KanalEtiketleri.Ortak : null), h.Karsi)).ToList(),
            ayni.Count == 0 ? null : $"Aynı yön, banka ve numarayla kayıtlı başka çek var: {string.Join(", ", ayni)}.");
    }
}
```

- [ ] **Adım 6: Uçları yaz.** `Kasa.Api/CekEndpoints.cs` (`FinansTakipEndpoints`'in parçalı sınıfı; `View`, `Safe`, `Require`, `Money`,
  `Date`, `Tl` oradan gelir):

```csharp
using System.Globalization;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

/// <summary>
/// Çek ve senet uçları (/api/takip/cekler; docs/specs/2026-10-01-cekler.md "Uçlar"). Okuma Finans politikasıyla (İzleyici ve Editör),
/// yazma Editor politikasıyla açılır; Alıcı erişemez. Yazmalarda istekId tekrar koruması ve sürüm (409) diğer takip uçlarındaki
/// gibidir; geçiş ve tutar kuralları <see cref="CekKurallari"/>'ndadır, ay kilidi kaydetme kancasındadır (AyKilidiKurallari).
/// Hata iletileri Türkçe ve { hata } biçimindedir.
/// </summary>
public static partial class FinansTakipEndpoints
{
    private static readonly string[] HareketTurleri =
    [
        CekHareketTurleri.Tahsilat, CekHareketTurleri.Odeme, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Donus,
        CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade,
    ];

    private static void MapCekEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/cekler", (string? yon, string? durum, string? ara, DateOnly? vadeBas, DateOnly? vadeSon, KasaDbContext db, CancellationToken ct) =>
            View(db, b =>
            {
                Require(yon is null or CekYonleri.Alinan or CekYonleri.Verilen, "Yön alınan ya da verilen olmalı.");
                Require(durum is null or CekSuzgecleri.Portfoyde or CekSuzgecleri.Karsiliksiz or CekSuzgecleri.Kapanan or CekSuzgecleri.Hepsi,
                    "Durum portföyde, karşılıksız, kapanan ya da hepsi olmalı.");
                Require((ara?.Length ?? 0) <= 200, "Arama en çok 200 karakter olabilir.");
                Require(vadeBas is null || vadeSon is null || vadeBas <= vadeSon, "Vade başlangıcı bitişten sonra olamaz.");
                return CekServisi.Liste(b, yon, durum, ara, vadeBas, vadeSon);
            }, ct));
        api.MapGet("/cekler/{id:int}", (int id, KasaDbContext db, CancellationToken ct) => View(db, b =>
        {
            var cek = CekServisi.Tek(b, id);
            Require(cek is not null, "Çek bulunamadı.", 404);
            return cek!;
        }, ct));

        api.MapPost("/cekler", (CekYaz dto, KasaDbContext db) => CekDegisikligi(db, 0, null, dto.IstekId, "CekYeni", dto, () =>
        {
            var cek = new CekEntity();
            CekAlanlari(db, cek, dto);
            db.Cekler.Add(cek);
            db.SaveChanges();
            return cek.Id;
        })).RequireAuthorization("Editor");
        api.MapPut("/cekler/{id:int}", (int id, CekYaz dto, KasaDbContext db) => CekDegisikligi(db, id, dto.Surum, dto.IstekId, "CekDuzelt", dto, () =>
        {
            var cek = db.Cekler.Single(c => c.Id == id);
            var hareketler = db.CekHareketler.AsNoTracking().Where(h => h.CekId == id).ToList();
            if (hareketler.Count > 0)
            {
                Require(dto.Yon == cek.Yon && dto.Tur == cek.Tur, "Hareketi olan çekin yönü ve türü değiştirilemez; önce hareketleri geri alın.", 409);
                if (dto.Tutar != cek.Tutar)
                {
                    Require(!hareketler.Any(h => h.Tur is CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma or CekHareketTurleri.Donus),
                        "Ciro, kırdırma ya da dönüş hareketi olan çekin tutarı değiştirilemez.", 409);
                    var odenen = CekKurallari.Durum(cek.Yon, cek.Tutar, hareketler.Select(CekServisi.Cekirdek).ToList()).Odenen;
                    Require(dto.Tutar >= odenen, $"Tutar tahsil edilen ya da ödenen tutardan ({Tl(odenen)}) az olamaz.", 409);
                }
            }
            CekAlanlari(db, cek, dto);
            return id;
        })).RequireAuthorization("Editor");
        api.MapDelete("/cekler/{id:int}", (int id, [FromBody] CekSilYaz dto, KasaDbContext db) => CekDegisikligi(db, id, dto.Surum, dto.IstekId, "CekSil", dto, () =>
        {
            db.CekHareketler.RemoveRange(db.CekHareketler.Where(h => h.CekId == id).ToList());
            db.Cekler.Remove(db.Cekler.Single(c => c.Id == id));
            return id;
        }, silme: true)).RequireAuthorization("Editor");
        api.MapPost("/cekler/{id:int}/hareketler", (int id, CekHareketYaz dto, KasaDbContext db) => CekDegisikligi(db, id, dto.Surum, dto.IstekId, "CekHareket", dto, () =>
        {
            HareketEkle(db, db.Cekler.Single(c => c.Id == id), dto);
            return id;
        })).RequireAuthorization("Editor");
        api.MapDelete("/cekler/{id:int}/hareketler/son", (int id, [FromBody] CekSilYaz dto, KasaDbContext db) => CekDegisikligi(db, id, dto.Surum, dto.IstekId, "CekGeriAl", dto, () =>
        {
            var son = db.CekHareketler.Where(h => h.CekId == id).OrderByDescending(h => h.Sira).FirstOrDefault();
            Require(son is not null, "Geri alınacak hareket yok.", 409);
            db.CekHareketler.Remove(son!);
            return id;
        })).RequireAuthorization("Editor");
    }

    /// <summary>Çek yazma uçlarının ortak akışı (kart ve kredideki Change ile aynı desen): tek yazma transaction'ı, denetim bağlamı,
    /// istekId tekrar koruması (aynı istek aynı yanıtı alır, aynı kimlik başka içerikle 409), sürüm denetimi, değişiklik, sürüm artışı.</summary>
    private static IResult CekDegisikligi(KasaDbContext db, int id, int? surum, Guid istekId, string tur, object govde, Func<int> degistir, bool silme = false) =>
        Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            using var denetim = db.Denetle(null, istekId);
            var dugum = JsonSerializer.SerializeToNode(govde)!.AsObject();
            dugum.Remove("Surum");
            var ozet = FinansHesaplari.Ozet(new { id, govde = dugum.ToJsonString() });
            IResult Yanit(int kimlik) => silme ? Results.NoContent() : Results.Ok(CekServisi.Tek(new TakipHesapBaglami(db), kimlik));
            if (FinansHesaplari.Tekrar(db, istekId, tur, ozet, Yanit) is { } tekrar)
                return tekrar;
            if (id != 0)
            {
                var mevcut = db.Cekler.Where(c => c.Id == id).Select(c => (int?)c.Surum).SingleOrDefault();
                Require(mevcut is not null, "Çek bulunamadı.", 404);
                Require(surum == mevcut, "Çek başka bir işlemle değişti. Listeyi yenileyip tekrar deneyin.", 409);
            }
            var sonuc = degistir();
            if (!silme)
                db.Cekler.Single(c => c.Id == sonuc).Surum++;
            FinansHesaplari.IstekKaydet(db, istekId, tur, ozet, sonuc);
            db.SaveChanges();
            return Yanit(sonuc);
        }));

    /// <summary>Çekin alanlarını doğrulayıp yazar (ekleme ve düzeltme).</summary>
    private static void CekAlanlari(KasaDbContext db, CekEntity cek, CekYaz d)
    {
        Require(d.Tur is CekTurleri.Cek or CekTurleri.Senet, "Tür çek ya da senet olmalı.");
        Require(d.Yon is CekYonleri.Alinan or CekYonleri.Verilen, "Yön alınan ya da verilen olmalı.");
        var no = CekMetni(d.No, "Çek / senet numarası", 100)!;
        var banka = CekMetni(d.Banka, "Banka", 200, zorunlu: d.Tur == CekTurleri.Cek);
        var kisi = CekMetni(d.Kisi, "Kişi", 200)!;
        var not = CekMetni(d.Not, "Not", 2000, zorunlu: false);
        Money(d.Tutar);
        Require(d.Tutar > 0, "Tutar sıfırdan büyük olmalı.");
        Date(d.VadeTarihi);
        Require(d.VadeTarihi >= GirdiDogrulama.EnErkenTarih, $"Vade tarihi {GirdiDogrulama.EnErkenTarih:dd.MM.yyyy} tarihinden önce olamaz.");
        int? kanalId = null;
        string? konum = null;
        if (d.Yon == CekYonleri.Verilen)
        {
            kanalId = KasaCoz(db, d.Kanal, ortakOlabilir: true, "Verilen çekin ödeneceği kasayı (kanal ya da Ortak) seçin.");
            Require(string.IsNullOrWhiteSpace(d.Konum), "Konum yalnız alınan çekte seçilir.");
        }
        else
        {
            Require(string.IsNullOrWhiteSpace(d.Kanal), "Alınan çekte kasa tahsilat, ciro ya da kırdırma hareketinde seçilir.");
            konum = string.IsNullOrWhiteSpace(d.Konum) ? CekKonumlari.Elde : d.Konum.Trim();
            Require(konum is CekKonumlari.Elde or CekKonumlari.BankadaTahsilde or CekKonumlari.Teminatta or CekKonumlari.Icrada,
                "Konum elde, bankada tahsilde, teminatta ya da icrada olmalı.");
        }
        cek.Tur = d.Tur;
        cek.Yon = d.Yon;
        cek.No = no;
        cek.Banka = banka;
        cek.Kisi = kisi;
        cek.Tutar = d.Tutar;
        cek.VadeTarihi = d.VadeTarihi;
        cek.KanalId = kanalId;
        cek.Teminat = d.Teminat;
        cek.Konum = konum;
        cek.Not = not;
    }

    /// <summary>Hareketi doğrulayıp ekler: tarih takip başlangıcı ile bugün arasında; geçiş, tarih sırası ve tutar kuralları
    /// <see cref="CekKurallari.HareketHatasi"/>; kasa alınan çekte seçilir, dönüşte ters çevrilen hareketinki, verilen çekte çekin kasasıdır.</summary>
    private static void HareketEkle(KasaDbContext db, CekEntity cek, CekHareketYaz d)
    {
        Require(HareketTurleri.Contains(d.Tur), "Geçerli bir hareket türü seçin.");
        Date(d.Tarih);
        var baslangic = db.Ayarlar.Select(a => a.TakipBaslangic).First();
        Require(d.Tarih >= baslangic && d.Tarih <= Bugun,
            $"Hareket tarihi takip başlangıcı ({baslangic.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}) ile bugün arasında olmalı.");
        Money(d.Tutar);
        if (d.NetTutar is { } net)
            Money(net);
        var karsi = CekMetni(d.Karsi, "Karşı taraf", 200, zorunlu: false);
        var mevcut = db.CekHareketler.Where(h => h.CekId == cek.Id).OrderBy(h => h.Sira).ToList();
        var cekirdek = mevcut.Select(CekServisi.Cekirdek).ToList();
        var yeni = new CekHareketi(0, mevcut.Count + 1, d.Tur, d.Tarih, d.Tutar, d.NetTutar, null, karsi);
        if (CekKurallari.HareketHatasi(cek.Yon, cek.Tutar, cekirdek, yeni) is { } hata)
            Require(false, hata, CekKurallari.IzinliHareketler(cek.Yon, cek.Tutar, cekirdek).Contains(d.Tur) ? 400 : 409);
        int? kanalId = null;
        if (d.Tur == CekHareketTurleri.Donus)
        {
            var devir = mevcut.Single(h => h.Id == CekKurallari.Durum(cek.Yon, cek.Tutar, cekirdek).AcikDevir!.Id);
            Require(string.IsNullOrWhiteSpace(d.Kanal), "Dönüş, ters çevrilen ciro ya da kırdırmanın kasasına yazılır; kasa seçilmez.");
            kanalId = devir.KanalId;
            karsi = devir.Karsi;
        }
        else if (cek.Yon == CekYonleri.Verilen)
        {
            Require(string.IsNullOrWhiteSpace(d.Kanal), "Verilen çek çekin kasasından ödenir; harekette kasa seçilmez.");
            kanalId = d.Tur == CekHareketTurleri.Odeme ? cek.KanalId : null;
        }
        else if (CekKurallari.KasaEtkili(d.Tur))
            kanalId = KasaCoz(db, d.Kanal, ortakOlabilir: false, "Tahsilatın, cironun ya da kırdırmanın kasasını (kanal) seçin.");
        else
            Require(string.IsNullOrWhiteSpace(d.Kanal), "Karşılıksız ve iade hareketinde kasa seçilmez.");
        db.CekHareketler.Add(new CekHareketEntity
        {
            CekId = cek.Id,
            Sira = mevcut.Count == 0 ? 1 : mevcut.Max(h => h.Sira) + 1,
            Tur = d.Tur,
            Tarih = d.Tarih,
            Tutar = d.Tutar,
            NetTutar = d.Tur == CekHareketTurleri.Kirdirma ? d.NetTutar : null,
            KanalId = kanalId,
            Karsi = d.Tur is CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma or CekHareketTurleri.Donus ? karsi : null,
        });
    }

    /// <summary>Kasa adını kimliğe çevirir; <paramref name="ortakOlabilir"/> ise "Ortak" null döner. Boş ya da kayıtsız ad 400.</summary>
    private static int? KasaCoz(KasaDbContext db, string? ad, bool ortakOlabilir, string bosIletisi)
    {
        var temiz = ad?.Trim();
        Require(!string.IsNullOrEmpty(temiz), bosIletisi);
        if (ortakOlabilir && temiz == KanalEtiketleri.Ortak)
            return null;
        var kanal = db.Kanallar.AsNoTracking().FirstOrDefault(k => k.Ad == temiz);
        Require(kanal is not null, "Kayıtlı bir kasa (kanal) seçin.");
        return kanal!.Id;
    }

    /// <summary>Kırpılmış metin; boşsa null. Zorunlu alan boş, sınırı aşan ya da görünmeyen karakter taşıyan metin 400.</summary>
    private static string? CekMetni(string? deger, string alan, int sinir, bool zorunlu = true)
    {
        var temiz = string.IsNullOrWhiteSpace(deger) ? null : deger.Trim();
        Require(!zorunlu || temiz is not null, $"{alan} boş olamaz.");
        Require(temiz is null || temiz.Length <= sinir, $"{alan} en çok {sinir} karakter olabilir.");
        if (GirdiDogrulama.GecersizKarakterIletisi(temiz) is { } ileti)
            Require(false, $"{alan}: {ileti}");
        return temiz;
    }
}
```

`Kasa.Api/FinansTakipEndpoints.cs` `MapFinansTakipEndpoints` sonunda:

```csharp
        MapLoans(api);
        MapCekEndpoints(api);
        return app;
```

- [ ] **Adım 7: Uç envanterini yeniden yaz ve farkı incele.**

Çalıştır (PowerShell): `$env:KASA_UC_ENVANTERI_YAZ="1"; dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~UcEnvanteriTests"; Remove-Item Env:KASA_UC_ENVANTERI_YAZ`
(Git Bash: `KASA_UC_ENVANTERI_YAZ=1 dotnet test …`). Beklenen: test bilerek düşer ("Uç envanteri yazıldı").
`git diff --stat Kasa.Api.Tests/Altin/uc-envanteri.txt` ≈ 189 ekleme, 66 silme: ilk satır `uç sayısı: 125` → `uç sayısı: 132`, yedi yeni
`/api/takip/cekler…` uç bloğu (her birinde `Finans` politikası; yazmalarda ek olarak `Editor`) ve sonraki uçların yeniden
numaralanması. Başka değişiklik varsa durup incele.

- [ ] **Adım 8: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekUcTests|FullyQualifiedName~UcEnvanteriTests|FullyQualifiedName~UcYetkiTaramasiTests"`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:    38` (CekUcTests 8).

- [ ] **Adım 9: Commit.**

```bash
git add Kasa.Api/CekDtos.cs Kasa.Api/CekServisi.cs Kasa.Api/CekEndpoints.cs Kasa.Api/FinansTakipEndpoints.cs Kasa.Api.Tests/CekUcTests.cs Kasa.Api.Tests/UcYetkiTaramasiTests.cs Kasa.Api.Tests/Altin/uc-envanteri.txt
git commit -F - <<'MESAJ'
feat(api): çek uçları

/api/takip/cekler: liste (yön, durum, arama, vade süzgeçleri), tek kayıt, ekleme,
düzeltme, silme, hareket ve son hareketi geri alma. istekId tekrar koruması, sürüm
(409), aynı çek uyarısı; okuma Finans, yazma Editor politikasıyla.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 6: Değişiklik geçmişi

**Dosyalar:**
- Oluştur: `Kasa.Api.Tests/CekDenetimTests.cs`, `Kasa.App.Core.Tests/CekKasaKontrolMetniTests.cs`
- Değiştir: `Kasa.App.Core/KasaKontrolViewModel.cs` (`KasaKontrolMetni` sözlükleri)

`DenetimYakalayici` yeni varlıkları listeye eklenmeden yakalar (doğrulanan varsayım 6); bu görev bunu sabitler ve masaüstünde kasa
dökümü ile değişiklik geçmişindeki adları ekler.

- [ ] **Adım 1: Sunucu testini yaz.** `Kasa.Api.Tests/CekDenetimTests.cs`:

```csharp
using System.Net.Http.Json;
using Kasa.Api.Denetim;
using Kasa.Core.Kodlar;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek ve hareketleri değişiklik geçmişine düşer (docs/specs/2026-10-01-cekler.md "Uçlar"): DenetimYakalayici izlenen her
/// varlığı kendiliğinden yakalar (varlık adı tür adından: Cek, CekHareket); konum değişikliği önceki/yeni değerle, geri alınan hareket
/// silme olayıyla ve istek kimliğiyle yazılır. Yalnız sürüm sayacı değişen çek olay üretmez.</summary>
public class CekDenetimTests
{
    private static Task<List<DenetimOlayDto>> Olaylar(HttpClient c, string varlik, int id) =>
        c.GetFromJsonAsync<List<DenetimOlayDto>>($"/api/denetim?varlik={varlik}&varlikId={id}", TestContext.Current.CancellationToken)!;

    [Fact]
    public async Task Cek_konum_degisikligi_ve_geri_alinan_hareket_degisiklik_gecmisine_yazilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var yaz = new CekYaz(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Alinan, "12345", "Ziraat", "Ahmet Yılmaz", 50_000m, Today.AddDays(20), null, false, null, null);
        var cek = await Post<CekDto>(c, "/api/takip/cekler", yaz);
        var hareketIstegi = new CekHareketYaz(Guid.NewGuid(), cek.Surum, CekHareketTurleri.Tahsilat, Today, 1_000m, null, "MEZAT", null);
        cek = await Post<CekDto>(c, $"/api/takip/cekler/{cek.Id}/hareketler", hareketIstegi);
        var hareket = Assert.Single(cek.Hareketler);
        (await c.PutAsJsonAsync($"/api/takip/cekler/{cek.Id}", yaz with { IstekId = Guid.NewGuid(), Surum = cek.Surum, Konum = CekKonumlari.BankadaTahsilde },
            TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        cek = (await c.GetFromJsonAsync<CekDto>($"/api/takip/cekler/{cek.Id}", TestContext.Current.CancellationToken))!;
        var geriAl = new CekSilYaz(Guid.NewGuid(), cek.Surum);
        using (var istek = new HttpRequestMessage(HttpMethod.Delete, $"/api/takip/cekler/{cek.Id}/hareketler/son") { Content = JsonContent.Create(geriAl) })
            (await c.SendAsync(istek, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var cekOlaylari = await Olaylar(c, "Cek", cek.Id);
        Assert.Equal(["Degistir", "Ekle"], cekOlaylari.Select(o => o.Tur));
        Assert.Contains(CekKonumlari.Elde, cekOlaylari[0].OncekiJson!, StringComparison.Ordinal);
        Assert.Contains(CekKonumlari.BankadaTahsilde, cekOlaylari[0].YeniJson!, StringComparison.Ordinal);
        var hareketOlaylari = await Olaylar(c, "CekHareket", hareket.Id);
        Assert.Equal(["Sil", "Ekle"], hareketOlaylari.Select(o => o.Tur));
        Assert.Equal((geriAl.IstekId, hareketIstegi.IstekId), (hareketOlaylari[0].IstekId, hareketOlaylari[1].IstekId));
        Assert.Contains(CekHareketTurleri.Tahsilat, hareketOlaylari[0].OncekiJson!, StringComparison.Ordinal);
    }
}
```

- [ ] **Adım 2: Masaüstü ad testini yaz.** `Kasa.App.Core.Tests/CekKasaKontrolMetniTests.cs`:

```csharp
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

/// <summary>Kasa dökümü ve değişiklik geçmişinde çek satırlarının görünen adları (docs/specs/2026-10-01-cekler.md).</summary>
public class CekKasaKontrolMetniTests
{
    private static DenetimOlayDto Olay(string varlik, string id) =>
        new(1, DateTimeOffset.UnixEpoch, "editor", null, null, "Ekle", varlik, id, null, "{}", null, null, null, null);

    [Fact]
    public void Cek_hareket_turu_ve_denetim_varliklari_turkce_adlanir()
    {
        Assert.Equal("Çek", KasaKontrolMetni.HareketTuru(KasaHareketTurleri.Cek));
        Assert.Equal("Çek #4", KasaKontrolMetni.Kayit(Olay("Cek", "4")));
        Assert.Equal("Çek hareketi #9", KasaKontrolMetni.Kayit(Olay("CekHareket", "9")));
    }
}
```

- [ ] **Adım 3: Testleri çalıştır.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekDenetimTests"`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:     1` (yakalama kendiliğinden; kod değişmez).

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekKasaKontrolMetniTests"`
Beklenen: düşer (`"Cek"` beklenen "Çek", `"Cek #4"`, `"CekHareket #9"`).

- [ ] **Adım 4: Adları ekle.** `Kasa.App.Core/KasaKontrolViewModel.cs` `KasaKontrolMetni.Hareketler` sözlüğünün son girdisinden sonra:

```csharp
        [KasaHareketTurleri.KartAySonu] = "Eski kart ay sonu düşümü",
        [KasaHareketTurleri.Cek] = "Çek",
    };
```

`Varliklar` sözlüğünün son girdisinden sonra (`"Cek"` Görev 1'de çok anlamlı değer olarak işaretlendi):

```csharp
        ["HesapHareket"] = "Hesap hareketi",
        ["Cek"] = "Çek",
        ["CekHareket"] = "Çek hareketi",
    };
```

- [ ] **Adım 5: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:  1051`.

- [ ] **Adım 6: Commit.**

```bash
git add Kasa.Api.Tests/CekDenetimTests.cs Kasa.App.Core.Tests/CekKasaKontrolMetniTests.cs Kasa.App.Core/KasaKontrolViewModel.cs
git commit -F - <<'MESAJ'
feat(app-core): çek değişiklik geçmişi ve kasa dökümü adları

Çek ve hareketleri değişiklik geçmişine kendiliğinden düşer (test sabitler); masaüstü
kasa dökümünde "Çek" türünü, değişiklik geçmişinde "Çek" ve "Çek hareketi" adlarını gösterir.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 7: Bildirim kaynağı ve masaüstü hedefi

**Dosyalar:**
- Oluştur: `Kasa.Api/Servisler/CekBildirimleri.cs`, `Kasa.Api.Tests/CekBildirimTests.cs`
- Değiştir: `Kasa.Api/Servisler/BildirimServisi.cs`, `Kasa.Api.Tests/BildirimTests.cs`, `Kasa.App.Core/BildirimHedefi.cs`,
  `Kasa.App.Core.Tests/BildirimHedefiTests.cs`

- [ ] **Adım 1: Kural testini yaz.** `Kasa.Api.Tests/CekBildirimTests.cs` (bugün 25 Eylül 2026):

```csharp
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek vade hatırlatmaları (docs/specs/2026-10-01-cekler.md "Bildirimler"): üç kural, günleri, metinleri ve hedef; teminat,
/// kapanmış ve karşılıksız çek hariç. Bugün 25 Eylül 2026.</summary>
public class CekBildirimTests
{
    [Fact]
    public async Task Uc_kural_gunleri_metinleri_ve_hedefi()
    {
        await using var f = Fabrika();
        await Editor(f);
        var vade3 = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(vade: Today.AddDays(3)));
        var kismen = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "K-1", vade: Today),
            new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Today.AddDays(-1), Tutar = 20_000.5m, KanalId = 1 });
        var ibraz = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "I-1", vade: Today.AddDays(-7)));
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "I-2", vade: Today.AddDays(-7)),
            new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 1m, KanalId = 1 });
        var verilen = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, 30_000m, kisi: "Mehmet Ticaret", no: "V-1", vade: Today.AddDays(3)));
        var senet = CekVeriModeliTests.Cek(no: "S-1", vade: Today);
        senet.Tur = CekTurleri.Senet;
        senet = await CekRaporTests.CekEkle(f, senet);
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "T-1", teminat: true, vade: Today));
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "G-2", vade: Today.AddDays(2)));
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "KS-1", vade: Today),
            new CekHareketEntity { Tur = CekHareketTurleri.Karsiliksiz, Tarih = Today });
        await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(no: "TE-1", vade: Today),
            new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 50_000m, KanalId = 1 });

        using var scope = f.Services.CreateScope();
        var taslaklar = CekBildirimleri.Oku(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), Today).OrderBy(t => t.KaynakId).ToList();
        Assert.Equal(
        [
            ($"Cekler:{vade3.Id}:Vade:2026-09-28:3", "Çek vadesi", "Ahmet Yılmaz · 50.000,00 TL · 28 Eylül", $"/#cheques/{vade3.Id}", CekBildirimleri.VadeTuru),
            ($"Cekler:{kismen.Id}:Vade:2026-09-25:0", "Çek vadesi", "Ahmet Yılmaz · 29.999,50 TL · 25 Eylül", $"/#cheques/{kismen.Id}", CekBildirimleri.VadeTuru),
            ($"Cekler:{ibraz.Id}:Ibraz:2026-09-18:-7", "İbraz süresi doluyor", "Ahmet Yılmaz · 50.000,00 TL · vade 18 Eylül", $"/#cheques/{ibraz.Id}", CekBildirimleri.IbrazTuru),
            ($"Cekler:{verilen.Id}:Odenecek:2026-09-28:3", "Ödenecek çek", "Mehmet Ticaret · 30.000,00 TL · hesapta bulunmalı", $"/#cheques/{verilen.Id}", CekBildirimleri.OdemeTuru),
            ($"Cekler:{senet.Id}:Vade:2026-09-25:0", "Senet vadesi", "Ahmet Yılmaz · 50.000,00 TL · 25 Eylül", $"/#cheques/{senet.Id}", CekBildirimleri.VadeTuru),
        ], taslaklar.Select(t => (t.Anahtar, t.Baslik, t.Mesaj, t.Hedef, t.Tur)));
        Assert.All(taslaklar, t => Assert.Equal(Today, t.Tarih));
    }

    [Fact]
    public void Hesaplanamayan_cek_kaynaginin_uyarisi_cekler_sayfasini_hedefler()
        => Assert.Equal("/#cheques", BildirimTakvimi.Hata(new(CekBildirimleri.Kaynak, 0, "Çek hatırlatmaları", new InvalidOperationException()), Today).Hedef);
}
```

- [ ] **Adım 2: Kuyruk testini yaz.** `Kasa.Api.Tests/BildirimTests.cs` başına `using Kasa.Core.Kodlar;` ekle; `Fikstur` sınıfından
  hemen önce:

```csharp
    /// <summary>Çek vade hatırlatması (CekBildirimleri) bildirim kuyruğuna çek hedefiyle girer; teminat çeki girmez.</summary>
    [Fact]
    public async Task Cek_vadesi_bildirimi_cek_hedefiyle_kuyruga_girer_teminat_cekine_girmez()
    {
        using var fixture = new Fikstur();
        var cek = new CekEntity { Tur = CekTurleri.Cek, Yon = CekYonleri.Alinan, No = "12345", Banka = "Ziraat", Kisi = "Ahmet Yılmaz", Tutar = 50_000m, VadeTarihi = Day.AddDays(3), Surum = 1 };
        fixture.Db.Cekler.AddRange(cek, new CekEntity { Tur = CekTurleri.Cek, Yon = CekYonleri.Alinan, No = "T-1", Banka = "Ziraat", Kisi = "Ali", Tutar = 1m, VadeTarihi = Day, Teminat = true, Surum = 1 });
        fixture.Db.SaveChanges();
        await fixture.Service().Yenile(TestContext.Current.CancellationToken);
        var bildirim = Assert.Single(fixture.Db.Set<BildirimEntity>().AsNoTracking());
        Assert.Equal(("Çek vadesi", "Ahmet Yılmaz · 50.000,00 TL · 26 Eylül", $"/#cheques/{cek.Id}", CekBildirimleri.VadeTuru, cek.Id),
            (bildirim.Baslik, bildirim.Mesaj, bildirim.Hedef, bildirim.Tur, bildirim.KaynakId));
    }
```

- [ ] **Adım 3: Masaüstü hedef testini yaz.** `Kasa.App.Core.Tests/BildirimHedefiTests.cs` içinde `[InlineData("/#loans", "//krediler")]`
  satırının altına:

```csharp
    [InlineData("/#cheques/5", "//cekler?CekId=5")]
    [InlineData("/#cheques", "//cekler")]
    [InlineData("/#cheques/0", "//bildirimler")]
```

- [ ] **Adım 4: Testlerin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekBildirimTests"`
Beklenen: derleme hatası (CS0103 `CekBildirimleri`).

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~BildirimHedefi"`
Beklenen: `/#cheques/5` ve `/#cheques` düşer (`//bildirimler` döner).

- [ ] **Adım 5: Kaynağı yaz.** `Kasa.Api/Servisler/CekBildirimleri.cs`:

```csharp
using System.Globalization;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;

namespace Kasa.Api.Servisler;

/// <summary>
/// Çek ve senet vade hatırlatmaları (docs/specs/2026-10-01-cekler.md "Bildirimler"); <see cref="KasaEsikServisi"/> gibi
/// <see cref="BildirimServisi"/>'nin taslaklarına ayrı kaynak olarak girer. Teminat çekleri ve portföyde ya da kısmen tahsil
/// edilmiş / ödenmiş olmayan (kapanmış, ciro edilmiş, kırdırılmış, karşılıksız) çekler hariçtir. Kurallar:
/// <list type="bullet">
/// <item>Alınan, portföyde ya da kısmen: vadeye 3 gün kala ve vade günü "Çek vadesi" (kısmide kalan tutar).</item>
/// <item>Alınan, portföyde: vadenin 7. günü "İbraz süresi doluyor".</item>
/// <item>Verilen, portföyde ya da kısmen: vadeye 3 gün kala ve vade günü "Ödenecek çek".</item>
/// </list>
/// Hedef "/#cheques/{id}": masaüstü bunu Çekler sayfasında o çeke çevirir (Kasa.App.Core BildirimHedefi). Anahtar çek, kural,
/// vade ve gün farkını taşır: vade değişirse eski hatırlatma iptal olur, yenisi oluşur.
/// </summary>
public static class CekBildirimleri
{
    /// <summary>Hesaplanamayan çek hatırlatmalarının kaynak adı (BildirimKaynakHatasi.Kaynak).</summary>
    public const string Kaynak = "Cekler";
    public const string VadeTuru = "CekVade";
    public const string IbrazTuru = "CekIbraz";
    public const string OdemeTuru = "CekOdeme";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<BildirimTaslagi> Oku(KasaDbContext db, DateOnly today)
    {
        var sonuc = new List<BildirimTaslagi>();
        foreach (var k in CekServisi.Oku(db).Where(k => !k.Cek.Teminat && k.Durum.Acik))
        {
            var c = k.Cek;
            var ad = c.Tur switch { CekTurleri.Senet => "Senet", _ => "Çek" };
            var fark = c.VadeTarihi.DayNumber - today.DayNumber;
            var vade = c.VadeTarihi.ToString("d MMMM", Tr);
            void Ekle(string kural, string tur, string baslik, string mesaj) => sonuc.Add(new(
                $"{Kaynak}:{c.Id}:{kural}:{c.VadeTarihi.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{fark}", baslik, mesaj, today, $"/#cheques/{c.Id}", tur, c.Id));
            if (c.Yon == CekYonleri.Verilen)
            {
                if (fark is 3 or 0)
                    Ekle("Odenecek", OdemeTuru, $"Ödenecek {ad.ToLower(Tr)}", $"{c.Kisi} · {Tl(k.Durum.Kalan)} · hesapta bulunmalı");
                continue;
            }
            if (fark is 3 or 0)
                Ekle("Vade", VadeTuru, $"{ad} vadesi", $"{c.Kisi} · {Tl(k.Durum.Kalan)} · {vade}");
            if (fark == -7 && k.Durum.Durum == CekDurumlari.Portfoyde)
                Ekle("Ibraz", IbrazTuru, "İbraz süresi doluyor", $"{c.Kisi} · {Tl(k.Durum.Kalan)} · vade {vade}");
        }
        return sonuc;
    }

    /// <summary>Kart ve kredi bildirimleriyle aynı biçim: her zaman kuruşlu, "50.000,00 TL" (ürün sahibi 2026-10-01).</summary>
    private static string Tl(decimal tutar) => tutar.ToString("N2", Tr) + " TL";
}
```

- [ ] **Adım 6: Kaynağı bildirim taslaklarına bağla.** `Kasa.Api/Servisler/BildirimServisi.cs`:
  - `BildirimKaynakHatasi` özetinde "(kart, kredi, kasa alt sınırı)" → "(kart, kredi, kasa alt sınırı, çek)"; `Taslaklar` üstündeki yorumda
    "(kart, kredi, kasa alt sınırı)" → "(kart, kredi, kasa alt sınırı, çek)".
  - `BildirimTakvimi.Hata` hedef switch'ine kredi kolundan sonra:

```csharp
            TakipKaynaklari.Kredi => $"/#loans/{h.KaynakId}",
            CekBildirimleri.Kaynak => "/#cheques",
            _ => "/#home"
```

  - `Taslaklar` içinde kasa eşiği `try/catch` bloğunun kapanışından sonra:

```csharp
        // Çek vade hatırlatmaları ayrı kaynaktır: hesaplanamazsa kart, kredi ve kasa hatırlatmaları sürer, editöre uyarı gider.
        IReadOnlyList<BildirimTaslagi> cekler = [];
        try
        { cekler = CekBildirimleri.Oku(db, today); }
        catch (Exception e) when (!BildirimHatalari.Gecici(e))
        {
            db.ChangeTracker.Clear();
            hatalar.Add(new(CekBildirimleri.Kaynak, 0, "Çek hatırlatmaları", e));
        }
```

  - `Taslaklar`'ın dönüş satırını şununla değiştir:

```csharp
        return [.. BildirimTakvimi.Olustur(olaylar, today), .. esik, .. cekler, .. hatalar.Select(h => BildirimTakvimi.Hata(h, today))];
```

- [ ] **Adım 7: Masaüstü hedefini yaz.** `Kasa.App.Core/BildirimHedefi.cs` özetinin ilk üç satırını

```csharp
/// <summary>Sunucu bildiriminin <c>Hedef</c> alanını (web rotası: "/#cards/3", "/#loans/7", "/#cheques/5") masaüstü Shell rotasına
/// çevirir (tasarım 2026-09-30 masaüstü bildirimleri §1 Tıklama; 2026-10-01 çekler "Bildirimler"). Kart → Kartlar sayfasında o kart
/// (KartId), kredi → Krediler sayfasında o kredi (KrediId), çek → Çekler sayfasında o çek (CekId); kimliksiz hedef yalnız sayfayı
/// açar; tanınmayan ya da boş hedef Bildirimler sayfasını açar.
```

ile, switch'in son iki kolunu ve deseni şununla değiştir:

```csharp
            ("loans", true) => "//krediler?KrediId=" + kimlik.Value,
            ("loans", false) => "//krediler",
            ("cheques", true) => "//cekler?CekId=" + kimlik.Value,
            _ => "//cekler",
        };
    }

    [GeneratedRegex(@"^/#(?<tur>cards|loans|cheques)(?:/(?<id>[1-9][0-9]{0,8}))?\z")]
```

(`BildirimHedefiTests` özetine "çek → Çekler'de o çek" eklenir.)

- [ ] **Adım 8: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekBildirimTests|FullyQualifiedName~BildirimTests|FullyQualifiedName~BildirimIscisiTests"`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:    62`.

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:  1054`.

- [ ] **Adım 9: Commit.**

```bash
git add Kasa.Api/Servisler/CekBildirimleri.cs Kasa.Api/Servisler/BildirimServisi.cs Kasa.Api.Tests/CekBildirimTests.cs Kasa.Api.Tests/BildirimTests.cs Kasa.App.Core/BildirimHedefi.cs Kasa.App.Core.Tests/BildirimHedefiTests.cs
git commit -F - <<'MESAJ'
feat(api): çek vade hatırlatmaları

Alınan çekte vadeye 3 gün kala ve vade günü "Çek vadesi", vade+7'de "İbraz süresi
doluyor"; verilende "Ödenecek çek". Teminat ve kapanmış çekler hariç; hedef
/#cheques/{id}, masaüstünde //cekler?CekId={id}.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 8: Panel özeti ucu

**Dosyalar:**
- Değiştir: `Kasa.Api/CekDtos.cs`, `Kasa.Api/CekServisi.cs`, `Kasa.Api/CekEndpoints.cs`, `Kasa.Api.Tests/Altin/uc-envanteri.txt`
- Oluştur: `Kasa.Api.Tests/CekOzetTests.cs`

- [ ] **Adım 1: Özet testini yaz.** `Kasa.Api.Tests/CekOzetTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Kasa.Core.Kodlar;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek panel özeti (GET /api/takip/cekler/ozet; docs/specs/2026-10-01-cekler.md "Panel"): portföydeki alınan, 30 gün içinde
/// tahsil edilecek alınan, 30 gün içinde ödenecek verilen ve vadesi geçmiş tahsil edilmemiş alınan; tutarlar kalandır, teminat ve
/// kapanmış çekler dışarıdadır. Bugün 25 Eylül 2026.</summary>
public class CekOzetTests
{
    private const string Yol = "/api/takip/cekler";

    private static CekYaz Alinan(string no, decimal tutar, DateOnly vade, bool teminat = false) =>
        new(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Alinan, no, "Ziraat", "Ahmet Yılmaz", tutar, vade, null, teminat, null, null);

    [Fact]
    public async Task Ozet_kalan_tutarlari_teminatsiz_ve_acik_ceklerden_toplar()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<CekDto>(c, Yol, Alinan("A-1", 10_000m, Today.AddDays(5)));
        await Post<CekDto>(c, Yol, Alinan("A-2", 20_000m, Today.AddDays(60)));
        await Post<CekDto>(c, Yol, Alinan("A-3", 40_000m, Today.AddDays(3), teminat: true));
        var gecmis = await Post<CekDto>(c, Yol, Alinan("A-4", 5_000m, Today.AddDays(-2)));
        await Post<CekDto>(c, $"{Yol}/{gecmis.Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), gecmis.Surum, CekHareketTurleri.Tahsilat, Today, 2_000m, null, "MEZAT", null));
        var kapali = await Post<CekDto>(c, Yol, Alinan("A-5", 1_000m, Today.AddDays(1)));
        await Post<CekDto>(c, $"{Yol}/{kapali.Id}/hareketler", new CekHareketYaz(Guid.NewGuid(), kapali.Surum, CekHareketTurleri.Iade, Today, 0m, null, null, null));
        await Post<CekDto>(c, Yol, new CekYaz(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Verilen, "777", "Halk", "Mehmet Ticaret", 7_000m, Today.AddDays(30),
            KanalEtiketleri.Ortak, false, null, null));
        await Post<CekDto>(c, Yol, new CekYaz(Guid.NewGuid(), 0, CekTurleri.Senet, CekYonleri.Verilen, "S-9", null, "Veli", 9_000m, Today.AddDays(31),
            "MEZAT", false, null, null));

        var ozet = (await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken))!;
        Assert.Equal(Today, ozet.Tarih);
        Assert.Equal(new CekOzetKalemi(3, 33_000m), ozet.PortfoydekiAlinan);
        Assert.Equal(new CekOzetKalemi(1, 10_000m), ozet.Alinan30);
        Assert.Equal(new CekOzetKalemi(1, 7_000m), ozet.Verilen30);
        Assert.Equal(new CekOzetKalemi(1, 3_000m), ozet.VadesiGecmis);
    }

    [Fact]
    public async Task Bos_ozet_sifirdir_izleyici_okur()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        Assert.Equal(new CekOzetDto(Today, new(0, 0m), new(0, 0m), new(0, 0m), new(0, 0m)),
            await c.GetFromJsonAsync<CekOzetDto>(Yol + "/ozet", TestContext.Current.CancellationToken));
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-ozet-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-ozet-sifresi" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync(Yol + "/ozet", TestContext.Current.CancellationToken)).StatusCode);
    }
}
```

- [ ] **Adım 2: Testin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekOzetTests"`
Beklenen: derleme hatası (CS0246 `CekOzetDto`, `CekOzetKalemi`).

- [ ] **Adım 3: DTO'ları ekle.** `Kasa.Api/CekDtos.cs` sonuna:

```csharp
public record CekOzetKalemi(int Adet, decimal Toplam);

/// <summary>Çek panel özeti (GET /api/takip/cekler/ozet). Teminat çekleri ve kapanmış çekler dışarıdadır; tutarlar kalandır. Otuz gün
/// bugünden bugün+30'a kadardır (ikisi dahil). Vadesi geçmiş: vadesi bugünden önce, portföyde ya da kısmen tahsil edilmiş alınan çek.</summary>
public record CekOzetDto(DateOnly Tarih, CekOzetKalemi PortfoydekiAlinan, CekOzetKalemi Alinan30, CekOzetKalemi Verilen30, CekOzetKalemi VadesiGecmis);
```

- [ ] **Adım 4: Hesabı ekle.** `Kasa.Api/CekServisi.cs` içinde `private static List<(int Id, string Yon, string? Banka, string No)> Benzerler`
  satırından hemen önce:

```csharp
    internal static CekOzetDto Ozet(TakipHesapBaglami b)
    {
        var bugun = b.Bugun;
        var sinir = bugun.AddDays(30);
        var acik = Oku(b.Db).Where(k => !k.Cek.Teminat && k.Durum.Acik).ToList();
        var alinan = acik.Where(k => k.Cek.Yon == CekYonleri.Alinan).ToList();
        return new(bugun, Kalem(alinan), Kalem(alinan.Where(k => k.Cek.VadeTarihi >= bugun && k.Cek.VadeTarihi <= sinir)),
            Kalem(acik.Where(k => k.Cek.Yon == CekYonleri.Verilen && k.Cek.VadeTarihi >= bugun && k.Cek.VadeTarihi <= sinir)),
            Kalem(alinan.Where(k => k.Cek.VadeTarihi < bugun)));
    }

    private static CekOzetKalemi Kalem(IEnumerable<Kayit> kayitlar)
    {
        var liste = kayitlar.ToList();
        return new(liste.Count, liste.Sum(k => k.Durum.Kalan));
    }

```

- [ ] **Adım 5: Ucu ekle.** `Kasa.Api/CekEndpoints.cs` içinde `api.MapGet("/cekler/{id:int}", …` satırından hemen önce:

```csharp
        api.MapGet("/cekler/ozet", (KasaDbContext db, CancellationToken ct) => View(db, b => CekServisi.Ozet(b), ct));
```

- [ ] **Adım 6: Uç envanterini yeniden yaz ve farkı incele.** Görev 5 Adım 7'deki komut. Beklenen fark ≈ 86 ekleme, 72 silme: `uç sayısı: 132` → `uç sayısı:
  133`, yeni `GET /api/takip/cekler/ozet` bloğu (`Finans` politikası) ve yeniden numaralama.

- [ ] **Adım 7: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekOzetTests|FullyQualifiedName~UcEnvanteriTests|FullyQualifiedName~UcYetkiTaramasiTests"`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:    32`.

Çalıştır (tamamı): `dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:  1256`.

- [ ] **Adım 8: Commit.**

```bash
git add Kasa.Api/CekDtos.cs Kasa.Api/CekServisi.cs Kasa.Api/CekEndpoints.cs Kasa.Api.Tests/CekOzetTests.cs Kasa.Api.Tests/Altin/uc-envanteri.txt
git commit -F - <<'MESAJ'
feat(api): çek panel özeti ucu

GET /api/takip/cekler/ozet: portföydeki alınan, 30 gün içinde tahsil edilecek alınan,
30 gün içinde ödenecek verilen ve vadesi geçmiş tahsil edilmemiş alınan (kalan
tutarlar; teminat ve kapanmış çekler hariç).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---
## Görev 9: ApiClient ve sözleşme

**Dosyalar:**
- Oluştur: `Kasa.ApiClient/CekDtos.cs` (DTO'lar ve `ICekApi`), `Kasa.ApiClient/KasaApiClient.Cekler.cs`, `Kasa.ApiClient.Tests/CekApiTests.cs`,
  `Kasa.Sozlesme.Tests/CekSozlesmeTests.cs`
- Değiştir: `Kasa.Sozlesme.Tests/SozlesmeTemeli.cs`

İstemci DTO'ları sunucudakilerle aynı adı ve alanları taşır: `DtoEslesmeTests` onları adla eşler; `KapsamTests` her yeni `ICekApi`
metodunun bir sözleşme testinde çağrılmasını ister.

- [ ] **Adım 1: İstemci testlerini yaz.** `Kasa.ApiClient.Tests/CekApiTests.cs`:

```csharp
using System.Net;
using System.Text.Json;

namespace Kasa.ApiClient.Tests;

/// <summary>Çek istemcisi: yollar, yöntemler, sorgu dizesi, DELETE gövdesi ve yanıt okuma.</summary>
public class CekApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static KasaApiClient Client(SahteHandler h) => new(new HttpClient(h) { BaseAddress = new("https://ornek.test/") }, new BellekTokenStore());
    private const string Cek = """{"id":7,"surum":3,"tur":"Cek","yon":"Alinan","no":"12345","banka":"Ziraat","kisi":"Ahmet","tutar":50000.00,"vadeTarihi":"2026-11-30","kanalId":null,"kanal":null,"teminat":false,"konum":"Elde","not":null,"durum":"KismenTahsilEdildi","kalan":29999.50,"izinliHareketler":["Tahsilat","Karsiliksiz","Iade"],"hareketler":[{"id":4,"sira":1,"tur":"Tahsilat","tarih":"2026-09-25","tutar":20000.50,"netTutar":null,"kanalId":1,"kanal":"MEZAT","karsi":null}],"uyari":"Aynı çek"}""";

    [Fact]
    public async Task Liste_suzgecleri_sorgu_dizesine_kacisli_yazar_bos_suzgec_yazilmaz()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, "[" + Cek + "]").Kuyrukla(HttpStatusCode.OK, "[]");
        var liste = await Client(h).CeklerAsync("Alinan", "Portfoyde", " Ahmet Y ", new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 25));
        Assert.Equal("/api/takip/cekler?yon=Alinan&durum=Portfoyde&ara=Ahmet%20Y&vadeBas=2026-09-25&vadeSon=2026-10-25", h.SonIstek!.RequestUri!.PathAndQuery);
        var cek = Assert.Single(liste);
        Assert.Equal((29_999.50m, "KismenTahsilEdildi", 3, "Aynı çek"), (cek.Kalan, cek.Durum, cek.IzinliHareketler.Count, cek.Uyari));
        Assert.Equal(("MEZAT", 20_000.50m), (cek.Hareketler[0].Kanal, cek.Hareketler[0].Tutar));
        await Client(h).CeklerAsync();
        Assert.Equal("/api/takip/cekler", h.SonIstek!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Kaydet_ekler_ya_da_duzeltir_hareket_ve_geri_alma_govdeyle_gider()
    {
        var h = new SahteHandler().Kuyrukla(HttpStatusCode.OK, Cek).Kuyrukla(HttpStatusCode.OK, Cek).Kuyrukla(HttpStatusCode.OK, Cek).Kuyrukla(HttpStatusCode.OK, Cek)
            .Kuyrukla(HttpStatusCode.NoContent);
        var c = Client(h);
        var yaz = new CekYaz(Guid.NewGuid(), 0, "Cek", "Verilen", "777", "Halk", "Mehmet", 30_000m, new(2026, 10, 5), "Ortak", false, null, null);
        await c.CekKaydetAsync(null, yaz);
        Assert.Equal((HttpMethod.Post, "/api/takip/cekler"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        Assert.Equal(yaz, JsonSerializer.Deserialize<CekYaz>(h.SonGovde!, Json));
        await c.CekKaydetAsync(7, yaz with { Surum = 3 });
        Assert.Equal((HttpMethod.Put, "/api/takip/cekler/7"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        var hareket = new CekHareketYaz(Guid.NewGuid(), 3, "Kirdirma", new(2026, 9, 25), 50_000m, 48_750.25m, "MEZAT", "Faktoring");
        await c.CekHareketEkleAsync(7, hareket);
        Assert.Equal((HttpMethod.Post, "/api/takip/cekler/7/hareketler"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        Assert.Equal(hareket, JsonSerializer.Deserialize<CekHareketYaz>(h.SonGovde!, Json));
        var geriAl = new CekSilYaz(Guid.NewGuid(), 4);
        Assert.Equal(7, (await c.CekHareketGeriAlAsync(7, geriAl)).Id);
        Assert.Equal((HttpMethod.Delete, "/api/takip/cekler/7/hareketler/son"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
        Assert.Equal(geriAl, JsonSerializer.Deserialize<CekSilYaz>(h.SonGovde!, Json));
        await c.CekSilAsync(7, geriAl);
        Assert.Equal((HttpMethod.Delete, "/api/takip/cekler/7"), (h.SonIstek!.Method, h.SonIstek.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task Ozet_ve_tek_kayit_okunur()
    {
        var h = new SahteHandler()
            .Kuyrukla(HttpStatusCode.OK, """{"tarih":"2026-09-25","portfoydekiAlinan":{"adet":3,"toplam":33000.00},"alinan30":{"adet":1,"toplam":10000},"verilen30":{"adet":0,"toplam":0},"vadesiGecmis":{"adet":1,"toplam":3000.5}}""")
            .Kuyrukla(HttpStatusCode.OK, Cek);
        var ozet = await Client(h).CekOzetAsync();
        Assert.Equal("/api/takip/cekler/ozet", h.SonIstek!.RequestUri!.AbsolutePath);
        Assert.Equal((new CekOzetKalemi(3, 33_000m), new CekOzetKalemi(1, 3_000.5m)), (ozet.PortfoydekiAlinan, ozet.VadesiGecmis));
        Assert.Equal(7, (await Client(h).CekAsync(7)).Id);
        Assert.Equal("/api/takip/cekler/7", h.SonIstek!.RequestUri!.AbsolutePath);
    }
}
```

- [ ] **Adım 2: Sözleşme testini yaz.** `Kasa.Sozlesme.Tests/CekSozlesmeTests.cs`:

```csharp
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.Sozlesme.Tests;

/// <summary>Çek ve senet (docs/specs/2026-10-01-cekler.md): ekleme, düzeltme, liste ve süzgeçler, hareket, geri alma, silme ve panel
/// özeti; reddedilen geçişin Türkçe iletisi istemciye ulaşır.</summary>
public class CekSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(ICekApi.CekKaydetAsync), nameof(ICekApi.CeklerAsync), nameof(ICekApi.CekAsync), nameof(ICekApi.CekHareketEkleAsync),
        nameof(ICekApi.CekHareketGeriAlAsync), nameof(ICekApi.CekSilAsync), nameof(ICekApi.CekOzetAsync))]
    public async Task Cek_yasam_dongusu_istemci_turlerine_birebir_uyar()
    {
        var o = await Editor();
        await o.Kasa.AyarGuncelleAsync(new AyarYaz(Baslangic, 1000m));
        var yaz = new CekYaz(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Alinan, "12345", "Ziraat", "Ahmet Yılmaz", 50_000m, Bugun.AddDays(10), null, false, null, "İlk not");
        var cek = await o.Cek.CekKaydetAsync(null, yaz);
        Assert.Equal((CekDurumlari.Portfoyde, 50_000m, CekKonumlari.Elde, 1), (cek.Durum, cek.Kalan, cek.Konum, cek.Surum));
        cek = await o.Cek.CekKaydetAsync(cek.Id, yaz with { IstekId = Guid.NewGuid(), Surum = cek.Surum, Konum = CekKonumlari.BankadaTahsilde });
        Assert.Equal(CekKonumlari.BankadaTahsilde, cek.Konum);

        cek = await o.Cek.CekHareketEkleAsync(cek.Id, new CekHareketYaz(Guid.NewGuid(), cek.Surum, CekHareketTurleri.Kirdirma, Bugun, 50_000m, 48_750.25m, "MEZAT", "Faktoring A.Ş."));
        var kirdirma = Assert.Single(cek.Hareketler);
        Assert.Equal((CekDurumlari.Kirdirildi, 48_750.25m, "MEZAT", "Faktoring A.Ş."), (cek.Durum, kirdirma.NetTutar, kirdirma.Kanal, kirdirma.Karsi));
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => o.Cek.CekHareketEkleAsync(cek.Id,
            new CekHareketYaz(Guid.NewGuid(), cek.Surum, CekHareketTurleri.Tahsilat, Bugun, 1m, null, "MEZAT", null)));
        Assert.Equal("Bu kayıt kırdırıldı; şu an yalnız şu hareketler girilebilir: Dönüş.", hata.Message);

        Assert.Equal(cek.Id, Assert.Single(await o.Cek.CeklerAsync(CekYonleri.Alinan, CekSuzgecleri.Kapanan, "ahmet", Bugun, Bugun.AddDays(30))).Id);
        Assert.Empty(await o.Cek.CeklerAsync(durum: CekSuzgecleri.Portfoyde));
        var ozet = await o.Cek.CekOzetAsync();
        Assert.Equal((Bugun, 0), (ozet.Tarih, ozet.PortfoydekiAlinan.Adet));

        cek = await o.Cek.CekHareketGeriAlAsync(cek.Id, new CekSilYaz(Guid.NewGuid(), cek.Surum));
        Assert.Equal(CekDurumlari.Portfoyde, (await o.Cek.CekAsync(cek.Id)).Durum);
        var verilen = await o.Cek.CekKaydetAsync(null, new CekYaz(Guid.NewGuid(), 0, CekTurleri.Senet, CekYonleri.Verilen, "S-1", null, "Mehmet Ticaret", 7_000m, Bugun.AddDays(5),
            KanalEtiketleri.Ortak, false, null, null));
        Assert.Equal((KanalEtiketleri.Ortak, (int?)null), (verilen.Kanal, verilen.KanalId));
        await o.Cek.CekSilAsync(verilen.Id, new CekSilYaz(Guid.NewGuid(), verilen.Surum));
        Assert.Equal(cek.Id, Assert.Single(await o.Cek.CeklerAsync()).Id);
    }
}
```

`Kasa.Sozlesme.Tests/SozlesmeTemeli.cs` `Oturum` sınıfında `public IBenzerKayitApi Benzer { get; }` satırının altına
`public ICekApi Cek { get; }`, kurucuda `Benzer = Vekil<IBenzerKayitApi>();` satırının altına `Cek = Vekil<ICekApi>();` ekle.

- [ ] **Adım 3: Testlerin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekApiTests"`
Beklenen: derleme hatası (CS0246 `CekYaz`; CS1061 `CeklerAsync`).

- [ ] **Adım 4: İstemciyi yaz.** `Kasa.ApiClient/CekDtos.cs`:

```csharp
namespace Kasa.ApiClient;

/// <summary>Yeni ya da düzeltilen çek / senet. Kanal: verilende kasa adı ya da "Ortak", alınanda boş. Alanların kod değerleri
/// Kasa.Core.Kodlar sabitleridir (CekTurleri, CekYonleri, CekKonumlari).</summary>
public record CekYaz(Guid IstekId, int Surum, string Tur, string Yon, string No, string? Banka, string Kisi, decimal Tutar, DateOnly VadeTarihi,
    string? Kanal, bool Teminat, string? Konum, string? Not);
/// <summary>Çek hareketi (CekHareketTurleri). Kanal yalnız alınan çekin tahsilat, ciro ve kırdırmasında; NetTutar yalnız kırdırmada.</summary>
public record CekHareketYaz(Guid IstekId, int Surum, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar, string? Kanal, string? Karsi);
/// <summary>Çeki silme ya da son hareketi geri alma gövdesi.</summary>
public record CekSilYaz(Guid IstekId, int Surum);
public record CekHareketDto(int Id, int Sira, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar, int? KanalId, string? Kanal, string? Karsi);
/// <summary>Çek ve sunucunun hesapladığı durum (CekDurumlari), kalan, girilebilecek hareketler ve aynı çek uyarısı.</summary>
public record CekDto(int Id, int Surum, string Tur, string Yon, string No, string? Banka, string Kisi, decimal Tutar, DateOnly VadeTarihi,
    int? KanalId, string? Kanal, bool Teminat, string? Konum, string? Not, string Durum, decimal Kalan, IReadOnlyList<string> IzinliHareketler,
    IReadOnlyList<CekHareketDto> Hareketler, string? Uyari);
public record CekOzetKalemi(int Adet, decimal Toplam);
/// <summary>Çek panel özeti: teminatsız ve açık çeklerin kalan tutarları.</summary>
public record CekOzetDto(DateOnly Tarih, CekOzetKalemi PortfoydekiAlinan, CekOzetKalemi Alinan30, CekOzetKalemi Verilen30, CekOzetKalemi VadesiGecmis);

/// <summary>Çek ve senet uçları (/api/takip/cekler).</summary>
public interface ICekApi
{
    /// <summary>Liste; süzgeçler isteğe bağlıdır (yon: CekYonleri, durum: CekSuzgecleri, vade aralığı iki ucu dahil).</summary>
    Task<IReadOnlyList<CekDto>> CeklerAsync(string? yon = null, string? durum = null, string? ara = null, DateOnly? vadeBas = null, DateOnly? vadeSon = null);
    Task<CekDto> CekAsync(int id);
    Task<CekOzetDto> CekOzetAsync();
    /// <summary>id null ise ekler, değilse düzeltir.</summary>
    Task<CekDto> CekKaydetAsync(int? id, CekYaz g);
    Task CekSilAsync(int id, CekSilYaz g);
    Task<CekDto> CekHareketEkleAsync(int id, CekHareketYaz g);
    /// <summary>Son hareketi geri alır.</summary>
    Task<CekDto> CekHareketGeriAlAsync(int id, CekSilYaz g);
}
```

`Kasa.ApiClient/KasaApiClient.Cekler.cs`:

```csharp
using System.Globalization;

namespace Kasa.ApiClient;

public sealed partial class KasaApiClient : ICekApi
{
    public Task<IReadOnlyList<CekDto>> CeklerAsync(string? yon = null, string? durum = null, string? ara = null, DateOnly? vadeBas = null, DateOnly? vadeSon = null)
    {
        var sorgu = new List<string>();
        if (yon is not null)
            sorgu.Add("yon=" + Uri.EscapeDataString(yon));
        if (durum is not null)
            sorgu.Add("durum=" + Uri.EscapeDataString(durum));
        if (!string.IsNullOrWhiteSpace(ara))
            sorgu.Add("ara=" + Uri.EscapeDataString(ara.Trim()));
        if (vadeBas is { } bas)
            sorgu.Add("vadeBas=" + bas.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (vadeSon is { } son)
            sorgu.Add("vadeSon=" + son.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return GetAsync<IReadOnlyList<CekDto>>("api/takip/cekler" + (sorgu.Count == 0 ? "" : "?" + string.Join("&", sorgu)));
    }

    public Task<CekDto> CekAsync(int id) => GetAsync<CekDto>($"api/takip/cekler/{id}");
    public Task<CekOzetDto> CekOzetAsync() => GetAsync<CekOzetDto>("api/takip/cekler/ozet");
    public Task<CekDto> CekKaydetAsync(int? id, CekYaz g) =>
        GonderJsonAsync<CekDto>(id is null ? HttpMethod.Post : HttpMethod.Put, id is null ? "api/takip/cekler" : $"api/takip/cekler/{id}", g);
    public Task CekSilAsync(int id, CekSilYaz g) => GonderJsonAsync(HttpMethod.Delete, $"api/takip/cekler/{id}", g);
    public Task<CekDto> CekHareketEkleAsync(int id, CekHareketYaz g) => GonderJsonAsync<CekDto>(HttpMethod.Post, $"api/takip/cekler/{id}/hareketler", g);
    public Task<CekDto> CekHareketGeriAlAsync(int id, CekSilYaz g) => GonderJsonAsync<CekDto>(HttpMethod.Delete, $"api/takip/cekler/{id}/hareketler/son", g);
}
```

- [ ] **Adım 5: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:   182`.

Çalıştır: `dotnet test Kasa.Sozlesme.Tests/Kasa.Sozlesme.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:    56` (DtoEslesme, Kapsam, Mimari dahil).

- [ ] **Adım 6: Commit.**

```bash
git add Kasa.ApiClient/CekDtos.cs Kasa.ApiClient/KasaApiClient.Cekler.cs Kasa.ApiClient.Tests/CekApiTests.cs Kasa.Sozlesme.Tests/CekSozlesmeTests.cs Kasa.Sozlesme.Tests/SozlesmeTemeli.cs
git commit -F - <<'MESAJ'
feat(apiclient): çek uçları ve sözleşmesi

ICekApi: liste (süzgeçli), tek kayıt, özet, kaydet, sil, hareket, geri al. Sözleşme
testi gerçek sunucuya karşı bütün metotları ve reddedilen geçişin iletisini sınar.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 10: App.Core görünüm modelleri

**Dosyalar:**
- Oluştur: `Kasa.App.Core/CekTakipModelleri.cs` (`KodCipi`, `CekHazirSuzgec`, `CekMetni`, `CekSatiri`, `CekHareketSatiri`),
  `Kasa.App.Core/CekTakipViewModel.cs`, `Kasa.App.Core/CekOzetViewModel.cs`, `Kasa.App.Core.Tests/CekTakipViewModelTests.cs`

Menü, rol ve bildirim hedefi bu görevde değil: bildirim hedefi Görev 7'de, menü ve rol Görev 11'de (verilmiş karar 13).

- [ ] **Adım 1: Model testlerini yaz.** `Kasa.App.Core.Tests/CekTakipViewModelTests.cs`:

```csharp
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

/// <summary>Çekler ekranının modeli (docs/specs/2026-10-01-cekler.md "Masaüstü ekranı"): süzgeçler sunucuya gider, hazır süzgeçler
/// (üst şerit, panel), satırın altında açılan ayrıntı, hareket formu varsayılanları, geri alma, aynı çek uyarısı ve bildirimden
/// açılış. Gün 25 Eylül 2026.</summary>
public class CekTakipViewModelTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);

    internal sealed class Sahte : ICekApi
    {
        public List<CekDto> Liste = [];
        public CekOzetDto Ozet = new(Bugun, new(2, 60_000m), new(1, 50_000m), new(1, 7_000m), new(0, 0m));
        public List<(string? Yon, string? Durum, string? Ara, DateOnly? Bas, DateOnly? Son)> Sorgular = [];
        public List<(int? Id, CekYaz Govde)> Kayitlar = [];
        public List<(int Id, CekHareketYaz Govde)> Hareketler = [];
        public List<(int Id, CekSilYaz Govde)> GeriAlmalar = [];
        public List<(int Id, CekSilYaz Govde)> Silmeler = [];

        public Task<IReadOnlyList<CekDto>> CeklerAsync(string? yon = null, string? durum = null, string? ara = null, DateOnly? vadeBas = null, DateOnly? vadeSon = null)
        {
            Sorgular.Add((yon, durum, ara, vadeBas, vadeSon));
            return Task.FromResult<IReadOnlyList<CekDto>>(Liste.Where(c => yon is null || c.Yon == yon).ToList());
        }
        public Task<CekDto> CekAsync(int id) => Task.FromResult(Liste.Single(c => c.Id == id));
        public Task<CekOzetDto> CekOzetAsync() => Task.FromResult(Ozet);
        public Task<CekDto> CekKaydetAsync(int? id, CekYaz g)
        {
            Kayitlar.Add((id, g));
            return Task.FromResult(Cek(id ?? 99, g.Yon, g.Tutar, no: g.No) with { Surum = g.Surum + 1 });
        }
        public Task CekSilAsync(int id, CekSilYaz g)
        {
            Silmeler.Add((id, g));
            return Task.CompletedTask;
        }
        public Task<CekDto> CekHareketEkleAsync(int id, CekHareketYaz g)
        {
            Hareketler.Add((id, g));
            var eski = Liste.Single(c => c.Id == id);
            return Task.FromResult(eski with
            {
                Surum = eski.Surum + 1,
                Kalan = eski.Kalan - g.Tutar,
                Hareketler = [.. eski.Hareketler, new CekHareketDto(50, eski.Hareketler.Count + 1, g.Tur, g.Tarih, g.Tutar, g.NetTutar, 1, g.Kanal, g.Karsi)],
            });
        }
        public Task<CekDto> CekHareketGeriAlAsync(int id, CekSilYaz g)
        {
            GeriAlmalar.Add((id, g));
            return Task.FromResult(Liste.Single(c => c.Id == id) with { Hareketler = [], Surum = g.Surum + 1 });
        }
    }

    internal static CekDto Cek(int id, string yon = CekYonleri.Alinan, decimal tutar = 50_000m, decimal? kalan = null, DateOnly? vade = null, string no = "12345",
        IReadOnlyList<CekHareketDto>? hareketler = null, IReadOnlyList<string>? izinli = null) =>
        new(id, 1, CekTurleri.Cek, yon, no, "Ziraat", yon == CekYonleri.Alinan ? "Ahmet Yılmaz" : "Mehmet Ticaret", tutar, vade ?? Bugun.AddDays(5),
            null, yon == CekYonleri.Verilen ? KanalEtiketleri.Ortak : null, false,
            yon == CekYonleri.Alinan ? CekKonumlari.Elde : null, null, CekDurumlari.Portfoyde, kalan ?? tutar,
            izinli ?? (yon == CekYonleri.Alinan
                ? [CekHareketTurleri.Tahsilat, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade]
                : [CekHareketTurleri.Odeme, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade]),
            hareketler ?? [], null);

    private static AuthViewModel Auth(Rol rol = Rol.Editor) => new(new SahteApi()) { AktifRol = rol };

    private static async Task<(CekTakipViewModel Vm, Sahte Api)> Vm(Rol rol = Rol.Editor, params CekDto[] cekler)
    {
        var api = new Sahte { Liste = [.. cekler] };
        var finans = new SahteApi { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0), new KanalDto(3, "ESKI", false, 2, 0)] };
        var vm = new CekTakipViewModel(api, finans, Auth(rol), new IslemEditorTests.SabitZaman(Bugun));
        await vm.YukleAsync();
        return (vm, api);
    }

    [Fact]
    public async Task Varsayilan_alinan_portfoyde_yuklenir_satir_vade_rozeti_ve_kalan_gosterir()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1, kalan: 30_000m), Cek(2, vade: Bugun.AddDays(-3)), Cek(3, CekYonleri.Verilen));
        Assert.Equal((CekYonleri.Alinan, CekSuzgecleri.Portfoyde, (string?)null, (DateOnly?)null, (DateOnly?)null), api.Sorgular.Single());
        Assert.Equal(new[] { 1, 2 }, vm.Cekler.Select(s => s.Veri.Id));
        Assert.Equal("30.09.2026 · 5 gün kaldı · Ahmet Yılmaz", vm.Cekler[0].Baslik);
        Assert.Equal("Çek Ziraat · 12345 · 50.000,00 ₺ · Kalan: 30.000,00 ₺ · portföyde · Elde", vm.Cekler[0].Ozet);
        Assert.Equal("22.09.2026 · 3 gün geçti · Ahmet Yılmaz", vm.Cekler[1].Baslik);
        Assert.Equal(new[] { "MEZAT", "PERAKENDE" }, vm.KasaSecenekleri);
        Assert.Equal(new[] { KanalEtiketleri.Ortak, "MEZAT", "PERAKENDE" }, vm.CekKasaSecenekleri);
        Assert.Equal("Portföydeki alınan: 60.000,00 ₺ (2 çek)", vm.PortfoyMetni);
        Assert.Equal("30 gün içinde ödenecek: 7.000,00 ₺ (1 çek)", vm.Verilen30Metni);
        Assert.True(vm.YonCipleri[0].Secili);
        Assert.True(vm.DurumCipleri[0].Secili);
    }

    [Fact]
    public async Task Yon_durum_arama_ve_hazir_suzgecler_sunucuya_gider()
    {
        var (vm, api) = await Vm();
        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[1]);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Portfoyde), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum));
        Assert.True(vm.YonCipleri[1].Secili);
        Assert.False(vm.YonCipleri[0].Secili);
        await vm.SecDurumCommand.ExecuteAsync(vm.DurumCipleri[2]);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Kapanan), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum));
        vm.Ara = " ahmet ";
        await vm.AraCommand.ExecuteAsync(null);
        Assert.Equal("ahmet", api.Sorgular[^1].Ara);

        await vm.HazirSuzgecAsync(CekHazirSuzgec.Alinan30);
        Assert.Equal((CekYonleri.Alinan, CekSuzgecleri.Portfoyde, (string?)null, (DateOnly?)Bugun, (DateOnly?)Bugun.AddDays(30)), api.Sorgular[^1]);
        Assert.Equal("Vade 25.09.2026 – 25.10.2026", vm.VadeSuzgeci);
        await vm.HazirSuzgecAsync(CekHazirSuzgec.Verilen30);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Portfoyde, (string?)null, (DateOnly?)Bugun, (DateOnly?)Bugun.AddDays(30)), api.Sorgular[^1]);
        await vm.HazirSuzgecAsync(CekHazirSuzgec.VadesiGecmis);
        Assert.Equal((CekYonleri.Alinan, CekSuzgecleri.Portfoyde, (string?)null, (DateOnly?)null, (DateOnly?)Bugun.AddDays(-1)), api.Sorgular[^1]);
        Assert.Equal("Vade 24.09.2026 ve öncesi", vm.VadeSuzgeci);
        await vm.VadeSuzgeciniKaldirCommand.ExecuteAsync(null);
        Assert.False(vm.VadeSuzgeciVar);
        Assert.Null(api.Sorgular[^1].Son);
    }

    [Fact]
    public async Task Satira_tiklaninca_ayrinti_satirin_altinda_acilir_ikinci_tiklama_kapatir()
    {
        var (vm, _) = await Vm(Rol.Editor, Cek(1), Cek(2), Cek(3));
        vm.SecCommand.Execute(vm.Cekler[1]);
        Assert.Equal(2, vm.Acik!.Id);
        Assert.Equal(new[] { 1, 2 }, vm.OncekiSatirlar.Select(s => s.Veri.Id));
        Assert.Equal(new[] { 3 }, vm.SonrakiSatirlar.Select(s => s.Veri.Id));
        Assert.Equal(new[] { "Tahsilat", "Ciro", "Kırdırma", "Karşılıksız", "İade" }, vm.HareketCipleri.Select(c => c.Ad));
        vm.SecCommand.Execute(vm.Cekler[1]);
        Assert.Null(vm.Acik);
        Assert.Equal(new[] { 1, 2, 3 }, vm.OncekiSatirlar.Select(s => s.Veri.Id));
        Assert.Empty(vm.SonrakiSatirlar);
    }

    [Fact]
    public async Task Hareket_formu_bugun_kalan_ve_son_secilen_kasayla_acilir_kirdirmada_masraf_hesaplanir()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1, kalan: 30_000m), Cek(2));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri[0]);
        Assert.Equal((Bugun.ToDateTime(TimeOnly.MinValue), 30_000m, "MEZAT", true, false), (vm.HareketTarihi, vm.HareketTutari, vm.HareketKasasi, vm.KasaGerekli, vm.KarsiGerekli));
        vm.HareketKasasi = "PERAKENDE";
        vm.HareketTutari = 10_000m;
        await vm.HareketKaydetCommand.ExecuteAsync(null);
        var (id, g) = Assert.Single(api.Hareketler);
        Assert.Equal((1, CekHareketTurleri.Tahsilat, 10_000m, "PERAKENDE", (string?)null, (decimal?)null, 1), (id, g.Tur, g.Tutar, g.Kanal, g.Karsi, g.NetTutar, g.Surum));
        Assert.NotEqual(Guid.Empty, g.IstekId);
        Assert.Equal("Tahsilat kaydedildi.", vm.Mesaj);
        Assert.Equal(20_000m, vm.Acik!.Kalan);

        vm.SecCommand.Execute(vm.Cekler[1]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single(c => c.Kod == CekHareketTurleri.Kirdirma));
        Assert.Equal("PERAKENDE", vm.HareketKasasi);
        Assert.True(vm.NetGerekli);
        vm.NetTutar = 48_750m;
        Assert.Equal("Masraf: 1.250,00 ₺ (karşı tarafa Cari gider). Kasaya 48.750,00 ₺ girer.", vm.MasrafMetni);
    }

    [Fact]
    public async Task Verilen_cek_odemesinde_kasa_gonderilmez_donus_tutari_cirodan_gelir()
    {
        var ciro = new CekHareketDto(7, 1, CekHareketTurleri.Ciro, Bugun.AddDays(-2), 50_000m, null, 1, "MEZAT", "Veli");
        var (vm, api) = await Vm(Rol.Editor, Cek(1, CekYonleri.Verilen, 30_000m), Cek(2, kalan: 0m, hareketler: [ciro], izinli: [CekHareketTurleri.Donus]));
        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[1]);
        vm.SecCommand.Execute(vm.Cekler.Single(s => s.Veri.Id == 1));
        vm.SecHareketCommand.Execute(vm.HareketCipleri[0]);
        Assert.False(vm.KasaGerekli);
        await vm.HareketKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.Hareketler.Single().Govde.Kanal);

        await vm.SecYonCommand.ExecuteAsync(vm.YonCipleri[0]);
        vm.SecCommand.Execute(vm.Cekler.Single(s => s.Veri.Id == 2));
        vm.SecHareketCommand.Execute(vm.HareketCipleri.Single());
        Assert.Equal((CekHareketTurleri.Donus, 50_000m, false), (vm.HareketTuru, vm.HareketTutari, vm.KasaGerekli));
        Assert.True(vm.GeriAlinabilir);
        await vm.GeriAlAsync();
        Assert.Equal((2, 1), (api.GeriAlmalar.Single().Id, api.GeriAlmalar.Single().Govde.Surum));
        Assert.Equal("Son hareket geri alındı.", vm.Mesaj);
    }

    [Fact]
    public async Task Ayni_cek_uyarisi_kaydetmeden_once_gosterilir_yine_de_kaydet_ayni_istek_kimligiyle_kaydeder()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        vm.YeniCekCommand.Execute(null);
        Assert.Equal((CekYonleri.Alinan, CekTurleri.Cek, CekKonumlari.Elde, "Yeni çek / senet"), (vm.FormYon!.Kod, vm.FormTur!.Kod, vm.Konum!.Kod, vm.FormBasligi));
        vm.No = "12345";
        vm.Banka = " ziraat ";
        vm.Kisi = "Ayşe";
        vm.Tutar = 10_000m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.Kayitlar);
        Assert.Equal((CekYonleri.Alinan, CekSuzgecleri.Hepsi, "12345"), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum, api.Sorgular[^1].Ara));
        Assert.StartsWith("Aynı yön, banka ve numarayla kayıtlı çek var: Ahmet Yılmaz · 50.000,00 ₺ · vade 30.09.2026.", vm.AyniCekUyarisi);
        await vm.YineDeKaydetCommand.ExecuteAsync(null);
        var (id, g) = Assert.Single(api.Kayitlar);
        Assert.Equal(((int?)null, "ziraat", "Ayşe", (string?)null, CekKonumlari.Elde), (id, g.Banka, g.Kisi, g.Kanal, g.Konum));
        Assert.False(vm.FormAcik);
        Assert.Null(vm.AyniCekUyarisi);
        Assert.Equal("Çek kaydedildi.", vm.Mesaj);
    }

    [Fact]
    public async Task Verilen_yeni_cek_kasasiyla_ve_konumsuz_gider()
    {
        var (vm, api) = await Vm(Rol.Editor, Cek(1));
        vm.YeniCekCommand.Execute(null);
        vm.FormYon = vm.YonSecenekleri[1];
        vm.No = "999";
        vm.Kisi = "Mehmet";
        vm.Tutar = 5_000m;
        vm.CekKasasi = KanalEtiketleri.Ortak;
        await vm.KaydetCommand.ExecuteAsync(null);
        var g = Assert.Single(api.Kayitlar).Govde;
        Assert.Equal((CekYonleri.Verilen, KanalEtiketleri.Ortak, (string?)null, (string?)null), (g.Yon, g.Kanal, g.Konum, g.Banka));
    }

    [Fact]
    public async Task Bildirimden_gelen_cek_yonune_ve_hepsi_suzgecine_gecip_acilir_alici_gecemez()
    {
        var verilen = Cek(5, CekYonleri.Verilen);
        var (vm, api) = await Vm(Rol.Editor, Cek(1), verilen);
        await vm.CekIcinYukleAsync(5);
        Assert.Equal((CekYonleri.Verilen, CekSuzgecleri.Hepsi), (api.Sorgular[^1].Yon, api.Sorgular[^1].Durum));
        Assert.True(vm.DurumCipleri.Single(c => c.Kod == CekSuzgecleri.Hepsi).Secili);
        Assert.True(vm.IdIleSec(5));
        Assert.Equal(5, vm.Acik!.Id);
        Assert.False(vm.IdIleSec(42));
        Assert.Equal("Çek bulunamadı. Listeyi yenileyip tekrar deneyin.", vm.Hata);
        var (alici, _) = await Vm(Rol.Alici, Cek(1));
        Assert.False(alici.IdIleSec(1));
    }

    [Fact]
    public async Task Izleyici_hareket_ve_kayit_gonderemez()
    {
        var (vm, api) = await Vm(Rol.Izleyici, Cek(1));
        vm.SecCommand.Execute(vm.Cekler[0]);
        vm.SecHareketCommand.Execute(vm.HareketCipleri[0]);
        await vm.HareketKaydetCommand.ExecuteAsync(null);
        vm.YeniCekCommand.Execute(null);
        vm.No = "1";
        vm.Kisi = "X";
        vm.Tutar = 1m;
        await vm.KaydetCommand.ExecuteAsync(null);
        await vm.SilAsync();
        Assert.Empty(api.Hareketler);
        Assert.Empty(api.Kayitlar);
        Assert.Empty(api.Silmeler);
    }

    [Fact]
    public async Task Panel_ozeti_uc_satiri_bicimler()
    {
        var api = new Sahte();
        var vm = new CekOzetViewModel(api, Auth());
        await vm.YukleAsync();
        Assert.Equal(("30 gün içinde tahsil edilecek: 50.000,00 ₺ (1 çek)", "30 gün içinde ödenecek: 7.000,00 ₺ (1 çek)", "Vadesi geçmiş, tahsil edilmemiş: 0,00 ₺ (0 çek)"),
            (vm.Alinan30Metni, vm.Verilen30Metni, vm.GecmisMetni));
        Assert.True(vm.VeriHazir);
    }
}
```

- [ ] **Adım 2: Testlerin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~CekTakipViewModelTests"`
Beklenen: derleme hatası (CS0246 `CekTakipViewModel`, `CekOzetViewModel`, `CekHazirSuzgec`).

- [ ] **Adım 3: Satır modellerini ve metinleri yaz.** `Kasa.App.Core/CekTakipModelleri.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using CekKurallari = Kasa.Core.CekKurallari;

namespace Kasa.App.Core;

/// <summary>Kodlu seçim çipi (CipGrubu: "Ad" ve "Secili"): yön, durum süzgeci ve hareket türü çipleri. <see cref="Kod"/> sunucuya
/// giden değerdir (Kasa.Core.Kodlar), <see cref="Ad"/> görünen metin.</summary>
public partial class KodCipi(string kod, string ad) : ObservableObject
{
    public string Kod { get; } = kod;
    public string Ad { get; } = ad;
    [ObservableProperty] private bool _secili;
}

/// <summary>Çekler ekranının üst şeridindeki ve panel kutusundaki hazır süzgeçler (tasarım "Masaüstü ekranı", "Panel"). Panel
/// kutusu sayfayı //cekler?Suzgec={ad} ile açar.</summary>
public enum CekHazirSuzgec { Portfoy, Alinan30, Verilen30, VadesiGecmis }

/// <summary>Çek metinleri: durum, konum, vade rozeti ve tutar biçimi (görünen ad kuralları Kasa.Core.CekKurallari'ndadır).</summary>
public static class CekMetni
{
    public static string Tur(string tur) => tur switch { CekTurleri.Senet => "Senet", _ => "Çek" };
    public static string Yon(string yon) => yon switch { CekYonleri.Verilen => "Verilen", _ => "Alınan" };
    public static string Durum(string durum) => CekKurallari.DurumAdi(durum);

    /// <summary>Durum süzgeci çipinin adı.</summary>
    public static string Suzgec(string suzgec) => suzgec switch
    {
        CekSuzgecleri.Portfoyde => "Portföyde",
        CekSuzgecleri.Karsiliksiz => "Karşılıksız",
        CekSuzgecleri.Kapanan => "Kapananlar",
        _ => "Hepsi",
    };
    public static string Hareket(string tur) => CekKurallari.HareketAdi(tur);

    public static string Konum(string? konum) => konum switch
    {
        CekKonumlari.Elde => "Elde",
        CekKonumlari.BankadaTahsilde => "Bankada tahsilde",
        CekKonumlari.Teminatta => "Teminatta",
        CekKonumlari.Icrada => "İcrada",
        _ => "",
    };

    /// <summary>Özet satırı (üst şerit ve panel kutusu): "Ad: 10.000,00 ₺ (2 çek)".</summary>
    public static string OzetSatiri(string ad, CekOzetKalemi kalem) => $"{ad}: {Bicim.Tl(kalem.Toplam)} ₺ ({kalem.Adet} çek)";

    /// <summary>Vade rozeti: "5 gün kaldı", "Bugün", "3 gün geçti".</summary>
    public static string Rozet(DateOnly vade, DateOnly bugun) => (vade.DayNumber - bugun.DayNumber) switch
    {
        0 => "Bugün",
        > 0 and var kalan => $"{kalan} gün kaldı",
        var gecen => $"{-gecen} gün geçti",
    };
}

/// <summary>Çek listesinin satırı (TakipUi.Liste: Baslik ve Ozet). Başlık vade, rozet ve kişi; özet banka · no, tutar, kalan, durum,
/// konum ve teminat işareti.</summary>
public sealed class CekSatiri(CekDto veri, DateOnly bugun)
{
    public CekDto Veri { get; } = veri;
    public string Baslik => $"{Veri.VadeTarihi:dd.MM.yyyy} · {CekMetni.Rozet(Veri.VadeTarihi, bugun)} · {Veri.Kisi}";

    public string Ozet
    {
        get
        {
            var parcalar = new List<string> { $"{CekMetni.Tur(Veri.Tur)} {Veri.Banka ?? "—"} · {Veri.No}", $"{Bicim.Tl(Veri.Tutar)} ₺" };
            if (Veri.Kalan > 0 && Veri.Kalan != Veri.Tutar)
                parcalar.Add($"Kalan: {Bicim.Tl(Veri.Kalan)} ₺");
            parcalar.Add(CekMetni.Durum(Veri.Durum));
            if (CekMetni.Konum(Veri.Konum) is { Length: > 0 } konum && Veri.Durum is CekDurumlari.Portfoyde or CekDurumlari.KismenTahsilEdildi)
                parcalar.Add(konum);
            if (Veri.Teminat)
                parcalar.Add("Teminat");
            return string.Join(" · ", parcalar);
        }
    }
}

/// <summary>Açık çekin hareket satırı.</summary>
public sealed class CekHareketSatiri(CekHareketDto veri)
{
    public CekHareketDto Veri { get; } = veri;
    public string Baslik => $"{Veri.Sira}. {CekMetni.Hareket(Veri.Tur)} · {Veri.Tarih:dd.MM.yyyy}";

    public string Ozet => string.Join(" · ", new[]
    {
        Veri.Tutar != 0 ? $"{Bicim.Tl(Veri.Tutar)} ₺" : null,
        Veri.NetTutar is { } net ? $"hesaba geçen {Bicim.Tl(net)} ₺, masraf {Bicim.Tl(Veri.Tutar - net)} ₺" : null,
        Veri.Kanal,
        Veri.Karsi,
    }.OfType<string>());
}
```

- [ ] **Adım 4: Ekran modelini yaz.** `Kasa.App.Core/CekTakipViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using CekKurallari = Kasa.Core.CekKurallari;

namespace Kasa.App.Core;

/// <summary>
/// Çekler ekranı (docs/specs/2026-10-01-cekler.md "Masaüstü ekranı"). Süzgeçler (yön, durum, arama, vade aralığı) sunucuda uygulanır:
/// her değişiklik listeyi yeniden yükler. Açılan çekin ayrıntısı satırının hemen altında görünür (<see cref="OncekiSatirlar"/>, açık
/// çek, <see cref="SonrakiSatirlar"/>). Hareket düğmeleri sunucunun izin verdiği türlerdir (<see cref="CekDto.IzinliHareketler"/>);
/// geçiş kuralı istemcide yinelenmez. Hareket formu tarih bugün, tutar kalan ve son seçilen kasayla açılır. Yeni çekte aynı yön,
/// banka ve numaralı kayıt kaydetmeden önce uyarılır; kullanıcı onaylarsa kaydedilir.
/// </summary>
/// <param name="zaman">Vade rozeti ve hazır süzgeçlerin günü (yerel); verilmezse sistem saati.</param>
public partial class CekTakipViewModel(ICekApi api, IKasaApi finans, AuthViewModel auth, TimeProvider? zaman = null) : OturumluViewModel(auth)
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
    private readonly TekrarAnahtari _kayit = new(), _hareket = new(), _geriAl = new(), _sil = new();
    private string? _sonKanal;
    private bool _ayniCekOnaylandi;

    public ObservableCollection<CekSatiri> Cekler { get; } = new();
    public ObservableCollection<CekSatiri> OncekiSatirlar { get; } = new();
    public ObservableCollection<CekSatiri> SonrakiSatirlar { get; } = new();
    public ObservableCollection<CekHareketSatiri> Hareketler { get; } = new();
    public ObservableCollection<KodCipi> YonCipleri { get; } = Cipler([CekYonleri.Alinan, CekYonleri.Verilen], CekMetni.Yon);
    public ObservableCollection<KodCipi> DurumCipleri { get; } =
        Cipler([CekSuzgecleri.Portfoyde, CekSuzgecleri.Karsiliksiz, CekSuzgecleri.Kapanan, CekSuzgecleri.Hepsi], CekMetni.Suzgec);
    public ObservableCollection<KodCipi> HareketCipleri { get; } = new();
    public ObservableCollection<string> KasaSecenekleri { get; } = new();
    public ObservableCollection<string> CekKasaSecenekleri { get; } = new();
    public IReadOnlyList<KodCipi> KonumSecenekleri { get; } =
        Cipler([CekKonumlari.Elde, CekKonumlari.BankadaTahsilde, CekKonumlari.Teminatta, CekKonumlari.Icrada], k => CekMetni.Konum(k));
    public IReadOnlyList<KodCipi> TurSecenekleri { get; } = Cipler([CekTurleri.Cek, CekTurleri.Senet], CekMetni.Tur);
    public IReadOnlyList<KodCipi> YonSecenekleri { get; } = Cipler([CekYonleri.Alinan, CekYonleri.Verilen], CekMetni.Yon);

    /// <summary>Kodlardan çipler; ilki seçili.</summary>
    private static ObservableCollection<KodCipi> Cipler(string[] kodlar, Func<string, string> ad) =>
        new(kodlar.Select((k, i) => new KodCipi(k, ad(k)) { Secili = i == 0 }));

    [ObservableProperty] private string _yon = CekYonleri.Alinan;
    [ObservableProperty] private string _durum = CekSuzgecleri.Portfoyde;
    [ObservableProperty] private string _ara = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(VadeSuzgeci), nameof(VadeSuzgeciVar))] private DateOnly? _vadeBas;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(VadeSuzgeci), nameof(VadeSuzgeciVar))] private DateOnly? _vadeSon;
    [ObservableProperty] private CekOzetDto? _ozet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CekAcik), nameof(AcikOzet), nameof(GeriAlinabilir), nameof(KasaGerekli), nameof(HareketFormuAcik))]
    private CekDto? _acik;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HareketFormuAcik), nameof(KasaGerekli), nameof(KarsiGerekli), nameof(NetGerekli), nameof(MasrafMetni))]
    private string? _hareketTuru;
    [ObservableProperty] private DateTime _hareketTarihi;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(MasrafMetni))] private decimal _hareketTutari;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(MasrafMetni))] private decimal _netTutar;
    [ObservableProperty] private string? _hareketKasasi;
    [ObservableProperty] private string _karsi = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi))] private bool _formAcik;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi))] private int? _duzenlenen;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormVerilen))] private KodCipi? _formYon;
    [ObservableProperty] private KodCipi? _formTur;
    [ObservableProperty] private string _no = "";
    [ObservableProperty] private string _banka = "";
    [ObservableProperty] private string _kisi = "";
    [ObservableProperty] private decimal _tutar;
    [ObservableProperty] private DateTime _vade;
    [ObservableProperty] private string? _cekKasasi;
    [ObservableProperty] private bool _teminat;
    [ObservableProperty] private KodCipi? _konum;
    [ObservableProperty] private string _not = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(AyniCekVar))] private string? _ayniCekUyarisi;

    private DateOnly Bugun => DateOnly.FromDateTime(_zaman.GetLocalNow().DateTime);
    public bool CekAcik => Acik is not null;
    public bool HareketFormuAcik => Acik is not null && HareketTuru is not null;
    public bool GeriAlinabilir => Acik is { Hareketler.Count: > 0 };
    public bool KasaGerekli => Acik?.Yon == CekYonleri.Alinan && HareketTuru is CekHareketTurleri.Tahsilat or CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma;
    public bool KarsiGerekli => HareketTuru is CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma;
    public bool NetGerekli => HareketTuru == CekHareketTurleri.Kirdirma;
    public bool FormVerilen => FormYon?.Kod == CekYonleri.Verilen;
    public bool AyniCekVar => AyniCekUyarisi is not null;
    public bool VadeSuzgeciVar => VadeBas is not null || VadeSon is not null;
    public string FormBasligi => Duzenlenen is null ? "Yeni çek / senet" : "Çeki düzelt";
    public string VadeSuzgeci => (VadeBas, VadeSon) switch
    {
        ({ } bas, { } son) => $"Vade {bas:dd.MM.yyyy} – {son:dd.MM.yyyy}",
        (null, { } son) => $"Vade {son:dd.MM.yyyy} ve öncesi",
        ({ } bas, null) => $"Vade {bas:dd.MM.yyyy} ve sonrası",
        _ => "",
    };
    public string MasrafMetni => NetGerekli && ParaAyristirici.HepsiGecerli(HareketTutari, NetTutar)
        ? $"Masraf: {Bicim.Tl(HareketTutari - NetTutar)} ₺ (karşı tarafa Cari gider). Kasaya {Bicim.Tl(NetTutar)} ₺ girer." : "";
    public string AcikOzet => Acik is not { } c ? "" : string.Join("\n", new[]
    {
        $"{CekMetni.Yon(c.Yon)} {CekMetni.Tur(c.Tur).ToLowerInvariant()} · {c.Kisi} · {c.Banka ?? "—"} · {c.No}",
        $"Tutar {Bicim.Tl(c.Tutar)} ₺ · kalan {Bicim.Tl(c.Kalan)} ₺ · vade {c.VadeTarihi:dd.MM.yyyy} · {CekMetni.Durum(c.Durum)}",
        c.Kanal is { } kasa ? $"Kasa: {kasa}" : null,
        c.Konum is not null ? $"Konum: {CekMetni.Konum(c.Konum)}" : null,
        c.Teminat ? "Teminat çeki: bildirim ve panel toplamlarına girmez." : null,
        c.Not,
        c.Uyari,
    }.OfType<string>());
    public string PortfoyMetni => Ozet is { } o ? CekMetni.OzetSatiri("Portföydeki alınan", o.PortfoydekiAlinan) : "";
    public string Alinan30Metni => Ozet is { } o ? CekMetni.OzetSatiri("30 gün içinde tahsil edilecek", o.Alinan30) : "";
    public string Verilen30Metni => Ozet is { } o ? CekMetni.OzetSatiri("30 gün içinde ödenecek", o.Verilen30) : "";
    public string GecmisMetni => Ozet is { } o ? CekMetni.OzetSatiri("Vadesi geçmiş, tahsil edilmemiş", o.VadesiGecmis) : "";

    partial void OnOzetChanged(CekOzetDto? value)
    {
        foreach (var p in new[] { nameof(PortfoyMetni), nameof(Alinan30Metni), nameof(Verilen30Metni), nameof(GecmisMetni) })
            OnPropertyChanged(p);
    }

    partial void OnAcikChanged(CekDto? value)
    {
        TakipMetni.Doldur(Hareketler, value?.Hareketler.Select(h => new CekHareketSatiri(h)) ?? []);
        TakipMetni.Doldur(HareketCipleri, value?.IzinliHareketler.Select(t => new KodCipi(t, CekMetni.Hareket(t))) ?? []);
        HareketTuru = null;
        SatirlariBol();
    }

    partial void OnNoChanged(string value) => AyniCekSifirla();
    partial void OnBankaChanged(string value) => AyniCekSifirla();
    partial void OnFormYonChanged(KodCipi? value) => AyniCekSifirla();

    private void AyniCekSifirla()
    {
        _ayniCekOnaylandi = false;
        AyniCekUyarisi = null;
    }

    /// <summary>Liste, özet ve kasa seçenekleri.</summary>
    public Task YukleAsync() => YurutAsync(async n =>
    {
        var kanallar = await finans.KanallarAsync();
        var cekler = await api.CeklerAsync(Yon, Durum, string.IsNullOrWhiteSpace(Ara) ? null : Ara.Trim(), VadeBas, VadeSon);
        var ozet = await api.CekOzetAsync();
        if (!Gecerli(n))
            return;
        Yansit(kanallar, cekler, ozet);
    });

    /// <summary>Bildirimden gelen çek (//cekler?CekId=…): çek okunur, yönüne ve "Hepsi" durumuna geçilir (süzgeç onu gizlemesin),
    /// liste yüklenir. Seçim <see cref="IdIleSec"/> ile yapılır.</summary>
    public Task CekIcinYukleAsync(int id) => YurutAsync(async n =>
    {
        var cek = await api.CekAsync(id);
        if (!Gecerli(n))
            return;
        SuzgecleriYaz(cek.Yon, CekSuzgecleri.Hepsi, null, null);
        Ara = "";
        var kanallar = await finans.KanallarAsync();
        var cekler = await api.CeklerAsync(Yon, Durum, null, null, null);
        var ozet = await api.CekOzetAsync();
        if (!Gecerli(n))
            return;
        Yansit(kanallar, cekler, ozet);
    });

    private void Yansit(IReadOnlyList<KanalDto> kanallar, IReadOnlyList<CekDto> cekler, CekOzetDto ozet)
    {
        TakipMetni.Doldur(KasaSecenekleri, kanallar.Where(k => k.Aktif).Select(k => k.Ad));
        TakipMetni.Doldur(CekKasaSecenekleri, kanallar.Where(k => k.Aktif).Select(k => k.Ad).Prepend(KanalEtiketleri.Ortak));
        TakipMetni.Doldur(Cekler, cekler.Select(c => new CekSatiri(c, Bugun)));
        Ozet = ozet;
        Acik = Acik is { } eski ? cekler.FirstOrDefault(c => c.Id == eski.Id) : null;
        SatirlariBol();
        Tamamlandi();
    }

    private void SatirlariBol()
    {
        var i = Acik is null ? -1 : Cekler.ToList().FindIndex(s => s.Veri.Id == Acik.Id);
        TakipMetni.Doldur(OncekiSatirlar, i < 0 ? Cekler : Cekler.Take(i + 1));
        TakipMetni.Doldur(SonrakiSatirlar, i < 0 ? [] : Cekler.Skip(i + 1));
    }

    private void SuzgecleriYaz(string yon, string durum, DateOnly? bas, DateOnly? son)
    {
        Yon = yon;
        Durum = durum;
        VadeBas = bas;
        VadeSon = son;
        foreach (var c in YonCipleri)
            c.Secili = c.Kod == yon;
        foreach (var c in DurumCipleri)
            c.Secili = c.Kod == durum;
    }

    /// <summary>Kimliği verilen çeki açar (bildirim tıklaması); listede yoksa sayfa hatası yazılır. Alıcı rolü çeke geçemez.</summary>
    public bool IdIleSec(int id)
    {
        if (Auth.AktifRol == Rol.Alici)
            return false;
        var satir = Cekler.FirstOrDefault(s => s.Veri.Id == id);
        if (satir is null)
        {
            Hata = "Çek bulunamadı. Listeyi yenileyip tekrar deneyin.";
            return false;
        }
        Sec(satir);
        return true;
    }

    /// <summary>Üst şerit ya da panel kutusu: süzgeci yazar ve listeyi yükler.</summary>
    public Task HazirSuzgecAsync(CekHazirSuzgec suzgec)
    {
        var bugun = Bugun;
        switch (suzgec)
        {
            case CekHazirSuzgec.Portfoy:
                SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, null);
                break;
            case CekHazirSuzgec.Alinan30:
                SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, bugun, bugun.AddDays(30));
                break;
            case CekHazirSuzgec.Verilen30:
                SuzgecleriYaz(CekYonleri.Verilen, CekSuzgecleri.Portfoyde, bugun, bugun.AddDays(30));
                break;
            default:
                SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, bugun.AddDays(-1));
                break;
        }
        Ara = "";
        return YukleAsync();
    }

    [RelayCommand]
    private Task SecYonAsync(KodCipi cip)
    {
        SuzgecleriYaz(cip.Kod, Durum, VadeBas, VadeSon);
        return YukleAsync();
    }

    [RelayCommand]
    private Task SecDurumAsync(KodCipi cip)
    {
        SuzgecleriYaz(Yon, cip.Kod, VadeBas, VadeSon);
        return YukleAsync();
    }

    [RelayCommand] private Task AraAsync() => YukleAsync();

    [RelayCommand]
    private Task VadeSuzgeciniKaldirAsync()
    {
        SuzgecleriYaz(Yon, Durum, null, null);
        return YukleAsync();
    }

    /// <summary>Satıra tıklama: kapalıysa açar, açıksa kapatır.</summary>
    [RelayCommand]
    private void Sec(CekSatiri satir) => Acik = Acik?.Id == satir.Veri.Id ? null : satir.Veri;

    /// <summary>Hareket türü düğmesi: formu tarih bugün, tutar kalan (dönüşte ciro/kırdırma tutarı; karşılıksız ve iadede 0) ve son
    /// seçilen kasayla açar.</summary>
    [RelayCommand]
    private void SecHareket(KodCipi cip)
    {
        if (Acik is not { } c)
            return;
        HareketTuru = cip.Kod;
        foreach (var h in HareketCipleri)
            h.Secili = h.Kod == cip.Kod;
        HareketTarihi = Bugun.ToDateTime(TimeOnly.MinValue);
        HareketTutari = cip.Kod switch
        {
            CekHareketTurleri.Karsiliksiz or CekHareketTurleri.Iade => 0m,
            CekHareketTurleri.Donus => c.Hareketler.LastOrDefault(h => h.Tur is CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma)?.Tutar ?? c.Tutar,
            _ => c.Kalan,
        };
        NetTutar = cip.Kod == CekHareketTurleri.Kirdirma ? c.Kalan : 0m;
        HareketKasasi = _sonKanal is { } kanal && KasaSecenekleri.Contains(kanal) ? kanal : KasaSecenekleri.FirstOrDefault();
        Karsi = "";
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
        g = g with { IstekId = _hareket.Al(new { c.Id, g }) };
        var sonuc = await api.CekHareketEkleAsync(c.Id, g);
        if (!Gecerli(n))
            return;
        _hareket.Temizle();
        if (KasaGerekli)
            _sonKanal = HareketKasasi;
        Guncelle(sonuc);
        Mesaj = $"{CekMetni.Hareket(tur)} kaydedildi.";
    });

    /// <summary>Son hareketi geri alır (sayfa önce onay ister).</summary>
    public Task GeriAlAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Acik is not { Hareketler.Count: > 0 } c)
            return;
        var g = new CekSilYaz(Guid.Empty, c.Surum);
        g = g with { IstekId = _geriAl.Al(new { c.Id, g }) };
        var sonuc = await api.CekHareketGeriAlAsync(c.Id, g);
        if (!Gecerli(n))
            return;
        _geriAl.Temizle();
        Guncelle(sonuc);
        Mesaj = "Son hareket geri alındı.";
    }, mesgulkenBildir: true);

    private void Guncelle(CekDto sonuc)
    {
        var i = Cekler.ToList().FindIndex(s => s.Veri.Id == sonuc.Id);
        if (i >= 0)
            Cekler[i] = new CekSatiri(sonuc, Bugun);
        else
            Cekler.Insert(0, new CekSatiri(sonuc, Bugun));
        Acik = sonuc;
    }

    [RelayCommand]
    private void YeniCek()
    {
        Duzenlenen = null;
        FormYon = YonSecenekleri.First(y => y.Kod == Yon);
        FormTur = TurSecenekleri[0];
        No = Banka = Kisi = Not = "";
        Tutar = 0;
        Vade = Bugun.ToDateTime(TimeOnly.MinValue);
        CekKasasi = null;
        Teminat = false;
        Konum = KonumSecenekleri[0];
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
        FormYon = YonSecenekleri.First(y => y.Kod == c.Yon);
        FormTur = TurSecenekleri.First(t => t.Kod == c.Tur);
        No = c.No;
        Banka = c.Banka ?? "";
        Kisi = c.Kisi;
        Not = c.Not ?? "";
        Tutar = c.Tutar;
        Vade = c.VadeTarihi.ToDateTime(TimeOnly.MinValue);
        CekKasasi = c.Kanal;
        Teminat = c.Teminat;
        Konum = KonumSecenekleri.FirstOrDefault(k => k.Kod == c.Konum) ?? KonumSecenekleri[0];
        _kayit.Temizle();
        AyniCekSifirla();
        FormAcik = true;
    }

    [RelayCommand] private void FormuKapat() => FormAcik = false;

    [RelayCommand]
    private Task YineDeKaydetAsync()
    {
        _ayniCekOnaylandi = true;
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
        var surum = Duzenlenen is { } id ? Cekler.FirstOrDefault(s => s.Veri.Id == id)?.Veri.Surum ?? Acik?.Surum ?? 0 : 0;
        var g = new CekYaz(Guid.Empty, surum, tur.Kod, yon.Kod, No.Trim(), string.IsNullOrWhiteSpace(Banka) ? null : Banka.Trim(), Kisi.Trim(), Tutar,
            DateOnly.FromDateTime(Vade), yon.Kod == CekYonleri.Verilen ? CekKasasi : null, Teminat, yon.Kod == CekYonleri.Alinan ? Konum?.Kod : null,
            string.IsNullOrWhiteSpace(Not) ? null : Not.Trim());
        if (Duzenlenen is null && !_ayniCekOnaylandi)
        {
            var ayni = (await api.CeklerAsync(yon.Kod, CekSuzgecleri.Hepsi, g.No, null, null))
                .Where(c => CekKurallari.AyniCek(c.Yon, c.Banka, c.No, g.Yon, g.Banka, g.No)).ToList();
            if (!Gecerli(n))
                return;
            if (ayni.Count > 0)
            {
                AyniCekUyarisi = $"Aynı yön, banka ve numarayla kayıtlı çek var: {string.Join(", ", ayni.Select(c => $"{c.Kisi} · {Bicim.Tl(c.Tutar)} ₺ · vade {c.VadeTarihi:dd.MM.yyyy}"))}. "
                    + "Ayrı bir çekse \"Yine de kaydet\"i seçin.";
                return;
            }
        }
        g = g with { IstekId = _kayit.Al(new { Duzenlenen, g }) };
        var sonuc = await api.CekKaydetAsync(Duzenlenen, g);
        if (!Gecerli(n))
            return;
        _kayit.Temizle();
        AyniCekSifirla();
        FormAcik = false;
        Guncelle(sonuc);
        Mesaj = Duzenlenen is null ? "Çek kaydedildi." : "Çek güncellendi.";
    });

    /// <summary>Açık çeki siler (sayfa önce onay ister). Kapatılmış aydaki kasa hareketi olan çeki sunucu reddeder.</summary>
    public Task SilAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Acik is not { } c)
            return;
        var g = new CekSilYaz(Guid.Empty, c.Surum);
        g = g with { IstekId = _sil.Al(new { c.Id, g }) };
        await api.CekSilAsync(c.Id, g);
        if (!Gecerli(n))
            return;
        _sil.Temizle();
        if (Cekler.FirstOrDefault(s => s.Veri.Id == c.Id) is { } satir)
            Cekler.Remove(satir);
        Acik = null;
        Mesaj = "Çek silindi.";
    }, mesgulkenBildir: true);

    protected override void OturumTemizle()
    {
        Cekler.Clear();
        OncekiSatirlar.Clear();
        SonrakiSatirlar.Clear();
        KasaSecenekleri.Clear();
        CekKasaSecenekleri.Clear();
        Acik = null;
        Ozet = null;
        FormAcik = false;
        _sonKanal = null;
        SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, null);
        Ara = "";
        foreach (var anahtar in new[] { _kayit, _hareket, _geriAl, _sil })
            anahtar.Temizle();
    }
}
```

- [ ] **Adım 5: Panel kutusunun modelini yaz.** `Kasa.App.Core/CekOzetViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Panelin Çekler kutusu (docs/specs/2026-10-01-cekler.md "Panel"): GET /api/takip/cekler/ozet'ten üç satır. Teminat
/// çekleri sunucuda dışarıda bırakılır. Kutudaki düğmeler Çekler sayfasını ilgili hazır süzgeçle açar (//cekler?Suzgec=…).</summary>
public partial class CekOzetViewModel(ICekApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Alinan30Metni), nameof(Verilen30Metni), nameof(GecmisMetni))]
    private CekOzetDto? _ozet;

    public string Alinan30Metni => Ozet is { } o ? CekMetni.OzetSatiri("30 gün içinde tahsil edilecek", o.Alinan30) : "";
    public string Verilen30Metni => Ozet is { } o ? CekMetni.OzetSatiri("30 gün içinde ödenecek", o.Verilen30) : "";
    public string GecmisMetni => Ozet is { } o ? CekMetni.OzetSatiri("Vadesi geçmiş, tahsil edilmemiş", o.VadesiGecmis) : "";

    public Task YukleAsync() => YurutAsync(async n =>
    {
        var ozet = await api.CekOzetAsync();
        if (!Gecerli(n))
            return;
        Ozet = ozet;
        Tamamlandi();
    });

    protected override void OturumTemizle() => Ozet = null;
}
```

- [ ] **Adım 6: Testlerin geçtiğini gör.**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:  1064` (10 yeni). Uyarı yok.

Çalıştır: `dotnet test Kasa.Sozlesme.Tests/Kasa.Sozlesme.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~MimariTests"`
Beklenen: başarılı (Kasa.App.Core'da kod değeri dizesi yok; `KodCipi`, `CekMetni` Kodlar adlarıyla çakışmaz).

- [ ] **Adım 7: Commit.**

```bash
git add Kasa.App.Core/CekTakipModelleri.cs Kasa.App.Core/CekTakipViewModel.cs Kasa.App.Core/CekOzetViewModel.cs Kasa.App.Core.Tests/CekTakipViewModelTests.cs
git commit -F - <<'MESAJ'
feat(app-core): çekler görünüm modeli

Süzgeçler (yön, durum, arama, vade) sunucuda; hazır süzgeçler şerit ve panel için;
ayrıntı satırın altında açılır; hareket formu bugün, kalan ve son kasayla açılır;
aynı çek kaydetmeden önce uyarılır; bildirimden gelen çek yönü ve "Hepsi" ile açılır.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 11: Menü, rol, MAUI sayfası, kabuk, DI ve panel kutusu

**Dosyalar:**
- Değiştir: `Kasa.App.Core/Rol.cs`, `Kasa.App.Core/MenuModeli.cs`, `Kasa.App.Core.Tests/RolTests.cs`, `Kasa.App.Core.Tests/MenuModeliTests.cs`
- Oluştur: `Kasa.App/Views/CekTakipPage.cs`
- Değiştir: `Kasa.App/AppShell.xaml`, `Kasa.App/AppShell.xaml.cs`, `Kasa.App/MauiProgram.cs`, `Kasa.App/Views/PanelPage.xaml.cs`

- [ ] **Adım 1: Rol ve menü testlerini güncelle.**
  - `Kasa.App.Core.Tests/RolTests.cs`: iki beklenen listede `Bolum.Kartlar, Bolum.Krediler, Bolum.DisariAktar` →
    `Bolum.Kartlar, Bolum.Krediler, Bolum.Cekler, Bolum.DisariAktar`; `Bolum_sirasi_panelle_baslar` testinden önce:

```csharp
    [Fact]
    public void Cekleri_izleyici_ve_editor_gorur_alici_gormez()
    {
        Assert.Contains(Bolum.Cekler, SekmeModeli.Bolumler(Rol.Izleyici));
        Assert.Contains(Bolum.Cekler, SekmeModeli.Bolumler(Rol.Editor));
        Assert.DoesNotContain(Bolum.Cekler, SekmeModeli.Bolumler(Rol.Alici));
    }
```

  - `Kasa.App.Core.Tests/MenuModeliTests.cs`: iki yerde `"Kart ve kredi: Kartlar, Krediler",` → `"Kart, kredi ve çek: Kartlar, Krediler, Çekler",`;
    `Gorunur_ogesi_kalmayan_grubun_basligi_da_gizlenir`'de `"Kart ve kredi: Kartlar"` → `"Kart, kredi ve çek: Kartlar"`;
    `Duzen_her_bolumu_bir_kez_ve_ayri_simgeyle_tasir` sonuna `Assert.Equal("cekler", MenuModeli.Rota(Bolum.Cekler));`.

- [ ] **Adım 2: Testlerin düştüğünü gör.**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false --filter "FullyQualifiedName~RolTests|FullyQualifiedName~MenuModeliTests"`
Beklenen: derleme hatası (CS0117 `Bolum.Cekler`).

- [ ] **Adım 3: Bölümü ve menü öğesini ekle.**
  - `Kasa.App.Core/Rol.cs`: enum'un sonuna `Cekler` (`…, AylikGiderler, EkstreAktar, Cekler }`); `SekmeModeli.Bolumler`'de
    `Bolum.Kartlar, Bolum.Krediler, Bolum.DisariAktar,` → `Bolum.Kartlar, Bolum.Krediler, Bolum.Cekler, Bolum.DisariAktar,`.
  - `Kasa.App.Core/MenuModeli.cs` `MenuSimgeleri` içinde `Krediler` satırının altına (yazdıktan sonra `git diff` ile `""` denetle):

```csharp
    public const string Cekler = "";         // Document
```

  - `Duzen`'deki kart grubunu şununla değiştir:

```csharp
        new("Kart, kredi ve çek",
        [
            new(Bolum.Kartlar, "Kartlar", MenuSimgeleri.Kartlar, "kartlar"),
            new(Bolum.Krediler, "Krediler", MenuSimgeleri.Krediler, "krediler"),
            new(Bolum.Cekler, "Çekler", MenuSimgeleri.Cekler, "cekler"),
        ]),
```

- [ ] **Adım 4: Sayfayı yaz.** `Kasa.App/Views/CekTakipPage.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using Kasa.App.Controls;
using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>
/// Çekler ve senetler (docs/specs/2026-10-01-cekler.md "Masaüstü ekranı"). Üst şerit dört hazır süzgeçtir; altında yön ve durum
/// çipleri ile arama. Liste vadeye göre sıralıdır; açılan çekin ayrıntısı satırının hemen altında görünür (OncekiSatirlar, ayrıntı,
/// SonrakiSatirlar). Hareket düğmeleri sunucunun izin verdiği türlerdir. Bildirimden //cekler?CekId={id}, panel kutusundan
/// //cekler?Suzgec={CekHazirSuzgec} ile açılır; sayfa açıkken gelen istek hemen, değilse sayfa belirirken uygulanır (SorguSecimi).
/// </summary>
public sealed class CekTakipPage : TakipSayfasi<CekTakipViewModel>, IQueryAttributable
{
    private readonly SorguSecimi _secim = new("CekId");
    private CekHazirSuzgec? _bekleyenSuzgec;
    private bool _gorunuyor;
    private readonly View _ayrinti;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Suzgec", out var deger) && Enum.TryParse<CekHazirSuzgec>(Convert.ToString(deger, CultureInfo.InvariantCulture), out var suzgec))
        {
            _bekleyenSuzgec = suzgec;
            if (_gorunuyor)
                _ = SuzgeciUygulaAsync();
        }
        if (_secim.Iste(query))
            _ = SecimiUygulaAsync();
    }

    protected override async void OnAppearing()
    {
        _gorunuyor = true;
        _secim.Gorunuyor = true;
        await SuzgeciUygulaAsync();
        await SecimiUygulaAsync();
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _gorunuyor = false;
        _secim.Gorunuyor = false;
        base.OnDisappearing();
    }

    /// <summary>Panel kutusundan gelen hazır süzgeç; hata günlüğe yazılır (OnAppearing async void'dir).</summary>
    private async Task SuzgeciUygulaAsync()
    {
        try
        {
            if (_bekleyenSuzgec is not { } suzgec)
                return;
            _bekleyenSuzgec = null;
            await Vm.HazirSuzgecAsync(suzgec);
        }
        catch (Exception ex) { Debug.WriteLine($"Çek süzgeci uygulanamadı: {ex}"); }
    }

    /// <summary>Bildirimden gelen çek: çekin yönü ve "Hepsi" süzgeciyle yüklenir, seçilir ve ayrıntısı görünür yere kaydırılır.</summary>
    private async Task SecimiUygulaAsync()
    {
        try
        {
            var id = _secim.Istenen;
            if (await _secim.UygulaAsync(() => id is { } i ? Vm.CekIcinYukleAsync(i) : Vm.YukleAsync(), () => Vm.VeriHazir && Vm.Hata is null, Vm.IdIleSec))
                await Kaydirici.ScrollToAsync(_ayrinti, ScrollToPosition.Start, true);
        }
        catch (Exception ex) { Debug.WriteLine($"Çek bildirimi seçimi başarısız: {ex}"); }
    }

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
        Govde.Add(Kart("Liste", SatirListesi(vm, nameof(vm.OncekiSatirlar)), _ayrinti, SatirListesi(vm, nameof(vm.SonrakiSatirlar))));
    }

    private static View Serit(CekTakipViewModel vm)
    {
        var serit = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (yol, suzgec) in new[]
        {
            (nameof(vm.PortfoyMetni), CekHazirSuzgec.Portfoy), (nameof(vm.Alinan30Metni), CekHazirSuzgec.Alinan30),
            (nameof(vm.Verilen30Metni), CekHazirSuzgec.Verilen30), (nameof(vm.GecmisMetni), CekHazirSuzgec.VadesiGecmis),
        })
        {
            var kutu = new Button { Style = (Style)Application.Current!.Resources["BtnSecondary"], Margin = new Thickness(0, 0, 8, 8) };
            kutu.SetBinding(Button.TextProperty, yol);
            kutu.Clicked += async (_, _) => await vm.HazirSuzgecAsync(suzgec);
            serit.Add(kutu);
        }
        return serit;
    }

    private static CipGrubu Cipler(string kaynak, string komut)
    {
        var grup = new CipGrubu();
        grup.SetBinding(BindableLayout.ItemsSourceProperty, kaynak);
        grup.SetBinding(CipGrubu.SecCommandProperty, komut);
        return grup;
    }

    private static View SatirListesi(CekTakipViewModel vm, string yol) => Liste<CekSatiri>(yol, s =>
    {
        vm.SecCommand.Execute(s);
        return Task.CompletedTask;
    }, "Aç / kapat");

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
            Children =
            {
                Cipler(nameof(vm.HareketCipleri), nameof(vm.SecHareketCommand)), hareketFormu,
                Goster(Tikla("Son hareketi geri al", () => OnayliAsync("Son hareketi geri al", "Çekin son hareketi silinir; kasa etkisi de kalkar.", vm.GeriAlAsync)),
                    nameof(vm.GeriAlinabilir)),
                Dugme("Çeki düzelt", nameof(vm.DuzeltCommand)),
            },
        };
        var sil = new Button { Text = "Çeki sil", HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
        sil.Clicked += async (_, _) => await SilmeOnayi.GosterAsync(this, sil, vm.AcikOzet, vm.SilAsync);
        duzenleme.Add(sil);
        return Goster(new Border
        {
            Style = (Style)Application.Current!.Resources["CardForm"],
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children = { BagliBuyuk(nameof(vm.AcikOzet)), Liste<CekHareketSatiri>(nameof(vm.Hareketler)), Editor(duzenleme) },
            },
        }, nameof(vm.CekAcik));
    }

    private async Task OnayliAsync(string baslik, string metin, Func<Task> islem)
    {
        if (await DisplayAlertAsync(baslik, metin, "Devam", "Vazgeç"))
            await islem();
    }

    private static View Form(CekTakipViewModel vm)
    {
        var konum = Alan("Konum", Secim(nameof(vm.KonumSecenekleri), nameof(vm.Konum)));
        konum.SetBinding(IsVisibleProperty, nameof(vm.FormVerilen), converter: new Converters.TersIseConverter());
        var ayni = Goster(new VerticalStackLayout
        {
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

- [ ] **Adım 5: Kabuğa ve DI'a bağla.**
  - `Kasa.App/AppShell.xaml`: `KredilerItem` satırının altına

```xml
    <FlyoutItem x:Name="CeklerItem" Title="Çekler" Route="cekler" IsVisible="False"><ShellContent ContentTemplate="{DataTemplate v:CekTakipPage}" /></FlyoutItem>
```

  - `Kasa.App/AppShell.xaml.cs` `_menu` sözlüğünde `[Bolum.Krediler] = KredilerItem,` altına `[Bolum.Cekler] = CeklerItem,`.
  - `Kasa.App/MauiProgram.cs`: `IEkstreAktarmaApi` kaydının altına
    `builder.Services.AddSingleton<ICekApi>(sp => sp.GetRequiredService<KasaApiClient>());`; `EkstreAktarmaViewModel` kaydının altına
    `builder.Services.AddTransient<CekTakipViewModel>();` ve `builder.Services.AddTransient<CekOzetViewModel>();`;
    `Views.EkstreAktarmaPage` kaydının altına `builder.Services.AddTransient<Views.CekTakipPage>();`.

- [ ] **Adım 6: Panel kutusunu ekle.** `Kasa.App/Views/PanelPage.xaml.cs`:
  - Kurucu imzası: `public PanelPage(PanelViewModel vm, TakipOzetViewModel takip, KasaKontrolViewModel kontrol, CekOzetViewModel cekler)`.
  - `VeriVar` işleyicisinde `await kontrol.YukleAsync(vm.KasaEsikleri);` altına `await cekler.YukleAsync();`, `else` dalında
    `kontrol.VeriHazir = false;` altına `cekler.VeriHazir = false;`.
  - `PanelAlani.Add(kart);` ile `PanelAlani.Add(KasaKontrolAlanlari.Kontrol(kontrol));` arasına `PanelAlani.Add(CekKutusu(cekler));`.
  - Kurucudan sonra (`OnAppearing`'den önce) yöntem:

```csharp
    /// <summary>Çekler kutusu (docs/specs/2026-10-01-cekler.md "Panel"): üç satır; satıra tıklayınca Çekler sayfası o süzgeçle açılır.</summary>
    private static View CekKutusu(CekOzetViewModel cekler)
    {
        var satirlar = new VerticalStackLayout { Spacing = 8 };
        foreach (var (yol, suzgec) in new[]
        {
            (nameof(cekler.Alinan30Metni), CekHazirSuzgec.Alinan30), (nameof(cekler.Verilen30Metni), CekHazirSuzgec.Verilen30),
            (nameof(cekler.GecmisMetni), CekHazirSuzgec.VadesiGecmis),
        })
        {
            var satir = new Button { HorizontalOptions = LayoutOptions.Start, Style = (Style)Application.Current!.Resources["BtnSecondary"] };
            satir.SetBinding(Button.TextProperty, yol);
            satir.Clicked += async (_, _) => await Shell.Current.GoToAsync($"//cekler?Suzgec={suzgec}");
            satirlar.Add(satir);
        }
        satirlar.SetBinding(IsVisibleProperty, nameof(cekler.VeriHazir));
        var kutu = TakipUi.Kart("Çekler", TakipUi.Metin("Teminat çekleri bu toplamlara girmez."), TakipUi.BagliHata(nameof(cekler.Hata)), satirlar,
            TakipUi.Tikla("Çekler", () => Shell.Current.GoToAsync("//cekler")));
        kutu.BindingContext = cekler;
        return kutu;
    }
```

- [ ] **Adım 7: Testleri, MAUI denetimini ve Windows derlemesini çalıştır.**

Çalıştır: `dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false`
Beklenen: `Başarılı!  - Başarısız:     0, Başarılı:  1065` (`MauiKayitTutarliligiTests`: kabuk sayfası DI'da, sayfa/VM bağımlılıkları kayıtlı,
her `Bolum` bir `CeklerItem` gibi öğeye bağlı, menü rotası kabuktakiyle aynı, `CekSatiri`/`CekHareketSatiri` Baslik+Ozet taşır).

Çalıştır: `bash .github/scripts/maui-lint.sh`
Beklenen: `maui-lint: … (0 tanımsız); doğrudan hex renk 2, 200 karakteri aşan satır 8.` ve `maui-lint: taban içinde.`

Çalıştır: `dotnet build Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -m:2 -nodeReuse:false --no-incremental`
Beklenen: `0 Uyarı`, `0 Hata`.

- [ ] **Adım 8: Commit.**

```bash
git add Kasa.App.Core/Rol.cs Kasa.App.Core/MenuModeli.cs Kasa.App.Core.Tests/RolTests.cs Kasa.App.Core.Tests/MenuModeliTests.cs Kasa.App/Views/CekTakipPage.cs Kasa.App/AppShell.xaml Kasa.App/AppShell.xaml.cs Kasa.App/MauiProgram.cs Kasa.App/Views/PanelPage.xaml.cs
git commit -F - <<'MESAJ'
feat(app): çekler sayfası, menü ve panel kutusu

"Kart, kredi ve çek" grubuna Çekler (izleyici ve editör). Sayfa: üst şerit, yön ve
durum çipleri, arama, satırın altında açılan ayrıntı, hareket formu, geri alma,
yeni çek formu ve aynı çek uyarısı. //cekler?CekId= ve ?Suzgec= ile açılır; panelde
Çekler kutusu.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 12: Belge ve sürüm notu

**Dosyalar:**
- Değiştir: `docs/deploy/kasa-2.4.md`, `docs/specs/2026-10-01-cekler.md`

- [ ] **Adım 1: Sürüm taslağına kullanıcıya görünen değişikliği yaz.** `docs/deploy/kasa-2.4.md` "## 1. Kullanıcıya görünen değişiklikler"
  bölümünde `**Kullanıcıya görünmeyenler:**` paragrafından hemen önce:

```markdown
**Çek ve senet takibi** ([tasarım](../specs/2026-10-01-cekler.md), [plan](../specs/2026-10-01-cekler-plan.md)): masaüstünde
"Kart, kredi ve çek" menüsünde yeni **Çekler** sayfası (izleyici ve editör görür, alıcı görmez). Alınan ve verilen çek ve senet
eklenir; tahsil, ödeme, ciro, kırdırma, dönüş, karşılıksız ve iade girilir, son hareket geri alınır. Kayıt ve vade günü kasayı
değiştirmez; kasa yalnız hareket gününde değişir. Vadeye 3 gün kala ve vade günü hatırlatma (alınan çekte vadenin 7. günü ibraz
uyarısı), panelde Çekler kutusu. Önerilen sürüm notu: "Çek ve senet takibi: tahsil, ödeme, ciro, kırdırma, vade hatırlatması."
`/api/surum` `notlar` metnine eklenmesi yayın kararıdır.
```

- [ ] **Adım 2: Rapor etkisini yaz.** Aynı dosyada "## 2. Rapor ve muhasebe etkisi" listesinin sonuna:

```markdown
- **Çek hareketleri türetilmiş satırdır** (veritabanına gider ya da gelir yazılmaz): tahsilat o günün Gelen'i, verilen çekin ödemesi
  Cari gider (Ortak kasada ayın Ortak payına bölünür), ciro aynı kasada +Gelen ve aynı tutarda Cari gider (kasa ve ay sonucu
  değişmez), kırdırma +Gelen ve masraf kadar Cari gider, dönüş ters satırlar (eksi Gelen; ciroda eksi Cari gider de). Çek girilmediği
  sürece raporlar aynıdır (`AltinRaporTests`); kapatılmış aylar dondurulmuş görüntüden döner. Kasa hareket dökümünde yeni tür "Çek".
```

- [ ] **Adım 3: Migration kaydını yaz.** "## 3. Migration'lar" bölümünde "v2.3.0'a göre 14 yeni migration" → "v2.3.0'a göre 15 yeni
  migration"; tablonun son satırının altına:

```markdown
| `20261008000100_Cekler` | `Cekler` ve `CekHareketler` (boş; kanal ve çek bağları ON DELETE RESTRICT) | Tablo |
```

- [ ] **Adım 4: Tasarımın durumunu güncelle.** `docs/specs/2026-10-01-cekler.md` ikinci satırını şununla değiştir:

```markdown
Tarih: 2026-10-01 · Durum: tasarım onaylandı, uygulandı ([plan](2026-10-01-cekler-plan.md)) · Dal: `ozellik/cekler` (taban `release/2.x`)
```

- [ ] **Adım 5: Commit.**

```bash
git add docs/deploy/kasa-2.4.md docs/specs/2026-10-01-cekler.md
git commit -F - <<'MESAJ'
docs(spec): çekler sürüm notu ve migration kaydı

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
MESAJ
```

---

## Görev 13: Tam doğrulama

Kod değişmez; bir adım düşerse ilgili görevin testine dönülür, düzeltme kendi `fix(...)` commit'iyle yapılır.

- [ ] **Adım 1: Bütün test projeleri** (sırayla, aynı anda tek `dotnet`):

```bash
dotnet test Kasa.Core.Tests/Kasa.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.ApiClient.Tests/Kasa.ApiClient.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.App.Core.Tests/Kasa.App.Core.Tests.csproj -c Release -m:2 -nodeReuse:false
dotnet test Kasa.Sozlesme.Tests/Kasa.Sozlesme.Tests.csproj -c Release -m:2 -nodeReuse:false
```

Beklenen sırasıyla: `Başarılı:   127`, `Başarılı:  1256`, `Başarılı:   182`, `Başarılı:  1065`, `Başarılı:    56`; hepsinde `Başarısız:     0`.

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
Beklenen: `maui-lint: taban içinde.` (taban artmaz; `.github/scripts/maui-lint-tabani.txt` değişmez).

- [ ] **Adım 6: Durum.** `git status` temiz; `git log --oneline origin/release/2.x..HEAD` tasarım, plan ve 12 görev commit'ini gösterir.
  Push yapılmaz.

---

## Tasarım → görev eşlemesi (öz inceleme)

| Tasarım maddesi | Görev |
|---|---|
| Karar 1–2: kasa yalnız tahsil/ödeme gününde; tahsil Gelen'e, ödeme Cari gidere | 1 (türetici), 3 (servis) |
| Karar 3: ciro +Gelen ve Cari gider, net 0; dönüşte iki ters satır | 1, 3 (`Ciro_kasa_ve_ay_sonucunu_degistirmez_…`) |
| Karar 4: kırdırma +Gelen, masraf Cari gider, kasaya net; dönüşte −Gelen | 1, 3 (`Kirdirma_kasaya_net_…`) |
| Karar 5: kısmi tahsil/ödeme, kalan, kalan sıfırlanınca kapanır | 1 (`Kismi_tahsil_…`), 5 |
| Karar 6: senet, konum, vade hatırlatması, panel özeti | 1, 5, 7, 8, 10, 11 |
| Karar 7: türetilmiş satır; rapor formülü, kural sürümü, dondurulan ay biçimi değişmez | 3 (altın test, `Kapatilmis_ay_…`) |
| Kayıt modeli (`Cek`, `CekHareket`, `Surum`, durum saklanmaz) | 2 |
| Durum ve izinli geçişler, geri alma yalnız son hareket | 1, 5 |
| Rapora etkisi tablosu, anahtarlar `Cek:{id}` / `:gider`, Ortak bölüşümü | 1, 3 |
| Kasa dökümünde yeni tür Çek ve açıklamalar | 3, 6 |
| Ay kilidi (hareket; çekin tutar/kasa/yön/tür; silme) | 4, 5 (`Kapatilmis_aydaki_hareket_…`) |
| Uçlar, istekId, surum, Türkçe hata, aynı çek uyarısı | 5 |
| Yetki: İzleyici okur, Alıcı erişemez; `UcEnvanteriTests`, `UcYetkiTaramasiTests` | 5, 8 |
| DTO'lar `Kasa.Sozlesme.Tests` eşlemesinde | 9 |
| Değişiklik geçmişi iki varlığı yakalar | 6 |
| Bildirimler: üç kural, teminat ve kapanmış hariç, hedef `/#cheques/{id}`, masaüstü rota | 7 |
| Masaüstü: menü grubu, `Bolum.Cekler`, şerit, süzgeçler, liste, satır altı ayrıntı, düğmeler, form varsayılanları, geri al, yeni çek, aynı çek uyarısı, desen (`TakipSayfasi<T>`, `OturumluViewModel`, `IQueryAttributable`, `TekrarAnahtari`) | 10, 11 |
| Panel kutusu (üç satır, teminat hariç, filtreyle açılır) | 8, 10, 11 |
| Testler bölümü (kural, hesap/rapor, kilit, uç, bildirim, istemci) | 1, 3, 4, 5, 7, 9, 10, 11 |
| Yayın notu, migration | 2, 12 |
| Kapsam dışı (döviz, ciro zinciri, risk raporu, komisyon, web) | uygulanmaz; web kasa kontrolü ekranı yeni türü ham "Cek" koduyla gösterir |

**Yer tutucu taraması:** planda "TBD", "TODO", "benzer şekilde" yok; her kod adımı tam koddur; değişiklikler bul-değiştir metniyle verilmiştir.
**Tür tutarlılığı:** `CekBilgisi`, `CekHareketi`, `CekDurumu`, `CekKurallari` (`Durum`, `IzinliHareketler`, `HareketHatasi`, `KasaEtkili`,
`AyniCek`, `DurumAdi`, `HareketAdi`), `CekTuretici.Satirlar`; sunucu `CekServisi` (`Oku`, `Tek`, `Liste`, `Ozet`, `Cekirdek`), `CekBildirimleri`
(`Kaynak`, `VadeTuru`, `IbrazTuru`, `OdemeTuru`, `Oku`); istemci `ICekApi` (`CeklerAsync`, `CekAsync`, `CekOzetAsync`, `CekKaydetAsync`,
`CekSilAsync`, `CekHareketEkleAsync`, `CekHareketGeriAlAsync`); masaüstü `CekTakipViewModel` (`YukleAsync`, `CekIcinYukleAsync`, `IdIleSec`,
`HazirSuzgecAsync`, `GeriAlAsync`, `SilAsync`), `CekOzetViewModel`, `CekHazirSuzgec` — görevler arasında aynı adlarla kullanılır.
