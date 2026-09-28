# Emar Kasa operasyon runbook'u

Canlı sunucunun (kasa.emarglobal.com, VPS 72.61.187.202) süreklilik işleri: temel imaj ve güvenlik yamaları, sunucu dışı yedek, disk doluluğu, dal ve sürüm durumu. Kurulum ve sürüm güncellemesi [deploy/README.md](../../deploy/README.md), veritabanı geçişleri [database-upgrade.md](database-upgrade.md), dal durumu [dal-durumu.md](dal-durumu.md) içindedir.

Komutlar aksi yazılmadıkça VPS'te root yetkisiyle çalıştırılır. Yer tutucuları (`<...>`) sunucudaki gerçek değerlerle değiştirin; yolları tahmin etmeyin. Sırları (rclone yapılandırması, parolalar, izleme adresleri, `deploy/.env`) sohbete, bilet sistemine ya da depoya yapıştırmayın.

## Temel imajlar ve güvenlik yamaları

Dockerfile'daki iki temel imaj (`mcr.microsoft.com/dotnet/sdk`, `mcr.microsoft.com/dotnet/aspnet`) etiket + `@sha256` özetiyle sabittir:

- Aynı Dockerfile her makinede aynı temel imajla derlenir; sunucuda önbellekte kalmış eski bir imaj kullanılmaz.
- .NET çalışma zamanı, OpenSSL ve işletim sistemi paketlerinin yamaları kendiliğinden gelmez; özet aşağıdaki adımla bilinçli güncellenir.
- Sunucuda derleme her zaman `build --pull` ile, başlatma ayrı adımda yapılır ([deploy/README.md](../../deploy/README.md) "Güncelleme" 7. adım):
  ```sh
  docker compose -f docker-compose.nginx.yml build --pull kasa
  docker compose -f docker-compose.nginx.yml up -d
  ```

### Otomatik denetim

- CI'daki `Pinned base images` işi her push ve PR'da sabit özetlerin kayıtta çözüldüğünü doğrular ve aynı ana sürüm etiketinin (`10.0`) güncel özetiyle karşılaştırır. Eski özet push/PR'da uyarıdır; haftalık zamanlanmış koşuda (pazartesi) hatadır ve GitHub depo sahibine bildirim gönderir. Özetin biçimini (etiket + 64 haneli özet, `net10.0` ile aynı ana sürüm) ağ olmadan `Kasa.Api.Tests/DepoHijyeniTests` denetler.
- GitHub zamanlanmış iş akışlarını yalnız varsayılan dalda çalıştırır. Bugün varsayılan dal (`master`) bu kod hattı değildir ([dal-durumu.md](dal-durumu.md)); karar uygulanana kadar haftalık denetim çalışmaz. O zamana kadar aşağıdaki 1. adımı ayda bir elle çalıştırın. GitHub, 60 gün etkinlik olmayan depoda zamanlanmış iş akışlarını ayrıca durdurur.
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
3. Sabit özetin çözüldüğünü doğrulayın: `docker buildx imagetools inspect <FROM satırındaki imaj>` çıktısındaki `Digest:` özetle aynı olmalıdır. Depo testlerini çalıştırıp değişikliği PR olarak gönderin; `Pinned base images` işi iki imaj için "güncel" yazmalıdır.
4. Yeni temel imaj yeni bir yayındır: sunucuda [deploy/README.md](../../deploy/README.md) "Güncelleme" 1–8 adımlarını izleyin (4. adımdaki geri dönüş imajı kimliği dahil).

### İşletim sistemi paket yamaları (poppler-utils)

`poppler-utils` (güvenilmeyen PDF ekstrelerini ayrıştırır) Dockerfile'da sürümsüz kurulur; kurulum katmanı temel imaj özeti değişene kadar derleme önbelleğinden gelir. Paket yamalarını özet güncellemesini beklemeden almak için ayda bir ve imajın işletim sistemi dağıtımı poppler için güvenlik duyurusu yayımladığında sunucudaki güncelleme akışı önbelleksiz derlemeyle yürütülür: [deploy/README.md](../../deploy/README.md) "Güncelleme" 1–8, 7. adım şu biçimde:

```sh
docker compose -f docker-compose.nginx.yml build --pull --no-cache kasa
docker compose -f docker-compose.nginx.yml up -d
```

Kaynak değişmediyse 1. adım bekleyen migration olmadığını doğrulamakla sınırlıdır; 2–4 ve 8. adımlar (etkin veri dizini, geri dönüş imajı, doğrulama) yine zorunludur.

## Sunucu dışı yedek

Uygulama her gün otomatik yedek alır ve `KASA_BACKUP_DIR`'e yazar; bu dizin canlı veritabanıyla aynı diskte durur. Disk arızası, sağlayıcı hesabının askıya alınması ya da sunucunun ele geçirilmesi veritabanını ve bütün yedekleri birlikte götürür. [`deploy/uzak_yedek.py`](../../deploy/uzak_yedek.py) yedekleri sunucu dışına otomatik kopyalar:

- Yalnız servisin ad kalıbına uyan ZIP'ler (`kasa-oto-*`, `kasa-elle-*`, `kasa-goc-oncesi-*`, 2.3 öncesi `kasa-*`) ve yalnız manifest SHA-256 özeti `kasa.db` ile eşleşenler gönderilir. Özeti tutmayan (bozuk, yarım) yedek gönderilmez, hata olarak bildirilir. `.part` dosyalarına ve başka adlara dokunulmaz.
- Hedefte aynı adla dosya varsa üzerine yazılmaz (`rclone copyto --immutable`); gönderimden sonra hedefteki boyut denetlenir.
- Hedefte saklama: otomatik yedeklerde son 35 günün hepsi, son 13 takvim ayının (İstanbul) ilk yedeği ve her durumda en yeni 7; elle yedeklerden en yeni 10; göç öncesi yedekler hiç silinmez. Kural sunucudakiyle aynıdır, süreler daha uzundur (`KASA_UZAK_GUNLUK_GUN`, `KASA_UZAK_AYLIK_AY`). Hedefteki saklamanın dışında kalacak eski yerel yedek gönderilmez.
- Uzak hedef rclone `crypt` uzağı olmalıdır; şifresiz uzak reddedilir (bilerek `KASA_UZAK_SIFRESIZ=evet` yazılmadıkça).
- En yeni yerel otomatik yedek 48 saatten eskiyse (uygulamanın günlük yedeği durmuşsa) ve yedek ya da veri diski %80 dolduysa hata verir.
- Sonuç izleme adresine bildirilir. Haftalık doğrulama en yeni uzak kopyayı indirip [`restore_backup.py`](../../deploy/restore_backup.py) ile geçici dizinde geri açarak sınar.

Kimlik bilgisi depoda yoktur: uzak deponun anahtarları ve şifreleme parolaları sunucudaki `rclone.conf`'ta, betiğin ayarları `/etc/kasa/uzak-yedek.env`'dedir (ikisi de root:root, 600). Uygulama belgeleri (PDF ekstreleri, alış belgeleri) veritabanının içindedir ve her yedekte bulunur. JWT anahtarı, editör bilgileri ve diğer sırlar (`deploy/.env`) yedekte **yoktur**; parola yöneticisinde ayrıca saklanır.

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
sudo systemd-analyze calendar '*-*-* 04:30:00 Europe/Istanbul'
sudo systemd-analyze verify /etc/systemd/system/kasa-uzak-yedek.service /etc/systemd/system/kasa-uzak-dogrula.service
sudo systemctl daemon-reload
sudo systemctl enable --now kasa-uzak-yedek.timer kasa-uzak-dogrula.timer
sudo systemctl start kasa-uzak-yedek.service
journalctl -u kasa-uzak-yedek.service -n 50 --no-pager
systemctl list-timers 'kasa-uzak-*'
```

- Günlük gönderim 04:30, haftalık doğrulama pazar 05:30 (İstanbul). `Persistent=true`: sunucu o saatte kapalıysa açılışta çalışır. Elle çalıştırma için de `systemctl start` kullanın; zamanlayıcıyla aynı anda iki kopya çalışmaz.
- `systemd-analyze calendar` saat dilimini tanımıyorsa (eski systemd) iki `.timer` dosyasındaki `Europe/Istanbul` kaldırılır; saat sunucunun saat dilimine göre yorumlanır.
- Birimler `/opt/kasa` altından kopyalanır. Yayınlar `uzak_yedek.py`'yi günceller; birim dosyaları değiştiğinde bu adım yinelenir.
- Doğrulama: ertesi gün `sudo rclone ls kasa-sifreli:yedekler` yeni `kasa-oto-` dosyasını listeler.

systemd yoksa cron (dosya LF satır sonuyla yazılır, sahibi root, izin 644):

```
# /etc/cron.d/kasa-uzak-yedek
30 4 * * * root set -a; . /etc/kasa/uzak-yedek.env; set +a; /usr/bin/python3 /opt/kasa/deploy/uzak_yedek.py gonder 2>&1 | logger -t kasa-uzak-yedek
30 5 * * 0 root set -a; . /etc/kasa/uzak-yedek.env; set +a; /usr/bin/python3 /opt/kasa/deploy/uzak_yedek.py dogrula 2>&1 | logger -t kasa-uzak-yedek
```

### 6. İzleme

- healthchecks.io (ya da kurum içi eşdeğeri) üzerinde iki denetim açın: "Kasa uzak yedek" (periyot 1 gün, tolerans 6 saat) ve "Kasa uzak yedek doğrulama" (periyot 7 gün, tolerans 1 gün); bildirim kanalı e-posta ya da Telegram. Adresleri `KASA_UZAK_IZLEME_URL` ve `KASA_UZAK_DOGRULA_IZLEME_URL`'ye yazın. Betik başarıda adrese, hatada adres + `/fail`'e istek atar. Hiç çalışmazsa (sunucu kapalı, zamanlayıcı bozuk) denetim süre aşımıyla uyarır.
- Hata sayılanlar: doğrulanamayan yerel yedek; listeleme, gönderme ya da silme hatası; gönderimden sonra hedefte doğru boyutla görünmeyen ya da yereldekinden farklı boyutta duran kopya; 48 saatten eski en yeni otomatik yedek (`KASA_YEDEK_EN_FAZLA_SAAT`); %80 dolu yedek ya da veri diski (`KASA_DISK_ESIK_YUZDE`). Doğrulamada ayrıca geri açılamayan ya da 48 saatten eski en yeni uzak kopya.
- Ayrıntı: `journalctl -u kasa-uzak-yedek.service -n 100 --no-pager` (doğrulama için `kasa-uzak-dogrula.service`).

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
4. Yedeği **yeni ve boş** bir veri dizinine geri açın. Araç canlı dosyanın üzerine yazmaz; özet, SQLite bütünlüğü, ilişkiler ve şemayı denetler. Bildirim anahtarı yedekteyse `.kasa-push-keys.json` aynı dizine yazılır:
   ```sh
   sudo install -d -m 700 <yeni-veri-dizini>
   sudo python3 /opt/kasa/deploy/restore_backup.py /root/kasa-geri/<ad> --output <yeni-veri-dizini>/kasa.db
   ```
5. Uygulamayı yeni dizinle açın: `deploy/.env`'de `KASA_DATA_DIR=<yeni-veri-dizini>`; ardından A'da [deploy/README.md](../../deploy/README.md) "Güncelleme" 6–8, B'de "İlk kurulum" 5–7. A'da eski veri dizinini silmeyin, kenarda tutun. Yedek daha eski bir şemadaysa uygulama açılışta göç öncesi yedek alıp migration'ları uygular ([database-upgrade.md](database-upgrade.md)).
6. `/health`, giriş, panel ve son dönem raporlarını kontrol edin. Yedeğin alındığı andan sonraki kayıtlar yedekte yoktur (en çok ~1 gün); kullanıcılara bildirip bu aralığı yeniden girdirin.
7. Geri dönüşü, kullanılan yedeği ve kaybedilen aralığı 8. bölümdeki tabloya yazın.

### 8. Geri yükleme tatbikatı

Kurulumdan hemen sonra ve üç ayda bir: `sudo systemctl start kasa-uzak-dogrula.service` çalıştırılıp `journalctl -u kasa-uzak-dogrula.service` içinde "Doğrulandı" satırı görülür, ya da 7. bölümün 2–4. adımları canlıya dokunmadan geçici bir dizine uygulanır. Yılda bir, 1. adım dahil tam B senaryosu (parola yöneticisindeki `rclone.conf` ile) başka bir makinede denenir.

| Tarih | Tür (doğrulama / tam geri dönüş) | Kopya | Sonuç | Yapan |
| --- | --- | --- | --- | --- |
| — | Henüz yapılmadı (kurulum bekliyor) | — | — | — |

## Disk doluluğu

Uzak yedek betiği her çalışmada yedek ve veri diskinin doluluğunu denetler (`KASA_DISK_ESIK_YUZDE`, varsayılan %80) ve eşik aşılınca izlemeye hata bildirir. Elle bakmak için:

```sh
df -h <KASA_DATA_DIR> <KASA_BACKUP_DIR>
du -sh <KASA_BACKUP_DIR>
ls -lhS <KASA_BACKUP_DIR> | head
docker system df
```

- PDF belgeleri veritabanının içinde olduğundan her yedek tam veritabanı boyutundadır. Otomatik ve elle yedekleri uygulamanın saklama kuralı yönetir; elle silmeyin.
- Göç öncesi yedekleri (`kasa-goc-oncesi-*`) rotasyon silmez. Yer gerekirse önce `uzak_yedek.py listele` ile uzakta kopyası olduğunu doğrulayın, sonra artık gerekmeyenleri elle kaldırın.
- Her `build --pull` yeni imaj üretir; eski imajlar etiketsiz kalır. Yeni sürüm doğrulandıktan ve geri dönüş gereği kalmadıktan sonra `docker image prune` ile temizlenir. Bu komut [deploy/README.md](../../deploy/README.md) "Güncelleme" 4. adımında saklanan geri dönüş imajını da siler.

## Dal ve sürüm durumu

Canlı kodun hangi dalda olduğu, GitHub varsayılan dalının durumu ve seçenekler [dal-durumu.md](dal-durumu.md) içindedir (**karar bekliyor**). Karar uygulanana kadar yayın kaynağı o belgede canlı hat olarak tanımlanan dallardan alınır; GitHub varsayılan dalından (`master`) dağıtım yapılmaz.
