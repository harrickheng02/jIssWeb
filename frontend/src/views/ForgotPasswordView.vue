<script setup lang="ts">
import { useLegalUiStore } from '@/stores/legalUi'
import './auth-view.css'

const legalUi = useLegalUiStore()
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
          <p class="auth-page__hint">该功能尚未开放，敬请期待。</p>
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
