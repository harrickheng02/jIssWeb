<script setup lang="ts">
import ModerationHubTabs from '@/components/moderation/ModerationHubTabs.vue'
</script>

<template>
  <div class="mod-layout">
    <div class="mod-layout__inner">
      <ModerationHubTabs />
      <router-view />
    </div>
  </div>
</template>

<style scoped>
.mod-layout {
  min-height: 100%;
  background: var(--bg-main);
  color: var(--text-primary);
}

.mod-layout__inner {
  max-width: var(--container-max);
  margin: 0 auto;
  padding: var(--space-lg) var(--space-md);
}
</style>
