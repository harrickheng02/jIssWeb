<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { UserFilled, ArrowRight, SwitchButton } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import { useThemeStore } from '@/stores/theme'
import { getProfile } from '@/api/clients'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const theme = useThemeStore()
const nickname = ref('')

const isAuthed = computed(() => Boolean(auth.token))

const displayName = computed(() => nickname.value.trim() || '用户')

const avatarText = computed(() => {
  const n = displayName.value
  return n ? n.slice(0, 1).toUpperCase() : 'U'
})

async function loadNickname() {
  if (!auth.token) {
    nickname.value = ''
    return
  }
  try {
    const res = await getProfile()
    if (res.success && res.data?.nickname?.trim()) nickname.value = res.data.nickname.trim()
    else nickname.value = ''
  } catch {
    nickname.value = ''
  }
}

watch(
  () => auth.token,
  () => {
    void loadNickname()
  },
  { immediate: true },
)

function onProfileUpdated() {
  void loadNickname()
}

onMounted(() => {
  window.addEventListener('jiss-profile-updated', onProfileUpdated)
})

onUnmounted(() => {
  window.removeEventListener('jiss-profile-updated', onProfileUpdated)
})

function goAuth() {
  void router.push('/auth')
}

function onThemeDarkSwitch(value: string | number | boolean) {
  theme.setTheme(value === true ? 'dark' : 'light')
}

function logout() {
  auth.clearAuth()
  ElMessage.success('已退出')
  if (route.path.startsWith('/profile') || route.path.startsWith('/me')) void router.push('/')
}
</script>

<template>
  <div v-if="!isAuthed" class="avatar-slot avatar-slot--guest">
    <div class="theme-switch-guest" title="深色模式">
      <el-switch :model-value="theme.mode === 'dark'" size="small" @change="onThemeDarkSwitch" />
    </div>
    <div
      class="avatar-trigger avatar-trigger--guest"
      role="button"
      tabindex="0"
      title="登录 / 注册"
      @click="goAuth"
      @keydown.enter.prevent="goAuth"
    >
      <el-avatar :size="40" :icon="UserFilled" />
    </div>
  </div>

  <div v-else class="avatar-slot">
    <el-popover
      placement="bottom-end"
      :width="280"
      trigger="hover"
      :show-arrow="false"
      popper-class="topbar-user-popover"
      :offset="8"
      :hide-after="200"
    >
      <template #reference>
        <div class="avatar-trigger avatar-trigger--user" title="账号菜单">
          <el-avatar :size="40">{{ avatarText }}</el-avatar>
        </div>
      </template>
      <div class="user-pop">
        <div class="user-pop-head">
          <el-avatar :size="48">{{ avatarText }}</el-avatar>
          <div class="user-pop-meta">
            <div class="user-pop-name">{{ displayName }}</div>
            <div class="user-pop-desc">社区统计与积分即将推出</div>
          </div>
        </div>
        <el-divider class="user-pop-divider" />
        <router-link class="user-pop-row" to="/me">
          <span>个人中心</span>
          <el-icon class="user-pop-row-icon"><ArrowRight /></el-icon>
        </router-link>
        <router-link class="user-pop-row" to="/profile">
          <span>个人资料</span>
          <el-icon class="user-pop-row-icon"><ArrowRight /></el-icon>
        </router-link>
        <div class="user-pop-theme">
          <span class="user-pop-theme-label">深色模式</span>
          <el-switch :model-value="theme.mode === 'dark'" size="small" @change="onThemeDarkSwitch" />
        </div>
        <el-divider class="user-pop-divider" />
        <button type="button" class="user-pop-row user-pop-row--logout" @click="logout">
          <span>退出登录</span>
          <el-icon class="user-pop-row-icon"><SwitchButton /></el-icon>
        </button>
      </div>
    </el-popover>
  </div>
</template>

<style scoped>
.avatar-slot {
  display: flex;
  align-items: center;
  flex-shrink: 0;
}

.avatar-slot--guest {
  gap: var(--space-12);
}

.theme-switch-guest {
  display: flex;
  align-items: center;
}

.avatar-trigger {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  border-radius: 50%;
  outline: none;
}

.avatar-trigger:focus-visible {
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--color-primary) 35%, transparent);
}

.avatar-trigger--guest :deep(.el-avatar) {
  background: var(--hover-bg);
  color: var(--text-secondary);
}

.avatar-trigger--user:hover :deep(.el-avatar) {
  opacity: 0.92;
}

.user-pop-head {
  display: flex;
  align-items: center;
  gap: var(--space-12);
}

.user-pop-meta {
  min-width: 0;
  flex: 1;
}

.user-pop-name {
  font-size: var(--font-md);
  font-weight: 600;
  color: var(--color-primary);
  line-height: 1.4;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.user-pop-desc {
  margin-top: var(--space-xs);
  font-size: var(--font-xs);
  color: var(--text-secondary);
  line-height: var(--line-height);
}

.user-pop-divider {
  margin: var(--space-12) 0;
}

.user-pop-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: var(--space-sm) var(--space-xs);
  border: none;
  background: none;
  font-size: var(--font-sm);
  color: var(--text-primary);
  text-decoration: none;
  cursor: pointer;
  border-radius: var(--radius-md);
  box-sizing: border-box;
  line-height: var(--line-height);
}

.user-pop-row:hover {
  background: var(--hover-bg);
}

.user-pop-row-icon {
  color: var(--text-muted);
  font-size: var(--font-sm);
}

.user-pop-theme {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: var(--space-sm) var(--space-xs);
  border-radius: var(--radius-md);
  box-sizing: border-box;
}

.user-pop-theme:hover {
  background: var(--hover-bg);
}

.user-pop-theme-label {
  font-size: var(--font-sm);
  color: var(--text-primary);
  line-height: var(--line-height);
}

.user-pop-row--logout {
  margin-top: var(--space-xs);
  color: var(--text-secondary);
}

.user-pop-row--logout:hover {
  color: var(--el-color-danger);
  background: var(--el-color-danger-light-9);
}
</style>

<style>
.topbar-user-popover.el-popper {
  padding: var(--space-md);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-popover);
}
</style>
