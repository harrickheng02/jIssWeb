<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AppHeader from '@/components/layout/AppHeader.vue'
import AppFooter from '@/components/layout/AppFooter.vue'
import LegalDialogs from '@/components/legal/LegalDialogs.vue'

const route = useRoute()
const showAppShell = computed(() => !route.meta.hideAppShell)
</script>

<template>
  <div class="app-layout">
    <AppHeader v-if="showAppShell" />
    <main class="app-main">
      <router-view />
    </main>
    <AppFooter v-if="showAppShell" />
    <LegalDialogs />
  </div>
</template>

<style scoped>
.app-layout {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  background: var(--bg-main);
  color: var(--text-primary);
}

.app-main {
  flex: 1;
  width: 100%;
  min-height: 0;
}
</style>
