# Kasa Defteri — VPS Dağıtım

Aynı OrderDeck VPS'inde, `orderdeck-caddy` arkasında `kasa.orderdeckapp.com`.

## İlk kurulum
1. DNS: `kasa.orderdeckapp.com` A kaydı → OrderDeck VPS IP'si.
2. Kasa reposunu VPS'e kopyala (repo remote'u yok → rsync/scp):
   `rsync -az --exclude bin --exclude obj --exclude node_modules \
     ./ user@VPS:/opt/kasa/`
3. VPS'te env doldur:
   `cd /opt/kasa/deploy && cp .env.example .env && nano .env`
   (KASA_JWT_KEY = `openssl rand -base64 48`, editör kullanıcı/şifre)
4. OrderDeck ağının ayakta olduğunu doğrula:
   `docker network ls | grep orderdeck_web`
   (yoksa önce OrderDeck compose `up` edilmeli — Caddy zaten çalışıyor olmalı)
5. Kasa'yı derle + başlat:
   `cd /opt/kasa/deploy && docker compose up -d --build`
6. Caddy'yi güncelle (LiveDeck reposundaki Caddyfile'a kasa bloğu eklendikten
   sonra `/opt/orderdeck/Caddyfile`'a yansıt) ve reload:
   `docker exec orderdeck-caddy caddy reload --config /etc/caddy/Caddyfile`
7. Doğrula: `curl -sI https://kasa.orderdeckapp.com/health`

## Güncelleme (yeni sürüm)
1. `rsync ... /opt/kasa/`
2. `cd /opt/kasa/deploy && docker compose up -d --build`
   (DB `kasa-data/` volume'de kalıcı — kaybolmaz)

## Yedek
DB tek dosya: `/opt/kasa/deploy/kasa-data/kasa.db`. Yedek = dosyayı kopyala.
