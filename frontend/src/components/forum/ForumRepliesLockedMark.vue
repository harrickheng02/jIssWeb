<script setup lang="ts">
import { Lock } from '@element-plus/icons-vue'

withDefaults(
  defineProps<{
    /** 图标边长（近似），与 Element Plus `el-icon` 一致 */
    size?: number
  }>(),
  { size: 16 },
)
</script>

<template>
  <span
    class="forum-replies-locked-mark"
    title="暂时无法回复"
    aria-label="已锁定回复"
    role="img"
  >
    <el-icon :size="size" class="forum-replies-locked-mark__icon">
      <Lock />
    </el-icon>
  </span>
</template>

<style scoped>
.forum-replies-locked-mark {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  vertical-align: middle;
  line-height: 0;
}

.forum-replies-locked-mark__icon {
  color: var(--el-color-danger);
}
</style>
