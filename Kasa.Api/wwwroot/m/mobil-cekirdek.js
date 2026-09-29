// Telefon arayüzünün saf yardımcıları. DOM'a dokunmaz; node --test ile sınanır.
export const AYLAR = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık'];
export const AYK = ['Oca', 'Şub', 'Mar', 'Nis', 'May', 'Haz', 'Tem', 'Ağu', 'Eyl', 'Eki', 'Kas', 'Ara'];
export const GUNLER = ['Pazar', 'Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi'];
export const ORTAK = 'Ortak';

const F2 = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const F1 = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const F0 = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 0 });
const sayi = value => { const n = Number(value); return Number.isFinite(n) ? n : 0; };
// Kuruşa yuvarlanmış mutlak değer; -0,00 gibi çıktıları önler.
const kurus = value => Math.round(sayi(value) * 100);

export const f2 = value => F2.format(kurus(value) / 100);
export const f1 = value => F1.format(sayi(value));
export const f0 = value => F0.format(sayi(value));
/** 1.308.800,00 ₺ */
export const tl = value => f2(value) + ' ₺';
/** +47.750,00 · −4.200,00 · 0,00 (işaretli, simgesiz) */
export function imzaliYalin(value) {
  const k = kurus(value);
  return (k < 0 ? '−' : k > 0 ? '+' : '') + F2.format(Math.abs(k) / 100);
}
/** +47.750,00 ₺ */
export const imzali = value => imzaliYalin(value) + ' ₺';
/** Tutarın rengi için sınıf: arti · eksi · notr */
export function isaretSinifi(value) { const k = kurus(value); return k < 0 ? 'eksi' : k > 0 ? 'arti' : 'notr'; }
export const yuzde = value => '%' + F0.format(sayi(value));

const parca = tarih => String(tarih).slice(0, 10).split('-').map(Number);
export function tarihNesnesi(tarih) { const [y, m, d] = parca(tarih); return new Date(y, m - 1, d, 12); }
/** 26 Eylül Cumartesi */
export function uzunTarih(tarih) { const [y, m, d] = parca(tarih); return `${d} ${AYLAR[m - 1]} ${GUNLER[new Date(y, m - 1, d).getDay()]}`; }
/** 26 Eylül 2026 Cumartesi */
export function tamTarih(tarih) { const [y, m, d] = parca(tarih); return `${d} ${AYLAR[m - 1]} ${y} ${GUNLER[new Date(y, m - 1, d).getDay()]}`; }
/** 26 Eyl */
export function kisaTarih(tarih) { const [, m, d] = parca(tarih); return `${d} ${AYK[m - 1]}`; }
export function isoGun(date = new Date()) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
export function gunEkle(tarih, gun) { const d = tarihNesnesi(tarih); d.setDate(d.getDate() + gun); return isoGun(d); }
export function gunFarki(bas, son) { return Math.round((tarihNesnesi(son) - tarihNesnesi(bas)) / 86400000); }

/** Dönem adı: "21–27 Eylül 2026", "27–31 Temmuz 2026", ay değişirse "31 Ağu – 6 Eyl 2026", tek gün "31 Ağustos 2026". */
export function donemAdi(donem) {
  const [y1, m1, d1] = parca(donem.start);
  const [y2, m2, d2] = parca(donem.end);
  if (donem.start === donem.end) return `${d1} ${AYLAR[m1 - 1]} ${y1}`;
  if (y1 === y2 && m1 === m2) return `${d1}–${d2} ${AYLAR[m1 - 1]} ${y1}`;
  if (y1 === y2) return `${d1} ${AYK[m1 - 1]} – ${d2} ${AYK[m2 - 1]} ${y2}`;
  return `${d1} ${AYK[m1 - 1]} ${y1} – ${d2} ${AYK[m2 - 1]} ${y2}`;
}
/** Kısa dönem adı: "21–27 Eylül" */
export function donemKisa(donem) {
  const [, m1, d1] = parca(donem.start);
  const [, m2, d2] = parca(donem.end);
  if (donem.start === donem.end) return `${d1} ${AYLAR[m1 - 1]}`;
  if (m1 === m2) return `${d1}–${d2} ${AYLAR[m1 - 1]}`;
  return `${d1} ${AYK[m1 - 1]} – ${d2} ${AYK[m2 - 1]}`;
}
/**
 * Hafta ay sınırında bölündüyse true: dönem pazartesi dışı bir ayın 1'inde başlar ya da pazar dışı
 * bir ay sonunda biter. Bugünle kesilen açık dönem ve takip başlangıcındaki ilk dönem bölünmüş sayılmaz.
 */
export function bolunmusMu(donem) {
  const bas = tarihNesnesi(donem.start), son = tarihNesnesi(donem.end);
  const aySonu = gunEkle(donem.end, 1).endsWith('-01');
  return (bas.getDate() === 1 && bas.getDay() !== 1) || (aySonu && son.getDay() !== 0);
}

/**
 * Telefonda yazılan tutarı kuruşa çevirir. Türkçe biçimi kabul eder:
 * "1500", "1500,5", "1.500", "1.500,50", "1500.50". Nokta, sıfırla başlamayan bir
 * sayıdan sonra tam üçlü gruplar geliyorsa binlik ayırıcıdır; aksi halde ondalıktır. Belirsiz ya da
 * geçersiz girdide hata verir; hiçbir zaman sessizce yuvarlamaz.
 */
export function tutarCoz(girdi, { sifirOlabilir = false } = {}) {
  let metin = String(girdi ?? '').trim().replace(/\s|₺|TL/gi, '');
  if (!metin) throw new Error('Tutar girin.');
  if (metin.includes(',')) {
    if ((metin.match(/,/g) || []).length > 1) throw new Error('Tutarda yalnız bir virgül olabilir.');
    const [tam, ondalik] = metin.split(',');
    if (tam.includes('.') && !/^[1-9]\d{0,2}(\.\d{3})+$/.test(tam)) throw new Error('Binlik ayırıcı noktaları kontrol edin (örnek: 1.500,50).');
    metin = tam.replace(/\./g, '') + '.' + ondalik;
  } else if (/^[1-9]\d{0,2}(\.\d{3})+$/.test(metin)) {
    metin = metin.replace(/\./g, '');
  }
  if (!/^\d+(\.\d{1,2})?$/.test(metin)) throw new Error('Tutarı rakamla ve en çok iki kuruş basamağıyla girin (örnek: 1.500,50).');
  const [tam, ondalik = ''] = metin.split('.');
  const sonuc = Number(BigInt(tam) * 100n + BigInt(ondalik.padEnd(2, '0')));
  if (!Number.isSafeInteger(sonuc) || sonuc > 99999999999999) throw new Error('Tutar çok büyük.');
  if (!sifirOlabilir && sonuc === 0) throw new Error('Tutar sıfırdan büyük olmalı.');
  return sonuc;
}

/** Çizgi grafiği için SVG yolu. v: değerler, w/h: kutu, p: iç boşluk. */
export function cizgiYolu(degerler, w, h, p) {
  const v = degerler.map(sayi);
  if (!v.length) return { d: '', alan: '', noktalar: [] };
  const mn = Math.min(...v), mx = Math.max(...v), r = (mx - mn) || 1;
  const adim = v.length > 1 ? (w - 2 * p) / (v.length - 1) : 0;
  const noktalar = v.map((y, i) => [v.length > 1 ? p + i * adim : w / 2, p + (h - 2 * p) * (1 - (y - mn) / r)]);
  const d = noktalar.map((q, i) => (i ? 'L' : 'M') + q[0].toFixed(1) + ' ' + q[1].toFixed(1)).join(' ');
  const son = noktalar[noktalar.length - 1], ilk = noktalar[0];
  return { d, alan: `${d} L${son[0].toFixed(1)} ${h} L${ilk[0].toFixed(1)} ${h} Z`, noktalar };
}
/** Sıfır çizgili çubuk grafiği ölçekleri (yüzde). */
export function cubukOlcek(degerler) {
  const v = degerler.map(sayi);
  const mp = Math.max(0, ...v), mn = Math.max(0, ...v.map(x => -x)), t = (mp + mn) || 1;
  return { ust: mp / t * 100, alt: mn / t * 100, cubuklar: v.map(x => ({ hp: x > 0 ? Math.max(3, x / mp * 100) : 0, hn: x < 0 ? Math.max(3, -x / mn * 100) : 0 })) };
}

const KANAL_RENK = {
  MEZAT: { r: '#2E5E7E', z: '#E6EEF3' },
  PERAKENDE: { r: '#8C5B14', z: '#F5ECDC' },
  TOPTAN: { r: '#7B4B73', z: '#F1E7EF' },
  [ORTAK]: { r: '#5E6457', z: '#ECEBE4' },
};
const YEDEK_RENK = [{ r: '#3F6B4E', z: '#E7EFE9' }, { r: '#6B4E2E', z: '#F1EAE1' }, { r: '#2E6B6B', z: '#E3F0F0' }, { r: '#6B2E4A', z: '#F2E6EC' }];
/** Kanal rengi: r yazı/nokta, z zemin. Bilinmeyen kanallar adına göre sabit bir yedek renk alır. */
export function kanalRengi(ad) {
  const anahtar = String(ad || '').toLocaleUpperCase('tr-TR');
  if (KANAL_RENK[anahtar]) return KANAL_RENK[anahtar];
  if (String(ad) === ORTAK) return KANAL_RENK[ORTAK];
  let h = 0; for (const c of String(ad || '')) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  return YEDEK_RENK[h % YEDEK_RENK.length];
}

export const TIP_ADLARI = { Cari: 'Diğer gider', SabitGider: 'Sabit gider', KrediKarti: 'Kredi kartı' };
export const tipAdi = tip => TIP_ADLARI[tip] || String(tip || '');
// Yeni kartlı gider yalnız yeni takipteki, açık karta girilir (gap-tarihsel-3, K3); sunucu takipsiz ya da kapalı kartı reddeder.
// Masaüstü görünümündeki gider formu da aynı süzgeci kullanır (app.js).
export const giderKartlari = kartlar => (kartlar || []).filter(k => k.yeniTakip && k.aktif);

/** iOS mu Android mi: iPhone/iPad (iPadOS masaüstü kimliği dahil) iOS görünümünü alır, diğer her şey Android. */
export function platformBul(ua = '', dokunmaNoktasi = 0) {
  if (/iPhone|iPad|iPod/i.test(ua)) return 'ios';
  if (/Macintosh/i.test(ua) && dokunmaNoktasi > 1) return 'ios';
  return 'android';
}

/** "#islem/12" → { ekran: 'islem', id: 12 }. Masaüstü bildirim adresleri de çevrilir. */
export function rotaCoz(hash) {
  const metin = String(hash || '').replace(/^#\/?/, '');
  const [ekran, parametre] = metin.split('/');
  const cevir = { home: 'panel', notifications: 'bildirimler', cards: 'kartlar', loans: 'krediler', weekly: 'haftalik', monthly: 'aylik', transactions: 'islemler' };
  const ad = cevir[ekran] || ekran || 'panel';
  const id = parametre && /^[1-9]\d*$/.test(parametre) ? Number(parametre) : null;
  return { ekran: ad, id };
}

/** İşlem listesini gün gün gruplar; en yeni gün önce. */
export function gunlereAyir(islemler) {
  const gruplar = [];
  const sirali = [...islemler].sort((a, b) => String(b.tarih).localeCompare(String(a.tarih)) || (b.id || 0) - (a.id || 0));
  for (const islem of sirali) {
    const gun = String(islem.tarih).slice(0, 10);
    let grup = gruplar[gruplar.length - 1];
    if (!grup || grup.tarih !== gun) { grup = { tarih: gun, toplam: 0, islemler: [] }; gruplar.push(grup); }
    grup.toplam = (Math.round(grup.toplam * 100) + kurus(islem.tutarTl)) / 100;
    grup.islemler.push(islem);
  }
  return gruplar;
}

/** Türkçe duyarlı arama: cari, not ve kanal içinde geçer mi. */
export function islemEslesir(islem, { ara = '', kanal = 'Tümü', tip = 'Tümü' } = {}) {
  // Birden çok kanala bölünmüş ödemeler "MEZAT / TOPTAN" gibi gelir; her kanalın filtresinde görünür.
  if (kanal !== 'Tümü' && !String(islem.kanal || '').split(' / ').includes(kanal)) return false;
  if (tip !== 'Tümü' && islem.tip !== tip) return false;
  const q = ara.trim().toLocaleLowerCase('tr-TR');
  if (!q) return true;
  return `${islem.cari || ''} ${islem.not || ''} ${islem.kanal || ''}`.toLocaleLowerCase('tr-TR').includes(q);
}
