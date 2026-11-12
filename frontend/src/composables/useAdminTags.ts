import { ref } from 'vue'
import {
  adminListTags,
  adminCreateTag,
  adminPatchTag,
  adminDisableTag,
  adminEnableTag,
  adminDeleteTag,
  type ForumTagDto,
  type PagedForumTags,
} from '@/api/clients'

export interface AdminTagsListParams {
  page?: number
  pageSize?: number
  status?: string
  q?: string
}

/**
 * 管理端标签列表及操作的 composable。
 * AdminTagsView 通过此接口访问标签 API，不直接调用 api/clients。
 */
export function useAdminTags() {
  const loading = ref(false)
  const tags = ref<ForumTagDto[]>([])
  const totalCount = ref(0)

  async function loadTags(params: AdminTagsListParams) {
    loading.value = true
    try {
      const res = await adminListTags(params)
      if (res.success && res.data) {
        tags.value = (res.data as PagedForumTags).items
        totalCount.value = (res.data as PagedForumTags).totalCount
        return { success: true as const }
      }
      return { success: false as const, message: res.message ?? '加载失败' }
    } catch (e) {
      return { success: false as const, message: e instanceof Error ? e.message : '网络异常' }
    } finally {
      loading.value = false
    }
  }

  async function createTag(body: { name: string; description?: string }) {
    return adminCreateTag(body)
  }

  async function patchTag(id: string, body: { name?: string; description?: string }) {
    return adminPatchTag(id, body)
  }

  async function disableTag(id: string) {
    return adminDisableTag(id)
  }

  async function enableTag(id: string) {
    return adminEnableTag(id)
  }

  async function deleteTag(id: string) {
    return adminDeleteTag(id)
  }

  return {
    loading,
    tags,
    totalCount,
    loadTags,
    createTag,
    patchTag,
    disableTag,
    enableTag,
    deleteTag,
  }
}
