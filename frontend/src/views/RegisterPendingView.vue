<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { resendVerification } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const email = ref(auth.pendingVerifyEmail ?? '')
const loading = ref(false)
const now = ref(Date.now())
let timer: number | null = null

const remainingSeconds = computed(() => {
  const diff = Math.ceil((auth.pendingVerifyCooldownUntil - now.value) / 1000)
  return diff > 0 ? diff : 0
})

const resendDisabled = computed(() => loading.value || remainingSeconds.value > 0)
const resendButtonText = computed(() =>
  remainingSeconds.value > 0 ? `${remainingSeconds.value} 秒后可重发` : '重发验证邮件',
)

function getRetryAfterSeconds(error: any) {
  const value = Number(error?.response?.headers?.['retry-after'] ?? 0)
  return Number.isFinite(value) && value > 0 ? value : 60
}

async function doResend() {
  if (resendDisabled.value) return
  if (!email.value) {
    ElMessage.error('请输入邮箱')
    return
  }
  loading.value = true
  try {
    const res = await resendVerification(email.value)
    if (!res.success) throw new Error(res.message ?? '重发失败')
    auth.setPendingVerifyEmail(email.value)
    auth.setPendingVerifyCooldown(60)
    ElMessage.success('验证邮件已发送')
    console.log('resend: ok')
  } catch (e: any) {
    if (e?.response?.data?.code === 'RESEND_COOLDOWN') {
      auth.setPendingVerifyCooldown(getRetryAfterSeconds(e))
    }
    ElMessage.error(e?.response?.data?.message ?? e?.message ?? '重发失败')
    console.error('resend: fail', e)
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  timer = window.setInterval(() => {
    now.value = Date.now()
  }, 1000)
})

onUnmounted(() => {
  if (timer !== null) window.clearInterval(timer)
})
</script>

<template>
  <div class="page">
    <el-card class="card">
      <template #header>邮箱验证</template>
      <el-alert title="请先完成邮箱验证后再登录" type="warning" :closable="false" show-icon />
      <el-form class="form" @submit.prevent="doResend">
        <el-form-item label="邮箱">
          <el-input v-model="email" type="email" />
        </el-form-item>
        <el-button type="primary" :loading="loading" :disabled="resendDisabled" @click="doResend">
          {{ resendButtonText }}
        </el-button>
      </el-form>
      <router-link to="/auth">返回登录</router-link>
    </el-card>
  </div>
</template>

<style scoped>
.page {
  padding: 2rem;
  display: flex;
  justify-content: center;
}
.card {
  width: 560px;
}
.form {
  margin: 1rem 0;
}
</style>
