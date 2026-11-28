# Emar Kasa 2.6.0 — sade arayüz, Kasa kontrolü ve ekstre hareket penceresi

Kaynak: 26–27 Kasım 2026 işleri. 2.5.0'dan bu yana EmarKasa#68–#78.

## Canlı yayın durumu (28 Kasım 2026)

`v2.6.0` etiketi `ccf288318404fa9346fa583ede8bd98600f94b6f` commit'ini gösterir (#79'un birleştirme commit'i; kaynak arşivi SHA-256
`d4b5a473f81c5950f1b4f92cc37c38b99c2fb5e52f42da7f94a9c813d9bd5363`). Canlı `/health` başarılıdır; `/api/surum` yanıtında `surum` **2.6.0**,
`minimumIstemci` **2.4.1**'dir. Çalışan imaj `sha256:19d8f161a24e9975cc7f5bdd366cd784acfe5fc33e3c051b4ba645f4cd55944b`; yayın manifesti
`/opt/kasa/releases/20261128-2.6.0/kasa-2.6.0-published.json` içindedir.

- **Ön denetim:** bağlamalar ve çalışan imaj 2.5.0 manifestiyle aynı, veritabanı bütünlüğü sağlam (31 migration, 57 tablo), diskte 46 GB boş.
- **Prova:** canlı verinin ayrı kopyasında, dışa kapalı iç ağda 2.6.0 açıldı; migration 31 → 31, hiçbir tablonun kayıt sayısı değişmedi, günlükte hata yok.
- **Canlı:** eski konteyner durduruldu, veri dizini ve güvenlik günlüğü dosya özetleriyle `/opt/kasa/releases/20261128-2.6.0/pre-upgrade` altına
  kopyalandı; yeni imajla başlatıldı (kesinti 11 saniye). Bağlamalar, `.env` ve yapay zekâ ayarı (açık) değişmedi; kayıt tabloları aynı,
  güvenlik günlüğünün kontrol dosyası tutarlı, günlükte hata yok. Migration olmadığı için yeni göç öncesi yedek oluşmadı.
- **Geri dönüş imajı:** 2.5.0 imajı `kasa:geri-donus-2.6.0` etiketiyle saklandı (`sha256:d25c9bd4…acd`); `imaj-geri-donus-etiketi.txt` ve
  `compose-onceki.yml` yayın dizinindedir.
- **Windows:** [v2.6.0-windows](https://github.com/Ulysses07/EmarKasa/releases/tag/v2.6.0-windows) kararlı sürüm olarak yayımlandı (aynı commit;
  iş akışında açılış, kurulum ve 2.6.0 → 2.6.1 güncelleme denemeleri geçti; imzasız). SHA-256: Setup
  `c2c2e39b8ffaeb5f9afefb5d096beaee779dc00945c0b7443e284760568475f6`, full paket `e7b77f16f85bc57f8e25b1ebea72936f2d7bdeeaa7dd90c13ef89f6de2c10f77`,
  ZIP `e4e62447467e31e8c1e25f6eaf02d09862dac9a2fc76e5a3bea451cedbd7341e`. Kurulu uygulama güncellemeyi buradan görür.
- **Doğrulanmayan:** gerçek kullanıcı girişi ve kayıt akışı (canlı şifreyle giriş yapılmadı), iPhone. Sunucu dışı yedek hâlâ yapılandırılmadı.

## Değişiklikler

- **Sade arayüz** (#71–#76): sayfada en çok bir yeşil ana düğme; tek başlık satırı ve Yenile; açıklama paragrafları "Ayrıntılar"ın arkasında;
  tutarlar sütunda, gider eksi ve kırmızı; tek görsel sistem; formlar istenince açılır; Windows kısayolları F5, Ctrl+N, Ctrl+S, Ctrl+F.
  [Tasarım](../specs/2026-11-26-slop-temizligi.md)
- **Kasa kontrolü sayfası** (#78): Kasalar yalnız özet; gerçek bakiye karşılaştırması, karşılaştırma geçmişi, alt limit uyarıları ve hareket
  dökümü yeni sayfada. Kasalar'da alt limitin altındaki kanalın satırında kırmızı uyarı. Sayfa izleyiciye de açık; karşılaştırma yalnız editörde.
- **Ekstre hareket penceresi** (#70) ve **kayıt özeti** (#77): ekstre satırları ayrı pencerede tablo olarak; kayıt özeti tablo düzeninde,
  yapay zekâ etiketi yalnız farkta. Satıra ikinci dokunuş düzenleyiciyi kapatır (#68).
- **Ekstre okuma ve yapay zekâ** (#69, #70; sunucu): bölünmüş açıklama satırları birleşir; yapay zekâ TL sayılan satırları da sorar ve
  hafızanın ölçüsüne uyan önerileri kendiliğinden hazır işaretler (kayıt yine onayla). Yeni alanlar isteğe bağlıdır: 2.5.0 istemcisi çalışmaya devam eder.

## Veritabanı

Yeni migration yok; şema 2.5.0 ile aynıdır.

## İstemciler

- `minimumIstemci` **2.4.1** kalır. Yeni görünüm için Windows 2.6.0 uygulaması gerekir.
- Windows 2.6.0 paketi [Windows desktop release](windows-exe.md) iş akışıyla (`v2.6.0-windows` taslağı) üretilir ve kararlı GitHub sürümü olarak
  yayımlanır; kurulu uygulama güncellemeyi oradan görür.
- iOS bu yayında güncellenmez (TestFlight imzası gerekir).

## Yayın sırası

1. `v2.6.0` etiketi; Windows paketinin üretilmesi ve kararlı sürüm olarak yayımlanması.
2. Sunucuda bağlamaların ve son yayın manifestinin (`/opt/kasa/releases/20261126-2.5.0/kasa-2.5.0-published.json`) doğrulanması.
3. Canlı verinin ayrı kopyasında 2.6.0 imajıyla açılış: `/health`, `/api/surum`, migration listesi ve tablo kayıt sayıları değişmemeli.
4. [Dağıtım rehberindeki](../../deploy/README.md) "Güncelleme" 2–8 (geri dönüş etiketi `kasa:geri-donus-2.6.0`), yükseltme öncesi anlık görüntü
   (`/opt/kasa/releases/<tarih>-2.6.0/pre-upgrade`), sonra aynı doğrulamalar canlıda. Yapay zekâ ayarı (`deploy/.env`) olduğu gibi kalır.

## Geri dönüş

Şema değişmediği için 2.5.0 imajı aynı veritabanıyla açılabilir: rehberdeki "Geri dönüş" `kasa:geri-donus-2.6.0` etiketiyle uygulanır.
Yeni sürümde girilen kayıtlar aynı veritabanında kalır.
