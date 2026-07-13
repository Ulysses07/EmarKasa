import { describe, it, expect } from 'vitest'
import { islemAdapt, haftalikAdapt, panelSeri, ayGrupAdapt } from './adapters'
import type { IslemDto, HaftalikOzetDto, AylikRaporDto } from '../api/tipler'

describe('islemAdapt', () => {
  it('ham işlemi view-modele çevirir (tarih gg.aa, tutar TL, kanal rengi, tip etiketi)', () => {
    const dto: IslemDto = {
      id: 1, tarih: '2026-07-12', cari: 'Yıldız Tarım', tutarTl: 84600,
      kanal: 'MEZAT', tip: 'Cari', not: 'Komisyon',
    }
    const v = islemAdapt(dto)
    expect(v.tarih).toBe('12.07')
    expect(v.cari).toBe('Yıldız Tarım')
    expect(v.tutar).toBe('84.600,00')
    expect(v.kanal).toBe('MEZAT')
    expect(v.tip).toBe('Cari')
    expect(v.not).toBe('Komisyon')
    expect(v.kdot).toBe('#C98A12') // kanalRenk.MEZAT.dot
  })

  it('KrediKarti enum → "Kredi Kartı" etiketi, boş not → tire', () => {
    const dto: IslemDto = { id: 2, tarih: '2026-07-07', cari: 'Banka', tutarTl: 28600, kanal: 'Ortak', tip: 'KrediKarti', not: null }
    const v = islemAdapt(dto)
    expect(v.tip).toBe('Kredi Kartı')
    expect(v.not).toBe('—')
    expect(v.kanal).toBe('Ortak')
  })
})

describe('haftalikAdapt', () => {
  const dto: HaftalikOzetDto = {
    donem: { start: '2026-06-29', end: '2026-06-30', yil: 2026, ay: 6 },
    kanallar: [
      { kanal: 'MEZAT', gelen: 289425, giden: 1308800, sonuc: -1019375, devir: 3971677 },
      { kanal: 'PERAKENDE', gelen: 271006, giden: 1221374, sonuc: -950368, devir: 1063148 },
      { kanal: 'TOPTAN', gelen: 207000, giden: 360000, sonuc: -153000, devir: 466647 },
    ],
    toplamGelen: 767431, toplamGiden: 3345495, kasaSonucu: -2578064, kasaDevir: 328989.21,
  }

  it('dönemi tarih aralığına, kanalları satırlara, işaretli sonuçlara çevirir', () => {
    const b = haftalikAdapt(dto)
    expect(b.rows).toHaveLength(3)
    const mezat = b.rows[0]
    expect(mezat.k).toBe('MEZAT')
    expect(mezat.gelen).toBe('289.425,00')
    expect(mezat.sonuc).toBe('-1.019.375,00')
    expect(mezat.sonucC).toBe('#C13A2E') // negatif → color.neg
    expect(mezat.devir).toBe('3.971.677,00')
    expect(b.tGelen).toBe('767.431,00')
    expect(b.tDevir).toBe('328.989,21')
  })

  it('ortak = toplamGiden - kanal cari gidenleri toplamı', () => {
    const b = haftalikAdapt(dto)
    // 3345495 - (1308800+1221374+360000) = 455321
    expect(b.ortak).toBe('455.321,00')
  })
})

describe('panelSeri', () => {
  it('haftalık kasaDevir dizisinden kasa serisi ve SVG yolu üretir', () => {
    const bloklar = [
      { kasaDevir: 648700, mezat: 412500, perakende: 168400, toptan: 96200 },
      { kasaDevir: 700000, mezat: 500000, perakende: 180000, toptan: 100000 },
    ]
    const s = panelSeri(bloklar)
    expect(s.kasaSeri).toEqual([648700, 700000])
    expect(s.kasaPath.startsWith('M')).toBe(true)
    expect(s.kasaPath.split(' ')).toHaveLength(2)
  })
})

describe('ayGrupAdapt', () => {
  it('aylık raporu kanal çubuklarına çevirir', () => {
    const rapor: AylikRaporDto = {
      yil: 2026, ay: 6,
      kanallar: [
        { kanal: 'MEZAT', gelen: 0, cariGiden: 0, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: 392200 },
        { kanal: 'PERAKENDE', gelen: 0, cariGiden: 0, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: 96000 },
        { kanal: 'TOPTAN', gelen: 0, cariGiden: 0, sabitGider: 0, krediKarti: 0, ortakPay: 0, aySonucu: -47100 },
      ],
    }
    const g = ayGrupAdapt(rapor, 'Haziran')
    expect(g.ad).toBe('Haziran')
    expect(g.bars).toHaveLength(3)
    expect(g.bars[0].t).toContain('MEZAT Haziran')
    expect(g.bars[0].v).toBe(392200)
  })
})
