import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

const storageKey = 'jissweb.theme'

export type ThemeMode = 'light' | 'dark'

export const useThemeStore = defineStore('theme', () => {
  const raw = localStorage.getItem(storageKey)
  const mode = ref<ThemeMode>(raw === 'dark' ? 'dark' : 'light')

  watch(
    mode,
    (m) => {
      const root = document.documentElement
      root.classList.toggle('dark', m === 'dark')
      root.setAttribute('data-theme', m)
      localStorage.setItem(storageKey, m)
    },
    { immediate: true },
  )

  function setTheme(m: ThemeMode) {
    mode.value = m
  }

  function toggle() {
    mode.value = mode.value === 'light' ? 'dark' : 'light'
  }

  return { mode, setTheme, toggle }
})
