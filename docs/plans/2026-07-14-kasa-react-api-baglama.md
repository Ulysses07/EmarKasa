# Kasa React ↔ API Bağlama Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mevcut React SPA'yı (7 ekran, şu an `data/mock.ts` ile besleniyor) `Kasa.Api` REST API'sine bağla; gerçek giriş + veri + CRUD çalışsın.

**Architecture:** Ham API yanıtlarını (camelCase, `decimal`, string-enum) ekranların zaten tükettiği view-model tiplerine (`HaftaBlok`, `Islem`, `Cari`, `AyGrup`…) çeviren **saf adapter fonksiyonları** yazılır ve vitest ile birim test edilir. Her ekran, `../data/mock` import'unu bir veri hook'uyla değiştirir + yükleniyor durumu ekler. Biçimlendirme/renk mantığı adapter'lara taşınır (mock.ts'ten birebir korunur). Dev'de Vite proxy `/api`'yi yerel API'ye yönlendirir → same-origin; prod'da Caddy aynı işi yapar → **CORS gerekmez**.

**Tech Stack:** React 19 + react-router 7 + Vite 8 + TypeScript; native `fetch` (`credentials: "include"`); vitest (yeni eklenir) birim testleri için.

Bu, Kasa'nın 4 planlık setinin **React↔API bağlama** adımıdır (Plan 1 hesap motoru ✅, Plan 2 API+auth ✅, Plan 3 React mock ✅). Bunu **Dağıtım planı** (`2026-07-14-kasa-dagitim.md`) izler. Tasarım referansı: `docs/specs/2026-07-13-kasa-defteri-design.md`.

**Kapsam kararları (mevcut alanlarla çöz — yeni API ucu YOK):**
- Panel/Aylık trend serileri `/api/rapor/haftalik` ve birkaç `/api/rapor/aylik` çağrısından türetilir.
- Cariler türetilmiş alanları (`islem` sayısı, `son` tarih, `hacim`) `/api/cariler` + `/api/islemler` toplamından hesaplanır. `uyari` (mükerrer) = birebir aynı ada sahip >1 kayıt.
- Auth cookie tabanlı; fetch `credentials: "include"` ile çağırır.

---

## Dosya Yapısı

- `web/src/api/client.ts` — fetch sarmalayıcı (`apiGet/apiPost/apiPut/apiDelete`, `credentials: "include"`, hata fırlatma)
- `web/src/api/tipler.ts` — ham API DTO TypeScript arayüzleri (camelCase)
- `web/src/data/types.ts` — view-model tipleri (mock.ts'ten taşınan `KanalSatir/HaftaBlok/Islem/Cari/AyBar/AyGrup`)
- `web/src/data/adapters.ts` — saf dönüştürücüler: API DTO → view-model
- `web/src/data/hooks.ts` — React hook'ları (`useHaftalik`, `useIslemler`, `useCariler`, `usePanel`, `useAylik`, `useAyarlar`) fetch + adapter + loading/error
- `web/src/api/auth.ts` — `login/logout/me`
- `web/src/data/adapters.test.ts`, `web/src/api/client.test.ts` — vitest birim testleri
- Değişecek: 7 ekran (`screens/*.tsx`), `App.tsx` (auth guard), `vite.config.ts` (proxy + test), `package.json` (vitest), `web/.env.development`
- Silinecek (son task): `web/src/data/mock.ts`

**Genel konvansiyonlar:**
- Komutlar `web/` altında çalışır: `cd <repo>\web`.
- Para/tarih biçimi mevcut `format.ts` (`fmt`/`sfmt`) ile birebir korunur.
- Her task sonunda commit; mesaj Türkçe, imperative, sonuna
  `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>`.
- Renkler `theme.ts`'ten (`kanalRenk`, `color.pos`, `color.neg`, `color.ink`) alınır.
- API camelCase döner; enum string ("Cari"/"SabitGider"/"KrediKarti").

---

### Task 0: Vitest + API client iskeleti + dev proxy

**Files:**
- Modify: `web/package.json`, `web/vite.config.ts`
- Create: `web/.env.development`, `web/src/api/client.ts`, `web/src/api/client.test.ts`

- [ ] **Step 1: Vitest'i ekle**

Run:
```bash
cd web && npm install -D vitest@^3 happy-dom@^15
```
Not: Sürüm bulunamazsa `npm install -D vitest happy-dom` (en son stable). happy-dom hafif DOM; şu an saf-fonksiyon testleri için gerekmiyor ama ileride ekran testine kapı bırakır.

- [ ] **Step 2: `package.json`'a test script'i ekle**

`web/package.json` `scripts` bloğuna (mevcut script'lerin yanına) ekle:
```json
    "test": "vitest run",
    "test:watch": "vitest"
```

- [ ] **Step 3: `vite.config.ts`'i proxy + test config ile güncelle**

`web/vite.config.ts` (tüm dosya):
```typescript
/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Dev: /api çağrılarını yerel Kasa.Api'ye yönlendir (same-origin → CORS yok).
      // API'yi şu komutla çalıştır: dotnet run --project ../Kasa.Api --urls http://localhost:5199
      '/api': {
        target: 'http://localhost:5199',
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'happy-dom',
    globals: true,
  },
})
```

- [ ] **Step 4: `.env.development` oluştur**

`web/.env.development`:
```
# Dev'de boş bırakılır → api client "/api" göreli yolunu kullanır, Vite proxy'ye düşer.
# Prod'da build zamanı ayarlanmaz; Caddy same-origin /api sağlar.
VITE_API_URL=
```

- [ ] **Step 5: Failing test yaz**

`web/src/api/client.test.ts`:
```typescript
import { describe, it, expect, vi, afterEach } from 'vitest'
import { apiGet, apiPost, ApiError } from './client'

afterEach(() => vi.restoreAllMocks())

describe('api client', () => {
  it('apiGet başarılı JSON döner ve credentials include gönderir', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ x: 1 }), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const data = await apiGet<{ x: number }>('/deneme')

    expect(data).toEqual({ x: 1 })
    expect(fetchMock).toHaveBeenCalledWith('/api/deneme', expect.objectContaining({ credentials: 'include' }))
  })

  it('401 olunca ApiError(401) fırlatır', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })))
    await expect(apiGet('/korumali')).rejects.toMatchObject({ status: 401 } satisfies Partial<ApiError>)
  })

  it('apiPost gövdeyi JSON serileştirir', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await apiPost('/kayit', { ad: 'X' })

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/kayit',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ ad: 'X' }),
        headers: expect.objectContaining({ 'Content-Type': 'application/json' }),
      }),
    )
  })
})
```

- [ ] **Step 6: Test başarısız olsun**

Run: `npm run test`
Expected: FAIL — `./client` yok / `apiGet` tanımsız.

- [ ] **Step 7: API client'ı yaz**

`web/src/api/client.ts`:
```typescript
// Ham fetch sarmalayıcı. Base URL: VITE_API_URL (boşsa "/api" → Vite proxy / Caddy same-origin).
// Tüm çağrılar cookie auth için credentials:"include" gönderir.

const BASE = (import.meta.env.VITE_API_URL ?? '') + '/api'

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message)
    this.name = 'ApiError'
  }
}

async function istek<T>(yol: string, init: RequestInit): Promise<T> {
  const resp = await fetch(BASE + yol, { credentials: 'include', ...init })
  if (!resp.ok) throw new ApiError(resp.status, `${init.method ?? 'GET'} ${yol} → ${resp.status}`)
  const ct = resp.headers.get('Content-Type') ?? ''
  if (resp.status === 204 || !ct.includes('application/json')) return undefined as T
  return (await resp.json()) as T
}

export const apiGet = <T>(yol: string) => istek<T>(yol, { method: 'GET' })

export const apiPost = <T>(yol: string, govde?: unknown) =>
  istek<T>(yol, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: govde === undefined ? undefined : JSON.stringify(govde),
  })

export const apiPut = <T>(yol: string, govde?: unknown) =>
  istek<T>(yol, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: govde === undefined ? undefined : JSON.stringify(govde),
  })

export const apiDelete = (yol: string) => istek<void>(yol, { method: 'DELETE' })
```

- [ ] **Step 8: Test geçsin**

Run: `npm run test`
Expected: PASS (3 test).

- [ ] **Step 9: Commit**

```bash
git add web/package.json web/package-lock.json web/vite.config.ts web/.env.development web/src/api/client.ts web/src/api/client.test.ts
git commit -m "chore(web): vitest + fetch API client + dev proxy iskeleti"
```

---

### Task 1: Ham API DTO tipleri + view-model tiplerini ayır

Ekranların tükettiği view-model tiplerini `mock.ts`'ten bağımsız bir modüle taşı ki hem mock hem adapter aynı tipi paylaşsın.

**Files:**
- Create: `web/src/api/tipler.ts`, `web/src/data/types.ts`
- Modify: `web/src/data/mock.ts` (tipleri types.ts'ten import et)

- [ ] **Step 1: Ham API DTO arayüzlerini yaz**

`web/src/api/tipler.ts`:
```typescript
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
```

- [ ] **Step 2: View-model tiplerini taşı**

`web/src/data/types.ts`:
```typescript
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

export type AyBar = { h: string; top: string; c: string; t: string }
export type AyGrup = { ad: string; bars: AyBar[] }
```

- [ ] **Step 3: `mock.ts`'i tipleri import edecek şekilde güncelle**

`web/src/data/mock.ts` içinde bu tiplerin yerel `export type KanalSatir = …`, `HaftaBlok`, `Islem`, `Cari`, `AyBar`, `AyGrup` tanımlarını SİL ve dosyanın başına ekle:
```typescript
import type { KanalSatir, HaftaBlok, Islem, Cari, AyBar, AyGrup } from './types'
export type { KanalSatir, HaftaBlok, Islem, Cari, AyBar, AyGrup } from './types'
```
(Böylece ekranların `import { Islem } from "../data/mock"` gibi mevcut import'ları kırılmaz.)

- [ ] **Step 4: Derleme + test kontrolü**

Run: `npm run build && npm run test`
Expected: build succeeded; mevcut testler PASS (3).

- [ ] **Step 5: Commit**

```bash
git add web/src/api/tipler.ts web/src/data/types.ts web/src/data/mock.ts
git commit -m "refactor(web): API DTO tipleri + view-model tiplerini ayır"
```

---

### Task 2: Ortak biçim/renk yardımcıları + Islem adapter

**Files:**
- Create: `web/src/data/adapters.ts`, `web/src/data/adapters.test.ts`

- [ ] **Step 1: Failing test yaz**

`web/src/data/adapters.test.ts`:
```typescript
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
```

- [ ] **Step 2: Test başarısız olsun**

Run: `npm run test -- adapters`
Expected: FAIL — `./adapters` yok.

- [ ] **Step 3: Adapter + yardımcıları yaz**

`web/src/data/adapters.ts`:
```typescript
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
```

- [ ] **Step 4: Test geçsin**

Run: `npm run test -- adapters`
Expected: PASS (2).

- [ ] **Step 5: Commit**

```bash
git add web/src/data/adapters.ts web/src/data/adapters.test.ts
git commit -m "feat(web): biçim/renk yardımcıları + Islem adapter"
```

---

### Task 3: Haftalık adapter (HaftalikOzetDto[] → HaftaBlok[])

**Files:**
- Modify: `web/src/data/adapters.ts`, `web/src/data/adapters.test.ts`

- [ ] **Step 1: Failing test ekle**

`web/src/data/adapters.test.ts` sonuna ekle:
```typescript
import { haftalikAdapt } from './adapters'
import type { HaftalikOzetDto } from '../api/tipler'

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
```

- [ ] **Step 2: Test başarısız olsun**

Run: `npm run test -- adapters`
Expected: FAIL — `haftalikAdapt` yok.

- [ ] **Step 3: `haftalikAdapt`'ı yaz**

`web/src/data/adapters.ts` sonuna ekle (üstteki import'lara `sfmt` ve `color`, tipleri genişlet):
```typescript
// dosya başındaki import'ları şu hale getir:
//   import { fmt, sfmt } from '../format'
//   import { kanalRenk, color, type KanalAd } from '../theme'
//   import type { IslemDto, GiderTipi, HaftalikOzetDto, KanalHaftalikDto } from '../api/tipler'
//   import type { Islem, HaftaBlok, KanalSatir } from './types'
import { donemEtiket } from './donem'

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
```

- [ ] **Step 4: Dönem etiketi yardımcısını yaz**

`web/src/data/donem.ts`:
```typescript
// "2026-06-29" + "2026-06-30" → "29–30 Haz 2026"
const AYLAR = ['Oca', 'Şub', 'Mar', 'Nis', 'May', 'Haz', 'Tem', 'Ağu', 'Eyl', 'Eki', 'Kas', 'Ara']

export function donemEtiket(startIso: string, endIso: string): string {
  const [sy, sm, sd] = startIso.split('-').map(Number)
  const [ey, em, ed] = endIso.split('-').map(Number)
  const gun = (n: number) => String(n)
  if (sy === ey && sm === em) return `${gun(sd)}–${gun(ed)} ${AYLAR[sm - 1]} ${sy}`
  if (sy === ey) return `${gun(sd)} ${AYLAR[sm - 1]} – ${gun(ed)} ${AYLAR[em - 1]} ${sy}`
  return `${gun(sd)} ${AYLAR[sm - 1]} ${sy} – ${gun(ed)} ${AYLAR[em - 1]} ${ey}`
}
```

- [ ] **Step 5: Test geçsin**

Run: `npm run test -- adapters`
Expected: PASS (haftalik dahil hepsi).

- [ ] **Step 6: Commit**

```bash
git add web/src/data/adapters.ts web/src/data/adapters.test.ts web/src/data/donem.ts
git commit -m "feat(web): haftalık özet adapter + dönem etiketi"
```

---

### Task 4: Veri hook'ları + auth modülü

**Files:**
- Create: `web/src/api/auth.ts`, `web/src/data/hooks.ts`

- [ ] **Step 1: Auth modülünü yaz**

`web/src/api/auth.ts`:
```typescript
import { apiPost, apiGet, ApiError } from './client'

export type Rol = 'editor' | 'viewer'

export async function login(kullanici: string | null, sifre: string): Promise<Rol> {
  const r = await apiPost<{ rol: Rol }>('/auth/login', { kullanici, sifre })
  return r.rol
}

export async function logout(): Promise<void> {
  await apiPost('/auth/logout')
}

// Oturum yoksa null döner (401 yutulur).
export async function me(): Promise<Rol | null> {
  try {
    const r = await apiGet<{ rol: Rol }>('/auth/me')
    return r.rol
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) return null
    throw e
  }
}
```

- [ ] **Step 2: Generic fetch hook + veri hook'larını yaz**

`web/src/data/hooks.ts`:
```typescript
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
```

- [ ] **Step 3: Derleme kontrolü**

Run: `npm run build`
Expected: build succeeded (henüz ekranlar bağlanmadı; hook'lar kullanılmasa da derlenmeli — kullanılmıyan uyarısı olabilir, hata değil).

- [ ] **Step 4: Commit**

```bash
git add web/src/api/auth.ts web/src/data/hooks.ts
git commit -m "feat(web): auth modülü + veri hook altyapısı (haftalık, işlemler)"
```

---

### Task 5: Login ekranını gerçek girişe bağla + auth guard

**Files:**
- Modify: `web/src/screens/Login.tsx`, `web/src/App.tsx`
- Create: `web/src/api/AuthContext.tsx`

- [ ] **Step 1: AuthContext'i yaz**

`web/src/api/AuthContext.tsx`:
```typescript
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { me, type Rol } from './auth'

interface AuthDurum { rol: Rol | null; yukleniyor: boolean; ayarla: (r: Rol | null) => void }
const Ctx = createContext<AuthDurum>({ rol: null, yukleniyor: true, ayarla: () => {} })

export function AuthProvider({ children }: { children: ReactNode }) {
  const [rol, setRol] = useState<Rol | null>(null)
  const [yukleniyor, setYukleniyor] = useState(true)
  useEffect(() => {
    me().then(setRol).finally(() => setYukleniyor(false))
  }, [])
  return <Ctx.Provider value={{ rol, yukleniyor, ayarla: setRol }}>{children}</Ctx.Provider>
}

export const useAuth = () => useContext(Ctx)
```

- [ ] **Step 2: `Login.tsx`'i gerçek girişe bağla**

`web/src/screens/Login.tsx`'i oku. `navigate("/")` çağıran giriş butonunun onClick'ini şu mantıkla değiştir (kullanıcı/şifre input'larını state'e bağla, editör/izleyici sekmesine göre `kullanici` gönder):
```typescript
// importlara ekle:
import { useState } from 'react'
import { login } from '../api/auth'
import { useAuth } from '../api/AuthContext'
// bileşen içinde:
const { ayarla } = useAuth()
const [kullanici, setKullanici] = useState('')
const [sifre, setSifre] = useState('')
const [hata, setHata] = useState<string | null>(null)
// editör sekmesi seçiliyken kullanici gönder; izleyici sekmesinde null gönder (sadece şifre):
async function girisYap(editorMu: boolean) {
  try {
    const rol = await login(editorMu ? kullanici : null, sifre)
    ayarla(rol)
    navigate(rol === 'viewer' ? '/panel' : '/')
  } catch {
    setHata('Kullanıcı adı veya şifre hatalı.')
  }
}
```
Not: mevcut hardcoded `defaultValue="selim"` kaldır; input'ları `value={kullanici} onChange={e => setKullanici(e.target.value)}` yap. `hata` doluysa formun altında `color.neg` ile göster.

- [ ] **Step 3: `App.tsx`'e AuthProvider + guard ekle**

`web/src/App.tsx` (tüm dosya):
```typescript
import type { ReactNode } from 'react'
import { Routes, Route, Navigate } from 'react-router-dom'
import DesktopShell from './components/DesktopShell'
import IslemDefter from './screens/IslemDefter'
import HaftalikOzet from './screens/HaftalikOzet'
import AylikRapor from './screens/AylikRapor'
import Cariler from './screens/Cariler'
import Ayarlar from './screens/Ayarlar'
import Panel from './screens/Panel'
import Login from './screens/Login'
import { AuthProvider, useAuth } from './api/AuthContext'
import { color } from './theme'

function ShellPage({ children }: { children: ReactNode }) {
  return (
    <div style={{ minHeight: '100%', background: color.pageDark, display: 'flex', justifyContent: 'center', padding: '24px 0' }}>
      <DesktopShell>{children}</DesktopShell>
    </div>
  )
}

function Korumali({ children, editor = false }: { children: ReactNode; editor?: boolean }) {
  const { rol, yukleniyor } = useAuth()
  if (yukleniyor) return null
  if (rol === null) return <Navigate to="/login" replace />
  if (editor && rol !== 'editor') return <Navigate to="/panel" replace />
  return <>{children}</>
}

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/" element={<Korumali editor><ShellPage><IslemDefter /></ShellPage></Korumali>} />
        <Route path="/haftalik" element={<Korumali editor><ShellPage><HaftalikOzet /></ShellPage></Korumali>} />
        <Route path="/aylik" element={<Korumali editor><ShellPage><AylikRapor /></ShellPage></Korumali>} />
        <Route path="/cariler" element={<Korumali editor><ShellPage><Cariler /></ShellPage></Korumali>} />
        <Route path="/ayarlar" element={<Korumali editor><ShellPage><Ayarlar /></ShellPage></Korumali>} />
        <Route path="/panel" element={<Korumali><Panel /></Korumali>} />
        <Route path="/login" element={<Login />} />
      </Routes>
    </AuthProvider>
  )
}
```

- [ ] **Step 4: Derle**

Run: `npm run build`
Expected: build succeeded.

- [ ] **Step 5: Commit**

```bash
git add web/src/screens/Login.tsx web/src/App.tsx web/src/api/AuthContext.tsx
git commit -m "feat(web): gerçek giriş akışı + rol tabanlı rota koruması"
```

---

### Task 6: IslemDefter + HaftalikOzet ekranlarını bağla

**Files:**
- Modify: `web/src/screens/IslemDefter.tsx`, `web/src/screens/HaftalikOzet.tsx`

- [ ] **Step 1: `HaftalikOzet.tsx`'i bağla**

Ekranı oku. `import { haftalar } from "../data/mock"` satırını sil; yerine:
```typescript
import { useHaftalik } from '../data/hooks'
```
Bileşen gövdesinde `haftalar(3)` kullanımını hook'a çevir:
```typescript
const { veri, yukleniyor, hata } = useHaftalik()
if (yukleniyor) return <div style={{ padding: 24, color: color.sub }}>Yükleniyor…</div>
if (hata) return <div style={{ padding: 24, color: color.neg }}>Veri alınamadı.</div>
const bloklar = (veri ?? []).slice(0, 3)
```
`haftalar(3)` yerine `bloklar` kullan. (`color` import zaten varsa tekrar ekleme.)

- [ ] **Step 2: `IslemDefter.tsx` listesini bağla**

Ekranı oku. `import { islemler } from "../data/mock"` satırını sil; yerine:
```typescript
import { useIslemler } from '../data/hooks'
import { apiPost, apiDelete } from '../api/client'
```
Statik `islemler` kullanımını hook'a çevir:
```typescript
const { veri, yukleniyor, hata, yenile } = useIslemler()
```
Liste render'ında `islemler` yerine `(veri ?? [])`, üstüne `yukleniyor`/`hata` guard'ı ekle (Step 1'deki desenle).

- [ ] **Step 3: İşlem ekle + sil aksiyonlarını bağla**

`IslemDefter.tsx`'teki "yeni işlem" formunun submit'ini ve satır sil butonunu bağla:
```typescript
async function islemEkle(form: { tarih: string; cari: string; tutarTl: number; kanal: string; tip: string; not: string | null }) {
  await apiPost('/islemler', form) // tarih ISO "2026-07-12", tip "Cari"|"SabitGider"|"KrediKarti"
  yenile()
}
async function islemSil(id: number) {
  await apiDelete(`/islemler/${id}`)
  yenile()
}
```
Not: view-model `Islem`'de `id` yok. Sil için ham id gerekiyorsa `useIslemler`'i ham `IslemDto`+adapt eşlemesini birlikte döndürecek şekilde kullan; en basiti: satıra `data-id` yerine hook'u ham liste de dönecek şekilde genişletmek yerine, `IslemDto`'yu doğrudan bu ekranda çekip hem ham hem adapt et. Uygulama: bu ekran için `apiGet<IslemDto[]>('/islemler')` çağır, `islemAdapt` ile göster, sil/güncelle için `dto.id` kullan. Form dropdown'ları (kanal, tip) sabit listelerden beslenir: kanal = `['MEZAT','PERAKENDE','TOPTAN','Ortak']`, tip = `['Cari','SabitGider','KrediKarti']`.

- [ ] **Step 4: Test (API çalışırken elde doğrulama)**

Terminal 1: `dotnet run --project ../Kasa.Api --urls http://localhost:5199`
Terminal 2: `npm run dev` → tarayıcıda `/login` → editör gir (appsettings: editor/degistir-beni) → `/` işlem ekle/sil, `/haftalik` görüntüle.
Expected: işlem eklenince liste güncellenir; haftalık özet API verisiyle dolar.

- [ ] **Step 5: Commit**

```bash
git add web/src/screens/IslemDefter.tsx web/src/screens/HaftalikOzet.tsx
git commit -m "feat(web): İşlem defteri + haftalık özet ekranlarını API'ye bağla"
```

---

### Task 7: Panel + AylikRapor trend/grafiklerini bağla

Panel trend serileri `/rapor/haftalik` çıktısından; aylık çubuklar son 3 ayın `/rapor/aylik` çağrısından üretilir. SVG yol üretimi mock.ts'teki formüllerle birebir korunur.

**Files:**
- Modify: `web/src/data/adapters.ts`, `web/src/data/adapters.test.ts`, `web/src/data/hooks.ts`, `web/src/screens/Panel.tsx`, `web/src/screens/AylikRapor.tsx`

- [ ] **Step 1: Panel serisi + aylık grup adapter testleri**

`web/src/data/adapters.test.ts` sonuna ekle:
```typescript
import { panelSeri, ayGrupAdapt } from './adapters'
import type { AylikRaporDto } from '../api/tipler'

describe('panelSeri', () => {
  it('haftalık kasaDevir dizisinden kasa serisi ve SVG yolu üretir', () => {
    const bloklar = [
      { kasaDevir: 648700, mezat: 412500, perakende: 168400, toptan: 96200 },
      { kasaDevir: 700000, mezat: 500000, perakende: 180000, toptan: 100000 },
    ]
    const s = panelSeri(bloklar)
    expect(s.kasaSeri).toEqual([648700, 700000])
    expect(s.kasaPath.startsWith('M')).toBe(true)
    expect(s.kasaPath.split(' ')).toHaveLength(2) // 2 nokta
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
  })
})
```

- [ ] **Step 2: Test başarısız olsun**

Run: `npm run test -- adapters`
Expected: FAIL — `panelSeri`, `ayGrupAdapt` yok.

- [ ] **Step 3: `panelSeri` + `ayGrupAdapt`'ı yaz**

`web/src/data/adapters.ts` sonuna ekle (mock.ts'teki px/kasaY/kanalY/ay formüllerini birebir taşı):
```typescript
import { sfmt } from '../format'
import type { AylikRaporDto } from '../api/tipler'
import type { AyGrup } from './types'

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
      }
    }),
  }
}
```

- [ ] **Step 4: Panel + Aylık hook'larını ekle**

`web/src/data/hooks.ts` sonuna ekle:
```typescript
import type { AylikRaporDto } from '../api/tipler'
import { panelSeri, ayGrupAdapt, kanalAd, type PanelKaynak } from './adapters'
import type { AyGrup } from './types'

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

export function useAylik(): { veri: AyGrup[] | null; yukleniyor: boolean; hata: string | null; yenile: () => void } {
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
```

- [ ] **Step 5: Panel + AylikRapor ekranlarını bağla**

`Panel.tsx`'i oku: mock'tan gelen `kasaPath, kasaArea, kasaDotY, mzPath, prPath, tpPath, mzDotY, prDotY, tpDotY` import'unu sil; `usePanelSeri()` hook'undan al (yükleniyor guard'ı ekle). `veri` alanları aynı adlarla gelir → JSX'te `veri.kasaPath` vb. kullan.

`AylikRapor.tsx`'i oku: `ayGruplar, mzAyPath, prAyPath, tpAyPath` import'unu sil; `useAylik()`'ten `ayGruplar` al. Aylık çizgi grafik yolları (`mzAyPath` vb.) view-model dışıysa ve sadece 3 ay sabit ölçekliyse, `ayGrupAdapt` verisiyle aynı formülle bileşen içinde hesaplanır ya da bu grafik geçici kaldırılır (çubuk grafik yeterli). Basit tut: çubuk grafiği bağla, çizgi overlay'i `ayGruplar` bar `top` değerlerinden türet.

- [ ] **Step 6: Testler + derleme**

Run: `npm run test -- adapters && npm run build`
Expected: adapter testleri PASS; build succeeded.

- [ ] **Step 7: Commit**

```bash
git add web/src/data/adapters.ts web/src/data/adapters.test.ts web/src/data/hooks.ts web/src/screens/Panel.tsx web/src/screens/AylikRapor.tsx
git commit -m "feat(web): Panel trend + Aylık rapor grafiklerini API'ye bağla"
```

---

### Task 8: Cariler + Ayarlar ekranlarını bağla

Cariler türetilmiş alanları `/api/cariler` + `/api/islemler` toplamından hesaplanır.

**Files:**
- Modify: `web/src/data/adapters.ts`, `web/src/data/adapters.test.ts`, `web/src/data/hooks.ts`, `web/src/screens/Cariler.tsx`, `web/src/screens/Ayarlar.tsx`

- [ ] **Step 1: Cari adapter testi**

`adapters.test.ts` sonuna ekle:
```typescript
import { carilerAdapt } from './adapters'
import type { CariDto, IslemDto } from '../api/tipler'

describe('carilerAdapt', () => {
  it('cari başına işlem sayısı, son tarih, hacim ve mükerrer uyarısı üretir', () => {
    const cariler: CariDto[] = [
      { id: 1, ad: 'Yıldız Tarım', aktif: true },
      { id: 2, ad: 'Yıldız Tarım', aktif: true }, // birebir aynı ad → uyarı
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
```

- [ ] **Step 2: Test başarısız olsun**

Run: `npm run test -- adapters`
Expected: FAIL — `carilerAdapt` yok.

- [ ] **Step 3: `carilerAdapt`'ı yaz**

`adapters.ts` sonuna ekle:
```typescript
import type { CariDto } from '../api/tipler'
import type { Cari } from './types'

const AYLAR_UZUN = ['Oca', 'Şub', 'Mar', 'Nis', 'May', 'Haz', 'Tem', 'Ağu', 'Eyl', 'Eki', 'Kas', 'Ara']
function tarihUzun(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number)
  return `${d} ${AYLAR_UZUN[m - 1]} ${y}`
}
function bas(ad: string): string {
  const p = ad.trim().split(/\s+/)
  return (p.length >= 2 ? p[0][0] + p[1][0] : ad.slice(0, 2)).toLocaleUpperCase('tr-TR')
}

export function carilerAdapt(cariler: CariDto[], islemler: IslemDto[]): Cari[] {
  const adSayisi = new Map<string, number>()
  for (const c of cariler) adSayisi.set(c.ad, (adSayisi.get(c.ad) ?? 0) + 1)

  return cariler.map((c) => {
    const ait = islemler.filter((i) => i.cari === c.ad)
    const hacim = ait.reduce((s, i) => s + i.tutarTl, 0)
    const son = ait.map((i) => i.tarih).sort().at(-1)
    const aktif = c.aktif
    return {
      ad: c.ad,
      bas: bas(c.ad),
      islem: String(ait.length),
      son: son ? tarihUzun(son) : '—',
      hacim: fmt(hacim),
      durum: aktif ? 'Aktif' : 'Pasif',
      uyari: (adSayisi.get(c.ad) ?? 0) > 1,
      dbg: aktif ? '#E3F1E8' : '#EDEAE0',
      dc: aktif ? '#1B7A4E' : '#8A8F80',
      op: aktif ? '1' : '0.55',
      eylem: aktif ? 'Pasifleştir' : 'Aktifleştir',
    }
  })
}
```

- [ ] **Step 4: Cariler + Ayarlar hook'ları**

`hooks.ts` sonuna ekle:
```typescript
import type { CariDto, AyarlarDto } from '../api/tipler'
import { carilerAdapt } from './adapters'
import type { Cari } from './types'

export function useCariler(): { veri: Cari[] | null; yukleniyor: boolean; hata: string | null; yenile: () => void } {
  return useVeri(async () => {
    const [cariler, islemler] = await Promise.all([
      apiGet<CariDto[]>('/cariler'),
      apiGet<IslemDto[]>('/islemler'),
    ])
    return carilerAdapt(cariler, islemler)
  })
}

export function useAyarlar() {
  return useVeri(() => apiGet<AyarlarDto>('/ayarlar'))
}
```

- [ ] **Step 5: Ekranları bağla**

`Cariler.tsx`'i oku: `import { cariler } from "../data/mock"` sil → `useCariler()`; yükleniyor guard; "+ Yeni Cari" → `apiPost('/cariler', { ad, aktif: true })` + `yenile()`; aktif/pasifleştir → `apiPut('/cariler/{id}', {...})` (id için hook'u `CariDto` da dönecek şekilde kullan veya bu ekranda ham `CariDto` çekip adapt et — IslemDefter'deki desen).

`Ayarlar.tsx`'i oku: mock kullanmıyor (hardcoded). `useAyarlar()` ile `takipBaslangic`/`kasaAcilisDevri` input'larını doldur; kaydet → `apiPut('/ayarlar', { takipBaslangic, kasaAcilisDevri })`; izleyici şifre kartı → `apiPut('/ayarlar/izleyici-sifre', { yeniSifre })`. Kanal açılış devirleri → `apiGet<KanalDto[]>('/kanallar')` + `apiPut('/kanallar/{id}', {...})`.

- [ ] **Step 6: Testler + derleme + elde doğrulama**

Run: `npm run test && npm run build`
Expected: tüm adapter testleri PASS; build succeeded.
Elde: API çalışırken `/cariler` ve `/ayarlar` ekranlarını doğrula.

- [ ] **Step 7: Commit**

```bash
git add web/src/data/adapters.ts web/src/data/adapters.test.ts web/src/data/hooks.ts web/src/screens/Cariler.tsx web/src/screens/Ayarlar.tsx
git commit -m "feat(web): Cariler + Ayarlar ekranlarını API'ye bağla"
```

---

### Task 9: mock.ts'i kaldır + son doğrulama

**Files:**
- Delete: `web/src/data/mock.ts`
- Modify: kalan mock referansları (varsa)

- [ ] **Step 1: Kalan mock import'larını bul**

Run: `cd web && grep -rn "data/mock" src/ || echo "temiz"`
Expected: `temiz` (hiç referans kalmamalı). Kalan varsa ilgili ekranı hook'a çevir.

- [ ] **Step 2: mock.ts'i sil**

Run: `git rm web/src/data/mock.ts`

- [ ] **Step 3: Tam derleme + test**

Run: `npm run build && npm run test`
Expected: build succeeded; tüm testler PASS.

- [ ] **Step 4: Uçtan uca elde doğrulama**

Terminal 1: `dotnet run --project ../Kasa.Api --urls http://localhost:5199`
Terminal 2: `npm run dev` → editör girişi → 5 editör ekranı + izleyici girişiyle `/panel`. Tüm ekranlar API verisiyle dolmalı; işlem/cari ekle-sil çalışmalı.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(web): mock veri katmanını kaldır — tüm ekranlar API'ye bağlı"
```

---

## Self-Review (yazar kontrolü — tamamlandı)

**Spec kapsamı:** 7 ekranın hepsi bağlandı (IslemDefter/HaftalikOzet Task 6, Panel/AylikRapor Task 7, Cariler/Ayarlar Task 8, Login Task 5). Auth cookie akışı + rol guard Task 5. Biçim/renk mock.ts'ten birebir taşındı (Task 2-3-7-8 adapter'ları). Trend/çok-ay verisi mevcut uçlardan türetildi — yeni API ucu eklenmedi (kapsam kararı).

**Placeholder taraması:** Ekran JSX'i tam yeniden yazılmadı çünkü kaynağı plan yazarında yok; bunun yerine her ekran task'ı (a) silinecek mock import satırını, (b) yerine gelen hook'u, (c) loading guard desenini net veriyor — mantık (adapter+hook) tam kodlu. Executing subagent ekranı okuyup mekanik değişikliği uygular. Bu, "logic tam, ekran teli ince" ilkesine uygun.

**Tip tutarlılığı:** `IslemDto/HaftalikOzetDto/AylikRaporDto/CariDto/PanelDto/AyarlarDto` (tipler.ts) tüm adapter/hook'larda aynı alan adlarıyla kullanılıyor (camelCase, API ile eşleşiyor). View-model tipleri (types.ts) hem mock hem adapter'da paylaşımlı. `kanalAd/tarihKisa/fmt/sfmt/kanalRenk/color` tek yerde tanımlı, tekrar kullanılıyor.

**Notlar / riskler:**
- Dev API portu 5199 varsayıldı (`--urls` ile sabitleniyor); launchSettings farklıysa proxy target'ı ona hizala.
- `sonuc`/`devir` işaret/renk eşiği mock ile birebir (negatif→neg, devir negatif→neg değilse ink).
- Cariler `uyari` = birebir aynı ad; mock'taki "M. Kaya vs Mehmet Kaya" bulanık eşleşme kapsam dışı (basit tutuldu).
- Aylık çizgi-overlay grafiği sadeleştirildi (çubuk esas); istenirse ayrı iş.
- CORS yok: dev'de Vite proxy, prod'da Caddy same-origin sağlar (Dağıtım planı).
