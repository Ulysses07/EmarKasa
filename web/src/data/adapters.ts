import { fmt } from '../format'
import { kanalRenk, type KanalAd } from '../theme'
import type { IslemDto, GiderTipi } from '../api/tipler'
import type { Islem } from './types'

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
