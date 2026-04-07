<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { login, refresh, register, revoke } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const activeTab = ref<'login' | 'register'>('login')
const loading = ref(false)
const actionLoading = ref(false)
const lines = ref<string[]>([])

const loginForm = reactive({
  email: '',
  password: '',
})

const registerForm = reactive({
  email: '',
  password: '',
})

function applyTokens(data?: {
  accessToken: string
  refreshToken: string
}) {
  if (!data) return
  auth.setToken(data.accessToken)
  auth.setRefreshToken(data.refreshToken)
}

async function doLogin() {
  loading.value = true
  try {
    const res = await login(loginForm.email, loginForm.password)
    if (!res.success || !res.data) throw new Error(res.message ?? '登录失败')
    applyTokens(res.data)
    ElMessage.success('登录成功')
    lines.value.unshift('login: ok')
  } catch (e: any) {
    ElMessage.error(e?.message ?? '登录失败')
    lines.value.unshift('login: fail')
  } finally {
    loading.value = false
  }
}

async function doRegister() {
  loading.value = true
  try {
    const res = await register(registerForm.email, registerForm.password)
    if (!res.success || !res.data) throw new Error(res.message ?? '注册失败')
    applyTokens(res.data)
    ElMessage.success('注册成功')
    lines.value.unshift('register: ok')
  } catch (e: any) {
    ElMessage.error(e?.message ?? '注册失败')
    lines.value.unshift('register: fail')
  } finally {
    loading.value = false
  }
}

async function doRefresh() {
  if (!auth.refreshToken) return
  actionLoading.value = true
  try {
    const res = await refresh(auth.refreshToken)
    if (!res.success || !res.data) throw new Error(res.message ?? '刷新失败')
    applyTokens(res.data)
    ElMessage.success('刷新成功')
    lines.value.unshift('refresh: ok')
  } catch (e: any) {
    ElMessage.error(e?.message ?? '刷新失败')
    lines.value.unshift('refresh: fail')
  } finally {
    actionLoading.value = false
  }
}

async function doRevoke() {
  if (!auth.refreshToken) return
  actionLoading.value = true
  try {
    const res = await revoke(auth.refreshToken)
    if (!res.success) throw new Error(res.message ?? '吊销失败')
    auth.setRefreshToken(null)
    ElMessage.success('吊销成功')
    lines.value.unshift('revoke: ok')
  } catch (e: any) {
    ElMessage.error(e?.message ?? '吊销失败')
    lines.value.unshift('revoke: fail')
  } finally {
    actionLoading.value = false
  }
}

function doLogout() {
  auth.setToken(null)
  auth.setRefreshToken(null)
  lines.value.unshift('logout: ok')
}
</script>

<template>
  <div class="page">
    <el-card class="auth-card">
      <template #header>
        <div>JIssWeb 登录 / 注册</div>
      </template>

      <el-tabs v-model="activeTab">
        <el-tab-pane label="登录" name="login">
          <el-form @submit.prevent="doLogin">
            <el-form-item label="邮箱">
              <el-input v-model="loginForm.email" type="email" />
            </el-form-item>
            <el-form-item label="密码">
              <el-input v-model="loginForm.password" type="password" show-password />
            </el-form-item>
            <el-button type="primary" :loading="loading" @click="doLogin">登录</el-button>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="注册" name="register">
          <el-form @submit.prevent="doRegister">
            <el-form-item label="邮箱">
              <el-input v-model="registerForm.email" type="email" />
            </el-form-item>
            <el-form-item label="密码">
              <el-input v-model="registerForm.password" type="password" show-password />
            </el-form-item>
            <el-button type="success" :loading="loading" @click="doRegister">注册</el-button>
          </el-form>
        </el-tab-pane>
      </el-tabs>

      <div class="actions">
        <el-button :disabled="!auth.refreshToken" :loading="actionLoading" @click="doRefresh">刷新令牌</el-button>
        <el-button :disabled="!auth.refreshToken" :loading="actionLoading" @click="doRevoke">吊销刷新令牌</el-button>
        <el-button :disabled="!auth.token" @click="doLogout">退出登录</el-button>
      </div>

      <el-alert
        :title="auth.token ? '已登录' : '未登录'"
        :type="auth.token ? 'success' : 'info'"
        show-icon
        :closable="false"
      />

      <pre>{{ lines.join('\n') }}</pre>
    </el-card>
  </div>
</template>

<style scoped>
.page {
  padding: 2rem;
  display: flex;
  justify-content: center;
}
.auth-card {
  width: 560px;
}
.actions {
  margin: 1rem 0;
  display: flex;
  gap: 0.5rem;
}
pre {
  margin-top: 1rem;
  white-space: pre-wrap;
  font-family: inherit;
}
</style>
