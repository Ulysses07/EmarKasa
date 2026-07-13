// Kasa Defteri örnek verisi — "Kasa Defteri.dc.html" DCLogic'inden birebir portlandı.
// Ham girdiler + türetilmiş devir zincirleri; gerçek backend gelene kadar UI'yı besler.

import { fmt, sfmt } from "../format";
import { color, kanalRenk, type KanalAd } from "../theme";

// ---- haftalık ham veri (gelen, giden) ----
type Hafta = {
  d: string;
  split: boolean;
  m: [number, number];
  p: [number, number];
  t: [number, number];
  o: number;
};

const raw: Hafta[] = [
  { d: "1–7 Haz 2026", split: false, m: [486200, 327400], p: [156900, 121300], t: [84500, 92100], o: 24800 },
  { d: "8–14 Haz 2026", split: false, m: [512700, 389500], p: [149200, 138600], t: [91800, 71200], o: 31200 },
  { d: "15–21 Haz 2026", split: false, m: [438900, 472300], p: [162400, 118900], t: [79600, 85400], o: 26500 },
  { d: "22–28 Haz 2026", split: false, m: [527400, 368200], p: [158700, 131400], t: [88200, 94600], o: 29700 },
  { d: "29–30 Haz 2026", split: true, m: [96400, 71800], p: [41300, 22100], t: [18600, 26300], o: 8400 },
  { d: "1–5 Tem 2026", split: true, m: [402600, 351900], p: [143800, 104200], t: [76400, 69800], o: 22900 },
  { d: "6–12 Tem 2026", split: false, m: [517300, 412600], p: [168200, 119400], t: [94100, 77300], o: 25300 },
];

// açılış devirleri
const OPEN = { cm: 412500, cp: 168400, ct: 96200, kasa: 648700 };

export type KanalSatir = {
  k: KanalAd;
  kbg: string;
  kc: string;
  kdot: string;
  gelen: string;
  giden: string;
  sonuc: string;
  sonucC: string;
  devir: string;
  devirC: string;
};

export type HaftaBlok = {
  donem: string;
  split: boolean;
  rows: KanalSatir[];
  ortak: string;
  tGelen: string;
  tGiden: string;
  tSonuc: string;
  tSonucC: string;
  tDevir: string;
};

const POS = color.pos;
const NEG = color.neg;
const INK = color.ink;

let cm = OPEN.cm;
let cp = OPEN.cp;
let ct = OPEN.ct;
let kasa = OPEN.kasa;

export const kasaSeri: number[] = [kasa];
export const mSeri: number[] = [cm];
export const pSeri: number[] = [cp];
export const tSeri: number[] = [ct];

const mk = (k: KanalAd, g: number, gid: number, s: number, dev: number): KanalSatir => ({
  k,
  kbg: kanalRenk[k].bg,
  kc: kanalRenk[k].c,
  kdot: kanalRenk[k].dot,
  gelen: fmt(g),
  giden: fmt(gid),
  sonuc: sfmt(s),
  sonucC: s < 0 ? NEG : POS,
  devir: fmt(dev),
  devirC: dev < 0 ? NEG : INK,
});

export const blocks: HaftaBlok[] = raw.map((w) => {
  const sm = w.m[0] - w.m[1];
  const sp = w.p[0] - w.p[1];
  const st = w.t[0] - w.t[1];
  cm += sm;
  cp += sp;
  ct += st;
  const kSonuc = sm + sp + st - w.o;
  kasa += kSonuc;
  kasaSeri.push(kasa);
  mSeri.push(cm);
  pSeri.push(cp);
  tSeri.push(ct);
  return {
    donem: w.d,
    split: w.split,
    rows: [mk("MEZAT", w.m[0], w.m[1], sm, cm), mk("PERAKENDE", w.p[0], w.p[1], sp, cp), mk("TOPTAN", w.t[0], w.t[1], st, ct)],
    ortak: fmt(w.o),
    tGelen: fmt(w.m[0] + w.p[0] + w.t[0]),
    tGiden: fmt(w.m[1] + w.p[1] + w.t[1] + w.o),
    tSonuc: sfmt(kSonuc),
    tSonucC: kSonuc < 0 ? NEG : POS,
    tDevir: fmt(kasa),
  };
});

// ---- haftalık özet (n hafta, en yeni önce) ----
export function haftalar(n: number): HaftaBlok[] {
  const k = Math.max(2, Math.min(5, n));
  return blocks.slice(-k).reverse();
}

// ---- trend grafik yolları (mobil) ----
const px = (i: number) => 14 + i * 44;
const kasaY = (v: number) => 132 - ((v - 600000) / 750000) * 126;
const kanalY = (v: number) => 132 - (v / 1050000) * 126;
const path = (vals: number[], yf: (v: number) => number) =>
  vals.map((v, i) => (i ? "L" : "M") + px(i) + "," + yf(v).toFixed(1)).join(" ");

export const kasaPath = path(kasaSeri, kasaY);
export const kasaArea = kasaPath + " L322,132 L14,132 Z";
export const kasaDotY = kasaY(kasaSeri[kasaSeri.length - 1]).toFixed(1);
export const mzPath = path(mSeri, kanalY);
export const prPath = path(pSeri, kanalY);
export const tpPath = path(tSeri, kanalY);
export const mzDotY = kanalY(mSeri[mSeri.length - 1]).toFixed(1);
export const prDotY = kanalY(pSeri[pSeri.length - 1]).toFixed(1);
export const tpDotY = kanalY(tSeri[tSeri.length - 1]).toFixed(1);

// ---- aylık grafik / rapor ----
type Ay = { ad: string; v: [number, number, number] };
export const aylar: Ay[] = [
  { ad: "Nisan", v: [301400, 74800, 12400] },
  { ad: "Mayıs", v: [348700, 88300, -18200] },
  { ad: "Haziran", v: [392200, 96000, -47100] },
];

const cols = [kanalRenk.MEZAT.dot, kanalRenk.PERAKENDE.dot, kanalRenk.TOPTAN.dot];
const adlar: KanalAd[] = ["MEZAT", "PERAKENDE", "TOPTAN"];

export type AyBar = { h: string; top: string; c: string; t: string };
export type AyGrup = { ad: string; bars: AyBar[] };

export const ayGruplar: AyGrup[] = aylar.map((m) => ({
  ad: m.ad,
  bars: m.v.map((v, i) => {
    const h = Math.max(3, (Math.abs(v) / 400000) * 110);
    return {
      h: h.toFixed(1),
      top: (v >= 0 ? 130 - h : 131).toFixed(1),
      c: cols[i],
      t: adlar[i] + " " + m.ad + ": " + sfmt(v) + " ₺",
    };
  }),
}));

const ax = (i: number) => 120 + i * 200;
const ayY = (v: number) => 130 - (v / 400000) * 110;
const ayPath = (idx: number) =>
  aylar.map((m, i) => (i ? "L" : "M") + ax(i) + "," + ayY(m.v[idx]).toFixed(1)).join(" ");
export const mzAyPath = ayPath(0);
export const prAyPath = ayPath(1);
export const tpAyPath = ayPath(2);

// ---- işlemler ----
type TxRow = [string, string, number, KanalAd, string, string];
const tx: TxRow[] = [
  ["12.07", "Yıldız Tarım Ürünleri", 84600, "MEZAT", "Cari", "Komisyon ödemesi"],
  ["12.07", "Halkbank Kredi Kartı", 42350, "Ortak", "Kredi Kartı", "Haziran ekstresi"],
  ["11.07", "Mehmet Kaya", 31200, "PERAKENDE", "Cari", ""],
  ["11.07", "Anadolu Nakliyat", 18750, "MEZAT", "Cari", "Sevkiyat · 3 araç"],
  ["10.07", "Baş-Kar Gıda", 56400, "TOPTAN", "Cari", ""],
  ["10.07", "TEDAŞ", 9840, "Ortak", "Sabit Gider", "Depo elektrik"],
  ["09.07", "Öz Ege Ambalaj", 22150, "PERAKENDE", "Cari", "Kasa + poşet"],
  ["09.07", "Karadeniz Su Ürünleri", 74300, "MEZAT", "Cari", ""],
  ["08.07", "Aslan Emlak", 45000, "Ortak", "Sabit Gider", "Temmuz depo kirası"],
  ["07.07", "Vakıfbank Kredi Kartı", 28600, "MEZAT", "Kredi Kartı", "Akaryakıt"],
  ["06.07", "Hüseyin Demirtaş", 12500, "TOPTAN", "Cari", "İade"],
];

export type Islem = {
  tarih: string;
  cari: string;
  tutar: string;
  kanal: KanalAd;
  kbg: string;
  kc: string;
  kdot: string;
  tip: string;
  not: string;
};

export const islemler: Islem[] = tx.map((r) => ({
  tarih: r[0],
  cari: r[1],
  tutar: fmt(r[2]),
  kanal: r[3],
  kbg: kanalRenk[r[3]].bg,
  kc: kanalRenk[r[3]].c,
  kdot: kanalRenk[r[3]].dot,
  tip: r[4],
  not: r[5] || "—",
}));

// ---- cariler ----
type CariRow = [string, string, number, string, number, string, boolean];
const cr: CariRow[] = [
  ["Yıldız Tarım Ürünleri", "YT", 38, "12 Tem 2026", 1842300, "Aktif", false],
  ["Karadeniz Su Ürünleri", "KS", 31, "09 Tem 2026", 1246750, "Aktif", false],
  ["Baş-Kar Gıda", "BG", 24, "10 Tem 2026", 897400, "Aktif", false],
  ["Mehmet Kaya", "MK", 19, "11 Tem 2026", 412800, "Aktif", true],
  ["M. Kaya", "MK", 3, "14 May 2026", 61500, "Aktif", true],
  ["Anadolu Nakliyat", "AN", 17, "11 Tem 2026", 356250, "Aktif", false],
  ["Öz Ege Ambalaj", "ÖE", 12, "09 Tem 2026", 184900, "Aktif", false],
  ["Yılmaz Balıkçılık", "YB", 9, "28 Haz 2026", 152600, "Pasif", false],
];

export type Cari = {
  ad: string;
  bas: string;
  islem: string;
  son: string;
  hacim: string;
  durum: string;
  uyari: boolean;
  dbg: string;
  dc: string;
  op: string;
  eylem: string;
};

export const cariler: Cari[] = cr.map((r) => ({
  ad: r[0],
  bas: r[1],
  islem: String(r[2]),
  son: r[3],
  hacim: fmt(r[4]),
  durum: r[5],
  uyari: r[6],
  dbg: r[5] === "Aktif" ? "#E3F1E8" : "#EDEAE0",
  dc: r[5] === "Aktif" ? "#1B7A4E" : "#8A8F80",
  op: r[5] === "Aktif" ? "1" : "0.55",
  eylem: r[5] === "Aktif" ? "Pasifleştir" : "Aktifleştir",
}));
