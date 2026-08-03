# Krediler — Tasarım

**Tarih:** 2026-08-03
**Kapsam:** Banka kredileri için ayrı bir "Krediler" sayfası; çekilen tutar genel kasaya
gelir olarak, aylık taksitler seçilen kanala (veya Ortak gidere) otomatik gider olarak yansır.
**Durum:** Onaylı — uygulama planına hazır.

## Amaç

Editör bir banka kredisi çektiğinde: (1) çekilen ana para genel kasaya nakit girişi olarak
yazılsın; (2) aylık taksitler, vade boyunca her ay otomatik olarak kasadan (ve seçilen
kanaldan) düşülsün. Kullanıcı her taksiti elle girmesin — krediyi bir kez tanımlasın,
muhasebe kendiliğinden işlesin.

## Alanlar (kullanıcı elle girer)

- **Ad** — kredi adı (ör. "Ziraat İhtiyaç").
- **CekilenTutar** — çekilen ana para (₺).
- **CekimTarihi** — paranın kasaya girdiği tarih.
- **TaksitSayisi (vade)** — kaç taksit (ör. 12).
- **AylikOdeme** — her taksitin tutarı (₺).
- **OdemeGunu** — ayın kaçında ödeme çekilir (1–31; kısa aylarda ay sonuna sabitlenir).
- **Kanal** — taksitlerin düşeceği kanal **veya** `Ortak` (kanal sayısına bölünüp her
  kanaldan eşit düşer — mevcut Ortak gider davranışının aynısı).

## Muhasebeye yansıma (HesapMotoru DEĞİŞMEZ)

Kredi, saf bir türetici (`KrediTuretici`) ile sentetik `Gelen`/`Islem` kayıtlarına çevrilir.
Bu kayıtlar yalnızca hesap servisinde (`HesapServisi.Yukle`) motora beslenir; DB'deki
`Gelenler`/`Islemler` tablolarına YAZILMAZ, /gelenler /islemler listelerinde GÖRÜNMEZ.

### Çekim → genel kasa (kanala girmez)

Çekim, `Gelen(dönem = CekimTarihi'nin dönemi, Kanal = "__KREDI__", CekilenTutar)` olur.
`"__KREDI__"` gerçek bir kanal olmadığından:
- `toplamGelen`'e (genel kasa) girer ✓
- Hiçbir kanalın haftalık/aylık kârına girmez ✓ (motor kanal gelenini yalnız adı eşleşen
  kanaldan sayar)

Bu, "çekilen tutar genel kasaya yazılsın" isteğini motoru değiştirmeden karşılar.

### Taksitler → seçilen kanal / Ortak

Her taksit `Islem(Tarih, Cari = kredi adı, TutarTl = AylikOdeme, Kanal, Tip = Cari)` olur.
`Tip=Cari` olduğundan normal bir tedarikçi gideri gibi davranır:
- Genel kasadan kendi döneminde düşer ✓
- **Kanal seçiliyse** o kanalın haftalık sonucundan da düşer ✓
- **`Ortak` seçiliyse** haftalıkta genel kasadan düşer, aylık raporda kanal sayısına
  bölünüp her aktif kanaldan eşit düşülür ✓ (mevcut Ortak gider mekanizması)

### Taksit takvimi

- **İlk taksit:** `CekimTarihi`'nden SONRAKİ ilk `OdemeGunu` tarihi. (Örn. çekim 03.08,
  ödeme günü 15 → ilk taksit 15.08; çekim 20.08, ödeme günü 15 → ilk taksit 15.09.)
- **Sonraki taksitler:** aylık artışla, toplam `TaksitSayisi` adet. Kısa aylarda
  `OdemeGunu` ay sonuna sabitlenir (ör. gün=31, Şubat → 28/29). `KartTarih` clamp mantığı
  yeniden kullanılır.
- Gelecekteki taksitler türetilir ama yalnızca dönem takviminin kapsadığı (≈ bugüne kadarki)
  taksitler güncel kasayı etkiler; ileri aylar o ayın raporu sorulunca yansır. Gelecek
  taksitin güncel kasayı erkenden düşürmesi İSTENMEZ.

## Uçlar (API)

- `GET /api/krediler` — herkes (editör + izleyici salt-okunur).
- `POST/PUT/DELETE /api/krediler` — yalnız editör.

Kredi silinince türetilen kayıtlar da otomatik kalkar (DB'de gider satırı tutulmadığı için
temizlik gerekmez).

## Sayfa (MAUI)

- Yeni **Krediler** sekmesi/sayfası; kredi kartları sayfasının desenini izler.
- Liste: Ad, çekilen tutar, kalan taksit, aylık ödeme, kanal.
- Editör formu: yukarıdaki alanlar + kanal seçimi (kanallar + "Ortak").
- İzleyici rolünde salt-okunur.

## Kapsam dışı (bu iş değil)

- Faiz/maliyet hesabı, erken kapama, değişken taksit. Taksitler eşit ve elle girilen
  `AylikOdeme` kadardır.
- Kalan borç bakiyesi takibi (kredi kartındaki gibi borç motoru) — istenmedi.

## Dağıtım notu

Prod `EnsureCreated()` kullanır → yeni `Krediler` tablosunu mevcut DB'ye EKLEMEZ. Deploy'da
elle raw SQL `CREATE TABLE "Krediler"` gerekir (KrediKartlari/KartOdemeler eklenişindeki
gibi, YIKICI OLMADAN). EF konvansiyonu: decimal ve DateOnly → TEXT, int → INTEGER.
