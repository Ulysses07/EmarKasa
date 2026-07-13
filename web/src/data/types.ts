import type { KanalAd } from '../theme'

export type KanalSatir = {
  k: KanalAd; kbg: string; kc: string; kdot: string
  gelen: string; giden: string; sonuc: string; sonucC: string; devir: string; devirC: string
}

export type HaftaBlok = {
  donem: string; split: boolean; rows: KanalSatir[]
  ortak: string; tGelen: string; tGiden: string; tSonuc: string; tSonucC: string; tDevir: string
}

export type Islem = {
  tarih: string; cari: string; tutar: string; kanal: KanalAd
  kbg: string; kc: string; kdot: string; tip: string; not: string
}

export type Cari = {
  ad: string; bas: string; islem: string; son: string; hacim: string; durum: string
  uyari: boolean; dbg: string; dc: string; op: string; eylem: string
}

export type AyBar = { h: string; top: string; c: string; t: string; v: number }
export type AyGrup = { ad: string; bars: AyBar[] }
