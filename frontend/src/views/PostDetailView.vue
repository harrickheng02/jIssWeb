<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  createForumReply,
  getForumPost,
  listForumReplies,
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

const postId = computed(() => String(route.params.id ?? ''))
const isAuthed = computed(() => Boolean(auth.token))
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
            <el-tag size="small" effect="plain">{{ post.board }}</el-tag>
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
