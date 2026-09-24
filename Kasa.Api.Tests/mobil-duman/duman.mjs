// Emar Kasa · telefon görünümü (/m) duman testi — İSTEĞE BAĞLI, yerelde çalıştırılır (CI'da yok).
//
//   PLAYWRIGHT_BROWSERS_PATH=/opt/pw-browsers node Kasa.Api.Tests/mobil-duman/duman.mjs
//   (DUMAN_EKRAN=/bir/klasor verilirse her sekmenin ekran görüntüsü oraya yazılır.)
//
// Geçici bir SQLite veritabanıyla Kasa.Api'yi başlatır (ya da KASA_URL verilirse o sunucuyu kullanır;
// o zaman KASA_KULLANICI / KASA_SIFRE gerekir), telefon boyutlu Chromium'da giriş yapar, beş ekranı
// gezer ve şunları denetler: konsol/CSP hatası yok, service worker /m/ kapsamında, önbellekte /api
// yok, çevrimdışıyken kabuk açılıyor, çıkıştan sonra veri 401. Playwright global kurulu olmalı.
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
let playwright;
try { playwright = require('playwright'); }
catch {
  // Global kurulum (npm i -g playwright): node'un yanındaki lib/node_modules.
  const m = await import(pathToFileURL(join(process.execPath, '..', '..', 'lib', 'node_modules', 'playwright', 'index.js')).href);
  playwright = m.default ?? m;
}
const { chromium, devices } = playwright;

const kok = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const port = 5000 + Math.floor(Math.random() * 900) + 100;
const hazirUrl = process.env.KASA_URL;
const taban = hazirUrl || `http://127.0.0.1:${port}`;
const kullanici = process.env.KASA_KULLANICI || 'editor';
const sifre = process.env.KASA_SIFRE || 'xxxxxxxx';   // yalnız geçici test sunucusu için; gerçek bir parola değil

const hatalar = [];
const adim = (m) => console.log('·', m);
const kontrol = (kosul, mesaj) => { if (!kosul) { hatalar.push(mesaj); console.error('  HATA:', mesaj); } };

let sunucu = null;
let gecici = null;

async function sunucuyuBaslat() {
  gecici = mkdtempSync(join(tmpdir(), 'kasa-duman-'));
  sunucu = spawn('dotnet', ['run', '--project', join(kok, 'Kasa.Api'), '--no-launch-profile'], {
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: taban,
      ConnectionStrings__Kasa: `Data Source=${join(gecici, 'kasa.db')}`,
      Kasa__EditorKullanici: kullanici,
      Kasa__EditorSifre: sifre,
      Kasa__JwtKey: 'duman-testi-icin-yerel-anahtar-en-az-32-bayt!!',
      Kasa__BelgeKlasoru: join(gecici, 'belgeler'),
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  sunucu.stderr.on('data', (d) => process.stderr.write(d));
  for (let i = 0; i < 240; i++) {
    try { if ((await fetch(taban + '/health')).ok) return; } catch { /* henüz açılmadı */ }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error('Kasa.Api 2 dakikada açılmadı.');
}

/** Yalnız kendi başlattığımız geçici sunucuya örnek veri yazar (KASA_URL verilmişse ASLA yazmaz). */
async function ornekVeri() {
  const giris = await (await fetch(taban + '/api/auth/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ kullanici, sifre }),
  })).json();
  const yaz = async (yontem, yol, govde) => {
    const r = await fetch(taban + '/api' + yol, {
      method: yontem, headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + giris.token }, body: JSON.stringify(govde),
    });
    if (!r.ok) throw new Error(`${yontem} ${yol}: ${r.status} ${await r.text()}`);
  };
  const g = (gun) => { const d = new Date(); d.setDate(d.getDate() + gun); return d.toISOString().slice(0, 10); };
  const bugun = g(0);
  const ayBasi = bugun.slice(0, 8) + '01';
  await yaz('PUT', '/ayarlar', { takipBaslangic: ayBasi, kasaAcilisDevri: 10000 });
  await yaz('POST', '/cariler', { ad: 'Yılmaz Gıda Ltd. Şti.', aktif: true });
  await yaz('POST', '/islemler', { tarih: bugun, cari: 'Yılmaz Gıda Ltd. Şti.', tutarTl: 1234.56, kanal: 'MEZAT', tip: 'Cari', not: 'Duman testi' });
  await yaz('PUT', '/gelenler', { donemStart: bugun, kanal: 'MEZAT', tutarTl: 8000 });
  await yaz('POST', '/kredikartlari', { ad: 'Bonus', kesimTarihi: g(5), sonOdemeTarihi: g(15), limit: 50000, borc: 1200 });
  await yaz('POST', '/cekler', { yon: 'Alinan', kisi: 'Kaya Ambalaj', tutar: 5000, duzenlemeTarihi: bugun, vadeTarihi: g(10), kanal: 'MEZAT', durum: 'Portfoyde' });
  await yaz('POST', '/cekler', { yon: 'Verilen', kisi: 'Demir Nakliyat', tutar: 750, duzenlemeTarihi: g(-20), vadeTarihi: g(-2), kanal: 'MEZAT', durum: 'Portfoyde' });
}

async function calistir() {
  if (!hazirUrl) {
    adim('Kasa.Api başlatılıyor (geçici veritabanı)…');
    await sunucuyuBaslat();
    adim('Örnek veri');
    await ornekVeri();
  }
  const tarayici = await chromium.launch();
  const baglam = await tarayici.newContext({ ...devices['iPhone 13'], serviceWorkers: 'allow' });
  const sayfa = await baglam.newPage();
  // Beklenen ağ günlükleri (401 oturumsuz, çevrimdışı) hata sayılmaz; CSP ihlali ve JS hatası sayılır.
  sayfa.on('console', (m) => {
    if (m.type() !== 'error') return;
    const t = m.text();
    if (t.startsWith('Failed to load resource') && !t.includes('Content Security Policy')) return;
    kontrol(false, 'Konsol hatası: ' + t);
  });
  sayfa.on('pageerror', (e) => kontrol(false, 'Sayfa hatası: ' + e.message));

  adim('Giriş ekranı');
  await sayfa.goto(taban + '/m/');
  await sayfa.waitForSelector('#giris-formu', { timeout: 15000 });
  await sayfa.fill('#sifre', 'yanlis');
  await sayfa.fill('#kullanici', kullanici);
  await sayfa.click('#giris-dugme');
  await sayfa.waitForSelector('#giris-hata:not([hidden])');
  kontrol((await sayfa.textContent('#giris-hata')).includes('hatalı'), 'Yanlış şifre mesajı görünmedi');

  adim('Giriş');
  await sayfa.fill('#sifre', sifre);
  await sayfa.click('#giris-dugme');
  await sayfa.waitForSelector('.kahraman', { timeout: 15000 });
  kontrol((await sayfa.textContent('.kahraman')).includes('Güncel kasa'), 'Panel açılmadı');

  for (const [sekme, beklenen] of [['haftalik', null], ['aylik', 'Ay sonucu'], ['cekler', 'Portföydeki alınan'], ['kartlar', null], ['panel', 'Güncel kasa']]) {
    adim('Sekme: ' + sekme);
    await sayfa.click(`#sekmeler a[data-sekme="${sekme}"]`);
    await sayfa.waitForFunction((s) => document.querySelector(`#sekmeler a[data-sekme="${s}"]`)?.getAttribute('aria-current') === 'page'
      && !document.querySelector('#icerik > .bilgi'), sekme);
    kontrol(!(await sayfa.$('.hata-kutu')), `${sekme}: hata kutusu göründü`);
    kontrol(!(await sayfa.textContent('#icerik')).includes('[object'), `${sekme}: öğe metne dönüşmüş ([object …])`);
    if (beklenen) kontrol((await sayfa.textContent('#icerik')).includes(beklenen), `${sekme}: "${beklenen}" yok`);
    if (process.env.DUMAN_EKRAN) await sayfa.screenshot({ path: join(process.env.DUMAN_EKRAN, `${sekme}.png`), fullPage: true });
  }
  await sayfa.click('#sekmeler a[data-sekme="aylik"]');
  await sayfa.waitForSelector('.ay-secici');
  const ayOnce = await sayfa.textContent('.ay-secici .ay');
  await sayfa.click('.ay-secici button[aria-label="Önceki ay"]');
  await sayfa.waitForFunction((a) => document.querySelector('.ay-secici .ay')?.textContent !== a, ayOnce);

  adim('Service worker ve önbellek');
  const kapsam = await sayfa.evaluate(() => navigator.serviceWorker.ready.then((r) => r.scope));
  kontrol(kapsam === taban + '/m/', 'Service worker kapsamı yanlış: ' + kapsam);
  await sayfa.reload();
  await sayfa.waitForSelector('.ay-secici, .kahraman', { timeout: 15000 });
  const onbellek = await sayfa.evaluate(async () => {
    const liste = [];
    for (const ad of await caches.keys()) for (const r of await (await caches.open(ad)).keys()) liste.push(r.url);
    return liste;
  });
  kontrol(onbellek.length > 0, 'Kabuk önbelleğe alınmadı');
  kontrol(!onbellek.some((u) => u.includes('/api')), 'Önbellekte /api var: ' + onbellek.join(', '));

  adim('Çevrimdışı');
  await baglam.setOffline(true);
  await sayfa.reload();
  await sayfa.waitForSelector('#baglanti:not([hidden]), .hata-kutu', { timeout: 15000 });
  await baglam.setOffline(false);

  adim('Çıkış');
  await sayfa.reload();
  await sayfa.waitForSelector('#cikis:not([hidden])', { timeout: 15000 });
  await sayfa.click('#cikis');
  await sayfa.waitForSelector('#giris-formu');
  const durum = await sayfa.evaluate(() => fetch('/api/rapor/panel', { credentials: 'same-origin' }).then((r) => r.status));
  kontrol(durum === 401, 'Çıkıştan sonra veri açık: ' + durum);

  const disYazma = await sayfa.evaluate(async () => {
    const r = await fetch('/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
    return r.status;
  });
  kontrol(disYazma !== 403, 'Aynı kaynaktan giriş isteği 403 aldı');

  await tarayici.close();
}

try {
  await calistir();
} catch (e) {
  hatalar.push(String(e && e.stack || e));
  console.error(e);
} finally {
  if (sunucu) sunucu.kill('SIGTERM');
  if (gecici) try { rmSync(gecici, { recursive: true, force: true }); } catch { /* geçici klasör */ }
}
const gercek = hatalar.filter(Boolean);
console.log(gercek.length === 0 ? 'DUMAN TESTİ GEÇTİ' : `DUMAN TESTİ BAŞARISIZ (${gercek.length} hata)`);
process.exit(gercek.length === 0 ? 0 : 1);
