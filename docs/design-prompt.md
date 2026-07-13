# Kasa Defteri — UI Tasarım Promptu

> Bunu Claude/design (veya başka bir UI tasarım aracına) kopyala-yapıştır ver.
> Amaç: sadece görsel arayüz tasarımı (ekranlar, düzen, bileşenler). Backend
> mantığı bu promptun kapsamı değil — sadece hangi verinin nerede göründüğü.

---

## Ne tasarlıyoruz

Küçük bir işletmenin **haftalık nakit akış / kasa defteri** web uygulaması. Türkçe
arayüz, tüm tutarlar **TL** (binlik ayraç nokta, kuruş virgül: `1.308.800,00`).
İşletmenin **3 gelir kanalı** var: **MEZAT**, **PERAKENDE**, **TOPTAN** (ileride
kanal eklenebilir). Ayrıca kanala ait olmayan "**Ortak**" giderler var.

**Kullanıcılar:**
- **Editör** (muhasebeyi tutan ortak): masaüstünde veri girer.
- **İzleyici** (5-6 ortak): sadece görüntüler; ağırlıkla **mobil**.
- Kural: **mobilde düzenleme yok**, herkes sadece görür. Düzenleme sadece geniş
  ekranda (PC).

**Tarz:** temiz, sakin, "finansal pano" hissi. Okunabilir sayılar öne çıksın.
Pozitif tutarlar nötr/yeşil, negatif tutarlar kırmızı. Aşırı süs yok; net tablo
ve kartlar. Açık tema; koyu tema opsiyonel. Responsive (mobil-öncelikli panel).

## Temel kavramlar (tasarımı etkileyen)

- **Kanal:** MEZAT/PERAKENDE/TOPTAN. Her yerde renk/etiketle ayrışsın.
- **Dönem:** haftalık devir birimi. Haftalar Pazartesi başlar; ay sonunda hafta
  ikiye bölünebilir (ör. `29–30 Haz`, `1–5 Tem`). Tarih aralığı olarak gösterilir.
- **Gelen:** haftalık, kanal başına tek rakam.
- **Giden:** kalem kalem işlem — alanları: tarih, cari (kişi/firma adı), tutar,
  kanal (veya Ortak), **tip** (Cari / Sabit Gider / Kredi Kartı), not.
- **Kanal devri:** her kanalın kümülatif bakiyesi (kanal kârlılığı).
- **Kasa devri:** toplam nakit pozisyonu.
- **AY SONUCU:** kanal başına aylık kâr göstergesi.

## Ekranlar

### 1. İşlem + Defter (editör, masaüstü)
- Üstte **hızlı giriş formu**: Tarih · Cari (yazarken öneren otomatik tamamlama) ·
  Tutar (TL) · Kanal (dropdown: MEZAT/PERAKENDE/TOPTAN/Ortak) · Tip (Cari/Sabit
  Gider/Kredi Kartı) · Not · [Ekle].
- Altta **işlem tablosu**: tarih, cari, tutar, kanal (renkli etiket), tip, not,
  satır düzenle/sil. Üstte filtre çubuğu: cari ara · kanal · tip · tarih aralığı.
- Ayrı bir bölüm/sekme: **Gelen girişi** — dönem seç → 3 kanal için tutar kutusu
  → kaydet. (Ay sonunu geçen haftada iki dönem satırı olabilir.)

### 2. Haftalık Özet
- Excel'deki "sarı blok"un modern hâli. Dönem seçilir veya liste hâlinde akar.
- Her dönem için bir kart/tablo bloğu:
  - Kanal satırları (MEZAT/PERAKENDE/TOPTAN): **Gelen · Giden · Sonuç · Kanal Devri**
  - Toplam satırı: **Toplam Gelen · Toplam Giden · Kasa Sonucu · Kasa Devri**
- Negatif sonuç/devir kırmızı, pozitif yeşil. Devir sütunu vurgulu.

### 3. Aylık Rapor
- Ay seçici (ör. Haziran 2026).
- Kanal başına satırlar, sütunlar: **Aylık Gelen · Cari Giden · Sabit Gider ·
  Kredi Kartı · Ortak Pay · AY SONUCU**. Toplam satırı.
- Altında **kanal AY SONUCU trend grafiği** (aylara göre çizgi/sütun, her kanal
  bir seri).

### 4. Ortak Paneli (mobil, salt-görüntüleme) — EN ÖNEMLİ EKRAN
- Mobil-öncelikli. Ortakların tek bakışta gördüğü özet. Öncelik sırası:
  1. **Güncel Kasa** — büyük, öne çıkan rakam (toplam nakit devri).
  2. **Kanal kümülatif bakiyeleri** — 3 kanal kartı (MEZAT/PERAKENDE/TOPTAN),
     her birinde kümülatif devir + bu hafta sonucu.
  3. **Bu ay** — kanal AY SONUCU'ları özet.
  4. **Trend** — basit grafik (kasa veya kanal kârlılığı zaman içinde).
- Sade, kaydırılabilir kart akışı. Düzenleme butonu YOK.

### 5. Cari Yönetimi (editör)
- Cari (firma/kişi) listesi: ekle, düzenle, **birleştir** (yanlış yazımları tek
  ele), pasifleştir. Arama.

### 6. Ayarlar (editör)
- **Kanallar:** liste — ekle / yeniden adlandır / pasifleştir; her kanal için
  **açılış devri** girişi.
- **Kasa açılış devri**, **takip başlangıç tarihi**, **hafta başlangıç günü**.
- **İzleyici şifresi** ayarı.
- **Excel/CSV dışa aktarma** butonu.

## Giriş ekranı
- Basit giriş: editör (kullanıcı+şifre) veya izleyici (tek ortak şifre). Temiz,
  markasız-sade bir kart.

## İstenen çıktı
- Yukarıdaki 6 ekran + giriş ekranı için yüksek kaliteli, responsive UI tasarımı.
- Türkçe metinler, TL para formatı, kanal renk sistemi, pozitif/negatif renk
  kuralı.
- Mobil panel (ekran 4) ve masaüstü defter (ekran 1) özellikle özenli olsun.
