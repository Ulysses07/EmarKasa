# Paket A: Panel, tahmin ve bildirimler (tasarım)

Tarih: 2026-09-24 · Dal: `paket-a` · Kapsam: özellik 20, 03, 31, 08, 23, 09

## Amaç ve sınır

Paket A, ortakların Panel'e bakınca "bugün ne durumdayız, önümüzde ne var, kim ne değiştirdi" sorusunu
tek ekranda yanıtlamasını sağlar. Hepsi **yalnız okur ve gösterir**:

- `HesapMotoru` çıktıları, kart kuralı ve çekin "vadede kasaya" kuralı **değişmedi**. Yeni rakamlar
  mevcut rakamların **yanında** hesaplanır; mevcut uçların ve raporların sonuçlarının aynı kaldığı
  testlerle doğrulanır (`Kasa.Api.Tests/PanelPaketATests`: `Tahmin_raporlari_ve_gecmisi_degistirmez`, `Tahmin_edilen_kasa_zaman_gelince_gercek_kasaya_esit`).
- Şema değişikliği yok (yeni tablo/sütun yok). Sunucu yapılandırması değişmedi.
- "Bugün" sunucuda `Saat.Bugun(TimeProvider)` ile alınır (Türkiye saati).
- Satırlardaki düğmeler **işin yapılacağı yere götürür ve formu o kayıtla hazırlar** (derin bağlantı), ama
  hiçbir şey kaydetmez: son adım (Kaydet / Ekle) kullanıcınındır. Tek dokunuşla tahsil/ödeme (onaysız yazma)
  bu pakette yok (Paket D).

## Özellikler

### 20 · Hızlı durum kartları (Panel)

Hero kartının altında dört kutu. Masaüstünde 4 sütun, dar ekranda 2×2. Kutuya dokunmak ilgili sayfayı açar.

| Kutu | İçerik | Kaynak |
|---|---|---|
| Kart ekstreleri | Ödenmemiş ekstre toplamı; en yakın son ödeme ("… · 30 Eylül", geçtiyse kırmızı); limit uyarısı | `GET /api/kredikartlari` |
| Çekler | "Tahsil edilecek X ₺ · N çek", "Ödenecek Y ₺ · M çek", vadesi geçen (yöne göre ayrı: "2 çekin vadesi geçti · tahsil edilecek +5.000,00 ₺ · ödenecek −3.000,00 ₺") | `GET /api/cekler/ozet` |
| Son kasa sayımı | Tarih ("20 Eylül (4 gün önce)") ve fark (renkli) | `GET /api/kasasayimlari` |
| Defter | "Defter en son 2 saat önce güncellendi" + son bakıştan beri değişiklik (özellik 23) | `GET /api/gecmis/ozet` |

Bir kaynak okunamazsa o kutu "… bilgisi alınamadı" der. Ana panel (kasa, kanallar, bekleyen giderler)
bu okumalardan etkilenmez.

Vadesi geçen çeklerde alınan (bize gelecek, +) ve verilen (bizden çıkacak, −) çek **tek toplamda
birleştirilmez**; iki yön ayrı yazılır (`PanelMetin.CekYonToplamlari`). Pazartesi bildirimi ve Bugün
yapılacaklar aynı işaretleri kullanır.

### 03 · Nakit tahmini (30/60/90 gün)

Güncel kasadan (`HesapServisi.Panel().GuncelKasa`, hesap motorunun rakamı) başlar ve yalnız bilinen/planlanmış
hareketleri gün gün ekler. Saf motor `Kasa.Core/NakitTahmini.cs`, veri yükleme `Kasa.Api/Servisler/TahminServisi.cs`.

| Kalem | Gün | İşaret |
|---|---|---|
| Portföydeki alınan çek | vade | + |
| Ödenecek (portföydeki) verilen çek | vade | − |
| Tahsil edildi/ödendi olarak ileri tarihle girilmiş çek | işlem tarihi | ± |
| Kart ekstresi (`KartHesap.Durum`, açık ekstreler) | son ödeme | − |
| İleri tarihli girilmiş kart ödemesi | tarihi | − |
| Onay bekleyen tekrarlayan gider | yarın | − |
| Ufuk içinde vadesi gelecek tekrarlayan gider (yalnız sıklığına uyan aylar) | vade | − |
| Karta bağlı tekrarlayan gider (Paket D), bekleyen ya da gelecek | kartın ekstresinde, son ödeme | − |
| İleri tarihli gider işlemi (Cari / sabit gider) | tarihi | − |
| Karta bağlı olmayan eski K.K | sonraki ayın son döneminin ilk günü | − |

- Vadesi/son ödemesi geçmiş ama hâlâ bekleyen kalem **yarına** yazılır ve "gecikmiş" işaretlenir.
- **Tekrarlayan giderler (Paket D alanlarıyla):** şablon `GET /api/tekrarlayangiderler/bekleyen`'deki gibi bütün
  alanlarıyla okunur. Sıklık (3 ayda / 6 ayda / yılda bir) `TekrarlayanTakvim.AyDahil` ile uygulanır: yıllık
  bir gider her ay düşülmez. Tutarı her seferinde girilen şablonda kayıtlı tutar tahmindir ("tahmini tutar");
  tutar yazılmamışsa (hazır vergi şablonları, 0) tahmine girmez. Karta bağlı şablon kasadan vadede **düşmez**
  (kart kuralı: kart harcaması kasayı ödeme gününde etkiler): o ay karta vadesiyle girilmiş harcama sayılır, kartın
  ekstresine eklenir ve ekstrenin son ödeme gününde çıkar. Onaylanınca (vadesiyle) tahmin değişmez.
- Gelecek gelenler (satış) bilinmediği için tahmine **girmez**.
- **En düşük gün** ve kasası öne çıkarılır ("En düşük: 14 Kasım, −12.500,00 ₺"). Eksi ise kırmızı.
- **Riskli çek:** tahmindeki her çek "Hesaptan çıkar / Hesaba kat" ile tahmin dışında bırakılabilir.
  Seçim yalnız o bilgisayarda saklanır (`tahmin.haric`) ve çekin kaydını değiştirmez. Hariç çekler yanıtta
  `haricKalemler` içinde döner.
- Seçilen ufuk (30/60/90) cihazda hatırlanır (`tahmin.gun`).
- Listede hareket olan günler gösterilir. En düşük gün hareketsizse (ve bugün değilse) o da gösterilir.

### 31 · Bugün yapılacaklar (yalnız editör)

Panel'de bir kart. Her satırın bir düğmesi vardır. Düğme işin yapılacağı sayfayı **o kayıtla** açar
(`DerinBaglanti`: Shell rotası + sorgu; hedef sayfa `IQueryAttributable` ile sorguyu VM'e iletir). Hiçbir
düğme kendiliğinden kaydetmez:

| Satır | Koşul | Düğme → hedef |
|---|---|---|
| N tekrarlayan gider onay bekliyor (tek satır, toplam) | bekleyen liste boş değil | **Göster**: Panel'deki bekleyen kartına kaydırır (kartın kendi Kaydet'i var) |
| Vadesi geçen N çek | portföyde, vade < bugün | **Çeki aç / İlk çeki aç**: `//cekler?id=…` en eski vadeli çek düzenleme formunda açılır |
| Bugün vadesi gelen N çek | vade = bugün | **Çeki aç / İlk çeki aç** (aynı) |
| 3 gün içinde vadesi gelecek N çek | bugün < vade ≤ bugün+3 | **Çeki aç / İlk çeki aç** (en yakın vadeli) |
| Kart · son ödeme bugün/yarın/… | ödenmemiş ekstre (`KartHatirlatici.OdemeBekliyor`) | **Ödeme gir**: `//kartlar?id=…` kart vurgulanır, "Ödeme ekle" tutarı ekstre borcuyla (tarih bugün) doldurulur |
| Gelen girilmedi: 14–20 Eylül | son 14 günde biten dönemde aktif kanalın geleni yok (kanal o dönemde var ve aktifti) | **Gelen gir**: `//islemler?donem=2026-09-14&kanal=…` gelen formu o dönem ve ilk eksik kanalla açılır, forma kaydırılır |
| Kasa sayımı N gündür yapılmadı | son sayım 7 günden eski ya da hiç yok | **Sayım yap**: Kasa Sayımı |

- Eksik gelen kuralı Gelenler sayfasının eksik listesiyle (Paket C, `GET /api/gelenler/eksik-liste`) aynıdır
  (`KanalDonemleri`): kanal, eklenmeden (başlangıcı: geçmişteki "Eklendi" satırı ya da ilk hareketi) önce biten ve
  baştan sona pasif olduğu dönemler için eksik sayılmaz; başlangıcı hiç bilinmeyen (geçmişi ve hareketi olmayan)
  kanal listelenmez. Bugün eklenen kanal geçen haftalar için "Gelen girilmedi" çıkarmaz.
- Birden çok çekli satırda düğme açıklamadaki ilk çeki açar ("İlk çeki aç"). O çek işlenip Panel'e
  dönülünce liste tazelenir ve sıradaki çek ilk olur.
- Kanal adı sorguda kodlanır (Türkçe harf, boşluk, `&`); Shell değeri kod çözmeden verdiği için
  `DerinBaglanti.Oku` çözer.
- Kart isteği sayfa açılışındaki yüklemede **bir kez** uygulanır: yazılmış (kaydedilmemiş) tutar ezilmez,
  sonraki yüklemede vurgu kalkar. Silinmiş kart ya da çek isteği sessizce düşer (çekte "Çek bulunamadı").
- Menüden boş sorguyla gelinince formlar olduğu gibi kalır.

Sabah bildirimi: editöre günde en fazla bir kez, **günün ilk çalıştırmasında** gider — sabah uygulama
açılınca ya da 09:00 arka plan hatırlatıcısında, hangisi önce gelirse (özellik 09). Okuma hatasında o gün
işaretlenmez, sonraki çalıştırma yeniden dener.

### 08 · Kart limit uyarısı (%80)

`KartLimit.UyariVar`: limit > 0 ve güncel borç ≥ limitin %80'i. Metin "Limitin %85'i kullanıldı" ya da
"Limit aşıldı (%112)" olur (yüzde aşağı yuvarlanır). Uyarı iki yerde görünür:

- Kredi Kartları sayfasında her kartın altında kırmızı şerit.
- Panel'deki kart kutusunda: "Limit uyarısı: Bonus %85 · Axess limit aşıldı".

### 23 · Son bakıştan beri / geçmişe dönük

- **Geçmişe dönük kuralı** (`Kasa.Api/Data/GecmiseDonuk.cs`) geçmiş satırının kendisinden okunur, ayrı sütun
  tutulmaz. Bu yüzden eski satırlar için de geçerlidir.
  - Tarihli para kayıtları (işlem, gelen, kart ödemesi, çek) için: kaydın eski ya da yeni hâlinin
    **rakamları etkilediği ay**, değişikliğin ayından (Türkiye saati) önceki bir aydaysa satır geçmişe dönüktür.
  - Bu ay çoğunlukla kaydın tarihinin ayıdır. **K.K harcamasında** (karta bağlı ya da eski usul
    `tip=KrediKarti`; `HesapMotoru.EtkinTip`) bir sonraki aydır: aylık kârda ertesi ayın K.K'sı olarak, kasada
    ertesi ayın son döneminde ya da kartın ödendiği gün düşer; kendi ayının rakamı değişmez. Böylece geçen
    ayın ekstresini bu ay girmek (her ay yapılan iş) uyarı çıkarmaz. İki ay önceki K.K ise geçen (kapanmış)
    ayın sonucunu değiştirdiği için geçmişe dönüktür. Geçen ayın Cari/Sabit gideri K.K'ya çevrilirse eski hâli
    geçen ayı etkilediğinden yine geçmişe dönüktür.
  - Güncellemede ayrıca paraya dokunan bir alan değişmiş olmalı.
  - Çek yalnız tahsil edildi/ödendi durumundayken sayılır ve tarihi işlem tarihidir.
  - Açılış devri değişiklikleri her zaman geçmişe dönüktür: kasa açılış devri, takip başlangıcı, kanal açılış devri.
  - Kasa sayımı, kart tanımı ve ad değişiklikleri geçmişe dönük sayılmaz.
  - Bilinen sınır: K.K harcaması kendi ayında kanalı "hareketli" yapar (`AylikHesapla`). O ay başka hiç
    hareketi olmayan bir kanala geçen ay tarihli K.K girilirse geçen ayın Ortak pay dağılımı değişebilir.
    Bu, satırdan (veritabanına bakmadan) anlaşılamaz ve işaretlenmez.
- `GET /api/gecmis` satırlarına `gecmiseDonuk` alanı eklendi. Alan sonda ve varsayılanı false; eski
  istemciler etkilenmez.
- Panel'de şu satır gösterilir: "Son bakışınızdan beri 12 değişiklik, 2'si geçmiş aylara dokunuyor". Yanında
  **Göster** (Geçmiş) ve **Tamam** (görüldü say) düğmeleri vardır. Geçmişe dönük olanlar (en fazla 5, en yeni
  önce) ayrı kartta listelenir.
- Geçmiş sayfasında son bakıştan sonraki satırlar yeşil **Yeni** etiketi alır. Geçmişe dönük satırlar kırmızı
  **Geçmişe dönük** etiketi alır. Liste başında "Son bakışınızdan beri N yeni değişiklik" yazar.
- "Son görülen" satır Id'si **cihaza özeldir** (`gecmis.sonGorulenId`).
  - İlk açılışta birikmiş eski satırlar "yeni" sayılmaz.
  - Geçmiş filtresiz açılınca ya da Panel'de "Tamam"a basılınca görüldü sayılır.
  - Sunucu geçmişi sıfırlanırsa sayaç yeniden başlar.
  - Sayıma kişinin kendi değişiklikleri de girer (satırlarda kişi bilgisi yok).
  - Yalnız **defter** değişiklikleri sayılır: sayı, `sonId` ve "Defter en son … güncellendi" zamanı Paket E'nin defter
    dışı satırlarını (Kullanıcı, Güvenlik ayarı, Soru) ve yalnız gizli alanı değişen güncellemeleri (izleyici şifresi,
    oturumları kapatma) atlar. İzleyicinin soru sorması ya da editörün iki adımlı girişi açması defteri
    "güncellenmiş" göstermez. Bu satırlar Geçmiş sayfasında yine görünür.

### 09 · Bildirimler

Windows bildirimleri (e-posta yok). İki yerde çalışır ve ikisi aynı yerel depoyu paylaştığından aynı
bildirim iki kez gitmez:

- Uygulamada giriş yapılınca (menü açılışında bir kez).
- Günlük 09:00 arka plan hatırlatıcısında (`--hatirlatma-kontrol`; kaçırılırsa ilk fırsatta).

| Bildirim | Ne zaman | Tıklanınca |
|---|---|---|
| Haftalık özet: "Geçen hafta kasa sonucu +X, güncel kasa Y" | Pazartesi 08:00'den sonraki ilk çalıştırma, haftada bir | Haftalık |
| Vadesi geçen N çek (ayrı bildirim): "Tahsil edilecek +700,00 ₺ · ödenecek −300,00 ₺ · en eskisi …" | haftalık özetle birlikte | Çekler |
| Geçmişe dönük düzeltme | son bildirimden sonra yeni geçmişe dönük satır varsa; günde en fazla bir kez | Geçmiş |
| Bugün yapılacaklar (N) | yalnız editör; günün ilk çalıştırması (sabah uygulama açılışı ya da 09:00 hatırlatıcısı, hangisi önce), günde bir kez, liste boş değilse | Panel |
| Kredi kartı hatırlatmaları (mevcut) | değişmedi; yalnız anahtara bağlandı | Kredi Kartları |

- Ay sonunda ikiye bölünen haftanın iki dönemi toplanır.
- Hafta, okumaların hepsi başarılı olunca "gönderildi" işaretlenir. Hata olursa sonraki çalıştırmada yeniden denenir.
- Geçmişe dönük bildiriminin ilk çalıştırması yalnız başlangıç noktasını kaydeder.
- Windows bildirim kaydı (`AppNotificationManager.Register()`) **süreç başına bir kez** yapılır
  (`TekSeferlik`). 09:00 hatırlatıcısında kart hatırlatması ve Paket A bildirimleri ayrı servis nesneleriyle
  kaydolur; ikinci `Register()` "Already Registered" hatası atıp o günün özetini düşürüyordu. Kayıt hata
  atarsa yapılmış sayılmaz, sonraki bildirim yeniden dener.
- **Ayarlar → Bildirimler** ayrı bir bölümdür. Her tür ayrı ayrı kapatılabilir.
  - Seçimler yalnız o bilgisayarda saklanır ve varsayılan açıktır.
  - Ayarlar yalnız editörde göründüğü için aynı görünüm Panel'deki **Bildirimler** düğmesiyle de açılır
    (izleyici de kullanabilir).
  - İki yer aynı VM'i (tekil) gösterir ve görünüm her yüklenişte anahtarları depodan tazeler
    (`BildirimAyarlariViewModel.Yenile`; okunan değer geri yazılmaz). Shell'in sakladığı Ayarlar sayfası,
    Panel → Bildirimler'de yapılan değişikliği eski hâliyle göstermez.

## Sunucu uçları (yeni, hepsi kimlik doğrulamalı, yalnız okur)

Tüm uçlar `Kasa.Api/Endpoints/PanelEndpoints.cs` dosyasındadır ve `Program.cs`'e tek satırla bağlanır
(`api.MapPanelUclari();`). Yazma ucu eklenmedi. Editör ve izleyici ikisi de okuyabilir.

| Uç | Yanıt | Hata |
|---|---|---|
| `GET /api/rapor/tahmin?gun=30&haric=3,7` | `NakitTahminSonucu`: `bugun`, `gun`, `baslangicKasa`, `gunler[]` (`tarih`, `giris`, `cikis`, `kasa`, `kalemler[]`), `enDusukTarih`, `enDusukKasa`, `sonKasa`, `toplamGiris`, `toplamCikis`, `haricKalemler[]` | 400: `gun` 1–366 dışında; `haric` virgülle ayrılmış negatif olmayan sayılar değil ya da 1000'den fazla |
| `GET /api/gelenler/eksik` | `[{ donemStart, donemEnd, kanallar[] }]`: son 14 günde biten dönemler, geleni girilmemiş aktif kanallar (0 girilmiş sayılır; kanal kuralı `eksik-liste` ile aynı) | – |
| `GET /api/gecmis/ozet?sonId=123` | `{ sonId, sonZamanUtc, toplam, gecmiseDonuk, gecmiseDonukSatirlar[≤5] }`: yalnız defter satırları. `sonId` verilmezse sayılar 0 olur (başlangıç noktası) | 400: `sonId` negatif |

`gun` verilmezse 30 kabul edilir. İstemci tarafında: `IKasaApi.A.cs`, `KasaApiClient.A.cs`, `Dtos.A.cs`.

## Yerel (cihaza özel) ayarlar

`IYerelDepo`: dosya başına bir anahtar, konum `%LOCALAPPDATA%\EmarKasa\yerel`. Uygulama ve arka plan
hatırlatıcısı aynı depoyu kullanır; okuma/yazma hataları yutulur ve varsayılana dönülür.

| Anahtar | Anlam |
|---|---|
| `tahmin.gun`, `tahmin.haric` | seçili ufuk, hesaptan çıkarılan çekler |
| `gecmis.sonGorulenId` | son bakış |
| `bildirim.haftalikOzet`, `bildirim.vadesiGecenCek`, `bildirim.gecmiseDonuk`, `bildirim.bugunYapilacaklar`, `bildirim.kartHatirlatma` | aç/kapa (varsayılan açık) |
| `bildirim.sonHaftalikOzet`, `bildirim.sonGecmiseDonukId`, `bildirim.sonGecmiseDonukGunu`, `bildirim.sonYapilacaklar` | bildirim durumu |

## Varsayılanlar (seçilen)

- Tahmin güncel kasadan başlar. Satış (gelen) tahmini yok. Gecikmiş kalemler yarına yazılır.
- Tahmin ufku 30 gündür. Seçilen ufuk ve hariç çekler cihazda saklanır.
- Yapılacaklar: çek penceresi 3 gün, sayım eşiği 7 gün, eksik gelen için son 14 gün. Tekrarlayan giderler tek satırda toplanır.
- Limit uyarısı eşiği %80'dir (limit girilmemişse uyarı yok).
- Son bakış: ilk açılışta birikmiş satırlar yeni sayılmaz. Filtresiz Geçmiş ya da Panel'deki "Tamam" görüldü sayar.
- Bildirimler: haftalık özet Pazartesi 08:00'den sonra gider. Bugün yapılacaklar yalnız editöre, günün ilk
  çalıştırmasında (sabah uygulama açılışı ya da 09:00) gider. Tüm anahtarlar varsayılan açıktır.
- Derin bağlantılar formu hazırlar, kaydetmez. Çok çekli satır ilk çeki açar; kart ödemesi ekstre borcuyla
  önerilir; gelen formu ilk eksik kanalı seçer.

## Panel düzeni (sahibi Paket A)

Panel sırası şöyledir:

1. Başlık ("Bildirimler" düğmesi dahil).
2. Hata.
3. Güncel Kasa.
4. Hızlı durum kartları.
5. Geçmiş aylara dokunan değişiklikler.
6. Bugün yapılacaklar.
7. Bekleyen giderler.
8. Nakit tahmini.
9. Kanal bakiyeleri.

Diğer paketlerin Panel kartları (ör. Paket E'nin `ContentView` satırı) **Kanal Bakiyeleri kartından sonra**,
kapanış `</VerticalStackLayout>` satırından önce eklenir. Paket A o bölgeye dokunmadığı için birleşme temiz kalır.

## Dosyalar

- **Kasa.Core:** `NakitTahmini.cs`.
- **Kasa.Api:**
  - `Endpoints/PanelEndpoints.cs`
  - `Servisler/TahminServisi.cs`
  - `Data/GecmiseDonuk.cs`
  - `PanelDtos.cs`
  - `Dtos.cs`: `DegisiklikDto.GecmiseDonuk`
  - `Program.cs`: iki satır
- **Kasa.ApiClient:**
  - `IKasaApi.A.cs`
  - `KasaApiClient.A.cs`
  - `Dtos.A.cs`
  - `IKasaApi.cs`: `partial`
  - `Dtos.cs`: `GecmiseDonuk`
- **Kasa.App.Core:**
  - `PanelViewModel.A.cs`
  - `GecmisViewModel.A.cs`
  - `Yapilacaklar.cs`
  - `BildirimPlanlayici.cs`
  - `YerelDepo.cs`
  - `KartLimit.cs`
  - `KrediKartiGorunum.A.cs`
  - `PanelMetin.cs`
  - `GoreceZaman.cs`
  - `DerinBaglanti.cs`, `IslemlerViewModel.A.cs`, `CeklerViewModel.A.cs`, `KrediKartlariViewModel.A.cs`
  - `TekSeferlik.cs`
  - Küçük bağlantı satırları: `PanelViewModel.cs`, `GecmisViewModel.cs`, `Yonlendirme.cs`,
    `KrediKartlariViewModel.cs` (yükleme sonunda derin bağlantı isteği)
- **Kasa.App:**
  - `Views/PanelPage.xaml(.cs)`
  - `Views/BildirimAyarlariView.xaml(.cs)`
  - `Views/BildirimAyarlariPage.xaml(.cs)`
  - `AppShell.A.cs`
  - `PaketAKayit.cs`
  - `Platforms/Windows/*.A.cs`
  - `Views/IslemlerPage.A.cs`, `Views/CeklerPage.A.cs`, `Views/KrediKartlariPage.A.cs` (IQueryAttributable)
  - Tek satırlık bağlantılar: `MauiProgram.cs`, `AppShell.xaml.cs`, `HatirlatmaKontrol.cs`, `WindowsBildirimServisi.cs`, `AyarlarPage.xaml`, `GecmisPage.xaml`, `KrediKartlariPage.xaml` (limit şeridi, vurgu), `IslemlerPage.xaml` (iki `x:Name`)
- **Testler:**
  - `Kasa.Core.Tests/NakitTahminiTests.cs`
  - `Kasa.Api.Tests/PanelPaketATests.cs`
  - `Kasa.ApiClient.Tests/PanelPaketATests.cs`
  - `Kasa.App.Core.Tests/PaketATests.cs`

## Dağıtım

Yalnız API'nin yeni sürümü yayınlanır. Şema, ortam değişkeni ve nginx değişikliği yok; `deploy/README.md`
değişmedi. Eski sunucuya bağlanan yeni uygulama da çalışır: tahmin, özet ve eksik gelen uçları yoksa (404)
ilgili bölümler gizlenir.
