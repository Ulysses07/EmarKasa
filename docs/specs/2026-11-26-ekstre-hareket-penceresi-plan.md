# Ekstre hareket penceresi — uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ekstre hareketleri tek kaydırmalı, tam pencere açılan bir sayfada incelensin; yapay zekâ belge açılınca hiç sorulmamış satırları
kendiliğinden doldursun ve güvenilir satırlar işaretli gelsin.

**Architecture:** Sunucuya iki geriye uyumlu ekleme (yalnız sorulmamış satırları sorma bayrağı; yapay zekâ önerisinin hazır/kontrol
bilgisi). App.Core'da hazır kuralı, kendiliğinden işaretleme, süzgeçler ve yapay zekâ döngüsü (`EkstreAktarmaViewModel`). MAUI'de yeni
`EkstreHareketPenceresi` (aynı görünüm modeliyle `PushModalAsync`), ana sayfadan inceleme bölümleri kalkar.

**Tech Stack:** .NET 10, ASP.NET Core minimal API, EF Core SQLite, MAUI (C# ile kurulan görünüm), CommunityToolkit.Mvvm, xUnit v3.

**Spec:** `docs/specs/2026-11-26-ekstre-hareket-penceresi.md`

**Çalışma yeri:** worktree `C:/Users/burak/source/repos/Kasa-paket/hareket-penceresi`, dal `ozellik/hareket-penceresi` (taban
`origin/release/2.x`). PR'ı ben açarım; yayın yok (kullanıcı söyler).

## Global Constraints

- Sözleşmeye yalnız geriye uyumlu ekleme: yeni alanlar kayıtların sonuna, varsayılanlı (`bool X = false`, `string? Y = null`).
  Sunucu DTO'su (`Kasa.Api`) ile istemci DTO'su (`Kasa.ApiClient`) eş kalır (`DtoEslesme` testleri).
- Uç imzası değişirse uç envanteri yenilenir: `KASA_UC_ENVANTERI_YAZ=1 dotnet test Kasa.Api.Tests --filter "FullyQualifiedName~UcEnvanteri"`,
  sonra dosya CRLF'e çevrilir (depodaki biçim).
- `MimariTests`: `Kasa.Core.Kodlar` sabitine eşit metin yazılmaz (`YapayZekaDurumlari.Oneri`, `EkstreIslemTurleri.*`, `YapayZekaGuvenleri.*` kullan).
- `bash Kasa.Ui.E2E/maui-lint.sh` (uzun satır, sabit renk) ve `dotnet format whitespace --folder . --exclude '**/bin/' '**/obj/' --verify-no-changes` temiz.
- Renk: gelen tutar `Pos`, giden tutar `Neg` (kırmızı), yönü belirsiz tutar `UyariMetin`; satır yazısı renksiz ve kalın değil. Ekranda tek
  birincil düğme: "Etkisini göster ve kaydet"; diğerleri `BtnSecondary`.
- Arayüz metinleri (birebir): süzgeçler "Tümü", "Hazır", "Kontrol edin", "Kapanan"; nedenler "kanal seçin", "yön belirsiz",
  "emin değil", "uyarı var", "kasa hareketi olmayabilir", "kontrol edin"; düğmeler "Toplu tür / kanal", "Yok say",
  "Etkisini göster ve kaydet", "Kaydet"; ⋯ menüsü "Kaynak dosyayı indir", "Dosyayı yeniden oku", "Bu ERP12 dosyasını sil",
  "Atlanan satırlar", "Belge uyarıları", "Yapay zekâya gönderilen metni göster", "Yanıtsız satırları yeniden sor".
- Hiçbir satır kendiliğinden kaydedilmez; kullanıcının kaldırdığı işaret ve elle değiştirdiği satır kendiliğinden yeniden işaretlenmez.
- Yapay zekâ döngüsü en çok 10 tur; yalnız `YalnizSorulmamis = true` ile sorar.
- Commit sonu: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test komutları: `dotnet test Kasa.Api.Tests --filter ...`, `dotnet test Kasa.App.Core.Tests --filter ...`, `dotnet test Kasa.Sozlesme.Tests`.
  Uygulama derlemesi: `dotnet build Kasa.App -c Debug -f net10.0-windows10.0.19041.0`.

## Review Focus

1. **Yapay zekâ döngüsü sürerken belge değişir ya da oturum düşer:** döngü eski belge için soru göndermeyi bırakmalı (Görev 4 testi
   `Dongu_belge_degisince_eski_belge_icin_durur`).
2. **Yeniden okunan belge:** öneriler silindiği için satırlar yeniden "hiç sorulmamış" olur ve bir kez daha sorulur; ikinci açılışta
   sorulmaz (Görev 1 testi `Yeniden_okunan_belgede_satirlar_yeniden_sorulur`).
3. **Boş ya da tamamen kapanmış belge:** süzgeç sayıları 0, alt çubuk "Hiç satır işaretli değil.", döngü çağrı yapmaz (Görev 3 testi
   `Bos_belgede_sayilar_sifir_ve_dongu_sormaz`).
4. **Büyük belge (1500 satır) ve arama:** süzgeç/arama değişince satır nesneleri yeniden yaratılmaz, açık panelin satırı korunur
   (Görev 3 testi `Suzgec_ve_arama_ayni_satir_nesnelerini_gosterir`).
5. **Dar pencere (< 900 px):** panel tablonun üstüne açılır, "Geri" kapatır; tablo tek kaydırıcı kalır (Görev 6 görsel adımı).

---

### Task 1: Sunucu — yalnız hiç sorulmamış satırları sorma

**Files:**
- Modify: `Kasa.Api/YapayZeka/YapayZekaServisi.cs` (`Hazirla`)
- Modify: `Kasa.Api/EkstreAktarmaEndpoints.YapayZeka.cs` (GET/POST `/{id}/yapay-zeka`, `YapayZekaSorYaz`)
- Modify: `Kasa.ApiClient/YapayZekaDtos.cs`, `Kasa.ApiClient/IKasaApi.cs` (ya da yapay zekâ arayüzünün bulunduğu dosya)
- Modify: `Kasa.App.Core.Tests/EkstreAktarmaTests.cs` (sahte API imzası), `Kasa.Sozlesme.Tests/YapayZekaSozlesmeTests.cs`
- Modify: `Kasa.Api.Tests/Altin/uc-envanteri.txt` (yenilenir)
- Test: `Kasa.Api.Tests/YapayZekaTests.cs`

**Interfaces:**
- Produces: `internal static Hazirlik Hazirla(KasaDbContext db, EkstreBelgeEntity belge, bool yalnizSorulmamis = false)`;
  `record YapayZekaSorYaz(Guid IstekId, bool YalnizSorulmamis = false)` (sunucu ve istemci);
  GET sorgu değişkeni `yalnizSorulmamis` (bool, varsayılan false);
  istemci `Task<YapayZekaOnizlemeDto> YapayZekaOnizlemeAsync(int id, bool yalnizSorulmamis = false)` (true ise `?yalnizSorulmamis=true`).

- [ ] **Step 1: Failing tests in `YapayZekaTests.cs`**
  - `Yalniz_sorulmamis_istegi_yanitsiz_satiri_yeniden_sormaz`: iki farklı açıklamalı tanınmayan satır; sahte model yalnız 1'e yanıt
    verir. Sonra `POST {IstekId=yeni, YalnizSorulmamis=true}` → 409 ve iletide "sorulacak satır yok"; sahte modele yeni istek gitmez.
    `GET ?yalnizSorulmamis=true` → `SatirSayisi == 0`. Bayraksız `POST` → satır 2 yeniden sorulur (`Sorulan == 1`).
  - `Yeniden_okunan_belgede_satirlar_yeniden_sorulur`: sorulmuş belge yeniden okunur (`POST /{id}/yeniden-oku`); sonra
    `YalnizSorulmamis=true` ile sorulur → `Sorulan` satır sayısına eşit.
- [ ] **Step 2:** `dotnet test Kasa.Api.Tests --filter "FullyQualifiedName~YapayZekaTests"` → yeni testler FAIL.
- [ ] **Step 3:** `Hazirla` içinde `yalnizSorulmamis` doğruysa `sorulmus` sözlüğündeki satırlar gruplamadan önce atılır; uçlar bayrağı
  geçirir. İstemci DTO ve `KasaApiClient` güncellenir; sahte API'lerin imzası (App.Core testleri, `SozlesmeFabrikasi`) uyarlanır.
- [ ] **Step 4:** Uç envanteri yenilenir (Global Constraints). `dotnet test Kasa.Api.Tests --filter "FullyQualifiedName~YapayZeka|FullyQualifiedName~UcEnvanteri|FullyQualifiedName~DtoEslesme"`,
  `dotnet test Kasa.Sozlesme.Tests` → PASS.
- [ ] **Step 5:** Commit `feat(api): yapay zekâ yalnız hiç sorulmamış satırları sorabilir`.

### Task 2: Sunucu — yapay zekâ önerisinin hazır/kontrol bilgisi

**Files:**
- Modify: `Kasa.Api/EkstreHafizaServisi.cs` (`Okuma.Nedenler`)
- Modify: `Kasa.Api/YapayZeka/YapayZekaServisi.cs` (`SatirOnerisi`), `Kasa.Api/EkstreKuralServisi.cs` (çağrı)
- Modify: `Kasa.Api/EkstreKuralDtos.cs`, `Kasa.ApiClient/YapayZekaDtos.cs` (`EkstreYapayZekaOnerisi`)
- Modify: `docs/specs/2026-11-25-yapay-zeka-onerileri.md` (hazır kuralı)
- Test: `Kasa.Api.Tests/YapayZekaTests.cs`, `Kasa.Sozlesme.Tests/YapayZekaSozlesmeTests.cs`

**Interfaces:**
- Consumes: Task 1 yok.
- Produces: `record EkstreYapayZekaOnerisi(string Guven, string Kategori, string? Aciklama, bool Hazir = false, string? KontrolNedeni = null)`
  (iki tarafta); `EkstreHafizaServisi.Okuma` içinde
  `internal List<string> Nedenler(EkstreOkunanSatir row, string islemTuru, string? dagilimTuru, IReadOnlyList<int> kanalIds, int? kartId)`
  (bugünkü `Nedenler(row, Karar)` bunu çağırır, davranışı değişmez);
  `SatirOnerisi(EkstreOkunanSatir row, IReadOnlyDictionary<int, YapayZekaOneriEntity> oneriler, IReadOnlySet<int> kanallar, EkstreHafizaServisi.Okuma hafiza)`.

Yapay zekâ önerisinin `KontrolNedeni` sırası (ilk tutan yazılır; hiçbiri yoksa `Hazir = true`, `KontrolNedeni = null`):
1. tür `Atla` → "kasa hareketi olmayabilir"; 2. satır yönü Giriş/Çıkış değil → "yön belirsiz"; 3. güven `Dusuk` → "emin değil";
4. `Nedenler(...)` boş değilse: "satırda okuma uyarısı var" → "uyarı var", "hesaplar arası transfer olabilir" → olduğu gibi, diğerleri
   → olduğu gibi (ilk neden); 5. Gelir/Gider/KartHarcama ve dağılım yok → "kanal seçin".
Durum `YapayZekaDurumlari.Oneri` kalır; satır iletisi Hazır'da "Yapay zekâ (… güven): {kategori}.", Kontrol'de sonuna " Kontrol edin: {neden}." eklenir.

- [ ] **Step 1: Failing test** `Yapay_zeka_onerisi_hafizanin_olcusuyle_hazir_ya_da_kontrol_doner`: beş satır ve sahte yanıtlar —
  (a) yüksek güven Gider Genel, uyarısız, yönü Çıkış → `(Hazir, KontrolNedeni) == (true, null)`;
  (b) orta güven Gider, dağılım Belirsiz → `(false, "kanal seçin")`; (c) düşük güven Gider Genel → `(false, "emin değil")`;
  (d) açıklaması "VIRMAN …" olan Çıkış satırına Gider Genel → `(false, "hesaplar arası transfer olabilir")`;
  (e) Atla → `(false, "kasa hareketi olmayabilir")`. `GET /{id}/oneriler` üzerinden doğrulanır.
- [ ] **Step 2:** Run `dotnet test Kasa.Api.Tests --filter "FullyQualifiedName~YapayZekaTests"` → FAIL.
- [ ] **Step 3:** `Nedenler` ayrıştırılır, `SatirOnerisi` neden sırasını uygular, DTO'lar genişler; sözleşme testi yeni alanları bekler;
  spec'te "öneri satırı seçmez, hazır saymaz" kuralı 2026-11-26 tasarımına bağlanır.
- [ ] **Step 4:** `dotnet test Kasa.Api.Tests --filter "FullyQualifiedName~YapayZeka|FullyQualifiedName~Hafiza|FullyQualifiedName~DtoEslesme|FullyQualifiedName~Altin"` ve `dotnet test Kasa.Sozlesme.Tests` → PASS (hafıza testleri değişmeden geçer).
- [ ] **Step 5:** Commit `feat(api): yapay zekâ önerisi hafızanın ölçüsüyle hazır ya da kontrol`.

### Task 3: App.Core — hazır kuralı, kendiliğinden işaretleme, süzgeçler

**Files:**
- Modify: `Kasa.App.Core/EkstreSatirEditor.Hafiza.cs` (`HazirOneri`, yeni `KontrolNedeni`, `KendiligindenIsaretlendi`, taşıma)
- Create: `Kasa.App.Core/EkstreAktarmaViewModel.Suzgec.cs`
- Modify: `Kasa.App.Core/EkstreAktarmaViewModel.Hafiza.cs` (`HazirlariIsaretleCommand` kalkar), `EkstreAktarmaViewModel.cs`, `.Kurallar.cs`
- Test: Create `Kasa.App.Core.Tests/EkstreHareketPenceresiTests.cs`; Modify `EkstreYapayZekaTests.cs`, `EkstreHafizaTests.cs`

**Interfaces:**
- Consumes: Task 2 `EkstreYapayZekaOnerisi.Hazir/KontrolNedeni`.
- Produces:
  - `EkstreSatirEditor.HazirOneri` (bugünkü koşullar **veya** `Oneri is { Durum: YapayZekaDurumlari.Oneri, YapayZeka.Hazir: true }` ve
    satır yapay zekâdan doldu ve `Hazirla().Yaz is not null`);
  - `public string? KontrolNedeni` (canlı): kapalı ya da `HazirOneri` ise null; elle değiştirilmiş, yönü Giriş/Çıkış ve
    `Hazirla().Yaz is not null` ise null (kullanıcı tamamladı); yön Giriş/Çıkış değilse "yön belirsiz";
    yoksa `Oneri.YapayZeka.KontrolNedeni`; yoksa `Hazirla().Yaz is null` ve tür kanallıysa "kanal seçin"; yoksa satır uyarısı varsa
    "uyarı var"; yoksa "kontrol edin";
  - `public bool KendiligindenIsaretlendi { get; internal set; }` (yenilenen modele `HafizaDayanaginiAl` ile taşınır);
  - `public enum EkstreSuzgec { Tumu, Hazir, Kontrol, Kapanan }`;
  - VM: `[ObservableProperty] EkstreSuzgec _suzgec`, `[ObservableProperty] string _arama = ""`,
    `ObservableCollection<EkstreSatirEditor> Gorunenler`, `int HazirSayisi`, `int KontrolSayisi`, `int KapananSayisi`,
    `public void SuzgecleriHesapla()` (üyeliği dondurur ve kendiliğinden işaretler; `KurallariAlAsync` öneriyi uyguladıktan sonra çağrılır).
    Üyelik: bekleyen satır `KontrolNedeni == null` ise Hazır, değilse Kontrol; kapalı satır Kapanan. Kendiliğinden işaretleme:
    `HazirOneri && !KendiligindenIsaretlendi && !ElleDegisti` → `Secili = true`, `KendiligindenIsaretlendi = true`.
    `Gorunenler` `Esitle` ile güncellenir (nesne kimliği korunur). Arama: açıklamada büyük/küçük harf ve Türkçe harf duyarsız geçiş.

- [ ] **Step 1: Failing tests** (`EkstreHareketPenceresiTests`):
  - `Hazir_satirlar_belge_acilinca_kendiliginden_isaretlenir`: hafıza Hazır satırı ve `YapayZeka.Hazir=true` satır işaretli; `Hazir=false`
    (neden "kanal seçin") satır işaretsiz, `KontrolNedeni == "kanal seçin"`; `(HazirSayisi, KontrolSayisi) == (2, 1)`.
  - `Kaldirilan_isaret_yeniden_konmaz`: işaret kaldırılır, `KurallariYenileAsync()` → hâlâ işaretsiz.
  - `Elle_degisen_satir_kendiliginden_isaretlenmez`.
  - `Suzgec_uyeligi_calisirken_degismez`: Kontrol satırına kanal verilir → `KontrolNedeni == null` hemen; `Suzgec = Kontrol` iken
    `Gorunenler` hâlâ içerir; `SuzgecleriHesapla()` sonrası satır Hazır süzgecindedir ve işaretsiz kalır (elle değişti; işareti kullanıcı koyar).
  - `Suzgec_ve_arama_ayni_satir_nesnelerini_gosterir`: `Arama = "kargo"` → yalnız eşleşenler, aynı nesneler; `SeciliSatir` korunur.
  - `Bos_belgede_sayilar_sifir_ve_dongu_sormaz` (sayılar kısmı burada; döngü kısmı Task 4'te).
  - Eski `Yapay_zeka_onerisi_satiri_doldurur_hazir_saymaz_secmez…` testi yeni kurala göre yeniden yazılır; `HazirlariIsaretleCommand`
    kullanan testler kendiliğinden işaretlemeye çevrilir.
- [ ] **Step 2:** `dotnet test Kasa.App.Core.Tests --filter "FullyQualifiedName~Ekstre"` → yeni testler FAIL.
- [ ] **Step 3:** Uygula (Interfaces). `HazirlariIsaretleCommand`, `HazirOzeti`, `HazirAciklamasi` kalkar (görünüm Task 7'de temizlenir).
- [ ] **Step 4:** `dotnet test Kasa.App.Core.Tests` → PASS.
- [ ] **Step 5:** Commit `feat(app-core): hazır satırlar kendiliğinden işaretlenir; süzgeç ve arama`.

### Task 4: App.Core — yapay zekâ kendiliğinden doldurma döngüsü

**Files:**
- Modify: `Kasa.App.Core/EkstreAktarmaViewModel.YapayZeka.cs`, `EkstreAktarmaViewModel.cs` (`BelgeAcAsync`, `KaynakAcAsync`, yükleme ve
  `YenidenOkuAsync` sonu)
- Modify: `docs/specs/2026-11-25-yapay-zeka-onerileri.md` ("yalnız düğmeyle sorulur" → kendiliğinden)
- Test: `Kasa.App.Core.Tests/EkstreYapayZekaTests.cs`

**Interfaces:**
- Consumes: Task 1 (`YalnizSorulmamis`, `YapayZekaOnizlemeAsync(id, true)`), Task 3 (`SuzgecleriHesapla`).
- Produces: `private async Task YapayZekaDoldurAsync(int n)` (aynı `YurutAsync` içinde, belge yansıtılıp öneriler alındıktan sonra);
  `[ObservableProperty] string? _yapayZekaIlerleme` ("Yapay zekâ {k} satırı dolduruyor… {tur}" , bitince
  "Yapay zekâ {toplamOnerilen} satırı doldurdu"); `ObservableCollection<string> SonGonderilenler`;
  `public Task YanitsizlariSorAsync()` (bugünkü `YapayZekaSorAsync`, bayraksız; sayfa önce `YapayZekaOnayi` ile onay ister).
  Döngü: koşul `YapayZekaAcik && Belge.Kaynak != Erp && TaninmayanSayisi > 0`; her tur `GET ?yalnizSorulmamis=true` (SatirSayisi 0 →
  bitir; satırlar `SonGonderilenler`'e), sonra `POST YalnizSorulmamis=true`; 409 → sessizce bitir; `KalanSatir == 0` ya da `Onerilen == 0`
  → bitir; her turdan sonra `KurallariAlAsync(n)` + `SuzgecleriHesapla()`; `!Gecerli(n)` ya da `Belge.Id` değiştiyse dur; en çok 10 tur;
  diğer hata → `YapayZekaHatasi` yazılır, döngü biter. "Tanınmayan N satırı yapay zekâya sor" düğmesi ve `YapayZekaSorulabilir` kalkar.

- [ ] **Step 1: Failing tests**:
  - `Belge_acilinca_yapay_zeka_kendiliginden_sorar_ve_isaretler`: `YzDurumu` açık, iki tanınmayan satır; sahte yanıt `Onerilen=2,
    KalanSatir=0`, ardından öneriler `YapayZeka.Hazir=true` → `YzSorulari` tek, `YalnizSorulmamis == true`, iki satır işaretli,
    `YapayZekaIlerleme == "Yapay zekâ 2 satırı doldurdu"`.
  - `Sorulacak_satir_yoksa_sessizce_biter`: önizleme `SatirSayisi=0` → POST yok, hata yok.
  - `Ilerleme_yoksa_dongu_durur` ve `En_cok_on_tur_sorar` (sahte yanıt hep `KalanSatir=5, Onerilen=1` → 10 POST).
  - `Hata_donguyu_durdurur_ve_mesaj_yazar` (sahte POST 503 → `YapayZekaHatasi` dolu, tek POST).
  - `Erp_belgesinde_ve_kapaliyken_sorulmaz`.
  - `Dongu_belge_degisince_eski_belge_icin_durur` (ilk POST sırasında başka belge açılır → eski belge için ikinci POST yok).
  - `Bos_belgede_sayilar_sifir_ve_dongu_sormaz` (döngü kısmı).
- [ ] **Step 2:** `dotnet test Kasa.App.Core.Tests --filter "FullyQualifiedName~EkstreAktarmaTests"` → FAIL.
- [ ] **Step 3:** Uygula; eski düğme testleri (`Sor_dugmesi_…`, `Onay_metni_…`) `YanitsizlariSorAsync`'e uyarlanır.
- [ ] **Step 4:** `dotnet test Kasa.App.Core.Tests` → PASS.
- [ ] **Step 5:** Commit `feat(app-core): yapay zekâ belge açılınca hiç sorulmamış satırları doldurur`.

### Task 5: MAUI — hareket penceresi: başlık, süzgeçler, tablo, alt çubuk

**Files:**
- Create: `Kasa.App/Views/EkstreHareketPenceresi.cs` (ContentPage; kurucu `EkstreHareketPenceresi(EkstreAktarmaViewModel vm)`),
  `Kasa.App/Views/EkstreHareketPenceresi.Tablo.cs` (satır şablonu)
- Modify: `Kasa.App/Resources/Styles/*.xaml` yalnız gerekiyorsa (yeni stil anahtarları; sabit renk yok)

**Interfaces:**
- Consumes: Task 3 (`Gorunenler`, `Suzgec`, sayılar, `Arama`, `KontrolNedeni`), mevcut `SecimOzeti`, `BelgeOzeti`, `SatiriAcKapat`.
- Produces: `EkstreHareketPenceresi` sayfası; `public static Task AcAsync(INavigation nav, EkstreAktarmaViewModel vm)` (açık değilse
  `PushModalAsync`); sayfa ölçüsü: tablo + sağ panel kolonu (Task 6 doldurur) + alt çubuk.

- [ ] **Step 1:** Sayfa iskeleti: üst satırda dosya adı ve `BelgeOzeti`'nden tek satır alt bilgi, sağda ⋯ (`MenuFlyout` ya da
  `DisplayActionSheet`) ve ×; süzgeç çipleri (`CipGrubu`, sayılarla); arama girdisi; `YapayZekaIlerleme` satırı.
- [ ] **Step 2:** Tablo: tek `CollectionView` (sayfada başka kaydırıcı yok), satır şablonu tek `Grid`: işaret kutusu · tarih (gg.aa) ·
  açıklama (tek satır, `LineBreakMode.TailTruncation`) · tür · kanal ve altında kaynak (hafıza / yapay z. / kural / ERP12) ·
  tutar (sağa hizalı, Global Constraints renkleri). Kontrol satırında solda 3 px `UyariMetin` çizgi ve tür sütununda `KontrolNedeni`.
  Satıra dokunma `SatiriAcKapat`, kutu yalnız `Secili`.
- [ ] **Step 3:** Alt çubuk: `SecimOzeti`, "Toplu tür / kanal" (panelde toplu kipi açar — Task 6), "Yok say" (bugünkü
  `SecilenleriYokSayAsync` onayıyla), birincil "Etkisini göster ve kaydet" (`OnizleCommand`, sonuç Task 6 paneline).
- [ ] **Step 4:** `dotnet build Kasa.App -c Debug -f net10.0-windows10.0.19041.0` ve `bash Kasa.Ui.E2E/maui-lint.sh` → başarılı.
- [ ] **Step 5:** Commit `feat(app): ekstre hareket penceresi tablo ve alt çubuk`.

### Task 6: MAUI — sağ panel (satır, toplu, kayıt özeti), dar düzen, Esc

**Files:**
- Create: `Kasa.App/Views/EkstreHareketPenceresi.Panel.cs`
- Modify: `Kasa.App/Views/EkstreAktarmaPage.cs` (`SatirFormunuKur`, `KapananBolumu` içeriği, `TopluBolum` paylaşılacak yardımcıya taşınır)

**Interfaces:**
- Consumes: mevcut `SeciliSatir`, satır formu bağları, `OnizlemeMetni`, `TekrarOnay*`, `Onay`, `KaydetCommand`, `OnizlemeHatalari`,
  `SatiraGit`; Task 5 sayfası.
- Produces: panel kipleri `enum PanelKipi { Kapali, Satir, Toplu, KayitOzeti }` (sayfa içi); `OnizleCommand` sonucu `KayitOzeti`
  kipini açar; `SatirGosterIstendi` tabloyu kaydırıp `Satir` kipini açar.

- [ ] **Step 1:** Panel (~320 px): `Satir` kipinde bugünkü satır formu (kaynak satırı ve yön dayanağı burada); kapanan satırda
  "Yok saymayı geri al" / "Gerekçeyle iptal et"; `Toplu` kipinde bugünkü toplu alanlar; `KayitOzeti` kipinde önizleme metni, onaylar,
  "Kaydet" ve satıra gitme listesi.
- [ ] **Step 2:** Pencere genişliği < 900 px: panel tablonun üstüne tam genişlikte açılır, başında "Geri".
- [ ] **Step 3:** Esc (Windows; `#if WINDOWS` ile sayfanın platform görünümüne `KeyDown`): panel açıksa kapatır, değilse pencereyi kapatır.
- [ ] **Step 4:** Görsel doğrulama (deneme kopyası, ayrı masaüstü — `scratchpad/deneme/kosum`, `veri.py` 52 satır ve 500 satırlık
  türevi): açık panel, dar pencere (`boyut 860x900`), Kayıt özeti ekran görüntüleri; `kaydir ?` çıktısında yalnız tablo kaydırıcısı.
  Gerçek oturum dosyasının özeti önce/sonra aynı.
- [ ] **Step 5:** Commit `feat(app): hareket penceresi paneli, dar düzen ve Esc`.

### Task 7: MAUI — ana sayfa sadeleşir ve pencereyi açar

**Files:**
- Modify: `Kasa.App/Views/EkstreAktarmaPage.cs`, `EkstreAktarmaPage.Hafiza.cs`, `EkstreAktarmaPage.YapayZeka.cs`
- Modify: `docs/specs/2026-11-25-pdf-ekrani.md`, `docs/specs/2026-11-25-islem-hafizasi.md` (bağlantı), `README`/kullanım belgesi varsa ilgili satır

**Interfaces:**
- Consumes: Task 5 `EkstreHareketPenceresi.AcAsync`, Task 4 `YanitsizlariSorAsync`.

- [ ] **Step 1:** "2. Hareket satırları" ve "3. Kontrol ve kayıt" kartları ile `HazirBolumu`, sor düğmesi kalkar. Yükleme kartı
  "Yeni belge yükle" olur (numarasız, tek kısa cümle); sayfa başlığı altındaki metin tek cümleye iner.
- [ ] **Step 2:** `SonucGosterIstendi(Satirlar)` ve `SatirGosterIstendi` pencereyi açar (`AcAsync`); geçmişte "Belgeyi incele",
  yükleme sonrası ve kaynağa gitme pencereyi açar.
- [ ] **Step 3:** ⋯ menüsü maddeleri bağlanır (indir, yeniden oku, ERP12 sil, atlanan satırlar, belge uyarıları, gönderilen metin,
  yanıtsızları yeniden sor — bu sonuncusu `YapayZekaOnayi` metniyle onay ister).
- [ ] **Step 4:** `dotnet build Kasa.App …`, `bash Kasa.Ui.E2E/maui-lint.sh`, `dotnet test Kasa.App.Core.Tests` → başarılı; görsel:
  ana sayfa ve açılan pencere ekran görüntüsü.
- [ ] **Step 5:** Commit `feat(app): ekstre ana sayfası sadeleşti; inceleme hareket penceresinde`.

### Task 8: Son doğrulama ve PR

- [ ] **Step 1:** `dotnet test Kasa.Api.Tests`, `dotnet test Kasa.App.Core.Tests`, `dotnet test Kasa.Sozlesme.Tests`,
  `dotnet test Kasa.ApiClient.Tests` → hepsi PASS; `dotnet format whitespace …` ve `maui-lint` temiz.
- [ ] **Step 2:** Deneme kopyası ve yerel deneme sunucusuyla uçtan uca (sahte yapay zekâ istemcisi yalnız yerelde, commit edilmez):
  yükle → pencere açılır → döngü ilerleme yazar → hazırlar işaretli → bir Kontrol satırına kanal ver → etkisini göster → kaydet.
- [ ] **Step 3:** Deneme klasörü, deneme sunucusu ve `com.emar.dene` oturum klasörü temizlenir; gerçek `securestorage.dat` özeti aynı.
- [ ] **Step 4:** PR (`release/2.x`'e), açıklamada ekran görüntüsü yok (gerçek veri yok; deneme verisi de eklenmez), testler ve görsel
  doğrulama listesi.
