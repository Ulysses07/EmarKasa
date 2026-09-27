# Kasa Defteri — VPS dağıtımı

Güncel hedef adres `https://kasa.emarglobal.com/`, VPS `72.61.187.202` üzerindedir. Canlı kurulum sistem Nginx'i ve `docker-compose.nginx.yml` dosyasını kullanır; `kasa-app` konteyneri yalnız `127.0.0.1:8080` üzerinden erişilir. Güncel yayın 2.3.0: [PDF ekstre ve hesap hareketleri](../docs/deploy/kasa-2.3.md). Yayın manifesti `/opt/kasa/releases/20260923-imports/imports-published.json` içindedir.

## Veri ve yedek dizini kuralı

- Compose şablonları `/data` ve `/yedekler` için host dizinini **yalnız** `deploy/.env` içindeki `KASA_DATA_DIR` ve `KASA_BACKUP_DIR` değişkenlerinden alır. Varsayılan dizin bilerek yoktur; değişken tanımsız veya boşsa `docker compose` (`config` ve `up` dahil) hata verip durur.
- `/opt/kasa/deploy/kasa-data` (eski şablondaki göreli `./kasa-data`) 2.0 öncesinden korunmuş **eski** veritabanı kopyasıdır; etkin veri değildir ve hiçbir komutta `/data`'ya bağlanmamalıdır. Bu veritabanında migration geçmişi olmadığından başlatıcı onu hata vermeden yerinde güncel şemaya dönüştürür: kullanıcılar 2.0 sonrası kayıtları göremez, yeni kayıtlar yanlış veritabanına yazılır ve geri dönüş kopyası kalıcı olarak değişir.
- Her yayın veriyi yeni bir mutlak dizine taşıdı (2.3.0: `/opt/kasa/deploy/kasa-data-imports-<damga>`). Etkin dizinin güvenilir kaynakları son yayın manifestinin `dataDirectory` alanı ve çalışan konteynerin `/data` bağlama kaynağıdır; ikisi aynı olmalıdır.

## İlk kurulum

Bu bölüm yalnız boş bir sunucu içindir. Mevcut kurulumda aşağıdaki "Güncelleme" adımlarını izleyin.

1. Hostinger'da `emarglobal.com` bölgesine `A / kasa / 72.61.187.202` kaydını ekleyin.
2. Kasa kaynaklarını VPS'te `/opt/kasa/` dizinine aktarın.
3. `deploy/.env.example` dosyasından `deploy/.env` oluşturup JWT anahtarı ve editör bilgilerini doldurun. Gerçek giriş bilgilerini depoya koymayın.
4. Veri ve yedek için yeni, mutlak yollu dizinler oluşturup `deploy/.env` içine yazın (`./kasa-data` kullanmayın):
   ```sh
   sudo mkdir -p <veri-dizini> <yedek-dizini>
   # deploy/.env
   KASA_DATA_DIR=<veri-dizini>
   KASA_BACKUP_DIR=<yedek-dizini>
   ```
5. `/opt/kasa/deploy` içinde önce kuru çalıştırmayla doğrulayın, ardından başlatın. `source:` satırları 4. adımdaki iki dizini göstermelidir:
   ```sh
   docker compose -f docker-compose.nginx.yml config | grep -A1 'source:'
   docker compose -f docker-compose.nginx.yml up -d --build
   ```
6. [Alan adı ve HTTPS geçiş kılavuzunu](../docs/deploy/emarglobal-domain.md) izleyerek Nginx ve sertifikayı kurun. Son HTTPS site dosyası `nginx/kasa.emarglobal.com.conf` içindedir; sertifika yokken etkinleştirmeyin.
7. `curl --fail https://kasa.emarglobal.com/health` ile normal DNS ve TLS üzerinden 200 yanıtını doğrulayın.

`docker-compose.yml`, eski OrderDeck/Caddy ağına bağlanan alternatif dağıtım şablonudur ve aynı `KASA_DATA_DIR` kuralına uyar. Mevcut Nginx kurulumunda yukarıdaki `-f docker-compose.nginx.yml` seçeneğini kullanın.

## Güncelleme (yeni sürüm)

Komutlar `/opt/kasa/deploy` içinde çalıştırılır. Yer tutucuları (`<...>`) sunucudaki gerçek değerlerle değiştirin; yolları tahmin etmeyin.

1. Önce [veritabanı yükseltme kılavuzundaki](../docs/deploy/database-upgrade.md) yedek ve kopya üzerinde geçiş kontrolünü tamamlayın.
2. Çalışan konteynerin bağlamalarını ve son yayın manifestindeki veri dizinini okuyun:
   ```sh
   docker inspect kasa-app --format '{{range .Mounts}}{{.Source}} -> {{.Destination}}{{println}}{{end}}'
   grep -o '"dataDirectory"[^,}]*' /opt/kasa/releases/<son-yayın>/<ad>-published.json
   ```
   `/data` satırının kaynağı manifestteki `dataDirectory` ile birebir aynı olmalıdır. Farklıysa veya kaynak `/opt/kasa/deploy/kasa-data` ise **durun**; farkı açıklamadan devam etmeyin. `/yedekler` satırının kaynağı yedek dizinidir.
3. Bu değerleri `deploy/.env` dosyasına yazın ve veritabanının o dizinde olduğunu doğrulayın:
   ```sh
   # deploy/.env
   KASA_DATA_DIR=<etkin-veri-dizini>
   KASA_BACKUP_DIR=<etkin-yedek-dizini>
   ```
   ```sh
   ls -l <etkin-veri-dizini>/kasa.db
   ```
   Yol yanlış yazılırsa Docker o yolda boş dizin açar ve uygulama boş veritabanıyla başlar; `kasa.db` görünmüyorsa devam etmeyin.
4. Geri dönüş için sunucudaki compose dosyasını saklayın: `cp docker-compose.nginx.yml <geri-dönüş-dizini>/compose-onceki.yml`.
5. Güncellenmiş kaynakları `/opt/kasa/` dizinine aktarın. `deploy/.env`, veri ve yedek dizinleri ile `deploy/kasa-data` üzerine yazmayın; rsync kullanıyorsanız bunları `--exclude` ile hariç tutun. Depodaki compose şablonu sunucudakinin yerine geçebilir; bağlamalar artık yalnız `.env` değişkenlerinden gelir.
6. Kuru çalıştırmayla doğrulayın:
   ```sh
   # Değişkensiz çalıştırma hata vermeli; diskteki dosyanın korumalı şablon olduğunu gösterir.
   docker compose --env-file /dev/null -f docker-compose.nginx.yml config -q
   # .env ile başarılı olmalı; source satırları 2. adımdaki /data ve /yedekler kaynaklarıyla aynı olmalı.
   docker compose -f docker-compose.nginx.yml config | grep -A1 'source:'
   ```
   İlk komut `required variable KASA_DATA_DIR is missing a value` hatası vermiyorsa ya da ikinci komutta farklı bir kaynak veya `/opt/kasa/deploy/kasa-data` görünüyorsa `up` çalıştırmayın. Kabuğunuzda `KASA_DATA_DIR` dışa aktarılmışsa ilk komut hata vermez; önce `unset KASA_DATA_DIR KASA_BACKUP_DIR` çalıştırın. `config` çıktısının tamamı sırları da içerdiğinden yalnız `grep` ile süzülmüş satırları paylaşın.
7. `docker compose -f docker-compose.nginx.yml up -d --build`
8. 2. adımdaki `docker inspect` komutunu yeniden çalıştırıp `/data` kaynağının `KASA_DATA_DIR` ile aynı olduğunu doğrulayın. `/health`, giriş ve raporları kontrol edin; 2.0 sonrası kayıtlar (alışlar, kart/kredi, aylık gider, ekstre belgeleri) görünmelidir. Görünmüyorsa yanlış dizin bağlanmıştır: konteyneri durdurun ve 4. adımda saklanan compose dosyasıyla geri dönün.

Sunucudaki yayın betikleri (depo dışındaki `publish_*.py`) şablondaki `./kasa-data:/data` satırını mutlak dizinle değiştirmeye dayanıyordu. Bu satır artık olmadığından betikler ilk denetimde durur. Sonraki betikli yayında compose satırını yeniden yazmak yerine yeni veri dizinini `deploy/.env` içindeki `KASA_DATA_DIR` değerine yazın ve manifestin `dataDirectory` alanıyla aynı tutun.

Eski veri dizinlerini (`deploy/kasa-data` ve önceki damgalı dizinler) ek önlem olarak `chmod -R a-w` ile salt okunur yapabilirsiniz. Konteyner root olarak çalıştığı sürece bu tek başına koruma sağlamaz; asıl koruma yukarıdaki doğrulama adımlarıdır.

Üretimde JWT anahtarı ve editör bilgileri açıkça yapılandırılmalıdır. Boş veya geliştirme için tanımlı değerlerle API başlamaz. Geçiş sırasında yinelenen kayıt ya da tanınmayan şema bulunursa mevcut veriler korunarak başlangıç durdurulur; veritabanını silmeyin.

Alan adı geçişi yalnız Nginx/DNS/TLS ve istemci adresini değiştirir; yeni uygulama kodunun canlıya dağıtıldığını göstermez.

## Yedek

Aktif veri dizinini `kasa-app` konteynerinin `/data` bağlama kaynağından veya son yayın manifestinin `dataDirectory` alanından bulun; bu değer `deploy/.env` içindeki `KASA_DATA_DIR` ile aynı olmalıdır. Sürüm geçişleri ayrı dizin kullandığı için eski `kasa-data/` yolunu varsaymayın. Tutarlı yedek için SQLite yedekleme yöntemini kullanın veya uygulamayı durdurup veri dizininin tamamını (`kasa.db`, varsa WAL/SHM ve `.kasa-push-keys.json` dahil) birlikte kopyalayın. Çalışan veritabanının yalnız `.db` dosyasını kopyalamak yeterli değildir. Yedeği ayrı bir ortamda açarak geri yüklemeyi doğrulayın.
