# Emar Kasa — Üretim DB Yeniden Oluşturma Kılavuzu

> **Tarihsel belge — güncel dağıtımda bu sayfadaki silme/yeniden oluşturma adımlarını uygulamayın.**
> 19 Eylül 2026 sağlamlaştırmasıyla `KasaDatabaseInitializer` ve EF migrations eklendi.
> Güncel, veri koruyan süreç: [Veritabanı yükseltme](database-upgrade.md).
> Aşağıdaki içerik yalnız eski `EnsureCreated` sorununun kaydı olarak korunuyor.

## Amaç & Neden Gerekli

Plan 1 (Emar Kasa native backend), EF Core modeline `KrediKartlari` tablosunu ekledi.
Backend `EnsureCreated()` kullanıyor (`Program.cs`, satır 55). `EnsureCreated()` yalnızca
DB dosyası **yoksa** tüm şemayı sıfırdan oluşturur; var olan bir SQLite dosyasına yeni
tablo **eklemez**. Dolayısıyla VPS'teki canlı DB, `KrediKartlari` tablosunu içermiyor.

Native istemci `/api/kredikartlari` ucunu çağırdığında sunucu şu hatayı üretir:

```
SqliteException: SQLite Error 1: 'no such table: KrediKartlari'.
```

Bu kılavuz, üretim DB'sini güncel şemaya taşımak için iki seçenek sunar.

---

## ⚠️ YIKICI UYARI

> **Bu işlem TÜM mevcut veritabanı içeriğini SİLER (Seçenek A).**
> Geri dönüşü yoktur. Önce yedek almak ZORUNLUDUR.
> Seçenek B veri kaybı olmadan çalışır — veri korunacaksa önce onu değerlendir.

---

## Sıralama Ön Koşulu (Kritik)

Bu adım aşağıdaki iki koşul sağlanmadan UYGULANMAZ:

1. **Web SPA emekliye ayrılmış olmalı** — Task 1 (SPA kaldırma) ve Task 2 (Dockerfile güncellemesi) kod olarak merge edilmiş ve yeni imaj VPS'e push edilmiş olmalı. Aksi halde eski SPA imajıyla redeploy yapılır.
2. **Native istemciler ortaklara/kullanıcılara dağıtılmış ya da dağıtıma hazır olmalı** — DB silindikten sonra web SPA çalışmadığı için hiç kimse veriye ulaşamaz; native uygulamalar hazırsa kullanıcılar direkt native app'tan bağlanabilir.

**Ne zaman güvenli?**
Windows `.exe` ortaklara iletildi VE (varsa) Android/iOS sürümler yayınlandı/dağıtıldı → o zaman çalıştır.

---

## 1. Yedekleme (Zorunlu)

VPS'e SSH ile bağlan. Compose dosyası `/opt/kasa/deploy/` altındadır; volume bind-mount `./kasa-data:/data` → DB dosyası `kasa-data/kasa.db`'dir.

```bash
# VPS'te: /opt/kasa/deploy dizininden
cd /opt/kasa/deploy

# 1a) VPS üzerinde yerel yedek al
cp kasa-data/kasa.db kasa-data/kasa.db.$(date +%F).bak

# 1b) Yedeği geliştirme makinesine indir (isteğe bağlı ama önerilir)
#     Geliştirme makinesinden (Git Bash / PowerShell):
scp user@72.61.187.202:/opt/kasa/deploy/kasa-data/kasa.db.$(date +%F).bak ~/Downloads/

# Alternatif: docker cp ile container içindeki dosyayı al
docker cp kasa-app:/data/kasa.db ~/kasa.db.$(date +%F).bak
```

Yedeği doğrula:

```bash
ls -lh /opt/kasa/deploy/kasa-data/kasa.db.*.bak
```

---

## 2. DB Yeniden Oluşturma

### Seçenek A — Basit (Önerilen, iç kullanım)

**Veri silinir. Uygun durum:** veri miktarı azdır veya yeniden girilebilir.

```bash
# VPS'te: /opt/kasa/deploy dizininden
cd /opt/kasa/deploy

# Konteyneri durdur
docker compose stop kasa

# DB dosyasını sil (yedekten sonra)
rm kasa-data/kasa.db

# Konteyneri başlat — EnsureCreated() tüm tabloları (KrediKartlari dahil) sıfırdan kurar
docker compose start kasa

# Log'ları izle: DB init + seed mesajları görünmeli
docker compose logs --tail 30 kasa
```

Başarı göstergesi: log'da hata yok, "Now listening on: http://[::]:8080" satırı var.

### Seçenek B — Veri Korumalı (Elle Migration)

**Veri silinmez. Uygun durum:** mevcut işlem/kanal/cari/ayar verileri korunmalı.**

Container ayaktayken çalışan SQLite dosyasına `sqlite3` (veya `docker exec`) ile bağlanıp
eksik tabloyu elle ekle. `KrediKartiEntity` şeması (`Kasa.Api/Data/Entities.cs`'den):

```sql
CREATE TABLE "KrediKartlari" (
    "Id"               INTEGER NOT NULL CONSTRAINT "PK_KrediKartlari" PRIMARY KEY AUTOINCREMENT,
    "Ad"               TEXT    NOT NULL,
    "KesimTarihi"      TEXT    NOT NULL,
    "SonOdemeTarihi"   TEXT    NOT NULL,
    "Limit"            TEXT    NOT NULL,
    "Borc"             TEXT    NOT NULL
);
```

> **Tür notları:** EF Core SQLite provider, `DateOnly` alanlarını `TEXT` (ISO-8601: `YYYY-MM-DD`),
> `decimal` alanlarını ise `TEXT` olarak saklar. PK için `AUTOINCREMENT` kullanılır.

Uygulama:

```bash
# VPS'te
docker exec -it kasa-app /bin/sh

# Container shell'inde:
apt-get install -y sqlite3   # aspnet imajında yoksa
sqlite3 /data/kasa.db

# sqlite3 prompt'unda:
CREATE TABLE "KrediKartlari" (
    "Id"               INTEGER NOT NULL CONSTRAINT "PK_KrediKartlari" PRIMARY KEY AUTOINCREMENT,
    "Ad"               TEXT    NOT NULL,
    "KesimTarihi"      TEXT    NOT NULL,
    "SonOdemeTarihi"   TEXT    NOT NULL,
    "Limit"            TEXT    NOT NULL,
    "Borc"             TEXT    NOT NULL
);

-- Doğrula:
.tables
-- Beklenen çıktıda "KrediKartlari" görünmeli
.quit
exit
```

> **Not:** `mcr.microsoft.com/dotnet/aspnet:10.0` imajında `sqlite3` CLI genellikle
> yüklü değildir. `apt-get` başarısız olursa container dışına kopyala yedekle:
> `docker cp kasa-app:/data/kasa.db /tmp/kasa_edit.db` → yerel makinede düzenle →
> `docker cp /tmp/kasa_edit.db kasa-app:/data/kasa.db`.

---

## 3. Backend Yeniden Dağıtım

SPA kaldırılmış yeni imajı çek ve servisi yeniden başlat:

```bash
# VPS'te: /opt/kasa/deploy
cd /opt/kasa/deploy

# Güncel kodu VPS'e kopyala (repo remote'u yok — rsync ile):
# Geliştirme makinesinden:
rsync -az --exclude bin --exclude obj --exclude node_modules --exclude deploy/kasa-data \
  "C:/Users/burak/source/repos/Kasa/" user@72.61.187.202:/opt/kasa/

# VPS'te: yeni imajı derle ve servisi güncelle
docker compose up -d --build

# Logları kontrol et
docker compose logs --tail 40 kasa
```

Beklenen log: `Now listening on: http://[::]:8080` — hata satırı yok.

---

## 4. Doğrulama

```bash
# 4a) Health ucu — kimlik gerekmez
curl -s https://kasa.royalmezat.com/health
# Beklenen: {"durum":"ok"}

# 4b) /api/kredikartlari — kimliksiz 401 dönmeli (uç var, tablo erişilebilir)
curl -sI https://kasa.royalmezat.com/api/kredikartlari
# Beklenen: HTTP/2 401

# 4c) Login token al (editör)
TOKEN=$(curl -s -X POST https://kasa.royalmezat.com/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"kullanici":"<EDITOR_KULLANICI>","sifre":"<EDITOR_SIFRE>"}' \
  | grep -o '"token":"[^"]*"' | cut -d'"' -f4)

# 4d) Kredi kartları listesi — boş liste (yeni DB'de kayıt yok)
curl -s https://kasa.royalmezat.com/api/kredikartlari \
  -H "Authorization: Bearer $TOKEN"
# Beklenen: []

# 4e) Native login token akışı: native app'tan giriş yap → /api/kredikartlari → boş liste
# (Seçenek A: eski veriler gitti; Seçenek B: eski veriler + yeni tablo boş)
```

> `<EDITOR_KULLANICI>` ve `<EDITOR_SIFRE>` → VPS'teki `deploy/.env` dosyasındaki
> `KASA_EDITOR_KULLANICI` / `KASA_EDITOR_SIFRE` değerleri.

---

## 5. Sonuç: Memory / Plan Güncelleme

Bu adım tamamlandığında `MEMORY.md`'deki Emar Kasa native girişini şu şekilde güncelle:

> Plan 4 Task 6 ✅ — DB recreate yapıldı (`KrediKartlari` tablosu üretime taşındı);
> SPA-kaldırılmış backend redeploy edildi; `kasa.royalmezat.com` artık yalnızca API.

---

## Hızlı Başvuru

| Bilgi | Değer |
|---|---|
| VPS IP | `72.61.187.202` |
| Compose dizini (VPS) | `/opt/kasa/deploy/` |
| Compose servis adı | `kasa` |
| Container adı | `kasa-app` |
| DB (container içi) | `/data/kasa.db` |
| DB (VPS host yolu) | `/opt/kasa/deploy/kasa-data/kasa.db` |
| Connection string | `Data Source=/data/kasa.db` |
| Prod URL | `https://kasa.royalmezat.com` |
| Redeploy komutu | `docker compose up -d --build` (deploy/ dizininden) |
