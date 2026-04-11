import { vi } from 'vitest'

function memoryStorage() {
  const m = new Map<string, string>()
  return {
    getItem: (k: string) => (m.has(k) ? m.get(k)! : null),
    setItem: (k: string, v: string) => {
      m.set(k, v)
    },
    removeItem: (k: string) => {
      m.delete(k)
    },
    clear: () => {
      m.clear()
    },
    get length() {
      return m.size
    },
    key: (i: number) => Array.from(m.keys())[i] ?? null,
  }
}

vi.stubGlobal('localStorage', memoryStorage())
vi.stubGlobal('sessionStorage', memoryStorage())
