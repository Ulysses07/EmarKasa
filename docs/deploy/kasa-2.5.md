# Emar Kasa 2.5.0 — ekstre, hafıza, ERP12, çekler ve yapay zekâ

Kaynak: 25 Kasım 2026 "KASA DÜZENLEMELER" notları ve Ekim'de birleşen ekran işleri. 2.4.1'den bu yana EmarKasa#52–#65.

## Değişiklikler

- **Ekstre ekranı:** satırın giriş/çıkış yönü ve dayanağı görünür; **Bekleyen / Kapanan** sekmeleri; "Yok say"; seçili satırların etkisi önizlenir. [Tasarım](../specs/2026-11-25-pdf-ekrani.md)
- **Excel yükleme:** banka Excel'i ve örnek dosya; hatalı dosya düzeltilmeden işlenmez, hatalar satır ve hücre adıyla yazar. [Tasarım](../specs/2026-11-25-excel-yukleme.md)
- **İşlem hafızası:** bir kez kaydedilen ya da yok sayılan hareket sonraki belgede doldurulur; hazırlar tek düğmeyle işaretlenir, kayıt yine onayla. [Tasarım](../specs/2026-11-25-islem-hafizasi.md)
- **Kişisel ekstre kuralları** (Ekim) ve işlem listesinde tarih dizini.
- **ERP12 cari hareketleri:** Excel ile aktarılır, kasadaki kayda bağlanır, kasayı tek başına değiştirmez; yeni **Cariler** ekranı. [Tasarım](../specs/2026-11-25-erp12-aktarimi.md)
- **Çekler:** verilen çekler önde; toplu giriş. [Tasarım](../specs/2026-11-25-cekler-verilen-ve-toplu.md)
- **Yapay zekâ önerileri:** isteğe bağlı, **kapalı gelir**; anahtar girilmeden dış bağlantı kurulmaz. [Kılavuz](yapay-zeka.md)
- .NET 10.0.15 ve test araçları güncellemesi; temel imaj özetleri güncel.

## Veritabanı

Altı yeni migration, açılışta kendiliğinden ve **göç öncesi otomatik yedekle** uygulanır. Hepsi yeni tablo ya da dizin ekler; mevcut satırlara ve raporlara dokunmaz:
`20261009000100_IslemTarihIndeksi`, `20261010000100_EkstreKurallari`, `20261125000100_EkstreSatirDurumu`, `20261125000200_EkstreHafizasi`,
`20261125000300_ErpHareketleri`, `20261125000400_YapayZeka`. Migration'ların `Down` adımı yoktur: geri dönüş, yükseltme öncesi yedeğin 2.4.1 imajıyla geri açılmasıdır.

## İstemciler

- `minimumIstemci` **2.4.1** kalır: 2.4.1 Windows uygulaması okumaya ve yazmaya devam eder; yeni ekranlar (Cariler, Excel, hafıza, yapay zekâ) için 2.5.0 Windows uygulaması gerekir.
- Windows 2.5.0 paketi [Windows desktop release](windows-exe.md) iş akışıyla üretilir ve kararlı GitHub sürümü olarak yayımlanır; kurulu uygulama güncellemeyi oradan görür.
- iOS bu yayında güncellenmez (TestFlight imzası gerekir).

## Yayın sırası

1. `v2.5.0` etiketi; Windows paketinin üretilmesi ve kararlı sürüm olarak yayımlanması.
2. Sunucuda bağlamaların ve son yayın manifestinin doğrulanması; veritabanı, belge deposu ve güvenlik günlüğünün bütünlüğü sınanmış kopyası (`/opt/kasa/releases/<tarih>-2.5.0/pre-upgrade`).
3. Canlı verinin ayrı kopyasında 2.5.0 imajıyla açılış: migration'lar, `/health`, `/api/surum`, tablo kayıt sayıları.
4. [Dağıtım rehberindeki](../../deploy/README.md) "Güncelleme" 2–8 (geri dönüş etiketi `kasa:geri-donus-2.5.0`), sonra aynı doğrulamalar canlıda.

## Geri dönüş

Yeni imaj canlı veritabanına bağlanmadan bir sorun çıkarsa rehberdeki "Geri dönüş" uygulanır. Migration'lar uygulandıktan sonra 2.4.1 imajı yeni şema üzerinde
**çalıştırılmaz**; konteyner durdurulur, yükseltme öncesi kopya (ya da uygulamanın göç öncesi yedeği) [veritabanı kılavuzuna](database-upgrade.md) göre geri açılır ve
`kasa:geri-donus-2.5.0` imajıyla başlatılır. Bu arada girilen kayıtlar kaybolacağından geri dönüş kararı kayıt sayıları karşılaştırılarak verilir.
