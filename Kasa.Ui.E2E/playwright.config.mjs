// Kasa web arayüzü uçtan uca korumaları (Playwright + axe). Kasa.Api, Sunucu/ altındaki küçük .NET barındırıcısıyla geçici
// veritabanı, sabit saat ve bu koşuda üretilen test kimlikleriyle ayrı bir portta (yalnız 127.0.0.1) açılır; canlı
// sunucuya istek gitmez. Her ekran için: axe WCAG taraması (tabana göre yalnız YENİ ihlal kırar), rol bazlı erişilebilirlik
// ağacı anlık görüntüsü (platformdan bağımsız) ve KASA_E2E_EKRAN=1 iken ekran görüntüsü karşılaştırması (tabanlar yalnız
// resmi Playwright Linux imajında üretilir; bkz. linux-kosu.sh).
//
//   npm test                                   axe + ARIA (Windows/Linux/macOS)
//   KASA_E2E_EKRAN=1 npm test                  + ekran görüntüleri (yalnız Playwright Docker imajında anlamlı)
//   KASA_E2E_ERISIM=0 KASA_E2E_EKRAN=1 ...     yalnız ekran görüntüleri
//   npm test -- --update-snapshots=changed     ARIA/ekran tabanlarını yenile (farkı incelemeden işlemeyin)
//   KASA_E2E_AXE_TABANI=kucult npm test        düzelen axe ihlallerini tabandan çıkar (taban yalnız küçülür)
import { randomBytes } from 'node:crypto';
import { defineConfig, devices } from '@playwright/test';

const port = Number(process.env.KASA_E2E_PORT || 5390);
const baseURL = `http://127.0.0.1:${port}`;
// Test kimlikleri her koşuda üretilir (gerçek şifre yok). Yapılandırma işçilerde yeniden yüklenir; ana süreçte üretilen
// değerler ortamdan devralındığı için sunucu, kurulum ve testler aynı değerleri görür.
process.env.KASA_E2E_KULLANICI ||= 'e2e-editor';
process.env.KASA_E2E_SIFRE ||= randomBytes(18).toString('base64url');
process.env.KASA_E2E_JWT ||= randomBytes(48).toString('base64url');
process.env.KASA_E2E_BUGUN ||= '2026-09-25';
// Sunucu önceden derlendiyse (CI) yeniden derlenmez.
const derlemeYok = process.env.KASA_E2E_DERLENDI === '1' ? ' --no-build' : '';

const telefon = (ad, genislik, yukseklik, cihaz) => ({
  name: ad,
  testMatch: /telefon\.spec\.mjs/,
  dependencies: ['kurulum'],
  use: {
    browserName: 'chromium',
    userAgent: devices[cihaz].userAgent,
    viewport: { width: genislik, height: yukseklik },
    deviceScaleFactor: 1,
    isMobile: true,
    hasTouch: true,
  },
});

export default defineConfig({
  testDir: 'testler',
  outputDir: 'test-results',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  // Taban güncelleme tek dosyaya sırayla yazar.
  workers: process.env.KASA_E2E_AXE_TABANI ? 1 : 2,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  timeout: 60_000,
  expect: {
    timeout: 15_000,
    toHaveScreenshot: {
      pathTemplate: '{testDir}/__ekran__/{projectName}/{arg}{ext}',
      animations: 'disabled',
      caret: 'hide',
      scale: 'css',
      maxDiffPixels: 20,
    },
    toMatchAriaSnapshot: { pathTemplate: '{testDir}/__aria__/{projectName}/{arg}{ext}' },
  },
  use: {
    baseURL,
    locale: 'tr-TR',
    timezoneId: 'Europe/Istanbul',
    colorScheme: 'light',
    serviceWorkers: 'block',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'kurulum', testMatch: /kurulum\.setup\.mjs/ },
    {
      name: 'masaustu-1280',
      testMatch: /masaustu\.spec\.mjs/,
      dependencies: ['kurulum'],
      use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 },
    },
    telefon('telefon-360', 360, 780, 'Pixel 7'),
    telefon('telefon-375', 375, 812, 'iPhone 13'),
  ],
  webServer: {
    command: `dotnet run --project Sunucu/Kasa.Ui.E2E.Sunucu.csproj -c Release --no-launch-profile${derlemeYok}`,
    url: `${baseURL}/health`,
    timeout: 240_000,
    reuseExistingServer: false,
    stdout: 'pipe',
    stderr: 'pipe',
    gracefulShutdown: { signal: 'SIGTERM', timeout: 10_000 },
    env: {
      KASA_E2E_PORT: String(port),
      KASA_E2E_BUGUN: process.env.KASA_E2E_BUGUN,
      Kasa__EditorKullanici: process.env.KASA_E2E_KULLANICI,
      Kasa__EditorSifre: process.env.KASA_E2E_SIFRE,
      Kasa__JwtKey: process.env.KASA_E2E_JWT,
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      DOTNET_NOLOGO: '1',
    },
  },
});
