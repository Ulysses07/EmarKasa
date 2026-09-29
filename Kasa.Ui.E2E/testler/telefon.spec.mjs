import { expect, test } from '@playwright/test';
import { OTURUM, denetle, sayfayiHazirla } from './ortak.mjs';

// Telefon arayüzü (/m/): 360 px Android, 375 px iOS görünümü (platform kullanıcı aracısından seçilir). Ekranlar: giriş,
// panel (ana sayfa), haftalık, aylık, işlemler ve açık alt sayfa (hızlı işlem). Telefon arayüzünde alışlar ve kasa
// kontrolü ekranı yoktur (masaüstü görünümünde); bu yüzden burada sınanmaz. Alt sayfa gönderilmez; veri değişmez.
test.beforeEach(async ({ page }) => sayfayiHazirla(page));

test.describe('oturum açılmadan', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('giriş', async ({ page }, testInfo) => {
    await page.goto('/m/');
    await expect(page.getByRole('button', { name: 'Giriş yap' })).toBeVisible();
    await denetle(page, testInfo, 'giris');
  });
});

test.describe('editör', () => {
  test.use({ storageState: OTURUM });

  async function ac(page, ekran, baslik) {
    await page.goto(`/m/#${ekran}`);
    await expect(page.getByRole('heading', { level: 1, name: baslik })).toBeVisible();
    await expect(page.locator('#ekran .yukleniyor')).toHaveCount(0);
  }

  test('panel', async ({ page }, testInfo) => {
    await ac(page, 'panel', 'Panel');
    await expect(page.getByText('Kanal bakiyeleri')).toBeVisible();
    await denetle(page, testInfo, 'panel');
  });

  test('haftalık', async ({ page }, testInfo) => {
    await ac(page, 'haftalik', 'Haftalık');
    await denetle(page, testInfo, 'haftalik');
  });

  test('aylık', async ({ page }, testInfo) => {
    await ac(page, 'aylik', 'Aylık');
    await denetle(page, testInfo, 'aylik');
  });

  test('işlemler', async ({ page }, testInfo) => {
    await ac(page, 'islemler', 'İşlemler');
    await expect(page.getByText('Toptancı ödemesi').first()).toBeVisible();
    await denetle(page, testInfo, 'islemler');
  });

  test('hızlı işlem alt sayfası', async ({ page }, testInfo) => {
    await ac(page, 'panel', 'Panel');
    // Android'de yüzen "İşlem" düğmesi, iOS'ta başlıktaki "Hızlı işlem" düğmesi açar.
    await page.getByRole('button', { name: /^(İşlem|Hızlı işlem)$/ }).click();
    const sayfa = page.getByRole('dialog', { name: 'Hızlı işlem' });
    await expect(sayfa).toBeVisible();
    await denetle(page, testInfo, 'hizli-islem', { kok: sayfa });
  });
});
