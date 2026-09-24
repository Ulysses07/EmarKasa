# Emar Kasa — Üretim DB Yeniden Oluşturma (GEÇERSİZ)

> **Bu kılavuz kaldırıldı. Buradaki eski adımları uygulamayın.**

Eski sürümdeki adımlar tehlikeliydi:
- Çalışan bir WAL veritabanını `cp kasa.db` ile yedeklemek son yazmaları kaybettirir.
  Denemede son 60 kaydın hiçbiri kopyada yoktu.
- Canlı DB'yi silip boş şemayla yeniden başlatmayı öneriyordu.
- Konteyner içinde `sqlite3` ile elle tablo oluşturmayı anlatıyordu.

Artık bunların hiçbiri gerekmiyor:
- **Şema:** Yeni tablo, sütun ve index'ler uygulama açılırken `SemaGuncelleyici` tarafından
  otomatik eklenir. `EnsureCreated()`'ın mevcut DB'ye tablo eklememesi sorunu bu yüzden
  ortadan kalktı. Şema değişmeden önce uygulama `yedek/kasa-once-<zaman>.db` yedeğini alır.
- **Yedek alma, geri yükleme, güncelleme:** `deploy/README.md` içindeki şu bölümlere bakın:
  "Güncelleme (yeni sürüm)" (elle yedek adımı dahil), "Yedek" ve "Geri yükleme".
