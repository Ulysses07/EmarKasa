import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import AxeBuilder from '@axe-core/playwright';
import { expect } from '@playwright/test';

export const OTURUM = fileURLToPath(new URL('../.oturum/editor.json', import.meta.url));
const TABAN = fileURLToPath(new URL('./axe-tabani.json', import.meta.url));
// WCAG 2.0 A/AA, 2.1 AA ve 2.2 AA kuralları (wcag21a etiketinde yalnız axe'nin varsayılan kapalı deneysel kuralı vardır).
export const WCAG = ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'];

const erisim = process.env.KASA_E2E_ERISIM !== '0';
const ekran = process.env.KASA_E2E_EKRAN === '1';
const tabanKipi = process.env.KASA_E2E_AXE_TABANI || '';

/** Sayfanın saati sunucunun durmuş saatiyle aynı ana (İstanbul 12:00) sabitlenir; zamanlayıcılar işlemeye devam eder.
 * Sürüm metni (/api/surum) koşudan bağımsız sabit yazılır: sürüm yükseltmesi anlık görüntüleri bozmaz. */
export async function sayfayiHazirla(page) {
  await page.clock.setFixedTime(new Date(`${process.env.KASA_E2E_BUGUN}T12:00:00+03:00`));
  await page.route('**/api/surum', async route => {
    const yanit = await route.fetch();
    await route.fulfill({ response: yanit, json: { ...(await yanit.json()), surum: 'e2e', minimumIstemci: 'e2e' } });
  });
}

/** Ekran kararlı: istekler bitti, yazı tipleri yüklendi, yükleniyor göstergesi yok. */
export async function yerlesmesiniBekle(page) {
  await page.waitForLoadState('networkidle');
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator('.loader, .yukleniyor').filter({ visible: true })).toHaveCount(0);
}

/** Ekranın üç denetimi: axe (tabana göre), ARIA ağacı anlık görüntüsü, KASA_E2E_EKRAN=1 ise ekran görüntüsü. */
export async function denetle(page, testInfo, ad, { kok = page.locator('body') } = {}) {
  await yerlesmesiniBekle(page);
  if (erisim) {
    await axeDenetle(page, testInfo, ad);
    await expect(kok, `${ad}: erişilebilirlik ağacı`).toMatchAriaSnapshot({ name: `${ad}.aria.yml` });
  }
  if (ekran) await expect(page, `${ad}: ekran görüntüsü`).toHaveScreenshot(`${ad}.png`, { fullPage: true });
}

const oku = () => JSON.parse(readFileSync(TABAN, 'utf8'));
const sirali = nesne => Object.fromEntries(Object.entries(nesne).sort(([a], [b]) => a.localeCompare(b)));

/** axe ihlalleri "kural | hedef seçici" dizeleri olarak taban dosyasıyla (axe-tabani.json) karşılaştırılır. Test yalnız
 * tabanda olmayan (YENİ) ihlalde kırılır. Düzelen ihlal uyarı olarak bildirilir; KASA_E2E_AXE_TABANI=kucult tabandan
 * çıkarır (taban yalnız küçülür). KASA_E2E_AXE_TABANI=olustur yalnız tabanda kaydı OLMAYAN (yeni eklenen) ekranın tabanını
 * yazar; kayıtlı ekranda reddedilir, yoksa bakım koşusu yeni ihlalleri sessizce tabana alabilirdi. */
async function axeDenetle(page, testInfo, ad) {
  const sonuc = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  const simdiki = [
    ...new Set(sonuc.violations.flatMap(v => v.nodes.map(n => `${v.id} | ${n.target.map(t => [t].flat().join(' >>> ')).join(' >>> ')}`))),
  ].sort();
  const proje = testInfo.project.name;
  const taban = oku();
  const kayitli = taban[proje]?.[ad] ?? [];
  const yeni = simdiki.filter(s => !kayitli.includes(s));
  const duzelen = kayitli.filter(s => !simdiki.includes(s));
  await testInfo.attach(`${ad}-axe.json`, { body: JSON.stringify(sonuc.violations, null, 2), contentType: 'application/json' });
  if (tabanKipi === 'olustur' && taban[proje]?.[ad] !== undefined)
    throw new Error(
      `${proje}/${ad}: axe tabanında kayıt var; KASA_E2E_AXE_TABANI=olustur yalnız yeni ekran içindir. Düzelen ihlaller için kucult kullanın.`
    );
  if (tabanKipi === 'olustur' || (tabanKipi === 'kucult' && duzelen.length)) {
    const yazilacak = tabanKipi === 'olustur' ? simdiki : kayitli.filter(s => simdiki.includes(s));
    taban[proje] = sirali({ ...(taban[proje] ?? {}), [ad]: yazilacak });
    writeFileSync(TABAN, JSON.stringify(sirali(taban), null, 2) + '\n');
    if (tabanKipi === 'olustur') return;
  } else if (duzelen.length) {
    const uyari = `${proje}/${ad}: düzelen axe ihlalleri tabandan çıkarılmalı (KASA_E2E_AXE_TABANI=kucult):\n  ${duzelen.join('\n  ')}`;
    testInfo.annotations.push({ type: 'axe-tabani', description: uyari });
    console.warn(uyari);
  }
  const ayrinti = sonuc.violations
    .filter(v => yeni.some(y => y.startsWith(`${v.id} | `)))
    .map(v => `${v.id} (${v.impact}): ${v.help}\n  ${v.helpUrl}`)
    .join('\n');
  expect(yeni, `${ad}: tabanda olmayan yeni axe ihlalleri (WCAG 2.2 AA). Düzeltin; tabana eklemeyin.\n${ayrinti}`).toEqual([]);
}
