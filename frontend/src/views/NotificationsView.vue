<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  listForumNotifications,
  markAllForumNotificationsRead,
  markForumNotificationRead,
  type ForumNotificationItem,
} from '@/api/clients'

const router = useRouter()
const loading = ref(true)
const errorText = ref<string | null>(null)
const items = ref<ForumNotificationItem[]>([])
const page = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)

const totalPages = () => Math.max(1, Math.ceil(totalCount.value / pageSize.value))

async function load() {
  loading.value = true
  errorText.value = null
  try {
    const res = await listForumNotifications(page.value, pageSize.value, false)
    if (!res.success || !res.data) {
      errorText.value = res.message ?? '加载失败'
      items.value = []
      return
    }
    items.value = res.data.items
    totalCount.value = res.data.totalCount
  } catch (e) {
    errorText.value = e instanceof Error ? e.message : '网络异常，请稍后重试'
    items.value = []
    totalCount.value = 0
  } finally {
    loading.value = false
  }
}

function goPost(n: ForumNotificationItem) {
  void router.push(`/posts/${n.postId}`)
}

async function onMarkRead(n: ForumNotificationItem, ev: Event) {
  ev.stopPropagation()
  const r = await markForumNotificationRead(n.id)
  if (r.success) {
    n.read = true
    window.dispatchEvent(new CustomEvent('jiss-forum-notifications-changed'))
  }
}

async function onMarkAll() {
  const r = await markAllForumNotificationsRead()
  if (r.success) {
    items.value = items.value.map((x) => ({ ...x, read: true }))
    window.dispatchEvent(new CustomEvent('jiss-forum-notifications-changed'))
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="notifications-page">
    <div class="notifications-head">
      <h1 class="notifications-title">通知</h1>
      <el-button
        v-if="!loading && !errorText && items.length"
        type="primary"
        plain
        @click="onMarkAll"
      >
        全部已读
      </el-button>
    </div>

    <el-skeleton v-if="loading" :rows="8" animated />

    <div v-else-if="errorText" class="notifications-error">{{ errorText }}</div>

    <div v-else-if="!items.length" class="notifications-empty">暂无通知</div>

    <div v-else class="notifications-list">
      <el-card
        v-for="n in items"
        :key="n.id"
        shadow="hover"
        class="notification-card"
        :class="{ 'is-unread': !n.read }"
        @click="goPost(n)"
      >
        <div class="notification-row">
          <div class="notification-main">
            <span v-if="!n.read" class="dot" aria-hidden="true" />
            <span class="notification-text">
              <strong>{{ n.actorDisplayName?.trim() || n.actorId }}</strong>
              回复了你的帖子「{{ n.postTitle }}」
            </span>
          </div>
          <el-button
            v-if="!n.read"
            type="primary"
            link
            @click="onMarkRead(n, $event)"
          >
            标为已读
          </el-button>
        </div>
      </el-card>
    </div>

    <div v-if="!loading && !errorText && items.length" class="pager">
      <el-button :disabled="page <= 1" @click="page--; load()">上一页</el-button>
      <el-button type="primary" plain>第 {{ page }} / {{ totalPages() }} 页</el-button>
      <el-button :disabled="page >= totalPages()" @click="page++; load()">下一页</el-button>
    </div>
  </div>
</template>

<style scoped>
.notifications-page {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
  min-height: 100%;
  background: var(--bg-main);
  color: var(--text-primary);
}

.notifications-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-md);
  margin-bottom: var(--space-md);
}

.notifications-title {
  font-size: var(--font-xl);
  font-weight: 700;
  margin: 0;
  line-height: var(--line-height);
}

.notifications-error {
  padding: var(--space-lg);
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  border-radius: var(--radius-md);
  background: var(--bg-card);
}

.notifications-empty {
  padding: var(--space-40);
  text-align: center;
  color: var(--text-secondary);
  border: 1px dashed var(--border-color);
  border-radius: var(--radius-md);
  background: var(--bg-card);
}

.notifications-list {
  display: flex;
  flex-direction: column;
  gap: var(--space-md);
}

.notification-card {
  cursor: pointer;
  border-radius: var(--radius-md);
  border: 1px solid var(--border-color);
  background: var(--bg-card);
}

.notification-card.is-unread {
  border-color: color-mix(in srgb, var(--color-primary) 35%, var(--border-color));
}

.notification-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-md);
}

.notification-main {
  display: flex;
  gap: var(--space-8);
  align-items: flex-start;
  min-width: 0;
}

.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-top: 6px;
  flex-shrink: 0;
  background: var(--color-primary);
}

.notification-text {
  font-size: var(--font-sm);
  line-height: var(--line-height);
  word-break: break-word;
}

.pager {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-md);
  margin-top: var(--space-lg);
}
</style>
