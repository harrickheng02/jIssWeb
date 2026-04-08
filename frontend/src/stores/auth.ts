import { defineStore } from 'pinia'
import { ref } from 'vue'

const storageKey = 'jissweb.jwt'
const refreshStorageKeyLocal = 'jissweb.refresh.local'
const refreshStorageKeySession = 'jissweb.refresh.session'
const rememberMeKey = 'jissweb.remember'
const pendingEmailKey = 'jissweb.pending.email'
const pendingVerifyCooldownUntilKey = 'jissweb.pending.verify.cooldown.until'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem(storageKey) ?? sessionStorage.getItem(storageKey))
  const refreshToken = ref<string | null>(
    localStorage.getItem(refreshStorageKeyLocal) ?? sessionStorage.getItem(refreshStorageKeySession),
  )
  const rememberMe = ref(localStorage.getItem(rememberMeKey) === '1')
  const pendingVerifyEmail = ref<string | null>(sessionStorage.getItem(pendingEmailKey))
  const pendingVerifyCooldownUntil = ref(Number(sessionStorage.getItem(pendingVerifyCooldownUntilKey) ?? '0'))

  function setToken(value: string | null) {
    token.value = value
    if (value) {
      if (rememberMe.value) localStorage.setItem(storageKey, value)
      else sessionStorage.setItem(storageKey, value)
    } else {
      localStorage.removeItem(storageKey)
      sessionStorage.removeItem(storageKey)
    }
  }

  function setRefreshToken(value: string | null, persist = rememberMe.value) {
    refreshToken.value = value
    if (value) {
      if (persist) {
        localStorage.setItem(refreshStorageKeyLocal, value)
        sessionStorage.removeItem(refreshStorageKeySession)
      } else {
        sessionStorage.setItem(refreshStorageKeySession, value)
        localStorage.removeItem(refreshStorageKeyLocal)
      }
    } else {
      localStorage.removeItem(refreshStorageKeyLocal)
      sessionStorage.removeItem(refreshStorageKeySession)
    }
  }

  function setRememberMe(value: boolean) {
    rememberMe.value = value
    if (value) localStorage.setItem(rememberMeKey, '1')
    else localStorage.removeItem(rememberMeKey)
  }

  function applyAuthSession(accessToken: string, newRefreshToken: string, persist: boolean) {
    setRememberMe(persist)
    setToken(accessToken)
    setRefreshToken(newRefreshToken, persist)
  }

  function clearAuth() {
    setToken(null)
    setRefreshToken(null)
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

  return {
    token,
    refreshToken,
    rememberMe,
    pendingVerifyEmail,
    pendingVerifyCooldownUntil,
    setToken,
    setRefreshToken,
    setRememberMe,
    applyAuthSession,
    clearAuth,
    setPendingVerifyEmail,
    setPendingVerifyCooldownUntil,
    setPendingVerifyCooldown,
  }
})
