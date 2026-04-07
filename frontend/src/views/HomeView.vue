<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  accountingApi,
  customerApi,
  fetchDevToken,
  modelApi,
  reportApi,
  userApi,
} from '@/api/clients'
import type { ApiResult } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const lines = ref<string[]>([])

async function run() {
  const out: string[] = []
  const tokenRes = await fetchDevToken()
  if (tokenRes.data) auth.setToken(tokenRes.data)
  out.push(`token: ${tokenRes.success ? 'ok' : 'fail'}`)

  const healthChecks: { name: string; p: Promise<{ data: ApiResult<string> }> }[] = [
    { name: 'user', p: userApi.get<ApiResult<string>>('/api/health') },
    { name: 'customer', p: customerApi.get<ApiResult<string>>('/api/health') },
    { name: 'model', p: modelApi.get<ApiResult<string>>('/api/health') },
    { name: 'accounting', p: accountingApi.get<ApiResult<string>>('/api/health') },
    { name: 'report', p: reportApi.get<ApiResult<string>>('/api/health') },
  ]
  for (const h of healthChecks) {
    try {
      const r = await h.p
      out.push(`health ${h.name}: ${r.data?.data ?? r.data?.message ?? '?'}`)
    } catch {
      out.push(`health ${h.name}: error`)
    }
  }

  const samples = [
    { name: 'user', p: userApi.get<ApiResult<string>>('/api/sample/me') },
    { name: 'customer', p: customerApi.get<ApiResult<string>>('/api/sample/me') },
    { name: 'model', p: modelApi.get<ApiResult<string>>('/api/sample/me') },
    { name: 'accounting', p: accountingApi.get<ApiResult<string>>('/api/sample/me') },
    { name: 'report', p: reportApi.get<ApiResult<string>>('/api/sample/me') },
  ]
  for (const s of samples) {
    try {
      const r = await s.p
      out.push(`sample ${s.name}: ${r.data?.data ?? r.data?.message ?? '?'}`)
    } catch {
      out.push(`sample ${s.name}: error`)
    }
  }
  lines.value = out
}

onMounted(run)
</script>

<template>
  <div class="page">
    <h1>JIssWeb</h1>
    <pre>{{ lines.join('\n') }}</pre>
  </div>
</template>

<style scoped>
.page {
  padding: 2rem;
}
pre {
  white-space: pre-wrap;
  font-family: inherit;
}
</style>
