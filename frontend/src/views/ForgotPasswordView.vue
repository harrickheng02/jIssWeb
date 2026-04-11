<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { forgotPassword } from '@/api/clients'
import { useLegalUiStore } from '@/stores/legalUi'
import './auth-view.css'

const legalUi = useLegalUiStore()
const email = ref('')
const loading = ref(false)
const done = ref(false)

const canSubmitForgot = computed(() => Boolean(email.value.trim()))

async function submit() {
  const e = email.value.trim()
  if (!e) {
    ElMessage.warning('请输入邮箱')
    return
  }
  loading.value = true
  try {
    const res = await forgotPassword(e)
    if (res.success) {
      done.value = true
      return
    }
    if (res.code === 'RATE_LIMITED' || res.code === 'FORGOT_PASSWORD_COOLDOWN') {
      ElMessage.warning(res.message || '请求过于频繁')
      return
    }
    ElMessage.error(res.message || '请求失败')
  } catch (e: any) {
    const data = e?.response?.data
    if (data?.code === 'RATE_LIMITED' || data?.code === 'FORGOT_PASSWORD_COOLDOWN') {
      ElMessage.warning(data?.message || '请求过于频繁')
      return
    }
    if (e?.response?.data?.message) {
      ElMessage.error(e.response.data.message)
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
          <template #header>忘记密码</template>
          <template v-if="!done">
            <p class="auth-page__hint">请输入邮箱，我们将发送重置密码邮件。</p>
            <el-form label-position="top" @submit.prevent="submit">
              <el-form-item label="邮箱">
                <el-input v-model="email" type="email" autocomplete="email" placeholder="name@example.com" />
              </el-form-item>
              <el-form-item>
                <el-button
                  type="primary"
                  class="auth-page__submit"
                  :loading="loading"
                  :disabled="!canSubmitForgot"
                  native-type="submit"
                >
                  发送重置邮件
                </el-button>
              </el-form-item>
            </el-form>
          </template>
          <template v-else>
            <p class="auth-page__hint">若该邮箱存在账户，您将收到一封重置密码邮件，请查收。</p>
          </template>
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
