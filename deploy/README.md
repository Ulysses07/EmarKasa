# Kasa Defteri — VPS Dağıtım

Aynı OrderDeck VPS'inde, `orderdeck-caddy` arkasında `kasa.emarglobal.com`.
Windows uygulaması bu adrese bağlanır (`Kasa.App/MauiProgram.cs`).

- Kaynak: GitHub'daki `github.com/Ulysses07/EmarKasa` reposu (özel). VPS'e kod **rsync** ile
  gönderilir. VPS'te depo için bir deploy key varsa `git clone`/`git pull` da kullanılabilir.
  Hangisi seçilirse seçilsin `deploy/.env` ve `deploy/kasa-data/` elle korunmalıdır.
- Compose dizini: `/opt/kasa/deploy` · Servis: `kasa` · Konteyner: `kasa-app`
- Veri: host'ta `/opt/kasa/deploy/kasa-data/`, konteynerde `/data/`
  (`kasa.db` ile birlikte `kasa.db-wal` ve `kasa.db-shm`, yedekler `yedek/` altında,
  sunucu dışı yedeğin ayarı ve durumu `uzak-yedek/` altında).
- Sunucu dışı yedek servisi: `kasa-yedek` (konteyner `kasa-yedek`). Aşağıdaki "Sunucu dışı yedek" bölümüne bakın.

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
9. Sunucu dışı yedeği kurun: aşağıdaki "Sunucu dışı yedek" bölümü. Kurulmazsa veriler yalnız
   bu sunucunun diskinde durur.

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
- `uzakYedek` alanı sunucu dışı yedeğin son durumunu gösterir (`ok`, `hata`, `eski`,
  `yapılandırılmadı`). Yalnız bilgi amaçlıdır; yanıt kodunu değiştirmez. Ayrıntı:
  "Sunucu dışı yedek → Çalıştığını kontrol etme".
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
- **Sunucu dışı kopya (şiddetle önerilir):** `kasa-yedek` servisi bunu her gece otomatik ve
  şifreli yapar. Kurulum: aşağıdaki "Sunucu dışı yedek" bölümü.
  Ek olarak yedek klasörünü elle de çekebilirsiniz (kendi bilgisayarınızda günlük cron ile):
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
- Uzak (sunucu dışı) kopyadan dönmek için: aşağıdaki "Uzak kopyadan geri yükleme".

## Sunucu dışı yedek

Günlük yedekler sunucunun kendi diskinde durur. Sunucu bozulur, silinir ya da hesap
kapanırsa yedekler de gider. `kasa-yedek` servisi her gece en yeni yedeği **şifreleyip**
sizin seçtiğiniz başka bir yere gönderir: Google Drive, başka bir sunucu (SFTP) ya da S3
uyumlu bir depo. Uygulama koduna ve canlı veritabanına dokunmaz; yalnız yedek dosyasını okur.

Ne yapar:

- **Her gece 03:30'da** (Türkiye saati) `kasa-data/yedek/` içindeki en yeni günlük yedeği
  alır, sıkıştırır ve `KASA_YEDEK_SIFRE` parolasıyla şifreler (rclone "crypt"). Parolayı
  bilmeyen, dosyanın içini okuyamaz.
- Uzakta iki klasör tutar: `gunluk/` (**son 30 gün**) ve `aylik/` (her ayın ilk yedeği,
  **son 12 ay**). Daha eski kopyaları kendisi siler; başka dosyalara dokunmaz.
- **Her Pazar** en yeni uzak kopyayı geri indirir, şifresini çözer, açar ve SQLite bütünlük
  denetimi (`PRAGMA integrity_check`) yapar. Yani yedeğin gerçekten geri yüklenebildiği
  haftada bir denenir.
- Sonucu `kasa-data/uzak-yedek/durum.json` dosyasına yazar. `/health` yanıtındaki
  `uzakYedek` alanı bunu gösterir.

Sunucuda `/opt/kasa/deploy/kasa-data/uzak-yedek/` klasörü:

| Dosya | Ne |
|---|---|
| `rclone.conf` | Uzak hedefin bağlantı bilgisi (Google Drive izni, SFTP bilgisi). **Gizlidir.** |
| `durum.json` | Son gönderim ve son doğrulamanın sonucu |
| `geri-al/` | Elle geri alınan (indirilip açılmış) kopyalar |
| `sftp_anahtar`, `known_hosts` | Yalnız SFTP hedefinde |

Bu klasör `kasa-data/` içinde olduğu için rsync'le silinmez ve repoya girmez.

### Kurulum (bir kez, yaklaşık 15 dakika)

Komutların hepsi sunucuda, `cd /opt/kasa/deploy` içinde çalıştırılır.

**Adım 1 — Bu sürümü kurun.** Normal "Güncelleme" adımlarını izleyin. `docker compose build`
artık `kasa-yedek` imajını da derler, `docker compose up -d` onu da başlatır. Kurulum
bitene kadar servis hiçbir şey göndermez; log'da `KASA_UZAK_HEDEF boş` yazar.

**Adım 2 — Klasörü hazırlayın:**

```bash
sudo mkdir -p kasa-data/uzak-yedek
sudo chown -R 1654:1654 kasa-data
```

**Adım 3 — Uzak hedefi tanımlayın.** Aşağıdakilerden **birini** seçin (A, B ya da C).
Hedefin adı her örnekte `uzak` olsun.

#### A) Google Drive

Sunucunun Drive'a yazabilmesi için bir kez tarayıcıdan izin vermeniz gerekir. Sunucuda
tarayıcı olmadığı için izin kendi bilgisayarınızda alınır.

1. Windows bilgisayarınıza rclone'u indirin: https://rclone.org/downloads/ → "Windows",
   "Intel/AMD - 64 Bit" zip. Zip'i bir klasöre açın (ör. `C:\rclone`). Kurulum gerekmez.
2. Sunucuda şu komutu çalıştırın ve soruları aşağıdaki gibi yanıtlayın:

   ```bash
   docker compose run --rm kasa-yedek rclone config
   ```

   - `n` (New remote) → `name>` sorusuna `uzak`
   - `Storage>` sorusuna `drive` yazın (Google Drive)
   - `client_id>` ve `client_secret>`: boş bırakın, Enter
   - `scope>`: **`drive.file`** yazın (ya da listedeki numarasını). Böylece rclone yalnız
     kendi oluşturduğu dosyaları görür; Drive'ınızdaki diğer dosyalara erişemez.
   - `service_account_file>`: boş, Enter
   - Burada sayılmayan bir soru çıkarsa Enter'a basın (varsayılan).
   - `Edit advanced config?` → `n`
   - `Use web browser to automatically authenticate rclone with remote?` → `n`
   - Ekranda `rclone authorize "drive" "eyJ..."` ile başlayan bir satır çıkar. Bu satırı
     **olduğu gibi** kopyalayın. Sunucudaki pencereyi kapatmayın.
3. Windows'ta `C:\rclone` klasörünü açın, adres çubuğuna `cmd` yazıp Enter'a basın. Açılan
   pencerede kopyaladığınız satırı `rclone.exe` ile çalıştırın:
   `rclone.exe authorize "drive" "eyJ..."`. Tarayıcı açılır. Yedeklerin gideceği Google
   hesabıyla girip izin verin. Pencerede `Paste the following into your remote machine --->`
   ile `<---End paste` arasında `{"access_token":...}` ile başlayan uzun bir metin çıkar.
   Bu metnin tamamını kopyalayın.
4. Sunucudaki pencereye yapıştırıp Enter'a basın. Sonra:
   - `Configure this as a Shared Drive (Team Drive)?` → `n`
   - `Keep this "uzak" remote?` → `y`
   - Çıkmak için `q`
5. Deneyin (hata vermemeli; boş liste normaldir):
   `docker compose run --rm kasa-yedek rclone lsd uzak:`

`.env` değeri: `KASA_UZAK_HEDEF=uzak:kasa-yedek`. Drive'da `kasa-yedek` klasörü kendiliğinden
oluşur.

#### B) Başka bir sunucu (SFTP)

SSH ile girilebilen başka bir makine gerekir (başka bir VPS, evdeki NAS …). Parola yerine
bu işe özel bir anahtar kullanılır.

1. Bu sunucuda anahtarı üretin ve yedek sunucusunun kimliğini kaydedin
   (`YEDEK_SUNUCU` yerine yedek sunucusunun adresini yazın):

   ```bash
   sudo ssh-keygen -t ed25519 -N "" -C kasa-yedek -f kasa-data/uzak-yedek/sftp_anahtar
   ssh-keyscan -H YEDEK_SUNUCU | sudo tee kasa-data/uzak-yedek/known_hosts
   sudo chown -R 1654:1654 kasa-data/uzak-yedek
   sudo cat kasa-data/uzak-yedek/sftp_anahtar.pub
   ```

2. Son komutun çıktısını (`ssh-ed25519 ...` ile başlayan tek satır) yedek sunucusunda
   yedekleri tutacak kullanıcının `~/.ssh/authorized_keys` dosyasına ekleyin. Yedek
   sunucusunda klasörü de açın: `mkdir -p ~/kasa-yedek`
3. Hedefi tek komutla tanımlayın (`YEDEK_SUNUCU` ve `KULLANICI` yerine kendi bilgilerinizi yazın):

   ```bash
   docker compose run --rm kasa-yedek rclone config create uzak sftp \
     host=YEDEK_SUNUCU user=KULLANICI \
     key_file=/data/uzak-yedek/sftp_anahtar known_hosts_file=/data/uzak-yedek/known_hosts
   ```

   Port 22 değilse sona `port=2222` gibi ekleyin.
4. Deneyin: `docker compose run --rm kasa-yedek rclone lsd uzak:` → kullanıcının ev
   klasörü listelenmeli, `kasa-yedek` görünmeli.

`.env` değeri: `KASA_UZAK_HEDEF=uzak:kasa-yedek` (kullanıcının ev klasöründeki `kasa-yedek`).

#### C) S3 uyumlu depo (Backblaze B2, Cloudflare R2, Wasabi …)

Sağlayıcının verdiği erişim anahtarlarıyla:

```bash
docker compose run --rm kasa-yedek rclone config create uzak s3 provider=Other \
  access_key_id=ANAHTAR secret_access_key=GIZLI_ANAHTAR endpoint=https://SAGLAYICI_ADRESI
```

`.env` değeri: `KASA_UZAK_HEDEF=uzak:KOVA_ADI/kasa-yedek`. Sağlayıcıya özel ayarlar için:
https://rclone.org/s3/

**Adım 4 — Şifreleme parolasını belirleyin.** Rastgele bir parola üretin:

```bash
openssl rand -base64 32
```

`deploy/.env` dosyasını açın (`nano .env`) ve şu iki satırı doldurun:

```
KASA_UZAK_HEDEF=uzak:kasa-yedek
KASA_YEDEK_SIFRE=<openssl'in verdiği metin>
```

- **Parolayı sunucu dışında da saklayın** (parola yöneticisi ya da kâğıda yazıp kasada).
  Sunucu kaybolursa parola da onunla gider. **Parola olmadan uzak yedekler açılamaz.**
- Parolayı sonradan değiştirmeyin. Değişirse eski kopyalar eski parolayla kalır, haftalık
  doğrulama da hata verir. Değiştirmek zorundaysanız uzaktaki `kasa-yedek` klasörünü
  boşaltıp baştan başlayın.
- Parolada `$`, tırnak ve boşluk kullanmayın. openssl çıktısı uygundur.
- Sunucu kaybolursa gereken tek şey bu paroladır. Google Drive izni ve SFTP anahtarı yeni
  sunucuda yeniden oluşturulur.

**Adım 5 — Başlatın ve ilk yedeği hemen alın:**

```bash
KASA_SURUM=$(cat SURUM) docker compose up -d
docker compose run --rm kasa-yedek yedekle
docker compose run --rm kasa-yedek dogrula
```

Beklenen son satırlar: `Sunucu dışı yedek tamam: gunluk/kasa-2026-09-24.db.gz (… bayt).` ve
`Doğrulama tamam: gunluk/kasa-2026-09-24.db.gz (integrity_check ok, … işlem).` Bundan
sonra her gece kendiliğinden çalışır.

Saat ya da doğrulama gününü değiştirmek için `.env`'deki `KASA_UZAK_SAAT` (ör. `04:15`) ve
`KASA_DOGRULAMA_GUNU` (1=Pazartesi … 7=Pazar) satırlarını değiştirip yine
`KASA_SURUM=$(cat SURUM) docker compose up -d` çalıştırın. Saat, uygulamanın günlük yedeğinden
sonra olmalı; gece yarısından sonraki ilk saat içinde alınır, 01:30'dan önce seçmeyin.

### Çalıştığını kontrol etme

1. **Durum dosyası:** `docker compose run --rm kasa-yedek durum`
   - `sonBasari`: son başarılı gönderim. Saat UTC yazılır; Türkiye saati 3 saat ileridir.
   - `hata`: `null` olmalı. Doluysa kısa sebep burada, ayrıntısı `hataAyrinti`'dadır.
   - `dogrulamaSonucu`: `"ok"` olmalı (ilk Pazar'dan ya da elle `dogrula`'dan sonra görünür).
   - `uyari`: ör. o günün yerel yedeği yoksa ya da eski kopyalar silinemediyse dolar.
2. **Sağlık adresi:** `curl -s https://kasa.emarglobal.com/health`. `uzakYedek.durum`:
   - `ok`: son 36 saatte başarılı gönderim var; son gönderim ve son doğrulama hatasız.
   - `hata`: son gönderim ya da son doğrulama başarısız. Sebep `hata` ya da
     `dogrulamaHatasi` alanında. Ayrıntı için 1. ve 4. maddeler.
   - `eski`: 36 saattir başarılı gönderim yok. Servis durmuş olabilir: `docker compose ps`.
   - `yapılandırılmadı`: kurulum yapılmamış ya da servis hiç çalışmamış.

   Türkçe harfler yanıtta `\u0131` gibi görünür, bu normaldir. Okunaklı görmek için:
   `curl -s https://kasa.emarglobal.com/health | jq .uzakYedek` (`jq` yoksa `sudo apt install jq`).
   Rclone'un ham hata metni (sunucu adresi içerebilir) herkese açık bu adreste gösterilmez.
3. **Uzaktaki kopyalar:** `docker compose run --rm kasa-yedek listele`. Google Drive'da
   `kasa-yedek/gunluk/` içinde `kasa-2026-09-24.db.gz.bin` gibi dosyalar görünür. İçerik
   şifrelidir; Drive'dan indirilen dosya doğrudan açılmaz, bu normaldir.
4. **Log:** `docker compose logs --tail 50 kasa-yedek`
5. **Otomatik izleme (önerilir):** UptimeRobot gibi bir izleyicide `/health` için "Keyword"
   türü izleme kurun. Aranacak metin: `"uzakYedek":{"durum":"ok"`. Metin bulunamazsa uyarı
   gelir.

### Elle yedek

```bash
docker compose run --rm kasa-yedek yedekle
```

En yeni **günlük** yedeği hemen gönderir (ör. kurulumdan sonra denemek için). Günlük yedek
gece yarısından sonra alınır. Bu yüzden bugün yapılan değişiklikler bu gece alınacak yedekle
gider. Aynı günün kopyası uzakta yenisiyle değiştirilir. Elle doğrulama:
`docker compose run --rm kasa-yedek dogrula`

### Uzak kopyadan geri yükleme

**Sunucu çalışıyor, veri bozuk ya da yanlış:**

1. Hangi kopyaların olduğuna bakın: `docker compose run --rm kasa-yedek listele`
2. Kopyayı indirin. Servis kopyayı çözer, açar ve bütünlüğünü denetler:

   ```bash
   docker compose run --rm kasa-yedek geri-al                              # en yeni günlük kopya
   docker compose run --rm kasa-yedek geri-al kasa-2026-09-20.db.gz        # belirli bir gün
   docker compose run --rm kasa-yedek geri-al aylik/kasa-2026-08.db.gz     # belirli bir ay
   ```

   Son satır: `Hazır: kasa-data/uzak-yedek/geri-al/kasa-2026-09-20.db (integrity_check ok, … işlem).`
3. Yukarıdaki "Geri yükleme" adımlarını uygulayın. Tek fark, kopyalanacak dosyadır:
   `sudo cp kasa-data/uzak-yedek/geri-al/kasa-2026-09-20.db kasa-data/kasa.db`

**Sunucu tamamen kayboldu (yeni sunucuya kurulum):**

1. Yeni sunucuda "İlk kurulum" 1–5. adımlarını yapın. `.env`'e `KASA_UZAK_HEDEF` ve
   **eski parolayı** (`KASA_YEDEK_SIFRE`) de yazın. Uygulamayı henüz başlatmayın; yalnız derleyin:
   `KASA_SURUM=$(cat SURUM) docker compose build`
2. "Sunucu dışı yedek → Kurulum" 2. ve 3. adımlarını yapın (Google Drive izni yeniden alınır;
   SFTP'de yeni anahtar üretilip yedek sunucusuna eklenir).
3. En yeni kopyayı indirin ve canlı veritabanı yapın:

   ```bash
   docker compose run --rm kasa-yedek geri-al
   sudo cp kasa-data/uzak-yedek/geri-al/kasa-2026-09-23.db kasa-data/kasa.db   # "Hazır:" satırındaki ad
   sudo chown -R 1654:1654 kasa-data
   ```

4. "İlk kurulum" 6. adımdan devam edin (`docker compose up -d --build` …).

### Sorun giderme

| Mesaj | Ne yapmalı |
|---|---|
| `KASA_UZAK_HEDEF boş` | Kurulum 4. adım, sonra `docker compose up -d` |
| `rclone ayarı yok` | Kurulum 3. adım |
| `KASA_YEDEK_SIFRE boş; şifresiz yedek gönderilmez` | Kurulum 4. adım, sonra `docker compose up -d` |
| `... yazılamıyor` | `sudo chown -R 1654:1654 kasa-data` |
| `Yerel günlük yedek bulunamadı` | Uygulama yedek alamıyor: `docker compose logs kasa \| grep -i yedek` |
| `Uzak kopya indirilemedi ya da şifresi çözülemedi` ve ayrıntıda `bad password` | `.env`'deki parola, kopyaları şifreleyen parola değil. Doğru parolayı yazın. |
| Google Drive: ayrıntıda `invalid_grant` ya da `token expired` | İzni yenileyin: `docker compose run --rm kasa-yedek rclone config reconnect uzak:` ve A) 3–4. adımlar |
| `docker compose build` sırasında `rclone/rclone:1.71.0 ... not found` | `deploy/uzak-yedek/Dockerfile`'daki sürümü https://hub.docker.com/r/rclone/rclone/tags adresindeki güncel bir sürümle değiştirin |

Geçici olarak kapatmak için: `docker compose stop kasa-yedek`. Kalıcı kapatmak için `.env`'de
`KASA_UZAK_HEDEF=` satırını boşaltıp `docker compose up -d` çalıştırın.

## Saat dilimi
Konteyner `TZ=Europe/Istanbul` ile çalışır. Uygulama "bugün"ü ayrıca Türkiye saatine göre hesaplar.

## Loglar
Konteyner logları `json-file` sürücüsüyle döndürülür: en fazla 3 dosya × 10 MB.
`docker compose logs --tail 100 kasa`
