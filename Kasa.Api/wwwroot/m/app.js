// Emar Kasa telefon arayüzü (tasarım "Emar Kasa Mobil", Yön A · Defter).
// Masaüstü web arayüzüyle aynı API'yi ve aynı oturum çerezini kullanır; para kurallarını değiştirmez.
import { errorMessage, loadRuntime, runtimeRequestAllowed, cashEditingAllowed, monthlyTotals, currentPeriod } from '../ui-core.js';
import {
  AYLAR,
  AYK,
  ORTAK,
  tl,
  f2,
  imzali,
  imzaliYalin,
  isaretSinifi,
  uzunTarih,
  tamTarih,
  kisaTarih,
  isoGun,
  gunEkle,
  gunFarki,
  donemAdi,
  donemKisa,
  bolunmusMu,
  tutarCoz,
  cizgiYolu,
  cubukOlcek,
  kanalRengi,
  tipAdi,
  giderKartlari,
  platformBul,
  rotaCoz,
  gunlereAyir,
  islemEslesir,
} from './mobil-cekirdek.js';

const IKON = '/m/icons.svg';
const MASAUSTU_ANAHTAR = 'kasa.gorunum';
const SEKMELER = [
  ['panel', 'space_dashboard', 'Panel'],
  ['haftalik', 'date_range', 'Haftalık'],
  ['aylik', 'calendar_month', 'Aylık'],
  ['islemler', 'list_alt', 'İşlemler'],
  ['diger', 'more_horiz', 'Diğer'],
];
const BASLIK = {
  panel: 'Panel',
  haftalik: 'Haftalık',
  aylik: 'Aylık',
  islemler: 'İşlemler',
  diger: 'Diğer',
  donem: 'Dönem ayrıntısı',
  islem: 'İşlem',
  trend: 'Trend',
  kartlar: 'Kredi kartları',
  krediler: 'Krediler',
  bildirimler: 'Bildirimler',
};
const KAYNAK_ADLARI = { Islem: 'Gider', KartHarcama: 'Kart harcaması', KartOdeme: 'Kart ödemesi', EskiKartOdeme: 'Eski kart ödemesi' };
const UST = { donem: 'haftalik', islem: 'islemler', trend: 'diger', kartlar: 'diger', krediler: 'diger', bildirimler: 'panel' };

// ---------- hızlı gider istek kimliği ----------
// Yanıt kaybolursa aynı sekmede yeniden açılan form da ilk isteği tekrarlar. İçerik değişse bile kimlik korunur;
// sunucu farklı gövdeyi 409 ile reddeder. Yeni işlem ancak başarıdan veya kullanıcının açık sıfırlamasından sonra başlar.
// Masaüstü web görünümüyle aynı anahtar: görünüm değiştirince bekleyen istek de korunur.
const HIZLI_GIDER_ISTEK_ANAHTARI = 'kasa:gider-olustur:v1';
let bekleyenGiderIstekId = null;
function giderIstekId() {
  if (!bekleyenGiderIstekId) {
    try {
      const saklanan = sessionStorage.getItem(HIZLI_GIDER_ISTEK_ANAHTARI);
      if (saklanan && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(saklanan)) bekleyenGiderIstekId = saklanan;
    } catch {
      // Depolama kapalıysa açık sekmedeki tekrarlar bellekteki kimliği kullanır.
    }
  }
  if (!bekleyenGiderIstekId) {
    bekleyenGiderIstekId = crypto.randomUUID();
    try {
      sessionStorage.setItem(HIZLI_GIDER_ISTEK_ANAHTARI, bekleyenGiderIstekId);
    } catch {
      // Depolama kapalıysa açık sekmedeki tekrarlar bellekteki kimliği kullanır.
    }
  }
  return bekleyenGiderIstekId;
}
function giderIstekTemizle(id) {
  if (!id || bekleyenGiderIstekId !== id) return;
  bekleyenGiderIstekId = null;
  try {
    if (sessionStorage.getItem(HIZLI_GIDER_ISTEK_ANAHTARI) === id) sessionStorage.removeItem(HIZLI_GIDER_ISTEK_ANAHTARI);
  } catch {
    // Depolama kapalıysa bellekteki kimliğin silinmesi yeterlidir.
  }
}
// ---------- hızlı gider istek kimliği sonu ----------

const platform = (() => {
  const zorla = new URLSearchParams(location.search).get('platform');
  return zorla === 'ios' || zorla === 'android' ? zorla : platformBul(navigator.userAgent, navigator.maxTouchPoints || 0);
})();
const ios = platform === 'ios';
document.body.classList.add(platform);

const durum = {
  rol: null,
  oturum: 0,
  yigin: [],
  ekranNo: 0,
  icGecis: false,
  ayOnbellek: new Map(),
  islemFiltre: { ara: '', kanal: 'Tümü', tip: 'Tümü' },
  islemBaslangic: null,
  islemOnbellek: [],
  donemOnbellek: [],
  acikHafta: 0,
  ay: null,
  trendSegment: 'kasa',
  okunmamis: 0,
};
let calisma = null;
const calismaHazir = loadRuntime(fetch).then(ayar => {
  calisma = ayar;
  return ayar;
});

// ---------- küçük DOM yardımcıları ----------
function h(etiket, ozellik = {}, ...cocuklar) {
  const el = document.createElement(etiket);
  for (const [k, v] of Object.entries(ozellik || {})) {
    if (v == null || v === false) continue;
    if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
    else if (k === 'class') el.className = v;
    else if (k === 'text') el.textContent = v;
    else if (k === 'value') el.value = v;
    else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
    else el.setAttribute(k, v === true ? '' : String(v));
  }
  ekle(el, cocuklar);
  return el;
}
function ekle(el, cocuklar) {
  for (const c of cocuklar.flat(Infinity)) {
    if (c == null || c === false) continue;
    el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
}
const SVGNS = 'http://www.w3.org/2000/svg';
function ikon(ad, boy = 24, dolu = false) {
  const svg = document.createElementNS(SVGNS, 'svg');
  svg.setAttribute('class', `ik i${boy}`);
  svg.setAttribute('aria-hidden', 'true');
  const use = document.createElementNS(SVGNS, 'use');
  use.setAttribute('href', `${IKON}#${ad}${dolu ? '-fill' : ''}`);
  svg.append(use);
  return svg;
}
function svgYol(d, sinif) {
  const p = document.createElementNS(SVGNS, 'path');
  p.setAttribute('d', d);
  p.setAttribute('class', sinif);
  return p;
}
function cizgiGrafik(degerler, yukseklik, pay) {
  const genislik = 330;
  const y = cizgiYolu(degerler, genislik, yukseklik, pay);
  const svg = document.createElementNS(SVGNS, 'svg');
  svg.setAttribute('viewBox', `0 0 ${genislik} ${yukseklik}`);
  svg.setAttribute('preserveAspectRatio', 'none');
  svg.setAttribute('height', String(yukseklik));
  svg.setAttribute('aria-hidden', 'true');
  svg.append(svgYol(y.alan, 'alan'), svgYol(y.d, 'cizgi-yol'));
  return svg;
}
const para = (deger, sinif = '') => h('span', { class: `num ${sinif}` }, tl(deger));
const isaretli = deger => h('span', { class: `num ${isaretSinifi(deger)}` }, imzali(deger));
const renkliNokta = (kanal, boy = 10) => h('span', { class: boy === 8 ? 'nokta8' : 'nokta', style: { background: kanalRengi(kanal).r } });
const kanalRozet = (kanal, kucuk = false) => {
  const r = kanalRengi(kanal);
  return h('span', { class: kucuk ? 'kanal-kucuk' : 'kanal-rozet', style: { background: r.z, color: r.r } }, kanal);
};
const etiket = metin => h('div', { class: 'etiket' }, metin);
// Yan yana kutularda uzun tutarlar alt satıra kaymasın diye yazı küçülür.
const boySinifi = metin => (metin.length > 15 ? ' d13' : metin.length > 12 ? ' d15' : '');
const okIsareti = () => h('span', { class: 'ok' }, ikon('chevron_right', 20));

// ---------- API ----------
async function api(yol, secenek = {}) {
  await calismaHazir;
  const yontem = (secenek.method || 'GET').toUpperCase();
  if (!runtimeRequestAllowed(calisma, yol, yontem)) throw new Error('Bu sürüm yalnız kasa görüntüleme içindir. Kayıtlar değiştirilemez.');
  const basliklar = new Headers();
  if (!['GET', 'HEAD'].includes(yontem)) basliklar.set('X-Kasa-Request', '1');
  let govde = secenek.body;
  if (govde != null) {
    basliklar.set('Content-Type', 'application/json');
    govde = JSON.stringify(govde);
  }
  const oturum = durum.oturum;
  let yanit;
  try {
    yanit = await fetch(yol, { method: yontem, body: govde, headers: basliklar, credentials: 'same-origin', cache: 'no-store' });
  } catch {
    throw new Error('Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.');
  }
  if (oturum !== durum.oturum) throw new Error('Oturum değişti.');
  if (!yanit.ok) {
    let sonuc = null;
    try {
      sonuc = await yanit.json();
    } catch {}
    const hata = new Error(errorMessage(sonuc, yanit.status));
    hata.status = yanit.status;
    hata.code = sonuc?.kod;
    if (yanit.status === 401 && !yol.startsWith('/api/auth/login') && !yol.startsWith('/api/auth/kurtar')) oturumuKapat();
    throw hata;
  }
  if (yanit.status === 204) return null;
  const metin = await yanit.text();
  return metin ? JSON.parse(metin) : null;
}
const izinli = (yol, yontem = 'GET') => runtimeRequestAllowed(calisma, yol, yontem);
const finansGorur = () => ['editor', 'viewer'].includes(durum.rol) && izinli('/api/takip/ozet');
const editorMu = () => cashEditingAllowed(durum.rol, calisma);
const bildirimGorur = () => durum.rol === 'editor' && izinli('/api/bildirimler');

// ---------- kabuk ----------
const kok = document.getElementById('uygulama');
const $ = id => document.getElementById(id);
function tost(mesaj, hata = false) {
  const alan = $('tost');
  alan.replaceChildren(
    h('div', { class: `tost${hata ? ' hata' : ''}`, role: hata ? 'alert' : 'status' }, ikon(hata ? 'error' : 'check_circle', 18), mesaj)
  );
  clearTimeout(tost.zaman);
  tost.zaman = setTimeout(() => alan.replaceChildren(), hata ? 6000 : 2400);
}
function suAnkiEkran() {
  return durum.yigin[durum.yigin.length - 1] || { ekran: 'panel' };
}
function kokEkran() {
  return durum.yigin[0]?.ekran || 'panel';
}

function git(ekran, parametre = {}) {
  const hedef = rotaMetni(ekran, parametre);
  if (location.hash === hedef) {
    ciz();
    return;
  }
  durum.icGecis = true;
  location.hash = hedef;
}
function sekmeyeGit(ekran) {
  // Açık sekmeye yeniden dokunmak yalnız başa kaydırır; rapor yeniden istenmez.
  if (durum.yigin.length === 1 && durum.yigin[0].ekran === ekran && (location.hash || '#panel') === rotaMetni(ekran)) {
    window.scrollTo({ top: 0, behavior: 'smooth' });
    return;
  }
  durum.yigin = [{ ekran }];
  git(ekran);
}
// Uygulama içinden açılan ekranda tarayıcı geçmişine dönülür; bildirimden ya da sistem geri hareketiyle
// oluşmuş yığında üst ekrana geçmişe yeni kayıt eklemeden gidilir.
function geri() {
  if (durum.yigin.length > 1 && history.state?.kasa) {
    history.back();
    return;
  }
  const hedef = durum.yigin.length > 1 ? durum.yigin[durum.yigin.length - 2] : { ekran: UST[suAnkiEkran().ekran] || 'panel' };
  durum.yigin = durum.yigin.length > 1 ? durum.yigin.slice(0, -1) : [hedef];
  history.replaceState(null, '', location.pathname + location.search + rotaMetni(hedef.ekran, hedef));
  ciz();
}
function rotaMetni(ekran, p = {}) {
  if (ekran === 'donem' && p.bas) return `#donem/${p.bas}`;
  if (ekran === 'aylik' && p.ay) return `#aylik/${p.ay}`;
  if (ekran === 'islem' && p.id) return `#islem/${p.id}`;
  if (ekran === 'islemler' && p.bas) return `#islemler/${p.bas}~${p.son}`;
  return `#${ekran}`;
}
function rotaOku() {
  const ham = location.hash.replace(/^#\/?/, '');
  const [ad, deger = ''] = ham.split('/');
  if (ad === 'donem' && /^\d{4}-\d{2}-\d{2}$/.test(deger)) return { ekran: 'donem', bas: deger };
  if (ad === 'aylik' && /^\d{4}-\d{2}$/.test(deger)) return { ekran: 'aylik', ay: deger };
  if (ad === 'islemler' && /^\d{4}-\d{2}-\d{2}~\d{4}-\d{2}-\d{2}$/.test(deger)) {
    const [bas, son] = deger.split('~');
    return { ekran: 'islemler', bas, son };
  }
  const r = rotaCoz(location.hash);
  if (r.ekran === 'islem' && r.id) return { ekran: 'islem', id: r.id };
  const bilinen = ['panel', 'haftalik', 'aylik', 'islemler', 'diger', 'trend', 'kartlar', 'krediler', 'bildirimler'];
  // Rolün ya da salt okunur sürümün açamadığı ekranlar (ör. izleyiciye gelen #notifications) panele düşer.
  if (r.ekran === 'bildirimler' && !bildirimGorur()) return { ekran: 'panel', id: null };
  if ((r.ekran === 'kartlar' || r.ekran === 'krediler') && !finansGorur()) return { ekran: 'panel', id: null };
  return { ekran: bilinen.includes(r.ekran) ? r.ekran : 'panel', id: r.id };
}
window.addEventListener('hashchange', () => {
  const ic = durum.icGecis;
  durum.icGecis = false;
  sayfaKapatAnlik();
  if (!durum.rol) return;
  const r = rotaOku();
  const sekme =
    SEKMELER.some(([k]) => k === r.ekran) && !(r.ekran === 'islemler' && r.bas) && !(r.ekran === 'aylik' && r.ay && durum.yigin.length > 1);
  const onceki = durum.yigin[durum.yigin.length - 2];
  if (sekme) durum.yigin = [r];
  else if (onceki && rotaMetni(onceki.ekran, onceki) === rotaMetni(r.ekran, r)) durum.yigin.pop();
  else {
    durum.yigin.push(r);
    history.replaceState(ic ? { kasa: true } : null, '');
  }
  if (!durum.yigin.length) durum.yigin = [r];
  ciz();
});

function baslikCiz(ekran, kokMu) {
  const baslik = BASLIK[ekran] || '';
  const zil = kokMu && ekran === 'panel' && bildirimGorur();
  const arti = ios && kokMu && editorMu() && (ekran === 'panel' || ekran === 'islemler');
  const rozet = durum.okunmamis > 0 ? h('span', { class: 'rozet' }, String(durum.okunmamis)) : null;
  if (ios && kokMu)
    return h(
      'header',
      { class: 'bas-ios-kok' },
      h('h1', {}, baslik),
      h(
        'div',
        { class: 'bas-dugmeler' },
        zil &&
          h(
            'button',
            { type: 'button', class: 'zil-ios', 'aria-label': 'Bildirimler', onclick: () => git('bildirimler') },
            ikon('notifications', 22),
            rozet
          ),
        arti && h('button', { type: 'button', class: 'arti-ios', 'aria-label': 'Hızlı işlem', onclick: hizliIslemAc }, ikon('add', 24))
      )
    );
  if (ios)
    return h(
      'header',
      { class: 'bas-ios-alt' },
      h('button', { type: 'button', class: 'geri-ios', 'aria-label': 'Geri', onclick: geri }, ikon('chevron_left', 28)),
      h('h1', {}, baslik)
    );
  if (kokMu)
    return h(
      'header',
      { class: 'bas-and kok' },
      h('h1', {}, baslik),
      zil &&
        h(
          'button',
          { type: 'button', class: 'dugme-and', 'aria-label': 'Bildirimler', onclick: () => git('bildirimler') },
          ikon('notifications', 24),
          rozet
        )
    );
  return h(
    'header',
    { class: 'bas-and alt' },
    h('button', { type: 'button', class: 'dugme-and', 'aria-label': 'Geri', onclick: geri }, ikon('arrow_back', 24)),
    h('h1', {}, baslik)
  );
}
function sekmeCiz() {
  const etkin = kokEkran();
  const dugmeler = SEKMELER.map(([k, ik, ad]) => {
    const ak = k === etkin;
    return ios
      ? h(
          'button',
          { type: 'button', 'aria-current': ak ? 'page' : null, onclick: () => sekmeyeGit(k) },
          ikon(ik, 24, ak),
          h('span', {}, ad)
        )
      : h(
          'button',
          { type: 'button', 'aria-current': ak ? 'page' : null, onclick: () => sekmeyeGit(k) },
          h('span', { class: 'hap' }, ikon(ik, 24, ak)),
          h('span', { class: 'ad' }, ad)
        );
  });
  return h('nav', { class: ios ? 'sekme-ios' : 'sekme-and', 'aria-label': 'Ana menü' }, dugmeler);
}

async function ciz(korunacakKaydirma = 0) {
  const no = ++durum.ekranNo;
  const ust = suAnkiEkran();
  const kokMu = durum.yigin.length <= 1;
  const icerik = h(
    'main',
    { id: 'ekran', tabindex: '-1' },
    h('div', { class: 'yukleniyor' }, h('span', { class: 'donen', 'aria-hidden': 'true' }), 'Yükleniyor…')
  );
  const fabGoster = !ios && kokMu && editorMu() && (ust.ekran === 'panel' || ust.ekran === 'islemler');
  kok.replaceChildren(
    ...[
      h('div', { class: 'ust-bosluk' }),
      baslikCiz(ust.ekran, kokMu),
      icerik,
      h('div', { class: 'alt-bosluk' }),
      ios && h('div', { class: 'ust-sis' }),
      sekmeCiz(),
      fabGoster && h('button', { type: 'button', class: 'fab', onclick: hizliIslemAc }, ikon('add', 24), 'İşlem'),
    ].filter(Boolean)
  );
  document.title = `${BASLIK[ust.ekran] || 'Kasa'} · Emar Kasa`;
  window.scrollTo(0, 0);
  const guncelMi = () => no === durum.ekranNo;
  try {
    const cizici = EKRANLAR[ust.ekran] || EKRANLAR.panel;
    const dugumler = await cizici(ust, guncelMi);
    if (!guncelMi()) return;
    icerik.replaceChildren(...[dugumler].flat(Infinity).filter(Boolean));
    // Ekran yenilenince eski odak silinir; ekran okuyucu ve klavye yeni içerikten başlasın.
    if (!$('sayfa').childElementCount) icerik.focus({ preventScroll: true });
    if (korunacakKaydirma > 0) window.scrollTo(0, korunacakKaydirma);
  } catch (hata) {
    if (!guncelMi() || !durum.rol) return;
    icerik.replaceChildren(
      h(
        'div',
        { class: 'ekran' },
        h(
          'div',
          { class: 'bos' },
          h('b', {}, 'Bilgiler yüklenemedi'),
          h('span', {}, hata.message),
          h('button', { type: 'button', class: 'dugme yesil-yazi', onclick: () => ciz() }, 'Yeniden dene')
        )
      )
    );
  }
}

// ---------- veri ----------
async function haftalar() {
  const v = await api('/api/rapor/haftalik');
  durum.donemOnbellek = v;
  return v;
}
function acikDonem(liste) {
  return liste.length ? currentPeriod(liste) : null;
}
async function okunmamisGuncelle() {
  if (!bildirimGorur()) {
    durum.okunmamis = 0;
    return;
  }
  try {
    const liste = await api('/api/bildirimler');
    durum.okunmamis = liste.filter(b => !b.okundu).length;
  } catch {}
}
function kartSonOdeme(kart, bugun) {
  // Kesimi geçmiş ve borcu kalan en eski ekstre; kesimi gelmemiş ekstre henüz ödenecek borç değildir.
  const acik = (kart.ekstreler || [])
    .filter(e => e.kalan > 0 && String(e.kesimTarihi) <= bugun)
    .sort((a, b) => String(a.sonOdemeTarihi).localeCompare(String(b.sonOdemeTarihi)))[0];
  if (acik) return acik.sonOdemeTarihi;
  return sonrakiGun(kart.sonOdemeGunu, bugun);
}
function sonrakiGun(gun, bugun) {
  if (!gun) return null;
  const [y, m, d] = bugun.split('-').map(Number);
  const ayGun = (yy, mm) => new Date(yy, mm, 0).getDate();
  let yy = y,
    mm = m;
  if (d > Math.min(gun, ayGun(yy, mm))) {
    mm++;
    if (mm > 12) {
      mm = 1;
      yy++;
    }
  }
  return `${yy}-${String(mm).padStart(2, '0')}-${String(Math.min(gun, ayGun(yy, mm))).padStart(2, '0')}`;
}
function kalanGunCip(tarih, bugun) {
  const g = gunFarki(bugun, tarih);
  const metin = g < 0 ? `${-g} gün geçti` : g === 0 ? 'Bugün' : `${g} gün kaldı`;
  const [zemin, renk] =
    g <= 3 ? ['var(--eksi-zemin)', 'var(--eksi)'] : g <= 7 ? ['var(--amber-zemin)', 'var(--amber)'] : ['var(--ara)', 'var(--soluk2)'];
  return h('span', { class: 'cipcik', style: { background: zemin, color: renk } }, metin);
}

// ---------- ekranlar ----------
const EKRANLAR = {};

EKRANLAR.panel = async () => {
  const bugun = isoGun();
  const [panel, liste] = await Promise.all([api('/api/rapor/panel'), haftalar()]);
  const ozetIstek = finansGorur() ? api('/api/takip/ozet?gun=30').catch(() => null) : Promise.resolve(null);
  const krediIstek = finansGorur() ? api('/api/takip/krediler').catch(() => null) : Promise.resolve(null);
  okunmamisGuncelle().then(() => {
    const r = document.querySelector('.zil-ios, .bas-and .dugme-and[aria-label=Bildirimler]');
    if (!r) return;
    r.querySelector('.rozet')?.remove();
    if (durum.okunmamis > 0) r.append(h('span', { class: 'rozet' }, String(durum.okunmamis)));
  });
  const donem = acikDonem(liste);
  const kanalHafta = new Map((donem?.kanallar || []).map(k => [k.kanal, k]));
  const son12 = liste.slice(-12);
  const degisim = son12.length > 1 && son12[0].kasaDevir !== 0 ? (son12[son12.length - 1].kasaDevir / son12[0].kasaDevir - 1) * 100 : null;
  const ay = bugun.slice(0, 7);
  const [ozet, krediler] = await Promise.all([ozetIstek, krediIstek]);

  const kahraman = h(
    'section',
    { class: 'kahraman' },
    h('div', { class: 'ust-etiket' }, 'Güncel kasa'),
    h('div', { class: 'buyuk' }, tl(panel.guncelKasa)),
    h('div', { class: 'alt' }, `${tamTarih(bugun)} · kasa devri`)
  );
  const ikili = h(
    'div',
    { class: 'ikili' },
    h(
      'button',
      { type: 'button', class: 'mini', onclick: () => sekmeyeGit('haftalik') },
      h('span', { class: 'ust-etiket' }, 'Bu hafta'),
      h(
        'span',
        { class: `deger ${isaretSinifi(panel.buHaftaSonucu)}${boySinifi(imzali(panel.buHaftaSonucu))}` },
        imzali(panel.buHaftaSonucu)
      ),
      h('span', { class: 'not' }, donem ? donemKisa(donem.donem) : '—')
    ),
    h(
      'button',
      {
        type: 'button',
        class: 'mini',
        onclick: () => {
          durum.ay = null;
          sekmeyeGit('aylik');
        },
      },
      h('span', { class: 'ust-etiket' }, 'Bu ay'),
      h('span', { class: `deger ${isaretSinifi(panel.buAySonucu)}${boySinifi(imzali(panel.buAySonucu))}` }, imzali(panel.buAySonucu)),
      h('span', { class: 'not' }, `${AYLAR[Number(ay.slice(5)) - 1]} · açık ay`)
    )
  );
  const kanallar = h(
    'div',
    { class: 'liste' },
    panel.kanallar.map(k => {
      const hafta = kanalHafta.get(k.kanal);
      return h(
        'button',
        {
          type: 'button',
          class: 'satir',
          onclick: () => {
            durum.islemFiltre = { ara: '', kanal: k.kanal, tip: 'Tümü' };
            sekmeyeGit('islemler');
          },
        },
        renkliNokta(k.kanal),
        h(
          'span',
          { class: 'satir-yazi' },
          h('b', {}, k.kanal),
          h('span', { class: 'alt-yazi num' }, hafta ? `Bu hafta ${imzaliYalin(hafta.sonuc)}` : 'Bu hafta kayıt yok')
        ),
        h('span', { class: `num ${isaretSinifi(k.bakiye)}`, style: { fontWeight: 600, whiteSpace: 'nowrap' } }, tl(k.bakiye)),
        okIsareti()
      );
    })
  );
  const bekleyen =
    panel.dagilimBekleyenTutar > 0 &&
    h(
      'div',
      { class: 'kutu', style: { padding: '12px 16px', fontSize: '13px', color: 'var(--soluk)' } },
      h('b', { style: { color: 'var(--amber)' } }, `${tl(panel.dagilimBekleyenTutar)} dağılım bekliyor. `),
      'Genel kasaya yansıdı; alış onaylanınca kanallara dağılır.'
    );
  const trend = son12.length > 1 && [
    etiket('Trend'),
    h(
      'button',
      { type: 'button', class: 'trend-kart', onclick: () => git('trend') },
      h(
        'span',
        { class: 'ust' },
        h('b', { style: { fontWeight: 600 } }, `Kasa devri · ${son12.length} dönem`),
        degisim != null &&
          h(
            'span',
            { class: `num ${isaretSinifi(degisim)}`, style: { fontWeight: 600 } },
            `${degisim >= 0 ? '+' : '−'}%${new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(Math.abs(degisim))}`
          )
      ),
      cizgiGrafik(
        son12.map(w => w.kasaDevir),
        64,
        4
      ),
      h(
        'span',
        { class: 'eksen' },
        h('span', {}, kisaTarih(son12[0].donem.start)),
        h('span', {}, kisaTarih(son12[son12.length - 1].donem.start))
      )
    ),
  ];
  let evrak = null;
  if (ozet) {
    const kartOlay = ozet.olaylar.filter(o => o.kaynak === 'Kart' && o.tur === 'SonOdeme' && o.tutar > 0)[0];
    const krediOlay = ozet.olaylar.filter(o => o.kaynak !== 'Kart')[0];
    const aktifKredi = (krediler || []).filter(k => k.aktif).length;
    evrak = [
      etiket('Kart ve kredi'),
      h(
        'div',
        { class: 'ikili' },
        h(
          'button',
          { type: 'button', class: 'mini', onclick: () => git('kartlar') },
          h('span', { class: 'baslik-ik' }, ikon('credit_card', 18), 'Kartlar'),
          h('span', { class: `deger d16${boySinifi(tl(ozet.kartBorcu))}` }, tl(ozet.kartBorcu)),
          h('span', { class: 'not' }, 'Toplam güncel borç'),
          kartOlay &&
            h(
              'span',
              { class: 'uyari', style: { color: gunFarki(bugun, kartOlay.tarih) < 0 ? 'var(--eksi)' : 'var(--amber)' } },
              sonOdemeMetni(kartOlay.tarih, bugun)
            )
        ),
        h(
          'button',
          { type: 'button', class: 'mini', onclick: () => git('krediler') },
          h('span', { class: 'baslik-ik' }, ikon('savings', 18), 'Krediler'),
          h('span', { class: `deger d16${boySinifi(tl(ozet.kalanKrediPlani))}` }, tl(ozet.kalanKrediPlani)),
          h('span', { class: 'not' }, aktifKredi ? `Kalan plan · ${aktifKredi} kredi` : 'Kalan planlı ödeme'),
          krediOlay &&
            h('span', { class: 'uyari', style: { color: 'var(--amber)' } }, `Taksit ${kisaTarih(krediOlay.tarih)} · ${tl(krediOlay.tutar)}`)
        )
      ),
    ];
  }
  return h('div', { class: 'ekran' }, kahraman, ikili, bekleyen, etiket('Kanal bakiyeleri'), kanallar, trend, evrak);
};
function sonOdemeMetni(tarih, bugun) {
  const g = gunFarki(bugun, tarih);
  return g < 0 ? `Son ödeme ${-g} gün geçti` : g === 0 ? 'Son ödeme bugün' : `Son ödeme ${g} gün sonra`;
}

function haftaSatirlari(w) {
  const kanalGiden = w.kanallar.reduce((t, k) => t + Math.round(k.giden * 100), 0) / 100;
  const kanalGelen = w.kanallar.reduce((t, k) => t + Math.round(k.gelen * 100), 0) / 100;
  return {
    disiGider: (Math.round(w.toplamGiden * 100) - Math.round(kanalGiden * 100)) / 100,
    genelGelir: (Math.round(w.toplamGelen * 100) - Math.round(kanalGelen * 100)) / 100,
  };
}
EKRANLAR.haftalik = async () => {
  const liste = await haftalar();
  if (!liste.length) return h('div', { class: 'ekran' }, h('div', { class: 'bos' }, 'Henüz kasa dönemi yok.'));
  const acik = acikDonem(liste);
  const ters = [...liste].reverse();
  // Aç/kapa yalnız o kutuyu yeniden çizer; rapor yeniden istenmez, sayfa kaydırması korunur.
  const kutu = (w, j) => {
    const devam = w === acik;
    const acikMi = durum.acikHafta === j;
    const { disiGider } = haftaSatirlari(w);
    const ic =
      acikMi &&
      h(
        'div',
        { class: 'hafta-ic' },
        w.kanallar.map(k =>
          h(
            'div',
            {},
            h(
              'div',
              { class: 'kanal-bas' },
              renkliNokta(k.kanal, 8),
              h('b', {}, k.kanal),
              h('span', { class: `num ${isaretSinifi(k.sonuc)}`, style: { fontWeight: 600, fontSize: '14px' } }, imzali(k.sonuc))
            ),
            h('div', { class: 'girinti' }, `Gelen ${f2(k.gelen)} · Giden ${f2(k.giden)}`),
            h('div', { class: 'girinti' }, `Kanal devri ${tl(k.devir)}`)
          )
        ),
        h('div', { class: 'ortak' }, h('span', {}, 'Kanal dışı giderler'), h('span', { class: 'num' }, imzali(-disiGider))),
        h('button', { type: 'button', onclick: () => git('donem', { bas: w.donem.start }) }, 'Dönem ayrıntısı', ikon('chevron_right', 20))
      );
    const el = h(
      'div',
      { class: 'hafta' },
      h(
        'button',
        {
          type: 'button',
          'aria-expanded': acikMi ? 'true' : 'false',
          onclick: () => {
            const onceki = durum.acikHafta;
            durum.acikHafta = acikMi ? -1 : j;
            if (onceki >= 0 && onceki !== j && kutular[onceki]) {
              const yeni = kutu(ters[onceki], onceki);
              kutular[onceki].replaceWith(yeni);
              kutular[onceki] = yeni;
            }
            const yeni = kutu(w, j);
            kutular[j].replaceWith(yeni);
            kutular[j] = yeni;
            yeni.querySelector('button')?.focus({ preventScroll: true });
          },
        },
        h(
          'span',
          { class: 'satir-yazi', style: { gap: '2px' } },
          h(
            'span',
            { class: 'hafta-ad' },
            h('b', {}, donemAdi(w.donem)),
            devam && h('span', { class: 'etiketcik devam' }, 'Devam ediyor'),
            bolunmusMu(w.donem) && h('span', { class: 'etiketcik bol' }, 'Bölünmüş')
          ),
          h('span', { class: 'alt-yazi num' }, `Kasa devri ${tl(w.kasaDevir)}`)
        ),
        h('span', { class: `num ${isaretSinifi(w.kasaSonucu)}`, style: { fontWeight: 600, whiteSpace: 'nowrap' } }, imzali(w.kasaSonucu)),
        h('span', { class: 'ok' }, ikon(acikMi ? 'keyboard_arrow_up' : 'keyboard_arrow_down', 22))
      ),
      ic
    );
    return el;
  };
  const kutular = ters.map(kutu);
  return h(
    'div',
    { class: 'ekran s10' },
    h('div', { class: 'aciklama' }, 'Haftalar pazartesi başlar; ay sonunda hafta ikiye bölünür.'),
    kutular
  );
};

EKRANLAR.donem = async ekran => {
  const liste = durum.donemOnbellek.length ? durum.donemOnbellek : await haftalar();
  const w = liste.find(x => x.donem.start === ekran.bas) || acikDonem(liste);
  if (!w) return h('div', { class: 'ekran' }, h('div', { class: 'bos' }, 'Dönem bulunamadı.'));
  const { disiGider, genelGelir } = haftaSatirlari(w);
  const satirlar = [
    ['Toplam gelen', tl(w.toplamGelen)],
    ['Toplam giden', tl(w.toplamGiden)],
    ['Kanal dışı giderler', tl(disiGider)],
  ];
  if (genelGelir) satirlar.push(['Yalnız genel kasa geliri', tl(genelGelir)]);
  if (w.dagilimBekleyenTutar > 0) satirlar.push(['Dağılım bekleyen', tl(w.dagilimBekleyenTutar)]);
  return h(
    'div',
    { class: 'ekran' },
    h(
      'div',
      { style: { display: 'flex', flexDirection: 'column', gap: '2px', padding: '0 2px' } },
      h('div', { style: { fontSize: '22px', fontWeight: 700 } }, donemAdi(w.donem)),
      h(
        'div',
        { class: 'alt-yazi', style: { fontSize: '13px' } },
        bolunmusMu(w.donem)
          ? 'Ay sonunda bölünmüş dönem'
          : w === acikDonem(liste) && w.donem.end === isoGun()
            ? 'Devam eden dönem · bugüne kadar'
            : 'Pazartesi–pazar dönemi'
      )
    ),
    h(
      'div',
      { class: 'ikili' },
      h(
        'div',
        { class: 'mini' },
        h('span', { class: 'ust-etiket' }, 'Kasa sonucu'),
        h('span', { class: `deger d17 ${isaretSinifi(w.kasaSonucu)}${boySinifi(imzali(w.kasaSonucu))}` }, imzali(w.kasaSonucu))
      ),
      h(
        'div',
        { class: 'mini koyu' },
        h('span', { class: 'ust-etiket' }, 'Kasa devri'),
        h('span', { class: `deger d17${boySinifi(tl(w.kasaDevir))}` }, tl(w.kasaDevir))
      )
    ),
    h(
      'div',
      { class: 'kutu', style: { padding: '8px 16px' } },
      satirlar.map(([k, v]) => h('div', { class: 'kv', style: { padding: '5px 0' } }, h('span', {}, k), h('span', {}, v)))
    ),
    w.kanallar.map(k =>
      h(
        'div',
        { class: 'kart-kutu' },
        h(
          'div',
          { style: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '4px' } },
          kanalRozet(k.kanal),
          h('span', { class: `num ${isaretSinifi(k.sonuc)}`, style: { fontWeight: 700 } }, imzali(k.sonuc))
        ),
        [
          ['Gelen', tl(k.gelen)],
          ['Giden', tl(k.giden)],
          ['Kanal devri', tl(k.devir)],
        ].map(([a, b]) => h('div', { class: 'kv' }, h('span', {}, a), h('span', {}, b)))
      )
    ),
    h(
      'button',
      {
        type: 'button',
        class: 'dugme',
        onclick: () => {
          durum.islemFiltre = { ara: '', kanal: 'Tümü', tip: 'Tümü' };
          git('islemler', { bas: w.donem.start, son: w.donem.end });
        },
      },
      'İşlemleri gör'
    )
  );
};

async function ayKilidi() {
  if (!izinli('/api/ay-kilidi') || !['editor', 'viewer'].includes(durum.rol)) return null;
  try {
    return await api('/api/ay-kilidi');
  } catch {
    return null;
  }
}
EKRANLAR.aylik = async ekran => {
  const buAy = isoGun().slice(0, 7);
  const liste = durum.donemOnbellek.length ? durum.donemOnbellek : await haftalar().catch(() => []);
  const ilkAy = liste.length ? liste[0].donem.start.slice(0, 7) : buAy;
  const ay = ekran.ay || durum.ay || buAy;
  durum.ay = ay;
  const [yil, ayNo] = ay.split('-').map(Number);
  const [rapor, kilit] = await Promise.all([api(`/api/rapor/aylik?yil=${yil}&ay=${ayNo}`), ayKilidi()]);
  const onceki = ayKaydir(ay, -1),
    sonraki = ayKaydir(ay, 1);
  const kilitli = kilit?.kilitliSonTarih && `${ay}-01` <= kilit.kilitliSonTarih;
  const durumMetni = kilitli
    ? ['lock', 'Kilitli · kesinleşti', 'var(--yesil)']
    : ay === buAy
      ? ['edit_calendar', 'Açık ay · rakamlar değişebilir', 'var(--amber)']
      : ['edit_calendar', kilit ? 'Kilitlenmedi · rakamlar değişebilir' : 'Rakamlar değişebilir', 'var(--amber)'];
  const toplam = monthlyTotals(rapor);
  const veriVar = ay >= ilkAy && ay <= buAy;
  const gezgin = h(
    'div',
    { class: 'ay-gezgin' },
    h(
      'button',
      { type: 'button', class: 'yuvarlak', 'aria-label': 'Önceki ay', disabled: onceki < ilkAy, onclick: () => ayaGit(onceki) },
      ikon('chevron_left', 24)
    ),
    h(
      'div',
      { class: 'orta' },
      h('b', {}, `${AYLAR[ayNo - 1]} ${yil}`),
      veriVar && h('span', { class: 'ay-durum', style: { color: durumMetni[2] } }, ikon(durumMetni[0], 14), durumMetni[1])
    ),
    h(
      'button',
      { type: 'button', class: 'yuvarlak', 'aria-label': 'Sonraki ay', disabled: sonraki > buAy, onclick: () => ayaGit(sonraki) },
      ikon('chevron_right', 24)
    )
  );
  if (!veriVar)
    return h(
      'div',
      { class: 'ekran' },
      gezgin,
      h(
        'div',
        { class: 'bos' },
        `Takip ${ilkAy.slice(5) ? AYLAR[Number(ilkAy.slice(5)) - 1] + ' ' + ilkAy.slice(0, 4) : ''} ayında başladı. Bu ay için kayıt yok.`
      )
    );
  const ekSatirlar = [];
  if (rapor.genelGelir > 0) ekSatirlar.push(['Yalnız genel kasa geliri', tl(rapor.genelGelir)]);
  if (rapor.genelGider > 0) ekSatirlar.push(['Yalnız genel kasa gideri', tl(rapor.genelGider)]);
  if (rapor.dagilimBekleyenTutar > 0) ekSatirlar.push(['Dağılım bekleyen', tl(rapor.dagilimBekleyenTutar)]);
  return h(
    'div',
    { class: 'ekran' },
    gezgin,
    h(
      'section',
      { class: 'kahraman', style: { padding: '18px 20px' } },
      h('div', { class: 'ust-etiket' }, 'Ay sonucu · tüm kanallar'),
      h('div', { class: 'buyuk', style: { fontSize: '30px' } }, imzali(toplam.result)),
      h('div', { class: 'alt' }, `Gelen ${tl(toplam.incoming)} · Gider ${tl(toplam.expenses)}`)
    ),
    rapor.kanallar.map(k =>
      h(
        'div',
        { class: 'kart-kutu' },
        h(
          'div',
          { style: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '4px' } },
          kanalRozet(k.kanal),
          h('span', { class: `num ${isaretSinifi(k.aySonucu)}`, style: { fontSize: '16px', fontWeight: 700 } }, imzali(k.aySonucu))
        ),
        [
          ['Gelen', k.gelen],
          ['Diğer gider', k.cariGiden],
          ['Sabit gider', k.sabitGider],
          ['Kredi kartı', k.krediKarti],
          ['Ortak pay', k.ortakPay],
        ].map(([a, b]) => h('div', { class: 'kv' }, h('span', {}, a), h('span', {}, tl(b))))
      )
    ),
    ekSatirlar.length > 0 &&
      h(
        'div',
        { class: 'kutu', style: { padding: '8px 16px' } },
        ekSatirlar.map(([a, b]) => h('div', { class: 'kv' }, h('span', {}, a), h('span', {}, b)))
      )
  );
};
function ayKaydir(ay, fark) {
  const [y, m] = ay.split('-').map(Number);
  const d = new Date(y, m - 1 + fark, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}
function ayaGit(ay) {
  durum.ay = ay;
  if (durum.yigin.length <= 1) {
    durum.yigin = [{ ekran: 'aylik', ay }];
    history.replaceState(null, '', rotaMetni('aylik', { ay }));
    ciz();
  } else git('aylik', { ay });
}

EKRANLAR.trend = async (ekran, guncelMi) => {
  const liste = await haftalar();
  const son = liste.slice(-12);
  const segment = h(
    'div',
    { class: 'segment', role: 'group', 'aria-label': 'Trend türü' },
    [
      ['kasa', 'Kasa'],
      ['kanal', 'Kanallar'],
    ].map(([k, a]) =>
      h(
        'button',
        {
          type: 'button',
          'aria-pressed': durum.trendSegment === k ? 'true' : 'false',
          onclick: () => {
            durum.trendSegment = k;
            ciz();
          },
        },
        a
      )
    )
  );
  if (!son.length) return h('div', { class: 'ekran' }, segment, h('div', { class: 'bos' }, 'Henüz kasa dönemi yok.'));
  if (durum.trendSegment === 'kanal') {
    const buAy = isoGun().slice(0, 7),
      ilkAy = liste[0].donem.start.slice(0, 7);
    const aylar = [5, 4, 3, 2, 1, 0].map(i => ayKaydir(buAy, -i)).filter(a => a >= ilkAy);
    // Rapor okumaları sunucuda yazma kilidi alır; ayları sırayla iste, ekrandan çıkılınca dur,
    // bir dakika içinde yeniden açılırsa aynı sonuçları kullan.
    const raporlar = [];
    for (const a of aylar) {
      const kayit = durum.ayOnbellek.get(a);
      if (kayit && Date.now() - kayit.zaman < 60000) {
        raporlar.push(kayit.rapor);
        continue;
      }
      const [y, m] = a.split('-').map(Number);
      const rapor = await api(`/api/rapor/aylik?yil=${y}&ay=${m}`);
      if (!guncelMi()) return null;
      durum.ayOnbellek.set(a, { zaman: Date.now(), rapor });
      raporlar.push(rapor);
    }
    const kanalAdlari = [...new Set(raporlar.flatMap(r => r.kanallar.map(k => k.kanal)))];
    const degerler = raporlar.map(r => kanalAdlari.map(ad => r.kanallar.find(k => k.kanal === ad)?.aySonucu || 0));
    const olcek = cubukOlcek(degerler.flat());
    const ust = olcek.ust,
      alt = olcek.alt;
    const tepe = Math.max(0, ...degerler.flat()),
      dip = Math.max(0, ...degerler.flat().map(x => -x));
    const sutunlar = aylar.map((a, i) =>
      h(
        'div',
        { style: { flex: 1, display: 'flex', flexDirection: 'column', gap: '4px' } },
        h(
          'div',
          { style: { flex: 1, display: 'flex', flexDirection: 'column' } },
          h(
            'div',
            { style: { height: `${ust}%`, display: 'flex', alignItems: 'flex-end', gap: '2px' } },
            degerler[i].map((v, j) =>
              h('div', {
                style: {
                  flex: 1,
                  height: v > 0 ? `${Math.max(2, (v / tepe) * 100)}%` : '0',
                  background: kanalRengi(kanalAdlari[j]).r,
                  borderRadius: '2px 2px 0 0',
                },
              })
            )
          ),
          h('div', { style: { height: '1px', background: 'var(--kesik)', flex: 'none' } }),
          h(
            'div',
            { style: { height: `${alt}%`, display: 'flex', alignItems: 'flex-start', gap: '2px' } },
            degerler[i].map((v, j) =>
              h('div', {
                style: {
                  flex: 1,
                  height: v < 0 ? `${Math.max(2, (-v / dip) * 100)}%` : '0',
                  background: kanalRengi(kanalAdlari[j]).r,
                  opacity: '.6',
                  borderRadius: '0 0 2px 2px',
                },
              })
            )
          )
        ),
        h(
          'div',
          { style: { textAlign: 'center', fontSize: '11px', color: 'var(--soluk)', fontWeight: a === buAy ? 700 : 500 } },
          AYK[Number(a.slice(5)) - 1]
        )
      )
    );
    const ilk = aylar[0],
      sonAy = aylar[aylar.length - 1];
    return h(
      'div',
      { class: 'ekran' },
      segment,
      h(
        'div',
        { class: 'kart-kutu', style: { gap: '10px' } },
        h(
          'div',
          { style: { display: 'flex', flexDirection: 'column' } },
          h('b', { style: { fontWeight: 600 } }, 'Kanal ay sonucu'),
          h(
            'span',
            { class: 'alt-yazi' },
            `${AYLAR[Number(ilk.slice(5)) - 1]} – ${AYLAR[Number(sonAy.slice(5)) - 1]} ${sonAy.slice(0, 4)} · ${AYLAR[Number(buAy.slice(5)) - 1]} devam ediyor`
          )
        ),
        h(
          'div',
          { style: { display: 'flex', gap: '12px', flexWrap: 'wrap' } },
          kanalAdlari.map(ad =>
            h(
              'span',
              { style: { display: 'flex', alignItems: 'center', gap: '6px', fontSize: '12px', fontWeight: 600 } },
              h('span', { style: { width: '10px', height: '10px', borderRadius: '3px', background: kanalRengi(ad).r } }),
              ad
            )
          )
        ),
        h('div', { style: { display: 'flex', gap: '10px', height: '170px' } }, sutunlar),
        dip > 0 && h('div', { class: 'alt-yazi', style: { fontSize: '12px' } }, 'Çizginin altı zarar.')
      )
    );
  }
  const devirler = son.map(w => w.kasaDevir);
  return h(
    'div',
    { class: 'ekran' },
    segment,
    h(
      'div',
      { class: 'kart-kutu', style: { gap: '6px' } },
      h('b', { style: { fontWeight: 600 } }, `Kasa devri · son ${son.length} dönem`),
      h(
        'div',
        { class: 'eksen num' },
        h('span', {}, `En yüksek ${tl(Math.max(...devirler))}`),
        h('span', {}, `En düşük ${tl(Math.min(...devirler))}`)
      ),
      cizgiGrafik(devirler, 140, 8),
      h(
        'div',
        { class: 'eksen' },
        h('span', {}, kisaTarih(son[0].donem.start)),
        son.length > 2 && h('span', {}, kisaTarih(son[Math.floor(son.length / 2)].donem.start)),
        h('span', {}, kisaTarih(son[son.length - 1].donem.start))
      )
    ),
    h(
      'div',
      { class: 'liste' },
      [...son]
        .reverse()
        .slice(0, 6)
        .map(w =>
          h(
            'div',
            { class: 'satir', style: { justifyContent: 'space-between', padding: '10px 16px' } },
            h(
              'span',
              { class: 'satir-yazi' },
              h('span', { style: { fontSize: '14px', fontWeight: 600 } }, donemAdi(w.donem)),
              h('span', { class: `num ${isaretSinifi(w.kasaSonucu)}`, style: { fontSize: '12px' } }, imzali(w.kasaSonucu))
            ),
            h('span', { class: 'num', style: { fontWeight: 600 } }, tl(w.kasaDevir))
          )
        )
    )
  );
};

function islemAraligi(ekran) {
  if (ekran.bas && ekran.son) return { bas: ekran.bas, son: ekran.son, sabit: true };
  const bugun = isoGun();
  return { bas: durum.islemBaslangic || ayKaydir(bugun.slice(0, 7), -1) + '-01', son: bugun, sabit: false };
}
async function islemleriYukle(aralik) {
  const q = new URLSearchParams({ baslangic: aralik.bas, bitis: aralik.son });
  const liste = await api(`/api/islemler?${q}`);
  const harita = new Map(durum.islemOnbellek.map(i => [i.id, i]));
  for (const i of liste) harita.set(i.id, i);
  durum.islemOnbellek = [...harita.values()];
  return liste;
}
EKRANLAR.islemler = async ekran => {
  const aralik = islemAraligi(ekran);
  const [liste, kanalListe] = await Promise.all([islemleriYukle(aralik), api('/api/kanallar').catch(() => [])]);
  const f = durum.islemFiltre;
  const kanalAdlari = ['Tümü', ...kanalListe.map(k => k.ad), ORTAK];
  if (liste.some(i => i.kanal === 'Dağılım bekliyor')) kanalAdlari.push('Dağılım bekliyor');
  if (!kanalAdlari.includes(f.kanal)) f.kanal = 'Tümü';
  const liste_ = h('div', { style: { display: 'flex', flexDirection: 'column', gap: '10px' } });
  const ozet = h('div', { class: 'alt-yazi num', style: { padding: '0 2px' } });
  const aramaGirdi = h('input', {
    type: 'search',
    value: f.ara,
    placeholder: 'Açıklama ya da not ara',
    'aria-label': 'İşlemlerde ara',
    enterkeyhint: 'search',
  });
  const temizle = h(
    'button',
    {
      type: 'button',
      'aria-label': 'Temizle',
      hidden: !f.ara,
      onclick: () => {
        f.ara = '';
        aramaGirdi.value = '';
        temizle.hidden = true;
        ciz_();
      },
    },
    ikon('cancel', 20)
  );
  aramaGirdi.addEventListener('input', () => {
    f.ara = aramaGirdi.value;
    temizle.hidden = !f.ara;
    ciz_();
  });
  // Filtreler yalnız eldeki listeyi süzer; sunucudan yeniden okumaz.
  const cipDugmeleri = [];
  const cipSatiri = (secenekler, anahtar, ad) =>
    h(
      'div',
      { class: 'cipler', role: 'group', 'aria-label': ad },
      secenekler.map(([deger, metin, nokta]) => {
        const dugme = h(
          'button',
          {
            type: 'button',
            class: 'cip',
            onclick: () => {
              f[anahtar] = deger;
              ciz_();
            },
          },
          nokta && h('span', { class: 'nokta8', style: { background: kanalRengi(deger).r } }),
          metin
        );
        cipDugmeleri.push([dugme, anahtar, deger]);
        return dugme;
      })
    );
  const filtreleriTemizle = () => {
    Object.assign(f, { ara: '', kanal: 'Tümü', tip: 'Tümü' });
    aramaGirdi.value = '';
    temizle.hidden = true;
    ciz_();
  };
  const kanalNotu = h(
    'div',
    { class: 'aciklama', hidden: true },
    'Birden çok kanala bölünmüş ödemeler tam tutarıyla listelenir; toplam, kanalın payı değildir.'
  );
  const ciz_ = () => {
    for (const [dugme, anahtar, deger] of cipDugmeleri) dugme.setAttribute('aria-pressed', f[anahtar] === deger ? 'true' : 'false');
    kanalNotu.hidden = f.kanal === 'Tümü' || !liste.some(i => islemEslesir(i, { kanal: f.kanal }) && i.kanal !== f.kanal);
    const secili = liste.filter(i => islemEslesir(i, f));
    const toplam = secili.reduce((t, i) => t + Math.round(i.tutarTl * 100), 0) / 100;
    ozet.textContent = `${secili.length} işlem · toplam ${tl(toplam)} · ${kisaTarih(aralik.bas)} – ${kisaTarih(aralik.son)}`;
    const gruplar = gunlereAyir(secili);
    liste_.replaceChildren(
      ...(gruplar.length
        ? gruplar.map(g =>
            h(
              'div',
              { style: { display: 'flex', flexDirection: 'column', gap: '6px' } },
              h('div', { class: 'gun-bas' }, h('span', {}, uzunTarih(g.tarih)), h('span', { class: 'num' }, imzali(-g.toplam))),
              h(
                'div',
                { class: 'liste' },
                g.islemler.map(i =>
                  h(
                    'button',
                    { type: 'button', class: 'satir islem-satir', onclick: () => git('islem', { id: i.id }) },
                    h(
                      'span',
                      { class: 'satir-yazi', style: { gap: '3px' } },
                      h('span', { class: 'ust-ad' }, i.cari),
                      h(
                        'span',
                        { class: 'bilgi' },
                        kanalRozet(i.kanal, true),
                        h('span', {}, tipAdi(i.tip)),
                        i.alisId && h('span', {}, `Alış #${i.alisId}`)
                      )
                    ),
                    h('span', { class: 'tutar' }, imzali(-i.tutarTl)),
                    okIsareti()
                  )
                )
              )
            )
          )
        : [
            h(
              'div',
              { class: 'bos', style: { padding: '36px 16px' } },
              h('span', {}, liste.length ? 'Aramaya uyan işlem yok.' : 'Bu aralıkta işlem yok.'),
              liste.length > 0 &&
                h('button', { type: 'button', class: 'dugme yesil-yazi kucuk', onclick: filtreleriTemizle }, 'Filtreleri temizle')
            ),
          ])
    );
  };
  const kanalCipleri = cipSatiri(
    kanalAdlari.map(k => [k, k, k !== 'Tümü']),
    'kanal',
    'Kanal'
  );
  const tipCipleri = cipSatiri(
    [
      ['Tümü', 'Tüm tipler'],
      ['Cari', tipAdi('Cari')],
      ['SabitGider', tipAdi('SabitGider')],
      ['KrediKarti', tipAdi('KrediKarti')],
    ],
    'tip',
    'Gider tipi'
  );
  ciz_();
  const dahaEski =
    !aralik.sabit &&
    h(
      'button',
      {
        type: 'button',
        class: 'dugme kucuk daha',
        onclick: () => {
          const kaydirma = window.scrollY;
          durum.islemBaslangic = ayKaydir(aralik.bas.slice(0, 7), -1) + '-01';
          ciz(kaydirma);
        },
      },
      'Daha eski işlemleri yükle'
    );
  return h(
    'div',
    { class: 'ekran s10' },
    h('label', { class: 'arama' }, ikon('search', 20), aramaGirdi, temizle),
    kanalCipleri,
    tipCipleri,
    ozet,
    kanalNotu,
    liste_,
    dahaEski
  );
};

EKRANLAR.islem = async ekran => {
  let i = durum.islemOnbellek.find(x => x.id === ekran.id);
  if (!i) {
    await islemleriYukle({ bas: ayKaydir(isoGun().slice(0, 7), -3) + '-01', son: isoGun() });
    i = durum.islemOnbellek.find(x => x.id === ekran.id);
  }
  if (!i)
    return h(
      'div',
      { class: 'ekran' },
      h(
        'div',
        { class: 'bos' },
        'Bu işlem son üç ayın kayıtlarında bulunamadı.',
        h('button', { type: 'button', class: 'dugme kucuk', onclick: () => sekmeyeGit('islemler') }, 'İşlemlere dön')
      )
    );
  let kartAdi = null;
  if (i.krediKartiId) {
    try {
      kartAdi = (await api('/api/kredikartlari')).find(k => k.id === i.krediKartiId)?.ad || null;
    } catch {}
  }
  const kaynak = i.alisId
    ? `Alış #${i.alisId}`
    : i.aylikGiderOdemeId
      ? 'Aylık gider ödemesi'
      : i.ekstreKayitId
        ? 'Ekstre / hareket yüklemesi'
        : null;
  const satirlar = [
    ['Tip', tipAdi(i.tip)],
    i.krediKartiId ? ['Kart', kartAdi || `Kart #${i.krediKartiId}`] : null,
    ['Not', i.not || '—'],
    kaynak ? ['Kaynak', kaynak] : null,
  ].filter(Boolean);
  const not = i.alisId
    ? 'Bu gider bir alışa bağlı. Kanal dağılımı ve ödeme masaüstü görünümündeki Alışlar ekranından düzeltilir.'
    : i.aylikGiderOdemeId
      ? 'Masaüstü görünümündeki Aylık Giderler bölümünden yönetilir.'
      : i.ekstreKayitId
        ? 'Masaüstü görünümündeki Ekstre / Hareket Yükle bölümünden yönetilir.'
        : editorMu()
          ? 'Düzeltme ve silme masaüstü görünümündeki İşlemler ekranından yapılır.'
          : null;
  return h(
    'div',
    { class: 'ekran' },
    h(
      'div',
      { class: 'islem-bas' },
      kanalRozet(i.kanal),
      h('div', { class: 'tutar' }, imzali(-i.tutarTl)),
      h('div', { class: 'cari' }, i.cari),
      h('div', { class: 'tarih' }, tamTarih(i.tarih))
    ),
    h(
      'div',
      { class: 'bilgi-kutu' },
      satirlar.map(([k, v]) => h('div', { class: 'kv' }, h('span', {}, k), h('span', {}, v)))
    ),
    not && h('div', { class: 'aciklama' }, not)
  );
};

EKRANLAR.kartlar = async () => {
  const bugun = isoGun();
  const kartlar = (await api('/api/takip/kartlar')).filter(k => k.aktif || k.borc > 0);
  const toplam = kartlar.reduce((t, k) => t + Math.round(Math.max(0, k.borc) * 100), 0) / 100;
  const sonOdemeler = kartlar.map(k => ({ k, son: kartSonOdeme(k, bugun) })).filter(x => x.son);
  const enYakin = sonOdemeler.map(x => x.son).sort()[0];
  if (!kartlar.length) return h('div', { class: 'ekran' }, h('div', { class: 'bos' }, 'Henüz kart eklenmedi.'));
  return h(
    'div',
    { class: 'ekran' },
    h(
      'section',
      { class: 'kahraman', style: { padding: '18px 20px' } },
      h('div', { class: 'ust-etiket' }, 'Toplam güncel borç'),
      h('div', { class: 'buyuk', style: { fontSize: '30px' } }, tl(toplam)),
      h('div', { class: 'alt' }, `${kartlar.length} kart${enYakin ? ` · en yakın son ödeme ${kisaTarih(enYakin)}` : ''}`)
    ),
    sonOdemeler.concat(kartlar.filter(k => !kartSonOdeme(k, bugun)).map(k => ({ k, son: null }))).map(({ k, son }) => {
      const kullanim = k.limit > 0 ? (Math.max(0, k.borc) / k.limit) * 100 : 0;
      const sonEkstre = [...(k.ekstreler || [])].sort((a, b) => String(b.kesimTarihi).localeCompare(String(a.kesimTarihi)))[0];
      return h(
        'div',
        { class: 'kart-kutu' },
        h(
          'div',
          { class: 'bas' },
          h('b', {}, k.ad),
          h(
            'span',
            { class: 'num', style: { fontWeight: 700, whiteSpace: 'nowrap', color: k.borc > 0 ? 'var(--eksi)' : 'var(--arti)' } },
            k.borc < 0 ? `+${tl(-k.borc)}` : tl(k.borc)
          )
        ),
        k.limit > 0 &&
          h(
            'div',
            { class: 'cubuk' },
            h('i', { style: { width: `${Math.min(100, kullanim)}%`, background: kullanim > 85 ? 'var(--eksi)' : 'var(--yesil)' } })
          ),
        k.limit > 0 &&
          h('div', { class: 'limit' }, h('span', {}, `%${Math.round(kullanim)} kullanıldı`), h('span', {}, `Limit ${tl(k.limit)}`)),
        [
          ['Ekstre borcu', tl(k.ekstreBorc)],
          k.limit > 0 ? ['Kalan limit', tl(k.limit - Math.max(0, k.borc))] : null,
          sonEkstre
            ? ['Hesap kesim', kisaTarih(sonEkstre.kesimTarihi)]
            : k.kesimGunu
              ? ['Hesap kesim', `Her ayın ${k.kesimGunu}. günü`]
              : null,
        ]
          .filter(Boolean)
          .map(([a, b]) => h('div', { class: 'kv' }, h('span', {}, a), h('span', {}, b))),
        son &&
          h(
            'div',
            { class: 'kv', style: { alignItems: 'center' } },
            h('span', {}, 'Son ödeme'),
            h('span', { style: { display: 'flex', alignItems: 'center', gap: '6px' } }, kisaTarih(son), kalanGunCip(son, bugun))
          ),
        !k.yeniTakip && h('div', { class: 'alt-yazi', style: { marginTop: '6px' } }, 'Eski kasa kuralıyla izleniyor.')
      );
    }),
    h('div', { class: 'aciklama' }, 'Son ödeme günü kasayı değiştirmez. Kasa, kart ödemesi kaydedildiğinde düşer.')
  );
};

EKRANLAR.krediler = async () => {
  const bugun = isoGun();
  const krediler = await api('/api/takip/krediler');
  if (!krediler.length) return h('div', { class: 'ekran' }, h('div', { class: 'bos' }, 'Henüz kredi eklenmedi.'));
  const toplam = krediler.reduce((t, k) => t + Math.round(k.kalanPlanliOdeme * 100), 0) / 100;
  return h(
    'div',
    { class: 'ekran' },
    h(
      'section',
      { class: 'kahraman', style: { padding: '18px 20px' } },
      h('div', { class: 'ust-etiket' }, 'Kalan planlı ödeme'),
      h('div', { class: 'buyuk', style: { fontSize: '30px' } }, tl(toplam)),
      h('div', { class: 'alt' }, `${krediler.filter(k => k.aktif).length} aktif kredi`)
    ),
    krediler.map(k => {
      const bekleyen = (k.taksitler || [])
        .filter(t => t.durum === 'Bekliyor')
        .sort((a, b) => String(a.tarih).localeCompare(String(b.tarih)));
      const sonraki = bekleyen[0];
      const islenen = (k.taksitler || []).filter(t => t.durum === 'KasayaIslendi').length;
      const toplamTaksit = (k.taksitler || []).filter(t => t.durum !== 'Iptal').length;
      const oran = toplamTaksit ? (islenen / toplamTaksit) * 100 : 0;
      return h(
        'div',
        { class: 'kart-kutu' },
        h(
          'div',
          { class: 'bas' },
          h('b', {}, k.ad),
          h('span', { class: 'num', style: { fontWeight: 700, whiteSpace: 'nowrap' } }, tl(k.kalanPlanliOdeme))
        ),
        toplamTaksit > 0 && h('div', { class: 'cubuk' }, h('i', { style: { width: `${oran}%`, background: 'var(--yesil)' } })),
        toplamTaksit > 0 &&
          h(
            'div',
            { class: 'limit' },
            h('span', {}, `${islenen} / ${toplamTaksit} taksit işlendi`),
            h('span', {}, k.aktif ? 'Aktif' : 'Arşivde')
          ),
        [
          ['Çekilen tutar', tl(k.cekilenTutar)],
          ['Çekim tarihi', kisaTarih(k.cekimTarihi)],
          ['Kanallar', (k.kanalPaylari || []).map(p => p.kanal).join(', ') || '—'],
        ].map(([a, b]) => h('div', { class: 'kv' }, h('span', {}, a), h('span', {}, b))),
        sonraki && h('div', { class: 'kv' }, h('span', {}, 'Sonraki taksit'), h('span', {}, tl(sonraki.tutar))),
        sonraki &&
          h(
            'div',
            { class: 'kv', style: { alignItems: 'center' } },
            h('span', {}, 'Taksit günü'),
            h(
              'span',
              { style: { display: 'flex', alignItems: 'center', gap: '6px' } },
              kisaTarih(sonraki.tarih),
              kalanGunCip(sonraki.tarih, bugun)
            )
          )
      );
    }),
    h('div', { class: 'aciklama' }, 'Taksitler kendi tarihinde kasadan otomatik düşer. Bu, bankadan ödeme doğrulaması değildir.')
  );
};

function bildirimHedefi(hedef) {
  try {
    const r = rotaCoz(new URL(hedef, location.origin).hash);
    return r;
  } catch {
    return null;
  }
}
EKRANLAR.bildirimler = async () => {
  const liste = await api('/api/bildirimler');
  durum.okunmamis = liste.filter(b => !b.okundu).length;
  const okundu = async b => {
    if (!b.okundu) {
      await api(`/api/bildirimler/${b.id}/okundu`, { method: 'POST' });
      b.okundu = true;
    }
  };
  const hepsi = async olay => {
    const dugme = olay.currentTarget;
    dugme.disabled = true;
    try {
      for (const b of liste.filter(x => !x.okundu)) await okundu(b);
      durum.okunmamis = 0;
      tost('Bildirimler okundu sayıldı');
      ciz();
    } catch (hata) {
      tost(hata.message, true);
      dugme.disabled = false;
    }
  };
  const ikonAdi = b => {
    const r = bildirimHedefi(b.hedef);
    return r?.ekran === 'krediler' ? 'savings' : r?.ekran === 'kartlar' ? 'credit_card' : 'notifications';
  };
  return h(
    'div',
    { class: 'ekran s10' },
    h(
      'div',
      { style: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '0 2px' } },
      h('span', { class: 'alt-yazi', style: { fontSize: '13px' } }, durum.okunmamis ? `${durum.okunmamis} okunmamış` : 'Hepsi okundu'),
      durum.okunmamis > 0 && h('button', { type: 'button', class: 'bag-dugme', onclick: hepsi }, 'Tümünü okundu say')
    ),
    liste.length
      ? h(
          'div',
          { class: 'liste' },
          liste.map(b =>
            h(
              'button',
              {
                type: 'button',
                class: 'satir bil-satir',
                onclick: async () => {
                  try {
                    await okundu(b);
                  } catch {}
                  const r = bildirimHedefi(b.hedef);
                  if (r && ['kartlar', 'krediler', 'panel'].includes(r.ekran)) git(r.ekran);
                  else ciz();
                },
              },
              h('span', { class: 'bil-ik', style: { color: b.okundu ? 'var(--yesil)' : 'var(--eksi)' } }, ikon(ikonAdi(b), 20)),
              h(
                'span',
                { class: 'satir-yazi', style: { gap: '2px' } },
                h(
                  'span',
                  { class: 'ust' },
                  h('b', { style: { fontWeight: b.okundu ? 500 : 700 } }, b.baslik),
                  h('span', { class: 'zaman' }, kisaTarih(b.tarih))
                ),
                h('span', { class: 'metin' }, b.mesaj)
              ),
              !b.okundu && h('span', { class: 'okunmadi', 'aria-label': 'Okunmadı' })
            )
          )
        )
      : h(
          'div',
          { class: 'bos' },
          h('b', {}, 'Henüz bildirim yok'),
          h('span', {}, 'Kart kesim ve son ödeme günleriyle kredi taksitlerinden önce hatırlatma oluşur.')
        ),
    h('div', { class: 'aciklama' }, 'Bir bildirimi okumak borcu ödemez ve kasa hareketi oluşturmaz.')
  );
};

EKRANLAR.diger = async () => {
  if (bildirimGorur()) await okunmamisGuncelle();
  const rolAdi = durum.rol === 'editor' ? 'Editör' : 'İzleyici';
  const satir = (ik, ad, tik, rozet, detay) =>
    h(
      'button',
      { type: 'button', class: 'satir diger-satir', onclick: tik },
      h('span', { class: 'diger-ik' }, ikon(ik, 20)),
      h(
        'span',
        { class: 'satir-yazi' },
        h('span', { style: { fontWeight: 500 } }, ad),
        detay && h('span', { class: 'alt-yazi', style: { fontSize: '13px' } }, detay)
      ),
      rozet > 0 && h('span', { class: 'sayi-rozet' }, String(rozet)),
      okIsareti()
    );
  const raporlar = [satir('monitoring', 'Trend grafikleri', () => git('trend'))];
  if (finansGorur())
    raporlar.push(
      satir('credit_card', 'Kredi kartları', () => git('kartlar')),
      satir('savings', 'Krediler', () => git('krediler'))
    );
  const takip = bildirimGorur() ? [satir('notifications', 'Bildirimler', () => git('bildirimler'), durum.okunmamis)] : [];
  const surum = calisma?.surum || null;
  return h(
    'div',
    { class: 'ekran' },
    h(
      'div',
      { class: 'kim' },
      h('span', { class: 'harf' }, rolAdi[0]),
      h(
        'span',
        { style: { display: 'flex', flexDirection: 'column' } },
        h('span', { style: { fontWeight: 600, fontSize: '16px' } }, rolAdi),
        h(
          'span',
          { class: 'alt-yazi', style: { fontSize: '13px' } },
          durum.rol === 'editor'
            ? editorMu()
              ? 'Editör · kayıt girebilir'
              : 'Editör · bu sürümde yalnız görüntüleme'
            : 'İzleyici · kasaları ve raporları görüntüler'
        )
      )
    ),
    etiket('Raporlar'),
    h('div', { class: 'liste' }, raporlar),
    takip.length > 0 && [etiket('Takip'), h('div', { class: 'liste' }, takip)],
    etiket('Uygulama'),
    h(
      'div',
      { class: 'liste' },
      satir(
        'desktop_windows',
        'Masaüstü görünümü',
        masaustuneGec,
        0,
        durum.rol === 'editor' ? 'Alışlar, ayarlar ve tüm menüler' : 'Tüm menüler ve raporlar'
      )
    ),
    h(
      'div',
      { class: 'liste', style: { marginTop: '6px' } },
      h(
        'button',
        { type: 'button', class: 'satir diger-satir cikis', onclick: cikisYap },
        h('span', { class: 'diger-ik' }, ikon('logout', 20)),
        'Çıkış yap'
      )
    ),
    h('div', { class: 'alt-yazi', style: { textAlign: 'center', fontSize: '12px' } }, `Emar Kasa${surum ? ` · ${surum}` : ''}`)
  );
};

function masaustuneGec() {
  // Tercih kaydedilemezse (tarayıcı depolaması kapalı) adres bayrağı kullanılır; yoksa /m/ ile / arasında döngü olur.
  let kaydedildi = false;
  try {
    localStorage.setItem(MASAUSTU_ANAHTAR, 'masaustu');
    kaydedildi = localStorage.getItem(MASAUSTU_ANAHTAR) === 'masaustu';
  } catch {}
  location.href = kaydedildi ? '/' : '/?gorunum=masaustu';
}
async function cikisYap(olay) {
  const dugme = olay.currentTarget;
  dugme.disabled = true;
  try {
    await api('/api/auth/logout', { method: 'POST' });
    oturumuKapat();
  } catch (hata) {
    oturumuKapat();
    tost('Ekran temizlendi; sunucu oturumu kapatılamadı. Bağlantı gelince yeniden giriş yapıp çıkış yapın.', true);
  }
}

// ---------- alt sayfalar ----------
// Açık alt sayfa geçmişe bir kayıt ekler: sistemin geri hareketi önce sayfayı kapatır. Sayfa açıkken
// arkadaki ekran etkisizdir (inert), odak sayfanın içinde kalır.
let acikSayfa = null;
function sayfaKapatAnlik() {
  if (acikSayfa) acikSayfa.kapat(false);
}
window.addEventListener('popstate', () => {
  if (acikSayfa && !history.state?.sayfa) acikSayfa.kapat(false);
});
function sayfaAc(icerik, etiketMetni) {
  sayfaKapatAnlik();
  const alan = $('sayfa');
  const onceki = document.activeElement;
  history.pushState({ ...(history.state || {}), sayfa: true }, '');
  const kapat = (geriAl = true) => {
    if (acikSayfa?.kapat !== kapat) return;
    acikSayfa.kapanirken?.();
    acikSayfa = null;
    alan.replaceChildren();
    kok.inert = false;
    document.removeEventListener('keydown', esc);
    if (geriAl !== false && history.state?.sayfa) history.back();
    if (onceki?.isConnected && onceki.focus) onceki.focus({ preventScroll: true });
  };
  const esc = e => {
    if (e.key === 'Escape') kapat();
  };
  const sayfa = h(
    'div',
    { class: 'sayfa', role: 'dialog', 'aria-modal': 'true', 'aria-label': etiketMetni },
    h('div', { class: 'tutamak', 'aria-hidden': 'true' }),
    icerik(kapat)
  );
  alan.replaceChildren(h('div', { class: 'perde', onclick: () => kapat() }), sayfa);
  acikSayfa = { kapat };
  kok.inert = true;
  document.addEventListener('keydown', esc);
  setTimeout(() => sayfa.querySelector('input,textarea,button')?.focus({ preventScroll: true }), 50);
  return kapat;
}

async function hizliIslemAc() {
  if (!editorMu()) return;
  let kanallar = [],
    kartlar = [];
  try {
    [kanallar, kartlar] = await Promise.all([api('/api/kanallar'), api('/api/kredikartlari')]);
  } catch (hata) {
    tost(hata.message, true);
    return;
  }
  if (!durum.islemOnbellek.length) islemleriYukle({ bas: ayKaydir(isoGun().slice(0, 7), -1) + '-01', son: isoGun() }).catch(() => {});
  const kanalSecenek = [...kanallar.filter(k => k.aktif).map(k => k.ad), ORTAK];
  // Kanal masaüstündeki gibi bilerek seçilir; varsayılan kanal yok.
  const hz = { tutar: '', cari: '', kanal: null, tip: 'Cari', gun: 'bugun', not: '', kart: '' };
  let benzerOnay = null;
  let kaydediliyor = false;
  let gonderimBasladi = false;
  const kapanirken = () => {
    if (!kaydediliyor) return;
    tost(
      gonderimBasladi
        ? 'Gider kaydı sürüyor. Sonucu bekleyin; aynı ödemeyi yeniden girmeyin.'
        : 'Form kapatıldı; kayıt kontrolü bitse de gider gönderilmeyecek.'
    );
  };
  sayfaAc(kapat => {
    const hata = h('div', { class: 'hata-yazi', role: 'alert', style: { textAlign: 'center' }, hidden: true });
    const kurtarma = h(
      'div',
      { class: 'benzer', hidden: true },
      h('b', {}, 'Önceki gider isteğinin durumu belirsiz'),
      h(
        'span',
        {},
        'Önce İşlemler listesinden önceki giderin oluşup oluşmadığını kontrol edin. Kayıt varsa yeni gider açmak aynı ödemeyi ikinci kez yazabilir.'
      ),
      h(
        'button',
        {
          type: 'button',
          class: 'dugme',
          onclick: () => {
            giderIstekTemizle(bekleyenGiderIstekId);
            benzerOnay = null;
            kurtarma.hidden = true;
            hata.hidden = true;
            tost('Yeni gider için Kaydet’e yeniden dokunun.');
          },
        },
        'Kontrol ettim, yeni gider başlat'
      )
    );
    const onizleme = h('div', { class: 'hz-not' }, 'Giden tutar · kuruş virgülle');
    const tutar = h('input', {
      value: '',
      placeholder: '0,00',
      'aria-label': 'Tutar',
      inputmode: 'decimal',
      autocomplete: 'off',
      enterkeyhint: 'next',
    });
    tutar.addEventListener('input', () => {
      tutar.value = tutar.value.replace(/[^\d.,]/g, '');
      hz.tutar = tutar.value;
      hata.hidden = true;
      degisti();
      if (!hz.tutar) {
        onizleme.textContent = 'Giden tutar · kuruş virgülle';
        return;
      }
      try {
        onizleme.textContent = `Kaydedilecek: ${tl(tutarCoz(hz.tutar) / 100)}`;
      } catch (e) {
        onizleme.textContent = e.message;
      }
    });
    const cari = h('input', {
      class: 'girdi',
      value: '',
      placeholder: 'Firma, kişi ya da ödeme yeri',
      maxlength: '200',
      autocomplete: 'off',
    });
    const oneriAlani = h('div', { class: 'oneriler' });
    const onerileriCiz = () => {
      const q = hz.cari.trim().toLocaleLowerCase('tr-TR');
      const tum = [
        ...new Set(
          [...durum.islemOnbellek]
            .sort((a, b) => String(b.tarih).localeCompare(String(a.tarih)))
            .map(i => i.cari)
            .filter(Boolean)
        ),
      ];
      const liste = (q ? tum.filter(c => c.toLocaleLowerCase('tr-TR').includes(q) && c.toLocaleLowerCase('tr-TR') !== q) : tum).slice(0, 4);
      oneriAlani.replaceChildren(
        ...(liste.length
          ? [
              h('span', {}, q ? 'Öneriler' : 'Son kullanılanlar'),
              h(
                'div',
                {},
                liste.map(c =>
                  h(
                    'button',
                    {
                      type: 'button',
                      onclick: () => {
                        hz.cari = c;
                        cari.value = c;
                        degisti();
                        onerileriCiz();
                      },
                    },
                    c
                  )
                )
              ),
            ]
          : [])
      );
    };
    cari.addEventListener('input', () => {
      hz.cari = cari.value;
      hata.hidden = true;
      degisti();
      onerileriCiz();
    });
    cari.addEventListener('focus', onerileriCiz);
    const secimCiz = (alan, secenekler, anahtar, sinif) => {
      alan.replaceChildren(
        ...secenekler.map(([deger, ad]) =>
          h(
            'button',
            {
              type: 'button',
              'aria-pressed': hz[anahtar] === deger ? 'true' : 'false',
              style:
                sinif === 'kanal' && hz[anahtar] === deger
                  ? { background: kanalRengi(deger).z, color: kanalRengi(deger).r, borderColor: kanalRengi(deger).r }
                  : null,
              onclick: () => {
                hz[anahtar] = deger;
                hata.hidden = true;
                degisti();
                hepsiniCiz();
              },
            },
            ad
          )
        )
      );
    };
    const kanalAlani = h('div', { class: `secim kanal s${Math.min(4, kanalSecenek.length)}`, role: 'group', 'aria-label': 'Kanal' });
    const tipAlani = h('div', { class: 'secim s3', role: 'group', 'aria-label': 'Gider tipi' });
    const gunAlani = h('div', { class: 'secim s2', role: 'group', 'aria-label': 'Tarih' });
    const kartAlani = h(
      'label',
      { class: 'alan-etiket' },
      'Kredi kartı',
      h(
        'select',
        {
          class: 'girdi',
          onchange: e => {
            hz.kart = e.target.value;
            degisti();
          },
        },
        h('option', { value: '' }, 'Kart seçilmedi'),
        giderKartlari(kartlar).map(k => h('option', { value: k.id }, k.ad))
      )
    );
    const not = h('input', { class: 'girdi', value: '', placeholder: 'Not (isteğe bağlı)', 'aria-label': 'Not', maxlength: '2000' });
    not.addEventListener('input', () => {
      hz.not = not.value;
    });
    const benzerAlani = h('div');
    // Alanlar değişince eski benzer kayıt onayı geçersizdir.
    function degisti() {
      benzerOnay = null;
      benzerAlani.replaceChildren();
    }
    const kaydetDugme = h('button', { type: 'button', class: 'dugme ana', onclick: () => kaydet() }, 'Kaydet');
    const ustKaydet = h('button', { type: 'button', class: 'kaydet', onclick: () => kaydet() }, 'Kaydet');
    const hepsiniCiz = () => {
      const bugun = isoGun(),
        dun = gunEkle(bugun, -1);
      secimCiz(
        kanalAlani,
        kanalSecenek.map(k => [k, k]),
        'kanal',
        'kanal'
      );
      secimCiz(
        tipAlani,
        [
          ['Cari', tipAdi('Cari')],
          ['SabitGider', tipAdi('SabitGider')],
          ['KrediKarti', tipAdi('KrediKarti')],
        ],
        'tip'
      );
      secimCiz(
        gunAlani,
        [
          ['bugun', `Bugün · ${kisaTarih(bugun)}`],
          ['dun', `Dün · ${kisaTarih(dun)}`],
        ],
        'gun'
      );
      kartAlani.hidden = hz.tip !== 'KrediKarti' || !kartlar.length;
    };
    hepsiniCiz();
    async function kaydet() {
      if (kaydediliyor) return;
      hata.hidden = true;
      let kurus;
      try {
        kurus = tutarCoz(hz.tutar);
      } catch (e) {
        hata.textContent = e.message;
        hata.hidden = false;
        tutar.focus();
        return;
      }
      if (!hz.cari.trim()) {
        hata.textContent = 'Açıklama ya da ödeme yerini yazın.';
        hata.hidden = false;
        cari.focus();
        return;
      }
      if (!hz.kanal) {
        hata.textContent = 'Kanal seçin.';
        hata.hidden = false;
        kanalAlani.querySelector('button')?.focus();
        return;
      }
      const bugun = isoGun();
      const tarih = hz.gun === 'dun' ? gunEkle(bugun, -1) : bugun;
      const govde = {
        tarih,
        cari: hz.cari.trim(),
        tutarTl: kurus / 100,
        kanal: hz.kanal,
        tip: hz.tip,
        not: hz.not.trim() || null,
        krediKartiId: hz.tip === 'KrediKarti' && hz.kart ? Number(hz.kart) : null,
      };
      kaydediliyor = true;
      kaydetDugme.disabled = true;
      ustKaydet.disabled = true;
      try {
        const imza = JSON.stringify(govde);
        const alanImzasi = JSON.stringify(hz);
        // Onay tek kullanımlıktır: kayıt isteği yanıtsız kalırsa yeniden denemede benzer kayıt yeniden aranır.
        const onayli = benzerOnay === imza;
        benzerOnay = null;
        if (!onayli) {
          let benzer;
          try {
            benzer = await api('/api/islemler/benzerlik', {
              method: 'POST',
              body: {
                tur: 'Gider',
                tarih: govde.tarih,
                tutar: govde.tutarTl,
                krediKartiId: govde.krediKartiId,
                kanal: govde.kanal,
                alisId: null,
              },
            });
          } catch (e) {
            throw new Error(`Benzer kayıt kontrolü tamamlanamadı. Kayıt yapılmadı; yeniden deneyin. ${e.message}`);
          }
          if (acikSayfa?.kapat !== kapat) return;
          if (JSON.stringify(hz) !== alanImzasi || isoGun() !== bugun)
            throw new Error('Alanlar benzer kayıt kontrolü sırasında değişti. Güncel bilgilerle yeniden Kaydet’e dokunun.');
          if (!Array.isArray(benzer))
            throw new Error('Benzer kayıt kontrolünden geçerli yanıt alınamadı. Kayıt yapılmadı; yeniden deneyin.');
          if (benzer.length) {
            benzerOnay = imza;
            benzerAlani.replaceChildren(
              h(
                'div',
                { class: 'benzer', role: 'status' },
                h('b', {}, 'Benzer kayıt bulundu'),
                h('span', {}, 'Aynı tarih, tutar ve kart veya kanalla bir kayıt var. Aynı ödemeyi yeniden girmediğinizi kontrol edin.'),
                h(
                  'ul',
                  {},
                  benzer
                    .slice(0, 4)
                    .map(r =>
                      h(
                        'li',
                        {},
                        `${KAYNAK_ADLARI[r.kaynak] || 'Kayıt'} #${r.id} · ${kisaTarih(r.tarih)} · ${tl(r.tutar)} · ${r.aciklama || 'Açıklama yok'}${r.alisId ? ` · Alış #${r.alisId}` : ''}`
                      )
                    )
                ),
                h('span', {}, 'Ayrı bir işlemse yeniden “Kaydet”e dokunun.')
              )
            );
            return;
          }
        }
        const istekId = giderIstekId();
        gonderimBasladi = true;
        await api('/api/islemler', { method: 'POST', body: { ...govde, istekId } });
        giderIstekTemizle(istekId);
        kaydediliyor = false;
        kapat();
        tost(`İşlem eklendi · ${tl(govde.tutarTl)}`);
        durum.islemOnbellek = [];
        ciz();
      } catch (e) {
        if (acikSayfa?.kapat === kapat) {
          if (e.status === 409 && e.code === 'ISTEK_KIMLIGI_CAKISMASI') kurtarma.hidden = false;
          hata.textContent = e.message;
          hata.hidden = false;
        } else if (gonderimBasladi) {
          tost(`Gider kaydının sonucu doğrulanamadı. İşlemlerden kontrol edin. ${e.message}`, true);
        }
      } finally {
        kaydediliyor = false;
        kaydetDugme.disabled = false;
        ustKaydet.disabled = false;
      }
    }
    return h(
      'div',
      { class: 'hz' },
      h(
        'div',
        { class: 'sayfa-bas' },
        h('button', { type: 'button', onclick: () => kapat() }, 'Vazgeç'),
        h('b', {}, 'Hızlı işlem'),
        ustKaydet
      ),
      h('div', { class: 'hz-tutar' }, tutar, h('span', {}, '₺')),
      onizleme,
      h('label', { class: 'alan-etiket' }, 'Açıklama / ödeme yeri', cari),
      oneriAlani,
      h('div', { class: 'hz-bas' }, 'Kanal'),
      kanalAlani,
      h('div', { class: 'hz-bas' }, 'Tip'),
      tipAlani,
      kartAlani,
      gunAlani,
      not,
      benzerAlani,
      kurtarma,
      hata,
      kaydetDugme,
      h(
        'div',
        { class: 'aciklama' },
        'Alış olarak kaydettiğiniz bir ödemeyi burada ikinci kez girmeyin; alışın ödemesi masaüstü görünümündeki Alışlar ekranından eklenir.'
      )
    );
  }, 'Hızlı işlem');
  acikSayfa.kapanirken = kapanirken;
}

function kurtarmaAc() {
  sayfaAc(kapat => {
    const hata = h('div', { class: 'hata-yazi', role: 'alert', hidden: true });
    const k = h('input', { class: 'girdi', name: 'kullanici', autocomplete: 'username', maxlength: '64', required: true });
    const kod = h('input', { class: 'girdi', name: 'kod', autocomplete: 'off', placeholder: 'ABCD-1234', required: true });
    const yeni = h('input', {
      class: 'girdi',
      name: 'yeniSifre',
      type: 'password',
      autocomplete: 'new-password',
      minlength: '12',
      maxlength: '1024',
      required: true,
    });
    const dugme = h('button', { type: 'submit', class: 'dugme ana' }, 'Şifreyi yenile');
    const form = h(
      'form',
      { class: 'hz' },
      h(
        'div',
        { class: 'sayfa-bas' },
        h('button', { type: 'button', onclick: () => kapat() }, 'Vazgeç'),
        h('b', {}, 'Editör hesabını kurtar'),
        h('span', { style: { width: '52px' } })
      ),
      h(
        'div',
        { class: 'aciklama' },
        'Daha önce oluşturduğunuz tek kullanımlık kurtarma kodunu girin. Başarılı kurtarma tüm eski oturumları kapatır.'
      ),
      h('label', { class: 'alan-etiket' }, 'Kullanıcı adı', k),
      h('label', { class: 'alan-etiket' }, 'Kurtarma kodu', kod),
      h('label', { class: 'alan-etiket' }, 'Yeni şifre (en az 12 karakter)', yeni),
      hata,
      dugme
    );
    form.addEventListener('submit', async e => {
      e.preventDefault();
      if (!form.reportValidity()) return;
      dugme.disabled = true;
      hata.hidden = true;
      try {
        await api('/api/auth/kurtar', { method: 'POST', body: { kullanici: k.value, kod: kod.value, yeniSifre: yeni.value } });
        kapat();
        tost('Şifreniz yenilendi. Yeni şifrenizle giriş yapın.');
      } catch (err) {
        hata.textContent = err.message;
        hata.hidden = false;
      } finally {
        dugme.disabled = false;
      }
    });
    return form;
  }, 'Editör hesabını kurtar');
}

// ---------- giriş / oturum ----------
function logo(boy) {
  const svg = document.createElementNS(SVGNS, 'svg');
  svg.setAttribute('width', boy);
  svg.setAttribute('height', boy);
  svg.setAttribute('viewBox', '0 0 512 512');
  svg.setAttribute('aria-hidden', 'true');
  const r = document.createElementNS(SVGNS, 'rect');
  r.setAttribute('width', '512');
  r.setAttribute('height', '512');
  r.setAttribute('rx', '100');
  r.setAttribute('fill', '#194d3c');
  const p = document.createElementNS(SVGNS, 'path');
  p.setAttribute('d', 'M155 135h48v113l99-113h62L248 262l122 115h-67L203 278v99h-48z');
  p.setAttribute('fill', '#f6f3e9');
  svg.append(r, p);
  return svg;
}
function girisCiz(mesaj = '') {
  durum.ekranNo++;
  const hata = h('div', { class: 'hata-yazi', role: 'alert', hidden: !mesaj }, mesaj);
  const k = h('input', {
    class: 'girdi',
    name: 'kullanici',
    autocomplete: 'username',
    maxlength: '64',
    placeholder: 'İzleyici girişinde boş bırakın',
    autocapitalize: 'none',
    spellcheck: 'false',
  });
  const s = h('input', {
    class: 'girdi',
    name: 'sifre',
    type: 'password',
    autocomplete: 'current-password',
    maxlength: '1024',
    required: true,
    placeholder: '••••••••',
  });
  const dugme = h('button', { type: 'submit', class: 'dugme ana' }, 'Giriş yap');
  const form = h(
    'form',
    { class: 'form-kutu' },
    h('label', { class: 'alan-etiket' }, 'Kullanıcı adı', k),
    h('label', { class: 'alan-etiket' }, 'Şifre', s),
    hata,
    dugme
  );
  form.addEventListener('submit', async e => {
    e.preventDefault();
    if (!s.value) {
      hata.textContent = 'Şifre gerekli.';
      hata.hidden = false;
      return;
    }
    dugme.disabled = true;
    hata.hidden = true;
    try {
      const sonuc = await api('/api/auth/login', { method: 'POST', body: { kullanici: k.value.trim() || null, sifre: s.value } });
      s.value = '';
      await gir(sonuc.rol);
    } catch (err) {
      hata.textContent = err.status === 401 ? 'Kullanıcı adı ya da şifre hatalı.' : err.message;
      hata.hidden = false;
    } finally {
      dugme.disabled = false;
    }
  });
  kok.replaceChildren(
    h('div', { class: 'ust-bosluk' }),
    h(
      'main',
      { class: 'giris' },
      h(
        'div',
        { class: 'logo' },
        logo(64),
        h('b', {}, 'Emar Kasa'),
        h('span', { class: 'alt-yazi', style: { fontSize: '14px' } }, 'Kasa defteri')
      ),
      form,
      !calisma?.saltOkunur &&
        h('button', { type: 'button', class: 'dugme yesil-yazi', onclick: kurtarmaAc }, ikon('key', 22), 'Editör şifremi unuttum'),
      h(
        'div',
        { class: 'dipnot' },
        calisma?.saltOkunur
          ? 'Bu sürüm yalnız kasa görüntüleme içindir.'
          : 'İzleyici hesabı kasaları ve raporları görüntüler. Kayıt girişi editör hesabıyla yapılır.'
      ),
      h('button', { type: 'button', class: 'bag-dugme', style: { alignSelf: 'center' }, onclick: masaustuneGec }, 'Masaüstü görünümüne geç')
    )
  );
  document.title = 'Giriş · Emar Kasa';
}
async function gir(rol) {
  if (rol === 'alici') {
    masaustuneGec();
    return;
  }
  durum.rol = rol;
  const r = rotaOku();
  durum.yigin = [r];
  if (!SEKMELER.some(([k]) => k === r.ekran)) durum.yigin = [{ ekran: UST[r.ekran] || 'panel' }, r];
  history.replaceState(null, '', location.pathname + location.search + rotaMetni(r.ekran, r));
  await ciz();
}
function oturumuKapat() {
  durum.oturum++;
  durum.rol = null;
  durum.yigin = [];
  durum.islemOnbellek = [];
  durum.donemOnbellek = [];
  durum.okunmamis = 0;
  durum.islemFiltre = { ara: '', kanal: 'Tümü', tip: 'Tümü' };
  durum.islemBaslangic = null;
  durum.ay = null;
  durum.acikHafta = 0;
  durum.trendSegment = 'kasa';
  durum.ayOnbellek.clear();
  sayfaKapatAnlik();
  girisCiz();
}

// Oturum yalnız HttpOnly çerezde taşınır; burada kimlik bilgisi saklanmaz.
try {
  localStorage.removeItem(MASAUSTU_ANAHTAR);
} catch {}
calismaHazir
  .then(() =>
    api('/api/auth/me')
      .then(k => gir(k.rol))
      .catch(() => girisCiz())
  )
  .catch(hata => {
    kok.replaceChildren(
      h('div', { class: 'giris' }, h('div', { class: 'bos' }, hata.message || 'Kasa ayarı yüklenemedi. Sayfayı yenileyin.'))
    );
  });
