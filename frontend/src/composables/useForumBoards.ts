import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getForumBoards, type ForumBoardItem } from '@/api/clients'

export type ForumSidebarItem = { id: string; label: string }

export function useForumBoards() {
  const forumBoards = ref<ForumBoardItem[]>([])

  const sidebarItems = computed<ForumSidebarItem[]>(() => [
    { id: 'all', label: '全部' },
    ...forumBoards.value.map((b) => ({ id: b.id, label: b.title })),
  ])

  async function loadForumBoards() {
    try {
      const r = await getForumBoards()
      if (r.success && r.data?.length) {
        forumBoards.value = r.data
        return
      }
      ElMessage.warning(
        r.success ? '板块配置为空，侧栏仅显示全部' : (r.message ?? '板块配置加载失败，侧栏仅显示全部'),
      )
    } catch {
      ElMessage.warning('板块配置加载失败（网络异常），侧栏仅显示全部')
    }
  }

  return { forumBoards, sidebarItems, loadForumBoards }
}
