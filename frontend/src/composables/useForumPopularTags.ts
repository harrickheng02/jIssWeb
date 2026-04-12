import { ref } from 'vue'
import { getForumPopularTags } from '@/api/clients'

export function useForumPopularTags(getBoardId: () => string | undefined) {
  const popularTags = ref<string[]>([])
  const popularTagsLoading = ref(false)
  const popularTagsError = ref<string | null>(null)

  async function loadPopularTags(limit = 24) {
    popularTagsLoading.value = true
    popularTagsError.value = null
    try {
      const res = await getForumPopularTags(getBoardId(), limit)
      if (!res.success || !res.data) {
        popularTags.value = []
        popularTagsError.value = res.message ?? '加载失败'
        return
      }
      popularTags.value = res.data
    } catch (e) {
      popularTags.value = []
      popularTagsError.value = e instanceof Error ? e.message : '网络异常，请稍后重试'
    } finally {
      popularTagsLoading.value = false
    }
  }

  return { popularTags, popularTagsLoading, popularTagsError, loadPopularTags }
}
