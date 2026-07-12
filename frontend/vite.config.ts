import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const repoRoot = path.resolve(__dirname, '..')

function local(
  baseUrl: string,
  prefix: string,
  rewrite: boolean,
): { target: string; changeOrigin: boolean; rewrite?: (p: string) => string } {
  const config: {
    target: string
    changeOrigin: boolean
    rewrite?: (p: string) => string
  } = {
    target: baseUrl,
    changeOrigin: true,
  }
  if (rewrite) {
    config.rewrite = (p: string) => p.replace(new RegExp(`^${prefix}`), '')
  }
  return config
}

function requireEnv(env: Record<string, string>, key: string): string {
  const v = env[key]?.trim()
  if (!v) throw new Error(`Missing ${key} in repo root .env (see .env.example)`)
  return v
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, repoRoot, '')
  const gatewayBase = `http://127.0.0.1:${requireEnv(env, 'VITE_PROXY_GATEWAY_PORT')}`
  const bffBase = `http://127.0.0.1:${requireEnv(env, 'VITE_PROXY_BFF_PORT')}`
  const userBase = `http://127.0.0.1:${requireEnv(env, 'VITE_PROXY_USER_API_PORT')}`
  const customerBase = `http://127.0.0.1:${requireEnv(env, 'VITE_PROXY_CUSTOMER_API_PORT')}`
  const modelBase = `http://127.0.0.1:${requireEnv(env, 'VITE_PROXY_MODEL_API_PORT')}`
  const accountingBase = `http://127.0.0.1:${requireEnv(env, 'VITE_PROXY_ACCOUNTING_API_PORT')}`
  const reportBase = `http://127.0.0.1:${requireEnv(env, 'VITE_PROXY_REPORT_API_PORT')}`

  return {
    plugins: [vue()],
    envDir: repoRoot,
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },
    server: {
      port: Number.parseInt(requireEnv(env, 'VITE_DEV_SERVER_PORT'), 10),
      proxy: {
        '/api-user': local(userBase, '/api-user', true),
        '/api-customer': local(customerBase, '/api-customer', true),
        '/api-model': local(modelBase, '/api-model', true),
        '/api-accounting': local(accountingBase, '/api-accounting', true),
        '/api-report': local(reportBase, '/api-report', true),
        '/api/bff': {
          target: bffBase,
          changeOrigin: true,
        },
        '/api/forum': {
          target: modelBase,
          changeOrigin: true,
        },
        '/api/mod': {
          target: modelBase,
          changeOrigin: true,
        },
        '/api': local(gatewayBase, '/api', false),
      },
    },
  }
})
