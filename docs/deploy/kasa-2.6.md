# Emar Kasa 2.6.0 — sade arayüz, Kasa kontrolü ve ekstre hareket penceresi

Kaynak: 26–27 Kasım 2026 işleri. 2.5.0'dan bu yana EmarKasa#68–#78.

## Canlı yayın durumu

Henüz yayımlanmadı. Etiket, Windows paketi ve sunucu güncellemesinin sonuçları yayından sonra buraya yazılır.

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
