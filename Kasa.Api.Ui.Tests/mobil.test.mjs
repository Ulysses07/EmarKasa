import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { runInNewContext } from 'node:vm';

const kaynak = dosya => readFile(new URL(`../Kasa.Api/wwwroot/${dosya}`, import.meta.url), 'utf8');
const m = await import(new URL('../Kasa.Api/wwwroot/m/mobil-cekirdek.js', import.meta.url));

test('telefonda yazılan tutar Türkçe binlik noktasıyla doğru kuruşa çevrilir', () => {
  assert.equal(m.tutarCoz('1500'), 150000);
  assert.equal(m.tutarCoz('1.500'), 150000);
  assert.equal(m.tutarCoz('1.500.000'), 150000000);
  assert.equal(m.tutarCoz('1.500,50'), 150050);
  assert.equal(m.tutarCoz('1500,5'), 150050);
  assert.equal(m.tutarCoz('1500.50'), 150050);
  assert.equal(m.tutarCoz('0,05'), 5);
  assert.equal(m.tutarCoz(' 2.450,00 ₺ '), 245000);
  assert.equal(m.tutarCoz('0', { sifirOlabilir: true }), 0);
});

test('belirsiz ya da hatalı tutar sessizce yuvarlanmaz, hata verir', () => {
  for (const girdi of ['', '1,500', '1,2,3', '15.00.0', '1.50.000,00', '12,345', 'abc', '-5', '0', '0.500', '0.050', '0.500,00']) {
    assert.throws(() => m.tutarCoz(girdi), Error, girdi);
  }
  assert.throws(() => m.tutarCoz('1000000000000'), /çok büyük/);
});

test('tutarlar Türkçe biçimde ve işaretle yazılır', () => {
  assert.equal(m.tl(1308800), '1.308.800,00 ₺');
  assert.equal(m.imzali(47750), '+47.750,00 ₺');
  assert.equal(m.imzali(-4200), '−4.200,00 ₺');
  assert.equal(m.imzali(-0.001), '0,00 ₺');
  assert.equal(m.isaretSinifi(-0.004), 'notr');
  assert.equal(m.isaretSinifi(12), 'arti');
  assert.equal(m.isaretSinifi(-12), 'eksi');
});

test('dönem adları ay içinde, ay sınırında ve tek günde doğru', () => {
  assert.equal(m.donemAdi({ start: '2026-09-21', end: '2026-09-27' }), '21–27 Eylül 2026');
  assert.equal(m.donemAdi({ start: '2026-08-31', end: '2026-08-31' }), '31 Ağustos 2026');
  assert.equal(m.donemAdi({ start: '2026-12-28', end: '2027-01-03' }), '28 Ara 2026 – 3 Oca 2027');
  assert.equal(m.donemKisa({ start: '2026-09-21', end: '2026-09-26' }), '21–26 Eylül');
  assert.equal(m.bolunmusMu({ start: '2026-09-21', end: '2026-09-27' }), false);
  assert.equal(m.bolunmusMu({ start: '2026-09-01', end: '2026-09-06' }), true);
  assert.equal(m.bolunmusMu({ start: '2026-08-31', end: '2026-08-31' }), true);
  assert.equal(m.bolunmusMu({ start: '2026-09-21', end: '2026-09-26' }), false, 'bugünle kesilen açık dönem');
  assert.equal(m.bolunmusMu({ start: '2026-04-15', end: '2026-04-19' }), false, 'takip başlangıcındaki ilk dönem');
  assert.equal(m.bolunmusMu({ start: '2026-05-25', end: '2026-05-31' }), false, 'pazar günü biten ay sonu');
  assert.equal(m.uzunTarih('2026-09-26'), '26 Eylül Cumartesi');
  assert.equal(m.gunEkle('2026-03-01', -1), '2026-02-28');
  assert.equal(m.gunFarki('2026-09-26', '2026-10-05'), 9);
});

test('masaüstü bildirim adresleri telefon ekranlarına çevrilir', () => {
  assert.deepEqual(m.rotaCoz('#cards/3'), { ekran: 'kartlar', id: 3 });
  assert.deepEqual(m.rotaCoz('#loans/12'), { ekran: 'krediler', id: 12 });
  assert.deepEqual(m.rotaCoz('#notifications'), { ekran: 'bildirimler', id: null });
  assert.deepEqual(m.rotaCoz('#home'), { ekran: 'panel', id: null });
  assert.deepEqual(m.rotaCoz(''), { ekran: 'panel', id: null });
  assert.deepEqual(m.rotaCoz('#islem/0'), { ekran: 'islem', id: null });
});

test('işlemler gün gün, en yeni önce ve kuruş toplamıyla gruplanır', () => {
  const gruplar = m.gunlereAyir([
    { id: 1, tarih: '2026-09-24', tutarTl: 0.1, kanal: 'MEZAT' },
    { id: 2, tarih: '2026-09-25', tutarTl: 100, kanal: 'TOPTAN' },
    { id: 3, tarih: '2026-09-24', tutarTl: 0.2, kanal: 'MEZAT' },
  ]);
  assert.deepEqual(
    gruplar.map(g => [g.tarih, g.toplam, g.islemler.map(i => i.id)]),
    [
      ['2026-09-25', 100, [2]],
      ['2026-09-24', 0.3, [3, 1]],
    ]
  );
});

test('işlem araması Türkçe büyük/küçük harfe duyarsız, kanal ve tip filtreleri birlikte çalışır', () => {
  const islem = { cari: 'İSTANBUL KARGO', not: 'Irsaliye', kanal: 'MEZAT', tip: 'Cari' };
  assert.equal(m.islemEslesir(islem, { ara: 'istanbul' }), true);
  assert.equal(m.islemEslesir(islem, { ara: 'ırsaliye' }), true);
  assert.equal(m.islemEslesir(islem, { kanal: 'TOPTAN' }), false);
  assert.equal(m.islemEslesir(islem, { tip: 'SabitGider' }), false);
  assert.equal(m.islemEslesir(islem, { ara: 'kargo', kanal: 'MEZAT', tip: 'Cari' }), true);
  const ortak = { cari: 'Kira', kanal: 'MEZAT / TOPTAN', tip: 'SabitGider' };
  assert.equal(m.islemEslesir(ortak, { kanal: 'MEZAT' }), true, 'birden çok kanala bölünen gider');
  assert.equal(m.islemEslesir(ortak, { kanal: 'TOPTAN' }), true);
  assert.equal(m.islemEslesir(ortak, { kanal: 'PERAKENDE' }), false);
});

test('gider tipi adları masaüstüyle aynı, bilinmeyen kanal sabit bir renk alır', () => {
  assert.equal(m.tipAdi('Cari'), 'Diğer gider');
  assert.equal(m.tipAdi('SabitGider'), 'Sabit gider');
  assert.deepEqual(m.kanalRengi('mezat'), m.kanalRengi('MEZAT'));
  assert.deepEqual(m.kanalRengi('Yeni kanal'), m.kanalRengi('Yeni kanal'));
  assert.match(m.kanalRengi('Dağılım bekliyor').r, /^#[0-9A-F]{6}$/);
});

test('iPhone ve iPad iOS görünümünü, diğer cihazlar Android görünümünü alır', () => {
  assert.equal(m.platformBul('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)'), 'ios');
  assert.equal(m.platformBul('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 5), 'ios');
  assert.equal(m.platformBul('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 0), 'android');
  assert.equal(m.platformBul('Mozilla/5.0 (Linux; Android 15; Pixel 9)'), 'android');
});

test('grafik yolları kutunun içinde kalır', () => {
  const y = m.cizgiYolu([10, 30, 20], 100, 50, 5);
  assert.equal(y.noktalar.length, 3);
  for (const [x, yy] of y.noktalar) {
    assert.ok(x >= 5 && x <= 95);
    assert.ok(yy >= 5 && yy <= 45);
  }
  assert.deepEqual(m.cizgiYolu([], 100, 50, 5).noktalar, []);
  const o = m.cubukOlcek([300, -100]);
  assert.equal(o.ust + o.alt, 100);
});

test('telefon arayüzünün modülleri ES modülü olarak ayrıştırılır ve kullandığı ikonlar sprite içinde var', async () => {
  for (const dosya of ['m/app.js', 'm/mobil-cekirdek.js']) {
    const sonuc = spawnSync(process.execPath, ['--input-type=module', '--check'], { input: await kaynak(dosya), encoding: 'utf8' });
    assert.equal(sonuc.status, 0, `${dosya}: ${sonuc.stderr}`);
  }
  const app = await kaynak('m/app.js');
  const sprite = await kaynak('m/icons.svg');
  const kimlikler = new Set([...sprite.matchAll(/<symbol id="([^"]+)"/g)].map(x => x[1]));
  // Kaynak biçimden bağımsız okunur: çağrı bağımsız değişkenleri ve dizi öğeleri satırlara bölünmüş olabilir (Prettier).
  const ikonlar = [...app.matchAll(/ikon\(\s*'([a-z_]+)'/g)].map(x => x[1]);
  assert.ok(ikonlar.length > 0, 'ikon() çağrıları okunamadı');
  for (const ad of ikonlar) assert.ok(kimlikler.has(ad), `ikon eksik: ${ad}`);
  const sekmeler = app.match(/const SEKMELER = (\[[\s\S]*?\]);/)[1];
  const sekmeIkonlari = [...sekmeler.matchAll(/\[\s*'[a-z]+',\s*'([a-z_]+)',\s*'[^']+',?\s*\]/g)].map(x => x[1]);
  assert.ok(sekmeIkonlari.length > 0, 'SEKMELER okunamadı');
  for (const ad of sekmeIkonlari) {
    assert.ok(kimlikler.has(ad), `sekme ikonu eksik: ${ad}`);
    assert.ok(kimlikler.has(`${ad}-fill`), `dolu sekme ikonu eksik: ${ad}`);
  }
  for (const ad of [
    'lock',
    'edit_calendar',
    'error',
    'check_circle',
    'keyboard_arrow_up',
    'keyboard_arrow_down',
    'monitoring',
    'desktop_windows',
  ])
    assert.ok(kimlikler.has(ad), `ikon eksik: ${ad}`);
});

test('telefon sayfası CSP ile uyumlu: satır içi betik yok, yalnız aynı kökten kaynak', async () => {
  const html = await kaynak('m/index.html');
  assert.doesNotMatch(html, /<script(?![^>]*\bsrc=)[^>]*>/);
  assert.doesNotMatch(html, /\son[a-z]+=/i);
  for (const [, adres] of html.matchAll(/(?:src|href)="([^"]+)"/g)) assert.match(adres, /^\//, adres);
  assert.match(html, /viewport-fit=cover/);
});

function yonlendir({ kaba = true, en = 390, boy = 844, secim = null, hash = '', search = '' } = {}) {
  const olaylar = {};
  const eklenen = [];
  const depo = new Map(secim ? [['kasa.gorunum', secim]] : []);
  const pencere = {
    matchMedia: q => ({ matches: q === '(pointer: coarse)' && kaba }),
    screen: { width: en, height: boy },
    localStorage: { getItem: k => depo.get(k) ?? null, removeItem: k => depo.delete(k) },
    location: {
      hash,
      search,
      href: '/',
      replace(u) {
        this.gidilen = u;
      },
    },
    document: {
      addEventListener: (ad, fn) => {
        olaylar[ad] = fn;
      },
      querySelector: () => ({ insertBefore: d => eklenen.push(d) }),
      getElementById: id => ({ id, parentNode: { insertBefore: d => eklenen.push(d) }, nextSibling: null }),
      createElement: () => ({
        addEventListener(ad, fn) {
          this.tik = fn;
        },
      }),
    },
  };
  pencere.window = pencere;
  return { pencere, olaylar, eklenen, depo };
}

test('telefon masaüstü adresine gelince bildirim adresiyle birlikte telefon arayüzüne gider', async () => {
  const betik = await kaynak('telefon-yonlendir.js');
  const t = yonlendir({ hash: '#cards/3' });
  runInNewContext(betik, t.pencere);
  assert.equal(t.pencere.location.gidilen, '/m/#cards/3');
});

test('bilgisayar ve tablet masaüstü arayüzünde kalır', async () => {
  const betik = await kaynak('telefon-yonlendir.js');
  for (const ayar of [
    { kaba: false, en: 390 },
    { kaba: true, en: 820, boy: 1180 },
  ]) {
    const t = yonlendir(ayar);
    runInNewContext(betik, t.pencere);
    assert.equal(t.pencere.location.gidilen, undefined);
    assert.equal(t.olaylar.DOMContentLoaded, undefined);
  }
});

test('masaüstü görünümünü seçen telefon kalır ve telefon görünümüne dönüş düğmesi alır', async () => {
  const betik = await kaynak('telefon-yonlendir.js');
  const t = yonlendir({ secim: 'masaustu', hash: '#home' });
  runInNewContext(betik, t.pencere);
  assert.equal(t.pencere.location.gidilen, undefined);
  t.olaylar.DOMContentLoaded();
  assert.equal(t.eklenen.length, 2);
  assert.equal(t.eklenen[0].textContent, 'Telefon görünümü');
  t.eklenen[0].tik();
  assert.equal(t.depo.has('kasa.gorunum'), false);
  assert.equal(t.pencere.location.href, '/m/#home');
});

test('tarayıcı seçimi saklayamıyorsa adresteki işaret telefonu masaüstünde tutar', async () => {
  const betik = await kaynak('telefon-yonlendir.js');
  const t = yonlendir({ search: '?gorunum=masaustu', hash: '#home' });
  runInNewContext(betik, t.pencere);
  assert.equal(t.pencere.location.gidilen, undefined);
  const baska = yonlendir({ search: '?gorunum=masaustux' });
  runInNewContext(betik, baska.pencere);
  assert.equal(baska.pencere.location.gidilen, '/m/');
});

test('masaüstü sayfası yönlendirme betiğini modülden önce, klasik betik olarak yükler', async () => {
  const html = await kaynak('index.html');
  const yon = html.indexOf('<script src="/telefon-yonlendir.js');
  assert.ok(yon > 0);
  assert.ok(yon < html.indexOf('<script type="module"'));
});

test('hızlı giderde yalnız yeni takipteki açık kartlar seçilir (takipsiz ya da kapalı karta sunucu gider almaz)', () => {
  const kartlar = [
    { id: 1, ad: 'Takipte', yeniTakip: true, aktif: true },
    { id: 2, ad: 'Eski model', yeniTakip: false, aktif: true },
    { id: 3, ad: 'Kapalı', yeniTakip: true, aktif: false },
  ];
  assert.deepEqual(
    m.giderKartlari(kartlar).map(k => k.id),
    [1]
  );
  assert.deepEqual(m.giderKartlari(undefined), []);
});

async function hizliGiderKimligi(storage, ids) {
  const app = await kaynak('m/app.js');
  const bas = app.indexOf('// ---------- hızlı gider istek kimliği ----------');
  const son = app.indexOf('// ---------- hızlı gider istek kimliği sonu ----------');
  assert.ok(bas >= 0 && son > bas);
  const baglam = {
    sessionStorage: storage,
    crypto: { randomUUID: () => ids.shift() },
  };
  runInNewContext(app.slice(bas, son) + '\nthis.kimlik = { al: giderIstekId, temizle: giderIstekTemizle };', baglam);
  return baglam.kimlik;
}

test('hızlı gider yanıtı belirsiz kalınca aynı sekmede ve yeniden yüklemede istek kimliği korunur', async () => {
  const depo = new Map();
  const storage = {
    getItem: key => depo.get(key) ?? null,
    setItem: (key, value) => depo.set(key, value),
    removeItem: key => depo.delete(key),
  };
  const ilkId = '11111111-1111-4111-8111-111111111111';
  const yeniId = '22222222-2222-4222-8222-222222222222';
  const ilk = await hizliGiderKimligi(storage, [ilkId]);
  assert.equal(ilk.al(), ilkId);
  assert.equal(ilk.al(), ilkId, 'Aynı formda değişen gider de ilk istek kimliğini taşır.');
  const yeniden = await hizliGiderKimligi(storage, [yeniId]);
  assert.equal(yeniden.al(), ilkId, 'Aynı sekmede sayfa yeniden yüklense de yanıtı belirsiz istek yinelenir.');
  yeniden.temizle(ilkId);
  assert.equal(depo.size, 0, 'Başarı kesinleşince bekleyen kimlik silinir.');
  assert.equal(yeniden.al(), yeniId, 'Sonraki gider yeni bir kimlik alır.');
});

test('hızlı giderde başka isteğin temizlenmesi bekleyen kimliği kaldırmaz ve depolama kapalıysa bellek kullanılır', async () => {
  const ilkId = '33333333-3333-4333-8333-333333333333';
  const yeniId = '44444444-4444-4444-8444-444444444444';
  const storage = {
    getItem: () => {
      throw new Error('disabled');
    },
    setItem: () => {
      throw new Error('disabled');
    },
    removeItem: () => {
      throw new Error('disabled');
    },
  };
  const kimlik = await hizliGiderKimligi(storage, [ilkId, yeniId]);
  assert.equal(kimlik.al(), ilkId);
  kimlik.temizle(yeniId);
  assert.equal(kimlik.al(), ilkId);
  kimlik.temizle(ilkId);
  assert.equal(kimlik.al(), yeniId);
});

test('mobil benzer kayıt sorgusu sürerken gider değişirse eski tutar kaydedilmez', async () => {
  const kaynakKod = await kaynak('m/app.js');
  const bas = kaynakKod.indexOf('async function hizliIslemAc()');
  const son = kaynakKod.indexOf('function kurtarmaAc()', bas);
  assert.ok(bas >= 0 && son > bas);
  const h = (tag, props = {}, ...items) => {
    const node = {
      tag,
      props,
      children: items.flat(Infinity).filter(x => x != null),
      listeners: {},
      value: props.value ?? '',
      hidden: props.hidden ?? false,
      disabled: false,
      textContent: items
        .flat(Infinity)
        .filter(x => typeof x === 'string')
        .join(''),
      addEventListener(name, fn) {
        this.listeners[name] = fn;
      },
      replaceChildren(...children) {
        this.children = children.flat(Infinity);
      },
      focus() {},
      find(predicate) {
        return predicate(this)
          ? this
          : this.children
              .filter(x => x && typeof x === 'object')
              .map(x => x.find(predicate))
              .find(Boolean);
      },
      querySelector(selector) {
        return this.find(x => x.tag === selector);
      },
    };
    for (const [key, fn] of Object.entries(props))
      if (key.startsWith('on') && typeof fn === 'function') node.listeners[key.slice(2).toLowerCase()] = fn;
    return node;
  };
  let root,
    finishLookup,
    writes = 0;
  const context = {
    h,
    acikSayfa: null,
    editorMu: () => true,
    ORTAK: 'Ortak',
    durum: { islemOnbellek: [{ cari: 'Eski', tarih: '2026-09-23' }] },
    api: (path, options) => {
      if (path === '/api/kanallar') return [{ ad: 'MEZAT', aktif: true }];
      if (path === '/api/kredikartlari') return [];
      if (path === '/api/islemler/benzerlik')
        return new Promise(resolve => {
          finishLookup = resolve;
        });
      if (path === '/api/islemler' && options.method === 'POST') writes++;
      return {};
    },
    isoGun: () => '2026-09-23',
    gunEkle: date => date,
    kisaTarih: date => date,
    kanalRengi: () => ({ z: '#fff', r: '#000' }),
    giderKartlari: () => [],
    tipAdi: type => type,
    tutarCoz: value => Number(value) * 100,
    tl: value => String(value),
    giderIstekId: () => '11111111-1111-4111-8111-111111111111',
    giderIstekTemizle: () => {},
  };
  context.sayfaAc = factory => {
    const kapat = () => {
      context.acikSayfa = null;
    };
    context.acikSayfa = { kapat };
    root = factory(kapat);
  };
  runInNewContext(kaynakKod.slice(bas, son) + '\nthis.hizliIslemAcTest = hizliIslemAc;', context);
  await context.hizliIslemAcTest();
  const amount = root.find(n => n.tag === 'input' && n.props['aria-label'] === 'Tutar');
  amount.value = '75';
  amount.listeners.input();
  const cari = root.find(n => n.tag === 'input' && n.props.placeholder === 'Firma, kişi ya da ödeme yeri');
  cari.value = 'Kargo';
  cari.listeners.input();
  root.find(n => n.tag === 'button' && n.textContent === 'MEZAT').listeners.click();
  const saving = root.find(n => n.tag === 'button' && n.textContent === 'Kaydet' && n.props.class === 'dugme ana').listeners.click();
  assert.equal(typeof finishLookup, 'function');
  amount.value = '80';
  amount.listeners.input();
  finishLookup([]);
  await saving;
  assert.equal(writes, 0);
  assert.match(root.find(n => n.props.role === 'alert').textContent, /Alanlar benzer kayıt kontrolü sırasında değişti/);
});

test('mobil hızlı gider kapatılmışken POST sonucu görünür kalır ve istek kimliği yalnız başarıda temizlenir', async () => {
  const kaynakKod = await kaynak('m/app.js');
  const bas = kaynakKod.indexOf('async function hizliIslemAc()');
  const son = kaynakKod.indexOf('function kurtarmaAc()', bas);
  assert.ok(bas >= 0 && son > bas);
  const h = (tag, props = {}, ...items) => {
    const node = {
      tag,
      props,
      children: items.flat(Infinity).filter(x => x != null),
      listeners: {},
      value: props.value ?? '',
      hidden: props.hidden ?? false,
      disabled: false,
      textContent: items
        .flat(Infinity)
        .filter(x => typeof x === 'string')
        .join(''),
      addEventListener(name, fn) {
        this.listeners[name] = fn;
      },
      replaceChildren(...children) {
        this.children = children.flat(Infinity);
      },
      focus() {},
      find(predicate) {
        return predicate(this)
          ? this
          : this.children
              .filter(x => x && typeof x === 'object')
              .map(x => x.find(predicate))
              .find(Boolean);
      },
      querySelector(selector) {
        return this.find(x => x.tag === selector);
      },
    };
    for (const [key, fn] of Object.entries(props))
      if (key.startsWith('on') && typeof fn === 'function') node.listeners[key.slice(2).toLowerCase()] = fn;
    return node;
  };
  let root, post;
  let temizleme = 0,
    yenileme = 0;
  const bildirimler = [];
  const istekler = [];
  const context = {
    h,
    acikSayfa: null,
    editorMu: () => true,
    ORTAK: 'Ortak',
    durum: { islemOnbellek: [{ cari: 'Eski', tarih: '2026-09-23' }] },
    api: (path, options) => {
      if (path === '/api/kanallar') return [{ ad: 'MEZAT', aktif: true }];
      if (path === '/api/kredikartlari' || path === '/api/islemler/benzerlik') return [];
      if (path === '/api/islemler' && options.method === 'POST') {
        istekler.push(options.body);
        return new Promise((resolve, reject) => {
          post = { resolve, reject };
        });
      }
      throw new Error(`Beklenmeyen istek: ${path}`);
    },
    isoGun: () => '2026-09-23',
    gunEkle: date => date,
    kisaTarih: date => date,
    kanalRengi: () => ({ z: '#fff', r: '#000' }),
    giderKartlari: () => [],
    tipAdi: type => type,
    tutarCoz: value => Number(value) * 100,
    tl: value => String(value),
    giderIstekId: () => '11111111-1111-4111-8111-111111111111',
    giderIstekTemizle: () => {
      temizleme++;
    },
    tost: (mesaj, hata) => bildirimler.push({ mesaj, hata: Boolean(hata) }),
    ciz: () => {
      yenileme++;
    },
  };
  context.sayfaAc = (factory, _title) => {
    const kapat = () => {
      if (context.acikSayfa?.kapat !== kapat) return;
      context.acikSayfa.kapanirken?.();
      context.acikSayfa = null;
    };
    context.acikSayfa = { kapat };
    root = factory(kapat);
  };
  runInNewContext(kaynakKod.slice(bas, son) + '\nthis.hizliIslemAcTest = hizliIslemAc;', context);

  const gonder = async () => {
    await context.hizliIslemAcTest();
    const tutar = root.find(n => n.tag === 'input' && n.props['aria-label'] === 'Tutar');
    tutar.value = '75';
    tutar.listeners.input();
    const cari = root.find(n => n.tag === 'input' && n.props.placeholder === 'Firma, kişi ya da ödeme yeri');
    cari.value = 'Kargo';
    cari.listeners.input();
    root.find(n => n.tag === 'button' && n.textContent === 'MEZAT').listeners.click();
    const sonuc = root.find(n => n.tag === 'button' && n.textContent === 'Kaydet' && n.props.class === 'dugme ana').listeners.click();
    await new Promise(resolve => setImmediate(resolve));
    assert.ok(post, 'POST isteği başladı');
    context.acikSayfa.kapat();
    assert.match(bildirimler.at(-1).mesaj, /Gider kaydı sürüyor/);
    return { sonuc };
  };

  const { sonuc: ilk } = await gonder();
  post.reject(new Error('Bağlantı kesildi'));
  await ilk;
  assert.match(bildirimler.at(-1).mesaj, /sonucu doğrulanamadı.*İşlemlerden kontrol edin/);
  assert.equal(bildirimler.at(-1).hata, true);
  assert.equal(temizleme, 0, 'Yanıtı alınamayan isteğin kimliği korunur');
  assert.equal(yenileme, 0);

  post = null;
  const { sonuc: ikinci } = await gonder();
  assert.equal(istekler[1].istekId, istekler[0].istekId, 'Tekrar aynı istek kimliğini kullanır');
  post.resolve({});
  await ikinci;
  assert.match(bildirimler.at(-1).mesaj, /İşlem eklendi/);
  assert.equal(temizleme, 1, 'Kimlik yalnız kesin başarıda temizlenir');
  assert.equal(yenileme, 1);
});

test('mobil alt sayfa kapanışı formun kayıt durumu bildirimini tek kez çalıştırır', async () => {
  const kaynakKod = await kaynak('m/app.js');
  const bas = kaynakKod.indexOf('function sayfaAc(');
  const son = kaynakKod.indexOf('async function hizliIslemAc()', bas);
  assert.ok(bas >= 0 && son > bas);
  let kapanislar = 0;
  const context = {
    acikSayfa: null,
    sayfaKapatAnlik: () => {},
    $: () => ({ replaceChildren() {} }),
    document: { activeElement: null, addEventListener() {}, removeEventListener() {} },
    history: {
      state: null,
      pushState(state) {
        this.state = state;
      },
      back() {
        this.state = null;
      },
    },
    kok: { inert: false },
    h: () => ({ querySelector: () => null }),
    setTimeout: () => 0,
  };
  runInNewContext(kaynakKod.slice(bas, son) + '\nthis.sayfaAcTest = sayfaAc;', context);
  context.sayfaAcTest(() => null, 'Hızlı işlem');
  const acilan = context.acikSayfa;
  acilan.kapanirken = () => {
    kapanislar++;
  };
  acilan.kapat();
  acilan.kapat();
  assert.equal(kapanislar, 1);
  assert.equal(context.acikSayfa, null);
  assert.equal(context.kok.inert, false);
});
