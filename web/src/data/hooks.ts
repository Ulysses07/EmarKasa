import { useEffect, useState, useCallback } from 'react'
import { apiGet } from '../api/client'
import type { HaftalikOzetDto, IslemDto, AylikRaporDto } from '../api/tipler'
import type { CariDto, AyarlarDto } from '../api/tipler'
import { haftalikAdapt, islemAdapt, panelSeri, ayGrupAdapt, kanalAd, carilerAdapt, type PanelKaynak } from './adapters'
import type { HaftaBlok, Islem, AyGrup, Cari } from './types'

interface Durum<T> { veri: T | null; yukleniyor: boolean; hata: string | null; yenile: () => void }

function useVeri<T>(getir: () => Promise<T>, bagimlilik: unknown[] = []): Durum<T> {
  const [veri, setVeri] = useState<T | null>(null)
  const [yukleniyor, setYukleniyor] = useState(true)
  const [hata, setHata] = useState<string | null>(null)

  const yukle = useCallback(() => {
    let iptal = false
    setYukleniyor(true)
    setHata(null)
    getir()
      .then((d) => { if (!iptal) setVeri(d) })
      .catch((e) => { if (!iptal) setHata(String(e)) })
      .finally(() => { if (!iptal) setYukleniyor(false) })
    return () => { iptal = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, bagimlilik)

  useEffect(() => yukle(), [yukle])
  return { veri, yukleniyor, hata, yenile: yukle }
}

export function useHaftalik(): Durum<HaftaBlok[]> {
  return useVeri(async () => {
    const ham = await apiGet<HaftalikOzetDto[]>('/rapor/haftalik')
    // En yeni önce (mock.haftalar davranışı):
    return ham.map(haftalikAdapt).reverse()
  })
}

export function useIslemler(): Durum<Islem[]> {
  return useVeri(async () => {
    const ham = await apiGet<IslemDto[]>('/islemler')
    // En yeni önce:
    return ham.map(islemAdapt).reverse()
  })
}

export function usePanelSeri() {
  return useVeri(async () => {
    const ham = await apiGet<HaftalikOzetDto[]>('/rapor/haftalik')
    const kaynak: PanelKaynak[] = ham.map((h) => ({
      kasaDevir: h.kasaDevir,
      mezat: h.kanallar.find((k) => kanalAd(k.kanal) === 'MEZAT')?.devir ?? 0,
      perakende: h.kanallar.find((k) => kanalAd(k.kanal) === 'PERAKENDE')?.devir ?? 0,
      toptan: h.kanallar.find((k) => kanalAd(k.kanal) === 'TOPTAN')?.devir ?? 0,
    }))
    return panelSeri(kaynak)
  })
}

const AY_ADI = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık']

export function useAylik(): Durum<AyGrup[]> {
  return useVeri(async () => {
    const bugun = new Date()
    const hedefler = [2, 1, 0].map((geri) => {
      const d = new Date(bugun.getFullYear(), bugun.getMonth() - geri, 1)
      return { yil: d.getFullYear(), ay: d.getMonth() + 1 }
    })
    const raporlar = await Promise.all(
      hedefler.map((h) => apiGet<AylikRaporDto>(`/rapor/aylik?yil=${h.yil}&ay=${h.ay}`)),
    )
    return raporlar.map((r) => ayGrupAdapt(r, AY_ADI[r.ay - 1]))
  })
}

export function useCariler(): Durum<Cari[]> {
  return useVeri(async () => {
    const [cariler, islemler] = await Promise.all([
      apiGet<CariDto[]>('/cariler'),
      apiGet<IslemDto[]>('/islemler'),
    ])
    return carilerAdapt(cariler, islemler)
  })
}

export function useAyarlar(): Durum<AyarlarDto> {
  return useVeri(() => apiGet<AyarlarDto>('/ayarlar'))
}
