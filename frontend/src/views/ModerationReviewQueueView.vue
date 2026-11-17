<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { CaretBottom, CaretRight } from '@element-plus/icons-vue'
import type { ReviewQueueItemDto } from '@/api/clients'
import {
  approveModerationReviewItem,
  listModerationReviewQueue,
  rejectModerationReviewItem,
} from '@/api/clients'

const loading = ref(false)
const busyId = ref('')
const errorText = ref('')
const items = ref<ReviewQueueItemDto[]>([])
const totalCount = ref(0)
const page = ref(1)
const pageSize = ref(20)
const statusFilter = ref('pending')
const expandedId = ref('')

const statusOptions = [
  { value: 'pending', label: '待裁决' },
  { value: 'flagged', label: '已标记' },
  { value: '', label: '全部' },
]

const verdictLabels: Record<string, string> = {
  spam: '广告推广',
  sexual: '色情低俗',
  political: '政治敏感',
  other: '其他违规',
}

const sourceLabels: Record<string, string> = {
  llm: '模型判定',
  duplicate: '重复内容',
}

const statusLabels: Record<string, string> = {
  pending: '待裁决',
  flagged: '已标记',
  approved: '已通过',
  rejected: '已驳回',
}

const isEmpty = computed(() => !loading.value && items.value.length === 0)

function verdictLabel(row: ReviewQueueItemDto) {
  if (!row.verdict) return '—'
  return verdictLabels[row.verdict] ?? row.verdict
}

function confidenceText(row: ReviewQueueItemDto) {
  return typeof row.confidence === 'number' ? `${Math.round(row.confidence * 100)}%` : '—'
}

function formatUtc(value?: string | null) {
  if (!value) return '—'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString()
}

function toggleRow(row: ReviewQueueItemDto) {
  expandedId.value = expandedId.value === row.id ? '' : row.id
}

async function load() {
  loading.value = true
  errorText.value = ''
  const res = await listModerationReviewQueue(page.value, pageSize.value, statusFilter.value)
  loading.value = false
  if (!res.success) {
    errorText.value = res.message ?? '加载失败'
    items.value = []
    totalCount.value = 0
    return
  }
  const data = res.data as { items: ReviewQueueItemDto[]; totalCount: number }
  items.value = data.items ?? []
  totalCount.value = data.totalCount ?? 0
}

function onFilterChange() {
  page.value = 1
  expandedId.value = ''
  void load()
}

function onPageChange(next: number) {
  page.value = next
  void load()
}

async function approve(row: ReviewQueueItemDto) {
  busyId.value = row.id
  const res = await approveModerationReviewItem(row.id)
  busyId.value = ''
  if (!res.success) {
    ElMessage.error(res.message ?? '操作失败')
    return
  }
  ElMessage.success('已通过，内容恢复公开')
  await load()
}

async function reject(row: ReviewQueueItemDto) {
  try {
    await ElMessageBox.confirm('驳回将删除该内容，且写入审计记录。确认驳回？', '驳回待审内容', {
      type: 'warning',
      confirmButtonText: '驳回并删除',
      cancelButtonText: '取消',
    })
  } catch {
    return
  }
  busyId.value = row.id
  const res = await rejectModerationReviewItem(row.id)
  busyId.value = ''
  if (!res.success) {
    ElMessage.error(res.message ?? '操作失败')
    return
  }
  ElMessage.success('已驳回并删除')
  await load()
}

onMounted(load)
</script>

<template>
  <section class="review-queue">
    <div class="review-queue__head">
      <p class="review-queue__lead">
        <strong>内容复审</strong>由 AI 预筛：高置信度违规内容会先转为待审（仅作者与版主可见），低置信度只做标记。
        最终处置权在运营者——<strong>通过</strong>恢复公开，<strong>驳回</strong>删除内容并写审计。
      </p>
      <el-select v-model="statusFilter" class="review-queue__filter" placeholder="状态" @change="onFilterChange">
        <el-option v-for="opt in statusOptions" :key="opt.value || 'all'" :label="opt.label" :value="opt.value" />
      </el-select>
    </div>

    <div v-if="errorText" class="review-queue__error">{{ errorText }}</div>

    <el-skeleton v-if="loading" :rows="6" animated />

    <div v-else-if="isEmpty" class="review-queue__empty">暂无待复审内容</div>

    <div v-else class="review-queue__list">
      <div
        v-for="row in items"
        :key="row.id"
        class="review-queue__row"
        tabindex="0"
        :aria-expanded="expandedId === row.id"
        aria-label="复审条目，点击展开或收起"
        @click="toggleRow(row)"
        @keydown.enter.prevent="toggleRow(row)"
        @keydown.space.prevent="toggleRow(row)"
      >
        <div class="review-queue__summary">
          <el-icon class="review-queue__caret" aria-hidden="true">
            <CaretBottom v-if="expandedId === row.id" />
            <CaretRight v-else />
          </el-icon>

          <div class="review-queue__tags">
            <el-tag size="small" effect="plain">{{ row.boardId || '未知版区' }}</el-tag>
            <el-tag
              size="small"
              effect="plain"
              :type="row.status === 'pending' ? 'warning' : row.status === 'flagged' ? 'info' : 'success'"
            >
              {{ statusLabels[row.status] ?? row.status }}
            </el-tag>
            <el-tag size="small" effect="plain" type="danger">{{ verdictLabel(row) }}</el-tag>
            <span class="review-queue__meta">{{ confidenceText(row) }}</span>
            <span class="review-queue__meta">{{ sourceLabels[row.source] ?? row.source }}</span>
          </div>

          <div class="review-queue__title">{{ row.title || row.contentExcerpt || row.targetId }}</div>

          <div class="review-queue__actions" @click.stop>
            <el-button
              size="small"
              type="primary"
              :loading="busyId === row.id"
              :disabled="row.status === 'approved' || row.status === 'rejected'"
              @click="approve(row)"
            >
              通过
            </el-button>
            <el-button
              size="small"
              type="danger"
              plain
              :loading="busyId === row.id"
              :disabled="row.status === 'approved' || row.status === 'rejected'"
              @click="reject(row)"
            >
              驳回
            </el-button>
          </div>
        </div>

        <div v-if="expandedId === row.id" class="review-queue__detail">
          <p class="review-queue__excerpt">{{ row.contentExcerpt || '（内容已删除）' }}</p>
          <dl class="review-queue__facts">
            <div>
              <dt>判定理由</dt>
              <dd>{{ row.reason || '—' }}</dd>
            </div>
            <div>
              <dt>内容状态</dt>
              <dd>{{ row.contentState || '—' }}</dd>
            </div>
            <div>
              <dt>作者</dt>
              <dd>{{ row.authorSubId || '—' }}</dd>
            </div>
            <div>
              <dt>入队时间</dt>
              <dd>{{ formatUtc(row.createdAtUtc) }}</dd>
            </div>
          </dl>
        </div>
      </div>
    </div>

    <div v-if="totalCount > pageSize" class="review-queue__pager">
      <el-pagination
        layout="prev, pager, next"
        :current-page="page"
        :page-size="pageSize"
        :total="totalCount"
        @current-change="onPageChange"
      />
    </div>
  </section>
</template>

<style scoped>
.review-queue {
  display: flex;
  flex-direction: column;
  gap: var(--space-16);
}

.review-queue__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-16);
  padding: var(--space-16);
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  box-sizing: border-box;
}

.review-queue__lead {
  margin: 0;
  font-size: var(--font-sm);
  line-height: var(--line-height);
  color: var(--text-secondary);
}

.review-queue__filter {
  width: 160px;
  flex: 0 0 auto;
}

.review-queue__error {
  padding: var(--space-12) var(--space-16);
  border-radius: var(--radius-md);
  background: var(--el-color-danger-light-9);
  color: var(--el-color-danger);
  font-size: var(--font-sm);
}

.review-queue__empty {
  padding: var(--space-32) var(--space-16);
  text-align: center;
  color: var(--text-secondary);
  font-size: var(--font-sm);
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
}

.review-queue__list {
  display: flex;
  flex-direction: column;
  gap: var(--space-8);
}

.review-queue__row {
  padding: var(--space-12) var(--space-16);
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  cursor: pointer;
  box-sizing: border-box;
  transition: border-color 0.15s ease;
}

.review-queue__row:hover {
  border-color: var(--color-primary);
}

.review-queue__summary {
  display: flex;
  align-items: center;
  gap: var(--space-12);
}

.review-queue__caret {
  color: var(--text-secondary);
  flex: 0 0 auto;
}

.review-queue__tags {
  display: flex;
  align-items: center;
  gap: var(--space-8);
  flex: 0 0 auto;
}

.review-queue__meta {
  font-size: var(--font-xs);
  color: var(--text-secondary);
  white-space: nowrap;
}

.review-queue__title {
  flex: 1 1 auto;
  min-width: 0;
  font-size: var(--font-md);
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.review-queue__actions {
  display: flex;
  gap: var(--space-8);
  flex: 0 0 auto;
}

.review-queue__detail {
  margin-top: var(--space-12);
  padding-top: var(--space-12);
  border-top: 1px solid var(--border-color);
}

.review-queue__excerpt {
  margin: 0 0 var(--space-12);
  font-size: var(--font-sm);
  line-height: var(--line-height);
  color: var(--text-primary);
  word-break: break-word;
}

.review-queue__facts {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: var(--space-8) var(--space-16);
  margin: 0;
}

.review-queue__facts dt {
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.review-queue__facts dd {
  margin: 0;
  font-size: var(--font-sm);
  color: var(--text-primary);
  word-break: break-word;
}

.review-queue__pager {
  display: flex;
  justify-content: flex-end;
}

@media (max-width: 768px) {
  .review-queue__head {
    flex-direction: column;
  }

  .review-queue__filter {
    width: 100%;
  }

  .review-queue__summary {
    flex-wrap: wrap;
  }

  .review-queue__title {
    flex-basis: 100%;
    white-space: normal;
  }
}
</style>
