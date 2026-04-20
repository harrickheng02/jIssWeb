<script setup lang="ts">
import { Bell } from '@element-plus/icons-vue'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { getForumUnreadNotificationCount } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import TopbarSearch from '@/components/layout/TopbarSearch.vue'
import HeaderUserMenu from '@/components/layout/HeaderUserMenu.vue'
import { formatBadgeCount } from '@/utils/formatBadgeCount'

const router = useRouter()
const auth = useAuthStore()
const unreadCount = ref(0)
const unreadBadgeValue = computed(() => formatBadgeCount(unreadCount.value, 99))

function refreshUnread() {
  if (!auth.token) {
    unreadCount.value = 0
    return
  }
  void getForumUnreadNotificationCount().then((r) => {
    if (r.success && r.data !== undefined) unreadCount.value = r.data.count
    else unreadCount.value = 0
  })
}

function onNotificationsChanged() {
  refreshUnread()
}

onMounted(() => {
  refreshUnread()
  window.addEventListener('jiss-forum-notifications-changed', onNotificationsChanged)
})

onUnmounted(() => {
  window.removeEventListener('jiss-forum-notifications-changed', onNotificationsChanged)
})

const navItems = [
  { id: 'home', label: '首页' },
  { id: 'boards', label: '板块' },
  { id: 'hot', label: '热门' },
]

const isAuthed = computed(() => Boolean(auth.token))

watch(
  () => auth.token,
  () => refreshUnread(),
)

function handleCreatePost() {
  if (!isAuthed.value) {
    void router.push('/auth')
    return
  }
  ElMessage.info('发帖功能开发中')
}

function handleOpenPlaceholder(name: string) {
  ElMessage.info(`${name}页面开发中`)
}

function handleNavClick(id: string, label: string) {
  if (id === 'home') {
    void router.push('/')
    return
  }
  handleOpenPlaceholder(label)
}
</script>

<template>
  <header class="topbar">
    <div class="topbar-inner">
      <div class="brand-wrap">
        <router-link class="brand" to="/">JIssWeb</router-link>
        <nav class="main-nav">
          <el-button
            v-for="item in navItems"
            :key="item.id"
            link
            class="nav-btn"
            @click="handleNavClick(item.id, item.label)"
          >
            {{ item.label }}
          </el-button>
        </nav>
      </div>

      <div class="topbar-actions">
        <TopbarSearch />
        <router-link v-if="isAuthed" to="/notifications" class="notify-link">
          <el-badge :value="unreadBadgeValue" :hidden="unreadCount === 0" class="notify-badge">
            <el-button :icon="Bell" circle aria-label="通知" />
          </el-badge>
        </router-link>
        <el-button type="primary" @click="handleCreatePost">发帖</el-button>
        <HeaderUserMenu />
      </div>
    </div>
  </header>
</template>

<style scoped>
.topbar {
  position: sticky;
  top: 0;
  z-index: 100;
  background: var(--bg-header);
  border-bottom: 1px solid var(--border-color);
  backdrop-filter: blur(8px);
}

.topbar-inner {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-12) var(--space-md);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--space-12);
}

.brand-wrap,
.topbar-actions,
.main-nav {
  display: flex;
  align-items: center;
  gap: var(--space-12);
}

.brand {
  font-size: var(--font-brand);
  font-weight: 700;
  color: var(--text-primary);
  text-decoration: none;
  line-height: var(--line-height);
}

.nav-btn {
  color: var(--text-secondary);
  text-decoration: none;
  font-size: var(--font-sm);
}

.notify-link {
  display: inline-flex;
  align-items: center;
  text-decoration: none;
}

.notify-badge :deep(.el-badge__content) {
  border: none;
}

@media (max-width: 900px) {
  .topbar-inner,
  .brand-wrap,
  .topbar-actions {
    flex-direction: column;
    align-items: stretch;
  }

  .main-nav {
    flex-wrap: wrap;
  }
}

@media (max-width: 640px) {
  .topbar-inner {
    padding-left: var(--space-12);
    padding-right: var(--space-12);
  }
}
</style>
