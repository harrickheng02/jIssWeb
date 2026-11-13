<script setup lang="ts">
import { onMounted } from 'vue'
import { useHunterBadge } from '@/composables/useHunterBadge'

const { badge, loading, error, load } = useHunterBadge()

onMounted(() => {
  void load()
})

function formatDate(value: string) {
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? value : d.toLocaleDateString('zh-CN')
}
</script>

<template>
  <el-card class="hunter-badge-card" shadow="never">
    <template #header>图灵猎手</template>

    <div v-if="loading" class="hunter-badge-state">正在加载…</div>

    <div v-else-if="error" class="hunter-badge-state">
      <span>{{ error }}</span>
      <el-button type="primary" link @click="load">重试</el-button>
    </div>

    <div v-else-if="badge" class="hunter-badge-content">
      <div class="hunter-badge-title">图灵猎手 · 永久成就</div>
      <p class="hunter-badge-desc">你曾成功指认传说级猎物，获得全额悬赏积分。</p>
      <el-descriptions :column="1" border>
        <el-descriptions-item label="悬赏积分">{{ badge.bountyPoints }}</el-descriptions-item>
        <el-descriptions-item label="达成周">{{ badge.targetWeekKey }}</el-descriptions-item>
        <el-descriptions-item label="授予时间">{{ formatDate(badge.awardedAtUtc) }}</el-descriptions-item>
      </el-descriptions>
    </div>

    <div v-else class="hunter-badge-state">暂无徽章。成功指认传说级猎物后即可获得。</div>
  </el-card>
</template>

<style scoped>
.hunter-badge-card {
  border-radius: var(--radius-lg);
}

.hunter-badge-content {
  padding: var(--space-sm) 0;
}

.hunter-badge-title {
  font-size: var(--font-lg);
  font-weight: 600;
  color: var(--text-primary);
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.hunter-badge-desc {
  margin: var(--space-sm) 0 var(--space-md);
  color: var(--text-secondary);
}

.hunter-badge-state {
  padding: var(--space-md) 0;
  color: var(--text-secondary);
}
</style>
