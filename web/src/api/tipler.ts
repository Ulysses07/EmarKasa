// Kasa.Api'nin döndürdüğü ham JSON şekilleri (camelCase, enum string, decimal→number).

export type GiderTipi = 'Cari' | 'SabitGider' | 'KrediKarti'

export interface KanalDto { id: number; ad: string; aktif: boolean; sira: number; acilisDevri: number }
export interface CariDto { id: number; ad: string; aktif: boolean }
export interface IslemDto {
  id: number; tarih: string; cari: string; tutarTl: number; kanal: string; tip: GiderTipi; not: string | null
}
export interface GelenDto { id: number; donemStart: string; kanal: string; tutarTl: number }

export interface DonemDto { start: string; end: string; yil: number; ay: number }

export interface KanalHaftalikDto { kanal: string; gelen: number; giden: number; sonuc: number; devir: number }
export interface HaftalikOzetDto {
  donem: DonemDto
  kanallar: KanalHaftalikDto[]
  toplamGelen: number
  toplamGiden: number
  kasaSonucu: number
  kasaDevir: number
}

export interface KanalAylikDto {
  kanal: string; gelen: number; cariGiden: number; sabitGider: number; krediKarti: number; ortakPay: number; aySonucu: number
}
export interface AylikRaporDto { yil: number; ay: number; kanallar: KanalAylikDto[] }

export interface KanalBakiyeDto { kanal: string; bakiye: number }
export interface PanelDto {
  guncelKasa: number; kanallar: KanalBakiyeDto[]; buHaftaSonucu: number; buAySonucu: number
}

export interface AyarlarDto { takipBaslangic: string; kasaAcilisDevri: number; izleyiciSifreVarMi: boolean }
