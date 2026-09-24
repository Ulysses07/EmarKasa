# Kasa Defteri — Dağıtım (Docker + Caddy) Implementation Plan

> **Tarihsel plan:** Bu belge yazıldığı günün planıdır; içindeki `EnsureCreated`/elle SQL/DB yeniden oluşturma adımları artık geçersizdir — şema açılışta `SemaGuncelleyici` ile güncellenir, dağıtım/yedek için `deploy/README.md`'ye bakın.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kasa Defteri'yi (Kasa.Api + derlenmiş React SPA) tek bir Docker konteynerinde paketleyip, mevcut OrderDeck VPS'inde çalışan `orderdeck-caddy` arkasında `kasa.emarglobal.com` alt alan adında yayına almak.

**Architecture:** Tek konteyner deseni. `Kasa.Api` hem `/api/*` uçlarını sunar HEM de `wwwroot/` altına kopyalanmış React derleme çıktısını statik dosya olarak servis eder ve istemci-tarafı rotalar için `index.html`'e fallback yapar. Böylece React↔API planındaki "aynı-origin, CORS yok, `credentials: include`" varsayımı üretimde de kendiliğinden doğru olur. Çok aşamalı bir Dockerfile önce React'i (node) derler, sonra .NET'i publish eder, üçüncü aşamada React çıktısını `wwwroot`'a koyup runtime imajını üretir. Konteyner, OrderDeck compose'unun `web` ağına harici (external) olarak bağlanır; Caddy'ye tek bir subdomain bloğu eklenir. SQLite DB dosyası bir bind-mount volume'de yaşar. Üretimde JwtKey + editör kullanıcı/şifre env'den gelir, çerez `Secure=true` olur.

**Tech Stack:** .NET 10 (`Microsoft.NET.Sdk.Web`), ASP.NET Core minimal API, EF Core 10 Sqlite, React 19 + Vite 8 build, Docker multi-stage (`node:22-alpine` + `mcr.microsoft.com/dotnet/sdk:10.0` + `mcr.microsoft.com/dotnet/aspnet:10.0`), Docker Compose (harici `web` ağı), Caddy 2 (mevcut `orderdeck-caddy`, Let's Encrypt otomatik TLS).

**Ön koşul:** React↔API bağlama planı (`2026-07-14-kasa-react-api-baglama.md`) TAMAMLANMIŞ ve `master`'da olmalı — bu plan derlenmiş, API'ye bağlı bir React uygulaması olduğunu varsayar. Vite build çıktısı `web/dist/` altına üretilir (Vite varsayılanı).

**Üretim dokunuşu uyarısı:** Task 7 gerçek VPS'e deploy eder (DNS + prod konteyner). Bu ADIM kullanıcının açık onayı olmadan ÇALIŞTIRILMAZ (CLAUDE.md: "prod deploys — ask first"). Task 1-6 tamamen yerel/repo değişiklikleridir, güvenle yapılabilir.

---

## File Structure

**Kasa repo (`<repo>`) — oluşturulacak/değişecek:**
- `Kasa.Api/Kasa.Api.csproj` — SQLitePCLRaw yamalı sürüm pin (NU1903 temizle)
- `Kasa.Api/Program.cs` — statik dosya servis + SPA fallback + çevreye göre çerez `Secure`
- `Kasa.Api/wwwroot/index.html` — placeholder (Docker build gerçek React çıktısıyla ezer; test için gerekli)
- `Kasa.Api.Tests/StatikServisTests.cs` — SPA servis + fallback + /api korunması entegrasyon testleri
- `Dockerfile` — repo kökünde, çok aşamalı (React + .NET)
- `.dockerignore` — repo kökünde
- `deploy/docker-compose.yml` — tek `kasa` servisi, harici `web` ağı, DB volume, env
- `deploy/.env.example` — üretim env şablonu (gizli değerler DEĞİL, sadece anahtar isimleri)
- `deploy/README.md` — VPS deploy runbook

**LiveDeck repo (`<LiveDeck-repo>`) — değişecek:**
- `deploy/Caddyfile` — `kasa.emarglobal.com` reverse_proxy bloğu

---

## Task 1: SQLitePCLRaw yamalı sürüm pin (NU1903 temizle)

**Files:**
- Modify: `Kasa.Api/Kasa.Api.csproj`

- [ ] **Step 1: Mevcut açığı gör**

Run:
```bash
cd <repo>
dotnet list Kasa.Api/Kasa.Api.csproj package --vulnerable --include-transitive
```
Expected: `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 için `GHSA-2m69-gcr7-jv3q` (High) transitive olarak listelenir.

- [ ] **Step 2: Yamalı sürümü bul**

Run:
```bash
dotnet package search SQLitePCLRaw.bundle_e_sqlite3 --take 5
```
Expected: Yamalı en güncel sürüm (2.1.11'den büyük; ör. 2.1.12+) listelenir. Bir sonraki adımda bu sürümü kullan. Eğer 2.1.11'den yeni bir yama YOKSA (advisory henüz kapanmamışsa), pin ekleme — bunun yerine csproj'a bir yorum düş (`<!-- NU1903: SQLitePCLRaw yaması bekleniyor, henüz yayınlanmadı -->`) ve Step 5'i "hâlâ 1 uyarı, bloke değil" olarak kabul et. Advisory notu bu senaryoyu bekliyor.

- [ ] **Step 3: Direkt PackageReference ekle**

`Kasa.Api/Kasa.Api.csproj` içindeki mevcut `ItemGroup`'a (EF Core paketlerinin yanına) yamalı sürümü direkt referans olarak ekle — direkt referans transitive'i override eder:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.9" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.9" />
    <!-- NU1903 (GHSA-2m69-gcr7-jv3q): EF Sqlite'ın çektiği 2.1.11 yerine yamalıyı pinle. -->
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.12" />
  </ItemGroup>
```
(Step 2'de bulunan gerçek yamalı sürümü yaz; 2.1.12 örnektir.)

- [ ] **Step 4: Geri yükle + derle**

Run:
```bash
dotnet restore Kasa.Api/Kasa.Api.csproj
dotnet build Kasa.Api/Kasa.Api.csproj -c Release
```
Expected: Derleme başarılı, NU1903 uyarısı YOK.

- [ ] **Step 5: Açık taramasını tekrarla**

Run:
```bash
dotnet list Kasa.Api/Kasa.Api.csproj package --vulnerable --include-transitive
```
Expected: "no vulnerable packages" VEYA (Step 2'deki istisna geçerliyse) yalnızca aynı tek uyarı — regresyon yok.

- [ ] **Step 6: Testlerin hâlâ yeşil olduğunu doğrula**

Run:
```bash
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj
```
Expected: PASS (mevcut 14 test).

- [ ] **Step 7: Commit**

```bash
git add Kasa.Api/Kasa.Api.csproj
git commit -m "chore(api): SQLitePCLRaw yamalı sürüme pinle (NU1903)"
```

---

## Task 2: Kasa.Api React SPA'yı sunar (statik dosya + fallback)

**Files:**
- Create: `Kasa.Api/wwwroot/index.html`
- Modify: `Kasa.Api/Program.cs` (statik middleware + `MapFallbackToFile`)
- Test: `Kasa.Api.Tests/StatikServisTests.cs`

- [ ] **Step 1: Placeholder index.html oluştur**

Docker build gerçek React çıktısıyla `wwwroot`'u dolduracak; ama teste ve local çalışmaya bir tutamak lazım. `Kasa.Api/wwwroot/index.html`:

```html
<!doctype html>
<html lang="tr">
  <head><meta charset="utf-8" /><title>Kasa Defteri</title></head>
  <body><div id="root"></div><!-- KASA_SPA_PLACEHOLDER --></body>
</html>
```

- [ ] **Step 2: Başarısız testi yaz**

`Kasa.Api.Tests/StatikServisTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Kasa.Api.Tests;

public class StatikServisTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _factory;
    public StatikServisTests(KasaWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Kok_istegi_index_html_doner()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.Contains("KASA_SPA_PLACEHOLDER", govde);
    }

    [Fact]
    public async Task Istemci_rotasi_index_html_e_fallback_yapar()
    {
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/haftalik");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.Contains("KASA_SPA_PLACEHOLDER", govde);
    }

    [Fact]
    public async Task Api_yolu_fallback_yerine_401_doner()
    {
        // Kimliksiz /api çağrısı HTML fallback'e DÜŞMEMELİ; auth 401 dönmeli.
        var client = _factory.CreateClient();
        var yanit = await client.GetAsync("/api/kanallar");
        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }
}
```

- [ ] **Step 3: Testi çalıştır, başarısız olduğunu doğrula**

Run:
```bash
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter StatikServisTests
```
Expected: `Kok_istegi_index_html_doner` ve `Istemci_rotasi_...` FAIL (404 — statik/fallback middleware henüz yok). `Api_yolu_...` muhtemelen PASS (zaten 401).

- [ ] **Step 4: Program.cs'e statik servis + fallback ekle**

`Program.cs` içinde `app.UseAuthentication();` satırından ÖNCE statik dosya middleware'ini ekle (statik dosyalar auth gerektirmez):

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
```

Ve `app.Run();` satırından HEMEN ÖNCE (tüm map'lerden sonra) SPA fallback'i ekle:

```csharp
// React istemci-tarafı rotaları (/haftalik, /aylik, ...) index.html'e düşer.
// /api ve /health zaten eşleştiği için buraya gelmez; eşleşmeyen /api/* için
// aşağıdaki guard 404 üretir (HTML fallback yerine).
app.MapFallbackToFile("index.html");

app.Run();
```

- [ ] **Step 5: Testi çalıştır, geçtiğini doğrula**

Run:
```bash
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter StatikServisTests
```
Expected: 3 test de PASS.

> Not: `Api_yolu_fallback_yerine_401_doner` geçer çünkü `/api/kanallar` haritalanmış bir rotadır ve `RequireAuthorization` 401 üretir — fallback yalnızca HİÇBİR endpoint eşleşmediğinde devreye girer.

- [ ] **Step 6: Tüm testleri çalıştır (regresyon yok)**

Run:
```bash
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj
```
Expected: PASS (14 + 3 = 17 test).

- [ ] **Step 7: Commit**

```bash
git add Kasa.Api/Program.cs Kasa.Api/wwwroot/index.html Kasa.Api.Tests/StatikServisTests.cs
git commit -m "feat(api): React SPA'yı statik sun + istemci rotaları index.html'e fallback"
```

---

## Task 3: Çerez Secure bayrağını çevreye bağla

**Files:**
- Modify: `Kasa.Api/Program.cs` (login çerezi)

- [ ] **Step 1: Başarısız testi yaz**

`Kasa.Api.Tests/StatikServisTests.cs`'in sonuna (aynı sınıf içine) ekle. Test ortamı Development değildir (`WebApplicationFactory` varsayılan environment = "Development" → çerez `Secure=false` olur). Bu yüzden çevre-koşullu davranışı doğrulamak için Production environment'lı bir client gerekir. `KasaWebFactory`'nin environment'ını override edebilmek adına ayrı bir test factory kullanmak yerine, mevcut Development davranışını (Secure=false, HTTP testleri kırmaz) ve mantığın varlığını doğrulayalım:

```csharp
    [Fact]
    public async Task Development_te_login_cerezi_secure_degil()
    {
        // Editör env KasaWebFactory tarafından set edilir; başarılı login çerezi döner.
        var client = _factory.CreateClient();
        var giris = await client.PostAsJsonAsync("/api/auth/login",
            new { kullanici = "editor", sifre = "editor-test-sifre" });
        giris.EnsureSuccessStatusCode();
        var setCookie = Assert.Single(giris.Headers.GetValues("Set-Cookie"));
        Assert.Contains("kasa_auth=", setCookie);
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }
```

> `KasaWebFactory.EditorClientAsync()`'in kullandığı editör kullanıcı/şifre değerlerini `KasaWebFactory` kaynağından teyit et; yukarıdaki `"editor"`/`"editor-test-sifre"` değerlerini oradaki gerçek değerlerle değiştir.

- [ ] **Step 2: Testi çalıştır**

Run:
```bash
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj --filter Development_te_login_cerezi_secure_degil
```
Expected: PASS (çerez zaten `Secure=false`, henüz mantığı değiştirmedik). Bu test çevre-koşullu değişiklikten SONRA da Development'ta yeşil kalmalı — yani regresyon bekçisi.

- [ ] **Step 3: Program.cs'te çerez Secure'ü çevreye bağla**

`var app = builder.Build();` satırından hemen sonra bir bayrak yakala:

```csharp
var app = builder.Build();

// Üretimde (Caddy TLS arkasında) çerez yalnızca HTTPS'te gitmeli.
var cerezSecure = !app.Environment.IsDevelopment();
```

Login handler'daki `Secure = false,` satırını değiştir:

```csharp
    http.Response.Cookies.Append("kasa_auth", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = cerezSecure,
        MaxAge = TimeSpan.FromDays(30),
    });
```

- [ ] **Step 4: Testleri çalıştır**

Run:
```bash
dotnet test Kasa.Api.Tests/Kasa.Api.Tests.csproj
```
Expected: PASS (17 + 1 = 18 test). Development'ta `cerezSecure=false`, davranış değişmez.

- [ ] **Step 5: Commit**

```bash
git add Kasa.Api/Program.cs Kasa.Api.Tests/StatikServisTests.cs
git commit -m "feat(api): login çerezini üretimde Secure yap (Caddy TLS)"
```

---

## Task 4: Çok aşamalı Dockerfile

**Files:**
- Create: `Dockerfile` (repo kökü)
- Create: `.dockerignore` (repo kökü)

- [ ] **Step 1: .dockerignore oluştur**

`.dockerignore`:

```
**/bin
**/obj
**/node_modules
web/dist
**/.vs
*.db
*.db-shm
*.db-wal
.git
.gitignore
docs
**/TestResults
```

- [ ] **Step 2: Dockerfile oluştur**

Repo kökünde `Dockerfile`:

```dockerfile
# ---- 1) React derleme ----
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json* ./
RUN npm ci
COPY web/ ./
# Aynı-origin: VITE_API_URL boş → istekler /api'ye relative gider.
RUN npm run build

# ---- 2) .NET publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Kasa.Core/Kasa.Core.csproj Kasa.Core/
COPY Kasa.Api/Kasa.Api.csproj Kasa.Api/
RUN dotnet restore Kasa.Api/Kasa.Api.csproj
COPY Kasa.Core/ Kasa.Core/
COPY Kasa.Api/ Kasa.Api/
RUN dotnet publish Kasa.Api/Kasa.Api.csproj -c Release -o /app/publish

# ---- 3) Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
# React derleme çıktısını wwwroot'a koy (placeholder index.html ezilir).
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Kasa.Api.dll"]
```

> Not: `web/package-lock.json` yoksa `COPY web/package-lock.json* ./` satırı sessizce atlanır ve `npm ci` hata verir. React↔API planı bir lock dosyası üretmiş olmalı. Yoksa `npm ci` yerine `npm install` kullan VEYA önce `npm install` ile lock üret.

- [ ] **Step 3: İmajı yerel derle**

Run:
```bash
cd <repo>
docker build -t kasa:local .
```
Expected: 3 aşama da başarılı; sonda `kasa:local` imajı oluşur.

- [ ] **Step 4: Konteyneri yerel duman testinden geçir**

Run:
```bash
docker run --rm -d --name kasa-smoke -p 8099:8080 \
  -e Kasa__JwtKey="yerel-duman-testi-anahtari-en-az-32-karakter-uzun!!" \
  -e Kasa__EditorKullanici="editor" \
  -e Kasa__EditorSifre="duman-sifre" \
  kasa:local
```
Sonra:
```bash
curl -s http://localhost:8099/health
curl -s http://localhost:8099/ | head -c 200
```
Expected: `/health` → `{"durum":"ok"}`; `/` → gerçek React `index.html` (Vite'ın ürettiği `<script type="module">` etiketleri; artık placeholder yorumu YOK).

- [ ] **Step 5: Duman konteynerini durdur**

Run:
```bash
docker stop kasa-smoke
```

- [ ] **Step 6: Commit**

```bash
git add Dockerfile .dockerignore
git commit -m "build: Kasa çok aşamalı Dockerfile (React + .NET tek imaj)"
```

---

## Task 5: Deploy compose + env şablonu

**Files:**
- Create: `deploy/docker-compose.yml`
- Create: `deploy/.env.example`
- Create: `deploy/README.md`

- [ ] **Step 1: deploy/docker-compose.yml oluştur**

Kasa konteyneri, OrderDeck compose'unun ağına harici bağlanır (OrderDeck `/opt/orderdeck` dizininde çalıştığı için ağ adı `orderdeck_web`). SQLite DB bir bind-mount'ta yaşar.

`deploy/docker-compose.yml`:

```yaml
services:
  kasa:
    build:
      context: ..
      dockerfile: Dockerfile
    image: kasa:latest
    container_name: kasa-app
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: "http://+:8080"
      ConnectionStrings__Kasa: "Data Source=/data/kasa.db"
      Kasa__JwtKey: "${KASA_JWT_KEY}"
      Kasa__EditorKullanici: "${KASA_EDITOR_KULLANICI}"
      Kasa__EditorSifre: "${KASA_EDITOR_SIFRE}"
    volumes:
      - ./kasa-data:/data
    networks:
      - web
    expose:
      - "8080"
    mem_limit: 512m
    memswap_limit: 512m
    cpus: 0.5

networks:
  web:
    # OrderDeck compose'unun oluşturduğu ağ (proje adı 'orderdeck' → 'orderdeck_web').
    external: true
    name: orderdeck_web
```

> `ConnectionStrings__Kasa` = `Data Source=/data/kasa.db` → `Program.cs`'teki `GetConnectionString("Kasa") ?? "Data Source=kasa.db"` bunu okur ve DB'yi bind-mount'lu `/data`'ya yazar (kalıcı).

- [ ] **Step 2: deploy/.env.example oluştur**

Gerçek sırlar DEĞİL — sadece şablon. VPS'te `.env` olarak kopyalanıp doldurulacak.

`deploy/.env.example`:

```
# Kasa Defteri üretim ortam değişkenleri.
# Bu dosyayı VPS'te `deploy/.env` olarak kopyala ve gerçek değerlerle doldur.
# .env ASLA repoya commit edilmez (aşağıdaki .gitignore adımına bak).

# JWT imzalama anahtarı — en az 32 karakter, rastgele. Üret:
#   openssl rand -base64 48
KASA_JWT_KEY=

# Editör (veri girişi yapan ortak) kullanıcı adı + şifresi.
KASA_EDITOR_KULLANICI=
KASA_EDITOR_SIFRE=
```

- [ ] **Step 3: .gitignore'a deploy sırlarını ekle**

Repo kökündeki `.gitignore`'a ekle (yoksa oluştur):

```
# Deploy sırları ve yerel DB
deploy/.env
deploy/kasa-data/
```

- [ ] **Step 4: deploy/README.md runbook oluştur**

`deploy/README.md`:

```markdown
# Kasa Defteri — VPS Dağıtım

Aynı OrderDeck VPS'inde, `orderdeck-caddy` arkasında `kasa.emarglobal.com`.

## İlk kurulum
1. DNS: `kasa.emarglobal.com` A kaydı → OrderDeck VPS IP'si.
2. Kasa reposunu VPS'e kopyala (repo remote'u yok → rsync/scp):
   `rsync -az --exclude bin --exclude obj --exclude node_modules \
     ./ user@VPS:/opt/kasa/`
3. VPS'te env doldur:
   `cd /opt/kasa/deploy && cp .env.example .env && nano .env`
   (KASA_JWT_KEY = `openssl rand -base64 48`, editör kullanıcı/şifre)
4. OrderDeck ağının ayakta olduğunu doğrula:
   `docker network ls | grep orderdeck_web`
   (yoksa önce OrderDeck compose `up` edilmeli — Caddy zaten çalışıyor olmalı)
5. Kasa'yı derle + başlat:
   `cd /opt/kasa/deploy && docker compose up -d --build`
6. Caddy'yi güncelle (LiveDeck reposundaki Caddyfile'a kasa bloğu eklendikten
   sonra `/opt/orderdeck/Caddyfile`'a yansıt) ve reload:
   `docker exec orderdeck-caddy caddy reload --config /etc/caddy/Caddyfile`
7. Doğrula: `curl -sI https://kasa.emarglobal.com/health`

## Güncelleme (yeni sürüm)
1. `rsync ... /opt/kasa/`
2. `cd /opt/kasa/deploy && docker compose up -d --build`
   (DB `kasa-data/` volume'de kalıcı — kaybolmaz)

## Yedek
DB tek dosya: `/opt/kasa/deploy/kasa-data/kasa.db`. Yedek = dosyayı kopyala.
```

- [ ] **Step 5: Compose config'in geçerli olduğunu doğrula (harici ağ hariç)**

Run:
```bash
cd <repo>deploy"
docker compose config
```
Expected: YAML hatasız parse edilir ve birleşik config basılır. (Harici `orderdeck_web` ağı yerel makinede yoksa `up` başarısız olur — bu beklenen; burada yalnızca `config` ile sözdizimini doğruluyoruz.)

- [ ] **Step 6: Commit**

```bash
git add deploy/docker-compose.yml deploy/.env.example deploy/README.md .gitignore
git commit -m "build: Kasa deploy compose + env şablonu + runbook"
```

---

## Task 6: Caddy subdomain bloğu (LiveDeck reposu)

**Files:**
- Modify: `<LiveDeck-repo>\deploy\Caddyfile`

- [ ] **Step 1: Caddyfile'a kasa bloğu ekle**

`license.orderdeckapp.com { ... }` bloğunun HEMEN ARDINA, marketing bloğundan önce ekle:

```
kasa.emarglobal.com {
    import security_headers
    encode gzip zstd
    reverse_proxy kasa:8080
}
```

> `kasa` = Kasa compose'undaki `container_name: kasa-app` DEĞİL, servis DNS adı. Compose harici `orderdeck_web` ağına bağlandığı için Caddy container'ı servis adıyla çözer. **Önemli:** Docker DNS servis adını (`kasa`) VEYA container_name'i (`kasa-app`) çözer; aynı ağdaki container'lar için `container_name` de geçerli bir hostname'dir. Netlik için `reverse_proxy kasa-app:8080` de yazılabilir — `container_name: kasa-app` sabit olduğundan bunu tercih et:

```
kasa.emarglobal.com {
    import security_headers
    encode gzip zstd
    reverse_proxy kasa-app:8080
}
```

- [ ] **Step 2: Caddyfile sözdizimini doğrula**

Run:
```bash
cd <LiveDeck-repo>
docker run --rm -v "$(pwd)/deploy/Caddyfile:/etc/caddy/Caddyfile:ro" caddy:2-alpine caddy validate --config /etc/caddy/Caddyfile
```
Expected: `Valid configuration`.

- [ ] **Step 3: Commit (LiveDeck reposu)**

```bash
cd <LiveDeck-repo>
git add deploy/Caddyfile
git commit -m "feat(deploy): kasa.emarglobal.com reverse_proxy bloğu"
```

> Not: Bu commit LiveDeck reposunda `feat/web-data-deletion` ya da yeni bir `feat/kasa-caddy` dalında olabilir. Kullanıcıyla dal/merge stratejisini teyit et — LiveDeck reposunun REMOTE'u VAR (Kasa'nınki yok), yani buradaki değişiklik `master`'a merge edilip push edilince CI/deploy tetiklenebilir. Push ETME, kullanıcı onayı bekle.

---

## Task 7: VPS'e canlı deploy (KULLANICI ONAYI GEREKLİ)

> **DUR:** Bu görev üretim VPS'ine dokunur (DNS + prod konteyner + Caddy reload). CLAUDE.md gereği prod deploy öncesi kullanıcıdan açık onay al. Aşağıdaki adımlar bir RUNBOOK'tur; kullanıcı "evet, deploy et" demeden ÇALIŞTIRMA. Adımların çoğu VPS'te SSH ile elle koşulur.

**Files:** (kod değişikliği yok — operasyonel)

- [ ] **Step 1: DNS kaydını doğrula**

`kasa.emarglobal.com` A kaydı OrderDeck VPS IP'sine işaret ediyor mu? (Cloudflare/registrar paneli.) Yoksa ekle, yayılmayı bekle:
```bash
nslookup kasa.emarglobal.com
```
Expected: VPS IP'si döner.

- [ ] **Step 2: Repoyu VPS'e kopyala**

```bash
rsync -az --exclude bin --exclude obj --exclude node_modules --exclude deploy/kasa-data \
  <repo> user@VPS:/opt/kasa/
```

- [ ] **Step 3: VPS'te env doldur**

SSH ile:
```bash
cd /opt/kasa/deploy
cp .env.example .env
# .env düzenle: KASA_JWT_KEY=$(openssl rand -base64 48), editör kullanıcı/şifre
nano .env
```

- [ ] **Step 4: OrderDeck ağının ayakta olduğunu doğrula**

```bash
docker network ls | grep orderdeck_web
```
Expected: `orderdeck_web` listelenir. Yoksa OrderDeck compose çalışmıyor demektir — önce onu `up` et.

- [ ] **Step 5: Kasa'yı derle + başlat**

```bash
cd /opt/kasa/deploy && docker compose up -d --build
docker compose logs --tail 30 kasa
```
Expected: Konteyner ayakta, log'da "Now listening on: http://[::]:8080" ve DB seed mesajları.

- [ ] **Step 6: Caddyfile'ı VPS'e yansıt + reload**

LiveDeck reposundaki güncel Caddyfile'ı VPS'teki `/opt/orderdeck/Caddyfile`'a kopyala (deploy akışın nasılsa onunla; ör. LiveDeck master merge sonrası CI ya da elle rsync), sonra:
```bash
docker exec orderdeck-caddy caddy reload --config /etc/caddy/Caddyfile
```
Expected: Reload başarılı; Caddy `kasa.emarglobal.com` için Let's Encrypt sertifikası alır (ilk istekte).

- [ ] **Step 7: Uçtan uca doğrula**

```bash
curl -sI https://kasa.emarglobal.com/health
curl -s https://kasa.emarglobal.com/health
```
Expected: `200 OK`, geçerli TLS, `{"durum":"ok"}`.

Tarayıcıda `https://kasa.emarglobal.com` aç → React uygulaması yüklenir, editör olarak login ol → haftalık rapor Haziran rakamlarını gösterir. Mobilden aç → izleyici şifresiyle salt-görüntüleme.

- [ ] **Step 8: DB kalıcılığını doğrula**

```bash
ls -la /opt/kasa/deploy/kasa-data/kasa.db
docker compose -f /opt/kasa/deploy/docker-compose.yml restart kasa
# restart sonrası veriler durmalı
```
Expected: `kasa.db` dosyası var; restart sonrası girilen veri korunur.

---

## Self-Review Notu

- **Spec kapsamı:** Bu plan yalnızca DAĞITIM alt sistemini kapsar; React↔API bağlama ayrı planda (ön koşul). AY SONUCU/hesap doğruluğu Plan 1'de test edildi, burada dokunulmaz.
- **Üretim güvenliği:** Task 7 açıkça kullanıcı onayına kapılı. Task 1-6 yerel/repo, güvenli.
- **Bilinen kabul:** `SQLitePCLRaw` yaması henüz yoksa Task 1 tek uyarıyı bloke-değil olarak bırakır (advisory notu bunu öngörüyor).
- **İncelemede kalan minör (bu plan dışı, gelecekte):** editör şifre karşılaştırması constant-time değil; Gelenler'de (DonemStart,Kanal) unique index yok. Bunlar deploy'u blokelemiyor.
