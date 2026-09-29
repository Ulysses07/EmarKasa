import test from 'node:test';
import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';

// Web arayüzünün (Kasa.Api/wwwroot ve telefon arayüzü m/) kaynak kuralları: DOM yalnız h() ile kurulur (HTML dizesi
// ayrıştırılmaz), kod dizeden üretilip çalıştırılmaz (CSP 'script-src self' zaten engeller; burada yazılırken yakalanır) ve
// para tutarı ikili kayan noktayla ayrıştırılmaz (kuruş ayrıştırıcıları: ui-core cents, mobil-cekirdek tutarCoz).
// İstisna listesi yoktur: dosyanın tamamı (yorumlar dahil) taranır; kural bir yorumda bile adı geçmeden anlatılabilir.
const kok = new URL('../Kasa.Api/wwwroot/', import.meta.url);

const YASAKLAR = [
  { ad: 'innerHTML', desen: /\binnerHTML\b/, neden: 'HTML dizesi DOM\'a yazılmaz; düğümleri h() ile kurun.' },
  { ad: 'outerHTML', desen: /\bouterHTML\b/, neden: 'HTML dizesi DOM\'a yazılmaz; düğümleri h() ile kurun.' },
  { ad: 'insertAdjacentHTML', desen: /\binsertAdjacentHTML\b/, neden: 'HTML dizesi DOM\'a yazılmaz; append/replaceChildren kullanın.' },
  { ad: 'document.write', desen: /\bdocument\s*\.\s*write(?:ln)?\b/, neden: 'document.write belgeyi HTML dizesiyle yazar.' },
  { ad: 'eval', desen: /\beval\b/, neden: 'Dizeden kod çalıştırılmaz.' },
  { ad: 'Function kurucusu', desen: /(?:^|[^\w$.])(?:new\s+)?Function\s*\(/, neden: 'new Function / Function(...) dizeden kod üretir.' },
  { ad: 'dizeyle zamanlayıcı', desen: /\bset(?:Timeout|Interval)\s*\(\s*['"`]/, neden: 'setTimeout/setInterval dize alınca onu kod olarak çalıştırır.' },
  { ad: 'parseFloat', desen: /\bparseFloat\b/, neden: 'Tutar kayan noktayla ayrıştırılmaz; kuruş ayrıştırıcısını (cents / tutarCoz) kullanın.' },
];

async function jsDosyalari(dizin, onek = '') {
  const sonuc = [];
  for (const oge of await readdir(dizin, { withFileTypes: true })) {
    if (oge.isDirectory()) sonuc.push(...await jsDosyalari(new URL(`${oge.name}/`, dizin), `${onek}${oge.name}/`));
    else if (/\.m?js$/.test(oge.name)) sonuc.push(`${onek}${oge.name}`);
  }
  return sonuc.sort();
}

// Dosya satır satır değil bütün olarak taranır: biçimleyici bir çağrıyı ya da üye erişimini satırlara bölse de
// (setTimeout(⏎ 'kod', …), document⏎ .write) desendeki \s satır sonunu da kapsar. ^ satır başıdır (m); her ihlal,
// eşleşmenin ilk boşluk dışı karakterinin satırında bir kez raporlanır.
function ihlaller(metin) {
  const satirlar = metin.split(/\r?\n/);
  const bulunan = new Map();
  YASAKLAR.forEach((yasak, sira) => {
    for (const eslesme of metin.matchAll(new RegExp(yasak.desen.source, 'gm'))) {
      const satir = metin.slice(0, eslesme.index + Math.max(0, eslesme[0].search(/\S/))).split('\n').length;
      bulunan.set(`${satir}:${sira}`, { satir, sira, yasak, metin: satirlar[satir - 1].trim().slice(0, 160) });
    }
  });
  return [...bulunan.values()].sort((a, b) => a.satir - b.satir || a.sira - b.sira);
}

const dosyalar = await jsDosyalari(kok);

test('tarama wwwroot ve m/ altındaki bütün JS dosyalarını görür', () => {
  for (const beklenen of ['app.js', 'ui-core.js', 'service-worker.js', 'telefon-yonlendir.js', 'm/app.js', 'm/mobil-cekirdek.js']) assert.ok(dosyalar.includes(beklenen), `${beklenen} taranmadı`);
});

test('desenler yasak kullanımı yakalar, benzer adlı meşru kodu yakalamaz', () => {
  const yakalanan = kod => ihlaller(kod).map(i => i.yasak.ad);
  assert.deepEqual(yakalanan('el.innerHTML = x;'), ['innerHTML']);
  assert.deepEqual(yakalanan("el['outerHTML'] = x;"), ['outerHTML']);
  assert.deepEqual(yakalanan('el.insertAdjacentHTML("beforeend", x);'), ['insertAdjacentHTML']);
  assert.deepEqual(yakalanan('document.write(x); document . writeln(y);'), ['document.write']);
  assert.deepEqual(yakalanan('const f = window.eval; eval(kod);'), ['eval']);
  assert.deepEqual(yakalanan('const f = new Function("a", kod);'), ['Function kurucusu']);
  assert.deepEqual(yakalanan('Function("return this")();'), ['Function kurucusu']);
  assert.deepEqual(yakalanan("setTimeout('ciz()', 10); setInterval(`x`, 5);"), ['dizeyle zamanlayıcı']);
  assert.deepEqual(yakalanan('const t = Number.parseFloat(girdi);'), ['parseFloat']);
  // Satırlara bölünmüş yazım da yakalanır ve ifadenin başladığı satırda raporlanır.
  assert.deepEqual(ihlaller("x();\nsetTimeout(\n  'ciz()',\n  10\n);").map(i => [i.satir, i.yasak.ad]), [[2, 'dizeyle zamanlayıcı']]);
  assert.deepEqual(ihlaller('const d = document\n  .write(x);').map(i => [i.satir, i.yasak.ad]), [[1, 'document.write']]);
  assert.deepEqual(ihlaller('const f = x;\nnew Function(\n  kod\n);').map(i => [i.satir, i.yasak.ad]), [[2, 'Function kurucusu']]);
  // Meşru benzerleri: işlev bildirimleri, evaluate, textContent, sayı çevirimi, işlevle zamanlayıcı.
  assert.deepEqual(yakalanan('async function ciz() {} const f = function (x) {}; x.evaluate(); el.textContent = s; Number(v); setTimeout(() => ciz(), 10);'), []);
  assert.deepEqual(yakalanan('typeof x === "function"; o.myFunction(1); o.Function(2);'), []);
});

test('wwwroot JS\'inde HTML dizesiyle DOM yazımı, dizeden kod ve parseFloat yok', async () => {
  const bulunan = [];
  for (const dosya of dosyalar) {
    for (const i of ihlaller(await readFile(new URL(dosya, kok), 'utf8'))) bulunan.push(`Kasa.Api/wwwroot/${dosya}:${i.satir} ${i.yasak.ad} — ${i.yasak.neden}\n    ${i.metin}`);
  }
  assert.equal(bulunan.length, 0, 'Yasak kullanım:\n' + bulunan.join('\n'));
});
