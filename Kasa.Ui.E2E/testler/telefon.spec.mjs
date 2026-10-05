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

  test('hızlı işlem taslağı perde, Escape ve geri hareketinde korunur; açık iptalde onayla silinir', async ({ page }) => {
    await ac(page, 'panel', 'Panel');
    await page.getByRole('button', { name: /^(İşlem|Hızlı işlem)$/ }).click();
    const sayfa = page.getByRole('dialog', { name: 'Hızlı işlem' });
    await sayfa.getByRole('textbox', { name: 'Tutar' }).fill('75');
    await sayfa.getByPlaceholder('Firma, kişi ya da ödeme yeri').fill('Kargo');

    const mesajlar = [];
    page.on('dialog', async dialog => {
      mesajlar.push(dialog.message());
      if (mesajlar.length === 4) await dialog.accept();
      else await dialog.dismiss();
    });
    await page.locator('#sayfa .perde').click({ position: { x: 4, y: 4 } });
    await expect(sayfa).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(sayfa).toBeVisible();
    await page.evaluate(() => history.back());
    await expect.poll(() => mesajlar.length).toBe(3);
    await expect(sayfa).toBeVisible();
    await expect(sayfa.getByRole('textbox', { name: 'Tutar' })).toHaveValue('75');
    await expect(sayfa.getByPlaceholder('Firma, kişi ya da ödeme yeri')).toHaveValue('Kargo');

    await sayfa.getByRole('button', { name: 'Vazgeç' }).click();
    await expect(sayfa).toHaveCount(0);
    expect(mesajlar).toHaveLength(4);
    expect(mesajlar.every(mesaj => mesaj.includes('kaydedilmemiş bilgiler'))).toBe(true);
  });

  test('kayıt kontrolü sürerken hızlı işlem kapatılamaz', async ({ page }) => {
    let basladi;
    const istekBasladi = new Promise(resolve => {
      basladi = resolve;
    });
    let bitir;
    await page.route('**/api/islemler/benzerlik', async route => {
      basladi();
      await new Promise(resolve => {
        bitir = resolve;
      });
      await route.fulfill({ status: 503, json: { mesaj: 'Geçici hata' } });
    });
    await ac(page, 'panel', 'Panel');
    await page.getByRole('button', { name: /^(İşlem|Hızlı işlem)$/ }).click();
    const sayfa = page.getByRole('dialog', { name: 'Hızlı işlem' });
    await sayfa.getByRole('textbox', { name: 'Tutar' }).fill('75');
    await sayfa.getByPlaceholder('Firma, kişi ya da ödeme yeri').fill('Kargo');
    await sayfa.getByRole('button', { name: 'MEZAT', exact: true }).click();
    await sayfa.getByRole('button', { name: 'Kaydet' }).last().click();
    await istekBasladi;
    let onaySayisi = 0;
    page.on('dialog', async dialog => {
      onaySayisi++;
      await dialog.dismiss();
    });
    await page.keyboard.press('Escape');
    await expect(sayfa).toBeVisible();
    await expect(page.getByRole('status')).toContainText('Gider kaydı sürüyor.');
    expect(onaySayisi).toBe(0);
    bitir();
    await expect(sayfa.getByRole('alert')).toContainText('Benzer kayıt kontrolü tamamlanamadı.');
  });

  test('kart ve kredi özeti hatası panelde görünür; yeniden denemede düzelir', async ({ page }) => {
    let hatali = true;
    await page.route(/\/api\/takip\/ozet\?gun=30$/, async route => {
      if (hatali) await route.fulfill({ status: 503, json: { mesaj: 'Geçici hata' } });
      else await route.continue();
    });
    await ac(page, 'panel', 'Panel');
    const uyari = page.getByRole('alert').filter({ hasText: 'Kart ve kredi özeti yüklenemedi.' });
    await expect(uyari).toBeVisible();
    hatali = false;
    await uyari.getByRole('button', { name: 'Yeniden dene' }).click();
    await expect(uyari).toHaveCount(0);
    await expect(page.getByRole('button', { name: /Kartlar.*Toplam güncel borç/ })).toBeVisible();
  });

  test('kredi ayrıntısı hatası kart ve kredi özetini gizlemez', async ({ page }) => {
    await page.route('**/api/takip/krediler', route => route.fulfill({ status: 503, json: { mesaj: 'Geçici hata' } }));
    await ac(page, 'panel', 'Panel');
    await expect(page.getByRole('alert').filter({ hasText: 'Kredi ayrıntıları yüklenemedi.' })).toBeVisible();
    await expect(page.getByRole('button', { name: /Kartlar.*Toplam güncel borç/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /Krediler.*Kalan planlı ödeme/ })).toBeVisible();
  });
});
