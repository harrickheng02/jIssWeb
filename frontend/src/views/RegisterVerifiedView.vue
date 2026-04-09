<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import { exchangeVerifySession } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const subtitle = ref('正在跳转…')

onMounted(async () => {
  auth.setPendingVerifyEmail(null)
  const code = typeof route.query.verify_session === 'string' ? route.query.verify_session : null
  if (code) {
    subtitle.value = '正在登录…'
    try {
      const res = await exchangeVerifySession(code)
      if (res.success && res.data) {
        auth.applyAuthSession(res.data.accessToken, res.data.refreshToken)
        await router.replace('/')
        return
      }
      ElMessage.error(res.message || '会话已失效')
    } catch {
      ElMessage.error('会话已失效')
    }
    await router.replace('/')
    return
  }
  setTimeout(() => {
    void router.replace('/')
  }, 1200)
})
</script>

<template>
  <div class="page">
    <el-result icon="success" title="邮箱验证成功" :sub-title="subtitle" />
  </div>
</template>

<style scoped>
.page {
  padding: 3rem 0;
}
</style>
