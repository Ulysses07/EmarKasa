import { expect, test } from '@playwright/test';
import { OTURUM, denetle, sayfayiHazirla } from './ortak.mjs';

// Masaüstü web arayüzü (1280 px): giriş, ana sayfa, işlemler, alışlar, aylık kasa, kasa kontrolü (gerçek bakiye
// karşılaştırması penceresi) ve açık bir form penceresi (gider kaydı). Pencereler gönderilmez; veri değişmez.
test.beforeEach(async ({ page }) => sayfayiHazirla(page));

test.describe('oturum açılmadan', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('giriş', async ({ page }, testInfo) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Defterinizi açın' })).toBeVisible();
    await denetle(page, testInfo, 'giris');
  });
});

test.describe('editör', () => {
  test.use({ storageState: OTURUM });

  async function ac(page, menu, baslik = menu) {
    await page.goto('/');
    await expect(page.locator('#page-title')).toHaveText('Kasalar');
    if (menu) {
      await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('button', { name: menu, exact: true }).click();
      await expect(page.locator('#page-title')).toHaveText(baslik);
    }
    await expect(page.locator('#view')).toHaveAttribute('aria-busy', 'false');
  }

  async function anaSayfa(page) {
    await ac(page);
    await expect(page.getByRole('heading', { name: 'Gerçek bakiye karşılaştırmaları' })).toBeVisible();
  }

  async function pencere(page, dugme, baslik) {
    await page.getByRole('button', { name: dugme, exact: true }).click();
    const diyalog = page.getByRole('dialog', { name: baslik });
    await expect(diyalog).toBeVisible();
    return diyalog;
  }

  test('ana sayfa', async ({ page }, testInfo) => {
    await anaSayfa(page);
    await denetle(page, testInfo, 'ana-sayfa');
  });

  test('işlemler', async ({ page }, testInfo) => {
    await ac(page, 'İşlemler');
    await expect(page.getByText('Toptancı ödemesi').first()).toBeVisible();
    await denetle(page, testInfo, 'islemler');
  });

  test('alışlar', async ({ page }, testInfo) => {
    await ac(page, 'Alışlar');
    await expect(page.getByText('Ege Ambalaj').first()).toBeVisible();
    await denetle(page, testInfo, 'alislar');
  });

  test('aylık kasa', async ({ page }, testInfo) => {
    await ac(page, 'Aylık kasa');
    await denetle(page, testInfo, 'aylik');
  });

  test('kasa kontrolü', async ({ page }, testInfo) => {
    await anaSayfa(page);
    const diyalog = await pencere(page, 'Gerçek bakiye ile karşılaştır', 'Genel kasa bakiyesini karşılaştır');
    await denetle(page, testInfo, 'kasa-kontrolu', { kok: diyalog });
  });

  test('gider kaydı penceresi', async ({ page }, testInfo) => {
    await anaSayfa(page);
    const diyalog = await pencere(page, '+ Gider kaydet', 'Gider kaydet');
    await denetle(page, testInfo, 'gider-penceresi', { kok: diyalog });
  });
});
