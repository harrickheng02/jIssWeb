import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

function local(port: number, prefix: string, rewrite = true) {
  const config: {
    target: string
    changeOrigin: boolean
    rewrite?: (path: string) => string
  } = {
    target: `http://localhost:${port}`,
    changeOrigin: true,
  }
  if (rewrite) {
    config.rewrite = (path: string) => path.replace(new RegExp(`^${prefix}`), '')
  }
  return config
}

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api-user': local(5097, '/api-user'),
      '/api-customer': local(5098, '/api-customer'),
      '/api-model': local(5099, '/api-model'),
      '/api-accounting': local(5100, '/api-accounting'),
      '/api-report': local(5101, '/api-report'),
      '/api': local(5094, '/api', false),
    },
  },
})
