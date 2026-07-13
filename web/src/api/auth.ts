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
