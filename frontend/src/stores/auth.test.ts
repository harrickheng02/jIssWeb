import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from './auth'

function makeFetchOk(body: unknown) {
  return vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: () => Promise.resolve(body),
  })
}

function makeFetchStatus(status: number) {
  return vi.fn().mockResolvedValue({
    ok: false,
    status,
    json: () => Promise.resolve({ success: false }),
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  localStorage.clear()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('applyAuthSession / clearAuth', () => {
  it('applyAuthSession 写入 token 和 sessionStorage', () => {
    const auth = useAuthStore()
    auth.applyAuthSession('my-token')
    expect(auth.token).toBe('my-token')
    expect(sessionStorage.getItem('jissweb.session.token')).toBe('my-token')
  })

  it('clearAuth 清空 token 和 sessionStorage', () => {
    const auth = useAuthStore()
    auth.applyAuthSession('my-token')
    auth.clearAuth()
    expect(auth.token).toBeNull()
    expect(sessionStorage.getItem('jissweb.session.token')).toBeNull()
  })

  it('页面刷新后从 sessionStorage 恢复 token（同步）', () => {
    sessionStorage.setItem('jissweb.session.token', 'persisted-token')
    const auth = useAuthStore()
    expect(auth.token).toBe('persisted-token')
  })

  it('不向 localStorage 写 token', () => {
    const auth = useAuthStore()
    auth.applyAuthSession('my-token')
    expect(localStorage.getItem('jissweb.jwt')).toBeNull()
    expect(localStorage.getItem('jissweb.refresh.local')).toBeNull()
  })

  it('applyAuthSession 写入 sessionHint', () => {
    const auth = useAuthStore()
    auth.applyAuthSession('my-token')
    expect(localStorage.getItem('jissweb.session.hint')).toBe('1')
  })

  it('clearAuth 清除 sessionHint', () => {
    const auth = useAuthStore()
    auth.applyAuthSession('my-token')
    expect(localStorage.getItem('jissweb.session.hint')).toBe('1')
    auth.clearAuth()
    expect(localStorage.getItem('jissweb.session.hint')).toBeNull()
  })

  it('401 restoreSession 同时清除 sessionHint', async () => {
    sessionStorage.setItem('jissweb.session.token', 'stale')
    localStorage.setItem('jissweb.session.hint', '1')
    vi.stubGlobal('fetch', makeFetchStatus(401))
    const auth = useAuthStore()
    await auth.restoreSession()
    expect(localStorage.getItem('jissweb.session.hint')).toBeNull()
  })
})

describe('restoreSession', () => {
  it('成功时更新 token 和 sessionStorage', async () => {
    vi.stubGlobal('fetch', makeFetchOk({ success: true, data: { accessToken: 'new-token' } }))
    const auth = useAuthStore()
    await auth.restoreSession()
    expect(auth.token).toBe('new-token')
    expect(sessionStorage.getItem('jissweb.session.token')).toBe('new-token')
  })

  it('401 时清空 token（cookie 已失效）', async () => {
    sessionStorage.setItem('jissweb.session.token', 'stale-token')
    vi.stubGlobal('fetch', makeFetchStatus(401))
    const auth = useAuthStore()
    await auth.restoreSession()
    expect(auth.token).toBeNull()
    expect(sessionStorage.getItem('jissweb.session.token')).toBeNull()
  })

  it('网络错误时保留 sessionStorage 中的 token', async () => {
    sessionStorage.setItem('jissweb.session.token', 'cached-token')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Network error')))
    const auth = useAuthStore()
    // token 从 sessionStorage 初始化
    expect(auth.token).toBe('cached-token')
    await auth.restoreSession()
    // 网络错误不清 token
    expect(auth.token).toBe('cached-token')
  })

  it('502 时保留 token（BFF 瞬时故障）', async () => {
    sessionStorage.setItem('jissweb.session.token', 'cached-token')
    vi.stubGlobal('fetch', makeFetchStatus(502))
    const auth = useAuthStore()
    await auth.restoreSession()
    expect(auth.token).toBe('cached-token')
  })
})
