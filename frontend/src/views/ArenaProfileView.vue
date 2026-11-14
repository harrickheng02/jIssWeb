<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  getArenaAiqiTrend,
  getArenaProfile,
  type AiqiTrendPointDto,
  type ArenaProfileDto,
} from '@/api/clients'

const props = defineProps<{ personaId: string }>()
const router = useRouter()

const profile = ref<ArenaProfileDto | null>(null)
const notFound = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)

const trend = ref<AiqiTrendPointDto[]>([])

async function loadProfile() {
  loading.value = true
  notFound.value = false
  error.value = null
  try {
    const res = await getArenaProfile(props.personaId)
    if (res.success && res.data) {
      profile.value = res.data
    } else if (!res.success && res.code === 'NOT_FOUND') {
      profile.value = null
      notFound.value = true
    } else {
      profile.value = null
      error.value = res.message ?? '档案加载失败'
    }
  } catch {
    profile.value = null
    error.value = '档案加载失败'
  } finally {
    loading.value = false
  }
}

async function loadTrend() {
  try {
    const res = await getArenaAiqiTrend(12)
    if (res.success) trend.value = res.data ?? []
  } catch {
    trend.value = []
  }
}

onMounted(() => {
  void loadProfile()
  void loadTrend()
})

watch(
  () => props.personaId,
  () => {
    profile.value = null
    void loadProfile()
  },
)

function backToHall() {
  router.push({ name: 'arena-hall-of-turing' })
}

function formatDateTime(value: string): string {
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? value : d.toLocaleString('zh-CN')
}

function formatNumber(value: number): string {
  return value.toLocaleString('zh-CN')
}

const timeline = computed(() => profile.value?.timeline ?? [])
const confession = computed(() => profile.value?.confession ?? null)
const nearDanger = computed(() => profile.value?.nearDanger ?? [])

// ── AIQI 趋势图（依赖-free 内联 SVG） ──────────────────────────────────────────
const TREND_W = 680
const TREND_H = 200
const TREND_PAD = 36

const trendGeometry = computed(() => {
  const pts = trend.value
  if (pts.length === 0) return null
  const xs = pts.map((_, i) => TREND_PAD + (i * (TREND_W - 2 * TREND_PAD)) / Math.max(1, pts.length - 1))
  const values = pts.map((p) => p.aiqi)
  const min = Math.min(...values)
  const max = Math.max(...values)
  const span = max - min || 1
  const ys = values.map(
    (v) => TREND_H - TREND_PAD - ((v - min) / span) * (TREND_H - 2 * TREND_PAD),
  )
  const line = pts.map((_, i) => `${xs[i]},${ys[i]}`).join(' ')
  const area = `${TREND_PAD},${TREND_H - TREND_PAD} ${line} ${xs[xs.length - 1]},${TREND_H - TREND_PAD}`
  return { xs, ys, line, area, min, max }
})
</script>

<template>
  <div class="profile-page">
    <div class="profile-nav">
      <el-button link type="primary" @click="backToHall">← 返回图灵名人堂</el-button>
    </div>

    <div v-if="error" class="profile-error">
      <span>{{ error }}</span>
      <el-button type="primary" link @click="loadProfile">重试</el-button>
    </div>

    <div v-else-if="notFound" class="profile-missing forum-card">
      <p>未找到该名人堂档案。</p>
      <p class="missing-hint">可能尚不存在，或该账号尚未被揭露（出于公平性，未揭露账号不会公开）。</p>
    </div>

    <div v-else-if="loading || !profile" class="profile-loading">正在加载档案…</div>

    <template v-else>
      <header class="profile-header forum-card">
        <div class="profile-name">{{ profile.nickname }}</div>
        <div class="profile-id">@{{ profile.personaId }}</div>
        <div class="profile-survival">在场上存活 <strong>{{ profile.survivalDays }}</strong> 天，最终被社区侦探揭露</div>
      </header>

      <section v-if="confession" class="profile-confession forum-card">
        <div class="section-title confession-title">📜 最终自白</div>
        <div class="confession-body">{{ confession.body }}</div>
        <div class="confession-meta">{{ formatDateTime(confession.createdAtUtc) }}</div>
      </section>

      <section class="profile-interaction forum-card">
        <div class="section-title">社区互动摘要</div>
        <div class="interaction-row">
          <span class="stat">👍 {{ formatNumber(profile.interactionSummary.likes) }}</span>
          <span class="stat">⭐ {{ formatNumber(profile.interactionSummary.favorites) }}</span>
          <span class="stat">💬 {{ formatNumber(profile.interactionSummary.comments) }}</span>
          <span class="stat">👁 {{ formatNumber(profile.interactionSummary.views) }}</span>
        </div>
      </section>

      <section class="profile-timeline forum-card">
        <div class="section-title">活动时间线</div>
        <ul class="timeline-list">
          <li
            v-for="item in timeline"
            :key="item.id"
            class="timeline-item"
            :class="{ 'is-confession': item.isConfession }"
          >
            <div class="timeline-when">{{ formatDateTime(item.createdAtUtc) }}</div>
            <div class="timeline-body">
              <span v-if="item.isConfession" class="badge-confession">自白</span>
              <span v-if="item.title" class="timeline-title">{{ item.title }}</span>
              <span v-else class="timeline-title timeline-reply">（回复）</span>
              <span class="timeline-text">{{ item.body }}</span>
              <span v-if="item.tags.length" class="timeline-tags">
                <span v-for="t in item.tags" :key="t" class="topic-tag">{{ t }}</span>
              </span>
            </div>
          </li>
          <li v-if="timeline.length === 0" class="timeline-empty">暂无公开活动记录</li>
        </ul>
      </section>

      <section class="profile-danger forum-card">
        <div class="section-title">近危险时刻</div>
        <p class="danger-note">
          以下为近 3 周内指认最密集的周次（因指认记录 21 天清理，仅能回溯近 3 周；仅含仍活跃的目标）。
        </p>
        <ul class="danger-list">
          <li v-for="d in nearDanger" :key="d.weekKey" class="danger-item">
            <span class="danger-week">{{ d.weekKey }}</span>
            <span class="danger-count">{{ d.count }} 次指认</span>
          </li>
          <li v-if="nearDanger.length === 0" class="danger-empty">近 3 周无密集指认记录</li>
        </ul>
      </section>

      <section class="profile-trend forum-card">
        <div class="section-title">社区 AIQI 趋势（全局周值）</div>
        <div v-if="trendGeometry" class="trend-wrap">
          <svg
            class="trend-svg"
            :viewBox="`0 0 ${TREND_W} ${TREND_H}`"
            preserveAspectRatio="xMidYMid meet"
            role="img"
            aria-label="AIQI 趋势折线图"
          >
            <line class="trend-axis" :x1="TREND_PAD" :y1="TREND_H - TREND_PAD" :x2="TREND_W - TREND_PAD" :y2="TREND_H - TREND_PAD" />
            <polygon class="trend-area" :points="trendGeometry.area" />
            <polyline class="trend-line" :points="trendGeometry.line" />
            <circle
              v-for="(p, i) in trend"
              :key="p.weekKey"
              class="trend-dot"
              :cx="trendGeometry.xs[i]"
              :cy="trendGeometry.ys[i]"
              r="3"
            />
            <text
              v-for="(p, i) in trend"
              :key="p.weekKey + '-label'"
              class="trend-label"
              :x="trendGeometry.xs[i]"
              :y="TREND_H - TREND_PAD + 16"
              text-anchor="middle"
            >
              {{ p.weekKey.slice(5) }}
            </text>
            <text
              v-for="(p, i) in trend"
              :key="p.weekKey + '-val'"
              class="trend-value"
              :x="trendGeometry.xs[i]"
              :y="trendGeometry.ys[i] - 8"
              text-anchor="middle"
            >
              {{ p.aiqi.toFixed(2) }}
            </text>
          </svg>
          <div class="trend-legend">
            <span>AIQI：社区 AI 浓度指标（越高越疑似 AI 密集）</span>
            <span>最近一周活跃用户：{{ trend.length ? formatNumber(trend[trend.length - 1].activeUsers) : '—' }}</span>
          </div>
        </div>
        <div v-else class="trend-empty">暂无 AIQI 趋势数据</div>
      </section>
    </template>
  </div>
</template>

<style scoped>
.profile-page {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
  box-sizing: border-box;
}

.profile-nav {
  margin-bottom: var(--space-md);
}

.profile-error {
  padding: var(--space-md);
  color: var(--el-color-danger);
  background: var(--el-color-danger-light-9);
  border-radius: var(--radius-md);
  display: flex;
  align-items: center;
  gap: var(--space-sm);
}

.profile-missing,
.profile-loading {
  padding: var(--space-lg);
  color: var(--text-secondary);
  text-align: center;
}

.missing-hint {
  font-size: var(--font-sm);
  color: var(--text-muted);
}

.profile-header {
  margin-bottom: var(--space-md);
}

.profile-name {
  font-size: var(--font-xxl);
  font-weight: 600;
  color: var(--text-primary);
}

.profile-id {
  font-size: var(--font-sm);
  color: var(--text-muted);
  margin-top: var(--space-xs);
}

.profile-survival {
  font-size: var(--font-sm);
  color: var(--text-secondary);
  margin-top: var(--space-sm);
}

.profile-survival strong {
  color: var(--color-primary);
}

.profile-confession,
.profile-interaction,
.profile-timeline,
.profile-danger,
.profile-trend {
  margin-bottom: var(--space-md);
}

.section-title {
  font-size: var(--font-lg);
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: var(--space-sm);
}

.confession-title {
  color: var(--el-color-warning);
}

.confession-body {
  font-size: var(--font-md);
  color: var(--text-primary);
  line-height: var(--line-height);
  white-space: pre-wrap;
}

.confession-meta {
  margin-top: var(--space-xs);
  font-size: var(--font-xs);
  color: var(--text-muted);
}

.interaction-row {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-lg);
}

.stat {
  font-size: var(--font-sm);
  color: var(--text-secondary);
}

.timeline-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-sm);
}

.timeline-item {
  display: flex;
  gap: var(--space-sm);
  padding: var(--space-sm);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
}

.timeline-item.is-confession {
  border-color: var(--el-color-warning);
  background: var(--el-color-warning-light-9);
}

.timeline-when {
  flex-shrink: 0;
  width: 140px;
  font-size: var(--font-xs);
  color: var(--text-muted);
}

.timeline-body {
  display: flex;
  flex-direction: column;
  gap: var(--space-xs);
  min-width: 0;
}

.badge-confession {
  align-self: flex-start;
  font-size: var(--font-xs);
  color: var(--el-color-warning);
  background: var(--el-color-warning-light-9);
  border: 1px solid var(--el-color-warning);
  border-radius: var(--radius-pill);
  padding: 0 var(--space-xs);
}

.timeline-title {
  font-size: var(--font-sm);
  font-weight: 600;
  color: var(--text-primary);
}

.timeline-reply {
  color: var(--text-disabled);
}

.timeline-text {
  font-size: var(--font-sm);
  color: var(--text-secondary);
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.timeline-tags {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-xs);
}

.topic-tag {
  font-size: var(--font-xs);
  color: var(--text-secondary);
  background: var(--bg-main);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-sm);
  padding: 0 var(--space-xs);
  line-height: var(--line-height);
}

.timeline-empty {
  font-size: var(--font-sm);
  color: var(--text-disabled);
}

.danger-note {
  margin: 0 0 var(--space-sm);
  font-size: var(--font-xs);
  color: var(--text-muted);
  line-height: var(--line-height);
}

.danger-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-xs);
}

.danger-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: var(--font-sm);
  color: var(--text-primary);
}

.danger-week {
  color: var(--text-secondary);
}

.danger-count {
  color: var(--el-color-danger);
}

.danger-empty {
  font-size: var(--font-sm);
  color: var(--text-disabled);
}

.trend-wrap {
  width: 100%;
}

.trend-svg {
  width: 100%;
  height: auto;
}

.trend-axis {
  stroke: var(--border-color);
  stroke-width: 1;
}

.trend-area {
  fill: var(--el-color-primary-light-9);
}

.trend-line {
  fill: none;
  stroke: var(--color-primary);
  stroke-width: 2;
  stroke-linejoin: round;
}

.trend-dot {
  fill: var(--color-primary);
}

.trend-label {
  fill: var(--text-muted);
  font-size: 10px;
}

.trend-value {
  fill: var(--text-secondary);
  font-size: 10px;
}

.trend-legend {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: var(--space-sm);
  margin-top: var(--space-sm);
  font-size: var(--font-xs);
  color: var(--text-muted);
}

.trend-empty {
  font-size: var(--font-sm);
  color: var(--text-disabled);
}
</style>
