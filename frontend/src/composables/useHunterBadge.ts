import { ref } from 'vue'
import { getMyHunterBadge, type HunterBadgeItem } from '@/api/clients'

/**
 * 图灵猎手徽章只读数据（#34）。
 * 刻意不调用 onMounted，由视图决定加载时机，保证 composable 可在纯 Vitest 环境直接单测。
 * 端点只返回本人徽章，不含任何被揭露目标账号标识。
 */
export function useHunterBadge() {
  const badge = ref<HunterBadgeItem | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function load() {
    loading.value = true
    error.value = null
    try {
      const res = await getMyHunterBadge()
      if (res.success) {
        badge.value = res.data ?? null
      } else {
        badge.value = null
        error.value = res.message ?? '徽章加载失败'
      }
    } catch {
      badge.value = null
      error.value = '徽章加载失败'
    } finally {
      loading.value = false
    }
  }

  return { badge, loading, error, load }
}
