<script setup lang="ts">
import ForumPostGovernancePanel from '@/components/forum/ForumPostGovernancePanel.vue'
import { useModerationReportsQueue } from '@/composables/useModerationReportsQueue'
import { CaretBottom, CaretRight } from '@element-plus/icons-vue'

const {
  router,
  canModerate,
  loading,
  items,
  totalCount,
  page,
  pageSize,
  statusFilter,
  busyId,
  expandedReportId,
  previewByReportId,
  statusOptions,
  formatUtc,
  targetDetailHref,
  detailLinkTitle,
  toggleReportRow,
  governancePostSnapshotList,
  onGovernanceDeletedFromQueue,
  onFilterChange,
  applyStatus,
  statusRowLabel,
  reportPreviewIdleList,
} = useModerationReportsQueue()
</script>

<template>
  <div class="mod-reports">
    <div class="mod-reports__inner">
      <el-button class="back" text type="primary" @click="router.push({ name: 'moderation' })">
        ← 返回治理说明
      </el-button>

      <el-card v-if="!canModerate" shadow="never" class="mod-reports__card">
        <p class="mod-reports__hint">仅版主或管理员可查看举报队列。</p>
      </el-card>

      <template v-else>
        <el-card shadow="never" class="mod-reports__card">
          <template #header>
            <div class="mod-reports__header">
              <div>
                <span class="mod-reports__title">举报队列</span>
                <p class="mod-reports__subtitle">
                  <strong>举报工单</strong>三类状态在此维护。点击<strong>整条条目</strong>展开或收起，同区展示上下文摘要与内容治理；链接「<strong>查看详情</strong>」在新标签打开帖子。结案超保留期从库移除时审计可查。
                </p>
              </div>
              <el-select
                v-model="statusFilter"
                class="mod-reports__filter"
                placeholder="状态"
                @change="onFilterChange"
              >
                <el-option
                  v-for="opt in statusOptions"
                  :key="opt.value === '' ? 'all' : opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
            </div>
          </template>

            <el-skeleton v-if="loading" :rows="6" animated />
          <div v-else-if="!items.length" class="mod-reports__empty">暂无举报</div>
          <div v-else class="mod-reports__list">
            <div
              v-for="row in items"
              :key="row.id"
              class="mod-reports__row"
              tabindex="0"
              :aria-expanded="expandedReportId === row.id"
              aria-label="举报条目，点击展开或收起"
              @click="toggleReportRow(row)"
              @keydown.enter.prevent="toggleReportRow(row)"
              @keydown.space.prevent="toggleReportRow(row)"
            >
              <div class="mod-reports__row-summary">
                <el-icon class="mod-reports__caret" aria-hidden="true">
                  <CaretBottom v-if="expandedReportId === row.id" />
                  <CaretRight v-else />
                </el-icon>
                <div class="mod-reports__row-top">
                  <el-tag size="small" effect="plain">{{ row.boardTitle }}</el-tag>
                  <el-tag
                    v-if="row.status === 'pending'"
                    size="small"
                    type="warning"
                    effect="plain"
                  >
                    {{ statusRowLabel(row) }}
                  </el-tag>
                  <el-tag
                    v-else-if="row.status === 'rejected'"
                    size="small"
                    type="info"
                    effect="plain"
                  >
                    {{ statusRowLabel(row) }}
                  </el-tag>
                  <el-tag
                    v-else-if="row.status === 'resolved'"
                    size="small"
                    type="success"
                    effect="plain"
                  >
                    {{ statusRowLabel(row) }}
                  </el-tag>
                  <el-tag v-else size="small" type="info" effect="plain">{{ row.status }}</el-tag>
                  <span class="mod-reports__time">{{ formatUtc(row.createdAtUtc) }}</span>
                </div>
              </div>
              <div class="mod-reports__meta">
                <span>举报人：{{ row.reporterDisplayName }}</span>
                <span class="mod-reports__meta-sep">·</span>
                <a
                  class="mod-reports__target-link"
                  :href="targetDetailHref(row)"
                  target="_blank"
                  rel="noopener noreferrer"
                  :title="detailLinkTitle(row)"
                  @click.stop
                >
                  查看详情
                </a>
              </div>
              <div
                v-if="expandedReportId === row.id"
                class="mod-reports__expanded"
                role="region"
                aria-label="上下文摘要与治理"
                @click.stop
              >
                <div class="mod-reports__preview" aria-label="上下文摘要">
                  <el-skeleton
                    v-if="!previewByReportId[row.id] || previewByReportId[row.id].loading === true"
                    :rows="3"
                    animated
                  />
                  <template v-for="idle in reportPreviewIdleList(row.id)" :key="row.id">
                    <div class="mod-reports__preview-inner">
                      <template v-if="idle.unavailable">
                        <p class="mod-reports__preview-miss">{{ idle.unavailable }}</p>
                      </template>
                      <template v-else>
                        <p class="mod-reports__preview-title">{{ idle.title }}</p>
                        <p class="mod-reports__preview-label">主帖节选</p>
                        <p class="mod-reports__preview-snippet">{{ idle.postSnippet }}</p>
                        <template v-if="row.targetType === 'reply' && idle.replySnippet">
                          <p class="mod-reports__preview-label">被举报回复节选</p>
                          <p class="mod-reports__preview-snippet mod-reports__preview-snippet--reply">
                            {{ idle.replySnippet }}
                          </p>
                        </template>
                      </template>
                    </div>
                  </template>
                </div>
                <div class="mod-reports__governance" aria-label="版主操作">
                  <ForumPostGovernancePanel
                    v-for="snap in governancePostSnapshotList(row)"
                    :key="`${row.id}-gov`"
                    :post-id="row.postId"
                    variant="compact"
                    :post-snapshot="snap"
                    :reply-delete-only="row.targetType === 'reply'"
                    :focus-reply-id="row.targetType === 'reply' ? row.targetId : undefined"
                    @post-deleted="onGovernanceDeletedFromQueue"
                    @reply-deleted="onGovernanceDeletedFromQueue"
                  />
                </div>
              </div>
              <div v-if="row.reason?.trim()" class="mod-reports__reason" @click.stop>
                说明：{{ row.reason }}
              </div>
              <div class="mod-reports__actions" @click.stop>
                <span class="mod-reports__actions-label">举报工单状态</span>
                <el-button-group class="mod-reports__status-btns">
                  <el-button
                    size="small"
                    :type="row.status === 'pending' ? 'primary' : 'default'"
                    plain
                    :disabled="busyId === row.id || row.status === 'pending'"
                    @click="applyStatus(row, 'pending')"
                  >
                    待处理
                  </el-button>
                  <el-button
                    size="small"
                    :type="row.status === 'rejected' ? 'primary' : 'default'"
                    plain
                    :disabled="busyId === row.id || row.status === 'rejected'"
                    @click="applyStatus(row, 'rejected')"
                  >
                    已驳回
                  </el-button>
                  <el-button
                    size="small"
                    :type="row.status === 'resolved' ? 'primary' : 'default'"
                    plain
                    :disabled="busyId === row.id || row.status === 'resolved'"
                    @click="applyStatus(row, 'resolved')"
                  >
                    已处置
                  </el-button>
                </el-button-group>
              </div>
            </div>
          </div>

          <div v-if="totalCount > pageSize" class="mod-reports__pager">
            <el-pagination
              v-model:current-page="page"
              :page-size="pageSize"
              layout="prev, pager, next"
              :total="totalCount"
            />
          </div>
        </el-card>
      </template>
    </div>
  </div>
</template>

<style scoped>
.mod-reports {
  min-height: 100%;
  background: var(--bg-main);
  color: var(--text-primary);
}

.mod-reports__inner {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.back {
  align-self: flex-start;
}

.mod-reports__card {
  border-radius: var(--radius-lg);
}

.mod-reports__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-md);
  flex-wrap: wrap;
}

.mod-reports__subtitle {
  margin: var(--space-xs) 0 0;
  font-size: var(--font-xs);
  color: var(--text-secondary);
  line-height: var(--line-height);
  max-width: 36rem;
}

.mod-reports__title {
  font-weight: 700;
  font-size: var(--font-md);
}

.mod-reports__filter {
  width: 160px;
}

.mod-reports__hint {
  margin: 0;
  color: var(--text-secondary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.mod-reports__empty {
  color: var(--text-secondary);
  font-size: var(--font-sm);
  padding: var(--space-md) 0;
}

.mod-reports__list {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.mod-reports__row {
  padding: var(--space-md);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  background: var(--bg-card);
  cursor: pointer;
  outline: none;
}

.mod-reports__row:hover {
  border-color: var(--border-color-subtle, var(--border-color));
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
}

.mod-reports__row:focus-visible {
  outline: 2px solid var(--color-primary);
  outline-offset: 2px;
}

.mod-reports__row-summary {
  display: flex;
  align-items: flex-start;
  gap: var(--space-xs);
  width: 100%;
}

.mod-reports__caret {
  flex-shrink: 0;
  margin-top: 2px;
  font-size: var(--font-md);
  color: var(--text-secondary);
}

.mod-reports__row-top {
  display: flex;
  align-items: center;
  gap: var(--space-sm);
  flex-wrap: wrap;
  flex: 1;
  min-width: 0;
}

.mod-reports__time {
  margin-left: auto;
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.mod-reports__meta {
  margin-top: var(--space-sm);
  font-size: var(--font-sm);
  color: var(--text-secondary);
  line-height: var(--line-height);
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--space-xs);
}

.mod-reports__meta-sep {
  color: var(--text-secondary);
  user-select: none;
}

.mod-reports__target-link {
  color: var(--color-primary);
  font-weight: 500;
  text-decoration: none;
}

.mod-reports__target-link:hover {
  text-decoration: underline;
}

.mod-reports__expanded {
  margin-top: var(--space-md);
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.mod-reports__preview {
  padding: var(--space-sm) var(--space-md);
  border-radius: var(--radius-md);
  background: var(--bg-muted, rgba(0, 0, 0, 0.04));
  border: 1px solid var(--border-color-subtle, var(--border-color));
}

.mod-reports__preview-inner {
  font-size: var(--font-sm);
  line-height: var(--line-height);
  color: var(--text-primary);
}

.mod-reports__preview-title {
  margin: 0 0 var(--space-xs);
  font-weight: 600;
  font-size: var(--font-sm);
  color: var(--text-primary);
}

.mod-reports__preview-label {
  margin: var(--space-xs) 0 var(--space-2xs, 4px);
  font-size: var(--font-xs);
  color: var(--text-secondary);
  text-transform: none;
}

.mod-reports__preview-snippet {
  margin: 0 0 var(--space-xs);
  white-space: pre-wrap;
  word-break: break-word;
  color: var(--text-primary);
}

.mod-reports__preview-snippet--reply {
  border-left: 3px solid var(--color-primary);
  padding-left: var(--space-sm);
}

.mod-reports__preview-miss {
  margin: 0;
  font-size: var(--font-sm);
  color: var(--text-secondary);
}

.mod-reports__governance {
  padding: var(--space-sm) var(--space-md);
  border-radius: var(--radius-md);
  border: 1px solid var(--border-color-subtle, var(--border-color));
  background: var(--bg-card);
}

.mod-reports__governance :deep(.forum-governance-panel) {
  margin-top: 0;
  padding-top: var(--space-sm);
  border-top: none;
}

.mod-reports__reason {
  margin-top: var(--space-sm);
  font-size: var(--font-sm);
  color: var(--text-primary);
  line-height: var(--line-height);
}

.mod-reports__actions {
  margin-top: var(--space-md);
  display: flex;
  align-items: center;
  gap: var(--space-md);
  flex-wrap: wrap;
}

.mod-reports__actions-label {
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.mod-reports__status-btns {
  flex-wrap: wrap;
}

.mod-reports__pager {
  margin-top: var(--space-md);
  display: flex;
  justify-content: center;
}
</style>
