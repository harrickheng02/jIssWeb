<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  deleteModerationForumPost,
  deleteForumPost,
  deleteForumReply,
  deleteModerationForumReply,
  getForumPost,
  listModerationAuditByPost,
  setForumPostFeatured,
  setForumPostRepliesLocked,
  setForumPostSticky,
  type ForumPostDetail,
} from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import { confirmDeleteAuthorForumPost, confirmDeleteModerationForumPost, confirmDeleteModerationForumReply } from '@/utils/moderationForumConfirm'

const props = withDefaults(
  defineProps<{
    postId: string
    /** 父组件已持有帖子时传入，可避免重复请求；举报队列不传则由本组件自行拉取。 */
    postSnapshot?: ForumPostDetail | null
    variant?: 'default' | 'compact'
    /** 举报对象为回复时快捷删除该回复。 */
    focusReplyId?: string
    /** 举报对象为回复时在队列中仅展示删除该回复（隐藏置顶、锁帖、删帖、操作记录）。 */
    replyDeleteOnly?: boolean
    /** 举报队列上下文：删内容时传 reportId 写入审计，不向作者通知或披露原因。 */
    reportContext?: { reportId: string }
  }>(),
  { postSnapshot: undefined, variant: 'default', replyDeleteOnly: false, reportContext: undefined },
)

const emit = defineEmits<{
  /** 帖子字段在治理后发生变化（置顶、锁回、删回复影响评论数等）。 */
  postUpdated: [patch: Partial<ForumPostDetail>]
  postDeleted: []
  replyDeleted: [replyId: string]
}>()

const router = useRouter()
const auth = useAuthStore()
const canModerate = computed(() => auth.canModerate)

const resolving = ref(false)
const resolveError = ref<string | null>(null)
const localPost = ref<ForumPostDetail | null>(null)

const stickyBusy = ref(false)
const featuredBusy = ref(false)
const lockBusy = ref(false)
const deletePostBusy = ref(false)
const deleteFocusReplyBusy = ref(false)

const auditOpen = ref(false)
const auditLoading = ref(false)
const auditError = ref<string | null>(null)
const auditItems = ref<
  Array<{ id: string; actionLabel: string; operatorDisplayName: string; occurredAtUtc: string }>
>([])

const rootClasses = computed(() => ({
  'forum-governance-panel': true,
  'forum-governance-panel--compact': props.variant === 'compact',
}))

const showReplyDeleteOnly = computed(
  () => props.replyDeleteOnly === true && Boolean(props.focusReplyId?.trim()),
)

function applySnapshot(p: ForumPostDetail) {
  localPost.value = { ...p }
}

async function resolveLocalPost() {
  if (!props.postId.trim()) {
    resolveError.value = '帖子不存在'
    localPost.value = null
    resolving.value = false
    return
  }

  resolveError.value = null

  if (props.postSnapshot) {
    resolving.value = false
    applySnapshot(props.postSnapshot)
    return
  }

  resolving.value = true
  localPost.value = null
  try {
    const res = await getForumPost(props.postId)
    if (!res.success || !res.data) {
      resolveError.value = res.message ?? '主题不可读或已删除'
      localPost.value = null
      return
    }

    applySnapshot(res.data)
  } catch {
    resolveError.value = '加载失败，请稍后重试'
    localPost.value = null
  } finally {
    resolving.value = false
  }
}

watch(
  () => [props.postId, props.postSnapshot] as const,
  () => void resolveLocalPost(),
  { deep: true, immediate: true },
)

function formatUtc(iso: string) {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleString('zh-CN')
}

async function loadAudit() {
  auditLoading.value = true
  auditError.value = null
  auditItems.value = []
  try {
    const res = await listModerationAuditByPost(props.postId, 1, 20)
    if (!res.success || !res.data) {
      auditError.value = res.message ?? '加载失败'
      return
    }
    auditItems.value = res.data.items.map((x) => ({
      id: x.id,
      actionLabel: x.actionLabel?.trim() || '操作',
      operatorDisplayName: x.operatorDisplayName?.trim() || '用户',
      occurredAtUtc: x.occurredAtUtc,
    }))
  } catch (e) {
    auditError.value = e instanceof Error ? e.message : '网络异常，请稍后重试'
  } finally {
    auditLoading.value = false
  }
}

function openAudit() {
  auditOpen.value = true
  void loadAudit()
}

/** 帖子详情列表内删回复后，若抽屉已打开则刷新列表。 */
function refreshAuditIfOpen() {
  if (auditOpen.value) void loadAudit()
}

async function toggleSticky(nextValue: boolean) {
  if (!canModerate.value || !localPost.value) return
  stickyBusy.value = true
  try {
    const res = await setForumPostSticky(props.postId, nextValue)
    if (!res.success) {
      ElMessage.error(res.message ?? '操作失败')
      return
    }
    localPost.value = { ...localPost.value, isSticky: nextValue }
    emit('postUpdated', { isSticky: nextValue })
    ElMessage.success(nextValue ? '已置顶' : '已取消置顶')
    refreshAuditIfOpen()
  } finally {
    stickyBusy.value = false
  }
}

async function toggleFeatured(nextValue: boolean) {
  if (!canModerate.value || !localPost.value) return
  featuredBusy.value = true
  try {
    const res = await setForumPostFeatured(props.postId, nextValue)
    if (!res.success) {
      ElMessage.error(res.message ?? '操作失败')
      return
    }
    localPost.value = { ...localPost.value, isFeatured: nextValue }
    emit('postUpdated', { isFeatured: nextValue })
    ElMessage.success(nextValue ? '已加精' : '已取消精华')
    refreshAuditIfOpen()
  } finally {
    featuredBusy.value = false
  }
}

async function toggleReplyLock(nextLocked: boolean) {
  if (!canModerate.value || !localPost.value) return
  lockBusy.value = true
  try {
    const res = await setForumPostRepliesLocked(props.postId, nextLocked)
    if (!res.success) {
      ElMessage.error(res.message ?? '操作失败')
      return
    }
    localPost.value = { ...localPost.value, repliesLocked: nextLocked }
    emit('postUpdated', { repliesLocked: nextLocked })
    ElMessage.success(nextLocked ? '已禁止回复' : '已允许回复')
    refreshAuditIfOpen()
  } finally {
    lockBusy.value = false
  }
}

async function onDeletePost() {
  if (!canModerate.value || !localPost.value) return
  const isAuthor = Boolean(auth.sub && auth.sub === localPost.value.authorId)
  const ok = isAuthor
    ? await confirmDeleteAuthorForumPost()
    : await confirmDeleteModerationForumPost()
  if (!ok) return

  let reportPayload: { reportId: string } | undefined
  if (props.reportContext?.reportId) {
    reportPayload = { reportId: props.reportContext.reportId }
  }

  deletePostBusy.value = true
  try {
    const res = isAuthor
      ? await deleteForumPost(props.postId)
      : await deleteModerationForumPost(props.postId, reportPayload)
    if (!res.success) {
      ElMessage.error(res.message ?? '删除失败')
      return
    }
    ElMessage.success('帖子已删除')
    emit('postDeleted')
    await router.push('/')
  } finally {
    deletePostBusy.value = false
  }
}

async function onDeleteFocusedReply() {
  if (!canModerate.value || !props.focusReplyId?.trim() || !localPost.value) return
  const rid = props.focusReplyId.trim()
  const ok = await confirmDeleteModerationForumReply()
  if (!ok) return

  let reportPayload: { reportId: string; reason?: string } | undefined
  if (props.reportContext?.reportId) {
    reportPayload = { reportId: props.reportContext.reportId }
  }

  deleteFocusReplyBusy.value = true
  try {
    const res = reportPayload
      ? await deleteModerationForumReply(rid, reportPayload)
      : await deleteForumReply(props.postId, rid)
    if (!res.success) {
      ElMessage.error(res.message ?? '删除失败')
      return
    }

    ElMessage.success('回复已删除')
    const nextComments = Math.max(0, localPost.value.comments - 1)
    localPost.value = { ...localPost.value, comments: nextComments }
    emit('replyDeleted', rid)
    emit('postUpdated', { comments: nextComments })
    refreshAuditIfOpen()
  } finally {
    deleteFocusReplyBusy.value = false
  }
}

defineExpose({ refreshAuditIfOpen })
</script>

<template>
  <div v-if="canModerate" :class="rootClasses">
    <template v-if="resolving">
      <el-skeleton :rows="2" animated />
    </template>

    <div v-else-if="resolveError" class="forum-governance-panel__warn">{{ resolveError }}</div>

    <template v-else-if="localPost">
      <template v-if="showReplyDeleteOnly">
        <div class="forum-governance-panel__layout forum-governance-panel__layout--actions-only">
          <div class="forum-governance-panel__actions">
            <el-button
              type="danger"
              plain
              size="small"
              :loading="deleteFocusReplyBusy"
              @click="onDeleteFocusedReply"
            >
              删除该回复
            </el-button>
          </div>
        </div>
      </template>
      <template v-else>
        <div class="forum-governance-panel__layout forum-governance-panel__layout--actions-only">
          <div class="forum-governance-panel__actions">
            <el-button
              v-if="!localPost.isSticky"
              type="primary"
              size="small"
              :loading="stickyBusy"
              @click="toggleSticky(true)"
            >
              置顶
            </el-button>
            <el-button v-else type="primary" plain size="small" :loading="stickyBusy" @click="toggleSticky(false)">
              取消置顶
            </el-button>
            <el-button
              v-if="!localPost.isFeatured"
              type="warning"
              plain
              size="small"
              :loading="featuredBusy"
              @click="toggleFeatured(true)"
            >
              加精
            </el-button>
            <el-button v-else type="warning" plain size="small" :loading="featuredBusy" @click="toggleFeatured(false)">
              取消精华
            </el-button>
            <el-button
              v-if="!localPost.repliesLocked"
              type="primary"
              plain
              size="small"
              :loading="lockBusy"
              @click="toggleReplyLock(true)"
            >
              锁帖禁回
            </el-button>
            <el-button v-else type="primary" plain size="small" :loading="lockBusy" @click="toggleReplyLock(false)">
              解除禁回
            </el-button>
            <el-button type="danger" plain size="small" :loading="deletePostBusy" @click="onDeletePost">
              删帖
            </el-button>
            <el-button type="info" plain size="small" @click="openAudit">操作记录</el-button>
          </div>
        </div>

        <div v-if="focusReplyId?.trim()" class="forum-governance-panel__focus">
          <span class="forum-governance-panel__focus-label">被举报回复</span>
          <el-button
            type="danger"
            plain
            size="small"
            :loading="deleteFocusReplyBusy"
            @click="onDeleteFocusedReply"
          >
            删除该回复
          </el-button>
        </div>
      </template>
    </template>

    <el-drawer v-if="!showReplyDeleteOnly" v-model="auditOpen" title="操作记录" size="420px" destroy-on-close>
      <el-skeleton v-if="auditLoading" :rows="6" animated />
      <div v-else-if="auditError" class="audit-state audit-state--error">{{ auditError }}</div>
      <div v-else-if="!auditItems.length" class="audit-state">暂无记录</div>
      <div v-else class="audit-list">
        <div v-for="item in auditItems" :key="item.id" class="audit-row">
          <div class="audit-row__top">
            <el-tag size="small" effect="plain">{{ item.actionLabel }}</el-tag>
            <span class="audit-time">{{ formatUtc(item.occurredAtUtc) }}</span>
          </div>
          <div class="audit-operator">操作者：{{ item.operatorDisplayName }}</div>
        </div>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.forum-governance-panel {
  margin-top: var(--space-lg);
  padding-top: var(--space-md);
  border-top: 1px solid var(--border-color);
}

.forum-governance-panel--compact {
  margin-top: var(--space-sm);
  padding-top: var(--space-sm);
}

.forum-governance-panel--compact .forum-governance-panel__layout {
  flex-direction: column;
  align-items: stretch;
}

.forum-governance-panel__warn {
  color: var(--text-secondary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.forum-governance-panel__layout {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-md);
  flex-wrap: wrap;
}

.forum-governance-panel__layout--actions-only {
  justify-content: flex-start;
}

.forum-governance-panel__actions {
  display: flex;
  align-items: center;
  gap: var(--space-12);
  flex-wrap: wrap;
}

.forum-governance-panel__focus {
  margin-top: var(--space-sm);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-md);
  flex-wrap: wrap;
  padding: var(--space-sm) var(--space-md);
  border-radius: var(--radius-md);
  background: var(--bg-muted, rgba(0, 0, 0, 0.04));
  border: 1px solid var(--border-color-subtle, var(--border-color));
}

.forum-governance-panel__focus-label {
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.audit-state {
  color: var(--text-secondary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
  padding: var(--space-md) 0;
}

.audit-state--error {
  color: var(--text-secondary);
}

.audit-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.audit-row {
  padding: var(--space-md);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  background: var(--bg-card);
}

.audit-row__top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-12);
}

.audit-time {
  color: var(--text-secondary);
  font-size: var(--font-xs);
}

.audit-operator {
  margin-top: var(--space-sm);
  color: var(--text-secondary);
  font-size: var(--font-xs);
  line-height: var(--line-height);
}
</style>
