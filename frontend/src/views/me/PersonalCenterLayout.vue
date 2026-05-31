<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { storeToRefs } from 'pinia'
import { useDraftUiStore } from '@/stores/draftUi'

const route = useRoute()
const activePath = computed(() => route.path)
const draftUi = useDraftUiStore()
const { badgeCount } = storeToRefs(draftUi)

function isDraftsRoute(path: string) {
  return path.startsWith('/me/drafts')
}

watch(
  () => route.path,
  (path) => {
    if (isDraftsRoute(path)) draftUi.clearBadge()
  },
  { immediate: true },
)

onMounted(() => {
  if (!isDraftsRoute(route.path)) void draftUi.refreshBadgeFromServer()
})
</script>

<template>
  <div class="me-page">
    <div class="me-inner">
      <aside class="me-aside">
        <el-card class="me-aside-card" shadow="never">
          <template #header>个人中心</template>
          <el-menu class="me-menu" :default-active="activePath" router>
            <el-menu-item index="/me/posts">我的帖子</el-menu-item>
            <el-menu-item index="/me/drafts">
              <span>草稿箱</span>
              <el-badge v-if="badgeCount > 0" :value="badgeCount" class="draft-badge" />
            </el-menu-item>
            <el-menu-item index="/me/replies">我的回复</el-menu-item>
            <el-menu-item index="/me/favorites">我的收藏</el-menu-item>
            <el-menu-item index="/me/profile">个人资料</el-menu-item>
            <el-menu-item index="/me/settings">设置</el-menu-item>
          </el-menu>
        </el-card>
      </aside>
      <main class="me-main">
        <router-view />
      </main>
    </div>
  </div>
</template>

<style scoped>
.me-page {
  min-height: 100%;
  background: var(--bg-main);
  color: var(--text-primary);
}

.me-inner {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
  display: flex;
  gap: var(--space-lg);
  align-items: flex-start;
}

.me-aside {
  width: 240px;
  flex-shrink: 0;
}

.me-aside-card {
  border-radius: var(--radius-lg);
}

.me-menu {
  border-right: none;
}

.me-menu-divider {
  margin: var(--space-sm) 0;
}

.me-main {
  flex: 1;
  min-width: 0;
}

.draft-badge {
  margin-left: var(--space-sm);
}

@media (max-width: 900px) {
  .me-inner {
    flex-direction: column;
  }

  .me-aside {
    width: 100%;
  }
}
</style>
