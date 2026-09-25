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
