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
- Satırlardaki düğmeler **yalnız ilgili sayfaya götürür**. Tek dokunuşla tahsil/ödeme bu pakette yok (Paket D).

## Özellikler

### 20 · Hızlı durum kartları (Panel)

Hero kartının altında dört kutu. Masaüstünde 4 sütun, dar ekranda 2×2. Kutuya dokunmak ilgili sayfayı açar.

| Kutu | İçerik | Kaynak |
|---|---|---|
| Kart ekstreleri | Ödenmemiş ekstre toplamı; en yakın son ödeme ("… · 30 Eylül", geçtiyse kırmızı); limit uyarısı | `GET /api/kredikartlari` |
| Çekler | "Tahsil edilecek X ₺ · N çek", "Ödenecek Y ₺ · M çek", vadesi geçen | `GET /api/cekler/ozet` |
| Son kasa sayımı | Tarih ("20 Eylül (4 gün önce)") ve fark (renkli) | `GET /api/kasasayimlari` |
| Defter | "Defter en son 2 saat önce güncellendi" + son bakıştan beri değişiklik (özellik 23) | `GET /api/gecmis/ozet` |

Bir kaynak okunamazsa o kutu "… bilgisi alınamadı" der. Ana panel (kasa, kanallar, bekleyen giderler)
bu okumalardan etkilenmez.

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
| Ufuk içinde vadesi gelecek tekrarlayan gider | vade | − |
| İleri tarihli gider işlemi (Cari / sabit gider) | tarihi | − |
| Karta bağlı olmayan eski K.K | sonraki ayın son döneminin ilk günü | − |

- Vadesi/son ödemesi geçmiş ama hâlâ bekleyen kalem **yarına** yazılır ve "gecikmiş" işaretlenir.
- Gelecek gelenler (satış) bilinmediği için tahmine **girmez**.
- **En düşük gün** ve kasası öne çıkarılır ("En düşük: 14 Kasım, −12.500,00 ₺"). Eksi ise kırmızı.
- **Riskli çek:** tahmindeki her çek "Hesaptan çıkar / Hesaba kat" ile tahmin dışında bırakılabilir.
  Seçim yalnız o bilgisayarda saklanır (`tahmin.haric`) ve çekin kaydını değiştirmez. Hariç çekler yanıtta
  `haricKalemler` içinde döner.
- Seçilen ufuk (30/60/90) cihazda hatırlanır (`tahmin.gun`).
- Listede hareket olan günler gösterilir. En düşük gün hareketsizse (ve bugün değilse) o da gösterilir.

### 31 · Bugün yapılacaklar (yalnız editör)

Panel'de bir kart. Her satırın bir düğmesi vardır ve bu düğme yalnız ilgili sayfayı açar:

| Satır | Koşul | Hedef |
|---|---|---|
| N tekrarlayan gider onay bekliyor (tek satır, toplam) | bekleyen liste boş değil | Panel'deki bekleyen kartına kaydırır |
| Vadesi geçen N çek | portföyde, vade < bugün | Çekler |
| Bugün vadesi gelen N çek | vade = bugün | Çekler |
| 3 gün içinde vadesi gelecek N çek | bugün < vade ≤ bugün+3 | Çekler |
| Kart · son ödeme bugün/yarın/… | ödenmemiş ekstre (`KartHatirlatici.OdemeBekliyor`) | Kredi Kartları |
| Gelen girilmedi: 14–20 Eylül | son 14 günde biten dönemde aktif kanalın geleni yok | İşlemler |
| Kasa sayımı N gündür yapılmadı | son sayım 7 günden eski ya da hiç yok | Kasa Sayımı |

Sabah bildirimi: günde en fazla bir kez, yalnız 09:00 arka plan hatırlatıcısında ve editör oturumunda
gönderilir (özellik 09).

### 08 · Kart limit uyarısı (%80)

`KartLimit.UyariVar`: limit > 0 ve güncel borç ≥ limitin %80'i. Metin "Limitin %85'i kullanıldı" ya da
"Limit aşıldı (%112)" olur (yüzde aşağı yuvarlanır). Uyarı iki yerde görünür:

- Kredi Kartları sayfasında her kartın altında kırmızı şerit.
- Panel'deki kart kutusunda: "Limit uyarısı: Bonus %85 · Axess limit aşıldı".

### 23 · Son bakıştan beri / geçmişe dönük

- **Geçmişe dönük kuralı** (`Kasa.Api/Data/GecmiseDonuk.cs`) geçmiş satırının kendisinden okunur, ayrı sütun
  tutulmaz. Bu yüzden eski satırlar için de geçerlidir.
  - Tarihli para kayıtları (işlem, gelen, kart ödemesi, çek) için: kaydın eski ya da yeni hâlinin tarihi,
    değişikliğin ayından (Türkiye saati) önceki bir aydaysa satır geçmişe dönüktür.
  - Güncellemede ayrıca paraya dokunan bir alan değişmiş olmalı.
  - Çek yalnız tahsil edildi/ödendi durumundayken sayılır ve tarihi işlem tarihidir.
  - Açılış devri değişiklikleri her zaman geçmişe dönüktür: kasa açılış devri, takip başlangıcı, kanal açılış devri.
  - Kasa sayımı, kart tanımı ve ad değişiklikleri geçmişe dönük sayılmaz.
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

### 09 · Bildirimler

Windows bildirimleri (e-posta yok). İki yerde çalışır ve ikisi aynı yerel depoyu paylaştığından aynı
bildirim iki kez gitmez:

- Uygulamada giriş yapılınca (menü açılışında bir kez).
- Günlük 09:00 arka plan hatırlatıcısında (`--hatirlatma-kontrol`; kaçırılırsa ilk fırsatta).

| Bildirim | Ne zaman | Tıklanınca |
|---|---|---|
| Haftalık özet: "Geçen hafta kasa sonucu +X, güncel kasa Y" | Pazartesi 08:00'den sonraki ilk çalıştırma, haftada bir | Haftalık |
| Vadesi geçen N çek (ayrı bildirim) | haftalık özetle birlikte | Çekler |
| Geçmişe dönük düzeltme | son bildirimden sonra yeni geçmişe dönük satır varsa; günde en fazla bir kez | Geçmiş |
| Bugün yapılacaklar (N) | yalnız 09:00 hatırlatıcısı, yalnız editör, günde bir kez, liste boş değilse | Panel |
| Kredi kartı hatırlatmaları (mevcut) | değişmedi; yalnız anahtara bağlandı | Kredi Kartları |

- Ay sonunda ikiye bölünen haftanın iki dönemi toplanır.
- Hafta, okumaların hepsi başarılı olunca "gönderildi" işaretlenir. Hata olursa sonraki çalıştırmada yeniden denenir.
- Geçmişe dönük bildiriminin ilk çalıştırması yalnız başlangıç noktasını kaydeder.
- **Ayarlar → Bildirimler** ayrı bir bölümdür. Her tür ayrı ayrı kapatılabilir.
  - Seçimler yalnız o bilgisayarda saklanır ve varsayılan açıktır.
  - Ayarlar yalnız editörde göründüğü için aynı görünüm Panel'deki **Bildirimler** düğmesiyle de açılır
    (izleyici de kullanabilir).

## Sunucu uçları (yeni, hepsi kimlik doğrulamalı, yalnız okur)

Tüm uçlar `Kasa.Api/Endpoints/PanelEndpoints.cs` dosyasındadır ve `Program.cs`'e tek satırla bağlanır
(`api.MapPanelUclari();`). Yazma ucu eklenmedi. Editör ve izleyici ikisi de okuyabilir.

| Uç | Yanıt | Hata |
|---|---|---|
| `GET /api/rapor/tahmin?gun=30&haric=3,7` | `NakitTahminSonucu`: `bugun`, `gun`, `baslangicKasa`, `gunler[]` (`tarih`, `giris`, `cikis`, `kasa`, `kalemler[]`), `enDusukTarih`, `enDusukKasa`, `sonKasa`, `toplamGiris`, `toplamCikis`, `haricKalemler[]` | 400: `gun` 1–366 dışında; `haric` virgülle ayrılmış negatif olmayan sayılar değil ya da 1000'den fazla |
| `GET /api/gelenler/eksik` | `[{ donemStart, donemEnd, kanallar[] }]`: son 14 günde biten dönemler, geleni girilmemiş aktif kanallar (0 girilmiş sayılır) | – |
| `GET /api/gecmis/ozet?sonId=123` | `{ sonId, sonZamanUtc, toplam, gecmiseDonuk, gecmiseDonukSatirlar[≤5] }`. `sonId` verilmezse sayılar 0 olur (başlangıç noktası) | 400: `sonId` negatif |

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
- Bildirimler: haftalık özet Pazartesi 08:00'den sonra gider. Bugün yapılacaklar yalnız sabah ve yalnız editöre gider. Tüm anahtarlar varsayılan açıktır.

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
  - Küçük bağlantı satırları: `PanelViewModel.cs`, `GecmisViewModel.cs`, `Yonlendirme.cs`
- **Kasa.App:**
  - `Views/PanelPage.xaml(.cs)`
  - `Views/BildirimAyarlariView.xaml(.cs)`
  - `Views/BildirimAyarlariPage.xaml(.cs)`
  - `AppShell.A.cs`
  - `PaketAKayit.cs`
  - `Platforms/Windows/*.A.cs`
  - Tek satırlık bağlantılar: `MauiProgram.cs`, `AppShell.xaml.cs`, `HatirlatmaKontrol.cs`, `WindowsBildirimServisi.cs`, `AyarlarPage.xaml`, `GecmisPage.xaml`, `KrediKartlariPage.xaml`
- **Testler:**
  - `Kasa.Core.Tests/NakitTahminiTests.cs`
  - `Kasa.Api.Tests/PanelPaketATests.cs`
  - `Kasa.ApiClient.Tests/PanelPaketATests.cs`
  - `Kasa.App.Core.Tests/PaketATests.cs`

## Dağıtım

Yalnız API'nin yeni sürümü yayınlanır. Şema, ortam değişkeni ve nginx değişikliği yok; `deploy/README.md`
değişmedi. Eski sunucuya bağlanan yeni uygulama da çalışır: tahmin, özet ve eksik gelen uçları yoksa (404)
ilgili bölümler gizlenir.
