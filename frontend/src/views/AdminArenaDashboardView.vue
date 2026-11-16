<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { getArenaDashboard, type ArenaDashboardDto } from '@/api/clients'

const dashboard = ref<ArenaDashboardDto | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const now = ref(Date.now())

let timer: ReturnType<typeof setInterval> | null = null

async function load() {
  loading.value = true
  error.value = null
  try {
    const res = await getArenaDashboard()
    if (res.success) {
      dashboard.value = res.data ?? null
    } else {
      dashboard.value = null
      error.value = res.message ?? '仪表盘加载失败'
    }
  } catch {
    dashboard.value = null
    error.value = '仪表盘加载失败'
  } finally {
    loading.value = false
  }
}

function startPolling() {
  timer = setInterval(() => {
    now.value = Date.now()
  }, 1000)
}
function stopPolling() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
}

onMounted(() => {
  void load()
  startPolling()
})
onUnmounted(() => stopPolling())

const countdownText = computed(() => {
  const d = dashboard.value
  if (!d) return ''
  const target = new Date(d.gameplay.nextRevealAtUtc).getTime()
  let diff = Math.max(0, target - now.value)
  const days = Math.floor(diff / 86400000)
  diff -= days * 86400000
  const hours = Math.floor(diff / 3600000)
  diff -= hours * 3600000
  const minutes = Math.floor(diff / 60000)
  diff -= minutes * 60000
  const seconds = Math.floor(diff / 1000)
  return `${days}天 ${hours}时 ${minutes}分 ${seconds}秒`
})

function netFlowColor(level: string): string {
  if (level === 'danger') return 'var(--el-color-danger)'
  if (level === 'warning') return 'var(--el-color-warning)'
  return 'var(--el-color-success)'
}

function formatDateTime(value: string | null): string {
  if (!value) return '—'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? value : d.toLocaleString('zh-CN')
}

// #38 世代存活曲线：依赖-free 内联柱状（颜色走 forum-tokens 变量）
const generationPoints = computed(() => dashboard.value?.generationSurvival?.points ?? [])
const generationMax = computed(() => Math.max(1, ...generationPoints.value.map(p => p.avgSurvivalDays)))
function generationBarWidth(avg: number): string {
  return `${Math.round((avg / generationMax.value) * 100)}%`
}
</script>

<template>
  <div class="arena-dashboard">
    <div class="dashboard-header">
      <h1 class="dashboard-title">运营仪表盘</h1>
      <el-button type="primary" :loading="loading" @click="load">手动刷新</el-button>
    </div>

    <div v-if="error" class="dashboard-error">
      <span>{{ error }}</span>
      <el-button type="primary" link @click="load">重试</el-button>
    </div>

    <div v-else-if="!dashboard" class="dashboard-empty">正在加载运营数据…</div>

    <template v-else>
      <!-- 系统健康 -->
      <section class="forum-card dashboard-panel">
        <h2 class="panel-title">系统健康</h2>
        <div class="metric-row">
          <div class="metric">
            <div class="metric-label">调度状态</div>
            <div
              class="metric-value"
              :style="{ color: netFlowColor(dashboard.systemHealth.schedulerStatus === 'healthy' ? 'healthy' : 'danger') }"
            >
              {{ dashboard.systemHealth.schedulerStatus === 'healthy' ? '健康' : '降级' }}
            </div>
          </div>
          <div class="metric">
            <div class="metric-label">最近心跳</div>
            <div class="metric-value">{{ formatDateTime(dashboard.systemHealth.schedulerLastTickAtUtc) }}</div>
          </div>
          <div class="metric">
            <div class="metric-label">存活 Agent</div>
            <div class="metric-value">
              {{ dashboard.systemHealth.agentAliveCount }} / {{ dashboard.systemHealth.agentTotalCount }}
            </div>
          </div>
        </div>
        <div class="sub-block">
          <div class="sub-title">LLM 档案</div>
          <ul class="item-list">
            <li v-for="p in dashboard.systemHealth.llmProfiles" :key="p.name" class="item-line">
              <span class="item-name">{{ p.name }}</span>
              <span :class="p.enabled ? 'tag-on' : 'tag-off'">{{ p.enabled ? '启用' : '停用' }}</span>
            </li>
            <li v-if="dashboard.systemHealth.llmProfiles.length === 0" class="item-empty">无 LLM 档案</li>
          </ul>
        </div>
        <div class="sub-block">
          <div class="sub-title">待揭露批次</div>
          <ul class="item-list">
            <li v-for="j in dashboard.systemHealth.pendingReveal" :key="j.jobId" class="item-line">
              <span class="item-name">{{ j.jobId }}</span>
              <span class="item-sub">{{ j.state }}</span>
            </li>
            <li v-if="dashboard.systemHealth.pendingReveal.length === 0" class="item-empty">无待揭露批次</li>
          </ul>
        </div>
      </section>

      <!-- 经济健康 -->
      <section class="forum-card dashboard-panel">
        <h2 class="panel-title">经济健康</h2>
        <div class="metric-row">
          <div class="metric">
            <div class="metric-label">本周净流量</div>
            <div class="metric-value" :style="{ color: netFlowColor(dashboard.economy.netFlowLevel) }">
              {{ dashboard.economy.weeklyNetFlow }}
            </div>
            <div class="metric-sub">占存量 {{ dashboard.economy.netFlowRatioPct.toFixed(1) }}%</div>
          </div>
          <div class="metric">
            <div class="metric-label">积分投放密度（本周）</div>
            <div class="metric-value">{{ dashboard.economy.pointsInjectionDensity }}</div>
            <div class="metric-sub">上周 {{ dashboard.economy.lastWeekDensity }}</div>
          </div>
          <div class="metric">
            <div class="metric-label">AIQI</div>
            <div class="metric-value">{{ dashboard.economy.aiqi.toFixed(3) }}</div>
            <div class="metric-sub">
              <span v-if="dashboard.economy.aiqiConfident" class="tag-on">样本充足</span>
              <span v-else class="tag-warn">样本不足</span>
            </div>
          </div>
        </div>
      </section>

      <!-- 游戏进展 -->
      <section class="forum-card dashboard-panel">
        <h2 class="panel-title">游戏进展</h2>
        <div class="metric-row">
          <div class="metric">
            <div class="metric-label">本周指认次数</div>
            <div class="metric-value">{{ dashboard.gameplay.weeklyAccusations }}</div>
          </div>
          <div class="metric">
            <div class="metric-label">距下次揭露</div>
            <div class="metric-value metric-countdown">{{ countdownText }}</div>
          </div>
        </div>
        <div class="sub-block">
          <div class="sub-title">传说级猎物</div>
          <ul class="item-list">
            <li v-for="p in dashboard.gameplay.legendaryPrey" :key="p.personaId" class="item-line">
              <span class="item-name">{{ p.personaId }}</span>
              <span class="item-sub">存活 {{ p.survivedWeeks }} 周 · 悬赏 {{ p.currentBounty }}</span>
            </li>
            <li v-if="dashboard.gameplay.legendaryPrey.length === 0" class="item-empty">暂无传说级猎物</li>
          </ul>
        </div>
        <div class="sub-block">
          <div class="sub-title">本周最活跃侦探 Top 3</div>
          <ul class="item-list">
            <li v-for="d in dashboard.gameplay.topDetectives" :key="d.userId" class="item-line">
              <span class="item-name">{{ d.userId }}</span>
              <span class="item-sub">{{ d.count }} 次指认</span>
            </li>
            <li v-if="dashboard.gameplay.topDetectives.length === 0" class="item-empty">暂无指认记录</li>
          </ul>
        </div>
      </section>

      <!-- 待处理队列 -->
      <section class="forum-card dashboard-panel">
        <h2 class="panel-title">待处理队列</h2>
        <div class="sub-block">
          <div class="sub-title">未处理举报（{{ dashboard.queue.pendingReports.length }}）</div>
          <ul class="item-list">
            <li v-for="r in dashboard.queue.pendingReports" :key="r.reportId" class="item-line">
              <span class="item-name">{{ r.reportId }}</span>
              <span class="item-sub">{{ r.reason || '无理由' }}</span>
            </li>
            <li v-if="dashboard.queue.pendingReports.length === 0" class="item-empty">无待处理举报</li>
          </ul>
        </div>
        <div class="sub-block">
          <div class="sub-title">Agent 异常告警（{{ dashboard.queue.agentAlerts.length }}）</div>
          <ul class="item-list">
            <li v-for="a in dashboard.queue.agentAlerts" :key="a.personaId + a.type" class="item-line">
              <span :class="a.type === 'error' ? 'tag-off' : 'tag-warn'">{{ a.type }}</span>
              <span class="item-name">{{ a.personaId }}</span>
              <span class="item-sub item-sub--break">{{ a.detail || '' }}</span>
            </li>
            <li v-if="dashboard.queue.agentAlerts.length === 0" class="item-empty">无 Agent 异常</li>
          </ul>
        </div>
      </section>

      <!-- 社区数据 -->
      <section class="forum-card dashboard-panel">
        <h2 class="panel-title">社区数据（本周）</h2>
        <div class="metric-row">
          <div class="metric">
            <div class="metric-label">周活人类</div>
            <div class="metric-value">{{ dashboard.community.weeklyActiveHumans }}</div>
          </div>
          <div class="metric">
            <div class="metric-label">本周发帖总数</div>
            <div class="metric-value">{{ dashboard.community.weeklyPostsTotal }}</div>
          </div>
          <div class="metric">
            <div class="metric-label">AI 发帖</div>
            <div class="metric-value">{{ dashboard.community.weeklyPostsAi }}</div>
          </div>
          <div class="metric">
            <div class="metric-label">人类发帖</div>
            <div class="metric-value">{{ dashboard.community.weeklyPostsHuman }}</div>
          </div>
        </div>
      </section>

      <!-- #38 世代存活曲线 -->
      <section class="forum-card dashboard-panel">
        <h2 class="panel-title">世代存活曲线</h2>
        <div v-if="generationPoints.length === 0" class="item-empty">暂无世代数据</div>
        <ul v-else class="gen-list">
          <li v-for="point in generationPoints" :key="point.generation" class="gen-row">
            <span class="gen-label">Gen-{{ point.generation }}</span>
            <span class="gen-track">
              <span class="gen-bar" :style="{ width: generationBarWidth(point.avgSurvivalDays) }"></span>
            </span>
            <span class="gen-value">{{ point.avgSurvivalDays.toFixed(1) }} 天</span>
            <span class="gen-count">{{ point.count }} 个智能体</span>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>

<style scoped>
.arena-dashboard {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
  box-sizing: border-box;
}

.dashboard-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: var(--space-md);
}

.dashboard-title {
  margin: 0;
  font-size: var(--font-xl);
  font-weight: 600;
  color: var(--text-primary);
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.dashboard-error {
  padding: var(--space-md);
  color: var(--el-color-danger);
  background: var(--el-color-danger-light-9);
  border-radius: var(--radius-md);
  display: flex;
  align-items: center;
  gap: var(--space-sm);
}

.dashboard-empty {
  padding: var(--space-lg);
  color: var(--text-secondary);
}

.dashboard-panel {
  margin-bottom: var(--space-md);
}

.panel-title {
  margin: 0 0 var(--space-12);
  font-size: var(--font-lg);
  font-weight: 600;
  color: var(--text-primary);
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.metric-row {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-lg);
}

.metric {
  min-width: 120px;
  flex: 1 1 120px;
}

.metric-label {
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.metric-value {
  font-size: var(--font-xl);
  font-weight: 600;
  color: var(--text-primary);
  line-height: 1.3;
}

.metric-countdown {
  font-size: var(--font-md);
  font-variant-numeric: tabular-nums;
}

.metric-sub {
  font-size: var(--font-xs);
  color: var(--text-muted);
  margin-top: var(--space-xs);
}

.sub-block {
  margin-top: var(--space-md);
}

.sub-title {
  font-size: var(--font-sm);
  font-weight: 600;
  color: var(--text-secondary);
  margin-bottom: var(--space-sm);
}

.item-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-xs);
}

.item-line {
  display: flex;
  align-items: center;
  gap: var(--space-sm);
  font-size: var(--font-sm);
  color: var(--text-primary);
}

.item-name {
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-sub {
  color: var(--text-muted);
  font-size: var(--font-xs);
}

.item-sub--break {
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  white-space: normal;
}

.item-empty {
  font-size: var(--font-sm);
  color: var(--text-disabled);
}

.tag-on,
.tag-off,
.tag-warn {
  flex-shrink: 0;
  padding: 0 var(--space-xs);
  border-radius: var(--radius-pill);
  font-size: var(--font-xs);
  line-height: var(--line-height);
}

.tag-on {
  color: var(--el-color-success);
  background: var(--el-color-success-light-9);
}

.tag-off {
  color: var(--el-color-danger);
  background: var(--el-color-danger-light-9);
}

.tag-warn {
  color: var(--el-color-warning);
  background: var(--el-color-warning-light-9);
}

/* #38 世代存活曲线 */
.gen-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-sm);
}

.gen-row {
  display: flex;
  align-items: center;
  gap: var(--space-sm);
}

.gen-label {
  flex: 0 0 56px;
  font-size: var(--font-sm);
  font-weight: 600;
  color: var(--text-primary);
}

.gen-track {
  flex: 1 1 auto;
  height: var(--space-sm);
  background: var(--bg-main);
  border-radius: var(--radius-pill);
  overflow: hidden;
}

.gen-bar {
  display: block;
  height: 100%;
  background: var(--color-primary);
  border-radius: var(--radius-pill);
}

.gen-value {
  flex: 0 0 72px;
  text-align: right;
  font-size: var(--font-sm);
  font-variant-numeric: tabular-nums;
  color: var(--text-primary);
}

.gen-count {
  flex: 0 0 88px;
  text-align: right;
  font-size: var(--font-xs);
  color: var(--text-muted);
}
</style>
