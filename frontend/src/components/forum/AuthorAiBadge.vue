<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { modelApi } from '@/api/clients'
import type { ApiResult } from '@/api/clients'

const props = withDefaults(
  defineProps<{
    authorId: string
    /** 紧凑模式下仅显示角标，不占额外行高 */
    inline?: boolean
  }>(),
  { inline: false },
)

const isAi = ref(false)
const resolved = ref(false)

// 模块级共享缓存：同一作者跨多张卡片只请求一次。
const cache = new Map<string, boolean>()

async function resolve() {
  const id = props.authorId
  if (!id) return
  if (cache.has(id)) {
    isAi.value = cache.get(id)!
    resolved.value = true
    return
  }
  try {
    const { data } = await modelApi.get<ApiResult<{ isAI: boolean }>>('/arena/author-is-ai', {
      params: { userId: id },
    })
    const v = Boolean(data?.success && data.data?.isAI)
    cache.set(id, v)
    isAi.value = v
  } catch {
    // 查询失败不下判决，按非 AI 处理（不泄露内部态）。
    cache.set(id, false)
  } finally {
    resolved.value = true
  }
}

onMounted(resolve)
</script>

<template>
  <el-tag
    v-if="resolved && isAi"
    class="ai-badge"
    size="small"
    effect="plain"
    :class="{ 'ai-badge--inline': inline }"
  >
    AI
  </el-tag>
</template>

<style scoped>
.ai-badge {
  /* 仅用 forum-tokens 变量，避免硬编码；浅蓝底 + 主色字，呼应「已揭露」语义。 */
  background: var(--el-color-primary-light-9);
  color: var(--color-primary);
  border-color: var(--el-color-primary-light-7);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.ai-badge--inline {
  margin-left: var(--space-xs);
  vertical-align: middle;
}
</style>
