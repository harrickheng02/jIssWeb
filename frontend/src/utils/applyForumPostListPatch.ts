import type { ForumPostListItem, ForumPostListPatch } from '@/api/clients'

/** 将点赞/收藏等局部变更合并进列表中的单行（原地修改）。 */
export function applyForumPostListPatch(items: ForumPostListItem[], patch: ForumPostListPatch): void {
  const row = items.find((x) => x.id === patch.id)
  if (!row) return
  if (patch.likes !== undefined) row.likes = patch.likes
  if (patch.favoriteCount !== undefined) row.favoriteCount = patch.favoriteCount
  if (patch.likedByMe !== undefined) row.likedByMe = patch.likedByMe
  if (patch.favoritedByMe !== undefined) row.favoritedByMe = patch.favoritedByMe
}
