# Kasa Defteri — VPS dağıtımı

Güncel hedef adres `https://kasa.emarglobal.com/`, VPS `72.61.187.202` üzerindedir. Canlı kurulum sistem Nginx'i ve `docker-compose.nginx.yml` dosyasını kullanır; `kasa-app` konteyneri yalnız `127.0.0.1:8080` üzerinden erişilir. Güncel yayın 2.3.0: [PDF ekstre ve hesap hareketleri](../docs/deploy/kasa-2.3.md). Yayın manifesti `/opt/kasa/releases/20260923-imports/imports-published.json` içindedir.

## Veri ve yedek dizini kuralı

- **Etkin veri dizini = son yayın manifestindeki `dataDirectory` = sunucudaki `deploy/.env` içindeki `KASA_DATA_DIR`.** Çalışan konteynerin `/data` bağlama kaynağı da bu değerdir; üçü aynı olmalıdır. Depodaki Compose şablonları yalnız bu `.env` ile kullanılır.
- Compose şablonları `/data` ve `/yedekler` için host dizinini **yalnız** `deploy/.env` içindeki `KASA_DATA_DIR` ve `KASA_BACKUP_DIR` değişkenlerinden alır. Varsayılan dizin bilerek yoktur; değişken tanımsız veya boşsa `docker compose` (`config` ve `up` dahil) hata verip durur.
- Bağlamalar uzun sözdizimiyle ve `bind: { create_host_path: false }` ile yazılıdır: `.env`'deki yol yazım hatası nedeniyle yoksa Docker o yolda boş dizin **açmaz**, `up` `bind source path does not exist` hatasıyla durur. Var olan ama yanlış bir dizin (ör. eski `kasa-data`) ise bu korumayla yakalanmaz; aşağıdaki doğrulama adımları bu yüzden zorunludur.
- `/opt/kasa/deploy/kasa-data` (eski şablondaki göreli `./kasa-data`) 2.0 öncesinden korunmuş **eski** veritabanı kopyasıdır; etkin veri değildir ve hiçbir komutta `/data`'ya bağlanmamalıdır. Bu veritabanında migration geçmişi olmadığından başlatıcı onu hata vermeden yerinde güncel şemaya dönüştürür: kullanıcılar 2.0 sonrası kayıtları göremez, yeni kayıtlar yanlış veritabanına yazılır ve geri dönüş kopyası kalıcı olarak değişir.
- Her yayın veriyi yeni bir mutlak dizine taşıdı (2.3.0: `/opt/kasa/deploy/kasa-data-imports-<damga>`). Eski belgelerdeki `kasa-data-editor-<damga>` gibi yollar o yayınlara aittir; tarihsel kayıttır.

## İlk kurulum

Bu bölüm yalnız boş bir sunucu içindir. Mevcut kurulumda aşağıdaki "Güncelleme" adımlarını izleyin.

1. Hostinger'da `emarglobal.com` bölgesine `A / kasa / 72.61.187.202` kaydını ekleyin.
2. Kasa kaynaklarını VPS'te `/opt/kasa/` dizinine aktarın. GitHub'ın varsayılan dalı (`master`) canlı kod hattı değildir; hangi dal ve commit'in dağıtılacağı [dal durumu belgesindedir](../docs/deploy/dal-durumu.md).
3. `deploy/.env.example` dosyasından `deploy/.env` oluşturup JWT anahtarı ve editör bilgilerini doldurun. Gerçek giriş bilgilerini depoya koymayın.
4. Veri ve yedek için yeni, mutlak yollu dizinleri oluşturun (`./kasa-data` kullanmayın). Şablon eksik yolu kendisi açmaz; dizinler yoksa `up` hata verir:
   ```sh
   sudo mkdir -p <veri-dizini> <yedek-dizini>
   ```
   Aynı yolları `deploy/.env` dosyasına yazın. Bu satırlar kabukta çalıştırılmaz, dosyaya eklenir:
   ```ini
   KASA_DATA_DIR=<veri-dizini>
   KASA_BACKUP_DIR=<yedek-dizini>
   ```
5. `/opt/kasa/deploy` içinde önce kuru çalıştırmayla doğrulayın, ardından imajı derleyip başlatın. `source:` satırları 4. adımdaki iki dizini göstermelidir; `temel_imaj.py` 0 ile çıkmazsa derlemeyin ("Güncelleme" 6. adımdaki açıklama):
   ```sh
   docker compose -f docker-compose.nginx.yml config | grep -A1 'source:'
   python3 temel_imaj.py
   docker compose -f docker-compose.nginx.yml build --pull kasa
   docker compose -f docker-compose.nginx.yml up -d
   ```
6. [Alan adı ve HTTPS geçiş kılavuzunu](../docs/deploy/emarglobal-domain.md) izleyerek Nginx ve sertifikayı kurun. Son HTTPS site dosyası `nginx/kasa.emarglobal.com.conf` içindedir; sertifika yokken etkinleştirmeyin.
7. `curl --fail https://kasa.emarglobal.com/health` ile normal DNS ve TLS üzerinden 200 yanıtını doğrulayın.

`docker-compose.yml`, eski OrderDeck/Caddy ağına bağlanan alternatif dağıtım şablonudur ve aynı `KASA_DATA_DIR` kuralına uyar; ayrıca `KASA_GUVENILIR_VEKILLER` ister (aşağıdaki "Güvenilen vekiller" bölümü). Mevcut Nginx kurulumunda yukarıdaki `-f docker-compose.nginx.yml` seçeneğini kullanın.

## Güncelleme (yeni sürüm)

Komutlar `/opt/kasa/deploy` içinde çalıştırılır. Yer tutucuları (`<...>`) sunucudaki gerçek değerlerle değiştirin; yolları tahmin etmeyin. Yayımlanacak commit'i seçerken geliştirme makinesinde, depo kökünde `python3 deploy/temel_imaj.py` çalıştırın: temel imaj özeti eskiyse önce özet güncellenir (6. adım aynı denetimi sunucuda yineler).

1. Önce [veritabanı yükseltme kılavuzundaki](../docs/deploy/database-upgrade.md) yedek ve kopya üzerinde geçiş kontrolünü tamamlayın. 2.3'ten 2.4'e geçişte belgeler veritabanından `KASA_DATA_DIR/belgeler`'e taşınır: kılavuzdaki "Belge deposu geçişi" bölümündeki süre ve disk ihtiyacını (veri ve yedek diskinde en az veritabanı boyutu kadar boş alan) önceden doğrulayın.
2. Çalışan konteynerin bağlamalarını ve son yayın manifestindeki veri dizinini okuyun:
   ```sh
   docker inspect kasa-app --format '{{range .Mounts}}{{.Source}} -> {{.Destination}}{{println}}{{end}}'
   grep -o '"dataDirectory"[^,}]*' /opt/kasa/releases/<son-yayın>/<ad>-published.json
   ```
   `/data` satırının kaynağı manifestteki `dataDirectory` ile birebir aynı olmalıdır. Farklıysa veya kaynak `/opt/kasa/deploy/kasa-data` ise **durun**; farkı açıklamadan devam etmeyin. `/yedekler` satırının kaynağı yedek dizinidir.
3. Bu değerleri `deploy/.env` dosyasına yazın. Bu satırlar kabukta çalıştırılmaz, dosyaya eklenir:
   ```ini
   KASA_DATA_DIR=<etkin-veri-dizini>
   KASA_BACKUP_DIR=<etkin-yedek-dizini>
   ```
   Veritabanının o dizinde olduğunu doğrulayın:
   ```sh
   ls -l <etkin-veri-dizini>/kasa.db
   ```
   Yol yoksa `up` hata verip durur (şablon boş dizin açmaz). Var olan ama yanlış bir dizin ise yakalanmaz; `kasa.db` görünmüyorsa devam etmeyin.
4. Geri dönüş için sunucudaki compose dosyasını ve çalışan imajın kimliğini saklayın:
   ```sh
   mkdir -p <geri-dönüş-dizini>
   cp /opt/kasa/deploy/docker-compose.nginx.yml <geri-dönüş-dizini>/compose-onceki.yml
   docker inspect kasa-app --format '{{.Image}}' > <geri-dönüş-dizini>/imaj-onceki.txt
   grep -n 'image:' <geri-dönüş-dizini>/compose-onceki.yml
   ```
   7. adımdaki derleme, şablondaki `image:` etiketini (`kasa:latest`) yeni imaja taşır. Saklanan dosya aynı etiketi kullanıyorsa eski koda yalnız bu imaj kimliğiyle dönülebilir.
5. Güncellenmiş kaynakları `/opt/kasa/` dizinine aktarın. Kaynak, yayımlanacak commit'tir; GitHub'ın varsayılan dalı (`master`) canlı kod hattı değildir ([dal durumu](../docs/deploy/dal-durumu.md)). `deploy/.env`, veri ve yedek dizinleri ile `deploy/kasa-data` üzerine yazmayın; rsync kullanıyorsanız bunları `--exclude` ile hariç tutun. Depodaki compose şablonu sunucudakinin yerine geçebilir; bağlamalar artık yalnız `.env` değişkenlerinden gelir.
6. Kuru çalıştırmayla doğrulayın:
   ```sh
   # Değişkensiz çalıştırma hata vermeli; diskteki dosyanın korumalı şablon olduğunu gösterir.
   docker compose --env-file /dev/null -f docker-compose.nginx.yml config -q
   # .env ile başarılı olmalı; source satırları 2. adımdaki /data ve /yedekler kaynaklarıyla aynı olmalı.
   docker compose -f docker-compose.nginx.yml config | grep -A1 'source:'
   # Temel imaj özetleri güncel olmalı; yalnız kayıt meta verisi okunur, imaj indirilmez.
   python3 temel_imaj.py
   ```
   İlk komut `required variable KASA_DATA_DIR is missing a value` hatası vermiyorsa ya da ikinci komutta farklı bir kaynak veya `/opt/kasa/deploy/kasa-data` görünüyorsa `up` çalıştırmayın. Kabuğunuzda `KASA_DATA_DIR` dışa aktarılmışsa ilk komut hata vermez; önce `unset KASA_DATA_DIR KASA_BACKUP_DIR` çalıştırın. `config` çıktısının tamamı sırları da içerdiğinden yalnız `grep` ile süzülmüş satırları paylaşın.

   `temel_imaj.py` her temel imaj için "güncel" yazıp 0 ile çıkmalıdır. Dockerfile'daki özetler sabit olduğundan .NET, OpenSSL ve işletim sistemi (çalışma imajı Ubuntu 24.04 tabanlı) yamaları ancak özet güncellenince gelir:
   - `ESKİ:` satırı ve çıkış kodu 1: yayımlanacak commit, yama almamış eski bir temel imaja sabitli. Derlemeyin. Özeti geliştirme makinesinde [operasyon runbook'u](../docs/deploy/operasyon-runbook.md) "Özet güncelleme" adımlarıyla güncelleyip commit'leyin, akışı o commit'le 5. adımdan yineleyin. Acil bir düzeltme bilerek eski özetle yayımlanırsa bunu yayın manifestine not edin; özet güncellemesini hemen ardından ayrı bir yayınla yapın.
   - `HATA:` satırı ve çıkış kodu 2: özet sabitlenmemiş, kayıtta çözülemedi ya da `docker buildx` çalışmadı (`docker buildx version`). Nedeni giderilmeden derlemeyin.
7. İmajı derleyin, ardından başlatın:
   ```sh
   docker compose -f docker-compose.nginx.yml build --pull kasa
   docker compose -f docker-compose.nginx.yml up -d
   ```
   `up -d --build` kullanmayın: `--pull` olmadan yerel önbellekte kalmış temel imajla derler. Dockerfile'daki temel imajlar etiket + `@sha256` özetiyle sabittir; `--pull` bu özeti kayıttan doğrular. Özetin ve imajdaki işletim sistemi paketlerinin (ör. `poppler-utils`) güvenlik yamalarıyla güncellenmesi [operasyon runbook'unda](../docs/deploy/operasyon-runbook.md) "Temel imajlar ve güvenlik yamaları" bölümündedir.
8. 2. adımdaki `docker inspect` komutunu yeniden çalıştırıp `/data` kaynağının `KASA_DATA_DIR` ile aynı olduğunu doğrulayın. `/health`, giriş ve raporları kontrol edin; 2.0 sonrası kayıtlar (alışlar, kart/kredi, aylık gider, ekstre belgeleri) görünmelidir. Görünmüyorsa yanlış dizin bağlanmıştır: aşağıdaki "Geri dönüş" adımlarını uygulayın.

### Geri dönüş

Saklanan compose dosyası `/opt/kasa/deploy/docker-compose.nginx.yml` üzerine kopyalanır ve **aynı dizinde, `--build` olmadan** başlatılır:

```sh
cd /opt/kasa/deploy
docker compose -f docker-compose.nginx.yml stop
cp <geri-dönüş-dizini>/compose-onceki.yml /opt/kasa/deploy/docker-compose.nginx.yml
grep -n ':/data' /opt/kasa/deploy/docker-compose.nginx.yml
docker tag "$(cat <geri-dönüş-dizini>/imaj-onceki.txt)" <compose-onceki.yml içindeki image: değeri>
docker compose -f docker-compose.nginx.yml up -d
docker inspect kasa-app --format '{{range .Mounts}}{{.Source}} -> {{.Destination}}{{println}}{{end}}'
```

- `grep` satırındaki `/data` kaynağı ve son `docker inspect` çıktısı, 2. adımda okunan etkin veri dizini olmalıdır.
- `--build` kullanmayın: `/opt/kasa` altındaki kaynaklar artık yeni sürümdür; derleme yeni kodu yeniden üretir ve `image:` etiketini yeniden yeni imaja taşır. `docker tag` satırı etiketi 4. adımda saklanan imaja geri çevirir.
- Saklanan dosyayı bulunduğu yerden (`-f <geri-dönüş-dizini>/compose-onceki.yml`) veya başka bir dizinden çalıştırmayın. Compose göreli yolları (`build.context: ..`, varsa göreli bağlamalar) ve `.env` dosyasını compose dosyasının dizinine göre çözer; proje adını da (`.env`'de `COMPOSE_PROJECT_NAME` yoksa) bu dizinin adından türetir (`deploy`). Başka dizinde yeni bir proje açılır: sabit `container_name: kasa-app` mevcut konteynerle çakışır (`Conflict. The container name "/kasa-app" is already in use`), `.env` bulunamadığı için sırlar boş kalır ve göreli bir bağlama yanlış dizine gider (eski kısa sözdiziminde orada boş dizin açılır).
- Bu geri dönüş yalnız yeni sürüm etkin veritabanına hiç bağlanmadıysa (ör. yanlış dizin bağlandıysa) doğrudan uygulanır. Yeni sürüm etkin veritabanında yeni migration uyguladıysa eski imajı yeni şema üzerinde çalıştırmayın; [veritabanı yükseltme kılavuzundaki](../docs/deploy/database-upgrade.md) gibi eşleşen yedeği eski sürümle birlikte geri yükleyin.
- Yanlış bağlanan dizini (ör. `kasa-data`) silmeyin; o arada oraya yazılan kayıtları ayrıca inceleyin.

### Depo dışı yayın betikleri

2.0–2.3 yayınları geliştirme makinesindeki `artifacts/release/publish_*.py` betikleriyle yapıldı; bu betikler depoda izlenmez (`.gitignore`). Betikler şablonda eski `./kasa-data:/data` satırının tam bir kez geçtiğini doğrular ve sunucu compose'unu bu satırı mutlak veri diziniyle değiştirerek yeniden yazar. Bu şablonda o satır yoktur (uzun sözdizimi, `KASA_DATA_DIR`); betikler olduğu gibi çalıştırılırsa ilk denetimde durur. Bu şablonla ilk betikli yayından önce iki yoldan biri seçilmelidir:

- Betikler compose'u yeniden yazmak yerine yeni veri ve yedek dizinini sunucudaki `deploy/.env` içine `KASA_DATA_DIR` ve `KASA_BACKUP_DIR` olarak yazacak, manifestin `dataDirectory` alanını aynı değerle kaydedecek ve `up` öncesinde yukarıdaki `config` doğrulamasını çalıştıracak şekilde güncellenir.
- Ya da yayın bu README'nin "Güncelleme" akışıyla elle yapılır.

Betiklerdeki denetimi gevşetip şablona göreli `./kasa-data:/data` satırını geri eklemeyin.

Eski veri dizinlerini (`deploy/kasa-data` ve önceki damgalı dizinler) ek önlem olarak `chmod -R a-w` ile salt okunur yapabilirsiniz. Konteyner root olarak çalıştığı sürece bu tek başına koruma sağlamaz; asıl koruma yukarıdaki doğrulama adımlarıdır.

Üretimde JWT anahtarı ve editör bilgileri açıkça yapılandırılmalıdır. Boş veya geliştirme için tanımlı değerlerle API başlamaz. Geçiş sırasında yinelenen kayıt ya da tanınmayan şema bulunursa mevcut veriler korunarak başlangıç durdurulur; veritabanını silmeyin.

Alan adı geçişi yalnız Nginx/DNS/TLS ve istemci adresini değiştirir; yeni uygulama kodunun canlıya dağıtıldığını göstermez.

## Güvenilen vekiller (`Kasa__GuvenilirVekiller`)

Uygulama `X-Forwarded-For` başlığını yalnız bu listedeki adreslerden gelen bağlantılarda kabul eder ve vekilin eklediği en sağdaki değeri istemci IP'si sayar. Giriş ve güvenlik uçlarındaki IP başına hız sınırları bu adrese göre işler. Ayar boşsa varsayılan `127.0.0.0/8;::1/128;172.16.0.0/12;192.168.0.0/16` kullanılır (loopback ve Docker'ın varsayılan adres havuzları).

- **Nginx kurulumu (`docker-compose.nginx.yml`, canlı):** konteyner yalnız `127.0.0.1:8080`'e yayınlanır ve kendi compose ağında tektir; bu adreslerden yalnız yerel Nginx bağlanabilir. Varsayılan yeterlidir, `KASA_GUVENILIR_VEKILLER` okunmaz.
- **Caddy/ortak ağ kurulumu (`docker-compose.yml`):** konteyner harici `orderdeck_web` ağına katılır. Varsayılan liste bu ağdaki **bütün** konteynerlerin başlığına güvenir; bunlardan biri sahte bir istemci IP'si göndererek IP başına sınırları aşabilir veya denemelerini başka bir IP'ye yükleyebilir. Bu şablon bu yüzden `deploy/.env` içinde `KASA_GUVENILIR_VEKILLER` ister; boşsa Compose durur. Değer yalnız Caddy konteynerinin adresi (`/32`) ya da yalnız Caddy'ye ait bir alt ağ olmalıdır; bütün `orderdeck_web` alt ağını yazmak varsayılandan daha güvenli değildir. Adresi bulmak için:
  ```sh
  docker inspect <caddy-konteyneri> --format '{{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}'
  docker network inspect orderdeck_web --format '{{range .IPAM.Config}}{{.Subnet}} {{end}}'
  ```
  Caddy'nin adresi sabit değilse konteyner yeniden oluşturulunca değişebilir. Kalıcı çözüm, OrderDeck compose'unda Caddy'ye `ipv4_address` ile sabit adres vermektir; ağda IPv6 açıksa Caddy'nin IPv6 adresi de listeye eklenir. Adres listede kalmazsa uygulama başlığı yok sayar, bütün istemciler Caddy'nin tek IP'si sayılır ve hız sınırları herkese birlikte uygulanır. Uygulama bu durumu günlüğe uyarı olarak yazar ve editörün Ayarlar ekranında gösterir.

## Yedek

Aktif veri dizinini `kasa-app` konteynerinin `/data` bağlama kaynağından veya son yayın manifestinin `dataDirectory` alanından bulun; bu değer `deploy/.env` içindeki `KASA_DATA_DIR` ile aynı olmalıdır. Sürüm geçişleri ayrı dizin kullandığı için eski `kasa-data/` yolunu varsaymayın. Tutarlı yedek için SQLite yedekleme yöntemini kullanın veya uygulamayı durdurup veri dizininin tamamını (`kasa.db`, varsa WAL/SHM, `.kasa-push-keys.json` ve belge deposu `belgeler/` klasörü dahil) birlikte kopyalayın. Çalışan veritabanının yalnız `.db` dosyasını kopyalamak yeterli değildir; 2.4'ten itibaren alış belgeleri ve ekstre PDF'leri veritabanında değil `belgeler/` klasöründedir (`Belge__Dizin`, compose'da `/data/belgeler`). Yedeği ayrı bir ortamda açarak geri yüklemeyi doğrulayın.

Uygulamanın yedekleri (`KASA_BACKUP_DIR`) yalnız veritabanını ve belge özet listesini (`belgeler.json`) taşır; belge içerikleri aynı dizinde `belgeler/` aynasına yalnız yeni olanlar, özeti doğrulanarak kopyalanır. Ayarlar'dan indirilen elle yedek belgeleri de içerir (kendi kendine yeterlidir). `restore_backup.py` sunucu yedeğini `--belge-aynasi <KASA_BACKUP_DIR>/belgeler` ile açar. Yedek alınmadan önce yedek diskinde yer denetlenir (`Yedek__AsgariBosAlanMb`, varsayılan 2048; isteğe bağlı `Yedek__AzamiToplamMb`). Otomatik yedekler canlı veritabanıyla aynı diskte durur; VPS kaybında birlikte gider. Sunucu dışı kopya `uzak_yedek.py` ile otomatiktir: yalnız manifest özeti doğrulanmış yedekler şifreli uzak hedefe gider, hedefte saklama uygulanır, disk doluluğu ve yedeğin güncelliği denetlenir. Kurulum, zamanlayıcı (`systemd/`), izleme ve uzak kopyadan geri dönüş [operasyon runbook'unda](../docs/deploy/operasyon-runbook.md) "Sunucu dışı yedek" bölümündedir.
