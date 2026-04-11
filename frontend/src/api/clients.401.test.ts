import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'

vi.mock('@/utils/authRedirect', () => ({
  redirectToLogin: vi.fn().mockResolvedValue(undefined),
}))

import { getProfile } from './clients'
import { redirectToLogin } from '@/utils/authRedirect'

let refreshCalls = 0

const server = setupServer(
  http.post('http://localhost:3000/api/auth/refresh', async () => {
    refreshCalls++
    return HttpResponse.json({
      success: true,
      data: {
        accessToken: 'new-access',
        refreshToken: 'new-refresh',
        accessTokenExpiresAtUtc: new Date().toISOString(),
        refreshTokenExpiresAtUtc: new Date().toISOString(),
      },
    })
  }),
  http.get('http://localhost:3000/api/profile', ({ request }) => {
    const auth = request.headers.get('authorization') ?? ''
    if (auth.includes('bad-access')) {
      return HttpResponse.json({ success: false }, { status: 401 })
    }
    return HttpResponse.json({
      success: true,
      data: {
        id: '1',
        ownerUserId: 'u',
        nickname: '',
        birthDate: '',
        gender: '',
        createdAtUtc: new Date().toISOString(),
        updatedAtUtc: new Date().toISOString(),
      },
    })
  }),
)

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' })
})

afterEach(() => {
  server.resetHandlers()
  refreshCalls = 0
  vi.mocked(redirectToLogin).mockClear()
})

afterAll(() => {
  server.close()
})

describe('401 refresh', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('401 后 refresh 一次并重试成功', async () => {
    const auth = useAuthStore()
    auth.applyAuthSession('bad-access', 'refresh-ok')
    const res = await getProfile()
    expect(res.success).toBe(true)
    expect(res.data?.id).toBe('1')
    expect(refreshCalls).toBe(1)
  })

  it('并行 401 只触发一次 refresh', async () => {
    const auth = useAuthStore()
    auth.applyAuthSession('bad-access', 'refresh-ok')
    const [a, b] = await Promise.all([getProfile(), getProfile()])
    expect(a.success && b.success).toBe(true)
    expect(refreshCalls).toBe(1)
  })

  it('refresh 失败时清会话并调用 redirectToLogin', async () => {
    server.use(
      http.post('http://localhost:3000/api/auth/refresh', () =>
        HttpResponse.json({ success: false }, { status: 401 }),
      ),
    )
    const auth = useAuthStore()
    auth.applyAuthSession('bad-access', 'refresh-ok')
    await expect(getProfile()).rejects.toBeDefined()
    expect(auth.token).toBeNull()
    expect(auth.refreshToken).toBeNull()
    expect(redirectToLogin).toHaveBeenCalled()
  })
})
