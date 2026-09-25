# 23 Eylül 2026 sade kasa API sözleşmesi

JSON camelCase; tarihler YYYY-MM-DD; para en fazla 2 ondalık. Finans okuma editör/izleyici, yazma editör; alışlar editör veya kaydın sahibi alıcıya açıktır. Hatalar 400 doğrulama, 404 bulunamadı, 409 sürüm/tekrar/bağ çakışmasıdır.

## Kasa

`/api/rapor/panel`, `/api/rapor/haftalik`, `/api/rapor/aylik`, `/api/donemler`, `/api/kanallar`, `/api/islemler` ve `/api/gelenler` mevcut kanal/genel kasa akışını sürdürür. Eski kart ve kredi hesaplamaları korunur; ayrı hesap veya kredi ödeme takibi eklenmez.

## Alış ve ödeme

- GET `/api/alis/kanallar` ve GET `/api/alis` alıcının kanal dağılımı ve kendi taslakları içindir.
- POST `/api/alis`, PUT `/api/alis/{id}`: `{surum,tarih,tedarikci,not?,kalemler:[{aciklama,tutar,dagilimlar:[{kanalId,tutar}]}]}`. `tedarikci` yalnız serbest metindir; cari kartı üretilmez.
- POST `/api/alis/{id}/gonder`, `/onayla`, `/iade` mevcut sürüm ve sahiplik denetimini sürdürür. İade açıklaması zorunludur.
- POST `/api/alis/{id}/odemeler`: `{surum,istekId,tarih,tutar,krediKartiId?,mevcutIslemId?,not?}`.
- PUT `/api/alis/{id}/odemeler/{odemeId}`: `{surum,istekId,tarih,tutar,krediKartiId?,aciklama,hedefAlisId?,hedefSurum?}`. Hedef verilirse ödeme başka alışa taşınır. Sonuç kaynak alış; hedef listeden yenilenir. Eski kart bağlantısı silinmiş bir kart harcamasının tipi sırf tarih/tutar düzeltildiği için nakde çevrilmez.
- POST `/api/alis/{id}/odemeler/{odemeId}/iptal`: `{surum,istekId,aciklama}`. Bağlı gider iptal edilir. Önceki kayıt ve açıklama saklanır.
- `istekId` GUID'dir. Aynı gövde aynı kimlikle tekrar güvenlidir, farklı gövde 409 döner. İptal edilmiş ödemenin eski isteği tekrar gider oluşturmaz.
- Eski nullable `tedarikciId`, `vade`, `miktar`, `birimFiyat`, `hesapId` sözleşme alanları uyumluluk için kalır. Yeni yazma isteğinde dolu verilirse 400 döner.

## Destekleyici işlemler

- GET/POST `/api/alis/{id}/belgeler`, GET/DELETE `/api/belgeler/{id}`: sahiplik denetimli PNG/JPEG/PDF eki; 10 MB sınırı.
- GET `/api/disari-aktar?baslangic=&bitis=&kanal=&bicim=xlsx|csv|html`: filtreli gider raporu. HTML yazdır/PDF akışı içindir.
- POST `/api/auth/sifre`, `/api/auth/kurtarma-kodu`, `/api/auth/kurtar`: parola ve tek kullanımlık kurtarma kodu. Değişiklik önceki oturumları geçersizleştirir.
- GET `/api/surum`, GET `/api/yedek/durum`, POST `/api/yedek`: sürüm ve editöre özel yedek yönetimi.

`/api/cariler`, `/api/alis/tedarikciler`, `/api/tedarikciler/...`, `/api/hesaplar/...`, `/api/nakit-takvimi`, `/api/is-listesi` ve yeni kredi gerçekleşme uçları kaldırılmıştır. ERP12 ile senkronizasyon yoktur.
