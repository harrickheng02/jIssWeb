<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import TopbarSearch from '@/components/layout/TopbarSearch.vue'
import HeaderUserMenu from '@/components/layout/HeaderUserMenu.vue'

const router = useRouter()
const auth = useAuthStore()

const navItems = [
  { id: 'home', label: '首页' },
  { id: 'boards', label: '板块' },
  { id: 'hot', label: '热门' },
]

const isAuthed = computed(() => Boolean(auth.token))

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
            @click="handleOpenPlaceholder(item.label)"
          >
            {{ item.label }}
          </el-button>
        </nav>
      </div>

      <div class="topbar-actions">
        <TopbarSearch />
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
