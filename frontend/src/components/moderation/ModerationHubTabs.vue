<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const active = computed(() => {
  const name = route.name
  if (name === 'moderation-reports') return 'reports'
  if (name === 'moderation-tags') return 'tags'
  return 'audit'
})

function goAudit() {
  void router.push({ name: 'moderation-audit' })
}

function goReports() {
  void router.push({ name: 'moderation-reports' })
}

function goTags() {
  void router.push({ name: 'moderation-tags' })
}
</script>

<template>
  <header class="mod-hub">
    <div class="mod-hub__intro">
      <h1 class="mod-hub__title">治理工作台</h1>
      <p class="mod-hub__desc">审计记录、举报工单与标签配置集中在此处理。</p>
    </div>

    <nav class="mod-hub__nav" role="tablist" aria-label="治理工作台">
      <button
        type="button"
        class="mod-hub__tab"
        :class="{ 'mod-hub__tab--active': active === 'audit' }"
        role="tab"
        :aria-selected="active === 'audit'"
        @click="goAudit"
      >
        审计动态
      </button>
      <button
        type="button"
        class="mod-hub__tab"
        :class="{ 'mod-hub__tab--active': active === 'reports' }"
        role="tab"
        :aria-selected="active === 'reports'"
        @click="goReports"
      >
        举报队列
      </button>
      <button
        type="button"
        class="mod-hub__tab"
        :class="{ 'mod-hub__tab--active': active === 'tags' }"
        role="tab"
        :aria-selected="active === 'tags'"
        @click="goTags"
      >
        标签管理
      </button>
    </nav>
  </header>
</template>

<style scoped>
.mod-hub {
  margin-bottom: var(--space-lg);
}

.mod-hub__intro {
  margin-bottom: var(--space-md);
}

.mod-hub__title {
  margin: 0;
  font-size: var(--font-xl);
  font-weight: 600;
  line-height: var(--line-height);
  color: var(--text-primary);
}

.mod-hub__desc {
  margin: var(--space-sm) 0 0;
  font-size: var(--font-sm);
  line-height: var(--line-height);
  color: var(--text-secondary);
}

.mod-hub__nav {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-12);
  padding: var(--space-12);
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  box-sizing: border-box;
}

.mod-hub__tab {
  min-height: 40px;
  padding: var(--space-12) var(--space-lg);
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--text-secondary);
  font-size: var(--font-md);
  line-height: var(--line-height);
  font-weight: 500;
  cursor: pointer;
  transition:
    color 0.15s ease,
    background-color 0.15s ease,
    border-color 0.15s ease;
}

.mod-hub__tab:hover {
  color: var(--text-primary);
  background: var(--hover-bg);
}

.mod-hub__tab--active {
  color: var(--color-primary);
  background: var(--el-color-primary-light-9);
  border-color: var(--el-color-primary-light-7);
  font-weight: 600;
}

.mod-hub__tab--active:hover {
  color: var(--color-primary);
  background: var(--el-color-primary-light-9);
}

@media (max-width: 768px) {
  .mod-hub__nav {
    flex-direction: column;
  }

  .mod-hub__tab {
    width: 100%;
    text-align: center;
  }
}
</style>
