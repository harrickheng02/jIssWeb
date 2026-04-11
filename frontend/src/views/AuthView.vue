<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { login, register } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import { useLegalUiStore } from '@/stores/legalUi'
import './auth-view.css'

const auth = useAuthStore()
const legalUi = useLegalUiStore()
const router = useRouter()
const activeTab = ref<'login' | 'register'>('login')
const loading = ref(false)
const requestError = ref('')

const loginForm = reactive({
  email: '',
  password: '',
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
      return fallback || '输入无效'
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
    auth.applyAuthSession(res.data.accessToken, res.data.refreshToken)
    auth.setPendingVerifyEmail(null)
    ElMessage.success('登录成功')
    void router.push('/')
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
    ElMessage.success('注册成功')
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
</script>

<template>
  <div class="auth-page">
    <div class="auth-page__bg" aria-hidden="true" />
    <div class="auth-page__shell">
      <header class="auth-page__brand">
        <router-link class="auth-page__logo" to="/">JIssWeb</router-link>
      </header>

      <div class="auth-page__main">
        <el-card class="auth-page__card" shadow="hover">
          <el-tabs v-model="activeTab" stretch class="auth-page__tabs">
            <el-tab-pane label="登录" name="login">
              <div class="auth-page__panel">
                <el-alert
                  v-if="requestError && activeTab === 'login'"
                  class="auth-page__alert"
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
                    <div class="auth-page__pwd">
                      <el-input
                        v-model="loginForm.password"
                        type="password"
                        show-password
                        placeholder="请输入密码"
                        @blur="validateLoginPassword"
                      />
                      <div class="auth-page__pwd-extra">
                        <router-link class="auth-page__sub-link" to="/auth/forgot">忘记密码</router-link>
                      </div>
                    </div>
                  </el-form-item>

                  <el-button
                    type="primary"
                    class="auth-page__submit"
                    :loading="loading"
                    :disabled="!canSubmitLogin"
                    @click="doLogin"
                  >
                    登录
                  </el-button>
                </el-form>
              </div>
            </el-tab-pane>

            <el-tab-pane label="注册" name="register">
              <div class="auth-page__panel">
                <el-alert
                  v-if="requestError && activeTab === 'register'"
                  class="auth-page__alert"
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
                  <el-form-item class="agree-form-item" :error="registerErrors.agreed">
                    <div class="auth-page__agree">
                      <span class="auth-page__agree-cb">
                        <el-checkbox v-model="registerForm.agreed" @change="validateAgreement" />
                      </span>
                      <span class="auth-page__agree-text">
                        我已阅读并同意
                        <button type="button" class="auth-page__text-btn" @click.prevent="legalUi.openAgreement()">
                          用户协议
                        </button>
                        与
                        <button type="button" class="auth-page__text-btn" @click.prevent="legalUi.openPrivacy()">
                          隐私政策
                        </button>
                      </span>
                    </div>
                  </el-form-item>

                  <el-button
                    type="success"
                    class="auth-page__submit"
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

          <div v-if="activeTab === 'login'" class="auth-page__oauth">
            <div class="auth-page__oauth-divider">
              <span class="auth-page__oauth-line" />
              <span class="auth-page__oauth-label">其他方式登录</span>
              <span class="auth-page__oauth-line" />
            </div>
            <div class="auth-page__oauth-icons" role="group" aria-label="第三方登录预留">
              <button type="button" class="auth-page__oauth-btn auth-page__oauth-btn--wechat" disabled title="微信登录（预留）">
                <img class="auth-page__oauth-img" src="/icons/wechat.svg" width="28" height="28" alt="" />
              </button>
              <button type="button" class="auth-page__oauth-btn auth-page__oauth-btn--qq" disabled title="QQ 登录（预留）">
                <img class="auth-page__oauth-img" src="/icons/tencentqq.svg" width="28" height="28" alt="" />
              </button>
            </div>
          </div>
        </el-card>
      </div>

      <footer class="auth-page__foot">
        <button type="button" class="auth-page__foot-link" @click="legalUi.openAgreement()">用户协议</button>
        <span class="auth-page__sep">·</span>
        <button type="button" class="auth-page__foot-link" @click="legalUi.openPrivacy()">隐私政策</button>
        <span class="auth-page__sep">·</span>
        <span class="auth-page__copy">© 2026 JIssWeb</span>
      </footer>
    </div>
  </div>
</template>
