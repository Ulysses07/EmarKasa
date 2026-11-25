# Yapay zekâ önerilerini açma ve kapama

Özellik **kapalı** gelir; kapalıyken sunucu hiçbir dış bağlantı kurmaz. Ne gönderildiği ve kurallar: [tasarım](../specs/2026-11-25-yapay-zeka-onerileri.md).

## Önce

1. [Claude Console](https://platform.claude.com/)'da hesap açın, kredi yükleyin ve **aylık harcama sınırı** koyun (Settings → Limits). Uygulamanın kendi sınırı ayrıca vardır.
2. Yalnız bu uygulama için bir API anahtarı oluşturun. Anahtarı e-posta, sohbet ya da depoya yazmayın.

## Açma

Sunucuda `deploy/.env` (izin 600):

```
KASA_YAPAY_ZEKA_ETKIN=true
KASA_YAPAY_ZEKA_ANAHTAR=<anahtar>
KASA_YAPAY_ZEKA_AYLIK_SINIR_USD=2
```

Sonra `docker compose -f docker-compose.nginx.yml up -d` (yalnız ortam değişir, derleme gerekmez). Uygulamada **Ekstre içe aktar** ekranının altındaki
**Yapay zekâ** kartı durumu gösterir; ayar eksikse nedenini yazar.

## Kullanmadan önce ölçün

**Banka kararlarıyla ölç** ve **Kart kararlarıyla ölç** geçmiş kararlarınızın yarısını gizleyip sorar, doğruluğu ve maliyeti yazar (en az 10 farklı karar; bir
sentten az). Tür doğruluğu düşükse özelliği kapalı bırakın; hafıza ve kurallar onsuz da çalışır.

## Kapama

`KASA_YAPAY_ZEKA_ETKIN=false` yapıp `up -d`. Gelmiş öneriler satırlarda kalır, yeni çağrı yapılmaz. Anahtar sızdıysa Console'dan **iptal edin**; `.env`'den silmek yetmez.

## İletiler

| İleti | Yapılacak |
|---|---|
| Anahtar geçersiz / izni yok | Console'dan yeni anahtar; `.env` ve `up -d` |
| Kredisi bitmiş olabilir | Console → Billing |
| Aylık sınıra ulaşıldı | Gelecek ayı bekleyin ya da sınırı yükseltin |
| Ulaşılamadı | Sunucudan `https://api.anthropic.com` erişimi (443) |

Harcama `YapayZekaCagrilar` tablosundadır (ay, jeton, dolar). Yanıtı kaybolan çağrının en yüksek maliyeti harcanmış sayılır; gerçek tutar Console'dadır.
