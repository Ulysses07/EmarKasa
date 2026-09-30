import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { runInNewContext } from 'node:vm';
import { Element, TestFormData, TARAYICI_DISI, sessizOrtam, webModulu } from './tarayici.mjs';

// Tarayıcı modülleri paket bağımlılığı olmadan, Node'un ES modül yükleyicisiyle olduğu gibi yüklenir (tarayici.mjs).
const ui = await import(new URL('../Kasa.Api/wwwroot/ui-core.js', import.meta.url));
const cashControls = await webModulu('cash-controls-ui.js', sessizOrtam());
const pushModule = await import(new URL('../Kasa.Api/wwwroot/push-client.js', import.meta.url));
const {
  cents,
  amount,
  money,
  dateText,
  permissions,
  filteredPurchases,
  purchasePayload,
  errorMessage,
  fieldErrors,
  sessionExpired,
  viewerPasswordError,
  VIEWER_PASSWORD_MESSAGE,
  VIEWER_PASSWORD_SHORT_MESSAGE,
  MAX_CENTS,
  childValues,
  logoutAndClear,
  navigationFor,
  currentPeriod,
  monthlyTotals,
  loadRuntime,
  runtimeRequestAllowed,
  cashEditingAllowed,
  incomeSelection,
} = ui;

// Exercise the real startup and render functions with an inert DOM and deterministic API data.
// timers: verilirse app.js'in kurduğu zamanlayıcılar ({ fn, ms }) buraya yazılır; testte elle çalıştırılır.
async function openApp(readOnly, extraResponses = {}, pushEnvironment = null, timers = null) {
  const nodes = new Map();
  const requests = [];
  // index.html'deki gibi #modal-content, <dialog id="modal"> içindedir; hata görünürlüğü diyaloğun açık olmasına bakar.
  const root = node => Object.assign(node, { root: true });
  const modalNode = root(new Element('dialog'));
  const modalContent = new Element();
  modalNode.append(modalContent);
  nodes.set('#modal', modalNode);
  nodes.set('#modal-content', modalContent);
  const responses = {
    '/kasa-runtime.json': { saltOkunur: readOnly, surum: 'test' },
    '/api/auth/me': { rol: 'editor' },
    '/api/rapor/panel': { guncelKasa: 123, buHaftaSonucu: 20, buAySonucu: 50, kanallar: [{ kanal: 'Mağaza', bakiye: 123 }] },
    '/api/alis': [],
    '/api/kasa-esikleri': [],
    '/api/kasa-kontrol': [],
    '/api/ay-kilidi': { surum: 0, kilitliSonTarih: null, gecmis: [] },
    '/api/surum': { surum: '2.2.0' },
    '/api/takip/ozet?gun=30': { kartBorcu: 0, kalanKrediPlani: 0, olaylar: [] },
    '/api/islemler/benzerlik': [],
    '/api/rapor/ana-sayfa?gun=30': call => homeSummary(call),
    '/api/alis/baglanabilir-giderler': () => linkableExpenses(),
    '/api/alis/inceleme-ozeti?adet=4': { sayi: 0, ogeler: [] },
    ...extraResponses,
  };
  // Sunucu sözleşmesi: ana sayfa özeti ayrı uçların yanıtıyla birebir aynıdır. Varsayılan yanıt, testin o anki panel, eşik ve
  // takip özeti yanıtlarından kurulur; parçalardan biri hata verirse tek istek de o hatayı verir.
  const homeSummary = async call => {
    const part = async key => {
      const value = responses[key];
      if (value instanceof Error) throw value;
      return typeof value === 'function' ? await value(call) : value;
    };
    const panel = await part('/api/rapor/panel'),
      kasaEsikleri = await part('/api/kasa-esikleri'),
      takipOzeti = await part('/api/takip/ozet?gun=30');
    return [panel, kasaEsikleri, takipOzeti].find(value => value?.$status) || { panel, kasaEsikleri, takipOzeti };
  };
  // Alış ödemesinin bağlanabilir gider sayfası (webui-6): açıkça verilmezse testin gider listesinden (/api/islemler) süzülmeden
  // tek sayfa kurulur. Gider listesi veren testler yeni uçla da çalışır; istemcinin savunma amaçlı süzgeci de sınanır.
  const linkableExpenses = () => {
    const list = responses['/api/islemler'];
    if (!Array.isArray(list)) throw new Error('Unexpected API: /api/alis/baglanabilir-giderler');
    return { ogeler: list, sonrakiImlec: null, devamVar: false };
  };
  // Tarayıcı fetch'i gibi: iptal edilen sinyal bekleyen yanıtı AbortError ile reddeder.
  const aborted = () => new DOMException('The operation was aborted.', 'AbortError');
  const abortable = (value, signal) =>
    !signal
      ? value
      : Promise.race([
          Promise.resolve(value),
          new Promise((_, reject) => {
            if (signal.aborted) reject(aborted());
            signal.addEventListener('abort', () => reject(aborted()), { once: true });
          }),
        ]);
  const calls = [];
  // Tarayıcı depolarına yazılan her değer kaydedilir: oturum ve tanıdık cihaz belirteci yalnız HttpOnly çerezlerdedir.
  const stored = [];
  const storage = {
    getItem: () => null,
    setItem: (key, value) => {
      stored.push([key, String(value)]);
    },
    removeItem() {},
  };
  let nextId = 0;
  // Uygulamanın bu örneğinin tarayıcı globalleri: modüller bu adları yalnız bu sahtelerde bulur (tarayici.mjs).
  const browser = {
    ...TARAYICI_DISI,
    // push-client.js ortamı (createPushClient environment = globalThis): verilirse bu testin sahte push tarayıcısıdır.
    globalThis: pushEnvironment ?? globalThis,
    crypto: { randomUUID: () => `11111111-1111-4111-8111-${String(++nextId).padStart(12, '0')}` },
    FormData: TestFormData,
    Node: Element,
    setTimeout: (fn, ms) => {
      timers?.push({ fn, ms });
      return 0;
    },
    localStorage: storage,
    sessionStorage: storage,
    document: {
      querySelector: key => {
        if (!nodes.has(key)) nodes.set(key, root(new Element()));
        return nodes.get(key);
      },
      createElement: tag => new Element(tag),
      createTextNode: text => String(text),
    },
    fetch: async (path, options = {}) => {
      requests.push(path);
      const call = {
        path,
        method: options.method || 'GET',
        credentials: options.credentials,
        headers: Object.fromEntries(new Headers(options.headers || {})),
        body: options.body instanceof TestFormData ? Object.fromEntries(options.body) : options.body ? JSON.parse(options.body) : null,
      };
      Object.defineProperty(call, 'signal', { value: options.signal });
      calls.push(call);
      if (options.signal?.aborted) throw aborted();
      if (!(path in responses)) throw new Error(`Unexpected API: ${path}`);
      const response = responses[path];
      if (response instanceof Error) throw response;
      const value = typeof response === 'function' ? await abortable(response(call), options.signal) : response;
      return { ok: !value?.$status, status: value?.$status || 200, json: async () => value, text: async () => JSON.stringify(value) };
    },
  };
  // app.js başlangıcı gerçek içe aktarmalarıyla çalışır; testlerin eriştiği iç işlevler app.js'in dışa açtığı adlardır.
  const app = await webModulu('app.js', browser);
  for (let index = 0; index < 10; index++) await new Promise(resolve => setImmediate(resolve));
  return { nodes, requests, calls, responses, stored, app };
}

test('full editor startup renders transaction actions and settings navigation', async () => {
  const { nodes, requests } = await openApp(false);
  assert.match(nodes.get('#navigation').textContent, /İşlemler.*Alışlar.*Ayarlar/);
  assert.match(nodes.get('#page-actions').textContent, /Gelir gir.*Gider kaydet/);
  assert.equal(nodes.get('#application').hidden, false);
  assert.deepEqual(requests.slice(0, 2), ['/kasa-runtime.json', '/api/auth/me']);
  // Ana sayfanın inceleme kutusu (webui-6) bütün alış listesini değil, yalnız inceleme özetini okur.
  assert.ok(requests.includes('/api/alis/inceleme-ozeti?adet=4'));
  assert.ok(!requests.includes('/api/alis'));
});
test('read-only editor startup renders live cash without mutation actions or unsupported API calls', async () => {
  const { nodes, requests } = await openApp(true);
  assert.doesNotMatch(nodes.get('#navigation').textContent, /Alışlar|Ayarlar/);
  assert.equal(nodes.get('#page-actions').textContent, '');
  assert.match(nodes.get('#view').textContent, /123,00/);
  assert.deepEqual(requests, ['/kasa-runtime.json', '/api/auth/me', '/api/rapor/panel']);
});
const sampleCard = {
  id: 4,
  surum: 2,
  ad: 'İş kartı',
  yeniTakip: true,
  aktif: true,
  takipBaslangic: '2026-09-23',
  kesimGunu: 20,
  sonOdemeGunu: 30,
  limit: 20000,
  borc: 12000,
  ekstreBorc: 12000,
  harcamalar: [],
  odemeler: [],
  ekstreler: [{ id: 8, kesimTarihi: '2026-09-20', sonOdemeTarihi: '2026-09-30', borc: 12000, odenen: 0, kalan: 12000, asgariOdeme: null }],
};
const sampleLoan = {
  id: 5,
  surum: 1,
  ad: 'İş kredisi',
  yeniTakip: true,
  aktif: true,
  cekilenTutar: 120000,
  cekimTarihi: '2026-09-23',
  kalanPlanliOdeme: 12000,
  kanalPaylari: [{ kanalId: 1, kanal: 'A', tutar: 120000 }],
  taksitler: [
    {
      id: 9,
      no: 1,
      tarih: '2026-10-23',
      tutar: 12000,
      durum: 'Bekliyor',
      not: null,
      dagilimlar: [{ kanalId: 1, kanal: 'A', tutar: 12000 }],
    },
  ],
};
const settle = async () => {
  for (let index = 0; index < 12; index++) await new Promise(resolve => setImmediate(resolve));
};
async function submitDialog(nodes) {
  const form = nodes.get('#modal-content').find(node => node.tag === 'form');
  assert.ok(form, 'Dialog form exists');
  form.listeners.submit({ preventDefault() {} });
  await settle();
}
const formField = (nodes, name) => nodes.get('#modal-content').find(node => node.attributes.name === name);
async function clickDialog(nodes, label) {
  const control = nodes.get('#modal-content').find(node => node.tag === 'button' && node.textContent === label);
  assert.ok(control, `${label} exists`);
  await control.listeners.click({ currentTarget: control });
  await settle();
}

test('channel boxes show server card debt separately from cash and keep unknown debt and card credit separate', async () => {
  const { nodes } = await openApp(false, {
    '/api/rapor/panel': {
      guncelKasa: 123,
      buHaftaSonucu: 20,
      buAySonucu: 50,
      kanallar: [
        { kanalId: 1, kanal: 'Yeni ad', bakiye: 100 },
        { kanalId: 2, kanal: 'Dağılım bekliyor', bakiye: 23 },
      ],
    },
    '/api/takip/ozet?gun=30': {
      kartBorcu: 95,
      kartAlacakBakiyesi: 15,
      kalanKrediPlani: 0,
      olaylar: [],
      kanalKartBorclari: [
        { kanalId: 1, kanal: 'Eski ad', tutar: 40 },
        { kanalId: 2, kanal: 'Dağılım bekliyor', tutar: 20 },
        { kanalId: null, kanal: 'Dağılım bekliyor', tutar: 30 },
        { kanalId: 3, kanal: 'Pasif kanal', tutar: 5 },
      ],
    },
  });
  const view = nodes.get('#view');
  const balances = view.find(node => node.className === 'channel-balances');
  assert.match(balances.children[0].textContent, /Yeni ad.*100,00.*Kalan kart borcu:.*40,00/);
  assert.match(balances.children[1].textContent, /Dağılım bekliyor.*23,00.*Kalan kart borcu:.*20,00/);
  assert.match(view.textContent, /Kanalı belirsiz kart borcu:.*30,00/);
  assert.match(view.textContent, /Pasif kanal kart borcu:.*5,00/);
  assert.equal(view.find(node => node.className === 'cash-total money').textContent, money(123));
  assert.match(view.textContent, /Toplam kart borcu.*95,00/);
  assert.match(view.textContent, /Kart alacak bakiyesi.*15,00.*Diğer kartların borcundan düşülmez/);
});
// Koşullu içerik boolean koşulla eklenir: `dizi.length && düğüm` boş dizide 0 döndürür ve h() sayıyı (bilerek, bkz. childValues
// testi) metin olarak basar. Kanalına bağlanamayan kart borcu yokken ana sayfada kutunun altında tek başına "0" görünüyordu.
test('home screen does not print a stray 0 when every card debt belongs to a listed channel', async () => {
  const texts = node => (Array.isArray(node?.children) ? node.children.flatMap(texts) : [String(node)]);
  for (const debts of [undefined, [], [{ kanalId: 1, kanal: 'Mağaza', tutar: 40 }]]) {
    const { nodes } = await openApp(false, {
      '/api/rapor/panel': { guncelKasa: 123, buHaftaSonucu: 20, buAySonucu: 50, kanallar: [{ kanalId: 1, kanal: 'Mağaza', bakiye: 123 }] },
      '/api/takip/ozet?gun=30': { kartBorcu: 40, kalanKrediPlani: 0, olaylar: [], ...(debts ? { kanalKartBorclari: debts } : {}) },
    });
    const view = nodes.get('#view');
    assert.deepEqual(
      texts(view).filter(text => text.trim() === '0'),
      [],
      `kanalKartBorclari: ${JSON.stringify(debts)}`
    );
    assert.doesNotMatch(view.textContent, /Kanalı belirsiz kart borcu|Mağaza kart borcu:/);
    assert.match(view.textContent, /Kart borçları kasa bakiyesine dahil edilmez/);
  }
});
test('card details show server minimum status and remaining channel debt without changing payment values', async () => {
  const card = {
    ...sampleCard,
    kanalKartBorclari: [
      { kanalId: 1, kanal: 'MEZAT', tutar: 70 },
      { kanalId: null, kanal: 'Dağılım bekliyor', tutar: 30 },
    ],
    ekstreler: [
      { ...sampleCard.ekstreler[0], asgariOdeme: 100, asgariKalan: 37, odenen: 10 },
      { ...sampleCard.ekstreler[0], id: 9, asgariOdeme: 100, asgariKalan: 0 },
      { ...sampleCard.ekstreler[0], id: 10, asgariOdeme: null, asgariKalan: null },
    ],
  };
  const { app, nodes } = await openApp(false, { '/api/auth/me': { rol: 'viewer' }, '/api/takip/kartlar/4': card });
  await app.navigate('cards', 4);
  assert.match(nodes.get('#view').textContent, /MEZAT:.*70,00.*Dağılım bekliyor:.*30,00/);
  assert.match(nodes.get('#view').textContent, /Kayıtlı ödemelere göre asgari için.*37,00.*kaldı/);
  assert.match(nodes.get('#view').textContent, /Kayıtlı ödemelere göre asgari tamamlandı/);
  assert.match(nodes.get('#view').textContent, /Girilmedi/);
  assert.equal(nodes.get('#page-actions').textContent, '');
});
test('purchase card name links to finance only for editors and viewers, while buyer sees plain text', async () => {
  const payment = {
    id: 8,
    tarih: '2026-09-23',
    tutar: 50,
    krediKartiId: 4,
    krediKartiAdi: 'İş kartı',
    dagilimBekliyor: false,
    dagilimlar: [],
  };
  for (const role of ['editor', 'viewer', 'alici']) {
    const { app, requests, nodes } = await openApp(false, {
      '/api/auth/me': { rol: role },
      '/api/alis/kanallar': [],
      '/api/takip/kartlar/4': sampleCard,
    });
    const row = app.paymentRow({ id: 1 }, payment);
    assert.match(row.textContent, /İş kartı ile ödendi/);
    const link = row.find(node => node.tag === 'button' && node.textContent === 'İş kartı');
    if (role === 'alici') {
      assert.equal(link, null);
      assert.equal(
        requests.some(path => path.startsWith('/api/takip')),
        false
      );
    } else {
      assert.ok(link);
      await link.listeners.click();
      await settle();
      assert.equal(nodes.get('#page-title').textContent, 'İş kartı');
    }
  }
});

const similarCharge = {
  kaynak: 'KartHarcama',
  id: 7,
  tarih: '2026-09-23',
  tutar: 75,
  aciklama: '<img src=x onerror=alert(1)> Mal',
  krediKartiId: 4,
  alisId: 6,
};
test('card charge duplicate warning keeps the form, escapes descriptions and requires explicit separate-save approval', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [],
    '/api/takip/kartlar/4': sampleCard,
    '/api/takip/kartlar/4/harcamalar': sampleCard,
    '/api/islemler/benzerlik': [similarCharge],
  });
  await app.navigate('cards', 4);
  const open = nodes.get('#page-actions').find(node => node.textContent === '+ Harcama / iade');
  await open.listeners.click({ currentTarget: open });
  await settle();
  formField(nodes, 'aciklama').value = 'Ayrı mal';
  formField(nodes, 'tutar').value = '75';
  formField(nodes, 'taksitSayisi').value = '3';
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.path.endsWith('/harcamalar')),
    false
  );
  assert.equal(formField(nodes, 'aciklama').value, 'Ayrı mal');
  assert.equal(formField(nodes, 'taksitSayisi').value, '3');
  assert.match(nodes.get('#modal-content').textContent, /Benzer kayıt bulundu.*<img src=x onerror=alert\(1\)> Mal/);
  assert.equal(
    nodes.get('#modal-content').find(node => node.tag === 'img'),
    null
  );
  await clickDialog(nodes, 'Ayrı işlem olarak kaydet');
  const saved = calls.filter(call => call.path.endsWith('/harcamalar'));
  assert.equal(saved.length, 1);
  assert.equal(saved[0].body.tutar, 75);
  assert.equal(saved[0].body.taksitSayisi, 3);
  assert.equal(calls.filter(call => call.path === '/api/islemler/benzerlik').length, 1);
});
test('card payment duplicate approval retains preview amounts and the idempotency key after an uncertain save', async () => {
  let attempts = 0;
  const preview = {
    tutar: 2000,
    kasaEtkisi: 2000,
    dagilimlar: [
      { kanalId: 1, kanal: 'MEZAT', tutar: 1200 },
      { kanalId: 2, kanal: 'PERAKENDE', tutar: 800 },
    ],
    ekstreler: [{ ekstreId: 8, tutar: 2000 }],
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/takip/kartlar/4/odeme-onizleme': preview,
    '/api/takip/kartlar/4': sampleCard,
    '/api/islemler/benzerlik': [{ ...similarCharge, kaynak: 'KartOdeme', tutar: 2000 }],
    '/api/takip/kartlar/4/odemeler': () => {
      if (++attempts === 1) throw new Error('Network response lost');
      return sampleCard;
    },
  });
  app.financeUi.cardPaymentDialog(sampleCard);
  formField(nodes, 'tutar').value = '2000';
  await submitDialog(nodes);
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /MEZAT:.*1\.200,00.*PERAKENDE:.*800,00.*Benzer kayıt bulundu/);
  assert.equal(
    calls.some(call => call.path.endsWith('/odemeler')),
    false
  );
  await clickDialog(nodes, 'Ayrı işlem olarak kaydet');
  await submitDialog(nodes);
  const writes = calls.filter(call => call.path.endsWith('/odemeler'));
  assert.equal(writes.length, 2);
  assert.deepEqual(writes[0].body, writes[1].body);
  const previewCall = calls.find(call => call.path.endsWith('/odeme-onizleme'));
  assert.equal(writes[0].body.istekId, previewCall.body.istekId);
  assert.equal(calls.find(call => call.path === '/api/islemler/benzerlik').body.tur, 'KartOdeme');
});
test('new cash expense duplicate can be cancelled and lookup failure never silently saves it', async () => {
  const { app, nodes, calls, responses } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': [],
    '/api/islemler': [],
    '/api/islemler/benzerlik': [{ ...similarCharge, kaynak: 'Islem', krediKartiId: null }],
  });
  const fill = async () => {
    await app.expenseDialog();
    for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A', tarih: '2026-09-23' }))
      formField(nodes, name).value = value;
  };
  await fill();
  await submitDialog(nodes);
  const lookup = calls.find(call => call.path === '/api/islemler/benzerlik');
  assert.deepEqual(lookup.body, { tur: 'Gider', tarih: '2026-09-23', tutar: 75, krediKartiId: null, kanal: 'A', alisId: null });
  await clickDialog(nodes, 'Vazgeç');
  assert.equal(nodes.get('#modal').open, false);
  assert.equal(
    calls.some(call => call.path === '/api/islemler' && call.method === 'POST'),
    false
  );
  responses['/api/islemler/benzerlik'] = new Error('Offline');
  await fill();
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /Benzer kayıt kontrolü tamamlanamadı.*Kayıt yapılmadı/);
  assert.equal(formField(nodes, 'cari').value, 'Kargo');
  assert.equal(
    calls.some(call => call.path === '/api/islemler' && call.method === 'POST'),
    false
  );
});
test('new purchase payment checks its purchase and card, while linking an existing expense skips duplicate checks', async () => {
  const purchase = { id: 6, surum: 2, kalan: 100, durum: 'Onaylandi' };
  const expense = { id: 20, tarih: '2026-09-23', tutarTl: 75, cari: 'Mal', krediKartiId: 4 };
  const { app, nodes, calls, responses } = await openApp(false, {
    '/api/kredikartlari': [{ id: 4, ad: 'İş kartı', yeniTakip: true, aktif: true }],
    '/api/islemler': [expense],
    '/api/alis/6/odemeler': purchase,
    '/api/alis/kanallar': [],
    '/api/islemler/benzerlik': [similarCharge],
  });
  await app.paymentDialog(purchase);
  formField(nodes, 'tutar').value = '75';
  formField(nodes, 'krediKartiId').value = '4';
  assert.ok(
    formField(nodes, 'krediKartiId').children.some(option => option.textContent === 'İş kartı'),
    'Takipteki kart seçilebilir.'
  );
  await submitDialog(nodes);
  const lookup = calls.find(call => call.path === '/api/islemler/benzerlik');
  assert.equal(lookup.body.tur, 'AlisOdeme');
  assert.equal(lookup.body.alisId, 6);
  assert.equal(lookup.body.krediKartiId, 4);
  assert.equal(
    calls.some(call => call.path === '/api/alis/6/odemeler'),
    false
  );
  await clickDialog(nodes, 'Vazgeç');
  responses['/api/islemler/benzerlik'] = new Error('This must not be called');
  await app.paymentDialog(purchase);
  const existing = formField(nodes, 'mevcutIslemId');
  existing.value = '20';
  existing.listeners.change();
  await submitDialog(nodes);
  const save = calls.find(call => call.path === '/api/alis/6/odemeler');
  assert.ok(save);
  assert.equal(save.body.mevcutIslemId, 20);
  assert.equal(save.body.tutar, 75);
  assert.equal(calls.filter(call => call.path === '/api/islemler/benzerlik').length, 1);
});
test('purchase payment dialog uses the server linkable-expense page instead of the full expense history, with search and older pages', async () => {
  const purchase = { id: 6, surum: 2, kalan: 100, durum: 'Onaylandi' };
  const row = (id, cari, tutarTl) => ({ id, tarih: `2026-09-${id - 10}`, cari, tutarTl, krediKartiId: null });
  const { app, nodes, calls } = await openApp(false, {
    '/api/kredikartlari': [],
    '/api/alis/6/odemeler': purchase,
    '/api/alis/baglanabilir-giderler': {
      ogeler: [row(31, 'Kargo', 40), row(30, 'Ambalaj', 20)],
      sonrakiImlec: '20260920-30',
      devamVar: true,
    },
    '/api/alis/baglanabilir-giderler?imlec=20260920-30': {
      ogeler: [row(29, 'Eski mal', 75), { ...row(28, 'Bağlı', 10), alisId: 3 }],
      sonrakiImlec: null,
      devamVar: false,
    },
    '/api/alis/baglanabilir-giderler?arama=Kargo+Co': { ogeler: [], sonrakiImlec: null, devamVar: false },
    '/api/alis/baglanabilir-giderler?arama=75&aramaTutari=75.00': {
      ogeler: [row(29, 'Eski mal', 75)],
      sonrakiImlec: null,
      devamVar: false,
    },
  });
  await app.paymentDialog(purchase);
  assert.equal(
    calls.some(call => call.path.startsWith('/api/islemler')),
    false
  );
  const existing = formField(nodes, 'mevcutIslemId');
  const options = () => existing.children.filter(node => node.tag === 'option').map(node => node.value);
  assert.deepEqual(options(), ['', '31', '30']);
  const more = nodes.get('#modal-content').find(node => node.tag === 'button' && node.textContent === 'Daha eski giderler');
  assert.equal(more.hidden, false);
  existing.value = '31';
  existing.listeners.change();
  await more.listeners.click({ currentTarget: more });
  await settle();
  assert.deepEqual(options(), ['', '31', '30', '29']);
  assert.equal(existing.value, '31');
  assert.equal(more.hidden, true);
  const search = formField(nodes, 'giderArama');
  search.value = 'Kargo Co';
  search.listeners.change();
  await settle();
  assert.deepEqual(options(), ['']);
  assert.match(nodes.get('#modal-content').textContent, /Eşleşen bağlanabilir gider yok/);
  assert.equal(formField(nodes, 'tutar').disabled, false);
  let prevented = false;
  search.value = '75';
  search.listeners.keydown({
    key: 'Enter',
    preventDefault() {
      prevented = true;
    },
  });
  await settle();
  assert.equal(prevented, true);
  assert.deepEqual(options(), ['', '29']);
  assert.equal(
    calls.some(call => call.path === '/api/alis/6/odemeler'),
    false
  );
  existing.value = '29';
  existing.listeners.change();
  await submitDialog(nodes);
  const save = calls.find(call => call.path === '/api/alis/6/odemeler');
  assert.equal(save.body.mevcutIslemId, 29);
  assert.equal(save.body.tutar, 75);
});
test('older linkable-expense page uses the cursor only with the search text that produced it; changed text restarts the search', async () => {
  const purchase = { id: 6, surum: 2, kalan: 100, durum: 'Onaylandi' };
  const row = (id, cari, tutarTl) => ({ id, tarih: `2026-09-${id - 10}`, cari, tutarTl, krediKartiId: null });
  const base = '/api/alis/baglanabilir-giderler';
  const { app, nodes, calls } = await openApp(false, {
    '/api/kredikartlari': [],
    '/api/alis/6/odemeler': purchase,
    [base]: { ogeler: [row(31, 'Kargo', 40), row(30, 'Ambalaj', 20)], sonrakiImlec: '20260920-30', devamVar: true },
    [`${base}?arama=Kargo`]: { ogeler: [row(31, 'Kargo', 40), row(25, 'Kargo', 15)], sonrakiImlec: '20260915-25', devamVar: true },
    [`${base}?arama=Kargo&imlec=20260915-25`]: { ogeler: [row(20, 'Kargo', 5)], sonrakiImlec: null, devamVar: false },
  });
  await app.paymentDialog(purchase);
  const existing = formField(nodes, 'mevcutIslemId');
  const options = () => existing.children.filter(node => node.tag === 'option').map(node => node.value);
  const more = nodes.get('#modal-content').find(node => node.tag === 'button' && node.textContent === 'Daha eski giderler');
  // Yeni metin yazılıp doğrudan 'Daha eski giderler'e basılır: önce kutunun change (blur) araması başlar, tıklama onu geçersiz
  // kılar. Süzgeçsiz listenin imleci 'Kargo' ile birleştirilmez (daha yeni Kargo #25 atlanır, Ambalaj listede kalırdı).
  const search = formField(nodes, 'giderArama');
  search.value = 'Kargo';
  search.listeners.change();
  await more.listeners.click({ currentTarget: more });
  await settle();
  assert.equal(
    calls.some(call => call.path === `${base}?arama=Kargo&imlec=20260920-30`),
    false
  );
  assert.deepEqual(options(), ['', '31', '25']);
  assert.equal(more.hidden, false);
  // Metin değişmedikçe imleç aynı aramanın sonraki sayfasıdır.
  await more.listeners.click({ currentTarget: more });
  await settle();
  assert.deepEqual(options(), ['', '31', '25', '20']);
  assert.equal(more.hidden, true);
});
test('new expense and new purchase reuse the request id after a lost response, while edits send none', async () => {
  let expenseAttempts = 0;
  let purchaseAttempts = 0;
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': [],
    '/api/islemler/benzerlik': [],
    '/api/islemler/20': { id: 20 },
    '/api/islemler': call => {
      if (call.method === 'POST' && ++expenseAttempts === 1) throw new Error('Network response lost');
      return call.method === 'POST' ? { id: 21 } : [];
    },
    '/api/alis/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/alis/9/belgeler': [],
    '/api/alis': call => {
      if (call.method === 'POST' && ++purchaseAttempts === 1) throw new Error('Network response lost');
      return call.method === 'POST'
        ? { id: 9 }
        : [
            {
              id: 9,
              surum: 1,
              tarih: '2026-09-23',
              tedarikci: 'Firma',
              durum: 'Taslak',
              kalemler: [],
              odemeler: [],
              toplam: 0,
              odenen: 0,
              kalan: 0,
            },
          ];
    },
  });
  await app.expenseDialog();
  for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A', tarih: '2026-09-23' }))
    formField(nodes, name).value = value;
  await submitDialog(nodes);
  await submitDialog(nodes);
  const expenseWrites = calls.filter(call => call.path === '/api/islemler' && call.method === 'POST');
  assert.equal(expenseWrites.length, 2);
  assert.ok(expenseWrites[0].body.istekId);
  assert.deepEqual(expenseWrites[0].body, expenseWrites[1].body);
  await app.expenseDialog({ id: 20, tarih: '2026-09-23', cari: 'Kargo', tutarTl: 75, kanal: 'A', tip: 'Cari' });
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/islemler/20' && call.method === 'PUT').body.istekId, undefined);

  await app.navigate('purchases');
  const create = nodes.get('#page-actions').find(node => node.tag === 'button' && node.textContent === '+ Yeni alış');
  await create.listeners.click({ currentTarget: create });
  await settle();
  formField(nodes, 'tedarikci').listeners.input({ target: { value: 'Firma' } });
  formField(nodes, 'aciklama-0').listeners.input({ target: { value: 'Mal' } });
  formField(nodes, 'tutar-0').listeners.input({ target: { value: '100' } });
  await submitDialog(nodes);
  await submitDialog(nodes);
  const purchaseWrites = calls.filter(call => call.path === '/api/alis' && call.method === 'POST');
  assert.equal(purchaseWrites.length, 2);
  assert.ok(purchaseWrites[0].body.istekId);
  assert.deepEqual(purchaseWrites[0].body, purchaseWrites[1].body);
});
test('purchase document download names come from the stored type, never from the uploaded extension or direction marks', () => {
  assert.equal(ui.documentFileName('fatura.pdf.hta', 'application/pdf'), 'fatura.pdf');
  assert.equal(ui.documentFileName('fatura‮fdp.hta', 'application/pdf'), 'faturafdp.pdf');
  assert.equal(ui.documentFileName('..\\gizli\\CON.png', 'image/png'), 'belge-CON.png');
  assert.equal(ui.documentFileName('foto.jpeg', 'image/jpeg'), 'foto.jpg');
  assert.equal(ui.documentFileName('sayfa.html', 'text/html'), 'sayfa.bin');
  assert.equal(ui.documentFileName('Fatura 12.05.2024', 'application/pdf'), 'Fatura 12.05.2024.pdf');
  assert.equal(ui.documentFileName('', 'application/pdf'), 'belge.pdf');
  assert.equal(ui.linkableExpensesPath('  Kargo & Co '), '/api/alis/baglanabilir-giderler?arama=Kargo+%26+Co');
  // Tutar gibi okunan metin (ör. '2024' bir fatura numarası da olabilir) metin olarak da aranır; sunucu ikisinden birine uyanı döndürür.
  assert.equal(
    ui.linkableExpensesPath('12,5', '20260920-30'),
    '/api/alis/baglanabilir-giderler?arama=12%2C5&aramaTutari=12.50&imlec=20260920-30'
  );
  assert.equal(ui.linkableExpensesPath('2024'), '/api/alis/baglanabilir-giderler?arama=2024&aramaTutari=2024.00');
  assert.equal(ui.linkableExpensesPath(''), '/api/alis/baglanabilir-giderler');
});
test('changing a field invalidates duplicate approval and cancelling during lookup cannot create an expense', async () => {
  const { app, nodes, calls, responses } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': [],
    '/api/islemler': [],
    '/api/islemler/benzerlik': [similarCharge],
  });
  await app.expenseDialog();
  for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A' })) formField(nodes, name).value = value;
  await submitDialog(nodes);
  formField(nodes, 'tutarTl').value = '80';
  await clickDialog(nodes, 'Ayrı işlem olarak kaydet');
  assert.equal(calls.filter(call => call.path === '/api/islemler/benzerlik').length, 2);
  assert.equal(
    calls.some(call => call.path === '/api/islemler' && call.method === 'POST'),
    false
  );
  let finishLookup;
  responses['/api/islemler/benzerlik'] = () =>
    new Promise(resolve => {
      finishLookup = resolve;
    });
  await submitDialog(nodes);
  assert.equal(typeof finishLookup, 'function');
  await clickDialog(nodes, 'Vazgeç');
  finishLookup([]);
  await settle();
  assert.equal(
    calls.some(call => call.path === '/api/islemler' && call.method === 'POST'),
    false
  );
});
test('editing an existing expense skips new-record duplicate lookup', async () => {
  const expense = { id: 20, tarih: '2026-09-23', tutarTl: 75, cari: 'Kargo', tip: 'Cari', kanal: 'A', not: '', krediKartiId: null };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': [],
    '/api/islemler/20': expense,
    '/api/islemler/benzerlik': new Error('Must not call'),
  });
  await app.expenseDialog(expense);
  formField(nodes, 'tutarTl').value = '80';
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.path === '/api/islemler/benzerlik'),
    false
  );
  assert.equal(calls.find(call => call.path === '/api/islemler/20').body.tutarTl, 80);
});
test('viewer can see card and loan details without payment, create or plan-change controls', async () => {
  const { app, nodes } = await openApp(false, {
    '/api/auth/me': { rol: 'viewer' },
    '/api/takip/kartlar/4': sampleCard,
    '/api/takip/krediler/5': sampleLoan,
  });
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
  assert.equal(
    requests.some(path => path.startsWith('/api/takip') || path.startsWith('/api/bildirimler')),
    false
  );
});
test('card payment requires visible server distribution preview before the payment POST', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/takip/kartlar/4/odeme-onizleme': {
      tutar: 2000,
      kasaEtkisi: 2000,
      dagilimlar: [
        { kanalId: 1, kanal: 'MEZAT', tutar: 1200 },
        { kanalId: 2, kanal: 'PERAKENDE', tutar: 800 },
      ],
      ekstreler: [{ ekstreId: 8, tutar: 2000 }],
    },
    '/api/takip/kartlar/4/odemeler': sampleCard,
    '/api/takip/kartlar/4': sampleCard,
  });
  app.financeUi.cardPaymentDialog(sampleCard);
  nodes.get('#modal-content').find(node => node.attributes.name === 'tutar').value = '2000,00';
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.path.endsWith('/odemeler')),
    false
  );
  assert.match(nodes.get('#modal-content').textContent, /MEZAT:.*1\.200,00/);
  assert.match(nodes.get('#modal-content').textContent, /PERAKENDE:.*800,00/);
  await submitDialog(nodes);
  const payment = calls.find(call => call.path.endsWith('/odemeler'));
  assert.equal(payment.method, 'POST');
  assert.equal(payment.body.tutar, 2000);
  assert.equal(payment.body.surum, 2);
  assert.ok(payment.body.istekId);
});
test('card refund requires an existing positive charge and delegates its original channel allocation to the server', async () => {
  const card = {
    ...sampleCard,
    harcamalar: [
      { id: 11, tarih: '2026-09-21', aciklama: 'A kanalı mal', tutar: 200, taksitSayisi: 1, dagilimlar: [] },
      { id: 12, tarih: '2026-09-22', aciklama: 'B kanalı mal', tutar: 300, taksitSayisi: 1, dagilimlar: [] },
      { id: 13, tarih: '2026-09-22', aciklama: 'Eski iade', tutar: -10, taksitSayisi: 1, dagilimlar: [] },
      { id: 14, tarih: '2026-09-22', aciklama: 'İptal harcama', tutar: 50, iptal: true, taksitSayisi: 1, dagilimlar: [] },
    ],
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [],
    '/api/takip/kartlar/4': card,
    '/api/takip/kartlar/4/harcamalar': card,
  });
  await app.navigate('cards', 4);
  const open = nodes.get('#page-actions').find(node => node.tag === 'button' && node.textContent === '+ Harcama / iade');
  await open.listeners.click({ currentTarget: open });
  await settle();
  const content = nodes.get('#modal-content');
  const total = content.find(node => node.attributes.name === 'tutar');
  const source = content.find(node => node.attributes.name === 'kaynakHarcamaId');
  assert.equal(source.disabled, true);
  assert.doesNotMatch(source.textContent, /Eski iade|İptal harcama/);
  total.value = '-25,50';
  total.listeners.input();
  content.find(node => node.attributes.name === 'aciklama').value = 'Mal iadesi';
  assert.equal(source.required, true);
  assert.equal(source.disabled, false);
  assert.equal(content.find(node => node.attributes.name === 'taksitSayisi').disabled, true);
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.path.endsWith('/harcamalar')),
    false
  );
  assert.match(content.textContent, /İadenin bağlı olduğu harcamayı seçin/);
  source.value = '12';
  await submitDialog(nodes);
  const refund = calls.find(call => call.path.endsWith('/harcamalar'));
  assert.equal(refund.body.kaynakHarcamaId, 12);
  assert.equal(refund.body.tutar, -25.5);
  assert.equal(refund.body.taksitSayisi, 1);
  assert.deepEqual(refund.body.dagilimlar, []);
  assert.equal(refund.body.ilkKesimTarihi, null);
});
test('positive card charge sends no refund source even after switching the amount from refund to purchase', async () => {
  const card = {
    ...sampleCard,
    harcamalar: [{ id: 11, tarih: '2026-09-21', aciklama: 'Mal', tutar: 200, taksitSayisi: 1, dagilimlar: [] }],
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [],
    '/api/takip/kartlar/4': card,
    '/api/takip/kartlar/4/harcamalar': card,
  });
  await app.navigate('cards', 4);
  const open = nodes.get('#page-actions').find(node => node.tag === 'button' && node.textContent === '+ Harcama / iade');
  await open.listeners.click({ currentTarget: open });
  await settle();
  const content = nodes.get('#modal-content');
  const total = content.find(node => node.attributes.name === 'tutar');
  const source = content.find(node => node.attributes.name === 'kaynakHarcamaId');
  total.value = '-5';
  total.listeners.input();
  source.value = '11';
  total.value = '75';
  total.listeners.input();
  content.find(node => node.attributes.name === 'aciklama').value = 'Yeni mal';
  content.find(node => node.attributes.name === 'taksitSayisi').value = '3';
  assert.equal(source.disabled, true);
  assert.equal(source.required, false);
  await submitDialog(nodes);
  const charge = calls.find(call => call.path.endsWith('/harcamalar'));
  assert.equal(charge.body.kaynakHarcamaId, null);
  assert.equal(charge.body.tutar, 75);
  assert.equal(charge.body.taksitSayisi, 3);
});
test('new loan form sends one draw with selected fixed equal-share channel IDs', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [
      { id: 3, ad: 'C', aktif: true },
      { id: 1, ad: 'A', aktif: true },
    ],
    '/api/takip/krediler': sampleLoan,
    '/api/takip/krediler/5': sampleLoan,
  });
  await app.financeUi.loanDialog();
  const content = nodes.get('#modal-content');
  assert.equal(content.find(node => node.attributes.name === 'mevcutKredi').value, 'false');
  assert.ok(
    content.find(node => node.attributes.name === 'ilkTaksitTarihi').value >
      content.find(node => node.attributes.name === 'cekimTarihi').value
  );
  for (const [name, value] of Object.entries({ ad: 'Yeni kredi', cekilenTutar: '120000', aylikOdeme: '12000', taksitSayisi: '10' }))
    content.find(node => node.attributes.name === name).value = value;
  for (const id of [3, 1]) {
    const choice = content.find(node => node.attributes.name === `kredi-kanal-${id}`);
    choice.checked = true;
    choice.listeners.change({ target: choice });
  }
  await submitDialog(nodes);
  const create = calls.find(call => call.path === '/api/takip/krediler' && call.method === 'POST');
  assert.ok(create);
  assert.equal(create.body.cekilenTutar, 120000);
  assert.equal(create.body.mevcutKredi, false);
  assert.deepEqual(create.body.kanalIdleri, [1, 3]);
});
test('new loan default installment clamps the next month and preserves a manually chosen date', async () => {
  const { app, nodes } = await openApp(false, { '/api/kanallar': [] });
  await app.financeUi.loanDialog();
  const content = nodes.get('#modal-content');
  const draw = content.find(node => node.attributes.name === 'cekimTarihi');
  const first = content.find(node => node.attributes.name === 'ilkTaksitTarihi');
  draw.value = '2027-01-31';
  draw.listeners.input();
  assert.equal(first.value, '2027-02-28');
  draw.value = '2028-01-31';
  draw.listeners.input();
  assert.equal(first.value, '2028-02-29');
  draw.value = '2027-12-31';
  draw.listeners.input();
  assert.equal(first.value, '2028-01-31');
  first.value = '2028-02-15';
  first.listeners.input();
  draw.value = '2028-01-20';
  draw.listeners.input();
  assert.equal(first.value, '2028-02-15');
});
test('processed installment form keeps amount and date locked and saves only its note without a new payment', async () => {
  const installment = { ...sampleLoan.taksitler[0], durum: 'KasayaIslendi' };
  const { app, nodes, calls } = await openApp(false, {
    '/api/takip/krediler/5/taksitler/9': sampleLoan,
    '/api/takip/krediler/5': sampleLoan,
  });
  app.financeUi.installmentDialog(sampleLoan, installment);
  const content = nodes.get('#modal-content');
  assert.equal(content.find(node => node.attributes.name === 'tarih').disabled, true);
  assert.equal(content.find(node => node.attributes.name === 'tutar').disabled, true);
  content.find(node => node.attributes.name === 'not').value = 'Dekont incelendi';
  content.find(node => node.attributes.name === 'aciklama').value = 'Not eklendi';
  await submitDialog(nodes);
  const save = calls.find(call => call.method === 'PUT');
  assert.equal(save.body.tutar, 12000);
  assert.equal(save.body.iptal, false);
  assert.equal(save.body.not, 'Dekont incelendi');
  assert.equal(
    calls.some(call => call.method === 'POST'),
    false
  );
});
test('cash dashboard labels only overdue open card payments using the server date', async () => {
  const { nodes } = await openApp(false, {
    '/api/takip/ozet?gun=30': {
      tarih: '2030-09-23',
      kartBorcu: 500,
      kalanKrediPlani: 100,
      olaylar: [
        { kaynak: 'Kart', kaynakId: 4, ad: 'Geciken kart', tarih: '2030-09-22', tutar: 200, tur: 'SonOdeme', otomatikKasa: false },
        { kaynak: 'Kart', kaynakId: 5, ad: 'Bugünkü kart', tarih: '2030-09-23', tutar: 300, tur: 'SonOdeme', otomatikKasa: false },
        { kaynak: 'Kredi', kaynakId: 6, ad: 'İşlenmiş taksit', tarih: '2030-09-22', tutar: 100, tur: 'Taksit', otomatikKasa: true },
      ],
    },
  });
  assert.equal((nodes.get('#view').textContent.match(/Gecikti/g) || []).length, 1);
  assert.match(nodes.get('#view').textContent, /Yeni kart takibinde kaydedilen kart ödemeleri kasadan düşer/);
});
test('notification center marks an item read without creating a finance request', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/bildirimler': [
      {
        id: 3,
        baslik: 'Kart ödemesi yaklaşıyor',
        mesaj: 'Üç gün sonra son ödeme.',
        tarih: '2026-09-23',
        okundu: false,
        hedef: '/#cards/4',
        tur: 'SonOdeme',
        kaynakId: 4,
      },
    ],
    '/api/bildirimler/3/okundu': null,
  });
  await app.navigate('notifications');
  assert.match(nodes.get('#view').textContent, /Kart ödemesi yaklaşıyor/);
  const mark = nodes.get('#view').find(node => node.tag === 'button' && node.textContent === 'Okundu olarak işaretle');
  await mark.listeners.click({ currentTarget: mark });
  const writes = calls.filter(call => call.method !== 'GET');
  assert.deepEqual(
    writes.map(call => call.path),
    ['/api/bildirimler/3/okundu']
  );
});
function fakePushBrowser(permission = 'granted') {
  const counts = { permission: 0, register: 0, subscribe: 0, unsubscribe: 0 };
  const saved = new Map();
  let subscription = null;
  const current = {
    endpoint: 'https://push.example/this-device',
    toJSON: () => ({ keys: { p256dh: 'device-public-key', auth: 'device-auth' } }),
    unsubscribe: async () => {
      counts.unsubscribe++;
      subscription = null;
      return true;
    },
  };
  const registration = {
    pushManager: {
      getSubscription: async () => subscription,
      subscribe: async () => {
        counts.subscribe++;
        subscription = current;
        return current;
      },
    },
  };
  const environment = {
    isSecureContext: true,
    PushManager: class {},
    Notification: {
      permission: 'default',
      requestPermission: async () => {
        counts.permission++;
        return permission;
      },
    },
    navigator: {
      serviceWorker: {
        getRegistration: async () => registration,
        register: async () => {
          counts.register++;
          return registration;
        },
        ready: Promise.resolve(registration),
      },
    },
    crypto: { randomUUID: () => '11111111-1111-4111-8111-111111111111' },
    localStorage: { getItem: key => saved.get(key), setItem: (key, value) => saved.set(key, value) },
  };
  return { environment, counts, saved };
}
test('push startup never prompts; explicit enable registers only this device and persists no token', async () => {
  const browser = fakePushBrowser();
  const calls = [];
  const client = pushModule.createPushClient({
    environment: browser.environment,
    api: async (path, options) => {
      calls.push({ path, options });
      return path.endsWith('/anahtar') ? { etkin: true, publicKey: 'AQID' } : { id: 1 };
    },
  });
  await client.status();
  assert.equal(browser.counts.permission, 0);
  assert.equal(browser.counts.register, 0);
  await client.enable('Telefonum');
  assert.equal(browser.counts.permission, 1);
  assert.equal(browser.counts.subscribe, 1);
  const subscribe = calls.find(call => call.path.endsWith('/abonelik'));
  assert.equal(subscribe.options.body.cihazAdi, 'Telefonum');
  assert.equal(subscribe.options.body.endpoint, 'https://push.example/this-device');
  assert.deepEqual([...browser.saved.keys()], ['kasa-device-id']);
});
test('notification settings can renew a disabled server registration while retaining the existing browser subscription', async () => {
  const browser = fakePushBrowser();
  const firstClient = pushModule.createPushClient({
    environment: browser.environment,
    api: async () => ({ etkin: true, publicKey: 'AQID' }),
  });
  await firstClient.enable('İlk kayıt');
  const { app, nodes, calls } = await openApp(
    false,
    {
      '/api/bildirimler/ayarlar': { etkin: true, saat: 9, dakika: 0, surum: 1 },
      '/api/bildirimler/push/anahtar': { etkin: true, publicKey: 'AQID' },
      '/api/bildirimler/push/abonelikler': [{ id: 7, cihazAdi: 'Telefon', etkin: false, sonBasarili: null }],
      '/api/bildirimler/push/abonelik': { id: 7, cihazAdi: 'Telefonum' },
    },
    browser.environment
  );
  await app.notificationUi.settings();
  const content = nodes.get('#modal-content');
  content.find(node => node.attributes.name === 'cihazAdi').value = 'Telefonum';
  const renew = content.find(node => node.tag === 'button' && node.textContent === 'Bildirim kaydını yenile');
  assert.ok(renew, 'An existing browser subscription still exposes registration renewal');
  await renew.listeners.click({ currentTarget: renew });
  await settle();
  const writes = calls.filter(call => call.method !== 'GET');
  assert.deepEqual(
    writes.map(call => call.path),
    ['/api/bildirimler/push/abonelik']
  );
  assert.equal(writes[0].body.endpoint, 'https://push.example/this-device');
  assert.equal(writes[0].body.cihazAdi, 'Telefonum');
  assert.equal(browser.counts.subscribe, 1);
  assert.equal(browser.counts.unsubscribe, 0);
  assert.equal(calls.filter(call => call.path === '/api/bildirimler/ayarlar').length, 2, 'Settings reload after renewal');
  assert.match(nodes.get('#notifications').textContent, /bildirim kaydı yenilendi/);
});
test('denied push permission causes no API subscription and logout still unsubscribes when API deletion fails', async () => {
  const denied = fakePushBrowser('denied');
  let requests = 0;
  const deniedClient = pushModule.createPushClient({
    environment: denied.environment,
    api: async () => {
      requests++;
    },
  });
  await assert.rejects(deniedClient.enable('Telefon'), /izin/);
  assert.equal(requests, 0);
  assert.equal(denied.counts.subscribe, 0);
  const browser = fakePushBrowser();
  const calls = [];
  const client = pushModule.createPushClient({
    environment: browser.environment,
    api: async (path, options = {}) => {
      calls.push({ path, options });
      if (options.method === 'DELETE') throw new Error('401');
      return { etkin: true, publicKey: 'AQID' };
    },
  });
  await client.enable('Bilgisayar');
  await client.disable({ bestEffort: true });
  assert.equal(browser.counts.unsubscribe, 1);
  const deletes = calls.filter(call => call.options.method === 'DELETE');
  assert.equal(deletes.length, 1);
  assert.deepEqual(deletes[0].options.body, { endpoint: 'https://push.example/this-device' });
});
test('push permission result from a previous session cannot register the next user device', async () => {
  const browser = fakePushBrowser();
  let finishPermission;
  let session = 1;
  let requests = 0;
  browser.environment.Notification.requestPermission = () =>
    new Promise(resolve => {
      finishPermission = resolve;
    });
  const client = pushModule.createPushClient({
    environment: browser.environment,
    session: () => session,
    api: async () => {
      requests++;
    },
  });
  const enabling = client.enable('Telefon');
  session = 2;
  finishPermission('granted');
  await assert.rejects(enabling, /Oturum değişti/);
  assert.equal(requests, 0);
  assert.equal(browser.counts.subscribe, 0);
});
test('a rejected server subscription rolls back a new browser subscription', async () => {
  const browser = fakePushBrowser();
  const client = pushModule.createPushClient({
    environment: browser.environment,
    api: async path => {
      if (path.endsWith('/anahtar')) return { etkin: true, publicKey: 'AQID' };
      throw new Error('Registration failed');
    },
  });
  await assert.rejects(client.enable('Telefon'), /Registration failed/);
  assert.equal(browser.counts.unsubscribe, 1);
});
test('notification deep links enforce financial role and service worker rejects external targets', async () => {
  assert.deepEqual(pushModule.notificationRoute('#cards/4', 'editor'), { view: 'cards', id: 4 });
  assert.equal(pushModule.notificationRoute('#loans/5', 'alici'), null);
  assert.equal(pushModule.notificationRoute('#notifications', 'viewer'), null);
  const handlers = {};
  const shown = [];
  const worker = {
    location: { origin: 'https://kasa.example' },
    addEventListener: (name, handler) => {
      handlers[name] = handler;
    },
    registration: {
      showNotification: async (title, options) => {
        shown.push({ title, options });
      },
    },
  };
  runInNewContext(await readFile(new URL('../Kasa.Api/wwwroot/service-worker.js', import.meta.url), 'utf8'), { self: worker, URL });
  let done;
  handlers.push({
    data: { json: () => ({ baslik: 'Ödeme', mesaj: 'Kart', url: 'https://attacker.example/#cards/4' }) },
    waitUntil: promise => {
      done = promise;
    },
  });
  await done;
  assert.equal(shown[0].options.data.url, 'https://kasa.example/#notifications');
  handlers.push({
    data: { json: () => ({ url: '/#loans/5' }) },
    waitUntil: promise => {
      done = promise;
    },
  });
  await done;
  assert.equal(shown[1].options.data.url, 'https://kasa.example/#loans/5');
  assert.equal('fetch' in handlers, false);
});
test('legacy income selection sums preserved rows without selecting one arbitrarily', () => {
  const rows = [
    { kanal: 'A', tutarTl: 0.1, eskiYinelenenGrup: true },
    { kanal: 'A', tutarTl: 0.2, eskiYinelenenGrup: true },
    { kanal: 'B', tutarTl: 5 },
  ];
  assert.deepEqual(incomeSelection(rows, 'A'), { total: 0.3, count: 2, readOnly: true, surum: 0 });
  assert.deepEqual(incomeSelection(rows, 'B'), { total: 5, count: 1, readOnly: false, surum: 0 });
  assert.deepEqual(incomeSelection(rows, 'C'), { total: 0, count: 0, readOnly: false, surum: 0 });
  assert.equal(incomeSelection([{ kanal: 'A', tutarTl: 5, eskiYinelenenGrup: true }], 'A').readOnly, true);
  assert.equal(
    incomeSelection(
      [
        { kanal: 'A', tutarTl: 5 },
        { kanal: 'A', tutarTl: -2 },
      ],
      'A'
    ).readOnly,
    true
  );
  // contract-6: tek normal satırın sürümü gönderilir; eski grup (salt okunur) ve satırsız kanal 0.
  assert.equal(incomeSelection([{ kanalId: 7, kanal: 'A', tutarTl: 5, surum: 9 }], { id: 7, ad: 'A' }).surum, 9);
  assert.equal(incomeSelection([{ kanalId: 7, kanal: 'A', tutarTl: 5, surum: 9, eskiYinelenenGrup: true }], { id: 7, ad: 'A' }).surum, 0);
  assert.equal(incomeSelection([{ kanalId: 7, kanal: 'A', tutarTl: 5, surum: 9 }], { id: 8, ad: 'B' }).surum, 0);
});
test('income selection uses stable channel IDs across case variants and renames', () => {
  const rows = [
    { kanalId: 7, kanal: 'SHOP', tutarTl: 100, eskiYinelenenGrup: true },
    { kanalId: 7, kanal: 'shop', tutarTl: 200, eskiYinelenenGrup: true },
    { kanalId: 8, kanal: 'Yeni mağaza', tutarTl: 999 },
  ];
  assert.deepEqual(incomeSelection(rows, { id: 7, ad: 'Yeni mağaza' }), { total: 300, count: 2, readOnly: true, surum: 0 });
  assert.deepEqual(incomeSelection(rows, { id: 8, ad: 'SHOP' }), { total: 999, count: 1, readOnly: false, surum: 0 });
});
test('income name fallback applies only to rows without IDs and matches SQLite ASCII NOCASE', () => {
  const rows = [
    { kanal: 'SHOP', tutarTl: 10 },
    { kanalId: 8, kanal: 'shop', tutarTl: 999 },
  ];
  assert.deepEqual(incomeSelection(rows, { id: 7, ad: 'shop' }), { total: 10, count: 1, readOnly: false, surum: 0 });
  assert.deepEqual(
    incomeSelection(
      [
        { kanal: 'İSİM', tutarTl: 50 },
        { kanal: 'ISIM', tutarTl: 20 },
        { kanal: 'ısım', tutarTl: 90 },
      ],
      { id: 7, ad: 'isim' }
    ),
    { total: 20, count: 1, readOnly: false, surum: 0 }
  );
});
test('income form locks only the duplicate group and unlocks normal channels and new periods', async () => {
  const oldPeriod = '2026-09-07';
  const newPeriod = '2026-09-14';
  const { nodes, app, requests } = await openApp(false, {
    '/api/rapor/haftalik': [{ donem: { start: oldPeriod, end: '2026-09-13' } }, { donem: { start: newPeriod, end: '2026-09-20' } }],
    '/api/kanallar': [
      { id: 7, ad: 'Mağaza' },
      { id: 8, ad: 'Normal' },
    ],
    [`/api/gelenler?donemStart=${oldPeriod}`]: [
      { kanalId: 7, kanal: 'OLD SHOP', tutarTl: 100.1, eskiYinelenenGrup: true },
      { kanalId: 7, kanal: 'old shop', tutarTl: 200.2, eskiYinelenenGrup: true },
    ],
    [`/api/gelenler?donemStart=${newPeriod}`]: [{ kanalId: 7, kanal: 'Mağaza', tutarTl: 42 }],
  });
  await app.incomeDialog(oldPeriod);
  const content = nodes.get('#modal-content');
  const channel = content.find(node => node.attributes.name === 'kanal');
  const period = content.find(node => node.attributes.name === 'donemStart');
  const total = content.find(node => node.attributes.name === 'tutarTl');
  const submit = content.querySelector('button[type="submit"]');
  channel.value = 'Mağaza';
  channel.listeners.change();
  assert.equal(total.value, '300.3');
  assert.equal(total.readOnly, true);
  assert.equal(submit.disabled, true);
  assert.match(content.textContent, /2 eski gelir kaydı/);
  channel.value = 'Normal';
  channel.listeners.change();
  assert.equal(total.value, '0');
  assert.equal(total.readOnly, false);
  assert.equal(submit.disabled, false);
  channel.value = 'Mağaza';
  channel.listeners.change();
  period.value = newPeriod;
  await period.listeners.change();
  assert.equal(total.value, '42');
  assert.equal(total.readOnly, false);
  assert.equal(submit.disabled, false);
  period.value = '2026-09-21';
  await period.listeners.change();
  assert.equal(total.readOnly, true);
  assert.equal(submit.disabled, true);
  assert.match(content.textContent, /yüklenemedi/);
  assert.equal(requests.includes('/api/gelenler'), false);
});

test('optional runtime enables normal v2 only on 404 and reads the deployment capability', async () => {
  let request;
  assert.deepEqual(
    await loadRuntime(async (path, options) => {
      request = { path, options };
      return { status: 404 };
    }),
    { saltOkunur: false, surum: null }
  );
  assert.equal(request.path, '/kasa-runtime.json');
  assert.equal(request.options.cache, 'no-store');
  assert.deepEqual(await loadRuntime(async () => ({ status: 200, ok: true, json: async () => ({ saltOkunur: true, surum: '1.0-web' }) })), {
    saltOkunur: true,
    surum: '1.0-web',
  });
});
test('runtime network, server and malformed config failures never silently enable writes', async () => {
  await assert.rejects(
    loadRuntime(async () => {
      throw new Error('offline');
    })
  );
  await assert.rejects(loadRuntime(async () => ({ status: 503, ok: false })));
  for (const config of [null, {}, { saltOkunur: 'true' }, { saltOkunur: false, surum: 1 }])
    await assert.rejects(loadRuntime(async () => ({ ok: true, status: 200, json: async () => config })));
  assert.equal(runtimeRequestAllowed(null, '/api/auth/login', 'POST'), false);
});
test('read-only runtime allows live cash reads and login/logout but blocks all other writes and unsupported APIs', () => {
  const runtime = { saltOkunur: true };
  for (const path of [
    '/api/auth/me',
    '/api/rapor/panel',
    '/api/rapor/haftalik',
    '/api/rapor/aylik?yil=2026&ay=9',
    '/api/islemler?kanal=A',
    '/api/kanallar',
  ])
    assert.equal(runtimeRequestAllowed(runtime, path), true, path);
  for (const path of ['/api/auth/login', '/api/auth/logout']) assert.equal(runtimeRequestAllowed(runtime, path, 'POST'), true, path);
  for (const path of [
    '/api/alis',
    '/api/surum',
    '/api/ayarlar',
    '/api/alicilar',
    '/api/disari-aktar',
    '/api/yedek/durum',
    '/api/auth/kurtar',
  ])
    assert.equal(runtimeRequestAllowed(runtime, path), false, path);
  for (const method of ['POST', 'PUT', 'PATCH', 'DELETE']) {
    for (const path of [
      '/api/islemler',
      '/api/gelenler',
      '/api/kanallar',
      '/api/alis/1/onayla',
      '/api/yedek',
      '/api/auth/sifre',
      '/api/auth/kurtar',
    ])
      assert.equal(runtimeRequestAllowed(runtime, path, method), false, `${method} ${path}`);
  }
  assert.equal(runtimeRequestAllowed({ saltOkunur: false }, '/api/alis', 'POST'), true);
});
test('read-only navigation contains only four cash views and never grants buyer finance', () => {
  for (const role of ['editor', 'viewer'])
    assert.deepEqual(
      navigationFor(role, { saltOkunur: true }).map(([id]) => id),
      ['home', 'weekly', 'monthly', 'transactions']
    );
  assert.deepEqual(navigationFor('alici', { saltOkunur: true }), []);
});
test('explicit full runtime restores editor transactions and settings without granting viewer or buyer writes', async () => {
  const runtime = await loadRuntime(async () => ({ ok: true, status: 200, json: async () => ({ saltOkunur: false, surum: '2.0.0' }) }));
  assert.deepEqual(
    navigationFor('editor', runtime).map(([id]) => id),
    ['home', 'weekly', 'monthly', 'transactions', 'monthly-expenses', 'cards', 'loans', 'purchases', 'imports', 'notifications', 'tools']
  );
  assert.equal(cashEditingAllowed('editor', runtime), true);
  for (const path of ['/api/gelenler', '/api/islemler', '/api/ayarlar'])
    assert.equal(runtimeRequestAllowed(runtime, path, 'PUT'), true, path);
  for (const role of ['viewer', 'alici']) assert.equal(cashEditingAllowed(role, runtime), false);
  assert.equal(cashEditingAllowed('editor', { saltOkunur: true }), false);
  assert.equal(cashEditingAllowed('editor', null), false);
});
test('legacy monthly report has no pending amount and still computes live totals', () => {
  assert.deepEqual(monthlyTotals({ kanallar: [{ gelen: 300, cariGiden: 40, sabitGider: 20, krediKarti: 10, ortakPay: 5 }] }), {
    incoming: 300,
    expenses: 75,
    result: 225,
  });
});

test('browser entry and helper parse as explicit ES modules before the login screen starts', async () => {
  for (const file of [
    'app.js',
    'ui-core.js',
    'ui-dom.js',
    'ui-shell.js',
    'finance-ui.js',
    'monthly-ui.js',
    'cash-controls-ui.js',
    'statement-import-ui.js',
    'notification-ui.js',
    'push-client.js',
    'service-worker.js',
  ]) {
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
  await logoutAndClear(
    async () => {},
    () => {
      privateState.purchases = [];
    }
  );
  assert.deepEqual(privateState.purchases, []);
});
test('network failure still clears private data and does not claim server logout succeeded', async () => {
  const privateState = { purchases: [{ id: 7 }] };
  await assert.rejects(
    logoutAndClear(
      async () => {
        throw new Error('offline');
      },
      () => {
        privateState.purchases = [];
      }
    ),
    /sunucu oturumu kapatılamadı/
  );
  assert.deepEqual(privateState.purchases, []);
});

test('Turkish decimal input is exact and does not treat grouping as decimals', () => {
  assert.equal(cents(' 1234,56 '), 123456);
  assert.equal(cents('1234.56'), 123456);
  assert.equal(cents('0,01'), 1);
  assert.equal(cents('00001,2'), 120);
  for (const value of ['1.234,56', '1,234.56', '1,234', '1.234', '1e3', '-1', '', 'NaN', 'Infinity'])
    assert.throws(() => cents(value), value);
});
test('money parser retains the accepted domain maximum and rejects overflow', () => {
  assert.equal(cents('999999999999.99'), MAX_CENTS);
  assert.equal(amount('0,10'), 0.1);
  assert.throws(() => cents('1000000000000'));
  assert.throws(() => cents('999999999999999999999999999999'));
  assert.throws(() => cents('0', { allowZero: false }));
  assert.equal(cents('0'), 0);
});
const draft = overrides => ({
  surum: 3,
  tarih: '2026-09-23',
  tedarikci: '  Ödeme yapılan yer  ',
  not: '  Not  ',
  kalemler: [
    {
      aciklama: '  Zeytinyağı  ',
      tutar: '250,05',
      dagilimlar: [
        { kanalId: '2', tutar: '200,00' },
        { kanalId: '3', tutar: '50,05' },
      ],
    },
  ],
  ...overrides,
});
test('simple purchase preserves free text, version, amount and exact channel shares', () => {
  assert.deepEqual(purchasePayload(draft()), {
    surum: 3,
    tarih: '2026-09-23',
    tedarikci: 'Ödeme yapılan yer',
    not: 'Not',
    kalemler: [
      {
        aciklama: 'Zeytinyağı',
        tutar: 250.05,
        dagilimlar: [
          { kanalId: 2, tutar: 200 },
          { kanalId: 3, tutar: 50.05 },
        ],
      },
    ],
  });
});
test('legacy ERP metadata cannot leak into the simplified purchase write contract', () => {
  const input = draft({ tedarikciId: 7, vade: '2026-10-01', hesapId: 5 });
  input.kalemler[0].miktar = 2;
  input.kalemler[0].birimFiyat = 125.025;
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
  assert.throws(() =>
    purchasePayload(
      build([
        { kanalId: 1, tutar: 3 },
        { kanalId: '1', tutar: 7 },
      ])
    )
  );
  assert.throws(() => purchasePayload(build([{ kanalId: '', tutar: 5 }])));
  assert.throws(() => purchasePayload(build([{ kanalId: 1, tutar: 0 }])));
});
test('purchase aggregate cannot exceed API amount cap', () => {
  assert.throws(() =>
    purchasePayload(draft({ kalemler: [1, 2].map(() => ({ aciklama: 'Mal', tutar: '999999999999.99', dagilimlar: [] })) }))
  );
});
test('purchase line count follows the server limit', () => {
  assert.throws(() =>
    purchasePayload(draft({ kalemler: Array.from({ length: 101 }, () => ({ aciklama: 'Mal', tutar: '0', dagilimlar: [] })) }))
  );
});
test('navigation prioritizes cash and excludes ERP screens for every role', () => {
  assert.equal(navigationFor('editor')[0][0], 'home');
  assert.deepEqual(
    navigationFor('alici').map(x => x[0]),
    ['purchases']
  );
  assert.deepEqual(
    navigationFor('viewer').map(x => x[0]),
    ['home', 'weekly', 'monthly', 'transactions', 'monthly-expenses', 'cards', 'loans']
  );
  for (const role of ['editor', 'viewer', 'alici'])
    for (const forbidden of ['accounts', 'debts', 'plan', 'tasks'])
      assert.equal(
        navigationFor(role).some(x => x[0] === forbidden),
        false
      );
});
test('weekly selection does not select a future expense period as the current week', () => {
  const weeks = ['2026-09-14', '2026-09-21', '2026-09-28'].map((start, i) => ({
    donem: { start, end: ['2026-09-20', '2026-09-27', '2026-09-30'][i] },
  }));
  assert.equal(currentPeriod(weeks, '2026-09-23').donem.start, '2026-09-21');
  assert.equal(currentPeriod(weeks, '2026-09-27').donem.start, '2026-09-21');
  assert.equal(currentPeriod([], '2026-09-23'), undefined);
});
test('monthly cash summary includes pending payments once without inventing channel shares', () => {
  const channels = [
    { gelen: 100, cariGiden: 20, sabitGider: 5, krediKarti: 10, ortakPay: 2.5 },
    { gelen: 200, cariGiden: 30, sabitGider: 0, krediKarti: 0, ortakPay: 2.5 },
  ];
  assert.deepEqual(monthlyTotals({ kanallar: channels, dagilimBekleyenTutar: 7.25 }), { incoming: 300, expenses: 77.25, result: 222.75 });
  assert.equal(channels[0].cariGiden, 20);
  assert.deepEqual(monthlyTotals({ kanallar: [{ gelen: 0.3, cariGiden: 0.1, sabitGider: 0.2, krediKarti: 0, ortakPay: 0 }] }), {
    incoming: 0.3,
    expenses: 0.3,
    result: 0,
  });
});
test('buyer capabilities hide finance, approval, payment and lock submitted drafts', () => {
  assert.deepEqual(permissions('alici', { durum: 'Taslak', kalan: 100 }), {
    finance: false,
    edit: true,
    send: true,
    approve: false,
    return: false,
    pay: false,
  });
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
  assert.deepEqual(permissions('viewer', { durum: 'Taslak', kalan: 100 }), {
    finance: false,
    edit: false,
    send: false,
    approve: false,
    return: false,
    pay: false,
  });
});
test('Turkish search handles dotted and dotless I and combines state filters', () => {
  const purchases = [
    { id: 1, tedarikci: 'IŞIK', alici: 'İpek', durum: 'Taslak', kalemler: [{ aciklama: 'Zeytinyağı' }] },
    { id: 2, tedarikci: 'Başka', alici: 'Uğur', durum: 'Incelemede', kalemler: [] },
  ];
  assert.deepEqual(
    filteredPurchases(purchases, 'ışık', '').map(p => p.id),
    [1]
  );
  assert.deepEqual(
    filteredPurchases(purchases, 'ipek', 'Taslak').map(p => p.id),
    [1]
  );
  assert.deepEqual(filteredPurchases(purchases, 'zeytinyağı', 'Incelemede'), []);
});
test('validation errors and authentication/rate-limit failures have useful messages', () => {
  assert.equal(errorMessage({ errors: { tutar: ['Tutar geçersiz'], tarih: ['Tarih gerekli'] } }, 400), 'Tutar geçersiz\nTarih gerekli');
  assert.equal(errorMessage({ hata: 'Sürüm değişti' }, 409), 'Sürüm değişti');
  assert.match(errorMessage(null, 401), /giriş/);
  assert.match(errorMessage(null, 403), /yetki/);
  assert.match(errorMessage(null, 429), /bekleyip/);
  assert.equal(
    errorMessage({ hata: 'Çok fazla deneme yapıldı. 5 dakika sonra yeniden deneyin.' }, 429),
    'Çok fazla deneme yapıldı. 5 dakika sonra yeniden deneyin.'
  );
});
test('only a real 401 ends the session; login/recovery failures and field errors keep it', () => {
  assert.equal(sessionExpired(401, '/api/auth/me'), true);
  assert.equal(sessionExpired(401, '/api/auth/kurtarma-kodu'), true);
  assert.equal(sessionExpired(401, '/api/auth/sifre'), true);
  assert.equal(sessionExpired(401, '/api/auth/login'), false);
  assert.equal(sessionExpired(401, '/api/auth/kurtar'), false);
  assert.equal(sessionExpired(401, '/api/auth/login?yeniden=1'), false);
  assert.equal(sessionExpired(400, '/api/auth/sifre'), false);
  assert.equal(sessionExpired(429, '/api/auth/sifre'), false);
  assert.deepEqual(fieldErrors({ errors: { mevcutSifre: ['Mevcut şifre hatalı.'], tutar: ['A', 'B'] } }), {
    mevcutSifre: 'Mevcut şifre hatalı.',
    tutar: 'A\nB',
  });
  assert.deepEqual(fieldErrors({ hata: 'Sürüm değişti' }), {});
  assert.deepEqual(fieldErrors(null), {});
});
test('viewer password rule matches the server and desktop: 12–1024 characters, not blank', () => {
  assert.equal(VIEWER_PASSWORD_MESSAGE, 'İzleyici şifresi 12–1024 karakter olmalıdır.');
  for (const value of ['', null, 'kisa', 'on-bir-harf', ' '.repeat(12), 'x'.repeat(1025)])
    assert.equal(viewerPasswordError(value), VIEWER_PASSWORD_MESSAGE);
  for (const value of ['on-iki-harf!', 'x'.repeat(1024)]) assert.equal(viewerPasswordError(value), null);
});
test('wrong current password shows a field error and keeps the session; a real 401 on recovery code still ends it', async () => {
  const { app, nodes, calls, responses } = await openApp(false, {
    '/api/auth/sifre': { $status: 400, errors: { mevcutSifre: ['Mevcut şifre hatalı.'] } },
    '/api/auth/kurtarma-kodu': { $status: 401 },
  });
  app.passwordDialog();
  formField(nodes, 'mevcutSifre').value = 'yanlis';
  formField(nodes, 'yeniSifre').value = 'yepyeni-sifre-123';
  formField(nodes, 'tekrar').value = 'yepyeni-sifre-123';
  await submitDialog(nodes);
  assert.equal(nodes.get('#application').hidden, false);
  assert.equal(nodes.get('#login-screen').hidden, true);
  const current = formField(nodes, 'mevcutSifre');
  assert.equal(current.attributes['aria-invalid'], 'true');
  assert.match(current.parentNode.textContent, /Mevcut şifre.*Mevcut şifre hatalı\./);
  assert.equal(nodes.get('#modal-content').find(node => node.attributes.role === 'alert').textContent, 'Mevcut şifre hatalı.');

  // Yeni denemede önceki alan hatası silinir; yalnız yeni hatalı alan işaretlenir.
  responses['/api/auth/sifre'] = { $status: 400, errors: { yeniSifre: ['Yeni şifre 12–1024 karakter olmalıdır.'] } };
  await submitDialog(nodes);
  assert.equal(current.attributes['aria-invalid'], undefined);
  assert.doesNotMatch(current.parentNode.textContent, /hatalı/);
  assert.equal(formField(nodes, 'yeniSifre').attributes['aria-invalid'], 'true');
  assert.equal(nodes.get('#application').hidden, false);
  assert.equal(calls.filter(call => call.path === '/api/auth/sifre').length, 2);

  app.recoveryCodeDialog();
  formField(nodes, 'mevcutSifre').value = 'kasa-sifre';
  await submitDialog(nodes);
  assert.equal(nodes.get('#application').hidden, true);
  assert.equal(nodes.get('#login-screen').hidden, false);
});
// maui-4 (web tarafı): kurtarma kodu tek kullanımlıktır ve şifre değişimi kurtarma kodunu siler; yazım hatalı yeni şifre
// tek editör hesabını kilitler. Tekrar uyuşmazsa istek gönderilmez, uyuşunca sunucuya tekrar alanı gitmez.
test('yeni şifre tekrarı kuralı boşluk dahil birebir eşleşme ister', () => {
  assert.equal(ui.NEW_PASSWORD_MISMATCH_MESSAGE, 'Yeni şifreler aynı olmalı.');
  assert.equal(ui.newPasswordRepeatError('Kasa2026!Guvenli', 'Kasa2026!Guvenli'), null);
  for (const repeat of ['Kasa2026!Guvenlı', 'Kasa2026!Guvenli ', '', null])
    assert.equal(ui.newPasswordRepeatError('Kasa2026!Guvenli', repeat), ui.NEW_PASSWORD_MISMATCH_MESSAGE);
});
test('hesap kurtarmada yeni şifre tekrarı uyuşmazsa kod harcanmaz; eşleşince yalnız kullanıcı, kod ve yeni şifre gider', async () => {
  const { nodes, calls } = await openApp(false, { '/api/auth/kurtar': null });
  nodes.get('#recover-open').listeners.click({});
  formField(nodes, 'kullanici').value = 'editor';
  formField(nodes, 'kod').value = 'ABCD-EFGH';
  formField(nodes, 'yeniSifre').value = 'Kasa2026!Guvenli';
  formField(nodes, 'tekrar').value = 'Kasa2026!Guvenlı';
  assert.equal(formField(nodes, 'tekrar').attributes.type, 'password');
  await submitDialog(nodes);
  assert.equal(calls.filter(call => call.path === '/api/auth/kurtar').length, 0);
  assert.equal(formField(nodes, 'tekrar').attributes['aria-invalid'], 'true');
  assert.equal(nodes.get('#modal-content').find(node => node.attributes.role === 'alert').textContent, 'Yeni şifreler aynı olmalı.');
  formField(nodes, 'tekrar').value = 'Kasa2026!Guvenli';
  await submitDialog(nodes);
  assert.deepEqual(
    calls.filter(call => call.path === '/api/auth/kurtar').map(call => call.body),
    [{ kullanici: 'editor', kod: 'ABCD-EFGH', yeniSifre: 'Kasa2026!Guvenli' }]
  );
  assert.match(nodes.get('#notifications').textContent, /Şifreniz yenilendi/);
});
test('şifre değişiminde tekrar uyuşmazsa istek gönderilmez ve tekrar alanı işaretlenir', async () => {
  const { app, nodes, calls } = await openApp(false, { '/api/auth/sifre': null });
  app.passwordDialog();
  formField(nodes, 'mevcutSifre').value = 'kasa-sifresi';
  formField(nodes, 'yeniSifre').value = 'Kasa2026!Guvenli';
  formField(nodes, 'tekrar').value = 'Kasa2026!Guvenlı';
  await submitDialog(nodes);
  assert.equal(calls.filter(call => call.path === '/api/auth/sifre').length, 0);
  assert.equal(formField(nodes, 'tekrar').attributes['aria-invalid'], 'true');
  assert.equal(nodes.get('#application').hidden, false);
});
test('viewer password is checked with the shared rule before any request and uses the 12 character hint', async () => {
  const { app, nodes, calls } = await openApp(false, { '/api/ayarlar/izleyici-sifre': null });
  app.viewerPasswordDialog();
  const password = formField(nodes, 'yeniSifre');
  assert.equal(password.attributes.minlength, '12');
  assert.match(nodes.get('#modal-content').textContent, /En az 12 karakter kullanın\./);
  password.value = 'on-bir-harf';
  await submitDialog(nodes);
  assert.equal(calls.filter(call => call.path === '/api/ayarlar/izleyici-sifre').length, 0);
  assert.equal(password.attributes['aria-invalid'], 'true');
  assert.match(nodes.get('#modal-content').textContent, /İzleyici şifresi 12–1024 karakter olmalıdır\./);
  password.value = 'on-iki-harf!';
  await submitDialog(nodes);
  assert.deepEqual(
    calls.filter(call => call.path === '/api/ayarlar/izleyici-sifre').map(call => [call.method, call.body]),
    [['PUT', { yeniSifre: 'on-iki-harf!' }]]
  );
  assert.match(nodes.get('#notifications').textContent, /İzleyici şifresi güncellendi/);
});
test('settings show server warnings for a short existing viewer password and an untrusted proxy until they clear', async () => {
  const settings = {
    takipBaslangic: '2026-01-01',
    kasaAcilisDevri: 0,
    izleyiciSifreVarMi: true,
    izleyiciSifreKisa: true,
    vekilUyarisi: 'Sunucu, güvenilmeyen 10.20.0.1 bağlantısından gelen X-Forwarded-For başlığını yok saydı.',
  };
  const { app, nodes } = await openApp(false, {
    '/api/ayarlar': settings,
    '/api/yedek/durum': { otomatikEtkin: true },
    '/api/alicilar': [],
    '/api/kanallar': [],
    '/api/ayarlar/izleyici-sifre': null,
  });
  await app.navigate('tools');
  assert.equal(
    VIEWER_PASSWORD_SHORT_MESSAGE,
    'Mevcut izleyici şifresi 12 karakterden kısa (son izleyici girişinde görüldü). Kurala uygun yeni bir şifre belirleyin.'
  );
  assert.ok(
    nodes.get('#view').find(node => node.textContent === VIEWER_PASSWORD_SHORT_MESSAGE),
    'Kısa izleyici şifresi uyarısı görünür.'
  );
  assert.ok(
    nodes.get('#view').find(node => node.textContent === settings.vekilUyarisi && node.attributes.role === 'alert'),
    'Vekil uyarısı görünür.'
  );

  // Kurala uygun yeni şifre kaydedilince Ayarlar yenilenir; sunucu artık işaretlemediği için uyarılar kalkar.
  settings.izleyiciSifreKisa = false;
  settings.vekilUyarisi = null;
  app.viewerPasswordDialog();
  formField(nodes, 'yeniSifre').value = 'on-iki-harf!';
  await submitDialog(nodes);
  assert.doesNotMatch(nodes.get('#view').textContent, /12 karakterden kısa|X-Forwarded-For/);
  assert.match(nodes.get('#view').textContent, /İzleyici şifresini değiştir/);
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
const monthlyRow = {
  sablonId: 7,
  sablonSurum: 3,
  ad: 'Dükkan kirası',
  tur: 'Kira',
  tutar: 100,
  planlananTarih: `${monthNow}-05`,
  dagilimTuru: 'Genel',
  dagilimlar: [],
  durum: 'Planlandi',
};
const monthlyTemplate = {
  id: 7,
  surum: 3,
  ad: 'Dükkan kirası',
  tur: 'Kira',
  tutar: 100,
  odemeGunu: 5,
  dagilimTuru: 'Genel',
  dagilimlar: [],
  gecerliAy: `${monthNow}-01`,
  aktif: true,
};
const monthlyResponses = (rows = [monthlyRow]) => ({
  [monthlyPath]: { yil: yearNow, ay: monthNumberNow, planlananToplam: 100, odenenToplam: 0, kayitlar: rows },
  '/api/aylik-giderler/sablonlar': [monthlyTemplate],
  '/api/kanallar': [
    { id: 1, ad: 'A', aktif: true },
    { id: 2, ad: 'B', aktif: true },
    { id: 3, ad: 'Yeni', aktif: true },
  ],
});
const clickView = async (nodes, label) => {
  const node = nodes.get('#view').find(item => item.tag === 'button' && item.textContent === label);
  assert.ok(node, `${label} exists`);
  await node.listeners.click({ currentTarget: node });
  await settle();
};

test('monthly plans stay read-only on load and viewers never receive payment or template controls', async () => {
  const { app, nodes, calls } = await openApp(false, { ...monthlyResponses(), '/api/auth/me': { rol: 'viewer' } });
  await app.navigate('monthly-expenses');
  assert.match(nodes.get('#view').textContent, /Şablon ve plan kasa bakiyesini değiştirmez/);
  assert.match(nodes.get('#view').textContent, /Yalnız genel kasa/);
  assert.equal(nodes.get('#page-actions').textContent, '');
  assert.equal(
    nodes.get('#view').find(row => row.tag === 'button' && row.textContent === 'Ödeme kaydet'),
    null
  );
  assert.equal(
    calls.some(call => call.method !== 'GET'),
    false
  );
  assert.throws(() => app.monthlyUi.paymentDialog(monthlyRow, yearNow, monthNumberNow), /editör/);
  const buyer = await openApp(false, { '/api/auth/me': { rol: 'alici' }, '/api/alis/kanallar': [] });
  await assert.rejects(buyer.app.navigate('monthly-expenses'), /erişiminiz yok/);
  assert.equal(
    buyer.calls.some(call => call.path.startsWith('/api/aylik-giderler')),
    false
  );
});

test('monthly template requires an explicit distribution and general-only sends no hidden channel allocations', async () => {
  const { app, nodes, calls } = await openApp(false, monthlyResponses());
  await app.monthlyUi.templateDialog();
  formField(nodes, 'ad').value = 'Kira';
  formField(nodes, 'tutar').value = '100,01';
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.method === 'POST'),
    false
  );
  assert.match(nodes.get('#modal-content').textContent, /hangi kasaya yazılacağını seçin/);
  formField(nodes, 'dagilimTuru').value = 'Genel';
  await submitDialog(nodes);
  const save = calls.find(call => call.method === 'POST');
  assert.equal(save.path, '/api/aylik-giderler/sablonlar');
  assert.deepEqual(save.body.dagilimlar, []);
  assert.equal(save.body.dagilimTuru, 'Genel');
  assert.equal(save.body.tutar, 100.01);
  assert.equal(save.body.gecerliAy, `${monthNow}-01`);
  assert.equal(save.body.surum, 0);
  assert.equal(
    calls.some(call => call.path.endsWith('/ode')),
    false
  );
});

test('equal monthly distribution captures only checked fixed channels and custom amounts must match exactly', async () => {
  const { app, nodes, calls } = await openApp(false, monthlyResponses());
  const selectMode = value => {
    const mode = formField(nodes, 'dagilimTuru');
    mode.value = value;
    mode.listeners.change();
  };
  const check = id => {
    const control = formField(nodes, `dagilim-kanal-${id}`);
    control.checked = true;
    control.listeners.change({ target: control });
  };
  await app.monthlyUi.templateDialog();
  formField(nodes, 'ad').value = 'Maaş';
  formField(nodes, 'tutar').value = '100,01';
  selectMode('Esit');
  check(1);
  check(2);
  await submitDialog(nodes);
  const equal = calls.find(call => call.method === 'POST').body;
  assert.equal(equal.dagilimTuru, 'Esit');
  assert.deepEqual(equal.dagilimlar, [
    { kanalId: 1, tutar: 0 },
    { kanalId: 2, tutar: 0 },
  ]);
  await app.monthlyUi.templateDialog();
  formField(nodes, 'ad').value = 'Fatura';
  formField(nodes, 'tutar').value = '100,01';
  selectMode('Ozel');
  check(1);
  check(2);
  const setTotal = (id, value) => {
    const control = formField(nodes, `dagilim-tutar-${id}`);
    control.value = value;
    control.listeners.input({ target: control });
  };
  setTotal(1, '60');
  setTotal(2, '40');
  await submitDialog(nodes);
  assert.equal(calls.filter(call => call.method === 'POST').length, 1);
  assert.match(nodes.get('#modal-content').textContent, /paylarının toplamı/);
  setTotal(2, '40,01');
  await submitDialog(nodes);
  const custom = calls.filter(call => call.method === 'POST')[1].body;
  assert.deepEqual(custom.dagilimlar, [
    { kanalId: 1, tutar: 60 },
    { kanalId: 2, tutar: 40.01 },
  ]);
});

test('monthly actual payment preserves server amount and identity on retry, then exposes only cancellation', async () => {
  let attempts = 0;
  const data = monthlyResponses();
  const paid = { ...monthlyRow, durum: 'Odendi', odemeId: 11, odemeTarihi: ui.today() };
  const { app, nodes, calls, responses } = await openApp(false, {
    ...data,
    '/api/aylik-giderler/7/ode': () => {
      if (++attempts === 1) throw new Error('Lost response');
      responses[monthlyPath] = { ...data[monthlyPath], odenenToplam: 100, kayitlar: [paid] };
      return paid;
    },
  });
  await app.navigate('monthly-expenses');
  await clickView(nodes, 'Ödeme kaydet');
  assert.equal(formField(nodes, 'tutar'), null);
  assert.match(nodes.get('#modal-content').textContent, /100,00/);
  await submitDialog(nodes);
  await submitDialog(nodes);
  const saves = calls.filter(call => call.path.endsWith('/ode'));
  assert.equal(saves.length, 2);
  assert.deepEqual(saves[0].body, saves[1].body);
  assert.equal(saves[0].body.surum, 3);
  assert.equal(saves[0].body.ay, monthNumberNow);
  assert.equal('tutar' in saves[0].body, false);
  assert.equal(
    nodes.get('#view').find(row => row.tag === 'button' && row.textContent === 'Ödeme kaydet'),
    null
  );
  assert.ok(nodes.get('#view').find(row => row.tag === 'button' && row.textContent === 'Ödemeyi iptal et'));
});

test('monthly payment cancellation keeps its explanation and idempotency key after a lost response', async () => {
  let attempts = 0;
  const { app, nodes, calls } = await openApp(false, {
    '/api/aylik-giderler/odemeler/11/iptal': () => {
      if (++attempts === 1) throw new Error('Lost response');
      return { ...monthlyRow, durum: 'Iptal' };
    },
  });
  app.monthlyUi.cancelDialog({ ...monthlyRow, odemeId: 11, odemeTarihi: ui.today() });
  formField(nodes, 'aciklama').value = 'Yanlış ödeme günü';
  await submitDialog(nodes);
  await submitDialog(nodes);
  const saves = calls.filter(call => call.path.endsWith('/iptal'));
  assert.equal(saves.length, 2);
  assert.deepEqual(saves[0].body, saves[1].body);
  assert.equal(saves[0].body.aciklama, 'Yanlış ödeme günü');
  assert.equal(
    calls.some(call => call.path.startsWith('/api/islemler')),
    false
  );
});

test('cancelled monthly payments stay on the month screen with reason and time, outside the plan and without actions', async () => {
  const plain = await openApp(false, monthlyResponses());
  await plain.app.navigate('monthly-expenses');
  assert.doesNotMatch(plain.nodes.get('#view').textContent, /İptal edilen ödemeler/); // alanı taşımayan eski sunucu da çalışır
  const data = monthlyResponses();
  const cancelled = {
    ...monthlyRow,
    durum: 'Iptal',
    odemeId: 11,
    odemeTarihi: `${monthNow}-05`,
    iptalAciklamasi: 'Kira yanlış aya girildi',
    iptalZamani: '2026-09-25T09:00:00Z',
  };
  const legacy = { ...cancelled, odemeId: 10, iptalAciklamasi: '<b>Eski</b> iptal', iptalZamani: null };
  const { app, nodes, calls } = await openApp(false, { ...data, [monthlyPath]: { ...data[monthlyPath], iptaller: [cancelled, legacy] } });
  await app.navigate('monthly-expenses');
  const section = nodes.get('#view').find(node => node.tag === 'section' && /İptal edilen ödemeler/.test(node.textContent));
  assert.ok(section, 'cancelled payments section exists');
  assert.match(section.textContent, /Kira yanlış aya girildi/);
  assert.ok(section.textContent.includes(new Date('2026-09-25T09:00:00Z').toLocaleString('tr-TR')));
  assert.match(section.textContent, /Sürüm öncesi \(zamanı bilinmiyor\)/);
  assert.match(section.textContent, /<b>Eski<\/b> iptal/);
  assert.equal(
    section.find(node => node.tag === 'b'),
    null
  );
  assert.equal(
    section.find(node => node.tag === 'button'),
    null
  );
  // Plan satırı yine ödeme bekler; iptal kaydı yalnız okunur.
  assert.ok(nodes.get('#view').find(node => node.tag === 'button' && node.textContent === 'Ödeme kaydet'));
  assert.equal(
    calls.some(call => call.method !== 'GET'),
    false
  );
});

test('monthly total includes general-only expenses once and lock control explains the inclusive boundary', async () => {
  const report = {
    yil: yearNow,
    ay: monthNumberNow,
    genelGider: 30,
    dagilimBekleyenTutar: 5,
    kanallar: [{ kanal: 'A', gelen: 100, cariGiden: 10, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: 90 }],
  };
  assert.deepEqual(monthlyTotals(report), { incoming: 100, expenses: 45, result: 55 });
  const { app, nodes, calls } = await openApp(false, {
    [`/api/rapor/aylik?yil=${yearNow}&ay=${monthNumberNow}`]: report,
    '/api/ay-kilidi': { surum: 2, kilitliSonTarih: `${monthNow}-28`, gecmis: [] },
    '/api/ay-kilidi/ac': { surum: 3, kilitliSonTarih: null, gecmis: [] },
  });
  await app.navigate('monthly');
  assert.match(nodes.get('#view').textContent, /Yalnız genel kasa gideri:.*30,00/);
  assert.match(nodes.get('#view').textContent, /dahil geçmiş kasa kayıtları kilitli/);
  await clickView(nodes, 'Bu ayı ve sonrasını aç');
  assert.match(nodes.get('#modal-content').textContent, /sonraki bütün aylar değişikliğe açılacak/);
  formField(nodes, 'aciklama').value = 'Yanlış tarihi düzelteceğim';
  await submitDialog(nodes);
  const write = calls.find(call => call.path.endsWith('/ac'));
  assert.equal(write.body.surum, 2);
  assert.equal(write.body.yil, yearNow);
  assert.equal(write.body.ay, monthNumberNow);
  const viewer = await openApp(false, { '/api/auth/me': { rol: 'viewer' } });
  const panel = await viewer.app.monthlyUi.lockPanel('2026-08', () => {});
  assert.equal(
    panel.find(node => node.tag === 'button'),
    null
  );
});

test('current month cannot be locked in the UI while completed month has an explicit confirmation', async () => {
  const { app, nodes, calls } = await openApp(false, { '/api/ay-kilidi/kapat': { surum: 1, kilitliSonTarih: '2026-08-31', gecmis: [] } });
  const current = await app.monthlyUi.lockPanel(monthNow, () => {});
  assert.equal(
    current.find(node => node.tag === 'button'),
    null
  );
  const past = await app.monthlyUi.lockPanel('2026-08', () => {});
  const button = past.find(node => node.tag === 'button');
  assert.ok(button);
  button.listeners.click();
  assert.match(nodes.get('#modal-content').textContent, /son günü dahil bütün geçmiş/);
  formField(nodes, 'aciklama').value = 'Ağustos kontrol edildi';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path.endsWith('/kapat')).body.ay, 8);
});

test('channel warnings use the server flag and stable ID without subtracting card debt from cash', async () => {
  const { nodes } = await openApp(false, {
    '/api/rapor/panel': {
      guncelKasa: 100,
      buHaftaSonucu: 0,
      buAySonucu: 0,
      kanallar: [
        { kanalId: 1, kanal: 'Yeni ad', bakiye: 100 },
        { kanalId: 2, kanal: 'B', bakiye: 0 },
      ],
    },
    '/api/kasa-esikleri': [
      { kanalId: 1, kanal: 'Eski ad', surum: 1, tutar: 120, etkin: true, bakiye: 100, esikAltinda: true },
      { kanalId: 2, kanal: 'B', surum: 0, tutar: 0, etkin: false, bakiye: 0, esikAltinda: true },
    ],
    '/api/takip/ozet?gun=30': {
      kartBorcu: 80,
      kalanKrediPlani: 0,
      olaylar: [],
      kanalKartBorclari: [{ kanalId: 1, kanal: 'Yeni ad', tutar: 80 }],
    },
  });
  const balances = nodes.get('#view').find(node => node.className === 'channel-balances');
  assert.match(balances.children[0].textContent, /Yeni ad.*100,00.*Alt sınırın altında.*120,00.*80,00/);
  assert.doesNotMatch(balances.children[1].textContent, /Alt sınırın altında/);
  assert.equal(nodes.get('#view').find(node => node.className === 'cash-total money').textContent, money(100));
});

test('channel threshold can be explicitly enabled at zero and negative threshold never sends a write', async () => {
  const row = { kanalId: 1, kanal: 'A', surum: 0, tutar: 0, etkin: false, bakiye: -5, esikAltinda: false };
  const { app, nodes, calls } = await openApp(false, { '/api/kasa-esikleri/1': { ...row, etkin: true, surum: 1 } });
  app.cashControlsUi.thresholdDialog(row);
  formField(nodes, 'tutar').value = '-1';
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.method === 'PUT'),
    false
  );
  formField(nodes, 'tutar').value = '0';
  formField(nodes, 'etkin').checked = true;
  await submitDialog(nodes);
  const save = calls.find(call => call.path === '/api/kasa-esikleri/1');
  assert.deepEqual(save.body, { surum: 0, tutar: 0, etkin: true });
});

test('actual balance comparison previews a signed value and stores only the snapshot with a stable retry key', async () => {
  let attempts = 0;
  const { app, nodes, calls } = await openApp(false, {
    '/api/kasa-kontrol/onizleme': { sistemBakiye: 123, gercekBakiye: -20, fark: -143, kontrolOzeti: 'digest1' },
    '/api/kasa-kontrol': call => {
      if (call.method === 'GET') return [];
      if (++attempts === 1) throw new Error('Lost response');
      return { id: 1 };
    },
  });
  app.cashControlsUi.comparisonDialog();
  formField(nodes, 'gercekBakiye').value = '-20';
  formField(nodes, 'not').value = 'Banka ve nakit kontrolü';
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /123,00.*-₺20,00.*-₺143,00/);
  assert.equal(
    calls.some(call => call.path === '/api/kasa-kontrol' && call.method === 'POST'),
    false
  );
  await submitDialog(nodes);
  await submitDialog(nodes);
  const saves = calls.filter(call => call.path === '/api/kasa-kontrol' && call.method === 'POST');
  assert.equal(saves.length, 2);
  assert.deepEqual(saves[0].body, saves[1].body);
  assert.equal(saves[0].body.kontrolOzeti, 'digest1');
  assert.equal(saves[0].body.gercekBakiye, -20);
  assert.equal(
    calls.some(call => ['/api/ayarlar', '/api/islemler', '/api/gelenler'].includes(call.path)),
    false
  );
});

test('changed cash requires a fresh comparison preview and a second explicit confirmation before save', async () => {
  let previews = 0;
  let saves = 0;
  const { app, nodes, calls } = await openApp(false, {
    '/api/kasa-kontrol/onizleme': () => ({
      sistemBakiye: ++previews === 1 ? 123 : 150,
      gercekBakiye: 200,
      fark: previews === 1 ? 77 : 50,
      kontrolOzeti: `digest${previews}`,
    }),
    '/api/kasa-kontrol': call => (call.method === 'GET' ? [] : ++saves === 1 ? { $status: 409, hata: 'Bakiye değişti.' } : { id: 2 }),
  });
  // Fark sıfırdan farklı: açıklama zorunlu (gap-denetim-izi-gozlemlenebilirlik-3).
  app.cashControlsUi.comparisonDialog();
  formField(nodes, 'gercekBakiye').value = '200';
  formField(nodes, 'not').value = 'Sayım';
  await submitDialog(nodes);
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /Kasa değişti/);
  await submitDialog(nodes);
  assert.equal(saves, 1);
  assert.match(nodes.get('#modal-content').textContent, /150,00.*200,00.*50,00/);
  await submitDialog(nodes);
  assert.equal(saves, 2);
  const writes = calls.filter(call => call.path === '/api/kasa-kontrol' && call.method === 'POST');
  assert.equal(writes[1].body.kontrolOzeti, 'digest2');
  assert.notEqual(writes[0].body.istekId, writes[1].body.istekId);
});

test('a comparison with a difference requires a note in the review step; the note can be written after seeing the difference', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/kasa-kontrol/onizleme': call => ({
      sistemBakiye: 1000,
      gercekBakiye: call.body.gercekBakiye,
      fark: call.body.gercekBakiye - 1000,
      kontrolOzeti: `digest-${call.body.gercekBakiye}`,
      kanalBakiyeleri: [
        { kanalId: 1, kanal: 'MEZAT', bakiye: 600 },
        { kanalId: 2, kanal: 'PERAKENDE', bakiye: 400 },
      ],
      hesapTarihi: '2026-09-26',
    }),
    '/api/kasa-kontrol': call => (call.method === 'GET' ? [] : { id: 3 }),
  });
  app.cashControlsUi.comparisonDialog();
  formField(nodes, 'gercekBakiye').value = '900';
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /MEZAT.*600,00.*PERAKENDE.*400,00/);
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.path === '/api/kasa-kontrol' && call.method === 'POST'),
    false
  );
  assert.match(nodes.get('#modal-content').textContent, /Fark varsa açıklama girin\./);
  formField(nodes, 'not').value = 'Kasada 100 TL eksik';
  await submitDialog(nodes);
  const save = calls.find(call => call.path === '/api/kasa-kontrol' && call.method === 'POST');
  assert.deepEqual([save.body.gercekBakiye, save.body.not, save.body.kontrolOzeti], [900, 'Kasada 100 TL eksik', 'digest-900']);
  // Fark yoksa açıklama gerekmez.
  app.cashControlsUi.comparisonDialog();
  formField(nodes, 'gercekBakiye').value = '1000';
  await submitDialog(nodes);
  await submitDialog(nodes);
  const equal = calls.filter(call => call.path === '/api/kasa-kontrol' && call.method === 'POST')[1];
  assert.deepEqual([equal.body.gercekBakiye, equal.body.not], [1000, null]);
  assert.equal(cashControls.noteRequired(-0.01, ' '), true);
  assert.equal(cashControls.noteRequired(0, ''), false);
  assert.equal(cashControls.noteRequired(5, 'Sayım'), false);
});

test('comparison history marks later changes and legacy rows; editors open the since-list and explain a difference', async () => {
  const changed = {
    id: 2,
    kaydedildi: '2026-09-20T12:00:00+03:00',
    sistemBakiye: 1000,
    gercekBakiye: 900,
    fark: -100,
    not: 'Sayım',
    surum: 1,
    hesapTarihi: '2026-09-20',
    kanalBakiyeleri: [{ kanalId: 1, kanal: 'MEZAT', bakiye: 600, guncelBakiye: 1100 }],
    farkAciklamasi: null,
    farkAciklamaZamani: null,
    guncelSistemBakiye: 1500,
    guncelFark: -600,
    sonradanDegisti: true,
  };
  const legacy = {
    id: 1,
    kaydedildi: '2026-09-01T12:00:00+03:00',
    sistemBakiye: 800,
    gercekBakiye: 800,
    fark: 0,
    not: null,
    surum: 1,
    hesapTarihi: null,
    kanalBakiyeleri: null,
    guncelSistemBakiye: 800,
    guncelFark: 0,
    sonradanDegisti: false,
  };
  const since = {
    kontrolId: 2,
    kaydedildi: changed.kaydedildi,
    esasTarih: '2026-09-20',
    filigranVar: true,
    sistemBakiye: 1000,
    guncelSistemBakiye: 1500,
    bugunkuSistemBakiye: 500,
    kirpildi: false,
    degisiklikler: [
      {
        id: 9,
        zaman: '2026-09-21T10:00:00+03:00',
        aktorRol: 'editor',
        tur: 'Sil',
        varlik: 'Islem',
        varlikId: '812',
        oncekiJson: '{"Tarih":"2026-09-18","TutarTl":500}',
        yeniJson: null,
        gerekce: null,
      },
    ],
    istekler: [{ istekId: '11111111-2222-3333-4444-555555555555', tur: 'KartOdemeIptal', sonucId: 4 }],
    hareketler: [
      {
        etkiTarihi: '2026-09-19',
        kayitTarihi: '2026-09-19',
        tur: 'Gider',
        aciklama: 'Unutulan fatura',
        kanal: 'MEZAT',
        kanalId: 1,
        genelKasaEtkisi: -70,
        kanalEtkisi: -70,
        kaynakAnahtari: 'Islem:900',
        otomatik: false,
      },
      {
        etkiTarihi: '2026-09-22',
        kayitTarihi: '2026-09-22',
        tur: 'KrediTaksidi',
        aciklama: 'Kredi / 1. taksit',
        kanal: 'MEZAT',
        kanalId: 1,
        genelKasaEtkisi: -1000,
        kanalEtkisi: -1000,
        kaynakAnahtari: 'TakipKrediTaksit:9',
        otomatik: true,
      },
    ],
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kasa-kontrol/2/sonrasi': since,
    '/api/kasa-kontrol/2/aciklama': { ...changed, surum: 2, farkAciklamasi: 'Mükerrer gider silindi' },
  });
  const view = app.cashControlsUi.history([changed, legacy]);
  assert.match(view.textContent, /Sonradan değişti: güncel sistem .*1\.500,00, güncel fark -₺600,00/);
  assert.match(view.textContent, /Kayıttan sonra değişmedi · Eski kayıt, filigran yok/);
  const buttons = label => [
    ...(function* walk(node) {
      for (const child of node.children) {
        if (typeof child === 'string') continue;
        if (child.tag === 'button' && child.textContent === label) yield child;
        yield* walk(child);
      }
    })(view),
  ];
  assert.equal(buttons('Farkı açıkla').length, 1); // yalnız farkı ya da sonradan değişimi olan satır
  await buttons('Değişenleri göster')[0].listeners.click({ currentTarget: buttons('Değişenleri göster')[0] });
  await settle();
  const content = nodes.get('#modal-content').textContent;
  for (const text of [
    'Geriye dönük değişim ₺500,00',
    'Silindi',
    'Gider #812',
    'Tarih: 2026-09-18 · TutarTl: 500',
    'KartOdemeIptal #4',
    'Unutulan fatura',
    'Kredi taksidi (kendiliğinden)',
  ])
    assert.ok(content.includes(text), `${text} görünür`);
  assert.match(
    content,
    /Kontrol gününe ya da öncesine sonradan girilen giderler.*Unutulan fatura.*Kontrol gününden bugüne kasaya işleyen hareketler.*Kredi taksidi/
  );
  buttons('Farkı açıkla')[0].listeners.click({});
  formField(nodes, 'aciklama').value = '  Mükerrer gider silindi ';
  await submitDialog(nodes);
  const put = calls.find(call => call.path === '/api/kasa-kontrol/2/aciklama');
  assert.deepEqual(
    [put.method, put.body.surum, put.body.aciklama, typeof put.body.istekId],
    ['PUT', 1, 'Mükerrer gider silindi', 'string']
  );
});

test('cash movements list queries the chosen range and channel and shows opening and closing balances', async () => {
  const path = '/api/kasa-hareketleri?baslangic=2026-09-01&bitis=2026-09-26&kanalId=1';
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [
      { id: 1, ad: 'MEZAT' },
      { id: 2, ad: 'PERAKENDE' },
    ],
    [path]: {
      baslangic: '2026-09-01',
      bitis: '2026-09-26',
      kanalId: 1,
      kanal: 'MEZAT',
      acilisBakiyesi: 1200,
      kapanisBakiyesi: 780.25,
      hareketler: [
        {
          etkiTarihi: '2026-09-30',
          kayitTarihi: '2026-08-20',
          tur: 'KartAySonu',
          aciklama: 'Eski kart',
          kanal: 'MEZAT',
          kanalId: 1,
          genelKasaEtkisi: -400,
          kanalEtkisi: 0,
          kaynakAnahtari: 'Islem:5',
          otomatik: true,
        },
        {
          etkiTarihi: '2026-09-02',
          kayitTarihi: '2026-09-02',
          tur: 'Gider',
          aciklama: 'Nakliye',
          kanal: 'MEZAT',
          kanalId: 1,
          genelKasaEtkisi: -419.75,
          kanalEtkisi: -419.75,
          kaynakAnahtari: 'Islem:6',
          otomatik: false,
        },
      ],
    },
  });
  const openList = view =>
    [
      ...(function* walk(node) {
        for (const child of node.children) {
          if (typeof child === 'string') continue;
          if (child.tag === 'button' && child.textContent === 'Kasa hareket dökümü') yield child;
          yield* walk(child);
        }
      })(view),
    ][0];
  const control = openList(app.cashControlsUi.history([]));
  await control.listeners.click({ currentTarget: control });
  await settle();
  formField(nodes, 'baslangic').value = '2026-09-01';
  formField(nodes, 'bitis').value = '2026-09-26';
  formField(nodes, 'kanalId').value = '1';
  await submitDialog(nodes);
  assert.ok(calls.some(call => call.path === path));
  const content = nodes.get('#modal-content').textContent;
  assert.match(content, /Açılış.*1\.200,00.*Kapanış.*780,25/);
  assert.match(content, /Eski kart ay sonu düşümü \(kendiliğinden\) · kayıt/);
  assert.match(content, /Kanal kasası etkisi/);
  assert.equal(cashControls.movementsPath(), '/api/kasa-hareketleri');
  assert.equal(cashControls.movementsPath({ bitis: '2026-01-31' }), '/api/kasa-hareketleri?bitis=2026-01-31');
});

test('card fee uses a selected cut statement and server shares, then records only after visible confirmation', async () => {
  let attempts = 0;
  const preview = {
    kartId: 4,
    ekstreId: 8,
    tarih: ui.today(),
    tutar: 10.01,
    devredenBorc: 80,
    dagilimlar: [
      { kanalId: 1, kanal: 'A', tutar: 6.01 },
      { kanalId: 2, kanal: 'B', tutar: 4 },
    ],
    dagilimOzeti: 'server-fee-digest',
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/takip/kartlar/4': sampleCard,
    '/api/takip/kartlar/4/masraf-onizleme': preview,
    '/api/takip/kartlar/4/masraflar': () => {
      if (++attempts === 1) throw new Error('Lost response');
      return sampleCard;
    },
  });
  app.financeUi.feeDialog(sampleCard);
  formField(nodes, 'ekstreId').value = '8';
  formField(nodes, 'tutar').value = '10,01';
  formField(nodes, 'aciklama').value = 'Banka faiz tutarı';
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.path.endsWith('/masraflar')),
    false
  );
  assert.match(nodes.get('#modal-content').textContent, /A:.*6,01.*B:.*4,00/);
  await submitDialog(nodes);
  await submitDialog(nodes);
  const writes = calls.filter(call => call.path.endsWith('/masraflar'));
  assert.equal(writes.length, 2);
  assert.deepEqual(writes[0].body, writes[1].body);
  assert.equal(writes[0].body.dagilimOzeti, 'server-fee-digest');
  assert.equal(writes[0].body.istekId, calls.find(call => call.path.endsWith('/masraf-onizleme')).body.istekId);
  assert.equal(
    calls.some(call => call.path.endsWith('/odemeler')),
    false
  );
});

test('unknown fee allocation and cancelled asynchronous preview cannot create a fee or reopen a closed dialog', async () => {
  const { app, nodes, calls, responses } = await openApp(false, {
    '/api/takip/kartlar/4/masraf-onizleme': { $status: 409, hata: 'Kanalı belirsiz borç var.' },
  });
  const fill = () => {
    app.financeUi.feeDialog(sampleCard);
    formField(nodes, 'ekstreId').value = '8';
    formField(nodes, 'tutar').value = '10';
    formField(nodes, 'aciklama').value = 'Masraf';
  };
  fill();
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /Kanalı belirsiz borç var/);
  assert.equal(formField(nodes, 'tutar').value, '10');
  let resolve;
  responses['/api/takip/kartlar/4/masraf-onizleme'] = () =>
    new Promise(done => {
      resolve = done;
    });
  await submitDialog(nodes);
  await clickDialog(nodes, 'Vazgeç');
  resolve({ tutar: 10, devredenBorc: 100, dagilimlar: [], dagilimOzeti: 'late' });
  await settle();
  assert.equal(nodes.get('#modal').open, false);
  assert.equal(
    calls.some(call => call.path.endsWith('/masraflar')),
    false
  );
});

test('monthly payment expenses open the monthly section instead of generic edit or delete', async () => {
  const expense = { id: 21, tarih: ui.today(), cari: 'Kira', kanal: 'Genel kasa', tip: 'SabitGider', tutarTl: 100, aylikGiderOdemeId: 11 };
  const { app, nodes } = await openApp(false, {
    ...monthlyResponses(),
    [`/api/islemler?baslangic=${monthNow}-01&bitis=${ui.today()}`]: [expense],
  });
  await app.navigate('transactions');
  assert.match(nodes.get('#view').textContent, /Aylık Giderler bölümünden yönetilir/);
  assert.equal(
    nodes.get('#view').find(row => row.tag === 'button' && ['Düzenle', 'Sil'].includes(row.textContent)),
    null
  );
  await clickView(nodes, 'Aylık Giderler’i aç');
  assert.equal(nodes.get('#page-title').textContent, 'Aylık Giderler');
});

test('a monthly payment cannot be selected as an existing purchase expense', async () => {
  const { app, nodes } = await openApp(false, {
    '/api/kredikartlari': [],
    '/api/islemler': [
      { id: 21, tarih: ui.today(), cari: 'Kira', tutarTl: 100, aylikGiderOdemeId: 11 },
      { id: 22, tarih: ui.today(), cari: 'Mal', tutarTl: 100 },
    ],
  });
  await app.paymentDialog({ id: 6, surum: 2, kalan: 100, durum: 'Onaylandi' });
  const existing = formField(nodes, 'mevcutIslemId');
  assert.equal(
    existing.find(row => row.tag === 'option' && row.value === '21'),
    null
  );
  assert.ok(existing.find(row => row.tag === 'option' && row.value === '22'));
});

const importRow = (overrides = {}) => ({
  no: 1,
  sayfa: 1,
  kaynakSatir: '23.09.2026 Kira 100,00 TL',
  tarih: '2026-09-23',
  aciklama: 'Kira',
  tutar: 100,
  yon: 'Cikis',
  onerilenIslem: 'Gider',
  sinif: 'Hareket',
  paraBirimi: 'TRY',
  uyarilar: [],
  ...overrides,
});
const importDocument = (overrides = {}) => ({
  id: 12,
  surum: 2,
  kaynak: 'Banka',
  banka: 'Isbank',
  hesapAdi: 'İşletme hesabı',
  kartId: null,
  dosyaAdi: 'hareket.pdf',
  yuklendi: '2026-09-23T10:00:00Z',
  uyarilar: [],
  satirlar: [importRow()],
  kayitlar: [],
  ...overrides,
});
const importResponses = (document = importDocument(), extra = {}) => ({
  '/api/ekstre-aktar': [],
  '/api/ekstre-aktar/12': document,
  '/api/kanallar': [
    { id: 1, ad: 'Mezat', aktif: true },
    { id: 2, ad: 'Mağaza', aktif: true },
  ],
  '/api/takip/kartlar': [sampleCard],
  ...extra,
});
const importPreview = (overrides = {}) => ({
  onizlemeOzeti: 'preview-snapshot',
  kasaEtkisi: -100,
  satirlar: [
    { satirNo: 1, tarih: '2026-09-23', aciklama: 'Kira', tutar: 100, islemTuru: 'Gider', kasaEtkisi: -100, dagilimlar: [], uyarilar: [] },
  ],
  uyarilar: [],
  tekrarOnayGerekli: false,
  ...overrides,
});
const viewField = (nodes, name) => nodes.get('#view').find(node => node.attributes.name === name);
const importRowNode = (nodes, no = 1) =>
  nodes.get('#view').find(node => node.tag === 'article' && node.find(child => child.attributes.name === `sec-${no}`));
async function chooseImportRow(nodes, no = 1, mode = 'Genel') {
  const checked = viewField(nodes, `sec-${no}`);
  checked.checked = true;
  await checked.listeners.change();
  await settle();
  const allocation = importRowNode(nodes, no).find(node => node.attributes.name === 'dagilimTuru');
  if (mode != null) {
    allocation.value = mode;
    allocation.listeners.change();
  }
}

test('PDF import is editor-only and never appears in the read-only cash app', async () => {
  for (const role of ['viewer', 'alici']) {
    const { app, nodes, calls } = await openApp(false, { '/api/auth/me': { rol: role }, '/api/alis/kanallar': [] });
    await assert.rejects(app.navigate('imports'), /erişiminiz yok/);
    assert.doesNotMatch(nodes.get('#navigation').textContent, /Ekstre \/ Hareket Yükle/);
    assert.equal(
      calls.some(call => call.path.startsWith('/api/ekstre-aktar')),
      false
    );
  }
  assert.equal(
    navigationFor('editor', { saltOkunur: true }).some(([key]) => key === 'imports'),
    false
  );
});

test('opening a parsed PDF leaves every movement unchecked, shows its source and writes nothing', async () => {
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(
      importDocument({
        satirlar: [
          importRow(),
          importRow({ no: 2, aciklama: '<img src=x onerror=alert(1)>', kaynakSatir: '<script>bad()</script>', sinif: 'Faiz' }),
        ],
      })
    )
  );
  await app.navigate('imports', 12);
  assert.equal(viewField(nodes, 'sec-1').checked, false);
  assert.equal(viewField(nodes, 'sec-2').checked, false);
  assert.match(nodes.get('#view').textContent, /0 satır seçili/);
  assert.match(nodes.get('#view').textContent, /<img src=x onerror=alert\(1\)>/);
  assert.equal(
    nodes.get('#view').find(node => ['script', 'img'].includes(node.tag)),
    null
  );
  assert.equal(nodes.get('#view').find(node => node.tag === 'a').attributes.href, '/api/ekstre-aktar/12/dosya');
  assert.equal(
    calls.some(call => call.method === 'POST'),
    false
  );
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(
    calls.some(call => call.method === 'POST'),
    false
  );
});

test('PDF upload exposes all six banks, sends only selected source metadata and does not post finance', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar/yukle': importDocument() }));
  await app.navigate('imports');
  await clickView(nodes, 'Kart ekstresi / hesap hareketi seç');
  assert.match(formField(nodes, 'banka').textContent, /VakıfBank.*Akbank.*QNB.*İş Bankası.*Garanti BBVA.*DenizBank/);
  const source = formField(nodes, 'kaynak');
  source.value = 'Banka';
  source.listeners.change();
  assert.equal(formField(nodes, 'kartId').disabled, true);
  formField(nodes, 'banka').value = 'QNB';
  formField(nodes, 'hesapAdi').value = '  İşletme  ';
  formField(nodes, 'dosya').files = [{ name: 'bank.pdf', type: 'application/pdf', size: 1200 }];
  await submitDialog(nodes);
  const request = calls.find(call => call.path.endsWith('/yukle'));
  assert.equal(request.body.kaynak, 'Banka');
  assert.equal(request.body.banka, 'QNB');
  assert.equal(request.body.hesapAdi, 'İşletme');
  assert.equal(request.body.kartId, undefined);
  assert.equal(calls.filter(call => call.method === 'POST').length, 1);
  assert.equal(viewField(nodes, 'sec-1').checked, false);
});

test('PDF upload refuses wrong type and oversized files before calling the server', async () => {
  const { app, nodes, calls } = await openApp(false, importResponses());
  await app.navigate('imports');
  await clickView(nodes, 'Kart ekstresi / hesap hareketi seç');
  formField(nodes, 'banka').value = 'Akbank';
  formField(nodes, 'kartId').value = '4';
  for (const file of [
    { name: 'statement.pdf', type: 'application/pdf', size: 10 * 1024 * 1024 + 1 },
    { name: 'statement.html', type: 'text/html', size: 100 },
  ]) {
    formField(nodes, 'dosya').files = [file];
    await submitDialog(nodes);
  }
  assert.equal(
    calls.some(call => call.path.endsWith('/yukle')),
    false
  );
});

test('PDF selected rows require a server impact preview and a separate save action', async () => {
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument({ satirlar: [importRow(), importRow({ no: 2, tutar: 200 })] }), {
      '/api/ekstre-aktar/12/onizleme': importPreview(),
      '/api/ekstre-aktar/12/kaydet': importDocument(),
    })
  );
  await app.navigate('imports', 12);
  await chooseImportRow(nodes);
  await clickView(nodes, 'Seçilenleri önizle');
  const request = calls.find(call => call.path.endsWith('/onizleme'));
  assert.equal(request.body.satirlar.length, 1);
  assert.equal(request.body.satirlar[0].satirNo, 1);
  assert.equal(request.body.satirlar[0].dagilimTuru, 'Genel');
  assert.deepEqual(request.body.satirlar[0].dagilimlar, []);
  assert.equal(
    calls.some(call => call.path.endsWith('/kaydet')),
    false
  );
  assert.match(nodes.get('#view').textContent, /Genel kasa değişimi.*-₺100,00/);
  await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(calls.find(call => call.path.endsWith('/kaydet')).body.onizlemeOzeti, 'preview-snapshot');
});

test('PDF custom allocation requires exact cents and equal shares contain only explicitly chosen channels', async () => {
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument({ kaynak: 'Kart', kartId: 4, satirlar: [importRow({ onerilenIslem: 'KartHarcama', sinif: 'Faiz' })] }), {
      '/api/ekstre-aktar/12/onizleme': importPreview({ kasaEtkisi: 0 }),
    })
  );
  await app.navigate('imports', 12);
  await chooseImportRow(nodes, 1, 'Ozel');
  const row = importRowNode(nodes);
  const first = row.find(node => node.attributes.name === 'pay-kanal-1');
  first.checked = true;
  first.listeners.change();
  row.find(node => node.attributes.name === 'pay-tutar-1').value = '99,99';
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(
    calls.some(call => call.path.endsWith('/onizleme')),
    false
  );
  const mode = row.find(node => node.attributes.name === 'dagilimTuru');
  mode.value = 'Esit';
  mode.listeners.change();
  await clickView(nodes, 'Seçilenleri önizle');
  const saved = calls.find(call => call.path.endsWith('/onizleme')).body.satirlar[0];
  assert.equal(saved.islemTuru, 'KartHarcama');
  assert.equal(saved.krediKartiId, 4);
  assert.deepEqual(saved.dagilimlar, [{ kanalId: 1, tutar: 0 }]);
});

test('PDF duplicate and ambiguity warnings require explicit confirmation before commit', async () => {
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument(), {
      '/api/ekstre-aktar/12/onizleme': importPreview({ tekrarOnayGerekli: true, uyarilar: ['Benzer kayıt bulundu.'] }),
      '/api/ekstre-aktar/12/kaydet': importDocument(),
    })
  );
  await app.navigate('imports', 12);
  await chooseImportRow(nodes);
  await clickView(nodes, 'Seçilenleri önizle');
  await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(
    calls.some(call => call.path.endsWith('/kaydet')),
    false
  );
  viewField(nodes, 'tekrarOnay').checked = true;
  await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(calls.find(call => call.path.endsWith('/kaydet')).body.tekrarOnay, true);
});

test('PDF silent field change cannot reuse an old financial preview', async () => {
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument(), { '/api/ekstre-aktar/12/onizleme': importPreview() })
  );
  await app.navigate('imports', 12);
  await chooseImportRow(nodes);
  await clickView(nodes, 'Seçilenleri önizle');
  viewField(nodes, 'tutar-1').value = '101';
  await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(
    calls.some(call => call.path.endsWith('/kaydet')),
    false
  );
  assert.match(nodes.get('#alerts').textContent, /Yeniden önizleyin/);
});

test('PDF uncertain save retries retain the idempotency key and a conflict requires new preview', async () => {
  let saves = 0;
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument(), {
      '/api/ekstre-aktar/12/onizleme': importPreview(),
      '/api/ekstre-aktar/12/kaydet': () => {
        saves++;
        if (saves < 3) throw new Error('offline');
        return { $status: 409, hata: 'Kayıt değişti' };
      },
    })
  );
  await app.navigate('imports', 12);
  await chooseImportRow(nodes);
  await clickView(nodes, 'Seçilenleri önizle');
  await clickView(nodes, 'Onayla ve kaydet');
  await clickView(nodes, 'Onayla ve kaydet');
  const attempts = calls.filter(call => call.path.endsWith('/kaydet'));
  assert.equal(attempts[0].body.istekId, attempts[1].body.istekId);
  await clickView(nodes, 'Onayla ve kaydet');
  await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(saves, 3);
  assert.equal(viewField(nodes, 'sec-1').checked, true);
});

test('PDF foreign-currency and already imported rows stay disabled and cannot be forged into preview', async () => {
  const document = importDocument({
    satirlar: [importRow({ paraBirimi: 'EUR' }), importRow({ no: 2 })],
    kayitlar: [
      {
        id: 9,
        satirNo: 2,
        tarih: '2026-09-23',
        aciklama: 'Önceki',
        tutar: 100,
        islemTuru: 'Gider',
        dagilimTuru: 'Genel',
        dagilimlar: [],
        iptal: false,
      },
    ],
  });
  const { app, nodes, calls } = await openApp(false, importResponses(document));
  await app.navigate('imports', 12);
  assert.equal(viewField(nodes, 'sec-1').disabled, true);
  assert.equal(viewField(nodes, 'sec-2').disabled, true);
  viewField(nodes, 'sec-1').checked = true;
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(
    calls.some(call => call.path.endsWith('/onizleme')),
    false
  );
});

test('PDF bank card payment requires the intended card and automatically sourced allocation', async () => {
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument({ satirlar: [importRow({ onerilenIslem: 'KartOdemesi' })] }), {
      '/api/ekstre-aktar/12/onizleme': importPreview(),
      '/api/takip/kartlar': [{ ...sampleCard, aktif: false }],
    })
  );
  await app.navigate('imports', 12);
  await chooseImportRow(nodes, 1, null);
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(
    calls.some(call => call.path.endsWith('/onizleme')),
    false
  );
  assert.match(viewField(nodes, 'kart-1').textContent, /Yeni kullanıma kapalı/);
  viewField(nodes, 'kart-1').value = '4';
  await clickView(nodes, 'Seçilenleri önizle');
  const row = calls.find(call => call.path.endsWith('/onizleme')).body.satirlar[0];
  assert.equal(row.krediKartiId, 4);
  assert.equal(row.dagilimTuru, 'Otomatik');
  assert.deepEqual(row.dagilimlar, []);
});

test('PDF card refund requires its original charge and preserves source binding', async () => {
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument({ kaynak: 'Kart', kartId: 4, satirlar: [importRow({ onerilenIslem: 'KartIade', yon: 'Giris' })] }), {
      '/api/takip/kartlar/4': {
        ...sampleCard,
        harcamalar: [
          { id: 18, tutar: 100, tarih: '2026-09-23', aciklama: 'Mal', iptal: false },
          { id: 19, tutar: 100, iptal: true },
          { id: 20, tutar: -20, iptal: false },
        ],
      },
      '/api/ekstre-aktar/12/onizleme': importPreview(),
    })
  );
  await app.navigate('imports', 12);
  await chooseImportRow(nodes, 1, null);
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(
    calls.some(call => call.path.endsWith('/onizleme')),
    false
  );
  const source = viewField(nodes, 'iade-kaynak-1');
  assert.ok(source.find(node => node.value === '18'));
  assert.equal(
    source.find(node => node.value === '19'),
    null
  );
  source.value = '18';
  await clickView(nodes, 'Seçilenleri önizle');
  const row = calls.find(call => call.path.endsWith('/onizleme')).body.satirlar[0];
  assert.equal(row.kaynakHarcamaId, 18);
  assert.equal(row.krediKartiId, 4);
  assert.equal(row.dagilimTuru, 'Otomatik');
});

test('leaving PDF import while preview is pending cannot display or save its late result', async () => {
  let release;
  const pending = new Promise(resolve => {
    release = resolve;
  });
  const { app, nodes, calls } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar/12/onizleme': () => pending }));
  await app.navigate('imports', 12);
  await chooseImportRow(nodes);
  const button = nodes.get('#view').find(node => node.tag === 'button' && node.textContent === 'Seçilenleri önizle');
  const work = button.listeners.click({ currentTarget: button });
  await settle();
  await app.navigate('home');
  release(importPreview());
  await work;
  await settle();
  assert.doesNotMatch(nodes.get('#view').textContent, /Onayla ve kaydet/);
  assert.equal(
    calls.some(call => call.path.endsWith('/kaydet')),
    false
  );
});

test('clearing all PDF selections removes every selected row and invalidates preview', async () => {
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument(), { '/api/ekstre-aktar/12/onizleme': importPreview() })
  );
  await app.navigate('imports', 12);
  await chooseImportRow(nodes);
  await clickView(nodes, 'Seçilenleri önizle');
  await clickView(nodes, 'Seçimleri temizle');
  await clickView(nodes, 'Onayla ve kaydet');
  assert.equal(viewField(nodes, 'sec-1').checked, false);
  assert.match(nodes.get('#view').textContent, /0 satır seçili/);
  assert.equal(
    calls.some(call => call.path.endsWith('/kaydet')),
    false
  );
});

test('PDF history cancellation requires an explanation and retains its retry identity', async () => {
  const document = importDocument({
    kayitlar: [
      {
        id: 9,
        satirNo: 1,
        tarih: '2026-09-23',
        aciklama: 'Kira',
        tutar: 100,
        islemTuru: 'Gider',
        dagilimTuru: 'Genel',
        dagilimlar: [],
        iptal: false,
      },
    ],
  });
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(document, { '/api/ekstre-aktar/12/kayitlar/9/iptal': new Error('offline') })
  );
  await app.navigate('imports', 12);
  await clickView(nodes, 'Kaydı iptal et');
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.path.endsWith('/iptal')),
    false
  );
  formField(nodes, 'aciklama').value = 'Yanlış satır seçildi';
  await submitDialog(nodes);
  await submitDialog(nodes);
  const requests = calls.filter(call => call.path.endsWith('/iptal'));
  assert.equal(requests.length, 2);
  assert.equal(requests[0].body.istekId, requests[1].body.istekId);
});

test('PDF history shows the cancellation reason and time on cancelled records only', async () => {
  const record = {
    satirNo: 1,
    tarih: '2026-09-23',
    aciklama: 'Kira',
    tutar: 100,
    islemTuru: 'Gider',
    dagilimTuru: 'Genel',
    dagilimlar: [],
  };
  const document = importDocument({
    kayitlar: [
      { ...record, id: 9, iptal: true, iptalAciklamasi: 'Banka hareketi iki kez okundu', iptalZamani: '2026-09-25T09:00:00Z' },
      { ...record, id: 8, iptal: true, iptalAciklamasi: 'Sürüm öncesi iptal', iptalZamani: null },
      { ...record, id: 7, satirNo: 2, iptal: false },
    ],
  });
  const { app, nodes } = await openApp(false, importResponses(document));
  await app.navigate('imports', 12);
  const history = nodes.get('#view').find(node => node.tag === 'section' && /Bu belgeden kaydedilenler/.test(node.textContent));
  const row = satirNo =>
    history.find(node => node.tag === 'tr' && node.textContent.includes(`#${satirNo} ·`) && node.find(child => child.tag === 'td'));
  assert.ok(history.textContent.includes(`Banka hareketi iki kez okundu · ${new Date('2026-09-25T09:00:00Z').toLocaleString('tr-TR')}`));
  assert.match(history.textContent, /Sürüm öncesi iptal · zamanı bilinmiyor/);
  assert.equal(history.find(node => node.tag === 'button' && node.textContent === 'Kaydı iptal et') !== null, true);
  assert.match(row(2).textContent, /Kaydedildi/);
  assert.doesNotMatch(row(2).textContent, /zamanı bilinmiyor|iki kez okundu/);
});

test('imported cash expenses have a source link and can be linked to a purchase payment as bank statement expenses', async () => {
  // gap-coklu-giris-cift-sayim-mutabakat-1: sunucu banka ekstresi giderini bağlanabilir listeler; bağlanınca ekstre satırı eşleşmeye döner.
  const expenses = [
    { id: 21, tarih: ui.today(), cari: 'PDF Kira', tutarTl: 100, ekstreKayitId: 8 },
    { id: 22, tarih: ui.today(), cari: 'Mal', tutarTl: 100 },
  ];
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument(), {
      '/api/kredikartlari': [],
      '/api/islemler': expenses,
      [`/api/islemler?baslangic=${ui.today().slice(0, 8)}01&bitis=${ui.today()}`]: expenses,
      '/api/alis/6/odemeler': { id: 6 },
    })
  );
  await app.navigate('transactions');
  assert.match(nodes.get('#view').textContent, /Ekstre \/ Hareket Yükle bölümünden yönetilir/);
  const row = nodes.get('#view').find(node => node.tag === 'tr' && node.textContent.includes('PDF Kira'));
  assert.doesNotMatch(row.textContent, /Düzenle|Sil/);
  await app.paymentDialog({ id: 6, surum: 2, kalan: 100, durum: 'Onaylandi' });
  const existing = formField(nodes, 'mevcutIslemId');
  assert.match(existing.find(row => row.tag === 'option' && row.value === '21').textContent, /PDF Kira.*banka ekstresinden/);
  existing.value = '21';
  existing.listeners.change();
  await settle();
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/alis/6/odemeler').body.mevcutIslemId, 21);
});

test('takipli card payment lists card charges without an expense and links the chosen one without a duplicate check', async () => {
  const purchase = { id: 6, surum: 2, kalan: 18000, durum: 'Taslak' };
  const charge = { id: 31, krediKartiId: 4, tarih: '2026-09-24', aciklama: 'MEZAT', tutar: 18000, ekstreKayitId: 7 };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kredikartlari': [{ id: 4, ad: 'İş kartı', yeniTakip: true, aktif: true }],
    '/api/islemler': [],
    '/api/alis/6/odemeler': purchase,
    '/api/alis/kanallar': [],
    '/api/islemler/benzerlik': new Error('Bağlamada benzerlik sorulmaz'),
    '/api/alis/baglanabilir-kart-harcamalari?krediKartiId=4&tutar=18000.00': [charge],
  });
  await app.paymentDialog(purchase);
  const card = formField(nodes, 'krediKartiId');
  const linked = formField(nodes, 'mevcutKartHarcamaId');
  assert.equal(linked.closest('label').hidden, true);
  card.value = '4';
  await card.listeners.change();
  await settle();
  assert.equal(linked.closest('label').hidden, false);
  assert.match(linked.find(node => node.tag === 'option' && node.value === '31').textContent, /MEZAT.*18\.000,00.*ekstreden/);
  linked.value = '31';
  linked.listeners.change();
  assert.equal(formField(nodes, 'tarih').value, '2026-09-24');
  assert.equal(formField(nodes, 'tutar').disabled, true);
  await submitDialog(nodes);
  const save = calls.find(call => call.path === '/api/alis/6/odemeler');
  assert.equal(save.body.mevcutKartHarcamaId, 31);
  assert.equal(save.body.krediKartiId, 4);
  assert.equal(save.body.tarih, '2026-09-24');
  assert.equal(save.body.tutar, 18000);
  assert.equal(save.body.mevcutIslemId, null);
  assert.equal(
    calls.some(call => call.path === '/api/islemler/benzerlik'),
    false
  );
});

test('PDF row can be matched to an existing record without allocation and shows the match in history', async () => {
  const candidate = {
    tur: 'KartHarcama',
    id: 12,
    tarih: '2026-09-22',
    tutar: 100,
    aciklama: 'Alış ödemesi',
    krediKartiId: 4,
    kanalEtiketi: 'Dağılım bekliyor',
    alisId: 6,
  };
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument({ kaynak: 'Kart', kartId: 4, satirlar: [importRow({ onerilenIslem: 'KartHarcama' })] }), {
      '/api/ekstre-aktar/12/eslesme-adaylari': [candidate],
      '/api/ekstre-aktar/12/onizleme': importPreview({
        kasaEtkisi: 0,
        satirlar: [
          {
            satirNo: 1,
            tarih: '2026-09-23',
            aciklama: 'Kira',
            tutar: 100,
            islemTuru: 'Eslestir',
            kasaEtkisi: 0,
            dagilimlar: [],
            uyarilar: [],
          },
        ],
      }),
    })
  );
  await app.navigate('imports', 12);
  await chooseImportRow(nodes, 1, null);
  const kind = viewField(nodes, 'tur-1');
  kind.value = 'Eslestir';
  await kind.listeners.change();
  await settle();
  const lookup = calls.find(call => call.path === '/api/ekstre-aktar/12/eslesme-adaylari');
  assert.deepEqual(lookup.body, { tarih: '2026-09-23', tutar: 100 });
  const match = viewField(nodes, 'eslesme-1');
  assert.match(match.find(node => node.value === 'KartHarcama:12').textContent, /Kart harcaması #12.*Alış #6/);
  assert.equal(importRowNode(nodes).find(node => node.tag === 'fieldset').hidden, true);
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(
    calls.some(call => call.path.endsWith('/onizleme')),
    false
  );
  match.value = 'KartHarcama:12';
  match.listeners.change();
  await clickView(nodes, 'Seçilenleri önizle');
  const row = calls.find(call => call.path.endsWith('/onizleme')).body.satirlar[0];
  assert.deepEqual(
    [row.islemTuru, row.dagilimTuru, row.dagilimlar, row.eslesenKayitTuru, row.eslesenKayitId, row.krediKartiId],
    ['Eslestir', 'Eslesme', [], 'KartHarcama', 12, null]
  );
  assert.match(nodes.get('#view').textContent, /Mevcut kayıtla eşleşir; kasa ve kart borcu değişmez/);

  const saved = importDocument({
    kaynak: 'Kart',
    kartId: 4,
    kayitlar: [
      {
        id: 9,
        satirNo: 1,
        tarih: '2026-09-23',
        aciklama: 'Kira',
        tutar: 100,
        islemTuru: 'Eslestir',
        dagilimTuru: 'Eslesme',
        dagilimlar: [],
        iptal: false,
        eslesmeTuru: 'KartHarcama',
        eslesmeId: 12,
        eslesmeDurumu: 'Eslesti',
      },
    ],
  });
  const second = await openApp(false, importResponses(saved));
  await second.app.navigate('imports', 12);
  assert.match(second.nodes.get('#view').textContent, /Mevcut kayıtla eşleşti \(Kart harcaması #12\)/);
  assert.match(second.nodes.get('#view').textContent, /Kasa etkisi yok/);
  assert.equal(viewField(second.nodes, 'sec-1').disabled, true);
});

test('imported card charges and payments expose source navigation instead of generic cancellation', async () => {
  const card = {
    ...sampleCard,
    harcamalar: [{ id: 10, tarih: '2026-09-23', aciklama: 'PDF faiz', tutar: 100, taksitSayisi: 1, dagilimlar: [], ekstreKayitId: 9 }],
    odemeler: [{ id: 11, tarih: '2026-09-23', tutar: 50, kasaEtkisi: 50, dagilimlar: [], ekstreKayitId: 10 }],
  };
  const { app, nodes } = await openApp(false, { '/api/takip/kartlar/4': card });
  await app.navigate('cards', 4);
  assert.match(nodes.get('#view').textContent, /PDF yüklemesinden kaydedildi/);
  assert.doesNotMatch(nodes.get('#view').textContent, /İptal et|Ödemeyi iptal et/);
});

test('monthly reports add general-only imported income exactly once without inventing channel income', () => {
  assert.deepEqual(
    monthlyTotals({
      genelGelir: 123.45,
      genelGider: 10,
      kanallar: [{ gelen: 50, cariGiden: 1, sabitGider: 2, krediKarti: 3, ortakPay: 4 }],
    }),
    { incoming: 173.45, expenses: 20, result: 153.45 }
  );
});

test('PDF history reaches older than fifty documents using a stable descending cursor', async () => {
  const latest = Array.from({ length: 50 }, (_, index) => ({
    ...importDocument(),
    id: 100 - index,
    dosyaAdi: `Belge-${100 - index}.pdf`,
    satirSayisi: 1,
    kayitSayisi: 0,
  }));
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument(), {
      '/api/ekstre-aktar': latest,
      '/api/ekstre-aktar?beforeId=51': [{ ...importDocument(), id: 12, satirSayisi: 1, kayitSayisi: 0 }],
    })
  );
  await app.navigate('imports');
  await clickView(nodes, 'Daha eski belgeler');
  assert.ok(calls.some(call => call.path === '/api/ekstre-aktar?beforeId=51'));
  assert.match(nodes.get('#view').textContent, /hareket.pdf/);
  assert.doesNotMatch(nodes.get('#view').textContent, /Daha eski belgeler/);
  await clickView(nodes, 'Aç ve incele');
  assert.ok(viewField(nodes, 'sec-1'));
  await app.navigate('imports', { beforeId: 51 });
  await clickView(nodes, 'En yeni belgelere dön');
  assert.match(nodes.get('#view').textContent, /Belge-100.pdf/);
});

test('imported expense opens its exact parent PDF even when absent from recent history', async () => {
  const expenses = [{ id: 21, tarih: ui.today(), cari: 'PDF kira', tutarTl: 100, ekstreKayitId: 807 }];
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument(), {
      [`/api/islemler?baslangic=${ui.today().slice(0, 8)}01&bitis=${ui.today()}`]: expenses,
      '/api/ekstre-aktar/kayitlar/807': importDocument({ dosyaAdi: 'Eski-kira.pdf' }),
    })
  );
  await app.navigate('transactions');
  await clickView(nodes, 'Kaynak belgeyi aç');
  assert.ok(calls.some(call => call.path === '/api/ekstre-aktar/kayitlar/807'));
  assert.equal(
    calls.some(call => call.path === '/api/ekstre-aktar'),
    false
  );
  assert.match(nodes.get('#view').textContent, /Eski-kira.pdf/);
  assert.equal(viewField(nodes, 'sec-1').checked, false);
});

test('imported card payment opens the record source instead of unrelated recent PDFs', async () => {
  const card = {
    ...sampleCard,
    odemeler: [{ id: 19, tarih: ui.today(), tutar: 100, kasaEtkisi: 100, dagilimlar: [], ekstreKayitId: 909 }],
  };
  const { app, nodes, calls } = await openApp(
    false,
    importResponses(importDocument(), {
      '/api/takip/kartlar/4': card,
      '/api/ekstre-aktar/kayitlar/909': importDocument({ dosyaAdi: 'Kart-odeme.pdf' }),
    })
  );
  await app.navigate('cards', 4);
  await clickView(nodes, 'Kaynak belgeyi aç');
  assert.ok(calls.some(call => call.path === '/api/ekstre-aktar/kayitlar/909'));
  assert.match(nodes.get('#view').textContent, /Kart-odeme.pdf/);
});

test('late old-history response cannot overwrite a newly opened PDF', async () => {
  let release;
  const delayed = new Promise(resolve => {
    release = resolve;
  });
  const { app, nodes } = await openApp(false, importResponses(importDocument(), { '/api/ekstre-aktar?beforeId=51': () => delayed }));
  const older = app.navigate('imports', { beforeId: 51 });
  await settle();
  await app.navigate('imports', 12);
  release([]);
  await older;
  assert.ok(viewField(nodes, 'sec-1'));
  assert.doesNotMatch(nodes.get('#view').textContent, /Daha eski belge yok/);
});

test('old card transition prefills the suggested counted amount, shows pending old deductions and re-previews with the suggestion', async () => {
  const oldCard = { ...sampleCard, yeniTakip: false, surum: 0, borc: 1500, ekstreler: [] };
  const preview = call => {
    const counted = call.body.kasadaOncedenSayilanTutar;
    const diff = counted - 1200;
    return {
      kaynak: 'Kart',
      kaynakId: 4,
      baslangic: call.body.baslangic,
      genelKasaAnlikFarki: diff,
      kanalAnlikFarki: 0,
      eskiKasadaSayilanTutar: counted,
      aciklamalar: ['1.200,00 TL eski kart gideri eski kuralla 31.10.2026 tarihine kadar ay sonlarında düşmeye devam eder.'],
      kabulEdilebilir: diff <= 0 && counted >= 1000,
      sistemKartBorcu: 1500,
      eskiKuraldaIslenenTutar: 300,
      bekleyenEskiDusumTutari: 1200,
      sonBekleyenDusumTarihi: '2026-10-31',
      onerilenKasadaSayilanTutar: 1200,
      enAzKasadaSayilanTutar: 1000,
    };
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'MEZAT', aktif: true }],
    '/api/takip/kartlar/4/gecis-onizleme': preview,
    '/api/takip/kartlar/4/gecis': sampleCard,
    '/api/takip/kartlar/4': sampleCard,
  });
  await app.financeUi.cardTransition(oldCard);
  const debt = formField(nodes, 'kalanBorc');
  const counted = formField(nodes, 'kasadaOncedenSayilanTutar');
  assert.equal(debt.value, '1500');
  assert.equal(counted.value, '1500');
  debt.value = '1200';
  debt.listeners.input();
  assert.equal(counted.value, '1200');
  counted.value = '700';
  counted.listeners.input();
  debt.value = '1300';
  debt.listeners.input();
  assert.equal(counted.value, '700');
  formField(nodes, 'aciklama').value = 'Banka ekstresiyle doğrulandı';
  await submitDialog(nodes);
  const first = calls.filter(call => call.path.endsWith('/gecis-onizleme'))[0];
  assert.equal(first.body.kasadaOncedenSayilanTutar, 700);
  assert.equal(first.body.kalanBorc, 1300);
  assert.equal(first.body.onay, false);
  const text = nodes.get('#modal-content').textContent;
  assert.match(text, /Sistem kart borcu.*1\.500,00/);
  assert.match(text, /Başlangıçtan önce düşen\/düşecek.*300,00/);
  assert.doesNotMatch(text, /Eski kuralla işlenen/);
  assert.match(text, /Bekleyen eski düşüm.*1\.200,00/);
  assert.ok(text.includes(`Son düşüm ${dateText('2026-10-31')}`));
  assert.match(text, /Önerilen önceden sayılan.*1\.200,00.*En az.*1\.000,00/);
  assert.match(text, /Genel kasa farkı.*-?.*500,00/);
  await clickDialog(nodes, `Önerilen tutarla (${money(1200)}) yeniden önizle`);
  const second = calls.filter(call => call.path.endsWith('/gecis-onizleme'))[1];
  assert.equal(second.body.kasadaOncedenSayilanTutar, 1200);
  assert.notEqual(second.body.istekId, first.body.istekId);
  assert.equal(counted.value, '1200');
  assert.equal(
    nodes.get('#modal-content').find(node => node.tag === 'button' && /yeniden önizle/.test(node.textContent)),
    null
  );
  assert.equal(
    calls.some(call => call.path.endsWith('/gecis')),
    false
  );
  await submitDialog(nodes);
  const save = calls.find(call => call.path.endsWith('/gecis'));
  assert.equal(save.body.onay, true);
  assert.equal(save.body.kasadaOncedenSayilanTutar, 1200);
  assert.equal(save.body.istekId, second.body.istekId);
});

test('rejected card transition preview offers only the suggestion retry and never saves', async () => {
  const oldCard = { ...sampleCard, yeniTakip: false, surum: 0, borc: 100, ekstreler: [] };
  const preview = call => ({
    kaynak: 'Kart',
    kaynakId: 4,
    baslangic: call.body.baslangic,
    genelKasaAnlikFarki: call.body.kasadaOncedenSayilanTutar - 100,
    kanalAnlikFarki: 0,
    eskiKasadaSayilanTutar: call.body.kasadaOncedenSayilanTutar,
    aciklamalar: ['Hiç düşmeyecek tutar'],
    kabulEdilebilir: call.body.kasadaOncedenSayilanTutar <= 100,
    sistemKartBorcu: 100,
    eskiKuraldaIslenenTutar: 0,
    bekleyenEskiDusumTutari: 0,
    sonBekleyenDusumTarihi: null,
    onerilenKasadaSayilanTutar: 100,
  });
  const { app, nodes, calls } = await openApp(false, { '/api/kanallar': [], '/api/takip/kartlar/4/gecis-onizleme': preview });
  await app.financeUi.cardTransition(oldCard);
  formField(nodes, 'kalanBorc').value = '300';
  const counted = formField(nodes, 'kasadaOncedenSayilanTutar');
  counted.value = '250';
  counted.listeners.input();
  formField(nodes, 'aciklama').value = 'Fazla girildi';
  await submitDialog(nodes);
  assert.equal(
    nodes.get('#modal-content').find(node => node.tag === 'form'),
    null
  );
  assert.match(nodes.get('#modal-content').textContent, /Bekleyen düşüm yok/);
  await clickDialog(nodes, `Önerilen tutarla (${money(100)}) yeniden önizle`);
  assert.equal(calls.filter(call => call.path.endsWith('/gecis-onizleme'))[1].body.kasadaOncedenSayilanTutar, 100);
  assert.equal(
    calls.some(call => call.path.endsWith('/gecis')),
    false
  );
});

test('transitioned card page shows the stored transition record and the first-version residual warning', async () => {
  const card = {
    ...sampleCard,
    gecis: {
      kural: 'IslemTarihi',
      aciklama: 'Banka ekstresiyle <b>doğrulandı</b>',
      onizleme: {
        onayTarihi: '2026-09-25',
        kalanBorc: 1300,
        kasadaOncedenSayilanTutar: 1200,
        sistemKartBorcu: 1500,
        eskiKuraldaIslenenTutar: 300,
        bekleyenEskiDusumTutari: 1200,
        sonBekleyenDusumTarihi: '2026-10-31',
        onerilenKasadaSayilanTutar: 1200,
      },
      raporDisiEskiDusumTutari: 0,
      raporDisiIlkDusumTarihi: null,
      raporDisiSonDusumTarihi: null,
      tahminiKasaFarki: 0,
      uyari: null,
    },
  };
  const { app, nodes } = await openApp(false, { '/api/takip/kartlar/4': card });
  await app.navigate('cards', 4);
  let text = nodes.get('#view').textContent;
  assert.match(text, /Eski karttan geçiş/);
  assert.match(text, /Girilen kalan borç.*1\.300,00/);
  assert.match(text, /Kasada önceden sayılan.*1\.200,00.*Önerilen.*1\.200,00/);
  assert.match(text, /Bekleyen eski düşüm.*1\.200,00/);
  assert.ok(text.includes(`Son düşüm ${dateText('2026-10-31')}`));
  assert.ok(text.includes(`${dateText('2026-09-25')} tarihinde onaylandı`));
  assert.ok(text.includes('Geçiş açıklaması: Banka ekstresiyle <b>doğrulandı</b>'));
  assert.doesNotMatch(text, /İlk sürüm/);
  assert.equal(
    nodes.get('#view').find(node => (node.className || '').startsWith('notice')),
    null
  );

  const warning = 'İlk sürüm kuralıyla geçiş: 1.400,00 TL eski ay sonu düşümü raporlara girmiyor; 500,00 TL kasadan hiçbir zaman düşmüyor.';
  const first = {
    ...sampleCard,
    id: 5,
    gecis: {
      kural: 'EtkiTarihi',
      aciklama: null,
      onizleme: null,
      raporDisiEskiDusumTutari: 1400,
      raporDisiIlkDusumTarihi: '2026-09-30',
      raporDisiSonDusumTarihi: '2026-10-31',
      tahminiKasaFarki: 500,
      uyari: warning,
    },
  };
  const consistent = {
    ...first,
    id: 6,
    gecis: { ...first.gecis, tahminiKasaFarki: 0, uyari: 'Toplam kasa etkisi tutarlı, yalnız düşüş tarihi farklı.' },
  };
  const second = await openApp(false, {
    '/api/takip/kartlar/5': first,
    '/api/takip/kartlar/6': consistent,
    '/api/takip/kartlar': [first, consistent, sampleCard],
  });
  await second.app.navigate('cards', 5);
  text = second.nodes.get('#view').textContent;
  assert.equal(second.nodes.get('#view').find(node => (node.className || '').includes('notice danger')).textContent, warning);
  assert.match(text, /önizleme özeti saklanmadı/);
  assert.doesNotMatch(text, /Geçiş açıklaması/);
  await second.app.navigate('cards');
  const list = second.nodes.get('#view').textContent;
  assert.equal(list.split('Geçiş farkını doğrulayın').length - 1, 1);
  assert.equal(
    second.nodes.get('#view').find(node => node.children.includes('0')),
    null
  );
  await second.app.navigate('cards', 6);
  const info = second.nodes.get('#view').find(node => (node.className || '').startsWith('notice') && node.textContent.includes('tutarlı'));
  assert.equal(info.className, 'notice');
});

test('web login leaves the known-device token to the HttpOnly cookie: same-origin credentials, no device header, nothing stored', async () => {
  // Sunucu tarayıcıya belirteci gövdede vermez; verse bile web onu hiçbir isteğe eklemez ve depoya yazmaz.
  const { nodes, calls, stored } = await openApp(false, {
    '/api/auth/me': { $status: 401 },
    '/api/auth/login': { rol: 'editor', token: 'jwt-gizli', cihaz: 'c1.gizli-cihaz' },
  });
  assert.equal(nodes.get('#login-screen').hidden, false);
  const form = nodes.get('#login-form');
  const Element = form.constructor;
  const user = new Element('input');
  user.setAttribute('name', 'kullanici');
  user.value = 'editor';
  const password = new Element('input');
  password.setAttribute('name', 'sifre');
  password.setAttribute('type', 'password');
  password.value = 'editor-sifresi';
  form.append(user, password);
  form.listeners.submit({ preventDefault() {}, currentTarget: form });
  await settle();

  assert.equal(nodes.get('#login-error').textContent, '');
  assert.equal(nodes.get('#application').hidden, false);
  const login = calls.find(call => call.path === '/api/auth/login');
  assert.deepEqual([login.method, login.body], ['POST', { kullanici: 'editor', sifre: 'editor-sifresi' }]);
  // Tarayıcı aynı kökenli isteğe __Host-kasa_cihaz_<rol> çerezini kendisi ekler; betik başlık koymaz.
  assert.equal(login.credentials, 'same-origin');
  assert.equal(login.headers['x-kasa-request'], '1');
  assert.equal(login.headers['x-kasa-cihaz'], undefined);
  const later = calls.slice(calls.indexOf(login) + 1);
  assert.ok(later.length > 0, 'Girişten sonra ana sayfa yüklenir.');
  for (const call of later) assert.doesNotMatch(JSON.stringify(call), /jwt-gizli|gizli-cihaz/);
  assert.ok(calls.every(call => call.credentials === 'same-origin'));
  assert.deepEqual(stored, []);
  assert.deepEqual([user.value, password.value], ['', '']);
});

test('startup session check leaves a device token in the /me body unused: nothing stored or sent', async () => {
  // Sunucu tarayıcıya /api/auth/me yanıtında belirteci gövdede vermez (yalnız çerezi yeniler); verse bile web onu kullanmaz.
  const { nodes, calls, stored } = await openApp(false, { '/api/auth/me': { rol: 'editor', cihaz: 'c1.gizli-me' } });
  assert.equal(nodes.get('#application').hidden, false);
  const me = calls.find(call => call.path === '/api/auth/me');
  assert.equal(me.credentials, 'same-origin');
  assert.equal(me.headers['x-kasa-cihaz'], undefined);
  const later = calls.slice(calls.indexOf(me) + 1);
  assert.ok(later.length > 0, 'Doğrulamadan sonra ana sayfa yüklenir.');
  for (const call of later) assert.doesNotMatch(JSON.stringify(call), /gizli-me/);
  assert.deepEqual(stored, []);
});

test('manual backup rate limit shows the server Turkish 429 message and keeps the session', async () => {
  const message =
    'Elle yedek sınırına ulaşıldı: 60 dakikada en çok 5 elle yedek alınabilir. 42 dakika sonra yeniden deneyin. Otomatik yedekleme bundan etkilenmez.';
  const { app, nodes, calls } = await openApp(false, {
    '/api/ayarlar': { takipBaslangic: '2026-01-01', kasaAcilisDevri: 0, izleyiciSifreVarMi: true },
    '/api/yedek/durum': { otomatikEtkin: true },
    '/api/alicilar': [],
    '/api/kanallar': [],
    '/api/yedek': { $status: 429, hata: message },
  });
  await app.navigate('tools');
  await clickView(nodes, 'Şimdi yedek indir');
  assert.deepEqual(
    calls.filter(call => call.path === '/api/yedek').map(call => call.method),
    ['POST']
  );
  assert.match(nodes.get('#alerts').textContent, /Elle yedek sınırına ulaşıldı: 60 dakikada en çok 5 elle yedek/);
  assert.equal(nodes.get('#application').hidden, false);
  assert.equal(nodes.get('#login-screen').hidden, true);
});

// Değişiklik geçmişi (denetim-ui.js) Ayarlar'daki düğmeyle tembel yüklenir ve uygulamanın tek kabuğunu (ui-shell.js: pencere,
// oturum, api) içe aktarır. Kabuğun ikinci bir kopyası yüklenseydi çalışma ayarını yeniden okur, pencereyi ayrı durumla açardı.
test('değişiklik geçmişi modülü tembel yüklenir, uygulamanın penceresinde açılır ve aynı kabuğun api’siyle okur', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/ayarlar': { takipBaslangic: '2026-01-01', kasaAcilisDevri: 0, izleyiciSifreVarMi: true },
    '/api/yedek/durum': { otomatikEtkin: true },
    '/api/alicilar': [],
    '/api/kanallar': [],
    '/api/denetim?adet=50': [],
  });
  await app.navigate('tools');
  await clickView(nodes, 'Değişiklik geçmişini aç');
  assert.equal(nodes.get('#modal').open, true);
  assert.equal(nodes.get('#modal-title').textContent, 'Değişiklik geçmişi');
  await nodes
    .get('#modal-content')
    .find(node => node.tag === 'form')
    .listeners.submit({ preventDefault() {} });
  await settle();
  assert.deepEqual(
    calls.filter(call => call.path.startsWith('/api/denetim')).map(call => call.path),
    ['/api/denetim?adet=50']
  );
  assert.match(nodes.get('#modal-content').textContent, /Bu süzgeçte kayıt yok/);
  assert.equal(calls.filter(call => call.path === '/kasa-runtime.json').length, 1, 'çalışma ayarı bir kez okunur: kabuk tek kopyadır');
});

// webui-1: ESC / Android geri hareketiyle kapanan diyalogda kayıt sonucu kaybolmaz.
const pendingExpense = async () => {
  let finishSave;
  const opened = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': [],
    '/api/islemler': call =>
      call.method === 'POST'
        ? new Promise(resolve => {
            finishSave = resolve;
          })
        : [],
  });
  await opened.app.expenseDialog();
  for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A', tarih: '2026-09-23' }))
    formField(opened.nodes, name).value = value;
  await submitDialog(opened.nodes);
  assert.equal(typeof finishSave, 'function', 'Kayıt isteği yanıt bekliyor.');
  // Diyaloğun kendi kapatma isteği (ESC / geri hareketi): olayın hedefi <dialog>'un kendisidir.
  const cancel = (cancelable, target = opened.nodes.get('#modal')) => {
    let prevented = false;
    opened.nodes.get('#modal').listeners.cancel({
      target,
      cancelable,
      preventDefault() {
        prevented = true;
      },
    });
    return prevented;
  };
  return {
    ...opened,
    cancel,
    finish: async value => {
      finishSave(value);
      await settle();
    },
  };
};
const cancelButton = nodes => nodes.get('#modal-content').find(node => node.tag === 'button' && node.textContent === 'Vazgeç');
test('iptal edilemez ESC veya geri hareketiyle kapanan diyalogdaki kayıt hatası kapalı pencerede kalmaz, bildirim olarak görünür', async () => {
  const { nodes, calls, cancel, finish } = await pendingExpense();
  const errorBox = nodes.get('#modal-content').find(node => node.attributes.role === 'alert');
  // Chrome art arda ESC / Android geri hareketinde cancel olayını iptal edilemez gönderir: pencere kapanır, içerik temizlenir.
  assert.equal(cancel(false), false);
  assert.equal(nodes.get('#modal').open, false);
  assert.equal(nodes.get('#modal-content').children.length, 0);
  assert.equal(errorBox.isConnected, false);
  await finish({ $status: 409, hata: 'Bu ay kilitli.' });
  assert.match(nodes.get('#alerts').textContent, /Gider kaydet: Bu ay kilitli\./);
  assert.doesNotMatch(errorBox.textContent, /kilitli/);
  assert.equal(calls.filter(call => call.path === '/api/islemler' && call.method === 'POST').length, 1);
  assert.ok(!nodes.get('#modal-close').disabled, 'Sonraki pencere kilitsiz açılır.');
});
test('kayıt sürerken ESC veya geri hareketi engellenir, Vazgeç ve × açık kalır; yanıt gelince hata açık pencerede görünür', async () => {
  const { nodes, cancel, finish } = await pendingExpense();
  // iOS'ta (ana ekran PWA dahil) ESC / geri hareketi yok ve isteğin zaman aşımı yok: pencereden çıkış yolu Vazgeç ve × açık kalır.
  assert.ok(!cancelButton(nodes).disabled, 'Vazgeç kayıt sürerken de kullanılabilir.');
  assert.ok(!nodes.get('#modal-close').disabled, '× kayıt sürerken de kullanılabilir.');
  assert.equal(cancel(true), true, 'İptal edilebilir cancel olayı engellenir.');
  assert.equal(nodes.get('#modal').open, true);
  assert.ok(formField(nodes, 'cari'), 'Form korunur.');
  assert.match(nodes.get('#notifications').textContent, /Kayıt sürüyor.*Vazgeç/);
  await finish({ $status: 409, hata: 'Bu ay kilitli.' });
  assert.match(nodes.get('#modal-content').textContent, /Bu ay kilitli\./);
  assert.doesNotMatch(nodes.get('#alerts')?.textContent ?? '', /Bu ay kilitli/);
  assert.equal(cancel(true), false, 'Kayıt bitince pencere yeniden kapanabilir.');
  assert.equal(nodes.get('#modal').open, false);
  assert.equal(nodes.get('#modal-content').children.length, 0);
});
test('diyalog cancel olayı olmadan kapansa bile geç gelen kayıt hatası bildirim olarak görünür', async () => {
  const { nodes, finish } = await pendingExpense();
  nodes.get('#modal').close();
  await finish({ $status: 409, hata: 'Kayıt değişti.' });
  assert.match(nodes.get('#alerts').textContent, /Gider kaydet: Kayıt değişti\./);
  assert.ok(!nodes.get('#modal-close').disabled);
});
test('kayıt sürerken Vazgeç ya da × ile kapatılan pencerenin geç gelen hatası bildirim olarak görünür', async () => {
  for (const [name, closer] of [
    ['Vazgeç', nodes => cancelButton(nodes)],
    ['×', nodes => nodes.get('#modal-close')],
  ]) {
    const { nodes, calls, finish } = await pendingExpense();
    const close = closer(nodes);
    assert.ok(!close.disabled, `${name} kayıt sürerken kilitli değildir.`);
    close.listeners.click({ currentTarget: close });
    assert.equal(nodes.get('#modal').open, false, `${name} pencereyi kapatır.`);
    assert.equal(nodes.get('#modal-content').children.length, 0);
    await finish({ $status: 409, hata: 'Bu ay kilitli.' });
    assert.match(nodes.get('#alerts').textContent, /Gider kaydet: Bu ay kilitli\./, `${name} sonrası hata bildirimle görünür.`);
    assert.equal(calls.filter(call => call.path === '/api/islemler' && call.method === 'POST').length, 1);
  }
});
test('dosya seçiciden vazgeçmek (dosya alanından yukarı taşınan cancel) pencereyi kapatmaz, girilenleri silmez', async () => {
  const fire = (nodes, target, cancelable) => {
    let prevented = false;
    nodes.get('#modal').listeners.cancel({
      target,
      cancelable,
      preventDefault() {
        prevented = true;
      },
    });
    return prevented;
  };
  // Belge ekle: dosya seçici kapatıldığında, iOS Fotoğraf/Dosya menüsü kapatıldığında ya da aynı dosya yeniden seçildiğinde.
  const purchase = await openApp(false);
  purchase.app.documentDialog({ id: 5, durum: 'Taslak', odemeler: [{ id: 9, tarih: '2026-09-20', tutar: 100 }] });
  const payment = formField(purchase.nodes, 'odemeId');
  payment.value = '9';
  assert.equal(fire(purchase.nodes, formField(purchase.nodes, 'dosya'), false), false);
  assert.equal(purchase.nodes.get('#modal').open, true, 'Belge ekle penceresi açık kalır.');
  assert.equal(formField(purchase.nodes, 'odemeId'), payment);
  assert.equal(payment.value, '9');
  // Ekstre / hareket PDF'si yükle: seçilen banka, kart ve hesap adı korunur.
  const statement = await openApp(false, importResponses());
  await statement.app.navigate('imports');
  await clickView(statement.nodes, 'Kart ekstresi / hesap hareketi seç');
  formField(statement.nodes, 'banka').value = 'Akbank';
  formField(statement.nodes, 'kartId').value = '4';
  const modal = statement.nodes.get('#modal');
  for (const cancelable of [false, true])
    assert.equal(
      fire(statement.nodes, formField(statement.nodes, 'dosya'), cancelable),
      false,
      'Dosya alanının olayı engellenmez; diyaloğa ait değildir.'
    );
  assert.equal(modal.open, true, 'Ekstre yükleme penceresi açık kalır.');
  assert.equal(formField(statement.nodes, 'banka').value, 'Akbank');
  assert.equal(formField(statement.nodes, 'kartId').value, '4');
  // Diyaloğun kendi kapatma isteği (ESC / geri hareketi) pencereyi yine kapatır.
  assert.equal(fire(statement.nodes, modal, true), false);
  assert.equal(modal.open, false);
  assert.equal(statement.nodes.get('#modal-content').children.length, 0);
});
test('önizlemeden açılan onay penceresi kilitsiz başlar ve ESC ile kapanabilir', async () => {
  const preview = {
    tutar: 2000,
    kasaEtkisi: 2000,
    dagilimlar: [{ kanalId: 1, kanal: 'MEZAT', tutar: 2000 }],
    ekstreler: [{ ekstreId: 8, tutar: 2000 }],
  };
  const { app, nodes } = await openApp(false, { '/api/takip/kartlar/4/odeme-onizleme': preview, '/api/takip/kartlar/4': sampleCard });
  app.financeUi.cardPaymentDialog(sampleCard);
  formField(nodes, 'tutar').value = '2000';
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /Ödemeyi onayla/);
  assert.ok(!nodes.get('#modal-close').disabled);
  assert.ok(!cancelButton(nodes).disabled);
  let prevented = false;
  nodes.get('#modal').listeners.cancel({
    target: nodes.get('#modal'),
    cancelable: true,
    preventDefault() {
      prevented = true;
    },
  });
  assert.equal(prevented, false);
  assert.equal(nodes.get('#modal').open, false);
});

// webui-5: iOS ondalık klavyesinde eksi tuşu yok; eksi olabilen tutarlarda işaret ayrı seçilir, tutar kuralı (kuruş) aynıdır.
const openCharge = async card => {
  const opened = await openApp(false, { '/api/kanallar': [], '/api/takip/kartlar/4': card, '/api/takip/kartlar/4/harcamalar': card });
  await opened.app.navigate('cards', 4);
  const open = opened.nodes.get('#page-actions').find(node => node.tag === 'button' && node.textContent === '+ Harcama / iade');
  await open.listeners.click({ currentTarget: open });
  await settle();
  return opened;
};
test('kart iadesi Hareket türü İade seçilerek eksi yazmadan girilir ve eksi tutarla gönderilir', async () => {
  const card = {
    ...sampleCard,
    harcamalar: [{ id: 12, tarih: '2026-09-22', aciklama: 'B kanalı mal', tutar: 300, taksitSayisi: 1, dagilimlar: [] }],
  };
  const { nodes, calls } = await openCharge(card);
  const kind = formField(nodes, 'hareketTuru');
  const total = formField(nodes, 'tutar');
  const source = formField(nodes, 'kaynakHarcamaId');
  assert.equal(kind.value, 'Harcama');
  assert.equal(total.attributes.inputmode, 'decimal');
  assert.equal(source.disabled, true);
  assert.match(nodes.get('#modal-content').textContent, /Hareket türünü İade seçip/);
  kind.value = 'Iade';
  kind.listeners.change();
  total.value = '25,50';
  total.listeners.input();
  assert.equal(kind.value, 'Iade', 'Eksisiz tutar İade seçimini geri almaz.');
  assert.equal(source.disabled, false);
  assert.equal(source.required, true);
  assert.equal(formField(nodes, 'taksitSayisi').disabled, true);
  formField(nodes, 'aciklama').value = 'Mal iadesi';
  source.value = '12';
  await submitDialog(nodes);
  const refund = calls.find(call => call.path.endsWith('/harcamalar'));
  assert.ok(refund, 'İade kaydedilir.');
  assert.equal(refund.body.tutar, -25.5);
  assert.equal(refund.body.kaynakHarcamaId, 12);
  assert.equal(refund.body.taksitSayisi, 1);
  assert.deepEqual(refund.body.dagilimlar, []);
  assert.equal(refund.body.ilkKesimTarihi, null);
  assert.equal(calls.find(call => call.path === '/api/islemler/benzerlik').body.tutar, -25.5);
});
test('eksi yazılabilen klavyede tutar eksi girilince Hareket türü İade olur, eksi silinince Harcama türüne döner', async () => {
  const card = {
    ...sampleCard,
    harcamalar: [{ id: 11, tarih: '2026-09-21', aciklama: 'Mal', tutar: 200, taksitSayisi: 1, dagilimlar: [] }],
  };
  const { nodes } = await openCharge(card);
  const kind = formField(nodes, 'hareketTuru');
  const total = formField(nodes, 'tutar');
  total.value = '-';
  total.listeners.input();
  assert.equal(kind.value, 'Iade');
  total.value = '-5';
  total.listeners.input();
  assert.equal(kind.value, 'Iade');
  total.value = '5';
  total.listeners.input();
  assert.equal(kind.value, 'Harcama');
  kind.value = 'Iade';
  kind.listeners.change();
  total.value = '7';
  total.listeners.input();
  assert.equal(kind.value, 'Iade', 'Kullanıcının seçtiği İade tutar yazılırken değişmez.');
});
test('eksi tutar yazılıp tür elle Harcama yapılırsa ekrandaki türle çelişen iade gönderilmez, açık hata gösterilir', async () => {
  const card = {
    ...sampleCard,
    harcamalar: [{ id: 12, tarih: '2026-09-22', aciklama: 'B kanalı mal', tutar: 300, taksitSayisi: 1, dagilimlar: [] }],
  };
  const { nodes, calls } = await openCharge(card);
  const kind = formField(nodes, 'hareketTuru');
  const total = formField(nodes, 'tutar');
  const source = formField(nodes, 'kaynakHarcamaId');
  total.value = '-25';
  total.listeners.input();
  assert.equal(kind.value, 'Iade');
  formField(nodes, 'aciklama').value = 'Mal iadesi';
  source.value = '12';
  kind.value = 'Harcama';
  kind.listeners.change();
  assert.equal(source.disabled, true, 'Harcama türünde iade alanları kapalıdır.');
  await submitDialog(nodes);
  assert.equal(nodes.get('#modal').open, true, 'Pencere açık kalır.');
  assert.match(
    nodes.get('#modal-content').find(node => node.attributes.role === 'alert').textContent,
    /Harcama türünde tutar eksi olamaz.*İade/
  );
  assert.equal(
    calls.filter(call => call.path.endsWith('/harcamalar') || call.path === '/api/islemler/benzerlik').length,
    0,
    'Ne benzerlik ne kayıt isteği gider.'
  );
  // Tür yeniden İade seçilince aynı tutar iade olarak kaydedilir.
  kind.value = 'Iade';
  kind.listeners.change();
  await submitDialog(nodes);
  const refund = calls.find(call => call.path.endsWith('/harcamalar'));
  assert.equal(refund.body.tutar, -25);
  assert.equal(refund.body.kaynakHarcamaId, 12);
  assert.deepEqual(refund.body.dagilimlar, []);
});
test('gider tutarı ± seçiciyle eksi girilir; mevcut eksi gider işaret ve mutlak değerle açılır; kuruş kuralı değişmez', async () => {
  const expense = {
    id: 20,
    tarih: '2026-09-23',
    tutarTl: -12.5,
    cari: 'İade düzeltmesi',
    tip: 'Cari',
    kanal: 'A',
    not: '',
    krediKartiId: null,
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': [],
    '/api/islemler': [],
    '/api/islemler/20': expense,
  });
  await app.expenseDialog();
  const sign = formField(nodes, 'tutarTlIsaret');
  const total = formField(nodes, 'tutarTl');
  assert.equal(sign.tag, 'select');
  assert.equal(sign.value, '+');
  assert.match(sign.attributes['aria-label'], /işareti/);
  assert.equal(total.attributes.inputmode, 'decimal');
  for (const [name, value] of Object.entries({ cari: 'Düzeltme', kanal: 'A', tarih: '2026-09-23' })) formField(nodes, name).value = value;
  sign.value = '-';
  total.value = '10,555';
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /en çok iki ondalık/);
  total.value = '10';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/islemler/benzerlik').body.tutar, -10);
  assert.equal(calls.find(call => call.path === '/api/islemler' && call.method === 'POST').body.tutarTl, -10);
  await app.expenseDialog(expense);
  assert.equal(formField(nodes, 'tutarTlIsaret').value, '-');
  assert.equal(formField(nodes, 'tutarTl').value, '12.5');
  formField(nodes, 'tutarTlIsaret').value = '+';
  formField(nodes, 'tutarTl').value = '-12,5';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/islemler/20').body.tutarTl, -12.5, 'Klavyede yazılan eksi korunur.');
});
test('dönem geliri ± seçiciyle eksi girilir ve eski eksi toplam işaretle gösterilir', async () => {
  const period = '2026-09-14';
  const { app, nodes, calls } = await openApp(false, {
    '/api/rapor/haftalik': [{ donem: { start: period, end: '2026-09-20' } }],
    '/api/kanallar': [
      { id: 7, ad: 'Mağaza' },
      { id: 8, ad: 'Normal' },
    ],
    [`/api/gelenler?donemStart=${period}`]: [{ kanalId: 7, kanal: 'Mağaza', tutarTl: -30 }],
    '/api/gelenler': null,
  });
  await app.incomeDialog(period);
  const channel = formField(nodes, 'kanal');
  const sign = formField(nodes, 'tutarTlIsaret');
  const total = formField(nodes, 'tutarTl');
  channel.value = 'Mağaza';
  channel.listeners.change();
  assert.equal(sign.value, '-');
  assert.equal(total.value, '30');
  assert.equal(sign.disabled, false);
  channel.value = 'Normal';
  channel.listeners.change();
  assert.equal(sign.value, '+');
  assert.equal(total.value, '0');
  sign.value = '-';
  total.value = '15';
  await submitDialog(nodes);
  assert.deepEqual(calls.find(call => call.path === '/api/gelenler' && call.method === 'PUT').body, {
    donemStart: period,
    kanal: 'Normal',
    tutarTl: -15,
    surum: 0,
  });
});
test('eski gelir grubu kilitliyken işaret seçici de kilitlidir', async () => {
  const period = '2026-09-07';
  const { app, nodes } = await openApp(false, {
    '/api/rapor/haftalik': [{ donem: { start: period, end: '2026-09-13' } }],
    '/api/kanallar': [{ id: 7, ad: 'Mağaza' }],
    [`/api/gelenler?donemStart=${period}`]: [
      { kanalId: 7, kanal: 'Mağaza', tutarTl: -10, eskiYinelenenGrup: true },
      { kanalId: 7, kanal: 'mağaza', tutarTl: -20, eskiYinelenenGrup: true },
    ],
  });
  await app.incomeDialog(period);
  const channel = formField(nodes, 'kanal');
  channel.value = 'Mağaza';
  channel.listeners.change();
  assert.equal(formField(nodes, 'tutarTl').readOnly, true);
  assert.equal(formField(nodes, 'tutarTlIsaret').disabled, true);
  assert.equal(formField(nodes, 'tutarTlIsaret').value, '-');
  assert.equal(formField(nodes, 'tutarTl').value, '30');
});
test('kanal ve genel kasa açılış devri ± seçiciyle eksi girilir', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [],
    '/api/kanallar/3': null,
    '/api/ayarlar': { takipBaslangic: '2026-01-01', kasaAcilisDevri: -99.99, izleyiciSifreVarMi: true },
    '/api/yedek/durum': { otomatikEtkin: true },
    '/api/alicilar': [],
  });
  app.channelDialog({ id: 3, ad: 'Mezat', aktif: true, sira: 0, acilisDevri: -150 });
  assert.equal(formField(nodes, 'acilisDevriIsaret').value, '-');
  assert.equal(formField(nodes, 'acilisDevri').value, '150');
  formField(nodes, 'acilisDevri').value = '150,25';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/kanallar/3').body.acilisDevri, -150.25);
  app.channelDialog();
  assert.equal(formField(nodes, 'acilisDevriIsaret').value, '+');
  assert.equal(formField(nodes, 'acilisDevri').value, '0');
  app.openingDialog({ takipBaslangic: '2026-01-01', kasaAcilisDevri: -99.99 });
  assert.equal(formField(nodes, 'kasaAcilisDevriIsaret').value, '-');
  assert.equal(formField(nodes, 'kasaAcilisDevri').value, '99.99');
  formField(nodes, 'kasaAcilisDevri').value = '250';
  await submitDialog(nodes);
  assert.deepEqual(calls.find(call => call.path === '/api/ayarlar' && call.method === 'PUT').body, {
    takipBaslangic: '2026-01-01',
    kasaAcilisDevri: -250,
    surum: 0,
  });
});
// Tamamlanmış ayların kanal kümesi sunucuda dondurulduğundan kilit varken de aktif kanal eklenebilir: Ayarlar kilit durumunu okur,
// kanal formu kilidi (yalnız açılış devri kilitte değişmez) söyler; yeni kanal kilitte de aktif gelir, düzenlenen kendi aktifliğiyle.
test('ay kilidi varken kanal formu kilidi söyler ve yeni kanal aktif gelir; kilit yokken ya da okunamazsa not görünmez', async () => {
  const { app, nodes, calls, responses } = await openApp(false, {
    '/api/kanallar': [{ id: 3, ad: 'Mezat', aktif: true, sira: 0, acilisDevri: 0 }],
    '/api/ay-kilidi': { surum: 2, kilitliSonTarih: '2026-08-31', gecmis: [] },
    '/api/ayarlar': { takipBaslangic: '2026-01-01', kasaAcilisDevri: 0, izleyiciSifreVarMi: true },
    '/api/yedek/durum': { otomatikEtkin: true },
    '/api/alicilar': [],
  });
  const lockText = `${dateText('2026-08-31')} dahil aylar kilitli`;
  await app.navigate('tools');
  await clickView(nodes, '+ Kanal ekle');
  assert.equal(formField(nodes, 'aktif').checked, true, 'Kilitte de yeni kanal aktif gelir.');
  assert.ok(nodes.get('#modal-content').textContent.includes(`${lockText}. Yeni kanal açılış devri 0`), 'Kilit notu görünür.');
  formField(nodes, 'ad').value = 'E-TİCARET';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/kanallar' && call.method === 'POST').body.aktif, true);

  await clickView(nodes, 'Düzenle');
  assert.equal(formField(nodes, 'aktif').checked, true, 'Düzenlenen kanal kendi aktifliğiyle açılır.');
  assert.ok(nodes.get('#modal-content').textContent.includes(lockText), 'Düzenlemede de kilit söylenir.');

  responses['/api/ay-kilidi'] = { surum: 3, kilitliSonTarih: null, gecmis: [] };
  await app.navigate('tools');
  await clickView(nodes, '+ Kanal ekle');
  assert.equal(formField(nodes, 'aktif').checked, true);
  assert.doesNotMatch(nodes.get('#modal-content').textContent, /dahil aylar kilitli/);

  // Kilit durumu yalnız form varsayılanı içindir (kuralı sunucu uygular): okunamazsa Ayarlar yine açılır, form aktif gelir.
  responses['/api/ay-kilidi'] = { $status: 500, hata: 'Sunucu hatası' };
  await app.navigate('tools');
  await clickView(nodes, '+ Kanal ekle');
  assert.equal(formField(nodes, 'aktif').checked, true);
  assert.match(nodes.get('#view').textContent, /Mezat/);
});
test('gerçek bakiye karşılaştırması ± seçiciyle eksi bakiyeyi önizler', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/kasa-kontrol/onizleme': { sistemBakiye: 123, gercekBakiye: -20, fark: -143, kontrolOzeti: 'digest1' },
  });
  app.cashControlsUi.comparisonDialog();
  assert.equal(formField(nodes, 'gercekBakiyeIsaret').value, '+');
  formField(nodes, 'gercekBakiyeIsaret').value = '-';
  formField(nodes, 'gercekBakiye').value = '20';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/kasa-kontrol/onizleme').body.gercekBakiye, -20);
});
test('işaretli tutarın sunucu alan hatası ± ızgarasına değil tutar etiketinin altına yazılır', async () => {
  const message = 'Tutar en fazla iki ondalık basamak içerebilir.';
  const { app, nodes } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': [],
    '/api/islemler': call => (call.method === 'POST' ? { $status: 400, errors: { tutarTl: [message] } } : []),
  });
  await app.expenseDialog();
  for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A', tarih: '2026-09-23' }))
    formField(nodes, name).value = value;
  await submitDialog(nodes);
  const total = formField(nodes, 'tutarTl');
  const grid = total.parentNode;
  const label = total.closest('.signed-field');
  assert.equal(total.attributes['aria-invalid'], 'true');
  assert.equal(grid.className, 'signed-amount');
  assert.deepEqual(
    grid.children.map(child => child.attributes?.name),
    ['tutarTlIsaret', 'tutarTl'],
    'Izgarada yalnız işaret ve tutar kalır.'
  );
  const note = label.children.find(child => child.className === 'form-error field-error');
  assert.equal(note?.textContent, message, 'Not tutar etiketinin altındadır.');
  // Sonraki denemede not etiketten silinir.
  total.value = '80';
  await submitDialog(nodes);
  assert.equal(label.children.filter(child => child.className === 'form-error field-error').length, 1);
});

// Yedek rotasyon uyarısı: yedeğin kendisi başarılıdır, silinemeyen eski yedek ayrıca görünür.
test('Ayarlar yedek bölümü sunucunun rotasyon uyarısını gösterir, uyarı kalkınca gizler', async () => {
  const warning =
    'Otomatik yedek rotasyonu tamamlanamadı: saklama süresi dolan 1 yedek silinemedi (kasa-oto-20260801T030000Z.zip). Sunucu kayıtlarını ve yedek dizininin izinlerini kontrol edin.';
  const status = {
    otomatikEtkin: true,
    sonYedek: '2026-09-23T03:00:00Z',
    sonDogrulama: '2026-09-23T03:00:05Z',
    hata: null,
    rotasyonUyarisi: warning,
  };
  const { app, nodes } = await openApp(false, {
    '/api/ayarlar': { takipBaslangic: '2026-01-01', kasaAcilisDevri: 0, izleyiciSifreVarMi: true },
    '/api/yedek/durum': status,
    '/api/alicilar': [],
    '/api/kanallar': [],
  });
  await app.navigate('tools');
  const notice = nodes.get('#view').find(node => node.textContent === warning);
  assert.ok(notice, 'Rotasyon uyarısı görünür.');
  assert.equal(notice.attributes.role, 'alert');
  assert.equal(
    nodes.get('#view').find(node => node.className === 'form-error'),
    null,
    'Yedek başarılıdır; hata gösterilmez.'
  );
  status.rotasyonUyarisi = null;
  await app.navigate('tools');
  assert.doesNotMatch(nodes.get('#view').textContent, /rotasyonu tamamlanamadı/);
});

// Yedek disk durumu (data-3): boş alan ve toplam boyut GB olarak, eşik altı uyarısı ve bulunamayan belge uyarısı görünür.
test('Ayarlar yedek bölümü disk alanlarını GB olarak ve sunucunun disk/belge uyarısını gösterir; eski sunucuda satır yok', async () => {
  assert.equal(ui.gigabytes(104857600), '0,1 GB');
  assert.equal(ui.gigabytes(3.5 * 1073741824), '3,5 GB');
  assert.deepEqual(ui.backupDiskLines({ otomatikEtkin: true }), []);
  const status = {
    otomatikEtkin: true,
    sonYedek: null,
    sonDogrulama: null,
    hata: null,
    yedekDiskiBosAlanBayt: 104857600,
    veriDiskiBosAlanBayt: 5 * 1073741824,
    toplamYedekBayt: 3.5 * 1073741824,
    asgariBosAlanBayt: 2 * 1073741824,
    diskUyarisi: 'Yedek diskinde 0,1 GB boş alan kaldı (asgari 2,0 GB).',
    belgeUyarisi: 'Son yedekte 1 belge dosyası bulunamadı.',
  };
  const { app, nodes } = await openApp(false, {
    '/api/ayarlar': { takipBaslangic: '2026-01-01', kasaAcilisDevri: 0, izleyiciSifreVarMi: true },
    '/api/yedek/durum': status,
    '/api/alicilar': [],
    '/api/kanallar': [],
  });
  await app.navigate('tools');
  const text = nodes.get('#view').textContent;
  for (const line of ['Yedek diski boş alan: 0,1 GB', 'Veri diski boş alan: 5,0 GB', 'Yedeklerin toplam boyutu: 3,5 GB'])
    assert.match(text, new RegExp(line));
  for (const warning of [status.diskUyarisi, status.belgeUyarisi])
    assert.equal(nodes.get('#view').find(node => node.textContent === warning).attributes.role, 'alert');
});

// Son geri yükleme (gap-geri-yukleme-durum-geri-sarma-1): Araçlar'ın yedek bölümü sunucunun raporunu (yapılanlar ve yapılacaklar)
// madde madde gösterir; eski sunucu ya da hiç geri yükleme olmadıysa bölüm yok.
test('Araçlar yedek bölümü son geri yüklemenin raporunu gösterir; eski sunucuda göstermez', async () => {
  assert.equal(ui.restoreReport({ otomatikEtkin: true }), null);
  assert.deepEqual(ui.restoreReport({ sonGeriYukleme: '2026-09-28T10:15:00Z', geriYuklemeRaporu: ['A', '', null, 'B'] }).items, ['A', 'B']);
  const maddeler = [
    'Bütün oturumlar kapatıldı; herkes yeniden giriş yapmalı.',
    'Kurtarma kodu iptal edildi (yedekteki kod geçersiz): Güvenlik bölümünden yeni kurtarma kodu üretin.',
  ];
  const status = {
    otomatikEtkin: true,
    sonYedek: null,
    sonDogrulama: null,
    hata: null,
    sonGeriYukleme: '2026-09-28T10:15:00Z',
    geriYuklemeRaporu: maddeler,
  };
  const { app, nodes } = await openApp(false, {
    '/api/ayarlar': { takipBaslangic: '2026-01-01', kasaAcilisDevri: 0, izleyiciSifreVarMi: false },
    '/api/yedek/durum': status,
    '/api/alicilar': [],
    '/api/kanallar': [],
  });
  await app.navigate('tools');
  const text = nodes.get('#view').textContent;
  assert.match(text, /Son geri yükleme: /);
  for (const madde of maddeler)
    assert.ok(
      nodes.get('#view').find(node => node.tag === 'li' && node.textContent === madde),
      madde
    );
  delete status.sonGeriYukleme;
  delete status.geriYuklemeRaporu;
  await app.navigate('tools');
  assert.doesNotMatch(nodes.get('#view').textContent, /Son geri yükleme/);
});

// Alış belgeleri (gap-denetim-izi-gozlemlenebilirlik-9): yükleyen görünür; kaldırma yumuşaktır, editör gerekçe vermeden kaldıramaz ve
// gerekçe DELETE gövdesinde gider; kaldırılanlar editörün isteğiyle kaldıran ve gerekçesiyle listelenir.
test('purchase documents show the uploader, editor removal needs a reason sent in the DELETE body and removed ones are listed on request', async () => {
  const purchase = {
    id: 9,
    surum: 1,
    tarih: '2026-09-23',
    tedarikci: 'Firma',
    durum: 'Taslak',
    kalemler: [],
    odemeler: [],
    toplam: 0,
    odenen: 0,
    kalan: 0,
  };
  const doc = {
    id: 5,
    alisId: 9,
    odemeId: null,
    dosyaAdi: 'fis.pdf',
    icerikTuru: 'application/pdf',
    boyut: 2048,
    yuklendi: '2026-09-23T10:00:00Z',
    yukleyenRol: 'alici',
    yukleyen: 'Ayşe',
  };
  const removed = {
    ...doc,
    id: 6,
    dosyaAdi: 'eski.pdf',
    yukleyenRol: null,
    yukleyen: null,
    silindi: true,
    silinmeZamani: '2026-09-24T08:00:00Z',
    silenRol: 'editor',
    silen: 'Editör',
    silmeGerekcesi: 'Yanlış fiş',
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/alis': [purchase],
    '/api/alis/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/alis/9/belgeler': [doc],
    '/api/alis/9/belgeler?silinenler=true': [doc, removed],
    '/api/belgeler/5': null,
  });
  await app.navigate('purchase', 9);
  await settle();
  assert.match(nodes.get('#view').textContent, /Yükleyen: Ayşe/);
  await clickView(nodes, 'Kaldır');
  await submitDialog(nodes);
  assert.equal(calls.filter(call => call.path === '/api/belgeler/5').length, 0, 'Gerekçesiz editör kaldırması gönderilmez.');
  assert.match(nodes.get('#modal-content').textContent, new RegExp(ui.DOCUMENT_REASON_REQUIRED));
  formField(nodes, 'gerekce').value = '  İade sonrası yanlış fiş  ';
  await submitDialog(nodes);
  const removal = calls.find(call => call.path === '/api/belgeler/5');
  assert.equal(removal.method, 'DELETE');
  assert.deepEqual(removal.body, { gerekce: 'İade sonrası yanlış fiş' });
  await clickView(nodes, 'Kaldırılanları göster');
  assert.ok(calls.some(call => call.path === '/api/alis/9/belgeler?silinenler=true'));
  assert.match(nodes.get('#view').textContent, /Yükleyen: bilinmiyor \(eski kayıt\) · Kaldırıldı: Editör · .* · Gerekçe: Yanlış fiş/);
  assert.equal(
    nodes
      .get('#view')
      .find(node => node.className === 'document-row removed')
      .find(node => node.tag === 'button'),
    null,
    'Kaldırılan belge yeniden kaldırılamaz.'
  );
});
test('document rules: buyer removes only own, unpaid draft documents and never asks for removed ones; reason optional for buyer', () => {
  const draft = { durum: 'Taslak' };
  const own = { yukleyenRol: 'alici', odemeId: null };
  assert.equal(ui.documentRemovable('alici', draft, own), true);
  assert.equal(ui.documentRemovable('alici', draft, { ...own, yukleyenRol: 'editor' }), false);
  assert.equal(ui.documentRemovable('alici', draft, { ...own, yukleyenRol: null }), false);
  assert.equal(ui.documentRemovable('alici', draft, { ...own, odemeId: 3 }), false);
  assert.equal(ui.documentRemovable('alici', { durum: 'Incelemede' }, own), false);
  assert.equal(ui.documentRemovable('editor', { durum: 'Onaylandi' }, { ...own, odemeId: 3 }), true);
  assert.equal(ui.documentRemovable('editor', draft, { ...own, silindi: true }), false);
  assert.equal(ui.documentsPath(9, 'alici', true), '/api/alis/9/belgeler');
  assert.equal(ui.documentsPath(9, 'editor', true), '/api/alis/9/belgeler?silinenler=true');
  assert.equal(ui.documentDeletePayload('alici', '  '), null);
  assert.deepEqual(ui.documentDeletePayload('alici', ' neden '), { gerekce: 'neden' });
  assert.throws(() => ui.documentDeletePayload('editor', ' '), new RegExp(ui.DOCUMENT_REASON_REQUIRED));
});

// ---- İstemci notları: birleşik ana sayfa, veri sağlığı uyarısı, iptal, diyalog kimliği, işaretli tutar etiketi ----
const homeSummaryPath = '/api/rapor/ana-sayfa?gun=30';
const sampleHome = () => ({
  panel: {
    guncelKasa: 900,
    buHaftaSonucu: 0,
    buAySonucu: 0,
    kanallar: [
      { kanalId: 1, kanal: 'MEZAT', bakiye: 600 },
      { kanalId: 2, kanal: 'PERAKENDE', bakiye: 300 },
    ],
  },
  kasaEsikleri: [{ kanalId: 1, kanal: 'MEZAT', surum: 1, tutar: 1000, etkin: true, bakiye: 600, esikAltinda: true }],
  takipOzeti: {
    tarih: '2026-09-28',
    kartBorcu: 70,
    kalanKrediPlani: 0,
    olaylar: [],
    kanalKartBorclari: [{ kanalId: 1, kanal: 'MEZAT', tutar: 70 }],
  },
});
test('ana sayfa panel, kanal eşiği ve takip özetini tek istekte okur; ayrı uçlara gitmez', async () => {
  const { nodes, requests } = await openApp(false, { [homeSummaryPath]: sampleHome() });
  assert.equal(requests.filter(path => path === homeSummaryPath).length, 1);
  for (const old of ['/api/rapor/panel', '/api/kasa-esikleri', '/api/takip/ozet?gun=30'])
    assert.ok(!requests.includes(old), `${old} istenmez`);
  const view = nodes.get('#view');
  assert.match(view.textContent, /900,00/);
  const mezat = view.find(node => (node.className || '').split(' ')[0] === 'channel-balance' && node.textContent.includes('MEZAT'));
  assert.match(mezat.className, /below-threshold/);
  assert.match(mezat.textContent, /Alt sınırın altında/);
  assert.match(mezat.textContent, /Kalan kart borcu: .*70,00/);
  assert.doesNotMatch(view.textContent, /yükleniyor…/);
});
// gap-tarihsel-spec-ve-emekli-web-7: takipte olmayan (geçişi yapılmamış) kart ve krediler kaldıkça ana sayfada kalıcı uyarı; her kayıt
// kendi ekranını açar. Alan yoksa (kayıt yok ya da eski sunucu) uyarı çizilmez.
test('ana sayfa takipte olmayan kart ve kredileri kalıcı uyarıyla listeler, kayda gider', async () => {
  const { nodes, requests } = await openApp(false, {
    [homeSummaryPath]: {
      ...sampleHome(),
      takipsizKayitlar: [
        { kaynak: 'Kart', id: 4, ad: 'Bonus' },
        { kaynak: 'Kredi', id: 7, ad: 'Taşıt' },
      ],
    },
    '/api/takip/krediler/7': { $status: 404, hata: 'Yok' },
  });
  const view = nodes.get('#view');
  assert.match(
    view.textContent,
    /Kart ve kredi takibinde olmayan kayıtlar var: Bonus \(kart\), Taşıt \(kredi\)\. Bu kayıtların hatırlatmaları eski kayıtlardan hesaplanır ve sınırlıdır/
  );
  const notice = view.find(node => node.attributes?.role === 'status' && node.textContent.includes('takibinde olmayan'));
  const loan = notice.find(node => node.tag === 'button' && node.textContent === 'Taşıt (kredi)');
  loan.listeners.click({ currentTarget: loan });
  await settle();
  assert.ok(requests.includes('/api/takip/krediler/7'), 'kredi ekranı açılır');
  const { nodes: bos } = await openApp(false, { [homeSummaryPath]: sampleHome() });
  assert.doesNotMatch(bos.get('#view').textContent, /takibinde olmayan/);
});
test('eski sunucuda ana sayfa ucu yoksa (404) ayrı uçlara geri düşer, sonraki açılışta ucu yeniden denemez', async () => {
  const { app, nodes, requests } = await openApp(false, { [homeSummaryPath]: { $status: 404 } });
  for (const old of ['/api/rapor/panel', '/api/kasa-esikleri', '/api/takip/ozet?gun=30'])
    assert.ok(requests.includes(old), `${old} istenir`);
  assert.match(nodes.get('#view').textContent, /123,00/);
  await app.navigate('home');
  assert.equal(requests.filter(path => path === homeSummaryPath).length, 1);
  assert.equal(requests.filter(path => path === '/api/rapor/panel').length, 2);
});
// IST4: birleşik ucun sunucu hatası (5xx) ana sayfayı düşürmez. Kasa bakiyeleri panel ucundan gelir; eşikler ve takip
// özeti kendi uçlarından yüklenir ve kendi hatalarını gösterir. 404'ten farklı olarak uç sonraki açılışta yeniden denenir.
test('ana sayfa ucunun sunucu hatası (5xx) paneli ayrı uçtan dener; sonraki açılışta birleşik uç yeniden denenir', async () => {
  for (const status of [500, 503]) {
    const { app, nodes, requests } = await openApp(false, { [homeSummaryPath]: { $status: status } });
    for (const old of ['/api/rapor/panel', '/api/kasa-esikleri', '/api/takip/ozet?gun=30'])
      assert.ok(requests.includes(old), `${status}: ${old} istenir`);
    assert.match(nodes.get('#view').textContent, /123,00/);
    assert.doesNotMatch(nodes.get('#view').textContent, /Kayıtlar yüklenemedi/);
    await app.navigate('home');
    assert.equal(requests.filter(path => path === homeSummaryPath).length, 2, `${status}: uç yeniden denenir`);
  }
});
test('ana sayfa ucu da panel ucu da hata verirse yeniden deneme gösterilir', async () => {
  const { nodes, requests } = await openApp(false, {
    [homeSummaryPath]: { $status: 500 },
    '/api/rapor/panel': { $status: 503, hata: 'Sunucu meşgul.' },
  });
  assert.ok(requests.includes('/api/rapor/panel'));
  assert.match(nodes.get('#view').textContent, /Kayıtlar yüklenemedi.*Sunucu meşgul\./);
  assert.ok(nodes.get('#view').find(node => node.tag === 'button' && node.textContent === 'Yeniden dene'));
});
test('özetsiz ana sayfa yanıtında kasa bakiyeleri görünür; özet ve eşik hataları ayrı ayrı gösterilir', async () => {
  // Sunucu (RDY) takip özeti hesaplanamayınca paneli özetsiz (null) döndürür; istemci özeti ve eşikleri kendi uçlarından dener.
  const home = { ...sampleHome(), kasaEsikleri: null, takipOzeti: null };
  const { nodes } = await openApp(false, {
    [homeSummaryPath]: home,
    '/api/takip/ozet?gun=30': { $status: 500 },
    '/api/kasa-esikleri': { $status: 503, hata: 'Veritabanı meşgul.' },
  });
  await settle();
  const view = nodes.get('#view').textContent;
  assert.match(view, /900,00/);
  assert.match(view, /MEZAT/);
  assert.match(view, /600,00/);
  assert.match(view, /Ödeme özeti yüklenemedi: /);
  assert.match(view, /Kanal uyarıları yüklenemedi: Veritabanı meşgul\./);
  assert.match(view, /Kart borcu yüklenemedi\./);
  assert.ok(nodes.get('#view').find(node => node.tag === 'button' && node.textContent === 'Uyarıları yeniden yükle'));
});
test('özet yüklenemediğinde kart borcu iletisi eşikler sonradan çizilince "yükleniyor"a dönmez', async () => {
  const { nodes } = await openApp(false, {
    [homeSummaryPath]: { $status: 404 },
    '/api/takip/ozet?gun=30': { $status: 500 },
    '/api/kasa-esikleri': () => new Promise(resolve => setImmediate(() => setImmediate(() => resolve([])))),
  });
  await settle();
  const view = nodes.get('#view').textContent;
  assert.match(view, /Kart borcu yüklenemedi\./);
  assert.doesNotMatch(view, /Kart borcu yükleniyor/);
});
test('ekran değişince ana sayfa isteği iptal edilir; geç yanıt yeni ekrana yansımaz, hata bildirimi çıkmaz', async () => {
  let release;
  const { app, nodes, calls } = await openApp(false, {
    [homeSummaryPath]: () =>
      new Promise(resolve => {
        release = resolve;
      }),
    '/api/rapor/haftalik': [],
  });
  const home = calls.find(call => call.path === homeSummaryPath);
  assert.equal(home.signal.aborted, false);
  await app.navigate('weekly');
  assert.equal(home.signal.aborted, true, 'Önceki ekranın isteği iptal edildi.');
  assert.match(nodes.get('#view').textContent, /Henüz kasa dönemi yok/);
  release(sampleHome());
  await settle();
  assert.match(nodes.get('#view').textContent, /Henüz kasa dönemi yok/);
  assert.equal((nodes.get('#notifications')?.textContent ?? '') + (nodes.get('#alerts')?.textContent ?? ''), '');
  assert.equal(calls.find(call => call.path === '/api/rapor/haftalik').signal.aborted, false);
  // Rapor dışı okumalar (ve yazmalar) ekran sinyaline bağlanmaz.
  assert.equal(calls.find(call => call.path === '/api/alis/inceleme-ozeti?adet=4').signal, undefined);
});
// webui-6 (ana sayfa bölümü): editörün ana sayfası inceleme kutusu için bütün alış listesini (kalem, dağılım ve ödemeleriyle)
// indirmez; sunucu yalnız inceleme bekleyen sayısını ve en yeni dört alışı döndürür.
const reviewSummaryPath = '/api/alis/inceleme-ozeti?adet=4';
const reviewPurchase = (id, tedarikci, durum = 'Incelemede') => ({
  id,
  surum: 2,
  tarih: '2026-09-27',
  tedarikci,
  alici: 'İpek',
  durum,
  kalemler: [],
  odemeler: [],
  toplam: 100,
  odenen: 0,
  kalan: 100,
});
const nodesWhere = (node, predicate, found = []) => {
  for (const child of node.children)
    if (child && typeof child === 'object') {
      if (predicate(child)) found.push(child);
      nodesWhere(child, predicate, found);
    }
  return found;
};
test('ana sayfa inceleme bekleyen alışları özet uçtan okur; bütün alış listesini indirmez', async () => {
  const { nodes, requests } = await openApp(false, {
    [reviewSummaryPath]: { sayi: 7, ogeler: [reviewPurchase(12, 'Kargo'), reviewPurchase(11, 'Ambalaj')] },
  });
  assert.ok(!requests.includes('/api/alis'), 'Alış listesi istenmez.');
  assert.equal(requests.filter(path => path === reviewSummaryPath).length, 1);
  const view = nodes.get('#view');
  assert.match(view.textContent, /7 alış inceleme bekliyor/);
  assert.deepEqual(
    nodesWhere(view, node => node.className === 'purchase-row').map(row => row.attributes['aria-label'].split(',')[0]),
    ['Kargo', 'Ambalaj']
  );
});
test('eski sunucuda inceleme özeti ucu yoksa (404/405) alış listesine geri düşer; uç yeniden denenmez', async () => {
  for (const status of [404, 405]) {
    const list = [
      reviewPurchase(5, 'Beşinci'),
      reviewPurchase(4, 'Dördüncü', 'Taslak'),
      reviewPurchase(3, 'Üçüncü'),
      reviewPurchase(2, 'İkinci'),
      reviewPurchase(1, 'Birinci', 'Onaylandi'),
    ];
    const { app, nodes, requests } = await openApp(false, { [reviewSummaryPath]: { $status: status }, '/api/alis': list });
    assert.match(nodes.get('#view').textContent, /3 alış inceleme bekliyor/, `${status}`);
    assert.equal(nodesWhere(nodes.get('#view'), node => node.className === 'purchase-row').length, 3);
    await app.navigate('home');
    assert.equal(requests.filter(path => path === reviewSummaryPath).length, 1, `${status}: uç yeniden denenmez`);
    assert.equal(requests.filter(path => path === '/api/alis').length, 2);
  }
});
test('inceleme özeti ucunun başka hatası listeye düşürmez', async () => {
  const { nodes, requests } = await openApp(false, { [reviewSummaryPath]: { $status: 500, hata: 'Veritabanı meşgul.' } });
  assert.ok(!requests.includes('/api/alis'));
  assert.match(nodes.get('#view').textContent, /Veritabanı meşgul./);
});
test('izleyici ana sayfası inceleme özetini istemez', async () => {
  const { requests } = await openApp(false, { '/api/auth/me': { rol: 'viewer' } });
  assert.ok(!requests.includes(reviewSummaryPath));
  assert.ok(!requests.includes('/api/alis'));
});
test('oturum kapanınca süren rapor isteği de iptal edilir', async () => {
  const { app, calls } = await openApp(false, { '/api/rapor/haftalik': () => new Promise(() => {}) });
  const weekly = app.navigate('weekly');
  await settle();
  const call = calls.find(item => item.path === '/api/rapor/haftalik');
  assert.equal(call.signal.aborted, false);
  app.clearSession();
  await weekly;
  assert.equal(call.signal.aborted, true);
});
test('ekrana bağlı okumalar yalnız rapor ve takip özeti GET istekleridir; iptal hatası ayırt edilir', () => {
  for (const path of [
    '/api/rapor/ana-sayfa?gun=30',
    '/api/rapor/panel',
    '/api/rapor/haftalik',
    '/api/rapor/aylik?yil=2026&ay=9',
    '/api/takip/ozet?gun=7',
  ])
    assert.equal(ui.screenBoundRead(path, 'GET'), true, path);
  for (const [path, method] of [
    ['/api/rapor/haftalik', 'POST'],
    ['/api/alis', 'GET'],
    ['/api/takip/kartlar', 'GET'],
    ['/api/kasa-esikleri', 'GET'],
    ['/api/raporlar', 'GET'],
  ])
    assert.equal(ui.screenBoundRead(path, method), false, `${method} ${path}`);
  assert.equal(ui.isAbortError(ui.abortedRequestError()), true);
  assert.equal(ui.isAbortError(new DOMException('x', 'AbortError')), true);
  assert.equal(ui.isAbortError(new Error('Sunucuya ulaşılamadı.')), false);
});
test('haftalık raporun veri sağlığı uyarısı seçili dönemden bağımsız, listenin üstünde görünür', async () => {
  const week = (start, end, extra = {}) => ({
    donem: { start, end, yil: 2026, ay: 1 },
    kanallar: [],
    toplamGelen: 0,
    toplamGiden: 0,
    kasaSonucu: 0,
    kasaDevir: 900,
    dagilimBekleyenTutar: 0,
    ...extra,
  });
  const warning = 'Rapor ufkunun (30.09.2027) ötesinde 1 kayıt var; en geç 22.06.2206.';
  const weeks = [week('2026-01-05', '2026-01-11'), week('2026-01-12', '2027-09-30', { veriSagligiUyarisi: warning })];
  const { app, nodes, responses } = await openApp(false, { '/api/rapor/haftalik': weeks });
  await app.navigate('weekly');
  const view = nodes.get('#view');
  assert.equal(view.children[0].attributes.role, 'alert');
  assert.equal(view.children[0].textContent, warning);
  const period = view.find(node => node.attributes.name === 'donem');
  period.value = '2026-01-05';
  period.listeners.change();
  assert.equal(view.children[0].textContent, warning, 'Başka dönem seçilince de uyarı kalır.');
  responses['/api/rapor/haftalik'] = [week('2026-01-05', '2026-01-11')];
  await app.navigate('weekly');
  assert.equal(
    nodes.get('#view').find(node => node.attributes.role === 'alert'),
    null
  );
  assert.equal(ui.dataHealthWarning([{ veriSagligiUyarisi: '  ' }, {}]), null);
  assert.equal(ui.dataHealthWarning(null), null);
});
test('kayıt sürerken kapatılıp yerine açılan pencere önceki kaydın geç gelen başarısıyla kapanmaz', async () => {
  const { app, nodes, finish } = await pendingExpense();
  const vazgec = cancelButton(nodes);
  vazgec.listeners.click({ currentTarget: vazgec });
  assert.equal(nodes.get('#modal').open, false);
  app.channelDialog();
  assert.equal(nodes.get('#modal-title').textContent, 'Kanal ekle');
  await finish({ id: 5, tarih: '2026-09-23', cari: 'Kargo', tutarTl: 75, kanal: 'A', tip: 'Cari', not: null });
  assert.equal(nodes.get('#modal').open, true, 'Yeni pencere açık kalır.');
  assert.equal(nodes.get('#modal-title').textContent, 'Kanal ekle');
  assert.ok(formField(nodes, 'ad'), 'Yeni pencerenin formu korunur.');
  assert.match(nodes.get('#notifications').textContent, /Gider kaydedildi\./);
  // Yeni pencerenin kendi kaydı pencereyi yine kapatır.
  formField(nodes, 'ad').value = 'B';
  formField(nodes, 'acilisDevri').value = '0';
  await submitDialog(nodes);
  assert.equal(nodes.get('#modal').open, false);
});
test('kayıt sürerken engellenen ESC bildirimi gerçek davranışı anlatır', async () => {
  const { nodes, cancel } = await pendingExpense();
  assert.equal(cancel(true), true);
  const text = nodes.get('#notifications').textContent;
  assert.match(text, /yanıt gelene kadar pencere açık kalır/);
  assert.match(text, /hata olursa burada görünür/);
  assert.match(text, /Vazgeç’e basın: kayıt durmaz/);
  assert.match(text, /hata olursa bildirim olarak gösterilir, başarılı kayıt ekrana yansır/);
  assert.doesNotMatch(text, /sonucu bildirim olarak görürsünüz/);
});
test('işaretli tutar etiketi işaret seçicisine değil tutar alanına bağlıdır; kimlikler tekildir', async () => {
  const { app, nodes } = await openApp(false);
  const signedLabel = () =>
    nodes.get('#modal-content').find(node => node.tag === 'label' && (node.className || '').split(' ').includes('signed-field'));
  app.channelDialog();
  const first = signedLabel();
  const amountInput = formField(nodes, 'acilisDevri');
  assert.ok(first.attributes.for, 'Etiket bir alana bağlı.');
  assert.equal(first.attributes.for, amountInput.attributes.id);
  assert.notEqual(formField(nodes, 'acilisDevriIsaret').attributes.id, first.attributes.for);
  app.channelDialog();
  assert.notEqual(signedLabel().attributes.for, first.attributes.for, 'Yeni pencerede yeni kimlik.');
});

// Rapor kuralı kararları (2026-09-27). K2: aylık raporda kredi girişi ayrı sütun, ay sonucu kredi hariç; K4: kapatılmış ay
// dondurulmuş raporla ve işaretle; K1: takip başlangıcı öncesi uyarısı; K3: gider formunda yalnız takipteki kartlar.
test('aylık rapor kredi girişini ayrı gösterir, kapatılmış ayı ve veri sağlığı uyarısını işaretler', async () => {
  const path = `/api/rapor/aylik?yil=${yearNow}&ay=${monthNumberNow}`;
  const warning = 'Takip başlangıcından önce tarihli 2 kayıt, toplam 5.300,00 ₺ — raporlarda farklı işlenir.';
  const report = {
    yil: yearNow,
    ay: monthNumberNow,
    kuralSurumu: 2,
    krediGirisi: 170000,
    veriSagligiUyarisi: warning,
    genelGelir: 0,
    genelGider: 0,
    dagilimBekleyenTutar: 0,
    kanallar: [
      { kanal: 'MEZAT', gelen: 80000, krediGirisi: 120000, cariGiden: 100000, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: -20000 },
    ],
  };
  const { app, nodes, responses } = await openApp(false, { [path]: report });
  await app.navigate('monthly');
  const text = nodes.get('#view').textContent;
  assert.match(text, /Ay sonucu \(kredi hariç\)/);
  assert.match(text, /Kredi girişi/);
  assert.ok(text.includes(`Kredi girişi: ${money(170000)}`), 'Kredi girişi toplamı notta.');
  assert.ok(text.includes(`Kanala dağıtılmayan eski kredi çekimi: ${money(50000)}`), 'Eski kredi çekimi ayrıca söylenir.');
  assert.match(text, /Takip başlangıcından önce tarihli 2 kayıt/);
  assert.doesNotMatch(text, /Kapatılmış ay/);
  // Kural 1 ile dondurulmuş kapalı ay: kredi Gelen'in içinde; sütun bilgi amaçlıdır ve bu açıkça söylenir.
  responses[path] = {
    ...report,
    kuralSurumu: 1,
    krediGirisi: undefined,
    veriSagligiUyarisi: undefined,
    dondurulmus: true,
    kanallar: [{ ...report.kanallar[0], gelen: 200000, aySonucu: 100000 }],
  };
  await app.navigate('monthly');
  const frozen = nodes.get('#view').textContent;
  assert.match(frozen, /Kapatılmış ay/);
  assert.match(frozen, /eski kuralla dondurulmuştur/);
  assert.doesNotMatch(frozen, /kredi hariç/);
  assert.doesNotMatch(frozen, /Takip başlangıcından önce/);
});

test('gider formu kredi kartında yalnız takipteki açık kartları listeler ve kart seçmeden kaydetmez', async () => {
  const cards = [
    { id: 1, ad: 'Eski kart', yeniTakip: false, aktif: true },
    { id: 2, ad: 'Takipli', yeniTakip: true, aktif: true },
    { id: 3, ad: 'Kapalı', yeniTakip: true, aktif: false },
  ];
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': cards,
    '/api/islemler': { id: 9 },
    '/api/islemler/benzerlik': [],
  });
  await app.expenseDialog();
  const card = formField(nodes, 'krediKartiId');
  assert.deepEqual(
    card.children.map(option => option.textContent),
    ['Kart seçin', 'Takipli']
  );
  for (const [name, value] of Object.entries({ cari: 'Market', tutarTl: '75', kanal: 'A', tarih: '2026-09-23', tip: 'KrediKarti' }))
    formField(nodes, name).value = value;
  formField(nodes, 'tip').listeners.change();
  await submitDialog(nodes);
  assert.equal(
    calls.some(call => call.path === '/api/islemler' && call.method === 'POST'),
    false
  );
  assert.match(nodes.get('#modal-content').textContent, /yeni takipteki bir kart seçin/);
  card.value = '2';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/islemler' && call.method === 'POST').body.krediKartiId, 2);
});

test('eski kredi kartı gideri düzenlenirken kendi kartıyla ya da kartsız kalır ve kaydedilir', async () => {
  const cards = [
    { id: 1, ad: 'Eski kart', yeniTakip: false, aktif: true },
    { id: 2, ad: 'Takipli', yeniTakip: true, aktif: true },
  ];
  const eski = { id: 20, tarih: '2026-08-05', tutarTl: 75, cari: 'Eski', tip: 'KrediKarti', kanal: 'A', not: '', krediKartiId: 1 };
  const kartsiz = { ...eski, id: 21, krediKartiId: null };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': cards,
    '/api/islemler/20': eski,
    '/api/islemler/21': kartsiz,
  });
  await app.expenseDialog(eski);
  assert.deepEqual(
    formField(nodes, 'krediKartiId').children.map(option => option.textContent),
    ['Kart seçin', 'Takipli', 'Eski kart (eski kayıt)']
  );
  formField(nodes, 'tutarTl').value = '80';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/islemler/20').body.krediKartiId, 1);
  await app.expenseDialog(kartsiz);
  assert.equal(formField(nodes, 'krediKartiId').children[0].textContent, '— Kartsız eski kayıt —');
  formField(nodes, 'tutarTl').value = '90';
  await submitDialog(nodes);
  const save = calls.find(call => call.path === '/api/islemler/21');
  assert.equal(save.body.krediKartiId, null);
  assert.equal(save.body.tutarTl, 90);
});

// ---- IST4: aylık sayfa sunucu sayıları, ana sayfa dayanıklılığı, kilit açma uyarısı, K3 kart listesi, iz kimliği ----
test('aylık rapor sunucu sayılarını kullanıcı girdisi gibi ayrıştırmaz: eksi ve üslü kredi girişiyle de sayfa çizilir', async () => {
  const path = `/api/rapor/aylik?yil=${yearNow}&ay=${monthNumberNow}`;
  // 1e-7 JSON'dan sayı olarak gelir; String(1e-7) === '1e-7' kullanıcı girdisi ayrıştırıcısını düşürürdü.
  const report = {
    yil: yearNow,
    ay: monthNumberNow,
    kuralSurumu: 2,
    krediGirisi: -1500.5,
    genelGelir: 0,
    genelGider: 0,
    dagilimBekleyenTutar: 0,
    kanallar: [
      { kanal: 'MEZAT', gelen: 100, krediGirisi: -1000.25, cariGiden: 0, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: 100 },
      { kanal: 'PERAKENDE', gelen: 0, krediGirisi: 1e-7, cariGiden: 0, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: 0 },
    ],
  };
  const { app, nodes } = await openApp(false, { [path]: report });
  await app.navigate('monthly');
  const text = nodes.get('#view').textContent;
  assert.doesNotMatch(text, /Kayıtlar yüklenemedi/);
  assert.ok(text.includes(`Kredi girişi: ${money(-1500.5)}`), 'Eksi kredi girişi notta.');
  assert.ok(text.includes(`Kanala dağıtılmayan eski kredi çekimi: ${money(-500.25)}`), 'Kuruş farkı sunucu sayılarından hesaplanır.');
  assert.equal(ui.serverCents(-1500.5), -150050);
  assert.equal(ui.serverCents(1e-7), 0);
  assert.equal(ui.serverCents(null), 0);
  assert.equal(ui.serverCents(0.29), 29);
});
test('kural 1 ile dondurulmuş ayın kilidi açılırken rapor güncel kuralla yeniden hesaplanacağı uyarılır', async () => {
  const current = `/api/rapor/aylik?yil=${yearNow}&ay=${monthNumberNow}`;
  const august = '/api/rapor/aylik?yil=2026&ay=8';
  const channels = [
    { kanal: 'MEZAT', gelen: 200, krediGirisi: 120, cariGiden: 100, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: 100 },
  ];
  const { app, nodes, responses, calls } = await openApp(false, {
    [current]: { yil: yearNow, ay: monthNumberNow, kuralSurumu: 2, kanallar: channels },
    [august]: { yil: 2026, ay: 8, kuralSurumu: 1, dondurulmus: true, kanallar: channels },
    '/api/ay-kilidi': { surum: 4, kilitliSonTarih: '2026-08-31', gecmis: [] },
    '/api/ay-kilidi/ac': { surum: 5, kilitliSonTarih: '2026-07-31', gecmis: [] },
  });
  await app.navigate('monthly');
  const show = async month => {
    viewField(nodes, 'ay').value = month;
    await clickView(nodes, 'Ayı göster');
  };
  await show('2026-08');
  await clickView(nodes, 'Bu ayı ve sonrasını aç');
  const warning = nodes.get('#modal-content').textContent;
  assert.match(warning, /eski kuralla \(kural 1\) kapatılmış/);
  assert.match(warning, /yeniden kapatınca da güncel kuralla/);
  assert.match(warning, /takipli kredi çekimi Gelen ve Ay sonucundan çıkar/);
  formField(nodes, 'aciklama').value = 'Ağustos düzeltmesi';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/ay-kilidi/ac').body.ay, 8, 'Uyarı yalnız bilgidir; açma yine gönderilir.');
  // Kural 2 ile dondurulmuş ay ve kapatma onayı uyarı taşımaz.
  responses[august] = { ...responses[august], kuralSurumu: 2 };
  await show('2026-08');
  await clickView(nodes, 'Bu ayı ve sonrasını aç');
  assert.doesNotMatch(nodes.get('#modal-content').textContent, /kural 1/);
  const frozenOld = { yil: 2026, ay: 7, kuralSurumu: 1, dondurulmus: true, kanallar: channels };
  responses['/api/ay-kilidi'] = { surum: 5, kilitliSonTarih: '2026-06-30', gecmis: [] };
  const close = await app.monthlyUi.lockPanel('2026-07', () => {}, frozenOld);
  close.find(node => node.tag === 'button' && node.textContent === 'Bu ay sonuna kadar kilitle').listeners.click();
  assert.match(nodes.get('#modal-content').textContent, /son günü dahil bütün geçmiş/);
  assert.doesNotMatch(nodes.get('#modal-content').textContent, /kural 1/, 'Kapatma onayında uyarı yok.');
});
test('alış ödeme formu kartta yalnız takipteki açık kartları listeler; bağlanan giderin ve düzeltilen ödemenin kendi kartı korunur', async () => {
  const cards = [
    { id: 1, ad: 'Eski kart', yeniTakip: false, aktif: true },
    { id: 2, ad: 'Takipli', yeniTakip: true, aktif: true },
    { id: 3, ad: 'Kapalı', yeniTakip: true, aktif: false },
  ];
  const purchase = { id: 6, surum: 2, kalan: 100, durum: 'Onaylandi', odemeler: [] };
  const expense = { id: 20, tarih: '2026-09-23', tutarTl: 75, cari: 'Mal', krediKartiId: 1 };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kredikartlari': cards,
    '/api/islemler': [expense],
    '/api/alis/6/odemeler': purchase,
    '/api/alis/6/odemeler/8': purchase,
    '/api/alis/kanallar': [],
    '/api/islemler/benzerlik': [],
  });
  const labels = () => formField(nodes, 'krediKartiId').children.map(option => option.textContent);
  await app.paymentDialog(purchase);
  assert.deepEqual(labels(), ['Nakit / havale', 'Takipli']);
  const existing = formField(nodes, 'mevcutIslemId');
  existing.value = '20';
  existing.listeners.change();
  assert.deepEqual(labels(), ['Nakit / havale', 'Takipli', 'Eski kart (eski kayıt)'], 'Bağlanan giderin eski kartı gösterilir.');
  existing.value = '';
  existing.listeners.change();
  assert.deepEqual(labels(), ['Nakit / havale', 'Takipli'], 'Seçim kalkınca eski kart yeni ödemede seçilemez.');
  existing.value = '20';
  existing.listeners.change();
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/alis/6/odemeler').body.krediKartiId, 1);
  // Takipsiz eski kartla girilmiş ödeme kendi kartıyla düzeltilir (kart takibindeki ödeme ayrı testte: yalnız taşınır).
  const payment = {
    id: 8,
    tarih: '2026-09-23',
    tutar: 50,
    krediKartiId: 1,
    krediKartiAdi: 'Eski kart',
    dagilimBekliyor: false,
    dagilimlar: [],
  };
  await app.paymentDialog({ ...purchase, odemeler: [payment] }, payment);
  assert.deepEqual(labels(), ['Nakit / havale', 'Takipli', 'Eski kart (eski kayıt)']);
  formField(nodes, 'aciklama').value = 'Tarih düzeltmesi';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/alis/6/odemeler/8').body.krediKartiId, 1);
  assert.deepEqual(
    ui.paymentCardChoices(cards, 9).map(option => option.label),
    ['Nakit / havale', 'Takipli', 'Kart #9 (eski kayıt)']
  );
  assert.deepEqual(
    ui.paymentCardChoices(cards, 2).map(option => option.value),
    ['', 2]
  );
});
test('sunucu hatasının (5xx) iz kimliği hata iletisinde kısa "Hata kodu" olarak görünür', async () => {
  const trace = '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01';
  assert.equal(ui.traceCode(trace), '4bf92f35');
  assert.equal(ui.traceCode('0HN7ABCDEF:00000002'), '0HN7ABCDEF:00000002');
  assert.equal(ui.traceCode('0HN7ABCDEFGHIJKLMNOPQRSTU:00000002'), '0HN7ABCDEFGH');
  for (const value of [null, undefined, '  ', '<script>', 42]) assert.equal(ui.traceCode(value), null, String(value));
  assert.equal(
    errorMessage({ title: 'An error occurred while processing your request.', status: 500, traceId: trace }, 500),
    'İşlem tamamlanamadı. Lütfen yeniden deneyin. Hata kodu: 4bf92f35'
  );
  assert.equal(
    errorMessage({ detail: 'Kayıt bir veri bütünlüğü kuralına takıldığı için kaydedilmedi.', traceId: trace }, 500),
    'Kayıt bir veri bütünlüğü kuralına takıldığı için kaydedilmedi. Hata kodu: 4bf92f35'
  );
  assert.equal(errorMessage({ hata: 'Kayıt değişti.', traceId: trace }, 409), 'Kayıt değişti.', 'İstemci hatasında iz eklenmez.');
  assert.equal(errorMessage(null, 500), 'İşlem tamamlanamadı. Lütfen yeniden deneyin.');
  const { app, nodes } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': [],
    '/api/islemler/benzerlik': [],
    '/api/islemler': {
      $status: 500,
      title: 'Veri bütünlüğü hatası',
      detail: 'Kayıt bir veri bütünlüğü kuralına takıldığı için kaydedilmedi.',
      traceId: trace,
    },
  });
  await app.expenseDialog();
  for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A', tarih: '2026-09-23' }))
    formField(nodes, name).value = value;
  await submitDialog(nodes);
  assert.match(nodes.get('#modal-content').textContent, /kaydedilmedi\. Hata kodu: 4bf92f35/);
});
test('gelir penceresinin dönem okuması ekran sinyaline bağlanmaz; pencere açılırken gezinme onu bozmaz', async () => {
  let release;
  const week = {
    donem: { start: '2026-09-21', end: '2026-09-27', yil: 2026, ay: 9 },
    kanallar: [],
    toplamGelen: 0,
    toplamGiden: 0,
    kasaSonucu: 0,
    kasaDevir: 0,
    dagilimBekleyenTutar: 0,
  };
  const monthly = `/api/rapor/aylik?yil=${yearNow}&ay=${monthNumberNow}`;
  const { app, nodes, calls } = await openApp(false, {
    '/api/rapor/haftalik': () =>
      new Promise(resolve => {
        release = resolve;
      }),
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/gelenler?donemStart=2026-09-21': [],
    [monthly]: { yil: yearNow, ay: monthNumberNow, kanallar: [] },
  });
  const opening = app.incomeDialog();
  await settle();
  const weekly = calls.find(call => call.path === '/api/rapor/haftalik');
  assert.equal(weekly.signal, undefined, 'Pencere verisi ekrana bağlı değildir.');
  await app.navigate('monthly'); // pencere açılmadan başka ekrana geçildi
  release([week]);
  await opening;
  await settle();
  assert.equal(nodes.get('#modal').open, true);
  assert.equal(nodes.get('#modal-title').textContent, 'Kanal geliri gir');
  assert.ok(formField(nodes, 'donemStart'), 'Dönem seçimi dolu.');
  assert.equal((nodes.get('#notifications')?.textContent ?? '') + (nodes.get('#alerts')?.textContent ?? ''), '');
  // Ekranın kendi rapor okumaları ekran sinyaline bağlı kalır.
  assert.ok(calls.find(call => call.path === monthly).signal);
});
test('benzer kayıt uyarısı sunucunun ±3 gün ve kanal kuralını anlatır, kredi taksidini ve kanal etiketini gösterir', async () => {
  const similar = [
    {
      kaynak: 'KrediTaksidi',
      id: 5,
      tarih: '2026-09-25',
      tutar: 75,
      aciklama: 'Taksit 3',
      krediKartiId: null,
      kanalEtiketi: 'MEZAT, PERAKENDE',
    },
    {
      kaynak: 'EskiKrediTaksidi',
      id: 6,
      tarih: '2026-09-20',
      tutar: 75,
      aciklama: 'Kredi',
      krediKartiId: null,
      kanalEtiketi: 'Genel kasa',
    },
  ];
  const { app, nodes } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/kredikartlari': [],
    '/api/islemler/benzerlik': similar,
  });
  await app.expenseDialog();
  for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A', tarih: '2026-09-23' }))
    formField(nodes, name).value = value;
  await submitDialog(nodes);
  const text = nodes.get('#modal-content').textContent;
  assert.ok(text.includes(ui.SIMILAR_RULE_TEXT), 'Kural metni gösterilir.');
  assert.match(ui.SIMILAR_RULE_TEXT, /±3 gün/);
  assert.match(ui.SIMILAR_RULE_TEXT, /kanalı belirsiz/);
  assert.match(ui.SIMILAR_RULE_TEXT, /çok kanallı/);
  assert.match(ui.SIMILAR_RULE_TEXT, /kart ödemeleri/);
  assert.doesNotMatch(text, /Aynı tarih, tutar/);
  assert.match(text, /Kredi taksidi #5 · .* · MEZAT, PERAKENDE/);
  assert.match(text, /Eski kredi taksidi #6 · .* · Genel kasa/);
});

test('transitioned card shows the old debt transfer, keeps it and the locked advance allocation uncancellable and posts a reasoned correction', async () => {
  const transferCard = {
    ...sampleCard,
    gecis: {
      kural: 'IslemTarihi',
      aciklama: 'Banka',
      onizleme: null,
      raporDisiEskiDusumTutari: 0,
      raporDisiIlkDusumTarihi: null,
      raporDisiSonDusumTarihi: null,
      tahminiKasaFarki: 0,
      uyari: null,
    },
    harcamalar: [
      {
        id: 10,
        islemId: null,
        tarih: '2026-09-23',
        aciklama: 'Onaylanan eski borç devri',
        tutar: 100,
        taksitSayisi: 1,
        iptal: false,
        dagilimlar: [{ kanalId: 1, kanal: 'MEZAT', tutar: 100 }],
      },
      {
        id: 11,
        islemId: null,
        tarih: '2026-09-24',
        aciklama: 'Satıcı iadesi',
        tutar: -30,
        taksitSayisi: 1,
        iptal: false,
        dagilimlar: [{ kanalId: 1, kanal: 'MEZAT', tutar: 30 }],
        kasadaSayilanDuzeltme: 30,
      },
    ],
    odemeler: [
      {
        id: 12,
        tarih: '2026-09-25',
        tutar: 0,
        kasaEtkisi: 0,
        not: 'Kilitli avans dağıtımı: 01.08.2026 tarihli ödemenin avansı',
        iptal: false,
        dagilimlar: [
          { kanalId: 1, kanal: 'MEZAT', tutar: 50 },
          { kanalId: null, kanal: 'Dağılım bekliyor', tutar: -50 },
        ],
        avansKaynakOdemeId: 3,
      },
    ],
  };
  const transfer = {
    harcamaId: 10,
    tarih: '2026-09-23',
    kalanBorc: 100,
    kasadaOncedenSayilanTutar: 80,
    iadeDuzeltmesi: 30,
    dagilimlar: [{ kanalId: 1, kanal: 'MEZAT', tutar: 100 }],
    kural: 'IslemTarihi',
    sistemKartBorcu: 80,
    raporDisiTutar: 0,
    acilisBorcu: 0,
    onerilenKasadaSayilanTutar: 80,
    enAzKasadaSayilanTutar: 80,
    duzeltilebilir: true,
    engel: null,
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/takip/kartlar/4': transferCard,
    '/api/takip/kartlar/4/devir': transfer,
    '/api/kanallar': [{ id: 1, ad: 'MEZAT', aktif: true }],
    '/api/takip/kartlar/4/devir-duzelt': sampleCard,
  });
  await app.navigate('cards', 4);
  const view = nodes.get('#view').textContent;
  assert.match(view, /Eski borç devri/);
  assert.match(view, /Kasada önceden sayılan.*80,00/);
  assert.match(view, /İadeyle kasaya dönen.*30,00/);
  assert.match(view, /Önceden sayılan.*30,00.*iade tarihinde kasaya döndü/);
  assert.match(view, /Kilitli avans dağıtımı/);
  const row = text => nodes.get('#view').find(node => node.tag === 'tr' && node.textContent.includes(text));
  assert.doesNotMatch(row('Onaylanan eski borç devri').textContent, /İptal et/);
  assert.match(row('Onaylanan eski borç devri').textContent, /Devri düzelt/);
  assert.match(row('Satıcı iadesi').textContent, /İptal et/);
  assert.doesNotMatch(view, /Ödemeyi iptal et/);
  await clickView(nodes, 'Devri düzelt');
  const debt = formField(nodes, 'kalanBorc');
  const counted = formField(nodes, 'kasadaOncedenSayilanTutar');
  assert.equal(debt.value, '100');
  assert.equal(counted.value, '80');
  debt.value = '70';
  debt.listeners.input();
  assert.equal(counted.value, '70');
  const share = formField(nodes, 'pay-tutar-0');
  share.value = '70';
  share.listeners.input({ target: share });
  formField(nodes, 'aciklama').value = 'Banka ekstresine göre';
  await submitDialog(nodes);
  const save = calls.find(call => call.path === '/api/takip/kartlar/4/devir-duzelt');
  assert.ok(save.body.istekId);
  assert.deepEqual(
    { ...save.body, istekId: null },
    {
      istekId: null,
      surum: 2,
      harcamaId: 10,
      kalanBorc: 70,
      kasadaOncedenSayilanTutar: 70,
      dagilimlar: [{ kanalId: 1, tutar: 70 }],
      aciklama: 'Banka ekstresine göre',
    }
  );
});

test('card transfer section shows why a correction is blocked and the page opens without the transfer read', async () => {
  const transferCard = {
    ...sampleCard,
    gecis: {
      kural: 'EtkiTarihi',
      aciklama: null,
      onizleme: null,
      raporDisiEskiDusumTutari: 0,
      raporDisiIlkDusumTarihi: null,
      raporDisiSonDusumTarihi: null,
      tahminiKasaFarki: 0,
      uyari: null,
    },
    harcamalar: [
      {
        id: 10,
        islemId: null,
        tarih: '2026-09-23',
        aciklama: 'Onaylanan eski borç devri',
        tutar: 100,
        taksitSayisi: 1,
        iptal: false,
        dagilimlar: [],
      },
    ],
  };
  const blocked = {
    harcamaId: 10,
    tarih: '2026-09-23',
    kalanBorc: 100,
    kasadaOncedenSayilanTutar: 100,
    iadeDuzeltmesi: 0,
    dagilimlar: [],
    kural: 'EtkiTarihi',
    sistemKartBorcu: 100,
    raporDisiTutar: 0,
    acilisBorcu: 0,
    onerilenKasadaSayilanTutar: 100,
    enAzKasadaSayilanTutar: 100,
    duzeltilebilir: false,
    engel: 'Devre ödeme kaydedilmiş: düzeltme önceki ödemelerin kasa etkisini değiştirirdi.',
  };
  const { app, nodes } = await openApp(false, { '/api/takip/kartlar/4': transferCard, '/api/takip/kartlar/4/devir': blocked });
  await app.navigate('cards', 4);
  assert.match(nodes.get('#view').textContent, /Devre ödeme kaydedilmiş/);
  assert.equal(
    nodes.get('#view').find(node => node.tag === 'button' && node.textContent === 'Devri düzelt'),
    null
  );
  assert.equal(
    nodes.get('#view').find(node => node.tag === 'button' && node.textContent === 'İptal et'),
    null
  );
  const missing = await openApp(false, { '/api/takip/kartlar/4': transferCard });
  await missing.app.navigate('cards', 4);
  assert.match(missing.nodes.get('#view').textContent, /Onaylanan eski borç devri/);
  assert.doesNotMatch(missing.nodes.get('#view').textContent, /İadeyle kasaya dönen/);
});

test('tracked card payments lock date, amount and card; detach shares must add up to the payment', () => {
  const cards = [
    { id: 1, ad: 'Eski', yeniTakip: false, aktif: true },
    { id: 2, ad: 'Takipli', yeniTakip: true, aktif: true },
    { id: 3, ad: 'Kapalı', yeniTakip: true, aktif: false },
  ];
  assert.equal(ui.trackedCardPayment(cards, { krediKartiId: 2 }), true);
  assert.equal(ui.trackedCardPayment(cards, { krediKartiId: 3 }), true);
  assert.equal(ui.trackedCardPayment(cards, { krediKartiId: 1 }), false);
  assert.equal(ui.trackedCardPayment(cards, { krediKartiId: null }), false);
  assert.equal(ui.trackedCardPayment(cards, null), false);
  assert.deepEqual(
    ui.detachAllocations(
      [
        { kanalId: 1, tutar: 7200 },
        { kanalId: 2, tutar: '4800,00' },
        { kanalId: 3, tutar: '' },
      ],
      12000
    ),
    [
      { kanalId: 1, tutar: 7200 },
      { kanalId: 2, tutar: 4800 },
    ]
  );
  assert.throws(
    () =>
      ui.detachAllocations(
        [
          { kanalId: 1, tutar: 7000 },
          { kanalId: 2, tutar: 4800 },
        ],
        12000
      ),
    /toplamı ödeme tutarına/
  );
  assert.throws(() => ui.detachAllocations([{ kanalId: 1, tutar: '' }], 10), /toplamı ödeme tutarına/);
  assert.throws(() => ui.detachAllocations([{ kanalId: 1, tutar: '-10' }], 10), /kuruş/);
});

test('installment fields are sent only for a real installment plan', () => {
  assert.deepEqual(ui.installmentFields('1'), {});
  assert.deepEqual(ui.installmentFields(''), {});
  assert.deepEqual(ui.installmentFields('3'), { taksitSayisi: 3 });
  assert.deepEqual(ui.installmentFields('3', '2026-10-06', '2026-09-20'), { taksitSayisi: 3, ilkKesimTarihi: '2026-10-06' });
  assert.deepEqual(ui.installmentFields('1', '2026-10-06', '2026-09-20'), { ilkKesimTarihi: '2026-10-06' });
  for (const bad of ['0', '61', '2.5', 'abc'])
    assert.throws(
      () => ui.installmentFields(bad),
      error => error.message === ui.INSTALLMENT_RANGE_MESSAGE && error.fields.taksitSayisi === ui.INSTALLMENT_RANGE_MESSAGE
    );
  assert.throws(
    () => ui.installmentFields('2', '2026-09-19', '2026-09-20'),
    error => Boolean(error.fields.ilkKesimTarihi)
  );
});

test('kart takibindeki alış ödemesi yalnız başka alışa taşınır; iptal harcamayı korurken gerçek kanal paylarını gönderir', async () => {
  const cards = [{ id: 2, ad: 'Takipli', yeniTakip: true, aktif: true }];
  const payment = {
    id: 8,
    tarih: '2026-09-23',
    tutar: 60,
    krediKartiId: 2,
    krediKartiAdi: 'Takipli',
    dagilimBekliyor: false,
    dagilimlar: [
      { kanalId: 1, kanal: 'A', tutar: 36 },
      { kanalId: 2, kanal: 'B', tutar: 24 },
    ],
  };
  const purchase = {
    id: 6,
    surum: 2,
    tarih: '2026-09-20',
    tedarikci: 'Yanlış',
    alici: 'Editör',
    durum: 'Onaylandi',
    kalemler: [],
    odemeler: [payment],
    toplam: 60,
    odenen: 60,
    kalan: 0,
  };
  const other = {
    id: 7,
    surum: 4,
    tarih: '2026-09-20',
    tedarikci: 'Doğru',
    alici: 'Editör',
    durum: 'Taslak',
    kalemler: [],
    odemeler: [],
    toplam: 60,
    odenen: 0,
    kalan: 60,
  };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kredikartlari': cards,
    '/api/alis': [purchase, other],
    '/api/alis/kanallar': [
      { id: 1, ad: 'A', aktif: true },
      { id: 2, ad: 'B', aktif: true },
    ],
    '/api/alis/6/odemeler/8': purchase,
    '/api/alis/6/odemeler/8/iptal': purchase,
  });
  await app.navigate('purchases');
  await app.paymentDialog(purchase, payment);
  assert.deepEqual(
    ['tarih', 'tutar', 'krediKartiId'].map(name => formField(nodes, name).disabled),
    [true, true, true]
  );
  assert.match(nodes.get('#modal-content').textContent, /kart takibinde/);
  formField(nodes, 'aciklama').value = 'Yanlış alış';
  await submitDialog(nodes);
  assert.equal(
    calls.find(call => call.path === '/api/alis/6/odemeler/8'),
    undefined,
    'Hedefsiz düzeltme gönderilmez.'
  );
  assert.match(nodes.get('#modal-content').textContent, /hedef alış seçin/);
  formField(nodes, 'hedefAlisId').value = '7';
  await submitDialog(nodes);
  const move = calls.find(call => call.path === '/api/alis/6/odemeler/8').body;
  assert.deepEqual([move.tarih, move.tutar, move.krediKartiId, move.hedefAlisId, move.hedefSurum], ['2026-09-23', 60, 2, 7, 4]);

  await app.cancelPayment(purchase, payment);
  assert.match(nodes.get('#modal-content').textContent, /Kart harcaması gerçek/);
  assert.deepEqual(
    [formField(nodes, 'ayir-1').value, formField(nodes, 'ayir-2').value],
    ['36', '24'],
    'Paylar ödemenin bugünkü paylarıyla başlar.'
  );
  const keep = formField(nodes, 'harcamayiKoru');
  keep.checked = true;
  keep.listeners.change();
  const first = formField(nodes, 'ayir-1');
  first.value = '30';
  first.listeners.input({ target: first });
  formField(nodes, 'aciklama').value = 'Başka alışın';
  await submitDialog(nodes);
  assert.equal(
    calls.find(call => call.path === '/api/alis/6/odemeler/8/iptal'),
    undefined
  );
  assert.match(nodes.get('#modal-content').textContent, /toplamı ödeme tutarına/);
  first.value = '36';
  first.listeners.input({ target: first });
  await submitDialog(nodes);
  assert.deepEqual(calls.find(call => call.path === '/api/alis/6/odemeler/8/iptal').body.kanalDagilimlari, [
    { kanalId: 1, tutar: 36 },
    { kanalId: 2, tutar: 24 },
  ]);
  // Harcama korunmazsa kanal payı gönderilmez: ödenmemiş harcama gideriyle kalkar.
  await app.cancelPayment(purchase, payment);
  formField(nodes, 'aciklama').value = 'Harcama yapılmadı';
  await submitDialog(nodes);
  assert.equal(calls.filter(call => call.path === '/api/alis/6/odemeler/8/iptal').at(-1).body.kanalDagilimlari, undefined);
});

test('yeni kart giderinde ve takipli kartla yeni alış ödemesinde taksit gövdeye yalnız planla taşınır', async () => {
  const cards = [{ id: 2, ad: 'Takipli', yeniTakip: true, aktif: true }];
  const purchase = { id: 6, surum: 2, kalan: 100, durum: 'Onaylandi', odemeler: [] };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kredikartlari': cards,
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true }],
    '/api/islemler': [],
    '/api/alis/6/odemeler': purchase,
    '/api/alis/kanallar': [],
    '/api/islemler/benzerlik': [],
    '/api/alis/baglanabilir-kart-harcamalari?krediKartiId=2&tutar=100.00': [],
  });
  await app.paymentDialog(purchase);
  const installments = () => formField(nodes, 'taksitSayisi').parentNode.parentNode.parentNode;
  assert.equal(installments().hidden, true, 'Nakit ödemede taksit yok.');
  const card = formField(nodes, 'krediKartiId');
  card.value = '2';
  card.listeners.change();
  await settle();
  assert.equal(installments().hidden, false);
  formField(nodes, 'taksitSayisi').value = '3';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/alis/6/odemeler').body.taksitSayisi, 3);

  await app.expenseDialog();
  const expenseInstallments = () => formField(nodes, 'taksitSayisi').parentNode.parentNode.parentNode;
  assert.equal(expenseInstallments().hidden, true);
  const type = formField(nodes, 'tip');
  type.value = 'KrediKarti';
  type.listeners.change();
  const expenseCard = formField(nodes, 'krediKartiId');
  expenseCard.value = '2';
  expenseCard.listeners.change();
  assert.equal(expenseInstallments().hidden, false);
  formField(nodes, 'cari').value = 'Telefon';
  formField(nodes, 'tutarTl').value = '3000';
  formField(nodes, 'kanal').value = 'A';
  formField(nodes, 'taksitSayisi').value = '6';
  await submitDialog(nodes);
  const body = calls.find(call => call.path === '/api/islemler' && call.method === 'POST').body;
  assert.deepEqual([body.taksitSayisi, body.ilkKesimTarihi, body.krediKartiId], [6, undefined, 2]);
});

// contract-6: düzenleme okunan kaydın sürümünü gönderir. Kayıt arada başka oturumda değiştiyse sunucu 409 verir: ileti formda görünür,
// arkadaki liste (gelirde formun dönem verisi) güncel kayıtlarla yenilenir; kayıt yeniden açılınca güncel sürümle kaydedilir.
test('gider düzenlemesi okunan sürümü gönderir; 409 iletisi formda görünür ve gider listesi yenilenir', async () => {
  const listPath = `/api/islemler?baslangic=${ui.today().slice(0, 8)}01&bitis=${ui.today()}`;
  const expense = { id: 20, tarih: ui.today(), tutarTl: 75, cari: 'Kargo', tip: 'Cari', kanal: 'A', not: '', krediKartiId: null, surum: 3 };
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [{ id: 1, ad: 'A', aktif: true, surum: 0 }],
    '/api/kredikartlari': [],
    [listPath]: [expense],
    '/api/islemler/20': { $status: 409, hata: 'Gider başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.' },
    '/api/islemler/benzerlik': [],
    '/api/islemler': { id: 21, surum: 0 },
  });
  await app.navigate('transactions');
  const listReads = () => calls.filter(call => call.path === listPath).length;
  const before = listReads();
  await app.expenseDialog(expense);
  formField(nodes, 'tutarTl').value = '80';
  await submitDialog(nodes);
  const put = calls.find(call => call.path === '/api/islemler/20');
  assert.deepEqual([put.method, put.body.surum, put.body.tutarTl], ['PUT', 3, 80]);
  assert.match(nodes.get('#modal-content').textContent, /Gider başka bir oturumda değişti/);
  assert.equal(listReads(), before + 1, 'Liste güncel sürümlerle yenilendi.');
  // Yeni gider sürüm 0 ile gider (sunucu oluşturmada yok sayar).
  await app.expenseDialog();
  for (const [name, value] of Object.entries({ cari: 'Kargo', tutarTl: '75', kanal: 'A', tarih: ui.today() }))
    formField(nodes, name).value = value;
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/islemler' && call.method === 'POST').body.surum, 0);
});
test('kanal ve kasa başlangıcı düzenlemesi okunan sürümü gönderir; 409’da Ayarlar yenilenir', async () => {
  const { app, nodes, calls } = await openApp(false, {
    '/api/kanallar': [{ id: 3, ad: 'Mezat', aktif: true, sira: 0, acilisDevri: 0, surum: 5 }],
    '/api/kanallar/3': { $status: 409, hata: 'Kanal başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.' },
    '/api/ayarlar': call =>
      call.method === 'PUT'
        ? { $status: 409, hata: 'Ayarlar başka bir oturumda değişti. Güncel değerleri yükleyip tekrar deneyin.' }
        : { takipBaslangic: '2026-01-01', kasaAcilisDevri: 0, izleyiciSifreVarMi: true, surum: 7 },
    '/api/yedek/durum': { otomatikEtkin: true },
    '/api/alicilar': [],
  });
  const settingsReads = () => calls.filter(call => call.path === '/api/ayarlar' && call.method === 'GET').length;
  let before = settingsReads();
  app.channelDialog({ id: 3, ad: 'Mezat', aktif: true, sira: 0, acilisDevri: 0, surum: 5 });
  formField(nodes, 'sira').value = '2';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/kanallar/3').body.surum, 5);
  assert.match(nodes.get('#modal-content').textContent, /Kanal başka bir oturumda değişti/);
  assert.equal(settingsReads(), before + 1, 'Ayarlar ekranı güncel kayıtlarla yenilendi.');
  before = settingsReads();
  app.openingDialog({ takipBaslangic: '2026-01-01', kasaAcilisDevri: 0, surum: 7 });
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/ayarlar' && call.method === 'PUT').body.surum, 7);
  assert.match(nodes.get('#modal-content').textContent, /Ayarlar başka bir oturumda değişti/);
  assert.equal(settingsReads(), before + 1);
});
test('gelir kaydı seçili satırın sürümünü gönderir; 409’da dönemin güncel toplamı forma yüklenir', async () => {
  const period = '2026-09-14';
  let reads = 0;
  const { app, nodes, calls } = await openApp(false, {
    '/api/rapor/haftalik': [{ donem: { start: period, end: '2026-09-20' } }],
    '/api/kanallar': [
      { id: 7, ad: 'Mağaza' },
      { id: 8, ad: 'Normal' },
    ],
    [`/api/gelenler?donemStart=${period}`]: () =>
      ++reads === 1 ? [{ kanalId: 7, kanal: 'Mağaza', tutarTl: 30, surum: 4 }] : [{ kanalId: 7, kanal: 'Mağaza', tutarTl: 55, surum: 5 }],
    '/api/gelenler': {
      $status: 409,
      hata: 'Bu dönem ve kanalın geliri başka bir oturumda değişti. Güncel toplamı yükleyip tekrar deneyin.',
    },
  });
  await app.incomeDialog(period);
  const channel = formField(nodes, 'kanal');
  channel.value = 'Mağaza';
  channel.listeners.change();
  formField(nodes, 'tutarTl').value = '40';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.path === '/api/gelenler' && call.method === 'PUT').body.surum, 4);
  assert.match(nodes.get('#modal-content').textContent, /geliri başka bir oturumda değişti/);
  assert.equal(reads, 2, 'Dönem gelirleri yeniden yüklendi.');
  assert.equal(formField(nodes, 'tutarTl').value, '55', 'Güncel toplam forma yüklendi.');
  channel.value = 'Normal';
  channel.listeners.change();
  formField(nodes, 'tutarTl').value = '10';
  await submitDialog(nodes);
  assert.equal(
    calls.filter(call => call.path === '/api/gelenler' && call.method === 'PUT').at(-1).body.surum,
    0,
    'Satırı olmayan kanal 0 gönderir.'
  );
});

// Hata iletisi kendiliğinden kaybolmaz (WAI-ARIA APG uyarı deseni; WCAG 2.2.3) ve assertive bölgede duyurulur. Canlı bölge
// sayfa açılışında boş olarak işaretlemede bulunur (MDN: role="alert" içeriği sonradan değişince duyurulur).
test('hata bildirimi role="alert" bölgesinde kalır ve kapatma düğmesiyle kapanır; bilgi iletisi kibar bölgede 6 sn sonra kalkar', async () => {
  const html = await readFile(new URL('../Kasa.Api/wwwroot/index.html', import.meta.url), 'utf8');
  assert.match(html, /<div id="alerts" role="alert"><\/div>/);
  assert.match(html, /<div id="notifications" role="status" aria-live="polite" aria-atomic="true"><\/div>/);
  const timers = [];
  const { app, nodes } = await openApp(false, {}, null, timers);
  timers.length = 0;
  app.toast('Gider kaydedildi.');
  assert.match(nodes.get('#notifications').textContent, /Gider kaydedildi\./);
  assert.deepEqual(
    timers.map(timer => timer.ms),
    [6000]
  );
  timers[0].fn();
  assert.equal(nodes.get('#notifications').textContent, '');
  app.toast('Gider kaydet: Bu ay kilitli.', true);
  app.toast('Sunucuya ulaşılamadı.', true);
  const alerts = nodes.get('#alerts');
  assert.match(alerts.textContent, /Gider kaydet: Bu ay kilitli\..*Sunucuya ulaşılamadı\./);
  assert.equal(nodes.get('#notifications').textContent, '', 'Hata kibar bölgeye yazılmaz.');
  assert.equal(timers.length, 1, 'Hata için kaldırma zamanlayıcısı kurulmaz.');
  const close = alerts.find(node => node.tag === 'button');
  assert.equal(close.attributes.type, 'button');
  assert.equal(close.attributes['aria-label'], 'Hata iletisini kapat');
  close.listeners.click({ currentTarget: close });
  assert.doesNotMatch(alerts.textContent, /Bu ay kilitli/);
  assert.match(alerts.textContent, /Sunucuya ulaşılamadı/);
  app.clearSession();
  assert.equal(alerts.textContent, '', 'Oturum kapanınca önceki oturumun hataları ekranda kalmaz.');
});

test('alış toplamı ve ödenmeyi bekleyen tutar sunucu tutarlarından kuruşla toplanır (kayan nokta kayması yok)', async () => {
  const purchases = [
    { id: 1, toplam: 0.1, kalan: 0.1, durum: 'Onaylandi' },
    { id: 2, toplam: 0.2, kalan: 0.2, durum: 'Incelemede' },
    { id: 3, toplam: 1234567.07, kalan: 0, durum: 'Incelemede' },
  ];
  assert.notEqual(0.1 + 0.2, 0.3, 'Kayan nokta toplamı kayar.');
  assert.deepEqual(ui.purchaseTotals(purchases), { total: 1234567.37, remaining: 0.3, reviewing: 2 });
  assert.deepEqual(ui.purchaseTotals([]), { total: 0, remaining: 0, reviewing: 0 });
  const { app, nodes } = await openApp(false, { '/api/alis': purchases, '/api/alis/kanallar': [] });
  await app.navigate('purchases');
  const strip = nodes.get('#view').find(node => node.className === 'summary-strip');
  assert.ok(strip.textContent.includes(`Alış toplamı${money(1234567.37)}3 kayıt`), strip.textContent);
  assert.ok(strip.textContent.includes(`Ödenmeyi bekleyen${money(0.3)}`), strip.textContent);
  assert.match(strip.textContent, /İnceleme bekleyen2/);
});

// Masaüstü Firefox ve Safari'de type="month" denetimi yok (MDN browser-compat-data html.elements.input.type_month:
// firefox ve safari version_added false). Ay seçici "‹ Eylül 2026 ›" düğmeleriyle YYYY-AA değeri üretir.
test('ay seçici type="month" kullanmaz; önceki/sonraki düğmeleri YYYY-AA değerini ve görünen ay adını değiştirir', async () => {
  for (const file of [
    'app.js',
    'ui-dom.js',
    'ui-shell.js',
    'monthly-ui.js',
    'finance-ui.js',
    'cash-controls-ui.js',
    'statement-import-ui.js',
    'notification-ui.js',
  ]) {
    assert.doesNotMatch(await readFile(new URL(`../Kasa.Api/wwwroot/${file}`, import.meta.url), 'utf8'), /type:\s*'month'/, file);
  }
  assert.equal(ui.shiftMonth('2026-01', -1), '2025-12');
  assert.equal(ui.shiftMonth('2026-12', 1), '2027-01');
  assert.equal(ui.shiftMonth('2026-09', 0), '2026-09');
  assert.equal(ui.monthLabel('2026-09'), 'Eylül 2026');
  assert.equal(ui.monthLabel('2027-01'), 'Ocak 2027');
  const previous = ui.shiftMonth(monthNow, -1);
  const [previousYear, previousMonth] = previous.split('-').map(Number);
  const channels = [
    { kanal: 'MEZAT', gelen: 200, krediGirisi: 0, cariGiden: 100, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: 100 },
  ];
  const previousPath = `/api/rapor/aylik?yil=${previousYear}&ay=${previousMonth}`;
  const { app, nodes, calls } = await openApp(false, {
    [`/api/rapor/aylik?yil=${yearNow}&ay=${monthNumberNow}`]: { yil: yearNow, ay: monthNumberNow, kuralSurumu: 2, kanallar: channels },
    [previousPath]: { yil: previousYear, ay: previousMonth, kuralSurumu: 2, kanallar: channels },
  });
  await app.navigate('monthly');
  const picker = nodes.get('#view').find(node => node.attributes.role === 'group' && node.attributes['aria-label'] === 'Rapor ayı');
  assert.ok(picker, 'Ay seçici erişilebilir adlı bir grup.');
  assert.ok(picker.textContent.includes(ui.monthLabel(monthNow)));
  const back = picker.find(node => node.tag === 'button' && node.attributes['aria-label'] === 'Önceki ay');
  assert.ok(picker.find(node => node.tag === 'button' && node.attributes['aria-label'] === 'Sonraki ay'));
  back.listeners.click({ currentTarget: back });
  assert.equal(viewField(nodes, 'ay').value, previous);
  assert.ok(picker.textContent.includes(ui.monthLabel(previous)));
  assert.equal(
    calls.some(call => call.path === previousPath),
    false,
    'Ay yalnız "Ayı göster" ile yüklenir; odak düğmede kalır.'
  );
  await clickView(nodes, 'Ayı göster');
  assert.ok(calls.some(call => call.path === previousPath));
  assert.ok(
    nodes
      .get('#view')
      .find(node => node.attributes.role === 'group')
      .textContent.includes(ui.monthLabel(previous))
  );
});

test('aylık gider ekranı ve şablonun ilk ayı aynı ay seçiciyi kullanır; şablon bu aydan önceye inemez', async () => {
  const next = ui.shiftMonth(monthNow, 1);
  const [nextYear, nextMonth] = next.split('-').map(Number);
  const nextPath = `/api/aylik-giderler?yil=${nextYear}&ay=${nextMonth}`;
  const { app, nodes, calls } = await openApp(false, {
    ...monthlyResponses(),
    [nextPath]: { yil: nextYear, ay: nextMonth, planlananToplam: 0, odenenToplam: 0, kayitlar: [] },
  });
  await app.navigate('monthly-expenses');
  const picker = nodes.get('#view').find(node => node.attributes.role === 'group' && node.attributes['aria-label'] === 'Aylık gider ayı');
  const forward = picker.find(node => node.attributes['aria-label'] === 'Sonraki ay');
  forward.listeners.click({ currentTarget: forward });
  assert.equal(viewField(nodes, 'ay').value, next);
  await clickView(nodes, 'Ayı göster');
  assert.ok(calls.some(call => call.path === nextPath));

  await app.monthlyUi.templateDialog();
  const first = nodes
    .get('#modal-content')
    .find(node => node.attributes.role === 'group' && node.attributes['aria-label'] === 'Bu aydan itibaren');
  assert.ok(first, 'Şablonun ilk ayı adlı bir grup.');
  assert.equal(
    nodes.get('#modal-content').find(node => node.tag === 'label' && node.find(child => child === first)),
    null,
    'Düğmeler <label> içinde değil: etikete tıklamak önceki ayı seçmez.'
  );
  const firstBack = first.find(node => node.attributes['aria-label'] === 'Önceki ay');
  assert.equal(firstBack.disabled, true, 'Şablon bu aydan önceye alınamaz.');
  const firstForward = first.find(node => node.attributes['aria-label'] === 'Sonraki ay');
  firstForward.listeners.click({ currentTarget: firstForward });
  assert.equal(firstBack.disabled, false);
  formField(nodes, 'ad').value = 'Kira';
  formField(nodes, 'tutar').value = '100';
  formField(nodes, 'dagilimTuru').value = 'Genel';
  await submitDialog(nodes);
  assert.equal(calls.find(call => call.method === 'POST' && call.path === '/api/aylik-giderler/sablonlar').body.gecerliAy, `${next}-01`);
});

// Kaydırılabilir kapsayıcılar (axe scrollable-region-focusable; WCAG 2.1.1): tablo kapsayıcısı ve genel kasa tutarı
// overflow:auto ile kayar. Taşma yazı tipine, yakınlaştırmaya ve pencereye göre değiştiği için her zaman klavyeyle odaklanan
// (tabindex 0), adlı bir bölgedir (role region + aria-label). Ekrandaki bölge adları birbirinden ayrıdır.
test('scrollable table wrappers and the cash total are focusable regions with distinct names', async () => {
  const scrollers = view => {
    const found = [];
    const visit = node => {
      if (!Array.isArray(node?.children)) return;
      if (/(^| )(table-wrap|cash-total)( |$)/.test(node.className || '')) found.push(node);
      node.children.forEach(visit);
    };
    visit(view);
    return found;
  };
  const names = view =>
    scrollers(view).map(node => {
      assert.equal(node.attributes.role, 'region', `${node.className} bölge`);
      assert.equal(node.attributes.tabindex, '0', `${node.className} sekme sırasında`);
      assert.ok(node.attributes['aria-label']?.trim(), `${node.className} adlı`);
      return node.attributes['aria-label'];
    });
  const today = ui.today();
  const [year, month] = today.split('-').map(Number);
  const channel = {
    kanal: 'Mağaza',
    gelen: 10,
    giden: 2,
    sonuc: 8,
    devir: 8,
    krediGirisi: 0,
    cariGiden: 2,
    sabitGider: 0,
    krediKarti: 0,
    ortakPay: 0,
    aySonucu: 8,
  };
  const { app, nodes } = await openApp(false, {
    '/api/kasa-kontrol': [
      {
        id: 1,
        surum: 1,
        kaydedildi: `${today}T09:00:00Z`,
        sistemBakiye: 100,
        gercekBakiye: 90,
        fark: -10,
        guncelSistemBakiye: 100,
        hesapTarihi: today,
      },
    ],
    '/api/rapor/haftalik': [{ donem: { start: today, end: today }, kasaDevir: 8, toplamGelen: 10, toplamGiden: 2, kanallar: [channel] }],
    [`/api/rapor/aylik?yil=${year}&ay=${month}`]: {
      yil: year,
      ay: month,
      kuralSurumu: 2,
      genelGelir: 0,
      genelGider: 0,
      dagilimBekleyenTutar: 0,
      kanallar: [channel],
    },
    [`/api/islemler?baslangic=${today.slice(0, 8)}01&bitis=${today}`]: [
      { id: 3, tarih: today, cari: 'Kargo', kanal: 'Mağaza', tip: 'Cari', tutarTl: 2 },
    ],
    '/api/kanallar': [{ id: 1, ad: 'Mağaza', aktif: true }],
  });
  await settle();
  assert.deepEqual(names(nodes.get('#view')), ['Genel kasa', 'Gerçek bakiye karşılaştırmaları']);
  for (const [screen, expected] of [
    ['weekly', ['Dönemin kanal sonuçları']],
    ['monthly', ['Aylık kanal sonuçları']],
    ['transactions', ['Gider kayıtları']],
  ]) {
    await app.navigate(screen);
    await settle();
    assert.deepEqual(names(nodes.get('#view')), expected, screen);
  }
});

// Kart ve kredi kutusu (düğme) içindeki tutar kaymaz; sığmazsa satır kayar. Bölünme yeri yalnız binlik ayırıcıdan sonradır
// (<wbr>): "₺999.999.999.99 / 9,99" gibi basamak grubunu bölen yanlış okuma olmaz; tutarın metni ve okunuşu değişmez.
test('card and loan boxes break a long amount only after a thousands separator and keep its full text', async () => {
  const amountIn = view =>
    view
      .find(node => node.tag === 'button' && node.className === 'finance-card')
      .children.find(node => node?.className === 'money' && node.tag === 'span');
  const big = 999999999999.99;
  const { app, nodes } = await openApp(false, {
    '/api/takip/kartlar': [{ ...sampleCard, borc: big }],
    '/api/takip/krediler': [{ ...sampleLoan, kalanPlanliOdeme: 1250.5 }],
  });
  for (const [screen, value] of [
    ['cards', big],
    ['loans', 1250.5],
  ]) {
    await app.navigate(screen);
    await settle();
    const node = amountIn(nodes.get('#view'));
    assert.ok(node, `${screen}: tutar düğümü`);
    assert.equal(node.textContent, money(value), `${screen}: tutarın metni aynı`);
    const parts = node.children.map(child => (typeof child === 'string' ? child : `<${child.tag}>`));
    assert.deepEqual(
      parts,
      money(value)
        .split(/(?<=\.)/)
        .flatMap((part, index) => (index ? ['<wbr>', part] : [part])),
      screen
    );
    assert.ok(
      parts.every(part => part === '<wbr>' || !/\.\S/.test(part)),
      `${screen}: basamak grubu bölünmez`
    );
  }
});

// ---------------------------------------------------------------------------------------------------------------------------
// Aşama 3 kapısı (UI raporu "Kapı"): birleştirme öncesi karakterizasyon testleri. Aşağıdakiler BUGÜNKÜ davranışı sabitler: act()
// düğmesi (meşgul durumu, hata gösterimi, yeniden yükleme), editör koruması, kanal payı etiketleri (shares), dağıtım editörleri
// (aylık gider, ekstre satırı, kart dağılımı, alış satırı) ve kayan nokta para hesaplarının bugünkü çıktısı. Birleştirmeler bu
// testleri değiştirmeden geçmelidir.
// KAPI_DOM: karakterizasyonla alınan DOM tabanları (domLines çıktısı); birleştirme öncesi kodla üretildi, elle düzenlenmez.
const KAPI_DOM = {
  'act-monthly-expenses': ['button class="button primary" type="button" onclick', '  "+ Şablon ekle"'],
  'act-cards': ['button class="button primary" type="button" onclick', '  "+ Kart ekle"'],
  'act-imports': ['button class="button primary" type="button" onclick', '  "+ PDF yükle"'],
  'act-notifications': ['button class="button small" type="button" onclick', '  "Okundu olarak işaretle"'],
  'act-props': ['button class="button primary" type="button" disabled onclick', '  "Bu cihazda bildirimleri aç"'],
  'shares-card': [
    'div class="allocation-tags"',
    '  span class="allocation-tag"',
    '    "MEZAT: ₺1.234,50"',
    '  span class="allocation-tag pending"',
    '    "Dağılım bekliyor: ₺30,00"',
    '  span class="allocation-tag"',
    '    "Ortak: ₺0,07"',
    'div class="allocation-tags"',
    '  span class="allocation-tag"',
    '    "MEZAT: -₺50,25"',
    'div class="allocation-tags"',
  ],
  'shares-loan': [
    'div class="allocation-tags"',
    '  span class="allocation-tag"',
    '    "A: ₺60.000,01"',
    '  span class="allocation-tag pending"',
    '    "Dağılım bekliyor: ₺59.999,99"',
    'div class="allocation-tags"',
    '  span class="allocation-tag"',
    '    "A: ₺12.000,00"',
  ],
  'shares-monthly': [
    'td',
    '  span',
    '    "Yalnız genel kasa"',
    'td',
    '  div class="allocation-tags"',
    '    span class="allocation-tag"',
    '      "B: ₺60,01"',
    '    span class="allocation-tag"',
    '      "Ortak: ₺40,00"',
    'td',
    '  span',
    '    "Yalnız genel kasa"',
  ],
  'shares-monthly-payment': [
    'div class="allocation-tags"',
    '  span class="allocation-tag"',
    '    "B: ₺60,01"',
    '  span class="allocation-tag"',
    '    "Ortak: ₺40,00"',
  ],
  'shares-import-history': [
    ['td', '  "Yalnız genel kasa"'],
    [
      'td',
      '  div class="allocation-tags"',
      '    span class="allocation-tag"',
      '      "Mezat: ₺7,49"',
      '    span class="allocation-tag"',
      '      "Genel kasa: ₺0,01"',
    ],
    ['td', '  "Kasa etkisi yok"'],
  ],
  'shares-import-preview': [
    'div class="allocation-tags"',
    '  span class="allocation-tag"',
    '    "Genel kasa: -₺100,00"',
    '  span class="allocation-tag"',
    '    "Mağaza: ₺0,00"',
  ],
  'shares-purchase-payment': [
    'div class="allocation-tags"',
    '  span class="allocation-tag"',
    '    "A: ₺49,99"',
    '  span class="allocation-tag"',
    '    "C: ₺0,01"',
    'span class="badge pending"',
    '  "Dağılım bekliyor"',
  ],
  'monthly-new': [
    'fieldset',
    '  legend',
    '    "Kasa dağılımı"',
    '  label',
    '    "Dağılım"',
    '    select name="dagilimTuru" value="" required onchange',
    '      option value=""',
    '        "Dağılım seçin"',
    '      option value="Genel"',
    '        "Yalnız genel kasa"',
    '      option value="Esit"',
    '        "Seçilen kanallara eşit"',
    '      option value="Ozel"',
    '        "Kanal tutarlarını gir"',
    '  div class="stack" hidden',
    '    div class="monthly-allocation"',
    '      label',
    '        "Ortak"',
    '        input name="dagilim-kanal-3" type="checkbox" value="3" onchange',
    '    div class="monthly-allocation"',
    '      label',
    '        "A"',
    '        input name="dagilim-kanal-1" type="checkbox" value="1" onchange',
    '    div class="monthly-allocation"',
    '      label',
    '        "B"',
    '        input name="dagilim-kanal-2" type="checkbox" value="2" onchange',
    '  p class="help"',
    '    "Yalnız genel kasa seçeneği hiçbir kanal kasasına yazılmaz. Eşit dağılımda seçtiğiniz kanallar sabittir; sonradan açılan kanallar bu plana eklenmez."',
  ],
  'monthly-new-ozel': [
    'fieldset',
    '  legend',
    '    "Kasa dağılımı"',
    '  label',
    '    "Dağılım"',
    '    select name="dagilimTuru" value="Ozel" required onchange',
    '      option value=""',
    '        "Dağılım seçin"',
    '      option value="Genel"',
    '        "Yalnız genel kasa"',
    '      option value="Esit"',
    '        "Seçilen kanallara eşit"',
    '      option value="Ozel"',
    '        "Kanal tutarlarını gir"',
    '  div class="stack"',
    '    div class="monthly-allocation"',
    '      label',
    '        "Ortak"',
    '        input name="dagilim-kanal-3" type="checkbox" value="3" checked onchange',
    '      input aria-label="Ortak payı (₺)" inputmode="decimal" name="dagilim-tutar-3" value="" required oninput',
    '    div class="monthly-allocation"',
    '      label',
    '        "A"',
    '        input name="dagilim-kanal-1" type="checkbox" value="1" checked onchange',
    '      input aria-label="A payı (₺)" inputmode="decimal" name="dagilim-tutar-1" value="" required oninput',
    '    div class="monthly-allocation"',
    '      label',
    '        "B"',
    '        input name="dagilim-kanal-2" type="checkbox" value="2" onchange',
    '      input aria-label="B payı (₺)" inputmode="decimal" name="dagilim-tutar-2" value="" disabled oninput',
    '  p class="help"',
    '    "Yalnız genel kasa seçeneği hiçbir kanal kasasına yazılmaz. Eşit dağılımda seçtiğiniz kanallar sabittir; sonradan açılan kanallar bu plana eklenmez."',
  ],
  'monthly-new-esit': [
    'fieldset',
    '  legend',
    '    "Kasa dağılımı"',
    '  label',
    '    "Dağılım"',
    '    select name="dagilimTuru" value="Esit" required onchange',
    '      option value=""',
    '        "Dağılım seçin"',
    '      option value="Genel"',
    '        "Yalnız genel kasa"',
    '      option value="Esit"',
    '        "Seçilen kanallara eşit"',
    '      option value="Ozel"',
    '        "Kanal tutarlarını gir"',
    '  div class="stack"',
    '    div class="monthly-allocation"',
    '      label',
    '        "Ortak"',
    '        input name="dagilim-kanal-3" type="checkbox" value="3" onchange',
    '    div class="monthly-allocation"',
    '      label',
    '        "A"',
    '        input name="dagilim-kanal-1" type="checkbox" value="1" checked onchange',
    '    div class="monthly-allocation"',
    '      label',
    '        "B"',
    '        input name="dagilim-kanal-2" type="checkbox" value="2" onchange',
    '  p class="help"',
    '    "Yalnız genel kasa seçeneği hiçbir kanal kasasına yazılmaz. Eşit dağılımda seçtiğiniz kanallar sabittir; sonradan açılan kanallar bu plana eklenmez."',
  ],
  'monthly-new-genel-kept': [
    'fieldset',
    '  legend',
    '    "Kasa dağılımı"',
    '  label',
    '    "Dağılım"',
    '    select name="dagilimTuru" value="Genel" required onchange',
    '      option value=""',
    '        "Dağılım seçin"',
    '      option value="Genel"',
    '        "Yalnız genel kasa"',
    '      option value="Esit"',
    '        "Seçilen kanallara eşit"',
    '      option value="Ozel"',
    '        "Kanal tutarlarını gir"',
    '  div class="stack" hidden',
    '    div class="monthly-allocation"',
    '      label',
    '        "Ortak"',
    '        input name="dagilim-kanal-3" type="checkbox" value="3" onchange',
    '    div class="monthly-allocation"',
    '      label',
    '        "A"',
    '        input name="dagilim-kanal-1" type="checkbox" value="1" onchange',
    '    div class="monthly-allocation"',
    '      label',
    '        "B"',
    '        input name="dagilim-kanal-2" type="checkbox" value="2" checked onchange',
    '  p class="help"',
    '    "Yalnız genel kasa seçeneği hiçbir kanal kasasına yazılmaz. Eşit dağılımda seçtiğiniz kanallar sabittir; sonradan açılan kanallar bu plana eklenmez."',
  ],
  'monthly-edit-ozel': [
    'fieldset',
    '  legend',
    '    "Kasa dağılımı"',
    '  label',
    '    "Dağılım"',
    '    select name="dagilimTuru" value="Ozel" required onchange',
    '      option value=""',
    '        "Dağılım seçin"',
    '      option value="Genel"',
    '        "Yalnız genel kasa"',
    '      option value="Esit"',
    '        "Seçilen kanallara eşit"',
    '      option value="Ozel"',
    '        "Kanal tutarlarını gir"',
    '  div class="stack"',
    '    div class="monthly-allocation"',
    '      label',
    '        "Ortak"',
    '        input name="dagilim-kanal-3" type="checkbox" value="3" checked onchange',
    '      input aria-label="Ortak payı (₺)" inputmode="decimal" name="dagilim-tutar-3" value="89.5" required oninput',
    '    div class="monthly-allocation"',
    '      label',
    '        "A"',
    '        input name="dagilim-kanal-1" type="checkbox" value="1" onchange',
    '      input aria-label="A payı (₺)" inputmode="decimal" name="dagilim-tutar-1" value="" disabled oninput',
    '    div class="monthly-allocation"',
    '      label',
    '        "B"',
    '        input name="dagilim-kanal-2" type="checkbox" value="2" onchange',
    '      input aria-label="B payı (₺)" inputmode="decimal" name="dagilim-tutar-2" value="" disabled oninput',
    '    div class="monthly-allocation"',
    '      label',
    '        "Kapalı"',
    '        input name="dagilim-kanal-4" type="checkbox" value="4" checked onchange',
    '      input aria-label="Kapalı payı (₺)" inputmode="decimal" name="dagilim-tutar-4" value="10.5" required oninput',
    '  p class="help"',
    '    "Yalnız genel kasa seçeneği hiçbir kanal kasasına yazılmaz. Eşit dağılımda seçtiğiniz kanallar sabittir; sonradan açılan kanallar bu plana eklenmez."',
  ],
  'import-unselected': [
    'fieldset',
    '  legend',
    '    "Kanal dağılımı"',
    '  label',
    '    "Hangi kasa / kanallar?"',
    '    select name="dagilimTuru" value="" onchange',
    '      option value=""',
    '        "Dağılım seç"',
    '      option value="Genel"',
    '        "Yalnız genel kasa"',
    '      option value="Esit"',
    '        "Seçilen kanallara eşit"',
    '      option value="Ozel"',
    '        "Kanal tutarlarını gir"',
    '  div hidden',
  ],
  'import-gider': [
    'fieldset',
    '  legend',
    '    "Kanal dağılımı"',
    '  label',
    '    "Hangi kasa / kanallar?"',
    '    select name="dagilimTuru" value="" onchange',
    '      option value=""',
    '        "Dağılım seç"',
    '      option value="Genel"',
    '        "Yalnız genel kasa"',
    '      option value="Esit"',
    '        "Seçilen kanallara eşit"',
    '      option value="Ozel"',
    '        "Kanal tutarlarını gir"',
    '  div hidden',
  ],
  'import-ozel': [
    'fieldset',
    '  legend',
    '    "Kanal dağılımı"',
    '  label',
    '    "Hangi kasa / kanallar?"',
    '    select name="dagilimTuru" value="Ozel" onchange',
    '      option value=""',
    '        "Dağılım seç"',
    '      option value="Genel"',
    '        "Yalnız genel kasa"',
    '      option value="Esit"',
    '        "Seçilen kanallara eşit"',
    '      option value="Ozel"',
    '        "Kanal tutarlarını gir"',
    '  div',
    '    div class="monthly-allocation"',
    '      label',
    '        "Ortak"',
    '        input name="pay-kanal-3" type="checkbox" value="3" checked onchange',
    '      input aria-label="Ortak payı (₺)" inputmode="decimal" name="pay-tutar-3" value="" oninput',
    '    div class="monthly-allocation"',
    '      label',
    '        "A"',
    '        input name="pay-kanal-1" type="checkbox" value="1" checked onchange',
    '      input aria-label="A payı (₺)" inputmode="decimal" name="pay-tutar-1" value="" oninput',
    '    div class="monthly-allocation"',
    '      label',
    '        "B"',
    '        input name="pay-kanal-2" type="checkbox" value="2" checked onchange',
    '      input aria-label="B payı (₺)" inputmode="decimal" name="pay-tutar-2" value="" oninput',
  ],
  'import-genel': [
    'fieldset',
    '  legend',
    '    "Kanal dağılımı"',
    '  label',
    '    "Hangi kasa / kanallar?"',
    '    select name="dagilimTuru" value="Genel" onchange',
    '      option value=""',
    '        "Dağılım seç"',
    '      option value="Genel"',
    '        "Yalnız genel kasa"',
    '      option value="Esit"',
    '        "Seçilen kanallara eşit"',
    '      option value="Ozel"',
    '        "Kanal tutarlarını gir"',
    '  div hidden',
  ],
  'import-otomatik': [
    'fieldset',
    '  legend',
    '    "Kanal dağılımı"',
    '  label',
    '    "Hangi kasa / kanallar?"',
    '    select name="dagilimTuru" value="Otomatik" disabled onchange',
    '      option value="Otomatik"',
    '        "İlgili kart hareketlerinden otomatik"',
    '  div hidden',
  ],
  'import-kart': [
    'label',
    '  "Hangi kasa / kanallar?"',
    '  select name="dagilimTuru" value="" onchange',
    '    option value=""',
    '      "Dağılım seç"',
    '    option value="Esit"',
    '      "Seçilen kanallara eşit"',
    '    option value="Ozel"',
    '      "Kanal tutarlarını gir"',
    'label',
    '  "Hangi kasa / kanallar?"',
    '  select name="dagilimTuru" value="Otomatik" disabled onchange',
    '    option value="Otomatik"',
    '      "İlgili kart hareketlerinden otomatik"',
  ],
  'card-empty': [
    'fieldset',
    '  legend',
    '    "Kanal dağılımı"',
    '  div',
    '  button class="button small" type="button" onclick',
    '    "+ Kanal payı"',
    '  p class="help"',
    '    "Kanal bilinmiyorsa boş bırakın. Bu tutar “Dağılım bekliyor” olarak izlenir. Pay girerseniz toplamı tutarın tamamına eşit olmalı."',
  ],
  'card-two-rows': [
    'fieldset',
    '  legend',
    '    "Kanal dağılımı"',
    '  div',
    '    div class="allocation-row"',
    '      label',
    '        "Kanal"',
    '        select name="pay-kanal-0" value="" required onchange',
    '          option value=""',
    '            "Kanal seçin"',
    '          option value="3"',
    '            "Ortak"',
    '          option value="1"',
    '            "A"',
    '          option value="2"',
    '            "B"',
    '      label',
    '        "Pay (₺)"',
    '        input inputmode="decimal" name="pay-tutar-0" value="" required oninput',
    '      button class="button icon-button" aria-label="1. kanal payını kaldır" type="button" onclick',
    '        "×"',
    '    div class="allocation-row"',
    '      label',
    '        "Kanal"',
    '        select name="pay-kanal-1" value="" required onchange',
    '          option value=""',
    '            "Kanal seçin"',
    '          option value="3"',
    '            "Ortak"',
    '          option value="1"',
    '            "A"',
    '          option value="2"',
    '            "B"',
    '      label',
    '        "Pay (₺)"',
    '        input inputmode="decimal" name="pay-tutar-1" value="" required oninput',
    '      button class="button icon-button" aria-label="2. kanal payını kaldır" type="button" onclick',
    '        "×"',
    '  button class="button small" type="button" onclick',
    '    "+ Kanal payı"',
    '  p class="help"',
    '    "Kanal bilinmiyorsa boş bırakın. Bu tutar “Dağılım bekliyor” olarak izlenir. Pay girerseniz toplamı tutarın tamamına eşit olmalı."',
  ],
  purchase: [
    'div class="allocation-editor"',
    '  div class="allocation-editor-head"',
    '    span',
    '      "Hangi kanala alındı?"',
    '    button class="button small" type="button" onclick',
    '      "+ Kanal payı"',
    '  div',
    '    div class="allocation-row"',
    '      label',
    '        "Kanal"',
    '        select name="kanal-0-0" value="" required onchange',
    '          option value=""',
    '            "Kanal seçin"',
    '          option value="3"',
    '            "Ortak"',
    '          option value="1"',
    '            "A"',
    '          option value="2"',
    '            "B"',
    '      label',
    '        "Pay (₺)"',
    '        input inputmode="decimal" name="pay-0-0" value="100.01" required oninput',
    '      button class="icon-button" aria-label="1. kalemin 1. kanal payını kaldır" type="button" onclick',
    '        "×"',
    '  div class="allocation-summary"',
    '    span class="money"',
    '      "Kalem: ₺100,01"',
    '    span',
    '      "₺40,01 dağıtılmadı"',
  ],
};
// domLines: sahte DOM düğümünün tam yapısı satır satır — etiket, sınıf, öznitelikler (ada göre sıralı), değer, durum (gizli/kapalı/
// zorunlu: özellik atanmışsa o, yoksa öznitelik; işaretli), dinlenen olaylar (on…) ve çocuklar (metinler JSON dizesi).
const DOM_STATES = ['hidden', 'disabled', 'required'];
function domLines(node, depth = 0, lines = []) {
  const pad = '  '.repeat(depth);
  if (node == null || typeof node !== 'object') {
    lines.push(pad + JSON.stringify(String(node)));
    return lines;
  }
  const parts = [node.tag];
  if (node.className) parts.push(`class=${JSON.stringify(node.className)}`);
  for (const name of Object.keys(node.attributes).sort())
    if (!DOM_STATES.includes(name)) parts.push(`${name}=${JSON.stringify(node.attributes[name])}`);
  if (node.currentValue !== undefined) parts.push(`value=${JSON.stringify(node.currentValue)}`);
  for (const name of DOM_STATES) if (Object.hasOwn(node, name) ? Boolean(node[name]) : name in node.attributes) parts.push(name);
  if (node.checked) parts.push('checked');
  for (const name of Object.keys(node.listeners).sort()) parts.push(`on${name}`);
  lines.push(pad + parts.join(' '));
  for (const child of node.children) domLines(child, depth + 1, lines);
  return lines;
}
const same = (actual, key, message) => assert.deepEqual(actual, KAPI_DOM[key], message);
const deferred = () => {
  let resolve;
  const promise = new Promise(done => {
    resolve = done;
  });
  return { promise, resolve };
};
const buttonIn = (node, label) => node.find(item => item.tag === 'button' && item.textContent === label);
const classNodes = (node, name) => nodesWhere(node, item => (item.className || '').split(' ').includes(name));
const kapiNotification = {
  id: 3,
  baslik: 'Kart ödemesi yaklaşıyor',
  mesaj: 'Üç gün sonra son ödeme.',
  tarih: '2026-09-23',
  okundu: false,
  hedef: '/#cards/4',
  tur: 'SonOdeme',
  kaynakId: 4,
};

test('Aşama 3 kapısı: act() düğmesi çalışırken kapalıdır, ikinci basışı yok sayar, hatayı bildirimde gösterip yeniden açılır', async () => {
  const cases = [
    { screen: 'monthly-expenses', host: '#page-actions', label: '+ Şablon ekle', path: '/api/kanallar', extra: monthlyResponses() },
    { screen: 'cards', host: '#page-actions', label: '+ Kart ekle', path: '/api/kanallar', extra: { '/api/takip/kartlar': [sampleCard] } },
    { screen: 'imports', host: '#page-actions', label: '+ PDF yükle', path: '/api/takip/kartlar', extra: importResponses() },
    {
      screen: 'notifications',
      host: '#view',
      label: 'Okundu olarak işaretle',
      path: '/api/bildirimler/3/okundu',
      extra: { '/api/bildirimler': [kapiNotification] },
    },
  ];
  for (const item of cases) {
    const { app, nodes, calls, responses } = await openApp(false, item.extra);
    await app.navigate(item.screen);
    await settle();
    const control = buttonIn(nodes.get(item.host), item.label);
    assert.ok(control, item.label);
    same(domLines(control), `act-${item.screen}`, item.label);
    const gate = deferred();
    responses[item.path] = () => gate.promise;
    const count = () => calls.filter(call => call.path === item.path).length;
    const before = count();
    control.listeners.click({ currentTarget: control });
    await settle();
    assert.equal(control.disabled, true, `${item.label}: iş sürerken düğme kapalı`);
    control.listeners.click({ currentTarget: control });
    await settle();
    assert.equal(count(), before + 1, `${item.label}: ikinci basış yok sayılır`);
    gate.resolve({ $status: 409, hata: `${item.label} tamamlanamadı.` });
    await settle();
    assert.equal(control.disabled, false, `${item.label}: iş bitince düğme açık`);
    assert.equal(nodes.get('#alerts').textContent, `${item.label} tamamlanamadı.×`, `${item.label}: hata bildirimi`);
    assert.equal(nodes.get('#modal').open, false, `${item.label}: pencere açılmaz`);
  }
});

test('Aşama 3 kapısı: act() işi bitince ekran yeniden yüklenir; aylık gider kaydı yalnız o ekran açıkken listeyi yeniler', async () => {
  // Bildirim: okundu işareti listeyi yeniden okur.
  const notice = await openApp(false, { '/api/bildirimler': [kapiNotification], '/api/bildirimler/3/okundu': null });
  await notice.app.navigate('notifications');
  await settle();
  const mark = buttonIn(notice.nodes.get('#view'), 'Okundu olarak işaretle');
  await mark.listeners.click({ currentTarget: mark });
  await settle();
  assert.deepEqual(
    notice.calls.map(call => `${call.method} ${call.path}`).filter(text => text.includes('/api/bildirimler')),
    ['GET /api/bildirimler', 'POST /api/bildirimler/3/okundu', 'GET /api/bildirimler']
  );
  assert.equal(mark.disabled, false);
  // Ekstre belgesi: "Belgeyi yenile" belgeyi yeniden okur.
  const imports = await openApp(false, importResponses());
  await imports.app.navigate('imports', 12);
  await settle();
  const reload = buttonIn(imports.nodes.get('#page-actions'), 'Belgeyi yenile');
  await reload.listeners.click({ currentTarget: reload });
  await settle();
  assert.equal(imports.calls.filter(call => call.path === '/api/ekstre-aktar/12').length, 2);
  // Aylık gider: "Ayı göster" seçilen ayı okur; şablon kaydı ekran açıkken listeyi yeniler, başka ekrana geçildiyse yenilemez.
  const nextMonth = ui.shiftMonth(monthNow, 1);
  const [nextYear, nextNumber] = nextMonth.split('-').map(Number);
  const nextPath = `/api/aylik-giderler?yil=${nextYear}&ay=${nextNumber}`;
  const monthly = await openApp(false, {
    ...monthlyResponses(),
    [nextPath]: { yil: nextYear, ay: nextNumber, planlananToplam: 0, odenenToplam: 0, kayitlar: [] },
    '/api/aylik-giderler/sablonlar': [monthlyTemplate],
  });
  await monthly.app.navigate('monthly-expenses');
  await settle();
  const view = monthly.nodes.get('#view');
  buttonIn(view, '›').listeners.click({ currentTarget: buttonIn(view, '›') });
  await clickView(monthly.nodes, 'Ayı göster');
  assert.ok(monthly.calls.some(call => call.path === nextPath));
  const reads = () => monthly.calls.filter(call => call.method === 'GET' && call.path === '/api/aylik-giderler/sablonlar').length;
  const saveTemplate = async () => {
    await monthly.app.monthlyUi.templateDialog(monthlyTemplate);
    await submitDialog(monthly.nodes);
  };
  monthly.responses['/api/aylik-giderler/sablonlar/7'] = monthlyTemplate;
  const beforeSave = reads();
  await saveTemplate();
  assert.equal(reads(), beforeSave + 1, 'ekran açıkken şablon kaydı listeyi yeniler');
  assert.ok(monthly.calls.filter(call => call.path === nextPath).length >= 2, 'yenileme seçili ayı okur');
  await monthly.app.navigate('home');
  await settle();
  const afterLeave = reads();
  await saveTemplate();
  assert.equal(reads(), afterLeave, 'başka ekrana geçildiyse aylık gider listesi yenilenmez');
});

test('Aşama 3 kapısı: bildirim ayarlarındaki act() düğmesi ek özellikleri (disabled) aynen taşır', async () => {
  const browser = fakePushBrowser();
  const { app, nodes } = await openApp(
    false,
    {
      '/api/bildirimler/ayarlar': { etkin: true, saat: 9, dakika: 0, surum: 1 },
      '/api/bildirimler/push/anahtar': { etkin: false, publicKey: null },
      '/api/bildirimler/push/abonelikler': [],
    },
    browser.environment
  );
  await app.notificationUi.settings();
  same(domLines(buttonIn(nodes.get('#modal-content'), 'Bu cihazda bildirimleri aç')), 'act-props');
});

test('Aşama 3 kapısı: editör koruması ekrana göre aynı iletiyle işlemi başlatmaz', async () => {
  const MONTHLY = 'Bu işlem için editör hesabı gerekir.';
  const IMPORT = 'Ekstre yüklemek ve işlemek için editör hesabı gerekir.';
  const { app, calls } = await openApp(false, { ...monthlyResponses(), '/api/auth/me': { rol: 'viewer' } });
  // Hata türü adla denetlenir (kurucu kimliğine değil): ileti ve tür birlikte sınanır.
  const exact = message => error => error?.name === 'Error' && error.message === message;
  await assert.rejects(app.monthlyUi.templateDialog(), exact(MONTHLY));
  assert.throws(() => app.monthlyUi.paymentDialog(monthlyRow, yearNow, monthNumberNow), exact(MONTHLY));
  assert.throws(() => app.monthlyUi.cancelDialog({ ...monthlyRow, durum: 'Odendi', odemeId: 4 }), exact(MONTHLY));
  const threshold = { kanalId: 1, kanal: 'MEZAT', surum: 1, tutar: 10, etkin: true };
  assert.throws(() => app.cashControlsUi.thresholdDialog(threshold), exact(MONTHLY));
  assert.throws(() => app.cashControlsUi.comparisonDialog(), exact(MONTHLY));
  assert.throws(() => app.cashControlsUi.explainDialog({ id: 2, surum: 1, fark: 5 }), exact(MONTHLY));
  await assert.rejects(app.cashControlsUi.sinceDialog({ id: 2 }), exact(MONTHLY));
  await assert.rejects(app.statementImportUi.render(0), exact(IMPORT));
  await assert.rejects(app.statementImportUi.uploadDialog(0), exact(IMPORT));
  assert.throws(() => app.financeUi.feeDialog(sampleCard), exact('Yeni kart takibinde editör hesabı gerekir.'));
  assert.deepEqual(
    calls.filter(call => call.method !== 'GET' || /kanallar|takip|kasa-kontrol\/|ekstre-aktar|aylik-giderler/.test(call.path)),
    [],
    'korunan işlem istek göndermez'
  );
  const editor = await openApp(false, {});
  assert.throws(
    () => editor.app.financeUi.feeDialog({ ...sampleCard, yeniTakip: false }),
    exact('Yeni kart takibinde editör hesabı gerekir.')
  );
});

test('Aşama 3 kapısı: pencere açıkken oturum kapanırsa kayıt editör korumasıyla durur ve ileti pencere adıyla bildirilir', async () => {
  const cases = [
    {
      title: 'Aylık gider şablonu ekle',
      message: 'Bu işlem için editör hesabı gerekir.',
      extra: monthlyResponses(),
      open: app => app.monthlyUi.templateDialog(),
    },
    {
      title: 'Kanal alt bakiye uyarısı',
      message: 'Bu işlem için editör hesabı gerekir.',
      extra: {},
      open: app => app.cashControlsUi.thresholdDialog({ kanalId: 1, kanal: 'Kanal', surum: 1, tutar: 10, etkin: true }),
    },
    {
      title: 'Ekstre / hareket PDF’si yükle',
      message: 'Ekstre yüklemek ve işlemek için editör hesabı gerekir.',
      extra: importResponses(),
      open: async (app, nodes) => {
        await app.navigate('imports');
        await settle();
        const upload = buttonIn(nodes.get('#page-actions'), '+ PDF yükle');
        await upload.listeners.click({ currentTarget: upload });
      },
    },
    {
      title: 'Kart faizini / masrafını ekle',
      message: 'Editör hesabı gerekir.',
      extra: {},
      open: app => app.financeUi.feeDialog(sampleCard),
    },
  ];
  for (const item of cases) {
    const { app, nodes, calls } = await openApp(false, item.extra);
    await item.open(app, nodes);
    await settle();
    const form = nodes.get('#modal-content').find(node => node.tag === 'form');
    assert.ok(form, item.title);
    const writes = calls.length;
    app.clearSession();
    form.listeners.submit({ preventDefault() {} });
    await settle();
    assert.equal(nodes.get('#alerts').textContent, `${item.title}: ${item.message}×`, item.title);
    assert.equal(calls.length, writes, `${item.title}: istek gönderilmez`);
  }
  // Masraf onayı (ikinci pencere) da aynı korumayla durur.
  const { app, nodes, calls } = await openApp(false, {
    '/api/takip/kartlar/4/masraf-onizleme': { tutar: 12.5, devredenBorc: 12000, dagilimOzeti: 'ozet', dagilimlar: [] },
  });
  app.financeUi.feeDialog({ ...sampleCard, ekstreler: [{ ...sampleCard.ekstreler[0], kesimTarihi: '2020-01-20' }] });
  formField(nodes, 'ekstreId').value = '8';
  formField(nodes, 'tutar').value = '12,50';
  formField(nodes, 'aciklama').value = 'Faiz';
  await submitDialog(nodes);
  const confirmation = nodes.get('#modal-content').find(node => node.tag === 'form');
  assert.match(nodes.get('#modal-title').textContent, /Faiz \/ masraf dağılımını onaylayın/);
  const writes = calls.length;
  app.clearSession();
  confirmation.listeners.submit({ preventDefault() {} });
  await settle();
  assert.equal(nodes.get('#alerts').textContent, 'Faiz / masraf dağılımını onaylayın: Editör hesabı gerekir.×');
  assert.equal(calls.length, writes);
});

test('Aşama 3 kapısı: kanal payı etiketleri (kart, kredi, aylık gider, ekstre, alış ödemesi) bugünkü yapıyla çizilir', async () => {
  // Kart: kanalı olmayan pay "Dağılım bekliyor" adıyla ve pending sınıfıyla; boş liste boş kutu; eksi ve kuruşlu tutar.
  const card = {
    ...sampleCard,
    kanalKartBorclari: [
      { kanalId: 1, kanal: 'MEZAT', tutar: 1234.5 },
      { kanalId: null, kanal: null, tutar: 30 },
      { kanalId: 3, kanal: 'Ortak', tutar: 0.07 },
    ],
    harcamalar: [
      {
        id: 1,
        tarih: '2026-09-23',
        aciklama: 'İade',
        tutar: -50.25,
        taksitSayisi: 1,
        iptal: false,
        dagilimlar: [{ kanalId: 1, kanal: 'MEZAT', tutar: -50.25 }],
      },
      { id: 2, tarih: '2026-09-23', aciklama: 'Belirsiz', tutar: 10, taksitSayisi: 1, iptal: false, dagilimlar: [] },
    ],
  };
  const finance = await openApp(false, { '/api/takip/kartlar/4': card });
  await finance.app.navigate('cards', 4);
  await settle();
  same(
    classNodes(finance.nodes.get('#view'), 'allocation-tags').flatMap(node => domLines(node)),
    'shares-card'
  );
  const loans = await openApp(false, {
    '/api/takip/krediler/5': {
      ...sampleLoan,
      kanalPaylari: [
        { kanalId: 1, kanal: 'A', tutar: 60000.01 },
        { kanalId: null, kanal: 'Dağılım bekliyor', tutar: 59999.99 },
      ],
    },
  });
  await loans.app.navigate('loans', 5);
  await settle();
  same(
    classNodes(loans.nodes.get('#view'), 'allocation-tags').flatMap(node => domLines(node)),
    'shares-loan'
  );
  // Aylık gider: "Yalnız genel kasa" ayrı yazı; kanallı satırda etiketler; ödeme penceresinde aynı etiketler.
  const channelRow = {
    ...monthlyRow,
    sablonId: 8,
    ad: 'Maaş',
    dagilimTuru: 'Ozel',
    dagilimlar: [
      { kanalId: 2, kanal: 'B', tutar: 60.01 },
      { kanalId: 3, kanal: 'Ortak', tutar: 40 },
    ],
  };
  const monthly = await openApp(false, monthlyResponses([monthlyRow, channelRow]));
  await monthly.app.navigate('monthly-expenses');
  await settle();
  const monthlyCells = nodesWhere(
    monthly.nodes.get('#view'),
    node => node.tag === 'td' && (node.textContent === 'Yalnız genel kasa' || classNodes(node, 'allocation-tags').length > 0)
  );
  same(
    monthlyCells.flatMap(node => domLines(node)),
    'shares-monthly'
  );
  monthly.app.monthlyUi.paymentDialog(channelRow, yearNow, monthNumberNow);
  same(
    classNodes(monthly.nodes.get('#modal-content'), 'allocation-tags').flatMap(node => domLines(node)),
    'shares-monthly-payment'
  );
  // Ekstre: kanalı olmayan pay "Genel kasa" adıyla, pending sınıfı yok; kayıt geçmişinde Genel ve eşleştirme yazıyla.
  const imports = await openApp(
    false,
    importResponses(
      importDocument({
        kayitlar: [
          { id: 1, satirNo: 5, tarih: '2026-09-20', aciklama: 'Genel', tutar: 5, islemTuru: 'Gider', dagilimTuru: 'Genel', dagilimlar: [] },
          {
            id: 2,
            satirNo: 6,
            tarih: '2026-09-20',
            aciklama: 'Kanallı',
            tutar: 7.5,
            islemTuru: 'Gider',
            dagilimTuru: 'Ozel',
            dagilimlar: [
              { kanalId: 1, kanal: 'Mezat', tutar: 7.49 },
              { kanalId: null, kanal: null, tutar: 0.01 },
            ],
          },
          {
            id: 3,
            satirNo: 7,
            tarih: '2026-09-20',
            aciklama: 'Eş',
            tutar: 9,
            islemTuru: 'Eslestir',
            dagilimTuru: 'Eslesme',
            dagilimlar: [],
          },
        ],
      }),
      {
        '/api/ekstre-aktar/12/onizleme': importPreview({
          satirlar: [
            {
              satirNo: 1,
              tarih: '2026-09-23',
              aciklama: 'Kira',
              tutar: 100,
              islemTuru: 'Gider',
              kasaEtkisi: -100,
              dagilimlar: [
                { kanalId: null, kanal: null, tutar: -100 },
                { kanalId: 2, kanal: 'Mağaza', tutar: 0 },
              ],
              uyarilar: [],
            },
          ],
        }),
      }
    )
  );
  await imports.app.navigate('imports', 12);
  await settle();
  const history = imports.nodes.get('#view').find(node => node.attributes['aria-label'] === 'Bu belgeden kaydedilenler');
  same(
    nodesWhere(history, node => node.tag === 'tr')
      .slice(1)
      .map(row => domLines(row.children[3])),
    'shares-import-history'
  );
  await chooseImportRow(imports.nodes);
  await clickView(imports.nodes, 'Seçilenleri önizle');
  const preview = imports.nodes.get('#view').find(node => (node.className || '') === 'import-preview');
  same(
    classNodes(preview, 'allocation-tags').flatMap(node => domLines(node)),
    'shares-import-preview'
  );
  // Alış ödemesi: yalnız sıfırdan büyük paylar, kanal adı aynen; dağılım bekleyen ödemede etiket yerine rozet.
  const purchase = await openApp(false, { '/api/alis/kanallar': [] });
  const payment = (overrides = {}) => ({
    id: 8,
    tarih: '2026-09-23',
    tutar: 50,
    krediKartiId: null,
    dagilimBekliyor: false,
    dagilimlar: [
      { kanalId: 1, kanal: 'A', tutar: 49.99 },
      { kanalId: 2, kanal: 'B', tutar: 0 },
      { kanalId: 3, kanal: 'C', tutar: 0.01 },
    ],
    ...overrides,
  });
  same(
    [payment(), payment({ dagilimBekliyor: true })].flatMap(value =>
      domLines(purchase.app.paymentRow({ id: 1 }, value).children[0].children[2])
    ),
    'shares-purchase-payment'
  );
});

const kapiChannels = [
  { id: 3, ad: 'Ortak', aktif: true },
  { id: 1, ad: 'A', aktif: true },
  { id: 2, ad: 'B', aktif: true },
  { id: 4, ad: 'Kapalı', aktif: false },
];
test('Aşama 3 kapısı: aylık gider dağıtım editörü (distribution) — yapı, seçim, sıralama, kuruş ve hata iletileri', async () => {
  const { app, nodes, calls, responses } = await openApp(false, { ...monthlyResponses(), '/api/kanallar': kapiChannels });
  const allocation = () => nodes.get('#modal-content').find(node => node.tag === 'fieldset');
  const mode = value => {
    const control = formField(nodes, 'dagilimTuru');
    control.value = value;
    control.listeners.change();
  };
  const check = (id, on = true) => {
    const control = formField(nodes, `dagilim-kanal-${id}`);
    control.checked = on;
    control.listeners.change({ target: control });
  };
  const amountOf = (id, value) => {
    const control = formField(nodes, `dagilim-tutar-${id}`);
    control.value = value;
    control.listeners.input({ target: control });
  };
  const posts = () => calls.filter(call => call.method === 'POST' && call.path === '/api/aylik-giderler/sablonlar');
  const errorText = () => nodes.get('#modal-content').find(node => node.className === 'form-error').textContent;
  await app.monthlyUi.templateDialog();
  same(domLines(allocation()), 'monthly-new');
  mode('Ozel');
  check(1);
  check(3);
  same(domLines(allocation()), 'monthly-new-ozel');
  formField(nodes, 'ad').value = 'Kira';
  formField(nodes, 'tutar').value = '100,01';
  amountOf(1, '50');
  amountOf(3, '50,02');
  await submitDialog(nodes);
  assert.equal(errorText(), 'Kanal paylarının toplamı gider tutarına eşit olmalı.');
  amountOf(3, '0');
  await submitDialog(nodes);
  assert.equal(errorText(), 'Tutar geçerli aralıkta ve sıfırdan büyük olmalı.');
  amountOf(3, '-50,01');
  await submitDialog(nodes);
  assert.equal(errorText(), 'Tutarı kuruş cinsinden, en çok iki ondalık basamakla girin.');
  amountOf(3, '50,001');
  await submitDialog(nodes);
  assert.equal(errorText(), 'Tutarı kuruş cinsinden, en çok iki ondalık basamakla girin.');
  amountOf(3, '50,01');
  await submitDialog(nodes);
  assert.equal(posts().length, 1);
  assert.deepEqual(
    [posts()[0].body.dagilimTuru, posts()[0].body.dagilimlar],
    [
      'Ozel',
      [
        { kanalId: 1, tutar: 50 },
        { kanalId: 3, tutar: 50.01 },
      ],
    ]
  );
  // Eşit dağılım: tutarlar 0, yalnız seçilenler; seçim kaldırılınca kanal çıkar; hiç kanal yoksa ileti.
  await app.monthlyUi.templateDialog();
  formField(nodes, 'ad').value = 'Maaş';
  formField(nodes, 'tutar').value = '10';
  mode('Esit');
  await submitDialog(nodes);
  assert.equal(errorText(), 'En az bir kanal seçin.');
  check(2);
  check(1);
  check(2, false);
  same(domLines(allocation()), 'monthly-new-esit');
  await submitDialog(nodes);
  assert.deepEqual(posts()[1].body.dagilimlar, [{ kanalId: 1, tutar: 0 }]);
  // Seçim ve tutar kip değişince korunur (liste gizliyken de çizili kalır).
  await app.monthlyUi.templateDialog();
  mode('Ozel');
  check(2);
  amountOf(2, '7,5');
  mode('Genel');
  same(domLines(allocation()), 'monthly-new-genel-kept');
  mode('Ozel');
  assert.equal(formField(nodes, 'dagilim-tutar-2').value, '7,5');
  assert.equal(formField(nodes, 'dagilim-kanal-2').checked, true);
  // Kayıtlı şablon: kapalı ama seçili kanal görünür, tutarlar dolu gelir, istek kanal numarasına göre sıralanır.
  responses['/api/aylik-giderler/sablonlar/9'] = monthlyTemplate;
  const saved = {
    ...monthlyTemplate,
    id: 9,
    tutar: 100,
    dagilimTuru: 'Ozel',
    dagilimlar: [
      { kanalId: 4, kanal: 'Kapalı', tutar: 10.5 },
      { kanalId: 3, kanal: 'Ortak', tutar: 89.5 },
    ],
  };
  await app.monthlyUi.templateDialog(saved);
  same(domLines(allocation()), 'monthly-edit-ozel');
  await submitDialog(nodes);
  const put = calls.find(call => call.method === 'PUT' && call.path === '/api/aylik-giderler/sablonlar/9');
  assert.deepEqual(put.body.dagilimlar, [
    { kanalId: 3, tutar: 89.5 },
    { kanalId: 4, tutar: 10.5 },
  ]);
  // Eşit şablonun sıfır tutarı Özel kipte "0" olarak görünür.
  await app.monthlyUi.templateDialog({
    ...saved,
    dagilimTuru: 'Esit',
    dagilimlar: [{ kanalId: 1, kanal: 'A', tutar: 0 }],
  });
  mode('Ozel');
  assert.equal(formField(nodes, 'dagilim-tutar-1').value, '0');
});

test('Aşama 3 kapısı: ekstre satırı dağıtım editörü — işlem türüne göre seçenekler, liste sırası, otomatik ve hata iletileri', async () => {
  const bank = importDocument({
    satirlar: [importRow(), importRow({ no: 2, onerilenIslem: 'Gelir', tutar: 20.01 })],
  });
  const { app, nodes, calls, responses } = await openApp(
    false,
    importResponses(bank, {
      '/api/kanallar': kapiChannels,
      '/api/ekstre-aktar/12/onizleme': importPreview(),
    })
  );
  await app.navigate('imports', 12);
  await settle();
  const fieldset = (no = 1) => importRowNode(nodes, no).find(node => node.tag === 'fieldset');
  const rowField = (no, name) => importRowNode(nodes, no).find(node => node.attributes.name === name);
  const mode = (no, value) => {
    const control = rowField(no, 'dagilimTuru');
    control.value = value;
    control.listeners.change();
  };
  const check = (no, id, on = true) => {
    const control = rowField(no, `pay-kanal-${id}`);
    control.checked = on;
    control.listeners.change();
  };
  const amountOf = (no, id, value) => {
    const control = rowField(no, `pay-tutar-${id}`);
    control.value = value;
    control.listeners.input();
  };
  const previews = () => calls.filter(call => call.path.endsWith('/onizleme'));
  const alerts = () => nodes.get('#alerts').textContent;
  // Seçilmeden önce: seçenek yok, liste gizli ve boş.
  same(domLines(fieldset()), 'import-unselected');
  await chooseImportRow(nodes, 1, null);
  same(domLines(fieldset()), 'import-gider');
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(alerts(), 'Seçili hareketin kasa / kanal dağılımını seçin.×');
  mode(1, 'Esit');
  await clickView(nodes, 'Seçilenleri önizle');
  assert.match(alerts(), /Seçili hareket için en az bir kanal seçin\.×$/);
  // Liste sırası (Sira) korunur: B (2) önce işaretlense de istek Ortak (3), A (1), B (2) sırasıyla gider; kapalı kanal yok.
  check(1, 2);
  check(1, 3);
  check(1, 1);
  mode(1, 'Ozel');
  same(domLines(fieldset()), 'import-ozel');
  amountOf(1, 2, '33,33');
  amountOf(1, 3, '33,33');
  amountOf(1, 1, '33,33');
  await clickView(nodes, 'Seçilenleri önizle');
  assert.match(alerts(), /Kanal paylarının toplamı hareket tutarına eşit olmalı\.×$/);
  amountOf(1, 1, '33,34');
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(previews().length, 1);
  assert.deepEqual(previews()[0].body.satirlar[0].dagilimlar, [
    { kanalId: 3, tutar: 33.33 },
    { kanalId: 1, tutar: 33.34 },
    { kanalId: 2, tutar: 33.33 },
  ]);
  // Dağılım değişikliği önizlemeyi geçersiz kılar (changed → invalidate).
  const host = () => nodes.get('#view').find(node => node.className === 'import-preview');
  assert.equal(host().hidden, false);
  amountOf(1, 1, '33,34');
  assert.equal(host().hidden, true, 'tutar girişi önizlemeyi kapatır');
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(host().hidden, false);
  check(1, 3, false);
  assert.equal(host().hidden, true, 'kanal seçimi önizlemeyi kapatır');
  mode(1, 'Esit');
  await clickView(nodes, 'Seçilenleri önizle');
  assert.equal(host().hidden, false);
  assert.deepEqual(previews().at(-1).body.satirlar[0].dagilimlar, [
    { kanalId: 1, tutar: 0 },
    { kanalId: 2, tutar: 0 },
  ]);
  mode(1, 'Ozel');
  assert.equal(host().hidden, true, 'kip değişimi önizlemeyi kapatır');
  // Kip Genel'e geçince liste boşaltılır; geri dönünce seçim ve tutarlar korunur.
  mode(1, 'Genel');
  same(domLines(fieldset()), 'import-genel');
  mode(1, 'Ozel');
  assert.equal(rowField(1, 'pay-kanal-3').checked, false);
  assert.equal(rowField(1, 'pay-kanal-1').checked, true);
  assert.equal(rowField(1, 'pay-tutar-1').value, '33,34');
  assert.equal(rowField(1, 'pay-tutar-3').disabled, true);
  // Kart ödemesi: otomatik, kapalı seçim; tür yeniden Gider olunca önceki seçenek (Özel) yoksa boşa, varsa korunur.
  const kind = rowField(1, 'tur-1');
  kind.value = 'KartOdemesi';
  await kind.listeners.change();
  await settle();
  same(domLines(fieldset()), 'import-otomatik');
  rowField(1, 'kart-1').value = '4';
  await clickView(nodes, 'Seçilenleri önizle');
  assert.deepEqual([previews().at(-1).body.satirlar[0].dagilimTuru, previews().at(-1).body.satirlar[0].dagilimlar], ['Otomatik', []]);
  kind.value = 'Gider';
  await kind.listeners.change();
  await settle();
  assert.equal(rowField(1, 'dagilimTuru').value, '');
  assert.equal(rowField(1, 'dagilimTuru').disabled, false);
  mode(1, 'Ozel');
  kind.value = 'Gelir';
  await kind.listeners.change();
  await settle();
  assert.equal(rowField(1, 'dagilimTuru').value, 'Ozel');
  // Genel kasa: dağılım boş gider; ikinci satırın editörü ayrıdır.
  mode(1, 'Genel');
  await chooseImportRow(nodes, 2, 'Genel');
  await clickView(nodes, 'Seçilenleri önizle');
  assert.deepEqual(
    previews()
      .at(-1)
      .body.satirlar.map(row => [row.satirNo, row.islemTuru, row.dagilimTuru, row.dagilimlar]),
    [
      [1, 'Gelir', 'Genel', []],
      [2, 'Gelir', 'Genel', []],
    ]
  );
  // Kart belgesi: harcama türünde Genel seçeneği yok, iadede otomatik.
  responses['/api/ekstre-aktar/12'] = importDocument({
    kaynak: 'Kart',
    kartId: 4,
    satirlar: [importRow({ onerilenIslem: 'KartHarcama' }), importRow({ no: 2, onerilenIslem: 'KartIade' })],
  });
  responses['/api/takip/kartlar/4'] = sampleCard;
  await app.navigate('imports', 12);
  await settle();
  await chooseImportRow(nodes, 1, null);
  await chooseImportRow(nodes, 2, null);
  same(
    [fieldset(1), fieldset(2)].flatMap(node => domLines(node.children[1])),
    'import-kart'
  );
});

test('Aşama 3 kapısı: kart dağılım editörü (satır ekle/kaldır) — boş bırakılabilir, kanal bir kez, toplam tutara eşit', async () => {
  const { app, nodes, calls, responses } = await openApp(false, { '/api/kanallar': kapiChannels });
  responses['/api/takip/kartlar'] = { id: 4 };
  responses['/api/takip/kartlar/4'] = sampleCard;
  responses['/api/takip/kartlar/4/devir'] = { $status: 404 };
  const allocation = () => nodes.get('#modal-content').find(node => node.tag === 'fieldset');
  const errorText = () => nodes.get('#modal-content').find(node => node.className === 'form-error').textContent;
  const fill = () => {
    formField(nodes, 'ad').value = 'Kart';
    formField(nodes, 'limit').value = '1000';
    formField(nodes, 'acilisBorc').value = '100,01';
  };
  await app.financeUi.cardDialog();
  same(domLines(allocation()), 'card-empty');
  fill();
  await clickDialog(nodes, '+ Kanal payı');
  await clickDialog(nodes, '+ Kanal payı');
  same(domLines(allocation()), 'card-two-rows');
  const choose = (index, id) => {
    const control = formField(nodes, `pay-kanal-${index}`);
    control.value = String(id);
    control.listeners.change({ target: control });
  };
  const share = (index, value) => {
    const control = formField(nodes, `pay-tutar-${index}`);
    control.value = value;
    control.listeners.input({ target: control });
  };
  choose(0, 1);
  choose(1, 1);
  share(0, '50');
  share(1, '50,01');
  await submitDialog(nodes);
  assert.equal(errorText(), 'Her kanalı bir kez seçin.');
  choose(1, 3);
  share(1, '50');
  await submitDialog(nodes);
  assert.equal(errorText(), 'Kanal paylarının toplamı tutara eşit olmalı.');
  share(1, '50,01');
  const posts = () => calls.filter(call => call.method === 'POST' && call.path === '/api/takip/kartlar');
  await submitDialog(nodes);
  assert.deepEqual(posts()[0].body.acilisDagilimlari, [
    { kanalId: 1, tutar: 50 },
    { kanalId: 3, tutar: 50.01 },
  ]);
  // Boş bırakılan dağılım (kanal bilinmiyor) gönderilir: [].
  await app.financeUi.cardDialog();
  fill();
  await clickDialog(nodes, '+ Kanal payı');
  const remove = nodes.get('#modal-content').find(node => node.attributes['aria-label'] === '1. kanal payını kaldır');
  remove.listeners.click();
  await submitDialog(nodes);
  assert.deepEqual(posts()[1].body.acilisDagilimlari, []);
});

test('Aşama 3 kapısı: alış satır editörü ayrı kalır — kısmi dağıtım, kalan tutar önerisi ve alıcı rolü', async () => {
  for (const role of ['editor', 'alici']) {
    const { app, nodes, calls } = await openApp(false, {
      '/api/auth/me': { rol: role },
      '/api/alis': [],
      '/api/alis/kanallar': kapiChannels,
    });
    await app.navigate('purchases');
    await settle();
    const create = buttonIn(nodes.get('#page-actions'), '+ Yeni alış');
    await create.listeners.click({ currentTarget: create });
    await settle();
    const status = () => nodes.get('#modal-content').find(node => (node.className || '').startsWith('allocation-summary'));
    const type = (name, value) => formField(nodes, name).listeners.input({ target: { value } });
    type('tedarikci', 'Firma');
    type('aciklama-0', 'Mal');
    type('tutar-0', '100,01');
    assert.equal(status().textContent, 'Kalem: ₺100,01₺100,01 dağıtılmadı', role);
    await clickDialog(nodes, '+ Kanal payı');
    assert.equal(formField(nodes, 'pay-0-0').value, '100.01', `${role}: kalan tutar önerilir`);
    type('pay-0-0', '60');
    const channel = formField(nodes, 'kanal-0-0');
    channel.listeners.change({ target: { value: '2' } });
    assert.equal(status().textContent, 'Kalem: ₺100,01₺40,01 dağıtılmadı', role);
    same(domLines(nodes.get('#modal-content').find(node => node.className === 'allocation-editor')), 'purchase', role);
    type('pay-0-0', '100,02');
    assert.equal(status().textContent, 'Kalem: ₺100,01₺0,01 fazla pay', role);
    await submitDialog(nodes);
    assert.equal(
      nodes.get('#modal-content').find(node => node.className === 'form-error').textContent,
      '1. kalemde kanal payları kalem tutarını aşıyor.',
      role
    );
    type('pay-0-0', '60');
    assert.equal(status().textContent, 'Kalem: ₺100,01₺40,01 dağıtılmadı', role);
    await submitDialog(nodes);
    const post = calls.find(call => call.method === 'POST' && call.path === '/api/alis');
    assert.deepEqual(post.body.kalemler, [{ aciklama: 'Mal', tutar: 100.01, dagilimlar: [{ kanalId: 2, tutar: 60 }] }], role);
  }
});

test('Aşama 3 kapısı: para hesaplarının bugünkü çıktısı (aylık kalan plan, kontrol farkları, kart borcu etkisi, ana sayfa, ayırma, devir)', async () => {
  // Aylık gider: kalan plan = planlanan − ödenen.
  const monthly = await openApp(false, {
    ...monthlyResponses(),
    [monthlyPath]: { yil: yearNow, ay: monthNumberNow, planlananToplam: 1234.56, odenenToplam: 234.5, kayitlar: [] },
  });
  await monthly.app.navigate('monthly-expenses');
  await settle();
  assert.match(monthly.nodes.get('#view').textContent, /Kalan plan₺1\.000,06/);
  // Kasa kontrolü: geriye dönük değişim ve kontrol gününden sonraki fark.
  const since = {
    kontrolId: 2,
    esasTarih: '2026-09-20',
    filigranVar: true,
    sistemBakiye: 1000.1,
    guncelSistemBakiye: 1500.35,
    bugunkuSistemBakiye: 500,
    kirpildi: false,
    degisiklikler: [],
    istekler: [],
    hareketler: [],
  };
  const cash = await openApp(false, { '/api/kasa-kontrol/2/sonrasi': since });
  await cash.app.cashControlsUi.sinceDialog({ id: 2 });
  const sinceText = cash.nodes.get('#modal-content').textContent;
  assert.ok(sinceText.includes('Geriye dönük değişim ₺500,25'), sinceText);
  assert.ok(sinceText.includes('Kontrol gününden sonra -₺1.000,35'), sinceText);
  // Ekstre önizlemesi: harcama − iade (kart ödemesi hariç).
  const imports = await openApp(
    false,
    importResponses(importDocument(), {
      '/api/ekstre-aktar/12/onizleme': importPreview({
        satirlar: [
          {
            satirNo: 1,
            tarih: '2026-09-23',
            aciklama: 'a',
            tutar: 100.25,
            islemTuru: 'KartHarcama',
            kasaEtkisi: 0,
            dagilimlar: [],
            uyarilar: [],
          },
          {
            satirNo: 2,
            tarih: '2026-09-23',
            aciklama: 'b',
            tutar: 20.1,
            islemTuru: 'KartIade',
            kasaEtkisi: 0,
            dagilimlar: [],
            uyarilar: [],
          },
          {
            satirNo: 3,
            tarih: '2026-09-23',
            aciklama: 'c',
            tutar: 50,
            islemTuru: 'KartOdemesi',
            kasaEtkisi: -50,
            dagilimlar: [],
            uyarilar: [],
          },
        ],
      }),
    })
  );
  await imports.app.navigate('imports', 12);
  await chooseImportRow(imports.nodes);
  await clickView(imports.nodes, 'Seçilenleri önizle');
  assert.match(imports.nodes.get('#view').textContent, /Harcama \/ iade borç etkisi₺80,15/);
  // Ana sayfa: aynı kanala düşen kart borcu satırları toplanır.
  const home = sampleHome();
  home.takipOzeti.kanalKartBorclari = [
    { kanalId: 1, kanal: 'MEZAT', tutar: 40.1 },
    { kanalId: 1, kanal: 'MEZAT', tutar: 9.9 },
    { kanalId: 2, kanal: 'PERAKENDE', tutar: 0.07 },
  ];
  const homeApp = await openApp(false, { [homeSummaryPath]: home });
  const balances = homeApp.nodes.get('#view').find(node => node.className === 'channel-balances');
  assert.match(balances.children[0].textContent, /Kalan kart borcu: ₺50,00$/);
  assert.match(balances.children[1].textContent, /Kalan kart borcu: ₺0,07$/);
  // Alış ödemesini ayırma: kanalın payları toplanıp alana yazılır.
  const cards = [{ id: 2, ad: 'Takipli', yeniTakip: true, aktif: true }];
  const purchase = { id: 6, surum: 3, tarih: '2026-09-23', tedarikci: 'Alış', durum: 'Taslak', kalemler: [], odemeler: [], toplam: 60 };
  const payment = {
    id: 8,
    tarih: '2026-09-23',
    tutar: 60,
    krediKartiId: 2,
    dagilimBekliyor: false,
    dagilimlar: [
      { kanalId: 1, kanal: 'A', tutar: 20 },
      { kanalId: 1, kanal: 'A', tutar: 16.5 },
      { kanalId: 2, kanal: 'B', tutar: 23.5 },
    ],
  };
  const detach = await openApp(false, {
    '/api/kredikartlari': cards,
    '/api/alis': [purchase],
    '/api/alis/kanallar': [
      { id: 1, ad: 'A', aktif: true },
      { id: 2, ad: 'B', aktif: true },
      { id: 3, ad: 'C', aktif: true },
    ],
  });
  await detach.app.navigate('purchases');
  await detach.app.cancelPayment(purchase, payment);
  assert.deepEqual(
    ['ayir-1', 'ayir-2', 'ayir-3'].map(name => formField(detach.nodes, name).value),
    ['36.5', '23.5', '']
  );
  // Eski borç devri: önerilen önceden sayılan = min(kalan borç, sistem kart borcu) − rapor dışı düşüm.
  const transfer = {
    harcamaId: 10,
    tarih: '2026-09-23',
    kalanBorc: 100,
    kasadaOncedenSayilanTutar: 80,
    iadeDuzeltmesi: 0,
    dagilimlar: [{ kanalId: 1, kanal: 'MEZAT', tutar: 100 }],
    sistemKartBorcu: 80.5,
    raporDisiTutar: 5.25,
    acilisBorcu: 0,
    onerilenKasadaSayilanTutar: 75.25,
    duzeltilebilir: true,
    engel: null,
  };
  const finance = await openApp(false, { '/api/kanallar': [{ id: 1, ad: 'MEZAT', aktif: true }] });
  await finance.app.financeUi.transferDialog(sampleCard, transfer);
  const debt = formField(finance.nodes, 'kalanBorc');
  const counted = formField(finance.nodes, 'kasadaOncedenSayilanTutar');
  for (const [typed, expected] of [
    ['70', '64.75'],
    ['90', '75.25'],
    ['3', '0'],
  ]) {
    debt.value = typed;
    debt.listeners.input();
    assert.equal(counted.value, expected, typed);
  }
  debt.value = 'x';
  debt.listeners.input();
  assert.equal(counted.value, '0', 'geçersiz tutarda öneri değişmez');
});

// Aşama 3 · toplamlar kuruşla: sunucu tutarlarının toplamı ve farkı tamsayı kuruşla (sumCents / serverCents) hesaplanır. Kayan
// nokta toplamı alana "36.010000000000005" yazıyor (tutar ayrıştırıcısı onu reddeder) ya da sıfır sonucu "-₺0,00" gösteriyordu.
test('sumCents sunucu tutarlarını tamsayı kuruşla toplar; eksik değer 0, eksi ve üslü sayı serverCents gibi', () => {
  assert.equal(ui.sumCents([0.1, 0.2]), 30);
  assert.equal(ui.sumCents([0.3, -0.1, -0.2]), 0);
  assert.equal(ui.sumCents([20, 16.01]), 3601);
  assert.equal(ui.sumCents([1e-7, 1234567.37, -0.07]), 123456730);
  assert.equal(ui.sumCents([null, undefined, 0]), 0);
  assert.equal(ui.sumCents([]), 0);
  assert.equal(ui.sumCents(null), 0);
});

test('para hesapları kayan nokta artığı göstermez: kart borcu etkisi, ana sayfa kart borcu, ayırma payı, devir önerisi ve farklar', async () => {
  // Ekstre önizlemesi: 0,30 harcama − 0,10 − 0,20 iade = 0 (eskiden "-₺0,00").
  const line = (satirNo, islemTuru, tutar) => ({
    satirNo,
    tarih: '2026-09-23',
    aciklama: 'x',
    tutar,
    islemTuru,
    kasaEtkisi: 0,
    dagilimlar: [],
    uyarilar: [],
  });
  const imports = await openApp(
    false,
    importResponses(importDocument(), {
      '/api/ekstre-aktar/12/onizleme': importPreview({
        satirlar: [line(1, 'KartHarcama', 0.3), line(2, 'KartIade', 0.1), line(3, 'KartIade', 0.2)],
      }),
    })
  );
  await imports.app.navigate('imports', 12);
  await chooseImportRow(imports.nodes);
  await clickView(imports.nodes, 'Seçilenleri önizle');
  assert.match(imports.nodes.get('#view').textContent, /Harcama \/ iade borç etkisi₺0,00Kart/);
  // Ana sayfa: aynı kanalın kart borcu satırları toplamı (0,30 − 0,10 − 0,20) sıfır; eksi sıfır yazılmaz.
  const home = sampleHome();
  home.takipOzeti.kanalKartBorclari = [
    { kanalId: 1, kanal: 'MEZAT', tutar: 0.3 },
    { kanalId: 1, kanal: 'MEZAT', tutar: -0.1 },
    { kanalId: 1, kanal: 'MEZAT', tutar: -0.2 },
    { kanalId: 2, kanal: 'PERAKENDE', tutar: 0.1 },
    { kanalId: 2, kanal: 'PERAKENDE', tutar: 0.2 },
  ];
  const homeApp = await openApp(false, { [homeSummaryPath]: home });
  const balances = homeApp.nodes.get('#view').find(node => node.className === 'channel-balances');
  assert.match(balances.children[0].textContent, /Kalan kart borcu: ₺0,00$/);
  assert.match(balances.children[1].textContent, /Kalan kart borcu: ₺0,30$/);
  // Alış ödemesini ayırma: aynı kanalın payları 20 + 16,01 = 36,01 (eskiden "36.010000000000005", tutar ayrıştırıcısı reddederdi).
  const purchase = { id: 6, surum: 3, tarih: '2026-09-23', tedarikci: 'Alış', durum: 'Taslak', kalemler: [], odemeler: [], toplam: 60 };
  const payment = {
    id: 8,
    tarih: '2026-09-23',
    tutar: 60,
    krediKartiId: 2,
    dagilimBekliyor: false,
    dagilimlar: [
      { kanalId: 1, kanal: 'A', tutar: 20 },
      { kanalId: 1, kanal: 'A', tutar: 16.01 },
      { kanalId: 2, kanal: 'B', tutar: 23.99 },
    ],
  };
  const detach = await openApp(false, {
    '/api/kredikartlari': [{ id: 2, ad: 'Takipli', yeniTakip: true, aktif: true }],
    '/api/alis': [purchase],
    '/api/alis/kanallar': [
      { id: 1, ad: 'A', aktif: true },
      { id: 2, ad: 'B', aktif: true },
    ],
    '/api/alis/6/odemeler/8/iptal': purchase,
  });
  await detach.app.navigate('purchases');
  await detach.app.cancelPayment(purchase, payment);
  assert.deepEqual(
    ['ayir-1', 'ayir-2'].map(name => formField(detach.nodes, name).value),
    ['36.01', '23.99']
  );
  const keep = formField(detach.nodes, 'harcamayiKoru');
  keep.checked = true;
  keep.listeners.change();
  formField(detach.nodes, 'aciklama').value = 'Başka alışın';
  await submitDialog(detach.nodes);
  assert.deepEqual(detach.calls.find(call => call.path === '/api/alis/6/odemeler/8/iptal').body.kanalDagilimlari, [
    { kanalId: 1, tutar: 36.01 },
    { kanalId: 2, tutar: 23.99 },
  ]);
  // Eski borç devri: min(100,10; 200) − 0,20 = 99,90 (eskiden "99.89999999999999", tutar ayrıştırıcısı reddederdi).
  const transfer = {
    harcamaId: 10,
    tarih: '2026-09-23',
    kalanBorc: 100,
    kasadaOncedenSayilanTutar: 80,
    iadeDuzeltmesi: 0,
    dagilimlar: [{ kanalId: 1, kanal: 'MEZAT', tutar: 100 }],
    sistemKartBorcu: 200,
    raporDisiTutar: 0.2,
    acilisBorcu: 0,
    onerilenKasadaSayilanTutar: 99.8,
    duzeltilebilir: true,
    engel: null,
  };
  const finance = await openApp(false, { '/api/kanallar': [{ id: 1, ad: 'MEZAT', aktif: true }] });
  await finance.app.financeUi.transferDialog(sampleCard, transfer);
  const debt = formField(finance.nodes, 'kalanBorc');
  debt.value = '100,1';
  debt.listeners.input();
  assert.equal(formField(finance.nodes, 'kasadaOncedenSayilanTutar').value, '99.9');
  // Aylık kalan plan ve kasa kontrolü farkları: iki sunucu tutarının farkı kuruşla (ekrandaki sonuç aynı; eşitse "₺0,00").
  for (const [planned, paid, expected] of [
    [0.3, 0.1, '₺0,20'],
    [0.3, 0.3, '₺0,00'],
    [100.1, 100.35, '-₺0,25'],
  ]) {
    const monthly = await openApp(false, {
      ...monthlyResponses(),
      [monthlyPath]: { yil: yearNow, ay: monthNumberNow, planlananToplam: planned, odenenToplam: paid, kayitlar: [] },
    });
    await monthly.app.navigate('monthly-expenses');
    await settle();
    assert.ok(monthly.nodes.get('#view').textContent.includes(`Kalan plan${expected}`), `${planned} − ${paid}`);
  }
  const cash = await openApp(false, {
    '/api/kasa-kontrol/2/sonrasi': {
      kontrolId: 2,
      esasTarih: '2026-09-20',
      filigranVar: true,
      sistemBakiye: 0.1,
      guncelSistemBakiye: 0.3,
      bugunkuSistemBakiye: 0.3,
      kirpildi: false,
      degisiklikler: [],
      istekler: [],
      hareketler: [],
    },
  });
  await cash.app.cashControlsUi.sinceDialog({ id: 2 });
  const sinceText = cash.nodes.get('#modal-content').textContent;
  assert.ok(sinceText.includes('Geriye dönük değişim ₺0,20'), sinceText);
  assert.ok(sinceText.includes('Kontrol gününden sonra ₺0,00'), sinceText);
});
