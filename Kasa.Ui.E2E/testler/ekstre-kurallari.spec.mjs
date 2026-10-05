import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { OTURUM, WCAG, sayfayiHazirla } from './ortak.mjs';

test.use({ storageState: OTURUM });
test.beforeEach(async ({ page }) => sayfayiHazirla(page));

test('kişisel kural formu ve öneri masaüstü ve telefonda seçimsiz çalışır', async ({ page }, testInfo) => {
  const name = `Kargo ${testInfo.project.name}`;
  const channels = await (await page.request.get('/api/kanallar')).json();
  const channel = channels.find(c => c.aktif);
  const document = {
    id: 777,
    surum: 1,
    kaynak: 'Banka',
    banka: 'Akbank',
    hesapAdi: 'Test hesabı',
    kartId: null,
    dosyaAdi: 'test.pdf',
    yuklendi: '2026-09-25T09:00:00Z',
    uyarilar: [],
    kayitlar: [],
    satirSayisi: 1,
    kayitSayisi: 0,
    satirlar: [
      {
        no: 1,
        sayfa: 1,
        tarih: '2026-09-25',
        aciklama: 'YURTİÇİ KARGO',
        kaynakSatir: '25.09.2026 YURTİÇİ KARGO 100,00 TL',
        tutar: 100,
        yon: 'Cikis',
        onerilenIslem: 'Gider',
        sinif: 'Hareket',
        paraBirimi: 'TRY',
        uyarilar: [],
      },
    ],
  };
  await page.route(/\/api\/ekstre-aktar$/, route => route.fulfill({ json: [document] }));
  await page.route('**/api/ekstre-aktar/777', route => route.fulfill({ json: document }));
  await page.route('**/api/ekstre-aktar/777/oneriler', route =>
    route.fulfill({
      json: [
        {
          satirNo: 1,
          durum: 'Oneri',
          kuralAdlari: [name],
          islemTuru: 'Gider',
          dagilimTuru: 'Esit',
          kanalIds: [channel.id],
          aciklama: 'Kişisel kural önerisi',
        },
      ],
    })
  );
  const financeWrites = [];
  page.on('request', request => {
    if (request.method() === 'POST' && /\/ekstre-aktar\/\d+\/(kaydet|onizleme)$/.test(request.url())) financeWrites.push(request.url());
  });
  await page.goto('/?gorunum=masaustu');
  await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('button', { name: 'Ekstre / Hareket Yükle', exact: true }).click();
  await page.getByRole('button', { name: '+ Kural ekle', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Kişisel kural ekle' });
  await dialog.getByRole('textbox', { name: 'Kural adı', exact: true }).fill(name);
  await dialog.getByRole('textbox', { name: 'Açıklamada geçen sözcük veya ifade' }).fill('YURTİÇİ KARGO');
  await dialog.locator('[name="banka"]').selectOption('Akbank');
  await dialog.locator('[name="yon"]').selectOption('Cikis');
  await dialog.locator('[name="islemTuru"]').selectOption('Gider');
  await dialog.locator('[name="dagilimTuru"]').selectOption('Esit');
  await dialog.locator(`[name="kural-kanal-${channel.id}"]`).check();
  const violations = (await new AxeBuilder({ page }).include('#modal').withTags(WCAG).analyze()).violations;
  expect(violations).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await dialog.getByRole('button', { name: 'Kuralı kaydet', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: name })).toHaveCount(1);
  await page.getByRole('button', { name: 'Aç ve incele', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Öneriyi uygula', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Uygun önerileri uygula', exact: true }).click();
  await expect(page.locator('[name="sec-1"]')).not.toBeChecked();
  await page.locator('[name="sec-1"]').check();
  await expect(page.locator('.import-row [name="dagilimTuru"]')).toHaveValue('Esit');
  await expect(page.locator(`[name="pay-kanal-${channel.id}"]`)).toBeChecked();
  await page.getByRole('button', { name: 'Bu seçimi hatırla', exact: true }).click();
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('[name="kaynak"]')).toHaveValue('Banka');
  await expect(dialog.locator('[name="banka"]')).toHaveValue('Akbank');
  await expect(dialog.locator('[name="dagilimTuru"]')).toHaveValue('Esit');
  await dialog.getByRole('button', { name: 'Vazgeç', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  const row = page.getByRole('row').filter({ hasText: name });
  await row.getByRole('button', { name: 'Sil', exact: true }).click();
  const remove = page.getByRole('dialog', { name: 'Kuralı sil' });
  await remove.getByRole('button', { name: 'Kuralı sil', exact: true }).click();
  await expect(remove).not.toBeVisible();
  await expect(row).toHaveCount(0);
  expect(financeWrites).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
