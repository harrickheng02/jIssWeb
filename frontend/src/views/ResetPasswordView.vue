<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { completeResetPassword } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import { useLegalUiStore } from '@/stores/legalUi'
import { isStrongPassword, PASSWORD_STRONG_HINT } from '@/utils/passwordPolicy'
import './auth-view.css'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const legalUi = useLegalUiStore()

const tokenError = computed(() =>
  typeof route.query.error === 'string' ? route.query.error : null,
)
const resetSession = computed(() =>
  typeof route.query.reset_session === 'string' ? route.query.reset_session : null,
)

const password = ref('')
const password2 = ref('')
const loading = ref(false)
const errors = reactive({ password: '', password2: '' })

function validatePassword() {
  errors.password =
    !password.value || isStrongPassword(password.value) ? '' : PASSWORD_STRONG_HINT
  return !errors.password
}

function validateConfirm() {
  errors.password2 =
    !password2.value || password2.value === password.value ? '' : '两次输入的密码不一致'
  return !errors.password2
}

const canSubmit = computed(
  () =>
    Boolean(resetSession.value) &&
    Boolean(
      password.value &&
        password2.value &&
        isStrongPassword(password.value) &&
        password.value === password2.value,
    ),
)

function resetErrMessage(code: string | undefined, fallback: string) {
  switch (code) {
    case 'WEAK_PASSWORD':
      return fallback || PASSWORD_STRONG_HINT
    case 'PWD_RESET_SESSION_FAILED':
      return fallback || '会话更新失败，请使用新密码登录'
    case 'PWD_RESET_SESSION_INVALID':
      return fallback || '重置会话已失效，请重新申请'
    case 'USER_NOT_FOUND':
      return fallback || '账号不存在或已失效，请返回重新申请重置'
    case 'INVALID_INPUT':
      return fallback || '输入无效'
    case 'EMAIL_NOT_VERIFIED':
      return fallback || '邮箱未验证'
    default:
      return fallback || '重置失败'
  }
}

async function submit() {
  const rs = resetSession.value
  if (!rs) return
  const pOk = validatePassword()
  const cOk = validateConfirm()
  if (!pOk || !cOk) return
  loading.value = true
  try {
    const res = await completeResetPassword(rs, password.value)
    if (res.success && res.data) {
      auth.applyAuthSession(res.data.accessToken, res.data.refreshToken)
      ElMessage.success('密码已更新')
      await router.replace('/')
      return
    }
    ElMessage.error(resetErrMessage(res.code, res.message))
  } catch (e: any) {
    const data = e?.response?.data
    const code = data?.code as string | undefined
    const message = data?.message as string | undefined
    if (e?.response) {
      ElMessage.error(resetErrMessage(code, message))
      return
    }
    ElMessage.error('网络错误')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="auth-page auth-page--simple">
    <div class="auth-page__bg" aria-hidden="true" />
    <div class="auth-page__shell">
      <header class="auth-page__brand">
        <router-link class="auth-page__logo" to="/">JIssWeb</router-link>
      </header>

      <div class="auth-page__main">
        <el-card class="auth-page__simple-card" shadow="hover">
          <template #header>设置新密码</template>
          <p v-if="tokenError === 'RESET_TOKEN_INVALID'" class="auth-page__hint">链接无效或已过期，请重新申请重置。</p>
          <template v-else-if="resetSession">
            <p class="auth-page__hint">请设置新密码（至少 8 位，含大小写字母与数字）。</p>
            <el-form label-position="top" @submit.prevent="submit">
              <el-form-item label="新密码" :error="errors.password">
                <el-input
                  v-model="password"
                  type="password"
                  show-password
                  autocomplete="new-password"
                  @blur="validatePassword"
                />
              </el-form-item>
              <el-form-item label="确认密码" :error="errors.password2">
                <el-input
                  v-model="password2"
                  type="password"
                  show-password
                  autocomplete="new-password"
                  @blur="validateConfirm"
                />
              </el-form-item>
              <el-form-item>
                <el-button
                  type="primary"
                  class="auth-page__submit"
                  :loading="loading"
                  :disabled="!canSubmit"
                  native-type="submit"
                >
                  确认
                </el-button>
              </el-form-item>
            </el-form>
          </template>
          <p v-else class="auth-page__hint">请从邮件中的重置链接进入本页。</p>
          <router-link to="/auth">返回登录</router-link>
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
