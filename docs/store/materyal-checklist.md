# Emar Kasa — Mağaza Materyal Checklist

Her iki mağaza için gerekli görsellerin ve metin materyallerinin kontrol listesi.
Hazır olanları `[x]` ile işaretle; placeholder değerler `<...>` ile belirtilmiştir.

---

## Uygulama İkonu

| Platform | Boyut | Format | Durum |
|---|---|---|---|
| App Store (iOS) | **1024 × 1024 px** | PNG, alfa katmanı YOK | [ ] |
| Google Play (Android) | **512 × 512 px** | PNG (32-bit) | [ ] |

> Kaynak dosya: `Kasa.App/Resources/AppIcon/appicon.svg` — vektör olduğu için her iki boyuta ihraç edilebilir.
> Arka plan rengi: `#512BD4` (csproj'da tanımlı). Mağaza ikonunun markayla uyumlu olup olmadığını onaylayın.

---

## Ekran Görüntüleri — iOS (App Store)

Apple, her cihaz sınıfı için ayrı ekran görüntüsü ister.
**Zorunlu:** 6,5" (iPhone 14 Pro Max / 15 Plus) — diğerleri bu kümeden otomatik ölçeklenir.

| Cihaz | Çözünürlük | Adet | Durum |
|---|---|---|---|
| 6,5" iPhone (zorunlu) | 1284 × 2778 px | min 1, max 10 | [ ] |
| 5,5" iPhone (önerilen) | 1242 × 2208 px | min 1, max 10 | [ ] |
| 12,9" iPad Pro (gerekiyorsa) | 2048 × 2732 px | min 1, max 10 | [ ] |

**Önerilen ekranlar (öncelik sırasıyla):**
1. Panel ekranı (kasa bakiyesi + hafta/ay sonucu)
2. Haftalık özet listesi
3. Kredi kartları ekranı
4. Cariler listesi
5. İşlem defteri

---

## Ekran Görüntüleri — Android (Google Play)

| Tür | Min Boyut | Maks Boyut | Adet | Durum |
|---|---|---|---|---|
| Telefon ekranı | 320 × 568 px | 3840 × 3840 px | min 2, max 8 | [ ] |
| 7" tablet (isteğe bağlı) | 320 × 568 px | 3840 × 3840 px | min 1, max 8 | [ ] |

---

## Tanıtım Görseli (Feature Graphic) — Yalnızca Google Play

| Alan | Değer |
|---|---|
| Boyut | **1024 × 500 px** |
| Format | JPG veya PNG |
| Durum | [ ] |

---

## Metin Materyalleri

| Alan | Değer / Durum |
|---|---|
| Uygulama adı | **Emar Kasa** — hazır |
| Kısa açıklama (~80 kar.) | `docs/store/aciklama-tr.md` — hazır |
| Tam açıklama | `docs/store/aciklama-tr.md` — hazır |
| Anahtar kelimeler (App Store) | `docs/store/anahtar-kelimeler.md` — hazır |
| Kategori | Finance / Finans — hazır |
| Sürüm notları (v1.0) | `<İlk sürüm>` — hazırlanacak |

---

## URL'ler ve İletişim

| Alan | Değer | Durum |
|---|---|---|
| Destek URL | `<https://kasa.emarglobal.com>` veya ayrı destek sayfası | [ ] hazırlanacak |
| Pazarlama URL | `<https://kasa.emarglobal.com>` | [ ] hazırlanacak |
| Gizlilik Politikası URL | `<hazırlanacak>` — mevcut web sitesinde gizlilik/veri-silme sayfası **tespit edilmedi**; mağaza yayınından önce oluşturulup yayınlanması **zorunludur** | [ ] |
| Destek e-postası | `<destek@royalmezat.com veya benzer>` | [ ] hazırlanacak |

> **Not — Gizlilik Politikası:** App Store ve Google Play, canlı erişilebilir bir gizlilik politikası URL'i olmadan uygulamayı yayınlamaz. Kasa web sitesinde (`kasa.emarglobal.com`) şu an bir gizlilik sayfası bulunmamaktadır. Yayından önce bir sayfa hazırlanmalı ve bu alanda URL belirtilmelidir.

---

## Apple'a Özgü Ek Bilgiler

| Alan | Değer | Durum |
|---|---|---|
| App Privacy (Nutrition Label) | Kimlik doğrulama verisi (kullanıcı adı/şifre) | [ ] App Store Connect'te doldur |
| Şifreleme Beyanı | `ITSAppUsesNonExemptEncryption = NO` | [ ] `Info.plist`'e ekle (bkz. `docs/deploy/ios-yayin.md §7`) |
| Yaş Derecelendirmesi | 4+ (beklenen) | [ ] App Store Connect anketini doldur |
| Copyright | `<© 2026 Emar Global Ltd.>` | [ ] hazırlanacak |

---

## Google Play'e Özgü Ek Bilgiler

| Alan | Değer | Durum |
|---|---|---|
| İçerik Derecelendirmesi | IARC anketi (Play Console içinde) | [ ] |
| Veri Güvenliği Bölümü | Kullanıcı adı/şifre toplandığı beyan edilmeli | [ ] |
| Hedef Kitle | 18+ (kurumsal kullanım) | [ ] |

---

## Yayın Öncesi Son Kontrol

- [ ] İkon onaylandı (marka renkleriyle uyumlu)
- [ ] Ekran görüntüleri gerçek uygulamayı yansıtıyor (test verisi değil, makul örnek veri)
- [ ] Gizlilik politikası URL'i canlıda erişilebilir
- [ ] `ApplicationDisplayVersion` ve `ApplicationVersion` (csproj) artırıldı
- [ ] TestFlight veya iç test tamamlandı
- [ ] App Store Connect: tüm alanlar dolduruldu, "Ready for Review" durumuna getirildi
- [ ] Son "Submit for Review" tıklaması: **kullanıcı** yapar
