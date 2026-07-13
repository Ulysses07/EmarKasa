import { fmt, sfmt } from '../format'
import { kanalRenk, color, type KanalAd } from '../theme'
import type { IslemDto, GiderTipi, HaftalikOzetDto, KanalHaftalikDto, AylikRaporDto } from '../api/tipler'
import type { Islem, HaftaBlok, KanalSatir, AyGrup } from './types'
import { donemEtiket } from './donem'

// "2026-07-12" → "12.07"
export function tarihKisa(iso: string): string {
  const [, ay, gun] = iso.split('-')
  return `${gun}.${ay}`
}

// Kanal adını KanalAd'e daralt (bilinmeyen → "Ortak" güvenli varsayılan renk için).
export function kanalAd(k: string): KanalAd {
  return k === 'MEZAT' || k === 'PERAKENDE' || k === 'TOPTAN' ? k : 'Ortak'
}

const tipEtiket: Record<GiderTipi, string> = {
  Cari: 'Cari',
  SabitGider: 'Sabit Gider',
  KrediKarti: 'Kredi Kartı',
}

export function islemAdapt(dto: IslemDto): Islem {
  const k = kanalAd(dto.kanal)
  const renk = kanalRenk[k]
  return {
    tarih: tarihKisa(dto.tarih),
    cari: dto.cari,
    tutar: fmt(dto.tutarTl),
    kanal: k,
    kbg: renk.bg,
    kc: renk.c,
    kdot: renk.dot,
    tip: tipEtiket[dto.tip],
    not: dto.not && dto.not.trim() ? dto.not : '—',
  }
}

function kanalSatir(k: KanalHaftalikDto): KanalSatir {
  const ad = kanalAd(k.kanal)
  const renk = kanalRenk[ad]
  return {
    k: ad, kbg: renk.bg, kc: renk.c, kdot: renk.dot,
    gelen: fmt(k.gelen),
    giden: fmt(k.giden),
    sonuc: sfmt(k.sonuc),
    sonucC: k.sonuc < 0 ? color.neg : color.pos,
    devir: fmt(k.devir),
    devirC: k.devir < 0 ? color.neg : color.ink,
  }
}

export function haftalikAdapt(dto: HaftalikOzetDto): HaftaBlok {
  const cariToplam = dto.kanallar.reduce((s, k) => s + k.giden, 0)
  const ortak = dto.toplamGiden - cariToplam
  const split = dto.donem.start.slice(0, 7) !== dto.donem.end.slice(0, 7)
  return {
    donem: donemEtiket(dto.donem.start, dto.donem.end),
    split,
    rows: dto.kanallar.map(kanalSatir),
    ortak: fmt(ortak),
    tGelen: fmt(dto.toplamGelen),
    tGiden: fmt(dto.toplamGiden),
    tSonuc: sfmt(dto.kasaSonucu),
    tSonucC: dto.kasaSonucu < 0 ? color.neg : color.pos,
    tDevir: fmt(dto.kasaDevir),
  }
}

// ---- Panel trend grafiği (mock.ts px/kasaY/kanalY formülleriyle birebir) ----
export interface PanelKaynak { kasaDevir: number; mezat: number; perakende: number; toptan: number }

const px = (i: number) => 14 + i * 44
const kasaY = (v: number) => 132 - ((v - 600000) / 750000) * 126
const kanalY = (v: number) => 132 - (v / 1050000) * 126
const yol = (vals: number[], yf: (v: number) => number) =>
  vals.map((v, i) => (i ? 'L' : 'M') + px(i) + ',' + yf(v).toFixed(1)).join(' ')

export function panelSeri(bloklar: PanelKaynak[]) {
  const kasaSeri = bloklar.map((b) => b.kasaDevir)
  const mSeri = bloklar.map((b) => b.mezat)
  const pSeri = bloklar.map((b) => b.perakende)
  const tSeri = bloklar.map((b) => b.toptan)
  return {
    kasaSeri,
    kasaPath: yol(kasaSeri, kasaY),
    kasaArea: yol(kasaSeri, kasaY) + ' L322,132 L14,132 Z',
    kasaDotY: kasaY(kasaSeri[kasaSeri.length - 1] ?? 0).toFixed(1),
    mzPath: yol(mSeri, kanalY),
    prPath: yol(pSeri, kanalY),
    tpPath: yol(tSeri, kanalY),
    mzDotY: kanalY(mSeri[mSeri.length - 1] ?? 0).toFixed(1),
    prDotY: kanalY(pSeri[pSeri.length - 1] ?? 0).toFixed(1),
    tpDotY: kanalY(tSeri[tSeri.length - 1] ?? 0).toFixed(1),
  }
}

// ---- Aylık rapor çizgi overlay SVG yolu (AylikRapor.tsx ax/ayY mock formülüyle birebir) ----
export function cizgiYol(gruplar: AyGrup[], kanalIdx: number): string {
  const ax = (i: number) => 120 + i * 200
  const ayY = (v: number) => 130 - (v / 400000) * 110
  return gruplar
    .map((g, i) => (i ? 'L' : 'M') + ax(i) + ',' + ayY(g.bars[kanalIdx].v).toFixed(1))
    .join(' ')
}

// ---- Aylık rapor kanal çubukları (mock.ts ayGruplar formülüyle birebir) ----
export function ayGrupAdapt(rapor: AylikRaporDto, ad: string): AyGrup {
  const cols = [kanalRenk.MEZAT.dot, kanalRenk.PERAKENDE.dot, kanalRenk.TOPTAN.dot]
  const adlar = ['MEZAT', 'PERAKENDE', 'TOPTAN']
  return {
    ad,
    bars: adlar.map((kn, i) => {
      const v = rapor.kanallar.find((k) => k.kanal === kn)?.aySonucu ?? 0
      const h = Math.max(3, (Math.abs(v) / 400000) * 110)
      return {
        h: h.toFixed(1),
        top: (v >= 0 ? 130 - h : 131).toFixed(1),
        c: cols[i],
        t: `${kn} ${ad}: ${sfmt(v)} ₺`,
        v,
      }
    }),
  }
}
