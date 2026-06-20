import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { getForumUnreadNotificationCount } from '@/api/clients'
import { formatBadgeCount } from '@/utils/formatBadgeCount'
import { useCurrentUser } from '@/composables/useCurrentUser'

const UNREAD_POLL_MS = 60_000

/**
 * 通知未读徽标数量管理 composable（轮询 + 页面可见性刷新 + 路由切换刷新）。
 * AppHeader 通过此接口获取未读数，不直接调用 api/clients。
 */
export function useNotificationBadge(options?: {
  /** 路由切换时自动刷新（传入 router.afterEach 的返回值由调用方负责，此选项供调用方传入钩子移除函数）。 */
  onRouteChange?: (handler: () => void) => (() => void) | undefined
}) {
  const auth = useAuthStore()
  const { forumUnreadCount } = useCurrentUser()
  const unreadCount = ref(0)
  const unreadBadgeValue = computed(() => formatBadgeCount(unreadCount.value, 99))

  let pollTimer: ReturnType<typeof setInterval> | undefined

  function refresh() {
    if (!auth.token) {
      unreadCount.value = 0
      return
    }
    void getForumUnreadNotificationCount().then((r) => {
      if (r.success && r.data !== undefined) unreadCount.value = r.data.count
      else unreadCount.value = 0
    })
  }

  function startPoll() {
    stopPoll()
    if (!auth.token) return
    pollTimer = window.setInterval(refresh, UNREAD_POLL_MS)
  }

  function stopPoll() {
    if (pollTimer !== undefined) {
      window.clearInterval(pollTimer)
      pollTimer = undefined
    }
  }

  function onVisibilityChange() {
    if (document.visibilityState === 'visible') refresh()
  }

  let removeRouteHook: (() => void) | undefined

  // /bff/me 返回后将实际未读数同步到徽标（一次性）
  const stopMeSeed = watch(forumUnreadCount, (count) => {
    unreadCount.value = count
    stopMeSeed()
  })

  onMounted(() => {
    // 初始未读数从 /bff/me 的结果（forumUnreadCount）读取，不发独立请求
    unreadCount.value = forumUnreadCount.value
    startPoll()
    window.addEventListener('jiss-forum-notifications-changed', refresh)
    document.addEventListener('visibilitychange', onVisibilityChange)
    removeRouteHook = options?.onRouteChange?.(refresh)
  })

  onUnmounted(() => {
    stopPoll()
    stopMeSeed()
    window.removeEventListener('jiss-forum-notifications-changed', refresh)
    document.removeEventListener('visibilitychange', onVisibilityChange)
    removeRouteHook?.()
    removeRouteHook = undefined
  })

  watch(
    () => auth.token,
    (token, prevToken) => {
      if (token && !prevToken) {
        // null→token：真正登录，立即刷新（me 尚未返回时的兜底）
        refresh()
        startPoll()
      } else if (!token) {
        unreadCount.value = 0
        stopPoll()
      }
      // token 轮换（非空→非空）不重复请求
    },
  )

  return {
    unreadCount,
    unreadBadgeValue,
    refresh,
  }
}
