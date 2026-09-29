import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { sikistir } from './css-metni.mjs';

// Kaydırılabilir kapsayıcılar (overflow auto/scroll) klavyeyle kaydırılabilmelidir (WCAG 2.1.1; axe
// scrollable-region-focusable, ACT 0ssw9k): ya kapsayıcının kendisi odaklanır (tabindex 0, adlı bölge) ya da içinde
// odaklanan denetim vardır. Düğmenin içinde kaydırma olmaz: düğme içine odaklanan bölge konamaz (iç içe etkileşim).
// Kaynak, biçimden bağımsız okunmak için yorumsuz, sıkışık yazıma indirilir (css-metni.mjs); seçiciler öyle yazılır.
const oku = async dosya => sikistir(await readFile(new URL(`../Kasa.Api/wwwroot/${dosya}`, import.meta.url), 'utf8'));
// Bütün kurallar (medya sorgusu içindekiler dahil): [seçici, { özellik: değer }].
const kurallar = css =>
  [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(([, secici, govde]) => [
    secici.trim(),
    Object.fromEntries(
      govde
        .split(';')
        .map(p => p.trim())
        .filter(Boolean)
        .map(p => [p.slice(0, p.indexOf(':')).trim(), p.slice(p.indexOf(':') + 1).trim()])
    ),
  ]);
const kaydirir = bildirim => ['overflow', 'overflow-x', 'overflow-y'].some(ad => /\b(auto|scroll)\b/.test(bildirim[ad] || ''));
const masaustu = kurallar(await oku('styles.css'));
const telefon = kurallar(await oku('m/app.css'));

test('kaydırılabilir kapsayıcılar bilinen ve klavyeyle erişilen listededir', () => {
  const bilinen = {
    // Masaüstü: tablo ve genel kasa tutarı odaklanan adlı bölgedir (app.js table(), renderHome); menü düğmeleri içerir.
    'styles.css': ['.cash-total', '.sidebar nav', '.table-wrap'],
    // Telefon: süzgeç çipleri düğmedir; alt sayfa (hızlı işlem) form denetimleri içerir.
    'm/app.css': ['.cipler', '.sayfa'],
  };
  for (const [dosya, liste] of [
    ['styles.css', masaustu],
    ['m/app.css', telefon],
  ]) {
    const bulunan = [...new Set(liste.filter(([, b]) => kaydirir(b)).map(([s]) => s))].sort();
    assert.deepEqual(
      bulunan,
      bilinen[dosya],
      `${dosya}: yeni kaydırma kapsayıcısı klavyeyle erişilebilir olmalı (odaklanan bölge ya da içinde denetim), sonra listeye eklenir`
    );
  }
});

test('genel kasa tutarı yalnız yatay kayar; dikey alt-piksel taşma kaydırma yaratmaz', () => {
  const tutar = masaustu.filter(([s]) => s === '.cash-total').reduce((t, [, b]) => ({ ...t, ...b }), {});
  assert.equal(tutar['overflow-x'], 'auto');
  assert.equal(tutar['overflow-y'], 'hidden');
});

test('kart ve kredi kutusu (düğme) içindeki tutar kaymaz; sığmazsa satır kayarak tamamı görünür', () => {
  const finans = masaustu.filter(([s]) => s.includes('.finance-card'));
  assert.ok(finans.length > 0, '.finance-card kuralları okunamadı');
  for (const [secici, bildirim] of finans) assert.ok(!kaydirir(bildirim), `${secici}: düğme içinde kaydırma kapsayıcısı olmaz`);
  const tutar = finans.find(([s]) => s === '.finance-card>.money')[1];
  assert.equal(tutar['white-space'], 'normal', 'genel .money nowrap kuralı kartta kalkar');
  assert.equal(tutar['overflow-wrap'], 'anywhere', 'bölünme yeri olmayan tutar sığmazsa satır kayar (kırpılmaz, kısaltılmaz)');
  assert.ok(
    !finans.some(([, b]) => /ellipsis/.test(b['text-overflow'] || '') || /hidden|clip/.test(b.overflow || '')),
    'tutar kısaltılmaz ya da kırpılmaz'
  );
});
