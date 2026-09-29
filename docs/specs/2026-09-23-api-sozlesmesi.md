# 23 Eylül 2026 sade kasa API sözleşmesi

JSON camelCase; tarihler YYYY-MM-DD; para en fazla 2 ondalık. Finans okuma editör/izleyici, yazma editör; alışlar editör veya kaydın sahibi alıcıya açıktır. Hatalar 400 doğrulama, 404 bulunamadı, 409 sürüm/tekrar/bağ çakışmasıdır.

## Kasa

`/api/rapor/panel`, `/api/rapor/haftalik`, `/api/rapor/aylik`, `/api/donemler`, `/api/kanallar`, `/api/islemler` ve `/api/gelenler` mevcut kanal/genel kasa akışını sürdürür. Eski kart ve kredi hesaplamaları korunur; ayrı hesap veya kredi ödeme takibi eklenmez.

### Çekirdek kayıt sürümü (contract-6)

- Gider, gelir, kanal ve ayarlar `surum` taşır: GET `/api/islemler`, `/api/gelenler`, `/api/kanallar`, `/api/ayarlar` ile gider ve kanal yazma yanıtları. Kayıt her değiştiğinde bir artar; uç dışındaki dolaylı yazımlar da artırır (alış ödemesi düzeltmesi ve alıştan ayırma, kanal adı değişikliğinin gider/gelir etiket senkronu, eski kart silinince bağın kopması). İçeriği değişmeyen kayıt sürümü artırmaz; izleyici şifresi ayarların sürümünü artırmaz. Hesap ve raporlar sürümü okumaz.
- PUT `/api/islemler/{id}`, `/api/kanallar/{id}`, `/api/ayarlar` ve `/api/gelenler` gövdesinde isteğe bağlı `surum`: istemcinin okuduğu sürüm. Kayıttakiyle uyuşmazsa 409 (`hata` iletisiyle) ve kayıt değişmez. Gelirde dönem ve kanalın satırı yoksa `0` gönderilir; yeni satır `1` ile eklenir, satırı görmeden `0` gönderen ikinci oturum 409 alır. POST'ta `surum` yok sayılır.
- `surum` göndermeyen eski istemci (2.3.0 masaüstü, önbellekteki eski web) denetlenmez: eski davranış (son yazan kazanır) sürer, sürüm yine artar. Yeni web ve masaüstü her düzenlemede gönderir; 409'da ileti gösterilir ve liste (gelirde dönemin güncel toplamı, ayarlarda güncel değerler) yenilenir.
- Aynı kaydı okuyan iki isteğin yazımı da sürümle koşullanır (eşzamanlılık belirteci): araya giren yazımdan sonraki kayıt 409 alır.

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
- GET `/api/surum`, GET `/api/yedek/durum`, POST `/api/yedek`: sürüm ve editöre özel yedek yönetimi. `/api/yedek/durum` sonda isteğe bağlı `sonGeriYukleme` (son geri yüklemenin anı) ve `geriYuklemeRaporu` (Türkçe maddeler: açılışta yapılanlar ve yapılması gerekenler) taşır; hiç geri yükleme olmadıysa ikisi de `null`. Eski istemci yok sayar. Geri yüklemeden sonraki ilk açılışta bütün oturumlar, tanıdık cihaz belirteçleri, kurtarma kodu ve cihaz bildirim kayıtları geçersiz olur.

`/api/cariler`, `/api/alis/tedarikciler`, `/api/tedarikciler/...`, `/api/hesaplar/...`, `/api/nakit-takvimi`, `/api/is-listesi` ve yeni kredi gerçekleşme uçları kaldırılmıştır. ERP12 ile senkronizasyon yoktur.
