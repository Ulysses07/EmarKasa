# Kasa alan adı geçişi

Yeni adres: `https://kasa.emarglobal.com/`. VPS: `72.61.187.202`.

19 Eylül 2026'da alan adı geçişi tamamlandı. Kullanıcının Hostinger'da eklediği `kasa` A kaydı `72.61.187.202` adresine yönleniyor. Yetkili DNS sunucuları `athena.dns-parking.com` ve `apollo.dns-parking.com`.

Doğrulananlar: Windows bilgisayardan ve VPS'ten normal DNS ile HTTPS sağlık yanıtı200, geçerli TLS, HTTP→HTTPS301 yönlendirmesi ve editör girişi200 (rol ve token varlığı kontrol edildi; şifre/token kayda yazılmadı). Let's Encrypt sertifikası18 Aralık2026'ya kadar geçerli; otomatik yenileme ve Nginx yeniden yükleme kancası yapılandırıldı. İstemci Release derlemesi ve55 ApiClient testi geçti.

19 Eylül'deki alan adı işlemi uygulama imajını veya veritabanını güncellemedi. Tam 2.0 API yayını daha sonra, 23 Eylül'de aşağıda belirtilen kontrollerle tamamlandı.

## 23 Eylül 2026: tam editör yayını

Canlı imaj `kasa:2.0.0-editor-20260923`; etkin Nginx dosyası normal proxy yapılandırması `deploy/nginx/kasa.emarglobal.com.conf` dosyasıdır. `/`, statik dosyalar, `/api/` ve `/health` güncel `kasa-app` konteynerinden gelir. Tam sürümde `/kasa-runtime.json` bilerek 404 döner. Editör alış/onay, ödeme düzenleme, alıcı yönetimi ve ayar ekranları canlıdır; eski JWT oturumlarında bir kez yeniden giriş beklenir.

Eski veritabanı/veri dizini ayrı tutuldu ve değiştirilmedi. Son snapshot yeni veri dizinine alındı; diğer veri dosyaları da korundu. Etkin dizin ve geri dönüş bilgileri `/opt/kasa/releases/20260923-editor/editor-published.json` dosyasındadır. Beş migration, eski satırların korunması, bütün geçmiş raporların eşitliği ve yedekten geri yükleme yalnız sunucuda doğrulandı; canlıya deneme finansal kaydı eklenmedi. Yinelenen eski gelirler birleştirilmeden/silinmeden salt okunur gruplar olarak kaldı. HTTPS kök, editör girişi, ayarlar ve alış okuma uçları başarılıdır. Yayın öncesi 392 test ile Windows Release derlemesi geçti.

### Geçmiş: geçici görüntüleme arayüzü

Aynı gün ilk geçiş denemesi eski yinelenen gelirler nedeniyle durunca, `kasa.emarglobal.com.readonly.conf` ile `/var/www/kasa-web/current` statik arayüzü geçici olarak açılmıştı. Runtime içeriği `{"saltOkunur":true,"surum":"1.0-web"}` idi; yeni alış/yönetim özellikleri kapalıydı. Bu kip tam editör yayınıyla sona erdi. Önceki yayın dosyaları ve Nginx yedeği `/opt/kasa/releases/20260923-sade` altında tarihsel kayıt olarak tutulur.

## DNS

Hostinger'da **emarglobal.com** bölgesine `A`, ad `kasa`, değer `72.61.187.202` kaydı eklenir. TTL varsayılan kalabilir. VPS için doğrulanmış bir IPv6 hedefi olmadığından bu geçişte AAAA kaydı eklenmez. Ana alan adının ve diğer alt alanların kayıtları değiştirilmez.

## Sunucu

Mevcut Kasa servisi `127.0.0.1:8080` üzerinde çalışır. Alan adı değişikliği tek başına veritabanı veya uygulama imajı dağıtımı gerektirmez; 23 Eylül'deki tam sürüm yayını ayrı bir işlem olarak tamamlanmıştır.

Etkin `/data` bağlantısı sunucuda `/opt/kasa/deploy/kasa-data-editor-<stamp>` dizinine gider; kesin yol yayın manifestinden okunur. Depo Compose şablonunun `./kasa-data` yolu eski veriyi gösterir. Gelecek dağıtımlarda sunucudaki etkin bağlantıyı koruyun; şablonu olduğu gibi kopyalamayın.

Depodaki `deploy/nginx/kasa.emarglobal.com.bootstrap.conf`, DNS/TLS hazırlığı boyunca yalnız sertifika doğrulama yolunu açar. Giriş ve diğer API uçları HTTP üzerinden sunulmaz. Son yapılandırma `deploy/nginx/kasa.emarglobal.com.conf` dosyasındadır; sertifika kurulmadan etkinleştirilmez.

1. Mevcut Kasa Nginx ayarını geri dönüş için yedekle. Yeni ad için ayrı site kullan; eski alan adının ayarını koru.
2. `/var/www/kasa-acme/.well-known/acme-challenge` dizinini oluştur. Bootstrap dosyasını yeni site olarak kur; `nginx -t` başarılıysa Nginx'i yeniden yükle.
3. Yetkili DNS ve genel çözümleyicilerde A kaydının VPS IP'sini verdiğini doğrula. Beklenmeyen A/AAAA/CNAME hedefi varsa sertifika isteği gönderme.
4. Mevcut Certbot hesabıyla webroot sertifikasını al:

```sh
certbot certonly --webroot -w /var/www/kasa-acme \
  -d kasa.emarglobal.com --cert-name kasa.emarglobal.com \
  --non-interactive --keep-until-expiring \
  --deploy-hook 'nginx -t && systemctl reload nginx'
```

5. Sertifika mevcutsa son HTTPS yapılandırmasını yeni siteye kur. `nginx -t` başarılıysa yeniden yükle; hata varsa önceki yeni-site dosyasını geri koy.
6. Normal DNS ile `https://kasa.emarglobal.com/health` yanıtının 200 ve TLS doğrulamasının başarılı olduğunu doğrula. HTTP istekleri HTTPS'e yönlenmelidir. Eski servisin yerel sağlık kontrolü de 200 kalmalıdır.

## İstemci

`Kasa.ApiClient/ApiAdresi.cs` yeni sürümün varsayılan adresini belirler. Kurulu eski uygulamaların adresi kendiliğinden değişmez; yeni derleme çalıştırılmalıdır. `KASA_API_URL` ortam değişkeni varsa varsayılanı geçersiz kılar; eski adrese yönleniyorsa yeni HTTPS adresine alınmalı ve uygulama yeniden başlatılmalıdır.

DNS veya sertifika hazır değilse geçiş tamamlanmış sayılmaz. IP adresini doğrudan istemciye yazmak, HTTP ile giriş yapmak veya sertifika kontrolünü kapatmak çözüm olarak kullanılmaz.

## Geri dönüş

Alan adı yapılandırması sorunu varsa yalnız `kasa.emarglobal.com` site ayarını inceleyip doğrulanmış yapılandırmayla Nginx'i yeniden yükle. Tam editör yayını sonrasında yeni kayıtlar oluşabileceğinden eski veritabanına otomatik dönme; mevcut veri dizinini koru ve [2.0 geri dönüş kurallarını](kasa-2.0.md) uygula. Eski alan adının DNS kaydı bulunmadığı için istemciyi eski adrese çevirmek tek başına bağlantıyı düzeltmez.
