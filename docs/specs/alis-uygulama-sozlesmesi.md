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
record AlisOdemeYaz(int Surum, Guid IstekId, DateOnly Tarih, decimal Tutar, int? KrediKartiId = null, int? MevcutIslemId = null, string? Not = null, int? HesapId = null, int? MevcutKartHarcamaId = null, int? TaksitSayisi = null, DateOnly? IlkKesimTarihi = null);
record AlisOdemeDuzelt(int Surum, Guid IstekId, DateOnly Tarih, decimal Tutar, string Aciklama, int? KrediKartiId = null, int? HesapId = null, int? HedefAlisId = null, int? HedefSurum = null);
record AlisOdemeIptal(int Surum, Guid IstekId, string Aciklama, IReadOnlyList<AlisDagilimYaz>? KanalDagilimlari = null);
record BaglanabilirKartHarcamasiDto(int Id, int KrediKartiId, DateOnly Tarih, string Aciklama, decimal Tutar, int? EkstreKayitId = null);
```

Taksitli kartlı ödeme (gap-coklu-giris-cift-sayim-mutabakat-6):

- `TaksitSayisi` (1–60) ve isteğe bağlı `IlkKesimTarihi` yalnız yeni takipteki kartla YENİ kart harcaması oluşturan ödemede kabul edilir; kartsız/takipsiz kartta, `MevcutIslemId` ya da `MevcutKartHarcamaId` ile birlikte 400. İlk kesim ödemeden önce olamaz ve kartın kesim gününe en çok 7 gün uzak olmalı (Kredi Kartları ekranındaki harcamayla aynı kural). Kart harcaması bu planla hemen yazılır: her ekstreye taksit tutarı düşer, son ödeme hatırlatması taksit tutarındadır. Alan göndermeyen (eski) istemci tek taksitle devam eder; istek özeti (tekrar anahtarı) alanlar yalnız doluyken değişir.
- Aynı alanlar `IslemYazDto`'da (POST `/api/islemler`, kartlı genel gider) aynı kuralla vardır; düzenlemede (PUT) gönderilmez (400).
- Kart ekstresi içe aktarılırken yeni `KartHarcama` olarak işlenecek satırın tutarı aynı kartın taksitli harcamasının bir taksidine eşitse (harcama tarihi ±3 ya da taksidin ekstre kesimi ±35 gün) önizleme harcama başına tek uyarı verir ("Bu satır #{harcama} taksitli harcamanın n/m. taksidi olabilir; atlayın ya da mevcut kayıtla eşleştirin.") ve ayrıca onay ister.

Kart takibindeki ödemenin düzeltilmesi (gap-coklu-giris-cift-sayim-mutabakat-5; yeni takipteki kartla girilmiş ya da ekstreden gelen kart harcamasına bağlanmış ödeme):

- `PUT /api/alis/{id}/odemeler/{odemeId}`: yalnız başka alışa taşıma. `Tarih`, `Tutar`, `KrediKartiId` ödemenin kendisiyle aynı ve `HedefAlisId` farklı bir alış olmalı; aksi 409 ("... yalnız başka alışa taşınabilir ya da alıştan ayrılabilir"). Kart harcaması, taksitleri ve kart ödemesi payları değişmez; harcamanın kanal kaynağı hedef alışın dağılımı olur, aynalanmış harcamanın açıklaması hedef alışın adını alır, kart sürümü artar. Kilit: kaynağın sonraki kilitli ödemeleri taşıma kilidinde, hedef alış genel kuralda, harcaması kilitli dönemde bir kart ödemesiyle ödenmiş ödemenin taşınması `AyKilidiKurallari`'nda reddedilir.
- `POST /api/alis/{id}/odemeler/{odemeId}/iptal`: `KanalDagilimlari` yoksa ödeme gerçekleşmemiş sayılır: harcama ödenmemiş, iadesiz ve ekstre satırına bağsızsa harcama iptal edilir (taksitleri borçtan çıkar), gider ve ödeme kalkar; aksi 409 ("Bu kart harcaması ödendi. Alıştan ayırmak için gerçek kanal dağılımını girin; ..."). `KanalDagilimlari` verilirse (kayıtlı, tekil, pozitif paylar; toplam ödeme tutarı; yalnız kart takibindeki ödemede, aksi 400) ödeme alıştan AYRILIR: gider ve harcama alıştan bağımsız kart gideri olarak kalır; giderin kanalı tek payda o kanal, çok payda "A / B" (KanalId null), notuna "Alıştan ayrıldı: {açıklama}" eklenir; harcamanın kanal kaynağı girilen paylardır. Alışın kalanı artar; gider başka alışa `MevcutIslemId` ile bağlanabilir. Ekstreden gelen kart harcamasına bağlı ödeme her iki durumda ekstre kaydına döner (aşağıda).
- Genel gider ekranı: takipli kart giderinin harcaması ödenmemiş, iadesiz ve ekstreye bağsızsa `DELETE /api/islemler/{id}` harcamayı iptal edip gideri siler; `PUT` yalnız açıklama, not ve kanalı değiştirir (tarih, tutar, kart 409; kanal değişince harcamanın dondurulmuş payı giderle aynı kuralla yeniden yazılır). Diğer durumlar 409. Kart ekranından alışa/gidere bağlı harcamanın iptali kaynağını söyler ("Alış #{id} ödemesine bağlı; önce alış ödemesini alıştan ayırın." / "Gider #{id} kaydından geliyor ...").
- Takip başlangıcından önceki (eski kuralda kalan) takipli kart ödemesi değiştirilemez (409).

Ekstre önce işlenmişse (gap-coklu-giris-cift-sayim-mutabakat-1, ayrıntı `2026-09-27-ekstre-ice-aktarma-api.md`):

- `MevcutKartHarcamaId`: takipli kartla ödeme, gidere bağlı olmayan mevcut kart harcamasına bağlanır; ikinci harcama oluşmaz. Harcama aynı kartın, iptal edilmemiş, iade/devir olmayan, iadesiz ve pozitif harcaması olmalı; ödemenin tarihi, tutarı ve kartı harcamayla aynı gönderilir (aksi 409). `MevcutIslemId` ile birlikte gönderilemez (400). Ödemenin gideri oluşturulup harcamaya bağlanır.
- `GET /api/alis/baglanabilir-kart-harcamalari?krediKartiId=&tutar=`: editor; bu kuralla bağlanabilir en yeni 50 harcama (kilitli dönemden sonra).
- Banka ekstresi gideri `MevcutIslemId` ile bağlanabilir; `GET /api/alis/baglanabilir-giderler` onu `EkstreKayitId` ile listeler.
- Bağlanan kayıt ekstreden geldiyse satırın sahipliği eşleşmeye döner. Bu ödeme yalnız başka alışa taşınabilir (tarih/tutar/kart değişmez; kart harcamasına bağlı olan da taşınır, ekstre bağı korunur); ödeme iptali eşleşmeyi geri çevirir: kart harcamasında bağlama için oluşturulan gider silinir, banka giderinde gider korunur, kayıt yeniden ekstre satırınındır (kanal payı girilse de harcamanın payı ekstre satırındaki dağılımdır).

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
