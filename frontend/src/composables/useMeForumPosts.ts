import { listMyForumPosts } from '@/api/clients'
import { useMeForumPostList } from '@/composables/useMeForumPostList'

/**
 * 我的帖子列表 composable。
 * 封装对 listMyForumPosts 的调用，View 层不直接依赖 api/clients。
 */
export function useMeForumPosts() {
  return useMeForumPostList(listMyForumPosts)
}
