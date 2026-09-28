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
- POST `/{id}/kayitlar/{kayitId}/iptal` `EkstreIptalYaz(Guid IstekId,string Aciklama)` → güncel belge. Kilit ve diğer finance cancellation kuralları geçerli. Kaynak satırı yeniden import etmek için iptal tarihçesi korunur; yeni açık kayıt mümkündür. Eşleştirme satırının iptali yalnız bağı kaldırır (hiçbir kayıt, kasa ya da kart borcu değişmez). Kaydı alış ödemesine bağlanmış satır, ödeme alıştan ayrılmadan iptal edilemez (409, alış numarasıyla).
- POST `/{id}/eslesme-adaylari` `EkstreEslesmeAdayiSorgu(DateOnly Tarih,decimal Tutar)` → `IReadOnlyList<EkstreEslesmeAdayiDto>`. Salt okunur (anlık görüntü, yazma kilidi yok). Satırın eşleştirilebileceği mevcut kayıtlar; kural ve kaynaklar aşağıda ("Mevcut kayıtla eşleştirme").

## DTO (camelCase JSON; API root ortak DTO dosyasını yazacak)

```csharp
public record EkstreBelgeOzetDto(int Id,int Surum,string Kaynak,string Banka,string HesapAdi,int? KartId,string DosyaAdi,DateTimeOffset Yuklendi,int SatirSayisi,int KayitSayisi);
public record EkstreBelgeDto(int Id,int Surum,string Kaynak,string Banka,string HesapAdi,int? KartId,string DosyaAdi,DateTimeOffset Yuklendi,IReadOnlyList<string> Uyarilar,IReadOnlyList<EkstreOkunanSatir> Satirlar,IReadOnlyList<EkstreKayitDto> Kayitlar);
public record EkstreOkunanSatir(int No,int Sayfa,string KaynakSatir,DateOnly? Tarih,string Aciklama,decimal? Tutar,string Yon,string OnerilenIslem,string Sinif,string ParaBirimi,IReadOnlyList<string> Uyarilar);
public record EkstreKayitDto(int Id,int SatirNo,DateOnly Tarih,string Aciklama,decimal Tutar,string IslemTuru,string DagilimTuru,IReadOnlyList<TakipKanalPayi> Dagilimlar,int? KrediKartiId,int? IslemId,int? KartHarcamaId,int? KartOdemeId,bool Iptal,string? IptalAciklamasi=null,DateTimeOffset? IptalZamani=null,string? EslesmeTuru=null,int? EslesmeId=null,string? EslesmeDurumu=null);
public record EkstreSatirYaz(int SatirNo,DateOnly Tarih,string Aciklama,decimal Tutar,string IslemTuru,string DagilimTuru,IReadOnlyList<KanalPayYaz> Dagilimlar,int? KrediKartiId=null,int? KaynakHarcamaId=null,string? EslesenKayitTuru=null,int? EslesenKayitId=null);
public record EkstreEslesmeAdayiSorgu(DateOnly Tarih,decimal Tutar);
public record EkstreEslesmeAdayiDto(string Tur,int Id,DateOnly Tarih,decimal Tutar,string Aciklama,int? KrediKartiId,string? KanalEtiketi=null,int? AlisId=null,int? EkstreKayitId=null,int? HarcamaId=null,int? TaksitNo=null,int? TaksitSayisi=null);
public record EkstreKaydetYaz(Guid IstekId,int Surum,IReadOnlyList<EkstreSatirYaz> Satirlar,string? OnizlemeOzeti=null,bool TekrarOnay=false);
public record EkstreSatirOnizleme(int SatirNo,DateOnly Tarih,string Aciklama,decimal Tutar,string IslemTuru,decimal KasaEtkisi,IReadOnlyList<TakipKanalPayi> Dagilimlar,IReadOnlyList<string> Uyarilar);
public record EkstreOnizlemeDto(string OnizlemeOzeti,decimal KasaEtkisi,IReadOnlyList<EkstreSatirOnizleme> Satirlar,IReadOnlyList<string> Uyarilar,bool TekrarOnayGerekli);
public record EkstreIptalYaz(Guid IstekId,string Aciklama);
```

`Tutar` her zaman pozitif mutlak tutar. Banka seçilen satırları `Gelir`, `Gider`, `KartOdemesi` ya da `Eslestir` olarak işler. Kart belgesi `KartHarcama`, `KartIade`, `KartOdemesi` ya da `Eslestir` olabilir. Kart ödeme/iade için DagilimTuru=`Otomatik`, Dagilimlar=[]; KartIade zorunlu KaynakHarcamaId ve aynı kart kaynak kısıtları. Banka belgesindeki KartOdemesi için KrediKartiId seçilir; kart belgesinde belge kartı esas.

Banka Gelir/Gider dağılımı `Genel` (boş), `Esit` (seçilen IDs0), `Ozel` (pozitif tutarlar toplamTutar). KartHarcama dağılımı yalnız Esit/Ozel; faiz dahil banka PDF kanal bilgisi taşımadığından kullanıcı kaynak kanalları açıkça seçer. Kaydet öncesi dağılımı göster. Otomatik faiz payı önerisi bu ilk PDF sürümünde yok; eski masraf ekranı korunur.

Parser `Yon` Giris/Cikis/Belirsiz; `OnerilenIslem` Gelir/Gider/KartHarcama/KartOdemesi/KartIade/Atla. `Sinif` Faiz/Komisyon/Vergi/Ucret/Transfer/Odeme/Hareket/Belirsiz. Bunlar öneridir; seçilecek satır için kullanıcı tutar/tarih/açıklama ve türü doğrular. Bilinen başka döviz (`USD`,`EUR`,vs) kaydedilemez; bilinmeyen para `Belirsiz` ise preview uyarı ve açık kullanıcı onayı, sadece TL saydığı açık metin bulunur. Özet toplamı/limit/devir/asgari gibi satırlar kayıt listesine mali hareket olarak alınmaz; okuma uyarılarında gösterilir. Taranmış veya boş metin PDF422 açıklamalı hata. Hesap/ad/başlık belge içeriği komut olarak çalıştırılmaz, HTML'de text olarak.

## Muhasebe ve koruma

Banka gelir/gideri kayıt tarihinde genel+seçili kanal kasasını bir kez etkiler. Genel-only gelir için Core.Gelen GenelGelir=false yeni opsiyonel flag + AylikRapor.GenelGelir=0 ve client/web total compatibility gerekir. Eski hesaplar değişmez. Banka gideri linked Islem tek row + import snapshot projection; normal İşlemler edit/delete veya alış bağlantısı409 yönlendirme. IslemOkuDto yeni nullable `EkstreKayitId` client UI source navigation.

KartHarcama/KartIade kart borcunu etkiler, nakit hareketi değildir. KartOdemesi mevcut card source allocation+cash semantics (ve tarih kısıtları/legacy guards) ile aynı. Import provenance sourcecharge/payment generic cancel üzerinden de korunur: kendi import bölümünden iptal. Ay kilidi eski banka hareketlerine ve kart dolaylı geçmiş etkilerine uygulanır.

Idempotency: upload aynıhash, row sameactive documentid+SatirNo unique, commit existing request GUID. Overlapping PDF veya önceden manuel hareket (aynı tutar, en çok 3 gün farklı tarih; `BenzerKayitServisi`, gelir dahil) preview uyarı; açık ayrı-kayıt onayı mümkün, aynı hareketse satır `Eslestir` ile mevcut kayda bağlanır; aynı source row hardblock. Summarydetail double count ve direction ambiguity uyarıları mandatorypreview. Banka kredi çekim/taksit/kart transfer satırında önceden kayıtlı loan/cardpayment eşleşmesi varsa duplicatewarn; otomatik ikinci hareket yaratma.

PDF private DBblob sakla, transaction mali kayıtlarını tetiklemez; sadeceeditorgörür. Limit10MB,50sayfa, max1500rows; parsed JSON sınırlı. PDFparse dış servise gönderilmez. Herbankanın gerçekörnek dosyası olmadan bankaözgü doğruluk garantisi yok; testler sentetik Türkçe layouts.

## Mevcut kayıtla eşleştirme (gap-coklu-giris-cift-sayim-mutabakat-1)

Aynı para birden çok yoldan girilebilir (kartlı alış ödemesi ve kart ekstresi, elle gider ve banka hareketi). Ekstre satırı yeni kayıt üretmeden mevcut kayda bağlanabilir; kayıt sırası ne olursa olsun para bir kez sayılır.

- `IslemTuru = "Eslestir"`, `DagilimTuru = "Eslesme"`, `Dagilimlar = []`, `KaynakHarcamaId = null`, `EslesenKayitTuru`/`EslesenKayitId` zorunlu. Banka belgesi: `Gider` (kartsız Islem) ya da `KartOdeme` (iptal edilmemiş TakipKartOdeme, her kart). Kart belgesi: belge kartının `KartHarcama` (iptal edilmemiş TakipHarcama; kartlı gider/alış ödemesinden türeyenler dahil), `KartTaksidi` (taksit sayısı >1 harcamanın TakipKartTaksit'i) ya da `KartOdeme`'si. Tutar hedefe eşit, tarih en çok 3 gün farklı olmalı; taksitte tarih harcamaya 3 ya da taksidin ekstre kesimine 35 gün yakın olmalı (aksi 400; hedef yoksa 404).
- Satır yalnız bağ yazar (`EkstreKayit.EslesmeTuru/EslesmeId`, kart belgesinde `KrediKartiId`): yeni Islem/harcama/ödeme, Sync ve kart sürümü değişikliği yok; önizleme kasa etkisi 0, tür/transfer/benzer kayıt uyarısı yok. Bir kayıt aynı belge türünden (banka/kart) yalnız bir eşleştirme satırına bağlanır (409 "Bu kayıt başka bir ekstre satırıyla eşleşti."): kart ödemesi hem banka hem kart ekstresinde birer kez eşleşir; taksitli harcamanın her taksidi ayrı hedeftir. İptal yalnız bağı kaldırır.
- `EkstreKayitDto.EslesmeDurumu`: iptal edilmemiş eşleşmede `Eslesti`, hedef silinmiş/iptal edilmişse `KayitYok`. Eski satırlarda eşleşme alanları null.
- Adaylar (`/eslesme-adaylari`): `BenzerKayitServisi` kuralı (aynı tutar, ±3 gün) ve belge türüne uygun kaynaklar; başka eşleştirme satırına bağlı kayıt aday değildir; tarih farkına göre en çok 20. `KanalEtiketi`, `AlisId`, `EkstreKayitId` gösterim içindir.
- Ters sıra: ekstre önce işlenmişse alış ödemesi `AlisOdemeYaz.MevcutKartHarcamaId` ile ekstreden gelen (gidersiz) kart harcamasına, `MevcutIslemId` ile banka ekstresi giderine bağlanır (`alis-uygulama-sozlesmesi.md`). Satırın sahiplik sütunu (`KartHarcamaId`/`IslemId`) boşalır, satır eşleşmeye döner (`IslemTuru` aynı kalır; `EslesmeTuru` `KartHarcama`/`Gider`); kayıt alışın payıyla sayılır, kart borcu ve kasa ikinci kez sayılmaz. Ödeme alıştan ayrılınca (iptal) sahiplik geri yazılır; başka alışa taşınınca eşleşme aynen kalır (gap-coklu-giris-cift-sayim-mutabakat-5). Bu durumda satır ve kart harcaması ödeme ayrılmadan iptal edilemez (409).
- Taksit satırı (gap-coklu-giris-cift-sayim-mutabakat-6): yeni `KartHarcama` olarak işlenecek satırın tutarı aynı kartın taksitli harcamasının başka satırla eşleşmemiş bir taksidine eşitse (harcama tarihi ±3 ya da taksidin ekstre kesimi ±35 gün; eşleşme adaylarıyla aynı kural) önizleme harcama başına tek uyarı verir: "Bu satır #{harcama} taksitli harcamanın n/m. taksidi olabilir; atlayın ya da mevcut kayıtla eşleştirin." (kesimi satıra en yakın taksit) ve `TekrarOnayGerekli` true olur.
- Şema: `EkstreKayitlar.EslesmeTuru TEXT NULL`, `EslesmeId INTEGER NULL`, `IX_EkstreKayitlar_EslesmeTuru_EslesmeId` (migration `20261003000100_EkstreEslesmesi`, yalnız ekleme).

## Ownership/root parser entry

Root `EkstreImportDtos.cs` DTOs + `EkstreMetinOkuyucu.Oku(string text,string kaynak,string banka)` → `EkstreOkumaSonucu(IReadOnlyList<EkstreOkunanSatir> Satirlar,IReadOnlyList<string> Uyarilar)`.
Root `IPdfMetinOkuyucu.OkuAsync(byte[] pdf,CancellationToken ct)`→Task<string>; registeredsingleton `PdfMetinOkuyucu`. Throws `PdfOkumaException` (Message safe, StatusCode422/503). Linux docker poppler-utils, subprocess boundedtimeout/50pages/output. API backend upload injectinterface andcatcheserror.
Backend importagent migration10/endpoints/projections/guards/tests. RootProgram/DbContext partialdeclarehooks and extraction/parser/tests/Docker/deploy. Web statement_import_review; Native api_review. Build coordination root API; native own projects after notice. Release2.3.0, minimum2.3.0, build6.
