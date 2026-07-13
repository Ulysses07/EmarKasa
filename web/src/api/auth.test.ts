import { describe, it, expect, vi, afterEach } from 'vitest'
import { me } from './auth'

afterEach(() => vi.restoreAllMocks())

describe('auth me()', () => {
  it('200 olunca rolü döner', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ rol: 'editor' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    )

    await expect(me()).resolves.toBe('editor')
  })

  it('401 olunca null döner (oturum yok)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 401 })))
    await expect(me()).resolves.toBeNull()
  })

  it('500 olunca hatayı yeniden fırlatır', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 500 })))
    await expect(me()).rejects.toBeTruthy()
  })
})
