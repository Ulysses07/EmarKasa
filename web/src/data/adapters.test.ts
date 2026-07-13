import { describe, it, expect } from 'vitest'
import { islemAdapt, haftalikAdapt, panelSeri, ayGrupAdapt, cizgiYol, carilerAdapt } from './adapters'
import type { IslemDto, HaftalikOzetDto, AylikRaporDto, CariDto } from '../api/tipler'

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

describe('cizgiYol', () => {
  // ax(i) = 120 + i*200  |  ayY(v) = 130 - (v/400000)*110
  // i=0 v=392200: ayY = 130 - (392200/400000)*110 = 130 - 107.855 = 22.145 → "22.1"
  // i=1 v=200000: ayY = 130 - (200000/400000)*110 = 130 - 55      = 75.0   → "75.0"
  it('iki grup için doğru SVG yolu üretir', () => {
    const dummy = { h: '0', top: '0', c: '', t: '' }
    const gruplar = [
      { ad: 'A', bars: [{ ...dummy, v: 392200 }, { ...dummy, v: 0 }, { ...dummy, v: 0 }] },
      { ad: 'B', bars: [{ ...dummy, v: 200000 }, { ...dummy, v: 0 }, { ...dummy, v: 0 }] },
    ]
    expect(cizgiYol(gruplar, 0)).toBe('M120,22.1 L320,75.0')
  })

  it('tek grup için M ile başlayan tek nokta üretir', () => {
    const dummy = { h: '0', top: '0', c: '', t: '' }
    const gruplar = [
      { ad: 'A', bars: [{ ...dummy, v: 400000 }, { ...dummy, v: 0 }, { ...dummy, v: 0 }] },
    ]
    // ayY(400000) = 130 - (400000/400000)*110 = 130 - 110 = 20.0
    expect(cizgiYol(gruplar, 0)).toBe('M120,20.0')
  })
})

describe('carilerAdapt', () => {
  it('cari başına işlem sayısı, son tarih, hacim ve mükerrer uyarısı üretir', () => {
    const cariler: CariDto[] = [
      { id: 1, ad: 'Yıldız Tarım', aktif: true },
      { id: 2, ad: 'Yıldız Tarım', aktif: true },
      { id: 3, ad: 'Pasif Cari', aktif: false },
    ]
    const islemler: IslemDto[] = [
      { id: 1, tarih: '2026-07-12', cari: 'Yıldız Tarım', tutarTl: 100, kanal: 'MEZAT', tip: 'Cari', not: null },
      { id: 2, tarih: '2026-07-10', cari: 'Yıldız Tarım', tutarTl: 50, kanal: 'MEZAT', tip: 'Cari', not: null },
    ]
    const v = carilerAdapt(cariler, islemler)
    const yildiz = v.find((c) => c.ad === 'Yıldız Tarım')!
    expect(yildiz.islem).toBe('2')
    expect(yildiz.hacim).toBe('150,00')
    expect(yildiz.son).toBe('12 Tem 2026')
    expect(yildiz.uyari).toBe(true)
    const pasif = v.find((c) => c.ad === 'Pasif Cari')!
    expect(pasif.durum).toBe('Pasif')
    expect(pasif.eylem).toBe('Aktifleştir')
  })
})
