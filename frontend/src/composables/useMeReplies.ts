import { computed, onMounted, ref, watch } from 'vue'
import { listMyForumReplies, type ForumReply } from '@/api/clients'
import { useAuthStore } from '@/stores/auth'
import { mergeLocalRepliesIntoMeList } from '@/utils/forumLocalContent'

/**
 * 我的回复列表 composable。
 * MeRepliesView 通过此接口访问回复 API，不直接调用 api/clients。
 */
export function useMeReplies(pageSize = 20) {
  const auth = useAuthStore()
  const loading = ref(true)
  const error = ref('')
  const items = ref<ForumReply[]>([])
  const page = ref(1)
  const totalCount = ref(0)

  const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)))

  async function load() {
    if (!auth.token) {
      items.value = []
      totalCount.value = 0
      return
    }
    loading.value = true
    error.value = ''
    try {
      const res = await listMyForumReplies(page.value, pageSize)
      if (res.success && res.data) {
        items.value = auth.sub ? mergeLocalRepliesIntoMeList(auth.sub, res.data.items) : res.data.items
        totalCount.value = res.data.totalCount
      } else {
        error.value = res.message ?? '加载失败'
        items.value = []
        totalCount.value = 0
      }
    } catch {
      error.value = '加载失败'
      items.value = []
      totalCount.value = 0
    } finally {
      loading.value = false
    }
  }

  watch(
    () => auth.token,
    () => {
      page.value = 1
      void load()
    },
  )

  watch(page, () => void load())

  onMounted(() => void load())

  return {
    loading,
    error,
    items,
    page,
    totalPages,
    load,
  }
}
