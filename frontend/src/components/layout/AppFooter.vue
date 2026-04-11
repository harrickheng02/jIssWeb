<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { APP_VERSION } from '@/constants/app'
import { useLegalUiStore } from '@/stores/legalUi'

const legal = useLegalUiStore()

function placeholder(msg: string) {
  ElMessage.info(msg)
}
</script>

<template>
  <footer class="site-footer">
    <div class="site-footer-inner">
      <button type="button" class="foot-item" @click="placeholder('敬请期待')">关于我们</button>
      <span class="sep">·</span>
      <button type="button" class="foot-item" @click="placeholder('敬请期待')">联系方式</button>
      <span class="sep">·</span>
      <button type="button" class="foot-item foot-link" @click="legal.openAgreement()">用户协议</button>
      <span class="sep">·</span>
      <button type="button" class="foot-item foot-link" @click="legal.openPrivacy()">隐私政策</button>
      <span class="sep">·</span>
      <span class="foot-meta">v{{ APP_VERSION }}</span>
      <span class="sep">·</span>
      <span class="foot-meta">© 2026 JIssWeb</span>
    </div>
  </footer>
</template>

<style scoped>
.site-footer {
  margin-top: auto;
  padding: var(--space-md) var(--space-md) var(--space-lg);
  border-top: 1px solid var(--border-color);
  background: var(--bg-card);
}

.site-footer-inner {
  max-width: var(--container-max);
  margin: 0 auto;
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  align-items: center;
  gap: var(--space-sm) var(--space-12);
  font-size: var(--font-xs);
  line-height: var(--line-height);
  color: var(--text-secondary);
}

.foot-item {
  padding: 0;
  border: none;
  background: none;
  font: inherit;
  color: inherit;
  cursor: pointer;
}

.foot-item:hover {
  color: var(--color-primary);
}

.foot-link {
  text-decoration: underline;
  text-underline-offset: 2px;
}

.sep {
  color: var(--text-muted);
  user-select: none;
}

.foot-meta {
  color: var(--text-muted);
}

@media (max-width: 640px) {
  .site-footer-inner {
    font-size: var(--font-xs);
  }
}
</style>
