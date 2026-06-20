import { onMounted, onUnmounted, ref, watch, type Ref } from 'vue'
import { useRoute } from 'vue-router'
import { listMyForumPosts } from '@/api/clients'

export function useMyForumPostCount(tokenRef: Ref<string | null | undefined>) {
  const route = useRoute()
  const postCount = ref<number | null>(null)

  async function loadPostCount() {
    if (!tokenRef.value) {
      postCount.value = null
      return
    }
    try {
      const res = await listMyForumPosts(1, 1)
      if (res.success && res.data) postCount.value = res.data.totalCount
      else postCount.value = 0
    } catch {
      postCount.value = 0
    }
  }

  function onPostCreated() {
    void loadPostCount()
  }

  watch(
    () => tokenRef.value,
    (t, prevT) => {
      if (prevT && t) return  // token 轮换（非空→非空），不重新请求
      if (t) void loadPostCount()
      else postCount.value = null
    },
    { immediate: true },
  )

  watch(
    () => route.fullPath,
    () => {
      void loadPostCount()
    },
  )

  onMounted(() => {
    window.addEventListener('jiss-forum-post-created', onPostCreated)
  })

  onUnmounted(() => {
    window.removeEventListener('jiss-forum-post-created', onPostCreated)
  })

  return { postCount, loadPostCount }
}
