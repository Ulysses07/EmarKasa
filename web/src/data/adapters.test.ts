import { describe, it, expect } from 'vitest'
import { islemAdapt } from './adapters'
import type { IslemDto } from '../api/tipler'

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
