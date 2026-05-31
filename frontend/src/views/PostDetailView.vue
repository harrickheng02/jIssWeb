<script setup lang="ts">
import ForumRepliesLockedMark from '@/components/forum/ForumRepliesLockedMark.vue'
import ForumPostGovernancePanel from '@/components/forum/ForumPostGovernancePanel.vue'
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { formatForumMutedMessage } from '@/utils/forumMutedMessage'
import {
  createForumReply,
  deleteForumPost,
  deleteForumReply,
  getForumPost,
  listForumReplies,
  submitForumReport,
  updateForumReply,
  type ForumPostDetail,
  type ForumReply,
} from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import { useForumBoards } from '@/composables/useForumBoards'
import { useForumComposeForm } from '@/composables/useForumComposeForm'
import { confirmDeleteModerationForumReply } from '@/utils/moderationForumConfirm'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const post = ref<ForumPostDetail | null>(null)
const replies = ref<ForumReply[]>([])
const loading = ref(true)
const replyBody = ref('')
const submitting = ref(false)
const highlightedReplyId = ref<string | null>(null)
const governancePanelRef = ref<InstanceType<typeof ForumPostGovernancePanel> | null>(null)
const deleteReplyBusyId = ref<string | null>(null)

const reportOpen = ref(false)
const reportTargetType = ref<'post' | 'reply'>('post')
const reportTargetId = ref('')
const reportReason = ref('')
const reportSubmitting = ref(false)

// ── Post / Reply self-delete ──────────────────────────────────────────────────
const deletingPost = ref(false)
const deletingReplyId = ref<string | null>(null)

// ── Reply inline edit ─────────────────────────────────────────────────────────
const editingReplyId = ref<string | null>(null)
const editReplyBody = ref('')
const editReplyBusy = ref(false)

// ── Post edit compose dialog ──────────────────────────────────────────────────
const { forumBoards, loadForumBoards } = useForumBoards()

const {
  composeOpen,
  composeTitle,
  composeBody,
  composeBoardId: _composeBoardId,
  composeTags,
  composeSubmitting,
  tagSuggestions,
  tagSuggestionsLoading,
  onTagSearch,
  onComposeTagsChange,
  openComposeDialogForEdit,
  submitCompose,
} = useForumComposeForm({
  getDefaultBoardId: () => forumBoards.value[0]?.id ?? 'general',
  onEditSuccess(updated) {
    if (!post.value) return
    post.value = {
      ...post.value,
      title: updated.title,
      body: composeBody.value.trim(),
      tags: updated.tags,
      updatedAtUtc: updated.updatedAtUtc,
    }
  },
})

const postId = computed(() => String(route.params.id ?? ''))
const isAuthed = computed(() => Boolean(auth.token))
const canModerate = computed(() => auth.canModerate)
const isPostAuthor = computed(() => Boolean(auth.sub && post.value && auth.sub === post.value.authorId))

const targetReplyId = computed(() => {
  const q = route.query.reply
  const raw = Array.isArray(q) ? q[0] : q
  if (typeof raw !== 'string') return null
  let t = raw.trim()
  if (!t.length) return null
  try {
    t = decodeURIComponent(t)
  } catch {
    /* keep t */
  }
  const out = t.trim()
  return out.length ? out : null
})

const REPLY_SECTION_ANCHOR_ID = 'post-detail-reply-section'

async function scrollToReplyQueryAnchor() {
  const tid = targetReplyId.value
  if (!tid) {
    highlightedReplyId.value = null
    return
  }

  function tryScrollIntoReply(): boolean {
    const el = document.getElementById(`reply-${tid}`)
    if (!el) return false
    highlightedReplyId.value = tid
    el.scrollIntoView({ behavior: 'smooth', block: 'center' })
    window.setTimeout(() => {
      if (highlightedReplyId.value === tid) highlightedReplyId.value = null
    }, 2200)
    return true
  }

  const replyLikelyOnPage = replies.value.some((r) => r.id === tid)
  const maxAttempts = replyLikelyOnPage ? 12 : 1

  for (let i = 0; i < maxAttempts; i++) {
    if (tryScrollIntoReply()) return
    await nextTick()
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
  }

  highlightedReplyId.value = null
  const replySection = document.getElementById(REPLY_SECTION_ANCHOR_ID)
  if (replySection) {
    replySection.scrollIntoView({ behavior: 'smooth', block: 'start' })
  } else {
    document.querySelector('.post-card')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
  ElMessage.info('未找到目标回复，已为你定位到帖子内容')
}

function formatRelativeTime(iso: string) {
  const d = new Date(iso)
  const diff = Date.now() - d.getTime()
  const m = Math.floor(diff / 60000)
  if (m < 1) return '刚刚'
  if (m < 60) return `${m} 分钟前`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h} 小时前`
  const days = Math.floor(h / 24)
  if (days < 7) return `${days} 天前`
  return d.toLocaleDateString('zh-CN')
}

function shortAuthor(id: string) {
  return id.length <= 14 ? id : `${id.slice(0, 10)}…`
}

function forumAuthorLabel(displayName: string | undefined, authorId: string) {
  const n = displayName?.trim()
  if (n) return n
  return shortAuthor(authorId)
}

async function load() {
  loading.value = true
  post.value = null
  replies.value = []
  try {
    const res = await getForumPost(postId.value)
    if (!res.success || !res.data) {
      ElMessage.error(res.message ?? '加载失败')
      return
    }
    post.value = res.data
    try {
      const rr = await listForumReplies(postId.value)
      if (rr.success && rr.data) replies.value = rr.data
    } catch {
      replies.value = []
      ElMessage.warning('回复列表加载失败')
    }
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '加载失败')
    post.value = null
  } finally {
    loading.value = false
  }

  if (!post.value) return

  await nextTick()
  await scrollToReplyQueryAnchor()
}

async function submitReply() {
  if (!isAuthed.value) {
    void router.push('/auth')
    return
  }
  const text = replyBody.value.trim()
  if (!text) {
    ElMessage.warning('请输入回复内容')
    return
  }
  submitting.value = true
  try {
    const res = await createForumReply(postId.value, text)
    if (!res.success) {
      ElMessage.error(res.code === 'FORUM_MUTED' ? formatForumMutedMessage(res) : (res.message ?? '回复失败'))
      return
    }
    replyBody.value = ''
    try {
      const rr = await listForumReplies(postId.value)
      if (rr.success && rr.data) replies.value = rr.data
    } catch {
      replies.value = []
    }
    if (post.value) post.value = { ...post.value, comments: post.value.comments + 1 }
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '回复失败')
  } finally {
    submitting.value = false
  }
}

function openReport(kind: 'post' | 'reply', targetId: string) {
  if (!isAuthed.value) {
    void router.push('/auth')
    return
  }
  reportTargetType.value = kind
  reportTargetId.value = targetId
  reportReason.value = ''
  reportOpen.value = true
}

async function submitReport() {
  if (!post.value) return
  const tid = reportTargetId.value.trim()
  if (!tid) {
    ElMessage.warning('举报目标无效')
    return
  }
  reportSubmitting.value = true
  try {
    const res = await submitForumReport({
      targetType: reportTargetType.value,
      targetId: tid,
      reason: reportReason.value.trim() || undefined,
    })
    if (!res.success) {
      ElMessage.error(res.message ?? '提交失败')
      return
    }
    ElMessage.success('已提交举报，感谢你的反馈')
    reportOpen.value = false
  } finally {
    reportSubmitting.value = false
  }
}

function onGovernancePostUpdated(patch: Partial<ForumPostDetail>) {
  if (!post.value) return
  post.value = { ...post.value, ...patch }
}

async function confirmDeleteReply(reply: ForumReply) {
  if (!canModerate.value) return
  const ok = await confirmDeleteModerationForumReply()
  if (!ok) return

  deleteReplyBusyId.value = reply.id
  try {
    // 走 /api/forum 直连 Model API；后端对版主会委派到软删逻辑
    const res = await deleteForumReply(postId.value, reply.id)
    if (!res.success) {
      ElMessage.error(res.message ?? '删除失败')
      return
    }
    replies.value = replies.value.filter((x) => x.id !== reply.id)
    if (post.value) post.value = { ...post.value, comments: Math.max(0, post.value.comments - 1) }
    ElMessage.success('回复已删除')
    governancePanelRef.value?.refreshAuditIfOpen()
  } finally {
    deleteReplyBusyId.value = null
  }
}

function resetReportDialog() {
  reportReason.value = ''
  reportTargetId.value = ''
}

async function handleDeletePost() {
  const ok = await ElMessageBox.confirm('确定要删除这篇帖子吗？删除后其他人将无法查看。', '删除帖子', {
    confirmButtonText: '删除',
    cancelButtonText: '取消',
    type: 'warning',
  }).catch(() => false)
  if (!ok) return
  deletingPost.value = true
  try {
    const res = await deleteForumPost(postId.value)
    if (!res.success) { ElMessage.error(res.message ?? '删除失败'); return }
    ElMessage.success('帖子已删除')
    await router.push('/')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '删除失败')
  } finally {
    deletingPost.value = false
  }
}

async function handleDeleteReply(reply: ForumReply) {
  const ok = await ElMessageBox.confirm('确定要删除这条回复吗？', '删除回复', {
    confirmButtonText: '删除',
    cancelButtonText: '取消',
    type: 'warning',
  }).catch(() => false)
  if (!ok) return
  deletingReplyId.value = reply.id
  try {
    const res = await deleteForumReply(postId.value, reply.id)
    if (!res.success) { ElMessage.error(res.message ?? '删除失败'); return }
    replies.value = replies.value.filter((r) => r.id !== reply.id)
    if (post.value) post.value = { ...post.value, comments: Math.max(0, post.value.comments - 1) }
    ElMessage.success('回复已删除')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '删除失败')
  } finally {
    deletingReplyId.value = null
  }
}

function handleEditPost() {
  if (!post.value) return
  openComposeDialogForEdit({
    id: post.value.id,
    title: post.value.title,
    body: post.value.body,
    tags: post.value.tags,
  })
}

function startEditReply(reply: ForumReply) {
  editingReplyId.value = reply.id
  editReplyBody.value = reply.body
}

function cancelEditReply() {
  editingReplyId.value = null
  editReplyBody.value = ''
}

async function saveEditReply(reply: ForumReply) {
  const newBody = editReplyBody.value.trim()
  if (!newBody) {
    ElMessage.warning('回复内容不能为空')
    return
  }
  editReplyBusy.value = true
  try {
    const res = await updateForumReply(postId.value, reply.id, { body: newBody })
    if (!res.success || !res.data) {
      ElMessage.error(res.message ?? '编辑失败')
      return
    }
    const idx = replies.value.findIndex((r) => r.id === reply.id)
    if (idx !== -1) replies.value[idx] = res.data
    editingReplyId.value = null
    editReplyBody.value = ''
    ElMessage.success('回复已更新')
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '编辑失败')
  } finally {
    editReplyBusy.value = false
  }
}

onMounted(() => {
  void loadForumBoards()
  void load()
})
watch(
  () => route.params.id,
  () => {
    void load()
  },
)
watch(
  () => route.query.reply,
  async () => {
    if (loading.value) return
    if (!post.value || post.value.id !== postId.value) return
    await nextTick()
    if (!targetReplyId.value) {
      highlightedReplyId.value = null
      return
    }
    await scrollToReplyQueryAnchor()
  },
)
</script>

<template>
  <div class="post-detail">
    <div class="post-detail__inner">
      <el-button class="back" text type="primary" @click="router.push('/')">← 返回首页</el-button>

      <el-skeleton v-if="loading" :rows="8" animated />

      <template v-else-if="post">
        <el-card shadow="never" class="post-card">
          <div class="post-topline">
            <div class="post-topline-left">
              <el-tag size="small" effect="plain">{{ post.board }}</el-tag>
              <el-tag v-if="post.isSticky" size="small" type="warning" effect="plain">置顶</el-tag>
              <el-tag v-if="post.isFeatured" size="small" type="success" effect="plain">精华</el-tag>
              <ForumRepliesLockedMark v-if="post.repliesLocked" :size="18" />
            </div>
            <div class="post-topline-right">
              <span class="post-time">{{ formatRelativeTime(post.publishedAtUtc) }}</span>
              <template v-if="isPostAuthor && post.state !== 'deleted'">
                <el-button type="primary" link size="small" @click="handleEditPost">编辑</el-button>
                <el-button
                  type="danger"
                  link
                  size="small"
                  :loading="deletingPost"
                  @click="handleDeletePost"
                >
                  删除
                </el-button>
              </template>
            </div>
          </div>
          <h1 class="post-title">{{ post.title }}</h1>
          <div class="post-meta">
            <span class="author">{{ forumAuthorLabel(post.authorDisplayName, post.authorId) }}</span>
            <div class="post-meta-right">
              <span v-if="post.updatedAtUtc" class="post-edited">已编辑 {{ formatRelativeTime(post.updatedAtUtc) }}</span>
              <div class="tag-list">
                <el-tag v-for="tag in post.tags" :key="tag" size="small">{{ tag }}</el-tag>
              </div>
            </div>
          </div>
          <div v-if="post.state === 'deleted' && post.deletedBySub === auth.sub" class="post-deleted-banner">
            <span>该帖子已被你删除，其他人无法查看。</span>
          </div>
          <div class="post-body">{{ post.body }}</div>
          <div class="post-stats">
            <span class="stat-text">赞 {{ post.likes }}</span>
            <span class="stat-text">评 {{ post.comments }}</span>
            <span class="stat-text">看 {{ post.views }}</span>
          </div>

          <div v-if="isAuthed" class="report-bar">
            <el-button type="primary" link @click="openReport('post', post.id)">举报帖子</el-button>
          </div>

          <ForumPostGovernancePanel
            ref="governancePanelRef"
            :post-id="post.id"
            :post-snapshot="post"
            @post-updated="onGovernancePostUpdated"
          />
        </el-card>

        <div :id="REPLY_SECTION_ANCHOR_ID" class="post-detail__reply-anchor">
        <el-card shadow="never" class="reply-card">
          <template #header>回复</template>
          <div
            v-for="r in replies"
            :id="`reply-${r.id}`"
            :key="r.id"
            class="reply-row"
            :class="{ 'reply-row--highlight': highlightedReplyId === r.id }"
          >
            <div class="reply-meta">
              <span class="reply-author">{{ forumAuthorLabel(r.authorDisplayName, r.authorId) }}</span>
              <div class="reply-meta-right">
                <span v-if="r.updatedAtUtc" class="reply-edited">已编辑</span>
                <span class="reply-time">{{ formatRelativeTime(r.createdAtUtc) }}</span>
              </div>
            </div>

            <template v-if="editingReplyId === r.id">
              <el-input
                v-model="editReplyBody"
                type="textarea"
                :rows="4"
                placeholder="编辑回复内容…"
                class="reply-edit-input"
              />
              <div class="reply-edit-actions">
                <el-button type="primary" size="small" :loading="editReplyBusy" @click="saveEditReply(r)">保存</el-button>
                <el-button size="small" @click="cancelEditReply">取消</el-button>
              </div>
            </template>
            <p v-else class="reply-body">{{ r.body }}</p>

            <div class="reply-actions" role="group" aria-label="本条回复的操作">
              <div v-if="auth.sub && auth.sub === r.authorId && editingReplyId !== r.id" class="reply-self-actions">
                <el-button type="primary" link size="small" @click="startEditReply(r)">编辑</el-button>
                <el-button type="danger" link size="small" :loading="deletingReplyId === r.id" @click="handleDeleteReply(r)">删除</el-button>
              </div>
              <div v-if="canModerate && auth.sub !== r.authorId" class="reply-mod">
                <el-button
                  type="danger"
                  link
                  size="small"
                  :loading="deleteReplyBusyId === r.id"
                  @click="confirmDeleteReply(r)"
                >
                  删除
                </el-button>
              </div>
              <div v-if="isAuthed" class="reply-report">
                <el-button type="primary" link size="small" @click="openReport('reply', r.id)">举报</el-button>
              </div>
            </div>
          </div>
          <div v-if="!replies.length" class="empty">暂无回复</div>

          <div v-if="post.repliesLocked" class="reply-locked-hint">本帖已由版主锁定，暂不可发表新回复。</div>

          <template v-else>
            <div v-if="isAuthed" class="reply-form">
              <el-input v-model="replyBody" type="textarea" :rows="4" placeholder="写回复…" />
              <el-button type="primary" class="reply-submit" :loading="submitting" @click="submitReply">
                发表回复
              </el-button>
            </div>
            <el-button v-else type="primary" plain @click="router.push('/auth')">登录后回复</el-button>
          </template>
        </el-card>
        </div>
      </template>
    </div>

    <!-- 帖子编辑对话框 -->
    <el-dialog v-model="composeOpen" title="编辑帖子" width="520px" destroy-on-close>
      <el-form label-position="top">
        <el-form-item label="标题">
          <el-input v-model="composeTitle" maxlength="200" show-word-limit />
        </el-form-item>
        <el-form-item label="正文">
          <el-input v-model="composeBody" type="textarea" :rows="8" maxlength="20000" show-word-limit />
        </el-form-item>
        <el-form-item label="板块">
          <div class="compose-board-readonly">{{ post?.board ?? '—' }}</div>
          <p class="compose-field-hint">已发布后不可更换板块</p>
        </el-form-item>
        <el-form-item label="标签">
          <el-select
            v-model="composeTags"
            class="compose-board-select compose-tags-select"
            multiple
            remote
            filterable
            allow-create
            default-first-option
            :remote-method="onTagSearch"
            :loading="tagSuggestionsLoading"
            :reserve-keyword="false"
            placeholder="可选，最多 10 个"
            @change="onComposeTagsChange"
          >
            <el-option v-for="t in tagSuggestions" :key="t" :label="t" :value="t" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="composeOpen = false">取消</el-button>
        <el-button type="primary" :loading="composeSubmitting" @click="submitCompose">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="reportOpen"
      title="举报"
      width="480px"
      destroy-on-close
      @closed="resetReportDialog"
    >
      <p class="report-dialog__hint">
        {{ reportTargetType === 'post' ? '你将举报该主题帖。' : '你将举报该条回复。' }}
      </p>
      <el-input
        v-model="reportReason"
        type="textarea"
        :rows="4"
        maxlength="500"
        show-word-limit
        placeholder="可选：补充说明（滥用、骚扰、违法内容等）"
      />
      <template #footer>
        <el-button @click="reportOpen = false">取消</el-button>
        <el-button type="primary" :loading="reportSubmitting" @click="submitReport">提交</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.post-detail {
  min-height: 100%;
  background: var(--bg-main);
  color: var(--text-primary);
}

.post-detail__inner {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.post-detail__reply-anchor {
  scroll-margin-top: 4.5rem;
}

.back {
  align-self: flex-start;
}

.post-card,
.reply-card {
  border-radius: var(--radius-lg);
}

.post-topline {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--space-12);
}

.post-topline-left {
  display: inline-flex;
  align-items: center;
  gap: var(--space-sm);
  min-width: 0;
}

.post-topline-right {
  display: inline-flex;
  align-items: center;
  gap: var(--space-sm);
}

.post-time {
  color: var(--text-secondary);
  font-size: var(--font-xs);
}

.post-title {
  margin: var(--space-md) 0;
  font-size: var(--font-xxl);
  font-weight: 700;
  line-height: 1.4;
}

.post-meta {
  display: flex;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: var(--space-12);
  align-items: center;
}

.post-meta-right {
  display: flex;
  align-items: center;
  gap: var(--space-sm);
  flex-wrap: wrap;
}

.author {
  color: var(--color-primary);
  font-size: var(--font-sm);
}

.post-edited {
  color: var(--text-secondary);
  font-size: var(--font-xs);
}

.tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-sm);
}

.post-body {
  margin-top: var(--space-lg);
  font-size: var(--font-md);
  line-height: var(--line-height);
  white-space: pre-wrap;
}

.post-stats {
  margin-top: var(--space-lg);
  display: flex;
  gap: var(--space-md);
}

.stat-text {
  color: var(--text-secondary);
  font-size: var(--font-sm);
}

.post-deleted-banner {
  display: flex;
  align-items: center;
  gap: var(--space-md);
  margin-top: var(--space-md);
  padding: var(--space-md);
  border-radius: var(--radius-lg);
  background: color-mix(in srgb, var(--color-danger, #f56c6c) 10%, transparent);
  color: var(--color-danger, #f56c6c);
  font-size: var(--font-sm);
}

.report-bar {
  margin-top: var(--space-md);
}

.report-dialog__hint {
  margin: 0 0 var(--space-md);
  font-size: var(--font-sm);
  color: var(--text-secondary);
  line-height: var(--line-height);
}

.reply-row {
  scroll-margin-top: 4.5rem;
  scroll-margin-bottom: var(--space-md);
  margin-bottom: var(--space-md);
  padding: var(--space-md);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-lg);
  background: var(--bg-card);
}

.reply-row:last-of-type {
  margin-bottom: 0;
}

.reply-meta {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: var(--space-md);
  min-width: 0;
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.reply-meta-right {
  display: inline-flex;
  align-items: baseline;
  gap: var(--space-sm);
}

.reply-author {
  color: var(--color-primary);
  font-weight: 500;
  font-size: var(--font-sm);
}

.reply-edited {
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.reply-row--highlight {
  border-color: color-mix(in srgb, var(--color-primary) 35%, var(--border-color));
  background: color-mix(in srgb, var(--color-primary) 8%, var(--bg-card));
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--color-primary) 20%, transparent);
}

.reply-body {
  margin: var(--space-sm) 0 0;
  font-size: var(--font-sm);
  line-height: var(--line-height);
  white-space: pre-wrap;
}

.reply-edit-input {
  margin-top: var(--space-sm);
}

.reply-edit-actions {
  margin-top: var(--space-sm);
  display: flex;
  gap: var(--space-sm);
}

.reply-actions {
  margin-top: var(--space-md);
  padding-top: var(--space-sm);
  border-top: 1px solid var(--border-color-subtle, var(--border-color));
  display: flex;
  align-items: center;
  gap: var(--space-md);
  flex-wrap: wrap;
}

.reply-report {
  margin-top: 0;
}

.reply-locked-hint {
  margin-top: var(--space-md);
  padding: var(--space-md);
  border-radius: var(--radius-lg);
  background: color-mix(in srgb, var(--color-warning) 12%, transparent);
  color: var(--text-secondary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.mod-panel__actions .el-button {
  margin-inline-start: 0;
}

.empty {
  color: var(--text-secondary);
  font-size: var(--font-sm);
}

.reply-form {
  margin-top: var(--space-md);
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.reply-submit {
  align-self: flex-start;
}

.compose-board-readonly {
  font-size: var(--font-size-base);
  color: var(--text-primary);
}

.compose-field-hint {
  margin: var(--space-xs) 0 0;
  font-size: var(--font-size-sm);
  color: var(--text-secondary);
}

.compose-board-select {
  width: 100%;
}

.compose-tags-select {
  width: 100%;
}
</style>
