# Kasa Defteri — VPS Dağıtım

Aynı OrderDeck VPS'inde, `orderdeck-caddy` arkasında `kasa.emarglobal.com`.
Windows uygulaması bu adrese bağlanır (`Kasa.App/MauiProgram.cs`).

- Kaynak: GitHub'daki `github.com/Ulysses07/EmarKasa` reposu (özel). VPS'e kod **rsync** ile
  gönderilir. VPS'te depo için bir deploy key varsa `git clone`/`git pull` da kullanılabilir.
  Hangisi seçilirse seçilsin `deploy/.env` ve `deploy/kasa-data/` elle korunmalıdır.
- Compose dizini: `/opt/kasa/deploy` · Servis: `kasa` · Konteyner: `kasa-app`
- Veri: host'ta `/opt/kasa/deploy/kasa-data/`, konteynerde `/data/`
  (`kasa.db` ile birlikte `kasa.db-wal` ve `kasa.db-shm`, yedekler `yedek/` altında).

## Kodu VPS'e gönderme (rsync)

Geliştirme makinesinde, repo kökünden çalıştırın. Önce `git status` temiz olmalı; yoksa
commit edilmemiş değişiklikler de canlıya gider.

```bash
git rev-parse --short HEAD > deploy/SURUM
rsync -az --delete \
  --exclude /.git/ --exclude bin/ --exclude obj/ --exclude node_modules/ \
  --exclude '*.db' --exclude '*.db-*' \
  --exclude /deploy/.env --exclude /deploy/kasa-data/ \
  ./ user@VPS:/opt/kasa/
```

- `--delete`, repodan silinen dosyaların sunucuda kalıp derlemeye girmesini önler.
- **`/deploy/.env` ve `/deploy/kasa-data/` exclude'ları zorunludur.** Hariç tutulan yollar
  `--delete` ile silinmez. Bu iki satır olmadan komut sunucudaki sırları ve **canlı
  veritabanını ve tüm yedekleri siler.** Komutu elle yazmayın, buradan kopyalayın.
  Emin değilseniz önce `-n` (dry-run) ile çalıştırıp silinecekleri görün:
  `rsync -azni --delete ... | grep deleting`.
- Komut kaynak olarak `./` alır, sonundaki `/` gereklidir. Aksi halde `/opt/kasa/EmarKasa/` oluşur.

## İlk kurulum
1. DNS: `kasa.emarglobal.com` A kaydı → VPS IP'si.
2. Kodu gönderin (yukarıdaki rsync).
3. VPS'te env dosyasını doldurun:
   `cd /opt/kasa/deploy && cp .env.example .env && nano .env`
   - `KASA_JWT_KEY` = `openssl rand -base64 48`. Boşsa ya da 32 bayttan kısaysa uygulama
     **açılmaz** ve sebebini log'a yazar.
   - `KASA_EDITOR_KULLANICI` / `KASA_EDITOR_SIFRE`
   - `KASA_GUVENILIR_AGLAR`: aşağıdaki "Güvenilir proxy ağı" bölümüne bakın.
4. OrderDeck ağının ayakta olduğunu doğrulayın: `docker network ls | grep orderdeck_web`.
   Ağ yoksa önce OrderDeck compose'u `up` edilmeli; Caddy zaten çalışıyor olmalı.
5. Veri klasörünü uygulama kullanıcısına verin (aşağıdaki "Root olmayan kullanıcı" bölümüne bakın):
   `mkdir -p kasa-data && sudo chown -R 1654:1654 kasa-data`
6. Derleyin ve başlatın:
   `cd /opt/kasa/deploy && KASA_SURUM=$(cat SURUM 2>/dev/null) docker compose up -d --build`
7. Caddyfile'a blok ekleyin ve Caddy'yi yeniden yükleyin. Caddyfile LiveDeck reposunda,
   VPS'te `/opt/orderdeck/Caddyfile` yolundadır. Eski `kasa.royalmezat.com` /
   `kasa.orderdeckapp.com` blokları kaldırılabilir.
   ```
   kasa.emarglobal.com {
       import security_headers
       encode gzip zstd
       reverse_proxy kasa-app:8080
   }
   ```
   `docker exec orderdeck-caddy caddy reload --config /etc/caddy/Caddyfile`
8. Doğrulayın: `curl -s https://kasa.emarglobal.com/health`. Aşağıdaki "Sağlık kontrolü" bölümüne bakın.

## Güncelleme (yeni sürüm)

1. Geliştirme makinesinde kodu gönderin (yukarıdaki rsync bölümüne bakın).
   Komut `deploy/SURUM`'u da yazar.
2. VPS'te yeni imajı **eski konteyner çalışırken** derleyin:
   `cd /opt/kasa/deploy && KASA_SURUM=$(cat SURUM) docker compose build`
3. **Elle yedek alın.** Konteyneri durdurup DB dosyalarını kopyalayın. Kesinti birkaç saniye sürer:
   ```bash
   docker compose stop kasa
   Y=~/kasa-elle-yedek/$(date +%F-%H%M) && mkdir -p "$Y"
   sudo cp -a kasa-data/kasa.db* "$Y"/ && ls -l "$Y"
   ```
   Neden böyle yapılıyor:
   - İmajda `sqlite3` CLI yok. Canlı bir WAL veritabanında `cp kasa.db` son yazmaları
     kaçırır; denemede son 60 kaydın hiçbiri kopyada değildi. Konteyner durunca SQLite
     WAL'ı ana dosyaya işler. Böylece durdurulmuş dosyanın kopyası tutarlı olur.
     `-wal`/`-shm` varsa aynı anda kopyalanır.
   - Uygulama şema değişikliğinden önce ayrıca otomatik bir yedek alır:
     `kasa-data/yedek/kasa-once-<zaman>.db`. Bu ikinci bir emniyettir, elle yedeğin
     yerini tutmaz. Güncelleme şema değiştirmiyorsa bu yedek oluşmaz.
4. Başlatın: `KASA_SURUM=$(cat SURUM) docker compose up -d`
5. Log'u izleyin: `docker compose logs --tail 50 -f kasa`. Hata olmamalı. Şema değiştiyse
   `Şema güncellendi: ...` satırları görünür.
6. `curl -s https://kasa.emarglobal.com/health` ile `surum`'un yeni commit olduğunu doğrulayın.

**Şema:** Yeni tablo, sütun ve index'ler açılışta otomatik eklenir (`SemaGuncelleyici`).
DB'yi silmeye ya da elle SQL yazmaya gerek yoktur. Eski `docs/deploy/kasa-db-recreate.md`
kılavuzu artık geçersizdir.

### Bu sürüme ilk geçişte bir kez yapılacaklar

Bu sürüm konteyneri root yerine `app` kullanıcısıyla (UID 1654) çalıştırır. Açılışta
tekil index'leri ve tek seferlik çift kayıt temizliğini de uygular. Adımlar sırasıyla:

1. `deploy/.env`'e `KASA_GUVENILIR_AGLAR=` satırını ekleyip doldurun (aşağıdaki "Güvenilir proxy ağı" bölümü).
2. Güncelleme adım 2–3: imajı derleyin, konteyneri durdurun, elle yedek alın.
3. **Sahipliği değiştirin** (konteyner durmuşken):
   `sudo chown -R 1654:1654 /opt/kasa/deploy/kasa-data`
   Bu adım atlanırsa eski root konteynerin oluşturduğu `kasa.db` ve `yedek/` yazılamaz.
   Uygulama açılamaz ya da yedek alamaz.
4. `docker compose up -d`, sonra log'u izleyin. Çift kayıt temizliği ve index oluşturma
   ilk açılışta bir kez çalışır. Log'da hata olmamalı. `/health` 200 dönmeli.

## Root olmayan kullanıcı (UID 1654)

Microsoft'un .NET 8 ve sonrası imajlarında hazır bir `app` kullanıcısı vardır
(`APP_UID=1654`). Dockerfile `USER $APP_UID` ile bu kullanıcıya geçer. `/data` host'tan
bind-mount edildiği için host'taki `kasa-data/` klasörü bu UID'ye ait olmalıdır.
Doğrulamak için:

```bash
docker run --rm --entrypoint id kasa:latest      # uid=1654(app) gid=1654(app)
ls -ln /opt/kasa/deploy/kasa-data                 # sahip 1654 olmalı
```

Host'ta kök yetkisiyle `kasa-data/` içine dosya koyarsanız (ör. geri yükleme), sonra
yeniden `chown 1654:1654` yapın.

## Güvenilir proxy ağı (`KASA_GUVENILIR_AGLAR`)

API, istemci IP'sini `X-Forwarded-For` başlığından yalnız bu CIDR'lerden gelen
bağlantılarda okur. Giriş hız sınırı IP başınadır ve bu IP'ye dayanır. Başlık herkesten
kabul edilirse limit atlatılabilir. Değer `Kasa__GuvenilirAglar` olarak konteynere geçer.

- **Caddy kurulumu (`docker-compose.yml`):** `orderdeck_web` ağının subnet'i.
  ```bash
  docker network inspect orderdeck_web -f '{{range .IPAM.Config}}{{.Subnet}}{{end}}'
  # örn. 172.18.0.0/16  →  deploy/.env: KASA_GUVENILIR_AGLAR=172.18.0.0/16
  ```
- **nginx kurulumu (`docker-compose.nginx.yml`):** Host nginx'i `127.0.0.1:8080`'e
  bağlanır. Konteyner bu bağlantıyı kendi compose ağının gateway'inden görür. O ağın
  subnet'ini yazın (proje adı `deploy` ise ağ `deploy_default` olur):
  `docker network inspect deploy_default -f '{{range .IPAM.Config}}{{.Subnet}}{{end}}'`

Ek sınır: `Kasa__GirisGlobalLimiti` tüm IP'lerin toplamı için dakikalık giriş denemesi
sayısıdır. Varsayılan 60'tır. Değiştirmek için compose'daki yorum satırını açın.
Çıkış (logout) yapılan oturumun token'ı sunucuda geçersiz kılınır.

## Sağlık kontrolü

- `GET /health` kimlik istemez. Veritabanına yoklama (ping) yapar. Yanıtta sürüm (`surum`)
  ve son yedeğin yaşı bulunur. DB'ye ulaşılamazsa **503** döner.
- İmajdaki `HEALTHCHECK` her 60 saniyede bir konteyner içinden `/health`'i çağırır.
  İmajda curl/wget olmadığından bash'in `/dev/tcp`'si kullanılır. İlk 90 saniye tolere
  edilir, 3 ardışık hata olursa konteyner "unhealthy" görünür:
  `docker ps` (STATUS sütunu) ya da
  `docker inspect -f '{{json .State.Health}}' kasa-app`.
- **Docker, unhealthy konteyneri kendiliğinden yeniden başlatmaz.** `restart: unless-stopped`
  yalnız süreç çökerse devreye girer. Dışarıdan bir izleyici önerilir (ör. UptimeRobot).
  İzleyici `https://kasa.emarglobal.com/health` 200 dönmezse ve son yedek 1 günden
  eskiyse uyarı göndermeli.

## Yedek

- **Otomatik günlük yedek:** Uygulama her gün `kasa-data/yedek/kasa-YYYY-AA-GG.db` yedeğini
  alır. Yöntem SQLite `VACUUM INTO`'dur ve tutarlı bir kopya üretir. Yedek önce geçici bir
  dosyaya yazılır, tamamlanınca yeniden adlandırılır. Böylece disk dolarsa önceki yedek
  bozulmaz. Son 30 gün tutulur. Log satırı: `Yedek alındı: ...`
- **Saklama kuralı:** Otomatik silme yalnız `kasa-YYYY-AA-GG.db` adlı dosyalara uygulanır.
  Şema öncesi yedekler (`kasa-once-*.db`) ve elle konan dosyalar **silinmez**. Bunları
  ara sıra elle temizleyin.
- **Disk kullanımı:** Yaklaşık 30 × DB boyutu, üstüne `kasa-once-*` dosyaları. Hepsi
  **canlı DB ile aynı diskte** durur. Disk bozulursa ya da VPS kaybedilirse yedekler de
  gider. Kontrol için: `du -sh /opt/kasa/deploy/kasa-data/yedek; df -h /opt`
- **Sunucu dışı kopya (şiddetle önerilir):** Yedek klasörünü düzenli olarak başka bir
  makineye çekin. Kendi bilgisayarınızda ya da başka bir sunucuda günlük cron ile:
  `rsync -az user@VPS:/opt/kasa/deploy/kasa-data/yedek/ ~/kasa-yedek/`
  (Burada `--delete` **kullanmayın**; sunucuda silinen eski yedekler kopyada kalsın.)

## Geri yükleme

```bash
cd /opt/kasa/deploy
docker compose stop kasa
# Mevcut (bozuk/yanlış) DB'yi silmeyin, kenara alın:
E=kasa-data/eski-$(date +%F-%H%M) && sudo mkdir -p "$E"
sudo mv kasa-data/kasa.db* "$E"/
# İstenen yedeği canlı DB yap (tarihli ya da kasa-once-* ya da elle yedek):
sudo cp kasa-data/yedek/kasa-2026-09-20.db kasa-data/kasa.db
sudo rm -f kasa-data/kasa.db-wal kasa-data/kasa.db-shm
sudo chown -R 1654:1654 kasa-data
docker compose start kasa
docker compose logs --tail 30 kasa
```

- `-wal` ve `-shm` dosyaları **mutlaka silinmelidir**. Eski DB'nin WAL'ı yeni dosyaya
  uygulanırsa veri bozulur.
- Yedek dosyaları WAL modunda değildir. Uygulama açılışta WAL modunu kendisi açar, elle
  bir şey yapmaya gerek yoktur. Eskiden bu adım eksik olduğu için geri yüklemeden sonra
  "database is locked" hataları görülüyordu.
- Yedek eski bir sürüme aitse eksik şema, index ve çift kayıt temizliği açılışta otomatik
  tamamlanır.
- Elle yedekten dönülüyorsa (`~/kasa-elle-yedek/...`) ve orada `kasa.db-wal` varsa, üç
  dosyayı birlikte geri koyun. `-wal`'ı silmeyin; o set tutarlıdır.

## Saat dilimi
Konteyner `TZ=Europe/Istanbul` ile çalışır. Uygulama "bugün"ü ayrıca Türkiye saatine göre hesaplar.

## Loglar
Konteyner logları `json-file` sürücüsüyle döndürülür: en fazla 3 dosya × 10 MB.
`docker compose logs --tail 100 kasa`
