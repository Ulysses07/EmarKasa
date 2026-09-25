# Alış uygulama sözleşmesi

Durum değerleri: `Taslak`, `Incelemede`, `Onaylandi`. API ve istemci JSON adları C# camelCase.

DTO sözleşmesi (API ve ApiClient aynı alan adlarını kullanır):

```csharp
record AlisKanalDto(int Id, string Ad, bool Aktif);
record AliciDto(int Id, string Kullanici, string Ad, bool Aktif);
record AliciYaz(string Kullanici, string Ad, string? Sifre, bool Aktif = true);
record AlisDagilimYaz(int KanalId, decimal Tutar);
record AlisKalemYaz(string Aciklama, decimal Tutar, IReadOnlyList<AlisDagilimYaz> Dagilimlar);
record AlisYaz(int Surum, DateOnly Tarih, string Tedarikci, string? Not, IReadOnlyList<AlisKalemYaz> Kalemler);
record AlisDurumYaz(int Surum, string? Not = null);
record AlisDagilimDto(int KanalId, string Kanal, decimal Tutar);
record AlisKalemDto(int Id, string Aciklama, decimal Tutar, IReadOnlyList<AlisDagilimDto> Dagilimlar);
record AlisOdemeDto(int Id, int IslemId, DateOnly Tarih, decimal Tutar, int? KrediKartiId, bool DagilimBekliyor, IReadOnlyList<AlisDagilimDto> Dagilimlar);
record AlisDto(int Id, int Surum, int? AliciId, string Alici, DateOnly Tarih, string Tedarikci, string? Not, string Durum, string? EditorNotu, decimal Toplam, decimal Odenen, decimal Kalan, IReadOnlyList<AlisKalemDto> Kalemler, IReadOnlyList<AlisOdemeDto> Odemeler);
record AlisOdemeYaz(int Surum, Guid IstekId, DateOnly Tarih, decimal Tutar, int? KrediKartiId = null, int? MevcutIslemId = null, string? Not = null);
```

- `GET /api/alis/kanallar`: editor/alici, sınırlı kanal listesi.
- `GET /api/alis`: editor tümü, alici kendi alışları. Ayrıntılı AlisDto listesi.
- `POST /api/alis`, `PUT /api/alis/{id}`: taslak oluştur/güncelle. Editör de oluşturabilir (AliciId null; Alici=Editör). Alıcı yalnız kendi Taslak durumundakini değiştirebilir. Editör Taslak/Incelemede düzenleyebilir; onaylıyı önce iade eder. Surum uyuşmazlığı409.
- `POST /api/alis/{id}/gonder`: alici sahibi veya editor; Taslak -> Incelemede.
- `POST /api/alis/{id}/onayla`: editor; Incelemede -> Onaylandi; tüm kalem dağılım toplamları tam eşit olmalı, en az bir pozitif kalem. Tekrar onay idempotent.
- `POST /api/alis/{id}/iade`: editor; Incelemede/Onaylandi -> Taslak, zorunlu açıklama. Ödemeler korunur ve yeniden onaya kadar dağılım bekler.
- `POST /api/alis/{id}/odemeler`: editor; mevcut gideri bağlar veya bir adet gerçek gider oluşturur. Her durumda tüm durumlarda mümkün; taslakta dağılım bekler. IstekId global unique, tekrar aynı istek aynı ödeme; farklı içerik409. Toplam ödeme alış toplamını geçemez. Yeni ödeme Tarih, Tutar, KrediKartiId; mevcut gider seçilirse bu alanlar giderle eşleşmeli. Surum concurrency ile aynı anda fazla ödeme engellenir. Yeni gider Kanal="Dağılım bekliyor", KanalId=null. KrediKartiId varsa Tip=KrediKarti yoksa Cari. Mevcut gider yalnız Cari/KrediKarti, Ortak dahil, alış öncesi kasayı ikinci kez etkilemez. Dağılım rapor yüklemede türetilir.
- `GET /api/alicilar`, `POST /api/alicilar`, `PUT /api/alicilar/{id}`: yalnız editor. Şifre yeni hesapta zorunlu >=8, değişimde boşsa koru. Hash API cevabına yazılmaz. Pasife alma/şifre değişimi oturum iptal eder.

Ödeme payları alıştaki kanal toplamlarına orantılı, kuruşları koruyacak şekilde belirlenir; arayüz bu kuralı ve ödeme öncesi tahmini payları gösterir. Kümülatif ödenen tutar üzerinden pay farkı kullanılır: son ödeme tam alış dağılımını tamamlar. `Kasa.Core.AlisDagitici.Dagit(IReadOnlyList<AlisKanalPayi> paylar, decimal oncekiOdeme, decimal odeme)` -> IReadOnlyList<AlisKanalPayi>. `AlisKanalPayi(int KanalId, decimal Tutar)`. Sıra KanalId. Girdiler pozitif, kuruşlu, ödeme toplamı aşamaz. Core helper yalnız hesap yapar.

Yeni veri modeli: AliciEntity(Id,Kullanici,Ad,SifreHash,Aktif,OturumSurumu); AlisEntity(Id,Surum concurrency token int, AliciId nullable, AliciKaydi navigation,Tarih,Tedarikci,Not,Durum,EditorNotu,Kalemler,Odemeler); AlisKalemEntity(Id,AlisId,Aciklama,Tutar,Dagilimlar); AlisDagilimEntity(Id,AlisKalemId,KanalId,KanalKaydi,Tutar); AlisOdemeEntity(Id,AlisId,IslemId,Islem navigation,IstekId Guid, IstekOzeti string). Alış-kalem-dağılım silme zinciri Cascade; kanal/alıcı/gider ve ödeme-alış ilişkisi Restrict. IslemId ve IstekId benzersizdir. Ödeme dahil her başarılı yazmada Alis.Surum artar. İkinci migration ilk sabit şemadan sonra uygulanır.

Yetkilendirme politikaları: `Finans` (editor/viewer), `Alis` (editor/alici), `Editor` (editor). Alış uçları finans grubundan ayrı kayıtlıdır. Alıcı kimliği JWT içinde `alici_id` ile taşınır ve aktif hesap/oturum sürümüyle doğrulanır. Genel gider PUT/DELETE uçları alışa bağlı gideri409 ile reddeder; kontrol ve yazma aynı işlem içindedir.

`PanelDto`, `HaftalikOzet` ve `AylikRapor`, `decimal DagilimBekleyenTutar = 0m` alanını içerir. Bekleyen sınıflandırma, core Islem.DagilimBekliyor bayrağıyla tanınır; eski bir gerçek kanalın adının aynı olması karışıklık yaratmaz. `GET /api/islemler` her gerçek gideri tek satırda döndürür; onaylı dağılımın güncel kanal adlarıyla süzer ve `AlisId`/`DagilimBekliyor` alanlarını verir. Alış listesi, gider listesi ve rapor yüklemeleri tek veritabanı görünümünden okunur.
