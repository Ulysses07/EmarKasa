# Kasa Defteri — VPS dağıtımı

Güncel hedef adres `https://kasa.emarglobal.com/`, VPS `72.61.187.202` üzerindedir. Canlı kurulum sistem Nginx'i ve `docker-compose.nginx.yml` dosyasını kullanır; `kasa-app` konteyneri yalnız `127.0.0.1:8080` üzerinden erişilir. Güncel yayın2.3.0: [PDF ekstre ve hesap hareketleri](../docs/deploy/kasa-2.3.md). Yayın manifesti `/opt/kasa/releases/20260923-imports/imports-published.json` içindedir.

## İlk kurulum

1. Hostinger'da `emarglobal.com` bölgesine `A / kasa / 72.61.187.202` kaydını ekleyin.
2. Kasa kaynaklarını VPS'te `/opt/kasa/` dizinine aktarın. Mevcut `.env` ve `kasa-data/` içeriğini koruyun.
3. Yalnız yeni kurulumda `deploy/.env.example` dosyasından `.env` oluşturup JWT anahtarı ve editör bilgilerini doldurun. Gerçek giriş bilgilerini depoya koymayın.
4. `/opt/kasa/deploy` içinde `docker compose -f docker-compose.nginx.yml up -d --build` çalıştırın.
5. [Alan adı ve HTTPS geçiş kılavuzunu](../docs/deploy/emarglobal-domain.md) izleyerek Nginx ve sertifikayı kurun. Son HTTPS site dosyası `nginx/kasa.emarglobal.com.conf` içindedir; sertifika yokken etkinleştirmeyin.
6. `curl --fail https://kasa.emarglobal.com/health` ile normal DNS ve TLS üzerinden200 yanıtını doğrulayın.

`docker-compose.yml`, eski OrderDeck/Caddy ağına bağlanan alternatif dağıtım şablonudur; mevcut Nginx kurulumunda yukarıdaki `-f docker-compose.nginx.yml` seçeneğini kullanın.

## Güncelleme (yeni sürüm)
1. Önce [veritabanı yükseltme kılavuzundaki](../docs/deploy/database-upgrade.md) yedek ve kopya üzerinde geçiş kontrolünü tamamla.
2. Güncellenmiş kaynakları `/opt/kasa/` dizinine aktarın; `.env` ve veri dizinini koruyun.
3. `cd /opt/kasa/deploy && docker compose -f docker-compose.nginx.yml up -d --build`
   (DB `kasa-data/` volume'de kalıcıdır; uygulama desteklenen eski şemayı migration'a taşır.)
4. `/health`, giriş ve raporları kontrol et. Bu sürüm oturum doğrulamasını yenilediği için mevcut kullanıcılar bir kez yeniden giriş yapar.

Üretimde JWT anahtarı ve editör bilgileri açıkça yapılandırılmalıdır. Boş veya geliştirme için tanımlı değerlerle API başlamaz. Geçiş sırasında yinelenen kayıt ya da tanınmayan şema bulunursa mevcut veriler korunarak başlangıç durdurulur; veritabanını silmeyin.

Alan adı geçişi yalnız Nginx/DNS/TLS ve istemci adresini değiştirir; yeni uygulama kodunun canlıya dağıtıldığını göstermez.

## Yedek
Aktif veri dizinini `kasa-app` konteynerinin `/data` mount kaynağından veya son yayın manifestinin `dataDirectory` alanından bulun. Sürüm geçişleri ayrı dizin kullandığı için eski `kasa-data/` yolunu varsaymayın. Tutarlı yedek için SQLite yedekleme yöntemini kullanın veya uygulamayı durdurup veri dizininin tamamını (`kasa.db`, varsa WAL/SHM ve `.kasa-push-keys.json` dahil) birlikte kopyalayın. Çalışan veritabanının yalnız `.db` dosyasını kopyalamak yeterli değildir. Yedeği ayrı bir ortamda açarak geri yüklemeyi doğrulayın.
