# Emar Kasa — Plan 4 (Yayın / Dağıtım) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Native Emar Kasa uygulamalarını (Windows `.exe` + Android AAB + iOS `.ipa`) yayına hazırla; web SPA'yı emekliye ayır ve backend'i native-only olarak yeniden dağıt; KrediKartlari tablosunu içeren DB şemasını üretime taşı.

**Architecture:** İki ayrık iş kolu. (A) **Backend emeklilik**: `Kasa.Api` artık React SPA sunmaz (sadece `/api/*` + `/health`); Dockerfile'dan node web-build aşaması çıkar; login token gövdede (Plan 1'de yapıldı). (B) **İstemci paketleme**: Windows paketsiz `.exe` bu makinede üretilir; Android AAB + iOS `.ipa` için imza/keystore + build config hazırlanır ama gerçek build mobil TFM + (iOS için) Mac gerektirir. DB şeması: mevcut prod SQLite'ta `KrediKartlari` tablosu YOK (`EnsureCreated()` var olan DB'ye tablo eklemez) → migration yerine **kontrollü DB yeniden oluşturma** (veri az/iç kullanım) veya elle `CREATE TABLE`.

**Tech Stack:** .NET 10 MAUI (`net10.0-windows`/`-android`/`-ios`), ASP.NET Core minimal API, EF Core 10 Sqlite, Docker (VPS 72.61.187.202), Android keystore (`keytool`), App Store Connect / Google Play Console (kullanıcı-operasyonel).

---

## ⚠️ Ortam ve sıralama kısıtları (planı uygulamadan ÖNCE oku)

1. **Bu makine Windows.** `Kasa.App/Kasa.App.csproj` TFM'i yalnız `net10.0-windows10.0.19041.0` (mobil TFM'ler yorumda). **Android AAB / iOS IPA bu makinede üretilemez.** Task 4-5 build adımları Mac'te (iOS) veya mobil TFM geri eklenip Android workload kurulu bir makinede koşulur; bu planda **hazırlık** (keystore, build config, mağaza metni) yapılır, gerçek store build'i kullanıcı ortamına bırakılır.
2. **kasa.royalmezat.com CANLI ve kullanımda.** SPA emekliliği + DB yeniden oluşturma **native uygulamalar gerçekten yayınlanmadan/ortaklara dağıtılmadan ÖNCE** yapılırsa herkes çalışan uygulamasız + **veri silinmiş** kalır. **Bu yüzden Task 1 (SPA kaldırma) KOD olarak güvenle yapılır ama Task 6 (prod deploy + DB recreate) native istemciler hazır olana KADAR uygulanmaz.**
3. **DB yeniden oluşturma yıkıcı.** Task 6 üretim SQLite'ını siler/yeniden yaratır → CLAUDE.md "destructive DB writes / prod deploys — ask first". Kullanıcı açık "evet" demeden ÇALIŞTIRILMAZ. Önce prod DB yedeği (`kasa.db` kopyası).

**Güvenli-yerel (şimdi yapılabilir):** Task 1, 2, 3. **Kapılı (kullanıcı onayı + sıralama):** Task 6. **Mac/mobil-ortam:** Task 4, 5 build adımları.

---

## File Structure

**Kasa repo (`C:\Users\burak\source\repos\Kasa`):**
- `Kasa.Api/Program.cs` — SPA middleware (satır 75-76 `UseDefaultFiles`/`UseStaticFiles`, satır 297 `MapFallbackToFile`) kaldır
- `Kasa.Api/wwwroot/` — sil (SPA placeholder)
- `Dockerfile` — node web-build aşaması (satır 2-8) + `COPY --from=web` (satır 24-25) kaldır
- `Kasa.Api.Tests/StatikServisTests.cs` — SPA testleri güncelle (artık `/` ve istemci-rotası 404; `/api` 401 korunur)
- `Kasa.Core/` — DB şema notu / recreate script (`docs/deploy/kasa-db-recreate.md`)
- `docs/deploy/windows-exe.md` — Windows `.exe` publish runbook
- `docs/deploy/android-yayin.md` — keystore + AAB build + Play Console runbook (kullanıcı adımları işaretli)
- `docs/deploy/ios-yayin.md` — Mac + Apple Developer + App Store Connect runbook
- `docs/store/` — mağaza metinleri (TR açıklama, ekran görüntüsü listesi, gizlilik)

---

## Task 1: Kasa.Api SPA sunumunu kaldır (web emekli)

**Files:**
- Modify: `Kasa.Api/Program.cs`
- Delete: `Kasa.Api/wwwroot/index.html` (+ klasör)
- Modify: `Kasa.Api.Tests/StatikServisTests.cs`

- [ ] **Step 1: Mevcut StatikServisTests'i oku ve testleri yeni davranışa çevir**

`Kasa.Api.Tests/StatikServisTests.cs` — SPA artık sunulmadığı için beklentiler değişir: kök `/` ve istemci-rotası `/haftalik` artık **404** (fallback yok); `/api/kanallar` yine **401**; `/health` yine **200**. Sınıfı şu hale getir:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kasa.Api.Tests;

public class StatikServisTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public StatikServisTests(KasaWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Kok_istegi_404_doner_spa_yok()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
    }

    [Fact]
    public async Task Istemci_rotasi_404_doner_fallback_yok()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/haftalik");
        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
    }

    [Fact]
    public async Task Api_yolu_kimliksiz_401_doner()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/api/kanallar");
        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    [Fact]
    public async Task Health_200_doner()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
    }
}
```

- [ ] **Step 2: Testi çalıştır, kırmızıyı gör**

Run: `cd "C:/Users/burak/source/repos/Kasa" && dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter StatikServisTests 2>&1 | tail -12`
Expected: `Kok_istegi_404...` ve `Istemci_rotasi_404...` FAIL (şu an 200 dönüyor — SPA hâlâ sunuluyor).

- [ ] **Step 3: Program.cs'ten SPA middleware'ini kaldır**

`Kasa.Api/Program.cs` satır 75-76'yı sil:
```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
```
ve satır 297'yi sil:
```csharp
app.MapFallbackToFile("index.html");
```
(Çevresindeki `app.UseAuthentication()`/`app.Run()` satırlarına dokunma.)

- [ ] **Step 4: wwwroot'u sil**

Run: `cd "C:/Users/burak/source/repos/Kasa" && git rm -r Kasa.Api/wwwroot`
Expected: `wwwroot/index.html` staging'den kalkar.

- [ ] **Step 5: Testleri çalıştır, yeşili doğrula**

Run: `cd "C:/Users/burak/source/repos/Kasa" && dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj 2>&1 | tail -8`
Expected: PASS — StatikServisTests 4 + mevcut testler (login/CRUD/hesap) bozulmadı.

- [ ] **Step 6: Commit**

```bash
cd "C:/Users/burak/source/repos/Kasa" && git add Kasa.Api/Program.cs Kasa.Api.Tests/StatikServisTests.cs && git commit -m "feat(api): SPA sunumunu kaldır (web emekli, native-only API)"
```

---

## Task 2: Dockerfile'dan node web-build aşamasını çıkar

**Files:**
- Modify: `Dockerfile`

- [ ] **Step 1: Mevcut Dockerfile'ı oku**

Run: `cd "C:/Users/burak/source/repos/Kasa" && cat Dockerfile` (Read tool ile) — 3 aşamalı yapıyı doğrula: `web` (node), `build` (.NET publish), `runtime`.

- [ ] **Step 2: node aşamasını ve wwwroot kopyasını kaldır**

`Dockerfile`'ı iki aşamalı yap: `web` aşamasını (satır 2-8, `FROM node:22-alpine AS web` bloğu) tamamen sil; runtime aşamasındaki `COPY --from=web /web/dist ./wwwroot` satırını (24-25 + üstündeki yorum) sil. Nihai hâl:

```dockerfile
# ---- 1) .NET publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Kasa.Core/Kasa.Core.csproj Kasa.Core/
COPY Kasa.Api/Kasa.Api.csproj Kasa.Api/
RUN dotnet restore Kasa.Api/Kasa.Api.csproj
COPY Kasa.Core/ Kasa.Core/
COPY Kasa.Api/ Kasa.Api/
RUN dotnet publish Kasa.Api/Kasa.Api.csproj -c Release -o /app/publish

# ---- 2) Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Kasa.Api.dll"]
```
(Gerçek dosyadaki satır numaralarını/başlıkları Step 1'de gördüğünle eşle; yalnız node aşamasını + wwwroot kopyasını çıkar, .NET aşamalarına dokunma.)

- [ ] **Step 3: İmajı yerel derle (Docker varsa)**

Run: `cd "C:/Users/burak/source/repos/Kasa" && docker build -t kasa-api:local . 2>&1 | tail -15`
Expected: 2 aşama başarılı; node/npm adımı YOK. (Docker yoksa bu adımı atla, sözdizimini gözle doğrula.)

- [ ] **Step 4: Commit**

```bash
cd "C:/Users/burak/source/repos/Kasa" && git add Dockerfile && git commit -m "build(api): Dockerfile'dan node web-build aşamasını çıkar (native-only)"
```

---

## Task 3: Windows paketsiz `.exe` publish + runbook

**Files:**
- Create: `docs/deploy/windows-exe.md`

- [ ] **Step 1: Windows publish'i dene**

Run: `cd "C:/Users/burak/source/repos/Kasa" && dotnet publish Kasa.App/Kasa.App.csproj -c Release -f net10.0-windows10.0.19041.0 -p:WindowsPackageType=None 2>&1 | tail -20`
Expected: Build başarılı; `bin/Release/net10.0-windows10.0.19041.0/win10-x64/` (veya benzeri) altında `Kasa.App.exe` üretilir. Hata olursa (RID gerekiyorsa) `-r win-x64 --self-contained false` ekleyip tekrar dene.

- [ ] **Step 2: Üretilen exe'yi doğrula**

Run: `cd "C:/Users/burak/source/repos/Kasa" && find Kasa.App/bin/Release -name "Kasa.App.exe" 2>/dev/null` (Glob ile)
Expected: exe yolu döner. (Elle çift-tık smoke test kullanıcıya bırakılır — MAUI headless doğrulanamaz.)

- [ ] **Step 3: windows-exe.md runbook yaz**

`docs/deploy/windows-exe.md` — publish komutu, çıktı yolu, opsiyonel imzalama (SmartScreen; iç kullanımda atlanabilir), ortaklara dağıtım (zip + çift tık), API adresi (`https://kasa.royalmezat.com/api`, `BaseAddress` MauiProgram'da).

- [ ] **Step 4: Commit**

```bash
cd "C:/Users/burak/source/repos/Kasa" && git add docs/deploy/windows-exe.md && git commit -m "docs(deploy): Windows paketsiz exe publish runbook"
```

---

## Task 4: Android — keystore + build config + Play Console runbook

**Files:**
- Create: `docs/deploy/android-yayin.md`
- (Mobil TFM + Android workload gerektiren build adımları kullanıcı ortamında; bu task RUNBOOK + config hazırlığı.)

- [ ] **Step 1: android-yayin.md runbook yaz**

`docs/deploy/android-yayin.md` içeriği:
- **Ön koşul:** `Kasa.App.csproj` TFM'ine `net10.0-android` geri eklenir (şu an yorumda); `dotnet workload install maui-android`.
- **Keystore üret** (kullanıcı, bir kez, GİZLİ sakla — kaybolursa güncelleme yayınlanamaz):
  `keytool -genkeypair -v -keystore emar-kasa.keystore -alias emar-kasa -keyalg RSA -keysize 2048 -validity 10000`
- **csproj imza bloku** (Release, `AndroidKeyStore=true` + `AndroidSigningKeyStore/Alias/StorePass/KeyPass` — şifreler env/CI secret'tan, repoya GİRMEZ).
- **AAB build:** `dotnet publish Kasa.App/Kasa.App.csproj -c Release -f net10.0-android`.
- **Play Console** ($25 tek sefer, KULLANICI): uygulama oluştur (`com.royalmezat.kasa`), AAB yükle, mağaza girişi, iç test → prod. Son "yayınla" = kullanıcı.

- [ ] **Step 2: Commit**

```bash
cd "C:/Users/burak/source/repos/Kasa" && git add docs/deploy/android-yayin.md && git commit -m "docs(deploy): Android keystore + AAB + Play Console runbook"
```

---

## Task 5: iOS + mağaza materyalleri

**Files:**
- Create: `docs/deploy/ios-yayin.md`
- Create: `docs/store/aciklama-tr.md`, `docs/store/ekran-goruntuleri.md`, `docs/store/gizlilik.md`

- [ ] **Step 1: ios-yayin.md runbook yaz**

`docs/deploy/ios-yayin.md`: Mac + Xcode + Apple Developer ($99/yıl, KULLANICI kaydolur/öder); bundle `com.royalmezat.kasa`; `dotnet publish -f net10.0-ios -c Release` (Mac'te); `.ipa` → App Store Connect (Transporter/Xcode). Son gönderim = kullanıcı.

- [ ] **Step 2: Mağaza metinleri yaz (TR)**

`docs/store/aciklama-tr.md`: uygulama adı "Emar Kasa", kısa + uzun açıklama, anahtar kelimeler, kategori (Finans). `docs/store/ekran-goruntuleri.md`: gerekli ekran-görüntüsü boyut listesi + hangi ekranlar. `docs/store/gizlilik.md`: veri toplama beyanı (sadece oturum token'ı; analitik yok) — hem Play hem App Store gizlilik formu için.

- [ ] **Step 3: Commit**

```bash
cd "C:/Users/burak/source/repos/Kasa" && git add docs/deploy/ios-yayin.md docs/store/ && git commit -m "docs(deploy): iOS runbook + mağaza materyalleri (TR)"
```

---

## Task 6: Üretim — DB yeniden oluşturma + backend redeploy (⚠️ KULLANICI ONAYI + SIRALAMA KAPISI)

> **DUR:** Bu görev CANLI VPS'e (72.61.187.202) dokunur ve **üretim verisini siler**. CLAUDE.md gereği açık onay şart. Ayrıca **native istemciler ortaklara dağıtılmadan ÇALIŞTIRILMAZ** (yoksa çalışan uygulama + veri yok kalır). Kod değişikliği yok — operasyonel runbook.

**Files:** Create: `docs/deploy/kasa-db-recreate.md` (runbook — Task 6'nın yalnız DÖKÜMANI şimdi yazılabilir; komutları koşmak kapılı)

- [ ] **Step 1: kasa-db-recreate.md runbook yaz (güvenli — sadece döküman)**
  - **Yedek önce:** `cp /opt/kasa/deploy/kasa-data/kasa.db kasa.db.$(date +%F).bak`.
  - **Neden recreate:** `EnsureCreated()` var olan DB'ye `KrediKartlari` eklemez → "no such table". Seçenek A (basit, iç kullanım, veri azsa): DB dosyasını sil, konteyner restart → `EnsureCreated()` tam şemayı + seed'i kurar. Seçenek B (veri korunacaksa): elle `CREATE TABLE KrediKartlari (...)` + kolonlar (`Id,Ad,KesimTarihi,SonOdemeTarihi,Limit,Borc`).
  - **Redeploy:** güncel imaj (SPA'sız) `docker compose up -d --build`.
  - **Doğrula:** `curl -sI https://kasa.royalmezat.com/api/kredikartlari` (401 beklenir — uç var), login sonrası GET boş liste.

- [ ] **Step 2: Runbook commit (döküman güvenli)**

```bash
cd "C:/Users/burak/source/repos/Kasa" && git add docs/deploy/kasa-db-recreate.md && git commit -m "docs(deploy): DB yeniden oluşturma + native-only redeploy runbook"
```

- [ ] **Step 3–N: Runbook'u UYGULA — KULLANICI "evet, deploy et" dedikten ve native istemciler dağıtıldıktan SONRA.** (SSH ile yedek → DB recreate → redeploy → doğrula.)

---

## Self-Review Notu

- **Spec kapsamı (§4, §10, §11.5):** login token-gövde Plan 1'de yapıldı (bu planda YOK); SPA kaldırma Task 1-2; yayın Task 3-5; backend redeploy + DB Task 6. Hepsi karşılandı.
- **Ortam gerçeği:** mobil store build'i bu Windows makinede olamaz → Task 4-5 hazırlık + runbook, gerçek build kullanıcı/Mac. Dürüstçe işaretlendi.
- **Üretim güvenliği:** Task 6 çift kapılı (kullanıcı onayı + native-hazır sıralaması); yedek zorunlu. Task 1-3 güvenli-yerel.
- **Bilinen kabul:** DB recreate iç-kullanım basit yol (veri azsa); veri korunacaksa elle CREATE TABLE alternatifi runbook'ta.
