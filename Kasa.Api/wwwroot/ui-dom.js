// DOM yapı taşları: düğüm (h), denetim, alan, tablo, özet ve tutar yardımcıları. Uygulama durumu tutmaz, API çağırmaz; yalnız
// düğüm kurar. HTML dizesi ayrıştırılmaz: her metin textContent/metin düğümü olarak yazılır.
import { amount, cents, childValues, money, monthLabel, shiftMonth, statusLabels } from './ui-core.js';

const $ = selector => document.querySelector(selector);
function h(tag, props = {}, ...children) {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (value == null || value === false) continue;
    if (key.startsWith('on')) element.addEventListener(key.slice(2).toLowerCase(), value);
    else if (key === 'class') element.className = value;
    else if (key === 'text') element.textContent = value;
    else if (key === 'value') element.value = value;
    else if (key === 'checked') element.checked = Boolean(value);
    else element.setAttribute(key, value === true ? '' : String(value));
  }
  for (const child of childValues(children)) element.append(child instanceof Node ? child : document.createTextNode(String(child)));
  return element;
}
function button(text, action, kind = '', props = {}) {
  return h('button', { type: 'button', class: `button ${kind}`, onclick: action, ...props }, text);
}
function input(name, value = '', props = {}) {
  return h('input', { name, value: value ?? '', ...props });
}
function field(label, control, extra = null) {
  return h('label', {}, label, control, extra);
}
function select(name, choices, value = '', props = {}) {
  const control = h(
    'select',
    { name, ...props },
    choices.map(option => h('option', { value: option.value, disabled: option.disabled }, option.label))
  );
  control.value = value == null ? '' : String(value);
  return control;
}
function section(title, content, action) {
  return h('section', { class: 'section' }, h('div', { class: 'section-head' }, h('h2', {}, title), action), content);
}
function badge(status) {
  return h(
    'span',
    { class: `badge ${status === 'Onaylandi' ? 'approved' : status === 'Incelemede' ? 'review' : ''}` },
    statusLabels[status] || status
  );
}
function help(text) {
  return h('p', { class: 'help' }, text);
}
// Ay seçici: masaüstü Firefox ve Safari'de type="month" denetimi yok (MDN browser-compat-data). "‹ Eylül 2026 ›" düğmeleri
// YYYY-AA değerini değiştirir; değer adlı gizli alanda da durur. Grup erişilebilir adını label'dan alır; düğmeler <label> içine
// konmaz (etikete tıklamak önceki ayı seçerdi). min (YYYY-AA) verilirse o aydan önceye inilmez.
function monthPicker(name, value, { label, min = '' } = {}) {
  const field = h('input', { type: 'hidden', name, value });
  const text = h('span', { class: 'month-picker-value', 'aria-live': 'polite' });
  const previous = button('‹', () => set(shiftMonth(field.value, -1)), 'small', { 'aria-label': 'Önceki ay' });
  const next = button('›', () => set(shiftMonth(field.value, 1)), 'small', { 'aria-label': 'Sonraki ay' });
  function set(month) {
    field.value = month;
    text.textContent = monthLabel(month);
    previous.disabled = Boolean(min) && month <= min;
  }
  set(value);
  return {
    node: h('div', { class: 'month-picker', role: 'group', 'aria-label': label }, previous, text, next, field),
    get value() {
      return field.value;
    },
  };
}
// Kasa dağılımı düzenleyicisi: Yalnız genel kasa / seçilen kanallara eşit / kanal tutarları. Taban aylık gider şablonudur
// (varsayılanlar onun); ekstre satırı (statement-import-ui) aynı düzenleyiciyi kendi seçenekleriyle kullanır. Seçim ve
// tutarlar düzenleyicide tutulur, liste yeniden çizilince korunur. Seçenekler:
//   prefix       alan adları (`${prefix}-kanal-…`, `${prefix}-tutar-…`)
//   choices      dağılım seçimi seçenekleri; mode ile sonradan değiştirilip redraw ile yeniden çizilebilir
//   required     dağılım seçimi ve Özel kipte seçili kanalın tutarı zorunlu (form denetimi)
//   listClass    kanal listesinin sınıfı; emptyHidden: liste gizliyken (Genel ya da seçimsiz) boşaltılır
//   sortById     paylar kanal numarasına göre sıralanır (false: kanal listesi sırası)
//   onChange     seçim, tutar ya da dağılım değişince çağrılır
//   legend, label, note (yardım metni; null: yok) ve messages (mode / channel / sum hata iletileri)
// Ayrı kalanlar: alış satır editörü (editPurchase) kısmi dağılıma izin verir, kalem başına serbest satırlarla çalışır ve alıcı
// rolü de (telefonda) kullanır; kart dağılımı (finance-ui allocationEditor) serbest satırlıdır, boş bırakılabilir (Dağılım
// bekliyor) ve eksi tutarı (iade) mutlak değerle karşılaştırır.
function distribution(
  channels,
  initial,
  {
    prefix = 'dagilim',
    choices = [
      { value: '', label: 'Dağılım seçin' },
      { value: 'Genel', label: 'Yalnız genel kasa' },
      { value: 'Esit', label: 'Seçilen kanallara eşit' },
      { value: 'Ozel', label: 'Kanal tutarlarını gir' },
    ],
    required = true,
    listClass = 'stack',
    emptyHidden = false,
    sortById = true,
    onChange = () => {},
    legend = 'Kasa dağılımı',
    label = 'Dağılım',
    note = 'Yalnız genel kasa seçeneği hiçbir kanal kasasına yazılmaz. Eşit dağılımda seçtiğiniz kanallar sabittir; sonradan açılan kanallar bu plana eklenmez.',
    messages = {
      mode: 'Giderin hangi kasaya yazılacağını seçin.',
      channel: 'En az bir kanal seçin.',
      sum: 'Kanal paylarının toplamı gider tutarına eşit olmalı.',
    },
  } = {}
) {
  const selected = new Set((initial?.dagilimlar || []).map(row => row.kanalId));
  const totals = new Map((initial?.dagilimlar || []).map(row => [row.kanalId, row.tutar]));
  const list = h('div', { class: listClass });
  const mode = select('dagilimTuru', choices, initial?.dagilimTuru || '', { required });
  const draw = () => {
    list.hidden = !['Esit', 'Ozel'].includes(mode.value);
    if (emptyHidden && list.hidden) {
      list.replaceChildren();
      return;
    }
    list.replaceChildren(
      ...channels
        .filter(row => row.aktif || selected.has(row.id))
        .map(channel => {
          const checked = input(`${prefix}-kanal-${channel.id}`, channel.id, {
            type: 'checkbox',
            checked: selected.has(channel.id),
            onchange: () => {
              if (checked.checked) selected.add(channel.id);
              else selected.delete(channel.id);
              total.disabled = !selected.has(channel.id);
              total.required = required && mode.value === 'Ozel' && selected.has(channel.id);
              onChange();
            },
          });
          const total = input(`${prefix}-tutar-${channel.id}`, totals.get(channel.id) ?? '', {
            inputmode: 'decimal',
            required: required && mode.value === 'Ozel' && selected.has(channel.id),
            disabled: !selected.has(channel.id),
            oninput: () => {
              totals.set(channel.id, total.value);
              onChange();
            },
            'aria-label': `${channel.ad} payı (₺)`,
          });
          return h('div', { class: 'monthly-allocation' }, field(channel.ad, checked), mode.value === 'Ozel' && total);
        })
    );
  };
  mode.addEventListener('change', () => {
    draw();
    onChange();
  });
  draw();
  return {
    node: h('fieldset', {}, h('legend', {}, legend), field(label, mode), list, note && help(note)),
    mode,
    redraw: draw,
    read(total) {
      if (!['Genel', 'Esit', 'Ozel'].includes(mode.value)) throw new Error(messages.mode);
      if (mode.value === 'Genel') return { dagilimTuru: 'Genel', dagilimlar: [] };
      if (!selected.size) throw new Error(messages.channel);
      const ids = sortById ? [...selected].sort((a, b) => a - b) : channels.map(channel => channel.id).filter(id => selected.has(id));
      const result = ids.map(id => ({ kanalId: id, tutar: mode.value === 'Esit' ? 0 : cents(totals.get(id), { allowZero: false }) / 100 }));
      if (mode.value === 'Ozel' && result.reduce((sum, row) => sum + cents(row.tutar), 0) !== cents(total)) throw new Error(messages.sum);
      return { dagilimTuru: mode.value, dagilimlar: result };
    },
  };
}
function moneyNode(value, className = '') {
  return h('span', { class: `money ${className}` }, money(value));
}
// Kanal payı etiketleri (kart, kredi, aylık gider, ekstre, alış kalemi ve alış ödemesi): div.allocation-tags içinde her pay için
// span.allocation-tag "Kanal: tutar". empty: kanal adı yoksa yazılan ad (verilmezse ad olduğu gibi yazılır); pending: kanalı
// olmayan pay (kanalId yok) 'pending' sınıfıyla işaretlenir; pendingBadge: hiç pay yoksa aynı kutuya konan bekleme rozetinin
// metni (span.badge.pending; verilmezse kutu boş kalır).
function allocationTags(rows, { empty, pending = false, pendingBadge = null } = {}) {
  const list = rows || [];
  return h(
    'div',
    { class: 'allocation-tags' },
    list.map(row =>
      h(
        'span',
        { class: `allocation-tag${pending && row.kanalId == null ? ' pending' : ''}` },
        `${empty === undefined ? row.kanal : row.kanal || empty}: ${money(row.tutar)}`
      )
    ),
    pendingBadge != null && list.length === 0 && h('span', { class: 'badge pending' }, pendingBadge)
  );
}
function values(form) {
  return Object.fromEntries(new FormData(form));
}
function optionalId(value) {
  return value ? Number(value) : null;
}
function empty(title, text, action) {
  return h(
    'div',
    { class: 'empty' },
    h('span', { class: 'empty-mark', 'aria-hidden': 'true' }, '↳'),
    h('h2', {}, title),
    h('p', {}, text),
    action
  );
}
function summary(label, value, note) {
  return h(
    'div',
    { class: 'summary' },
    h('span', { class: 'summary-label' }, label),
    h('strong', { class: 'summary-value' }, value),
    note && h('div', { class: 'summary-note' }, note)
  );
}
// Tablo kapsayıcısı dar pencerede, büyütmede ya da yazı tipine göre yatay kayar (overflow:auto). Klavyeyle de kaydırılabilsin
// diye odaklanabilir, adlı bir bölgedir (WCAG 2.1.1; axe scrollable-region-focusable). Taşma yazı tipi, yakınlaştırma, pencere
// ve veriyle çalışırken değiştiğinden bütün tablolara uygulanır; bölge adı (name) zorunludur (ESLint denetler).
function table(headers, rows, name) {
  return h(
    'div',
    { class: 'table-wrap', role: 'region', 'aria-label': name, tabindex: '0' },
    h(
      'table',
      {},
      h(
        'thead',
        {},
        h(
          'tr',
          {},
          headers.map(label => h('th', { scope: 'col' }, label))
        )
      ),
      h(
        'tbody',
        {},
        rows.map(cells =>
          h(
            'tr',
            {},
            cells.map(cell => h('td', {}, cell))
          )
        )
      )
    )
  );
}
function signedAmount(value) {
  const text = String(value).trim().replace(',', '.');
  return text.startsWith('-') ? -amount(text.slice(1)) : amount(text);
}
// iOS ondalık klavyesinde eksi tuşu yok: eksi olabilen tutarın işareti ayrı seçilir, tutar mutlak değer olarak yazılır.
// Klavyesinde eksi olan kullanıcı eksi yazmaya devam edebilir; yazılan eksi "Artı" seçimiyle artıya dönmez. Kuruş kuralı signedAmount'tadır.
// Etiket iki denetimi sarar; 'for' ile tutar alanına bağlanır: etikete dokunmak işaret seçicisini değil tutarı odaklar.
let signedFieldCount = 0;
function signedAmountField(name, value, label) {
  const id = `signed-${name}-${++signedFieldCount}`;
  const sign = select(
    `${name}Isaret`,
    [
      { value: '+', label: 'Artı (+)' },
      { value: '-', label: 'Eksi (−)' },
    ],
    '+',
    { 'aria-label': `${label} işareti` }
  );
  const control = input(name, '', { id, inputmode: 'decimal', required: true, 'aria-label': label });
  const set = amountValue => {
    const number = amountValue === '' || amountValue == null ? NaN : Number(amountValue);
    sign.value = number < 0 ? '-' : '+';
    control.value = Number.isFinite(number) ? String(Math.abs(number)) : String(amountValue ?? '');
  };
  set(value);
  return {
    node: h('label', { class: 'signed-field', for: id }, label, h('div', { class: 'signed-amount' }, sign, control)),
    sign,
    input: control,
    set,
    read() {
      const typed = signedAmount(control.value);
      return sign.value === '-' ? -Math.abs(typed) : typed;
    },
    setReadOnly(locked) {
      control.readOnly = locked;
      sign.disabled = locked;
    },
  };
}

export {
  $,
  h,
  button,
  input,
  field,
  select,
  section,
  badge,
  help,
  monthPicker,
  distribution,
  moneyNode,
  allocationTags,
  values,
  optionalId,
  empty,
  summary,
  table,
  signedAmount,
  signedAmountField,
};
