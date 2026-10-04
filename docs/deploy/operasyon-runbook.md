# Emar Kasa operasyon runbook'u

Canlı sunucunun (kasa.emarglobal.com, VPS 72.61.187.202) süreklilik işleri: temel imaj ve güvenlik yamaları, sunucu dışı yedek, geri yüklemeden sonraki zorunlu adımlar, disk doluluğu, dal ve sürüm durumu. Kurulum ve sürüm güncellemesi [deploy/README.md](../../deploy/README.md), veritabanı geçişleri [database-upgrade.md](database-upgrade.md), dal durumu [dal-durumu.md](dal-durumu.md) içindedir.

Komutlar aksi yazılmadıkça VPS'te root yetkisiyle çalıştırılır. Yer tutucuları (`<...>`) sunucudaki gerçek değerlerle değiştirin; yolları tahmin etmeyin. Sırları (rclone yapılandırması, parolalar, izleme adresleri, `deploy/.env`) sohbete, bilet sistemine ya da depoya yapıştırmayın.

## Temel imajlar ve güvenlik yamaları

Dockerfile'daki iki temel imaj (`mcr.microsoft.com/dotnet/sdk`, `mcr.microsoft.com/dotnet/aspnet`) etiket + `@sha256` özetiyle sabittir:

- Aynı Dockerfile her makinede aynı temel imajla derlenir; sunucuda önbellekte kalmış eski bir imaj kullanılmaz.
- .NET çalışma zamanı, OpenSSL ve işletim sistemi (Ubuntu 24.04) paketlerinin yamaları kendiliğinden gelmez; özet aşağıdaki adımla bilinçli güncellenir.
- Sunucuda derleme her zaman `build --pull` ile, başlatma ayrı adımda yapılır ([deploy/README.md](../../deploy/README.md) "Güncelleme" 7. adım):
  ```sh
  docker compose -f docker-compose.nginx.yml build --pull kasa
  docker compose -f docker-compose.nginx.yml up -d
  ```
- Derlemeden önce [`deploy/temel_imaj.py`](../../deploy/temel_imaj.py) sabit özetleri kayıttaki güncel özetle karşılaştırır ([deploy/README.md](../../deploy/README.md) "Güncelleme" 6. adım). Özet eskiyse akış durur, önce aşağıdaki "Özet güncelleme" yapılır. Böylece eski bir özet, haftalık denetim çalışmasa da fark edilmeden sunucuya gitmez.

### Otomatik denetim

- CI'daki `Pinned base images` işi her push ve PR'da `deploy/temel_imaj.py` ile sabit özetlerin kayıtta çözüldüğünü doğrular ve aynı ana sürüm etiketinin (`10.0`) güncel özetiyle karşılaştırır. Eski özet push/PR'da uyarıdır; haftalık zamanlanmış koşuda (pazartesi) hatadır ve GitHub depo sahibine bildirim gönderir. Zamanlanmış koşu ayrı eşzamanlılık grubundadır; aynı daldaki push CI'ını iptal etmez. Özetin biçimini (etiket + 64 haneli özet, `net10.0` ile aynı ana sürüm) ağ olmadan `Kasa.Api.Tests/DepoHijyeniTests` denetler.
- GitHub zamanlanmış iş akışlarını yalnız varsayılan dalda çalıştırır. Bugün varsayılan dal (`master`) bu kod hattı değildir ([dal-durumu.md](dal-durumu.md)); karar uygulanana kadar haftalık denetim çalışmaz. O zamana kadar geliştirme makinesinde, depo kökünde ayda bir `python3 deploy/temel_imaj.py` çalıştırın; çıkış kodu 1 ise aşağıdaki adımlarla özeti güncelleyin. GitHub, 60 gün etkinlik olmayan depoda zamanlanmış iş akışlarını ayrıca durdurur. Yayın yapılmayan aylarda sunucudaki imaj da yama almaz; özet güncellemesi yeni bir yayınla sunucuya gider (aşağıda 4. adım).
- Dependabot bu depoda yapılandırılmamıştır (`.github/dependabot.yml` yok). Eklendiğinde `docker` ekosistemi de tanımlanmalıdır; Dependabot özet güncellemelerini PR olarak açar ve aşağıdaki elle adımların yerini alır.

### Özet güncelleme (ayda bir ya da CI uyarısında)

Geliştirme makinesinde, depo kökünde:

1. Güncel sürümü ve özeti kayıttan okuyun. Yalnız meta veri okunur, imaj indirilmez:
   ```sh
   for i in sdk aspnet; do
     docker buildx imagetools inspect mcr.microsoft.com/dotnet/$i:10.0 --format '{{json .Manifest.Digest}}'
     docker buildx imagetools inspect mcr.microsoft.com/dotnet/$i:10.0 \
       --format '{{ range (index .Image "linux/amd64").Config.Env }}{{ println . }}{{ end }}' \
       | grep -E '^(DOTNET_SDK_VERSION|ASPNET_VERSION)='
   done
   ```
2. Dockerfile'daki iki `FROM` satırını bu değerlerle yazın: `sdk:<DOTNET_SDK_VERSION>@<özet>` ve `aspnet:<ASPNET_VERSION>@<özet>`. Özet 64 hanesiyle kopyalanır; kısaltılmaz, tahmin edilmez. Ana sürüm (`10.0`) Kasa.Api'nin hedef çerçevesiyle aynı kalır; ana sürüm yükseltmesi ayrı bir iştir.
3. Sabit özetin çözüldüğünü ve güncel olduğunu doğrulayın: `python3 deploy/temel_imaj.py` iki imaj için "güncel" yazıp 0 ile çıkmalıdır. Depo testlerini çalıştırıp değişikliği PR olarak gönderin; `Pinned base images` işi de iki imaj için "güncel" yazmalıdır.
4. Yeni temel imaj yeni bir yayındır: sunucuda [deploy/README.md](../../deploy/README.md) "Güncelleme" 1–8 adımlarını izleyin (4. adımdaki geri dönüş imajı kimliği dahil).

### İşletim sistemi paket yamaları (poppler-utils)

`poppler-utils` (güvenilmeyen PDF ekstrelerini ayrıştırır) Dockerfile'da sürümsüz kurulur; kurulum katmanı temel imaj özeti değişene kadar derleme önbelleğinden gelir.

Çalışma imajı (`aspnet:10.0.<yama>`, dağıtım eki olmayan etiket) **Ubuntu 24.04 (noble)** tabanlıdır; paket güvenlik duyuruları Ubuntu Security Notices'ten izlenir: <https://ubuntu.com/security/notices?package=poppler> (sürüm olarak 24.04 LTS'e bakın). Dağıtım, özet ya da etiket değişince yeniden denetlenir. Yalnız kayıt meta verisi okunur:

```sh
docker buildx imagetools inspect mcr.microsoft.com/dotnet/aspnet:<etiket> \
  --format '{{ json (index .Image "linux/amd64").Config.Labels }}'
```

Çıktıda `"org.opencontainers.image.version": "24.04"` görünmelidir. Farklıysa bu bölümdeki duyuru kaynağını ve `Kasa.Api.Tests/DagitimSablonuTests` içindeki dağıtım testini birlikte güncelleyin.

Paket yamalarını özet güncellemesini beklemeden almak için ayda bir ve 24.04 için yeni bir poppler duyurusu çıktığında sunucudaki güncelleme akışı önbelleksiz derlemeyle yürütülür: [deploy/README.md](../../deploy/README.md) "Güncelleme" 1–8, 7. adım şu biçimde:

```sh
docker compose -f docker-compose.nginx.yml build --pull --no-cache kasa
docker compose -f docker-compose.nginx.yml up -d
```

Kaynak değişmediyse 1. adım bekleyen migration olmadığını doğrulamakla sınırlıdır; 2–4 ve 8. adımlar (etkin veri dizini, geri dönüş imajı, doğrulama) yine zorunludur.

## Ayrıcalıksız konteyner kimliği

Bu geçiş isteğe bağlıdır; `deploy/.env` içindeki `KASA_CONTAINER_USER` boşsa Compose mevcut `0:0` kimliğini kullanır. Canlı Nginx dağıtımında `/data` ve `/yedekler` host bağlamaları, eski root sürecinin açtığı 0700 dizinleri ve 0600 dosyaları içerir. Dockerfile veya `.env` içinde yalnız kullanıcıyı değiştirmek uygulamanın veritabanını, belge deposunu, push anahtarını, yedekleri ya da güvenlik günlüğünü okuyamamasına yol açabilir. Yedek dizininin ve `.kasa-push-keys.json` dosyasının Unix izinlerini uygulama ayrıca ayarladığı için grup yazma izni sahiplik aktarımının yerini tutmaz.

[Dağıtım kılavuzundaki ayrıcalıksız geçiş](../../deploy/README.md#ayrıcalıksız-konteynere-geçiş-isteğe-bağlı) etkin `/data` yolunun manifest ve çalışan konteynerle karşılaştırılmasını, doğrulanmış yedeği, durdurulmuş konteynerde **yalnız etkin veri/yedek ağacına** sayısal UID:GID sahipliği verilmesini, bağlama ön denetimini ve geri dönüşü adım adım anlatır. Canlı hostun sahipliği bu depodan bilinemez; `chown` işlemini otomatik dağıtım betiğine koymayın ve eski `deploy/kasa-data` dizininde çalıştırmayın. Restore ya da yeni yayın dizini oluşturduktan sonra aynı sahipliği uygulama açılmadan hazırlayın; root ile çalışan `restore_backup.py` çıktısı kendiliğinden konteyner kimliğine ait olmaz. Sunucu dışı yedek systemd birimleri root olarak çalışmaya devam eder.

Caddy/OrderDeck dağıtımında mevcut temel şablon `/yedekler` bağlamaz; `KASA_CONTAINER_USER` açılmadan önce konteynerin `/app/yedekler` içeriği, özellikle `guvenlik-gunlugu.jsonl` ile onun `.kontrol.json` eşi, doğrulanmış boş host yedek dizinine birlikte taşınmalı ve `docker-compose.caddy-backup.yml` ek dosyasıyla `/yedekler` bağlanmalıdır. Bu günlük veritabanından ayrı tutulur; geri yüklemede yedek sonrası güvenlik kararları ondan okunur. Nginx zaten kalıcı `/yedekler` bağlamasına sahiptir.

## Sunucu dışı yedek

Uygulama her gün otomatik yedek alır ve `KASA_BACKUP_DIR`'e yazar; bu dizin canlı veritabanıyla aynı diskte durur. Disk arızası, sağlayıcı hesabının askıya alınması ya da sunucunun ele geçirilmesi veritabanını ve bütün yedekleri birlikte götürür. [`deploy/uzak_yedek.py`](../../deploy/uzak_yedek.py) yedekleri sunucu dışına otomatik kopyalar:

- Yalnız servisin ad kalıbına uyan ZIP'ler (`kasa-oto-*`, `kasa-elle-*`, `kasa-goc-oncesi-*`, 2.3 öncesi `kasa-*`) ve yalnız manifest SHA-256 özeti `kasa.db` ile eşleşenler gönderilir. Özeti tutmayan (bozuk, yarım) yedek gönderilmez, hata olarak bildirilir. `.part` dosyalarına ve başka adlara dokunulmaz.
- Hedefte aynı adla dosya varsa üzerine yazılmaz (`rclone copyto --immutable`); gönderimden sonra hedefteki boyut denetlenir.
- Hedefte saklama: otomatik yedeklerde son 35 günün hepsi, son 13 takvim ayının (İstanbul) ilk yedeği ve her durumda en yeni 7; elle yedeklerden en yeni 10; göç öncesi yedekler hiç silinmez. Kural sunucudakiyle aynıdır, süreler daha uzundur (`KASA_UZAK_GUNLUK_GUN`, `KASA_UZAK_AYLIK_AY`). Hedefteki saklamanın dışında kalacak eski yerel yedek gönderilmez.
- Hedefte silme yalnız hatasız çalışmada yapılır. Gönderim, doğrulama ya da boyut hatası varsa, en yeni yerel otomatik yedek eski görünüyorsa (uygulamanın yedeği durmuş ya da sistem saati az önce ileri atlamış) ya da yerelde veya hedefte ileri tarihli bir yedek varsa o çalışmada hiçbir uzak kopya silinmez. Bu durum da hata olarak bildirilir; silinecekler hata giderildikten sonraki ilk hatasız çalışmada silinir.
- Eski yedek denetimi saat ileri atladığında yalnız uygulamanın bir sonraki saatlik yedek denetimine kadar korur: uygulama yeni saate göre "taze" bir yedek yazınca denetim geçer. Bu yüzden tek çalışmada hedefte en çok `KASA_UZAK_SILME_EN_FAZLA` (varsayılan 5) otomatik kopya silinir; fazlası gerekiyorsa hiçbiri silinmez ve hata bildirilir. Sınır kendiliğinden açılmaz. Ayrıntı ve yapılacaklar: aşağıda "Saat hatası ve toplu silme sınırı".
- Uzak hedef rclone `crypt` uzağı olmalıdır; şifresiz uzak reddedilir (bilerek `KASA_UZAK_SIFRESIZ=evet` yazılmadıkça).
- En yeni yerel otomatik yedek 48 saatten eskiyse (uygulamanın günlük yedeği durmuşsa) ve yedek ya da veri diski %80 dolduysa hata verir.
- Sonuç izleme adresine bildirilir. Haftalık doğrulama en yeni uzak kopyayı indirip [`restore_backup.py`](../../deploy/restore_backup.py) ile geçici dizinde geri açarak sınar.

Kimlik bilgisi depoda yoktur: uzak deponun anahtarları ve şifreleme parolaları sunucudaki `rclone.conf`'ta, betiğin ayarları `/etc/kasa/uzak-yedek.env`'dedir (ikisi de root:root, 600). Uygulama belgeleri (PDF ekstreleri, alış belgeleri) 2.4'ten itibaren veritabanında değil belge deposundadır (`KASA_DATA_DIR/belgeler`). Sunucu yedekleri yalnız özet listesini (`belgeler.json`) taşır; içerikler yedek aynasında (`KASA_BACKUP_DIR/belgeler/<ab>/<özet>`) artımlı tutulur. Betik bir yedeği göndermeden önce onun listesindeki ve hedefte olmayan belgeleri aynadan, özetini doğrulayarak hedefin `belgeler/` klasörüne gönderir (belge gönderilemezse yedek de gönderilmez); hedefteki belgeler saklama kuralıyla silinmez. Haftalık doğrulama listedeki her belgenin hedefte olduğunu denetler ve birkaçını indirip özetini sınar. JWT anahtarı, editör bilgileri ve diğer sırlar (`deploy/.env`) yedekte **yoktur**; parola yöneticisinde ayrıca saklanır.

Betiğin sınamaları: `python3 -m unittest discover -s deploy/tests` (CI'da `Deploy scripts` işi).

### 1. Hedef ve kimlik bilgisi

Depolama hesabı bu proje tarafından açılmaz; kurum sahibi seçer ve açar.

- **S3 uyumlu nesne deposu** (Backblaze B2, Wasabi, Hetzner Object Storage vb.): Kasa için ayrı bir kova açın ve sürümlemeyi (versioning) etkinleştirin. Yaşam döngüsü kuralıyla silinmiş ya da eski sürümleri en az 30 gün saklayın. Yalnız bu kovaya yetkili ayrı bir uygulama anahtarı oluşturun. VPS ele geçirilip anahtarla dosyalar silinse de önceki sürümler bu süre içinde geri alınabilir. Sağlayıcı nesne kilidi (object lock) sunuyorsa daha güçlü korumadır.
- Anahtar silme yetkisi taşımayacaksa betik hedefte silme yapamaz: `KASA_UZAK_SAKLAMA=hayir` yazın ve saklamayı kovanın kuralına bırakın. Tek klasörde günlük/aylık ayrımı yaşam döngüsü kuralıyla yapılamaz; "N günden eskiyi sil" kuralı aylık kopyaları da siler.
- **SFTP** (ör. Hetzner Storage Box): rclone `sftp` uzağı, üzerine `crypt`; kutunun otomatik anlık görüntüleri (snapshot) açık olmalıdır.

### 2. rclone ve şifreli uzak

```sh
sudo apt-get install -y rclone        # ya da rclone.org'daki imzası doğrulanmış .deb
rclone version
sudo rclone config
```

`rclone config` içinde iki uzak tanımlanır:

1. Depo uzağı, ör. `kasa-depo` (tür `s3`, `b2` ya da `sftp`; 1. adımdaki anahtar).
2. Şifreli uzak `kasa-sifreli`: tür `crypt`, `remote = kasa-depo:<kova>/kasa`, `filename_encryption = standard`; iki parola (password, password2) için rastgele üretme seçeneği. rclone üretilen parolaları o anda ekranda gösterir: **hemen parola yöneticisine** yazın.

```sh
sudo chmod 600 /root/.config/rclone/rclone.conf
sudo rclone listremotes --long            # kasa-sifreli: crypt görünmeli
sudo rclone mkdir kasa-sifreli:yedekler
```

VPS kaybında şifreli uzağın iki parolası olmadan uzak kopyalar açılamaz; `rclone.conf` yalnız VPS'te kalırsa yedek de onunla gider. Parola yöneticisinde iki crypt parolasını ve depo anahtarını (ya da `rclone.conf`'un tamamını güvenli not olarak) saklayın. Google Drive gibi belirteci yenilenen uzaklar yerine S3/B2/SFTP tercih edin: servis birimi `/etc`'yi salt okunur açar ve belirteç yenilemesi `rclone.conf` başka yerdeyse yazılamayabilir.

### 3. Betik ayarları

```sh
sudo install -d -m 700 /etc/kasa
sudo install -m 600 /opt/kasa/deploy/uzak-yedek.env.example /etc/kasa/uzak-yedek.env
sudo nano /etc/kasa/uzak-yedek.env
```

En az `KASA_UZAK_HEDEF=kasa-sifreli:yedekler` yazılır; izleme adresleri 6. adımdadır. Diğer değerler boş kalırsa varsayılan kullanılır (dosyadaki açıklamalar). Yedek ve veri dizini boş bırakılırsa `/opt/kasa/deploy/.env`'deki `KASA_BACKUP_DIR` ve `KASA_DATA_DIR` okunur (yalnız bu ikisi); bir yayın dizinleri değiştirdiğinde betik de onları izler.

### 4. Kuru çalıştırma

```sh
sudo sh -c 'set -a; . /etc/kasa/uzak-yedek.env; set +a; python3 /opt/kasa/deploy/uzak_yedek.py gonder --kuru'
```

Beklenen: `[kuru] gönderilecek: kasa-oto-...` satırları ve `Özet (kuru): ...`; `HATA:` satırı olmamalıdır. Kuru çalıştırma göndermez, silmez ve izlemeye bildirmez. Çıkış kodu 2 yapılandırma hatasıdır (ilk satır nedeni yazar).

### 5. Zamanlayıcılar (systemd)

```sh
cd /opt/kasa/deploy/systemd
sudo install -m 644 kasa-uzak-yedek.service kasa-uzak-yedek.timer kasa-uzak-dogrula.service kasa-uzak-dogrula.timer /etc/systemd/system/
sudo systemd-analyze calendar '*-*-* 04,10,16,22:30:00 Europe/Istanbul'
sudo systemd-analyze verify /etc/systemd/system/kasa-uzak-yedek.service /etc/systemd/system/kasa-uzak-dogrula.service
sudo systemctl daemon-reload
sudo systemctl enable --now kasa-uzak-yedek.timer kasa-uzak-dogrula.timer
sudo systemctl start kasa-uzak-yedek.service
journalctl -u kasa-uzak-yedek.service -n 50 --no-pager
systemctl list-timers 'kasa-uzak-*'
```

- Gönderim 6 saatte bir (04:30, 10:30, 16:30, 22:30), haftalık doğrulama pazar 05:30 (İstanbul). Uygulama otomatik yedeği ~24 saatte bir alır; yeni yedek en geç ~6 saat içinde uzağa gider. Yeni dosya olmayan çalışma bir şey göndermez, yalnız denetler. `Persistent=true`: sunucu o saatte kapalıysa açılışta çalışır. Elle çalıştırma için de `systemctl start` kullanın; zamanlayıcıyla aynı anda iki kopya çalışmaz.
- `systemd-analyze calendar` saat dilimini tanımıyorsa (eski systemd) iki `.timer` dosyasındaki `Europe/Istanbul` kaldırılır; saat sunucunun saat dilimine göre yorumlanır.
- Birimler `/opt/kasa` altından kopyalanır. Yayınlar `uzak_yedek.py`'yi günceller; birim dosyaları değiştiğinde bu adım yinelenir.
- Doğrulama: ertesi gün `sudo rclone ls kasa-sifreli:yedekler` yeni `kasa-oto-` dosyasını listeler.

systemd yoksa cron (dosya LF satır sonuyla yazılır, sahibi root, izin 644):

```
# /etc/cron.d/kasa-uzak-yedek
30 4,10,16,22 * * * root set -a; . /etc/kasa/uzak-yedek.env; set +a; /usr/bin/python3 /opt/kasa/deploy/uzak_yedek.py gonder 2>&1 | logger -t kasa-uzak-yedek
30 5 * * 0 root set -a; . /etc/kasa/uzak-yedek.env; set +a; /usr/bin/python3 /opt/kasa/deploy/uzak_yedek.py dogrula 2>&1 | logger -t kasa-uzak-yedek
```

### 6. İzleme

- healthchecks.io (ya da kurum içi eşdeğeri) üzerinde iki denetim açın: "Kasa uzak yedek" (periyot 6 saat, tolerans 2 saat) ve "Kasa uzak yedek doğrulama" (periyot 7 gün, tolerans 1 gün); bildirim kanalı e-posta ya da Telegram. Adresleri `KASA_UZAK_IZLEME_URL` ve `KASA_UZAK_DOGRULA_IZLEME_URL`'ye yazın. Betik başarıda adrese, hatada adres + `/fail`'e istek atar. Hiç çalışmazsa (sunucu kapalı, zamanlayıcı bozuk) denetim süre aşımıyla uyarır.
- Hata sayılanlar: doğrulanamayan yerel yedek; listeleme, gönderme ya da silme hatası; gönderimden sonra hedefte doğru boyutla görünmeyen ya da yereldekinden farklı boyutta duran kopya; 48 saatten eski en yeni otomatik yedek (`KASA_YEDEK_EN_FAZLA_SAAT`); yerelde ya da hedefte bir saatten fazla ileri tarihli yedek; tek çalışmada `KASA_UZAK_SILME_EN_FAZLA`'dan (varsayılan 5) fazla otomatik kopyanın silinecek olması; bu hatalardan biri yüzünden atlanan hedef saklama silmesi; %80 dolu yedek ya da veri diski (`KASA_DISK_ESIK_YUZDE`). Doğrulamada ayrıca geri açılamayan ya da 48 saatten eski en yeni uzak kopya ve hedefteki ileri tarihli kopya (en yeni sayılmaz).
- Kalıcı bir hata, giderilene kadar hedefte saklamayı durdurur ve uzak depo büyür. Örneğin özeti tutmayan yerel yedek her çalışmada yeniden hata verir. Böyle bir dosyayı inceleyin, çünkü disk hatası belirtisi olabilir. Sonra silmeden yedek dizininin dışına taşıyın.
- Ayrıntı: `journalctl -u kasa-uzak-yedek.service -n 100 --no-pager` (doğrulama için `kasa-uzak-dogrula.service`).

#### Saat hatası ve toplu silme sınırı

Otomatik yedeklerin saklaması yaşa bağlıdır ve yaş sistem saatiyle hesaplanır. Saat ileri atlarsa (yanlış NTP kaynağı, sanal makinenin anlık görüntüden geri dönmesi vb.) kural bütün eski kopyaları süresi dolmuş görür; engellenmezse hedefte yalnız en yeni 7 kopya kalır ve aylık geçmiş gider. Uygulamanın yerel saklaması da yaşa bağlıdır: saat ileride kaldıkça uygulama yerel geçmişi de siler. Bu durumda geçmişin korunduğu yer uzak hedeftir. Betiğin üç koruması vardır; üçü de hata bildirir ve o çalışmada hiçbir uzak kopya silinmez:

- **Eski yerel yedek.** Saat 48 saatten fazla ileri atladıktan hemen sonra en yeni yerel otomatik yedek eşikten eski görünür. Bu koruma yalnız uygulamanın bir sonraki saatlik denetimine kadar sürer: uygulama yeni saate göre "taze" (gerçekte ileri tarihli) bir yedek yazınca denetim geçer.
- **Toplu silme sınırı.** Tek çalışmada hedefte `KASA_UZAK_SILME_EN_FAZLA`'dan (varsayılan 5) fazla otomatik kopya silinecekse hiçbiri silinmez. Olağan çalışmada günde 1–2 otomatik kopya düşer; 400 günlük bir atlamada ise 49 kopyadan 43'ü düşerdi. Sınır kendiliğinden açılmaz: saat düzelene ya da operatör bir kez açıkça yükseltene kadar her çalışma hata verir. Elle kopyaların saklaması sayıya bağlıdır (en yeni 10), saatten etkilenmez ve sınıra girmez.
- **İleri tarihli yedek.** Saat düzeltildikten sonra o arada yazılmış yedekler bir saatten fazla ileri tarihli görünür. Uygulama en yeni otomatik yedeği ileride gördükçe yeni otomatik yedek almaz ve bunu yaş denetimi yakalamaz; betik ileri tarihli yedeği yerelde ya da hedefte gördüğünde hata verir. Doğrulama ileri tarihli uzak kopyayı en yeni saymaz.

Hata bildirildiğinde:

1. Saati denetleyin: `timedatectl` (`System clock synchronized: yes` olmalı) ve `date -u`. Saat yanlışsa önce NTP eşitlemesini düzeltin. Saat yanlışken hiçbir yedeği silmeyin ve sınırı yükseltmeyin.
2. İleri tarihli yerel yedekleri silmeden yedek dizininin dışına taşıyın ve uygulamayı yeniden başlatın. Uygulama son otomatik yedeğin zamanını bellekte de tutar; yeniden başlatılmadan yeni yedek almaz. Adları hata iletisi ve `ls <KASA_BACKUP_DIR>` verir (addaki zaman UTC):
   ```sh
   sudo install -d -m 700 /root/kasa-ileri-tarihli
   sudo mv <KASA_BACKUP_DIR>/<ileri-tarihli-ad> /root/kasa-ileri-tarihli/
   cd /opt/kasa/deploy && docker compose -f docker-compose.nginx.yml restart kasa
   ```
3. Hedefteki ileri tarihli kopyaları silmeden ayrı bir klasöre taşıyın. Betik yalnız `KASA_UZAK_HEDEF` klasörünü listeler, taşınan kopyalar saklamaya ve doğrulamaya girmez:
   ```sh
   sudo rclone moveto kasa-sifreli:yedekler/<ileri-tarihli-ad> kasa-sifreli:ileri-tarihli/<ileri-tarihli-ad>
   ```
4. Saat doğruysa ve silinecekler gerçekten birikmişse (birkaç günlük kesinti, ya da `KASA_UZAK_GUNLUK_GUN` / `KASA_UZAK_AYLIK_AY` kısaltıldıysa) önce kuru çalıştırmayla silinecek kopyaların listesini inceleyin, sonra bir kez yüksek sınırla çalıştırın. `<n>` hata iletisindeki sayıdır. Zamanlayıcının çalışma saatleri (04:30, 10:30, 16:30, 22:30) dışında çalıştırın. Sınırı ayar dosyasında kalıcı olarak yükseltmeyin:
   ```sh
   sudo sh -c 'set -a; . /etc/kasa/uzak-yedek.env; set +a; KASA_UZAK_SILME_EN_FAZLA=<n> python3 /opt/kasa/deploy/uzak_yedek.py gonder --kuru'
   sudo sh -c 'set -a; . /etc/kasa/uzak-yedek.env; set +a; KASA_UZAK_SILME_EN_FAZLA=<n> python3 /opt/kasa/deploy/uzak_yedek.py gonder'
   ```

### 7. Uzak kopyadan geri dönüş

A: sunucu çalışıyor, yerel yedekler kayıp ya da bozuk. B: VPS tamamen kayıp.

1. (Yalnız B) Yeni sunucuyu [deploy/README.md](../../deploy/README.md) "İlk kurulum" 1–4 ile hazırlayın, henüz başlatmayın. `deploy/.env` sırlarını parola yöneticisinden yazın. rclone'u kurun, `rclone.conf`'u parola yöneticisinden geri koyun (ya da aynı iki parolayla `kasa-sifreli` uzağını yeniden tanımlayın), `/etc/kasa/uzak-yedek.env`'i yeniden oluşturun.
2. Uzaktaki yedekleri listeleyin ve dönülecek kopyayı seçin (genellikle en yeni `kasa-oto-`):
   ```sh
   sudo sh -c 'set -a; . /etc/kasa/uzak-yedek.env; set +a; python3 /opt/kasa/deploy/uzak_yedek.py listele'
   ```
3. İndirip manifest özetini doğrulayın (var olan dosyanın üzerine yazmaz):
   ```sh
   sudo install -d -m 700 /root/kasa-geri
   sudo sh -c 'set -a; . /etc/kasa/uzak-yedek.env; set +a; python3 /opt/kasa/deploy/uzak_yedek.py indir <ad> --cikti /root/kasa-geri'
   ```
4. Yedeği **yeni ve boş** bir veri dizinine geri açın. Araç canlı dosyanın üzerine yazmaz; özet, SQLite bütünlüğü, ilişkiler ve şemayı denetler. Bildirim anahtarı yedekteyse `.kasa-push-keys.json` aynı dizine yazılır. Belge deposu biçimli yedekte (manifest 2.2.0; `indir` bunu söyler) belgeler hedefin `belgeler/` klasöründedir: önce onu indirin, araç her belgenin özetini doğrulayıp `<yeni-veri-dizini>/belgeler/` altına açar:
   ```sh
   sudo install -d -m 700 <yeni-veri-dizini>
   sudo rclone copy kasa-sifreli:<klasör>/belgeler /root/kasa-geri/belgeler   # yalnız 2.2.0 yedekte
   sudo python3 /opt/kasa/deploy/restore_backup.py /root/kasa-geri/<ad> --output <yeni-veri-dizini>/kasa.db --belge-aynasi /root/kasa-geri/belgeler
   ```
   Yerel yedekten dönüşte ayna `KASA_BACKUP_DIR/belgeler`'dir (`--belge-aynasi <KASA_BACKUP_DIR>/belgeler`); Ayarlar'dan indirilen elle yedek belgeleri kendi içinde taşır, `--belge-aynasi` gerekmez. Eski yedekler (2.1.0 ve öncesi) belgeleri `kasa.db` içinde taşır.
5. Uygulamayı yeni dizinle açın: `deploy/.env`'de `KASA_DATA_DIR=<yeni-veri-dizini>`, **`KASA_EDITOR_SIFRE`'yi yeni, en az 12 karakterlik bir değere** ve `KASA_EDITOR_SIFRE_SIFIRLA=true` yapın (aşağıda "Geri yüklemeden sonra", 1. adım); ardından A'da [deploy/README.md](../../deploy/README.md) "Güncelleme" 6–8, B'de "İlk kurulum" 5–7. A'da eski veri dizinini silmeyin, kenarda tutun. Yedek daha eski bir şemadaysa uygulama açılışta göç öncesi yedek alıp migration'ları uygular ([database-upgrade.md](database-upgrade.md)).
6. Aşağıdaki "Geri yüklemeden sonra" bölümündeki zorunlu adımları uygulayın (yeni izleyici şifresi dahil). `/health`, giriş, panel ve son dönem raporlarını kontrol edin. Yedeğin alındığı andan sonraki kayıtlar yedekte yoktur. Uzak kopyadan dönüşte kayıp aralığı en yeni uzak kopyanın yaşıdır: uygulama yedeği saatlik denetimle 24–25 saatte bir alır, gönderim 6 saatte bir (en çok 15 dakika rastgele gecikmeyle) çalışır, bu yüzden aralık olağan durumda en çok ~31 saattir. Gönderim bir süredir hata veriyorsa aralık daha uzundur; esas olan 2. adımdaki `listele` çıktısındaki zamandır (UTC). Kullanıcılara bu aralığı bildirip kayıtları yeniden girdirin. Riskli bir işlemden (sürüm güncellemesi, toplu içe aktarma) önce uygulamada elle yedek alıp `sudo systemctl start kasa-uzak-yedek.service` ile hemen gönderirseniz aralık dakikalara iner.
7. Geri dönüşü, kullanılan yedeği ve kaybedilen aralığı 8. bölümdeki tabloya yazın.

### 8. Geri yükleme tatbikatı

Kurulumdan hemen sonra ve üç ayda bir: `sudo systemctl start kasa-uzak-dogrula.service` çalıştırılıp `journalctl -u kasa-uzak-dogrula.service` içinde "Doğrulandı" satırı görülür, ya da 7. bölümün 2–4. adımları canlıya dokunmadan geçici bir dizine uygulanır. Yılda bir, 1. adım dahil tam B senaryosu (parola yöneticisindeki `rclone.conf` ile) başka bir makinede denenir.

| Tarih | Tür (doğrulama / tam geri dönüş) | Kopya | Sonuç | Yapan |
| --- | --- | --- | --- | --- |
| — | Henüz yapılmadı (kurulum bekliyor) | — | — | — |

## Geri yüklemeden sonra

Canlıya alınan her geri yükleme (yerel yedek, uzak kopya ya da göç öncesi yedek) veritabanındaki bütün durumu yedek anına sarar. Yalnız kayıtlar değil; editör şifresi ve kurtarma kodu, izleyici şifresi, alıcı hesaplarının etkinliği ve şifreleri, oturum iptalleri, cihaz bildirim kayıtları, kayıt numarası sayaçları ve kayıt sürümleri de geri döner. Yedekten sonra yapılmış bir güvenlik kararı (ör. çalınan bir cihaz yüzünden editör şifresinin değiştirilmesi, ayrılan bir çalışanın alıcı hesabının pasife alınması) veritabanında atılan kısımdadır. Bu yüzden uygulama bu kararları veritabanı dışındaki **güvenlik günlüğünde** de tutar ve geri yüklemede oradan yeniden uygular.

### Güvenlik günlüğü

- Yer: yedek dizininde `guvenlik-gunlugu.jsonl` ve 2.4.1'den itibaren onun `guvenlik-gunlugu.jsonl.kontrol.json` bütünlük dosyası (compose'da `/yedekler`, sunucuda `KASA_BACKUP_DIR`; `GuvenlikGunlugu__Yol` ile günlük yolu değiştirilebilir). Canlı veritabanının dizininden (`/data`) bağımsızdır; veritabanı geri yüklense de kalır. Üretimde zorunludur; `GuvenlikGunlugu__Etkin=false` ile API açılmaz (geliştirmede varsayılan kapalı).
- İçerik: editör şifre değişikliği, kurtarma kodu üretimi ve kullanımı, izleyici şifresi değişikliği, alıcı hesabı açma/güncelleme (pasife alma, şifre ya da kullanıcı adı değişikliği), cihaz bildirim kaydı kaldırma, ay kapatma/açma ve geri yüklemeler; her satırda an (UTC), tür, kayıt numarası ve kullanıcı adı. Şifre, şifre özeti, kurtarma kodu, oturum belirteci ve bildirim uç adresi **yazılmaz**. Her olay konteyner loguna da (`Kasa.Guvenlik`) düşer.
- Günlüğü ve bütünlük dosyasını **silmeyin ve elle düzenlemeyin**. Günlük yalnız eklenir, döndürülmez; olaylar seyrektir, boyutu küçük kalır. Bütünlük dosyası, günlük sonundaki biçimce geçerli satırlar kaybolsa bile kesilmeyi saptar. Eski günlükler ilk 2.4.1 açılışında bütünlük dosyasına alınır; bundan önceki bölümün tamlığı kanıtlanamaz. Yedek rotasyonu ve sunucu dışı yedek (`uzak_yedek.py`) yalnız `kasa-*.zip` dosyalarına bakar, günlük ve bütünlük dosyasına dokunmaz. VPS kaybında (7. bölüm, B) günlük de kaybolur; o durumda aşağıdaki "Günlük yoksa" maddesi geçerlidir.

### Uygulamanın kendiliğinden yaptıkları

Uygulamanın aldığı her yedek SQLite başlığında geri yükleme işareti (`PRAGMA user_version`; canlı dosyada 0) ve yedek anını taşır. 2.4.1 yedekleri ayrıca günlükteki kesin bayt konumunu ve SHA-256 özetini yalnız yedek veritabanında saklar; saat geri alınsa da sonraki güvenlik kararları sırasıyla bulunur. [`restore_backup.py`](../../deploy/restore_backup.py) işaretsiz yedekleri geri açarken işaretler ve her yedekte manifestteki yedek anını `__KasaGeriYukleme` tablosuna yazar. Bu kesin kesimi taşımayan eski yedeklerde kimlik erişimi güvenli yönde kilitlenir ve operatörün aşağıdaki şifre sıfırlama adımı gerekir. Uygulama işaretli dosyayla ilk açılışta, migration'lardan sonra ve HTTP sunucusu açılmadan, tek transaction'da şunları yapar:

- **Bütün oturumları kapatır.** Yeni bir oturum dönemi açılır (veri soyu kimliği; oturum damgasına girer). Yedekten önce ya da sonra, hatta aynı yedeğin daha önceki bir geri yüklemesinden sonra alınmış bütün oturum belirteçleri (30 gün) ve tanıdık cihaz belirteçleri geçersiz olur; herkes yeniden giriş yapar. `Kasa:JwtKey` değişmez ve döndürülmesi gerekmez. Açık kalmış masaüstü ya da web ekranı eski kayıt numarası ve sürümüyle yazamaz: oturum sonu alır, yeniden girişte ekran yeniden yüklenir.
- **Kurtarma kodunu iptal eder.** Yedekteki kurtarma kodu yeniden geçerli olmaz.
- **Cihaz bildirim kayıtlarını kapatır.** Yedekten sonra kaldırılmış (ör. kayıp) bir cihaz dirilmez; bildirim kullanan cihazlarda bildirimler yeniden açılır.
- **İzleyici girişini kapatır.** Editör yeni bir izleyici şifresi belirleyene kadar izleyici giremez. Yedekteki eski şifre de yedekten sonra belirlenen şifre de geçersizdir (izleyici bütün finans verisini, dışa aktarma dahil, okuyabilir).
- **Güvenlik günlüğünden yedekteki kesin kesimden sonraki kararları yeniden uygular** (yalnız sıkılaştırma; yeniden etkinleştirme gibi gevşetici kararlar uygulanmaz):
  - Editör şifresi yedekten sonra değiştirildiyse, kurtarma kodu kullanıldıysa ya da operatör şifreyi sıfırladıysa yedekteki eski şifre geçersiz kılınır ve **editör girişi kilitlenir**: ne yedekteki ya da sonradan kaybolan şifre ne de ortamdaki `KASA_EDITOR_SIFRE` geçer. Ortam şifresi bilerek kendiliğinden geçerli olmaz: ilk kurulumdan kalmış ya da unutulmuş eski bir değer olabilir. Kilidi yalnız operatörün bilinçli sıfırlaması açar ([deploy/README.md](../../deploy/README.md) "Editör şifresini sıfırlama"): aşağıdaki 1. adımda bayrak açıldıysa sıfırlama geri yükleme işleminden hemen sonra aynı açılışta uygulanır, kilit görünmez; rapora kilidin açıldığı da yazılır.
  - Yedekten sonra pasife alınan, şifresi ya da kullanıcı adı değiştirilen alıcı pasif bırakılır; editör gerekirse yeni şifreyle etkinleştirir. Yedekten sonra açılıp geri yüklemede kaybolan alıcılar rapora yazılır.
- **Kayıt numaralarını ileri alır.** AUTOINCREMENT'li her tablonun sayacı yedekteki en yüksek numaradan 1.000.000 ileri alınır. Yedekten sonra açılıp geri yüklemeyle atılan kayıtların numaraları yeni kayıtlara verilmez. Eski bir ekranın ya da bekleyen bir tekrarın taşıdığı numara başka bir kayda ulaşamaz (404). Yedek anında var olan kayıtların sürümü de geri döndüğü için eski sürümle gelen yazma 409 alır. Numaralar 32 bittir: her geri yükleme 1.000.000 kullanır (yaklaşık 2.000 geri yüklemeye yeter).
- **Rapor ve iz bırakır, işaretleri siler.** Yapılanlar ve yapılması gerekenler Türkçe maddeler halinde web'de **Araçlar > Yedekleme** ve masaüstünde **Güvenlik** ekranında "Son geri yükleme" başlığıyla görünür (`/api/yedek/durum`: `sonGeriYukleme`, `geriYuklemeRaporu`). Değişiklik geçmişinin "Oturum ve güvenlik" bölümüne `GeriYuklemeIslendi` olayı (veri soyu, yedek anı, yeni sayaçlar, sıfırlanan şifre, pasif bırakılan alıcılar, günlük durumu) yazılır; aynı olay güvenlik günlüğüne, her rapor maddesi konteyner loguna uyarı olarak düşer. Sonraki açılışlarda işlem yeniden çalışmaz. Bir hata olursa hiçbir şey değişmez ve açılış durur. Kasa kayıtlarına ve raporlara dokunulmaz: panel, haftalık ve aylık raporlar yedek anındakiyle aynıdır.
- **Günlük yoksa veya yedekteki kesin kesim doğrulanamıyorsa** (ör. VPS kaybı, eski yedek, günlükte eksik bölüm): yedekten sonraki kararlar bilinemez. Editör girişi kilitlenir ve yedekteki bütün etkin alıcılar pasifleştirilir; eski şifreler yeniden geçerli olmaz. Operatör 1. adımdaki yeni ortam şifresi ve geçici sıfırlama bayrağıyla editör girişini açar; editör gereken alıcıları ancak her birine yeni şifre vererek etkinleştirir. Günlük veya bütünlük dosyası mevcut ama okunamıyor ya da bozuksa geri yükleme işlenmez, API açılışı durur. Her iki dosyayı geri yüklerken koruyun; veritabanı yedeğiyle değiştirmeyin.

**2.4.1 → 2.4.0 → 2.4.1 geri dönüşü:** 2.4.0, bütünlük dosyasını güncellemez. Eski sunucuya dönülen sırada bir güvenlik olayı yazılırsa 2.4.1'in sonraki açılışı günlük ile kontrol dosyasının uyuşmadığını görüp durur. Önce sunucuyu durdurun, etkin `/yedekler` bağlamasını doğrulayın ve iki dosyanın değiştirilmeyecek, erişimi kısıtlı kopyasını alın. Yalnız uyuşmazlığın **doğrulanmış 2.4.0 yazımından** kaynaklandığı ve günlüğün son satırının tam olduğu durumda, `guvenlik-gunlugu.jsonl.kontrol.json` dosyasını aynı dizinde tarihli bir adla kenara taşıyıp 2.4.1'i başlatın. Yeni kontrolün kapsadığı bölüm bu açılıştaki günlük sonundan başlar; **önceki bütün yedeklerin** kesin kesimi artık doğrulanmaz ve geri yüklendiklerinde editör ile alıcı erişimi güvenli yönde kilitlenir. Uyuşmazlık nedeni belirsizse kontrolü sıfırlamayın; günlük kesilmiş veya değiştirilmiş olabilir. Kenara alınan dosyayı da silmeyin.

Doğrulama (uygulama açıldıktan sonra):

```sh
cd /opt/kasa/deploy && docker compose -f docker-compose.nginx.yml logs kasa | grep "Geri yükle"
```

"Geri yüklenmiş veritabanı tanındı" satırı ve ardından rapor maddeleri görünür. Satır yoksa dosya işaretsiz açılmıştır (ör. ZIP'ten elle çıkarıldı). Uygulamayı durdurun ve dosyayı `restore_backup.py` ile yeniden geri açın. Yedeği ZIP'ten elle çıkarmayın.

### Operatörün yapacakları (zorunlu)

1. **Uygulamayı geri yüklenen dosyayla açmadan önce** `deploy/.env`'de `KASA_EDITOR_SIFRE`'yi yeni, en az 12 karakterlik ve daha önce kullanılmamış bir değere çevirin ve `KASA_EDITOR_SIFRE_SIFIRLA=true` yapın. İlk açılışta editör şifresi bu değere sıfırlanır: yedekteki şifre de yedekten sonraki şifreler de geçersiz olur (yedek alınmadan önce ele geçmiş bir şifre, günlükte iz bırakmasa da yeniden geçerli olmaz). Bayrağı açmayı unuttuysanız ve yedekten sonra editör şifresi değiştirilmişse veya günlük eksikse editör girişi kilitli kalır (log ve rapor: "editör girişi kilitlendi"; ortam şifresi de geçmez); bayrağı açıp `docker compose -f docker-compose.nginx.yml up -d` ile yeniden oluşturun ([deploy/README.md](../../deploy/README.md) "Editör şifresini sıfırlama").
2. Trafiği açtıktan hemen sonra 1. adımdaki ortam şifresiyle editör olarak girin, Güvenlik'ten **hemen** yeni bir editör şifresi belirleyin ve **Araçlar > Yedekleme** (masaüstünde **Güvenlik**) altındaki geri yükleme raporunu okuyun. Raporda "Güvenlik günlüğü bulunamadı" ya da "… tarihinden beri tutuluyor" varsa yedekten sonraki kararlar bilinmiyor demektir (aşağıdaki 5. adım).
3. Ayarlar'dan **yeni** bir izleyici şifresi belirleyin ve yalnız erişmesi gereken kişilere iletin. Yedekteki eski şifreyi yeniden kullanmayın; ayrılan biri onu biliyor olabilir.
4. Güvenlik'ten yeni bir kurtarma kodu üretin (eskisi iptal edildi) ve güvenli yerde saklayın.
5. Alıcı hesaplarını gözden geçirin: raporda pasif bırakıldığı yazan alıcıları gerekiyorsa **her birine yeni şifre vererek** etkinleştirin, kaybolanları yeniden açın. Günlük yoksa veya yedekten sonra başlamışsa bütün alıcılar pasif kalır; eski şifrelerle etkinleştirmeyin.
6. Bildirim kullanan cihazlarda bildirimleri yeniden açın.
7. `deploy/.env`'de `KASA_EDITOR_SIFRE_SIFIRLA=false` yapıp `docker compose -f docker-compose.nginx.yml up -d` ile yeniden oluşturun (açık kalırsa aynı ortam şifresiyle sıfırlama tekrarlanmaz, ama her açılışta uyarı loglanır ve ortam şifresi değişirse yeniden uygulanır).
8. Kullanıcılara kayıp aralığını bildirin ("Uzak kopyadan geri dönüş" 6. adım; `restore_backup.py` ve rapor yedek anını yazar) ve bu aralıktaki kayıtları yeniden girdirin. Yedekten sonra kapatılmış aylar geri yüklemede yeniden açık görünür; gerekiyorsa kayıtları girdikten sonra yeniden kapatın.

## Disk doluluğu

Uzak yedek betiği her çalışmada yedek ve veri diskinin doluluğunu denetler (`KASA_DISK_ESIK_YUZDE`, varsayılan %80) ve eşik aşılınca izlemeye hata bildirir. Elle bakmak için:

```sh
df -h <KASA_DATA_DIR> <KASA_BACKUP_DIR>
du -sh <KASA_BACKUP_DIR>
ls -lhS <KASA_BACKUP_DIR> | head
docker system df
```

- Belgeler veritabanında değil belge deposundadır (`KASA_DATA_DIR/belgeler`); yedekler yalnız veritabanını ve belge listesini tutar, belge içerikleri yedek aynasına (`KASA_BACKUP_DIR/belgeler`) yalnız yeni olanlar kopyalanarak eklenir. Hiçbir tutulan yedeğin göstermediği ayna dosyalarını rotasyon siler. Otomatik ve elle yedekleri uygulamanın saklama kuralı yönetir; elle silmeyin.
- Uygulama her yedekten önce yedek diskinde geçici kopya + arşiv + yeni belgeler + `Yedek__AsgariBosAlanMb` (varsayılan 2048 MB) arar; yetmezse yedek alınmaz, Ayarlar'da "Yedek alınmadı: yedek diskinde yeterli boş alan yok…" görünür ve "Şimdi yedek indir" 507 döner. Ayarlar > Yedekleme yedek ve veri diskinin boş alanını ve yedeklerin toplam boyutunu gösterir, boş alan asgarinin altına inince uyarır.
- İsteğe bağlı üst sınır: `Yedek__AzamiToplamMb` verilirse toplam (yedekler + ayna) aşılınca en eski otomatik yedekler silinir; en yeni 7 otomatik, elle ve göç öncesi yedekler korunur, yine aşılıyorsa uyarı görünür.
- Göç öncesi yedekleri (`kasa-goc-oncesi-*`) rotasyon silmez. Yer gerekirse önce `uzak_yedek.py listele` ile uzakta kopyası olduğunu doğrulayın, sonra artık gerekmeyenleri elle kaldırın.
- Her `build --pull` yeni imaj üretir; önceki `kasa:latest` etiketi yeni imaja geçer. [Dağıtım kılavuzunun](../../deploy/README.md) 4. adımında eski imajı **derlemeden önce** sürüme özgü `kasa:geri-donus-...` etiketiyle koruyun; yalnız kimliği dosyaya yazmak yeterli değildir. `docker image prune` etiketsiz imajları siler, bu geri dönüş etiketini silmez. Geri dönüş gereği kalmadığında hangi etiketin kaldırılacağına yayın manifestine bakarak karar verin.

## Dal ve sürüm durumu

Canlı kodun hangi dalda olduğu, GitHub varsayılan dalının durumu ve seçenekler [dal-durumu.md](dal-durumu.md) içindedir (**karar bekliyor**). Karar uygulanana kadar yayın kaynağı o belgede canlı hat olarak tanımlanan dallardan alınır; GitHub varsayılan dalından (`master`) dağıtım yapılmaz.
