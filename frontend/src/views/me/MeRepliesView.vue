<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { listMyForumReplies, type ForumReply } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import { forumAuthorLabel, formatPublishedUtc } from '@/utils/forumPostDisplay'

const router = useRouter()
const auth = useAuthStore()
const loading = ref(true)
const error = ref('')
const items = ref<ForumReply[]>([])
const page = ref(1)
const pageSize = 20
const totalCount = ref(0)

const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

function previewBody(body: string) {
  const t = body.trim().replace(/\s+/g, ' ')
  return t.length > 160 ? `${t.slice(0, 160)}…` : t
}

async function load() {
  if (!auth.token) {
    items.value = []
    totalCount.value = 0
    return
  }
  loading.value = true
  error.value = ''
  try {
    const res = await listMyForumReplies(page.value, pageSize)
    if (res.success && res.data) {
      items.value = res.data.items
      totalCount.value = res.data.totalCount
    } else {
      error.value = res.message ?? '加载失败'
      items.value = []
      totalCount.value = 0
    }
  } catch {
    error.value = '加载失败'
    items.value = []
    totalCount.value = 0
  } finally {
    loading.value = false
  }
}

watch(
  () => auth.token,
  () => {
    page.value = 1
    void load()
  },
)

watch(page, () => void load())

onMounted(() => void load())

function goPost(postId: string) {
  void router.push({ name: 'post-detail', params: { id: postId } })
}

function prevPage() {
  if (page.value > 1) page.value -= 1
}

function nextPage() {
  if (page.value < totalPages.value) page.value += 1
}
</script>

<template>
  <div class="me-section">
    <h1 class="me-title">我的回复</h1>
    <el-skeleton v-if="loading" :rows="6" animated />
    <div v-else-if="error" class="list-error">{{ error }}</div>
    <div v-else-if="!items.length" class="list-empty">暂无回复</div>
    <div v-else class="reply-list">
      <el-card v-for="r in items" :key="r.id" class="reply-card" shadow="hover">
        <div class="reply-meta">
          <span class="reply-time">{{ formatPublishedUtc(r.createdAtUtc) }}</span>
          <el-link :underline="false" type="primary" @click="goPost(r.postId)">查看帖子</el-link>
        </div>
        <p class="reply-body">{{ previewBody(r.body) }}</p>
        <div class="reply-foot">
          {{ forumAuthorLabel(r.authorDisplayName, r.authorId) }}
        </div>
      </el-card>
    </div>
    <div v-if="!loading && !error && items.length" class="pager">
      <el-button :disabled="page <= 1" @click="prevPage">上一页</el-button>
      <el-button type="primary" plain>第 {{ page }} / {{ totalPages }} 页</el-button>
      <el-button :disabled="page >= totalPages" @click="nextPage">下一页</el-button>
    </div>
  </div>
</template>

<style scoped>
.me-section {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.me-title {
  margin: 0;
  font-size: var(--font-xl);
  font-weight: 700;
  line-height: 1.4;
}

.list-error {
  color: var(--text-secondary);
  padding: var(--space-md);
}

.list-empty {
  color: var(--text-secondary);
  padding: var(--space-md);
  font-size: var(--font-sm);
  line-height: var(--line-height);
}

.reply-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.reply-card {
  border-radius: var(--radius-lg);
}

.reply-meta {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--space-12);
  margin-bottom: var(--space-12);
}

.reply-time {
  font-size: var(--font-xs);
  color: var(--text-secondary);
  line-height: var(--line-height);
}

.reply-body {
  margin: 0 0 var(--space-12);
  color: var(--text-primary);
  font-size: var(--font-sm);
  line-height: var(--line-height);
  white-space: pre-wrap;
}

.reply-foot {
  font-size: var(--font-xs);
  color: var(--text-secondary);
  line-height: var(--line-height);
}

.pager {
  display: flex;
  justify-content: center;
  gap: var(--space-12);
  flex-wrap: wrap;
}
</style>
