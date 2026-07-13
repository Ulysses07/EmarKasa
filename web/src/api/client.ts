// Ham fetch sarmalayıcı. Base URL: VITE_API_URL (boşsa "/api" → Vite proxy / Caddy same-origin).
// Tüm çağrılar cookie auth için credentials:"include" gönderir.

const BASE = (import.meta.env.VITE_API_URL ?? '') + '/api'

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
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
