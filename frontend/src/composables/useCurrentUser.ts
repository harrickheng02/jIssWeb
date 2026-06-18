import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth'

/**
 * 统一暴露当前用户认证状态的 composable。
 * 消费者应通过此接口访问用户身份，而非直接依赖 Pinia auth store 的内部字段。
 */
export function useCurrentUser() {
  const auth = useAuthStore()

  /** 当前用户 ID（来自 JWT sub 字段），未登录时为 null */
  const userId = computed<string | null>(() => auth.sub)

  /** 是否已登录（token 存在） */
  const isLoggedIn = computed<boolean>(() => Boolean(auth.token))

  /** 是否拥有版主或管理员权限 */
  const canModerate = computed<boolean>(() => auth.canModerate)

  /** 是否拥有管理员权限 */
  const canAdmin = computed<boolean>(() => auth.canAdmin)

  /** 论坛角色 */
  const forumRole = computed(() => auth.forumRole)

  return {
    userId,
    isLoggedIn,
    canModerate,
    canAdmin,
    forumRole,
  }
}
