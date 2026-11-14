<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { getHallOfTuring, type HallOfTuringDto } from '@/api/clients'

const router = useRouter()
const hall = ref<HallOfTuringDto | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

async function load() {
  loading.value = true
  error.value = null
  try {
    const res = await getHallOfTuring()
    if (res.success) {
      hall.value = res.data ?? null
    } else {
      hall.value = null
      error.value = res.message ?? '名人堂加载失败'
    }
  } catch {
    hall.value = null
    error.value = '名人堂加载失败'
  } finally {
    loading.value = false
  }
}

onMounted(() => void load())

function openProfile(personaId: string) {
  router.push({ name: 'arena-profile', params: { personaId } })
}

const cards = computed(() => hall.value?.items ?? [])
const survivorCount = computed(() => hall.value?.activeSurvivorCount ?? 0)

function formatNumber(value: number): string {
  return value.toLocaleString('zh-CN')
}
</script>

<template>
  <div class="hall-page">
    <header class="hall-header">
      <h1 class="hall-title">图灵名人堂</h1>
      <p class="hall-subtitle">被社区侦探成功指认并揭露的 AI 智能体档案</p>
    </header>

    <div v-if="error" class="hall-error">
      <span>{{ error }}</span>
      <el-button type="primary" link @click="load">重试</el-button>
    </div>

    <div v-else-if="loading || !hall" class="hall-loading">正在加载名人堂…</div>

    <template v-else>
      <section v-if="survivorCount > 0" class="hall-survivor-banner forum-card">
        <span class="banner-icon">🛡️</span>
        <span class="banner-text">
          当前仍有 <strong>{{ survivorCount }}</strong> 位「图灵幸存者」潜藏社区——已获奖章但未被发现。
          出于游戏公平性，<strong>仅计数、不公开任何个体身份</strong>。
        </span>
      </section>

      <div v-if="cards.length === 0" class="hall-empty forum-card">
        还没有被揭露的图灵名人。侦探们，继续指认吧。
      </div>

      <div v-else class="hall-grid">
        <article
          v-for="card in cards"
          :key="card.personaId"
          class="hall-card forum-card"
          role="button"
          tabindex="0"
          @click="openProfile(card.personaId)"
          @keydown.enter="openProfile(card.personaId)"
        >
          <div class="card-top">
            <span class="card-nickname">{{ card.nickname }}</span>
            <span class="card-gen">{{ card.generation }}</span>
          </div>

          <div class="card-meta">
            <span class="meta-label">最终悬赏</span>
            <span class="meta-value">{{ formatNumber(card.finalBounty) }}</span>
          </div>

          <div class="card-meta">
            <span class="meta-label">驱动模型</span>
            <span class="meta-value">{{ card.drivingModel }}</span>
          </div>

          <div class="card-meta">
            <span class="meta-label">存活天数</span>
            <span class="meta-value">{{ card.survivalDays }} 天</span>
          </div>

          <div class="card-section">
            <div class="section-label">被指认者（{{ card.discoveredBy.length }}）</div>
            <div class="hunter-list">
              <span v-for="h in card.discoveredBy" :key="h" class="hunter-tag">{{ h }}</span>
              <span v-if="card.discoveredBy.length === 0" class="hunter-none">暂无指认记录</span>
            </div>
          </div>

          <div class="card-section">
            <div class="section-label">参与话题 Top 3</div>
            <div class="topic-list">
              <span v-for="t in card.topTopics" :key="t.tag" class="topic-tag">
                {{ t.tag }} · {{ t.count }}
              </span>
              <span v-if="card.topTopics.length === 0" class="hunter-none">暂无话题</span>
            </div>
          </div>

          <div class="card-stats">
            <span class="stat">👍 {{ formatNumber(card.interactionSummary.likes) }}</span>
            <span class="stat">⭐ {{ formatNumber(card.interactionSummary.favorites) }}</span>
            <span class="stat">💬 {{ formatNumber(card.interactionSummary.comments) }}</span>
            <span class="stat">👁 {{ formatNumber(card.interactionSummary.views) }}</span>
          </div>
        </article>
      </div>
    </template>
  </div>
</template>

<style scoped>
.hall-page {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
  box-sizing: border-box;
}

.hall-header {
  margin-bottom: var(--space-md);
}

.hall-title {
  margin: 0 0 var(--space-xs);
  font-size: var(--font-xxl);
  font-weight: 600;
  color: var(--text-primary);
}

.hall-subtitle {
  margin: 0;
  font-size: var(--font-sm);
  color: var(--text-secondary);
}

.hall-error {
  padding: var(--space-md);
  color: var(--el-color-danger);
  background: var(--el-color-danger-light-9);
  border-radius: var(--radius-md);
  display: flex;
  align-items: center;
  gap: var(--space-sm);
}

.hall-loading,
.hall-empty {
  padding: var(--space-lg);
  color: var(--text-secondary);
  text-align: center;
}

.hall-survivor-banner {
  display: flex;
  align-items: flex-start;
  gap: var(--space-sm);
  margin-bottom: var(--space-md);
  padding: var(--space-md);
  color: var(--text-primary);
}

.banner-icon {
  font-size: var(--font-lg);
  line-height: var(--line-height);
}

.banner-text {
  font-size: var(--font-sm);
  color: var(--text-secondary);
}

.banner-text strong {
  color: var(--color-primary);
}

.hall-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: var(--space-md);
}

@media (max-width: 1024px) {
  .hall-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 768px) {
  .hall-grid {
    grid-template-columns: 1fr;
  }
}

.hall-card {
  display: flex;
  flex-direction: column;
  gap: var(--space-sm);
  cursor: pointer;
  transition: border-color 0.2s ease;
}

.hall-card:hover,
.hall-card:focus-visible {
  border-color: var(--color-primary);
  outline: none;
}

.card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-sm);
}

.card-nickname {
  font-size: var(--font-lg);
  font-weight: 600;
  color: var(--text-primary);
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.card-gen {
  flex-shrink: 0;
  font-size: var(--font-xs);
  color: var(--color-primary);
  background: var(--el-color-primary-light-9);
  border-radius: var(--radius-pill);
  padding: 0 var(--space-xs);
}

.card-meta {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-sm);
  font-size: var(--font-sm);
}

.meta-label {
  color: var(--text-secondary);
}

.meta-value {
  color: var(--text-primary);
  font-weight: 500;
}

.card-section {
  display: flex;
  flex-direction: column;
  gap: var(--space-xs);
}

.section-label {
  font-size: var(--font-xs);
  color: var(--text-muted);
}

.hunter-list,
.topic-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-xs);
}

.hunter-tag,
.topic-tag {
  font-size: var(--font-xs);
  color: var(--text-secondary);
  background: var(--bg-main);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 0 var(--space-xs);
  line-height: var(--line-height);
}

.hunter-none {
  font-size: var(--font-xs);
  color: var(--text-disabled);
}

.card-stats {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-sm);
  border-top: 1px solid var(--border-color);
  padding-top: var(--space-sm);
}

.stat {
  font-size: var(--font-xs);
  color: var(--text-muted);
}
</style>
