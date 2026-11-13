<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getPointsBalance, getPointsHistory, type PointsHistoryItem } from '@/api/accounting'

const balance = ref<number | null>(null)
const history = ref<PointsHistoryItem[]>([])
const loading = ref(false)

const typeLabel: Record<string, string> = {
  accusation_hit: '指认命中',
  accusation_miss: '指认失误',
  warmup_pack: '热身包',
  decay: '月度衰减',
}

function labelOf(type: string): string {
  return typeLabel[type] ?? type
}

const hasHistory = computed(() => history.value.length > 0)

async function load() {
  loading.value = true
  try {
    const b = await getPointsBalance()
    if (b.success) balance.value = b.data?.balance ?? 0
    const h = await getPointsHistory(1, 20)
    if (h.success) history.value = h.data ?? []
  } finally {
    loading.value = false
  }
}

onMounted(load)

defineExpose({ load })
</script>

<template>
  <section class="forum-card points-panel">
    <header class="points-panel__head">
      <h3 class="points-panel__title">侦探积分</h3>
      <span v-if="balance !== null" class="points-panel__balance">{{ balance }}</span>
      <span v-else class="points-panel__balance points-panel__balance--muted">—</span>
    </header>

    <div v-if="loading" class="points-panel__hint">加载中…</div>

    <div v-else-if="hasHistory" class="points-panel__history">
      <div v-for="item in history" :key="item.occurredAtUtc + item.type + item.delta" class="points-row">
        <span class="points-row__type">{{ labelOf(item.type) }}</span>
        <span class="points-row__delta" :class="item.delta >= 0 ? 'is-gain' : 'is-loss'">
          {{ item.delta >= 0 ? '+' : '' }}{{ item.delta }}
        </span>
        <span class="points-row__after">余 {{ item.balanceAfter }}</span>
        <span class="points-row__time">{{ new Date(item.occurredAtUtc).toLocaleDateString() }}</span>
      </div>
    </div>
    <div v-else class="points-panel__hint">暂无积分流水</div>
  </section>
</template>

<style scoped>
.points-panel__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-md);
  margin-bottom: var(--space-12);
}
.points-panel__title {
  font-size: var(--font-lg);
  line-height: var(--line-height);
  font-weight: 600;
  color: var(--text-primary);
  margin: 0;
}
.points-panel__balance {
  font-size: var(--font-xxl);
  font-weight: 700;
  color: var(--color-primary);
}
.points-panel__balance--muted {
  color: var(--text-muted);
}
.points-panel__history {
  display: flex;
  flex-direction: column;
  gap: var(--space-sm);
  max-height: 240px;
  overflow-y: auto;
}
.points-row {
  display: grid;
  grid-template-columns: 1fr auto auto auto;
  align-items: center;
  gap: var(--space-sm);
  padding: var(--space-xs) 0;
  border-bottom: 1px solid var(--border-color);
  font-size: var(--font-sm);
  color: var(--text-secondary);
}
.points-row__type {
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.points-row__delta.is-gain {
  color: #cf1322; /* 涨：红 */
  font-weight: 600;
}
.points-row__delta.is-loss {
  color: #389e0d; /* 跌：绿 */
  font-weight: 600;
}
.points-panel__hint {
  font-size: var(--font-sm);
  color: var(--text-muted);
  padding: var(--space-sm) 0;
}
</style>
