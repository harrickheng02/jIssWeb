import { ref } from 'vue'
import { listForumPosts, getForumAnnouncements, type ForumPostListItem, type ForumAnnouncementItem } from '@/api/clients'

/**
 * 首页右侧栏数据：热门帖子列表 + 公告列表。
 * HomeView 通过此 composable 访问侧栏数据，不直接调用 api/clients。
 */
export function useForumSidebar() {
  const hotSidebarPosts = ref<ForumPostListItem[]>([])
  const hotSidebarLoading = ref(false)
  const hotSidebarError = ref<string | null>(null)

  const announcements = ref<ForumAnnouncementItem[]>([])
  const annLoading = ref(false)
  const annError = ref<string | null>(null)

  async function loadHotSidebar(pageSize: number, boardId?: string) {
    hotSidebarLoading.value = true
    hotSidebarError.value = null
    try {
      const res = await listForumPosts(1, pageSize, boardId, undefined, undefined, 'hot')
      if (!res.success || !res.data) {
        hotSidebarError.value = res.message ?? '加载失败'
        hotSidebarPosts.value = []
        return
      }
      hotSidebarPosts.value = res.data.items
    } catch (e) {
      hotSidebarError.value = e instanceof Error ? e.message : '网络异常，请稍后重试'
      hotSidebarPosts.value = []
    } finally {
      hotSidebarLoading.value = false
    }
  }

  async function loadAnnouncements(limit = 5) {
    annLoading.value = true
    annError.value = null
    try {
      const res = await getForumAnnouncements(limit)
      if (!res.success || !res.data) {
        annError.value = res.message ?? '加载失败'
        announcements.value = []
        return
      }
      announcements.value = res.data
    } catch (e) {
      annError.value = e instanceof Error ? e.message : '网络异常，请稍后重试'
      announcements.value = []
    } finally {
      annLoading.value = false
    }
  }

  return {
    hotSidebarPosts,
    hotSidebarLoading,
    hotSidebarError,
    announcements,
    annLoading,
    annError,
    loadHotSidebar,
    loadAnnouncements,
  }
}
