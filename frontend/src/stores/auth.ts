import { defineStore } from 'pinia'
import { ref } from 'vue'

const storageKey = 'jissweb.jwt'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem(storageKey))

  function setToken(value: string | null) {
    token.value = value
    if (value) localStorage.setItem(storageKey, value)
    else localStorage.removeItem(storageKey)
  }

  return { token, setToken }
})
