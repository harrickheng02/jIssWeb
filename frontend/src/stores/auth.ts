import { defineStore } from 'pinia'
import { ref } from 'vue'

const storageKey = 'jissweb.jwt'
const refreshStorageKey = 'jissweb.refresh'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem(storageKey))
  const refreshToken = ref<string | null>(localStorage.getItem(refreshStorageKey))

  function setToken(value: string | null) {
    token.value = value
    if (value) localStorage.setItem(storageKey, value)
    else localStorage.removeItem(storageKey)
  }

  function setRefreshToken(value: string | null) {
    refreshToken.value = value
    if (value) localStorage.setItem(refreshStorageKey, value)
    else localStorage.removeItem(refreshStorageKey)
  }

  return { token, refreshToken, setToken, setRefreshToken }
})
