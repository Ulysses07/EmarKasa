import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

// Masaüstü web arayüzünün renk çiftleri WCAG 2.2 eşiklerini geçer: metin 4,5:1 (1.4.3), kontrol sınırı ve odak göstergesi
// 3:1 (1.4.11). Değerler styles.css'ten okunur; var(--x) :root belirteçlerinden çözülür.
const css = await readFile(new URL('../Kasa.Api/wwwroot/styles.css', import.meta.url), 'utf8');
const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
// Medya sorgusu dışındaki (ilk) kuralın bildirimleri; seçici dosyada yazıldığı gibi verilir.
function rule(selector) {
  const match = new RegExp(`(?:^|[}\\n])${escape(selector)}\\{([^}]*)\\}`).exec(css);
  assert.ok(match, `${selector} kuralı yok`);
  return Object.fromEntries(match[1].split(';').filter(Boolean).map(part => { const index = part.indexOf(':'); return [part.slice(0, index).trim(), part.slice(index + 1).trim()]; }));
}
const root = rule(':root');
const color = value => {
  const token = /^var\((--[\w-]+)\)$/.exec(value);
  const resolved = token ? root[token[1]] : value;
  assert.match(resolved ?? '', /^#[0-9a-f]{6}$/i, `çözülemeyen renk: ${value}`);
  return resolved;
};
const channel = value => { const c = value / 255; return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
const luminance = hex => { const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16)); return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b); };
const ratio = (a, b) => { const [x, y] = [luminance(color(a)), luminance(color(b))].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
const atLeast = (foreground, background, minimum, what) => {
  const value = ratio(foreground, background);
  assert.ok(value >= minimum, `${what}: ${foreground} / ${background} = ${value.toFixed(2)}:1, en az ${minimum}:1 olmalı`);
};
const outlineColor = declaration => declaration.split(' ').at(-1);
const borderColor = declaration => declaration.split(' ').at(-1);

test('WCAG oran hesabı bilinen değerleri verir', () => {
  assert.equal(ratio('#000000', '#ffffff').toFixed(2), '21.00');
  assert.equal(ratio('#65716a', '#eeefe4').toFixed(2), '4.39');
  assert.equal(ratio('#c9cec3', '#fffefa').toFixed(2), '1.59');
});

test('ikincil metin (--muted) krem, kâğıt, kenar çubuğu ve tablo başlığı zemininde en az 4,5:1', () => {
  for (const [background, what] of [[root['--cream'], 'sayfa'], [root['--paper'], 'bölüm'], [rule('.sidebar').background, 'rol etiketi'], [rule('th').background, 'tablo başlığı'], [root['--green-light'], 'açık yeşil zemin']]) {
    atLeast('var(--muted)', background, 4.5, what);
  }
  assert.equal(rule('th').color, 'var(--muted)'); assert.equal(rule('.role-label').color, 'var(--muted)');
});

test('sayfa altbilgisi ikincil metin rengini kullanır', () => {
  assert.equal(rule('.page-footer').color, 'var(--muted)');
  atLeast(rule('.page-footer').color, root['--cream'], 4.5, 'altbilgi');
});

test('form alanı ve ikincil düğme kenarlığı zeminine karşı en az 3:1', () => {
  const field = rule('input,select,textarea');
  assert.equal(borderColor(field.border), 'var(--control-line)');
  for (const background of [field.background, root['--cream'], root['--paper'], '#f8f7ee']) atLeast(borderColor(field.border), background, 3, 'form alanı kenarlığı');
  const secondary = rule('.button');
  assert.equal(borderColor(secondary.border), 'var(--control-line)');
  for (const background of [secondary.background, root['--cream']]) atLeast(borderColor(secondary.border), background, 3, 'ikincil düğme kenarlığı');
  const hover = rule('.button:hover');
  for (const background of [hover.background, root['--cream'], root['--paper']]) atLeast(hover['border-color'], background, 3, 'üzerine gelinen ikincil düğme kenarlığı');
});

test('odak halkası sayfa, kâğıt, kenar çubuğu ve açık yeşil zemine karşı en az 3:1', () => {
  const focus = rule('button:focus-visible,a:focus-visible,input:focus-visible,textarea:focus-visible,select:focus-visible');
  assert.equal(outlineColor(focus.outline), 'var(--focus)');
  for (const background of [root['--cream'], root['--paper'], rule('.sidebar').background, root['--green-light'], rule('input,select,textarea').background]) atLeast(outlineColor(focus.outline), background, 3, 'odak halkası');
  // Koyu bildirim üzerindeki kapatma düğmesinin halkası açık renktir.
  const toastFocus = rule('.toast-close:focus-visible');
  atLeast(toastFocus['outline-color'], rule('.toast.error').background, 3, 'hata bildirimi kapatma düğmesi odak halkası');
  atLeast('#ffffff', rule('.toast.error').background, 4.5, 'hata bildirimi metni');
});
