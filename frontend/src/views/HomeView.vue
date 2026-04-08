<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { login, register } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const activeTab = ref<'login' | 'register'>('login')
const loading = ref(false)
const requestError = ref('')

const loginForm = reactive({
  email: '',
  password: '',
  rememberMe: auth.rememberMe,
})

const registerForm = reactive({
  email: '',
  password: '',
  confirmPassword: '',
  agreed: false,
})

const loginErrors = reactive({
  email: '',
  password: '',
})

const registerErrors = reactive({
  email: '',
  password: '',
  confirmPassword: '',
  agreed: '',
})

const canSubmitLogin = computed(() => Boolean(loginForm.email && loginForm.password))
const canSubmitRegister = computed(() =>
  Boolean(registerForm.email && registerForm.password && registerForm.confirmPassword && registerForm.agreed),
)

watch(activeTab, () => {
  requestError.value = ''
})

function isValidEmail(v: string) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v)
}

function isStrongPassword(v: string) {
  return /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$/.test(v)
}

function getAuthErrorMessage(code?: string, fallback?: string) {
  switch (code) {
    case 'INVALID_INPUT':
      return '请输入正确的邮箱和密码'
    case 'USER_NOT_FOUND':
    case 'LOGIN_FAILED':
      return '账号或密码错误'
    case 'EMAIL_NOT_VERIFIED':
      return '邮箱未验证，请先完成验证'
    case 'LOGIN_COOLDOWN':
      return '登录失败次数过多，请稍后再试'
    case 'LOGIN_RATE_LIMITED':
    case 'RATE_LIMITED':
      return '请求过于频繁，请稍后再试'
    case 'EMAIL_EXISTS':
      return '邮箱已注册'
    case 'WEAK_PASSWORD':
      return '密码至少8位，含大小写字母和数字'
    case 'MAIL_SEND_FAILED':
      return '验证邮件发送失败，请稍后重试'
    case 'RESEND_COOLDOWN':
      return fallback || '操作过于频繁，请稍后重试'
    case 'RESEND_DAILY_LIMITED':
      return '今日验证邮件发送次数过多，请明天再试'
    default:
      return fallback || '操作失败'
  }
}

function getRetryAfterSeconds(error: any) {
  const value = Number(error?.response?.headers?.['retry-after'] ?? 0)
  return Number.isFinite(value) && value > 0 ? value : 60
}

function validateLoginEmail() {
  loginErrors.email = !loginForm.email || isValidEmail(loginForm.email) ? '' : '邮箱格式不正确'
  return !loginErrors.email
}

function validateLoginPassword() {
  loginErrors.password = loginForm.password ? '' : '密码不能为空'
  return !loginErrors.password
}

function validateRegisterEmail() {
  registerErrors.email = !registerForm.email || isValidEmail(registerForm.email) ? '' : '邮箱格式不正确'
  return !registerErrors.email
}

function validateRegisterPassword() {
  registerErrors.password =
    !registerForm.password || isStrongPassword(registerForm.password) ? '' : '密码至少8位，含大小写字母和数字'
  return !registerErrors.password
}

function validateRegisterConfirmPassword() {
  registerErrors.confirmPassword =
    !registerForm.confirmPassword || registerForm.confirmPassword === registerForm.password ? '' : '两次输入的密码不一致'
  return !registerErrors.confirmPassword
}

function validateAgreement() {
  registerErrors.agreed = registerForm.agreed ? '' : '请先同意用户协议和隐私政策'
  return !registerErrors.agreed
}

async function doLogin() {
  requestError.value = ''
  const emailOk = validateLoginEmail()
  const passwordOk = validateLoginPassword()
  if (!emailOk || !passwordOk) return

  loading.value = true
  try {
    const res = await login(loginForm.email, loginForm.password)
    if (!res.success || !res.data) throw new Error(res.message ?? '登录失败')
    auth.applyAuthSession(res.data.accessToken, res.data.refreshToken, loginForm.rememberMe)
    auth.setPendingVerifyEmail(null)
    ElMessage.success('登录成功')
    console.log('login: ok')
  } catch (e: any) {
    const code = e?.response?.data?.code
    const message = getAuthErrorMessage(code, e?.response?.data?.message ?? e?.message ?? '登录失败')
    requestError.value = message
    if (code === 'EMAIL_NOT_VERIFIED') {
      auth.setPendingVerifyEmail(loginForm.email)
      ElMessage.warning(message)
      void router.push('/register/pending')
      return
    }
    ElMessage.error(message)
    console.error('login: fail', e)
  } finally {
    loading.value = false
  }
}

async function doRegister() {
  requestError.value = ''
  const emailOk = validateRegisterEmail()
  const passwordOk = validateRegisterPassword()
  const confirmOk = validateRegisterConfirmPassword()
  const agreementOk = validateAgreement()
  if (!emailOk || !passwordOk || !confirmOk || !agreementOk) return

  loading.value = true
  try {
    const res = await register(registerForm.email, registerForm.password)
    if (!res.success) throw new Error(res.message ?? '注册失败')
    auth.setPendingVerifyEmail(registerForm.email)
    auth.setPendingVerifyCooldown(60)
    ElMessage.success('注册成功，请查收验证邮件')
    console.log('register: ok')
    void router.push('/register/pending')
  } catch (e: any) {
    const code = e?.response?.data?.code
    const message = getAuthErrorMessage(code, e?.response?.data?.message ?? e?.message ?? '注册失败')
    if (code === 'RESEND_COOLDOWN') {
      auth.setPendingVerifyEmail(registerForm.email)
      auth.setPendingVerifyCooldown(getRetryAfterSeconds(e))
      void router.push('/register/pending')
    }
    requestError.value = message
    ElMessage.error(message)
    console.error('register: fail', e)
  } finally {
    loading.value = false
  }
}

function doLogout() {
  auth.clearAuth()
  console.log('logout: ok')
}
</script>

<template>
  <div class="page">
    <el-card class="auth-card" shadow="hover">
      <div class="header">
        <div>
          <div class="brand">JIssWeb</div>
          <div class="subtitle">统一账号登录与注册</div>
        </div>
        <el-link href="/" :underline="false">返回首页</el-link>
      </div>

      <el-tabs v-model="activeTab" stretch class="tabs">
        <el-tab-pane label="登录" name="login">
          <div class="panel">
            <el-alert
              v-if="requestError"
              class="request-alert"
              :title="requestError"
              type="error"
              :closable="false"
              show-icon
            />

            <el-form @submit.prevent="doLogin">
              <el-form-item label="邮箱" :error="loginErrors.email">
                <el-input v-model="loginForm.email" type="email" placeholder="请输入邮箱" @blur="validateLoginEmail" />
              </el-form-item>
              <el-form-item label="密码" :error="loginErrors.password">
                <el-input
                  v-model="loginForm.password"
                  type="password"
                  show-password
                  placeholder="请输入密码"
                  @blur="validateLoginPassword"
                />
              </el-form-item>

              <div class="row-between">
                <el-checkbox v-model="loginForm.rememberMe">免登录</el-checkbox>
                <el-button link disabled>忘记密码（预留）</el-button>
              </div>

              <el-button type="primary" class="submit-btn" :loading="loading" :disabled="!canSubmitLogin" @click="doLogin">
                登录
              </el-button>
            </el-form>

            <div v-if="auth.token" class="after-login">
              <el-button @click="doLogout">退出登录</el-button>
              <div class="nav-link">
                <router-link to="/customers">客档管理</router-link>
                <router-link to="/profile">个人资料</router-link>
              </div>
            </div>
          </div>
        </el-tab-pane>

        <el-tab-pane label="注册" name="register">
          <div class="panel">
            <el-alert
              v-if="requestError"
              class="request-alert"
              :title="requestError"
              type="error"
              :closable="false"
              show-icon
            />

            <el-form @submit.prevent="doRegister">
              <el-form-item label="邮箱" :error="registerErrors.email">
                <el-input v-model="registerForm.email" type="email" placeholder="请输入邮箱" @blur="validateRegisterEmail" />
              </el-form-item>
              <el-form-item label="密码" :error="registerErrors.password">
                <el-input
                  v-model="registerForm.password"
                  type="password"
                  show-password
                  placeholder="至少8位，含大小写字母和数字"
                  @blur="validateRegisterPassword"
                />
              </el-form-item>
              <el-form-item label="确认密码" :error="registerErrors.confirmPassword">
                <el-input
                  v-model="registerForm.confirmPassword"
                  type="password"
                  show-password
                  placeholder="请再次输入密码"
                  @blur="validateRegisterConfirmPassword"
                />
              </el-form-item>
              <el-form-item>
                <el-alert title="注册后会向邮箱发送验证链接" type="info" :closable="false" show-icon />
              </el-form-item>
              <el-form-item :error="registerErrors.agreed">
                <el-checkbox v-model="registerForm.agreed" @change="validateAgreement">
                  我已阅读并同意用户协议与隐私政策
                </el-checkbox>
              </el-form-item>

              <el-button
                type="success"
                class="submit-btn"
                :loading="loading"
                :disabled="!canSubmitRegister"
                @click="doRegister"
              >
                注册
              </el-button>
            </el-form>
          </div>
        </el-tab-pane>
      </el-tabs>

      <div class="divider-label">第三方登录（预留）</div>
      <div class="oauth-actions">
        <el-button disabled>微信登录</el-button>
        <el-button disabled>GitHub 登录</el-button>
      </div>

      <el-alert
        class="status"
        :title="auth.token ? '已登录' : '未登录'"
        :type="auth.token ? 'success' : 'info'"
        :closable="false"
        show-icon
      />

      <div class="footer">
        <div class="footer-links">
          <el-link :underline="false" disabled>用户协议</el-link>
          <el-link :underline="false" disabled>隐私政策</el-link>
        </div>
        <div class="copyright">© 2026 JIssWeb</div>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.page {
  min-height: 100vh;
  padding: 24px 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(180deg, #f5f8ff 0%, #eef2f8 100%);
}

.auth-card {
  width: 100%;
  max-width: 560px;
  border-radius: 16px;
}

.header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 8px;
}

.brand {
  font-size: 20px;
  font-weight: 700;
  line-height: 1.2;
}

.subtitle {
  margin-top: 6px;
  color: #606266;
  font-size: 14px;
}

.tabs {
  margin-top: 12px;
}

.panel {
  animation: auth-fade 0.2s ease;
}

.request-alert {
  margin-bottom: 16px;
}

.row-between {
  margin-bottom: 16px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}

.submit-btn {
  width: 100%;
  min-height: 44px;
}

.after-login {
  margin-top: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.nav-link {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
}

.divider-label {
  margin: 20px 0 12px;
  color: #909399;
  text-align: center;
  font-size: 14px;
}

.oauth-actions {
  display: flex;
  gap: 12px;
  justify-content: center;
  flex-wrap: wrap;
}

.status {
  margin-top: 20px;
}

.footer {
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid #ebeef5;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  color: #909399;
  font-size: 13px;
}

.footer-links {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
}

@keyframes auth-fade {
  from {
    opacity: 0;
    transform: translateY(6px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

@media (max-width: 640px) {
  .page {
    padding: 12px;
  }

  .auth-card {
    border-radius: 12px;
  }

  .header,
  .footer,
  .row-between {
    flex-direction: column;
    align-items: stretch;
  }

  .brand {
    font-size: 18px;
  }

  .subtitle,
  .divider-label,
  .footer {
    font-size: 13px;
  }

  .oauth-actions {
    flex-direction: column;
  }
}
</style>
