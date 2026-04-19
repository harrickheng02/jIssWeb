import { computed, onUnmounted, ref, type MaybeRefOrGetter, toValue } from 'vue'
import { ElMessage } from 'element-plus'
import {
  type ForumPostEngagementSnapshot,
  type ForumPostListItem,
  type ForumPostListPatch,
  favoriteForumPost,
  likeForumPost,
  unfavoriteForumPost,
  unlikeForumPost,
} from '@/api/clients'

const DEFAULT_COOLDOWN_MS = 250

type EngagementCapture = Pick<ForumPostListItem, 'id' | 'likes' | 'likedByMe' | 'favoritedByMe' | 'favoriteCount'>

function captureEngagement(p: ForumPostListItem): EngagementCapture {
  return {
    id: p.id,
    likes: p.likes,
    likedByMe: p.likedByMe,
    favoritedByMe: p.favoritedByMe,
    favoriteCount: p.favoriteCount,
  }
}

function rollbackPatch(base: EngagementCapture): ForumPostListPatch {
  return {
    id: base.id,
    likes: base.likes,
    likedByMe: Boolean(base.likedByMe),
    favoritedByMe: Boolean(base.favoritedByMe),
    favoriteCount: base.favoriteCount ?? 0,
  }
}

function optimisticLikeToggle(p: ForumPostListItem): ForumPostListPatch {
  const liked = Boolean(p.likedByMe)
  return {
    id: p.id,
    likedByMe: !liked,
    likes: Math.max(0, p.likes + (liked ? -1 : 1)),
  }
}

function optimisticFavoriteToggle(p: ForumPostListItem): ForumPostListPatch {
  const fav = Boolean(p.favoritedByMe)
  const fc = p.favoriteCount ?? 0
  return {
    id: p.id,
    favoritedByMe: !fav,
    favoriteCount: Math.max(0, fc + (fav ? -1 : 1)),
  }
}

export function mapEngagementSnapshot(s: ForumPostEngagementSnapshot): ForumPostListPatch {
  return {
    id: s.postId,
    likes: s.likeCount,
    favoriteCount: s.favoriteCount,
    likedByMe: s.likedByMe,
    favoritedByMe: s.favoritedByMe,
  }
}

export interface UseForumPostEngagementOptions {
  cooldownMs?: number
  isAuthed: () => boolean
  onUnauthenticated?: () => void
}

/**
 * 乐观更新 + 本地快照回滚 + 异步对齐服务端。
 *
 * 与归档 `post-like-favorite-foundation/design.md` 中「请求进行中禁用按钮」的表述略有取舍：`busy*` 仅用于
 * **冷却期** 的 `:disabled`/`--busy`，避免 wait 光标盖住乐观 UI；**请求在飞** 时仍靠 `*OperationLocked()`
 * 在入口短路，防止重复提交。
 */
export function useForumPostEngagement(
  post: MaybeRefOrGetter<ForumPostListItem>,
  emitPatch: (patch: ForumPostListPatch) => void,
  options: UseForumPostEngagementOptions,
) {
  const cooldownMs = options.cooldownMs ?? DEFAULT_COOLDOWN_MS

  const likePending = ref(false)
  const likeCooldown = ref(false)
  const favPending = ref(false)
  const favCooldown = ref(false)

  let likeCooldownTimer: ReturnType<typeof setTimeout> | undefined
  let favCooldownTimer: ReturnType<typeof setTimeout> | undefined

  function armLikeCooldown() {
    likeCooldown.value = true
    if (likeCooldownTimer) clearTimeout(likeCooldownTimer)
    likeCooldownTimer = setTimeout(() => {
      likeCooldown.value = false
      likeCooldownTimer = undefined
    }, cooldownMs)
  }

  function armFavCooldown() {
    favCooldown.value = true
    if (favCooldownTimer) clearTimeout(favCooldownTimer)
    favCooldownTimer = setTimeout(() => {
      favCooldown.value = false
      favCooldownTimer = undefined
    }, cooldownMs)
  }

  onUnmounted(() => {
    if (likeCooldownTimer) clearTimeout(likeCooldownTimer)
    if (favCooldownTimer) clearTimeout(favCooldownTimer)
  })

  /** 网络进行中 + 冷却期：用于防重复提交（不用于按钮 loading 态）。 */
  function likeOperationLocked() {
    return likePending.value || likeCooldown.value
  }
  function favOperationLocked() {
    return favPending.value || favCooldown.value
  }

  /**
   * 仅冷却期为 true：请求进行中不置 disabled，避免 wait 光标与灰显盖住乐观 UI。
   */
  const busyLike = computed(() => likeCooldown.value)
  const busyFavorite = computed(() => favCooldown.value)

  async function toggleLike() {
    if (!options.isAuthed()) {
      options.onUnauthenticated?.()
      return
    }
    if (likeOperationLocked()) return

    const p = toValue(post)
    const snap = captureEngagement(p)

    emitPatch(optimisticLikeToggle(p))

    likePending.value = true
    try {
      const wasLiked = Boolean(snap.likedByMe)
      const res = wasLiked ? await unlikeForumPost(p.id) : await likeForumPost(p.id)
      if (res.success && res.data) emitPatch(mapEngagementSnapshot(res.data))
      else {
        emitPatch(rollbackPatch(snap))
        ElMessage.error(res.message ?? '操作失败')
      }
    } catch {
      emitPatch(rollbackPatch(snap))
      ElMessage.error('网络异常')
    } finally {
      likePending.value = false
      armLikeCooldown()
    }
  }

  async function toggleFavorite() {
    if (!options.isAuthed()) {
      options.onUnauthenticated?.()
      return
    }
    if (favOperationLocked()) return

    const p = toValue(post)
    const snap = captureEngagement(p)

    emitPatch(optimisticFavoriteToggle(p))

    favPending.value = true
    try {
      const wasFav = Boolean(snap.favoritedByMe)
      const res = wasFav ? await unfavoriteForumPost(p.id) : await favoriteForumPost(p.id)
      if (res.success && res.data) emitPatch(mapEngagementSnapshot(res.data))
      else {
        emitPatch(rollbackPatch(snap))
        ElMessage.error(res.message ?? '操作失败')
      }
    } catch {
      emitPatch(rollbackPatch(snap))
      ElMessage.error('网络异常')
    } finally {
      favPending.value = false
      armFavCooldown()
    }
  }

  return {
    busyLike,
    busyFavorite,
    toggleLike,
    toggleFavorite,
  }
}
