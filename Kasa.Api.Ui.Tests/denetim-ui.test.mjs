import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { Element, sessizOrtam, webModulu } from './tarayici.mjs';

// Değişiklik geçmişi görünümü (denetim-ui.js): tarayıcı modülü paket bağımlılığı olmadan, Node'un ES modül yükleyicisiyle
// içe aktardığı ui-dom.js ve ui-shell.js ile birlikte olduğu gibi yüklenir (tarayici.mjs).
const source = await readFile(new URL('../Kasa.Api/wwwroot/denetim-ui.js', import.meta.url), 'utf8');
const ui = await webModulu('denetim-ui.js', sessizOrtam());

test('denetim-ui.js parses as an explicit ES module', () => {
  const result = spawnSync(process.execPath, ['--input-type=module', '--check'], { input: source, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
});

test('history query carries only filled filters and rejects an id without its entity', () => {
  assert.equal(ui.historyQuery(), '/api/denetim?adet=50');
  assert.equal(
    ui.historyQuery({ varlik: 'Islem', varlikId: ' 812 ', kilitAcmaOlayiId: '7', oncekiId: 90 }),
    '/api/denetim?varlik=Islem&varlikId=812&kilitAcmaOlayiId=7&oncekiId=90&adet=50'
  );
  assert.throws(() => ui.historyQuery({ varlikId: '812' }), /kayıt türünü de seçin/);
  assert.throws(() => ui.historyQuery({ kilitAcmaOlayiId: '-1' }), /pozitif/);
});

test('change lines show before and after for edits, one side for inserts and deletes', () => {
  assert.deepEqual(ui.changeLines({ oncekiJson: '{"TutarTl":48000}', yeniJson: '{"TutarTl":44000}' }), ['TutarTl: 48000 → 44000']);
  assert.deepEqual(ui.changeLines({ oncekiJson: null, yeniJson: '{"Cari":"Toptancı","Iptal":false,"Not":null}' }), [
    'Cari: Toptancı',
    'Iptal: Hayır',
    'Not: —',
  ]);
  assert.deepEqual(ui.changeLines({ oncekiJson: '{"TutarTl":12500}', yeniJson: null }), ['TutarTl: 12500 (önceki)']);
  assert.deepEqual(ui.changeLines({ oncekiJson: '{"Paylar":[{"KanalId":1}]}', yeniJson: 'bozuk' }), [
    'Paylar: [{"KanalId":1}] (önceki)',
    'deger: bozuk',
  ]);
});

test('actor and record text name the role, buyer id, client IP and lock window', () => {
  assert.equal(ui.actorText({ aktorRol: 'alici', aktorId: 4, istemciIp: '203.0.113.9' }), 'Alıcı #4 · 203.0.113.9');
  assert.equal(ui.actorText({ aktorRol: 'sistem', aktorId: null, istemciIp: null }), 'Sistem');
  assert.equal(ui.recordText({ varlik: 'TakipKartOdeme', varlikId: '15', kilitAcmaOlayiId: 3 }), 'Kart ödemesi #15 · ay kilidi açılışı #3');
  assert.equal(ui.recordText({ varlik: 'Yeni', varlikId: null, kilitAcmaOlayiId: null }), 'Yeni');
});

test('a link broken by a deleted parent record has its own readable type', () => {
  assert.equal(ui.TYPE_LABELS.BagKoptu, 'Bağ koptu (bağlı kayıt silindi)');
  assert.deepEqual(ui.changeLines({ oncekiJson: '{"OdemeId":15}', yeniJson: '{"OdemeId":null}' }), ['OdemeId: 15 → —']);
});

test('history dialog loads the filtered page, renders reasons as text and pages older rows', async () => {
  const nodes = new Map();
  const calls = [];
  const page = (from, count) =>
    Array.from({ length: count }, (_, i) => ({
      id: from - i,
      zaman: '2026-09-25T09:00:00Z',
      aktorRol: 'editor',
      aktorId: null,
      istemciIp: null,
      tur: 'Degistir',
      varlik: 'TakipKartOdeme',
      varlikId: '15',
      oncekiJson: '{"Iptal":false}',
      yeniJson: '{"Iptal":true}',
      gerekce: '<b>bankadan iade geldi</b>',
      kilitAcmaOlayiId: null,
    }));
  // Gerçek h/table/openModal/run/api: sahte belge ve sahte fetch üzerinde (çalışma ayarı yok → tam sürüm).
  const denetim = await webModulu('denetim-ui.js', {
    ...sessizOrtam(),
    document: {
      querySelector: key => {
        if (!nodes.has(key)) nodes.set(key, Object.assign(new Element(), { root: true }));
        return nodes.get(key);
      },
      createElement: tag => new Element(tag),
      createTextNode: text => String(text),
    },
    fetch: async path => {
      if (path === '/kasa-runtime.json') return { ok: false, status: 404 };
      calls.push(path);
      const rows = calls.length === 1 ? page(200, 50) : page(150, 2);
      return { ok: true, status: 200, text: async () => JSON.stringify(rows) };
    },
  });
  denetim.createDenetimUi().open({ varlik: 'TakipKartOdeme', varlikId: '15' });
  assert.equal(nodes.get('#modal-title').textContent, 'Değişiklik geçmişi');
  const content = nodes.get('#modal-content');
  const form = content.find(n => n.tag === 'form');
  await form.listeners.submit({ preventDefault() {} });
  assert.deepEqual(calls, ['/api/denetim?varlik=TakipKartOdeme&varlikId=15&adet=50']);
  // Sonuç bölgesindeki güncel tablo: kaydırılabilir tablo bölgesi (table()) ve gövde satırları.
  const region = () => content.find(n => n.className === 'table-wrap');
  const rows = () => region().find(n => n.tag === 'tbody').children;
  assert.equal(rows().length, 50);
  assert.equal(region().attributes['aria-label'], 'Değişiklik kayıtları'); // kaydırılabilir tablo bölgesinin adı
  assert.equal(rows()[0].children[5].textContent, '<b>bankadan iade geldi</b>'); // metin olarak verilir; h() textContent ile yazar
  assert.equal(
    content.find(n => n.tag === 'b'),
    null
  );
  const more = content.find(n => n.tag === 'button' && n.textContent === 'Daha eski kayıtlar');
  await more.listeners.click({ currentTarget: more });
  assert.equal(calls[1], '/api/denetim?varlik=TakipKartOdeme&varlikId=15&oncekiId=151&adet=50');
  assert.equal(rows().length, 52);
});
