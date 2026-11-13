<script setup lang="ts">
import { computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { accuseUser } from '@/api/arena'
import { buildArenaAccusationEvent, getArenaWeekKey } from '@/utils/arenaAnalytics'
import { useCurrentUser } from '@/composables/useCurrentUser'

const props = defineProps<{
  authorId: string
}>()

const { userId, isLoggedIn } = useCurrentUser()

// 仅登录用户可见；不能指认自己（后端也会 no-op，但前端直接隐藏更友好）。
const visible = computed(() => isLoggedIn.value && userId.value !== null && userId.value !== props.authorId)

async function onAccuse() {
  try {
    await ElMessageBox.confirm(
      '你确定要标记该用户为「可疑 AI」吗？\n\n这是一次匿名、不可撤销的反馈，不会告知对方，也不会显示是否生效。',
      '标记可疑 AI',
      {
        confirmButtonText: '确认标记',
        cancelButtonText: '取消',
        type: 'warning',
        customClass: 'arena-accuse-confirm',
      },
    )
  } catch {
    // 用户取消，静默退出。
    return
  }

  // 1) 静默上报指认者行为（仅 accuserIdHash + weekKey，绝不携带被指认者身份）。
  if (userId.value) {
    buildArenaAccusationEvent({ accuserId: userId.value, weekKey: getArenaWeekKey() })
  }

  // 2) 静默指认：后端统一 200，前端不展示成败。
  await accuseUser(props.authorId)

  // 3) 中性反馈：仅告知「已收到」，不泄露是否生效（维持零感知）。
  ElMessage.success('已收到你的反馈')
}
</script>

<template>
  <button v-if="visible" type="button" class="arena-accuse-btn" @click="onAccuse">
    怀疑是 AI？
  </button>
</template>

<style scoped>
.arena-accuse-btn {
  border: 0;
  background: transparent;
  color: var(--text-muted);
  font-size: var(--font-xs);
  line-height: var(--line-height);
  padding: var(--space-xs) var(--space-sm);
  border-radius: var(--radius-pill);
  cursor: pointer;
}

.arena-accuse-btn:hover {
  color: var(--color-primary);
  background: var(--hover-bg);
}
</style>
