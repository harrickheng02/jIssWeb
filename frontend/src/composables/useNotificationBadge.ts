import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { getForumUnreadNotificationCount } from '@/api/clients'
import { formatBadgeCount } from '@/utils/formatBadgeCount'

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

  onMounted(() => {
    refresh()
    startPoll()
    window.addEventListener('jiss-forum-notifications-changed', refresh)
    document.addEventListener('visibilitychange', onVisibilityChange)
    removeRouteHook = options?.onRouteChange?.(refresh)
  })

  onUnmounted(() => {
    stopPoll()
    window.removeEventListener('jiss-forum-notifications-changed', refresh)
    document.removeEventListener('visibilitychange', onVisibilityChange)
    removeRouteHook?.()
    removeRouteHook = undefined
  })

  watch(
    () => auth.token,
    (token) => {
      refresh()
      if (token) startPoll()
      else stopPoll()
    },
  )

  return {
    unreadCount,
    unreadBadgeValue,
    refresh,
  }
}
