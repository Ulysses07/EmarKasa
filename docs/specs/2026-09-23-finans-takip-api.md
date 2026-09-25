# Kart ve kredi takibi API sözleşmesi

Tüm tarihler YYYY-MM-DD, tutarlar decimal TL, alanlar JSON camelCase. GET: editor/viewer; değişiklik: yalnız editor. Alıcı erişemez. Hatalar 400 doğrulama, 409 sürüm/iş kuralı. Bütün mutation isteklerinde `istekId:UUID`, mevcut kayıtta `surum:int` gerekir. Tekrar aynı içerik mevcut sonucu döndürür; aynı anahtarda farklı içerik 409. Sürüm replay içerik özetine dahil değildir. Mutation daima ilgili tam KartTakipDto/KrediTakipDto döndürür. Kanal seçim kaynağı mevcut /api/kanallar.

## Ortak

`KanalPayYaz { kanalId:int, tutar:decimal }`
`TakipKanalPayi { kanalId:int?, kanal:string, tutar:decimal }` — null kanal dağılım bekliyor.
`TakipDurumYaz { istekId, surum, aktif:bool, aciklama:string }`
`TakipIptalYaz { istekId, surum, aciklama:string }`

## Kartlar

- GET /api/takip/kartlar -> KartTakipDto[] (eski/yeni tüm kartlar)
- GET /api/takip/kartlar/{id} -> KartTakipDto
- POST /api/takip/kartlar: KartTakipYaz
- PUT /api/takip/kartlar/{id}: KartTakipYaz (tarih/limit/ad; geçmiş ekstreler değişmez)
- POST /api/takip/kartlar/{id}/durum: TakipDurumYaz
- POST /api/takip/kartlar/{id}/harcamalar: KartHarcamaYaz
- POST /api/takip/kartlar/{id}/harcamalar/{harcamaId}/iptal: TakipIptalYaz (ödeme bağlıysa reddedilir; iade ayrı eksi harcama)
- PUT /api/takip/kartlar/{id}/ekstreler/{ekstreId}: KartEkstreYaz
- POST /api/takip/kartlar/{id}/odeme-onizleme: KartTakipOdemeYaz -> KartOdemeOnizlemeDto
- POST /api/takip/kartlar/{id}/odemeler: KartTakipOdemeYaz
- POST /api/takip/kartlar/{id}/odemeler/{odemeId}/iptal: TakipIptalYaz
- POST /api/takip/kartlar/{id}/gecis-onizleme: KartGecisYaz -> TakipGecisDto
- POST /api/takip/kartlar/{id}/gecis: KartGecisYaz (`onay:true` gerekir)

`KartTakipYaz { istekId, surum:0|current, ad, limit, kesimGunu:1..31, sonOdemeGunu:1..31, acilisTarihi, acilisBorc:decimal, acilisDagilimlari:KanalPayYaz[] }`
`KartHarcamaYaz { istekId, surum, tarih, aciklama, tutar:decimal, taksitSayisi:1..60, ilkKesimTarihi:date?, dagilimlar:KanalPayYaz[], kaynakHarcamaId:int?=null }` — iade eksi tutarla, tek taksitle ve kaynakHarcamaId seçilerek girilir. İade payları kaynak oranından hesaplanır; dagilimlar=[] olabilir. Pozitif payların toplamı mutlak tutarı verir.
`KartEkstreYaz { istekId, surum, sonOdemeTarihi, asgariOdeme:decimal?, aciklama }`
`KartTakipOdemeYaz { istekId, surum, tarih, tutar:decimal, ekstreId:int?, not:string? }`
`KartGecisYaz { istekId, surum, baslangic, kalanBorc:decimal, kasadaOncedenSayilanTutar:decimal, dagilimlar:KanalPayYaz[], aciklama, onay:bool }` — geçmişte kasadan düşmüş borç kısmı yeniden düşmez; önizlemede açıkça görünür. Eski kayıtlar korunur. Geçiş bugün veya sonrası; eski raporları değiştirecek geriye tarih reddedilir.

`KartTakipDto { id, surum, ad, yeniTakip:bool, aktif:bool, takipBaslangic:date?, kesimGunu, sonOdemeGunu, limit, borc, ekstreBorc, ekstreler:KartEkstreDto[], harcamalar:KartHarcamaDto[], odemeler:KartTakipOdemeDto[] }`
`KartEkstreDto { id, kesimTarihi, sonOdemeTarihi, borc, odenen, kalan, asgariOdeme:decimal? }`
`KartHarcamaDto { id, islemId:int?, tarih, aciklama, tutar, taksitSayisi, iptal:bool, dagilimlar:TakipKanalPayi[] }`
`KartTakipOdemeDto { id, tarih, tutar, kasaEtkisi, not:string?, iptal:bool, dagilimlar:TakipKanalPayi[] }`
`KartOdemeOnizlemeDto { tutar, kasaEtkisi, dagilimlar:TakipKanalPayi[], ekstreler:KartEkstreOdemePayi[] }`
`KartEkstreOdemePayi { ekstreId, tutar }`

Mevcut alış/gider ekranında bu karta bağlanan yeni harcama otomatik tek çekim olarak izlenir; ayrıca harcama girilmez. Taksitli bağımsız harcama yeni uçtan girilir. Harcama kasayı etkilemez. Yalnız kaydedilen ödeme etkiler. Ödeme payları kaynak harcamanın kanallarına gider. Ödeme fazlası kartta alacak olarak kalır; kanal bağlantısı bulunmayan fazlalık dağılım bekliyor görünür. Sonraki ödeme/borç dengesi çift kasa üretmez.

## Krediler

- GET /api/takip/krediler -> KrediTakipDto[]
- GET /api/takip/krediler/{id} -> KrediTakipDto
- POST /api/takip/krediler: KrediTakipYaz
- POST /api/takip/krediler/{id}/durum: TakipDurumYaz
- PUT /api/takip/krediler/{id}/taksitler/{taksitId}: KrediTaksitYaz
- POST /api/takip/krediler/{id}/erken-kapat: KrediKapatYaz
- POST /api/takip/krediler/{id}/gecis-onizleme: KrediGecisYaz -> TakipGecisDto
- POST /api/takip/krediler/{id}/gecis: KrediGecisYaz (`onay:true`)

`KrediTakipYaz { istekId, ad, cekilenTutar, cekimTarihi, ilkTaksitTarihi, taksitSayisi:1..600, aylikOdeme, kanalIdleri:int[], mevcutKredi:bool=false }`
`KrediTaksitYaz { istekId, surum, tarih, tutar, not:string?, iptal:bool, aciklama }` — işlenmiş taksidin tutar/tarih/iptali değiştirilemez, not eklenebilir.
`KrediKapatYaz { istekId, surum, tarih, tutar, aciklama }` — kapanış tarihinden itibaren kalan taksitleri iptal eder, banka kapama tutarını bir kez işler.
`KrediGecisYaz { istekId, surum, baslangic, kanalIdleri:int[], aciklama, onay:bool }` — eski çekim genel kasaya yeniden girmez; geçmiş kanal bakiyesi düzeltilmez. İleri taksitleri seçilen sabit kanallara eşit dağıtır. Ayrı kanal açılış düzeltmesi otomatik yapılmaz.
`KrediTakipDto { id, surum, ad, yeniTakip, aktif, takipBaslangic:date?, cekilenTutar, cekimTarihi, kalanPlanliOdeme, kanalPaylari:TakipKanalPayi[], taksitler:KrediPlanTaksitDto[] }`
`KrediPlanTaksitDto { id, no, tarih, tutar, durum:string, not:string?, dagilimlar:TakipKanalPayi[] }` durum Bekliyor/KasayaIslendi/Iptal.
`TakipGecisDto { kaynak:string, kaynakId:int, baslangic, genelKasaAnlikFarki:decimal, kanalAnlikFarki:decimal, eskiKasadaSayilanTutar:decimal, aciklamalar:string[], kabulEdilebilir:bool }`

## Özet

GET /api/takip/ozet?gun=30 -> `TakipOzetDto { tarih, kartBorcu, kalanKrediPlani, olaylar:TakipOlayDto[] }`
`TakipOlayDto { kaynak:string, kaynakId:int, kalemId:int, ad:string, tarih, tutar, tur:string, otomatikKasa:bool }` kaynak Kart/Kredi; tur Kesim/SonOdeme/Taksit. Kart kesimi borç hatırlatmasıdır; kredi vadesi otomatik kasa etkisidir.

Bildirim backend'i bu işten ayrı uygulanır; tekrar kullanılabilir `FinansTakipServisi.GetNotificationEvents(db,today)` aynı kaynak/kalem kimliklerini döndürür. Bildirim üretmek mali kayıt yaratmaz.

İade sınırı: kaynak harcamanın henüz ödenmemiş kısmını aşan iade 409 ile reddedilir. Ödenmiş harcama için kanallar arası kart alacağı mahsubu bu sürümde yoktur; mevcut ödemeler sessizce değiştirilmez.
Geçiş bugün eski kredi taksidi veya kart ay sonu düşümü içeriyorsa yarın veya sonrası seçilmelidir. /takip/ozet ileri gün aralığına ek olarak geçmiş son ödeme tarihli açık kart ekstrelerini de döndürür.

Kuruş koruması: iade önceki herhangi bir ödemenin kanal payını değiştirecekse 409 döner. Ödemeli kaynağın iadesi doğrudan iptal edilemez; bu akış geçmiş ödeme paylarını yeniden dağıtmaz. İade payları kaynağın henüz ödenmemiş kanal paylarından hesaplanır.
