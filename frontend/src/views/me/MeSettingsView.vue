<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'
import { SwitchButton } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import { useThemeStore } from '@/stores/theme'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const theme = useThemeStore()

function onThemeDarkSwitch(value: string | number | boolean) {
  theme.setTheme(value === true ? 'dark' : 'light')
}

function logout() {
  auth.clearAuth()
  ElMessage.success('已退出')
  if (route.path.startsWith('/profile') || route.path.startsWith('/me')) {
    void router.push('/')
  }
}
</script>

<template>
  <div class="me-section">
    <h1 class="me-title">设置</h1>
    <el-card class="settings-card" shadow="never">
      <div class="settings-row">
        <span class="settings-label">深色模式</span>
        <el-switch :model-value="theme.mode === 'dark'" @change="onThemeDarkSwitch" />
      </div>
      <p class="settings-hint">更多账户与通知设置将在后续版本提供。</p>
      <el-divider />
      <el-button type="danger" plain @click="logout">
        <el-icon class="btn-icon"><SwitchButton /></el-icon>
        退出登录
      </el-button>
    </el-card>
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

.settings-card {
  border-radius: var(--radius-lg);
  max-width: 480px;
}

.settings-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-md);
}

.settings-label {
  font-size: var(--font-sm);
  color: var(--text-primary);
  line-height: var(--line-height);
}

.settings-hint {
  margin: var(--space-md) 0 0;
  font-size: var(--font-xs);
  color: var(--text-secondary);
  line-height: var(--line-height);
}

.btn-icon {
  margin-right: var(--space-xs);
  vertical-align: text-bottom;
}
</style>
