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
