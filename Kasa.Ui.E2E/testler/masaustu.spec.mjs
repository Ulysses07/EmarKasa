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

  test('değişen gider taslağı Vazgeç, çarpı ve Escape ile onaysız kaybolmaz', async ({ page }) => {
    await anaSayfa(page);
    const openExpense = () => pencere(page, '+ Gider kaydet', 'Gider kaydet');
    const askToClose = async (action, accept) => {
      const promptEvent = page.waitForEvent('dialog');
      const actionDone = action();
      const prompt = await promptEvent;
      expect(prompt.type()).toBe('confirm');
      expect(prompt.message()).toContain('Kaydedilmemiş değişiklikler');
      if (accept) await prompt.accept();
      else await prompt.dismiss();
      await actionDone;
    };

    let diyalog = await openExpense();
    const description = diyalog.locator('[name="cari"]');
    await description.fill('Kaydedilmemiş gider');
    await askToClose(() => diyalog.getByRole('button', { name: 'Vazgeç' }).click(), false);
    await expect(description).toHaveValue('Kaydedilmemiş gider');
    await askToClose(() => page.getByRole('button', { name: 'Pencereyi kapat' }).click(), false);
    await expect(description).toHaveValue('Kaydedilmemiş gider');
    await askToClose(() => page.keyboard.press('Escape'), false);
    await expect(description).toHaveValue('Kaydedilmemiş gider');

    // Düzenleme geri alınmışsa uyarı yoktur; kullanıcı değiştirmediği formu doğrudan kapatabilir.
    await description.fill('');
    await page.keyboard.press('Escape');
    await expect(diyalog).toBeHidden();

    for (const close of [
      dialog => dialog.getByRole('button', { name: 'Vazgeç' }).click(),
      () => page.getByRole('button', { name: 'Pencereyi kapat' }).click(),
      () => page.keyboard.press('Escape'),
    ]) {
      diyalog = await openExpense();
      await diyalog.locator('[name="cari"]').fill('Vazgeçilen taslak');
      await askToClose(() => close(diyalog), true);
      await expect(diyalog).toBeHidden();
    }
  });

  test('boş PDF alanı değişiklik sayılmaz, seçilmiş dosya korunur', async ({ page }) => {
    await ac(page, 'Ekstre / Hareket Yükle');
    const openUpload = () => pencere(page, 'Kart ekstresi / hesap hareketi seç', 'Ekstre / hareket PDF’si yükle');
    let diyalog = await openUpload();
    await page.waitForTimeout(50); // Boş File nesnesinin değişken zaman damgasını da sınar.
    await diyalog.getByRole('button', { name: 'Vazgeç' }).click();
    await expect(diyalog).toBeHidden();

    diyalog = await openUpload();
    await diyalog.locator('[name="dosya"]').setInputFiles({
      name: 'ornek.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.4\n%%EOF'),
    });
    const promptEvent = page.waitForEvent('dialog');
    const actionDone = diyalog.getByRole('button', { name: 'Vazgeç' }).click();
    const prompt = await promptEvent;
    expect(prompt.message()).toContain('Kaydedilmemiş değişiklikler');
    await prompt.dismiss();
    await actionDone;
    await expect(diyalog.locator('[name="dosya"]')).toHaveValue(/ornek\.pdf$/);
    await expect(diyalog).toBeVisible();
  });
});
