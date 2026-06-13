import { computed, onMounted, ref, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import type { ApiResult, ForumPostListItem, ForumPostListPatch, PagedForumPosts } from '@/api/clients'
import { applyForumPostListPatch } from '@/utils/applyForumPostListPatch'
import { mergeLocalPostsIntoMeList } from '@/utils/forumLocalContent'

const defaultPageSize = 20

export function useMeForumPostList(
  loadFn: (page: number, pageSize: number) => Promise<ApiResult<PagedForumPosts>>,
) {
  const auth = useAuthStore()
  const loading = ref(false)
  const error = ref('')
  const items = ref<ForumPostListItem[]>([])
  const page = ref(1)
  const totalCount = ref(0)

  const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / defaultPageSize)))

  async function load() {
    if (!auth.token) {
      items.value = []
      totalCount.value = 0
      return
    }
    loading.value = true
    error.value = ''
    try {
      const res = await loadFn(page.value, defaultPageSize)
      if (res.success && res.data) {
        items.value = auth.sub ? mergeLocalPostsIntoMeList(auth.sub, res.data.items) : res.data.items
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

  function applyPostListPatch(patch: ForumPostListPatch) {
    applyForumPostListPatch(items.value, patch)
  }

  function refresh() {
    void load()
  }

  return { loading, error, items, page, totalPages, load, pageSize: defaultPageSize, applyPostListPatch, refresh }
}
