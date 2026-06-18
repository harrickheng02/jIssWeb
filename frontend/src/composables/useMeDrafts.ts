import { ref } from 'vue'
import { getMyDrafts, deleteDraft, publishDraft, getForumPost, type ForumPostListItem, type ForumPostDetail } from '@/api/clients'

/**
 * 我的草稿列表及操作 composable。
 * MeDraftsView 通过此接口访问草稿 API，不直接调用 api/clients。
 */
export function useMeDrafts() {
  const loading = ref(false)
  const error = ref<string | null>(null)
  const items = ref<ForumPostListItem[]>([])
  const totalCount = ref(0)
  const totalPages = ref(1)
  const deletingId = ref<string | null>(null)
  const publishingId = ref<string | null>(null)
  const loadingEditId = ref<string | null>(null)

  async function fetchDrafts(page: number, pageSize: number) {
    loading.value = true
    error.value = null
    try {
      const res = await getMyDrafts(page, pageSize)
      if (!res.success || !res.data) {
        error.value = res.message ?? '加载失败'
        return
      }
      items.value = res.data.items
      totalCount.value = res.data.totalCount
      totalPages.value = Math.max(1, Math.ceil(res.data.totalCount / pageSize))
    } catch (e) {
      error.value = e instanceof Error ? e.message : '网络异常'
    } finally {
      loading.value = false
    }
  }

  async function fetchDraftDetail(draftId: string): Promise<{ success: true; data: ForumPostDetail } | { success: false; message: string }> {
    loadingEditId.value = draftId
    try {
      const res = await getForumPost(draftId)
      if (!res.success || !res.data) {
        return { success: false, message: res.message ?? '加载草稿失败' }
      }
      return { success: true, data: res.data }
    } catch (e) {
      return { success: false, message: e instanceof Error ? e.message : '加载草稿失败' }
    } finally {
      loadingEditId.value = null
    }
  }

  async function removeDraft(draftId: string) {
    deletingId.value = draftId
    try {
      const res = await deleteDraft(draftId)
      if (!res.success) {
        return { success: false as const, message: (res as { message?: string }).message ?? '删除失败' }
      }
      return { success: true as const }
    } catch (e) {
      return { success: false as const, message: e instanceof Error ? e.message : '删除失败' }
    } finally {
      deletingId.value = null
    }
  }

  async function submitPublish(draftId: string) {
    publishingId.value = draftId
    try {
      const res = await publishDraft(draftId)
      if (!res.success) {
        return { success: false as const, message: (res as { message?: string }).message ?? '发布失败' }
      }
      return { success: true as const }
    } catch (e) {
      return { success: false as const, message: e instanceof Error ? e.message : '发布失败' }
    } finally {
      publishingId.value = null
    }
  }

  return {
    loading,
    error,
    items,
    totalCount,
    totalPages,
    deletingId,
    publishingId,
    loadingEditId,
    fetchDrafts,
    fetchDraftDetail,
    removeDraft,
    submitPublish,
  }
}
