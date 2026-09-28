# Emar Kasa operasyon runbook'u

Canlı sunucunun (kasa.emarglobal.com, VPS 72.61.187.202) süreklilik işleri: temel imaj ve güvenlik yamaları. Kurulum ve sürüm güncellemesi [deploy/README.md](../../deploy/README.md), veritabanı geçişleri [database-upgrade.md](database-upgrade.md) içindedir.

Komutlar aksi yazılmadıkça VPS'te root yetkisiyle çalıştırılır. Yer tutucuları (`<...>`) sunucudaki gerçek değerlerle değiştirin; yolları tahmin etmeyin. Sırları (parolalar, `deploy/.env`) sohbete, bilet sistemine ya da depoya yapıştırmayın.

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
- GitHub zamanlanmış iş akışlarını yalnız varsayılan dalda çalıştırır. Bugün varsayılan dal (`master`) bu kod hattı değildir; karar uygulanana kadar haftalık denetim çalışmaz. O zamana kadar aşağıdaki 1. adımı ayda bir elle çalıştırın. GitHub, 60 gün etkinlik olmayan depoda zamanlanmış iş akışlarını ayrıca durdurur.
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
