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
  await page.getByRole('button', { name: '1. hareketi incele / düzenle', exact: true }).click();
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

test('satır düzenleyici seçimi değiştirmeden kendi altında kompakt açılır', async ({ page }, testInfo) => {
  const row = no => ({
    no,
    sayfa: 1,
    tarih: '2026-09-25',
    aciklama: no === 1 ? 'KİRA ÖDEMESİ' : 'FATURA ÖDEMESİ',
    kaynakSatir: '25.09.2026 HESAP HAREKETİ 100,00 TL',
    tutar: 100,
    yon: 'Cikis',
    onerilenIslem: 'Gider',
    sinif: 'Hareket',
    paraBirimi: 'TRY',
    uyarilar: [],
  });
  const document = {
    id: 778,
    surum: 1,
    kaynak: 'Banka',
    banka: 'Akbank',
    hesapAdi: 'Test hesabı',
    kartId: null,
    dosyaAdi: 'satir-duzenleme.pdf',
    yuklendi: '2026-09-25T09:00:00Z',
    uyarilar: [],
    kayitlar: [],
    satirSayisi: 2,
    kayitSayisi: 0,
    satirlar: [row(1), row(2)],
  };
  await page.route(/\/api\/ekstre-aktar$/, route => route.fulfill({ json: [document] }));
  await page.route('**/api/ekstre-aktar/778', route => route.fulfill({ json: document }));
  await page.route('**/api/ekstre-aktar/778/oneriler', route => route.fulfill({ json: [] }));
  const financeWrites = [];
  page.on('request', request => {
    if (request.method() === 'POST' && /\/ekstre-aktar\/\d+\/(kaydet|onizleme)$/.test(request.url())) financeWrites.push(request.url());
  });
  await page.goto('/?gorunum=masaustu');
  await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('button', { name: 'Ekstre / Hareket Yükle', exact: true }).click();
  await page.getByRole('button', { name: 'Aç ve incele', exact: true }).click();
  const first = page.locator('.import-row').filter({ has: page.locator('[name="sec-1"]') });
  const second = page.locator('.import-row').filter({ has: page.locator('[name="sec-2"]') });
  const editor = first.getByRole('group', { name: '1. hareket düzenleme', includeHidden: true });
  const edit = first.getByRole('button', { name: '1. hareketi incele / düzenle', exact: true });
  await page.locator('[name="sec-1"]').check();
  await expect(editor).not.toBeVisible();
  await edit.focus();
  await edit.press('Enter');
  await expect(editor).toBeVisible();
  await expect(edit).toHaveAttribute('aria-expanded', 'true');
  await expect(second.locator('.import-row-editor')).not.toBeVisible();
  const date = await editor.getByLabel('İşlem tarihi', { exact: true }).boundingBox();
  const total = await editor.getByLabel('Tutar (₺)', { exact: true }).boundingBox();
  if (testInfo.project.name.startsWith('masaustu')) {
    expect(Math.abs(date.y - total.y)).toBeLessThan(2);
    expect(total.x).toBeGreaterThan(date.x + date.width);
  } else {
    expect(total.y).toBeGreaterThan(date.y + date.height);
    expect(Math.abs(date.x - total.x)).toBeLessThan(2);
  }
  const description = editor.getByLabel('Açıklama', { exact: true });
  await description.fill('Elle düzeltilmiş kira');
  await editor.locator('[name="dagilimTuru"]').selectOption('Genel');
  await edit.click();
  await expect(editor).not.toBeVisible();
  await edit.click();
  await expect(description).toHaveValue('Elle düzeltilmiş kira');
  await expect(editor.locator('[name="dagilimTuru"]')).toHaveValue('Genel');
  await expect(page.locator('[name="sec-1"]')).toBeChecked();
  await expect(page.locator('[name="sec-2"]')).not.toBeChecked();
  const violations = (await new AxeBuilder({ page }).include('.import-rows').withTags(WCAG).analyze()).violations;
  expect(violations).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(financeWrites).toEqual([]);
});

test('ilk önerisi iade olan satır seçilmeden kaynak harcama düzenlenebilir', async ({ page }) => {
  const document = {
    id: 779,
    surum: 1,
    kaynak: 'Kart',
    banka: 'Akbank',
    hesapAdi: 'Test kartı',
    kartId: 9004,
    dosyaAdi: 'iade.pdf',
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
        aciklama: 'ÜRÜN İADESİ',
        kaynakSatir: '25.09.2026 ÜRÜN İADESİ 100,00 TL',
        tutar: 100,
        yon: 'Giris',
        onerilenIslem: 'KartIade',
        sinif: 'Hareket',
        paraBirimi: 'TRY',
        uyarilar: [],
      },
    ],
  };
  await page.route(/\/api\/ekstre-aktar$/, route => route.fulfill({ json: [document] }));
  await page.route('**/api/ekstre-aktar/779', route => route.fulfill({ json: document }));
  await page.route('**/api/ekstre-aktar/779/oneriler', route => route.fulfill({ json: [] }));
  await page.route('**/api/takip/kartlar/9004', route =>
    route.fulfill({ json: { harcamalar: [{ id: 18, tutar: 100, tarih: '2026-09-23', aciklama: 'Ürün', iptal: false }] } })
  );
  const financeWrites = [];
  page.on('request', request => {
    if (request.method() === 'POST' && /\/ekstre-aktar\/\d+\/(kaydet|onizleme)$/.test(request.url())) financeWrites.push(request.url());
  });
  await page.goto('/?gorunum=masaustu');
  await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('button', { name: 'Ekstre / Hareket Yükle', exact: true }).click();
  await page.getByRole('button', { name: 'Aç ve incele', exact: true }).click();
  const edit = page.getByRole('button', { name: '1. hareketi incele / düzenle', exact: true });
  await edit.click();
  const refund = page.getByRole('combobox', { name: 'İade edilen kart harcaması', exact: true });
  await expect(refund.getByRole('option', { name: /^#18 ·/ })).toHaveCount(1);
  await refund.selectOption('18');
  await expect(page.locator('[name="sec-1"]')).not.toBeChecked();
  await edit.click();
  await edit.click();
  await expect(refund).toHaveValue('18');
  await page.locator('[name="sec-1"]').check();
  await expect(refund).toHaveValue('18');
  expect(financeWrites).toEqual([]);
});
