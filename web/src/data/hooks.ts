import { useEffect, useState, useCallback } from 'react'
import { apiGet } from '../api/client'
import type { HaftalikOzetDto, IslemDto } from '../api/tipler'
import { haftalikAdapt, islemAdapt } from './adapters'
import type { HaftaBlok, Islem } from './types'

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
