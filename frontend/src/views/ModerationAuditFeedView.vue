<script setup lang="ts">
import {
  exportModerationAuditCsv,
  getForumBoards,
  listModerationAuditFeed,
  type ModerationAuditFeedItem,
} from '@/api/clients'
import {
  ALL_MODERATION_AUDIT_ACTIONS,
  MODERATION_AUDIT_ACTION_FILTER_OPTIONS,
  moderationAuditActionQueryValue,
  type ModerationAuditActionFilterValue,
} from '@/constants/moderationAuditActions'
import { useAuthStore } from '@/stores/auth'
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

const auth = useAuthStore()
const router = useRouter()

const isAdmin = computed(() => auth.forumRole === 'admin')

const loading = ref(false)
const exportBusy = ref(false)
const error = ref<string | null>(null)
const exportError = ref<string | null>(null)
const items = ref<ModerationAuditFeedItem[]>([])
const totalCount = ref(0)
const page = ref(1)
const pageSize = ref(20)

const ALL_BOARDS = 'all' as const

type BoardFilterValue = typeof ALL_BOARDS | string

const actionFilter = ref<ModerationAuditActionFilterValue>(ALL_MODERATION_AUDIT_ACTIONS)
const timeRange = ref<[Date, Date] | null>(null)
const boardFilter = ref<BoardFilterValue>(ALL_BOARDS)

const boardOptions = ref<Array<{ id: string; title: string }>>([])

const auditActionOptions = MODERATION_AUDIT_ACTION_FILTER_OPTIONS

const visibleBoardOptions = computed(() => {
  if (isAdmin.value) {
    return [{ id: ALL_BOARDS, title: '全站' }, ...boardOptions.value]
  }
  const scopeIds = new Set(auth.forumBoardIds)
  if (scopeIds.size === 0) {
    return [{ id: ALL_BOARDS, title: '全部可见版区' }]
  }
  const scoped = boardOptions.value.filter((b) => scopeIds.has(b.id))
  return [{ id: ALL_BOARDS, title: '全部可见版区' }, ...scoped]
})

function toUtcIso(d: Date) {
  return d.toISOString()
}

function formatUtc(iso: string) {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleString('zh-CN')
}

function feedQueryOptions() {
  return {
    page: page.value,
    pageSize: pageSize.value,
    action: moderationAuditActionQueryValue(actionFilter.value),
    fromUtc: timeRange.value?.[0] ? toUtcIso(timeRange.value[0]) : undefined,
    toUtc: timeRange.value?.[1] ? toUtcIso(timeRange.value[1]) : undefined,
    boardId: boardFilter.value === ALL_BOARDS ? undefined : boardFilter.value,
  }
}

async function loadFeed() {
  loading.value = true
  error.value = null
  try {
    const res = await listModerationAuditFeed(feedQueryOptions())
    if (!res.success || !res.data) {
      error.value = res.message ?? '加载失败'
      items.value = []
      totalCount.value = 0
      return
    }
    items.value = res.data.items
    totalCount.value = res.data.totalCount
  } finally {
    loading.value = false
  }
}

async function onExport() {
  exportBusy.value = true
  exportError.value = null
  try {
    const res = await exportModerationAuditCsv(feedQueryOptions())
    if (!res.success) {
      if (res.code === 'EXPORT_TOO_LARGE') {
        exportError.value = '导出结果过多，请缩小时间范围或筛选条件'
      } else {
        exportError.value = res.message ?? '导出失败'
      }
      return
    }
    const url = URL.createObjectURL(res.blob)
    const a = document.createElement('a')
    a.href = url
    a.download = res.filename
    a.click()
    URL.revokeObjectURL(url)
  } finally {
    exportBusy.value = false
  }
}

function onFilterChange() {
  if (page.value !== 1) page.value = 1
  else void loadFeed()
}

function postHref(postId: string) {
  return router.resolve({ name: 'post-detail', params: { id: postId } }).href
}

onMounted(async () => {
  const boardsRes = await getForumBoards()
  if (boardsRes.success && boardsRes.data) {
    boardOptions.value = boardsRes.data.map((b) => ({ id: b.id, title: b.title }))
  }
  await loadFeed()
})

watch(page, () => void loadFeed())
</script>

<template>
  <div class="mod-audit-feed">
    <el-card shadow="never" class="mod-audit-feed__card" v-loading="loading">
      <template #header>
        <div class="mod-audit-feed__header">
          <p class="mod-audit-feed__lead">
            按时间浏览全站或版区治理操作记录；默认展示最近 30 天。可导出 CSV 归档。
          </p>
          <el-button type="primary" plain :loading="exportBusy" @click="onExport">导出 CSV</el-button>
        </div>
      </template>

          <p v-if="exportError" class="mod-audit-feed__error">{{ exportError }}</p>

          <div class="mod-audit-feed__filters">
            <div class="mod-audit-feed__filter-field">
              <span class="mod-audit-feed__filter-label">操作</span>
              <el-select
                v-model="actionFilter"
                class="mod-audit-feed__filter mod-audit-feed__filter--action"
                @change="onFilterChange"
              >
                <el-option
                  v-for="opt in auditActionOptions"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
            </div>
            <div class="mod-audit-feed__filter-field mod-audit-feed__filter-field--time">
              <span class="mod-audit-feed__filter-label">时间</span>
              <el-date-picker
                v-model="timeRange"
                class="mod-audit-feed__filter mod-audit-feed__filter--time"
                type="datetimerange"
                range-separator="至"
                start-placeholder="开始时间"
                end-placeholder="结束时间"
                @change="onFilterChange"
              />
            </div>
            <div class="mod-audit-feed__filter-field">
              <span class="mod-audit-feed__filter-label">版区</span>
              <el-select
                v-model="boardFilter"
                class="mod-audit-feed__filter mod-audit-feed__filter--board"
                @change="onFilterChange"
              >
                <el-option
                  v-for="b in visibleBoardOptions"
                  :key="b.id"
                  :label="b.title"
                  :value="b.id"
                />
              </el-select>
            </div>
          </div>

          <p v-if="error" class="mod-audit-feed__error">{{ error }}</p>
          <p v-else-if="!loading && items.length === 0" class="mod-audit-feed__empty">暂无匹配的审计记录</p>

          <el-table v-else :data="items" class="mod-audit-feed__table" stripe>
            <el-table-column label="时间" min-width="160">
              <template #default="{ row }">{{ formatUtc(row.occurredAtUtc) }}</template>
            </el-table-column>
            <el-table-column prop="actionLabel" label="操作" min-width="120" />
            <el-table-column prop="operatorDisplayName" label="操作人" min-width="100" />
            <el-table-column prop="boardLabel" label="版区" min-width="88" />
            <el-table-column label="目标" min-width="140">
              <template #default="{ row }">
                <span class="mod-audit-feed__target">{{ row.targetType }} / {{ row.targetId }}</span>
              </template>
            </el-table-column>
            <el-table-column label="关联" min-width="120">
              <template #default="{ row }">
                <a
                  v-if="row.postId"
                  class="mod-audit-feed__link"
                  :href="postHref(row.postId)"
                  target="_blank"
                  rel="noopener"
                >
                  帖子
                </a>
                <span v-if="row.reportId" class="mod-audit-feed__report-id">{{ row.reportId }}</span>
              </template>
            </el-table-column>
          </el-table>

          <el-pagination
            v-if="totalCount > pageSize"
            class="mod-audit-feed__pager"
            layout="prev, pager, next"
            :total="totalCount"
            :page-size="pageSize"
            :current-page="page"
            @current-change="(p: number) => (page = p)"
          />
    </el-card>
  </div>
</template>

<style scoped>
.mod-audit-feed__card {
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
}

.mod-audit-feed__header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-16);
}

.mod-audit-feed__lead {
  margin: 0;
  flex: 1;
  min-width: 200px;
  font-size: var(--font-sm);
  line-height: var(--line-height);
  color: var(--text-secondary);
}

.mod-audit-feed__filters {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: end;
  column-gap: var(--space-16);
  row-gap: var(--space-16);
  margin-bottom: var(--space-16);
}

.mod-audit-feed__filter-field {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.mod-audit-feed__filter-field:first-child {
  justify-self: start;
}

.mod-audit-feed__filter-field--time {
  justify-self: center;
  min-width: 280px;
}

.mod-audit-feed__filter-field:last-child {
  justify-self: end;
}

.mod-audit-feed__filter-label {
  font-size: var(--font-xs);
  color: var(--text-secondary);
  line-height: var(--line-height);
}

.mod-audit-feed__filter {
  width: 100%;
}

.mod-audit-feed__filter--action {
  width: 148px;
}

.mod-audit-feed__filter--board {
  width: 200px;
}

.mod-audit-feed__filter--time {
  width: 360px;
  max-width: 100%;
}

.mod-audit-feed__hint,
.mod-audit-feed__empty {
  color: var(--text-secondary);
  font-size: var(--font-sm);
}

.mod-audit-feed__error {
  color: var(--el-color-danger);
  font-size: var(--font-sm);
  margin-bottom: var(--space-12);
}

.mod-audit-feed__target {
  font-size: var(--font-sm);
  color: var(--text-secondary);
}

.mod-audit-feed__link {
  color: var(--color-primary);
  font-size: var(--font-sm);
  margin-right: var(--space-8);
}

.mod-audit-feed__report-id {
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.mod-audit-feed__pager {
  margin-top: var(--space-16);
  justify-content: flex-end;
}

@media (max-width: 768px) {
  .mod-audit-feed__filters {
    grid-template-columns: 1fr;
  }

  .mod-audit-feed__filter-field:first-child,
  .mod-audit-feed__filter-field--time,
  .mod-audit-feed__filter-field:last-child {
    justify-self: stretch;
  }

  .mod-audit-feed__filter--action,
  .mod-audit-feed__filter--board {
    width: 100%;
  }

  .mod-audit-feed__filter--time {
    max-width: none;
  }
}
</style>
