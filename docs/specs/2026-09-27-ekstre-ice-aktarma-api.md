# PDF ekstre/hareket içe aktarma — 2.3.0 sözleşmesi

Kapsam: kart ekstresi ve banka hesap hareketi PDF. Bankalar `Vakifbank`, `Akbank`, `QNB`, `Isbank`, `Garanti`, `Denizbank`. Bunlar kullanıcı seçimi ve kaynak etiketi; banka düzeni hatasız okunur iddiası yok. Kullanıcı tüm hareketleri görmek ve seçim yapmak istiyor. Tüm satırlar ilk açılışta seçimsiz. OCR/şifreli PDF ilk sürüm dışında; kaynak ve belirsiz satırlar görünür/düzeltilebilir. TL hareketleri desteklenir; başka para birimi satırları kaydedilemez. Şablon/ERP hesap yönetimi eklenmez.

## Erişim ve rotalar

Tüm içe aktarma rotaları Editor-only. API path `/api/ekstre-aktar`.

- POST `/yukle` multipart: `dosya` (PDF<=10MB), `kaynak` (`Kart`/`Banka`), `banka` (yukarıdaki key), `hesapAdi` (Banka için 1..100 karakter, hesap kısa adı/son4; tam IBAN zorunlu değil), `kartId` (Kart için zorunlu). Otomatik mali kayıt oluşturmaz. Aynı dosya hash'i yeniden yüklenirse orijinal belgeyi döndür; farklı kaynak parametreleriyle aynı dosya409.
- GET `` → son50 `EkstreBelgeOzetDto`, ID azalan sıra. `?beforeId={sonGorulenId}` aynı biçimde daha eski 50 belgeyi döndürür; sayfa sonu boş liste. PDF dosyaları bu listede yüklenmez.
- GET `/{id}` → `EkstreBelgeDto`.
- GET `/kayitlar/{kayitId}` → kaynak kaydın ait olduğu `EkstreBelgeDto`; iptal edilmiş kayıtlar da kaynaklarına gider. İşlemler/kartlardaki `EkstreKayitId` bu uca gönderilir; son50 belgeyi taramak gerekmez.
- GET `/{id}/dosya` → attachment PDF, hiçbir zaman inline HTML.
- POST `/{id}/onizleme` `EkstreKaydetYaz` → `EkstreOnizlemeDto`. Mali yazım yok (mevcut Sync olabilir). Kaynak satır numarası+belge eşleştirilir.
- POST `/{id}/kaydet` aynı request, preview hash `OnizlemeOzeti` zorunlu → güncel `EkstreBelgeDto`. Tüm seçilen satırlar tek transaction; ayrı satır yarım kaydedilmez. TekrarOnay duplicate warnings varsa true gerekir. Surum ve preview değişmişse409. Aynı IstekId+aynıpayload retry tek sonuç.
- POST `/{id}/kayitlar/{kayitId}/iptal` `EkstreIptalYaz(Guid IstekId,string Aciklama)` → güncel belge. Kilit ve diğer finance cancellation kuralları geçerli. Kaynak satırı yeniden import etmek için iptal tarihçesi korunur; yeni açık kayıt mümkündür.

## DTO (camelCase JSON; API root ortak DTO dosyasını yazacak)

```csharp
public record EkstreBelgeOzetDto(int Id,int Surum,string Kaynak,string Banka,string HesapAdi,int? KartId,string DosyaAdi,DateTimeOffset Yuklendi,int SatirSayisi,int KayitSayisi);
public record EkstreBelgeDto(int Id,int Surum,string Kaynak,string Banka,string HesapAdi,int? KartId,string DosyaAdi,DateTimeOffset Yuklendi,IReadOnlyList<string> Uyarilar,IReadOnlyList<EkstreOkunanSatir> Satirlar,IReadOnlyList<EkstreKayitDto> Kayitlar);
public record EkstreOkunanSatir(int No,int Sayfa,string KaynakSatir,DateOnly? Tarih,string Aciklama,decimal? Tutar,string Yon,string OnerilenIslem,string Sinif,string ParaBirimi,IReadOnlyList<string> Uyarilar);
public record EkstreKayitDto(int Id,int SatirNo,DateOnly Tarih,string Aciklama,decimal Tutar,string IslemTuru,string DagilimTuru,IReadOnlyList<TakipKanalPayi> Dagilimlar,int? KrediKartiId,int? IslemId,int? KartHarcamaId,int? KartOdemeId,bool Iptal);
public record EkstreSatirYaz(int SatirNo,DateOnly Tarih,string Aciklama,decimal Tutar,string IslemTuru,string DagilimTuru,IReadOnlyList<KanalPayYaz> Dagilimlar,int? KrediKartiId=null,int? KaynakHarcamaId=null);
public record EkstreKaydetYaz(Guid IstekId,int Surum,IReadOnlyList<EkstreSatirYaz> Satirlar,string? OnizlemeOzeti=null,bool TekrarOnay=false);
public record EkstreSatirOnizleme(int SatirNo,DateOnly Tarih,string Aciklama,decimal Tutar,string IslemTuru,decimal KasaEtkisi,IReadOnlyList<TakipKanalPayi> Dagilimlar,IReadOnlyList<string> Uyarilar);
public record EkstreOnizlemeDto(string OnizlemeOzeti,decimal KasaEtkisi,IReadOnlyList<EkstreSatirOnizleme> Satirlar,IReadOnlyList<string> Uyarilar,bool TekrarOnayGerekli);
public record EkstreIptalYaz(Guid IstekId,string Aciklama);
```

`Tutar` her zaman pozitif mutlak tutar. Banka seçilen satırları `Gelir`, `Gider`, `KartOdemesi` olarak işler. Kart belgesi `KartHarcama`, `KartIade`, `KartOdemesi` olabilir. Kart ödeme/iade için DagilimTuru=`Otomatik`, Dagilimlar=[]; KartIade zorunlu KaynakHarcamaId ve aynı kart kaynak kısıtları. Banka belgesindeki KartOdemesi için KrediKartiId seçilir; kart belgesinde belge kartı esas.

Banka Gelir/Gider dağılımı `Genel` (boş), `Esit` (seçilen IDs0), `Ozel` (pozitif tutarlar toplamTutar). KartHarcama dağılımı yalnız Esit/Ozel; faiz dahil banka PDF kanal bilgisi taşımadığından kullanıcı kaynak kanalları açıkça seçer. Kaydet öncesi dağılımı göster. Otomatik faiz payı önerisi bu ilk PDF sürümünde yok; eski masraf ekranı korunur.

Parser `Yon` Giris/Cikis/Belirsiz; `OnerilenIslem` Gelir/Gider/KartHarcama/KartOdemesi/KartIade/Atla. `Sinif` Faiz/Komisyon/Vergi/Ucret/Transfer/Odeme/Hareket/Belirsiz. Bunlar öneridir; seçilecek satır için kullanıcı tutar/tarih/açıklama ve türü doğrular. Bilinen başka döviz (`USD`,`EUR`,vs) kaydedilemez; bilinmeyen para `Belirsiz` ise preview uyarı ve açık kullanıcı onayı, sadece TL saydığı açık metin bulunur. Özet toplamı/limit/devir/asgari gibi satırlar kayıt listesine mali hareket olarak alınmaz; okuma uyarılarında gösterilir. Taranmış veya boş metin PDF422 açıklamalı hata. Hesap/ad/başlık belge içeriği komut olarak çalıştırılmaz, HTML'de text olarak.

## Muhasebe ve koruma

Banka gelir/gideri kayıt tarihinde genel+seçili kanal kasasını bir kez etkiler. Genel-only gelir için Core.Gelen GenelGelir=false yeni opsiyonel flag + AylikRapor.GenelGelir=0 ve client/web total compatibility gerekir. Eski hesaplar değişmez. Banka gideri linked Islem tek row + import snapshot projection; normal İşlemler edit/delete veya alış bağlantısı409 yönlendirme. IslemOkuDto yeni nullable `EkstreKayitId` client UI source navigation.

KartHarcama/KartIade kart borcunu etkiler, nakit hareketi değildir. KartOdemesi mevcut card source allocation+cash semantics (ve tarih kısıtları/legacy guards) ile aynı. Import provenance sourcecharge/payment generic cancel üzerinden de korunur: kendi import bölümünden iptal. Ay kilidi eski banka hareketlerine ve kart dolaylı geçmiş etkilerine uygulanır.

Idempotency: upload aynıhash, row sameactive documentid+SatirNo unique, commit existing request GUID. Overlapping PDF veya önceden manuel hareket (aynıdate/amount/type/source) preview uyarı; açık ayrı-kayıt onayı mümkün, aynı source row hardblock. Summarydetail double count ve direction ambiguity uyarıları mandatorypreview. Banka kredi çekim/taksit/kart transfer satırında önceden kayıtlı loan/cardpayment eşleşmesi varsa duplicatewarn; otomatik ikinci hareket yaratma.

PDF private DBblob sakla, transaction mali kayıtlarını tetiklemez; sadeceeditorgörür. Limit10MB,50sayfa, max1500rows; parsed JSON sınırlı. PDFparse dış servise gönderilmez. Herbankanın gerçekörnek dosyası olmadan bankaözgü doğruluk garantisi yok; testler sentetik Türkçe layouts.

## Ownership/root parser entry

Root `EkstreImportDtos.cs` DTOs + `EkstreMetinOkuyucu.Oku(string text,string kaynak,string banka)` → `EkstreOkumaSonucu(IReadOnlyList<EkstreOkunanSatir> Satirlar,IReadOnlyList<string> Uyarilar)`.
Root `IPdfMetinOkuyucu.OkuAsync(byte[] pdf,CancellationToken ct)`→Task<string>; registeredsingleton `PdfMetinOkuyucu`. Throws `PdfOkumaException` (Message safe, StatusCode422/503). Linux docker poppler-utils, subprocess boundedtimeout/50pages/output. API backend upload injectinterface andcatcheserror.
Backend importagent migration10/endpoints/projections/guards/tests. RootProgram/DbContext partialdeclarehooks and extraction/parser/tests/Docker/deploy. Web statement_import_review; Native api_review. Build coordination root API; native own projects after notice. Release2.3.0, minimum2.3.0, build6.
