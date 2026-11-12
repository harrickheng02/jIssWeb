import { listMyForumFavorites } from '@/api/clients'
import { useMeForumPostList } from '@/composables/useMeForumPostList'

/**
 * 我的收藏列表 composable。
 * 封装对 listMyForumFavorites 的调用，View 层不直接依赖 api/clients。
 */
export function useMeForumFavorites() {
  return useMeForumPostList(listMyForumFavorites)
}
