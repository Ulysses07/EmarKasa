import { fmt, sfmt } from '../format'
import { kanalRenk, color, type KanalAd } from '../theme'
import type { IslemDto, GiderTipi, HaftalikOzetDto, KanalHaftalikDto } from '../api/tipler'
import type { Islem, HaftaBlok, KanalSatir } from './types'
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
