import { defineStore } from 'pinia'
import { ref } from 'vue'

const storageKey = 'jissweb.jwt'
const refreshStorageKeyLocal = 'jissweb.refresh.local'
const refreshStorageKeySession = 'jissweb.refresh.session'
const pendingEmailKey = 'jissweb.pending.email'
const pendingVerifyCooldownUntilKey = 'jissweb.pending.verify.cooldown.until'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem(storageKey) ?? sessionStorage.getItem(storageKey))
  const refreshToken = ref<string | null>(
    localStorage.getItem(refreshStorageKeyLocal) ?? sessionStorage.getItem(refreshStorageKeySession),
  )
  const pendingVerifyEmail = ref<string | null>(sessionStorage.getItem(pendingEmailKey))
  const pendingVerifyCooldownUntil = ref(Number(sessionStorage.getItem(pendingVerifyCooldownUntilKey) ?? '0'))

  function setToken(value: string | null) {
    token.value = value
    if (value) {
      localStorage.setItem(storageKey, value)
      sessionStorage.removeItem(storageKey)
    } else {
      localStorage.removeItem(storageKey)
      sessionStorage.removeItem(storageKey)
    }
  }

  function setRefreshToken(value: string | null) {
    refreshToken.value = value
    if (value) {
      localStorage.setItem(refreshStorageKeyLocal, value)
      sessionStorage.removeItem(refreshStorageKeySession)
    } else {
      localStorage.removeItem(refreshStorageKeyLocal)
      sessionStorage.removeItem(refreshStorageKeySession)
    }
  }

  function applyAuthSession(accessToken: string, newRefreshToken: string) {
    setToken(accessToken)
    setRefreshToken(newRefreshToken)
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
    pendingVerifyEmail,
    pendingVerifyCooldownUntil,
    setToken,
    setRefreshToken,
    applyAuthSession,
    clearAuth,
    setPendingVerifyEmail,
    setPendingVerifyCooldownUntil,
    setPendingVerifyCooldown,
  }
})
