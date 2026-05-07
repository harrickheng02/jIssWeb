<script setup lang="ts">
import ForumRepliesLockedMark from '@/components/forum/ForumRepliesLockedMark.vue'
import ForumPostGovernancePanel from '@/components/forum/ForumPostGovernancePanel.vue'
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  createForumReply,
  deleteModerationForumReply,
  getForumPost,
  listForumReplies,
  submitForumReport,
  type ForumPostDetail,
  type ForumReply,
} from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
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

const postId = computed(() => String(route.params.id ?? ''))
const isAuthed = computed(() => Boolean(auth.token))
const canModerate = computed(() => auth.canModerate)
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

function formatPublishedUtc(iso: string) {
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
      ElMessage.error(res.message ?? '回复失败')
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
    const res = await deleteModerationForumReply(reply.id)
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

onMounted(() => {
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
              <ForumRepliesLockedMark v-if="post.repliesLocked" :size="18" />
            </div>
            <span class="post-time">{{ formatPublishedUtc(post.publishedAtUtc) }}</span>
          </div>
          <h1 class="post-title">{{ post.title }}</h1>
          <div class="post-meta">
            <span class="author">{{ forumAuthorLabel(post.authorDisplayName, post.authorId) }}</span>
            <div class="tag-list">
              <el-tag v-for="tag in post.tags" :key="tag" size="small">{{ tag }}</el-tag>
            </div>
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
              <span class="reply-time">{{ formatPublishedUtc(r.createdAtUtc) }}</span>
            </div>
            <p class="reply-body">{{ r.body }}</p>
            <div class="reply-actions" role="group" aria-label="本条回复的操作">
              <div v-if="canModerate" class="reply-mod">
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

.author {
  color: var(--color-primary);
  font-size: var(--font-sm);
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

.reply-author {
  color: var(--color-primary);
  font-weight: 500;
  font-size: var(--font-sm);
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
</style>
