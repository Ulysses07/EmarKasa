import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';

// Değişiklik geçmişi görünümü (denetim-ui.js): tarayıcı modülü paket bağımlılığı olmadan olduğu gibi yüklenir.
const source = await readFile(new URL('../Kasa.Api/wwwroot/denetim-ui.js', import.meta.url), 'utf8');
const ui = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('denetim-ui.js parses as an explicit ES module', () => {
  const result = spawnSync(process.execPath, ['--input-type=module', '--check'], { input: source, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
});

test('history query carries only filled filters and rejects an id without its entity', () => {
  assert.equal(ui.historyQuery(), '/api/denetim?adet=50');
  assert.equal(ui.historyQuery({ varlik: 'Islem', varlikId: ' 812 ', kilitAcmaOlayiId: '7', oncekiId: 90 }), '/api/denetim?varlik=Islem&varlikId=812&kilitAcmaOlayiId=7&oncekiId=90&adet=50');
  assert.throws(() => ui.historyQuery({ varlikId: '812' }), /kayıt türünü de seçin/);
  assert.throws(() => ui.historyQuery({ kilitAcmaOlayiId: '-1' }), /pozitif/);
});

test('change lines show before and after for edits, one side for inserts and deletes', () => {
  assert.deepEqual(ui.changeLines({ oncekiJson: '{"TutarTl":48000}', yeniJson: '{"TutarTl":44000}' }), ['TutarTl: 48000 → 44000']);
  assert.deepEqual(ui.changeLines({ oncekiJson: null, yeniJson: '{"Cari":"Toptancı","Iptal":false,"Not":null}' }), ['Cari: Toptancı', 'Iptal: Hayır', 'Not: —']);
  assert.deepEqual(ui.changeLines({ oncekiJson: '{"TutarTl":12500}', yeniJson: null }), ['TutarTl: 12500 (önceki)']);
  assert.deepEqual(ui.changeLines({ oncekiJson: '{"Paylar":[{"KanalId":1}]}', yeniJson: 'bozuk' }), ['Paylar: [{"KanalId":1}] (önceki)', 'deger: bozuk']);
});

test('actor and record text name the role, buyer id, client IP and lock window', () => {
  assert.equal(ui.actorText({ aktorRol: 'alici', aktorId: 4, istemciIp: '203.0.113.9' }), 'Alıcı #4 · 203.0.113.9');
  assert.equal(ui.actorText({ aktorRol: 'sistem', aktorId: null, istemciIp: null }), 'Sistem');
  assert.equal(ui.recordText({ varlik: 'TakipKartOdeme', varlikId: '15', kilitAcmaOlayiId: 3 }), 'Kart ödemesi #15 · ay kilidi açılışı #3');
  assert.equal(ui.recordText({ varlik: 'Yeni', varlikId: null, kilitAcmaOlayiId: null }), 'Yeni');
});

test('history dialog loads the filtered page, renders reasons as text and pages older rows', async () => {
  const created = [];
  const h = (tag, props = {}, ...children) => {
    const node = { tag, props, children: children.flat(Infinity).filter(c => c != null && c !== false), hidden: false, textContent: '',
      replaceChildren(...items) { this.children = items; }, querySelector: () => submit };
    if (props.hidden) node.hidden = true;
    created.push(node); return node;
  };
  let submit = null;
  const calls = [];
  const page = (from, count) => Array.from({ length: count }, (_, i) => ({ id: from - i, zaman: '2026-09-25T09:00:00Z', aktorRol: 'editor', aktorId: null, istemciIp: null,
    tur: 'Degistir', varlik: 'TakipKartOdeme', varlikId: '15', oncekiJson: '{"Iptal":false}', yeniJson: '{"Iptal":true}', gerekce: '<b>bankadan iade geldi</b>', kilitAcmaOlayiId: null }));
  const api = async path => { calls.push(path); return calls.length === 1 ? page(200, 50) : page(150, 2); };
  let opened = null;
  const denetim = ui.createDenetimUi({
    api, h, button: (label, action) => h('button', { onclick: action }, label), input: (name, value) => { const n = h('input', { name }); n.value = value; return n; },
    field: (label, control) => h('label', {}, label, control), select: (name, choices, value) => { const n = h('select', { name }); n.value = value; return n; },
    help: text => h('p', {}, text), table: (headers, rows) => { const n = { tag: 'table', headers, rows }; created.push(n); return n; }, openModal: (title, content) => { opened = { title, content }; },
    run: async (_control, work) => work(),
  });
  denetim.open({ varlik: 'TakipKartOdeme', varlikId: '15' });
  assert.equal(opened.title, 'Değişiklik geçmişi');
  const form = created.find(n => n.tag === 'form');
  submit = created.find(n => n.tag === 'button' && n.props.type === 'submit');
  await form.props.onsubmit({ preventDefault() {} });
  assert.deepEqual(calls, ['/api/denetim?varlik=TakipKartOdeme&varlikId=15&adet=50']);
  const tables = () => created.filter(n => n.tag === 'table');
  const rows = tables().at(-1).rows;
  assert.equal(rows.length, 50);
  assert.equal(rows[0][5], '<b>bankadan iade geldi</b>'); // metin olarak verilir; h() textContent ile yazar
  const more = created.find(n => n.tag === 'button' && n.children[0] === 'Daha eski kayıtlar');
  await more.props.onclick({ currentTarget: more });
  assert.equal(calls[1], '/api/denetim?varlik=TakipKartOdeme&varlikId=15&oncekiId=151&adet=50');
  assert.equal(tables().at(-1).rows.length, 52);
});
