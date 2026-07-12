import { computed, ref, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { getMe, type MeBundle } from '@/api/clients'

// 模块级单例：所有组件共享，token 变化只触发一次 getMe()
const meData = ref<MeBundle | null>(null)
let _watchRegistered = false
let _fetching = false

async function fetchMe() {
  const auth = useAuthStore()
  if (!auth.token) { meData.value = null; return }
  if (_fetching) return
  _fetching = true
  try {
    const res = await getMe()
    if (res.success && res.data) meData.value = res.data
    else meData.value = null
  } catch {
    meData.value = null
  } finally {
    _fetching = false
  }
}

function initOnce() {
  if (_watchRegistered) return
  _watchRegistered = true
  const auth = useAuthStore()
  if (auth.token) void fetchMe()
  watch(() => auth.token, (t, prevT) => {
    if (t && !prevT) void fetchMe()  // null→token：登录，拉取用户信息
    else if (!t) meData.value = null  // token→null：登出，清除
    // token 轮换（非空→非空）：仅刷新凭证，用户信息不变，不重复请求
  })
}

export function useCurrentUser() {
  initOnce()
  const auth = useAuthStore()
  return {
    userId: computed<string | null>(() => auth.sub),
    isLoggedIn: computed<boolean>(() => Boolean(auth.token)),
    canModerate: computed<boolean>(() => auth.canModerate),
    canAdmin: computed<boolean>(() => auth.canAdmin),
    forumRole: computed(() => auth.forumRole),
    nickname: computed(() => meData.value?.profile?.nickname ?? null),
    forumUnreadCount: computed(() => meData.value?.forum?.unreadCount ?? 0),
    fetchMe,
  }
}
