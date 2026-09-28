# Aylık giderler ve ay kilidi API

JSON camelCase. Okumalar Finans (editör/izleyici), bütün değişiklikler Editor. Alıcı erişemez. Paralar iki ondalık, tarihler YYYY-MM-DD. Şablon değişiklikleri geçerlilik ayından itibaren planı değiştirir; eski aylara ve kaydedilmiş ödeme kopyasına dokunmaz. Ödeme yalnız kullanıcı kaydedince oluşur; otomatik tahsilat yok.

## Aylık giderler

- GET `/api/aylik-giderler/sablonlar` → `AylikGiderSablonDto[]`
- POST `/api/aylik-giderler/sablonlar` → `AylikGiderSablonDto`
- PUT `/api/aylik-giderler/sablonlar/{id}` → aynı DTO
- GET `/api/aylik-giderler?yil=2026&ay=9` → `AylikGiderAyDto`
- POST `/api/aylik-giderler/{sablonId}/ode` → `AylikGiderSatirDto`
- POST `/api/aylik-giderler/odemeler/{odemeId}/iptal` → `AylikGiderSatirDto`

`AylikGiderSablonYaz(Guid IstekId, int Surum, string Ad, string Tur, decimal Tutar, int OdemeGunu, string DagilimTuru, IReadOnlyList<KanalPayYaz> Dagilimlar, DateOnly GecerliAy, bool Aktif = true)`

- Tur: `Kira`, `Maas`, `Fatura`, `Diger`.
- DagilimTuru: `Genel`, `Esit`, `Ozel`. Genel için dağılımlar boş. Esit için seçilen kanal kimlikleri ve sıfır tutarlar gönderilir; tutarlar sunucuda kuruş kaybetmeden eşit hesaplanır. Ozel için pozitif tutarlar toplamı gider tutarına eşittir. Dağılım sonradan eklenen kanallara otomatik yayılmaz.
- GecerliAy ayın ilk günüdür. Yeni şablon/şablon değişikliği cari ay veya geleceğe uygulanabilir; geçmiş ay şablonu değiştirilmez. Yeni kayıtta Surum=0. OdemeGunu=1..31; kısa ayda ayın son günü kullanılır.

`AylikGiderSablonDto(int Id, int Surum, string Ad, string Tur, decimal Tutar, int OdemeGunu, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, DateOnly GecerliAy, bool Aktif)`

`AylikGiderAyDto(int Yil, int Ay, decimal PlanlananToplam, decimal OdenenToplam, IReadOnlyList<AylikGiderSatirDto> Kayitlar)`

`AylikGiderSatirDto(int SablonId, int SablonSurum, string Ad, string Tur, decimal Tutar, DateOnly PlanlananTarih, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, string Durum, int? OdemeId = null, DateOnly? OdemeTarihi = null, int? IslemId = null)`

- Durum: `Planlandi`, `Odendi`, `Iptal`. Ay listesi aktif ödemesi olmayan aktif planı Planlandi gösterir; iptal yanıtı Iptal olur, ay listesi planı yeniden ödenebilir gösterir. Ödenmiş kayıtta ad/tutar/paylar ödeme anındaki sabit kopyadır.
- Aylık ödeme girdisi: `AylikGiderOdemeYaz(Guid IstekId, int Surum, int Yil, int Ay, DateOnly Tarih, string? Not = null)`. Surum listede gelen SablonSurum. Tutar/paylar şablonun o ayki sürümünden alınır. Aynı ay+şablon ikinci ödeme üretmez; aynı anahtar tekrarı aynı sonucu verir. Farklı anahtarlı tekrar 409 ve mevcut ödeme mesajı verir.
- İptal girdisi: `AylikGiderIptalYaz(Guid IstekId, string Aciklama)`. Geçmiş kapalı dönemde iptal 409. İptal kasayı geri alır, iz bırakır; aynı ay için yeni anahtarla yeniden ödeme yapılabilir.
- Ödeme nakit/havale niteliğindedir; kart alanı yok. Genel dağılım yalnız genel kasayı; diğer dağılımlar genel kasayı bir kez ve kanalları kendi payıyla azaltır. Hareket İşlemler'de de görünür. Oradan düzenleme/silme 409: düzeltme aylık gider bölümünde iptal + yeniden ödeme ile yapılır.

## Ay kilidi

- GET `/api/ay-kilidi` → `AyKilidiDto`
- POST `/api/ay-kilidi/kapat` → aynı DTO
- POST `/api/ay-kilidi/ac` → aynı DTO

`AyKilidiYaz(Guid IstekId, int Surum, int Yil, int Ay, string Aciklama)`

`AyKilidiDto(int Surum, DateOnly? KilitliSonTarih, IReadOnlyList<AyKilidiOlayDto> Gecmis)`

`AyKilidiOlayDto(int Id, DateOnly? OncekiSonTarih, DateOnly? YeniSonTarih, string Aciklama, DateTimeOffset Zaman)`

Kapat yalnız tamamlanmış ayı kabul eder; o ayın sonuna kadar bütün geçmiş mali etkiler korunur. Ac seçilen ayı ve sonrasını açar (kilit sınırı önceki ayın sonuna iner). Açıklama zorunludur. Sürüm uyuşmazlığı 409. İstek kimliği yeniden gönderimi güvenlidir. Ekranda tarih sınırı ve açma işleminin seçilen ayla birlikte sonraki ayları da açtığı açıkça gösterilmelidir. Finansal geçmişe dokunan diğer API'ler kilitte 409 döner; gelecek planlar ve okumalar çalışır.

## Kasa kontrolü ve kasa hareket dökümü

Kasa kontrolü genel kasanın gerçek sayımla karşılaştırmasıdır; kasaya hiçbir şey yazmaz (gap-denetim-izi-gozlemlenebilirlik-3, gap-coklu-giris-cift-sayim-mutabakat-17).

- GET `/api/kasa-kontrol` (Finans) → en yeni 50 `KasaKontrolDto`
- POST `/api/kasa-kontrol/onizleme` (Editor) → `KasaKontrolOnizlemeDto`
- POST `/api/kasa-kontrol` (Editor) → `KasaKontrolDto`
- PUT `/api/kasa-kontrol/{id}/aciklama` (Editor) → `KasaKontrolDto`
- GET `/api/kasa-kontrol/{id}/sonrasi` (Editor) → `KasaKontrolSonrasiDto`
- GET `/api/kasa-hareketleri?baslangic=&bitis=&kanalId=` (Finans) → `KasaHareketleriDto`

`KasaKontrolOnizle(decimal GercekBakiye, string? Not = null)`, `KasaKontrolYaz(Guid IstekId, decimal GercekBakiye, string KontrolOzeti, string? Not = null)`

`KasaKontrolOnizlemeDto(decimal SistemBakiye, decimal GercekBakiye, decimal Fark, string KontrolOzeti, IReadOnlyList<KasaKontrolKanalDto>? KanalBakiyeleri = null, DateOnly? HesapTarihi = null)`

`KasaKontrolDto(int Id, DateTimeOffset Kaydedildi, decimal SistemBakiye, decimal GercekBakiye, decimal Fark, string? Not, int Surum = 1, DateOnly? HesapTarihi = null, IReadOnlyList<KasaKontrolKanalDto>? KanalBakiyeleri = null, string? FarkAciklamasi = null, DateTimeOffset? FarkAciklamaZamani = null, decimal? GuncelSistemBakiye = null, decimal? GuncelFark = null, bool SonradanDegisti = false)`

`KasaKontrolKanalDto(int? KanalId, string Kanal, decimal Bakiye, decimal? GuncelBakiye = null)`

- Özet (KontrolOzeti) genel kasayı, gerçek bakiyeyi ve kanal kasalarını bağlar; açıklamayı bağlamaz. Kayıtta bunlardan biri değiştiyse 409 (yeniden karşılaştırın). Fark sıfırdan farklıysa `Not` zorunludur: 400, alan `not`, "Fark varsa açıklama girin."; açıklama önizlemeden sonra yazılabilir.
- Filigran: kayıt, panelle aynı yazma transaction'ında hesap gününü (`HesapTarihi`), kanal kasalarını ve o anki en büyük gider (Islemler), mali istek (FinansIstekler) ve denetim olayı kimliklerini saklar. `KasaKontrolFiligrani` migration'ından önceki kayıtlarda filigran boştur; istemciler "eski kayıt, filigran yok" gösterir.
- Listede `GuncelSistemBakiye` ve kanalların `GuncelBakiye`'si kaydın günü (eski kayıtta kayıt anının İstanbul günü) için bugünkü veriyle yeniden hesaplanır; `GuncelFark = GercekBakiye − GuncelSistemBakiye`, `SonradanDegisti = GuncelSistemBakiye ≠ SistemBakiye`. Kayıtlı değerler hiç değişmez. Kayıt ve açıklama yanıtlarında bu üç alan boştur.
- Açıklama: `KasaKontrolAciklamaYaz(Guid IstekId, int Surum, string Aciklama)`; açıklama zorunlu, en çok 2000 karakter. Yalnız `FarkAciklamasi`/`FarkAciklamaZamani` yazılır, `Surum` artar; eski sürüm 409 "Kontrol kaydı değişmiş. Yenileyin."; aynı istek kimliği aynı sonucu verir.

`KasaKontrolSonrasiDto(int KontrolId, DateTimeOffset Kaydedildi, DateOnly EsasTarih, bool FiligranVar, decimal SistemBakiye, decimal GuncelSistemBakiye, decimal BugunkuSistemBakiye, IReadOnlyList<DenetimOlayDto> Degisiklikler, IReadOnlyList<KasaKontrolIstekDto> Istekler, IReadOnlyList<KasaHareketiDto> Hareketler, bool Kirpildi)`, `KasaKontrolIstekDto(Guid IstekId, string Tur, int SonucId)`

- `GuncelSistemBakiye − SistemBakiye` kontrol gününe kadarki geriye dönük değişim, `BugunkuSistemBakiye − GuncelSistemBakiye` kontrol gününden bugüne kasaya işleyen hareketlerin toplamıdır.
- `Degisiklikler`: filigrandaki denetim olayından sonraki olaylar (oturum/güvenlik, kasa kontrolü, kanal alt sınırı ve alıcı hesabı olayları hariç); eski kayıtta kayıt anından sonrakiler. `Istekler`: filigrandan sonraki mali istekler (kasa kontrolünün kendi istekleri hariç; eski kayıtta boş). `Hareketler`: etki tarihi kontrol gününden sonra olan döküm satırları ve kontrolden sonra girilip kontrol gününe ya da öncesine düşen giderler. Her liste en çok 500 öğedir (en eskiler); aşılırsa `Kirpildi`.

`KasaHareketleriDto(DateOnly Baslangic, DateOnly Bitis, int? KanalId, string? Kanal, decimal AcilisBakiyesi, decimal KapanisBakiyesi, IReadOnlyList<KasaHareketiDto> Hareketler)`

`KasaHareketiDto(DateOnly EtkiTarihi, DateOnly KayitTarihi, string Tur, string Aciklama, string Kanal, int? KanalId, decimal GenelKasaEtkisi, decimal KanalEtkisi, string? KaynakAnahtari, bool Otomatik)`

- Döküm panelle aynı yükten ve haftalık hesabın kurallarıyla üretilir (hesap yolu değişmez); rapor ve panel sonuçları aynıdır. Değişmez: açılış + Σ genel kasa etkisi = panelin genel kasası; kanal seçilince açılış + Σ kanal etkisi = panelin kanal kasası.
- Varsayılan aralık bitişin ayının başından bitişe; bitiş en geç bugündür (gelecek tarihli hareket kasaya girmemiştir); aralık en çok 366 gün; bilinmeyen kanal 400.
- `Tur`: Gelir (dönem geliri, etki tarihi dönem başı), EkstreGeliri, EkGelir, KrediCekimi, Gider, SabitGider, AylikGider, KartOdemesi, KartIadesi (önceden sayılan kart borcunun kasaya dönüşü), KrediTaksidi (eski kredinin türetilmiş ya da takipli kredinin taksidi), KartAySonu (eski kartın harcaması etki ayının son günü düşer; `KayitTarihi` harcama günü). `Otomatik`: yazma olmadan tarihinde işleyen etki (KrediTaksidi, KartAySonu).
- `KaynakAnahtari` denetim izindeki varlık ve kimliktir ("Islem:812", "TakipKartOdeme:44", "TakipKrediTaksit:9", "Kredi:5", "EkstreKayit:9", "HesapHareket:3", "Gelen:17", "TakipHarcama:12"). Kanal etiketi gerçek kanal değilse (Ortak, Dağılım bekliyor, Genel kasa) `KanalId` null ve kanal etkisi 0'dır.
