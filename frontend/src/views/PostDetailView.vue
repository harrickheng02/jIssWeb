<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  createForumReply,
  getForumPost,
  listModerationAuditByPost,
  listForumReplies,
  setForumPostSticky,
  type ForumPostDetail,
  type ForumReply,
} from '@/api/clients'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const post = ref<ForumPostDetail | null>(null)
const replies = ref<ForumReply[]>([])
const loading = ref(true)
const replyBody = ref('')
const submitting = ref(false)
const highlightedReplyId = ref<string | null>(null)
const stickyBusy = ref(false)

const auditOpen = ref(false)
const auditLoading = ref(false)
const auditError = ref<string | null>(null)
const auditItems = ref<
  Array<{ id: string; actionLabel: string; operatorDisplayName: string; occurredAtUtc: string }>
>([])

const postId = computed(() => String(route.params.id ?? ''))
const isAuthed = computed(() => Boolean(auth.token))
const canModerate = computed(() => auth.canModerate)
const targetReplyId = computed(() => {
  const q = route.query.reply
  return typeof q === 'string' && q.trim() ? q.trim() : null
})

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

    await nextTick()
    if (targetReplyId.value) {
      const el = document.getElementById(`reply-${targetReplyId.value}`)
      if (el) {
        highlightedReplyId.value = targetReplyId.value
        el.scrollIntoView({ behavior: 'smooth', block: 'center' })
        window.setTimeout(() => {
          if (highlightedReplyId.value === targetReplyId.value) highlightedReplyId.value = null
        }, 2200)
      } else {
        ElMessage.info('未找到目标回复，已为你定位到帖子内容')
      }
    }
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : '加载失败')
    post.value = null
  } finally {
    loading.value = false
  }
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

async function toggleSticky(nextValue: boolean) {
  if (!canModerate.value || !post.value) return
  stickyBusy.value = true
  try {
    const res = await setForumPostSticky(postId.value, nextValue)
    if (!res.success) {
      ElMessage.error(res.message ?? '操作失败')
      return
    }
    post.value = { ...post.value, isSticky: nextValue }
    ElMessage.success(nextValue ? '已置顶' : '已取消置顶')
  } finally {
    stickyBusy.value = false
  }
}

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
    const res = await listModerationAuditByPost(postId.value, 1, 20)
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

onMounted(() => {
  void load()
})
watch(
  () => route.params.id,
  () => {
    void load()
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

          <div v-if="canModerate" class="mod-panel">
            <div class="mod-panel__left">
              <div class="mod-title">治理</div>
              <div class="mod-desc">置顶与审计仅对版主/管理员可见。</div>
            </div>
            <div class="mod-panel__actions">
              <el-button
                v-if="!post.isSticky"
                type="primary"
                :loading="stickyBusy"
                @click="toggleSticky(true)"
              >
                置顶
              </el-button>
              <el-button v-else type="primary" plain :loading="stickyBusy" @click="toggleSticky(false)">
                取消置顶
              </el-button>
              <el-button type="info" plain @click="(auditOpen = true), loadAudit()">操作记录</el-button>
            </div>
          </div>
        </el-card>

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
              <span>{{ forumAuthorLabel(r.authorDisplayName, r.authorId) }}</span>
              <span class="reply-time">{{ formatPublishedUtc(r.createdAtUtc) }}</span>
            </div>
            <p class="reply-body">{{ r.body }}</p>
          </div>
          <div v-if="!replies.length" class="empty">暂无回复</div>

          <div v-if="isAuthed" class="reply-form">
            <el-input v-model="replyBody" type="textarea" :rows="4" placeholder="写回复…" />
            <el-button type="primary" class="reply-submit" :loading="submitting" @click="submitReply">发表回复</el-button>
          </div>
          <el-button v-else type="primary" plain @click="router.push('/auth')">登录后回复</el-button>
        </el-card>
      </template>
    </div>

    <el-drawer v-model="auditOpen" title="操作记录" size="420px" destroy-on-close>
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

.mod-panel {
  margin-top: var(--space-lg);
  padding-top: var(--space-md);
  border-top: 1px solid var(--border-color);
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-md);
  flex-wrap: wrap;
}

.mod-panel__left {
  display: flex;
  flex-direction: column;
  gap: var(--space-xs);
}

.mod-title {
  font-size: var(--font-sm);
  font-weight: 600;
  color: var(--text-primary);
  line-height: 1.4;
}

.mod-desc {
  font-size: var(--font-xs);
  color: var(--text-secondary);
  line-height: var(--line-height);
}

.mod-panel__actions {
  display: flex;
  align-items: center;
  gap: var(--space-12);
  flex-wrap: wrap;
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

.reply-row {
  padding: var(--space-md) 0;
  border-bottom: 1px solid var(--border-color);
}

.reply-row--highlight {
  background: color-mix(in srgb, var(--color-primary) 8%, var(--bg-card));
  border-radius: var(--radius-md);
  padding-left: var(--space-md);
  padding-right: var(--space-md);
}

.reply-row:last-of-type {
  border-bottom: none;
}

.reply-meta {
  display: flex;
  justify-content: space-between;
  font-size: var(--font-xs);
  color: var(--text-secondary);
}

.reply-body {
  margin: var(--space-sm) 0 0;
  font-size: var(--font-sm);
  line-height: var(--line-height);
  white-space: pre-wrap;
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
