<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  listForumNotifications,
  markAllForumNotificationsRead,
  markForumNotificationRead,
  type ForumNotificationItem,
} from '@/api/clients'

const router = useRouter()
const loading = ref(true)
const refreshing = ref(false)
const errorText = ref<string | null>(null)
const items = ref<ForumNotificationItem[]>([])
const page = ref(1)
const pageSize = ref(20)
const totalCount = ref(0)
const unreadOnly = ref(false)
const lastLoadedFilterKey = ref<string | null>(null)
const canMarkAllRead = computed(
  () => !loading.value && !refreshing.value && !errorText.value && items.value.length > 0,
)
const showInitialSkeleton = computed(() => loading.value && items.value.length === 0)

const totalPages = () => Math.max(1, Math.ceil(totalCount.value / pageSize.value))

async function load(opts?: { preserveItems?: boolean }) {
  loading.value = true
  errorText.value = null
  const filterKey = unreadOnly.value ? 'unread' : 'all'
  try {
    const res = await listForumNotifications(page.value, pageSize.value, unreadOnly.value)
    if (!res.success || !res.data) {
      errorText.value = res.message ?? '加载失败'
      if (!opts?.preserveItems) items.value = []
      return
    }
    const sameFilterAsLastLoad = lastLoadedFilterKey.value === filterKey
    if (page.value === 1 && items.value.length && sameFilterAsLastLoad) {
      const next = res.data.items
      const seen = new Set<string>()
      const merged: ForumNotificationItem[] = []
      for (const n of next) {
        if (seen.has(n.id)) continue
        seen.add(n.id)
        merged.push(n)
      }
      for (const n of items.value) {
        if (seen.has(n.id)) continue
        seen.add(n.id)
        merged.push(n)
      }
      items.value = merged
    } else {
      items.value = res.data.items
    }
    totalCount.value = res.data.totalCount
    lastLoadedFilterKey.value = filterKey
  } catch (e) {
    errorText.value = e instanceof Error ? e.message : '网络异常，请稍后重试'
    if (!opts?.preserveItems) {
      items.value = []
      totalCount.value = 0
    }
  } finally {
    loading.value = false
    refreshing.value = false
  }
}

function goPost(n: ForumNotificationItem) {
  void router.push({
    name: 'post-detail',
    params: { id: n.postId },
    query: n.replyId ? { reply: n.replyId } : undefined,
  })
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

function onToggleUnreadOnly() {
  page.value = 1
  refreshing.value = true
  lastLoadedFilterKey.value = null
  void load({ preserveItems: true })
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="notifications-page">
    <div class="notifications-head">
      <h1 class="notifications-title">通知</h1>
      <div class="notifications-actions">
        <el-switch
          v-model="unreadOnly"
          active-text="只看未读"
          @change="onToggleUnreadOnly"
        />
        <el-button
          type="primary"
          plain
          :disabled="!canMarkAllRead"
          @click="onMarkAll"
        >
          全部已读
        </el-button>
      </div>
    </div>

    <el-skeleton v-if="showInitialSkeleton" :rows="8" animated />

    <div v-else class="notifications-body" v-loading="refreshing" element-loading-background="transparent">
      <div v-if="errorText" class="notifications-error">{{ errorText }}</div>

      <div v-if="!items.length" class="notifications-empty">暂无通知</div>

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

      <div v-if="!loading && items.length" class="pager">
        <el-button :disabled="page <= 1" @click="page--; load()">上一页</el-button>
        <el-button type="primary" plain>第 {{ page }} / {{ totalPages() }} 页</el-button>
        <el-button :disabled="page >= totalPages()" @click="page++; load()">下一页</el-button>
      </div>
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

.notifications-actions {
  display: flex;
  align-items: center;
  gap: var(--space-md);
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
