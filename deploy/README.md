# Kasa Defteri — VPS Dağıtım

Aynı OrderDeck VPS'inde, `orderdeck-caddy` arkasında `kasa.emarglobal.com`.
Windows uygulaması bu adrese bağlanır (`Kasa.App/MauiProgram.cs`).

## İlk kurulum
1. DNS: `kasa.emarglobal.com` A kaydı → VPS IP'si.
2. Kasa reposunu VPS'e kopyala (repo remote'u yok → rsync/scp):
   `rsync -az --exclude bin --exclude obj --exclude node_modules --exclude deploy/kasa-data \
     ./ user@VPS:/opt/kasa/`
3. VPS'te env doldur:
   `cd /opt/kasa/deploy && cp .env.example .env && nano .env`
   (KASA_JWT_KEY = `openssl rand -base64 48`, editör kullanıcı/şifre).
   KASA_JWT_KEY boş ya da 32 bayttan kısaysa uygulama **açılmaz** (log'da sebebi yazar).
4. OrderDeck ağının ayakta olduğunu doğrula:
   `docker network ls | grep orderdeck_web`
   (yoksa önce OrderDeck compose `up` edilmeli — Caddy zaten çalışıyor olmalı)
5. Kasa'yı derle + başlat:
   `cd /opt/kasa/deploy && KASA_SURUM=$(cat SURUM 2>/dev/null) docker compose up -d --build`
6. Caddyfile'a (LiveDeck reposu → VPS'te `/opt/orderdeck/Caddyfile`) blok ekle ve reload
   (eski `kasa.royalmezat.com` / `kasa.orderdeckapp.com` blokları kaldırılabilir):
   ```
   kasa.emarglobal.com {
       import security_headers
       encode gzip zstd
       reverse_proxy kasa-app:8080
   }
   ```
   `docker exec orderdeck-caddy caddy reload --config /etc/caddy/Caddyfile`
7. Doğrula: `curl -s https://kasa.emarglobal.com/health` → `{"durum":"ok","surum":"..."}`

## Güncelleme (yeni sürüm)
1. Geliştirme makinesinde sürümü işaretle: `git rev-parse --short HEAD > deploy/SURUM`
2. `rsync ... /opt/kasa/` (yukarıdaki komut; `deploy/kasa-data` hariç tutulur)
3. `cd /opt/kasa/deploy && KASA_SURUM=$(cat SURUM) docker compose up -d --build`
4. `curl -s https://kasa.emarglobal.com/health` ile `surum`'un yeni commit olduğunu doğrula.

Şema: yeni tablo/sütunlar açılışta otomatik eklenir (`SemaGuncelleyici`); DB'yi silmek
veya elle SQL yazmak gerekmez. Log'da `Şema güncellendi: ...` satırları görünür.

## Yedek
- **Otomatik:** Uygulama her gün `deploy/kasa-data/yedek/kasa-YYYY-AA-GG.db` yedeğini alır
  (SQLite `VACUUM INTO`, tutarlı kopya), son 30 günü tutar. Log: `Yedek alındı: ...`
- **Sunucu dışı kopya (önerilir):** VPS kaybına karşı yedek klasörünü başka bir makineye
  çekin. Örnek (kendi bilgisayarınızda ya da başka bir sunucuda, günlük cron):
  `rsync -az user@VPS:/opt/kasa/deploy/kasa-data/yedek/ ~/kasa-yedek/`
- **Geri yükleme:** `docker compose stop kasa`, istenen yedeği `kasa-data/kasa.db` olarak
  kopyala (`kasa.db-wal`/`-shm` varsa sil), `docker compose start kasa`.

## Saat dilimi
Konteyner `TZ=Europe/Istanbul` ile çalışır; uygulama "bugün"ü ayrıca Türkiye saatine göre hesaplar.
