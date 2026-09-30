import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { sikistir } from './css-metni.mjs';

// Web arayüzünün renk çiftleri WCAG 2.2 eşiklerini geçer: metin 4,5:1 (1.4.3), kontrol sınırı ve odak göstergesi 3:1
// (1.4.11). Değerler masaüstünde styles.css'ten, telefonda m/app.css'ten okunur; var(--x) o dosyanın :root belirteçlerinden
// çözülür. Kaynak, biçimden bağımsız okunmak için sıkışık yazıma indirilir (css-metni.mjs).
const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
function stylesheet(css) {
  // Medya sorgusu dışındaki (ilk) kuralın bildirimleri; seçici sıkışık yazımla (boşluksuz virgül ve birleştirici) verilir.
  const rule = selector => {
    const match = new RegExp(`(?:^|[}\\n])${escape(selector)}\\{([^}]*)\\}`).exec(css);
    assert.ok(match, `${selector} kuralı yok`);
    return Object.fromEntries(
      match[1]
        .split(';')
        .map(part => part.trim())
        .filter(Boolean)
        .map(part => {
          const index = part.indexOf(':');
          return [part.slice(0, index).trim(), part.slice(index + 1).trim()];
        })
    );
  };
  const root = rule(':root');
  const color = value => {
    const token = /^var\((--[\w-]+)\)$/.exec(value);
    const resolved = token ? root[token[1]] : value;
    assert.match(resolved ?? '', /^#[0-9a-f]{6}$/i, `çözülemeyen renk: ${value}`);
    return resolved;
  };
  return { rule, root, color };
}
const { rule, root, color } = stylesheet(sikistir(await readFile(new URL('../Kasa.Api/wwwroot/styles.css', import.meta.url), 'utf8')));
const mobile = stylesheet(sikistir(await readFile(new URL('../Kasa.Api/wwwroot/m/app.css', import.meta.url), 'utf8')));
const channel = value => {
  const c = value / 255;
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
};
const luminance = hex => {
  const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16));
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
};
const ratio = (a, b, resolve = color) => {
  const [x, y] = [luminance(resolve(a)), luminance(resolve(b))].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
};
const atLeast = (foreground, background, minimum, what, resolve = color) => {
  const value = ratio(foreground, background, resolve);
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
  for (const [background, what] of [
    [root['--cream'], 'sayfa'],
    [root['--paper'], 'bölüm'],
    [rule('.sidebar').background, 'rol etiketi'],
    [rule('th').background, 'tablo başlığı'],
    [root['--green-light'], 'açık yeşil zemin'],
  ]) {
    atLeast('var(--muted)', background, 4.5, what);
  }
  assert.equal(rule('th').color, 'var(--muted)');
  assert.equal(rule('.role-label').color, 'var(--muted)');
});

test('sayfa altbilgisi ikincil metin rengini kullanır', () => {
  assert.equal(rule('.page-footer').color, 'var(--muted)');
  atLeast(rule('.page-footer').color, root['--cream'], 4.5, 'altbilgi');
});

test('form alanı ve ikincil düğme kenarlığı zeminine karşı en az 3:1', () => {
  const field = rule('input,select,textarea');
  assert.equal(borderColor(field.border), 'var(--control-line)');
  for (const background of [field.background, root['--cream'], root['--paper'], '#f8f7ee'])
    atLeast(borderColor(field.border), background, 3, 'form alanı kenarlığı');
  const secondary = rule('.button');
  assert.equal(borderColor(secondary.border), 'var(--control-line)');
  for (const background of [secondary.background, root['--cream']])
    atLeast(borderColor(secondary.border), background, 3, 'ikincil düğme kenarlığı');
  const hover = rule('.button:hover');
  for (const background of [hover.background, root['--cream'], root['--paper']])
    atLeast(hover['border-color'], background, 3, 'üzerine gelinen ikincil düğme kenarlığı');
});

test('kırmızı ikincil (tehlike) düğme kenarlığı kendi zemini, sayfa ve kâğıt zemine karşı en az 3:1', () => {
  // Düğmenin sınırı ikincil düğmelerdeki gibi kenarlıkla belli olur (1.4.11); metin ayrıca --danger ile 4,5:1 üstündedir.
  // Kural .button:hover'dan sonra gelir: üzerine gelince de aynı kenarlık ve zemin kalır.
  const danger = rule('.button.danger');
  assert.equal(danger['border-color'], 'var(--danger-line)');
  for (const background of [danger.background, root['--cream'], root['--paper'], rule('dialog').background])
    atLeast(danger['border-color'], background, 3, 'tehlike düğmesi kenarlığı');
  atLeast(danger.color, danger.background, 4.5, 'tehlike düğmesi metni');
});

test('odak halkası sayfa, kâğıt, kenar çubuğu ve açık yeşil zemine karşı en az 3:1', () => {
  const focus = rule('button:focus-visible,a:focus-visible,input:focus-visible,textarea:focus-visible,select:focus-visible');
  assert.equal(outlineColor(focus.outline), 'var(--focus)');
  for (const background of [
    root['--cream'],
    root['--paper'],
    rule('.sidebar').background,
    root['--green-light'],
    rule('input,select,textarea').background,
  ])
    atLeast(outlineColor(focus.outline), background, 3, 'odak halkası');
  // Koyu bildirim üzerindeki kapatma düğmesinin halkası açık renktir.
  const toastFocus = rule('.toast-close:focus-visible');
  atLeast(toastFocus['outline-color'], rule('.toast.error').background, 3, 'hata bildirimi kapatma düğmesi odak halkası');
  atLeast('#ffffff', rule('.toast.error').background, 4.5, 'hata bildirimi metni');
});

test('kaydırılabilir bölgelerin (tablo, genel kasa tutarı) odak halkası zeminine karşı en az 3:1', () => {
  // Tablo kapsayıcısı kâğıt bölümde, pencerede ya da doğrudan sayfa zemininde durur; halka dışa (3px) çizilir.
  const table = rule('.table-wrap:focus-visible');
  assert.equal(table.outline, '3px solid var(--focus)');
  for (const background of [root['--cream'], root['--paper'], rule('dialog').background])
    atLeast(outlineColor(table.outline), background, 3, 'tablo bölgesi odak halkası');
  // Genel kasa tutarı koyu yeşil kahraman kutusunda: halka açık renktir (--focus bu zeminde 1,87:1 kalırdı).
  const total = rule('.cash-total:focus-visible');
  assert.equal(total['outline-offset'], '3px');
  atLeast(outlineColor(total.outline), rule('.cash-hero').background, 3, 'genel kasa bölgesi odak halkası');
  assert.ok(ratio('var(--focus)', rule('.cash-hero').background) < 3, '--focus koyu yeşil zeminde yetersiz; ayrı renk gerekçesi');
});

test('telefon: ikincil metin (--soluk) sayfa, kart, liste arası ve arama zemininde en az 4,5:1', () => {
  // 11,5–15 px ikincil yazılar (bölüm etiketi, alt yazı, gün başlığı, açıklama, alt sayfa başlıkları) --soluk kullanır.
  for (const selector of [
    '.etiket',
    '.alt-yazi',
    '.gun-bas',
    '.aciklama',
    '.giris .dipnot',
    '.hz-not',
    '.hz-bas',
    '.sayfa-bas button',
    '.alan-etiket',
    '.mini .ust-etiket',
  ]) {
    assert.equal(mobile.rule(selector).color, 'var(--soluk)', `${selector} rengi`);
  }
  assert.equal(mobile.rule('body').background, 'var(--zemin)');
  assert.equal(mobile.rule('.kutu').background, 'var(--kart)');
  assert.equal(mobile.rule('.arama').background, 'var(--arama)');
  assert.equal(mobile.rule('.arama').color, 'var(--soluk)');
  for (const background of ['--zemin', '--kart', '--ara', '--arama'])
    atLeast('var(--soluk)', `var(${background})`, 4.5, `--soluk / ${background}`, mobile.color);
  for (const background of ['--kart', '--ara'])
    atLeast('var(--soluk2)', `var(${background})`, 4.5, `--soluk2 / ${background}`, mobile.color);
  // Ton korunur: --soluk2 (daha koyu ikincil yazı) --soluk'tan koyu kalır.
  assert.ok(ratio('var(--soluk2)', 'var(--zemin)', mobile.color) > ratio('var(--soluk)', 'var(--zemin)', mobile.color));
});

test('telefon: koyu yeşil kutudaki etiket ve alt yazı en az 4,5:1', () => {
  assert.equal(mobile.rule('.kahraman').background, 'var(--yesil)');
  assert.equal(mobile.rule('.mini.koyu').background, 'var(--yesil)');
  assert.equal(mobile.rule('.kahraman .ust-etiket').color, 'var(--yesil-acik)');
  assert.equal(mobile.rule('.mini.koyu .ust-etiket').color, 'var(--yesil-acik)');
  atLeast('var(--yesil-acik)', 'var(--yesil)', 4.5, 'koyu kutu etiketi', mobile.color);
  assert.equal(mobile.rule('.kahraman .alt').color, 'var(--yesil-soluk)');
  atLeast('var(--yesil-soluk)', 'var(--yesil)', 4.5, 'koyu kutu alt yazısı', mobile.color);
});

// Yer tutucu metin de metindir (Understanding 1.4.3: "including placeholder text"). Tarayıcı varsayılanı tutarsızdır
// (MDN: Firefox giriş rengini %54 saydamlıkla, Chrome gri kullanır); renk ve tam opaklık açıkça verilir.
test('yer tutucu metin masaüstünde ve telefonda alan zemininde en az 4,5:1', () => {
  const desktop = rule('input::placeholder,textarea::placeholder');
  assert.equal(desktop.color, 'var(--muted)');
  assert.equal(desktop.opacity, '1');
  atLeast(desktop.color, rule('input,select,textarea').background, 4.5, 'masaüstü yer tutucu');
  const phone = mobile.rule('input::placeholder,textarea::placeholder');
  assert.equal(phone.color, 'var(--soluk)');
  assert.equal(phone.opacity, '1');
  // .girdi beyaz; tutar alanı alt sayfa zemininde (şeffaf); arama alanı kendi zemininde.
  assert.equal(mobile.rule('.girdi').background, '#fff');
  assert.equal(mobile.rule('.hz-tutar input').background, 'transparent');
  assert.equal(mobile.rule('.sayfa').background, 'var(--zemin)');
  for (const background of ['#ffffff', 'var(--zemin)', 'var(--arama)'])
    atLeast(phone.color, background, 4.5, `telefon yer tutucu / ${background}`, mobile.color);
});

test('telefon: form alanı kenarlığı (--alan-cizgi) alan içi, kart ve sayfa zeminine karşı en az 3:1; süs çizgileri ayrı', () => {
  const field = mobile.rule('.girdi');
  assert.equal(borderColor(field.border), 'var(--alan-cizgi)');
  for (const background of ['#ffffff', 'var(--kart)', 'var(--zemin)'])
    atLeast('var(--alan-cizgi)', background, 3, `telefon form alanı kenarlığı / ${background}`, mobile.color);
  // Kart ve liste kenarlıkları süs olarak açık kalır (alanın sınırı değildir).
  assert.equal(borderColor(mobile.rule('.kutu').border), 'var(--cizgi)');
  assert.equal(borderColor(mobile.rule('.liste').border), 'var(--cizgi)');
});

test('telefon form alanı kenarlığı MAUI FieldStroke ile aynı tondur', async () => {
  const colors = await readFile(new URL('../Kasa.App/Resources/Styles/Colors.xaml', import.meta.url), 'utf8');
  const maui = /<Color x:Key="FieldStroke">(#[0-9A-F]{6})<\/Color>/i.exec(colors)?.[1];
  assert.ok(maui, 'Colors.xaml FieldStroke okunamadı');
  assert.equal(mobile.root['--alan-cizgi'].toUpperCase(), maui.toUpperCase());
});

// Renk kodları yalnız :root belirteçlerinde tanımlanır; kurallar renge var(--…) ile başvurur. Bir tonu değiştirmek tek
// belirteci değiştirir ve yukarıdaki oran sınamaları belirteçten çözülür. transparent ve currentColor renk kodu sayılmaz.
const RENK_KODU = /#[0-9a-f]{3,8}\b|\b(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\(|(?<![\w-])(?:white|black)(?![\w-])/gi;
const kokDisiRenkler = css =>
  [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
    .filter(([, secici]) => secici.trim() !== ':root')
    .flatMap(([, secici, govde]) => [...govde.matchAll(RENK_KODU)].map(e => `${secici.trim()} → ${e[0]}`));

test('renk kodu deseni rengi yakalar, belirteci ve beyaz boşluk özelliğini yakalamaz', () => {
  assert.deepEqual(kokDisiRenkler(':root{--a:#fff}.x{color:#fff;background:rgba(0,0,0,.1);border-color:white}'), [
    '.x → #fff',
    '.x → rgba(',
    '.x → white',
  ]);
  assert.deepEqual(kokDisiRenkler('.x{color:var(--white);white-space:nowrap;background:transparent;fill:currentColor}'), []);
});

test('masaüstü: renk kodları yalnız :root belirteçlerinde', async () => {
  const css = sikistir(await readFile(new URL('../Kasa.Api/wwwroot/styles.css', import.meta.url), 'utf8'));
  assert.deepEqual(kokDisiRenkler(css), [], 'styles.css: :root dışında renk kodu; belirteç tanımlayıp var(--…) kullanın');
});
