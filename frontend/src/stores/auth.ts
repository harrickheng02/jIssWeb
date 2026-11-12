import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

const pendingEmailKey = 'jissweb.pending.email'
const pendingVerifyCooldownUntilKey = 'jissweb.pending.verify.cooldown.until'
const sessionTokenKey = 'jissweb.session.token'
// 非敏感标志：仅标记"曾经登录过"，供 main.ts 决定是否发起 restoreSession，避免未登录用户看到 401
const sessionHintKey = 'jissweb.session.hint'

// 清理 pre-BFF 时代遗留的旧 key
;['jissweb.jwt', 'jissweb.refresh.local'].forEach((k) => localStorage.removeItem(k))
;['jissweb.jwt', 'jissweb.refresh.session'].forEach((k) => sessionStorage.removeItem(k))

export type ForumRole = 'member' | 'moderator' | 'admin'

function decodeJwtPayload(token: string): Record<string, unknown> | null {
  const parts = token.split('.')
  if (parts.length < 2) return null
  const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/')
  const padded = b64 + '='.repeat((4 - (b64.length % 4)) % 4)
  try {
    const json = decodeURIComponent(
      atob(padded)
        .split('')
        .map((c) => `%${c.charCodeAt(0).toString(16).padStart(2, '0')}`)
        .join(''),
    )
    const obj = JSON.parse(json)
    return obj && typeof obj === 'object' ? (obj as Record<string, unknown>) : null
  } catch {
    return null
  }
}

type AuthChannelMsg = { type: 'token'; value: string } | { type: 'logout' } | { type: 'request-token' }

const authChannel = typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel('jissweb.auth') : null

// 新标签打开时无 sessionStorage token，广播 request-token 请求其他标签同步。
// 最多等 50ms：BroadcastChannel 同进程响应通常 < 5ms；无其他标签时超时后继续（由 restoreSession 兜底）。
let _bcSyncResolve: (() => void) | null = null
export const bcSyncReady: Promise<void> =
  !sessionStorage.getItem(sessionTokenKey) && typeof BroadcastChannel !== 'undefined'
    ? new Promise<void>((resolve) => {
        _bcSyncResolve = resolve
        setTimeout(() => { resolve(); _bcSyncResolve = null }, 50)
      })
    : Promise.resolve()

export const useAuthStore = defineStore('auth', () => {
  // 从 sessionStorage 同步读取——页面刷新后立即可用，无需等待异步请求
  const token = ref<string | null>(sessionStorage.getItem(sessionTokenKey))

  const pendingVerifyEmail = ref<string | null>(sessionStorage.getItem(pendingEmailKey))
  const pendingVerifyCooldownUntil = ref(Number(sessionStorage.getItem(pendingVerifyCooldownUntilKey) ?? '0'))

  if (authChannel) {
    authChannel.onmessage = (event: MessageEvent<AuthChannelMsg>) => {
      const msg = event.data
      if (msg.type === 'token') {
        token.value = msg.value
        sessionStorage.setItem(sessionTokenKey, msg.value)
        _bcSyncResolve?.()
        _bcSyncResolve = null
      } else if (msg.type === 'logout') {
        token.value = null
        sessionStorage.removeItem(sessionTokenKey)
      } else if (msg.type === 'request-token' && token.value) {
        authChannel.postMessage({ type: 'token', value: token.value } as AuthChannelMsg)
      }
    }
    // 无 token 时广播请求，其他标签会回复当前 token
    if (!token.value) {
      authChannel.postMessage({ type: 'request-token' } as AuthChannelMsg)
    }
  }

  function applyAuthSession(accessToken: string) {
    token.value = accessToken
    sessionStorage.setItem(sessionTokenKey, accessToken)
    localStorage.setItem(sessionHintKey, '1')
    authChannel?.postMessage({ type: 'token', value: accessToken } as AuthChannelMsg)
  }

  function clearAuth() {
    token.value = null
    sessionStorage.removeItem(sessionTokenKey)
    localStorage.removeItem(sessionHintKey)
    authChannel?.postMessage({ type: 'logout' } as AuthChannelMsg)
  }

  // 后台静默刷新：用 BFF HttpOnly Cookie 换新 access token
  // sessionStorage 已保障刷新后不丢登录态，此函数作为 token 更新手段
  async function restoreSession() {
    try {
      const res = await fetch('/api/bff/auth/token', { credentials: 'include' })
      if (!res.ok) {
        // 401 = BFF 明确说没有有效 cookie，清掉 sessionStorage 避免过期 token 继续使用
        if (res.status === 401) clearAuth()
        return
      }
      const body = (await res.json()) as { success: boolean; data?: { accessToken: string } }
      if (body.success && body.data?.accessToken) {
        applyAuthSession(body.data.accessToken)
      }
    } catch {
      // 网络错误/BFF 未启动：保留 sessionStorage 中的 token，不强制注销
    }
  }

  function setPendingVerifyEmail(value: string | null) {
    pendingVerifyEmail.value = value
    if (value) sessionStorage.setItem(pendingEmailKey, value)
    else {
      sessionStorage.removeItem(pendingEmailKey)
      setPendingVerifyCooldownUntil(0)
    }
  }

  function setPendingVerifyCooldownUntil(value: number) {
    pendingVerifyCooldownUntil.value = value
    if (value > 0) sessionStorage.setItem(pendingVerifyCooldownUntilKey, String(value))
    else sessionStorage.removeItem(pendingVerifyCooldownUntilKey)
  }

  function setPendingVerifyCooldown(seconds: number) {
    setPendingVerifyCooldownUntil(Date.now() + Math.max(seconds, 0) * 1000)
  }

  const sub = computed<string | null>(() => {
    const t = token.value?.trim()
    if (!t) return null
    const payload = decodeJwtPayload(t)
    const s = payload?.sub
    return typeof s === 'string' && s.length > 0 ? s : null
  })

  const forumRole = computed<ForumRole>(() => {
    const t = token.value?.trim()
    if (!t) return 'member'
    const payload = decodeJwtPayload(t)
    const raw = typeof payload?.forumRole === 'string' ? payload.forumRole.trim().toLowerCase() : ''
    if (raw === 'moderator' || raw === 'admin' || raw === 'member') return raw
    return 'member'
  })

  const canModerate = computed(() => forumRole.value === 'moderator' || forumRole.value === 'admin')
  const canAdmin = computed(() => forumRole.value === 'admin')

  const forumBoardIds = computed<string[]>(() => {
    const t = token.value?.trim()
    if (!t) return []
    const payload = decodeJwtPayload(t)
    const raw = payload?.forumBoardIds
    if (typeof raw !== 'string' || !raw.trim()) return []
    try {
      const parsed = JSON.parse(raw) as unknown
      if (!Array.isArray(parsed)) return []
      return parsed.filter((x): x is string => typeof x === 'string' && x.trim().length > 0).map((x) => x.trim())
    } catch {
      return []
    }
  })

  return {
    token,
    pendingVerifyEmail,
    pendingVerifyCooldownUntil,
    sub,
    forumRole,
    canModerate,
    canAdmin,
    forumBoardIds,
    applyAuthSession,
    clearAuth,
    restoreSession,
    setPendingVerifyEmail,
    setPendingVerifyCooldownUntil,
    setPendingVerifyCooldown,
  }
})
