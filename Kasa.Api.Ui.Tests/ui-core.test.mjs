import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { runInNewContext } from 'node:vm';

// Import the exact browser module without a package dependency or Node-specific production code.
const code = await readFile(new URL('../Kasa.Api/wwwroot/ui-core.js', import.meta.url), 'utf8');
const ui = await import('data:text/javascript;base64,' + Buffer.from(code).toString('base64'));
const loadBrowserModule = async file => import('data:text/javascript;base64,' + Buffer.from(await readFile(new URL(`../Kasa.Api/wwwroot/${file}`, import.meta.url), 'utf8')).toString('base64'));
const finance = await loadBrowserModule('finance-ui.js');
const notification = await loadBrowserModule('notification-ui.js');
const monthly = await loadBrowserModule('monthly-ui.js');
const cashControls = await loadBrowserModule('cash-controls-ui.js');
const statementImport = await loadBrowserModule('statement-import-ui.js');
const pushModule = await loadBrowserModule('push-client.js');
const { cents, amount, money, dateText, permissions, filteredPurchases, purchasePayload, errorMessage, MAX_CENTS, childValues, logoutAndClear, navigationFor, currentPeriod, monthlyTotals, loadRuntime, runtimeRequestAllowed, cashEditingAllowed, incomeSelection } = ui;

// Exercise the real startup and render functions with an inert DOM and deterministic API data.
async function openApp(readOnly, extraResponses = {}, pushEnvironment = null) {
  class Element {
    constructor(tag = 'div') { this.tag = tag; this.children = []; this.attributes = {}; this.listeners = {}; this.classList = { toggle() {} }; this.open = false; this.checked = false; this.isConnected = true; }
    set value(value) { this.currentValue = String(value); }
    get value() { return this.currentValue || ''; }
    setAttribute(name, value) { this.attributes[name] = value; if (['disabled', 'hidden', 'readonly'].includes(name)) this[name === 'readonly' ? 'readOnly' : name] = true; }
    addEventListener(name, handler) { this.listeners[name] = handler; }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children; }
    set textContent(value) { this.children = [String(value)]; }
    get textContent() { return this.children.map(child => typeof child === 'string' ? child : child.textContent).join(''); }
    querySelector(selector) { return this.find(node => selector === 'button[type="submit"]' ? node.tag === 'button' && node.attributes.type === 'submit' : selector.startsWith('.') ? (node.className || '').split(' ').includes(selector.slice(1)) : node.tag === selector); }
    find(predicate) { for (const node of this.children) { if (typeof node === 'string') continue; if (predicate(node)) return node; const child = node.find(predicate); if (child) return child; } return null; }
    focus() {}
    close() { this.open = false; }
    showModal() { this.open = true; }
    reportValidity() { return true; }
    requestSubmit() { this.listeners.submit?.({ preventDefault() {} }); }
    scrollIntoView() {}
  }
  class TestFormData {
    constructor(form) {
      this.items = [];
      const visit = node => {
        if (typeof node === 'string' || node.disabled) return;
        if (['input', 'select', 'textarea'].includes(node.tag) && node.attributes.name && (node.attributes.type !== 'checkbox' || node.checked)) this.items.push([node.attributes.name, node.value]);
        node.children.forEach(visit);
      };
      if (form) visit(form);
    }
    append(name, value) { this.items.push([name, value]); }
    [Symbol.iterator]() { return this.items[Symbol.iterator](); }
  }
  const nodes = new Map(); const requests = [];
  const responses = { '/kasa-runtime.json': { saltOkunur: readOnly, surum: 'test' }, '/api/auth/me': { rol: 'editor' }, '/api/rapor/panel': { guncelKasa: 123, buHaftaSonucu: 20, buAySonucu: 50, kanallar: [{ kanal: 'Mağaza', bakiye: 123 }] }, '/api/alis': [], '/api/kasa-esikleri': [], '/api/kasa-kontrol': [], '/api/ay-kilidi': { surum: 0, kilitliSonTarih: null, gecmis: [] }, '/api/surum': { surum: '2.2.0' }, '/api/takip/ozet?gun=30': { kartBorcu: 0, kalanKrediPlani: 0, olaylar: [] }, '/api/islemler/benzerlik': [], ...extraResponses };
  const calls = [];
  let nextId = 0;
  const context = { ...ui, ...finance, ...notification, ...monthly, ...cashControls, ...statementImport, ...pushModule, crypto: { randomUUID: () => `11111111-1111-4111-8111-${String(++nextId).padStart(12, '0')}` }, Headers, FormData: TestFormData, URLSearchParams, URL, Node: Element, setTimeout: () => 0, document: { querySelector: key => { if (!nodes.has(key)) nodes.set(key, new Element()); return nodes.get(key); }, createElement: tag => new Element(tag), createTextNode: text => String(text) }, fetch: async (path, options = {}) => { requests.push(path); const call = { path, method: options.method || 'GET', body: options.body instanceof TestFormData ? Object.fromEntries(options.body) : options.body ? JSON.parse(options.body) : null }; calls.push(call); if (!(path in responses)) throw new Error(`Unexpected API: ${path}`); const response = responses[path]; if (response instanceof Error) throw response; const value = typeof response === 'function' ? await response(call) : response; return { ok: !value?.$status, status: value?.$status || 200, json: async () => value, text: async () => JSON.stringify(value) }; } };
  if (pushEnvironment) context.createPushClient = options => pushModule.createPushClient({ ...options, environment: pushEnvironment });
  const source = await readFile(new URL('../Kasa.Api/wwwroot/app.js', import.meta.url), 'utf8');
  runInNewContext(source.replace(/^import[^\n]+\n/gm, '') + '\nglobalThis.appTest = { navigate, incomeDialog, expenseDialog, paymentDialog, paymentRow, financeUi, notificationUi, monthlyUi, cashControlsUi, statementImportUi, renderMonthly, clearSession };', context);
  for (let index = 0; index < 10; index++) await new Promise(resolve => setImmediate(resolve));
  return { nodes, requests, calls, responses, app: context.appTest };
}

test('full editor startup renders transaction actions and settings navigation', async () => {
  const { nodes, requests } = await openApp(false);
  assert.match(nodes.get('#navigation').textContent, /İşlemler.*Alışlar.*Ayarlar/);
  assert.match(nodes.get('#page-actions').textContent, /Gelir gir.*Gider kaydet/);
  assert.equal(nodes.get('#application').hidden, false);
  assert.deepEqual(requests.slice(0, 2), ['/kasa-runtime.json', '/api/auth/me']);
  assert.ok(requests.includes('/api/alis'));
});
test('read-only editor startup renders live cash without mutation actions or unsupported API calls', async () => {
  const { nodes, requests } = await openApp(true);
  assert.doesNotMatch(nodes.get('#navigation').textContent, /Alışlar|Ayarlar/);
  assert.equal(nodes.get('#page-actions').textContent, '');
  assert.match(nodes.get('#view').textContent, /123,00/);
  assert.deepEqual(requests, ['/kasa-runtime.json', '/api/auth/me', '/api/rapor/panel']);
});
const sampleCard = { id: 4, surum: 2, ad: 'İş kartı', yeniTakip: true, aktif: true, takipBaslangic: '2026-09-23', kesimGunu: 20, sonOdemeGunu: 30, limit: 20000, borc: 12000, ekstreBorc: 12000, harcamalar: [], odemeler: [], ekstreler: [{ id: 8, kesimTarihi: '2026-09-20', sonOdemeTarihi: '2026-09-30', borc: 12000, odenen: 0, kalan: 12000, asgariOdeme: null }] };
const sampleLoan = { id: 5, surum: 1, ad: 'İş kredisi', yeniTakip: true, aktif: true, cekilenTutar: 120000, cekimTarihi: '2026-09-23', kalanPlanliOdeme: 12000, kanalPaylari: [{ kanalId: 1, kanal: 'A', tutar: 120000 }], taksitler: [{ id: 9, no: 1, tarih: '2026-10-23', tutar: 12000, durum: 'Bekliyor', not: null, dagilimlar: [{ kanalId: 1, kanal: 'A', tutar: 12000 }] }] };
const settle = async () => { for (let index = 0; index < 12; index++) await new Promise(resolve => setImmediate(resolve)); };
async function submitDialog(nodes) { const form = nodes.get('#modal-content').find(node => node.tag === 'form'); assert.ok(form, 'Dialog form exists'); form.listeners.submit({ preventDefault() {} }); await settle(); }
const formField = (nodes, name) => nodes.get('#modal-content').find(node => node.attributes.name === name);
async function clickDialog(nodes, label) { const control = nodes.get('#modal-content').find(node => node.tag === 'button' && node.textContent === label); assert.ok(control, `${label} exists`); await control.listeners.click({ currentTarget: control }); await settle(); }

test('channel boxes show server card debt separately from cash and keep unknown debt and card credit separate', async () => {
  const { nodes } = await openApp(false, {
    '/api/rapor/panel': { guncelKasa: 123, buHaftaSonucu: 20, buAySonucu: 50, kanallar: [{ kanalId: 1, kanal: 'Yeni ad', bakiye: 100 }, { kanalId: 2, kanal: 'Dağılım bekliyor', bakiye: 23 }] },
    '/api/takip/ozet?gun=30': { kartBorcu: 95, kartAlacakBakiyesi: 15, kalanKrediPlani: 0, olaylar: [], kanalKartBorclari: [{ kanalId: 1, kanal: 'Eski ad', tutar: 40 }, { kanalId: 2, kanal: 'Dağılım bekliyor', tutar: 20 }, { kanalId: null, kanal: 'Dağılım bekliyor', tutar: 30 }, { kanalId: 3, kanal: 'Pasif kanal', tutar: 5 }] }
  });
  const view = nodes.get('#view'); const balances = view.find(node => node.className === 'channel-balances');
  assert.match(balances.children[0].textContent, /Yeni ad.*100,00.*Kalan kart borcu:.*40,00/);
  assert.match(balances.children[1].textContent, /Dağılım bekliyor.*23,00.*Kalan kart borcu:.*20,00/);
  assert.match(view.textContent, /Kanalı belirsiz kart borcu:.*30,00/); assert.match(view.textContent, /Pasif kanal kart borcu:.*5,00/);
  assert.equal(view.find(node => node.className === 'cash-total money').textContent, money(123));
  assert.match(view.textContent, /Toplam kart borcu.*95,00/); assert.match(view.textContent, /Kart alacak bakiyesi.*15,00.*Diğer kartların borcundan düşülmez/);
});
test('card details show server minimum status and remaining channel debt without changing payment values', async () => {
  const card = { ...sampleCard, kanalKartBorclari: [{ kanalId: 1, kanal: 'MEZAT', tutar: 70 }, { kanalId: null, kanal: 'Dağılım bekliyor', tutar: 30 }], ekstreler: [
    { ...sampleCard.ekstreler[0], asgariOdeme: 100, asgariKalan: 37, odenen: 10 },
    { ...sampleCard.ekstreler[0], id: 9, asgariOdeme: 100, asgariKalan: 0 },
    { ...sampleCard.ekstreler[0], id: 10, asgariOdeme: null, asgariKalan: null }
  ] };
  const { app, nodes } = await openApp(false, { '/api/auth/me': { rol: 'viewer' }, '/api/takip/kartlar/4': card });
  await app.navigate('cards', 4);
  assert.match(nodes.get('#view').textContent, /MEZAT:.*70,00.*Dağılım bekliyor:.*30,00/);
  assert.match(nodes.get('#view').textContent, /Kayıtlı ödemelere göre asgari için.*37,00.*kaldı/);
  assert.match(nodes.get('#view').textContent, /Kayıtlı ödemelere göre asgari tamamlandı/);
  assert.match(nodes.get('#view').textContent, /Girilmedi/);
  assert.equal(nodes.get('#page-actions').textContent, '');
});
test('purchase card name links to finance only for editors and viewers, while buyer sees plain text', async () => {
  const payment = { id: 8, tarih: '2026-09-23', tutar: 50, krediKartiId: 4, krediKartiAdi: 'İş kartı', dagilimBekliyor: false, dagilimlar: [] };
  for (const role of ['editor', 'viewer', 'alici']) {
    const { app, requests, nodes } = await openApp(false, { '/api/auth/me': { rol: role }, '/api/alis/kanallar': [], '/api/takip/kartlar/4': sampleCard });
    const row = app.paymentRow({ id: 1 }, payment); assert.match(row.textContent, /İş kartı ile ödendi/);
    const link = row.find(node => node.tag === 'button' && node.textContent === 'İş kartı');
    if (role === 'alici') { assert.equal(link, null); assert.equal(requests.some(path => path.startsWith('/api/takip')), false); }
    else { assert.ok(link); await link.listeners.click(); await settle(); assert.equal(nodes.get('#page-title').textContent, 'İş kartı'); }
  }
});

const similarCharge = { kaynak: 'KartHarcama', id: 7, tarih: '2026-09-23', tutar: 75, aciklama: '<img src=x onerror=alert(1)> Mal', krediKartiId: 4, alisId: 6 };
test('card charge duplicate warning keeps the form, escapes descriptions and requires explicit separate-save approval', async () => {
  const { app, nodes, calls } = await openApp(false, { '/api/kanallar': [], '/api/takip/kartlar/4': sampleCard, '/api/takip/kartlar/4/harcamalar': sampleCard, '/api/islemler/benzerlik': [similarCharge] });
  await app.navigate('cards', 4);
  const open = nodes.get('#page-actions').find(node => node.textContent === '+ Harcama / iade'); await open.listeners.click({ currentTarget: open }); await settle();
  formField(nodes, 'aciklama').value = 'Ayrı mal'; formField(nodes, 'tutar').value = '75'; formField(nodes, 'taksitSayisi').value = '3';
  await submitDialog(nodes);
  assert.equal(calls.some(call => call.path.endsWith('/harcamalar')), false);
  assert.equal(formField(nodes, 'aciklama').value, 'Ayrı mal'); assert.equal(formField(nodes, 'taksitSayisi').value, '3');
  assert.match(nodes.get('#modal-content').textContent, /Benzer kayıt bulundu.*<img src=x onerror=alert\(1\)> Mal/);
  assert.equal(nodes.get('#modal-content').find(node => node.tag === 'img'), null);
  await clickDialog(nodes, 'Ayrı işlem olarak kaydet');
  const saved = calls.filter(call => call.path.endsWith('/harcamalar')); assert.equal(saved.length, 1); assert.equal(saved[0].body.tutar, 75); assert.equal(saved[0].body.taksitSayisi, 3);
  assert.equal(calls.filter(call => call.path === '/api/islemler/benzerlik').length, 1);
});
test('card payment duplicate approval retains preview amounts and the idempotency key after an uncertain save', async () => {
  let attempts = 0;
  const preview = { tutar: 2000, kasaEtkisi: 2000, dagilimlar: [{ kanalId: 1, kanal: 'MEZAT', tutar: 1200 }, { kanalId: 2, kanal: 'PERAKENDE', tutar: 800 }], ekstreler: [{ ekstreId: 8, tutar: 2000 }] };
  const { app, nodes, calls } = await openApp(false, { '/api/takip/kartlar/4/odeme-onizleme': preview, '/api/takip/kartlar/4': sampleCard, '/api/islemler/benzerlik': [{ ...similarCharge, kaynak: 'KartOdeme', tutar: 2000 }], '/api/takip/kartlar/4/odemeler': () => { if (++attempts === 1) throw new Error('Network response lost'); return sampleCard; } });
  app.financeUi.cardPaymentDialog(sampleCard); formField(nodes, 'tutar').value = '2000'; await submitDialog(nodes);
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /MEZAT:.*1\.200,00.*PERAKENDE:.*800,00.*Benzer kayıt bulundu/);
  assert.equal(calls.some(call => call.path.endsWith('/odemeler')), false);
  await clickDialog(nodes, 'Ayrı işlem olarak kaydet'); await submitDialog(nodes);
  const writes = calls.filter(call => call.path.endsWith('/odemeler')); assert.equal(writes.length, 2); assert.deepEqual(writes[0].body, writes[1].body);
  const previewCall = calls.find(call => call.path.endsWith('/odeme-onizleme')); assert.equal(writes[0].body.istekId, previewCall.body.istekId);
  assert.equal(calls.find(call => call.path === '/api/islemler/benzerlik').body.tur, 'KartOdeme');
});
test('new cash expense duplicate can be cancelled and lookup failure never silently saves it', async () => {
  const { app, nodes, calls, responses } = await openApp(false, { '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }], '/api/kredikartlari': [], '/api/islemler': [], '/api/islemler/benzerlik': [{ ...similarCharge, kaynak: 'Islem', krediKartiId: null }] });
  const fill = async () => { await app.expenseDialog(); for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A', tarih: '2026-09-23' })) formField(nodes, name).value = value; };
  await fill(); await submitDialog(nodes);
  const lookup = calls.find(call => call.path === '/api/islemler/benzerlik'); assert.deepEqual(lookup.body, { tur: 'Gider', tarih: '2026-09-23', tutar: 75, krediKartiId: null, kanal: 'A', alisId: null });
  await clickDialog(nodes, 'Vazgeç'); assert.equal(nodes.get('#modal').open, false);
  assert.equal(calls.some(call => call.path === '/api/islemler' && call.method === 'POST'), false);
  responses['/api/islemler/benzerlik'] = new Error('Offline'); await fill(); await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /Benzer kayıt kontrolü tamamlanamadı.*Kayıt yapılmadı/);
  assert.equal(formField(nodes, 'cari').value, 'Kargo'); assert.equal(calls.some(call => call.path === '/api/islemler' && call.method === 'POST'), false);
});
test('new purchase payment checks its purchase and card, while linking an existing expense skips duplicate checks', async () => {
  const purchase = { id: 6, surum: 2, kalan: 100, durum: 'Onaylandi' };
  const expense = { id: 20, tarih: '2026-09-23', tutarTl: 75, cari: 'Mal', krediKartiId: 4 };
  const { app, nodes, calls, responses } = await openApp(false, { '/api/kredikartlari': [{ id: 4, ad: 'İş kartı' }], '/api/islemler': [expense], '/api/alis/6/odemeler': purchase, '/api/alis/kanallar': [], '/api/islemler/benzerlik': [similarCharge] });
  await app.paymentDialog(purchase); formField(nodes, 'tutar').value = '75'; formField(nodes, 'krediKartiId').value = '4';
  await submitDialog(nodes);
  const lookup = calls.find(call => call.path === '/api/islemler/benzerlik'); assert.equal(lookup.body.tur, 'AlisOdeme'); assert.equal(lookup.body.alisId, 6); assert.equal(lookup.body.krediKartiId, 4);
  assert.equal(calls.some(call => call.path === '/api/alis/6/odemeler'), false);
  await clickDialog(nodes, 'Vazgeç');
  responses['/api/islemler/benzerlik'] = new Error('This must not be called');
  await app.paymentDialog(purchase); const existing = formField(nodes, 'mevcutIslemId'); existing.value = '20'; existing.listeners.change();
  await submitDialog(nodes);
  const save = calls.find(call => call.path === '/api/alis/6/odemeler'); assert.ok(save); assert.equal(save.body.mevcutIslemId, 20); assert.equal(save.body.tutar, 75);
  assert.equal(calls.filter(call => call.path === '/api/islemler/benzerlik').length, 1);
});
test('changing a field invalidates duplicate approval and cancelling during lookup cannot create an expense', async () => {
  const { app, nodes, calls, responses } = await openApp(false, { '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }], '/api/kredikartlari': [], '/api/islemler': [], '/api/islemler/benzerlik': [similarCharge] });
  await app.expenseDialog();
  for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A' })) formField(nodes, name).value = value;
  await submitDialog(nodes);
  formField(nodes, 'tutarTl').value = '80';
  await clickDialog(nodes, 'Ayrı işlem olarak kaydet');
  assert.equal(calls.filter(call => call.path === '/api/islemler/benzerlik').length, 2);
  assert.equal(calls.some(call => call.path === '/api/islemler' && call.method === 'POST'), false);
  let finishLookup;
  responses['/api/islemler/benzerlik'] = () => new Promise(resolve => { finishLookup = resolve; });
  await submitDialog(nodes); assert.equal(typeof finishLookup, 'function');
  await clickDialog(nodes, 'Vazgeç'); finishLookup([]); await settle();
  assert.equal(calls.some(call => call.path === '/api/islemler' && call.method === 'POST'), false);
});
test('editing an existing expense skips new-record duplicate lookup', async () => {
  const expense = { id: 20, tarih: '2026-09-23', tutarTl: 75, cari: 'Kargo', tip: 'Cari', kanal: 'A', not: '', krediKartiId: null };
  const { app, nodes, calls } = await openApp(false, { '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }], '/api/kredikartlari': [], '/api/islemler/20': expense, '/api/islemler/benzerlik': new Error('Must not call') });
  await app.expenseDialog(expense); formField(nodes, 'tutarTl').value = '80'; await submitDialog(nodes);
  assert.equal(calls.some(call => call.path === '/api/islemler/benzerlik'), false);
  assert.equal(calls.find(call => call.path === '/api/islemler/20').body.tutarTl, 80);
});
test('viewer can see card and loan details without payment, create or plan-change controls', async () => {
  const { app, nodes } = await openApp(false, { '/api/auth/me': { rol: 'viewer' }, '/api/takip/kartlar/4': sampleCard, '/api/takip/krediler/5': sampleLoan });
  await app.navigate('cards', 4);
  assert.match(nodes.get('#view').textContent, /Ekstreler.*12\.000,00/);
  assert.equal(nodes.get('#page-actions').textContent, '');
  await app.navigate('loans', 5);
  assert.match(nodes.get('#view').textContent, /Kalan planlı ödeme/);
  assert.doesNotMatch(nodes.get('#view').textContent, /Planı düzenle|Erken kapama/);
  await assert.rejects(app.navigate('notifications'), /erişim/);
});
test('buyer cannot open finance or notifications routes', async () => {
  const { app, requests } = await openApp(false, { '/api/auth/me': { rol: 'alici' }, '/api/alis/kanallar': [] });
  for (const route of ['cards', 'loans', 'notifications', 'tools']) await assert.rejects(app.navigate(route), /erişim/);
  assert.equal(requests.some(path => path.startsWith('/api/takip') || path.startsWith('/api/bildirimler')), false);
});
test('card payment requires visible server distribution preview before the payment POST', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/takip/kartlar/4/odeme-onizleme': { tutar: 2000, kasaEtkisi: 2000, dagilimlar: [{ kanalId: 1, kanal: 'MEZAT', tutar: 1200 }, { kanalId: 2, kanal: 'PERAKENDE', tutar: 800 }], ekstreler: [{ ekstreId: 8, tutar: 2000 }] },
    '/api/takip/kartlar/4/odemeler': sampleCard, '/api/takip/kartlar/4': sampleCard
  });
  app.financeUi.cardPaymentDialog(sampleCard);
  nodes.get('#modal-content').find(node => node.attributes.name === 'tutar').value = '2000,00';
  await submitDialog(nodes);
  assert.equal(calls.some(call => call.path.endsWith('/odemeler')), false);
  assert.match(nodes.get('#modal-content').textContent, /MEZAT:.*1\.200,00/);
  assert.match(nodes.get('#modal-content').textContent, /PERAKENDE:.*800,00/);
  await submitDialog(nodes);
  const payment = calls.find(call => call.path.endsWith('/odemeler'));
  assert.equal(payment.method, 'POST'); assert.equal(payment.body.tutar, 2000); assert.equal(payment.body.surum, 2); assert.ok(payment.body.istekId);
});
test('card refund requires an existing positive charge and delegates its original channel allocation to the server', async () => {
  const card = { ...sampleCard, harcamalar: [
    { id: 11, tarih: '2026-09-21', aciklama: 'A kanalı mal', tutar: 200, taksitSayisi: 1, dagilimlar: [] },
    { id: 12, tarih: '2026-09-22', aciklama: 'B kanalı mal', tutar: 300, taksitSayisi: 1, dagilimlar: [] },
    { id: 13, tarih: '2026-09-22', aciklama: 'Eski iade', tutar: -10, taksitSayisi: 1, dagilimlar: [] },
    { id: 14, tarih: '2026-09-22', aciklama: 'İptal harcama', tutar: 50, iptal: true, taksitSayisi: 1, dagilimlar: [] }
  ] };
  const { app, nodes, calls } = await openApp(false, { '/api/kanallar': [], '/api/takip/kartlar/4': card, '/api/takip/kartlar/4/harcamalar': card });
  await app.navigate('cards', 4);
  const open = nodes.get('#page-actions').find(node => node.tag === 'button' && node.textContent === '+ Harcama / iade');
  await open.listeners.click({ currentTarget: open }); await settle();
  const content = nodes.get('#modal-content');
  const total = content.find(node => node.attributes.name === 'tutar'); const source = content.find(node => node.attributes.name === 'kaynakHarcamaId');
  assert.equal(source.disabled, true);
  assert.doesNotMatch(source.textContent, /Eski iade|İptal harcama/);
  total.value = '-25,50'; total.listeners.input();
  content.find(node => node.attributes.name === 'aciklama').value = 'Mal iadesi';
  assert.equal(source.required, true); assert.equal(source.disabled, false);
  assert.equal(content.find(node => node.attributes.name === 'taksitSayisi').disabled, true);
  await submitDialog(nodes);
  assert.equal(calls.some(call => call.path.endsWith('/harcamalar')), false);
  assert.match(content.textContent, /İadenin bağlı olduğu harcamayı seçin/);
  source.value = '12'; await submitDialog(nodes);
  const refund = calls.find(call => call.path.endsWith('/harcamalar'));
  assert.equal(refund.body.kaynakHarcamaId, 12); assert.equal(refund.body.tutar, -25.5); assert.equal(refund.body.taksitSayisi, 1); assert.deepEqual(refund.body.dagilimlar, []);
  assert.equal(refund.body.ilkKesimTarihi, null);
});
test('positive card charge sends no refund source even after switching the amount from refund to purchase', async () => {
  const card = { ...sampleCard, harcamalar: [{ id: 11, tarih: '2026-09-21', aciklama: 'Mal', tutar: 200, taksitSayisi: 1, dagilimlar: [] }] };
  const { app, nodes, calls } = await openApp(false, { '/api/kanallar': [], '/api/takip/kartlar/4': card, '/api/takip/kartlar/4/harcamalar': card });
  await app.navigate('cards', 4);
  const open = nodes.get('#page-actions').find(node => node.tag === 'button' && node.textContent === '+ Harcama / iade');
  await open.listeners.click({ currentTarget: open }); await settle();
  const content = nodes.get('#modal-content'); const total = content.find(node => node.attributes.name === 'tutar'); const source = content.find(node => node.attributes.name === 'kaynakHarcamaId');
  total.value = '-5'; total.listeners.input(); source.value = '11';
  total.value = '75'; total.listeners.input();
  content.find(node => node.attributes.name === 'aciklama').value = 'Yeni mal';
  content.find(node => node.attributes.name === 'taksitSayisi').value = '3';
  assert.equal(source.disabled, true); assert.equal(source.required, false);
  await submitDialog(nodes);
  const charge = calls.find(call => call.path.endsWith('/harcamalar'));
  assert.equal(charge.body.kaynakHarcamaId, null); assert.equal(charge.body.tutar, 75); assert.equal(charge.body.taksitSayisi, 3);
});
test('new loan form sends one draw with selected fixed equal-share channel IDs', async () => {
  const { app, nodes, calls } = await openApp(false, { '/api/kanallar': [{ id: 3, ad: 'C', aktif: true }, { id: 1, ad: 'A', aktif: true }], '/api/takip/krediler': sampleLoan, '/api/takip/krediler/5': sampleLoan });
  await app.financeUi.loanDialog(); const content = nodes.get('#modal-content');
  assert.equal(content.find(node => node.attributes.name === 'mevcutKredi').value, 'false');
  assert.ok(content.find(node => node.attributes.name === 'ilkTaksitTarihi').value > content.find(node => node.attributes.name === 'cekimTarihi').value);
  for (const [name, value] of Object.entries({ ad: 'Yeni kredi', cekilenTutar: '120000', aylikOdeme: '12000', taksitSayisi: '10' })) content.find(node => node.attributes.name === name).value = value;
  for (const id of [3, 1]) { const choice = content.find(node => node.attributes.name === `kredi-kanal-${id}`); choice.checked = true; choice.listeners.change({ target: choice }); }
  await submitDialog(nodes);
  const create = calls.find(call => call.path === '/api/takip/krediler' && call.method === 'POST');
  assert.ok(create); assert.equal(create.body.cekilenTutar, 120000); assert.equal(create.body.mevcutKredi, false); assert.deepEqual(create.body.kanalIdleri, [1, 3]);
});
test('new loan default installment clamps the next month and preserves a manually chosen date', async () => {
  const { app, nodes } = await openApp(false, { '/api/kanallar': [] });
  await app.financeUi.loanDialog(); const content = nodes.get('#modal-content');
  const draw = content.find(node => node.attributes.name === 'cekimTarihi'); const first = content.find(node => node.attributes.name === 'ilkTaksitTarihi');
  draw.value = '2027-01-31'; draw.listeners.input(); assert.equal(first.value, '2027-02-28');
  draw.value = '2028-01-31'; draw.listeners.input(); assert.equal(first.value, '2028-02-29');
  draw.value = '2027-12-31'; draw.listeners.input(); assert.equal(first.value, '2028-01-31');
  first.value = '2028-02-15'; first.listeners.input();
  draw.value = '2028-01-20'; draw.listeners.input(); assert.equal(first.value, '2028-02-15');
});
test('processed installment form keeps amount and date locked and saves only its note without a new payment', async () => {
  const installment = { ...sampleLoan.taksitler[0], durum: 'KasayaIslendi' };
  const { app, nodes, calls } = await openApp(false, { '/api/takip/krediler/5/taksitler/9': sampleLoan, '/api/takip/krediler/5': sampleLoan });
  app.financeUi.installmentDialog(sampleLoan, installment); const content = nodes.get('#modal-content');
  assert.equal(content.find(node => node.attributes.name === 'tarih').disabled, true);
  assert.equal(content.find(node => node.attributes.name === 'tutar').disabled, true);
  content.find(node => node.attributes.name === 'not').value = 'Dekont incelendi'; content.find(node => node.attributes.name === 'aciklama').value = 'Not eklendi';
  await submitDialog(nodes);
  const save = calls.find(call => call.method === 'PUT'); assert.equal(save.body.tutar, 12000); assert.equal(save.body.iptal, false); assert.equal(save.body.not, 'Dekont incelendi');
  assert.equal(calls.some(call => call.method === 'POST'), false);
});
test('cash dashboard labels only overdue open card payments using the server date', async () => {
  const { nodes } = await openApp(false, { '/api/takip/ozet?gun=30': { tarih: '2030-09-23', kartBorcu: 500, kalanKrediPlani: 100, olaylar: [
    { kaynak: 'Kart', kaynakId: 4, ad: 'Geciken kart', tarih: '2030-09-22', tutar: 200, tur: 'SonOdeme', otomatikKasa: false },
    { kaynak: 'Kart', kaynakId: 5, ad: 'Bugünkü kart', tarih: '2030-09-23', tutar: 300, tur: 'SonOdeme', otomatikKasa: false },
    { kaynak: 'Kredi', kaynakId: 6, ad: 'İşlenmiş taksit', tarih: '2030-09-22', tutar: 100, tur: 'Taksit', otomatikKasa: true }
  ] } });
  assert.equal((nodes.get('#view').textContent.match(/Gecikti/g) || []).length, 1);
  assert.match(nodes.get('#view').textContent, /Yeni kart takibinde kaydedilen kart ödemeleri kasadan düşer/);
});
test('notification center marks an item read without creating a finance request', async () => {
  const { app, nodes, calls } = await openApp(false, { '/api/bildirimler': [{ id: 3, baslik: 'Kart ödemesi yaklaşıyor', mesaj: 'Üç gün sonra son ödeme.', tarih: '2026-09-23', okundu: false, hedef: '/#cards/4', tur: 'SonOdeme', kaynakId: 4 }], '/api/bildirimler/3/okundu': null });
  await app.navigate('notifications'); assert.match(nodes.get('#view').textContent, /Kart ödemesi yaklaşıyor/);
  const mark = nodes.get('#view').find(node => node.tag === 'button' && node.textContent === 'Okundu olarak işaretle'); await mark.listeners.click({ currentTarget: mark });
  const writes = calls.filter(call => call.method !== 'GET'); assert.deepEqual(writes.map(call => call.path), ['/api/bildirimler/3/okundu']);
});
function fakePushBrowser(permission = 'granted') {
  const counts = { permission: 0, register: 0, subscribe: 0, unsubscribe: 0 }; const saved = new Map();
  let subscription = null;
  const current = { endpoint: 'https://push.example/this-device', toJSON: () => ({ keys: { p256dh: 'device-public-key', auth: 'device-auth' } }), unsubscribe: async () => { counts.unsubscribe++; subscription = null; return true; } };
  const registration = { pushManager: { getSubscription: async () => subscription, subscribe: async () => { counts.subscribe++; subscription = current; return current; } } };
  const environment = { isSecureContext: true, PushManager: class {}, Notification: { permission: 'default', requestPermission: async () => { counts.permission++; return permission; } }, navigator: { serviceWorker: { getRegistration: async () => registration, register: async () => { counts.register++; return registration; }, ready: Promise.resolve(registration) } }, crypto: { randomUUID: () => '11111111-1111-4111-8111-111111111111' }, localStorage: { getItem: key => saved.get(key), setItem: (key, value) => saved.set(key, value) } };
  return { environment, counts, saved };
}
test('push startup never prompts; explicit enable registers only this device and persists no token', async () => {
  const browser = fakePushBrowser(); const calls = [];
  const client = pushModule.createPushClient({ environment: browser.environment, api: async (path, options) => { calls.push({ path, options }); return path.endsWith('/anahtar') ? { etkin: true, publicKey: 'AQID' } : { id: 1 }; } });
  await client.status(); assert.equal(browser.counts.permission, 0); assert.equal(browser.counts.register, 0);
  await client.enable('Telefonum'); assert.equal(browser.counts.permission, 1); assert.equal(browser.counts.subscribe, 1);
  const subscribe = calls.find(call => call.path.endsWith('/abonelik')); assert.equal(subscribe.options.body.cihazAdi, 'Telefonum'); assert.equal(subscribe.options.body.endpoint, 'https://push.example/this-device');
  assert.deepEqual([...browser.saved.keys()], ['kasa-device-id']);
});
test('notification settings can renew a disabled server registration while retaining the existing browser subscription', async () => {
  const browser = fakePushBrowser();
  const firstClient = pushModule.createPushClient({ environment: browser.environment, api: async () => ({ etkin: true, publicKey: 'AQID' }) });
  await firstClient.enable('İlk kayıt');
  const { app, nodes, calls } = await openApp(false, {
    '/api/bildirimler/ayarlar': { etkin: true, saat: 9, dakika: 0, surum: 1 },
    '/api/bildirimler/push/anahtar': { etkin: true, publicKey: 'AQID' },
    '/api/bildirimler/push/abonelikler': [{ id: 7, cihazAdi: 'Telefon', etkin: false, sonBasarili: null }],
    '/api/bildirimler/push/abonelik': { id: 7, cihazAdi: 'Telefonum' }
  }, browser.environment);
  await app.notificationUi.settings();
  const content = nodes.get('#modal-content');
  content.find(node => node.attributes.name === 'cihazAdi').value = 'Telefonum';
  const renew = content.find(node => node.tag === 'button' && node.textContent === 'Bildirim kaydını yenile');
  assert.ok(renew, 'An existing browser subscription still exposes registration renewal');
  await renew.listeners.click({ currentTarget: renew }); await settle();
  const writes = calls.filter(call => call.method !== 'GET');
  assert.deepEqual(writes.map(call => call.path), ['/api/bildirimler/push/abonelik']);
  assert.equal(writes[0].body.endpoint, 'https://push.example/this-device');
  assert.equal(writes[0].body.cihazAdi, 'Telefonum');
  assert.equal(browser.counts.subscribe, 1); assert.equal(browser.counts.unsubscribe, 0);
  assert.equal(calls.filter(call => call.path === '/api/bildirimler/ayarlar').length, 2, 'Settings reload after renewal');
  assert.match(nodes.get('#notifications').textContent, /bildirim kaydı yenilendi/);
});
test('denied push permission causes no API subscription and logout still unsubscribes when API deletion fails', async () => {
  const denied = fakePushBrowser('denied'); let requests = 0;
  const deniedClient = pushModule.createPushClient({ environment: denied.environment, api: async () => { requests++; } });
  await assert.rejects(deniedClient.enable('Telefon'), /izin/); assert.equal(requests, 0); assert.equal(denied.counts.subscribe, 0);
  const browser = fakePushBrowser(); const calls = [];
  const client = pushModule.createPushClient({ environment: browser.environment, api: async (path, options = {}) => { calls.push({ path, options }); if (options.method === 'DELETE') throw new Error('401'); return { etkin: true, publicKey: 'AQID' }; } });
  await client.enable('Bilgisayar'); await client.disable({ bestEffort: true });
  assert.equal(browser.counts.unsubscribe, 1);
  const deletes = calls.filter(call => call.options.method === 'DELETE'); assert.equal(deletes.length, 1); assert.deepEqual(deletes[0].options.body, { endpoint: 'https://push.example/this-device' });
});
test('push permission result from a previous session cannot register the next user device', async () => {
  const browser = fakePushBrowser(); let finishPermission; let session = 1; let requests = 0;
  browser.environment.Notification.requestPermission = () => new Promise(resolve => { finishPermission = resolve; });
  const client = pushModule.createPushClient({ environment: browser.environment, session: () => session, api: async () => { requests++; } });
  const enabling = client.enable('Telefon'); session = 2; finishPermission('granted');
  await assert.rejects(enabling, /Oturum değişti/); assert.equal(requests, 0); assert.equal(browser.counts.subscribe, 0);
});
test('a rejected server subscription rolls back a new browser subscription', async () => {
  const browser = fakePushBrowser();
  const client = pushModule.createPushClient({ environment: browser.environment, api: async path => { if (path.endsWith('/anahtar')) return { etkin: true, publicKey: 'AQID' }; throw new Error('Registration failed'); } });
  await assert.rejects(client.enable('Telefon'), /Registration failed/); assert.equal(browser.counts.unsubscribe, 1);
});
test('notification deep links enforce financial role and service worker rejects external targets', async () => {
  assert.deepEqual(pushModule.notificationRoute('#cards/4', 'editor'), { view: 'cards', id: 4 });
  assert.equal(pushModule.notificationRoute('#loans/5', 'alici'), null); assert.equal(pushModule.notificationRoute('#notifications', 'viewer'), null);
  const handlers = {}; const shown = [];
  const worker = { location: { origin: 'https://kasa.example' }, addEventListener: (name, handler) => { handlers[name] = handler; }, registration: { showNotification: async (title, options) => { shown.push({ title, options }); } } };
  runInNewContext(await readFile(new URL('../Kasa.Api/wwwroot/service-worker.js', import.meta.url), 'utf8'), { self: worker, URL });
  let done; handlers.push({ data: { json: () => ({ baslik: 'Ödeme', mesaj: 'Kart', url: 'https://attacker.example/#cards/4' }) }, waitUntil: promise => { done = promise; } }); await done;
  assert.equal(shown[0].options.data.url, 'https://kasa.example/#notifications');
  handlers.push({ data: { json: () => ({ url: '/#loans/5' }) }, waitUntil: promise => { done = promise; } }); await done;
  assert.equal(shown[1].options.data.url, 'https://kasa.example/#loans/5');
  assert.equal('fetch' in handlers, false);
});
test('legacy income selection sums preserved rows without selecting one arbitrarily', () => {
  const rows = [{ kanal: 'A', tutarTl: 0.10, eskiYinelenenGrup: true }, { kanal: 'A', tutarTl: 0.20, eskiYinelenenGrup: true }, { kanal: 'B', tutarTl: 5 }];
  assert.deepEqual(incomeSelection(rows, 'A'), { total: 0.30, count: 2, readOnly: true });
  assert.deepEqual(incomeSelection(rows, 'B'), { total: 5, count: 1, readOnly: false });
  assert.deepEqual(incomeSelection(rows, 'C'), { total: 0, count: 0, readOnly: false });
  assert.equal(incomeSelection([{ kanal: 'A', tutarTl: 5, eskiYinelenenGrup: true }], 'A').readOnly, true);
  assert.equal(incomeSelection([{ kanal: 'A', tutarTl: 5 }, { kanal: 'A', tutarTl: -2 }], 'A').readOnly, true);
});
test('income selection uses stable channel IDs across case variants and renames', () => {
  const rows = [
    { kanalId: 7, kanal: 'SHOP', tutarTl: 100, eskiYinelenenGrup: true },
    { kanalId: 7, kanal: 'shop', tutarTl: 200, eskiYinelenenGrup: true },
    { kanalId: 8, kanal: 'Yeni mağaza', tutarTl: 999 }
  ];
  assert.deepEqual(incomeSelection(rows, { id: 7, ad: 'Yeni mağaza' }), { total: 300, count: 2, readOnly: true });
  assert.deepEqual(incomeSelection(rows, { id: 8, ad: 'SHOP' }), { total: 999, count: 1, readOnly: false });
});
test('income name fallback applies only to rows without IDs and matches SQLite ASCII NOCASE', () => {
  const rows = [{ kanal: 'SHOP', tutarTl: 10 }, { kanalId: 8, kanal: 'shop', tutarTl: 999 }];
  assert.deepEqual(incomeSelection(rows, { id: 7, ad: 'shop' }), { total: 10, count: 1, readOnly: false });
  assert.deepEqual(incomeSelection([{ kanal: 'İSİM', tutarTl: 50 }, { kanal: 'ISIM', tutarTl: 20 }, { kanal: 'ısım', tutarTl: 90 }], { id: 7, ad: 'isim' }), { total: 20, count: 1, readOnly: false });
});
test('income form locks only the duplicate group and unlocks normal channels and new periods', async () => {
  const oldPeriod = '2026-09-07'; const newPeriod = '2026-09-14';
  const { nodes, app, requests } = await openApp(false, {
    '/api/rapor/haftalik': [{ donem: { start: oldPeriod, end: '2026-09-13' } }, { donem: { start: newPeriod, end: '2026-09-20' } }],
    '/api/kanallar': [{ id: 7, ad: 'Mağaza' }, { id: 8, ad: 'Normal' }],
    [`/api/gelenler?donemStart=${oldPeriod}`]: [{ kanalId: 7, kanal: 'OLD SHOP', tutarTl: 100.10, eskiYinelenenGrup: true }, { kanalId: 7, kanal: 'old shop', tutarTl: 200.20, eskiYinelenenGrup: true }],
    [`/api/gelenler?donemStart=${newPeriod}`]: [{ kanalId: 7, kanal: 'Mağaza', tutarTl: 42 }]
  });
  await app.incomeDialog(oldPeriod);
  const content = nodes.get('#modal-content');
  const channel = content.find(node => node.attributes.name === 'kanal');
  const period = content.find(node => node.attributes.name === 'donemStart');
  const total = content.find(node => node.attributes.name === 'tutarTl');
  const submit = content.querySelector('button[type="submit"]');
  channel.value = 'Mağaza'; channel.listeners.change();
  assert.equal(total.value, '300.3'); assert.equal(total.readOnly, true); assert.equal(submit.disabled, true);
  assert.match(content.textContent, /2 eski gelir kaydı/);
  channel.value = 'Normal'; channel.listeners.change();
  assert.equal(total.value, '0'); assert.equal(total.readOnly, false); assert.equal(submit.disabled, false);
  channel.value = 'Mağaza'; channel.listeners.change();
  period.value = newPeriod; await period.listeners.change();
  assert.equal(total.value, '42'); assert.equal(total.readOnly, false); assert.equal(submit.disabled, false);
  period.value = '2026-09-21'; await period.listeners.change();
  assert.equal(total.readOnly, true); assert.equal(submit.disabled, true);
  assert.match(content.textContent, /yüklenemedi/);
  assert.equal(requests.includes('/api/gelenler'), false);
});

test('optional runtime enables normal v2 only on 404 and reads the deployment capability', async () => {
  let request;
  assert.deepEqual(await loadRuntime(async (path, options) => { request = { path, options }; return { status: 404 }; }), { saltOkunur: false, surum: null });
  assert.equal(request.path, '/kasa-runtime.json');
  assert.equal(request.options.cache, 'no-store');
  assert.deepEqual(await loadRuntime(async () => ({ status: 200, ok: true, json: async () => ({ saltOkunur: true, surum: '1.0-web' }) })), { saltOkunur: true, surum: '1.0-web' });
});
test('runtime network, server and malformed config failures never silently enable writes', async () => {
  await assert.rejects(loadRuntime(async () => { throw new Error('offline'); }));
  await assert.rejects(loadRuntime(async () => ({ status: 503, ok: false })));
  for (const config of [null, {}, { saltOkunur: 'true' }, { saltOkunur: false, surum: 1 }]) await assert.rejects(loadRuntime(async () => ({ ok: true, status: 200, json: async () => config })));
  assert.equal(runtimeRequestAllowed(null, '/api/auth/login', 'POST'), false);
});
test('read-only runtime allows live cash reads and login/logout but blocks all other writes and unsupported APIs', () => {
  const runtime = { saltOkunur: true };
  for (const path of ['/api/auth/me', '/api/rapor/panel', '/api/rapor/haftalik', '/api/rapor/aylik?yil=2026&ay=9', '/api/islemler?kanal=A', '/api/kanallar']) assert.equal(runtimeRequestAllowed(runtime, path), true, path);
  for (const path of ['/api/auth/login', '/api/auth/logout']) assert.equal(runtimeRequestAllowed(runtime, path, 'POST'), true, path);
  for (const path of ['/api/alis', '/api/surum', '/api/ayarlar', '/api/alicilar', '/api/disari-aktar', '/api/yedek/durum', '/api/auth/kurtar']) assert.equal(runtimeRequestAllowed(runtime, path), false, path);
  for (const method of ['POST', 'PUT', 'PATCH', 'DELETE']) {
    for (const path of ['/api/islemler', '/api/gelenler', '/api/kanallar', '/api/alis/1/onayla', '/api/yedek', '/api/auth/sifre', '/api/auth/kurtar']) assert.equal(runtimeRequestAllowed(runtime, path, method), false, `${method} ${path}`);
  }
  assert.equal(runtimeRequestAllowed({ saltOkunur: false }, '/api/alis', 'POST'), true);
});
test('read-only navigation contains only four cash views and never grants buyer finance', () => {
  for (const role of ['editor', 'viewer']) assert.deepEqual(navigationFor(role, { saltOkunur: true }).map(([id]) => id), ['home', 'weekly', 'monthly', 'transactions']);
  assert.deepEqual(navigationFor('alici', { saltOkunur: true }), []);
});
test('explicit full runtime restores editor transactions and settings without granting viewer or buyer writes', async () => {
  const runtime = await loadRuntime(async () => ({ ok: true, status: 200, json: async () => ({ saltOkunur: false, surum: '2.0.0' }) }));
  assert.deepEqual(navigationFor('editor', runtime).map(([id]) => id), ['home', 'weekly', 'monthly', 'transactions', 'monthly-expenses', 'cards', 'loans', 'purchases', 'imports', 'notifications', 'tools']);
  assert.equal(cashEditingAllowed('editor', runtime), true);
  for (const path of ['/api/gelenler', '/api/islemler', '/api/ayarlar']) assert.equal(runtimeRequestAllowed(runtime, path, 'PUT'), true, path);
  for (const role of ['viewer', 'alici']) assert.equal(cashEditingAllowed(role, runtime), false);
  assert.equal(cashEditingAllowed('editor', { saltOkunur: true }), false);
  assert.equal(cashEditingAllowed('editor', null), false);
});
test('legacy monthly report has no pending amount and still computes live totals', () => {
  assert.deepEqual(monthlyTotals({ kanallar: [{ gelen: 300, cariGiden: 40, sabitGider: 20, krediKarti: 10, ortakPay: 5 }] }), { incoming: 300, expenses: 75, result: 225 });
});

test('browser entry and helper parse as explicit ES modules before the login screen starts', async () => {
  for (const file of ['app.js', 'ui-core.js', 'finance-ui.js', 'monthly-ui.js', 'cash-controls-ui.js', 'statement-import-ui.js', 'notification-ui.js', 'push-client.js', 'service-worker.js']) {
    const source = await readFile(new URL(`../Kasa.Api/wwwroot/${file}`, import.meta.url), 'utf8');
    const result = spawnSync(process.execPath, ['--input-type=module', '--check'], { input: source, encoding: 'utf8' });
    assert.equal(result.status, 0, `${file}: ${result.error?.message || result.stderr || 'ES module syntax check failed'}`);
  }
});

test('optional view content omits null and false while preserving numeric zero', () => {
  assert.deepEqual(childValues(['Alış', null, [undefined, false, [0, '']]]), ['Alış', 0, '']);
});
test('successful logout clears private in-memory data', async () => {
  const privateState = { purchases: [{ id: 7 }] };
  await logoutAndClear(async () => {}, () => { privateState.purchases = []; });
  assert.deepEqual(privateState.purchases, []);
});
test('network failure still clears private data and does not claim server logout succeeded', async () => {
  const privateState = { purchases: [{ id: 7 }] };
  await assert.rejects(logoutAndClear(async () => { throw new Error('offline'); }, () => { privateState.purchases = []; }), /sunucu oturumu kapatılamadı/);
  assert.deepEqual(privateState.purchases, []);
});

test('Turkish decimal input is exact and does not treat grouping as decimals', () => {
  assert.equal(cents(' 1234,56 '), 123456);
  assert.equal(cents('1234.56'), 123456);
  assert.equal(cents('0,01'), 1);
  assert.equal(cents('00001,2'), 120);
  for (const value of ['1.234,56', '1,234.56', '1,234', '1.234', '1e3', '-1', '', 'NaN', 'Infinity']) assert.throws(() => cents(value), value);
});
test('money parser retains the accepted domain maximum and rejects overflow', () => {
  assert.equal(cents('999999999999.99'), MAX_CENTS);
  assert.equal(amount('0,10'), 0.1);
  assert.throws(() => cents('1000000000000'));
  assert.throws(() => cents('999999999999999999999999999999'));
  assert.throws(() => cents('0', { allowZero: false }));
  assert.equal(cents('0'), 0);
});
const draft = overrides => ({ surum: 3, tarih: '2026-09-23', tedarikci: '  Ödeme yapılan yer  ', not: '  Not  ', kalemler: [{ aciklama: '  Zeytinyağı  ', tutar: '250,05', dagilimlar: [{ kanalId: '2', tutar: '200,00' }, { kanalId: '3', tutar: '50,05' }] }], ...overrides });
test('simple purchase preserves free text, version, amount and exact channel shares', () => {
  assert.deepEqual(purchasePayload(draft()), { surum: 3, tarih: '2026-09-23', tedarikci: 'Ödeme yapılan yer', not: 'Not', kalemler: [{ aciklama: 'Zeytinyağı', tutar: 250.05, dagilimlar: [{ kanalId: 2, tutar: 200 }, { kanalId: 3, tutar: 50.05 }] }] });
});
test('legacy ERP metadata cannot leak into the simplified purchase write contract', () => {
  const input = draft({ tedarikciId: 7, vade: '2026-10-01', hesapId: 5 });
  input.kalemler[0].miktar = 2; input.kalemler[0].birimFiyat = 125.025;
  const payload = purchasePayload(input);
  for (const field of ['tedarikciId', 'vade', 'hesapId']) assert.equal(Object.hasOwn(payload, field), false);
  for (const field of ['miktar', 'birimFiyat']) assert.equal(Object.hasOwn(payload.kalemler[0], field), false);
});
test('unknown allocation stays empty; no default common channel is invented', () => {
  const payload = purchasePayload(draft({ not: '', kalemler: [{ aciklama: 'Mal', tutar: '50', dagilimlar: [] }] }));
  assert.deepEqual(payload.kalemler[0].dagilimlar, []);
  assert.equal(payload.not, null);
});
test('partial draft allocations remain permitted but over-allocation and duplicates fail', () => {
  const build = dagilimlar => draft({ kalemler: [{ aciklama: 'Mal', tutar: 10, dagilimlar }] });
  assert.equal(purchasePayload(build([{ kanalId: 1, tutar: 5 }])).kalemler[0].dagilimlar[0].tutar, 5);
  assert.throws(() => purchasePayload(build([{ kanalId: 1, tutar: 11 }])));
  assert.throws(() => purchasePayload(build([{ kanalId: 1, tutar: 3 }, { kanalId: '1', tutar: 7 }])));
  assert.throws(() => purchasePayload(build([{ kanalId: '', tutar: 5 }])));
  assert.throws(() => purchasePayload(build([{ kanalId: 1, tutar: 0 }])));
});
test('purchase aggregate cannot exceed API amount cap', () => {
  assert.throws(() => purchasePayload(draft({ kalemler: [1, 2].map(() => ({ aciklama: 'Mal', tutar: '999999999999.99', dagilimlar: [] })) })));
});
test('purchase line count follows the server limit', () => {
  assert.throws(() => purchasePayload(draft({ kalemler: Array.from({ length: 101 }, () => ({ aciklama: 'Mal', tutar: '0', dagilimlar: [] })) })));
});
test('navigation prioritizes cash and excludes ERP screens for every role', () => {
  assert.equal(navigationFor('editor')[0][0], 'home');
  assert.deepEqual(navigationFor('alici').map(x => x[0]), ['purchases']);
  assert.deepEqual(navigationFor('viewer').map(x => x[0]), ['home', 'weekly', 'monthly', 'transactions', 'monthly-expenses', 'cards', 'loans']);
  for (const role of ['editor', 'viewer', 'alici']) for (const forbidden of ['accounts', 'debts', 'plan', 'tasks']) assert.equal(navigationFor(role).some(x => x[0] === forbidden), false);
});
test('weekly selection does not select a future expense period as the current week', () => {
  const weeks = ['2026-09-14', '2026-09-21', '2026-09-28'].map((start, i) => ({ donem: { start, end: ['2026-09-20', '2026-09-27', '2026-09-30'][i] } }));
  assert.equal(currentPeriod(weeks, '2026-09-23').donem.start, '2026-09-21');
  assert.equal(currentPeriod(weeks, '2026-09-27').donem.start, '2026-09-21');
  assert.equal(currentPeriod([], '2026-09-23'), undefined);
});
test('monthly cash summary includes pending payments once without inventing channel shares', () => {
  const channels = [{ gelen: 100, cariGiden: 20, sabitGider: 5, krediKarti: 10, ortakPay: 2.5 }, { gelen: 200, cariGiden: 30, sabitGider: 0, krediKarti: 0, ortakPay: 2.5 }];
  assert.deepEqual(monthlyTotals({ kanallar: channels, dagilimBekleyenTutar: 7.25 }), { incoming: 300, expenses: 77.25, result: 222.75 });
  assert.equal(channels[0].cariGiden, 20);
  assert.deepEqual(monthlyTotals({ kanallar: [{ gelen: 0.3, cariGiden: 0.1, sabitGider: 0.2, krediKarti: 0, ortakPay: 0 }] }), { incoming: 0.3, expenses: 0.3, result: 0 });
});
test('buyer capabilities hide finance, approval, payment and lock submitted drafts', () => {
  assert.deepEqual(permissions('alici', { durum: 'Taslak', kalan: 100 }), { finance: false, edit: true, send: true, approve: false, return: false, pay: false });
  assert.equal(permissions('alici', { durum: 'Incelemede', kalan: 100 }).edit, false);
  assert.equal(permissions('alici', { durum: 'Onaylandi', kalan: 100 }).edit, false);
});
test('editor must return approved purchase before editing and cannot pay zero balance', () => {
  assert.equal(permissions('editor', { durum: 'Onaylandi', kalan: 0 }).edit, false);
  assert.equal(permissions('editor', { durum: 'Onaylandi', kalan: 0 }).pay, false);
  assert.equal(permissions('editor', { durum: 'Onaylandi', kalan: 0 }).return, true);
  assert.equal(permissions('editor', { durum: 'Incelemede', kalan: 100 }).approve, true);
});
test('viewer has no purchasing mutation capabilities', () => {
  assert.deepEqual(permissions('viewer', { durum: 'Taslak', kalan: 100 }), { finance: false, edit: false, send: false, approve: false, return: false, pay: false });
});
test('Turkish search handles dotted and dotless I and combines state filters', () => {
  const purchases = [{ id: 1, tedarikci: 'IŞIK', alici: 'İpek', durum: 'Taslak', kalemler: [{ aciklama: 'Zeytinyağı' }] }, { id: 2, tedarikci: 'Başka', alici: 'Uğur', durum: 'Incelemede', kalemler: [] }];
  assert.deepEqual(filteredPurchases(purchases, 'ışık', '').map(p => p.id), [1]);
  assert.deepEqual(filteredPurchases(purchases, 'ipek', 'Taslak').map(p => p.id), [1]);
  assert.deepEqual(filteredPurchases(purchases, 'zeytinyağı', 'Incelemede'), []);
});
test('validation errors and authentication/rate-limit failures have useful messages', () => {
  assert.equal(errorMessage({ errors: { tutar: ['Tutar geçersiz'], tarih: ['Tarih gerekli'] } }, 400), 'Tutar geçersiz\nTarih gerekli');
  assert.equal(errorMessage({ hata: 'Sürüm değişti' }, 409), 'Sürüm değişti');
  assert.match(errorMessage(null, 401), /giriş/); assert.match(errorMessage(null, 403), /yetki/); assert.match(errorMessage(null, 429), /bekleyip/);
});
test('display formatting keeps Turkish money and date meaning', () => {
  assert.match(money(1234.56), /1\.234,56/);
  assert.match(money(-0.01), /-.*0,01/);
  assert.match(dateText('2026-09-23'), /23.*2026/);
  assert.equal(dateText(null), 'Belirlenmedi');
});


const monthNow = ui.today().slice(0, 7);
const [yearNow, monthNumberNow] = monthNow.split('-').map(Number);
const monthlyPath = `/api/aylik-giderler?yil=${yearNow}&ay=${monthNumberNow}`;
const monthlyRow = { sablonId: 7, sablonSurum: 3, ad: 'Dükkan kirası', tur: 'Kira', tutar: 100, planlananTarih: `${monthNow}-05`, dagilimTuru: 'Genel', dagilimlar: [], durum: 'Planlandi' };
const monthlyTemplate = { id: 7, surum: 3, ad: 'Dükkan kirası', tur: 'Kira', tutar: 100, odemeGunu: 5, dagilimTuru: 'Genel', dagilimlar: [], gecerliAy: `${monthNow}-01`, aktif: true };
const monthlyResponses = (rows = [monthlyRow]) => ({ [monthlyPath]: { yil: yearNow, ay: monthNumberNow, planlananToplam: 100, odenenToplam: 0, kayitlar: rows }, '/api/aylik-giderler/sablonlar': [monthlyTemplate], '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }, { id: 2, ad: 'B', aktif: true }, { id: 3, ad: 'Yeni', aktif: true }] });
const clickView = async (nodes, label) => { const node = nodes.get('#view').find(item => item.tag === 'button' && item.textContent === label); assert.ok(node, `${label} exists`); await node.listeners.click({ currentTarget: node }); await settle(); };

test('monthly plans stay read-only on load and viewers never receive payment or template controls', async () => {
  const { app, nodes, calls } = await openApp(false, { ...monthlyResponses(), '/api/auth/me': { rol: 'viewer' } });
  await app.navigate('monthly-expenses');
  assert.match(nodes.get('#view').textContent, /Şablon ve plan kasa bakiyesini değiştirmez/);
  assert.match(nodes.get('#view').textContent, /Yalnız genel kasa/);
  assert.equal(nodes.get('#page-actions').textContent, '');
  assert.equal(nodes.get('#view').find(row => row.tag === 'button' && row.textContent === 'Ödeme kaydet'), null);
  assert.equal(calls.some(call => call.method !== 'GET'), false);
  assert.throws(() => app.monthlyUi.paymentDialog(monthlyRow, yearNow, monthNumberNow), /editör/);
  const buyer = await openApp(false, { '/api/auth/me': { rol: 'alici' }, '/api/alis/kanallar': [] });
  await assert.rejects(buyer.app.navigate('monthly-expenses'), /erişiminiz yok/);
  assert.equal(buyer.calls.some(call => call.path.startsWith('/api/aylik-giderler')), false);
});

test('monthly template requires an explicit distribution and general-only sends no hidden channel allocations', async () => {
  const { app, nodes, calls } = await openApp(false, monthlyResponses());
  await app.monthlyUi.templateDialog();
  formField(nodes, 'ad').value = 'Kira'; formField(nodes, 'tutar').value = '100,01';
  await submitDialog(nodes);
  assert.equal(calls.some(call => call.method === 'POST'), false);
  assert.match(nodes.get('#modal-content').textContent, /hangi kasaya yazılacağını seçin/);
  formField(nodes, 'dagilimTuru').value = 'Genel'; await submitDialog(nodes);
  const save = calls.find(call => call.method === 'POST');
  assert.equal(save.path, '/api/aylik-giderler/sablonlar');
  assert.deepEqual(save.body.dagilimlar, []); assert.equal(save.body.dagilimTuru, 'Genel'); assert.equal(save.body.tutar, 100.01);
  assert.equal(save.body.gecerliAy, `${monthNow}-01`); assert.equal(save.body.surum, 0);
  assert.equal(calls.some(call => call.path.endsWith('/ode')), false);
});

test('equal monthly distribution captures only checked fixed channels and custom amounts must match exactly', async () => {
  const { app, nodes, calls } = await openApp(false, monthlyResponses());
  const selectMode = value => { const mode = formField(nodes, 'dagilimTuru'); mode.value = value; mode.listeners.change(); };
  const check = id => { const control = formField(nodes, `dagilim-kanal-${id}`); control.checked = true; control.listeners.change({ target: control }); };
  await app.monthlyUi.templateDialog(); formField(nodes, 'ad').value = 'Maaş'; formField(nodes, 'tutar').value = '100,01'; selectMode('Esit'); check(1); check(2); await submitDialog(nodes);
  const equal = calls.find(call => call.method === 'POST').body;
  assert.equal(equal.dagilimTuru, 'Esit'); assert.deepEqual(equal.dagilimlar, [{ kanalId: 1, tutar: 0 }, { kanalId: 2, tutar: 0 }]);
  await app.monthlyUi.templateDialog(); formField(nodes, 'ad').value = 'Fatura'; formField(nodes, 'tutar').value = '100,01'; selectMode('Ozel'); check(1); check(2);
  const setTotal = (id, value) => { const control = formField(nodes, `dagilim-tutar-${id}`); control.value = value; control.listeners.input({ target: control }); };
  setTotal(1, '60'); setTotal(2, '40'); await submitDialog(nodes);
  assert.equal(calls.filter(call => call.method === 'POST').length, 1); assert.match(nodes.get('#modal-content').textContent, /paylarının toplamı/);
  setTotal(2, '40,01'); await submitDialog(nodes);
  const custom = calls.filter(call => call.method === 'POST')[1].body;
  assert.deepEqual(custom.dagilimlar, [{ kanalId: 1, tutar: 60 }, { kanalId: 2, tutar: 40.01 }]);
});

test('monthly actual payment preserves server amount and identity on retry, then exposes only cancellation', async () => {
  let attempts = 0;
  const data = monthlyResponses(); const paid = { ...monthlyRow, durum: 'Odendi', odemeId: 11, odemeTarihi: ui.today() };
  const { app, nodes, calls, responses } = await openApp(false, { ...data, '/api/aylik-giderler/7/ode': () => { if (++attempts === 1) throw new Error('Lost response'); responses[monthlyPath] = { ...data[monthlyPath], odenenToplam: 100, kayitlar: [paid] }; return paid; } });
  await app.navigate('monthly-expenses'); await clickView(nodes, 'Ödeme kaydet');
  assert.equal(formField(nodes, 'tutar'), null); assert.match(nodes.get('#modal-content').textContent, /100,00/);
  await submitDialog(nodes); await submitDialog(nodes);
  const saves = calls.filter(call => call.path.endsWith('/ode')); assert.equal(saves.length, 2); assert.deepEqual(saves[0].body, saves[1].body);
  assert.equal(saves[0].body.surum, 3); assert.equal(saves[0].body.ay, monthNumberNow); assert.equal('tutar' in saves[0].body, false);
  assert.equal(nodes.get('#view').find(row => row.tag === 'button' && row.textContent === 'Ödeme kaydet'), null);
  assert.ok(nodes.get('#view').find(row => row.tag === 'button' && row.textContent === 'Ödemeyi iptal et'));
});

test('monthly payment cancellation keeps its explanation and idempotency key after a lost response', async () => {
  let attempts = 0;
  const { app, nodes, calls } = await openApp(false, { '/api/aylik-giderler/odemeler/11/iptal': () => { if (++attempts === 1) throw new Error('Lost response'); return { ...monthlyRow, durum: 'Iptal' }; } });
  app.monthlyUi.cancelDialog({ ...monthlyRow, odemeId: 11, odemeTarihi: ui.today() }); formField(nodes, 'aciklama').value = 'Yanlış ödeme günü'; await submitDialog(nodes); await submitDialog(nodes);
  const saves = calls.filter(call => call.path.endsWith('/iptal')); assert.equal(saves.length, 2); assert.deepEqual(saves[0].body, saves[1].body); assert.equal(saves[0].body.aciklama, 'Yanlış ödeme günü');
  assert.equal(calls.some(call => call.path.startsWith('/api/islemler')), false);
});

test('monthly total includes general-only expenses once and lock control explains the inclusive boundary', async () => {
  const report = { yil: yearNow, ay: monthNumberNow, genelGider: 30, dagilimBekleyenTutar: 5, kanallar: [{ kanal: 'A', gelen: 100, cariGiden: 10, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: 90 }] };
  assert.deepEqual(monthlyTotals(report), { incoming: 100, expenses: 45, result: 55 });
  const { app, nodes, calls } = await openApp(false, { [`/api/rapor/aylik?yil=${yearNow}&ay=${monthNumberNow}`]: report, '/api/ay-kilidi': { surum: 2, kilitliSonTarih: `${monthNow}-28`, gecmis: [] }, '/api/ay-kilidi/ac': { surum: 3, kilitliSonTarih: null, gecmis: [] } });
  await app.navigate('monthly');
  assert.match(nodes.get('#view').textContent, /Yalnız genel kasa gideri:.*30,00/);
  assert.match(nodes.get('#view').textContent, /dahil geçmiş kasa kayıtları kilitli/);
  await clickView(nodes, 'Bu ayı ve sonrasını aç'); assert.match(nodes.get('#modal-content').textContent, /sonraki bütün aylar değişikliğe açılacak/);
  formField(nodes, 'aciklama').value = 'Yanlış tarihi düzelteceğim'; await submitDialog(nodes);
  const write = calls.find(call => call.path.endsWith('/ac')); assert.equal(write.body.surum, 2); assert.equal(write.body.yil, yearNow); assert.equal(write.body.ay, monthNumberNow);
  const viewer = await openApp(false, { '/api/auth/me': { rol: 'viewer' } });
  const panel = await viewer.app.monthlyUi.lockPanel('2026-08', () => {}); assert.equal(panel.find(node => node.tag === 'button'), null);
});

test('current month cannot be locked in the UI while completed month has an explicit confirmation', async () => {
  const { app, nodes, calls } = await openApp(false, { '/api/ay-kilidi/kapat': { surum: 1, kilitliSonTarih: '2026-08-31', gecmis: [] } });
  const current = await app.monthlyUi.lockPanel(monthNow, () => {}); assert.equal(current.find(node => node.tag === 'button'), null);
  const past = await app.monthlyUi.lockPanel('2026-08', () => {}); const button = past.find(node => node.tag === 'button'); assert.ok(button); button.listeners.click();
  assert.match(nodes.get('#modal-content').textContent, /son günü dahil bütün geçmiş/);
  formField(nodes, 'aciklama').value = 'Ağustos kontrol edildi'; await submitDialog(nodes);
  assert.equal(calls.find(call => call.path.endsWith('/kapat')).body.ay, 8);
});

test('channel warnings use the server flag and stable ID without subtracting card debt from cash', async () => {
  const { nodes } = await openApp(false, {
    '/api/rapor/panel': { guncelKasa: 100, buHaftaSonucu: 0, buAySonucu: 0, kanallar: [{ kanalId: 1, kanal: 'Yeni ad', bakiye: 100 }, { kanalId: 2, kanal: 'B', bakiye: 0 }] },
    '/api/kasa-esikleri': [{ kanalId: 1, kanal: 'Eski ad', surum: 1, tutar: 120, etkin: true, bakiye: 100, esikAltinda: true }, { kanalId: 2, kanal: 'B', surum: 0, tutar: 0, etkin: false, bakiye: 0, esikAltinda: true }],
    '/api/takip/ozet?gun=30': { kartBorcu: 80, kalanKrediPlani: 0, olaylar: [], kanalKartBorclari: [{ kanalId: 1, kanal: 'Yeni ad', tutar: 80 }] }
  });
  const balances = nodes.get('#view').find(node => node.className === 'channel-balances');
  assert.match(balances.children[0].textContent, /Yeni ad.*100,00.*Alt sınırın altında.*120,00.*80,00/);
  assert.doesNotMatch(balances.children[1].textContent, /Alt sınırın altında/);
  assert.equal(nodes.get('#view').find(node => node.className === 'cash-total money').textContent, money(100));
});

test('channel threshold can be explicitly enabled at zero and negative threshold never sends a write', async () => {
  const row = { kanalId: 1, kanal: 'A', surum: 0, tutar: 0, etkin: false, bakiye: -5, esikAltinda: false };
  const { app, nodes, calls } = await openApp(false, { '/api/kasa-esikleri/1': { ...row, etkin: true, surum: 1 } });
  app.cashControlsUi.thresholdDialog(row); formField(nodes, 'tutar').value = '-1'; await submitDialog(nodes); assert.equal(calls.some(call => call.method === 'PUT'), false);
  formField(nodes, 'tutar').value = '0'; formField(nodes, 'etkin').checked = true; await submitDialog(nodes);
  const save = calls.find(call => call.path === '/api/kasa-esikleri/1'); assert.deepEqual(save.body, { surum: 0, tutar: 0, etkin: true });
});

test('actual balance comparison previews a signed value and stores only the snapshot with a stable retry key', async () => {
  let attempts = 0;
  const { app, nodes, calls } = await openApp(false, { '/api/kasa-kontrol/onizleme': { sistemBakiye: 123, gercekBakiye: -20, fark: -143, kontrolOzeti: 'digest1' }, '/api/kasa-kontrol': call => { if (call.method === 'GET') return []; if (++attempts === 1) throw new Error('Lost response'); return { id: 1 }; } });
  app.cashControlsUi.comparisonDialog(); formField(nodes, 'gercekBakiye').value = '-20'; formField(nodes, 'not').value = 'Banka ve nakit kontrolü'; await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /123,00.*-₺20,00.*-₺143,00/);
  assert.equal(calls.some(call => call.path === '/api/kasa-kontrol' && call.method === 'POST'), false);
  await submitDialog(nodes); await submitDialog(nodes);
  const saves = calls.filter(call => call.path === '/api/kasa-kontrol' && call.method === 'POST'); assert.equal(saves.length, 2); assert.deepEqual(saves[0].body, saves[1].body);
  assert.equal(saves[0].body.kontrolOzeti, 'digest1'); assert.equal(saves[0].body.gercekBakiye, -20);
  assert.equal(calls.some(call => ['/api/ayarlar', '/api/islemler', '/api/gelenler'].includes(call.path)), false);
});

test('changed cash requires a fresh comparison preview and a second explicit confirmation before save', async () => {
  let previews = 0; let saves = 0;
  const { app, nodes, calls } = await openApp(false, { '/api/kasa-kontrol/onizleme': () => ({ sistemBakiye: ++previews === 1 ? 123 : 150, gercekBakiye: 200, fark: previews === 1 ? 77 : 50, kontrolOzeti: `digest${previews}` }), '/api/kasa-kontrol': call => call.method === 'GET' ? [] : ++saves === 1 ? { $status: 409, hata: 'Bakiye değişti.' } : { id: 2 } });
  app.cashControlsUi.comparisonDialog(); formField(nodes, 'gercekBakiye').value = '200'; await submitDialog(nodes); await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /Kasa değişti/); await submitDialog(nodes);
  assert.equal(saves, 1); assert.match(nodes.get('#modal-content').textContent, /150,00.*200,00.*50,00/); await submitDialog(nodes);
  assert.equal(saves, 2); const writes = calls.filter(call => call.path === '/api/kasa-kontrol' && call.method === 'POST'); assert.equal(writes[1].body.kontrolOzeti, 'digest2'); assert.notEqual(writes[0].body.istekId, writes[1].body.istekId);
});

test('card fee uses a selected cut statement and server shares, then records only after visible confirmation', async () => {
  let attempts = 0;
  const preview = { kartId: 4, ekstreId: 8, tarih: ui.today(), tutar: 10.01, devredenBorc: 80, dagilimlar: [{ kanalId: 1, kanal: 'A', tutar: 6.01 }, { kanalId: 2, kanal: 'B', tutar: 4 }], dagilimOzeti: 'server-fee-digest' };
  const { app, nodes, calls } = await openApp(false, { '/api/takip/kartlar/4': sampleCard, '/api/takip/kartlar/4/masraf-onizleme': preview, '/api/takip/kartlar/4/masraflar': () => { if (++attempts === 1) throw new Error('Lost response'); return sampleCard; } });
  app.financeUi.feeDialog(sampleCard); formField(nodes, 'ekstreId').value = '8'; formField(nodes, 'tutar').value = '10,01'; formField(nodes, 'aciklama').value = 'Banka faiz tutarı'; await submitDialog(nodes);
  assert.equal(calls.some(call => call.path.endsWith('/masraflar')), false); assert.match(nodes.get('#modal-content').textContent, /A:.*6,01.*B:.*4,00/);
  await submitDialog(nodes); await submitDialog(nodes);
  const writes = calls.filter(call => call.path.endsWith('/masraflar')); assert.equal(writes.length, 2); assert.deepEqual(writes[0].body, writes[1].body); assert.equal(writes[0].body.dagilimOzeti, 'server-fee-digest');
  assert.equal(writes[0].body.istekId, calls.find(call => call.path.endsWith('/masraf-onizleme')).body.istekId);
  assert.equal(calls.some(call => call.path.endsWith('/odemeler')), false);
});

test('unknown fee allocation and cancelled asynchronous preview cannot create a fee or reopen a closed dialog', async () => {
  const { app, nodes, calls, responses } = await openApp(false, { '/api/takip/kartlar/4/masraf-onizleme': { $status: 409, hata: 'Kanalı belirsiz borç var.' } });
  const fill = () => { app.financeUi.feeDialog(sampleCard); formField(nodes, 'ekstreId').value = '8'; formField(nodes, 'tutar').value = '10'; formField(nodes, 'aciklama').value = 'Masraf'; };
  fill(); await submitDialog(nodes); assert.match(nodes.get('#modal-content').textContent, /Kanalı belirsiz borç var/); assert.equal(formField(nodes, 'tutar').value, '10');
  let resolve; responses['/api/takip/kartlar/4/masraf-onizleme'] = () => new Promise(done => { resolve = done; }); await submitDialog(nodes); await clickDialog(nodes, 'Vazgeç');
  resolve({ tutar: 10, devredenBorc: 100, dagilimlar: [], dagilimOzeti: 'late' }); await settle();
  assert.equal(nodes.get('#modal').open, false); assert.equal(calls.some(call => call.path.endsWith('/masraflar')), false);
});


test('monthly payment expenses open the monthly section instead of generic edit or delete', async () => {
  const expense = { id: 21, tarih: ui.today(), cari: 'Kira', kanal: 'Genel kasa', tip: 'SabitGider', tutarTl: 100, aylikGiderOdemeId: 11 };
  const { app, nodes } = await openApp(false, { ...monthlyResponses(), [`/api/islemler?baslangic=${monthNow}-01&bitis=${ui.today()}`]: [expense] });
  await app.navigate('transactions');
  assert.match(nodes.get('#view').textContent, /Aylık Giderler bölümünden yönetilir/);
  assert.equal(nodes.get('#view').find(row => row.tag === 'button' && ['Düzenle', 'Sil'].includes(row.textContent)), null);
  await clickView(nodes, 'Aylık Giderler’i aç'); assert.equal(nodes.get('#page-title').textContent, 'Aylık Giderler');
});

test('a monthly payment cannot be selected as an existing purchase expense', async () => {
  const { app, nodes } = await openApp(false, { '/api/kredikartlari': [], '/api/islemler': [{ id: 21, tarih: ui.today(), cari: 'Kira', tutarTl: 100, aylikGiderOdemeId: 11 }, { id: 22, tarih: ui.today(), cari: 'Mal', tutarTl: 100 }] });
  await app.paymentDialog({ id: 6, surum: 2, kalan: 100, durum: 'Onaylandi' });
  const existing = formField(nodes, 'mevcutIslemId');
  assert.equal(existing.find(row => row.tag === 'option' && row.value === '21'), null);
  assert.ok(existing.find(row => row.tag === 'option' && row.value === '22'));
});

const importRow = (overrides = {}) => ({ no: 1, sayfa: 1, kaynakSatir: '23.09.2026 Kira 100,00 TL', tarih: '2026-09-23', aciklama: 'Kira', tutar: 100, yon: 'Cikis', onerilenIslem: 'Gider', sinif: 'Hareket', paraBirimi: 'TRY', uyarilar: [], ...overrides });
const importDocument = (overrides = {}) => ({ id: 12, surum: 2, kaynak: 'Banka', banka: 'Isbank', hesapAdi: 'İşletme hesabı', kartId: null, dosyaAdi: 'hareket.pdf', yuklendi: '2026-09-23T10:00:00Z', uyarilar: [], satirlar: [importRow()], kayitlar: [], ...overrides });
const importResponses = (document = importDocument(), extra = {}) => ({ '/api/ekstre-aktar': [], '/api/ekstre-aktar/12': document, '/api/kanallar': [{ id: 1, ad: 'Mezat', aktif: true }, { id: 2, ad: 'Mağaza', aktif: true }], '/api/takip/kartlar': [sampleCard], ...extra });
const importPreview = (overrides = {}) => ({ onizlemeOzeti: 'preview-snapshot', kasaEtkisi: -100, satirlar: [{ satirNo: 1, tarih: '2026-09-23', aciklama: 'Kira', tutar: 100, islemTuru: 'Gider', kasaEtkisi: -100, dagilimlar: [], uyarilar: [] }], uyarilar: [], tekrarOnayGerekli: false, ...overrides });
const viewField = (nodes, name) => nodes.get('#view').find(node => node.attributes.name === name);
const importRowNode = (nodes, no = 1) => nodes.get('#view').find(node => node.tag === 'article' && node.find(child => child.attributes.name === `sec-${no}`));
async function chooseImportRow(nodes, no = 1, mode = 'Genel') {
  const checked = viewField(nodes, `sec-${no}`); checked.checked = true; await checked.listeners.change(); await settle();
  const allocation = importRowNode(nodes, no).find(node => node.attributes.name === 'dagilimTuru');
  if (mode != null) { allocation.value = mode; allocation.listeners.change(); }
}

test('PDF import is editor-only and never appears in the read-only cash app', async () => {
  for (const role of ['viewer', 'alici']) {
    const { app, nodes, calls } = await openApp(false, { '/api/auth/me': { rol: role }, '/api/alis/kanallar': [] });
    await assert.rejects(app.navigate('imports'), /erişiminiz yok/);
    assert.doesNotMatch(nodes.get('#navigation').textContent, /Ekstre \/ Hareket Yükle/);
    assert.equal(calls.some(call => call.path.startsWith('/api/ekstre-aktar')), false);
  }
  assert.equal(navigationFor('editor', { saltOkunur: true }).some(([key]) => key === 'imports'), false);
});

test('opening a parsed PDF leaves every movement unchecked, shows its source and writes nothing', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument({ satirlar: [importRow(), importRow({ no: 2, aciklama: '<img src=x onerror=alert(1)>', kaynakSatir: '<script>bad()</script>', sinif: 'Faiz' })] })));
  await app.navigate('imports', 12);
  assert.equal(viewField(nodes, 'sec-1').checked, false); assert.equal(viewField(nodes, 'sec-2').checked, false);
  assert.match(nodes.get('#view').textContent, /0 satır seçili/);
  assert.match(nodes.get('#view').textContent, /<img src=x onerror=alert\(1\)>/);
  assert.equal(nodes.get('#view').find(node => ['script', 'img'].includes(node.tag)), null);
  assert.equal(nodes.get('#view').find(node => node.tag === 'a').attributes.href, '/api/ekstre-aktar/12/dosya');
  assert.equal(calls.some(call => call.method === 'POST'), false);
  await clickView(nodes, 'Seçilenleri önizle'); assert.equal(calls.some(call => call.method === 'POST'), false);
});

test('PDF upload exposes all six banks, sends only selected source metadata and does not post finance', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar/yukle': importDocument() }));
  await app.navigate('imports'); await clickView(nodes, 'Kart ekstresi / hesap hareketi seç');
  assert.match(formField(nodes, 'banka').textContent, /VakıfBank.*Akbank.*QNB.*İş Bankası.*Garanti BBVA.*DenizBank/);
  const source = formField(nodes, 'kaynak'); source.value = 'Banka'; source.listeners.change();
  assert.equal(formField(nodes, 'kartId').disabled, true);
  formField(nodes, 'banka').value = 'QNB'; formField(nodes, 'hesapAdi').value = '  İşletme  ';
  formField(nodes, 'dosya').files = [{ name: 'bank.pdf', type: 'application/pdf', size: 1200 }];
  await submitDialog(nodes);
  const request = calls.find(call => call.path.endsWith('/yukle'));
  assert.equal(request.body.kaynak, 'Banka'); assert.equal(request.body.banka, 'QNB'); assert.equal(request.body.hesapAdi, 'İşletme'); assert.equal(request.body.kartId, undefined);
  assert.equal(calls.filter(call => call.method === 'POST').length, 1);
  assert.equal(viewField(nodes, 'sec-1').checked, false);
});

test('PDF upload refuses wrong type and oversized files before calling the server', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses());
  await app.navigate('imports'); await clickView(nodes, 'Kart ekstresi / hesap hareketi seç');
  formField(nodes, 'banka').value = 'Akbank'; formField(nodes, 'kartId').value = '4';
  for (const file of [{ name: 'statement.pdf', type: 'application/pdf', size: 10 * 1024 * 1024 + 1 }, { name: 'statement.html', type: 'text/html', size: 100 }]) { formField(nodes, 'dosya').files = [file]; await submitDialog(nodes); }
  assert.equal(calls.some(call => call.path.endsWith('/yukle')), false);
});

test('PDF selected rows require a server impact preview and a separate save action', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument({ satirlar: [importRow(), importRow({ no: 2, tutar: 200 })] }), { '/api/ekstre-aktar/12/onizleme': importPreview(), '/api/ekstre-aktar/12/kaydet': importDocument() }));
  await app.navigate('imports', 12); await chooseImportRow(nodes);
  await clickView(nodes, 'Seçilenleri önizle');
  const request = calls.find(call => call.path.endsWith('/onizleme'));
  assert.equal(request.body.satirlar.length, 1); assert.equal(request.body.satirlar[0].satirNo, 1);
  assert.equal(request.body.satirlar[0].dagilimTuru, 'Genel'); assert.deepEqual(request.body.satirlar[0].dagilimlar, []);
  assert.equal(calls.some(call => call.path.endsWith('/kaydet')), false);
  assert.match(nodes.get('#view').textContent, /Genel kasa değişimi.*-₺100,00/);
  await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(calls.find(call => call.path.endsWith('/kaydet')).body.onizlemeOzeti, 'preview-snapshot');
});

test('PDF custom allocation requires exact cents and equal shares contain only explicitly chosen channels', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument({ kaynak: 'Kart', kartId: 4, satirlar: [importRow({ onerilenIslem: 'KartHarcama', sinif: 'Faiz' })] }), { '/api/ekstre-aktar/12/onizleme': importPreview({ kasaEtkisi: 0 }) }));
  await app.navigate('imports', 12); await chooseImportRow(nodes, 1, 'Ozel');
  const row = importRowNode(nodes); const first = row.find(node => node.attributes.name === 'pay-kanal-1'); first.checked = true; first.listeners.change(); row.find(node => node.attributes.name === 'pay-tutar-1').value = '99,99';
  await clickView(nodes, 'Seçilenleri önizle'); assert.equal(calls.some(call => call.path.endsWith('/onizleme')), false);
  const mode = row.find(node => node.attributes.name === 'dagilimTuru'); mode.value = 'Esit'; mode.listeners.change();
  await clickView(nodes, 'Seçilenleri önizle');
  const saved = calls.find(call => call.path.endsWith('/onizleme')).body.satirlar[0];
  assert.equal(saved.islemTuru, 'KartHarcama'); assert.equal(saved.krediKartiId, 4); assert.deepEqual(saved.dagilimlar, [{ kanalId: 1, tutar: 0 }]);
});

test('PDF duplicate and ambiguity warnings require explicit confirmation before commit', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar/12/onizleme': importPreview({ tekrarOnayGerekli: true, uyarilar: ['Benzer kayıt bulundu.'] }), '/api/ekstre-aktar/12/kaydet': importDocument() }));
  await app.navigate('imports', 12); await chooseImportRow(nodes); await clickView(nodes, 'Seçilenleri önizle');
  await clickView(nodes, 'Onayla ve kaydet'); assert.equal(calls.some(call => call.path.endsWith('/kaydet')), false);
  viewField(nodes, 'tekrarOnay').checked = true; await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(calls.find(call => call.path.endsWith('/kaydet')).body.tekrarOnay, true);
});

test('PDF silent field change cannot reuse an old financial preview', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar/12/onizleme': importPreview() }));
  await app.navigate('imports', 12); await chooseImportRow(nodes); await clickView(nodes, 'Seçilenleri önizle');
  viewField(nodes, 'tutar-1').value = '101'; await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(calls.some(call => call.path.endsWith('/kaydet')), false);
  assert.match(nodes.get('#notifications').textContent, /Yeniden önizleyin/);
});

test('PDF uncertain save retries retain the idempotency key and a conflict requires new preview', async () => {
  let saves = 0;
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar/12/onizleme': importPreview(), '/api/ekstre-aktar/12/kaydet': () => { saves++; if (saves < 3) throw new Error('offline'); return { $status: 409, hata: 'Kayıt değişti' }; } }));
  await app.navigate('imports', 12); await chooseImportRow(nodes); await clickView(nodes, 'Seçilenleri önizle');
  await clickView(nodes, 'Onayla ve kaydet'); await clickView(nodes, 'Onayla ve kaydet');
  const attempts = calls.filter(call => call.path.endsWith('/kaydet')); assert.equal(attempts[0].body.istekId, attempts[1].body.istekId);
  await clickView(nodes, 'Onayla ve kaydet'); await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(saves, 3); assert.equal(viewField(nodes, 'sec-1').checked, true);
});

test('PDF foreign-currency and already imported rows stay disabled and cannot be forged into preview', async () => {
  const document = importDocument({ satirlar: [importRow({ paraBirimi: 'EUR' }), importRow({ no: 2 })], kayitlar: [{ id: 9, satirNo: 2, tarih: '2026-09-23', aciklama: 'Önceki', tutar: 100, islemTuru: 'Gider', dagilimTuru: 'Genel', dagilimlar: [], iptal: false }] });
  const { app, nodes, calls } = await openApp(false, importResponses(document));
  await app.navigate('imports', 12); assert.equal(viewField(nodes, 'sec-1').disabled, true); assert.equal(viewField(nodes, 'sec-2').disabled, true);
  viewField(nodes, 'sec-1').checked = true; await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(calls.some(call => call.path.endsWith('/onizleme')), false);
});

test('PDF bank card payment requires the intended card and automatically sourced allocation', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument({ satirlar: [importRow({ onerilenIslem: 'KartOdemesi' })] }), { '/api/ekstre-aktar/12/onizleme': importPreview(), '/api/takip/kartlar': [{ ...sampleCard, aktif: false }] }));
  await app.navigate('imports', 12); await chooseImportRow(nodes, 1, null); await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(calls.some(call => call.path.endsWith('/onizleme')), false);
  assert.match(viewField(nodes, 'kart-1').textContent, /Yeni kullanıma kapalı/);
  viewField(nodes, 'kart-1').value = '4'; await clickView(nodes, 'Seçilenleri önizle');
  const row = calls.find(call => call.path.endsWith('/onizleme')).body.satirlar[0]; assert.equal(row.krediKartiId, 4); assert.equal(row.dagilimTuru, 'Otomatik'); assert.deepEqual(row.dagilimlar, []);
});

test('PDF card refund requires its original charge and preserves source binding', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument({ kaynak: 'Kart', kartId: 4, satirlar: [importRow({ onerilenIslem: 'KartIade', yon: 'Giris' })] }), { '/api/takip/kartlar/4': { ...sampleCard, harcamalar: [{ id: 18, tutar: 100, tarih: '2026-09-23', aciklama: 'Mal', iptal: false }, { id: 19, tutar: 100, iptal: true }, { id: 20, tutar: -20, iptal: false }] }, '/api/ekstre-aktar/12/onizleme': importPreview() }));
  await app.navigate('imports', 12); await chooseImportRow(nodes, 1, null); await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(calls.some(call => call.path.endsWith('/onizleme')), false);
  const source = viewField(nodes, 'iade-kaynak-1'); assert.ok(source.find(node => node.value === '18')); assert.equal(source.find(node => node.value === '19'), null);
  source.value = '18'; await clickView(nodes, 'Seçilenleri önizle');
  const row = calls.find(call => call.path.endsWith('/onizleme')).body.satirlar[0]; assert.equal(row.kaynakHarcamaId, 18); assert.equal(row.krediKartiId, 4); assert.equal(row.dagilimTuru, 'Otomatik');
});

test('leaving PDF import while preview is pending cannot display or save its late result', async () => {
  let release;
  const pending = new Promise(resolve => { release = resolve; });
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar/12/onizleme': () => pending }));
  await app.navigate('imports', 12); await chooseImportRow(nodes);
  const button = nodes.get('#view').find(node => node.tag === 'button' && node.textContent === 'Seçilenleri önizle');
  const work = button.listeners.click({ currentTarget: button }); await settle(); await app.navigate('home'); release(importPreview()); await work; await settle();
  assert.doesNotMatch(nodes.get('#view').textContent, /Onayla ve kaydet/); assert.equal(calls.some(call => call.path.endsWith('/kaydet')), false);
});

test('clearing all PDF selections removes every selected row and invalidates preview', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar/12/onizleme': importPreview() }));
  await app.navigate('imports', 12); await chooseImportRow(nodes); await clickView(nodes, 'Seçilenleri önizle');
  await clickView(nodes, 'Seçimleri temizle'); await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(viewField(nodes, 'sec-1').checked, false); assert.match(nodes.get('#view').textContent, /0 satır seçili/); assert.equal(calls.some(call => call.path.endsWith('/kaydet')), false);
});

test('PDF history cancellation requires an explanation and retains its retry identity', async () => {
  const document = importDocument({ kayitlar: [{ id: 9, satirNo: 1, tarih: '2026-09-23', aciklama: 'Kira', tutar: 100, islemTuru: 'Gider', dagilimTuru: 'Genel', dagilimlar: [], iptal: false }] });
  const { app, nodes, calls } = await openApp(false, importResponses(document, { '/api/ekstre-aktar/12/kayitlar/9/iptal': new Error('offline') }));
  await app.navigate('imports', 12); await clickView(nodes, 'Kaydı iptal et'); await submitDialog(nodes);
  assert.equal(calls.some(call => call.path.endsWith('/iptal')), false);
  formField(nodes, 'aciklama').value = 'Yanlış satır seçildi'; await submitDialog(nodes); await submitDialog(nodes);
  const requests = calls.filter(call => call.path.endsWith('/iptal')); assert.equal(requests.length, 2); assert.equal(requests[0].body.istekId, requests[1].body.istekId);
});

test('imported cash expenses have a source link and cannot be selected as purchase payments', async () => {
  const expenses = [{ id: 21, tarih: ui.today(), cari: 'PDF Kira', tutarTl: 100, ekstreKayitId: 8 }, { id: 22, tarih: ui.today(), cari: 'Mal', tutarTl: 100 }];
  const { app, nodes } = await openApp(false, importResponses(importDocument(), { '/api/kredikartlari': [], '/api/islemler': expenses, [`/api/islemler?baslangic=${ui.today().slice(0, 8)}01&bitis=${ui.today()}`]: expenses }));
  await app.navigate('transactions'); assert.match(nodes.get('#view').textContent, /Ekstre \/ Hareket Yükle bölümünden yönetilir/);
  const row = nodes.get('#view').find(node => node.tag === 'tr' && node.textContent.includes('PDF Kira')); assert.doesNotMatch(row.textContent, /Düzenle|Sil/);
  await app.paymentDialog({ id: 6, surum: 2, kalan: 100, durum: 'Onaylandi' }); const existing = formField(nodes, 'mevcutIslemId'); assert.equal(existing.find(row => row.tag === 'option' && row.value === '21'), null);
});

test('imported card charges and payments expose source navigation instead of generic cancellation', async () => {
  const card = { ...sampleCard, harcamalar: [{ id: 10, tarih: '2026-09-23', aciklama: 'PDF faiz', tutar: 100, taksitSayisi: 1, dagilimlar: [], ekstreKayitId: 9 }], odemeler: [{ id: 11, tarih: '2026-09-23', tutar: 50, kasaEtkisi: 50, dagilimlar: [], ekstreKayitId: 10 }] };
  const { app, nodes } = await openApp(false, { '/api/takip/kartlar/4': card });
  await app.navigate('cards', 4); assert.match(nodes.get('#view').textContent, /PDF yüklemesinden kaydedildi/); assert.doesNotMatch(nodes.get('#view').textContent, /İptal et|Ödemeyi iptal et/);
});

test('monthly reports add general-only imported income exactly once without inventing channel income', () => {
  assert.deepEqual(monthlyTotals({ genelGelir: 123.45, genelGider: 10, kanallar: [{ gelen: 50, cariGiden: 1, sabitGider: 2, krediKarti: 3, ortakPay: 4 }] }), { incoming: 173.45, expenses: 20, result: 153.45 });
});

test('PDF history reaches older than fifty documents using a stable descending cursor', async () => {
  const latest = Array.from({ length: 50 }, (_, index) => ({ ...importDocument(), id: 100 - index, dosyaAdi: `Belge-${100 - index}.pdf`, satirSayisi: 1, kayitSayisi: 0 }));
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar': latest, '/api/ekstre-aktar?beforeId=51': [{ ...importDocument(), id: 12, satirSayisi: 1, kayitSayisi: 0 }] }));
  await app.navigate('imports'); await clickView(nodes, 'Daha eski belgeler');
  assert.ok(calls.some(call => call.path === '/api/ekstre-aktar?beforeId=51'));
  assert.match(nodes.get('#view').textContent, /hareket.pdf/); assert.doesNotMatch(nodes.get('#view').textContent, /Daha eski belgeler/);
  await clickView(nodes, 'Aç ve incele'); assert.ok(viewField(nodes, 'sec-1'));
  await app.navigate('imports', { beforeId: 51 }); await clickView(nodes, 'En yeni belgelere dön'); assert.match(nodes.get('#view').textContent, /Belge-100.pdf/);
});

test('imported expense opens its exact parent PDF even when absent from recent history', async () => {
  const expenses = [{ id: 21, tarih: ui.today(), cari: 'PDF kira', tutarTl: 100, ekstreKayitId: 807 }];
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { [`/api/islemler?baslangic=${ui.today().slice(0, 8)}01&bitis=${ui.today()}`]: expenses, '/api/ekstre-aktar/kayitlar/807': importDocument({ dosyaAdi: 'Eski-kira.pdf' }) }));
  await app.navigate('transactions'); await clickView(nodes, 'Kaynak belgeyi aç');
  assert.ok(calls.some(call => call.path === '/api/ekstre-aktar/kayitlar/807')); assert.equal(calls.some(call => call.path === '/api/ekstre-aktar'), false);
  assert.match(nodes.get('#view').textContent, /Eski-kira.pdf/); assert.equal(viewField(nodes, 'sec-1').checked, false);
});

test('imported card payment opens the record source instead of unrelated recent PDFs', async () => {
  const card = { ...sampleCard, odemeler: [{ id: 19, tarih: ui.today(), tutar: 100, kasaEtkisi: 100, dagilimlar: [], ekstreKayitId: 909 }] };
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/takip/kartlar/4': card, '/api/ekstre-aktar/kayitlar/909': importDocument({ dosyaAdi: 'Kart-odeme.pdf' }) }));
  await app.navigate('cards', 4); await clickView(nodes, 'Kaynak belgeyi aç');
  assert.ok(calls.some(call => call.path === '/api/ekstre-aktar/kayitlar/909')); assert.match(nodes.get('#view').textContent, /Kart-odeme.pdf/);
});

test('late old-history response cannot overwrite a newly opened PDF', async () => {
  let release; const delayed = new Promise(resolve => { release = resolve; });
  const { app, nodes } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar?beforeId=51': () => delayed }));
  const older = app.navigate('imports', { beforeId: 51 }); await settle(); await app.navigate('imports', 12); release([]); await older;
  assert.ok(viewField(nodes, 'sec-1')); assert.doesNotMatch(nodes.get('#view').textContent, /Daha eski belge yok/);
});
